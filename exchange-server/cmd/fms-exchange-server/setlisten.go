package main

import (
	"errors"
	"io"
	"os"

	"fms/exchange-server/internal/config"
)

// runSetListen stores the rendezvous listen address in the config, leaving every other member as
// it is. A UI changes the port through this command rather than editing the JSON itself, so the
// file is validated by the same rules `serve` applies. A running server reads the file only at
// start: the caller restarts it.
func runSetListen(cmd command, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	cfg, err := config.Load(paths.Config)
	if err != nil && !errors.Is(err, os.ErrNotExist) {
		printf(stderr, "cannot read the config: %v\n", err)
		return exitConfig
	}
	if errors.Is(err, os.ErrNotExist) {
		cfg = config.Default()
	}
	cfg.Listen = cmd.value
	if err := cfg.Validate(); err != nil {
		printf(stderr, "%v\n", err)
		return exitUsage
	}
	if err := config.Save(paths.Config, cfg); err != nil {
		printf(stderr, "cannot write the config: %v\n", err)
		return exitConfig
	}
	printf(stdout, "listen %s\n", cfg.Listen)
	return exitOK
}
