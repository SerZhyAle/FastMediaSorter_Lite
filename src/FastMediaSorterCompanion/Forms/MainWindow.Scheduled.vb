Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

''' <summary>
''' The way into Scheduled file operations (ticket 222, 7.3 and 7.4): the tray menu and the viewer's one
''' launcher entry both end here. Since L0297 that way is the standalone Scheduled operations window - the
''' settings page stays as an alternative view and links to the same window, so there is exactly one
''' management surface over the one engine this process started.
''' </summary>
Partial Public NotInheritable Class MainWindow

    ''' <summary>Opens the Scheduled operations window. A folder starts a new operation with that
    ''' source in the editor, for the user to complete and save.</summary>
    Friend Sub OpenScheduledOperations(Optional folder As String = "")
        Scheduled_Manager_Form.OpenScheduledManager(folder, Me)
    End Sub

End Class
