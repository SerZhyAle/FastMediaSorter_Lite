Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Picks what "through Google Drive" means for one side of an operation (ticket 269 P4, 231 rule 3): as a source, what other devices send
''' to this PC or a resource of another device; as a destination, any own device or one of them. The rows come from the listing the
''' channel already holds: asking the list makes no request.
''' </summary>
Friend NotInheritable Class Scheduled_Drive_Picker_Form
    Inherits Form

    Private ReadOnly combo As ComboBox
    Private ReadOnly btnOk As Button
    Private ReadOnly btnCancel As Button
    Private ReadOnly _answerRow As DialogButtonRow
    Private ReadOnly choices As List(Of ScheduledDriveChoice)
    Private _iconHandle As IntPtr

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property Chosen As ScheduledDriveChoice

    Friend Sub New(current As ScheduledLocation, isSource As Boolean)
        Dim directory As ExchangeDirectorySnapshot = DriveChannelHosts.ListedDirectory()
        choices = If(isSource, ScheduledDriveChoice.ForSource(directory), ScheduledDriveChoice.ForDestination(directory))
        ' A choice made earlier stays on the list while its device is not listed: opening the dialog does not lose it.
        Dim selected As Integer = choices.FindIndex(Function(c As ScheduledDriveChoice) SameChoice(c, current))
        If selected < 0 AndAlso current.Kind = ScheduledLocationKind.Drive AndAlso current.ResourceId.Length > 0 Then
            choices.Add(New ScheduledDriveChoice(current.ResourceId, current.DriveDeviceName, current.DriveResourceId, current.ResourceName))
            selected = choices.Count - 1
        End If

        SuspendLayout()
        UiLanguage.ApplyTo(Me)
        Text = Localization.T("Через Google Drive")
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
            .Padding = New Padding(14, 12, 14, 2)}
        table.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        Dim caption As String = If(isSource, Localization.T("Источник"), Localization.T("Назначение"))
        table.Controls.Add(New Label With {.AutoSize = True, .Text = caption, .Margin = New Padding(0, 0, 0, 2)})
        combo = New RowFittingComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 380, .Name = "cmbSchedDrive",
            .Margin = New Padding(0, 0, 0, 8), .AccessibleName = caption}
        For Each choice As ScheduledDriveChoice In choices
            combo.Items.Add(If(choice.Label.Length > 0, choice.Label, If(isSource, Localization.T("Что другие устройства присылают на этот ПК"), Localization.T("Любое из моих устройств"))))
        Next
        combo.SelectedIndex = Math.Max(selected, 0)
        table.Controls.Add(combo)

        table.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(460, 0), .Margin = New Padding(0, 0, 0, 10),
            .ForeColor = ThemeService.Palette.TextMuted,
            .Text = If(isSource,
                       Localization.T("Файлы приходят во временную папку этого ПК и попадают в назначение с базовыми именами, без вложенных папок."),
                       Localization.T("Файлы отправляются пакетами через ваш Google Drive и попадают на устройство, когда оно их заберёт. Вложенные папки не сохраняются."))})

        btnCancel = New Button With {.Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel, .Name = "btnSchedDriveCancel"}
        btnOk = New Button With {.Text = Localization.T("Выбрать"), .Name = "btnSchedDriveOk"}
        AddHandler btnOk.Click, AddressOf OnOkClicked
        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "btnSchedDriveButtons", .Padding = New Padding(14, 4, 14, 10)}
        DpiLayout.AddAnswer(_answerRow, btnOk, DialogButtonRole.Affirmative, "action.confirm")
        DpiLayout.AddAnswer(_answerRow, btnCancel, DialogButtonRole.Dismissing, "action.cancel")

        table.Dock = DockStyle.Fill
        Controls.Add(table)
        Controls.Add(_answerRow)
        _answerRow.Bind(Me)
        ResumeLayout(True)
        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    Private Shared Function SameChoice(choice As ScheduledDriveChoice, current As ScheduledLocation) As Boolean
        If current.Kind <> ScheduledLocationKind.Drive Then Return False
        Return String.Equals(choice.DeviceId, current.ResourceId, StringComparison.Ordinal) AndAlso
            String.Equals(choice.ResourceId, current.DriveResourceId, StringComparison.Ordinal)
    End Function

    Private Sub OnOkClicked(sender As Object, e As EventArgs)
        If combo.SelectedIndex < 0 Then Return
        Chosen = choices(combo.SelectedIndex)
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
