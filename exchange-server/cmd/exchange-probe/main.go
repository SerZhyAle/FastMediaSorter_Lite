// Command exchange-probe is the self-test client of ticket 205 (T-2): one scripted run of a
// Media Exchange Server deployment that prints ONE verdict line.
//
//	exchange-probe --server host:port --fingerprint SHA256:... --login alice --password-stdin
//
// It enrolls a device, says hello, publishes a resource, lists, enrolls a second device with a pairing
// code and subscribes it, opens a tunnel by resourceId and by shareId (a real SSH session goes through
// each), uploads to the relay and listens to it, casts between the two devices, and revokes a third
// one. Every device it enrolled is revoked again before it exits, so the account is left as it was.
//
// The server and the pin are parameters; nothing is built in. The account's password is read from the
// first line of standard input, never from the command line. The probe is a developer and joint-test
// tool: it does not ship in any package.
//
// Exit codes: 0 PASS; 1 FAIL (a step failed, named on the line); 2 could not run (usage, no pin, no
// password). The last and only line of standard output is the verdict.
package main

import (
	"bufio"
	"flag"
	"fmt"
	"io"
	"os"
	"strings"
	"time"
)

func main() { os.Exit(run(os.Args[1:], os.Stdin, os.Stdout, os.Stderr)) }

func run(args []string, stdin io.Reader, stdout, stderr io.Writer) int {
	fs := flag.NewFlagSet("exchange-probe", flag.ContinueOnError)
	fs.SetOutput(stderr)
	server := fs.String("server", "", "the exchange server, host:port (required)")
	pin := fs.String("fingerprint", "", "the server certificate's pin, SHA256:<base64> (required unless --trust-first-use)")
	trust := fs.Bool("trust-first-use", false, "pin whatever certificate the server presents, and print its fingerprint to standard error")
	login := fs.String("login", "", "the account to run in (required)")
	pwStdin := fs.Bool("password-stdin", false, "read the account's password from the first line of standard input (required)")
	skip := fs.String("skip", "", "comma-separated parts to leave out: tunnel, relay, cast (a skipped part is named on the verdict line)")
	timeout := fs.Duration("timeout", 90*time.Second, "give up after this long")
	verbose := fs.Bool("v", false, "print each step to standard error as it passes")
	if err := fs.Parse(args); err != nil {
		return 2
	}
	usage := func(why string) int {
		fmt.Fprintf(stdout, "exchange-probe: COULD NOT RUN - %s\n", why)
		return 2
	}
	switch {
	case *server == "":
		return usage("--server host:port is required")
	case *login == "":
		return usage("--login is required")
	case !*pwStdin:
		return usage("--password-stdin is required: the password is never taken from the command line")
	case *pin == "" && !*trust:
		return usage("no pin: pass --fingerprint SHA256:.. (the server prints it at start), or --trust-first-use to pin what it presents")
	}
	line, _ := bufio.NewReader(stdin).ReadString('\n')
	password := strings.TrimRight(line, "\r\n")
	if password == "" {
		return usage("the account's password is read from the first line of standard input and there was none")
	}
	skipped := map[string]bool{}
	for _, s := range strings.Split(*skip, ",") {
		if s = strings.TrimSpace(s); s != "" {
			if s != "tunnel" && s != "relay" && s != "cast" {
				return usage("--skip names tunnel, relay or cast, not " + s)
			}
			skipped[s] = true
		}
	}

	p := &prober{server: *server, pin: *pin, login: *login, password: password, skip: skipped, verbose: *verbose, log: stderr}
	if *trust && *pin == "" {
		fp, err := learnPin(*server)
		if err != nil {
			fmt.Fprintf(stdout, "exchange-probe: FAIL at step \"connect\": %v\n", err)
			return 1
		}
		fmt.Fprintf(stderr, "pinned %s\n", fp)
		p.pin = fp
	}

	done := make(chan error, 1)
	go func() { done <- p.run() }()
	var err error
	select {
	case err = <-done:
	case <-time.After(*timeout):
		err = &stepError{step: p.current(), err: fmt.Errorf("no answer within %v", *timeout)}
	}
	if err != nil {
		fmt.Fprintf(stdout, "exchange-probe: FAIL at step %q: %v\n", err.(*stepError).step, err.(*stepError).err)
		return 1
	}
	detail := strings.Join(p.passed, ", ")
	if len(skipped) > 0 {
		var s []string
		for _, k := range []string{"tunnel", "relay", "cast"} {
			if skipped[k] {
				s = append(s, k)
			}
		}
		detail += "; skipped: " + strings.Join(s, ", ")
	}
	fmt.Fprintf(stdout, "exchange-probe: PASS (%s)\n", detail)
	return 0
}
