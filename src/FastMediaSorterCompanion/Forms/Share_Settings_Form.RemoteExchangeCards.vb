Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>One card of the rail of the remote administration window (ticket L0278 5.6, 5.6.2). The shell hosts the control and calls
''' <see cref="ApplyState"/> on every state change, on the UI thread; a card holds no state of the sign-in of its own (7.6).</summary>
Friend MustInherit Class RemoteAdminCard
    Inherits UserControl

    Protected ReadOnly Controller As RemoteAdminController

    Protected Sub New(controller As RemoteAdminController)
        Me.Controller = controller
    End Sub

    ''' <summary>The stable id of the rail entry; the window's saved context refers to it.</summary>
    Friend MustOverride ReadOnly Property RailId As String

    ''' <summary>The caption of the rail entry, in the current language.</summary>
    Friend MustOverride ReadOnly Property RailCaption As String

    ''' <summary>Paints the card from <c>Controller.State</c>.</summary>
    Friend MustOverride Sub ApplyState()

    ''' <summary>The inks the card chose itself (muted, greyed rows), read again from the current theme: the tree pass of a live theme
    ''' change knows nothing of them.</summary>
    Friend MustOverride Sub ApplyThemeInks()

    ''' <summary>An action of the card ended, whatever it came to: the shell repaints what an action can change beside the state (a
    ''' stored "do not ask again" turns the reset button on).</summary>
    Friend Event ActionFinished As EventHandler

    Protected Sub RaiseActionFinished()
        RaiseEvent ActionFinished(Me, EventArgs.Empty)
    End Sub
End Class

''' <summary>The six cards, in rail order (5.6): none is ever hidden.</summary>
Friend NotInheritable Class RemoteAdminCards

    Private Sub New()
    End Sub

    Friend Shared Function CreateAll(controller As RemoteAdminController) As IReadOnlyList(Of RemoteAdminCard)
        Dim cards As New List(Of RemoteAdminCard) From {
            New RemoteAdminStatusCard(controller),
            New RemoteAdminAccountsCard(controller),
            New RemoteAdminDevicesCard(controller),
            New RemoteAdminInvitationsCard(controller),
            New RemoteAdminSharedInvitationCard(controller),
            New RemoteAdminLimitsCard(controller)}
        Return cards
    End Function

End Class

''' <summary>The words of the rows the cards share (kind, a limit, a count).</summary>
Friend NotInheritable Class RemoteAdminCardText

    Private Sub New()
    End Sub

    Friend Shared Function Number(value As Long) As String
        Return value.ToString(CultureInfo.InvariantCulture)
    End Function

    ''' <summary>"member" and "guest" in the person's language; anything else as the server sent it.</summary>
    Friend Shared Function KindWord(kind As String) As String
        Select Case kind
            Case "guest" : Return Localization.T("Гость")
            Case "member" : Return Localization.T("Участник")
            Case Else : Return If(kind, "")
        End Select
    End Function

    ''' <summary>The members of <paramref name="limits"/> that are set, as label and value, in the order of 5.6.2.</summary>
    Friend Shared Function LimitRows(limits As RemoteAdminLimits) As List(Of KeyValuePair(Of String, String))
        Dim rows As New List(Of KeyValuePair(Of String, String))()
        If limits Is Nothing Then Return rows
        If limits.DevicesPerAccount.HasValue Then rows.Add(New KeyValuePair(Of String, String)(Localization.T("Устройств на учётную запись"), Number(limits.DevicesPerAccount.Value)))
        If limits.ResourcesPerAccount.HasValue Then rows.Add(New KeyValuePair(Of String, String)(Localization.T("Ресурсов на учётную запись"), Number(limits.ResourcesPerAccount.Value)))
        If limits.BroadcastsPerAccount.HasValue Then rows.Add(New KeyValuePair(Of String, String)(Localization.T("Трансляций на учётную запись"), Number(limits.BroadcastsPerAccount.Value)))
        If limits.BroadcastsPerDevice.HasValue Then rows.Add(New KeyValuePair(Of String, String)(Localization.T("Трансляций на устройство"), Number(limits.BroadcastsPerDevice.Value)))
        If limits.ClipboardMaxBytes.HasValue Then rows.Add(New KeyValuePair(Of String, String)(Localization.T("Размер буфера обмена, байт"), Number(limits.ClipboardMaxBytes.Value)))
        Return rows
    End Function

    ''' <summary>One cell: "label: value" for every limit that is set, "" when none is.</summary>
    Friend Shared Function LimitsLine(limits As RemoteAdminLimits) As String
        Dim parts As New List(Of String)()
        For Each pair As KeyValuePair(Of String, String) In LimitRows(limits)
            parts.Add(Localization.TF("{0}: {1}", pair.Key, pair.Value))
        Next
        Return String.Join("; ", parts)
    End Function
End Class

''' <summary>What the six cards have in common (5.5, 5.6): the empty-state line with no session, the greyed last-known rows after a
''' lapse, the three moments of a read (first show after a sign-in, Refresh, the re-read after a change), one read at a time, the
''' sentence of a failed read beside the buttons, and the shape of a list. A card costs nothing until it is shown: nothing here reads in
''' a constructor, and nothing reads on a timer.</summary>
Friend MustInherit Class RemoteAdminCardBase
    Inherits RemoteAdminCard

    ''' <summary>The width of a list and of the text over it, in 96-dpi design units.</summary>
    Protected Const ContentWidth As Integer = 560

    Private Const MaxListRows As Integer = 8

    Private ReadOnly _flow As FlowLayoutPanel
    Private ReadOnly _lblLast As Label
    Private ReadOnly _buttonRow As FlowLayoutPanel
    Private ReadOnly _lblLine As Label
    Private ReadOnly _btnRefresh As Button
    Private ReadOnly _actions As New List(Of Button)()
    Private ReadOnly _needs As New Dictionary(Of Button, Func(Of Boolean))()
    Private ReadOnly _lists As New List(Of ListView)()
    Private ReadOnly _content As New List(Of Control)()
    Private _shownGeneration As Long = Long.MinValue
    Private _wasLive As Boolean
    Private _needsRead As Boolean
    Private _reading As Boolean
    Private _again As Boolean
    Private _acting As Boolean
    Private _hasRows As Boolean
    Private _greyed As Boolean
    Private _note As String = ""
    Private _lineText As String = ""

    Protected Sub New(controller As RemoteAdminController)
        MyBase.New(controller)
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink
        Me.Padding = New Padding(0, 4, 0, 4)

        _flow = New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0)}
        _lblLast = NewLabel(Localization.T("Последнее известное состояние"))
        _lblLast.ForeColor = ThemeService.Palette.ColorOf(ThemeRole.TextMuted)
        _lblLast.Visible = False
        _flow.Controls.Add(_lblLast)

        _buttonRow = New FlowLayoutPanel With {.FlowDirection = FlowDirection.LeftToRight, .WrapContents = True, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 4, 0, 0), .MaximumSize = New Size(ContentWidth, 0)}
        _btnRefresh = MakeButton(Localization.T("Обновить"), "action.refresh", AddressOf OnRefreshClicked)
        _buttonRow.Controls.Add(_btnRefresh)
        _lblLine = NewLabel("")
        _lblLine.Margin = New Padding(4, 12, 0, 0)
        _lblLine.Visible = False
        _buttonRow.Controls.Add(_lblLine)
        _flow.Controls.Add(_buttonRow)
        Me.Controls.Add(_flow)
        _btnRefresh.Enabled = False
    End Sub

    ' ------------------------------------------------------------------------------------------------ the parts a card builds

    Protected Function NewLabel(text As String) As Label
        Return New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 2, 0, 2), .Text = text}
    End Function

    ''' <summary>A control of the card's content: it is shown while the card is live or still holds rows, hidden before that. A control
    ''' with <paramref name="ownVisibility"/> is placed in the card but shown or hidden by the card itself (a hint that depends on a choice).</summary>
    Protected Sub AddContent(c As Control, Optional ownVisibility As Boolean = False)
        If Not ownVisibility Then _content.Add(c)
        c.Visible = False
        _flow.Controls.Add(c)
        _flow.Controls.SetChildIndex(_buttonRow, _flow.Controls.Count - 1)
    End Sub

    Protected Function NewList(accessibleName As String, header As Boolean, columns As String(), widths As Integer()) As ListView
        Dim list As New ListView With {.View = View.Details, .FullRowSelect = True, .HideSelection = False, .MultiSelect = False,
            .Width = ContentWidth, .Height = 60, .Margin = New Padding(0, 2, 0, 2), .AccessibleName = accessibleName,
            .HeaderStyle = If(header, ColumnHeaderStyle.Nonclickable, ColumnHeaderStyle.None)}
        For i As Integer = 0 To columns.Length - 1
            list.Columns.Add(columns(i), Me.LogicalToDeviceUnits(widths(i)))
        Next
        _lists.Add(list)
        AddContent(list)
        FitList(list)
        Return list
    End Function

    ''' <summary>A button of the card's action row, enabled while the session is live and <paramref name="needs"/> (a selection, say) holds.</summary>
    Protected Function AddAction(text As String, glyph As String, needs As Func(Of Boolean), onClick As Action) As Button
        Dim b As Button = MakeButton(text, glyph, onClick)
        b.Enabled = False
        _actions.Add(b)
        If needs IsNot Nothing Then _needs(b) = needs
        _buttonRow.Controls.Add(b)
        _buttonRow.Controls.SetChildIndex(b, _actions.Count - 1)
        Return b
    End Function

    Private Function MakeButton(text As String, glyph As String, onClick As Action) As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 5, 12, 5),
            .Margin = New Padding(0, 2, 8, 2), .MinimumSize = New Size(0, 44), .Text = text, .AccessibleName = text.TrimEnd("."c),
            .UseVisualStyleBackColor = True}
        AddHandler b.Click, Sub(sender As Object, e As EventArgs) onClick()
        If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph)
        Return b
    End Function

    ''' <summary>The list is as high as its rows, one row at least and eight at most (a scroll bar beyond): never stretched past them.</summary>
    Protected Sub FitList(list As ListView)
        Dim rowHeight As Integer = list.Font.Height + Me.LogicalToDeviceUnits(6)
        Dim head As Integer = If(list.HeaderStyle = ColumnHeaderStyle.None, 0, rowHeight + Me.LogicalToDeviceUnits(2))
        Dim rows As Integer = Math.Min(MaxListRows, Math.Max(1, list.Items.Count))
        list.Height = head + rows * rowHeight + Me.LogicalToDeviceUnits(6)
    End Sub

    ' ------------------------------------------------------------------------------------------------ what a card implements

    ''' <summary>Reads and paints. Every request goes through <c>Controller.CallAsync</c>; a Nothing result paints nothing. Paint only
    ''' after <see cref="CanPaint"/> said so.</summary>
    Protected MustOverride Function ReadAsync(generation As Long) As Task

    ''' <summary>Empties every list and field of the card (the target changed, or there is nothing to keep).</summary>
    Protected MustOverride Sub ClearRows()

    ''' <summary>True when a reply may be painted: there is one, it is of the generation asked under, and it was done. A read the server
    ''' refused with the session alive puts its sentence beside the buttons and the list stays; a lapse is the controller's, and the card
    ''' only greys.</summary>
    Protected Function CanPaint(result As RemoteAdminResult, generation As Long) As Boolean
        If result Is Nothing OrElse IsDisposed Then Return False
        If Controller.State.Generation <> generation Then Return False
        If result.IsDone Then Return True
        If Controller.State.CardsLive Then
            SetNote(RemoteAdminWords.ForOutcome(result.Outcome))
        Else
            ApplyState()
        End If
        Return False
    End Function

    ''' <summary>The card now holds rows of this target: they stay, greyed, after a lapse.</summary>
    Protected Sub NoteRows()
        _hasRows = True
        UpdateAll()
    End Sub

    Protected Sub SetNote(text As String)
        _note = If(text, "")
        RefreshLine()
    End Sub

    Protected ReadOnly Property IsReading As Boolean
        Get
            Return _reading
        End Get
    End Property

    Protected Sub UpdateActionStates()
        Dim allowed As Boolean = Controller.State.CardsLive AndAlso Not _acting
        For Each b As Button In _actions
            Dim needs As Func(Of Boolean) = Nothing
            b.Enabled = allowed AndAlso (Not _needs.TryGetValue(b, needs) OrElse needs.Invoke())
        Next
        _btnRefresh.Enabled = Controller.State.CardsLive AndAlso Not _reading
    End Sub

    ''' <summary>The Refresh button was clicked: a card with a narrower read than "everything" widens it here.</summary>
    Protected Overridable Sub OnRefreshing()
    End Sub

    ' ------------------------------------------------------------------------------------------------ the state

    Friend Overrides Sub ApplyState()
        Dim state As RemoteAdminState = Controller.State
        If state.Generation <> _shownGeneration Then
            _shownGeneration = state.Generation
            ClearAll()
            _wasLive = False
        End If
        Dim live As Boolean = state.CardsLive
        If live AndAlso Not _wasLive Then _needsRead = True
        _wasLive = live
        If state.Phase = RemoteAdminPhase.NoTarget OrElse state.Phase = RemoteAdminPhase.Disabled Then ClearAll()
        UpdateAll()
        If live Then TryReadOnShow()
    End Sub

    Friend Overrides Sub ApplyThemeInks()
        _lblLast.ForeColor = ThemeService.Palette.ColorOf(ThemeRole.TextMuted)
        UpdateAll()
    End Sub

    Private Sub ClearAll()
        ClearRows()
        _hasRows = False
        _note = ""
        _again = False
        For Each list As ListView In _lists
            FitList(list)
        Next
    End Sub

    Private Sub UpdateAll()
        If IsDisposed Then Return
        Dim live As Boolean = Controller.State.CardsLive
        _greyed = Not live AndAlso _hasRows
        Dim show As Boolean = live OrElse _hasRows
        For Each c As Control In _content
            c.Visible = show
        Next
        _lblLast.Visible = _greyed
        Dim ink As Color = ThemeService.Palette.ColorOf(If(_greyed, ThemeRole.TextDisabled, ThemeRole.TextPrimary))
        For Each list As ListView In _lists
            list.ForeColor = ink
        Next
        UpdateActionStates()
        RefreshLine()
    End Sub

    Private Sub RefreshLine()
        If IsDisposed Then Return
        _lineText = If(Controller.State.CardsLive, _note, Localization.T("Войдите, чтобы увидеть это."))
        _lblLine.Text = _lineText
        _lblLine.Visible = _lineText.Length > 0
    End Sub

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        MyBase.OnSizeChanged(e)
        If Width <= 0 Then Return
        Dim full As Integer = Me.LogicalToDeviceUnits(ContentWidth)
        Dim wide As Integer = Math.Max(Me.LogicalToDeviceUnits(160), Math.Min(full, Width - Padding.Horizontal))
        For Each c As Control In _flow.Controls
            Dim label As Label = TryCast(c, Label)
            If label IsNot Nothing Then label.MaximumSize = New Size(wide, 0)
        Next
        _lblLine.MaximumSize = New Size(wide, 0)
        _buttonRow.MaximumSize = New Size(Math.Max(full, wide), 0)
    End Sub

    ' ------------------------------------------------------------------------------------------------ reads

    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        MyBase.OnVisibleChanged(e)
        If Visible Then TryReadOnShow()
    End Sub

    ''' <summary>The first show after a sign-in of this generation. A card that is not on screen reads nothing.</summary>
    Private Sub TryReadOnShow()
        If Not _needsRead OrElse _reading OrElse Not Controller.State.CardsLive Then Return
        If Not Visible OrElse Parent Is Nothing Then Return
        Dim dropped As Task = ReloadAsync()
    End Sub

    Private Sub OnRefreshClicked()
        OnRefreshing()
        Dim dropped As Task = ReloadAsync()
    End Sub

    ''' <summary>One read of the card; a second one while it runs is refused (a re-read after a change waits for it instead). Never throws.</summary>
    Friend Async Function ReloadAsync(Optional afterChange As Boolean = False) As Task
        If IsDisposed OrElse Not Controller.State.CardsLive Then Return
        If _reading Then
            If afterChange Then _again = True
            Return
        End If
        _reading = True
        _needsRead = False
        _note = ""
        Dim generation As Long = Controller.State.Generation
        UpdateAll()
        Try
            Await ReadAsync(generation)
        Catch ex As Exception
            AppFileLogger.LogStamped("remote-admin: card " & ex.GetType().Name)
        Finally
            _reading = False
            UpdateAll()
        End Try
        If IsDisposed Then Return
        If _again Then
            _again = False
            Await ReloadAsync(True)
        ElseIf _needsRead Then
            TryReadOnShow()
        End If
    End Function

    ''' <summary>A read tied to a click inside the card (a row chosen); it never throws and is not a second "whole card" read.</summary>
    Protected Sub FireRead(work As Func(Of Task))
        Dim dropped As Task = GuardedAsync(work)
    End Sub

    Private Shared Async Function GuardedAsync(work As Func(Of Task)) As Task
        Try
            Await work.Invoke()
        Catch ex As Exception
            AppFileLogger.LogStamped("remote-admin: card " & ex.GetType().Name)
        End Try
    End Function

    ' ------------------------------------------------------------------------------------------------ actions

    ''' <summary>The dialog or the confirmation of an action, then the re-read when the server changed, then its sentence.</summary>
    Protected Sub Fire(work As Func(Of Task(Of RemoteAdminActionResult)))
        Dim dropped As Task = RunActionAsync(work)
    End Sub

    Private Async Function RunActionAsync(work As Func(Of Task(Of RemoteAdminActionResult))) As Task
        If _acting OrElse IsDisposed OrElse Not Controller.State.CardsLive Then Return
        _acting = True
        UpdateActionStates()
        Try
            Dim result As RemoteAdminActionResult = Await work.Invoke()
            If result Is Nothing OrElse IsDisposed Then Return
            If result.Changed Then Await ReloadAsync(True)
            If Not IsDisposed AndAlso result.Line.Length > 0 Then SetNote(result.Line)
        Catch ex As Exception
            AppFileLogger.LogStamped("remote-admin: card " & ex.GetType().Name)
        Finally
            _acting = False
            If Not IsDisposed Then
                UpdateActionStates()
                RaiseActionFinished()
            End If
        End Try
    End Function

    ' ------------------------------------------------------------------------------------------------ what a test reads

    ''' <summary>The rows of the card's first list (a card without a list says what it shows instead).</summary>
    Friend Overridable ReadOnly Property RowCount As Integer
        Get
            Return If(_lists.Count = 0, 0, _lists(0).Items.Count)
        End Get
    End Property

    Friend ReadOnly Property ListAt(index As Integer) As ListView
        Get
            Return _lists(index)
        End Get
    End Property

    ''' <summary>True while the rows on screen are the last known ones of a session that is gone.</summary>
    Friend ReadOnly Property IsGreyed As Boolean
        Get
            Return _greyed
        End Get
    End Property

    ''' <summary>The caption over greyed rows, "" otherwise.</summary>
    Friend ReadOnly Property LastKnownCaption As String
        Get
            Return If(_greyed, _lblLast.Text, "")
        End Get
    End Property

    ''' <summary>The line beside the buttons: the empty-state line with no session, the sentence of a failed read or an action, or "".</summary>
    Friend ReadOnly Property StateLine As String
        Get
            Return _lineText
        End Get
    End Property

    ''' <summary>True when any action button (not Refresh) is enabled.</summary>
    Friend ReadOnly Property ActionsEnabled As Boolean
        Get
            Return _actions.Any(Function(b As Button) b.Enabled)
        End Get
    End Property

    Friend ReadOnly Property RefreshEnabled As Boolean
        Get
            Return _btnRefresh.Enabled
        End Get
    End Property

    ''' <summary>True when the card shows its content (live, or still holding rows); False shows the empty-state line alone.</summary>
    Friend ReadOnly Property ShowsContent As Boolean
        Get
            Return Controller.State.CardsLive OrElse _hasRows
        End Get
    End Property

    ''' <summary>The text of every cell of a row, tab-joined.</summary>
    Friend Function RowText(index As Integer) As String
        Dim item As ListViewItem = _lists(0).Items(index)
        Return String.Join(vbTab, item.SubItems.Cast(Of ListViewItem.ListViewSubItem)().Select(Function(s As ListViewItem.ListViewSubItem) s.Text))
    End Function

End Class
