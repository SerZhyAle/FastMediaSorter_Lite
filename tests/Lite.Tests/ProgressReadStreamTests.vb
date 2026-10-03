#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Threading.Tasks
Imports Xunit

Public Class ProgressReadStreamTests

    <Fact>
    Public Sub ProgressReadStream_Tracks_Bytes_And_Timestamp()
        Dim sampleData As Byte() = New Byte() {1, 2, 3, 4, 5, 6, 7, 8}
        Dim ms As New MemoryStream(sampleData)

        Dim progressStream As New ProgressReadStream(ms)
        Assert.Equal(0L, progressStream.TotalBytesRead)
        Assert.False(progressStream.CanSeek)
        Assert.False(progressStream.CanWrite)
        Assert.True(progressStream.CanRead)

        Dim buf(3) As Byte
        Dim bytesReadCount As Integer = 0
        AddHandler progressStream.BytesRead, Sub(c) bytesReadCount += c

        Dim read1 As Integer = progressStream.Read(buf, 0, 4)
        Assert.Equal(4, read1)
        Assert.Equal(4L, progressStream.TotalBytesRead)
        Assert.Equal(4, bytesReadCount)

        Dim read2 As Integer = progressStream.Read(buf, 0, 4)
        Assert.Equal(4, read2)
        Assert.Equal(8L, progressStream.TotalBytesRead)
        Assert.Equal(8, bytesReadCount)

        progressStream.Dispose()
        Assert.False(progressStream.CanRead)
    End Sub

    <Fact>
    Public Async Function ProgressReadStream_ReadAsync_Works() As Task
        Dim sampleData As Byte() = New Byte() {10, 20, 30}
        Dim ms As New MemoryStream(sampleData)

        Dim progressStream As New ProgressReadStream(ms)
        Dim buf(9) As Byte

        Dim readCount As Integer = Await progressStream.ReadAsync(buf, 0, buf.Length)
        Assert.Equal(3, readCount)
        Assert.Equal(3L, progressStream.TotalBytesRead)

        progressStream.Dispose()
    End Function

End Class
#End If
