package main

import (
	"errors"
	"io"
	"net"
	"os"
	"strconv"

	"fms/exchange-server/internal/config"
)

// runSetPortRange stores the third-party port range (ticket 164, set by the Share Manager's
// administration group, ticket 168 section 3.5), leaving every other member as it is. The range is
// validated by the rules `serve` applies (two ports, 1..65535, low first) and must not contain the
// rendezvous listen port: a third-party tunnel could then never be granted that port. A running
// server reads the file only at start: the caller restarts it.
func runSetPortRange(cmd command, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	cfg, err := config.Load(paths.Config)
	if err != nil && !errors.Is(err, os.ErrNotExist) {
		printf(stderr, "cannot read the config: %v\n", err)
		return exitConfig
	}
	if errors.Is(err, os.ErrNotExist) {
		cfg = config.Default()
	}
	lo, hi, err := config.ParsePortRange(cmd.value)
	if err != nil {
		printf(stderr, "%v\n", err)
		return exitUsage
	}
	if _, p, err := net.SplitHostPort(cfg.Listen); err == nil {
		if n, err := strconv.Atoi(p); err == nil && n >= lo && n <= hi {
			printf(stderr, "portRange %q contains the listen port %d: a third-party tunnel could never use it\n", cmd.value, n)
			return exitUsage
		}
	}
	cfg.PortRange = strconv.Itoa(lo) + "-" + strconv.Itoa(hi)
	if err := cfg.Validate(); err != nil {
		printf(stderr, "%v\n", err)
		return exitUsage
	}
	if err := config.Save(paths.Config, cfg); err != nil {
		printf(stderr, "cannot write the config: %v\n", err)
		return exitConfig
	}
	printf(stdout, "port range %s\n", cfg.PortRange)
	return exitOK
}
