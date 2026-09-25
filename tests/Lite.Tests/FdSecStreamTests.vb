#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' The decrypting stream (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §5.2,
''' §13 "Stream tests"). This is the piece that lets decision 3 - no decrypted byte ever
''' reaches the disk - be cheap rather than heroic, and it is the piece a decoder and
''' LibVLC will use in ways no image loader ever does: seeking backwards, reading a
''' megabyte at a time, and doing both from threads we do not own.
'''
''' Everything here runs under the lowered test profile with a small alignment and chunk
''' size, so a "multi-chunk" container is a few kilobytes and the suite stays fast.
'''
''' Modern-only: on the net48 leg src/Secure and this file compile to nothing.
''' </summary>
Public Class FdSecStreamTests

    Private Const Credential As String = "correct horse battery staple"
    Private Const Alignment As Integer = 512
    Private Const ChunkSize As Integer = 1024
    Private Const Capacity As Integer = ChunkSize - 16

    Private Shared Function Payload(count As Integer) As Byte()
        Dim bytes(count - 1) As Byte
        For i As Integer = 0 To count - 1
            ' Deliberately period-free relative to the chunk size, so a chunk read at the
            ' wrong offset shows up as different bytes rather than as the same pattern.
            bytes(i) = CByte((i * 37 + (i \ 251)) And &HFF)
        Next
        Return bytes
    End Function

    Private Shared Function OpenPacked(content As Byte()) As FdSecContainer
        Dim packed As Byte() = FdSecTestContainer.Pack(content, "inside.bin", Credential,
                                                       FdSecTestContainer.FastProfile, Alignment, ChunkSize)
        Return FdSecContainer.Open(New MemoryStream(packed, writable:=False), Credential,
                                   ownsSource:=True, profile:=FdSecTestContainer.FastProfile)
    End Function

    ''' <summary>Length is the ORIGINAL's real size out of the sealed metadata, never the
    ''' container's own length - which is bigger, cluster-padded and deliberately says
    ''' nothing about the size inside.</summary>
    <Fact>
    Public Sub Length_is_the_originals_size_not_the_containers()
        Dim content As Byte() = Payload(5000)
        Using container As FdSecContainer = OpenPacked(content)
            Using stream As Stream = container.OpenContentStream()
                Assert.Equal(5000L, stream.Length)
                Assert.True(container.ChunkCount > 1)
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' A random-access read of every byte equals a full sequential read. This is the
    ''' assertion that actually proves the chunk arithmetic: an off-by-one in the chunk
    ''' index, the offset inside a chunk or the last chunk's length shows up here as one
    ''' wrong byte and nowhere else.
    ''' </summary>
    <Fact>
    Public Sub Random_access_reads_equal_a_full_sequential_read()
        Dim content As Byte() = Payload(5000)
        Using container As FdSecContainer = OpenPacked(content)
            Dim sequential As Byte()
            Using stream As Stream = container.OpenContentStream()
                Using buffer As New MemoryStream()
                    stream.CopyTo(buffer)
                    sequential = buffer.ToArray()
                End Using
            End Using
            Assert.Equal(content, sequential)

            Using stream As Stream = container.OpenContentStream()
                ' Backwards, so every read crosses a boundary the previous one did not and
                ' the one-chunk cache is evicted on almost every step.
                For position As Integer = content.Length - 1 To 0 Step -1
                    stream.Seek(position, SeekOrigin.Begin)
                    Dim one(0) As Byte
                    Assert.Equal(1, stream.Read(one, 0, 1))
                    Assert.Equal(content(position), one(0))
                Next
            End Using
        End Using
    End Sub

    ''' <summary>Seeks that land exactly on a chunk boundary, one byte before it and one
    ''' byte after it - the three places the arithmetic can differ.</summary>
    <Fact>
    Public Sub Seeks_across_chunk_boundaries_land_on_the_right_byte()
        Dim content As Byte() = Payload(5000)
        Using container As FdSecContainer = OpenPacked(content)
            Using stream As Stream = container.OpenContentStream()
                For boundary As Integer = Capacity To content.Length - 1 Step Capacity
                    For Each offset As Integer In {-1, 0, 1}
                        Dim position As Integer = boundary + offset
                        If position < 0 OrElse position >= content.Length Then Continue For
                        Assert.Equal(CLng(position), stream.Seek(position, SeekOrigin.Begin))
                        Dim one(0) As Byte
                        Assert.Equal(1, stream.Read(one, 0, 1))
                        Assert.Equal(content(position), one(0))
                    Next
                Next
            End Using
        End Using
    End Sub

    ''' <summary>A single read larger than one chunk is served out of several chunks and
    ''' returns all of them - a stream that quietly returned only the first chunk would
    ''' still "work" for an image loader that loops, and would truncate anything that does
    ''' not.</summary>
    <Fact>
    Public Sub A_read_larger_than_a_chunk_is_served_whole()
        Dim content As Byte() = Payload(5000)
        Using container As FdSecContainer = OpenPacked(content)
            Using stream As Stream = container.OpenContentStream()
                Dim buffer(4095) As Byte
                Dim read As Integer = stream.Read(buffer, 0, buffer.Length)
                Assert.Equal(4096, read)
                Assert.Equal(content.Take(4096).ToArray(), buffer)
            End Using
        End Using
    End Sub

    ''' <summary>Seeking past the end reads nothing rather than failing: it is legal on a
    ''' Stream, and VLC probes that way while it works out what it is playing.</summary>
    <Fact>
    Public Sub Reading_past_the_end_returns_zero()
        Using container As FdSecContainer = OpenPacked(Payload(100))
            Using stream As Stream = container.OpenContentStream()
                stream.Seek(1000, SeekOrigin.Begin)
                Dim buffer(9) As Byte
                Assert.Equal(0, stream.Read(buffer, 0, buffer.Length))
                Assert.Equal(0, stream.Read(buffer, 0, 0))
            End Using
        End Using
    End Sub

    ''' <summary>An empty original is a stream of length zero that reads nothing and does
    ''' not fall over - the one-chunk-of-length-zero case, seen from the consumer's
    ''' side.</summary>
    <Fact>
    Public Sub An_empty_original_is_an_empty_stream()
        Using container As FdSecContainer = OpenPacked(Array.Empty(Of Byte)())
            Using stream As Stream = container.OpenContentStream()
                Assert.Equal(0L, stream.Length)
                Dim buffer(9) As Byte
                Assert.Equal(0, stream.Read(buffer, 0, buffer.Length))
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' Two threads reading their own streams over one container get the right bytes.
    ''' LibVLC reads from threads we do not own, so this is not a hypothetical: without
    ''' the lock the two would race on the shared source's file position and each would
    ''' authenticate a chunk the other had seeked away from.
    ''' </summary>
    <Fact>
    Public Sub Concurrent_reads_from_two_threads_agree_with_the_content()
        Dim content As Byte() = Payload(20000)
        Using container As FdSecContainer = OpenPacked(content)
            Dim failures As New System.Collections.Concurrent.ConcurrentBag(Of String)()

            Dim work As Action(Of Integer) =
                Sub(worker)
                    Try
                        Using stream As Stream = container.OpenContentStream()
                            Dim random As New Random(worker)
                            For pass As Integer = 1 To 200
                                Dim position As Integer = random.Next(0, content.Length - 64)
                                stream.Seek(position, SeekOrigin.Begin)
                                Dim buffer(63) As Byte
                                Dim got As Integer = 0
                                While got < buffer.Length
                                    Dim read As Integer = stream.Read(buffer, got, buffer.Length - got)
                                    If read <= 0 Then Exit While
                                    got += read
                                End While
                                If got <> buffer.Length Then
                                    failures.Add("short read at " & position.ToString())
                                    Return
                                End If
                                For i As Integer = 0 To buffer.Length - 1
                                    If buffer(i) <> content(position + i) Then
                                        failures.Add("wrong byte at " & (position + i).ToString())
                                        Return
                                    End If
                                Next
                            Next
                        End Using
                    Catch ex As Exception
                        failures.Add(ex.GetType().Name & ": " & ex.Message)
                    End Try
                End Sub

            Parallel.Invoke(Sub() work(1), Sub() work(2), Sub() work(3), Sub() work(4))
            Assert.Empty(failures)
        End Using
    End Sub

    ''' <summary>
    ''' Disposing the container releases the file handle. Nothing downstream is careful
    ''' about this on its own - the viewer's move and delete run straight after the media
    ''' is released - so a container that kept its handle would turn every file operation
    ''' on a displayed container into an access-denied.
    ''' </summary>
    <Fact>
    Public Sub Disposal_releases_the_file_handle()
        Dim containerPath As String = Path.Combine(Path.GetTempPath(), "fms-fdsec-" & Guid.NewGuid().ToString("N") & ".fd-sec")
        Try
            File.WriteAllBytes(containerPath, FdSecTestContainer.Pack(Payload(3000), "inside.bin", Credential,
                                                             FdSecTestContainer.FastProfile, Alignment, ChunkSize))

            Dim container As FdSecContainer = FdSecContainer.OpenFile(containerPath, Credential, FdSecTestContainer.FastProfile)
            Using stream As Stream = container.OpenContentStream()
                Dim buffer(9) As Byte
                Assert.Equal(10, stream.Read(buffer, 0, buffer.Length))
            End Using
            container.Dispose()

            ' Exclusive access proves the handle is really gone.
            Using exclusive As New FileStream(containerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                Assert.True(exclusive.Length > 0)
            End Using
        Finally
            Try
                If File.Exists(containerPath) Then File.Delete(containerPath)
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>The content stream is read-only, and says so rather than pretending to
    ''' accept a write. Nothing in the viewer writes to it; a future caller that tries
    ''' finds out at the call, not in a file.</summary>
    <Fact>
    Public Sub The_content_stream_refuses_to_be_written()
        Using container As FdSecContainer = OpenPacked(Payload(100))
            Using stream As Stream = container.OpenContentStream()
                Assert.False(stream.CanWrite)
                Assert.Throws(Of NotSupportedException)(Sub() stream.Write(New Byte(9) {}, 0, 10))
                Assert.Throws(Of NotSupportedException)(Sub() stream.SetLength(10))
            End Using
        End Using
    End Sub

End Class
#End If
