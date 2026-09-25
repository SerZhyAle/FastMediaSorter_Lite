#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

Partial Public Class Main_Form
    ''' <summary>Applies the catalog picture and its canonical localized UIA name in
    ''' one place. This is intentionally image-based: Unicode and MDL2 substitutes
    ''' have different shapes (and can mirror) on different Windows installations.</summary>
    Friend Sub ApplyCatalogToolbarGlyphs()
        RenderToolbarCatalogGlyphs()
        ApplyToolbarAccessibleNames()
    End Sub

    ''' <summary>
    ''' APP-BEHAVIOUR rule 9 (ticket 028 Ф-7): every toolbar button whose caption is not its
    ''' meaning - a glyph, an image, a two-letter stand-in - carries the words the overflow
    ''' menu already shows for it, so a screen reader and anything that drives the window
    ''' from outside read "Random slideshow", not "R&gt;". The words exist once
    ''' (OverflowText / IconMap), so the menu line and the name cannot drift apart. Called
    ''' from here, which both the first layout and a language change run.
    ''' </summary>
    Friend Sub ApplyToolbarAccessibleNames()
        If flow_Toolbar Is Nothing Then Return
        For Each c As Control In flow_Toolbar.Controls
            Dim button As Button = TryCast(c, Button)
            If button Is Nothing Then Continue For
            Dim words As String = OverflowText(button)
            ' A button whose caption IS its words needs nothing; one whose words are only
            ' its caption (a glyph with no entry) keeps the name its own code gave it.
            If String.IsNullOrEmpty(words) OrElse words = button.Text Then Continue For
            button.AccessibleName = words
        Next
    End Sub

    Friend Sub UpdateFullScreenGlyph()
        If btn_Full_Screen Is Nothing Then Return
        ApplyCatalogGlyph(btn_Full_Screen, 20, If(is_Full_Screen_Mode, "media.exit-fullscreen", "media.fullscreen"))
    End Sub

    Friend Sub ApplyCatalogGlyph(button As Button, tier As Integer, Optional forcedGlyphId As String = "")
        If button Is Nothing Then Return
        Dim meaning As IconMeaning = IconMap.GetMeaning(button.Name)
        Dim glyphId As String = If(String.IsNullOrEmpty(forcedGlyphId), If(meaning Is Nothing, "", meaning.GlyphId), forcedGlyphId)
        If glyphId.Length = 0 Then Return
        Dim oldImage As Image = button.Image
        button.Image = GlyphRenderer.Render(glyphId, tier, button.ForeColor, AddressOf RuntimeBootstrap.OpenBundledAsset)
        If oldImage IsNot Nothing Then oldImage.Dispose()
        button.BackgroundImage = Nothing
        button.ImageAlign = ContentAlignment.MiddleCenter
        If button Is btn_Prev_File OrElse button Is btn_Next_File OrElse button Is bt_Delete Then
            button.TextImageRelation = TextImageRelation.ImageBeforeText
        Else
            button.Text = ""
            button.TextImageRelation = TextImageRelation.Overlay
        End If
        If meaning IsNot Nothing Then button.AccessibleName = Localization.T(meaning.LocalizationKey)
    End Sub

    ''' <summary>Re-rasterise after a content-driven scheme changes; cache keys include
    ''' the colour, so this also keeps the old bitmap out of a newly bright surface.</summary>
    Friend Sub RecolorCatalogToolbarGlyphs()
        RenderToolbarCatalogGlyphs()
    End Sub

    ''' <summary>Renders every toolbar button whose picture comes from the catalog, in the
    ''' button's current colour. Walks the toolbar and asks IconMap rather than a fixed
    ''' list: the fixed list forgot btn_Edit, which stayed black on the black scheme
    ''' (audit B-05). The full-screen button's picture depends on the mode, so it has its
    ''' own call.</summary>
    Private Sub RenderToolbarCatalogGlyphs()
        If flow_Toolbar Is Nothing Then Return
        For Each c As Control In flow_Toolbar.Controls
            Dim button As Button = TryCast(c, Button)
            If button Is Nothing OrElse button Is btn_Full_Screen Then Continue For
            If IconMap.IsToolbarCatalogGlyph(button.Name) Then ApplyCatalogGlyph(button, 20)
        Next
        UpdateFullScreenGlyph()
    End Sub
End Class
#End If
