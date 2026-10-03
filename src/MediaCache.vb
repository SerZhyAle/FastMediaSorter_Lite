#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Imaging

''' <summary>
''' One decoded picture the viewer is not showing right now, with everything needed to know it is
''' still the picture of that file: the path, the file's write time and length when it was read, and
''' the one setting that changes what a decode produces (EXIF auto-rotate). The picture and the
''' MemoryStream it was decoded from travel together because the stream is kept open for as long as
''' the image lives (that is what releases the file handle) - whoever drops one drops both.
''' </summary>
Friend NotInheritable Class MediaEntry
    Public Property Path As String = ""
    Public Property WriteTimeUtc As DateTime
    Public Property Length As Long
    Public Property ExifAutoRotate As Boolean
    Public Property Picture As Image
    Public Property Data As IO.MemoryStream

    ''' <summary>What the cache counted this entry at when it took it (<see cref="PixelBytes"/>).
    ''' Stored, not recomputed: removing an entry must subtract exactly what was added, and a
    ''' picture that was disposed behind the cache's back can no longer say its size.</summary>
    Friend Property CostBytes As Long

    ''' <summary>What the entry costs in memory, for the budget: the decoded pixels, at four bytes
    ''' each. An over-estimate for a stream-backed image GDI+ has not drawn yet - the budget is a
    ''' ceiling, and the safe side of a ceiling is high.</summary>
    Public Function PixelBytes() As Long
        If Picture Is Nothing Then Return 0
        Return CLng(Picture.Width) * CLng(Picture.Height) * 4L
    End Function

    ''' <summary>Releases both halves. Never throws: an entry is dropped from places that cannot
    ''' act on a failure (an eviction in the middle of a flip, the shutdown).</summary>
    Public Sub Dispose()
        Try
            Picture?.Dispose()
        Catch ex As Exception
            Debug.WriteLine("MediaEntry: disposing the picture failed: " & ex.Message)
        End Try
        Try
            Data?.Dispose()
        Catch ex As Exception
            Debug.WriteLine("MediaEntry: disposing the stream failed: " & ex.Message)
        End Try
        Picture = Nothing
        Data = Nothing
    End Sub
End Class

''' <summary>
''' The pictures the user has just paged past, kept so that going back to one is not a decode
''' (014_SPECIFICATION_VIEWER_CORE_S11_MODERN_ASYNC.md, U-01). Modern-only.
'''
''' OWNERSHIP IS THE WHOLE DESIGN. A decoded Image has exactly one owner at a time: a picture box
''' or this cache, never both. The specification first sketched a shared reference with a rule
''' ("only the cache disposes, and only when no visible box holds it") - which would have meant
''' teaching every one of the places that replace or release a box's image about the cache, and
''' a miss in any of them is an ObjectDisposedException on the next repaint. Instead the picture is
''' MOVED:
'''   * a box about to lose its image to a newer one offers it here (<see cref="Offer"/>) instead
'''     of disposing it - the cache takes ownership, and disposes what it evicts;
'''   * a flip back asks for it (<see cref="TryTake"/>) - the cache gives ownership away and no
'''     longer holds it, so the picture box that receives it is, as always, its only owner.
''' The sites that must NOT keep a picture (a file about to be moved or deleted, the shutdown) keep
''' disposing it themselves and simply never offer it: a missed offer costs one decode, a missed
''' dispose is the other kind of bug.
'''
''' Nothing is stored that a flip back could not safely reuse: an animated picture (its frame
''' position is state), one rotated on screen (it no longer pictures the file) and one that came
''' out of a secure container (nothing decrypted outlives its display) are never offered - see
''' <see cref="IsCacheable"/>.
'''
''' UI-thread only, by contract: the decode that fills a picture runs elsewhere, but the picture
''' reaches the cache from the swap in the UI thread, and leaves it the same way. No locking
''' because there is nothing to lock against.
''' </summary>
Friend NotInheritable Class MediaCache

    ''' <summary>Five, as specified: enough to step back and forth over a short run without a decode.</summary>
    Friend Const DefaultCapacity As Integer = 5

    ''' <summary>Decoded pixels the cache may hold, whichever of the two limits is hit first.
    ''' Five 24-megapixel photos would be ~480 MB on top of the two the picture boxes already
    ''' hold; this keeps the cache to what a machine that opens such photos can spare.</summary>
    Friend Const DefaultBudgetBytes As Long = 256L * 1024L * 1024L

    ''' <summary>Most recently offered first, so the eviction end is the last node.</summary>
    Private ReadOnly _entries As New LinkedList(Of MediaEntry)()
    Private ReadOnly _capacity As Integer
    Private ReadOnly _budgetBytes As Long
    Private _totalBytes As Long

    Friend Sub New(Optional capacity As Integer = DefaultCapacity,
                   Optional budgetBytes As Long = DefaultBudgetBytes)
        _capacity = Math.Max(0, capacity)
        _budgetBytes = Math.Max(0L, budgetBytes)
    End Sub

    Friend ReadOnly Property Count As Integer
        Get
            Return _entries.Count
        End Get
    End Property

    Friend ReadOnly Property TotalBytes As Long
        Get
            Return _totalBytes
        End Get
    End Property

    ''' <summary>
    ''' May this picture be offered at all? Pure, so the three refusals are tested rather than read.
    ''' </summary>
    ''' <param name="has_Origin">The picture's origin (path + stamps) is known - an image placed by
    ''' a path that does not record one (a secure container's) has none.</param>
    ''' <param name="is_Animated">GIF, or an animation transcoded to one: the frame the picture is
    ''' on is state, and a flip back must start the animation from its first frame.</param>
    ''' <param name="is_Mutated">Rotated in place on screen: the pixels are no longer the file's.</param>
    Friend Shared Function IsCacheable(has_Origin As Boolean, is_Animated As Boolean, is_Mutated As Boolean) As Boolean
        Return has_Origin AndAlso Not is_Animated AndAlso Not is_Mutated
    End Function

    ''' <summary>True for a GIF - what StartGifLoopPlayback animates, and what an animated WEBP/APNG
    ''' is transcoded to by the decoder seam.</summary>
    Friend Shared Function IsAnimatedFormat(picture As Image) As Boolean
        If picture Is Nothing Then Return False
        Try
            Return picture.RawFormat.Equals(ImageFormat.Gif)
        Catch ex As Exception
            ' An image that cannot even say its format is not one to keep.
            Return True
        End Try
    End Function

    ''' <summary>
    ''' Takes ownership of <paramref name="entry"/>. Replaces an older entry for the same path,
    ''' then evicts the least recently offered ones - disposing them - until the count and the
    ''' pixel budget hold again. An entry that alone exceeds the budget is disposed on the spot
    ''' instead of emptying the cache to make room it could never have.
    ''' </summary>
    Friend Sub Offer(entry As MediaEntry)
        If entry Is Nothing Then Return
        Dim cost As Long
        Try
            cost = entry.PixelBytes()
        Catch ex As Exception
            entry.Dispose()
            Return
        End Try

        If _capacity = 0 OrElse cost > _budgetBytes OrElse String.IsNullOrEmpty(entry.Path) Then
            entry.Dispose()
            Return
        End If

        Evict(entry.Path)
        entry.CostBytes = cost
        _entries.AddFirst(entry)
        _totalBytes += cost

        While _entries.Count > _capacity OrElse _totalBytes > _budgetBytes
            Dim oldest As LinkedListNode(Of MediaEntry) = _entries.Last
            If oldest Is Nothing OrElse Object.ReferenceEquals(oldest.Value, entry) Then Exit While
            Remove(oldest)
        End While
    End Sub

    ''' <summary>
    ''' Gives the picture of <paramref name="path"/> back, if the cache holds it and it is still
    ''' the picture of the file as it is NOW (same write time, same length, same decode setting).
    ''' Ownership moves to the caller and the entry leaves the cache. An entry for the path that
    ''' no longer matches is stale and is disposed here, not kept for the next look.
    ''' </summary>
    Friend Function TryTake(path As String, writeTimeUtc As DateTime, length As Long,
                            exifAutoRotate As Boolean) As MediaEntry
        If String.IsNullOrEmpty(path) Then Return Nothing
        Dim node As LinkedListNode(Of MediaEntry) = Find(path)
        If node Is Nothing Then Return Nothing

        Dim entry As MediaEntry = node.Value
        _entries.Remove(node)
        _totalBytes -= entry.CostBytes

        Dim current As Boolean = entry.WriteTimeUtc = writeTimeUtc AndAlso entry.Length = length AndAlso
                                 entry.ExifAutoRotate = exifAutoRotate
        If current Then
            Try
                ' A picture that cannot say its size is not one to hand to a picture box.
                If entry.Picture IsNot Nothing AndAlso entry.Picture.Width > 0 Then Return entry
            Catch ex As Exception
                Debug.WriteLine("MediaCache: a cached picture is unusable: " & ex.Message)
            End Try
        End If
        entry.Dispose()
        Return Nothing
    End Function

    ''' <summary>Drops the entry for <paramref name="path"/>, if any, and disposes it.</summary>
    Friend Sub Evict(path As String)
        If String.IsNullOrEmpty(path) Then Return
        Dim node As LinkedListNode(Of MediaEntry) = Find(path)
        If node IsNot Nothing Then Remove(node)
    End Sub

    ''' <summary>Disposes everything. A new list, a changed decode setting and the shutdown all
    ''' end the cache's usefulness at once.</summary>
    Friend Sub Clear()
        While _entries.Count > 0
            Remove(_entries.First)
        End While
        _totalBytes = 0
    End Sub

    ''' <summary>The path of every entry, most recently offered first - for the tests.</summary>
    Friend Function Paths() As List(Of String)
        Dim result As New List(Of String)(_entries.Count)
        For Each entry As MediaEntry In _entries
            result.Add(entry.Path)
        Next
        Return result
    End Function

    Private Function Find(path As String) As LinkedListNode(Of MediaEntry)
        Dim node As LinkedListNode(Of MediaEntry) = _entries.First
        While node IsNot Nothing
            If String.Equals(node.Value.Path, path, StringComparison.OrdinalIgnoreCase) Then Return node
            node = node.Next
        End While
        Return Nothing
    End Function

    Private Sub Remove(node As LinkedListNode(Of MediaEntry))
        Dim entry As MediaEntry = node.Value
        _entries.Remove(node)
        _totalBytes -= entry.CostBytes
        entry.Dispose()
    End Sub

End Class
#End If
