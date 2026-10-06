// fms-exchange-server is the exchange server of the Fast Media Sorter package: rendezvous and relay
// for anywhere access (contract ANYWHERE-ACCESS, specification 133). The binary prints no product
// name; the visible name belongs to the package's surfaces.
package main

import (
	"context"
	"io"
	"os"

	"fms/exchange-server/internal/deploy"
)

// version is stamped at build time: -ldflags "-X main.version=<YY.M.D.HHmm>".
var version = "dev"

// Exit codes. 0 ok, 1 a runtime failure (for `stop`: nothing is running to stop), 2 usage (also
// `serve --service` outside the service control manager), 3 a password the policy refuses or a config
// with no usable password, 4 a config that cannot be read or written, 5 a console `serve` refused
// because the Media Exchange Server service is running or holds the listen port (the service wins).
const (
	exitOK      = 0
	exitFailure = 1
	exitUsage   = 2
	exitPolicy  = 3
	exitConfig  = 4
	exitService = 5
)

func main() {
	// Signals only: serve adds the named stop event itself, once it knows which config it serves.
	ctx, stop, _ := deploy.NotifyStop(context.Background(), "")
	defer stop()
	os.Exit(run(ctx, os.Args[1:], os.Stdin, os.Stdout, os.Stderr))
}

func run(ctx context.Context, args []string, stdin io.Reader, stdout, stderr io.Writer) int {
	cmd, err := parseArgs(args)
	if err != nil {
		printUsage(stderr, err)
		return exitUsage
	}
	switch cmd.name {
	case "version":
		printf(stdout, "%s\n", version)
		return exitOK
	case "serve":
		return runServe(ctx, cmd, stdout, stderr)
	case "set-password":
		return runSetPassword(cmd, stdin, stdout, stderr)
	case "set-listen":
		return runSetListen(cmd, stdout, stderr)
	case "inspect":
		return runInspect(cmd, stdout, stderr)
	case "stop":
		return runStop(cmd, stdout, stderr)
	}
	return exitUsage
}
