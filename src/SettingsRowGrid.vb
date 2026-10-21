#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

''' <summary>
''' Pure layout arithmetic of the settings window's two-column row grid
''' (SPECIFICATION 127 §3.1). No WinForms, no colours, no scale: every number is in
''' LOGICAL pixels at the caller's chosen unit, so the unit tests run the same math the
''' window scales through <c>Sp()</c>.
'''
''' A regular row is a label column (caption + hint, both wrapping) on the left and a
''' value column (the editor, flush to the row's right padding) on the right. The value
''' column's width is shared by every row of a page, so captions and editors line up in
''' two visual columns. In a narrow window the value column shrinks to preserve space
''' for the labels; editors always stay beside their captions and hints.
''' Boolean rows use a left checkbox followed by a full-width caption and hint.
''' </summary>
Friend NotInheritable Class SettingsRowGridRules

    ''' <summary>The preferred minimum label width; very narrow rows divide the available room.</summary>
    Public Const MinLabelColumnWidth As Integer = 220

    ''' <summary>Caption left inset, column gap and editor right inset.</summary>
    Public Const RowColumnInsets As Integer = 38

    ''' <summary>Clamp bounds of a page's shared value column.</summary>
    Public Const MinValueColumnWidth As Integer = 200
    Public Const MaxValueColumnWidth As Integer = 340

    ''' <summary>The page's shared value column: the widest editor on the page, clamped.</summary>
    Public Shared Function PageValueColumn(widestEditorWidth As Integer) As Integer
        Return ClampValueColumn(widestEditorWidth)
    End Function

    Public Shared Function ClampValueColumn(width As Integer) As Integer
        Return Math.Max(MinValueColumnWidth, Math.Min(MaxValueColumnWidth, width))
    End Function

    ''' <summary>Fit the shared value column beside the labels, including at high DPI.
    ''' Keep the label floor where possible, otherwise reserve half the usable row.</summary>
    Public Shared Function FitValueColumn(contentWidth As Integer, preferredWidth As Integer) As Integer
        Dim available As Integer = Math.Max(2, contentWidth - RowColumnInsets)
        Dim labelWidth As Integer = Math.Min(MinLabelColumnWidth, available \ 2)
        Return Math.Max(1, Math.Min(preferredWidth, available - labelWidth))
    End Function

    ''' <summary>The label column is everything the value column does not take.</summary>
    Public Shared Function LabelColumnWidth(contentWidth As Integer, valueColumnWidth As Integer) As Integer
        Return Math.Max(1, contentWidth - valueColumnWidth - RowColumnInsets)
    End Function

    ''' <summary>An editor inside the two-column value column: fill-eligible editors (text
    ''' endpoints, editable model fields) take exactly the shared column, fixed ones keep
    ''' their declared width unless the column is narrower.</summary>
    Public Shared Function EditorWidth(declaredWidth As Integer, valueColumnWidth As Integer, fillsColumn As Boolean) As Integer
        If fillsColumn Then Return valueColumnWidth
        Return Math.Min(declaredWidth, valueColumnWidth)
    End Function

    ''' <summary>A text-fitted command button: its text plus the padding of §3.2.</summary>
    Public Const ButtonHorizontalPadding As Integer = 16
    Public Const ButtonHeight As Integer = 32

    Public Shared Function CommandButtonWidth(textWidth As Integer) As Integer
        Return textWidth + 2 * ButtonHorizontalPadding
    End Function

    ''' <summary>The numeric editor of §3.2: 110 logical px, plus the muted unit suffix
    ''' that sits immediately to its left.</summary>
    Public Const NumericEditorWidth As Integer = 110
    Public Const NumericFontPixels As Integer = 16

    ''' <summary>The row caption: the ordinary control type role of the window (9 pt = 12 logical px at
    ''' 96 dpi, the form font). BOOLEAN-OPTION 2.0 section 2 rule 5 makes it the larger of the two roles.</summary>
    Public Const CaptionFontPoints As Single = 9.0F

    ''' <summary>The explanation under a caption: at least 12 logical px at the default scale
    ''' (BOOLEAN-OPTION 2.0 section 2 rule 5). The window scales it through its pixel font; it is
    ''' never clipped, ellipsized or shrunk to fit - the row grows instead.</summary>
    Public Const ExplanationFontPixels As Integer = 12
    Public Const SliderEditorWidth As Integer = 180
    Public Const SliderCompanionWidth As Integer = 64
    Public Const StripGap As Integer = 8

    Public Shared Function StripWidth(itemWidths As Integer()) As Integer
        Dim total As Integer = 0
        For Each item As Integer In itemWidths
            total += item
        Next
        If itemWidths.Length > 1 Then total += StripGap * (itemWidths.Length - 1)
        Return total
    End Function
End Class
#End If
