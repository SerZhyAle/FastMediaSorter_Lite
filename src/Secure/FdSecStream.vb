#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO

''' <summary>
''' The seekable, read-only, decrypting view of a container's payload
''' (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §5.2). This is what makes
''' "no plaintext on disk" cheap rather than heroic.
'''
''' The payload is a sequence of independently sealed chunks at fixed offsets with
''' nonces derived from the file key and the chunk index, which is a random-access
''' format - so this stream is ordinary:
'''
''' - Length is the ORIGINAL's real size from the sealed metadata, not the container's;
''' - Seek is arithmetic and decrypts nothing;
''' - Read opens the chunk the position falls in, authenticates it, and copies out of a
'''   small plaintext buffer, so a sequential read decrypts each chunk exactly once;
''' - one decrypted chunk is held at a time (plus the ciphertext buffer inside
'''   ReadChunk), so a 40 GB container costs about two megabytes of plaintext in memory
'''   rather than 40 GB;
''' - it is thread-safe by a lock, because LibVLC reads from its own threads.
'''
''' The buffer is zeroed when it is replaced and on disposal, as far as a
''' garbage-collected runtime allows - which is BEST EFFORT and is described that way
''' here rather than promised (FDSEC-BEHAVIOUR §8.2).
'''
''' The stream does NOT own the container: the two are disposed together by the caller
''' that opened them, because a video player holds this stream for as long as it plays.
''' </summary>
Public NotInheritable Class FdSecStream
    Inherits Stream

    Private ReadOnly container As FdSecContainer
    Private ReadOnly gate As New Object()

    Private contentPosition As Long
    Private cachedIndex As Long = -1
    Private cachedChunk As Byte()
    Private closed As Boolean

    Friend Sub New(container As FdSecContainer)
        Me.container = container
    End Sub

    Public Overrides ReadOnly Property CanRead As Boolean
        Get
            Return Not closed
        End Get
    End Property

    Public Overrides ReadOnly Property CanSeek As Boolean
        Get
            Return Not closed
        End Get
    End Property

    Public Overrides ReadOnly Property CanWrite As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overrides ReadOnly Property Length As Long
        Get
            Return container.ContentLength
        End Get
    End Property

    Public Overrides Property Position As Long
        Get
            SyncLock gate
                Return contentPosition
            End SyncLock
        End Get
        Set(value As Long)
            Seek(value, SeekOrigin.Begin)
        End Set
    End Property

    Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
        SyncLock gate
            Dim target As Long
            Select Case origin
                Case SeekOrigin.Begin : target = offset
                Case SeekOrigin.Current : target = contentPosition + offset
                Case SeekOrigin.End : target = container.ContentLength + offset
                Case Else : Throw New ArgumentOutOfRangeException(NameOf(origin))
            End Select
            If target < 0 Then Throw New IOException("cannot seek before the start of the content")
            ' Seeking past the end is legal on a Stream and simply reads nothing; VLC
            ' probes that way while it works out what it is playing.
            contentPosition = target
            Return contentPosition
        End SyncLock
    End Function

    Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
        If buffer Is Nothing Then Throw New ArgumentNullException(NameOf(buffer))
        If offset < 0 OrElse count < 0 OrElse offset + count > buffer.Length Then
            Throw New ArgumentOutOfRangeException(NameOf(count))
        End If
        If count = 0 Then Return 0

        SyncLock gate
            If closed Then Throw New ObjectDisposedException(NameOf(FdSecStream))

            Dim total As Long = container.ContentLength
            If contentPosition >= total Then Return 0

            Dim remaining As Integer = CInt(Math.Min(CLng(count), total - contentPosition))
            Dim written As Integer = 0
            While written < remaining
                Dim capacity As Long = container.ChunkCapacity
                Dim index As Long = contentPosition \ capacity
                ' Long, not Integer: the in-chunk offset is bounded by the chunk size, and
                ' converting it under checked arithmetic was an OverflowException instead of
                ' an outcome class (audit 052 G-18).
                Dim inside As Long = contentPosition Mod capacity

                Dim chunk As Byte()
                Try
                    chunk = EnsureChunk(index)
                    If inside >= chunk.Length Then
                        ' The metadata's size and the chunk geometry agree on every container
                        ' the reader accepted, so this cannot happen on an intact file - and
                        ' if it ever does, it is damage, not a short read.
                        Throw FdSecException.Damaged("chunk " & index.ToString() & " is shorter than the payload requires")
                    End If
                Catch ex As FdSecException
                    NoteFailure(ex.Outcome)
                    Throw
                End Try

                Dim take As Integer = CInt(Math.Min(CLng(remaining - written), chunk.Length - inside))
                Array.Copy(chunk, inside, buffer, CLng(offset + written), CLng(take))
                written += take
                contentPosition += take
            End While
            Return written
        End SyncLock
    End Function

    ''' <summary>
    ''' The class of the first chunk that failed, or Nothing (audit 052 G-09). A decoder
    ''' that reads through this stream - LibVLC's StreamMediaInput above all - turns an
    ''' exception into "end of data", so the outcome would otherwise be lost exactly where
    ''' the streaming reader does its authenticating. The viewer reads this, or listens to
    ''' <see cref="Failed"/>, to say "altered or damaged" rather than let playback just stop.
    ''' </summary>
    Public ReadOnly Property LastFailure As FdSecOutcome?
        Get
            SyncLock gate
                Return first_Failure
            End SyncLock
        End Get
    End Property

    ''' <summary>Raised once, on the reading thread, when the first chunk fails.</summary>
    Public Event Failed As EventHandler(Of FdSecOutcome)

    Private first_Failure As FdSecOutcome?

    ''' <summary>Caller holds the lock.</summary>
    Private Sub NoteFailure(outcome As FdSecOutcome)
        If first_Failure.HasValue Then Return
        first_Failure = outcome
        Try
            RaiseEvent Failed(Me, outcome)
        Catch
            ' A listener's failure must not replace the outcome being thrown.
        End Try
    End Sub

    ''' <summary>Caller holds the lock. Replaces the cached chunk, wiping the one it
    ''' evicts - a straddling read costs one extra decrypt and never leaves the previous
    ''' chunk's plaintext lying in the heap.</summary>
    Private Function EnsureChunk(index As Long) As Byte()
        If cachedIndex = index AndAlso cachedChunk IsNot Nothing Then Return cachedChunk
        Dim fresh As Byte() = container.ReadChunk(index)
        FdSecPrimitives.Wipe(cachedChunk)
        cachedChunk = fresh
        cachedIndex = index
        Return fresh
    End Function

    Public Overrides Sub Flush()
        ' Nothing is buffered on the way out - this stream never writes.
    End Sub

    Public Overrides Sub SetLength(value As Long)
        Throw New NotSupportedException("a container's content is read-only")
    End Sub

    Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
        Throw New NotSupportedException("a container's content is read-only")
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        SyncLock gate
            closed = True
            FdSecPrimitives.Wipe(cachedChunk)
            cachedChunk = Nothing
            cachedIndex = -1
        End SyncLock
        MyBase.Dispose(disposing)
    End Sub

End Class

''' <summary>
''' A MemoryStream that zeroes its buffer when disposed (audit 052 G-19). A container
''' image is read whole into one of these and decoded from it, and the picture box keeps
''' it alive for as long as the picture is on screen; every release path disposes it, and
''' disposal is where the plaintext goes. Best effort, as everything about wiping is in a
''' garbage-collected runtime (FDSEC-BEHAVIOUR §8.2): a buffer that had to grow left its
''' earlier, smaller copies behind, which is why the caller sizes it up front.
''' </summary>
Public NotInheritable Class FdSecWipingMemoryStream
    Inherits MemoryStream

    Public Sub New(capacity As Integer)
        MyBase.New(capacity)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            Dim held As ArraySegment(Of Byte)
            If TryGetBuffer(held) AndAlso held.Array IsNot Nothing Then
                Array.Clear(held.Array, 0, held.Array.Length)
            End If
        Catch
            ' Already disposed: the buffer was cleared the first time.
        End Try
        MyBase.Dispose(disposing)
    End Sub

End Class
#End If
