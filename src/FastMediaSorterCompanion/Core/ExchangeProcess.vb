Option Strict On

Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Locates, runs and stops the bundled exchange server (ticket 142, 133 section 5): the console program
''' <c>exchange\fms-exchange-server.exe</c> beside the Share Manager.
'''
''' It differs from <see cref="WorkerProcess"/> on purpose. The worker is a background sidecar that
''' outlives the Share Manager; the exchange server is a console program started for the user, on a click,
''' and stopped on request:
'''   * nothing starts it but <see cref="Start"/> (no autostart, 133 section 3);
'''   * it is stopped by a STOP REQUEST - the binary's own <c>stop</c> subcommand signals the named event
'''     the running console instance waits on, so registrations close cleanly and the server logs its stop
'''     line - and only after 5 s without an exit is it killed;
'''   * the Share Manager stops a server it started itself when it exits, unless
'''     <see cref="ExchangeSettings.KeepRunningOnExit"/> is on (<see cref="StopStartedOnExit"/>);
'''   * an account's password goes to the binary over STDIN (or the admin pipe, <see cref="ExchangeAdmin"/>)
'''     and never appears on a command line, in the registry or in any log.
'''
''' The binary owns the accounts and their password hashing, the certificate and the config file; this
''' module only drives it (ticket 168: the accounts model of 162 replaced the single server password and
''' its <c>set-password</c> verb). Everything the UI needs to know about the server's state comes from
''' <c>inspect</c> (JSON on stdout, counts and a fingerprint - never a login, a hash or a secret), the
''' process list and, for the service, <see cref="ExchangeService"/>.
''' </summary>
Friend Module ExchangeProcess

    ''' <summary>Payload exe file name.</summary>
    Friend Const ExeName As String = "fms-exchange-server.exe"

    ''' <summary>Process name for Process.GetProcessesByName (no extension).</summary>
    Friend Const ProcessName As String = "fms-exchange-server"

    ''' <summary>The folder beside the Share Manager that holds the exe and its two helpers.</summary>
    Friend Const FolderName As String = "exchange"

    ''' <summary>How long a start waits for an immediate refusal (no password, bad config, port taken, the
    ''' service is running) before it reports "started". The server answers all of those in well under a
    ''' second; a healthy one is still running when the wait ends.</summary>
    Friend Const StartupGraceMs As Integer = 1500

    ''' <summary>How long a stop request may take before the process is killed (ticket 142: 5 s).</summary>
    Friend Const StopWaitMs As Integer = 5000

    ''' <summary>A one-shot subcommand (inspect, set-endpoint, an offline account change) is quick; this bounds a hung one.</summary>
    Friend Const CommandTimeoutMs As Integer = 20000

    ' The binary's exit codes (cmd/fms-exchange-server/main.go).
    Friend Const ExitOk As Integer = 0
    Friend Const ExitFailure As Integer = 1
    Friend Const ExitUsage As Integer = 2
    Friend Const ExitPolicy As Integer = 3
    Friend Const ExitConfig As Integer = 4
    Friend Const ExitBesideService As Integer = 5

    ''' <summary>The data-directory override the server itself honours (config.EnvDataDir).</summary>
    Friend Const DataDirEnvironmentName As String = "FMS_EXCHANGE_DATA_DIR"

    ' --- test seams: nothing here is reachable by a user --------------------------------

    ''' <summary>Tests point the module at a scratch folder holding a copy of the binary. Nothing else sets it.</summary>
    Friend ExeDirectoryOverride As String

    ''' <summary>Tests (and only tests) run the real binary against a scratch data directory.</summary>
    Friend DataDirOverride As String

    ''' <summary>One run of the binary: the exit code and what it wrote.</summary>
    Friend Structure RunResult
        Public Started As Boolean
        Public TimedOut As Boolean
        Public ExitCode As Integer
        Public StdOut As String
        Public StdErr As String
    End Structure

    ''' <summary>Tests replace the process launch with a function over (arguments, stdin text). Nothing
    ''' else sets it; the default launches the real binary.</summary>
    Friend RunOverride As Func(Of String(), String, RunResult)

    ' --- location -----------------------------------------------------------------------

    ''' <summary>&lt;app dir&gt;\exchange, or "" when it cannot be resolved.</summary>
    Friend Function ExeDirectory() As String
        If Not String.IsNullOrEmpty(ExeDirectoryOverride) Then Return ExeDirectoryOverride
        Try
            Dim baseDir As String = Path.GetDirectoryName(Application.ExecutablePath)
            If String.IsNullOrEmpty(baseDir) Then Return ""
            Return Path.Combine(baseDir, FolderName)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Absolute path of the exe, or "" if it cannot be resolved.</summary>
    Friend Function ExePath() As String
        Dim dir As String = ExeDirectory()
        If dir.Length = 0 Then Return ""
        Return Path.Combine(dir, ExeName)
    End Function

    ''' <summary>True when the component is installed. Everything about the exchange server in the
    ''' Share Manager - the settings group, the tray item - exists only while this is true: without the
    ''' component the program is exactly what it was.</summary>
    Friend Function IsAvailable() As Boolean
        Dim p As String = ExePath()
        Return p.Length > 0 AndAlso File.Exists(p)
    End Function

    ' --- what the binary reports ---------------------------------------------------------

    ''' <summary>The parsed <c>inspect</c> report. <see cref="Answered"/> False means the binary could not be
    ''' asked or printed no JSON - never "no account"; callers keep the two apart. The report holds COUNTS
    ''' and the public facts of the certificate and the addresses: no login, hash, token or key (162).</summary>
    Friend NotInheritable Class InspectReport
        ' The bundled binary seals. A live status, when available, overrides this setup-time fact.
        Public Property Sealed As Boolean = True
        Public Property Answered As Boolean
        Public Property ExitCode As Integer
        Public Property DataDir As String = ""
        Public Property ConfigPresent As Boolean
        ''' <summary>How many accounts the stores hold. The server refuses to start with none.</summary>
        Public Property Accounts As Integer
        Public Property Devices As Integer
        Public Property CertPresent As Boolean
        ''' <summary>exchange.json names a certificate and key to serve (an import) instead of the generated
        ''' self-signed identity.</summary>
        Public Property CertImported As Boolean
        Public Property Fingerprint As String = ""
        Public Property Listen As String = ""
        Public Property PortRange As String = ""
        ''' <summary>The <c>host:port</c> announced in every welcome, "" while none is set.</summary>
        Public Property PublicEndpoint As String = ""
        Public Property ErrorText As String = ""
        Public Property KeepAwake As Boolean
        Public Property LogRetentionDays As Integer = 31
        Public Property MaxAccounts As Integer = 10
        Public Property AdminOn As Boolean
        Public Property AdminPasswordSet As Boolean
        Public Property AdminAllowedAddresses As String() = Array.Empty(Of String)()
        Public Property AdminSessionLifetimeMinutes As Integer = 15
        Public Property WebDir As String = ""

        ''' <summary>The listen port from the endpoint ("" host or not), or 0 when it has none.</summary>
        Public ReadOnly Property Port As Integer
            Get
                Return ExchangeProcess.PortOfListen(Listen)
            End Get
        End Property

        ''' <summary>At least one account exists: the server can start (162 - it refuses to serve nobody).</summary>
        Public ReadOnly Property Configured As Boolean
            Get
                Return Accounts > 0
            End Get
        End Property

        ''' <summary>&lt;DataDir&gt;\exchange.json - the file the binary reads and writes.</summary>
        Public ReadOnly Property ConfigPath As String
            Get
                Return If(DataDir.Length = 0, "", Path.Combine(DataDir, "exchange.json"))
            End Get
        End Property
    End Class

    ''' <summary>Parses the JSON <c>inspect</c> prints, even when it exits non-zero (it reports a problem
    ''' in its <c>error</c> member and still prints). Unknown members are ignored, a missing one is the
    ''' zero value, and text that is not a JSON object is "not answered".</summary>
    Friend Function ParseInspect(json As String, exitCode As Integer) As InspectReport
        Dim report As New InspectReport With {.ExitCode = exitCode}
        If String.IsNullOrWhiteSpace(json) Then Return report
        Try
            Using doc As JsonDocument = JsonDocument.Parse(json)
                Dim root As JsonElement = doc.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Return report
                report.DataDir = ReadString(root, "dataDir")
                report.ConfigPresent = ReadBool(root, "configPresent")
                report.Accounts = ReadInt(root, "accounts")
                report.Devices = ReadInt(root, "devices")
                report.CertPresent = ReadBool(root, "certPresent")
                report.CertImported = ReadBool(root, "certImported")
                report.Fingerprint = ReadString(root, "fingerprint")
                report.Listen = ReadString(root, "listen")
                report.PortRange = ReadString(root, "portRange")
                report.PublicEndpoint = ReadString(root, "publicEndpoint")
                report.ErrorText = ReadString(root, "error")
                report.KeepAwake = ReadBool(root, "keepAwake")
                Dim days As Integer = ReadInt(root, "logRetentionDays")
                report.LogRetentionDays = If(days > 0, days, 31)
                Dim maxAcc As Integer = ReadInt(root, "maxAccounts")
                report.MaxAccounts = If(maxAcc > 0, maxAcc, 10)
                report.AdminOn = ReadBool(root, "adminOn")
                report.AdminPasswordSet = ReadBool(root, "adminPasswordSet")
                report.AdminAllowedAddresses = ReadStringArray(root, "adminAllowedAddresses")
                Dim life As Integer = ReadInt(root, "adminSessionLifetimeMinutes")
                report.AdminSessionLifetimeMinutes = If(life > 0, life, 15)
                report.WebDir = ReadString(root, "webDir")
                report.Answered = True
            End Using
        Catch ex As JsonException
            report.Answered = False
        End Try
        Return report
    End Function

    Private Function ReadStringArray(root As JsonElement, name As String) As String()
        Dim v As JsonElement = Nothing
        If root.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.Array Then
            Dim list As New List(Of String)()
            For Each item As JsonElement In v.EnumerateArray()
                If item.ValueKind = JsonValueKind.String Then
                    Dim s As String = item.GetString()
                    If Not String.IsNullOrEmpty(s) Then list.Add(s)
                End If
            Next
            Return list.ToArray()
        End If
        Return Array.Empty(Of String)()
    End Function

    Private Function ReadString(root As JsonElement, name As String) As String
        Dim v As JsonElement = Nothing
        If root.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then Return If(v.GetString(), "")
        Return ""
    End Function

    Private Function ReadBool(root As JsonElement, name As String) As Boolean
        Dim v As JsonElement = Nothing
        Return root.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.True
    End Function

    Private Function ReadInt(root As JsonElement, name As String) As Integer
        Dim v As JsonElement = Nothing
        Dim n As Integer
        If root.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.Number AndAlso v.TryGetInt32(n) AndAlso n >= 0 Then Return n
        Return 0
    End Function

    ''' <summary>The port of a listen endpoint (":22022", "127.0.0.1:22022", "[::1]:22022"), or 0.</summary>
    Friend Function PortOfListen(listen As String) As Integer
        If String.IsNullOrEmpty(listen) Then Return 0
        Dim colon As Integer = listen.LastIndexOf(":"c)
        If colon < 0 Then Return 0
        Dim n As Integer
        If Integer.TryParse(listen.Substring(colon + 1), Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, n) AndAlso n >= 0 AndAlso n <= 65535 Then Return n
        Return 0
    End Function

    ''' <summary>Asks the binary what it holds (read-only: creates nothing).</summary>
    Friend Function Inspect() As InspectReport
        If Not IsAvailable() Then Return New InspectReport()
        Dim r As RunResult = Run({"inspect"}, Nothing, CommandTimeoutMs)
        If Not r.Started OrElse r.TimedOut Then Return New InspectReport With {.ExitCode = r.ExitCode}
        Return ParseInspect(r.StdOut, r.ExitCode)
    End Function


    ''' <summary>Asks the binary what the named data directory holds - the registered service's - for this one run (the
    ''' directory goes to the child's environment, not into <see cref="DataDirOverride"/>). The enrolled user may not read the
    ''' stores there, so <c>accounts</c> is not to be trusted and the report may carry an error; its address and the fingerprint of
    ''' the certificate (a file the user may read) are what this is for.</summary>
    Friend Function InspectIn(dataDir As String) As InspectReport
        If Not IsAvailable() Then Return New InspectReport()
        Dim r As RunResult = Run({"inspect"}, Nothing, CommandTimeoutMs, dataDir)
        If Not r.Started OrElse r.TimedOut Then Return New InspectReport With {.ExitCode = r.ExitCode}
        Return ParseInspect(r.StdOut, r.ExitCode)
    End Function

    ' --- the listen port --------------------------------------------------------------------

    Friend Enum ListenOutcome
        Saved
        ''' <summary>Exit 2: the binary refused the address (it validates with the serve rules).</summary>
        BadAddress
        ''' <summary>Exit 4: the config cannot be read or written.</summary>
        ConfigProblem
        Failed
        Unavailable
    End Enum

    ''' <summary>The endpoint with its port replaced and its host kept: ":22022" -> ":2222",
    ''' "127.0.0.1:22022" -> "127.0.0.1:2222", "[::1]:22022" -> "[::1]:2222". An endpoint with no colon
    ''' (or an empty one) becomes ":port".</summary>
    Friend Function WithPort(listen As String, port As Integer) As String
        Dim p As String = port.ToString(Globalization.CultureInfo.InvariantCulture)
        If String.IsNullOrEmpty(listen) Then Return ":" & p
        Dim colon As Integer = listen.LastIndexOf(":"c)
        If colon < 0 Then Return ":" & p
        Return listen.Substring(0, colon + 1) & p
    End Function

    ''' <summary>Writes the listen endpoint THROUGH the binary (<c>set-listen</c>): it validates with the serve
    ''' rules, stores only <c>listen</c> and keeps every other member. This program never edits
    ''' exchange.json itself. The change reaches a running server only after a restart.</summary>
    Friend Function SetListen(listen As String, Optional dataDirForThisRun As String = "") As ListenOutcome
        If Not IsAvailable() Then Return ListenOutcome.Unavailable
        Dim r As RunResult = Run({"set-listen", If(listen, "")}, Nothing, CommandTimeoutMs, dataDirForThisRun)
        Log("set-listen", r)
        If Not r.Started OrElse r.TimedOut Then Return ListenOutcome.Failed
        Select Case r.ExitCode
            Case ExitOk : Return ListenOutcome.Saved
            Case ExitUsage : Return ListenOutcome.BadAddress
            Case ExitConfig : Return ListenOutcome.ConfigProblem
            Case Else : Return ListenOutcome.Failed
        End Select
    End Function

    ' --- the public address, the third-party range, the certificate ------------------------------
    ' Every one of these is written THROUGH the binary (it validates with the serve rules and keeps every
    ' other member of exchange.json); this program never edits the file itself. All of them reach a running
    ' server only at its next start, and the group says so.

    ''' <summary>The outcome of a configuration verb of the binary.</summary>
    Friend Enum ConfigOutcome
        Saved
        ''' <summary>Exit 2: the binary refused the value.</summary>
        Refused
        ''' <summary>Exit 4: the config cannot be read or written.</summary>
        ConfigProblem
        Failed
        Unavailable
    End Enum

    Private Function ConfigOutcomeOf(r As RunResult) As ConfigOutcome
        If Not r.Started OrElse r.TimedOut Then Return ConfigOutcome.Failed
        Select Case r.ExitCode
            Case ExitOk : Return ConfigOutcome.Saved
            Case ExitUsage : Return ConfigOutcome.Refused
            Case ExitConfig : Return ConfigOutcome.ConfigProblem
            Case Else : Return ConfigOutcome.Failed
        End Select
    End Function

    ''' <summary>Stores the public <c>host:port</c> every welcome announces (<c>set-endpoint</c>); an empty
    ''' value clears it. The binary refuses a loopback or wildcard address.</summary>
    Friend Function SetKeepAwake(value As Boolean) As ConfigOutcome
        If Not IsAvailable() Then Return ConfigOutcome.Unavailable
        Dim r As RunResult = Run({"set-keep-awake", If(value, "on", "off")}, Nothing, CommandTimeoutMs)
        Log("set-keep-awake", r)
        Return ConfigOutcomeOf(r)
    End Function

    Friend Function SetLogRetention(days As Integer) As ConfigOutcome
        If Not IsAvailable() Then Return ConfigOutcome.Unavailable
        Dim r As RunResult = Run({"set-log-retention", days.ToString(Globalization.CultureInfo.InvariantCulture)}, Nothing, CommandTimeoutMs)
        Log("set-log-retention", r)
        Return ConfigOutcomeOf(r)
    End Function

    Friend Function SetMaxAccounts(maxAccounts As Integer) As ConfigOutcome
        If Not IsAvailable() Then Return ConfigOutcome.Unavailable
        Dim r As RunResult = Run({"set-max-accounts", maxAccounts.ToString(Globalization.CultureInfo.InvariantCulture)}, Nothing, CommandTimeoutMs)
        Log("set-max-accounts", r)
        Return ConfigOutcomeOf(r)
    End Function

    Friend Function RenewCertificate() As CertificateResult
        If Not IsAvailable() Then Return New CertificateResult With {.Outcome = CertificateOutcome.Unavailable}
        Dim r As RunResult = Run({"tls", "renew"}, Nothing, CommandTimeoutMs)
        Log("tls renew", r)
        Return CertificateResultOf(r)
    End Function

    Friend Function SetEndpoint(value As String, Optional dataDirForThisRun As String = "") As ConfigOutcome
        If Not IsAvailable() Then Return ConfigOutcome.Unavailable
        Dim text As String = If(value, "").Trim()
        Dim r As RunResult = Run({"set-endpoint", If(text.Length = 0, "-", text)}, Nothing, CommandTimeoutMs, dataDirForThisRun)
        Log("set-endpoint", r)
        Return ConfigOutcomeOf(r)
    End Function

    ' --- the connection string (ticket 187 T-2) ---------------------------------------------

    Friend Enum ConnectionStringOutcome
        ''' <summary>The string was made; <see cref="ConnectionStringResult.Text"/> holds it.</summary>
        Made
        ''' <summary>Exit 2: no address for clients is set, or the name or login cannot be carried in the string.</summary>
        Refused
        ''' <summary>Exit 4: the configuration or the certificate cannot be read (the server never started).</summary>
        ConfigProblem
        Failed
        Unavailable
    End Enum

    Friend NotInheritable Class ConnectionStringResult
        Public Property Outcome As ConnectionStringOutcome = ConnectionStringOutcome.Failed
        ''' <summary>The one line the binary printed. Not a secret: it carries no password, token or code.</summary>
        Public Property Text As String = ""
    End Class

    ''' <summary>The connection string clients use (<c>connection-string</c>): built by the binary from the
    ''' configured public endpoint and the real certificate - this program never assembles it, so what a button
    ''' shows is what the command prints, byte for byte. A blank name or login is left out.</summary>
    Friend Function ConnectionString(name As String, login As String, Optional invitation As String = "") As ConnectionStringResult
        Dim result As New ConnectionStringResult
        If Not IsAvailable() Then
            result.Outcome = ConnectionStringOutcome.Unavailable
            Return result
        End If
        Dim args As New List(Of String) From {"connection-string"}
        If Not String.IsNullOrWhiteSpace(name) Then
            args.Add("--name")
            args.Add(name.Trim())
        End If
        If Not String.IsNullOrWhiteSpace(login) Then
            args.Add("--login")
            args.Add(login.Trim())
        End If
        If Not String.IsNullOrWhiteSpace(invitation) Then
            args.Add("--inv")
            args.Add(invitation.Trim())
        End If
        Dim r As RunResult = Run(args.ToArray(), Nothing, CommandTimeoutMs)
        ' the verb and the exit code only: the string, the name and the login are not written to the log (187 rule 11)
        Log("connection-string", r)
        If Not r.Started OrElse r.TimedOut Then Return result
        Select Case r.ExitCode
            Case ExitOk
                result.Text = r.StdOut.Trim()
                result.Outcome = If(result.Text.StartsWith("FMSEXCH1:", StringComparison.Ordinal), ConnectionStringOutcome.Made, ConnectionStringOutcome.Failed)
            Case ExitUsage : result.Outcome = ConnectionStringOutcome.Refused
            Case ExitConfig : result.Outcome = ConnectionStringOutcome.ConfigProblem
        End Select
        Return result
    End Function

    ''' <summary>Stores the third-party port range as "lo-hi" (<c>set-port-range</c>).</summary>
    Friend Function SetPortRange(range As String, Optional dataDirForThisRun As String = "") As ConfigOutcome
        If Not IsAvailable() Then Return ConfigOutcome.Unavailable
        Dim r As RunResult = Run({"set-port-range", If(range, "")}, Nothing, CommandTimeoutMs, dataDirForThisRun)
        Log("set-port-range", r)
        Return ConfigOutcomeOf(r)
    End Function

    Friend Enum CertificateOutcome
        ''' <summary>Done; <see cref="CertificateResult.Fingerprint"/> is the one the next start serves.</summary>
        Done
        ''' <summary>Import: the key is not the certificate's key.</summary>
        Mismatch
        Expired
        NotYetValid
        ''' <summary>Import: a file is missing or is not PEM.</summary>
        Unreadable
        Failed
        Unavailable
    End Enum

    Friend NotInheritable Class CertificateResult
        Public Property Outcome As CertificateOutcome
        ''' <summary>The <c>SHA256:</c> fingerprint the binary printed on success; never a key.</summary>
        Public Property Fingerprint As String = ""
    End Class

    ''' <summary>Replaces the identity with a NEW self-signed one (<c>tls replace</c>). Every device that pinned
    ''' the old fingerprint refuses the server until its user pins the new one - the caller has asked twice.</summary>
    Friend Function ReplaceCertificate() As CertificateResult
        If Not IsAvailable() Then Return New CertificateResult With {.Outcome = CertificateOutcome.Unavailable}
        Dim r As RunResult = Run({"tls", "replace"}, Nothing, CommandTimeoutMs)
        Log("tls replace", r)
        Return CertificateResultOf(r)
    End Function

    ''' <summary>Imports a PEM certificate and key to serve (<c>tls import</c>): the binary checks that they
    ''' are a pair, inside their validity and readable, copies them into its own folder with its access list
    ''' and names the copy in its config. The key path travels as an argument; the key itself is never read
    ''' here, shown, logged or copied to the clipboard.</summary>
    Friend Function ImportCertificate(certPath As String, keyPath As String) As CertificateResult
        If Not IsAvailable() Then Return New CertificateResult With {.Outcome = CertificateOutcome.Unavailable}
        Dim r As RunResult = Run({"tls", "import", "--cert", If(certPath, ""), "--key", If(keyPath, "")}, Nothing, CommandTimeoutMs)
        Log("tls import", r)
        Return CertificateResultOf(r)
    End Function

    ''' <summary>Maps one run of a <c>tls</c> verb: exit 0 and a <c>fingerprint SHA256:..</c> line, or exit 2
    ''' and the one stable stderr line <c>tls-refused: &lt;class&gt;</c>.</summary>
    Friend Function CertificateResultOf(r As RunResult) As CertificateResult
        Dim result As New CertificateResult With {.Outcome = CertificateOutcome.Failed}
        If Not r.Started OrElse r.TimedOut Then Return result
        If r.ExitCode = ExitOk Then
            For Each line As String In If(r.StdOut, "").Split({ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries)
                If line.StartsWith("fingerprint SHA256:", StringComparison.Ordinal) Then
                    result.Fingerprint = line.Substring("fingerprint ".Length).Trim()
                    result.Outcome = CertificateOutcome.Done
                End If
            Next
            Return result
        End If
        If r.ExitCode = ExitUsage Then
            Dim err As String = If(r.StdErr, "")
            If err.Contains("tls-refused: mismatch") Then
                result.Outcome = CertificateOutcome.Mismatch
            ElseIf err.Contains("tls-refused: expired") Then
                result.Outcome = CertificateOutcome.Expired
            ElseIf err.Contains("tls-refused: not-yet-valid") Then
                result.Outcome = CertificateOutcome.NotYetValid
            ElseIf err.Contains("tls-refused: unreadable") Then
                result.Outcome = CertificateOutcome.Unreadable
            End If
        End If
        Return result
    End Function

    ' --- remote administration (ticket 245, DEVICE-EXCHANGE 1.4) -------------------------

    ''' <summary>Converts a "SHA256:<base64>" fingerprint into 32 colon-separated hex bytes, e.g. "AA:BB:...".</summary>
    Friend Function FormatFingerprintHex(fingerprint As String) As String
        If String.IsNullOrWhiteSpace(fingerprint) Then Return ""
        Dim b64 As String = fingerprint.Trim()
        If b64.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase) Then
            b64 = b64.Substring("SHA256:".Length).Trim()
        End If
        Try
            Dim bytes As Byte() = Convert.FromBase64String(b64)
            If bytes.Length = 32 Then
                Return BitConverter.ToString(bytes).Replace("-"c, ":"c)
            End If
        Catch ex As FormatException
        End Try
        Return fingerprint
    End Function

    ''' <summary>Computes the browser admin URL https://<x>/#admin per Rule 13.</summary>
    Friend Function BrowserAdminUrl(report As InspectReport) As String
        If report Is Nothing Then Return ""
        If Not report.AdminOn OrElse String.IsNullOrWhiteSpace(report.WebDir) Then Return ""
        Dim hostPort As String = report.PublicEndpoint.Trim()
        If String.IsNullOrEmpty(hostPort) Then
            Dim port As Integer = report.Port
            If port = 0 Then Return ""
            Dim separator As Integer = report.Listen.LastIndexOf(":"c)
            Dim listenHost As String = report.Listen.Substring(0, separator).Trim("["c, "]"c)
            If listenHost = "" OrElse listenHost = "0.0.0.0" Then
                listenHost = "127.0.0.1"
            ElseIf listenHost = "::" Then
                listenHost = "::1"
            End If
            If listenHost.Contains(":"c) Then listenHost = "[" & listenHost & "]"
            hostPort = listenHost & ":" & port.ToString(Globalization.CultureInfo.InvariantCulture)
        End If
        Return "https://" & hostPort & "/#admin"
    End Function

    ''' <summary>The sign-in link of the browser client (WEB-BINDING page-served-by-the-server 3, item AE): https://&lt;x&gt;/#&lt;connection string&gt;.
    ''' Pure. The host and port come out of the connection string itself (so the link and the string name the same server; an IPv6 literal
    ''' is bracketed), the string rides after the # unchanged (only the white space around it is dropped) and a fragment never reaches the wire. "" when the server serves no web
    ''' client, or when there is no string, or when the string is not a connection string with an endpoint - never a half link.</summary>
    Friend Function BrowserSignInUrl(report As InspectReport, connectionString As String) As String
        If report Is Nothing OrElse String.IsNullOrWhiteSpace(report.WebDir) Then Return ""
        If String.IsNullOrEmpty(connectionString) Then Return ""
        Dim parsed As ExchangeConnectionStringResult = ExchangeConnectionString.Parse(connectionString)
        If Not parsed.Succeeded Then Return ""
        Dim endpoint As String = parsed.Value.Endpoint
        If parsed.Value.Host.Length = 0 OrElse parsed.Value.Port <= 0 Then Return ""
        Return "https://" & endpoint & "/#" & connectionString.Trim()
    End Function

    Friend NotInheritable Class AdminActionResult
        Public Property Outcome As ConfigOutcome = ConfigOutcome.Failed
        Public Property GeneratedPassword As String = ""
        Public Property ErrorMessage As String = ""
    End Class

    Friend Function AdminOn(generate As Boolean, password As String, allowedAddresses As String, sessionLifetimeMinutes As Integer) As AdminActionResult
        Dim res As New AdminActionResult
        If Not IsAvailable() Then
            res.Outcome = ConfigOutcome.Unavailable
            Return res
        End If
        Dim args As New List(Of String) From {"admin", "on"}
        If generate Then
            args.Add("--generate")
        End If
        If allowedAddresses IsNot Nothing Then
            args.Add("--allowed")
            args.Add(If(allowedAddresses.Trim().Length = 0, "-", allowedAddresses.Trim()))
        End If
        If sessionLifetimeMinutes > 0 Then
            args.Add("--lifetime")
            args.Add(sessionLifetimeMinutes.ToString(Globalization.CultureInfo.InvariantCulture))
        End If
        Dim stdin As String = Nothing
        If Not generate AndAlso Not String.IsNullOrEmpty(password) Then
            stdin = password
        End If
        Dim r As RunResult = Run(args.ToArray(), stdin, CommandTimeoutMs)
        Log("admin on", r)
        If Not r.Started OrElse r.TimedOut Then
            res.Outcome = ConfigOutcome.Failed
            Return res
        End If
        Select Case r.ExitCode
            Case ExitOk
                res.Outcome = ConfigOutcome.Saved
                If generate Then
                    Dim lines As String() = If(r.StdOut, "").Split({ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries)
                    If lines.Length > 0 Then res.GeneratedPassword = lines(0).Trim()
                End If
            Case ExitUsage : res.Outcome = ConfigOutcome.Refused
            Case ExitPolicy : res.Outcome = ConfigOutcome.Refused
            Case ExitConfig : res.Outcome = ConfigOutcome.ConfigProblem
            Case Else : res.Outcome = ConfigOutcome.Failed
        End Select
        res.ErrorMessage = If(r.StdErr, "").Trim()
        Return res
    End Function

    Friend Function AdminOff() As ConfigOutcome
        If Not IsAvailable() Then Return ConfigOutcome.Unavailable
        Dim r As RunResult = Run({"admin", "off"}, Nothing, CommandTimeoutMs)
        Log("admin off", r)
        Return ConfigOutcomeOf(r)
    End Function

    Friend Function AdminPasswd(generate As Boolean, password As String) As AdminActionResult
        Dim res As New AdminActionResult
        If Not IsAvailable() Then
            res.Outcome = ConfigOutcome.Unavailable
            Return res
        End If
        Dim args As New List(Of String) From {"admin", "passwd"}
        If generate Then
            args.Add("--generate")
        End If
        Dim stdin As String = Nothing
        If Not generate AndAlso Not String.IsNullOrEmpty(password) Then
            stdin = password
        End If
        Dim r As RunResult = Run(args.ToArray(), stdin, CommandTimeoutMs)
        Log("admin passwd", r)
        If Not r.Started OrElse r.TimedOut Then
            res.Outcome = ConfigOutcome.Failed
            Return res
        End If
        Select Case r.ExitCode
            Case ExitOk
                res.Outcome = ConfigOutcome.Saved
                If generate Then
                    Dim lines As String() = If(r.StdOut, "").Split({ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries)
                    If lines.Length > 0 Then res.GeneratedPassword = lines(0).Trim()
                End If
            Case ExitUsage : res.Outcome = ConfigOutcome.Refused
            Case ExitPolicy : res.Outcome = ConfigOutcome.Refused
            Case ExitConfig : res.Outcome = ConfigOutcome.ConfigProblem
            Case Else : res.Outcome = ConfigOutcome.Failed
        End Select
        res.ErrorMessage = If(r.StdErr, "").Trim()
        Return res
    End Function

    Friend Function AdminSet(allowedAddresses As String, sessionLifetimeMinutes As Integer) As ConfigOutcome
        If Not IsAvailable() Then Return ConfigOutcome.Unavailable
        Dim args As New List(Of String) From {"admin", "set"}
        If allowedAddresses IsNot Nothing Then
            args.Add("--allowed")
            args.Add(If(allowedAddresses.Trim().Length = 0, "-", allowedAddresses.Trim()))
        End If
        If sessionLifetimeMinutes > 0 Then
            args.Add("--lifetime")
            args.Add(sessionLifetimeMinutes.ToString(Globalization.CultureInfo.InvariantCulture))
        End If
        Dim r As RunResult = Run(args.ToArray(), Nothing, CommandTimeoutMs)
        Log("admin set", r)
        Return ConfigOutcomeOf(r)
    End Function

    ' --- where the data lives ----------------------------------------------------------------

    Private _dataDirCache As String = ""
    Private _dataDirAt As DateTime = DateTime.MinValue

    ''' <summary>Forgets what <see cref="DataDir"/> remembered (a test, or a service just registered).</summary>
    Friend Sub ForgetDataDir()
        _dataDirCache = ""
        _dataDirAt = DateTime.MinValue
    End Sub

    ''' <summary>The data directory the binary resolves, as its own <c>inspect</c> reports it, remembered for
    ''' 20 s (it only changes when a service is registered or removed). "" when it cannot be asked. The admin
    ''' pipe's name is derived from it (<see cref="ExchangeAdmin.PipeNameFor"/>). Blocks on the first call.</summary>
    Friend Function DataDir() As String
        If Not String.IsNullOrEmpty(DataDirOverride) Then Return DataDirOverride
        If _dataDirCache.Length > 0 AndAlso (DateTime.UtcNow - _dataDirAt).TotalSeconds < 20 Then Return _dataDirCache
        Dim report As InspectReport = Inspect()
        If report.Answered AndAlso report.DataDir.Length > 0 Then
            _dataDirCache = report.DataDir
            _dataDirAt = DateTime.UtcNow
        End If
        Return _dataDirCache
    End Function

    ' --- start / stop ---------------------------------------------------------------------

    Friend Enum StartOutcome
        Started
        AlreadyRunning
        ''' <summary>Exit 3: no account yet - the server refuses to serve nobody (162).</summary>
        NoAccount
        ''' <summary>Exit 4: the config cannot be read or is not valid.</summary>
        ConfigProblem
        ''' <summary>Exit 5: the service runs - the service wins (133 section 6.1).</summary>
        BesideService
        ''' <summary>Any other early exit - typically the listen port is taken.</summary>
        Failed
        Unavailable
    End Enum

    Private ReadOnly Gate As New Object()

    ''' <summary>The server THIS Share Manager session started and has not seen exit. Servers it merely
    ''' found already running are never stopped on exit.</summary>
    Private _started As Process

    ''' <summary>Raised on a thread-pool thread when the server this session started exits by itself or is
    ''' stopped; the group refreshes on it. Never raised for a server it did not start.</summary>
    Friend Event ConsoleExited As EventHandler

    ''' <summary>Starts <c>serve</c> hidden. Waits <see cref="StartupGraceMs"/> for an immediate refusal and maps
    ''' the exit code; a server still running after that is "started". Never asks for elevation.</summary>
    Friend Function Start() As StartOutcome
        If Not IsAvailable() Then Return StartOutcome.Unavailable
        If FindRunning().Length > 0 Then Return StartOutcome.AlreadyRunning

        Dim psi As New ProcessStartInfo(ExePath()) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .WindowStyle = ProcessWindowStyle.Hidden,
            .WorkingDirectory = ExeDirectory()}
        psi.ArgumentList.Add("serve")
        ApplyEnvironment(psi)

        Dim p As Process
        Try
            p = Process.Start(psi)
        Catch ex As Exception
            AppFileLogger.LogException("Exchange server: start", ex)
            Return StartOutcome.Failed
        End Try
        If p Is Nothing Then Return StartOutcome.Failed

        If p.WaitForExit(StartupGraceMs) Then
            Dim code As Integer = p.ExitCode
            p.Dispose()
            AppFileLogger.WriteLine("Exchange server: serve exited at once with code " & code.ToString(Globalization.CultureInfo.InvariantCulture))
            Select Case code
                Case ExitPolicy : Return StartOutcome.NoAccount
                Case ExitConfig : Return StartOutcome.ConfigProblem
                Case ExitBesideService : Return StartOutcome.BesideService
                Case Else : Return StartOutcome.Failed
            End Select
        End If

        SyncLock Gate
            _started = p
        End SyncLock
        p.EnableRaisingEvents = True
        AddHandler p.Exited, AddressOf OnStartedProcessExited
        AppFileLogger.WriteLine("Exchange server: started, pid " & p.Id.ToString(Globalization.CultureInfo.InvariantCulture))
        Return StartOutcome.Started
    End Function

    Private Sub OnStartedProcessExited(sender As Object, e As EventArgs)
        SyncLock Gate
            If _started IsNot Nothing AndAlso Object.ReferenceEquals(_started, sender) Then
                Try : _started.Dispose() : Catch : End Try
                _started = Nothing
            End If
        End SyncLock
        RaiseEvent ConsoleExited(Nothing, EventArgs.Empty)
    End Sub

    Friend Enum StopOutcome
        NotRunning
        ''' <summary>The stop request was honoured within the wait.</summary>
        Stopped
        ''' <summary>No exit within 5 s (or the binary does not know the stop command): killed.</summary>
        Killed
        Failed
    End Enum

    ''' <summary>The running console server (this session's or one found by exe path), or an empty array.
    ''' A process whose path cannot be read (another account's, the service's) is not ours.</summary>
    Friend Function FindRunning() As Process()
        Dim mine As String = ExePath()
        If mine.Length = 0 Then Return Array.Empty(Of Process)()
        Dim wanted As String
        Try
            wanted = Path.GetFullPath(mine)
        Catch
            Return Array.Empty(Of Process)()
        End Try
        Dim found As New List(Of Process)()
        Try
            For Each p As Process In Process.GetProcessesByName(ProcessName)
                Dim keep As Boolean = False
                Try
                    Dim modulePath As String = p.MainModule?.FileName
                    keep = Not String.IsNullOrEmpty(modulePath) AndAlso String.Equals(Path.GetFullPath(modulePath), wanted, StringComparison.OrdinalIgnoreCase)
                Catch ex As Win32Exception
                Catch ex As InvalidOperationException
                End Try
                If keep Then found.Add(p) Else p.Dispose()
            Next
        Catch ex As Exception
            AppFileLogger.LogException("Exchange server: looking for a running instance", ex)
        End Try
        Return found.ToArray()
    End Function

    ''' <summary>The pid of the console server, or 0 when none runs. Cheap enough for the tray menu.</summary>
    Friend Function RunningPid() As Integer
        Dim all As Process() = FindRunning()
        Try
            Return If(all.Length > 0, all(0).Id, 0)
        Finally
            For Each p As Process In all
                p.Dispose()
            Next
        End Try
    End Function

    ''' <summary>Asks the running console server to stop by its own <c>stop</c> command (a named event, so a
    ''' stop always closes registrations cleanly) and waits <see cref="StopWaitMs"/>; kills the process
    ''' only when it did not go, or when the binary has no such command.</summary>
    Friend Function StopServer() As StopOutcome
        Dim targets As Process() = FindRunning()
        If targets.Length = 0 Then Return StopOutcome.NotRunning
        Try
            Dim r As RunResult = Run({"stop"}, Nothing, CommandTimeoutMs)
            Log("stop", r)
            ' Exit 2 = a binary that does not know the command: nothing was signalled, so waiting would
            ' only delay the kill. Exit 1 = "nothing running" (it exited in the meantime, or it is not a
            ' console instance of this config): wait, the check below tells.
            Dim signalled As Boolean = r.Started AndAlso Not r.TimedOut AndAlso r.ExitCode <> ExitUsage
            Dim gone As Boolean = False
            If signalled Then gone = WaitAllExited(targets, StopWaitMs)
            If gone Then Return StopOutcome.Stopped
            For Each p As Process In targets
                Try
                    If Not p.HasExited Then p.Kill(entireProcessTree:=True)
                Catch ex As Exception
                    AppFileLogger.LogException("Exchange server: killing after the stop request did not end it", ex)
                End Try
            Next
            If WaitAllExited(targets, 3000) Then
                AppFileLogger.WriteLine("Exchange server: killed (no exit within " & (StopWaitMs \ 1000).ToString(Globalization.CultureInfo.InvariantCulture) & " s of the stop request)")
                Return StopOutcome.Killed
            End If
            Return StopOutcome.Failed
        Finally
            For Each p As Process In targets
                p.Dispose()
            Next
        End Try
    End Function

    Private Function WaitAllExited(targets As Process(), timeoutMs As Integer) As Boolean
        Dim deadline As DateTime = DateTime.UtcNow.AddMilliseconds(timeoutMs)
        For Each p As Process In targets
            Dim left As Integer = CInt(Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds))
            Try
                If Not p.HasExited AndAlso Not p.WaitForExit(left) Then Return False
            Catch
                ' A process that cannot be waited on is gone as far as this call is concerned.
            End Try
        Next
        Return True
    End Function

    ''' <summary>Called when the Share Manager exits: stops the server THIS session started, unless the user
    ''' asked to keep it running. A server found already running is never touched.</summary>
    Friend Function StopStartedOnExit(keepRunning As Boolean) As StopOutcome
        Dim mine As Process
        SyncLock Gate
            mine = _started
        End SyncLock
        If mine Is Nothing Then Return StopOutcome.NotRunning
        Try
            If mine.HasExited Then Return StopOutcome.NotRunning
        Catch
            Return StopOutcome.NotRunning
        End Try
        If keepRunning Then Return StopOutcome.NotRunning
        Return StopServer()
    End Function

    ' --- plumbing -------------------------------------------------------------------------


    Private Sub ApplyEnvironment(psi As ProcessStartInfo, Optional dataDirForThisRun As String = "")
        If Not String.IsNullOrEmpty(dataDirForThisRun) Then
            psi.Environment(DataDirEnvironmentName) = dataDirForThisRun
        ElseIf Not String.IsNullOrEmpty(DataDirOverride) Then
            psi.Environment(DataDirEnvironmentName) = DataDirOverride
        End If
    End Sub

    ''' <summary>One run of the binary with bounded waits. Reads stdout and stderr concurrently (a full pipe
    ''' would otherwise block the child), writes stdin as UTF-8 WITHOUT a byte-order mark - the Go side
    ''' trims only newlines, so a BOM would become the first character of the password.</summary>
    Friend Function Run(args As String(), stdin As String, timeoutMs As Integer, Optional dataDirForThisRun As String = "") As RunResult
        If RunOverride IsNot Nothing Then Return RunOverride(args, stdin)

        Dim psi As New ProcessStartInfo(ExePath()) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .RedirectStandardInput = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8,
            .StandardInputEncoding = New UTF8Encoding(False),
            .WorkingDirectory = ExeDirectory()}
        For Each a As String In args
            psi.ArgumentList.Add(a)
        Next
        ApplyEnvironment(psi, dataDirForThisRun)

        Dim result As New RunResult
        Dim p As Process
        Try
            p = Process.Start(psi)
        Catch ex As Exception
            AppFileLogger.LogException("Exchange server: running " & args(0), ex)
            Return result
        End Try
        If p Is Nothing Then Return result
        result.Started = True
        Using p
            Dim outTask As Task(Of String) = p.StandardOutput.ReadToEndAsync()
            Dim errTask As Task(Of String) = p.StandardError.ReadToEndAsync()
            Try
                If stdin IsNot Nothing Then p.StandardInput.Write(stdin & vbLf)
                p.StandardInput.Close()
            Catch ex As IOException
                ' The child exited before reading (a refusal it decided from the arguments alone).
            End Try
            If Not p.WaitForExit(timeoutMs) Then
                result.TimedOut = True
                Try : p.Kill(entireProcessTree:=True) : Catch : End Try
                Return result
            End If
            p.WaitForExit()
            result.ExitCode = p.ExitCode
            result.StdOut = outTask.GetAwaiter().GetResult()
            result.StdErr = errTask.GetAwaiter().GetResult()
        End Using
        Return result
    End Function

    ''' <summary>One line for companion.log: the verb and the exit code. The command line, stdin and both
    ''' output streams are NEVER logged - stdin of <c>account add</c> is a password and stdout of an account
    ''' listing is a set of logins.</summary>
    Private Sub Log(verb As String, r As RunResult)
        AppFileLogger.WriteLine("Exchange server: " & verb & " - " &
            If(Not r.Started, "could not run", If(r.TimedOut, "timed out", "exit " & r.ExitCode.ToString(Globalization.CultureInfo.InvariantCulture))))
    End Sub

End Module
