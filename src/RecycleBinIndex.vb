#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text

' What the Recycle Bin knows about a file it holds, read straight off disk.
' 017_SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.6. Modern build only, like the rest
' of the feature, so this whole file compiles to nothing in the x86 viewer.
'
' A recycled file is a PAIR inside "<volume root>\$Recycle.Bin\<user SID>\":
'   $R<token><ext>  the data, MOVED there - which is why recycling a 200 MB clip is
'                   instant, and why restoring it is a move back rather than a copy;
'   $I<token><ext>  a small fixed-layout record: version, size, deletion time as a
'                   FILETIME, and the full path the file came from.
' Restoring is therefore: find the $I whose record names our path, move the matching $R
' back to it, delete the $I. Explorer's own Restore does exactly this.
'
' WHY NOT Shell.Application. The COM route needs NameSpace(10), FolderItem2.ExtendedProperty
' and either InvokeVerb (whose verb name is LOCALIZED - "Restore" is "Восстановить" on a
' Russian Windows) or MoveHere. All of it is late binding, which Option Strict On forbids
' outright: it would take CallByName throughout, or Option Strict Off for a file, both
' against the house rules. It also needs STA, is slow to enumerate a large bin, and cannot
' be tested without a Recycle Bin. The $I route is managed, ordinal, fast - and its parser
' is a pure function, which is what this file is.
'
' NOTHING HERE EVER WRITES AN $I. A hand-made record that is subtly wrong is a file the
' user cannot get back through Explorer; we only ever read them, and delete the one whose
' data we have already moved out.

''' <summary>One record out of the bin, parsed. Paths are absolute.</summary>
Friend NotInheritable Class RecycleBinRecord
    ''' <summary>The $I file this was read from.</summary>
    Public Property IndexPath As String = ""
    ''' <summary>The matching $R file - the data itself. Empty when the index file is not
    ''' named the way the shell names them, in which case there is nothing to restore.</summary>
    Public Property DataPath As String = ""
    ''' <summary>Where the file was when it was deleted.</summary>
    Public Property OriginalPath As String = ""
    Public Property DeletedUtc As DateTime
    Public Property SizeBytes As Long
End Class

Friend Module RecycleBinIndex

    ' Layout, stable since Vista and versioned:
    '   0  Int64  version (1 = Vista..8.1, 2 = Windows 10+)
    '   8  Int64  size in bytes
    '  16  Int64  deletion time, FILETIME (UTC)
    '  24  v1: 260 WCHAR fixed, null-terminated  |  v2: Int32 length in WCHAR, then the path
    Private Const Header_Bytes As Integer = 24
    Private Const V1_Path_Bytes As Integer = 520          ' 260 WCHAR, MAX_PATH as it was
    Private Const V1_Record_Bytes As Integer = Header_Bytes + V1_Path_Bytes
    ''' <summary>A sane ceiling on the v2 path length. An extended-length path tops out at
    ''' 32 767 characters; anything claiming more is a corrupt record, not a long name, and
    ''' believing it would mean allocating from a number a stranger wrote.</summary>
    Private Const Max_Path_Chars As Integer = 32768

    ''' <summary>
    ''' Two seconds of slack between "when we queued the deletion" and "when the shell
    ''' stamped the record". Both come from the same clock, and the shell always stamps
    ''' later, so this is not correcting for drift - it is making sure a NTP adjustment or
    ''' a clock of coarser resolution can never turn a file that is plainly there into
    ''' "no longer in the Recycle Bin". It cannot make U restore the wrong file: the path
    ''' still has to match exactly, and among matches the newest wins.
    ''' </summary>
    Friend ReadOnly Bin_Clock_Tolerance As TimeSpan = TimeSpan.FromSeconds(2)

    ''' <summary>
    ''' Parses one $I record. Returns Nothing for anything this build does not recognise -
    ''' an unknown version, a truncated file, an unreadable timestamp. An unknown layout is
    ''' REFUSED, never guessed at: the honest refusal ("the file is no longer in the bin")
    ''' costs the user a trip to Explorer, while a guess moves the wrong file over theirs.
    ''' </summary>
    Friend Function TryParse(bytes As Byte(), indexPath As String) As RecycleBinRecord
        If bytes Is Nothing OrElse bytes.Length < Header_Bytes Then Return Nothing

        Dim version As Long = BitConverter.ToInt64(bytes, 0)
        If version <> 1L AndAlso version <> 2L Then Return Nothing

        Dim size_Bytes As Long = BitConverter.ToInt64(bytes, 8)
        If size_Bytes < 0L Then Return Nothing

        Dim deleted_Utc As DateTime
        Try
            deleted_Utc = DateTime.FromFileTimeUtc(BitConverter.ToInt64(bytes, 16))
        Catch
            ' Outside the FILETIME range: the record is damaged, not merely old.
            Return Nothing
        End Try

        Dim original_Path As String
        If version = 1L Then
            If bytes.Length < V1_Record_Bytes Then Return Nothing
            original_Path = TrimAtNull(Encoding.Unicode.GetString(bytes, Header_Bytes, V1_Path_Bytes))
        Else
            If bytes.Length < Header_Bytes + 4 Then Return Nothing
            Dim char_Count As Integer = BitConverter.ToInt32(bytes, Header_Bytes)
            If char_Count <= 0 OrElse char_Count > Max_Path_Chars Then Return Nothing
            If bytes.Length < Header_Bytes + 4 + char_Count * 2 Then Return Nothing
            original_Path = TrimAtNull(Encoding.Unicode.GetString(bytes, Header_Bytes + 4, char_Count * 2))
        End If

        If original_Path.Length = 0 Then Return Nothing

        Return New RecycleBinRecord With {
            .IndexPath = If(indexPath, ""),
            .DataPath = DataPathFor(indexPath),
            .OriginalPath = original_Path,
            .DeletedUtc = deleted_Utc,
            .SizeBytes = size_Bytes}
    End Function

    ''' <summary>
    ''' The $R that belongs to a $I: same folder, same token, same extension, one letter
    ''' apart. Empty for a name that is not shaped like an index file - there is then no
    ''' data file to pair it with, and inventing one would move an unrelated file.
    ''' </summary>
    Friend Function DataPathFor(indexPath As String) As String
        If String.IsNullOrEmpty(indexPath) Then Return ""

        ' By hand rather than through Path.GetFileName/GetDirectoryName so that a name the
        ' shell wrote but this parser does not expect cannot throw on the way through.
        Dim cut_At As Integer = indexPath.LastIndexOfAny(New Char() {"\"c, "/"c})
        Dim folder As String = If(cut_At >= 0, indexPath.Substring(0, cut_At + 1), "")
        Dim name As String = indexPath.Substring(cut_At + 1)

        If Not name.StartsWith("$I", StringComparison.OrdinalIgnoreCase) Then Return ""
        Return folder & "$R" & name.Substring(2)
    End Function

    ''' <summary>
    ''' Picks the record that belongs to a deletion WE made: the same full path, stamped at
    ''' or after the moment we queued it, newest first.
    '''
    ''' Both halves of that are load-bearing. Without the path, U after deleting two files
    ''' would restore whichever the bin holds most recently - the acceptance case that
    ''' deletes 'cover.jpg' from two folders exists for exactly this. Without the time, a
    ''' file deleted, restored and deleted again leaves two records for one path and the
    ''' older one would win.
    ''' </summary>
    Friend Function BestMatch(records As IEnumerable(Of RecycleBinRecord),
                              originalPath As String,
                              deletedAtUtc As DateTime) As RecycleBinRecord
        If records Is Nothing OrElse String.IsNullOrEmpty(originalPath) Then Return Nothing

        Dim floor_Utc As DateTime = deletedAtUtc - Bin_Clock_Tolerance
        Dim best As RecycleBinRecord = Nothing

        For Each record As RecycleBinRecord In records
            If record Is Nothing Then Continue For
            If Not String.Equals(record.OriginalPath, originalPath, StringComparison.OrdinalIgnoreCase) Then Continue For
            If record.DeletedUtc < floor_Utc Then Continue For
            If best Is Nothing OrElse record.DeletedUtc > best.DeletedUtc Then best = record
        Next

        Return best
    End Function

    ''' <summary>The fixed-width v1 field is padded with nulls, and v2's length includes the
    ''' terminator - both end up as a String with a tail this has to cut.</summary>
    Private Function TrimAtNull(raw As String) As String
        If String.IsNullOrEmpty(raw) Then Return ""
        Dim null_At As Integer = raw.IndexOf(ChrW(0))
        Return If(null_At >= 0, raw.Substring(0, null_At), raw)
    End Function

End Module
#End If
