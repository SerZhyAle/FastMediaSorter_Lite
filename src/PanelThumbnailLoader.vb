#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Threading

' The two halves of "paint the visible cards without a decode that nobody will look at"
' (005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md PG-3):
'   ThumbnailMemoryCache  - the bitmaps the grid holds right now, bounded by BYTES;
'   PanelThumbnailLoader  - the worker threads, fed only what is on screen.
'
' The grid used to build a card (a window handle and a bitmap) for every file up to a
' 600-card ceiling and decode each original in full. It is now one owner-drawn surface: no
' handles at all, a bounded bitmap set, and a queue that is REPLACED every time the visible
' range changes - so scrolling past 300 cards never leaves 300 decodes queued behind the one
' the user stopped on.

''' <summary>
''' Bitmaps by path, least-recently-used out first, bounded by memory rather than by count
''' (a 256 px card is sixteen times the bytes of a 64 px one). UI thread only - the loader
''' hands its results over before they get here.
''' </summary>
Friend NotInheritable Class ThumbnailMemoryCache
    Implements IDisposable

    Private ReadOnly byPath As New Dictionary(Of String, LinkedListNode(Of Entry))(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly order As New LinkedList(Of Entry)()
    Private ReadOnly maxBytes As Long
    Private used As Long

    Private NotInheritable Class Entry
        Public Path As String
        Public Image As Bitmap
        Public Bytes As Long
    End Class

    Friend Sub New(budgetBytes As Long)
        maxBytes = Math.Max(1L, budgetBytes)
    End Sub

    Friend ReadOnly Property Count As Integer
        Get
            Return byPath.Count
        End Get
    End Property

    Friend ReadOnly Property BytesUsed As Long
        Get
            Return used
        End Get
    End Property

    ''' <summary>The bitmap, marked as just used. Nothing when absent. The cache keeps ownership:
    ''' the caller draws it and never disposes it.</summary>
    Friend Function TryGet(path As String) As Bitmap
        Dim node As LinkedListNode(Of Entry) = Nothing
        If path Is Nothing OrElse Not byPath.TryGetValue(path, node) Then Return Nothing
        order.Remove(node)
        order.AddFirst(node)
        Return node.Value.Image
    End Function

    Friend Function Contains(path As String) As Boolean
        Return path IsNot Nothing AndAlso byPath.ContainsKey(path)
    End Function

    ''' <summary>Takes ownership of <paramref name="image"/>. A replaced entry is disposed; the
    ''' oldest are disposed until the budget holds - but never the one just added, so a single
    ''' oversized bitmap is still shown once.</summary>
    Friend Sub Put(path As String, image As Bitmap)
        If path Is Nothing OrElse image Is Nothing Then
            image?.Dispose()
            Return
        End If
        Remove(path)

        Dim entry As New Entry With {.Path = path, .Image = image, .Bytes = CLng(image.Width) * image.Height * 4L}
        byPath(path) = order.AddFirst(entry)
        used += entry.Bytes

        While used > maxBytes AndAlso order.Count > 1
            Dim oldest As LinkedListNode(Of Entry) = order.Last
            RemoveNode(oldest)
        End While
    End Sub

    Friend Sub Remove(path As String)
        Dim node As LinkedListNode(Of Entry) = Nothing
        If path IsNot Nothing AndAlso byPath.TryGetValue(path, node) Then RemoveNode(node)
    End Sub

    Friend Sub Clear()
        While order.Count > 0
            RemoveNode(order.First)
        End While
    End Sub

    Private Sub RemoveNode(node As LinkedListNode(Of Entry))
        order.Remove(node)
        byPath.Remove(node.Value.Path)
        used -= node.Value.Bytes
        Try
            node.Value.Image.Dispose()
        Catch
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Clear()
    End Sub

End Class

''' <summary>
''' Two STA worker threads that make thumbnails for what the grid asks for and hand each
''' result to a callback (the panel marshals it to the UI thread). STA because thumbnail
''' handlers are COM objects and some refuse any other apartment; two because more would only
''' queue behind the disk and the shell's own extraction host.
''' </summary>
Friend NotInheritable Class PanelThumbnailLoader
    Implements IDisposable

    Private ReadOnly service As ThumbnailService
    Private ReadOnly px As Integer
    Private ReadOnly deliver As Action(Of String, Bitmap)
    Private ReadOnly gate As New Object()
    Private ReadOnly wanted As New List(Of PanelFileRow)()
    Private ReadOnly inFlight As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly workers As New List(Of Thread)()
    Private disposed As Boolean

    ''' <param name="deliver">Called on a WORKER thread with the path and its thumbnail, or
    ''' Nothing when there is no way to make one. The receiver owns the bitmap.</param>
    Friend Sub New(service As ThumbnailService, px As Integer, deliver As Action(Of String, Bitmap),
                   Optional workerCount As Integer = 2)
        Me.service = service
        Me.px = px
        Me.deliver = deliver
        For i As Integer = 1 To Math.Max(1, workerCount)
            Dim t As New Thread(AddressOf Work) With {
                .IsBackground = True,
                .Name = "FMS panel thumbnails " & i.ToString(),
                .Priority = ThreadPriority.BelowNormal}
            t.SetApartmentState(ApartmentState.STA)
            workers.Add(t)
            t.Start()
        Next
    End Sub

    ''' <summary>
    ''' REPLACES the queue with these rows, in order - the visible ones first. Whatever was
    ''' queued and is no longer wanted is simply never started; a decode already running is
    ''' allowed to finish and its result is kept for when the user scrolls back.
    ''' </summary>
    Friend Sub SetWanted(rows As IEnumerable(Of PanelFileRow))
        SyncLock gate
            If disposed Then Return
            wanted.Clear()
            If rows IsNot Nothing Then wanted.AddRange(rows)
            Monitor.PulseAll(gate)
        End SyncLock
    End Sub

    Private Sub Work()
        While True
            Dim row As PanelFileRow = Nothing
            SyncLock gate
                While Not disposed
                    row = Next_Unstarted()
                    If row IsNot Nothing Then Exit While
                    Monitor.Wait(gate)
                End While
                If disposed Then Return
                inFlight.Add(row.Path)
            End SyncLock

            Dim made As Bitmap = Nothing
            Try
                made = service.Render(row.Path, px, row.Kind)
            Catch ex As Exception
                AppFileLogger.LogException("Panel thumbnail: " & row.Path, ex)
                made = Nothing
            End Try

            Dim handed As Boolean = False
            Try
                If Not disposed Then
                    deliver(row.Path, made)
                    handed = True
                End If
            Catch ex As Exception
                ' The panel went away between the check and the hand-over.
            Finally
                If Not handed Then made?.Dispose()
                SyncLock gate
                    inFlight.Remove(row.Path)
                    ' It may have been wanted AGAIN while it was being made (the user scrolled
                    ' away and back); the next pass decides.
                    Monitor.PulseAll(gate)
                End SyncLock
            End Try
        End While
    End Sub

    ''' <summary>The first wanted row nobody is already working on, and removes it from the
    ''' queue. Caller holds the gate.</summary>
    Private Function Next_Unstarted() As PanelFileRow
        For i As Integer = 0 To wanted.Count - 1
            Dim candidate As PanelFileRow = wanted(i)
            If inFlight.Contains(candidate.Path) Then Continue For
            wanted.RemoveAt(i)
            Return candidate
        Next
        Return Nothing
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock gate
            If disposed Then Return
            disposed = True
            wanted.Clear()
            Monitor.PulseAll(gate)
        End SyncLock
    End Sub

End Class
#End If
