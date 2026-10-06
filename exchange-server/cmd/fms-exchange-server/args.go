package main

import (
	"errors"
	"fmt"
	"io"
	"strings"
)

// command is a parsed command line. The parser is hand-written, the worker's style: an unknown
// flag is an error, never silently ignored.
type command struct {
	name     string // version | serve | set-password | set-listen | inspect | stop
	config   string
	generate bool   // set-password --generate
	init     bool   // inspect --init
	service  bool   // serve --service (under the Windows service control manager)
	value    string // set-listen: the host:port to store
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
	case "serve", "set-password", "set-listen", "inspect", "stop":
		c.name = args[0]
	default:
		return c, fmt.Errorf("unknown command %q", args[0])
	}
	rest := args[1:]
	for i := 0; i < len(rest); i++ {
		a := rest[i]
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
			if c.name != "set-password" || hasVal {
				return c, fmt.Errorf("unknown option %q for %s", a, c.name)
			}
			c.generate = true
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
		default:
			if c.name == "set-listen" && c.value == "" && !strings.HasPrefix(a, "-") {
				c.value = a
				continue
			}
			return c, fmt.Errorf("unknown option %q for %s", a, c.name)
		}
	}
	if c.name == "set-listen" && c.value == "" {
		return c, errors.New("set-listen needs an address such as :22022 or 0.0.0.0:22022")
	}
	return c, nil
}

func printUsage(w io.Writer, err error) {
	printf(w, "error: %v\n", err)
	printf(w, "usage:\n")
	printf(w, "  fms-exchange-server serve [--config <file>] [--service]   (--service: only under the Windows service control manager)\n")
	printf(w, "  fms-exchange-server stop [--config <file>]                (asks the console server of that config to stop cleanly)\n")
	printf(w, "  fms-exchange-server set-password [--config <file>] [--generate]   (reads the password from stdin)\n")
	printf(w, "  fms-exchange-server set-listen [--config <file>] <host:port>   (the rendezvous listen address, e.g. :22022)\n")
	printf(w, "  fms-exchange-server inspect [--config <file>] [--init]\n")
	printf(w, "  fms-exchange-server --version\n")
}

func printf(w io.Writer, format string, a ...any) { _, _ = fmt.Fprintf(w, format, a...) }
