package deploy

import (
	"encoding/binary"
	"errors"
	"fmt"
	"unsafe"

	"golang.org/x/sys/windows"
)

// serviceStatus is what the query-only SCM access can tell about one service.
type serviceStatus struct {
	exists bool
	state  uint32
	pid    uint32 // the service's process, 0 while it is stopped
}

func (s serviceStatus) running() bool {
	return s.exists && (s.state == windows.SERVICE_RUNNING ||
		s.state == windows.SERVICE_START_PENDING ||
		s.state == windows.SERVICE_CONTINUE_PENDING)
}

// queryService asks the SCM about name with the MINIMAL access masks (SC_MANAGER_CONNECT +
// SERVICE_QUERY_STATUS) - deliberately not svc/mgr, whose Connect asks for SC_MANAGER_ALL_ACCESS,
// which a standard user does not get: the connect would fail and "no service" would be the answer on
// exactly the machine that has one. A service that does not exist is (exists=false, nil), not an
// error; a failure to ask at all is an error.
func queryService(name string) (serviceStatus, error) {
	scm, err := windows.OpenSCManager(nil, nil, windows.SC_MANAGER_CONNECT)
	if err != nil {
		return serviceStatus{}, fmt.Errorf("cannot open the service control manager: %w", err)
	}
	defer windows.CloseServiceHandle(scm)

	n, err := windows.UTF16PtrFromString(name)
	if err != nil {
		return serviceStatus{}, err
	}
	h, err := windows.OpenService(scm, n, windows.SERVICE_QUERY_STATUS)
	if err != nil {
		if errors.Is(err, windows.ERROR_SERVICE_DOES_NOT_EXIST) {
			return serviceStatus{}, nil
		}
		return serviceStatus{}, fmt.Errorf("cannot open the service %s: %w", name, err)
	}
	defer windows.CloseServiceHandle(h)

	var st windows.SERVICE_STATUS_PROCESS
	var needed uint32
	if err := windows.QueryServiceStatusEx(h, windows.SC_STATUS_PROCESS_INFO,
		(*byte)(unsafe.Pointer(&st)), uint32(unsafe.Sizeof(st)), &needed); err != nil {
		return serviceStatus{}, fmt.Errorf("cannot read the status of %s: %w", name, err)
	}
	return serviceStatus{exists: true, state: st.CurrentState, pid: st.ProcessId}, nil
}

// serviceRunning is ServiceRunning for any service name (the test asks about one that is not there).
func serviceRunning(name string) (bool, error) {
	st, err := queryService(name)
	if err != nil {
		return false, err
	}
	return st.running(), nil
}

// ServiceRunning reports whether the Media Exchange Server service is running or starting. A service that
// is not registered is (false, nil).
func ServiceRunning() (bool, error) { return serviceRunning(ServiceName) }

// defaultProbe is the real SCM probe.
type defaultProbe struct{}

func (defaultProbe) Running() (bool, error) { return ServiceRunning() }

func (defaultProbe) HoldsPort(port int) (bool, error) {
	st, err := queryService(ServiceName)
	if err != nil || st.pid == 0 {
		return false, err
	}
	return listenerOwnedBy(st.pid, port)
}

var (
	procGetExtendedTCPTable = windows.NewLazySystemDLL("iphlpapi.dll").NewProc("GetExtendedTcpTable")
)

const (
	afInet6                 = 23
	tcpTableOwnerPIDListen  = 3 // TCP_TABLE_OWNER_PID_LISTENER
	errorInsufficientBuffer = 122
)

// listenerOwnedBy reports whether the process pid has a listening TCP socket on port (any local
// address, IPv4 or IPv6).
func listenerOwnedBy(pid uint32, port int) (bool, error) {
	for _, t := range []struct {
		family                   uint32
		rowSize, portOff, pidOff int
	}{
		{windows.AF_INET, 24, 8, 20}, // MIB_TCPROW_OWNER_PID
		{afInet6, 56, 20, 52},        // MIB_TCP6ROW_OWNER_PID
	} {
		buf, err := tcpListenerTable(t.family)
		if err != nil {
			return false, err
		}
		if rowsHold(buf, t.rowSize, t.portOff, t.pidOff, pid, port) {
			return true, nil
		}
	}
	return false, nil
}

func tcpListenerTable(family uint32) ([]byte, error) {
	size := uint32(16 * 1024)
	for attempt := 0; attempt < 8; attempt++ {
		buf := make([]byte, size)
		r, _, _ := procGetExtendedTCPTable.Call(uintptr(unsafe.Pointer(&buf[0])), uintptr(unsafe.Pointer(&size)),
			0, uintptr(family), tcpTableOwnerPIDListen, 0)
		switch r {
		case 0:
			return buf[:size], nil
		case errorInsufficientBuffer: // size now holds the need; the table may grow between calls
			size += 4096
		default:
			return nil, fmt.Errorf("GetExtendedTcpTable failed: %w", windows.Errno(r))
		}
	}
	return nil, errors.New("the TCP table kept growing")
}

// rowsHold scans a MIB_TCPTABLE_OWNER_PID-shaped buffer: a DWORD row count, then fixed-size rows
// whose local port is a DWORD in network byte order.
func rowsHold(buf []byte, rowSize, portOff, pidOff int, pid uint32, port int) bool {
	if len(buf) < 4 {
		return false
	}
	n := int(binary.LittleEndian.Uint32(buf))
	for i := 0; i < n; i++ {
		row := 4 + i*rowSize
		if row+rowSize > len(buf) {
			return false
		}
		p := binary.LittleEndian.Uint32(buf[row+portOff:])
		localPort := int(p&0xff)<<8 | int(p>>8&0xff)
		if localPort == port && binary.LittleEndian.Uint32(buf[row+pidOff:]) == pid {
			return true
		}
	}
	return false
}
