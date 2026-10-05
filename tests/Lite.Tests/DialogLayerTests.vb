#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Ticket 124 UX-201: a window the viewer opens joins the viewer's z-order band, or it opens BEHIND a viewer
''' pinned "on top" and a modal one looks like a hung program. This reads the source (like the other scans of this
''' suite - no window is built) and fails on a bare modal show.
''' </summary>
Public Class DialogLayerTests

    ''' <summary>Native common dialogs cannot carry the topmost flag - an owner is all they take - and are not the
    ''' viewer's windows.</summary>
    Private Shared ReadOnly NativeDialogTypes As String() = {"OpenFileDialog", "SaveFileDialog", "FolderBrowserDialog", "ColorDialog", "FontDialog"}

    Private Shared ReadOnly NativeReceivers As String() = {"ofd", "sfd", "fbd", "openFileDialog", "saveFileDialog", "folder_Browser_Dialog", "folderBrowse", "TaskDialog"}

    ''' <summary>Shows that are allowed without the layer, each with the reason.</summary>
    Private Shared ReadOnly Allowed As (File As String, Fragment As String, Reason As String)() = {
        ("Main_Form.FileDetails.vb", "file_Details_Dialog.Show(Me)",
         "the #Else branch: the net48 fallback keeps its stock show (maintenance policy); the mainline branch above uses the layer")
    }

    Private Shared ReadOnly ShowCall As New Regex("(?<recv>[A-Za-z_][A-Za-z0-9_]*)\.(?:ShowDialog\(|Show\((?:Me|owner)\))", RegexOptions.Compiled)

    Private Shared Function SourceFiles() As List(Of String)
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then
                Return Directory.GetFiles(candidate, "*.vb", SearchOption.AllDirectories).
                    Where(Function(p) p.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                                      p.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                                      p.IndexOf("\FastMediaSorterCompanion\", StringComparison.OrdinalIgnoreCase) < 0).ToList()
            End If
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src/ not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function IsComment(line As String) As Boolean
        Return line.TrimStart().StartsWith("'", StringComparison.Ordinal)
    End Function

    ''' <summary>The receiver is a native common dialog: a stock receiver name, or a variable declared as one in
    ''' the lines just above.</summary>
    Private Shared Function IsNative(receiver As String, lines As String(), index As Integer) As Boolean
        If NativeReceivers.Contains(receiver) Then Return True
        For back As Integer = index To Math.Max(0, index - 14) Step -1
            For Each t As String In NativeDialogTypes
                If Regex.IsMatch(lines(back), "\bAs New " & t & "\b") AndAlso Regex.IsMatch(lines(back), "\b" & Regex.Escape(receiver) & "\b") Then Return True
            Next
        Next
        Return False
    End Function

    ''' <summary>The window was put into the viewer's band by hand right before it is shown (the older shape of
    ''' the same rule, still used by the settings window and the thumbnail panel on BOTH builds).</summary>
    Private Shared Function PinnedJustBefore(lines As String(), index As Integer) As Boolean
        For back As Integer = index - 1 To Math.Max(0, index - 20) Step -1
            If lines(back).Contains("PinToViewerBand(") Then Return True
        Next
        Return False
    End Function

    <Fact>
    Public Sub No_viewer_window_is_shown_without_the_viewer_band()
        Dim bare As New List(Of String)()
        Dim scanned As Integer = 0
        For Each path As String In SourceFiles()
            Dim name As String = System.IO.Path.GetFileName(path)
            If name = "DialogLayer.vb" Then Continue For
            scanned += 1
            Dim lines As String() = File.ReadAllLines(path)
            For i As Integer = 0 To lines.Length - 1
                If IsComment(lines(i)) Then Continue For
                Dim m As Match = ShowCall.Match(lines(i))
                If Not m.Success Then Continue For
                Dim receiver As String = m.Groups("recv").Value
                If IsNative(receiver, lines, i) Then Continue For
                If PinnedJustBefore(lines, i) Then Continue For
                If Allowed.Any(Function(a) a.File = name AndAlso lines(i).Contains(a.Fragment)) Then Continue For
                bare.Add(name & ":" & (i + 1).ToString() & "  " & lines(i).Trim())
            Next
        Next
        Assert.True(scanned > 100, "Only " & scanned & " source files scanned.")
        Assert.True(bare.Count = 0,
                    "A window is shown without joining the viewer's z-order band (124 UX-201) - open it through " &
                    "DialogLayer.ShowViewerDialog / ShowViewerWindow:" & vbLf & String.Join(vbLf, bare))
    End Sub

    ''' <summary>The gate can only fail if it catches something.</summary>
    <Fact>
    Public Sub The_scanner_catches_a_bare_show_and_passes_the_layer_and_native_dialogs()
        Dim bare As String() = {"        dlg.ShowDialog(Me)"}
        Assert.True(ShowCall.IsMatch(bare(0)))
        Assert.False(IsNative("dlg", bare, 0))
        Assert.False(PinnedJustBefore(bare, 0))
        Dim native As String() = {"        Using dialog As New OpenFileDialog()", "            dialog.ShowDialog(Me)"}
        Assert.True(IsNative("dialog", native, 1))
        Dim pinned As String() = {"        PinToViewerBand(panel)", "        panel.ShowDialog(Me)"}
        Assert.True(PinnedJustBefore(pinned, 1))
        Assert.False(ShowCall.IsMatch("        DialogLayer.ShowViewerDialog(dialog, Me)"))
    End Sub

    ''' <summary>UX-201 names these windows: each opens through the layer.</summary>
    <Theory>
    <InlineData("Main_Form.Secure.vb", "DialogLayer.ShowViewerDialog(dialog, Me)")>
    <InlineData("Main_Form.ResourceOpen.vb", "DialogLayer.ShowViewerDialog(dialog, Me)")>
    <InlineData("Main_Form.LiveBroadcast.vb", "DialogLayer.ShowViewerDialog(dlg, Me)")>
    <InlineData("Main_Form.FileDetails.vb", "DialogLayer.ShowViewerWindow(file_Details_Dialog, Me)")>
    <InlineData("Image_Panel_Form.Modern.vb", "DialogLayer.ShowViewerDialog(dialog, Me)")>
    <InlineData("Table_Form.SettingsDiscovery.vb", "DialogLayer.ShowViewerDialog(dialog, Me)")>
    <InlineData("Resource_List_Panel.vb", "DialogLayer.ShowViewerDialog(dialog, owner)")>
    <InlineData("Main_Form.ToVideo.vb", "DialogLayer.ShowViewerDialog(dialog, Me)")>
    <InlineData("Main_Form.ImageEditor.vb", "DialogLayer.ShowViewerDialog(editor, Me)")>
    Public Sub The_windows_of_UX_201_open_through_the_layer(fileName As String, call_Text As String)
        Dim path As String = SourceFiles().First(Function(p) System.IO.Path.GetFileName(p) = fileName)
        Assert.Contains(call_Text, File.ReadAllText(path))
    End Sub

    ''' <summary>Every question goes through the same layer (the ask dialogs open through ShowViewerDialog).</summary>
    <Fact>
    Public Sub The_question_surface_opens_through_the_layer()
        Dim source As String = File.ReadAllText(SourceFiles().First(Function(p) System.IO.Path.GetFileName(p) = "DialogLayer.vb"))
        Assert.Contains("DialogLayer.ShowViewerDialog(dialog, owner)", source)
    End Sub

    ''' <summary>The viewer installs its preparation once its window exists; without it the layer is a plain show.</summary>
    <Fact>
    Public Sub The_viewer_installs_the_layer_when_it_starts()
        Dim path As String = SourceFiles().First(Function(p) System.IO.Path.GetFileName(p) = "Main_Form.Lifecycle.vb")
        Assert.Contains("ViewerDialogPrep.Install()", File.ReadAllText(path))
    End Sub

    ''' <summary>A shown window without the hook is shown with its owner - what the tests that open a dialog need.</summary>
    <Fact>
    Public Sub Without_the_viewers_hook_the_layer_only_shows_the_window()
        Assert.Null(DialogLayer.Preparation)
        ' Prepare must not throw for a null window or a missing hook.
        DialogLayer.Prepare(Nothing, Nothing)
    End Sub

End Class
#End If
