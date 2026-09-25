Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' APP-BEHAVIOUR gates for the Share Manager (028_SPECIFICATION_CONTRACT_SYNC_DESKTOP_APP_UX.md
''' §5). The scanners are the viewer suite's, linked, so both programs are held to one reading.
''' </summary>
Public Class DesktopUxConformanceTests

    ''' <summary>Uses of a Message outside a log call that are allowed, each with its reason.</summary>
    Private Shared ReadOnly Allowed As (File As String, Fragment As String, Reason As String)() = {
        ("ShareController.vb", "refusal.Message",
         "not an exception's text: WorkerVersionRefusedException is built from a localized sentence (WorkerIpc.vb)")
    }

    ''' <summary>
    ''' No exception text reaches the user (rule 6). The Share Manager never showed one; what
    ''' it did instead was write seven of them to Debug.WriteLine, which a Release build drops -
    ''' this gate keeps the first half true, AppFileLogger.LogException is the second.
    ''' </summary>
    <Fact>
    Public Sub NoExceptionTextReachesTheUser()
        Dim srcDir = FindCompanionSourceDir()
        Assert.True(srcDir IsNot Nothing, "Could not locate src\FastMediaSorterCompanion from the test binary.")

        Dim leaks As New List(Of String)()
        For Each srcPath In Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories)
            If srcPath.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If srcPath.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            Dim name = Path.GetFileName(srcPath)
            For Each hit In ExceptionTextScanner.Scan(File.ReadAllText(srcPath))
                If Allowed.Any(Function(a) a.File = name AndAlso hit.Text.Contains(a.Fragment)) Then Continue For
                leaks.Add(name & ":" & hit.Line & "  " & hit.Text)
            Next
        Next

        Assert.True(leaks.Count = 0,
                    "An exception's Message outside a log call (APP-BEHAVIOUR rule 6):" & vbLf & String.Join(vbLf, leaks))
    End Sub

    ''' <summary>A caught exception goes to companion.log, never only to Debug output.</summary>
    <Fact>
    Public Sub No_catch_site_logs_only_to_Debug()
        Dim srcDir = FindCompanionSourceDir()
        Dim debugOnly As New List(Of String)()
        For Each srcPath In Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories)
            If srcPath.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            Dim lines = File.ReadAllLines(srcPath)
            For i = 0 To lines.Length - 1
                If lines(i).Contains("Debug.WriteLine(") AndAlso lines(i).Contains("ex.Message") Then
                    debugOnly.Add(Path.GetFileName(srcPath) & ":" & (i + 1))
                End If
            Next
        Next
        Assert.True(debugOnly.Count = 0,
                    "A caught exception written only to Debug, which a Release build drops - use AppFileLogger.LogException:" &
                    vbLf & String.Join(vbLf, debugOnly))
    End Sub

    ''' <summary>
    ''' APP-BEHAVIOUR rule 8 (ticket 028 Ф-7): every window of the Share Manager takes its
    ''' direction and script font from UiLanguage.ApplyTo - the one reader of the language
    ''' table's direction flag. Qr_Zoom_Form was the one that did not. A form is matched by
    ''' its class file (a partial class counts as one form across its files).
    ''' </summary>
    <Fact>
    Public Sub Every_form_takes_its_direction_from_the_language()
        Dim srcDir = FindCompanionSourceDir()
        Dim formsDir = Path.Combine(srcDir, "Forms")
        Dim missing As New List(Of String)()
        Dim formCount = 0
        For Each formFile In Directory.GetFiles(formsDir, "*.vb")
            Dim text = File.ReadAllText(formFile)
            If Not text.Contains("Inherits Form") Then Continue For
            formCount += 1
            Dim stem = Path.GetFileNameWithoutExtension(formFile)
            Dim applied = Directory.GetFiles(formsDir, stem & "*.vb").Any(Function(f) File.ReadAllText(f).Contains("UiLanguage.ApplyTo("))
            If Not applied Then missing.Add(stem)
        Next
        ' The consent dialog of "send the log" is built in code, not as a class.
        Assert.Contains("UiLanguage.ApplyTo(", File.ReadAllText(Path.Combine(formsDir, "SendLogsFlow.vb")))
        Assert.True(formCount >= 8, "Only " & formCount & " forms found.")
        Assert.True(missing.Count = 0, "Forms that set no direction from the language: " & String.Join(", ", missing))
    End Sub

    Private Shared Function FindCompanionSourceDir() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src", "FastMediaSorterCompanion")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Program.vb")) Then
                Return candidate
            End If
            dir = dir.Parent
        End While
        Return Nothing
    End Function

End Class
