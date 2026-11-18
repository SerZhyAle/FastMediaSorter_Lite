Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Threading

' The remote half of a scheduled backup (ticket L0343): an SFTP resource as the source or the destination.
'
' FileDO has no SFTP path, so a run with an SFTP side works in a run-owned local staging folder:
'   - an SFTP source is downloaded whole (a complete listing, empty folders, no links, no name Windows cannot hold,
'     every file re-checked for size after the download) and FileDO backs up the staged tree;
'   - an SFTP destination gets the local generation FileDO built, uploaded under the operation's own partial name,
'     read back in full, and only then does the remote numbered set move: the chosen slot goes, the new generation
'     takes its place by one non-replacing rename. Nothing is removed before the new generation is verified on the
'     server, and a run that cannot prove a step removes nothing at all.
'
' The numbered-set rule is FileDO's own, transcribed from its backupGen* (its cmd/filedo/backup_generation.go,
' CLI-EVENT-STREAM 0.17 item D): a set is the regular files whose whole name is `<stem>_<NNN><ext>` with NNN inside
' 001..keep; the newest generation is the latest modification time, equal times resolved by the higher number; the
' run writes the number after the newest, 001 after keep, 001 when empty; retirement is the replacement of the
' chosen slot - at most one removal per run, only a whole-name match, never a foreign holder, never an out-of-range
' slot or another stem's or extension's file.
'
' Concurrency (the owner's quiz decision of 2026-11-17): cooperating writers per generation set. A run holds a
' cooperative lock (`<stem>.fms-lock` in the destination folder, acquired by an upload plus a non-replacing rename)
' and refuses - no publication, no retirement - on any observable change of the set between its check and each
' mutation. A client that ignores the lock is outside the guarantee.

''' <summary>One generation of the remote set, as a complete listing showed it.</summary>
Friend NotInheritable Class BackupRemoteGeneration
    Friend ReadOnly Number As Integer
    ''' <summary>The server's exact spelling of the file name; the removal addresses this path and nothing else.</summary>
    Friend ReadOnly Name As String
    Friend ReadOnly FullPath As String
    Friend ReadOnly LastWriteUtc As Date

    Friend Sub New(number As Integer, name As String, fullPath As String, lastWriteUtc As Date)
        Me.Number = number
        Me.Name = name
        Me.FullPath = fullPath
        Me.LastWriteUtc = lastWriteUtc
    End Sub
End Class

''' <summary>
''' The remote generation set: "/"-path helpers and the FileDO number rule, over entries a complete listing already
''' brought. Pure - no provider, no disk - so the rule is provable in a unit test against names alone.
''' </summary>
Friend Module BackupRemoteSet

    ''' <summary>The owned partial name: the verified new generation stands under it until the one rename.</summary>
    Friend Const PartialSuffix As String = ".fms-part"
    ''' <summary>The cooperative lock of the set: one writer at a time among cooperating clients.</summary>
    Friend Const LockSuffix As String = ".fms-lock"
    Friend Const ZipExtension As String = ".zip"
    Friend Const SealedExtension As String = ".fd-sec"

    ''' <summary>`&lt;stem&gt;_&lt;NNN&gt;&lt;ext&gt;`, three digits exactly as the producer writes them.</summary>
    Friend Function GenerationName(stem As String, number As Integer, extension As String) As String
        Return stem & "_" & number.ToString("000", CultureInfo.InvariantCulture) & extension
    End Function

    Friend Function LockName(stem As String) As String
        Return stem & LockSuffix
    End Function

    ''' <summary>"&lt;parent&gt;/&lt;child&gt;", the parent keeping its place ("/" is the server's own root).</summary>
    Private Function Combine(parent As String, child As String) As String
        Dim p As String = If(parent, "").TrimEnd("/"c)
        If p.Length = 0 Then Return "/" & child
        Return p & "/" & child
    End Function

    ''' <summary>The server's path of one name in this destination folder.</summary>
    Friend Function PathOf(destinationFolder As String, name As String) As String
        Return Combine(destinationFolder, name)
    End Function

    ''' <summary>Is <paramref name="child"/> the same "/"-separated folder as <paramref name="parent"/>, or below it?
    ''' The POSIX server's own comparison: exact and case-sensitive.</summary>
    Friend Function IsSameOrInside(child As String, parent As String) As Boolean
        Dim c As String = If(child, "").Trim()
        Dim p As String = If(parent, "").Trim()
        If c.Length = 0 OrElse p.Length = 0 Then Return False
        c = c.TrimEnd("/"c)
        p = p.TrimEnd("/"c)
        If p.Length = 0 Then Return c.Length > 0
        If String.Equals(c, p, StringComparison.Ordinal) Then Return True
        Return c.StartsWith(p & "/", StringComparison.Ordinal)
    End Function

    ''' <summary>The number of <paramref name="name"/> when it is a generation of the set, else 0 (FileDO
    ''' backupGenNumber: the whole name, NNN in 001..<paramref name="keep"/>, the comparison case-insensitive as the
    ''' producer's own EqualFold is).</summary>
    Friend Function SlotNumberOf(name As String, stem As String, extension As String, keep As Integer) As Integer
        Dim text As String = If(name, "")
        If stem.Length = 0 OrElse keep < 1 OrElse keep > 999 Then Return 0
        If text.Length < extension.Length + 5 Then Return 0
        If Not String.Equals(text.Substring(text.Length - extension.Length), extension, StringComparison.OrdinalIgnoreCase) Then Return 0
        Dim body As String = text.Substring(0, text.Length - extension.Length)
        If body.Length < 4 Then Return 0
        Dim digits As String = body.Substring(body.Length - 3)
        For i As Integer = 0 To digits.Length - 1
            If digits(i) < "0"c OrElse digits(i) > "9"c Then Return 0
        Next
        Dim head As String = body.Substring(0, body.Length - 3)
        If head.Length = 0 OrElse head(head.Length - 1) <> "_"c Then Return 0
        If Not String.Equals(head.Substring(0, head.Length - 1), stem, StringComparison.OrdinalIgnoreCase) Then Return 0
        Dim number As Integer
        If Not Integer.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, number) Then Return 0
        If number < 1 OrElse number > keep Then Return 0
        Return number
    End Function

    ''' <summary>The generations of the set in a listing: regular files alone - a folder or a link under a matching
    ''' name is not a generation and is never retired (FileDO backupGenScan). A number appears once, under the
    ''' listing's first spelling of it.</summary>
    Friend Function Scan(entries As IReadOnlyList(Of ResourceEntry), stem As String, extension As String,
                         keep As Integer) As Dictionary(Of Integer, BackupRemoteGeneration)
        Dim present As New Dictionary(Of Integer, BackupRemoteGeneration)()
        If entries Is Nothing Then Return present
        For Each entry As ResourceEntry In entries
            If entry.IsDirectory OrElse entry.IsLink Then Continue For
            Dim number As Integer = SlotNumberOf(entry.Name, stem, extension, keep)
            If number = 0 OrElse present.ContainsKey(number) Then Continue For
            present(number) = New BackupRemoteGeneration(number, entry.Name, entry.FullPath, entry.LastWriteUtc)
        Next
        Return present
    End Function

    ''' <summary>The newest generation by the producer's rule: the latest modification time, equal times the higher
    ''' number.</summary>
    Friend Function NewestOf(present As Dictionary(Of Integer, BackupRemoteGeneration)) As BackupRemoteGeneration
        Dim newest As BackupRemoteGeneration = Nothing
        For Each generation As BackupRemoteGeneration In present.Values
            If newest Is Nothing OrElse generation.LastWriteUtc > newest.LastWriteUtc OrElse
               (generation.LastWriteUtc = newest.LastWriteUtc AndAlso generation.Number > newest.Number) Then
                newest = generation
            End If
        Next
        Return newest
    End Function

    ''' <summary>The number a run writes: the one after the newest, 001 after the keep-count, 001 when the set is
    ''' empty. Derived from the destination's own listing and from nothing else (FileDO backupGenNext).</summary>
    Friend Function NextNumber(present As Dictionary(Of Integer, BackupRemoteGeneration), keep As Integer) As Integer
        Dim newest As BackupRemoteGeneration = NewestOf(present)
        If newest Is Nothing Then Return 1
        If newest.Number >= keep Then Return 1
        Return newest.Number + 1
    End Function

    ''' <summary>The part of a listing this set owns: every entry whose name carries the stem (a generation/dated file, a
    ''' partial, the lock or a foreign holder). This and nothing else is what an observable change is measured on.</summary>
    Friend Function RelevantEntries(entries As IReadOnlyList(Of ResourceEntry), stem As String,
                                    Optional dated As Boolean = False) As List(Of ResourceEntry)
        Dim result As New List(Of ResourceEntry)()
        If entries Is Nothing Then Return result
        Dim head As String = stem & If(dated, "-", "_")
        For Each entry As ResourceEntry In entries
            If String.Equals(entry.Name, LockName(stem), StringComparison.OrdinalIgnoreCase) OrElse
               entry.Name.StartsWith(head, StringComparison.OrdinalIgnoreCase) Then
                result.Add(entry)
            End If
        Next
        Return result
    End Function

    ''' <summary>Are the two snapshots of the set's own entries the same listing - same names, kinds, sizes, times -
    ''' apart from <paramref name="ignoreName"/> (this run's own partial, which is expected to appear and leave)?
    ''' Any other difference is an observable change: the run refuses before its next mutation.</summary>
    Friend Function SameSnapshot(a As List(Of ResourceEntry), b As List(Of ResourceEntry),
                                 Optional ignoreName As String = "") As Boolean
        Dim ignore As String = If(ignoreName, "")
        Dim left As List(Of ResourceEntry) = a.
            Where(Function(e As ResourceEntry) Not String.Equals(e.Name, ignore, StringComparison.OrdinalIgnoreCase)).
            OrderBy(Function(e As ResourceEntry) e.Name, StringComparer.OrdinalIgnoreCase).ToList()
        Dim right As List(Of ResourceEntry) = b.
            Where(Function(e As ResourceEntry) Not String.Equals(e.Name, ignore, StringComparison.OrdinalIgnoreCase)).
            OrderBy(Function(e As ResourceEntry) e.Name, StringComparer.OrdinalIgnoreCase).ToList()
        If left.Count <> right.Count Then Return False
        For i As Integer = 0 To left.Count - 1
            If Not String.Equals(left(i).Name, right(i).Name, StringComparison.OrdinalIgnoreCase) Then Return False
            If left(i).IsDirectory <> right(i).IsDirectory OrElse left(i).IsLink <> right(i).IsLink Then Return False
            If left(i).Length <> right(i).Length Then Return False
            If left(i).LastWriteUtc <> right(i).LastWriteUtc Then Return False
        Next
        Return True
    End Function

    ''' <summary>Does any entry of the listing hold this exact name as a folder or a link? Such a holder is never a
    ''' generation and is never replaced or removed (FileDO item D4).</summary>
    Friend Function ForeignHolder(entries As IReadOnlyList(Of ResourceEntry), name As String) As Boolean
        If entries Is Nothing Then Return False
        For Each entry As ResourceEntry In entries
            If String.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase) AndAlso
               (entry.IsDirectory OrElse entry.IsLink) Then Return True
        Next
        Return False
    End Function

    ''' <summary>Does any entry of the listing hold this exact name (any regular file, folder, or link)?</summary>
    Friend Function NameOccupied(entries As IReadOnlyList(Of ResourceEntry), name As String) As Boolean
        If entries Is Nothing Then Return False
        For Each entry As ResourceEntry In entries
            If String.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

    Friend NotInheritable Class DatedSelection
        Friend ReadOnly Property Retire As New List(Of ResourceEntry)()
        Friend Property KeptCount As Integer
        Friend Property MonthlyKept As Integer
    End Class

    Private NotInheritable Class DatedCandidate
        Friend ReadOnly Entry As ResourceEntry
        Friend ReadOnly Name As String
        Friend ReadOnly Stamp As DateTime
        Friend Sub New(entry As ResourceEntry, name As String, stamp As DateTime)
            Me.Entry = entry
            Me.Name = name
            Me.Stamp = stamp
        End Sub
    End Class

    ''' <summary>
    ''' Dated retention selection matching FileDO's `backupKeepSelect` (CLI-EVENT-STREAM 0.18 items I.1-I.2):
    ''' dates parsed solely from filenames (`&lt;stem&gt;-&lt;yyMMddHHmm&gt;&lt;ext&gt;`), earliest per calendar month kept when
    ''' <paramref name="keepMonthly"/> is true (including <paramref name="protectName"/>), files older than
    ''' <paramref name="keepDays"/> retired except monthly-first and <paramref name="protectName"/>.
    ''' </summary>
    Friend Function SelectDated(entries As IReadOnlyList(Of ResourceEntry), stem As String, extension As String,
                                keepDays As Integer, keepMonthly As Boolean, nowLocal As DateTime,
                                protectName As String) As DatedSelection
        Dim candidates As New List(Of DatedCandidate)()
        Dim seenProtect As Boolean = False
        If entries IsNot Nothing Then
            For Each entry As ResourceEntry In entries
                If entry.IsDirectory OrElse entry.IsLink Then Continue For
                Dim stamp As DateTime
                If Not BackupCommandLine.TryParseDatedArchiveStamp(entry.Name, stem, extension, stamp) Then Continue For
                If Not String.IsNullOrEmpty(protectName) AndAlso
                   String.Equals(entry.Name, protectName, StringComparison.OrdinalIgnoreCase) Then
                    seenProtect = True
                End If
                candidates.Add(New DatedCandidate(entry, entry.Name, stamp))
            Next
        End If
        If Not seenProtect AndAlso Not String.IsNullOrEmpty(protectName) Then
            Dim protectStamp As DateTime
            If BackupCommandLine.TryParseDatedArchiveStamp(protectName, stem, extension, protectStamp) Then
                candidates.Add(New DatedCandidate(Nothing, protectName, protectStamp))
            End If
        End If
        candidates.Sort(Function(a As DatedCandidate, b As DatedCandidate) As Integer
                            Dim cmp As Integer = DateTime.Compare(a.Stamp, b.Stamp)
                            If cmp <> 0 Then Return cmp
                            Return StringComparer.Ordinal.Compare(a.Name.ToLowerInvariant(), b.Name.ToLowerInvariant())
                        End Function)

        Dim monthlyFirst As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If keepMonthly Then
            Dim seenMonth As New HashSet(Of String)(StringComparer.Ordinal)
            For Each c As DatedCandidate In candidates
                Dim ym As String = c.Stamp.Year.ToString(CultureInfo.InvariantCulture) & "-" & c.Stamp.Month.ToString("00", CultureInfo.InvariantCulture)
                If seenMonth.Add(ym) Then monthlyFirst.Add(c.Name)
            Next
        End If

        Dim cutoff As DateTime = nowLocal.AddDays(-keepDays)
        Dim sel As New DatedSelection()
        For Each c As DatedCandidate In candidates
            If Not String.IsNullOrEmpty(protectName) AndAlso
               String.Equals(c.Name, protectName, StringComparison.OrdinalIgnoreCase) Then
                Continue For
            End If
            If c.Stamp >= cutoff Then Continue For
            If keepMonthly AndAlso monthlyFirst.Contains(c.Name) Then
                sel.MonthlyKept += 1
                Continue For
            End If
            If c.Entry IsNot Nothing Then sel.Retire.Add(c.Entry)
        Next
        sel.KeptCount = candidates.Count - sel.Retire.Count
        Return sel
    End Function

End Module

''' <summary>
''' The run-owned staging folders under %TEMP% (ticket L0343): one folder per run, named after the operation, holding
''' the staged source tree and the local generation FileDO builds for an SFTP destination. Everything is removed at
''' the end of the run; an earlier run's leftovers are recovered by their full name alone, so nothing unrelated is
''' ever touched.
''' </summary>
Friend Module BackupStagingFolders

    Friend Const Prefix As String = "FMS-Scheduled-Backup-"
    ''' <summary>The staged source tree an SFTP source is downloaded into.</summary>
    Friend Const SourceFolderName As String = "source"
    ''' <summary>The local folder FileDO builds the generation in when the destination is an SFTP resource.</summary>
    Friend Const SetFolderName As String = "set"
    Private Const StampFormat As String = "yyyyMMddHHmmss"

    Friend Function FolderName(operationId As String, startedAt As Date) As String
        Return Prefix & FillWorkFolders.OperationTag(operationId) & "-" & startedAt.ToString(StampFormat, CultureInfo.InvariantCulture)
    End Function

    ''' <summary>Creates this run's staging folder under %TEMP% (per-user, so the staged plaintext never leaves the
    ''' current account) and answers its path; an earlier run's leftovers are removed first, by name alone, and the
    ''' ones that stay are named in <paramref name="leftovers"/> for the journal.</summary>
    Friend Function Create(operationId As String, startedAt As Date, ByRef leftovers As List(Of String)) As String
        leftovers = New List(Of String)()
        For Each earlier As String In OwnFolders(operationId)
            Try
                Directory.Delete(earlier, True)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                AppFileLogger.LogException("Scheduled operations: removing an earlier backup staging folder", ex)
                leftovers.Add(earlier)
            End Try
        Next
        Dim folder As String = Path.Combine(Path.GetTempPath(), FolderName(operationId, startedAt))
        Directory.CreateDirectory(folder)
        Return folder
    End Function

    ''' <summary>This operation's staging folders, recognised by their full name alone: the prefix, this operation's
    ''' tag and a 14-digit stamp, and nothing else.</summary>
    Friend Function OwnFolders(operationId As String) As List(Of String)
        Dim own As New List(Of String)()
        Dim tag As String = FillWorkFolders.OperationTag(operationId)
        If tag.Length = 0 Then Return own
        Dim head As String = Prefix & tag & "-"
        Try
            For Each entry As String In Directory.EnumerateDirectories(Path.GetTempPath())
                Dim name As String = Path.GetFileName(entry)
                If Not name.StartsWith(head, StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim stamp As String = name.Substring(head.Length)
                If stamp.Length = StampFormat.Length AndAlso stamp.All(Function(c As Char) Char.IsDigit(c)) Then own.Add(entry)
            Next
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppFileLogger.LogException("Scheduled operations: looking for earlier backup staging folders", ex)
        End Try
        Return own
    End Function

    ''' <summary>Removes everything the run staged. What cannot be removed is reported, never silenced; the folder
    ''' is this run's own by name, so a leftover holds nothing but staged plaintext.</summary>
    Friend Function Clear(folder As String) As List(Of String)
        Dim left As New List(Of String)()
        If String.IsNullOrEmpty(folder) Then Return left
        Try
            Directory.Delete(folder, True)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppFileLogger.LogException("Scheduled operations: removing the backup staging folder " & folder, ex)
            left.Add(folder)
        End Try
        Return left
    End Function

    ''' <summary>Does any entry of the listing hold this exact name (any regular file, folder, or link)?</summary>
    Friend Function NameOccupied(entries As IReadOnlyList(Of ResourceEntry), name As String) As Boolean
        If entries Is Nothing Then Return False
        For Each entry As ResourceEntry In entries
            If String.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

    Friend NotInheritable Class DatedSelection
        Friend ReadOnly Property Retire As New List(Of ResourceEntry)()
        Friend Property KeptCount As Integer
        Friend Property MonthlyKept As Integer
    End Class

    Private NotInheritable Class DatedCandidate
        Friend ReadOnly Entry As ResourceEntry
        Friend ReadOnly Name As String
        Friend ReadOnly Stamp As DateTime
        Friend Sub New(entry As ResourceEntry, name As String, stamp As DateTime)
            Me.Entry = entry
            Me.Name = name
            Me.Stamp = stamp
        End Sub
    End Class

    ''' <summary>
    ''' Dated retention selection matching FileDO's `backupKeepSelect` (CLI-EVENT-STREAM 0.18 items I.1-I.2):
    ''' dates parsed solely from filenames (`&lt;stem&gt;-&lt;yyMMddHHmm&gt;&lt;ext&gt;`), earliest per calendar month kept when
    ''' <paramref name="keepMonthly"/> is true (including <paramref name="protectName"/>), files older than
    ''' <paramref name="keepDays"/> retired except monthly-first and <paramref name="protectName"/>.
    ''' </summary>
    Friend Function SelectDated(entries As IReadOnlyList(Of ResourceEntry), stem As String, extension As String,
                                keepDays As Integer, keepMonthly As Boolean, nowLocal As DateTime,
                                protectName As String) As DatedSelection
        Dim candidates As New List(Of DatedCandidate)()
        Dim seenProtect As Boolean = False
        If entries IsNot Nothing Then
            For Each entry As ResourceEntry In entries
                If entry.IsDirectory OrElse entry.IsLink Then Continue For
                Dim stamp As DateTime
                If Not BackupCommandLine.TryParseDatedArchiveStamp(entry.Name, stem, extension, stamp) Then Continue For
                If Not String.IsNullOrEmpty(protectName) AndAlso
                   String.Equals(entry.Name, protectName, StringComparison.OrdinalIgnoreCase) Then
                    seenProtect = True
                End If
                candidates.Add(New DatedCandidate(entry, entry.Name, stamp))
            Next
        End If
        If Not seenProtect AndAlso Not String.IsNullOrEmpty(protectName) Then
            Dim protectStamp As DateTime
            If BackupCommandLine.TryParseDatedArchiveStamp(protectName, stem, extension, protectStamp) Then
                candidates.Add(New DatedCandidate(Nothing, protectName, protectStamp))
            End If
        End If
        candidates.Sort(Function(a As DatedCandidate, b As DatedCandidate) As Integer
                            Dim cmp As Integer = DateTime.Compare(a.Stamp, b.Stamp)
                            If cmp <> 0 Then Return cmp
                            Return StringComparer.Ordinal.Compare(a.Name.ToLowerInvariant(), b.Name.ToLowerInvariant())
                        End Function)

        Dim monthlyFirst As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If keepMonthly Then
            Dim seenMonth As New HashSet(Of String)(StringComparer.Ordinal)
            For Each c As DatedCandidate In candidates
                Dim ym As String = c.Stamp.Year.ToString(CultureInfo.InvariantCulture) & "-" & c.Stamp.Month.ToString("00", CultureInfo.InvariantCulture)
                If seenMonth.Add(ym) Then monthlyFirst.Add(c.Name)
            Next
        End If

        Dim cutoff As DateTime = nowLocal.AddDays(-keepDays)
        Dim sel As New DatedSelection()
        For Each c As DatedCandidate In candidates
            If Not String.IsNullOrEmpty(protectName) AndAlso
               String.Equals(c.Name, protectName, StringComparison.OrdinalIgnoreCase) Then
                Continue For
            End If
            If c.Stamp >= cutoff Then Continue For
            If keepMonthly AndAlso monthlyFirst.Contains(c.Name) Then
                sel.MonthlyKept += 1
                Continue For
            End If
            If c.Entry IsNot Nothing Then sel.Retire.Add(c.Entry)
        Next
        sel.KeptCount = candidates.Count - sel.Retire.Count
        Return sel
    End Function

End Module

''' <summary>Why a staged source could not be made. A machine code; the words are composed at display time.</summary>
Friend Enum BackupRemoteRefusal
    ''' <summary>The folder's complete listing could not be read - failed, denied, missing, or cut short.</summary>
    Listing = 0
    Link = 1
    UnsafeName = 2
    SourceChanged = 3
    Space = 4
    Transport = 5
End Enum

''' <summary>A staged source tree: the whole of it, or the machine reason it could not be.</summary>
Friend NotInheritable Class BackupStageResult

    Private ReadOnly refusedCode As BackupRemoteRefusal?
    Private ReadOnly placeValue As String

    Friend Sub New()
        refusedCode = Nothing
        placeValue = ""
    End Sub

    Private Sub New(code As BackupRemoteRefusal, place As String)
        refusedCode = code
        placeValue = place
    End Sub

    Friend Property Files As Integer
    Friend Property Folders As Integer
    Friend Property Bytes As Long
    Friend ReadOnly Property SkippedLinks As New List(Of String)()

    Friend ReadOnly Property Refused As Boolean
        Get
            Return refusedCode.HasValue
        End Get
    End Property

    Friend ReadOnly Property Code As BackupRemoteRefusal
        Get
            Return refusedCode.Value
        End Get
    End Property

    Friend ReadOnly Property Place As String
        Get
            Return placeValue
        End Get
    End Property

    ''' <summary>The wall detail, exactly as the executor writes it into the run's result.</summary>
    Friend ReadOnly Property Detail As String
        Get
            Select Case refusedCode
                Case BackupRemoteRefusal.Listing : Return "backup-remote-listing|" & placeValue
                Case BackupRemoteRefusal.Link : Return "backup-remote-link|" & placeValue
                Case BackupRemoteRefusal.UnsafeName : Return "backup-remote-name|" & placeValue
                Case BackupRemoteRefusal.SourceChanged : Return "backup-source-changed|" & placeValue
                Case BackupRemoteRefusal.Space : Return "disk-full|" & placeValue
                Case Else : Return "backup-remote-transport|" & placeValue
            End Select
        End Get
    End Property

    Friend Shared Function Refusal(code As BackupRemoteRefusal, place As String) As BackupStageResult
        Return New BackupStageResult(code, place)
    End Function
End Class

''' <summary>
''' The complete, bounded, link-safe walk of a remote source tree, and its download into a run-owned local folder.
''' A listing that is truncated or skips a child cannot establish a complete backup, so it is refused; a link is
''' refused rather than entered or skipped past; a file or folder whose name Windows cannot hold is refused rather
''' than damaged; every downloaded file is re-checked for size against its listing entry, so a source that changed
''' under the run is never reported as backed up.
''' </summary>
Friend NotInheritable Class BackupRemoteStage

    Private Const MaxDepth As Integer = 32
    Private Const MaxEntries As Integer = 100000
    ''' <summary>The margin above the staged bytes: the local generation costs room of its own, and a volume that
    ''' cannot hold both is exhausted before the first byte moves.</summary>
    Private Const SpaceMarginBytes As Long = 64L * 1024 * 1024

    ''' <summary>Test seam: the free bytes of a folder's volume, Nothing when they cannot be told. The product's
    ''' default asks the drive when it can be determined and never guesses.</summary>
    Friend Property FreeBytesOf As Func(Of String, Long?) = AddressOf FreeBytesOfVolume

    Private Shared Function FreeBytesOfVolume(folder As String) As Long?
        Try
            Dim root As String = Path.GetPathRoot(Path.GetFullPath(folder))
            If String.IsNullOrEmpty(root) Then Return Nothing
            Dim drive As New DriveInfo(root)
            If Not drive.IsReady Then Return Nothing
            Return drive.AvailableFreeSpace
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException
            Return Nothing
        End Try
    End Function

    ''' <summary>Downloads <paramref name="root"/> of <paramref name="side"/> into <paramref name="localRoot"/> -
    ''' the whole tree: files, structure and empty folders. Answers the staged counts, or the refusal.</summary>
    Friend Function Stage(side As ProviderSide, localRoot As String, ct As CancellationToken,
                          Optional skipLinks As Boolean = False) As BackupStageResult
        Dim provider As IFileSystemProvider = side.Provider
        Dim manager As IManagerDirectoryProvider = TryCast(provider, IManagerDirectoryProvider)
        If manager Is Nothing Then Return BackupStageResult.Refusal(BackupRemoteRefusal.Listing, side.Describe())

        ct.ThrowIfCancellationRequested()
        Dim rootEntry As StatResult = manager.StatEntry(side.Root)
        If rootEntry.Outcome <> StatOutcome.Found OrElse Not rootEntry.Entry.IsDirectory Then
            Return BackupStageResult.Refusal(BackupRemoteRefusal.Listing, side.Describe())
        End If

        Dim files As New List(Of ResourceEntry)()
        Dim folders As New List(Of String)()
        Dim skippedLinks As New List(Of String)()
        Dim refused As BackupStageResult = Walk(side.Root, manager, files, folders, skipLinks, skippedLinks, ct)
        If refused IsNot Nothing Then Return refused

        ' The room for the whole staged tree is asked once, when it can be determined: the source keeps changing
        ' and no fixed cap is claimed - a run only refuses to start what it cannot hold, and never retires anything,
        ' because at this point nothing has moved at all.
        Dim bytes As Long = 0L
        For Each entry As ResourceEntry In files
            bytes += entry.Length
        Next
        Dim free As Long? = FreeBytesOf.Invoke(localRoot)
        If free.HasValue AndAlso free.Value < bytes + SpaceMarginBytes Then
            Return BackupStageResult.Refusal(BackupRemoteRefusal.Space, localRoot)
        End If

        Try
            Directory.CreateDirectory(localRoot)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppFileLogger.LogException("Scheduled operations: staging " & side.Describe(), ex)
            Return BackupStageResult.Refusal(BackupRemoteRefusal.Transport, localRoot)
        End Try
        For Each folder As String In folders
            ct.ThrowIfCancellationRequested()
            Directory.CreateDirectory(Path.Combine(localRoot, folder.Replace("/"c, "\"c)))
        Next

        For Each entry As ResourceEntry In files
            ct.ThrowIfCancellationRequested()
            Dim relative As String = RelativeBelow(side.Root, entry.FullPath)
            If relative.Length = 0 Then Return BackupStageResult.Refusal(BackupRemoteRefusal.UnsafeName, entry.FullPath)
            Dim localPath As String = Path.Combine(localRoot, relative.Replace("/"c, "\"c))
            Try
                provider.Download(entry.FullPath, localPath, Nothing, ct)
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                AppFileLogger.LogException("Scheduled operations: staging " & relative, ex)
                If ScheduledFailure.CodeOf(ex) = "disk-full" Then
                    Return BackupStageResult.Refusal(BackupRemoteRefusal.Space, localRoot)
                End If
                Return BackupStageResult.Refusal(BackupRemoteRefusal.Transport, relative)
            End Try
            ' The staged tree is a copy of a listing this run established: a file that changed size under it is not
            ' what was listed, and the run says so instead of backing up an unknown mix.
            Dim written As Long
            Try
                written = New FileInfo(localPath).Length
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                AppFileLogger.LogException("Scheduled operations: staging " & relative, ex)
                Return BackupStageResult.Refusal(BackupRemoteRefusal.Transport, relative)
            End Try
            If written <> entry.Length Then
                Return BackupStageResult.Refusal(BackupRemoteRefusal.SourceChanged, relative)
            End If
        Next

        Dim result As New BackupStageResult With {.Files = files.Count, .Folders = folders.Count, .Bytes = bytes}
        result.SkippedLinks.AddRange(skippedLinks)
        Return result
    End Function

    ''' <summary>Walks the tree breadth-first. Nothing means the walk ended complete; a result is the refusal. Every
    ''' directory listing must come back complete - the immediate children, with the truncation and typed outcomes of
    ''' the file manager interface.</summary>
    Private Shared Function Walk(root As String, manager As IManagerDirectoryProvider,
                                 files As List(Of ResourceEntry), folders As List(Of String),
                                 skipLinks As Boolean, skippedLinks As List(Of String),
                                 ct As CancellationToken) As BackupStageResult
        Dim pending As New Queue(Of KeyValuePair(Of String, String))()
        pending.Enqueue(New KeyValuePair(Of String, String)(root, ""))
        While pending.Count > 0
            ct.ThrowIfCancellationRequested()
            Dim place As KeyValuePair(Of String, String) = pending.Dequeue()
            Dim listing As DirectoryListing = manager.ListChildren(place.Key, ct)
            If listing.Outcome = DirectoryOutcome.Cancelled Then Throw New OperationCanceledException()
            If Not listing.IsComplete Then
                Return BackupStageResult.Refusal(BackupRemoteRefusal.Listing,
                    If(place.Value.Length > 0, place.Value, place.Key))
            End If
            For Each entry As ResourceEntry In listing.Entries
                ct.ThrowIfCancellationRequested()
                If entry.IsLink Then
                    Dim linkRelative As String = If(place.Value.Length > 0, place.Value & "/" & entry.Name, entry.Name)
                    If skipLinks Then
                        skippedLinks.Add(linkRelative)
                        Continue For
                    End If
                    ' Never entered, never followed, never skipped past unless skip-links is on.
                    Return BackupStageResult.Refusal(BackupRemoteRefusal.Link, linkRelative)
                End If
                If Not SafeLocalName(entry.Name) Then
                    Return BackupStageResult.Refusal(BackupRemoteRefusal.UnsafeName, entry.Name)
                End If
                Dim relative As String = If(place.Value.Length > 0, place.Value & "/" & entry.Name, entry.Name)
                If entry.IsDirectory Then
                    If folders.Count >= MaxEntries OrElse pending.Count >= MaxEntries Then
                        Return BackupStageResult.Refusal(BackupRemoteRefusal.Listing, relative)
                    End If
                    folders.Add(relative)
                    pending.Enqueue(New KeyValuePair(Of String, String)(entry.FullPath, relative))
                Else
                    If files.Count >= MaxEntries Then
                        Return BackupStageResult.Refusal(BackupRemoteRefusal.Listing, relative)
                    End If
                    files.Add(entry)
                End If
            Next
            If pending.Count > MaxDepth Then
                Return BackupStageResult.Refusal(BackupRemoteRefusal.Listing, place.Value)
            End If
        End While
        Return Nothing
    End Function

    ''' <summary>The relative "/"-path of <paramref name="fullPath"/> below the walk's root, "" when it is not below it.</summary>
    Private Shared Function RelativeBelow(root As String, fullPath As String) As String
        Dim r As String = If(root, "").TrimEnd("/"c)
        Dim f As String = If(fullPath, "").Trim()
        If r.Length = 0 Then Return f.TrimStart("/"c)
        If Not f.StartsWith(r & "/", StringComparison.Ordinal) Then Return ""
        Return f.Substring(r.Length + 1)
    End Function

    ''' <summary>A name a Windows folder can hold: no reserved character, no reserved device name, not "." or "..",
    ''' and neither a trailing dot nor a trailing space - the tree is mirrored verbatim or refused.</summary>
    Friend Shared Function SafeLocalName(name As String) As Boolean
        If String.IsNullOrEmpty(name) Then Return False
        If name = "." OrElse name = ".." Then Return False
        If name.Length > 255 Then Return False
        If name.EndsWith("."c) OrElse name.EndsWith(" "c) Then Return False
        If name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 Then Return False
        Dim stem As String = name
        Dim dot As Integer = stem.IndexOf("."c)
        If dot > 0 Then stem = stem.Substring(0, dot)
        Select Case stem.ToUpperInvariant()
            Case "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                 "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
                Return False
        End Select
        Return True
    End Function

End Class

''' <summary>The publish of one local generation into the remote set: published, or the machine reason it was not.</summary>
Friend NotInheritable Class BackupPublishResult

    Private Sub New(published As Boolean, artifact As String, number As Integer, retired As Integer, detail As String,
                    Optional retiredNames As List(Of String) = Nothing, Optional keptCount As Integer = 0,
                    Optional monthlyKept As Integer = 0)
        Me.IsPublished = published
        Me.RemoteArtifact = artifact
        Me.Number = number
        Me.RetiredNumber = retired
        Me.RefusalDetail = detail
        Me.RetiredNames = If(retiredNames, New List(Of String)())
        Me.KeptCount = keptCount
        Me.MonthlyKept = monthlyKept
    End Sub

    Friend ReadOnly Property IsPublished As Boolean
    ''' <summary>The verified remote generation's path, in the server's own spelling.</summary>
    Friend ReadOnly Property RemoteArtifact As String
    ''' <summary>The slot of the remote set the generation was published into (the destination's own numbering).</summary>
    Friend ReadOnly Property Number As Integer
    ''' <summary>The slot the run retired by replacement, 0 when none was.</summary>
    Friend ReadOnly Property RetiredNumber As Integer
    ''' <summary>"" when published; otherwise the wall detail ("code|place"), exactly as the executor writes it.</summary>
    Friend ReadOnly Property RefusalDetail As String
    ''' <summary>Names of dated backups retired after publish on SFTP.</summary>
    Friend ReadOnly Property RetiredNames As List(Of String)
    Friend ReadOnly Property KeptCount As Integer
    Friend ReadOnly Property MonthlyKept As Integer

    Friend Shared Function Published(artifact As String, number As Integer, retired As Integer) As BackupPublishResult
        Return New BackupPublishResult(True, artifact, number, retired, "")
    End Function

    Friend Shared Function PublishedDated(artifact As String, retiredNames As List(Of String),
                                          keptCount As Integer, monthlyKept As Integer) As BackupPublishResult
        Return New BackupPublishResult(True, artifact, 0, 0, "", retiredNames, keptCount, monthlyKept)
    End Function

    Friend Shared Function Refused(detail As String) As BackupPublishResult
        Return New BackupPublishResult(False, "", 0, 0, detail)
    End Function
End Class

''' <summary>
''' The checked publication of one local generation into the destination's numbered set (L0343): FileDO's own
''' commit order, on the server. Build under the owned partial name, read the whole thing back and prove it, then -
''' and only then - at most one whole-name removal (the chosen slot) and one non-replacing rename into place, with
''' a provably-later server modification time proven before any of it. A run that cannot prove a step removes
''' nothing and publishes nothing, so the last good generation stands exactly as it was.
''' </summary>
Friend NotInheritable Class BackupRemotePublish

    ''' <summary>A fresh lock is one written within this window; an older one is a crashed run's leftover and goes.</summary>
    Private Const LockStaleMinutes As Integer = 30

    ''' <summary>Publishes <paramref name="artifactLocal"/> as the next generation of the set named
    ''' <paramref name="stem"/> in <paramref name="side"/>'s folder. Never throws for a provider or disk condition -
    ''' it answers refused - and rethrows cancellation so the run ends Cancelled, having removed nothing.</summary>
    Friend Shared Function Run(side As ProviderSide, stem As String, sealedBackup As Boolean, keep As Integer,
                               artifactLocal As String, ct As CancellationToken) As BackupPublishResult
        Dim provider As IFileSystemProvider = side.Provider
        Dim manager As IManagerDirectoryProvider = TryCast(provider, IManagerDirectoryProvider)
        If manager Is Nothing Then Return BackupPublishResult.Refused("backup-remote-listing|" & side.Describe())
        If Not File.Exists(artifactLocal) Then
            Return BackupPublishResult.Refused("backup-remote-transport|artifact")
        End If
        Dim root As String = side.Root
        Dim extension As String = If(sealedBackup, BackupRemoteSet.SealedExtension, BackupRemoteSet.ZipExtension)
        Dim lockName As String = BackupRemoteSet.LockName(stem)
        Dim lockPath As String = BackupRemoteSet.PathOf(root, lockName)

        Dim rootStat As StatResult = manager.StatEntry(root)
        If rootStat.Outcome <> StatOutcome.Found OrElse Not rootStat.Entry.IsDirectory Then
            Return BackupPublishResult.Refused("backup-remote-listing|" & side.Describe())
        End If

        Dim lockRefused As BackupPublishResult = AcquireLock(provider, lockName, lockPath, ct)
        If lockRefused IsNot Nothing Then Return lockRefused

        Dim partialPath As String = ""
        Dim keepPartial As Boolean = False
        Try
            ' The plan comes from the set's own complete listing, held under the lock.
            Dim first As DirectoryListing = ListComplete(manager, root, ct)
            If first Is Nothing Then Return BackupPublishResult.Refused("backup-remote-listing|" & side.Describe())
            Dim snapshot As List(Of ResourceEntry) = BackupRemoteSet.RelevantEntries(first.Entries, stem)
            Dim present As Dictionary(Of Integer, BackupRemoteGeneration) =
                BackupRemoteSet.Scan(first.Entries, stem, extension, keep)
            Dim number As Integer = BackupRemoteSet.NextNumber(present, keep)
            Dim occupied As Boolean = present.ContainsKey(number)
            Dim finalName As String = BackupRemoteSet.GenerationName(stem, number, extension)
            Dim finalPath As String = BackupRemoteSet.PathOf(root, finalName)
            partialPath = finalPath & BackupRemoteSet.PartialSuffix

            If Not occupied AndAlso BackupRemoteSet.ForeignHolder(first.Entries, finalName) Then
                ' The slot's name is held by something that is not a file of the set: never replaced, never removed.
                Return BackupPublishResult.Refused("backup-remote-place|" & finalName)
            End If

            ' An owned partial of this slot left by an earlier run goes first (the producer's own recovery); one
            ' another run still holds on this machine shows up as a failure and ends the run.
            If Not ClearPartial(provider, partialPath, ct) Then
                Return BackupPublishResult.Refused("backup-remote-transport|" & finalName)
            End If

            ' 1. The upload under the owned partial name.
            Try
                provider.Upload(artifactLocal, partialPath, Nothing, ct)
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                AppFileLogger.LogException("Scheduled operations: publishing " & finalName, ex)
                Return BackupPublishResult.Refused("backup-remote-transport|" & finalName)
            End Try

            ' 2. The read-back: the whole uploaded generation, proved equal to the one FileDO built locally.
            If VerifyReadBack(provider, partialPath, artifactLocal, ct).Length > 0 Then
                Return BackupPublishResult.Refused("backup-remote-verify|" & finalName)
            End If

            ' 3. The timestamp the server gave the new generation must lie strictly after every kept generation's:
            ' equal times lose the producer's tie-break to the higher number, and a wrap could then retire the
            ' newest good generation - so nothing has moved and nothing will.
            Dim newStat As ResourceEntry = Nothing
            If Not provider.TryStat(partialPath, newStat) OrElse newStat Is Nothing Then
                Return BackupPublishResult.Refused("backup-remote-transport|" & finalName)
            End If
            For Each generation As BackupRemoteGeneration In present.Values
                If generation.Number = number Then Continue For
                If newStat.LastWriteUtc <= generation.LastWriteUtc Then
                    AppFileLogger.LogStamped("Scheduled operations: the new generation " & finalName &
                        " is not provably later than generation " &
                        generation.Number.ToString(CultureInfo.InvariantCulture) & "; nothing was published.")
                    Return BackupPublishResult.Refused("backup-remote-time|" & finalName)
                End If
            Next

            ' 4. No observable change between the check and the mutation: the same own-entries listing, or refuse.
            ' The run's own partial is expected to appear and leave, so it is not a change.
            Dim second As DirectoryListing = ListComplete(manager, root, ct)
            If second Is Nothing Then Return BackupPublishResult.Refused("backup-remote-listing|" & side.Describe())
            If Not BackupRemoteSet.SameSnapshot(snapshot, BackupRemoteSet.RelevantEntries(second.Entries, stem),
                                               BackupRemoteSet.GenerationName(stem, number, extension) & PartialSuffix) Then
                Return BackupPublishResult.Refused("backup-remote-changed|" & side.Describe())
            End If

            ' 5. The one removal: the chosen slot's file, by its exact listed path, only when the slot was occupied.
            If occupied Then
                Try
                    provider.DeleteFile(present(number).FullPath)
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As Exception
                    AppFileLogger.LogException("Scheduled operations: retiring " & present(number).Name, ex)
                    Return BackupPublishResult.Refused("backup-remote-transport|" & present(number).Name)
                End Try
            End If

            ' 6. The rename into place. Failing here keeps the verified new generation under its partial name - the
            ' recoverable leftover the producer keeps, with its name in the refusal, until the next locked run.
            Try
                provider.Rename(partialPath, finalPath)
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                AppFileLogger.LogException("Scheduled operations: placing " & finalName, ex)
                keepPartial = True
                Return BackupPublishResult.Refused("backup-remote-move|" & partialName(finalName))
            End Try

            Dim placed As ResourceEntry = Nothing
            If Not provider.TryStat(finalPath, placed) OrElse placed Is Nothing Then
                Return BackupPublishResult.Refused("backup-remote-transport|" & finalName)
            End If
            Return BackupPublishResult.Published(side.Combine(finalName), number, If(occupied, number, 0))
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            ' Nothing above is allowed to fall through unproven: the run continues as refused, with its code.
            AppFileLogger.LogException("Scheduled operations: publishing a generation of " & side.Describe(), ex)
            Return BackupPublishResult.Refused("backup-remote-transport|" & ScheduledFailure.CodeOf(ex))
        Finally
            If partialPath.Length > 0 AndAlso Not keepPartial Then
                Try
                    provider.DeleteFile(partialPath)
                Catch
                End Try
            End If
            Try
                provider.DeleteFile(lockPath)
            Catch
            End Try
        End Try
    End Function

    ''' <summary>
    ''' Publishes a verified dated archive <paramref name="artifactLocal"/> (`&lt;stem&gt;-&lt;yyMMddHHmm&gt;&lt;ext&gt;`) into
    ''' <paramref name="side"/> under the cooperative lock, reads it back in full, renames it into place without replacing,
    ''' and then retires expired older files of the dated set (CLI-EVENT-STREAM 0.18 items I-J).
    ''' </summary>
    Friend Shared Function RunDated(side As ProviderSide, stem As String, sealedBackup As Boolean,
                                    keepDays As Integer, keepMonthly As Boolean,
                                    artifactLocal As String, nowLocal As DateTime,
                                    ct As CancellationToken) As BackupPublishResult
        Dim provider As IFileSystemProvider = side.Provider
        Dim manager As IManagerDirectoryProvider = TryCast(provider, IManagerDirectoryProvider)
        If manager Is Nothing Then Return BackupPublishResult.Refused("backup-remote-listing|" & side.Describe())
        If Not File.Exists(artifactLocal) Then
            Return BackupPublishResult.Refused("backup-remote-transport|artifact")
        End If
        Dim finalName As String = Path.GetFileName(artifactLocal)
        Dim extension As String = If(sealedBackup, BackupRemoteSet.SealedExtension, BackupRemoteSet.ZipExtension)
        Dim parsedStamp As DateTime
        If Not BackupCommandLine.TryParseDatedArchiveStamp(finalName, stem, extension, parsedStamp) Then
            Return BackupPublishResult.Refused("backup-remote-transport|" & finalName)
        End If

        Dim root As String = side.Root
        Dim lockName As String = BackupRemoteSet.LockName(stem)
        Dim lockPath As String = BackupRemoteSet.PathOf(root, lockName)

        Dim rootStat As StatResult = manager.StatEntry(root)
        If rootStat.Outcome <> StatOutcome.Found OrElse Not rootStat.Entry.IsDirectory Then
            Return BackupPublishResult.Refused("backup-remote-listing|" & side.Describe())
        End If

        Dim lockRefused As BackupPublishResult = AcquireLock(provider, lockName, lockPath, ct)
        If lockRefused IsNot Nothing Then Return lockRefused

        Dim partialPath As String = ""
        Dim keepPartial As Boolean = False
        Try
            Dim first As DirectoryListing = ListComplete(manager, root, ct)
            If first Is Nothing Then Return BackupPublishResult.Refused("backup-remote-listing|" & side.Describe())
            If BackupRemoteSet.NameOccupied(first.Entries, finalName) Then
                Return BackupPublishResult.Refused("backup-remote-place|" & finalName)
            End If
            Dim snapshot As List(Of ResourceEntry) = BackupRemoteSet.RelevantEntries(first.Entries, stem, dated:=True)
            Dim finalPath As String = BackupRemoteSet.PathOf(root, finalName)
            partialPath = finalPath & BackupRemoteSet.PartialSuffix

            If Not ClearPartial(provider, partialPath, ct) Then
                Return BackupPublishResult.Refused("backup-remote-transport|" & finalName)
            End If

            Try
                provider.Upload(artifactLocal, partialPath, Nothing, ct)
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                AppFileLogger.LogException("Scheduled operations: publishing " & finalName, ex)
                Return BackupPublishResult.Refused("backup-remote-transport|" & finalName)
            End Try

            If VerifyReadBack(provider, partialPath, artifactLocal, ct).Length > 0 Then
                Return BackupPublishResult.Refused("backup-remote-verify|" & finalName)
            End If

            Dim second As DirectoryListing = ListComplete(manager, root, ct)
            If second Is Nothing Then Return BackupPublishResult.Refused("backup-remote-listing|" & side.Describe())
            If BackupRemoteSet.NameOccupied(second.Entries, finalName) Then
                Return BackupPublishResult.Refused("backup-remote-place|" & finalName)
            End If
            If Not BackupRemoteSet.SameSnapshot(snapshot, BackupRemoteSet.RelevantEntries(second.Entries, stem, dated:=True),
                                               finalName & BackupRemoteSet.PartialSuffix) Then
                Return BackupPublishResult.Refused("backup-remote-changed|" & side.Describe())
            End If

            Try
                provider.Rename(partialPath, finalPath)
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                AppFileLogger.LogException("Scheduled operations: placing " & finalName, ex)
                keepPartial = True
                Return BackupPublishResult.Refused("backup-remote-move|" & partialName(finalName))
            End Try

            Dim placed As ResourceEntry = Nothing
            If Not provider.TryStat(finalPath, placed) OrElse placed Is Nothing Then
                Return BackupPublishResult.Refused("backup-remote-transport|" & finalName)
            End If

            Dim selection As BackupRemoteSet.DatedSelection = BackupRemoteSet.SelectDated(
                second.Entries, stem, extension, keepDays, keepMonthly, nowLocal, finalName)
            Dim retiredNames As New List(Of String)()
            Dim failedRemove As Integer = 0
            For Each victim As ResourceEntry In selection.Retire
                Try
                    provider.DeleteFile(victim.FullPath)
                    retiredNames.Add(victim.Name)
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As Exception
                    failedRemove += 1
                    AppFileLogger.LogException("Scheduled operations: retiring remote backup " & victim.Name, ex)
                End Try
            Next

            Return BackupPublishResult.PublishedDated(side.Combine(finalName), retiredNames,
                                                      selection.KeptCount + failedRemove, selection.MonthlyKept)
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: publishing a dated backup of " & side.Describe(), ex)
            Return BackupPublishResult.Refused("backup-remote-transport|" & ScheduledFailure.CodeOf(ex))
        Finally
            If partialPath.Length > 0 AndAlso Not keepPartial Then
                Try
                    provider.DeleteFile(partialPath)
                Catch
                End Try
            End If
            Try
                provider.DeleteFile(lockPath)
            Catch
            End Try
        End Try
    End Function

    Private Shared Function partialName(finalName As String) As String
        Return finalName & BackupRemoteSet.PartialSuffix
    End Function

    ''' <summary>The immediate listing of the destination folder, or Nothing when it is not a complete one: a folder
    ''' that cannot be read to its end is never an operation manifest.</summary>
    Private Shared Function ListComplete(manager As IManagerDirectoryProvider, root As String,
                                         ct As CancellationToken) As DirectoryListing
        Dim listing As DirectoryListing = manager.ListChildren(root, ct)
        If listing.Outcome = DirectoryOutcome.Cancelled Then Throw New OperationCanceledException()
        If Not listing.IsComplete Then Return Nothing
        Return listing
    End Function

    ''' <summary>
    ''' The cooperative lock. Held and fresh: another cooperating run owns the set, and this one goes away. Held and
    ''' stale: a crashed run's leftover, removed and taken over. The acquisition is a rename onto the lock name, and
    ''' the SFTP rename does not replace: two runs cannot both land on it. Answers Nothing when the lock is ours, or
    ''' the refusal.
    ''' </summary>
    Private Shared Function AcquireLock(provider As IFileSystemProvider, lockName As String, lockPath As String,
                                        ct As CancellationToken) As BackupPublishResult
        Dim holder As ResourceEntry = Nothing
        If provider.TryStat(lockPath, holder) AndAlso holder IsNot Nothing Then
            If holder.LastWriteUtc > Date.UtcNow.AddMinutes(-LockStaleMinutes) Then
                Return BackupPublishResult.Refused("backup-remote-locked|" & lockName)
            End If
            Try
                provider.DeleteFile(lockPath)
            Catch ex As Exception
                AppFileLogger.LogException("Scheduled operations: clearing a stale backup lock", ex)
                Return BackupPublishResult.Refused("backup-remote-locked|" & lockName)
            End Try
        End If

        Dim localLock As String = Path.Combine(Path.GetTempPath(), "fms-backup-lock-" & Guid.NewGuid().ToString("N"))
        Dim tempPath As String = lockPath & "." & Guid.NewGuid().ToString("N") & BackupRemoteSet.PartialSuffix
        Try
            File.WriteAllText(localLock, "FastMediaSorterCompanion backup lock " & DateTime.UtcNow.ToString("o"))
            provider.Upload(localLock, tempPath, Nothing, ct)
            provider.Rename(tempPath, lockPath)
            Return Nothing
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: taking the backup lock " & lockName, ex)
            Try
                provider.DeleteFile(tempPath)
            Catch
            End Try
            Return BackupPublishResult.Refused("backup-remote-locked|" & lockName)
        Finally
            Try
                File.Delete(localLock)
            Catch
            End Try
        End Try
    End Function

    ''' <summary>Removes an owned partial of this slot left by an earlier run. False when the leftover cannot go:
    ''' a partial another run still holds is not replaced under this protocol.</summary>
    Private Shared Function ClearPartial(provider As IFileSystemProvider, partialPath As String,
                                          ct As CancellationToken) As Boolean
        Try
            ct.ThrowIfCancellationRequested()
            Dim leftover As ResourceEntry = Nothing
            If provider.TryStat(partialPath, leftover) AndAlso leftover IsNot Nothing Then
                provider.DeleteFile(partialPath)
            End If
            Return True
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: clearing the owned partial of a slot", ex)
            Return False
        End Try
    End Function

    ''' <summary>Reads the uploaded generation back in full and compares it with the local artifact: length and
    ''' SHA-256. "" when it is the same; otherwise the code of what differed, logged.</summary>
    Private Shared Function VerifyReadBack(provider As IFileSystemProvider, partialPath As String,
                                            artifactLocal As String, ct As CancellationToken) As String
        Dim localRead As String = Path.Combine(Path.GetTempPath(), "fms-backup-readback-" & Guid.NewGuid().ToString("N"))
        Try
            provider.Download(partialPath, localRead, Nothing, ct)
            If New FileInfo(localRead).Length <> New FileInfo(artifactLocal).Length Then
                AppFileLogger.LogStamped("Scheduled operations: the uploaded generation " & partialPath & " read back with another length.")
                Return "length"
            End If
            If Not HashEqual(localRead, artifactLocal) Then
                AppFileLogger.LogStamped("Scheduled operations: the uploaded generation " & partialPath & " read back with another content.")
                Return "hash"
            End If
            Return ""
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: reading back " & partialPath, ex)
            Return "transport"
        Finally
            Try
                File.Delete(localRead)
            Catch
            End Try
        End Try
    End Function

    Private Shared Function HashEqual(a As String, b As String) As Boolean
        Dim first As Byte() = HashOfFile(a)
        Dim second As Byte() = HashOfFile(b)
        If first Is Nothing OrElse second Is Nothing OrElse first.Length <> second.Length Then Return False
        Return CryptographicOperations.FixedTimeEquals(first, second)
    End Function

    Private Shared Function HashOfFile(path As String) As Byte()
        Try
            Using stream As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920)
                Using sha As SHA256 = SHA256.Create()
                    Return sha.ComputeHash(stream)
                End Using
            End Using
        Catch
            Return Nothing
        End Try
    End Function

End Class
