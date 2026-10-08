// Package config is the exchange server's one configuration file and the data directory it lives in
// (133 section 5, ticket 138, extended by tickets 162-164).
//
// The file is JSON with a "_comment" member per section (JSON has no comments). It holds NO secret:
// accounts and their Argon2id records live in accounts.json, written by the server alone. It is
// written atomically, and a UI never builds its JSON by hand: it calls the binary's commands.
package config

import (
	"encoding/json"
	"errors"
	"fmt"
	"io/fs"
	"net"
	"net/netip"
	"os"
	"path/filepath"
	"strconv"
	"strings"
)

// File names inside the data directory.
const (
	FileName = "exchange.json"
	// LogsDir is the one subdirectory the enrolled user may read (ticket 182 L-1): the logs are
	// redacted by construction (typed hashed ids only), everything else in the data directory is not
	// theirs. LogName, ServiceLogName and the rotation live inside it.
	LogsDir        = "logs"
	LogName        = "exchange.log"
	ServiceLogName = "service.log"
	TLSDir         = "tls"
	CertName       = "cert.pem"
	KeyName        = "key.pem"
	// EnvDataDir overrides the data directory (tests, portable deployments).
	EnvDataDir = "FMS_EXCHANGE_DATA_DIR"
)

// Rate is one token-bucket setting.
type Rate struct {
	PerMinute float64 `json:"perMinute"`
	Burst     int     `json:"burst"`
}

// Limits are the deployment's caps and rates (working defaults until rung 2, ticket 167 rule 6).
type Limits struct {
	Comment              string `json:"_comment,omitempty"`
	MaxConnections       int    `json:"maxConnections"`
	TunnelsPerResource   int    `json:"tunnelsPerResource"`
	PreAuthSlots         int    `json:"preAuthSlots"`
	VerifierConcurrency  int    `json:"verifierConcurrency"`
	DevicesPerAccount    int    `json:"devicesPerAccount"`
	ResourcesPerAccount  int    `json:"resourcesPerAccount"`
	BroadcastsPerAccount int    `json:"broadcastsPerAccount"`
	BroadcastsPerDevice  int    `json:"broadcastsPerDevice"`
	CodesPerAccount      int    `json:"codesPerAccount"`
	Connection           Rate   `json:"connectionRate"`     // accepted connections, per address
	UnknownID            Rate   `json:"unknownIdRate"`      // connects naming an id nobody holds, per address
	FailedAuth           Rate   `json:"failedAuthRate"`     // failed enroll or hello, per address
	AttachMiss           Rate   `json:"attachMissRate"`     // attaches naming no pending tunnel, per address
	FailedEnroll         Rate   `json:"failedEnrollRate"`   // failed enroll, per login
	FailedHello          Rate   `json:"failedHelloRate"`    // failed hello, per account
	PairingRequest       Rate   `json:"pairingRequestRate"` // pairing-request, per account
	BroadcastStart       Rate   `json:"broadcastStartRate"` // broadcast-start, per device
	Cast                 Rate   `json:"castRate"`           // cast, per device (ticket 166)
	// The relay (ticket 165): what a hostile or mislabelled stream can cost a deployment.
	RelayUploadMbit        int `json:"relayUploadMbit"`            // sustained upload ceiling, Mbit/s over 5 s
	RelayListenersPerBcast int `json:"relayListenersPerBroadcast"` // relay listeners and tunnels of one broadcast
	RelayListenersTotal    int `json:"relayListenersTotal"`        // relay listeners of the whole deployment
	CastPendingPerAccount  int `json:"castPendingPerAccount"`      // offers waiting for an answer, per account
}

// Keepalive is the presence lifetime (DEVICE-EXCHANGE 7.3).
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

// Admin configures the local admin channel (ticket 162 section 4).
type Admin struct {
	Comment string `json:"_comment,omitempty"`
	// ManageSIDs are Windows SIDs besides the data directory's owner, the server's own account, SYSTEM
	// and Administrators that may use the channel: the Server edition's installer enrols the
	// management user here (the worker's --manage-sid, for this server).
	ManageSIDs []string `json:"manageSids"`
}

// Config is the whole file.
type Config struct {
	Comment   string `json:"_comment,omitempty"`
	Listen    string `json:"listen"`
	PortRange string `json:"portRange"`
	// PublicEndpoint is the host:port every client reaches the server by, announced in `welcome`;
	// producers build their endpoint URLs from it. Empty: the server still serves, announces an empty
	// endpoint and hides `tunnel` and `relay` (ticket 163 Q163-3).
	PublicEndpoint string    `json:"publicEndpoint"`
	Limits         Limits    `json:"limits"`
	Keepalive      Keepalive `json:"keepalive"`
	TLS            TLS       `json:"tls"`
	Log            Log       `json:"log"`
	Admin          Admin     `json:"admin"`

	// ObsoletePasswordHash is set by Load when the file still carries the single server password of
	// the superseded design (ticket 162 section 3): it is ignored, logged once, and gone from the file
	// the next time it is saved. Nothing deployed ever held one, so there is no migration.
	ObsoletePasswordHash bool `json:"-"`
}

// Default returns the working defaults, with the comments a first-run file carries.
func Default() Config {
	return Config{
		Comment:   "Media Exchange Server configuration. Numbers are working defaults until the wire freezes. Add the first account with: fms-exchange-server account add <login> --generate",
		Listen:    ":22022",
		PortRange: "40000-40100",
		Limits: Limits{
			Comment:              "Caps and per-address rates. A device over a limit is refused; nothing here is kept on disk as state.",
			MaxConnections:       1000,
			TunnelsPerResource:   16,
			PreAuthSlots:         16,
			VerifierConcurrency:  2,
			DevicesPerAccount:    64,
			ResourcesPerAccount:  256,
			BroadcastsPerAccount: 16,
			BroadcastsPerDevice:  4,
			CodesPerAccount:      5,
			Connection:           Rate{PerMinute: 120, Burst: 40},
			UnknownID:            Rate{PerMinute: 10, Burst: 3},
			FailedAuth:           Rate{PerMinute: 5, Burst: 2},
			AttachMiss:           Rate{PerMinute: 10, Burst: 5},
			FailedEnroll:         Rate{PerMinute: 10, Burst: 5},
			FailedHello:          Rate{PerMinute: 10, Burst: 5},
			PairingRequest:       Rate{PerMinute: 6, Burst: 6},
			BroadcastStart:       Rate{PerMinute: 6, Burst: 6},
			Cast:                 Rate{PerMinute: 10, Burst: 10},

			RelayUploadMbit:        16,
			RelayListenersPerBcast: 500,
			RelayListenersTotal:    1000,
			CastPendingPerAccount:  16,
		},
		Keepalive: Keepalive{
			Comment:          "A device proposes an interval; the server clamps it to min..max and takes it offline after windowMultiplier missed intervals.",
			DefaultSeconds:   30,
			MinSeconds:       10,
			MaxSeconds:       120,
			WindowMultiplier: 3,
		},
		TLS:   TLS{Comment: "Leave empty to use the generated self-signed identity. Set both files to serve a CA-signed certificate."},
		Log:   Log{Comment: "The log is renamed to exchange.log.1 when it reaches maxBytes.", MaxBytes: 4 << 20},
		Admin: Admin{Comment: "SIDs (Windows) that may use the local admin channel besides the owner of this directory, SYSTEM and Administrators.", ManageSIDs: []string{}},
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

// CheckPublicEndpoint validates the endpoint clients are told: host:port, a real port, a host that is
// not empty, not a wildcard and not loopback (a client elsewhere could never use it). An empty string
// is allowed - the server serves and says what is hidden.
func CheckPublicEndpoint(s string) error {
	if s == "" {
		return nil
	}
	host, port, err := net.SplitHostPort(s)
	if err != nil {
		return fmt.Errorf("publicEndpoint %q is not host:port", s)
	}
	if n, err := strconv.Atoi(port); err != nil || n < 1 || n > 65535 {
		return fmt.Errorf("publicEndpoint %q has a bad port", s)
	}
	if host == "" || strings.ContainsAny(host, " \t/\\@") {
		return fmt.Errorf("publicEndpoint %q has no usable host", s)
	}
	if strings.EqualFold(host, "localhost") || strings.HasSuffix(strings.ToLower(host), ".localhost") {
		return fmt.Errorf("publicEndpoint %q is a loopback name: no other device could reach it", s)
	}
	if ip, err := netip.ParseAddr(strings.Trim(host, "[]")); err == nil {
		if ip.IsLoopback() || ip.IsUnspecified() {
			return fmt.Errorf("publicEndpoint %q is a loopback or wildcard address: no other device could reach it", s)
		}
	}
	return nil
}

// Validate checks the whole file. It says nothing about accounts: whether the server may start
// without one is serve's question, answered from the accounts store.
func (c Config) Validate() error {
	if _, port, err := net.SplitHostPort(c.Listen); err != nil {
		return fmt.Errorf("listen %q is not host:port", c.Listen)
	} else if n, err := strconv.Atoi(port); err != nil || n < 0 || n > 65535 {
		return fmt.Errorf("listen %q has a bad port", c.Listen)
	}
	if _, _, err := ParsePortRange(c.PortRange); err != nil {
		return err
	}
	if err := CheckPublicEndpoint(c.PublicEndpoint); err != nil {
		return err
	}
	k := c.Keepalive
	if k.MinSeconds < 1 || k.MaxSeconds < k.MinSeconds || k.DefaultSeconds < k.MinSeconds ||
		k.DefaultSeconds > k.MaxSeconds || k.WindowMultiplier < 1 {
		return errors.New("keepalive: need 1 <= minSeconds <= defaultSeconds <= maxSeconds and windowMultiplier >= 1")
	}
	l := c.Limits
	caps := []struct {
		name string
		n    int
	}{{"maxConnections", l.MaxConnections}, {"tunnelsPerResource", l.TunnelsPerResource}, {"preAuthSlots", l.PreAuthSlots},
		{"verifierConcurrency", l.VerifierConcurrency}, {"devicesPerAccount", l.DevicesPerAccount},
		{"resourcesPerAccount", l.ResourcesPerAccount}, {"broadcastsPerAccount", l.BroadcastsPerAccount},
		{"broadcastsPerDevice", l.BroadcastsPerDevice}, {"codesPerAccount", l.CodesPerAccount},
		{"relayUploadMbit", l.RelayUploadMbit}, {"relayListenersPerBroadcast", l.RelayListenersPerBcast},
		{"relayListenersTotal", l.RelayListenersTotal}, {"castPendingPerAccount", l.CastPendingPerAccount}}
	for _, c := range caps {
		if c.n < 1 {
			return fmt.Errorf("limits.%s: every cap must be at least 1", c.name)
		}
	}
	rates := []struct {
		name string
		r    Rate
	}{{"connectionRate", l.Connection}, {"unknownIdRate", l.UnknownID}, {"failedAuthRate", l.FailedAuth},
		{"attachMissRate", l.AttachMiss}, {"failedEnrollRate", l.FailedEnroll}, {"failedHelloRate", l.FailedHello},
		{"pairingRequestRate", l.PairingRequest}, {"broadcastStartRate", l.BroadcastStart}, {"castRate", l.Cast}}
	for _, c := range rates {
		if c.r.PerMinute <= 0 || c.r.Burst < 1 {
			return fmt.Errorf("limits.%s: perMinute must be positive and burst at least 1", c.name)
		}
	}
	if (c.TLS.CertFile == "") != (c.TLS.KeyFile == "") {
		return errors.New("tls: set both certFile and keyFile, or neither")
	}
	if c.Log.MaxBytes < 4096 {
		return errors.New("log.maxBytes must be at least 4096")
	}
	for _, s := range c.Admin.ManageSIDs {
		if !strings.HasPrefix(s, "S-1-") {
			return fmt.Errorf("admin.manageSids: %q is not a SID", s)
		}
	}
	return nil
}

// Paths are the files of one deployment.
type Paths struct {
	Dir    string
	Config string
	TLSDir string
	Cert   string
	Key    string
	// LogsDir holds Log and ServiceLog (and the rotation Log+".1"); see LogsDir the constant.
	LogsDir    string
	Log        string
	ServiceLog string
}

func pathsIn(dir, configFile string) Paths {
	tls := filepath.Join(dir, TLSDir)
	logs := filepath.Join(dir, LogsDir)
	return Paths{
		Dir: dir, Config: configFile, TLSDir: tls,
		Cert: filepath.Join(tls, CertName), Key: filepath.Join(tls, KeyName),
		LogsDir: logs, Log: filepath.Join(logs, LogName), ServiceLog: filepath.Join(logs, ServiceLogName),
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
	var old struct {
		PasswordHash *string `json:"passwordHash"`
	}
	if json.Unmarshal(data, &old) == nil && old.PasswordHash != nil {
		c.ObsoletePasswordHash = true
	}
	return c, nil
}

// writeTemp creates the temporary file of Save; a test replaces it.
var writeTemp = os.WriteFile

// Save writes the config atomically (temporary file, then rename) with the directory created. The
// obsolete password member is not written back: it is removed by the first save after it was found.
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
