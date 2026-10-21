package admin

import (
	"encoding/json"
	"fmt"
	"testing"
	"time"

	"fms/exchange-server/internal/clipboard"
	"fms/exchange-server/internal/directory"
	"fms/exchange-server/internal/wire"
)

type dummySink struct{}

func (dummySink) Send(wire.Envelope) error { return nil }
func (dummySink) SendFrame([]byte) error  { return nil }
func (dummySink) Close()                  {}

func TestManagementVisibilityOperations(t *testing.T) {
	o, log := newOps(t)
	o.Live = true
	o.Audit = NewAuditTracker()
	clipTable := clipboard.NewTable(time.Now, nil)
	o.Clipboard = clipTable

	ask := serve(t, o, log.Logger)

	// 1. Add account and enroll device
	_, _ = o.AddAccount("alice", pw1)
	devID := wire.NewID()
	o.Dir.DeviceEnrolled(directory.DeviceInfo{
		ID: devID, Account: "alice", Name: "Alice Phone", Product: "FMS_A",
	})
	o.Dir.Online("alice", devID, dummySink{})

	// 2. Publish resource
	resID := wire.NewID()
	rawRes := json.RawMessage(fmt.Sprintf(`{"resourceId":%q,"deviceId":%q,"name":"Photos","kind":"folder"}`, resID, devID))
	_, err := o.Dir.Publish("alice", devID, rawRes)
	if err != nil {
		t.Fatalf("publish failed: %v", err)
	}

	// Test resource.list
	resp, _ := ask(Request{Op: OpResourceList, Login: "alice"})
	if !resp.OK {
		t.Fatalf("resource.list failed: %s", resp.Error)
	}
	var resList []directory.ResourceView
	if err := json.Unmarshal(resp.Data, &resList); err != nil {
		t.Fatalf("unmarshal resource list: %v", err)
	}
	if len(resList) != 1 || resList[0].ResourceID != resID || resList[0].Name != "Photos" || resList[0].DeviceName != "Alice Phone" {
		t.Fatalf("unexpected resource list: %+v", resList)
	}

	// 3. Start live broadcast
	broID := wire.NewID()
	rawBro := json.RawMessage(fmt.Sprintf(`{"broadcastId":%q,"deviceId":%q,"mode":"screen","descriptor":{"width":1920,"height":1080}}`, broID, devID))
	maxL := 10
	_, err = o.Dir.BroadcastStart("alice", devID, rawBro, &maxL)
	if err != nil {
		t.Fatalf("broadcast start failed: %v", err)
	}

	// Test broadcast.list
	resp, _ = ask(Request{Op: OpBroadcastList, Login: "alice"})
	if !resp.OK {
		t.Fatalf("broadcast.list failed: %s", resp.Error)
	}
	var broList []directory.BroadcastView
	if err := json.Unmarshal(resp.Data, &broList); err != nil {
		t.Fatalf("unmarshal broadcast list: %v", err)
	}
	if len(broList) != 1 || broList[0].BroadcastID != broID || broList[0].Kind != "screen" || broList[0].MaxListeners != 10 {
		t.Fatalf("unexpected broadcast list: %+v", broList)
	}

	// 4. Clipboard operations
	// Initially empty
	resp, _ = ask(Request{Op: OpClipboardInfo, Login: "alice"})
	if !resp.OK {
		t.Fatalf("clipboard.info failed: %s", resp.Error)
	}
	var clipInfo clipboard.ClipboardSlotView
	if err := json.Unmarshal(resp.Data, &clipInfo); err != nil {
		t.Fatalf("unmarshal clipboard info: %v", err)
	}
	if clipInfo.Present {
		t.Fatalf("expected clipboard slot to be empty, got: %+v", clipInfo)
	}

	// Put clipboard content
	put, err := clipTable.BeginPut("alice", clipboard.Decl{Kind: "text", Size: 5}, devID, "Alice Phone")
	if err != nil {
		t.Fatalf("begin put failed: %v", err)
	}
	put.Write([]byte("hello"))
	val := put.Complete()
	if val == nil {
		t.Fatalf("put complete failed")
	}

	// Inspect clipboard slot
	resp, _ = ask(Request{Op: OpClipboardInfo, Login: "alice"})
	if !resp.OK {
		t.Fatalf("clipboard.info failed: %s", resp.Error)
	}
	if err := json.Unmarshal(resp.Data, &clipInfo); err != nil {
		t.Fatalf("unmarshal clipboard info: %v", err)
	}
	if !clipInfo.Present || clipInfo.SizeBytes != 5 || clipInfo.Kind != "text" || clipInfo.SourceDeviceName != "Alice Phone" {
		t.Fatalf("unexpected clipboard slot: %+v", clipInfo)
	}

	// Erase clipboard
	resp, _ = ask(Request{Op: OpClipboardErase, Login: "alice"})
	if !resp.OK {
		t.Fatalf("clipboard.erase failed: %s", resp.Error)
	}
	resp, _ = ask(Request{Op: OpClipboardInfo, Login: "alice"})
	var afterErase clipboard.ClipboardSlotView
	_ = json.Unmarshal(resp.Data, &afterErase)
	if afterErase.Present {
		t.Fatalf("clipboard should be empty after erase")
	}

	// 5. Test broadcast.end
	resp, _ = ask(Request{Op: OpBroadcastEnd, Login: "alice", BroadcastID: broID})
	if !resp.OK {
		t.Fatalf("broadcast.end failed: %s", resp.Error)
	}
	resp, _ = ask(Request{Op: OpBroadcastList, Login: "alice"})
	_ = json.Unmarshal(resp.Data, &broList)
	if len(broList) != 0 {
		t.Fatalf("broadcast list should be empty after end, got: %+v", broList)
	}

	// 6. Test resource.unpublish
	resp, _ = ask(Request{Op: OpResourceUnpublish, Login: "alice", ResourceID: resID})
	if !resp.OK {
		t.Fatalf("resource.unpublish failed: %s", resp.Error)
	}
	resp, _ = ask(Request{Op: OpResourceList, Login: "alice"})
	_ = json.Unmarshal(resp.Data, &resList)
	if len(resList) != 0 {
		t.Fatalf("resource list should be empty after unpublish, got: %+v", resList)
	}

	// 7. Test admin.audit.list
	resp, _ = ask(Request{Op: OpAdminAuditList})
	if !resp.OK {
		t.Fatalf("admin.audit.list failed: %s", resp.Error)
	}
	var auditView AdminAuditView
	if err := json.Unmarshal(resp.Data, &auditView); err != nil {
		t.Fatalf("unmarshal audit: %v", err)
	}
	if len(auditView.Records) == 0 {
		t.Fatalf("expected recorded audit entries, got none")
	}
	// Verify some recorded ops are in audit
	foundErase := false
	for _, r := range auditView.Records {
		if r.Op == OpClipboardErase && r.Target == "alice" && r.Status == "OK" {
			foundErase = true
			break
		}
	}
	if !foundErase {
		t.Fatalf("clipboard.erase not found in audit records: %+v", auditView.Records)
	}
}
