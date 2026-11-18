Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The "Live broadcast" page of the Share Manager's settings (ticket L0332, SETTINGS-NAVIGATION 2.1 item A): the two standing
''' preferences the live broadcast window used to carry as check boxes - "allow viewers on my network" and "show the cursor".
''' The window keeps one link to this page (<c>Live_Broadcast_Form.OpenManagerSettingsPage</c>, wired by the tray).
'''
''' The page exists only while the live broadcast is offered (<see cref="LiveBroadcastHost.Offered"/>, the one gate every surface
''' of the producer consults); with the gate shut nothing is built and no page id "live" exists, so a route to it is ignored like
''' any unknown page. Reading and writing is <see cref="LiveBroadcastOptions"/>: a change is stored at once, as reload, change one
''' field, save; the viewers choice is applied to a running broadcast FIRST and a refusal reverts the box and says why.
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    Private _secLive As CollapsibleSection
    Private _chkLiveAllowLan As CheckBox
    Private _chkLiveCursor As CheckBox
    Private _lblLiveNote As Label
    ''' <summary>Set while the page changes a box by itself (loading it, reverting a refusal): the change handlers do nothing then.</summary>
    Private _liveApplying As Boolean

    ''' <summary>The window's page, or Nothing while the live broadcast is not offered. Added to the page list by
    ''' <see cref="AllSections"/> after Scheduled file operations and before the rest.</summary>
    Private Function CreateLiveSection() As CollapsibleSection
        If Not LiveBroadcastHost.Offered() Then Return Nothing
        Dim sec As CollapsibleSection = CreateSettingsSection(Live_Broadcast_Form.SettingsPageId, LivePageCaption())
        _secLive = sec

        _chkLiveAllowLan = New CheckBox With {.Name = NameOf(_chkLiveAllowLan), .AutoSize = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Разрешить зрителям в моей сети")}
        AddHandler _chkLiveAllowLan.CheckedChanged, AddressOf OnLiveAllowLanChanged
        Dim allowRow As OptionRowHost = OptionRows.InFlow(_chkLiveAllowLan)
        allowRow.Margin = New Padding(0, 2, 0, 2)
        sec.AddBodyRow(allowRow)

        _chkLiveCursor = New CheckBox With {.Name = NameOf(_chkLiveCursor), .AutoSize = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Показывать курсор")}
        AddHandler _chkLiveCursor.CheckedChanged, AddressOf OnLiveCursorChanged
        ' The cursor is read when the screen source is made: a broadcast already on air keeps the choice it started with.
        Dim cursorRow As OptionRowHost = OptionRows.InFlow(_chkLiveCursor,
            OptionHint(_chkLiveCursor, Localization.T("Действует со следующей трансляции.")))
        cursorRow.Margin = New Padding(0, 2, 0, 2)
        sec.AddBodyRow(cursorRow)

        ' The one sentence of a refused change (the same sentence the broadcast window gives a refused start). It starts where the
        ' captions start, on the reading-start side, and is empty (hidden) until a change is refused.
        _lblLiveNote = New Label With {.Name = "managerLiveNote", .AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0),
            .ForeColor = CollapsibleSection.AttentionColor, .Margin = New Padding(CaptionInsetLogical, 2, 0, 2), .Visible = False}
        sec.AddBodyRow(_lblLiveNote)

        Dim lnkLivePortal As New LinkLabel With {.Name = "managerLivePortalLink", .AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0),
            .Margin = New Padding(0, 4, 0, 8), .Text = Localization.T("Живая трансляция (портал)")}
        AddHandler lnkLivePortal.LinkClicked, Sub() NetworkInfo.OpenInBrowser(ShareLinks.ExchangePortalUrl("live-broadcast"))
        sec.AddBodyRow(lnkLivePortal)

        Return sec
    End Function

    Friend Shared Function LivePageCaption() As String
        Return Localization.T("Живая трансляция")
    End Function

    ''' <summary>Shows the stored values in the two boxes without storing anything. Read from the hive each time the page is
    ''' shown, so the boxes always say what the next start will use.</summary>
    Private Sub LoadLiveState()
        If _chkLiveAllowLan Is Nothing OrElse _chkLiveCursor Is Nothing Then Return
        Dim stored As LiveProducerSettings = LiveBroadcastOptions.ForHive().Load()
        Dim was As Boolean = _liveApplying
        _liveApplying = True
        Try
            _chkLiveAllowLan.Checked = stored.AllowLanVideoViewers
            _chkLiveCursor.Checked = stored.ScreenCursor
        Finally
            _liveApplying = was
        End Try
        ShowLiveNote("")
    End Sub

    Private Sub OnLiveCursorChanged(sender As Object, e As EventArgs)
        If _loading OrElse _liveApplying Then Return
        Try
            LiveBroadcastOptions.ForHive().SetShowCursor(_chkLiveCursor.Checked)
        Catch
            ' The hive refused the write: the box must not claim what is not stored.
            RevertLiveBox(_chkLiveCursor)
        End Try
    End Sub

    ''' <summary>Stores the choice and, while a broadcast runs, applies it to that broadcast first. A refusal puts the box back and
    ''' shows the sentence; the stored value and the running broadcast keep the previous choice.</summary>
    Private Sub OnLiveAllowLanChanged(sender As Object, e As EventArgs)
        If _loading OrElse _liveApplying Then Return
        Dim controller As LiveBroadcastController = LiveBroadcastHost.Controller()
        Dim result As LiveStartResult
        Try
            result = LiveBroadcastOptions.ForHive().SetAllowLanViewers(_chkLiveAllowLan.Checked, controller)
        Catch
            result = LiveStartResult.Failed(LiveStartRefusal.None)
        End Try
        If result.Ok Then
            ShowLiveNote("")
            Return
        End If
        RevertLiveBox(_chkLiveAllowLan)
        ShowLiveNote(Live_Broadcast_Form.RefusalSentence(result, controller.State.Port))
    End Sub

    Private Sub RevertLiveBox(box As CheckBox)
        Dim was As Boolean = _liveApplying
        _liveApplying = True
        Try
            box.Checked = Not box.Checked
        Finally
            _liveApplying = was
        End Try
    End Sub

    Private Sub ShowLiveNote(text As String)
        If _lblLiveNote Is Nothing Then Return
        _lblLiveNote.Text = text
        _lblLiveNote.Visible = text.Length > 0
        If _built AndAlso _pageHost IsNot Nothing Then _pageHost.PerformLayout()
    End Sub

    ''' <summary>The language changed: the boxes and the explanation are translated in place with the rest of the window; a refusal
    ''' sentence belongs to the moment it was given, so it is taken away rather than left in the old language.</summary>
    Private Sub RefreshLiveLanguage()
        If _lblLiveNote IsNot Nothing Then
            _lblLiveNote.Text = ""
            _lblLiveNote.Visible = False
        End If
    End Sub

End Class
