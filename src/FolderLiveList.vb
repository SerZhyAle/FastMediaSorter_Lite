#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices

' The pure half of the live folder list (020_SPECIFICATION_VIEWER_CORE_S9_MODERN_FOLDER.md, U-04).
' Modern-only.
'
' The list the viewer pages through is a one-off snapshot of the folder. What the watcher in
' Main_Form.FolderWatch.vb learns is only "these paths were touched"; this file turns that into
' edits of the snapshot, and it is kept free of the window so the one thing that can go wrong
' silently - a list that drifts away from what a fresh scan would have produced - can be proven
' without one.
'
' THE RULE EVERYTHING HERE SERVES: after any batch, the list is the list a rescan would have built
' (same sort, same tie-breaking), except that the file on screen never moves under the user. A new
' file is placed by the sort that is active, not appended; a vanished one is dropped; the index of
' the file on screen follows the shifts.
'
' Reconciling by STATE, not by event: the watcher's events are only a set of touched paths, and the
' caller asks the disk what is true of each one. Events then need no ordering, a Created for a file
' that is already listed costs nothing, and the app's own optimistic list edits (a move removes its
' row before the file has left) cannot be undone by the echo of the same operation.
Friend Enum LiveSortKind
    NameAscending       ' "abc"
    NameDescending      ' "xyz"
    NaturalAscending    ' "<0123"
    NaturalDescending   ' ">3210"
    SizeDescending      ' ">size"
    SizeAscending       ' "<size"
    TimeDescending      ' ">time"
    TimeAscending       ' "<time"
    Unordered           ' "rnd": the order is arbitrary, so a newcomer goes to the end
End Enum

''' <summary>What a sort by size or time needs to know about a file.</summary>
Friend NotInheritable Class LiveFileFact
    Friend ReadOnly Path As String
    Friend ReadOnly Length As Long
    ''' <summary>Local time, as the scan carries it (LastWriteTimeUtc.ToLocalTime()).</summary>
    Friend ReadOnly WriteTime As Date

    Friend Sub New(path As String, length As Long, writeTime As Date)
        Me.Path = path
        Me.Length = length
        Me.WriteTime = writeTime
    End Sub
End Class

''' <summary>The outcome of a batch: how many rows changed and where the file on screen is now.</summary>
Friend NotInheritable Class LiveListChange
    Friend Added As Integer
    Friend Removed As Integer
    Friend CurrentIndex As Integer
End Class

Friend NotInheritable Class FolderLiveList

    Private Sub New()
    End Sub

    <DllImport("shlwapi.dll", CharSet:=CharSet.Unicode, ExactSpelling:=True)>
    Private Shared Function StrCmpLogicalW(psz1 As String, psz2 As String) As Integer
    End Function

    ''' <summary>The marker of the editor's temporary file (<c>photo.fms-tmp.jpg</c>): it carries a
    ''' real picture extension for a few seconds and is never a file of the folder.</summary>
    Private Const temp_Marker As String = ".fms-tmp"

    ''' <summary>The sort box's own labels - the only ones <c>GetFiles</c> orders by. A label nobody
    ''' knows is "no known order", the same arbitrary answer the random sort gives.</summary>
    Friend Shared Function SortKindOf(label As String) As LiveSortKind
        Select Case label
            Case "abc" : Return LiveSortKind.NameAscending
            Case "xyz" : Return LiveSortKind.NameDescending
            Case "<0123" : Return LiveSortKind.NaturalAscending
            Case ">3210" : Return LiveSortKind.NaturalDescending
            Case ">size" : Return LiveSortKind.SizeDescending
            Case "<size" : Return LiveSortKind.SizeAscending
            Case ">time" : Return LiveSortKind.TimeDescending
            Case "<time" : Return LiveSortKind.TimeAscending
            Case Else : Return LiveSortKind.Unordered
        End Select
    End Function

    ''' <summary>True for the orders that read a file's size or time - the only ones for which a
    ''' newcomer cannot be placed from its path alone.</summary>
    Friend Shared Function NeedsMetadata(kind As LiveSortKind) As Boolean
        Select Case kind
            Case LiveSortKind.SizeAscending, LiveSortKind.SizeDescending,
                 LiveSortKind.TimeAscending, LiveSortKind.TimeDescending
                Return True
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>
    ''' Does <paramref name="a"/> come before (&lt;0), tie with (0) or come after (&gt;0)
    ''' <paramref name="b"/> in the order <c>GetFiles</c> builds? The comparers are the ones its
    ''' <c>OrderBy</c> calls use: the default string comparer (culture-aware - UseNls pins it to
    ''' net48's) for "abc"/"xyz", the shell's natural comparer for the digit sorts. A missing fact
    ''' in a size/time order answers "after": nothing better is known, and any consistent answer
    ''' keeps the binary search terminating.
    ''' </summary>
    Friend Shared Function Compare(kind As LiveSortKind,
                                   a_Path As String, a_Fact As LiveFileFact,
                                   b_Path As String, b_Fact As LiveFileFact) As Integer
        Select Case kind
            Case LiveSortKind.NameAscending
                Return String.Compare(Path.GetFileName(a_Path), Path.GetFileName(b_Path), StringComparison.CurrentCulture)
            Case LiveSortKind.NameDescending
                Return String.Compare(Path.GetFileName(b_Path), Path.GetFileName(a_Path), StringComparison.CurrentCulture)
            Case LiveSortKind.NaturalAscending
                Return StrCmpLogicalW(Path.GetFileName(a_Path), Path.GetFileName(b_Path))
            Case LiveSortKind.NaturalDescending
                Return StrCmpLogicalW(Path.GetFileName(b_Path), Path.GetFileName(a_Path))
            Case LiveSortKind.SizeDescending
                If a_Fact Is Nothing OrElse b_Fact Is Nothing Then Return 1
                Return b_Fact.Length.CompareTo(a_Fact.Length)
            Case LiveSortKind.SizeAscending
                If a_Fact Is Nothing OrElse b_Fact Is Nothing Then Return 1
                Return a_Fact.Length.CompareTo(b_Fact.Length)
            Case LiveSortKind.TimeDescending
                If a_Fact Is Nothing OrElse b_Fact Is Nothing Then Return 1
                Return b_Fact.WriteTime.CompareTo(a_Fact.WriteTime)
            Case LiveSortKind.TimeAscending
                If a_Fact Is Nothing OrElse b_Fact Is Nothing Then Return 1
                Return a_Fact.WriteTime.CompareTo(b_Fact.WriteTime)
            Case Else
                ' Unordered: the existing row is "before" the newcomer, so it lands last.
                Return -1
        End Select
    End Function

    ''' <summary>Orders newcomers among themselves, stably - the same tie-break the scan's
    ''' <c>OrderBy</c> has.</summary>
    Private NotInheritable Class FactComparer
        Implements IComparer(Of LiveFileFact)

        Private ReadOnly sort_Kind As LiveSortKind

        Friend Sub New(kind As LiveSortKind)
            sort_Kind = kind
        End Sub

        Public Function Compare(x As LiveFileFact, y As LiveFileFact) As Integer Implements IComparer(Of LiveFileFact).Compare
            Return FolderLiveList.Compare(sort_Kind, x.Path, x, y.Path, y)
        End Function
    End Class

    ''' <summary>
    ''' The first row at or after <paramref name="from"/> that sorts strictly after the newcomer, so
    ''' a newcomer that ties with rows already there goes behind them - what a stable sort of the
    ''' whole folder would do. Assumes the rows from <paramref name="from"/> on are in order.
    ''' </summary>
    Friend Shared Function UpperBound(rows As List(Of String), from As Integer, fact As LiveFileFact,
                                      kind As LiveSortKind, lookup As Func(Of String, LiveFileFact)) As Integer
        Dim lo As Integer = Math.Max(0, from)
        Dim hi As Integer = rows.Count
        While lo < hi
            Dim mid As Integer = lo + (hi - lo) \ 2
            Dim existing As String = rows(mid)
            Dim existing_Fact As LiveFileFact = If(lookup Is Nothing, Nothing, lookup(existing))
            If Compare(kind, existing, existing_Fact, fact.Path, fact) <= 0 Then
                lo = mid + 1
            Else
                hi = mid
            End If
        End While
        Return lo
    End Function

    ''' <summary>
    ''' Applies one batch to <paramref name="rows"/> in place: the vanished paths leave, the new
    ''' ones go in at their place in the active order, and the index of the file on screen is
    ''' carried along. The file on screen is never removed - a delete from outside is left for the
    ''' display path, which already knows what to do when the file under the cursor is gone.
    ''' </summary>
    ''' <remarks>One pass over the list for the removals and one merge for the additions (a
    ''' binary search per newcomer, then a block copy), so a thousand arrivals cost a thousand
    ''' searches and one copy of the list rather than a thousand inserts into the middle of it.</remarks>
    Friend Shared Function Apply(rows As List(Of String), current_Index As Integer,
                                 added As IEnumerable(Of LiveFileFact), removed As IEnumerable(Of String),
                                 kind As LiveSortKind, lookup As Func(Of String, LiveFileFact)) As LiveListChange
        Dim change As New LiveListChange With {.CurrentIndex = current_Index}
        If rows Is Nothing Then Return change

        ' ---- removals: one compaction pass ---------------------------------------------------
        Dim doomed As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If removed IsNot Nothing Then
            For Each path As String In removed
                If Not String.IsNullOrEmpty(path) Then doomed.Add(path)
            Next
        End If
        If doomed.Count > 0 Then
            Dim write As Integer = 0
            Dim new_Current As Integer = current_Index
            For read As Integer = 0 To rows.Count - 1
                If read <> current_Index AndAlso doomed.Contains(rows(read)) Then
                    change.Removed += 1
                    If read < current_Index Then new_Current -= 1
                    Continue For
                End If
                If write <> read Then rows(write) = rows(read)
                write += 1
            Next
            If write < rows.Count Then rows.RemoveRange(write, rows.Count - write)
            change.CurrentIndex = new_Current
        End If

        ' ---- additions: sorted, then merged --------------------------------------------------
        If added Is Nothing Then Return change

        Dim present As New HashSet(Of String)(rows, StringComparer.OrdinalIgnoreCase)
        Dim fresh As New List(Of LiveFileFact)()
        For Each fact As LiveFileFact In added
            If fact Is Nothing OrElse String.IsNullOrEmpty(fact.Path) Then Continue For
            If present.Add(fact.Path) Then fresh.Add(fact)
        Next
        If fresh.Count = 0 Then Return change

        ' Unordered rows take newcomers at the end in the order they arrived; every other order
        ' sorts them first, stably, so the merge below only ever walks forward.
        Dim newcomers As List(Of LiveFileFact) =
            If(kind = LiveSortKind.Unordered, fresh, fresh.OrderBy(Function(f) f, New FactComparer(kind)).ToList())

        Dim merged As New List(Of String)(rows.Count + newcomers.Count)
        Dim copied As Integer = 0
        Dim shift As Integer = 0
        For Each fact As LiveFileFact In newcomers
            Dim at As Integer = UpperBound(rows, copied, fact, kind, lookup)
            ' The block of old rows in front of the newcomer, then the newcomer itself.
            If at > copied Then merged.AddRange(rows.GetRange(copied, at - copied))
            merged.Add(fact.Path)
            copied = at
            ' Everything from the insertion point on - the file on screen included - moves down one.
            If at <= change.CurrentIndex Then shift += 1
        Next
        If copied < rows.Count Then merged.AddRange(rows.GetRange(copied, rows.Count - copied))

        rows.Clear()
        rows.AddRange(merged)
        change.Added = newcomers.Count
        change.CurrentIndex += shift
        Return change
    End Function

    ''' <summary>
    ''' What is true of each touched path RIGHT NOW: a file that is there and listable
    ''' (<paramref name="present"/>, with its size and time) or a path that is not
    ''' (<paramref name="absent"/>). Anything else - not a kind of file the viewer lists, the
    ''' editor's temporary file, a file with no bytes yet, one that could not be asked about -
    ''' is in neither, which is "change nothing".
    ''' </summary>
    ''' <remarks>
    ''' The empty file is deliberately skipped, not listed: a download creates it before the first
    ''' byte, and a row for it is a file the display path would have to skip over. It is listed by
    ''' the <c>Changed</c> event that follows the first write.
    '''
    ''' Called from a pool thread: it touches no control and no field of the window.
    ''' </remarks>
    Friend Shared Sub Classify(paths As IEnumerable(Of String), supported_Extensions As ICollection(Of String),
                               present As List(Of LiveFileFact), absent As List(Of String))
        For Each path As String In paths
            Dim name As String = Global.System.IO.Path.GetFileName(path)
            If String.IsNullOrEmpty(name) Then Continue For
            If name.IndexOf(temp_Marker, StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If Not supported_Extensions.Contains(Global.System.IO.Path.GetExtension(name).ToLowerInvariant()) Then Continue For
            Try
                Dim info As New FileInfo(path)
                If info.Exists Then
                    If info.Length > 0 Then present.Add(New LiveFileFact(path, info.Length, info.LastWriteTime))
                ElseIf Not Directory.Exists(path) Then
                    absent.Add(path)
                End If
            Catch ex As Exception
                ' Denied, a path that went bad, a dropped session: not a statement about the file.
            End Try
        Next
    End Sub

End Class
#End If
