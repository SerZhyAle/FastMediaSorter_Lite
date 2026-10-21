package admin

import (
	"sync"
	"time"
)

// The ClientProduct of a session or a record, and the Product member of the file audit, name the PATH an
// administration came by, as the server itself saw it - never a product the client said. Nothing on the remote
// wire carries one: `admin-auth` holds only the password, `admin-request` no product member, and an operator
// is no enrolled device that could lend its record's product (a text a client sends is a hint, never proof,
// logx.AuditEvent). So the ring is ANONYMOUS about the product on the remote path - every remote operator is
// "remote", whichever program it is - and only the local channel (the Share Manager's pipe, the command line)
// is told apart, by "local". Both consumers take the words as they are: the Share Manager marks a session
// whose product is not "local" as an external administration.
const (
	ClientProductLocal  = "local"  // the local administration channel: the named pipe and the command line
	ClientProductRemote = "remote" // the remote administration stream (TLS or WebSocket), whatever the client is
)

// AdminAuditRecord is one administrative change or connection action in memory (spec 246 Group D, RECOMMENDATIONS-2026-10-19).
type AdminAuditRecord struct {
	Seq           int64  `json:"seq"`
	Timestamp     string `json:"timestamp"`
	RemoteAddr    string `json:"remoteAddr"`
	ClientProduct string `json:"clientProduct,omitempty"`
	Operator      string `json:"operator"`
	Op            string `json:"op"`
	Target        string `json:"target,omitempty"`
	Status        string `json:"status"`
}

// AdminSessionView is an active administrative connection in memory (spec 246 Group D).
type AdminSessionView struct {
	SessionID     string `json:"sessionId"`
	RemoteAddr    string `json:"remoteAddr"`
	ClientProduct string `json:"clientProduct,omitempty"`
	ConnectedAt   string `json:"connectedAt"`
	LastOp        string `json:"lastOp,omitempty"`
}

// AdminAuditView is what admin.audit.list returns.
type AdminAuditView struct {
	ActiveSessions []AdminSessionView `json:"activeSessions"`
	Records        []AdminAuditRecord `json:"records"`
}

// AuditTracker manages active admin sessions and the bounded ring buffer of recent audit records.
type AuditTracker struct {
	mu       sync.Mutex
	seq      int64
	sessions map[string]*AdminSessionView
	records  []AdminAuditRecord
	maxRec   int
}

// NewAuditTracker creates a new audit tracker with a 100-record capacity.
func NewAuditTracker() *AuditTracker {
	return &AuditTracker{
		sessions: make(map[string]*AdminSessionView),
		records:  make([]AdminAuditRecord, 0, 100),
		maxRec:   100,
	}
}

func (a *AuditTracker) AddSession(sessionID, remoteAddr, clientProduct, connectedAt string) {
	if a == nil {
		return
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	a.sessions[sessionID] = &AdminSessionView{
		SessionID:     sessionID,
		RemoteAddr:    remoteAddr,
		ClientProduct: clientProduct,
		ConnectedAt:   connectedAt,
	}
}

func (a *AuditTracker) UpdateSessionOp(sessionID, op string) {
	if a == nil {
		return
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	if s := a.sessions[sessionID]; s != nil {
		s.LastOp = op
	}
}

func (a *AuditTracker) RemoveSession(sessionID string) {
	if a == nil {
		return
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	delete(a.sessions, sessionID)
}

func (a *AuditTracker) Record(remoteAddr, clientProduct, operator, op, target, status string) {
	if a == nil {
		return
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	a.seq++
	rec := AdminAuditRecord{
		Seq:           a.seq,
		Timestamp:     time.Now().UTC().Format(time.RFC3339),
		RemoteAddr:    remoteAddr,
		ClientProduct: clientProduct,
		Operator:      operator,
		Op:            op,
		Target:        target,
		Status:        status,
	}
	if len(a.records) >= a.maxRec {
		copy(a.records, a.records[1:])
		a.records[len(a.records)-1] = rec
	} else {
		a.records = append(a.records, rec)
	}
}

func (a *AuditTracker) View() AdminAuditView {
	if a == nil {
		return AdminAuditView{
			ActiveSessions: []AdminSessionView{},
			Records:        []AdminAuditRecord{},
		}
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	sessList := make([]AdminSessionView, 0, len(a.sessions))
	for _, s := range a.sessions {
		sessList = append(sessList, *s)
	}
	recList := make([]AdminAuditRecord, len(a.records))
	copy(recList, a.records)
	return AdminAuditView{
		ActiveSessions: sessList,
		Records:        recList,
	}
}
