#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' Ticket 118 Ф-3 / T-C1, T-C2: the viewer's one secret door onto the clipboard. The three
''' privacy formats carry a 4-byte zero stream each, an empty payload is refused without touching
''' the clipboard, and a source scan holds that the export path has no other <c>Clipboard.Set*</c>
''' - the payload is a credential and travels only through the guarded door.
''' </summary>
Public Class SecretClipboardTests

    <Fact>
    Public Sub TheThreePrivacyFormatsAreCarried_AsFourZeroByteStreams()
        Dim data As DataObject = SecretClipboard.BuildSecretText("payload")
        Assert.Equal("payload", data.GetText(TextDataFormat.UnicodeText))
        For Each format As String In SecretClipboard.PrivacyFormats
            Dim stream As MemoryStream = Nothing
            Assert.True(data.TryGetData(format, stream), "the privacy format must be present: " & format)
            Assert.Equal({CByte(0), CByte(0), CByte(0), CByte(0)}, stream.ToArray())
        Next
    End Sub

    <Fact>
    Public Sub AnEmptyPayloadIsRefused_WithoutTouchingTheClipboard()
        Assert.False(SecretClipboard.SetSecret(Nothing))
        Assert.False(SecretClipboard.SetSecret(""))
    End Sub

    <Fact>
    Public Sub ARealCopyReturnsABooleanAndNeverThrows()
        ' On a test thread without STA (or with the clipboard held) this comes back False through
        ' the catch - a failure is a return value, never an exception on the caller's screen.
        Dim copied As Boolean = SecretClipboard.SetSecret("not really copied in tests")
        Assert.True(copied = True OrElse copied = False)
    End Sub

    <Fact>
    Public Sub TheExportPathHasNoClipboardDoorOutsideTheGuard()
        ' T-C2: the JSON and the compressed code are credentials; only SecretClipboard may write
        ' to the clipboard anywhere along the export path. The phone-code window's address carries a
        ' single-use token (CLIPBOARD-GUARD rule 6), so its copy button is held to the same door.
        Dim guarded As String = LocateSource("SecretClipboard.vb")
        Dim offenders As New List(Of String)()
        If CodeText(guarded).Split("Clipboard.Set").Length - 1 <> 1 Then offenders.Add(guarded)
        For Each name As String In {"FmsConfigExport.vb", "ExportWriter.vb", "Resource_Export_Form.vb", "Resource_List_Panel.vb", "Resource_Phone_Code_Form.vb"}
            Dim path As String = TryLocate(name)
            ' A standalone Clipboard class call only: SecretClipboard.SetSecret (the door itself) is no offender.
            If path IsNot Nothing AndAlso System.Text.RegularExpressions.Regex.IsMatch(CodeText(path), "\bClipboard\s*\.\s*Set") Then offenders.Add(path)
        Next
        Assert.True(offenders.Count = 0, "unguarded clipboard writes: " & String.Join(", ", offenders))
    End Sub

    Private Shared Function LocateSource(fileName As String) As String
        Dim path As String = TryLocate(fileName)
        If path Is Nothing Then Throw New FileNotFoundException("Could not locate src\Resources\" & fileName & " from the test binary.")
        Return path
    End Function

    ''' <summary>Nothing when the file does not exist yet - the scan holds whatever of the export
    ''' path is already written, and the later files join the check as they are created.</summary>
    Private Shared Function TryLocate(fileName As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Resources", fileName)
            If File.Exists(candidate) Then Return candidate
            dir = dir.Parent
        End While
        Return Nothing
    End Function

    ''' <summary>Code lines only: a mention in a doc comment is not a clipboard call.</summary>
    Private Shared Function CodeText(path As String) As String
        Dim lines As String() = File.ReadAllLines(path)
        Dim code As New List(Of String)(lines.Length)
        For Each line As String In lines
            Dim trimmed As String = line.TrimStart()
            If trimmed.StartsWith("'''", StringComparison.Ordinal) OrElse trimmed.StartsWith("'", StringComparison.Ordinal) Then Continue For
            code.Add(line)
        Next
        Return String.Join(vbLf, code)
    End Function

End Class
#End If
