package main

import (
	"errors"
	"io"
	"os"

	"fms/exchange-server/internal/config"
)

// runSetEndpoint stores the public host:port announced in `welcome` (ticket 163 rule 1), leaving every
// other member as it is; "-" clears it. A UI sets the endpoint through this command rather than
// editing the JSON, so the value is validated by the rules `serve` applies. A running server reads the
// file only at start: the caller restarts it.
func runSetEndpoint(cmd command, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	cfg, err := config.Load(paths.Config)
	if err != nil && !errors.Is(err, os.ErrNotExist) {
		printf(stderr, "cannot read the config: %v\n", err)
		return exitConfig
	}
	if errors.Is(err, os.ErrNotExist) {
		cfg = config.Default()
	}
	if cmd.value == "-" {
		cfg.PublicEndpoint = ""
	} else {
		cfg.PublicEndpoint = cmd.value
	}
	if err := cfg.Validate(); err != nil {
		printf(stderr, "%v\n", err)
		return exitUsage
	}
	if err := config.Save(paths.Config, cfg); err != nil {
		printf(stderr, "cannot write the config: %v\n", err)
		return exitConfig
	}
	printf(stdout, "public endpoint %s\n", cfg.PublicEndpoint)
	return exitOK
}
