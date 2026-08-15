#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The image editor window (SPECIFICATION_IMAGE_EDITOR_DOTNET10.md). This file is the
''' window itself - opening a picture, putting it back on disk, and closing. The tools
''' live in <c>Image_Editor_Form.Tools.vb</c>: Ф-1 built the dangerous half (the write),
''' Ф-2 the drawing, and text (Ф-3) and crop (Ф-4) hang off the same toolbar.
'''
''' WHY A SEPARATE WINDOW (§3): the viewer is driven by single letters - D deletes the
''' file, 0..9 move it to a recipient folder, R rotates - so an edit mode laid over the
''' main window would have to silence a keyboard where the cost of one stray press is a
''' deleted file. Here the main window is frozen by ShowDialog and nothing reaches it.
''' It also settles the owner's "no scrolling while editing" by construction: the canvas
''' is always fitted, so there is nothing to scroll and no wheel handler to write.
'''
''' WHAT THIS WINDOW MUST NOT DO (§9.2, invariant 3): take its pixels from
''' Picture_Box.Image. That bitmap is not the original - WEBP arrives rebuilt through a
''' PNG, an animated one arrives as a GIF, and an EXIF rotation has already been baked
''' into the pixels with the tag stripped. The editor reads the file's own bytes once,
''' decodes its own copy, and keeps those bytes as the EXIF donor for the save.
'''
''' Modern-only: whole file "#If Not NETFRAMEWORK".
''' </summary>
Partial Friend NotInheritable Class Image_Editor_Form
    Inherits Form

    ' --- what was opened ------------------------------------------------------

    Private ReadOnly source_Path As String
    Private ReadOnly orientation_Policy As ExifOrientationPolicy

    ''' <summary>The original file's bytes, read once at open. They are the EXIF donor
    ''' (§9.5) - re-reading them at save time would be a second trip to the share, and
    ''' worse, could pick up a file somebody changed in the meantime.</summary>
    Private original_Bytes As Byte()

    ''' <summary>The editor's own copy of the pixels. Everything Ф-2 onwards draws goes
    ''' here, and this is what the encoder is handed.</summary>
    Private image_Bitmap As Bitmap

    ''' <summary>Whether the result may be laid over the original, decided ONCE at open
    ''' (§9.3) - the probe is a round trip to the file system, and on a network share
    ''' that is not something to repeat per repaint.</summary>
    Private replace_Refusal As ImageFileWriter.ReplaceRefusal = ImageFileWriter.ReplaceRefusal.Missing

    ' --- what the viewer gets back (§9.7) -------------------------------------

    ''' <summary>Where the result was written, or "" when nothing was saved.</summary>
    Friend ReadOnly Property SavedPath As String
        Get
            Return saved_Path
        End Get
    End Property
    Private saved_Path As String = ""

    ''' <summary>True when the save replaced the file that was opened, so the viewer
    ''' re-reads the current file instead of navigating to a new one.</summary>
    Friend ReadOnly Property SavedOverOriginal As Boolean
        Get
            Return saved_Over_Original
        End Get
    End Property
    Private saved_Over_Original As Boolean = False

    ''' <summary>
    ''' The JPEG re-compression warning is asked once per session, not once per save
    ''' (§9.5). Shared, because "the session" is the running viewer - being asked again
    ''' for the next photo is the kind of nagging that trains people to click through
    ''' warnings without reading them.
    ''' </summary>
    Private Shared jpeg_Recompression_Confirmed As Boolean = False

    ' --- chrome ----------------------------------------------------------------

    Private canvas As EditorCanvas
    Private lbl_Editor_Status As Label
    Private WithEvents btn_Save As Button
    Private WithEvents btn_Save_As As Button
    Private WithEvents btn_Close As Button
    Private saving As Boolean = False

    Private Const Registry_Top As String = "EditorTop"
    Private Const Registry_Left As String = "EditorLeft"
    Private Const Registry_Width As String = "EditorWidth"
    Private Const Registry_Height As String = "EditorHeight"

    Friend Sub New(filePath As String, autoRotate As Boolean)
        source_Path = filePath
        ' The tag written must describe the pixels written (§9.2, invariant 4). The
        ' editor shows what the viewer shows, so the viewer's own setting decides both:
        ' auto-rotate on means the pixels were turned upright and the tag has to go;
        ' off means they are raw and the original's tag still describes them.
        orientation_Policy = If(autoRotate, ExifOrientationPolicy.PixelsAreUpright,
                                            ExifOrientationPolicy.PixelsAreRaw)
        BuildChrome()
    End Sub

    ''' <summary>
    ''' Reads the original and decodes the editor's own copy. False means there is
    ''' nothing to edit and the caller must not show the window - a message beats an
    ''' empty editor.
    ''' </summary>
    Friend Function TryLoadOriginal() As Boolean
        Try
            original_Bytes = File.ReadAllBytes(source_Path)

            Dim extension As String = Path.GetExtension(source_Path).ToLowerInvariant()
            Using stream As New MemoryStream(original_Bytes, writable:=False)
                Dim decoded As Image
                If ImageDecoderProvider.Decoder_Backed_Extensions.Contains(extension) Then
                    decoded = ImageDecoderProvider.Current.DecodeToImage(stream)
                Else
                    decoded = Image.FromStream(stream)
                End If
                If decoded Is Nothing Then Return False

                Using decoded
                    If orientation_Policy = ExifOrientationPolicy.PixelsAreUpright Then
                        FileManager.ApplyExifOrientation(decoded)
                    End If
                    image_Bitmap = ToEditableCopy(decoded)
                End Using
            End Using
        Catch ex As Exception
            AppFileLogger.LogException("Image editor: opening " & source_Path, ex)
            Return False
        End Try

        replace_Refusal = ImageFileWriter.CanReplaceOriginal(source_Path)

        canvas.Bitmap = image_Bitmap
        Me.Text = Localization.TF("Правка: {0}", Path.GetFileName(source_Path))
        ApplySaveAvailability()
        Return True
    End Function

    ''' <summary>
    ''' The editor's own surface: always 32bpp, always drawable.
    '''
    ''' <c>New Bitmap(image)</c> would be shorter but can hand back the source's own pixel
    ''' format, and <see cref="Graphics.FromImage"/> refuses an indexed one outright - a
    ''' static GIF or an 8-bit PNG would open and then throw on the first stroke. Copying
    ''' through an explicit 32bppArgb surface settles that at load time.
    '''
    ''' <c>SourceCopy</c> + unscaled: the pixels must arrive exactly as decoded, not
    ''' blended onto the transparent surface underneath and not resampled.
    ''' </summary>
    Private Shared Function ToEditableCopy(decoded As Image) As Bitmap
        Dim copy As New Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppArgb)
        Using g As Graphics = Graphics.FromImage(copy)
            g.CompositingMode = CompositingMode.SourceCopy
            g.DrawImageUnscaled(decoded, 0, 0)
        End Using
        Return copy
    End Function

    ' ------------------------------------------------------------------ chrome ----

    Private Sub BuildChrome()
        Me.Name = "Image_Editor_Form"
        Me.Text = Localization.T("Правка изображения")
        Me.StartPosition = FormStartPosition.Manual
        Me.MinimumSize = New Size(900, 700)
        Me.KeyPreview = True
        Me.ShowInTaskbar = False

        ' The application is pinned to HighDpiMode.DpiUnaware (Application_Events.vb) and
        ' this window inherits that on purpose: it must behave like the rest of the app,
        ' not scale on its own next to a viewer that does not. The font is only touched
        ' for a script the pinned Microsoft Sans Serif cannot draw at all.
        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then Me.Font = New Font(family, Me.Font.Size)
        If Localization.IsRightToLeft() Then Me.RightToLeft = RightToLeft.Yes

        canvas = New EditorCanvas() With {
            .Name = "editor_Canvas",
            .Dock = DockStyle.Fill
        }
        WireCanvasGestures(canvas)

        Dim bottom As New Panel With {
            .Name = "editor_Bottom",
            .Dock = DockStyle.Bottom,
            .Height = 40,
            .Padding = New Padding(8, 6, 8, 6)
        }

        btn_Close = NewFooterButton("btn_Editor_Close", Localization.T("Закрыть"))
        btn_Save_As = NewFooterButton("btn_Editor_Save_As", Localization.T("Сохранить как.."))
        btn_Save = NewFooterButton("btn_Editor_Save", Localization.T("Сохранить"))
        btn_Undo = NewFooterButton("btn_Editor_Undo", Localization.T("Отменить"))
        btn_Undo.Enabled = False

        lbl_Editor_Status = New Label With {
            .Name = "lbl_Editor_Status",
            .AutoSize = False,
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Text = ""
        }

        ' Docked controls are laid out back-to-front, so the FIRST one brought to the
        ' front is the one that ends up outermost - each later BringToFront pushes the
        ' previous one inwards. Hence the reversed list: on screen it reads
        ' "Undo | Save | Save as.. | Close", with the way out under the right hand.
        ' (Ф-1 had this list in reading order and shipped with Close on the left.)
        bottom.Controls.Add(lbl_Editor_Status)
        For Each b As Button In New Button() {btn_Close, btn_Save_As, btn_Save, btn_Undo}
            b.Dock = DockStyle.Right
            bottom.Controls.Add(b)
            b.BringToFront()
        Next

        ' Fill last, Top second: a docked Fill takes what the docked bands leave, and the
        ' bands are handed the edges in the order they were added.
        Me.Controls.Add(canvas)
        Me.Controls.Add(bottom)
        Me.Controls.Add(BuildToolBar())

        RestoreWindowBounds()
    End Sub

    Private Function NewFooterButton(name As String, caption As String) As Button
        Return New Button With {
            .Name = name,
            .Text = caption,
            .AutoSize = False,
            .Width = Localization.Scaled(130),
            .Margin = New Padding(6, 0, 0, 0)
        }
    End Function

    ''' <summary>
    ''' Restores the window's own place (§5). Judged against the desktop that actually
    ''' exists, exactly as the main window's restore is: a saved rectangle is good when
    ''' any part of it lands on the virtual screen, so a monitor placed left of or above
    ''' the primary one is not thrown away.
    ''' </summary>
    ' NOTE: not "RestoreBounds" - Form already has a property by that name (the
    ' normal-state rectangle of a maximized window), and SaveWindowBounds below reads it.
    Private Sub RestoreWindowBounds()
        Dim width As Integer = 1100
        Dim height As Integer = 800
        Integer.TryParse(GetSetting(App_name, Second_App_Name, Registry_Width), width)
        Integer.TryParse(GetSetting(App_name, Second_App_Name, Registry_Height), height)
        width = Math.Max(Me.MinimumSize.Width, width)
        height = Math.Max(Me.MinimumSize.Height, height)

        Dim placed As Boolean = False
        Dim left As Integer, top As Integer
        If Integer.TryParse(GetSetting(App_name, Second_App_Name, Registry_Left), left) AndAlso
           Integer.TryParse(GetSetting(App_name, Second_App_Name, Registry_Top), top) Then
            If SystemInformation.VirtualScreen.IntersectsWith(New Rectangle(left, top, width, height)) Then
                Me.SetBounds(left, top, width, height)
                placed = True
            End If
        End If

        If Not placed Then
            Dim work As Rectangle = Screen.PrimaryScreen.WorkingArea
            width = Math.Min(width, work.Width)
            height = Math.Min(height, work.Height)
            Me.SetBounds(work.Left + (work.Width - width) \ 2, work.Top + (work.Height - height) \ 2,
                         width, height)
        End If
    End Sub

    Private Sub SaveWindowBounds()
        ' Maximized/minimized states report the wrong rectangle; Form.RestoreBounds is the
        ' normal-state one, which is what should come back next time.
        Dim bounds As Rectangle = If(Me.WindowState = FormWindowState.Normal, Me.Bounds, Me.RestoreBounds)
        Try
            SaveSetting(App_name, Second_App_Name, Registry_Left, bounds.Left.ToString())
            SaveSetting(App_name, Second_App_Name, Registry_Top, bounds.Top.ToString())
            SaveSetting(App_name, Second_App_Name, Registry_Width, bounds.Width.ToString())
            SaveSetting(App_name, Second_App_Name, Registry_Height, bounds.Height.ToString())
        Catch ex As Exception
            AppFileLogger.LogException("Image editor: saving window bounds", ex)
        End Try
    End Sub

    ' --------------------------------------------------------------- save state ----

    ''' <summary>
    ''' Enables "Save" or explains why it is off. A grey button with no reason reads as
    ''' "the program is broken" (§9.3), so the reason goes into both the tooltip and the
    ''' status line - and "Save as.." stays available in every case, which is the point
    ''' the message makes.
    ''' </summary>
    Private Sub ApplySaveAvailability()
        Dim allowed As Boolean = (replace_Refusal = ImageFileWriter.ReplaceRefusal.None)
        btn_Save.Enabled = allowed AndAlso Not saving
        btn_Save_As.Enabled = Not saving
        btn_Close.Enabled = Not saving
        btn_Undo.Enabled = undo_History.CanUndo AndAlso Not saving
        If btn_Apply_Crop IsNot Nothing Then btn_Apply_Crop.Enabled = Not saving

        If allowed Then
            SetStatus(SizeText())
            Return
        End If

        ' The refusal is the more useful thing to read, but the size still belongs on
        ' screen - it is the one fact about the picture the window otherwise never states.
        Dim message As String = Localization.TF("Поверх оригинала нельзя: {0}. Доступно «Сохранить как..»",
                                                RefusalReason(replace_Refusal))
        SetStatus(SizeText() & " - " & message)
    End Sub

    ''' <summary>The picture's size, marked when it is no longer what is on disk.</summary>
    Private Function SizeText() As String
        If image_Bitmap Is Nothing Then Return ""
        Dim size As String = image_Bitmap.Width.ToString() & " × " & image_Bitmap.Height.ToString()
        If Not dirty Then Return size
        Return size & " · " & Localization.T("изменено")
    End Function

    Private Shared Function RefusalReason(refusal As ImageFileWriter.ReplaceRefusal) As String
        Select Case refusal
            Case ImageFileWriter.ReplaceRefusal.FormatNotWritable
                Return Localization.T("этот формат не записывается")
            Case ImageFileWriter.ReplaceRefusal.ReadOnlyAttribute
                Return Localization.T("файл только для чтения")
            Case ImageFileWriter.ReplaceRefusal.AccessDenied
                Return Localization.T("нет прав на запись")
            Case ImageFileWriter.ReplaceRefusal.Locked
                Return Localization.T("файл занят другой программой")
            Case Else
                Return Localization.T("файл не найден")
        End Select
    End Function

    Private Sub SetStatus(text As String)
        If lbl_Editor_Status IsNot Nothing Then lbl_Editor_Status.Text = text
    End Sub

    ' -------------------------------------------------------------------- save ----

    Private Async Sub btn_Save_Click(sender As Object, e As EventArgs) Handles btn_Save.Click
        If saving OrElse replace_Refusal <> ImageFileWriter.ReplaceRefusal.None Then Return
        If Not ConfirmJpegRecompression(source_Path) Then Return
        Await WriteTo(source_Path, overOriginal:=True)
    End Sub

    Private Async Sub btn_Save_As_Click(sender As Object, e As EventArgs) Handles btn_Save_As.Click
        If saving Then Return
        Dim target As String = AskWhereToSave()
        If String.IsNullOrEmpty(target) Then Return
        Await WriteTo(target, overOriginal:=String.Equals(target, source_Path, StringComparison.OrdinalIgnoreCase))
    End Sub

    Private Sub btn_Close_Click(sender As Object, e As EventArgs) Handles btn_Close.Click
        Me.Close()
    End Sub

    ''' <summary>
    ''' One confirmation per session before a JPEG is re-compressed over itself (§9.5).
    '''
    ''' The specification sketched a "do not ask again" checkbox; asking once per session
    ''' makes it redundant, so this is a plain Yes/No - and the app's existing
    ''' "no confirmations" setting still silences it, which is exactly what that setting
    ''' promises.
    ''' </summary>
    Private Function ConfirmJpegRecompression(targetPath As String) As Boolean
        Dim extension As String = Path.GetExtension(targetPath).ToLowerInvariant()
        If extension <> ".jpg" AndAlso extension <> ".jpeg" Then Return True
        If jpeg_Recompression_Confirmed OrElse Is_no_request_before_file_operation Then Return True

        Dim answer As DialogResult = MessageBox.Show(Me,
            Localization.T("JPEG будет пересжат - это необратимо. Дата съёмки и остальные EXIF-данные сохранятся. Продолжить?"),
            Localization.T("Правка изображения"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If answer <> DialogResult.Yes Then Return False

        jpeg_Recompression_Confirmed = True
        Return True
    End Function

    ''' <summary>
    ''' "Save as.." (§9.4). The name collision is the dialog's business, not ours -
    ''' ResolveDestinationCollision exists for automatic operations where there is nobody
    ''' to ask; here there is a dialog and a person.
    ''' </summary>
    Private Function AskWhereToSave() As String
        Dim originalExtension As String = Path.GetExtension(source_Path).ToLowerInvariant()
        Dim preferred As String = If(ImageEncoderProvider.IsWritableExtension(source_Path), originalExtension, ".png")

        Using dialog As New SaveFileDialog()
            dialog.Title = Localization.T("Сохранить как..")
            dialog.InitialDirectory = Path.GetDirectoryName(source_Path)
            dialog.FileName = Path.GetFileNameWithoutExtension(source_Path) & " (edited)" & preferred
            dialog.OverwritePrompt = True
            dialog.AddExtension = True
            dialog.Filter = BuildSaveFilter(preferred)
            dialog.FilterIndex = 1
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return ""
            Return dialog.FileName
        End Using
    End Function

    ''' <summary>
    ''' Only the writable formats (§9.1), with the picture's own format first so the
    ''' obvious answer is the default one. Format names are proper nouns - there is
    ''' nothing here to translate.
    ''' </summary>
    Private Shared Function BuildSaveFilter(preferred As String) As String
        Dim entries As New List(Of String)()
        Dim add As Action(Of String, String) =
            Sub(label As String, pattern As String)
                Dim entry As String = label & " (" & pattern & ")|" & pattern
                If entries.Contains(entry) Then Return
                entries.Add(entry)
            End Sub

        ' The picture's own format leads, then the rest in a fixed order.
        Select Case preferred
            Case ".jpg", ".jpeg" : add("JPEG", "*.jpg;*.jpeg")
            Case ".bmp" : add("BMP", "*.bmp")
            Case ".tiff" : add("TIFF", "*.tiff")
            Case ".webp" : add("WebP", "*.webp")
            Case Else : add("PNG", "*.png")
        End Select
        add("PNG", "*.png")
        add("JPEG", "*.jpg;*.jpeg")
        add("BMP", "*.bmp")
        add("TIFF", "*.tiff")
        add("WebP", "*.webp")

        Return String.Join("|", entries)
    End Function

    ''' <summary>
    ''' Encodes and puts the file down, off the UI thread (§9.6). The window stays open
    ''' on failure with the reason in the status line: the edit is still in memory, and
    ''' closing would throw it away over a share that blinked.
    ''' </summary>
    Private Async Function WriteTo(targetPath As String, overOriginal As Boolean) As Task
        saving = True
        btn_Save.Enabled = False
        btn_Save_As.Enabled = False
        btn_Close.Enabled = False
        SetStatus(Localization.T("Сохраняю.."))

        Try
            Await ImageFileWriter.SaveAsync(image_Bitmap, targetPath, original_Bytes, orientation_Policy)
        Catch ex As Exception
            AppFileLogger.LogException("Image editor: saving " & targetPath, ex)
            saving = False
            ApplySaveAvailability()
            SetStatus(Localization.TF("Не удалось сохранить: {0}", ex.Message))
            Return
        End Try

        saving = False
        saved_Path = targetPath
        saved_Over_Original = overOriginal
        ' Under ShowDialog this both closes the window and is the answer the caller reads;
        ' a Close() after it would only run the closing handlers a second time.
        Me.DialogResult = DialogResult.OK
    End Function

    ' ------------------------------------------------------------------ window ----

    Private Sub Image_Editor_Form_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        If saving Then Return

        If e.KeyCode = Keys.Z AndAlso e.Control Then
            e.Handled = True
            UndoLastEdit()
            Return
        End If

        ' Enter applies the crop frame (§6.1). It does nothing else in this window - there
        ' is no default button - so it cannot be pressed by accident into something worse.
        If e.KeyCode = Keys.Enter Then
            e.Handled = True
            ApplyCrop()
            Return
        End If

        If e.KeyCode = Keys.Escape Then
            e.Handled = True
            ' Escape peels one layer at a time, cheapest first: a stroke or a frame drag in
            ' flight, then the crop frame, then the window. Closing on the same press that
            ' abandoned a gesture would throw away the whole edit over a rectangle the hand
            ' had merely started in the wrong place.
            If CancelGesture() Then Return
            If ClearCropFrameIfAny() Then Return
            Me.Close()
        End If
    End Sub

    Private Sub Image_Editor_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        ' A save in flight owns the bitmap it is encoding from; letting the window close
        ' under it would dispose those pixels mid-write.
        If saving AndAlso e.CloseReason = CloseReason.UserClosing Then
            e.Cancel = True
            Return
        End If

        If Not ConfirmDiscardingEdits(e.CloseReason) Then
            e.Cancel = True
            Return
        End If

        SaveWindowBounds()
    End Sub

    ''' <summary>
    ''' The one question the "no confirmations" setting does NOT silence (§9.7): that flag
    ''' is about file operations, which are undoable and reversible here, while this is
    ''' work that exists only in memory and is gone the moment the window is.
    ''' </summary>
    Private Function ConfirmDiscardingEdits(reason As CloseReason) As Boolean
        If Not dirty Then Return True
        If reason <> CloseReason.UserClosing Then Return True
        ' DialogResult.OK is the save path closing the window itself - there is nothing
        ' unsaved left to ask about.
        If Me.DialogResult = DialogResult.OK Then Return True

        Dim answer As DialogResult = MessageBox.Show(Me,
            Localization.T("Правки не сохранены. Закрыть?"),
            Localization.T("Правка изображения"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
        Return answer = DialogResult.Yes
    End Function

    Private Sub Image_Editor_Form_FormClosed(sender As Object, e As FormClosedEventArgs) Handles Me.FormClosed
        If canvas IsNot Nothing Then canvas.Bitmap = Nothing
        If image_Bitmap IsNot Nothing Then
            image_Bitmap.Dispose()
            image_Bitmap = Nothing
        End If
        ' The history holds a full-size copy per step; on a 24-megapixel photo that is
        ' most of what this window was using.
        undo_History.Dispose()
        original_Bytes = Nothing
    End Sub

    ' ------------------------------------------------------------------ canvas ----

    ''' <summary>
    ''' The surface the picture is fitted onto. A Control rather than a PictureBox
    ''' because Ф-2 draws a rubber-band preview OVER the image without touching the
    ''' bitmap, and that needs the paint to be ours.
    '''
    ''' It swallows the mouse wheel (§5, invariant 7). Not a placeholder for a future
    ''' zoom: the canvas is always fitted, so there is nothing to scroll, and a wheel
    ''' that quietly did nothing at all is exactly what the owner asked for.
    ''' </summary>
    Private NotInheritable Class EditorCanvas
        Inherits Control

        Private Const WM_MOUSEWHEEL As Integer = &H20A

        Private image As Bitmap

        ''' <summary>
        ''' The picture already resampled to the size it is being shown at.
        '''
        ''' Without it every repaint bicubic-resamples the full picture, and a brush stroke
        ''' repaints on every MouseMove - some 30-80 ms per frame on a 24-megapixel photo,
        ''' which is a stroke that arrives after the hand has moved on. Scaling once per
        ''' canvas size instead turns each frame into one unscaled blit.
        ''' </summary>
        Private display_Cache As Bitmap

        Friend Sub New()
            Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                        ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
            Me.BackColor = Color.FromArgb(32, 32, 32)
        End Sub

        Friend Property Bitmap As Bitmap
            Get
                Return image
            End Get
            Set(value As Bitmap)
                image = value
                DropDisplayCache()
                Me.Invalidate()
            End Set
        End Property

        ''' <summary>The bitmap is the same object but its pixels are not: a stroke was
        ''' just committed into it, so the scaled copy on screen is now a picture of the
        ''' state before it.</summary>
        Friend Sub PixelsChanged()
            DropDisplayCache()
            Me.Invalidate()
        End Sub

        Private Sub DropDisplayCache()
            If display_Cache Is Nothing Then Return
            display_Cache.Dispose()
            display_Cache = Nothing
        End Sub

        ''' <summary>
        ''' The scaled copy for <paramref name="size"/>, rebuilt when the canvas has been
        ''' resized. Nothing when it could not be made - a picture large enough to fail
        ''' here must still be drawable, so the caller falls back to resampling per frame.
        ''' </summary>
        Private Function DisplayCopy(size As Size) As Bitmap
            If display_Cache IsNot Nothing AndAlso display_Cache.Size = size Then Return display_Cache

            DropDisplayCache()
            Try
                Dim scaled As New Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb)
                Using g As Graphics = Graphics.FromImage(scaled)
                    ' Bicubic on the way down - the canvas is nearly always showing a photo
                    ' smaller than it really is, and the nearest-neighbour default makes a
                    ' shimmering mess of it. Same reasoning as HqPictureBox in the viewer.
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality
                    g.DrawImage(image, New Rectangle(Point.Empty, size))
                End Using
                display_Cache = scaled
            Catch ex As Exception
                AppFileLogger.LogException("Image editor: scaling the canvas copy", ex)
                display_Cache = Nothing
            End Try

            Return display_Cache
        End Function

        ''' <summary>Where the picture lies right now, in canvas coordinates. Ф-2 maps
        ''' the mouse back through this rectangle.</summary>
        Friend Function ImageRect() As Rectangle
            If image Is Nothing Then Return Rectangle.Empty
            Return EditorGeometry.FitRect(image.Size, Me.ClientSize)
        End Function

        ''' <summary>
        ''' Paints the picture, then lets the Paint handlers run - in that order, and it
        ''' matters. The rubber-band preview of a gesture in flight is drawn by the
        ''' window's handler (§6), and a handler that ran before the picture would be
        ''' painted over by it. Same reasoning as HqPictureBox, mirrored: there the
        ''' interpolation has to be set BEFORE the base draws, here the overlay after.
        ''' </summary>
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            If image Is Nothing Then
                MyBase.OnPaint(e)
                Return
            End If

            Dim target As Rectangle = ImageRect()
            If target.Width <= 0 OrElse target.Height <= 0 Then
                MyBase.OnPaint(e)
                Return
            End If

            If target.Size = image.Size Then
                ' Shown at its own size (a picture smaller than the canvas is never
                ' enlarged): there is nothing to scale and nothing to cache.
                e.Graphics.DrawImageUnscaled(image, target.Location)
            Else
                Dim scaled As Bitmap = DisplayCopy(target.Size)
                If scaled IsNot Nothing Then
                    e.Graphics.DrawImageUnscaled(scaled, target.Location)
                Else
                    e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic
                    e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality
                    e.Graphics.DrawImage(image, target)
                End If
            End If

            ' A hairline so a white photo does not bleed into a light background.
            Using pen As New Pen(Color.FromArgb(90, 90, 90))
                e.Graphics.DrawRectangle(pen, target.Left - 1, target.Top - 1,
                                         target.Width + 1, target.Height + 1)
            End Using

            MyBase.OnPaint(e)
        End Sub

        Protected Overrides Sub WndProc(ByRef m As Message)
            If m.Msg = WM_MOUSEWHEEL Then Return
            MyBase.WndProc(m)
        End Sub

        ''' <summary>The cache is a full canvas-sized bitmap of our own making - the one
        ''' thing here the window does not already own and dispose.</summary>
        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then DropDisplayCache()
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Class
#End If
