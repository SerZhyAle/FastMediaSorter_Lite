#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.IO
Imports System.Text

''' <summary>
''' Debug view of the overlay (SPECIFICATION_OCR_OVERLAY_ACCURACY.md, stage S5): what the
''' recognizer said the box was, and what the renderer actually painted. Without it the one
''' question that matters - "does the plate sit on its own text?" - had nothing to answer it,
''' because staying put through zoom and resize proves only that the geometry is CONSISTENT.
''' A systematic offset is perfectly consistent; the other project measured plates 1.9x wider
''' than their text with zero drift between every state they checked.
'''
''' Switched on by environment variable rather than by a setting, deliberately: a developer
''' switch does not belong in a window that would then owe it thirteen translations.
'''   FMS_OCR_DIAG=1              draw the boxes over the image
'''   FMS_OCR_DIAG_FILE=&lt;path&gt;    append one JSON line per image
''' Both are read once per process, so a change takes effect on the next start.
'''
''' Every value reported here comes from the SAME functions the renderer used - the plate
''' rectangle and the font size are what it recorded while painting, not a second
''' calculation - so turning diagnostics on cannot change what it describes. Nothing is
''' written next to the user's images: the dump goes only where the user pointed it.
'''
''' Modern-only: it is a development tool, not something a Windows 7 fallback needs.
''' </summary>
Public Module OcrDiagnostics

    Private ReadOnly diag_Enabled As Boolean = ReadFlag("FMS_OCR_DIAG")
    Private ReadOnly diag_File As String = ReadPath("FMS_OCR_DIAG_FILE")

    ''' <summary>Guard so an image is dumped once rather than on every repaint - the overlay
    ''' repaints on each zoom step, resize and expose. Read and written from two threads now
    ''' (the painter, and the pipeline for a page that produced no plates at all), hence the
    ''' lock below rather than a bare field.</summary>
    Private last_Dumped As OcrOverlayDocument

    Private ReadOnly dump_Sync As New Object()

    Public ReadOnly Property Enabled As Boolean
        Get
            Return diag_Enabled
        End Get
    End Property

    Public ReadOnly Property DumpEnabled As Boolean
        Get
            Return diag_File.Length > 0
        End Get
    End Property

    Private Function ReadFlag(name As String) As Boolean
        Try
            Dim raw As String = If(Environment.GetEnvironmentVariable(name), "").Trim()
            Return raw = "1" OrElse String.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

    Private Function ReadPath(name As String) As String
        Try
            Return If(Environment.GetEnvironmentVariable(name), "").Trim()
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Outlines one block: the recognizer's box and the plate that was painted for it, in two
    ''' colours, with the block index. Both rectangles are in picture-box client coordinates -
    ''' the space the plate was drawn in - so an offset between them is visible directly
    ''' rather than inferred.
    ''' </summary>
    Public Sub DrawBlockBoxes(g As Graphics, index As Integer, ocrBox As Rectangle, plate As Rectangle)
        If Not diag_Enabled OrElse g Is Nothing Then Return
        Try
            Using ocrPen As New Pen(Color.Magenta, 1.0F)
                ocrPen.DashStyle = DashStyle.Dash
                g.DrawRectangle(ocrPen, ocrBox)
            End Using
            Using platePen As New Pen(Color.Lime, 1.0F)
                g.DrawRectangle(platePen, plate)
            End Using
            Using label As New Font("Segoe UI", 10.0F, FontStyle.Bold, GraphicsUnit.Pixel)
                Using brush As New SolidBrush(Color.Magenta)
                    g.DrawString(index.ToString(CultureInfo.InvariantCulture), label, brush, ocrBox.X + 1, ocrBox.Y + 1)
                End Using
            End Using
        Catch
            ' A debug overlay must never be able to take the image down with it.
        End Try
    End Sub

    ''' <summary>
    ''' Appends one JSON line describing the whole image. Shape follows the exchange
    ''' specification section 10.4 so the two projects' dumps stay comparable, plus the three
    ''' fields only we have: the plate as it ended up after the growth rung, whether the
    ''' translation still had to be trimmed, and what the thresholds refused.
    '''
    ''' Called for a page with NO plates too, and that is the point rather than an edge case
    ''' (section 16.1): a scene where a correctly read word was thrown away by a threshold used
    ''' to look exactly like a scene where the engine found nothing, because neither wrote a
    ''' line. The record only reports; it renders nothing and changes no decision.
    ''' </summary>
    Public Sub DumpDocument(doc As OcrOverlayDocument)
        If doc Is Nothing OrElse diag_File.Length = 0 Then Return
        SyncLock dump_Sync
            If ReferenceEquals(doc, last_Dumped) Then Return
            last_Dumped = doc
        End SyncLock

        Try
            Dim sb As New StringBuilder()
            sb.Append("{""file"":").Append(Quote(doc.FilePath))
            sb.Append(",""width"":").Append(Num(doc.ImageSize.Width))
            sb.Append(",""height"":").Append(Num(doc.ImageSize.Height))
            sb.Append(",""blocks"":[")
            For i As Integer = 0 To doc.Blocks.Count - 1
                Dim b As OcrBlock = doc.Blocks(i)
                If i > 0 Then sb.Append(",")
                Dim plateBottom As Integer = b.Box.Top + If(b.RenderPlateHeight > 0, b.RenderPlateHeight, b.Box.Height)
                sb.Append("{""text"":").Append(Quote(If(Not String.IsNullOrWhiteSpace(b.TranslatedText), b.TranslatedText, b.SourceText)))
                sb.Append(",""x0"":").Append(Num(b.Box.Left))
                sb.Append(",""y0"":").Append(Num(b.Box.Top))
                sb.Append(",""x1"":").Append(Num(b.Box.Right))
                sb.Append(",""y1"":").Append(Num(b.Box.Bottom))
                sb.Append(",""lineH"":").Append(Num(OcrOverlayFit.MedianLineHeight(b)))
                sb.Append(",""fontPx"":").Append(b.RenderFontPx.ToString("0.##", CultureInfo.InvariantCulture))
                sb.Append(",""plateY1"":").Append(Num(plateBottom))
                sb.Append(",""truncated"":").Append(If(b.Truncated, "true", "false"))
                sb.Append(",""background"":").Append(Rgb(b.PlateBackgroundArgb))
                sb.Append(",""ink"":").Append(Rgb(b.PlateInkArgb))
                sb.Append("}")
            Next
            sb.Append("]")

            ' "dropped": an ARRAY when the run measured its refusals - empty means it measured
            ' and dropped nothing - and null when nobody did, which is what a document read
            ' back from the cache honestly is. The two must stay distinguishable: reporting
            ' "not measured" as "nothing was dropped" is the same flattering zero that made the
            ' hidden-ink gate green while it was excluding every scene that found no text
            ' (section 16.5).
            sb.Append(",""dropped"":")
            If doc.Dropped Is Nothing Then
                sb.Append("null")
            Else
                sb.Append("[")
                For i As Integer = 0 To doc.Dropped.Count - 1
                    Dim d As OcrDroppedLine = doc.Dropped(i)
                    If i > 0 Then sb.Append(",")
                    sb.Append("{""text"":").Append(Quote(d.Text))
                    sb.Append(",""x0"":").Append(Num(d.Box.Left))
                    sb.Append(",""y0"":").Append(Num(d.Box.Top))
                    sb.Append(",""x1"":").Append(Num(d.Box.Right))
                    sb.Append(",""y1"":").Append(Num(d.Box.Bottom))
                    ' Negative confidence means the refusal happened after the engine, where
                    ' there is none - null rather than a made-up number.
                    sb.Append(",""conf"":").Append(If(d.Confidence < 0.0F, "null",
                                                      d.Confidence.ToString("0.###", CultureInfo.InvariantCulture)))
                    sb.Append(",""rule"":").Append(Quote(d.Rule))
                    sb.Append("}")
                Next
                sb.Append("]")
            End If
            sb.Append("}")

            ' UTF8Encoding(False), not Encoding.UTF8: the latter puts a byte-order mark at the
            ' head of the file, and a BOM on line 1 of a JSONL dump makes the first record fail
            ' to parse in most languages - on a file whose whole purpose is to be read by other
            ' tools and compared with the other project's dumps.
            '
            ' Under the same lock as the guard above: the painter and the pipeline both append
            ' now, and two appends racing on one handle lose a line to a sharing violation -
            ' silently, since the catch below only writes to the debug output.
            SyncLock dump_Sync
                File.AppendAllText(diag_File, sb.ToString() & Environment.NewLine, New UTF8Encoding(False))
            End SyncLock
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr-diag: dump failed: " & ex.Message)
        End Try
    End Sub

    Private Function Num(value As Integer) As String
        Return value.ToString(CultureInfo.InvariantCulture)
    End Function

    ''' <summary>Sampled colour, or JSON null when it was never computed - 0 means "not
    ''' computed" and must not be reported as black.</summary>
    Private Function Rgb(argb As Integer) As String
        If argb = 0 Then Return "null"
        Dim c As Color = Color.FromArgb(argb)
        Return """rgb(" & Num(c.R) & "," & Num(c.G) & "," & Num(c.B) & ")"""
    End Function

    Private Function Quote(text As String) As String
        Dim sb As New StringBuilder("""")
        For Each ch As Char In If(text, "")
            Select Case ch
                Case """"c : sb.Append("\""")
                Case "\"c : sb.Append("\\")
                Case ControlChars.Lf : sb.Append("\n")
                Case ControlChars.Cr : sb.Append("\r")
                Case ControlChars.Tab : sb.Append("\t")
                Case Else
                    If AscW(ch) < 32 Then
                        sb.Append("\u").Append(AscW(ch).ToString("x4", CultureInfo.InvariantCulture))
                    Else
                        sb.Append(ch)
                    End If
            End Select
        Next
        Return sb.Append("""").ToString()
    End Function

End Module
#End If
