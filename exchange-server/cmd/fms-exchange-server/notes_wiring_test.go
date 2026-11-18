package main

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"fms/exchange-server/internal/accounts"
	"fms/exchange-server/internal/config"
	"fms/exchange-server/internal/notes"
)

// The wiring of the notes store into the command (DEVICE-EXCHANGE notes sync, ticket L0321): the count limit
// (limits.notesPerAccount -> policyOf -> Limits.Notes, kept by saveLimitsTo) and the offline account commands that
// erase an account's notes (items AU and AO). The store itself, the engine and the admin operations have their
// own tests; these hold the seams between them.

// wiringBox is a box whose content the state log must never carry.
const wiringBox = `{"v":1,"k":"BOXSECRET"}`

// putNotes leaves n live notes of an account in the notes store of a data directory, as a server that ran before
// the offline command would have.
func putNotes(t *testing.T, dir, account string, n int) {
	t.Helper()
	s, err := notes.Open(dir, nil)
	if err != nil {
		t.Fatal(err)
	}
	for i := 1; i <= n; i++ {
		id := fmt.Sprintf("00000000-0000-4000-8000-%012d", i)
		if _, err := s.Put(account, "device-1", id, 0, false, json.RawMessage(wiringBox), 0); err != nil {
			t.Fatal(err)
		}
	}
}

// heldNotes is what the notes file of a data directory holds for an account: its records and its revision counter.
// The store is opened afresh, so this is the disk and not a process's memory.
func heldNotes(t *testing.T, dir, account string) (count int, revision int64) {
	t.Helper()
	s, err := notes.Open(dir, nil)
	if err != nil {
		t.Fatal(err)
	}
	revision, _, count = s.State(account)
	return count, revision
}

// stateLogText is every file of the data directory's logs folder, joined: the state log an offline command wrote.
func stateLogText(t *testing.T, dir string) string {
	t.Helper()
	logs := filepath.Join(dir, config.LogsDir)
	entries, err := os.ReadDir(logs)
	if err != nil {
		t.Fatal(err)
	}
	var b strings.Builder
	for _, e := range entries {
		if e.IsDir() {
			continue
		}
		raw, err := os.ReadFile(filepath.Join(logs, e.Name()))
		if err != nil {
			t.Fatal(err)
		}
		b.Write(raw)
	}
	return b.String()
}

// config.DefaultNotesPerAccount is spelt once more in the config package, which imports nothing of the server:
// the two must stay one number, and that number is 5000 (item FL, criterion 8).
func TestTheNotesCountDefaultIsOneNumberInTheConfigAndThePolicy(t *testing.T) {
	if config.DefaultNotesPerAccount != accounts.DefaultNotesPerAccount {
		t.Fatalf("config.DefaultNotesPerAccount %d differs from accounts.DefaultNotesPerAccount %d",
			config.DefaultNotesPerAccount, accounts.DefaultNotesPerAccount)
	}
	if accounts.DefaultNotesPerAccount != 5000 {
		t.Fatalf("the default count of item FL is 5000, got %d", accounts.DefaultNotesPerAccount)
	}
	if got := config.Default().Limits.NotesPerAccount; got != accounts.DefaultNotesPerAccount {
		t.Fatalf("a default config carries %d, want %d", got, accounts.DefaultNotesPerAccount)
	}
	if got := accounts.DefaultPolicy().Defaults.Notes; got != accounts.DefaultNotesPerAccount {
		t.Fatalf("DefaultPolicy carries %d, want %d", got, accounts.DefaultNotesPerAccount)
	}
	if got := policyOf(config.Default().Limits).Defaults.Notes; got != 5000 {
		t.Fatalf("the policy of a default config carries %d, want 5000", got)
	}
}

func TestNotesPolicyDefaultsAndPersistence(t *testing.T) {
	p := cfgPath(t)
	cfg := config.Default()
	for _, n := range []int{0, 1, 7} {
		cfg.Limits.NotesPerAccount = n
		cfg.Limits.NotesMaxBytes = int64(n)
		pol := policyOf(cfg.Limits)
		if pol.Defaults.Notes != n || pol.Defaults.NotesBytes != int64(n) {
			t.Fatalf("policy lost notes limits: %+v", pol.Defaults)
		}
		if err := config.Save(p, cfg); err != nil {
			t.Fatal(err)
		}
		if err := saveLimitsTo(p)(pol); err != nil {
			t.Fatal(err)
		}
		got, err := config.Load(p)
		if err != nil || got.Limits.NotesPerAccount != n || got.Limits.NotesMaxBytes != int64(n) {
			t.Fatalf("persisted notes limits: %+v, %v", got.Limits, err)
		}
	}
}

func TestNotesPolicyCommandWritesAndShowsBothLimits(t *testing.T) {
	p := cfgPath(t)
	if err := config.Save(p, config.Default()); err != nil {
		t.Fatal(err)
	}
	code, _, stderr := execute(t, []string{"policy", "set", "--notes", "0", "--notes-bytes", "7", "--config", p}, "")
	if code != 0 {
		t.Fatalf("policy set: exit %d %s", code, stderr)
	}
	got, err := config.Load(p)
	if err != nil || got.Limits.NotesPerAccount != 0 || got.Limits.NotesMaxBytes != 7 {
		t.Fatalf("persisted limits: %+v, %v", got.Limits, err)
	}
	code, out, stderr := execute(t, []string{"policy", "show", "--json", "--config", p}, "")
	if code != 0 || !strings.Contains(out, `"notes":0`) || !strings.Contains(out, `"notesBytes":7`) {
		t.Fatalf("policy show: exit %d, %s, %s", code, out, stderr)
	}
}

// `account remove` run offline (no server holds the data directory) opens the notes store of that directory and
// erases the account's notes with the account (item AU), leaving every other account's.
func TestAccountRemoveOfflineErasesTheNotesOfThatAccountOnly(t *testing.T) {
	p := cfgPath(t)
	dir := filepath.Dir(p)
	for _, l := range []string{"alice", "bobby"} {
		if code, _, stderr := execute(t, []string{"account", "add", l, "--config", p}, "Str0ngPassw0rd\n"); code != 0 {
			t.Fatalf("account add %s: exit %d %s", l, code, stderr)
		}
	}
	putNotes(t, dir, "alice", 2)
	putNotes(t, dir, "bobby", 1)

	if code, _, stderr := execute(t, []string{"account", "remove", "alice", "--config", p}, ""); code != 0 {
		t.Fatalf("account remove: exit %d %s", code, stderr)
	}
	if n, rev := heldNotes(t, dir, "alice"); n != 0 || rev != 0 {
		t.Fatalf("the notes file still holds %d records, counter %d, for the removed account", n, rev)
	}
	if n, rev := heldNotes(t, dir, "bobby"); n != 1 || rev != 1 {
		t.Fatalf("another account's notes: %d records, counter %d, want 1 and 1", n, rev)
	}
	if raw, err := os.ReadFile(filepath.Join(dir, notes.File)); err != nil || strings.Contains(string(raw), "alice") {
		t.Fatalf("the notes file still names the removed account: %v", err)
	}

	// the state log of the command says how many records went, never a note or a box
	log := stateLogText(t, dir)
	if !strings.Contains(log, "\tnotes-erased\t") || !strings.Contains(log, "records=2") {
		t.Fatalf("the state log has no notes-erased line with records=2:\n%s", log)
	}
	if strings.Contains(log, "BOXSECRET") || strings.Contains(log, "00000000-0000-4000-8000-") {
		t.Fatalf("the state log carries a note:\n%s", log)
	}
}

// `account kind` run offline erases the notes when the account becomes a guest (item AO) and in no other case.
func TestAccountKindOfflineErasesTheNotesOnlyOnTheWayToAGuest(t *testing.T) {
	p := cfgPath(t)
	dir := filepath.Dir(p)
	for _, l := range []string{"alice", "bobby"} {
		if code, _, stderr := execute(t, []string{"account", "add", l, "--config", p}, "Str0ngPassw0rd\n"); code != 0 {
			t.Fatalf("account add %s: exit %d %s", l, code, stderr)
		}
	}
	if code, _, stderr := execute(t, []string{"account", "add", "gina", "--generate", "--kind", "guest", "--config", p}, ""); code != 0 {
		t.Fatalf("account add gina: exit %d %s", code, stderr)
	}
	putNotes(t, dir, "alice", 2)
	putNotes(t, dir, "bobby", 1)
	putNotes(t, dir, "gina", 1) // a guest holds none through the server; the file keeps what it is given

	// no change of kind
	code, out, stderr := execute(t, []string{"account", "kind", "alice", "member", "--config", p}, "")
	if code != 0 || !strings.Contains(out, "already is a member") {
		t.Fatalf("kind of a member to member: exit %d %q %s", code, out, stderr)
	}
	if n, _ := heldNotes(t, dir, "alice"); n != 2 {
		t.Fatalf("a kind that did not change left %d records, want 2", n)
	}

	// a guest becomes a member: the notes stay
	code, out, stderr = execute(t, []string{"account", "kind", "gina", "member", "--config", p}, "")
	if code != 0 || !strings.Contains(out, "now a member") {
		t.Fatalf("kind of a guest to member: exit %d %q %s", code, out, stderr)
	}
	if n, _ := heldNotes(t, dir, "gina"); n != 1 {
		t.Fatalf("becoming a member left %d records, want 1", n)
	}

	// a member becomes a guest: its notes go, the others stay
	code, out, stderr = execute(t, []string{"account", "kind", "alice", "guest", "--config", p}, "")
	if code != 0 || !strings.Contains(out, "now a guest") {
		t.Fatalf("kind of a member to guest: exit %d %q %s", code, out, stderr)
	}
	// the account goes on: no records, but the counter it had (2) and the horizon raised to it (item FG: a revision
	// never comes back, so a device that synchronised up to 2 is answered `full` and never meets new notes 1, 2)
	if n, rev := heldNotes(t, dir, "alice"); n != 0 || rev != 2 {
		t.Fatalf("a new guest keeps %d records, counter %d; want no records and the counter 2 it had", n, rev)
	}
	if s, err := notes.Open(dir, nil); err != nil {
		t.Fatal(err)
	} else if _, hor, _ := s.State("alice"); hor != 2 {
		t.Fatalf("a new guest has horizon %d, want 2 (the counter)", hor)
	}
	if n, _ := heldNotes(t, dir, "bobby"); n != 1 {
		t.Fatalf("another account's notes: %d records, want 1", n)
	}
	if n, _ := heldNotes(t, dir, "gina"); n != 1 {
		t.Fatalf("the former guest's notes: %d records, want 1", n)
	}
	log := stateLogText(t, dir)
	if !strings.Contains(log, "\tnotes-erased\t") || !strings.Contains(log, "records=2") {
		t.Fatalf("the state log has no notes-erased line with records=2:\n%s", log)
	}
	if strings.Contains(log, "BOXSECRET") || strings.Contains(log, "00000000-0000-4000-8000-") {
		t.Fatalf("the state log carries a note:\n%s", log)
	}

	// a guest made a guest again is no change
	putNotes(t, dir, "alice", 1)
	code, out, stderr = execute(t, []string{"account", "kind", "alice", "guest", "--config", p}, "")
	if code != 0 || !strings.Contains(out, "already is a guest") {
		t.Fatalf("kind of a guest to guest: exit %d %q %s", code, out, stderr)
	}
	if n, _ := heldNotes(t, dir, "alice"); n != 1 {
		t.Fatalf("a kind that did not change left %d records, want 1", n)
	}
}
