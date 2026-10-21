package admin

import (
	"encoding/json"
	"fmt"
	"sort"
	"strings"

	"fms/exchange-server/internal/logx"
)

// HandleAudited preserves the ordinary authorization of each transport. Only the caller supplies
// authenticated context; request JSON never supplies an operator identity or a peer address.
func (o *Ops) HandleAudited(req Request, context logx.AuditEvent) Response {
	if !isMutation(req.Op) {
		return o.Handle(req)
	}
	o.auditMu.Lock()
	defer o.auditMu.Unlock()
	before := o.auditSetting(req)
	resp := o.Handle(req)
	context.Op, context.Actor = req.Op, "OPERATOR"
	context.Target = req.Login
	if context.Target == "" {
		context.Target = req.DeviceID
	}
	if context.Target == "" {
		context.Target = req.BroadcastID
	}
	if context.Target == "" {
		context.Target = req.ResourceID
	}
	if context.Target == "" {
		context.Target = req.TargetID
	}
	context.Status = logx.AuditStatusOK
	if !resp.OK {
		context.Status = logx.AuditRefusal(resp.Code)
		context.Details = "change refused"
	} else if before != "" {
		context.Details = settingDelta(before, o.auditSetting(req))
	} else {
		// No request/reply serialization: these operations can carry secrets and descriptors.
		context.Details = "state changed"
		if req.Op == OpAccountPasswd {
			context.Details = "password changed"
		}
	}
	o.AuditLog.Record(context)
	return resp
}

// Compact property deltas keep the changed value visible within the bounded DETAILS member.
func settingDelta(before, after string) string {
	var old, current map[string]any
	if json.Unmarshal([]byte(before), &old) != nil || json.Unmarshal([]byte(after), &current) != nil {
		return before + " -> " + after
	}
	var changes []string
	var walk func(string, map[string]any, map[string]any)
	walk = func(prefix string, a, b map[string]any) {
		keys := make([]string, 0, len(b))
		for key := range b {
			keys = append(keys, key)
		}
		sort.Strings(keys)
		for _, key := range keys {
			if bm, ok := b[key].(map[string]any); ok {
				am, _ := a[key].(map[string]any)
				walk(prefix+key+".", am, bm)
			} else if fmt.Sprint(a[key]) != fmt.Sprint(b[key]) {
				changes = append(changes, fmt.Sprintf("%s%s: %v -> %v", prefix, key, a[key], b[key]))
			}
		}
	}
	walk("", old, current)
	if len(changes) == 0 {
		return "settings unchanged"
	}
	return strings.Join(changes, "; ")
}

func isMutation(op string) bool {
	switch op {
	case OpAccountAdd, OpAccountPasswd, OpAccountKind, OpAccountRemove, OpAccountLimits,
		OpDeviceRevoke, OpDevicePairingCode, OpInvitationCreate, OpInvitationWithdraw,
		OpSharedInvitationSet, OpPolicySet, OpGrantAdd, OpGrantRemove,
		OpBroadcastEnd, OpResourceUnpublish, OpClipboardErase, OpHostSettings:
		return true
	}
	return false
}

// Only these explicitly safe settings may be rendered as deltas. Never marshal a request or account.
func (o *Ops) auditSetting(req Request) string {
	var value any
	switch req.Op {
	case OpAccountKind:
		return "kind=" + o.Accounts.KindOf(req.Login)
	case OpAccountLimits:
		value = o.Accounts.LimitsFor(req.Login)
	case OpPolicySet:
		value = o.Accounts.Policy()
	case OpHostSettings:
		if o.ReadHostSettings == nil {
			return ""
		}
		awake, days := o.ReadHostSettings()
		return fmt.Sprintf("keepAwake=%t logRetentionDays=%d", awake, days)
	default:
		return ""
	}
	b, _ := json.Marshal(value)
	return string(b)
}
