// A separate module on purpose: this manual-proof tool serves a real SFTP subsystem (pkg/sftp),
// and that dependency must not enter the exchange server's own go.mod. It reuses the server
// module's test harness through a replace directive.
module fms/exchange-server/cmd/exchange-fakeproducer

go 1.26

toolchain go1.26.8

require (
	fms/exchange-server v0.0.0
	github.com/pkg/sftp v1.13.10
	golang.org/x/crypto v0.54.0
)

require (
	github.com/kr/fs v0.1.0 // indirect
	golang.org/x/sys v0.47.0 // indirect
)

replace fms/exchange-server => ../..
