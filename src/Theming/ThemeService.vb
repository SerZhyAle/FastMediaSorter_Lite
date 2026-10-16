#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports Microsoft.VisualBasic
Imports Microsoft.Win32

''' <summary>
''' The application theme (spec 060): the stored mode, what it resolves to, and getting that onto
''' every window of the process without each window asking.
'''
''' - The mode is <c>UiTheme</c> in the shared SZA\FastMediaSorter hive - one value for both
'''   programs, like UiLanguage.
''' - System mode follows Windows live (UserPreferenceChanged), and a change made in either
'''   program reaches the other through one registered broadcast message.
''' - Every form is themed on the first WM_PAINT any of its controls receives, BEFORE that paint
'''   is dispatched: a message filter, so a dialog nobody touched for this still never shows light.
''' - Menus follow through ToolStripManager.Renderer.
''' </summary>
Public NotInheritable Class ThemeService

    Private Sub New()
    End Sub

    Private Shared _initialized As Boolean
    Private Shared _mode As ThemeMode = ThemeMode.System
    Private Shared _palette As ThemePalette = ThemePalette.Light
    Private Shared _originalRenderer As ToolStripRenderer
    Private Shared _darkRenderer As ToolStripRenderer
    Private Shared _broadcastMessage As Integer
    Private Shared _marshal As Control
    Private Shared ReadOnly Themed As New ConditionalWeakTable(Of Form, Object)()
    Private Shared _revision As Integer

    Private NotInheritable Class AppliedTheme
        Public ReadOnly Palette As ThemePalette
        Public ReadOnly Revision As Integer

        Public Sub New(palette As ThemePalette, revision As Integer)
            Me.Palette = palette
            Me.Revision = revision
        End Sub
    End Class

    ''' <summary>Raised on the UI thread after the resolved theme changed and every open window
    ''' has been repainted.</summary>
    Public Shared Event Changed As EventHandler

    Public Shared ReadOnly Property Mode As ThemeMode
        Get
            Return _mode
        End Get
    End Property

    ''' <summary>The active palette. Light until Initialize runs, so a caller that paints before
    ''' it (a test, a tool) gets the system colours it always had.</summary>
    Public Shared ReadOnly Property Palette As ThemePalette
        Get
            Return _palette
        End Get
    End Property

    Public Shared ReadOnly Property IsDark As Boolean
        Get
            Return _palette.IsDark
        End Get
    End Property

    ''' <summary>Once per process, on the UI thread, before the first window - after
    ''' SetCompatibleTextRenderingDefault, because it creates the marshalling control.</summary>
    Public Shared Sub Initialize()
        If _initialized Then Return
        _initialized = True
        _mode = ReadStoredMode()
        ' A never-shown control whose handle exists only so that system notifications, raised
        ' on a SystemEvents thread, can be marshalled to the UI thread.
        _marshal = New Control()
        If _marshal.Handle = IntPtr.Zero Then _marshal = Nothing
        _originalRenderer = ToolStripManager.Renderer
        Try
            _broadcastMessage = RegisterWindowMessage(BroadcastName)
        Catch
            _broadcastMessage = 0
        End Try
        _palette = ThemePalette.ForDark(ResolveNow())
        ApplyRenderer()
        ButtonGlyphs.UsePalette(_palette)
        Application.AddMessageFilter(New ThemeMessageFilter())
        AddHandler SystemEvents.UserPreferenceChanged, AddressOf OnUserPreferenceChanged
    End Sub

    ''' <summary>The user picked a mode: store it, apply it here, tell the other program.</summary>
    Public Shared Sub SetMode(mode As ThemeMode)
        _mode = mode
        Try
            Interaction.SaveSetting(App_name, Second_App_Name, ThemeModes.SettingValue, ThemeModes.ToSetting(mode))
        Catch
            ' An unwritable hive still changes this session.
        End Try
        Refresh()
        If _broadcastMessage <> 0 Then
            Try
                PostMessage(HwndBroadcast, _broadcastMessage, New IntPtr(Environment.ProcessId), IntPtr.Zero)
            Catch
            End Try
        End If
    End Sub

    ''' <summary>Themes one window now - for a window that must be right before its first paint
    ''' (the filter would catch it a moment later anyway).</summary>
    Public Shared Sub Attach(form As Form)
        If form Is Nothing OrElse form.IsDisposed Then Return
        ApplyForm(form)
    End Sub

    ' --- resolution ---------------------------------------------------------------------

    Private Shared Function ReadStoredMode() As ThemeMode
        Try
            Return ThemeModes.Parse(Interaction.GetSetting(App_name, Second_App_Name, ThemeModes.SettingValue, ""))
        Catch
            Return ThemeMode.System
        End Try
    End Function

    Private Shared Function ResolveNow() As Boolean
        Return ThemeModes.ResolveDark(_mode, SystemAppsUseLightTheme(), SystemInformation.HighContrast)
    End Function

    ''' <summary>Windows' own app theme. Missing or unreadable is light - the pre-1809 answer.</summary>
    Private Shared Function SystemAppsUseLightTheme() As Boolean
        Try
            Using key As RegistryKey = Registry.CurrentUser.OpenSubKey("Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
                Dim value As Object = If(key Is Nothing, Nothing, key.GetValue("AppsUseLightTheme"))
                If value IsNot Nothing Then Return Convert.ToInt32(value) <> 0
            End Using
        Catch
        End Try
        Return True
    End Function

    ''' <summary>Re-resolves and, only if the answer changed, repaints every open window.</summary>
    Private Shared Sub Refresh(Optional force As Boolean = False)
        Dim dark As Boolean = ResolveNow()
        If dark = _palette.IsDark AndAlso Not force Then Return
        _palette = ThemePalette.ForDark(dark)
        _revision += 1
        ApplyRenderer()
        Dim forms As New List(Of Form)()
        For Each f As Form In Application.OpenForms
            forms.Add(f)
        Next
        For Each f As Form In forms
            ApplyForm(f)
        Next
        ' Glyphs on answer buttons follow the theme once every face is final (WINDOWS-UI section 11 item 6).
        ButtonGlyphs.UsePalette(_palette)
        RaiseEvent Changed(Nothing, EventArgs.Empty)
    End Sub

    Private Shared Sub ApplyRenderer()
        If _palette.IsDark Then
            If _darkRenderer Is Nothing Then _darkRenderer = New ThemedMenuRenderer(ThemePalette.Dark)
            ToolStripManager.Renderer = _darkRenderer
        ElseIf _originalRenderer IsNot Nothing Then
            ToolStripManager.Renderer = _originalRenderer
        End If
    End Sub

    Private Shared Iterator Function ThemeSubtree(root As Control) As IEnumerable(Of Control)
        Yield root
        For Each child As Control In root.Controls
            For Each descendant In ThemeSubtree(child)
                Yield descendant
            Next
        Next
    End Function

    Private Shared Sub ApplyForm(form As Form)
        If form.IsDisposed Then Return
        ' Ambient color/font changes otherwise remeasure every nested AutoSize parent per control.
        Dim held As New List(Of Control)(ThemeSubtree(form))
        For Each control In held
            control.SuspendLayout()
        Next
        Themed.AddOrUpdate(form, New AppliedTheme(_palette, _revision))
        Dim aware As IThemeAware = TryCast(form, IThemeAware)
        Try
            CheckBoxLayout.ApplyTree(form)
            If aware IsNot Nothing AndAlso aware.ThemesItself Then
                aware.ApplyTheme(_palette)
            Else
                ThemeApplier.ApplyTree(form, _palette)
                ThemeApplier.ApplyTitleBar(form, _palette.IsDark)
                If aware IsNot Nothing Then aware.ApplyTheme(_palette)
            End If
            ThemeApplier.ApplyButtonCaptions(form, _palette)
            ContextHelpUi.Attach(form)
        Catch ex As Exception
            AppFileLogger.LogException("Theme: applying to " & form.GetType().Name, ex)
        Finally
            For index = held.Count - 1 To 0 Step -1
                held(index).ResumeLayout(index = 0)
            Next
        End Try
        form.Invalidate(True)
    End Sub

    ' --- the three triggers -------------------------------------------------------------

    Private Shared Sub OnUserPreferenceChanged(sender As Object, e As UserPreferenceChangedEventArgs)
        Select Case e.Category
            Case UserPreferenceCategory.General, UserPreferenceCategory.VisualStyle,
                 UserPreferenceCategory.Color, UserPreferenceCategory.Accessibility
            Case Else
                Return
        End Select
        ' System colors/high contrast can change while resolved Light/Dark stays the same.
        RunOnUi(Sub() Refresh(force:=True))
    End Sub

    Private Shared Sub RunOnUi(action As Action)
        Try
            If _marshal IsNot Nothing AndAlso _marshal.IsHandleCreated AndAlso Not _marshal.IsDisposed Then
                If _marshal.InvokeRequired Then
                    _marshal.BeginInvoke(action)
                Else
                    action()
                End If
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Theme: following a system change", ex)
        End Try
    End Sub

    ''' <summary>The other program changed the stored mode.</summary>
    Private Shared Sub OnBroadcast()
        Dim stored As ThemeMode = ReadStoredMode()
        If stored = _mode Then Return
        _mode = stored
        Refresh()
    End Sub

    Private NotInheritable Class ThemeMessageFilter
        Implements IMessageFilter

        Public Function PreFilterMessage(ByRef m As Message) As Boolean Implements IMessageFilter.PreFilterMessage
            If m.Msg = WmPaint Then
                Dim c As Control = Control.FromHandle(m.HWnd)
                If c Is Nothing Then Return False
                Dim f As Form = TryCast(c, Form)
                If f Is Nothing Then f = c.FindForm()
                Dim seen As Object = Nothing
                ' Reused/hidden windows may have missed Refresh's OpenForms enumeration.
                ' Compare the applied palette so their first new paint catches up as well.
                If f IsNot Nothing Then
                    Themed.TryGetValue(f, seen)
                    Dim applied As AppliedTheme = TryCast(seen, AppliedTheme)
                    If applied Is Nothing OrElse applied.Palette IsNot _palette OrElse applied.Revision <> _revision Then ApplyForm(f)
                End If
            ElseIf _broadcastMessage <> 0 AndAlso m.Msg = _broadcastMessage Then
                If m.WParam.ToInt64() <> Environment.ProcessId Then OnBroadcast()
            End If
            Return False
        End Function
    End Class

    Private Const WmPaint As Integer = &HF
    Private Const BroadcastName As String = "FastMediaSorter.UiThemeChanged"
    Private Shared ReadOnly HwndBroadcast As New IntPtr(&HFFFF)

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function RegisterWindowMessage(lpString As String) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function PostMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As Boolean
    End Function

End Class
#End If
