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
    ''' <summary>The glyph of the pin button in a panel's corner - the side of the catalog glyph's 24-unit
    ''' square, so the push-pin itself inks about 8 x 14 px (the button itself is a
    ''' <see cref="MinTarget"/> wide slot, so the hit target stays at the floor).</summary>
    Public Const PinGlyphSize As Integer = 16
    ''' <summary>The visual size of the corner pin button (a small unobtrusive button).</summary>
    Public Const PinButtonSize As Integer = 20
    ''' <summary>How far past a panel the pointer still counts as "near it" - the reveal zone.</summary>
    Public Const HotZoneMargin As Integer = 48
    ''' <summary>Extra padding around the window-controls panel's buttons (188 §3.1): the free
    ''' background is the panel's drag grip, so it breathes wider than the toolbar rows do.</summary>
    Public Const WindowControlsExtraPadding As Integer = 4

    ' --- the window-controls panel's own footprint (188 §3.7) -----------------------------
    ' The logical size the panel can never go below: the padding, the pin slot, the three
    ' command buttons and the corner drag grip at the hit-target floor. The viewer's minimum
    ' tracking size is built from these, so the window can never be asked to hold less than
    ' the panel shows.

    Public Shared Function WindowControlsLogicalWidth() As Integer
        Dim button As Integer = Math.Max(ItemHeight, MinTarget)
        Dim pad As Integer = PanelPadding + WindowControlsExtraPadding
        Return 2 * pad + (MinTarget + Gap) + 4 * button + 3 * Gap
    End Function

    Public Shared Function WindowControlsLogicalHeight() As Integer
        ' Vertical breathing stays the standard padding: the panel stands beside the toolbar rows
        ' at the same top edge and shares their height (2026-10-12 owner revision). The extra
        ' padding is horizontal only - the drag frame along the row.
        Return Math.Max(ItemHeight, MinTarget) + 2 * PanelPadding
    End Function

    ''' <summary>The invisible resize band of the borderless window (188 §6, 2026-10-12 owner
    ''' revision): how far from an edge or a corner the pointer still grabs the frame. Logical px
    ''' at the window's DPI, and never below the frame Windows actually keeps.</summary>
    Public Const WindowResizeBand As Integer = 15
    Public Const FocusRingWidth As Integer = 2
    Public Const FocusRingInset As Integer = 2
    ''' <summary>The non-colour mark of a checked toggle (BUTTONS 2.0 section 3 rule 5; 243 T-9): a short bar along the
    ''' bottom edge, in the item's own ink, drawn INSIDE the item (it never changes a size). Its length is the glyph's
    ''' side, its thickness the focus ring's, and it stands clear of both focus contours (inset + ring width + 1 px).</summary>
    Public Const CheckedMarkLength As Integer = 16
    Public Const CheckedMarkThickness As Integer = 2
    Public Const CheckedMarkInset As Integer = 5
    ''' <summary>The room a menu button keeps at its end for its ▾ (125 UX-305), and a submenu row for its ▸.</summary>
    Public Const ChevronWidth As Integer = 14
    ''' <summary>The corner mark of an item that has a secondary action (the right click); it was 4 px (125 UX-306).</summary>
    Public Const SecondaryMarkSize As Integer = 6
    ''' <summary>How far a submenu overlaps its parent's edge, so the pointer can slide across.</summary>
    Public Const SubmenuOverlap As Integer = 4
    ''' <summary>The strip a scrolling menu paints its ▴ / ▾ cue in (125 UX-329).</summary>
    Public Const ScrollCueHeight As Integer = 14

    ' --- ratios (percent, never scaled) -------------------------------------------------
    Public Const PanelOpacityPercent As Integer = 86
    ''' <summary>The 220 tone flip: at a panel opacity strictly below this percent (the SET value,
    ''' never the windows' display floors) a panel's ink pair inverts - black becomes white, white
    ''' becomes black, the outline with it. High contrast never flips.</summary>
    Public Const InkInvertBelowPercent As Integer = 50

    ''' <summary>The 220 decision, pure: does a panel's ink invert at this set opacity percent? 49 and
    ''' lower flip; 50 is the first non-inverted value.</summary>
    Public Shared Function InvertsInk(setOpacityPercent As Integer) As Boolean
        Return setOpacityPercent < InkInvertBelowPercent
    End Function

    Public Const HairlinePercent As Integer = 12
    Public Const HoverPercent As Integer = 14
    Public Const PressedPercent As Integer = 26
    ''' <summary>The disabled ink: the foreground at half strength, about 3:1 against the panel on both a light
    ''' and a dark scheme (125 Q125-5; it was 38 %, near 2.7:1, and an empty recipient slot read poorly).</summary>
    Public Const DisabledPercent As Integer = 50
    ''' <summary>The corner pin's ink at rest: free, and pinned (125 UX-313h - it was 35 % / 50 %, about 2.4:1 on a light scheme).</summary>
    Public Const PinRestPercent As Integer = 55
    Public Const PinPinnedPercent As Integer = 75

    ' --- time ---------------------------------------------------------------------------
    Public Const HoldMilliseconds As Integer = 500
    Public Const TooltipWidth As Integer = 480
    Public Const FadeMilliseconds As Integer = 150
    Public Const PressEaseMilliseconds As Integer = 60
    ''' <summary>The shared tick while a panel is idle: the pointer poll that summons or hides it.</summary>
    Public Const IdleTickMilliseconds As Integer = 100
    ''' <summary>The shared tick while anything animates.</summary>
    Public Const AnimationTickMilliseconds As Integer = 16
    ''' <summary>How long an unpinned surface - a floating panel, the recipients overlay - stays up
    ''' after the pointer leaves it (117 Ф-1/Ф-2). A fixed number, on purpose NOT the video controls'
    ''' <c>VideoControlsHideDelaySec</c>: the panels stopped borrowing that setting.</summary>
    Public Const PanelHideDelayMilliseconds As Integer = 5000
    ''' <summary>How long a window must have shown no media before its panels are held up (125 UX-313m). Longer
    ''' than the gap between two files of different kinds (a picture hands over to a video, a video to a picture),
    ''' which is a few hundred milliseconds at most - the panels must not flash up on every such switch.</summary>
    Public Const EmptyWindowHoldDelayMilliseconds As Integer = 700
    ''' <summary>How long the status panel stays up after the LAST copy, move or delete when the viewer is set to
    ''' show file operations there (<c>RevealStatusOnFileOperation</c>). Each further operation restarts the
    ''' count and, while the panel is already up, shows nothing new - it just stays; operations still running keep it
    ''' up whatever the count says.</summary>
    Public Const FileOperationRevealMilliseconds As Integer = 3000

    ' --- the destructive family (the recipients overlay's delete row; one colour = "this destroys") ---
    Public Shared ReadOnly DestructiveRest As Color = Color.FromArgb(96, 32, 32)
    Public Shared ReadOnly DestructiveHover As Color = Color.FromArgb(140, 44, 44)
    Public Shared ReadOnly DestructivePressed As Color = Color.FromArgb(170, 50, 50)

End Class
#End If
