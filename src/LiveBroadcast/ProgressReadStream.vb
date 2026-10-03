#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Threading

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md §6.2.
' Non-seekable stream wrapper that tracks read progress and unblocks native reads on dispose.

Friend NotInheritable Class ProgressReadStream
    Inherits Stream

    Private ReadOnly innerStream As Stream
    Private isDisposed As Integer = 0
    Private totalBytes As Long = 0
    Private lastReadTimeUtc As DateTime = DateTime.UtcNow

    Public Event BytesRead As Action(Of Integer)

    Public Sub New(inner As Stream)
        If inner Is Nothing Then Throw New ArgumentNullException(NameOf(inner))
        innerStream = inner
        lastReadTimeUtc = DateTime.UtcNow
    End Sub

    Public ReadOnly Property TotalBytesRead As Long
        Get
            Return Interlocked.Read(totalBytes)
        End Get
    End Property

    Public ReadOnly Property LastReadTimestampUtc As DateTime
        Get
            Return lastReadTimeUtc
        End Get
    End Property

    Public Overrides ReadOnly Property CanRead As Boolean
        Get
            Return innerStream.CanRead AndAlso Interlocked.CompareExchange(isDisposed, 0, 0) = 0
        End Get
    End Property

    Public Overrides ReadOnly Property CanSeek As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overrides ReadOnly Property CanWrite As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overrides ReadOnly Property Length As Long
        Get
            Throw New NotSupportedException()
        End Get
    End Property

    Public Overrides Property Position As Long
        Get
            Return TotalBytesRead
        End Get
        Set(value As Long)
            Throw New NotSupportedException()
        End Set
    End Property

    Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
        If Interlocked.CompareExchange(isDisposed, 0, 0) <> 0 Then Return 0
        Dim bytes As Integer = 0
        Try
            bytes = innerStream.Read(buffer, offset, count)
        Catch
            Return 0
        End Try

        If bytes > 0 Then
            Interlocked.Add(totalBytes, bytes)
            lastReadTimeUtc = DateTime.UtcNow
            RaiseEvent BytesRead(bytes)
        End If
        Return bytes
    End Function

    Public Overrides Async Function ReadAsync(buffer As Byte(), offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task(Of Integer)
        If Interlocked.CompareExchange(isDisposed, 0, 0) <> 0 Then Return 0
        Dim bytes As Integer = 0
        Try
            bytes = Await innerStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(False)
        Catch
            Return 0
        End Try

        If bytes > 0 Then
            Interlocked.Add(totalBytes, bytes)
            lastReadTimeUtc = DateTime.UtcNow
            RaiseEvent BytesRead(bytes)
        End If
        Return bytes
    End Function

    Public Overrides Sub Flush()
    End Sub

    Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
        Throw New NotSupportedException()
    End Function

    Public Overrides Sub SetLength(value As Long)
        Throw New NotSupportedException()
    End Sub

    Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
        Throw New NotSupportedException()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If Interlocked.Exchange(isDisposed, 1) = 0 Then
            If disposing Then
                Try
                    innerStream.Dispose()
                Catch
                End Try
            End If
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class
#End If
