Media Exchange Server (fms-exchange-server.exe)
================================================

WHAT IT IS

A small program that lets devices reach each other when neither of them can be reached directly.
A phone or a PC that shares folders registers with it from the inside (an outgoing connection, no
router setting on the sharing device). Another device that knows the share's identifier asks the
server for a connection, and the server joins the two. The server only passes bytes along. The
SFTP session is encrypted from end to end between the two devices, so the server sees ciphertext
and nothing else. It is also what a third-party SFTP client (WinSCP, FileZilla) connects to: every
registered share can be given its own public TCP port.

It is optional. It is not installed, not started and not allowed through the firewall unless you
asked for it, and nothing in the viewer or the Share Manager needs it.

Windows 10 1607 or Server 2016 and later, 64-bit only.


WHAT A DEVICE NEEDS

Every device of yours (the Android app, another copy of this program) enrolls once into an account
that you create on this PC, and keeps only a token afterwards. It needs:

  1. Address   the name or IP address that reaches this PC from the device's network
               (the PC's LAN address inside your own network; your router's public address, or a
               name that points at it, from outside)
  2. Port      the rendezvous port: 22022 unless you changed "listen" in exchange.json
  3. Account   the login and the password of one of the accounts (see "Accounts" below), or a
               pairing code that a device already enrolled in the account made for it
  +  Fingerprint  "SHA256:" followed by the certificate fingerprint. The server prints it when it
               starts and "inspect" prints it any time. The device pins the certificate the first
               time it enrolls; compare the value the device records with the one this PC shows.

COMMANDS (console)

  fms-exchange-server serve [--config <file>] [--service]
  fms-exchange-server stop [--config <file>]
  fms-exchange-server account add <login> [--generate] | passwd <login> [--generate] [--sign-out] | remove <login> | list [--json]
  fms-exchange-server device list <login> [--json] | revoke <login> <deviceId>
  fms-exchange-server set-listen | set-endpoint | set-port-range [--config <file>] <value>
  fms-exchange-server tls replace | import --cert <file> --key <file> [--config <file>]
  fms-exchange-server inspect [--config <file>] [--init]
  fms-exchange-server --version

serve
    Runs the server in the console until Ctrl+C or "stop". On start it prints the version, the
    address it listens on, the per-session port range and the certificate fingerprint. It refuses
    to start while no account exists and never serves nobody. It never replaces a certificate:
    if the key file is damaged it stops and says so instead of creating a new identity.
    The first run writes a commented exchange.json with the default settings.
    "serve --service" is the entry point of the Windows service only. The service control manager
    starts it; typed in a console it is refused (exit code 2).
    A console "serve" also refuses (exit code 5) while the Windows service of this server runs or
    holds the listen port, and says why. The service wins.

stop
    Asks the console server that runs for the same configuration to stop cleanly (registrations
    closed, a "stop" line in the log). It returns at once and does not wait. It does not touch a
    service; a service is stopped from the Windows Services console or from the Share Manager.
    Exit code 0 once the request is delivered, 1 when nothing is running for that configuration.
    Windows only for now.

account, device
    An account is a login and a password; every device you enroll belongs to one account. Only the
    Argon2id hash of a password is ever written, never the password. A password is read as one line
    from standard input, never from the command line (a command line is visible in process lists):

        "Your-Password-1" | fms-exchange-server account add alice

    With --generate the program makes a 20-character password itself, stores its hash and prints
    the password ONCE on standard output. Copy it then; it cannot be shown again, only replaced
    ("account passwd", with --sign-out to sign every device of the account out as well).
    A password under 10 characters, or without an upper-case Latin letter, a lower-case Latin
    letter and a digit, is refused (exit code 3) and nothing is changed.
    "device list" shows the devices of an account and "device revoke" signs one out for good.
    A running server takes these changes at once through its local admin channel (available to the
    administrators and to the account that owns the data directory); with the server stopped the
    same commands change the files directly.

set-listen, set-endpoint, set-port-range, tls
    "set-listen" is the address the server listens on (for example :22022); "set-endpoint" is the
    public address announced to devices (use "-" to clear it); "set-port-range" is the third-party
    port pool (for example 40000-40100). "tls replace" makes a new self-signed identity and
    "tls import" serves a certificate and key from a certificate authority. Each applies at the next
    start. Replacing a certificate makes every device that pinned the old one ask again.
inspect
    Prints a JSON report: data directory, whether a configuration, any account and a
    certificate exist, the fingerprint, the listen address and the port range. It changes nothing.
    With --init it first writes the default configuration and the certificate if they do not exist
    yet, which is the way to read the fingerprint before the first "serve".

--config <file>
    Use that configuration file; its folder becomes the data directory. Use it for a portable
    deployment (for example on a USB stick or beside the program), or to run a second instance.

Exit codes
    0 done            1 a failure at run time (for "stop": nothing is running)
    2 wrong command line, or "serve --service" outside the service control manager
    3 password refused by the policy, or no account exists
    4 the configuration cannot be read or written
    5 a console "serve" refused because the service runs or holds the port


THE DATA DIRECTORY

Nothing mutable is ever kept next to the program: the install folder can be read-only.

  Interactive use     %LOCALAPPDATA%\FastMediaSorterExchange
  Windows service     %ProgramData%\FastMediaSorterExchange   (Server edition only)

The program picks the directory in this order: the folder of --config; the folder named by the
environment variable FMS_EXCHANGE_DATA_DIR; the service folder when it exists and can be opened;
otherwise the per-user folder. So once the service store exists, a console run without --config
uses it too.

  exchange.json       the settings (listen address, port range, limits)
  accounts, devices   the account store (password hashes, token hashes); the enrolled user is given nothing here
  tls\cert.pem        the certificate that devices pin
  tls\key.pem         its private key
  logs\               the logs, readable by the enrolled user and by nobody else outside the administrators:
                      exchange.log (renamed to exchange.log.1 when it reaches 4 MiB), service.log (only when
                      the service has something to report at start-up), install-exchange-service.log and
                      enable-exchange-server.log (one line per run of the helpers)

The certificate is created once and never regenerated by a restart or an update. A new one would
make every device that pinned the old one refuse to connect. To keep the same identity when you
move the server to another PC or between the console and the service, copy the whole tls folder
and exchange.json.

Settings worth knowing in exchange.json (the numbers are working defaults and may change before
the protocol is final):

  "listen"      ":22022"          the rendezvous port
  "portRange"   "40000-40100"     the pool of per-session public ports for third-party SFTP clients
  "tls"         certFile/keyFile  leave empty for the generated certificate; set both to serve a
                                  certificate from a certificate authority instead


MAKING IT REACHABLE

On your own PC, inside your own network, nothing more is needed: devices on the same LAN use the
PC's LAN address and the port. To be reached from outside there are two parts, and both must be
open.

1. The Windows Firewall. A program that listens is not reachable from outside until an inbound
   rule allows it. There are two ways, and you choose; nothing does this for you.
   a) Say yes to Windows' own prompt ("allow access") the first time the server listens. That rule
      is Windows' own and follows what you answer there (it may cover only some network profiles).
   b) Run the helper once. It adds exactly ONE rule, scoped to this program and not to any port,
      named "FastMediaSorter Media Exchange Server", for TCP on every profile. It needs
      administrator rights and Windows asks once (one UAC prompt). In the Share Manager the button
      "Allow access from other devices.." runs it. By hand, from an elevated PowerShell:

        .\enable-exchange-server.ps1 -ExePath "<full path of fms-exchange-server.exe>"

      Because the rule names the program, changing the port or the range later needs no new rule.
      To withdraw it:  .\enable-exchange-server.ps1 -ExePath "<same path>" -Disable
      (this removes every rule aimed at that program, including the one Windows added itself).
      Exit codes: 0 done, 2 the path is not usable, 5 not elevated, 8 netsh failed.
   The helper is never run at install, never by the server itself and never because of a request
   from the network.

2. Your router. If the PC is behind a router, forward these to the PC's LAN address:
   - TCP 22022 (or your "listen" port): the rendezvous port that every device registers with
   - TCP 40000-40100 (or your "portRange"): ONLY if a third-party SFTP client (WinSCP, FileZilla)
     should connect to a share on its own port. Each share that asks for one takes a port from
     this range, so forward the whole range, not a single port, and keep the range as small as
     you need. A share that asks for a public port when the range is used up is refused.
   A device inside the same network does not need the router at all.
   This program never configures a router; the forwards are always set by you.

The server cannot tell whether it can be reached from outside, and says so: it has no connect-back
test and makes no outgoing connection at all. To check, use any online "open port" checker from
another network (or a phone on mobile data) against your public address and port 22022. A closed
port means the firewall rule or the router forward is missing, or your provider does not give you
a public address of your own.


ACCOUNTS

An account decides which devices belong together: a device sees and reaches only the devices of its
own account. The password rules are fixed: at least 10 characters, with an upper-case Latin letter,
a lower-case Latin letter and a digit. A password is typed once, when the first device enrolls;
after that the device holds a token, and further devices are added with a pairing code made by one
already enrolled. Prefer "account add --generate". In the Share Manager the group "Media Exchange
Server" manages accounts and devices and has the "Use this PC as my server" wizard.

RUNNING IT AS A WINDOWS SERVICE (Server edition only)

The Server edition can run this program as the service FastMediaSorterExchangeServer, so it serves
from boot with nobody signed in. It runs as LocalService with its data in
%ProgramData%\FastMediaSorterExchange. It is never registered by the viewer edition, by the
Microsoft Store package, by a default or silent install, or by the server itself. Only two things
register it: the Server installer with the service choice ticked (it is not ticked by default), or
one visible administrator prompt from the Share Manager. That same act may add the firewall rule
above.

While the service runs, the Share Manager shows and controls it, and a console "serve" refuses to
start beside it. Moving from a console deployment to the service copies the configuration and the
certificate once, and only into a service store that does not hold an identity yet, so a device
that pinned the certificate keeps connecting. An update keeps the certificate.


WHAT IT KEEPS

It keeps no file content and no list of sessions, and writes nothing about a session to disk:
presence, registrations and tunnels live in memory and are gone when the process ends. The log says that a
registration or a tunnel began or ended, the client's IP address (never the port), counts of bytes,
and refusals. It names a session only by eight characters of a one-way hash of its identifier,
never by the identifier, a share name or a file name, and never writes a password. The only
connections the program makes are the ones that devices open to it.


REMOVING IT, AND EVERYTHING IT LEFT

1. Stop it: Ctrl+C in its console, "stop", the Share Manager, or the Services console for the
   service.
2. Uninstall the program from Windows Settings, Apps. This removes the program files, the
   firewall rule added by the helper, and the service (Server edition). It keeps the data
   directory on purpose, so that a reinstall serves the same certificate and every pinned device
   keeps working. A silent uninstall cannot ask, so for an install made for your account only it
   may leave the firewall rule; remove it first with the helper and -Disable, or in Windows
   Defender Firewall with Advanced Security, Inbound Rules, "FastMediaSorter Media Exchange
   Server".
3. Check:  netsh advfirewall firewall show rule name="FastMediaSorter Media Exchange Server"
   should say that no rules match. Any rule Windows added itself from its own prompt is named
   after the program and is listed in the same Inbound Rules view.
4. Delete the data directory by hand, in File Explorer, for each place that exists:
   %LOCALAPPDATA%\FastMediaSorterExchange and %ProgramData%\FastMediaSorterExchange.
   Setup never deletes it. Deleting it throws the certificate away: a device that pinned it must be
   paired again, and the accounts go with it.
5. Remove the forwards you made on your router.
