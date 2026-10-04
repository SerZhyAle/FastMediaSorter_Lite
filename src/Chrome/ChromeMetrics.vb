#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' The one place the floating chrome keeps a number (002 section 4, invariant 4). Every value is a
''' LOGICAL size - it reaches the screen only through <see cref="UiScale.Px"/> - or a ratio that
''' does not scale. Nothing else in the chrome carries a literal.
''' </summary>
Friend NotInheritable Class ChromeMetrics

    Private Sub New()
    End Sub

    ' --- sizes (logical px) -------------------------------------------------------------
    Public Const TransportHeight As Integer = 38
    Public Const TransportButtonWidth As Integer = 34
    Public Const TransportSliderHeight As Integer = 18
    Public Const TransportVolumeWidth As Integer = 80
    Public Const TransportTimeWidth As Integer = 96
    Public Const TransportGlyphSize As Integer = 20
    Public Const TransportSeekMinimum As Integer = 40
    Public Const TransportMinimumWidth As Integer = 200
    Public Const TransportMargin As Integer = 10
    Public Const TextSize As Integer = 12
    Public Const SymbolSize As Integer = 14
    Public Const PathWidth As Integer = 320
    Public Const PathMinWidth As Integer = 120
    Public Const MenuMinWidth As Integer = 120
    Public Const CompletionDelayMilliseconds As Integer = 400
    Public Const CompletionLimit As Integer = 200
    Public Const RowHeight As Integer = 32
    Public Const ItemHeight As Integer = 32
    Public Const GlyphSize As Integer = 16
    Public Const Gap As Integer = 4
    Public Const PanelPadding As Integer = 6
    Public Const PanelCorner As Integer = 6
    Public Const ItemCorner As Integer = 4
    ''' <summary>The hit-target floor of ICON-RENDER 0.15 rule 5: at least 28 logical px for a
    ''' control used with a mouse or a pen; no item is smaller than this in either dimension.
    ''' (Coarse input asks 44 px - a separate, undecided question for this viewer.)</summary>
    Public Const MinTarget As Integer = 28
    ''' <summary>Horizontal breathing room around a caption that is text, not a glyph.</summary>
    Public Const CaptionPadding As Integer = 8
    ''' <summary>The space between the media edge and a floating panel.</summary>
    Public Const ScreenMargin As Integer = 8
    ''' <summary>The gap between the window edge and the toolbar/status panels: none - they start flush
    ''' at the start edge (left; right in a right-to-left language) and the top (status: bottom) of the
    ''' media area, the way the docked toolbar did.</summary>
    Public Const PanelEdgeInset As Integer = 0
    ''' <summary>The glyph of the pin button in a panel's corner (the button itself is a
    ''' <see cref="MinTarget"/> wide slot, so the hit target stays at the floor).</summary>
    Public Const PinGlyphSize As Integer = 10
    ''' <summary>The visual size of the corner pin button (a small unobtrusive button).</summary>
    Public Const PinButtonSize As Integer = 20
    ''' <summary>How far past a panel the pointer still counts as "near it" - the reveal zone.</summary>
    Public Const HotZoneMargin As Integer = 48
    Public Const FocusRingWidth As Integer = 2
    Public Const FocusRingInset As Integer = 2

    ' --- ratios (percent, never scaled) -------------------------------------------------
    Public Const PanelOpacityPercent As Integer = 86
    Public Const HairlinePercent As Integer = 12
    Public Const HoverPercent As Integer = 14
    Public Const PressedPercent As Integer = 26
    Public Const DisabledPercent As Integer = 38

    ' --- time ---------------------------------------------------------------------------
    Public Const HoldMilliseconds As Integer = 500
    Public Const TooltipWidth As Integer = 480
    Public Const FadeMilliseconds As Integer = 150
    Public Const PressEaseMilliseconds As Integer = 60
    ''' <summary>The shared tick while a panel is idle: the pointer poll that summons or hides it.</summary>
    Public Const IdleTickMilliseconds As Integer = 100
    ''' <summary>The shared tick while anything animates.</summary>
    Public Const AnimationTickMilliseconds As Integer = 16

    ' --- the destructive family (the recipients overlay's delete row; one colour = "this destroys") ---
    Public Shared ReadOnly DestructiveRest As Color = Color.FromArgb(96, 32, 32)
    Public Shared ReadOnly DestructiveHover As Color = Color.FromArgb(140, 44, 44)
    Public Shared ReadOnly DestructivePressed As Color = Color.FromArgb(170, 50, 50)

End Class
#End If
