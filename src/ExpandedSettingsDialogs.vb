#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms

''' <summary>
''' "Show these file types" - §3.4. A compact dialog rather than a row on the settings
''' page, because the answer is thirty-odd extensions in four groups and the row it
''' replaces had become a semicolon-separated text field nobody could check by eye.
'''
''' The result is deliberately EMPTY when everything is ticked: §3.4 defines an empty list
''' as "all supported formats", not as "nothing", and writing out the full list instead
''' would freeze today's formats into the profile - a format added by a later version
''' would then be filtered out of a profile that never asked to filter anything.
''' </summary>
Public NotInheritable Class File_Types_Form
    Inherits Form

    Private ReadOnly tree As New TreeView()
    Private ReadOnly btnOk As New Button()
    Private ReadOnly btnCancel As New Button()
    Private ReadOnly groups As List(Of KeyValuePair(Of String, String()))
    Private suspendCascade As Boolean

    ''' <summary>The chosen extensions, lowercase and dotted; empty means all supported.</summary>
    Public ReadOnly Property Selection As List(Of String)
        Get
            Dim chosen As New List(Of String)()
            Dim total As Integer = 0
            For Each group As TreeNode In tree.Nodes
                For Each leaf As TreeNode In group.Nodes
                    total += 1
                    If leaf.Checked Then chosen.Add(Convert.ToString(leaf.Tag))
                Next
            Next
            If chosen.Count = total Then Return New List(Of String)()
            Return chosen
        End Get
    End Property

    Public Sub New(groups As List(Of KeyValuePair(Of String, String())), current As IEnumerable(Of String))
        Me.groups = groups
        Build()
        Fill(current)
    End Sub

    Private Sub Build()
        Text = Localization.T("Типы файлов")
        FormBorderStyle = FormBorderStyle.Sizable
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(Localization.Scaled(420), 520)
        MinimumSize = New Size(Localization.Scaled(360), 380)
        Font = New Font(Localization.FontFamily(), 9.0F)
        If Localization.IsRightToLeft() Then RightToLeft = RightToLeft.Yes

        tree.CheckBoxes = True
        tree.Bounds = New Rectangle(12, 12, ClientSize.Width - 24, ClientSize.Height - 60)
        tree.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        AddHandler tree.AfterCheck, AddressOf NodeChecked
        Controls.Add(tree)

        btnOk.Text = Localization.T("ОК")
        btnOk.Bounds = New Rectangle(ClientSize.Width - 24 - 210, ClientSize.Height - 38, 105, 28)
        btnOk.Anchor = AnchorStyles.Right Or AnchorStyles.Bottom
        btnOk.DialogResult = DialogResult.OK
        Controls.Add(btnOk)

        btnCancel.Text = Localization.T("Отмена")
        btnCancel.Bounds = New Rectangle(ClientSize.Width - 12 - 105, ClientSize.Height - 38, 105, 28)
        btnCancel.Anchor = AnchorStyles.Right Or AnchorStyles.Bottom
        btnCancel.DialogResult = DialogResult.Cancel
        Controls.Add(btnCancel)

        AcceptButton = btnOk
        CancelButton = btnCancel
    End Sub

    Private Sub Fill(current As IEnumerable(Of String))
        Dim allowed As New HashSet(Of String)(If(current, Enumerable.Empty(Of String)()), StringComparer.OrdinalIgnoreCase)
        ' An empty stored list is the "everything" sentinel, so it must arrive here as
        ' every box ticked - not as an empty dialog.
        Dim everything As Boolean = allowed.Count = 0

        suspendCascade = True
        For Each group As KeyValuePair(Of String, String()) In groups
            Dim parent As New TreeNode(Localization.T(group.Key))
            For Each extension As String In group.Value
                Dim leaf As New TreeNode(extension) With {.Tag = extension}
                leaf.Checked = everything OrElse allowed.Contains(extension)
                parent.Nodes.Add(leaf)
            Next
            parent.Checked = parent.Nodes.Cast(Of TreeNode)().All(Function(leaf) leaf.Checked)
            tree.Nodes.Add(parent)
            parent.Expand()
        Next
        suspendCascade = False
    End Sub

    ''' <summary>A group ticks and unticks its whole set; a leaf keeps its group's own box
    ''' honest. AfterCheck fires for the nodes we change here too, hence the guard.</summary>
    Private Sub NodeChecked(sender As Object, e As TreeViewEventArgs)
        If suspendCascade OrElse e.Node Is Nothing Then Return
        suspendCascade = True
        Try
            If e.Node.Parent Is Nothing Then
                For Each leaf As TreeNode In e.Node.Nodes
                    leaf.Checked = e.Node.Checked
                Next
            Else
                e.Node.Parent.Checked = e.Node.Parent.Nodes.Cast(Of TreeNode)().All(Function(leaf) leaf.Checked)
            End If
        Finally
            suspendCascade = False
        End Try
    End Sub

End Class

''' <summary>
''' "Recent files and folders" - §7.2. Open an entry, drop one, or clear the list; the
''' limits themselves stay on the settings page, because they are numbers and this is a
''' list.
'''
''' A path that no longer exists is shown greyed rather than hidden: §7.2 says such an
''' entry is removed when it is next read, and letting the user SEE what is about to go
''' is the difference between a tidy-up and a list that quietly loses things.
''' </summary>
Public NotInheritable Class History_Form
    Inherits Form

    Private ReadOnly tabs As New TabControl()
    Private ReadOnly fileList As New ListBox()
    Private ReadOnly folderList As New ListBox()
    Private ReadOnly btnOpen As New Button()
    Private ReadOnly btnRemove As New Button()
    Private ReadOnly btnClear As New Button()
    Private ReadOnly btnClose As New Button()

    Private ReadOnly files As List(Of String)
    Private ReadOnly folders As List(Of String)

    ''' <summary>Raised when the user asked to open an entry - the viewer owns what that
    ''' means, this window only knows the path.</summary>
    Public Event EntryChosen(entry As String)

    Public ReadOnly Property Files_Result As List(Of String)
        Get
            Return files
        End Get
    End Property

    Public ReadOnly Property Folders_Result As List(Of String)
        Get
            Return folders
        End Get
    End Property

    Public Sub New(files As List(Of String), folders As List(Of String))
        Me.files = New List(Of String)(If(files, New List(Of String)()))
        Me.folders = New List(Of String)(If(folders, New List(Of String)()))
        Build()
        Fill()
    End Sub

    Private Sub Build()
        Text = Localization.T("Недавние файлы и папки")
        FormBorderStyle = FormBorderStyle.Sizable
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(Localization.Scaled(660), 480)
        MinimumSize = New Size(Localization.Scaled(520), 340)
        Font = New Font(Localization.FontFamily(), 9.0F)
        If Localization.IsRightToLeft() Then RightToLeft = RightToLeft.Yes

        tabs.Bounds = New Rectangle(12, 12, ClientSize.Width - 24, ClientSize.Height - 62)
        tabs.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        tabs.TabPages.Add(NewPage(Localization.T("Файлы"), fileList))
        tabs.TabPages.Add(NewPage(Localization.T("Папки"), folderList))
        Controls.Add(tabs)

        AddButton(btnOpen, Localization.T("Открыть"), 12, ClientSize.Width, 110, AddressOf OpenSelected)
        AddButton(btnRemove, Localization.T("Удалить"), 128, ClientSize.Width, 110, AddressOf RemoveSelected)
        AddButton(btnClear, Localization.T("Очистить"), 244, ClientSize.Width, 110, AddressOf ClearCurrent)

        btnClose.Text = Localization.T("Закрыть")
        btnClose.Bounds = New Rectangle(ClientSize.Width - 12 - 110, ClientSize.Height - 40, 110, 28)
        btnClose.Anchor = AnchorStyles.Right Or AnchorStyles.Bottom
        btnClose.DialogResult = DialogResult.OK
        Controls.Add(btnClose)

        AcceptButton = btnClose
        CancelButton = btnClose

        AddHandler fileList.DoubleClick, Sub() OpenSelected(Nothing, EventArgs.Empty)
        AddHandler folderList.DoubleClick, Sub() OpenSelected(Nothing, EventArgs.Empty)
    End Sub

    Private Function NewPage(caption As String, list As ListBox) As TabPage
        Dim page As New TabPage(caption)
        list.Dock = DockStyle.Fill
        list.IntegralHeight = False
        list.DrawMode = DrawMode.OwnerDrawFixed
        AddHandler list.DrawItem, AddressOf DrawEntry
        page.Controls.Add(list)
        Return page
    End Function

    Private Sub AddButton(button As Button, caption As String, left As Integer, clientWidth As Integer,
                          width As Integer, handler As EventHandler)
        button.Text = caption
        button.Bounds = New Rectangle(left, ClientSize.Height - 40, width, 28)
        button.Anchor = AnchorStyles.Left Or AnchorStyles.Bottom
        AddHandler button.Click, handler
        Controls.Add(button)
    End Sub

    ''' <summary>Newest first - the order the recent-files menu already shows.</summary>
    Private Sub Fill()
        fileList.BeginUpdate()
        fileList.Items.Clear()
        For Each entry As String In Enumerable.Reverse(files)
            fileList.Items.Add(entry)
        Next
        fileList.EndUpdate()

        folderList.BeginUpdate()
        folderList.Items.Clear()
        For Each entry As String In Enumerable.Reverse(folders)
            folderList.Items.Add(entry)
        Next
        folderList.EndUpdate()
    End Sub

    Private Sub DrawEntry(sender As Object, e As DrawItemEventArgs)
        Dim list As ListBox = TryCast(sender, ListBox)
        If list Is Nothing OrElse e.Index < 0 OrElse e.Index >= list.Items.Count Then Return

        Dim entry As String = Convert.ToString(list.Items(e.Index))
        Dim missing As Boolean = Not Exists(entry, list Is folderList)
        e.DrawBackground()
        Dim colour As Color = If((e.State And DrawItemState.Selected) = DrawItemState.Selected,
                                 SystemColors.HighlightText,
                                 If(missing, SystemColors.GrayText, SystemColors.WindowText))
        TextRenderer.DrawText(e.Graphics, entry, e.Font, e.Bounds, colour,
                              TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.PathEllipsis)
        e.DrawFocusRectangle()
    End Sub

    Private Shared Function Exists(entry As String, isFolder As Boolean) As Boolean
        Try
            Return If(isFolder, Directory.Exists(entry), File.Exists(entry))
        Catch
            Return False
        End Try
    End Function

    Private Function CurrentList() As ListBox
        Return If(tabs.SelectedIndex = 1, folderList, fileList)
    End Function

    Private Sub OpenSelected(sender As Object, e As EventArgs)
        Dim list As ListBox = CurrentList()
        If list.SelectedIndex < 0 Then Return
        RaiseEvent EntryChosen(Convert.ToString(list.SelectedItem))
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub RemoveSelected(sender As Object, e As EventArgs)
        Dim list As ListBox = CurrentList()
        If list.SelectedIndex < 0 Then Return
        Dim entry As String = Convert.ToString(list.SelectedItem)
        If list Is folderList Then folders.Remove(entry) Else files.Remove(entry)
        Fill()
    End Sub

    Private Sub ClearCurrent(sender As Object, e As EventArgs)
        If MessageBox.Show(Me, Localization.T("Очистить весь список?"), Text,
                           MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        If CurrentList() Is folderList Then folders.Clear() Else files.Clear()
        Fill()
    End Sub

End Class
#End If
