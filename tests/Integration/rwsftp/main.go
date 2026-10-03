// Command rwsftp is a throw-away WRITABLE SFTP server for the live acceptance of remote recipient
// slots (ticket 007 section 4.2). The worker's own devserve serves read-only by construction, so the
// write path - upload, verify, publish, rename, delete, mkdir - could only be proved against the
// in-memory fixture; this proves it over a real SSH connection with the real SFTP protocol
// (github.com/pkg/sftp, the library the worker itself is built on).
//
// Safe by construction: it listens on 127.0.0.1 only, keeps every file in memory (nothing it is sent
// ever touches the disk), accepts one user and one password given on the command line, and exits when
// its parent kills it. The only file it writes is its own host key, at the path it is given, so a
// restart comes back with the same identity - which is what a server returning after a dropped link
// looks like.
//
// Usage: rwsftp <port> <host-key-file> <password>
// Prints one JSON line on stdout: {"port":N,"user":"slot","fingerprint":"SHA256:..."}.
package main

import (
	"crypto/ed25519"
	"crypto/rand"
	"crypto/x509"
	"encoding/json"
	"encoding/pem"
	"errors"
	"fmt"
	"net"
	"os"
	"strconv"

	"github.com/pkg/sftp"
	"golang.org/x/crypto/ssh"
)

const user = "slot"

func main() {
	if len(os.Args) < 4 {
		fmt.Fprintln(os.Stderr, "usage: rwsftp <port> <host-key-file> <password>")
		os.Exit(2)
	}
	port, err := strconv.Atoi(os.Args[1])
	if err != nil {
		fmt.Fprintln(os.Stderr, "bad port:", err)
		os.Exit(2)
	}
	signer, err := hostKey(os.Args[2])
	if err != nil {
		fmt.Fprintln(os.Stderr, "host key:", err)
		os.Exit(1)
	}
	password := os.Args[3]

	config := &ssh.ServerConfig{
		PasswordCallback: func(meta ssh.ConnMetadata, given []byte) (*ssh.Permissions, error) {
			if meta.User() == user && string(given) == password {
				return nil, nil
			}
			return nil, errors.New("password rejected")
		},
	}
	config.AddHostKey(signer)

	listener, err := net.Listen("tcp", net.JoinHostPort("127.0.0.1", strconv.Itoa(port)))
	if err != nil {
		fmt.Fprintln(os.Stderr, "listen:", err)
		os.Exit(1)
	}

	// One file system for every connection, seeded with the folder the slots point into.
	handlers := sftp.InMemHandler()
	if err := handlers.FileCmd.Filecmd(sftp.NewRequest("Mkdir", "/MOV")); err != nil {
		fmt.Fprintln(os.Stderr, "seed:", err)
		os.Exit(1)
	}

	line, _ := json.Marshal(map[string]any{
		"port":        listener.Addr().(*net.TCPAddr).Port,
		"user":        user,
		"fingerprint": ssh.FingerprintSHA256(signer.PublicKey()),
	})
	fmt.Println(string(line))

	for {
		conn, err := listener.Accept()
		if err != nil {
			return
		}
		go serve(conn, config, handlers)
	}
}

func serve(conn net.Conn, config *ssh.ServerConfig, handlers sftp.Handlers) {
	_, channels, requests, err := ssh.NewServerConn(conn, config)
	if err != nil {
		conn.Close()
		return
	}
	go ssh.DiscardRequests(requests)
	for incoming := range channels {
		if incoming.ChannelType() != "session" {
			incoming.Reject(ssh.UnknownChannelType, "session only")
			continue
		}
		channel, channelRequests, err := incoming.Accept()
		if err != nil {
			continue
		}
		go func(in <-chan *ssh.Request) {
			for request := range in {
				// "subsystem" + uint32 length + "sftp"
				request.Reply(request.Type == "subsystem" && len(request.Payload) > 4 && string(request.Payload[4:]) == "sftp", nil)
			}
		}(channelRequests)
		go func() {
			server := sftp.NewRequestServer(channel, handlers)
			server.Serve()
			server.Close()
		}()
	}
}

// hostKey loads the ed25519 key at path, or creates and stores one there.
func hostKey(path string) (ssh.Signer, error) {
	if raw, err := os.ReadFile(path); err == nil {
		block, _ := pem.Decode(raw)
		if block == nil {
			return nil, errors.New("not a PEM key")
		}
		key, err := x509.ParsePKCS8PrivateKey(block.Bytes)
		if err != nil {
			return nil, err
		}
		return ssh.NewSignerFromKey(key)
	}
	_, private, err := ed25519.GenerateKey(rand.Reader)
	if err != nil {
		return nil, err
	}
	der, err := x509.MarshalPKCS8PrivateKey(private)
	if err != nil {
		return nil, err
	}
	if err := os.WriteFile(path, pem.EncodeToMemory(&pem.Block{Type: "PRIVATE KEY", Bytes: der}), 0o600); err != nil {
		return nil, err
	}
	return ssh.NewSignerFromKey(private)
}
