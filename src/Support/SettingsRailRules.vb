#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

''' <summary>The keys a page rail answers, with no <c>System.Windows.Forms.Keys</c> in the pure rules
''' (SETTINGS-NAVIGATION 2.0 section 2 rule 7). A window maps its own key codes onto these.</summary>
Friend Enum RailKey
    ''' <summary>Any other key: the rail does nothing with it.</summary>
    None = 0
    Up = 1
    Down = 2
    Home = 3
    [End] = 4
    Enter = 5
    Space = 6
End Enum

''' <summary>
''' The arithmetic of a settings page rail (SETTINGS-NAVIGATION 2.0 section 2 rules 5 and 7; ticket 243,
''' slice T-7a): row target, glyph and glyph-to-caption gap from the ordinary pointer size tokens, the rail
''' width measured from the real captions, and the focus movement of the keyboard. Pure: no WinForms, no
''' registry, no clock. The only dependency is <see cref="UiSizing"/>, which the Share Manager links too, so the
''' same file can be linked there without a change.
'''
''' Every pixel value is a DEVICE pixel at the interface factor that is passed in (1.0 = 96 dpi); the
''' logical values are the rows of <see cref="UiSizing.Tokens"/>, never retyped here.
''' </summary>
Friend NotInheritable Class SettingsRailRules

    Private Sub New()
    End Sub

    ''' <summary>The rail never takes more than this share of the client width (rule 5: "keep rail width based
    ''' on readable names, indentation and targets, without consuming a disproportionate share of content").
    ''' A caption that would need more wraps and grows its row instead (rule 2).</summary>
    Public Const MaxShareOfClient As Double = 0.3R

    ''' <summary>The floor of the rail width, in glyph widths of room for a caption: the rail is never narrower
    ''' than its own non-caption parts (padding, glyph, gap) plus this many glyph widths (96 logical px at the
    ''' 16 px pointer glyph - about a word like Images). It is a floor in the SAME unit as the other inputs, so
    ''' it grows with the interface factor and no separate pixel constant can drift from the scale. It guards a
    ''' failed or empty caption measurement; real captions are wider.</summary>
    Public Const FloorCaptionGlyphs As Integer = 6

    ''' <summary>Vertical room a row keeps around a wrapped caption, logical px (the 6 the rail always had).</summary>
    Public Const CaptionVerticalSlackLogical As Integer = 6

    Private Shared ReadOnly Ordinary As UiSizeTokens = UiSizing.Tokens(UiInputProfile.Pointer, UiDensity.Ordinary)

    ''' <summary>The row hit target (32 logical, pointer, ordinary) in device pixels. A row is never shorter.</summary>
    Public Shared Function RowTargetPx(factor As Single) As Integer
        Return UiSizing.TargetPx(UiInputProfile.Pointer, UiDensity.Ordinary, factor)
    End Function

    ''' <summary>The destination glyph side (16 logical) in device pixels.</summary>
    Public Shared Function GlyphPx(factor As Single) As Integer
        Return UiSizing.GlyphPx(UiInputProfile.Pointer, UiDensity.Ordinary, factor, 1.0F)
    End Function

    ''' <summary>The glyph-to-caption gap (8 logical) in device pixels.</summary>
    Public Shared Function GlyphGapPx(factor As Single) As Integer
        Return UiSizing.DevicePx(Ordinary.GlyphGap, factor)
    End Function

    ''' <summary>The height of a row whose caption needs <paramref name="captionHeightPx"/> pixels (the wrapped,
    ''' measured height): the target, or the caption plus its vertical slack when that is taller. Never below
    ''' <see cref="RowTargetPx"/>; a negative, zero or garbage caption height is the target.</summary>
    Public Shared Function RowHeightPx(factor As Single, captionHeightPx As Integer) As Integer
        Dim target As Integer = RowTargetPx(factor)
        If captionHeightPx <= 0 Then Return target
        Dim wanted As Long = CLng(captionHeightPx) + UiSizing.DevicePx(CaptionVerticalSlackLogical, factor)
        If wanted > UiSizing.MaxDevicePx Then wanted = UiSizing.MaxDevicePx
        Return Math.Max(target, CInt(wanted))
    End Function

    ''' <summary>The rail width in device pixels: the single-line width of the LONGEST caption plus the glyph, the gap
    ''' and <paramref name="paddingPx"/> (every other horizontal inset of the rail), raised to the floor
    ''' (<see cref="FloorCaptionGlyphs"/>) and limited to <see cref="MaxShareOfClient"/> of
    ''' <paramref name="clientWidthPx"/>. When the share limit is below the floor the FLOOR wins: a rail narrower
    ''' than a glyph and its padding is not a rail, and the share limit only stops growth above it. A client width
    ''' that is not a positive finite number is "unknown" and sets no share limit. Never negative, never above
    ''' <see cref="UiSizing.MaxDevicePx"/>; NaN, infinity and negative inputs count as 0.</summary>
    Public Shared Function RailWidth(longestCaptionPx As Double, glyphPx As Integer, gapPx As Integer,
                                     paddingPx As Integer, clientWidthPx As Double) As Integer
        Dim caption As Double = NonNegative(longestCaptionPx)
        Dim glyph As Double = Math.Max(0, glyphPx)
        Dim gap As Double = Math.Max(0, gapPx)
        Dim padding As Double = Math.Max(0, paddingPx)

        Dim floor As Double = padding + glyph + gap + FloorCaptionGlyphs * glyph
        Dim wanted As Double = Math.Max(caption + glyph + gap + padding, floor)

        If Not Double.IsNaN(clientWidthPx) AndAlso Not Double.IsInfinity(clientWidthPx) AndAlso clientWidthPx > 0.0R Then
            Dim cap As Double = Math.Floor(clientWidthPx * MaxShareOfClient)
            If wanted > cap Then wanted = Math.Max(cap, floor)
        End If

        If wanted > UiSizing.MaxDevicePx Then wanted = UiSizing.MaxDevicePx
        Return CInt(Math.Ceiling(wanted))
    End Function

    ''' <summary>Where the keyboard FOCUS goes from <paramref name="current"/> on a rail of <paramref name="count"/>
    ''' rows. Up and Down move by one, Home and End jump to the ends, and none of them wraps (a list has ends).
    ''' <c>-1</c> is "no row has focus": Down and Home go to the first row, Up to the first row, End to the last.
    ''' A <paramref name="current"/> past the end counts as the last row. Any other key (including the selection
    ''' keys) leaves the focus where it is. An empty rail answers -1. Only the focus moves here: choosing the page
    ''' is <see cref="IsSelectionKey"/>'s Enter or Space, so an arrow key never runs the work of a page switch.</summary>
    Public Shared Function NextIndex(current As Integer, count As Integer, key As RailKey) As Integer
        If count <= 0 Then Return -1
        Dim last As Integer = count - 1
        Dim at As Integer = If(current > last, last, current)
        Select Case key
            Case RailKey.Up
                Return If(at < 0, 0, Math.Max(0, at - 1))
            Case RailKey.Down
                Return If(at < 0, 0, Math.Min(last, at + 1))
            Case RailKey.Home
                Return 0
            Case RailKey.End
                Return last
            Case Else
                Return If(at < 0, -1, at)
        End Select
    End Function

    ''' <summary>True for the keys that choose the focused row's page: Enter and Space.</summary>
    Public Shared Function IsSelectionKey(key As RailKey) As Boolean
        Return key = RailKey.Enter OrElse key = RailKey.Space
    End Function

    Private Shared Function NonNegative(value As Double) As Double
        If Double.IsNaN(value) OrElse Double.IsInfinity(value) OrElse value < 0.0R Then Return 0.0R
        Return value
    End Function

End Class
#End If
