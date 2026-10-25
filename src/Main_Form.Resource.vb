#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

' The viewer's side of the provider layer (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md).
'
' A plain folder - local or UNC - is listed through LocalProvider, which is the scan the
' viewer always had, moved. A configured resource (an SFTP server or a network share, ticket 007) is shown
' the way an archive is: the file list holds paths in a cache session directory, and each
' file appears there the moment something is about to show it, so the decoder, the
' prefetch, zoom, OCR and LibVLC keep working on plain paths.
'
' What this partial owns: entering and leaving a resource, the rows for GetFiles, putting
' an entry on disk, and the ONE point that refuses a file verb (for archives and resources
' alike). What it deliberately does not own: the cache (RemoteCacheStore), the session
' (ResourceSession) and the routing of FileManager's verbs (ResourceRouting).
'
' Modern-only.
Partial Public Class Main_Form

    ''' <summary>The one open resource, if any - IsOpen = False for a plain folder, which is what
    ''' is on screen unless the user opened an SFTP server or a network share (ticket 007,
    ''' Main_Form.ResourceOpen.vb).</summary>
    Private ReadOnly resource_Slot As New ResourceSessionSlot()
    Private resource_Pending As ResourceSession
    Private resource_Open_Generation As Integer
    Private resource_Display_Cancel As CancellationTokenSource

    ''' <summary>Plain folders. Reads the subfolder preference on every listing, as the
    ''' scan it replaces did - including from the prefetch worker's thread.</summary>
    Private ReadOnly local_Provider As New LocalProvider(
        Function() modern_Preferences IsNot Nothing AndAlso modern_Preferences.IncludeSubfolders)

    Friend Function IsResourceMode() As Boolean
        Return resource_Slot.IsOpen
    End Function

    ''' <summary>The open resource's session, or Nothing for a plain folder.</summary>
    Private Function ActiveResourceSession() As ResourceSession
        Return resource_Slot.Current
    End Function

    ''' <summary>
    ''' Where the viewer is standing, for the F3 panel's file manager mode (257): the registry id of the open resource and the
    ''' directory it shows, or "" and the plain folder (the folder an archive sits in, for an archive). It reads three fields - it
    ''' lists nothing, takes no snapshot of the file list and cannot change what the viewer shows.
    ''' </summary>
    Friend Sub ManagerStartFolder(ByRef resourceId As String, ByRef directory As String)
        resourceId = ""
        directory = ""
        Dim resource As ResourceSession = ActiveResourceSession()
        If resource IsNot Nothing AndAlso resource.Resource IsNot Nothing Then
            resourceId = resource.Resource.Id
            directory = If(String.IsNullOrEmpty(resource.CurrentDirectory), resource.Resource.RootPath, resource.CurrentDirectory)
            Return
        End If
        directory = If(IsArchiveMode(), archive_Return_Folder, Current_Folder_Path)
    End Sub

    ''' <summary>The password the file manager may use for a resource without asking anyone: the stored one, else the one typed earlier
    ''' this run, else Nothing. UI thread only - it reads the run's password table.</summary>
    Friend Function ManagerCredentialOf(descriptor As ResourceDescriptor) As String
        Return StoredPassword(descriptor)
    End Function

    ' ------------------------------------------------------------ the refusal point ----

    ''' <summary>
    ''' The one place that refuses a file verb, and says so in one sentence (PV-7) - for an
    ''' archive, which is view-only (010 §7), and for a resource whose capabilities do not
    ''' include the verb. Returns True when the caller must stop. Asked BEFORE any state
    ''' changes, so a refused verb never half-happens.
    ''' </summary>
    Friend Function ResourceBlocksFileOperations(verb As ResourceVerb) As Boolean
        Return ResourceBlocksFileOperations(verb, ViewerOperationContext())
    End Function

    Friend Function ResourceBlocksFileOperations(verb As ResourceVerb, context As ResourceOperationContext) As Boolean
        Dim silent As Boolean
        Dim reason As String = ResourceRefusalReason(verb, context, silent)
        If reason.Length > 0 Then
            lbl_Status.Text = reason
            ' A refusal nobody could see afterwards: the status line is overwritten by the next key. The
            ' log keeps the verb and the sentence, so "it would not copy" can be answered from current.log.
            AppFileLogger.WriteLine("File operation refused: " & verb.ToString() & " -> " & reason)
        End If
        Return reason.Length > 0 OrElse silent
    End Function

    ''' <summary>
    ''' Why a file verb is refused right now, in one localized sentence - or "" when it is allowed. The one
    ''' place the rule lives: <see cref="ResourceBlocksFileOperations"/> says it on the status line, and the
    ''' media menus (125 UX-326) draw the same verb disabled with the same sentence as its tooltip instead of
    ''' hiding it or letting the click fail. <paramref name="silent"/> is set when the verb is refused with
    ''' nothing to say (a resource is still opening).
    ''' </summary>
    Friend Function ResourceRefusalReason(verb As ResourceVerb, ByRef silent As Boolean) As String
        Return ResourceRefusalReason(verb, ViewerOperationContext(), silent)
    End Function

    Friend Function ResourceRefusalReason(verb As ResourceVerb, context As ResourceOperationContext, ByRef silent As Boolean) As String
        silent = False
        ' Also while a solid archive is still being unpacked: the picture on screen belongs to
        ' the previous folder, and the verb was meant for the archive that is opening.
        If context.IsPending Then
            silent = True
            Return ""
        End If
        If context.InsideArchive Then Return Localization.T("В архиве файловые операции недоступны")
        ' A stream from "Open URL" is on screen and the folder's file is only underneath it:
        ' a verb here would act on a file the user cannot see (audit 050 D-05).
        If context.IsStream Then Return Localization.T("Открыт поток по URL - файловые операции недоступны")
        If ResourceRefusalPolicy.IsAllowed(context, verb) Then Return ""
        Return Localization.T("Этот ресурс не позволяет эту операцию с файлами")
    End Function

    Private Function ViewerOperationContext() As ResourceOperationContext
        Return New ResourceOperationContext With {
            .SourceCapabilities = ActiveResourceSession()?.Provider.Capabilities,
            .InsideArchive = IsArchiveMode() OrElse IsArchiveOpenPending(),
            .IsPending = resource_Pending IsNot Nothing,
            .IsStream = IsStreamOnScreen()}
    End Function

    ''' <summary>The same answer without the status line - for surfaces that draw a verb
    ''' disabled rather than refuse it (the recipients overlay).</summary>
    Friend Function ResourceAllows(verb As ResourceVerb) As Boolean
        ' An archive is view-only (010 §7): the recipients overlay draws every row muted.
        If IsArchiveMode() OrElse IsArchiveOpenPending() Then Return False
        If resource_Pending IsNot Nothing OrElse resource_Display_Cancel IsNot Nothing Then Return False
        If IsStreamOnScreen() Then Return False
        Dim session As ResourceSession = ActiveResourceSession()
        If session Is Nothing Then Return True
        Return ResourceRefusalPolicy.IsAllowed(session.Provider.Capabilities, verb)
    End Function

    ' ------------------------------------------------------------ entering/leaving ----

    ''' <summary>
    ''' Opens a configured resource as the current folder - the mirror of EnterArchive. The
    ''' folder box shows its locator (<c>sftp://user@host:port/path</c>), never the cache.
    ''' Reached through <c>ConnectAndEnterResource</c> (Main_Form.ResourceOpen.vb), which has
    ''' already connected, asked for what was missing and pinned the host key: this method
    ''' only turns a connected provider into the current folder, and disposes it again if that
    ''' fails. A plain folder is not a resource and never comes through here.
    ''' </summary>
    Friend Async Sub EnterResource(resource As ResourceDescriptor, provider As IFileSystemProvider)
        LeaveArchive()
        LeaveResource()
        SlideShowStop()

        Dim generation As Integer = resource_Open_Generation
        ' Only a plain folder can be restored: the previous resource/archive is closed.
        Dim previousFolder As String = If(ResourceLocator.IsLocator(Current_Folder_Path) OrElse
                                          RemoteCachePaths.IsSessionCopy(Current_Folder_Path), "", Current_Folder_Path)
        Dim previousBox As String = If(previousFolder = "", "", cmbox_Media_Folder.Text)
        Dim sessionDir As String = ""
        Dim session As ResourceSession = Nothing
        Try
            ' Only now, not at startup: someone who never opens a resource should not pay
            ' for a directory walk.
            RemoteCacheStore.SweepOrphans()

            If provider.Capabilities.NeedsMaterialisation Then sessionDir = RemoteCacheStore.CreateSession()
            session = New ResourceSession(resource, provider, sessionDir,
                                          CLng(GetModernPreferences().RemoteCacheMaxMb) * 1024L * 1024L)
            resource_Pending = session
            Dim listing As Task = Task.Run(Sub() session.LoadListing(resource.RootPath, session.Cancellation))
            Dim watch As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
            Dim frame As Integer
            While Not listing.IsCompleted
                If generation <> resource_Open_Generation OrElse IsDisposed Then Exit While
                ShowLoadingStatus(resource.DisplayName)
                ShowLoadingBadge(resource.DisplayName, 0, watch.Elapsed, frame)
                frame += 1
                Await Task.WhenAny(listing, Task.Delay(loading_Badge_Frame_Ms))
            End While
            Await listing
            If generation <> resource_Open_Generation OrElse IsDisposed Then Return
            resource_Pending = Nothing
            HideLoadingBadge()
            resource_Slot.Enter(session, AddressOf ReleaseActiveMedia)

            Current_Folder_Path = session.Locator
            is_TextBox_Editing = True
            cmbox_Media_Folder.Text = session.Locator
            is_TextBox_Editing = False

            current_File_Index = 0
            Current_File_Name = ""
            Current_Image_Path = ""
            is_External_Input_Received = False
            was_External_Input_Previously = False

            ReadShowMediaFile(Mode_FolderAndFile)
            ShowResourceListingLimit(session)
            ApplyRecipientsOverlayHealth()
        Catch ex As Exception
            AppFileLogger.LogException("Resource: opening " & If(resource?.DisplayName, ""), ex)
            If session IsNot Nothing AndAlso resource_Slot.Current Is session Then
                resource_Slot.Leave(AddressOf ReleaseActiveMedia)
            Else
                session?.Dispose()
                ' A connection that never became a session is still a connection: let go of it.
                If session Is Nothing Then TryCast(provider, IDisposable)?.Dispose()
                RemoteCacheStore.DeleteSession(sessionDir)
            End If
            If generation = resource_Open_Generation AndAlso Not IsDisposed Then
                resource_Pending = Nothing
                HideLoadingBadge()
                RestoreResourceFolder(previousFolder, previousBox)
                ' One sentence that says WHICH kind of trouble (the folder is gone, the server said no, the
                ' link dropped), never the exception's own text.
                lbl_Status.Text = Localization.TF("«{0}»: {1}", If(resource?.DisplayName, ""),
                                                  ResourceMessages.ForFailure(ResourceFailure.FromException(ex)))
            End If
        End Try
    End Sub

    Private Sub ShowResourceListingLimit(session As ResourceSession)
        Dim sftp As SftpProvider = TryCast(session.Provider, SftpProvider)
        If sftp IsNot Nothing AndAlso sftp.WasTruncated Then
            lbl_Status.Text = Localization.TF("Показаны первые {0} записей", session.Entries.Count.ToString())
        End If
    End Sub

    ''' <summary>
    ''' Closes the open resource and its cache directory. Called before ANY change of
    ''' context - an archive, a folder, a file, shutdown - and a no-op when none is open,
    ''' which today is always. Playback and the picture boxes let go of the materialised
    ''' file first (ResourceSessionSlot.Leave).
    ''' </summary>
    Friend Sub LeaveResource()
        resource_Open_Generation += 1
        ' A connect that is still dialling belongs to the context being left (Main_Form.ResourceOpen.vb).
        resource_Connect_Cancel?.Cancel()
        resource_Display_Cancel?.Cancel()
        resource_Display_Cancel = Nothing
        If resource_Pending IsNot Nothing Then
            Dim pending As ResourceSession = resource_Pending
            resource_Pending = Nothing
            pending.Dispose()
            RemoteCacheStore.DeleteSession(pending.SessionDir)
        End If
        HideLoadingBadge()
        If Not resource_Slot.IsOpen Then Return
        resource_Slot.Leave(Sub()
                                SlideShowStop()
                                ReleaseActiveMedia()
                            End Sub)
        ApplyRecipientsOverlayHealth()
    End Sub

    ' --------------------------------------------------------------- the file list ----

    ''' <summary>
    ''' The rows GetFiles sorts: the open resource's listing, or the plain folder's scan
    ''' through LocalProvider. Same filter, same fields, same local time as the DirectoryInfo
    ''' scan this replaces - LastWriteTime IS LastWriteTimeUtc.ToLocalTime().
    ''' </summary>
    Private Function ProviderFileEntries(folderPath As String) As List(Of FileEntry)
        Dim session As ResourceSession = ActiveResourceSession()
        Dim listed As IReadOnlyList(Of ResourceEntry) = Nothing
        If session IsNot Nothing Then
            listed = session.Entries
        Else
            ' The walk of a plain folder may already have been done off the UI thread
            ' (014 U-03c, Main_Form.FolderScanAsync.vb): the listing is taken from there, and the
            ' FILTER below still runs here, on the thread that owns all_Supported_Extensions.
            ' A scan that failed rethrows its own exception, so GetFiles classifies it exactly
            ' as it classifies a synchronous one.
            listed = TakeScannedListing(folderPath)
            If listed Is Nothing Then listed = local_Provider.ListEntries(folderPath, Threading.CancellationToken.None)
        End If

        Dim rows As New List(Of FileEntry)(listed.Count)
        For Each entry As ResourceEntry In listed
            If entry.IsDirectory OrElse Not MediaBrowsing.IsEligible(entry.FullPath, all_Supported_Extensions) Then Continue For
            rows.Add(New FileEntry With {
                .FilePath = entry.LocalPath,
                .FileSize = entry.Length,
                .FileName = entry.Name,
                .FileDate = entry.LastWriteUtc.ToLocalTime()
            })
        Next
        Return rows
    End Function

    ''' <summary>How many listable files a folder holds - the prefetch worker's background
    ''' count. Inside a resource the listing already knows.</summary>
    Private Function CountProviderFiles(folderPath As String, session As ResourceSession) As Integer
        Dim listed As IReadOnlyList(Of ResourceEntry)
        Try
            listed = If(session IsNot Nothing, session.Entries, local_Provider.ListEntries(folderPath, Threading.CancellationToken.None))
        Catch ex As Exception
            ' The scan throws for a root it cannot read (audit 048, A-07). A background count
            ' has nobody to tell: -1 means "no count", and the prefetch goes on.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0085: background count failed: " & ex.Message)
            Return -1
        End Try
        ' Enumerable.Count by name: on an IReadOnlyList VB binds ".Count(" to the property.
        Return Enumerable.Count(listed, Function(entry) Not entry.IsDirectory AndAlso MediaBrowsing.IsEligible(entry.FullPath, all_Supported_Extensions))
    End Function

    ' --------------------------------------------------------- bytes on this machine ----

    ''' <summary>
    ''' Puts the file behind <paramref name="filePath"/> on disk when it belongs to an open
    ''' archive or resource; a no-op for a plain file. Must run BEFORE anything asks whether
    ''' the file exists - until it is fetched it does not, and the display path drops a
    ''' missing file from the list.
    ''' </summary>
    Friend Async Function EnsureEntryLocal(filePath As String) As Task(Of Boolean)
        EnsureArchiveEntryOnDisk(filePath)
        Dim session As ResourceSession = ActiveResourceSession()
        If session Is Nothing OrElse String.IsNullOrEmpty(filePath) Then Return True
        Dim generation As Integer = media_Generation
        resource_Display_Cancel?.Cancel()
        Dim cancel As New CancellationTokenSource()
        resource_Display_Cancel = cancel
        Dim watch As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
        Dim savedStatus As String = Nothing
        Try
            Dim depth As Integer = Math.Max(1, RemoteCacheStore.PrefetchDepth(session.Provider.Capabilities))
            Dim token As CancellationToken = cancel.Token
            Dim loadedBytes As Long = 0
            Dim progress As IProgress(Of Long) = New ActionProgress(Of Long)(Sub(b) Interlocked.Exchange(loadedBytes, b))
            Dim fetch As Task(Of EnsureLocalOutcome) = Task.Run(
                Function() session.TryEnsureLocal(filePath, token, progress, protectAround:=depth))
            Dim frame As Integer
            Dim entry As ResourceEntry = session.EntryAt(filePath)
            Dim totalBytes As Long = If(entry IsNot Nothing AndAlso entry.Length > 0, entry.Length, 0L)
            While Not fetch.IsCompleted
                If generation <> media_Generation OrElse session IsNot ActiveResourceSession() OrElse IsDisposed Then
                    cancel.Cancel()
                    Return False
                End If
                If watch.ElapsedMilliseconds >= loading_Badge_Delay_Ms Then
                    If savedStatus Is Nothing Then savedStatus = ShowLoadingStatus(filePath)
                    If totalBytes <= 0 Then totalBytes = If(session.EntryAt(filePath)?.Length, 0L)
                    ShowLoadingBadge(filePath, totalBytes, watch.Elapsed, frame, Interlocked.Read(loadedBytes))
                    frame += 1
                End If
                Await Task.WhenAny(fetch, Task.Delay(loading_Badge_Frame_Ms))
            End While
            Dim outcome As EnsureLocalOutcome = Await fetch
            If generation <> media_Generation OrElse session IsNot ActiveResourceSession() OrElse IsDisposed Then Return False
            If outcome = EnsureLocalOutcome.Ready OrElse outcome = EnsureLocalOutcome.NotOurs Then Return True
            BlankActiveMediaSurface()
            lbl_Status.Text = Localization.T("Не удалось получить файл с ресурса")
            savedStatus = Nothing
            Return False
        Catch ex As Exception
            AppFileLogger.LogException("Resource: fetching for display", ex)
            If generation = media_Generation AndAlso Not IsDisposed Then
                BlankActiveMediaSurface()
                lbl_Status.Text = Localization.T("Не удалось получить файл с ресурса")
                savedStatus = Nothing
            End If
            Return False
        Finally
            If resource_Display_Cancel Is cancel Then
                resource_Display_Cancel = Nothing
                HideLoadingBadge()
                RestoreLoadingStatus(savedStatus)
            End If
            cancel.Dispose()
        End Try
    End Function

    Private Sub RestoreResourceFolder(folder As String, box As String)
        ResourceSessionSlot.RestoreFolderAfterFailure(folder, box,
            Sub(path, text)
                Current_Folder_Path = path
                is_TextBox_Editing = True
                Try
                    cmbox_Media_Folder.Text = text
                Finally
                    is_TextBox_Editing = False
                End Try
            End Sub)
        Current_File_Name = ""
        Current_Image_Path = ""
        current_File_Index = 0
        total_File_Count = 0
        files_Array = Array.Empty(Of String)()
        files_List = New List(Of String)()
        BlankActiveMediaSurface()
        lbl_File_Number.Text = ""
        lbl_Current_File.Text = ""
        ApplyRecipientsOverlayHealth()
    End Sub

    ''' <summary>What to call a resource file on screen: its own name, never the cache path.</summary>
    Friend Function ResourceDisplayName(filePath As String) As String
        Dim session As ResourceSession = ActiveResourceSession()
        If session Is Nothing OrElse Not session.NeedsMaterialisation Then Return ""
        Dim entry As ResourceEntry = session.EntryAt(filePath)
        Return If(entry Is Nothing, "", entry.Name)
    End Function

    ''' <summary>The name a file goes under when it is moved or copied into a slot: its own name. A
    ''' file of an open resource is listed as its cache copy, "00042_photo.jpg", and that index
    ''' prefix used to become the file's name in the slot.</summary>
    Friend Function TransferNameOf(filePath As String) As String
        Dim real As String = ResourceDisplayName(If(filePath, ""))
        If real.Length > 0 Then Return real
        Return Path.GetFileName(If(filePath, ""))
    End Function

    ''' <summary>Where a file really is, spelled the way a slot would name it: a resource file's
    ''' address on its server (a locator for SFTP, its UNC path for a share), any other file its
    ''' own path. What "the slot is the folder this file is already in" is decided on.</summary>
    Friend Function LogicalPathOf(filePath As String) As String
        Dim session As ResourceSession = ActiveResourceSession()
        If session Is Nothing OrElse Not session.NeedsMaterialisation Then Return filePath
        Dim entry As ResourceEntry = session.EntryAt(filePath)
        If entry Is Nothing Then Return filePath
        If session.Resource IsNot Nothing AndAlso session.Resource.Kind = ResourceKind.Sftp Then
            Return ResourceLocator.AppendName(ResourceLocator.Build(session.Resource, ResourceTransfer.ParentOf(entry.FullPath)), entry.Name)
        End If
        Return entry.FullPath
    End Function

    ''' <summary>
    ''' The files the prefetch should materialise after the one on screen (PV-6), in list
    ''' order, wrapping like the "next file" does. Empty - and free - for a plain folder.
    ''' </summary>
    Private Function ResourcePrefetchAhead() As List(Of String)
        Dim ahead As New List(Of String)()
        Dim session As ResourceSession = ActiveResourceSession()
        If session Is Nothing Then Return ahead
        Dim depth As Integer = RemoteCacheStore.PrefetchDepth(session.Provider.Capabilities)
        If depth <= 0 OrElse total_File_Count <= 1 Then Return ahead

        For stepAhead As Integer = 1 To Math.Min(depth, total_File_Count - 1)
            Dim at As Integer = (current_File_Index + stepAhead) Mod total_File_Count
            Dim candidate As String = If(is_Files_Array_Active,
                                         If(files_Array IsNot Nothing AndAlso at < files_Array.Length, files_Array(at), Nothing),
                                         If(files_List IsNot Nothing AndAlso at < files_List.Count, files_List(at), Nothing))
            If Not String.IsNullOrEmpty(candidate) Then ahead.Add(candidate)
        Next
        Return ahead
    End Function

End Class
#End If
