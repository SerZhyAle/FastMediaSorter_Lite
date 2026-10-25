package wire

import (
	"bufio"
	"errors"
	"strconv"
	"strings"
)

// The HTTP opening of the one port (DEVICE-EXCHANGE 7.1): inside TLS, a first byte that is an ASCII
// letter is an HTTP/1.1 request to the relay (7.6). This file is the only place that reads a
// request head or writes a status line; the relay itself is ticket 165's.

// maxHTTPHead bounds a request head: a client that sends more is dropped.
const maxHTTPHead = 8 * 1024

// ErrHTTPHead: a request head that is too long, not HTTP/1.x or cut short.
var ErrHTTPHead = errors.New("wire: bad HTTP request head")

// ListenPath is the relay's listen URL path of 7.6 for a broadcast id.
func ListenPath(broadcastID string) string { return "/v2/b/" + broadcastID + "/stream" }

// ParseListenPath returns the broadcast id of "/v2/b/<id>/stream", or false for any other path.
func ParseListenPath(path string) (string, bool) {
	rest, ok := strings.CutPrefix(path, "/v2/b/")
	if !ok {
		return "", false
	}
	id, ok := strings.CutSuffix(rest, "/stream")
	if !ok || id == "" || strings.Contains(id, "/") {
		return "", false
	}
	return id, true
}

// HTTPRequest is the part of a request head the one port needs.
type HTTPRequest struct {
	Method  string
	Path    string // the request target without a query
	Version string // "HTTP/1.1" or another HTTP/1.x
	Headers map[string]string
}

// ReadHTTPRequest reads one request head (the request line and the header lines up to the blank
// line) from r, bounded at maxHTTPHead bytes, and returns its method, path and headers.
func ReadHTTPRequest(r *bufio.Reader) (HTTPRequest, error) {
	var req HTTPRequest
	req.Headers = make(map[string]string)
	total := 0
	first := true
	for {
		line, err := r.ReadString('\n')
		total += len(line)
		if err != nil || total > maxHTTPHead {
			return req, ErrHTTPHead
		}
		line = strings.TrimRight(line, "\r\n")
		if first {
			parts := strings.Split(line, " ")
			if len(parts) != 3 || !strings.HasPrefix(parts[2], "HTTP/1.") || parts[0] == "" || parts[1] == "" {
				return req, ErrHTTPHead
			}
			req.Method = parts[0]
			req.Path = parts[1]
			req.Version = parts[2]
			if i := strings.IndexAny(req.Path, "?#"); i >= 0 {
				req.Path = req.Path[:i]
			}
			first = false
			continue
		}
		if line == "" {
			return req, nil
		}
		if colon := strings.Index(line, ":"); colon > 0 {
			k := strings.ToLower(strings.TrimSpace(line[:colon]))
			v := strings.TrimSpace(line[colon+1:])
			req.Headers[k] = v
		}
	}
}

// BroadcastRealm is the realm of the Basic challenge a protected broadcast's listen URL answers with (item BF).
const BroadcastRealm = "fms-broadcast"

// HTTPResponse is a body-less response with the given status, closing the connection. retryAfter,
// when positive, adds the Retry-After header of a 503 (7.6). A 401 carries the one challenge of item BF,
// `WWW-Authenticate: Basic realm="fms-broadcast"`, whatever made it a 401.
func HTTPResponse(status int, retryAfter int) []byte {
	text := map[int]string{400: "Bad Request", 401: "Unauthorized", 403: "Forbidden", 404: "Not Found", 405: "Method Not Allowed",
		503: "Service Unavailable", 505: "HTTP Version Not Supported"}[status]
	if text == "" {
		text = "Error"
	}
	var b strings.Builder
	b.WriteString("HTTP/1.1 " + strconv.Itoa(status) + " " + text + "\r\n")
	if status == 401 {
		b.WriteString("WWW-Authenticate: Basic realm=\"" + BroadcastRealm + "\"\r\n")
	}
	if status == 405 {
		b.WriteString("Allow: GET\r\n")
	}
	if retryAfter > 0 {
		b.WriteString("Retry-After: " + strconv.Itoa(retryAfter) + "\r\n")
	}
	b.WriteString("Content-Length: 0\r\nConnection: close\r\n\r\n")
	return []byte(b.String())
}
