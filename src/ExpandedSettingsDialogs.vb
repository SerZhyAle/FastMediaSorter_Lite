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
    Private dialogFont As Font

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            MyBase.Dispose(disposing)
        Finally
            If disposing AndAlso dialogFont IsNot Nothing Then
                dialogFont.Dispose()
                dialogFont = Nothing
            End If
        End Try
    End Sub

    ''' <summary>The chosen extensions, lowercase and dotted; empty means all supported. The
    ''' verdict (including "nothing ticked cannot be accepted") is FileTypeSelection's.</summary>
    Public ReadOnly Property Selection As List(Of String)
        Get
            Return CurrentVerdict().Selection
        End Get
    End Property

    Private Function CurrentVerdict() As FileTypeSelection.Verdict
        Dim chosen As New List(Of String)()
        Dim total As Integer = 0
        For Each group As TreeNode In tree.Nodes
            For Each leaf As TreeNode In group.Nodes
                total += 1
                If leaf.Checked Then chosen.Add(Convert.ToString(leaf.Tag))
            Next
        Next
        Return FileTypeSelection.Evaluate(total, chosen)
    End Function
    Public Sub New(groups As List(Of KeyValuePair(Of String, String())), current As IEnumerable(Of String))
        Me.groups = groups
        Try
            Build()
            Fill(current)
        Catch
            Dispose()
            Throw
        End Try
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
        dialogFont = New Font(Localization.FontFamily(), 9.0F)
        Font = dialogFont
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
        ButtonGlyphs.Apply(btnOk, "action.confirm", ButtonGlyphRole.Affirmative)
        Controls.Add(btnOk)

        btnCancel.Text = Localization.T("Отмена")
        btnCancel.Bounds = New Rectangle(ClientSize.Width - 12 - 105, ClientSize.Height - 38, 105, 28)
        btnCancel.Anchor = AnchorStyles.Right Or AnchorStyles.Bottom
        btnCancel.DialogResult = DialogResult.Cancel
        ButtonGlyphs.Apply(btnCancel, "action.cancel", ButtonGlyphRole.Dismissing)
        Controls.Add(btnCancel)

        AcceptButton = btnOk
        CancelButton = btnCancel

        ' LAST line of the builder: scaling touches only the children that exist by now (124 UX-202).
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DialogLayout.ClampToWorkingArea(Me)
        ' The OK / Cancel pair keeps its end edge; a caption with its glyph that outgrew 105 px grows towards the start.
        PerformAutoScale()
        Dim between As Integer = btnCancel.Left - btnOk.Right
        Dim cancelWidth As Integer = GlyphButtonFit.NeededWidth(btnCancel)
        btnCancel.SetBounds(btnCancel.Right - cancelWidth, btnCancel.Top, cancelWidth, btnCancel.Height)
        Dim okWidth As Integer = GlyphButtonFit.NeededWidth(btnOk)
        btnOk.SetBounds(btnCancel.Left - between - okWidth, btnOk.Top, okWidth, btnOk.Height)
    End Sub

    Private Sub Fill(current As IEnumerable(Of String))
        ' An empty stored list is the "everything" sentinel, so it must arrive here as
        ' every box ticked - not as an empty dialog.
        Dim allowed As HashSet(Of String) = FileTypeSelection.TickedOnOpen(
            current, groups.SelectMany(Function(g) g.Value))
        Dim everything As Boolean = False

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
        UpdateOkButtonState()
    End Sub

    Private Sub UpdateOkButtonState()
        btnOk.Enabled = CurrentVerdict().CanAccept
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
        UpdateOkButtonState()
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
    Private dialogFont As Font

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            ' Queued probes of this dialog never run; a call already in flight finishes on its own.
            If disposing Then HistoryProbes.CancelOwner(Me)
            MyBase.Dispose(disposing)
        Finally
            If disposing AndAlso dialogFont IsNot Nothing Then
                dialogFont.Dispose()
                dialogFont = Nothing
            End If
        End Try
    End Sub

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
        Try
            Build()
            Fill()
        Catch
            Dispose()
            Throw
        End Try
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
        dialogFont = New Font(Localization.FontFamily(), 9.0F)
        Font = dialogFont
        If Localization.IsRightToLeft() Then RightToLeft = RightToLeft.Yes

        tabs.Bounds = New Rectangle(12, 12, ClientSize.Width - 24, ClientSize.Height - 62)
        tabs.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        tabs.TabPages.Add(NewPage(Localization.T("Файлы"), fileList))
        tabs.TabPages.Add(NewPage(Localization.T("Папки"), folderList))
        Controls.Add(tabs)

        AddButton(btnOpen, Localization.T("Открыть"), 12, ClientSize.Width, 110, AddressOf OpenSelected)
        AddButton(btnRemove, Localization.T("Удалить"), 128, ClientSize.Width, 110, AddressOf RemoveSelected)
        AddButton(btnClear, Localization.T("Очистить"), 244, ClientSize.Width, 110, AddressOf ClearCurrent)
        ' The three are actions on the list, not the window's answer (Close is): mono glyphs.
        ButtonGlyphs.Apply(btnOpen, "nav.open-external")
        ButtonGlyphs.Apply(btnRemove, "action.delete")
        ButtonGlyphs.Apply(btnClear, "action.clear-all")

        btnClose.Text = Localization.T("Закрыть")
        btnClose.Bounds = New Rectangle(ClientSize.Width - 12 - 110, ClientSize.Height - 40, 110, 28)
        btnClose.Anchor = AnchorStyles.Right Or AnchorStyles.Bottom
        btnClose.DialogResult = DialogResult.OK
        ' Accept and Cancel at once: the one way out of this list window, so it is the dismissing answer.
        ButtonGlyphs.Apply(btnClose, "nav.close", ButtonGlyphRole.Dismissing)
        Controls.Add(btnClose)

        AcceptButton = btnClose
        CancelButton = btnClose

        AddHandler fileList.DoubleClick, Sub() OpenSelected(Nothing, EventArgs.Empty)
        AddHandler folderList.DoubleClick, Sub() OpenSelected(Nothing, EventArgs.Empty)

        ' LAST line of the builder: scaling touches only the children that exist by now (124 UX-202).
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DialogLayout.ClampToWorkingArea(Me)
        ' Three chained buttons at the start edge grow towards the end, keeping the 6 px gap they had; Close keeps its end edge.
        PerformAutoScale()
        Dim gap As Integer = btnRemove.Left - btnOpen.Right
        btnOpen.SetBounds(btnOpen.Left, btnOpen.Top, GlyphButtonFit.NeededWidth(btnOpen), btnOpen.Height)
        btnRemove.SetBounds(btnOpen.Right + gap, btnRemove.Top, GlyphButtonFit.NeededWidth(btnRemove), btnRemove.Height)
        btnClear.SetBounds(btnRemove.Right + gap, btnClear.Top, GlyphButtonFit.NeededWidth(btnClear), btnClear.Height)
        Dim closeWidth As Integer = GlyphButtonFit.NeededWidth(btnClose)
        btnClose.SetBounds(btnClose.Right - closeWidth, btnClose.Top, closeWidth, btnClose.Height)
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

    ''' <summary>Newest first - the order the recent-files menu already shows. The two
    ''' lists are stored the opposite way round (files newest LAST, folders newest FIRST),
    ''' so only the file list is reversed; reversing both showed the oldest folder at the
    ''' top (053 F-04).</summary>
    Private Sub Fill()
        fileList.BeginUpdate()
        fileList.Items.Clear()
        For Each entry As String In Utils.RecentNewestFirst(files, storedNewestFirst:=False)
            fileList.Items.Add(entry)
        Next
        fileList.EndUpdate()

        folderList.BeginUpdate()
        folderList.Items.Clear()
        For Each entry As String In Utils.RecentNewestFirst(folders, storedNewestFirst:=True)
            folderList.Items.Add(entry)
        Next
        folderList.EndUpdate()
    End Sub

    ''' <summary>Every history dialog shares two actual existence calls and at most 64 queued
    ''' keys (105 L01, 057.S15-3). A call stuck on a dead share keeps its permit until the OS
    ''' returns - closing the dialog drops its queue, not a call already in flight - so repeated
    ''' opens cannot pile blocked probes onto the thread pool. The same (dialog, kind, path) is
    ''' asked once.</summary>
    Friend Shared ReadOnly HistoryProbes As New ProbeScheduler(Of Boolean)(
        New ProbePolicy With {.MaxActive = 2, .MaxPending = 64, .CoalesceByKey = True})
    Friend Shared ExistsFunc As Func(Of String, Boolean, Boolean) = AddressOf Exists

    ''' <summary>Existence verdicts, by list and path. Filled off the UI thread; read by
    ''' DrawEntry, which never touches the disk itself.</summary>
    Private ReadOnly existence As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly probing As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly existenceLock As New Object()

    ''' <summary>
    ''' The verdict for one entry, or Nothing while it is unknown - in which case a probe is
    ''' started once, on a worker, and the list is repainted when it answers. DrawEntry used to
    ''' call File.Exists/Directory.Exists itself on every paint of every row: a history with NAS
    ''' paths froze the dialog for tens of seconds per row, again on every scroll (053 F-04).
    ''' </summary>
    Private Function KnownExistence(entry As String, isFolder As Boolean, list As ListBox) As Boolean?
        If IsDisposed OrElse Disposing Then Return Nothing

        Dim key As String = If(isFolder, "D|", "F|") & entry
        SyncLock existenceLock
            Dim known As Boolean
            If existence.TryGetValue(key, known) Then Return known
            If Not probing.Add(key) Then Return Nothing
        End SyncLock

        ScheduleProbe(key, entry, isFolder, list)
        Return Nothing
    End Function

    Private Sub ScheduleProbe(key As String, entry As String, isFolder As Boolean, list As ListBox)
        Dim checker As Func(Of String, Boolean, Boolean) = If(ExistsFunc, AddressOf Exists)
        Dim answer As Threading.Tasks.Task(Of ProbeResult(Of Boolean)) =
            HistoryProbes.Submit(Me, key, Function() checker(entry, isFolder), Function() Not IsDisposed AndAlso Not Disposing)
        answer.ContinueWith(Sub(done) ProbeFinished(key, list, done.Result), Threading.Tasks.TaskScheduler.Default)
    End Sub

    ''' <summary>Runs on a pool thread when the scheduler has an outcome for a row. Anything
    ''' other than an answer leaves the row UNKNOWN (drawn as present) - never "missing".</summary>
    Private Sub ProbeFinished(key As String, list As ListBox, result As ProbeResult(Of Boolean))
        Try
            SyncLock existenceLock
                probing.Remove(key)
                If result.Status = ProbeStatus.Completed Then existence(key) = result.Value
            End SyncLock
            If result.Status = ProbeStatus.Faulted Then
                AppFileLogger.LogException("History probe", result.Failure)
            End If
            If result.Status = ProbeStatus.Completed AndAlso Not IsDisposed AndAlso Not Disposing AndAlso
               Not list.IsDisposed AndAlso list.IsHandleCreated Then
                list.BeginInvoke(New MethodInvoker(AddressOf list.Invalidate))
            End If
        Catch ex As Exception
            ' The dialog went away between the check and the post; nothing to repaint.
            AppFileLogger.LogException("History: repainting after a probe", ex)
        End Try
    End Sub
    Private Sub DrawEntry(sender As Object, e As DrawItemEventArgs)
        Dim list As ListBox = TryCast(sender, ListBox)
        If list Is Nothing OrElse e.Index < 0 OrElse e.Index >= list.Items.Count Then Return

        Dim entry As String = Convert.ToString(list.Items(e.Index))
        ' Unknown is drawn as present: greying an entry is a claim, made only once proven.
        Dim missing As Boolean = Not KnownExistence(entry, list Is folderList, list).GetValueOrDefault(True)
        e.DrawBackground()
        Dim colour As Color = If((e.State And DrawItemState.Selected) = DrawItemState.Selected,
                                 ThemeService.Palette.AccentInk,
                                 If(missing, ThemeService.Palette.TextMuted, ThemeService.Palette.TextPrimary))
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
        If ViewerBox.Show(Me, Localization.T("Очистить весь список?"), Text,
                           MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        If CurrentList() Is folderList Then folders.Clear() Else files.Clear()
        Fill()
    End Sub

End Class

''' <summary>What a fixed-width command button needs once a glyph sits before its caption: the caption, the glyph
''' bitmap (it already carries its own gap to the caption) and the 12 px side padding DialogLayout.FitContent uses.
''' For the windows of this file, which lay their button rows out by hand and do not run FitContent.</summary>
Friend Module GlyphButtonFit
    Friend Function NeededWidth(button As Button) As Integer
        Dim caption As Integer = TextRenderer.MeasureText(button.Text, button.Font).Width
        Return Math.Max(button.Width, caption + ButtonGlyphs.ExtraWidth(button) + button.LogicalToDeviceUnits(24))
    End Function
End Module
#End If
