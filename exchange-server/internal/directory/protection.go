package directory

import (
	"encoding/json"
	"fms/exchange-server/internal/wire"
)

// resourcesFor never changes the producer's record. Unknown requesters have no capability.
// Full lists and changed pushes use the same gate, including on re-enrollment.
func (d *Dir) resourcesFor(h *hub, deviceID string, records []json.RawMessage) []json.RawMessage {
	canPin := false
	if device := h.devices[deviceID]; device != nil {
		for _, capability := range device.info.Capabilities {
			if capability == wire.PinCapability {
				canPin = true
			}
		}
	}
	out := make([]json.RawMessage, 0, len(records))
	for _, raw := range records {
		var record map[string]json.RawMessage
		err := json.Unmarshal(raw, &record)
		if err != nil {
			continue
		}
		if protection, has := record["protection"]; has && (!canPin || !wire.ValidProtection(protection)) {
			delete(record, "access")
			delete(record, "shareId")
			delete(record, "publicPort")
			filtered, _ := json.Marshal(record)
			out = append(out, filtered)
		} else {
			out = append(out, raw)
		}
	}
	return out
}
