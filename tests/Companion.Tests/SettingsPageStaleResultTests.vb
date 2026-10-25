Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' SETTINGS-NAVIGATION 2.0 section 5 rules 3 and 5 on the Share Manager's settings window (ticket 243 T-13): a load that
''' repaints a page - the hosting probe, the exchange snapshot, the accounts read - is dropped when a newer request of its kind
''' started or the window went away, so an old result never replaces what the page now shows; and a window opened and closed
''' over and over leaves no live timer and no subscription behind. Each stale case starts a read that BLOCKS on a gate, supersedes
''' it, completes the newer one, then lets the old one return with different data and asserts that the page still shows the newer.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class SettingsPageStaleResultTests
    Implements IDisposable

    Private ReadOnly _dir As String
    Private ReadOnly _worker As String

    Public Sub New()
        ExchangeTestSupport.Reset()
        Share_Settings_Form.HostingProbeOverride = Nothing
        _dir = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        ' The Hosting page lives in the Manager window while the worker payload is installed (253 R-2).
        _worker = ExchangeTestSupport.MakeFakeWorker()
        ExchangeProcess.ExeDirectoryOverride = _dir
        ExchangeProcess.RunOverride = AddressOf StandIn
        ' no server runs: the pipe does not exist and every call goes through the binary's offline verbs
        ExchangeAdmin.PipeNameOverride = "fms-exchange-admin-test-none-" & Guid.NewGuid().ToString("N").Substring(0, 12)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Share_Settings_Form.HostingProbeOverride = Nothing
        ExchangeTestSupport.Reset()
        ExchangeTestSupport.DeleteQuietly(_dir)
        ExchangeTestSupport.DeleteQuietly(_worker)
    End Sub

    Private Shared Function InspectJson(fingerprint As String) As String
        Return "{""dataDir"":""C:\\fms-test-no-such-dir"",""configPresent"":true,""accounts"":2,""devices"":3,""certPresent"":true,""fingerprint"":""" & fingerprint &
               """,""listen"":"":22022"",""portRange"":""40000-40100"",""publicEndpoint"":""""}"
    End Function

    Private Shared Function AccountsJson(ParamArray logins As String()) As String
        Return "[" & String.Join(",", logins.Select(Function(l) "{""login"":""" & l & """,""createdAt"":"""",""devices"":0}")) & "]"
    End Function

    Private Shared Function StandIn(args As String(), stdin As String) As ExchangeProcess.RunResult
        Dim r As New ExchangeProcess.RunResult With {.Started = True}
        Select Case args(0)
            Case "inspect" : r.StdOut = InspectJson("SHA256:initial")
            Case "account" : r.StdOut = AccountsJson("alice", "bob")
            Case "device" : r.StdOut = "[]"
        End Select
        Return r
    End Function

    Private Shared Function Field(Of T)(obj As Object, name As String) As T
        Return DirectCast(obj.GetType().GetField(name, BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(obj), T)
    End Function

    Private Shared Sub Pump(until As Func(Of Boolean), what As String)
        Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(12)
        While DateTime.UtcNow < deadline AndAlso Not until()
            Application.DoEvents()
            Thread.Sleep(15)
        End While
        Assert.True(until(), "timed out waiting for " & what)
    End Sub

    ''' <summary>Lets the message loop run for a while - for asserting that something does NOT happen.</summary>
    Private Shared Sub PumpFor(milliseconds As Integer)
        Dim deadline As DateTime = DateTime.UtcNow.AddMilliseconds(milliseconds)
        While DateTime.UtcNow < deadline
            Application.DoEvents()
            Thread.Sleep(15)
        End While
    End Sub

    Private Shared Function StartRefresh(form As Share_Settings_Form, method As String) As Task
        Return DirectCast(GetType(Share_Settings_Form).GetMethod(method, BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, Nothing), Task)
    End Function

    Private Shared Sub ShowOffScreen(form As Share_Settings_Form)
        form.StartPosition = FormStartPosition.Manual
        form.Location = New Point(-6000, -6000)
        form.ShowInTaskbar = False
        form.Show()
    End Sub

    Private Shared Sub SelectPage(form As Share_Settings_Form, pageId As String)
        GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, New Object() {pageId})
    End Sub

    ' ------------------------------------------------------------------ the hosting probe ----

    <Fact>
    Public Sub A_late_hosting_probe_for_an_older_request_does_not_replace_the_newer_state()
        ExchangeTestSupport.OnUi(
            Sub()
                Using form As New Share_Settings_Form()
                    ShowOffScreen(form)
                    Dim older As New WorkerResponse With {.Ok = True}
                    Dim newer As New WorkerResponse With {.Ok = True}
                    Dim entered As New ManualResetEventSlim(False)
                    Dim gate As New ManualResetEventSlim(False)
                    Dim calls As Integer = 0
                    Share_Settings_Form.HostingProbeOverride =
                        Function()
                            If Interlocked.Increment(calls) = 1 Then
                                entered.Set()
                                gate.Wait(TimeSpan.FromSeconds(10))
                                Return older
                            End If
                            Return newer
                        End Function

                    Dim first As Task = StartRefresh(form, "RefreshHostingStateAsync")
                    Pump(Function() entered.IsSet, "the first probe to be out")
                    Dim second As Task = StartRefresh(form, "RefreshHostingStateAsync")
                    Pump(Function() second.IsCompleted, "the newer probe to be painted")
                    Assert.Same(newer, Field(Of WorkerResponse)(form, "_hostingSnapshot"))

                    gate.Set()
                    Pump(Function() first.IsCompleted, "the older probe to return")
                    PumpFor(150)
                    Assert.Same(newer, Field(Of WorkerResponse)(form, "_hostingSnapshot"))
                    form.Hide()
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub A_hosting_probe_that_returns_after_the_window_closed_paints_nothing()
        ExchangeTestSupport.OnUi(
            Sub()
                Dim answer As New WorkerResponse With {.Ok = True}
                Dim entered As New ManualResetEventSlim(False)
                Dim gate As New ManualResetEventSlim(False)
                Share_Settings_Form.HostingProbeOverride =
                    Function()
                        entered.Set()
                        gate.Wait(TimeSpan.FromSeconds(10))
                        Return answer
                    End Function
                Dim form As New Share_Settings_Form()
                ShowOffScreen(form)
                Dim pending As Task = StartRefresh(form, "RefreshHostingStateAsync")
                Pump(Function() entered.IsSet, "the probe to be out")
                form.Close()
                form.Dispose()
                gate.Set()
                Pump(Function() pending.IsCompleted, "the probe to return")
                Assert.False(pending.IsFaulted, "a probe returning into a closed window must end quietly")
                Assert.Null(Field(Of WorkerResponse)(form, "_hostingSnapshot"))
            End Sub)
    End Sub

    ' ------------------------------------------------------------------ the exchange snapshot ----

    <Fact>
    Public Sub A_late_exchange_snapshot_for_an_older_request_does_not_replace_the_newer_state()
        ExchangeTestSupport.OnUi(
            Sub()
                Using form As New Share_Settings_Form(destination:=SettingsDestination.ExchangeServer)
                    ShowOffScreen(form)
                    SelectPage(form, "exchange")
                    Dim snapshotOf As Func(Of String) = Function()
                                                            Dim s As ExchangeSnapshot = Field(Of ExchangeSnapshot)(form, "_exchangeSnapshot")
                                                            Return If(s Is Nothing OrElse s.Report Is Nothing, "", s.Report.Fingerprint)
                                                        End Function
                    Pump(Function() snapshotOf() = "SHA256:initial", "the first live read")
                    Pump(Function() Not Field(Of Boolean)(form, "_exAccReading"), "the accounts read of the first snapshot to end")

                    Dim entered As New ManualResetEventSlim(False)
                    Dim gate As New ManualResetEventSlim(False)
                    Dim calls As Integer = 0
                    ExchangeProcess.RunOverride =
                        Function(args, stdin)
                            Dim r As ExchangeProcess.RunResult = StandIn(args, stdin)
                            If args(0) = "inspect" Then
                                If Interlocked.Increment(calls) = 1 Then
                                    entered.Set()
                                    gate.Wait(TimeSpan.FromSeconds(10))
                                    r.StdOut = InspectJson("SHA256:late-and-old")
                                Else
                                    r.StdOut = InspectJson("SHA256:newer")
                                End If
                            End If
                            Return r
                        End Function

                    Dim first As Task = StartRefresh(form, "RefreshExchangeAsync")
                    Pump(Function() entered.IsSet, "the first read to be out")
                    Dim second As Task = StartRefresh(form, "RefreshExchangeAsync")
                    Pump(Function() second.IsCompleted, "the newer read to be painted")
                    Assert.Equal("SHA256:newer", snapshotOf())

                    gate.Set()
                    Pump(Function() first.IsCompleted, "the older read to return")
                    PumpFor(150)
                    Assert.Equal("SHA256:newer", snapshotOf())
                    form.Hide()
                End Using
            End Sub)
    End Sub

    ' ------------------------------------------------------------------ the accounts read ----

    <Fact>
    Public Sub A_late_accounts_read_for_an_older_request_is_dropped_and_the_newer_one_is_painted()
        ExchangeTestSupport.OnUi(
            Sub()
                Using form As New Share_Settings_Form(destination:=SettingsDestination.ExchangeServer)
                    ShowOffScreen(form)
                    SelectPage(form, "exchange")
                    Dim accounts As ListView = Field(Of ListView)(form, "_lvXAccounts")
                    Pump(Function() accounts.Items.Count = 2, "the first accounts list")
                    Pump(Function() Not Field(Of Boolean)(form, "_exAccReading"), "the first read to end")

                    Dim entered1 As New ManualResetEventSlim(False)
                    Dim gate1 As New ManualResetEventSlim(False)
                    Dim entered2 As New ManualResetEventSlim(False)
                    Dim gate2 As New ManualResetEventSlim(False)
                    Dim calls As Integer = 0
                    ExchangeProcess.RunOverride =
                        Function(args, stdin)
                            Dim r As ExchangeProcess.RunResult = StandIn(args, stdin)
                            If args(0) = "account" AndAlso args.Length > 1 AndAlso args(1) = "list" Then
                                Select Case Interlocked.Increment(calls)
                                    Case 1
                                        entered1.Set()
                                        gate1.Wait(TimeSpan.FromSeconds(10))
                                        r.StdOut = AccountsJson("stale-1")
                                    Case 2
                                        entered2.Set()
                                        gate2.Wait(TimeSpan.FromSeconds(10))
                                        r.StdOut = AccountsJson("fresh-1", "fresh-2", "fresh-3")
                                    Case Else
                                        r.StdOut = AccountsJson("fresh-1", "fresh-2", "fresh-3")
                                End Select
                            End If
                            Return r
                        End Function

                    Dim first As Task = StartRefresh(form, "RefreshAccountsAsync")
                    Pump(Function() entered1.IsSet, "the first read to be out")
                    ' a second request while the first is out (the person clicked another account, the server state changed):
                    Dim second As Task = StartRefresh(form, "RefreshAccountsAsync")
                    Assert.True(second.IsCompleted, "a request made while a read is out only supersedes it")

                    gate1.Set()
                    Pump(Function() entered2.IsSet, "the re-read that the second request asked for")
                    ' the first read has returned with a list for a request that is gone: it must not be on the page
                    PumpFor(200)
                    Assert.Equal({"alice", "bob"}, accounts.Items.Cast(Of ListViewItem)().Select(Function(i) i.Text).ToArray())
                    Assert.Equal(2, Field(Of ExchangeAccountsView)(form, "_exAccView").Accounts.Count)

                    gate2.Set()
                    Pump(Function() first.IsCompleted, "the loop to end")
                    Assert.Equal({"fresh-1", "fresh-2", "fresh-3"}, accounts.Items.Cast(Of ListViewItem)().Select(Function(i) i.Text).ToArray())
                    form.Hide()
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub An_accounts_read_that_returns_after_the_state_changed_to_no_accounts_does_not_bring_the_list_back()
        ExchangeTestSupport.OnUi(
            Sub()
                Using form As New Share_Settings_Form(destination:=SettingsDestination.ExchangeServer)
                    ShowOffScreen(form)
                    SelectPage(form, "exchange")
                    Dim accounts As ListView = Field(Of ListView)(form, "_lvXAccounts")
                    Pump(Function() accounts.Items.Count = 2, "the first accounts list")
                    Pump(Function() Not Field(Of Boolean)(form, "_exAccReading"), "the first read to end")

                    Dim entered As New ManualResetEventSlim(False)
                    Dim gate As New ManualResetEventSlim(False)
                    Dim calls As Integer = 0
                    ExchangeProcess.RunOverride =
                        Function(args, stdin)
                            Dim r As ExchangeProcess.RunResult = StandIn(args, stdin)
                            If args(0) = "account" AndAlso args.Length > 1 AndAlso args(1) = "list" AndAlso Interlocked.Increment(calls) = 1 Then
                                entered.Set()
                                gate.Wait(TimeSpan.FromSeconds(10))
                                r.StdOut = AccountsJson("ghost")
                            End If
                            Return r
                        End Function

                    Dim pending As Task = StartRefresh(form, "RefreshAccountsAsync")
                    Pump(Function() entered.IsSet, "the read to be out")
                    ' the exchange group stops offering accounts (the same step ApplyExchangeAdminState takes): the read in flight is void
                    Field(Of StaleGuard)(form, "_exAccGuard").Invalidate()
                    GetType(Share_Settings_Form).GetField("_exAccView", BindingFlags.Instance Or BindingFlags.NonPublic).SetValue(form, Nothing)

                    gate.Set()
                    Pump(Function() pending.IsCompleted, "the read to return")
                    PumpFor(150)
                    Assert.Null(Field(Of ExchangeAccountsView)(form, "_exAccView"))
                    Assert.DoesNotContain("ghost", accounts.Items.Cast(Of ListViewItem)().Select(Function(i) i.Text))
                    form.Hide()
                End Using
            End Sub)
    End Sub

    ' ------------------------------------------------------------------ opened and closed over and over ----

    Private Shared Function IsTimerReleased(timer As System.Windows.Forms.Timer) As Boolean
        If timer Is Nothing Then Return True
        If timer.Enabled Then Return False
        ' WinForms keeps a native timer window until Dispose; its private field is null once the timer is disposed. If the field
        ' is renamed by a later runtime the Enabled check above still holds the test.
        Dim native As FieldInfo = GetType(System.Windows.Forms.Timer).GetField("_timerWindow", BindingFlags.Instance Or BindingFlags.NonPublic)
        Return native Is Nothing OrElse native.GetValue(timer) Is Nothing
    End Function

    <Theory>
    <InlineData(SettingsDestination.ExchangeServer)>
    <InlineData(SettingsDestination.Manager)>
    Public Sub A_window_opened_and_closed_twenty_times_leaves_no_live_timer_and_no_process_subscription(destination As SettingsDestination)
        ExchangeTestSupport.OnUi(
            Sub()
                Dim consoleExited As FieldInfo = GetType(ExchangeProcess).GetField("ConsoleExitedEvent", BindingFlags.Static Or BindingFlags.NonPublic Or BindingFlags.Public)
                Dim subscribersBefore As Integer = SubscriberCount(consoleExited)
                Dim names As String() = {"_exAccTimer", "_pxTimer", "_accTimer"}
                Dim builtAny As Boolean = False
                For round As Integer = 1 To 20
                    Dim form As New Share_Settings_Form(destination:=destination)
                    ShowOffScreen(form)
                    Application.DoEvents()
                    Dim timers As New List(Of System.Windows.Forms.Timer)()
                    For Each name As String In names
                        Dim f As FieldInfo = GetType(Share_Settings_Form).GetField(name, BindingFlags.Instance Or BindingFlags.NonPublic)
                        Dim t As System.Windows.Forms.Timer = TryCast(f?.GetValue(form), System.Windows.Forms.Timer)
                        If t IsNot Nothing Then timers.Add(t)
                    Next
                    builtAny = builtAny OrElse timers.Count > 0
                    ' Alternate the two ways a window ends: Close (FormClosed) and Hide + Dispose (no FormClosed at all).
                    If round Mod 2 = 0 Then
                        form.Close()
                        form.Dispose()
                    Else
                        form.Hide()
                        form.Dispose()
                    End If
                    For Each t As System.Windows.Forms.Timer In timers
                        Assert.True(IsTimerReleased(t), "a timer of round " & round & " is still live after the window ended")
                    Next
                Next
                If destination = SettingsDestination.ExchangeServer Then Assert.True(builtAny, "the exchange window built none of the timers this test checks")
                Assert.Equal(subscribersBefore, SubscriberCount(consoleExited))
            End Sub)
    End Sub

    Private Shared Function SubscriberCount(eventField As FieldInfo) As Integer
        If eventField Is Nothing Then Return 0
        Dim handler As [Delegate] = TryCast(eventField.GetValue(Nothing), [Delegate])
        Return If(handler Is Nothing, 0, handler.GetInvocationList().Length)
    End Function

End Class
