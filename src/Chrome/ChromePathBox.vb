#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Friend NotInheritable Class ChromePathBox
    Inherits Control
    Implements IChromeInkSource
    Private inkExternal As Boolean
    Private ReadOnly input As New TextBox With {.BorderStyle = BorderStyle.None, .ShortcutsEnabled = True, .AllowDrop = True}
    Private ReadOnly debounce As New System.Windows.Forms.Timer With {.Interval = ChromeMetrics.CompletionDelayMilliseconds}
    Private ReadOnly tooltip As New ToolTip With {.ShowAlways = True}
    Private Shared ReadOnly completionGate As New SemaphoreSlim(1, 1)
    Private generation As Integer
    Private cancellation As CancellationTokenSource
    Private recent As String() = Array.Empty(Of String)()
    Private theme As ChromeTheme = ChromeTheme.FromScheme(Color.Black, Color.White, False)
    Private scaleFactor As Single = 1.0F
    Private rtl As Boolean
    Private symbols As Font
    Private textFont As Font
    Private symbolsFont As Font
    Private editing As Boolean
    Private savedText As String = ""
    Private updating As Boolean
    Private editHook As PathEditHook
    Public Event Accepted As EventHandler
    Public Event EditingStarted As EventHandler
    Public Event EditingEnded As EventHandler
    Public Event ContextRequested As EventHandler

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer, True)
        SetStyle(ControlStyles.Selectable, False)
        TabStop = False
        AccessibleRole = AccessibleRole.ComboBox
        input.Name = "cmbox_Media_Folder_Editor"
        Controls.Add(input)
        AddHandler input.MouseDown, Sub(sender, args)
                                        If args.Button = MouseButtons.Left Then BeginEditing()
                                    End Sub
        AddHandler input.KeyDown, AddressOf InputKeyDown
        ' Editing ends when the box loses the keyboard (a click on the picture, another window): the typed
        ' text is dropped like an Esc, and the tick may show the folder again (125 UX-303).
        AddHandler input.LostFocus, Sub()
                                        If editing Then EndEditing(True)
                                    End Sub
        AddHandler input.TextChanged, Sub()
                                          If updating OrElse Not editing Then Return
                                          ' The list on screen belongs to the text that was there a moment ago.
                                          If ChromeMenu.SuggestionListVisible Then ChromeMenu.DismissPopup()
                                          generation += 1
                                          CancelCompletion()
                                          debounce.Stop()
                                          debounce.Start()
                                      End Sub
        AddHandler debounce.Tick, AddressOf CompletePath
        AddHandler input.HandleCreated, Sub()
                                            If editHook IsNot Nothing Then editHook.ReleaseHandle()
                                            editHook = New PathEditHook(input.Handle, Sub() RaiseEvent ContextRequested(Me, EventArgs.Empty))
                                        End Sub
    End Sub

    Private NotInheritable Class PathEditHook
        Inherits NativeWindow
        Private ReadOnly requested As Action
        Public Sub New(handle As IntPtr, action As Action)
            requested = action
            AssignHandle(handle)
        End Sub
        Protected Overrides Sub WndProc(ByRef message As Message)
            If message.Msg = &H7B Then
                requested()
                Return
            End If
            MyBase.WndProc(message)
        End Sub
    End Class

    Public ReadOnly Property Editor As TextBox
        Get
            Return input
        End Get
    End Property
    Public Overrides Property Text As String
        Get
            Return input.Text
        End Get
        Set(value As String)
            If input.Text = value Then Return
            updating = True
            Try
                input.Text = value
            Finally
                updating = False
            End Try
            ' The text is the ink layer's while the box is not being edited: it repaints the panel.
            If inkExternal AndAlso Parent IsNot Nothing Then Parent.Invalidate()
        End Set
    End Property

    ''' <summary>While the panel is translucent its ink is drawn by the host's ink layer (opaque), so the
    ''' native box shows nothing of its own text until it is being edited: the text is painted in the
    ''' panel's fill colour, which is invisible against it.</summary>
    Private Sub InkModeChanged(external As Boolean) Implements IChromeInkSource.InkModeChanged
        inkExternal = external
        ApplyInputInk()
        Invalidate()
    End Sub

    Private Sub ApplyInputInk()
        input.ForeColor = If(inkExternal AndAlso Not editing, theme.PanelFill, theme.Foreground)
    End Sub

    Private Sub PaintInk(g As Graphics) Implements IChromeInkSource.PaintInk
        Dim drop As Integer = UiScale.Px(ChromeMetrics.ItemHeight, scaleFactor)
        Dim arrowArea As New RectangleF(If(rtl, 0, Width - drop), 0, drop, Height)
        Using brush As New SolidBrush(theme.Foreground)
            Using format As New StringFormat(StringFormat.GenericTypographic)
                format.Alignment = StringAlignment.Center
                format.LineAlignment = StringAlignment.Center
                g.DrawString(ChrW(&H25BE), If(symbols, input.Font), brush, arrowArea, format)
            End Using
            ' The right click opens the folder menu: the same corner mark the panel's other items carry (125 UX-306).
            ChromeSurface.PaintSecondaryMark(g, New Rectangle(0, 0, Width, Height), theme.Foreground, scaleFactor, rtl)
            If editing OrElse String.IsNullOrEmpty(input.Text) Then Return
            Using format As New StringFormat(StringFormat.GenericTypographic)
                format.FormatFlags = StringFormatFlags.NoWrap
                If rtl Then format.FormatFlags = format.FormatFlags Or StringFormatFlags.DirectionRightToLeft
                format.Alignment = StringAlignment.Near
                format.LineAlignment = StringAlignment.Center
                format.HotkeyPrefix = Drawing.Text.HotkeyPrefix.None
                Dim area As New RectangleF(input.Left, 0, input.Width, Height)
                Dim state As GraphicsState = g.Save()
                g.IntersectClip(area)
                g.DrawString(input.Text, input.Font, brush, area, format)
                g.Restore(state)
            End Using
        End Using
    End Sub
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public WriteOnly Property RecentFolders As IEnumerable(Of String)
        Set(value As IEnumerable(Of String))
            recent = If(value Is Nothing, Array.Empty(Of String)(), value.ToArray())
        End Set
    End Property

    Public Sub Configure(newTheme As ChromeTheme, factor As Single, rightToLeft As Boolean, font As Font, symbolFont As Font)
        theme = newTheme
        scaleFactor = factor
        rtl = rightToLeft
        If symbolFont IsNot Nothing AndAlso (symbolsFont Is Nothing OrElse Not symbolsFont.Equals(symbolFont)) Then
            Dim oldSymbols As Font = symbolsFont
            symbolsFont = DirectCast(symbolFont.Clone(), Font)
            If oldSymbols IsNot Nothing Then oldSymbols.Dispose()
        End If
        symbols = symbolsFont
        If font IsNot Nothing AndAlso (textFont Is Nothing OrElse Not textFont.Equals(font)) Then
            Dim oldFont As Font = textFont
            textFont = DirectCast(font.Clone(), Font)
            input.Font = Nothing
            input.Font = textFont
            If oldFont IsNot Nothing Then oldFont.Dispose()
        End If
        ApplyInputInk()
        input.BackColor = theme.PanelFill
        input.RightToLeft = If(rtl, System.Windows.Forms.RightToLeft.Yes, System.Windows.Forms.RightToLeft.No)
        input.AccessibleName = AccessibleName
        LayoutInput()
        Invalidate()
    End Sub
    Public Sub BeginEditing()
        If editing Then Return
        editing = True
        ApplyInputInk()
        savedText = Text
        RaiseEvent EditingStarted(Me, EventArgs.Empty)
        input.Focus()
        If inkExternal AndAlso Parent IsNot Nothing Then Parent.Invalidate()
    End Sub
    Public Sub SetTooltip(text As String)
        tooltip.SetToolTip(Me, text)
        tooltip.SetToolTip(input, text)
    End Sub
    Public Sub EndEditing(Optional cancel As Boolean = False)
        If Not editing Then Return
        debounce.Stop()
        generation += 1
        CancelCompletion()
        If ChromeMenu.SuggestionListVisible Then ChromeMenu.DismissPopup()
        If cancel Then Text = savedText
        editing = False
        ApplyInputInk()
        RaiseEvent EditingEnded(Me, EventArgs.Empty)
        If inkExternal AndAlso Parent IsNot Nothing Then Parent.Invalidate()
    End Sub
    Private Sub InputKeyDown(sender As Object, args As KeyEventArgs)
        If ChromeMenu.HandlePopupKey(args.KeyData) Then
            args.SuppressKeyPress = True
        ElseIf args.KeyCode = Keys.Enter Then
            args.SuppressKeyPress = True
            RaiseEvent Accepted(Me, EventArgs.Empty)
        ElseIf args.KeyCode = Keys.Escape Then
            args.SuppressKeyPress = True
            EndEditing(True)
        End If
    End Sub
    Private Sub CancelCompletion()
        If cancellation IsNot Nothing Then
            cancellation.Cancel()
            cancellation.Dispose()
            cancellation = Nothing
        End If
    End Sub
    Private Async Sub CompletePath(sender As Object, args As EventArgs)
        debounce.Stop()
        Dim request As Integer = generation
        Dim typed As String = Text
        CancelCompletion()
        cancellation = New CancellationTokenSource()
        Dim token As CancellationToken = cancellation.Token
        Try
            Await completionGate.WaitAsync(token)
            Dim matches As String()
            Try
                matches = Await Task.Run(Function() ChromePathCompletion.Find(typed, token), token)
            Finally
                completionGate.Release()
            End Try
            If IsDisposed OrElse Not editing OrElse request <> generation OrElse token.IsCancellationRequested Then Return
            If matches.Length > 0 Then ShowPaths(matches, False, isSuggestionList:=True)
        Catch ex As OperationCanceledException
        Catch ex As Exception
            AppFileLogger.LogException("Chrome path completion", ex)
        End Try
    End Sub
    ''' <summary>The completion list (isSuggestionList) is a helper of the text box: no row has the ring until an
    ''' arrow key asks, and every key that edits text stays the box's (125 UX-302). The recent-folders list, which
    ''' a click opens, is an ordinary menu, but a mouse opens it without a ring.</summary>
    Private Sub ShowPaths(paths As IEnumerable(Of String), accept As Boolean, Optional isSuggestionList As Boolean = False)
        Dim entries As New List(Of ChromeMenuItem)()
        For Each path In paths
            Dim selected As String = path
            entries.Add(New ChromeMenuItem(path) With {.Caption = path, .Tooltip = path,
                .Action = Sub()
                              Text = selected
                              If accept Then RaiseEvent Accepted(Me, EventArgs.Empty) Else input.Focus()
                          End Sub})
        Next
        Dim host As Form = FindForm()
        ChromeMenu.ShowPopup(If(host.Owner, host), PointToScreen(New Point(0, Height)), entries, theme, scaleFactor, rtl, input.Font, symbols,
                             focusFirstRow:=False, isSuggestionList:=isSuggestionList)
    End Sub
    Protected Overrides Sub OnMouseUp(args As MouseEventArgs)
        MyBase.OnMouseUp(args)
        If args.Button = MouseButtons.Right Then
            RaiseEvent ContextRequested(Me, EventArgs.Empty)
        ElseIf args.Button = MouseButtons.Left Then
            ShowPaths(recent, True)
        End If
    End Sub
    Protected Overrides Sub OnResize(args As EventArgs)
        MyBase.OnResize(args)
        LayoutInput()
    End Sub
    Private Sub LayoutInput()
        If input Is Nothing Then Return
        Dim gap As Integer = UiScale.Px(ChromeMetrics.Gap, scaleFactor)
        Dim drop As Integer = UiScale.Px(ChromeMetrics.ItemHeight, scaleFactor)
        input.Bounds = New Rectangle(If(rtl, drop, gap), Math.Max(gap, (Height - input.PreferredHeight) \ 2),
                                     Math.Max(0, Width - drop - gap * 2), input.PreferredHeight)
    End Sub
    Protected Overrides Sub OnPaint(args As PaintEventArgs)
        args.Graphics.Clear(theme.PanelFill)
        If inkExternal Then Return
        Dim drop As Integer = UiScale.Px(ChromeMetrics.ItemHeight, scaleFactor)
        Dim area As New Rectangle(If(rtl, 0, Width - drop), 0, drop, Height)
        TextRenderer.DrawText(args.Graphics, ChrW(&H25BE), If(symbols, input.Font), area, theme.Foreground,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
        args.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        ChromeSurface.PaintSecondaryMark(args.Graphics, New Rectangle(0, 0, Width, Height), theme.Foreground, scaleFactor, rtl)
    End Sub
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            tooltip.Dispose()
            debounce.Dispose()
            CancelCompletion()
            If editHook IsNot Nothing Then editHook.ReleaseHandle()
            If textFont IsNot Nothing Then textFont.Dispose() : textFont = Nothing
            If symbolsFont IsNot Nothing Then symbolsFont.Dispose() : symbolsFont = Nothing
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
#End If
