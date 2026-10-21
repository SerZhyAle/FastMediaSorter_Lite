package main

import (
	"io"
	"os"

	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/logx"
)

func stateMutation(op string) bool {
	switch op {
	case admin.OpAccountAdd, admin.OpAccountPasswd, admin.OpAccountKind, admin.OpAccountRemove,
		admin.OpDeviceRevoke, admin.OpInvitationCreate, admin.OpInvitationWithdraw,
		admin.OpSharedInvitationSet, admin.OpAccountLimits, admin.OpPolicySet, admin.OpGrantAdd, admin.OpGrantRemove:
		return true
	}
	return false
}

// logConfigCommand records CLI configuration changes even while serve is stopped.
// The allow-list contains commands only: no arguments, passwords, keys or output.
func logConfigCommand(cmd command, code int, stderr io.Writer) {
	op := cmd.name
	switch cmd.name {
	case "set-listen", "set-endpoint", "set-port-range", "set-keep-awake", "set-log-retention", "set-max-accounts":
	case "tls":
		if cmd.sub != "renew" && cmd.sub != "replace" && cmd.sub != "import" {
			return
		}
		op += "." + cmd.sub
	case "admin":
		switch cmd.sub {
		case "on", "off", "passwd", "set":
			op += "." + cmd.sub
		default:
			return
		}
	default:
		return
	}
	paths := config.Resolve(cmd.config, os.Getenv)
	// A failed command must not create a data directory or change disk state for logging.
	if code != exitOK {
		return
	}
	cfg, err := config.Load(paths.Config)
	if err != nil {
		cfg = config.Default()
	}
	if err := os.MkdirAll(paths.LogsDir, 0o700); err != nil {
		printf(stderr, "state log unavailable after configuration change\n")
		return
	}
	logger, err := logx.OpenDaily(paths.LogsDir, cfg.Log.MaxBytes, cfg.Log.RetentionDays, nil)
	if err != nil {
		printf(stderr, "state log unavailable after configuration change\n")
		return
	}
	defer logger.Close()
	logger.State("configuration-changed", logx.Text("operation", op))
}
