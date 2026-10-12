#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 199 section 4, the screen source's pure half: the capture hands over frames at whatever
' size the monitor runs, while the encoder is told ONE fixed input size at start (the rawvideo -s
' argument cannot change mid-stream). LiveVideoCopy folds an arriving frame into the fixed buffer:
' same size is a row copy, a different size is copied clamped and the remainder stays black - a
' monitor that changes resolution mid-broadcast keeps broadcasting instead of killing the stream,
' letterboxed.
'
' Pure byte math, so the suite drives it without a device (section 8).

''' <summary>The screen source's frame fold: tightly packed BGRA rows in, the fixed buffer out.</summary>
Friend NotInheritable Class LiveVideoCopy

    Private Sub New()
    End Sub

    ''' <summary>Copies src (srcWidth x srcHeight, tightly packed BGRA) into dst (dstWidth x dstHeight).
    ''' Bytes beyond the copy stay as they were - the caller clears dst once at start and every fold
    ''' after a shrink writes black once, by writing zeros over the region the copy no longer covers.</summary>
    Public Shared Sub FoldFrame(src As Byte(), srcWidth As Integer, srcHeight As Integer,
                                dst As Byte(), dstWidth As Integer, dstHeight As Integer)
        If src Is Nothing OrElse dst Is Nothing Then Return
        If srcWidth <= 0 OrElse srcHeight <= 0 OrElse dstWidth <= 0 OrElse dstHeight <= 0 Then Return

        Dim copyWidth As Integer = Math.Min(srcWidth, dstWidth)
        Dim copyHeight As Integer = Math.Min(srcHeight, dstHeight)
        Dim srcStride As Integer = srcWidth * 4
        Dim dstStride As Integer = dstWidth * 4

        ' A shrink leaves stale pixels to the right and below the copied region: clear those rows and
        ' the right strip once per fold, so a resolution change never bleeds the old picture through.
        For y As Integer = copyHeight To dstHeight - 1
            Array.Clear(dst, y * dstStride, dstStride)
        Next
        If copyWidth < dstWidth Then
            For y As Integer = 0 To copyHeight - 1
                Array.Clear(dst, y * dstStride + copyWidth * 4, (dstWidth - copyWidth) * 4)
            Next
        End If

        For y As Integer = 0 To copyHeight - 1
            Array.Copy(src, y * srcStride, dst, y * dstStride, copyWidth * 4)
        Next
    End Sub

End Class
#End If
