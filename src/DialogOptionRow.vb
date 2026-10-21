#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The boolean option row of the viewer's dialog windows (ticket 243 T-11b, BOOLEAN-OPTION 2.0 sections 2 and 3): the
''' SAME anatomy the Settings window uses, built around a native <see cref="CheckBox"/> the dialog already owns. The
''' check box keeps its name, its tab place, its <c>Checked</c> default, its <c>CheckedChanged</c> handlers and its
''' persistence - what changes is how it looks and how it is hit. <see cref="SettingsCheckBox"/> paints the leading
''' box (20 logical px, a 32 px hit target), the caption becomes a label that toggles the box once, and the dialog's
''' own hint label (when it has one) becomes the explanation: wrapped under the caption at the caption inset and never
''' a toggle. The row is as tall as its content, so a long translation or a larger scale makes it taller and never
''' clips (section 2 rules 5 and 6); <see cref="DialogLayout.FitContent"/> pushes what is under a grown row down.
'''
''' The dialogs are hand-positioned in 96-dpi pixels, so a row is a small <see cref="OptionRowHost"/> panel placed at
''' an authored place with an authored (nominal) height; the real height is decided when the window has its scale.
''' Whole-file modern-only: the net48 fallback keeps its native check boxes.
''' </summary>
Friend NotInheritable Class DialogOptionRow

    Private Sub New()
    End Sub

    ''' <summary>The height of a one-line row with no explanation at 96 dpi: the 32 px hit target plus its 3 px lead and
    ''' 5 px tail (<see cref="SettingsCheckBox.ArrangeRow"/>). Dialogs stack rows with it.</summary>
    Friend Const RowHeightLogical As Integer = 40

    ''' <summary>The dialogs' own margin: the visible box stands where the native check box stood, so the 32 px hit
    ''' target reaches 6 px past it.</summary>
    Friend Const EdgeInsetLogical As Integer = 14

    ''' <summary>
    ''' Gives an existing check box (and its explanation label, when the dialog has one) the Settings treatment and
    ''' returns the row that now holds them. The caller adds the returned row to the dialog INSTEAD of the check box, at
    ''' the same place in the control list, so the tab order is the one it was.
    ''' </summary>
    ''' <param name="check">The dialog's check box. Its <c>Text</c> becomes the caption.</param>
    ''' <param name="explanation">The hint label under it, or Nothing. It is moved into the row; its text and name stay.</param>
    ''' <param name="boxLeft">Where the native check box's left edge was (96-dpi px, the dialog's coordinates).</param>
    ''' <param name="top">The row's top edge (96-dpi px).</param>
    ''' <param name="rightEdge">The row's reading-end edge: the dialog's client width, so the caption ends at the margin.</param>
    ''' <param name="height">The authored height at 96 dpi; <see cref="RowHeightLogical"/> for a one-line caption.</param>
    Friend Shared Function CreateAt(check As CheckBox, explanation As Label, boxLeft As Integer, top As Integer,
                                    rightEdge As Integer, Optional height As Integer = RowHeightLogical) As OptionRowHost
        Dim left As Integer = boxLeft - EdgeInsetLogical
        Return Create(check, explanation, New Rectangle(left, top, rightEdge - left, height), EdgeInsetLogical)
    End Function

    ''' <summary>The same with the host rectangle given outright (a hosted panel places its own rows).</summary>
    Friend Shared Function Create(check As CheckBox, explanation As Label, bounds As Rectangle, edgeInsetLogical As Integer) As OptionRowHost
        If check Is Nothing Then Throw New ArgumentNullException(NameOf(check))
        Dim host As New OptionRowHost(check, explanation, edgeInsetLogical)
        host.Bounds = bounds
        ApplyPalette(host, ThemePalette.Light)
        ' Not arranged here: the window is authored in 96-dpi pixels and FitContent measures every control at the authored
        ' height BEFORE it asks a row for its need, so a row grown now would leave the controls under it where they were.
        ' The first arrangement is FitContent's (DialogLayout.NeededHeight), once the window has its scale and direction.
        Return host
    End Function

    Friend Shared Function IsRow(c As Control) As Boolean
        Return TypeOf c Is OptionRowHost
    End Function

    ''' <summary>The height the row needs now, or 0 when what it has is enough. It arranges the row first, so
    ''' <see cref="DialogLayout"/> can push the controls under a grown row down by the difference.</summary>
    Friend Shared Function NeededHeight(c As Control) As Integer
        Dim host As OptionRowHost = TryCast(c, OptionRowHost)
        If host Is Nothing Then Return 0
        Dim before As Integer = host.Height
        host.Arrange()
        Return If(host.Height > before, host.Height, 0)
    End Function

    ''' <summary>The factor the row is laid out at. A window built from 96-dpi pixels is scaled by the framework after the
    ''' row exists, and the hit target is the one control whose size was authored (32 px), so what the framework did to
    ''' it IS the scale - in the product (AutoScaleMode.Dpi) and when a test scales the window by hand alike. The
    ''' window's own DPI is the floor, and a host that places its rows itself says its factor outright.</summary>
    Friend Shared Function ScaleOf(host As OptionRowHost) As Single
        If host.ScaleOverride > 0.0F Then Return host.ScaleOverride
        Dim fromDpi As Single = host.DeviceDpi / 96.0F
        Dim fromCheck As Single = host.Check.Width / CSng(UiSizing.PointerTarget)
        Return Math.Max(1.0F, Math.Max(fromDpi, fromCheck))
    End Function

    ''' <summary>The dialog palette path: the box, its ring and its tick from the active theme, in the roles the Settings
    ''' window gives them (surface, editor fill, muted border, text on hover, accent, ink on the accent). High contrast
    ''' uses the system colours. An accent that would not reach 3:1 on the surface (or whose tick would not on it) falls
    ''' back to the standard blue, as the Settings window does.</summary>
    Friend Shared Sub ApplyPalette(host As OptionRowHost, palette As ThemePalette)
        If host Is Nothing OrElse palette Is Nothing Then Return
        Dim surface As Color = host.BackColor
        If SystemInformation.HighContrast Then
            host.ApplyColors(surface, SystemColors.Window, SystemColors.GrayText, SystemColors.WindowText,
                             SystemColors.Highlight, SystemColors.HighlightText)
            Return
        End If
        Dim accent As Color = palette.Accent
        Dim tick As Color = palette.AccentInk
        If AccentColors.Contrast(accent, surface) < 3.0R OrElse AccentColors.Contrast(tick, accent) < 3.0R Then
            accent = Color.FromArgb(0, 120, 215)
            tick = Color.White
        End If
        host.ApplyColors(surface, palette.SurfaceRaised, palette.TextMuted, palette.TextPrimary, accent, tick)
    End Sub
End Class

''' <summary>One option row of a dialog: the leading box, the caption and the optional explanation, arranged by
''' <see cref="SettingsCheckBox.ArrangeRow"/> (see <see cref="DialogOptionRow"/>). The row mirrors itself under
''' <c>RightToLeft</c>, a property it only learns once it sits in the window, hence the handler.</summary>
Friend NotInheritable Class OptionRowHost
    Inherits Panel
    Implements IThemedOptionRow

    ''' <summary>The dialog's own check box - the one control that keeps its name, state and handlers.</summary>
    Friend ReadOnly Property Check As CheckBox Implements IThemedOptionRow.Check

    ''' <summary>The caption label; a click on it toggles <see cref="Check"/> exactly once.</summary>
    Friend ReadOnly Property Caption As Label

    ''' <summary>The explanation label, or Nothing. It carries no toggle handler.</summary>
    Friend ReadOnly Property Explanation As Label

    ''' <summary>The factor to lay out at when the owner places the row itself (a hosted panel); 0 = derive it.</summary>
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property ScaleOverride As Single

    ''' <summary>True when the owner paints the row's colours itself (a hosted panel with its own palette), so the
    ''' window theme walk leaves them alone.</summary>
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property OwnsPalette As Boolean Implements IThemedOptionRow.OwnsPalette

    ''' <summary>The theme walk's hand-off: the box takes the palette roles (see <see cref="DialogOptionRow.ApplyPalette"/>).</summary>
    Friend Sub ApplyThemePalette(palette As ThemePalette) Implements IThemedOptionRow.ApplyThemePalette
        DialogOptionRow.ApplyPalette(Me, palette)
    End Sub

    Private ReadOnly edgeInsetLogical As Integer
    Private explanationFont As Font
    Private arranging As Boolean
    Private armed As Boolean
    Private arrangedWidth As Integer

    Friend Sub New(check As CheckBox, explanation As Label, edgeInsetLogical As Integer)
        Me.Check = check
        Me.Explanation = explanation
        Me.edgeInsetLogical = edgeInsetLogical
        Dim captionText As String = check.Text
        Me.Name = If(String.IsNullOrEmpty(check.Name), "", check.Name & "_Row")
        Me.TabStop = False
        Me.AutoScroll = False

        ' The native control's text moves to the caption label; SettingsCheckBox.Attach clears it. The accessible
        ' name and description carry it on, so the box stays ONE named boolean with its explanation as description
        ' (BOOLEAN-OPTION 2.0 section 3 rule 3).
        Caption = New Label With {
            .Name = If(String.IsNullOrEmpty(check.Name), "", check.Name & "_Caption"),
            .AutoSize = False,
            .Text = captionText,
            .Height = 16
        }
        SettingsCheckBox.Attach(check, 1.0F)
        check.AccessibleName = captionText
        If explanation IsNot Nothing Then
            check.AccessibleDescription = explanation.Text
            ' The caller may have placed the label already; an explanation belongs to the row alone.
            If explanation.Parent IsNot Nothing Then explanation.Parent.Controls.Remove(explanation)
        End If
        If check.Parent IsNot Nothing Then check.Parent.Controls.Remove(check)

        Controls.Add(check)
        Controls.Add(Caption)
        If explanation IsNot Nothing Then Controls.Add(explanation)
        SettingsCheckBox.BindCaption(Caption, check)
        Caption.Enabled = check.Enabled
        AddHandler check.EnabledChanged, Sub() Caption.Enabled = check.Enabled
        AddHandler RightToLeftChanged, Sub() Reflow()
        AddHandler FontChanged, Sub() Reflow()
        AddHandler DpiChangedAfterParent, Sub() Reflow()
        ' A dialog that re-lays its rows at another width (a mode change) only sets the bounds; the caption wraps to the new width.
        AddHandler Resize, Sub() If armed AndAlso Width <> arrangedWidth Then Arrange()
        ' Not arranged here either: a panel starts 200 px wide, so an arrangement now would arm the row at that width and the
        ' bounds the owner gives it next would re-arrange it before the window's reflow (DialogLayout.FitContent) measures the
        ' authored heights. The first arrangement is the reflow's (DialogOptionRow.NeededHeight) or the host's (Arrange).
    End Sub

    ''' <summary>Changes the caption at run time (a language switch of a hosted panel) and lays the row out again.</summary>
    Friend Sub SetCaption(text As String)
        Caption.Text = text
        Check.AccessibleName = text
        Reflow()
    End Sub

    ''' <summary>Changes the explanation at run time and lays the row out again.</summary>
    Friend Sub SetExplanation(text As String)
        If Explanation Is Nothing Then Return
        Explanation.Text = text
        Check.AccessibleDescription = text
        Reflow()
    End Sub

    Private Sub Reflow()
        If armed Then Arrange()
    End Sub

    ''' <summary>Lays the caption, the explanation and the hit target out for the row's present width, scale, font and
    ''' reading direction, and sets the row's height from its content.</summary>
    Friend Sub Arrange()
        If arranging OrElse IsDisposed OrElse Width <= 0 Then Return
        arranging = True
        Try
            armed = True
            arrangedWidth = Width
            Dim scale As Single = DialogOptionRow.ScaleOf(Me)
            SettingsCheckBox.Attach(Check, scale)
            FitExplanationFont(scale)
            SettingsCheckBox.ArrangeRow(Me, Check, Caption, Explanation, scale, Nothing, edgeInsetLogical)
        Finally
            arranging = False
        End Try
    End Sub

    ''' <summary>The explanation is the 12 logical px role at least (section 2 rule 5), in the window's own face, so a
    ''' script font (ar, ja, ..) still draws it. It grows with the scale and is never shrunk to fit.</summary>
    Private Sub FitExplanationFont(scale As Single)
        If Explanation Is Nothing Then Return
        Dim captionPixels As Single = Font.SizeInPoints * DeviceDpi / 72.0F
        Dim pixels As Single = Math.Max(SettingsRowGridRules.ExplanationFontPixels * scale, captionPixels)
        If explanationFont IsNot Nothing AndAlso Math.Abs(explanationFont.Size - pixels) < 0.01F AndAlso
           explanationFont.Unit = GraphicsUnit.Pixel AndAlso String.Equals(explanationFont.FontFamily.Name, Font.FontFamily.Name, StringComparison.Ordinal) Then
            Return
        End If
        Dim made As New Font(Font.FontFamily, pixels, FontStyle.Regular, GraphicsUnit.Pixel)
        Dim old As Font = explanationFont
        explanationFont = made
        Explanation.Font = made
        old?.Dispose()
    End Sub

    ''' <summary>Palette roles of the box (the same seven the Settings window passes).</summary>
    Friend Sub ApplyColors(surface As Color, uncheckedBack As Color, border As Color, hover As Color, accent As Color, tick As Color)
        SettingsCheckBox.ApplyPalette(Check, surface, uncheckedBack, border, hover, accent, tick)
    End Sub

    ''' <summary>A host that owns its palette (the hosted Exchange devices panel) colours the whole row.</summary>
    Friend Sub ApplyOwnPalette(surface As Color, text As Color, muted As Color, uncheckedBack As Color,
                               accent As Color, tick As Color)
        OwnsPalette = True
        BackColor = surface
        Caption.ForeColor = text
        If Explanation IsNot Nothing Then Explanation.ForeColor = muted
        If SystemInformation.HighContrast Then
            ApplyColors(surface, SystemColors.Window, SystemColors.GrayText, SystemColors.WindowText, SystemColors.Highlight, SystemColors.HighlightText)
            Return
        End If
        ' The same floor the dialogs have: an accent that would not reach 3:1 on the surface (or a tick that would not on the
        ' accent) falls back to the standard blue.
        If AccentColors.Contrast(accent, surface) < 3.0R OrElse AccentColors.Contrast(tick, accent) < 3.0R Then
            accent = Color.FromArgb(0, 120, 215)
            tick = Color.White
        End If
        ApplyColors(surface, uncheckedBack, muted, text, accent, tick)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then
            explanationFont?.Dispose()
            explanationFont = Nothing
        End If
    End Sub
End Class
#End If
