package admin

import (
	"encoding/json"
	"errors"
	"io"
	"net"
	"sync"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/password"
)

// The local admin channel (ticket 162 section 4). One JSON request per connection, answered
// {ok, error, data}; the request is bounded and the whole exchange has a deadline; no secret is ever
// returned. A password travels on this channel only, never on a command line. The transport - a named
// pipe with a current-user ACL on Windows, a mode-0600 Unix socket elsewhere - is internal/deploy's;
// this file speaks over any net.Conn.

// Bounds of the channel.
const (
	MaxRequest    = 4 << 10 // bytes of one request
	MaxResponse   = 1 << 20
	maxConcurrent = 4
)

// ExchangeLimit bounds the whole exchange of one connection (a variable only so a test can shorten it).
var ExchangeLimit = 10 * time.Second

// Operations.
const (
	OpAccountAdd    = "account.add"
	OpAccountPasswd = "account.passwd"
	OpAccountRemove = "account.remove"
	OpAccountList   = "account.list"
	OpDeviceList    = "device.list"
	OpDeviceRevoke  = "device.revoke"
	OpStatus        = "status"
)

// Request is one request. It holds a password, so it prints as its operation only.
type Request struct {
	Op       string `json:"op"`
	Login    string `json:"login,omitempty"`
	Password string `json:"password,omitempty"`
	DeviceID string `json:"deviceId,omitempty"`
	SignOut  bool   `json:"signOut,omitempty"`
}

// String never prints a member.
func (r Request) String() string { return "admin.Request{" + r.Op + "}" }

// GoString never prints a member.
func (r Request) GoString() string { return r.String() }

// Response is the answer: ok, or an error with a stable code and a sentence, and the data.
type Response struct {
	OK    bool            `json:"ok"`
	Error string          `json:"error,omitempty"`
	Code  string          `json:"code,omitempty"`
	Data  json.RawMessage `json:"data,omitempty"`
}

// Codes of a failed response.
const (
	CodeBadRequest = "bad-request"
	CodeNoAccount  = "no-account"
	CodeExists     = "exists"
	CodeBadLogin   = "bad-login"
	CodeWeak       = "weak-password"
	CodeNotFound   = "not-found"
	CodeInternal   = "internal"
)

// Error is a failed response as a Go error.
type Error struct{ Code, Message string }

func (e *Error) Error() string { return e.Message }

// Err returns the failure of a response, or nil.
func (r Response) Err() error {
	if r.OK {
		return nil
	}
	return &Error{Code: r.Code, Message: r.Error}
}

func fail(code, msg string) Response { return Response{Code: code, Error: msg} }

func okData(v any) Response {
	if v == nil {
		return Response{OK: true}
	}
	b, err := json.Marshal(v)
	if err != nil {
		return fail(CodeInternal, "cannot encode the answer")
	}
	return Response{OK: true, Data: b}
}

// failFrom turns an operation's error into a response. The text of a refused password is the policy's
// own sentence, which names no secret.
func failFrom(err error) Response {
	switch {
	case errors.Is(err, accounts.ErrNoAccount):
		return fail(CodeNoAccount, "no such account")
	case errors.Is(err, accounts.ErrExists):
		return fail(CodeExists, "the account exists")
	case errors.Is(err, accounts.ErrBadLogin):
		return fail(CodeBadLogin, err.Error())
	case errors.Is(err, password.ErrTooShort), errors.Is(err, password.ErrNoUpper),
		errors.Is(err, password.ErrNoLower), errors.Is(err, password.ErrNoDigit):
		return fail(CodeWeak, err.Error())
	}
	return fail(CodeInternal, "the operation failed; see the server log")
}

// Handle runs one request. It never returns a secret.
func (o *Ops) Handle(req Request) Response {
	switch req.Op {
	case OpAccountAdd:
		login, err := o.AddAccount(req.Login, req.Password)
		if err != nil {
			return failFrom(err)
		}
		return okData(map[string]string{"login": login})
	case OpAccountPasswd:
		if err := o.SetPassword(req.Login, req.Password, req.SignOut); err != nil {
			return failFrom(err)
		}
		return okData(nil)
	case OpAccountRemove:
		if err := o.RemoveAccount(req.Login); err != nil {
			return failFrom(err)
		}
		return okData(nil)
	case OpAccountList:
		return okData(o.ListAccounts())
	case OpDeviceList:
		devs, err := o.ListDevices(req.Login)
		if err != nil {
			return failFrom(err)
		}
		return okData(devs)
	case OpDeviceRevoke:
		found, err := o.RevokeDevice(req.Login, req.DeviceID)
		if err != nil {
			return failFrom(err)
		}
		if !found {
			return fail(CodeNotFound, "no such device in that account")
		}
		return okData(nil)
	case OpStatus:
		return okData(o.Status())
	}
	return fail(CodeBadRequest, "unknown operation")
}

// Serve answers connections from l until it is closed. At most maxConcurrent run at once; the rest
// are closed unanswered. Every connection has a deadline, a bounded request and exactly one answer.
func Serve(l net.Listener, o *Ops, log *logx.Logger) {
	sem := make(chan struct{}, maxConcurrent)
	var wg sync.WaitGroup
	defer wg.Wait()
	for {
		c, err := l.Accept()
		if err != nil {
			return
		}
		select {
		case sem <- struct{}{}:
		default:
			_ = c.Close()
			continue
		}
		wg.Add(1)
		go func() {
			defer wg.Done()
			defer func() { <-sem }()
			serveOne(c, o, log)
		}()
	}
}

func serveOne(c net.Conn, o *Ops, log *logx.Logger) {
	defer c.Close()
	_ = c.SetDeadline(time.Now().Add(ExchangeLimit))
	var req Request
	dec := json.NewDecoder(io.LimitReader(c, MaxRequest))
	var resp Response
	if err := dec.Decode(&req); err != nil {
		// the exchange deadline may be what ended the read: the refusal still gets a moment to be written
		_ = c.SetDeadline(time.Now().Add(2 * time.Second))
		resp = fail(CodeBadRequest, "the request is not one JSON object within the bound")
	} else {
		log.Info("admin-request", logx.Text("op", req.Op))
		resp = o.Handle(req)
	}
	b, _ := json.Marshal(resp)
	_, _ = c.Write(append(b, '\n'))
}

// Do sends one request on conn and reads the answer. It closes conn.
func Do(conn net.Conn, req Request) (Response, error) {
	defer conn.Close()
	_ = conn.SetDeadline(time.Now().Add(ExchangeLimit))
	b, err := json.Marshal(req)
	if err != nil {
		return Response{}, err
	}
	if len(b) > MaxRequest {
		return Response{}, errors.New("the request is too large")
	}
	if _, err := conn.Write(b); err != nil {
		return Response{}, err
	}
	var resp Response
	if err := json.NewDecoder(io.LimitReader(conn, MaxResponse)).Decode(&resp); err != nil {
		return Response{}, err
	}
	return resp, nil
}
