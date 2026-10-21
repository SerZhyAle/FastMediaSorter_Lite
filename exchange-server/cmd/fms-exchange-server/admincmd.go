package main

import (
	"bufio"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
	"unicode/utf8"

	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/password"
)

// runAdmin handles the admin subcommands (DEVICE-EXCHANGE 1.4, ticket 245).
func runAdmin(cmd command, stdin io.Reader, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	cfg, err := config.Load(paths.Config)
	if errors.Is(err, os.ErrNotExist) {
		cfg, err = config.Default(), nil
	}
	if err != nil {
		printf(stderr, "cannot read config: %v\n", err)
		return exitConfig
	}

	switch cmd.sub {
	case "status":
		if cmd.json {
			out := struct {
				On                     bool     `json:"on"`
				PasswordSet            bool     `json:"passwordSet"`
				AllowedAddresses       []string `json:"allowedAddresses,omitempty"`
				Origins                []string `json:"origins,omitempty"`
				SessionLifetimeMinutes int      `json:"sessionLifetimeMinutes"`
				PasswordFloor          int      `json:"passwordFloor"`
				WebDir                 string   `json:"webDir,omitempty"`
				WebSocketMessageBytes  int      `json:"webSocketMessageBytes"`
			}{
				On:                     cfg.Admin.On,
				PasswordSet:            cfg.Admin.PasswordRecord != "",
				AllowedAddresses:       cfg.Admin.AllowedAddresses,
				Origins:                cfg.Admin.Origins,
				SessionLifetimeMinutes: cfg.Admin.SessionLifetimeMinutes,
				PasswordFloor:          cfg.Admin.PasswordFloor,
				WebDir:                 cfg.WebDir,
				WebSocketMessageBytes:  cfg.Limits.WebSocketMessage(),
			}
			b, _ := json.MarshalIndent(out, "", "  ")
			printf(stdout, "%s\n", b)
			return exitOK
		}
		statusStr := "off"
		if cfg.Admin.On {
			statusStr = "on"
		}
		pwStr := "not set"
		if cfg.Admin.PasswordRecord != "" {
			pwStr = "set"
		}
		printf(stdout, "admin %s\n", statusStr)
		printf(stdout, "password %s\n", pwStr)
		if len(cfg.Admin.AllowedAddresses) > 0 {
			printf(stdout, "allowed addresses %s\n", strings.Join(cfg.Admin.AllowedAddresses, ", "))
		}
		if len(cfg.Admin.Origins) > 0 {
			printf(stdout, "origins %s\n", strings.Join(cfg.Admin.Origins, ", "))
		}
		printf(stdout, "session lifetime %d minutes\n", cfg.Admin.SessionLifetimeMinutes)
		printf(stdout, "password floor %d characters\n", cfg.Admin.PasswordFloor)
		if cfg.WebDir != "" {
			printf(stdout, "web dir %s\n", cfg.WebDir)
		}
		printf(stdout, "websocket message cap %d bytes\n", cfg.Limits.WebSocketMessage())
		return exitOK

	case "off":
		cfg.Admin.On = false
		if err := cfg.Validate(); err != nil {
			printf(stderr, "%v\n", err)
			return exitUsage
		}
		if err := config.Save(paths.Config, cfg); err != nil {
			printf(stderr, "cannot write config: %v\n", err)
			return exitConfig
		}
		printf(stdout, "remote administration turned off; applies on next start\n")
		return exitOK

	case "on":
		cfg.Admin.On = true
		var generated string
		if cmd.generate {
			p, err := password.Generate()
			if err != nil {
				printf(stderr, "cannot generate password: %v\n", err)
				return exitFailure
			}
			generated = p
			if err := checkPasswordPolicy(p, cfg.Admin.PasswordFloor); err != nil {
				printf(stderr, "%v\n", err)
				return exitPolicy
			}
			hash, err := password.Hash(p)
			if err != nil {
				printf(stderr, "cannot hash password: %v\n", err)
				return exitFailure
			}
			cfg.Admin.PasswordRecord = hash
		} else if cfg.Admin.PasswordRecord == "" {
			line, err := bufio.NewReaderSize(stdin, 4096).ReadString('\n')
			if err != nil && err != io.EOF {
				printf(stderr, "cannot read password from stdin: %v\n", err)
				return exitUsage
			}
			pw := strings.TrimPrefix(strings.TrimRight(line, "\r\n"), "\ufeff")
			if pw == "" {
				printf(stderr, "admin on requires a password: provide it on stdin or pass --generate\n")
				return exitUsage
			}
			if err := checkPasswordPolicy(pw, cfg.Admin.PasswordFloor); err != nil {
				printf(stderr, "%v\n", err)
				return exitPolicy
			}
			hash, err := password.Hash(pw)
			if err != nil {
				printf(stderr, "cannot hash password: %v\n", err)
				return exitFailure
			}
			cfg.Admin.PasswordRecord = hash
		}
		if cmd.allowedAddresses != "" {
			if cmd.allowedAddresses == "-" || strings.EqualFold(cmd.allowedAddresses, "none") {
				cfg.Admin.AllowedAddresses = nil
			} else {
				parts := strings.Split(cmd.allowedAddresses, ",")
				var addrs []string
				for _, p := range parts {
					p = strings.TrimSpace(p)
					if p != "" {
						addrs = append(addrs, p)
					}
				}
				cfg.Admin.AllowedAddresses = addrs
			}
		}
		if cmd.sessionLifetime > 0 {
			cfg.Admin.SessionLifetimeMinutes = cmd.sessionLifetime
		}
		if err := cfg.Validate(); err != nil {
			printf(stderr, "%v\n", err)
			return exitUsage
		}
		if err := config.Save(paths.Config, cfg); err != nil {
			printf(stderr, "cannot write config: %v\n", err)
			return exitConfig
		}
		if generated != "" {
			printf(stdout, "%s\n", generated)
		}
		printf(stdout, "remote administration turned on; applies on next start\n")
		return exitOK

	case "set":
		if cmd.allowedAddresses != "" {
			if cmd.allowedAddresses == "-" || strings.EqualFold(cmd.allowedAddresses, "none") {
				cfg.Admin.AllowedAddresses = nil
			} else {
				parts := strings.Split(cmd.allowedAddresses, ",")
				var addrs []string
				for _, p := range parts {
					p = strings.TrimSpace(p)
					if p != "" {
						addrs = append(addrs, p)
					}
				}
				cfg.Admin.AllowedAddresses = addrs
			}
		}
		if cmd.sessionLifetime > 0 {
			cfg.Admin.SessionLifetimeMinutes = cmd.sessionLifetime
		}
		if cmd.webDir != nil {
			if *cmd.webDir == "-" || strings.EqualFold(*cmd.webDir, "none") {
				cfg.WebDir = ""
			} else {
				// stored absolute: the server may run from another working directory (a service starts in System32)
				abs, err := filepath.Abs(*cmd.webDir)
				if err != nil {
					printf(stderr, "cannot resolve --web-dir: %v\n", err)
					return exitUsage
				}
				cfg.WebDir = abs
			}
		}
		if cmd.origins != nil {
			cfg.Admin.Origins = nil
			if *cmd.origins != "-" && !strings.EqualFold(*cmd.origins, "none") {
				for _, o := range strings.Split(*cmd.origins, ",") {
					if o = strings.TrimSpace(o); o != "" {
						cfg.Admin.Origins = appendOrigin(cfg.Admin.Origins, o)
					}
				}
			}
		}
		if cmd.originEdit != nil {
			for _, o := range cmd.originEdit.add {
				cfg.Admin.Origins = appendOrigin(cfg.Admin.Origins, strings.TrimSpace(o))
			}
			for _, o := range cmd.originEdit.remove {
				var kept []string
				for _, have := range cfg.Admin.Origins {
					if !strings.EqualFold(have, strings.TrimSpace(o)) {
						kept = append(kept, have)
					}
				}
				if len(kept) == len(cfg.Admin.Origins) {
					printf(stderr, "origin %q is not in the list\n", o)
					return exitUsage
				}
				cfg.Admin.Origins = kept
			}
		}
		if cmd.wsMaxMessage != nil {
			cfg.Limits.WebSocketMessageBytes = *cmd.wsMaxMessage
		}
		// ValidateIn names the data directory, so a web directory that is, holds or lies in it (or holds the private
		// key) is refused here, before it is written; so is an origin that is not a plain https origin.
		if err := cfg.ValidateIn(paths); err != nil {
			printf(stderr, "%v\n", err)
			return exitUsage
		}
		if err := config.Save(paths.Config, cfg); err != nil {
			printf(stderr, "cannot write config: %v\n", err)
			return exitConfig
		}
		printf(stdout, "admin settings updated; applies on next start\n")
		return exitOK

	case "passwd", "set-password":
		var generated string
		var pw string
		if cmd.generate {
			p, err := password.Generate()
			if err != nil {
				printf(stderr, "cannot generate password: %v\n", err)
				return exitFailure
			}
			generated = p
			pw = p
		} else {
			line, err := bufio.NewReaderSize(stdin, 4096).ReadString('\n')
			if err != nil && err != io.EOF {
				printf(stderr, "cannot read password from stdin: %v\n", err)
				return exitUsage
			}
			pw = strings.TrimPrefix(strings.TrimRight(line, "\r\n"), "\ufeff")
			if pw == "" {
				printf(stderr, "admin passwd requires a password: provide it on stdin or pass --generate\n")
				return exitUsage
			}
		}
		if err := checkPasswordPolicy(pw, cfg.Admin.PasswordFloor); err != nil {
			printf(stderr, "%v\n", err)
			return exitPolicy
		}
		hash, err := password.Hash(pw)
		if err != nil {
			printf(stderr, "cannot hash password: %v\n", err)
			return exitFailure
		}
		cfg.Admin.PasswordRecord = hash
		if err := cfg.Validate(); err != nil {
			printf(stderr, "%v\n", err)
			return exitUsage
		}
		if err := config.Save(paths.Config, cfg); err != nil {
			printf(stderr, "cannot write config: %v\n", err)
			return exitConfig
		}
		if generated != "" {
			printf(stdout, "%s\n", generated)
		}
		printf(stdout, "admin password changed; applies on next start\n")
		return exitOK
	}
	return exitUsage
}

// appendOrigin adds an origin to the list unless it is already there (case-insensitive, as the host of an
// origin is); the list is validated as a whole by Config.Validate.
func appendOrigin(list []string, origin string) []string {
	for _, have := range list {
		if strings.EqualFold(have, origin) {
			return list
		}
	}
	return append(list, origin)
}

func checkPasswordPolicy(pw string, floor int) error {
	if err := password.Check(pw); err != nil {
		return err
	}
	if floor > password.MinLength && utf8.RuneCountInString(pw) < floor {
		return fmt.Errorf("the password must be at least %d characters long", floor)
	}
	return nil
}
