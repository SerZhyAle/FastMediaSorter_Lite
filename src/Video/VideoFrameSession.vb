#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Threading
Imports System.Threading.Tasks
Imports System.Collections.Generic

''' <summary>UI-thread-owned frame history, with exact decoder timestamps and a
''' bounded image cache. Older frames are decoded again when stepping beyond the cache.</summary>
Friend NotInheritable Class VideoFrameSession
    Implements IDisposable

    Friend Const IntervalMs As Integer = 1000
    Private Const CacheBytes As Long = 64L * 1024 * 1024
    Private ReadOnly executable As String
    Private ReadOnly sourcePath As String
    Private ReadOnly cacheLimit As Long
    Private ReadOnly stopping As New CancellationTokenSource()
    Private decoder As VideoFrameDecoder
    Private history As New List(Of DecodedVideoFrame)()
    Private ReadOnly retired As New List(Of DecodedVideoFrame)()
    Private cursor As Integer = -1
    Private bytes As Long
    Private disposed As Boolean

    Friend Sub New(executable As String, sourcePath As String, Optional cacheLimitBytes As Long = CacheBytes)
        Me.executable = executable
        Me.sourcePath = sourcePath
        cacheLimit = Math.Max(1, Math.Min(CacheBytes, cacheLimitBytes))
    End Sub

    Friend ReadOnly Property Current As DecodedVideoFrame
        Get
            If cursor < 0 OrElse cursor >= history.Count Then Return Nothing
            Return history(cursor)
        End Get
    End Property

    Friend Async Function OpenAsync(timeMs As Long, previous As Boolean) As Task
        Dim preroll As Long = 2000
        Do
            Dim startMs As Long = Math.Max(0, timeMs - preroll)
            decoder = New VideoFrameDecoder(executable, sourcePath, startMs)
            Do
                Dim frame As DecodedVideoFrame = Await decoder.ReadNextAsync(stopping.Token)
                If disposed Then
                    If frame IsNot Nothing Then frame.Dispose()
                    Throw New OperationCanceledException()
                End If
                If frame Is Nothing Then Exit Do
                AddFrame(frame)
                If frame.TimeMs >= timeMs Then Exit Do
            Loop
            If startMs = 0 OrElse (Current IsNot Nothing AndAlso (Not previous OrElse cursor > 0)) Then
                If Current Is Nothing Then Throw New IO.InvalidDataException("The file has no decoded video frames.")
                If previous AndAlso cursor > 0 Then cursor -= 1
                Exit Do
            End If
            ' Sparse video can have more than two seconds between pictures. Expand
            ' the preroll until the actual preceding frame is available, or the start is reached.
            decoder.Dispose()
            For Each frame As DecodedVideoFrame In history
                frame.Dispose()
            Next
            history.Clear()
            cursor = -1
            bytes = 0
            preroll = Math.Min(timeMs, If(preroll > Long.MaxValue \ 2, Long.MaxValue, preroll * 2))
        Loop
    End Function

    Private Sub AddFrame(frame As DecodedVideoFrame)
        history.Add(frame)
        bytes += CLng(frame.Image.Width) * frame.Image.Height * 4
        cursor = history.Count - 1
        While bytes > cacheLimit AndAlso history.Count > 2
            Dim oldest As DecodedVideoFrame = history(0)
            bytes -= CLng(oldest.Image.Width) * oldest.Image.Height * 4
            history.RemoveAt(0)
            cursor -= 1
            oldest.Dispose()
        End While
    End Sub

    Friend Async Function MoveAsync(forward As Boolean) As Task(Of Boolean)
        If disposed Then Throw New OperationCanceledException()
        If forward Then
            If cursor + 1 < history.Count Then
                cursor += 1
                Return True
            End If
            Dim frame As DecodedVideoFrame = Await decoder.ReadNextAsync(stopping.Token)
            If disposed Then
                If frame IsNot Nothing Then frame.Dispose()
                Throw New OperationCanceledException()
            End If
            If frame Is Nothing Then Return False
            AddFrame(frame)
            Return True
        End If
        If cursor > 0 Then
            cursor -= 1
            Return True
        End If
        If Current Is Nothing OrElse Current.TimeMs <= 0 Then Return False
        Dim replacement As New VideoFrameSession(executable, sourcePath, cacheLimit)
        Using cancellation As CancellationTokenRegistration = stopping.Token.Register(Sub() replacement.Dispose())
            Try
                Await replacement.OpenAsync(Current.TimeMs, previous:=True)
                If disposed Then Throw New OperationCanceledException()
                decoder.Dispose()
                retired.AddRange(history)
                history = replacement.history
                cursor = replacement.cursor
                bytes = replacement.bytes
                decoder = replacement.decoder
                replacement.history = New List(Of DecodedVideoFrame)()
                replacement.decoder = Nothing
                replacement.ReleaseRetired()
            Finally
                replacement.Dispose()
            End Try
        End Using
        Return True
    End Function

    Friend Sub ReleaseRetired()
        For Each frame As DecodedVideoFrame In retired
            frame.Dispose()
        Next
        retired.Clear()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If disposed Then Return
        disposed = True
        stopping.Cancel()
        If decoder IsNot Nothing Then decoder.Dispose()
        For Each frame As DecodedVideoFrame In history
            frame.Dispose()
        Next
        history.Clear()
        ReleaseRetired()
        stopping.Dispose()
    End Sub
End Class
#End If
