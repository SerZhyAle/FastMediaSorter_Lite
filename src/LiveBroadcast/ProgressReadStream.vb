#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Threading

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md section 6.2.
' Non-seekable stream wrapper that tracks read progress and unblocks native reads on dispose.

''' <summary>How a read-only live stream stopped delivering bytes.</summary>
Friend Enum LiveStreamEnd
    ''' <summary>The producer closed it: a clean end of the body, or the socket closed (EOF).</summary>
    Eof = 0
    ''' <summary>A read failed after the stream had started (reset, timeout of the transport).</summary>
    Reset = 1
    ''' <summary>The watchdog saw no bytes for too long and closed the response.</summary>
    Stalled = 2
    ''' <summary>This side closed it (stop, reconnect, dispose).</summary>
    Closed = 3
End Enum

Friend NotInheritable Class ProgressReadStream
    Inherits Stream

    Private ReadOnly innerStream As Stream
    Private isDisposed As Integer = 0
    Private endRaised As Integer = 0
    Private totalBytes As Long = 0
    Private lastReadTicks As Long = Environment.TickCount64
    Private readPendingSince As Long = -1L

    ''' <summary>Raised on every block that carried bytes (on the thread that read them).</summary>
    Public Event BytesRead As Action(Of Integer)

    ''' <summary>Raised ONCE when the stream ends or fails, before the read that saw it returns.</summary>
    Public Event StreamEnded As Action(Of LiveStreamEnd)

    ''' <summary>
    ''' Called with every block read, before the reader gets it. Used to count audio frames in what passes;
    ''' it must be fast and must not throw.
    ''' </summary>
    Public Property Observer As Action(Of Byte(), Integer, Integer)

    Public Sub New(inner As Stream)
        If inner Is Nothing Then Throw New ArgumentNullException(NameOf(inner))
        innerStream = inner
    End Sub

    Public ReadOnly Property TotalBytesRead As Long
        Get
            Return Interlocked.Read(totalBytes)
        End Get
    End Property

    ''' <summary>Environment.TickCount64 of the last read that returned bytes (or of the creation).</summary>
    Public ReadOnly Property LastReadTickMs As Long
        Get
            Return Interlocked.Read(lastReadTicks)
        End Get
    End Property

    ''' <summary>
    ''' Environment.TickCount64 at which the read now waiting for bytes began, or -1 when no read is waiting.
    ''' A stall is a read that waits too long - not a consumer that has not asked for bytes yet.
    ''' </summary>
    Public ReadOnly Property ReadPendingSinceTickMs As Long
        Get
            Return Interlocked.Read(readPendingSince)
        End Get
    End Property

    Public Overrides ReadOnly Property CanRead As Boolean
        Get
            Return Interlocked.CompareExchange(isDisposed, 0, 0) = 0 AndAlso innerStream.CanRead
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
        Dim bytes As Integer
        Interlocked.Exchange(readPendingSince, Environment.TickCount64)
        Try
            bytes = innerStream.Read(buffer, offset, count)
        Catch
            ' Closing the stream from another thread is how a blocked read is released: that is
            ' "Closed", not a failure of the transport.
            Interlocked.Exchange(readPendingSince, -1L)
            RaiseEnd(If(Interlocked.CompareExchange(isDisposed, 0, 0) <> 0, LiveStreamEnd.Closed, LiveStreamEnd.Reset))
            Return 0
        End Try
        Interlocked.Exchange(readPendingSince, -1L)
        Return Account(buffer, offset, bytes)
    End Function

    Public Overrides Async Function ReadAsync(buffer As Byte(), offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task(Of Integer)
        If Interlocked.CompareExchange(isDisposed, 0, 0) <> 0 Then Return 0
        Dim bytes As Integer
        Interlocked.Exchange(readPendingSince, Environment.TickCount64)
        Try
            bytes = Await innerStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(False)
        Catch
            Interlocked.Exchange(readPendingSince, -1L)
            RaiseEnd(If(Interlocked.CompareExchange(isDisposed, 0, 0) <> 0, LiveStreamEnd.Closed, LiveStreamEnd.Reset))
            Return 0
        End Try
        Interlocked.Exchange(readPendingSince, -1L)
        Return Account(buffer, offset, bytes)
    End Function

    Private Function Account(buffer As Byte(), offset As Integer, bytes As Integer) As Integer
        If bytes <= 0 Then
            RaiseEnd(LiveStreamEnd.Eof)
            Return 0
        End If

        Interlocked.Add(totalBytes, bytes)
        Interlocked.Exchange(lastReadTicks, Environment.TickCount64)
        Dim watch As Action(Of Byte(), Integer, Integer) = Observer
        If watch IsNot Nothing Then watch(buffer, offset, bytes)
        RaiseEvent BytesRead(bytes)
        Return bytes
    End Function

    Private Sub RaiseEnd(kind As LiveStreamEnd)
        If Interlocked.Exchange(endRaised, 1) = 0 Then RaiseEvent StreamEnded(kind)
    End Sub

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
            RaiseEnd(LiveStreamEnd.Closed)
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
