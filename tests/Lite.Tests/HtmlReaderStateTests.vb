#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO
Imports System.Text
Imports Xunit

' Specification 275 H15: the HTML reading-state store on a disposable file, and the HTML preference
' bounds, unknown values and the appearance reset. Modern leg only.
Public Class HtmlReaderStateTests
    Implements IDisposable

    Private ReadOnly _folder As String = Path.Combine(Path.GetTempPath(), "fms-html-state-" & Guid.NewGuid().ToString("N"))
    Private ReadOnly _file As String

    Public Sub New()
        Directory.CreateDirectory(_folder)
        _file = Path.Combine(_folder, "html-state-v1.json")
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Directory.Delete(_folder, True)
        Catch ex As IOException
        Catch ex As UnauthorizedAccessException
        End Try
    End Sub

    Private Shared ReadOnly Version As New HtmlSourceVersion With {.Length = 120, .LastWriteUtcTicks = 638000000000000000L, .Digest = New String("a"c, 64)}

    Private Shared Function Entry(identity As String) As HtmlReaderEntry
        Return New HtmlReaderEntry With {
            .Identity = identity, .Length = Version.Length, .LastWriteUtcTicks = Version.LastWriteUtcTicks, .Digest = Version.Digest,
            .PageKey = "chapter/one.html", .Anchor = "part-2", .ScrollRatio = 0.4,
            .Zoom = 150, .Theme = "sepia", .FontSize = 20}
    End Function

    Private Function Reloaded() As HtmlReaderState
        Dim state As New HtmlReaderState(_file)
        state.Load()
        Return state
    End Function

    <Fact>
    Public Sub Recorded_entry_survives_a_save_and_a_reload()
        Dim state As New HtmlReaderState(_file)
        state.Load()
        state.Record(Entry("C:\Docs\Page.html"), 500, True, True)
        state.Save()
        Assert.Equal("", state.Diagnostic)

        Dim again = Reloaded()
        Assert.False(again.IsRecoveryLocked)
        ' Identities compare ignoring case, like the other document histories.
        Dim found = again.Lookup("c:\docs\page.html", Version)
        Assert.NotNull(found)
        Assert.Equal("chapter/one.html", found.PageKey)
        Assert.Equal("part-2", found.Anchor)
        Assert.Equal(0.4, found.ScrollRatio, 6)
        Assert.Equal(150, found.Zoom)
        Assert.Equal("sepia", found.Theme)
        Assert.Equal(20, found.FontSize)
    End Sub

    <Fact>
    Public Sub A_changed_file_keeps_its_appearance_but_loses_its_place()
        Dim state As New HtmlReaderState(_file)
        state.Record(Entry("a.html"), 500, True, True)
        Dim changed As New HtmlSourceVersion With {.Length = Version.Length + 1, .LastWriteUtcTicks = Version.LastWriteUtcTicks, .Digest = Version.Digest}
        Dim found = state.Lookup("a.html", changed)
        Assert.False(found.HasPosition())
        Assert.Equal(150, found.Zoom)

        Dim otherDigest As New HtmlSourceVersion With {.Length = Version.Length, .LastWriteUtcTicks = Version.LastWriteUtcTicks, .Digest = New String("b"c, 64)}
        Assert.False(state.Lookup("a.html", otherDigest).HasPosition())
        Assert.True(state.Lookup("a.html", Version).HasPosition())
        Assert.Null(state.Lookup("unknown.html", Version))
    End Sub

    <Fact>
    Public Sub Switches_and_the_limit_decide_what_is_kept()
        Dim state As New HtmlReaderState(_file)
        state.Record(Entry("zero.html"), 0, True, True)
        Assert.Equal(0, state.Count)

        state.Record(Entry("no-place.html"), 500, False, True)
        Dim kept = state.Lookup("no-place.html", Version)
        Assert.False(kept.HasPosition())
        Assert.True(kept.HasAppearance())

        ' Neither switch on: nothing of the document stays, an older record included.
        state.Record(Entry("no-place.html"), 500, False, False)
        Assert.Null(state.Lookup("no-place.html", Version))
    End Sub

    <Fact>
    Public Sub Above_the_limit_the_least_recently_used_entries_go()
        Dim state As New HtmlReaderState(_file)
        state.Record(Entry("1.html"), 2, True, True)
        state.Record(Entry("2.html"), 2, True, True)
        state.Record(Entry("1.html"), 2, True, True)
        state.Record(Entry("3.html"), 2, True, True)
        Assert.Equal(2, state.Count)
        Assert.NotNull(state.Lookup("1.html", Version))
        Assert.Null(state.Lookup("2.html", Version))
        Assert.NotNull(state.Lookup("3.html", Version))
    End Sub

    <Fact>
    Public Sub Stored_values_are_clamped_and_unknown_values_mean_no_override()
        Dim state As New HtmlReaderState(_file)
        Dim wild = Entry("wild.html")
        wild.Zoom = 9999
        wild.FontSize = 2
        wild.ColumnWidth = 7
        wild.Margin = 5000
        wild.LineHeight = 9.0
        wild.ScrollRatio = 7
        wild.Theme = "neon"
        wild.Presentation = "READER"
        wild.FontFamily = "x;}body{display:none"
        state.Record(wild, 500, True, True)
        Dim found = state.Lookup("wild.html", Version)
        Assert.Equal(ModernViewerPreferences.HtmlZoomMax, found.Zoom)
        Assert.Equal(ModernViewerPreferences.HtmlFontSizeMin, found.FontSize)
        Assert.Equal(ModernViewerPreferences.HtmlColumnWidthMin, found.ColumnWidth)
        Assert.Equal(ModernViewerPreferences.HtmlMarginMax, found.Margin)
        Assert.Equal(3.0, found.LineHeight, 6)
        Assert.Equal(1.0, found.ScrollRatio, 6)
        Assert.Equal("", found.Theme)
        Assert.Equal("reader", found.Presentation)
        Assert.Equal("", found.FontFamily)
    End Sub

    <Fact>
    Public Sub Reset_appearance_keeps_places_and_drops_appearance_only_entries()
        Dim state As New HtmlReaderState(_file)
        state.Record(Entry("both.html"), 500, True, True)
        Dim looksOnly = Entry("looks.html")
        looksOnly.ClearPosition()
        state.Record(looksOnly, 500, True, True)

        state.ResetAllAppearance()
        Dim both = state.Lookup("both.html", Version)
        Assert.True(both.HasPosition())
        Assert.False(both.HasAppearance())
        Assert.Null(state.Lookup("looks.html", Version))

        state.Record(Entry("one.html"), 500, True, True)
        state.ResetAppearance("ONE.html")
        Assert.False(state.Lookup("one.html", Version).HasAppearance())
        Assert.True(state.Lookup("one.html", Version).HasPosition())
    End Sub

    <Theory>
    <InlineData("{not json")>
    <InlineData("[]")>
    <InlineData("{""schema"":2,""entries"":[]}")>
    <InlineData("{""schema"":""1"",""entries"":[]}")>
    <InlineData("{""schema"":1,""entries"":{}}")>
    Public Sub An_untrusted_file_is_kept_byte_for_byte_until_an_explicit_clear(content As String)
        File.WriteAllText(_file, content, New UTF8Encoding(False))
        Dim original = File.ReadAllBytes(_file)

        Dim state = Reloaded()
        Assert.True(state.IsRecoveryLocked)
        Assert.NotEqual("", state.Diagnostic)
        Assert.DoesNotContain(_folder, state.Diagnostic, StringComparison.OrdinalIgnoreCase)
        state.Record(Entry("a.html"), 500, True, True)
        state.ResetAllAppearance()
        state.Save()
        Assert.Equal(original, File.ReadAllBytes(_file))
        Assert.Null(state.Lookup("a.html", Version))

        state.Clear()
        Assert.False(state.IsRecoveryLocked)
        Dim cleared = Reloaded()
        Assert.False(cleared.IsRecoveryLocked)
        Assert.Equal(0, cleared.Count)
    End Sub

    <Fact>
    Public Sub An_oversized_file_is_not_read_and_not_overwritten()
        Using stream As New FileStream(_file, FileMode.CreateNew)
            stream.SetLength(HtmlReaderState.MaxFileBytes + 1L)
        End Using
        Dim state = Reloaded()
        Assert.True(state.IsRecoveryLocked)
        state.Record(Entry("a.html"), 500, True, True)
        state.Save()
        Assert.Equal(HtmlReaderState.MaxFileBytes + 1L, New FileInfo(_file).Length)
    End Sub

    <Fact>
    Public Sub One_bad_entry_is_dropped_and_the_others_stay()
        Dim json = "{""schema"":1,""entries"":[" &
            "{""identity"":""good.html"",""length"":120,""lastWriteUtcTicks"":638000000000000000,""pageKey"":""p.html"",""zoom"":120,""lastAccess"":5}," &
            "{""identity"":42,""pageKey"":""p.html""}," &
            "{""identity"":""typed.html"",""zoom"":""big""}," &
            "{""pageKey"":""no-identity.html""}," &
            "7]}"
        File.WriteAllText(_file, json, New UTF8Encoding(True))
        Dim state = Reloaded()
        Assert.False(state.IsRecoveryLocked)
        Assert.Equal(1, state.Count)
        Dim good = state.Lookup("good.html", New HtmlSourceVersion With {.Length = 120, .LastWriteUtcTicks = 638000000000000000L})
        Assert.Equal("p.html", good.PageKey)
        Assert.Equal(120, good.Zoom)
    End Sub

    <Fact>
    Public Sub Clearing_removes_records_and_writes_a_valid_empty_file()
        Dim state As New HtmlReaderState(_file)
        state.Record(Entry("a.html"), 500, True, True)
        state.Save()
        state.Clear()
        Assert.Equal(0, state.Count)
        Assert.True(File.Exists(_file))
        Assert.Equal(0, Reloaded().Count)
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"))
    End Sub

    ' ---- the HTML preferences ---------------------------------------------------------------------

    <Fact>
    Public Sub Html_viewing_and_online_pages_are_both_off_by_default()
        Dim defaults As New ModernViewerPreferences()
        Assert.False(defaults.HtmlViewerEnabled)
        Assert.False(defaults.HtmlOnlinePagesEnabled)
        Assert.Equal("original", defaults.HtmlPresentation)
        Assert.Equal("ask", defaults.HtmlExternalLinks)
    End Sub

    <Fact>
    Public Sub Html_preferences_clamp_and_fall_back_to_defaults_for_unknown_values()
        Dim p As New ModernViewerPreferences With {
            .HtmlPresentation = "fancy", .HtmlTheme = "neon", .HtmlFontFamily = "Arial; color:red",
            .HtmlFontSize = 500, .HtmlLineHeight = Double.NaN, .HtmlColumnWidth = 5, .HtmlMargin = -3,
            .HtmlZoom = 1, .HtmlExternalLinks = "always", .HtmlHistoryLimit = 999999, .HtmlSnapshotCacheMb = 1}
        p.NormalizeHtmlPreferences()
        Assert.Equal("original", p.HtmlPresentation)
        Assert.Equal("system", p.HtmlTheme)
        Assert.Equal("authored", p.HtmlFontFamily)
        Assert.Equal(ModernViewerPreferences.HtmlFontSizeMax, p.HtmlFontSize)
        Assert.Equal(1.6, p.HtmlLineHeight, 6)
        Assert.Equal(ModernViewerPreferences.HtmlColumnWidthMin, p.HtmlColumnWidth)
        Assert.Equal(0, p.HtmlMargin)
        Assert.Equal(ModernViewerPreferences.HtmlZoomMin, p.HtmlZoom)
        Assert.Equal("ask", p.HtmlExternalLinks)
        Assert.Equal(ModernViewerPreferences.HtmlHistoryLimitMax, p.HtmlHistoryLimit)
        Assert.Equal(ModernViewerPreferences.HtmlSnapshotCacheMbMin, p.HtmlSnapshotCacheMb)

        Dim whole As New ModernViewerPreferences With {.HtmlColumnWidth = 0, .HtmlFontFamily = "  Segoe UI  "}
        whole.NormalizeHtmlPreferences()
        Assert.Equal(0, whole.HtmlColumnWidth)
        Assert.Equal("Segoe UI", whole.HtmlFontFamily)
    End Sub

    <Fact>
    Public Sub Reset_appearance_leaves_the_switches_links_and_history_alone()
        Dim p As New ModernViewerPreferences With {
            .HtmlViewerEnabled = True, .HtmlOnlinePagesEnabled = True, .HtmlExternalLinks = "block", .HtmlHistoryLimit = 7,
            .HtmlRememberPosition = False, .HtmlPresentation = "reader", .HtmlTheme = "dark", .HtmlZoom = 300, .HtmlFontSize = 30,
            .HtmlWrapWideContent = True}
        p.ResetHtmlAppearance()
        Dim defaults As New ModernViewerPreferences()
        Assert.True(p.HtmlViewerEnabled)
        Assert.True(p.HtmlOnlinePagesEnabled)
        Assert.Equal("block", p.HtmlExternalLinks)
        Assert.Equal(7, p.HtmlHistoryLimit)
        Assert.False(p.HtmlRememberPosition)
        Assert.Equal(defaults.HtmlPresentation, p.HtmlPresentation)
        Assert.Equal(defaults.HtmlTheme, p.HtmlTheme)
        Assert.Equal(defaults.HtmlZoom, p.HtmlZoom)
        Assert.Equal(defaults.HtmlFontSize, p.HtmlFontSize)
        Assert.Equal(defaults.HtmlWrapWideContent, p.HtmlWrapWideContent)
    End Sub

    <Fact>
    Public Sub Every_stored_html_name_is_a_real_preference()
        For Each name In ModernViewerPreferences.HtmlPreferenceNames
            Assert.NotNull(GetType(ModernViewerPreferences).GetProperty(name))
        Next
        For Each name In ModernViewerPreferences.HtmlAppearanceNames
            Assert.Contains(name, ModernViewerPreferences.HtmlPreferenceNames)
        Next
    End Sub
End Class
#End If
