Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Spec 227 rule 1, the intake door in the Share Manager's add-resource manager: one door that takes
''' the phone's connection code as pasted text, from the clipboard (a screenshot lands there), from an
''' opened file, or from the camera. Whatever came in is read by the same readers the viewer's panel
''' uses (the linked <c>PhoneConnectionIntake</c>), and a read code is relayed to the viewer - the ONE
''' importer - whose import dialog then asks what it always asks. The code is a secret holder: it is
''' shown here only in the field, and never written to any log.
''' </summary>
Friend NotInheritable Class Phone_Code_Intake_Form
    Inherits Form
    Implements IThemeAware

    Private ReadOnly _codeBox As TextBox
    Private ReadOnly _status As Label
    Private ReadOnly _relayButton As Button
    Private ReadOnly _cameraButton As Button
    Private ReadOnly _fileButton As Button
    Private ReadOnly _clipboardButton As Button
    Private _answerRow As DialogButtonRow
    Private _relay As PhoneCodeRelay
    Private _busy As Boolean

    Public Sub New()
        UiLanguage.ApplyTo(Me)
        Text = Localization.T("Код телефона")
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        ' The body ends at 164; the rest is the room of a one-line answer row (OnLoad adds the missing height of a longer one).
        ClientSize = New Size(560, 218)

        Dim explain As New Label With {
            .Location = New Point(16, 14),
            .Size = New Size(528, 44),
            .Text = Localization.T("Код добавит папку телефона в список ресурсов Fast Media Sorter на этом компьютере. Вставьте его, откройте файл или снимок, или отсканируйте камерой."),
            .UseMnemonic = False}

        ' A single-line field: the system password character is ignored by a multiline TextBox, so a
        ' multiline one would show the secret in clear (spec 227: the code is a secret holder).
        _codeBox = New TextBox With {
            .Location = New Point(16, 64),
            .Size = New Size(528, 24),
            .UseSystemPasswordChar = True,
            .AccessibleName = Localization.T("Код подключения")}

        _fileButton = New Button With {
            .Location = New Point(16, 100),
            .Size = New Size(168, 34),
            .Text = Localization.T("Открыть файл..")}
        AddHandler _fileButton.Click, AddressOf OnOpenFile

        _clipboardButton = New Button With {
            .Location = New Point(192, 100),
            .Size = New Size(168, 34),
            .Text = Localization.T("Из буфера..")}
        AddHandler _clipboardButton.Click, AddressOf OnFromClipboard

        _cameraButton = New Button With {
            .Location = New Point(368, 100),
            .Size = New Size(176, 34),
            .Text = Localization.T("Сканировать камерой..")}
        AddHandler _cameraButton.Click, AddressOf OnScanCamera

        _status = New Label With {
            .Location = New Point(16, 142),
            .Size = New Size(528, 22),
            .Text = "",
            .ForeColor = SystemColors.GrayText,
            .UseMnemonic = False}

        ' The answers ride the shared bottom row (ticket 243 T-8b batch C, BUTTONS 2.0 sections 3 and 4): Cancel first, Relay
        ' last, widths from the caption and its glyph. The three intake doors above (file, clipboard, camera) are commands
        ' of the field and stay where they are. Escape is Cancel; Enter is Relay as before - it only hands the typed code
        ' to the viewer, whose own import dialog asks what it always asks, so nothing is added or changed by it.
        _relayButton = New Button With {.Text = Localization.T("Передать"), .Name = "xpi_Relay"}
        AddHandler _relayButton.Click, AddressOf OnRelay

        Dim cancelButton As New Button With {.Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel, .Name = "xpi_Cancel"}
        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xpi_Buttons", .Padding = New Padding(16, 4, 16, 10)}
        DpiLayout.AddAnswer(_answerRow, _relayButton, DialogButtonRole.Affirmative, "action.confirm")
        DpiLayout.AddAnswer(_answerRow, cancelButton, DialogButtonRole.Dismissing, "action.cancel")

        Controls.AddRange({explain, _codeBox, _fileButton, _clipboardButton, _cameraButton, _status})
        ' Last in the collection: the docked row takes its edge first and reads after the field and its doors in the Tab order.
        Controls.Add(_answerRow)
        _answerRow.Bind(Me)

        ' Spec 227 rule 1: the door takes a dropped file the same as an opened one.
        AllowDrop = True
        AddHandler DragEnter,
            Sub(sender As Object, e As DragEventArgs)
                e.Effect = If(DroppedIntakeFile(e) IsNot Nothing, DragDropEffects.Copy, DragDropEffects.None)
            End Sub
        AddHandler DragDrop,
            Sub(sender As Object, e As DragEventArgs)
                Dim path As String = DroppedIntakeFile(e)
                If path IsNot Nothing Then OpenIntakeFile(path)
            End Sub

        DpiLayout.ApplyAutoScale(Me)
        ThemeService.Attach(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' A fixed-size window: make it taller by what a wrapped row needs (a long language, a high scale) and keep it on screen.
        DpiLayout.KeepRoomForRow(Me, _answerRow)
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    ''' <summary>The viewer probe the tray's own item uses, handed in by the window.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend WriteOnly Property ViewerExeProbe As Func(Of String)
        Set(value As Func(Of String))
            _relay = New PhoneCodeRelay(value)
        End Set
    End Property

    ''' <summary>The one supported intake file in a drop, or Nothing.</summary>
    Private Shared Function DroppedIntakeFile(e As DragEventArgs) As String
        If e Is Nothing OrElse Not e.Data.GetDataPresent(DataFormats.FileDrop) Then Return Nothing
        Dim paths As String() = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If paths Is Nothing Then Return Nothing
        For Each path As String In paths
            If PhoneConnectionIntake.IsSupportedFile(path) Then Return path
        Next
        Return Nothing
    End Function

    Private Sub OnOpenFile(sender As Object, e As EventArgs)
        If _busy Then Return
        Using dialog As New OpenFileDialog With {
            .Title = Localization.T("Код телефона"),
            .Filter = Localization.T("Файлы настроек и изображений QR (*.fmscfg;*.json;*.txt;*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.fmscfg;*.json;*.txt;*.png;*.jpg;*.jpeg;*.bmp;*.webp|Файлы настроек (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|Изображения с QR-кодом (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|Все файлы (*.*)|*.*"),
            .CheckFileExists = True}
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            OpenIntakeFile(dialog.FileName)
        End Using
    End Sub

    ''' <summary>One file in hand (opened or dropped): checked by the one reader, then relayed as a path
    ''' - the viewer reads it through its own bounded door, so a multi-line document never has to fit
    ''' the wake transport.</summary>
    Private Sub OpenIntakeFile(path As String)
        Dim read As FmsConfigReadResult = PhoneConnectionIntake.ReadFile(path)
        If Not read.Succeeded Then
            ShowFailure(read)
            Return
        End If
        RelayAsync(Function() _relay.RelayDocumentFile(path))
    End Sub

    Private Sub OnFromClipboard(sender As Object, e As EventArgs)
        If _busy Then Return
        Dim qr As QrDecodeResult = QrCodeDecoder.DecodeClipboard()
        If Not qr.Succeeded Then
            _status.Text = qr.FailureMessage
            Return
        End If
        TakeCode(qr.Text)
    End Sub

    Private Sub OnScanCamera(sender As Object, e As EventArgs)
        If _busy Then Return
        Using scan As New Camera_Scan_Form()
            If scan.ShowDialog(Me) <> DialogResult.OK OrElse String.IsNullOrWhiteSpace(scan.DecodedText) Then Return
            TakeCode(scan.DecodedText)
        End Using
    End Sub

    Private Sub OnRelay(sender As Object, e As EventArgs)
        If _busy Then Return
        TakeCode(_codeBox.Text)
    End Sub

    ''' <summary>A code in hand: checked by the one reader, then relayed; the door closes when a viewer
    ''' has taken it. The text lands in the field, never in a log.</summary>
    Private Sub TakeCode(code As String)
        Dim payload As String = If(code, "").Trim()
        If payload.Length = 0 Then
            _status.Text = Localization.T("Введите код, вставьте его из буфера или откройте файл или снимок.")
            Return
        End If
        _codeBox.Text = payload
        Dim read As FmsConfigReadResult = PhoneConnectionIntake.ReadText(payload)
        If Not read.Succeeded Then
            ShowFailure(read)
            Return
        End If
        RelayAsync(Function() _relay.RelayCode(payload))
    End Sub

    ''' <summary>The relay runs off the UI thread (its send loop waits up to seconds); the answer comes
    ''' back on the window's own context, where the clipboard fallback is allowed to run.</summary>
    Private Async Sub RelayAsync(relay As Func(Of PhoneCodeRelay.RelayOutcome))
        If _relay Is Nothing Then Return
        _busy = True
        SetDoorEnabled(False)
        _status.Text = Localization.T("Передача в Fast Media Sorter..")
        Dim outcome As PhoneCodeRelay.RelayOutcome = Await System.Threading.Tasks.Task.Run(relay)
        _busy = False
        SetDoorEnabled(True)
        Select Case outcome
            Case PhoneCodeRelay.RelayOutcome.Delivered
                _status.Text = Localization.T("Код передан: подтвердите добавление в окне Fast Media Sorter.")
                DialogResult = DialogResult.OK
                Close()
            Case PhoneCodeRelay.RelayOutcome.NoViewer
                ClipboardFallback(Localization.T("Fast Media Sorter не найден на этом компьютере. Код помещён в буфер обмена - установите программу и вставьте код через «Импорт из буфера» в панели ресурсов."))
            Case Else
                ClipboardFallback(Localization.T("Не удалось передать код: Fast Media Sorter старой версии или не ответил. Код помещён в буфер обмена - вставьте его через «Импорт из буфера» в панели ресурсов."))
        End Select
    End Sub

    Private Sub SetDoorEnabled(enabled As Boolean)
        _relayButton.Enabled = enabled
        _cameraButton.Enabled = enabled
        _fileButton.Enabled = enabled
        _clipboardButton.Enabled = enabled
        _codeBox.Enabled = enabled
    End Sub

    ''' <summary>The fallback when no viewer took the code: the privacy-marker clipboard door, on the
    ''' UI thread, and the one sentence that says where the code now is. The code never appears twice.</summary>
    Private Sub ClipboardFallback(message As String)
        If Not SecureClipboard.SetSecret(_codeBox.Text) Then
            _status.Text = Localization.T("Буфер обмена занят другой программой")
            Return
        End If
        _status.Text = message
    End Sub

    Private Sub ShowFailure(read As FmsConfigReadResult)
        If read IsNot Nothing AndAlso read.Detail IsNot Nothing AndAlso read.Detail.Length > 0 Then
            _status.Text = read.Detail
        ElseIf read IsNot Nothing AndAlso read.Failure = FmsConfigFailure.UpdateRequired Then
            _status.Text = Localization.T("Файл создан более новой версией программы. Обновите Fast Media Sorter")
        Else
            _status.Text = Localization.T("Документ не читается как код подключения или файл настроек ресурса")
        End If
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        MyBase.OnFormClosed(e)
        _relay = Nothing
    End Sub

    Public ReadOnly Property ThemesItself As Boolean Implements IThemeAware.ThemesItself
        Get
            Return False
        End Get
    End Property

    Public Sub ApplyTheme(palette As ThemePalette) Implements IThemeAware.ApplyTheme
        _status.ForeColor = ThemePalette.ReadableText(palette.TextPrimary, palette.SurfaceWindow)
    End Sub

End Class
