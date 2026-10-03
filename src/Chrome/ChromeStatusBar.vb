#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D

''' <summary>
''' The status strip at the bottom (002 section 2.6 item 26): one floating strip, auto-hiding,
''' with the same two fields as today's panel_Status - lbl_Current_File and lbl_Status.
''' </summary>
Friend NotInheritable Class ChromeStatusBar
    Inherits Control

    Private ReadOnly textControls As New List(Of ChromeText)()
    Private ReadOnly m_leftText As New ChromeText()
    Private ReadOnly m_rightText As New ChromeText()
    
    Private chromeTheme As ChromeTheme
    Private currentScale As Single = 1.0F
    Private isRightToLeft As Boolean
    Private textFont As Font
    Private symbolFont As Font

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        SetStyle(ControlStyles.Selectable, False)
        TabStop = False
        
        textControls.Add(m_leftText)
        textControls.Add(m_rightText)
        
        Controls.Add(m_leftText)
        Controls.Add(m_rightText)
    End Sub

    ''' <summary>Configures the status bar with theme, scale, direction and fonts.</summary>
    Public Sub Configure(theme As ChromeTheme, scale As Single, rightToLeft As Boolean, font As Font, symbols As Font)
        chromeTheme = theme
        currentScale = scale
        isRightToLeft = rightToLeft
        textFont = font
        symbolFont = symbols
        
        m_leftText.Configure(theme, scale, rightToLeft, font, symbols)
        m_rightText.Configure(theme, scale, rightToLeft, font, symbols)
        
        Invalidate()
    End Sub

    ''' <summary>The left text (current file name).</summary>
    Public Property LeftText As String
        Get
            Return m_leftText.Text
        End Get
        Set(value As String)
            m_leftText.Text = value
        End Set
    End Property

    ''' <summary>The right text (status message).</summary>
    Public Property RightText As String
        Get
            Return m_rightText.Text
        End Get
        Set(value As String)
            m_rightText.Text = value
        End Set
    End Property

    ''' <summary>Left text colour.</summary>
    Public Property LeftTextColor As Color
        Get
            Return m_leftText.TextColor
        End Get
        Set(value As Color)
            m_leftText.TextColor = value
        End Set
    End Property

    ''' <summary>Right text colour.</summary>
    Public Property RightTextColor As Color
        Get
            Return m_rightText.TextColor
        End Get
        Set(value As Color)
            m_rightText.TextColor = value
        End Set
    End Property

    Private Function Px(logical As Integer) As Integer
        Return UiScale.Px(logical, currentScale)
    End Function

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        LayoutControls()
    End Sub

    Private Sub LayoutControls()
        Dim padding As Integer = Px(ChromeMetrics.PanelPadding)
        Dim textHeight As Integer = Px(ChromeMetrics.ItemHeight)
        Dim y As Integer = (Height - textHeight) \ 2
        
        ' Left text gets 2/3 of the width, right text gets 1/3
        Dim leftWidth As Integer = (Width - 3 * padding) * 2 \ 3
        Dim rightWidth As Integer = Width - leftWidth - 3 * padding
        
        If isRightToLeft Then
            m_leftText.Bounds = New Rectangle(padding, y, rightWidth, textHeight)
            m_rightText.Bounds = New Rectangle(2 * padding + rightWidth, y, leftWidth, textHeight)
            m_rightText.Text = RightText ' Swap for RTL
            m_leftText.Text = LeftText
        Else
            m_leftText.Bounds = New Rectangle(padding, y, leftWidth, textHeight)
            m_rightText.Bounds = New Rectangle(2 * padding + leftWidth, y, rightWidth, textHeight)
        End If
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.PixelOffsetMode = PixelOffsetMode.HighQuality
        
        Dim clientRect As New Rectangle(0, 0, Width, Height)
        Dim corner As Integer = Px(ChromeMetrics.PanelCorner)
        
        ' Draw the background
        Using path As GraphicsPath = ChromeSurface.RoundedRect(clientRect, corner)
            Using brush As New SolidBrush(chromeTheme.PanelFill)
                g.FillPath(brush, path)
            End Using
        End Using
        
        ' Draw the border
        Using path As GraphicsPath = ChromeSurface.RoundedRect(clientRect, corner)
            Using pen As New Pen(chromeTheme.Hairline, 1.0F)
                g.DrawPath(pen, path)
            End Using
        End Using
    End Sub

    Protected Overrides Function CreateAccessibilityInstance() As AccessibleObject
        Return New StatusBarAccessibleObject(Me)
    End Function

    ''' <summary>Accessible object for the status bar.</summary>
    Private NotInheritable Class StatusBarAccessibleObject
        Inherits Control.ControlAccessibleObject

        Private ReadOnly statusBar As ChromeStatusBar

        Public Sub New(owner As ChromeStatusBar)
            MyBase.New(owner)
            statusBar = owner
        End Sub

        Public Overrides ReadOnly Property Role As AccessibleRole
            Get
                Return AccessibleRole.StatusBar
            End Get
        End Property

        Public Overrides Property Name As String
            Get
                Return statusBar.LeftText & " " & statusBar.RightText
            End Get
            Set(value As String)
            End Set
        End Property

        Public Overrides Function GetChildCount() As Integer
            Return 2 ' Left and right text
        End Function

        Public Overrides Function GetChild(index As Integer) As AccessibleObject
            If index = 0 Then Return statusBar.m_leftText.AccessibilityObject
            If index = 1 Then Return statusBar.m_rightText.AccessibilityObject
            Return Nothing
        End Function
    End Class

End Class
#End If