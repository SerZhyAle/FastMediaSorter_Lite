package main

import (
	"bufio"
	"errors"
	"io"
	"os"
	"strings"

	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/password"
)

// runSetPassword stores the Argon2id record of a password in the config. The password comes from
// stdin (never argv, which any process list shows); --generate makes one that meets the policy,
// stores its hash and prints the plaintext ONCE on stdout. Both refuse a weak password with exit 3.
func runSetPassword(cmd command, stdin io.Reader, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	cfg, err := config.Load(paths.Config)
	if err != nil && !errors.Is(err, os.ErrNotExist) {
		printf(stderr, "cannot read the config: %v\n", err)
		return exitConfig
	}
	if errors.Is(err, os.ErrNotExist) {
		cfg = config.Default()
	}

	var plain string
	if cmd.generate {
		plain, err = password.Generate()
		if err != nil {
			printf(stderr, "cannot generate a password: %v\n", err)
			return exitFailure
		}
	} else {
		line, err := bufio.NewReaderSize(stdin, 4096).ReadString('\n')
		if err != nil && err != io.EOF {
			printf(stderr, "cannot read the password from stdin: %v\n", err)
			return exitUsage
		}
		plain = strings.TrimRight(line, "\r\n")
		if err := password.Check(plain); err != nil {
			printf(stderr, "%v\n", err)
			return exitPolicy
		}
	}

	rec, err := password.Hash(plain)
	if err != nil {
		printf(stderr, "cannot hash the password: %v\n", err)
		return exitFailure
	}
	cfg.PasswordHash = rec
	if err := config.Save(paths.Config, cfg); err != nil {
		printf(stderr, "cannot write the config: %v\n", err)
		return exitConfig
	}
	if cmd.generate {
		printf(stdout, "%s\n", plain)
	}
	return exitOK
}
