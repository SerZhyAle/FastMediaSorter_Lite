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
#If Not NETFRAMEWORK Then
    Private answerRow As DialogButtonRow
#End If
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
        status.ForeColor = SystemColors.GrayText
        btnCopy.Text = Localization.T("Копировать")
        AddHandler btnCopy.Click, AddressOf CopyAllText
        btnClose.Text = Localization.T("Закрыть")
        AddHandler btnClose.Click, Sub() Close()

#If Not NETFRAMEWORK Then
        BuildDockedLayout()
        ' LAST line of the builder: scaling touches only the children that exist by now (124 UX-202).
        DialogLayout.ApplyAutoScale(Me)
#Else
        box.Bounds = New Rectangle(12, 12, ClientSize.Width - 24, ClientSize.Height - 66)
        box.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        Controls.Add(box)

        status.AutoSize = False
        status.Bounds = New Rectangle(12, box.Bottom + 8, ClientSize.Width - 24, 24)
        status.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        Controls.Add(status)

        btnCopy.Bounds = New Rectangle(ClientSize.Width - 12 - Localization.Scaled(220), ClientSize.Height - 40, Localization.Scaled(105), 28)
        btnCopy.Anchor = AnchorStyles.Right Or AnchorStyles.Bottom
        Controls.Add(btnCopy)

        btnClose.Bounds = New Rectangle(ClientSize.Width - 12 - Localization.Scaled(105), ClientSize.Height - 40, Localization.Scaled(105), 28)
        btnClose.Anchor = AnchorStyles.Right Or AnchorStyles.Bottom
        Controls.Add(btnClose)
#End If
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' The modern window is a docked layout, not absolute positions. The absolute one put the status line
    ''' (24 px, 8 px under the text box) over the top 18 px of the button row (28 px high, 40 px from the bottom),
    ''' and a control added earlier is drawn OVER one added later, so the file name's label hid the buttons - at
    ''' every scale, and with a 42 px label at 175 % (ticket 124 left the numbers alone and only scaled them).
    ''' Here the text box fills what is left and the status and the buttons are docked along the bottom, so neither
    ''' the scale nor the language can push the buttons out of the window. Measures are 96-dpi pixels:
    ''' DialogLayout.ApplyAutoScale, the builder's last line, scales them.
    '''
    ''' The two commands are the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4): Copy is a command of the
    ''' window, Close its dismissing answer, and the row measures them by caption and glyph. The status line (the
    ''' file name, "copied") sits in its own docked line above the row, ellipsized, since the row owns its whole width
    ''' and its own height. A non-modal information window with no question: Bind makes Escape the Close button (as
    ''' the hand-wired handler below did) and leaves Enter alone - a read-only text has no default answer, and Enter
    ''' pressed in it must not close the window.
    ''' </summary>
    Private Sub BuildDockedLayout()
        box.Dock = DockStyle.Fill
        box.Name = "details_Box"
        Dim body As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(12, 12, 12, 4), .Name = "details_Body"}
        body.Controls.Add(box)

        status.AutoSize = False
        status.AutoEllipsis = True
        status.Dock = DockStyle.Bottom
        status.Height = 24
        status.TextAlign = ContentAlignment.MiddleLeft
        status.Padding = New Padding(12, 0, 12, 0)
        status.Name = "details_Status"

        btnCopy.Name = "details_Copy"
        btnClose.Name = "details_Close"
        answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "details_Buttons", .Padding = New Padding(12, 4, 12, 12)}
        answerRow.Add(btnCopy, DialogButtonRole.Command, "action.copy")
        answerRow.Add(btnClose, DialogButtonRole.Dismissing, "nav.close")

        ' Docked bottom edges are taken from the LAST added control inwards, and the Fill one takes what remains: the
        ' body first, then the status line, the row last (at the very bottom, and last in the tab order).
        Controls.Add(body)
        Controls.Add(status)
        Controls.Add(answerRow)
        answerRow.Bind(Me, bindAccept:=False)
    End Sub
#End If

#If Not NETFRAMEWORK Then
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' A window authored in 96-dpi pixels grows with the scale; on a small or very high-scale display it
        ' can end up larger than the monitor, which pushes the bottom strip with the buttons off the screen.
        DialogLayout.ClampToWorkingArea(Me)
    End Sub
#End If

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
#If Not NETFRAMEWORK Then
            ' Nothing rendered at all: say so instead of leaving an empty body (124 UX-208).
            If box.TextLength = 0 Then box.Text = Localization.T("Не удалось прочитать сведения о файле.")
#End If
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
                    AppendLine(Localization.TF("{0}: {1}", Localization.T(row.LabelKey), row.Value))
                End If
            Next
            AppendLine("")
        Next
        box.ResumeLayout()
#If Not NETFRAMEWORK Then
        ' Appending at the end leaves the view scrolled to the bottom when the text is longer than the box (a long
        ' path, a small window): the user opens the window to read from the top.
        box.SelectionStart = 0
        box.SelectionLength = 0
        box.ScrollToCaret()
#End If
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
#If Not NETFRAMEWORK Then
            ' The clipboard is held by another program for a moment: tell the user, do not fail silently.
            status.Text = Localization.T("Буфер обмена занят другой программой. Повторите.")
#End If
            AppFileLogger.WriteLine("File details copy skipped [" & ex.GetType().Name & "]: " & ex.Message)
        End Try
    End Sub

    Private Sub File_Details_Form_KeyDown(sender As Object, e As KeyEventArgs) Handles MyBase.KeyDown
        ' The net48 window has no CancelButton, so Escape is wired by hand; on modern the row's Bind makes Escape the
        ' Close button, which closes it first - this stays as the net48 path.
        If e.KeyCode = Keys.Escape Then Close()
    End Sub
End Class
