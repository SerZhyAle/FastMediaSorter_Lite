#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' The gates of spec 027 section 5 that the map, the renderer and the names answer to
''' (ICON-SET, ICON-RENDER, ICON-EXTERNAL). Net10 only: the x86 fallback keeps its
''' pre-contract glyphs under a dated registry exception, and IconMap/GlyphRenderer are
''' whole-file modern seams.
''' </summary>
Public Class IconographyContractTests

    Private Shared ReadOnly GlyphDir As String = Path.Combine(AppContext.BaseDirectory, "TestData", "glyphs")

    Private Shared Function VocabularyIds() As Dictionary(Of String, String)
        ' id -> the record line, read from the vendored artifact (never from the catalog).
        Dim result As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim idPattern As New Regex("""id"":\s*""([^""]+)""")
        For Each line As String In File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData", "icon-vocabulary", "vocabulary.jsonl"))
            Dim m As Match = idPattern.Match(line)
            If m.Success Then result(m.Groups(1).Value) = line
        Next
        Return result
    End Function

    <Fact>
    Public Sub Exchange_controls_use_the_catalog_exchange_meanings()
        Dim expected As New Dictionary(Of String, String) From {
            {"managerSettingsNav_exchange", "source.exchange-server"},
            {"managerSettingsNav_exchange-account", "action.connect-device"},
            {"res_DevicesAdd", "source.exchange-server"},
            {"xc_Connect", "action.connect-device"},
            {"btnClipboardSend", "action.send-clipboard"},
            {"btnClipboardGet", "action.receive-clipboard"},
            {"clipboardTraySend", "action.send-clipboard"},
            {"clipboardTrayGet", "action.receive-clipboard"},
            {"_btnAccAdd", "action.connect-device"},
            {"_btnXCreateInvitation", "content.invitation"},
            {"_rbGuest", "content.guest"}}
        For Each entry In expected
            Assert.Equal(entry.Value, IconMap.GetMeaning(entry.Key).GlyphId)
        Next
        Assert.True(File.Exists(Path.Combine(GlyphDir, "feature.server-relay.svg")))
    End Sub

    ' --- 5.1 the map ---------------------------------------------------------------

    <Fact>
    Public Sub Every_map_entry_is_a_vocabulary_meaning_or_names_its_pending_proposal()
        Dim ids = VocabularyIds()
        For Each meaning As IconMeaning In IconMap.All
            If meaning.IsVocabulary Then
                Assert.True(ids.ContainsKey(meaning.GlyphId), meaning.ControlName & " -> " & meaning.GlyphId & " is not in the vendored vocabulary")
                Assert.True(File.Exists(Path.Combine(GlyphDir, meaning.GlyphId & ".svg")), meaning.GlyphId & ".svg is not vendored")
            Else
                Assert.StartsWith("PROPOSAL-", meaning.Proposal)
                Assert.Equal("", meaning.GlyphId)
            End If
        Next
    End Sub

    <Fact>
    Public Sub The_rows_still_waiting_on_the_catalog_are_exactly_these_four()
        ' ICON-SET rule 5 / registry exception: a control whose meaning has no usable catalog
        ' picture keeps its own under a dated exception. The list is pinned by name so a row
        ' joins or leaves it on purpose, in the same edit as the exception that records it.
        ' Left in 2026-10-07: btn_ToVideo (action.convert-to-video arrived with ICON-SET 0.21 and a
        ' glyph file in 0.28). Still here: the random slideshow and the two filled shapes wait for
        ' the variants the catalog still owes; the password eye waits for the Share Manager to adopt
        ' action.reveal (its off form's file is owed too).
        Dim waiting = IconMap.All.Where(Function(m) Not m.IsVocabulary).Select(Function(m) m.ControlName).OrderBy(Function(n) n, StringComparer.Ordinal).ToArray()
        Assert.Equal(New String() {"btnRevealPassword", "btn_Random_Slideshow", "editor_Tool_Oval_Filled", "editor_Tool_Rectangle_Filled"}, waiting)
    End Sub

    <Fact>
    Public Sub The_meanings_the_2026_10_07_catalog_delivered_are_in_the_vendored_vocabulary()
        ' ICON-SET 0.28: action.convert-to-video and app.language carry a drawing of their own,
        ' view.table and feature.drawing exist as records. This product draws only the first (the
        ' animation-to-video button); the others have no control here, so only their records are
        ' asserted - the day one gets a control, it takes its row in IconMap.
        Dim ids = VocabularyIds()
        For Each id As String In New String() {"action.convert-to-video", "app.language", "view.table", "feature.drawing"}
            Assert.True(ids.ContainsKey(id), id & " is not in the vendored vocabulary")
        Next
        Assert.Equal("action.convert-to-video", IconMap.GetMeaning("btn_ToVideo").GlyphId)
        Assert.True(File.Exists(Path.Combine(GlyphDir, "action.convert-to-video.svg")))
    End Sub

    <Fact>
    Public Sub The_animation_to_video_button_is_worded_by_the_catalogs_canonical_name()
        ' ICON-SET rule 3 (specification 183): one name per meaning, in every surface. The key of the
        ' button's name is the record's Russian name, and its English and Ukrainian translations are
        ' the record's own words; the other ten languages are this product's until a proposal names them.
        Dim line As String = VocabularyIds()("action.convert-to-video")
        Dim names As System.Text.Json.JsonElement = System.Text.Json.JsonDocument.Parse(line).RootElement.GetProperty("name")
        Dim key As String = IconMap.GetMeaning("btn_ToVideo").LocalizationKey
        Assert.Equal(names.GetProperty("ru").GetString(), key)
        Dim translations As String() = Localization.All.Single(Function(kv) kv.Key = key).Value
        Assert.Equal(names.GetProperty("en").GetString(), translations(0))
        Assert.Equal(names.GetProperty("uk").GetString(), translations(1))
    End Sub

    <Fact>
    Public Sub The_brush_tool_is_not_drawn_as_the_edit_pencil()
        ' ICON-SET 0.28 item A: tool.brush is the Material brush, never the pencil that
        ' action.edit and action.rename share; the editor's brush button and the toolbar's edit
        ' button sit one window apart.
        Dim brush As String = Regex.Match(File.ReadAllText(Path.Combine(GlyphDir, "tool.brush.svg")), "\bd=""([^""]+)""").Groups(1).Value
        Dim pencil As String = Regex.Match(File.ReadAllText(Path.Combine(GlyphDir, "action.edit.svg")), "\bd=""([^""]+)""").Groups(1).Value
        Assert.NotEmpty(brush)
        Assert.NotEmpty(pencil)
        Assert.NotEqual(pencil, brush)
    End Sub

    <Fact>
    Public Sub The_pin_has_its_catalog_drawing_and_its_outlined_state_and_neither_is_the_pencil()
        ' The recipients table's header carries the pin and the edit pencil side by side (219): the catalog's
        ' own push-pin, filled while pinned and outlined (action.pin--off) while free.
        Dim ids = VocabularyIds()
        Assert.True(ids.ContainsKey("action.pin"), "action.pin is not in the vendored vocabulary")
        Dim pin As String = Path.Combine(GlyphDir, "action.pin.svg")
        Dim off As String = Path.Combine(GlyphDir, "action.pin--off.svg")
        Assert.True(File.Exists(pin), "action.pin.svg is not vendored")
        Assert.True(File.Exists(off), "action.pin--off.svg is not vendored")
        Dim d As Func(Of String, String) = Function(svgPath) Regex.Match(File.ReadAllText(svgPath), "\bd=""([^""]+)""").Groups(1).Value
        Dim pencil As String = d(Path.Combine(GlyphDir, "action.edit.svg"))
        Assert.NotEqual(pencil, d(pin))
        Assert.NotEqual(pencil, d(off))
        ' The state variant is an outline: its centre is empty where the filled pin's is inked.
        Using filled As Bitmap = GlyphRenderer.RenderSvg(File.ReadAllText(pin), 24, Color.Black)
            Using outlined As Bitmap = GlyphRenderer.RenderSvg(File.ReadAllText(off), 24, Color.Black)
                Assert.True(filled.GetPixel(12, 7).A > 128, "the filled pin's head is not painted")
                Assert.True(outlined.GetPixel(12, 7).A < 64, "the outlined pin is filled")
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub One_glyph_never_stands_for_two_meanings()
        ' ICON-SET rule 1: controls that share a glyph must share its name too (the two
        ' overflow buttons are one meaning on two rows).
        For Each group In IconMap.All.Where(Function(m) m.IsVocabulary).GroupBy(Function(m) m.GlyphId)
            Dim names = group.Select(Function(m) m.LocalizationKey).Distinct().ToList()
            Assert.True(names.Count = 1, group.Key & " is used for different names: " & String.Join(" | ", names))
        Next
    End Sub

    ' --- 5.3 the renderer -----------------------------------------------------------

    <Fact>
    Public Sub Every_vendored_glyph_keeps_its_ink_inside_the_live_area()
        ' ICON-RENDER item A: ink inside 1..23 of the 24-unit grid (half a unit of slack
        ' for curve extrema that GetBounds reports exactly).
        ' Measured AFTER the group transforms: action.rename draws its underline at y 23..25
        ' and relies on its <g transform> to lift it into the grid.
        For Each filePath As String In Directory.GetFiles(GlyphDir, "*.svg")
            Dim name As String = Path.GetFileName(filePath)
            ' A catalog drawing that departs from item A is the catalog's to fix, not the product's to redraw (rule 1
            ' of ICON-RENDER section 3: the vendored file is the catalog's). Named here, with the date it was found, so the
            ' list shrinks when the catalog does. 2026-10-11: action.paste (proposed, from the CyrFlip batch) inks at y 0..22.
            If name = "action.paste.svg" Then Continue For
            Dim shapes = GlyphRenderer.ReadShapes(File.ReadAllText(filePath))
            Try
                Assert.NotEmpty(shapes)
                For Each shape In shapes
                    Dim b As RectangleF = shape.InkBounds()
                    If b.Width = 0 AndAlso b.Height = 0 Then Continue For
                    Assert.True(b.Left >= 0.5F AndAlso b.Top >= 0.5F, name & " inks outside the live area: " & b.ToString())
                    Assert.True(b.Right <= 23.5F AndAlso b.Bottom <= 23.5F, name & " inks outside the live area: " & b.ToString())
                Next
            Finally
                For Each shape In shapes
                    shape.Dispose()
                Next
            End Try
        Next
    End Sub

    <Fact>
    Public Sub An_outline_glyph_is_drawn_hollow()
        ' tool.rectangle is fill="none" with a stroke. Filling it would draw the filled
        ' rectangle - a different tool, sitting one button away in the editor.
        Using bmp As Bitmap = GlyphRenderer.RenderSvg(File.ReadAllText(Path.Combine(GlyphDir, "tool.rectangle.svg")), 24, Color.Black)
            Assert.True(bmp.GetPixel(12, 12).A < 16, "the centre of the rectangle outline is painted")
            Assert.True(bmp.GetPixel(3, 12).A > 128, "the rectangle outline's edge is not painted")
        End Using
    End Sub

    <Fact>
    Public Sub A_group_transform_is_applied()
        ' Without the transform the underline of action.rename lies at y 23..25 and the
        ' 24 px bitmap loses it; with it, the underline sits inside rows 20..23.
        Using bmp As Bitmap = GlyphRenderer.RenderSvg(File.ReadAllText(Path.Combine(GlyphDir, "action.rename.svg")), 24, Color.Black)
            Dim bottomRowInk As Integer = 0
            For x As Integer = 0 To 23
                If bmp.GetPixel(x, 22).A > 128 Then bottomRowInk += 1
            Next
            Assert.True(bottomRowInk >= 12, "the rename underline is missing from row 22 (" & bottomRowInk & " px)")
        End Using
    End Sub

    <Fact>
    Public Sub The_renderer_never_emits_a_second_colour()
        ' ICON-RENDER rule 2: one colour, the control's; anti-aliasing varies alpha only.
        Dim ink As Color = Color.FromArgb(255, 30, 120, 200)
        For Each filePath As String In Directory.GetFiles(GlyphDir, "*.svg")
            Using bmp As Bitmap = GlyphRenderer.RenderSvg(File.ReadAllText(filePath), 24, ink)
                Dim inked As Integer = 0
                For y As Integer = 0 To bmp.Height - 1
                    For x As Integer = 0 To bmp.Width - 1
                        Dim px As Color = bmp.GetPixel(x, y)
                        If px.A < 64 Then Continue For
                        inked += 1
                        Assert.InRange(Math.Abs(CInt(px.R) - ink.R), 0, 3)
                        Assert.InRange(Math.Abs(CInt(px.G) - ink.G), 0, 3)
                        Assert.InRange(Math.Abs(CInt(px.B) - ink.B), 0, 3)
                    Next
                Next
                Assert.True(inked > 0, Path.GetFileName(filePath) & " renders no ink at all")
            End Using
        Next
    End Sub

    ' --- 5.4 names ------------------------------------------------------------------

    <Fact>
    Public Sub Every_viewer_name_is_a_registered_string_with_all_translations()
        Dim known As New Dictionary(Of String, String())(StringComparer.Ordinal)
        For Each kv In Localization.All
            known(kv.Key) = kv.Value
        Next
        For Each meaning As IconMeaning In IconMap.All.Where(Function(m) m.Surface = IconSurface.Viewer)
            Assert.True(known.ContainsKey(meaning.LocalizationKey), meaning.ControlName & ": name key not registered: " & meaning.LocalizationKey)
            Assert.DoesNotContain(known(meaning.LocalizationKey), Function(t) String.IsNullOrWhiteSpace(t))
        Next
    End Sub

    ' --- 5.5 right to left ----------------------------------------------------------

    <Fact>
    Public Sub No_transport_or_overflow_caption_uses_a_mirrored_arrow_character()
        ' ICON-RENDER rule 7: media transport is rtl "fixed". A caption written with
        ' < > « » ‹ › is bidi-mirrored under RightToLeft = Yes and would point the wrong
        ' way in Arabic and Urdu; the pictures are bitmaps and never mirror. Parentheses
        ' around the key hint are ordinary punctuation and mirror correctly as text.
        Dim mirroredArrows As Char() = {"<"c, ">"c, "«"c, "»"c, "‹"c, "›"c}
        Dim captions As New List(Of String) From {"предыдущий (PgUp)", "следующий (PgDn)", "удалить (del)"}
        captions.AddRange(IconMap.All.Where(Function(m) m.Surface = IconSurface.Viewer).Select(Function(m) m.LocalizationKey))
        Dim table As New Dictionary(Of String, String())(StringComparer.Ordinal)
        For Each kv In Localization.All
            table(kv.Key) = kv.Value
        Next
        For Each key As String In captions.Distinct()
            Assert.True(table.ContainsKey(key), "caption not registered: " & key)
            For Each text As String In New String() {key}.Concat(table(key))
                Assert.True(text.IndexOfAny(mirroredArrows) < 0, "mirrored arrow character in caption: " & text)
            Next
        Next
    End Sub

    ' --- 5.6 contrast ---------------------------------------------------------------

    <Fact>
    Public Sub The_glyph_colour_chooser_holds_three_to_one_on_every_background()
        ' ICON-RENDER rule 3 under the dynamic scheme: the chooser picks black or white by
        ' luminance, so the better of the two is never under 4.5 : 1. The channel sum it
        ' replaced failed this sweep on saturated greens.
        Dim worst As Double = Double.MaxValue
        For r As Integer = 0 To 255 Step 15
            For g As Integer = 0 To 255 Step 15
                For b As Integer = 0 To 255 Step 15
                    Dim bg As Color = Color.FromArgb(r, g, b)
                    worst = Math.Min(worst, ContrastRatio(bg, GetOppositeColor(bg)))
                Next
            Next
        Next
        Assert.True(worst >= 4.5, "worst contrast " & worst.ToString("0.00", CultureInfo.InvariantCulture))

        ' The case the spec measured: (0,255,77) got white at about 1.4 : 1.
        Dim green As Color = Color.FromArgb(0, 255, 77)
        Assert.Equal(Color.Black.ToArgb(), GetOppositeColor(green).ToArgb())
    End Sub

    <Fact>
    Public Sub The_fixed_schemes_hold_three_to_one()
        Assert.True(ContrastRatio(Color.Black, GetOppositeColor(Color.Black)) >= 3.0)
        Assert.True(ContrastRatio(Color.White, GetOppositeColor(Color.White)) >= 3.0)
        ' The Share Manager's state hues on its light surface (palette day tones).
        Assert.True(ContrastRatio(ColorTranslator.FromHtml("#2E7D32"), SystemColors.Control) >= 3.0)
        Assert.True(ContrastRatio(ColorTranslator.FromHtml("#D32F2F"), SystemColors.Control) >= 3.0)
    End Sub

    ' --- 5.7 site gate --------------------------------------------------------------

    <Fact>
    Public Sub No_published_page_or_readme_carries_an_emoji()
        ' ICON-SET rule 8 and PAGE-STYLE section 9: a page refers to a control by its
        ' vocabulary glyph, never by an emoji.
        Dim root As String = FindRepoRoot()
        Assert.NotNull(root)
        Dim files As New List(Of String)()
        files.AddRange(Directory.GetFiles(root, "*.html"))
        files.AddRange(Directory.GetFiles(root, "README*.md"))
        files.AddRange(Directory.GetFiles(Path.Combine(root, "docs"), "*.html"))
        Dim langDir As String = Path.Combine(root, "lang")
        If Directory.Exists(langDir) Then files.AddRange(Directory.GetFiles(langDir, "*.html", SearchOption.AllDirectories))
        Assert.NotEmpty(files)

        Dim offenders As New List(Of String)()
        For Each f As String In files
            Dim text As String = File.ReadAllText(f)
            Dim i As Integer = 0
            While i < text.Length
                Dim cp As Integer = Char.ConvertToUtf32(text, i)
                If IsEmoji(cp) Then
                    offenders.Add(Path.GetFileName(f) & ": U+" & cp.ToString("X4", CultureInfo.InvariantCulture))
                    Exit While
                End If
                i += If(Char.IsSurrogatePair(text, i), 2, 1)
            End While
        Next
        Assert.True(offenders.Count = 0, "emoji on a published page: " & String.Join(", ", offenders))
    End Sub

    <Fact>
    Public Sub Every_glyph_a_page_references_is_a_vendored_vocabulary_glyph()
        Dim root As String = FindRepoRoot()
        Dim ids = VocabularyIds()
        Dim reference As New Regex("assets/glyphs/([A-Za-z0-9.\-]+)\.svg")
        For Each f As String In Directory.GetFiles(root, "*.html").Concat(Directory.GetFiles(Path.Combine(root, "docs"), "*.html"))
            For Each m As Match In reference.Matches(File.ReadAllText(f))
                Dim id As String = m.Groups(1).Value
                Assert.True(ids.ContainsKey(id), Path.GetFileName(f) & " references " & id & ", which is no vocabulary id")
                Assert.True(File.Exists(Path.Combine(root, "assets", "glyphs", id & ".svg")), Path.GetFileName(f) & " references a glyph that is not vendored: " & id)
            Next
        Next
    End Sub

    <Fact>
    Public Sub Every_inline_glyph_on_a_page_is_a_verbatim_copy_of_the_vendored_glyph()
        ' An inline <svg data-glyph="id"> exists because an <img> cannot take currentColor;
        ' it must still draw the vendored file, so its path data and transforms are the file's.
        Dim root As String = FindRepoRoot()
        Dim ids = VocabularyIds()
        Dim inline As New Regex("<svg\b[^>]*\bdata-glyph=""([^""]+)""[^>]*>(.*?)</svg>", RegexOptions.Singleline)
        Dim geometry As New Regex("\b(?:d|transform)=""[^""]*""")
        For Each f As String In Directory.GetFiles(root, "*.html").Concat(Directory.GetFiles(Path.Combine(root, "docs"), "*.html"))
            For Each m As Match In inline.Matches(File.ReadAllText(f))
                Dim id As String = m.Groups(1).Value
                Dim where As String = Path.GetFileName(f) & " inline " & id
                Assert.True(ids.ContainsKey(id), where & " is no vocabulary id")
                Dim vendored As String = Path.Combine(root, "assets", "glyphs", id & ".svg")
                Assert.True(File.Exists(vendored), where & " is not vendored")
                Dim attributes = geometry.Matches(File.ReadAllText(vendored))
                Assert.NotEmpty(attributes)
                For Each attribute As Match In attributes
                    Assert.True(m.Groups(2).Value.Contains(attribute.Value, StringComparison.Ordinal), where & " lacks " & attribute.Value)
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub Root_page_draws_back_to_top_and_download_with_vocabulary_glyphs()
        ' Spec 044: the kit's text arrows gave way to nav.scroll-top and action.download.
        ' The theme switch keeps the kit's symbol until the vocabulary has a meaning for it.
        Dim page As String = File.ReadAllText(Path.Combine(FindRepoRoot(), "index.html"))
        Assert.Contains("data-glyph=""nav.scroll-top""", page)
        Assert.Contains("data-glyph=""action.download""", page)
        Assert.DoesNotContain("⤓", page)
        Assert.DoesNotContain(">↑<", page)
    End Sub

    ''' <summary>Pictographic code points: the emoji blocks proper plus the dingbat and
    ''' miscellaneous-symbol emoji. Typography (arrows, check marks, ◐) is not listed.</summary>
    Private Shared Function IsEmoji(cp As Integer) As Boolean
        If cp >= &H1F000 AndAlso cp <= &H1FAFF Then Return True
        If cp >= &H2600 AndAlso cp <= &H26FF Then Return True
        Return False
    End Function

    Private Shared Function FindRepoRoot() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Return Nothing
    End Function

End Class
#End If
