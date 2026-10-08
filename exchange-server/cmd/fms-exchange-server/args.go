package main

import (
	"errors"
	"fmt"
	"io"
	"strings"

	"fms/exchange-server/internal/wire"
)

// command is a parsed command line. The parser is hand-written, the worker's style: an unknown
// flag is an error, never silently ignored. Every field is comparable so a test can compare whole
// commands.
type command struct {
	name     string // version | serve | stop | set-listen | set-endpoint | set-port-range | inspect | account | device | tls
	sub      string // account: add|passwd|remove|list; device: list|revoke; tls: replace|import
	config   string
	generate bool   // account add / passwd --generate
	signOut  bool   // account passwd --sign-out
	json     bool   // list commands --json
	init     bool   // inspect --init
	service  bool   // serve --service (under the Windows service control manager)
	value    string // set-listen / set-endpoint: the host:port to store
	login    string // account and device commands
	deviceID string // device revoke
	cert     string // tls import: the PEM certificate chain to import
	key      string // tls import: its PEM private key
}

const (
	usageAccount = "account add <login> [--generate] | passwd <login> [--generate] [--sign-out] | remove <login> | list [--json]"
	usageDevice  = "device list <login> [--json] | revoke <login> <deviceId>"
	usageTLS     = "tls replace | import --cert <file> --key <file>"
)

func parseArgs(args []string) (command, error) {
	var c command
	if len(args) == 0 {
		return c, errors.New("no command given")
	}
	switch args[0] {
	case "--version", "version":
		if len(args) > 1 {
			return c, fmt.Errorf("unexpected argument %q", args[1])
		}
		c.name = "version"
		return c, nil
	case "serve", "stop", "set-listen", "set-endpoint", "set-port-range", "inspect", "account", "device", "tls":
		c.name = args[0]
	default:
		return c, fmt.Errorf("unknown command %q", args[0])
	}
	rest := args[1:]
	var pos []string
	for i := 0; i < len(rest); i++ {
		a := rest[i]
		if a == "--" { // everything after is positional
			pos = append(pos, rest[i+1:]...)
			break
		}
		name, val, hasVal := strings.Cut(a, "=")
		switch name {
		case "--config":
			if !hasVal {
				if i+1 >= len(rest) {
					return c, errors.New("--config needs a path")
				}
				i++
				val = rest[i]
			}
			if val == "" {
				return c, errors.New("--config needs a path")
			}
			c.config = val
		case "--generate":
			if c.name != "account" || hasVal {
				return c, fmt.Errorf("unknown option %q for %s", a, c.name)
			}
			c.generate = true
		case "--sign-out":
			if c.name != "account" || hasVal {
				return c, fmt.Errorf("unknown option %q for %s", a, c.name)
			}
			c.signOut = true
		case "--json":
			if (c.name != "account" && c.name != "device") || hasVal {
				return c, fmt.Errorf("unknown option %q for %s", a, c.name)
			}
			c.json = true
		case "--init":
			if c.name != "inspect" || hasVal {
				return c, fmt.Errorf("unknown option %q for %s", a, c.name)
			}
			c.init = true
		case "--service":
			if c.name != "serve" || hasVal {
				return c, fmt.Errorf("unknown option %q for %s", a, c.name)
			}
			c.service = true
		case "--cert", "--key":
			if c.name != "tls" {
				return c, fmt.Errorf("unknown option %q for %s", name, c.name)
			}
			if !hasVal {
				if i+1 >= len(rest) {
					return c, fmt.Errorf("%s needs a path", name)
				}
				i++
				val = rest[i]
			}
			if val == "" {
				return c, fmt.Errorf("%s needs a path", name)
			}
			if name == "--cert" {
				c.cert = val
			} else {
				c.key = val
			}
		default:
			// a deviceId is base64url and may begin with a hyphen: on `device` an id-shaped token is an argument
			if strings.HasPrefix(a, "-") && a != "-" && !(c.name == "device" && wire.ValidID(a)) {
				return c, fmt.Errorf("unknown option %q for %s", a, c.name)
			}
			pos = append(pos, a)
		}
	}

	switch c.name {
	case "set-listen", "set-endpoint", "set-port-range":
		if len(pos) != 1 {
			switch c.name {
			case "set-listen":
				return c, errors.New("set-listen needs an address such as :22022 or 0.0.0.0:22022")
			case "set-port-range":
				return c, errors.New("set-port-range needs the third-party port range, such as 40000-40100")
			}
			return c, errors.New("set-endpoint needs the public host:port, such as exchange.example.org:22022 (or - to clear it)")
		}
		c.value = pos[0]
	case "tls":
		if len(pos) != 1 {
			return c, errors.New("tls needs a subcommand: " + usageTLS)
		}
		c.sub = pos[0]
		switch c.sub {
		case "replace":
			if c.cert != "" || c.key != "" {
				return c, fmt.Errorf("--cert and --key belong to tls import: %s", usageTLS)
			}
		case "import":
			if c.cert == "" || c.key == "" {
				return c, fmt.Errorf("tls import needs both --cert and --key: %s", usageTLS)
			}
		default:
			return c, fmt.Errorf("unknown tls subcommand %q: %s", c.sub, usageTLS)
		}
	case "account":
		if len(pos) == 0 {
			return c, errors.New("account needs a subcommand: " + usageAccount)
		}
		c.sub = pos[0]
		switch c.sub {
		case "add", "passwd", "remove":
			if len(pos) != 2 {
				return c, fmt.Errorf("account %s needs a login: %s", c.sub, usageAccount)
			}
			c.login = pos[1]
		case "list":
			if len(pos) != 1 {
				return c, errors.New("account list takes no argument")
			}
		default:
			return c, fmt.Errorf("unknown account subcommand %q: %s", c.sub, usageAccount)
		}
		if (c.generate && c.sub != "add" && c.sub != "passwd") || (c.signOut && c.sub != "passwd") || (c.json && c.sub != "list") {
			return c, fmt.Errorf("an option does not belong to account %s: %s", c.sub, usageAccount)
		}
	case "device":
		if len(pos) == 0 {
			return c, errors.New("device needs a subcommand: " + usageDevice)
		}
		c.sub = pos[0]
		switch c.sub {
		case "list":
			if len(pos) != 2 {
				return c, fmt.Errorf("device list needs a login: %s", usageDevice)
			}
			c.login = pos[1]
		case "revoke":
			if len(pos) != 3 {
				return c, fmt.Errorf("device revoke needs a login and a deviceId: %s", usageDevice)
			}
			c.login, c.deviceID = pos[1], pos[2]
		default:
			return c, fmt.Errorf("unknown device subcommand %q: %s", c.sub, usageDevice)
		}
		if c.json && c.sub != "list" {
			return c, fmt.Errorf("--json does not belong to device %s", c.sub)
		}
	default:
		if len(pos) > 0 {
			return c, fmt.Errorf("unknown option %q for %s", pos[0], c.name)
		}
	}
	return c, nil
}

func printUsage(w io.Writer, err error) {
	printf(w, "error: %v\n", err)
	printf(w, "usage:\n")
	printf(w, "  fms-exchange-server serve [--config <file>] [--service]   (--service: only under the Windows service control manager)\n")
	printf(w, "  fms-exchange-server stop [--config <file>]                (asks the console server of that config to stop cleanly)\n")
	printf(w, "  fms-exchange-server %s [--config <file>]\n", usageAccount)
	printf(w, "      (a password is read from stdin, or --generate makes one that meets the policy and prints it ONCE)\n")
	printf(w, "  fms-exchange-server %s [--config <file>]\n", usageDevice)
	printf(w, "  fms-exchange-server set-listen [--config <file>] <host:port>   (the listen address, e.g. :22022)\n")
	printf(w, "  fms-exchange-server set-endpoint [--config <file>] <host:port|->   (the public address announced in welcome)\n")
	printf(w, "  fms-exchange-server set-port-range [--config <file>] <lo-hi>   (the third-party port range, e.g. 40000-40100)\n")
	printf(w, "  fms-exchange-server %s [--config <file>]\n", usageTLS)
	printf(w, "      (a new self-signed identity, or a certificate and key to serve; applies at the next start)\n")
	printf(w, "  fms-exchange-server inspect [--config <file>] [--init]\n")
	printf(w, "  fms-exchange-server --version\n")
}

func printf(w io.Writer, format string, a ...any) { _, _ = fmt.Fprintf(w, format, a...) }
