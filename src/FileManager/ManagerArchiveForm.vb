#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

Friend NotInheritable Class ManagerArchiveForm
    Inherits Form

    Private ReadOnly nameBox As New TextBox With {.Dock = DockStyle.Fill, .RightToLeft = RightToLeft.No}
    Private ReadOnly deleteBox As New CheckBox With {.Name = "deleteBox", .Checked = False}
    ''' <summary>The delete-originals option as a BOOLEAN-OPTION row (ticket L0325 D-6). The window stacks its controls in a
    ''' docked table, which <see cref="DialogLayout.FitContent"/> does not measure, so the row is arranged in OnLoad and the
    ''' window grows to the table's need.</summary>
    Private deleteRow As OptionRowHost
    Private stack As TableLayoutPanel
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property Destination As ManagerLocation
    Friend ReadOnly Property ArchiveName As String
        Get
            Return nameBox.Text
        End Get
    End Property
    Friend ReadOnly Property DeleteOriginals As Boolean
        Get
            Return deleteBox.Checked
        End Get
    End Property

    Friend Sub New(current As ManagerLocation, choose As Func(Of ManagerLocation), Optional canDelete As Boolean = True)
        Destination = current
        Text = Localization.T("Создать ZIP")
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(620, 260)
        MinimumSize = New Size(480, 260)
        MinimizeBox = False
        MaximizeBox = False
        stack = New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .ColumnCount = 1, .Padding = New Padding(12)}
        Dim address As New Label With {.AutoSize = True, .Dock = DockStyle.Fill, .Text = current.DirectoryPath}
        Dim browse As New Button With {.Text = Localization.T("Папка назначения"), .AutoSize = True}
        AddHandler browse.Click, Sub()
                                     Dim location As ManagerLocation = choose()
                                     If location IsNot Nothing Then Destination = location : address.Text = location.DirectoryPath
                                 End Sub
        nameBox.Text = "Archive-" & DateTime.Now.ToString("yyyyMMdd-HHmmss", Globalization.CultureInfo.InvariantCulture) & ".zip"
        deleteBox.Text = Localization.T("Удалить оригиналы после проверки архива")
        deleteBox.Enabled = canDelete
        deleteRow = DialogOptionRow.Create(deleteBox, Nothing, New Rectangle(0, 0, 160, DialogOptionRow.RowHeightLogical),
                                           (UiSizing.PointerTarget - UiSizing.PointerCheckBox) \ 2)
        deleteRow.Dock = DockStyle.Fill
        deleteRow.Margin = Padding.Empty
        Dim warning As New Label With {.AutoSize = True, .Dock = DockStyle.Fill}
        Dim buttons As New DialogButtonRow With {.AutoSize = True, .Dock = DockStyle.Fill}
        Dim cancel As New Button With {.Text = Localization.T("Отмена"), .AutoSize = True, .DialogResult = DialogResult.Cancel}
        Dim accept As New Button With {.Text = Localization.T("Создать ZIP"), .AutoSize = True}
        AddHandler accept.Click, Sub()
                                     If Not ManagerCommandPlanner.ValidLeaf(Destination.Place.Kind, nameBox.Text) OrElse
                                        Not nameBox.Text.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) Then
                                         warning.Text = ManagerCommandPlanner.ReasonText(ManagerFailure.InvalidName)
                                         Return
                                     End If
                                     DialogResult = DialogResult.OK
                                 End Sub
        buttons.Add(accept, DialogButtonRole.Affirmative)
        buttons.Add(cancel, DialogButtonRole.Dismissing, "action.cancel")
        buttons.Bind(Me)
        stack.Controls.Add(browse)
        stack.Controls.Add(address)
        stack.Controls.Add(New Label With {.Text = Localization.T("Введите имя:"), .AutoSize = True})
        stack.Controls.Add(nameBox)
        stack.Controls.Add(deleteRow)
        stack.Controls.Add(warning)
        stack.Controls.Add(buttons)
        Controls.Add(stack)
        AcceptButton = accept
        CancelButton = cancel
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DialogLayout.FitContent(Me)
        stack.PerformLayout()
        deleteRow.Arrange()
        Dim need As Integer = stack.GetPreferredSize(New Size(ClientSize.Width, 0)).Height
        If need > ClientSize.Height Then ClientSize = New Size(ClientSize.Width, need)
    End Sub
End Class
#End If
