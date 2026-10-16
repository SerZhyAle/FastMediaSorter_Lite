package rendezvous

import (
	"errors"
	"io"
	"net"
	"time"

	"fms/exchange-server/internal/clipboard"
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/wire"
)

// This file is ticket 229: the clipboard slot of DEVICE-EXCHANGE section 22. A `clipboard-put` on
// a data stream fills the account's one slot; a `clipboard-get` on a data stream reads it;
// `clipboard-info` on the control stream asks what sits there. The value travels as raw bytes
// after the metadata envelope (item BW: no frames, no chunk headers), the slot is memory only, and
// a value replaces the previous one only when its last declared byte has arrived (item BX). The
// refusals are `not-permitted` (a guest announces nothing, both directions), `limit-reached` (the
// declared size against the account's `clipboardBytes`, which 0 turns off) and `capacity` (the
// account already has its one upload in flight; ticket 229 T-1, item BZ).

// clipChunk is how much of a put or a get the engine moves with one read or write.
const clipChunk = 32 << 10

// sizeClass names a size class for the log (22 item CC: the log may name the kind and the size;
// rule 10 of ticket 229 keeps it to a class, never the value or its name).
func sizeClass(n int64) string {
	switch {
	case n < 1<<10:
		return "under-1k"
	case n < 1<<20:
		return "under-1m"
	case n < 64<<20:
		return "under-64m"
	case n < 1<<30:
		return "under-1g"
	default:
		return "1g-or-more"
	}
}

// clipboardBody is the members a `clipboard-state`'s value and a `clipboard-value` share.
func clipboardBody(v *clipboard.Value) wire.ClipboardValueBody {
	return wire.ClipboardValueBody{
		ClipID: v.ClipID, Size: v.Size, SHA256: v.SHA256, StoredAt: v.StoredAt,
		SourceDeviceID: v.SourceDeviceID, SourceDeviceName: v.SourceDeviceName,
		Kind: v.Kind, MimeType: v.MimeType, Name: v.Name, Sealed: v.Sealed,
	}
}

// ClipboardPut serves a sender's data stream (22 item BW). It owns the stream.
func (e *Engine) ClipboardPut(st *Stream, sess *Session, m *wire.ClipboardPut) {
	defer st.Close()
	acct, dev := sess.Account, sess.DeviceID
	refused := func(r wire.Reason, why string) {
		e.cfg.Log.Info("clipboard-put-refused", logx.Device(dev), logx.Login(acct),
			logx.Text("reason", string(r)), logx.Text("why", why))
		_ = st.Send(wire.Refuse(r))
	}
	if e.cfg.Accounts.IsGuest(acct) {
		refused(wire.ReasonNotPermitted, "guest")
		return
	}
	// the limit is checked on the declared size, before a byte flows (item BZ); 0 is the clipboard
	// turned off for the account, and the value being replaced does not count against the new one
	limit := e.cfg.Accounts.LimitsFor(acct).ClipboardBytes
	if limit <= 0 || m.Size > limit {
		refused(wire.ReasonLimitReached, "size")
		return
	}
	sourceName := ""
	if d, ok := e.cfg.Accounts.Device(acct, dev); ok {
		sourceName = d.Name
	}
	p, err := e.clip.BeginPut(acct, clipboard.Decl{Kind: m.Kind, Size: m.Size, MimeType: m.MimeType, Name: m.Name, Sealed: m.Sealed}, dev, sourceName)
	if errors.Is(err, clipboard.ErrPutInFlight) {
		refused(wire.ReasonCapacity, "put-in-flight")
		return
	}
	if err != nil {
		refused(wire.ReasonCapacity, "store")
		return
	}
	e.cfg.Log.Info("clipboard-put-ready", logx.Device(dev), logx.Login(acct),
		logx.Text("kind", m.Kind), logx.Text("size", sizeClass(m.Size)))
	if err := st.Send(&wire.ClipboardReady{}); err != nil {
		p.Drop()
		return
	}

	// exactly Size raw bytes, in pieces of any size (item BW); the hash runs as they arrive
	conn := st.Conn()
	buf := make([]byte, clipChunk)
	got := int64(0)
	for got < m.Size {
		want := m.Size - got
		if want < int64(len(buf)) {
			buf = buf[:want]
		}
		_ = conn.SetReadDeadline(time.Now().Add(e.p.ClipboardIdle))
		n, rerr := conn.Read(buf)
		if n > 0 {
			p.Write(buf[:n])
			got += int64(n)
		}
		if rerr != nil {
			// closed before size bytes, or no byte for the idle window: what it carried is dropped
			// and the previous value stays (items BW and BX)
			p.Drop()
			e.cfg.Log.Info("clipboard-put-dropped", logx.Device(dev), logx.Login(acct),
				logx.Text("why", putDropWhy(rerr)), logx.Text("size", sizeClass(got)))
			return
		}
	}

	// The value replaced the slot at its last byte (item BX). One short look for bytes beyond the
	// declared size before the answer (item BW: a stream that carries more is dropped) - a sender
	// that closed its side is seen at once, one that keeps it open costs the probe window and
	// nothing more; a byte that lands after the window is past the completion of item BX.
	_ = conn.SetReadDeadline(time.Now().Add(e.p.ClipboardProbe))
	var probe [1]byte
	if n, _ := conn.Read(probe[:]); n > 0 {
		p.Drop()
		e.cfg.Log.Info("clipboard-put-dropped", logx.Device(dev), logx.Login(acct),
			logx.Text("why", "over-size"), logx.Text("size", sizeClass(m.Size+1)))
		return
	}
	_ = conn.SetReadDeadline(time.Time{})
	v := p.Complete()
	if v == nil {
		return // erased with the account while the upload ran; its stream is closing anyway
	}
	e.cfg.Log.Info("clipboard-stored", logx.Device(dev), logx.Login(acct),
		logx.Text("kind", v.Kind), logx.Text("size", sizeClass(v.Size)))
	_ = st.Send(&wire.ClipboardStored{ClipID: v.ClipID, SHA256: v.SHA256, StoredAt: v.StoredAt})
}

func putDropWhy(err error) string {
	var ne net.Error
	switch {
	case errors.Is(err, io.EOF):
		return "closed-early"
	case errors.As(err, &ne) && ne.Timeout():
		return "idle"
	}
	return "stream"
}

// ClipboardGet serves a reader's data stream (22 item BY). It owns the stream.
func (e *Engine) ClipboardGet(st *Stream, sess *Session, m *wire.ClipboardGet) {
	defer st.Close()
	acct, dev := sess.Account, sess.DeviceID
	if e.cfg.Accounts.IsGuest(acct) {
		e.cfg.Log.Info("clipboard-get-refused", logx.Device(dev), logx.Login(acct),
			logx.Text("reason", string(wire.ReasonNotPermitted)), logx.Text("why", "guest"))
		_ = st.Send(wire.Refuse(wire.ReasonNotPermitted))
		return
	}
	v := e.clip.Get(acct)
	if m.ClipID != "" {
		// a client that showed one value never receives another in its place (item BY)
		v = e.clip.GetByID(acct, m.ClipID)
		if v == nil {
			e.cfg.Log.Info("clipboard-get-refused", logx.Device(dev), logx.Login(acct),
				logx.Text("reason", string(wire.ReasonUnavailable)), logx.Text("why", "clip-id"))
			_ = st.Send(wire.Refuse(wire.ReasonUnavailable))
			return
		}
	}
	if v == nil {
		e.cfg.Log.Info("clipboard-empty", logx.Device(dev), logx.Login(acct))
		_ = st.Send(&wire.ClipboardEmpty{})
		return
	}
	e.cfg.Log.Info("clipboard-get", logx.Device(dev), logx.Login(acct),
		logx.Text("kind", v.Kind), logx.Text("size", sizeClass(v.Size)))
	if err := st.Send(&wire.ClipboardValue{ClipboardValueBody: clipboardBody(v)}); err != nil {
		return
	}
	// exactly size raw bytes, then the server closes the stream. The bytes are the value as it was
	// when the get began: a value replaced while this get runs is delivered whole (item BY).
	data := v.Bytes()
	for start := 0; start < len(data); start += clipChunk {
		end := start + clipChunk
		if end > len(data) {
			end = len(data)
		}
		if err := st.WriteRaw(data[start:end]); err != nil {
			e.cfg.Log.Info("clipboard-get-dropped", logx.Device(dev), logx.Login(acct), logx.Text("why", "stream"))
			return
		}
	}
}
