// Command exchange-vectors generates the conformance vectors of DEVICE-EXCHANGE (ticket 205 T-1): the
// frames of the server wire and the records of section 6, produced by running the REAL exchange server
// against a scripted client with an injected clock, id source and random source.
//
//	go run ./cmd/exchange-vectors -out testdata/vectors          write the files
//	go run ./cmd/exchange-vectors -check -out testdata/vectors   regenerate and compare with what is there
//
// The generator is the only author of these files: a hand edit is detected by the hash in PROVENANCE.txt,
// and regenerating is the only way to change them. They are developer artifacts and do not ship.
//
// -check prints ONE verdict line (CHECK-VERDICT): `exchange-vectors: PASS` when the files on disk are
// byte for byte what the code generates now and PROVENANCE.txt names their hashes, `FAIL` with the
// first difference otherwise; exit 0 pass, 1 differ, 2 could not verify (no files, a generator error).
package main

import (
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
)

func main() { os.Exit(run(os.Args[1:], os.Stdout, os.Stderr)) }

func run(args []string, stdout, stderr io.Writer) int {
	fs := flag.NewFlagSet("exchange-vectors", flag.ContinueOnError)
	fs.SetOutput(stderr)
	out := fs.String("out", filepath.Join("testdata", "vectors"), "the directory the vectors are written to, or checked in")
	commit := fs.String("commit", "", "the commit the generator ran at, recorded in PROVENANCE.txt (git rev-parse HEAD)")
	check := fs.Bool("check", false, "regenerate and compare with the files in -out instead of writing")
	if err := fs.Parse(args); err != nil {
		return 2
	}
	files, err := generate(*commit)
	if err == nil {
		err = closeErr
	}
	if err != nil {
		fmt.Fprintf(stdout, "exchange-vectors: COULD NOT VERIFY - the generator failed: %v\n", err)
		return 2
	}
	if !*check {
		if err := os.MkdirAll(*out, 0o755); err != nil {
			fmt.Fprintf(stderr, "%v\n", err)
			return 2
		}
		for name, b := range files {
			if err := os.WriteFile(filepath.Join(*out, name), b, 0o644); err != nil {
				fmt.Fprintf(stderr, "%v\n", err)
				return 2
			}
		}
		fmt.Fprintf(stdout, "exchange-vectors: wrote %d files to %s\n", len(files), *out)
		return 0
	}
	disk := map[string][]byte{}
	for _, name := range append(append([]string(nil), hashedFiles...), provenanceFileName) {
		b, err := os.ReadFile(filepath.Join(*out, name))
		if err != nil {
			fmt.Fprintf(stdout, "exchange-vectors: COULD NOT VERIFY - %v\n", err)
			return 2
		}
		disk[name] = b
	}
	for _, name := range hashedFiles {
		if string(disk[name]) != string(files[name]) {
			fmt.Fprintf(stdout, "exchange-vectors: FAIL - %s is not what the generator produces now (regenerate with: go run ./cmd/exchange-vectors -out %s)\n", name, *out)
			return 1
		}
	}
	if err := checkProvenance(disk, string(disk[provenanceFileName])); err != nil {
		fmt.Fprintf(stdout, "exchange-vectors: FAIL - %v\n", err)
		return 1
	}
	fmt.Fprintln(stdout, "exchange-vectors: PASS (4 files, byte for byte, hashes in PROVENANCE.txt)")
	return 0
}
