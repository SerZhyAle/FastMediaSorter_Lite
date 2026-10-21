package rendezvous

import (
	"encoding/json"
	"net"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/connstr"
	"fms/exchange-server/internal/limits"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/password"
	"fms/exchange-server/internal/wire"
)

// carriedOps is the operations permitted over the remote administration wire: exactly the eighteen of
// WEB-BINDING operator-administration "Which operations" (REMOTE-ADMIN.md). Anything else - host.settings,
// grant.add and grant.remove until they are audited against item AZ, and the visibility operations of the
// local channel (broadcast.list and .end, resource.list and .unpublish, clipboard.info and .erase,
// admin.audit.list), which the accepted contract does not carry - is answered `bad-request` here and stays
// on the local administration channel (internal/admin, the Share Manager's pipe).
var carriedOps = map[string]bool{
	admin.OpStatus:                 true,
	admin.OpAccountList:            true,
	admin.OpAccountAdd:             true,
	admin.OpAccountPasswd:          true,
	admin.OpAccountKind:            true,
	admin.OpAccountRemove:          true,
	admin.OpAccountLimits:          true,
	admin.OpDeviceList:             true,
	admin.OpDeviceRevoke:           true,
	admin.OpDevicePairingCode:      true,
	admin.OpInvitationCreate:       true,
	admin.OpInvitationList:         true,
	admin.OpInvitationWithdraw:     true,
	admin.OpSharedInvitationStatus: true,
	admin.OpSharedInvitationSet:    true,
	admin.OpPolicyStatus:           true,
	admin.OpPolicySet:              true,
	admin.OpGrantList:              true,
}

func (s *Server) adminPacer() *accounts.Pacer {
	if s.cfg.AdminPacer != nil {
		return s.cfg.AdminPacer
	}
	if p, ok := s.cfg.Backend.(interface{ Pacer() *accounts.Pacer }); ok {
		return p.Pacer()
	}
	return nil
}

func (s *Server) adminVerifier() *limits.Verifier {
	if s.cfg.Verifier != nil {
		return s.cfg.Verifier
	}
	if v, ok := s.cfg.Backend.(interface{ Verifier() *limits.Verifier }); ok {
		return v.Verifier()
	}
	return nil
}

func (s *Server) adminOps() *admin.Ops {
	if s.cfg.Ops != nil {
		return s.cfg.Ops
	}
	if o, ok := s.cfg.Backend.(interface{ Ops() *admin.Ops }); ok {
		return o.Ops()
	}
	return nil
}

// SetAdminConfig updates the admin configuration dynamically.
func (s *Server) SetAdminConfig(cfg config.Admin) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.cfg.AdminConfig = cfg
}

// adminPace takes a turn for one administration password check of the client at addr: at most one a
// second per client address (WEB-BINDING operator-administration "The pace, per client address"). The key
// is the one every other limiter uses (limits.Key: the address, an IPv6 client by its /64), so a client
// that rotates addresses inside its own /64 is still one client. False means rate-limited, not evaluated.
func (s *Server) adminPace(addr net.Addr) bool {
	pacer := s.adminPacer()
	if pacer == nil {
		return true
	}
	return pacer.WaitAddr(limits.Key(addr))
}

// serveAdminStream handles an administration stream (DEVICE-EXCHANGE 1.4, REMOTE-ADMIN.md).
//
// operatorOrigin says the stream may be an operator's: no Origin (a native client) or the server's own
// origin (the page it serves). An origin that is only in the operator's `origins` list is a remote
// consumer, never an operator (WEB-BINDING normative resolution 2: "an allowlisted remote consumer origin
// is not operator permission"): that stream is closed without any answer, so the page cannot tell whether
// administration is on, off or exists at all.
func (s *Server) serveAdminStream(st *Stream, auth *wire.AdminAuth, leave func(), operatorOrigin bool) {
	defer st.Close()
	status := logx.AuditRefusal("closed")
	authenticated := false
	refuse := func(reason wire.Reason) { status = logx.AuditRefusal(string(reason)); st.Refuse(reason) }
	defer func() {
		ev := logx.AuditEvent{Op: "admin.auth", Product: admin.ClientProductRemote, Remote: st.raw.RemoteAddr().String(), Transport: st.Transport(), Status: status}
		if authenticated {
			ev.Actor = "OPERATOR"
			ev.Op = "admin.disconnect"
		}
		s.recordAudit(ev)
	}()
	s.mu.Lock()
	adminCfg := s.cfg.AdminConfig
	s.mu.Unlock()

	// A listed origin that is not the server's own is no operator: closed, no answer, nothing evaluated.
	if !operatorOrigin {
		status = logx.AuditRefusal("origin-not-same")
		st.Close()
		return
	}

	// Rule 11: Allowed addresses check. Excluded address gets closed stream without answer.
	if !adminCfg.Allows(st.raw.RemoteAddr().String()) {
		status = logx.AuditRefusal("address-denied")
		st.Close()
		return
	}

	// Rule 1: Off by default
	if !adminCfg.On || adminCfg.PasswordRecord == "" {
		refuse(wire.ReasonAdminDisabled)
		return
	}

	// Rule 3: Pace per client address (the limiter's key: an IPv6 client by its /64)
	if !s.adminPace(st.raw.RemoteAddr()) {
		refuse(wire.ReasonRateLimited)
		return
	}

	// Rule 4: Verifier with bounded concurrency
	verifier := s.adminVerifier()
	verifyFunc := func() bool {
		return password.Verify(adminCfg.PasswordRecord, auth.Password)
	}

	var verified bool
	if verifier != nil {
		v, err := verifier.Run(verifyFunc)
		if err != nil {
			refuse(wire.ReasonCapacity)
			return
		}
		verified = v
	} else {
		verified = verifyFunc()
	}

	if !verified {
		refuse(wire.ReasonBadCredentials)
		return
	}

	// Rule 6: Session and Token
	token := wire.NewToken()
	lifetimeMins := adminCfg.SessionLifetimeMinutes
	if lifetimeMins <= 0 {
		lifetimeMins = 15
	}
	lifetime := time.Duration(lifetimeMins) * time.Minute
	expiresSeconds := lifetimeMins * 60
	expiresAt := s.now().Add(lifetime)

	if err := st.Send(&wire.AdminAuthOK{
		Token:          token,
		ExpiresSeconds: expiresSeconds,
	}); err != nil {
		return
	}
	authenticated = true
	status = logx.AuditStatusOK
	s.recordAudit(logx.AuditEvent{Op: "admin.auth", Actor: "OPERATOR", Product: admin.ClientProductRemote, Remote: st.raw.RemoteAddr().String(), Transport: st.Transport(), Status: status})
	st.markAuthenticated()
	leave() // authenticated: release pre-auth slot

	s.cfg.Log.State("admin-authenticated", logx.Addr(st.Addr()))
	defer s.cfg.Log.State("admin-disconnected", logx.Addr(st.Addr()))

	sessionID := "sess-" + token[:12]
	ops := s.adminOps()
	if ops != nil && ops.Audit != nil {
		ops.Audit.AddSession(sessionID, st.Addr(), admin.ClientProductRemote, s.now().UTC().Format(time.RFC3339))
		defer ops.Audit.RemoveSession(sessionID)
	}

	for {
		// Bound the read itself: an idle client must release its stream without sending another frame.
		st.SetReadDeadline(time.Now().Add(lifetime))
		env, err := st.Recv()
		if err != nil {
			if !s.now().Before(expiresAt) {
				s.recordAudit(logx.AuditEvent{Op: "admin.expired", Actor: "OPERATOR", Product: admin.ClientProductRemote, Remote: st.raw.RemoteAddr().String(), Transport: st.Transport(), Status: logx.AuditRefusal("expired")})
			}
			return
		}
		if !s.now().Before(expiresAt) {
			s.recordAudit(logx.AuditEvent{Op: "admin.expired", Actor: "OPERATOR", Product: admin.ClientProductRemote, Remote: st.raw.RemoteAddr().String(), Transport: st.Transport(), Status: logx.AuditRefusal("expired")})
			refuse(wire.ReasonBadCredentials)
			return
		}

		switch e := env.(type) {
		case *wire.Bye:
			return
		case *wire.AdminRequest:
			if e.Token != token {
				refuse(wire.ReasonBadCredentials)
				return
			}
			expiresAt = s.now().Add(lifetime)
			if ops != nil && ops.Audit != nil {
				ops.Audit.UpdateSessionOp(sessionID, e.Op)
			}
			s.handleCarriedAdminOp(st, e)
		case *wire.Keepalive:
			// An administration stream needs none (REMOTE-ADMIN native-administration-stream) and answers none;
			// a client that sends one is not wrong enough to lose its session.
		default:
			// Recv hands out only known envelopes, so this is one that is invalid in this state - a second
			// stream kind (hello, enroll, register, connect, admin-auth again) on a stream whose kind the first
			// envelope fixed. 7.2: an envelope invalid in the current state closes the stream.
			status = logx.AuditRefusal("invalid-envelope")
			return
		}
	}
}

func (s *Server) handleCarriedAdminOp(st *Stream, req *wire.AdminRequest) {
	if !carriedOps[req.Op] {
		reply := &wire.AdminReply{
			ID:    req.ID,
			OK:    false,
			Code:  admin.CodeBadRequest,
			Error: "uncarried or unknown operation",
		}
		_ = st.Send(reply)
		return
	}

	ops := s.adminOps()
	if ops == nil {
		reply := &wire.AdminReply{
			ID:    req.ID,
			OK:    false,
			Code:  admin.CodeInternal,
			Error: "admin operations unavailable",
		}
		_ = st.Send(reply)
		return
	}

	areq := admin.Request{
		Op:            req.Op,
		Login:         req.Login,
		Password:      req.Password,
		Kind:          req.Kind,
		ExpectedLogin: req.ExpectedLogin,
		MaxAccounts:   req.MaxAccounts,
		CodeHash:      req.CodeHash,
		TargetID:      req.TargetID,
		GuestAccount:  req.GuestAccount,
		DeviceID:      req.DeviceID,
		RemoteAddr:    st.Addr(),
		ClientProduct: admin.ClientProductRemote,
	}
	if req.Limits != nil {
		areq.Limits = &accounts.AccountLimits{
			DevicesPerAccount:    req.Limits.DevicesPerAccount,
			ResourcesPerAccount:  req.Limits.ResourcesPerAccount,
			BroadcastsPerAccount: req.Limits.BroadcastsPerAccount,
			BroadcastsPerDevice:  req.Limits.BroadcastsPerDevice,
			ClipboardMaxBytes:    req.Limits.ClipboardMaxBytes,
		}
	}

	resp := ops.HandleAudited(areq, logx.AuditEvent{Product: admin.ClientProductRemote, Remote: st.raw.RemoteAddr().String(), Transport: st.Transport()})
	if ops.Audit != nil {
		target := req.Login
		if target == "" {
			target = req.TargetID
		}
		stResult := "OK"
		if !resp.OK {
			stResult = "Failed: " + resp.Code
		}
		switch req.Op {
		case admin.OpAccountAdd, admin.OpAccountPasswd, admin.OpAccountKind, admin.OpAccountRemove, admin.OpAccountLimits,
			admin.OpDeviceRevoke, admin.OpDevicePairingCode, admin.OpInvitationCreate, admin.OpInvitationWithdraw,
			admin.OpSharedInvitationSet, admin.OpPolicySet:
			// the changing operations among the carried ones; grant.add, grant.remove, broadcast.end,
			// resource.unpublish and clipboard.erase are not carried (carriedOps), so they never reach here
			ops.Audit.Record(st.Addr(), admin.ClientProductRemote, "operator", req.Op, target, stResult)
		}
	}
	// Only operation names from carriedOps and hashed targets reach the log. Never the request,
	// password, token, invitation reply, resource access or clipboard payload.
	s.cfg.Log.Info("admin-operation", logx.Addr(st.Addr()), logx.Text("operation", req.Op),
		logx.Login(req.Login), logx.Text("result", adminResult(resp.OK)))
	if req.Op == admin.OpInvitationCreate && resp.OK {
		var created admin.CreateInvitationResult
		if err := json.Unmarshal(resp.Data, &created); err == nil {
			connStr := s.invitationConnectionString(req.ExpectedLogin, created.Code)
			out := struct {
				admin.CreateInvitationResult
				ConnectionString string `json:"connectionString"`
			}{created, connStr}
			if b, err := json.Marshal(out); err == nil {
				resp.Data = b
			}
		}
	}

	reply := &wire.AdminReply{
		ID:    req.ID,
		OK:    resp.OK,
		Data:  resp.Data,
		Code:  resp.Code,
		Error: resp.Error,
	}

	frame, err := wire.EncodeFrame(reply, wire.MaxFrameAuth)
	if err != nil {
		reply = &wire.AdminReply{
			ID:    req.ID,
			OK:    false,
			Code:  admin.CodeCapacity,
			Error: "reply exceeds frame limit",
		}
		_ = st.Send(reply)
		return
	}
	_ = st.WriteRaw(frame)
}

func (s *Server) invitationConnectionString(expectedLogin, code string) string {
	if s.cfg.PublicEndpoint == "" || code == "" {
		return ""
	}
	c, err := connstr.NewWithInvitation(s.cfg.PublicEndpoint, s.cfg.LeafFingerprint, "", expectedLogin, code)
	if err != nil {
		return ""
	}
	str, err := c.String()
	if err != nil {
		return ""
	}
	return str
}

// recordAudit uses the local operational log, never the hashed security log or remote audit view.
func (s *Server) recordAudit(ev logx.AuditEvent) {
	if ops := s.adminOps(); ops != nil {
		ops.AuditLog.Record(ev)
	}
}
