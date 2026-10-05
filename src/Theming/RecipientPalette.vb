Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing

''' <summary>
''' One stable pastel fill per destination slot
''' (129_SPECIFICATION_RECIPIENT_IDENTITY_COLORS.md): the settings grid's folder cells and the
''' floating recipients overlay both ask this one module, so "the pink folder" is the same colour
''' on both surfaces and across sessions. The colour is the SLOT's, not the folder path's - a slot
''' the user re-points keeps the colour they memorized.
'''
''' Pure: a slot number and a theme bit in, a colour out. The blend factors are constants here,
''' tuned once; the acceptance gate is the contrast test (the theme's text ink against every fill
''' at 4.5:1 or better, in both themes), not taste. The module exposes fills only - the ink stays
''' the theme's text ink wherever a fill is painted.
'''
''' Shared: compiles on net48 and net10 unchanged (no theme references), so the multi-targeted
''' suite runs it on both runtimes. The callers decide what "dark" means for their surface (the
''' application theme on both modern surfaces); the legacy overlay has no theme and passes True.
''' </summary>
Friend NotInheritable Class RecipientPalette

    Private Sub New()
    End Sub

    ' The blend factors of 129 §3.1, as ChromeTheme.Blend(a, b, t) weighs them: t is how far the
    ' hue travels TOWARD the surface, so the resting row carries 0.40 of the hue on the dark theme
    ' and 0.30 on the light one. Hover carries 0.45 of the hue (129 §3.3): the row deepens the
    ' same blend instead of swapping its identity.
    Private Const RestTowardSurface_Dark As Single = 0.6F
    Private Const RestTowardSurface_Light As Single = 0.7F
    Private Const HoverTowardSurface As Single = 0.55F

    ' The surfaces the hues are blended toward: the dark panels' charcoal (the overlay's own
    ' backdrop, the theme's dark window surface) and the white of the light windows. Own values,
    ' because shared code cannot reach the net10-only palettes.
    Private Shared ReadOnly DarkSurface As Color = Color.FromArgb(32, 32, 32)
    Private Shared ReadOnly LightSurface As Color = Color.FromArgb(255, 255, 255)

    ' The eleven base hues, one per slot 0..10, dark-theme bases first, light-theme bases second.
    ' Slot order: 0 magenta, 1 rose, 2 coral, 3 amber, 4 lime, 5 green, 6 teal, 7 cyan, 8 azure,
    ' 9 indigo, 10 violet. Dark row: F06292 E57373 FF8A65 FFB74D AED581 81C784 4DB6AC 4DD0E1
    ' 64B5F6 7986CB BA68C8. Light row: AD1457 C62828 D84315 E65100 558B2F 2E7D32 00695C 00838F
    ' 1565C0 303F9F 6A1B9A. Fixed in code - the assignment survives restarts and rebinding.
    ' (No comments inside the initializers: they break the net10 compile. See the table above.)
    Private Shared ReadOnly DarkHues() As Color = {
        Color.FromArgb(240, 98, 146),
        Color.FromArgb(229, 115, 115),
        Color.FromArgb(255, 138, 101),
        Color.FromArgb(255, 183, 77),
        Color.FromArgb(174, 213, 129),
        Color.FromArgb(129, 199, 132),
        Color.FromArgb(77, 182, 172),
        Color.FromArgb(77, 208, 225),
        Color.FromArgb(100, 181, 246),
        Color.FromArgb(121, 134, 203),
        Color.FromArgb(186, 104, 200)}

    Private Shared ReadOnly LightHues() As Color = {
        Color.FromArgb(173, 20, 87),
        Color.FromArgb(198, 40, 40),
        Color.FromArgb(216, 67, 21),
        Color.FromArgb(230, 81, 0),
        Color.FromArgb(85, 139, 47),
        Color.FromArgb(46, 125, 50),
        Color.FromArgb(0, 105, 92),
        Color.FromArgb(0, 131, 143),
        Color.FromArgb(21, 101, 192),
        Color.FromArgb(48, 63, 159),
        Color.FromArgb(106, 27, 154)}

    ''' <summary>The resting fill of one slot: the slot's base hue blended toward its theme's
    ''' surface. Slots 0-10; anything else is the neutral row fill - the surface itself.</summary>
    Public Shared Function ColorFor(slot As Integer, dark As Boolean) As Color
        Return FillFor(slot, dark, If(dark, RestTowardSurface_Dark, RestTowardSurface_Light))
    End Function

    ''' <summary>The hover fill: the same blend deepened, never a different colour role
    ''' (129 §3.3). Out-of-range slots deepen the neutral fill the same way.</summary>
    Public Shared Function HoverColorFor(slot As Integer, dark As Boolean) As Color
        Return FillFor(slot, dark, HoverTowardSurface)
    End Function

    Private Shared Function FillFor(slot As Integer, dark As Boolean, towardSurface As Single) As Color
        Dim surface As Color = If(dark, DarkSurface, LightSurface)
        If slot < 0 OrElse slot > 10 Then Return surface
        Dim hues() As Color = If(dark, DarkHues, LightHues)
        Return Blend(hues(slot), surface, towardSurface)
    End Function

    ''' <summary>a + (b - a) * t per channel, rounded and clamped; the result is opaque. The same
    ''' math as ChromeTheme.Blend, spelled out here because shared code cannot reach that class.</summary>
    Private Shared Function Blend(a As Color, b As Color, t As Single) As Color
        If Single.IsNaN(t) Then t = 0.0F
        If t < 0.0F Then t = 0.0F
        If t > 1.0F Then t = 1.0F
        Return Color.FromArgb(255,
                              Channel(CInt(a.R) + (CInt(b.R) - CInt(a.R)) * t),
                              Channel(CInt(a.G) + (CInt(b.G) - CInt(a.G)) * t),
                              Channel(CInt(a.B) + (CInt(b.B) - CInt(a.B)) * t))
    End Function

    Private Shared Function Channel(value As Single) As Integer
        If value <= 0.0F Then Return 0
        If value >= 255.0F Then Return 255
        Return CInt(Math.Round(value, MidpointRounding.AwayFromZero))
    End Function

End Class
