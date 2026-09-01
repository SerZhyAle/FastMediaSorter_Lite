#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO

''' <summary>
''' One open archive, seen as a folder (010_SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md §3.2, §5).
'''
''' The whole design in one sentence: <b>there is no virtual file system</b>. Every entry
''' is given a real path inside the session directory the moment the archive is opened,
''' the file list is filled with those paths, and the file itself appears there only when
''' something is about to show it. That is what lets the decoder, the prefetch, the
''' perspective background, OCR and LibVLC keep working on plain paths, with no idea an
''' archive is involved.
'''
''' Phase Ф1: ZIP/CBZ, random access. Solid 7z/RAR (one sequential pass) is Ф2 - the same
''' class gains a Kind, not a second implementation.
'''
''' Modern-only, like the whole feature.
''' </summary>
Friend NotInheritable Class ArchiveSession
    Implements IDisposable

    ''' <summary>
    ''' Refused for a reason worth telling the user about (§6, invariant 9). "Say what went
    ''' wrong" is the whole rule here: a picture that silently does not appear is
    ''' indistinguishable from a broken program.
    ''' </summary>
    Friend Enum EntryRefusal
        None
        ''' <summary>Bigger than the per-entry ceiling (§5.3).</summary>
        TooLarge
        ''' <summary>The declared expansion ratio says archive bomb (§6.2).</summary>
        Bomb
        ''' <summary>Encrypted - v1 refuses honestly rather than prompting (§6.4).</summary>
        Encrypted
        ''' <summary>Read failed on this entry; the session survives it (§6.5).</summary>
        Broken
    End Enum

    ''' <summary>
    ''' Per-entry ceiling (§5.3). A 512 MB picture is not a picture; a video that large in
    ''' an archive is a download, not something to preview. Ф3 makes it a setting.
    ''' </summary>
    Friend Const Default_Max_Entry_Bytes As Long = 512L * 1024L * 1024L

    ''' <summary>
    ''' Ceiling on the session directory as a whole (§5.3), the default until Ф4 wires it
    ''' to a setting. Enforced by the LRU eviction below - a budget, not a hard wall: the
    ''' entry just touched and its immediate neighbours are never evicted, so the actual
    ''' directory can sit above this for as long as those alone exceed it.
    ''' </summary>
    Friend Const Default_Max_Cache_Bytes As Long = 2048L * 1024L * 1024L

    ''' <summary>
    ''' Expansion ratio that means "bomb" (§6.2). Deliberately generous - a page of flat
    ''' colour genuinely compresses ~50:1 - and deliberately not a setting: this one is a
    ''' guard, not a preference.
    ''' </summary>
    Friend Const Max_Expansion_Ratio As Long = 200

    Private ReadOnly archive_Path As String
    Private ReadOnly temp_Root As String
    Private ReadOnly stream_Of_Archive As FileStream
    Private ReadOnly archive As SharpCompress.Archives.IArchive
    Private ReadOnly entry_Infos As New List(Of ArchiveEntryInfo)()
    Private ReadOnly entry_Handles As New List(Of SharpCompress.Archives.IArchiveEntry)()
    Private ReadOnly by_Temp_Path As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
    Private disposed As Boolean = False

    ''' <summary>
    ''' Which entries are currently extracted, oldest-used first (§5.4). Touched on every
    ''' successful <see cref="TryEnsureExtracted"/> - whether that call wrote a new file or
    ''' found one already on disk, either way the entry was just asked for, so it moves to
    ''' the fresh end and is the last thing eviction would pick.
    ''' </summary>
    Private ReadOnly lru_Recency As New List(Of Integer)()
    Private ReadOnly extracted_Sizes As New Dictionary(Of Integer, Long)()
    Private session_Bytes As Long = 0

    Friend ReadOnly Property ArchiveFilePath As String
        Get
            Return archive_Path
        End Get
    End Property

    Friend ReadOnly Property TempRoot As String
        Get
            Return temp_Root
        End Get
    End Property

    Friend ReadOnly Property Entries As IReadOnlyList(Of ArchiveEntryInfo)
        Get
            Return entry_Infos
        End Get
    End Property

    ''' <summary>
    ''' Opens an archive and takes the list of entries it will show. Nothing is extracted
    ''' here - not one byte reaches the disk until something is displayed.
    '''
    ''' <paramref name="isSupportedMedia"/> answers "would the viewer open a file with this
    ''' extension", including the user's own narrowing of the set: the archive must show
    ''' exactly what the same files in a folder would show.
    '''
    ''' Throws when the archive cannot be read at all - the caller turns that into one
    ''' honest message and a log line.
    ''' </summary>
    Friend Sub New(archivePath As String, sessionDir As String,
                   isSupportedMedia As Func(Of String, Boolean),
                   maxEntries As Integer)
        archive_Path = archivePath
        temp_Root = sessionDir

        ' ReadWrite + Delete sharing (§14, О-6): the archive stays open for the whole
        ' session, and holding it exclusively would stop the user from moving or deleting
        ' the very file they are browsing in another program. If it does vanish under us,
        ' the next entry fails honestly instead of the open failing pre-emptively.
        stream_Of_Archive = New FileStream(archivePath, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite Or FileShare.Delete)
        Try
            archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(
                stream_Of_Archive, New SharpCompress.Readers.ReaderOptions())
            BuildEntryList(isSupportedMedia, maxEntries)
        Catch
            stream_Of_Archive.Dispose()
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' True when the archive was opened but every entry in it is encrypted - the honest
    ''' "password protected" case (§6.4), which otherwise looks exactly like an empty
    ''' archive.
    ''' </summary>
    Friend ReadOnly Property IsEncrypted As Boolean
        Get
            Return encrypted_Seen AndAlso entry_Infos.Count = 0
        End Get
    End Property
    Private encrypted_Seen As Boolean = False

    ''' <summary>True when the entry list was cut at the ceiling, so the status line can
    ''' say so rather than quietly showing part of an archive (§5.3).</summary>
    Friend ReadOnly Property WasTruncated As Boolean
        Get
            Return truncated
        End Get
    End Property
    Private truncated As Boolean = False

    Private Sub BuildEntryList(isSupportedMedia As Func(Of String, Boolean), maxEntries As Integer)
        Dim index As Integer = 0
        For Each entry As SharpCompress.Archives.IArchiveEntry In archive.Entries
            Dim key As String = If(entry.Key, "")
            If entry.IsEncrypted Then
                encrypted_Seen = True
                Continue For
            End If

            Dim extension As String = ArchiveEntryFilter.ExtensionOf(key)
            If Not ArchiveEntryFilter.IsEligible(key, entry.IsDirectory, entry.Size,
                                                 extension.Length > 0 AndAlso isSupportedMedia(extension)) Then
                Continue For
            End If

            If maxEntries > 0 AndAlso entry_Infos.Count >= maxEntries Then
                truncated = True
                Exit For
            End If

            entry_Infos.Add(New ArchiveEntryInfo(
                index, key,
                ArchiveEntryFilter.LastSegmentOf(key),
                entry.Size,
                If(entry.LastModifiedTime.HasValue, entry.LastModifiedTime.Value, Date.Now),
                Path.Combine(temp_Root, ArchivePaths.EntryFileName(index, key))))
            entry_Handles.Add(entry)
            by_Temp_Path(entry_Infos(entry_Infos.Count - 1).TempPath) = entry_Infos.Count - 1
            index += 1
        Next
    End Sub

    ''' <summary>Which entry a temporary path belongs to, or -1. The file list holds those
    ''' paths, so this is how the viewer gets back from "the file on screen" to "the entry
    ''' in the archive" - for the name label and for the extraction itself.</summary>
    Friend Function IndexOfTempPath(tempPath As String) As Integer
        If String.IsNullOrEmpty(tempPath) Then Return -1
        Dim found As Integer
        If by_Temp_Path.TryGetValue(tempPath, found) Then Return found
        Return -1
    End Function

    ''' <summary>
    ''' Makes sure the entry behind <paramref name="index"/> is on disk, and says why not
    ''' when it refuses. Safe to call from a worker thread; safe to call twice.
    '''
    ''' The extraction goes to a neighbouring ".part" file and is renamed into place, so a
    ''' half-written file can never be handed to the decoder - which matters because the
    ''' prefetch and the display path can be working on the same entry at once.
    ''' </summary>
    Friend Function TryEnsureExtracted(index As Integer, ByRef refusal As EntryRefusal,
                                       Optional maxEntryBytes As Long = Default_Max_Entry_Bytes,
                                       Optional maxCacheBytes As Long = Default_Max_Cache_Bytes) As Boolean
        ' Serialized on purpose: the display path and the prefetch worker both call this,
        ' and they would otherwise be reading two entries out of ONE archive stream at the
        ' same time - SharpCompress seeks that stream per entry, so concurrent reads do not
        ' race over a lock, they race over the file position. The work inside is one
        ' entry's decompression, so the wait is bounded by the picture being fetched.
        SyncLock extraction_Gate
            Return EnsureExtractedCore(index, refusal, maxEntryBytes, maxCacheBytes)
        End SyncLock
    End Function

    Private ReadOnly extraction_Gate As New Object()

    Private Function EnsureExtractedCore(index As Integer, ByRef refusal As EntryRefusal,
                                         maxEntryBytes As Long, maxCacheBytes As Long) As Boolean
        refusal = EntryRefusal.None
        If index < 0 OrElse index >= entry_Infos.Count Then
            refusal = EntryRefusal.Broken
            Return False
        End If

        Dim info As ArchiveEntryInfo = entry_Infos(index)
        Dim entry As SharpCompress.Archives.IArchiveEntry = entry_Handles(index)

        ' Already there and the right size - the common case once a folder has been walked
        ' once, and the reason the size is checked rather than mere existence: a leftover
        ' ".part" rename that half-happened would otherwise be trusted.
        Try
            Dim onDisk As New FileInfo(info.TempPath)
            If onDisk.Exists AndAlso onDisk.Length = info.Size Then
                TouchExtracted(index, onDisk.Length)
                EvictIfOverBudget(index, maxCacheBytes)
                Return True
            End If
        Catch
            ' Fall through and extract again.
        End Try

        If info.Size > maxEntryBytes Then
            refusal = EntryRefusal.TooLarge
            Return False
        End If
        ' Checked BEFORE anything is written (§6.2): the ratio is a property of the
        ' headers, so a bomb costs us nothing but the decision.
        If entry.CompressedSize > 0 AndAlso info.Size / entry.CompressedSize > Max_Expansion_Ratio Then
            refusal = EntryRefusal.Bomb
            Return False
        End If
        If entry.IsEncrypted Then
            refusal = EntryRefusal.Encrypted
            Return False
        End If

        Dim part As String = info.TempPath & ".part"
        Try
            Directory.CreateDirectory(temp_Root)
            Using source As Stream = entry.OpenEntryStream()
                Using target As New FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None)
                    CopyBounded(source, target, info.Size)
                End Using
            End Using

            If File.Exists(info.TempPath) Then File.Delete(info.TempPath)
            File.Move(part, info.TempPath)
            TouchExtracted(index, info.Size)
            EvictIfOverBudget(index, maxCacheBytes)
            Return True
        Catch ex As Exception
            AppFileLogger.WriteLine("Archive: entry not extracted: " & info.EntryName & " - " & ex.Message)
            Try
                If File.Exists(part) Then File.Delete(part)
            Catch
            End Try
            refusal = EntryRefusal.Broken
            Return False
        End Try
    End Function

    ''' <summary>Records that <paramref name="index"/> is on disk and just got asked for,
    ''' moving it to the fresh end of the recency order (§5.4). Safe to call more than
    ''' once for the same entry - only the first call counts its bytes.</summary>
    Private Sub TouchExtracted(index As Integer, size As Long)
        If Not extracted_Sizes.ContainsKey(index) Then
            extracted_Sizes(index) = size
            session_Bytes += size
        End If
        lru_Recency.Remove(index)
        lru_Recency.Add(index)
    End Sub

    ''' <summary>
    ''' Keeps the session directory under its budget by deleting the least-recently-used
    ''' extracted entries (§5.4) - except <paramref name="justTouched"/> and its immediate
    ''' neighbours, which are what the UI or the prefetch is about to want next and are
    ''' never evicted even when everything else already has been. A session that never
    ''' grows past the budget never deletes anything.
    ''' </summary>
    Private Sub EvictIfOverBudget(justTouched As Integer, maxCacheBytes As Long)
        If maxCacheBytes <= 0 Then Return
        Do While session_Bytes > maxCacheBytes
            Dim victim As Integer = -1
            For Each candidate As Integer In lru_Recency
                If candidate = justTouched OrElse candidate = justTouched - 1 OrElse candidate = justTouched + 1 Then Continue For
                victim = candidate
                Exit For
            Next
            If victim < 0 Then Exit Do   ' nothing left to evict - everything is protected
            EvictEntry(victim)
        Loop
    End Sub

    Private Sub EvictEntry(index As Integer)
        Dim size As Long
        If Not extracted_Sizes.TryGetValue(index, size) Then Return
        Try
            Dim path As String = entry_Infos(index).TempPath
            If File.Exists(path) Then File.Delete(path)
        Catch
            ' A file that refuses to go (antivirus, a lingering handle) is left for the
            ' cleanup rubezhi (§4) - the accounting below still drops it, so the budget
            ' check does not spin on the same entry forever.
        End Try
        extracted_Sizes.Remove(index)
        lru_Recency.Remove(index)
        session_Bytes -= size
    End Sub

    ''' <summary>
    ''' Copies with a ceiling on what is actually written, not on what the header claimed
    ''' (§6.2). A bomb declares a modest size and then keeps producing bytes; without this
    ''' the check above would be a suggestion.
    ''' </summary>
    Private Shared Sub CopyBounded(source As Stream, target As Stream, declaredSize As Long)
        ' A little slack: some writers round the stored size, and failing a legitimate
        ' entry over one byte would be worse than writing one byte too many.
        Dim ceiling As Long = declaredSize + 4096L
        Dim buffer(81919) As Byte
        Dim written As Long = 0

        Do
            Dim read As Integer = source.Read(buffer, 0, buffer.Length)
            If read <= 0 Then Exit Do
            written += read
            If written > ceiling Then
                Throw New InvalidDataException(
                    "Entry produced more data than it declared (" & declaredSize.ToString() & " bytes).")
            End If
            target.Write(buffer, 0, read)
        Loop
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If disposed Then Return
        disposed = True
        Try
            If archive IsNot Nothing Then archive.Dispose()
        Catch ex As Exception
            AppFileLogger.WriteLine("Archive: closing failed - " & ex.Message)
        End Try
        Try
            If stream_Of_Archive IsNot Nothing Then stream_Of_Archive.Dispose()
        Catch
        End Try
    End Sub

End Class

''' <summary>
''' One entry of an open archive, as the viewer needs it: a name to show, the metadata the
''' sort order is computed from, and the path the file will have on disk.
'''
''' TempPath is decided when the archive is opened, before any extraction - that is what
''' lets the file list be built from archive metadata alone (invariant 10), with no walk
''' of the temporary directory, whose contents change as the user browses.
''' </summary>
Friend NotInheritable Class ArchiveEntryInfo

    Friend Sub New(index As Integer, entryName As String, displayName As String,
                   size As Long, lastWrite As Date, tempPath As String)
        Me.Index = index
        Me.EntryName = entryName
        Me.DisplayName = displayName
        Me.Size = size
        Me.LastWrite = lastWrite
        Me.TempPath = tempPath
    End Sub

    ''' <summary>Position in the session's own order - also the prefix of the file name on
    ''' disk, which is what keeps two entries called "cover.jpg" apart.</summary>
    Friend ReadOnly Property Index As Integer

    ''' <summary>The full name inside the archive ("1998/03/foto12.jpg") - what the file
    ''' label shows, so the user sees where in the archive they are (§2.2).</summary>
    Friend ReadOnly Property EntryName As String

    ''' <summary>Just the leaf, for anything that wants a short name.</summary>
    Friend ReadOnly Property DisplayName As String

    ''' <summary>Uncompressed size, from the header.</summary>
    Friend ReadOnly Property Size As Long

    ''' <summary>Timestamp stored in the archive - the sort orders by date use it, so an
    ''' archive sorts the same way its contents would in a folder.</summary>
    Friend ReadOnly Property LastWrite As Date

    ''' <summary>Where this entry will be on disk once it is needed.</summary>
    Friend ReadOnly Property TempPath As String

End Class
#End If
