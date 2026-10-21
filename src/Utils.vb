Imports System.Collections.Generic
Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

Module Utils

    ''' <summary>
    ''' Trims a recent-items list to <paramref name="limit"/> entries, dropping the oldest.
    ''' <paramref name="newestFirst"/> says which end is old: the folder list keeps the
    ''' newest at index 0, the file list at the end. A limit of 0 (or less) empties the
    ''' list - "0 disables saving new entries" is a promise the folder list used to break
    ''' by removing one element per insert and never trimming at load (053 F-12).
    ''' </summary>
    Public Sub ApplyRecentLimit(list As List(Of String), limit As Integer, newestFirst As Boolean)
        If list Is Nothing Then Return
        Dim keep As Integer = Math.Max(0, limit)
        If list.Count <= keep Then Return
        If newestFirst Then
            list.RemoveRange(keep, list.Count - keep)
        Else
            list.RemoveRange(0, list.Count - keep)
        End If
    End Sub

    ''' <summary>
    ''' A number read from the settings, or <paramref name="defaultValue"/> when the text
    ''' is not one. <c>Integer.TryParse(text, field)</c> zeroes its out argument on failure,
    ''' so a corrupt value used to become 0 (or the minimum after a clamp) - a 1-second
    ''' slideshow instead of 10, a switched-off cache, a credential kept until exit
    ''' (053 F-17) - instead of the documented default.
    ''' </summary>
    Public Function ParseIntSetting(text As String, defaultValue As Integer) As Integer
        Dim parsed As Integer
        If Integer.TryParse(text, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, parsed) Then Return parsed
        Return defaultValue
    End Function

    ''' <summary>The same, held to <paramref name="minimum"/>..<paramref name="maximum"/>:
    ''' a parsed value out of range is clamped, an unparseable one is the default.</summary>
    Public Function ParseIntSetting(text As String, defaultValue As Integer, minimum As Integer, maximum As Integer) As Integer
        Dim value As Integer = ParseIntSetting(text, defaultValue)
        If value < minimum Then Return minimum
        If value > maximum Then Return maximum
        Return value
    End Function

    ''' <summary>
    ''' The slideshow interval after one press of a slideshow key (MEDIA-VIEWER 1.0): the first
    ''' press starts at <paramref name="baseMs"/>, every further press while it runs halves the
    ''' CURRENT interval, and no press ever goes below <paramref name="floorMs"/> (1 s in the
    ''' viewer). Both slideshow entry points (sequential and random) call this one rule.
    ''' </summary>
    Public Function SlideshowIntervalAfterPress(baseMs As Integer, currentMs As Integer, running As Boolean, floorMs As Integer) As Integer
        If Not running Then Return baseMs
        Dim halved As Integer = CInt(currentMs / 2)
        Return If(halved < floorMs, floorMs, halved)
    End Function

    ''' <summary>The number of background colour schemes the settings window offers
    ''' (dynamic, black, white, most frequent and two more).</summary>
    Public Const Color_Scheme_Count As Integer = 6

    ''' <summary>
    ''' The stored colour scheme, or 1 (black, the shipped default) for anything that is
    ''' not one of the <see cref="Color_Scheme_Count"/> schemes. Unchecked, a value of 6 or
    ''' more (a hand edit, corruption, a newer build with more schemes) threw from
    ''' SelectedIndex inside Form1_Load: a crash on every start (053 F-16) - the same class
    ''' as the SortDir fix.
    ''' </summary>
    Public Function ColorSchemeFromSetting(text As String) As Integer
        Dim value As Integer = ParseIntSetting(text, 1)
        If value < 0 OrElse value >= Color_Scheme_Count Then Return 1
        Return value
    End Function

    ''' <summary>A recent list in display order, newest first, whichever way it is stored.</summary>
    Public Function RecentNewestFirst(list As List(Of String), storedNewestFirst As Boolean) As List(Of String)
        Dim copy As New List(Of String)(If(list, New List(Of String)()))
        If Not storedNewestFirst Then copy.Reverse()
        Return copy
    End Function

    ''' <summary>
    ''' One argument quoted for a command line by the CommandLineToArgvW rules, so the
    ''' receiving process reads back exactly <paramref name="argument"/>. Plain
    ''' <c>"""" &amp; path &amp; """"</c> is wrong for any path ending in a backslash: for a
    ''' drive root <c>"E:\"</c> the <c>\"</c> reads as a literal quote and the receiver got
    ''' <c>E:"</c> (053 F-13). Backslashes are literal except in front of a quote, where
    ''' each must be doubled - including the ones in front of the closing quote.
    ''' </summary>
    Public Function QuoteArgument(argument As String) As String
        Dim text As String = If(argument, "")
        Dim sb As New System.Text.StringBuilder(text.Length + 2)
        sb.Append(""""c)
        Dim backslashes As Integer = 0
        For Each c As Char In text
            If c = "\"c Then
                backslashes += 1
                Continue For
            End If
            If c = """"c Then
                sb.Append("\"c, backslashes * 2 + 1)
            ElseIf backslashes > 0 Then
                sb.Append("\"c, backslashes)
            End If
            backslashes = 0
            sb.Append(c)
        Next
        sb.Append("\"c, backslashes * 2)
        sb.Append(""""c)
        Return sb.ToString()
    End Function

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
#If NETFRAMEWORK Then
        Dim sumColor = CInt(backgroundColor.R) + CInt(backgroundColor.G) + CInt(backgroundColor.B)
        If sumColor < 333 Then
            Return Color.White
        Else
            Return Color.Black
        End If
#Else
        ' Whichever of black and white contrasts more, by relative luminance (ICON-RENDER
        ' rule 3, spec 027). The channel sum it replaces gave white on a saturated green
        ' such as (0,255,77) - about 1.4 : 1. The better of the two is never under 4.5 : 1.
        Dim lum As Double = RelativeLuminance(backgroundColor)
        Dim againstWhite As Double = 1.05 / (lum + 0.05)
        Dim againstBlack As Double = (lum + 0.05) / 0.05
        Return If(againstWhite >= againstBlack, Color.White, Color.Black)
#End If
    End Function

    ''' <summary>WCAG 2 relative luminance of an sRGB colour, 0 (black) .. 1 (white).</summary>
    Public Function RelativeLuminance(c As Color) As Double
        Return 0.2126 * LinearChannel(c.R) + 0.7152 * LinearChannel(c.G) + 0.0722 * LinearChannel(c.B)
    End Function

    ''' <summary>WCAG 2 contrast ratio of two colours, 1 .. 21.</summary>
    Public Function ContrastRatio(a As Color, b As Color) As Double
        Dim la As Double = RelativeLuminance(a)
        Dim lb As Double = RelativeLuminance(b)
        Return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05)
    End Function

    Private Function LinearChannel(value As Byte) As Double
        Dim s As Double = value / 255.0
        Return If(s <= 0.03928, s / 12.92, Math.Pow((s + 0.055) / 1.055, 2.4))
    End Function

    ''' <summary>
    ''' Copies the given text to the clipboard and optionally updates a status label.
    ''' Another process holding the clipboard is a real, common Windows failure (CLIPBOARD-GUARD
    ''' rule 2, ticket 041): a bounded retry, then the label says so - never an exception into
    ''' the UI. The names and paths copied here are not secrets, so no privacy flags.
    ''' </summary>
    Public Sub CopyTextToClipboard(text As String, Optional statusLabel As Label = Nothing, Optional statusMessage As String = "")
        If String.IsNullOrEmpty(text) Then Return
        Dim message As String = statusMessage
        Try
            ' Explicit 3 x 120 ms; the 2-argument overload retries ten times, 100 ms apart.
            Clipboard.SetDataObject(New DataObject(DataFormats.UnicodeText, text), True, 3, 120)
        Catch ex As Exception
            AppFileLogger.LogException("Clipboard copy failed", ex)
            message = Localization.T("Не удалось скопировать в буфер обмена")
        End Try
        If statusLabel IsNot Nothing AndAlso Not String.IsNullOrEmpty(message) Then
            statusLabel.Text = message
        End If
    End Sub

    ''' <summary>
    ''' Explicitly detaches a task while still observing faults.
    '''
    ''' The fault goes to the file log (audit 057.S01-1): Debug.WriteLine is compiled away in
    ''' Release together with its argument, so the continuation used to do nothing there - a
    ''' faulted OCR pipeline or thumbnail batch surfaced only if the finalizer ever reported it
    ''' as unobserved, and in a Debug build it never reached the file at all.
    ''' </summary>
    Public Sub Forget(task As Task)
        If task Is Nothing Then Return

        task.ContinueWith(
            Sub(t)
                Try
                    AppFileLogger.LogException("Detached task failed", t.Exception.GetBaseException())
                Catch
                    ' The log itself is the last resort; nothing further to tell.
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
        Return GetImageDimensions(filePath, Is_Exif_AutoRotate)
    End Function

    ''' <param name="applyOrientation">Report the size as DISPLAYED (audit 048, A-21): with
    ''' EXIF auto-rotate on, a portrait phone photo stored as landscape pixels plus an
    ''' Orientation tag of 5..8 is shown upright, so its label must say 3024x4032, not the
    ''' stored 4032x3024. Only JPEG (APP1 Exif, read in the same header walk) and, on the
    ''' mainline, HEIC/AVIF/JXL (through the decoder seam) carry it.</param>
    Public Function GetImageDimensions(filePath As String, applyOrientation As Boolean) As Size
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
                    Dim orientation As Integer = 1
                    Dim jpeg_Size As Size = ReadJpegSize(fs, orientation)
                    If applyOrientation AndAlso IsQuarterTurnOrientation(orientation) AndAlso Not jpeg_Size.IsEmpty Then
                        Return New Size(jpeg_Size.Height, jpeg_Size.Width)
                    End If
                    Return jpeg_Size
                End If

                ' WEBP (and AVIF/HEIC/HEIF/JXL on modern): let the platform decoder read
                ' the dimensions so we stay off GDI+. The seam reports the displayed size
                ' itself (the mainline's Magick path reads the orientation it auto-orients by).
                ' By extension OR by header (audit 057.S01-2): a WEBP saved under a .jpg name
                ' is displayed through the same seam since audit 048 A-16, and its label read
                ' "?x?" because only the name was asked here.
                If ImageDecoderProvider.Decoder_Backed_Extensions.Contains(IO.Path.GetExtension(filePath)) OrElse
                   (n >= 12 AndAlso ImageDecoderProvider.LooksDecoderBacked(head)) Then
                    fs.Seek(0, IO.SeekOrigin.Begin)
                    Return ImageDecoderProvider.Current.TryGetPixelSize(fs)
                End If
            End Using
        Catch
        End Try
        Return Size.Empty
    End Function

    ''' <summary>EXIF Orientation 5..8 means the stored pixels are a quarter turn off the
    ''' displayed picture: width and height swap. Pure.</summary>
    Public Function IsQuarterTurnOrientation(orientation As Integer) As Boolean
        Return orientation >= 5 AndAlso orientation <= 8
    End Function

    ''' <summary>
    ''' The Orientation tag (0x0112) of an APP1 Exif segment's IFD0, or 1 when there is none.
    ''' <paramref name="segment"/> is the segment's payload, starting at "Exif\0\0". Never
    ''' throws: a malformed segment answers 1 ("as stored"), which is what the label showed
    ''' before this existed.
    ''' </summary>
    Public Function ReadExifOrientation(segment As Byte()) As Integer
        Try
            If segment Is Nothing OrElse segment.Length < 14 Then Return 1
            If segment(0) <> &H45 OrElse segment(1) <> &H78 OrElse segment(2) <> &H69 OrElse segment(3) <> &H66 OrElse
               segment(4) <> 0 OrElse segment(5) <> 0 Then Return 1
            Const tiff As Integer = 6
            Dim little As Boolean
            If segment(tiff) = &H49 AndAlso segment(tiff + 1) = &H49 Then
                little = True
            ElseIf segment(tiff) = &H4D AndAlso segment(tiff + 1) = &H4D Then
                little = False
            Else
                Return 1
            End If

            Dim read16 As Func(Of Integer, Integer) =
                Function(at As Integer) If(little, CInt(segment(at)) Or (CInt(segment(at + 1)) << 8),
                                              (CInt(segment(at)) << 8) Or CInt(segment(at + 1)))
            Dim read32 As Func(Of Integer, Long) =
                Function(at As Integer) If(little,
                    CLng(segment(at)) Or (CLng(segment(at + 1)) << 8) Or (CLng(segment(at + 2)) << 16) Or (CLng(segment(at + 3)) << 24),
                    (CLng(segment(at)) << 24) Or (CLng(segment(at + 1)) << 16) Or (CLng(segment(at + 2)) << 8) Or CLng(segment(at + 3)))

            Dim ifd_Offset As Long = read32(tiff + 4)
            Dim ifd As Long = tiff + ifd_Offset
            If ifd < tiff + 8 OrElse ifd + 2 > segment.Length Then Return 1
            Dim entries As Integer = read16(CInt(ifd))
            For i As Integer = 0 To entries - 1
                Dim entry As Long = ifd + 2 + 12L * i
                If entry + 12 > segment.Length Then Exit For
                If read16(CInt(entry)) = &H112 Then
                    ' SHORT, count 1: the value sits in the first two bytes of the value field.
                    Dim value As Integer = read16(CInt(entry) + 8)
                    Return If(value >= 1 AndAlso value <= 8, value, 1)
                End If
            Next
        Catch
        End Try
        Return 1
    End Function

    Private Function ReadJpegSize(fs As IO.Stream, ByRef orientation As Integer) As Size
        orientation = 1
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

            ' APP1 comes before the frame header: read its Exif Orientation on the way past
            ' (audit 048, A-21). Only the first Exif APP1 counts, as in every reader.
            If marker = &HE1 AndAlso orientation = 1 AndAlso segLen > 2 Then
                Dim payload(segLen - 3) As Byte
                Dim got As Integer = 0
                While got < payload.Length
                    Dim r As Integer = fs.Read(payload, got, payload.Length - got)
                    If r <= 0 Then Exit While
                    got += r
                End While
                If got < payload.Length Then Exit Do
                orientation = ReadExifOrientation(payload)
                Continue Do
            End If

            fs.Seek(segLen - 2, IO.SeekOrigin.Current)
        Loop
        Return Size.Empty
    End Function

End Module
