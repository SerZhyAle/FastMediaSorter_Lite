Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Threading.Tasks
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
        ' Said at the moment it is picked, not after a phone fails to open it (ticket 054 H-19).
        If FolderAccess.NetworkRootsInServerMode(New ShareFolder() {New ShareFolder With {.hostPath = path}}).Count > 0 Then
            SetHint(HostingText.FolderNoAccessTip())
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
        If IsDisposed Then Return
        Await ApplySharedFoldersAsync()
    End Function

    Private Async Sub OnRemoveFolder(sender As Object, e As EventArgs)
        If _busy OrElse lvFolders.SelectedItems.Count = 0 Then Return
        Dim it As ListViewItem = lvFolders.SelectedItems(0)
        If TypeOf it.Tag Is ShareFolder Then Return
        Dim host As String = Convert.ToString(it.Tag)
        ' The folder's own settings - PIN, filters, destination - go with it and nothing
        ' brings them back (APP-BEHAVIOUR rule 5, ticket 028 Ф-5). Cancel is the default.
        Dim question As String = Localization.TF(
            "Убрать папку «{0}» из раздачи?" & vbCrLf & vbCrLf &
            "Телефоны перестанут её видеть, а её настройки (PIN, фильтры, папка назначения) будут удалены.",
            If(String.IsNullOrEmpty(host), it.Text, host))
        If WillStopServer(it) Then question &= vbCrLf & vbCrLf & Localization.T("Это последняя отмеченная папка: раздача остановится.")
        If MessageBox.Show(Me, question, Me.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                           MessageBoxDefaultButton.Button2) <> DialogResult.OK Then Return
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
        If TypeOf it.Tag Is ShareFolder Then
            If DirectCast(it.Tag, ShareFolder).owner = "filedo" Then Await ShowDisksAsync()
            Return
        End If
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
                If IsDisposed Then Return
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
        If Not _loading AndAlso TypeOf lvFolders.Items(e.Index).Tag Is ShareFolder Then
            e.NewValue = e.CurrentValue
            Return
        End If
        If _suppressCheck Then
            e.NewValue = e.CurrentValue
            _suppressCheck = False
        End If
    End Sub

    Private Async Sub OnItemChecked(sender As Object, e As ItemCheckedEventArgs)
        If _loading OrElse _busy Then Return
        If TypeOf e.Item.Tag Is ShareFolder Then Return
        ' Unticking the last folder stops a running server and disconnects every phone
        ' on it - asked first (APP-BEHAVIOUR rule 5, ticket 028 Ф-5).
        If Not e.Item.Checked AndAlso ServerIsRunning() AndAlso CurrentShareFolders().Count = 0 AndAlso Not HasExternalRoots() Then
            If MessageBox.Show(Me, Localization.T("Это последняя отмеченная папка. Снять отметку и остановить раздачу? Подключённые телефоны будут отключены."),
                               Me.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                               MessageBoxDefaultButton.Button2) <> DialogResult.OK Then
                Dim prev As Boolean = _loading
                _loading = True
                e.Item.Checked = True
                _loading = prev
                Return
            End If
        End If
        Await ApplySharedFoldersAsync()
    End Sub

    Private Function ServerIsRunning() As Boolean
        Return _status IsNot Nothing AndAlso _status.Running
    End Function

    ''' <summary>True when removing <paramref name="item"/> leaves a running server with no
    ''' checked folder - ApplySharedFoldersAsync then stops it.</summary>
    Private Function WillStopServer(item As ListViewItem) As Boolean
        If HasExternalRoots() Then Return False
        If Not ServerIsRunning() OrElse Not item.Checked Then Return False
        For Each other As ListViewItem In lvFolders.Items
            If other IsNot item AndAlso other.Checked Then Return False
        Next
        Return True
    End Function

    ''' <summary>True when the folder is already in the share list (UI thread).</summary>
    Friend Function HasShareRow(path As String) As Boolean
        If String.IsNullOrWhiteSpace(path) OrElse lvFolders Is Nothing Then Return False
        For Each existing As ListViewItem In lvFolders.Items
            If String.Equals(Convert.ToString(existing.Tag), path, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

    Friend Sub SelectSharedFolder(path As String)
        For Each item As ListViewItem In lvFolders.Items
            If String.Equals(Convert.ToString(item.Tag), path, StringComparison.OrdinalIgnoreCase) Then
                SelectRow(item)
                Return
            End If
        Next
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
                If Not r.IsFmsOwned Then Continue For
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
        If lvFolders.SelectedItems.Count > 0 AndAlso TypeOf lvFolders.SelectedItems(0).Tag Is ShareFolder Then
            btnParams.Enabled = btnParams.Enabled AndAlso DirectCast(lvFolders.SelectedItems(0).Tag, ShareFolder).owner = "filedo"
            btnRemove.Enabled = False
        Else
            btnRemove.Enabled = Not _busy AndAlso WorkerProcess.IsAvailable() AndAlso lvFolders.SelectedItems.Count > 0
        End If
    End Sub

    Private Sub OnListSelectionChanged(sender As Object, e As EventArgs)
        RefreshRowActionButtons()
    End Sub

    Private Function CurrentShareFolders() As List(Of ShareFolder)
        Dim hosts As New List(Of String)()
        Dim wanted As New List(Of String)()
        For Each it As ListViewItem In lvFolders.Items
            If TypeOf it.Tag Is ShareFolder Then Continue For
            If Not it.Checked Then Continue For
            Dim host As String = Convert.ToString(it.Tag)
            If String.IsNullOrEmpty(host) Then Continue For
            hosts.Add(host)
            wanted.Add(it.Text)
        Next
        ' One name builder for every list the worker gets: two folders with the same leaf,
        ' a whole drive or a label with a slash used to take ALL sharing down (ticket 054 H-03).
        Dim names As List(Of String) = ShareRootNames.MakeUnique(wanted, hosts)
        Dim list As New List(Of ShareFolder)()
        For i As Integer = 0 To hosts.Count - 1
            Dim writable As Boolean = ShareRootParamsStore.GetFor(hosts(i)).IsWritable()
            list.Add(New ShareFolder With {.name = names(i), .hostPath = hosts(i), .readOnly = Not writable})
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
    ''' <summary>
    ''' The whole access preflight for a folder set: first the shared folders themselves,
    ''' then everything inside them. The second half is the part that was missing - a
    ''' readable root says nothing about the tree below it, which is how a phone came to
    ''' receive an empty listing for a folder of 758 files while every check here passed.
    ''' Skipped when the user has just declined the first grant: one refusal is an answer,
    ''' asking again in the next breath is nagging.
    ''' </summary>
    Private Async Function EnsureServiceAccessAsync(folders As List(Of ShareFolder)) As Task
        If Await EnsureRootAccessAsync(folders) Then
            Await EnsureSubtreeAccessAsync(folders)
        End If
    End Function

    ''' <summary>Returns False only when the user declined the prompt.</summary>
    Private Async Function EnsureRootAccessAsync(folders As List(Of ShareFolder)) As Task(Of Boolean)
        Dim needy As List(Of ShareFolder) = FolderAccess.RootsNeedingGrant(folders)
        If needy.Count = 0 Then Return True
        If Not ServiceControl.CanManage() Then Return True

        Dim names As New List(Of String)()
        For Each r As ShareFolder In needy
            names.Add(If(r.hostPath, ""))
        Next
        Dim question As String = HostingText.GrantNeededPrompt(String.Join(Environment.NewLine, names))
        If MessageBox.Show(Me, question, Me.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then
            SetHint(HostingText.GrantDeclinedHint())
            Return False
        End If

        SetHint(HostingText.GrantWorkingHint())
        ' Exactly the folders the prompt just listed, and no others: an elevated grant
        ' covers what the person approving it saw (ticket 054 H-09).
        Dim all As New List(Of String)()
        Dim readOnlyOnes As New List(Of String)()
        For Each r As ShareFolder In needy
            Dim host As String = If(r.hostPath, "")
            If host.Length = 0 Then Continue For
            all.Add(host)
            If r.readOnly Then readOnlyOnes.Add(host)
        Next

        Dim res As ServiceControl.ManageResult =
            Await Task.Run(Function() ServiceControl.Manage(ServiceControl.ManageAction.GrantRoots, all, readOnlyOnes))
        If IsDisposed Then Return False
        If res <> ServiceControl.ManageResult.Succeeded Then SetHint(HostingText.ManageResultLine(res))
        Return True
    End Function

    ''' <summary>
    ''' Walks inside the shared folders and offers to repair the subdirectories the
    ''' serving account cannot read.
    '''
    ''' The repair is an explicit ACE for the service account on each blocked folder, not
    ''' "re-enable ACL inheritance". Inheritance was the obvious guess and it is the wrong
    ''' one here: in the incident that prompted this, the folders sat under intermediate
    ''' directories that had inheritance disabled and carried no service ACE of their own,
    ''' so restoring inheritance only ever brought back Everyone - which is what the whole
    ''' share had silently been relying on. It also changes access for every other
    ''' principal at once and leaves no record, where a grant is scoped, recorded in the
    ''' ledger and removed again on revoke.
    '''
    ''' Asked once per folder set: a scan is not free, and a user who said no to this list
    ''' should not meet it again on the next status tick.
    ''' </summary>
    Private Async Function EnsureSubtreeAccessAsync(folders As List(Of ShareFolder)) As Task
        If folders Is Nothing OrElse folders.Count = 0 Then Return
        If Not ServiceControl.CanManage() Then Return

        Dim key As String = String.Join("|", folders.Select(Function(f) If(f?.hostPath, "")))
        If String.Equals(key, _subtreeCheckedKey, StringComparison.OrdinalIgnoreCase) Then Return
        _subtreeCheckedKey = key

        Dim scan As FolderAccess.SubtreeScan = Await Task.Run(Function() FolderAccess.ScanRoots(folders))
        If IsDisposed Then Return
        If scan Is Nothing OrElse scan.Grantable.Count = 0 Then
            ' Blocked by an explicit Deny only: reportable, but a grant cannot fix it, so
            ' it must not raise a UAC prompt that would change nothing.
            If scan IsNot Nothing AndAlso scan.Blocked.Count > 0 Then SetHint(HostingText.SubtreeBlockedWarning(scan.Blocked.Count))
            Return
        End If

        Dim question As String = HostingText.SubtreeGrantPrompt(FolderListForPrompt(scan.Grantable))
        If MessageBox.Show(Me, question, Me.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) <> DialogResult.OK Then
            SetHint(HostingText.SubtreeBlockedWarning(scan.Blocked.Count))
            Return
        End If

        SetHint(HostingText.GrantWorkingHint())
        Dim readOnlyOnes As List(Of String) = BlockedReadOnlySubset(scan.Grantable, folders)
        Dim res As ServiceControl.ManageResult =
            Await Task.Run(Function() ServiceControl.Manage(ServiceControl.ManageAction.GrantRoots, scan.Grantable, readOnlyOnes))
        If IsDisposed Then Return
        SetHint(If(res = ServiceControl.ManageResult.Succeeded, "", HostingText.ManageResultLine(res)))
    End Function

    ''' <summary>A prompt has to fit on screen: show the first few paths in full and count
    ''' the rest. Truncating each path instead would hide the very part that identifies
    ''' the folder, since these differ only deep in the tree.</summary>
    Private Shared Function FolderListForPrompt(paths As List(Of String)) As String
        Const shown As Integer = 12
        If paths.Count <= shown Then Return String.Join(Environment.NewLine, paths)
        Return String.Join(Environment.NewLine, paths.Take(shown)) & Environment.NewLine &
               HostingText.AndMore(paths.Count - shown)
    End Function

    ''' <summary>A blocked subfolder inherits the read/write level of the shared folder it
    ''' sits under - granting it more than its own root has would quietly widen what the
    ''' service can do.
    '''
    ''' The folder a path sits under is the LONGEST shared folder that contains it, compared
    ''' on a path boundary: a raw prefix match put "D:\Photos2\x" under a writable
    ''' "D:\Photos" listed first and granted it read/write (ticket 054 H-25).</summary>
    Friend Shared Function BlockedReadOnlySubset(blocked As List(Of String), folders As List(Of ShareFolder)) As List(Of String)
        Dim result As New List(Of String)()
        For Each path As String In blocked
            Dim owner As ShareFolder = Nothing
            Dim ownerLength As Integer = -1
            For Each f As ShareFolder In folders
                If f Is Nothing OrElse String.IsNullOrEmpty(f.hostPath) Then Continue For
                Dim root As String = f.hostPath.TrimEnd("\"c)
                Dim inside As Boolean = String.Equals(path.TrimEnd("\"c), root, StringComparison.OrdinalIgnoreCase) OrElse
                                        path.StartsWith(root & "\", StringComparison.OrdinalIgnoreCase)
                If inside AndAlso root.Length > ownerLength Then
                    owner = f
                    ownerLength = root.Length
                End If
            Next
            If owner IsNot Nothing AndAlso owner.readOnly Then result.Add(path)
        Next
        Return result
    End Function

    ''' <summary>
    ''' Re-checks which shared folders the service account cannot read, marks those rows
    ''' and returns how many there are.
    '''
    ''' Runs on every status refresh because the answer changes under the window - an
    ''' elevated grant, a folder swapped out, a switch to service hosting - but the DACL
    ''' reads (and the drive probes a disconnected mapped drive can block on for seconds)
    ''' happen OFF the UI thread (ticket 054 H-19): this used to run synchronously twenty
    ''' times in one reachability poll and could pin the window for most of it.
    '''
    ''' The row is coloured rather than renamed: the item text IS the share name the
    ''' phone sees (<see cref="CurrentShareFolders"/> reads it back), so decorating it
    ''' would rename the share.
    ''' </summary>
    Private Function RefreshFolderAccessAsync() As Task
        ' Coalesced: ApplyStatusToUi fires one, and the entry flow awaits one - the same
        ' walk serves both, and a burst of status refreshes cannot queue walks behind
        ' each other.
        If _accessRefresh IsNot Nothing AndAlso Not _accessRefresh.IsCompleted Then Return _accessRefresh
        _accessRefresh = RefreshFolderAccessCoreAsync()
        Return _accessRefresh
    End Function

    Private _accessRefresh As Task

    Private Async Function RefreshFolderAccessCoreAsync() As Task
        If lvFolders Is Nothing Then Return
        ' The ListView is read on the UI thread; only the disk/ACL work moves off it.
        Dim current As List(Of ShareFolder) = CurrentShareFolders()
        Dim key As String = FolderAccessKeyOf(current)
        Dim blocked As HashSet(Of String) = Nothing
        Dim network As HashSet(Of String) = Nothing
        Await Task.Run(Sub()
                           blocked = HostsOf(FolderAccess.RootsNeedingGrant(current))
                           network = HostsOf(FolderAccess.NetworkRootsInServerMode(current))
                       End Sub)
        ' The window may have closed while the walk was out (ticket 054 H-11)...
        If IsDisposed OrElse Disposing Then Return
        ' ...or the list may have moved on - painting a stale answer over a fresh list
        ' would name folders that are no longer shared and miss ones that just were.
        If Not String.Equals(key, FolderAccessKeyOf(CurrentShareFolders()), StringComparison.OrdinalIgnoreCase) Then Return
        _rootsWithoutAccess = ApplyFolderAccessWarnings(blocked, network)
        ' The state line carries the no-access warning; it was painted before this answer
        ' existed, so paint it again now that it does.
        UpdateStateLine(_status IsNot Nothing AndAlso _status.Running)
    End Function

    Private Shared Function HostsOf(roots As List(Of ShareFolder)) As HashSet(Of String)
        Dim set_ As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each r As ShareFolder In roots
            If r IsNot Nothing AndAlso Not String.IsNullOrEmpty(r.hostPath) Then set_.Add(r.hostPath)
        Next
        Return set_
    End Function

    ''' <summary>Identity of a folder list for the refresh guard: read/write matters as
    ''' much as the path, because a writable root asks more of the serving account.</summary>
    Private Shared Function FolderAccessKeyOf(folders As List(Of ShareFolder)) As String
        Dim sb As New System.Text.StringBuilder()
        For Each f As ShareFolder In folders
            sb.Append(If(f?.hostPath, "")).Append(If(f IsNot Nothing AndAlso f.readOnly, "|ro", "|rw")).Append(vbLf)
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Paints the verdicts onto the rows. A network folder is not a missing
    ''' grant - no grant helps it (ticket 054 H-19) - but it is just as unservable, and
    ''' says so in its own words.</summary>
    Private Function ApplyFolderAccessWarnings(blocked As HashSet(Of String), network As HashSet(Of String)) As Integer
        Dim tip As String = HostingText.FolderNoAccessTip()
        Dim networkTip As String = tip
        For Each it As ListViewItem In lvFolders.Items
            Dim host As String = Convert.ToString(it.Tag)
            Dim isNetwork As Boolean = Not String.IsNullOrEmpty(host) AndAlso network.Contains(host)
            Dim bad As Boolean = isNetwork OrElse (Not String.IsNullOrEmpty(host) AndAlso blocked.Contains(host))
            ' Set the ink explicitly: native owner-draw can use black for an empty row colour.
            it.ForeColor = If(bad, ThemeService.Palette.Danger, ThemeService.Palette.TextPrimary)
            it.ToolTipText = If(isNetwork, networkTip, If(bad, tip, ""))
        Next
        Return blocked.Count + network.Count
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
        If IsDisposed Then Return
        ApplyStatusToUi()
    End Function

    Private Async Function ApplySharedFoldersAsync() As Task
        CancelReachabilityPoll()
        SetBusy(True, Localization.T("Обновляю список папок.."))
        Dim folders As List(Of ShareFolder) = CurrentShareFolders()
        Dim refused As String = ""
        If folders.Count = 0 Then
            ' The empty list is PERSISTED before the stop (ticket 046 H-01): a bare stop
            ' left the last folder in the worker's shares.json, served again from the next
            ' worker start on.
            Dim u As ShareController.ShareResult = Await ShareController.UnshareAllAsync()
            _status = If(u.Status, Await ShareController.GetStatusAsync())
            refused = u.ErrorText
        Else
            Await EnsureServiceAccessAsync(folders)
            If IsDisposed Then Return
            Dim r As ShareController.ShareResult = Await ShareController.ShareFoldersAsync(folders)
            _status = r.Status
            refused = r.ErrorText
        End If
        ' The window may have closed while the worker was busy (ticket 054 H-11).
        If IsDisposed Then Return
        ApplyStatusToUi()
        SetBusy(False)
        If refused <> "" Then SetHint(refused)
        If _status IsNot Nothing AndAlso _status.Running AndAlso _status.Reachability Is Nothing Then Await PollReachabilityAsync()
    End Function

    Private Async Sub OnToggle(sender As Object, e As EventArgs)
        If _busy Then Return
        CancelReachabilityPoll()
        SetBusy(True, Localization.T("Минутку.."))
        Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
        If IsDisposed Then Return
        If st IsNot Nothing AndAlso st.Running Then
            SetHint(Localization.T("Останавливаю раздачу.."))
            Await ShareController.StopServerAsync()
            Dim after As WorkerStatus = Await ShareController.GetStatusAsync()
            If IsDisposed Then Return
            _status = after
            ApplyStatusToUi()
            SetBusy(False)
            SetHint(Localization.T("Раздача остановлена."))
            Return
        End If

        Dim folders As List(Of ShareFolder) = CurrentShareFolders()
        If folders.Count = 0 AndAlso Not HasExternalRoots() Then
            SetBusy(False)
            SetHint(Localization.T("Сначала добавьте папку и отметьте её галочкой."))
            Return
        End If
        Await EnsureServiceAccessAsync(folders)
        If IsDisposed Then Return
        SetHint(Localization.T("Включаю раздачу.."))
        Dim res As ShareController.ShareResult = Await ShareController.ShareFoldersAsync(folders)
        If IsDisposed Then Return
        _status = res.Status
        ApplyStatusToUi()
        SetBusy(False)
        ' A worker that answered "no" is a different problem from an address nobody has
        ' confirmed yet, and gets its own words (WORKER-IPC section 3 rule 2).
        If res.ErrorText <> "" Then
            SetHint(res.ErrorText)
        Else
            SetHint(If(res.Served, Localization.T("Раздача запущена."),
                                  Localization.T("Запущено, адрес не подтверждён - проверьте брандмауэр/сеть.")))
        End If
        If _status IsNot Nothing AndAlso _status.Running AndAlso _status.Reachability Is Nothing Then Await PollReachabilityAsync()
    End Sub

    ' --- small helpers ----------------------------------------------------------

    ''' <summary>RO-column cell: the canonical name of status.locked = hard read-only
    ''' (server blocks writes), painted as the padlock glyph by OnFolderDrawSubItem; "~" =
    ''' soft read-only (phone shown read-only, server still writable); blank otherwise.
    ''' The cell keeps a real text so a screen reader says "read-only", not nothing.
    ''' A check mark used to stand here - but a check mark is status.ok / action.confirm,
    ''' so a read-only folder read as "fine" (ICON-SET rule 1, spec 027 phase 7).</summary>
    Private Shared Function RoLabel(p As ShareRootParams) As String
        If p Is Nothing Then Return ""
        If Not p.IsWritable() Then Return HardReadOnlyText()
        If p.SoftReadOnlyInEffect() Then Return "~"   ' a destination ignores the hint (ticket 034)
        Return ""
    End Function

    Private Shared Function HardReadOnlyText() As String
        Return Localization.T(IconMap.GetMeaning("folderReadOnly").LocalizationKey)
    End Function

    ''' <summary>Two kinds of cell are drawn here. A hard read-only RO cell gets the
    ''' status.locked glyph. And every cell of a SELECTED row: once the list loses the
    ''' focus - a click on any button, another window, the screenshot tool - a selected
    ''' row falls back to the classic inactive-selection look, SystemColors.Control grey
    ''' with black text, unreadable on the dark palette; the RO cell already painted
    ''' itself from the palette for that reason. The unselected rows - zebra stripes,
    ''' warning ink, the check boxes, the headers - stay with Windows. In the light
    ''' palette these formulas restate to exactly the classic colours, so light mode
    ''' keeps its look.</summary>
    Private Sub OnFolderDrawSubItem(sender As Object, e As DrawListViewSubItemEventArgs)
        Dim selected As Boolean = e.Item.Selected
        Dim lockCell As Boolean = e.ColumnIndex = 3 AndAlso e.SubItem IsNot Nothing AndAlso
                                  e.SubItem.Text = HardReadOnlyText()
        If Not selected AndAlso Not lockCell Then
            e.DrawDefault = True
            Return
        End If
        e.DrawDefault = False
        Dim focused As Boolean = lvFolders.Focused
        Dim theme As ThemePalette = ThemeService.Palette
        Dim back As Color = If(selected, If(focused, theme.Accent, theme.SurfaceWindow), lvFolders.BackColor)
        Dim ink As Color = If(selected AndAlso focused, theme.AccentInk, theme.Restate(e.Item.ForeColor, False))
        ' Column 0 carries the check box Windows draws at the cell's left edge; the
        ' fill starts at the label so that box is not painted over.
        Dim left As Integer = e.Bounds.Left
        If e.ColumnIndex = 0 Then
            Dim label As Rectangle = lvFolders.GetItemRect(e.ItemIndex, ItemBoundsPortion.Label)
            left = Math.Max(e.Bounds.Left, label.Left)
        End If
        Using b As New SolidBrush(back)
            e.Graphics.FillRectangle(b, left, e.Bounds.Top, e.Bounds.Right - left, e.Bounds.Height)
        End Using
        If lockCell Then
            Dim size As Integer = Math.Min(LogicalToDeviceUnits(16), Math.Max(1, e.Bounds.Height - 2))
            Using glyph As Bitmap = GlyphRenderer.Render("status.locked", size, ink, AddressOf CompanionAssets.OpenBundledAsset)
                e.Graphics.DrawImage(glyph, e.Bounds.Left + (e.Bounds.Width - size) \ 2,
                                     e.Bounds.Top + (e.Bounds.Height - size) \ 2, size, size)
            End Using
            Return
        End If
        Dim cellText As String = If(e.SubItem Is Nothing, "", e.SubItem.Text)   ' column 0's SubItem is the item itself
        If cellText.Length > 0 Then
            Dim bounds As New Rectangle(left + 2, e.Bounds.Top, e.Bounds.Right - left - 4, e.Bounds.Height)
            TextRenderer.DrawText(e.Graphics, cellText, lvFolders.Font, bounds, ink,
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or
                                  TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        End If
    End Sub

    ''' <summary>Owner-drawn full-row-select lists repaint a hovered row without raising
    ''' DrawSubItem for it (a long-standing WinForms quirk), which would wipe the padlock
    ''' until the next full repaint - so a hovered row is invalidated whole.</summary>
    Private Sub OnFolderListMouseMove(sender As Object, e As MouseEventArgs)
        Dim hit As ListViewItem = lvFolders.GetItemAt(e.X, e.Y)
        If hit Is _lastHoveredFolder Then Return
        If _lastHoveredFolder IsNot Nothing AndAlso _lastHoveredFolder.ListView Is lvFolders Then
            lvFolders.Invalidate(_lastHoveredFolder.Bounds)
        End If
        _lastHoveredFolder = hit
        If hit IsNot Nothing Then lvFolders.Invalidate(hit.Bounds)
    End Sub

    Private _lastHoveredFolder As ListViewItem

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

    ''' <summary>A new row's name - the one the phone will see. A drive root used to come out
    ''' as "D:\" (DirectoryInfo's name for it), which the worker refuses as a share name.</summary>
    Private Shared Function ShareFolderDisplayName(path As String) As String
        Dim n As String = ShareRootNames.ForFolder(path)
        Return If(n.Length > 0, n, path)
    End Function

    ''' <summary>Alternate row tint. The light odd-row blue predates the palette and is kept; in
    ''' dark mode the rows alternate between the two surface roles (spec 060). Also restates the
    ''' "blocked folder" ink, which a theme switch would otherwise leave in the other palette.</summary>
    Private Sub RestripeList()
        Dim theme As ThemePalette = ThemeService.Palette
        Dim even As Color = theme.SurfaceRaised
        Dim odd As Color = theme.SurfaceWindow
        For i As Integer = 0 To lvFolders.Items.Count - 1
            Dim it As ListViewItem = lvFolders.Items(i)
            it.BackColor = If((i And 1) = 0, even, odd)
            it.ForeColor = If(ThemePalette.RoleForColor(it.ForeColor, False) = ThemeRole.Danger,
                             theme.Danger, theme.TextPrimary)
        Next
    End Sub

End Class
