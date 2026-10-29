#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

' A read lease belongs to a worker, not to the form. Retiring a session cannot
' delete a copy still being read, and no reader holds the original file open.
Friend NotInheritable Class DocumentSnapshot
    Implements IDisposable
    Private ReadOnly gate As New Object()
    Private ReadOnly file As String
    Private readers As Integer
    Private retired As Boolean
    Public ReadOnly Property Identity As DocumentIdentity

    Private Sub New(path As String, source As DocumentIdentity)
        file = path
        Identity = source
    End Sub

    Public Shared Async Function CreateAsync(path As String, provenance As String, token As CancellationToken) As Task(Of DocumentSnapshot)
        Dim folder = IO.Path.Combine(IO.Path.GetTempPath(), "fms-document-sessions")
        Directory.CreateDirectory(folder)
        Dim copy = IO.Path.Combine(folder, Guid.NewGuid().ToString("N") & ".epub")
        Try
            Dim before As New FileInfo(path)
            Dim identity As New DocumentIdentity With {.Path = path, .Length = before.Length, .LastWriteUtcTicks = before.LastWriteTimeUtc.Ticks, .Provenance = provenance}
            If identity.Length > 1024L * 1024 * 1024 Then Throw New InvalidDataException("Document exceeds the session snapshot limit.")
            Using input As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync:=True),
                  output As New FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync:=True)
                Dim buffer(65535) As Byte
                Dim total As Long
                Do
                    Dim count = Await input.ReadAsync(buffer.AsMemory(), token)
                    If count = 0 Then Exit Do
                    total += count
                    If total > identity.Length Then Throw New IOException("Source changed while copying.")
                    Await output.WriteAsync(buffer.AsMemory(0, count), token)
                Loop
                If total <> identity.Length Then Throw New IOException("Source changed while copying.")
            End Using
            before.Refresh()
            If before.Length <> identity.Length OrElse before.LastWriteTimeUtc.Ticks <> identity.LastWriteUtcTicks Then Throw New IOException("Source changed while copying.")
            token.ThrowIfCancellationRequested()
            Return New DocumentSnapshot(copy, identity)
        Catch
            IO.File.Delete(copy)
            Throw
        End Try
    End Function

    Public Function Read(Of T)(action As Func(Of Stream, T), token As CancellationToken) As T
        SyncLock gate
            token.ThrowIfCancellationRequested()
            If retired Then Throw New OperationCanceledException(token)
            readers += 1
        End SyncLock
        Try
            Using input As New FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)
                Return action(input)
            End Using
        Finally
            SyncLock gate
                readers -= 1
                If retired AndAlso readers = 0 Then DeleteCopy()
            End SyncLock
        End Try
    End Function

    Private Sub DeleteCopy()
        Try
            IO.File.Delete(file)
        Catch ex As IOException
        Catch ex As UnauthorizedAccessException
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock gate
            retired = True
            If readers = 0 Then DeleteCopy()
        End SyncLock
    End Sub
End Class
#End If
