#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' All popups share ChromeHost's non-activation and region behavior. The message filter
' routes deliberate menu navigation from the active viewer without activating a popup,
' and dismisses the menu when activation moves to a window that is neither the viewer
' nor one of its owned forms - a change, not a state, because the panels are no-activate
' and a menu opened from a background viewer is legitimate.
'
' A menu can have a submenu (125 UX-322): a row with a chevron opens a second ChromeMenu BESIDE the
' row, the parent stays on screen, Left / Esc return one level and Right / Enter open. The chain is
' one thing to the outside: the ROOT's filter decides dismissal for the whole chain (a click or the
' wheel outside every menu of it, a change of the foreground window) and routes every key to the
' LEAF, the one level that has the keyboard. What a key means is decided by ChromeMenuRules.
Friend NotInheritable Class ChromeMenu
    Inherits ChromeHost
    Implements IMessageFilter

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    ''' <summary>MAPVK_VK_TO_CHAR (2): the character a virtual key types on the active keyboard layout.</summary>
    <DllImport("user32.dll", EntryPoint:="MapVirtualKeyW")>
    Private Shared Function MapVirtualKey(code As UInteger, mapType As UInteger) As UInteger
    End Function

    Public Shared Event FocusKeyConsumed As Action
    ''' <summary>The leaf of the open chain: the menu that has the keyboard.</summary>
    Private Shared popup As ChromeMenu
    Private ReadOnly entries As New List(Of ChromeMenuItem)()
    Private menuFont As Font
    Private symbolFont As Font
    Private menuTheme As ChromeTheme
    Private menuScale As Single = 1.0F
    Private menuRtl As Boolean
    Private viewer As Form
    Private scrollOffset As Integer
    Private openForeground As IntPtr
    Private parentMenu As ChromeMenu
    Private childMenu As ChromeMenu
    ''' <summary>The path-suggestion list of the path box: typing stays the text box's (125 UX-302).</summary>
    Private suggestionList As Boolean
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

    ''' <summary>The open popup is the path box's suggestion list (so a click or key elsewhere in the box
    ''' may dismiss it without being a menu choice).</summary>
    Public Shared ReadOnly Property SuggestionListVisible As Boolean
        Get
            Return PopupVisible AndAlso popup.suggestionList
        End Get
    End Property

    ''' <summary>How many levels of the open chain are on screen (0 = none): the root plus its submenus.</summary>
    Friend Shared ReadOnly Property OpenLevels As Integer
        Get
            Dim levels As Integer = 0
            Dim current As ChromeMenu = popup
            While current IsNot Nothing
                If Not current.IsDisposed AndAlso current.Visible Then levels += 1
                current = current.parentMenu
            End While
            Return levels
        End Get
    End Property

    ''' <summary>Where the menus of the open chain are, root first (screen coordinates).</summary>
    Friend Shared Function OpenBounds() As List(Of Rectangle)
        Dim result As New List(Of Rectangle)()
        Dim current As ChromeMenu = popup
        While current IsNot Nothing
            If Not current.IsDisposed AndAlso current.Visible Then result.Insert(0, current.Bounds)
            current = current.parentMenu
        End While
        Return result
    End Function

    ''' <summary>The row of the open leaf menu that has the keyboard ring, or Nothing.</summary>
    Friend Shared ReadOnly Property FocusedRowOfLeaf As ChromeItem
        Get
            Return If(PopupVisible, popup.Surface.FocusedRow, Nothing)
        End Get
    End Property

    ''' <summary>Closes and disposes the whole chain: the open menu and every parent above it.</summary>
    Public Shared Sub DismissPopup()
        Dim leaf As ChromeMenu = popup
        popup = Nothing
        Dim current As ChromeMenu = leaf
        While current IsNot Nothing
            Dim up As ChromeMenu = current.parentMenu
            current.parentMenu = Nothing
            current.childMenu = Nothing
            current.CloseMenu()
            Dim disposing As ChromeMenu = current
            If disposing.IsHandleCreated Then disposing.BeginInvoke(New Action(Sub() disposing.Dispose())) Else disposing.Dispose()
            current = up
        End While
    End Sub

    ''' <param name="focusFirstRow">The keyboard opened it: the first row has the ring at once. A menu opened
    ''' by the mouse shows no ring beside the hover until an arrow key asks for one (125 UX-313j).</param>
    ''' <param name="isSuggestionList">The path box's suggestion list: no row has the ring until an arrow key puts
    ''' it there, and every key that edits text stays the text box's (125 UX-302).</param>
    Public Shared Sub ShowPopup(owner As Form, location As Point, items As IEnumerable(Of ChromeMenuItem),
                                theme As ChromeTheme, scale As Single, rtl As Boolean, font As Font, symbols As Font,
                                Optional focusFirstRow As Boolean = True, Optional isSuggestionList As Boolean = False,
                                Optional anchorTop As Integer = Integer.MinValue)
        DismissPopup()
        Dim menu As New ChromeMenu()
        menu.Configure(theme, scale, rtl, font, symbols)
        menu.SetItems(items)
        If menu.entries.Count = 0 Then
            menu.Dispose()
            Return
        End If
        menu.suggestionList = isSuggestionList
        popup = menu
        menu.ShowMenu(owner, location, focusFirstRow AndAlso Not isSuggestionList, anchorTop)
    End Sub

    Public Sub Configure(theme As ChromeTheme, scale As Single, rtl As Boolean, font As Font, symbols As Font)
        If menuFont IsNot Nothing Then menuFont.Dispose()
        If symbolFont IsNot Nothing Then symbolFont.Dispose()
        menuFont = DirectCast(font.Clone(), Font)
        symbolFont = DirectCast(symbols.Clone(), Font)
        menuTheme = theme
        menuScale = scale
        menuRtl = rtl
        BackColor = theme.PanelFill
        ' A popup is something the user is choosing from right now: it stays at least 40 % solid even
        ' when the floating panels are set fully see-through.
        Opacity = Math.Max(0.4R, theme.PanelOpacity)
        ' A translucent menu keeps its captions and glyphs fully opaque (the ink layer).
        UseInkLayer = Not theme.IsHighContrast AndAlso theme.PanelOpacity < 0.999R
        SetCornerRadius(UiScale.Px(theme.PanelCornerRadius, scale))
        Surface.Configure(theme, scale, rtl, menuFont, symbolFont)
        Surface.SetAnimationEnabled(False)
    End Sub

    Public Sub SetItems(items As IEnumerable(Of ChromeMenuItem))
        entries.Clear()
        If items IsNot Nothing Then
            ' A separator with nothing on one side of it is noise: leading, trailing and doubled ones go.
            entries.AddRange(ChromeMenuRules.TidySeparators(items.Where(Function(entry) entry.Visible).ToList(), Function(entry) entry.IsSeparator))
        End If
        Dim painted As New List(Of ChromeItem)()
        For Each entry In entries
            Dim source As ChromeMenuItem = entry
            Dim row As New ChromeItem(source.Id, If(source.Checked, ChromeItemKind.Toggle, ChromeItemKind.Button)) With {
                .IsSeparator = source.IsSeparator,
                .Caption = source.Caption, .AccessibleName = If(source.AccessibleName.Length > 0, source.AccessibleName, source.Caption),
                .Tooltip = If(Not source.Enabled AndAlso source.DisabledReason.Length > 0, source.DisabledReason,
                              If(source.Tooltip.Length > 0, source.Tooltip, source.Caption)),
                .Enabled = source.Enabled AndAlso Not source.IsSeparator, .Checked = source.Checked, .Destructive = source.Destructive,
                .Hint = source.Hint, .Chevron = source.HasSubmenu,
                .Tag = source, .Image = source.Image, .GlyphId = source.GlyphId}
            If source.HasSubmenu Then
                row.Action = Sub() OpenSubmenu(source, row, False)
            Else
                row.Action = Sub() InvokeEntry(source)
            End If
            painted.Add(row)
        Next
        Surface.SetItems(painted)
    End Sub

    Private Sub InvokeEntry(entry As ChromeMenuItem)
        ' Hide before the action: language changes rebuild the controller's fonts and
        ' dialogs must be able to activate. Dispose after the surface finishes dispatch. A choice in a
        ' submenu closes the whole chain, not just its own level.
        HideChain()
        entry.Action?.Invoke()
        RaiseEvent MenuItemClicked(Me, entry)
    End Sub

    Private Sub ItemInvoked(sender As Object, item As ChromeItem)
        ' A row that opens a submenu does not end the menu.
        Dim entry As ChromeMenuItem = TryCast(item.Tag, ChromeMenuItem)
        If entry IsNot Nothing AndAlso entry.HasSubmenu Then Return
        If IsHandleCreated Then BeginInvoke(New Action(AddressOf DisposeChainAfterDispatch))
    End Sub

    ''' <summary>Disposes every menu of this chain, once the click that closed it has finished dispatching.</summary>
    Private Sub DisposeChainAfterDispatch()
        Dim root As ChromeMenu = Me
        While root.parentMenu IsNot Nothing
            root = root.parentMenu
        End While
        Dim current As ChromeMenu = root
        While current IsNot Nothing
            Dim nextMenu As ChromeMenu = current.childMenu
            If popup Is current Then popup = Nothing
            current.Dispose()
            current = nextMenu
        End While
    End Sub

    Public Sub ShowMenu(owner As Control, location As Point, Optional focusFirstRow As Boolean = True, Optional anchorTop As Integer = Integer.MinValue)
        viewer = owner.FindForm()
        If TypeOf owner Is Form Then viewer = DirectCast(owner, Form)
        Me.Owner = viewer
        TopMost = viewer IsNot Nothing AndAlso viewer.TopMost
        Dim work As Rectangle = Screen.FromPoint(location).WorkingArea
        Dim preferred As Size = Surface.PreferredPanelSize()
        Dim width As Integer = Math.Min(work.Width, Math.Max(UiScale.Px(ChromeMetrics.MenuMinWidth, Surface.ScaleFactor), preferred.Width))
        Dim height As Integer = Math.Min(work.Height, preferred.Height)
        Dim origin As Point = ChromeMenuRules.PopupOrigin(location, New Size(width, height), work, anchorTop)
        Bounds = New Rectangle(origin, New Size(width, height))
        Surface.Bounds = New Rectangle(0, 0, width, preferred.Height)
        Surface.SetViewport(scrollOffset, ClientSize.Height)
        openForeground = GetForegroundWindow()
        Application.AddMessageFilter(Me)
        Show()
        If focusFirstRow Then Surface.EnterKeyboardMode()
    End Sub

    ''' <summary>Opens the submenu of a row beside it (125 UX-322): level with the row, on the side the reading
    ''' direction opens to, the parent left where it is. Replaces a submenu already open.</summary>
    Private Sub OpenSubmenu(entry As ChromeMenuItem, row As ChromeItem, focusFirstRow As Boolean)
        CloseChild()
        If entry.Submenu Is Nothing OrElse viewer Is Nothing Then Return
        Dim child As New ChromeMenu()
        child.parentMenu = Me
        child.Configure(menuTheme, menuScale, menuRtl, menuFont, symbolFont)
        child.SetItems(entry.Submenu)
        If child.entries.Count = 0 Then
            child.parentMenu = Nothing
            child.Dispose()
            Return
        End If
        childMenu = child
        popup = child
        Dim rowTop As Integer = Surface.PointToScreen(New Point(0, row.Bounds.Top)).Y - Surface.PanelPadding()
        Dim work As Rectangle = Screen.FromRectangle(Bounds).WorkingArea
        Dim preferred As Size = child.Surface.PreferredPanelSize()
        Dim size As New Size(Math.Min(work.Width, Math.Max(UiScale.Px(ChromeMetrics.MenuMinWidth, menuScale), preferred.Width)), Math.Min(work.Height, preferred.Height))
        Dim origin As Point = ChromeMenuRules.SubmenuOrigin(Bounds, rowTop, size, work, menuRtl, UiScale.Px(ChromeMetrics.SubmenuOverlap, menuScale))
        child.ShowMenu(viewer, origin, focusFirstRow)
    End Sub

    ''' <summary>Closes the submenu of this level, if one is open; the keys come back to this level.</summary>
    Private Sub CloseChild()
        If childMenu Is Nothing Then Return
        Dim gone As ChromeMenu = childMenu
        childMenu = Nothing
        gone.parentMenu = Nothing
        If gone.childMenu IsNot Nothing Then gone.CloseChild()
        gone.CloseMenu()
        If gone.IsHandleCreated Then gone.BeginInvoke(New Action(Sub() gone.Dispose())) Else gone.Dispose()
        popup = Me
    End Sub

    Public Sub CloseMenu()
        Hide()
        Application.RemoveMessageFilter(Me)
    End Sub

    ''' <summary>Left / Esc inside a submenu: close this level and give the keyboard back to the parent.
    ''' On a root menu there is nothing above, so it closes.</summary>
    Private Sub CloseThisLevel()
        If parentMenu Is Nothing Then
            CloseMenu()
            Return
        End If
        parentMenu.CloseChild()
    End Sub

    ''' <summary>Hides every menu of the chain (they are disposed after the dispatch that closed them).</summary>
    Private Sub HideChain()
        Dim root As ChromeMenu = Me
        While root.parentMenu IsNot Nothing
            root = root.parentMenu
        End While
        Dim current As ChromeMenu = root
        While current IsNot Nothing
            current.CloseMenu()
            current = current.childMenu
        End While
    End Sub

    Private Function RootMenu() As ChromeMenu
        Dim root As ChromeMenu = Me
        While root.parentMenu IsNot Nothing
            root = root.parentMenu
        End While
        Return root
    End Function

    Private Function ChainContains(point As Point) As Boolean
        Dim current As ChromeMenu = RootMenu()
        While current IsNot Nothing
            If current.Visible AndAlso current.Bounds.Contains(point) Then Return True
            current = current.childMenu
        End While
        Return False
    End Function

    Public Shared Function HandlePopupKey(keyData As Keys) As Boolean
        If Not PopupVisible Then Return False
        Return popup.HandleKey(keyData)
    End Function

    ''' <summary>What the keys are to this level - the facts <see cref="ChromeMenuRules.ResolveKey"/> decides on.</summary>
    Private Function KeyContext(keyData As Keys) As MenuKeyContext
        Dim count As Integer = Surface.Items.Count
        Dim accelerators(count - 1) As Char
        Dim initials(count - 1) As Char
        Dim enabled(count - 1) As Boolean
        Dim focused As Integer = -1
        For index As Integer = 0 To count - 1
            Dim item As ChromeItem = Surface.Items(index)
            Dim entry As ChromeMenuItem = TryCast(item.Tag, ChromeMenuItem)
            accelerators(index) = If(entry Is Nothing, ChrW(0), entry.Accelerator)
            initials(index) = If(item.IsSeparator, ChrW(0), ChromeMenuRules.InitialOf(item.Caption))
            enabled(index) = item.Enabled AndAlso Not item.IsSeparator
            If Surface.IsFocusedItem(item) Then focused = index
        Next
        Dim focusedEntry As ChromeMenuItem = If(focused >= 0, TryCast(Surface.Items(focused).Tag, ChromeMenuItem), Nothing)
        ' High bit set = a dead key: not a letter to jump to.
        Dim typed As UInteger = MapVirtualKey(CUInt(keyData And Keys.KeyCode), 2UI)
        Return New MenuKeyContext With {
            .TypedChar = If(typed = 0UI OrElse typed > &HFFFFUI OrElse (typed And &H80000000UI) <> 0UI, ChrW(0), ChrW(CInt(typed))),
            .IsSuggestionList = suggestionList, .IsSubmenu = parentMenu IsNot Nothing, .RightToLeft = menuRtl,
            .HasFocusedRow = focused >= 0, .FocusedIndex = focused,
            .FocusedRowOpensSubmenu = focusedEntry IsNot Nothing AndAlso focusedEntry.HasSubmenu,
            .Accelerators = accelerators, .Initials = initials, .Enabled = enabled}
    End Function

    Private Function HandleKey(keyData As Keys) As Boolean
        Dim result As MenuKeyResult = ChromeMenuRules.ResolveKey(keyData, KeyContext(keyData))
        Select Case result.Action
            Case MenuKeyAction.PassThrough
                ' The text box goes on typing: a row that had the ring gives it up, so Enter is the box's again.
                If suggestionList AndAlso Surface.HasFocusedItem Then Surface.LeaveKeyboardMode()
                Return False
            Case MenuKeyAction.Swallow
                ' Viewer sorting/navigation must not run while a menu is being browsed.
            Case MenuKeyAction.Close
                CloseThisLevel()
            Case MenuKeyAction.CloseAndEatF10
                RaiseEvent FocusKeyConsumed()
                HideChain()
            Case MenuKeyAction.BackOneLevel
                CloseThisLevel()
            Case MenuKeyAction.FocusNext
                If Not Surface.MoveFocus(True) Then Surface.EnterKeyboardMode(False)
                EnsureFocusVisible()
            Case MenuKeyAction.FocusPrevious
                If Not Surface.MoveFocus(False) Then Surface.EnterKeyboardMode(True)
                EnsureFocusVisible()
            Case MenuKeyAction.FocusFirst
                Surface.EnterKeyboardMode(False)
                EnsureFocusVisible()
            Case MenuKeyAction.FocusLast
                Surface.EnterKeyboardMode(True)
                EnsureFocusVisible()
            Case MenuKeyAction.Activate
                Surface.ActivateFocused()
            Case MenuKeyAction.OpenSubmenu
                Dim row As ChromeItem = Surface.FocusedRow
                Dim entry As ChromeMenuItem = If(row Is Nothing, Nothing, TryCast(row.Tag, ChromeMenuItem))
                If entry IsNot Nothing AndAlso entry.HasSubmenu Then OpenSubmenu(entry, row, True)
            Case MenuKeyAction.ByAccelerator
                If result.Index >= 0 AndAlso result.Index < entries.Count Then
                    Dim entry As ChromeMenuItem = entries(result.Index)
                    If entry.HasSubmenu Then OpenSubmenu(entry, Surface.Items(result.Index), True) Else InvokeEntry(entry)
                End If
            Case MenuKeyAction.TypeAhead
                If result.Index >= 0 AndAlso result.Index < Surface.Items.Count Then
                    Surface.FocusItem(Surface.Items(result.Index))
                    EnsureFocusVisible()
                End If
        End Select
        Return True
    End Function

    Public Function PreFilterMessage(ByRef message As Message) As Boolean Implements IMessageFilter.PreFilterMessage
        If Not Visible Then Return False
        ' A submenu rides on its root's rules: dismissal by the foreground, the mouse and the wheel, and the
        ' routing of keys to the leaf, are decided once, by the root's filter.
        If parentMenu IsNot Nothing Then Return False
        ' A click on a no-activate panel opens the menu without activating the viewer, so
        ' "the viewer is not focused" is how the menu looks when it opens from a background
        ' window. Dismiss on an activation CHANGE to a window that is neither the viewer nor
        ' one of its owned forms; the click-outside rule below still covers our own windows.
        Dim owned As New List(Of IntPtr)()
        If viewer IsNot Nothing Then
            If viewer.IsHandleCreated Then owned.Add(viewer.Handle)
            For Each form As Form In viewer.OwnedForms
                If form.IsHandleCreated Then owned.Add(form.Handle)
            Next
        End If
        ' ShouldPopupClose.. answers "should it close", so the menu closes when it says True. This line
        ' once read `If Not ..`, left over from the old "viewer is not focused" test it replaced, and
        ' dismissed every popup on its first message (UX-301 of 125; ChromeAutoHideTests holds the rule).
        If ChromeAutoHide.ShouldPopupCloseOnActivationChange(openForeground, GetForegroundWindow(), owned) Then
            HideChain()
            Return False
        End If
        Select Case message.Msg
            Case &H100, &H104
                Dim leaf As ChromeMenu = popup
                If leaf Is Nothing OrElse leaf.IsDisposed OrElse Not leaf.Visible Then Return False
                Dim code As Keys = CType(message.WParam.ToInt32(), Keys)
                ' Holding Escape must not dismiss successive menu levels or reach the viewer
                ' after this filter removes itself. The matching release ends the hold.
                If code = Keys.Escape AndAlso KeyRouting.IsAutoRepeat(message.LParam.ToInt64()) Then Return True
                Dim handled As Boolean = leaf.HandleKey(code Or Control.ModifierKeys)
                If handled AndAlso code = Keys.Escape Then EscapeReleaseSwallower.Arm()
                Return handled
            Case &H201, &H204, &H207, &HA1, &HA4
                Dim outside As Boolean = Not ChainContains(Cursor.Position)
                If outside Then HideChain()
                ' A press that closes the menu is the menu's, not the media's (UX-103 of 123): left it
                ' went on to flip the file, so the next digit moved a file the user never chose. Only a
                ' client press is eaten - the non-client ones (title bar, border) reach no media handler.
                Dim clientPress As Boolean = message.Msg = &H201 OrElse message.Msg = &H204 OrElse message.Msg = &H207
                If clientPress AndAlso ChromeAutoHide.ShouldConsumePressThatClosesPopup(True, outside) Then
                    ' HideChain has just removed this filter, so the matching button-up needs its own.
                    ButtonUpSwallower.Arm(message.Msg + 1)
                    Return True
                End If
            Case &H20A
                ' The wheel pages files over the media; with a menu open it is only ever the menu's.
                If Not ChainContains(Cursor.Position) Then
                    HideChain()
                    Return True
                End If
        End Select
        Return False
    End Function

    Private NotInheritable Class EscapeReleaseSwallower
        Implements IMessageFilter

        Private Shared instance As EscapeReleaseSwallower

        Public Shared Sub Arm()
            If instance IsNot Nothing Then Application.RemoveMessageFilter(instance)
            instance = New EscapeReleaseSwallower()
            Application.AddMessageFilter(instance)
        End Sub

        Public Function PreFilterMessage(ByRef message As Message) As Boolean Implements IMessageFilter.PreFilterMessage
            If message.WParam.ToInt64() <> CInt(Keys.Escape) Then Return False
            If message.Msg = &H101 OrElse message.Msg = &H105 Then
                Application.RemoveMessageFilter(Me)
                instance = Nothing
                Return True
            End If
            If message.Msg = &H100 OrElse message.Msg = &H104 Then
                If KeyRouting.IsAutoRepeat(message.LParam.ToInt64()) Then Return True
                ' A fresh down proves that a release was missed; allow the next ladder step.
                Application.RemoveMessageFilter(Me)
                instance = Nothing
            End If
            Return False
        End Function
    End Class

    ''' <summary>Eats the one button-up that pairs with a press the menu consumed, then removes itself
    ''' - so the media never sees half a click. Any other mouse press disarms it (the pair was lost).</summary>
    Private NotInheritable Class ButtonUpSwallower
        Implements IMessageFilter

        Private Shared instance As ButtonUpSwallower
        Private ReadOnly upMessage As Integer

        Private Sub New(up As Integer)
            upMessage = up
        End Sub

        Public Shared Sub Arm(up As Integer)
            Disarm()
            instance = New ButtonUpSwallower(up)
            Application.AddMessageFilter(instance)
        End Sub

        Private Shared Sub Disarm()
            If instance IsNot Nothing Then Application.RemoveMessageFilter(instance)
            instance = Nothing
        End Sub

        Public Function PreFilterMessage(ByRef m As Message) As Boolean Implements IMessageFilter.PreFilterMessage
            If m.Msg = upMessage Then
                Disarm()
                Return True
            End If
            If m.Msg = &H201 OrElse m.Msg = &H204 OrElse m.Msg = &H207 Then Disarm()
            Return False
        End Function
    End Class

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
        Surface.SetViewport(scrollOffset, ClientSize.Height)
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
    ''' <summary>Why a disabled row is disabled (shown as its tooltip) - a row that explains itself is better
    ''' than one that is hidden or one that fails after the click (125 UX-326).</summary>
    Public Property DisabledReason As String = ""
    Public Property Visible As Boolean = True
    Public Property Checked As Boolean
    ''' <summary>The look of "this destroys" (125 UX-321): only the look - what asks for confirmation is unchanged.</summary>
    Public Property Destructive As Boolean
    ''' <summary>A dividing line between groups of rows (125 UX-320).</summary>
    Public Property IsSeparator As Boolean
    ''' <summary>The key that does the same, read from the live key map when the menu is built (125 UX-325).</summary>
    Public Property Hint As String = ""
    ''' <summary>A digit (or letter) that runs the row from the keyboard while the menu is open (125 UX-323).</summary>
    Public Property Accelerator As Char = ChrW(0)
    ''' <summary>The rows of the submenu this row opens; Nothing = it is a command.</summary>
    Public Property Submenu As List(Of ChromeMenuItem)
    Public Property Image As Image
    Public Property Tag As Object
    Public Property Action As Action

    Public ReadOnly Property HasSubmenu As Boolean
        Get
            Return Submenu IsNot Nothing AndAlso Submenu.Count > 0
        End Get
    End Property

    Public Shared Function Separator() As ChromeMenuItem
        Return New ChromeMenuItem("separator") With {.IsSeparator = True}
    End Function
End Class
#End If
