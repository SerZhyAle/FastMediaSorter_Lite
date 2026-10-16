Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Spec 230 section 4, the part around the exchange: where the accounts come from and that a read of them creates nothing, the
''' coordinator that every door goes through, the command-line switches and the two wake commands, the desktop shortcuts, the words,
''' the Win32 helpers that are pure, and the places where "nothing is drawn / nothing is created silently" is a property of the source.
''' </summary>
Public Class ClipboardSurfaceTests

    ' ------------------------------------------------------------------ the accounts

    Private NotInheritable Class MemoryPort
        Implements IExchangeSettingsPort

        Public ReadOnly Values As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Public Writes As Integer

        Public Function GetValue(name As String) As String Implements IExchangeSettingsPort.GetValue
            Dim v As String = Nothing
            Return If(Values.TryGetValue(name, v), v, "")
        End Function

        Public Sub SetValue(name As String, value As String) Implements IExchangeSettingsPort.SetValue
            Writes += 1
            Values(name) = value
        End Sub

        Public Sub DeleteValue(name As String) Implements IExchangeSettingsPort.DeleteValue
            Writes += 1
            Values.Remove(name)
        End Sub
    End Class

    Private NotInheritable Class XorCipher
        Implements IExchangeSecretCipher

        Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
            Return plain.Select(Function(b) CByte(b Xor &H5A)).ToArray()
        End Function

        Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
            Return blob.Select(Function(b) CByte(b Xor &H5A)).ToArray()
        End Function
    End Class

    Private Shared Function TempDir() As String
        Return Path.Combine(Path.GetTempPath(), "fms-clip-accounts-" & Guid.NewGuid().ToString("N"))
    End Function

    <Fact>
    Public Sub With_no_account_nothing_is_configured_and_asking_creates_nothing()
        Dim port As New MemoryPort()
        Dim dir As String = TempDir()
        Dim accounts As New ExchangeAccountStore(port)
        Dim entries As New ExchangeEntryStore(port, Path.Combine(dir, "entries"), New XorCipher())
        Assert.False(ClipboardTargets.AnyConfigured(accounts, entries))
        Assert.False(ClipboardTargets.AnyConfigured(Nothing, Nothing))
        Assert.Equal(0, port.Writes)
        Assert.Empty(port.Values)
        Assert.False(Directory.Exists(dir))
        Assert.Empty(ClipboardTargets.Load(accounts, New ExchangeCredentialStore(Path.Combine(dir, "token"), New XorCipher()), entries, ExchangeTlsTransport.Instance))
    End Sub

    <Fact>
    Public Sub An_account_comes_first_then_the_other_entries_and_one_account_is_never_counted_twice()
        Dim port As New MemoryPort()
        Dim dir As String = TempDir()
        Try
            Dim cipher As New XorCipher()
            Dim pin As String = ExchangeTrust.FingerprintOf(New Byte() {1, 2, 3})
            Dim accounts As New ExchangeAccountStore(port)
            Dim credentials As New ExchangeCredentialStore(Path.Combine(dir, "token"), cipher)
            Dim entries As New ExchangeEntryStore(port, Path.Combine(dir, "entries"), cipher)

            Dim accountDevice As String = ExchangeWireTestKit.IdOf("D"c)
            accounts.Save(New ExchangeAccount("10.0.0.5:22022", "alice", pin, ExchangeCertificateKind.Pinned, accountDevice, "My PC"))
            Assert.True(credentials.Save(accountDevice, ExchangeWireTestKit.TokenOf("T"c)))

            ' a second server of the person's, with its own token
            Dim other As New ExchangeEntry(ExchangeEntryStore.NewEntryId(), "10.0.0.9:22022", "bob", pin, ExchangeCertificateKind.Pinned,
                                           ExchangeWireTestKit.IdOf("E"c), "My PC", "Office", False)
            Assert.True(entries.Upsert(other))
            Assert.True(entries.TokenStoreFor(other.Id).Save(other.DeviceId, ExchangeWireTestKit.TokenOf("U"c)))

            ' the same account as the Share Manager's, held by the viewer as well: one target, not two
            Dim same As New ExchangeEntry(ExchangeEntryStore.NewEntryId(), "10.0.0.5:22022", "ALICE", pin, ExchangeCertificateKind.Pinned,
                                          ExchangeWireTestKit.IdOf("F"c), "My PC", "Alias", False)
            Assert.True(entries.Upsert(same))
            Assert.True(entries.TokenStoreFor(same.Id).Save(same.DeviceId, ExchangeWireTestKit.TokenOf("V"c)))

            ' an entry whose token is missing is not a server a value can be sent to
            Dim noToken As New ExchangeEntry(ExchangeEntryStore.NewEntryId(), "10.0.0.7:22022", "carol", pin, ExchangeCertificateKind.Pinned,
                                             ExchangeWireTestKit.IdOf("G"c), "My PC", "Broken", False)
            Assert.True(entries.Upsert(noToken))

            Assert.True(ClipboardTargets.AnyConfigured(accounts, entries))
            Dim targets As List(Of ClipboardTarget) = ClipboardTargets.Load(accounts, credentials, entries, ExchangeTlsTransport.Instance)
            Assert.Equal(2, targets.Count)
            Assert.Equal("10.0.0.5:22022", targets(0).Label)
            Assert.Equal("Office", targets(1).Label)
            Assert.Equal(accountDevice, targets(0).Credentials.DeviceId)
            Assert.Equal(ExchangeWireTestKit.IdOf("E"c), targets(1).Credentials.DeviceId)
        Finally
            Try
                If Directory.Exists(dir) Then Directory.Delete(dir, True)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            End Try
        End Try
    End Sub

    <Fact>
    Public Sub An_account_with_no_readable_token_is_never_a_target()
        Dim port As New MemoryPort()
        Dim dir As String = TempDir()
        Try
            Dim accounts As New ExchangeAccountStore(port)
            accounts.Save(New ExchangeAccount("10.0.0.5:22022", "alice", ExchangeTrust.FingerprintOf(New Byte() {1}), ExchangeCertificateKind.Pinned,
                                              ExchangeWireTestKit.IdOf("D"c), "My PC"))
            Dim entries As New ExchangeEntryStore(port, Path.Combine(dir, "entries"), New XorCipher())
            Assert.True(ClipboardTargets.AnyConfigured(accounts, entries))
            Dim credentials As New ExchangeCredentialStore(Path.Combine(dir, "token"), New XorCipher())
            Assert.Empty(ClipboardTargets.Load(accounts, credentials, entries, ExchangeTlsTransport.Instance))
        Finally
            Try
                If Directory.Exists(dir) Then Directory.Delete(dir, True)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            End Try
        End Try
    End Sub

    ' ------------------------------------------------------------------ the coordinator

    Private Shared Function DummyTarget() As ClipboardTarget
        Return New ClipboardTarget("Home", "key|Home", New ExchangeServerAddress("127.0.0.1", ClipboardTestKit.ClosedPort()), "",
                                   New ExchangeDeviceCredentials(ExchangeWireTestKit.IdOf("D"c), ExchangeWireTestKit.TokenOf("T"c)),
                                   ExchangeTlsTransport.Instance)
    End Function

    ''' <summary>The test's own context is dropped for the call: an event then runs where it was raised and a test can count them.</summary>
    Private NotInheritable Class NoContext
        Implements IDisposable

        Private ReadOnly _saved As SynchronizationContext = SynchronizationContext.Current

        Public Sub New()
            SynchronizationContext.SetSynchronizationContext(Nothing)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            SynchronizationContext.SetSynchronizationContext(_saved)
        End Sub
    End Class

    <Fact>
    Public Async Function A_run_reports_its_state_and_one_result_paragraph() As Task
        Using New NoContext()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(DummyTarget())
                rig.Port.Snapshot = ClipboardSnapshot.OfKind(ClipboardReadKind.Empty)
                Dim coordinator As New ClipboardCoordinator(rig.Exchange(), Function() True)
                Dim states As Integer = 0
                Dim finished As New List(Of ClipboardFinishedEventArgs)()
                AddHandler coordinator.StateChanged, Sub(s, e) states += 1
                AddHandler coordinator.Finished, Sub(s, e) finished.Add(e)

                Dim result As ClipboardRunResult = Await coordinator.RunAsync(isSend:=True)

                Assert.False(result.Succeeded)
                Assert.True(states >= 2, "the state was announced at the start and at the end")
                Dim one As ClipboardFinishedEventArgs = Assert.Single(finished)
                Assert.True(one.IsSend)
                Assert.False(one.Succeeded)
                Assert.Equal(ClipboardMessages.TextOf(result), one.Text)
                Assert.False(coordinator.IsRunning)
                Assert.Equal("", coordinator.ProgressText)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_second_press_is_told_and_the_first_keeps_running() As Task
        Using New NoContext()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(DummyTarget())
                rig.Port.ReadGate = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                Dim coordinator As New ClipboardCoordinator(rig.Exchange(), Function() True)
                Dim finished As New List(Of ClipboardFinishedEventArgs)()
                AddHandler coordinator.Finished, Sub(s, e) finished.Add(e)

                Dim first As Task(Of ClipboardRunResult) = coordinator.RunAsync(isSend:=True)
                Assert.True(Await ClipboardTestKit.WaitUntilAsync(Function() rig.Port.Reads = 1))
                Assert.True(coordinator.IsRunning)
                Assert.True(coordinator.IsSending)
                Assert.False(String.IsNullOrEmpty(coordinator.ProgressText))

                Dim second As ClipboardRunResult = Await coordinator.RunAsync(isSend:=False)
                Assert.Equal(ClipboardLineCode.OperationUnderWay, Assert.Single(second.Lines).Code)
                Assert.True(coordinator.IsRunning, "the first one is still running")
                Assert.True(coordinator.IsSending, "and it is still the send")
                Assert.Equal(1, rig.Port.Reads)
                Assert.Single(finished)
                Assert.False(finished(0).Succeeded)

                rig.Port.ReadGate.SetResult(True)
                Await first
                Assert.False(coordinator.IsRunning)
                Assert.Equal(2, finished.Count)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Cancel_ends_the_running_operation_in_the_cancelled_sentence() As Task
        Using New NoContext()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(DummyTarget())
                rig.Port.ReadGate = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                Dim coordinator As New ClipboardCoordinator(rig.Exchange(), Function() True)

                Dim run As Task(Of ClipboardRunResult) = coordinator.RunAsync(isSend:=False)
                Assert.True(Await ClipboardTestKit.WaitUntilAsync(Function() coordinator.IsRunning))
                coordinator.Cancel()
                Dim finished As Task = Await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(15)))
                Assert.Same(run, finished)
                Dim result As ClipboardRunResult = Await run
                Assert.False(result.Succeeded)
                Assert.Equal(ClipboardLineCode.Cancelled, Assert.Single(result.Lines).Code)
                Assert.False(coordinator.IsRunning)
                ' Cancelling with nothing running is nothing.
                coordinator.Cancel()
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_defect_below_the_exchange_is_a_sentence_and_never_an_exception() As Task
        Using New NoContext()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(DummyTarget())
                rig.Port.ThrowOnRead = New InvalidOperationException("a defect that must not reach the person as its text")
                Dim coordinator As New ClipboardCoordinator(rig.Exchange(), Function() True)
                Dim texts As New List(Of String)()
                AddHandler coordinator.Finished, Sub(s, e) texts.Add(e.Text)

                Dim result As ClipboardRunResult = Await coordinator.RunAsync(isSend:=True)

                Assert.False(result.Succeeded)
                Assert.Equal(ClipboardLineCode.ClipboardUnreadable, Assert.Single(result.Lines).Code)
                Assert.DoesNotContain("defect", Assert.Single(texts))
                Assert.False(coordinator.IsRunning)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_run_that_is_not_offered_reads_nothing_even_with_an_account_on_file() As Task
        Using New NoContext()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(DummyTarget())
                rig.Port.Snapshot = ClipboardSnapshot.OfKind(ClipboardReadKind.Empty)
                Dim coordinator As New ClipboardCoordinator(rig.Exchange(), Function() False)
                Dim finished As New List(Of ClipboardFinishedEventArgs)()
                AddHandler coordinator.Finished, Sub(s, e) finished.Add(e)

                For Each isSend As Boolean In {True, False}
                    Dim result As ClipboardRunResult = Await coordinator.RunAsync(isSend)
                    Assert.False(result.Succeeded)
                    Assert.Equal(ClipboardLineCode.NoServer, Assert.Single(result.Lines).Code)
                Next
                Assert.Equal(0, rig.Port.Reads)
                Assert.Equal(2, finished.Count)
                Assert.False(coordinator.IsRunning)
            End Using
        End Using
    End Function

    <Fact>
    Public Sub The_functions_are_offered_only_with_an_account()
        Using rig As New ClipboardTestRig()
            Assert.False(New ClipboardCoordinator(rig.Exchange(), Function() False).IsOffered())
            Assert.True(New ClipboardCoordinator(rig.Exchange(), Function() True).IsOffered())
            Assert.False(New ClipboardCoordinator(rig.Exchange(), Nothing).IsOffered())
        End Using
    End Sub

    ' ------------------------------------------------------------------ the switches and the two wake commands

    <Theory>
    <InlineData("--clipboard-send", CInt(Program.ClipboardSwitch.Send))>
    <InlineData("--CLIPBOARD-SEND", CInt(Program.ClipboardSwitch.Send))>
    <InlineData("/clipboard-send", CInt(Program.ClipboardSwitch.Send))>
    <InlineData("::fms-clipboard-send::", CInt(Program.ClipboardSwitch.Send))>
    <InlineData("--clipboard-get", CInt(Program.ClipboardSwitch.Receive))>
    <InlineData("/Clipboard-Get", CInt(Program.ClipboardSwitch.Receive))>
    <InlineData("::fms-clipboard-get::", CInt(Program.ClipboardSwitch.Receive))>
    <InlineData("::FMS-CLIPBOARD-SEND::", CInt(Program.ClipboardSwitch.None))>
    <InlineData("--clipboard", CInt(Program.ClipboardSwitch.None))>
    <InlineData("--clipboard-send-now", CInt(Program.ClipboardSwitch.None))>
    <InlineData("D:\Photos", CInt(Program.ClipboardSwitch.None))>
    <InlineData("--tray", CInt(Program.ClipboardSwitch.None))>
    <InlineData("", CInt(Program.ClipboardSwitch.None))>
    Public Sub An_argument_is_a_clipboard_switch_in_its_three_spellings_and_nothing_else(arg As String, expectedRaw As Integer)
        Dim expected As Program.ClipboardSwitch = CType(expectedRaw, Program.ClipboardSwitch)
        Assert.Equal(expected, Program.ClipboardSwitchOf(arg))
    End Sub

    <Fact>
    Public Sub The_marker_form_is_exact_and_the_flag_forms_are_not_a_folder()
        Assert.Equal(Program.ClipboardSwitch.None, Program.ClipboardSwitchOf(Nothing))
        Assert.Equal("--clipboard-send", Program.ClipboardSendFlag)
        Assert.Equal("--clipboard-get", Program.ClipboardGetFlag)
        ' What a shortcut passes is never read as a folder to share.
        Assert.Equal(CompanionCommandKind.ClipboardSend, CompanionCommand.Parse(Program.ClipboardSendCommand).Kind)
        Assert.Equal(CompanionCommandKind.ClipboardGet, CompanionCommand.Parse(Program.ClipboardGetCommand).Kind)
        Assert.Equal("", CompanionCommand.Parse(Program.ClipboardSendCommand).TargetPath)
        Assert.Equal(CompanionCommandKind.Invalid, CompanionCommand.Parse("::FMS-CLIPBOARD-SEND::").Kind)
        Assert.Equal(CompanionCommandKind.Invalid, CompanionCommand.Parse(Program.ClipboardSendCommand & " x").Kind)
        Assert.Equal(CompanionCommandKind.Invalid, CompanionCommand.Parse(Program.ClipboardGetCommand & "D:\Photos").Kind)
        Assert.Equal(CompanionCommandKind.Invalid, CompanionCommand.Parse(Program.ClipboardSendFlag).Kind)
    End Sub

    <Fact>
    Public Sub Both_switches_at_once_send_and_neither_is_empty()
        Assert.Equal(Program.ClipboardSendCommand, Program.ClipboardPayloadOf(True, False))
        Assert.Equal(Program.ClipboardSendCommand, Program.ClipboardPayloadOf(True, True))
        Assert.Equal(Program.ClipboardGetCommand, Program.ClipboardPayloadOf(False, True))
        Assert.Equal("", Program.ClipboardPayloadOf(False, False))
    End Sub

    <Fact>
    Public Sub A_running_manager_gets_the_request_and_with_none_running_one_action_runs_and_exits()
        ' reaches a running instance ...
        Assert.Equal(Program.LaunchRoute.ForwardToRunning, Program.RouteOf(InstanceRole.Existing, clipboardRequested:=True))
        ' ... and works from a cold start: the action, never the tray manager.
        Assert.Equal(Program.LaunchRoute.ClipboardOneShot, Program.RouteOf(InstanceRole.Primary, clipboardRequested:=True))
        ' Everything that is not a clipboard switch keeps the route it had.
        Assert.Equal(Program.LaunchRoute.TrayManager, Program.RouteOf(InstanceRole.Primary, clipboardRequested:=False))
        Assert.Equal(Program.LaunchRoute.ForwardToRunning, Program.RouteOf(InstanceRole.Existing, clipboardRequested:=False))
    End Sub

    Private Shared Function Deliver(payload As String, ByRef command As CompanionCommand) As Integer
        Dim bytes As Byte() = Encoding.UTF8.GetBytes(payload)
        Dim mem As IntPtr = Marshal.AllocHGlobal(Math.Max(1, bytes.Length))
        Try
            Marshal.Copy(bytes, 0, mem, bytes.Length)
            Return ActivationReceiver.Evaluate(New IntPtr(CopyDataTransport.CompanionTag), bytes.Length, mem, Nothing, command)
        Finally
            Marshal.FreeHGlobal(mem)
        End Try
    End Function

    <Fact>
    Public Sub A_running_manager_acknowledges_the_clipboard_commands_and_stages_no_folder()
        Dim command As CompanionCommand = Nothing
        Assert.Equal(1, Deliver(Program.ClipboardSendCommand, command))
        Assert.Equal(CompanionCommandKind.ClipboardSend, command.Kind)
        Assert.Equal(1, Deliver(Program.ClipboardGetCommand, command))
        Assert.Equal(CompanionCommandKind.ClipboardGet, command.Kind)
    End Sub

    <Fact>
    Public Sub An_older_receiver_gets_the_plain_wake_never_the_unknown_payload()
        For Each payload As String In {Program.ClipboardSendCommand, Program.ClipboardGetCommand}
            Assert.Equal(Program.ShowWindowCommand, Program.CompatiblePayload(payload, Nothing))
            Assert.Equal(Program.ShowWindowCommand, Program.CompatiblePayload(payload, ShareActivationPolicy.MinScheduleVersion))
            Assert.Equal(payload, Program.CompatiblePayload(payload, ShareActivationPolicy.MinClipboardVersion))
            Assert.Equal(payload, Program.CompatiblePayload(payload, New Version(26, 11, 1, 0)))
        Next
        Assert.False(ShareActivationPolicy.IsClipboardCapable(Nothing))
        Assert.True(ShareActivationPolicy.IsClipboardCapable(ShareActivationPolicy.MinClipboardVersion))
        Assert.True(ShareActivationPolicy.MinClipboardVersion >= ShareActivationPolicy.MinScheduleVersion)
    End Sub

    ' ------------------------------------------------------------------ the desktop shortcuts

    <Fact>
    Public Sub The_plan_is_two_shortcuts_with_the_two_switches_in_the_name_of_the_person_s_language()
        Dim plan As List(Of ClipboardShortcutSpec) = ClipboardShortcuts.Plan("C:\Users\me\Desktop", "C:\Apps\FMS\FastMediaSorterCompanion.exe",
                                                                             "Отправить буфер обмена на сервер", "Получить буфер обмена с сервера", "Share Manager")
        Assert.Equal(2, plan.Count)
        Assert.Equal("C:\Users\me\Desktop\Отправить буфер обмена на сервер.lnk", plan(0).LinkPath)
        Assert.Equal("--clipboard-send", plan(0).Arguments)
        Assert.Equal("C:\Users\me\Desktop\Получить буфер обмена с сервера.lnk", plan(1).LinkPath)
        Assert.Equal("--clipboard-get", plan(1).Arguments)
        For Each spec As ClipboardShortcutSpec In plan
            Assert.Equal("C:\Apps\FMS\FastMediaSorterCompanion.exe", spec.TargetPath)
            Assert.Equal("C:\Apps\FMS", spec.WorkingDirectory)
            Assert.Equal("Share Manager", spec.Description)
        Next
    End Sub

    <Theory>
    <InlineData("a/b:c", "a b c")>
    <InlineData("  name. ", "name")>
    <InlineData("...", "Clipboard")>
    <InlineData("", "Clipboard")>
    <InlineData("Send: the clipboard?", "Send  the clipboard")>
    Public Sub A_file_name_windows_would_refuse_is_cleaned(name As String, expected As String)
        Assert.Equal(expected, ClipboardShortcuts.SafeFileName(name))
    End Sub

    <Fact>
    Public Sub A_shortcut_that_is_there_is_left_as_it_is_and_a_failure_counts_only_its_own()
        Dim specs As New List(Of ClipboardShortcutSpec) From {
            New ClipboardShortcutSpec("D:\a.lnk", "x.exe", "--clipboard-send", "D:\", ""),
            New ClipboardShortcutSpec("D:\b.lnk", "x.exe", "--clipboard-get", "D:\", ""),
            New ClipboardShortcutSpec("D:\c.lnk", "x.exe", "--clipboard-get", "D:\", ""),
            New ClipboardShortcutSpec("D:\d.lnk", "x.exe", "--clipboard-get", "D:\", "")}
        Dim written As New List(Of String)()
        Dim result As ClipboardShortcutsResult = ClipboardShortcuts.Create(specs,
            Function(path) path = "D:\a.lnk",
            Sub(spec)
                If spec.LinkPath = "D:\c.lnk" Then Throw New UnauthorizedAccessException("denied")
                If spec.LinkPath = "D:\d.lnk" Then Throw New COMException("shell refused")
                written.Add(spec.LinkPath)
            End Sub)
        Assert.Equal(1, result.Existing)
        Assert.Equal(1, result.Created)
        Assert.Equal(2, result.Failed)
        Assert.Equal(New String() {"D:\b.lnk"}, written.ToArray())
    End Sub

    <Fact>
    Public Sub The_shell_writer_makes_a_link_that_runs_this_program_with_the_switch()
        Dim dir As String = Path.Combine(Path.GetTempPath(), "fms-clip-links-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        Try
            Dim exe As String = Environment.ProcessPath
            Dim plan As List(Of ClipboardShortcutSpec) = ClipboardShortcuts.Plan(dir, exe, "Send it", "Get it", "Share Manager")
            Dim writer As MethodInfo = GetType(ClipboardShortcuts).GetMethod("WriteShellLink", BindingFlags.NonPublic Or BindingFlags.Static)
            Assert.NotNull(writer)
            Dim first As ClipboardShortcutsResult = ClipboardShortcuts.Create(plan, AddressOf File.Exists,
                Sub(spec) writer.Invoke(Nothing, New Object() {spec}))
            Assert.Equal(2, first.Created)
            Assert.Equal(0, first.Failed)
            Assert.True(File.Exists(plan(0).LinkPath))
            Assert.True(File.Exists(plan(1).LinkPath))

            Dim shellType As Type = Type.GetTypeFromProgID("WScript.Shell")
            Dim shell As Object = Activator.CreateInstance(shellType)
            For i As Integer = 0 To 1
                Dim link As Object = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, Nothing, shell, New Object() {plan(i).LinkPath})
                Dim linkType As Type = link.GetType()
                Assert.Equal(exe, CStr(linkType.InvokeMember("TargetPath", BindingFlags.GetProperty, Nothing, link, Nothing)), ignoreCase:=True)
                Assert.Equal(plan(i).Arguments, CStr(linkType.InvokeMember("Arguments", BindingFlags.GetProperty, Nothing, link, Nothing)))
                Assert.Equal("Share Manager", CStr(linkType.InvokeMember("Description", BindingFlags.GetProperty, Nothing, link, Nothing)))
                Marshal.ReleaseComObject(link)
            Next
            Marshal.ReleaseComObject(shell)

            ' A second press finds both there and writes nothing.
            Dim stamp As DateTime = File.GetLastWriteTimeUtc(plan(0).LinkPath)
            Dim second As ClipboardShortcutsResult = ClipboardShortcuts.Create(plan, AddressOf File.Exists,
                Sub(spec) writer.Invoke(Nothing, New Object() {spec}))
            Assert.Equal(2, second.Existing)
            Assert.Equal(0, second.Created)
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(plan(0).LinkPath))
        Finally
            Try
                Directory.Delete(dir, True)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            End Try
        End Try
    End Sub

    ' ------------------------------------------------------------------ the words

    <Fact>
    Public Sub Every_line_code_has_a_sentence_and_the_progress_is_in_percent()
        For Each code As ClipboardLineCode In [Enum].GetValues(GetType(ClipboardLineCode)).Cast(Of ClipboardLineCode)()
            Dim line As New ClipboardLine(code, "Home", "unreachable", "Phone", "C:\Received")
            Assert.False(String.IsNullOrWhiteSpace(ClipboardMessages.TextOf(line)), "no sentence for " & code.ToString())
        Next
        Assert.Contains("50", ClipboardMessages.ProgressText(True, 50L, 100L))
        Assert.Contains("0", ClipboardMessages.ProgressText(False, 0L, 0L))
        Assert.Contains("100", ClipboardMessages.ProgressText(True, 500L, 100L))
    End Sub

    <Fact>
    Public Sub A_failure_names_its_server_and_a_reason_of_the_common_table()
        Dim text As String = ClipboardMessages.TextOf(New ClipboardLine(ClipboardLineCode.Failure, "Office", "unreachable-server"))
        Assert.Contains("Office", text)
        Assert.NotEqual("unreachable-server", ClipboardMessages.ReasonText("unreachable-server"))
        ' A code the table does not know reads as its generic refusal, never as the code.
        Dim unknown As String = ClipboardMessages.ReasonText("a-code-nobody-wrote")
        Assert.False(String.IsNullOrWhiteSpace(unknown))
        Assert.DoesNotContain("a-code-nobody-wrote", unknown)
    End Sub

    ' ------------------------------------------------------------------ the pure Win32 helpers

    <Fact>
    Public Sub Line_ends_are_made_LF_on_the_wire_and_CRLF_in_the_clipboard_without_doubling()
        Assert.Equal("a" & vbLf & "b" & vbLf & "c" & vbLf & "d", ClipboardText.ToLf("a" & vbCrLf & "b" & vbCr & "c" & vbLf & "d"))
        Assert.Equal("a" & vbCrLf & "b" & vbCrLf & "c", ClipboardText.ToCrLf("a" & vbLf & "b" & vbCrLf & "c"))
        Assert.Equal("", ClipboardText.ToLf(Nothing))
        Assert.Equal("", ClipboardText.ToCrLf(""))
        Dim wire As Byte() = ClipboardText.ToWireBytes("é" & vbCrLf & "ю")
        Assert.Equal(New Byte() {&HC3, &HA9, &HA, &HD1, &H8E}, wire)
    End Sub

    <Fact>
    Public Sub A_file_drop_is_a_DROPFILES_header_and_one_wide_double_terminated_path()
        Dim drop As Byte() = ClipboardWin32.BuildDropFiles("C:\Received\ключ.txt")
        Assert.Equal(20UI, BitConverter.ToUInt32(drop, 0))
        Assert.Equal(1, BitConverter.ToInt32(drop, 16))
        Dim path As String = Encoding.Unicode.GetString(drop, 20, drop.Length - 20)
        Assert.Equal("C:\Received\ключ.txt" & vbNullChar & vbNullChar, path.TrimEnd(vbNullChar(0)) & vbNullChar & vbNullChar)
        Assert.Equal(CByte(0), drop(drop.Length - 1))
        Assert.Equal(CByte(0), drop(drop.Length - 2))
        Assert.Equal(CByte(0), drop(drop.Length - 3))
        Assert.Equal(CByte(0), drop(drop.Length - 4))
    End Sub

    <Fact>
    Public Sub A_png_becomes_a_packed_DIB_and_comes_back_with_the_same_pixels()
        Dim png As Byte() = ClipboardTestKit.MakePng(9, 7)
        Dim dib As Byte() = ClipboardImages.PngToDib(png)
        Assert.NotNull(dib)
        Assert.Equal(40, BitConverter.ToInt32(dib, 0))
        Dim back As Byte() = ClipboardImages.DibToPng(dib)
        Assert.NotNull(back)
        Assert.True(ClipboardImages.LooksLikePng(back))
        Using a As New Bitmap(New MemoryStream(png))
            Using b As New Bitmap(New MemoryStream(back))
                Assert.Equal(a.Width, b.Width)
                Assert.Equal(a.Height, b.Height)
                For y As Integer = 0 To a.Height - 1
                    For x As Integer = 0 To a.Width - 1
                        Assert.Equal(a.GetPixel(x, y).ToArgb(), b.GetPixel(x, y).ToArgb())
                    Next
                Next
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_24_bit_DIB_from_another_program_is_read_and_garbage_is_not()
        ' 2 x 2, bottom-up, BGR rows padded to 4 bytes: bottom row red, green; top row blue, white.
        Dim dib As New List(Of Byte)()
        dib.AddRange(BitConverter.GetBytes(40))
        dib.AddRange(BitConverter.GetBytes(2))
        dib.AddRange(BitConverter.GetBytes(2))
        dib.AddRange(BitConverter.GetBytes(CShort(1)))
        dib.AddRange(BitConverter.GetBytes(CShort(24)))
        dib.AddRange(BitConverter.GetBytes(0))
        dib.AddRange(BitConverter.GetBytes(16))
        dib.AddRange(New Byte(15) {})
        dib.AddRange({0, 0, 255, 0, 255, 0, 0, 0})            ' bottom row: red, green
        dib.AddRange({255, 0, 0, 255, 255, 255, 0, 0})        ' top row: blue, white
        Dim png As Byte() = ClipboardImages.DibToPng(dib.ToArray())
        Assert.NotNull(png)
        Using bmp As New Bitmap(New MemoryStream(png))
            Assert.Equal(2, bmp.Width)
            Assert.Equal(Color.FromArgb(255, 0, 0, 255).ToArgb(), bmp.GetPixel(0, 0).ToArgb())
            Assert.Equal(Color.FromArgb(255, 255, 255, 255).ToArgb(), bmp.GetPixel(1, 0).ToArgb())
            Assert.Equal(Color.FromArgb(255, 255, 0, 0).ToArgb(), bmp.GetPixel(0, 1).ToArgb())
            Assert.Equal(Color.FromArgb(255, 0, 255, 0).ToArgb(), bmp.GetPixel(1, 1).ToArgb())
        End Using
        Assert.Null(ClipboardImages.DibToPng(Nothing))
        Assert.Null(ClipboardImages.DibToPng(New Byte(20) {}))
        Assert.Null(ClipboardImages.DibToPng(New Byte(200) {}))
        Assert.Null(ClipboardImages.PngToDib(Nothing))
        Assert.Null(ClipboardImages.PngToDib(Encoding.ASCII.GetBytes("this is not an image")))
        Assert.False(ClipboardImages.LooksLikePng(Nothing))
        Assert.False(ClipboardImages.LooksLikePng(New Byte(3) {}))
    End Sub

    <Fact>
    Public Sub A_progress_stream_counts_and_changes_no_byte_and_leaves_what_it_does_not_own()
        Dim data As Byte() = ClipboardTestKit.Pattern(10000)
        Dim inner As New MemoryStream(data)
        Dim steps As New List(Of Long)()
        Using counted As New ProgressStream(inner, data.LongLength, Sub(done, total) steps.Add(done))
            Dim buffer(2999) As Byte
            Dim got As New MemoryStream()
            Dim n As Integer = counted.Read(buffer, 0, buffer.Length)
            While n > 0
                got.Write(buffer, 0, n)
                n = counted.Read(buffer, 0, buffer.Length)
            End While
            Assert.Equal(data, got.ToArray())
            Assert.Equal(data.LongLength, counted.Length)
            Assert.False(counted.CanSeek)
            Assert.Throws(Of NotSupportedException)(Sub() counted.Seek(0, SeekOrigin.Begin))
        End Using
        Assert.Equal(10000L, steps(steps.Count - 1))
        Assert.True(inner.CanRead, "a stream the counter does not own stays open")
    End Sub

    ' ------------------------------------------------------------------ the setting

    <Fact>
    Public Sub The_received_folder_is_downloads_until_the_person_chooses_one()
        Dim before As String = ClipboardSettings.StoredFolder()
        Try
            ClipboardSettings.SaveReceivedFolder("")
            Assert.Equal("", ClipboardSettings.StoredFolder())
            Dim downloads As String = ClipboardSettings.ReceivedFolder()
            Assert.True(Path.IsPathRooted(downloads))
            Assert.Equal(ClipboardSettings.DefaultFolder(), downloads)

            Dim chosen As String = Path.Combine(Path.GetTempPath(), "fms-clip-chosen")
            ClipboardSettings.SaveReceivedFolder(chosen)
            Assert.Equal(chosen, ClipboardSettings.ReceivedFolder())

            ' A value that is not a rooted path never becomes a place files are written to.
            ClipboardSettings.SaveReceivedFolder("relative\folder")
            Assert.Equal(ClipboardSettings.DefaultFolder(), ClipboardSettings.ReceivedFolder())
        Finally
            ClipboardSettings.SaveReceivedFolder(before)
        End Try
    End Sub

    ' ------------------------------------------------------------------ what is a property of the source

    Private Shared Function RepositoryRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If Directory.Exists(Path.Combine(dir.FullName, "src", "FastMediaSorterCompanion")) AndAlso
               File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Return Nothing
    End Function

    Private Shared Function CompanionSource(ParamArray parts As String()) As String
        Dim all As New List(Of String) From {RepositoryRoot(), "src", "FastMediaSorterCompanion"}
        all.AddRange(parts)
        Return File.ReadAllText(Path.Combine(all.ToArray()))
    End Function

    <Fact>
    Public Sub Nothing_is_drawn_without_an_account_the_buttons_start_hidden_and_the_tray_decides_when_the_menu_opens()
        Dim window As String = CompanionSource("Forms", "MainWindow.Clipboard.vb")
        Assert.Contains(".Visible = False", window)
        Assert.Contains("_clipboard.IsOffered()", window)
        Dim access As String = CompanionSource("Forms", "MainWindow.Access.vb")
        Assert.Contains("RefreshClipboardButtons()", access)
        Dim tray As String = CompanionSource("TrayContext.vb")
        Assert.Contains("IsOffered()", tray)
        Assert.Contains("Opening", tray)
        Dim settings As String = CompanionSource("Forms", "Share_Settings_Form.Clipboard.vb")
        Assert.Contains("ClipboardCoordinator.AnyAccount()", settings)
    End Sub

    <Fact>
    Public Sub Shortcuts_are_created_only_by_the_press_never_silently()
        Dim root As String = RepositoryRoot()
        Dim callers As New List(Of String)()
        For Each file As String In Directory.EnumerateFiles(Path.Combine(root, "src"), "*.vb", SearchOption.AllDirectories)
            If file.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar) OrElse
               file.Contains(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar) Then Continue For
            For Each match As Match In Regex.Matches(System.IO.File.ReadAllText(file), "ClipboardShortcuts\.CreateOnDesktop\(")
                callers.Add(Path.GetFileName(file))
            Next
        Next
        Assert.Equal(New String() {"Share_Settings_Form.Clipboard.vb"}, callers.ToArray())
        ' ... and inside that file only the handler of the button names it.
        Dim settings As String = CompanionSource("Forms", "Share_Settings_Form.Clipboard.vb")
        Dim handler As Integer = settings.IndexOf("Private Sub OnClipboardShortcuts()", StringComparison.Ordinal)
        Dim callPos As Integer = settings.IndexOf("ClipboardShortcuts.CreateOnDesktop(", StringComparison.Ordinal)
        Assert.True(handler >= 0 AndAlso callPos > handler)
        Assert.Contains("AddressOf OnClipboardShortcuts", settings)
    End Sub

    <Fact>
    Public Sub The_cold_start_action_starts_no_manager_no_worker_and_asks_nothing()
        Dim text As String = CompanionSource("ClipboardOneShot.vb")
        For Each forbidden As String In {"WorkerProcess", "ShareController", "TrayContext", "MainWindow", "MessageBox", "ServerFeatures"}
            Assert.DoesNotContain(forbidden, text)
        Next
        Assert.Contains("ClipboardCoordinator", text)
    End Sub

    ''' <summary>The code of a VB source: every comment (a whole line or a tail) is cut, string literals are kept as they are.</summary>
    Private Shared Function WithoutComments(source As String) As String
        Dim result As New StringBuilder()
        For Each raw As String In source.Split({vbLf}, StringSplitOptions.None)
            Dim inString As Boolean = False
            Dim cut As Integer = raw.Length
            For i As Integer = 0 To raw.Length - 1
                Dim c As Char = raw(i)
                If c = """"c Then
                    inString = Not inString
                ElseIf c = "'"c AndAlso Not inString Then
                    cut = i
                    Exit For
                End If
            Next
            result.Append(raw, 0, cut).Append(vbLf)
        Next
        Return result.ToString()
    End Function

    <Fact>
    Public Sub The_words_say_clipboard_and_never_shared_clipboard()
        Dim text As String = WithoutComments(CompanionSource("Localization", "Localization.Clipboard.vb"))
        Assert.DoesNotContain("shared clipboard", text, StringComparison.OrdinalIgnoreCase)
        Assert.DoesNotContain("общий буфер", text, StringComparison.OrdinalIgnoreCase)
        Assert.DoesNotContain("общего буфера", text, StringComparison.OrdinalIgnoreCase)
        Dim messages As String = WithoutComments(CompanionSource("Core", "ClipboardMessages.vb"))
        Assert.DoesNotContain("общий буфер", messages, StringComparison.OrdinalIgnoreCase)
    End Sub

    <Fact>
    Public Sub No_clipboard_source_writes_a_value_or_a_file_name_into_a_log_line()
        ' A log line of the clipboard code names a kind, a size class, a count or an outcome - never a variable that holds a value, a
        ' name, a path, a server or a device. The names below are the ones that hold them.
        Dim holders As String() = {"snapshot.Content", "snapshot.Path", "value.Name", "announced.Name", "announced.SourceDeviceName",
                                   "device", "finalPath", "folder", "label", "target.Label", "path", "name"}
        For Each file As String In {"ClipboardExchange.vb", "ClipboardCoordinator.vb", "ClipboardWin32.vb", "ClipboardShortcuts.vb"}
            Dim text As String = CompanionSource("Core", file)
            For Each m As Match In Regex.Matches(text, "(?:Log\.Invoke|AppFileLogger\.WriteLine|AppFileLogger\.LogException)\(([^\r\n]*)")
                Dim callText As String = m.Groups(1).Value
                For Each holder As String In holders
                    Assert.False(Regex.IsMatch(callText, "(?<![A-Za-z0-9_.])" & Regex.Escape(holder) & "(?![A-Za-z0-9_])"),
                                 file & " logs a holder of a value: " & callText)
                Next
            Next
        Next
    End Sub

End Class
