Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Net
Imports System.Runtime.InteropServices
Imports Tesseract

''' <summary>
''' Resolves the on-disk locations the OCR feature uses. The native Tesseract
''' DLLs (x86/x64) ship next to the executable via the NuGet .targets; the
''' language data and cache live under %LOCALAPPDATA% so a low-privilege install
''' (Program Files) never has to be writable.
''' </summary>
Friend Module OcrPaths

    Public Function ExeDir() As String
        Return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
    End Function

    ''' <summary>%LOCALAPPDATA%\SZA\FastMediaSorter</summary>
    Public Function AppDataRoot() As String
        Dim root As String = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            App_name, Second_App_Name)
        Return root
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
''' </summary>
Public Class TesseractOcrEngine
    Implements IOcrEngine

    Private Const MaxOcrDimension As Integer = 3200
    Private Const MinOcrDimension As Integer = 1200
    Private Const MaxUpscaleFactor As Double = 1.75
    Private Const DownloadTimeoutMs As Integer = 60000
    Private Const TessDataBaseUrl As String = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/"
    Private Const TessDataBestBaseUrl As String = "https://github.com/tesseract-ocr/tessdata_best/raw/main/"

    Private ReadOnly sync As New Object()
    Private cachedEngine As TesseractEngine
    Private cachedKey As String = ""

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

    Public Function Recognize(source As Bitmap, languages As String) As OcrResult Implements IOcrEngine.Recognize
        If source Is Nothing Then Return OcrResult.FromError("no image")

        Dim languageAttempts As List(Of String) = BuildLanguageAttempts(languages)
        Dim includeInverted As Boolean = ShouldTryInverted(source)
        Dim forcedPsm As PageSegMode
        Dim profiles As List(Of OcrAttemptProfile)
        If TryMapForcedMode(ForcedPageMode, forcedPsm) Then
            profiles = BuildForcedProfiles(languageAttempts, forcedPsm, includeInverted)
        Else
            profiles = BuildAttemptProfiles(languageAttempts, includeInverted)
        End If
        Dim preparedImages As Dictionary(Of OcrPreprocessMode, PreparedOcrImage)

        Try
            preparedImages = BuildPreparedImages(source, profiles)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: buffer error: " & ex.Message)
            Return OcrResult.FromError("image preparation failed")
        End Try

        Dim best As OcrAttemptScore = Nothing
        Dim lastFailure As OcrResult = Nothing

        For Each profile As OcrAttemptProfile In profiles
            Dim dataDir As String
            Try
                dataDir = EnsureTessData(profile.Language, PreferBest)
            Catch ex As Exception
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: tessdata error: " & ex.Message)
                Return OcrResult.Runtime("language data unavailable")
            End Try

            If dataDir Is Nothing Then
                Return OcrResult.Runtime("language data unavailable")
            End If

            Dim attemptResult As OcrResult = RecognizeSingleAttempt(source, preparedImages(profile.Preprocess), dataDir, profile)
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
        Return New OcrResult With {.Status = OcrStatus.NoText, .Message = "no text after retries"}
    End Function

    Private Function GetEngine(dataDir As String, langs As String) As TesseractEngine
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
                        Dim lines As List(Of OcrLine) = ExtractLines(page, prepared.InvScale, source.Width, source.Height)
                        Return New OcrResult With {
                            .Status = If(lines.Count > 0, OcrStatus.Ok, OcrStatus.NoText),
                            .Lines = lines
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
            Return OcrResult.FromError(ex.Message)
        End Try
    End Function

    Private Shared Function ExtractLines(page As Page, invScale As Double, maxW As Integer, maxH As Integer) As List(Of OcrLine)
        Dim lines As New List(Of OcrLine)

        Using iter As ResultIterator = page.GetIterator()
            iter.Begin()
            Do
                Dim r As Rect = Nothing
                If iter.TryGetBoundingBox(PageIteratorLevel.TextLine, r) Then
                    Dim txt As String = iter.GetText(PageIteratorLevel.TextLine)
                    If Not String.IsNullOrWhiteSpace(txt) Then
                        Dim conf As Single = iter.GetConfidence(PageIteratorLevel.TextLine) / 100.0F
                        Dim box As Rectangle = MapBox(r, invScale, maxW, maxH)
                        Dim cleaned As String = CleanLineText(txt)
                        If IsAcceptableLine(cleaned, conf) Then
                            lines.Add(New OcrLine With {
                                .Text = cleaned,
                                .Box = box,
                                .Words = New List(Of OcrWord) From {
                                    New OcrWord With {.Text = cleaned, .Box = box, .Confidence = conf}
                                }
                            })
                        End If
                    End If
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

    Private Shared Function CountUsefulCharacters(text As String) As Integer
        Dim count As Integer = 0
        For Each ch As Char In text
            If Char.IsLetterOrDigit(ch) Then count += 1
        Next
        Return count
    End Function

    ''' <summary>
    ''' Rejects the stray 1-2 character "lines" Tesseract hallucinates from
    ''' textured backgrounds (skin, fabric, hair) while keeping real text.
    ''' Keys on word length, NOT raw confidence: downscaled page text frequently
    ''' scores low yet is correct, so confidence-only filtering was dropping the
    ''' actual speech text.
    ''' </summary>
    Private Shared Function IsAcceptableLine(text As String, confidence As Single) As Boolean
        Dim useful As Integer = CountUsefulCharacters(text)
        If useful = 0 Then Return False
        ' Mostly punctuation/symbols -> noise.
        If useful / CDbl(Math.Max(1, text.Length)) < 0.45 Then Return False
        ' A real text line contains at least one proper word (3+ letters); accept
        ' it regardless of confidence.
        If LongestLetterRun(text) >= 3 Then Return True
        ' Otherwise keep only highly-confident short tokens (e.g. "OK", "Да").
        Return useful >= 2 AndAlso confidence >= 0.85F
    End Function

    Private Shared Function LongestLetterRun(text As String) As Integer
        Dim best As Integer = 0
        Dim current As Integer = 0
        For Each ch As Char In text
            If Char.IsLetter(ch) Then
                current += 1
                If current > best Then best = current
            Else
                current = 0
            End If
        Next
        Return best
    End Function

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

    Private Shared Function BuildPreparedImages(source As Bitmap, profiles As List(Of OcrAttemptProfile)) As Dictionary(Of OcrPreprocessMode, PreparedOcrImage)
        Dim result As New Dictionary(Of OcrPreprocessMode, PreparedOcrImage)()
        For Each mode As OcrPreprocessMode In profiles.Select(Function(p) p.Preprocess).Distinct()
            Dim invScale As Double = 1.0
            Dim bytes As Byte() = BuildOcrBuffer(source, mode, invScale)
            result(mode) = New PreparedOcrImage With {.Bytes = bytes, .InvScale = invScale}
        Next
        Return result
    End Function

    ''' <summary>Flatten + preprocess to a 24bpp BMP byte[]; sets invScale.</summary>
    Private Shared Function BuildOcrBuffer(source As Bitmap, preprocess As OcrPreprocessMode, ByRef invScale As Double) As Byte()
        Dim w As Integer = source.Width
        Dim h As Integer = source.Height
        Dim scale As Double = ComputeScale(w, h)
        invScale = 1.0 / scale

        Dim tw As Integer = Math.Max(1, CInt(Math.Round(w * scale)))
        Dim th As Integer = Math.Max(1, CInt(Math.Round(h * scale)))

        Using flat As Bitmap = RenderFlatBitmap(source, w, h, tw, th)
            ApplyPreprocess(flat, preprocess)
            Using ms As New MemoryStream()
                flat.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp)
                Return ms.ToArray()
            End Using
        End Using
    End Function

    Private Shared Function ComputeScale(width As Integer, height As Integer) As Double
        Dim longest As Integer = Math.Max(width, height)
        If longest <= 0 Then Return 1.0

        If longest > MaxOcrDimension Then
            Return MaxOcrDimension / CDbl(longest)
        End If

        If longest < MinOcrDimension Then
            Return Math.Min(MaxUpscaleFactor, MinOcrDimension / CDbl(longest))
        End If

        Return 1.0
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
        Dim confSum As Double = 0.0
        Dim confCount As Integer = 0

        For Each line As OcrLine In result.Lines
            totalChars += line.Text.Length
            Dim lineUseful As Integer = CountUsefulCharacters(line.Text)
            usefulChars += lineUseful
            If lineUseful >= 3 Then goodLines += 1
            For Each word As OcrWord In line.Words
                confSum += word.Confidence
                confCount += 1
            Next
        Next

        Dim avgConfidence As Double = If(confCount > 0, confSum / confCount, 0.0)
        Dim score As Double = (goodLines * 120.0) + (usefulChars * 3.0) + totalChars + (avgConfidence * 40.0)
        If profile.Preprocess = OcrPreprocessMode.Normal Then score += 5.0
        If profile.Segmentation = PageSegMode.Auto Then score += 2.0

        Dim isStrong As Boolean =
            (goodLines >= 2 AndAlso usefulChars >= 10) OrElse
            (goodLines >= 1 AndAlso usefulChars >= 20) OrElse
            (avgConfidence >= 0.75 AndAlso usefulChars >= 8)

        Return New OcrAttemptScore With {
            .Profile = profile,
            .Result = result,
            .Score = score,
            .IsStrong = isStrong
        }
    End Function

    ''' <summary>
    ''' Returns a tessdata directory that contains every requested language, or
    ''' Nothing if one could not be made available. Prefers a bundled or user
    ''' high-quality tessdata directory when present; otherwise downloads fast
    ''' models into the per-user directory.
    ''' </summary>
    Private Shared Function EnsureTessData(langs As String, preferBest As Boolean) As String
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
        If preferBest Then
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
            If cachedEngine IsNot Nothing Then
                Try
                    cachedEngine.Dispose()
                Catch
                End Try
                cachedEngine = Nothing
                cachedKey = ""
            End If
        End SyncLock
    End Sub

End Class
