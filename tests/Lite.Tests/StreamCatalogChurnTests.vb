#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports fmsl.Streams
Imports Xunit

Namespace Lite.Tests

    ''' <summary>
    ''' Acceptance tests for Rebuilding and Churn Guard (022_SPECIFICATION_PC_STREAM_CATALOG_PRODUCER.md, Criteria 22-25).
    ''' </summary>
    Public Class StreamCatalogChurnTests
        Implements IDisposable

        Private ReadOnly testDir As String

        Public Sub New()
            testDir = Path.Combine(Path.GetTempPath(), "fms_test_churn_" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(testDir)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                If Directory.Exists(testDir) Then
                    Directory.Delete(testDir, True)
                End If
            Catch
            End Try
        End Sub

        Private Function CreatePlaylistFile(fileName As String, count As Integer, Optional prefix As String = "station") As String
            Dim path = System.IO.Path.Combine(testDir, fileName)
            Dim sb As New StringBuilder()
            sb.AppendLine("#EXTM3U")
            For i = 1 To count
                sb.AppendLine("#EXTINF:-1,Station " & i)
                sb.AppendLine("https://stream.example.com/" & prefix & "/" & i)
            Next
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8)
            Return path
        End Function

        <Fact>
        Public Sub Criterion22_FailedRebuild_LeavesPreviousArchiveByteIdentical()
            ' Criterion 22: Replacement is atomic - failed run leaves previous archive untouched
            Dim goodPlaylist = CreatePlaylistFile("good.m3u", 10)
            Dim outZip = Path.Combine(testDir, "atomic.zip")

            ' Initial build
            Dim res1 = StreamCatalogProducer.Build(goodPlaylist, outZip)
            Assert.True(res1.Success)
            Dim originalBytes = File.ReadAllBytes(outZip)

            ' Try rebuild with bad/non-existent input
            Dim badInput = Path.Combine(testDir, "does_not_exist.m3u")
            Dim res2 = StreamCatalogProducer.Build(badInput, outZip)
            Assert.False(res2.Success)

            ' Verify outZip is byte-identical to original
            Dim currentBytes = File.ReadAllBytes(outZip)
            Assert.Equal(originalBytes.Length, currentBytes.Length)
            Assert.Equal(originalBytes, currentBytes)
        End Sub

        <Fact>
        Public Sub Criterion23_RerunOverUnchangedInput_ProducesFunctionallyIdenticalCatalog()
            ' Criterion 23: Re-running over unchanged input produces functionally identical catalog
            Dim playlist = CreatePlaylistFile("same.m3u", 10)
            Dim outZip = Path.Combine(testDir, "same.zip")

            Dim res1 = StreamCatalogProducer.Build(playlist, outZip)
            Assert.True(res1.Success)
            Dim urls1 = StreamCatalogProducer.ReadExistingArchiveUrls(outZip)

            Dim res2 = StreamCatalogProducer.Build(playlist, outZip)
            Assert.True(res2.Success)
            Dim urls2 = StreamCatalogProducer.ReadExistingArchiveUrls(outZip)

            Assert.Equal(urls1.Count, urls2.Count)
            For i = 0 To urls1.Count - 1
                Assert.Equal(urls1(i), urls2(i))
            Next
        End Sub

        <Fact>
        Public Sub Criterion24_ChurnGuard_RefusesWhenMoreThan20PercentUrlsLostOn50PlusRows()
            ' Criterion 24: Rebuild dropping > 20% of previous URLs on >= 50 rows stops unless confirmed
            Dim bigPlaylist = CreatePlaylistFile("big.m3u", 60)
            Dim outZip = Path.Combine(testDir, "churn.zip")

            ' Build initial 60-row catalog
            Dim res1 = StreamCatalogProducer.Build(bigPlaylist, outZip)
            Assert.True(res1.Success)

            ' New playlist has only 30 rows (50% dropped)
            Dim smallerPlaylist = CreatePlaylistFile("smaller.m3u", 30)

            ' Build without force -> churn refusal
            Dim res2 = StreamCatalogProducer.Build(smallerPlaylist, outZip, forceChurn:=False)
            Assert.False(res2.Success)
            Assert.True(res2.ChurnRefused)

            ' Build with callback returning False -> churn refused
            Dim callbackCalled = False
            Dim res3 = StreamCatalogProducer.Build(smallerPlaylist, outZip, forceChurn:=False,
                churnCallback:=Function(lost, prev, pct, mostAff)
                                   callbackCalled = True
                                   Return False
                               End Function)
            Assert.True(callbackCalled)
            Assert.False(res3.Success)
            Assert.True(res3.ChurnRefused)

            ' Build with force -> succeeds
            Dim res4 = StreamCatalogProducer.Build(smallerPlaylist, outZip, forceChurn:=True)
            Assert.True(res4.Success)
            Assert.Equal(30, res4.Report.ShippedRowCount)
        End Sub

        <Fact>
        Public Sub Criterion25_EditingOnePlaylist_PreservesUrlsAndConsumerIdentity()
            ' Criterion 25: Editing one playlist updates only affected rows, URL identity preserved
            Dim playlistFile = Path.Combine(testDir, "edit.m3u")
            File.WriteAllText(playlistFile, "#EXTM3U" & vbLf &
                                           "#EXTINF:-1,Original Name" & vbLf &
                                           "https://stream.example.com/ch1" & vbLf, Encoding.UTF8)

            Dim outZip = Path.Combine(testDir, "edit.zip")
            StreamCatalogProducer.Build(playlistFile, outZip)

            ' Edit name in playlist
            File.WriteAllText(playlistFile, "#EXTM3U" & vbLf &
                                           "#EXTINF:-1,Updated Station Name" & vbLf &
                                           "https://stream.example.com/ch1" & vbLf, Encoding.UTF8)

            Dim res2 = StreamCatalogProducer.Build(playlistFile, outZip)
            Assert.True(res2.Success)

            Dim parseResult = M3uPlaylistReader.ReadPath(playlistFile)
            Assert.Equal(1, parseResult.Rows.Count)
            Assert.Equal("Updated Station Name", parseResult.Rows(0).Name)
            Assert.Equal("https://stream.example.com/ch1", parseResult.Rows(0).Url)
        End Sub

    End Class

End Namespace
#End If
