#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Threading

''' <summary>
''' One open container: the head, the unwrapped file key, the sealed metadata and the
''' chunk geometry (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §5.1).
'''
''' This class implements <c>unsecure</c> ONLY, and only in the streaming shape of §4.2.
''' It knows nothing about this application: no logging, no dialog, no setting, no path
''' outside the container it was handed. Its public surface takes a credential and
''' returns an outcome, which is what lets the deferred writer, a future command-line
''' tool and the tests all call the same code (FDSEC-BEHAVIOUR §13.3).
'''
''' What it deliberately does NOT do is claim the container is intact. The contract's
''' reader verifies the sealed digest of the original at the end of a full sequential
''' read; a viewer hands a seekable stream to a decoder and a video player reads what it
''' needs, in the order it needs it. Every byte the stream returns IS authenticated - per
''' chunk, with the index, the count, the last-chunk flag and the length bound into the
''' associated data - so substitution, reordering, splicing and truncation are all caught
''' on the bytes we touch. What is given up, stated plainly: a chunk we never read could
''' be damaged and we would not say so. <see cref="VerifyAll"/> is the full guarantee,
''' offered as an explicit action rather than paid for on every open.
''' </summary>
Public NotInheritable Class FdSecContainer
    Implements IDisposable

    Private ReadOnly source As Stream
    ''' <summary>Guards the one thing every reader of this container shares: the source's
    ''' file position. Several content streams over one container is the normal case, not
    ''' an edge one - a video and its own metadata probe are two.</summary>
    Private ReadOnly sourceGate As New Object()
    Private ReadOnly ownsSource As Boolean
    Private ReadOnly head As FdSecHead
    Private ReadOnly fileKey As Byte()
    Private ReadOnly totalLength As Long
    Private disposed As Boolean

    ''' <summary>The sealed metadata: true name, real size, timestamps, digest.</summary>
    Public ReadOnly Property Metadata As FdSecMetadata

    Private Sub New(source As Stream, ownsSource As Boolean, head As FdSecHead, fileKey As Byte(),
                    metadata As FdSecMetadata, totalLength As Long)
        Me.source = source
        Me.ownsSource = ownsSource
        Me.head = head
        Me.fileKey = fileKey
        Me.Metadata = metadata
        Me.totalLength = totalLength
    End Sub

    ''' <summary>
    ''' Opens the container at <paramref name="path"/>. Throws
    ''' <see cref="FdSecException"/> with the outcome class, and nothing else: an I/O
    ''' failure comes out as its own <see cref="IOException"/>, because "the share went
    ''' away" is not one of the three classes and must never be dressed up as one.
    '''
    ''' The file is opened sharing read, write and DELETE on purpose: the container is an
    ''' ordinary file and the user's move, rename and delete must keep working on it
    ''' while it is on screen (§7).
    ''' </summary>
    Public Shared Function OpenFile(path As String, credential As String,
                                    Optional profile As FdSecProfile = Nothing,
                                    Optional keyCache As FdSecKeyCache = Nothing) As FdSecContainer
        Dim stream As FileStream = New FileStream(path, FileMode.Open, FileAccess.Read,
                                                  FileShare.ReadWrite Or FileShare.Delete)
        Try
            Return Open(stream, credential, ownsSource:=True, profile:=profile, keyCache:=keyCache)
        Catch
            stream.Dispose()
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Opens a container over an already-seekable stream. <paramref name="ownsSource"/>
    ''' decides whether disposing this object disposes the stream.
    ''' </summary>
    Public Shared Function Open(source As Stream, credential As String,
                                Optional ownsSource As Boolean = False,
                                Optional profile As FdSecProfile = Nothing,
                                Optional keyCache As FdSecKeyCache = Nothing) As FdSecContainer
        If source Is Nothing Then Throw New ArgumentNullException(NameOf(source))
        If Not source.CanSeek Then Throw New ArgumentException("a container needs a seekable source", NameOf(source))

        Dim total As Long = source.Length
        ' The length screen runs first and needs no credential at all (§4.3).
        If Not FdSecScreen.CouldBeContainer(total) Then
            Throw FdSecException.Damaged("length " & total.ToString() & " is impossible for a container")
        End If

        Return OpenCore(source, FdSecKdf.NormalizeCredential(credential), ownsSource, profile, keyCache, total)
    End Function

    ''' <summary>
    ''' The same open, for a credential held as bytes (<see cref="FdSecCredential"/>, audit
    ''' 052 G-19). A copy is derived from and wiped; the held credential is untouched.
    ''' </summary>
    Public Shared Function OpenFile(path As String, credential As FdSecCredential,
                                    Optional profile As FdSecProfile = Nothing,
                                    Optional keyCache As FdSecKeyCache = Nothing) As FdSecContainer
        If credential Is Nothing Then Throw New ArgumentNullException(NameOf(credential))
        Dim stream As FileStream = New FileStream(path, FileMode.Open, FileAccess.Read,
                                                  FileShare.ReadWrite Or FileShare.Delete)
        Try
            Return Open(stream, credential, ownsSource:=True, profile:=profile, keyCache:=keyCache)
        Catch
            stream.Dispose()
            Throw
        End Try
    End Function

    Public Shared Function Open(source As Stream, credential As FdSecCredential,
                                Optional ownsSource As Boolean = False,
                                Optional profile As FdSecProfile = Nothing,
                                Optional keyCache As FdSecKeyCache = Nothing) As FdSecContainer
        If source Is Nothing Then Throw New ArgumentNullException(NameOf(source))
        If credential Is Nothing Then Throw New ArgumentNullException(NameOf(credential))
        If Not source.CanSeek Then Throw New ArgumentException("a container needs a seekable source", NameOf(source))

        Dim total As Long = source.Length
        If Not FdSecScreen.CouldBeContainer(total) Then
            Throw FdSecException.Damaged("length " & total.ToString() & " is impossible for a container")
        End If

        Return OpenCore(source, credential.CopyBytes(), ownsSource, profile, keyCache, total)
    End Function

    ''' <summary>Takes ownership of <paramref name="credentialUtf8"/> and wipes it.</summary>
    Private Shared Function OpenCore(source As Stream, credentialUtf8 As Byte(), ownsSource As Boolean,
                                     profile As FdSecProfile, keyCache As FdSecKeyCache,
                                     total As Long) As FdSecContainer
        Dim fileKey As Byte() = Nothing
        Dim plain As Byte() = Nothing
        Dim handedOver As Boolean = False
        Try
            source.Seek(0, SeekOrigin.Begin)

            Dim head As FdSecHead = FdSecHead.Open(source, credentialUtf8, profile, keyCache, fileKey)

            Dim sealedMetadata(FdSecFormat.MetadataSize - 1) As Byte
            If Not FdSecHead.ReadExactly(source, sealedMetadata, FdSecFormat.MetadataSize) Then
                Throw FdSecException.Damaged("metadata block is unreadable")
            End If
            plain = FdSecPrimitives.XChaChaOpen(fileKey,
                                                FdSecHead.MetadataNonce(fileKey),
                                                FdSecHead.MetadataAssociatedData(head.HeaderDigest),
                                                sealedMetadata)
            If plain Is Nothing Then
                ' The credential is already proven right by key slot 0, so this is
                ' tampering or corruption - reported as indistinguishable, because it is.
                Throw FdSecException.CredentialOrTamper("metadata does not authenticate")
            End If

            Dim metadata As FdSecMetadata = FdSecMetadata.Parse(plain)

            ValidateLength(head, metadata.Size, total)
            handedOver = True
            Return New FdSecContainer(source, ownsSource, head, fileKey, metadata, total)
        Finally
            ' Every exit wipes what it holds (audit 057.S17-4): the credential, the sealed
            ' metadata's plaintext, and - unless a container took it over - the file key.
            ' A container that authenticated and then failed a later rule (damaged length,
            ' unparseable metadata) used to leave both behind in the heap.
            FdSecPrimitives.Wipe(credentialUtf8)
            FdSecPrimitives.Wipe(plain)
            If Not handedOver Then FdSecPrimitives.Wipe(fileKey)
        End Try
    End Function

    ''' <summary>
    ''' The length arithmetic of FDSEC-BEHAVIOUR §5.2 step 7: the file must be the unique
    ''' cluster-aligned length in [L_min, L_min + A). Anything else is truncation or
    ''' padding beyond one cluster, and both are DAMAGE - the credential has already
    ''' proven itself and must not be blamed here.
    ''' </summary>
    Private Shared Sub ValidateLength(head As FdSecHead, size As Long, total As Long)
        Dim k As Long = head.ChunkCount(size)
        Dim last As Long = head.LastChunkLength(size)
        ' 1.1 (§7, §11 step 7): a sealed size whose L_min - or L_min + A, the upper bound
        ' checked below - does not fit in a signed 64-bit integer is DAMAGE, decided
        ' before the multiplication that would overflow is attempted.
        Dim fixedPart As Long = head.PreLength + last + FdSecFormat.TagSize + head.ClusterAlignment
        If k > 1 AndAlso (Long.MaxValue - fixedPart) \ head.ChunkSize < k - 1 Then
            Throw FdSecException.Damaged("sealed size " & size.ToString() & " overflows the length arithmetic")
        End If
        Dim minimum As Long = head.PreLength + (k - 1) * head.ChunkSize + last + FdSecFormat.TagSize
        If total < minimum OrElse total >= minimum + head.ClusterAlignment OrElse
           total Mod head.ClusterAlignment <> 0 Then
            Throw FdSecException.Damaged("file length " & total.ToString() & " is impossible for a payload of " & size.ToString())
        End If
    End Sub

    ''' <summary>The original's real size, from the sealed metadata.</summary>
    Public ReadOnly Property ContentLength As Long
        Get
            Return Metadata.Size
        End Get
    End Property

    Friend ReadOnly Property ChunkCapacity As Long
        Get
            Return head.ChunkCapacity
        End Get
    End Property

    Friend ReadOnly Property ChunkCount As Long
        Get
            Return head.ChunkCount(Metadata.Size)
        End Get
    End Property

    ''' <summary>
    ''' Reads, authenticates and returns the plaintext of one chunk. The caller owns the
    ''' returned array and is expected to wipe it when it is done.
    '''
    ''' The SEEK AND THE READ hold the container's lock, because the file position is the
    ''' one piece of state every reader of this container shares - and LibVLC reads from
    ''' its own threads, so two of them racing here would each authenticate a chunk the
    ''' other had just seeked away from. The decryption runs outside the lock: by then
    ''' every input is a local array, and holding it would serialize the expensive part
    ''' for no reason.
    ''' </summary>
    Friend Function ReadChunk(index As Long) As Byte()
        If disposed Then Throw New ObjectDisposedException(NameOf(FdSecContainer))

        Dim count As Long = ChunkCount
        If index < 0 OrElse index >= count Then
            Throw FdSecException.Damaged("chunk " & index.ToString() & " is outside the payload")
        End If

        Dim isLast As Boolean = (index = count - 1)
        Dim plainLength As Long = If(isLast, head.LastChunkLength(Metadata.Size), head.ChunkCapacity)
        Dim want As Integer = CInt(plainLength + FdSecFormat.TagSize)

        Dim sealedChunk(want - 1) As Byte
        SyncLock sourceGate
            If disposed Then Throw New ObjectDisposedException(NameOf(FdSecContainer))
            source.Seek(head.PreLength + index * head.ChunkSize, SeekOrigin.Begin)
            If Not FdSecHead.ReadExactly(source, sealedChunk, want) Then
                Throw FdSecException.Damaged("chunk " & index.ToString() & " is unreadable")
            End If
        End SyncLock

        Dim plain As Byte() = FdSecPrimitives.XChaChaOpen(
            fileKey,
            FdSecHead.ChunkNonce(fileKey, index),
            FdSecHead.ChunkAssociatedData(head.HeaderDigest, index, count, isLast, plainLength),
            sealedChunk)
        If plain Is Nothing Then
            ' A Dispose that landed between the lock above and the open wipes fileKey under
            ' that open, and the tag then fails for a reason that is not the file's. "The
            ' reader was closed" is its own thing and must never be dressed up as tamper.
            ' Dispose sets the flag BEFORE it wipes, so a failure caused by the wipe always
            ' finds the flag set here; the read is under the same lock Dispose takes.
            SyncLock sourceGate
                If disposed Then Throw New ObjectDisposedException(NameOf(FdSecContainer))
            End SyncLock
            Throw FdSecException.CredentialOrTamper("chunk " & index.ToString() & " does not authenticate")
        End If
        Return plain
    End Function

    ''' <summary>
    ''' A seekable, read-only, decrypting view of the original. Its Length is the
    ''' original's real size, its Seek is arithmetic, and every byte it hands out has
    ''' passed its chunk's tag.
    ''' </summary>
    Public Function OpenContentStream() As Stream
        Return New FdSecStream(Me)
    End Function

    ''' <summary>
    ''' The full guarantee the streaming reader gives up: read every chunk in order and
    ''' check the recovered bytes against the digest sealed at pack time. This is what
    ''' "verify this container" runs, and it is an explicit action because on a 40 GB
    ''' container it is a 40 GB read.
    '''
    ''' Returns normally when the container is whole; throws the same three outcome
    ''' classes as everything else.
    ''' </summary>
    Public Sub VerifyAll(Optional cancellationToken As CancellationToken = Nothing,
                         Optional chunkProgress As Action(Of Long, Long) = Nothing)
        cancellationToken.ThrowIfCancellationRequested()
        Dim digest As New Org.BouncyCastle.Crypto.Digests.Blake2bDigest(256)
        Dim count As Long = ChunkCount
        For index As Long = 0 To count - 1
            cancellationToken.ThrowIfCancellationRequested()
            Dim plain As Byte() = ReadChunk(index)
            Try
                If plain.Length > 0 Then digest.BlockUpdate(plain, 0, plain.Length)
            Finally
                FdSecPrimitives.Wipe(plain)
            End Try
            chunkProgress?.Invoke(index + 1, count)
        Next
        cancellationToken.ThrowIfCancellationRequested()
        Dim recovered(FdSecFormat.DigestSize - 1) As Byte
        digest.DoFinal(recovered, 0)

        For i As Integer = 0 To FdSecFormat.DigestSize - 1
            If recovered(i) <> Metadata.Digest(i) Then
                Throw FdSecException.Damaged("recovered bytes do not hash to the packed digest")
            End If
        Next
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock sourceGate
            If disposed Then Return
            disposed = True
            FdSecPrimitives.Wipe(fileKey)
            If ownsSource Then source.Dispose()
        End SyncLock
    End Sub

End Class
#End If
