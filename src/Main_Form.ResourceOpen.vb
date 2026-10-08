#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' How a configured resource is opened from the viewer (007 §3): an address typed or restored, a saved
' resource picked from the list, a document imported. Main_Form.Resource.vb owns what a resource IS once it
' is open (the session, the file list, the refusal point); this partial owns getting there - the password
' when none is stored, the trust prompt for a new server, and what is said when it does not work.
'
' The connection itself is ResourceConnect, with no form in it. What stays here is exactly the part that
' needs a person: three prompts and one status line.
'
' Modern-only.
Partial Public Class Main_Form

    ''' <summary>The connect in flight, cancelled by any change of context (LeaveResource).</summary>
    Private resource_Connect_Cancel As CancellationTokenSource

    ''' <summary>Passwords typed with "remember" off. This run only, by resource id, never written anywhere.</summary>
    Private ReadOnly resource_Session_Passwords As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    Private ReadOnly resource_Secrets As IResourceSecretStore = New RegistryResourceSecretStore()

    Private Const Resource_Menu_Recent_Count As Integer = 5

    ' --------------------------------------------------------------------- entry points ----

    ''' <summary>
    ''' A typed, picked or restored folder that is really a resource address. True means it was taken (the
    ''' open continues on its own and the caller must not read a folder); False leaves it a plain path.
    ''' </summary>
    Friend Function TryOpenAsResourceLocator(text As String) As Boolean
        If Not ResourceLocator.IsLocator(text) Then Return False
        OpenResourceFromLocator(text.Trim())
        Return True
    End Function

    ''' <summary>The viewer's own handling of an argument that is not a path: an address to open, or a
    ''' Share Manager document to import. True when it was taken.</summary>
    Private Function TryHandleResourceArgument(argument As String) As Boolean
        If TryOpenAsResourceLocator(argument) Then Return True
        If Not ResourceImportFlow.IsImportFile(argument) OrElse
           Not String.Equals(Path.GetExtension(argument), ".fmscfg", StringComparison.OrdinalIgnoreCase) Then Return False
        ImportResourceFile(argument)
        Return True
    End Function

    ''' <summary>Imports a dropped or forwarded document. One resource stored opens at once - it is what the
    ''' drop was for; several are left in the manager, where the user picks.</summary>
    Private Sub ImportResourceFile(path As String)
        SlideShowStop()
        Dim stored As IReadOnlyList(Of String) = ResourceImportFlow.Run(Me, ResourceImportFlow.ReadFile(path))
        If stored.Count = 0 Then Return
        RefreshResourceButton()
        If stored.Count = 1 Then
            Dim imported As ResourceDescriptor = ResourceRegistry.Load().FirstOrDefault(Function(r) String.Equals(r.Id, stored(0), StringComparison.OrdinalIgnoreCase))
            If imported IsNot Nothing Then
                ConnectAndEnterResource(imported, "")
                Return
            End If
        End If
        lbl_Status.Text = Localization.TF("Добавлено или обновлено ресурсов: {0}", stored.Count)
        ShowResourceManager()
    End Sub

    ''' <summary>The "Resources.." dialog. What it returns to open is opened here; what was removed while
    ''' it was up is closed here if it is the one on screen. The dialog's managed-share handover is this
    ''' form's own Share entry point - a managed row's Edit/Remove in the dialog wakes the Share Manager
    ''' the same way the Settings window's panel does.</summary>
    Friend Sub ShowResourceManager(Optional draft As ResourceDescriptor = Nothing)
        SlideShowStop()
        Using dialog As New Resource_Manager_Form(draft, AddressOf OpenShareManagerWindow)
            Dim answer As DialogResult = DialogLayer.ShowViewerDialog(dialog, Me)
            ApplyResourceChanges(dialog.RemovedIds)
            If answer = DialogResult.OK AndAlso dialog.ToOpen IsNot Nothing Then ConnectAndEnterResource(dialog.ToOpen, "")
        End Using
    End Sub

    ''' <summary>What every host of a resource editor says back to the viewer when it closes and the
    ''' registry may have changed (ticket 118 13.4): the active resource that was removed is closed,
    ''' the slots that name a resource are dialled afresh, and the toolbar button follows the count.
    ''' One implementation for the manager dialog AND the Settings window, so the two cannot drift.</summary>
    Friend Sub ApplyResourceChanges(removedIds As IReadOnlyList(Of String))
        Dim active As ResourceSession = ActiveResourceSession()
        If active IsNot Nothing AndAlso removedIds.Any(Function(id) String.Equals(id, active.Resource.Id, StringComparison.OrdinalIgnoreCase)) Then
            LeaveResource()
            lbl_Status.Text = Localization.T("Ресурс удалён")
        End If
        ' Whatever was edited - an address, a key, a password, a removal - the slots that name a
        ' resource are dialled and asked afresh (007 §4.2).
        ResetRemoteSlots()
        RefreshResourceButton()
    End Sub

    ''' <summary>The resources used most recently, newest first - what the folder menu offers for one click.</summary>
    Friend Function RecentResources() As List(Of ResourceDescriptor)
        Return ResourceRegistry.Load().
            Where(Function(r) r.LastUsedUtc.HasValue).
            OrderByDescending(Function(r) r.LastUsedUtc.Value).
            Take(Resource_Menu_Recent_Count).ToList()
    End Function

    ''' <summary>The entries of the shared resource popup (ticket 118 Ф-2, Q118-7): saved resources in
    ''' the manager's order (last-used first, then name), capped like the folder menu, then «Управление
    ''' ресурсами..». One builder for the folder menu AND the «Ресурс» button, so the two cannot drift.
    ''' The button lists never-opened resources too (Q118-14); the folder menu keeps its recent rule.</summary>
    Friend Function BuildResourceMenuEntries(Optional includeNeverUsed As Boolean = False) As List(Of ChromeMenuItem)
        Dim entries As New List(Of ChromeMenuItem)()
        ' The registered resources AND the local server's shares (the mirror) - one list,
        ' because both are "what this viewer can open".
        Dim all As List(Of ResourceDescriptor) = WorkerShares.Catalog()
        Dim ordered As IEnumerable(Of ResourceDescriptor) = If(includeNeverUsed, all, all.Where(Function(r) r.LastUsedUtc.HasValue))
        For Each r As ResourceDescriptor In ordered.
                OrderByDescending(Function(x) If(x.LastUsedUtc, DateTime.MinValue)).
                ThenBy(Function(x) x.DisplayName, StringComparer.CurrentCultureIgnoreCase).
                Take(Resource_Menu_Recent_Count)
            Dim chosen As ResourceDescriptor = r
            entries.Add(New ChromeMenuItem("resource_" & r.Id) With {.Caption = r.DisplayName, .Tooltip = ResourceMessages.DisplayAddress(r), .Action = Sub() ConnectAndEnterResource(chosen, "")})
        Next
        entries.Add(New ChromeMenuItem("resource_manage") With {.Caption = Localization.T("Управление ресурсами.."), .Action = Sub() ShowResourceManager()})
        Return entries
    End Function

    ''' <summary>Fills the folder menu's "Resources" submenu: the recent ones, then the manager. Rebuilt on
    ''' every Opening so a resource removed since is never offered.</summary>
    Private Sub RebuildResourceMenu(parent As ToolStripMenuItem)
        parent.DropDownItems.Clear()
        Dim recent As List(Of ResourceDescriptor) = RecentResources()
        For Each r As ResourceDescriptor In recent
            Dim chosen As ResourceDescriptor = r
            Dim item As New ToolStripMenuItem(r.DisplayName) With {.ToolTipText = ResourceMessages.DisplayAddress(r)}
            AddHandler item.Click, Sub() ConnectAndEnterResource(chosen, "")
            parent.DropDownItems.Add(item)
        Next
        If recent.Count > 0 Then parent.DropDownItems.Add(New ToolStripSeparator())
        Dim manage As New ToolStripMenuItem(Localization.T("Управление ресурсами.."))
        AddHandler manage.Click, Sub() ShowResourceManager()
        parent.DropDownItems.Add(manage)
    End Sub

    ''' <summary>
    ''' "RE" inside a resource: ask the server for the folder again, then show it. The listing is replaced
    ''' only when the new one arrived whole - a link that drops mid-way leaves the old list and says so,
    ''' because a failed read must never empty the folder on screen (007 §2.2).
    ''' </summary>
    Friend Async Sub RefreshActiveResource()
        Dim session As ResourceSession = ActiveResourceSession()
        If session Is Nothing Then Return
        Dim generation As Integer = resource_Open_Generation
        lbl_Status.Text = Localization.TF("Обновление списка «{0}»..", session.Resource.DisplayName)
        Try
            Await Task.Run(Sub() session.LoadListing(session.CurrentDirectory, session.Cancellation))
            If generation <> resource_Open_Generation OrElse IsDisposed Then Return
            ReadShowMediaFile(Mode_FolderAndFile)
            ShowResourceListingLimit(session)
        Catch ex As Exception
            If generation <> resource_Open_Generation OrElse IsDisposed Then Return
            AppFileLogger.LogException("Resource: refreshing the listing", ex)
            lbl_Status.Text = Localization.TF("«{0}»: {1}", session.Resource.DisplayName, ResourceMessages.ForFailure(ResourceFailure.FromException(ex)))
        End Try
    End Sub

    ' --------------------------------------------------------------------- an address ----

    Private Sub OpenResourceFromLocator(locator As String)
        Dim user As String = "", host As String = "", port As Integer = 0, remotePath As String = "/"
        If Not ResourceLocator.TryParse(locator, user, host, port, remotePath) Then
            lbl_Status.Text = Localization.T("Адрес ресурса не распознан")
            Return
        End If
        Dim saved As ResourceDescriptor = ResourceMatching.FindForAddress(ResourceRegistry.Load(), host, port, user, remotePath)
        If saved IsNot Nothing Then
            ConnectAndEnterResource(saved, remotePath)
            Return
        End If
        ' An address nobody has set up: the form for a new resource opens with it filled in, because a
        ' password has to come from somewhere and a typed address cannot carry one.
        ShowResourceManager(New ResourceDescriptor With {
            .Kind = ResourceKind.Sftp, .Host = host, .Port = If(port > 0, port, 22), .UserName = user,
            .RootPath = remotePath, .DisplayName = host})
    End Sub

    ' --------------------------------------------------------------------- connecting ----

    ''' <summary>
    ''' Connects to <paramref name="descriptor"/> and shows it as the current folder. Everything here is off
    ''' the UI thread except the three prompts, which are marshalled back; any change of context cancels it.
    ''' </summary>
    ''' <param name="startPath">The folder to open when it is not the resource's own root (an address that
    ''' pointed into it); empty for the root.</param>
    Friend Async Sub ConnectAndEnterResource(descriptor As ResourceDescriptor, startPath As String)
        If descriptor Is Nothing Then Return
        LeaveArchive()
        LeaveResource()
        SlideShowStop()

        ' A share of the local SFTP server is a folder of this machine: it opens the plain
        ' local way - no credential, no dial - and the server's own read-only rule keeps
        ' living on its phone side, in the Share Manager.
        If WorkerShares.IsManaged(descriptor) Then
            OpenManagedShareFolder(descriptor)
            Return
        End If

        Dim cancel As New CancellationTokenSource()
        resource_Connect_Cancel = cancel
        Try
            ' Says how to stop it: a dial to a server can wait a long time, and Esc reads as "close the program" (124 UX-211).
            lbl_Status.Text = Localization.TF("Подключение к «{0}».. Esc - отмена", descriptor.DisplayName)

            ' Ticket 171 rule 4: a resource attached from "My devices" follows its producer - the agent is asked for the current access first, and
            ' when it differs the kept one is replaced and the user told once. Without the agent the kept access simply stays and the resource opens
            ' by its direct addresses as before (rule 8). Nothing happens for a resource of any other origin, nor while the features are not offered.
            If ResourceOrigin.IsExchange(descriptor) AndAlso ExchangeFeatures.IsEnabled() Then
                Dim followed As ExchangeFollowOutcome = Await ExchangeSync.CreateDefault().FollowOneAsync(descriptor, descriptor.ExchangeDeviceName, cancel.Token)
                If cancel.IsCancellationRequested OrElse IsDisposed Then Return
                descriptor = followed.Descriptor
                If followed.Changed Then
                    ClearExchangeNotice(descriptor)
                    Notify(ExchangeResourceText.NoticeLine(descriptor.DisplayName), NotifyLevel.Info)
                End If
            End If

            Dim password As String = StoredPassword(descriptor)
            Dim typed As Boolean
            Dim remember As Boolean
            Dim retry As Boolean
            Dim outcome As ConnectOutcome = Nothing
            Do
                If password Is Nothing Then
                    Using dialog As New Resource_Credential_Form(descriptor.DisplayName, descriptor.UserName, retry)
                        If DialogLayer.ShowViewerDialog(dialog, Me) <> DialogResult.OK Then
                            If resource_Connect_Cancel Is cancel Then lbl_Status.Text = Localization.T("Подключение отменено")
                            Return
                        End If
                        password = dialog.Credential
                        remember = dialog.RememberCredential
                        typed = True
                    End Using
                End If
                If cancel.IsCancellationRequested OrElse IsDisposed Then Return

                Dim chosen As ResourceDescriptor = descriptor
                ' Ticket 171 rule 6: the tunnel is one more candidate of the race, last; Nothing for a resource of any other origin or while the
                ' exchange client features are not offered
                Dim tunnel As IExchangeTunnelSource = ExchangeTunnelSources.For(chosen, resource_Secrets,
                    Function(fingerprint As String, ct As CancellationToken) AskExchangeTrust(chosen, fingerprint),
                    Function(fingerprint As String) PersistTunnelPin(chosen, fingerprint))
                outcome = Await ResourceConnect.ConnectAsync(chosen, password,
                    Function(fingerprint As String, ct As CancellationToken) AskTrust(chosen, fingerprint),
                    Function(fingerprint As String) ResourcePins.Persist(chosen, fingerprint, AddressOf ResourceRegistry.HiveRead, AddressOf ResourceRegistry.HiveWrite),
                    Function() If(chosen.IncludeSubfolders.HasValue, chosen.IncludeSubfolders.Value, modern_Preferences IsNot Nothing AndAlso modern_Preferences.IncludeSubfolders),
                    cancel.Token, Nothing, tunnel)
                If cancel.IsCancellationRequested OrElse IsDisposed Then
                    DirectCast(outcome.Provider, IDisposable)?.Dispose()
                    Return
                End If
                If outcome.Succeeded Then Exit Do

                ' A refused password is asked for again - once per answer, never in a loop of its own.
                If outcome.Failure.Kind = ResourceFailureKind.Auth Then
                    password = Nothing
                    retry = True
                    resource_Session_Passwords.Remove(descriptor.Id)
                    Continue Do
                End If
                ReportConnectFailure(descriptor, outcome)
                Return
            Loop

            RememberAfterSuccess(descriptor, password, typed, remember)
            ' The connect is over: from here a cancel belongs to the session, not to the attempt.
            If resource_Connect_Cancel Is cancel Then resource_Connect_Cancel = Nothing

            Dim opened As ResourceDescriptor = CloneForOpen(descriptor, startPath)
            EnterResource(opened, outcome.Provider)
        Catch ex As Exception
            AppFileLogger.LogException("Resource: connecting to " & descriptor.DisplayName, ex)
            If resource_Connect_Cancel Is cancel AndAlso Not IsDisposed Then lbl_Status.Text = Localization.T("Не удалось открыть ресурс")
        Finally
            If resource_Connect_Cancel Is cancel Then resource_Connect_Cancel = Nothing
            cancel.Dispose()
        End Try
    End Sub

    ''' <summary>A managed share opens like the folder it is: the path goes into the folder
    ''' box and the ordinary local scan reads it - the same triple the typed-folder path
    ''' runs (Main_Form.KeyboardInput). Nothing is stored about it here; the mirror row
    ''' itself stays the Share Manager's. "Not there" and "not for this user" are said
    ''' apart - a service-held FileDO disk is mounted with FileDO's private SYSTEM-only
    ''' DACL, which is a refusal, not an absence.</summary>
    Private Sub OpenManagedShareFolder(descriptor As ResourceDescriptor)
        Dim path As String = If(descriptor.RootPath, "").Trim()
        Select Case WorkerShares.ClassifyFolder(path)
            Case WorkerShares.FolderPresence.Present
                Current_Folder_Path = path
                cmbox_Media_Folder.Text = path
                ReadShowMediaFile(Mode_FolderAndFile)
            Case WorkerShares.FolderPresence.Denied
                lbl_Status.Text = Localization.TF("«{0}»: нет доступа к папке на этом компьютере - она смонтирована службой. Проверьте общую папку в Share Manager", descriptor.DisplayName)
            Case Else
                lbl_Status.Text = Localization.TF("«{0}»: папки нет на диске - проверьте общую папку в Share Manager", descriptor.DisplayName)
        End Select
    End Sub

    ''' <summary>Esc while a connection is being dialled: the attempt is cancelled and the window stays (the
    ''' ladder's CancelConnect rung). The awaiting code sees the cancelled token and unwinds by itself.</summary>
    Private Sub CancelResourceConnect()
        Dim attempt As CancellationTokenSource = resource_Connect_Cancel
        If attempt Is Nothing Then Return
        Try
            attempt.Cancel()
        Catch ex As ObjectDisposedException
            ' The attempt finished a moment ago; nothing is left to cancel.
            Return
        End Try
        Notify(Localization.T("Подключение отменено"), NotifyLevel.Info)
    End Sub

    ''' <summary>The stored credential, the one typed earlier this run, or Nothing when the user must be asked.</summary>
    Private Function StoredPassword(descriptor As ResourceDescriptor) As String
        Dim read As CredentialReadResult = ResourceCommit.ReadCredential(descriptor, resource_Secrets)
        If read.Outcome = CredentialReadOutcome.Ok Then Return read.Secret
        Dim remembered As String = Nothing
        If resource_Session_Passwords.TryGetValue(descriptor.Id, remembered) Then Return remembered
        Return Nothing
    End Function

    ''' <summary>After a connection that worked: a typed password is stored through DPAPI (or kept for this
    ''' run), and the resource's "last used" moves. Both are best effort - a registry that will not take them
    ''' must never undo a connection that is already open.</summary>
    Private Sub RememberAfterSuccess(descriptor As ResourceDescriptor, password As String, typed As Boolean, remember As Boolean)
        Try
            Dim current As List(Of ResourceDescriptor) = ResourceRegistry.Load()
            Dim saved As ResourceDescriptor = current.FirstOrDefault(Function(r) String.Equals(r.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase))
            If typed Then
                ' A slot that names the same resource (007 §4.2) uses the proved password this run
                ' too, instead of asking for it a second time.
                RemoteSlotConnections.Current?.RememberCredential(descriptor.Id, password)
                If remember AndAlso saved IsNot Nothing Then
                    ResourceCommit.Commit(saved, password, AddressOf ResourceRegistry.HiveRead, AddressOf ResourceRegistry.HiveWrite, resource_Secrets)
                Else
                    resource_Session_Passwords(descriptor.Id) = password
                End If
            End If
            current = ResourceRegistry.Load()
            saved = current.FirstOrDefault(Function(r) String.Equals(r.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase))
            If saved IsNot Nothing Then
                saved.LastUsedUtc = DateTime.UtcNow
                ResourceRegistry.Save(current)
            End If
        Catch ex As Exception
            AppFileLogger.WriteLine("Resource: could not record a successful open - " & ex.GetType().Name)
        End Try
    End Sub

    ''' <summary>The descriptor the session is opened with: the resource, rooted where the address pointed.
    ''' The registry entry is untouched - a path typed once is not a new root.</summary>
    Private Shared Function CloneForOpen(descriptor As ResourceDescriptor, startPath As String) As ResourceDescriptor
        Dim opened As ResourceDescriptor = System.Text.Json.JsonSerializer.Deserialize(Of ResourceDescriptor)(System.Text.Json.JsonSerializer.Serialize(descriptor))
        If Not String.IsNullOrEmpty(startPath) AndAlso descriptor.Kind = ResourceKind.Sftp Then opened.RootPath = startPath
        Return opened
    End Function

    ' ------------------------------------------------------------------------ prompts ----

    ''' <summary>The trust prompt for a server not seen before. Runs on the pool thread that is dialling and
    ''' marshals to the UI thread; Cancel is the default, and anything but an explicit Yes is a No.</summary>
    Private Function AskTrust(descriptor As ResourceDescriptor, fingerprint As String) As Boolean
        If IsDisposed OrElse Not IsHandleCreated Then Return False
        Dim answer As Object = Me.Invoke(New Func(Of Object)(
            Function() CObj(ViewerBox.Show(Me,
                Localization.TF("Этот сервер программе ещё незнаком: {0}{1}Отпечаток его ключа:{1}{2}{1}{1}Сверьте его с отпечатком, который вам дал владелец сервера (в Share Manager он показан рядом с адресом). Доверять этому серверу и запомнить его ключ?",
                                descriptor.DisplayName, vbLf, fingerprint),
                Localization.T("Новый сервер"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2))))
        Return CType(answer, DialogResult) = DialogResult.Yes
    End Function

    ''' <summary>The trust prompt for the EXCHANGE server of an outsider link (ticket 171 C4, DEVICE-EXCHANGE item D): asked only for a certificate the
    ''' platform does not trust, with its fingerprint; Cancel is the default and anything but an explicit Yes is a No. Runs on the pool thread that is
    ''' dialling and marshals to the UI thread.</summary>
    Private Function AskExchangeTrust(descriptor As ResourceDescriptor, fingerprint As String) As Boolean
        If IsDisposed OrElse Not IsHandleCreated Then Return False
        Dim answer As Object = Me.Invoke(New Func(Of Object)(
            Function() CObj(ViewerBox.Show(Me, ExchangeResourceText.CertificateQuestion(If(descriptor.TunnelHost, ""), fingerprint),
                                           ExchangeResourceText.CertificateTitle(), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2))))
        Return CType(answer, DialogResult) = DialogResult.Yes
    End Function

    ''' <summary>Stores the exchange certificate the user accepted for this link, BEFORE the tunnel connects. False means it was not stored, and the
    ''' tunnel does not open - there is no "accept any certificate".</summary>
    Private Shared Function PersistTunnelPin(descriptor As ResourceDescriptor, fingerprint As String) As Boolean
        If descriptor Is Nothing OrElse Not ExchangeTrust.IsFingerprint(fingerprint) Then Return False
        Try
            Dim all As List(Of ResourceDescriptor) = ResourceRegistry.Load()
            Dim saved As ResourceDescriptor = all.FirstOrDefault(Function(r) String.Equals(r.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase))
            If saved Is Nothing Then Return False
            saved.TunnelPin = fingerprint
            ResourceRegistry.Save(all)
            descriptor.TunnelPin = fingerprint
            Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is InvalidOperationException
            Return False
        End Try
    End Function

    ''' <summary>The notice that an attached resource's access was updated has been said (in the status line), so the flag goes: told once.</summary>
    Private Shared Sub ClearExchangeNotice(descriptor As ResourceDescriptor)
        Try
            Dim all As List(Of ResourceDescriptor) = ResourceRegistry.Load()
            Dim saved As ResourceDescriptor = all.FirstOrDefault(Function(r) String.Equals(r.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase))
            If saved Is Nothing OrElse saved.ExchangeNotice Is Nothing Then Return
            saved.ExchangeNotice = Nothing
            ResourceRegistry.Save(all)
            descriptor.ExchangeNotice = Nothing
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is InvalidOperationException
            AppFileLogger.WriteLine("Resource: a notice could not be cleared - " & ex.GetType().Name)
        End Try
    End Sub

    ''' <summary>Says why it did not open. A host key that is not the pinned one gets a dialog with BOTH
    ''' fingerprints - it is the one failure that may be an attack and must not scroll past in a status line;
    ''' everything else is one sentence in the status line.</summary>
    Private Sub ReportConnectFailure(descriptor As ResourceDescriptor, outcome As ConnectOutcome)
        Dim refusal As HostKeyDecision = outcome.HostKeyRefusal
        If outcome.Failure.Kind = ResourceFailureKind.KeyMismatch AndAlso refusal IsNot Nothing Then
            lbl_Status.Text = ResourceMessages.ForFailure(outcome.Failure)
            ViewerBox.Show(Me,
                Localization.TF("Ключ сервера «{0}» не совпал с запомненным.{1}Запомнен: {2}{1}Предложен: {3}{1}{1}Это может быть переустановка сервера - а может быть подмена. Подключение отменено. Если вы уверены в сервере, откройте «Изменить..» в списке ресурсов и забудьте его ключ.",
                                descriptor.DisplayName, vbLf, refusal.Expected, refusal.Received),
                Localization.T("Ключ сервера изменился"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If outcome.Failure.Kind = ResourceFailureKind.Cancelled Then
            ' Declining the trust prompt is the user's answer, not a failure to report.
            lbl_Status.Text = Localization.T("Подключение отменено")
            Return
        End If
        lbl_Status.Text = Localization.TF("«{0}»: {1}", descriptor.DisplayName, ResourceMessages.ForFailure(outcome.Failure))
    End Sub

End Class
#End If
