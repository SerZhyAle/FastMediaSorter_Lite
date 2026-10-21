#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms

''' <summary>The colour roles an option's heading parts and help popups paint with. The window that owns the row hands
''' over its own palette (surface, text, muted text, border, accent); <see cref="FromSystem"/> is the default of a row
''' that never was given one, and the high-contrast set is always the system colours.</summary>
Friend Structure OptionHelpPalette
    Public Surface As Color
    Public Ink As Color
    Public MutedInk As Color
    Public Border As Color
    Public Accent As Color

    ''' <summary>The Windows system colours: the default and the high-contrast set.</summary>
    Friend Shared Function FromSystem() As OptionHelpPalette
        Return New OptionHelpPalette With {
            .Surface = SystemColors.Control, .Ink = SystemColors.ControlText, .MutedInk = SystemColors.GrayText,
            .Border = SystemColors.ControlDark, .Accent = SystemColors.Highlight}
    End Function

    ''' <summary>A fraction of <paramref name="toward"/> laid over <paramref name="face"/> (percent).</summary>
    Friend Shared Function Blend(face As Color, toward As Color, percent As Integer) As Color
        Dim a As Double = Math.Max(0, Math.Min(100, percent)) / 100.0R
        Return Color.FromArgb(CInt(Math.Round(face.R + (CInt(toward.R) - CInt(face.R)) * a)),
                              CInt(Math.Round(face.G + (CInt(toward.G) - CInt(face.G)) * a)),
                              CInt(Math.Round(face.B + (CInt(toward.B) - CInt(face.B)) * a)))
    End Function
End Structure

''' <summary>The `?` command button of an option (BOOLEAN-OPTION 2.0 section 4, BUTTONS 2.0): a flat button whose only
''' content is the catalog glyph <c>app.help</c> from <see cref="ButtonGlyphs"/>, with a focus ring of its own that is
''' drawn whenever the button has the focus (a ring that waits for the keyboard to have been used last would be
''' invisible to a user who tabs in after a click).</summary>
Friend NotInheritable Class OptionHelpButton
    Inherits Button

    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property RingColor As Color = SystemColors.Highlight
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property RingScale As Single = 1.0F

    Friend Sub New()
        SetStyle(ControlStyles.Selectable, True)
        FlatStyle = FlatStyle.Flat
        FlatAppearance.BorderSize = 0
        TabStop = True
        UseMnemonic = False
        AutoSize = False
        DialogResult = DialogResult.None
        Margin = Padding.Empty
        RightToLeft = RightToLeft.No
        AccessibleRole = AccessibleRole.PushButton
        ButtonGlyphs.Apply(Me, "app.help")
        CentreGlyph()
    End Sub

    ''' <summary>ButtonGlyphs draws the glyph with a caption gap on one side, which puts the picture off-centre on a button
    ''' with no caption; the same width as padding on the other side puts it back in the middle.</summary>
    Friend Sub CentreGlyph()
        Dim gap As Integer = Math.Max(2, LogicalToDeviceUnits(ButtonGlyphs.GapLogicalSize))
        Dim wanted As New Padding(gap, 0, 0, 0)
        If Padding <> wanted Then Padding = wanted
    End Sub

    Protected Overrides Sub OnDpiChangedAfterParent(e As EventArgs)
        MyBase.OnDpiChangedAfterParent(e)
        CentreGlyph()
    End Sub

    ''' <summary>The focus ring is this control's own.</summary>
    Protected Overrides ReadOnly Property ShowFocusCues As Boolean
        Get
            Return False
        End Get
    End Property

    Protected Overrides Sub OnPaint(pevent As PaintEventArgs)
        MyBase.OnPaint(pevent)
        If Not Focused OrElse Not Enabled Then Return
        Dim ringWidth As Single = Math.Max(1.0F, 2.0F * RingScale)
        Dim inset As Integer = CInt(Math.Ceiling(ringWidth))
        Dim ring As New Rectangle(inset, inset, ClientSize.Width - 2 * inset - 1, ClientSize.Height - 2 * inset - 1)
        If ring.Width < 4 OrElse ring.Height < 4 Then Return
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Using pen As New Pen(RingColor, ringWidth)
            Using path As New GraphicsPath()
                Dim d As Integer = Math.Max(2, CInt(8.0F * RingScale))
                path.AddArc(ring.Left, ring.Top, d, d, 180, 90)
                path.AddArc(ring.Right - d, ring.Top, d, d, 270, 90)
                path.AddArc(ring.Right - d, ring.Bottom - d, d, d, 0, 90)
                path.AddArc(ring.Left, ring.Bottom - d, d, d, 90, 90)
                path.CloseFigure()
                pevent.Graphics.DrawPath(pen, path)
            End Using
        End Using
        pevent.Graphics.SmoothingMode = SmoothingMode.None
    End Sub
End Class

Friend Enum OptionHelpPopupKind
    ''' <summary>The short non-modal summary of hover and focus: never activated, never takes the focus.</summary>
    Summary = 0
    ''' <summary>The persistent explanation: activated, with a Close action; Escape or an outside click closes it.</summary>
    Full = 1
End Enum

''' <summary>
''' The window of a help text: a borderless owned tool window, not a dialog. The summary kind is never activated
''' (<c>WS_EX_NOACTIVATE</c>, shown without activation) so hover cannot steal the focus; the full kind takes the focus
''' into its Close button and gives it back through <see cref="OptionHelpController"/>. All geometry is explicit
''' coordinates; the text wraps and the window grows, nothing is clipped or ellipsized.
''' </summary>
Friend NotInheritable Class OptionHelpPopup
    Inherits Form

    Private ReadOnly kindValue As OptionHelpPopupKind
    Private ReadOnly captionLabel As New Label()
    Private ReadOnly bodyLabel As New Label()
    Private borderColor As Color = SystemColors.ControlDark
    Private ReadOnly fonts As New List(Of Font)()

    ''' <summary>The Close action of the full kind; Nothing for the summary.</summary>
    Friend ReadOnly Property CloseButton As Button

    ''' <summary>Raised when the pointer enters (True) or leaves (False) the window or any of its parts.</summary>
    Friend Event PointerChanged(inside As Boolean)

    ''' <summary>Raised for Escape inside the window.</summary>
    Friend Event EscapeRequested()

    Friend Sub New(kind As OptionHelpPopupKind)
        kindValue = kind
        FormBorderStyle = FormBorderStyle.None
        ShowInTaskbar = False
        StartPosition = FormStartPosition.Manual
        AutoScaleMode = AutoScaleMode.None
        KeyPreview = True
        Padding = Padding.Empty
        DoubleBuffered = True
        captionLabel.AutoSize = False
        captionLabel.UseMnemonic = False
        bodyLabel.AutoSize = False
        bodyLabel.UseMnemonic = False
        bodyLabel.AutoEllipsis = False
        Controls.Add(bodyLabel)
        If kind = OptionHelpPopupKind.Full Then
            Controls.Add(captionLabel)
            Dim close As New Button With {.FlatStyle = FlatStyle.Flat, .TabStop = True, .UseMnemonic = False,
                                          .AutoSize = False, .DialogResult = DialogResult.None, .Name = "optionHelpClose"}
            close.FlatAppearance.BorderSize = 1
            _CloseButton = close
            Controls.Add(close)
            AddHandler close.Click, Sub() RaiseEvent EscapeRequested()
        End If
        For Each part As Control In New Control() {Me, bodyLabel, captionLabel, _CloseButton}
            If part Is Nothing Then Continue For
            AddHandler part.MouseEnter, AddressOf OnPartPointer
            AddHandler part.MouseLeave, AddressOf OnPartPointer
        Next
    End Sub

    Friend ReadOnly Property Kind As OptionHelpPopupKind
        Get
            Return kindValue
        End Get
    End Property

    ''' <summary>The wrapped body label (tests read its text and font).</summary>
    Friend ReadOnly Property BodyControl As Label
        Get
            Return bodyLabel
        End Get
    End Property

    ''' <summary>The heading label of the full kind.</summary>
    Friend ReadOnly Property CaptionControl As Label
        Get
            Return captionLabel
        End Get
    End Property

    Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
        Get
            Return kindValue = OptionHelpPopupKind.Summary
        End Get
    End Property

    Protected Overrides ReadOnly Property CreateParams As CreateParams
        Get
            Dim cp As CreateParams = MyBase.CreateParams
            cp.ExStyle = cp.ExStyle Or &H80                    ' WS_EX_TOOLWINDOW: no taskbar button, no Alt+Tab entry
            If kindValue = OptionHelpPopupKind.Summary Then cp.ExStyle = cp.ExStyle Or &H8000000 ' WS_EX_NOACTIVATE
            Return cp
        End Get
    End Property

    Private Sub OnPartPointer(sender As Object, e As EventArgs)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        RaiseEvent PointerChanged(Bounds.Contains(Control.MousePosition))
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If keyData = Keys.Escape Then
            RaiseEvent EscapeRequested()
            Return True
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Using pen As New Pen(borderColor, 1.0F)
            e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1)
        End Using
    End Sub

    ''' <summary>The fonts of the previous <see cref="Present"/> call, kept alive while the labels still use them.</summary>
    Private ReadOnly retiredFonts As New List(Of Font)()

    ''' <summary>A pixel font for the labels. A font equal to one the labels already hold is that same instance: assigning an
    ''' equal font to a control keeps the old object, so a fresh copy would leave the label on a font disposed afterwards.</summary>
    Private Function PixelFont(family As String, pixels As Integer, style As FontStyle) As Font
        Dim font As New Font(family, CSng(Math.Max(8, pixels)), style, GraphicsUnit.Pixel)
        For Each held As Font In retiredFonts
            If held.Equals(font) Then
                font.Dispose()
                retiredFonts.Remove(held)
                fonts.Add(held)
                Return held
            End If
        Next
        fonts.Add(font)
        Return font
    End Function

    ''' <summary>Fills the window and sizes it: the text wrapped at the window's width, the Close action at the reading
    ''' end under it. After this <see cref="Control.ClientSize"/> is the size the popup wants.</summary>
    Friend Sub Present(captionText As String, bodyText As String, rtl As Boolean, scale As Single,
                       palette As OptionHelpPalette, workWidth As Integer)
        SuspendLayout()
        Try
            RightToLeft = If(rtl, RightToLeft.Yes, RightToLeft.No)
            BackColor = palette.Surface
            ForeColor = palette.Ink
            borderColor = palette.Border
            Dim family As String = If(Localization.FontFamily(), "Segoe UI")
            Dim pad As Integer = SettingsCheckBox.Px(12, scale)
            Dim logicalWidth As Integer = If(kindValue = OptionHelpPopupKind.Full, OptionHelpRules.FullWidthLogical, OptionHelpRules.SummaryMaxWidthLogical)
            Dim width As Integer = Math.Min(SettingsCheckBox.Px(logicalWidth, scale),
                                            Math.Max(SettingsCheckBox.Px(160, scale), workWidth - SettingsCheckBox.Px(16, scale)))
            Dim inner As Integer = Math.Max(SettingsCheckBox.Px(80, scale), width - 2 * pad)

            ' The fonts of the previous call are still the labels' fonts until the new ones are assigned below: equal ones are
            ' reused by PixelFont, the rest are disposed afterwards (a label measured with a disposed font throws).
            retiredFonts.AddRange(fonts)
            fonts.Clear()
            bodyLabel.Font = PixelFont(family, SettingsCheckBox.Px(SettingsRowGridRules.ExplanationFontPixels, scale), FontStyle.Regular)
            bodyLabel.ForeColor = palette.Ink
            bodyLabel.BackColor = palette.Surface
            bodyLabel.Text = If(bodyText, String.Empty)
            AccessibleName = If(captionText, String.Empty)
            AccessibleRole = AccessibleRole.Window

            Dim y As Integer = pad
            If kindValue = OptionHelpPopupKind.Full Then
                captionLabel.Font = PixelFont(family, SettingsCheckBox.Px(SettingsRowGridRules.ExplanationFontPixels, scale), FontStyle.Bold)
                captionLabel.ForeColor = palette.Ink
                captionLabel.BackColor = palette.Surface
                captionLabel.Text = If(captionText, String.Empty)
                captionLabel.Visible = captionLabel.Text.Length > 0
                If captionLabel.Visible Then
                    Dim captionHeight As Integer = SettingsRowText.WrappedHeight(captionLabel, inner, SettingsCheckBox.Px(16, scale), SettingsCheckBox.Px(2, scale))
                    captionLabel.SetBounds(pad, y, inner, captionHeight)
                    y += captionHeight + SettingsCheckBox.Px(4, scale)
                End If
            Else
                captionLabel.Visible = False
            End If

            Dim bodyHeight As Integer = SettingsRowText.WrappedHeight(bodyLabel, inner, SettingsCheckBox.Px(16, scale), SettingsCheckBox.Px(2, scale))
            bodyLabel.SetBounds(pad, y, inner, bodyHeight)
            y += bodyHeight + pad

            If kindValue = OptionHelpPopupKind.Full Then
                Dim close As Button = CloseButton
                close.Font = bodyLabel.Font
                close.Text = Localization.T("Закрыть")
                close.BackColor = OptionHelpPalette.Blend(palette.Surface, palette.Ink, 8)
                close.ForeColor = palette.Ink
                close.FlatAppearance.BorderColor = palette.Border
                close.FlatAppearance.MouseOverBackColor = OptionHelpPalette.Blend(palette.Surface, palette.Ink, 16)
                close.FlatAppearance.MouseDownBackColor = OptionHelpPalette.Blend(palette.Surface, palette.Ink, 24)
                ButtonGlyphs.Apply(close, ButtonGlyphs.DefaultGlyph(ButtonGlyphRole.Dismissing), ButtonGlyphRole.Dismissing)
                Dim textWidth As Integer = TextRenderer.MeasureText(close.Text, close.Font, New Size(Short.MaxValue, Short.MaxValue),
                                                                     TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine).Width
                Dim buttonWidth As Integer = textWidth + 2 * SettingsCheckBox.Px(UiSizing.PointerPaddingX, scale) + ButtonGlyphs.ExtraWidth(close)
                Dim buttonHeight As Integer = UiSizing.TargetPx(UiInputProfile.Pointer, UiDensity.Ordinary, scale)
                Dim left As Integer = If(rtl, pad, width - pad - buttonWidth)
                close.SetBounds(left, y, buttonWidth, buttonHeight)
                y += buttonHeight + pad
            End If
            ClientSize = New Size(width, y)
        Finally
            ResumeLayout(True)
            For Each old As Font In retiredFonts
                old.Dispose()
            Next
            retiredFonts.Clear()
        End Try
        Invalidate()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            For Each font As Font In fonts
                font.Dispose()
            Next
            fonts.Clear()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class

''' <summary>
''' The behaviour of one option's `?` (BOOLEAN-OPTION 2.0 section 4 rules 2 to 4): pointer hover and keyboard focus open
''' the short summary without taking the focus; click, Enter and Space open the persistent explanation; Escape and an
''' outside click close what is open and the focus goes back to the button. The checkbox is never touched - this object has
''' no reference to a handler of it, and its events are the help button's own.
''' The timing is the pure <see cref="OptionHelpHover"/>; a timer only feeds it the clock.
''' </summary>
Friend NotInheritable Class OptionHelpController

    Private Shared ReadOnly tracked As New List(Of OptionHelpController)()
    Private Shared ReadOnly filter As New OptionHelpMessageFilter()
    Private Shared filterInstalled As Boolean

    Private ReadOnly button As OptionHelpButton
    Private ReadOnly check As CheckBox
    Private ReadOnly hover As New OptionHelpHover()
    Private ReadOnly timer As New System.Windows.Forms.Timer() With {.Interval = 60}
    Private summaryPopup As OptionHelpPopup
    Private fullPopup As OptionHelpPopup

    Friend Sub New(button As OptionHelpButton, check As CheckBox)
        Me.button = button
        Me.check = check
        Palette = OptionHelpPalette.FromSystem()
        Scale = 1.0F
        Clock = Function() Environment.TickCount64
        WorkAreaProvider = Function(anchor As Rectangle) Screen.GetWorkingArea(anchor)
        AddHandler timer.Tick, Sub() Tick()
        AddHandler button.MouseEnter, Sub() PointerEnteredButton()
        AddHandler button.MouseLeave, Sub() PointerLeftButton()
        AddHandler button.GotFocus, Sub() FocusEntered()
        AddHandler button.LostFocus, Sub() FocusLeft()
        AddHandler button.Click, Sub() Activate()
        AddHandler button.VisibleChanged, Sub() If Not button.Visible Then CloseAll()
        AddHandler button.Disposed, Sub() Release()
    End Sub

    ''' <summary>The help text; replaced on a language switch.</summary>
    Friend Property Source As OptionHelpSource

    ''' <summary>The name of the option, the heading of the persistent explanation.</summary>
    Friend Property OptionName As String = String.Empty

    Friend Property Palette As OptionHelpPalette
    Friend Property Scale As Single

    ''' <summary>The clock the hover timing reads (milliseconds); a test replaces it.</summary>
    Friend Property Clock As Func(Of Long)

    ''' <summary>The usable area of the monitor around a screen rectangle; a test replaces it so a popup never reaches the
    ''' real desktop.</summary>
    Friend Property WorkAreaProvider As Func(Of Rectangle, Rectangle)

    Friend ReadOnly Property SummaryPopupWindow As OptionHelpPopup
        Get
            Return summaryPopup
        End Get
    End Property

    Friend ReadOnly Property FullPopupWindow As OptionHelpPopup
        Get
            Return fullPopup
        End Get
    End Property

    Friend ReadOnly Property SummaryVisible As Boolean
        Get
            Return summaryPopup IsNot Nothing AndAlso Not summaryPopup.IsDisposed AndAlso summaryPopup.Visible
        End Get
    End Property

    Friend ReadOnly Property FullVisible As Boolean
        Get
            Return fullPopup IsNot Nothing AndAlso Not fullPopup.IsDisposed AndAlso fullPopup.Visible
        End Get
    End Property

    Friend ReadOnly Property HoverModel As OptionHelpHover
        Get
            Return hover
        End Get
    End Property

    ' --- the events of the button and of the summary ------------------------------------------

    Private Function NowMs() As Long
        Return Clock.Invoke()
    End Function

    Friend Sub PointerEnteredButton()
        hover.PointerEnteredButton(NowMs())
        StartTimer()
    End Sub

    Friend Sub PointerLeftButton()
        hover.PointerLeftButton(NowMs())
        StartTimer()
    End Sub

    Friend Sub FocusEntered()
        hover.FocusEntered(NowMs())
        StartTimer()
        Tick()
    End Sub

    Friend Sub FocusLeft()
        hover.FocusLeft(NowMs())
        StartTimer()
    End Sub

    ''' <summary>The pointer entered (True) or left (False) the summary popup.</summary>
    Friend Sub PopupPointer(inside As Boolean)
        If inside Then hover.PointerEnteredPopup(NowMs()) Else hover.PointerLeftPopup(NowMs())
        StartTimer()
    End Sub

    Private Sub StartTimer()
        If Not timer.Enabled Then timer.Start()
    End Sub

    ''' <summary>Feeds the clock to the model and does what it asks.</summary>
    Friend Sub Tick()
        If button.IsDisposed Then Return
        Select Case hover.Tick(NowMs())
            Case OptionHelpHoverAction.ShowSummary : ShowSummaryPopup()
            Case OptionHelpHoverAction.HideSummary : HideSummaryPopup()
        End Select
        If hover.IsIdle Then timer.Stop()
    End Sub

    ' --- activation: click, Enter, Space -----------------------------------------------------------

    ''' <summary>Click, Enter or Space on the button: opens the persistent explanation, or closes it when it is open.</summary>
    Friend Sub Activate()
        If FullVisible Then
            CloseFull(True)
        Else
            OpenFull()
        End If
    End Sub

    Friend Sub OpenFull()
        If Source Is Nothing OrElse button.IsDisposed Then Return
        HideSummaryPopup()
        hover.Dismiss()
        If fullPopup Is Nothing OrElse fullPopup.IsDisposed Then
            fullPopup = New OptionHelpPopup(OptionHelpPopupKind.Full)
            AddHandler fullPopup.EscapeRequested, Sub() CloseFull(True)
        End If
        PresentAndPlace(fullPopup, Source.Full)
        Owned(fullPopup)
        If Not fullPopup.Visible Then fullPopup.Show()
        Track()
        fullPopup.ActiveControl = fullPopup.CloseButton
        fullPopup.Activate()
    End Sub

    ''' <summary>Closes the persistent explanation; with <paramref name="returnFocus"/> the focus goes back to the `?`
    ''' button (an explicit close, Escape or an outside click) and the summary stays quiet until the pointer and the
    ''' focus have both left.</summary>
    Friend Sub CloseFull(returnFocus As Boolean)
        ' Dismiss first: hiding the explanation re-activates the owner, which can focus the button, and a focus that arrives
        ' while the summary is not suppressed would open it at once.
        If returnFocus Then hover.Dismiss()
        If fullPopup IsNot Nothing AndAlso Not fullPopup.IsDisposed AndAlso fullPopup.Visible Then fullPopup.Hide()
        If returnFocus AndAlso Not button.IsDisposed Then
            button.Select()
            If button.CanFocus Then button.Focus()
            HideSummaryPopup()
        End If
        If Not SummaryVisible Then Untrack()
    End Sub

    ' --- Escape ---------------------------------------------------------------------------------------

    ''' <summary>Escape: closes the persistent explanation, else the summary. True when something was closed (the key is
    ''' then used; the pointer has not moved).</summary>
    Friend Function HandleEscape() As Boolean
        If FullVisible Then
            CloseFull(True)
            Return True
        End If
        If hover.Escape() Then
            HideSummaryPopup()
            Return True
        End If
        If SummaryVisible Then
            HideSummaryPopup()
            Return True
        End If
        Return False
    End Function

    ''' <summary>Everything this button opened goes away (the button was hidden, the window closed).</summary>
    Friend Sub CloseAll()
        HideSummaryPopup()
        If FullVisible Then fullPopup.Hide()
        hover.Reset()
        Untrack()
        timer.Stop()
    End Sub

    ' --- the popups -------------------------------------------------------------------------------------

    Private Sub ShowSummaryPopup()
        If Source Is Nothing OrElse button.IsDisposed OrElse FullVisible Then Return
        If summaryPopup Is Nothing OrElse summaryPopup.IsDisposed Then
            summaryPopup = New OptionHelpPopup(OptionHelpPopupKind.Summary)
            AddHandler summaryPopup.PointerChanged, Sub(inside As Boolean) PopupPointer(inside)
            AddHandler summaryPopup.EscapeRequested, Sub() HandleEscape()
        End If
        PresentAndPlace(summaryPopup, Source.Summary)
        Owned(summaryPopup)
        If Not summaryPopup.Visible Then summaryPopup.Show()
        Track()
    End Sub

    Private Sub HideSummaryPopup()
        If summaryPopup IsNot Nothing AndAlso Not summaryPopup.IsDisposed AndAlso summaryPopup.Visible Then summaryPopup.Hide()
        If Not FullVisible Then Untrack()
    End Sub

    Private Sub Owned(popup As OptionHelpPopup)
        Dim top As Form = button.FindForm()
        If top IsNot Nothing AndAlso Not top.IsDisposed AndAlso popup.Owner IsNot top Then popup.Owner = top
    End Sub

    Private Function IsRightToLeftRow() As Boolean
        Dim host As Control = If(check IsNot Nothing, check.Parent, button.Parent)
        Return host IsNot Nothing AndAlso host.RightToLeft = RightToLeft.Yes
    End Function

    Private Sub PresentAndPlace(popup As OptionHelpPopup, text As String)
        Dim rtl As Boolean = IsRightToLeftRow()
        Dim anchor As Rectangle = button.RectangleToScreen(button.ClientRectangle)
        Dim workArea As Rectangle = WorkAreaProvider.Invoke(anchor)
        popup.Present(OptionName, text, rtl, Scale, Palette, workArea.Width)
        Dim avoid As New List(Of Rectangle)()
        If check IsNot Nothing AndAlso Not check.IsDisposed Then avoid.Add(check.RectangleToScreen(check.ClientRectangle))
        popup.Bounds = OptionHelpPlacement.Place(anchor, avoid, popup.Size, workArea, rtl, SettingsCheckBox.Px(4, Scale))
    End Sub

    ''' <summary>Re-applies the palette and the text to a popup that is open (a theme or language switch).</summary>
    Friend Sub Restyle()
        If SummaryVisible AndAlso Source IsNot Nothing Then PresentAndPlace(summaryPopup, Source.Summary)
        If FullVisible AndAlso Source IsNot Nothing Then PresentAndPlace(fullPopup, Source.Full)
    End Sub

    Private Sub Release()
        timer.Stop()
        timer.Dispose()
        Untrack()
        If summaryPopup IsNot Nothing Then summaryPopup.Dispose()
        If fullPopup IsNot Nothing Then fullPopup.Dispose()
        summaryPopup = Nothing
        fullPopup = Nothing
    End Sub

    ' --- the process-wide Escape and outside-click filter ----------------------------------------------------

    Private Sub Track()
        If Not tracked.Contains(Me) Then tracked.Add(Me)
        If Not filterInstalled Then
            Application.AddMessageFilter(filter)
            filterInstalled = True
        End If
    End Sub

    Private Sub Untrack()
        tracked.Remove(Me)
        If tracked.Count = 0 AndAlso filterInstalled Then
            Application.RemoveMessageFilter(filter)
            filterInstalled = False
        End If
    End Sub

    ''' <summary>How many controllers have a popup open (tests).</summary>
    Friend Shared ReadOnly Property OpenCount As Integer
        Get
            Return tracked.Count
        End Get
    End Property

    ''' <summary>The Escape of the message loop: the controller with the persistent explanation first, else any with a
    ''' summary. True when a popup was closed.</summary>
    Friend Shared Function DismissByEscape() As Boolean
        For Each controller As OptionHelpController In tracked.ToArray()
            If controller.FullVisible Then Return controller.HandleEscape()
        Next
        For Each controller As OptionHelpController In tracked.ToArray()
            If controller.HandleEscape() Then Return True
        Next
        Return False
    End Function

    ''' <summary>A mouse press on the window <paramref name="handle"/>: every popup it is not inside of closes, except the
    ''' button's own click (which toggles) and the summary under the pointer.</summary>
    Friend Shared Sub DismissOutside(handle As IntPtr)
        Dim target As Control = Control.FromChildHandle(handle)
        Dim targetForm As Form = If(target Is Nothing, Nothing, target.FindForm())
        For Each controller As OptionHelpController In tracked.ToArray()
            If target Is controller.button Then Continue For
            If targetForm IsNot Nothing AndAlso (targetForm Is controller.summaryPopup OrElse targetForm Is controller.fullPopup) Then Continue For
            If controller.FullVisible Then controller.CloseFull(True)
            If controller.SummaryVisible Then
                controller.hover.Dismiss()
                controller.HideSummaryPopup()
            End If
        Next
    End Sub
End Class

''' <summary>The Escape key and the outside press, taken before any window sees them while a help popup is open: the form
''' behind a hover summary would otherwise close on Escape, and the pointer never needs to move.</summary>
Friend NotInheritable Class OptionHelpMessageFilter
    Implements IMessageFilter

    Friend Const WM_KEYDOWN As Integer = &H100
    Friend Const WM_LBUTTONDOWN As Integer = &H201
    Friend Const WM_RBUTTONDOWN As Integer = &H204
    Friend Const WM_MBUTTONDOWN As Integer = &H207
    Friend Const WM_NCLBUTTONDOWN As Integer = &HA1
    Friend Const WM_NCRBUTTONDOWN As Integer = &HA4
    Friend Const VK_ESCAPE As Integer = &H1B

    Public Function PreFilterMessage(ByRef m As Message) As Boolean Implements IMessageFilter.PreFilterMessage
        Select Case m.Msg
            Case WM_KEYDOWN
                If m.WParam.ToInt64() = VK_ESCAPE Then Return OptionHelpController.DismissByEscape()
            Case WM_LBUTTONDOWN, WM_RBUTTONDOWN, WM_MBUTTONDOWN, WM_NCLBUTTONDOWN, WM_NCRBUTTONDOWN
                OptionHelpController.DismissOutside(m.HWnd)
        End Select
        Return False
    End Function
End Class

''' <summary>
''' The optional parts of a boolean option's heading row - the recognizable glyph and the `?` help button - and what
''' belongs to them (BOOLEAN-OPTION 2.0 sections 2, 3 and 4; ticket 243, slice T-11a). An option that was given neither
''' has no <see cref="OptionHeading"/> at all, so its row is laid out exactly as before; <see cref="SettingsCheckBox.ArrangeRow"/>
''' finds the heading of a checkbox through <see cref="Attach"/>'s registration and places the parts.
''' </summary>
Friend NotInheritable Class OptionHeading

    Private Shared ReadOnly headings As New ConditionalWeakTable(Of CheckBox, OptionHeading)()

    Private ReadOnly host As Control
    Private ReadOnly check As CheckBox
    Private ReadOnly title As Label
    Private ReadOnly description As Label
    Private ReadOnly glyphId As String
    Private glyphInk As Color = Color.Empty
    Private palette As OptionHelpPalette = OptionHelpPalette.FromSystem()

    ''' <summary>The heading glyph (a label, so a click on it toggles the option like the caption does); Nothing when the
    ''' option has none.</summary>
    Friend ReadOnly Property Glyph As Label

    ''' <summary>The `?` button; Nothing when the option has no help.</summary>
    Friend ReadOnly Property Help As OptionHelpButton

    Friend ReadOnly Property Controller As OptionHelpController

    Friend Property Profile As UiInputProfile = UiInputProfile.Pointer
    Friend Property Density As UiDensity = UiDensity.Ordinary

    Private Sub New(host As Control, check As CheckBox, title As Label, description As Label, glyphId As String)
        Me.host = host
        Me.check = check
        Me.title = title
        Me.description = description
        Me.glyphId = glyphId
    End Sub

    ''' <summary>The heading registered for a checkbox, or Nothing.</summary>
    Friend Shared Function Find(check As CheckBox) As OptionHeading
        Dim found As OptionHeading = Nothing
        If check IsNot Nothing AndAlso headings.TryGetValue(check, found) Then Return found
        Return Nothing
    End Function

    Friend ReadOnly Property HasGlyph As Boolean
        Get
            Return Glyph IsNot Nothing
        End Get
    End Property

    Friend ReadOnly Property HasHelp As Boolean
        Get
            Return Help IsNot Nothing
        End Get
    End Property

    ''' <summary>
    ''' Gives a boolean row its optional glyph and help button. <paramref name="glyphId"/> is a catalog glyph id (empty: no
    ''' glyph, no room reserved); <paramref name="helpSource"/> a sound <see cref="OptionHelpSource"/> (Nothing or one that
    ''' fails <see cref="OptionHelpRules"/>: no button). With neither, nothing is created and Nothing is returned.
    ''' The controls are added to <paramref name="host"/> and the checkbox's accessibility is set (the caption is its name,
    ''' the explanation its description); the caller then runs <see cref="SettingsCheckBox.ArrangeRow"/> as for any row.
    ''' </summary>
    Friend Shared Function Attach(host As Control, check As CheckBox, title As Label, description As Label, scale As Single,
                                  glyphId As String, helpSource As OptionHelpSource,
                                  Optional profile As UiInputProfile = UiInputProfile.Pointer,
                                  Optional density As UiDensity = UiDensity.Ordinary) As OptionHeading
        Dim wantsGlyph As Boolean = Not String.IsNullOrEmpty(glyphId)
        Dim wantsHelp As Boolean = helpSource IsNot Nothing AndAlso OptionHelpRules.IsValid(helpSource)
        If Not wantsGlyph AndAlso Not wantsHelp Then Return Nothing
        Dim existing As OptionHeading = Find(check)
        If existing IsNot Nothing Then Return existing

        Dim heading As New OptionHeading(host, check, title, description, If(wantsGlyph, glyphId, String.Empty)) With {
            .Profile = profile, .Density = density}
        If wantsGlyph Then
            heading._Glyph = New Label With {.AutoSize = False, .Text = String.Empty, .ImageAlign = ContentAlignment.MiddleCenter,
                                             .BackColor = Color.Transparent, .TabStop = False, .Name = "optionGlyph",
                                             .AccessibleRole = AccessibleRole.None, .UseMnemonic = False}
            host.Controls.Add(heading.Glyph)
            SettingsCheckBox.BindCaption(heading.Glyph, check)
            AddHandler title.ForeColorChanged, Sub() heading.RenderGlyph(heading.lastScale)
            AddHandler heading.Glyph.Disposed, Sub() heading.DisposeGlyphImage()
        End If
        If wantsHelp Then
            heading._Help = New OptionHelpButton() With {.Name = "optionHelp"}
            heading._Controller = New OptionHelpController(heading.Help, check) With {.Scale = scale}
            host.Controls.Add(heading.Help)
            heading.Help.TabIndex = check.TabIndex + 1
            heading.SetHelp(helpSource)
        End If
        headings.Add(check, heading)
        heading.RefreshText()
        heading.RenderGlyph(heading.lastScale)
        Return heading
    End Function

    ''' <summary>Replaces the help text (a language switch); keeps the button, its focus and an open popup.</summary>
    Friend Sub SetHelp(source As OptionHelpSource)
        If Help Is Nothing Then Return
        Controller.Source = source
        RefreshText()
    End Sub

    ''' <summary>Re-reads the caption and the explanation into the accessibility properties: the checkbox is one named option
    ''' (name = caption, description = explanation) and its `?` is named "Help: caption" (section 3 rule 3, section 4 rule 4).
    ''' Called after a language switch.</summary>
    Friend Sub RefreshText()
        Dim caption As String = If(title Is Nothing, String.Empty, title.Text)
        check.AccessibleName = caption
        check.AccessibleDescription = If(description Is Nothing, String.Empty, description.Text)
        If Help Is Nothing Then Return
        Help.AccessibleName = OptionHelpRules.AccessibleName(caption)
        Help.AccessibleDescription = If(Controller.Source Is Nothing, String.Empty, Controller.Source.Summary)
        Controller.OptionName = caption
        Controller.Restyle()
    End Sub

    ''' <summary>The window's palette roles; called on build and on every theme change. High contrast always means the system
    ''' colours.</summary>
    Friend Sub ApplyPalette(newPalette As OptionHelpPalette)
        palette = If(SystemInformation.HighContrast, OptionHelpPalette.FromSystem(), newPalette)
        If Help IsNot Nothing Then
            Help.RingColor = palette.Accent
            Help.BackColor = palette.Surface
            Help.FlatAppearance.MouseOverBackColor = OptionHelpPalette.Blend(palette.Surface, palette.Ink, 14)
            Help.FlatAppearance.MouseDownBackColor = OptionHelpPalette.Blend(palette.Surface, palette.Ink, 24)
            Help.FlatAppearance.BorderSize = If(SystemInformation.HighContrast, 1, 0)
            Help.FlatAppearance.BorderColor = palette.Border
            Help.ForeColor = palette.Ink
            Controller.Palette = palette
            Controller.Restyle()
            Help.Invalidate()
        End If
        If Glyph IsNot Nothing Then
            glyphInk = palette.Ink
            RenderGlyph(lastScale)
        End If
    End Sub

    Private lastScale As Single = 1.0F

    ' --- sizes --------------------------------------------------------------------------------------------

    ''' <summary>The glyph's side (UI-SIZING: tier 16 for a pointer, 20 for touch).</summary>
    Friend Function GlyphSide(scale As Single) As Integer
        Return UiSizing.GlyphPx(Profile, Density, scale, 1.0F)
    End Function

    ''' <summary>The help target's side: 32 for a pointer, 48 for touch, the compact floor in compact mode.</summary>
    Friend Function HelpSide(scale As Single) As Integer
        Return UiSizing.TargetPx(Profile, Density, scale)
    End Function

    ''' <summary>
    ''' Plans the heading row in the text cell <paramref name="lead"/> (see <see cref="OptionHeadingGeometry"/>): glyph,
    ''' caption, help in reading order, 8 units apart, mirrored under right-to-left; the caption's natural width is its text
    ''' on one line.
    ''' </summary>
    Friend Function PlanFor(lead As SettingsCheckBox.Leading, rtl As Boolean, scale As Single) As OptionHeadingPlan
        lastScale = scale
        Dim gap As Integer = SettingsCheckBox.Px(UiSizing.GlyphGap, scale)
        Dim natural As Integer = TextRenderer.MeasureText(title.Text, title.Font, New Size(Integer.MaxValue \ 2, Integer.MaxValue \ 2),
                                                          TextFormatFlags.SingleLine Or TextFormatFlags.NoPadding).Width
        natural = Math.Max(natural, title.GetPreferredSize(Size.Empty).Width) + SettingsCheckBox.Px(4, scale)
        Return OptionHeadingGeometry.Plan(lead.TextLeft, lead.TextWidth, rtl, If(HasGlyph, GlyphSide(scale), 0),
                                          If(HasHelp, HelpSide(scale), 0), gap, natural, SettingsCheckBox.Px(40, scale))
    End Function

    ''' <summary>Puts the glyph and the help target on the first heading line.</summary>
    Friend Sub Place(plan As OptionHeadingPlan, titleTop As Integer, firstLine As Integer, scale As Single)
        If HasGlyph Then
            Dim side As Integer = GlyphSide(scale)
            Glyph.Visible = True
            Glyph.SetBounds(plan.GlyphLeft, titleTop + (firstLine - side) \ 2, side, side)
            RenderGlyph(scale)
        End If
        If HasHelp Then
            Dim side As Integer = HelpSide(scale)
            Help.Visible = True
            Help.SetBounds(plan.HelpLeft, titleTop + (firstLine - side) \ 2, side, side)
            Help.RingScale = scale
            Help.CentreGlyph()
            Controller.Scale = scale
        End If
    End Sub

    ''' <summary>Hides the parts of a row that has no caption to stand beside.</summary>
    Friend Sub HideParts()
        If HasGlyph Then Glyph.Visible = False
        If HasHelp Then Help.Visible = False
    End Sub

    ' --- the glyph picture ------------------------------------------------------------------------------------

    Private renderedKey As String = String.Empty

    Private Sub RenderGlyph(scale As Single)
        If Glyph Is Nothing OrElse Glyph.IsDisposed Then Return
        lastScale = scale
        Dim opener As Func(Of String, IO.Stream) = ButtonGlyphs.AssetOpener
        If opener Is Nothing Then Return
        Dim side As Integer = Math.Max(8, GlyphSide(scale))
        Dim ink As Color = CurrentGlyphInk()
        Dim key As String = glyphId & "|" & side.ToString(Globalization.CultureInfo.InvariantCulture) & "|" & ink.ToArgb().ToString(Globalization.CultureInfo.InvariantCulture)
        If String.Equals(key, renderedKey, StringComparison.Ordinal) AndAlso Glyph.Image IsNot Nothing Then Return
        Try
            Dim picture As Bitmap = GlyphRenderer.Render(glyphId, side, ink, opener)
            Dim old As Image = Glyph.Image
            Glyph.Image = picture
            old?.Dispose()
            renderedKey = key
        Catch ex As Exception
            ' A glyph that cannot be drawn never stops the row: the caption is complete without it.
            Diagnostics.Debug.WriteLine("OptionHeading: " & glyphId & ": " & ex.Message)
        End Try
    End Sub

    ''' <summary>The colour of the glyph: the caption's own colour (the window sets it on every theme pass), so the glyph holds
    ''' the contrast the caption holds on the row's surface; the system text colours under high contrast.</summary>
    Friend Function CurrentGlyphInk() As Color
        If SystemInformation.HighContrast Then Return If(check.Enabled, SystemColors.ControlText, SystemColors.GrayText)
        Dim fore As Color = If(title Is Nothing, Color.Empty, title.ForeColor)
        If fore.IsEmpty OrElse fore.A = 0 Then Return If(glyphInk.IsEmpty, palette.Ink, glyphInk)
        Return fore
    End Function

    Private Sub DisposeGlyphImage()
        If Glyph Is Nothing Then Return
        Dim old As Image = Glyph.Image
        Glyph.Image = Nothing
        old?.Dispose()
    End Sub
End Class
#End If
