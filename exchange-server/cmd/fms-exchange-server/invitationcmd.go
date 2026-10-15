package main

import (
	"encoding/json"
	"io"
	"net"
	"os"
	"time"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/connstr"
	"fms/exchange-server/internal/tlsid"
)

// invitationString is the connection string of this server with the invitation in it (19 item AR): the
// public endpoint, the pin of the certificate unless the platform already trusts it, the display name, the
// login the person is expected to take and the code as `i`. It returns "" and the reason when it cannot be
// made (no public endpoint yet, no certificate yet) - the code alone is still shown then.
func invitationString(paths config.Paths, label, expectLogin, code string) (string, string) {
	cfg, err := config.Load(paths.Config)
	if err != nil {
		return "", "the config cannot be read"
	}
	if cfg.PublicEndpoint == "" {
		return "", "the address clients use to reach this server is not set (set-endpoint host:port), so no invitation string is made"
	}
	certPath := paths.Cert
	if cfg.TLS.CertFile != "" {
		certPath = cfg.TLS.CertFile
	}
	fp, err := tlsid.FingerprintOfFile(certPath)
	if err != nil {
		return "", "the server has no readable certificate yet (start it once), so no invitation string is made"
	}
	host, _, err := net.SplitHostPort(cfg.PublicEndpoint)
	if err != nil {
		return "", "the configured public endpoint is not host:port"
	}
	if trusted, _ := tlsid.PubliclyTrusted(certPath, host, time.Now(), nil); trusted {
		fp = "" // a CA-signed deployment renews its leaf on the CA's schedule: no pin travels
	}
	c, err := connstr.NewWithInvitation(cfg.PublicEndpoint, fp, label, expectLogin, code)
	if err != nil {
		return "", err.Error()
	}
	s, err := c.String()
	if err != nil {
		return "", err.Error()
	}
	return s, ""
}

// runInvitation executes the invitation subcommands (ticket 224 rule 1). A created invitation is shown
// once - its code and the string that carries it - and the server keeps only a hash.
func runInvitation(cmd command, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	return withRunner(paths, stderr, func(run opRunner) int {
		switch cmd.sub {
		case "create":
			kind := cmd.kind
			if kind == "" {
				kind = accounts.KindMember
			}
			resp, err := run(admin.Request{Op: admin.OpInvitationCreate, Kind: kind, ExpectedLogin: cmd.expectLogin})
			if err != nil {
				printf(stderr, "%v\n", err)
				return exitFailure
			}
			if !resp.OK {
				return exitOf(resp, stderr)
			}
			var created admin.CreateInvitationResult
			if err := json.Unmarshal(resp.Data, &created); err != nil {
				printf(stderr, "%v\n", err)
				return exitFailure
			}
			s, why := invitationString(paths, cmd.label, cmd.expectLogin, created.Code)
			if cmd.json {
				out := struct {
					admin.CreateInvitationResult
					InvitationString string `json:"invitationString,omitempty"`
				}{created, s}
				b, _ := json.Marshal(out)
				printf(stdout, "%s\n", b)
				return exitOK
			}
			printf(stdout, "%s\n", created.Code) // shown once, on stdout
			if s != "" {
				printf(stdout, "%s\n", s)
			} else if why != "" {
				printf(stderr, "%s\n", why)
			}
			return exitOK

		case "list":
			resp, err := run(admin.Request{Op: admin.OpInvitationList})
			if err != nil {
				printf(stderr, "%v\n", err)
				return exitFailure
			}
			if !resp.OK {
				return exitOf(resp, stderr)
			}
			printList(stdout, cmd.json, resp, func(raw json.RawMessage) {
				var invs []admin.InvitationView
				_ = json.Unmarshal(raw, &invs)
				for _, iv := range invs {
					status := "unused"
					if iv.Used {
						status = "used"
						if iv.UsedAt != "" {
							status += " (" + iv.UsedAt + ")"
						}
						if iv.UsedBy != "" {
							status += " by " + iv.UsedBy
						}
					}
					prefix := iv.CodeHash
					if len(prefix) > 8 {
						prefix = prefix[:8]
					}
					expect := ""
					if iv.ExpectedLogin != "" {
						expect = "\texpects " + iv.ExpectedLogin
					}
					printf(stdout, "%s\t%s\t%s\t%s%s\n", prefix, iv.Kind, iv.CreatedAt, status, expect)
				}
			})
			return exitOK

		case "withdraw":
			resp, err := run(admin.Request{Op: admin.OpInvitationWithdraw, CodeHash: cmd.value})
			if err != nil {
				printf(stderr, "%v\n", err)
				return exitFailure
			}
			if !resp.OK {
				return exitOf(resp, stderr)
			}
			printf(stdout, "withdrawn\n")
			return exitOK
		}
		return exitUsage
	})
}

// runSharedInvitation executes the shared-invitation subcommands (ticket 224 rule 2).
func runSharedInvitation(cmd command, stdin io.Reader, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	return withRunner(paths, stderr, func(run opRunner) int {
		switch cmd.sub {
		case "set":
			kind := cmd.kind
			if kind == "" {
				kind = accounts.KindMember
			}
			pw, code := readPassword(cmd, stdin, stderr)
			if code != exitOK {
				return code
			}
			resp, err := run(admin.Request{Op: admin.OpSharedInvitationSet, Password: pw, Kind: kind})
			if err != nil {
				printf(stderr, "%v\n", err)
				return exitFailure
			}
			if !resp.OK {
				return exitOf(resp, stderr)
			}
			if cmd.generate {
				printf(stdout, "%s\n", pw) // once, on stdout
			} else {
				printf(stdout, "shared invitation password set\n")
			}
			return exitOK

		case "off":
			resp, err := run(admin.Request{Op: admin.OpSharedInvitationSet, Password: ""})
			if err != nil {
				printf(stderr, "%v\n", err)
				return exitFailure
			}
			if !resp.OK {
				return exitOf(resp, stderr)
			}
			printf(stdout, "shared invitation password off\n")
			return exitOK

		case "status":
			resp, err := run(admin.Request{Op: admin.OpSharedInvitationStatus})
			if err != nil {
				printf(stderr, "%v\n", err)
				return exitFailure
			}
			if !resp.OK {
				return exitOf(resp, stderr)
			}
			if cmd.json {
				printf(stdout, "%s\n", resp.Data)
				return exitOK
			}
			var st admin.SharedInvitationView
			_ = json.Unmarshal(resp.Data, &st)
			if st.Enabled {
				printf(stdout, "on\t%s\n", st.Kind)
			} else {
				printf(stdout, "off\n")
			}
			return exitOK
		}
		return exitUsage
	})
}

// runGrant executes the grant subcommands (ticket 224 rule 3): a member gives one of its resources or
// broadcasts to a guest account, and takes it back.
func runGrant(cmd command, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	return withRunner(paths, stderr, func(run opRunner) int {
		switch cmd.sub {
		case "add":
			kind := cmd.kind
			if kind == "" {
				kind = accounts.GrantResource
			}
			resp, err := run(admin.Request{Op: admin.OpGrantAdd, Login: cmd.login, GuestAccount: cmd.guest, TargetID: cmd.targetID, Kind: kind})
			if err != nil {
				printf(stderr, "%v\n", err)
				return exitFailure
			}
			if !resp.OK {
				return exitOf(resp, stderr)
			}
			printf(stdout, "granted\n")
			return exitOK

		case "remove":
			resp, err := run(admin.Request{Op: admin.OpGrantRemove, Login: cmd.login, GuestAccount: cmd.guest, TargetID: cmd.targetID})
			if err != nil {
				printf(stderr, "%v\n", err)
				return exitFailure
			}
			if !resp.OK {
				return exitOf(resp, stderr)
			}
			printf(stdout, "taken back\n")
			return exitOK

		case "list":
			// what the login was given (as a guest) and what it gave (as a member): one account is only ever one
			// of the two kinds, so one of the lists is empty - asking both keeps the command kind-agnostic
			received, err := run(admin.Request{Op: admin.OpGrantList, GuestAccount: cmd.login})
			if err != nil {
				printf(stderr, "%v\n", err)
				return exitFailure
			}
			if !received.OK {
				return exitOf(received, stderr)
			}
			given, err := run(admin.Request{Op: admin.OpGrantList, Login: cmd.login})
			if err != nil {
				printf(stderr, "%v\n", err)
				return exitFailure
			}
			if !given.OK {
				return exitOf(given, stderr)
			}
			var in, out []admin.GrantView
			_ = json.Unmarshal(received.Data, &in)
			_ = json.Unmarshal(given.Data, &out)
			if cmd.json {
				b, _ := json.Marshal(map[string][]admin.GrantView{"received": in, "given": out})
				printf(stdout, "%s\n", b)
				return exitOK
			}
			for _, g := range in {
				printf(stdout, "received\t%s\t%s\tfrom %s\t%s\n", g.TargetID, g.Kind, g.GrantedBy, g.GrantedAt)
			}
			for _, g := range out {
				printf(stdout, "given\t%s\t%s\tto %s\t%s\n", g.TargetID, g.Kind, g.Guest, g.GrantedAt)
			}
			return exitOK
		}
		return exitUsage
	})
}
