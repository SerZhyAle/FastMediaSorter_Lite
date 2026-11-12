Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Windows.Forms

''' <summary>
''' The "Scheduled file operations" page of the Share Manager's settings (ticket 222, section 7.1).
''' Since L0297 the page is ONE HOST of the shared management body <see cref="Scheduled_Manager_Panel"/>;
''' the standalone Scheduled operations window is the other. This page adds only its own wrapper - the
''' collapsible section, the section summary and the link that opens the standalone window - and keeps
''' the page available as an alternative view, as the owner decided.
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    Private _secScheduled As CollapsibleSection
    Private _schedPanel As Scheduled_Manager_Panel
    Private _schedOpenWindow As LinkLabel

    ''' <summary>The window's sections with this page before Help (and after the exchange account group).</summary>
    Private Function CreateScheduledSection() As CollapsibleSection
        Dim sec As CollapsibleSection = CreateSettingsSection("scheduled", ScheduledPageCaption())
        _secScheduled = sec

        _schedPanel = New Scheduled_Manager_Panel() With {.Name = "managerSchedPanel"}
        AddHandler _schedPanel.StateChanged, Sub() UpdateScheduledSummary()
        AddHandler _schedPanel.AutostartLinkRequested, Sub() SelectSettingsPage("startup", "startup")
        sec.AddBodyRow(_schedPanel)

        _schedOpenWindow = New LinkLabel With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 6), .Name = "managerSchedOpenWindow",
            .Text = Localization.T("Открыть в отдельном окне.."), .AccessibleName = Localization.T("Открыть в отдельном окне..")}
        AddHandler _schedOpenWindow.LinkClicked, Sub() Scheduled_Manager_Form.OpenScheduledManager("", Me)
        sec.AddBodyRow(_schedOpenWindow)

        Return sec
    End Function

    Friend Shared Function ScheduledPageCaption() As String
        Return Localization.T("Файловые операции по расписанию")
    End Function

    ''' <summary>The section summary follows the panel's list - one live state, no second code path.</summary>
    Private Sub UpdateScheduledSummary()
        If _secScheduled Is Nothing OrElse _schedPanel Is Nothing Then Return
        Dim count As Integer = _schedPanel.OperationCount
        _secScheduled.Summary = If(count = 0, Localization.T("Операций нет"), Localization.TF("Операций: {0}", count))
        _secScheduled.SummaryColor = ThemeService.Palette.TextMuted
    End Sub

    ''' <summary>The language changed: the page's own caption (the link) and the panel's controls.</summary>
    Private Sub RefreshScheduledLanguage()
        If _schedOpenWindow IsNot Nothing Then
            _schedOpenWindow.Text = Localization.T("Открыть в отдельном окне..")
            _schedOpenWindow.AccessibleName = Localization.T("Открыть в отдельном окне..")
        End If
        If _schedPanel IsNot Nothing Then _schedPanel.RefreshLanguage()
        UpdateScheduledSummary()
    End Sub

    ' --- entry from the outside ------------------------------------------------------------------------

    ''' <summary>Opens this page, with an optional folder the viewer's launcher asked to start a new
    ''' operation from (7.4), once the page is up.</summary>
    Friend Sub FocusScheduledPage(Optional folder As String = "")
        SelectSettingsPage("scheduled", "operations")
        If _schedPanel IsNot Nothing Then
            _schedPanel.PrefillFromFolder(folder)
            ' Already on screen (a second request while the window is open): there is no OnShown to wait for.
            If folder IsNot Nothing AndAlso folder.Length > 0 AndAlso settingsWasPresented AndAlso IsHandleCreated AndAlso Visible Then
                _schedPanel.HandleScheduledPrefill()
            End If
        End If
    End Sub

End Class
