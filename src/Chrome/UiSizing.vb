#If Not NETFRAMEWORK Then
Option Strict On

Imports Microsoft.Win32

''' <summary>The input profile a settings surface or command panel selects once, at opening, or through an
''' explicit user choice (UI-SIZING 1.1 section 2 rule 1). It never follows individual mouse or touch events.</summary>
Friend Enum UiInputProfile
    ''' <summary>Pointer and keyboard: the Windows desktop baseline.</summary>
    Pointer = 0
    ''' <summary>Touch: the larger ordinary targets.</summary>
    Touch = 1
End Enum

''' <summary>Ordinary or explicitly chosen compact density (UI-SIZING 1.1 section 2 rule 4).</summary>
Friend Enum UiDensity
    ''' <summary>The ordinary baseline: 32 for a pointer, 48 for touch.</summary>
    Ordinary = 0
    ''' <summary>Explicit compact mode: smaller visible geometry and a smaller target, on either profile.</summary>
    Compact = 1
End Enum

''' <summary>One resolved row of the size-token table, in LOGICAL pixels (96 dpi, factor 1.0). Immutable.
''' Take a row from <see cref="UiSizing.Tokens"/>; never build one by hand outside that table.</summary>
Friend NotInheritable Class UiSizeTokens

    Friend Sub New(target As Integer, paddingX As Integer, glyphTier As Integer, glyphGap As Integer,
                   buttonGap As Integer, groupGap As Integer, checkBox As Integer, markerTier As Integer)
        Me.Target = target
        Me.PaddingX = paddingX
        Me.GlyphTier = glyphTier
        Me.GlyphGap = glyphGap
        Me.ButtonGap = buttonGap
        Me.GroupGap = groupGap
        Me.CheckBox = checkBox
        Me.MarkerTier = markerTier
    End Sub

    ''' <summary>Single-line height and hit-target side (BUTTONS 2.0 section 3).</summary>
    Public ReadOnly Property Target As Integer
    ''' <summary>Horizontal padding per side.</summary>
    Public ReadOnly Property PaddingX As Integer
    ''' <summary>The caption-glyph tier (BOOLEAN-OPTION heading glyph tier 16 / 20).</summary>
    Public ReadOnly Property GlyphTier As Integer
    ''' <summary>Glyph-to-caption gap, also the box-to-caption gap of a boolean option.</summary>
    Public ReadOnly Property GlyphGap As Integer
    ''' <summary>Button and wrapped-row gap.</summary>
    Public ReadOnly Property ButtonGap As Integer
    ''' <summary>Semantic group gap.</summary>
    Public ReadOnly Property GroupGap As Integer
    ''' <summary>The visible side of a boolean option's box (BOOLEAN-OPTION 2.0 section 2: 20 pointer, 24 touch).</summary>
    Public ReadOnly Property CheckBox As Integer
    ''' <summary>The state-marker glyph tier of a collapsible group header (COLLAPSIBLE-GROUP 1.1 section 3).</summary>
    Public ReadOnly Property MarkerTier As Integer

End Class

''' <summary>
''' The ONE shared size-token resolver (UI-SIZING 1.1 sections 2 and 5; token table of BUTTONS 2.0 section 3;
''' BOOLEAN-OPTION 2.0 section 2; COLLAPSIBLE-GROUP 1.1 section 3). Ticket 243, slice T-3. Pure apart from
''' <see cref="SystemTextScale"/>: no WinForms, no registry access in the functions that compose sizes.
'''
''' What it does not do yet: nothing calls <see cref="SystemTextScale"/> or <see cref="GlyphPx"/> from a layout.
''' Wiring the stable profile, the text scale and the compact density into the windows is owed to the later
''' slices of the ticket; the first adopters only take their ordinary pointer values from this table, so the
''' default look does not move.
'''
''' Interface scale and text enlargement are independent (UI-SIZING section 2 rule 3): the interface factor
''' composes the whole geometry (targets, padding, gaps, glyphs, box, marker); the text factor grows the text
''' and layout in the caller and reaches only the glyphs here, moderately and with a recorded bound
''' (<see cref="GlyphGrowthLimit"/>). A target never takes the text factor.
'''
''' The class is a mirror of the rounding rules of <see cref="UiScale"/> on purpose and does not depend on it: the
''' Share Manager project links this file and cannot link <c>UiScale</c>.
''' </summary>
Friend NotInheritable Class UiSizing

    Private Sub New()
    End Sub

    ' --- the token table, logical px -----------------------------------------------------------
    ' Public constants (not only the Tokens rows) so that a Const elsewhere - ButtonGlyphs.GlyphLogicalSize,
    ' SettingsCheckBox.BoxSideLogical - takes its value from here. A test pins every such pair to Tokens.

    ''' <summary>Ordinary pointer: single-line height and hit target.</summary>
    Public Const PointerTarget As Integer = 32
    ''' <summary>Ordinary touch: single-line height and hit target.</summary>
    Public Const TouchTarget As Integer = 48

    ''' <summary>The smallest hit-target side of compact mode, either profile (logical px). PROVISIONAL: it is the
    ''' value the viewer chrome already protects (<c>ChromeMetrics.MinTarget</c>), declared so that a compact
    ''' surface has a number to measure and test - UI-SIZING section 2 rule 4 says the contract invents no
    ''' owner-approved compact floor, and the owner's answer is still open (ticket 243, question Q-2). Changing
    ''' it is one edit here plus the tests that pin it.</summary>
    Public Const CompactTargetFloor As Integer = 28

    Public Const PointerPaddingX As Integer = 12
    Public Const TouchPaddingX As Integer = 16
    Public Const CompactPaddingX As Integer = 8

    ''' <summary>Caption glyph tier beside a caption, ordinary pointer.</summary>
    Public Const PointerGlyphTier As Integer = 16
    ''' <summary>Caption glyph tier, ordinary touch.</summary>
    Public Const TouchGlyphTier As Integer = 20
    ''' <summary>Caption glyph tier, compact (either profile).</summary>
    Public Const CompactGlyphTier As Integer = 16

    ''' <summary>Glyph-to-caption gap, every profile and density.</summary>
    Public Const GlyphGap As Integer = 8
    ''' <summary>Button and wrapped-row gap, every profile and density.</summary>
    Public Const ButtonGap As Integer = 8
    ''' <summary>Semantic group gap, every profile and density.</summary>
    Public Const GroupGap As Integer = 16

    ''' <summary>Boolean-option box, ordinary pointer (at least 20).</summary>
    Public Const PointerCheckBox As Integer = 20
    ''' <summary>Boolean-option box, ordinary touch (at least 24).</summary>
    Public Const TouchCheckBox As Integer = 24
    ''' <summary>Boolean-option box, compact (either profile).</summary>
    Public Const CompactCheckBox As Integer = 16

    ''' <summary>Group-header state-marker tier, every profile and density.</summary>
    Public Const MarkerTier As Integer = 16

    ' --- composition bounds ----------------------------------------------------------------------

    ''' <summary>The recorded bound of text-only glyph growth (UI-SIZING section 2 rule 3): a glyph never exceeds
    ''' this multiple of its tier because of the text factor alone.</summary>
    Public Const GlyphGrowthLimit As Single = 1.5F

    ''' <summary>Text enlargement is bounded to 1.0 .. this (Windows' "Text size" slider reaches 225 %).</summary>
    Public Const MaxTextFactor As Single = 2.25F

    ''' <summary>Share of a text enlargement that reaches the glyphs: factor = 1 + this x (text factor - 1),
    ''' then capped by <see cref="GlyphGrowthLimit"/> (the cap is reached at a text factor of 2.0).</summary>
    Public Const GlyphFollowShare As Single = 0.5F

    ''' <summary>The smallest interface factor a TARGET is composed with. Below the smallest effective factor the
    ''' product produces (a 90 % interface choice times the 85 % "small" panel size = 0.765) it only guards
    ''' against a garbage factor, so a target can never shrink without bound or to nothing. PROVISIONAL like
    ''' <see cref="CompactTargetFloor"/>.</summary>
    Public Const MinTargetScale As Single = 0.75F

    ''' <summary>Same ceiling as <c>UiScale.Px</c>: a device size is never above this.</summary>
    Public Const MaxDevicePx As Integer = 100000

    Private Shared ReadOnly PointerOrdinaryTokens As New UiSizeTokens(
        PointerTarget, PointerPaddingX, PointerGlyphTier, GlyphGap, ButtonGap, GroupGap, PointerCheckBox, MarkerTier)
    Private Shared ReadOnly TouchOrdinaryTokens As New UiSizeTokens(
        TouchTarget, TouchPaddingX, TouchGlyphTier, GlyphGap, ButtonGap, GroupGap, TouchCheckBox, MarkerTier)
    Private Shared ReadOnly CompactTokens As New UiSizeTokens(
        CompactTargetFloor, CompactPaddingX, CompactGlyphTier, GlyphGap, ButtonGap, GroupGap, CompactCheckBox, MarkerTier)

    ''' <summary>The logical-pixel tokens of a profile and density. Compact is the same row for both profiles.
    ''' A value outside the enums resolves as the ordinary pointer row, never as something smaller.</summary>
    Public Shared Function Tokens(profile As UiInputProfile, density As UiDensity) As UiSizeTokens
        If density = UiDensity.Compact Then Return CompactTokens
        If profile = UiInputProfile.Touch Then Return TouchOrdinaryTokens
        Return PointerOrdinaryTokens
    End Function

    ''' <summary>A logical size in device pixels at an interface factor (1.0 = the logical size). Monotonic in
    ''' <paramref name="logical"/> and in the factor, equal inputs give equal outputs, a positive size is never
    ''' rounded down to nothing and never above <see cref="MaxDevicePx"/>; zero and negative sizes are 0. A factor
    ''' that is NaN, infinite, zero or negative is 1.0.</summary>
    Public Shared Function DevicePx(logical As Integer, interfaceFactor As Single) As Integer
        Return ToDevice(logical, InterfaceFactorOrOne(interfaceFactor))
    End Function

    ''' <summary>The hit-target side in device pixels. The target takes the interface factor only, never the text
    ''' factor (layout grows with the text in the caller, the target does not). The factor is first bounded below by
    ''' <see cref="MinTargetScale"/>, so the result is never zero, never unboundedly small, and never below
    ''' <c>DevicePx(<see cref="CompactTargetFloor"/>, factor)</c> for any factor the product offers. Since the
    ''' token itself is at least the floor, "at least the floor" holds for every profile and density.</summary>
    Public Shared Function TargetPx(profile As UiInputProfile, density As UiDensity, interfaceFactor As Single) As Integer
        Dim factor As Double = InterfaceFactorOrOne(interfaceFactor)
        If factor < MinTargetScale Then factor = MinTargetScale
        Return ToDevice(Tokens(profile, density).Target, factor)
    End Function

    ''' <summary>The caption-glyph side in device pixels: the tier at the interface factor, grown by
    ''' <see cref="GlyphGrowthFactor"/> of the text factor. With a text factor of 1.0 this equals
    ''' <c>DevicePx(tier, interfaceFactor)</c>; it never exceeds <see cref="GlyphGrowthLimit"/> times that.</summary>
    Public Shared Function GlyphPx(profile As UiInputProfile, density As UiDensity,
                                   interfaceFactor As Single, textFactor As Single) As Integer
        Dim scale As Double = InterfaceFactorOrOne(interfaceFactor) * CDbl(GlyphGrowthFactor(textFactor))
        Return ToDevice(Tokens(profile, density).GlyphTier, scale)
    End Function

    ''' <summary>A text factor as it is used: NaN, infinite and below 1.0 are 1.0 (a smaller text setting never
    ''' shrinks the glyphs further), above <see cref="MaxTextFactor"/> is the maximum.</summary>
    Public Shared Function NormalizeTextFactor(textFactor As Single) As Single
        If Single.IsNaN(textFactor) OrElse Single.IsInfinity(textFactor) OrElse textFactor < 1.0F Then Return 1.0F
        If textFactor > MaxTextFactor Then Return MaxTextFactor
        Return textFactor
    End Function

    ''' <summary>The multiplier a text enlargement puts on glyphs: 1 + <see cref="GlyphFollowShare"/> x (t - 1) for
    ''' the normalized text factor t, never above <see cref="GlyphGrowthLimit"/> (1.0 .. 1.5).</summary>
    Public Shared Function GlyphGrowthFactor(textFactor As Single) As Single
        Dim grown As Double = 1.0R + CDbl(GlyphFollowShare) * (CDbl(NormalizeTextFactor(textFactor)) - 1.0R)
        If grown > CDbl(GlyphGrowthLimit) Then Return GlyphGrowthLimit
        Return CSng(grown)
    End Function

    Private Const TextScaleKeyPath As String = "Software\Microsoft\Accessibility"
    Private Const TextScaleValueName As String = "TextScaleFactor"

    ''' <summary>The Windows "Text size" accessibility setting as a factor, 1.0 .. <see cref="MaxTextFactor"/>.
    ''' Reads the DWORD <c>TextScaleFactor</c> (percent, 100..225) under <c>HKCU\Software\Microsoft\Accessibility</c>;
    ''' a missing key or value, another type or any failure is 1.0. Never throws. NOT wired into any layout yet.</summary>
    Public Shared Function SystemTextScale() As Single
        Try
            Using key As RegistryKey = Registry.CurrentUser.OpenSubKey(TextScaleKeyPath, False)
                If key Is Nothing Then Return 1.0F
                Dim raw As Object = key.GetValue(TextScaleValueName, Nothing)
                If TypeOf raw IsNot Integer Then Return 1.0F
                Return TextScaleFromPercent(CInt(raw))
            End Using
        Catch
            ' Any failure is "no enlargement": the registry may be absent, locked down or redirected.
            Return 1.0F
        End Try
    End Function

    ''' <summary>The pure half of <see cref="SystemTextScale"/>: a percent as a factor bounded to
    ''' 1.0 .. <see cref="MaxTextFactor"/>.</summary>
    Public Shared Function TextScaleFromPercent(percent As Integer) As Single
        Return NormalizeTextFactor(percent / 100.0F)
    End Function

    Private Shared Function InterfaceFactorOrOne(factor As Single) As Double
        If Single.IsNaN(factor) OrElse Single.IsInfinity(factor) OrElse factor <= 0.0F Then Return 1.0R
        Return CDbl(factor)
    End Function

    Private Shared Function ToDevice(logical As Integer, scale As Double) As Integer
        If logical <= 0 Then Return 0
        Dim scaled As Double = Math.Round(logical * scale, MidpointRounding.AwayFromZero)
        If scaled < 1.0R Then Return 1
        If scaled > MaxDevicePx Then Return MaxDevicePx
        Return CInt(scaled)
    End Function

End Class
#End If
