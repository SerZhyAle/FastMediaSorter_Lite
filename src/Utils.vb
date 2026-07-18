Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

Module Utils

    ''' <summary>
    ''' Adds an element to an array at the specified index.
    ''' </summary>
    Public Function AddAt(Of T)(ByVal arr As T(), ByVal newOne As T, ByVal index As Integer) As T()
        Dim uBound = arr.GetUpperBound(0)
        Dim lBound = arr.GetLowerBound(0)
        Dim outArr(uBound + 1) As T
        Array.Copy(arr, lBound, outArr, lBound, index)
        outArr(index) = newOne
        Array.Copy(arr, index, outArr, index + 1, uBound - index + 1)
        Return outArr
    End Function

    ''' <summary>
    ''' Removes an element from an array at the specified index.
    ''' </summary>
    Public Function RemoveAt(Of T)(ByVal arr As T(), ByVal index As Integer) As T()
        Dim uBound = arr.GetUpperBound(0)
        Dim lBound = arr.GetLowerBound(0)
        If uBound < lBound Then Return New T() {}
        Dim outArr(uBound - 1) As T
        Array.Copy(arr, lBound, outArr, lBound, index)
        Array.Copy(arr, index + 1, outArr, index, uBound - index)
        Return outArr
    End Function

    ''' <summary>
    ''' Returns a color (black or white) that contrasts with the given background color.
    ''' </summary>
    Public Function GetOppositeColor(backgroundColor As Color) As Color
        Dim sumColor = CInt(backgroundColor.R) + CInt(backgroundColor.G) + CInt(backgroundColor.B)
        If sumColor < 333 Then
            Return Color.White
        Else
            Return Color.Black
        End If
    End Function

    ''' <summary>
    ''' Copies the given text to the clipboard and optionally updates a status label.
    ''' </summary>
    Public Sub CopyTextToClipboard(text As String, Optional statusLabel As Label = Nothing, Optional statusMessage As String = "")
        If Not String.IsNullOrEmpty(text) Then
            Clipboard.SetText(text)
            If statusLabel IsNot Nothing AndAlso Not String.IsNullOrEmpty(statusMessage) Then
                statusLabel.Text = statusMessage
            End If
        End If
    End Sub

    ''' <summary>
    ''' Explicitly detaches a task while still observing faults for debugging.
    ''' </summary>
    Public Sub Forget(task As Task)
        If task Is Nothing Then Return

        task.ContinueWith(
            Sub(t)
                Try
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " async: " & t.Exception.GetBaseException().Message)
                Catch
                End Try
            End Sub,
            TaskContinuationOptions.OnlyOnFaulted)
    End Sub

    ''' <summary>
    ''' Reads image pixel dimensions straight from the file header (JPEG/PNG/GIF/BMP/WEBP)
    ''' without decoding via GDI+. This is deliberately NOT Image.FromFile/FromStream:
    ''' the background worker calls this while the UI thread may be running GetPixel on
    ''' the same image, and concurrent GDI+ decoding corrupts shared state (throwing
    ''' OverflowException / "Parameter is not valid"). Header parsing touches no GDI+.
    ''' Returns Size.Empty when the format/size can't be determined.
    ''' </summary>
    Public Function GetImageDimensions(filePath As String) As Size
        Try
            Using fs As New IO.FileStream(filePath, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite)
                Dim head(31) As Byte
                Dim n As Integer = fs.Read(head, 0, head.Length)

                ' PNG: 8-byte signature, then IHDR with big-endian width/height at 16/20.
                If n >= 24 AndAlso head(0) = &H89 AndAlso head(1) = &H50 AndAlso head(2) = &H4E AndAlso head(3) = &H47 Then
                    Dim w As Integer = (CInt(head(16)) << 24) Or (CInt(head(17)) << 16) Or (CInt(head(18)) << 8) Or CInt(head(19))
                    Dim h As Integer = (CInt(head(20)) << 24) Or (CInt(head(21)) << 16) Or (CInt(head(22)) << 8) Or CInt(head(23))
                    Return New Size(w, h)
                End If

                ' GIF: "GIF", then little-endian logical screen width/height at 6/8.
                If n >= 10 AndAlso head(0) = &H47 AndAlso head(1) = &H49 AndAlso head(2) = &H46 Then
                    Return New Size(CInt(head(6)) Or (CInt(head(7)) << 8), CInt(head(8)) Or (CInt(head(9)) << 8))
                End If

                ' BMP: "BM", then 4-byte little-endian width/height at 18/22.
                If n >= 26 AndAlso head(0) = &H42 AndAlso head(1) = &H4D Then
                    Return New Size(BitConverter.ToInt32(head, 18), Math.Abs(BitConverter.ToInt32(head, 22)))
                End If

                ' JPEG: walk the marker segments to the SOF frame header.
                If n >= 2 AndAlso head(0) = &HFF AndAlso head(1) = &HD8 Then
                    fs.Seek(2, IO.SeekOrigin.Begin)
                    Return ReadJpegSize(fs)
                End If

                ' WEBP (and AVIF/HEIC/HEIF on modern): let the platform decoder read
                ' the dimensions so we stay off GDI+.
                If ImageDecoderProvider.Decoder_Backed_Extensions.Contains(IO.Path.GetExtension(filePath)) Then
                    fs.Seek(0, IO.SeekOrigin.Begin)
                    Return ImageDecoderProvider.Current.TryGetPixelSize(fs)
                End If
            End Using
        Catch
        End Try
        Return Size.Empty
    End Function

    Private Function ReadJpegSize(fs As IO.Stream) As Size
        Do
            Dim b As Integer = fs.ReadByte()
            If b < 0 Then Exit Do
            If b <> &HFF Then Continue Do

            ' Skip any fill 0xFF bytes; next non-FF byte is the marker.
            Dim marker As Integer = fs.ReadByte()
            Do While marker = &HFF
                marker = fs.ReadByte()
            Loop
            If marker < 0 Then Exit Do

            ' Standalone markers (no length payload): RSTn (D0-D7), SOI/EOI (D8/D9), TEM (01).
            If (marker >= &HD0 AndAlso marker <= &HD9) OrElse marker = &H1 Then Continue Do

            Dim len1 As Integer = fs.ReadByte()
            Dim len2 As Integer = fs.ReadByte()
            If len1 < 0 OrElse len2 < 0 Then Exit Do
            Dim segLen As Integer = (len1 << 8) Or len2

            ' SOF0..SOF15 carry the frame size, except the DHT/JPG/DAC markers (C4/C8/CC).
            Dim isSof As Boolean = marker >= &HC0 AndAlso marker <= &HCF AndAlso marker <> &HC4 AndAlso marker <> &HC8 AndAlso marker <> &HCC
            If isSof Then
                fs.ReadByte() ' sample precision
                Dim h1 As Integer = fs.ReadByte() : Dim h2 As Integer = fs.ReadByte()
                Dim w1 As Integer = fs.ReadByte() : Dim w2 As Integer = fs.ReadByte()
                If w2 < 0 Then Exit Do
                Return New Size((w1 << 8) Or w2, (h1 << 8) Or h2)
            End If

            If segLen < 2 Then Exit Do
            fs.Seek(segLen - 2, IO.SeekOrigin.Current)
        Loop
        Return Size.Empty
    End Function

End Module
