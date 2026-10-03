Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports Xunit

Public Class AppendOnlyFileStreamTests
    <Fact>
    Public Sub WriteRejectsInvalidRangesAndDisposedStreams()
        Dim logPath As String = Path.Combine(Path.GetTempPath(), "fms-s13-" & Guid.NewGuid().ToString("N") & ".log")
        Try
            Using stream As Stream = AppFileLogger.OpenAppendOnly(logPath)
                Dim bytes As Byte() = {10, 20, 30, 40}
                Assert.Throws(Of ArgumentNullException)(Sub() stream.Write(Nothing, 0, 0))
                Assert.Throws(Of ArgumentOutOfRangeException)(Sub() stream.Write(bytes, -1, 1))
                Assert.Throws(Of ArgumentOutOfRangeException)(Sub() stream.Write(bytes, 0, -1))
                Assert.Throws(Of ArgumentException)(Sub() stream.Write(bytes, 3, 2))
                Assert.Throws(Of ArgumentException)(Sub() stream.Write(bytes, Integer.MaxValue, 1))
                Assert.Throws(Of ArgumentException)(Sub() stream.Write(bytes, 0, Integer.MaxValue))
                stream.Write(bytes, 1, 2)
                stream.Write(bytes, bytes.Length, 0)
                stream.Dispose()
                Assert.Equal(New Byte() {20, 30}, File.ReadAllBytes(logPath))
                Assert.False(stream.CanWrite)
                Assert.Throws(Of ObjectDisposedException)(Sub() stream.Write(bytes, 0, 0))
            End Using
        Finally
            File.Delete(logPath)
        End Try
    End Sub
End Class
