Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The "about file" window of the first panel's "i" item. One read-only, selectable,
''' copyable text of everything the product knows about the file on screen: the file
''' itself, an image with its camera, an e-book, a text file's shape - and, on the
''' modern build, the audio and video tracks libvlc can parse.
'''
''' Deliberately a plain RichTextBox rather than a property grid: the rows a file offers
''' differ per type, a long path must wrap instead of being cut, and the one action a
''' details window gets asked for - "give me that text" - is Ctrl+C on the selection,
''' which the box already knows how to do.
'''
''' The window is a SNAPSHOT: it describes the file as it was when the user clicked.
''' Flipping to the next picture does not rewrite it (the next click re-opens it), and a
''' file deleted under it shows what it was, not an error. The media track section
''' arrives when libvlc finishes parsing - the window never blocks on it.
''' </summary>
Public NotInheritable Class File_Details_Form
    Inherits Form

    Private ReadOnly filePath As String
    Private ReadOnly mediaSectionProvider As Func(Of String, Task(Of List(Of FileDetailSection)))
    Private ReadOnly box As New RichTextBox()
    Private ReadOnly status As New Label()
    Private ReadOnly btnCopy As New Button()
    Private ReadOnly btnClose As New Button()
    Private ui_Font As Font
    Private header_Font As Font

    ''' <param name="mediaSectionProvider">The modern build's libvlc reader of the audio
    ''' and video track rows; Nothing on the net48 fallback, which ships no media engine.
    ''' Called off the click, awaited, never blocking the UI thread.</param>
    Public Sub New(filePath As String, Optional mediaSectionProvider As Func(Of String, Task(Of List(Of FileDetailSection))) = Nothing)
        Me.filePath = filePath
        Me.mediaSectionProvider = mediaSectionProvider
        Build()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then
            ' Form.Font does not take ownership of a Font it was handed (Hotkeys_Form).
            If ui_Font IsNot Nothing Then ui_Font.Dispose() : ui_Font = Nothing
            If header_Font IsNot Nothing Then header_Font.Dispose() : header_Font = Nothing
        End If
    End Sub

    Private Sub Build()
        ui_Font = New Font(Localization.FontFamily(), 9.0F)
        header_Font = New Font(Localization.FontFamily(), 10.0F, FontStyle.Bold)

        Text = Localization.T("О файле")
        FormBorderStyle = FormBorderStyle.Sizable
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(Localization.Scaled(560), Localization.Scaled(480))
        MinimumSize = New Size(Localization.Scaled(400), Localization.Scaled(260))
        Font = ui_Font
        KeyPreview = True
        ' Text direction only - never RightToLeftLayout (the localization rules).
        If Localization.IsRightToLeft() Then RightToLeft = RightToLeft.Yes

        box.ReadOnly = True
        box.BorderStyle = BorderStyle.None
        box.BackColor = SystemColors.Window
        box.ForeColor = SystemColors.WindowText
        box.DetectUrls = False
        box.WordWrap = True
        box.AccessibleName = Localization.T("О файле")
        box.Bounds = New Rectangle(12, 12, ClientSize.Width - 24, ClientSize.Height - 66)
        box.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        Controls.Add(box)

        status.AutoSize = False
        status.Bounds = New Rectangle(12, box.Bottom + 8, ClientSize.Width - 24, 24)
        status.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        status.ForeColor = SystemColors.GrayText
        Controls.Add(status)

        btnCopy.Text = Localization.T("Копировать")
        btnCopy.Bounds = New Rectangle(ClientSize.Width - 12 - Localization.Scaled(220), ClientSize.Height - 40, Localization.Scaled(105), 28)
        btnCopy.Anchor = AnchorStyles.Right Or AnchorStyles.Bottom
        AddHandler btnCopy.Click, AddressOf CopyAllText
        Controls.Add(btnCopy)

        btnClose.Text = Localization.T("Закрыть")
        btnClose.Bounds = New Rectangle(ClientSize.Width - 12 - Localization.Scaled(105), ClientSize.Height - 40, Localization.Scaled(105), 28)
        btnClose.Anchor = AnchorStyles.Right Or AnchorStyles.Bottom
        AddHandler btnClose.Click, Sub() Close()
        Controls.Add(btnClose)
    End Sub

    Private Async Sub File_Details_Form_Shown(sender As Object, e As EventArgs) Handles MyBase.Shown
        box.Text = ""
        status.Text = Localization.T("Чтение метаданных файла..")
        Try
            ' Off the UI thread: a network path's FileInfo and a header read are I/O,
            ' and the click must not freeze the viewer for them.
            Dim sections As List(Of FileDetailSection) =
                Await Task.Run(Function() FileDetails.BuildSections(filePath)).ConfigureAwait(True)
            If IsDisposed Then Return
            RenderSections(sections)
            status.Text = Path.GetFileName(filePath)

            If mediaSectionProvider IsNot Nothing AndAlso
               (FileDetails.IsAudioFile(filePath) OrElse FileDetails.IsVideoFile(filePath)) Then
                Dim media As List(Of FileDetailSection) = Await mediaSectionProvider(filePath).ConfigureAwait(True)
                If IsDisposed Then Return
                If media IsNot Nothing AndAlso media.Count > 0 Then RenderSections(media)
            End If
        Catch ex As Exception
            ' The rows already rendered stay: a details window that lost only one source
            ' still answers for the rest.
            status.Text = Path.GetFileName(filePath)
            AppFileLogger.WriteLine("File details not read [" & ex.GetType().Name & "]: " & ex.Message)
        End Try
    End Sub

    Private Sub RenderSections(sections As IEnumerable(Of FileDetailSection))
        box.SuspendLayout()
        For Each section As FileDetailSection In sections
            If section Is Nothing OrElse section.IsEmpty Then Continue For
            AppendTitle(Localization.T(section.TitleKey))
            For Each row As FileDetailRow In section.Rows
                If row.Value.Length = 0 Then
                    AppendLine(Localization.T(row.LabelKey))
                Else
                    AppendLine(Localization.T(row.LabelKey) & ": " & row.Value)
                End If
            Next
            AppendLine("")
        Next
        box.ResumeLayout()
    End Sub

    Private Sub AppendTitle(text As String)
        box.SelectionStart = box.TextLength
        box.SelectionFont = header_Font
        box.SelectedText = text
        box.SelectionFont = ui_Font
        box.SelectedText = Environment.NewLine
    End Sub

    Private Sub AppendLine(text As String)
        box.SelectionStart = box.TextLength
        box.SelectionFont = ui_Font
        box.SelectedText = text & Environment.NewLine
    End Sub

    Private Sub CopyAllText(sender As Object, e As EventArgs)
        ' Nothing rendered yet (the read is still running): the clipboard keeps its
        ' content rather than being replaced with an empty string or throwing.
        If box.Text.Length = 0 Then Return
        Try
            Clipboard.SetText(box.Text)
            status.Text = Localization.T("Скопировано в буфер обмена")
        Catch ex As Exception
            AppFileLogger.WriteLine("File details copy skipped [" & ex.GetType().Name & "]: " & ex.Message)
        End Try
    End Sub

    Private Sub File_Details_Form_KeyDown(sender As Object, e As KeyEventArgs) Handles MyBase.KeyDown
        ' Non-modal: no CancelButton, so Escape is wired by hand.
        If e.KeyCode = Keys.Escape Then Close()
    End Sub
End Class
