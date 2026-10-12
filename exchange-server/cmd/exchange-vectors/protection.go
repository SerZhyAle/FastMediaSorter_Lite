package main

import (
	"encoding/json"
	"fms/exchange-server/internal/testkit"
	"fms/exchange-server/internal/wire"
	"fmt"
)

func scenarioProtection() *transcript {
	r := newRig("resource-protection", rigOpts{})
	defer mustClose(r)
	tr := r.newTranscript("resource-protection", "The original resource PIN is never sent to discovery. Legacy consumers see the lock without access; capable consumers see the descriptor. Removing the resource removes it for both.")
	producer, dev, _ := r.enroll(tr, "producer", account, "Sharing PC", "fms-windows", roles("resource-producer"), "")
	r.control(producer, dev)
	legacy, legacyDev, _ := r.enroll(tr, "legacy", account, "Legacy phone", "fms-android", roles("resource-consumer"), "")
	r.control(legacy, legacyDev)
	capable := r.open(tr, "capable", "tls", "a consumer that implements the original PIN proof")
	capableDev := r.newID()
	capable.send(&wire.Enroll{Login: account, Password: testkit.TestPassword, DeviceID: capableDev, DeviceName: "PIN viewer", Product: "fms-windows", Roles: roles("resource-consumer"), Capabilities: []string{wire.PinCapability}})
	capable.expect(wire.TypeEnrolled)
	r.control(capable, capableDev)
	id := r.newID()
	producer.send(&wire.Publish{Resource: json.RawMessage(fmt.Sprintf(`{"resourceId":%q,"kind":"sftp-share","name":"Private","protection":{"scheme":"pin-v1"},"access":{"descriptor":"FMSSFTP2:synthetic-descriptor","root":"/Private"}}`, id))})
	producer.expect(wire.TypePublished)
	for _, consumer := range []*sess{legacy, capable} {
		consumer.send(&wire.List{})
		answer := consumer.expect(wire.TypeDirectory).(*wire.Directory)
		if len(answer.Resources) != 1 {
			failf("protected record missing")
		}
		record, err := wire.ParseRecord(answer.Resources[0])
		if err != nil || !record.Has("protection") || record.Has("access") != (consumer == capable) {
			failf("protection capability gate mismatch")
		}
		if consumer == capable {
			tr.note("Drive wrapper for a protected resource: " + wrapDrive("resource", id, answer.Resources[0]).Text)
		}
	}
	producer.send(&wire.Unpublish{ResourceID: id})
	producer.send(&wire.Keepalive{})
	producer.expect(wire.TypeKeepalive)
	for _, consumer := range []*sess{legacy, capable} {
		consumer.send(&wire.List{})
		if len(consumer.expect(wire.TypeDirectory).(*wire.Directory).Resources) != 0 {
			failf("unpublished protected resource retained")
		}
	}
	return tr
}
