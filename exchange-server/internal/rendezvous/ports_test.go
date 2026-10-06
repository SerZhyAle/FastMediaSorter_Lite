package rendezvous_test

import (
	"net"
	"strconv"
	"testing"
	"time"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

// Ticket 137: the per-session public TCP port. A raw client speaks SSH from its first byte -
// no envelope on its side - and is bridged like a connect.

func portStart(t *testing.T, size int) (*testkit.Env, int, int) {
	t.Helper()
	lo, hi := testkit.FreeRange(size)
	return testkit.StartServer(t, testkit.Options{PortRange: [2]int{lo, hi}}), lo, hi
}

func registerWithPort(t *testing.T, env *testkit.Env, port int) (*testkit.Producer, wire.Message) {
	t.Helper()
	p := env.NewProducer(sshPW)
	p.Port = wire.IntPtr(port)
	reply, err := p.Register()
	if err != nil {
		t.Fatal(err)
	}
	if reply.Type == wire.TypeRegistered {
		go p.Serve()
		t.Cleanup(p.Close)
	}
	return p, reply
}

func dialPort(port int) (net.Conn, error) {
	return net.DialTimeout("tcp", net.JoinHostPort("127.0.0.1", strconv.Itoa(port)), 2*time.Second)
}

func TestPortZeroGetsAListeningPortInRangeAndBridgesRawSSH(t *testing.T) {
	env, lo, hi := portStart(t, 10)
	_, reply := registerWithPort(t, env, 0)
	if reply.Type != wire.TypeRegistered || reply.Port == nil || *reply.Port < lo || *reply.Port > hi {
		t.Fatalf("port:0 must be answered with a port in %d-%d: %+v", lo, hi, reply)
	}
	c, err := dialPort(*reply.Port)
	if err != nil {
		t.Fatalf("the allocated port must really listen: %v", err)
	}
	out, err := testkit.SSHExec(c, "u", sshPW, "from-winscp")
	if err != nil || out != "echo:from-winscp" {
		t.Fatalf("raw SSH through the port: %q %v", out, err)
	}
}

func TestNoPortMemberMeansNoPort(t *testing.T) {
	env, _, _ := portStart(t, 5)
	p := env.NewProducer(sshPW)
	reply, _ := p.Register()
	if reply.Port != nil {
		t.Fatalf("a registration that asked for no port must not get one: %+v", reply)
	}
	if env.Pool.Leased() != 0 {
		t.Fatal("a port was leased unasked")
	}
}

func TestRequestedPortGrantedOrADifferentOne(t *testing.T) {
	env, lo, _ := portStart(t, 10)
	_, r1 := registerWithPort(t, env, lo+4)
	if r1.Port == nil || *r1.Port != lo+4 {
		t.Fatalf("a free requested port must be granted: %+v", r1)
	}
	// the same number again, while the first lease lives: a different port the producer shows
	_, r2 := registerWithPort(t, env, lo+4)
	if r2.Type != wire.TypeRegistered || r2.Port == nil || *r2.Port == lo+4 {
		t.Fatalf("a taken port must be answered with a different one: %+v", r2)
	}
	// a port another program holds is skipped the same way
	other, err := net.Listen("tcp", net.JoinHostPort("127.0.0.1", strconv.Itoa(lo+7)))
	if err != nil {
		t.Skip("cannot hold a port here")
	}
	defer other.Close()
	_, r3 := registerWithPort(t, env, lo+7)
	if r3.Type != wire.TypeRegistered || r3.Port == nil || *r3.Port == lo+7 {
		t.Fatalf("a port held elsewhere must not be handed out: %+v", r3)
	}
}

func TestExhaustedRangeRefusesWithPortUnavailableAndRegistersNothing(t *testing.T) {
	env, _, _ := portStart(t, 1)
	registerWithPort(t, env, 0) // takes the only port
	p, reply := registerWithPort(t, env, 0)
	if reply.Type != wire.TypeRefused || reply.Reason != wire.ReasonPortUnavailable {
		t.Fatalf("an exhausted range must be port-unavailable: %+v", reply)
	}
	if env.Server.Registrations() != 1 {
		t.Fatalf("a refused registration must not remain: %d", env.Server.Registrations())
	}
	_ = p
}

func TestNoPoolMeansPortUnavailable(t *testing.T) {
	env := testkit.StartServer(t, testkit.Options{})
	_, reply := registerWithPort(t, env, 0)
	if reply.Reason != wire.ReasonPortUnavailable {
		t.Fatalf("%+v", reply)
	}
}

func TestTwoRawClientsOnOnePortUpToTheTunnelCap(t *testing.T) {
	p := testkit.TestParams()
	p.TunnelCap = 2
	lo, hi := testkit.FreeRange(3)
	env := testkit.StartServer(t, testkit.Options{Params: &p, PortRange: [2]int{lo, hi}})
	prod := env.NewProducer(sshPW)
	prod.Port = wire.IntPtr(0)
	prod.Handler = holdHandler
	reply, _ := prod.Register()
	go prod.Serve()
	defer prod.Close()
	port := *reply.Port

	c1, _ := dialPort(port)
	c2, _ := dialPort(port)
	defer c1.Close()
	defer c2.Close()
	time.Sleep(150 * time.Millisecond) // both tunnels established
	c3, err := dialPort(port)
	if err != nil {
		t.Fatal(err)
	}
	defer c3.Close()
	// a raw client has no envelope to read: over the cap it is simply closed
	if !readsEOF(c3, 3*time.Second) {
		t.Fatal("a raw client over the tunnel cap must be closed")
	}
}

func TestPortLifetimeEndsWithTheRegistration(t *testing.T) {
	ck := newClock()
	lo, hi := testkit.FreeRange(3)
	env := testkit.StartServer(t, testkit.Options{PortRange: [2]int{lo, hi}, Now: ck.Now})

	// unregister releases the port
	p, reply := registerWithPort(t, env, 0)
	port := *reply.Port
	if c, err := dialPort(port); err != nil {
		t.Fatalf("not listening: %v", err)
	} else {
		c.Close()
	}
	_ = p.Control().Send(wire.New(wire.TypeUnregister))
	eventually(t, "the port to stop accepting after unregister", func() bool {
		c, err := dialPort(port)
		if err == nil {
			c.Close()
			return false
		}
		return true
	})
	if env.Pool.Leased() != 0 {
		t.Fatal("the lease outlived the registration")
	}

	// expiry releases it too
	q := env.NewProducer(sshPW)
	q.Port = wire.IntPtr(0)
	q.Keepalive = 10
	r2, _ := q.Register()
	port2 := *r2.Port
	ck.Add(time.Minute)
	eventually(t, "the port to stop accepting after expiry", func() bool {
		c, err := dialPort(port2)
		if err == nil {
			c.Close()
			return false
		}
		return true
	})
}

func TestResumeKeepsThePortLease(t *testing.T) {
	env, _, _ := portStart(t, 5)
	p, first := registerWithPort(t, env, 0)
	p.Close()
	time.Sleep(50 * time.Millisecond)
	second, err := p.Register() // resume with the token
	if err != nil || second.Type != wire.TypeRegistered {
		t.Fatalf("%v %+v", err, second)
	}
	if second.Port == nil || *second.Port != *first.Port {
		t.Fatalf("a resume must carry the lease over: %+v vs %+v", second, first)
	}
	if env.Pool.Leased() != 1 {
		t.Fatalf("leases after a resume: %d", env.Pool.Leased())
	}
}
