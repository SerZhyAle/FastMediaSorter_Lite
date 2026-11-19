#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The short message that rises over the media when the status line cannot be seen (full screen without
''' panels - ticket 124 section 3.3, UX-207). A warning or an error only, two to three seconds, dismissed by
''' the next key press (the viewer calls <see cref="DismissNow"/>) and by a click anywhere (it is
''' click-through, so the click lands on what is under it and the toast simply goes).
'''
''' A window of its own, for the same reason the chrome panels are: a child control cannot paint over the
''' LibVLC view or a picture box. Borderless, never activated (a toast must not take the keyboard from the
''' picture), owned by the viewer, in the viewer's z-order band.
'''
''' What it says and for how long are <see cref="NotifyRules"/>'s; where it sits is
''' <see cref="DialogPlacement.ToastBounds"/>'s. This class only draws a dark card with an accent bar in the
''' level's colour, contrast-checked against the card.
''' </summary>
Friend NotInheritable Class NotifyToast
    Inherits Form

    Private Const WS_EX_NOACTIVATE As Integer = &H8000000
    Private Const WS_EX_TOOLWINDOW As Integer = &H80
    Private Const WM_MOUSEACTIVATE As Integer = &H21
    Private Const WM_NCHITTEST As Integer = &H84
    Private Const MA_NOACTIVATE As Integer = 3
    Private Const HTTRANSPARENT As Integer = -1

    ''' <summary>The card's fill: a fixed dark neutral, because a toast sits over a picture of any colour and its
    ''' own surface is what the text is contrast-checked against.</summary>
    Friend Shared ReadOnly CardFill As Color = Color.FromArgb(34, 34, 34)
    Friend Shared ReadOnly CardInk As Color = Color.FromArgb(240, 240, 240)

    Private ReadOnly hideTimer As New Timer()
    Private message As NotifyMessage
    Private accent As Color
    Private rightToLeft_Text As Boolean
    Private toastFont As Font
    ''' <summary>The card's padding and accent-bar width in device pixels, as <see cref="Present"/> sized the card with them.</summary>
    Private cardPad As Integer = 14
    Private cardBar As Integer = 4

    Friend Sub New()
        FormBorderStyle = FormBorderStyle.None
        StartPosition = FormStartPosition.Manual
        ShowInTaskbar = False
        ControlBox = False
        MinimizeBox = False
        MaximizeBox = False
        KeyPreview = False
        AutoScaleMode = AutoScaleMode.None
        BackColor = CardFill
        DoubleBuffered = True
        AddHandler hideTimer.Tick, Sub() DismissNow()
    End Sub

    Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
        Get
            Return True
        End Get
    End Property

    Protected Overrides ReadOnly Property CreateParams As CreateParams
        Get
            Dim cp As CreateParams = MyBase.CreateParams
            cp.ExStyle = cp.ExStyle Or WS_EX_NOACTIVATE Or WS_EX_TOOLWINDOW
            Return cp
        End Get
    End Property

    Protected Overrides Sub WndProc(ByRef m As Message)
        If m.Msg = WM_MOUSEACTIVATE Then
            m.Result = New IntPtr(MA_NOACTIVATE)
            Return
        End If
        If m.Msg = WM_NCHITTEST Then
            ' A click goes to whatever is under the toast.
            m.Result = New IntPtr(HTTRANSPARENT)
            Return
        End If
        MyBase.WndProc(m)
    End Sub

    ''' <summary>Shows <paramref name="text"/> over <paramref name="mediaScreenRect"/> for its level's lifetime.
    ''' Replaces a toast already up (a second message never stacks).</summary>
    ''' <param name="designScale">The owner's own layout factor (K x the interface scale and text size, the viewer's
    ''' <c>GetCurrentDpiScale</c>); the pads, the widths and the face take it (L0361 rule S4, L0363 A-D16). 0 = the
    ''' owner's dpi factor only, which a plain form without an interface setting gets.</param>
    Friend Sub Present(owner As Form, mediaScreenRect As Rectangle, text As NotifyMessage, durationMs As Integer, Optional designScale As Single = 0.0F)
        If owner Is Nothing OrElse owner.IsDisposed OrElse durationMs <= 0 OrElse text.Text.Length = 0 Then Return
        message = text
        accent = NotifyRules.InkOnSurface(text.Level, CardFill, CardInk)
        rightToLeft_Text = Localization.IsRightToLeft()
        TopMost = owner.TopMost
        Dim previous As Font = toastFont
        toastFont = TextFont(owner, designScale)
        Font = toastFont
        previous?.Dispose()

        ' One set of metrics, resolved against the OWNER's dpi (the monitor the card is drawn on) and kept for OnPaint:
        ' this window has no handle on the first toast and starts at the system dpi, so its own LogicalToDeviceUnits
        ' would size the card for one dpi and paint it for another (L0363 A-D10, L0361 S5).
        Dim pad As Integer = DesignPx(owner, 14, designScale)
        Dim bar As Integer = DesignPx(owner, 4, designScale)
        cardPad = pad
        cardBar = bar
        Dim margin As Integer = DesignPx(owner, 24, designScale)
        Dim maxWidth As Integer = Math.Max(DesignPx(owner, 160, designScale), Math.Min(DesignPx(owner, 560, designScale), mediaScreenRect.Width - 2 * margin))
        ' Measured with the face as it will read once the window sits on the owner's monitor (c = D, L0361 S4): the
        ' font assigned to this window above is read at ITS dpi until the platform catches up at the first move.
        Dim textBox As Size
        Using measureFont As New Font(owner.Font.FontFamily, CSng(OwnerFacePoints(owner, designScale)), FontStyle.Regular, GraphicsUnit.Point)
            textBox = TextRenderer.MeasureText(text.Text, measureFont, New Size(maxWidth - 2 * pad - bar, Integer.MaxValue), TextFlags())
        End Using
        Dim size As New Size(textBox.Width + 2 * pad + bar, textBox.Height + 2 * pad)
        Bounds = DialogPlacement.ToastBounds(mediaScreenRect, size, DesignPx(owner, 64, designScale))
        Opacity = 0.94
        hideTimer.Stop()
        hideTimer.Interval = durationMs
        If Not Visible Then Show(owner) Else Invalidate()
        hideTimer.Start()
    End Sub

    ''' <summary>Hides the toast now (a key was pressed, or its time is up).</summary>
    Friend Sub DismissNow()
        hideTimer.Stop()
        If Not IsDisposed AndAlso Visible Then Hide()
    End Sub

    Private Function TextFlags() As TextFormatFlags
        Dim flags As TextFormatFlags = TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix Or TextFormatFlags.VerticalCenter
        If rightToLeft_Text Then flags = flags Or TextFormatFlags.RightToLeft Or TextFormatFlags.Right
        Return flags
    End Function

    ''' <summary>A pixel length of the card: l x the owner's design factor, or the owner's dpi factor alone when the
    ''' caller gave none.</summary>
    Private Shared Function DesignPx(owner As Form, logical As Integer, designScale As Single) As Integer
        Return If(designScale > 0.0F, UiScale.Px(logical, designScale), owner.LogicalToDeviceUnits(logical))
    End Function

    ''' <summary>The face's nominal size at the owner's dpi: the owner's font, a notch larger, times the interface
    ''' multiplier the owner's font does not carry (design factor / the dpi factor already in the owner's point size).</summary>
    Private Shared Function OwnerFacePoints(owner As Form, designScale As Single) As Double
        Dim points As Double = owner.Font.SizeInPoints * 1.25R
        If designScale > 0.0F Then points *= designScale / FormScale.LayoutFactor(Math.Max(FormScale.LogicalDpi, owner.DeviceDpi))
        Return points
    End Function

    Private Function TextFont(owner As Form, designScale As Single) As Font
        ' The owner's own font, a notch larger: the script family chosen for the language is already on it.
        ' The owner's point size already carries the owner's D / S, while this window reads the font at ITS dpi (S until
        ' the window exists, D once it is in; the platform rescales it at the first move onto the monitor): the size is
        ' re-based from the owner's dpi to this window's (L0361 S4, point form; L0363 A-D10).
        Dim points As Single = CSng(FormScale.RebasePoints(OwnerFacePoints(owner, designScale), DeviceDpi, owner.DeviceDpi))
        Return New Font(owner.Font.FontFamily, points, FontStyle.Regular, GraphicsUnit.Point)
    End Function

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim g As Graphics = e.Graphics
        Dim pad As Integer = cardPad
        Dim bar As Integer = cardBar
        Dim barRect As Rectangle = If(rightToLeft_Text, New Rectangle(ClientSize.Width - bar, 0, bar, ClientSize.Height), New Rectangle(0, 0, bar, ClientSize.Height))
        Using brush As New SolidBrush(accent)
            g.FillRectangle(brush, barRect)
        End Using
        Dim textRect As New Rectangle(If(rightToLeft_Text, pad, bar + pad), pad, ClientSize.Width - bar - 2 * pad, ClientSize.Height - 2 * pad)
        TextRenderer.DrawText(g, message.Text, Font, textRect, CardInk, TextFlags())
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            hideTimer.Dispose()
            toastFont?.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class
#End If
