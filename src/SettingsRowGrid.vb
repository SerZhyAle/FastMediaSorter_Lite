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
''' two visual columns. Below the stacked threshold - or when the shared column would
''' leave the labels too little room - the editor moves under the hint instead; that is
''' the stacked fallback, and only fill-eligible editors (text) stretch there.
''' </summary>
Friend NotInheritable Class SettingsRowGridRules

    ''' <summary>Content width below which the stacked fallback takes over.</summary>
    Public Const StackedBelowWidth As Integer = 560

    ''' <summary>The label column never gets less room than this in the two-column grid.</summary>
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

    ''' <summary>Two-column applies above the stacked threshold and only while the labels
    ''' keep their floor after the shared value column has taken its share.</summary>
    Public Shared Function IsTwoColumn(contentWidth As Integer, valueColumnWidth As Integer) As Boolean
        If contentWidth < StackedBelowWidth Then Return False
        Return LabelColumnWidth(contentWidth, valueColumnWidth) >= MinLabelColumnWidth
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

    ''' <summary>Editor width in the stacked fallback: fill-eligible editors stretch across
    ''' the row's content, everything else (buttons, the checkbox, spinners) keeps its
    ''' text-fitted or declared width, clamped to what the row actually has.</summary>
    Public Shared Function StackedEditorWidth(declaredWidth As Integer, contentWidth As Integer, fillsColumn As Boolean) As Integer
        Dim available As Integer = Math.Max(1, contentWidth)
        If fillsColumn Then Return available
        Return Math.Max(1, Math.Min(declaredWidth, available))
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
