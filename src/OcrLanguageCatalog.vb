Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Text
Imports System.IO

''' <summary>
''' One selectable OCR/translation language with its display name and flag key.
'''
''' The name is the language's ENDONYM - what the language calls itself. That was a
''' pair of NameEn/NameRu fields until SPECIFICATION_THIRTEEN_UI_LANGUAGES.md §2.8:
''' with thirteen interface languages, naming 33 languages in each of them would be
''' 429 strings for a list where the endonym is the better answer anyway. A Greek user
''' recognises "Ελληνικά" in any interface; "Greek" and "Греческий" only help two.
''' </summary>
Public Class LanguageEntry
    Public ReadOnly Code As String       ' app/translate code, e.g. "en", "zh-TW", "auto"
    Public ReadOnly Name As String       ' endonym, e.g. "Deutsch", "العربية"
    Public Sub New(code As String, name As String)
        Me.Code = code
        Me.Name = name
    End Sub

    ''' <summary>
    ''' Text for the picker. Only "auto" is translated - it is a UI instruction, not a
    ''' language, so it has no endonym to fall back on.
    ''' </summary>
    Public Function DisplayName() As String
        If String.Equals(Code, "auto", StringComparison.OrdinalIgnoreCase) Then
            Return Localization.T("Автоопределение")
        End If
        Return Name
    End Function
End Class

''' <summary>
''' The full language list shown in the OCR + Translation pickers. "auto"
''' (auto-detect) is first and is the default for the OCR source language.
''' Flag PNGs live in &lt;exe&gt;\flags\&lt;code&gt;.png (see FlagImages).
''' </summary>
Public Module OcrLanguageCatalog

    ' Endonyms, in the historical order (the picker's order is a product decision, not
    ' an alphabet). "auto" carries its Russian source string and is translated on display.
    Private ReadOnly _all As LanguageEntry() = {
        New LanguageEntry("auto", "Автоопределение"),
        New LanguageEntry("en", "English"),
        New LanguageEntry("ru", "Русский"),
        New LanguageEntry("uk", "Українська"),
        New LanguageEntry("be", "Беларуская"),
        New LanguageEntry("de", "Deutsch"),
        New LanguageEntry("fr", "Français"),
        New LanguageEntry("es", "Español"),
        New LanguageEntry("it", "Italiano"),
        New LanguageEntry("pt", "Português"),
        New LanguageEntry("nl", "Nederlands"),
        New LanguageEntry("pl", "Polski"),
        New LanguageEntry("cs", "Čeština"),
        New LanguageEntry("sk", "Slovenčina"),
        New LanguageEntry("sv", "Svenska"),
        New LanguageEntry("no", "Norsk"),
        New LanguageEntry("da", "Dansk"),
        New LanguageEntry("fi", "Suomi"),
        New LanguageEntry("tr", "Türkçe"),
        New LanguageEntry("el", "Ελληνικά"),
        New LanguageEntry("bg", "Български"),
        New LanguageEntry("ro", "Română"),
        New LanguageEntry("hu", "Magyar"),
        New LanguageEntry("ja", "日本語"),
        New LanguageEntry("ko", "한국어"),
        New LanguageEntry("zh", "中文（简体）"),
        New LanguageEntry("zh-TW", "中文（繁體）"),
        New LanguageEntry("ar", "العربية"),
        New LanguageEntry("he", "עברית"),
        New LanguageEntry("hi", "हिन्दी"),
        New LanguageEntry("th", "ไทย"),
        New LanguageEntry("vi", "Tiếng Việt"),
        New LanguageEntry("id", "Bahasa Indonesia"),
        New LanguageEntry("fa", "فارسی")
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
        ' AppContext.BaseDirectory: same as the exe dir on net48 and still correct
        ' in the .NET 10 single-file publish (Assembly.Location is empty there).
        Return Path.Combine(AppContext.BaseDirectory, "flags")
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
