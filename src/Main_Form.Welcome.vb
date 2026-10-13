#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

' <summary>
' The welcome wizard's host in the viewer (spec 184): what a launch knows about the profile before it writes anything,
' when the wizard opens, and the things it may apply. Spec 211 adds the invitation: while the profile is eligible, the
' empty media surface carries a "Первоначальная настройка" button that opens the wizard - the one-click way in for a
' first launch that carried a file, which by spec 184 T-2 never gets a modal between the click and the picture.
' Every rule behind these lines is in <see cref="WelcomeRules"/>; this partial only reads the registry, opens the
' window and writes the values the window hands back.
' </summary>
Partial Public Class Main_Form

    ''' <summary>The viewer's hive held no viewer value when this process started: a fresh install or a second Windows user.</summary>
    Private welcome_Fresh_Profile As Boolean

    ''' <summary>This process was started with an argument - the user's file, which by spec 184 T-2 gets no wizard.</summary>
    Private welcome_File_Argument As Boolean

    ''' <summary>The wizard is on screen. A second request must not open a second one.</summary>
    Private welcome_Open As Boolean

    ''' <summary>The wizard window while it is on screen (spec 215): the owner the Share Manager wake's own dialogs answer
    ''' to, so a refusal said from the finish screen is not hidden behind the modal wizard.</summary>
    Private welcome_Dialog As Form

    ''' <summary>The invitation button on the empty media surface (spec 211 §4.3); created on first use, hidden by default.</summary>
    Private welcome_Invite_Button As Button

    ''' <summary>
    ''' Whether the profile was eligible when this launch last checked. Held because <see cref="UpdateWelcomeInvite"/>
    ''' runs on every media-surface change and must not read the registry there; the only writer of Done mid-session is
    ''' <see cref="WelcomeRecordExit"/>, which clears the flag itself.
    ''' </summary>
    Private welcome_Invite_Eligible As Boolean

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
            welcome_Invite_Eligible = WelcomeRules.InviteOffered(WelcomeDoneStored(), WelcomePendingStored(), welcome_Fresh_Profile)
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

    ''' <summary>
    ''' Runs once the first picture has had its paint (Main_Form_Shown): the window is visible before a modal opens.
    ''' Spec 211 §4.3: on the launch that carried a file the invitation is the button on the empty surface; the toast
    ''' remains only for the case where the surface cannot host the button.
    ''' </summary>
    Private Sub ShowWelcomeIfDue()
        If Me.IsDisposed OrElse MultiWindowPolicy.IsSecondaryInstance() Then Return
        If welcome_File_Argument AndAlso Not WelcomeDoneStored() AndAlso WelcomePendingStored() Then
            If Not UpdateWelcomeInvite() Then
                Notify(Localization.T("Первоначальную настройку можно открыть кнопкой на пустом экране."), NotifyLevel.Info)
            End If
            Return
        End If
        If Not WelcomeRules.ShouldRun(WelcomeDoneStored(), WelcomePendingStored(), welcome_Fresh_Profile, welcome_File_Argument) Then Return
        ShowWelcome()
    End Sub

    ''' <summary>Opens the wizard now: from the first launch, from the empty surface's button, or from the settings window's row (spec 184 section 6).</summary>
    Friend Sub ShowWelcome()
        If welcome_Open OrElse Me.IsDisposed Then Return
        welcome_Open = True
        UpdateWelcomeInvite()
        Try
            Using dialog As New Welcome_Form(Me, IsPackagedApp())
                welcome_Dialog = dialog
                DialogLayer.ShowViewerDialog(dialog, Me)
            End Using
        Finally
            welcome_Dialog = Nothing
            welcome_Open = False
            UpdateWelcomeInvite()
            If Table_Form IsNot Nothing AndAlso Not Table_Form.IsDisposed AndAlso Table_Form.Visible Then
                Table_Form.PrepareForDisplay()
            End If
        End Try
    End Sub

    ''' <summary>Called by the wizard on every exit - finish, skip, Esc or the close button - so the trigger cannot re-fire. The recorded Done value also takes the invitation button away (spec 211 §4.3).</summary>
    Friend Sub WelcomeRecordExit()
        SaveSetting(App_name, Second_App_Name, WelcomeRules.DoneValue, "1")
        SaveSetting(App_name, Second_App_Name, WelcomeRules.PendingValue, "0")
        welcome_Invite_Eligible = False
        UpdateWelcomeInvite()
    End Sub

    ' ---------------------------------------------------------------- the invitation (spec 211 §4.3) ----

    ''' <summary>
    ''' Shows the invitation button when the profile is eligible and the media surface is free of media or an open
    ''' reference card. The first-run placeholder may stay behind the button. True when the button ended up on
    ''' screen. Cheap enough to call on every media-surface change.
    ''' </summary>
    Private Function UpdateWelcomeInvite() As Boolean
        If Me.IsDisposed OrElse Me.Disposing OrElse MultiWindowPolicy.IsSecondaryInstance() Then Return False
        Dim surface As Panel = panel_Media
        If surface Is Nothing Then Return False
        If welcome_Invite_Button Is Nothing Then
            welcome_Invite_Button = BuildWelcomeInviteButton(surface)
            AddHandler surface.SizeChanged, AddressOf WelcomeInviteSurfaceChanged
            AddHandler surface.BackColorChanged, AddressOf WelcomeInviteSurfaceChanged
            AddHandler surface.ControlAdded, AddressOf WelcomeInviteChildAdded
            AddHandler surface.ControlRemoved, AddressOf WelcomeInviteChildRemoved
            For Each child As Control In surface.Controls
                If child IsNot welcome_Invite_Button Then AddHandler child.VisibleChanged, AddressOf WelcomeInviteSurfaceChanged
            Next
        End If

        Dim offered As Boolean = Not welcome_Open AndAlso welcome_Invite_Eligible
        If offered AndAlso (surface.ClientSize.Width <= 0 OrElse surface.ClientSize.Height <= 0) Then offered = False
        If offered Then
            For Each child As Control In surface.Controls
                If child.Visible AndAlso child IsNot welcome_Invite_Button AndAlso child IsNot lbl_Help_Info Then
                    offered = False
                    Exit For
                End If
            Next
        End If

        If offered Then
            PositionWelcomeInvite(surface)
            welcome_Invite_Button.BringToFront()
        End If
        welcome_Invite_Button.Visible = offered
        surface.Invalidate()
        Return offered
    End Function

    Private Sub WelcomeInviteSurfaceChanged(sender As Object, e As EventArgs)
        UpdateWelcomeInvite()
    End Sub

    Private Sub WelcomeInviteChildAdded(sender As Object, e As ControlEventArgs)
        If e.Control Is welcome_Invite_Button Then Return
        AddHandler e.Control.VisibleChanged, AddressOf WelcomeInviteSurfaceChanged
        UpdateWelcomeInvite()
    End Sub

    Private Sub WelcomeInviteChildRemoved(sender As Object, e As ControlEventArgs)
        RemoveHandler e.Control.VisibleChanged, AddressOf WelcomeInviteSurfaceChanged
        UpdateWelcomeInvite()
    End Sub

    ''' <summary>Builds the invitation button once: a 44 px target on the empty surface, keyboard reachable, in the surface's own inks.</summary>
    Private Function BuildWelcomeInviteButton(surface As Panel) As Button
        Dim button As New Button With {
            .Name = "btn_Welcome_Invite",
            .Visible = False,
            .TabStop = True,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(14, 0, 14, 0),
            .Margin = New Padding(0),
            .FlatStyle = FlatStyle.Flat}
        AddHandler button.Click, AddressOf WelcomeInviteClicked
        surface.Controls.Add(button)
        Return button
    End Function

    Private Sub WelcomeInviteClicked(sender As Object, e As EventArgs)
        ShowWelcome()
    End Sub

    ''' <summary>Centres the button on the media surface's lower half, clear of the empty-state text, and paints it from the surface's own colours so a scheme or theme change needs no re-subscription.</summary>
    Private Sub PositionWelcomeInvite(surface As Panel)
        Dim button As Button = welcome_Invite_Button
        Dim margin As Integer = surface.LogicalToDeviceUnits(24)
        Dim target As Integer = surface.LogicalToDeviceUnits(44)
        button.MinimumSize = New Size(0, target)

        Dim ink As Color = ChromeTheme.ContrastingText(surface.BackColor)
        button.BackColor = ChromeTheme.Blend(surface.BackColor, ink, 0.08F)
        button.ForeColor = ink
        button.FlatAppearance.BorderColor = ChromeTheme.Blend(surface.BackColor, ink, 0.4F)
        button.FlatAppearance.MouseOverBackColor = ChromeTheme.Blend(surface.BackColor, ink, 0.16F)
        button.FlatAppearance.MouseDownBackColor = ChromeTheme.Blend(surface.BackColor, ink, 0.24F)

        If script_Font IsNot Nothing Then button.Font = script_Font
        button.Text = Localization.T("Первоначальная настройка")
        button.AccessibleName = button.Text
        button.RightToLeft = If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No)
        button.MaximumSize = New Size(Math.Max(1, surface.ClientSize.Width - 2 * margin), 0)

        Dim x As Integer = surface.ClientSize.Width - button.PreferredSize.Width
        Dim y As Integer = surface.ClientSize.Height - margin - button.PreferredSize.Height
        button.Location = New Point(Math.Max(0, x \ 2), Math.Max(0, y))
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

    ''' <summary>Step W-4 (spec 211 §4.2): whether Del aims for the Recycle Bin. Written through the settings block at once, the same flush the close path uses, so the choice survives it.</summary>
    Friend Sub WelcomeApplyDeleteToRecycleBin(toBin As Boolean)
        GetModernPreferences().DeleteToRecycleBin = toBin
        SaveSettingsNow()
    End Sub

    ''' <summary>Step W-5: the picture associations, through the same per-user path as the settings window's button.</summary>
    Friend Sub WelcomeRegisterImages()
        AssociateAllImageFormatsWithThisApp()
    End Sub

    ' ---------------------------------------------------------------- the servers block (spec 215) ----

    ''' <summary>
    ''' The finish screen's installed-servers rows, from facts read now: the two component probes are file checks and the
    ''' green fact is one process-name lookup per installed server (spec 215 sections 4-5). Called when the wizard opens
    ''' and again when its finish screen becomes current - no timer, no polling. Nothing here starts a process, opens a
    ''' pipe or claims a port; a lookup that cannot tell is amber, never green.
    ''' </summary>
    Friend Function WelcomeServerBlock() As IReadOnlyList(Of WelcomeServerRow)
        Dim shareInstalled As Boolean = ShareComponentInstalled()
        Dim exchangeInstalled As Boolean = ExchangeFeatures.ComponentInstalled()
        Dim shareUp As Boolean? = If(shareInstalled, ProcessIsUp(WelcomeRules.ShareWorkerProcessName), Nothing)
        Dim exchangeUp As Boolean? = If(exchangeInstalled, ProcessIsUp(WelcomeRules.ExchangeServerProcessName), Nothing)
        Return WelcomeRules.ServerBlock(shareInstalled, shareUp, exchangeInstalled, exchangeUp)
    End Function

    ''' <summary>Whether a process of that name exists; Nothing when the lookup failed. Every returned handle is released.</summary>
    Private Shared Function ProcessIsUp(processName As String) As Boolean?
        Dim found As Process() = Nothing
        Try
            found = Process.GetProcessesByName(processName)
            Return found.Length > 0
        Catch ex As Exception
            AppFileLogger.LogException("Welcome servers block process probe", ex)
            Return Nothing
        Finally
            If found IsNot Nothing Then
                For Each one As Process In found
                    Try : one.Dispose() : Catch : End Try
                Next
            End If
        End Try
    End Function

    ''' <summary>
    ''' A server row's button (spec 215 section 6): the same consent-gated wake the settings button uses, with no folder
    ''' ever sent - a green row also hands the add-resource intent, which an older Share Manager does not know and meets
    ''' as the plain wake. Never starts a server (rule 1) and never runs a companion flow (rule 2).
    ''' </summary>
    Friend Sub WelcomeOpenServerManager(act As ServerAct)
        ActivateShareEntryPoint(managerOnly:=True, addResource:=(act = ServerAct.AddResource))
    End Sub

End Class

#End If
