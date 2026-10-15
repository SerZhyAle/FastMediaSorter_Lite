#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The colours a host hands the panel (131 §3.3). Only the Settings window fills it - the
''' panel paints itself from it; the manager dialog passes nothing and keeps the machine's
''' SystemColors look. No colour literal lives in the panel itself, except the status roles'
''' fallback below, which repeats the contract's own light defaults.
''' </summary>
Friend NotInheritable Class ResourcePanelPalette
    Public Surface As Color
    Public Text As Color
    Public Muted As Color
    Public Border As Color
    Public EditorBack As Color
    Public EditorText As Color
    Public Accent As Color
    Public OnAccent As Color
    Public Success As Color
    Public Warning As Color
    Public Danger As Color
    Public Dark As Boolean
End Class

''' <summary>
''' The resource list and its actions (tickets 118 and 131): one implementation for two hosts.
''' The panel reads top-to-bottom as action row, list, status - the action row carries the
''' list summary on the left and the commands on the right (Add, Edit, Remove, Test, then Open
''' as the row's primary action on the Settings host, then the "more" overflow holding Export
''' and the two imports behind the 125 painted menu); the list fills every remaining pixel and
''' is fully themed in the dark through the host palette (owner-drawn header, editor-fill
''' rows); the status line is one always-visible line whose full text lives in
''' its tooltip and whose severity is a coloured catalogue glyph, never colour alone; with no
''' resources the list gives way to the 124 empty state - a muted glyph, one sentence and an
''' Add button.
'''
''' Everything it stores goes through <see cref="ResourceCommit"/> (the credential first, the
''' registry reference last), so no order of clicks and no failure part-way leaves a list entry
''' without its password. The hosts learn what happened through three events:
''' <see cref="ResourcesChanged"/> (any registry write), <see cref="ItemActivated"/> (the Open
''' action: a double-click or the Open button - both hosts answer it) and
''' <see cref="SelectionChanged"/>.
''' </summary>
Friend NotInheritable Class Resource_List_Panel
    Inherits UserControl

    ' Logical metrics (96-dpi px), scaled with the host window (131 §3.2/§3.3).
    Private Const ActionRowHeight As Integer = 40
    Private Const CommandHeight As Integer = 32
    Private Const CommandPadding As Integer = 16
    Private Const CommandGap As Integer = 8
    Private Const StatusHeight As Integer = 24
    Private Const RowHeight As Integer = 22
    Private Const GlyphSize As Integer = 16
    Private Const EmptyGlyphSize As Integer = 48

    ''' <summary>Column minimums (131 §3.3): their 536 logical sum fits the ~560 px content
    ''' width at the window's minimum - the 666 px total of UX-526 is gone; Address (index 2)
    ''' is the growing column and absorbs whatever the minimums leave.</summary>
    Private Shared ReadOnly ColumnMinimums As Integer() = {110, 96, 150, 90, 90}

    Private ReadOnly list As ListView
    Private ReadOnly actionRow As Panel
    Private ReadOnly summary As Label
    Private ReadOnly emptyPanel As Panel
    Private ReadOnly emptyGlyph As Label
    Private ReadOnly hint As Label
    Private ReadOnly emptyAddButton As Button
    Private ReadOnly statusRow As Panel
    Private ReadOnly status As Label
    Private ReadOnly statusGlyph As Label
    Private ReadOnly addButton As Button
    Private ReadOnly editButton As Button
    Private ReadOnly removeButton As Button
    Private ReadOnly testButton As Button
    Private ReadOnly openButton As Button
    Private ReadOnly moreButton As Button
    Private ReadOnly tips As New ToolTip()
    Private ReadOnly removed As New List(Of String)()
    Private testCancel As CancellationTokenSource
    Private panelScale As Single
    Private scaleIncludesDpi As Boolean
    Private palette As ResourcePanelPalette
    Private rowImages As ImageList
    Private menuTheme As ChromeTheme
    Private lastSeverity As ResourceStatusSeverity = ResourceStatusSeverity.None

    ' Persistence and prompts, injectable so the tests drive the same code the two hosts ship
    ' with; every default is exactly what the dialog used before the extraction.
    Private ReadOnly hiveRead As Func(Of String, String)
    Private ReadOnly hiveWrite As Action(Of String, String)
    Private ReadOnly secrets As IResourceSecretStore
    Private ReadOnly confirmRemove As Func(Of ResourceDescriptor, Boolean)
    Private ReadOnly customStore As Boolean
    ''' <summary>Where the local SFTP server's mirror rows come from. Nothing means "none":
    ''' the tests' injected world (a custom hive) shows no shares unless it names a source,
    ''' so a test never reads the machine's share list; the shipped hosts inject the real one.</summary>
    Private ReadOnly managedShareSource As Func(Of IEnumerable(Of ResourceDescriptor))
    ''' <summary>The handover for a managed row's Edit/Remove - waking the Share Manager.
    ''' The hosts pass their own entry point; without one the mirror rule still holds (the
    ''' command only says where the folder is managed), so the panel never grows a
    ''' dependency on a form.</summary>
    Private ReadOnly manageSharesAction As Action
    ''' <summary>Spec 227 rule 2: the host's "Share Manager installed" probe; Nothing keeps the file probe.</summary>
    Private ReadOnly shareInstalledSource As Func(Of Boolean)
    ''' <summary>Spec 227 rule 2: the host's camera hand-over; Nothing keeps the item absent.</summary>
    Private ReadOnly scanCameraAction As Action
    ''' <summary>The "My devices" section (ticket 171). Nothing unless the exchange client features are offered and the Share Manager is installed;
    ''' never in an injected world (a test's custom hive), where the agent's channel must not be asked. Once the Settings page has taken the
    ''' section over through <see cref="DetachDevices"/>, the field keeps the wiring (the metrics, the palette, the per-row states) while the
    ''' placement lives in the page's own group.</summary>
    Private ReadOnly devices As Exchange_Devices_Panel
    Private devicesDetached As Boolean

    ''' <summary>Any registry write happened through this panel: adds, edits, removals, imports.
    ''' Carries the ids removed so far, so a host can close a resource that is on screen.</summary>
    Friend Event ResourcesChanged(removedIds As IReadOnlyList(Of String))

    ''' <summary>The user asked to open a resource: a double-click on a row or the Open button -
    ''' the same action (131 §3.2; this revises Q118-13, which kept the Settings page's
    ''' double-click to selection only). Both hosts answer it: the manager dialog hands the
    ''' resource back to the viewer, the Settings page closes itself first and the viewer
    ''' connects through its own path.</summary>
    Friend Event ItemActivated(resource As ResourceDescriptor)

    ''' <summary>The selection moved; the argument says whether anything is selected.</summary>
    Friend Event SelectionChanged(hasSelection As Boolean)

    ''' <param name="scale">The interface scale: 1.0 for the dialog (its exact former metrics),
    ''' the settings window's own scale for the Settings page.</param>
    ''' <param name="withPrimaryOpen">The Settings page's Open action lives in the row as the
    ''' primary button; the manager dialog keeps Open in its own chrome and passes nothing.</param>
    ''' <param name="shareInstalledProbe">Spec 227 rule 2: the host's answer to "is the Share Manager
    ''' installed" (its own file probe). Nothing keeps the panel's quiet file probe.</param>
    ''' <param name="scanPhoneCodeCamera">Spec 227 rule 2: the host's hand-over of the camera scan to
    ''' the Share Manager's add-resource flow. Nothing keeps the item absent - the panel has no camera
    ''' door of its own to offer.</param>
    Friend Sub New(Optional scale As Single = 1.0F,
                   Optional readValue As Func(Of String, String) = Nothing,
                   Optional writeValue As Action(Of String, String) = Nothing,
                   Optional secretStore As IResourceSecretStore = Nothing,
                   Optional removeConfirmation As Func(Of ResourceDescriptor, Boolean) = Nothing,
                   Optional withPrimaryOpen As Boolean = False,
                   Optional deviceScale As Boolean = False,
                   Optional managedShares As Func(Of IEnumerable(Of ResourceDescriptor)) = Nothing,
                   Optional openShareManager As Action = Nothing,
                   Optional shareInstalledProbe As Func(Of Boolean) = Nothing,
                   Optional scanPhoneCodeCamera As Action = Nothing)
        panelScale = If(scale > 0.0F, scale, 1.0F)
        scaleIncludesDpi = deviceScale
        customStore = readValue IsNot Nothing OrElse writeValue IsNot Nothing OrElse secretStore IsNot Nothing
        hiveRead = If(readValue, AddressOf ResourceRegistry.HiveRead)
        hiveWrite = If(writeValue, AddressOf ResourceRegistry.HiveWrite)
        secrets = If(secretStore, New RegistryResourceSecretStore())
        confirmRemove = If(removeConfirmation, AddressOf AskRemoveConfirmation)
        managedShareSource = managedShares
        manageSharesAction = openShareManager
        shareInstalledSource = shareInstalledProbe
        scanCameraAction = scanPhoneCodeCamera

        If Localization.IsRightToLeft() Then RightToLeft = RightToLeft.Yes

        list = New ListView With {
            .Name = "res_List", .View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False,
            .ShowItemToolTips = True, .LabelWrap = False, .OwnerDraw = True,
            .AccessibleName = Localization.T("Ресурсы")}
        list.Columns.Add(Localization.T("Название"), Sp(110))
        list.Columns.Add(Localization.T("Тип"), Sp(96))
        list.Columns.Add(Localization.T("Адрес"), Sp(150))
        list.Columns.Add(Localization.T("Доступ"), Sp(90))
        list.Columns.Add(Localization.T("Открывался"), Sp(90))
        AddHandler list.SelectedIndexChanged,
            Sub()
                UpdateButtons()
                RaiseEvent SelectionChanged(Selected() IsNot Nothing)
            End Sub
        AddHandler HandleCreated,
            Sub()
                RefreshGlyphImages()
                Relayout()
            End Sub
        AddHandler list.DrawColumnHeader, AddressOf OnDrawColumnHeader
        AddHandler list.DrawSubItem, AddressOf OnDrawSubItem
        AddHandler list.DrawItem, Sub(sender As Object, e As DrawListViewItemEventArgs) e.DrawDefault = palette Is Nothing
        AddHandler list.MouseMove,
            Sub(sender, e)
                Dim item As ListViewItem = list.GetItemAt(e.X, e.Y)
                Dim fullText As String = If(item Is Nothing, "", item.ToolTipText)
                If tips.GetToolTip(list) <> fullText Then tips.SetToolTip(list, fullText)
            End Sub
        AddHandler list.DoubleClick,
            Sub()
                Dim chosen As ResourceDescriptor = Selected()
                If chosen IsNot Nothing Then RaiseEvent ItemActivated(chosen)
            End Sub
        ' Spec 227 rule 1: the door takes a dropped file too - a .fmscfg document or a QR image
        ' dragged straight onto the list reads through the same intake as the open dialog.
        AllowDrop = True
        AddHandler DragEnter,
            Sub(sender As Object, e As DragEventArgs)
                If DroppedIntakeFile(e) IsNot Nothing Then
                    e.Effect = DragDropEffects.Copy
                Else
                    e.Effect = DragDropEffects.None
                End If
            End Sub
        AddHandler DragDrop,
            Sub(sender As Object, e As DragEventArgs)
                Dim path As String = DroppedIntakeFile(e)
                If path IsNot Nothing Then ImportAndRefresh(PhoneConnectionIntake.ReadFile(path))
            End Sub

        summary = New Label With {
            .Name = "res_Summary", .AutoSize = False, .Text = "", .AutoEllipsis = True,
            .TextAlign = ContentAlignment.MiddleLeft, .ForeColor = SystemColors.GrayText}
        actionRow = New Panel With {.Name = "res_ActionRow", .Height = Sp(ActionRowHeight)}
        actionRow.Controls.Add(summary)
        ' Disabled buttons do not receive mouse messages. Their parent owns the hover
        ' reason so the tooltip remains available while the command is disabled.
        AddHandler actionRow.MouseMove,
            Sub(sender, e)
                Dim disabled As Button = ActionRowButtons().FirstOrDefault(Function(b) Not b.Enabled AndAlso b.Bounds.Contains(e.Location))
                Dim reason As String = If(disabled Is Nothing, "", tips.GetToolTip(disabled))
                If tips.GetToolTip(actionRow) <> reason Then tips.SetToolTip(actionRow, reason)
            End Sub

        addButton = MakeCommandButton("res_Add", Localization.T("Добавить.."), AddressOf OnAdd)
        editButton = MakeCommandButton("res_Edit", Localization.T("Изменить.."), AddressOf OnEdit)
        removeButton = MakeCommandButton("res_Remove", Localization.T("Удалить"), AddressOf OnRemove)
        testButton = MakeCommandButton("res_Test", Localization.T("Проверить"), AddressOf OnTest)
        If withPrimaryOpen Then
            openButton = MakeCommandButton("res_Open", Localization.T("Открыть"), AddressOf OnOpen)
        End If
        ' WINDOWS-UI section 11: a command carries the glyph of its verb, mono (a panel command is never an answer).
        ButtonGlyphs.Apply(addButton, "action.add")
        ButtonGlyphs.Apply(editButton, "action.edit")
        ButtonGlyphs.Apply(removeButton, "action.delete")
        ButtonGlyphs.Apply(testButton, "action.test-connection")
        If openButton IsNot Nothing Then ButtonGlyphs.Apply(openButton, "nav.open-external")
        moreButton = New Button With {
            .Name = "res_More", .Text = "", .ImageAlign = ContentAlignment.MiddleCenter,
            .MinimumSize = New Size(0, Sp(28)), .Height = Sp(CommandHeight),
            .AccessibleName = Localization.T("Ещё")}
        AddHandler moreButton.Click, AddressOf OnMore
        For Each b As Button In ActionRowButtons()
            actionRow.Controls.Add(b)
            AddHandler b.TextChanged, Sub() Relayout()
        Next

        ' The 124 empty-state pattern: with no resources it replaces the list body - a centred
        ' muted glyph, one sentence, an Add button - and the list's column headers do not float
        ' over it, because the list itself is hidden.
        emptyGlyph = New Label With {
            .Name = "res_EmptyGlyph", .AutoSize = False, .Size = New Size(Sp(EmptyGlyphSize), Sp(EmptyGlyphSize))}
        hint = New Label With {
            .Name = "res_Hint", .AutoSize = False, .TextAlign = ContentAlignment.TopCenter,
            .ForeColor = SystemColors.GrayText,
            .Text = Localization.T("Здесь пока нет ресурсов. Добавьте сервер вручную или импортируйте файл настроек из Share Manager (.fmscfg).")}
        emptyAddButton = New Button With {
            .Name = "res_EmptyAdd", .Text = Localization.T("Добавить.."),
            .MinimumSize = New Size(0, Sp(28)), .Height = Sp(CommandHeight)}
        ButtonGlyphs.Apply(emptyAddButton, "action.add")
        AddHandler emptyAddButton.Click, AddressOf OnAdd
        emptyPanel =New Panel With {.Name = "res_Empty"}
        emptyPanel.Controls.Add(emptyGlyph)
        emptyPanel.Controls.Add(hint)
        emptyPanel.Controls.Add(emptyAddButton)

        statusGlyph = New Label With {
            .Name = "res_StatusGlyph", .AutoSize = False, .Size = New Size(Sp(GlyphSize), Sp(GlyphSize))}
        status = New Label With {
            .Name = "res_Status", .AutoSize = False, .AutoEllipsis = True, .Height = Sp(StatusHeight),
            .TextAlign = ContentAlignment.MiddleLeft, .ForeColor = SystemColors.GrayText}
        statusRow = New Panel With {.Name = "res_StatusRow", .Height = Sp(StatusHeight)}
        statusRow.Controls.Add(statusGlyph)
        statusRow.Controls.Add(status)

        Controls.Add(list)
        Controls.Add(emptyPanel)
        Controls.Add(statusRow)
        Controls.Add(actionRow)

        ' Tickets 171 and 187: "My devices", one group per exchange server the viewer holds an entry for. Built only where it can exist (the
        ' gate); it needs no Share Manager (rule 5) and the section hides itself while the features are not offered.
        If Not customStore AndAlso ExchangeFeatures.IsEnabled() Then
            devices = New Exchange_Devices_Panel(ExchangeViewerHub.Instance(), ExchangeEntryStore.CreateDefault(), AddressOf ResourceRegistry.Load,
                                                 confirmRemove, AddressOf RemoveExchangeResource)
            devices.SetMetrics(Function(n) Dv(n))
            AddHandler devices.ResourcesChanged, Sub() OnDevicesChangedResources()
            AddHandler devices.StatesChanged, Sub() Refill(If(Selected() Is Nothing, Nothing, Selected().Id))
            AddHandler devices.StatusSaid, Sub(text, ok) Say(text, If(ok, ResourceStatusSeverity.Success, ResourceStatusSeverity.Warning))
            Controls.Add(devices)
        End If

        AddHandler Resize, Sub() Relayout()
        AddHandler FontChanged, Sub() Relayout()
        AddHandler RightToLeftChanged, Sub() Relayout()
        AddHandler DpiChangedAfterParent,
            Sub()
                RefreshGlyphImages()
                Relayout()
            End Sub
        Relayout()
        Refill()
        Localize()
    End Sub

    Private Function Sp(value As Integer) As Integer
        If panelScale = 1.0F Then Return value
        Return CInt(Math.Round(value * panelScale))
    End Function

    ''' <summary>Convert logical metrics once. Settings already includes monitor DPI in
    ''' panelScale; a dialog supplies only interface scaling and still needs DeviceDpi.</summary>
    Private Function Dv(value As Integer) As Integer
        If scaleIncludesDpi OrElse Not IsHandleCreated Then Return Sp(value)
        Return LogicalToDeviceUnits(Sp(value))
    End Function

    ''' <summary>Settings supplies its already DPI-adjusted scale, including monitor changes.
    ''' Dialog hosts supply logical interface scaling and use DeviceDpi in Dv instead.</summary>
    Friend Sub SetDeviceScale(scale As Single)
        scaleIncludesDpi = True
        Dim nextScale As Single = If(scale > 0.0F, scale, 1.0F)
        If panelScale = nextScale Then Return
        panelScale = nextScale
        RefreshGlyphImages()
        Relayout()
    End Sub

    ''' <summary>The height the Settings page gives the panel when the table stands at the
    ''' height of its own rows: the action row, the column strip, one row per resource and
    ''' the status line - more rows, a taller table - while the empty state keeps the block
    ''' its glyph and sentence need. The dialog host never asks; its body is the dialog's
    ''' own chrome.</summary>
    Friend Function PreferredHeight() As Integer
        Dim bodyHeight As Integer
        If list.Visible AndAlso list.Items.Count > 0 Then
            bodyHeight = ColumnStripHeight() + Dv(RowHeight) * list.Items.Count
        Else
            bodyHeight = Dv(170)
        End If
        Return actionRow.Height + Dv(6) + bodyHeight + Dv(6) + Dv(StatusHeight)
    End Function

    ''' <summary>The column strip's own height: asked from the list once its handle exists
    ''' (the first row's top is the strip's bottom), estimated before that. The Settings
    ''' host re-asks when the handle arrives, so the estimate never survives on screen.</summary>
    Private Function ColumnStripHeight() As Integer
        If list.IsHandleCreated AndAlso list.Items.Count > 0 Then
            Try
                Return Math.Max(0, list.GetItemRect(0).Top)
            Catch
            End Try
        End If
        Return Dv(24)
    End Function

    ''' <summary>Hands the "My devices" section to the Settings page's own collapsible group:
    ''' the placement moves out of this panel's body, the wiring - the events, the metrics,
    ''' the palette, the per-row states - stays. Nothing when the section cannot exist or has
    ''' already been handed over. The dialog host never calls this and keeps the embedded
    ''' look of tickets 171 and 187.</summary>
    Friend Function DetachDevices() As Exchange_Devices_Panel
        If devices Is Nothing OrElse devicesDetached Then Return Nothing
        devicesDetached = True
        Controls.Remove(devices)
        Relayout()
        Return devices
    End Function

    Private Function MakeCommandButton(controlName As String, text As String, handler As EventHandler) As Button
        Dim b As New Button With {.Name = controlName, .Text = text,
                                  .MinimumSize = New Size(0, Sp(28)), .Height = Sp(CommandHeight)}
        AddHandler b.Click, handler
        Return b
    End Function

    ''' <summary>The action row's commands, in reading order: Add, Edit, Remove, Test, the
    ''' primary Open (Settings host only) and the overflow. The empty state's Add is not part
    ''' of the row; it is styled with the same palette below.</summary>
    Private Function ActionRowButtons() As IEnumerable(Of Button)
        Dim buttons As New List(Of Button) From {addButton, editButton, removeButton, testButton}
        If openButton IsNot Nothing Then buttons.Add(openButton)
        buttons.Add(moreButton)
        Return buttons
    End Function

    Private Function Mirror(x As Integer, w As Integer, width As Integer) As Integer
        If RightToLeft <> RightToLeft.Yes Then Return x
        Return width - x - w
    End Function

    ''' <summary>A command fits its text with 16 logical px of horizontal padding at the uniform
    ''' 32 px height (127 §3.2) plus the verb's glyph and its gap (WINDOWS-UI section 11 item 3);
    ''' the overflow is square. Never docked, never stretched.</summary>
    Private Function MeasureCommandButton(button As Button) As Integer
        If button Is moreButton Then Return Dv(CommandHeight)
        Dim textWidth As Integer = TextRenderer.MeasureText(button.Text, button.Font,
            New Size(Integer.MaxValue, Integer.MaxValue), TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine).Width
        ' The picture already holds its own gap to the caption; the logical 16 + 6 is the floor before it is rendered.
        Dim glyphWidth As Integer = Math.Max(Dv(GlyphSize + 6), ButtonGlyphs.ExtraWidth(button))
        Return Math.Max(Dv(64), textWidth + glyphWidth + 2 * Dv(CommandPadding))
    End Function

    ''' <summary>The panel's whole layout (131 §3.1): action row, list (or the empty state over
    ''' its place), status line. Everything follows the panel's own size, so the two hosts (a
    ''' 700x372 dialog and a settings page that fills the window) need no layout code of their
    ''' own; the minimum list height of 320 logical px is the Settings host's decision.</summary>
    Private Sub Relayout()
        ' Everything below places a child by an explicit rectangle, so the panel is held while it is done (one
        ' layout pass, WINDOWS-UI section 5 amended 0.2) - each SetBounds used to run the panel's layout again.
        SuspendLayout()
        Try
            RelayoutHeld()
        Finally
            ResumeLayout(False)
        End Try
    End Sub

    Private Sub RelayoutHeld()
        ' Dv, not Sp: this runs after the window has its DPI (a Resize), where a logical 96-dpi pixel has to be
        ' turned into a device one - the constants used to stay small beside a font 1.75x larger (124 UX-202).
        Dim width As Integer = Math.Max(Dv(160), ClientSize.Width)
        Dim rowHeight As Integer = Dv(ActionRowHeight)
        actionRow.SetBounds(0, 0, width, rowHeight)
        LayoutActionRow(width, rowHeight)
        rowHeight = actionRow.Height
        ' The local is statusRowHeight, not statusHeight: VB matches names case-insensitively,
        ' so a local named like the StatusHeight constant would shadow it and Dv(StatusHeight)
        ' would read the not-yet-assigned local - a silent 0.
        Dim statusRowHeight As Integer = Dv(StatusHeight)
        Dim bodyTop As Integer = rowHeight + Dv(6)
        Dim bodyHeight As Integer = Math.Max(Dv(60), ClientSize.Height - bodyTop - statusRowHeight - Dv(6))
        ' Ticket 171: "My devices" takes the lower part of the body when it is offered; the resource list keeps the rest.
        ' A detached section is the Settings page's own group and is never laid out here.
        Dim devicesHeight As Integer = 0
        If devices IsNot Nothing AndAlso Not devicesDetached Then
            devicesHeight = Math.Min(devices.WantedHeight(bodyHeight), Math.Max(0, bodyHeight - Dv(80)))
            devices.SetBounds(0, bodyTop + bodyHeight - devicesHeight, width, devicesHeight)
            devices.Visible = devicesHeight > 0
            If devicesHeight > 0 Then bodyHeight = Math.Max(Dv(60), bodyHeight - devicesHeight - Dv(6))
        End If
        list.SetBounds(0, bodyTop, width, bodyHeight)
        emptyPanel.SetBounds(0, bodyTop, width, bodyHeight)
        statusRow.SetBounds(0, ClientSize.Height - statusRowHeight, width, statusRowHeight)
        LayoutStatusRow(width, statusRowHeight)
        LayoutColumns()
        LayoutEmptyState()
    End Sub

    ''' <summary>Summary at the reading start, commands at the end in reading order.
    ''' Long captions wrap at narrow widths; RTL mirrors every computed rectangle.</summary>
    Private Sub LayoutActionRow(width As Integer, rowHeight As Integer)
        Dim buttons As Button() = ActionRowButtons().ToArray()
        Dim gap As Integer = Dv(CommandGap)
        Dim widths As Integer() = buttons.Select(Function(b) MeasureCommandButton(b)).ToArray()
        Dim total As Integer = widths.Sum() + gap * (buttons.Length - 1)
        Dim summaryWidth As Integer = If(summary.Text.Length = 0, 0,
            TextRenderer.MeasureText(summary.Text, summary.Font, Size.Empty, TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine).Width + Dv(8))
        Dim separateSummary As Boolean = summaryWidth > 0 AndAlso total + summaryWidth + gap > width
        Dim top As Integer = If(separateSummary, rowHeight, 0)
        Dim index As Integer = 0
        While index < buttons.Length
            Dim start As Integer = index
            Dim used As Integer = Math.Min(width, widths(index))
            index += 1
            While index < buttons.Length AndAlso used + gap + widths(index) <= width
                used += gap + widths(index)
                index += 1
            End While
            Dim edge As Integer = width - used
            For position As Integer = start To index - 1
                Dim buttonWidth As Integer = Math.Min(width, widths(position))
                buttons(position).SetBounds(Mirror(edge, buttonWidth, width),
                    top + (rowHeight - Dv(CommandHeight)) \ 2, buttonWidth, Dv(CommandHeight))
                edge += buttonWidth + gap
            Next
            top += rowHeight
        End While
        Dim availableSummary As Integer = If(separateSummary, width, Math.Max(0, width - total - gap))
        summary.SetBounds(Mirror(0, availableSummary, width), 0, availableSummary, rowHeight)
        actionRow.Height = top
    End Sub

    ' The local is statusGlyphSize, not glyphSize: VB matches names case-insensitively, so a
    ' local named like the GlyphSize constant would shadow it and Dv(GlyphSize) would read
    ' the not-yet-assigned local - a silent 0 (same trap as statusRowHeight above).
    Private Sub LayoutStatusRow(width As Integer, rowHeight As Integer)
        Dim statusGlyphSize As Integer = Dv(GlyphSize)
        Dim glyphVisible As Boolean = statusGlyph.Image IsNot Nothing
        Dim textLeft As Integer = If(glyphVisible, statusGlyphSize + Dv(8), 0)
        status.SetBounds(Mirror(textLeft, width - textLeft, width), 0, Math.Max(Dv(40), width - textLeft), rowHeight)
        statusGlyph.SetBounds(Mirror(0, statusGlyphSize, width), (rowHeight - statusGlyphSize) \ 2, statusGlyphSize, statusGlyphSize)
        statusGlyph.Visible = glyphVisible
    End Sub

    Private Sub LayoutEmptyState()
        Dim width As Integer = Math.Max(Dv(160), emptyPanel.ClientSize.Width)
        Dim height As Integer = Math.Max(Dv(120), emptyPanel.ClientSize.Height)
        Dim glyphSize As Integer = Dv(EmptyGlyphSize)
        Dim textWidth As Integer = Math.Max(Dv(160), width - Dv(48))
        Dim hintTextHeight As Integer = Math.Max(Dv(18), TextRenderer.MeasureText(hint.Text, hint.Font,
            New Size(textWidth, Integer.MaxValue), TextFormatFlags.WordBreak).Height + Dv(4))
        Dim buttonWidth As Integer = MeasureCommandButton(emptyAddButton)
        Dim gap As Integer = Dv(10)
        Dim total As Integer = glyphSize + gap + hintTextHeight + gap + Dv(CommandHeight)
        Dim top As Integer = Math.Max(0, (height - total) \ 2)
        emptyGlyph.SetBounds((width - glyphSize) \ 2, top, glyphSize, glyphSize)
        hint.SetBounds((width - textWidth) \ 2, top + glyphSize + gap, textWidth, hintTextHeight)
        emptyAddButton.SetBounds((width - buttonWidth) \ 2, top + glyphSize + gap + hintTextHeight + gap,
                                 buttonWidth, Dv(CommandHeight))
    End Sub

    ''' <summary>Minimum widths that fit at the window's narrowest; Address grows into what is
    ''' left (131 §3.3). Rows ellipsize natively and carry the full row text in a tooltip.</summary>
    Private Sub LayoutColumns()
        If list.Columns.Count < ColumnMinimums.Length Then Return
        Dim available As Integer = Math.Max(0, list.ClientSize.Width)
        Dim widths(ColumnMinimums.Length - 1) As Integer
        Dim used As Integer = 0
        For index As Integer = 0 To ColumnMinimums.Length - 1
            widths(index) = Dv(ColumnMinimums(index))
            used += widths(index)
        Next
        If available > used Then widths(2) += available - used
        For index As Integer = 0 To widths.Length - 1
            list.Columns(index).Width = widths(index)
        Next
    End Sub

    ''' <summary>The host's palette on every part of the panel (131 §3.3). Only the Settings
    ''' window calls it - the manager dialog keeps the machine's SystemColors look. The rows
    ''' sit on the editor fill, the header is owner-drawn from the palette in the dark (the
    ''' light strips of the review are gone) and native in the light, the themed cells are
    ''' owner-drawn in both schemes, the scrollbars follow the
    ''' host's own ApplyNativeTree call, and the commands take the 127 button treatment with
    ''' Open in the accent.</summary>
    Friend Sub ApplyPalette(theme As ResourcePanelPalette)
        palette = theme
        BackColor = theme.Surface
        actionRow.BackColor = theme.Surface
        emptyPanel.BackColor = theme.Surface
        statusRow.BackColor = theme.Surface
        list.BackColor = theme.EditorBack
        list.ForeColor = theme.EditorText
        list.BorderStyle = If(theme.Dark, BorderStyle.None, BorderStyle.Fixed3D)
        summary.ForeColor = theme.Muted
        hint.ForeColor = theme.Muted
        status.ForeColor = theme.Muted
        For Each command As Button In ActionRowButtons().Concat({emptyAddButton})
            command.FlatStyle = FlatStyle.Flat
            command.BackColor = theme.Surface
            command.ForeColor = theme.Text
            command.FlatAppearance.BorderSize = 1
            command.FlatAppearance.BorderColor = theme.Border
            command.FlatAppearance.MouseOverBackColor = Blend(theme.Surface, theme.Text, 0.08F)
            command.FlatAppearance.MouseDownBackColor = Blend(theme.Surface, theme.Text, 0.16F)
        Next
        If openButton IsNot Nothing Then
            openButton.BackColor = theme.Accent
            openButton.ForeColor = theme.OnAccent
            openButton.FlatAppearance.BorderColor = theme.Accent
            openButton.FlatAppearance.MouseOverBackColor = Blend(theme.Accent, theme.OnAccent, 0.12F)
            openButton.FlatAppearance.MouseDownBackColor = Blend(theme.Accent, theme.OnAccent, 0.22F)
            StylePrimaryOpen(openButton.Enabled)
        End If
        ' The painted menu follows the same scheme; a stale theme is simply replaced.
        menuTheme = Nothing
        devices?.ApplyPalette(theme)
        RefreshGlyphImages()
        list.Invalidate()
    End Sub

    ''' <summary>The primary Open at reduced strength while it is disabled (the 124 disabled
    ''' treatment): an accent that stays full when the button cannot be pressed reads as an
    ''' invitation, and the palette keeps the difference visible in both themes.</summary>
    Private Sub StylePrimaryOpen(enabled As Boolean)
        If openButton Is Nothing OrElse palette Is Nothing Then Return
        Dim face As Color = If(enabled, palette.Accent, Blend(palette.Accent, palette.Surface, 0.5F))
        openButton.BackColor = face
        openButton.ForeColor = If(enabled, palette.OnAccent, Blend(palette.OnAccent, palette.Surface, 0.55F))
        openButton.FlatAppearance.BorderColor = face
    End Sub

    ''' <summary>Blends towards <paramref name="b"/> by t. The channels are widened before
    ''' subtracting: VB keeps Byte arithmetic in Byte, and 0 - 255 in the checked default
    ''' would overflow exactly when a light theme blends towards black.</summary>
    Private Shared Function Blend(a As Color, b As Color, t As Single) As Color
        Return Color.FromArgb(255,
            Math.Max(0, Math.Min(255, CInt(CInt(a.R) + (CInt(b.R) - CInt(a.R)) * t))),
            Math.Max(0, Math.Min(255, CInt(CInt(a.G) + (CInt(b.G) - CInt(a.G)) * t))),
            Math.Max(0, Math.Min(255, CInt(CInt(a.B) + (CInt(b.B) - CInt(a.B)) * t))))
    End Function

    ''' <summary>The dark header is painted here, not asked from the native theme: the
    ''' DarkMode sub-theme darkens the strip but kept it a light island on the review's
    ''' screenshot, so the palette draws the strip, the hairline under it and the muted
    ''' caption itself. In the light theme the header stays native.</summary>
    Private Sub OnDrawColumnHeader(sender As Object, e As DrawListViewColumnHeaderEventArgs)
        If palette Is Nothing OrElse Not palette.Dark Then
            e.DrawDefault = True
            Return
        End If
        Using back As New SolidBrush(palette.Surface)
            e.Graphics.FillRectangle(back, e.Bounds)
        End Using
        Using line As New Pen(palette.Border)
            e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right - 1, e.Bounds.Bottom - 1)
        End Using
        Dim flags As TextFormatFlags = TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or
                                       TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPadding
        If RightToLeft = RightToLeft.Yes Then flags = flags Or TextFormatFlags.RightToLeft
        Dim bounds As Rectangle = e.Bounds
        bounds.Inflate(-Dv(6), 0)
        TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, bounds, palette.Muted, flags)
    End Sub

    ''' <summary>Keep the native ListView selection and accessibility, but paint its cells
    ''' in the host palette. Native inactive selection otherwise leaves a light strip in
    ''' dark mode, and native image-list drawing can put a light tile behind a mono glyph.</summary>
    Private Sub OnDrawSubItem(sender As Object, e As DrawListViewSubItemEventArgs)
        If palette Is Nothing Then
            e.DrawDefault = True
            Return
        End If
        Dim back As Color = If(e.Item.Selected,
            Blend(palette.EditorBack, palette.Accent, If(list.Focused, 0.35F, 0.2F)), palette.EditorBack)
        Using fill As New SolidBrush(back)
            e.Graphics.FillRectangle(fill, e.Bounds)
        End Using
        Dim bounds As Rectangle = e.Bounds
        bounds.Inflate(-Dv(6), 0)
        Dim rtl As Boolean = RightToLeft = RightToLeft.Yes
        Dim flags As TextFormatFlags = TextFormatFlags.SingleLine Or TextFormatFlags.VerticalCenter Or
            TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPadding Or TextFormatFlags.NoPrefix
        If rtl Then flags = flags Or TextFormatFlags.RightToLeft
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, bounds, palette.EditorText, flags)
        If e.Item.Focused AndAlso list.Focused Then
            Using pen As New Pen(palette.Accent)
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Top)
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right - 1, e.Bounds.Bottom - 1)
            End Using
        End If
    End Sub

    ''' <summary>Ids removed while this panel was open, so a host can close one that is on screen.</summary>
    Friend ReadOnly Property RemovedIds As IReadOnlyList(Of String)
        Get
            Return removed
        End Get
    End Property

    ''' <summary>Re-reads every caption, tooltip and accessible name after a language switch:
    ''' the panel outlives the switch inside the reused Settings window, so its texts cannot
    ''' stay with the language it was built in.</summary>
    Friend Sub Localize()
        RightToLeft = If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No)
        list.RightToLeftLayout = RightToLeft = RightToLeft.Yes
        summary.TextAlign = If(RightToLeft = RightToLeft.Yes, ContentAlignment.MiddleRight, ContentAlignment.MiddleLeft)
        status.TextAlign = summary.TextAlign
        addButton.Text = Localization.T("Добавить..")
        editButton.Text = Localization.T("Изменить..")
        removeButton.Text = Localization.T("Удалить")
        testButton.Text = Localization.T("Проверить")
        emptyAddButton.Text = Localization.T("Добавить..")
        If openButton IsNot Nothing Then openButton.Text = Localization.T("Открыть")
        moreButton.AccessibleName = Localization.T("Ещё")
        hint.Text = Localization.T("Здесь пока нет ресурсов. Добавьте сервер вручную или импортируйте файл настроек из Share Manager (.fmscfg).")
        list.AccessibleName = Localization.T("Ресурсы")
        devices?.Localize()
        Dim captions As String() = {Localization.T("Название"), Localization.T("Тип"), Localization.T("Адрес"),
                                    Localization.T("Доступ"), Localization.T("Открывался")}
        For index As Integer = 0 To Math.Min(captions.Length, list.Columns.Count) - 1
            list.Columns(index).Text = captions(index)
        Next
        For Each item As ListViewItem In list.Items
            UpdateRowPresentation(item, DirectCast(item.Tag, ResourceDescriptor))
        Next
        UpdateSummary()
        UpdateButtons()
        Relayout()
    End Sub

    ''' <summary>Starts the "new resource" form with <paramref name="draft"/> filled in. The manager
    ''' dialog calls it from OnShown for a typed sftp:// address.</summary>
    Friend Sub BeginAdd(draft As ResourceDescriptor)
        AddOrEdit(Nothing, draft)
    End Sub

    ' ---------------------------------------------------------------------------- list ----

    ''' <summary>The list's source: the user's registered resources plus the local SFTP
    ''' server's shares as mirror rows (<see cref="WorkerShares"/>). The real hive keeps its
    ''' own recovery path (<see cref="ResourceRegistry.Load"/>); an injected reader is the
    ''' tests' in-memory registry, and an injected share source the tests' share list - a
    ''' custom hive must never make the panel read the machine's real shares.</summary>
    Private Function LoadResources() As List(Of ResourceDescriptor)
        Dim registered As List(Of ResourceDescriptor) =
            If(customStore, ResourceRegistry.Parse(hiveRead(ResourceRegistry.Value_Name)), ResourceRegistry.Load())
        Dim managed As IEnumerable(Of ResourceDescriptor) = Nothing
        If managedShareSource IsNot Nothing Then
            managed = managedShareSource()
        ElseIf Not customStore Then
            managed = WorkerShares.Load()
        End If
        Return WorkerShares.Combined(registered, managed)
    End Function

    ''' <summary>The rows carry no leading type glyph: the Type column already says what a
    ''' resource is, and the picture only took row height and a leading inset away.</summary>
    Private Sub Refill(Optional select_Id As String = Nothing)
        list.BeginUpdate()
        Try
            list.Items.Clear()
            ' The most recently used first: the one opened an hour ago is the one wanted again.
            Dim ordered As IEnumerable(Of ResourceDescriptor) = LoadResources().
                OrderByDescending(Function(r) If(r.LastUsedUtc, DateTime.MinValue)).
                ThenBy(Function(r) r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            For Each r As ResourceDescriptor In ordered
                Dim item As New ListViewItem(r.DisplayName) With {.Tag = r}
                UpdateRowPresentation(item, r)
                list.Items.Add(item)
                If select_Id IsNot Nothing AndAlso String.Equals(r.Id, select_Id, StringComparison.OrdinalIgnoreCase) Then item.Selected = True
            Next
            If list.SelectedItems.Count = 0 AndAlso list.Items.Count > 0 Then list.Items(0).Selected = True
        Finally
            list.EndUpdate()
        End Try
        Dim empty As Boolean = list.Items.Count = 0
        emptyPanel.Visible = empty
        list.Visible = Not empty
        UpdateSummary()
        UpdateButtons()
        Relayout()
        ClearShownNotices(list.Items.Cast(Of ListViewItem)().Select(Function(i) DirectCast(i.Tag, ResourceDescriptor)))
    End Sub

    Private Sub UpdateRowPresentation(item As ListViewItem, resource As ResourceDescriptor)
        Dim typeText As String
        If WorkerShares.IsManagedDisk(resource) Then
            typeText = Localization.T(ManagedDiskTypeLabelKey)
        ElseIf WorkerShares.IsManaged(resource) Then
            typeText = Localization.T(ManagedTypeLabelKey)
        ElseIf ResourceOrigin.IsExchange(resource) Then
            ' Ticket 171: a resource attached from "My devices" says whose it is, what it is now and - once - that its access was updated
            typeText = ExchangeResourceText.TypeOfDevice(resource.ExchangeDeviceName)
            Dim stateText As String = If(devices Is Nothing, "", ExchangeResourceText.ListState(devices.StateOf(resource)))
            If stateText.Length > 0 Then typeText &= " - " & stateText
            If resource.ExchangeNotice.GetValueOrDefault() Then typeText &= " - " & ExchangeResourceText.AccessUpdatedCell()
        ElseIf ResourceOrigin.IsLink(resource) Then
            typeText = ExchangeResourceText.TypeOfLink()
        Else
            typeText = ResourceMessages.KindLabel(resource.Kind)
        End If
        Dim values As String() = {resource.DisplayName, typeText,
            ResourceMessages.DisplayAddress(resource),
            If(resource.ReadOnlyHint, Localization.T("только чтение"), Localization.T("чтение и запись")),
            If(resource.LastUsedUtc.HasValue, resource.LastUsedUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), "")}
        While item.SubItems.Count < values.Length
            item.SubItems.Add("")
        End While
        For index As Integer = 0 To values.Length - 1
            item.SubItems(index).Text = values(index)
        Next
        item.ToolTipText = String.Join(" - ", values.Where(Function(part) part.Length > 0))
    End Sub

    Private Function Selected() As ResourceDescriptor
        Return If(list.SelectedItems.Count = 0, Nothing, DirectCast(list.SelectedItems(0).Tag, ResourceDescriptor))
    End Function

    ''' <summary>The resource of the selected row, or Nothing - what a host's own Open needs.</summary>
    Friend ReadOnly Property SelectedResource As ResourceDescriptor
        Get
            Return Selected()
        End Get
    End Property

    Private Sub UpdateSummary()
        summary.Text = If(list.Items.Count = 0, "", Localization.TF("Ресурсов: {0}", list.Items.Count))
    End Sub

    ''' <summary>Disabled-with-reason (131 §3.2): a command that needs a selection says why in
    ''' its tooltip and in its accessible name, not by greying alone.</summary>
    Private Sub SetSelectionState(button As Button, hasSelection As Boolean)
        button.Enabled = hasSelection
        Dim reason As String = If(hasSelection, "", Localization.T(NoSelectionReasonKey))
        tips.SetToolTip(button, If(reason.Length = 0, Nothing, reason))
        button.AccessibleName = If(reason.Length = 0, button.Text, button.Text & " - " & reason)
    End Sub

    Private Sub UpdateButtons()
        Dim current As ResourceDescriptor = Selected()
        Dim has As Boolean = current IsNot Nothing
        SetSelectionState(editButton, has)
        SetSelectionState(removeButton, has)
        SetSelectionState(testButton, has)
        If openButton IsNot Nothing Then
            SetSelectionState(openButton, has)
            StylePrimaryOpen(has)
        End If
        ' A managed row's Edit and Remove stay ON - the click hands the user over to the
        ' Share Manager - so the tooltip and the accessible name say up front where the
        ' folder is really managed.
        If has AndAlso WorkerShares.IsManaged(current) Then
            Dim note As String = Localization.T(ManagedNoteKey)
            tips.SetToolTip(editButton, note)
            tips.SetToolTip(removeButton, note)
            editButton.AccessibleName = editButton.Text & " - " & note
            removeButton.AccessibleName = removeButton.Text & " - " & note
        End If
    End Sub

    ' ------------------------------------------------------------------------ status ----

    Friend Enum ResourceStatusSeverity
        None
        Busy
        Success
        Warning
        Danger
    End Enum

    Private Const NoSelectionReasonKey As String = "Сначала выберите ресурс в списке"
    Private Const ManagedTypeLabelKey As String = "Общая папка (Share Manager)"
    Private Const ManagedDiskTypeLabelKey As String = "Диск (Share Manager)"
    Private Const ManagedNoteKey As String = "Этой общей папкой управляет Share Manager"

    Private Sub Say(text As String, Optional severity As ResourceStatusSeverity = ResourceStatusSeverity.None)
        lastSeverity = severity
        status.Text = text
        ' Multi-part results stay on one line: joined with "; " above, ellipsized here, and the
        ' whole sentence lives in the tooltip (131 §3.4).
        tips.SetToolTip(status, If(text.Length = 0, Nothing, text))
        RefreshStatusGlyph()
        Relayout()
    End Sub

    ''' <summary>The leading severity glyph (131 §3.4): the catalogue's status mark in the
    ''' success/warning/danger role the host's palette carries - shape AND colour, never
    ''' colour alone. Without a host palette (the manager dialog) the roles fall back to the
    ''' contract's light defaults, the same values ThemePalette answers for the light scheme.</summary>
    Private Shared ReadOnly StatusSuccessFallback As Color = Color.ForestGreen
    Private Shared ReadOnly StatusWarningFallback As Color = Color.FromArgb(239, 108, 0)
    Private Shared ReadOnly StatusDangerFallback As Color = Color.Firebrick

    Private Sub RefreshStatusGlyph()
        Dim glyphId As String = ""
        Dim hue As Color = If(palette IsNot Nothing, palette.Muted, SystemColors.GrayText)
        Select Case lastSeverity
            Case ResourceStatusSeverity.Success
                glyphId = "status.ok"
                hue = If(palette IsNot Nothing, palette.Success, StatusSuccessFallback)
            Case ResourceStatusSeverity.Warning
                glyphId = "status.warning"
                hue = If(palette IsNot Nothing, palette.Warning, StatusWarningFallback)
            Case ResourceStatusSeverity.Danger
                glyphId = "status.error"
                hue = If(palette IsNot Nothing, palette.Danger, StatusDangerFallback)
            Case ResourceStatusSeverity.Busy
                glyphId = "status.unknown"
        End Select
        Dim previous As Image = statusGlyph.Image
        statusGlyph.Image = If(glyphId.Length = 0, Nothing, RenderGlyph(glyphId, hue, GlyphSize))
        If previous IsNot Nothing Then previous.Dispose()
        LayoutStatusRow(Math.Max(Dv(160), ClientSize.Width), Dv(StatusHeight))
    End Sub

    ''' <summary>Renders a catalogue glyph as a bitmap, or Nothing when the asset cannot be
    ''' read - a missing glyph must never take the panel down.</summary>
    Private Function RenderGlyph(glyphId As String, hue As Color, logicalSize As Integer) As Bitmap
        Try
            Return GlyphRenderer.Render(glyphId, Dv(logicalSize), hue, AddressOf RuntimeBootstrap.OpenBundledAsset)
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>The row-height image list is the honest way to a fixed logical row height
    ''' (131 §3.3): the Details view sizes its rows after the small image list. The rows carry
    ''' no leading glyph any more (the Type column says what a resource is), so the cells stay
    ''' blank and only the height matters. Rebuilt when the scale or the handle changes. The
    ''' PANEL's handle drives this, not the list's: with no resources the list stays hidden
    ''' and never creates a handle, and the empty state's own glyph would never arrive.</summary>
    Private Sub RefreshGlyphImages()
        Dim cellHeight As Integer = Dv(RowHeight)
        Dim images As New ImageList With {.ImageSize = New Size(1, cellHeight), .ColorDepth = ColorDepth.Depth32Bit}
        Dim old As ImageList = rowImages
        rowImages = images
        list.SmallImageList = images
        If old IsNot Nothing Then old.Dispose()
        ' The empty state and the overflow carry palette-inked glyphs too.
        Dim previousEmpty As Image = emptyGlyph.Image
        emptyGlyph.Image = RenderGlyph("source.resource", If(palette IsNot Nothing, palette.Muted, SystemColors.GrayText), EmptyGlyphSize)
        If previousEmpty IsNot Nothing Then previousEmpty.Dispose()
        RefreshMoreImage()
        RefreshStatusGlyph()
    End Sub

    Private Function RenderGlyphForList(glyphId As String, ink As Color, size As Integer) As Bitmap
        Try
            Return GlyphRenderer.Render(glyphId, size, ink, AddressOf RuntimeBootstrap.OpenBundledAsset)
        Catch
            Return Nothing
        End Try
    End Function

    Private Sub RefreshMoreImage()
        Using glyph As Bitmap = RenderGlyphForList("nav.more", If(palette IsNot Nothing, palette.Text, ForeColor), Dv(GlyphSize))
            If glyph IsNot Nothing Then
                moreButton.Text = ""
                Dim previous As Image = moreButton.Image
                moreButton.Image = DirectCast(glyph.Clone(), Bitmap)
                If previous IsNot Nothing Then previous.Dispose()
            Else
                ' No catalogue asset readable (the tests' world): the typographic fallback.
                moreButton.Text = "⋯"
            End If
        End Using
    End Sub

    ' -------------------------------------------------------------------------- actions ----

    Private Sub OnAdd(sender As Object, e As EventArgs)
        AddOrEdit(Nothing, Nothing)
    End Sub

    Private Sub OnEdit(sender As Object, e As EventArgs)
        Dim current As ResourceDescriptor = Selected()
        If current Is Nothing Then Return
        If HandOverToShareManager(current) Then Return
        AddOrEdit(current, Nothing)
    End Sub

    ''' <summary>A share of the local SFTP server is the Share Manager's business: its Edit
    ''' and Remove become a handover - the mirror is neither changed nor deleted here. True
    ''' when the row was one.</summary>
    Private Function HandOverToShareManager(current As ResourceDescriptor) As Boolean
        If Not WorkerShares.IsManaged(current) Then Return False
        Say(Localization.T("Этой общей папкой управляет Share Manager - открываю его.."))
        If manageSharesAction IsNot Nothing Then manageSharesAction()
        Return True
    End Function

    Private Sub OnOpen(sender As Object, e As EventArgs)
        Dim chosen As ResourceDescriptor = Selected()
        If chosen IsNot Nothing Then RaiseEvent ItemActivated(chosen)
    End Sub

    Private Sub AddOrEdit(existing As ResourceDescriptor, draft As ResourceDescriptor)
        Dim owner As IWin32Window = If(FindForm(), DirectCast(Me, IWin32Window))
        Using dialog As New Resource_Edit_Form(existing, draft)
            If DialogLayer.ShowViewerDialog(dialog, owner) <> DialogResult.OK Then Return
            Dim result As ResourceCommitResult = ResourceCommit.Commit(dialog.Result, dialog.NewPassword, hiveRead, hiveWrite, secrets)
            If Not result.Succeeded Then
                AppFileLogger.WriteLine("Resource: save failed - " & result.Outcome.ToString())
                ViewerBox.Show(owner, Localization.T("Не удалось сохранить ресурс. Ничего не потеряно: то, что было сохранено раньше, осталось как было"),
                                dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If
            Refill(dialog.Result.Id)
            RaiseEvent ResourcesChanged(removed)
        End Using
    End Sub

    ''' <summary>The dialog's own question, the default answer No. Injectable for the tests.</summary>
    Private Function AskRemoveConfirmation(current As ResourceDescriptor) As Boolean
        Dim owner As IWin32Window = If(FindForm(), DirectCast(Me, IWin32Window))
        Return ViewerBox.Show(owner,
                               Localization.TF("Удалить ресурс «{0}»? Сохранённый для него пароль тоже будет удалён. Файлы на сервере не затрагиваются.", current.DisplayName),
                               If(FindForm()?.Text, Localization.T("Ресурсы")),
                               MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.Yes
    End Function

    ''' <summary>"My devices" attached or followed a resource: show it, and tell the host the registry moved.</summary>
    Private Sub OnDevicesChangedResources()
        Refill(If(Selected() Is Nothing, Nothing, Selected().Id))
        RaiseEvent ResourcesChanged(removed)
    End Sub

    ''' <summary>An untick of an attached resource under "My devices": the user's own removal, the same commit as the Remove button's (the
    ''' registry first, the credential afterwards). The confirmation was asked by the section.</summary>
    Private Sub RemoveExchangeResource(resource As ResourceDescriptor)
        Dim result As ResourceCommitResult = ResourceCommit.Remove(resource.Id, hiveRead, hiveWrite, secrets)
        If result.Succeeded Then
            removed.Add(resource.Id)
            Say("")
        Else
            Say(Localization.T("Не удалось удалить ресурс"), ResourceStatusSeverity.Danger)
        End If
    End Sub

    ''' <summary>"Told once": a row that carried the notice that its producer updated its access has now been shown, so the flag goes. The
    ''' registry is only touched when a flag was set (and never in an injected world).</summary>
    Private Sub ClearShownNotices(shown As IEnumerable(Of ResourceDescriptor))
        If customStore OrElse Not shown.Any(Function(r) r.ExchangeNotice.GetValueOrDefault()) Then Return
        Try
            Dim current As List(Of ResourceDescriptor) = ResourceRegistry.Load()
            Dim changed As Boolean
            For Each r As ResourceDescriptor In current
                If ResourceOrigin.IsExchange(r) AndAlso r.ExchangeNotice.GetValueOrDefault() Then
                    r.ExchangeNotice = Nothing
                    changed = True
                End If
            Next
            If changed Then ResourceRegistry.Save(current)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is InvalidOperationException
            AppFileLogger.WriteLine("Resource: a notice could not be cleared - " & ex.GetType().Name)
        End Try
    End Sub

    Private Sub OnRemove(sender As Object, e As EventArgs)
        Dim current As ResourceDescriptor = Selected()
        If current Is Nothing Then Return
        If HandOverToShareManager(current) Then Return
        If Not confirmRemove(current) Then Return
        Dim result As ResourceCommitResult = ResourceCommit.Remove(current.Id, hiveRead, hiveWrite, secrets)
        If result.Succeeded Then
            removed.Add(current.Id)
            Say("")
            RaiseEvent ResourcesChanged(removed)
        Else
            Say(Localization.T("Не удалось удалить ресурс"), ResourceStatusSeverity.Danger)
        End If
        Refill()
    End Sub

    Private Async Sub OnTest(sender As Object, e As EventArgs)
        Dim current As ResourceDescriptor = Selected()
        If current Is Nothing Then Return
        ' A managed row is a folder of this machine: the honest test is the folder itself,
        ' right now - no credential read, no dial. "Not there" and "not for this user"
        ' are different sentences: a service-held FileDO disk is mounted with FileDO's
        ' private SYSTEM-only DACL, and the share itself may be perfectly alive.
        If WorkerShares.IsManaged(current) Then
            Select Case WorkerShares.ClassifyFolder(current.RootPath)
                Case WorkerShares.FolderPresence.Present
                    Say(Localization.T("Папка открывается"), ResourceStatusSeverity.Success)
                Case WorkerShares.FolderPresence.Denied
                    Say(Localization.T("Нет доступа к папке на этом компьютере - она смонтирована службой. Проверьте общую папку в Share Manager"),
                        ResourceStatusSeverity.Warning)
                Case Else
                    Say(Localization.T("Папки нет на диске - проверьте общую папку в Share Manager"), ResourceStatusSeverity.Danger)
            End Select
            Return
        End If
        testCancel?.Cancel()
        testCancel?.Dispose()
        Dim cancel As New CancellationTokenSource()
        testCancel = cancel
        testButton.Enabled = False
        Say(Localization.T("Проверка.."), ResourceStatusSeverity.Busy)
        Try
            Dim sentence As String
            Dim severity As ResourceStatusSeverity
            If current.Kind = ResourceKind.Sftp Then
                ' Every address is asked, in the producer's order: a stale LAN address next to a live
                ' port forward is the ordinary state of an imported resource, and saying only the
                ' first verdict would call a working resource broken. The overall severity is the
                ' best news the addresses carry: one live SSH answers for the resource, an open
                ' port that is not SSH is a warning, silence is the failure (131 §3.4).
                Dim answers As New List(Of String)()
                Dim reachable As Boolean = False
                Dim openPortOnly As Boolean = False
                For Each endpoint As FmsEndpoint In ResourceConnect.CandidatesOf(current).Distinct(New EndpointKeyComparer())
                    Dim verdict As ProbeVerdict = Await ResourceProbe.ProbeSftpAsync(endpoint.Host, endpoint.Port, cancel.Token)
                    If cancel.IsCancellationRequested Then Return
                    answers.Add(endpoint.Host & ":" & endpoint.Port.ToString(CultureInfo.InvariantCulture) & " - " & ResourceProbe.VerdictSentence(verdict))
                    If verdict = ProbeVerdict.SshOk Then reachable = True
                    If verdict = ProbeVerdict.PortOpen Then openPortOnly = True
                Next
                sentence = String.Join("; ", answers)
                severity = If(reachable, ResourceStatusSeverity.Success, If(openPortOnly, ResourceStatusSeverity.Warning, ResourceStatusSeverity.Danger))
            Else
                Dim outcome As TestOutcome = Await TestShareAsync(current, cancel.Token)
                sentence = outcome.Sentence
                severity = outcome.Severity
            End If
            If Not cancel.IsCancellationRequested Then Say(sentence, severity)
        Finally
            If testCancel Is cancel Then SetSelectionState(testButton, Selected() IsNot Nothing)
        End Try
    End Sub

    ''' <summary>A sentence and the severity it is said with: the status line's glyph must
    ''' match the outcome, so the test carries both back instead of the caller parsing text.</summary>
    Private NotInheritable Class TestOutcome
        Public Sentence As String
        Public Severity As ResourceStatusSeverity
    End Class

    Private NotInheritable Class EndpointKeyComparer
        Implements IEqualityComparer(Of FmsEndpoint)
        Public Overloads Function Equals(x As FmsEndpoint, y As FmsEndpoint) As Boolean Implements IEqualityComparer(Of FmsEndpoint).Equals
            Return String.Equals(x.Key(), y.Key(), StringComparison.Ordinal)
        End Function
        Public Overloads Function GetHashCode(obj As FmsEndpoint) As Integer Implements IEqualityComparer(Of FmsEndpoint).GetHashCode
            Return obj.Key().GetHashCode()
        End Function
    End Class

    ''' <summary>A share is tested the only honest way: by opening it and reading its top folder, with the
    ''' stored credential, then letting go of the connection.</summary>
    Private Async Function TestShareAsync(current As ResourceDescriptor, ct As CancellationToken) As Task(Of TestOutcome)
        Dim credential As CredentialReadResult = ResourceCommit.ReadCredential(current, secrets)
        If credential.Outcome <> CredentialReadOutcome.Ok Then
            Return New TestOutcome With {.Sentence = Localization.T("Пароль не сохранён: введите его через «Изменить..»"),
                                         .Severity = ResourceStatusSeverity.Warning}
        End If
        Dim outcome As ConnectOutcome = Await ResourceConnect.ConnectAsync(current, credential.Secret, Nothing, Nothing, Nothing, ct)
        If Not outcome.Succeeded Then
            Return New TestOutcome With {.Sentence = ResourceMessages.ForFailure(outcome.Failure),
                                         .Severity = ResourceStatusSeverity.Danger}
        End If
        Try
            Await Task.Run(Sub() outcome.Provider.ListEntries(current.RootPath, ct))
            Return New TestOutcome With {.Sentence = Localization.T("Папка открывается"),
                                         .Severity = ResourceStatusSeverity.Success}
        Catch ex As Exception
            Return New TestOutcome With {.Sentence = ResourceMessages.ForFailure(ResourceFailure.FromException(ex)),
                                         .Severity = ResourceStatusSeverity.Danger}
        Finally
            DirectCast(outcome.Provider, IDisposable)?.Dispose()
        End Try
    End Function

    ' ------------------------------------------------------------------------ overflow ----

    ''' <summary>The overflow's rows (131 §3.2): Export beside the two imports, behind the 125
    ''' painted menu. Export needs a selection and says why when it has none; the imports never
    ''' do. The rows carry no key hints - no hotkey action exists for them, and the 125 rule
    ''' shows a hint only for an action the live map answers for. Friend so the tests can read
    ''' the offer without opening a popup.</summary>
    Friend Function BuildOverflowItems() As List(Of ChromeMenuItem)
        Dim hasSelection As Boolean = Selected() IsNot Nothing
        Dim reason As String = If(hasSelection, "", Localization.T(NoSelectionReasonKey))
        ' Exporting a managed share is the Share Manager's job (its wizard builds the phone's
        ' .fmscfg), so the row is refused with the handover as the reason.
        Dim managedNote As String = If(hasSelection AndAlso WorkerShares.IsManaged(Selected()), Localization.T(ManagedNoteKey), "")
        Dim exportReason As String = If(managedNote.Length > 0, managedNote, reason)
        Dim export As New ChromeMenuItem("res_Export") With {
            .GlyphId = "action.share", .Caption = Localization.T("Экспорт.."),
            .AccessibleName = If(exportReason.Length = 0, Localization.T("Экспорт.."), Localization.TF("{0} - {1}", Localization.T("Экспорт.."), exportReason)),
            .Enabled = exportReason.Length = 0, .DisabledReason = exportReason}
        If hasSelection Then export.Action = Sub() OnExport(Nothing, EventArgs.Empty)
        Dim importFile As New ChromeMenuItem("res_ImportFile") With {
            .GlyphId = "action.choose-file", .Caption = Localization.T("Импорт из файла.."),
            .AccessibleName = Localization.T("Импорт из файла..")}
        importFile.Action = Sub() OnImportFile(Nothing, EventArgs.Empty)
        Dim importClipboard As New ChromeMenuItem("res_ImportClipboard") With {
            .GlyphId = "action.copy", .Caption = Localization.T("Импорт из буфера"),
            .AccessibleName = Localization.T("Импорт из буфера")}
        importClipboard.Action = Sub() OnImportClipboard(Nothing, EventArgs.Empty)

        Dim items As New List(Of ChromeMenuItem) From {export, importFile, importClipboard}

        ' Spec 227 rule 2: "Scan with the camera.." lives in the Share Manager's add-resource manager;
        ' the panel offers the hand-over only when a camera door was handed in, the Share Manager is
        ' installed and a camera is present. With either missing the item is absent, not disabled.
        If scanCameraAction IsNot Nothing AndAlso ShareComponentInstalled() AndAlso CameraPresence.IsCameraPresent() Then
            Dim scanCamera As New ChromeMenuItem("res_ScanCamera") With {
                .GlyphId = "device.phone", .Caption = Localization.T("Сканировать камерой.."),
                .AccessibleName = Localization.T("Сканировать камерой..")}
            scanCamera.Action = Sub() OnScanCamera(Nothing, EventArgs.Empty)
            items.Add(scanCamera)
        End If

        ' Spec 227 rule 3: a QR the phone scans, carrying a one-time receive address for its code
        Dim showPhoneCode As New ChromeMenuItem("res_ShowPhoneCode") With {
            .GlyphId = "action.share", .Caption = Localization.T("Показать код для телефона.."),
            .AccessibleName = Localization.T("Показать код для телефона..")}
        showPhoneCode.Action = Sub() OnShowPhoneCode(Nothing, EventArgs.Empty)
        items.Add(showPhoneCode)

#If Not NETFRAMEWORK Then
        If hasSelection Then
            Dim sel = Selected()
            If sel IsNot Nothing Then
                Dim diagnoseItem As New ChromeMenuItem("res_Diagnose") With {
                    .GlyphId = "action.test-connection",
                    .Caption = ConnectionDiagnosisLocalization.DoorButtonText(Localization.CurrentCultureLanguage),
                    .AccessibleName = ConnectionDiagnosisLocalization.DoorButtonText(Localization.CurrentCultureLanguage)
                }
                diagnoseItem.Action = Sub()
                    Dim ctx As New ConnectionDiagnosisContext With {
                        .Host = sel.Host,
                        .Port = sel.Port,
                        .Language = Localization.CurrentCultureLanguage,
                        .LastMessageCode = ConnectionDiagnosisMessageCode.UnreachableServer
                    }
                    Dim ignored = Connection_Diagnosis_Dialog.RunAndShowAsync(FindForm(), ctx)
                End Sub
                items.Add(diagnoseItem)
            End If
        End If
#End If

        Return items
    End Function

    ''' <summary>The host's installed probe, or the panel's own file probe when no host one was handed
    ''' in (the tests construct without one) - the same probe spec 215 rule 3 uses.</summary>
    Private Function ShareComponentInstalled() As Boolean
        If shareInstalledSource IsNot Nothing Then
            Try
                Return shareInstalledSource()
            Catch
                Return False
            End Try
        End If
        Try
            Dim baseDir As String = AppDomain.CurrentDomain.BaseDirectory
            Dim companionPath As String = System.IO.Path.Combine(baseDir, "FastMediaSorterCompanion.exe")
            If System.IO.File.Exists(companionPath) Then Return True
            Dim parentDir As String = System.IO.Path.GetDirectoryName(baseDir.TrimEnd(System.IO.Path.DirectorySeparatorChar))
            If Not String.IsNullOrEmpty(parentDir) Then
                companionPath = System.IO.Path.Combine(parentDir, "FastMediaSorterCompanion.exe")
                If System.IO.File.Exists(companionPath) Then Return True
            End If
            Return False
        Catch
            Return False
        End Try
    End Function

    Private Sub OnMore(sender As Object, e As EventArgs)
        Dim owner As Form = FindForm()
        If owner Is Nothing Then Return
        If menuTheme Is Nothing Then
            menuTheme = ChromeTheme.FromScheme(If(palette IsNot Nothing, palette.Surface, SystemColors.Control),
                                               If(palette IsNot Nothing, palette.Text, SystemColors.ControlText),
                                               SystemInformation.HighContrast)
        End If
        ChromeMenu.ShowPopup(owner, moreButton.PointToScreen(New Point(0, moreButton.Height)), BuildOverflowItems(),
                             menuTheme, panelScale, RightToLeft = RightToLeft.Yes, Font, Font)
    End Sub

    ' --------------------------------------------------------------------------- import ----

    ''' <summary>The one supported intake file in a drop, or Nothing - the door takes one file at a
    ''' time, the first of the list that names something the intake reads.</summary>
    Private Shared Function DroppedIntakeFile(e As DragEventArgs) As String
        If e Is Nothing OrElse Not e.Data.GetDataPresent(DataFormats.FileDrop) Then Return Nothing
        Dim paths As String() = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If paths Is Nothing Then Return Nothing
        For Each path As String In paths
            If PhoneConnectionIntake.IsSupportedFile(path) Then Return path
        Next
        Return Nothing
    End Function

    Private Sub OnImportFile(sender As Object, e As EventArgs)
        Dim owner As IWin32Window = If(FindForm(), DirectCast(Me, IWin32Window))
        Using dialog As New OpenFileDialog With {
            .Title = Localization.T("Импорт ресурса"),
            .Filter = Localization.T("Файлы настроек и изображений QR (*.fmscfg;*.json;*.txt;*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.fmscfg;*.json;*.txt;*.png;*.jpg;*.jpeg;*.bmp;*.webp|Файлы настроек (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|Изображения с QR-кодом (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|Все файлы (*.*)|*.*"),
            .CheckFileExists = True}
            If dialog.ShowDialog(owner) <> DialogResult.OK Then Return
            ImportAndRefresh(PhoneConnectionIntake.ReadFile(dialog.FileName))
        End Using
    End Sub

    Private Sub OnImportClipboard(sender As Object, e As EventArgs)
        Dim read As FmsConfigReadResult = PhoneConnectionIntake.ReadClipboard()
        If read Is Nothing OrElse (Not read.Succeeded AndAlso read.Failure = FmsConfigFailure.Empty) Then
            ' An image that held no QR, or too many, carries the decoder's own sentence in Detail; only a
            ' clipboard with neither text nor a picture gets the generic one.
            Dim sentence As String = If(read IsNot Nothing AndAlso Not String.IsNullOrEmpty(read.Detail), read.Detail,
                                        Localization.T("В буфере обмена нет текста или изображения"))
            Say(sentence, ResourceStatusSeverity.Warning)
            Return
        End If
        ImportAndRefresh(read)
    End Sub

    ''' <summary>Spec 227 rule 2: hand the camera scan over to the Share Manager's add-resource flow
    ''' (the program that owns the camera code) through the host's delegate. Never a silent no-op:
    ''' the item is only built when a delegate exists.</summary>
    Private Sub OnScanCamera(sender As Object, e As EventArgs)
        scanCameraAction?.Invoke()
    End Sub

    Private Sub OnShowPhoneCode(sender As Object, e As EventArgs)
        Dim owner As IWin32Window = If(FindForm(), DirectCast(Me, IWin32Window))
        Using form As New Resource_Phone_Code_Form()
            If DialogLayer.ShowViewerDialog(form, owner) = DialogResult.OK AndAlso Not String.IsNullOrWhiteSpace(form.ReceivedCode) Then
                ImportAndRefresh(PhoneConnectionIntake.ReadText(form.ReceivedCode))
            End If
        End Using
    End Sub

    Private Sub ImportAndRefresh(read As FmsConfigReadResult)
        Dim owner As IWin32Window = If(FindForm(), DirectCast(Me, IWin32Window))
        Dim stored As IReadOnlyList(Of String) = ResourceImportFlow.Run(owner, read)
        If stored.Count > 0 Then
            Say(Localization.TF("Добавлено или обновлено ресурсов: {0}", stored.Count), ResourceStatusSeverity.Success)
            Refill()
            RaiseEvent ResourcesChanged(removed)
        End If
    End Sub

    ' --------------------------------------------------------------------------- export ----

    Private Sub OnExport(sender As Object, e As EventArgs)
        Dim current As ResourceDescriptor = Selected()
        If current Is Nothing Then Return
        Dim owner As IWin32Window = If(FindForm(), DirectCast(Me, IWin32Window))
        Using dialog As New Resource_Export_Form(current, secrets)
            DialogLayer.ShowViewerDialog(dialog, owner)
        End Using
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then
            testCancel?.Cancel()
            testCancel?.Dispose()
            tips.Dispose()
            rowImages?.Dispose()
            statusGlyph.Image?.Dispose()
            emptyGlyph.Image?.Dispose()
            moreButton.Image?.Dispose()
        End If
    End Sub

End Class
#End If
