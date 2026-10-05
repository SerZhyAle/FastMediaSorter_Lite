Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Text.Json
Imports System.Windows.Forms

Partial Public NotInheritable Class Share_Settings_Form
    Private managerSettingsContext As New SettingsUiContext()
    Private settingsWasPresented As Boolean

    Private Function SettingsSections() As CollapsibleSection()
        Return {_secStartup, _secNetwork, _secHosting, _secHelp}
    End Function

    Private Sub RestoreSettingsUiContext()
        Try
            Dim invalid As Boolean
            managerSettingsContext = SettingsUiContext.Read(GetSetting(App_name, Second_App_Name,
                "Share_SettingsContext", String.Empty), invalid)
            Dim page = managerSettingsContext.Page("manager")
            For Each section In SettingsSections()
                section.Expanded = page.Expanded(section.Key, section.Expanded)
            Next
        Catch ex As Exception
            AppFileLogger.LogException("Share settings: restoring UI context", ex)
        End Try
    End Sub

    Private Function RestoreSettingsWindowBounds() As Boolean
        Try
            Dim raw = GetSetting(App_name, Second_App_Name, "Share_SettingsBounds", String.Empty)
            If String.IsNullOrEmpty(raw) Then Return False
            Dim values = JsonSerializer.Deserialize(Of Integer())(raw)
            If values Is Nothing OrElse values.Length <> 4 OrElse values(2) < 300 OrElse values(3) < 200 OrElse
               values(2) > 8192 OrElse values(3) > 8192 Then Return False
            Dim factor = DeviceDpi / 96.0R
            Size = New Size(CInt(values(2) * factor), CInt(values(3) * factor))
            Location = New Point(values(0), values(1))
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
        Dim page = managerSettingsContext.Page("manager")
        Dim section = SettingsSections().FirstOrDefault(Function(item) item.Key = page.AnchorId)
        If section IsNot Nothing Then
            Dim target = Math.Max(0, section.Top + _root.AutoScrollPosition.Y -
                                 CInt(page.AnchorOffsetLogical * DeviceDpi / 96.0R))
            _root.AutoScrollPosition = New Point(0, target)
        End If
        If _secHosting.Expanded Then EnsureHostingLoaded()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        MyBase.OnFormClosing(e)
        If e.Cancel OrElse Not settingsWasPresented Then Return
        Try
            Dim page = managerSettingsContext.Page("manager")
            For Each section In SettingsSections()
                page.Groups(section.Key) = section.Expanded
            Next
            Dim anchor = SettingsSections().FirstOrDefault(Function(section) section.Bottom >= 0)
            If anchor IsNot Nothing Then
                page.AnchorId = anchor.Key
                page.AnchorOffsetLogical = CInt(anchor.Top * 96.0R / DeviceDpi)
            End If
            SaveSetting(App_name, Second_App_Name, "Share_SettingsContext", managerSettingsContext.ToJson())
            Dim savedBounds = If(WindowState = FormWindowState.Normal, Me.Bounds, Me.RestoreBounds)
            SaveSetting(App_name, Second_App_Name, "Share_SettingsBounds", JsonSerializer.Serialize(
                {savedBounds.X, savedBounds.Y, CInt(savedBounds.Width * 96.0R / DeviceDpi), CInt(savedBounds.Height * 96.0R / DeviceDpi)}))
        Catch ex As Exception
            AppFileLogger.LogException("Share settings: saving UI context", ex)
        End Try
    End Sub
End Class
