// Package statement holds the operator statement of DEVICE-EXCHANGE 7.9 ("visible to the operator")
// and ticket 167 rule 8: what the operator of an exchange server can see, said once, in one place.
//
// The text is the English source. The Share Manager's administration surface (ticket 168) renders it
// in 13 languages, keyed by Point.ID - so an id never changes meaning, and a point is added, never
// reworded in place. The statement is shown where an operator sets a server up, because on a
// deployment that hosts other people's accounts it is a limit they must know.
package statement

import (
	"fmt"
	"strings"
)

// WithRetention includes the deployment's actual retention, never a hardcoded operator claim.
func WithRetention(days int) string {
	return Text() + fmt.Sprintf("\n\nLogs use UTC day files and are kept for %d days, with daily cleanup and a per-file size cap.", days)
}

// Point is one sentence-sized fact of the statement.
type Point struct {
	ID   string
	Text string
}

// Points are the statement, in the order it is shown.
var Points = []Point{
	{"relay-visible", "A broadcast sent through this server's relay can be seen by the operator of this server: the " +
		"connection ends at the server, so the server handles the broadcast in the clear. It is never written to disk " +
		"and never kept in memory for longer than about two seconds."},
	{"tunnel-broadcast-plain", "A live broadcast reached through a tunnel is plain RTSP or HTTP inside the connection to " +
		"this server, so the operator can see its bytes while they pass."},
	{"access-visible", "The access details of a shared folder (its address, user name and password) are held in memory " +
		"while the device that shares it is online, and the operator can read them there. They are never written to disk " +
		"and never logged."},
	{"sftp-opaque", "A shared folder reached through a tunnel stays protected by its own SSH connection: the server " +
		"carries bytes it cannot read."},
	{"multi-user-limit", "On a server that hosts other people's accounts this is a known limit of this version: the " +
		"operator can see what the points above describe. Sealing the access details end to end, so that even the " +
		"operator cannot read them, is open work."},
	{"flat-trust", "Every device of an account trusts every other device of that account fully: any device can remove " +
		"another, ask for pairing codes and read the access details of every shared folder of the account. A device " +
		"can always remove itself."},
	{"clipboard-visible", "The last value sent to a clipboard of this server - a text, a picture or one file, within " +
		"the account's size limit - sits in this server's memory and nowhere else: never written to disk, never logged, " +
		"and lost when the server restarts. While it sits there the operator can read it in the clear; one account holds " +
		"one value, the newest that arrived whole."},
}

// Text is the whole statement as plain paragraphs.
func Text() string {
	parts := make([]string, len(Points))
	for i, p := range Points {
		parts[i] = p.Text
	}
	return strings.Join(parts, "\n\n")
}
