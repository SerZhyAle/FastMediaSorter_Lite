#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary
Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Friend NotInheritable Class ChromePathBox
    Inherits Control
    Private ReadOnly input As New TextBox With {.BorderStyle = BorderStyle.None, .ShortcutsEnabled = True, .AllowDrop = True}
    Private ReadOnly debounce As New System.Windows.Forms.Timer With {.Interval = ChromeMetrics.CompletionDelayMilliseconds}
    Private Shared ReadOnly completionGate As New SemaphoreSlim(1, 1)
    Private generation As Integer
    Private cancellation As CancellationTokenSource
    Private recent As String() = Array.Empty(Of String)()
    Private theme As ChromeTheme = ChromeTheme.FromScheme(Color.Black, Color.White, False)
    Private scaleFactor As Single = 1.0F
    Private rtl As Boolean
    Private symbols As Font
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
        AddHandler input.TextChanged, Sub()
                                          If updating OrElse Not editing Then Return
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
        End Set
    End Property
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
        symbols = symbolFont
        input.Font = font
        input.ForeColor = theme.Foreground
        input.BackColor = theme.PanelFill
        input.RightToLeft = If(rtl, System.Windows.Forms.RightToLeft.Yes, System.Windows.Forms.RightToLeft.No)
        input.AccessibleName = AccessibleName
        LayoutInput()
        Invalidate()
    End Sub
    Public Sub BeginEditing()
        If editing Then Return
        editing = True
        savedText = Text
        RaiseEvent EditingStarted(Me, EventArgs.Empty)
        input.Focus()
    End Sub
    Public Sub EndEditing(Optional cancel As Boolean = False)
        If Not editing Then Return
        debounce.Stop()
        generation += 1
        CancelCompletion()
        If cancel Then Text = savedText
        editing = False
        RaiseEvent EditingEnded(Me, EventArgs.Empty)
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
            If matches.Length > 0 Then ShowPaths(matches, False)
        Catch ex As OperationCanceledException
        Catch ex As Exception
            AppFileLogger.LogException("Chrome path completion", ex)
        End Try
    End Sub
    Private Sub ShowPaths(paths As IEnumerable(Of String), accept As Boolean)
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
        ChromeMenu.ShowPopup(If(host.Owner, host), PointToScreen(New Point(0, Height)), entries, theme, scaleFactor, rtl, input.Font, symbols)
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
        Dim drop As Integer = UiScale.Px(ChromeMetrics.ItemHeight, scaleFactor)
        Dim area As New Rectangle(If(rtl, 0, Width - drop), 0, drop, Height)
        TextRenderer.DrawText(args.Graphics, ChrW(&H25BE), If(symbols, input.Font), area, theme.Foreground,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
    End Sub
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            debounce.Dispose()
            CancelCompletion()
            If editHook IsNot Nothing Then editHook.ReleaseHandle()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
#End If
