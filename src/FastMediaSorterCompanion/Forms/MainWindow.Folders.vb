Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' The shared-folder list and the server operations behind it: add, remove, configure,
''' check/uncheck, and the Start/Stop that those actions mostly make unnecessary.
'''
''' Every method here moved verbatim out of the single-file MainWindow when the class was
''' split (SPECIFICATION_SHARE_MANAGER_COMPACT_WINDOW.md §3.9) - the compact-window work is
''' layout, not a model change, and nothing about WHEN a folder reaches the worker was
''' touched.
''' </summary>
Partial Public NotInheritable Class MainWindow

    ' --- folder-list handlers ---------------------------------------------------

    Private Async Sub OnAddFolder(sender As Object, e As EventArgs)
        If _busy Then Return
        Dim picked As String = Nothing
        Using dlg As New FolderBrowserDialog() With {.ShowNewFolderButton = False,
                .Description = Localization.T("Выберите папку, которую хотите открыть на телефоне")}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            picked = dlg.SelectedPath
        End Using
        Await AddFolderInteractive(picked)
    End Sub

    Private Async Sub OnAddCurrentFolder(sender As Object, e As EventArgs)
        If _busy OrElse _initialFolder.Length = 0 Then Return
        Try
            If Directory.Exists(_initialFolder) Then Await AddFolderInteractive(_initialFolder)
        Catch
        End Try
    End Sub

    Private Async Function AddFolderInteractive(path As String) As Task
        If String.IsNullOrWhiteSpace(path) Then Return
        If Not AddShareRow(path) Then
            SetHint(Localization.T("Эта папка уже в списке."))
            Return
        End If
        Dim before As ShareRootParams = ShareRootParamsStore.GetFor(path)
        Using dlg As New Share_Root_Params_Form(ShareFolderDisplayName(path), before)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                ShareRootParamsStore.SetFor(path, dlg.Result)
                For Each it As ListViewItem In lvFolders.Items
                    If String.Equals(Convert.ToString(it.Tag), path, StringComparison.OrdinalIgnoreCase) Then
                        Dim lbl As String = If(dlg.Result.Label, "").Trim()
                        If lbl.Length > 0 Then it.Text = lbl
                        it.SubItems(1).Text = ProfileLabel(dlg.Result)
                        it.SubItems(3).Text = RoLabel(dlg.Result)
                        Exit For
                    End If
                Next
            End If
        End Using
        Await ApplySharedFoldersAsync()
    End Function

    Private Async Sub OnRemoveFolder(sender As Object, e As EventArgs)
        If _busy OrElse lvFolders.SelectedItems.Count = 0 Then Return
        Dim it As ListViewItem = lvFolders.SelectedItems(0)
        Dim host As String = Convert.ToString(it.Tag)
        If Not String.IsNullOrEmpty(host) Then ShareRootParamsStore.RemoveFor(host)
        If String.Equals(host, _lastTouchedRoot, StringComparison.OrdinalIgnoreCase) Then _lastTouchedRoot = ""
        lvFolders.Items.Remove(it)
        RestripeList()
        SelectDefaultRow()
        Await ApplySharedFoldersAsync()
    End Sub

    Private Async Sub OnConfigureFolder(sender As Object, e As EventArgs)
        If _busy OrElse lvFolders.SelectedItems.Count = 0 Then Return
        Dim it As ListViewItem = lvFolders.SelectedItems(0)
        Dim host As String = Convert.ToString(it.Tag)
        If String.IsNullOrEmpty(host) Then Return
        Dim before As ShareRootParams = ShareRootParamsStore.GetFor(host)
        Using dlg As New Share_Root_Params_Form(it.Text, before)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim after As ShareRootParams = dlg.Result
            ShareRootParamsStore.SetFor(host, after)
            _lastTouchedRoot = host
            it.SubItems(1).Text = ProfileLabel(after)
            it.SubItems(3).Text = RoLabel(after)
            If before.IsWritable() <> after.IsWritable() AndAlso it.Checked AndAlso _status IsNot Nothing AndAlso _status.Running Then
                Await ApplySharedFoldersAsync()
            End If
        End Using
    End Sub

    Private Sub OnListMouseDown(sender As Object, e As MouseEventArgs)
        Try
            Dim hit As ListViewHitTestInfo = lvFolders.HitTest(e.Location)
            _suppressCheck = (e.Clicks = 2 AndAlso hit IsNot Nothing AndAlso hit.Location <> ListViewHitTestLocations.StateImage)
        Catch
        End Try
    End Sub

    Private Sub OnItemCheck(sender As Object, e As ItemCheckEventArgs)
        If _suppressCheck Then
            e.NewValue = e.CurrentValue
            _suppressCheck = False
        End If
    End Sub

    Private Async Sub OnItemChecked(sender As Object, e As ItemCheckedEventArgs)
        If _loading OrElse _busy Then Return
        Await ApplySharedFoldersAsync()
    End Sub

    Private Function AddShareRow(path As String) As Boolean
        If String.IsNullOrWhiteSpace(path) Then Return False
        For Each existing As ListViewItem In lvFolders.Items
            If String.Equals(Convert.ToString(existing.Tag), path, StringComparison.OrdinalIgnoreCase) Then Return False
        Next
        ' A freshly added folder defaults to the "Все файлы"/all_files profile (owner
        ' request) unless it already carries saved params. Persist it so the column, the
        ' resource dialog and the .fmscfg export all start from all_files. The class
        ' default stays "none" (Android import default / v1-purity invariant); only a
        ' new add in this app opts into all_files.
        Dim prm As ShareRootParams = ShareRootParamsStore.GetFor(path)
        If prm.IsDefault() Then
            prm.Profile = "all_files"
            ShareRootParamsStore.SetFor(path, prm)
        End If
        Dim prev As Boolean = _loading
        _loading = True
        Try
            Dim it As New ListViewItem(ShareFolderDisplayName(path)) With {.Checked = True, .Tag = path}
            it.SubItems.Add(ProfileLabel(prm))
            it.SubItems.Add(path)
            it.SubItems.Add(RoLabel(prm))
            lvFolders.Items.Add(it)
            _lastTouchedRoot = path
            SelectRow(it)
        Finally
            _loading = prev
        End Try
        RestripeList()
        Return True
    End Function

    Private Sub PopulateFolders(roots As List(Of ShareFolder))
        If roots Is Nothing Then Return
        Dim prev As Boolean = _loading
        _loading = True
        lvFolders.BeginUpdate()
        Try
            lvFolders.Items.Clear()
            For Each r As ShareFolder In roots
                Dim host As String = If(r.hostPath, "")
                If host.Length = 0 Then Continue For
                Dim prm As ShareRootParams = ShareRootParamsStore.GetFor(host)
                Dim it As New ListViewItem(If(String.IsNullOrEmpty(r.name), ShareFolderDisplayName(host), r.name)) With {.Checked = True, .Tag = host}
                it.SubItems.Add(ProfileLabel(prm))
                it.SubItems.Add(host)
                it.SubItems.Add(RoLabel(prm))
                lvFolders.Items.Add(it)
            Next
            RestripeList()
            SelectDefaultRow()
        Finally
            lvFolders.EndUpdate()
            _loading = prev
        End Try
        ' Inside BeginUpdate/EndUpdate the ListView does not raise SelectedIndexChanged for
        ' every language, so the button state is asked for once the list is settled.
        RefreshRowActionButtons()
    End Sub

    ''' <summary>
    ''' Puts the selection back on the row that was created or edited last, so the list
    ''' opens on the folder the user was working with rather than on nothing at all. With
    ''' no such row - a fresh session - the last row wins: folders are appended, so it is
    ''' the most recently added one.
    ''' </summary>
    Private Sub SelectDefaultRow()
        If lvFolders Is Nothing OrElse lvFolders.Items.Count = 0 Then Return
        If lvFolders.SelectedItems.Count > 0 Then Return
        Dim target As ListViewItem = Nothing
        If _lastTouchedRoot.Length > 0 Then
            For Each it As ListViewItem In lvFolders.Items
                If String.Equals(Convert.ToString(it.Tag), _lastTouchedRoot, StringComparison.OrdinalIgnoreCase) Then
                    target = it
                    Exit For
                End If
            Next
        End If
        SelectRow(If(target, lvFolders.Items(lvFolders.Items.Count - 1)))
    End Sub

    ''' <summary>Selects one row and scrolls it into view. Selecting is not focusing: the
    ''' list is not given the keyboard focus here, because this runs while the user may be
    ''' anywhere else in the window.</summary>
    Private Sub SelectRow(it As ListViewItem)
        If it Is Nothing Then Return
        Try
            lvFolders.SelectedItems.Clear()
            it.Selected = True
            it.Focused = True
            it.EnsureVisible()
        Catch
        End Try
        RefreshRowActionButtons()
    End Sub

    ''' <summary>
    ''' "Редактировать" acts on THE selected row, so it is offered only when there is one.
    ''' The busy/worker-availability rule <see cref="SetBusy"/> applies to every button
    ''' still holds - this is the single place where the two are combined.
    ''' </summary>
    Private Sub RefreshRowActionButtons()
        If btnParams Is Nothing OrElse lvFolders Is Nothing Then Return
        btnParams.Enabled = Not _busy AndAlso WorkerProcess.IsAvailable() AndAlso
                            lvFolders.Enabled AndAlso lvFolders.SelectedItems.Count > 0
    End Sub

    Private Sub OnListSelectionChanged(sender As Object, e As EventArgs)
        RefreshRowActionButtons()
    End Sub

    Private Function CurrentShareFolders() As List(Of ShareFolder)
        Dim list As New List(Of ShareFolder)()
        For Each it As ListViewItem In lvFolders.Items
            If Not it.Checked Then Continue For
            Dim host As String = Convert.ToString(it.Tag)
            If String.IsNullOrEmpty(host) Then Continue For
            Dim writable As Boolean = ShareRootParamsStore.GetFor(host).IsWritable()
            list.Add(New ShareFolder With {.name = it.Text, .hostPath = host, .readOnly = Not writable})
        Next
        Return list
    End Function

    ' --- server ops -------------------------------------------------------------

    ''' <summary>
    ''' Gives the service account access to the folders that are about to be served, at
    ''' the moment they are chosen - which is the only moment a user has any reason to
    ''' think about it.
    '''
    ''' In Server mode the worker runs as LOCAL SERVICE, so a folder the person picking
    ''' it can obviously read may be invisible to the thing that actually serves it.
    ''' That grant used to be a button in the Hosting console, which is a fair repair
    ''' path and a terrible primary one: nothing about adding a folder suggests you must
    ''' then go and find it, and until you did, the phone got an empty or unopenable
    ''' folder with no explanation anywhere.
    '''
    ''' No-op unless something is genuinely missing, so the common case adds no prompt:
    ''' <see cref="FolderAccess.RootsNeedingGrant"/> reads the ACLs without elevation
    ''' first, and only a real gap raises the one UAC prompt. Declining is allowed and
    ''' says what it costs - the folders stay in the list and the console can still fix
    ''' them later.
    ''' </summary>
    Private Async Function EnsureServiceAccessAsync(folders As List(Of ShareFolder)) As Task
        Dim needy As List(Of ShareFolder) = FolderAccess.RootsNeedingGrant(folders)
        If needy.Count = 0 Then Return
        If Not ServiceControl.CanManage() Then Return

        Dim names As New List(Of String)()
        For Each r As ShareFolder In needy
            names.Add(If(r.hostPath, ""))
        Next
        Dim question As String = HostingText.GrantNeededPrompt(String.Join(Environment.NewLine, names))
        If MessageBox.Show(Me, question, Me.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then
            SetHint(HostingText.GrantDeclinedHint())
            Return
        End If

        SetHint(HostingText.GrantWorkingHint())
        Dim all As New List(Of String)()
        Dim readOnlyOnes As New List(Of String)()
        For Each r As ShareFolder In folders
            Dim host As String = If(r.hostPath, "")
            If host.Length = 0 Then Continue For
            all.Add(host)
            If r.readOnly Then readOnlyOnes.Add(host)
        Next

        Dim res As ServiceControl.ManageResult =
            Await Task.Run(Function() ServiceControl.Manage(ServiceControl.ManageAction.GrantRoots, all, readOnlyOnes))
        If res <> ServiceControl.ManageResult.Succeeded Then SetHint(HostingText.ManageResultLine(res))
    End Function

    ''' <summary>
    ''' Re-checks which shared folders the service account cannot read, marks those rows
    ''' and returns how many there are.
    '''
    ''' Runs on every status refresh because the answer changes under the window - an
    ''' elevated grant, a folder swapped out, a switch to service hosting - and it can
    ''' afford to: one DACL read per folder, no elevation, no worker round-trip.
    '''
    ''' The row is coloured rather than renamed: the item text IS the share name the
    ''' phone sees (<see cref="CurrentShareFolders"/> reads it back), so decorating it
    ''' would rename the share.
    ''' </summary>
    Private Function RefreshFolderAccessWarnings() As Integer
        If lvFolders Is Nothing Then Return 0

        Dim blocked As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each r As ShareFolder In FolderAccess.RootsNeedingGrant(CurrentShareFolders())
            If r IsNot Nothing AndAlso Not String.IsNullOrEmpty(r.hostPath) Then blocked.Add(r.hostPath)
        Next

        Dim tip As String = HostingText.FolderNoAccessTip()
        For Each it As ListViewItem In lvFolders.Items
            Dim host As String = Convert.ToString(it.Tag)
            Dim bad As Boolean = Not String.IsNullOrEmpty(host) AndAlso blocked.Contains(host)
            it.ForeColor = If(bad, Color.Firebrick, SystemColors.WindowText)
            it.ToolTipText = If(bad, tip, "")
        Next
        Return blocked.Count
    End Function

    ''' <summary>
    ''' Offers the grant for folders that are already being served without it - the
    ''' state an installation lands in when the service was registered by something that
    ''' had no folder list to hand it (the Server installer over a User edition that was
    ''' already sharing), or when the offer at folder-pick time was declined.
    '''
    ''' The elevated helper now also grants on every service start, so this is the second
    ''' net rather than the only one: it covers the case where the service is already up
    ''' and nobody is going to restart it.
    ''' </summary>
    Private Async Function OfferGrantForServedFoldersAsync() As Task
        If _grantOffered Then Return
        If _rootsWithoutAccess = 0 Then Return
        _grantOffered = True
        Await EnsureServiceAccessAsync(CurrentShareFolders())
        ApplyStatusToUi()
    End Function

    Private Async Function ApplySharedFoldersAsync() As Task
        CancelReachabilityPoll()
        SetBusy(True, Localization.T("Обновляю список папок.."))
        Dim folders As List(Of ShareFolder) = CurrentShareFolders()
        If folders.Count = 0 Then
            Await ShareController.StopServerAsync()
            _status = Await ShareController.GetStatusAsync()
        Else
            Await EnsureServiceAccessAsync(folders)
            Dim r As ShareController.ShareResult = Await ShareController.ShareFoldersAsync(folders)
            _status = r.Status
        End If
        ApplyStatusToUi()
        SetBusy(False)
        If _status IsNot Nothing AndAlso _status.Running AndAlso _status.Reachability Is Nothing Then Await PollReachabilityAsync()
    End Function

    Private Async Sub OnToggle(sender As Object, e As EventArgs)
        If _busy Then Return
        CancelReachabilityPoll()
        SetBusy(True, Localization.T("Минутку.."))
        Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
        If st IsNot Nothing AndAlso st.Running Then
            SetHint(Localization.T("Останавливаю раздачу.."))
            Await ShareController.StopServerAsync()
            _status = Await ShareController.GetStatusAsync()
            ApplyStatusToUi()
            SetBusy(False)
            SetHint(Localization.T("Раздача остановлена."))
            Return
        End If

        Dim folders As List(Of ShareFolder) = CurrentShareFolders()
        If folders.Count = 0 Then
            SetBusy(False)
            SetHint(Localization.T("Сначала добавьте папку и отметьте её галочкой."))
            Return
        End If
        Await EnsureServiceAccessAsync(folders)
        SetHint(Localization.T("Включаю раздачу.."))
        Dim res As ShareController.ShareResult = Await ShareController.ShareFoldersAsync(folders)
        _status = res.Status
        ApplyStatusToUi()
        SetBusy(False)
        SetHint(If(res.Served, Localization.T("Раздача запущена."),
                              Localization.T("Запущено, адрес не подтверждён - проверьте брандмауэр/сеть.")))
        If _status IsNot Nothing AndAlso _status.Running AndAlso _status.Reachability Is Nothing Then Await PollReachabilityAsync()
    End Sub

    ' --- small helpers ----------------------------------------------------------

    ''' <summary>RO-column cell: "✓" = hard read-only (server blocks writes); "~" =
    ''' soft read-only (phone shown read-only, server still writable); blank otherwise.</summary>
    Private Shared Function RoLabel(p As ShareRootParams) As String
        If p Is Nothing Then Return ""
        If Not p.IsWritable() Then Return "✓"
        If p.SoftReadOnly Then Return "~"
        Return ""
    End Function

    ''' <summary>"Тип" column: the folder's export profile in the same words the
    ''' resource dialog's "Тип ресурса" combo uses. A plain (none) folder shows
    ''' blank - like the RO column, the cell is empty at the default.</summary>
    Private Shared Function ProfileLabel(p As ShareRootParams) As String
        Dim token As String = If(p Is Nothing OrElse String.IsNullOrEmpty(p.Profile), "none", p.Profile)
        Select Case token
            Case "audio_library" : Return Localization.T("Аудиотека")
            Case "video_library" : Return Localization.T("Видеотека")
            Case "photo_storage" : Return Localization.T("Фотохранилище")
            Case "documents" : Return Localization.T("Документы")
            Case "all_files" : Return Localization.T("Все файлы")
            Case Else : Return ""   ' none / regular folder - keep the cell clean
        End Select
    End Function

    Private Shared Function ShareFolderDisplayName(path As String) As String
        Try
            Dim n As String = New DirectoryInfo(path).Name
            If Not String.IsNullOrEmpty(n) Then Return n
        Catch
        End Try
        Return path
    End Function

    Private Sub RestripeList()
        Dim odd As Color = Color.FromArgb(244, 247, 252)
        For i As Integer = 0 To lvFolders.Items.Count - 1
            lvFolders.Items(i).BackColor = If((i And 1) = 0, SystemColors.Window, odd)
        Next
    End Sub

End Class
