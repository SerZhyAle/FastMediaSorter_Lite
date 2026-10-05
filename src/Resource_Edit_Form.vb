#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

''' <summary>
''' Adds or edits one resource by hand (007 §3.2): an SFTP server or a network share that wants a login.
''' Built in code from 96-dpi positions; DialogLayout scales it and reflows a wrapped caption (the viewer has
''' been PerMonitorV2 since the chrome migration - 124 UX-202/UX-203).
'''
''' What it will not do, by design:
''' - Hold a password longer than it takes to hand it to the caller. An existing resource shows no password
'''   at all; leaving the field empty keeps the stored one (<see cref="NewPassword"/> is Nothing).
''' - Quietly re-pin a changed host key. The fingerprint is shown, and "Forget the key" is a deliberate
'''   button in THIS dialog - the only way back after a changed key - that asks the next connection to be
'''   trusted again, with the new fingerprint on screen (007 §3.3).
''' </summary>
Friend NotInheritable Class Resource_Edit_Form
    Inherits Form

    Private Const LabelX As Integer = 14
    Private Const FieldX As Integer = 160
    Private Const FieldWidth As Integer = 286

    Private ReadOnly source As ResourceDescriptor
    Private ReadOnly isNew As Boolean
    Private ReadOnly kind As ComboBox
    Private ReadOnly nameBox As TextBox
    Private ReadOnly hostLabel As Label
    Private ReadOnly hostBox As TextBox
    Private ReadOnly portLabel As Label
    Private ReadOnly portBox As TextBox
    Private ReadOnly userBox As TextBox
    Private ReadOnly passwordBox As TextBox
    Private ReadOnly passwordHint As Label
    Private ReadOnly revealBox As CheckBox
    Private ReadOnly folderLabel As Label
    Private ReadOnly folderBox As TextBox
    Private ReadOnly readOnlyBox As CheckBox
    Private ReadOnly keyLabel As Label
    Private ReadOnly keyValue As Label
    Private ReadOnly forgetKey As Button
    Private ReadOnly problem As Label
    Private ReadOnly okButton As Button
    Private ReadOnly dismissButton As Button
    Private script_Font As Font
    Private pin As String

    ''' <summary>The resource as edited (a new Id for a new one), without a credential.</summary>
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property Result As ResourceDescriptor

    ''' <summary>The password typed, or Nothing to keep what is stored. Never an empty string.</summary>
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property NewPassword As String

    ''' <param name="existing">The resource to change, or Nothing to add one.</param>
    ''' <param name="draft">For a new one, the values to start from (an address typed in the folder box).</param>
    Friend Sub New(existing As ResourceDescriptor, Optional draft As ResourceDescriptor = Nothing)
        source = existing
        isNew = existing Is Nothing
        Dim start As ResourceDescriptor = If(existing, If(draft, New ResourceDescriptor With {.Kind = ResourceKind.Sftp, .Port = 22, .RootPath = "/"}))
        pin = start.PinnedFingerprint

        Me.Text = If(isNew, Localization.T("Новый ресурс"), Localization.T("Изменить ресурс"))
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(460, 404)

        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then
            script_Font = New Font(family, Me.Font.Size)
            Me.Font = script_Font
        End If
        If Localization.IsRightToLeft() Then Me.RightToLeft = RightToLeft.Yes

        Dim y As Integer = 14
        kind = New ComboBox With {.Name = "res_Kind", .DropDownStyle = ComboBoxStyle.DropDownList,
                                  .Location = New Point(FieldX, y), .Size = New Size(FieldWidth, 24)}
        kind.Items.Add(ResourceMessages.KindLabel(ResourceKind.Sftp))
        kind.Items.Add(ResourceMessages.KindLabel(ResourceKind.Smb))
        kind.SelectedIndex = If(start.Kind = ResourceKind.Smb OrElse start.Kind = ResourceKind.Unc, 1, 0)
        ' A saved resource cannot change what it is: its credential and its pinned key belong to one kind.
        kind.Enabled = isNew
        AddRow("res_KindLabel", Localization.T("Тип:"), y)

        y += 30
        nameBox = New TextBox With {.Name = "res_Name", .Location = New Point(FieldX, y), .Size = New Size(FieldWidth, 24), .Text = start.DisplayName}
        AddRow("res_NameLabel", Localization.T("Название:"), y)

        y += 30
        hostLabel = AddRow("res_HostLabel", Localization.T("Сервер:"), y)
        hostBox = New TextBox With {.Name = "res_Host", .Location = New Point(FieldX, y), .Size = New Size(FieldWidth, 24)}

        y += 30
        portLabel = AddRow("res_PortLabel", Localization.T("Порт:"), y)
        portBox = New TextBox With {.Name = "res_Port", .Location = New Point(FieldX, y), .Size = New Size(70, 24),
                                    .Text = If(start.Port > 0, start.Port.ToString(CultureInfo.InvariantCulture), "22")}

        y += 30
        userBox = New TextBox With {.Name = "res_User", .Location = New Point(FieldX, y), .Size = New Size(FieldWidth, 24), .Text = start.UserName}
        AddRow("res_UserLabel", Localization.T("Пользователь:"), y)

        y += 30
        passwordBox = New TextBox With {.Name = "res_Password", .Location = New Point(FieldX, y), .Size = New Size(FieldWidth, 24), .UseSystemPasswordChar = True}
        AddRow("res_PasswordLabel", Localization.T("Пароль:"), y)

        y += 26
        passwordHint = New Label With {.Name = "res_PasswordHint", .AutoSize = False, .Location = New Point(FieldX, y), .Size = New Size(FieldWidth, 16),
                                       .ForeColor = SystemColors.GrayText,
                                       .Text = If(isNew, "", Localization.T("Пусто - оставить сохранённый пароль"))}

        ' The same deliberate reveal the password window of a resource has: off at the start (124 UX-203).
        y += 20
        revealBox = New CheckBox With {.Name = "res_Reveal", .AutoSize = True, .Location = New Point(FieldX, y), .Text = Localization.T("Показать пароль")}
        AddHandler revealBox.CheckedChanged, Sub() passwordBox.UseSystemPasswordChar = Not revealBox.Checked

        y += 24
        folderLabel = AddRow("res_FolderLabel", Localization.T("Папка на сервере:"), y)
        folderBox = New TextBox With {.Name = "res_Folder", .Location = New Point(FieldX, y), .Size = New Size(FieldWidth, 24)}

        y += 30
        readOnlyBox = New CheckBox With {.Name = "res_ReadOnly", .AutoSize = True, .Location = New Point(FieldX, y),
                                         .Checked = start.ReadOnlyHint, .Text = Localization.T("Только чтение (ничего не менять на сервере)")}

        y += 30
        keyLabel = AddRow("res_KeyLabel", Localization.T("Ключ сервера:"), y)
        keyValue = New Label With {.Name = "res_KeyValue", .AutoSize = False, .Location = New Point(FieldX, y - 2), .Size = New Size(FieldWidth, 30)}
        y += 34
        forgetKey = New Button With {.Name = "res_ForgetKey", .Location = New Point(FieldX, y - 4), .Size = New Size(FieldWidth, 26),
                                     .Text = Localization.T("Забыть ключ сервера..")}
        AddHandler forgetKey.Click, AddressOf OnForgetKey

        problem = New Label With {.Name = "res_Problem", .AutoSize = False, .Location = New Point(LabelX, y + 26), .Size = New Size(432, 30), .ForeColor = Color.Firebrick}

        okButton = New Button With {.Name = "res_Ok", .Location = New Point(264, y + 60), .Size = New Size(86, 28), .Text = Localization.T("Сохранить")}
        AddHandler okButton.Click, AddressOf Confirm
        dismissButton = New Button With {.Name = "res_Cancel", .Location = New Point(358, y + 60), .Size = New Size(88, 28),
                                         .Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel}

        For Each c As Control In New Control() {kind, nameBox, hostBox, portBox, userBox, passwordBox, passwordHint, revealBox, folderBox, readOnlyBox, keyValue, forgetKey, problem, okButton, dismissButton}
            Me.Controls.Add(c)
        Next
        Me.AcceptButton = okButton
        Me.CancelButton = dismissButton
        Me.ClientSize = New Size(460, y + 98)

        If start.Kind = ResourceKind.Smb OrElse start.Kind = ResourceKind.Unc Then
            hostBox.Text = start.RootPath
            folderBox.Text = ""
        Else
            hostBox.Text = start.Host
            folderBox.Text = If(String.IsNullOrEmpty(start.RootPath), "/", start.RootPath)
        End If
        AddHandler kind.SelectedIndexChanged, Sub() ApplyKind()
        ApplyKind()

        ' LAST line of the builder: scaling touches only the children that exist by now.
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DialogLayout.FitContent(Me)
    End Sub

    Private Function AddRow(controlName As String, text As String, y As Integer) As Label
        Dim label As New Label With {.Name = controlName, .AutoSize = False, .Location = New Point(LabelX, y + 4), .Size = New Size(FieldX - LabelX - 6, 18), .Text = text}
        Me.Controls.Add(label)
        Return label
    End Function

    Private ReadOnly Property IsSmb As Boolean
        Get
            Return kind.SelectedIndex = 1
        End Get
    End Property

    ''' <summary>One field set serves both kinds: an SFTP server is host + port + folder, a share is one
    ''' network path. Which rows show follows the kind; nothing else about the dialog changes.</summary>
    Private Sub ApplyKind()
        Dim smb As Boolean = IsSmb
        hostLabel.Text = If(smb, Localization.T("Сетевая папка:"), Localization.T("Сервер:"))
        portLabel.Visible = Not smb
        portBox.Visible = Not smb
        folderLabel.Visible = Not smb
        folderBox.Visible = Not smb
        keyLabel.Visible = Not smb
        keyValue.Visible = Not smb
        forgetKey.Visible = Not smb AndAlso pin.Length > 0
        If Not smb Then ShowKey()
        problem.Text = ""
    End Sub

    Private Sub ShowKey()
        keyValue.Text = If(pin.Length > 0, pin, Localization.T("ещё не запомнен: появится при первом подключении"))
        forgetKey.Visible = pin.Length > 0
    End Sub

    ''' <summary>The deliberate re-pin: not the default button, not a dismissable dialog, and it only
    ''' clears the pin - the next connection shows the new fingerprint and asks again.</summary>
    Private Sub OnForgetKey(sender As Object, e As EventArgs)
        Dim answer As DialogResult = ViewerBox.Show(Me,
            Localization.TF("Сейчас запомнен ключ:{0}{1}{0}{0}Если сервер переустановили, это нормально. Если нет - кто-то может выдавать себя за него. Забыть ключ и доверять новому при следующем подключении?", vbLf, pin),
            Localization.T("Забыть ключ сервера"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
        If answer <> DialogResult.Yes Then Return
        pin = ""
        ShowKey()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then script_Font?.Dispose()
    End Sub

    Private Sub Fail(message As String, target As Control)
        problem.Text = message
        DialogLayout.FitLabel(problem)
        target.Focus()
    End Sub

    Private Sub Confirm(sender As Object, e As EventArgs)
        Dim user As String = userBox.Text.Trim()
        Dim typed As String = passwordBox.Text
        Dim built As New ResourceDescriptor With {
            .Id = If(source IsNot Nothing, source.Id, ResourceImport.NewResourceId()),
            .SecretHandle = If(source IsNot Nothing, source.SecretHandle, ""),
            .ReadOnlyHint = readOnlyBox.Checked,
            .UserName = user,
            .PinnedFingerprint = pin,
            .IncludeSubfolders = If(source IsNot Nothing, source.IncludeSubfolders, Nothing),
            .Endpoints = If(source IsNot Nothing, source.Endpoints, Nothing),
            .LastUsedUtc = If(source IsNot Nothing, source.LastUsedUtc, Nothing)}
        If source IsNot Nothing Then built.AdditionalFields = source.AdditionalFields

        If IsSmb Then
            Dim unc As String = hostBox.Text.Trim()
            If String.IsNullOrEmpty(SmbPathUtil.ExtractUncRoot(unc)) Then
                Fail(Localization.T("Укажите путь вида \\сервер\папка"), hostBox)
                Return
            End If
            built.Kind = ResourceKind.Smb
            built.RootPath = unc
            built.Host = ""
            built.Port = 0
            built.Endpoints = Nothing
            built.PinnedFingerprint = ""
        Else
            Dim host As String = hostBox.Text.Trim()
            If host.Length = 0 OrElse host.IndexOfAny({" "c, "/"c, "\"c, "@"c}) >= 0 Then
                Fail(Localization.T("Укажите адрес сервера (имя или IP)"), hostBox)
                Return
            End If
            Dim port As Integer
            If Not Integer.TryParse(portBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, port) OrElse port < 1 OrElse port > 65535 Then
                Fail(Localization.T("Порт - число от 1 до 65535"), portBox)
                Return
            End If
            Dim folder As String = folderBox.Text.Trim().Replace("\"c, "/"c)
            If folder.Length = 0 Then folder = "/"
            If Not folder.StartsWith("/", StringComparison.Ordinal) Then folder = "/" & folder
            If FmsConfigReader.CanonicalVirtualPath(folder) Is Nothing Then
                Fail(Localization.T("Папка на сервере указана неверно"), folderBox)
                Return
            End If
            built.Kind = ResourceKind.Sftp
            built.Host = host
            built.Port = port
            built.RootPath = FmsConfigReader.CanonicalVirtualPath(folder)
            ' A different address is a different server until the user says otherwise: the old
            ' alternates would send the credential somewhere the user did not choose.
            If source IsNot Nothing AndAlso (Not String.Equals(source.Host, host, StringComparison.OrdinalIgnoreCase) OrElse source.Port <> port) Then
                built.Endpoints = Nothing
            End If
        End If

        If user.Length = 0 Then
            Fail(Localization.T("Укажите имя пользователя"), userBox)
            Return
        End If
        If typed.Length = 0 AndAlso (isNew OrElse String.IsNullOrEmpty(built.SecretHandle)) Then
            Fail(Localization.T("Введите пароль"), passwordBox)
            Return
        End If
        built.DisplayName = If(nameBox.Text.Trim().Length > 0, nameBox.Text.Trim(),
                               If(IsSmb, built.RootPath, built.Host))

        Result = built
        NewPassword = If(typed.Length = 0, Nothing, typed)
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

End Class
#End If
