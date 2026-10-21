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
	"fms/exchange-server/internal/logx"
	"fms/exchange-server/internal/password"
)

// The account and device commands (ticket 162 section 4, ticket 224). They change accounts while a server
// runs (through the local admin channel) and when none does (by editing the stores directly under the
// data directory's lock): which of the two is decided by whether the lock can be taken. A password is
// read from stdin or made by --generate and printed ONCE on stdout - it is never an argument, never
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
		cfg, err := config.Load(paths.Config)
		if errors.Is(err, os.ErrNotExist) {
			cfg, err = config.Default(), nil
		}
		if err != nil {
			printf(stderr, "cannot read the config %s: %v\n", paths.Config, err)
			return exitConfig
		}
		policy := policyOf(cfg.Limits)
		store, err := accounts.Open(accounts.Options{Dir: paths.Dir, DevicesPerAccount: cfg.Limits.DevicesPerAccount,
			CodesPerAccount: cfg.Limits.CodesPerAccount, Policy: &policy})
		if err != nil {
			printf(stderr, "cannot open the accounts: %v\n", err)
			return exitConfig
		}
		dir := directory.New(directory.Options{Dir: paths.Dir})
		var infos []directory.DeviceInfo
		for _, d := range store.AllDevices() {
			infos = append(infos, directory.DeviceInfo{ID: d.ID, Account: d.Account, Name: d.Name, Product: d.Product,
				ProductVersion: d.ProductVersion, Platform: d.Platform, Roles: d.Roles, Capabilities: d.Capabilities, Receiver: d.Receiver, E2EKey: d.E2EKey})
		}
		if err := dir.Load(infos); err != nil {
			printf(stderr, "cannot open the directory: %v\n", err)
			return exitConfig
		}
		var logger *logx.Logger
		var audit *logx.Audit
		defer func() { _ = logger.Close() }()
		defer func() { _ = audit.Close() }()
		ops := &admin.Ops{Accounts: store, Dir: dir, SaveLimits: saveLimitsTo(paths.Config)}
		code := f(func(req admin.Request) (admin.Response, error) {
			if stateMutation(req.Op) && logger == nil {
				if err := os.MkdirAll(paths.LogsDir, 0o700); err != nil {
					return admin.Response{}, errors.New("cannot create the state log directory")
				}
				logger, err = logx.OpenDaily(paths.LogsDir, cfg.Log.MaxBytes, cfg.Log.RetentionDays, nil)
				if err != nil {
					return admin.Response{}, errors.New("cannot open the state log")
				}
				ops.Log = logger
				audit, err = logx.OpenAuditDaily(paths.LogsDir, cfg.Log.MaxBytes, cfg.Log.RetentionDays, nil)
				if err != nil {
					return admin.Response{}, errors.New("cannot open the operational audit")
				}
				ops.AuditLog = audit
			}
			return ops.HandleAudited(req, logx.AuditEvent{Transport: "LocalCLI", Product: admin.ClientProductLocal}), nil
		})
		if err := dir.Flush(); err != nil {
			printf(stderr, "cannot write the directory: %v\n", err)
			return exitConfig
		}
		if err := store.Flush(); err != nil {
			printf(stderr, "cannot write the accounts: %v\n", err)
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
	pw = strings.TrimPrefix(strings.TrimRight(line, "\r\n"), "\ufeff")
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
	case admin.CodeBadLogin, admin.CodeBadRequest, admin.CodeBadKind, admin.CodeBadLimit:
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
			if cmd.sub == "add" {
				kind := cmd.kind
				if kind == "" {
					kind = accounts.KindMember
				}
				req = admin.Request{Op: admin.OpAccountAdd, Login: cmd.login, Password: pw, Kind: kind}
			} else {
				req = admin.Request{Op: admin.OpAccountPasswd, Login: cmd.login, Password: pw}
			}
		case "kind":
			req = admin.Request{Op: admin.OpAccountKind, Login: cmd.login, Kind: cmd.kind}
		case "remove":
			req = admin.Request{Op: admin.OpAccountRemove, Login: cmd.login}
		case "list":
			req = admin.Request{Op: admin.OpAccountList}
		case "limits":
			return runAccountLimits(cmd, run, stdout, stderr)
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
		case "add":
			if generated != "" {
				printf(stdout, "%s\n", generated) // once, on stdout, and nowhere else
			}
			var added struct {
				OverCap bool `json:"overCap"`
			}
			_ = json.Unmarshal(resp.Data, &added)
			if added.OverCap {
				// the operator is never refused for the cap (it binds `register`): say that it is over
				if p, err := run(admin.Request{Op: admin.OpPolicyStatus}); err == nil && p.OK {
					var v admin.PolicyView
					_ = json.Unmarshal(p.Data, &v)
					printf(stderr, "warning: %d accounts exist and the cap is %d: registration is refused until one is removed or the cap is raised\n", v.Accounts, v.MaxAccounts)
				}
			}
		case "passwd":
			if generated != "" {
				printf(stdout, "%s\n", generated) // once, on stdout, and nowhere else
			}
			var done struct {
				SignedOut int `json:"signedOut"`
			}
			_ = json.Unmarshal(resp.Data, &done)
			printf(stderr, "%d devices signed out; they stay listed and are refused at their next sign-in\n", done.SignedOut)
		case "kind":
			var done struct {
				Changed bool `json:"changed"`
			}
			_ = json.Unmarshal(resp.Data, &done)
			if done.Changed {
				printf(stdout, "%s is now a %s\n", cmd.login, cmd.kind)
			} else {
				printf(stdout, "%s already is a %s\n", cmd.login, cmd.kind)
			}
		case "list":
			printList(stdout, cmd.json, resp, func(raw json.RawMessage) {
				var as []admin.AccountView
				_ = json.Unmarshal(raw, &as)
				for _, a := range as {
					kind := a.Kind
					if kind == "" {
						kind = accounts.KindMember
					}
					custom := ""
					if !a.Limits.IsEmpty() {
						custom = "\town limits"
					}
					printf(stdout, "%s\t%s\t%s\t%d devices%s\n", a.Login, kind, a.CreatedAt, a.Devices, custom)
				}
			})
		}
		return exitOK
	})
}

// runAccountLimits shows or changes the limits of one account. The store replaces an account's override
// set as a whole, so a change is read-merge-write: naming one limit leaves the others as they were, and
// --reset drops them all.
func runAccountLimits(cmd command, run opRunner, stdout, stderr io.Writer) int {
	login, ok := accounts.NormalizeLogin(cmd.login)
	if !ok {
		printf(stderr, "%q is not a login\n", cmd.login)
		return exitUsage
	}
	resp, err := run(admin.Request{Op: admin.OpAccountList})
	if err != nil {
		printf(stderr, "%v\n", err)
		return exitFailure
	}
	if !resp.OK {
		return exitOf(resp, stderr)
	}
	var all []admin.AccountView
	_ = json.Unmarshal(resp.Data, &all)
	var cur *admin.AccountView
	for i := range all {
		if all[i].Login == login {
			cur = &all[i]
		}
	}
	if cur == nil {
		printf(stderr, "no such account\n")
		return exitFailure
	}
	patch := cmd.limits()
	if patch == nil && !cmd.reset {
		if cmd.json {
			b, _ := json.Marshal(cur)
			printf(stdout, "%s\n", b)
			return exitOK
		}
		printf(stdout, "in force\t%s\n", describeLimits(cur.Effective))
		if cur.Limits.IsEmpty() {
			printf(stdout, "own limits\tnone (the deployment's defaults apply)\n")
		} else {
			printf(stdout, "own limits\t%s\n", describeOverrides(cur.Limits))
		}
		return exitOK
	}
	var next *accounts.AccountLimits
	if !cmd.reset {
		var base accounts.AccountLimits
		if cur.Limits != nil {
			base = *cur.Limits
		}
		merged := base.Merge(patch)
		next = &merged
	}
	resp, err = run(admin.Request{Op: admin.OpAccountLimits, Login: login, Limits: next})
	if err != nil {
		printf(stderr, "%v\n", err)
		return exitFailure
	}
	if !resp.OK {
		return exitOf(resp, stderr)
	}
	if cmd.reset {
		printf(stdout, "limits reset to the deployment's defaults\n")
	} else {
		printf(stdout, "limits updated\n")
	}
	return exitOK
}

// describeOverrides names the limits an account sets itself.
func describeOverrides(l *accounts.AccountLimits) string {
	var parts []string
	if l.DevicesPerAccount != nil {
		parts = append(parts, "devices "+capText(int64(*l.DevicesPerAccount)))
	}
	if l.ResourcesPerAccount != nil {
		parts = append(parts, "resources "+capText(int64(*l.ResourcesPerAccount)))
	}
	if l.BroadcastsPerAccount != nil {
		parts = append(parts, "broadcasts "+capText(int64(*l.BroadcastsPerAccount)))
	}
	if l.BroadcastsPerDevice != nil {
		parts = append(parts, "broadcasts per device "+capText(int64(*l.BroadcastsPerDevice)))
	}
	if l.ClipboardMaxBytes != nil {
		parts = append(parts, "clipboard "+clipboardText(*l.ClipboardMaxBytes))
	}
	return strings.Join(parts, ", ")
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
					switch {
					case d.SignedOut:
						state = "signed-out"
					case d.Online:
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
