// Package settings keeps the application settings standards of DEVICE-EXCHANGE 1.12 (items EN to ER,
// ticket L0296): at most one JSON document per (account, appId), on disk, readable by the operator, erased
// with the account. The server never reads inside a standard; CheckPayload is the whole of what it checks.
package settings

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"sync"
	"time"
	"unicode/utf8"
)

// File is the store's file in the data directory. It exists from the first accepted save on.
const File = "settings.json"

// StoreVersion is the shape of File.
const StoreVersion = 1

// ErrNotFound is a get for a pair that has no standard.
var ErrNotFound = errors.New("settings: no standard")

// Standard is one stored standard.
type Standard struct {
	Account       string `json:"account"`
	AppID         string `json:"appId"`
	FormatVersion int    `json:"formatVersion"`
	Payload       string `json:"settingsPayload"`
	Version       int64  `json:"version"`
	UpdatedAt     string `json:"updatedAt"`
}

type fileShape struct {
	StoreVersion int        `json:"storeVersion"`
	Standards    []Standard `json:"standards"`
}

type key struct{ account, app string }

// Store is the standards of every account. It is safe for concurrent use.
type Store struct {
	dir string
	now func() time.Time

	mu  sync.Mutex
	std map[key]*Standard
}

// Open reads the store of a data directory; an absent file is an empty store.
func Open(dir string, now func() time.Time) (*Store, error) {
	if now == nil {
		now = time.Now
	}
	s := &Store{dir: dir, now: now, std: map[key]*Standard{}}
	data, err := os.ReadFile(filepath.Join(dir, File))
	if errors.Is(err, os.ErrNotExist) {
		return s, nil
	}
	if err != nil {
		return nil, err
	}
	var f fileShape
	if err := json.Unmarshal(data, &f); err != nil {
		return nil, fmt.Errorf("%s is not valid JSON: %w", File, err)
	}
	if f.StoreVersion != StoreVersion {
		return nil, fmt.Errorf("%s has storeVersion %d, this server reads %d", File, f.StoreVersion, StoreVersion)
	}
	for i := range f.Standards {
		st := f.Standards[i]
		s.std[key{st.Account, st.AppID}] = &st
	}
	return s, nil
}

// CheckPayload is item EO: one JSON object of at most maxBytes bytes of UTF-8, whose `version` is an
// integer equal to formatVersion and whose `groups` is an object of objects. Nothing deeper is read.
func CheckPayload(formatVersion int, payload string, maxBytes int) bool {
	if len(payload) > maxBytes || !utf8.ValidString(payload) || !json.Valid([]byte(payload)) {
		return false // json.Valid also refuses anything after the one value
	}
	var top map[string]json.RawMessage
	if err := json.Unmarshal([]byte(payload), &top); err != nil || top == nil {
		return false
	}
	raw := bytes.TrimSpace(top["version"])
	if len(raw) == 0 || raw[0] == '"' {
		return false // absent, or a string where an integer belongs
	}
	var v json.Number
	dec := json.NewDecoder(bytes.NewReader(raw))
	dec.UseNumber()
	if err := dec.Decode(&v); err != nil {
		return false
	}
	n, err := v.Int64()
	if err != nil || n != int64(formatVersion) {
		return false
	}
	var groups map[string]json.RawMessage
	if err := json.Unmarshal(top["groups"], &groups); err != nil || groups == nil {
		return false
	}
	for _, g := range groups {
		var obj map[string]json.RawMessage
		if err := json.Unmarshal(g, &obj); err != nil || obj == nil {
			return false
		}
	}
	return true
}

// Save replaces the standard of the pair as a whole (item EN) and returns it with its new version. The
// file is written before the answer; a write that fails leaves the previous standard in memory and on disk.
func (s *Store) Save(account, appID string, formatVersion int, payload string) (Standard, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	k := key{account, appID}
	prev := s.std[k]
	next := &Standard{Account: account, AppID: appID, FormatVersion: formatVersion, Payload: payload, Version: 1,
		UpdatedAt: s.now().UTC().Format(time.RFC3339)}
	if prev != nil {
		next.Version = prev.Version + 1
	}
	s.std[k] = next
	if err := s.writeLocked(); err != nil {
		if prev != nil {
			s.std[k] = prev
		} else {
			delete(s.std, k)
		}
		return Standard{}, err
	}
	return *next, nil
}

// Get returns the standard of the pair, or ErrNotFound.
func (s *Store) Get(account, appID string) (Standard, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if st := s.std[key{account, appID}]; st != nil {
		return *st, nil
	}
	return Standard{}, ErrNotFound
}

// Erase removes every standard of an account (item AU) and reports how many it removed.
func (s *Store) Erase(account string) (int, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	removed := map[key]*Standard{}
	for k, st := range s.std {
		if k.account == account {
			removed[k] = st
			delete(s.std, k)
		}
	}
	if len(removed) == 0 {
		return 0, nil
	}
	if err := s.writeLocked(); err != nil {
		for k, st := range removed {
			s.std[k] = st
		}
		return 0, err
	}
	return len(removed), nil
}

// Count is the number of stored standards (a count, never a list).
func (s *Store) Count() int {
	s.mu.Lock()
	defer s.mu.Unlock()
	return len(s.std)
}

// writeLocked writes the whole store through a temp file and a rename, so a kill leaves the previous file whole.
func (s *Store) writeLocked() error {
	f := fileShape{StoreVersion: StoreVersion, Standards: []Standard{}}
	for _, st := range s.std {
		f.Standards = append(f.Standards, *st)
	}
	sort.Slice(f.Standards, func(i, j int) bool {
		if f.Standards[i].Account != f.Standards[j].Account {
			return f.Standards[i].Account < f.Standards[j].Account
		}
		return f.Standards[i].AppID < f.Standards[j].AppID
	})
	data, err := json.MarshalIndent(f, "", "  ")
	if err != nil {
		return err
	}
	data = append(data, '\n')
	if err := os.MkdirAll(s.dir, 0o700); err != nil {
		return err
	}
	path := filepath.Join(s.dir, File)
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, data, 0o600); err != nil {
		return err
	}
	if err := os.Rename(tmp, path); err != nil {
		_ = os.Remove(tmp)
		return err
	}
	return nil
}
