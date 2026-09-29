#If Not NETFRAMEWORK Then
Option Strict On

Imports System.ComponentModel
Imports System.Drawing
Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

''' <summary>
''' Paints a WinForms control tree in the active palette (060_SPECIFICATION_APP_THEME_SYSTEM_LIGHT_DARK.md
''' rules 7-8). The one idea that makes it safe on forms nobody rewrote for it: it remembers, per
''' control, which ROLE each system colour stood for, and repaints roles. A colour the form's code
''' chose itself - a literal green, a QR's white, an editor canvas - is not a system colour, has no
''' role, and is never touched. A colour inherited from the parent (an ambient colour) is left
''' ambient, so it keeps following its parent exactly as it did before.
''' </summary>
Friend NotInheritable Class ThemeApplier

    Private Sub New()
    End Sub

    Private NotInheritable Class ControlState
        Public BackRole As ThemeRole
        Public ForeRole As ThemeRole
        ' Button: the look it had before dark mode flattened it, restored for light.
        Public ButtonSeen As Boolean
        Public ButtonStandard As Boolean
        Public ButtonConverted As Boolean
        Public OrigFlatStyle As FlatStyle
        Public OrigBorderColor As Color
        Public OrigBorderSize As Integer
        Public OrigHover As Color
        Public OrigDown As Color
        ' ComboBox.
        Public ComboSeen As Boolean
        Public OrigComboFlat As FlatStyle
        ' LinkLabel.
        Public LinkSeen As Boolean
        Public OrigLink As Color
        Public OrigActiveLink As Color
        Public OrigVisitedLink As Color
        ' TabControl / TabPage.
        Public TabSeen As Boolean
        Public OrigTabDrawMode As TabDrawMode
        Public TabDrawHooked As Boolean
        Public OrigPageUseVisual As Boolean
        ' DataGridView.
        Public Grid As GridSnapshot
        ' Native theme (scroll bars, list headers) currently switched to dark.
        Public NativeDark As Boolean
    End Class

    Private NotInheritable Class GridSnapshot
        Public EnableHeadersVisualStyles As Boolean
        Public BackgroundColor As Color
        Public GridColor As Color
        Public CellBack, CellFore, CellSelBack, CellSelFore As Color
        Public ColHeadBack, ColHeadFore As Color
        Public RowHeadBack, RowHeadFore As Color
    End Class

    Private Shared ReadOnly States As New ConditionalWeakTable(Of Control, ControlState)()

    ''' <summary>Depth of our own colour writes: the Changed events they raise - on the control
    ''' and on every ambient child - are ours and must not be read as the code's choice.</summary>
    Private Shared _writing As Integer

    ' --- entry points -------------------------------------------------------------------

    Friend Shared Sub ApplyTree(root As Control, palette As ThemePalette)
        If root Is Nothing OrElse root.IsDisposed Then Return
        ApplyOne(root, palette)
        ' ToolStrips paint through the renderer (spec 060 rule 9); their items are not controls.
        If TypeOf root Is ToolStrip Then Return
        For Each child As Control In root.Controls
            ApplyTree(child, palette)
        Next
    End Sub

    ''' <summary>Only the native parts (scroll bars, list headers) - for a window that paints its
    ''' own colours and still wants dark scroll bars (the Settings window).</summary>
    Friend Shared Sub ApplyNativeTree(root As Control, dark As Boolean)
        If root Is Nothing OrElse root.IsDisposed Then Return
        Dim st As ControlState = Nothing
        If Not States.TryGetValue(root, st) Then
            st = New ControlState()
            States.Add(root, st)
        End If
        ApplyNative(root, st, dark)
        For Each child As Control In root.Controls
            ApplyNativeTree(child, dark)
        Next
    End Sub

    ''' <summary>The native caption: DWM attribute 20 on Windows 10 2004+ and 11, 19 before that.</summary>
    Friend Shared Sub ApplyTitleBar(form As Form, dark As Boolean)
        If form Is Nothing OrElse Not form.IsHandleCreated Then Return
        Try
            Dim flag As Integer = If(dark, 1, 0)
            If DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, flag, 4) <> 0 Then
                DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkModeOld, flag, 4)
            End If
        Catch
            ' Windows builds without the attribute keep the system caption.
        End Try
    End Sub

    ' --- one control --------------------------------------------------------------------

    Private Shared Sub ApplyOne(c As Control, palette As ThemePalette)
        Dim st As ControlState = Nothing
        If Not States.TryGetValue(c, st) Then
            st = New ControlState()
            st.BackRole = Classify(c, asBackground:=True)
            st.ForeRole = Classify(c, asBackground:=False)
            States.Add(c, st)
            Hook(c)
        End If

        Dim dark As Boolean = palette.IsDark

        If TypeOf c Is Button Then
            ApplyButton(DirectCast(c, Button), st, palette)
        ElseIf TypeOf c Is ComboBox Then
            Dim combo As ComboBox = DirectCast(c, ComboBox)
            If Not st.ComboSeen Then
                st.ComboSeen = True
                st.OrigComboFlat = combo.FlatStyle
            End If
            ' A visual-styles drop-down list ignores BackColor and stays white; the flat
            ' style paints with it.
            If dark Then
                If combo.FlatStyle = FlatStyle.Standard OrElse combo.FlatStyle = FlatStyle.System Then combo.FlatStyle = FlatStyle.Flat
            ElseIf combo.FlatStyle <> st.OrigComboFlat Then
                combo.FlatStyle = st.OrigComboFlat
            End If
        ElseIf TypeOf c Is LinkLabel Then
            Dim link As LinkLabel = DirectCast(c, LinkLabel)
            If Not st.LinkSeen Then
                st.LinkSeen = True
                st.OrigLink = link.LinkColor
                st.OrigActiveLink = link.ActiveLinkColor
                st.OrigVisitedLink = link.VisitedLinkColor
            End If
            link.LinkColor = If(dark, palette.Link, st.OrigLink)
            link.ActiveLinkColor = If(dark, palette.Link, st.OrigActiveLink)
            link.VisitedLinkColor = If(dark, palette.Link, st.OrigVisitedLink)
        ElseIf TypeOf c Is GroupBox Then
            ' A visual-styles group box draws an inherited caption colour from the theme,
            ' i.e. black - give it the role explicitly.
            If st.ForeRole = ThemeRole.None AndAlso Not IsSerialized(c, "ForeColor") Then st.ForeRole = ThemeRole.TextPrimary
        ElseIf TypeOf c Is TabControl Then
            ApplyTabControl(DirectCast(c, TabControl), st, dark)
        ElseIf TypeOf c Is TabPage Then
            Dim page As TabPage = DirectCast(c, TabPage)
            If Not st.TabSeen Then
                st.TabSeen = True
                st.OrigPageUseVisual = page.UseVisualStyleBackColor
            End If
            If dark Then
                page.UseVisualStyleBackColor = False
                If st.BackRole = ThemeRole.None Then st.BackRole = ThemeRole.SurfaceWindow
            Else
                page.UseVisualStyleBackColor = st.OrigPageUseVisual
            End If
        ElseIf TypeOf c Is DataGridView Then
            ApplyGrid(DirectCast(c, DataGridView), st, palette)
        End If

        If st.BackRole <> ThemeRole.None Then SetBack(c, palette.ColorOf(st.BackRole))
        If st.ForeRole <> ThemeRole.None Then SetFore(c, palette.ColorOf(st.ForeRole))

        ApplyNative(c, st, dark)
    End Sub

    ''' <summary>
    ''' The role a control's current colour stands for, or None. An inherited (ambient) colour is
    ''' None - the child keeps inheriting. The controls that carry their OWN default instead of
    ''' inheriting one (the edit and list controls, and a window without a parent) are read by
    ''' value either way.
    ''' </summary>
    Private Shared Function Classify(c As Control, asBackground As Boolean) As ThemeRole
        Dim propertyName As String = If(asBackground, "BackColor", "ForeColor")
        If Not OwnsDefaultColors(c) AndAlso Not IsSerialized(c, propertyName) Then Return ThemeRole.None
        Return ThemePalette.RoleForColor(If(asBackground, c.BackColor, c.ForeColor), asBackground)
    End Function

    Private Shared Function OwnsDefaultColors(c As Control) As Boolean
        Return c.Parent Is Nothing OrElse TypeOf c Is Form OrElse TypeOf c Is TextBoxBase OrElse
               TypeOf c Is ListBox OrElse TypeOf c Is ComboBox OrElse TypeOf c Is ListView OrElse
               TypeOf c Is TreeView OrElse TypeOf c Is UpDownBase
    End Function

    ''' <summary>Whether the property holds a value of its own - the designer's question, asked
    ''' through the public descriptor so no private member is reached by name.</summary>
    Private Shared Function IsSerialized(c As Control, propertyName As String) As Boolean
        Try
            Dim prop As PropertyDescriptor = TypeDescriptor.GetProperties(c)(propertyName)
            Return prop IsNot Nothing AndAlso prop.ShouldSerializeValue(c)
        Catch
            Return False
        End Try
    End Function

    ' --- the colour writes and the code's later writes -----------------------------------

    Private Shared Sub SetBack(c As Control, value As Color)
        If value.IsEmpty OrElse c.BackColor = value Then Return
        _writing += 1
        Try
            c.BackColor = value
        Catch
            ' A control that refuses the colour (a transparent-only one) keeps its own.
        Finally
            _writing -= 1
        End Try
    End Sub

    Private Shared Sub SetFore(c As Control, value As Color)
        If value.IsEmpty OrElse c.ForeColor = value Then Return
        _writing += 1
        Try
            c.ForeColor = value
        Catch
        Finally
            _writing -= 1
        End Try
    End Sub

    Private Shared Sub Hook(c As Control)
        AddHandler c.BackColorChanged, AddressOf OnBackColorChanged
        AddHandler c.ForeColorChanged, AddressOf OnForeColorChanged
        AddHandler c.ControlAdded, AddressOf OnControlAdded
        AddHandler c.HandleCreated, AddressOf OnHandleCreated
    End Sub

    ''' <summary>The code set a colour after the theme ran. A system colour is mapped to its role
    ''' (a note turned GrayText stays readable on charcoal); anything else is the code's own.</summary>
    Private Shared Sub OnBackColorChanged(sender As Object, e As EventArgs)
        If _writing > 0 Then Return
        Dim c As Control = TryCast(sender, Control)
        Dim st As ControlState = Nothing
        If c Is Nothing OrElse Not States.TryGetValue(c, st) Then Return
        st.BackRole = Classify(c, asBackground:=True)
        If st.BackRole <> ThemeRole.None Then SetBack(c, ThemeService.Palette.ColorOf(st.BackRole))
    End Sub

    Private Shared Sub OnForeColorChanged(sender As Object, e As EventArgs)
        If _writing > 0 Then Return
        Dim c As Control = TryCast(sender, Control)
        Dim st As ControlState = Nothing
        If c Is Nothing OrElse Not States.TryGetValue(c, st) Then Return
        st.ForeRole = Classify(c, asBackground:=False)
        If st.ForeRole = ThemeRole.None AndAlso TypeOf c Is GroupBox AndAlso Not IsSerialized(c, "ForeColor") Then st.ForeRole = ThemeRole.TextPrimary
        If st.ForeRole <> ThemeRole.None Then SetFore(c, ThemeService.Palette.ColorOf(st.ForeRole))
    End Sub

    ''' <summary>A control added after the form was themed - a lazily built row, a rebuilt list.</summary>
    Private Shared Sub OnControlAdded(sender As Object, e As ControlEventArgs)
        If e.Control Is Nothing Then Return
        ApplyTree(e.Control, ThemeService.Palette)
    End Sub

    ''' <summary>A recreated handle (a RightToLeft switch does it) loses the native theme and the
    ''' caption attribute; both are per-handle.</summary>
    Private Shared Sub OnHandleCreated(sender As Object, e As EventArgs)
        Dim c As Control = TryCast(sender, Control)
        Dim st As ControlState = Nothing
        If c Is Nothing OrElse Not States.TryGetValue(c, st) Then Return
        st.NativeDark = False
        ApplyNative(c, st, ThemeService.Palette.IsDark)
        If TypeOf c Is Form Then ApplyTitleBar(DirectCast(c, Form), ThemeService.Palette.IsDark)
    End Sub

    ' --- per-type -----------------------------------------------------------------------

    ''' <summary>
    ''' A standard button draws its face from the visual style and ignores BackColor, so dark mode
    ''' flattens it and gives it the control role; light mode puts back exactly what it had. A
    ''' button the code already styled (flat, or with a colour of its own) is left to the colour
    ''' roles alone.
    ''' </summary>
    Private Shared Sub ApplyButton(b As Button, st As ControlState, palette As ThemePalette)
        If Not st.ButtonSeen Then
            st.ButtonSeen = True
            st.OrigFlatStyle = b.FlatStyle
            st.OrigBorderColor = b.FlatAppearance.BorderColor
            st.OrigBorderSize = b.FlatAppearance.BorderSize
            st.OrigHover = b.FlatAppearance.MouseOverBackColor
            st.OrigDown = b.FlatAppearance.MouseDownBackColor
            ' UseVisualStyleBackColor alone cannot say it: a button that never set it answers
            ' True only while its inherited BackColor is Control - and the parent has already
            ' been repainted by the time the walk reaches it. A standard-style button whose face
            ' colour is inherited or a system colour is the visual-style button.
            st.ButtonStandard = (b.FlatStyle = FlatStyle.Standard OrElse b.FlatStyle = FlatStyle.System) AndAlso
                                (b.UseVisualStyleBackColor OrElse Not IsSerialized(b, "BackColor") OrElse b.BackColor.IsSystemColor)
        End If

        If palette.IsDark Then
            If st.ButtonStandard Then
                st.ButtonConverted = True
                b.FlatStyle = FlatStyle.Flat
                b.UseVisualStyleBackColor = False
                b.FlatAppearance.BorderSize = 1
                st.BackRole = ThemeRole.Control
            End If
            If st.ButtonConverted OrElse b.FlatAppearance.BorderColor.IsSystemColor OrElse b.FlatAppearance.BorderColor.IsEmpty Then
                If b.FlatStyle = FlatStyle.Flat Then b.FlatAppearance.BorderColor = palette.Border
            End If
            If st.ButtonConverted Then
                b.FlatAppearance.MouseOverBackColor = palette.ControlHover
                b.FlatAppearance.MouseDownBackColor = palette.ControlPressed
            End If
        ElseIf st.ButtonConverted Then
            st.ButtonConverted = False
            st.BackRole = ThemeRole.None
            b.FlatStyle = st.OrigFlatStyle
            b.FlatAppearance.BorderColor = st.OrigBorderColor
            b.FlatAppearance.BorderSize = st.OrigBorderSize
            b.FlatAppearance.MouseOverBackColor = st.OrigHover
            b.FlatAppearance.MouseDownBackColor = st.OrigDown
            _writing += 1
            Try
                b.ResetBackColor()
            Finally
                _writing -= 1
            End Try
            ' It was a visual-style button (that is what ButtonStandard means), whatever the
            ' getter answered once the parent had been repainted.
            b.UseVisualStyleBackColor = True
        ElseIf b.FlatStyle = FlatStyle.Flat AndAlso b.FlatAppearance.BorderColor = ThemePalette.Dark.Border Then
            b.FlatAppearance.BorderColor = st.OrigBorderColor
        End If
    End Sub

    ''' <summary>Tab headers are drawn by the visual style in its own light colours; dark mode draws
    ''' them itself. The strip beside the headers stays native (spec 060 §6).</summary>
    Private Shared Sub ApplyTabControl(tabs As TabControl, st As ControlState, dark As Boolean)
        If Not st.TabSeen Then
            st.TabSeen = True
            st.OrigTabDrawMode = tabs.DrawMode
        End If
        If st.OrigTabDrawMode <> TabDrawMode.Normal Then Return   ' the form draws its own
        If dark Then
            If Not st.TabDrawHooked Then
                st.TabDrawHooked = True
                AddHandler tabs.DrawItem, AddressOf OnDrawTab
            End If
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed
        Else
            tabs.DrawMode = TabDrawMode.Normal
        End If
    End Sub

    Private Shared Sub OnDrawTab(sender As Object, e As DrawItemEventArgs)
        Dim tabs As TabControl = TryCast(sender, TabControl)
        If tabs Is Nothing OrElse e.Index < 0 OrElse e.Index >= tabs.TabPages.Count Then Return
        Dim p As ThemePalette = ThemeService.Palette
        Dim selected As Boolean = (e.Index = tabs.SelectedIndex)
        Using back As New SolidBrush(If(selected, p.SurfaceRaised, p.SurfaceWindow))
            e.Graphics.FillRectangle(back, e.Bounds)
        End Using
        Dim flags As TextFormatFlags = TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine
        If tabs.RightToLeft = RightToLeft.Yes Then flags = flags Or TextFormatFlags.RightToLeft
        TextRenderer.DrawText(e.Graphics, tabs.TabPages(e.Index).Text, tabs.Font, e.Bounds,
                              If(tabs.Enabled, p.TextPrimary, p.TextDisabled), flags)
    End Sub

    Private Shared Sub ApplyGrid(grid As DataGridView, st As ControlState, palette As ThemePalette)
        If st.Grid Is Nothing Then
            st.Grid = New GridSnapshot With {
                .EnableHeadersVisualStyles = grid.EnableHeadersVisualStyles,
                .BackgroundColor = grid.BackgroundColor,
                .GridColor = grid.GridColor,
                .CellBack = grid.DefaultCellStyle.BackColor,
                .CellFore = grid.DefaultCellStyle.ForeColor,
                .CellSelBack = grid.DefaultCellStyle.SelectionBackColor,
                .CellSelFore = grid.DefaultCellStyle.SelectionForeColor,
                .ColHeadBack = grid.ColumnHeadersDefaultCellStyle.BackColor,
                .ColHeadFore = grid.ColumnHeadersDefaultCellStyle.ForeColor,
                .RowHeadBack = grid.RowHeadersDefaultCellStyle.BackColor,
                .RowHeadFore = grid.RowHeadersDefaultCellStyle.ForeColor}
        End If
        Dim s As GridSnapshot = st.Grid
        If palette.IsDark Then
            grid.EnableHeadersVisualStyles = False
            grid.BackgroundColor = palette.SurfaceRaised
            grid.GridColor = palette.Border
            grid.DefaultCellStyle.BackColor = palette.SurfaceRaised
            grid.DefaultCellStyle.ForeColor = palette.TextPrimary
            grid.DefaultCellStyle.SelectionBackColor = palette.Accent
            grid.DefaultCellStyle.SelectionForeColor = palette.AccentInk
            grid.ColumnHeadersDefaultCellStyle.BackColor = palette.SurfaceWindow
            grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.TextPrimary
            grid.RowHeadersDefaultCellStyle.BackColor = palette.SurfaceWindow
            grid.RowHeadersDefaultCellStyle.ForeColor = palette.TextPrimary
        Else
            grid.EnableHeadersVisualStyles = s.EnableHeadersVisualStyles
            grid.BackgroundColor = s.BackgroundColor
            grid.GridColor = s.GridColor
            grid.DefaultCellStyle.BackColor = s.CellBack
            grid.DefaultCellStyle.ForeColor = s.CellFore
            grid.DefaultCellStyle.SelectionBackColor = s.CellSelBack
            grid.DefaultCellStyle.SelectionForeColor = s.CellSelFore
            grid.ColumnHeadersDefaultCellStyle.BackColor = s.ColHeadBack
            grid.ColumnHeadersDefaultCellStyle.ForeColor = s.ColHeadFore
            grid.RowHeadersDefaultCellStyle.BackColor = s.RowHeadBack
            grid.RowHeadersDefaultCellStyle.ForeColor = s.RowHeadFore
        End If
    End Sub

    ' --- native parts: scroll bars, list headers ----------------------------------------

    ''' <summary>Only the controls that own a native scroll bar or header. Anything else keeps the
    ''' theme it had - passing a dark sub-theme to a button or an edit box changes nothing useful
    ''' and costs a WM_THEMECHANGED.</summary>
    Private Shared Function WantsNativeTheme(c As Control) As Boolean
        If TypeOf c Is ListView OrElse TypeOf c Is TreeView OrElse TypeOf c Is ListBox OrElse TypeOf c Is ScrollBar Then Return True
        Dim box As TextBoxBase = TryCast(c, TextBoxBase)
        If box IsNot Nothing Then Return box.Multiline
        Dim scrollable As ScrollableControl = TryCast(c, ScrollableControl)
        Return scrollable IsNot Nothing AndAlso scrollable.AutoScroll
    End Function

    Private Shared Sub ApplyNative(c As Control, st As ControlState, dark As Boolean)
        If Not c.IsHandleCreated OrElse st.NativeDark = dark Then Return
        If Not dark AndAlso Not st.NativeDark Then Return
        If Not WantsNativeTheme(c) Then Return
        Try
            If dark Then
                SetWindowTheme(c.Handle, "DarkMode_Explorer", Nothing)
            Else
                ' Both NULL removes our association and puts the default theme back.
                SetWindowTheme(c.Handle, Nothing, Nothing)
            End If
            Dim list As ListView = TryCast(c, ListView)
            If list IsNot Nothing AndAlso dark Then HeaderInk.Attach(list)
            If list IsNot Nothing Then
                Dim header As IntPtr = SendMessage(list.Handle, LvmGetHeader, IntPtr.Zero, IntPtr.Zero)
                If header <> IntPtr.Zero Then
                    If dark Then
                        SetWindowTheme(header, "DarkMode_ItemsView", Nothing)
                    Else
                        SetWindowTheme(header, Nothing, Nothing)
                    End If
                End If
            End If
            st.NativeDark = dark
        Catch
            ' Pre-1809 Windows has no dark sub-themes; the control keeps the light scroll bar.
        End Try
    End Sub

    ''' <summary>
    ''' The dark header sub-theme darkens the column header's background but keeps drawing its
    ''' text black - black on charcoal. The header asks its parent, the list, through
    ''' NM_CUSTOMDRAW before each column; answering with the palette's text colour is the only
    ''' documented way to change it. Attached once per list handle, and it answers only while
    ''' the theme is dark - in light mode every message passes through untouched, including the
    ''' owner-draw path WinForms runs for an OwnerDraw list.
    ''' </summary>
    Private NotInheritable Class HeaderInk
        Inherits NativeWindow

        Private Shared ReadOnly Attached As New ConditionalWeakTable(Of ListView, HeaderInk)()
        Private ReadOnly _list As ListView

        Private Sub New(list As ListView)
            _list = list
            AssignHandle(list.Handle)
        End Sub

        Friend Shared Sub Attach(list As ListView)
            If Not list.IsHandleCreated Then Return
            Dim existing As HeaderInk = Nothing
            If Attached.TryGetValue(list, existing) Then
                If existing.Handle = list.Handle Then Return
                Attached.Remove(list)
            End If
            Attached.Add(list, New HeaderInk(list))
        End Sub

        Protected Overrides Sub WndProc(ByRef m As Message)
            If m.Msg = WmNotify AndAlso ThemeService.IsDark AndAlso m.LParam <> IntPtr.Zero Then
                Dim hdr As NMHDR = Marshal.PtrToStructure(Of NMHDR)(m.LParam)
                If hdr.code = NmCustomDrawCode AndAlso hdr.hwndFrom = SendMessage(_list.Handle, LvmGetHeader, IntPtr.Zero, IntPtr.Zero) Then
                    Dim cd As NMCUSTOMDRAW = Marshal.PtrToStructure(Of NMCUSTOMDRAW)(m.LParam)
                    If cd.dwDrawStage = CddsPrepaint Then
                        m.Result = New IntPtr(CdrfNotifyItemDraw)
                        Return
                    End If
                    If cd.dwDrawStage = CddsItemPrepaint Then
                        SetTextColor(cd.hdc, ColorTranslator.ToWin32(ThemeService.Palette.TextPrimary))
                        m.Result = New IntPtr(CdrfDoDefault)
                        Return
                    End If
                End If
            End If
            MyBase.WndProc(m)
        End Sub

        Protected Overrides Sub OnHandleChange()
            MyBase.OnHandleChange()
            ' The list's handle is gone (a RightToLeft switch recreates it); HandleCreated
            ' attaches a fresh one.
            If Handle = IntPtr.Zero Then Attached.Remove(_list)
        End Sub
    End Class

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NMHDR
        Public hwndFrom As IntPtr
        Public idFrom As UIntPtr
        Public code As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NMCUSTOMDRAW
        Public hdr As NMHDR
        Public dwDrawStage As Integer
        Public hdc As IntPtr
        Public rcLeft, rcTop, rcRight, rcBottom As Integer
        Public dwItemSpec As UIntPtr
        Public uItemState As Integer
        Public lItemlParam As IntPtr
    End Structure

    Private Const WmNotify As Integer = &H4E
    Private Const NmCustomDrawCode As Integer = -12
    Private Const CddsPrepaint As Integer = &H1
    Private Const CddsItemPrepaint As Integer = &H10001
    Private Const CdrfDoDefault As Integer = &H0
    Private Const CdrfNotifyItemDraw As Integer = &H20

    <DllImport("gdi32.dll")>
    Private Shared Function SetTextColor(hdc As IntPtr, crColor As Integer) As Integer
    End Function

    Private Const DwmwaUseImmersiveDarkMode As Integer = 20
    Private Const DwmwaUseImmersiveDarkModeOld As Integer = 19
    Private Const LvmGetHeader As Integer = &H1000 + 31

    <DllImport("dwmapi.dll", PreserveSig:=True)>
    Private Shared Function DwmSetWindowAttribute(hwnd As IntPtr, attr As Integer, ByRef attrValue As Integer, attrSize As Integer) As Integer
    End Function

    <DllImport("uxtheme.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function SetWindowTheme(hwnd As IntPtr, pszSubAppName As String, pszSubIdList As String) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

End Class
#End If
