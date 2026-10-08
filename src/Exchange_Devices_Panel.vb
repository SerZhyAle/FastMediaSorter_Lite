#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The "My devices" section of Resources (ticket 171 rule 2): the user's other devices with a presence mark, and under each its resources with
''' a tick. A tick attaches (a resource of kind SFTP bound to the producer's id), an untick is the user removing it (with the panel's usual
''' confirmation). Hosted INSIDE <see cref="Resource_List_Panel"/>, so the Settings page and the manager dialog show the same section.
'''
''' Rules held here:
'''   * No row exists unless the exchange client features are offered AND the Share Manager (the agent's home) is installed (Q171-1);
'''   * the directory comes from the agent and is long-polled ONLY while this section is visible; hiding it cancels the loop;
'''   * the section never creates a connection of its own, never lists the device this program runs on (9 rule 1), and shows a kind it cannot
'''     use as "not supported by this version", never as an error;
'''   * absence is not authority: nothing here deletes anything except the user's own untick;
'''   * drawn from the host's palette (the Settings window's dark and light schemes), right-to-left aware, scaled with the host.
''' </summary>
Friend NotInheritable Class Exchange_Devices_Panel
    Inherits UserControl

    Private Enum ItemKind
        Header
        Resource
        Placeholder
    End Enum

    Private NotInheritable Class ItemData
        Friend Kind As ItemKind
        Friend Group As ExchangeDeviceGroup
        Friend Row As ExchangeDeviceRow
        Friend Record As ExchangeResourceRecord
    End Class

    Private ReadOnly _feed As ExchangeDevicesFeed
    Private ReadOnly _sync As ExchangeSync
    Private ReadOnly _loadResources As Func(Of List(Of ResourceDescriptor))
    Private ReadOnly _confirmRemove As Func(Of ResourceDescriptor, Boolean)
    Private ReadOnly _removeResource As Action(Of ResourceDescriptor)
    Private ReadOnly _titleLabel As Label
    Private ReadOnly _note As Label
    Private ReadOnly _startButton As Button
    Private ReadOnly _list As ListView
    Private _metrics As Func(Of Integer, Integer) = Function(n) n
    Private _palette As ResourcePanelPalette
    Private _cts As CancellationTokenSource
    Private _busy As Boolean
    Private _lastFollowedRevision As Long = -1
    Private _last As ExchangeDevicesState = ExchangeDevicesState.Hidden

    ''' <summary>An attach, an untick or a follow changed the registry: the host refills its list.</summary>
    Friend Event ResourcesChanged As EventHandler
    ''' <summary>One sentence for the host's status line (the outcome of a tick).</summary>
    Friend Event StatusSaid(text As String, ok As Boolean)
    ''' <summary>The directory moved the state of an attached resource (available, device offline, no longer published): the host's list redraws its rows.</summary>
    Friend Event StatesChanged As EventHandler

    Private _statesSignature As String = ""

    ''' <summary>The state of an attached resource against the LAST full directory this section saw; Unknown when there was none (so the row
    ''' says nothing - absence is not authority).</summary>
    Friend Function StateOf(resource As ResourceDescriptor) As ExchangeResourceState
        If Not ResourceOrigin.IsExchange(resource) Then Return ExchangeResourceState.Unknown
        Return ExchangeAttach.StateOf(resource.ExchangeResourceId, _last.Snapshot, _last.FullList)
    End Function

    Friend Sub New(feed As ExchangeDevicesFeed, sync As ExchangeSync, loadResources As Func(Of List(Of ResourceDescriptor)),
                   confirmRemove As Func(Of ResourceDescriptor, Boolean), removeResource As Action(Of ResourceDescriptor))
        _feed = feed
        _sync = sync
        _loadResources = loadResources
        _confirmRemove = confirmRemove
        _removeResource = removeResource
        Name = "res_Devices"
        Visible = False

        _titleLabel = New Label With {.Name = "res_DevicesTitle", .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft, .Text = ExchangeResourceText.SectionTitle()}
        _titleLabel.Font = New Font(Font, FontStyle.Bold)
        _note = New Label With {.Name = "res_DevicesNote", .AutoSize = False, .ForeColor = SystemColors.GrayText}
        _startButton = New Button With {.Name = "res_DevicesStart", .Text = ExchangeResourceText.StartButton(), .Visible = False}
        AddHandler _startButton.Click, AddressOf OnStartClicked
        _list = New ListView With {
            .Name = "res_DevicesList", .View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False,
            .HeaderStyle = ColumnHeaderStyle.None, .OwnerDraw = True, .ShowItemToolTips = True, .AccessibleName = ExchangeResourceText.SectionTitle()}
        _list.Columns.Add("", 100)
        AddHandler _list.DrawItem, AddressOf OnDrawItem
        AddHandler _list.DrawSubItem, Sub(s, e) e.DrawDefault = False
        AddHandler _list.MouseDown, AddressOf OnListMouseDown
        AddHandler _list.KeyDown, AddressOf OnListKeyDown
        AddHandler _list.Resize, Sub() FitColumn()
        Controls.Add(_list)
        Controls.Add(_startButton)
        Controls.Add(_note)
        Controls.Add(_titleLabel)
        AddHandler Resize, Sub() Relayout()
        AddHandler VisibleChanged, AddressOf OnSectionVisibleChanged
        AddHandler _feed.Changed, AddressOf OnFeedChanged
    End Sub

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
        _list.BackColor = theme.EditorBack
        _list.ForeColor = theme.EditorText
        _list.BorderStyle = If(theme.Dark, BorderStyle.None, BorderStyle.Fixed3D)
        _startButton.FlatStyle = FlatStyle.Flat
        _startButton.BackColor = theme.Surface
        _startButton.ForeColor = theme.Text
        _startButton.FlatAppearance.BorderColor = theme.Border
        _list.Invalidate()
    End Sub

    ''' <summary>Re-reads every caption after a language switch (the panel outlives it inside the reused Settings window).</summary>
    Friend Sub Localize()
        RightToLeft = If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No)
        _titleLabel.Text = ExchangeResourceText.SectionTitle()
        _startButton.Text = ExchangeResourceText.StartButton()
        _list.AccessibleName = ExchangeResourceText.SectionTitle()
        Render()
    End Sub

    ''' <summary>Whether this section may exist at all (the gate and the Share Manager).</summary>
    Friend Function IsOffered() As Boolean
        Return _feed.IsOffered()
    End Function

    ''' <summary>The height the section wants beside the host's list (0 when it is not offered).</summary>
    Friend Function WantedHeight(bodyHeight As Integer) As Integer
        If Not IsOffered() Then Return 0
        Return Math.Max(Dv(150), CInt(bodyHeight * 0.42))
    End Function

    ' --- life ---

    Private Sub OnSectionVisibleChanged(sender As Object, e As EventArgs)
        If Visible AndAlso IsOffered() Then StartLoop() Else StopLoop()
    End Sub

    Private Sub StartLoop()
        If _cts IsNot Nothing Then Return
        _cts = New CancellationTokenSource()
        Dim token As CancellationToken = _cts.Token
        ' runs until the section is hidden or disposed; the loop owns its failures (it never throws)
        Dim unused As Task = Task.Run(Function() _feed.RunAsync(token))
        Render()
    End Sub

    Private Sub StopLoop()
        Dim c As CancellationTokenSource = _cts
        _cts = Nothing
        If c IsNot Nothing Then
            Try : c.Cancel() : Catch : End Try
            Try : c.Dispose() : Catch : End Try
        End If
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
            RemoveHandler _feed.Changed, AddressOf OnFeedChanged
            StopLoop()
            Try : _titleLabel.Font?.Dispose() : Catch : End Try
        End If
        MyBase.Dispose(disposing)
    End Sub

    ' --- drawing the list ---

    Private Sub Relayout()
        Dim w As Integer = Math.Max(Dv(120), ClientSize.Width)
        Dim y As Integer = 0
        _titleLabel.SetBounds(0, y, w, Dv(24))
        y += Dv(24)
        Dim noteHeight As Integer = If(_note.Text.Length = 0, 0, TextRenderer.MeasureText(_note.Text, _note.Font, New Size(w, Integer.MaxValue), TextFormatFlags.WordBreak).Height + Dv(4))
        _note.SetBounds(0, y, w, noteHeight)
        y += noteHeight
        If _startButton.Visible Then
            Dim bw As Integer = Math.Max(Dv(110), TextRenderer.MeasureText(_startButton.Text, _startButton.Font).Width + Dv(32))
            _startButton.SetBounds(If(RightToLeft = RightToLeft.Yes, w - bw, 0), y + Dv(2), bw, Dv(30))
            y += Dv(34)
        End If
        _list.SetBounds(0, y, w, Math.Max(0, ClientSize.Height - y))
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

    Private Sub OnDrawItem(sender As Object, e As DrawListViewItemEventArgs)
        Dim data As ItemData = TryCast(e.Item.Tag, ItemData)
        Dim p As ResourcePanelPalette = _palette
        Dim back As Color = If(p IsNot Nothing, p.EditorBack, SystemColors.Window)
        Dim fore As Color = If(p IsNot Nothing, p.EditorText, SystemColors.WindowText)
        Dim muted As Color = If(p IsNot Nothing, p.Muted, SystemColors.GrayText)
        Dim accent As Color = If(p IsNot Nothing, p.Accent, SystemColors.Highlight)
        If e.Item.Selected AndAlso data IsNot Nothing AndAlso data.Kind = ItemKind.Resource Then
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
            Case ItemKind.Header
                Using bold As New Font(_list.Font, FontStyle.Bold)
                    TextRenderer.DrawText(e.Graphics, e.Item.Text, bold, textBounds, If(data.Group.Online, fore, muted), flags)
                End Using
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

    ' --- what is on screen ---

    Private Sub Render()
        If IsDisposed Then Return
        Dim state As ExchangeDevicesState = _feed.Current
        _last = state
        Dim groups As List(Of ExchangeDeviceGroup) = If(state.Availability = ExchangeDevicesAvailability.Ready,
            ExchangeDevicesModel.Build(state.Snapshot, SafeResources(), state.OwnDeviceId), New List(Of ExchangeDeviceGroup)())
        Dim hasRows As Boolean = groups.Any(Function(g) g.Rows.Count > 0)
        _note.Text = ExchangeResourceText.Availability(state, hasRows)
        _note.Visible = _note.Text.Length > 0
        _startButton.Visible = state.Availability = ExchangeDevicesAvailability.NotRunning
        _startButton.Enabled = Not _busy

        _list.BeginUpdate()
        Try
            Dim selectedId As String = SelectedResourceId()
            _list.Items.Clear()
            If state.Snapshot IsNot Nothing Then
                For Each g As ExchangeDeviceGroup In groups
                    _list.Items.Add(New ListViewItem(ExchangeResourceText.DeviceHeader(g.Name, g.Online)) With {.Tag = New ItemData With {.Kind = ItemKind.Header, .Group = g}})
                    If g.Rows.Count = 0 Then
                        _list.Items.Add(New ListViewItem(ExchangeResourceText.NoFolders()) With {.Tag = New ItemData With {.Kind = ItemKind.Placeholder, .Group = g}})
                    End If
                    For Each r As ExchangeDeviceRow In g.Rows
                        Dim record As ExchangeResourceRecord = state.Snapshot.Resources.FirstOrDefault(Function(x) String.Equals(x.ResourceId, r.ResourceId, StringComparison.Ordinal))
                        Dim item As New ListViewItem(r.Name) With {.Tag = New ItemData With {.Kind = ItemKind.Resource, .Group = g, .Row = r, .Record = record},
                                                                  .ToolTipText = r.Name & If(ExchangeResourceText.RowState(r).Length > 0, " - " & ExchangeResourceText.RowState(r), "")}
                        _list.Items.Add(item)
                        If String.Equals(r.ResourceId, selectedId, StringComparison.Ordinal) Then item.Selected = True
                    Next
                Next
            End If
        Finally
            _list.EndUpdate()
        End Try
        _list.Visible = state.Snapshot IsNot Nothing AndAlso hasRows
        Relayout()
        FollowWhenNew(state)
        ' the host's rows say what their producers are now; they are redrawn only when one of those states moved
        Dim states As Dictionary(Of String, ExchangeResourceState) = ExchangeDevicesModel.StatesOf(SafeResources(), state.Snapshot, state.FullList)
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

    Private Function SelectedResourceId() As String
        If _list.SelectedItems.Count = 0 Then Return ""
        Dim data As ItemData = TryCast(_list.SelectedItems(0).Tag, ItemData)
        Return If(data IsNot Nothing AndAlso data.Row IsNot Nothing, data.Row.ResourceId, "")
    End Function

    ''' <summary>A new full directory is the moment to follow the producers of the attached resources (rule 4), once per revision.</summary>
    Private Sub FollowWhenNew(state As ExchangeDevicesState)
        If state.Snapshot Is Nothing OrElse state.Snapshot.Revision = _lastFollowedRevision Then Return
        _lastFollowedRevision = state.Snapshot.Revision
        Dim snapshot As ExchangeDevicesState = state
        Dim token As CancellationToken = If(_cts IsNot Nothing, _cts.Token, CancellationToken.None)
        Dim t As Task = Task.Run(
            Async Function()
                Dim changed As List(Of String) = Await _sync.FollowAllAsync(snapshot, token).ConfigureAwait(False)
                If changed.Count > 0 AndAlso Not IsDisposed Then
                    Try
                        BeginInvoke(New Action(Sub() RaiseEvent ResourcesChanged(Me, EventArgs.Empty)))
                    Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ObjectDisposedException
                    End Try
                End If
            End Function)
    End Sub

    ' --- the ticks ---

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
        If _busy OrElse data.Row Is Nothing Then Return
        If data.Row.Attached Then
            ' an untick is the user's act: the panel's own removal, with its own confirmation
            Dim resource As ResourceDescriptor = ResourceOrigin.FindByExchangeId(SafeResources(), data.Row.ResourceId)
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
        _busy = True
        Try
            Dim outcome As ExchangeAttachOutcome = Await _sync.AttachAsync(data.Record, data.Group.Name, If(_cts IsNot Nothing, _cts.Token, CancellationToken.None))
            If outcome = ExchangeAttachOutcome.Attached Then
                RaiseEvent StatusSaid(ExchangeResourceText.Attached(data.Row.Name), True)
                RaiseEvent ResourcesChanged(Me, EventArgs.Empty)
            Else
                RaiseEvent StatusSaid(ExchangeResourceText.AttachFailure(outcome), False)
            End If
        Catch ex As OperationCanceledException
        Finally
            _busy = False
        End Try
        If Not IsDisposed Then Render()
    End Sub

    ' --- starting the Share Manager (the user's act) ---

    Private Async Sub OnStartClicked(sender As Object, e As EventArgs)
        If _busy Then Return
        _busy = True
        _startButton.Enabled = False
        RaiseEvent StatusSaid(ExchangeResourceText.Starting(), True)
        Try
            Dim started As Boolean = ExchangeAgentLocator.TryStart()
            If Not started Then
                RaiseEvent StatusSaid(ExchangeResourceText.StartFailed(), False)
                Return
            End If
            ' give it a few seconds to come up, then look again
            For i As Integer = 1 To 10
                Await Task.Delay(1000)
                If IsDisposed Then Return
                Dim state As ExchangeDevicesState = Await _feed.RefreshAsync(CancellationToken.None)
                If state.Availability <> ExchangeDevicesAvailability.NotRunning Then Exit For
            Next
        Finally
            _busy = False
            If Not IsDisposed Then Render()
        End Try
    End Sub

End Class
#End If
