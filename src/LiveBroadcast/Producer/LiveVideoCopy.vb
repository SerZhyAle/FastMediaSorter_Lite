#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 263 T-2 adds the letterboxed fold (FoldFrameLetterboxed) the window route uses: a window can be
' resized at any moment, and a picture of another shape is scaled into the fixed buffer with bars, never
' stretched. FoldFrame below keeps its older clamp-and-black contract and its tests.
'
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

    ''' <summary>The largest rectangle of the source's aspect that fits the destination, centred: the part
    ''' of the fixed buffer the picture occupies. The rest is the black bars. Integer maths, rounded to the
    ''' nearest pixel, never larger than the destination and never empty.</summary>
    Public Shared Sub LetterboxRect(srcWidth As Integer, srcHeight As Integer, dstWidth As Integer, dstHeight As Integer,
                                    ByRef outWidth As Integer, ByRef outHeight As Integer,
                                    ByRef offsetX As Integer, ByRef offsetY As Integer)
        outWidth = Math.Max(1, dstWidth)
        outHeight = Math.Max(1, dstHeight)
        offsetX = 0
        offsetY = 0
        If srcWidth <= 0 OrElse srcHeight <= 0 OrElse dstWidth <= 0 OrElse dstHeight <= 0 Then Return
        If CLng(srcWidth) * dstHeight >= CLng(srcHeight) * dstWidth Then
            ' Relatively wider (or the same shape): the width is the limit, bars above and below.
            outWidth = dstWidth
            outHeight = CInt(Math.Min(CLng(dstHeight), Math.Max(1L, (CLng(srcHeight) * dstWidth + srcWidth \ 2) \ srcWidth)))
        Else
            ' Relatively taller: the height is the limit, bars left and right.
            outHeight = dstHeight
            outWidth = CInt(Math.Min(CLng(dstWidth), Math.Max(1L, (CLng(srcWidth) * dstHeight + srcHeight \ 2) \ srcHeight)))
        End If
        offsetX = (dstWidth - outWidth) \ 2
        offsetY = (dstHeight - outHeight) \ 2
    End Sub

    ''' <summary>Ticket 263 T-2: folds src (srcWidth x srcHeight, tightly packed BGRA) into the fixed buffer
    ''' dst (dstWidth x dstHeight) WITHOUT stretching. The same size is a plain copy; a different size is
    ''' scaled (nearest pixel, centred sampling) to the largest rectangle of the source's own aspect that
    ''' fits, centred, and the bars around it are written black. A window resized mid-stream keeps
    ''' broadcasting into the size the encoder was told at start - it never restarts the stream and never
    ''' distorts the picture. Wrong sizes or short arrays are ignored, not thrown.</summary>
    Public Shared Sub FoldFrameLetterboxed(src As Byte(), srcWidth As Integer, srcHeight As Integer,
                                           dst As Byte(), dstWidth As Integer, dstHeight As Integer)
        If src Is Nothing OrElse dst Is Nothing Then Return
        If srcWidth <= 0 OrElse srcHeight <= 0 OrElse dstWidth <= 0 OrElse dstHeight <= 0 Then Return
        Dim srcBytes As Long = CLng(srcWidth) * srcHeight * 4L
        Dim dstBytes As Long = CLng(dstWidth) * dstHeight * 4L
        If srcBytes > Integer.MaxValue OrElse dstBytes > Integer.MaxValue Then Return
        If src.Length < srcBytes OrElse dst.Length < dstBytes Then Return

        If srcWidth = dstWidth AndAlso srcHeight = dstHeight Then
            Buffer.BlockCopy(src, 0, dst, 0, CInt(dstBytes))
            Return
        End If

        Dim outWidth As Integer, outHeight As Integer, offsetX As Integer, offsetY As Integer
        LetterboxRect(srcWidth, srcHeight, dstWidth, dstHeight, outWidth, outHeight, offsetX, offsetY)

        ' The bars: whole rows above and below, a strip at each side of the picture's rows.
        Dim dstStride As Integer = dstWidth * 4
        Dim rightBar As Integer = dstWidth - offsetX - outWidth
        For y As Integer = 0 To dstHeight - 1
            If y < offsetY OrElse y >= offsetY + outHeight Then
                Array.Clear(dst, y * dstStride, dstStride)
            Else
                If offsetX > 0 Then Array.Clear(dst, y * dstStride, offsetX * 4)
                If rightBar > 0 Then Array.Clear(dst, y * dstStride + (offsetX + outWidth) * 4, rightBar * 4)
            End If
        Next

        ' The picture: one 32-bit pixel at a time. VB has no Span, so a row is moved through a pair of Int32
        ' scratch rows (one source row in, one output row out) with Buffer.BlockCopy - two bulk copies per
        ' output row and a tight loop in between; consecutive output rows of a scale-up share the source row.
        Dim columnMap(outWidth - 1) As Integer
        For x As Integer = 0 To outWidth - 1
            columnMap(x) = CInt(((2L * x + 1L) * srcWidth) \ (2L * outWidth))
        Next
        Dim sourceRowPixels(srcWidth - 1) As Integer
        Dim outputRowPixels(outWidth - 1) As Integer
        Dim loadedRow As Integer = -1
        For y As Integer = 0 To outHeight - 1
            Dim sourceRow As Integer = CInt(((2L * y + 1L) * srcHeight) \ (2L * outHeight))
            If sourceRow <> loadedRow Then
                Buffer.BlockCopy(src, sourceRow * srcWidth * 4, sourceRowPixels, 0, srcWidth * 4)
                For x As Integer = 0 To outWidth - 1
                    outputRowPixels(x) = sourceRowPixels(columnMap(x))
                Next
                loadedRow = sourceRow
            End If
            Buffer.BlockCopy(outputRowPixels, 0, dst, ((offsetY + y) * dstWidth + offsetX) * 4, outWidth * 4)
        Next
    End Sub

    ''' <summary>True when the capture pool must be recreated at the content's new size: the picture changed
    ''' size (a window resized) and the new size is real. A zero or negative content size is what a minimised
    ''' window reports - nothing to do, the pacing thread keeps sending the last picture.</summary>
    Public Shared Function NeedsPoolRecreate(poolWidth As Integer, poolHeight As Integer, contentWidth As Integer, contentHeight As Integer) As Boolean
        If contentWidth <= 0 OrElse contentHeight <= 0 Then Return False
        Return contentWidth <> poolWidth OrElse contentHeight <> poolHeight
    End Function

    ''' <summary>The part of a pooled surface that holds the picture. Between a window resize and the pool's
    ''' recreation the surface still has its OLD size while the content is the new one: a shrunk window leaves
    ''' stale pixels right of and below the content, which must never reach the stream. An unknown content
    ''' size (zero) means the whole surface.</summary>
    Public Shared Sub ContentRegion(surfaceWidth As Integer, surfaceHeight As Integer, contentWidth As Integer, contentHeight As Integer,
                                    ByRef regionWidth As Integer, ByRef regionHeight As Integer)
        If contentWidth <= 0 OrElse contentHeight <= 0 Then
            regionWidth = surfaceWidth
            regionHeight = surfaceHeight
        Else
            regionWidth = Math.Min(surfaceWidth, contentWidth)
            regionHeight = Math.Min(surfaceHeight, contentHeight)
        End If
    End Sub

End Class
#End If
