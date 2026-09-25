#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq

' The viewer's side of the provider layer (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md).
'
' A plain folder - local or UNC - is listed through LocalProvider, which is the scan the
' viewer always had, moved. A configured resource (SFTP and friends, ticket 007) is shown
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

    ''' <summary>The one open resource, if any - Nothing-equivalent (IsOpen = False) for a
    ''' plain folder, which is the only thing that can be open until ticket 007.</summary>
    Private ReadOnly resource_Slot As New ResourceSessionSlot()

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

    ' ------------------------------------------------------------ the refusal point ----

    ''' <summary>
    ''' The one place that refuses a file verb, and says so in one sentence (PV-7) - for an
    ''' archive, which is view-only (010 §7), and for a resource whose capabilities do not
    ''' include the verb. Returns True when the caller must stop. Asked BEFORE any state
    ''' changes, so a refused verb never half-happens.
    ''' </summary>
    Friend Function ResourceBlocksFileOperations(verb As ResourceVerb) As Boolean
        If IsArchiveMode() Then
            lbl_Status.Text = Localization.T("В архиве файловые операции недоступны")
            Return True
        End If
        ' A stream from "Open URL" is on screen and the folder's file is only underneath it:
        ' a verb here would act on a file the user cannot see (audit 050 D-05).
        If IsStreamOnScreen() Then
            lbl_Status.Text = Localization.T("Открыт поток по URL - файловые операции недоступны")
            Return True
        End If
        If ResourceAllows(verb) Then Return False
        lbl_Status.Text = Localization.T("Этот ресурс не позволяет эту операцию с файлами")
        Return True
    End Function

    ''' <summary>The same answer without the status line - for surfaces that draw a verb
    ''' disabled rather than refuse it (the recipients overlay).</summary>
    Friend Function ResourceAllows(verb As ResourceVerb) As Boolean
        If IsStreamOnScreen() Then Return False
        Dim session As ResourceSession = ActiveResourceSession()
        If session Is Nothing Then Return True
        Return ResourceRefusalPolicy.IsAllowed(session.Provider.Capabilities, verb)
    End Function

    ' ------------------------------------------------------------ entering/leaving ----

    ''' <summary>
    ''' Opens a configured resource as the current folder - the mirror of EnterArchive. The
    ''' folder box shows its locator (<c>sftp://user@host:port/path</c>), never the cache.
    ''' Nothing in this build calls it yet: the only provider shipped is LocalProvider, and a
    ''' plain folder is not a resource. Ticket 007 adds the providers and the entry points.
    ''' </summary>
    Friend Sub EnterResource(resource As ResourceDescriptor, provider As IFileSystemProvider)
        LeaveArchive()
        LeaveResource()
        SlideShowStop()

        Dim sessionDir As String = ""
        Dim session As ResourceSession = Nothing
        Try
            ' Only now, not at startup: someone who never opens a resource should not pay
            ' for a directory walk.
            RemoteCacheStore.SweepOrphans()

            If provider.Capabilities.NeedsMaterialisation Then sessionDir = RemoteCacheStore.CreateSession()
            session = New ResourceSession(resource, provider, sessionDir,
                                          CLng(GetModernPreferences().RemoteCacheMaxMb) * 1024L * 1024L)
            session.LoadListing(resource.RootPath, session.Cancellation)
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
            ApplyRecipientsOverlayHealth()
        Catch ex As Exception
            AppFileLogger.LogException("Resource: opening " & If(resource?.DisplayName, ""), ex)
            If session IsNot Nothing AndAlso resource_Slot.Current Is session Then
                resource_Slot.Leave(AddressOf ReleaseActiveMedia)
            Else
                session?.Dispose()
                RemoteCacheStore.DeleteSession(sessionDir)
            End If
            lbl_Status.Text = Localization.T("Не удалось открыть ресурс")
        End Try
    End Sub

    ''' <summary>
    ''' Closes the open resource and its cache directory. Called before ANY change of
    ''' context - an archive, a folder, a file, shutdown - and a no-op when none is open,
    ''' which today is always. Playback and the picture boxes let go of the materialised
    ''' file first (ResourceSessionSlot.Leave).
    ''' </summary>
    Friend Sub LeaveResource()
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
        Dim listed As IReadOnlyList(Of ResourceEntry) =
            If(session IsNot Nothing, session.Entries, local_Provider.ListEntries(folderPath, Threading.CancellationToken.None))

        Dim rows As New List(Of FileEntry)(listed.Count)
        For Each entry As ResourceEntry In listed
            If Not all_Supported_Extensions.Contains(Path.GetExtension(entry.Name).ToLowerInvariant()) Then Continue For
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
        Return Enumerable.Count(listed, Function(entry) all_Supported_Extensions.Contains(Path.GetExtension(entry.Name).ToLowerInvariant()))
    End Function

    ' --------------------------------------------------------- bytes on this machine ----

    ''' <summary>
    ''' Puts the file behind <paramref name="filePath"/> on disk when it belongs to an open
    ''' archive or resource; a no-op for a plain file. Must run BEFORE anything asks whether
    ''' the file exists - until it is fetched it does not, and the display path drops a
    ''' missing file from the list.
    ''' </summary>
    Friend Sub EnsureEntryLocal(filePath As String)
        EnsureArchiveEntryOnDisk(filePath)

        Dim session As ResourceSession = ActiveResourceSession()
        If session Is Nothing OrElse String.IsNullOrEmpty(filePath) Then Return
        Dim outcome As EnsureLocalOutcome = session.TryEnsureLocal(
            filePath, protectAround:=Math.Max(1, RemoteCacheStore.PrefetchDepth(session.Provider.Capabilities)))
        If outcome = EnsureLocalOutcome.Failed Then
            lbl_Status.Text = Localization.T("Не удалось получить файл с ресурса")
        End If
    End Sub

    ''' <summary>What to call a resource file on screen: its own name, never the cache path.</summary>
    Friend Function ResourceDisplayName(filePath As String) As String
        Dim session As ResourceSession = ActiveResourceSession()
        If session Is Nothing OrElse Not session.NeedsMaterialisation Then Return ""
        Dim entry As ResourceEntry = session.EntryAt(filePath)
        Return If(entry Is Nothing, "", entry.Name)
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
