Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Web.Script.Serialization

' --- Disk DTOs ----------------------------------------------------------------
' The in-memory model uses System.Drawing.Rectangle, which JavaScriptSerializer
' renders awkwardly. These flat DTOs keep the JSON small and stable.

Public Class OcrCacheWord
    Public Property Text As String = ""
    Public Property X As Integer
    Public Property Y As Integer
    Public Property W As Integer
    Public Property H As Integer
    Public Property Conf As Single
End Class

Public Class OcrCacheLine
    Public Property Text As String = ""
    Public Property X As Integer
    Public Property Y As Integer
    Public Property W As Integer
    Public Property H As Integer
    Public Property Words As New List(Of OcrCacheWord)
End Class

Public Class OcrCacheBlock
    Public Property SourceText As String = ""
    Public Property TranslatedText As String = ""
    Public Property X As Integer
    Public Property Y As Integer
    Public Property W As Integer
    Public Property H As Integer
    Public Property Lines As New List(Of OcrCacheLine)
End Class

Public Class OcrCacheDoc
    Public Property FilePath As String = ""
    Public Property FileWriteTicks As Long
    Public Property ImgW As Integer
    Public Property ImgH As Integer
    Public Property SourceLanguage As String = ""
    Public Property TargetLanguage As String = ""
    Public Property Engine As String = ""
    Public Property Translator As String = ""
    Public Property Blocks As New List(Of OcrCacheBlock)
End Class

''' <summary>
''' Two-tier cache for overlay documents: a process-lifetime in-memory map plus
''' an optional JSON disk cache under %LOCALAPPDATA%\SZA\FastMediaSorter\ocr-cache.
''' Key = filePath|fileWriteTicks|ocrEngine|translator|srcLang|dstLang.
''' </summary>
Public Class TranslationCache

    Private ReadOnly memory As New Dictionary(Of String, OcrOverlayDocument)(StringComparer.Ordinal)
    Private ReadOnly sync As New Object()

    Public Shared Function BuildKey(filePath As String, fileWriteTicks As Long, ocrEngine As String, translator As String, srcLang As String, dstLang As String) As String
        Return String.Join("|", filePath, fileWriteTicks.ToString(), ocrEngine, translator, srcLang, dstLang)
    End Function

    Public Function TryGetMemory(key As String) As OcrOverlayDocument
        SyncLock sync
            Dim doc As OcrOverlayDocument = Nothing
            memory.TryGetValue(key, doc)
            Return doc
        End SyncLock
    End Function

    Public Sub PutMemory(key As String, doc As OcrOverlayDocument)
        SyncLock sync
            memory(key) = doc
        End SyncLock
    End Sub

    Public Function TryLoadFromDisk(key As String) As OcrOverlayDocument
        Try
            Dim cacheFile As String = DiskPath(key)
            If Not File.Exists(cacheFile) Then Return Nothing
            Dim json As String = File.ReadAllText(cacheFile, Encoding.UTF8)
            Dim serializer As JavaScriptSerializer = TranslateHttp.NewSerializer()
            Dim dto As OcrCacheDoc = serializer.Deserialize(Of OcrCacheDoc)(json)
            If dto Is Nothing Then Return Nothing
            Return FromDto(dto)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr-cache: load failed: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Sub SaveToDisk(key As String, doc As OcrOverlayDocument)
        Try
            Dim dir As String = OcrPaths.OcrCacheDir()
            Directory.CreateDirectory(dir)
            Dim serializer As JavaScriptSerializer = TranslateHttp.NewSerializer()
            Dim json As String = serializer.Serialize(ToDto(doc))
            File.WriteAllText(DiskPath(key), json, Encoding.UTF8)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr-cache: save failed: " & ex.Message)
        End Try
    End Sub

    Private Shared Function DiskPath(key As String) As String
        Return Path.Combine(OcrPaths.OcrCacheDir(), HashKey(key) & ".json")
    End Function

    Private Shared Function HashKey(key As String) As String
        Using sha As SHA1 = SHA1.Create()
            Dim bytes As Byte() = sha.ComputeHash(Encoding.UTF8.GetBytes(key))
            Dim sb As New StringBuilder(bytes.Length * 2)
            For Each b As Byte In bytes
                sb.Append(b.ToString("x2"))
            Next
            Return sb.ToString()
        End Using
    End Function

    ' --- mapping --------------------------------------------------------------

    Private Shared Function ToDto(doc As OcrOverlayDocument) As OcrCacheDoc
        Dim dto As New OcrCacheDoc With {
            .FilePath = doc.FilePath,
            .FileWriteTicks = doc.FileWriteTicks,
            .ImgW = doc.ImageSize.Width,
            .ImgH = doc.ImageSize.Height,
            .SourceLanguage = doc.SourceLanguage,
            .TargetLanguage = doc.TargetLanguage,
            .Engine = doc.Engine,
            .Translator = doc.Translator
        }
        For Each b As OcrBlock In doc.Blocks
            Dim cb As New OcrCacheBlock With {
                .SourceText = b.SourceText,
                .TranslatedText = b.TranslatedText,
                .X = b.Box.X, .Y = b.Box.Y, .W = b.Box.Width, .H = b.Box.Height
            }
            For Each l As OcrLine In b.Lines
                Dim cl As New OcrCacheLine With {
                    .Text = l.Text,
                    .X = l.Box.X, .Y = l.Box.Y, .W = l.Box.Width, .H = l.Box.Height
                }
                For Each w As OcrWord In l.Words
                    cl.Words.Add(New OcrCacheWord With {
                        .Text = w.Text, .Conf = w.Confidence,
                        .X = w.Box.X, .Y = w.Box.Y, .W = w.Box.Width, .H = w.Box.Height
                    })
                Next
                cb.Lines.Add(cl)
            Next
            dto.Blocks.Add(cb)
        Next
        Return dto
    End Function

    Private Shared Function FromDto(dto As OcrCacheDoc) As OcrOverlayDocument
        Dim doc As New OcrOverlayDocument With {
            .FilePath = dto.FilePath,
            .FileWriteTicks = dto.FileWriteTicks,
            .ImageSize = New Size(dto.ImgW, dto.ImgH),
            .SourceLanguage = dto.SourceLanguage,
            .TargetLanguage = dto.TargetLanguage,
            .Engine = dto.Engine,
            .Translator = dto.Translator
        }
        For Each cb As OcrCacheBlock In dto.Blocks
            Dim b As New OcrBlock With {
                .SourceText = cb.SourceText,
                .TranslatedText = cb.TranslatedText,
                .Box = New Rectangle(cb.X, cb.Y, cb.W, cb.H)
            }
            For Each cl As OcrCacheLine In cb.Lines
                Dim l As New OcrLine With {
                    .Text = cl.Text,
                    .Box = New Rectangle(cl.X, cl.Y, cl.W, cl.H)
                }
                For Each cw As OcrCacheWord In cl.Words
                    l.Words.Add(New OcrWord With {
                        .Text = cw.Text, .Confidence = cw.Conf,
                        .Box = New Rectangle(cw.X, cw.Y, cw.W, cw.H)
                    })
                Next
                b.Lines.Add(l)
            Next
            doc.Blocks.Add(b)
        Next
        Return doc
    End Function

End Class
