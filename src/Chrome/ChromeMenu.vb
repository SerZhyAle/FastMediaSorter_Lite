#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

' All popups share ChromeHost's non-activation and region behavior. The message filter
' routes deliberate menu navigation from the active viewer without activating a popup.
Friend NotInheritable Class ChromeMenu
    Inherits ChromeHost
    Implements IMessageFilter

    Public Shared Event FocusKeyConsumed As Action
    Private Shared popup As ChromeMenu
    Private ReadOnly entries As New List(Of ChromeMenuItem)()
    Private menuFont As Font
    Private symbolFont As Font
    Private viewer As Form
    Private scrollOffset As Integer
    Public Event MenuItemClicked As EventHandler(Of ChromeMenuItem)

    Public Sub New()
        Surface.VerticalLayout = True
        Surface.Dock = DockStyle.None
        AddHandler Surface.ItemInvoked, AddressOf ItemInvoked
        AddHandler Surface.MouseWheel, Sub(sender, args) ScrollBy(-Math.Sign(args.Delta) * Surface.ItemHeight())
    End Sub

    Public Shared ReadOnly Property PopupVisible As Boolean
        Get
            Return popup IsNot Nothing AndAlso Not popup.IsDisposed AndAlso popup.Visible
        End Get
    End Property

    Public Shared Sub DismissPopup()
        Dim previous As ChromeMenu = popup
        popup = Nothing
        If previous IsNot Nothing Then
            previous.CloseMenu()
            If previous.IsHandleCreated Then previous.BeginInvoke(New Action(Sub() previous.Dispose())) Else previous.Dispose()
        End If
    End Sub

    Public Shared Sub ShowPopup(owner As Form, location As Point, items As IEnumerable(Of ChromeMenuItem),
                                theme As ChromeTheme, scale As Single, rtl As Boolean, font As Font, symbols As Font)
        DismissPopup()
        Dim menu As New ChromeMenu()
        menu.Configure(theme, scale, rtl, font, symbols)
        menu.SetItems(items)
        If menu.entries.Count = 0 Then
            menu.Dispose()
            Return
        End If
        popup = menu
        menu.ShowMenu(owner, location)
    End Sub

    Public Sub Configure(theme As ChromeTheme, scale As Single, rtl As Boolean, font As Font, symbols As Font)
        If menuFont IsNot Nothing Then menuFont.Dispose()
        If symbolFont IsNot Nothing Then symbolFont.Dispose()
        menuFont = DirectCast(font.Clone(), Font)
        symbolFont = DirectCast(symbols.Clone(), Font)
        BackColor = theme.PanelFill
        Opacity = theme.PanelOpacity
        SetCornerRadius(UiScale.Px(theme.PanelCornerRadius, scale))
        Surface.Configure(theme, scale, rtl, menuFont, symbolFont)
        Surface.SetAnimationEnabled(False)
    End Sub

    Public Sub SetItems(items As IEnumerable(Of ChromeMenuItem))
        entries.Clear()
        If items IsNot Nothing Then entries.AddRange(items.Where(Function(entry) entry.Visible))
        Dim painted As New List(Of ChromeItem)()
        For Each entry In entries
            Dim source As ChromeMenuItem = entry
            painted.Add(New ChromeItem(source.Id, If(source.Checked, ChromeItemKind.Toggle, ChromeItemKind.Button)) With {
                .Caption = source.Caption, .AccessibleName = If(source.AccessibleName.Length > 0, source.AccessibleName, source.Caption),
                .Tooltip = If(source.Tooltip.Length > 0, source.Tooltip, source.Caption), .Enabled = source.Enabled, .Checked = source.Checked, .Destructive = source.Destructive,
                .Tag = source, .Image = source.Image, .GlyphId = source.GlyphId, .Action = Sub() InvokeEntry(source)})
        Next
        Surface.SetItems(painted)
    End Sub

    Private Sub InvokeEntry(entry As ChromeMenuItem)
        ' Hide before the action: language changes rebuild the controller's fonts and
        ' dialogs must be able to activate. Dispose after the surface finishes dispatch.
        Hide()
        Application.RemoveMessageFilter(Me)
        entry.Action?.Invoke()
        RaiseEvent MenuItemClicked(Me, entry)
    End Sub

    Private Sub ItemInvoked(sender As Object, item As ChromeItem)
        If IsHandleCreated Then BeginInvoke(New Action(Sub() Dispose()))
    End Sub

    Public Sub ShowMenu(owner As Control, location As Point)
        viewer = owner.FindForm()
        If TypeOf owner Is Form Then viewer = DirectCast(owner, Form)
        Me.Owner = viewer
        TopMost = viewer IsNot Nothing AndAlso viewer.TopMost
        Dim work As Rectangle = Screen.FromPoint(location).WorkingArea
        Dim preferred As Size = Surface.PreferredPanelSize()
        Dim width As Integer = Math.Min(work.Width, Math.Max(UiScale.Px(ChromeMetrics.MenuMinWidth, Surface.ScaleFactor), preferred.Width))
        Dim height As Integer = Math.Min(work.Height, preferred.Height)
        Bounds = New Rectangle(Math.Max(work.Left, Math.Min(location.X, work.Right - width)),
                               Math.Max(work.Top, Math.Min(location.Y, work.Bottom - height)), width, height)
        Surface.Bounds = New Rectangle(0, 0, width, preferred.Height)
        Application.AddMessageFilter(Me)
        Show()
        Surface.EnterKeyboardMode()
    End Sub

    Public Sub CloseMenu()
        Hide()
        Application.RemoveMessageFilter(Me)
    End Sub

    Public Shared Function HandlePopupKey(keyData As Keys) As Boolean
        If Not PopupVisible Then Return False
        Return popup.HandleKey(keyData)
    End Function

    Private Function HandleKey(keyData As Keys) As Boolean
        Dim code As Keys = keyData And Keys.KeyCode
        If (keyData And (Keys.Control Or Keys.Alt)) <> Keys.None Then Return False
        Select Case code
            Case Keys.Escape, Keys.F10
                If code = Keys.F10 Then RaiseEvent FocusKeyConsumed()
                CloseMenu()
            Case Keys.Down, Keys.Tab
                If Not Surface.MoveFocus((keyData And Keys.Shift) = Keys.None) Then Surface.EnterKeyboardMode((keyData And Keys.Shift) <> Keys.None)
                EnsureFocusVisible()
            Case Keys.Up
                If Not Surface.MoveFocus(False) Then Surface.EnterKeyboardMode(True)
                EnsureFocusVisible()
            Case Keys.Home, Keys.End
                Surface.EnterKeyboardMode(code = Keys.End)
                EnsureFocusVisible()
            Case Keys.Enter, Keys.Space
                Surface.ActivateFocused()
            Case Else
                ' Viewer sorting/navigation must not run while a menu is being browsed.
        End Select
        Return True
    End Function

    Public Function PreFilterMessage(ByRef message As Message) As Boolean Implements IMessageFilter.PreFilterMessage
        If Not Visible Then Return False
        Dim active As Form = Form.ActiveForm
        If Not viewer.ContainsFocus AndAlso Not viewer.OwnedForms.Any(Function(form) form.ContainsFocus) AndAlso active IsNot viewer AndAlso (active Is Nothing OrElse active.Owner IsNot viewer) Then
            CloseMenu()
            Return False
        End If
        Select Case message.Msg
            Case &H100, &H104
                Return HandleKey(CType(message.WParam.ToInt32(), Keys) Or Control.ModifierKeys)
            Case &H201, &H204, &H207, &HA1, &HA4
                If Not Bounds.Contains(Cursor.Position) Then CloseMenu()
        End Select
        Return False
    End Function

    Private Sub EnsureFocusVisible()
        For Each item In Surface.Items
            If Surface.IsFocusedItem(item) Then
                If item.Bounds.Top < scrollOffset Then scrollOffset = item.Bounds.Top
                If item.Bounds.Bottom > scrollOffset + ClientSize.Height Then scrollOffset = item.Bounds.Bottom - ClientSize.Height
                ScrollBy(0)
                Exit For
            End If
        Next
    End Sub

    Private Sub ScrollBy(delta As Integer)
        scrollOffset = Math.Max(0, Math.Min(Math.Max(0, Surface.Height - ClientSize.Height), scrollOffset + delta))
        Surface.Top = -scrollOffset
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            Application.RemoveMessageFilter(Me)
            If popup Is Me Then popup = Nothing
            MyBase.Dispose(disposing)
            If menuFont IsNot Nothing Then menuFont.Dispose() : menuFont = Nothing
            If symbolFont IsNot Nothing Then symbolFont.Dispose() : symbolFont = Nothing
            Return
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class

Friend NotInheritable Class ChromeMenuItem
    Public Sub New(id As String)
        Me.Id = id
    End Sub
    Public ReadOnly Property Id As String
    Public Property GlyphId As String = ""
    Public Property Caption As String = ""
    Public Property CaptionIsSymbol As Boolean
    Public Property AccessibleName As String = ""
    Public Property Tooltip As String = ""
    Public Property Enabled As Boolean = True
    Public Property Visible As Boolean = True
    Public Property Checked As Boolean
    Public Property Destructive As Boolean
    Public Property Image As Image
    Public Property Tag As Object
    Public Property Action As Action
End Class
#End If

