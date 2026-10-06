# Manual proof: WinSCP or FileZilla through a per-session port (ticket 137 T-4)

The automated stand-in is `TestSFTPListsAndReadsThroughThePerSessionPort` in
`cmd/exchange-fakeproducer`: a real SFTP client lists a folder and reads a file through the bridge.
This page is the by-hand step with the real tools. The joint proof with the phone producer is
ticket 140's, not this one's.

## Run it

1. Build both programs (use a scratch folder for everything; the fake producer serves a real directory):

   ```powershell
   .\exchange-server\build.ps1 -OutDir $env:TEMP\fms-ex
   cd exchange-server\cmd\exchange-fakeproducer; go build -o $env:TEMP\fms-ex\fakeproducer.exe .
   ```

2. Give the server a password and a loopback address, then start it:

   ```powershell
   $env:FMS_EXCHANGE_DATA_DIR = "$env:TEMP\fms-ex\data"
   $pw = & $env:TEMP\fms-ex\fms-exchange-server.exe set-password --generate     # prints the password once
   & $env:TEMP\fms-ex\fms-exchange-server.exe serve                              # prints the fingerprint
   ```

   The default listen address is `:22022` and the port range `40000-40100` (edit `exchange.json`
   under `$env:FMS_EXCHANGE_DATA_DIR` to change either, then restart). Windows may ask to allow the
   program through the firewall on first listen; allow it for a private network only for this test.

3. In a second console, register a share with a third-party port:

   ```powershell
   & $env:TEMP\fms-ex\fakeproducer.exe --server 127.0.0.1:22022 --fingerprint SHA256:<from step 2> `
       --server-password $pw --port 0 --root C:\scratch-folder
   ```

   It prints the allocated port, for example `third-party port 40000`.

4. Point **WinSCP** (File protocol: SFTP) or **FileZilla** (Protocol: SFTP) at host `127.0.0.1`,
   the printed port, user `tester`, password `tester-pass`. Accept the host key. List the folder,
   download a file, upload a file.

## What to look for

* The listing is the scratch folder, through a connection that crossed the server.
* In the server's console and `exchange.log` no line carries the share id; a session is `share=<8 hex>`.
* Stop the fake producer (Ctrl+C): its control stream drops, and the port stops accepting once the
  missed-keepalive window passes (about 90 s at the default 30 s interval - a dropped stream is not
  an `unregister`, so the registration waits for a possible resume).
* Restart `serve`: the same fingerprint is printed (the identity is never regenerated).

## Scripted variant (what the 2026-10-06 proof ran)

`WinSCP.com` (`winget install WinSCP.WinSCP`) can drive step 4 without the GUI. With the server and the fake
producer running as above and `<port>` the printed port:

```text
open sftp://tester:tester-pass@127.0.0.1:<port>/ -hostkey=*
ls
get big.bin C:\scratch-out\
put C:\scratch-out\up.txt /up.txt
mkdir sub
mv /up.txt /renamed.txt
exit
```

```powershell
& "$env:LOCALAPPDATA\Programs\WinSCP\WinSCP.com" /ini=nul /log=winscp.log /script=winscp.script
```

Compare `Get-FileHash` of the source and the downloaded file. The fake producer serves the folder through
`os.Root`, so an absolute path in `put` lands inside the scratch folder, never at the drive root.
