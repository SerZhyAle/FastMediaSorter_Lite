Option Strict On

Imports System.Diagnostics
Imports System.IO
Imports System.Text

''' <summary>
''' The exchange server's one firewall rule (ticket 143): a single inbound allow scoped to the PROGRAM -
''' never a port rule, never a profile-wide change, never added at install and never by the server
''' itself. Reachability from other devices exists only after a person asks for it with one button and one
''' UAC prompt; the same screen takes it away.
'''
''' Two halves, like <see cref="ServiceControl"/>:
'''   * READ - <see cref="QueryRule"/> asks <c>netsh advfirewall firewall show rule</c> (no elevation) and
'''     parses what it prints for the program path, so the UI shows the REAL state, not a remembered flag;
'''   * WRITE - <see cref="EnableViaElevation"/> and <see cref="WithdrawViaElevation"/> run the bundled
'''     helper <c>exchange\enable-exchange-server.ps1</c> through <see cref="ExchangeElevation"/>; the helper
'''     owns the rule's name, its scope and its deletion by program.
''' </summary>
Friend Module ExchangeFirewall

    ''' <summary>The rule name; the helper, the installer's uninstall step and this reader share it.</summary>
    Friend Const RuleName As String = "FastMediaSorter Media Exchange Server"

    Friend Const HelperScriptName As String = "enable-exchange-server.ps1"

    ''' <summary>What a read of the rule found.</summary>
    Friend Enum RuleState
        ''' <summary>No rule of that name exists.</summary>
        None
        ''' <summary>A rule exists and names THIS program's exe.</summary>
        ThisProgram
        ''' <summary>A rule of that name exists for another copy (typically the service's staged exe).</summary>
        OtherProgram
        ''' <summary>netsh could not be asked.</summary>
        Unknown
    End Enum

    ''' <summary>Tests replace the netsh read with a function returning (exit code, output). Nothing else
    ''' sets it.</summary>
    Friend ShowRuleOverride As Func(Of Tuple(Of Integer, String))

    Friend Function HelperScriptPath() As String
        Dim dir As String = ExchangeProcess.ExeDirectory()
        Return If(dir.Length = 0, "", Path.Combine(dir, HelperScriptName))
    End Function

    ''' <summary>The helper and the exe are both on disk: there is something to enable.</summary>
    Friend Function CanChange() As Boolean
        Dim script As String = HelperScriptPath()
        Return script.Length > 0 AndAlso File.Exists(script) AndAlso ExchangeProcess.IsAvailable()
    End Function

    ''' <summary>Pure: what netsh's answer means. <c>netsh ... show rule name=X verbose</c> exits non-zero when
    ''' no rule matches; with a match it prints every rule of that name, each with its "Program:" line (the
    ''' LABEL is localized, the path is not, so the path is what is searched for).</summary>
    Friend Function ParseRule(exitCode As Integer, output As String, exePath As String) As RuleState
        If exitCode <> 0 Then Return RuleState.None
        Dim text As String = If(output, "")
        If text.Trim().Length = 0 Then Return RuleState.None
        If Not String.IsNullOrEmpty(exePath) AndAlso text.IndexOf(exePath, StringComparison.OrdinalIgnoreCase) >= 0 Then Return RuleState.ThisProgram
        Return RuleState.OtherProgram
    End Function

    ''' <summary>Reads the rule's real state. Read-only, unelevated, bounded; any failure is Unknown, never
    ''' "no rule".</summary>
    Friend Function QueryRule() As RuleState
        Try
            Dim answer As Tuple(Of Integer, String) = If(ShowRuleOverride IsNot Nothing, ShowRuleOverride(), RunNetshShow())
            If answer Is Nothing Then Return RuleState.Unknown
            Return ParseRule(answer.Item1, answer.Item2, ExchangeProcess.ExePath())
        Catch ex As Exception
            AppFileLogger.LogException("Exchange firewall: reading the rule", ex)
            Return RuleState.Unknown
        End Try
    End Function

    Private Function RunNetshShow() As Tuple(Of Integer, String)
        Dim psi As New ProcessStartInfo("netsh", "advfirewall firewall show rule name=""" & RuleName & """ verbose") With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True}
        Try
            ' netsh writes in the console's OEM code page; a path with a non-ASCII account name only
            ' matches when it is decoded the same way.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
            psi.StandardOutputEncoding = Encoding.GetEncoding(Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
        Catch
        End Try
        Using p As Process = Process.Start(psi)
            If p Is Nothing Then Return Nothing
            Dim output As Threading.Tasks.Task(Of String) = p.StandardOutput.ReadToEndAsync()
            Dim errors As Threading.Tasks.Task(Of String) = p.StandardError.ReadToEndAsync()
            If Not p.WaitForExit(8000) Then
                Try : p.Kill() : Catch : End Try
                Return Nothing
            End If
            p.WaitForExit()
            Return Tuple.Create(p.ExitCode, output.GetAwaiter().GetResult())
        End Using
    End Function

    ''' <summary>Adds the program-scoped inbound rule through the helper: one UAC prompt.</summary>
    Friend Function EnableViaElevation() As ExchangeElevation.ElevatedOutcome
        Return Change(disable:=False)
    End Function

    ''' <summary>Removes the rule (by program, in the helper) through one UAC prompt.</summary>
    Friend Function WithdrawViaElevation() As ExchangeElevation.ElevatedOutcome
        Return Change(disable:=True)
    End Function

    Private Function Change(disable As Boolean) As ExchangeElevation.ElevatedOutcome
        If Not CanChange() Then Return ExchangeElevation.ElevatedOutcome.Unavailable
        Dim code As Integer
        Dim label As String = If(disable, "Exchange firewall: withdraw", "Exchange firewall: enable")
        If disable Then
            Return ExchangeElevation.RunHelper(label, HelperScriptPath(), code, "-ExePath", ExchangeProcess.ExePath(), "-Disable")
        End If
        Return ExchangeElevation.RunHelper(label, HelperScriptPath(), code, "-ExePath", ExchangeProcess.ExePath())
    End Function


End Module
