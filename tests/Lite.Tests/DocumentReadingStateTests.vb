Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

Public Class DocumentReadingStateTests
    <Theory>
    <InlineData(False, False)>
    <InlineData(False, True)>
    <InlineData(True, False)>
    <InlineData(True, True)>
    Public Sub Dispatch_keeps_format_opt_ins_independent_and_requires_the_epub_host(pdf As Boolean, epub As Boolean)
        Assert.Equal(If(pdf, DocumentContentRoute.PdfReader, DocumentContentRoute.PdfPreview), DocumentContentPolicy.SelectRoute(".PDF", pdf, epub, True))
        Assert.Equal(If(epub, DocumentContentRoute.EpubReader, DocumentContentRoute.EpubMetadata), DocumentContentPolicy.SelectRoute(".epub", pdf, epub, True))
        Assert.Equal(DocumentContentRoute.EpubMetadata, DocumentContentPolicy.SelectRoute(".epub", pdf, epub, False))
        Assert.Equal(DocumentContentRoute.Other, DocumentContentPolicy.SelectRoute(".jpg", pdf, epub, True))
    End Sub
    <Fact>
    Public Sub Identity_requires_version_and_provenance_but_tolerates_path_case()
        Dim first As New DocumentIdentity With {.Path = "C:\Book.pdf", .Length = 42, .LastWriteUtcTicks = 12}
        Assert.True(first.Matches(New DocumentIdentity With {.Path = "c:\book.pdf", .Length = 42, .LastWriteUtcTicks = 12}))
        Assert.False(first.Matches(New DocumentIdentity With {.Path = first.Path, .Length = 42, .LastWriteUtcTicks = 13}))
        Assert.False(first.Matches(New DocumentIdentity With {.Path = first.Path, .Length = 42, .LastWriteUtcTicks = 12, .Provenance = "archive"}))
        Assert.False(first.Matches(Nothing))
    End Sub

    <Fact>
    Public Sub Bookmarks_are_separate_bounded_and_normalized()
        Dim items As New List(Of DocumentBookmark) From {Nothing, New DocumentBookmark()}
        For index = 0 To DocumentReadingState.BookmarkLimit
            items.Add(New DocumentBookmark With {.Identity = New DocumentIdentity With {.Path = "book.pdf", .Length = 1},
                .Label = index.ToString(), .Position = New DocumentReadingPosition With {.Viewport = Double.NaN, .ZoomPercent = Integer.MaxValue, .PageNumber = -1}})
        Next
        Dim bounded = DocumentReadingState.NormalizeBookmarks(items)
        Assert.Equal(DocumentReadingState.BookmarkLimit, bounded.Count)
        Assert.Equal("1", bounded(0).Label)
        Assert.Equal(0.0, bounded(0).Position.Viewport)
        Assert.Equal(800, bounded(0).Position.ZoomPercent)
        Assert.Equal(1, bounded(0).Position.PageNumber)
    End Sub

    <Fact>
    Public Sub Literal_search_has_case_scope_and_result_bounds()
        Assert.Equal({0, 6}, DocumentReadingState.FindMatches("Book, book", "book", False))
        Assert.Equal({6}, DocumentReadingState.FindMatches("Book, book", "book", True))
        Assert.Equal({0}, DocumentReadingState.FindMatches("[x] [x]", "[x]", False, 1))
        Assert.Empty(DocumentReadingState.FindMatches("abc", "", False))
        Assert.Empty(DocumentReadingState.FindMatches("abc", "a", False, 0))
    End Sub

    <Fact>
    Public Sub Spread_pairs_cover_odd_even_and_reading_direction()
        Assert.Equal({1}, DocumentReadingState.SpreadPages(1, 8, True, False))
        Assert.Equal({2, 3}, DocumentReadingState.SpreadPages(3, 8, True, False))
        Assert.Equal({3, 2}, DocumentReadingState.SpreadPages(2, 8, True, True))
        Assert.Equal({7, 8}, DocumentReadingState.SpreadPages(8, 8, False, False))
        Assert.Equal({8}, DocumentReadingState.SpreadPages(8, 8, True, True))
    End Sub

#If Not NETFRAMEWORK Then
    <Fact>
    Public Sub Raster_targets_and_caches_share_the_configured_total_budget()
        For Each megabytes In {64, 256, 1024}
            For Each quality In {"automatic", "fast", "high"}
                Dim p As New ModernViewerPreferences With {.DocumentMemoryCacheMb = megabytes, .PdfRenderCacheMb = 512, .PdfRenderQuality = quality}
                Dim size = PdfReadingCanvas.RasterTarget(New Drawing.Size(Integer.MaxValue, Integer.MaxValue), p)
                Assert.InRange(size.Width, 1, PdfDocumentSource.MaxRenderDimension)
                Assert.InRange(size.Height, 1, PdfDocumentSource.MaxRenderDimension)
                Assert.True(CLng(size.Width) * size.Height * 4 <= CLng(megabytes) * 1024 * 1024 \ 10)
                Assert.True(PdfReadingCanvas.CacheBudget(p) <= CLng(megabytes) * 1024 * 1024 * 2 \ 5)
            Next
        Next
    End Sub

    <Fact>
    Public Sub Document_color_modes_change_only_the_rendered_copy()
        Using source As New Drawing.Bitmap(2, 1), result As New Drawing.Bitmap(2, 1)
            source.SetPixel(0, 0, Drawing.Color.Black)
            source.SetPixel(1, 0, Drawing.Color.White)
            Using painter = Drawing.Graphics.FromImage(result)
                PdfReadingCanvas.DrawPage(painter, source, New Drawing.Rectangle(0, 0, 2, 1), "night")
            End Using
            Assert.Equal(Drawing.Color.White.ToArgb(), result.GetPixel(0, 0).ToArgb())
            Assert.Equal(Drawing.Color.Black.ToArgb(), result.GetPixel(1, 0).ToArgb())
            Assert.Equal(Drawing.Color.Black.ToArgb(), source.GetPixel(0, 0).ToArgb())
        End Using
    End Sub

    <Fact>
    Public Sub Epub_full_width_is_distinct_from_bounded_character_width_and_floating_limits_are_safe()
        Dim p As New ModernViewerPreferences With {.EpubColumnWidth = 0, .EpubLineHeight = Double.NaN, .EpubParagraphSpacing = Double.PositiveInfinity, .DocumentTtsRate = Double.NegativeInfinity}
        p.Normalize()
        Assert.Equal(0, p.EpubColumnWidth)
        Assert.Equal(1.6, p.EpubLineHeight)
        Assert.Equal(0.5, p.EpubParagraphSpacing)
        Assert.Equal(1, p.DocumentTtsRate)
        p.EpubColumnWidth = 1
        p.Normalize()
        Assert.Equal(40, p.EpubColumnWidth)
        p.EpubColumnWidth = Integer.MaxValue
        p.Normalize()
        Assert.Equal(120, p.EpubColumnWidth)
    End Sub

    <Fact>
    Public Sub Document_appearance_overrides_only_explicit_fields_and_checks_version()
        Dim identity As New DocumentIdentity With {.Path = "book.pdf", .Length = 4, .LastWriteUtcTicks = 2}
        Dim p As New ModernViewerPreferences With {.PdfColorMode = "night"}
        p.OverrideDocumentAppearance(identity, "PdfLayout", "spread")
        p.OverrideDocumentAppearance(identity, "FullPdfReader", "True")
        Assert.Equal("spread", p.EffectiveDocumentPreferences(identity).PdfLayout)
        Assert.False(p.EffectiveDocumentPreferences(identity).FullPdfReader)
        p.PdfColorMode = "sepia"
        Assert.Equal("sepia", p.EffectiveDocumentPreferences(identity).PdfColorMode)
        identity = New DocumentIdentity With {.Path = "book.pdf", .Length = 5, .LastWriteUtcTicks = 2}
        Assert.Equal("single", p.EffectiveDocumentPreferences(identity).PdfLayout)
        Assert.Equal("[]", ModernViewerPreferences.FromJson(p.ExportJson()).DocumentAppearanceHistory)
    End Sub

    <Fact>
    Public Sub Verified_move_relocates_reading_state_but_a_different_destination_does_not()
        Dim path = IO.Path.Combine(IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Try
            IO.File.WriteAllText(path, "book")
            Dim info As New IO.FileInfo(path)
            Dim identity As New DocumentIdentity With {.Path = "before.pdf", .Length = info.Length, .LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks}
            Dim p As New ModernViewerPreferences()
            p.RecordDocumentPosition(identity, New DocumentReadingPosition With {.PageNumber = 12})
            p.AddDocumentBookmark(identity, New DocumentReadingPosition With {.PageNumber = 8}, "bookmark")
            p.RelocateDocumentState("before.pdf", path)
            Dim relocated As New DocumentIdentity With {.Path = path, .Length = info.Length, .LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks}
            Assert.Equal(12, p.RememberedDocumentPosition(relocated).PageNumber)
            Assert.Equal(path, Assert.Single(p.ReadDocumentBookmarks()).Identity.Path)
            p.RelocateDocumentState(path, "nonexistent.pdf")
            Assert.Equal(path, Assert.Single(p.ReadDocumentBookmarks()).Identity.Path)
        Finally
            IO.File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub Reader_opt_ins_are_independent_and_personal_state_is_scrubbed()
        Dim preferences As New ModernViewerPreferences()
        Assert.False(preferences.FullPdfReader)
        Assert.False(preferences.FullEpubReader)
        preferences.FullPdfReader = True
        Dim identity As New DocumentIdentity With {.Path = "C:\private.pdf", .Length = 4, .LastWriteUtcTicks = 2}
        preferences.RecordDocumentPosition(identity, New DocumentReadingPosition With {.PageNumber = 27, .Viewport = 0.3, .ZoomMode = "manual", .ZoomPercent = 175})
        preferences.AddDocumentBookmark(identity, New DocumentReadingPosition With {.PageNumber = 5}, "personal label")
        Dim copy = ModernViewerPreferences.FromJson(preferences.ExportJson())
        Assert.True(copy.FullPdfReader)
        Assert.False(copy.FullEpubReader)
        Assert.Equal("[]", copy.DocumentPageHistory)
        Assert.Equal("[]", copy.DocumentBookmarks)
        Assert.DoesNotContain("private.pdf", preferences.ExportJson())
        Dim personal = ModernViewerPreferences.FromJson(preferences.ExportJson(True))
        Assert.Equal(27, personal.RememberedDocumentPosition(identity).PageNumber)
        Assert.Equal(0.3, personal.RememberedDocumentPosition(identity).Viewport)
        Assert.Single(personal.ReadDocumentBookmarks())
        identity.Length += 1
        Assert.Equal(1, personal.RememberedDocumentPosition(identity).PageNumber)
    End Sub

    <Fact>
    Public Sub Appearance_reset_preserves_bookmarks_and_history_and_import_clamps()
        Dim preferences As New ModernViewerPreferences With {.PdfLayout = "spread", .PdfPrefetchPages = 999, .PdfRenderCacheMb = 999,
            .EpubLineHeight = 100, .EpubFontSize = -1, .DocumentMemoryCacheMb = -1, .DocumentDiskCacheMb = -1, .DocumentTtsRate = 100}
        Dim identity As New DocumentIdentity With {.Path = "book.pdf", .Length = 4}
        preferences.RecordDocumentPosition(identity, New DocumentReadingPosition With {.PageNumber = 10})
        preferences.AddDocumentBookmark(identity, New DocumentReadingPosition With {.PageNumber = 3}, "chapter")
        Dim copy = ModernViewerPreferences.FromJson(preferences.ExportJson(True))
        Assert.Equal(3, copy.PdfPrefetchPages)
        Assert.Equal(512, copy.PdfRenderCacheMb)
        Assert.Equal(3, copy.EpubLineHeight)
        Assert.Equal(10, copy.EpubFontSize)
        Assert.Equal(64, copy.DocumentMemoryCacheMb)
        Assert.Equal(0, copy.DocumentDiskCacheMb)
        Assert.Equal(2, copy.DocumentTtsRate)
        copy.ResetDocumentAppearance("pdf")
        Assert.Equal("single", copy.PdfLayout)
        Assert.Equal(10, copy.RememberedDocumentPosition(identity).PageNumber)
        Assert.Single(copy.ReadDocumentBookmarks())
    End Sub
#End If
End Class
