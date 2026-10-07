Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Spec 159 section 3.2, as a source scan: every way into the broadcasts page asks the one predicate, and the two
''' surfaces that advertise it (the tray item, the main window's button) start absent. The window code is not
''' unit-testable without a desktop, so this holds the wiring the way the other source-scan guards in this project do:
''' a new entry that skips <see cref="BroadcastPage.ShouldOffer(WorkerStatus)"/> fails here, naming the file.
''' </summary>
Public Class BroadcastWiringTests

    ''' <summary>The only files allowed to open the page; both must ask the predicate first.</summary>
    Private Shared ReadOnly Entries As String() = {"TrayContext.vb", "MainWindow.vb"}

    Private Shared Function PageConstructions() As List(Of String)
        Return ExchangeTestSupport.CompanionSources().
            Where(Function(f) ExchangeTestSupport.CodeOnly(File.ReadAllText(f)).Contains("New Share_Broadcasts_Form")).
            Select(Function(f) Path.GetFileName(f)).
            OrderBy(Function(n) n, StringComparer.Ordinal).ToList()
    End Function

    <Fact>
    Public Sub Only_the_two_known_entries_open_the_page()
        Assert.Equal(Entries.OrderBy(Function(n) n, StringComparer.Ordinal).ToList(), PageConstructions())
    End Sub

    <Fact>
    Public Sub Every_entry_asks_the_predicate_before_it_opens_the_page()
        For Each name As String In PageConstructions()
            Dim code As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(
                ExchangeTestSupport.CompanionSources().First(Function(f) Path.GetFileName(f) = name)))
            Dim ask As Integer = code.IndexOf("BroadcastPage.ShouldOffer", StringComparison.Ordinal)
            Dim open As Integer = code.IndexOf("New Share_Broadcasts_Form", StringComparison.Ordinal)
            Assert.True(ask >= 0 AndAlso ask < open,
                        name & " opens the broadcasts page without first asking BroadcastPage.ShouldOffer (spec 159 section 3.2)")
        Next
    End Sub

    ''' <summary>The tray item is created hidden, so no menu build can show it before the worker has vouched for it.</summary>
    <Fact>
    Public Sub The_tray_item_starts_absent_and_is_decided_on_opening()
        Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("TrayContext.vb"))
        Assert.Matches(New Regex("miBroadcasts As New ToolStripMenuItem\([^\r\n]*\)\s*With\s*\{\.Visible\s*=\s*False\}"), code)
        Assert.Contains("RefreshBroadcastTrayItem(miBroadcasts)", code)
        Assert.Contains("item.Visible = BroadcastPage.ShouldOffer", code)
    End Sub

    ''' <summary>The main window's button is created hidden and shown from the status, in the one place that applies it.</summary>
    <Fact>
    Public Sub The_main_window_button_starts_absent_and_follows_the_status()
        Dim layout As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\MainWindow.Layout.vb"))
        Assert.Matches(New Regex("btnBroadcasts = New Button With \{[^}]*\.Visible = False\}"), layout)
        Dim access As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\MainWindow.Access.vb"))
        Assert.Contains("btnBroadcasts.Visible = BroadcastPage.ShouldOffer(_status)", access)
    End Sub

End Class
