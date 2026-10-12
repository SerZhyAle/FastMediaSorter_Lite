package main

import (
	"bufio"
	"encoding/json"
	"errors"
	"io"
	"os"
	"strings"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/deploy"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/password"
)

// The account and device commands (ticket 162 section 4). They change accounts while a server runs
// (through the local admin channel) and when none does (by editing the stores directly under the data
// directory's lock): which of the two is decided by whether the lock can be taken. A password is read
// from stdin or made by --generate and printed ONCE on stdout - it is never an argument, never
// stored, never returned by the server.

// op runs one request against the stores or the running server. It returns the response of the
// channel in both cases, so the printing below is one piece of code.
type opRunner func(admin.Request) (admin.Response, error)

// withRunner finds the way to the stores: the directory lock taken (no server) gives a runner that
// edits the stores itself and flushes the directory; a lock that is held (a server runs) gives a
// runner that speaks to the admin channel.
func withRunner(paths config.Paths, stderr io.Writer, f func(opRunner) int) int {
	release, err := deploy.LockDir(paths.Dir)
	if err == nil {
		defer release()
		cfg, _ := config.Load(paths.Config)
		store, err := accounts.Open(accounts.Options{Dir: paths.Dir, DevicesPerAccount: cfg.Limits.DevicesPerAccount})
		if err != nil {
			printf(stderr, "cannot open the accounts: %v\n", err)
			return exitConfig
		}
		dir := directory.New(directory.Options{Dir: paths.Dir})
		var infos []directory.DeviceInfo
		for _, d := range store.AllDevices() {
			infos = append(infos, directory.DeviceInfo{ID: d.ID, Account: d.Account, Name: d.Name, Product: d.Product,
				ProductVersion: d.ProductVersion, Platform: d.Platform, Roles: d.Roles, Capabilities: d.Capabilities, Receiver: d.Receiver})
		}
		if err := dir.Load(infos); err != nil {
			printf(stderr, "cannot open the directory: %v\n", err)
			return exitConfig
		}
		ops := &admin.Ops{Accounts: store, Dir: dir}
		code := f(func(req admin.Request) (admin.Response, error) { return ops.Handle(req), nil })
		if err := dir.Flush(); err != nil {
			printf(stderr, "cannot write the directory: %v\n", err)
			return exitConfig
		}
		return code
	}
	if !errors.Is(err, deploy.ErrLocked) {
		printf(stderr, "cannot lock the data directory %s: %v\n", paths.Dir, err)
		return exitFailure
	}
	// a server holds the directory: ask it
	return f(func(req admin.Request) (admin.Response, error) {
		conn, err := deploy.DialAdmin(paths.Dir)
		if err != nil {
			return admin.Response{}, errors.New("a server is running on this data directory but its admin channel is not reachable: " + err.Error())
		}
		return admin.Do(conn, req)
	})
}

// readPassword is the password of `account add` and `account passwd`: made by --generate, otherwise
// one line of stdin.
func readPassword(cmd command, stdin io.Reader, stderr io.Writer) (pw string, code int) {
	if cmd.generate {
		p, err := password.Generate()
		if err != nil {
			printf(stderr, "cannot generate a password: %v\n", err)
			return "", exitFailure
		}
		return p, exitOK
	}
	line, err := bufio.NewReaderSize(stdin, 4096).ReadString('\n')
	if err != nil && err != io.EOF {
		printf(stderr, "cannot read the password from stdin: %v\n", err)
		return "", exitUsage
	}
	pw = strings.TrimRight(line, "\r\n")
	if err := password.Check(pw); err != nil {
		printf(stderr, "%v\n", err)
		return "", exitPolicy
	}
	return pw, exitOK
}

// exitOf maps a failed response onto the exit codes: a refused password is 3 (the policy), a
// missing account or device or an existing one is 1, a bad login 2.
func exitOf(r admin.Response, stderr io.Writer) int {
	printf(stderr, "%s\n", r.Error)
	switch r.Code {
	case admin.CodeWeak:
		return exitPolicy
	case admin.CodeBadLogin, admin.CodeBadRequest:
		return exitUsage
	}
	return exitFailure
}

func runAccount(cmd command, stdin io.Reader, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	return withRunner(paths, stderr, func(run opRunner) int {
		var req admin.Request
		var generated string
		switch cmd.sub {
		case "add", "passwd":
			pw, code := readPassword(cmd, stdin, stderr)
			if code != exitOK {
				return code
			}
			if cmd.generate {
				generated = pw
			}
			req = admin.Request{Op: admin.OpAccountAdd, Login: cmd.login, Password: pw}
			if cmd.sub == "passwd" {
				req.Op, req.SignOut = admin.OpAccountPasswd, cmd.signOut
			}
		case "remove":
			req = admin.Request{Op: admin.OpAccountRemove, Login: cmd.login}
		case "list":
			req = admin.Request{Op: admin.OpAccountList}
		}
		resp, err := run(req)
		if err != nil {
			printf(stderr, "%v\n", err)
			return exitFailure
		}
		if !resp.OK {
			return exitOf(resp, stderr)
		}
		switch cmd.sub {
		case "add", "passwd":
			if generated != "" {
				printf(stdout, "%s\n", generated) // once, on stdout, and nowhere else
			}
		case "list":
			printList(stdout, cmd.json, resp, func(raw json.RawMessage) {
				var as []admin.AccountView
				_ = json.Unmarshal(raw, &as)
				for _, a := range as {
					printf(stdout, "%s\t%s\t%d devices\n", a.Login, a.CreatedAt, a.Devices)
				}
			})
		}
		return exitOK
	})
}

func runDevice(cmd command, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	return withRunner(paths, stderr, func(run opRunner) int {
		req := admin.Request{Op: admin.OpDeviceList, Login: cmd.login}
		if cmd.sub == "revoke" {
			req = admin.Request{Op: admin.OpDeviceRevoke, Login: cmd.login, DeviceID: cmd.deviceID}
		}
		resp, err := run(req)
		if err != nil {
			printf(stderr, "%v\n", err)
			return exitFailure
		}
		if !resp.OK {
			return exitOf(resp, stderr)
		}
		if cmd.sub == "list" {
			printList(stdout, cmd.json, resp, func(raw json.RawMessage) {
				var ds []admin.DeviceView
				_ = json.Unmarshal(raw, &ds)
				for _, d := range ds {
					state := "offline"
					if d.Online {
						state = "online"
					}
					printf(stdout, "%s\t%s\t%s\t%s\t%s\n", d.DeviceID, state, d.Product, d.Name, d.LastSeen)
				}
			})
		}
		return exitOK
	})
}

func printList(w io.Writer, asJSON bool, resp admin.Response, text func(json.RawMessage)) {
	if asJSON {
		printf(w, "%s\n", resp.Data)
		return
	}
	text(resp.Data)
}
