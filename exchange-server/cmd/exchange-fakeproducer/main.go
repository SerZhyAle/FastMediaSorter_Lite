// exchange-fakeproducer is a MANUAL-PROOF tool (ticket 137 T-4, ported to DEVICE-EXCHANGE by ticket
// 164), not part of any package: it enrolls a device into an account of a running exchange server,
// publishes a share with a third-party port, and for every tunnel the server opens it runs a real SSH
// server with an SFTP subsystem over the attach stream. Point WinSCP or FileZilla at the per-resource
// port it prints and a folder listing comes through the bridge.
//
// It serves a real directory with a fixed login. Use a scratch folder. The account's password is read
// from stdin, never from the command line. Ticket 178's probe supersedes it as the producer of the
// third-party proof; it stays until that exists, because the suite's "manual proof" step runs its test.
package main

import (
	"bufio"
	"flag"
	"fmt"
	"net"
	"os"
	"strings"
	"time"

	"github.com/pkg/sftp"
	"golang.org/x/crypto/ssh"

	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
)

func main() {
	server := flag.String("server", "127.0.0.1:22022", "exchange server host:port")
	pin := flag.String("fingerprint", "", "the server's TLS fingerprint (SHA256:..); empty trusts the first certificate seen")
	login := flag.String("login", "", "the account to enroll into")
	port := flag.Int("port", 0, "third-party port to request (0 = any)")
	user := flag.String("user", "tester", "SFTP user name")
	pass := flag.String("pass", "tester-pass", "SFTP password")
	root := flag.String("root", ".", "the directory to serve (use a scratch folder)")
	flag.Parse()
	if *login == "" {
		fmt.Fprintln(os.Stderr, "need --login (and the account's password on stdin)")
		os.Exit(2)
	}
	line, _ := bufio.NewReader(os.Stdin).ReadString('\n')
	pw := strings.TrimRight(line, "\r\n")
	if pw == "" {
		fmt.Fprintln(os.Stderr, "the account's password is read from the first line of stdin")
		os.Exit(2)
	}

	cfg := testkit.SSHServerConfig("")
	cfg.PasswordCallback = func(c ssh.ConnMetadata, got []byte) (*ssh.Permissions, error) {
		if c.User() == *user && string(got) == *pass {
			return nil, nil
		}
		return nil, fmt.Errorf("denied")
	}

	dev := testkit.NewRemoteDevice(*server, *pin, *login)
	dev.Password = pw
	p := &testkit.Producer{Device: dev, Handler: func(c net.Conn) { serveSFTP(c, cfg, *root) }}
	if err := p.Start(); err != nil {
		fmt.Fprintln(os.Stderr, "enroll:", err)
		os.Exit(1)
	}
	resourceID, shareID, raw := p.ResourceJSON("fake producer share", true, port)
	ans, err := p.Publish(raw)
	if err != nil {
		fmt.Fprintln(os.Stderr, "publish:", err)
		os.Exit(1)
	}
	pub, ok := ans.(*wire.Published)
	if !ok {
		fmt.Fprintf(os.Stderr, "refused: %+v\n", ans)
		os.Exit(1)
	}
	fmt.Printf("enrolled and published; resource %s\n", resourceID)
	fmt.Printf("share id %s  (an outsider connects with this: connect {shareId})\n", shareID)
	if pub.PublicPort != nil {
		fmt.Printf("third-party port %d -> point WinSCP/FileZilla at <server host>:%d, SFTP, user %q, password %q\n", *pub.PublicPort, *pub.PublicPort, *user, *pass)
	}
	for {
		select {
		case <-p.Control().Closed():
			fmt.Println("control stream closed")
			return
		case <-time.After(15 * time.Second):
			if _, err := p.Control().Request(&wire.Keepalive{}, 5*time.Second, wire.TypeKeepalive); err != nil {
				fmt.Println("keepalive:", err)
				return
			}
		}
	}
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
