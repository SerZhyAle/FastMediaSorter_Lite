package wire

import (
	"bytes"
	"encoding/json"
	"io"
)

const PinCapability = "pin-v1"

// Only the public lock fact is admitted. Counting tokens also rejects duplicate
// scheme members, which map decoders would silently reduce to one value.
func ValidProtection(raw json.RawMessage) bool {
	decoder := json.NewDecoder(bytes.NewReader(raw))
	opening, err := decoder.Token()
	if err != nil || opening != json.Delim('{') || !decoder.More() {
		return false
	}
	key, err := decoder.Token()
	if err != nil || key != "scheme" {
		return false
	}
	var scheme string
	if decoder.Decode(&scheme) != nil || scheme != PinCapability || decoder.More() {
		return false
	}
	closing, err := decoder.Token()
	if err != nil || closing != json.Delim('}') {
		return false
	}
	_, err = decoder.Token()
	return err == io.EOF
}
