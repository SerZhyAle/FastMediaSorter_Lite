Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Reflection
Imports System.Text.Json
Imports System.Windows.Forms

Partial Public NotInheritable Class Share_Settings_Form
    Private managerSettingsContext As New SettingsUiContext()
    Private settingsWasPresented As Boolean

    Private Function SettingsSections() As CollapsibleSection()
        Return AllSettingsSections()
    End Function

    Private Sub RestoreSettingsUiContext()
        Try
            Dim invalid As Boolean
            managerSettingsContext = ReadSettingsUiContext(invalid)
            ' The retired single-page anchor identifies a useful destination on migration.
            If Not _pages.ContainsKey(managerSettingsContext.LastPageId) Then
                Dim previous = managerSettingsContext.Page("manager").AnchorId
                managerSettingsContext.LastPageId = If(_pages.ContainsKey(previous), previous, _pages.Keys.First())
            End If
            ' The menu resumes its saved expansion (210 section 7.4); the v1-to-v2
            ' migration happened inside Read.
            ApplySettingsNavExpansion()
            For Each pagePair In _pageGroups
                Dim pId = pagePair.Key
                Dim pageCtx = managerSettingsContext.Page(pId)
                For Each grp In pagePair.Value
                    Dim exp As Boolean
                    ' No stored fold for the group: the page's first card is the one that
                    ' starts open, the rest stay folded to keep the page short (238 T-3).
                    grp.Expanded = If(pageCtx.Groups.TryGetValue(grp.Id, exp), exp, pagePair.Value(0) Is grp)
                    grp.Card.Caption = grp.Caption
                Next
            Next
            SelectSettingsPage(managerSettingsContext.ResolvePage(_pages.Keys))
        Catch ex As Exception
            AppFileLogger.LogException("Share settings: restoring UI context", ex)
        End Try
        If _selectedPage.Length = 0 AndAlso _pages.Count > 0 Then SelectSettingsPage(_pages.Keys.First())
    End Sub

    ''' <summary>This window's own record, or - while it has none (the first open) - the first record of the retired windows that
    ''' exists: for the Manager window the record of the SFTP sharing window that was merged into it (253 R-7), then the retired
    ''' combined record. A seed is only read: the next close writes this window's own record, which then wins, so a later change
    ''' of a seed is ignored and the seeds are never rewritten or deleted. A record that exists but is malformed gives the safe
    ''' defaults and the records behind it stay untouched.</summary>
    Private Function ReadSettingsUiContext(ByRef invalid As Boolean) As SettingsUiContext
        Dim keys As New List(Of String) From {SettingsRoutes.ContextKey(Destination)}
        If Destination = SettingsDestination.Manager Then keys.Add(SettingsRoutes.SftpContextKey)
        keys.Add(SettingsRoutes.LegacyContextKey)
        For Each key In keys
            Dim raw = GetSetting(App_name, Second_App_Name, key, String.Empty)
            If Not String.IsNullOrWhiteSpace(raw) Then Return SettingsUiContext.Read(raw, invalid)
        Next
        Return SettingsUiContext.Read(String.Empty, invalid)
    End Function

    Private Function RestoreSettingsWindowBounds() As Boolean
        Try
            Dim raw = GetSetting(App_name, Second_App_Name, SettingsRoutes.BoundsKey(Destination),
                GetSetting(App_name, Second_App_Name, SettingsRoutes.LegacyBoundsKey, String.Empty))
            If String.IsNullOrEmpty(raw) Then Return False
            Dim values = JsonSerializer.Deserialize(Of Integer())(raw)
            If values Is Nothing OrElse values.Length <> 4 OrElse values(2) < 300 OrElse values(3) < 200 OrElse
               values(2) > 8192 OrElse values(3) > 8192 Then Return False
            Dim factor = DeviceDpi / 96.0R
            Size = New Size(CInt(values(2) * factor), CInt(values(3) * factor))
            Location = New Point(values(0), values(1))
            If Owner IsNot Nothing AndAlso Screen.FromRectangle(Bounds).DeviceName <> Screen.FromControl(Owner).DeviceName Then
                DpiLayout.CenterOnOwner(Me)
            End If
            DpiLayout.ClampToWorkingArea(Me)
            Return True
        Catch ex As Exception
            AppFileLogger.LogException("Share settings: restoring bounds", ex)
            Return False
        End Try
    End Function

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        settingsWasPresented = True
        LoadSelectedSettingsPage()
        _pages(_selectedPage).PerformLayout()
        RestoreSettingsViewport()
        HandleScheduledPrefill()
    End Sub

    Private Function PageRows() As Control()
        Dim section = SettingsSections().First(Function(item) item.Key = _selectedPage)
        Return section.Body.Controls.Cast(Of Control)().ToArray()
    End Function

    Private Function ViewportTop(row As Control) As Integer
        Dim top = row.Top
        Dim parent = row.Parent
        While parent IsNot Nothing AndAlso parent IsNot _pages(_selectedPage)
            top += parent.Top
            parent = parent.Parent
        End While
        Return top
    End Function

    ''' <summary>The form's control fields in name order, reflected once. A page switch asks for an anchor
    ''' for every unnamed row, and reflecting the whole form each time made every switch seconds slow.</summary>
    Private Shared ReadOnly _controlFields As Lazy(Of FieldInfo()) = New Lazy(Of FieldInfo())(Function()
        Return GetType(Share_Settings_Form).GetFields(BindingFlags.Instance Or BindingFlags.NonPublic).
            Where(Function(item) GetType(Control).IsAssignableFrom(item.FieldType)).
            OrderBy(Function(item) item.Name, StringComparer.Ordinal).ToArray()
    End Function)

    Private Function RowAnchor(row As Control) As String
        If row.Name.Length > 0 Then Return _selectedPage & "/" & row.Name
        ' Field names survive translations and inserted/reordered rows. Anonymous prose
        ' uses the next named editor/status row rather than an ordinal pixel anchor.
        For Each field In _controlFields.Value
            Dim control = TryCast(field.GetValue(Me), Control)
            If control IsNot Nothing AndAlso (control Is row OrElse row.Contains(control)) Then
                Return _selectedPage & "/" & field.Name
            End If
        Next
        Return String.Empty
    End Function

    Private Sub CaptureSettingsViewport()
        If _selectedPage.Length = 0 Then Return
        Dim page = managerSettingsContext.Page(_selectedPage)
        Dim section = SettingsSections().First(Function(item) item.Key = _selectedPage)
        page.AnchorId = _selectedPage
        page.AnchorOffsetLogical = CInt(ViewportTop(section.Body) * 96.0R / DeviceDpi)
        Dim rows = PageRows()
        For index = 0 To rows.Length - 1
            Dim row = rows(index)
            Dim anchor = RowAnchor(row)
            If anchor.Length > 0 AndAlso row.Visible AndAlso ViewportTop(row) + row.Height >= 0 Then
                page.AnchorId = anchor
                page.AnchorOffsetLogical = CInt(ViewportTop(row) * 96.0R / DeviceDpi)
                Exit For
            End If
        Next
    End Sub

    Private Sub RestoreSettingsViewport()
        If Not settingsWasPresented OrElse _selectedPage.Length = 0 Then Return
        Dim page = managerSettingsContext.Page(_selectedPage)
        Dim viewport = _pages(_selectedPage)
        viewport.AutoScrollPosition = Point.Empty
        viewport.PerformLayout()
        If page.AnchorId = _selectedPage Then
            Dim section = SettingsSections().First(Function(item) item.Key = _selectedPage)
            viewport.AutoScrollPosition = New Point(0, Math.Max(0,
                ViewportTop(section.Body) - CInt(page.AnchorOffsetLogical * DeviceDpi / 96.0R)))
            Return
        End If
        Dim rows = PageRows()
        For index = 0 To rows.Length - 1
            Dim row = rows(index)
            If page.AnchorId.Length > 0 AndAlso RowAnchor(row) = page.AnchorId AndAlso row.Visible Then
                viewport.AutoScrollPosition = New Point(0, Math.Max(0,
                    ViewportTop(row) - CInt(page.AnchorOffsetLogical * DeviceDpi / 96.0R)))
                Exit For
            End If
        Next
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If Not CanCloseDraft(e) Then Return
        MyBase.OnFormClosing(e)
        If e.Cancel OrElse Not settingsWasPresented Then Return
        Try
            CaptureSettingsViewport()
            For Each pagePair In _pageGroups
                Dim pId = pagePair.Key
                Dim pageCtx = managerSettingsContext.Page(pId)
                For Each grp In pagePair.Value
                    pageCtx.Groups(grp.Id) = grp.Expanded
                Next
            Next
            SaveSetting(App_name, Second_App_Name, SettingsRoutes.ContextKey(Destination), managerSettingsContext.ToJson())
            Dim savedBounds = If(WindowState = FormWindowState.Normal, Me.Bounds, Me.RestoreBounds)
            SaveSetting(App_name, Second_App_Name, SettingsRoutes.BoundsKey(Destination), JsonSerializer.Serialize(
                {savedBounds.X, savedBounds.Y, CInt(savedBounds.Width * 96.0R / DeviceDpi), CInt(savedBounds.Height * 96.0R / DeviceDpi)}))
        Catch ex As Exception
            AppFileLogger.LogException("Share settings: saving UI context", ex)
        End Try
    End Sub
End Class
