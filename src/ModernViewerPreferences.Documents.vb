#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Reflection
Imports System.Text.Json

Partial Public NotInheritable Class ModernViewerPreferences
    Public NotInheritable Class DocumentAppearanceEntry
        Public Property Identity As DocumentIdentity
        Public Property Values As New Dictionary(Of String, String)(StringComparer.Ordinal)
    End Class
    Private Shared ReadOnly appearanceNames As String() = {"PdfLayout", "PdfZoom", "PdfZoomPercent", "PdfColorMode", "PdfFirstPageAlone", "PdfSpreadDirection",
        "EpubLayout", "EpubTheme", "EpubFontFamily", "EpubFontSize", "EpubLineHeight", "EpubParagraphSpacing", "EpubHorizontalMargin", "EpubColumnWidth", "EpubAlignment", "EpubHyphenation", "EpubUseBookStyles", "EpubUseEmbeddedFonts"}
    Public Property FullPdfReader As Boolean
    Public Property FullEpubReader As Boolean
    Public Property RememberDocumentPosition As Boolean = True
    Public Property RememberDocumentAppearance As Boolean = True
    Public Property DocumentBoundaryNavigation As Boolean
    Public Property DocumentFullscreenChrome As String = "auto"
    Public Property DocumentTtsEnabled As Boolean
    Public Property DocumentTtsVoice As String = ""
    Public Property DocumentTtsRate As Double = 1
    Public Property PdfLayout As String = "single"
    Public Property PdfFirstPageAlone As Boolean = True
    Public Property PdfSpreadDirection As String = "document"
    Public Property PdfZoom As String = "fitPage"
    Public Property PdfZoomPercent As Integer = 100
    Public Property PdfColorMode As String = "original"
    Public Property PdfThumbnailPanel As Boolean
    Public Property PdfOutlinePanel As Boolean
    Public Property PdfPanelWidth As Integer = 240
    Public Property PdfSearchScope As String = "whole"
    Public Property PdfSearchCase As Boolean
    Public Property PdfRenderQuality As String = "automatic"
    Public Property PdfRenderCacheMb As Integer = 128
    Public Property PdfPrefetchPages As Integer = 1
    Public Const PdfScanOcrPolicy As String = "manual"
    Public Property EpubLayout As String = "continuous"
    Public Property EpubTheme As String = "system"
    Public Property EpubFontFamily As String = "book"
    Public Property EpubFontSize As Integer = 18
    Public Property EpubLineHeight As Double = 1.6
    Public Property EpubParagraphSpacing As Double = 0.5
    Public Property EpubHorizontalMargin As Integer = 16
    Public Property EpubColumnWidth As Integer = 80
    Public Property EpubAlignment As String = "book"
    Public Property EpubHyphenation As String = "book"
    Public Property EpubUseBookStyles As Boolean = True
    Public Property EpubUseEmbeddedFonts As Boolean = True
    Public Const EpubImages As String = "fit"
    Public Const EpubLinkColor As String = "theme"
    Public Property EpubTocDepth As Integer = 32
    Public Property EpubSearchScope As String = "whole"
    Public Property DocumentMemoryCacheMb As Integer = 256
    Public Property DocumentDiskCacheMb As Integer = 512
    Public Property DocumentBookmarks As String = "[]"
    Public Property DocumentAppearanceHistory As String = "[]"

    ' An explicit allow-list keeps unrelated preference keys out of this plumbing.
    Private Shared ReadOnly documentPreferenceNames As String() = (
        "FullPdfReader FullEpubReader RememberDocumentPosition RememberDocumentAppearance DocumentBoundaryNavigation DocumentFullscreenChrome " &
        "DocumentTtsEnabled DocumentTtsVoice DocumentTtsRate PdfLayout PdfFirstPageAlone PdfSpreadDirection PdfZoom PdfZoomPercent PdfColorMode " &
        "PdfThumbnailPanel PdfOutlinePanel PdfPanelWidth PdfSearchScope PdfSearchCase PdfRenderQuality PdfRenderCacheMb PdfPrefetchPages " &
        "EpubLayout EpubTheme EpubFontFamily EpubFontSize EpubLineHeight EpubParagraphSpacing EpubHorizontalMargin EpubColumnWidth EpubAlignment " &
        "EpubHyphenation EpubUseBookStyles EpubUseEmbeddedFonts EpubTocDepth EpubSearchScope DocumentMemoryCacheMb " &
        "DocumentDiskCacheMb DocumentBookmarks DocumentAppearanceHistory").Split(" "c)

    Private Sub LoadDocumentPreferences()
        For Each name In documentPreferenceNames
            Dim prop = GetType(ModernViewerPreferences).GetProperty(name)
            Dim fallback = Convert.ToString(prop.GetValue(Me), CultureInfo.InvariantCulture)
            Dim value = ReadString(name, fallback)
            Try
                If prop.PropertyType Is GetType(Boolean) Then
                    Dim parsed As Boolean
                    If value = "1" Then
                        prop.SetValue(Me, True)
                    ElseIf value = "0" Then
                        prop.SetValue(Me, False)
                    ElseIf Boolean.TryParse(value, parsed) Then
                        prop.SetValue(Me, parsed)
                    End If
                Else
                    prop.SetValue(Me, Convert.ChangeType(value, prop.PropertyType, CultureInfo.InvariantCulture))
                End If
            Catch ex As FormatException
            Catch ex As OverflowException
            End Try
        Next
        NormalizeDocumentPreferences()
    End Sub

    Private Sub SaveDocumentPreferences()
        NormalizeDocumentPreferences()
        For Each name In documentPreferenceNames
            Dim prop = GetType(ModernViewerPreferences).GetProperty(name)
            If prop.PropertyType Is GetType(Boolean) Then
                WriteBool(name, CBool(prop.GetValue(Me)))
            Else
                WriteString(name, Convert.ToString(prop.GetValue(Me), CultureInfo.InvariantCulture))
            End If
        Next
    End Sub

    Private Shared Function ChoiceValue(value As String, fallback As String, ParamArray choices As String()) As String
        Return Choice(value, fallback, choices)
    End Function

    Private Shared Function Bounded(value As Double, minimum As Double, maximum As Double, fallback As Double) As Double
        Return If(Double.IsNaN(value) OrElse Double.IsInfinity(value), fallback, Math.Max(minimum, Math.Min(maximum, value)))
    End Function

    Friend Sub NormalizeDocumentPreferences()
        PdfLayout = ChoiceValue(PdfLayout, "single", "single", "continuous", "spread")
        PdfSpreadDirection = ChoiceValue(PdfSpreadDirection, "document", "document", "ltr", "rtl")
        PdfZoom = ChoiceValue(PdfZoom, "fitPage", "fitPage", "fitWidth", "actual", "manual")
        PdfZoomPercent = Clamp(PdfZoomPercent, 25, 800)
        PdfColorMode = ChoiceValue(PdfColorMode, "original", "original", "night", "sepia")
        PdfPanelWidth = Clamp(PdfPanelWidth, 120, 640)
        PdfSearchScope = ChoiceValue(PdfSearchScope, "whole", "whole", "page")
        PdfRenderQuality = ChoiceValue(PdfRenderQuality, "automatic", "automatic", "fast", "high")
        PdfRenderCacheMb = Clamp(PdfRenderCacheMb, 32, 512)
        PdfPrefetchPages = Clamp(PdfPrefetchPages, 0, 3)
        DocumentMemoryCacheMb = Clamp(DocumentMemoryCacheMb, 64, 1024)
        DocumentDiskCacheMb = Clamp(DocumentDiskCacheMb, 0, 4096)
        DocumentFullscreenChrome = ChoiceValue(DocumentFullscreenChrome, "auto", "auto", "visible")
        DocumentTtsRate = Bounded(DocumentTtsRate, 0.5, 2, 1)
        DocumentTtsVoice = If(DocumentTtsVoice, "")
        If DocumentTtsVoice.Length > 256 Then DocumentTtsVoice = DocumentTtsVoice.Substring(0, 256)
        EpubLayout = ChoiceValue(EpubLayout, "continuous", "continuous", "paginated")
        EpubTheme = ChoiceValue(EpubTheme, "system", "system", "light", "dark", "sepia", "black")
        EpubFontFamily = If(String.IsNullOrWhiteSpace(EpubFontFamily), "book", EpubFontFamily)
        If EpubFontFamily.Length > 128 Then EpubFontFamily = "book"
        EpubFontSize = Clamp(EpubFontSize, 10, 48)
        EpubLineHeight = Bounded(EpubLineHeight, 1, 3, 1.6)
        EpubParagraphSpacing = Bounded(EpubParagraphSpacing, 0, 2, 0.5)
        EpubHorizontalMargin = Clamp(EpubHorizontalMargin, 0, 96)
        EpubColumnWidth = If(EpubColumnWidth = 0, 0, Clamp(EpubColumnWidth, 40, 120))
        EpubAlignment = ChoiceValue(EpubAlignment, "book", "book", "left", "justify")
        EpubHyphenation = ChoiceValue(EpubHyphenation, "book", "book", "on", "off")
        EpubTocDepth = Clamp(EpubTocDepth, 1, 32)
        EpubSearchScope = ChoiceValue(EpubSearchScope, "whole", "whole", "chapter")
        DocumentBookmarks = JsonSerializer.Serialize(ReadDocumentBookmarks())
        DocumentAppearanceHistory = JsonSerializer.Serialize(ReadDocumentAppearances())
    End Sub

    Private Function ReadDocumentAppearances() As List(Of DocumentAppearanceEntry)
        Try
            If DocumentAppearanceHistory Is Nothing OrElse DocumentAppearanceHistory.Length > 8 * 1024 * 1024 Then Return New List(Of DocumentAppearanceEntry)()
            Dim entries = JsonSerializer.Deserialize(Of List(Of DocumentAppearanceEntry))(NormalizeArrayJson(DocumentAppearanceHistory))
            If entries Is Nothing Then Return New List(Of DocumentAppearanceEntry)()
            entries.RemoveAll(Function(item) item Is Nothing OrElse item.Identity Is Nothing OrElse String.IsNullOrWhiteSpace(item.Identity.Path) OrElse item.Values Is Nothing)
            For Each item In entries
                For Each key In item.Values.Keys.ToArray()
                    If Not appearanceNames.Contains(key) OrElse item.Values(key) Is Nothing OrElse item.Values(key).Length > 256 Then item.Values.Remove(key)
                Next
            Next
            Return entries.Skip(Math.Max(0, entries.Count - DocumentReadingState.HistoryLimit)).ToList()
        Catch ex As JsonException
            Return New List(Of DocumentAppearanceEntry)()
        End Try
    End Function

    Public Function EffectiveDocumentPreferences(identity As DocumentIdentity) As ModernViewerPreferences
        Dim result = Snapshot()
        If Not RememberDocumentAppearance Then Return result
        Dim entry = ReadDocumentAppearances().LastOrDefault(Function(item) item.Identity.Matches(identity))
        If entry IsNot Nothing Then
            For Each item In entry.Values
                Dim prop = GetType(ModernViewerPreferences).GetProperty(item.Key)
                Try
                    prop.SetValue(result, Convert.ChangeType(item.Value, prop.PropertyType, CultureInfo.InvariantCulture))
                Catch ex As FormatException
                Catch ex As OverflowException
                End Try
            Next
        End If
        result.NormalizeDocumentPreferences()
        Return result
    End Function

    Public Sub OverrideDocumentAppearance(identity As DocumentIdentity, key As String, value As String)
        If identity Is Nothing OrElse Not appearanceNames.Contains(key) Then Return
        Dim entries = ReadDocumentAppearances()
        Dim entry = entries.LastOrDefault(Function(item) item.Identity.Matches(identity))
        If entry Is Nothing Then
            entry = New DocumentAppearanceEntry With {.Identity = identity}
        Else
            entries.Remove(entry)
        End If
        entry.Values(key) = value
        entries.Add(entry)
        If entries.Count > DocumentReadingState.HistoryLimit Then entries.RemoveRange(0, entries.Count - DocumentReadingState.HistoryLimit)
        DocumentAppearanceHistory = JsonSerializer.Serialize(entries)
    End Sub

    Public Sub ClearDocumentAppearance(identity As DocumentIdentity)
        Dim entries = ReadDocumentAppearances()
        entries.RemoveAll(Function(item) item.Identity.Matches(identity))
        DocumentAppearanceHistory = JsonSerializer.Serialize(entries)
    End Sub

    Public Function ReadDocumentBookmarks() As List(Of DocumentBookmark)
        Try
            If DocumentBookmarks Is Nothing OrElse DocumentBookmarks.Length > 8 * 1024 * 1024 Then Return New List(Of DocumentBookmark)()
            Return DocumentReadingState.NormalizeBookmarks(JsonSerializer.Deserialize(Of List(Of DocumentBookmark))(DocumentBookmarks))
        Catch ex As JsonException
            Return New List(Of DocumentBookmark)()
        End Try
    End Function

    Public Sub AddDocumentBookmark(identity As DocumentIdentity, position As DocumentReadingPosition, label As String)
        Dim entries = ReadDocumentBookmarks()
        entries.Add(New DocumentBookmark With {.Identity = identity, .Position = position, .Label = label})
        DocumentBookmarks = JsonSerializer.Serialize(DocumentReadingState.NormalizeBookmarks(entries))
    End Sub

    Public Function RememberedDocumentPosition(identity As DocumentIdentity) As DocumentReadingPosition
        If Not RememberDocumentPosition OrElse identity Is Nothing Then Return New DocumentReadingPosition()
        Dim entries = ReadDocumentPages()
        Dim item = entries.LastOrDefault(Function(entry) New DocumentIdentity With {.Path = entry.Path, .LastWriteUtcTicks = entry.LastWriteUtcTicks,
            .Length = entry.Length, .Provenance = entry.Provenance}.Matches(identity))
        If item Is Nothing Then Return New DocumentReadingPosition()
        Dim result = If(item.Position, New DocumentReadingPosition With {.PageNumber = item.PageNumber})
        result.Normalize()
        entries.Remove(item)
        entries.Add(item)
        DocumentPageHistory = JsonSerializer.Serialize(entries)
        Return result
    End Function

    Public Sub RecordDocumentPosition(identity As DocumentIdentity, position As DocumentReadingPosition)
        If Not RememberDocumentPosition OrElse identity Is Nothing OrElse position Is Nothing Then Return
        position.Normalize()
        Dim entries = ReadDocumentPages()
        entries.RemoveAll(Function(item) String.Equals(item.Path, identity.Path, StringComparison.OrdinalIgnoreCase) AndAlso item.Provenance = identity.Provenance)
        entries.Add(New DocumentPageEntry With {.Path = identity.Path, .LastWriteUtcTicks = identity.LastWriteUtcTicks, .Length = identity.Length,
            .PageNumber = position.PageNumber, .Position = position, .Provenance = identity.Provenance})
        If entries.Count > DocumentReadingState.HistoryLimit Then entries.RemoveRange(0, entries.Count - DocumentReadingState.HistoryLimit)
        DocumentPageHistory = JsonSerializer.Serialize(entries)
    End Sub

    Public Sub ResetDocumentAppearance(format As String)
        Dim defaults As New ModernViewerPreferences()
        For Each name In documentPreferenceNames.Where(Function(key) key.StartsWith(If(format = "pdf", "Pdf", "Epub"), StringComparison.Ordinal))
            Dim prop = GetType(ModernViewerPreferences).GetProperty(name)
            prop.SetValue(Me, prop.GetValue(defaults))
        Next
    End Sub

    ' Called only after the operation queue has verified success. A destination with
    ' a different version must never inherit another document's reading records.
    Public Sub RelocateDocumentState(sourcePath As String, destinationPath As String)
        Try
            Dim info As New IO.FileInfo(destinationPath)
            If Not info.Exists Then Return
            Dim pages = ReadDocumentPages()
            For Each entry In pages
                If String.Equals(entry.Path, sourcePath, StringComparison.OrdinalIgnoreCase) AndAlso entry.Length = info.Length AndAlso entry.LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks Then entry.Path = destinationPath
            Next
            Dim bookmarks = ReadDocumentBookmarks()
            For Each entry In bookmarks
                If String.Equals(entry.Identity.Path, sourcePath, StringComparison.OrdinalIgnoreCase) AndAlso entry.Identity.Length = info.Length AndAlso entry.Identity.LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks Then entry.Identity.Path = destinationPath
            Next
            DocumentPageHistory = JsonSerializer.Serialize(pages)
            DocumentBookmarks = JsonSerializer.Serialize(bookmarks)
            Dim appearances = ReadDocumentAppearances()
            For Each entry In appearances
                If String.Equals(entry.Identity.Path, sourcePath, StringComparison.OrdinalIgnoreCase) AndAlso entry.Identity.Length = info.Length AndAlso entry.Identity.LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks Then entry.Identity.Path = destinationPath
            Next
            DocumentAppearanceHistory = JsonSerializer.Serialize(appearances)
        Catch ex As IO.IOException
        Catch ex As UnauthorizedAccessException
        End Try
    End Sub
End Class
#End If
