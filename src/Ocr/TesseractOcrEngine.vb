Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Net
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports Tesseract

''' <summary>
''' Resolves the on-disk locations the OCR feature uses. The native Tesseract
''' DLLs (x86/x64) ship next to the executable via the NuGet .targets; the
''' language data and cache live under %LOCALAPPDATA% so a low-privilege install
''' (Program Files) never has to be writable.
''' </summary>
Friend Module OcrPaths

    Public Function ExeDir() As String
        ' AppContext.BaseDirectory: same as the exe dir on net48 and still correct
        ' in the .NET 10 single-file publish (Assembly.Location is empty there).
        Return AppContext.BaseDirectory
    End Function

    ''' <summary>%LOCALAPPDATA%\SZA\FastMediaSorter. The location itself is not an OCR
    ''' fact - the archive cache writes under the same root - so it lives in AppPaths and
    ''' this stays as the name the OCR code has always called it by.</summary>
    Public Function AppDataRoot() As String
        Return AppPaths.LocalAppDataRoot()
    End Function

    ''' <summary>Writable tessdata directory (created on demand).</summary>
    Public Function UserTessDataDir() As String
        Return Path.Combine(AppDataRoot(), "tessdata")
    End Function

    ''' <summary>Optional writable high-quality tessdata directory.</summary>
    Public Function UserBestTessDataDir() As String
        Return Path.Combine(AppDataRoot(), "tessdata-best")
    End Function

    ''' <summary>tessdata folder bundled next to the exe, if the build shipped one.</summary>
    Public Function BundledTessDataDir() As String
        Return Path.Combine(ExeDir(), "tessdata")
    End Function

    ''' <summary>Optional bundled high-quality tessdata directory.</summary>
    Public Function BundledBestTessDataDir() As String
        Return Path.Combine(ExeDir(), "tessdata-best")
    End Function

    Public Function OcrCacheDir() As String
        Return Path.Combine(AppDataRoot(), "ocr-cache")
    End Function

End Module

''' <summary>
''' Primary OCR engine: Tesseract 5 for .NET Framework 4.8.
''' Reads from in-memory BMP bytes (via Pix.LoadFromMemory) so we never depend
''' on Tesseract.Drawing / System.Drawing.Common. Missing native runtime or
''' missing language data degrade to <see cref="OcrStatus.RuntimeMissing"/>
''' instead of throwing.
'''
''' Constant status (`OCR-OVERLAY` rule 13). The sizing constants moved to OcrImageScale with
''' their table (audit 051 E-05). The one left here is about the patience of a download - it
''' does not decide what is drawn, which is why it carries no two-sided bracket. Ticket 033
''' owns measuring it.
''' | Constant | Status | Source |
''' | --- | --- | --- |
''' | DownloadTimeoutMs | chosen here, NOT derived | patience, not accuracy: a minute per language pack. It changes nothing about what is recognized. |
''' </summary>
Public Class TesseractOcrEngine
    Implements IOcrEngine

    Private Const DownloadTimeoutMs As Integer = 60000
    Private Const TessDataBaseUrl As String = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/"
    Private Const TessDataBestBaseUrl As String = "https://github.com/tesseract-ocr/tessdata_best/raw/main/"

    Private ReadOnly sync As New Object()
    Private cachedEngine As TesseractEngine
    Private cachedKey As String = ""
    ''' <summary>Set at shutdown: GetEngine refuses to build a new engine, so a job that got
    ''' past its cancellation check just before the close cannot re-create one (E-15).</summary>
    Private retired As Boolean

    Private Enum OcrPreprocessMode
        Normal
        Grayscale
        Threshold
        ThresholdInverted
    End Enum

    Private NotInheritable Class PreparedOcrImage
        Public Property Bytes As Byte()
        Public Property InvScale As Double
    End Class

    Private NotInheritable Class OcrAttemptProfile
        Public Property Language As String = ""
        Public Property Segmentation As PageSegMode
        Public Property Preprocess As OcrPreprocessMode
    End Class

    Private NotInheritable Class OcrAttemptScore
        Public Property Result As OcrResult
        Public Property Score As Double
        Public Property IsStrong As Boolean
        Public Property Profile As OcrAttemptProfile
    End Class

    Public ReadOnly Property Name As String Implements IOcrEngine.Name
        Get
            Return "tesseract"
        End Get
    End Property

    ''' <summary>Use the high-accuracy "best" language models (downloaded on demand)
    ''' instead of the fast ones. Set from settings via ApplyEngineOptions.</summary>
    Public Property PreferBest As Boolean = False

    ''' <summary>Force a single page-segmentation mode ("block"/"sparse"/"line"/
    ''' "vertical"); empty or "auto" keeps the multi-mode auto strategy.</summary>
    Public Property ForcedPageMode As String = ""

    Public Function Recognize(source As Bitmap, languages As String, ct As CancellationToken) As OcrResult Implements IOcrEngine.Recognize
        If source Is Nothing Then Return OcrResult.FromError("no image")
        If ct.IsCancellationRequested Then Return OcrResult.FromError("cancelled")

        Dim runtimeReason As String = ""
        If Not OptionalRuntimeManager.TryPrepareOcrRuntime(runtimeReason) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: runtime unavailable: " & runtimeReason)
            Return OcrResult.Runtime(runtimeReason)
        End If

        Dim languageAttempts As List(Of String) = BuildLanguageAttempts(languages)
        Dim includeInverted As Boolean = ShouldTryInverted(source)
        Dim forcedPsm As PageSegMode
        Dim profiles As List(Of OcrAttemptProfile)
        If TryMapForcedMode(ForcedPageMode, forcedPsm) Then
            profiles = BuildForcedProfiles(languageAttempts, forcedPsm, includeInverted)
        Else
            profiles = BuildAttemptProfiles(languageAttempts, includeInverted)
        End If
        ' One resample of the source, made on first need and shared by every preprocess
        ' mode; each mode's buffer is then built the first time a profile asks for it. They
        ' used to be built all at once, each with its own full-resolution resample, before
        ' the first attempt - which usually wins on its own (audit 051 E-08).
        Dim scale As Double = OcrImageScale.ComputeScale(source.Width, source.Height)
        Dim invScale As Double = 1.0 / scale
        Dim flat As Bitmap = Nothing
        Dim preparedImages As New LazyByKey(Of OcrPreprocessMode, PreparedOcrImage)(
            Function(mode As OcrPreprocessMode)
                If flat Is Nothing Then flat = RenderScaledBitmap(source, scale)
                Return New PreparedOcrImage With {.Bytes = EncodeOcrBuffer(flat, mode), .InvScale = invScale}
            End Function)

        Try
            Return RecognizeAttempts(source, profiles, preparedImages, ct)
        Finally
            If flat IsNot Nothing Then flat.Dispose()
        End Try
    End Function

    ''' <summary>The message of a Failed result whose picture could not be prepared for the
    ''' engine (usually memory on the 32-bit viewer); the pipeline names it.</summary>
    Public Const ImagePreparationFailed As String = "image preparation failed"

    Private Function RecognizeAttempts(source As Bitmap, profiles As List(Of OcrAttemptProfile),
                                       preparedImages As LazyByKey(Of OcrPreprocessMode, PreparedOcrImage),
                                       ct As CancellationToken) As OcrResult
        Dim best As OcrAttemptScore = Nothing
        Dim lastFailure As OcrResult = Nothing
        Dim languagesMissing As Boolean = False

        For Each profile As OcrAttemptProfile In profiles
            ' Once per attempt, and again before the (possibly 60 s) language download below:
            ' those are the only two places long enough to matter, and they are exactly where
            ' a stale job used to keep going after the user had flipped to another image.
            If ct.IsCancellationRequested Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: cancelled before attempt lang=" & profile.Language)
                Return OcrResult.FromError("cancelled")
            End If

            Dim dataDir As String
            Try
                dataDir = EnsureTessData(profile.Language, PreferBest, allowDownload:=RecognitionMayDownload)
            Catch ex As Exception
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: tessdata error: " & ex.Message)
                Return OcrResult.Runtime(LanguageDataMissing)
            End Try

            If dataDir Is Nothing Then
#If Not NETFRAMEWORK Then
                ' Nothing is downloaded from inside recognition (APP-BEHAVIOUR rule 4, ticket
                ' 028 Ф-6): a language with no data is skipped, and the caller - which asks
                ' before recognition starts - hears about it only if no language had data.
                languagesMissing = True
                Continue For
#Else
                ' The same message as the mainline's, so the pipeline names the language and
                ' the download instead of "OCR engine unavailable" (ticket 033 Ф-6, F7 - taken
                ' on x86 as a bug fix, owner decision O-5).
                Return OcrResult.Runtime(LanguageDataMissing)
#End If
            End If

            ' EnsureTessData can have spent up to a minute downloading a pack; the user may
            ' well have moved on in that time.
            If ct.IsCancellationRequested Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: cancelled after tessdata lang=" & profile.Language)
                Return OcrResult.FromError("cancelled")
            End If

            Dim prepared As PreparedOcrImage
            Try
                prepared = preparedImages.Get(profile.Preprocess)
            Catch ex As Exception
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: buffer error: " & ex.Message)
                Return OcrResult.FromError(ImagePreparationFailed)
            End Try

            Dim attemptResult As OcrResult = RecognizeSingleAttempt(source, prepared, dataDir, profile)
            If attemptResult.Status = OcrStatus.RuntimeMissing Then Return attemptResult

            If attemptResult.Status = OcrStatus.Failed Then
                lastFailure = attemptResult
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") &
                                " ocr: attempt failed lang=" & profile.Language &
                                " psm=" & profile.Segmentation.ToString() &
                                " prep=" & profile.Preprocess.ToString() &
                                " msg=" & attemptResult.Message)
                Continue For
            End If

            Dim scored As OcrAttemptScore = ScoreAttempt(attemptResult, profile)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") &
                            " ocr: attempt lang=" & profile.Language &
                            " psm=" & profile.Segmentation.ToString() &
                            " prep=" & profile.Preprocess.ToString() &
                            " lines=" & attemptResult.Lines.Count.ToString() &
                            " score=" & scored.Score.ToString("0.0"))

            If attemptResult.Status = OcrStatus.Ok AndAlso
               (best Is Nothing OrElse scored.Score > best.Score) Then
                best = scored
            End If

            If scored.IsStrong Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") &
                                " ocr: winner lang=" & profile.Language &
                                " psm=" & profile.Segmentation.ToString() &
                                " prep=" & profile.Preprocess.ToString())
                Return attemptResult
            End If
        Next

        If best IsNot Nothing Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") &
                            " ocr: best-effort winner lang=" & best.Profile.Language &
                            " psm=" & best.Profile.Segmentation.ToString() &
                            " prep=" & best.Profile.Preprocess.ToString())
            Return best.Result
        End If

        If lastFailure IsNot Nothing Then Return lastFailure
        If languagesMissing Then Return OcrResult.Runtime(LanguageDataMissing)
        Return New OcrResult With {.Status = OcrStatus.NoText, .Message = "no text after retries"}
    End Function

    ''' <summary>The Message of a RuntimeMissing result that means "no language had data",
    ''' so the pipeline can say that instead of "the OCR engine is unavailable".</summary>
    Public Const LanguageDataMissing As String = "language data missing"

    ''' <summary>Whether recognition may fetch a missing language pack itself. The x86
    ''' fallback keeps the historical implicit download (maintenance policy, dated
    ''' exception); the mainline never downloads from inside recognition - the caller asks
    ''' first (APP-BEHAVIOUR rule 4, ticket 028 Ф-6).</summary>
#If NETFRAMEWORK Then
    Private Const RecognitionMayDownload As Boolean = True
#Else
    Private Const RecognitionMayDownload As Boolean = False
#End If

    Private Function GetEngine(dataDir As String, langs As String) As TesseractEngine
        If Volatile.Read(retired) Then Throw New ObjectDisposedException(NameOf(TesseractOcrEngine))
        Dim key As String = dataDir & "|" & langs
        If cachedEngine IsNot Nothing AndAlso String.Equals(cachedKey, key, StringComparison.Ordinal) Then
            Return cachedEngine
        End If

        If cachedEngine IsNot Nothing Then
            Try
                cachedEngine.Dispose()
            Catch
            End Try
            cachedEngine = Nothing
        End If

        cachedEngine = New TesseractEngine(dataDir, langs, EngineMode.LstmOnly)
        cachedKey = key
        Return cachedEngine
    End Function

    Private Function RecognizeSingleAttempt(source As Bitmap, prepared As PreparedOcrImage, dataDir As String, profile As OcrAttemptProfile) As OcrResult
        Try
            SyncLock sync
                Dim engine As TesseractEngine = GetEngine(dataDir, profile.Language)
                Using pix As Pix = Pix.LoadFromMemory(prepared.Bytes)
                    Using page As Page = engine.Process(pix, profile.Segmentation)
                        Dim dropped As New List(Of OcrDroppedLine)
                        Dim regrouped As Boolean = False
                        Dim lines As List(Of OcrLine) = ExtractLines(page, prepared.InvScale, source.Width, source.Height, dropped, regrouped)
                        Return New OcrResult With {
                            .Status = If(lines.Count > 0, OcrStatus.Ok, OcrStatus.NoText),
                            .Lines = lines,
                            .Dropped = dropped,
                            .LinesRegrouped = regrouped
                        }
                    End Using
                End Using
            End SyncLock
        Catch ex As DllNotFoundException
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: native missing: " & ex.Message)
            Return OcrResult.Runtime("OCR native runtime missing")
        Catch ex As TypeInitializationException
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: type init: " & ex.Message)
            Return OcrResult.Runtime("OCR native runtime missing")
        Catch ex As TesseractException
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: tesseract: " & ex.Message)
            Return OcrResult.FromError("OCR runtime/data problem")
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: error: " & ex.Message)
            If TypeOf ex Is BadImageFormatException OrElse
               ex.Message.IndexOf("leptonica", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               ex.Message.IndexOf("tesseract", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return OcrResult.Runtime("OCR native runtime missing")
            End If
            ' The type only: the message is never shown (the pipeline says "OCR error"), and the
            ' full exception is already in the line logged above.
            Return OcrResult.FromError(ex.GetType().Name)
        End Try
    End Function

    ''' <param name="dropped">Receives every line the threshold refused, with the rule that
    ''' refused it. Filled from the SAME call that decides - see OcrTextFilter.LineRejection -
    ''' and carried on this attempt's result, so the record that reaches the diagnostics dump
    ''' belongs to the pass that actually won rather than to a union over all of them.</param>
    ''' <param name="regrouped">Set when <see cref="OcrLineSplitter"/> cut a stitched line, so
    ''' the returned order is its column order and the clustering must not re-sort.</param>
    Private Shared Function ExtractLines(page As Page, invScale As Double, maxW As Integer, maxH As Integer,
                                         dropped As List(Of OcrDroppedLine),
                                         ByRef regrouped As Boolean) As List(Of OcrLine)
        ' Read the whole page first, floor and all. The split (S7) needs to see the lines the
        ' floor will drop as well as the ones it keeps: an empty full-page "line" reaches no
        ' plate, but it is exactly what must be kept out of the column grouping, and it cannot
        ' be kept out of something it was never in.
        Dim raw As List(Of OcrLine) = ReadPageLines(page, invScale, maxW, maxH)

        Dim split As List(Of OcrLine) = OcrLineSplitter.SplitStitchedLines(raw, regrouped)

        Dim lines As New List(Of OcrLine)
        For Each line As OcrLine In split
            ' A line the engine returned with no text at all was never text, and recording it
            ' as a refusal would inflate "what the threshold threw away" (16.1) with things
            ' nobody lost. It was skipped before S7 and is skipped here; it travels this far
            ' only so the column grouping can see - and exclude - it.
            If line.Text.Length = 0 Then Continue For

            Dim rejection As String = OcrTextFilter.LineRejection(line.Text, line.Confidence)
            If rejection.Length = 0 Then
                lines.Add(line)
            ElseIf dropped IsNot Nothing Then
                dropped.Add(New OcrDroppedLine With {
                    .Text = line.Text,
                    .Box = line.Box,
                    .Confidence = line.Confidence,
                    .Rule = rejection
                })
            End If
        Next

        Return lines
    End Function

    ''' <summary>
    ''' The engine's own output, mapped back to original-image pixels: one entry per text line,
    ''' each carrying the WORDS inside it.
    '''
    ''' The word level is read with <c>Next(TextLine, Word)</c>, which walks the words of the
    ''' current line and stops at its last one - the iterator is then still inside that line,
    ''' so the outer <c>Next(TextLine)</c> advances exactly one line as it always did. Words
    ''' used to be a single stub equal to the whole line; the two rules of section 16.2/16.3
    ''' and the split of section 18 all measure a word, and none of them could be written
    ''' against a stub.
    ''' </summary>
    Private Shared Function ReadPageLines(page As Page, invScale As Double, maxW As Integer, maxH As Integer) As List(Of OcrLine)
        Dim lines As New List(Of OcrLine)

        Using iter As ResultIterator = page.GetIterator()
            iter.Begin()
            Do
                Dim r As Rect = Nothing
                If iter.TryGetBoundingBox(PageIteratorLevel.TextLine, r) Then
                    Dim txt As String = iter.GetText(PageIteratorLevel.TextLine)
                    Dim conf As Single = iter.GetConfidence(PageIteratorLevel.TextLine) / 100.0F
                    Dim box As Rectangle = MapBox(r, invScale, maxW, maxH)
                    Dim cleaned As String = CleanLineText(If(txt, ""))

                    Dim words As New List(Of OcrWord)
                    Do
                        Dim wr As Rect = Nothing
                        If iter.TryGetBoundingBox(PageIteratorLevel.Word, wr) Then
                            Dim wt As String = CleanLineText(If(iter.GetText(PageIteratorLevel.Word), ""))
                            If wt.Length > 0 Then
                                words.Add(New OcrWord With {
                                    .Text = wt,
                                    .Box = MapBox(wr, invScale, maxW, maxH),
                                    .Confidence = iter.GetConfidence(PageIteratorLevel.Word) / 100.0F
                                })
                            End If
                        End If
                    Loop While iter.Next(PageIteratorLevel.TextLine, PageIteratorLevel.Word)

                    ' A line the engine returned with no usable words keeps the stub it has
                    ' always had, so nothing downstream that reads Words meets an empty list
                    ' where it used to meet one entry.
                    If words.Count = 0 AndAlso cleaned.Length > 0 Then
                        words.Add(New OcrWord With {.Text = cleaned, .Box = box, .Confidence = conf})
                    End If

                    lines.Add(New OcrLine With {
                        .Text = cleaned,
                        .Box = box,
                        .Confidence = conf,
                        .Words = words
                    })
                End If
            Loop While iter.Next(PageIteratorLevel.TextLine)
        End Using

        Return lines
    End Function

    Private Shared Function CleanLineText(raw As String) As String
        Dim chars As Char() = raw.Replace(vbCr, " ").Replace(vbLf, " ").ToCharArray()
        Dim filtered As New System.Text.StringBuilder(chars.Length)
        For Each ch As Char In chars
            If Char.IsControl(ch) AndAlso Not Char.IsWhiteSpace(ch) Then Continue For
            filtered.Append(ch)
        Next
        Dim s As String = filtered.ToString().Trim()
        While s.IndexOf("  ", StringComparison.Ordinal) >= 0
            s = s.Replace("  ", " ")
        End While
        Return s
    End Function

    ' The line-acceptance rule that used to live here - it rejects the stray 1-2 character
    ' "lines" Tesseract hallucinates from textured backgrounds (skin, fabric, hair) while
    ' keeping real text, keying on word length rather than raw confidence - moved to
    ' OcrTextFilter.LineRejection unchanged. Not tidying: the record of what the threshold
    ' dropped has to come out of the same call that applies it, and the pure module is also
    ' the only place the tests can reach without the native engine. The two counting helpers
    ' went with it because the attempt scorer below uses the same two measures.

    Private Shared Function MapBox(r As Rect, inv As Double, maxW As Integer, maxH As Integer) As Rectangle
        Dim x As Integer = CInt(Math.Round(r.X1 * inv))
        Dim y As Integer = CInt(Math.Round(r.Y1 * inv))
        Dim w As Integer = CInt(Math.Round(r.Width * inv))
        Dim h As Integer = CInt(Math.Round(r.Height * inv))

        If x < 0 Then x = 0
        If y < 0 Then y = 0
        If w < 1 Then w = 1
        If h < 1 Then h = 1
        If x > maxW - 1 Then x = maxW - 1
        If y > maxH - 1 Then y = maxH - 1
        If x + w > maxW Then w = maxW - x
        If y + h > maxH Then h = maxH - y
        Return New Rectangle(x, y, w, h)
    End Function

    ''' <summary>The source flattened onto white and resampled by <paramref name="scale"/>,
    ''' 24 bpp - made once per recognition and shared by every preprocess mode.</summary>
    Private Shared Function RenderScaledBitmap(source As Bitmap, scale As Double) As Bitmap
        Dim w As Integer = source.Width
        Dim h As Integer = source.Height
        Dim tw As Integer = Math.Max(1, CInt(Math.Round(w * scale)))
        Dim th As Integer = Math.Max(1, CInt(Math.Round(h * scale)))
        Return RenderFlatBitmap(source, w, h, tw, th)
    End Function

    ''' <summary>One preprocess mode applied to a copy of the shared resample, as a 24 bpp BMP
    ''' byte array. Normal needs no copy - it is the resample itself, encoded.</summary>
    Private Shared Function EncodeOcrBuffer(flat As Bitmap, preprocess As OcrPreprocessMode) As Byte()
        If preprocess = OcrPreprocessMode.Normal Then Return EncodeBmp(flat)
        Using copy As Bitmap = flat.Clone(New Rectangle(0, 0, flat.Width, flat.Height), PixelFormat.Format24bppRgb)
            ApplyPreprocess(copy, preprocess)
            Return EncodeBmp(copy)
        End Using
    End Function

    Private Shared Function EncodeBmp(bitmap As Bitmap) As Byte()
        ' Sized up front: a 24 bpp BMP is its header plus padded rows, and a MemoryStream
        ' left to grow doubles its buffer on the way - twice the peak for nothing.
        Dim rowBytes As Long = ((CLng(bitmap.Width) * 3L + 3L) \ 4L) * 4L
        Dim capacity As Long = 54L + rowBytes * bitmap.Height
        Using ms As New MemoryStream(CInt(Math.Min(capacity, Integer.MaxValue)))
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp)
            Return ms.ToArray()
        End Using
    End Function

    Private Shared Function RenderFlatBitmap(source As Bitmap, sourceW As Integer, sourceH As Integer, targetW As Integer, targetH As Integer) As Bitmap
        Dim flat As New Bitmap(targetW, targetH, PixelFormat.Format24bppRgb)
        Using g As Graphics = Graphics.FromImage(flat)
            g.Clear(Color.White)
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            g.DrawImage(source, New Rectangle(0, 0, targetW, targetH), 0, 0, sourceW, sourceH, GraphicsUnit.Pixel)
        End Using
        Return flat
    End Function

    Private Shared Sub ApplyPreprocess(bitmap As Bitmap, preprocess As OcrPreprocessMode)
        Select Case preprocess
            Case OcrPreprocessMode.Normal
                Return
            Case OcrPreprocessMode.Grayscale
                ApplyGrayscale(bitmap)
            Case OcrPreprocessMode.Threshold
                ApplyThreshold(bitmap, False)
            Case OcrPreprocessMode.ThresholdInverted
                ApplyThreshold(bitmap, True)
        End Select
    End Sub

    Private Shared Sub ApplyGrayscale(bitmap As Bitmap)
        Dim rect As New Rectangle(0, 0, bitmap.Width, bitmap.Height)
        Dim data As BitmapData = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb)
        Try
            Dim stride As Integer = Math.Abs(data.Stride)
            Dim byteCount As Integer = stride * bitmap.Height
            Dim bytes(byteCount - 1) As Byte
            Marshal.Copy(data.Scan0, bytes, 0, byteCount)

            For y As Integer = 0 To bitmap.Height - 1
                Dim row As Integer = y * stride
                For x As Integer = 0 To bitmap.Width - 1
                    Dim idx As Integer = row + (x * 3)
                    Dim lum As Byte = Luminance(bytes(idx + 2), bytes(idx + 1), bytes(idx))
                    bytes(idx) = lum
                    bytes(idx + 1) = lum
                    bytes(idx + 2) = lum
                Next
            Next

            Marshal.Copy(bytes, 0, data.Scan0, byteCount)
        Finally
            bitmap.UnlockBits(data)
        End Try
    End Sub

    Private Shared Sub ApplyThreshold(bitmap As Bitmap, invert As Boolean)
        Dim rect As New Rectangle(0, 0, bitmap.Width, bitmap.Height)
        Dim data As BitmapData = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb)
        Try
            Dim stride As Integer = Math.Abs(data.Stride)
            Dim byteCount As Integer = stride * bitmap.Height
            Dim bytes(byteCount - 1) As Byte
            Marshal.Copy(data.Scan0, bytes, 0, byteCount)

            Dim sum As Long = 0
            Dim pixels As Long = CLng(bitmap.Width) * bitmap.Height
            For y As Integer = 0 To bitmap.Height - 1
                Dim row As Integer = y * stride
                For x As Integer = 0 To bitmap.Width - 1
                    Dim idx As Integer = row + (x * 3)
                    sum += Luminance(bytes(idx + 2), bytes(idx + 1), bytes(idx))
                Next
            Next

            Dim average As Integer = 128
            If pixels > 0 Then average = CInt(sum \ pixels)
            If average < 90 Then average = 90
            If average > 185 Then average = 185

            For y As Integer = 0 To bitmap.Height - 1
                Dim row As Integer = y * stride
                For x As Integer = 0 To bitmap.Width - 1
                    Dim idx As Integer = row + (x * 3)
                    Dim lum As Integer = CInt(Luminance(bytes(idx + 2), bytes(idx + 1), bytes(idx)))
                    Dim value As Integer = If(lum >= average, 255, 0)
                    If invert Then value = 255 - value
                    Dim b As Byte = CByte(value)
                    bytes(idx) = b
                    bytes(idx + 1) = b
                    bytes(idx + 2) = b
                Next
            Next

            Marshal.Copy(bytes, 0, data.Scan0, byteCount)
        Finally
            bitmap.UnlockBits(data)
        End Try
    End Sub

    Private Shared Function Luminance(r As Byte, g As Byte, b As Byte) As Byte
        Dim value As Integer = CInt(Math.Round((0.299R * r) + (0.587R * g) + (0.114R * b)))
        If value < 0 Then value = 0
        If value > 255 Then value = 255
        Return CByte(value)
    End Function

    Private Shared Function BuildLanguageAttempts(languageHint As String) As List(Of String)
        Dim attempts As List(Of String) = OcrTranslateSettings.OcrAttemptCodes(languageHint)
        If attempts.Count = 0 Then attempts.Add("eng")
        Return attempts
    End Function

    Private Shared Function BuildAttemptProfiles(languageAttempts As List(Of String), includeInverted As Boolean) As List(Of OcrAttemptProfile)
        Dim profiles As New List(Of OcrAttemptProfile)
        For Each lang As String In languageAttempts
            profiles.Add(New OcrAttemptProfile With {.Language = lang, .Segmentation = PageSegMode.Auto, .Preprocess = OcrPreprocessMode.Normal})
            profiles.Add(New OcrAttemptProfile With {.Language = lang, .Segmentation = PageSegMode.SparseText, .Preprocess = OcrPreprocessMode.Normal})
            profiles.Add(New OcrAttemptProfile With {.Language = lang, .Segmentation = PageSegMode.Auto, .Preprocess = OcrPreprocessMode.Grayscale})
            profiles.Add(New OcrAttemptProfile With {.Language = lang, .Segmentation = PageSegMode.Auto, .Preprocess = OcrPreprocessMode.Threshold})
            profiles.Add(New OcrAttemptProfile With {.Language = lang, .Segmentation = PageSegMode.SparseText, .Preprocess = OcrPreprocessMode.Threshold})
            If includeInverted Then
                profiles.Add(New OcrAttemptProfile With {.Language = lang, .Segmentation = PageSegMode.SparseText, .Preprocess = OcrPreprocessMode.ThresholdInverted})
            End If
        Next
        Return profiles
    End Function

    ''' <summary>Maps a forced-mode string to a PSM; False means "auto" (multi-try).</summary>
    Private Shared Function TryMapForcedMode(mode As String, ByRef psm As PageSegMode) As Boolean
        Select Case If(mode, "").Trim().ToLowerInvariant()
            Case "block" : psm = PageSegMode.SingleBlock : Return True
            Case "sparse" : psm = PageSegMode.SparseText : Return True
            Case "line" : psm = PageSegMode.SingleLine : Return True
            Case "vertical" : psm = PageSegMode.SingleBlockVertText : Return True
            Case Else : Return False
        End Select
    End Function

    ''' <summary>Profiles for a single forced PSM, still trying preprocess variants.</summary>
    Private Shared Function BuildForcedProfiles(languageAttempts As List(Of String), psm As PageSegMode, includeInverted As Boolean) As List(Of OcrAttemptProfile)
        Dim profiles As New List(Of OcrAttemptProfile)
        For Each lang As String In languageAttempts
            profiles.Add(New OcrAttemptProfile With {.Language = lang, .Segmentation = psm, .Preprocess = OcrPreprocessMode.Normal})
            profiles.Add(New OcrAttemptProfile With {.Language = lang, .Segmentation = psm, .Preprocess = OcrPreprocessMode.Grayscale})
            profiles.Add(New OcrAttemptProfile With {.Language = lang, .Segmentation = psm, .Preprocess = OcrPreprocessMode.Threshold})
            If includeInverted Then
                profiles.Add(New OcrAttemptProfile With {.Language = lang, .Segmentation = psm, .Preprocess = OcrPreprocessMode.ThresholdInverted})
            End If
        Next
        Return profiles
    End Function

    Private Shared Function ShouldTryInverted(source As Bitmap) As Boolean
        Dim sampleW As Integer = Math.Max(1, Math.Min(48, source.Width))
        Dim sampleH As Integer = Math.Max(1, Math.Min(48, source.Height))
        Using sample As New Bitmap(source, New Size(sampleW, sampleH))
            Dim darkPixels As Integer = 0
            Dim total As Integer = sampleW * sampleH
            Dim luminanceSum As Long = 0

            For y As Integer = 0 To sampleH - 1
                For x As Integer = 0 To sampleW - 1
                    Dim c As Color = sample.GetPixel(x, y)
                    Dim lum As Integer = CInt(Luminance(c.R, c.G, c.B))
                    luminanceSum += lum
                    If lum < 110 Then darkPixels += 1
                Next
            Next

            If total <= 0 Then Return False
            Dim average As Double = luminanceSum / CDbl(total)
            Return average < 135.0 OrElse darkPixels > (total \ 2)
        End Using
    End Function

    Private Shared Function ScoreAttempt(result As OcrResult, profile As OcrAttemptProfile) As OcrAttemptScore
        Dim totalChars As Integer = 0
        Dim usefulChars As Integer = 0
        Dim goodLines As Integer = 0
        Dim naturalLines As Integer = 0
        Dim suspiciousLines As Integer = 0
        Dim mixedScriptLines As Integer = 0
        Dim mixedLettersTotal As Integer = 0
        Dim expectedMismatchLines As Integer = 0
        Dim confSum As Double = 0.0
        Dim confCount As Integer = 0
        Dim expectedScript As TextScriptFamily = ExpectedScriptForLanguage(profile.Language)

        For Each line As OcrLine In result.Lines
            totalChars += line.Text.Length
            Dim lineUseful As Integer = OcrTextFilter.CountLettersAndDigits(line.Text)
            usefulChars += lineUseful
            If lineUseful >= 3 Then goodLines += 1

            Dim letters As Integer = 0
            Dim dominantShare As Double = 0.0
            Dim mixedLetters As Integer = 0
            Dim dominantScript As TextScriptFamily = TextScriptFamily.Unknown
            Dim hasDominantScript As Boolean = TryGetDominantScript(line.Text, dominantScript, dominantShare, letters, mixedLetters)
            Dim usefulRatio As Double = lineUseful / CDbl(Math.Max(1, line.Text.Length))
            Dim symbolNoise As Double = SymbolRatio(line.Text)
            Dim digitNoise As Double = DigitRatio(line.Text)
            Dim longestRun As Integer = OcrTextFilter.LongestLetterRun(line.Text)

            Dim looksNatural As Boolean =
                lineUseful >= 4 AndAlso
                usefulRatio >= 0.55 AndAlso
                longestRun >= 3 AndAlso
                symbolNoise <= 0.18 AndAlso
                digitNoise <= 0.3 AndAlso
                (Not hasDominantScript OrElse dominantShare >= 0.72)

            If looksNatural Then naturalLines += 1

            Dim looksSuspicious As Boolean =
                (lineUseful >= 4 AndAlso Not looksNatural) OrElse
                (hasDominantScript AndAlso letters >= 4 AndAlso dominantShare < 0.55) OrElse
                (line.Text.Length >= 8 AndAlso symbolNoise >= 0.28)

            If looksSuspicious Then suspiciousLines += 1
            If hasDominantScript AndAlso letters >= 4 AndAlso dominantShare < 0.72 Then mixedScriptLines += 1
            mixedLettersTotal += mixedLetters

            If expectedScript <> TextScriptFamily.Unknown AndAlso
               hasDominantScript AndAlso
               letters >= 4 AndAlso
               dominantScript <> TextScriptFamily.Unknown AndAlso
               dominantScript <> expectedScript AndAlso
               dominantShare >= 0.7 Then
                expectedMismatchLines += 1
            End If

            ' The LINE's confidence, not a mean over its words. While Words held one stub per
            ' line these were the same number; reading the words instead would have quietly
            ' re-weighted this average by word count - and this average picks the LANGUAGE -
            ' as a side effect of S7's geometry change. Section 18.4.
            confSum += line.Confidence
            confCount += 1
        Next

        Dim avgConfidence As Double = If(confCount > 0, confSum / confCount, 0.0)
        Dim score As Double =
            (goodLines * 90.0) +
            (naturalLines * 170.0) +
            (usefulChars * 2.5) +
            totalChars +
            (avgConfidence * 40.0) -
            (suspiciousLines * 110.0) -
            (mixedScriptLines * 70.0) -
            (mixedLettersTotal * 3.0) -
            (expectedMismatchLines * 80.0)
        If profile.Preprocess = OcrPreprocessMode.Normal Then score += 5.0
        If profile.Segmentation = PageSegMode.Auto Then score += 2.0

        Dim isStrong As Boolean =
            (naturalLines >= 2 AndAlso usefulChars >= 12 AndAlso suspiciousLines = 0 AndAlso mixedScriptLines = 0 AndAlso expectedMismatchLines = 0) OrElse
            (naturalLines >= 3 AndAlso usefulChars >= 18 AndAlso suspiciousLines <= 1 AndAlso expectedMismatchLines = 0) OrElse
            (avgConfidence >= 0.8 AndAlso naturalLines >= 1 AndAlso usefulChars >= 12 AndAlso suspiciousLines = 0 AndAlso expectedMismatchLines = 0)

        Return New OcrAttemptScore With {
            .Profile = profile,
            .Result = result,
            .Score = score,
            .IsStrong = isStrong
        }
    End Function

    Private Shared Function DigitRatio(text As String) As Double
        If String.IsNullOrEmpty(text) Then Return 0.0
        Dim digits As Integer = 0
        Dim nonWhitespace As Integer = 0
        For Each ch As Char In text
            If Char.IsWhiteSpace(ch) Then Continue For
            nonWhitespace += 1
            If Char.IsDigit(ch) Then digits += 1
        Next
        If nonWhitespace = 0 Then Return 0.0
        Return digits / CDbl(nonWhitespace)
    End Function

    ''' <summary>
    ''' Returns a tessdata directory that contains every requested language, or
    ''' Nothing if one could not be made available. Prefers a bundled or user
    ''' high-quality tessdata directory when present; otherwise downloads fast
    ''' models into the per-user directory.
    ''' </summary>
    Private Shared Function EnsureTessData(langs As String, preferBest As Boolean, Optional allowDownload As Boolean = True) As String
        Dim codes As String() = langs.Split("+"c)

        Dim bundledBest As String = OcrPaths.BundledBestTessDataDir()
        If Directory.Exists(bundledBest) AndAlso HasAllLanguages(bundledBest, codes) Then
            Return bundledBest
        End If

        Dim userBest As String = OcrPaths.UserBestTessDataDir()
        If Directory.Exists(userBest) AndAlso HasAllLanguages(userBest, codes) Then
            Return userBest
        End If

        ' "Best" quality: download the high-accuracy models into the best dir.
        If preferBest AndAlso allowDownload Then
            Directory.CreateDirectory(userBest)
            Dim allBest As Boolean = True
            For Each code As String In codes
                Dim bestDest As String = Path.Combine(userBest, code & ".traineddata")
                If File.Exists(bestDest) Then Continue For
                If Not DownloadTessDataFrom(TessDataBestBaseUrl, code, bestDest) Then
                    allBest = False
                    Exit For
                End If
            Next
            If allBest AndAlso HasAllLanguages(userBest, codes) Then Return userBest
            ' Best download failed -> fall through to the fast models below.
        End If

        Dim bundled As String = OcrPaths.BundledTessDataDir()
        If Directory.Exists(bundled) AndAlso HasAllLanguages(bundled, codes) Then
            Return bundled
        End If

        Dim userDir As String = OcrPaths.UserTessDataDir()
        Directory.CreateDirectory(userDir)

        For Each code As String In codes
            Dim fileName As String = code & ".traineddata"
            Dim dest As String = Path.Combine(userDir, fileName)
            If File.Exists(dest) Then Continue For

            Dim bundledFile As String = Path.Combine(bundled, fileName)
            If File.Exists(bundledFile) Then
                File.Copy(bundledFile, dest, True)
                Continue For
            End If

            If Not allowDownload Then Return Nothing
            If Not DownloadTessData(code, dest) Then
                Return Nothing
            End If
        Next

        Return If(HasAllLanguages(userDir, codes), userDir, Nothing)
    End Function

    Private Shared Function HasAllLanguages(dir As String, codes As String()) As Boolean
        For Each code As String In codes
            If Not File.Exists(Path.Combine(dir, code & ".traineddata")) Then Return False
        Next
        Return True
    End Function

    Private Shared Function DownloadTessData(code As String, dest As String) As Boolean
        Return DownloadTessDataFrom(TessDataBaseUrl, code, dest)
    End Function

    Private Shared Function DownloadTessDataFrom(baseUrl As String, code As String, dest As String) As Boolean
        Dim url As String = baseUrl & code & ".traineddata"
        Dim tmp As String = dest & ".part"
        Try
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
            Dim req As HttpWebRequest = DirectCast(WebRequest.Create(url), HttpWebRequest)
            req.Timeout = DownloadTimeoutMs
            req.ReadWriteTimeout = DownloadTimeoutMs
            req.UserAgent = "FastMediaSorter"
            Using resp As HttpWebResponse = DirectCast(req.GetResponse(), HttpWebResponse)
                Using respStream As Stream = resp.GetResponseStream()
                    Using fs As New FileStream(tmp, FileMode.Create, FileAccess.Write)
                        respStream.CopyTo(fs)
                    End Using
                End Using
            End Using
            If File.Exists(dest) Then File.Delete(dest)
            File.Move(tmp, dest)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: downloaded tessdata " & code)
            Return True
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: tessdata download failed " & code & ": " & ex.Message)
            Try
                If File.Exists(tmp) Then File.Delete(tmp)
            Catch
            End Try
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Explicitly download/install the language data for the given tesseract
    ''' codes. Safe to call from a background thread.
    ''' </summary>
    Public Shared Function EnsureLanguagesPublic(languages As String) As Boolean
        Return EnsureLanguagesPublic(languages, False)
    End Function

    ''' <summary>The Tesseract codes recognition will try for a source-language hint.</summary>
    Public Shared Function RecognitionLanguageCodes(languageHint As String) As List(Of String)
        Return BuildLanguageAttempts(languageHint)
    End Function

    ''' <summary>The Tesseract codes the settings' "download pack" button means.</summary>
    Public Shared Function DownloadLanguageCodes(languages As String) As List(Of String)
        Return New List(Of String)(NormalizeDownloadLanguages(languages).Split("+"c))
    End Function

    ''' <summary>
    ''' The codes with no language data on disk (ticket 028 Ф-6). With <paramref name="preferBest"/>
    ''' a code counts as present only in a "best" directory - so the answer is what a
    ''' download of the best models would fetch; without it, data of either quality will do.
    ''' Local disk reads only: this is what lets the caller ask BEFORE anything is fetched.
    ''' </summary>
    Public Shared Function MissingLanguageCodes(codes As IEnumerable(Of String), preferBest As Boolean) As List(Of String)
        Dim dirs As New List(Of String) From {OcrPaths.BundledBestTessDataDir(), OcrPaths.UserBestTessDataDir()}
        If Not preferBest Then
            dirs.Add(OcrPaths.BundledTessDataDir())
            dirs.Add(OcrPaths.UserTessDataDir())
        End If
        Dim missing As New List(Of String)
        For Each code As String In codes
            If String.IsNullOrWhiteSpace(code) OrElse missing.Contains(code) Then Continue For
            Dim present As Boolean = False
            For Each dir As String In dirs
                If File.Exists(Path.Combine(dir, code & ".traineddata")) Then
                    present = True
                    Exit For
                End If
            Next
            If Not present Then missing.Add(code)
        Next
        Return missing
    End Function

    ''' <summary>
    ''' Downloads the given language packs, one file at a time, each to a ".part" file that
    ''' is renamed only once complete - so a Cancel or a failure leaves no partial pack that
    ''' recognition would later try to load (APP-BEHAVIOUR rule 3). Throws on failure and
    ''' OperationCanceledException on Cancel; the caller runs it behind the download dialog.
    ''' </summary>
    Public Shared Async Function DownloadLanguagesAsync(codes As IList(Of String), preferBest As Boolean,
                                                        progress As IProgress(Of DownloadReport),
                                                        cancel As CancellationToken) As Task
        Dim targetDir As String = If(preferBest, OcrPaths.UserBestTessDataDir(), OcrPaths.UserTessDataDir())
        Dim baseUrl As String = If(preferBest, TessDataBestBaseUrl, TessDataBaseUrl)
        Directory.CreateDirectory(targetDir)
        For Each code As String In codes
            Dim dest As String = Path.Combine(targetDir, code & ".traineddata")
            If File.Exists(dest) Then Continue For
            Dim tmp As String = dest & ".part"
            Try
                Await OptionalRuntimeManager.DownloadWithInactivityTimeoutAsync(baseUrl & code & ".traineddata", tmp, progress, cancel).ConfigureAwait(False)
                If File.Exists(dest) Then File.Delete(dest)
                File.Move(tmp, dest)
            Finally
                Try
                    If File.Exists(tmp) Then File.Delete(tmp)
                Catch
                End Try
            End Try
        Next
    End Function

    Public Shared Function EnsureLanguagesPublic(languages As String, preferBest As Boolean) As Boolean
        Try
            Return EnsureTessData(NormalizeDownloadLanguages(languages), preferBest) IsNot Nothing
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: ensure-languages failed: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function NormalizeDownloadLanguages(languages As String) As String
        If String.IsNullOrWhiteSpace(languages) Then Return "eng"

        Dim normalized As New List(Of String)
        For Each part As String In languages.Split("+"c)
            Dim code As String = OcrTranslateSettings.NormalizeOcrSourceCode(part)
            If code = "auto" Then
                For Each autoCode As String In OcrTranslateSettings.OcrAttemptCodes("auto")
                    If Not normalized.Contains(autoCode) Then normalized.Add(autoCode)
                Next
            ElseIf code.Length > 0 AndAlso Not normalized.Contains(code) Then
                normalized.Add(code)
            End If
        Next

        If normalized.Count = 0 Then normalized.Add("eng")
        Return String.Join("+", normalized.ToArray())
    End Function

    Public Sub DisposeEngine()
        SyncLock sync
            DisposeCachedEngine()
        End SyncLock
    End Sub

    ''' <summary>
    ''' Shutdown: disposes the engine only if nothing is using it right now, and never waits.
    ''' A pass already inside engine.Process cannot be interrupted by cancellation, and
    ''' waiting for it here hung the closing window for seconds with "best" models (audit 051
    ''' E-15); an engine still busy is left to process exit. Either way no new engine is built
    ''' after this, so a job that slipped past its cancellation check cannot re-create one.
    ''' </summary>
    Public Sub RetireEngine()
        Volatile.Write(retired, True)
        If Not Monitor.TryEnter(sync, 0) Then Return
        Try
            DisposeCachedEngine()
        Finally
            Monitor.Exit(sync)
        End Try
    End Sub

    Private Sub DisposeCachedEngine()
        If cachedEngine IsNot Nothing Then
            Try
                cachedEngine.Dispose()
            Catch
            End Try
            cachedEngine = Nothing
            cachedKey = ""
        End If
    End Sub

End Class
