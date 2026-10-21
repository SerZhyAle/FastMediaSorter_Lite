Imports System.ComponentModel
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public Class Image_Panel_Form
    Inherits Form
#If Not NETFRAMEWORK Then
    Implements IThemeAware

    ' DECLARED OUTSIDE THE THEME (spec 060 rule 10, APP-STYLE rule 5): the thumbnail panel is
    ' content-coloured - it takes the main window's background scheme (UpdateBackgroundColor),
    ' because it is a strip of the same pictures. Its menus and dialogs follow the theme.
    Private ReadOnly Property ThemesItself As Boolean Implements IThemeAware.ThemesItself
        Get
            Return True
        End Get
    End Property

    Private Sub ApplyTheme(palette As ThemePalette) Implements IThemeAware.ApplyTheme
        ' The picture grid remains content-colored; command/status chrome follows the app.
        If toolbar IsNot Nothing Then
            ThemeApplier.ApplyTree(toolbar, palette)
            toolbar.BackColor = palette.SurfaceWindow
            toolbar.ForeColor = palette.TextPrimary
        End If
        If strip IsNot Nothing Then
            ThemeApplier.ApplyTree(strip, palette)
            strip.BackColor = palette.SurfaceWindow
            strip.ForeColor = palette.TextPrimary
        End If
        ThemeApplier.ApplyNativeTree(Me, palette.IsDark)
        ThemeApplier.ApplyTitleBar(Me, palette.IsDark)
    End Sub
#End If

#If NETFRAMEWORK Then
    ' ----------------------------------------------------------------------------------
    ' THE x86 FALLBACK PANEL, verbatim. Everything from here to the matching #End If is the
    ' panel as it was before 004/005: a PictureBox per file, its own sort, its own move/copy/
    ' delete loop. The maintenance policy freezes the net48 build (CLAUDE.md), and it has no
    ' batch engine to call. The mainline panel is Image_Panel_Form.Modern.vb / .Grid.vb /
    ' .Details.vb (005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md).
    ' ----------------------------------------------------------------------------------

    ' --- UI Controls and Constants ---
    Private Const resize_debounce_interval As Integer = 200
    Private Const first_run_top = 50
    Private Const first_run_left = 50
    Private Const first_run_width = 800
    Private Const first_run_height = 600
    Private Const picture_panel_position_Limit_Top = 720
    Private Const picture_panel_position_Limit_Left = 1000
    Private Const picture_panel_position_Limit_Width = 3000
    Private Const picture_panel_position_Limit_Width_Low = 320
    Private Const picture_panel_position_Limit_Height = 3000
    Private Const picture_panel_position_Limit_Height_Low = 240
    Private selected_Box_Border_Color As Color = Color.Blue
    Private initial_Target_Border_Color As Color = Color.Red ' Special color for the initial image
    Private Const selected_box_border_width As Integer = 5

    ''' <summary>
    ''' Ceiling on how many thumbnail cards may exist at once.
    '''
    ''' Each card is a real window handle plus a decoded bitmap, and the process shares a
    ''' 10,000-object USER quota: past it CreateWindow starts failing and WinForms throws on
    ''' every new control - the app does not recover. The x86 build dies sooner still, of
    ''' OutOfMemory (10,000 cards at 340x200 is ~2.7 GB in a 2 GB address space). 600 is far
    ''' more than any screen shows at any card size and sixteen times under the quota.
    ''' </summary>
    Private Const Max_Live_Thumbnails As Integer = 600

    ''' <summary>Rows of context materialized above the file the panel opens on, so it does
    ''' not look like the folder begins there.</summary>
    Private Const Context_Rows_Above_Target As Integer = 2

    ''' <summary>Gap between cards, matching what the column arithmetic assumes.</summary>
    Private Const Card_Gap As Integer = 6

    Private imagePanel As FlowLayoutPanel
    ' --- State Management ---
    Private allImageFiles As New List(Of String)()
    Private selectedPictureControls As New List(Of PictureBox)()
    Private initial_Target_PictureBox As PictureBox = Nothing ' To store the special target PictureBox

    ''' <summary>
    ''' The materialized window over allImageFiles: files [window_First_Index,
    ''' window_Last_Index) have a card, in that order. It used to be a single high-water mark
    ''' anchored at zero, which is what made opening the panel deep inside a big folder build
    ''' a card - and run a full-size decode - for every file from the start of the folder up
    ''' to where the user was standing.
    ''' </summary>
    Private window_First_Index As Integer = 0

    ''' <summary>Exclusive upper bound of the materialized window.</summary>
    Private window_Last_Index As Integer = 0

    Private is_Loading As Boolean = False

    ''' <summary>
    ''' The load session. ReleaseAllCards bumps it - on close and on every re-prepare - and
    ''' every loader captures it on entry and gives up after each Await once it has moved.
    ''' Without it a load still running when the panel closed resumed against the reset
    ''' window (0..0) and decoded the folder from index 0 up to the old target, off screen
    ''' (audit B-07). Read from the thread pool by FillThumbnailAsync, hence Volatile.
    ''' </summary>
    Private load_Generation As Integer = 0

    Private Function CurrentLoadGeneration() As Integer
        Return System.Threading.Volatile.Read(load_Generation)
    End Function

    ''' <summary>Set while a bulk copy/move/delete is running off the UI thread, so a second
    ''' one cannot start on a selection the first is still consuming.</summary>
    Private is_Bulk_Operation_Running As Boolean = False
    Private resizeDebounceTimer As New System.Windows.Forms.Timer()
    Private sortIndexFromMainForm As Integer = 0
    Private toolTip As ToolTip

    <DllImport("shlwapi.dll", CharSet:=CharSet.Unicode)>
    Public Shared Function StrCmpLogicalW(psz1 As String, psz2 As String) As Integer
    End Function

    Public Class NaturalFilenameComparer
        Implements IComparer(Of String)
        Public Function Compare(x As String, y As String) As Integer Implements IComparer(Of String).Compare
            Return StrCmpLogicalW(x, y)
        End Function
    End Class

    Private Function GetOppositeColor(inputColor As Color) As Color
        ' Calculate luminance using the same logic as Main_Form
        Dim luminance As Double = (0.299 * inputColor.R + 0.587 * inputColor.G + 0.114 * inputColor.B) / 255

        ' Return white for dark colors, black for light colors
        If luminance > 0.5 Then
            Return Color.Black
        Else
            Return Color.White
        End If
    End Function

    Private Sub UpdateBackgroundColor()
        Dim back_Color As System.Drawing.Color = System.Drawing.Color.Black

        ' Use the same color scheme logic as Main_Form
        Select Case Form_Color_Scheme
            Case 0 ' Dynamic color based on current image
                ' Try to get the background color from Main_Form if it's available
                Try
                    If Main_Form IsNot Nothing Then
                        back_Color = Main_Form.BackColor
                    Else
                        back_Color = System.Drawing.Color.Black
                    End If
                Catch
                    back_Color = System.Drawing.Color.Black
                End Try
            Case 1 ' Black
                back_Color = System.Drawing.Color.Black
            Case 2 ' White  
                back_Color = System.Drawing.Color.White
            Case Else ' Default to black
                back_Color = System.Drawing.Color.Black
        End Select

        ' Apply the color to both the form and the image panel
        Me.BackColor = back_Color
        imagePanel.BackColor = back_Color

        ' Update the text color to contrast with background
        Dim text_Color As Color = GetOppositeColor(back_Color)
        Me.ForeColor = text_Color
    End Sub

    Public Sub New()
        Me.Text = Localization.T("Панель изображений")
        Me.KeyPreview = True

        imagePanel = New FlowLayoutPanel()
        With imagePanel
            .Dock = DockStyle.Fill
            .AutoScroll = True
            .FlowDirection = FlowDirection.LeftToRight
        End With

        ' Apply the same background color logic as Main_Form
        UpdateBackgroundColor()

        Me.Controls.Add(imagePanel)

        AddHandler Me.Resize, AddressOf OnFormResize
        AddHandler Me.KeyDown, AddressOf OnFormKeyDown
        AddHandler imagePanel.Paint, AddressOf OnPanelPaint
        resizeDebounceTimer.Interval = resize_debounce_interval
        AddHandler resizeDebounceTimer.Tick, AddressOf OnResizeTimerTick
    End Sub

#If Not NETFRAMEWORK Then
    Private has_Remembered_Placement As Boolean = False

    ''' <summary>True once PrepareForDisplay restored a rectangle the user left the panel
    ''' at. The viewer then keeps it instead of centring the panel on its own monitor -
    ''' that centring is only the answer for a panel that has never been placed.</summary>
    Friend ReadOnly Property HasRememberedPlacement As Boolean
        Get
            Return has_Remembered_Placement
        End Get
    End Property
#End If

    Public Sub SetSortIndexFromMainForm(idx As Integer)
        sortIndexFromMainForm = idx
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' PrepareForDisplay is now called from Main_Form before showing, which is correct.
    End Sub

    Public Sub PrepareForDisplay()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0010: PicPanel init")

        ' Update background color when preparing for display
        UpdateBackgroundColor()

        ' Direction from the language table (APP-BEHAVIOUR rule 8, ticket 028 Ф-7): the
        ' window's title and the status of a batch are localized text. The x86 build ships
        ' RU/EN only, so there this is always No.
        Dim direction As RightToLeft = If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No)
        If Me.RightToLeft <> direction Then Me.RightToLeft = direction
        ' The grid itself stays in file order, left to right: a FlowLayoutPanel reverses its
        ' flow under RightToLeft, and the paging and keyboard code index cards by position.
        imagePanel.RightToLeft = RightToLeft.No

        InitializeState()
        InitializeTooltips()

        Dim app_Top_Int As Integer = first_run_top
        Dim app_Left_Int As Integer = first_run_left
        Dim app_Width_Int As Integer = first_run_width
        Dim app_Height_Int As Integer = first_run_height
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "PicturePanelTop"), app_Top_Int)
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "PicturePanelLeft"), app_Left_Int)
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "PicturePanelWidth"), app_Width_Int)
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "PicturePanelHeight"), app_Height_Int)
#If NETFRAMEWORK Then
        app_Top_Int = If(app_Top_Int < 0 OrElse app_Top_Int > picture_panel_position_Limit_Top, first_run_top, app_Top_Int)
        app_Left_Int = If(app_Left_Int < 0 OrElse app_Left_Int > picture_panel_position_Limit_Left, first_run_left, app_Left_Int)
#End If
        app_Width_Int = If(app_Width_Int < picture_panel_position_Limit_Width_Low OrElse app_Width_Int > picture_panel_position_Limit_Width, first_run_width, app_Width_Int)
        app_Height_Int = If(app_Height_Int < picture_panel_position_Limit_Height_Low OrElse app_Height_Int > picture_panel_position_Limit_Height, first_run_height, app_Height_Int)

#If Not NETFRAMEWORK Then
        ' APP-BEHAVIOUR rule 10 (ticket 028 Ф-2). The fixed ceilings (Top <= 720,
        ' Left <= 1000) came from a 1366x768 world, and "must not be negative" threw away
        ' every monitor left of or above the primary one. The position is now judged by
        ' the displays that exist, and one that lands on none goes to the nearest.
        ' Manual start position, or Windows picks its own and the rectangle is ignored.
        Me.StartPosition = FormStartPosition.Manual
        has_Remembered_Placement = GetSetting(App_name, Second_App_Name, "PicturePanelLeft", "").Length > 0
        Dim placed As Rectangle = WindowPlacement.Place(New Rectangle(app_Left_Int, app_Top_Int, app_Width_Int, app_Height_Int),
                                                        WindowPlacement.CurrentWorkAreas())
        app_Left_Int = placed.Left
        app_Top_Int = placed.Top
        app_Width_Int = placed.Width
        app_Height_Int = placed.Height
#End If

        Me.SetBounds(app_Left_Int, app_Top_Int, app_Width_Int, app_Height_Int)

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0020: Form_Sizes: " & app_Left_Int.ToString & " - " & app_Top_Int.ToString & " " & app_Width_Int.ToString & " - " & app_Height_Int.ToString)
    End Sub

    Protected Overrides Async Sub OnVisibleChanged(e As EventArgs)
        MyBase.OnVisibleChanged(e)
        If Me.Visible Then
            Await LoadContentAsync()
        End If
    End Sub

    ''' <summary>How many cards fit across the panel at the current card size.</summary>
    Private Function ColumnsPerRow() As Integer
        Return Math.Max(1, imagePanel.ClientSize.Width \ (Picture_Box_Width_At_Panel + Card_Gap))
    End Function

    Private Function RowHeight() As Integer
        Return Picture_Box_Height_At_Panel + Card_Gap
    End Function

    ''' <summary>
    ''' Opens the window over the folder AT the current file rather than at the folder's
    ''' start, then fills the visible area. The old loop walked forward from index 0 until
    ''' the current file's card appeared, so the cost of opening the panel was proportional
    ''' to how deep in the folder the user had got - see Max_Live_Thumbnails.
    ''' </summary>
    Private Async Function LoadContentAsync() As Task
        Dim generation As Integer = CurrentLoadGeneration()
        Dim targetFile As String = Current_File_Name

        initial_Target_PictureBox = Nothing ' Reset special highlight

        Dim targetIndex As Integer = -1
        If Not String.IsNullOrEmpty(targetFile) Then targetIndex = allImageFiles.IndexOf(targetFile)

        Dim cols As Integer = ColumnsPerRow()
        Dim start As Integer = 0
        If targetIndex > 0 Then
            start = Math.Max(0, targetIndex - cols * Context_Rows_Above_Target)
            start -= (start Mod cols)   ' whole rows only, or the grid shifts sideways
        End If
        window_First_Index = start
        window_Last_Index = start

        ' Forward until the target has a card. With the window anchored at the target that
        ' is one batch, not one per file in the folder.
        While window_Last_Index < allImageFiles.Count
            If is_Loading Then
                Await Task.Delay(50)
                If generation <> CurrentLoadGeneration() Then Return
                Continue While
            End If
            Await LoadNextBatchAsync()
            If generation <> CurrentLoadGeneration() Then Return
            If targetIndex < 0 OrElse window_Last_Index > targetIndex Then Exit While
        End While

        If targetIndex >= 0 Then
            initial_Target_PictureBox = FindCard(targetFile)
            If initial_Target_PictureBox IsNot Nothing Then
                imagePanel.ScrollControlIntoView(initial_Target_PictureBox)
                ClearSelection()
                AddToSelection(initial_Target_PictureBox)
                UpdateSelectionVisuals()
            End If
        End If

        Await FillVisibleAreaAsync()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0030: PicPanel content loaded, window " &
                        window_First_Index.ToString() & ".." & window_Last_Index.ToString())
    End Function

    Private Function FindCard(file_Path As String) As PictureBox
        For Each pb As PictureBox In imagePanel.Controls.OfType(Of PictureBox)()
            If String.Equals(CStr(pb.Tag), file_Path, StringComparison.Ordinal) Then Return pb
        Next
        Return Nothing
    End Function

    ' REMOVED: The OnFormShown event handler is no longer needed.

    Private Sub OnFormResize(sender As Object, e As EventArgs)
        resizeDebounceTimer.Stop()
        resizeDebounceTimer.Start()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0040: PicPanel resized")
    End Sub

    Private Sub OnResizeTimerTick(sender As Object, e As EventArgs)
        resizeDebounceTimer.Stop()
        Forget(FillVisibleAreaAsync())
    End Sub

    Private Sub OnPanelPaint(sender As Object, e As PaintEventArgs)
        If is_Loading OrElse Not imagePanel.VerticalScroll.Visible Then Return

        ' Scrolled to the bottom - more files below.
        If imagePanel.VerticalScroll.Value >= (imagePanel.VerticalScroll.Maximum - imagePanel.VerticalScroll.LargeChange) Then
            Forget(LoadNextBatchAsync())
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0050: PicPanel repaint")
            Return
        End If

        ' Scrolled to the top with files above the window: the panel now opens at the
        ' current file, so "above" is a real place to go and has to load on demand.
        If window_First_Index > 0 AndAlso imagePanel.VerticalScroll.Value <= imagePanel.VerticalScroll.Minimum Then
            Forget(LoadPreviousBatchAsync())
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0051: PicPanel repaint - load previous")
        End If
    End Sub

    Private Sub OnPictureBoxClick(sender As Object, e As EventArgs)
        Dim pb = CType(sender, PictureBox)
        If (Control.ModifierKeys And Keys.Control) = Keys.Control Then
            ToggleSelection(pb)
        Else
            ClearSelection()
            AddToSelection(pb)
        End If
        UpdateSelectionVisuals()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0060: Pic choosen")
    End Sub

    Private Sub OnPictureBoxDoubleClick(sender As Object, e As EventArgs)
        Dim pb = CType(sender, PictureBox)
        Choosen_Picture_From_Panel = CStr(pb.Tag)
        Me.Close()
    End Sub

    Private Sub OnPictureBoxPaint(sender As Object, e As PaintEventArgs)
        Dim pb = CType(sender, PictureBox)
        Dim penColor As Color = Color.Empty

        If pb Is initial_Target_PictureBox Then
            penColor = initial_Target_Border_Color
        ElseIf selectedPictureControls.Contains(pb) Then
            penColor = selected_Box_Border_Color
        End If

        If Not penColor.IsEmpty Then
            Using pen As New Pen(penColor, selected_box_border_width)
                pen.Alignment = Drawing2D.PenAlignment.Inset
                e.Graphics.DrawRectangle(pen, 0, 0, pb.Width - 1, pb.Height - 1)
            End Using
        End If
    End Sub

    Private Sub OnFormKeyDown(sender As Object, e As KeyEventArgs)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0070: key down: " & e.KeyCode.ToString())

        If selectedPictureControls.Count = 0 AndAlso e.KeyCode <> Keys.Escape Then Return ' No action if nothing is selected, except for Escape

        ' Same pairing as the main window: a digit moves the selection, Shift + a TOP-ROW
        ' digit copies it. Top row only - with NumLock on, Windows drops NumLock while
        ' Shift is held, so Shift+NumPad1 never arrives as NumPad1 anyway. On net48 the
        ' action still follows the global copy mode inside PoMove_for_Panel.
#If NETFRAMEWORK Then
        Dim as_Copy As Boolean = False
#Else
        Dim as_Copy As Boolean = e.Shift AndAlso e.KeyCode >= Keys.D0 AndAlso e.KeyCode <= Keys.D9
#End If

        Select Case e.KeyCode
            Case Keys.Delete
                e.Handled = True
                DeleteSelectedFiles()

            Case Keys.D1, Keys.NumPad1
                e.Handled = True
                PoMove_for_Panel(1, as_Copy)
            Case Keys.D2, Keys.NumPad2
                e.Handled = True
                PoMove_for_Panel(2, as_Copy)
            Case Keys.D3, Keys.NumPad3
                e.Handled = True
                PoMove_for_Panel(3, as_Copy)
            Case Keys.D4, Keys.NumPad4
                e.Handled = True
                PoMove_for_Panel(4, as_Copy)
            Case Keys.D5, Keys.NumPad5
                e.Handled = True
                PoMove_for_Panel(5, as_Copy)
            Case Keys.D6, Keys.NumPad6
                e.Handled = True
                PoMove_for_Panel(6, as_Copy)
            Case Keys.D7, Keys.NumPad7
                e.Handled = True
                PoMove_for_Panel(7, as_Copy)
            Case Keys.D8, Keys.NumPad8
                e.Handled = True
                PoMove_for_Panel(8, as_Copy)
            Case Keys.D9, Keys.NumPad9
                e.Handled = True
                PoMove_for_Panel(9, as_Copy)
            Case Keys.D0, Keys.NumPad0
                e.Handled = True
                PoMove_for_Panel(0, as_Copy) ' Key '0' uses index 0

            Case Keys.Escape
                e.Handled = True
                ' Not in the middle of a bulk move/delete: FormClosing refuses anyway, and
                ' the operation must finish on the session that started it (audit B-14).
                If Not is_Bulk_Operation_Running Then Me.Close()
        End Select
    End Sub

    ' --- Selection and Deletion Logic ---

    Private Sub AddToSelection(pb As PictureBox)
        If Not selectedPictureControls.Contains(pb) Then
            selectedPictureControls.Add(pb)
        End If
    End Sub

    Private Sub RemoveFromSelection(pb As PictureBox)
        If selectedPictureControls.Contains(pb) Then
            selectedPictureControls.Remove(pb)
        End If
    End Sub

    Private Sub ToggleSelection(pb As PictureBox)
        If selectedPictureControls.Contains(pb) Then
            RemoveFromSelection(pb)
        Else
            AddToSelection(pb)
        End If
    End Sub

    Private Sub ClearSelection()
        selectedPictureControls.Clear()
    End Sub

    Private Sub UpdateSelectionVisuals()
        For Each pb As PictureBox In imagePanel.Controls.OfType(Of PictureBox)()
            pb.Invalidate()
        Next
    End Sub

    Private Sub InitializeTooltips()
        If toolTip Is Nothing Then
            toolTip = New ToolTip()
            ' Optional: Customize tooltip appearance and behavior
            toolTip.AutoPopDelay = 7000 ' Linger time
            toolTip.InitialDelay = 700  ' Time before appearing
            toolTip.ReshowDelay = 500   ' Time before reappearing
            toolTip.ShowAlways = True   ' Show even if form is not active
        End If

        Dim panelTooltipText As String = Localization.T("ЛКМ: Выбрать изображение" & vbCrLf & "Ctrl+ЛКМ: Добавить/убрать из выделения" & vbCrLf & "Двойной клик: Открыть изображение в главном окне" & vbCrLf & "Del: Удалить выделенные файлы (без лишних церемоний)" & vbCrLf & "Цифры (0-9): Переместить/копировать выделенные файлы" & vbCrLf & "Esc: Закрыть эту панель и сделать вид, что её не было")

#If Not NETFRAMEWORK Then
        ' On the mainline the digit alone always moves and the copy is on Shift, so say so
        ' here rather than leaving "move/copy" to be decided by a mode that no longer exists.
        panelTooltipText &= vbCrLf & Localization.T("Shift + цифра верхнего ряда: скопировать выделенные файлы (без Shift - перенести)")
#End If

        toolTip.SetToolTip(imagePanel, panelTooltipText)
    End Sub

    ''' <summary>
    ''' Marks the panel busy for the length of a bulk file operation: the caption says what
    ''' is happening and the grid stops taking clicks and keys, so a second operation cannot
    ''' start on a selection the first one is already consuming.
    ''' </summary>
    Private Sub SetPanelBusy(busy As Boolean, Optional caption As String = "")
        is_Bulk_Operation_Running = busy
        imagePanel.Enabled = Not busy
        Me.UseWaitCursor = busy
        Me.Text = If(busy, caption, Localization.T("Панель изображений"))
    End Sub

    ''' <summary>Takes a card out of the panel and off the window, keeping window_Last_Index
    ''' in step with allImageFiles - both shrink by one.</summary>
    Private Sub RemoveCardForVanishedFile(pb As PictureBox, file_Path As String)
        If allImageFiles.Remove(file_Path) AndAlso window_Last_Index > window_First_Index Then
            window_Last_Index -= 1
        End If
        imagePanel.Controls.Remove(pb)
        DisposeCard(pb)
    End Sub

    ''' <summary>
    ''' Deletes the whole selection. The I/O runs off the UI thread: this is the app's only
    ''' multi-file surface, and a selection of a few hundred files on a network share used to
    ''' block the message loop inside the loop - modal panel, no repaint, "Not Responding",
    ''' and every File.Delete waiting out the full network timeout with no way to abort.
    ''' </summary>
    Private Async Sub DeleteSelectedFiles()
        If is_Bulk_Operation_Running Then Return

        Dim work As New List(Of Tuple(Of PictureBox, String))()
        For Each pb As PictureBox In selectedPictureControls
            work.Add(Tuple.Create(pb, CStr(pb.Tag)))
        Next
        If work.Count = 0 Then Return

#If Not NETFRAMEWORK Then
        ' The same policy as every other deletion in the application (R-1 §3.8), decided
        ' ONCE for the selection: three hundred files out of one folder must not cost
        ' three hundred probes.
        Dim decision As DeleteDecision = SelectionDeleteDecision(work)
        Dim confirmMsg = DeleteText.ConfirmMany(decision, work.Count)
        ' The same setting the viewer asks by (Ф4). A selection of three hundred files is
        ' the last place two surfaces should disagree about whether deleting asks.
        Dim mustAsk As Boolean = DeletePolicy.ShouldConfirm(Main_Form.GetModernPreferences().ConfirmDelete, decision)
#Else
        Dim confirmMsg = Localization.TF("Вы уверены, что хотите безвозвратно удалить {0} файл(ов)?", work.Count)
        Dim mustAsk As Boolean = Not Is_no_request_before_file_operation
#End If
        ' The safe answer holds the default focus (APP-BEHAVIOUR 0.12 rule 5): Enter declines.
        If mustAsk AndAlso
            ViewerBox.Show(confirmMsg, Localization.TC("panel", "Подтверждение удаления"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return

        Dim errors As New System.Text.StringBuilder()
        Dim done As Integer = 0

        SetPanelBusy(True, Localization.T("Удаление.."))
        Try
            For Each item In work
                Dim pb As PictureBox = item.Item1
                Dim filePath As String = item.Item2

                SetPanelBusy(True, Localization.TF("{0}: {1} из {2}", Localization.T("Удаление.."), done + 1, work.Count))

                Dim failure As Exception = Nothing
                ' The bin was promised and the shell deleted the file for good instead
                ' (audit 057.S05-2) - the confirmation said "Recycle Bin", so say otherwise.
                Dim went_For_Good As Boolean = False
                Await Task.Run(Sub()
                                   Try
#If Not NETFRAMEWORK Then
                                       went_For_Good = RecycleBinIo.DeleteAs(filePath, decision)
#Else
                                       File.Delete(filePath)
#End If
                                   Catch ex As Exception
                                       failure = ex
                                   End Try
                               End Sub)
                If Me.IsDisposed Then Return

                If failure Is Nothing Then
                    RemoveCardForVanishedFile(pb, filePath)
                    done += 1
                    If went_For_Good Then
                        AppFileLogger.WriteLine("Thumbnail panel: Recycle Bin kept no record - the file was deleted permanently: " & filePath)
                        errors.AppendLine(Localization.TF("! Корзина не приняла файл - он удалён безвозвратно: {0}", filePath).TrimStart("!"c, " "c))
                    End If
                Else
                    AppFileLogger.LogException("Thumbnail panel: delete " & filePath, failure)
                    errors.AppendLine(Localization.T("Не удалось удалить файл: ") & filePath & " - " & FailureText.Describe(failure))
                End If
            Next
        Finally
            If Not Me.IsDisposed Then SetPanelBusy(False)
        End Try

        ClearSelection()
        UpdateSelectionVisuals()

        If errors.Length > 0 Then
            ViewerBox.Show(errors.ToString(), Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0080: pics deleted: " & done.ToString())
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' One verdict for the whole selection, probing each distinct volume root once.
    '''
    ''' A mixed selection (possible only with subfolder scanning) takes the PERMANENT
    ''' answer, because that is the true one for at least one of the files, and a dialog
    ''' promising the Recycle Bin to a file the share will swallow is exactly the lie this
    ''' work exists to remove.
    '''
    ''' The size is deliberately not passed: the quota rule is per file, this decision is
    ''' per selection, and stat-ing three hundred files over SMB to sharpen one sentence
    ''' is not a trade worth making. It leaves §6.1 in force - the shell has the last word.
    ''' </summary>
    Private Function SelectionDeleteDecision(work As List(Of Tuple(Of PictureBox, String))) As DeleteDecision
        Dim verdict As DeleteDecision = Nothing
        Dim roots_Seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        For Each item In work
            Dim root As String = DeleteVolumeProbe.VolumeRootOf(item.Item2)
            If Not roots_Seen.Add(root) Then Continue For

            Dim one As DeleteDecision = DeletePolicy.Decide(DeleteVolumeProbe.FactsFor(item.Item2),
                                                            -1,
                                                            binEnabledBySetting:=Main_Form.GetModernPreferences().DeleteToRecycleBin,
                                                            forcedPermanent:=False)
            If verdict Is Nothing OrElse (Not verdict.IsPermanent AndAlso one.IsPermanent) Then verdict = one
        Next

        If verdict Is Nothing Then
            verdict = New DeleteDecision With {.Outcome = DeleteOutcome.Permanent,
                                               .Reason = PermanentReason.VolumeUnknown}
        End If
        Return verdict
    End Function
#End If

    Private Sub InitializeState()
        ReleaseAllCards()
        allImageFiles.Clear()
        window_First_Index = 0
        window_Last_Index = 0
        Dim current_Folder_Path As String = Main_Form.Current_Folder_Path
        If Not String.IsNullOrEmpty(current_Folder_Path) AndAlso Directory.Exists(current_Folder_Path) Then
            Dim extensions As String() = Main_Form.Image_File_Extensions
#If Not NETFRAMEWORK Then
            Dim thumbnailExtensions As New HashSet(Of String)(extensions, StringComparer.OrdinalIgnoreCase)
            Dim preferences As ModernViewerPreferences = Main_Form.GetModernPreferences()
            If preferences.IncludeDocumentInNavigation AndAlso preferences.DocumentExtensionSet().Contains(".pdf") Then thumbnailExtensions.Add(".pdf")
            Dim files = Directory.GetFiles(current_Folder_Path).Where(Function(f) thumbnailExtensions.Contains(Path.GetExtension(f))).ToList()
#Else
            Dim files = Directory.GetFiles(current_Folder_Path).Where(Function(f) extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList()
#End If
            Dim sortIndex As Integer = sortIndexFromMainForm
            Try
                If sortIndex = 0 AndAlso Main_Form.cmbox_Sort IsNot Nothing Then
                    sortIndex = Main_Form.cmbox_Sort.SelectedIndex
                End If
            Catch
                sortIndex = 0
            End Try

            Select Case sortIndex
                Case 0 : files = files.OrderBy(Function(f) Path.GetFileName(f)).ToList()
                Case 1 : files = files.OrderByDescending(Function(f) Path.GetFileName(f)).ToList()
                Case 2 : Dim rnd As New Random() : files = files.OrderBy(Function(f) rnd.Next()).ToList()
                Case 3 : files = files.OrderByDescending(Function(f) New FileInfo(f).Length).ToList()
                Case 4 : files = files.OrderBy(Function(f) New FileInfo(f).Length).ToList()
                Case 5 : files = files.OrderByDescending(Function(f) New FileInfo(f).LastWriteTime).ToList()
                Case 6 : files = files.OrderBy(Function(f) New FileInfo(f).LastWriteTime).ToList()
                Case 7 : files = files.OrderBy(Function(f) Path.GetFileName(f), New NaturalFilenameComparer()).ToList()
                Case 8 : files = files.OrderByDescending(Function(f) Path.GetFileName(f), New NaturalFilenameComparer()).ToList()
                Case Else : files = files.OrderBy(Function(f) f).ToList()
            End Select
            allImageFiles = files
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0090: Pics sorted")
        End If
    End Sub

    Private Async Function FillVisibleAreaAsync() As Task
        Dim generation As Integer = CurrentLoadGeneration()
        ' A hidden panel may never show a scrollbar, so without the Visible test this walked
        ' to the end of the folder behind a closed window (audit B-07).
        While Me.Visible AndAlso Not imagePanel.VerticalScroll.Visible AndAlso window_Last_Index < allImageFiles.Count
            If is_Loading Then Return
            Await LoadNextBatchAsync()
            Await Task.Delay(10)
            If generation <> CurrentLoadGeneration() Then Return
        End While
    End Function

    Private Sub Image_Panel_Form_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        If Not MultiWindowPolicy.IsSecondaryInstance() Then
#If NETFRAMEWORK Then
            If Me.Top >= 0 Then SaveSetting(App_name, Second_App_Name, "PicturePanelTop", Me.Top.ToString)
            If Me.Left >= 0 Then SaveSetting(App_name, Second_App_Name, "PicturePanelLeft", Me.Left.ToString)
            If Me.Height >= 200 Then SaveSetting(App_name, Second_App_Name, "PicturePanelHeight", Me.Height.ToString)
            If Me.Width >= 320 Then SaveSetting(App_name, Second_App_Name, "PicturePanelWidth", Me.Width.ToString)
#Else
            ' Negative coordinates are a monitor left of or above the primary one, and a
            ' maximized panel remembers its normal rectangle, not the screen's.
            Dim normal_Bounds As Rectangle = WindowPlacement.NormalRectangle(Me.WindowState, Me.Bounds, Me.RestoreBounds)
            SaveSetting(App_name, Second_App_Name, "PicturePanelTop", normal_Bounds.Top.ToString)
            SaveSetting(App_name, Second_App_Name, "PicturePanelLeft", normal_Bounds.Left.ToString)
            If normal_Bounds.Height >= 200 Then SaveSetting(App_name, Second_App_Name, "PicturePanelHeight", normal_Bounds.Height.ToString)
            If normal_Bounds.Width >= 320 Then SaveSetting(App_name, Second_App_Name, "PicturePanelWidth", normal_Bounds.Width.ToString)
#End If
        End If

        If toolTip IsNot Nothing Then
            toolTip.Dispose()
            toolTip = Nothing
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0100: PicPanel closed")
    End Sub

    ''' <summary>One empty card for a file. The thumbnail arrives later, off-thread.</summary>
    Private Function CreateThumbnailCard(file_Path As String) As PictureBox
        Dim pb As New PictureBox()
        With pb
            .Width = Picture_Box_Width_At_Panel
            .Height = Picture_Box_Height_At_Panel
            .BorderStyle = BorderStyle.None ' Border is now custom painted
            .Tag = file_Path
        End With
        AddHandler pb.Click, AddressOf OnPictureBoxClick
        AddHandler pb.Paint, AddressOf OnPictureBoxPaint
        AddHandler pb.DoubleClick, AddressOf OnPictureBoxDoubleClick ' Add DoubleClick handler
        Return pb
    End Function

    ''' <summary>
    ''' Decodes the thumbnail off the UI thread and hands it to its card.
    '''
    ''' Every early exit disposes the bitmap. It used to return without one when the card had
    ''' been recycled or the form closed mid-decode - and since PictureBox.Dispose does not
    ''' touch an Image assigned through the property, that bitmap had nothing left to reach
    ''' it either.
    ''' </summary>
    Private Function FillThumbnailAsync(pb As PictureBox, file_Path As String) As Task
        Dim generation As Integer = CurrentLoadGeneration()
        Return Task.Run(Sub()
                            ' The session this card belongs to is over (the panel closed or
                            ' was re-prepared): skip the full-size decode entirely (audit B-07).
                            If generation <> CurrentLoadGeneration() OrElse pb.IsDisposed Then Return

                            Dim thumbnail As Image = CreateThumbnail(file_Path, Picture_Box_Width_At_Panel, Picture_Box_Height_At_Panel)
                            ' Set inside the marshalled delegate once the bitmap has an owner
                            ' (the card, or its own Dispose). Until then it is ours: a failed
                            ' Invoke used to leave it to the finalizer, because the local had
                            ' already been cleared (audit B-19).
                            Dim handed_Over As Image = Nothing
                            Dim delivered As Boolean = False
                            Try
                                If Me.IsDisposed OrElse pb.IsDisposed OrElse CStr(pb.Tag) <> file_Path OrElse
                                   generation <> CurrentLoadGeneration() Then
                                    thumbnail?.Dispose()
                                    Return
                                End If
                                If thumbnail IsNot Nothing Then
                                    handed_Over = thumbnail
                                    thumbnail = Nothing
                                    pb.Invoke(New Action(Sub()
                                                             delivered = True
                                                             If pb.IsDisposed OrElse CStr(pb.Tag) <> file_Path Then
                                                                 handed_Over.Dispose()
                                                                 Return
                                                             End If
                                                             pb.Image?.Dispose()
                                                             pb.Image = handed_Over
                                                         End Sub))
                                Else
#If NETFRAMEWORK Then
                                    pb.Invoke(New Action(Sub() pb.BackColor = Color.Red))
#Else
                                    pb.Invoke(New Action(Sub() ShowMissingThumbnail(pb, file_Path)))
#End If
                                End If
                            Catch
                                ' The card or the form went away between the checks and the
                                ' marshal - the bitmap is still ours to release.
                                thumbnail?.Dispose()
                                If Not delivered Then handed_Over?.Dispose()
                            End Try
                        End Sub)
    End Function

    ''' <summary>
    ''' Drops every card and everything it holds. Called when the panel is re-prepared and
    ''' from Main_Form once the dialog is closed - the form instance is reused and reachable
    ''' from a Main_Form field, so without this its last session's thumbnails stayed resident
    ''' for as long as the viewer ran, which on a big folder at a large card size is hundreds
    ''' of megabytes behind a window that is not even open.
    ''' </summary>
    Friend Sub ReleaseAllCards()
        ' Ends the load session first: every loader still awaiting stops at its next check.
        System.Threading.Interlocked.Increment(load_Generation)
        ClearSelection()
        initial_Target_PictureBox = Nothing

        Dim cards As New List(Of PictureBox)(imagePanel.Controls.OfType(Of PictureBox)())
        imagePanel.SuspendLayout()
        Try
            imagePanel.Controls.Clear()
        Finally
            imagePanel.ResumeLayout()
        End Try
        For Each pb As PictureBox In cards
            DisposeCard(pb)
        Next

        window_First_Index = 0
        window_Last_Index = 0
    End Sub

    ''' <summary>Releases a card and the thumbnail bitmap it carries. PictureBox.Dispose
    ''' disposes neither, which is why removed cards used to leak their bitmap.</summary>
    Private Sub DisposeCard(pb As PictureBox)
        If pb Is Nothing Then Return
        If pb Is initial_Target_PictureBox Then initial_Target_PictureBox = Nothing
        RemoveFromSelection(pb)
        Try
            Dim carried As Image = pb.Image
            pb.Image = Nothing
            carried?.Dispose()
        Catch
        End Try
        Try
            pb.Dispose()
        Catch
        End Try
    End Sub

    ''' <summary>Grows the window downwards by one batch.</summary>
    Private Async Function LoadNextBatchAsync() As Task
        If is_Loading OrElse window_Last_Index >= allImageFiles.Count Then Return
        Try
            is_Loading = True
            Dim batchSize As Integer = ColumnsPerRow() * 2
            Dim startIndex As Integer = window_Last_Index
            Dim endIndex As Integer = Math.Min(startIndex + batchSize - 1, allImageFiles.Count - 1)
            If startIndex > endIndex Then Return

            Dim tasks As New List(Of Task)()
            For i = startIndex To endIndex
                Dim filePath As String = allImageFiles(i)
                Dim pb As PictureBox = CreateThumbnailCard(filePath)
                imagePanel.Controls.Add(pb)
                tasks.Add(FillThumbnailAsync(pb, filePath))
            Next
            window_Last_Index = endIndex + 1
            TrimWindow(from_Front:=True)
            Await Task.WhenAll(tasks)
        Finally
            is_Loading = False
        End Try
    End Function

    ''' <summary>Grows the window upwards by one batch - the direction the old code had no
    ''' way to go, because it always started from the beginning of the folder.</summary>
    Private Async Function LoadPreviousBatchAsync() As Task
        If is_Loading OrElse window_First_Index <= 0 Then Return
        Try
            is_Loading = True
            Dim batchSize As Integer = ColumnsPerRow() * 2
            Dim endIndex As Integer = window_First_Index - 1
            Dim startIndex As Integer = Math.Max(0, endIndex - batchSize + 1)

            ' FlowLayoutPanel lays its children out in Controls order, so prepending is
            ' Add + SetChildIndex to the front.
            Dim scroll_Before As Integer = 0
            Try
                scroll_Before = -imagePanel.AutoScrollPosition.Y
            Catch
            End Try

            Dim tasks As New List(Of Task)()
            Dim insert_At As Integer = 0
            For i = startIndex To endIndex
                Dim filePath As String = allImageFiles(i)
                Dim pb As PictureBox = CreateThumbnailCard(filePath)
                imagePanel.Controls.Add(pb)
                imagePanel.Controls.SetChildIndex(pb, insert_At)
                insert_At += 1
                tasks.Add(FillThumbnailAsync(pb, filePath))
            Next
            window_First_Index = startIndex
            TrimWindow(from_Front:=False)

            ' Keep what the user was looking at where it was: the rows just prepended push
            ' it down, so move the scroll offset down by the same height - the mirror of
            ' TrimWindow's compensation. Left at 0, the view jumped and the next paint
            ' found "at the top, more above" again and loaded another batch, and another
            ' (audit B-15).
            Dim cols As Integer = ColumnsPerRow()
            Dim added_Rows As Integer = (insert_At + cols - 1) \ cols
            Try
                imagePanel.AutoScrollPosition = New Point(0, scroll_Before + added_Rows * RowHeight())
            Catch
            End Try
            Await Task.WhenAll(tasks)
        Finally
            is_Loading = False
        End Try
    End Function

    ''' <summary>
    ''' Keeps the live-card count under Max_Live_Thumbnails by dropping whole rows from the
    ''' end AWAY from the batch just added. Whole rows so the grid never shifts sideways, and
    ''' when the drop happens at the top the scroll offset is moved down by the same height -
    ''' otherwise the content the user was looking at would jump.
    ''' </summary>
    Private Sub TrimWindow(from_Front As Boolean)
        Dim excess As Integer = (window_Last_Index - window_First_Index) - Max_Live_Thumbnails
        If excess <= 0 Then Return

        Dim cols As Integer = ColumnsPerRow()
        Dim rows As Integer = excess \ cols
        If rows <= 0 Then Return
        Dim drop As Integer = rows * cols

        Dim scroll_Before As Integer = 0
        Try
            scroll_Before = -imagePanel.AutoScrollPosition.Y
        Catch
        End Try

        imagePanel.SuspendLayout()
        Try
            For n As Integer = 1 To drop
                If imagePanel.Controls.Count = 0 Then Exit For
                Dim idx As Integer = If(from_Front, 0, imagePanel.Controls.Count - 1)
                Dim pb As PictureBox = TryCast(imagePanel.Controls(idx), PictureBox)
                If pb Is Nothing Then Exit For
                imagePanel.Controls.Remove(pb)
                DisposeCard(pb)
            Next
        Finally
            imagePanel.ResumeLayout()
        End Try

        If from_Front Then
            window_First_Index += drop
            Try
                imagePanel.AutoScrollPosition = New Point(0, Math.Max(0, scroll_Before - rows * RowHeight()))
            Catch
            End Try
        Else
            window_Last_Index -= drop
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0035: PicPanel trimmed " & drop.ToString() &
                        " cards, window " & window_First_Index.ToString() & ".." & window_Last_Index.ToString())
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' A card whose picture could not be decoded shows what the file IS - the vocabulary's
    ''' content.image or content.video glyph in the panel's text colour - instead of the
    ''' old solid red tile, which read as an error mark rather than as a file
    ''' (ICON-EXTERNAL rule 4, spec 027 phase 9).
    ''' </summary>
    Private Sub ShowMissingThumbnail(pb As PictureBox, file_Path As String)
        If pb.IsDisposed OrElse CStr(pb.Tag) <> file_Path Then Return
        Dim mapName As String = If(MediaKindClassifier.KindOf(file_Path) = MediaKind.Video,
                                   "thumbnail_Video_Missing", "thumbnail_Image_Missing")
        Dim meaning As IconMeaning = IconMap.GetMeaning(mapName)
        Dim w As Integer = Math.Max(1, pb.Width)
        Dim h As Integer = Math.Max(1, pb.Height)
        ' The largest size tier that leaves the glyph at most half the card.
        Dim tier As Integer = 16
        For Each candidate As Integer In New Integer() {20, 24, 32, 40, 48}
            If candidate * 2 <= Math.Min(w, h) Then tier = candidate
        Next
        ' Ours until the card takes it: a glyph that fails to render used to abandon the
        ' half-drawn bitmap to the finalizer (audit 057.S03).
        Dim card As Bitmap = Nothing
        Try
            card = New Bitmap(w, h)
            Using g As Graphics = Graphics.FromImage(card)
                g.Clear(Me.BackColor)
                Using glyph As Bitmap = GlyphRenderer.Render(meaning.GlyphId, tier, Me.ForeColor, AddressOf RuntimeBootstrap.OpenBundledAsset)
                    g.DrawImage(glyph, (w - tier) \ 2, (h - tier) \ 2, tier, tier)
                End Using
            End Using
            pb.Image?.Dispose()
            pb.Image = card
            card = Nothing
            pb.AccessibleName = Localization.T(meaning.LocalizationKey)
        Catch
            ' A missing glyph resource must not take the panel down; the old red tile
            ' is still a visible "this one failed".
            card?.Dispose()
            pb.BackColor = Color.Red
        End Try
    End Sub
#End If

    Private Function CreateThumbnail(imagePath As String, width As Integer, height As Integer) As Image
        Try
#If Not NETFRAMEWORK Then
            If String.Equals(Path.GetExtension(imagePath), ".pdf", StringComparison.OrdinalIgnoreCase) Then
                Using document As New PdfDocumentSource()
                    document.Open(imagePath)
                    Return document.RenderPage(1, width, height)
                End Using
            End If
#End If
#If NETFRAMEWORK Then
            Dim imageData = LoadImageWithStream(imagePath)
#Else
            ' A thumbnail of an animation is its first frame; the whole transcode of a long one is
            ' seconds of work for one grid cell (spec 241 rule 14).
            Dim thumbnailFailure As PathFailureKind = PathFailureKind.Content
            Dim imageData = LoadImageStaged(imagePath, thumbnailFailure, ImageLoadStage.FirstFrame)
#End If
            If imageData Is Nothing Then Return Nothing

            Using ms As MemoryStream = imageData.Item2
                Using originalImage As Image = imageData.Item1
                    Dim sourceWidth = originalImage.Width
                    Dim sourceHeight = originalImage.Height
                    Dim nPercentW = CSng(width) / CSng(sourceWidth)
                    Dim nPercentH = CSng(height) / CSng(sourceHeight)
                    Dim nPercent = Math.Min(nPercentW, nPercentH)
                    Dim destWidth = CInt(sourceWidth * nPercent)
                    Dim destHeight = CInt(sourceHeight * nPercent)
                    Dim destX = (width - destWidth) \ 2
                    Dim destY = (height - destHeight) \ 2
                    Dim bmPhoto As New Bitmap(width, height, Imaging.PixelFormat.Format32bppArgb)
                    Try
                        Dim dpiX As Single = If(originalImage.HorizontalResolution > 0, originalImage.HorizontalResolution, 96.0F)
                        Dim dpiY As Single = If(originalImage.VerticalResolution > 0, originalImage.VerticalResolution, 96.0F)
                        bmPhoto.SetResolution(dpiX, dpiY)
                        Using grPhoto As Graphics = Graphics.FromImage(bmPhoto)
                            grPhoto.Clear(Me.BackColor)  ' Use panel background color
                            grPhoto.InterpolationMode = InterpolationMode.Low
                            grPhoto.DrawImage(originalImage, New Rectangle(destX, destY, destWidth, destHeight), New Rectangle(0, 0, sourceWidth, sourceHeight), GraphicsUnit.Pixel)
                        End Using
                    Catch
                        ' A draw that fails (GDI+ out of memory on a huge original, a broken
                        ' frame) used to abandon the thumbnail bitmap; the outer Catch still
                        ' turns the failure into "no thumbnail" (audit 057.S03).
                        bmPhoto.Dispose()
                        Throw
                    End Try
                    Return bmPhoto
                End Using
            End Using
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Moves (or, with <paramref name="as_Copy"/>, copies) the whole selection into a
    ''' recipient slot. The panel keeps its own multi-file implementation - it is the one
    ''' surface that acts on many files at once, so it does not go through the main
    ''' window's single-file queue - but the ACTION is now a parameter, not a global mode
    ''' read: on the mainline there is nothing left to switch that mode with, and the panel
    ''' would have been stuck moving forever (SPECIFICATION_COPY_ACTIONS_REWORK.md §4.6,
    ''' the second of the two allowed solutions).
    ''' </summary>
    Private Async Sub PoMove_for_Panel(ByVal move_Slot_index As Integer, Optional ByVal as_Copy As Boolean = False)
        If is_Bulk_Operation_Running Then Return
        If selectedPictureControls.Count = 0 Then Return

        Dim recipient_Slot As Integer = If(move_Slot_index = 0, 10, move_Slot_index)
        If recipient_Slot < 1 OrElse recipient_Slot > 10 OrElse
           String.IsNullOrWhiteSpace(Hardkeys_to_move_mediafile(recipient_Slot)) Then Return

#If NETFRAMEWORK Then
        Dim copying As Boolean = Is_Copying_not_Moving
#Else
        Dim copying As Boolean = as_Copy
#End If

        ' In Main_Form, key '0' corresponds to index 10
        Dim destination_Folder_Path As String = Hardkeys_to_move_mediafile(recipient_Slot)

        Dim work As New List(Of Tuple(Of PictureBox, String))()
        For Each pb As PictureBox In selectedPictureControls
            work.Add(Tuple.Create(pb, CStr(pb.Tag)))
        Next

        Dim operation_type_string = If(copying, Localization.T("копировать"), Localization.T("переместить"))
        Dim confirmMsg = Localization.TF("Вы уверены, что хотите {0} {1} файл(ов) в '{2}'?", operation_type_string, work.Count, destination_Folder_Path)

        If Not Is_no_request_before_file_operation AndAlso
ViewerBox.Show(confirmMsg, Localization.T("Подтверждение"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return

        Dim success_count As Integer = 0
        Dim error_messages As New System.Text.StringBuilder()
        Dim busy_Caption As String = If(copying, Localization.T("Копирование.."), Localization.T("Перенос.."))

        ' Off the UI thread, one file at a time: a move between two different shares is a
        ' full byte copy over the wire, and a selection of a few hundred used to freeze the
        ' modal panel - and the whole app - for the length of the transfer.
        SetPanelBusy(True, busy_Caption)
        Try
            For Each item In work
                Dim pbToProcess As PictureBox = item.Item1
                Dim source_file_path As String = item.Item2
                Dim destination_file_path As String = Path.Combine(destination_Folder_Path, Path.GetFileName(source_file_path))

                SetPanelBusy(True, Localization.TF("{0}: {1} из {2}", busy_Caption, success_count + 1, work.Count))

                Dim failure As Exception = Nothing
                Dim name_Taken As Boolean = False
                Await Task.Run(Sub()
                                   Try
                                       ' Never over a file that is already there: IMG_0001.jpg
                                       ' from a second card is a different photo, and this
                                       ' loop has no undo and no Recycle Bin (audit 046, T-01 -
                                       ' the interim guard until 005 PG-1 routes the panel
                                       ' through the main window's one implementation).
                                       ' Through FileManager, never File.Copy/File.Move: those
                                       ' are the staged routines, so an interrupted transfer
                                       ' leaves no half file under the real name (audit 047,
                                       ' C-08) and a read-only source still moves (C-16).
                                       If File.Exists(destination_file_path) Then
                                           name_Taken = True
                                       ElseIf copying Then
                                           FileManager.CopyFile(source_file_path, destination_file_path)
                                       Else
                                           FileManager.MoveFile(source_file_path, destination_file_path)
                                       End If
                                   Catch ex As Exception
                                       failure = ex
                                   End Try
                               End Sub)
                If Me.IsDisposed Then Return

                If name_Taken Then
                    ' Logged as well as listed in the summary below (audit 057.S03, ticket 088).
                    AppFileLogger.WriteLine("Thumbnail panel: skipped, name already taken in the slot: " & destination_file_path)
                    error_messages.AppendLine(Localization.TF("Не удалось обработать {0}: {1}", source_file_path,
                                                              Localization.T("Файл с таким именем уже есть: операция пропущена")))
                ElseIf failure Is Nothing Then
                    ' Only a MOVE takes the file out of this folder; a copy leaves it here.
                    If Not copying Then RemoveCardForVanishedFile(pbToProcess, source_file_path)
                    success_count += 1
                Else
                    AppFileLogger.LogException("Thumbnail panel: move/copy " & source_file_path, failure)
                    error_messages.AppendLine(Localization.TF("Не удалось обработать {0}: {1}", source_file_path, FailureText.Describe(failure)))
                End If
            Next
        Finally
            If Not Me.IsDisposed Then SetPanelBusy(False)
        End Try

        ClearSelection()
        UpdateSelectionVisuals() ' Refresh the panel

        Dim summary_message As New System.Text.StringBuilder()
        summary_message.AppendLine(Localization.TF("{0} из {1} файлов обработано.", success_count, work.Count))
        If error_messages.Length > 0 Then
            summary_message.AppendLine(Localization.T("Ошибки:"))
            summary_message.Append(error_messages.ToString())
        End If

        ' "No confirmation" means do not ask before acting, not do not say what failed: a
        ' skipped or failed file is always reported (ticket 088); only the all-success
        ' summary is suppressed under no-confirm.
        Dim has_Errors As Boolean = error_messages.Length > 0
        If has_Errors OrElse Not Is_no_request_before_file_operation Then
            ViewerBox.Show(summary_message.ToString(), Localization.T("Операция завершена"), MessageBoxButtons.OK,
                            If(has_Errors, MessageBoxIcon.Warning, MessageBoxIcon.Information))
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & $" p0085: pics moved/copied. Success: {success_count}, Failed: {work.Count - success_count}")
    End Sub

    Private Sub Image_Panel_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        ' A bulk operation keeps running after the window closes: files kept moving with no
        ' progress shown, and a panel reopened meanwhile had its file list and window
        ' indices edited by the old loop (audit B-14). The user waits for "N of M" to end;
        ' Windows shutting down or the app exiting still closes.
        If is_Bulk_Operation_Running AndAlso e.CloseReason = CloseReason.UserClosing Then
            e.Cancel = True
            System.Media.SystemSounds.Beep.Play()
            Return
        End If

        If toolTip IsNot Nothing Then
            toolTip.Dispose()
            toolTip = Nothing
        End If
    End Sub
#End If
End Class
