package logx

import (
	"strings"
	"testing"
)

func TestStateKeepsEventAndRedactsIdentifiers(t *testing.T) {
	c := NewCapture()
	c.State("account-added", Login("private-login"), Device("private-device"))
	line := c.Lines()[0]
	fields := strings.Split(line, "\t")
	if fields[2] != "account-added" || !strings.Contains(line, "state=[STATE-CHANGE]") {
		t.Fatalf("state event lost its searchable event or marker: %q", line)
	}
	c.AssertNone(t, "private-login", "private-device")
	var absent *Logger
	absent.State("stop")
}
