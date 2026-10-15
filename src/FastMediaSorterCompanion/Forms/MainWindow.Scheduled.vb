Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

''' <summary>
''' The way into the Scheduled file operations page (ticket 222, 7.3 and 7.4): the tray menu and the viewer's one
''' launcher entry both end here. The page is a page of the settings window, so this is "open the settings,
''' on that page" - nothing about scheduling lives in the main window itself.
''' </summary>
Partial Public NotInheritable Class MainWindow

    Private _focusScheduledSettings As Boolean
    Private _scheduledPrefill As String = ""

    ''' <summary>Opens the settings on the Scheduled file operations page. A folder starts a new operation
    ''' with that source in the editor, for the user to complete and save.</summary>
    Friend Sub OpenScheduledOperations(Optional folder As String = "")
        If _settingsWindow IsNot Nothing AndAlso Not _settingsWindow.IsDisposed Then
            _settingsWindow.FocusScheduledPage(folder)
            _settingsWindow.Activate()
            Return
        End If
        _focusScheduledSettings = True
        _scheduledPrefill = If(folder, "")
        OnSettingsClicked(Me, EventArgs.Empty)
    End Sub

End Class