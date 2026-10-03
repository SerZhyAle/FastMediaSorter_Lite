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
''' Phases Ф1 + Ф2: ZIP/CBZ and non-solid 7z/RAR are <see cref="ArchiveKind.RandomAccess"/>
''' (one seek per entry); a SOLID 7z/RAR is <see cref="ArchiveKind.Sequential"/>: reaching
''' one entry means decompressing everything before it, so the first open makes ONE pass
''' over the archive (<see cref="RunSequentialPass"/>) and every later flip is a file that
''' is already on disk. Same class, a Kind - not a second implementation.
'''
''' Modern-only, like the whole feature.
''' </summary>
Friend NotInheritable Class ArchiveSession
    Implements IDisposable

    ''' <summary>How an entry is reached (§5.2).</summary>
    Friend Enum ArchiveKind
        ''' <summary>ZIP/CBZ and non-solid 7z/RAR: any entry costs one seek.</summary>
        RandomAccess
        ''' <summary>Solid 7z/RAR: an entry costs the decompression of all before it.</summary>
        Sequential
    End Enum

    ''' <summary>How the one sequential pass ended.</summary>
    Friend Enum PassOutcome
        ''' <summary>Finished (or nothing to do). Entries the pass skipped - over the cache
        ''' budget, too large, bomb, encrypted - are refused or extracted on demand.</summary>
        Completed
        ''' <summary>The archive was closed while it ran.</summary>
        Cancelled
        ''' <summary>The stream broke before a single entry could be stored.</summary>
        Failed
    End Enum

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
            archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(stream_Of_Archive, ReaderOptionsForLegacyNames())
            ' The openable set is decided by EXTENSION, but the factory detects the format by
            ' CONTENT - and a RAR renamed .cbz (or the other way round) is common. What is
            ' read is what the content says it is: ZIP, 7z and RAR. Anything else the library
            ' can open (TAR, GZip, ARC..) is refused by name, with no entries (audit 052
            ' G-15) - the rule is "a format we chose", not "whatever the library knows".
            Select Case archive.Type
                Case SharpCompress.Common.ArchiveType.Zip
                    BuildEntryList(isSupportedMedia, maxEntries)
                Case SharpCompress.Common.ArchiveType.Rar, SharpCompress.Common.ArchiveType.SevenZip
                    BuildEntryList(isSupportedMedia, maxEntries)
                    ' When in doubt the archive is sequential (§5.2): one honest pass beats
                    ' forty slow seeks, and a failure to answer is a doubt.
                    archive_Kind = If(IsSolidOrUnknown(archive), ArchiveKind.Sequential, ArchiveKind.RandomAccess)
                Case Else
                    unsupported_Kind = archive.Type.ToString()
            End Select
        Catch ex As SharpCompress.Common.CryptographicException
            ' A 7z/RAR with ENCRYPTED HEADERS cannot even list its files without the password
            ' (§6.4): the honest "password protected" case, which would otherwise surface as
            ' the vaguer "could not read". The session stays alive with no entries so the
            ' caller can ask IsEncrypted - exactly like an archive whose entries are encrypted.
            AppFileLogger.WriteLine("Archive: encrypted headers - " & ex.Message)
            encrypted_Seen = True
        Catch
            If archive IsNot Nothing Then archive.Dispose()
            stream_Of_Archive.Dispose()
            Throw
        End Try
    End Sub

    Private Shared Function IsSolidOrUnknown(source As SharpCompress.Archives.IArchive) As Boolean
        Try
            Return source.IsSolid
        Catch ex As Exception
            AppFileLogger.WriteLine("Archive: solidity unknown, treating as solid - " & ex.Message)
            Return True
        End Try
    End Function

    Private archive_Kind As ArchiveKind = ArchiveKind.RandomAccess

    ''' <summary>How an entry of this archive is reached. Fixed when the archive opens.</summary>
    Friend ReadOnly Property Kind As ArchiveKind
        Get
            Return archive_Kind
        End Get
    End Property

    ''' <summary>True until the one sequential pass has run: the caller must make it (off the
    ''' UI thread, with progress) before browsing, or the first flip would pay for it
    ''' synchronously.</summary>
    Friend ReadOnly Property NeedsSequentialPass As Boolean
        Get
            Return archive_Kind = ArchiveKind.Sequential AndAlso Not pass_Done AndAlso entry_Infos.Count > 0
        End Get
    End Property
    Private pass_Done As Boolean = False

    ''' <summary>
    ''' G-16: names stored WITHOUT the ZIP UTF-8 flag are in the OEM code page of the machine
    ''' that made the archive - CP866 for Explorer or WinRAR on Russian or Ukrainian Windows.
    ''' SharpCompress would read them as CP437 and show box-drawing garbage; this machine's
    ''' OEM code page is what Explorer and 7-Zip assume, and names that DO carry the flag
    ''' are still read as UTF-8.
    ''' </summary>
    Friend Shared Function ReaderOptionsForLegacyNames() As SharpCompress.Readers.ReaderOptions
        Dim options As New SharpCompress.Readers.ReaderOptions()
        Try
            Text.Encoding.RegisterProvider(Text.CodePagesEncodingProvider.Instance)
            Dim oem As Text.Encoding = Text.Encoding.GetEncoding(Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
            options.ArchiveEncoding = New SharpCompress.Common.ArchiveEncoding() With {.Default = oem}
        Catch ex As Exception
            ' No such code page on this machine: the library's own default is the fallback.
            AppFileLogger.WriteLine("Archive: OEM code page not available - " & ex.Message)
        End Try
        Return options
    End Function

    ''' <summary>The detected format when it is not ZIP ("Rar", "SevenZip", ..), otherwise
    ''' empty. A session with a kind here has no entries and must not be browsed.</summary>
    Friend ReadOnly Property UnsupportedKind As String
        Get
            Return unsupported_Kind
        End Get
    End Property
    Private unsupported_Kind As String = ""

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
        ' How many entries with this exact key came BEFORE the current one, counted over every
        ' file entry (listed or not). A sequential reader hands entries over in its own order,
        ' and an archive may repeat a name; (key, occurrence) is what lets the reader's entry
        ' be matched back to ours without relying on the two orders agreeing.
        Dim seen As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        For Each entry As SharpCompress.Archives.IArchiveEntry In archive.Entries
            Dim key As String = If(entry.Key, "")
            Dim occurrence As Integer = 0
            If Not entry.IsDirectory Then
                seen.TryGetValue(key, occurrence)
                seen(key) = occurrence + 1
            End If
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
            entry_Occurrences.Add(occurrence)
            by_Temp_Path(entry_Infos(entry_Infos.Count - 1).TempPath) = entry_Infos.Count - 1
            by_Key_Occurrence(KeyOccurrence(key, occurrence)) = entry_Infos.Count - 1
            index += 1
        Next
    End Sub

    Private ReadOnly entry_Occurrences As New List(Of Integer)()
    Private ReadOnly by_Key_Occurrence As New Dictionary(Of String, Integer)(StringComparer.Ordinal)

    Private Shared Function KeyOccurrence(key As String, occurrence As Integer) As String
        Return key & ChrW(0) & occurrence.ToString(Globalization.CultureInfo.InvariantCulture)
    End Function

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
    ''' <param name="allowSlow">False for a background guess (the prefetch): on a sequential
    ''' archive an entry that is not on disk costs a scan from the start, which a guess about
    ''' the NEXT picture must never hold the extraction lock for. It then returns False with
    ''' no refusal, and the display path - which passes True - does the real work if the user
    ''' actually gets there.</param>
    Friend Function TryEnsureExtracted(index As Integer, ByRef refusal As EntryRefusal,
                                       Optional maxEntryBytes As Long = Default_Max_Entry_Bytes,
                                       Optional maxCacheBytes As Long = Default_Max_Cache_Bytes,
                                       Optional allowSlow As Boolean = True) As Boolean
        ' Serialized on purpose: the display path and the prefetch worker both call this,
        ' and they would otherwise be reading two entries out of ONE archive stream at the
        ' same time - SharpCompress seeks that stream per entry, so concurrent reads do not
        ' race over a lock, they race over the file position. The work inside is one
        ' entry's decompression, so the wait is bounded by the picture being fetched.
        SyncLock extraction_Gate
            Return EnsureExtractedCore(index, refusal, maxEntryBytes, maxCacheBytes, allowSlow)
        End SyncLock
    End Function

    Private ReadOnly extraction_Gate As New Object()

    Private Function EnsureExtractedCore(index As Integer, ByRef refusal As EntryRefusal,
                                         maxEntryBytes As Long, maxCacheBytes As Long,
                                         allowSlow As Boolean) As Boolean
        refusal = EntryRefusal.None
        ' A late prefetch that reaches here after the archive was left must not recreate the
        ' session directory that LeaveArchive just deleted - it survived the whole process,
        ' because the orphan sweep treats our own pid as live (audit 052 G-21).
        If disposed OrElse closing_Requested Then
            refusal = EntryRefusal.Broken
            Return False
        End If
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
        Catch ex As Exception
            ' Fall through and extract again.
            AppFileLogger.WriteLine("Archive: cached entry could not be checked - " & ex.Message)
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

        ' A solid entry that is not on disk (over the pass's budget, or evicted since) costs a
        ' scan from the start of the archive - correct, and slow. Never for a guess.
        If archive_Kind = ArchiveKind.Sequential AndAlso Not allowSlow Then Return False

        Try
            If archive_Kind = ArchiveKind.Sequential Then
                If Not ExtractOneSequentially(index, info) Then
                    refusal = EntryRefusal.Broken
                    Return False
                End If
            Else
                Directory.CreateDirectory(temp_Root)
                Using source As Stream = entry.OpenEntryStream()
                    WriteEntryFile(info, source, requireFullSize:=False)
                End Using
            End If
            TouchExtracted(index, info.Size)
            EvictIfOverBudget(index, maxCacheBytes)
            Return True
        Catch ex As Exception
            AppFileLogger.WriteLine("Archive: entry not extracted: " & info.EntryName & " - " & ex.Message)
            refusal = EntryRefusal.Broken
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Writes <paramref name="source"/> to the entry's place on disk: to a neighbouring
    ''' ".part" file first and renamed into position, so a half-written file is never what the
    ''' decoder is handed (§5.1), and a failed or cancelled write leaves nothing behind. Throws
    ''' on any failure after cleaning up. Callers hold the extraction gate.
    '''
    ''' <paramref name="requireFullSize"/> is for the sequential reader, whose stream can end
    ''' early on a damaged solid block without saying so - a short file is then a failure,
    ''' not a picture. A random-access entry keeps the library's own end-of-stream verdict.
    ''' </summary>
    Private Sub WriteEntryFile(info As ArchiveEntryInfo, source As Stream, requireFullSize As Boolean)
        Dim part As String = info.TempPath & ".part"
        Try
            Directory.CreateDirectory(temp_Root)
            Dim written As Long
            Using target As New FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None)
                written = CopyBounded(source, target, info.Size, Function() Threading.Volatile.Read(closing_Requested))
            End Using
            If requireFullSize AndAlso written < info.Size Then
                Throw New InvalidDataException(
                    "Entry ended after " & written.ToString() & " of " & info.Size.ToString() & " bytes.")
            End If

            If File.Exists(info.TempPath) Then File.Delete(info.TempPath)
            File.Move(part, info.TempPath)
        Catch
            Try
                If File.Exists(part) Then File.Delete(part)
            Catch cleanupFailure As Exception
                AppFileLogger.WriteLine("Archive: partial entry cleanup failed - " & cleanupFailure.Message)
            End Try
            Throw
        End Try
    End Sub

    ' --------------------------------------------------------------- sequential ----

    ''' <summary>
    ''' A forward-only reader over the archive on its OWN file handle and its own parse of the
    ''' headers - not the session's: a sweep is long, the session's stream is what Dispose
    ''' closes and what the random-access path seeks, and a reader keeps its position between
    ''' entries. It is a reader obtained from the ARCHIVE (<c>ExtractAllEntries</c>) rather
    ''' than from <c>ReaderFactory</c>, because the latter does not know 7z at all - a solid
    ''' 7z is decoded a block at a time, which only the archive object can do.
    ''' </summary>
    Private NotInheritable Class SweepReader
        Implements IDisposable

        Private ReadOnly source_Stream As FileStream
        Private ReadOnly source_Archive As SharpCompress.Archives.IArchive
        Friend ReadOnly Reader As SharpCompress.Readers.IReader

        Friend Sub New(archivePath As String)
            source_Stream = New FileStream(archivePath, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite Or FileShare.Delete)
            Try
                source_Archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(source_Stream, ReaderOptionsForLegacyNames())
                Reader = source_Archive.ExtractAllEntries()
            Catch
                Dispose()
                Throw
            End Try
        End Sub

        ''' <summary>How far into the file the decompression has read - the only honest
        ''' progress, since a solid block cannot be sized in advance.</summary>
        Friend ReadOnly Property Fraction As Double
            Get
                Dim length As Long = source_Stream.Length
                If length <= 0 Then Return 0
                Return Math.Min(1.0R, source_Stream.Position / length)
            End Get
        End Property

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                If Reader IsNot Nothing Then Reader.Dispose()
            Catch ex As Exception
                AppFileLogger.WriteLine("Archive: sweep reader closing failed - " & ex.Message)
            End Try
            Try
                If source_Archive IsNot Nothing Then source_Archive.Dispose()
            Catch ex As Exception
                AppFileLogger.WriteLine("Archive: sweep archive closing failed - " & ex.Message)
            End Try
            source_Stream.Dispose()
        End Sub
    End Class

    ''' <summary>
    ''' One entry of a solid archive, reached by reading forward to it (§5.2). The honest slow
    ''' path: what makes it rare is the pass, which stores every entry that fits the budget
    ''' in a single sweep. Called with the extraction gate held.
    ''' </summary>
    Private Function ExtractOneSequentially(index As Integer, info As ArchiveEntryInfo) As Boolean
        Dim wanted As String = KeyOccurrence(If(entry_Handles(index).Key, ""), entry_Occurrences(index))
        Dim seen As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        Using sweep As New SweepReader(archive_Path)
            Dim reader As SharpCompress.Readers.IReader = sweep.Reader
            Do While reader.MoveToNextEntry()
                If Threading.Volatile.Read(closing_Requested) Then
                    Throw New OperationCanceledException("The archive was closed during extraction.")
                End If
                If reader.Entry.IsDirectory Then Continue Do
                If KeyOccurrence(If(reader.Entry.Key, ""), NextOccurrence(seen, reader.Entry.Key)) <> wanted Then Continue Do
                Using source As Stream = reader.OpenEntryStream()
                    WriteEntryFile(info, source, requireFullSize:=True)
                End Using
                Return True
            Loop
        End Using
        Return False
    End Function

    ''' <summary>The number of earlier entries with this key (and counts this one).</summary>
    Private Shared Function NextOccurrence(seen As Dictionary(Of String, Integer), key As String) As Integer
        Dim k As String = If(key, "")
        Dim occurrence As Integer = 0
        seen.TryGetValue(k, occurrence)
        seen(k) = occurrence + 1
        Return occurrence
    End Function

    ''' <summary>
    ''' The one sweep over a solid archive (§5.2): every listed entry that fits the limits is
    ''' written in archive order, so the user waits once ("Extracting archive: 37 %") and every
    ''' flip afterwards is instant. Safe to call off the UI thread; it takes the extraction
    ''' gate per ENTRY, never for the whole pass, so closing the archive waits for one write at
    ''' most. It does not evict - everything it stores is the newest there is - and it stops
    ''' storing at the cache budget: the rest is refused or extracted on demand.
    '''
    ''' <paramref name="onProgress"/> receives 0..100 (monotonic, from the archive stream's
    ''' position - the only honest measure of a decompression it cannot size in advance) and
    ''' runs on the calling thread.
    ''' </summary>
    Friend Function RunSequentialPass(maxEntryBytes As Long, maxCacheBytes As Long,
                                      onProgress As Action(Of Integer)) As PassOutcome
        If archive_Kind <> ArchiveKind.Sequential OrElse pass_Done Then Return PassOutcome.Completed
        If disposed OrElse Threading.Volatile.Read(closing_Requested) Then Return PassOutcome.Cancelled

        Dim seen As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        Dim stored As Integer = 0
        Dim failures As Integer = 0
        Dim lastPercent As Integer = -1

        Using sweep As New SweepReader(archive_Path)
            Dim reader As SharpCompress.Readers.IReader = sweep.Reader
            Try
                Do While reader.MoveToNextEntry()
                    If Threading.Volatile.Read(closing_Requested) Then Return PassOutcome.Cancelled
                    If reader.Entry.IsDirectory Then Continue Do

                    If onProgress IsNot Nothing Then
                        Dim percent As Integer = CInt(Math.Min(99.0R, sweep.Fraction * 100.0R))
                        If percent > lastPercent Then
                            lastPercent = percent
                            onProgress(percent)
                        End If
                    End If

                    Dim entryKey As String = If(reader.Entry.Key, "")
                    Dim index As Integer
                    If Not by_Key_Occurrence.TryGetValue(KeyOccurrence(entryKey, NextOccurrence(seen, entryKey)), index) Then
                        Continue Do   ' not listed: not media, junk, past the entry ceiling..
                    End If

                    Dim info As ArchiveEntryInfo = entry_Infos(index)
                    Dim handle As SharpCompress.Archives.IArchiveEntry = entry_Handles(index)

                    ' The same refusals the on-demand path makes, applied here as SKIPS: the
                    ' entry stays in the list and the display path gives the reason when the
                    ' user arrives (invariant 9). A skip is not an error.
                    If info.Size > maxEntryBytes Then Continue Do
                    If handle.IsEncrypted Then Continue Do
                    If handle.CompressedSize > 0 AndAlso info.Size / handle.CompressedSize > Max_Expansion_Ratio Then Continue Do

                    SyncLock extraction_Gate
                        If disposed OrElse closing_Requested Then Return PassOutcome.Cancelled
                        If maxCacheBytes > 0 AndAlso session_Bytes + info.Size > maxCacheBytes Then Continue Do
                        Try
                            Using source As Stream = reader.OpenEntryStream()
                                WriteEntryFile(info, source, requireFullSize:=True)
                            End Using
                            TouchExtracted(index, info.Size)
                            stored += 1
                        Catch cancelled As OperationCanceledException
                            Return PassOutcome.Cancelled
                        Catch ex As Exception
                            failures += 1
                            AppFileLogger.WriteLine("Archive: pass could not store " & info.EntryName & " - " & ex.Message)
                        End Try
                    End SyncLock
                Loop
            Catch ex As Exception
                ' The stream itself broke mid-sweep. What was stored stays; the rest fails
                ' honestly entry by entry when asked for.
                AppFileLogger.LogException("Archive: sequential pass", ex)
                pass_Done = True
                Return If(stored > 0, PassOutcome.Completed, PassOutcome.Failed)
            End Try
        End Using

        pass_Done = True
        If onProgress IsNot Nothing Then onProgress(100)
        Return If(stored = 0 AndAlso failures > 0, PassOutcome.Failed, PassOutcome.Completed)
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
        Dim attempted As New HashSet(Of Integer)()
        Do While session_Bytes > maxCacheBytes
            Dim victim As Integer = -1
            For Each candidate As Integer In lru_Recency
                If attempted.Contains(candidate) OrElse displayed_Entries.Contains(candidate) Then Continue For
                If candidate = justTouched OrElse candidate = justTouched - 1 OrElse candidate = justTouched + 1 Then Continue For
                victim = candidate
                Exit For
            Next
            If victim < 0 Then Exit Do   ' nothing left to evict - everything is protected
            attempted.Add(victim)
            EvictEntry(victim)
        Loop
    End Sub

    Private ReadOnly displayed_Entries As New HashSet(Of Integer)()

    ''' <summary>Navigation can sort or shuffle the list, so "the index next to the one just
    ''' touched" is not always the page on screen. The UI pins the rows actually displayed
    ''' and next to it; a prefetch must not redefine which page is current.</summary>
    Friend Sub ProtectDisplayedEntries(paths As IEnumerable(Of String))
        SyncLock extraction_Gate
            displayed_Entries.Clear()
            For Each path As String In paths
                Dim index As Integer = IndexOfTempPath(path)
                If index >= 0 Then displayed_Entries.Add(index)
            Next
        End SyncLock
    End Sub

    Private Sub EvictEntry(index As Integer)
        Dim size As Long
        If Not extracted_Sizes.TryGetValue(index, size) Then Return
        Try
            Dim path As String = entry_Infos(index).TempPath
            If File.Exists(path) Then File.Delete(path)
        Catch ex As Exception
            ' A file that refuses to go (antivirus, a lingering handle) is left for the
            ' cleanup boundaries (§4). Its bytes stay accounted for so a later eviction
            ' retries it; EvictIfOverBudget tries each candidate only once per pass, so the
            ' budget check does not spin on the same entry.
            AppFileLogger.WriteLine("Archive: cached entry eviction failed - " & ex.Message)
            Return
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
    Private Shared Function CopyBounded(source As Stream, target As Stream, declaredSize As Long,
                                        cancelled As Func(Of Boolean)) As Long
        ' A little slack: some writers round the stored size, and failing a legitimate
        ' entry over one byte would be worse than writing one byte too many.
        Dim ceiling As Long = declaredSize + 4096L
        Dim buffer(81919) As Byte
        Dim written As Long = 0

        Do
            ' Checked per block (G-14/G-21): closing the archive stops an extraction in
            ' flight instead of waiting for a large entry to finish under the lock.
            If cancelled() Then Throw New OperationCanceledException("The archive was closed during extraction.")
            Dim read As Integer = source.Read(buffer, 0, buffer.Length)
            If read <= 0 Then Exit Do
            written += read
            If written > ceiling Then
                Throw New InvalidDataException(
                    "Entry produced more data than it declared (" & declaredSize.ToString() & " bytes).")
            End If
            target.Write(buffer, 0, read)
        Loop
        Return written
    End Function

    ''' <summary>Set before the lock is taken, read inside the copy loop: an extraction in
    ''' flight notices the archive is closing within one block.</summary>
    Private closing_Requested As Boolean

    Public Sub Dispose() Implements IDisposable.Dispose
        If disposed Then Return
        Threading.Volatile.Write(closing_Requested, True)
        ' Under the extraction lock (G-21): the archive stream is not closed underneath a
        ' copy, and nothing can start extracting - or recreate the directory - after this.
        SyncLock extraction_Gate
            If disposed Then Return
            disposed = True
            Try
                If archive IsNot Nothing Then archive.Dispose()
            Catch ex As Exception
                AppFileLogger.WriteLine("Archive: closing failed - " & ex.Message)
            End Try
            Try
                If stream_Of_Archive IsNot Nothing Then stream_Of_Archive.Dispose()
            Catch ex As Exception
                AppFileLogger.WriteLine("Archive: source stream closing failed - " & ex.Message)
            End Try
        End SyncLock
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
