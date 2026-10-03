#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D

''' <summary>
''' A live text value drawn by the chrome (002 section 3.1, item kind Text): a read-only label
''' that shows things like the slideshow countdown, the file counter, the zoom factor. It is not
''' pressed, it only changes what it says, and it is the first item kind that is not a button.
''' </summary>
Friend NotInheritable Class ChromeText
    Inherits Control

    Private chromeTheme As ChromeTheme
    Private currentScale As Single = 1.0F
    Private isRightToLeft As Boolean
    Private textFont As Font
    Private symbolFont As Font
    
    Private currentText As String = ""
    Private m_textColor As Color = Color.Empty

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        SetStyle(ControlStyles.Selectable, False)
        TabStop = False
    End Sub

    ''' <summary>Configures the text with theme, scale, direction and fonts.</summary>
    Public Sub Configure(theme As ChromeTheme, scale As Single, rightToLeft As Boolean, font As Font, symbols As Font)
        chromeTheme = theme
        currentScale = scale
        isRightToLeft = rightToLeft
        textFont = font
        symbolFont = symbols
        Invalidate()
    End Sub

    ''' <summary>The text to display.</summary>
    Public Overrides Property [Text] As String
        Get
            Return currentText
        End Get
        Set(value As String)
            If currentText <> value Then
                currentText = value
                Invalidate()
            End If
        End Set
    End Property

    ''' <summary>The colour of the text; Color.Empty = use the theme's foreground.</summary>
    Public Property TextColor As Color
        Get
            Return m_textColor
        End Get
        Set(value As Color)
            If m_textColor <> value Then
                m_textColor = value
                Invalidate()
            End If
        End Set
    End Property

    ''' <summary>Whether to show the text.</summary>
    Public Property VisibleText As Boolean = True

    ''' <summary>Accessible name for screen readers.</summary>
    Public Shadows Property AccessibleName As String = ""

    Private Function Px(logical As Integer) As Integer
        Return UiScale.Px(logical, currentScale)
    End Function

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        
        If Not VisibleText OrElse String.IsNullOrEmpty(currentText) Then Return
        
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.PixelOffsetMode = PixelOffsetMode.HighQuality
        
        Dim ink As Color = If(m_textColor = Color.Empty OrElse m_textColor = Color.Transparent, chromeTheme.Foreground, m_textColor)
        Dim font As Font = textFont
        
        Dim flags As TextFormatFlags = TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or
                                   TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or
                                   TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPadding
        
        If isRightToLeft Then flags = flags Or TextFormatFlags.RightToLeft
        
        TextRenderer.DrawText(g, currentText, font, ClientRectangle, ink, flags)
    End Sub

    Protected Overrides Function CreateAccessibilityInstance() As AccessibleObject
        Return New TextAccessibleObject(Me)
    End Function

    ''' <summary>Accessible object for the text display.</summary>
    Private NotInheritable Class TextAccessibleObject
        Inherits Control.ControlAccessibleObject

        Private ReadOnly textControl As ChromeText

        Public Sub New(owner As ChromeText)
            MyBase.New(owner)
            textControl = owner
        End Sub

        Public Overrides Property Name As String
            Get
                Return If(String.IsNullOrEmpty(textControl.AccessibleName), textControl.Text, textControl.AccessibleName)
            End Get
            Set(value As String)
            End Set
        End Property

        Public Overrides ReadOnly Property Role As AccessibleRole
            Get
                Return AccessibleRole.StaticText
            End Get
        End Property

        Public Overrides Property Value As String
            Get
                Return textControl.Text
            End Get
            Set(value As String)
            End Set
        End Property

        Public Overrides ReadOnly Property State As AccessibleStates
            Get
                Dim s As AccessibleStates = MyBase.State
                If Not textControl.VisibleText Then s = s Or AccessibleStates.Invisible
                Return s
            End Get
        End Property
    End Class

End Class
#End If