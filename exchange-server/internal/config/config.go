// Package config is the exchange server's one configuration file and the data directory it lives in
// (133 section 5, ticket 138).
//
// The file is JSON with a "_comment" member per section (JSON has no comments). It never holds a
// plaintext password, only the Argon2id record. It is written atomically, and a UI never builds its
// JSON by hand: it calls the binary's set-password.
package config

import (
	"encoding/json"
	"errors"
	"fmt"
	"io/fs"
	"net"
	"os"
	"path/filepath"
	"strconv"
	"strings"

	"fms/exchange-server/internal/password"
)

// File names inside the data directory.
const (
	FileName = "exchange.json"
	LogName  = "exchange.log"
	TLSDir   = "tls"
	CertName = "cert.pem"
	KeyName  = "key.pem"
	// EnvDataDir overrides the data directory (tests, portable deployments).
	EnvDataDir = "FMS_EXCHANGE_DATA_DIR"
)

// ErrNoPassword means the config has no password hash: the server never runs password-less.
var ErrNoPassword = errors.New("no server password is set")

// Rate is one token-bucket setting.
type Rate struct {
	PerMinute float64 `json:"perMinute"`
	Burst     int     `json:"burst"`
}

// Limits are the deployment's caps and rates (working defaults until rung 2, Q-133-8).
type Limits struct {
	Comment             string `json:"_comment,omitempty"`
	MaxRegistrations    int    `json:"maxRegistrations"`
	MaxConnections      int    `json:"maxConnections"`
	TunnelsPerShare     int    `json:"tunnelsPerShare"`
	PreAuthSlots        int    `json:"preAuthSlots"`
	VerifierConcurrency int    `json:"verifierConcurrency"`
	Connection          Rate   `json:"connectionRate"`
	UnknownID           Rate   `json:"unknownIdRate"`
	FailedRegister      Rate   `json:"failedRegisterRate"`
	AttachMiss          Rate   `json:"attachMissRate"`
}

// Keepalive is the registration lifetime (133 section 5).
type Keepalive struct {
	Comment          string `json:"_comment,omitempty"`
	DefaultSeconds   int    `json:"defaultSeconds"`
	MinSeconds       int    `json:"minSeconds"`
	MaxSeconds       int    `json:"maxSeconds"`
	WindowMultiplier int    `json:"windowMultiplier"`
}

// TLS names a CA-signed pair for a hosted deployment. Empty means the generated self-signed identity.
type TLS struct {
	Comment  string `json:"_comment,omitempty"`
	CertFile string `json:"certFile"`
	KeyFile  string `json:"keyFile"`
}

// Log is the log's rotation bound.
type Log struct {
	Comment  string `json:"_comment,omitempty"`
	MaxBytes int64  `json:"maxBytes"`
}

// Config is the whole file.
type Config struct {
	Comment      string    `json:"_comment,omitempty"`
	Listen       string    `json:"listen"`
	PortRange    string    `json:"portRange"`
	PasswordHash string    `json:"passwordHash"`
	Limits       Limits    `json:"limits"`
	Keepalive    Keepalive `json:"keepalive"`
	TLS          TLS       `json:"tls"`
	Log          Log       `json:"log"`
}

// Default returns the working defaults, with the comments a first-run file carries.
func Default() Config {
	return Config{
		Comment:   "Media Exchange Server configuration. Numbers are working defaults until the wire freezes. Set the password with: fms-exchange-server set-password",
		Listen:    ":22022",
		PortRange: "40000-40100",
		Limits: Limits{
			Comment:             "Caps and per-address rates. A producer or consumer over a limit is refused; nothing here is kept on disk.",
			MaxRegistrations:    200,
			MaxConnections:      1000,
			TunnelsPerShare:     16,
			PreAuthSlots:        16,
			VerifierConcurrency: 2,
			Connection:          Rate{PerMinute: 120, Burst: 40},
			UnknownID:           Rate{PerMinute: 10, Burst: 3},
			FailedRegister:      Rate{PerMinute: 5, Burst: 2},
			AttachMiss:          Rate{PerMinute: 10, Burst: 5},
		},
		Keepalive: Keepalive{
			Comment:          "A producer proposes an interval; the server clamps it to min..max and ends a registration after windowMultiplier missed intervals.",
			DefaultSeconds:   30,
			MinSeconds:       10,
			MaxSeconds:       120,
			WindowMultiplier: 3,
		},
		TLS: TLS{Comment: "Leave empty to use the generated self-signed identity. Set both files to serve a CA-signed certificate."},
		Log: Log{Comment: "The log is renamed to exchange.log.1 when it reaches maxBytes.", MaxBytes: 4 << 20},
	}
}

// ParsePortRange parses "lo-hi".
func ParsePortRange(s string) (lo, hi int, err error) {
	a, b, ok := strings.Cut(strings.TrimSpace(s), "-")
	if !ok {
		return 0, 0, fmt.Errorf("portRange %q must look like 40000-40100", s)
	}
	lo, e1 := strconv.Atoi(strings.TrimSpace(a))
	hi, e2 := strconv.Atoi(strings.TrimSpace(b))
	if e1 != nil || e2 != nil || lo < 1 || hi > 65535 || lo > hi {
		return 0, 0, fmt.Errorf("portRange %q must be two ports, 1..65535, low first", s)
	}
	return lo, hi, nil
}

// Validate checks everything except the password (see CheckPassword).
func (c Config) Validate() error {
	if _, port, err := net.SplitHostPort(c.Listen); err != nil {
		return fmt.Errorf("listen %q is not host:port", c.Listen)
	} else if n, err := strconv.Atoi(port); err != nil || n < 0 || n > 65535 {
		return fmt.Errorf("listen %q has a bad port", c.Listen)
	}
	if _, _, err := ParsePortRange(c.PortRange); err != nil {
		return err
	}
	k := c.Keepalive
	if k.MinSeconds < 1 || k.MaxSeconds < k.MinSeconds || k.DefaultSeconds < k.MinSeconds ||
		k.DefaultSeconds > k.MaxSeconds || k.WindowMultiplier < 1 {
		return errors.New("keepalive: need 1 <= minSeconds <= defaultSeconds <= maxSeconds and windowMultiplier >= 1")
	}
	l := c.Limits
	if l.MaxRegistrations < 1 || l.MaxConnections < 1 || l.TunnelsPerShare < 1 || l.PreAuthSlots < 1 || l.VerifierConcurrency < 1 {
		return errors.New("limits: every cap must be at least 1")
	}
	for name, r := range map[string]Rate{"connectionRate": l.Connection, "unknownIdRate": l.UnknownID,
		"failedRegisterRate": l.FailedRegister, "attachMissRate": l.AttachMiss} {
		if r.PerMinute <= 0 || r.Burst < 1 {
			return fmt.Errorf("limits.%s: perMinute must be positive and burst at least 1", name)
		}
	}
	if (c.TLS.CertFile == "") != (c.TLS.KeyFile == "") {
		return errors.New("tls: set both certFile and keyFile, or neither")
	}
	if c.Log.MaxBytes < 4096 {
		return errors.New("log.maxBytes must be at least 4096")
	}
	return nil
}

// CheckPassword reports whether the config holds a usable password record.
func (c Config) CheckPassword() error {
	if c.PasswordHash == "" {
		return ErrNoPassword
	}
	return password.ValidRecord(c.PasswordHash)
}

// ValidateForServe is Validate plus the password requirement (133 section 5: never password-less).
func (c Config) ValidateForServe() error {
	if err := c.Validate(); err != nil {
		return err
	}
	return c.CheckPassword()
}

// Paths are the files of one deployment.
type Paths struct {
	Dir    string
	Config string
	TLSDir string
	Cert   string
	Key    string
	Log    string
}

func pathsIn(dir, configFile string) Paths {
	tls := filepath.Join(dir, TLSDir)
	return Paths{
		Dir: dir, Config: configFile, TLSDir: tls,
		Cert: filepath.Join(tls, CertName), Key: filepath.Join(tls, KeyName),
		Log: filepath.Join(dir, LogName),
	}
}

// Resolve picks the data directory: an explicit --config path wins (its directory is the data
// directory); then the FMS_EXCHANGE_DATA_DIR override; then the platform's order (on Windows the
// service's %ProgramData% store if it exists and opens, else %LOCALAPPDATA%).
func Resolve(configFlag string, getenv func(string) string) Paths {
	if configFlag != "" {
		abs, err := filepath.Abs(configFlag)
		if err != nil {
			abs = configFlag
		}
		return pathsIn(filepath.Dir(abs), abs)
	}
	if d := getenv(EnvDataDir); d != "" {
		return pathsIn(d, filepath.Join(d, FileName))
	}
	d := defaultDataDir(getenv)
	return pathsIn(d, filepath.Join(d, FileName))
}

// Load reads and parses the file onto the defaults (a missing member keeps its default; unknown
// members are ignored). It does not validate.
func Load(path string) (Config, error) {
	c := Default()
	data, err := os.ReadFile(path)
	if err != nil {
		return c, err
	}
	if err := json.Unmarshal(data, &c); err != nil {
		return Default(), fmt.Errorf("%s is not valid JSON: %w", filepath.Base(path), err)
	}
	return c, nil
}

// writeTemp creates the temporary file of Save; a test replaces it.
var writeTemp = os.WriteFile

// Save writes the config atomically (temporary file, then rename) with the directory created.
//
// When the temporary file cannot be created because the caller may modify the config file but
// not add files to its directory (the Server edition's service directory gives the enrolled user
// Modify on exchange.json only, ticket 144), the existing file is rewritten in place: not atomic,
// but it keeps the file's access list, which a rename over it would replace.
func Save(path string, c Config) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o700); err != nil {
		return err
	}
	data, err := json.MarshalIndent(c, "", "  ")
	if err != nil {
		return err
	}
	data = append(data, '\n')
	tmp := path + ".tmp"
	if err := writeTemp(tmp, data, 0o600); err != nil {
		if errors.Is(err, fs.ErrPermission) {
			if _, statErr := os.Stat(path); statErr == nil {
				return os.WriteFile(path, data, 0o600)
			}
		}
		return err
	}
	if err := os.Rename(tmp, path); err != nil {
		_ = os.Remove(tmp)
		return err
	}
	return nil
}

// LoadOrInit loads the file, writing the commented defaults first when it does not exist.
func LoadOrInit(path string) (Config, error) {
	c, err := Load(path)
	if err == nil {
		return c, nil
	}
	if !errors.Is(err, os.ErrNotExist) {
		return c, err
	}
	c = Default()
	if err := Save(path, c); err != nil {
		return c, err
	}
	return c, nil
}
