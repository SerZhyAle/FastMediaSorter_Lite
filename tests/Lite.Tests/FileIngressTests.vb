Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' One ingress for a concrete file (SPECIFICATION_CHOOSE_FILE_OPENS_SELECTED_FILE.md §5.1-§5.2).
'''
''' FILE/F4 used to duplicate a subset of ProcessArgument's setup and then call
''' ReadShowMediaFile(Mode_FolderAndKnownFile) itself. That mode deliberately does NOT
''' rebuild the list while is_External_Input_Received is set, and the picker never built
''' the one-file list, so the display pipeline could resolve the PREVIOUS entry - the
''' chosen file simply did not open.
'''
''' The fix is a routing rule, and a routing rule is what can be checked here: Main_Form
''' is a WinForms class the test project deliberately does not link (the project comment
''' says why), so these read the shipped source the way LocalizationCoverageTests does.
''' UI automation would prove more, but nothing about this change is visual - it is
''' entirely "who is allowed to enter this mode".
''' </summary>
Public Class FileIngressTests

    ''' <summary>
    ''' The picker hands its selected path to ProcessArgument and builds no state of its
    ''' own. Each forbidden name below is one line the old body carried; together they are
    ''' exactly the half-copy of ProcessArgument that made the defect possible.
    ''' </summary>
    <Fact>
    Public Sub Choose_file_delegates_the_selected_path_to_ProcessArgument()
        Dim body = ChooseFileBody()

        ' A scan that extracted the wrong span would pass every check below by having
        ' found nothing at all, so pin one landmark the real body cannot lose.
        Assert.Contains("openFileDialog", body, StringComparison.Ordinal)

        Assert.True(Regex.IsMatch(body, "ProcessArgument\s*\("),
                    "Choose_file must hand the selected path to ProcessArgument:" & vbLf & body)

        For Each forbidden In New String() {"ReadShowMediaFile", "files_List", "Current_Image_Path",
                                            "Current_File_Name", "is_External_Input_Received",
                                            "was_External_Input_Previously", "current_File_Index",
                                            "total_File_Count", "LeaveArchive"}
            Assert.False(Regex.IsMatch(body, "(?<![\w.])" & forbidden & "(?![\w])"),
                         "Choose_file must not touch " & forbidden & " - ProcessArgument owns that state:" & vbLf & body)
        Next
    End Sub

    ''' <summary>
    ''' The mode itself has exactly one entry point. This is the invariant the delegation
    ''' serves: a second caller could re-introduce the same bug without ever touching
    ''' Choose_file, and this fails the moment one appears.
    ''' </summary>
    <Fact>
    Public Sub Mode_FolderAndKnownFile_is_entered_from_exactly_one_place()
        Dim callers As New List(Of String)()

        For Each srcPath In Directory.GetFiles(SourceDir(), "*.vb", SearchOption.AllDirectories)
            If IsBuildOutput(srcPath) Then Continue For
            Dim text = BlankComments(File.ReadAllText(srcPath))
            For Each m As Match In Regex.Matches(text, "ReadShowMediaFile\s*\(\s*Mode_FolderAndKnownFile\s*\)")
                callers.Add(Path.GetFileName(srcPath) & ":" & LineOf(text, m.Index))
            Next
        Next

        Assert.True(callers.Count = 1,
                    "Mode_FolderAndKnownFile must be entered only from ApplyArgument. Found " &
                    callers.Count & ": " & String.Join(", ", callers))
        Assert.StartsWith("Main_Form.Lifecycle.vb", callers(0), StringComparison.Ordinal)
    End Sub

    ' ------------------------------------------------------------------ helpers ----

    ''' <summary>
    ''' The body of Private Sub Choose_file(), comments blanked - a comment quoting the
    ''' old code (this fix left one) is documentation, not a call.
    ''' </summary>
    Private Shared Function ChooseFileBody() As String
        Dim text = BlankComments(File.ReadAllText(Path.Combine(SourceDir(), "Main_Form.vb")))

        ' The sources are CRLF, and multiline "$" matches before the "\n" only - the "\r"
        ' has to be consumed explicitly or nothing here ever matches.
        Dim head = Regex.Match(text, "(?m)^(?<indent>[ \t]*)Private Sub Choose_file\s*\(\s*\)[ \t\r]*$")
        Assert.True(head.Success, "Could not find Private Sub Choose_file() in Main_Form.vb.")

        Dim tail = Regex.Match(text.Substring(head.Index + head.Length),
                               "(?m)^" & head.Groups("indent").Value & "End Sub[ \t\r]*$")
        Assert.True(tail.Success, "Could not find the End Sub of Choose_file.")

        Return text.Substring(head.Index, head.Length + tail.Index)
    End Function

    ''' <summary>obj\ and bin\ hold generated copies that would double every count.</summary>
    Private Shared Function IsBuildOutput(srcPath As String) As Boolean
        Return srcPath.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               srcPath.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    ''' <summary>Whole-line comments only - the same rule LocalizationCoverageTests uses.</summary>
    Private Shared Function BlankComments(text As String) As String
        Return Regex.Replace(text, "(?m)^[ \t]*'.*$", "")
    End Function

    Private Shared Function LineOf(text As String, index As Integer) As Integer
        Return text.Take(index).Count(Function(c) c = ChrW(10)) + 1
    End Function

    Private Shared Function SourceDir() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then
                Return candidate
            End If
            dir = dir.Parent
        End While
        Assert.True(False, "Could not locate the src\ directory from the test binary.")
        Return Nothing
    End Function

End Class
