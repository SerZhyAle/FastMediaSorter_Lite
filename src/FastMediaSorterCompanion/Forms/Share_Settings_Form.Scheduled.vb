Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The "Scheduled file operations" page of the Share Manager's settings (ticket 222, section 7.1;
''' L0352): an INVOCATION of the standalone Scheduled operations window, not a second copy of its
''' management body. The page states what the feature is, shows the live feature state and the live
''' operation count straight from the engine, and opens the one window. It holds no list, no commands
''' and no editor of the stored feature switch - the window is its one editor.
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    Private _secScheduled As CollapsibleSection
    Private _schedNote As Label
    Private _schedFeatureState As Label
    Private _schedOpenWindow As LinkLabel
    Private _schedEngineEvents As Boolean

    ''' <summary>A content width of this page's own, matching the drive page's wrap width.</summary>
    Private Const ScheduledContentWidth As Integer = 500

    ''' <summary>The window's sections with this page before Help (and after the exchange account group).</summary>
    Private Function CreateScheduledSection() As CollapsibleSection
        Dim sec As CollapsibleSection = CreateSettingsSection("scheduled", ScheduledPageCaption())
        _secScheduled = sec

        _schedNote = New Label With {.AutoSize = True, .MaximumSize = New Size(ScheduledContentWidth, 0),
            .Margin = New Padding(0, 2, 0, 6), .Name = "managerSchedNote",
            .Text = Localization.T("Файловые операции по расписанию - часть менеджера общего доступа: устанавливаются вместе с ним и выполняются им. Расписание работает только пока менеджер запущен."),
            .AccessibleName = ScheduledPageCaption()}
        sec.AddBodyRow(_schedNote)

        _schedFeatureState = New Label With {.AutoSize = True, .MaximumSize = New Size(ScheduledContentWidth, 0),
            .Margin = New Padding(0, 0, 0, 4), .Name = "managerSchedFeatureState"}
        sec.AddBodyRow(_schedFeatureState)

        _schedOpenWindow = New LinkLabel With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 6), .Name = "managerSchedOpenWindow",
            .Text = Localization.T("Открыть в отдельном окне.."), .AccessibleName = Localization.T("Открыть в отдельном окне..")}
        AddHandler _schedOpenWindow.LinkClicked, Sub() Scheduled_Manager_Form.OpenScheduledManager("", Me)
        sec.AddBodyRow(_schedOpenWindow)

        ' The live facts refresh straight from the engine: the queue count in the summary, the switch state in
        ' the body. Released with the window's own live resources, so the tray process never keeps a dead page.
        If ScheduledHost.Engine IsNot Nothing Then
            _schedEngineEvents = True
            AddHandler ScheduledHost.Engine.Changed, AddressOf OnScheduledEngineChanged
        End If
        RefreshScheduledState()
        Return sec
    End Function

    Friend Shared Function ScheduledPageCaption() As String
        Return Localization.T("Файловые операции по расписанию")
    End Function

    ''' <summary>The page's live facts - the queue count and the switch state - read straight from the
    ''' engine: state and invocation, never a second editor (L0352).</summary>
    Private Sub RefreshScheduledState()
        If _secScheduled Is Nothing Then Return
        Dim engine As ScheduledEngine = ScheduledHost.Engine
        Dim count As Integer = If(engine Is Nothing, 0, engine.Snapshot().Count)
        _secScheduled.Summary = If(count = 0, Localization.T("Операций нет"), Localization.TF("Операций: {0}", count))
        _secScheduled.SummaryColor = ThemeService.Palette.TextMuted
        If _schedFeatureState IsNot Nothing Then
            _schedFeatureState.Text = Scheduled_Manager_Panel.FeatureStateText(engine IsNot Nothing AndAlso engine.FeatureEnabled)
        End If
    End Sub

    Private Sub OnScheduledEngineChanged(sender As Object, e As EventArgs)
        Try
            If IsDisposed OrElse Disposing OrElse Not IsHandleCreated Then Return
            BeginInvoke(New Action(Sub()
                                       If IsDisposed OrElse Disposing Then Return
                                       RefreshScheduledState()
                                   End Sub))
        Catch ex As InvalidOperationException
            ' The window is going away.
        End Try
    End Sub

    ''' <summary>Removes the engine subscription this page took. Idempotent, called from the window's
    ''' one release path (FormClosed and Disposed).</summary>
    Private Sub ReleaseScheduledEngineEvents()
        If Not _schedEngineEvents Then Return
        _schedEngineEvents = False
        If ScheduledHost.Engine IsNot Nothing Then
            RemoveHandler ScheduledHost.Engine.Changed, AddressOf OnScheduledEngineChanged
        End If
    End Sub

    ''' <summary>The language changed: the page's own texts and the live facts.</summary>
    Private Sub RefreshScheduledLanguage()
        If _schedNote IsNot Nothing Then
            _schedNote.Text = Localization.T("Файловые операции по расписанию - часть менеджера общего доступа: устанавливаются вместе с ним и выполняются им. Расписание работает только пока менеджер запущен.")
            _schedNote.AccessibleName = ScheduledPageCaption()
        End If
        If _schedOpenWindow IsNot Nothing Then
            _schedOpenWindow.Text = Localization.T("Открыть в отдельном окне..")
            _schedOpenWindow.AccessibleName = Localization.T("Открыть в отдельном окне..")
        End If
        RefreshScheduledState()
    End Sub

    ' --- entry from the outside ------------------------------------------------------------------------

    ''' <summary>Opens the scheduled destination, with an optional folder the caller asked to start a
    ''' new operation from (7.4): the management WINDOW takes it - the page holds nothing to prefill
    ''' (L0352). The settings window still opens on its scheduled page, as any page request does.</summary>
    Friend Sub FocusScheduledPage(Optional folder As String = "")
        SelectSettingsPage("scheduled", "operations")
        If folder IsNot Nothing AndAlso folder.Length > 0 Then Scheduled_Manager_Form.OpenScheduledManager(folder, Me)
    End Sub

End Class
