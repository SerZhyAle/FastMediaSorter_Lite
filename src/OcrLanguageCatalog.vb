Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Text
Imports System.IO

''' <summary>One selectable language with its display names and flag key.</summary>
Public Class LanguageEntry
    Public ReadOnly Code As String       ' app/translate code, e.g. "en", "zh-TW", "auto"
    Public ReadOnly NameEn As String
    Public ReadOnly NameRu As String
    Public Sub New(code As String, nameEn As String, nameRu As String)
        Me.Code = code
        Me.NameEn = nameEn
        Me.NameRu = nameRu
    End Sub
    Public Function DisplayName(russian As Boolean) As String
        Return If(russian, NameRu, NameEn)
    End Function
End Class

''' <summary>
''' The full language list shown in the OCR + Translation pickers. "auto"
''' (auto-detect) is first and is the default for the OCR source language.
''' Flag PNGs live in &lt;exe&gt;\flags\&lt;code&gt;.png (see FlagImages).
''' </summary>
Public Module OcrLanguageCatalog

    Private ReadOnly _all As LanguageEntry() = {
        New LanguageEntry("auto", "Auto-detect", "Автоопределение"),
        New LanguageEntry("en", "English", "Английский"),
        New LanguageEntry("ru", "Russian", "Русский"),
        New LanguageEntry("uk", "Ukrainian", "Украинский"),
        New LanguageEntry("be", "Belarusian", "Белорусский"),
        New LanguageEntry("de", "German", "Немецкий"),
        New LanguageEntry("fr", "French", "Французский"),
        New LanguageEntry("es", "Spanish", "Испанский"),
        New LanguageEntry("it", "Italian", "Итальянский"),
        New LanguageEntry("pt", "Portuguese", "Португальский"),
        New LanguageEntry("nl", "Dutch", "Нидерландский"),
        New LanguageEntry("pl", "Polish", "Польский"),
        New LanguageEntry("cs", "Czech", "Чешский"),
        New LanguageEntry("sk", "Slovak", "Словацкий"),
        New LanguageEntry("sv", "Swedish", "Шведский"),
        New LanguageEntry("no", "Norwegian", "Норвежский"),
        New LanguageEntry("da", "Danish", "Датский"),
        New LanguageEntry("fi", "Finnish", "Финский"),
        New LanguageEntry("tr", "Turkish", "Турецкий"),
        New LanguageEntry("el", "Greek", "Греческий"),
        New LanguageEntry("bg", "Bulgarian", "Болгарский"),
        New LanguageEntry("ro", "Romanian", "Румынский"),
        New LanguageEntry("hu", "Hungarian", "Венгерский"),
        New LanguageEntry("ja", "Japanese", "Японский"),
        New LanguageEntry("ko", "Korean", "Корейский"),
        New LanguageEntry("zh", "Chinese (Simplified)", "Китайский (упр.)"),
        New LanguageEntry("zh-TW", "Chinese (Traditional)", "Китайский (трад.)"),
        New LanguageEntry("ar", "Arabic", "Арабский"),
        New LanguageEntry("he", "Hebrew", "Иврит"),
        New LanguageEntry("hi", "Hindi", "Хинди"),
        New LanguageEntry("th", "Thai", "Тайский"),
        New LanguageEntry("vi", "Vietnamese", "Вьетнамский"),
        New LanguageEntry("id", "Indonesian", "Индонезийский"),
        New LanguageEntry("fa", "Persian", "Персидский")
    }

    ''' <summary>All entries including "auto" (for the OCR source picker).</summary>
    Public Function SourceLanguages() As LanguageEntry()
        Return _all
    End Function

    ''' <summary>All entries except "auto" (for the translation target picker).</summary>
    Public Function TargetLanguages() As LanguageEntry()
        Return _all.Where(Function(e) e.Code <> "auto").ToArray()
    End Function

End Module

''' <summary>
''' Loads/caches the flag images that decorate the language pickers. Real flag
''' PNGs are shipped in &lt;exe&gt;\flags; "auto" and any missing flag are drawn
''' on the fly so the UI always shows something.
''' </summary>
Public Module FlagImages

    Private ReadOnly cache As New Dictionary(Of String, Image)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly sync As New Object()

    Public Const FlagWidth As Integer = 32
    Public Const FlagHeight As Integer = 24

    Public Function [Get](code As String) As Image
        Dim key As String = If(code, "auto").Trim()
        If key.Length = 0 Then key = "auto"

        SyncLock sync
            Dim img As Image = Nothing
            If cache.TryGetValue(key, img) Then Return img

            img = LoadOrDraw(key)
            cache(key) = img
            Return img
        End SyncLock
    End Function

    Private Function LoadOrDraw(code As String) As Image
        If String.Equals(code, "auto", StringComparison.OrdinalIgnoreCase) Then
            Return DrawAuto()
        End If

        Try
            Using bundled As Stream = RuntimeBootstrap.OpenBundledAsset("flags/" & code & ".png")
                If bundled IsNot Nothing Then
                    Using fromStream As Image = Image.FromStream(bundled)
                        Return New Bitmap(fromStream)
                    End Using
                End If
            End Using

            Dim flagPath As String = Path.Combine(FlagsDir(), code & ".png")
            If File.Exists(flagPath) Then
                Using fromFile As Image = Image.FromFile(flagPath)
                    Return New Bitmap(fromFile)
                End Using
            End If
        Catch
        End Try

        Return DrawFallback(code)
    End Function

    Private Function FlagsDir() As String
        Return Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "flags")
    End Function

    Private Function DrawAuto() As Image
        Dim bmp As New Bitmap(FlagWidth, FlagHeight)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.Clear(Color.FromArgb(33, 102, 172))
            Using pen As New Pen(Color.White, 1.4F)
                Dim r As New Rectangle(8, 3, FlagWidth - 16, FlagHeight - 6)
                g.DrawEllipse(pen, r)
                g.DrawLine(pen, 8, FlagHeight \ 2, FlagWidth - 8, FlagHeight \ 2)
                g.DrawArc(pen, New Rectangle(FlagWidth \ 2 - 4, 3, 8, FlagHeight - 6), 90, 180)
                g.DrawArc(pen, New Rectangle(FlagWidth \ 2 - 4, 3, 8, FlagHeight - 6), 270, 180)
            End Using
        End Using
        Return bmp
    End Function

    Private Function DrawFallback(code As String) As Image
        Dim bmp As New Bitmap(FlagWidth, FlagHeight)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.Clear(Color.Gainsboro)
            g.DrawRectangle(Pens.Gray, 0, 0, FlagWidth - 1, FlagHeight - 1)
            Dim text As String = If(code, "?")
            If text.Length > 2 Then text = text.Substring(0, 2)
            Using f As New Font("Segoe UI", 8.0F, FontStyle.Bold, GraphicsUnit.Pixel)
                Using sf As New StringFormat()
                    sf.Alignment = StringAlignment.Center
                    sf.LineAlignment = StringAlignment.Center
                    g.DrawString(text.ToUpperInvariant(), f, Brushes.DimGray, New RectangleF(0, 0, FlagWidth, FlagHeight), sf)
                End Using
            End Using
        End Using
        Return bmp
    End Function

End Module
