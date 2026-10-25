#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Local/configured destinations; connecting and policy validation remain at the queue boundary.</summary>
Friend NotInheritable Class ManagerDestinationForm
    Inherits Form
    Private ReadOnly places As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill}
    Private ReadOnly address As New TextBox With {.Dock = DockStyle.Fill, .RightToLeft = RightToLeft.No}
    Private ReadOnly warning As New Label With {.AutoSize = True, .Dock = DockStyle.Fill}
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property SelectedLocation As ManagerLocation

    Friend Sub New(available As IEnumerable(Of ManagerPlace), current As ManagerLocation)
        Text = Localization.T("Папка назначения")
        StartPosition = FormStartPosition.CenterParent
        MinimumSize = New Size(480, 230)
        ClientSize = New Size(620, 240)
        FormBorderStyle = FormBorderStyle.Sizable
        MinimizeBox = False
        MaximizeBox = False
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 6, .Padding = New Padding(12), .AutoSize = True}
        layout.Controls.Add(New Label With {.Text = Localization.T("Места"), .AutoSize = True})
        layout.Controls.Add(places)
        layout.Controls.Add(New Label With {.Text = Localization.T("Папка назначения"), .AutoSize = True})
        layout.Controls.Add(address)
        layout.Controls.Add(warning)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .FlowDirection = FlowDirection.RightToLeft}
        Dim cancel As New Button With {.Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel, .AutoSize = True}
        Dim accept As New Button With {.Text = Localization.T("Выбрать"), .AutoSize = True}
        Dim browse As New Button With {.Text = Localization.T("Обзор.."), .AutoSize = True}
        AddHandler browse.Click, Sub()
                                     Using picker As New FolderBrowserDialog()
                                         If picker.ShowDialog(Me) = DialogResult.OK Then
                                             Dim resolved As ManagerAddressResult = ManagerAddress.Resolve(picker.SelectedPath, New List(Of ResourceDescriptor)())
                                             If resolved.Kind = ManagerAddressKind.Ok Then
                                                 For Each item As ManagerPlace In places.Items
                                                     If item.Id = resolved.Location.Place.Id Then places.SelectedItem = item : Exit For
                                                 Next
                                                 address.Text = picker.SelectedPath
                                             End If
                                         End If
                                     End Using
                                 End Sub
        AddHandler accept.Click, Sub()
                                     Dim place As ManagerPlace = TryCast(places.SelectedItem, ManagerPlace)
                                     If place Is Nothing Then Return
                                     Dim normalized As String = ManagerPathRules.Normalize(place.Kind, address.Text)
                                     If normalized Is Nothing OrElse Not ManagerPathRules.Contains(place.Kind, place.RootPath, normalized) Then
                                         warning.Text = ManagerCommandPlanner.ReasonText(ManagerFailure.LinkOrRoot)
                                         Return
                                     End If
                                     SelectedLocation = New ManagerLocation(place, normalized)
                                     DialogResult = DialogResult.OK
                                 End Sub
        AddHandler places.SelectedIndexChanged, Sub()
                                                   Dim place As ManagerPlace = TryCast(places.SelectedItem, ManagerPlace)
                                                   If place IsNot Nothing Then address.Text = place.RootPath
                                               End Sub
        For Each place As ManagerPlace In available
            places.Items.Add(place)
            If current IsNot Nothing AndAlso place.Id = current.Place.Id Then places.SelectedItem = place
        Next
        If places.SelectedIndex < 0 AndAlso places.Items.Count > 0 Then places.SelectedIndex = 0
        If current IsNot Nothing AndAlso TryCast(places.SelectedItem, ManagerPlace)?.Id = current.Place.Id Then address.Text = current.DirectoryPath
        buttons.Controls.Add(cancel)
        buttons.Controls.Add(accept)
        buttons.Controls.Add(browse)
        layout.Controls.Add(buttons)
        Controls.Add(layout)
        AcceptButton = accept
        CancelButton = cancel
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DialogLayout.FitContent(Me)
    End Sub
End Class
#End If
