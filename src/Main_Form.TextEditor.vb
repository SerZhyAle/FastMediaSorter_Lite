#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Specification 277 steps P4.2, P4.4, P4.5 and P5.1: the in-place editor over the full text reader, behind
' TextEditingEnabled. A plain RichEdit surface over the media area takes the keyboard whole; the file is written
' only by an explicit save, through TextFileSaver; every way out of a dirty text passes one guard; a protected draft
' keeps the typing through a crash. The decisions without a window are in Documents\TextEditRules.vb,
' TextDocumentBuffer.vb, TextFileSaver.vb and TextDraftManager.vb. With the opt-in off nothing here runs.
Partial Public Class Main_Form

    ''' <summary>How a write ended, for the caller that goes on after it.</summary>
    Private Enum TextEditWrite
        ''' <summary>The text is in the file that was asked for.</summary>
        Written
        ''' <summary>Nothing changed; the editor stays with its edits (a failure said why, or the user cancelled).</summary>
        NotWritten
        ''' <summary>The answer to a question took the document away (Reload, Save As): a waiting navigation must not run.</summary>
        Left
    End Enum

    ''' <summary>The editing surface. Plain text only: paste takes the clipboard's text and nothing else, and the chords that
    ''' would give plain text a look (bold, alignment, line spacing) do nothing.</summary>
    Private NotInheritable Class PlainTextSurface
        Inherits RichTextBox

        Private Const EM_LINESCROLL As Integer = &HB6
        Private Const EM_GETFIRSTVISIBLELINE As Integer = &HCE
        Private Const EM_SETUNDOLIMIT As Integer = &H452
        Private Const UndoSteps As Integer = 1000

        <DllImport("user32.dll", EntryPoint:="SendMessageW")>
        Private Shared Function SendMessage(hWnd As IntPtr, message As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        End Function

        ''' <summary>False while another layer owns the keyboard (the F10 ring, the help card, a menu).</summary>
        Public Property KeysAllowed As Func(Of Boolean)

        Protected Overrides Sub OnHandleCreated(e As EventArgs)
            MyBase.OnHandleCreated(e)
            SendMessage(Handle, EM_SETUNDOLIMIT, New IntPtr(UndoSteps), IntPtr.Zero)
        End Sub

        Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
            If KeysAllowed Is Nothing OrElse KeysAllowed.Invoke() Then
                Select Case TextEditRules.CommandOf(keyData)
                    Case TextEditorCommand.Paste
                        If Not Me.ReadOnly Then Paste(DataFormats.GetFormat(DataFormats.UnicodeText))
                        Return True
                    Case TextEditorCommand.Swallow
                        Return True
                End Select
            End If
            Return MyBase.ProcessCmdKey(msg, keyData)
        End Function

        ''' <summary>Puts the caret at a place and that place's row at the top of the surface.</summary>
        Public Sub ShowAtTop(charIndex As Integer)
            Me.Select(charIndex, 0)
            ScrollToCaret()
            Dim first As Integer = SendMessage(Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32()
            SendMessage(Handle, EM_LINESCROLL, IntPtr.Zero, New IntPtr(GetLineFromCharIndex(charIndex) - first))
        End Sub
    End Class

    ''' <summary>The editor's frame: the commands work from the surface and from the find bar alike.</summary>
    Private NotInheritable Class TextEditorContainer
        Inherits Panel

        ''' <summary>The key and whether it is an auto-repeat; True when it was a command.</summary>
        Public Property CommandKey As Func(Of Keys, Boolean, Boolean)

        Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
            If CommandKey IsNot Nothing AndAlso CommandKey.Invoke(keyData, KeyRouting.IsAutoRepeat(msg.LParam.ToInt64())) Then Return True
            Return MyBase.ProcessCmdKey(msg, keyData)
        End Function
    End Class

    ''' <summary>A strip painted by the glue: the line numbers and the status line.</summary>
    Private NotInheritable Class TextEditorStrip
        Inherits Panel

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            TabStop = False
        End Sub
    End Class

    ''' <summary>The surface is on screen with a document in it.</summary>
    Private text_Editing As Boolean
    ''' <summary>The file is being read on the pool for the editor.</summary>
    Private text_Edit_Opening As Boolean
    ''' <summary>A write runs on the pool, or its question is on screen: the surface is read-only meanwhile.</summary>
    Private text_Edit_Saving As Boolean
    ''' <summary>A find or a replace over the whole text runs on the pool.</summary>
    Private text_Edit_Finding As Boolean
    ''' <summary>A replace over the whole text runs on the pool: the surface is read-only for it, and a save waits, or it would
    ''' call clean a text that is about to change.</summary>
    Private text_Edit_Replacing As Boolean
    ''' <summary>The question about a found draft is on screen.</summary>
    Private text_Edit_Asking_Draft As Boolean
    ''' <summary>The text is being put on the surface: the change events are not an edit.</summary>
    Private text_Edit_Loading As Boolean

    Private text_Edit_Container As TextEditorContainer
    Private text_Edit_Box As PlainTextSurface
    Private text_Edit_Gutter As TextEditorStrip
    Private text_Edit_Status As TextEditorStrip
    Private text_Edit_Bar As FlowLayoutPanel
    Private text_Edit_Find_Box As TextBox
    Private text_Edit_Replace_Box As TextBox
    Private text_Edit_Replace_Part As Control()
    Private text_Edit_Find_Case As CheckBox
    Private text_Edit_Find_Word As CheckBox
    Private text_Edit_Find_Regex As CheckBox
    Private text_Edit_Find_Count As Label
    Private text_Edit_Font As Font
    Private text_Edit_Ui_Font As Font
    Private text_Edit_Colors As TextReaderPalette
    Private text_Edit_Idle_Timer As System.Windows.Forms.Timer
    Private text_Edit_Draft_Timer As System.Windows.Forms.Timer

    Private text_Edit_Buffer As TextDocumentBuffer
    Private text_Edit_Snapshot As TextFileSnapshot
    Private text_Edit_Encoding As Encoding
    Private text_Edit_With_Bom As Boolean
    ''' <summary>Normalize Line Endings: the one terminator of the next save; Nothing keeps each line's own.</summary>
    Private text_Edit_Normalize As String
    ''' <summary>The surface's text at the last clean moment: an undo back to it is clean again.</summary>
    Private text_Edit_Clean_Text As String
    ''' <summary>Dirty without a change of the words: a new encoding, or the text came from a draft.</summary>
    Private text_Edit_Force_Dirty As Boolean
    ''' <summary>The marker on screen; the exact answer costs a read of the whole text, so it is taken when the typing pauses.</summary>
    Private text_Edit_Looks_Dirty As Boolean
    Private text_Edit_Line_Starts As Integer()
    ''' <summary>Counts the changes of the text; a draft and a list of hits belong to one value of it.</summary>
    Private text_Edit_Version As Long
    Private text_Edit_Draft_Version As Long
    ''' <summary>This file may have a draft (section 7.4 item 3); decided once off the UI thread when the editor opens.</summary>
    Private text_Edit_Draft_Allowed As Boolean
    ''' <summary>The last draft write or delete on the pool; the next one follows it, so they never overtake each other.</summary>
    Private text_Edit_Draft_Task As Task
    ''' <summary>The file changed under the reader's lines: leaving the editor reads it again.</summary>
    Private text_Edit_Saved_Any As Boolean
    Private text_Edit_Hits As List(Of TextEditHit)
    Private text_Edit_Hits_Key As String = ""

    ''' <summary>The opt-in is on and this document could be edited at all: what shows the Edit command.</summary>
    Private Function TextEditingOn() As Boolean
        Return modern_Preferences IsNot Nothing AndAlso modern_Preferences.FullTextReaderEnabled AndAlso modern_Preferences.TextEditingEnabled
    End Function

    Private Function TextEditFileName() As String
        Return Path.GetFileName(text_Current_File_Path)
    End Function

    Private Function SameTextEditPath(left As String, right As String) As Boolean
        Return String.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase)
    End Function

    ' --- entering ------------------------------------------------------------------------------------------------

    ''' <summary>
    ''' Ctrl+E, or Restore on a draft. The file is read again whole on the pool: the editor works on the file's own
    ''' bytes, never on the reader's lines. A file the encoding cannot give back byte for byte is refused, and so is a
    ''' text the surface itself would change: a save would rewrite the part nobody touched.
    ''' </summary>
    Private Async Sub BeginTextEdit(Optional draft As TextDraft = Nothing)
        If text_Editing OrElse text_Edit_Opening OrElse text_Edit_Asking_Draft OrElse Not FullTextReaderShown() OrElse Not TextEditingOn() Then Return
        Dim filePath As String = text_Current_File_Path
        If text_Virtual Then
            Notify(Localization.TF("Размер файла ({0}) больше предела правки ({1}). Файл открыт только для чтения, по частям.",
                FormatFileSize(text_Current_Result.TotalBytes),
                FormatFileSize(CLng(modern_Preferences.TextEditingMaxMb) * 1048576L)), NotifyLevel.Info)
            Return
        End If
        ' VM-08: saving a binary file through a text encoding would destroy it.
        If text_Binary_View Then
            Notify(Localization.T("Бинарный файл открыт только для просмотра - править его и сохранять из него выдержки нельзя."), NotifyLevel.Info)
            Return
        End If
        Dim insideArchive As Boolean = IsArchiveMode() OrElse ArchivePaths.IsUnderArchiveCache(filePath)
        If insideArchive Then
            Notify(Localization.T("Файл внутри архива только читается - чтобы изменить его, сначала извлеките его из архива"), NotifyLevel.Info)
            Return
        End If
        Dim insideRemoteCache As Boolean = IsResourceMode() OrElse RemoteCachePaths.IsUnderRemoteCache(filePath)
        If insideRemoteCache Then
            Notify(Localization.T("Файл сервера только читается - изменить можно файл на этом компьютере или в общей папке"), NotifyLevel.Info)
            Return
        End If
        If SecureBlocksWritingAction() Then Return
        SlideShowStop()
        If TextMarkdownRenderedShown() Then ShowTextMarkdownSource()

        Dim detection As EncodingDetectionResult = text_Detection
        Dim ceiling As Long = CLng(modern_Preferences.TextEditingMaxMb) * 1048576L
        Dim autosave As Boolean = modern_Preferences.TextRecoveryAutosave
        Dim opening As TextEditOpening = Nothing
        Dim draftAllowed As Boolean
        Dim folder As String = TextDraftManager.DefaultFolder()
        Dim pending As Task = text_Edit_Draft_Task
        Dim unanswered As TextDraft = Nothing
        text_Edit_Opening = True
        Try
            Await Task.Run(
                Sub()
                    draftAllowed = TextDraftManager.Allowed(autosave, IsOnLocalReadyVolume(filePath), insideArchive, insideRemoteCache, False)
                    If draft Is Nothing AndAlso draftAllowed Then unanswered = ReadDifferingTextDraft(folder, filePath, pending)
                    If draft Is Nothing AndAlso unanswered Is Nothing Then opening = TextEditRules.OpenForEditing(filePath, detection.Encoding, detection.HasBom, detection.BomLength, ceiling)
                End Sub)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException
            Notify(Localization.TF("Не удалось прочитать файл: {0}", FailureText.Describe(ex)), NotifyLevel.Error)
            Return
        Finally
            text_Edit_Opening = False
        End Try
        If IsDisposed OrElse text_Editing OrElse Not FullTextReaderShown() OrElse Not String.Equals(text_Current_File_Path, filePath, StringComparison.OrdinalIgnoreCase) Then Return

        ' A draft nobody answered is the user's earlier work and the first draft of this edit would replace it, so it is
        ' answered first. "Not now" leaves the reader as it is and the draft where it is.
        If unanswered IsNot Nothing Then
            Dim answer As Integer = AskTextDraft(filePath, unanswered)
            If IsDisposed OrElse text_Editing OrElse Not FullTextReaderShown() OrElse Not String.Equals(text_Current_File_Path, filePath, StringComparison.OrdinalIgnoreCase) Then Return
            If answer = AskSpecs.TextDraftDelete Then
                DeleteTextEditDraft(filePath)
                BeginTextEdit()
                Return
            End If
            If answer <> AskSpecs.TextDraftRestore Then Return
            draft = unanswered
        End If

        Dim buffer As TextDocumentBuffer
        If draft IsNot Nothing Then
            buffer = New TextDocumentBuffer(draft.Text)
        ElseIf opening.Problem = TextEditProblem.TooLarge Then
            Notify(Localization.T("Файл стал больше предела правки - откройте его заново"), NotifyLevel.Warning)
            Return
        ElseIf opening.Problem = TextEditProblem.BytesDoNotRoundTrip Then
            Notify(Localization.TF("В файле есть байты, которых нет в кодировке {0}: сохранение изменило бы их. Выберите другую кодировку (Ctrl+Shift+E)",
                TextReaderLayout.EncodingName(detection.Encoding, detection.HasBom)), NotifyLevel.Warning)
            Return
        Else
            buffer = opening.Buffer
        End If

        EnsureTextEditSurface()
        ApplyTextEditLook()
        Dim box As PlainTextSurface = text_Edit_Box
        Dim expected As String = buffer.EditorText()
        Cursor.Current = Cursors.WaitCursor
        text_Edit_Loading = True
        box.Text = expected
        Dim shown As String = box.Text
        If Not buffer.IsUnchanged(shown) Then
            box.Clear()
            text_Edit_Loading = False
            Dim place As Integer = TextEditRules.FirstDifference(expected, shown)
            Notify(Localization.TF("Этот файл здесь не правится: поле правки не хранит символ {0} (строка {1}) как есть, и сохранение изменило бы его",
                TextEditRules.CharacterLabel(expected, place),
                (TextEditRules.LineOfIndex(TextEditRules.LineStarts(expected), place) + 1).ToString()), NotifyLevel.Warning)
            Return
        End If
        ApplyTextEditTabs(expected.Length)
        box.ClearUndo()
        box.Modified = False
        text_Edit_Loading = False

        text_Edit_Buffer = buffer
        text_Edit_Clean_Text = If(draft Is Nothing, expected, Nothing)
        text_Edit_Line_Starts = TextEditRules.LineStarts(expected)
        text_Edit_Version = 0L
        text_Edit_Draft_Version = 0L
        text_Edit_Saved_Any = False
        text_Edit_Hits = Nothing
        text_Edit_Draft_Allowed = draftAllowed
        If draft Is Nothing Then
            text_Edit_Snapshot = opening.Snapshot
            text_Edit_Encoding = detection.Encoding
            text_Edit_With_Bom = detection.HasBom
            text_Edit_Normalize = Nothing
            text_Edit_Force_Dirty = False
        Else
            text_Edit_Snapshot = draft.Snapshot
            text_Edit_Encoding = TextEncodingDetector.CreateEncoding(draft.CodePage, False)
            text_Edit_With_Bom = draft.WithBom
            text_Edit_Normalize = If(String.IsNullOrEmpty(draft.NormalizedTerminator), Nothing, draft.NormalizedTerminator)
            text_Edit_Force_Dirty = True
        End If
        text_Edit_Looks_Dirty = text_Edit_Force_Dirty

        ' The caret starts where the reader was reading.
        Dim firstLine As Integer = If(text_Scroll_Offset >= 0 AndAlso text_Scroll_Offset < text_Rows.Count, text_Rows(text_Scroll_Offset).Line, 0)
        text_Editing = True
        text_Edit_Container.Visible = True
        text_Edit_Container.BringToFront()
        LayoutTextEditor(Me, EventArgs.Empty)
        box.ShowAtTop(text_Edit_Line_Starts(Math.Min(firstLine, text_Edit_Line_Starts.Length - 1)))
        ActiveControl = box
        box.Focus()

        text_Edit_Draft_Timer.Interval = Math.Max(ModernViewerPreferences.TextRecoveryIntervalSecondsMin, modern_Preferences.TextRecoveryIntervalSeconds) * 1000
        text_Edit_Draft_Timer.Start()
        UpdateContentWindowTitle()
        SyncDocumentPanel()
    End Sub

    Private Sub EnsureTextEditSurface()
        If text_Edit_Container IsNot Nothing Then Return
        text_Edit_Container = New TextEditorContainer With {
            .Name = "pnl_TextEditorContainer", .Visible = False, .RightToLeft = RightToLeft.No, .CommandKey = AddressOf TextEditorCommandKey}
        text_Edit_Box = New PlainTextSurface With {
            .Name = "txt_TextEditor", .Dock = DockStyle.Fill, .BorderStyle = BorderStyle.None, .DetectUrls = False,
            .EnableAutoDragDrop = False, .AllowDrop = False, .HideSelection = False, .AcceptsTab = True, .Multiline = True,
            .RightToLeft = RightToLeft.No, .AccessibleName = Localization.T("Правка"),
            .KeysAllowed = Function() Not TextEditorKeysTaken()}
        text_Edit_Gutter = New TextEditorStrip With {.Dock = DockStyle.Left}
        text_Edit_Status = New TextEditorStrip With {.Dock = DockStyle.Bottom}
        BuildTextEditBar()
        ' Docking takes the last added first: the bar and the status line span the width, the numbers stand beside the text.
        text_Edit_Container.Controls.Add(text_Edit_Box)
        text_Edit_Container.Controls.Add(text_Edit_Gutter)
        text_Edit_Container.Controls.Add(text_Edit_Bar)
        text_Edit_Container.Controls.Add(text_Edit_Status)
        panel_Media.Controls.Add(text_Edit_Container)
        ' The surface needs its window before the text goes in: until then Text only echoes what it was given,
        ' and the check that the surface keeps the file's text would prove nothing.
        Dim surfaceWindow As IntPtr = text_Edit_Box.Handle

        AddHandler text_Edit_Gutter.Paint, AddressOf PaintTextEditGutter
        AddHandler text_Edit_Status.Paint, AddressOf PaintTextEditStatus
        AddHandler text_Edit_Box.TextChanged, AddressOf TextEditTextChanged
        AddHandler text_Edit_Box.SelectionChanged, Sub(sender, args) text_Edit_Status.Invalidate()
        AddHandler text_Edit_Box.VScroll, Sub(sender, args) text_Edit_Gutter.Invalidate()
        AddHandler text_Edit_Box.Resize, Sub(sender, args) text_Edit_Gutter.Invalidate()
        AddHandler panel_Media.SizeChanged, AddressOf LayoutTextEditor
        text_Edit_Idle_Timer = New System.Windows.Forms.Timer With {.Interval = 300}
        AddHandler text_Edit_Idle_Timer.Tick, AddressOf TextEditIdleTick
        text_Edit_Draft_Timer = New System.Windows.Forms.Timer()
        AddHandler text_Edit_Draft_Timer.Tick, AddressOf TextEditDraftTick
    End Sub

    ''' <summary>The reader's own look on the surface. Word wrap and the scroll bars rebuild the control's window, so they
    ''' are set before the text goes in. The reader's line spacing, whitespace marks and syntax colours are not applied
    ''' here: formatting a plain text surface would land in its undo list.</summary>
    Private Sub ApplyTextEditLook()
        Dim prefs As ModernViewerPreferences = modern_Preferences
        Dim systemBack As Color = NeutralBackColor()
        text_Edit_Colors = TextReaderLayout.Palette(prefs.TextTheme, systemBack, GetOppositeColor(systemBack))
        Dim scale As Single = GetCurrentDpiScale()
        Dim points As Single = Math.Max(ModernViewerPreferences.TextFontSizeMin,
            Math.Min(ModernViewerPreferences.TextFontSizeMax, prefs.TextFontSize * text_Font_Scale))
        Dim family As String = If(text_Current_Result.IsMonospace, prefs.TextFontFamilyMonospace, prefs.TextFontFamilyProportional)
        Dim oldFont As Font = text_Edit_Font
        Dim oldUiFont As Font = text_Edit_Ui_Font
        text_Edit_Font = New Font(family, points * scale, FontStyle.Regular)
        text_Edit_Ui_Font = New Font(ScriptFonts.Family("Segoe UI"), Math.Max(8.0F, 9.0F * scale), FontStyle.Regular)

        Dim box As PlainTextSurface = text_Edit_Box
        box.WordWrap = prefs.TextWrap
        box.ScrollBars = If(prefs.TextWrap, RichTextBoxScrollBars.Vertical, RichTextBoxScrollBars.Both)
        box.Font = text_Edit_Font
        box.BackColor = text_Edit_Colors.Back
        box.ForeColor = text_Edit_Colors.Ink
        text_Edit_Container.BackColor = text_Edit_Colors.Back
        text_Edit_Gutter.BackColor = text_Edit_Colors.Back
        text_Edit_Status.BackColor = text_Edit_Colors.Back
        text_Edit_Gutter.Font = text_Edit_Font
        text_Edit_Status.Font = text_Edit_Ui_Font
        text_Edit_Status.Height = CInt(TextRenderer.MeasureText("0", text_Edit_Ui_Font).Height * 1.3F) + 4
        text_Edit_Status.Visible = prefs.TextStatusBar
        text_Edit_Gutter.Visible = prefs.TextLineNumbers
        text_Edit_Bar.Font = text_Edit_Ui_Font
        text_Edit_Bar.RightToLeft = If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No)
        ThemeApplier.ApplyTree(text_Edit_Bar, ThemeService.Palette)
        oldFont?.Dispose()
        oldUiFont?.Dispose()
    End Sub

    ''' <summary>The tab stops of the setting's width. A paragraph setting over the whole text: above 1 MiB the control's
    ''' default stops stay, the time is not worth it. The call lands in the undo list, so it is made before that is cleared.</summary>
    Private Sub ApplyTextEditTabs(length As Integer)
        If length > 1048576 Then Return
        Dim width As Integer = TextRenderer.MeasureText(New String(" "c, modern_Preferences.TextTabWidth), text_Edit_Font, Size.Empty, TextFormatFlags.NoPadding).Width
        If width <= 0 Then Return
        Dim stops(31) As Integer
        For index As Integer = 0 To stops.Length - 1
            stops(index) = (index + 1) * width
        Next
        text_Edit_Box.SelectAll()
        text_Edit_Box.SelectionTabs = stops
        text_Edit_Box.Select(0, 0)
    End Sub

    ' --- leaving -------------------------------------------------------------------------------------------------

    Private Function TextEditDirty() As Boolean
        If Not text_Editing Then Return False
        If text_Edit_Force_Dirty OrElse text_Edit_Normalize IsNot Nothing Then Return True
        If Not text_Edit_Box.Modified Then Return False
        Return Not String.Equals(text_Edit_Box.Text, text_Edit_Clean_Text, StringComparison.Ordinal)
    End Function

    ''' <summary>
    ''' The one guard of every way out of the document (section 7.1 item 3). True: the editor is gone and the caller goes
    ''' on. False: the caller stops; after Save succeeds <paramref name="retry"/> runs the same action again.
    ''' </summary>
    Friend Function TextEditorAllowsLeaving(retry As Action) As Boolean
        If Not text_Editing Then Return True
        If text_Edit_Saving Then
            Notify(Localization.T("Файл ещё сохраняется"), NotifyLevel.Info)
            Return False
        End If
        If Not TextEditDirty() Then
            ' The edits were undone after a draft of them was written: that draft has nothing left to recover.
            If text_Edit_Draft_Version <> 0L Then DeleteTextEditDraft(text_Current_File_Path)
            EndTextEdit()
            Return True
        End If
        Dim answer As Integer
        Using dialog As New Ask_Dialog(AskSpecs.TextEditUnsaved(TextEditFileName()))
            DialogLayer.ShowViewerDialog(dialog, Me)
            answer = dialog.Answer
        End Using
        If Not text_Editing Then Return True
        Select Case answer
            Case AskSpecs.TextEditUnsavedSave
                SaveTextEditThenLeave(retry)
                Return False
            Case AskSpecs.TextEditUnsavedDiscard
                DeleteTextEditDraft(text_Current_File_Path)
                EndTextEdit()
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Async Sub SaveTextEditThenLeave(retry As Action)
        If Await WriteTextEditAsync(text_Current_File_Path, text_Edit_Snapshot) <> TextEditWrite.Written OrElse IsDisposed Then Return
        EndTextEdit()
        retry?.Invoke()
    End Sub

    ''' <summary>The surface goes, the reader's page comes back; after a save it is read from the file again, at its place.</summary>
    Private Sub EndTextEdit()
        If Not text_Editing Then Return
        Dim filePath As String = text_Current_File_Path
        Dim reread As Boolean = text_Edit_Saved_Any
        Dim request As Integer = -1
        If reread AndAlso text_Edit_Encoding IsNot Nothing AndAlso text_Detection IsNot Nothing AndAlso
           text_Edit_Encoding.CodePage <> text_Detection.Encoding.CodePage Then
            request = text_Edit_Encoding.CodePage
        End If
        ' The reader continues at the line the caret was on; its own place and the remembered one follow from there.
        Dim caretLine As Integer = TextEditRules.LineOfOffset(text_Edit_Box.Text, text_Edit_Box.SelectionStart)
        text_Editing = False
        text_Edit_Idle_Timer.Stop()
        text_Edit_Draft_Timer.Stop()
        text_Edit_Bar.Visible = False
        text_Edit_Container.Visible = False
        text_Edit_Loading = True
        text_Edit_Box.Clear()
        text_Edit_Box.ClearUndo()
        text_Edit_Loading = False
        text_Edit_Buffer = Nothing
        text_Edit_Snapshot = Nothing
        text_Edit_Clean_Text = Nothing
        text_Edit_Line_Starts = Nothing
        text_Edit_Normalize = Nothing
        text_Edit_Hits = Nothing
        text_Edit_Force_Dirty = False
        text_Edit_Looks_Dirty = False
        text_Edit_Saved_Any = False
        ActiveControl = Nothing
        Focus()
        If reread AndAlso FullTextReaderShown() Then
            Dim scroll As Integer = text_Scroll_Offset
            text_Encoding_Request = request
            LoadTextDocument(filePath)
            If FullTextReaderShown() Then
                text_Scroll_Offset = scroll
                text_Pending_Top = Nothing
            End If
        End If
        If FullTextReaderShown() Then text_Pending_Reveal = New TextPosition(caretLine, 0)
        UpdateContentWindowTitle()
        SyncDocumentPanel()
        GetActivePictureBox()?.Invalidate()
    End Sub

    ''' <summary>
    ''' The last line, in ReleaseTextDocument: the document goes by a path no guard stood on (a window of its own moved the
    ''' file away). The typing is not lost: it goes to a draft, or where drafts are not allowed, to a copy the user names;
    ''' cancelling that dialog is the user's own refusal, and is said back.
    ''' </summary>
    Private Sub RescueTextEditOnRelease()
        If Not text_Editing Then Return
        Dim filePath As String = text_Current_File_Path
        Dim dirty As Boolean = TextEditDirty()
        Dim edited As String = If(dirty, text_Edit_Box.Text, Nothing)
        Dim composed As String = If(dirty, text_Edit_Buffer.ComposeText(edited, text_Edit_Normalize), Nothing)
        Dim allowed As Boolean = text_Edit_Draft_Allowed
        Dim encoding As Encoding = text_Edit_Encoding
        Dim withBom As Boolean = text_Edit_With_Bom
        Dim draft As TextDraft = If(dirty, NewTextDraft(filePath, composed), Nothing)
        text_Edit_Saved_Any = False
        EndTextEdit()
        If Not dirty Then Return
        If allowed AndAlso WriteTextDraftNow(draft) Then
            Notify(Localization.TF("Файл {0} закрыт с несохранёнными правками: они в черновике и будут предложены при следующем открытии", Path.GetFileName(filePath)), NotifyLevel.Warning)
            Return
        End If
        ' This string is the only copy of the typing now: the dialog comes back until it is written or the user refuses.
        Do
            Using dialog As New SaveFileDialog()
                dialog.Title = Localization.TF("Файл {0} закрыт с несохранёнными правками: сохранить их как", Path.GetFileName(filePath))
                dialog.FileName = Path.GetFileName(filePath)
                dialog.OverwritePrompt = True
                If dialog.ShowDialog(Me) <> DialogResult.OK Then
                    Notify(Localization.TF("Правки файла {0} не сохранены", Path.GetFileName(filePath)), NotifyLevel.Warning)
                    Return
                End If
                Dim outcome As TextSaveOutcome = TextFileSaver.Save(New TextSaveRequest With {
                    .TargetPath = dialog.FileName, .Text = composed, .Encoding = encoding, .WithBom = withBom, .ReplaceUnencodable = False})
                If outcome.Status = TextSaveStatus.Unencodable Then
                    outcome = TextFileSaver.Save(New TextSaveRequest With {
                        .TargetPath = dialog.FileName, .Text = composed, .Encoding = New UTF8Encoding(False), .WithBom = False})
                End If
                If outcome.Status = TextSaveStatus.Saved Then
                    Notify(Localization.TF("Файл сохранён: {0}", Path.GetFileName(dialog.FileName)), NotifyLevel.Info)
                    Return
                End If
                Notify(Localization.TF("Правки файла {0} не сохранены", Path.GetFileName(filePath)), NotifyLevel.Error)
            End Using
        Loop
    End Sub

    ''' <summary>Windows asks whether it may end, and nothing can ask back. The typing goes to a draft while the disk still
    ''' answers; where no draft may hold it (a network file, drafts switched off) or the write failed, the answer is no, and
    ''' the user, told why, saves or lets Windows go on regardless.</summary>
    Private Function TextEditStopsShutdown() As Boolean
        If Not text_Editing OrElse Not TextEditDirty() Then Return False
        If text_Edit_Draft_Allowed AndAlso WriteTextDraftNow(NewTextDraft(text_Current_File_Path, text_Edit_Buffer.ComposeText(text_Edit_Box.Text, text_Edit_Normalize))) Then Return False
        Notify(Localization.TF("В файле {0} есть несохранённые правки - завершение работы Windows остановлено, сохраните файл", TextEditFileName()), NotifyLevel.Warning)
        Return True
    End Function

    ''' <summary>The window is gone. The guard already ran, or Windows is ending past its answer, so nothing asks now.</summary>
    Private Sub TearDownTextEditor()
        text_Editing = False
        text_Edit_Idle_Timer?.Dispose()
        text_Edit_Draft_Timer?.Dispose()
        text_Edit_Idle_Timer = Nothing
        text_Edit_Draft_Timer = Nothing
        If text_Edit_Container IsNot Nothing Then
            RemoveHandler panel_Media.SizeChanged, AddressOf LayoutTextEditor
            text_Edit_Container.Dispose()
            text_Edit_Container = Nothing
        End If
        text_Edit_Font?.Dispose()
        text_Edit_Ui_Font?.Dispose()
        text_Edit_Font = Nothing
        text_Edit_Ui_Font = Nothing
    End Sub

    ' --- saving --------------------------------------------------------------------------------------------------

    ''' <summary>
    ''' One write of the surface's text, with the answers to what can refuse it asked here: another program's version of the
    ''' file (section 7.3 item 1), a character the encoding does not have (section 6.2). The surface is read-only until it
    ''' ends. A failure leaves the file whole and the edits dirty (VM-15). A write to the document's own file makes the
    ''' editor clean; a write under another name leaves it as it is.
    ''' </summary>
    Private Async Function WriteTextEditAsync(target As String, snapshot As TextFileSnapshot) As Task(Of TextEditWrite)
        If Not text_Editing OrElse text_Edit_Saving Then Return TextEditWrite.NotWritten
        If text_Edit_Replacing Then
            Notify(Localization.T("Идёт замена по всему файлу - сохранить можно, когда она закончится"), NotifyLevel.Info)
            Return TextEditWrite.NotWritten
        End If
        Dim box As PlainTextSurface = text_Edit_Box
        Dim edited As String = box.Text
        Dim buffer As TextDocumentBuffer = text_Edit_Buffer
        Dim normalize As String = text_Edit_Normalize
        Dim ownFile As Boolean = SameTextEditPath(target, text_Current_File_Path)
        Dim versions As Integer = If(modern_Preferences.TextBackupOnSave, modern_Preferences.TextBackupVersions, 0)
        Dim replaced As Boolean = False
        Dim encoding As Encoding = text_Edit_Encoding
        Dim withBom As Boolean = text_Edit_With_Bom
        Dim composed As String = Nothing
        Dim reloadAfter As Boolean = False
        Dim saveAsAfter As Boolean = False
        Dim outcome As TextSaveOutcome = Nothing
        text_Edit_Saving = True
        box.ReadOnly = True
        Try
            While True
                Dim request As New TextSaveRequest With {
                    .TargetPath = target, .Encoding = encoding, .WithBom = withBom, .Snapshot = snapshot,
                    .BackupVersions = versions, .ReplaceUnencodable = replaced}
                outcome = Await Task.Run(
                    Function() As TextSaveOutcome
                        If composed Is Nothing Then composed = buffer.ComposeText(edited, normalize)
                        request.Text = composed
                        Return TextFileSaver.Save(request)
                    End Function)
                If IsDisposed OrElse Not text_Editing Then Return TextEditWrite.NotWritten

                If outcome.Status = TextSaveStatus.Saved Then Exit While
                If outcome.Status = TextSaveStatus.Failed Then
                    AppFileLogger.LogMessage("Text save failed: " & If(outcome.Failure Is Nothing, "unknown", outcome.Failure.GetType().Name))
                    Notify(Localization.TF("Не удалось сохранить файл: {0}", FailureText.Describe(outcome.Failure)), NotifyLevel.Error)
                    Return TextEditWrite.NotWritten
                End If
                If outcome.Status = TextSaveStatus.Unencodable Then
                    Dim answer As Integer
                    Using dialog As New Ask_Dialog(AskSpecs.TextEditUnencodable(TextReaderLayout.EncodingName(encoding, withBom), outcome.UnencodableCharacters, outcome.UnencodableCount))
                        DialogLayer.ShowViewerDialog(dialog, Me)
                        answer = dialog.Answer
                    End Using
                    If answer = AskSpecs.TextEditUnencodableUtf8 Then
                        encoding = New UTF8Encoding(False)
                        withBom = False
                    ElseIf answer = AskSpecs.TextEditUnencodableReplace Then
                        replaced = True
                    Else
                        Return TextEditWrite.NotWritten
                    End If
                    Continue While
                End If

                ' Another program's version of the file: nothing was written.
                Dim conflict As Integer
                Using dialog As New Ask_Dialog(If(outcome.TargetMissing, AskSpecs.TextEditMissing(Path.GetFileName(target)), AskSpecs.TextEditConflict(Path.GetFileName(target))))
                    DialogLayer.ShowViewerDialog(dialog, Me)
                    conflict = dialog.Answer
                End Using
                If outcome.TargetMissing Then
                    If conflict = AskSpecs.TextEditMissingWrite Then
                        snapshot = Nothing
                    ElseIf conflict = AskSpecs.TextEditMissingSaveAs Then
                        saveAsAfter = True
                        Exit While
                    Else
                        Return TextEditWrite.NotWritten
                    End If
                ElseIf conflict = AskSpecs.TextEditConflictOverwrite Then
                    snapshot = Nothing
                ElseIf conflict = AskSpecs.TextEditConflictSaveAs Then
                    saveAsAfter = True
                    Exit While
                ElseIf conflict = AskSpecs.TextEditConflictReload Then
                    reloadAfter = True
                    Exit While
                Else
                    Return TextEditWrite.NotWritten
                End If
            End While
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException
            AppFileLogger.LogMessage("Text save failed: " & ex.GetType().Name)
            Notify(Localization.TF("Не удалось сохранить файл: {0}", FailureText.Describe(ex)), NotifyLevel.Error)
            Return TextEditWrite.NotWritten
        Finally
            text_Edit_Saving = False
            If Not box.IsDisposed Then box.ReadOnly = False
        End Try

        If reloadAfter Then
            ReloadTextEditFromDisk()
            Return TextEditWrite.Left
        End If
        If saveAsAfter Then
            Await SaveTextEditAsAsync()
            Return TextEditWrite.Left
        End If

        If Not ownFile Then Return TextEditWrite.Written
        DeleteTextEditDraft(target)
        text_Edit_Saved_Any = True
        text_Edit_Encoding = encoding
        text_Edit_With_Bom = withBom
        If replaced Then
            ' The file now says "?" where the surface says the character: the reader shows what is really there.
            Notify(Localization.TF("Файл сохранён: {0}", Path.GetFileName(target)), NotifyLevel.Info)
            EndTextEdit()
            Return TextEditWrite.Left
        End If
        text_Edit_Buffer = New TextDocumentBuffer(composed)
        text_Edit_Snapshot = outcome.NewSnapshot
        text_Edit_Clean_Text = edited
        text_Edit_Normalize = Nothing
        text_Edit_Force_Dirty = False
        text_Edit_Looks_Dirty = False
        text_Edit_Draft_Version = text_Edit_Version
        box.Modified = False
        Notify(Localization.TF("Файл сохранён: {0}", Path.GetFileName(target)), NotifyLevel.Info)
        UpdateContentWindowTitle()
        SyncDocumentPanel()
        text_Edit_Status.Invalidate()
        Return TextEditWrite.Written
    End Function

    ''' <summary>Save As: the edits go to a new file and that file opens; the document's own file stays as it was. In the
    ''' reader the same chord stays what it was before the editor, saving the selection.</summary>
    Private Async Function SaveTextEditAsAsync() As Task
        If Not text_Editing OrElse text_Edit_Saving OrElse SecureBlocksWritingAction() Then Return
        Dim filePath As String = text_Current_File_Path
        Dim extension As String = Path.GetExtension(filePath)
        Dim target As String
        Using dialog As New SaveFileDialog()
            dialog.Title = Localization.T("Сохранить как..")
            dialog.InitialDirectory = Path.GetDirectoryName(filePath)
            dialog.FileName = Path.GetFileName(filePath)
            dialog.OverwritePrompt = True
            dialog.AddExtension = extension.Length > 0
            If extension.Length > 0 Then dialog.DefaultExt = extension.TrimStart("."c)
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            target = dialog.FileName
        End Using
        If ArchivePaths.IsUnderArchiveCache(target) OrElse RemoteCachePaths.IsUnderRemoteCache(target) Then
            Notify(Localization.T("Сюда сохранить нельзя: это временная папка"), NotifyLevel.Warning)
            Return
        End If
        If SameTextEditPath(target, filePath) Then
            Await WriteTextEditAsync(filePath, text_Edit_Snapshot)
            Return
        End If
        If Await WriteTextEditAsync(target, Nothing) <> TextEditWrite.Written OrElse IsDisposed Then Return
        DeleteTextEditDraft(filePath)
        EndTextEdit()
        ProcessArgument(target)
    End Function

    Private Sub ReloadTextEditFromDisk()
        Dim filePath As String = text_Current_File_Path
        DeleteTextEditDraft(filePath)
        text_Edit_Saved_Any = False
        EndTextEdit()
        LoadTextDocument(filePath)
        BeginTextEdit()
    End Sub

    ' --- drafts (section 7.4) -------------------------------------------------------------------------------------

    Private Function NewTextDraft(filePath As String, composed As String) As TextDraft
        Return New TextDraft With {
            .SourcePath = filePath, .SavedAtUtc = DateTime.UtcNow, .CodePage = text_Edit_Encoding.CodePage,
            .WithBom = text_Edit_With_Bom, .NormalizedTerminator = If(text_Edit_Normalize, ""),
            .Snapshot = text_Edit_Snapshot, .Text = composed}
    End Function

    Private Function WriteTextDraftNow(draft As TextDraft) As Boolean
        Try
            ' WaitAny, not Wait: a failed earlier write has been logged and must not stop this one.
            Dim pending As Task = text_Edit_Draft_Task
            If pending IsNot Nothing Then Task.WaitAny(New Task() {pending}, 2000)
            TextDraftManager.Write(TextDraftManager.DefaultFolder(), draft)
            Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is System.Security.Cryptography.CryptographicException
            AppFileLogger.LogMessage("Text draft not written: " & ex.GetType().Name)
            Return False
        End Try
    End Function

    ''' <summary>While the text is dirty and has changed since the last draft, one more goes to the disk off the UI thread.
    ''' A failure is only logged, without the words: the editor itself still holds them.</summary>
    Private Sub TextEditDraftTick(sender As Object, e As EventArgs)
        If Not text_Editing OrElse text_Edit_Saving OrElse Not text_Edit_Draft_Allowed Then Return
        If text_Edit_Draft_Task IsNot Nothing AndAlso Not text_Edit_Draft_Task.IsCompleted Then Return
        If text_Edit_Version = text_Edit_Draft_Version OrElse Not TextEditDirty() Then Return
        Dim edited As String = text_Edit_Box.Text
        Dim buffer As TextDocumentBuffer = text_Edit_Buffer
        Dim normalize As String = text_Edit_Normalize
        Dim draft As TextDraft = NewTextDraft(text_Current_File_Path, Nothing)
        Dim folder As String = TextDraftManager.DefaultFolder()
        text_Edit_Draft_Version = text_Edit_Version
        text_Edit_Draft_Task = Task.Run(
            Sub()
                Try
                    draft.Text = buffer.ComposeText(edited, normalize)
                    TextDraftManager.Write(folder, draft)
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is System.Security.Cryptography.CryptographicException
                    AppFileLogger.LogMessage("Text draft not written: " & ex.GetType().Name)
                    ' No draft holds this version: the next tick writes again, typed or not.
                    text_Edit_Draft_Version = -1L
                End Try
            End Sub)
    End Sub

    ''' <summary>A draft of the file that says something else, off the UI thread. One equal to the file is what a crash right
    ''' after a save leaves: it has nothing to recover and goes.</summary>
    Private Shared Function ReadDifferingTextDraft(folder As String, filePath As String, pending As Task) As TextDraft
        If pending IsNot Nothing Then Task.WaitAny(pending)
        Dim draft As TextDraft = TextDraftManager.Read(folder, filePath)
        If draft Is Nothing Then Return Nothing
        If Not TextDraftManager.MatchesFile(draft, filePath) Then Return draft
        TextDraftManager.TryDelete(folder, filePath)
        Return Nothing
    End Function

    Private Function AskTextDraft(filePath As String, draft As TextDraft) As Integer
        text_Edit_Asking_Draft = True
        Try
            Using dialog As New Ask_Dialog(AskSpecs.TextDraftFound(Path.GetFileName(filePath), draft.SavedAtUtc.ToLocalTime().ToString("g", Globalization.CultureInfo.CurrentCulture)))
                DialogLayer.ShowViewerDialog(dialog, Me)
                Return dialog.Answer
            End Using
        Finally
            text_Edit_Asking_Draft = False
        End Try
    End Function

    ''' <summary>After a draft write still running, so that one cannot put the draft back.</summary>
    Private Sub DeleteTextEditDraft(filePath As String)
        Dim folder As String = TextDraftManager.DefaultFolder()
        Dim pending As Task = text_Edit_Draft_Task
        If pending Is Nothing Then pending = Task.CompletedTask
        text_Edit_Draft_Task = pending.ContinueWith(
            Sub(done As Task)
                If Not TextDraftManager.TryDelete(folder, filePath) Then AppFileLogger.LogMessage("Text draft not deleted")
            End Sub, TaskScheduler.Default)
    End Sub

    ''' <summary>A file opens that has a draft which says something else (section 7.4 item 2). Not now keeps the draft for
    ''' the next open. A draft equal to the file is what a crash right after a save leaves: it has nothing to recover.</summary>
    Private Async Sub OfferTextDraft(filePath As String)
        If Not TextEditingOn() OrElse text_Virtual OrElse text_Binary_View Then Return
        Dim autosave As Boolean = modern_Preferences.TextRecoveryAutosave
        Dim insideArchive As Boolean = IsArchiveMode() OrElse ArchivePaths.IsUnderArchiveCache(filePath)
        Dim insideRemoteCache As Boolean = IsResourceMode() OrElse RemoteCachePaths.IsUnderRemoteCache(filePath)
        Dim insideSecure As Boolean = IsSecureContentOnScreen() OrElse IsCurrentFileSecureContainer()
        Dim folder As String = TextDraftManager.DefaultFolder()
        Dim pending As Task = text_Edit_Draft_Task
        Dim found As TextDraft = Nothing
        Try
            found = Await Task.Run(
                Function() As TextDraft
                    If Not TextDraftManager.Allowed(autosave, IsOnLocalReadyVolume(filePath), insideArchive, insideRemoteCache, insideSecure) Then Return Nothing
                    Return ReadDifferingTextDraft(folder, filePath, pending)
                End Function)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException
            AppFileLogger.LogMessage("Text draft not read: " & ex.GetType().Name)
            Return
        End Try
        ' An editor being opened asks about the draft itself.
        If found Is Nothing OrElse IsDisposed OrElse text_Editing OrElse text_Edit_Opening OrElse text_Edit_Asking_Draft OrElse Not FullTextReaderShown() OrElse
           Not String.Equals(text_Current_File_Path, filePath, StringComparison.OrdinalIgnoreCase) Then Return
        Dim answer As Integer = AskTextDraft(filePath, found)
        If answer = AskSpecs.TextDraftRestore Then
            If FullTextReaderShown() AndAlso String.Equals(text_Current_File_Path, filePath, StringComparison.OrdinalIgnoreCase) Then BeginTextEdit(found)
        ElseIf answer = AskSpecs.TextDraftDelete Then
            DeleteTextEditDraft(filePath)
        End If
    End Sub

    ' --- the keyboard --------------------------------------------------------------------------------------------

    ''' <summary>Another layer has the keyboard now: its keys are not the editor's.</summary>
    Private Function TextEditorKeysTaken() As Boolean
        Return chrome_Keyboard_Mode OrElse chrome_F10_Pending OrElse IsHelpCardUp() OrElse ChromeMenu.PopupVisible
    End Function

    ''' <summary>
    ''' Asked first in KeybUse (VM-18): while the editor is open no key reaches a viewer command. True without Handled sends
    ''' the key on to the focused control, the surface or the find bar. F1 stays the reference card; with the card, a menu or
    ''' the F10 ring up nothing types into a text nobody is looking at.
    ''' </summary>
    Friend Function TextEditorOwnsInput(e As KeyEventArgs) As Boolean
        If Not text_Editing OrElse e.KeyCode = Keys.F1 Then Return False
        If TextEditorKeysTaken() Then
            e.Handled = True
            e.SuppressKeyPress = True
            Return True
        End If
        If text_Edit_Container.ContainsFocus Then Return True
        text_Edit_Box.Focus()
        e.Handled = True
        e.SuppressKeyPress = True
        Return True
    End Function

    ''' <summary>The wheel never flips a file from under the editor; the surface scrolls itself.</summary>
    Friend Function TextEditorOwnsWheel(e As MouseEventArgs) As Boolean
        Return text_Editing
    End Function

    Private Function TextEditorCommandKey(keyData As Keys, isRepeat As Boolean) As Boolean
        If Not text_Editing OrElse TextEditorKeysTaken() Then Return False
        If text_Edit_Bar.Visible AndAlso (keyData = Keys.Enter OrElse keyData = (Keys.Shift Or Keys.Enter)) Then
            If text_Edit_Find_Box.Focused Then
                FindInTextEdit(keyData = Keys.Enter)
                Return True
            End If
            If text_Edit_Replace_Box.Focused Then
                ReplaceInTextEdit()
                Return True
            End If
            Return False
        End If
        Select Case TextEditRules.CommandOf(keyData)
            Case TextEditorCommand.Save
                If Not isRepeat Then SaveTextEditCommand()
            Case TextEditorCommand.SaveAs
                If Not isRepeat Then SaveTextEditAsCommand()
            Case TextEditorCommand.Find
                ShowTextEditBar(False)
            Case TextEditorCommand.Replace
                ShowTextEditBar(True)
            Case TextEditorCommand.FindNext
                FindInTextEdit(True)
            Case TextEditorCommand.FindPrevious
                FindInTextEdit(False)
            Case TextEditorCommand.GoToLine
                If Not isRepeat Then PromptTextEditLine()
            Case TextEditorCommand.ReopenWithEncoding
                If Not isRepeat Then ReopenTextWithEncoding()
            Case TextEditorCommand.Leave
                If Not isRepeat Then LeaveTextEditByKey(keyData = Keys.Escape)
            Case Else
                Return False
        End Select
        Return True
    End Function

    ''' <summary>Escape closes the find bar first; after that, and on Ctrl+E at once, the editor asks to be left.</summary>
    Private Sub LeaveTextEditByKey(byEscape As Boolean)
        If byEscape AndAlso text_Edit_Bar.Visible Then
            text_Edit_Bar.Visible = False
            text_Edit_Box.Focus()
            Return
        End If
        TextEditorAllowsLeaving(Nothing)
    End Sub

    Private Async Sub SaveTextEditCommand()
        If Not text_Editing Then Return
        ' A save of nothing would push the version from before the edit out of the backups.
        If Not TextEditDirty() Then
            Notify(Localization.T("Нечего сохранять: правок нет"), NotifyLevel.Info)
            Return
        End If
        Await WriteTextEditAsync(text_Current_File_Path, text_Edit_Snapshot)
    End Sub

    Private Async Sub SaveTextEditAsCommand()
        Await SaveTextEditAsAsync()
    End Sub

    Private Sub ToggleTextEdit()
        If text_Editing Then
            TextEditorAllowsLeaving(Nothing)
        Else
            BeginTextEdit()
        End If
    End Sub

    Private Sub PromptTextEditLine()
        Dim starts As Integer() = TextEditRules.LineStarts(text_Edit_Box.Text)
        Dim highest As Integer = starts.Length
        Dim range As String = Localization.TF("Номер строки от {0} до {1}", 1, highest)
        Dim prompt As New PromptSpec(Localization.T("Перейти к строке"), range, "", Localization.T("Перейти"),
            Function(value)
                Dim number As Integer
                If Not Integer.TryParse(value.Trim(), number) OrElse number < 1 OrElse number > highest Then Return range
                Return Nothing
            End Function)
        Dim answer As Integer
        Using dialog As New Ask_Dialog(prompt)
            DialogLayer.ShowViewerDialog(dialog, Me)
            If dialog.Answer <> 0 OrElse Not Integer.TryParse(dialog.TypedText.Trim(), answer) Then Return
        End Using
        If Not text_Editing Then Return
        text_Edit_Box.ShowAtTop(starts(Math.Max(0, Math.Min(highest - 1, answer - 1))))
        text_Edit_Box.Focus()
    End Sub

    ' --- change tracking and what follows it ---------------------------------------------------------------------

    Private Sub TextEditTextChanged(sender As Object, e As EventArgs)
        If text_Edit_Loading OrElse Not text_Editing Then Return
        text_Edit_Version += 1L
        text_Edit_Idle_Timer.Stop()
        text_Edit_Idle_Timer.Start()
        If Not text_Edit_Looks_Dirty Then
            text_Edit_Looks_Dirty = True
            UpdateContentWindowTitle()
        End If
        text_Edit_Status.Invalidate()
    End Sub

    ''' <summary>The typing paused: the line table, the exact dirty answer and what shows them.</summary>
    Private Sub TextEditIdleTick(sender As Object, e As EventArgs)
        text_Edit_Idle_Timer.Stop()
        If Not text_Editing Then Return
        text_Edit_Line_Starts = TextEditRules.LineStarts(text_Edit_Box.Text)
        Dim dirty As Boolean = TextEditDirty()
        If dirty <> text_Edit_Looks_Dirty Then
            text_Edit_Looks_Dirty = dirty
            UpdateContentWindowTitle()
        End If
        SyncDocumentPanel()
        UpdateTextEditGutterWidth()
        text_Edit_Gutter.Invalidate()
        text_Edit_Status.Invalidate()
    End Sub

    Private Sub UpdateTextEditGutterWidth()
        If Not text_Edit_Gutter.Visible OrElse text_Edit_Line_Starts Is Nothing Then Return
        Dim digits As Integer = TextReaderLayout.GutterDigits(text_Edit_Line_Starts.Length)
        Dim width As Integer = TextRenderer.MeasureText(New String("0"c, digits), text_Edit_Font, Size.Empty, TextFormatFlags.NoPadding).Width + CInt(10 * GetCurrentDpiScale())
        If text_Edit_Gutter.Width <> width Then text_Edit_Gutter.Width = width
    End Sub

    ''' <summary>The window's title carries the unsaved mark; Nothing when the editor is not open.</summary>
    Private Function TextEditWindowTitle() As String
        If Not text_Editing Then Return Nothing
        Return If(text_Edit_Looks_Dirty, "*", "") & TextEditFileName()
    End Function

    Private Sub LayoutTextEditor(sender As Object, e As EventArgs)
        If text_Edit_Container Is Nothing OrElse Not text_Editing Then Return
        Dim top As Integer = DocumentPaneTop()
        document_Pane_Top = top
        text_Edit_Container.SetBounds(0, top, panel_Media.ClientSize.Width, Math.Max(0, panel_Media.ClientSize.Height - top))
        UpdateTextEditGutterWidth()
    End Sub

    ''' <summary>Numbers stand on the first row of each line; a wrapped row has none. The line table is at most one pause old.</summary>
    Private Sub PaintTextEditGutter(sender As Object, e As PaintEventArgs)
        e.Graphics.Clear(text_Edit_Colors.Back)
        Dim box As PlainTextSurface = text_Edit_Box
        Dim starts As Integer() = text_Edit_Line_Starts
        If Not text_Editing OrElse starts Is Nothing OrElse Not box.IsHandleCreated Then Return
        Dim rowHeight As Integer = TextRenderer.MeasureText("0", text_Edit_Font).Height
        Dim right As Integer = text_Edit_Gutter.ClientSize.Width - CInt(5 * GetCurrentDpiScale())
        Dim row As Integer = box.GetLineFromCharIndex(box.GetCharIndexFromPosition(New Point(1, 1)))
        Dim bottom As Integer = box.ClientSize.Height
        Dim flags As TextFormatFlags = TextFormatFlags.Right Or TextFormatFlags.NoPadding Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine
        While True
            Dim first As Integer = box.GetFirstCharIndexFromLine(row)
            If first < 0 Then Exit While
            Dim top As Integer = box.GetPositionFromCharIndex(first).Y
            If top > bottom Then Exit While
            Dim line As Integer = TextEditRules.LineOfIndex(starts, first)
            If line < starts.Length AndAlso starts(line) = first Then
                TextRenderer.DrawText(e.Graphics, (line + 1).ToString(), text_Edit_Font, New Rectangle(0, top, right, rowHeight), text_Edit_Colors.Muted, flags)
            End If
            row += 1
        End While
    End Sub

    Private Sub PaintTextEditStatus(sender As Object, e As PaintEventArgs)
        e.Graphics.Clear(text_Edit_Colors.Back)
        If Not text_Editing Then Return
        Dim bounds As Rectangle = text_Edit_Status.ClientRectangle
        bounds.Inflate(-CInt(6 * GetCurrentDpiScale()), 0)
        TextRenderer.DrawText(e.Graphics, TextEditStatusLine(), text_Edit_Ui_Font, bounds, text_Edit_Colors.Muted,
            TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis)
    End Sub

    ''' <summary>The reader's status line as the editor knows it: the logical line, never the wrapped row (section 4.2 item 2).</summary>
    Private Function TextEditStatusLine() As String
        Dim box As PlainTextSurface = text_Edit_Box
        Dim parts As New List(Of String)()
        parts.Add(If(text_Edit_Looks_Dirty, "*", "") & TextEditFileName())
        Dim place As Integer = box.SelectionStart
        Dim line As Integer = TextEditRules.LineOfIndex(text_Edit_Line_Starts, place)
        Dim column As Integer = If(text_Edit_Line_Starts Is Nothing OrElse line >= text_Edit_Line_Starts.Length, 0, Math.Max(0, place - text_Edit_Line_Starts(line)))
        parts.Add(Localization.TF("Стр. {0}, стб. {1}", (line + 1).ToString(Globalization.CultureInfo.CurrentCulture), (column + 1).ToString(Globalization.CultureInfo.CurrentCulture)))
        If box.SelectionLength > 0 Then parts.Add(Localization.TF("Выделено: {0}", box.SelectionLength.ToString()))
        If text_Edit_Snapshot IsNot Nothing Then parts.Add(FormatFileSize(text_Edit_Snapshot.Length))
        parts.Add(TextReaderLayout.EncodingName(text_Edit_Encoding, text_Edit_With_Bom))
        parts.Add(TextEditLineEndingCaption())
        parts.Add(Localization.T("Правка"))
        Return String.Join("  |  ", parts)
    End Function

    Private Function TextEditLineEndingCaption() As String
        If text_Edit_Normalize IsNot Nothing Then
            Return TextReaderLayout.LineEndingName(If(text_Edit_Normalize = vbCrLf, LineEndingKind.Crlf, LineEndingKind.Lf))
        End If
        Dim kind As LineEndingKind = If(text_Edit_Buffer Is Nothing, LineEndingKind.None, text_Edit_Buffer.LineEnding)
        If kind = LineEndingKind.Mixed Then Return Localization.T("Разные концы строк")
        Return If(kind = LineEndingKind.None, "", TextReaderLayout.LineEndingName(kind))
    End Function

    ' --- find and replace (section 7.1) ----------------------------------------------------------------------------

    Private Sub BuildTextEditBar()
        Dim scale As Single = GetCurrentDpiScale()
        text_Edit_Bar = New FlowLayoutPanel With {
            .Name = "pnl_TextEditorFind", .Dock = DockStyle.Top, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = True, .Visible = False, .Padding = New Padding(CInt(4 * scale))}
        text_Edit_Find_Box = New TextBox With {.Width = CInt(220 * scale), .AccessibleName = Localization.T("Что найти")}
        text_Edit_Replace_Box = New TextBox With {.Width = CInt(220 * scale), .AccessibleName = Localization.T("На что заменить")}
        Dim findNext As Button = TextEditBarButton("Найти", "action.search", Sub() FindInTextEdit(True))
        Dim findPrevious As Button = TextEditBarButton("Назад", "nav.back", Sub() FindInTextEdit(False))
        Dim replaceOne As Button = TextEditBarButton("Заменить", "action.replace", Sub() ReplaceInTextEdit())
        Dim replaceAll As Button = TextEditBarButton("Заменить все", "action.replace", Sub() ReplaceAllInTextEdit())
        Dim closeBar As Button = TextEditBarButton("Закрыть", "nav.close", Sub() LeaveTextEditByKey(True))
        text_Edit_Replace_Part = New Control() {text_Edit_Replace_Box, replaceOne, replaceAll}

        Dim prefs As ModernViewerPreferences = modern_Preferences
        text_Edit_Find_Case = TextEditBarCheck("Учитывать регистр", prefs.TextSearchCase, Sub(value) prefs.TextSearchCase = value)
        text_Edit_Find_Word = TextEditBarCheck("Слово целиком", prefs.TextSearchWholeWord, Sub(value) prefs.TextSearchWholeWord = value)
        text_Edit_Find_Regex = TextEditBarCheck("Регулярное выражение", prefs.TextRegexSearch, Sub(value) prefs.TextRegexSearch = value)
        text_Edit_Find_Count = New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(CInt(6 * scale), CInt(6 * scale), 0, 0)}

        text_Edit_Bar.Controls.AddRange(New Control() {
            text_Edit_Find_Box, findNext, findPrevious, text_Edit_Replace_Box, replaceOne, replaceAll,
            text_Edit_Find_Case, text_Edit_Find_Word, text_Edit_Find_Regex, text_Edit_Find_Count, closeBar})
    End Sub

    Private Function TextEditBarButton(captionKey As String, glyphId As String, act As Action) As Button
        Dim button As New Button With {.Text = Localization.T(captionKey), .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .TabStop = True}
        ButtonGlyphs.Apply(button, glyphId, ButtonGlyphRole.Neutral)
        AddHandler button.Click, Sub(sender, args) act()
        Return button
    End Function

    Private Function TextEditBarCheck(captionKey As String, value As Boolean, store As Action(Of Boolean)) As CheckBox
        Dim check As New CheckBox With {.Text = Localization.T(captionKey), .AutoSize = True, .Checked = value}
        AddHandler check.CheckedChanged,
            Sub(sender, args)
                store(check.Checked)
                MarkSettingsDirty()
                text_Edit_Hits = Nothing
            End Sub
        Return check
    End Function

    ''' <summary>The selection, when there is one on a single line, is what to find; Replace also shows its own half.</summary>
    Private Sub ShowTextEditBar(withReplace As Boolean)
        For Each part As Control In text_Edit_Replace_Part
            part.Visible = withReplace
        Next
        text_Edit_Find_Case.Checked = modern_Preferences.TextSearchCase
        text_Edit_Find_Word.Checked = modern_Preferences.TextSearchWholeWord
        text_Edit_Find_Regex.Checked = modern_Preferences.TextRegexSearch
        Dim selected As String = text_Edit_Box.SelectedText
        If selected.Length > 0 AndAlso selected.Length <= 256 AndAlso selected.IndexOf(ControlChars.Lf) < 0 Then text_Edit_Find_Box.Text = selected
        text_Edit_Bar.Visible = True
        text_Edit_Find_Box.Focus()
        text_Edit_Find_Box.SelectAll()
    End Sub

    Private Function TextEditFindOptions() As TextFindOptions
        Return New TextFindOptions With {
            .Query = text_Edit_Find_Box.Text, .MatchCase = text_Edit_Find_Case.Checked,
            .WholeWord = text_Edit_Find_Word.Checked, .UseRegex = text_Edit_Find_Regex.Checked}
    End Function

    Private Sub SayTextFindProblem(problem As TextFindProblem)
        If problem = TextFindProblem.InvalidPattern Then
            Notify(Localization.T("Неверное регулярное выражение"), NotifyLevel.Warning)
        ElseIf problem = TextFindProblem.TimedOut Then
            Notify(Localization.T("Поиск остановлен: выражение считается слишком долго"), NotifyLevel.Warning)
        End If
    End Sub

    ''' <summary>The hits of the bar's question over the text as it is now; read on the pool, and only when the text or the
    ''' question changed since the last time. Nothing when another search is running.</summary>
    Private Async Function TextEditHitsAsync() As Task(Of List(Of TextEditHit))
        If text_Edit_Finding Then Return Nothing
        Dim options As TextFindOptions = TextEditFindOptions()
        Dim key As String = options.Query & ChrW(0) & text_Edit_Version.ToString()
        If text_Edit_Hits IsNot Nothing AndAlso key = text_Edit_Hits_Key Then Return text_Edit_Hits
        Dim text As String = text_Edit_Box.Text
        Dim problem As TextFindProblem = TextFindProblem.None
        Dim hits As List(Of TextEditHit)
        text_Edit_Finding = True
        Try
            hits = Await Task.Run(
                Function() As List(Of TextEditHit)
                    Dim found As TextFindProblem
                    Dim list As List(Of TextEditHit) = TextEditRules.FindAll(text, options, found)
                    problem = found
                    Return list
                End Function)
        Finally
            text_Edit_Finding = False
        End Try
        If Not text_Editing Then Return Nothing
        SayTextFindProblem(problem)
        text_Edit_Hits = hits
        text_Edit_Hits_Key = key
        Return hits
    End Function

    Private Sub GoToTextEditHit(hits As List(Of TextEditHit), forward As Boolean)
        Dim box As PlainTextSurface = text_Edit_Box
        Dim index As Integer = TextEditRules.NextHit(hits, box.SelectionStart, box.SelectionStart + box.SelectionLength, forward)
        If index < 0 Then
            text_Edit_Find_Count.Text = Localization.T("Совпадений нет")
            Return
        End If
        box.Select(hits(index).Start, hits(index).Length)
        box.ScrollToCaret()
        text_Edit_Find_Count.Text = Localization.TF("{0} из {1}", (index + 1).ToString(), hits.Count.ToString())
    End Sub

    Private Async Sub FindInTextEdit(forward As Boolean)
        If Not text_Editing Then Return
        If text_Edit_Find_Box.TextLength = 0 Then
            ShowTextEditBar(text_Edit_Replace_Box.Visible)
            Return
        End If
        Dim hits As List(Of TextEditHit) = Await TextEditHitsAsync()
        If hits IsNot Nothing AndAlso text_Editing Then GoToTextEditHit(hits, forward)
    End Sub

    ''' <summary>Replaces the hit that is selected, one undo step, and goes to the next; with none selected it only goes.</summary>
    Private Async Sub ReplaceInTextEdit()
        If Not text_Editing OrElse text_Edit_Saving OrElse text_Edit_Find_Box.TextLength = 0 Then Return
        Dim hits As List(Of TextEditHit) = Await TextEditHitsAsync()
        If hits Is Nothing OrElse Not text_Editing OrElse text_Edit_Saving Then Return
        Dim box As PlainTextSurface = text_Edit_Box
        Dim start As Integer = box.SelectionStart
        Dim length As Integer = box.SelectionLength
        Dim current As Integer = hits.FindIndex(Function(hit) hit.Start = start AndAlso hit.Length = length)
        If current >= 0 Then
            Try
                box.SelectedText = TextEditRules.ReplacementOf(box.Text, hits(current), TextEditFindOptions(), text_Edit_Replace_Box.Text)
            Catch ex As System.Text.RegularExpressions.RegexMatchTimeoutException
                SayTextFindProblem(TextFindProblem.TimedOut)
                Return
            End Try
            hits = Await TextEditHitsAsync()
            If hits Is Nothing OrElse Not text_Editing Then Return
        End If
        GoToTextEditHit(hits, True)
    End Sub

    ''' <summary>Every hit at once, one undo step: the text is worked out on the pool and goes back as one selection replace.</summary>
    Private Async Sub ReplaceAllInTextEdit()
        If Not text_Editing OrElse text_Edit_Saving OrElse text_Edit_Finding OrElse text_Edit_Find_Box.TextLength = 0 Then Return
        Dim box As PlainTextSurface = text_Edit_Box
        Dim text As String = box.Text
        Dim buffer As TextDocumentBuffer = text_Edit_Buffer
        Dim options As TextFindOptions = TextEditFindOptions()
        Dim replacement As String = text_Edit_Replace_Box.Text
        Dim count As Integer
        Dim problem As TextFindProblem = TextFindProblem.None
        Dim result As String
        text_Edit_Finding = True
        text_Edit_Replacing = True
        box.ReadOnly = True
        Try
            result = Await Task.Run(
                Function() As String
                    Dim replaced As Integer
                    Dim found As TextFindProblem
                    Dim value As String = TextEditRules.ReplaceAll(text, options, replacement, replaced, found)
                    count = replaced
                    problem = found
                    Return value
                End Function)
        Finally
            text_Edit_Finding = False
            text_Edit_Replacing = False
            If Not box.IsDisposed Then box.ReadOnly = False
        End Try
        ' The user left the file meanwhile, perhaps for another one now being edited: this result is not for it.
        If Not text_Editing OrElse Not ReferenceEquals(buffer, text_Edit_Buffer) Then Return
        SayTextFindProblem(problem)
        If problem <> TextFindProblem.None Then Return
        If count = 0 Then
            text_Edit_Find_Count.Text = Localization.T("Совпадений нет")
            Return
        End If
        Dim caret As Integer = box.SelectionStart
        box.SelectAll()
        box.SelectedText = result
        box.Select(Math.Min(caret, box.TextLength), 0)
        box.ScrollToCaret()
        text_Edit_Find_Count.Text = Localization.TF("Заменено: {0}", count.ToString())
    End Sub

    ' --- the panel's commands --------------------------------------------------------------------------------------

    ''' <summary>A panel command that can take the document away asks first, from the mouse and from the F10 ring alike
    ''' (ChromeSurface.RunAction and the "more" menu come here before they run an item). F10 itself leaves the document where it is.</summary>
    Private Function ChromeActionGate(item As ChromeItem, act As Action) As Boolean
        If Not text_Editing OrElse Not TextEditRules.ChromeItemLeavesDocument(item.Id) Then Return True
        Return TextEditorAllowsLeaving(act)
    End Function

    ''' <summary>The full reader's commands on the document panel. Edit and what belongs to it only with the opt-in on; the
    ''' encoding reads the file again in the reader and converts it in the editor.</summary>
    Private Function TextReaderCommands() As Dictionary(Of String, DocumentCommand)
        Dim commands As New Dictionary(Of String, DocumentCommand)(StringComparer.Ordinal)
        If TextEditingOn() Then
            commands(DocumentPanelRules.IdTextEdit) = New DocumentCommand With {
                .Action = AddressOf ToggleTextEdit, .IsChecked = Function() text_Editing,
                .IsVisible = Function() text_Editing OrElse TextEditingOn(),
                .IsEnabled = Function() Not text_Virtual AndAlso Not text_Binary_View}
            commands(DocumentPanelRules.IdTextSave) = New DocumentCommand With {
                .Action = AddressOf SaveTextEditCommand, .IsVisible = Function() text_Editing,
                .IsEnabled = Function() text_Edit_Looks_Dirty AndAlso Not text_Edit_Saving}
            commands(DocumentPanelRules.IdTextSaveAs) = New DocumentCommand With {
                .Action = AddressOf SaveTextEditAsCommand, .IsVisible = Function() text_Editing}
            commands(DocumentPanelRules.IdTextReplace) = New DocumentCommand With {
                .Action = Sub() ShowTextEditBar(True), .IsVisible = Function() text_Editing}
            commands(DocumentPanelRules.IdTextLineEnding) = New DocumentCommand With {
                .Menu = AddressOf TextEditLineEndingMenu, .IsVisible = Function() text_Editing,
                .Text = AddressOf TextEditLineEndingCaption}
        End If
        ' A file over the editing limit cannot be edited here (section 5.1 item 3): the two ways on stand in its place.
        commands(DocumentPanelRules.IdTextExcerpt) = New DocumentCommand With {
            .Action = AddressOf SaveTextExcerpt, .IsVisible = Function() text_Virtual AndAlso Not text_Binary_View}
        commands(DocumentPanelRules.IdTextExternal) = New DocumentCommand With {
            .Action = AddressOf OpenContentInDefaultProgram, .IsVisible = Function() text_Virtual}
        commands(DocumentPanelRules.IdTextFind) = New DocumentCommand With {.Action = AddressOf FindFromTextPanel}
        commands(DocumentPanelRules.IdTextEncoding) = New DocumentCommand With {
            .Menu = AddressOf TextEncodingMenu, .Text = AddressOf TextEncodingCaption,
            .IsEnabled = Function() Not TextMarkdownRenderedShown()}
        commands(DocumentPanelRules.IdTextMarkdown) = New DocumentCommand With {
            .Action = AddressOf ToggleTextMarkdownView, .IsChecked = Function() TextMarkdownRenderedShown(),
            .IsVisible = Function() Not text_Editing AndAlso TextMarkdownAvailable()}
        ' Translation through doc-html-translate only with its opt-in (section 1.1 item 2); a binary file has no text to send.
        If TextTranslationOn() Then
            commands(DocumentPanelRules.IdTextTranslate) = New DocumentCommand With {
                .Menu = AddressOf TextTranslateMenu, .IsEnabled = Function() Not text_Binary_View AndAlso Not text_Edit_Replacing}
        End If
        ' The zoom is the reader's: the surface has its own font, and the rendered page its own zoom on its toolbar.
        Dim readerZoom As Func(Of Boolean) = Function() Not text_Editing AndAlso Not TextMarkdownRenderedShown()
        commands(DocumentPanelRules.IdZoomOut) = New DocumentCommand With {.Action = Sub() ZoomText(-0.1F), .IsVisible = readerZoom}
        commands(DocumentPanelRules.IdZoomIn) = New DocumentCommand With {.Action = Sub() ZoomText(0.1F), .IsVisible = readerZoom}
        commands(DocumentPanelRules.IdZoomReset) = New DocumentCommand With {.Action = AddressOf ResetTextZoom, .IsVisible = readerZoom}
        ' The context line (280 section 4.6): the reader leaves the keys to the file sorting, the editor takes them for the text.
        commands(DocumentPanelRules.IdContext) = New DocumentCommand With {
            .Text = Function() If(text_Editing, Localization.T("Документ: клавиши работают с текстом"), Localization.T("Просмотр: клавиши сортируют файлы")),
            .Reserved = Function() New String() {Localization.T("Просмотр: клавиши сортируют файлы"), Localization.T("Документ: клавиши работают с текстом")}}
        Return commands
    End Function

    ''' <summary>Section 1.1 item 2: translation is an opt-in of its own and applies only while the full reader is on.</summary>
    Private Function TextTranslationOn() As Boolean
        Return modern_Preferences IsNot Nothing AndAlso modern_Preferences.FullTextReaderEnabled AndAlso modern_Preferences.TextTranslationEnabled
    End Function

    ''' <summary>D08 / P5.2: what goes to doc-html-translate. The selection and the editor's text are immutable snapshots handed
    ''' to the 276 coordinator, so the editor, its text and its dirty state stay as they are (VM-19); the saved file goes the
    ''' ordinary document way and is never saved first. <c>TextTranslationScope</c> decides which row comes first.</summary>
    Private Function TextTranslateMenu() As IEnumerable(Of ChromeMenuItem)
        Dim hasSelection As Boolean = If(text_Editing, text_Edit_Box IsNot Nothing AndAlso text_Edit_Box.SelectionLength > 0, HasTextSelection())
        Dim selection As New ChromeMenuItem("text_Translate_Selection") With {
            .Caption = Localization.T("Выделенный фрагмент"), .Enabled = hasSelection,
            .DisabledReason = If(hasSelection, "", Localization.T("Выделите текст мышью или нажмите Ctrl+A")),
            .Action = Sub() TranslateTextSnapshot(DocHtmlSnapshotScope.Selection)}
        Dim buffer As ChromeMenuItem = Nothing
        If text_Editing Then
            buffer = New ChromeMenuItem("text_Translate_Buffer") With {
                .Caption = Localization.T("Весь текст с несохранёнными изменениями"),
                .Action = Sub() TranslateTextSnapshot(DocHtmlSnapshotScope.Buffer)}
        End If
        Dim saved As New ChromeMenuItem("text_Translate_File") With {
            .Caption = Localization.T("Сохранённый файл"),
            .Tooltip = If(text_Editing AndAlso text_Edit_Looks_Dirty, Localization.T("Несохранённые изменения в обработку не попадут"), ""),
            .Action = AddressOf TranslateSavedTextFile}
        Dim rows As New List(Of ChromeMenuItem)()
        Dim snapshotRows As New List(Of ChromeMenuItem) From {selection}
        If buffer IsNot Nothing Then snapshotRows.Add(buffer)
        If String.Equals(modern_Preferences.TextTranslationScope, "file", StringComparison.Ordinal) Then
            rows.Add(saved)
            rows.AddRange(snapshotRows)
        Else
            rows.AddRange(snapshotRows)
            rows.Add(saved)
        End If
        Return rows
    End Function

    ''' <summary>The text is taken now, at the click: later typing never reaches the job (section 7 of 276, VM-19).</summary>
    Private Sub TranslateTextSnapshot(scope As DocHtmlSnapshotScope)
        If Not IsTextDocumentShown() OrElse text_Binary_View Then Return
        Dim text As String
        If text_Editing AndAlso text_Edit_Box IsNot Nothing Then
            text = If(scope = DocHtmlSnapshotScope.Selection,
                      text_Edit_Box.SelectedText,
                      text_Edit_Buffer.ComposeText(text_Edit_Box.Text, text_Edit_Normalize))
        ElseIf scope = DocHtmlSnapshotScope.Selection AndAlso HasTextSelection() Then
            text = TextViewRules.Extract(text_Current_Result.Lines, text_Selection)
        Else
            Return
        End If
        BeginTextSnapshotConversion(text_Current_File_Path, scope, text)
    End Sub

    ''' <summary>The file as it is on disk, through the coordinator's ordinary document path. A file of an archive or a server
    ''' and a secure container's content are refused the way the document command refuses them.</summary>
    Private Sub TranslateSavedTextFile()
        If Not IsTextDocumentShown() Then Return
        If IsSecureContentOnScreen() OrElse DocHtmlSnapshot.IsSecureOrigin(text_Current_File_Path) Then
            Notify(Localization.T("Текст защищённого файла не передаётся в doc-html-translate."), NotifyLevel.Warning)
            Return
        End If
        If IsArchiveMode() OrElse IsResourceMode() Then
            Notify(Localization.T("Сначала извлеките файл из архива"), NotifyLevel.Warning)
            Return
        End If
        BeginDocumentConversion(text_Current_File_Path)
    End Sub

    ''' <summary>Find goes to what is on screen: the editor's bar, the rendered page's own find, the reader's prompt.</summary>
    Private Sub FindFromTextPanel()
        If text_Editing Then
            ShowTextEditBar(text_Edit_Replace_Box.Visible)
        ElseIf TextMarkdownRenderedShown() Then
            RunHtmlCommand(HtmlHostCommand.FindStart)
        Else
            PromptTextSearch()
        End If
    End Sub

    Private Function TextEncodingCaption() As String
        If text_Editing Then Return TextReaderLayout.EncodingName(text_Edit_Encoding, text_Edit_With_Bom)
        Return If(text_Detection Is Nothing, "", TextReaderLayout.EncodingName(text_Detection.Encoding, text_Detection.HasBom))
    End Function

    Private Function TextEncodingMenu() As IEnumerable(Of ChromeMenuItem)
        Dim rows As New List(Of ChromeMenuItem)()
        If Not text_Editing Then
            rows.Add(New ChromeMenuItem("text_ReopenEncoding") With {
                .Caption = Localization.T("Открыть в другой кодировке.."), .Hint = "Ctrl+Shift+E", .Action = AddressOf ReopenTextWithEncoding})
            Return rows
        End If
        For Each candidate As EncodingCandidate In TextEncodingDetector.ManualCandidates
            Dim codePage As Integer = candidate.CodePage
            rows.Add(New ChromeMenuItem("text_Encoding_" & codePage.ToString()) With {
                .Caption = candidate.Name, .Checked = text_Edit_Encoding.CodePage = codePage,
                .Action = Sub() ConvertTextEditEncoding(codePage)})
        Next
        Return rows
    End Function

    ''' <summary>Convert Encoding (section 6.2): the whole text passes the strict encoder at once, and a text the encoding
    ''' cannot hold keeps the encoding it has and says which characters stopped it. UTF-16 and UTF-32 always carry their
    ''' mark, UTF-8 keeps what the file had. The new encoding reaches the disk with the next save.</summary>
    Private Sub ConvertTextEditEncoding(codePage As Integer)
        If Not text_Editing OrElse text_Edit_Saving OrElse text_Edit_Replacing OrElse codePage = text_Edit_Encoding.CodePage Then Return
        Dim encoding As Encoding = TextEncodingDetector.CreateEncoding(codePage, False)
        Dim withBom As Boolean = If(codePage = 65001, text_Edit_With_Bom, TextEncodingDetector.ByteOrderMarkOf(codePage, encoding).Length > 0)
        Dim characters As IReadOnlyList(Of String) = Nothing
        Dim count As Integer
        If TextFileSaver.Encode(text_Edit_Box.Text, encoding, withBom, False, characters, count) Is Nothing Then
            Notify(Localization.TF("Кодировка не изменена: символов вне {0} - {1}: {2}", TextReaderLayout.EncodingName(encoding, withBom), count, String.Join(" ", characters)), NotifyLevel.Warning)
            Return
        End If
        text_Edit_Encoding = encoding
        text_Edit_With_Bom = withBom
        text_Edit_Force_Dirty = True
        TextEditStateChanged()
    End Sub

    Private Function TextEditLineEndingMenu() As IEnumerable(Of ChromeMenuItem)
        Dim rows As New List(Of ChromeMenuItem)()
        rows.Add(New ChromeMenuItem("text_Line_Keep") With {
            .Caption = Localization.T("Как в файле"), .Checked = text_Edit_Normalize Is Nothing, .Action = Sub() NormalizeTextEdit(Nothing)})
        rows.Add(New ChromeMenuItem("text_Line_Crlf") With {
            .Caption = TextReaderLayout.LineEndingName(LineEndingKind.Crlf), .Checked = text_Edit_Normalize = vbCrLf, .Action = Sub() NormalizeTextEdit(vbCrLf)})
        rows.Add(New ChromeMenuItem("text_Line_Lf") With {
            .Caption = TextReaderLayout.LineEndingName(LineEndingKind.Lf), .Checked = text_Edit_Normalize = vbLf, .Action = Sub() NormalizeTextEdit(vbLf)})
        Return rows
    End Function

    ''' <summary>Normalize Line Endings (section 7.2 item 2) takes effect at the next save; choosing "as in the file" takes it back.</summary>
    Private Sub NormalizeTextEdit(terminator As String)
        If Not text_Editing OrElse text_Edit_Saving OrElse text_Edit_Replacing Then Return
        text_Edit_Normalize = terminator
        TextEditStateChanged()
    End Sub

    ''' <summary>A change that is not typing: the draft follows, the marks show it.</summary>
    Private Sub TextEditStateChanged()
        text_Edit_Version += 1L
        text_Edit_Looks_Dirty = TextEditDirty()
        UpdateContentWindowTitle()
        SyncDocumentPanel()
        text_Edit_Status.Invalidate()
    End Sub

    ' --- the reader's side ----------------------------------------------------------------------------------------

    ''' <summary>The full reader finished loading a file: the panel carries its commands, a draft of the file is offered and a
    ''' Markdown file opens the way the setting says.</summary>
    Private Sub TextReaderOpened(filePath As String)
        ShowDocumentCommands(DocumentReaderKind.Text, TextReaderCommands())
        RestoreTextPlace(filePath)
        OfferTextDraft(filePath)
        OpenTextMarkdownIfWanted(filePath)
    End Sub

    ''' <summary>The document goes: what the guards did not stop is rescued; its rendered page and the panel leave with it.</summary>
    Private Sub TextReaderReleasing()
        RememberTextPlace()
        RescueTextEditOnRelease()
        If text_Markdown_Page Then ReleaseHtmlView()
        If document_Panel_Kind = DocumentReaderKind.Text OrElse document_Wanted_Kind = DocumentReaderKind.Text Then HideDocumentPanel()
    End Sub
End Class
#End If
