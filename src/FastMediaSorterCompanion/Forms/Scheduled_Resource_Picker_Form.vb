Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>
''' Picks one registered resource of the viewer's list and a folder inside it (ticket 222, 4.3). The list is
''' READ here - adding, changing and re-trusting a resource is the viewer's resource manager - and a run asks
''' nothing: a password that is not stored, or a host key that is not pinned, stops the operation. That is
''' said on this dialog, so nobody is surprised by it at three in the morning.
''' </summary>
Friend NotInheritable Class Scheduled_Resource_Picker_Form
    Inherits Form

    Private combo As ComboBox
    Private txtFolder As TextBox
    Private btnOk As Button
    Private btnCancel As Button
    Private ReadOnly resources As List(Of ResourceDescriptor)
    Private _iconHandle As IntPtr

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property ChosenId As String = ""
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property ChosenName As String = ""
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property ChosenFolder As String = ""

    Friend Sub New(current As ScheduledLocation)
        resources = ScheduledResourceRegistry.Offered()
        SuspendLayout()
        UiLanguage.ApplyTo(Me)
        Text = Localization.T("Выбор ресурса")
        Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler FormClosed, Sub() ShareIcons.FreeIcon(Icon, _iconHandle)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim table As New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 1,
            .Padding = New Padding(14, 12, 14, 10)}
        table.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        If resources.Count = 0 Then
            table.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(460, 0), .Margin = New Padding(0, 0, 0, 10),
                .Text = Localization.T("В списке ресурсов нет подходящих ресурсов. Добавьте SFTP-сервер или сетевую папку в просмотрщике.")})
        Else
            table.Controls.Add(New Label With {.AutoSize = True, .Text = Localization.T("Ресурс"), .Margin = New Padding(0, 0, 0, 2)})
            combo = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 380, .Name = "cmbSchedResource",
                .Margin = New Padding(0, 0, 0, 8), .AccessibleName = Localization.T("Ресурс")}
            For Each descriptor As ResourceDescriptor In resources
                combo.Items.Add(descriptor.DisplayName)
            Next
            Dim index As Integer = resources.FindIndex(Function(r As ResourceDescriptor) String.Equals(r.Id, current.ResourceId, StringComparison.OrdinalIgnoreCase))
            combo.SelectedIndex = If(index >= 0, index, 0)
            AddHandler combo.SelectedIndexChanged, Sub() txtFolder.Text = resources(combo.SelectedIndex).RootPath
            table.Controls.Add(combo)

            table.Controls.Add(New Label With {.AutoSize = True, .Text = Localization.T("Папка внутри ресурса"), .Margin = New Padding(0, 0, 0, 2)})
            txtFolder = New TextBox With {.Width = 380, .Name = "txtSchedResourceFolder", .Margin = New Padding(0, 0, 0, 8),
                .AccessibleName = Localization.T("Папка внутри ресурса")}
            txtFolder.Text = If(index >= 0 AndAlso current.Path.Length > 0, current.Path, resources(combo.SelectedIndex).RootPath)
            table.Controls.Add(txtFolder)
        End If
        table.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(460, 0), .Margin = New Padding(0, 0, 0, 10),
            .ForeColor = ThemeService.Palette.TextMuted,
            .Text = Localization.T("Пароль и ключ сервера берутся из списка ресурсов просмотрщика. Если пароль не сохранён или ключ не подтверждён, операция остановится - без вопросов.")})

        Dim footer As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.RightToLeft,
            .Dock = DockStyle.Fill, .Margin = New Padding(0)}
        btnCancel = New Button With {.Text = Localization.T("Отмена"), .AutoSize = True, .DialogResult = DialogResult.Cancel, .Name = "btnSchedResourceCancel",
            .Padding = New Padding(14, 5, 14, 5)}
        btnOk = New Button With {.Text = Localization.T("Выбрать"), .AutoSize = True, .Name = "btnSchedResourceOk", .Padding = New Padding(14, 5, 14, 5),
            .Enabled = resources.Count > 0}
        ButtonGlyphs.Apply(btnOk, "action.confirm", ButtonGlyphRole.Affirmative)
        ButtonGlyphs.Apply(btnCancel, "action.cancel", ButtonGlyphRole.Dismissing)
        AddHandler btnOk.Click, AddressOf OnOkClicked
        footer.Controls.AddRange({btnCancel, btnOk})
        table.Controls.Add(footer)

        Controls.Add(table)
        AcceptButton = btnOk
        CancelButton = btnCancel
        ResumeLayout(True)
        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    Private Sub OnOkClicked(sender As Object, e As EventArgs)
        If combo Is Nothing OrElse combo.SelectedIndex < 0 Then Return
        Dim descriptor As ResourceDescriptor = resources(combo.SelectedIndex)
        ChosenId = descriptor.Id
        ChosenName = descriptor.DisplayName
        ChosenFolder = txtFolder.Text.Trim()
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
