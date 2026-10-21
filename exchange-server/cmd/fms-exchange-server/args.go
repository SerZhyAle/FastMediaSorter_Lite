package main

import (
	"errors"
	"fmt"
	"io"
	"math"
	"strconv"
	"strings"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/wire"
)

// command is a parsed command line. The parser is hand-written, the worker's style: an unknown
// flag is an error, never silently ignored. Every field is comparable so a test can compare whole
// commands.
type command struct {
	name               string // version | serve | stop | set-listen | set-endpoint | set-port-range | inspect | account | device | tls | connection-string | invitation | shared-invitation | grant | policy
	sub                string // account: add|passwd|kind|remove|list|limits; device: list|revoke; tls: replace|import; invitation: create|list|withdraw; shared-invitation: set|off|status; grant: add|remove|list; policy: show|set
	config             string
	generate           bool   // account add / passwd / shared-invitation set --generate
	signOut            bool   // account passwd --sign-out (kept for old scripts: a reset signs every device out, item AT)
	json               bool   // list commands --json
	init               bool   // inspect --init
	service            bool   // serve --service (under the Windows service control manager)
	reset              bool   // account limits --reset: drop the account's own limits
	value              string // set-listen / set-endpoint: the host:port to store; invitation withdraw: the hash
	login              string // account and device commands, the owner of a grant, connection-string --login
	guest              string // grant add|remove: the guest account
	deviceID           string // device revoke
	cert               string // tls import: the PEM certificate chain to import
	key                string // tls import: its PEM private key
	label              string // connection-string and invitation --name: the display name of the server (optional)
	kind               string // account add / account kind / invitation / shared-invitation: member|guest; grant add: resource|broadcast
	targetID           string // grant targetId
	invitation         string // connection-string --inv
	expectLogin        string // invitation create --expect-login: the login the person is expected to take (a hint)
	limDevices         *int
	limResources       *int
	limBroadcast       *int
	limBroadcastDevice *int
	limClipboard       *int64
	maxAccounts        *int // policy set --max-accounts
	allowedAddresses   string
	sessionLifetime    int
	webDir             *string      // admin set --web-dir: the directory the page is served from; "-" or "none" clears it
	origins            *string      // admin set --origins: the whole list of origins that may open a stream; "-" or "none" clears it
	originEdit         *originEdits // admin set --origin / --remove-origin: single entries added to or removed from that list
	wsMaxMessage       *int         // admin set --ws-max-message: bytes, 0 = the default
}

// originEdits are the repeatable --origin and --remove-origin options of admin set, in the order given.
type originEdits struct{ add, remove []string }

// limits is the set of per-account limits the command line names, nil when it names none.
func (c command) limits() *accounts.AccountLimits {
	l := &accounts.AccountLimits{DevicesPerAccount: c.limDevices, ResourcesPerAccount: c.limResources,
		BroadcastsPerAccount: c.limBroadcast, BroadcastsPerDevice: c.limBroadcastDevice, ClipboardMaxBytes: c.limClipboard}
	if l.IsEmpty() {
		return nil
	}
	return l
}

const (
	usageAccount = "account add <login> [--generate] [--kind member|guest] | passwd <login> [--generate] | kind <login> member|guest | remove <login> | list [--json] | " +
		"limits <login> [--devices N] [--resources N] [--broadcasts N] [--broadcasts-per-device N] [--clipboard-bytes N] [--reset]"
	usageDevice           = "device list <login> [--json] | revoke <login> <deviceId>"
	usageInvitation       = "invitation create [--kind member|guest] [--expect-login <login>] [--name <label>] [--json] | list [--json] | withdraw <hashPrefix>"
	usageSharedInvitation = "shared-invitation set [--kind member|guest] [--generate] | off | status [--json]"
	usageGrant            = "grant add <member> <guest> <targetId> [--kind resource|broadcast] | remove <member> <guest> <targetId> | list <login> [--json]"
	usagePolicy           = "policy show [--json] | set [--max-accounts N] [--devices N] [--resources N] [--broadcasts N] [--broadcasts-per-device N] [--clipboard-bytes N]"
	usageTLS              = "tls renew | replace | import --cert <file> --key <file>"
	usageAdmin            = "admin on [--generate] [--allowed <list>] [--lifetime N] | off | passwd [--generate] | " +
		"set [--allowed <list>] [--lifetime N] [--web-dir <dir|->] [--origins <list|->] [--origin <origin>].. [--remove-origin <origin>].. [--ws-max-message <bytes>] | status [--json]"
)

// numberOption reads the value of a numeric option, in either `--name=3` or `--name 3` form.
func numberOption(rest []string, i *int, name, val string, hasVal bool) (int64, error) {
	if !hasVal {
		if *i+1 >= len(rest) {
			return 0, fmt.Errorf("%s needs a number", name)
		}
		*i++
		val = rest[*i]
	}
	n, err := strconv.ParseInt(val, 10, 64)
	if err != nil || n < 0 {
		return 0, fmt.Errorf("%s must be a non-negative integer", name)
	}
	return n, nil
}

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
	case "serve", "stop", "set-listen", "set-endpoint", "set-port-range", "set-keep-awake", "set-log-retention", "set-max-accounts", "inspect", "account", "device", "tls", "connection-string", "invitation", "shared-invitation", "grant", "policy", "admin":
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
		case "--name", "--login", "--inv", "--expect-login":
			ok := false
			switch name {
			case "--name":
				ok = c.name == "connection-string" || c.name == "invitation"
			case "--expect-login":
				ok = c.name == "invitation"
			default:
				ok = c.name == "connection-string"
			}
			if !ok {
				return c, fmt.Errorf("unknown option %q for %s", name, c.name)
			}
			if !hasVal {
				if i+1 >= len(rest) {
					return c, fmt.Errorf("%s needs a value", name)
				}
				i++
				val = rest[i]
			}
			if val == "" {
				return c, fmt.Errorf("%s needs a value", name)
			}
			switch name {
			case "--name":
				c.label = val
			case "--login":
				c.login = val
			case "--expect-login":
				l, ok := accounts.NormalizeLogin(val)
				if !ok {
					return c, fmt.Errorf("--expect-login %q is not a login", val)
				}
				c.expectLogin = l
			default:
				c.invitation = val
			}
		case "--kind":
			if c.name != "invitation" && c.name != "shared-invitation" && c.name != "account" && c.name != "grant" {
				return c, fmt.Errorf("unknown option %q for %s", name, c.name)
			}
			if !hasVal {
				if i+1 >= len(rest) {
					return c, errors.New("--kind needs a value")
				}
				i++
				val = rest[i]
			}
			if c.name == "grant" {
				if val != accounts.GrantResource && val != accounts.GrantBroadcast {
					return c, fmt.Errorf("invalid --kind %q: a grant is a resource or a broadcast", val)
				}
				c.kind = val
				break
			}
			k, err := accounts.NormalizeKind(val)
			if err != nil || val == "" {
				return c, fmt.Errorf("invalid --kind %q: an account is a member or a guest", val)
			}
			c.kind = k
		case "--devices", "--resources", "--broadcasts", "--broadcasts-per-device", "--clipboard-bytes", "--max-accounts":
			if (c.name != "account" && c.name != "policy") || (name == "--max-accounts" && c.name != "policy") {
				return c, fmt.Errorf("unknown option %q for %s", name, c.name)
			}
			n, err := numberOption(rest, &i, name, val, hasVal)
			if err != nil {
				return c, err
			}
			intVal := int(n)
			switch name {
			case "--devices":
				c.limDevices = &intVal
			case "--resources":
				c.limResources = &intVal
			case "--broadcasts":
				c.limBroadcast = &intVal
			case "--broadcasts-per-device":
				c.limBroadcastDevice = &intVal
			case "--clipboard-bytes":
				c.limClipboard = &n
			case "--max-accounts":
				if n < 1 {
					return c, errors.New("--max-accounts must be at least 1")
				}
				c.maxAccounts = &intVal
			}
		case "--generate":
			if (c.name != "account" && c.name != "shared-invitation" && c.name != "admin") || hasVal {
				return c, fmt.Errorf("unknown option %q for %s", a, c.name)
			}
			c.generate = true
		case "--sign-out":
			if c.name != "account" || hasVal {
				return c, fmt.Errorf("unknown option %q for %s", a, c.name)
			}
			c.signOut = true
		case "--reset":
			if c.name != "account" || hasVal {
				return c, fmt.Errorf("unknown option %q for %s", a, c.name)
			}
			c.reset = true
		case "--json":
			if (c.name != "account" && c.name != "device" && c.name != "invitation" && c.name != "shared-invitation" && c.name != "grant" && c.name != "policy" && c.name != "admin") || hasVal {
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
		case "--allowed-addresses", "--allowed":
			if c.name != "admin" {
				return c, fmt.Errorf("unknown option %q for %s", name, c.name)
			}
			if !hasVal {
				if i+1 >= len(rest) {
					return c, fmt.Errorf("%s needs an address list", name)
				}
				i++
				val = rest[i]
			}
			c.allowedAddresses = val
		case "--session-lifetime", "--lifetime":
			if c.name != "admin" {
				return c, fmt.Errorf("unknown option %q for %s", name, c.name)
			}
			if !hasVal {
				if i+1 >= len(rest) {
					return c, fmt.Errorf("%s needs a number of minutes", name)
				}
				i++
				val = rest[i]
			}
			mins, err := strconv.Atoi(val)
			if err != nil || mins <= 0 {
				return c, fmt.Errorf("%s needs a positive integer", name)
			}
			c.sessionLifetime = mins
		case "--web-dir", "--origins", "--origin", "--remove-origin", "--ws-max-message":
			if c.name != "admin" {
				return c, fmt.Errorf("unknown option %q for %s", name, c.name)
			}
			if name == "--ws-max-message" {
				n, err := numberOption(rest, &i, name, val, hasVal)
				if err != nil {
					return c, err
				}
				if n > math.MaxInt32 {
					return c, fmt.Errorf("%s is too large", name)
				}
				v := int(n)
				c.wsMaxMessage = &v
				break
			}
			if !hasVal {
				if i+1 >= len(rest) {
					return c, fmt.Errorf("%s needs a value", name)
				}
				i++
				val = rest[i]
			}
			if val == "" {
				return c, fmt.Errorf("%s needs a value", name)
			}
			text := val
			switch name {
			case "--web-dir":
				c.webDir = &text
			case "--origins":
				c.origins = &text
			default:
				if c.originEdit == nil {
					c.originEdit = &originEdits{}
				}
				if name == "--origin" {
					c.originEdit.add = append(c.originEdit.add, val)
				} else {
					c.originEdit.remove = append(c.originEdit.remove, val)
				}
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
	case "set-listen", "set-endpoint", "set-port-range", "set-keep-awake", "set-log-retention", "set-max-accounts":
		if len(pos) != 1 {
			switch c.name {
			case "set-listen":
				return c, errors.New("set-listen needs an address such as :22022 or 0.0.0.0:22022")
			case "set-port-range":
				return c, errors.New("set-port-range needs the third-party port range, such as 40000-40100")
			case "set-max-accounts":
				return c, errors.New("set-max-accounts needs a positive integer")
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
		case "replace", "renew":
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
		case "add", "passwd", "remove", "limits":
			if len(pos) != 2 {
				return c, fmt.Errorf("account %s needs a login: %s", c.sub, usageAccount)
			}
			c.login = pos[1]
		case "kind":
			if len(pos) != 3 {
				return c, fmt.Errorf("account kind needs a login and a kind: %s", usageAccount)
			}
			c.login = pos[1]
			k, err := accounts.NormalizeKind(pos[2])
			if err != nil || pos[2] == "" {
				return c, fmt.Errorf("invalid kind %q: an account is a member or a guest", pos[2])
			}
			c.kind = k
		case "list":
			if len(pos) != 1 {
				return c, errors.New("account list takes no argument")
			}
		default:
			return c, fmt.Errorf("unknown account subcommand %q: %s", c.sub, usageAccount)
		}
		hasLimits := c.limits() != nil
		if (c.generate && c.sub != "add" && c.sub != "passwd") || (c.signOut && c.sub != "passwd") || (c.json && c.sub != "list" && c.sub != "limits") ||
			(c.kind != "" && c.sub != "add" && c.sub != "kind") || ((hasLimits || c.reset) && c.sub != "limits") ||
			c.maxAccounts != nil {
			return c, fmt.Errorf("an option does not belong to account %s: %s", c.sub, usageAccount)
		}
		if c.reset && hasLimits {
			return c, errors.New("--reset drops the account's own limits: it cannot be combined with a limit")
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
	case "invitation":
		if len(pos) == 0 {
			return c, errors.New("invitation needs a subcommand: " + usageInvitation)
		}
		c.sub = pos[0]
		switch c.sub {
		case "create", "list":
			if len(pos) != 1 {
				return c, fmt.Errorf("invitation %s takes no argument", c.sub)
			}
		case "withdraw":
			if len(pos) != 2 {
				return c, fmt.Errorf("invitation withdraw needs a codeHash or prefix: %s", usageInvitation)
			}
			c.value = pos[1]
		default:
			return c, fmt.Errorf("unknown invitation subcommand %q: %s", c.sub, usageInvitation)
		}
		if ((c.kind != "" || c.expectLogin != "" || c.label != "") && c.sub != "create") || (c.json && c.sub != "list" && c.sub != "create") {
			return c, fmt.Errorf("an option does not belong to invitation %s: %s", c.sub, usageInvitation)
		}
	case "shared-invitation":
		if len(pos) == 0 {
			return c, errors.New("shared-invitation needs a subcommand: " + usageSharedInvitation)
		}
		c.sub = pos[0]
		switch c.sub {
		case "set", "off", "status":
			if len(pos) != 1 {
				return c, fmt.Errorf("shared-invitation %s takes no argument", c.sub)
			}
		default:
			return c, fmt.Errorf("unknown shared-invitation subcommand %q: %s", c.sub, usageSharedInvitation)
		}
		if (c.generate && c.sub != "set") || (c.kind != "" && c.sub != "set") || (c.json && c.sub != "status") {
			return c, fmt.Errorf("an option does not belong to shared-invitation %s: %s", c.sub, usageSharedInvitation)
		}
	case "grant":
		if len(pos) == 0 {
			return c, errors.New("grant needs a subcommand: " + usageGrant)
		}
		c.sub = pos[0]
		switch c.sub {
		case "add", "remove":
			if len(pos) != 4 {
				return c, fmt.Errorf("grant %s needs a member, a guest and a targetId: %s", c.sub, usageGrant)
			}
			c.login, c.guest, c.targetID = pos[1], pos[2], pos[3]
		case "list":
			if len(pos) != 2 {
				return c, fmt.Errorf("grant list needs a login: %s", usageGrant)
			}
			c.login = pos[1]
		default:
			return c, fmt.Errorf("unknown grant subcommand %q: %s", c.sub, usageGrant)
		}
		if (c.kind != "" && c.sub != "add") || (c.json && c.sub != "list") {
			return c, fmt.Errorf("an option does not belong to grant %s: %s", c.sub, usageGrant)
		}
	case "policy":
		if len(pos) == 0 {
			return c, errors.New("policy needs a subcommand: " + usagePolicy)
		}
		c.sub = pos[0]
		if len(pos) != 1 {
			return c, fmt.Errorf("policy %s takes no argument", c.sub)
		}
		switch c.sub {
		case "show":
			if c.maxAccounts != nil || c.limits() != nil {
				return c, fmt.Errorf("an option does not belong to policy show: %s", usagePolicy)
			}
		case "set":
			if c.maxAccounts == nil && c.limits() == nil {
				return c, fmt.Errorf("policy set needs at least one option: %s", usagePolicy)
			}
		default:
			return c, fmt.Errorf("unknown policy subcommand %q: %s", c.sub, usagePolicy)
		}
	case "admin":
		if len(pos) == 0 {
			return c, errors.New("admin needs a subcommand: " + usageAdmin)
		}
		c.sub = pos[0]
		switch c.sub {
		case "on", "off", "passwd", "set-password", "status", "set":
			if len(pos) > 1 {
				return c, fmt.Errorf("unexpected argument %q: %s", pos[1], usageAdmin)
			}
		default:
			return c, fmt.Errorf("unknown admin subcommand %q: %s", c.sub, usageAdmin)
		}
		if c.generate && c.sub != "on" && c.sub != "passwd" && c.sub != "set-password" {
			return c, fmt.Errorf("--generate does not belong to admin %s: %s", c.sub, usageAdmin)
		}
		if (c.allowedAddresses != "" || c.sessionLifetime > 0) && c.sub != "on" && c.sub != "set" {
			return c, fmt.Errorf("--allowed and --lifetime belong to admin on or admin set: %s", usageAdmin)
		}
		if (c.webDir != nil || c.origins != nil || c.originEdit != nil || c.wsMaxMessage != nil) && c.sub != "set" {
			return c, fmt.Errorf("--web-dir, --origins, --origin, --remove-origin and --ws-max-message belong to admin set: %s", usageAdmin)
		}
		if c.origins != nil && c.originEdit != nil {
			return c, fmt.Errorf("--origins replaces the whole list: do not combine it with --origin or --remove-origin: %s", usageAdmin)
		}
		if c.json && c.sub != "status" {
			return c, fmt.Errorf("--json does not belong to admin %s: %s", c.sub, usageAdmin)
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
	printf(w, "      (a password is read from stdin, or --generate makes one that meets the policy and prints it ONCE;\n")
	printf(w, "       a reset signs every device of the account out - the devices stay, their next sign-in is refused)\n")
	printf(w, "  fms-exchange-server %s [--config <file>]\n", usageDevice)
	printf(w, "  fms-exchange-server %s [--config <file>]\n", usageInvitation)
	printf(w, "      (the code is shown ONCE; the server keeps only its hash)\n")
	printf(w, "  fms-exchange-server %s [--config <file>]\n", usageSharedInvitation)
	printf(w, "  fms-exchange-server %s [--config <file>]\n", usageGrant)
	printf(w, "  fms-exchange-server %s [--config <file>]\n", usagePolicy)
	printf(w, "  fms-exchange-server %s [--config <file>]\n", usageAdmin)
	printf(w, "      (--web-dir serves a directory at / on the TLS port, - clears it; --origins / --origin list the browser origins that may open a\n")
	printf(w, "       WebSocket - a listed origin is a consumer's, never an operator's; --ws-max-message is the WebSocket message cap in bytes, 0 = default)\n")
	printf(w, "  fms-exchange-server set-listen [--config <file>] <host:port>   (the listen address, e.g. :22022)\n")
	printf(w, "  fms-exchange-server set-endpoint [--config <file>] <host:port|->   (the public address announced in welcome)\n")
	printf(w, "  fms-exchange-server set-port-range [--config <file>] <lo-hi>   (the third-party port range, e.g. 40000-40100)\n")
	printf(w, "  fms-exchange-server set-keep-awake [--config <file>] <on|off>\n")
	printf(w, "  fms-exchange-server set-log-retention [--config <file>] <days>\n")
	printf(w, "  fms-exchange-server set-max-accounts [--config <file>] <n>   (applies at the next start; `policy set` applies at once)\n")
	printf(w, "  fms-exchange-server %s [--config <file>]\n", usageTLS)
	printf(w, "      (a new self-signed identity, or a certificate and key to serve; applies at the next start)\n")
	printf(w, "  fms-exchange-server inspect [--config <file>] [--init]\n")
	printf(w, "  fms-exchange-server --version\n")
}

func printf(w io.Writer, format string, a ...any) { _, _ = fmt.Fprintf(w, format, a...) }
