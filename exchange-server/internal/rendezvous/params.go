package rendezvous

import "time"

// Params are the protocol's numbers: working defaults until rung 2 freezes them (DEVICE-EXCHANGE
// 1.4 and item L).
type Params struct {
	KeepaliveDefault  int // seconds, used when a device proposes none
	KeepaliveMin      int
	KeepaliveMax      int
	WindowMultiplier  int           // missed intervals that take a device offline
	TunnelCap         int           // simultaneous tunnels per resource (item L)
	PendingWindow     time.Duration // a tunnel waits this long for its attach (item L)
	UnavailableDelay  time.Duration // fixed minimum before an `unavailable` answer, from the connect (item L)
	IdleTimeout       time.Duration // a bridge with no byte either way is closed (item L)
	PeekTimeout       time.Duration // for the first byte, outside TLS and inside it
	HandshakeTimeout  time.Duration
	FirstFrameTimeout time.Duration // for the first envelope, and again for the first one after `welcome`
	MaxConns          int
	SweepInterval     time.Duration
	// Directory caps (ticket 163, working numbers until rung 2).
	DevicesPerAccount    int
	ResourcesPerAccount  int
	BroadcastsPerAccount int
	BroadcastsPerDevice  int
	CodesPerAccount      int           // outstanding pairing codes (ticket 162 rule 6)
	CodeLifetime         time.Duration // a pairing code lives this long
	TombstoneLifetime    time.Duration // a revoked token's hash is kept this long (item I)
}

// DefaultParams returns the working defaults.
func DefaultParams() Params {
	return Params{
		KeepaliveDefault: 30, KeepaliveMin: 10, KeepaliveMax: 120, WindowMultiplier: 3,
		TunnelCap:         16,
		PendingWindow:     10 * time.Second,
		UnavailableDelay:  250 * time.Millisecond,
		IdleTimeout:       10 * time.Minute,
		PeekTimeout:       5 * time.Second,
		HandshakeTimeout:  15 * time.Second,
		FirstFrameTimeout: 10 * time.Second,
		MaxConns:          1000,
		SweepInterval:     time.Second,

		DevicesPerAccount:    64,
		ResourcesPerAccount:  256,
		BroadcastsPerAccount: 16,
		BroadcastsPerDevice:  4,
		CodesPerAccount:      5,
		CodeLifetime:         10 * time.Minute,
		TombstoneLifetime:    30 * 24 * time.Hour,
	}
}

// clampKeepalive turns a device's proposal into the interval the server accepts: absent or zero
// means the default, then the bounds apply (7.3).
func (p Params) clampKeepalive(seconds int) time.Duration {
	if seconds <= 0 {
		seconds = p.KeepaliveDefault
	}
	if seconds < p.KeepaliveMin {
		seconds = p.KeepaliveMin
	}
	if seconds > p.KeepaliveMax {
		seconds = p.KeepaliveMax
	}
	return time.Duration(seconds) * time.Second
}
