#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports Xunit

''' <summary>
''' The archive behind Settings -> About -> "send the logs to the author"
''' (SPECIFICATION_SEND_LOGS_TO_AUTHOR.md).
'''
''' Two things here are worth pinning down, and both are the reasons the feature could
''' silently be useless: the log is held OPEN FOR WRITING by AppFileLogger while it is
''' being packed, and a log that has grown for months must not produce a mail nobody
''' can send.
''' </summary>
Public Class LogPackageTests

    <Fact>
    Public Sub A_log_that_is_open_for_writing_still_lands_in_the_archive()
        Dim logPath As String = Path.Combine(Path.GetTempPath(), "fms-test-open-" & Guid.NewGuid().ToString("N") & ".log")
        Dim zipPath As String = logPath & ".zip"

        Try
            File.WriteAllText(logPath, "first line" & vbLf & "second line" & vbLf)

            ' Exactly how AppFileLogger holds it: appending, sharing read+write. The
            ' convenience API (CreateEntryFromFile) opens with FileShare.Read and would
            ' throw IOException right here.
            Using writer As New FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                Using zip As New FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None)
                    Using archive As New ZipArchive(zip, ZipArchiveMode.Create)
                        Dim line As String = LogPackage.CopyLogEntry(archive, "current.log", logPath)
                        Assert.NotNull(line)
                        Assert.Contains("current.log", line)
                    End Using
                End Using
            End Using

            Assert.Equal("first line" & vbLf & "second line" & vbLf, ReadEntry(zipPath, "current.log"))
        Finally
            Delete(logPath)
            Delete(zipPath)
        End Try
    End Sub

    <Fact>
    Public Sub A_long_log_contributes_its_tail_with_a_truncation_marker()
        Dim logPath As String = Path.Combine(Path.GetTempPath(), "fms-test-long-" & Guid.NewGuid().ToString("N") & ".log")
        Dim zipPath As String = logPath & ".zip"

        Try
            Using writer As New StreamWriter(logPath, False, New UTF8Encoding(False))
                ' Comfortably over the 2 MB per-entry cap, with identifiable lines.
                For i As Integer = 0 To 60000
                    writer.WriteLine("line " & i.ToString() & " " & New String("x"c, 50))
                Next
            End Using

            Using zip As New FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None)
                Using archive As New ZipArchive(zip, ZipArchiveMode.Create)
                    LogPackage.CopyLogEntry(archive, "current.log", logPath)
                End Using
            End Using

            Dim packed As String = ReadEntry(zipPath, "current.log")
            Dim lines As String() = packed.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)

            Assert.StartsWith("[.. truncated:", lines(0))
            ' The tail seek lands mid-record; the partial line is dropped, so the first
            ' real line is a whole one.
            Assert.StartsWith("line ", lines(1))
            Assert.EndsWith("xxxxx", lines(1))
            ' The last line of the source survived - it is the tail we kept, not the head.
            Assert.StartsWith("line 60000 ", lines(lines.Length - 1))
            Assert.True(packed.Length < 2.2 * 1024 * 1024, "packed entry should be about the 2 MB cap, was " & packed.Length.ToString())
        Finally
            Delete(logPath)
            Delete(zipPath)
        End Try
    End Sub

    ''' <summary>The report dumps the registry profile, and exactly one value in there is
    ''' a secret. It never leaves the machine - not even in its DPAPI-encrypted form.</summary>
    <Theory>
    <InlineData("TranslateApiKey")>
    <InlineData("translateapikey")>
    <InlineData("SomePassword")>
    <InlineData("Share_Token")>
    Public Sub Secret_settings_are_recognised(name As String)
        Assert.True(LogPackage.IsSecretSetting(name))
    End Sub

    <Theory>
    <InlineData("MoveOn0")>
    <InlineData("OcrEnabled")>
    <InlineData("UiLanguage")>
    Public Sub Ordinary_settings_are_not_treated_as_secret(name As String)
        Assert.False(LogPackage.IsSecretSetting(name))
    End Sub

    <Fact>
    Public Sub Build_produces_a_readable_archive_with_a_report()
        Dim result As LogPackage.LogPackageResult = LogPackage.Build()

        Try
            Assert.False(String.IsNullOrEmpty(result.Path), "Build() returned no path: " & If(result.ErrorText, ""))
            Assert.True(File.Exists(result.Path))
            Assert.Contains("report.txt", result.Entries)

            Dim report As String = ReadEntry(result.Path, "report.txt")
            Assert.Contains("[application]", report)
            Assert.Contains("[system]", report)
            Assert.Contains("[settings]", report)
            ' Whatever is in this machine's profile, the key itself is never written out.
            Assert.DoesNotContain("TranslateApiKey=" & vbTab, report)
            If report.Contains("TranslateApiKey") Then Assert.Contains("TranslateApiKey=<hidden>", report)
        Finally
            If Not String.IsNullOrEmpty(result.Path) Then Delete(result.Path)
        End Try
    End Sub

    Private Shared Function ReadEntry(zipPath As String, entryName As String) As String
        Using archive As ZipArchive = ZipFile.OpenRead(zipPath)
            Dim entry As ZipArchiveEntry = archive.GetEntry(entryName)
            Assert.NotNull(entry)
            Using reader As New StreamReader(entry.Open())
                Return reader.ReadToEnd()
            End Using
        End Using
    End Function

    Private Shared Sub Delete(path As String)
        Try
            If File.Exists(path) Then File.Delete(path)
        Catch
        End Try
    End Sub

End Class
#End If
