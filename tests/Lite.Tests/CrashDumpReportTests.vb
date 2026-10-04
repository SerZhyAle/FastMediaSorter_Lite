#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports Xunit

Public Class CrashDumpReportTests
    <Fact>
    Public Sub Report_lists_dump_metadata_without_opening_or_archiving_contents()
        Dim directoryPath = Path.Combine(Path.GetTempPath(), "fms-dump-metadata-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(directoryPath)
        Dim dumpPath = Path.Combine(directoryPath, "FastMediaSorter_LITE.exe.123.dmp")
        File.WriteAllText(dumpPath, "private memory")
        File.WriteAllText(Path.Combine(directoryPath, "other.exe.123.dmp"), "unrelated")
        Dim package As LogPackage.LogPackageResult
        Try
            ' Exclusive handle forbids any content read; filesystem metadata remains readable.
            Using held As New FileStream(dumpPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                package = LogPackage.Build(Function() New List(Of KeyValuePair(Of String, Object)), Nothing, directoryPath,
                    Function(settings) LogPackage.BuildReport(settings, directoryPath))
                Assert.False(String.IsNullOrEmpty(package.Path), package.ErrorText)
                Using archive = ZipFile.OpenRead(package.Path)
                    Using reader As New StreamReader(archive.GetEntry("report.txt").Open())
                        Dim report = reader.ReadToEnd()
                        Assert.Contains("FastMediaSorter_LITE.exe.123.dmp: 14 bytes", report)
                        Assert.DoesNotContain("private memory", report)
                        Assert.DoesNotContain("other.exe.123.dmp", report)
                    End Using
                    Assert.DoesNotContain(archive.Entries, Function(entry) entry.FullName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase))
                End Using
            End Using
        Finally
            Directory.Delete(directoryPath, True)
        End Try
    End Sub
End Class
#End If
