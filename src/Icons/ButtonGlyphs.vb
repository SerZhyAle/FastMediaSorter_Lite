#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms

''' <summary>What a command button's answer means, which decides its glyph hue (BUTTONS 2.0 section 3 rule 5;
''' WINDOWS-UI 0.4 section 11 item 4). Only <see cref="Affirmative"/> and <see cref="Caution"/> are coloured,
''' only in the glyph and only on the answer buttons of a dialog; everything else - Cancel, No and Close
''' included - is neutral, the mono look (item 5). No answer tints its face; the one face colour is the
''' danger face of <see cref="Irreversible"/>.</summary>
Public Enum ButtonGlyphRole
    ''' <summary>Browse, Refresh, Add, Open - the glyph in the caption's own colour.</summary>
    Neutral
    ''' <summary>OK, Yes, Apply, Save, Done - <c>state.ok</c>, the product's success pair.</summary>
    Affirmative
    ''' <summary>Cancel, No, Close without applying - the glyph in the caption's own colour, exactly as
    ''' <see cref="Neutral"/> (a refusal is not an error); the role stays so the answer is recognised by order,
    ''' default glyph and focus.</summary>
    Dismissing
    ''' <summary>Continue anyway, Skip, Overwrite a copy - <c>state.warning</c>.</summary>
    Caution
    ''' <summary>The acting button of a confirmation of an irreversible act: the <c>danger</c> face with
    ''' its own ink (APP-STYLE section 4), never a tint, and never the default focus.</summary>
    Irreversible
End Enum

''' <summary>
''' One glyph in front of the caption of a WinForms <see cref="Button"/> (BUTTONS 2.0 section 3; WINDOWS-UI 0.4
''' section 11 items 1, 2, 4, 5, 6; ICON-RENDER 0.19 section 14), from the same catalog renderer the toolbars
''' use - never a bitmap of its own drawing.
'''
''' What it owns, so that no dialog does:
''' <list type="bullet">
''' <item>the bitmap: 16 logical px at the button's current DPI, the catalog path in ONE colour, plus an
''' 8 logical px gap on the caption's side so the pair is one picture and the gap scales with DPI;</item>
''' <item>the hue: <c>state.ok</c> / <c>state.warning</c> are the palette's Success / Warning pair of the
''' theme in force, moved along the lightness axis only as far as the 3 : 1 against the face it is drawn on
''' needs; Cancel, No and Close carry no hue;</item>
''' <item>no tint: a non-destructive answer keeps the control's own face; the only face colour is the danger
''' face of an irreversible confirmation's acting button;</item>
''' <item>liveness: a theme change, a DPI change, the reading direction and Enabled re-render the picture,
''' so no value is resolved once at load;</item>
''' <item>the disabled state: no hue - the framework (light) or the theme's own disabled paint (dark)
''' dims the picture together with the caption (item 6);</item>
''' <item>high contrast: system colours for glyph and caption, no danger face.</item>
''' </list>
''' It never touches Escape, Enter, focus or button order (item 7).
''' </summary>
Public NotInheritable Class ButtonGlyphs

    Private Sub New()
    End Sub

    ''' <summary>The glyph's size, in logical (96-dpi) pixels - the 16 px tier beside a caption
    ''' (ICON-RENDER section 3 rule 5). A reference value awaiting measurement. It is the ordinary pointer tier of
    ''' the shared token table (<c>UiSizing.Tokens(Pointer, Ordinary).GlyphTier</c>); a Const cannot call the
    ''' resolver, so it takes the table's own constant, and a test pins the pair. The touch and compact tiers
    ''' are for the later slices that wire a profile into the buttons.</summary>
    Public Const GlyphLogicalSize As Integer = UiSizing.PointerGlyphTier

    ''' <summary>The gap between glyph and caption, in logical pixels (BUTTONS 2.0 section 3: 8 beside the 16 tier);
    ''' the <c>GlyphGap</c> of the shared token table (<c>UiSizing</c>).</summary>
    Public Const GapLogicalSize As Integer = UiSizing.GlyphGap

    ''' <summary>How much room a button may have beyond its glyph, gap and caption before the pair is moved from
    ''' the middle to the reading start (logical px, both sides together). A full-width command button
    ''' otherwise centres glyph and caption as one unit, so the glyphs of stacked buttons stand at a different
    ''' x each; at the start they form one column and the captions follow in a second one.</summary>
    Public Const WideSlackLogical As Integer = 64

    ''' <summary>The inset of the pair from the reading-start edge of a wide button (logical px).</summary>
    Public Const StartInsetLogical As Integer = 12

    ''' <summary>The contrast a meaningful glyph holds against its face (WINDOWS-UI section 6 item 4).</summary>
    Public Const GlyphContrast As Double = 3.0R

    ''' <summary>The face a light visual-style button is measured against: the darkest one a Windows
    ''' version draws for a normal push button (Windows 10 Aero is #E1E1E1, Windows 11 is lighter), so a
    ''' glyph that holds 3 : 1 on it holds on both.</summary>
    Private Shared ReadOnly LightVisualStyleFace As Color = Color.FromArgb(225, 225, 225)

    ''' <summary>The program's way into its bundled glyph files: the viewer's
    ''' <c>RuntimeBootstrap.OpenBundledAsset</c>, the Share Manager's <c>CompanionAssets.OpenBundledAsset</c>.
    ''' Set once at start-up; until it is set a button simply shows no glyph.</summary>
    Public Shared Property AssetOpener As Func(Of String, Stream)

    Private NotInheritable Class GlyphState
        Public GlyphId As String = ""
        Public Role As ButtonGlyphRole = ButtonGlyphRole.Neutral
        Public Hooked As Boolean
        Public Rendering As Boolean
        Public Carries As Boolean
        ''' <summary>The BackColor / ForeColor / flat settings the button had before the danger face.</summary>
        Public FaceSaved As Boolean
        Public SavedBack As Color
        Public SavedFore As Color
        Public SavedUseVisualStyle As Boolean
        Public SavedFlatStyle As FlatStyle
        Public SavedBorder As Color
        Public SavedBorderSize As Integer
        ''' <summary>The pair was moved to the reading start because the button is much wider than its content;
        ''' the alignment and padding it had are kept so a narrower button gets them back.</summary>
        Public ArrangedStart As Boolean
        Public OriginalTextAlign As ContentAlignment
        Public OriginalPadding As Padding
        ''' <summary>What the pair was last placed for: a layout pass that repeats the same width, caption and font
        ''' (a hidden section being shown, a table re-fitting its rows) costs a comparison and nothing else.</summary>
        Public ArrangedWidth As Integer = -1
        Public ArrangedText As String
        Public ArrangedFont As Font
        Public ArrangedRtl As Boolean
        Public MeasuredText As String
        Public MeasuredFont As Font
        Public MeasuredWidth As Integer
    End Class

    Private Shared ReadOnly states As New ConditionalWeakTable(Of Button, GlyphState)()
    Private Shared ReadOnly live As New List(Of WeakReference(Of Button))()
    Private Shared paletteInForce As ThemePalette = ThemePalette.Light

    ''' <summary>The palette in force. <c>ThemeService</c> hands it over with <see cref="UsePalette"/> every time
    ''' the theme is resolved; until then (a test, a tool) it is the light one. The module names no service,
    ''' so the dialog layout that asks it for a width links into the test project without the theme stack.</summary>
    Public Shared ReadOnly Property ActivePalette As ThemePalette
        Get
            Return paletteInForce
        End Get
    End Property

    ''' <summary>The theme changed: remember the palette and re-render every live button. Called by
    ''' <c>ThemeService</c> after it has re-themed the open windows, so each button's face is already final.</summary>
    Public Shared Sub UsePalette(newPalette As ThemePalette)
        If newPalette Is Nothing Then Return
        paletteInForce = newPalette
        RefreshAll()
    End Sub

    ' --- the public surface ---------------------------------------------------------------

    ''' <summary>Puts <paramref name="glyphId"/> before the caption of <paramref name="button"/>. Call it once
    ''' per button, after its caption is set; the rest (theme, DPI, direction, Enabled) follows by itself.</summary>
    Public Shared Sub Apply(button As Button, glyphId As String, Optional role As ButtonGlyphRole = ButtonGlyphRole.Neutral)
        If button Is Nothing OrElse button.IsDisposed Then Return
        If String.IsNullOrEmpty(glyphId) Then Return
        Dim st As GlyphState = states.GetValue(button, Function(b) New GlyphState())
        st.GlyphId = glyphId
        st.Role = role
        st.Carries = True
        If Not st.Hooked Then
            st.Hooked = True
            AddHandler button.HandleCreated, AddressOf OnRefreshEvent
            AddHandler button.DpiChangedAfterParent, AddressOf OnRefreshEvent
            AddHandler button.EnabledChanged, AddressOf OnRefreshEvent
            AddHandler button.RightToLeftChanged, AddressOf OnRefreshEvent
            AddHandler button.ParentChanged, AddressOf OnRefreshEvent
            AddHandler button.SystemColorsChanged, AddressOf OnRefreshEvent
            ' A neutral glyph takes the caption's colour, and a window that themes itself (the settings
            ' window) sets that colour after the application palette has been handed over.
            AddHandler button.ForeColorChanged, AddressOf OnRefreshEvent
            ' The pair's place depends on how much room the caption has: a resize, a new caption (a language
            ' switch) or a new font moves it, with no need to draw the picture again.
            AddHandler button.SizeChanged, AddressOf OnArrangeEvent
            AddHandler button.TextChanged, AddressOf OnArrangeEvent
            AddHandler button.FontChanged, AddressOf OnArrangeEvent
            AddHandler button.Disposed, AddressOf OnDisposed
            SyncLock live
                live.Add(New WeakReference(Of Button)(button))
            End SyncLock
        End If
        Refresh(button)
    End Sub

    ''' <summary>The glyph a bare answer carries when its verb has no meaning of its own: the check of a
    ''' choice for the affirmative, the cross for the dismissing answer (item 4).</summary>
    Public Shared Function DefaultGlyph(role As ButtonGlyphRole) As String
        Select Case role
            Case ButtonGlyphRole.Affirmative : Return "action.confirm"
            Case ButtonGlyphRole.Dismissing : Return "action.cancel"
            Case Else : Return ""
        End Select
    End Function

    ''' <summary>True when the button was given a glyph by this module.</summary>
    Public Shared Function CarriesGlyph(button As Button) As Boolean
        Dim st As GlyphState = Nothing
        Return button IsNot Nothing AndAlso states.TryGetValue(button, st) AndAlso st.Carries AndAlso button.Image IsNot Nothing
    End Function

    ''' <summary>True when the picture already holds its own gap to the caption, so a painter that adds one of its
    ''' own (the theme's disabled paint) must add none.</summary>
    Public Shared Function PictureHoldsGap(button As Button) As Boolean
        Return CarriesGlyph(button)
    End Function

    ''' <summary>The width a button needs for its caption with the glyph: what a fixed-size dialog row
    ''' (DialogLayout) must grow to so no caption is clipped (item 3).</summary>
    Public Shared Function ExtraWidth(button As Button) As Integer
        If Not CarriesGlyph(button) Then Return 0
        Return button.Image.Width
    End Function

    ''' <summary>The role currently set on a button, or Neutral.</summary>
    Public Shared Function RoleOf(button As Button) As ButtonGlyphRole
        Dim st As GlyphState = Nothing
        If button IsNot Nothing AndAlso states.TryGetValue(button, st) Then Return st.Role
        Return ButtonGlyphRole.Neutral
    End Function

    ''' <summary>The id of the glyph a button carries, or an empty string.</summary>
    Public Shared Function GlyphOf(button As Button) As String
        Dim st As GlyphState = Nothing
        If button IsNot Nothing AndAlso states.TryGetValue(button, st) Then Return st.GlyphId
        Return ""
    End Function

    ''' <summary>The colour the glyph of <paramref name="button"/> is drawn in right now - what the tests and the
    ''' acceptance numbers measure.</summary>
    Public Shared Function CurrentInk(button As Button) As Color
        Dim st As GlyphState = Nothing
        If button Is Nothing OrElse Not states.TryGetValue(button, st) Then Return Color.Empty
        Return InkOf(button, st, paletteInForce)
    End Function

    ''' <summary>The face the glyph of <paramref name="button"/> is measured against: the control's own face.</summary>
    Public Shared Function CurrentFace(button As Button) As Color
        Dim st As GlyphState = Nothing
        If button Is Nothing OrElse Not states.TryGetValue(button, st) Then Return Color.Empty
        Return FaceOf(button, st, paletteInForce)
    End Function

    ''' <summary>The hue (before the contrast adjustment) a role stands for in a palette; empty where the glyph
    ''' takes the caption's own colour (Neutral, Dismissing, Irreversible).</summary>
    Public Shared Function HueOf(role As ButtonGlyphRole, palette As ThemePalette) As Color
        Select Case role
            Case ButtonGlyphRole.Affirmative : Return palette.Success
            Case ButtonGlyphRole.Caution : Return palette.Warning
            Case Else : Return Color.Empty
        End Select
    End Function

    ''' <summary>The colour a hue takes on <paramref name="face"/>: the hue itself where it holds
    ''' <see cref="GlyphContrast"/>, otherwise the same hue moved along lightness only as far as that needs.</summary>
    Public Shared Function Readable(hue As Color, face As Color, Optional ratio As Double = GlyphContrast) As Color
        If AccentColors.Contrast(hue, face) >= ratio Then Return hue
        Dim towards As Color = If(AccentColors.Contrast(Color.Black, face) >= AccentColors.Contrast(Color.White, face), Color.Black, Color.White)
        For step1 As Integer = 1 To 100
            Dim amount As Double = step1 / 100.0R
            Dim candidate As Color = Color.FromArgb(
                CInt(Math.Round(hue.R + (CInt(towards.R) - CInt(hue.R)) * amount)),
                CInt(Math.Round(hue.G + (CInt(towards.G) - CInt(hue.G)) * amount)),
                CInt(Math.Round(hue.B + (CInt(towards.B) - CInt(hue.B)) * amount)))
            If AccentColors.Contrast(candidate, face) >= ratio Then Return candidate
        Next
        Return towards
    End Function

    ''' <summary>A colour laid over another at <paramref name="percent"/> (the danger face's hover and pressed shades).</summary>
    Public Shared Function Over(face As Color, hue As Color, percent As Integer) As Color
        Dim a As Double = percent / 100.0R
        Return Color.FromArgb(
            CInt(Math.Round(face.R * (1 - a) + hue.R * a)),
            CInt(Math.Round(face.G * (1 - a) + hue.G * a)),
            CInt(Math.Round(face.B * (1 - a) + hue.B * a)))
    End Function

    ' --- live refresh ----------------------------------------------------------------------

    Private Shared Sub RefreshAll()
        Dim snapshot As New List(Of Button)()
        SyncLock live
            For i As Integer = live.Count - 1 To 0 Step -1
                Dim b As Button = Nothing
                If live(i).TryGetTarget(b) AndAlso Not b.IsDisposed Then
                    snapshot.Add(b)
                Else
                    live.RemoveAt(i)
                End If
            Next
        End SyncLock
        For Each b As Button In snapshot
            Refresh(b)
        Next
    End Sub

    Private Shared Sub OnRefreshEvent(sender As Object, e As EventArgs)
        Dim b As Button = TryCast(sender, Button)
        If b IsNot Nothing Then Refresh(b)
    End Sub

    Private Shared Sub OnArrangeEvent(sender As Object, e As EventArgs)
        Dim b As Button = TryCast(sender, Button)
        Dim st As GlyphState = Nothing
        If b Is Nothing OrElse b.IsDisposed OrElse Not states.TryGetValue(b, st) Then Return
        If st.Rendering Then Return
        If b.Width = st.ArrangedWidth AndAlso String.Equals(b.Text, st.ArrangedText) AndAlso
           Object.ReferenceEquals(b.Font, st.ArrangedFont) AndAlso (b.RightToLeft = RightToLeft.Yes) = st.ArrangedRtl Then Return
        st.Rendering = True
        Try
            ArrangeContent(b, st)
        Finally
            st.Rendering = False
        End Try
    End Sub

    Private Shared Sub OnDisposed(sender As Object, e As EventArgs)
        Dim b As Button = TryCast(sender, Button)
        If b Is Nothing Then Return
        Dim picture As Image = b.Image
        b.Image = Nothing
        picture?.Dispose()
    End Sub

    ''' <summary>The one place a button's picture, face and ink are decided. Safe to call at any time.</summary>
    Private Shared Sub Refresh(b As Button)
        Dim st As GlyphState = Nothing
        If b Is Nothing OrElse b.IsDisposed OrElse Not states.TryGetValue(b, st) Then Return
        If st.Rendering Then Return
        Dim opener As Func(Of String, Stream) = AssetOpener
        If opener Is Nothing Then Return
        st.Rendering = True
        Try
            Dim theme As ThemePalette = paletteInForce
            ApplyFace(b, st, theme)

            Dim pixel As Integer = Math.Max(8, b.LogicalToDeviceUnits(GlyphLogicalSize))
            Dim gap As Integer = Math.Max(2, b.LogicalToDeviceUnits(GapLogicalSize))
            Dim rtl As Boolean = b.RightToLeft = RightToLeft.Yes
            Dim ink As Color = InkOf(b, st, theme)

            Dim composed As New Bitmap(pixel + gap, pixel)
            Try
                composed.SetResolution(96.0F, 96.0F)
                Using glyph As Bitmap = GlyphRenderer.Render(st.GlyphId, pixel, ink, opener)
                    Using g As Graphics = Graphics.FromImage(composed)
                        g.Clear(Color.Transparent)
                        ' The gap sits on the caption's side: right of the glyph in a left-to-right layout,
                        ' left of it in a right-to-left one, where the framework places the picture at the
                        ' reading start (the right edge) and the caption to its left.
                        g.DrawImageUnscaled(glyph, If(rtl, gap, 0), 0)
                    End Using
                End Using
            Catch
                composed.Dispose()
                Throw
            End Try

            Dim old As Image = b.Image
            b.Image = composed
            ArrangeContent(b, st)
            old?.Dispose()
            b.Invalidate()
        Catch ex As Exception
            ' A glyph that cannot be drawn must never stop a dialog from opening: the caption is complete without it.
            Diagnostics.Debug.WriteLine("ButtonGlyphs: " & st.GlyphId & ": " & ex.Message)
        Finally
            st.Rendering = False
        End Try
    End Sub

    ' --- where the pair sits -----------------------------------------------------------------

    Private Shared Function IsCentred(a As ContentAlignment) As Boolean
        Return a = ContentAlignment.TopCenter OrElse a = ContentAlignment.MiddleCenter OrElse a = ContentAlignment.BottomCenter
    End Function

    Private Shared Function StartOf(a As ContentAlignment) As ContentAlignment
        Select Case a
            Case ContentAlignment.TopCenter : Return ContentAlignment.TopLeft
            Case ContentAlignment.BottomCenter : Return ContentAlignment.BottomLeft
            Case Else : Return ContentAlignment.MiddleLeft
        End Select
    End Function

    ''' <summary>True when the button has far more room than glyph, gap and caption need - a command stretched
    ''' across a page - so centring the pair would leave the glyphs of a stack at different x.</summary>
    Private Shared Function HasRoomToSpare(b As Button, st As GlyphState) As Boolean
        If b.Image Is Nothing OrElse String.IsNullOrEmpty(b.Text) OrElse b.Width <= 0 Then Return False
        If Not String.Equals(st.MeasuredText, b.Text) OrElse Not Object.ReferenceEquals(st.MeasuredFont, b.Font) Then
            Dim flags As TextFormatFlags = TextFormatFlags.NoPadding Or TextFormatFlags.HidePrefix Or TextFormatFlags.SingleLine
            st.MeasuredWidth = TextRenderer.MeasureText(b.Text, b.Font, New Size(Short.MaxValue, Short.MaxValue), flags).Width
            st.MeasuredText = b.Text
            st.MeasuredFont = b.Font
        End If
        Dim textWidth As Integer = st.MeasuredWidth
        ' The padding the button had before this module moved the pair, or the answer would depend on it.
        Dim own As Padding = If(st.ArrangedStart, st.OriginalPadding, b.Padding)
        Dim used As Integer = b.Image.Width + textWidth + own.Horizontal
        Return b.Width - used > b.LogicalToDeviceUnits(WideSlackLogical)
    End Function

    ''' <summary>Puts glyph and caption together at their place: the caption's own alignment, with the glyph in
    ''' front of it; a centred pair that has far more room than it needs goes to the reading start with an
    ''' inset instead (left in a left-to-right layout, right in a right-to-left one, where the framework
    ''' mirrors the alignment itself). The caption keeps its alignment when the button is narrow again.</summary>
    Private Shared Sub ArrangeContent(b As Button, st As GlyphState)
        If b.Image Is Nothing Then Return
        Dim rtl As Boolean = b.RightToLeft = RightToLeft.Yes
        Dim own As ContentAlignment = If(st.ArrangedStart, st.OriginalTextAlign, b.TextAlign)

        If IsCentred(own) AndAlso HasRoomToSpare(b, st) Then
            If Not st.ArrangedStart Then
                st.ArrangedStart = True
                st.OriginalTextAlign = b.TextAlign
                st.OriginalPadding = b.Padding
            End If
            Dim start As ContentAlignment = StartOf(own)
            Dim inset As Integer = b.LogicalToDeviceUnits(StartInsetLogical)
            Dim p As Padding = st.OriginalPadding
            Dim target As Padding = If(rtl,
                New Padding(p.Left, p.Top, Math.Max(p.Right, inset), p.Bottom),
                New Padding(Math.Max(p.Left, inset), p.Top, p.Right, p.Bottom))
            If b.TextAlign <> start Then b.TextAlign = start
            If b.Padding <> target Then b.Padding = target
        ElseIf st.ArrangedStart Then
            st.ArrangedStart = False
            If b.TextAlign <> st.OriginalTextAlign Then b.TextAlign = st.OriginalTextAlign
            If b.Padding <> st.OriginalPadding Then b.Padding = st.OriginalPadding
        End If

        ' The picture takes the caption's own horizontal alignment, so a left-aligned list button keeps its
        ' caption at the start with the glyph in front of it, and a centred one centres the pair.
        Dim imageAlign As ContentAlignment
        Select Case b.TextAlign
            Case ContentAlignment.TopLeft, ContentAlignment.MiddleLeft, ContentAlignment.BottomLeft
                imageAlign = ContentAlignment.MiddleLeft
            Case ContentAlignment.TopRight, ContentAlignment.MiddleRight, ContentAlignment.BottomRight
                imageAlign = ContentAlignment.MiddleRight
            Case Else
                imageAlign = ContentAlignment.MiddleCenter
        End Select
        If b.ImageAlign <> imageAlign Then b.ImageAlign = imageAlign
        If b.TextImageRelation <> TextImageRelation.ImageBeforeText Then b.TextImageRelation = TextImageRelation.ImageBeforeText
        st.ArrangedWidth = b.Width
        st.ArrangedText = b.Text
        st.ArrangedFont = b.Font
        st.ArrangedRtl = rtl
    End Sub

    ''' <summary>Where the glyph-and-caption pair of a button sits horizontally, as the framework will place it:
    ''' <c>-1</c> at the physical left edge, <c>1</c> at the right, <c>0</c> centred. Read by the disabled
    ''' repaint of the dark theme so it draws the pair where the enabled button does.</summary>
    Public Shared Function PairSide(b As Button) As Integer
        Dim leftSet As Boolean = b.TextAlign = ContentAlignment.TopLeft OrElse b.TextAlign = ContentAlignment.MiddleLeft OrElse b.TextAlign = ContentAlignment.BottomLeft
        Dim rightSet As Boolean = b.TextAlign = ContentAlignment.TopRight OrElse b.TextAlign = ContentAlignment.MiddleRight OrElse b.TextAlign = ContentAlignment.BottomRight
        Dim rtl As Boolean = b.RightToLeft = RightToLeft.Yes
        If leftSet Then Return If(rtl, 1, -1)
        If rightSet Then Return If(rtl, -1, 1)
        Return 0
    End Function

    ' --- ink, face ---------------------------------------------------------------------------

    ''' <summary>The face a glyph is judged against: the button's own face colour (no answer tints it).</summary>
    Private Shared Function FaceOf(b As Button, st As GlyphState, palette As ThemePalette) As Color
        Dim face As Color
        If SystemInformation.HighContrast Then
            face = SystemColors.Control
        ElseIf st.Role = ButtonGlyphRole.Irreversible AndAlso st.FaceSaved Then
            face = b.BackColor
        ElseIf palette.IsDark Then
            face = If(b.FlatStyle = FlatStyle.Flat AndAlso Not b.UseVisualStyleBackColor, b.BackColor, palette.ControlFace)
        ElseIf b.FlatStyle = FlatStyle.Flat AndAlso Not b.UseVisualStyleBackColor Then
            face = b.BackColor
        Else
            face = LightVisualStyleFace
        End If
        Return face
    End Function

    ''' <summary>The colour of the glyph: the caption's colour for a neutral, dismissing or unavailable button, the
    ''' hue of the answer (held to 3 : 1 against the face) for an available affirmative or caution one, the danger
    ''' face's ink for the acting button of an irreversible confirmation.</summary>
    Private Shared Function InkOf(b As Button, st As GlyphState, palette As ThemePalette) As Color
        If SystemInformation.HighContrast Then
            Return If(b.Enabled, SystemColors.ControlText, SystemColors.GrayText)
        End If
        If Not b.Enabled Then
            ' No hue when unavailable; the framework / the theme's disabled paint mutes the picture.
            Return If(palette.IsDark, palette.TextPrimary, SystemColors.ControlText)
        End If
        If st.Role = ButtonGlyphRole.Irreversible AndAlso st.FaceSaved Then Return b.ForeColor
        Dim hue As Color = HueOf(st.Role, palette)
        If hue.IsEmpty Then
            ' The caption's colour as the theme will have it: a system colour a button still carries (ControlText on a
            ' visual-style button) is the palette's text role, so the glyph is never black on a charcoal face.
            Dim fore As Color = b.ForeColor
            Return If(fore.IsEmpty OrElse fore.A = 0, palette.TextPrimary, palette.Restate(fore, False))
        End If
        Return Readable(hue, FaceOf(b, st, palette))
    End Function

    ''' <summary>The danger face of an irreversible confirmation's acting button: the palette's danger colour
    ''' with the better of white and black as its ink - set once and restored when the answer stops being
    ''' irreversible or the window turns high-contrast. Every other role leaves the face alone.</summary>
    Private Shared Sub ApplyFace(b As Button, st As GlyphState, palette As ThemePalette)
        Dim wantsDanger As Boolean = st.Role = ButtonGlyphRole.Irreversible AndAlso b.Enabled AndAlso Not SystemInformation.HighContrast
        If wantsDanger Then
            If Not st.FaceSaved Then
                st.FaceSaved = True
                st.SavedBack = b.BackColor
                st.SavedFore = b.ForeColor
                st.SavedUseVisualStyle = b.UseVisualStyleBackColor
                st.SavedFlatStyle = b.FlatStyle
                st.SavedBorder = b.FlatAppearance.BorderColor
                st.SavedBorderSize = b.FlatAppearance.BorderSize
            End If
            Dim face As Color = palette.Danger
            b.FlatStyle = FlatStyle.Flat
            b.UseVisualStyleBackColor = False
            b.BackColor = face
            b.FlatAppearance.BorderColor = face
            b.FlatAppearance.BorderSize = 1
            b.FlatAppearance.MouseOverBackColor = Over(face, Color.Black, 8)
            b.FlatAppearance.MouseDownBackColor = Over(face, Color.Black, 16)
            b.ForeColor = If(AccentColors.Contrast(Color.White, face) >= AccentColors.Contrast(Color.Black, face), Color.White, Color.Black)
        ElseIf st.FaceSaved Then
            st.FaceSaved = False
            b.FlatStyle = st.SavedFlatStyle
            b.UseVisualStyleBackColor = st.SavedUseVisualStyle
            b.BackColor = st.SavedBack
            b.ForeColor = st.SavedFore
            b.FlatAppearance.BorderColor = st.SavedBorder
            b.FlatAppearance.BorderSize = st.SavedBorderSize
        End If
    End Sub
End Class
#End If
