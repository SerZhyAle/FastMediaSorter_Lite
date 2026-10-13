#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The "My devices" section of Resources (tickets 171 and 187): one GROUP PER EXCHANGE SERVER the viewer holds an entry for, and under each the
''' account's devices with a presence mark and their resources with a tick. A tick attaches (a resource of kind SFTP bound to the entry AND the
''' producer's id), an untick is the user removing it (with the panel's usual confirmation). Hosted INSIDE <see cref="Resource_List_Panel"/>,
''' so the Settings page and the manager dialog show the same section.
'''
''' Rules held here:
'''   * no section exists unless the exchange client features are offered; there is no other precondition - the viewer is a complete client and
'''     needs no Share Manager (rule 5): "Add from an exchange server.." is on the section itself;
'''   * each entry has its OWN session, held open (with `bye` after) only while this section is visible (rule 8); a server that does not answer
'''     says so in its own group and never hides the others;
'''   * the section never lists the device this program runs as (9 rule 1), and shows a kind it cannot use as "not supported by this version";
'''   * absence is not authority: nothing here deletes anything except the user's own untick, and signing out of an entry leaves every
'''     attached resource in place (9.3);
'''   * automatic attachment of new resources is a per-entry setting, off by default, worded as what it does (Q187-4);
'''   * drawn from the host's palette (the Settings window's dark and light schemes), right-to-left aware, scaled with the host.
''' </summary>
Friend NotInheritable Class Exchange_Devices_Panel
    Inherits UserControl

    Private Enum ItemKind
        ServerHeader
        Header
        Resource
        Placeholder
    End Enum

    Private NotInheritable Class ItemData
        Friend Kind As ItemKind
        Friend Source As ServerSource
        Friend Group As ExchangeDeviceGroup
        Friend Row As ExchangeDeviceRow
        Friend Record As ExchangeResourceRecord
    End Class

    ''' <summary>One entry's live pieces: its session, the feed over it, the sync that attaches through it, and the loop that runs while the
    ''' section is visible.</summary>
    Private NotInheritable Class ServerSource
        Friend Entry As ExchangeEntry
        Friend Session As ExchangeEntrySession
        Friend Feed As ExchangeDevicesFeed
        Friend Sync As ExchangeSync
        Friend Lease As IDisposable
        Friend Cts As CancellationTokenSource
        Friend LastFollowedRevision As Long = -1
        Friend Last As ExchangeDevicesState = ExchangeDevicesState.Hidden
        Friend Handler As EventHandler
        ''' <summary>The address the last `welcome` announced as different from the entry's own ("" when there is nothing to say) - ask 2 of
        ''' the entries proposal: shown, offered, never switched silently.</summary>
        Friend Moved As String = ""
    End Class

    Private ReadOnly _hub As ExchangeEntryHub
    Private ReadOnly _entries As ExchangeEntryStore
    Private ReadOnly _loadResources As Func(Of List(Of ResourceDescriptor))
    Private ReadOnly _confirmRemove As Func(Of ResourceDescriptor, Boolean)
    Private ReadOnly _removeResource As Action(Of ResourceDescriptor)
    Private ReadOnly _syncFactory As Func(Of ExchangeEntry, ExchangeEntrySession, ExchangeSync)
    Private ReadOnly _sources As New List(Of ServerSource)
    Private ReadOnly _titleLabel As Label
    Private ReadOnly _note As Label
    Private ReadOnly _addButton As Button
    Private ReadOnly _attachAllButton As Button
    Private ReadOnly _signOutButton As Button
    Private ReadOnly _signInButton As Button
    Private ReadOnly _updateAddressButton As Button
    Private ReadOnly _handOnButton As Button
    Private ReadOnly _autoBox As CheckBox
    Private ReadOnly _autoNote As Label
    Private ReadOnly _list As ListView
    Private _metrics As Func(Of Integer, Integer) = Function(n) n
    Private _palette As ResourcePanelPalette
    Private _running As Boolean
    Private _busy As Boolean
    Private _loadingAuto As Boolean
    ''' <summary>The Settings page's own group hosts the section (217): its caption replaces the internal
    ''' title, and the section stands at the height of its own content. The manager dialog never sets either.</summary>
    Private _groupHostedValue As Boolean
    Private _autoHeightValue As Boolean

    ''' <summary>An attach, an untick or a follow changed the registry: the host refills its list.</summary>
    Friend Event ResourcesChanged As EventHandler
    ''' <summary>One sentence for the host's status line (the outcome of a tick).</summary>
    Friend Event StatusSaid(text As String, ok As Boolean)
    ''' <summary>The directory moved the state of an attached resource (available, device offline, no longer published): the host's list redraws its rows.</summary>
    Friend Event StatesChanged As EventHandler

    Private _statesSignature As String = ""

    ''' <summary>The state of an attached resource against the LAST full directory of ITS entry; Unknown when there was none (so the row says
    ''' nothing - absence is not authority). An attached resource whose entry is gone says it needs signing in again.</summary>
    Friend Function StateOf(resource As ResourceDescriptor) As ExchangeResourceState
        If Not ResourceOrigin.IsExchange(resource) Then Return ExchangeResourceState.Unknown
        If String.IsNullOrEmpty(resource.ExchangeEntryId) Then Return ExchangeResourceState.Unknown
        Dim src As ServerSource = _sources.FirstOrDefault(Function(s) String.Equals(s.Entry.Id, resource.ExchangeEntryId, StringComparison.Ordinal))
        If src Is Nothing Then Return ExchangeResourceState.Unknown
        Return ExchangeAttach.StateOf(resource.ExchangeResourceId, src.Last.Snapshot, src.Last.FullList)
    End Function

    ''' <param name="syncFactory">A test's way to give an entry a sync that touches no registry.</param>
    Friend Sub New(hub As ExchangeEntryHub, entries As ExchangeEntryStore, loadResources As Func(Of List(Of ResourceDescriptor)),
                   confirmRemove As Func(Of ResourceDescriptor, Boolean), removeResource As Action(Of ResourceDescriptor),
                   Optional syncFactory As Func(Of ExchangeEntry, ExchangeEntrySession, ExchangeSync) = Nothing)
        _hub = hub
        _entries = entries
        _loadResources = loadResources
        _confirmRemove = confirmRemove
        _removeResource = removeResource
        _syncFactory = If(syncFactory, New Func(Of ExchangeEntry, ExchangeEntrySession, ExchangeSync)(AddressOf DefaultSync))
        Name = "res_Devices"
        Visible = False

        _titleLabel = New Label With {.Name = "res_DevicesTitle", .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft, .Text = ExchangeResourceText.SectionTitle()}
        _titleLabel.Font = New Font(Font, FontStyle.Bold)
        _note = New Label With {.Name = "res_DevicesNote", .AutoSize = False, .ForeColor = SystemColors.GrayText}
        _addButton = MakeButton("res_DevicesAdd", ExchangeEntryText.AddButton(), AddressOf OnAddClicked)
        _attachAllButton = MakeButton("res_DevicesAttachAll", ExchangeEntryText.AttachAllButton(), AddressOf OnAttachAllClicked)
        _signOutButton = MakeButton("res_DevicesSignOut", ExchangeEntryText.SignOutButton(), AddressOf OnSignOutClicked)
        _signInButton = MakeButton("res_DevicesSignIn", ExchangeEntryText.SignInAgainButton(), AddressOf OnSignInClicked)
        _updateAddressButton = MakeButton("res_DevicesUpdateAddress", ExchangeEntryText.UpdateAddressButton(), AddressOf OnUpdateAddressClicked)
        _handOnButton = MakeButton("res_DevicesHandOn", ExchangeEntryText.HandOnButton(), AddressOf OnHandOnClicked)
        ' WINDOWS-UI section 11: every command carries the glyph of its verb, mono (panel commands are never answers).
        ButtonGlyphs.Apply(_addButton, "action.add")
        ButtonGlyphs.Apply(_attachAllButton, "action.reconnect")
        ButtonGlyphs.Apply(_signOutButton, "nav.exit")
        ButtonGlyphs.Apply(_signInButton, "action.reconnect")
        ButtonGlyphs.Apply(_updateAddressButton, "action.refresh")
        ButtonGlyphs.Apply(_handOnButton, "action.copy")
        _autoBox = New CheckBox With {.Name = "res_DevicesAuto", .AutoSize = False, .Text = ExchangeEntryText.AutoAttachCheckbox(), .Visible = False}
        AddHandler _autoBox.CheckedChanged, AddressOf OnAutoChanged
        _autoNote = New Label With {.Name = "res_DevicesAutoNote", .AutoSize = False, .ForeColor = SystemColors.GrayText, .Text = ExchangeEntryText.AutoAttachNote(), .Visible = False}
        _list = New ListView With {
            .Name = "res_DevicesList", .View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False,
            .HeaderStyle = ColumnHeaderStyle.None, .OwnerDraw = True, .ShowItemToolTips = True, .AccessibleName = ExchangeResourceText.SectionTitle()}
        _list.Columns.Add("", 100)
        AddHandler _list.DrawItem, AddressOf OnDrawItem
        AddHandler _list.DrawSubItem, Sub(s, e) e.DrawDefault = False
        AddHandler _list.MouseDown, AddressOf OnListMouseDown
        AddHandler _list.KeyDown, AddressOf OnListKeyDown
        AddHandler _list.SelectedIndexChanged, Sub() UpdateButtons()
        AddHandler _list.Resize, Sub() FitColumn()
        Controls.Add(_list)
        Controls.Add(_autoNote)
        Controls.Add(_autoBox)
        Controls.Add(_signInButton)
        Controls.Add(_signOutButton)
        Controls.Add(_updateAddressButton)
        Controls.Add(_handOnButton)
        Controls.Add(_attachAllButton)
        Controls.Add(_addButton)
        Controls.Add(_note)
        Controls.Add(_titleLabel)
        AddHandler Resize, Sub() Relayout()
        AddHandler HandleCreated, Sub() Relayout() ' the row height turns from an estimate into the list's own
        AddHandler VisibleChanged, AddressOf OnSectionVisibleChanged
        RebuildSources()
    End Sub

    Private Shared Function DefaultSync(entry As ExchangeEntry, session As ExchangeEntrySession) As ExchangeSync
        Return ExchangeSync.CreateFor(entry, New ExchangeEntryApi(session))
    End Function

    Private Function MakeButton(buttonName As String, text As String, onClick As EventHandler) As Button
        Dim b As New Button With {.Name = buttonName, .Text = text, .Visible = False}
        AddHandler b.Click, onClick
        Return b
    End Function

    ''' <summary>The host's pixel metrics: 96-dpi logical units to device units (its scale and the monitor's DPI).</summary>
    Friend Sub SetMetrics(metrics As Func(Of Integer, Integer))
        _metrics = If(metrics, Function(n) n)
        _list.Font = Font
        Relayout()
    End Sub

    Private Function Dv(n As Integer) As Integer
        Return _metrics.Invoke(n)
    End Function

    ''' <summary>The host's palette on every part of the section (the Settings window's scheme). Nothing keeps the machine's colours once it is set.</summary>
    Friend Sub ApplyPalette(theme As ResourcePanelPalette)
        _palette = theme
        BackColor = theme.Surface
        _titleLabel.ForeColor = theme.Text
        _note.ForeColor = theme.Muted
        _autoNote.ForeColor = theme.Muted
        _autoBox.ForeColor = theme.Text
        _list.BackColor = theme.EditorBack
        _list.ForeColor = theme.EditorText
        _list.BorderStyle = If(theme.Dark, BorderStyle.None, BorderStyle.Fixed3D)
        For Each b As Button In New Button() {_addButton, _attachAllButton, _signOutButton, _signInButton, _updateAddressButton, _handOnButton}
            b.FlatStyle = FlatStyle.Flat
            b.BackColor = theme.Surface
            b.ForeColor = theme.Text
            b.FlatAppearance.BorderColor = theme.Border
        Next
        _list.Invalidate()
    End Sub

    ''' <summary>Re-reads every caption after a language switch (the panel outlives it inside the reused Settings window).</summary>
    Friend Sub Localize()
        RightToLeft = If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No)
        _titleLabel.Text = ExchangeResourceText.SectionTitle()
        _addButton.Text = ExchangeEntryText.AddButton()
        _attachAllButton.Text = ExchangeEntryText.AttachAllButton()
        _signOutButton.Text = ExchangeEntryText.SignOutButton()
        _signInButton.Text = ExchangeEntryText.SignInAgainButton()
        _updateAddressButton.Text = ExchangeEntryText.UpdateAddressButton()
        _handOnButton.Text = ExchangeEntryText.HandOnButton()
        _autoBox.Text = ExchangeEntryText.AutoAttachCheckbox()
        _autoNote.Text = ExchangeEntryText.AutoAttachNote()
        _list.AccessibleName = ExchangeResourceText.SectionTitle()
        Render()
    End Sub

    ''' <summary>Whether this section may exist at all: the client features are offered. Nothing else is needed (rule 5).</summary>
    Friend Function IsOffered() As Boolean
        Return ExchangeFeatures.IsEnabled()
    End Function

    ''' <summary>The height the section wants beside the host's list (0 when it is not offered).</summary>
    Friend Function WantedHeight(bodyHeight As Integer) As Integer
        If Not IsOffered() Then Return 0
        Return Math.Max(Dv(180), CInt(bodyHeight * 0.46))
    End Function

    ' ---- the entries -------------------------------------------------------------------------------------------

    ''' <summary>Brings the sources in line with the stored entries: a new entry gets a session and a feed, a removed one is stopped and
    ''' forgotten, a changed one (another pin or address) starts afresh. Called when the section is built and after every change.</summary>
    Friend Sub RebuildSources()
        If IsDisposed Then Return
        Dim stored As List(Of ExchangeEntry) = If(IsOffered(), _entries.Load(), New List(Of ExchangeEntry)())
        For i As Integer = _sources.Count - 1 To 0 Step -1
            Dim src As ServerSource = _sources(i)
            Dim keep As ExchangeEntry = stored.FirstOrDefault(Function(e) String.Equals(e.Id, src.Entry.Id, StringComparison.Ordinal))
            ' Signing in again forgets the hub's session even when the address and pin stay the same.
            ' The feed must follow the replacement instead of retaining the disposed session.
            If keep Is Nothing OrElse Not String.Equals(keep.Server, src.Entry.Server, StringComparison.Ordinal) OrElse
               Not String.Equals(keep.Pin, src.Entry.Pin, StringComparison.Ordinal) OrElse
               Not Object.ReferenceEquals(src.Session, _hub.SessionFor(keep)) Then
                StopSource(src)
                RemoveHandler src.Feed.Changed, src.Handler
                _sources.RemoveAt(i)
            Else
                src.Entry = keep
            End If
        Next
        For Each e As ExchangeEntry In stored
            Dim have As ExchangeEntry = e
            If _sources.Any(Function(s) String.Equals(s.Entry.Id, have.Id, StringComparison.Ordinal)) Then Continue For
            Dim session As ExchangeEntrySession = _hub.SessionFor(e)
            Dim src As New ServerSource With {.Entry = e, .Session = session}
            src.Feed = New ExchangeDevicesFeed(New ExchangeEntryApi(session), AddressOf ExchangeFeatures.IsEnabled, Function() True)
            src.Sync = _syncFactory.Invoke(e, session)
            src.Handler = AddressOf OnFeedChanged
            AddHandler src.Feed.Changed, src.Handler
            _sources.Add(src)
            If _running Then StartSource(src)
        Next
        If Not IsDisposed Then Render()
    End Sub

    ' ---- life ----------------------------------------------------------------------------------------------------

    Private Sub OnSectionVisibleChanged(sender As Object, e As EventArgs)
        If Visible AndAlso IsOffered() Then StartLoop() Else StopLoop()
    End Sub

    Private Sub StartLoop()
        If _running Then Return
        _running = True
        RebuildSources()
        For Each src As ServerSource In _sources.ToList()
            StartSource(src)
        Next
        Render()
    End Sub

    Private Sub StopLoop()
        _running = False
        For Each src As ServerSource In _sources.ToList()
            StopSource(src)
        Next
    End Sub

    Private Sub StartSource(src As ServerSource)
        If src.Cts IsNot Nothing Then Return
        ' the session opens with this lease and closes - `bye` - when it ends (rule 8)
        src.Lease = src.Session.Lease()
        src.Cts = New CancellationTokenSource()
        Dim token As CancellationToken = src.Cts.Token
        Dim feed As ExchangeDevicesFeed = src.Feed
        ' runs until the section is hidden or disposed; the loop owns its failures (it never throws)
        Dim unused As Task = Task.Run(Function() feed.RunAsync(token))
    End Sub

    Private Sub StopSource(src As ServerSource)
        Dim c As CancellationTokenSource = src.Cts
        src.Cts = Nothing
        If c IsNot Nothing Then
            Try : c.Cancel() : Catch : End Try
            Try : c.Dispose() : Catch : End Try
        End If
        Dim l As IDisposable = src.Lease
        src.Lease = Nothing
        If l IsNot Nothing Then l.Dispose()
    End Sub

    Private Sub OnFeedChanged(sender As Object, e As EventArgs)
        Try
            If IsDisposed OrElse Not IsHandleCreated Then Return
            BeginInvoke(New Action(AddressOf Render))
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ObjectDisposedException
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            StopLoop()
            For Each src As ServerSource In _sources
                RemoveHandler src.Feed.Changed, src.Handler
            Next
            _sources.Clear()
            Try : _titleLabel.Font?.Dispose() : Catch : End Try
        End If
        MyBase.Dispose(disposing)
    End Sub

    ' ---- layout ----------------------------------------------------------------------------------------------------

    ''' <summary>The Settings page hosts the section as its own collapsible group (217): the
    ''' group's caption replaces the section's internal title, and the section stands at the
    ''' height of its own content - the rows it is showing, not a share of the list panel's
    ''' body. The group's fold, through the section's Visible flag, keeps the entries' loop
    ''' alive only while the section is really on screen (rule 8). The manager dialog never
    ''' calls this and keeps the embedded look of tickets 171 and 187.</summary>
    Friend Sub HostInCollapsibleGroup()
        _groupHostedValue = True
        _autoHeightValue = True
        _titleLabel.Visible = False
        Relayout()
    End Sub

    ''' <summary>The height one row of the section's list really takes: asked from the list
    ''' once its handle exists, estimated from the font before that (the section's rows are
    ''' font-height rows, no fixed image list sizes them).</summary>
    Private Function MeasuredRowHeight() As Integer
        If _list.IsHandleCreated AndAlso _list.Items.Count > 0 Then
            Try
                Return _list.Items(0).Bounds.Height
            Catch
            End Try
        End If
        Return Math.Max(Dv(26), TextRenderer.MeasureText("Ag", _list.Font).Height + Dv(8))
    End Function

    Private Sub Relayout()
        Dim w As Integer = Math.Max(Dv(120), ClientSize.Width)
        Dim y As Integer = 0
        If Not _groupHostedValue Then
            _titleLabel.SetBounds(0, y, w, Dv(24))
            y += Dv(24)
        End If
        Dim noteHeight As Integer = If(_note.Text.Length = 0, 0, TextRenderer.MeasureText(_note.Text, _note.Font, New Size(w, Integer.MaxValue), TextFormatFlags.WordBreak).Height + Dv(4))
        _note.SetBounds(0, y, w, noteHeight)
        y += noteHeight

        ' the buttons flow in one wrapped row; the selection decides which of the per-server ones show
        Dim rtl As Boolean = RightToLeft = RightToLeft.Yes
        Dim x As Integer = 0
        Dim rowTop As Integer = y + Dv(2)
        Dim gap As Integer = Dv(6)
        For Each b As Button In New Button() {_addButton, _attachAllButton, _signInButton, _signOutButton, _updateAddressButton, _handOnButton}
            If Not b.Visible Then Continue For
            ' The caption, its padding and the verb's glyph with its gap (WINDOWS-UI section 11 item 3).
            Dim glyphWidth As Integer = Math.Max(Dv(ButtonGlyphs.GlyphLogicalSize + ButtonGlyphs.GapLogicalSize), ButtonGlyphs.ExtraWidth(b))
            Dim bw As Integer = Math.Max(Dv(90), TextRenderer.MeasureText(b.Text, b.Font).Width + Dv(28) + glyphWidth)
            If x > 0 AndAlso x + bw > w Then
                x = 0
                rowTop += Dv(34)
            End If
            b.SetBounds(If(rtl, w - x - bw, x), rowTop, bw, Dv(30))
            x += bw + gap
        Next
        y = rowTop + Dv(34)
        If _autoBox.Visible Then
            _autoBox.SetBounds(0, y, w, Dv(22))
            y += Dv(22)
            Dim nh As Integer = TextRenderer.MeasureText(_autoNote.Text, _autoNote.Font, New Size(w, Integer.MaxValue), TextFormatFlags.WordBreak).Height + Dv(4)
            _autoNote.SetBounds(0, y, w, nh)
            y += nh
        End If
        Dim listHeight As Integer = Math.Max(0, ClientSize.Height - y)
        If _autoHeightValue Then
            listHeight = If(_list.Visible, _list.Items.Count * MeasuredRowHeight() + Dv(8), 0)
            Dim wantedHeight As Integer = y + listHeight + Dv(6)
            If Height <> wantedHeight Then
                Height = wantedHeight
                Return ' the Resize this triggers lays the list out at the settled height
            End If
        End If
        _list.SetBounds(0, y, w, listHeight)
        FitColumn()
    End Sub

    Private Sub FitColumn()
        If _list.Columns.Count > 0 Then _list.Columns(0).Width = Math.Max(Dv(100), _list.ClientSize.Width)
    End Sub

    Private Function RowHeight() As Integer
        Return Dv(26)
    End Function

    Private Function CheckBoxRect(bounds As Rectangle) As Rectangle
        Dim size As Integer = Dv(14)
        Dim x As Integer = If(RightToLeft = RightToLeft.Yes, bounds.Right - Dv(22) - size, bounds.Left + Dv(22))
        Return New Rectangle(x, bounds.Top + (bounds.Height - size) \ 2, size, size)
    End Function

    ' ---- drawing the list --------------------------------------------------------------------------------------

    Private Sub OnDrawItem(sender As Object, e As DrawListViewItemEventArgs)
        Dim data As ItemData = TryCast(e.Item.Tag, ItemData)
        Dim p As ResourcePanelPalette = _palette
        Dim back As Color = If(p IsNot Nothing, p.EditorBack, SystemColors.Window)
        Dim fore As Color = If(p IsNot Nothing, p.EditorText, SystemColors.WindowText)
        Dim muted As Color = If(p IsNot Nothing, p.Muted, SystemColors.GrayText)
        Dim accent As Color = If(p IsNot Nothing, p.Accent, SystemColors.Highlight)
        If e.Item.Selected AndAlso data IsNot Nothing AndAlso (data.Kind = ItemKind.Resource OrElse data.Kind = ItemKind.ServerHeader) Then
            back = Color.FromArgb(255, (CInt(back.R) + CInt(accent.R)) \ 2, (CInt(back.G) + CInt(accent.G)) \ 2, (CInt(back.B) + CInt(accent.B)) \ 2)
        End If
        Using fill As New SolidBrush(back)
            e.Graphics.FillRectangle(fill, e.Bounds)
        End Using
        If data Is Nothing Then Return
        Dim rtl As Boolean = RightToLeft = RightToLeft.Yes
        Dim flags As TextFormatFlags = TextFormatFlags.SingleLine Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPadding Or TextFormatFlags.NoPrefix
        If rtl Then flags = flags Or TextFormatFlags.RightToLeft
        Dim textBounds As Rectangle = e.Bounds
        textBounds.Inflate(-Dv(6), 0)

        Select Case data.Kind
            Case ItemKind.ServerHeader
                Using bold As New Font(_list.Font, FontStyle.Bold)
                    TextRenderer.DrawText(e.Graphics, e.Item.Text, bold, textBounds, fore, flags)
                End Using
            Case ItemKind.Header
                textBounds.X += If(rtl, 0, Dv(10))
                textBounds.Width -= Dv(10)
                TextRenderer.DrawText(e.Graphics, e.Item.Text, _list.Font, textBounds, If(data.Group.Online, fore, muted), flags)
            Case ItemKind.Placeholder
                textBounds.X += If(rtl, 0, Dv(22))
                textBounds.Width -= Dv(22)
                TextRenderer.DrawText(e.Graphics, e.Item.Text, _list.Font, textBounds, muted, flags)
            Case Else
                Dim box As Rectangle = CheckBoxRect(e.Bounds)
                Using pen As New Pen(If(data.Row.Supported AndAlso (data.Row.Attached OrElse data.Row.DeviceOnline), fore, muted))
                    e.Graphics.DrawRectangle(pen, box.X, box.Y, box.Width - 1, box.Height - 1)
                End Using
                If data.Row.Attached Then
                    Using pen As New Pen(accent, Math.Max(2.0F, Dv(2)))
                        e.Graphics.DrawLines(pen, New Point() {New Point(box.Left + Dv(3), box.Top + box.Height \ 2),
                                                               New Point(box.Left + box.Width \ 2 - 1, box.Bottom - Dv(4)),
                                                               New Point(box.Right - Dv(3), box.Top + Dv(3))})
                    End Using
                End If
                Dim state As String = ExchangeResourceText.RowState(data.Row)
                Dim stateWidth As Integer = If(state.Length = 0, 0, Math.Min(textBounds.Width \ 2, TextRenderer.MeasureText(state, _list.Font).Width + Dv(8)))
                Dim nameBounds As Rectangle = textBounds
                nameBounds.X += If(rtl, 0, Dv(44))
                nameBounds.Width = Math.Max(0, nameBounds.Width - Dv(44) - stateWidth)
                TextRenderer.DrawText(e.Graphics, data.Row.Name, _list.Font, nameBounds, fore, flags)
                If stateWidth > 0 Then
                    Dim stateBounds As New Rectangle(If(rtl, textBounds.Left, textBounds.Right - stateWidth), textBounds.Top, stateWidth, textBounds.Height)
                    TextRenderer.DrawText(e.Graphics, state, _list.Font, stateBounds, muted, flags Or If(rtl, TextFormatFlags.Left, TextFormatFlags.Right))
                End If
        End Select
    End Sub

    ' ---- what is on screen -----------------------------------------------------------------------------------

    Private Function StateOfSource(src As ServerSource) As ExchangeAgentState
        Dim s As ExchangeDevicesState = src.Feed.Current
        If s.Availability = ExchangeDevicesAvailability.Ready Then Return ExchangeAgentState.Online
        If s.Agent IsNot Nothing Then Return s.Agent.State
        Return ExchangeAgentState.Stopped
    End Function

    Private Sub Render()
        If IsDisposed Then Return
        Dim all As List(Of ResourceDescriptor) = SafeResources()
        Dim anyRows As Boolean = False
        _list.BeginUpdate()
        Try
            Dim keptKey As String = SelectedKey()
            _list.Items.Clear()
            For Each src As ServerSource In _sources
                Dim state As ExchangeDevicesState = src.Feed.Current
                src.Last = state
                Dim agentState As ExchangeAgentState = StateOfSource(src)
                src.Moved = ExchangeEntryMove.Announced(src.Entry.Server, src.Session.PublicEndpoint)
                _list.Items.Add(New ListViewItem(ExchangeEntryText.EntryHeader(src.Entry.DisplayName, src.Entry.Login, ExchangeEntryText.StateText(agentState))) With {
                    .Tag = New ItemData With {.Kind = ItemKind.ServerHeader, .Source = src}, .ToolTipText = src.Entry.Server})
                If src.Moved.Length > 0 Then
                    ' ask 2: the announced address is SHOWN here; the edit below it is offered, never taken silently
                    _list.Items.Add(New ListViewItem(ExchangeEntryText.AnnouncedAddress(src.Moved)) With {
                        .Tag = New ItemData With {.Kind = ItemKind.Placeholder, .Source = src}})
                End If
                If state.Availability = ExchangeDevicesAvailability.Ready AndAlso state.Snapshot IsNot Nothing Then
                    Dim mine As List(Of ResourceDescriptor) = all.Where(Function(r) String.Equals(If(r.ExchangeEntryId, ""), src.Entry.Id, StringComparison.Ordinal)).ToList()
                    Dim groups As List(Of ExchangeDeviceGroup) = ExchangeDevicesModel.Build(state.Snapshot, mine, state.OwnDeviceId)
                    Dim hasRows As Boolean = groups.Any(Function(g) g.Rows.Count > 0)
                    anyRows = anyRows OrElse hasRows
                    If groups.Count = 0 Then
                        _list.Items.Add(New ListViewItem(ExchangeResourceText.Availability(state, False)) With {
                            .Tag = New ItemData With {.Kind = ItemKind.Placeholder, .Source = src}})
                    End If
                    For Each g As ExchangeDeviceGroup In groups
                        _list.Items.Add(New ListViewItem(ExchangeResourceText.DeviceHeader(g.Name, g.Online)) With {
                            .Tag = New ItemData With {.Kind = ItemKind.Header, .Source = src, .Group = g}})
                        If g.Rows.Count = 0 Then
                            _list.Items.Add(New ListViewItem(ExchangeResourceText.NoFolders()) With {
                                .Tag = New ItemData With {.Kind = ItemKind.Placeholder, .Source = src, .Group = g}})
                        End If
                        For Each r As ExchangeDeviceRow In g.Rows
                            Dim rid As String = r.ResourceId
                            Dim record As ExchangeResourceRecord = state.Snapshot.Resources.FirstOrDefault(Function(x) String.Equals(x.ResourceId, rid, StringComparison.Ordinal))
                            Dim item As New ListViewItem(r.Name) With {
                                .Tag = New ItemData With {.Kind = ItemKind.Resource, .Source = src, .Group = g, .Row = r, .Record = record},
                                .ToolTipText = r.Name & If(ExchangeResourceText.RowState(r).Length > 0, " - " & ExchangeResourceText.RowState(r), "")}
                            _list.Items.Add(item)
                        Next
                    Next
                    If hasRows = False AndAlso groups.Count > 0 AndAlso groups.All(Function(g) g.Rows.Count = 0) Then
                        ' devices with no folders yet: each device already says so
                    End If
                Else
                    Dim reason As String = ExchangeResourceText.Availability(state, False)
                    If agentState = ExchangeAgentState.Online OrElse agentState = ExchangeAgentState.Connecting OrElse agentState = ExchangeAgentState.Reconnecting Then reason = ""
                    If reason.Length > 0 Then
                        _list.Items.Add(New ListViewItem(reason) With {.Tag = New ItemData With {.Kind = ItemKind.Placeholder, .Source = src}})
                    End If
                End If
            Next
            RestoreSelection(keptKey)
        Finally
            _list.EndUpdate()
        End Try
        _note.Text = If(_sources.Count = 0, ExchangeEntryText.NoServerYet(), "")
        _note.Visible = _note.Text.Length > 0
        _list.Visible = _sources.Count > 0
        UpdateButtons()
        Relayout()
        For Each src As ServerSource In _sources.ToList()
            FollowWhenNew(src)
        Next
        ' the host's rows say what their producers are now; they are redrawn only when one of those states moved
        Dim states As New Dictionary(Of String, ExchangeResourceState)(StringComparer.Ordinal)
        For Each r As ResourceDescriptor In all
            If ResourceOrigin.IsExchange(r) Then states(r.Id) = StateOf(r)
        Next
        Dim signature As String = String.Join("|", states.OrderBy(Function(kv) kv.Key, StringComparer.Ordinal).Select(Function(kv) kv.Key & "=" & CInt(kv.Value).ToString()))
        If Not String.Equals(signature, _statesSignature, StringComparison.Ordinal) Then
            _statesSignature = signature
            RaiseEvent StatesChanged(Me, EventArgs.Empty)
        End If
    End Sub

    Private Function SafeResources() As List(Of ResourceDescriptor)
        Try
            Return _loadResources.Invoke()
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
            Return New List(Of ResourceDescriptor)()
        End Try
    End Function

    Private Function SelectedKey() As String
        If _list.SelectedItems.Count = 0 Then Return ""
        Dim data As ItemData = TryCast(_list.SelectedItems(0).Tag, ItemData)
        If data Is Nothing Then Return ""
        Return If(data.Source Is Nothing, "", data.Source.Entry.Id) & "|" & If(data.Row Is Nothing, "", data.Row.ResourceId)
    End Function

    Private Sub RestoreSelection(key As String)
        If key.Length = 0 Then Return
        For Each item As ListViewItem In _list.Items
            Dim data As ItemData = TryCast(item.Tag, ItemData)
            If data Is Nothing OrElse data.Source Is Nothing Then Continue For
            Dim k As String = data.Source.Entry.Id & "|" & If(data.Row Is Nothing, "", data.Row.ResourceId)
            If String.Equals(k, key, StringComparison.Ordinal) Then
                item.Selected = True
                Return
            End If
        Next
    End Sub

    ''' <summary>The server the buttons act on: the selected row's, or the only one there is.</summary>
    Private Function CurrentSource() As ServerSource
        If _list.SelectedItems.Count > 0 Then
            Dim data As ItemData = TryCast(_list.SelectedItems(0).Tag, ItemData)
            If data IsNot Nothing AndAlso data.Source IsNot Nothing Then Return data.Source
        End If
        Return If(_sources.Count = 1, _sources(0), Nothing)
    End Function

    Private Sub UpdateButtons()
        Dim offered As Boolean = IsOffered()
        _addButton.Visible = offered
        Dim src As ServerSource = CurrentSource()
        Dim state As ExchangeAgentState = If(src Is Nothing, ExchangeAgentState.NotConfigured, StateOfSource(src))
        _attachAllButton.Visible = src IsNot Nothing AndAlso state = ExchangeAgentState.Online
        _signOutButton.Visible = src IsNot Nothing
        Dim needsPinEnrollment As Boolean = src IsNot Nothing AndAlso src.Feed.Current.Snapshot IsNot Nothing AndAlso
            src.Feed.Current.Snapshot.Resources.Any(Function(r) ExchangeAttach.NeedsPinEnrollment(r))
        _signInButton.Visible = src IsNot Nothing AndAlso (state = ExchangeAgentState.SignInAgain OrElse state = ExchangeAgentState.PinMismatch OrElse needsPinEnrollment)
        _updateAddressButton.Visible = src IsNot Nothing AndAlso src.Moved.Length > 0
        _handOnButton.Visible = src IsNot Nothing AndAlso state = ExchangeAgentState.Online
        _autoBox.Visible = src IsNot Nothing
        _autoNote.Visible = src IsNot Nothing
        If src IsNot Nothing Then
            _loadingAuto = True
            Try
                _autoBox.Checked = src.Entry.AutoAttach
            Finally
                _loadingAuto = False
            End Try
        End If
        _addButton.Enabled = Not _busy
        _attachAllButton.Enabled = Not _busy
        _updateAddressButton.Enabled = Not _busy
        _handOnButton.Enabled = Not _busy
        Relayout()
    End Sub

    ''' <summary>A new full directory is the moment to follow the producers of the attached resources (rule 4) and, when the entry asks for
    ''' it, to attach what is new - once per revision.</summary>
    Private Sub FollowWhenNew(src As ServerSource)
        Dim state As ExchangeDevicesState = src.Feed.Current
        If state.Snapshot Is Nothing OrElse state.Snapshot.Revision = src.LastFollowedRevision Then Return
        src.LastFollowedRevision = state.Snapshot.Revision
        Dim snapshot As ExchangeDevicesState = state
        Dim token As CancellationToken = If(src.Cts IsNot Nothing, src.Cts.Token, CancellationToken.None)
        Dim entry As ExchangeEntry = src.Entry
        Dim sync As ExchangeSync = src.Sync
        Dim t As Task = Task.Run(
            Async Function()
                Dim changed As List(Of String) = Await sync.FollowAllAsync(snapshot, token).ConfigureAwait(False)
                Dim attached As Integer = 0
                If entry.AutoAttach Then
                    ' what is NEW is what this entry has not listed before; the first look after the setting was turned on only records
                    Dim ids As List(Of String) = snapshot.Snapshot.Resources.Select(Function(r) r.ResourceId).ToList()
                    Dim fresh As New HashSet(Of String)(ids.Where(Function(id) Not entry.Seen.Contains(id)), StringComparer.Ordinal)
                    If fresh.Count > 0 AndAlso entry.Seen.Count > 0 Then attached = Await sync.AttachListedAsync(snapshot, fresh, token).ConfigureAwait(False)
                    _entries.Upsert(entry.WithSeen(ids))
                End If
                If (changed.Count > 0 OrElse attached > 0) AndAlso Not IsDisposed Then
                    Try
                        BeginInvoke(New Action(Sub()
                                                   If attached > 0 Then RaiseEvent StatusSaid(ExchangeEntryText.AttachedCount(attached), True)
                                                   RaiseEvent ResourcesChanged(Me, EventArgs.Empty)
                                               End Sub))
                    Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ObjectDisposedException
                    End Try
                End If
            End Function)
    End Sub

    ' ---- the ticks ---------------------------------------------------------------------------------------------

    Private Sub OnListMouseDown(sender As Object, e As MouseEventArgs)
        If e.Button <> MouseButtons.Left Then Return
        Dim item As ListViewItem = _list.GetItemAt(e.X, e.Y)
        If item Is Nothing Then Return
        Dim data As ItemData = TryCast(item.Tag, ItemData)
        If data Is Nothing OrElse data.Kind <> ItemKind.Resource Then Return
        ' the box and the text both count: a tick is the point of the row
        Toggle(data)
    End Sub

    Private Sub OnListKeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode <> Keys.Space OrElse _list.SelectedItems.Count = 0 Then Return
        Dim data As ItemData = TryCast(_list.SelectedItems(0).Tag, ItemData)
        If data IsNot Nothing AndAlso data.Kind = ItemKind.Resource Then
            e.Handled = True
            Toggle(data)
        End If
    End Sub

    Private Async Sub Toggle(data As ItemData)
        If _busy OrElse data.Row Is Nothing OrElse data.Source Is Nothing Then Return
        Dim src As ServerSource = data.Source
        If data.Row.Attached Then
            ' an untick is the user's act: the panel's own removal, with its own confirmation
            Dim resource As ResourceDescriptor = ResourceOrigin.FindByExchangeId(SafeResources(), data.Row.ResourceId, src.Entry.Id)
            If resource Is Nothing OrElse Not _confirmRemove.Invoke(resource) Then Return
            _removeResource.Invoke(resource)
            RaiseEvent ResourcesChanged(Me, EventArgs.Empty)
            Render()
            Return
        End If
        If Not data.Row.Supported Then
            RaiseEvent StatusSaid(ExchangeResourceText.AttachFailure(ExchangeAttachOutcome.Unusable), False)
            Return
        End If
        If Not data.Row.DeviceOnline Then
            RaiseEvent StatusSaid(ExchangeResourceText.AttachFailure(ExchangeAttachOutcome.NoAccess), False)
            Return
        End If
        If ExchangeAttach.NeedsPinEnrollment(data.Record) Then
            RaiseEvent StatusSaid(ExchangeResourceText.AttachFailure(ExchangeAttachOutcome.SignInAgain), False)
            UpdateButtons()
            Return
        End If
        _busy = True
        Try
            Dim token As CancellationToken = If(src.Cts IsNot Nothing, src.Cts.Token, CancellationToken.None)
            Dim pin As String = Nothing
            If data.Record.IsProtected Then
                pin = ResourcePinMemory.GetPin(New ResourceDescriptor With {.ExchangeEntryId = src.Entry.Id, .ExchangeResourceId = data.Row.ResourceId})
                If pin Is Nothing Then pin = ResourcePinPromptUi.Ask(FindForm(), data.Row.Name)
                If pin Is Nothing Then Return
            End If
            Dim failure As ResourceFailure = Nothing
            Dim outcome As ExchangeAttachOutcome = Await src.Sync.AttachAsync(data.Record, data.Group.Name, token, pin, Sub(f) failure = f)
            If outcome = ExchangeAttachOutcome.Attached Then
                RaiseEvent StatusSaid(ExchangeResourceText.Attached(data.Row.Name), True)
                RaiseEvent ResourcesChanged(Me, EventArgs.Empty)
            Else
                RaiseEvent StatusSaid(If(outcome = ExchangeAttachOutcome.ConnectionFailed, ResourceMessages.ForFailure(failure), ExchangeResourceText.AttachFailure(outcome)), False)
            End If
        Catch ex As OperationCanceledException
        Finally
            _busy = False
        End Try
        If Not IsDisposed Then Render()
    End Sub

    ' ---- the buttons -----------------------------------------------------------------------------------------

    Private Sub OnAddClicked(sender As Object, e As EventArgs)
        If _busy Then Return
        Dim added As ExchangeEntry = ExchangeConnectFlowUi.Run(FindForm(), "", Nothing)
        If added IsNot Nothing Then EntryAdded(added)
    End Sub

    ''' <summary>The host or the dialog added or re-signed an entry: the sources follow, and the section says so.</summary>
    Friend Sub EntryAdded(entry As ExchangeEntry)
        _hub.Forget(entry.Id)
        RebuildSources()
        RaiseEvent StatusSaid(ExchangeEntryText.Added(entry.DisplayName), True)
        RaiseEvent ResourcesChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub OnSignInClicked(sender As Object, e As EventArgs)
        Dim src As ServerSource = CurrentSource()
        If src Is Nothing OrElse _busy Then Return
        Dim added As ExchangeEntry = ExchangeConnectFlowUi.Run(FindForm(), "", src.Entry)
        If added IsNot Nothing Then EntryAdded(added)
    End Sub

    Private Async Sub OnAttachAllClicked(sender As Object, e As EventArgs)
        Dim src As ServerSource = CurrentSource()
        If src Is Nothing OrElse _busy Then Return
        _busy = True
        UpdateButtons()
        Try
            Dim token As CancellationToken = If(src.Cts IsNot Nothing, src.Cts.Token, CancellationToken.None)
            Dim count As Integer = Await src.Sync.AttachListedAsync(src.Feed.Current, Nothing, token)
            RaiseEvent StatusSaid(ExchangeEntryText.AttachedCount(count), count > 0)
            If count > 0 Then RaiseEvent ResourcesChanged(Me, EventArgs.Empty)
        Catch ex As OperationCanceledException
        Finally
            _busy = False
        End Try
        If Not IsDisposed Then
            UpdateButtons()
            Render()
        End If
    End Sub

    ''' <summary>"Sign out" (rule 6): the entry, its token and its pin go; an attached resource is NOT deleted - it says "sign in again".</summary>
    Private Sub OnSignOutClicked(sender As Object, e As EventArgs)
        Dim src As ServerSource = CurrentSource()
        If src Is Nothing OrElse _busy Then Return
        Dim owner As IWin32Window = If(FindForm(), DirectCast(Me, IWin32Window))
        If ViewerBox.Show(owner, ExchangeEntryText.SignOutQuestion(src.Entry.DisplayName, src.Entry.Login), ExchangeResourceText.SectionTitle(),
                          MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Dim name As String = src.Entry.DisplayName
        StopSource(src)
        _hub.Forget(src.Entry.Id)
        ResourcePinMemory.ForgetEntry(src.Entry.Id)
        _entries.Remove(src.Entry.Id)
        RebuildSources()
        RaiseEvent StatusSaid(ExchangeEntryText.SignedOut(name), True)
        RaiseEvent ResourcesChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Ask 2 of the entries proposal: the welcome announced another address and the user takes it. The edit is in place - the
    ''' device id, the token, the pin decision and every attached folder are kept (ExchangeEntry.WithAddress); a different leaf at the new
    ''' address is item D's explicit replacement (the `pin-mismatch` state, never a re-pin).</summary>
    Private Sub OnUpdateAddressClicked(sender As Object, e As EventArgs)
        Dim src As ServerSource = CurrentSource()
        If src Is Nothing OrElse _busy OrElse src.Moved.Length = 0 Then Return
        Dim owner As IWin32Window = If(FindForm(), DirectCast(Me, IWin32Window))
        If ViewerBox.Show(owner, ExchangeEntryText.UpdateAddressQuestion(src.Entry.Server, src.Moved), ExchangeResourceText.SectionTitle(),
                          MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) <> DialogResult.Yes Then Return
        If _entries.FindByIdentity(src.Moved, src.Entry.Login) IsNot Nothing Then
            RaiseEvent StatusSaid(ExchangeEntryText.AddressAlreadyAdded(), False)
            Return
        End If
        Dim name As String = src.Entry.DisplayName
        If Not _entries.Upsert(src.Entry.WithAddress(src.Moved)) Then
            RaiseEvent StatusSaid(ExchangeEntryText.AddressAlreadyAdded(), False)
            Return
        End If
        _hub.Forget(src.Entry.Id)
        RebuildSources()
        RaiseEvent StatusSaid(ExchangeEntryText.AddressUpdated(name), True)
        RaiseEvent ResourcesChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Ask 7 (optional): the string this entry hands its server on with - the address the server announced and, when the entry
    ''' holds a pin made under item D, the fingerprint. It is not a secret and carries none; nothing is stored and nothing is logged.</summary>
    Private Sub OnHandOnClicked(sender As Object, e As EventArgs)
        Dim src As ServerSource = CurrentSource()
        If src Is Nothing OrElse _busy Then Return
        Dim text As String = ExchangeEntryMove.HandOn(src.Entry, src.Session.PublicEndpoint)
        If text.Length = 0 Then
            RaiseEvent StatusSaid(ExchangeEntryText.HandOnNoEndpoint(), False)
            Return
        End If
        Try
            Clipboard.SetText(text)
            RaiseEvent StatusSaid(ExchangeEntryText.HandOnCopied(), True)
        Catch ex As Exception When TypeOf ex Is System.Runtime.InteropServices.ExternalException OrElse TypeOf ex Is InvalidOperationException
            RaiseEvent StatusSaid(ExchangeEntryText.HandOnCopyFailed(), False)
        End Try
    End Sub

    ''' <summary>Q187-4: per entry, off by default. Turning it on records what is listed NOW, so nothing that already exists is attached by
    ''' surprise - only what appears afterwards.</summary>
    Private Sub OnAutoChanged(sender As Object, e As EventArgs)
        If _loadingAuto Then Return
        Dim src As ServerSource = CurrentSource()
        If src Is Nothing Then Return
        Dim updated As ExchangeEntry = src.Entry.WithAutoAttach(_autoBox.Checked)
        If _autoBox.Checked Then
            Dim snapshot As ExchangeDirectorySnapshot = src.Feed.Current.Snapshot
            updated = updated.WithSeen(If(snapshot Is Nothing, Array.Empty(Of String)(), snapshot.Resources.Select(Function(r) r.ResourceId)))
        Else
            updated = updated.WithSeen(Array.Empty(Of String)())
        End If
        If _entries.Upsert(updated) Then src.Entry = updated
    End Sub

End Class

''' <summary>The window half of the add flow, in one place so the panel, the drop handler and the folder box open the same dialog.</summary>
Friend NotInheritable Class ExchangeConnectFlowUi
    Private Sub New()
    End Sub

    ''' <summary>Shows the dialog; returns the entry that was added (or signed in again), or Nothing.</summary>
    Friend Shared Function Run(owner As Form, initialText As String, signInAgain As ExchangeEntry) As ExchangeEntry
        If Not ExchangeFeatures.IsEnabled() Then Return Nothing
        Using dialog As New Exchange_Connect_Form(initialText, signInAgain)
            DialogLayer.ShowViewerDialog(dialog, owner)
            Return dialog.Added
        End Using
    End Function
End Class
#End If
