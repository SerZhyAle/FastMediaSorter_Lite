Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Net
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

    ''' <summary>tessdata folder bundled next to the exe, if the build shipped one.</summary>
    Public Function BundledTessDataDir() As String
        Return Path.Combine(ExeDir(), "tessdata")
    End Function

    Public Function OcrCacheDir() As String
        Return Path.Combine(AppDataRoot(), "ocr-cache")
    End Function

End Module

''' <summary>
''' v1 primary OCR engine: Tesseract 5 for .NET Framework 4.8.
''' Reads from an in-memory BMP (via Pix.LoadFromMemory) so we never depend on
''' Tesseract.Drawing / System.Drawing.Common. Missing native runtime or missing
''' language data degrade to <see cref="OcrStatus.RuntimeMissing"/> instead of
''' throwing, per the spec's graceful-failure requirement.
''' </summary>
Public Class TesseractOcrEngine
    Implements IOcrEngine

    Private Const MaxOcrDimension As Integer = 2600
    Private Const DownloadTimeoutMs As Integer = 60000
    Private Const TessDataBaseUrl As String = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/"

    Private ReadOnly sync As New Object()
    Private cachedEngine As TesseractEngine
    Private cachedKey As String = ""

    Public ReadOnly Property Name As String Implements IOcrEngine.Name
        Get
            Return "tesseract"
        End Get
    End Property

    Public Function Recognize(source As Bitmap, languages As String) As OcrResult Implements IOcrEngine.Recognize
        If source Is Nothing Then Return OcrResult.FromError("no image")

        Dim langs As String = NormalizeLanguages(languages)

        ' Make sure the language data exists (download fast models on first use).
        Dim dataDir As String
        Try
            dataDir = EnsureTessData(langs)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: tessdata error: " & ex.Message)
            Return OcrResult.Runtime("language data unavailable")
        End Try

        If dataDir Is Nothing Then
            Return OcrResult.Runtime("language data unavailable")
        End If

        ' Downscale a flattened (white-background, 24bpp) copy for OCR speed and
        ' encode it to an in-memory BMP that leptonica reads natively.
        Dim invScale As Double = 1.0
        Dim bmpBytes As Byte()
        Try
            bmpBytes = BuildOcrBuffer(source, invScale)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: buffer error: " & ex.Message)
            Return OcrResult.FromError("image preparation failed")
        End Try

        Try
            SyncLock sync
                Dim engine As TesseractEngine = GetEngine(dataDir, langs)

                Using pix As Pix = Pix.LoadFromMemory(bmpBytes)
                    Using page As Page = engine.Process(pix, PageSegMode.Auto)
                        Dim lines As List(Of OcrLine) = ExtractLines(page, invScale, source.Width, source.Height)
                        Dim result As New OcrResult With {
                            .Status = If(lines.Count > 0, OcrStatus.Ok, OcrStatus.NoText),
                            .Lines = lines
                        }
                        Return result
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
            Return OcrResult.Runtime("OCR runtime/data problem")
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: error: " & ex.Message)
            ' A native/loader failure often surfaces as a generic exception the
            ' first time; treat as runtime-missing so the UI message is helpful.
            If TypeOf ex Is BadImageFormatException OrElse
               ex.Message.IndexOf("leptonica", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               ex.Message.IndexOf("tesseract", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return OcrResult.Runtime("OCR native runtime missing")
            End If
            Return OcrResult.FromError(ex.Message)
        End Try
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

        ' tessdata_fast ships LSTM models only -> LstmOnly avoids legacy-data errors.
        cachedEngine = New TesseractEngine(dataDir, langs, EngineMode.LstmOnly)
        cachedKey = key
        Return cachedEngine
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
                        If cleaned.Length > 0 Then
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
        Dim s As String = raw.Replace(vbCr, " ").Replace(vbLf, " ").Trim()
        While s.IndexOf("  ", StringComparison.Ordinal) >= 0
            s = s.Replace("  ", " ")
        End While
        Return s
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

    ''' <summary>Flatten + optionally downscale to a 24bpp BMP byte[]; sets invScale.</summary>
    Private Shared Function BuildOcrBuffer(source As Bitmap, ByRef invScale As Double) As Byte()
        Dim w As Integer = source.Width
        Dim h As Integer = source.Height
        Dim scale As Double = 1.0
        Dim longest As Integer = Math.Max(w, h)
        If longest > MaxOcrDimension Then
            scale = MaxOcrDimension / CDbl(longest)
        End If
        invScale = 1.0 / scale

        Dim tw As Integer = Math.Max(1, CInt(Math.Round(w * scale)))
        Dim th As Integer = Math.Max(1, CInt(Math.Round(h * scale)))

        Using flat As New Bitmap(tw, th, PixelFormat.Format24bppRgb)
            Using g As Graphics = Graphics.FromImage(flat)
                g.Clear(Color.White)
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.DrawImage(source, New Rectangle(0, 0, tw, th), 0, 0, w, h, GraphicsUnit.Pixel)
            End Using
            Using ms As New MemoryStream()
                flat.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp)
                Return ms.ToArray()
            End Using
        End Using
    End Function

    Private Shared Function NormalizeLanguages(languages As String) As String
        If String.IsNullOrWhiteSpace(languages) OrElse
           String.Equals(languages.Trim(), "auto", StringComparison.OrdinalIgnoreCase) Then
            Return "eng+rus+ukr"
        End If
        Return languages.Trim()
    End Function

    ''' <summary>
    ''' Returns a tessdata directory that contains every requested language, or
    ''' Nothing if one could not be made available. Prefers a bundled tessdata
    ''' folder; otherwise downloads fast models into the per-user directory.
    ''' </summary>
    Private Shared Function EnsureTessData(langs As String) As String
        Dim codes As String() = langs.Split("+"c)

        ' Fast path: a bundled tessdata folder that already has everything.
        Dim bundled As String = OcrPaths.BundledTessDataDir()
        If Directory.Exists(bundled) AndAlso HasAllLanguages(bundled, codes) Then
            Return bundled
        End If

        ' Otherwise consolidate into the writable per-user directory.
        Dim userDir As String = OcrPaths.UserTessDataDir()
        Directory.CreateDirectory(userDir)

        For Each code As String In codes
            Dim fileName As String = code & ".traineddata"
            Dim dest As String = Path.Combine(userDir, fileName)
            If File.Exists(dest) Then Continue For

            ' Copy from a bundled folder when present (offline-friendly).
            Dim bundledFile As String = Path.Combine(bundled, fileName)
            If File.Exists(bundledFile) Then
                File.Copy(bundledFile, dest, True)
                Continue For
            End If

            ' Last resort: download the fast model.
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
        Dim url As String = TessDataBaseUrl & code & ".traineddata"
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
    ''' codes (e.g. "eng+rus+ukr"). Safe to call from a background thread.
    ''' Returns True when every requested language is available afterwards.
    ''' </summary>
    Public Shared Function EnsureLanguagesPublic(languages As String) As Boolean
        Try
            Return EnsureTessData(NormalizeLanguages(languages)) IsNot Nothing
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: ensure-languages failed: " & ex.Message)
            Return False
        End Try
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
