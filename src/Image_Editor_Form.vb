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
    Private WithEvents btn_Undo As Button
    Private WithEvents btn_Redo As Button
    Private saving As Boolean = False

    ''' <summary>Whether the editor restored a remembered window rectangle at open
    ''' (126 UX-606). The viewer centres it on its own monitor only when there is
    ''' nothing remembered - the same rule the thumbnail panel already follows.</summary>
    Friend ReadOnly Property HasRememberedPlacement As Boolean
        Get
            Return has_Remembered_Placement
        End Get
    End Property
    Private has_Remembered_Placement As Boolean = False

    ''' <summary>True once the close question was answered with "Не сохранять": the
    ''' next FormClosing runs without asking again.</summary>
    Private discard_Confirmed As Boolean = False

    ''' <summary>True while the close question's own save flow is putting the file
    ''' away, so a close attempt during it is held by the saving flag, not by a second
    ''' question.</summary>
    Private close_Question_Open As Boolean = False

    ''' <summary>The script font <see cref="BuildChrome"/> may hand to the form, kept so
    ''' <see cref="Dispose"/> can return it: a Control never disposes a Font it was
    ''' handed, and without this field nothing else holds it.</summary>
    Private editor_Font As Font

    Private Const Registry_Top As String = "EditorTop"
    Private Const Registry_Left As String = "EditorLeft"
    Private Const Registry_Width As String = "EditorWidth"
    Private Const Registry_Height As String = "EditorHeight"
    ''' <summary>The DPI Registry_Width/Registry_Height were measured at (WindowPlacement.SizeForDpi).</summary>
    Private Const Registry_Dpi As String = "EditorDpi"

    ''' <summary>The size the editor opens at the first time, in 96-dpi pixels like the rest of the layout.</summary>
    Private Const Default_Width As Integer = 1100
    Private Const Default_Height As Integer = 800

    ''' <summary>The remembered size exactly as stored (device pixels at <see cref="remembered_Dpi"/>), the
    ''' remembered position when it lies on a screen, and the DPI the size belongs to (0 = not recorded).</summary>
    Private remembered_Size As Size
    Private remembered_Location As Point
    Private remembered_Dpi As Integer

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
    ''' blended onto the transparent surface underneath and not resampled. A throw here
    ''' disposes the copy before re-raising, like <see cref="EditorImageOps.CropTo"/>:
    ''' the caller aborts the open, so no one else could ever reach the half-made surface.
    ''' </summary>
    Private Shared Function ToEditableCopy(decoded As Image) As Bitmap
        Dim copy As New Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppArgb)
        Try
            Using g As Graphics = Graphics.FromImage(copy)
                g.CompositingMode = CompositingMode.SourceCopy
                g.DrawImageUnscaled(decoded, 0, 0)
            End Using
        Catch
            copy.Dispose()
            Throw
        End Try
        Return copy
    End Function

    ' ------------------------------------------------------------------ chrome ----

    Private Sub BuildChrome()
        Me.Name = "Image_Editor_Form"
        Me.Text = Localization.T("Редактирование изображения")
        Me.StartPosition = FormStartPosition.Manual
        Me.MinimumSize = New Size(900, 700)
        Me.KeyPreview = True
        Me.ShowInTaskbar = False

        ' The viewer is PerMonitorV2 (Application_Events.vb) - the old note here said DpiUnaware, which
        ' is true of the net48 reference build only. The layout below is authored in 96-dpi pixels and
        ' scaled by DialogLayout.ApplyAutoScale (the last line of this builder); the remembered
        ' rectangle is applied after that scaling, never before it (WindowPlacement, 124 UX-202).
        ' The font is only touched
        ' for a script the pinned Microsoft Sans Serif cannot draw at all - and it is
        ' kept in a field because a Control never disposes a Font it was handed (audit
        ' 057.S18-1, the class of 057.S17-6); Dispose below lets it go.
        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then
            editor_Font = New Font(family, Me.Font.Size)
            Me.Font = editor_Font
        End If
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

        ' WINDOWS-UI section 11: every command carries its verb's glyph. Colour is for the answer of a dialog;
        ' this window's footer is a command strip, so only the one that keeps the work (Save) is the
        ' affirmative and only the way out (Close) is the dismissing one - Undo, Redo and Save as stay mono.
        btn_Close = NewFooterButton("btn_Editor_Close", Localization.T("Закрыть"), "nav.close", ButtonGlyphRole.Dismissing)
        btn_Save_As = NewFooterButton("btn_Editor_Save_As", Localization.T("Сохранить как.."), "action.save", ButtonGlyphRole.Neutral)
        btn_Save = NewFooterButton("btn_Editor_Save", Localization.T("Сохранить"), "action.save", ButtonGlyphRole.Affirmative)
        btn_Undo = NewFooterButton("btn_Editor_Undo", Localization.T("Отменить"), "action.undo", ButtonGlyphRole.Neutral)
        btn_Undo.Enabled = False
        btn_Redo = NewFooterButton("btn_Editor_Redo", Localization.T("Вернуть"), "action.redo", ButtonGlyphRole.Neutral)
        btn_Redo.Enabled = False

        lbl_Editor_Status = New Label With {
            .Name = "lbl_Editor_Status",
            .AutoSize = False,
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft,
            .AutoEllipsis = True,
            .Text = ""
        }

        ' Docked controls are laid out back-to-front, so the FIRST one brought to the
        ' front is the one that ends up outermost - each later BringToFront pushes the
        ' previous one inwards. Hence the reversed list: on screen it reads
        ' "Undo | Redo | Save | Save as.. | Close", with the way out under the right hand.
        ' (Ф-1 had this list in reading order and shipped with Close on the left.)
        bottom.Controls.Add(lbl_Editor_Status)
        For Each b As Button In New Button() {btn_Close, btn_Save_As, btn_Save, btn_Redo, btn_Undo}
            b.Dock = DockStyle.Right
            bottom.Controls.Add(b)
            b.BringToFront()
        Next

        ' Fill last, Top second: a docked Fill takes what the docked bands leave, and the
        ' bands are handed the edges in the order they were added.
        Me.Controls.Add(canvas)
        Me.Controls.Add(bottom)
        Me.Controls.Add(BuildToolBar())

        ' A first guess for the size, so the viewer can centre the window before it is shown; OnLoad
        ' replaces it once the window has scaled itself and knows its real DPI.
        ReadRememberedBounds()
        ApplyRememberedBounds()

        ' Last, once every child exists: scaling touches only the children present when it runs.
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    ''' <summary>
    ''' The window scales itself at its first layout. Doing it here, before the rectangle below, is the whole
    ''' rule: the remembered size is applied AFTER scaling, so it is not multiplied by the DPI a second time
    ''' (the window grew every session when it was). A window opened without a remembered place is centred on
    ''' the viewer's monitor again, because its size changed since the viewer centred it.
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

    Private Function NewFooterButton(name As String, caption As String, glyphId As String, role As ButtonGlyphRole) As Button
        Dim button As New Button With {
            .Name = name,
            .Text = caption,
            .AutoSize = False,
            .Width = Localization.Scaled(130),
            .Margin = New Padding(6, 0, 0, 0)
        }
        ButtonGlyphs.Apply(button, glyphId, role)
        ' 130 logical px stays while the caption and the glyph fit; a longer caption (another language, a
        ' script font) widens the button instead of clipping it. The caption is measured at the DPI this
        ' window has now and turned back into 96-dpi pixels, because ApplyAutoScale multiplies the width again.
        Dim measured As Integer = TextRenderer.MeasureText(caption, Me.Font, New Size(Integer.MaxValue, Integer.MaxValue),
                                                           TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine).Width
        Dim logicalText As Integer = CInt(Math.Ceiling(measured * 96.0R / Math.Max(96, Me.DeviceDpi)))
        Dim needed As Integer = logicalText + ButtonGlyphs.GlyphLogicalSize + ButtonGlyphs.GapLogicalSize + 2 * 12
        If needed > button.Width Then button.Width = needed
        Return button
    End Function

    ''' <summary>
    ''' Reads the window's own place (§5) - once, at construction. Judged against the desktop that actually
    ''' exists, exactly as the main window's restore is: a saved rectangle is good when
    ''' any part of it lands on the virtual screen, so a monitor placed left of or above
    ''' the primary one is not thrown away. The size is kept as it was stored (device pixels, with
    ''' the DPI it was measured at); <see cref="ApplyRememberedBounds"/> turns it into the window's own.
    ''' </summary>
    Private Sub ReadRememberedBounds()
        Dim width As Integer, height As Integer
        If Integer.TryParse(GetSetting(App_name, Second_App_Name, Registry_Width), width) AndAlso
           Integer.TryParse(GetSetting(App_name, Second_App_Name, Registry_Height), height) AndAlso
           width > 0 AndAlso height > 0 Then
            remembered_Size = New Size(width, height)
            remembered_Dpi = WindowPlacement.ParseDpi(GetSetting(App_name, Second_App_Name, Registry_Dpi))
        Else
            ' Nothing remembered: the default is authored in 96-dpi pixels, like the layout.
            remembered_Size = New Size(Default_Width, Default_Height)
            remembered_Dpi = 96
        End If

        Dim left As Integer, top As Integer
        Dim placed As Boolean = False
        If Integer.TryParse(GetSetting(App_name, Second_App_Name, Registry_Left), left) AndAlso
           Integer.TryParse(GetSetting(App_name, Second_App_Name, Registry_Top), top) Then
            If SystemInformation.VirtualScreen.IntersectsWith(New Rectangle(left, top, remembered_Size.Width, remembered_Size.Height)) Then
                remembered_Location = New Point(left, top)
                placed = True
            End If
        End If

        ' 126 UX-606: a remembered rectangle is the user's choice, and the viewer is told
        ' whether there was one - it centres the editor on its monitor only when there is
        ' nothing remembered, exactly as it already does for the thumbnail panel.
        has_Remembered_Placement = placed
    End Sub

    ''' <summary>
    ''' Puts the remembered rectangle on the window, in the pixels of the DPI the window has NOW. Called twice:
    ''' from the builder, where the DPI is only the system's and the answer is a guess for the viewer's
    ''' centring, and from OnLoad, after the window has scaled itself, where it is final.
    ''' </summary>
    Private Sub ApplyRememberedBounds()
        Dim size As Size = WindowPlacement.SizeForDpi(remembered_Size.Width, remembered_Size.Height, remembered_Dpi, Me.DeviceDpi)
        Dim width As Integer = Math.Max(Me.MinimumSize.Width, size.Width)
        Dim height As Integer = Math.Max(Me.MinimumSize.Height, size.Height)

        If has_Remembered_Placement Then
            Me.SetBounds(remembered_Location.X, remembered_Location.Y, width, height)
            Return
        End If

        Dim work As Rectangle = Screen.PrimaryScreen.WorkingArea
        width = Math.Min(width, work.Width)
        height = Math.Min(height, work.Height)
        Me.SetBounds(work.Left + (work.Width - width) \ 2, work.Top + (work.Height - height) \ 2,
                     width, height)
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
            ' The pixels above belong to this DPI; without it the next open cannot tell them from another monitor's.
            SaveSetting(App_name, Second_App_Name, Registry_Dpi, Me.DeviceDpi.ToString())
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
        ' "Save" writes over the original, so it waits for an edit (ticket 028 Ф-5,
        ' APP-BEHAVIOUR rule 5): re-encoding an untouched JPEG is a loss with nothing to
        ' show for it. The reason goes on the button's tooltip - a grey button with no
        ' reason reads as broken - and "Save as.." stays available either way.
        btn_Save.Enabled = allowed AndAlso dirty AndAlso Not saving
        btn_Save_As.Enabled = Not saving
        btn_Close.Enabled = Not saving
        btn_Undo.Enabled = undo_History.CanUndo AndAlso Not saving
        btn_Redo.Enabled = undo_History.CanRedo AndAlso Not saving
        If btn_Apply_Crop IsNot Nothing Then btn_Apply_Crop.Enabled = Not saving
        tool_Tips.SetToolTip(btn_Save, If(allowed AndAlso Not dirty, Localization.T("Нечего сохранять: изображение не изменено"), ""))

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

    ''' <summary>The picture's size, marked when it is no longer what is on disk - and,
    ''' from 126 Ф-2, where the user stands in the history: "Шаг N из M" is N steps still
    ''' undoable out of the M that were made. When the budget has dropped old steps, that
    ''' is said on the same line, from the first drop on: the counter alone would invite
    ''' an undo that silently goes back further than it promises.</summary>
    Private Function SizeText() As String
        If image_Bitmap Is Nothing Then Return ""
        Dim size As String = image_Bitmap.Width.ToString() & " × " & image_Bitmap.Height.ToString()
        If Not dirty Then Return size
        Dim text As String = size & " · " & Localization.T("изменено")
        If edits_Made > 0 Then
            text &= " · " & Localization.TF("Шаг {0} из {1}", undo_History.Count, edits_Made)
            If undo_History.StepsWereDropped Then
                text &= " · " & Localization.T("Старые шаги отброшены: лимит памяти")
            End If
        End If
        Return text
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
            Case ImageFileWriter.ReplaceRefusal.MultiFrame
                Return Localization.T("в файле несколько страниц или кадров, правится только первый")
            Case ImageFileWriter.ReplaceRefusal.InsideArchive
                Return Localization.T("это файл внутри архива")
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

        Dim answer As DialogResult = ViewerBox.Show(Me,
            Localization.T("JPEG будет пересжат - это необратимо. Дата съёмки и остальные EXIF-данные сохранятся. Продолжить?"),
            Localization.T("Редактирование изображения"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
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
            dialog.InitialDirectory = If(String.IsNullOrEmpty(save_As_Folder), Path.GetDirectoryName(source_Path), save_As_Folder)
            dialog.FileName = If(String.IsNullOrEmpty(save_As_Stem), Path.GetFileNameWithoutExtension(source_Path), save_As_Stem) &
                              Localization.T(" (ред.)") & preferred
            dialog.OverwritePrompt = True
            dialog.AddExtension = True
            dialog.Filter = BuildSaveFilter(preferred)
            dialog.FilterIndex = 1
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return ""
            ' Never into the archive cache: the whole session directory is deleted when the
            ' archive is left, so a result saved there is lost without a word (G-03).
            If ArchivePaths.IsUnderArchiveCache(dialog.FileName) Then
                SetStatus(Localization.T("Сюда сохранить нельзя: это временная папка архива"))
                Return ""
            End If
            Return dialog.FileName
        End Using
    End Function

    ''' <summary>Where "Save as.." starts, and the name it offers, when the picture being
    ''' edited is not where the user thinks it is - an archive entry is a temporary copy
    ''' under the archive cache, and the default must be the folder the ARCHIVE is in and
    ''' the entry's own name (audit 046, G-03). Empty means "beside the file".</summary>
    Private save_As_Folder As String = ""
    Private save_As_Stem As String = ""

    ''' <summary>Called by the viewer before the window is shown, for an archive entry.
    ''' <paramref name="entryName"/> is the name inside the archive ("1998/03/foto12.jpg");
    ''' only its last segment is used.</summary>
    Friend Sub SetSaveAsDefaults(folder As String, entryName As String)
        save_As_Folder = If(folder, "")
        Dim segment As String = If(entryName, "")
        Dim cut As Integer = segment.LastIndexOfAny(New Char() {"/"c, "\"c})
        If cut >= 0 Then segment = segment.Substring(cut + 1)
        Dim dot As Integer = segment.LastIndexOf("."c)
        save_As_Stem = If(dot > 0, segment.Substring(0, dot), segment)
    End Sub

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
        Catch ex As ImageFileWriter.ImageSaveIncompleteException
            ' The one failure after which "not saved" is not the whole truth: the original
            ' is no longer under its name. Both files are on disk, and the user is told
            ' where, rather than being left to believe nothing happened (G-01).
            AppFileLogger.LogException("Image editor: saving " & targetPath & " stopped half way", ex)
            saving = False
            ApplySaveAvailability()
            SetStatus(Localization.TF("Не удалось сохранить, и оригинал не вернулся на место. Оригинал: {0}; правка: {1}",
                                      ex.OriginalNowAt, ex.EditAt))
            Return
        Catch ex As Exception
            AppFileLogger.LogException("Image editor: saving " & targetPath, ex)
            saving = False
            ApplySaveAvailability()
            SetStatus(Localization.TF("Не удалось сохранить: {0}", FailureText.Describe(ex)))
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

    ''' <summary>
    ''' The keyboard (126 Ф-1): B/R/O/C select the tools, [ and ] walk the stroke width,
    ''' Ctrl+S saves, Ctrl+Z undoes, Ctrl+Shift+Z and Ctrl+Y redo, Ctrl+[ and Ctrl+] turn
    ''' the picture, H and V mirror it. The window is its own surface - none of this
    ''' collides with the viewer's letters (126 Q126-2), and with KeyPreview on every
    ''' handled key stops at the form, so a focused tool button is never pressed by
    ''' accident.
    ''' </summary>
    Private Sub Image_Editor_Form_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        If saving Then Return

        Dim handled As Boolean = True
        If e.Control AndAlso Not e.Alt Then
            Select Case e.KeyCode
                Case Keys.Z
                    If e.Shift Then RedoLastEdit() Else UndoLastEdit()
                Case Keys.Y
                    RedoLastEdit()
                Case Keys.S
                    SaveFromKeyboard()
                Case Keys.OemOpenBrackets
                    RotateEditorImage(clockwise:=False)
                Case Keys.OemCloseBrackets
                    RotateEditorImage(clockwise:=True)
                Case Else
                    handled = False
            End Select
        ElseIf Not e.Alt Then
            Select Case e.KeyCode
                Case Keys.B
                    SelectTool(EditorTool.Brush)
                Case Keys.R
                    SelectTool(EditorTool.RectangleOutline)
                Case Keys.O
                    SelectTool(EditorTool.EllipseOutline)
                Case Keys.C
                    SelectTool(EditorTool.Crop)
                Case Keys.H
                    FlipEditorImage(horizontal:=True)
                Case Keys.V
                    FlipEditorImage(horizontal:=False)
                Case Keys.OemOpenBrackets
                    StepThickness(-1)
                Case Keys.OemCloseBrackets
                    StepThickness(+1)
                Case Else
                    handled = False
            End Select
        Else
            handled = False
        End If

        If handled Then
            e.Handled = True
            Return
        End If

        If e.KeyCode = Keys.Enter Then
            ' Enter applies the crop frame (§6.1). It does nothing else in this window -
            ' there is no default button - so it cannot be pressed by accident into
            ' something worse. It stays outside the letter ladder: Ctrl+Enter meant the
            ' same thing before the keyboard grew, and still does.
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

    ''' <summary>Ctrl+S: the Save button's own path, so the JPEG confirmation and the
    ''' enabled-state reasoning happen exactly once, in one place.</summary>
    Private Sub SaveFromKeyboard()
        If btn_Save.Enabled Then
            btn_Save.PerformClick()
        Else
            ' The reason is already in the status line and on the button's tooltip;
            ' re-deriving it here would only be a second place to drift.
            ApplySaveAvailability()
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
    '''
    ''' Asked through the dialog layer with NAMED answers (126 UX-605): "Сохранить" saves
    ''' and the window closes when the write is done, "Не сохранять" discards, "Отмена" -
    ''' and Escape, and the close box - stays. The old Yes/No box had the user press No to
    ''' mean keep, which is the wrong question. This attempt at closing is always
    ''' cancelled; the answer's own flow closes the window when it is ready to.
    ''' </summary>
    Private Function ConfirmDiscardingEdits(reason As CloseReason) As Boolean
        If Not dirty Then Return True
        If reason <> CloseReason.UserClosing Then Return True
        ' DialogResult.OK is the save path closing the window itself - there is nothing
        ' unsaved left to ask about.
        If Me.DialogResult = DialogResult.OK Then Return True
        If discard_Confirmed OrElse close_Question_Open Then Return discard_Confirmed

        close_Question_Open = True
        Dim answer As Integer = AskDialogs.Choose(Me, AskSpecs.EditorUnsavedClose())
        close_Question_Open = False

        Select Case answer
            Case AskSpecs.EditorCloseSave
                SaveFromCloseQuestion()
            Case AskSpecs.EditorCloseDiscard
                discard_Confirmed = True
                Me.Close()
            Case Else
                ' Отмена: the window stays exactly as it is.
        End Select

        ' Whatever the answer, this close request itself is over - the save flow and the
        ' discard above close the window through their own calls.
        Return False
    End Function

    ''' <summary>The "Сохранить" answer. Over the original when that is allowed, else the
    ''' "Save as.." the status line has been advertising all along. A refused JPEG
    ''' confirmation or an abandoned file dialog leaves the window open with the edits
    ''' still in it - nothing was saved, so there is nothing to close over.</summary>
    Private Async Sub SaveFromCloseQuestion()
        If replace_Refusal = ImageFileWriter.ReplaceRefusal.None Then
            If Not ConfirmJpegRecompression(source_Path) Then Return
            Await WriteTo(source_Path, overOriginal:=True)
            Return
        End If

        Dim target As String = AskWhereToSave()
        If String.IsNullOrEmpty(target) Then Return
        Await WriteTo(target, overOriginal:=String.Equals(target, source_Path, StringComparison.OrdinalIgnoreCase))
    End Sub

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

    ''' <summary>
    ''' The chrome this window was handed but never owns. A Control disposes neither a
    ''' Font it was handed (057.S18-1) nor the toolbar's ToolTip component (057.S18-3),
    ''' and <c>FormClosed</c> does not run at all when a failed open disposes the window
    ''' before it was ever shown - so the one path that covers every exit is this
    ''' override, after the base has finished tearing the controls down.
    ''' </summary>
    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then
            tool_Tips.Dispose()
            If editor_Font IsNot Nothing Then
                editor_Font.Dispose()
                editor_Font = Nothing
            End If
        End If
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
            ' Outside the application theme on purpose (spec 060 rule 10, APP-STYLE rule 5): the
            ' canvas is the photo's surround, the same neutral dark in both themes, like a
            ' viewer's letterbox - a literal colour, so the theme leaves it alone.
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
