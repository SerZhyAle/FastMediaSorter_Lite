package config

import "testing"

// WEB-BINDING websocket-binding "Origin" and normative resolution 2: https required, normalized host and
// EFFECTIVE port equal to the request's own, a listed origin compared the same way, nothing reflected,
// no wildcard. Audit gap G-9.
func TestClassifyOriginHostAndEffectivePort(t *testing.T) {
	a := Admin{Origins: []string{"https://listed.example", "https://other.example:8443", "https://[2001:db8::1]:9000"}}
	cases := []struct {
		name         string
		origin, host string
		want         OriginRelation
	}{
		{"no Origin is a native client", "", "h:22022", OriginAbsent},
		{"same host and port", "https://h:22022", "h:22022", OriginSameHost},
		{"Host with :443, Origin without a port", "https://h", "h:443", OriginSameHost},
		{"Host without a port, Origin with :443", "https://h:443", "h", OriginSameHost},
		{"host is case-insensitive", "https://H.Example:22022", "h.example:22022", OriginSameHost},
		{"another port", "https://h:22023", "h:22022", OriginRejected},
		{"another host", "https://evil.example:22022", "h:22022", OriginRejected},
		{"Origin without a port against a non-default Host port", "https://h", "h:22022", OriginRejected},
		{"IPv6 spelled long against compressed", "https://[0:0:0:0:0:0:0:1]:22022", "[::1]:22022", OriginSameHost},
		{"IPv6 compressed against spelled long", "https://[::1]:22022", "[0:0:0:0:0:0:0:1]:22022", OriginSameHost},
		{"IPv6 case", "https://[2001:DB8::1]:22022", "[2001:db8::1]:22022", OriginSameHost},
		{"IPv6 default port", "https://[::1]", "[0:0:0:0:0:0:0:1]:443", OriginSameHost},
		{"another IPv6 address", "https://[::2]:22022", "[::1]:22022", OriginRejected},
		{"IPv6 without brackets in the Host is no host", "https://[::1]:22022", "::1:22022", OriginRejected},
		{"listed without a port", "https://listed.example", "h:22022", OriginListed},
		{"listed without a port, Origin with :443", "https://listed.example:443", "h:22022", OriginListed},
		{"listed with a port", "https://other.example:8443", "h:22022", OriginListed},
		{"listed with a port, Origin without it", "https://other.example", "h:22022", OriginRejected},
		{"listed IPv6", "https://[2001:0db8:0:0:0:0:0:1]:9000", "h:22022", OriginListed},
		{"listed host in another case", "https://LISTED.example", "h:22022", OriginListed},
		{"the same host is not enough for a listed port", "https://listed.example:444", "h:22022", OriginRejected},
		{"null", "null", "h:22022", OriginRejected},
		{"http", "http://h:22022", "h:22022", OriginRejected},
		{"no scheme", "h:22022", "h:22022", OriginRejected},
		{"a path", "https://h:22022/path", "h:22022", OriginRejected},
		{"a trailing slash", "https://h:22022/", "h:22022", OriginRejected},
		{"a query", "https://h:22022?x=1", "h:22022", OriginRejected},
		{"a fragment", "https://h:22022#x", "h:22022", OriginRejected},
		{"userinfo", "https://user@h:22022", "h:22022", OriginRejected},
		{"userinfo with a password", "https://user:pw@h:22022", "h:22022", OriginRejected},
		{"trailing dot on the Origin only", "https://h.example.:22022", "h.example:22022", OriginRejected},
		{"trailing dot on the Host only", "https://h.example:22022", "h.example.:22022", OriginRejected},
		{"trailing dot on both is one spelling", "https://h.example.:22022", "h.example.:22022", OriginSameHost},
		{"leading space", " https://h:22022", "h:22022", OriginRejected},
		{"trailing space", "https://h:22022 ", "h:22022", OriginRejected},
		{"empty port", "https://h:", "h:443", OriginRejected},
		{"port zero", "https://h:0", "h:0", OriginRejected},
		{"port too high", "https://h:70000", "h:70000", OriginRejected},
		{"port with a leading zero", "https://h:0443", "h:443", OriginRejected},
		{"percent escape in the host", "https://h%2eexample:22022", "h.example:22022", OriginRejected},
		{"IPv6 zone", "https://[fe80::1%25eth0]:22022", "[fe80::1%25eth0]:22022", OriginRejected},
		{"a wildcard Origin", "https://*", "*", OriginRejected},
		{"a Host that is no authority", "https://h:22022", "h:22022/x", OriginRejected},
		{"an empty Host", "https://h:22022", "", OriginRejected},
		{"a Host with userinfo", "https://h:22022", "u@h:22022", OriginRejected},
		{"non-ASCII host", "https://hé.example", "hé.example:443", OriginRejected},
	}
	for _, c := range cases {
		if got := a.ClassifyOrigin(c.origin, c.host); got != c.want {
			t.Errorf("%s: ClassifyOrigin(%q, %q) = %d, want %d", c.name, c.origin, c.host, got, c.want)
		}
		if got, want := a.OriginAllowed(c.origin, c.host), c.want != OriginRejected; got != want {
			t.Errorf("%s: OriginAllowed(%q, %q) = %v, want %v", c.name, c.origin, c.host, got, want)
		}
	}
}

// A listed entry is never a wildcard and never anything but an https origin: Validate refuses it, and
// even a config that was never validated lets such an entry match nothing.
func TestListedOriginsAreValidatedAndNeverWildcards(t *testing.T) {
	for _, bad := range []string{"*", "https://*", "https://*.example", "http://listed.example", "listed.example", "https://listed.example/", "https://listed.example/app", "https://user@listed.example", "null", "", "https://", "https://listed.example:0"} {
		c := Default()
		c.Admin.Origins = []string{bad}
		if c.Validate() == nil {
			t.Errorf("origins entry %q accepted", bad)
		}
		a := Admin{Origins: []string{bad}}
		for _, origin := range []string{bad, "https://listed.example", "https://anything.example", "null"} {
			if a.ClassifyOrigin(origin, "h:22022") == OriginListed {
				t.Errorf("entry %q listed the origin %q", bad, origin)
			}
		}
	}
	good := Default()
	good.Admin.Origins = []string{"https://listed.example", "https://listed.example:8443", "https://[2001:db8::1]:9000"}
	if err := good.Validate(); err != nil {
		t.Fatalf("good origins refused: %v", err)
	}
}
