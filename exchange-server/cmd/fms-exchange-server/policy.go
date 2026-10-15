package main

import (
	"encoding/json"
	"io"
	"os"
	"strconv"
	"sync"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/admin"
	"fms/exchange-server/internal/config"
)

// The deployment policy (ticket 224 rule 5): the cap on accounts and the limits a new account starts
// with. exchange.json states it; the accounts store holds the working copy and an operator's change goes
// to both, so a restart keeps it.

// configMu serialises the read-modify-write cycles of the config file inside this process: the host
// settings of the admin channel and the policy of `policy set` both rewrite the same file.
var configMu sync.Mutex

// policyOf is the policy the config states.
func policyOf(l config.Limits) accounts.Policy {
	return accounts.Policy{MaxAccounts: l.MaxAccounts, Defaults: accounts.Limits{
		Devices: l.DevicesPerAccount, Resources: l.ResourcesPerAccount, Broadcasts: l.BroadcastsPerAccount,
		BroadcastsPerDevice: l.BroadcastsPerDevice, ClipboardBytes: l.ClipboardBytes,
	}}
}

// saveLimitsTo is the Ops.SaveLimits that writes the policy into the config file at path. The rest of
// the file is read again under the lock, so a host setting changed meanwhile is not lost.
func saveLimitsTo(path string) func(accounts.Policy) error {
	return func(p accounts.Policy) error {
		configMu.Lock()
		defer configMu.Unlock()
		current, err := config.Load(path)
		if err != nil {
			return err
		}
		current.Limits.MaxAccounts = p.MaxAccounts
		current.Limits.DevicesPerAccount = p.Defaults.Devices
		current.Limits.ResourcesPerAccount = p.Defaults.Resources
		current.Limits.BroadcastsPerAccount = p.Defaults.Broadcasts
		current.Limits.BroadcastsPerDevice = p.Defaults.BroadcastsPerDevice
		current.Limits.ClipboardBytes = p.Defaults.ClipboardBytes
		if err := current.Validate(); err != nil {
			return err
		}
		return config.Save(path, current)
	}
}

// runPolicy executes `policy show` and `policy set`.
func runPolicy(cmd command, stdout, stderr io.Writer) int {
	paths := config.Resolve(cmd.config, os.Getenv)
	return withRunner(paths, stderr, func(run opRunner) int {
		req := admin.Request{Op: admin.OpPolicyStatus}
		if cmd.sub == "set" {
			req = admin.Request{Op: admin.OpPolicySet, MaxAccounts: cmd.maxAccounts, Limits: cmd.limits()}
		}
		resp, err := run(req)
		if err != nil {
			printf(stderr, "%v\n", err)
			return exitFailure
		}
		if !resp.OK {
			return exitOf(resp, stderr)
		}
		var v admin.PolicyView
		_ = json.Unmarshal(resp.Data, &v)
		if cmd.json {
			printf(stdout, "%s\n", resp.Data)
			return exitOK
		}
		printf(stdout, "accounts\t%d of %d\n", v.Accounts, v.MaxAccounts)
		printf(stdout, "new account starts with\t%s\n", describeLimits(v.Defaults))
		if v.Accounts > v.MaxAccounts {
			printf(stderr, "warning: %d accounts exist and the cap is %d: only registration is refused, nothing is removed\n", v.Accounts, v.MaxAccounts)
		}
		if cmd.sub == "set" && !v.Persisted {
			printf(stderr, "warning: the change is kept for this run only; it was not written to the config\n")
		}
		return exitOK
	})
}

// describeLimits is one line for a set of limits, in the words of the operator's commands.
func describeLimits(l admin.LimitsView) string {
	return "devices " + capText(int64(l.Devices)) + ", resources " + capText(int64(l.Resources)) +
		", broadcasts " + capText(int64(l.Broadcasts)) + ", broadcasts per device " + capText(int64(l.BroadcastsPerDevice)) +
		", clipboard " + clipboardText(l.ClipboardBytes)
}

func capText(n int64) string {
	if n == 0 {
		return "no cap"
	}
	return itoa(n)
}

func clipboardText(n int64) string {
	if n == 0 {
		return "off"
	}
	return itoa(n) + " bytes"
}

func itoa(n int64) string { return strconv.FormatInt(n, 10) }
