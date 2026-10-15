package main

import (
	"errors"
	"fms/exchange-server/internal/config"
	"io"
	"os"
	"strconv"
)

// Host settings take effect on the next start, like the other config commands.
func runSetHost(cmd command, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	cfg, err := config.Load(paths.Config)
	if errors.Is(err, os.ErrNotExist) {
		cfg, err = config.Default(), nil
	}
	if err != nil {
		printf(stderr, "cannot read config: %v\n", err)
		return exitConfig
	}
	if cmd.name == "set-keep-awake" {
		if cmd.value != "on" && cmd.value != "off" {
			return exitUsage
		}
		cfg.KeepAwake = cmd.value == "on"
	} else if cmd.name == "set-max-accounts" {
		n, err := strconv.Atoi(cmd.value)
		if err != nil || n < 1 {
			return exitUsage
		}
		cfg.Limits.MaxAccounts = n
	} else {
		days, err := strconv.Atoi(cmd.value)
		if err != nil {
			return exitUsage
		}
		cfg.Log.RetentionDays = days
	}
	if err := cfg.Validate(); err != nil {
		printf(stderr, "%v\n", err)
		return exitUsage
	}
	if err := config.Save(paths.Config, cfg); err != nil {
		printf(stderr, "cannot write config: %v\n", err)
		return exitConfig
	}
	printf(stdout, "host setting saved; applies on next start\n")
	return exitOK
}
