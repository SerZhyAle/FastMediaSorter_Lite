package deploy

import (
	"encoding/binary"
	"net"
	"os"
	"testing"
)

func TestServiceRunningForAServiceThatDoesNotExistIsFalseNotAnError(t *testing.T) {
	running, err := serviceRunning("FastMediaSorterNoSuchServiceForTests")
	if running || err != nil {
		t.Fatalf("got (%v, %v), want (false, nil)", running, err)
	}
}

func TestServiceRunningRealNameDoesNotFailOnThisMachine(t *testing.T) {
	// Whatever the machine has (normally no such service) the query-only access must answer.
	if _, err := ServiceRunning(); err != nil {
		t.Fatalf("the query-only SCM access failed: %v", err)
	}
	if held, err := (defaultProbe{}).HoldsPort(1); held || err != nil {
		t.Fatalf("no service process: nothing can hold a port: (%v, %v)", held, err)
	}
}

func TestListenerOwnedByFindsThisProcessListeningSocket(t *testing.T) {
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer ln.Close()
	port := ln.Addr().(*net.TCPAddr).Port
	if ok, err := listenerOwnedBy(uint32(os.Getpid()), port); err != nil || !ok {
		t.Fatalf("own listener not found: (%v, %v)", ok, err)
	}
	if ok, _ := listenerOwnedBy(uint32(os.Getpid())+4, port); ok {
		t.Fatal("another pid must not own it")
	}
	ln6, err := net.Listen("tcp6", "[::1]:0")
	if err == nil {
		defer ln6.Close()
		p6 := ln6.Addr().(*net.TCPAddr).Port
		if ok, err := listenerOwnedBy(uint32(os.Getpid()), p6); err != nil || !ok {
			t.Fatalf("own IPv6 listener not found: (%v, %v)", ok, err)
		}
	}
}

func TestRowsHoldReadsNetworkByteOrderPorts(t *testing.T) {
	// one IPv4 row; the local port is a DWORD whose first two bytes are the port in network order
	buf := make([]byte, 4+24)
	binary.LittleEndian.PutUint32(buf, 1)
	buf[4+8], buf[4+9] = 0x56, 0x06 // 22022 = 0x5606
	binary.LittleEndian.PutUint32(buf[4+20:], 1234)
	if !rowsHold(buf, 24, 8, 20, 1234, 22022) {
		t.Fatal("row not recognised")
	}
	if rowsHold(buf, 24, 8, 20, 1234, 22023) || rowsHold(buf, 24, 8, 20, 99, 22022) {
		t.Fatal("matched the wrong port or pid")
	}
	if rowsHold(buf[:10], 24, 8, 20, 1234, 22022) || rowsHold(nil, 24, 8, 20, 1, 1) {
		t.Fatal("a truncated table must not match")
	}
}
