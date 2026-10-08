#If Not NETFRAMEWORK Then
Option Strict On

' <summary>
' The welcome wizard's host in the viewer (spec 184): what a launch knows about the profile before it writes anything,
' when the wizard opens, and the four things it may apply. Every rule behind these lines is in <see cref="WelcomeRules"/>;
' this partial only reads the registry, opens the window and writes the values the window hands back.
' </summary>
Partial Public Class Main_Form

    ''' <summary>The viewer's hive held no viewer value when this process started: a fresh install or a second Windows user.</summary>
    Private welcome_Fresh_Profile As Boolean

    ''' <summary>This process was started with an argument - the user's file, which by spec 184 T-2 gets no wizard.</summary>
    Private welcome_File_Argument As Boolean

    ''' <summary>The wizard is on screen. A second request must not open a second one.</summary>
    Private welcome_Open As Boolean

    ''' <summary>
    ''' Called first in Form1_Load, before the launch writes its own values (the association marker is written on every
    ''' start). Decides freshness, remembers the argument, and leaves the note for the next launch when a fresh profile
    ''' was started with a file.
    ''' </summary>
    Private Sub CaptureWelcomeStartState()
        Try
            welcome_Fresh_Profile = Not WelcomeRules.HasViewerValues(StoredSettingNames())
            Dim launchArgument As String = String.Join(" ", My.Application.CommandLineArgs)
            StripNoBackFlag(launchArgument)
            welcome_File_Argument = Not String.IsNullOrWhiteSpace(launchArgument)
            If WelcomeRules.DeferToNextLaunch(WelcomeDoneStored(), welcome_Fresh_Profile, welcome_File_Argument) Then
                SaveSetting(App_name, Second_App_Name, WelcomeRules.PendingValue, "1")
            End If
        Catch ex As Exception
            AppFileLogger.LogException("welcome start state", ex)
        End Try
    End Sub

    ''' <summary>The names of every value in the viewer's settings hive, whatever wrote them.</summary>
    Private Shared Function StoredSettingNames() As List(Of String)
        Dim names As New List(Of String)()
        Dim stored As String(,) = GetAllSettings(App_name, Second_App_Name)
        If stored IsNot Nothing Then
            For row As Integer = stored.GetLowerBound(0) To stored.GetUpperBound(0)
                names.Add(stored(row, 0))
            Next
        End If
        Return names
    End Function

    Private Shared Function WelcomeDoneStored() As Boolean
        Return GetSetting(App_name, Second_App_Name, WelcomeRules.DoneValue, "0") = "1"
    End Function

    Private Shared Function WelcomePendingStored() As Boolean
        Return GetSetting(App_name, Second_App_Name, WelcomeRules.PendingValue, "0") = "1"
    End Function

    ''' <summary>Runs once the first picture has had its paint (Main_Form_Shown): the window is visible before a modal opens.</summary>
    Private Sub ShowWelcomeIfDue()
        If Me.IsDisposed OrElse MultiWindowPolicy.IsSecondaryInstance() Then Return
        If welcome_File_Argument AndAlso Not WelcomeDoneStored() AndAlso WelcomePendingStored() Then
            Notify(Localization.T("Первоначальная настройка доступна через F1."), NotifyLevel.Info)
            Return
        End If
        If Not WelcomeRules.ShouldRun(WelcomeDoneStored(), WelcomePendingStored(), welcome_Fresh_Profile, welcome_File_Argument) Then Return
        ShowWelcome()
    End Sub

    ''' <summary>Opens the wizard now: from the first launch, or from the settings window's row (spec 184 section 6).</summary>
    Friend Sub ShowWelcome()
        If welcome_Open OrElse Me.IsDisposed Then Return
        welcome_Open = True
        Try
            Using dialog As New Welcome_Form(Me, IsPackagedApp())
                DialogLayer.ShowViewerDialog(dialog, Me)
            End Using
        Finally
            welcome_Open = False
            If Table_Form IsNot Nothing AndAlso Not Table_Form.IsDisposed AndAlso Table_Form.Visible Then
                Table_Form.PrepareForDisplay()
            End If
        End Try
    End Sub

    ''' <summary>Called by the wizard on every exit - finish, skip, Esc or the close button - so the trigger cannot re-fire.</summary>
    Friend Sub WelcomeRecordExit()
        SaveSetting(App_name, Second_App_Name, WelcomeRules.DoneValue, "1")
        SaveSetting(App_name, Second_App_Name, WelcomeRules.PendingValue, "0")
    End Sub

    ''' <summary>Step W-2: the background scheme and the wheel. Written through the settings block at once.</summary>
    Friend Sub WelcomeApplyAppearance(scheme As Integer, wheelZooms As Boolean)
        Form_Color_Scheme = scheme
        Zoom_Wheel_Zooms = wheelZooms
        RepaintMedia()
        SaveSettingsNow()
    End Sub

    ''' <summary>Step W-3: the folder bound to key 1, or "" for none. Refreshes the recipients overlay and the slot probe.</summary>
    Friend Sub WelcomeApplyKeyOne(folder As String)
        Hardkeys_to_move_mediafile(1) = folder
        ApplyRecipientsOverlay()
        PrewarmSlotHealth()
        SaveSettingsNow()
    End Sub

    ''' <summary>Step W-5: the picture associations, through the same per-user path as the settings window's button.</summary>
    Friend Sub WelcomeRegisterImages()
        AssociateAllImageFormatsWithThisApp()
    End Sub

End Class

#End If
