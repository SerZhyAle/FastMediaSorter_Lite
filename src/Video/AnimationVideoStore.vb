#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Threading

' The encrypted temp store of the animation videos
' (242_SPECIFICATION_ANIMATED_GRAPHICS_THROUGH_TEMPORARY_ENCRYPTED_VIDEO.md section 3.5,
' rules 16-19, step T-3). Two halves in one file:
'   * AnimationVideoStoreRules - the pure rules: names, the per-run credential, the sweep decision;
'   * AnimationVideoStore / AnimationVideoLease - the mechanics: a folder per run, a sealed container per
'     video held by a DeleteOnClose handle, the release order, the sweep.
' Like src/Secure it has NO product dependencies: it never logs by itself (a caller-supplied hook gets
' plain sentences without the credential or the sealed name), never reads a setting and never asks which
' process is a viewer (the sweep takes that as a function). Which lease is current and which is ahead, and
' when one is released, is the viewer's retention policy (step T-7) and is not decided here.

''' <summary>
''' The decisions of the temp store that need no disk: how a run folder and a container are named, what the
''' per-run credential looks like, and which folders the start-up sweep may remove.
''' </summary>
Friend Module AnimationVideoStoreRules

    ''' <summary>The folder under the user's temp folder that holds every run folder (rule 17).</summary>
    Friend Const RootFolderName As String = "FastMediaSorter-av"

    ''' <summary>The true name sealed inside every container (rule 17): neutral, nothing of the original's name.</summary>
    Friend Const SealedTrueName As String = "animation.mp4"

    ''' <summary>A run folder older than this goes even when a live process claims its pid: Windows reuses
    ''' pids, the same reasoning as <c>ArchiveTempStore.Stale_After_Hours</c> (rule 18).</summary>
    Friend Const Stale_After_Hours As Double = 24.0

    ''' <summary>Random bytes behind a container's file name: 16, written as 32 lower-case hex digits.</summary>
    Friend Const ContainerNameBytes As Integer = 16

    ''' <summary>Random bytes behind a run folder's token: 8, written as 16 lower-case hex digits.</summary>
    Friend Const TokenBytes As Integer = 8

    ''' <summary>Random bytes behind the per-run credential. Written as hex that is 128 UTF-8 bytes, well past
    ''' the format's 64-byte threshold, so the cheap salted-BLAKE2b key derivation applies (rule 16).</summary>
    Friend Const CredentialRandomBytes As Integer = 64

    ''' <summary>The format's threshold for the fast key-derivation branch (FDSEC-FORMAT section 6).</summary>
    Friend Const CredentialMinUtf8Bytes As Integer = 64

    ''' <summary>The default root: <c>%TEMP%\FastMediaSorter-av</c>, from the temp folder the caller names.</summary>
    Friend Function DefaultRoot(tempFolder As String) As String
        Return Path.Combine(tempFolder, RootFolderName)
    End Function

    ''' <summary>A run folder's name: <c>&lt;pid&gt;-&lt;token&gt;</c>.</summary>
    Friend Function FolderName(processId As Integer, token As String) As String
        Return processId.ToString(CultureInfo.InvariantCulture) & "-" & token
    End Function

    ''' <summary>A fresh token for a run folder, from the operating system's CSPRNG.</summary>
    Friend Function NewToken() As String
        Return RandomHex(TokenBytes)
    End Function

    ''' <summary>
    ''' The pid and the token of a run folder name, or False when the name is not exactly one of ours. The
    ''' parse is strict - decimal pid without a sign or leading zeros, a token of exactly 16 lower-case hex
    ''' digits - because the sweep removes whatever this accepts and nothing else.
    ''' </summary>
    Friend Function TryParseFolderName(name As String, ByRef processId As Integer, ByRef token As String) As Boolean
        processId = 0
        token = ""
        If String.IsNullOrEmpty(name) Then Return False

        Dim separator As Integer = name.IndexOf("-"c)
        If separator <= 0 OrElse separator = name.Length - 1 Then Return False

        Dim parsedPid As Integer
        If Not Integer.TryParse(name.Substring(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, parsedPid) Then Return False
        If parsedPid <= 0 Then Return False

        Dim parsedToken As String = name.Substring(separator + 1)
        If Not IsLowerHex(parsedToken, TokenBytes * 2) Then Return False
        ' Canonical form only: "007-.." parses to pid 7 but is not a name this program ever wrote.
        If Not String.Equals(name, FolderName(parsedPid, parsedToken), StringComparison.Ordinal) Then Return False

        processId = parsedPid
        token = parsedToken
        Return True
    End Function

    ''' <summary>A container's file name: 16 random bytes as lower-case hex, NO extension, nothing from the
    ''' original's name (rule 17).</summary>
    Friend Function NewContainerFileName() As String
        Return RandomHex(ContainerNameBytes)
    End Function

    ''' <summary>True for a name <see cref="NewContainerFileName"/> could have produced.</summary>
    Friend Function IsContainerFileName(name As String) As Boolean
        Return IsLowerHex(name, ContainerNameBytes * 2)
    End Function

    ''' <summary>
    ''' A fresh per-run credential: 64 CSPRNG bytes as lower-case hex (128 characters, 128 UTF-8 bytes). The text
    ''' goes straight into an <c>FdSecCredential</c> held by the store instance; it is held in memory only and is
    ''' never logged, persisted or serialised (rule 16). Stated plainly: the text itself is an immutable String (and
    ''' the hex conversion makes one more) that cannot be wiped, so it lives in the heap until it is collected; the
    ''' <c>FdSecCredential</c> is what is wiped on dispose. Best effort on a managed runtime (FDSEC-BEHAVIOUR 8.2).
    ''' </summary>
    Friend Function NewCredentialText() As String
        Return RandomHex(CredentialRandomBytes)
    End Function

    ''' <summary>
    ''' Is this run folder one the start-up sweep may remove? Only a name that parses is ever judged ("not ours"
    ''' is never swept); then yes when the owning pid is not a running viewer, or when the folder is a day old
    ''' (rule 18). <paramref name="isViewerPid"/> is the caller's question to the operating system.
    ''' </summary>
    Friend Function ShouldSweep(folderName As String, lastWriteUtc As Date, nowUtc As Date,
                                isViewerPid As Func(Of Integer, Boolean)) As Boolean
        Dim owner As Integer
        Dim token As String = ""
        If Not TryParseFolderName(folderName, owner, token) Then Return False
        If (nowUtc - lastWriteUtc).TotalHours >= Stale_After_Hours Then Return True
        If isViewerPid Is Nothing Then Return False
        Return Not isViewerPid(owner)
    End Function

    Private Function RandomHex(byteCount As Integer) As String
        Dim bytes(byteCount - 1) As Byte
        RandomNumberGenerator.Fill(bytes)
        Try
            Return Convert.ToHexString(bytes).ToLowerInvariant()
        Finally
            Array.Clear(bytes, 0, bytes.Length)
        End Try
    End Function

    Private Function IsLowerHex(text As String, length As Integer) As Boolean
        If text Is Nothing OrElse text.Length <> length Then Return False
        For Each c As Char In text
            If Not ((c >= "0"c AndAlso c <= "9"c) OrElse (c >= "a"c AndAlso c <= "f"c)) Then Return False
        Next
        Return True
    End Function

End Module

''' <summary>
''' The store of one process run: a folder <c>&lt;root&gt;\&lt;pid&gt;-&lt;token&gt;\</c> created on first use, one
''' sealed container per animation video, and the one credential every container of the run is sealed under.
'''
''' The operating system, not our diligence, deletes a container: the writer's handle is opened
''' <c>DeleteOnClose</c> and stays open for the container's whole life (rule 18), so a killed or crashed viewer
''' leaves no file; the start-up <see cref="Sweep"/> covers power loss.
'''
''' The credential lives in a field of the instance only (rule 16). After a crash or kill the leftover container
''' can be opened by nobody, the next run of this program included.
''' </summary>
Friend NotInheritable Class AnimationVideoStore
    Implements IDisposable

    ''' <summary>The mechanical ceiling: the file on screen and one prepared ahead (rule 20). Which one is which,
    ''' and when to release, is the viewer's policy.</summary>
    Friend Const MaxLeases As Integer = 2

    ''' <summary>The test seam: how the container's bytes are produced. The product always uses
    ''' <see cref="FdSecWriter"/> sealing under <see cref="AnimationVideoStoreRules.SealedTrueName"/> (rule 25: the
    ''' viewer's only writer into the store is the sealing writer); a replacement exists for tests only, and is
    ''' reachable only through <see cref="SealSeam"/> and the constructor that takes it.</summary>
    Friend Delegate Function SealWriter(plaintext As ReadOnlyMemory(Of Byte), destination As Stream,
                                        credential As FdSecCredential, cancellationToken As CancellationToken) As Long

    ''' <summary>
    ''' TEST-ONLY wrapper that hands a <see cref="SealWriter"/> to the seam constructor. It is a class (not a bare
    ''' delegate) so that any use is spelled out by name: a source scan (<c>AnimationVideoStoreTests</c>) holds that
    ''' neither <c>SealSeam</c> nor <c>SealWriter</c> appears in any file under <c>src\</c> except this one - a
    ''' writer that sees the raw <c>DeleteOnClose</c> stream AND the plaintext is a route to plaintext on disk.
    ''' </summary>
    Friend NotInheritable Class SealSeam
        Friend ReadOnly Writer As SealWriter

        Friend Sub New(writer As SealWriter)
            If writer Is Nothing Then Throw New ArgumentNullException(NameOf(writer))
            Me.Writer = writer
        End Sub
    End Class

    Private ReadOnly runFolderPath As String
    Private ReadOnly logHook As Action(Of String)
    Private ReadOnly credential As FdSecCredential
    Private ReadOnly writeContainer As SealWriter
    Private ReadOnly gate As New Object()
    Private ReadOnly leases As New List(Of AnimationVideoLease)()
    ''' <summary>Leases held plus seals in progress, so the ceiling holds while a container is still being written.</summary>
    Private reserved As Integer
    Private disposed As Boolean

    ''' <param name="root">The folder that holds the run folders (<c>%TEMP%\FastMediaSorter-av</c> by default,
    ''' supplied by the caller). Not created here.</param>
    ''' <param name="processId">The pid of the running viewer, written into the run folder's name.</param>
    ''' <param name="logHook">Optional; receives short sentences, never the credential or a sealed name.</param>
    Friend Sub New(root As String, processId As Integer,
                   Optional logHook As Action(Of String) = Nothing)
        Me.New(root, processId, logHook, Nothing)
    End Sub

    ''' <summary>TEST-ONLY constructor (see <see cref="SealSeam"/>): the container is produced by the writer the seam
    ''' carries. The production constructor above takes no writer and always seals through
    ''' <see cref="FdSecWriter"/>.</summary>
    Friend Sub New(root As String, processId As Integer, logHook As Action(Of String), seam As SealSeam)
        If String.IsNullOrWhiteSpace(root) Then Throw New ArgumentException("the store needs a root folder", NameOf(root))
        If processId <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(processId))
        runFolderPath = Path.Combine(root, AnimationVideoStoreRules.FolderName(processId, AnimationVideoStoreRules.NewToken()))
        Me.logHook = logHook
        credential = FdSecCredential.FromText(AnimationVideoStoreRules.NewCredentialText())
        If seam Is Nothing Then
            writeContainer = AddressOf DefaultWrite
        Else
            writeContainer = seam.Writer
        End If
    End Sub

    ''' <summary>This run's folder. It exists only while something is sealed in it.</summary>
    Friend ReadOnly Property RunFolder As String
        Get
            Return runFolderPath
        End Get
    End Property

    ''' <summary>How many containers are held right now.</summary>
    Friend ReadOnly Property LiveCount As Integer
        Get
            SyncLock gate
                Return leases.Count
            End SyncLock
        End Get
    End Property

    Private Shared Function DefaultWrite(plaintext As ReadOnlyMemory(Of Byte), destination As Stream,
                                         credential As FdSecCredential, cancellationToken As CancellationToken) As Long
        Return FdSecWriter.Write(plaintext, AnimationVideoStoreRules.SealedTrueName, credential, destination,
                                 cancellationToken)
    End Function

    ''' <summary>
    ''' Seals <paramref name="mp4"/> into a new container and returns the lease that holds it. The file is created
    ''' <c>CreateNew</c> with <c>DeleteOnClose</c> and <c>FileShare.ReadWrite Or Delete</c>, written through the
    ''' sealing writer and flushed, then READ BACK through the real reader (open, <c>VerifyAll</c>, size, name and
    ''' the digest of the plaintext) while the writer's handle is still held. Any failure closes everything made, so
    ''' the operating system removes the file, and throws; a cancelled token is an
    ''' <see cref="OperationCanceledException"/>. The plaintext is neither copied nor kept.
    ''' </summary>
    ''' <exception cref="InvalidOperationException">The ceiling of <see cref="MaxLeases"/> held containers is reached.</exception>
    Friend Function Seal(mp4 As ReadOnlyMemory(Of Byte),
                         Optional cancellationToken As CancellationToken = Nothing) As AnimationVideoLease
        cancellationToken.ThrowIfCancellationRequested()
        If mp4.Length = 0 Then Throw New ArgumentException("an empty video cannot be sealed", NameOf(mp4))

        SyncLock gate
            If disposed Then Throw New ObjectDisposedException(NameOf(AnimationVideoStore))
            If reserved >= MaxLeases Then
                Throw New InvalidOperationException("the store already holds " & MaxLeases.ToString(CultureInfo.InvariantCulture) & " containers")
            End If
            reserved += 1
        End SyncLock

        Dim containerFile As String = Nothing
        Dim writerHandle As FileStream = Nothing
        Dim container As FdSecContainer = Nothing
        Dim handedOver As Boolean = False
        Try
            ' Creating the file happens under the gate so a ReleaseAll cannot remove the run folder between
            ' "make the folder" and "make the file" (it would find the folder non-empty and leave it).
            SyncLock gate
                If disposed Then Throw New ObjectDisposedException(NameOf(AnimationVideoStore))
                Directory.CreateDirectory(runFolderPath)
                containerFile = Path.Combine(runFolderPath, AnimationVideoStoreRules.NewContainerFileName())
                writerHandle = New FileStream(containerFile, FileMode.CreateNew, FileAccess.Write,
                                              FileShare.ReadWrite Or FileShare.Delete, 4096, FileOptions.DeleteOnClose)
            End SyncLock

            writeContainer(mp4, writerHandle, credential, cancellationToken)
            writerHandle.Flush()

            container = FdSecContainer.OpenFile(containerFile, credential)
            container.VerifyAll(cancellationToken)
            ConfirmReadBack(container, mp4)

            Dim lease As New AnimationVideoLease(Me, containerFile, writerHandle, container, mp4.Length)
            SyncLock gate
                If disposed Then Throw New ObjectDisposedException(NameOf(AnimationVideoStore))
                leases.Add(lease)
            End SyncLock
            handedOver = True
            Return lease
        Catch ex As Exception
            If Not TypeOf ex Is OperationCanceledException Then
                Log("Animation video store: sealing failed: " & ex.GetType().Name)
            End If
            Throw
        Finally
            If Not handedOver Then
                ' The reader's handle first, the writer's last: the writer's close is what deletes the file.
                Quietly(container)
                Quietly(writerHandle)
                ' Nothing else held and no other seal under way: the folder this attempt made must not outlive it
                ' (rule 27, "leaves nothing"). Under the gate, so a concurrent Seal cannot be between "make the
                ' folder" and "make the file"; the log line is written after the gate is let go.
                Dim leftover As String = Nothing
                SyncLock gate
                    reserved -= 1
                    If reserved = 0 Then leftover = RemoveRunFolderIfEmpty()
                End SyncLock
                If leftover IsNot Nothing Then Log(leftover)
            End If
        End Try
    End Function

    ''' <summary>Removes the run folder when it is empty. The caller holds <c>gate</c>. Returns the sentence to log
    ''' when the folder stayed (not empty - a scanner still holds a closed file - or refused), Nothing otherwise; the
    ''' caller logs it AFTER leaving the lock, because a log hook is foreign code (a hook that waits for the UI thread
    ''' while the UI thread waits for the gate would deadlock).</summary>
    Private Function RemoveRunFolderIfEmpty() As String
        Try
            If Directory.Exists(runFolderPath) Then Directory.Delete(runFolderPath, False)
            Return Nothing
        Catch ex As Exception
            ' The next run's sweep takes it.
            Return "Animation video store: the run folder was not removed: " & Describe(ex)
        End Try
    End Function

    ''' <summary>Releases every held container (rule 19 order inside each lease) and removes the now empty run
    ''' folder. The store stays usable: a later <see cref="Seal"/> makes the folder again. Safe to call twice.</summary>
    Friend Sub ReleaseAll()
        Dim snapshot As AnimationVideoLease()
        SyncLock gate
            snapshot = leases.ToArray()
        End SyncLock
        For Each lease As AnimationVideoLease In snapshot
            lease.Dispose()
        Next
        Dim leftover As String
        SyncLock gate
            leftover = RemoveRunFolderIfEmpty()
        End SyncLock
        If leftover IsNot Nothing Then Log(leftover)
    End Sub

    ''' <summary>The viewer exit: releases everything, refuses further seals and puts the credential down.</summary>
    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock gate
            If disposed Then Return
            disposed = True
        End SyncLock
        ReleaseAll()
        credential.Dispose()
    End Sub

    ''' <summary>Called by a lease that has been disposed.</summary>
    Friend Sub Forget(lease As AnimationVideoLease)
        SyncLock gate
            If leases.Remove(lease) Then reserved -= 1
        End SyncLock
    End Sub

    Friend Sub Log(message As String)
        LogTo(logHook, message)
    End Sub

    Private Shared Sub LogTo(hook As Action(Of String), message As String)
        If hook Is Nothing Then Return
        Try
            hook(message)
        Catch
            ' A logging hook must never break the store.
        End Try
    End Sub

    ''' <summary>The exception's type and HRESULT; never its message, which can carry a path.</summary>
    Private Shared Function Describe(ex As Exception) As String
        Return ex.GetType().Name & " 0x" & ex.HResult.ToString("X8", CultureInfo.InvariantCulture)
    End Function

    Private Shared Sub Quietly(target As IDisposable)
        If target Is Nothing Then Return
        Try
            target.Dispose()
        Catch
            ' Closing a half-made container must not hide the failure that made it half-made.
        End Try
    End Sub

    ''' <summary>
    ''' What the reader recovered must be what was sealed: the real size, the neutral name and the BLAKE2b-256 of
    ''' the plaintext. <c>VerifyAll</c> alone proves the chunks match the digest the writer sealed, not that the
    ''' writer sealed the right bytes.
    ''' </summary>
    Private Shared Sub ConfirmReadBack(container As FdSecContainer, mp4 As ReadOnlyMemory(Of Byte))
        If container.ContentLength <> mp4.Length Then Throw New InvalidDataException("the read-back size differs from the video")
        If Not String.Equals(container.Metadata.Name, AnimationVideoStoreRules.SealedTrueName, StringComparison.Ordinal) Then
            Throw New InvalidDataException("the read-back name differs from the sealed one")
        End If
        Dim expected As Byte() = DigestOf(mp4)
        If Not CryptographicOperations.FixedTimeEquals(expected, container.Metadata.Digest) Then
            Throw New InvalidDataException("the read-back digest differs from the video")
        End If
    End Sub

    ''' <summary>BLAKE2b-256 of the plaintext where it lies; an array-backed memory costs no copy.</summary>
    Private Shared Function DigestOf(plaintext As ReadOnlyMemory(Of Byte)) As Byte()
        Dim segment As ArraySegment(Of Byte) = Nothing
        If MemoryMarshal.TryGetArray(plaintext, segment) Then
            Return FdSecPrimitives.Blake2b256(segment.Array, segment.Offset, segment.Count)
        End If
        Dim copy As Byte() = plaintext.ToArray()
        Try
            Return FdSecPrimitives.Blake2b256(copy, 0, copy.Length)
        Finally
            FdSecPrimitives.Wipe(copy)
        End Try
    End Function

    ''' <summary>
    ''' The start-up sweep (rule 18): removes every run folder under <paramref name="root"/> that
    ''' <see cref="AnimationVideoStoreRules.ShouldSweep"/> condemns and returns how many it removed. It judges by
    ''' the folder's NAME and its last-write time only and deletes by the enumerated path - it never opens, lists
    ''' or reads a file inside. A folder that refuses to go (a handle still held) stays for the next run; a
    ''' junction or symlink is unlinked, never followed. One folder's failure never stops the rest.
    ''' </summary>
    Friend Shared Function Sweep(root As String, nowUtc As Date, isViewerPid As Func(Of Integer, Boolean),
                                 Optional logHook As Action(Of String) = Nothing) As Integer
        If isViewerPid Is Nothing Then Throw New ArgumentNullException(NameOf(isViewerPid))
        If String.IsNullOrEmpty(root) Then Return 0

        Dim candidates As New List(Of String)()
        Try
            If Not Directory.Exists(root) Then Return 0
            candidates.AddRange(Directory.EnumerateDirectories(root))
        Catch ex As Exception
            LogTo(logHook, "Animation video store: the sweep could not list the root: " & Describe(ex))
            Return 0
        End Try

        Dim removed As Integer = 0
        For Each candidate As String In candidates
            Try
                Dim name As String = System.IO.Path.GetFileName(candidate)
                If Not AnimationVideoStoreRules.ShouldSweep(name, Directory.GetLastWriteTimeUtc(candidate), nowUtc, isViewerPid) Then
                    Continue For
                End If
                Dim isLink As Boolean = (File.GetAttributes(candidate) And FileAttributes.ReparsePoint) <> 0
                Directory.Delete(candidate, Not isLink)
                removed += 1
            Catch ex As Exception
                LogTo(logHook, "Animation video store: a stale folder was not removed: " & Describe(ex))
            End Try
        Next
        If removed > 0 Then
            LogTo(logHook, "Animation video store: swept " & removed.ToString(CultureInfo.InvariantCulture) & " stale folder(s)")
        End If
        Return removed
    End Function

End Class

''' <summary>
''' One sealed container held open: the writer's <c>DeleteOnClose</c> handle, the reader over the same file and
''' the playback streams it handed out. <see cref="Dispose"/> releases in rule 19's order, after the CALLER has
''' already stopped the player and disposed its <c>Media</c> and <c>StreamMediaInput</c>: the streams this lease
''' created, then the container, then the writer's handle - the last close is what deletes the file.
''' </summary>
Friend NotInheritable Class AnimationVideoLease
    Implements IDisposable

    Private ReadOnly owner As AnimationVideoStore
    Private ReadOnly containerPath As String
    Private ReadOnly writerHandle As FileStream
    Private ReadOnly container As FdSecContainer
    Private ReadOnly streams As New List(Of Stream)()
    Private ReadOnly gate As New Object()
    Private disposed As Boolean

    Friend Sub New(owner As AnimationVideoStore, containerPath As String, writerHandle As FileStream,
                   container As FdSecContainer, length As Long)
        Me.owner = owner
        Me.containerPath = containerPath
        Me.writerHandle = writerHandle
        Me.container = container
        Me.Length = length
    End Sub

    ''' <summary>The video's size in bytes: the playback stream's <c>Length</c>.</summary>
    Friend ReadOnly Property Length As Long

    Friend ReadOnly Property IsDisposed As Boolean
        Get
            SyncLock gate
                Return disposed
            End SyncLock
        End Get
    End Property

    ''' <summary>
    ''' A new seekable, read-only stream over the decrypted video (every byte authenticated). Each call returns its
    ''' own stream; the lease disposes any the caller left open.
    ''' </summary>
    Friend Function OpenPlaybackStream() As Stream
        SyncLock gate
            If disposed Then Throw New ObjectDisposedException(NameOf(AnimationVideoLease))
            Dim stream As Stream = container.OpenContentStream()
            streams.Add(stream)
            Return stream
        End SyncLock
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Dim opened As Stream()
        SyncLock gate
            If disposed Then Return
            disposed = True
            opened = streams.ToArray()
            streams.Clear()
        End SyncLock

        For Each stream As Stream In opened
            Quietly(stream)
        Next
        Quietly(container)
        Quietly(writerHandle)
        owner.Forget(Me)

        Try
            If File.Exists(containerPath) Then owner.Log("Animation video store: a container outlived its release")
        Catch
        End Try
    End Sub

    Private Shared Sub Quietly(target As IDisposable)
        If target Is Nothing Then Return
        Try
            target.Dispose()
        Catch
            ' Each step of the release must run whatever the one before did.
        End Try
    End Sub

End Class
#End If
