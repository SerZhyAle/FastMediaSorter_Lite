#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' The mainline F3 panel: the triage surface of 005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md.
'
' What changed, and what did not:
'   * It no longer builds a file list of its own. It asks the main window for the one it has
'     (SnapshotFileList), so the order, the filter and the metadata are the main window's by
'     construction and video, audio and documents are no longer invisible here (PG-2).
'   * It no longer sorts, copies, moves or deletes. A digit or DEL builds a plan and hands it
'     to the main window's batch engine (Main_Form.StartBatchAsync), which is the same loop,
'     the same gates and the same undo stack as a key press there (PG-1). Ctrl+Z here is the
'     main window's U.
'   * The selection is a SelectionModel; the grid and the details list are two views of it
'     (PG-4, PG-5).
'   * Thumbnails come from the shell, then our cache, then our own decode - for the cards in
'     view only (PG-3).
'   * A run shows a strip at the bottom: progress, Cancel, and afterwards the report, which
'     opens a details window on a click (PG-6). Nothing in the panel is ever modal because of it.
'
' The x86 fallback keeps the original panel verbatim (Image_Panel_Form.vb, inside
' #If NETFRAMEWORK): the maintenance policy freezes it, and it has no batch engine to call.

Partial Public Class Image_Panel_Form

    Private Const first_Run_Top As Integer = 50
    Private Const first_Run_Left As Integer = 50
    Private Const first_Run_Width As Integer = 800
    Private Const first_Run_Height As Integer = 600
    Private Const Limit_Width_Low As Integer = 320
    Private Const Limit_Width_High As Integer = 3000
    Private Const Limit_Height_Low As Integer = 240
    Private Const Limit_Height_High As Integer = 3000

    ''' <summary>What the bitmaps in view may occupy. About 380 cards at the largest size - far more
    ''' than any screen shows - and bounded in BYTES because a card is sixteen times heavier at
    ''' 256 px than at 64.</summary>
    Private Const Memory_Budget_Bytes As Long = 96L * 1024L * 1024L

    ' --- controls ------------------------------------------------------------------
    Private toolbar As ToolStrip
    Private btn_ViewMode As ToolStripButton
    Private cmb_CardSize As ToolStripComboBox
    Private btn_SelectAll As ToolStripButton
    Private lbl_Selection As ToolStripLabel
    Private grid As ThumbnailGrid
    Private details As DetailsList
    Private strip As Panel
    Private lbl_Strip As Label
    Private btn_Cancel As Button
    ''' <summary>The "an operation is running" band (126 UX-608): shown when a close is
    ''' attempted during a run, because a window that only beeps teaches nothing - the
    ''' user does not learn that an operation is running, or that Esc cancels it.</summary>
    Private busy_Band As Panel
    Private lbl_Busy As Label
    Private tip As ToolTip
    Private ReadOnly flush_Timer As New System.Windows.Forms.Timer() With {.Interval = 80}

    ' --- state -----------------------------------------------------------------------
    Private ReadOnly model As New SelectionModel()
    Private rows As New List(Of PanelFileRow)()
    Private display As New List(Of PanelFileRow)()
    Private ReadOnly row_By_Path As New Dictionary(Of String, PanelFileRow)(StringComparer.OrdinalIgnoreCase)
    Private sort_Column As PanelSortColumn = PanelSortColumn.None
    Private sort_Descending As Boolean
    Private target_File As String = ""
    Private view_Is_List As Boolean
    Private thumb_Px As Integer = 128

    Private memory As ThumbnailMemoryCache
    Private ReadOnly no_Picture As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private loader As PanelThumbnailLoader
    Private session As Integer
    Private thumb_Dirty As Boolean
    Private ReadOnly pending_Removed As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    Private events_Wired As Boolean
    Private last_Report As BatchReport
    Private run_Is_Active As Boolean
    Private has_Remembered_Placement As Boolean

    ''' <summary>The remembered rectangle as stored: the size in device pixels at <see cref="remembered_Dpi"/>
    ''' (0 = not recorded), and the raw position. <see cref="ApplyRememberedBounds"/> turns them into the window's.</summary>
    Private remembered_Size As Size
    Private remembered_Location As Point
    Private remembered_Dpi As Integer

    ''' <summary>True once PrepareForDisplay restored a rectangle the user left the panel at. The
    ''' viewer then keeps it instead of centring the panel on its own monitor.</summary>
    Friend ReadOnly Property HasRememberedPlacement As Boolean
        Get
            Return has_Remembered_Placement
        End Get
    End Property

    ' ===================================================================== building ====

    Public Sub New()
        Me.Text = Localization.T("Панель изображений")
        Me.KeyPreview = True

        BuildToolbar()
        BuildSurfaces()
        BuildStrip()
        BuildBusyBand()

        ' Fill first, edges after: the edge-docked controls are laid out before it.
        Controls.Add(grid)
        Controls.Add(details)
        Controls.Add(strip)
        Controls.Add(busy_Band)
        Controls.Add(toolbar)

        ApplyContentColors()
        AddHandler Me.KeyDown, AddressOf OnFormKeyDown
        AddHandler flush_Timer.Tick, AddressOf OnFlushTick

        ' Last, once every child exists: scaling touches only the children present when it runs. The panel's
        ' size is NOT part of what this decides - see ApplyRememberedBounds (124 UX-202).
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Private Sub BuildToolbar()
        toolbar = New ToolStrip With {
            .GripStyle = ToolStripGripStyle.Hidden,
            .CanOverflow = True,
            .RenderMode = ToolStripRenderMode.ManagerRenderMode,
            .Dock = DockStyle.Top,
            .TabStop = False}

        btn_ViewMode = New ToolStripButton With {.DisplayStyle = ToolStripItemDisplayStyle.Text}
        AddHandler btn_ViewMode.Click, Sub(s, e) ToggleView()

        cmb_CardSize = New ToolStripComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .AutoSize = False,
            .Width = 88,
            .ToolTipText = Localization.T("Размер миниатюр")}
        For Each tier As Integer In ModernViewerPreferences.PanelThumbnailTiers
            cmb_CardSize.Items.Add(tier.ToString())
        Next
        AddHandler cmb_CardSize.SelectedIndexChanged, AddressOf OnCardSizeChosen

        btn_SelectAll = New ToolStripButton With {
            .DisplayStyle = ToolStripItemDisplayStyle.Text,
            .Text = Localization.T("Выбрать всё"),
            .ToolTipText = "Ctrl+A"}
        AddHandler btn_SelectAll.Click, Sub(s, e)
                                            model.SelectAll()
                                            AfterSelectionChange()
                                        End Sub

        lbl_Selection = New ToolStripLabel With {.Alignment = ToolStripItemAlignment.Right}

        toolbar.Items.Add(btn_ViewMode)
        toolbar.Items.Add(New ToolStripSeparator())
        toolbar.Items.Add(New ToolStripLabel(Localization.T("Размер:")))
        toolbar.Items.Add(cmb_CardSize)
        toolbar.Items.Add(New ToolStripSeparator())
        toolbar.Items.Add(btn_SelectAll)
        toolbar.Items.Add(lbl_Selection)
    End Sub

    Private Sub BuildSurfaces()
        grid = New ThumbnailGrid With {.Dock = DockStyle.Fill, .AccessibleName = Localization.T("Панель изображений"),
                                       .RightToLeft = RightToLeft.No}
        details = New DetailsList With {.Dock = DockStyle.Fill, .AccessibleName = Localization.T("Панель изображений"),
                                        .RightToLeft = RightToLeft.No, .Visible = False}

        AddHandler grid.ViewChanged, Sub(s, e) RefillLoader()
        AddHandler grid.SelectionGesture, Sub(s, e) AfterSelectionChange()
        AddHandler grid.ItemActivated, Sub(s, i) OpenInMain(i)
        AddHandler grid.HoverChanged, AddressOf OnGridHover

        AddHandler details.SelectionGesture, Sub(s, e) AfterSelectionChange()
        AddHandler details.ItemActivated, Sub(s, i) OpenInMain(i)
        AddHandler details.SortRequested, AddressOf OnSortRequested
    End Sub

    Private Sub BuildStrip()
        strip = New Panel With {.Dock = DockStyle.Bottom, .Height = 30, .BackColor = SystemColors.Control}
        btn_Cancel = New Button With {
            .Text = Localization.T("Отмена"),
            .Dock = DockStyle.Right,
            .Width = 96,
            .Visible = False,
            .TabStop = False}
        AddHandler btn_Cancel.Click, AddressOf OnCancelClicked

        lbl_Strip = New Label With {
            .Dock = DockStyle.Fill,
            .AutoEllipsis = True,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Padding = New Padding(10, 0, 10, 0),
            .ForeColor = SystemColors.ControlText}
        AddHandler lbl_Strip.Click, AddressOf OnStripClicked

        strip.Controls.Add(lbl_Strip)
        strip.Controls.Add(btn_Cancel)
    End Sub

    ''' <summary>The band under the toolbar (126 UX-608). It is shown, not composed per
    ''' tick: the strip already reports what a run is doing - this one says what a CLOSED
    ''' window would have meant, and stays until the run is over.</summary>
    Private Sub BuildBusyBand()
        Dim band_Text As String = Localization.T("Идёт операция - дождитесь или нажмите Esc")
        Dim band_Ink As Color = ThemeService.Palette.ColorOf(ThemeRole.Warning)
        busy_Band = New Panel With {
            .Name = "panel_Busy_Band",
            .Dock = DockStyle.Top,
            .Height = 30,
            .Visible = False}
        lbl_Busy = New Label With {
            .Name = "lbl_Panel_Busy",
            .Dock = DockStyle.Fill,
            .Text = band_Text,
            .TextAlign = ContentAlignment.MiddleCenter,
            .BackColor = band_Ink,
            .ForeColor = If(LuminanceOf(band_Ink) > 0.5, Color.Black, Color.White),
            .AccessibleName = band_Text}
        busy_Band.Controls.Add(lbl_Busy)
    End Sub

    ''' <summary>Perceived brightness, for ink that reads on the warning colour whatever
    ''' the theme is - the same weighting ApplyContentColors uses.</summary>
    Private Shared Function LuminanceOf(c As Color) As Double
        Return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255
    End Function

    Private Sub ShowBusyBand()
        If busy_Band IsNot Nothing Then busy_Band.Visible = True
    End Sub

    Private Sub HideBusyBand()
        If busy_Band IsNot Nothing Then busy_Band.Visible = False
    End Sub

    ''' <summary>The panel is content-coloured: it takes the main window's background scheme
    ''' (the same Form_Color_Scheme logic), because it is a strip of the same pictures.</summary>
    Private Sub ApplyContentColors()
        Dim back As Color = Color.Black
        Select Case Form_Color_Scheme
            Case 0
                Try
                    If Main_Form IsNot Nothing Then back = Main_Form.BackColor
                Catch
                    back = Color.Black
                End Try
            Case 2
                back = Color.White
            Case Else
                back = Color.Black
        End Select

        Dim luminance As Double = (0.299 * back.R + 0.587 * back.G + 0.114 * back.B) / 255
        Dim ink As Color = If(luminance > 0.5, Color.Black, Color.White)

        Me.BackColor = back
        Me.ForeColor = ink
        If grid IsNot Nothing Then
            grid.BackColor = back
            grid.ForeColor = ink
            details.BackColor = back
            details.ForeColor = ink
        End If
    End Sub

    ' ===================================================================== lifecycle ====

    Public Sub PrepareForDisplay()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0010: PicPanel init")
        EndSession()
        ApplyContentColors()

        ' Direction from the language table (APP-BEHAVIOUR rule 8): the window's title and the
        ' status of a batch are localized text. The grid itself stays in file order, left to
        ' right - the paging and keyboard code index cards by position.
        Dim direction As RightToLeft = If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No)
        If Me.RightToLeft <> direction Then Me.RightToLeft = direction
        grid.RightToLeft = RightToLeft.No
        details.RightToLeft = RightToLeft.No

        Dim prefs As ModernViewerPreferences = Main_Form.GetModernPreferences()
        view_Is_List = String.Equals(prefs.PanelViewMode, "list", StringComparison.Ordinal)
        thumb_Px = ResolveThumbnailSize(prefs)
        cmb_CardSize.SelectedItem = thumb_Px.ToString()
        grid.CardSize = thumb_Px

        BeginSession(prefs)
        WireMainEvents()

        sort_Column = PanelSortColumn.None
        sort_Descending = False
        LoadFromMain(keepSelection:=False)
        ApplyViewMode()
        InitializeTooltips()
        SelectOpeningTarget()
        ShowIdleStrip()

        PlaceFromRegistry()
    End Sub

    ''' <summary>The card side: the user's choice here, else the legacy card-size setting
    ''' rounded to one of the four tiers - so an upgrade does not change what was picked.</summary>
    Private Shared Function ResolveThumbnailSize(prefs As ModernViewerPreferences) As Integer
        Dim chosen As Integer = ModernViewerPreferences.SnapPanelThumbnailSize(prefs.PanelThumbnailSize)
        If chosen > 0 Then Return chosen
        Return ModernViewerPreferences.SnapPanelThumbnailSize(Math.Max(Picture_Box_Width_At_Panel, Picture_Box_Height_At_Panel))
    End Function

    ''' <summary>
    ''' Reads the remembered rectangle - on every open, because the panel is built once and shown many times -
    ''' and puts it on the window. The size is stored in device pixels together with the DPI it was measured at
    ''' (<c>PicturePanelDpi</c>); a stored size without one is device pixels at the DPI the window has now. The
    ''' defaults are authored in 96-dpi pixels, like the layout.
    ''' </summary>
    Private Sub PlaceFromRegistry()
        Dim app_Top As Integer = first_Run_Top
        Dim app_Left As Integer = first_Run_Left
        Dim app_Width As Integer = first_Run_Width
        Dim app_Height As Integer = first_Run_Height
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "PicturePanelTop"), app_Top)
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "PicturePanelLeft"), app_Left)
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "PicturePanelWidth"), app_Width)
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "PicturePanelHeight"), app_Height)

        Dim size_Dpi As Integer = WindowPlacement.ParseDpi(GetSetting(App_name, Second_App_Name, "PicturePanelDpi"))
        If app_Width < Limit_Width_Low OrElse app_Width > Limit_Width_High OrElse
           app_Height < Limit_Height_Low OrElse app_Height > Limit_Height_High Then
            ' One size, one DPI: if either side is unusable both fall back to the authored default.
            app_Width = first_Run_Width
            app_Height = first_Run_Height
            size_Dpi = 96
        End If

        ' APP-BEHAVIOUR rule 10: the position is judged by the displays that exist, and one
        ' that lands on none goes to the nearest. A manual start position, or Windows picks its
        ' own and the rectangle is ignored.
        Me.StartPosition = FormStartPosition.Manual
        has_Remembered_Placement = GetSetting(App_name, Second_App_Name, "PicturePanelLeft", "").Length > 0
        remembered_Location = New Point(app_Left, app_Top)
        remembered_Size = New Size(app_Width, app_Height)
        remembered_Dpi = size_Dpi
        ApplyRememberedBounds()
    End Sub

    ''' <summary>
    ''' Puts the remembered rectangle on the window, in the pixels of the DPI the window has NOW. Before the
    ''' panel has ever been shown that DPI is only the system's, so the answer is a guess for the viewer's
    ''' centring and <see cref="OnLoad"/> repeats it once the window has scaled itself; on every later open the
    ''' window is long scaled and this is final.
    ''' </summary>
    Private Sub ApplyRememberedBounds()
        Dim size As Size = WindowPlacement.SizeForDpi(remembered_Size.Width, remembered_Size.Height, remembered_Dpi, Me.DeviceDpi)
        Dim placed As Rectangle = WindowPlacement.Place(New Rectangle(remembered_Location, size),
                                                        WindowPlacement.CurrentWorkAreas())
        Me.SetBounds(placed.Left, placed.Top, placed.Width, placed.Height)
    End Sub

    ''' <summary>
    ''' The window scales itself at its first layout, which is the first time it is shown. Doing it here, before
    ''' the rectangle, is the whole rule: the remembered size is applied AFTER scaling, so it is not multiplied
    ''' by the DPI a second time (the window grew every session when it was). Only the first show gets here.
    ''' </summary>
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        PerformAutoScale()
        ApplyRememberedBounds()
        If Not has_Remembered_Placement Then
            Dim viewer As Main_Form = TryCast(Me.Owner, Main_Form)
            If viewer IsNot Nothing Then viewer.PositionChildOnViewerMonitor(Me)
        End If
        DialogLayout.ClampToWorkingArea(Me)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        ' The view exists at its final size only now: this is when "open on the current file"
        ' can be done exactly.
        Dim at As Integer = model.IndexOf(target_File)
        If view_Is_List Then
            details.ShowRow(If(at >= 0, at, model.FocusIndex))
        ElseIf at >= 0 Then
            grid.CenterOn(at)
        End If
        RefillLoader()
        FocusActiveView()
    End Sub

    Private Sub Image_Panel_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        ' A run keeps going after the window closes: files kept moving with no progress shown,
        ' and a panel reopened meanwhile would have edited the list under the old run (audit
        ' B-14). The user waits for the run, or cancels it; Windows shutting down still closes.
        ' Said on a band (126 UX-608) - a beep tells nobody that an operation is running or
        ' that Esc cancels it.
        If Main_Form.BatchIsRunning AndAlso e.CloseReason = CloseReason.UserClosing Then
            e.Cancel = True
            ShowBusyBand()
            Return
        End If

        If Not MultiWindowPolicy.IsSecondaryInstance() Then
            ' Negative coordinates are a monitor left of or above the primary one, and a
            ' maximized panel remembers its normal rectangle, not the screen's.
            Dim normal_Bounds As Rectangle = WindowPlacement.NormalRectangle(Me.WindowState, Me.Bounds, Me.RestoreBounds)
            SaveSetting(App_name, Second_App_Name, "PicturePanelTop", normal_Bounds.Top.ToString)
            SaveSetting(App_name, Second_App_Name, "PicturePanelLeft", normal_Bounds.Left.ToString)
            If normal_Bounds.Height >= 200 Then SaveSetting(App_name, Second_App_Name, "PicturePanelHeight", normal_Bounds.Height.ToString)
            If normal_Bounds.Width >= 320 Then SaveSetting(App_name, Second_App_Name, "PicturePanelWidth", normal_Bounds.Width.ToString)
            ' The pixels above belong to this DPI; without it the next open cannot tell them from another monitor's.
            SaveSetting(App_name, Second_App_Name, "PicturePanelDpi", Me.DeviceDpi.ToString)
        End If

        If tip IsNot Nothing Then
            tip.Dispose()
            tip = Nothing
        End If
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0100: PicPanel closed")
    End Sub

    ''' <summary>Called from Main_Form once the dialog is closed. The form instance is reused and
    ''' reachable from a Main_Form field, so without this its last session's thumbnails stayed
    ''' resident for as long as the viewer ran.</summary>
    Friend Sub ReleaseAllCards()
        EndSession()
    End Sub

    Private Sub BeginSession(prefs As ModernViewerPreferences)
        Interlocked.Increment(session)
        memory = New ThumbnailMemoryCache(Memory_Budget_Bytes)
        no_Picture.Clear()
        loader = New PanelThumbnailLoader(NewThumbnailService(prefs), thumb_Px, AddressOf OnThumbnailMade)
    End Sub

    ''' <summary>The recipe for a thumbnail, with the one thing it cannot know by itself: an entry of
    ''' an open archive has no place on disk until it is asked for. The main window is captured HERE,
    ''' on the UI thread - a default form instance is per thread, and a worker that named
    ''' Main_Form would be handed a brand-new window.</summary>
    Private Function NewThumbnailService(prefs As ModernViewerPreferences) As ThumbnailService
        Dim host As Main_Form = Main_Form
        Dim max_Entry As Long = CLng(prefs.ArchiveMaxEntryMb) * 1024L * 1024L
        Dim max_Cache As Long = CLng(prefs.ArchiveCacheMaxMb) * 1024L * 1024L
        Return New ThumbnailService(New ThumbnailCacheStore(ThumbnailCacheStore.DefaultDirectory(), prefs.PanelThumbCacheMb),
                                    Function(path As String) host.MaterializeForThumbnail(path, max_Entry, max_Cache))
    End Function

    Private Sub EndSession()
        ' Ends the load session first: every thumbnail still on its way is thrown away when
        ' it arrives.
        Interlocked.Increment(session)
        UnwireMainEvents()
        flush_Timer.Stop()
        pending_Removed.Clear()
        HideBusyBand()
        If loader IsNot Nothing Then
            loader.Dispose()
            loader = Nothing
        End If
        If grid IsNot Nothing Then
            grid.Bind(New SelectionModel(), New List(Of PanelFileRow)(), Nothing, no_Picture)
            details.Bind(New SelectionModel(), New List(Of PanelFileRow)())
        End If
        If memory IsNot Nothing Then
            memory.Dispose()
            memory = Nothing
        End If
        no_Picture.Clear()
        model.SetItems(Nothing)
        rows = New List(Of PanelFileRow)()
        display = New List(Of PanelFileRow)()
        row_By_Path.Clear()
        last_Report = Nothing
        run_Is_Active = False
    End Sub

    ' ======================================================================== data ====

    ''' <summary>Takes the main window's list as it is NOW. The selection survives by path: a
    ''' re-scan, an undo that puts files back and a sort change are all the same event here.</summary>
    Private Sub LoadFromMain(keepSelection As Boolean)
        Dim snapshot As PanelListSnapshot = Main_Form.SnapshotFileList()
        rows = snapshot.Rows
        target_File = snapshot.CurrentPath
        row_By_Path.Clear()
        For Each row As PanelFileRow In rows
            row_By_Path(row.Path) = row
        Next
        If Not keepSelection Then model.SelectNone()
        grid.TargetPath = target_File
        RebuildDisplay()
        FillMissingMetadata()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0090: PicPanel list taken from the main window: " & rows.Count.ToString())
    End Sub

    ''' <summary>A file the scan never saw (an undo put it back) has no size or date; they are
    ''' read once, off the UI thread, so the details list has something to show and sort by.</summary>
    Private Sub FillMissingMetadata()
        Dim missing As List(Of PanelFileRow) = rows.Where(Function(r) Not r.HasMetadata).Take(2000).ToList()
        If missing.Count = 0 Then Return
        Dim mine As Integer = Volatile.Read(session)
        Task.Run(Sub()
                     For Each row As PanelFileRow In missing
                         If mine <> Volatile.Read(session) Then Return
                         Try
                             Dim info As New FileInfo(row.Path)
                             If info.Exists Then
                                 row.SizeBytes = info.Length
                                 row.Modified = info.LastWriteTime
                             End If
                         Catch
                         End Try
                     Next
                     Try
                         BeginInvoke(New Action(Sub()
                                                    If mine <> session Then Return
                                                    If sort_Column = PanelSortColumn.Size OrElse sort_Column = PanelSortColumn.Modified Then
                                                        RebuildDisplay()
                                                    Else
                                                        details.Invalidate()
                                                    End If
                                                End Sub))
                     Catch
                     End Try
                 End Sub)
    End Sub

    ''' <summary>The views' order: the main window's, or the details list's own sort. The
    ''' selection follows by path.</summary>
    Private Sub RebuildDisplay()
        display = PanelSort.Order(rows, sort_Column, sort_Descending)
        model.SetItems(display.Select(Function(r) r.Path))
        grid.Bind(model, display, memory, no_Picture)
        details.Bind(model, display)
        details.SetSort(sort_Column, sort_Descending)
        UpdateSelectionLabel()
    End Sub

    Private Sub SelectOpeningTarget()
        Dim at As Integer = model.IndexOf(target_File)
        If at >= 0 Then model.Click(at)
        UpdateSelectionLabel()
    End Sub

    ''' <summary>Files that finished moving or being deleted leave the panel. Collected and
    ''' flushed on a short timer so a run of three hundred is a few repaints, not three hundred.</summary>
    Private Sub FlushRemoved()
        If pending_Removed.Count = 0 Then Return
        Dim gone As New HashSet(Of String)(pending_Removed, StringComparer.OrdinalIgnoreCase)
        pending_Removed.Clear()

        rows.RemoveAll(Function(r) gone.Contains(r.Path))
        display.RemoveAll(Function(r) gone.Contains(r.Path))
        For Each p As String In gone
            row_By_Path.Remove(p)
            memory?.Remove(p)
            no_Picture.Remove(p)
        Next
        model.RemovePaths(gone)
        grid.Bind(model, display, memory, no_Picture)
        details.Bind(model, display)
        UpdateSelectionLabel()
    End Sub

    Private Sub OnFlushTick(sender As Object, e As EventArgs)
        flush_Timer.Stop()
        FlushRemoved()
        If thumb_Dirty Then
            thumb_Dirty = False
            grid.Invalidate()
        End If
    End Sub

    ' ================================================================== thumbnails ====

    ''' <summary>On a loader thread.</summary>
    Private Sub OnThumbnailMade(path As String, thumbnail As Bitmap)
        Dim mine As Integer = Volatile.Read(session)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then
                thumbnail?.Dispose()
                Return
            End If
            BeginInvoke(New Action(Sub() DeliverThumbnail(mine, path, thumbnail)))
        Catch
            ' The form went away between the check and the post: the bitmap is still ours.
            thumbnail?.Dispose()
        End Try
    End Sub

    Private Sub DeliverThumbnail(forSession As Integer, path As String, thumbnail As Bitmap)
        If forSession <> session OrElse memory Is Nothing OrElse Me.IsDisposed Then
            thumbnail?.Dispose()
            Return
        End If
        If thumbnail Is Nothing Then
            no_Picture.Add(path)
        Else
            memory.Put(path, thumbnail)
        End If
        thumb_Dirty = True
        If Not flush_Timer.Enabled Then flush_Timer.Start()
    End Sub

    ''' <summary>The queue is REPLACED, not appended to: whatever is no longer in view is simply
    ''' never started, which is what keeps a fast scroll from queueing three hundred decodes.</summary>
    Private Sub RefillLoader()
        If loader Is Nothing OrElse memory Is Nothing Then Return
        If view_Is_List Then
            loader.SetWanted(Nothing)
            Return
        End If
        loader.SetWanted(grid.VisibleRows().Where(Function(r) Not memory.Contains(r.Path) AndAlso Not no_Picture.Contains(r.Path)).ToList())
    End Sub

    Private Sub OnCardSizeChosen(sender As Object, e As EventArgs)
        Dim chosen As Integer
        If Not Integer.TryParse(Convert.ToString(cmb_CardSize.SelectedItem), chosen) OrElse chosen = thumb_Px Then Return
        thumb_Px = chosen
        Main_Form.GetModernPreferences().PanelThumbnailSize = chosen

        ' A new size is a new picture: the old loader (it renders at the old size) and the old
        ' bitmaps go; the disk cache keys the size, so nothing stale can come back.
        Dim prefs As ModernViewerPreferences = Main_Form.GetModernPreferences()
        Interlocked.Increment(session)
        If loader IsNot Nothing Then loader.Dispose()
        memory?.Clear()
        no_Picture.Clear()
        loader = New PanelThumbnailLoader(NewThumbnailService(prefs), thumb_Px, AddressOf OnThumbnailMade)
        grid.CardSize = chosen
        RefillLoader()
        FocusActiveView()
    End Sub

    ' ================================================================== view mode ====

    Private Sub ToggleView()
        view_Is_List = Not view_Is_List
        Main_Form.GetModernPreferences().PanelViewMode = If(view_Is_List, "list", "grid")
        ApplyViewMode()
        Dim at As Integer = model.FocusIndex
        If view_Is_List Then
            details.ShowRow(at)
        Else
            grid.EnsureVisible(at)
            RefillLoader()
        End If
        FocusActiveView()
    End Sub

    Private Sub ApplyViewMode()
        grid.Visible = Not view_Is_List
        details.Visible = view_Is_List
        btn_ViewMode.Text = If(view_Is_List, Localization.T("Плитки"), Localization.T("Список"))
        btn_ViewMode.ToolTipText = Localization.T("Переключить вид: плитки или список (Ctrl+L)")
        cmb_CardSize.Enabled = Not view_Is_List
        If view_Is_List Then loader?.SetWanted(Nothing)
    End Sub

    Private Sub FocusActiveView()
        If view_Is_List Then details.Focus() Else grid.Focus()
    End Sub

    Private Sub OnSortRequested(sender As Object, clicked As PanelSortColumn)
        PanelSort.NextClick(clicked, sort_Column, sort_Descending)
        RebuildDisplay()
        details.ShowRow(model.FocusIndex)
    End Sub

    ' ======================================================================= tooltips ====

    Private Sub InitializeTooltips()
        If tip Is Nothing Then
            tip = New ToolTip With {.AutoPopDelay = 7000, .InitialDelay = 700, .ReshowDelay = 500, .ShowAlways = True}
        End If
    End Sub

    Private Sub OnGridHover(sender As Object, index As Integer)
        If tip Is Nothing Then Return
        Dim path As String = model.PathAt(index)
        Dim row As PanelFileRow = Nothing
        If path Is Nothing OrElse Not row_By_Path.TryGetValue(path, row) Then
            tip.SetToolTip(grid, HelpText())
            Return
        End If
        Dim facts As String = (PanelText.FormatSize(row.SizeBytes) & "  " & PanelText.FormatDate(row.Modified, row.HasMetadata)).Trim()
        tip.SetToolTip(grid, If(facts.Length = 0, row.Name, row.Name & vbCrLf & facts))
    End Sub

    Private Shared Function HelpText() As String
        Return Localization.T("ЛКМ: выбрать; Shift+ЛКМ: диапазон; Ctrl+ЛКМ: добавить или убрать; протяжка мышью - рамка выделения") & vbCrLf &
               Localization.T("Ctrl+A: выделить всё; Ctrl+Shift+A: снять выделение; Ctrl+I: обратить; Пробел: отметить; стрелки, Home, End, PgUp, PgDn: перемещение") & vbCrLf &
               Localization.T("Двойной клик или Enter: открыть в главном окне; Esc: отменить операцию, затем закрыть панель") & vbCrLf &
               Localization.T("Del: удалить выделенное; цифры 0-9: переместить; Shift + цифра верхнего ряда: скопировать") & vbCrLf &
               Localization.T("Ctrl+Z: отменить последнюю операцию; Ctrl+L: плитки или список")
    End Function

    ' ====================================================================== selection ====

    Private Sub AfterSelectionChange()
        UpdateSelectionLabel()
        grid.Invalidate()
        details.Invalidate()
    End Sub

    Private Sub UpdateSelectionLabel()
        lbl_Selection.Text = Localization.TF("Выбрано: {0} из {1}", model.SelectedCount, model.Count)
    End Sub

    ''' <summary>Opens a file in the main window - what a double click always did.</summary>
    Private Sub OpenInMain(index As Integer)
        If Main_Form.BatchIsRunning Then Return
        Dim path As String = model.PathAt(index)
        If path Is Nothing Then Return
        Choosen_Picture_From_Panel = path
        Me.Close()
    End Sub

    ' ========================================================================= keys ====

    Private Shared Function SlotFromKey(e As KeyEventArgs, ByRef topRow As Boolean) As Integer
        topRow = False
        Dim digit As Integer = -1
        If e.KeyCode >= Keys.D0 AndAlso e.KeyCode <= Keys.D9 Then
            digit = e.KeyCode - Keys.D0
            topRow = True
        ElseIf e.KeyCode >= Keys.NumPad0 AndAlso e.KeyCode <= Keys.NumPad9 Then
            digit = e.KeyCode - Keys.NumPad0
        End If
        If digit < 0 Then Return -1
        Return If(digit = 0, 10, digit)
    End Function

    Private Sub OnFormKeyDown(sender As Object, e As KeyEventArgs)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0070: key down: " & e.KeyCode.ToString())

        ' F1 opens the same reference the empty grid's tooltip carries (126 UX-609): a
        ' tooltip on empty space is the one place nobody looks first.
        If e.KeyCode = Keys.F1 Then
            AskDialogs.Tell(Me, Localization.T("Клавиши панели"), HelpText(), AskIcon.None)
            e.Handled = True
            e.SuppressKeyPress = True
            Return
        End If

        ' The size drop-down owns the keys while it is open.
        If cmb_CardSize IsNot Nothing AndAlso cmb_CardSize.DroppedDown Then Return

        Dim handled As Boolean = True
        Dim columns As Integer = If(view_Is_List, 1, grid.Cols())
        Dim page_Rows As Integer = If(view_Is_List, details.PageRows(), grid.PageRows())

        If e.KeyCode = Keys.Escape Then
            ' First the run, then the window: the second Esc closes.
            If Main_Form.BatchIsRunning Then
                OnCancelClicked(Nothing, EventArgs.Empty)
            Else
                Me.Close()
            End If
        ElseIf e.Control AndAlso Not e.Alt Then
            Select Case e.KeyCode
                Case Keys.A
                    If e.Shift Then model.SelectNone() Else model.SelectAll()
                    AfterSelectionChange()
                Case Keys.I
                    model.InvertSelection()
                    AfterSelectionChange()
                Case Keys.L
                    ToggleView()
                Case Keys.Z
                    UndoFromHere()
                Case Else
                    handled = False
            End Select
        ElseIf Not e.Alt Then
            Select Case e.KeyCode
                Case Keys.Left : MoveFocusBy(FocusMove.Left, e.Shift, columns, page_Rows)
                Case Keys.Right : MoveFocusBy(FocusMove.Right, e.Shift, columns, page_Rows)
                Case Keys.Up : MoveFocusBy(FocusMove.Up, e.Shift, columns, page_Rows)
                Case Keys.Down : MoveFocusBy(FocusMove.Down, e.Shift, columns, page_Rows)
                Case Keys.Home : MoveFocusBy(FocusMove.Home, e.Shift, columns, page_Rows)
                Case Keys.End : MoveFocusBy(FocusMove.End, e.Shift, columns, page_Rows)
                Case Keys.PageUp : MoveFocusBy(FocusMove.PageUp, e.Shift, columns, page_Rows)
                Case Keys.PageDown : MoveFocusBy(FocusMove.PageDown, e.Shift, columns, page_Rows)
                Case Keys.Space
                    model.ToggleFocused()
                    AfterSelectionChange()
                Case Keys.Enter
                    OpenInMain(model.FocusIndex)
                Case Keys.Delete
                    RunBatch(BatchIntent.Delete, 0)
                Case Else
                    Dim top_Row As Boolean
                    Dim slot As Integer = SlotFromKey(e, top_Row)
                    If slot < 0 Then
                        handled = False
                    Else
                        ' Same pairing as the main window: a digit moves the selection, Shift +
                        ' a TOP-ROW digit copies it. Top row only - with NumLock on, Windows
                        ' drops NumLock while Shift is held, so Shift+NumPad1 never arrives as
                        ' NumPad1 anyway.
                        RunBatch(If(e.Shift AndAlso top_Row, BatchIntent.CopyToSlot, BatchIntent.MoveToSlot), slot)
                    End If
            End Select
        Else
            handled = False
        End If

        If handled Then
            e.Handled = True
            e.SuppressKeyPress = True
        End If
    End Sub

    Private Sub MoveFocusBy(move As FocusMove, shift As Boolean, columns As Integer, pageRows As Integer)
        model.MoveFocus(move, shift, columns, pageRows)
        Dim at As Integer = model.FocusIndex
        If view_Is_List Then details.ShowRow(at) Else grid.EnsureVisible(at)
        AfterSelectionChange()
    End Sub

    ' ===================================================================== the batch ====

    ''' <summary>The selection goes to the main window's batch engine and stays there. The panel
    ''' keeps NO copy, move or delete logic of its own (PG-1).</summary>
    Private Async Sub RunBatch(intent As BatchIntent, slot As Integer)
        Dim paths As List(Of String) = model.SelectedPaths()
        If paths.Count = 0 Then
            ShowStrip(Localization.T("Ничего не выбрано"), running:=False, clickable:=False, warning:=False)
            Return
        End If

        Try
            Dim started As Boolean = Await Main_Form.StartBatchAsync(Me, paths, intent, slot)
            If Me.IsDisposed Then Return
            If Not started AndAlso Not Main_Form.BatchIsRunning Then
                ' Refused with a sentence on the main window's status line (a slot that is
                ' down, a busy queue, an archive) - which is behind this window: say it here.
                Dim reason As NotifyMessage = Main_Form.StatusMessageNow
                If Not String.IsNullOrWhiteSpace(reason.Text) Then ShowStrip(reason.Text, running:=False, clickable:=False, warning:=True)
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Thumbnail panel: batch", ex)
            If Not Me.IsDisposed Then ShowStrip(FailureText.Describe(ex), running:=False, clickable:=False, warning:=True)
        End Try
    End Sub

    ''' <summary>Ctrl+Z: exactly the main window's U.</summary>
    Private Sub UndoFromHere()
        Main_Form.UndoFromPanel()
        If Not Main_Form.BatchIsRunning Then
            Dim said As NotifyMessage = Main_Form.StatusMessageNow
            If Not String.IsNullOrWhiteSpace(said.Text) Then ShowStrip(said.Text, running:=False, clickable:=False, warning:=said.Level >= NotifyLevel.Warning)
        End If
    End Sub

    Private Sub WireMainEvents()
        If events_Wired Then Return
        events_Wired = True
        AddHandler Main_Form.BatchProgressChanged, AddressOf OnBatchProgress
        AddHandler Main_Form.BatchItemDone, AddressOf OnBatchItemDone
        AddHandler Main_Form.BatchCompleted, AddressOf OnBatchCompleted
    End Sub

    Private Sub UnwireMainEvents()
        If Not events_Wired Then Return
        events_Wired = False
        Try
            RemoveHandler Main_Form.BatchProgressChanged, AddressOf OnBatchProgress
            RemoveHandler Main_Form.BatchItemDone, AddressOf OnBatchItemDone
            RemoveHandler Main_Form.BatchCompleted, AddressOf OnBatchCompleted
        Catch ex As Exception
            ' The main window is going away with the application.
        End Try
    End Sub

    Private Sub OnBatchProgress(sender As Object, e As BatchProgressEventArgs)
        If Me.IsDisposed Then Return
        run_Is_Active = True
        ShowStrip(Localization.TF("Файл {0} из {1}: {2}", e.Index, e.Total, e.Name), running:=True, clickable:=False, warning:=False)
    End Sub

    ''' <summary>The grid follows the run: a file that has left its folder leaves the panel now,
    ''' not when the run ends.</summary>
    Private Sub OnBatchItemDone(sender As Object, e As BatchItemEventArgs)
        If Me.IsDisposed Then Return
        Dim result As BatchItemResult = e.Result
        If result.State <> BatchItemState.Done OrElse result.Op Is Nothing Then Return
        Select Case result.Op.Kind
            Case FileOpKind.Move, FileOpKind.RecycleDelete, FileOpKind.Delete
                pending_Removed.Add(result.Op.Source)
                If Not flush_Timer.Enabled Then flush_Timer.Start()
        End Select
    End Sub

    Private Sub OnBatchCompleted(sender As Object, e As BatchFinishedEventArgs)
        If Me.IsDisposed Then Return
        run_Is_Active = False
        FlushRemoved()

        Dim report As BatchReport = e.Report
        last_Report = report
        Select Case report.Kind
            Case BatchRunKind.Undo
                ' Files came back into the main window's list: take it again, selection kept.
                LoadFromMain(keepSelection:=True)
            Case BatchRunKind.Copy
                ' What was copied is done with - as the panel always cleared it.
                model.SelectNone()
        End Select
        AfterSelectionChange()

        Dim line As String = report.SummaryLine()
        If Not report.IsClean Then line &= "  -  " & Localization.T("нажмите, чтобы увидеть подробности")
        ShowStrip(line, running:=False, clickable:=Not report.IsClean OrElse report.Results.Count > 0, warning:=Not report.IsClean)
        HideBusyBand()
        FocusActiveView()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0085: batch finished: " & report.SummaryLine())
    End Sub

    ' ======================================================================== strip ====

    Private Sub ShowIdleStrip()
        ShowStrip("", running:=False, clickable:=False, warning:=False)
        HideBusyBand()
    End Sub

    Private Sub ShowStrip(text As String, running As Boolean, clickable As Boolean, warning As Boolean)
        lbl_Strip.Text = text
        lbl_Strip.Cursor = If(clickable, Cursors.Hand, Cursors.Default)
        ' The palette role, not a literal: the strip follows the active theme
        ' (APP-STYLE section 4; the day tone itself is pinned by ICON-RENDER 0.14 section 10 D).
        lbl_Strip.ForeColor = If(warning, ThemeService.Palette.ColorOf(ThemeRole.Warning), SystemColors.ControlText)
        btn_Cancel.Visible = running
        btn_Cancel.Enabled = running
    End Sub

    Private Sub OnCancelClicked(sender As Object, e As EventArgs)
        If Not Main_Form.BatchIsRunning Then Return
        Main_Form.CancelBatch()
        btn_Cancel.Enabled = False
        lbl_Strip.Text = Localization.T("Остановка.. текущий файл будет доделан")
    End Sub

    Private Sub OnStripClicked(sender As Object, e As EventArgs)
        If last_Report Is Nothing OrElse Main_Form.BatchIsRunning Then Return
        Using dialog As New Batch_Report_Form(last_Report)
            DialogLayer.ShowViewerDialog(dialog, Me)
        End Using
        FocusActiveView()
    End Sub

End Class
#End If
