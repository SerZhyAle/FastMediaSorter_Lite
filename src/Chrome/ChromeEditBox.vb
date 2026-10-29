#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary
Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

''' <summary>What the embedded editor accepts: a position takes digits only, the page find takes any text.</summary>
Friend Enum ChromeEditMode
    Digits
    Text
End Enum

''' <summary>
''' The small real text editor a floating panel lays over one of its items for a moment (specification 280 section 4.2):
''' the page or chapter box and the HTML page find. A sibling of <see cref="ChromePathBox"/> with the same contract - a real
''' text control, so IME, selection, caret and right-to-left come from the system, and a mouse click never activates the
''' host except for a deliberate edit (the owner raises <see cref="EditingStarted"/> to allow it) - but a temporary one: it
''' is hidden until <see cref="BeginEdit"/> and hides again when the edit ends. Accept (Enter) is reported, never decided
''' here: the owner parses the value, acts on it and calls <see cref="EndEdit"/>. Escape and a loss of focus restore the old
''' value and end the edit.
''' </summary>
Friend NotInheritable Class ChromeEditBox
    Inherits Control
    Implements IChromeInkSource

    Private ReadOnly input As New TextBox With {.BorderStyle = BorderStyle.None, .ShortcutsEnabled = True}
    Private theme As ChromeTheme = ChromeTheme.FromScheme(Color.Black, Color.White, False)
    Private scaleFactor As Single = 1.0F
    Private rtl As Boolean
    Private textFont As Font
    Private editing As Boolean
    Private mode As ChromeEditMode = ChromeEditMode.Text
    Private savedText As String = ""

    ''' <summary>Enter was pressed. <see cref="AcceptedWithShift"/> tells whether Shift was held (the find's "previous match").</summary>
    Public Event Accepted As EventHandler
    Public Event EditingStarted As EventHandler
    Public Event EditingEnded As EventHandler

    ''' <summary>The longest position the box takes: a page number never needs more, and a longer paste is cut.</summary>
    Friend Const MaxDigits As Integer = 9
    Friend Const MaxFindLength As Integer = 256

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer, True)
        SetStyle(ControlStyles.Selectable, False)
        TabStop = False
        Visible = False
        AccessibleRole = AccessibleRole.Text
        Controls.Add(input)
        AddHandler input.KeyDown, AddressOf InputKeyDown
        AddHandler input.KeyPress, AddressOf InputKeyPress
        ' Editing ends when the box loses the keyboard (a click on the page, another window): the typed text is dropped
        ' like an Esc, exactly as the path box does.
        AddHandler input.LostFocus, Sub()
                                        If editing Then EndEdit(True)
                                    End Sub
    End Sub

    Public ReadOnly Property IsEditing As Boolean
        Get
            Return editing
        End Get
    End Property

    ''' <summary>What the editor holds right now.</summary>
    Public ReadOnly Property Value As String
        Get
            Return input.Text
        End Get
    End Property

    ''' <summary>The last Enter carried Shift.</summary>
    Public Property AcceptedWithShift As Boolean

    ''' <summary>The editor is the only thing painted here while it shows: the item under it is skipped by the ink layer
    ''' (its bounds are the item's), and the text control draws its own text.</summary>
    Private Sub InkModeChanged(external As Boolean) Implements IChromeInkSource.InkModeChanged
        Invalidate()
    End Sub

    Private Sub PaintInk(g As Graphics) Implements IChromeInkSource.PaintInk
    End Sub

    Public Sub Configure(newTheme As ChromeTheme, factor As Single, rightToLeft As Boolean, font As Font, name As String)
        theme = newTheme
        scaleFactor = factor
        rtl = rightToLeft
        If font IsNot Nothing AndAlso (textFont Is Nothing OrElse Not textFont.Equals(font)) Then
            Dim oldFont As Font = textFont
            textFont = DirectCast(font.Clone(), Font)
            input.Font = Nothing
            input.Font = textFont
            If oldFont IsNot Nothing Then oldFont.Dispose()
        End If
        input.ForeColor = theme.Foreground
        input.BackColor = theme.PanelFill
        input.RightToLeft = If(rtl, System.Windows.Forms.RightToLeft.Yes, System.Windows.Forms.RightToLeft.No)
        input.AccessibleName = name
        AccessibleName = name
        LayoutInput()
        Invalidate()
    End Sub

    ''' <summary>Lays the editor over <paramref name="area"/> (the item's bounds in the surface) and gives it the keyboard.</summary>
    Public Sub BeginEdit(area As Rectangle, initial As String, newMode As ChromeEditMode)
        If editing Then Return
        mode = newMode
        input.MaxLength = If(mode = ChromeEditMode.Digits, MaxDigits, MaxFindLength)
        savedText = If(initial, "")
        input.Text = savedText
        Bounds = area
        LayoutInput()
        Visible = True
        BringToFront()
        editing = True
        RaiseEvent EditingStarted(Me, EventArgs.Empty)
        input.Focus()
        input.SelectAll()
    End Sub

    ''' <summary>Follows the item when the panel was laid out again under the editor.</summary>
    Public Sub Follow(area As Rectangle)
        If Not editing OrElse Bounds = area Then Return
        Bounds = area
        LayoutInput()
    End Sub

    Public Sub EndEdit(Optional cancel As Boolean = True)
        If Not editing Then Return
        editing = False
        If cancel Then input.Text = savedText
        Visible = False
        RaiseEvent EditingEnded(Me, EventArgs.Empty)
    End Sub

    Private Sub InputKeyDown(sender As Object, args As KeyEventArgs)
        If args.KeyCode = Keys.Enter Then
            args.SuppressKeyPress = True
            AcceptedWithShift = args.Shift
            RaiseEvent Accepted(Me, EventArgs.Empty)
        ElseIf args.KeyCode = Keys.Escape Then
            args.SuppressKeyPress = True
            EndEdit(True)
        End If
    End Sub

    ''' <summary>A position box takes a typed character only when it is a decimal digit of some script (Arabic-Indic and
    ''' Devanagari digits are digits for their readers); a paste is not filtered here, the owner's parse answers it.</summary>
    Private Sub InputKeyPress(sender As Object, args As KeyPressEventArgs)
        If mode <> ChromeEditMode.Digits OrElse Char.IsControl(args.KeyChar) Then Return
        If Char.GetUnicodeCategory(args.KeyChar) <> UnicodeCategory.DecimalDigitNumber Then args.Handled = True
    End Sub

    Protected Overrides Sub OnResize(args As EventArgs)
        MyBase.OnResize(args)
        LayoutInput()
    End Sub

    Private Sub LayoutInput()
        If input Is Nothing Then Return
        Dim gap As Integer = UiScale.Px(ChromeMetrics.Gap, scaleFactor)
        input.Bounds = New Rectangle(gap, Math.Max(0, (Height - input.PreferredHeight) \ 2),
                                     Math.Max(0, Width - gap * 2), input.PreferredHeight)
    End Sub

    Protected Overrides Sub OnPaint(args As PaintEventArgs)
        args.Graphics.Clear(theme.PanelFill)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            If textFont IsNot Nothing Then textFont.Dispose() : textFont = Nothing
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
#End If
