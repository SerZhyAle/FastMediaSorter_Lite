#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms

''' <summary>
''' The export dialog (ticket 118 Ф-3): hands one registered SFTP resource to another computer
''' or a phone as the same .fmscfg document the Share Manager produces. The document is built
''' fresh for every action and lives in a local variable only - never in a control's Text/Tag,
''' never in a log beyond the outcome name. Every refusal names its reason (a disabled button
''' would hide it), and "saved" is said only for a verified, owner-only file.
''' </summary>
Friend NotInheritable Class Resource_Export_Form
    Inherits Form

    Private ReadOnly resource As ResourceDescriptor
    Private ReadOnly secrets As IResourceSecretStore
    Private ReadOnly noPasswordRadio As RadioButton
    Private ReadOnly withPasswordRadio As RadioButton
    Private ReadOnly noStoredPasswordHint As Label
    Private ReadOnly status As Label
    Private ReadOnly saveButton As Button
    Private ReadOnly copyTextButton As Button
    Private ReadOnly copyCodeButton As Button
    Private ReadOnly answerRow As DialogButtonRow
    Private script_Font As Font

    Friend Sub New(resource As ResourceDescriptor, secretStore As IResourceSecretStore)
        Me.resource = resource
        Me.secrets = secretStore
        Me.Text = Localization.T("Экспорт ресурса")
        Me.FormBorderStyle = FormBorderStyle.Sizable
        ' Tall enough for the content and one line of the answer row (it was 300 for a 28 px button strip).
        Me.MinimumSize = New Size(480, 312)
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        ' The authored content ends at 214 (the status line); the bottom row below it is 50 tall (8 + one 32 line + 10) at 96 dpi.
        Me.ClientSize = New Size(470, 264)

        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then
            script_Font = New Font(family, Me.Font.Size)
            Me.Font = script_Font
        End If
        If Localization.IsRightToLeft() Then Me.RightToLeft = RightToLeft.Yes

        Dim nameHeader As New Label With {
            .Name = "res_Export_Name", .AutoSize = False, .Location = New Point(12, 12), .Size = New Size(446, 22),
            .Text = resource.DisplayName, .Font = New Font(Me.Font, FontStyle.Bold),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right}
        Dim warning As New Label With {
            .Name = "res_Export_Warning", .AutoSize = False, .Location = New Point(12, 38), .Size = New Size(446, 44),
            .ForeColor = SystemColors.GrayText,
            .Text = Localization.T("Файл содержит ключ доступа к серверу. Передавайте его только владельцу и не оставляйте на общих дисках."),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right}

        noPasswordRadio = New RadioButton With {
            .Name = "res_ExportNoPassword", .Text = Localization.T("Без пароля"), .Checked = True,
            .Location = New Point(14, 88), .AutoSize = True}
        withPasswordRadio = New RadioButton With {
            .Name = "res_ExportWithPassword", .Text = Localization.T("С паролем"),
            .Location = New Point(14, 112), .AutoSize = True}
        noStoredPasswordHint = New Label With {
            .Name = "res_Export_NoPasswordHint", .AutoSize = False, .Location = New Point(34, 136), .Size = New Size(424, 22),
            .ForeColor = SystemColors.GrayText, .Visible = False,
            .Text = Localization.T("Пароль не сохранён - файл будет без пароля."),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right}
        ' "Without" is the default button (Q118-8): the receiver asks the user, and a password is
        ' never handed out because a radio happened to be ticked.
        Dim stored As CredentialReadResult = ResourceCommit.ReadCredential(resource, secrets)
        If stored.Outcome <> CredentialReadOutcome.Ok Then
            withPasswordRadio.Enabled = False
            noStoredPasswordHint.Visible = True
        End If

        ' Not anchored to the bottom (it was): the answer row below is docked and owns its height, which grows by a
        ' line when its buttons wrap, so a label that kept its distance from the bottom edge would end up under it.
        status = New Label With {
            .Name = "res_Export_Status", .AutoSize = False, .Location = New Point(12, 168), .Size = New Size(446, 46),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right}

        saveButton = MakeButton("res_ExportSave", Localization.T("Сохранить в файл.."), AddressOf OnSave)
        copyTextButton = MakeButton("res_ExportCopyText", Localization.T("Копировать текст"), AddressOf OnCopyText)
        copyCodeButton = MakeButton("res_ExportCopyCode", Localization.T("Копировать код"), AddressOf OnCopyCode)
        Dim closeButton As New Button With {
            .Name = "res_ExportClose", .Text = Localization.T("Закрыть"), .DialogResult = DialogResult.Cancel}

        ' The three actions are not the window's answer (it has none: Close is its one way out), so they are commands
        ' of the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4): they read first, in the order they
        ' had on the screen, set apart from Close, the one dismissing answer. Mono glyphs for the commands. Close is
        ' Escape's target and, being the only answer, what Enter selects when nothing else holds the focus - none of
        ' the three commands is ever the default (each one hands out the access key).
        answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "res_Export_Buttons", .Padding = New Padding(12, 8, 12, 10)}
        answerRow.Add(saveButton, DialogButtonRole.Command, "action.export")
        answerRow.Add(copyTextButton, DialogButtonRole.Command, "action.copy")
        answerRow.Add(copyCodeButton, DialogButtonRole.Command, "action.copy")
        answerRow.Add(closeButton, DialogButtonRole.Dismissing, "nav.close")

        Controls.AddRange(New Control() {nameHeader, warning, noPasswordRadio, withPasswordRadio,
                                         noStoredPasswordHint, status, answerRow})
        answerRow.Bind(Me)

        ' LAST line of the builder: scaling touches only the children that exist by now (124 UX-202).
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DialogLayout.FitContent(Me)
        DialogLayout.KeepRoomForRow(Me, answerRow)
    End Sub

    Private Function MakeButton(controlName As String, text As String, handler As EventHandler) As Button
        Dim b As New Button With {.Name = controlName, .Text = text}
        AddHandler b.Click, handler
        Return b
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then script_Font?.Dispose()
    End Sub

    ''' <summary>Builds the document from a snapshot taken at open. A resource another window
    ''' removed meanwhile still exports from what this dialog was opened with.</summary>
    Private Function BuildDocument() As FmsExportResult
        Dim result As FmsExportResult = FmsConfigExport.Export(resource, withPasswordRadio.Checked, secrets)
        If Not result.Succeeded Then
            If result.SelfCheckFailure <> FmsConfigFailure.None Then
                AppFileLogger.WriteLine("Resource: export self-check failed - " & result.SelfCheckFailure.ToString())
                Say(Localization.T("Не удалось подготовить файл настроек."))
            Else
                AppFileLogger.WriteLine("Resource: export refused - " & result.Refusal.ToString())
                Select Case result.Refusal
                    Case FmsExportRefusal.Smb : Say(Localization.T("Передать можно только сервер SFTP: файл настроек не описывает сетевую папку Windows."))
                    Case FmsExportRefusal.NotPinned : Say(Localization.T("Ключ сервера ещё не запомнен. Откройте ресурс один раз и подтвердите ключ - тогда его можно передать."))
                    Case FmsExportRefusal.UnsupportedProtection : Say(Localization.T("заблокировано: защита не поддерживается"))
                    Case FmsExportRefusal.NoAddress : Say(Localization.T("У ресурса нет адреса, который можно передать."))
                    Case Else : Say(Localization.T("Папка ресурса записана так, что файл настроек её не примет. Исправьте её через «Изменить..»."))
                End Select
            End If
            Return Nothing
        End If
        Return result
    End Function

    Private Sub OnSave(sender As Object, e As EventArgs)
        Dim document As FmsExportResult = BuildDocument()
        If document Is Nothing Then Return
        Using dialog As New SaveFileDialog With {
            .Title = Localization.T("Экспорт ресурса"),
            .FileName = DefaultFileName(),
            .Filter = Localization.T("Файл настроек ресурса (*.fmscfg)|*.fmscfg"),
            .DefaultExt = ".fmscfg"}
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim outcome As ExportWriteOutcome = ExportWriter.Publish(dialog.FileName, document.Json)
            AppFileLogger.WriteLine("Resource: export " & outcome.ToString())
            Select Case outcome
                Case ExportWriteOutcome.Saved
                    Say(Localization.TF("Сохранено: {0}", dialog.FileName))
                Case ExportWriteOutcome.RefusedNoAcls
                    Say(Localization.T("Здесь нельзя сохранить: диск (FAT/exFAT или сетевой) не умеет закрывать файл от других пользователей. Выберите папку на диске NTFS."))
                Case ExportWriteOutcome.RefusedUnsafeTarget
                    Say(Localization.T("Здесь нельзя сохранить: эту папку могут менять другие пользователи или она перенаправлена. Выберите свою папку."))
                Case Else
                    Say(Localization.T("Не удалось сохранить файл. Ничего не потеряно: прежний файл остался как был."))
            End Select
        End Using
    End Sub

    Private Sub OnCopyText(sender As Object, e As EventArgs)
        CopySecret(Function(document) document.Json)
    End Sub

    Private Sub OnCopyCode(sender As Object, e As EventArgs)
        CopySecret(Function(document) document.Code)
    End Sub

    Private Sub CopySecret(transport As Func(Of FmsExportResult, String))
        Dim document As FmsExportResult = BuildDocument()
        If document Is Nothing Then Return
        If SecretClipboard.SetSecret(transport(document)) Then
            AppFileLogger.WriteLine("Resource: export copied")
            Say(Localization.T("Скопировано. Это ключ доступа: вставьте его только туда, куда нужно."))
        Else
            AppFileLogger.WriteLine("Resource: export clipboard failed")
            Say(Localization.T("Буфер обмена занят другой программой. Повторите."))
        End If
    End Sub

    ''' <summary>The resource's name, made safe for a file name and bounded - a long display
    ''' name must not push the save dialog's own text past what Windows shows.</summary>
    Private Function DefaultFileName() As String
        Dim safe As New String(resource.DisplayName.Select(
            Function(c) If(IO.Path.GetInvalidFileNameChars().Contains(c), "_"c, c)).ToArray())
        safe = safe.Trim()
        If safe.Length > 80 Then safe = safe.Substring(0, 80)
        If safe.Length = 0 Then safe = "resource"
        Return safe
    End Function

    Private Sub Say(text As String)
        status.Text = text
        DialogLayout.FitLabel(status)
    End Sub

End Class
#End If
