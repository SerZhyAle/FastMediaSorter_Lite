package config

import (
	"net/netip"
	"net/url"
	"strconv"
	"strings"
)

// OriginRelation says how the Origin of a WebSocket opening stands to the server.
type OriginRelation int

const (
	// OriginRejected: the Origin is malformed, not https, or neither the server's own nor listed.
	OriginRejected OriginRelation = iota
	// OriginAbsent: the request carries no Origin - every native client. It is not asked for one.
	OriginAbsent
	// OriginSameHost: the Origin names the host and the effective port of the request's own Host header,
	// which is what a page served by this server sends.
	OriginSameHost
	// OriginListed: the Origin is not the server's own but the operator lists it (a remote consumer).
	OriginListed
)

// defaultHTTPSPort is the effective port of an authority that names none: the opening is always inside TLS.
const defaultHTTPSPort = "443"

// authority is a host and an effective port in the one form two spellings of the same place share.
type authority struct{ host, port string }

// ClassifyOrigin judges the Origin of a WebSocket opening against the request's own Host header
// (WEB-BINDING websocket-binding "Origin" and normative resolution 2). https is required; the host
// (case-insensitive; an IPv6 literal by the address it denotes) and the EFFECTIVE port (443 when none is
// written) must equal those of the Host header, or the operator must list the origin, which is compared
// the same way (so https://h and https://h:443 are one origin). The Origin is never reflected, there is
// no wildcard, and a path, a query, a fragment or credentials on an origin make it malformed.
func (a Admin) ClassifyOrigin(origin, host string) OriginRelation {
	if origin == "" {
		return OriginAbsent
	}
	o, ok := parseOrigin(origin)
	if !ok {
		return OriginRejected
	}
	if h, ok := parseAuthority(host); ok && h == o {
		return OriginSameHost
	}
	for _, listed := range a.Origins {
		if l, ok := parseOrigin(listed); ok && l == o {
			return OriginListed
		}
	}
	return OriginRejected
}

// OriginAllowed reports whether a request carrying origin may open the WebSocket at all: no Origin, the
// server's own, or a listed one. Whether the stream may also be an operator's is ClassifyOrigin's
// OriginAbsent or OriginSameHost.
func (a Admin) OriginAllowed(origin, host string) bool {
	return a.ClassifyOrigin(origin, host) != OriginRejected
}

// parseOrigin reads "https://host[:port]" and nothing else.
func parseOrigin(s string) (authority, bool) {
	if s != strings.TrimSpace(s) || !strings.HasPrefix(strings.ToLower(s), "https://") {
		return authority{}, false
	}
	return parseAuthority(s[len("https://"):])
}

// parseAuthority reads "host[:port]" and nothing else: a Host header value, or the rest of an origin.
func parseAuthority(s string) (authority, bool) {
	if s == "" || s != strings.TrimSpace(s) || strings.ContainsAny(s, "/\\?#@%") {
		return authority{}, false
	}
	u, err := url.Parse("https://" + s)
	if err != nil || u.Host != s || u.User != nil || u.Path != "" || u.RawQuery != "" || u.Fragment != "" || u.Opaque != "" {
		return authority{}, false
	}
	host := strings.ToLower(u.Hostname())
	if !validHostName(host) {
		return authority{}, false
	}
	if ip, err := netip.ParseAddr(host); err == nil {
		if ip.Zone() != "" {
			return authority{}, false
		}
		host = ip.String() // [0:0:0:0:0:0:0:1] and [::1] are one address
	}
	port := u.Port()
	if port == "" {
		if strings.HasSuffix(s, ":") { // "host:" names an empty port, which is no spelling of 443
			return authority{}, false
		}
		port = defaultHTTPSPort
	}
	n, err := strconv.Atoi(port)
	if err != nil || n < 1 || n > 65535 || strconv.Itoa(n) != port {
		return authority{}, false
	}
	return authority{host: host, port: port}, true
}

// validHostName accepts an IP literal or a name of letters, digits, hyphens, underscores and dots (one
// trailing dot is kept as written: the browser sends it as written, and a name with it is not the name
// without it). A wildcard, a space or any other character makes the host unusable.
func validHostName(h string) bool {
	if h == "" {
		return false
	}
	if _, err := netip.ParseAddr(h); err == nil {
		return true
	}
	name := strings.TrimSuffix(h, ".")
	if name == "" {
		return false
	}
	for _, label := range strings.Split(name, ".") {
		if label == "" || len(label) > 63 {
			return false
		}
		for i := 0; i < len(label); i++ {
			c := label[i]
			switch {
			case c >= 'a' && c <= 'z', c >= '0' && c <= '9', c == '-', c == '_':
			default:
				return false
			}
		}
	}
	return len(h) <= 253
}
