Imports System.ComponentModel
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports System.Windows.Forms

Public Class Image_Panel_Form
    Inherits Form

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

    Private imagePanel As FlowLayoutPanel
    ' --- State Management ---
    Private allImageFiles As New List(Of String)()
    Private selectedPictureControls As New List(Of PictureBox)()
    Private initial_Target_PictureBox As PictureBox = Nothing ' To store the special target PictureBox
    Private currently_Loaded_Index As Integer = 0
    Private is_Loading As Boolean = False
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

    Public Sub New()
        Me.Text = If(Is_Russian_Language, "Панель изображений", "Image Panel")
        Me.KeyPreview = True

        imagePanel = New FlowLayoutPanel()
        With imagePanel
            .Dock = DockStyle.Fill
            .AutoScroll = True
            .FlowDirection = FlowDirection.LeftToRight
        End With

        imagePanel.BackColor = If(Form_Color_Scheme = 0, Color.Black, Color.White)
        Me.BackColor = imagePanel.BackColor

        Me.Controls.Add(imagePanel)

        AddHandler Me.Resize, AddressOf OnFormResize
        AddHandler Me.VisibleChanged, AddressOf OnVisibleChanged ' CHANGED: Use VisibleChanged instead of Shown
        AddHandler Me.KeyDown, AddressOf OnFormKeyDown
        AddHandler imagePanel.Paint, AddressOf OnPanelPaint
        resizeDebounceTimer.Interval = resize_debounce_interval
        AddHandler resizeDebounceTimer.Tick, AddressOf OnResizeTimerTick
    End Sub

    Public Sub SetSortIndexFromMainForm(idx As Integer)
        sortIndexFromMainForm = idx
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' PrepareForDisplay is now called from Main_Form before showing, which is correct.
    End Sub

    Public Sub PrepareForDisplay()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0010: PicPanel init")

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
        app_Top_Int = If(app_Top_Int < 0 OrElse app_Top_Int > picture_panel_position_Limit_Top, first_run_top, app_Top_Int)
        app_Left_Int = If(app_Left_Int < 0 OrElse app_Left_Int > picture_panel_position_Limit_Left, first_run_left, app_Left_Int)
        app_Width_Int = If(app_Width_Int < picture_panel_position_Limit_Width_Low OrElse app_Width_Int > picture_panel_position_Limit_Width, first_run_width, app_Width_Int)
        app_Height_Int = If(app_Height_Int < picture_panel_position_Limit_Height_Low OrElse app_Height_Int > picture_panel_position_Limit_Height, first_run_height, app_Height_Int)

        Me.SetBounds(app_Left_Int, app_Top_Int, app_Width_Int, app_Height_Int)

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0020: Form_Sizes: " & app_Left_Int.ToString & " - " & app_Top_Int.ToString & " " & app_Width_Int.ToString & " - " & app_Height_Int.ToString)
    End Sub

    ' ADDED: New handler for the VisibleChanged event
    Private Async Sub OnVisibleChanged(sender As Object, e As EventArgs)
        If Me.Visible Then
            Await LoadContentAsync()
        End If
    End Sub

    ' ADDED: New method containing the async loading logic
    Private Async Function LoadContentAsync() As Task
        Dim targetFile = Current_File_Name

        initial_Target_PictureBox = Nothing ' Reset special highlight

        If String.IsNullOrEmpty(targetFile) OrElse Not allImageFiles.Contains(targetFile) Then
            Await FillVisibleAreaAsync()
            Return
        End If

        While initial_Target_PictureBox Is Nothing AndAlso currently_Loaded_Index < allImageFiles.Count
            If is_Loading Then
                Await Task.Delay(50)
                Continue While
            End If
            Await LoadNextBatchAsync()
            initial_Target_PictureBox = imagePanel.Controls.OfType(Of PictureBox)().FirstOrDefault(Function(p) CStr(p.Tag) = targetFile)
        End While

        If initial_Target_PictureBox IsNot Nothing Then
            imagePanel.ScrollControlIntoView(initial_Target_PictureBox)
            ClearSelection()
            AddToSelection(initial_Target_PictureBox)
            UpdateSelectionVisuals()
        End If

        Await FillVisibleAreaAsync()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0030: PicPanel content loaded")
    End Function

    ' REMOVED: The OnFormShown event handler is no longer needed.

    Private Sub OnFormResize(sender As Object, e As EventArgs)
        resizeDebounceTimer.Stop()
        resizeDebounceTimer.Start()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0040: PicPanel resized")
    End Sub

    Private Sub OnResizeTimerTick(sender As Object, e As EventArgs)
        resizeDebounceTimer.Stop()
        FillVisibleAreaAsync()
    End Sub

    Private Sub OnPanelPaint(sender As Object, e As PaintEventArgs)
        If is_Loading OrElse Not imagePanel.VerticalScroll.Visible Then Return
        If imagePanel.VerticalScroll.Value >= (imagePanel.VerticalScroll.Maximum - imagePanel.VerticalScroll.LargeChange) Then
            LoadNextBatchAsync()
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0050: PicPanel repaint")
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

        Select Case e.KeyCode
            Case Keys.Delete
                e.Handled = True
                DeleteSelectedFiles()

            Case Keys.D1, Keys.NumPad1
                e.Handled = True
                PoMove_for_Panel(1)
            Case Keys.D2, Keys.NumPad2
                e.Handled = True
                PoMove_for_Panel(2)
            Case Keys.D3, Keys.NumPad3
                e.Handled = True
                PoMove_for_Panel(3)
            Case Keys.D4, Keys.NumPad4
                e.Handled = True
                PoMove_for_Panel(4)
            Case Keys.D5, Keys.NumPad5
                e.Handled = True
                PoMove_for_Panel(5)
            Case Keys.D6, Keys.NumPad6
                e.Handled = True
                PoMove_for_Panel(6)
            Case Keys.D7, Keys.NumPad7
                e.Handled = True
                PoMove_for_Panel(7)
            Case Keys.D8, Keys.NumPad8
                e.Handled = True
                PoMove_for_Panel(8)
            Case Keys.D9, Keys.NumPad9
                e.Handled = True
                PoMove_for_Panel(9)
            Case Keys.D0, Keys.NumPad0
                e.Handled = True
                PoMove_for_Panel(0) ' Key '0' uses index 0

            Case Keys.Escape
                e.Handled = True
                Me.Close()
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

        Dim panelTooltipText As String = If(Is_Russian_Language,
    "ЛКМ: Выбрать изображение" & vbCrLf &
    "Ctrl+ЛКМ: Добавить/убрать из выделения" & vbCrLf &
    "Двойной клик: Открыть изображение в главном окне" & vbCrLf &
    "Del: Удалить выделенные файлы" & vbCrLf &
    "Цифры (0-9): Переместить/копировать выделенные файлы" & vbCrLf &
    "Esc: Закрыть эту панель",
    "Left-Click: Select image" & vbCrLf &
    "Ctrl+Click: Add/remove from selection" & vbCrLf &
    "Double-Click: Open image in main window" & vbCrLf &
    "Del: Delete selected file(s)" & vbCrLf &
    "Number Keys (0-9): Move/copy selected file(s)" & vbCrLf &
    "Esc: Close this panel")

        toolTip.SetToolTip(imagePanel, panelTooltipText)
    End Sub

    Private Sub DeleteSelectedFiles()
        Dim filesToDelete = selectedPictureControls.Select(Function(pb) CStr(pb.Tag)).ToList()
        Dim confirmMsg = If(Is_Russian_Language, $"Вы уверены, что хотите безвозвратно удалить {filesToDelete.Count} файл(ов)?", $"Are you sure you want to permanently delete {filesToDelete.Count} file(s)?")
        If Not Is_no_request_before_file_operation AndAlso
            MessageBox.Show(confirmMsg, If(Is_Russian_Language, "Подтверждение удаления", "Deletion.."), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return

        For Each pbToDelete In selectedPictureControls.ToList() ' Use ToList to create a copy for safe iteration
            If pbToDelete Is initial_Target_PictureBox Then initial_Target_PictureBox = Nothing
            Dim filePath = CStr(pbToDelete.Tag)
            Try
                File.Delete(filePath)
                allImageFiles.Remove(filePath)
                imagePanel.Controls.Remove(pbToDelete)
                pbToDelete.Dispose()
            Catch ex As Exception
                MessageBox.Show(If(Is_Russian_Language, "Не удалось удалить файл: ", "Fail to delete file:") & filePath & vbCrLf & ex.Message, If(Is_Russian_Language, "Ошибка", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        Next
        ClearSelection()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0080: pics deleted")
    End Sub

    Private Sub InitializeState()
        ClearSelection()
        imagePanel.Controls.Clear()
        allImageFiles.Clear()
        currently_Loaded_Index = 0
        initial_Target_PictureBox = Nothing
        Dim current_Folder_Path As String = Main_Form.Current_Folder_Path
        If Not String.IsNullOrEmpty(current_Folder_Path) AndAlso Directory.Exists(current_Folder_Path) Then
            Dim extensions As String() = Main_Form.Image_File_Extensions
            Dim files = Directory.GetFiles(current_Folder_Path).Where(Function(f) extensions.Contains(Path.GetExtension(f).ToLower())).ToList()
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
        While Not imagePanel.VerticalScroll.Visible AndAlso currently_Loaded_Index < allImageFiles.Count
            If is_Loading Then Return
            Await LoadNextBatchAsync()
            Await Task.Delay(10)
        End While
    End Function

    Private Sub Image_Panel_Form_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        If Me.Top >= 0 Then SaveSetting(App_name, Second_App_Name, "PicturePanelTop", Me.Top.ToString)
        If Me.Left >= 0 Then SaveSetting(App_name, Second_App_Name, "PicturePanelLeft", Me.Left.ToString)
        If Me.Height >= 200 Then SaveSetting(App_name, Second_App_Name, "PicturePanelHeight", Me.Height.ToString)
        If Me.Width >= 320 Then SaveSetting(App_name, Second_App_Name, "PicturePanelWidth", Me.Width.ToString)

        If toolTip IsNot Nothing Then
            toolTip.Dispose()
            toolTip = Nothing
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0100: PicPanel closed")
    End Sub

    Private Async Function LoadNextBatchAsync() As Task
        If is_Loading OrElse currently_Loaded_Index >= allImageFiles.Count Then Return
        Try
            is_Loading = True
            Dim cols = Math.Max(1, imagePanel.ClientSize.Width \ (Picture_Box_Width_At_Panel + 6))
            Dim batchSize = cols * 2
            Dim startIndex = currently_Loaded_Index
            Dim endIndex = Math.Min(startIndex + batchSize - 1, allImageFiles.Count - 1)
            If startIndex > endIndex Then Return

            Dim tasks As New List(Of Task)()
            For i = startIndex To endIndex
                Dim filePath = allImageFiles(i)
                Dim pb = New PictureBox()
                With pb
                    .Width = Picture_Box_Width_At_Panel
                    .Height = Picture_Box_Height_At_Panel
                    .BorderStyle = BorderStyle.None ' Border is now custom painted
                    .Tag = filePath
                End With
                AddHandler pb.Click, AddressOf OnPictureBoxClick
                AddHandler pb.Paint, AddressOf OnPictureBoxPaint
                AddHandler pb.DoubleClick, AddressOf OnPictureBoxDoubleClick ' Add DoubleClick handler
                imagePanel.Controls.Add(pb)
                tasks.Add(Task.Run(Sub()
                                       Dim thumbnail = CreateThumbnail(filePath, Picture_Box_Width_At_Panel, Picture_Box_Height_At_Panel)
                                       If Me.IsDisposed OrElse pb.IsDisposed OrElse CStr(pb.Tag) <> filePath Then Return
                                       If thumbnail IsNot Nothing Then
                                           pb.Invoke(New Action(Sub() pb.Image = thumbnail))
                                       Else
                                           pb.Invoke(New Action(Sub() pb.BackColor = Color.Red))
                                       End If
                                   End Sub))
            Next
            currently_Loaded_Index = endIndex + 1
            Await Task.WhenAll(tasks)
        Finally
            is_Loading = False
        End Try
    End Function

    Private Function CreateThumbnail(imagePath As String, width As Integer, height As Integer) As Image
        Try
            Using ms As New MemoryStream(File.ReadAllBytes(imagePath))
                Using originalImage As Image = Image.FromStream(ms)
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
                    bmPhoto.SetResolution(originalImage.HorizontalResolution, originalImage.VerticalResolution)
                    Using grPhoto As Graphics = Graphics.FromImage(bmPhoto)
                        grPhoto.Clear(imagePanel.BackColor) ' Use panel background color
                        grPhoto.InterpolationMode = InterpolationMode.Low
                        grPhoto.DrawImage(originalImage, New Rectangle(destX, destY, destWidth, destHeight), New Rectangle(0, 0, sourceWidth, sourceHeight), GraphicsUnit.Pixel)
                    End Using
                    Return bmPhoto
                End Using
            End Using
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    Private Sub PoMove_for_Panel(ByVal move_Slot_index As Integer)
        If selectedPictureControls.Count = 0 Then Return

        ' In Main_Form, key '0' corresponds to index 10
        Dim destination_Folder_Path As String = Hardkeys_to_move_mediafile(If(move_Slot_index = 0, 10, move_Slot_index))
        Dim move_Slot_Key As String = If(move_Slot_index = 0, "0", move_Slot_index.ToString())

        If String.IsNullOrEmpty(destination_Folder_Path) Then
            MessageBox.Show(If(Is_Russian_Language, "! Нет каталога-получателя для клавиши " & move_Slot_Key, "! No destination folder set for key " & move_Slot_Key), If(Is_Russian_Language, "Внимание", "Warning"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim filesToProcess = selectedPictureControls.Select(Function(pb) CStr(pb.Tag)).ToList()
        Dim operation_type_string = If(Is_Copying_not_Moving, If(Is_Russian_Language, "копировать", "copy"), If(Is_Russian_Language, "переместить", "move"))
        Dim confirmMsg = If(Is_Russian_Language, $"Вы уверены, что хотите {operation_type_string} {filesToProcess.Count} файл(ов) в '{destination_Folder_Path}'?", $"Are you sure you want to {operation_type_string} {filesToProcess.Count} file(s) to '{destination_Folder_Path}'?")

        If Not Is_no_request_before_file_operation AndAlso
MessageBox.Show(confirmMsg, If(Is_Russian_Language, "Подтверждение", "Confirm"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return

        Dim success_count = 0
        Dim error_messages As New System.Text.StringBuilder()

        For Each pbToProcess In selectedPictureControls.ToList()
            Dim source_file_path = CStr(pbToProcess.Tag)
            Dim destination_file_path = Path.Combine(destination_Folder_Path, Path.GetFileName(source_file_path))

            Try
                If Is_Copying_not_Moving Then
                    File.Copy(source_file_path, destination_file_path, True) ' Allow overwrite
                Else
                    File.Move(source_file_path, destination_file_path)
                    ' If move is successful, remove from UI
                    allImageFiles.Remove(source_file_path)
                    imagePanel.Controls.Remove(pbToProcess)
                    pbToProcess.Dispose()
                End If
                success_count += 1
            Catch ex As Exception
                error_messages.AppendLine(If(Is_Russian_Language, $"Не удалось обработать {source_file_path}: {ex.Message}", $"Failed to process {source_file_path}: {ex.Message}"))
            End Try
        Next

        ClearSelection()
        UpdateSelectionVisuals() ' Refresh the panel

        Dim summary_message As New System.Text.StringBuilder()
        summary_message.AppendLine(If(Is_Russian_Language, $"{success_count} из {filesToProcess.Count} файлов обработано.", $"{success_count} of {filesToProcess.Count} files processed."))
        If error_messages.Length > 0 Then
            summary_message.AppendLine(If(Is_Russian_Language, "Ошибки:", "Errors:"))
            summary_message.Append(error_messages.ToString())
        End If

        If Not Is_no_request_before_file_operation Then
            MessageBox.Show(summary_message.ToString(), If(Is_Russian_Language, "Операция завершена", "Operation Complete"), MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & $" p0085: pics moved/copied. Success: {success_count}, Failed: {filesToProcess.Count - success_count}")
    End Sub

    Private Sub Image_Panel_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        If toolTip IsNot Nothing Then
            toolTip.Dispose()
            toolTip = Nothing
        End If
    End Sub
End Class