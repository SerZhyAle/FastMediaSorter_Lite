// exchange-fakeproducer is a MANUAL-PROOF tool (ticket 137 T-4), not part of any package: it
// registers a share with a running exchange server, and for every tunnel the server opens it runs a
// real SSH server with an SFTP subsystem over the attach stream. Point WinSCP or FileZilla at the
// per-session port it prints and a folder listing comes through the bridge.
//
// It serves a real directory with a fixed login. Use a scratch folder.
package main

import (
	"flag"
	"fmt"
	"net"
	"os"
	"time"

	"github.com/pkg/sftp"
	"golang.org/x/crypto/ssh"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

func main() {
	server := flag.String("server", "127.0.0.1:22022", "exchange server host:port")
	pin := flag.String("fingerprint", "", "the server's TLS fingerprint (SHA256:..); empty trusts the first certificate seen")
	serverPassword := flag.String("server-password", "", "the exchange server's password")
	port := flag.Int("port", 0, "third-party port to request (0 = any)")
	user := flag.String("user", "tester", "SFTP user name")
	pass := flag.String("pass", "tester-pass", "SFTP password")
	root := flag.String("root", ".", "the directory to serve (use a scratch folder)")
	flag.Parse()
	if *serverPassword == "" {
		fmt.Fprintln(os.Stderr, "need --server-password")
		os.Exit(2)
	}

	cfg := testkit.SSHServerConfig("")
	cfg.PasswordCallback = func(c ssh.ConnMetadata, got []byte) (*ssh.Permissions, error) {
		if c.User() == *user && string(got) == *pass {
			return nil, nil
		}
		return nil, fmt.Errorf("denied")
	}

	p := &testkit.Producer{
		Addr: *server, Pin: *pin, Password: *serverPassword, ShareID: wire.NewID(), Keepalive: 30,
		Port:    port,
		Handler: func(c net.Conn) { serveSFTP(c, cfg, *root) },
	}
	reply, err := p.Register()
	if err != nil {
		fmt.Fprintln(os.Stderr, "register:", err)
		os.Exit(1)
	}
	if reply.Type != wire.TypeRegistered {
		fmt.Fprintf(os.Stderr, "refused: %s\n", reply.Reason)
		os.Exit(1)
	}
	fmt.Printf("registered; keepalive %ds\n", reply.KeepaliveSeconds)
	fmt.Printf("share id %s  (a phone-style consumer connects with this)\n", p.ShareID)
	if reply.Port != nil {
		fmt.Printf("third-party port %d -> point WinSCP/FileZilla at <server host>:%d, SFTP, user %q, password %q\n", *reply.Port, *reply.Port, *user, *pass)
	}
	go func() {
		for range time.Tick(15 * time.Second) {
			if err := p.SendKeepalive(); err != nil {
				return
			}
		}
	}()
	p.Serve() // returns when the control stream closes
	fmt.Println("control stream closed")
}

func serveSFTP(conn net.Conn, cfg *ssh.ServerConfig, root string) {
	sc, chans, reqs, err := ssh.NewServerConn(conn, cfg)
	if err != nil {
		_ = conn.Close()
		return
	}
	defer sc.Close()
	go ssh.DiscardRequests(reqs)
	for nc := range chans {
		if nc.ChannelType() != "session" {
			_ = nc.Reject(ssh.UnknownChannelType, "session only")
			continue
		}
		ch, creqs, err := nc.Accept()
		if err != nil {
			continue
		}
		go func() {
			for r := range creqs {
				ok := r.Type == "subsystem" && len(r.Payload) >= 4 && string(r.Payload[4:]) == "sftp"
				_ = r.Reply(ok, nil)
				if ok {
					h, rt, err := newRootedHandlers(root)
					if err == nil {
						srv := sftp.NewRequestServer(ch, h)
						_ = srv.Serve()
						_ = srv.Close()
						_ = rt.Close()
					}
					return
				}
			}
		}()
	}
}
