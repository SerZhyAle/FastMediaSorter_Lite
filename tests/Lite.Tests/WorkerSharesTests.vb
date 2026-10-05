#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' The local SFTP server's shares in the viewer's resource list (the mirror): the pure half -
''' the share file's parse, the merge with the registered resources, the managed-row test -
''' and the panel's behavior with a managed row selected: the rows appear, the type says where
''' the folder is managed, Edit and Remove hand the user over to the Share Manager without
''' touching the registry, Export is refused with the same sentence, and the test probes the
''' folder itself. The dialogs are driven on an STA thread without being shown.
''' </summary>
<Collection("LocalizationState")>
Public Class WorkerSharesTests

    Private Shared Sub OnSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(20000), "the UI check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    Private Shared Function Managed(idName As String, hostPath As String, Optional readOnlyShare As Boolean = False) As ResourceDescriptor
        Return New ResourceDescriptor With {
            .Id = WorkerShares.ManagedIdPrefix & idName, .DisplayName = idName, .Kind = ResourceKind.Local,
            .RootPath = hostPath, .ReadOnlyHint = readOnlyShare}
    End Function

    Private Shared Function ManagedDisk(idName As String, mountPath As String, readOnlyShare As Boolean) As ResourceDescriptor
        Return New ResourceDescriptor With {
            .Id = WorkerShares.ManagedDiskIdPrefix & idName, .DisplayName = idName, .Kind = ResourceKind.Local,
            .RootPath = mountPath, .ReadOnlyHint = readOnlyShare}
    End Function

    Private NotInheritable Class MemoryStore
        Implements IResourceSecretStore

        Friend ReadOnly Blobs As New Dictionary(Of String, String)()

        Friend Sub PutBlob(handle As String, protectedBlob As String) Implements IResourceSecretStore.PutBlob
            Blobs(handle) = protectedBlob
        End Sub

        Friend Function GetBlob(handle As String) As String Implements IResourceSecretStore.GetBlob
            Dim value As String = Nothing
            Return If(Blobs.TryGetValue(handle, value), value, "")
        End Function

        Friend Sub DeleteBlob(handle As String) Implements IResourceSecretStore.DeleteBlob
            Blobs.Remove(handle)
        End Sub

        Friend Function AllHandles() As IReadOnlyList(Of String) Implements IResourceSecretStore.AllHandles
            Return Blobs.Keys.ToList()
        End Function
    End Class

    Private NotInheritable Class MemoryRegistry
        Friend ReadOnly Values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        Friend Function Read(name As String) As String
            Dim value As String = Nothing
            Return If(Values.TryGetValue(name, value), value, "")
        End Function

        Friend Sub Write(name As String, value As String)
            Values(name) = value
        End Sub
    End Class

    ' ------------------------------------------------------------------------ parse ----

    <Fact>
    Public Sub ParseTurnsEveryServedRootIntoAManagedLocalResource_AndRefusesHalfAnswers()
        Dim json As String = "[" &
            "{""name"": ""MOV"", ""hostPath"": ""D:\\Media\\MOV"", ""readOnly"": true, ""owner"": ""fms""}," &
            "{""name"": ""Photos"", ""hostPath"": ""E:\\Фото"", ""readOnly"": false}," &
            "{""name"": """", ""hostPath"": ""D:\\nameless""}," &
            "{""hostPath"": ""D:\\unnamed""}," &
            "{""name"": ""MOV"", ""hostPath"": ""D:\\duplicate""}]"
        Dim shares As List(Of ResourceDescriptor) = WorkerShares.Parse(json)
        Assert.Equal(2, shares.Count)
        Assert.Equal(WorkerShares.ManagedIdPrefix & "MOV", shares(0).Id)
        Assert.Equal("MOV", shares(0).DisplayName)
        Assert.Equal(ResourceKind.Local, shares(0).Kind)
        Assert.Equal("D:\Media\MOV", shares(0).RootPath)
        Assert.True(shares(0).ReadOnlyHint)
        Assert.Equal("E:\Фото", shares(1).RootPath)
        Assert.False(shares(1).ReadOnlyHint)

        ' Anything that is not the worker's array shows no shares at all - never half a list.
        Assert.Empty(WorkerShares.Parse(""))
        Assert.Empty(WorkerShares.Parse("   "))
        Assert.Empty(WorkerShares.Parse("{""name"": ""MOV""}"))
        Assert.Empty(WorkerShares.Parse("[{broken"))
    End Sub

    <Fact>
    Public Sub TheWorkerFileAsItIsReallyWritten_IsParsed_NameAndHostPathWithCapitalLetters()
        ' The Go worker marshals its Root struct without json tags, so the real shares.json
        ' carries Go's field names. This is the shape found on a live machine (2026-10-05).
        Dim json As String = "[" & vbCrLf &
            "  {" & vbCrLf &
            "    ""Name"": ""MOV""," & vbCrLf &
            "    ""HostPath"": ""I:\\MOV""," & vbCrLf &
            "    ""ReadOnly"": false" & vbCrLf &
            "  }," & vbCrLf &
            "  {" & vbCrLf &
            "    ""Name"": ""Common""," & vbCrLf &
            "    ""HostPath"": ""C:\\Common""," & vbCrLf &
            "    ""ReadOnly"": true" & vbCrLf &
            "  }" & vbCrLf &
            "]"
        Dim shares As List(Of ResourceDescriptor) = WorkerShares.Parse(json)
        Assert.Equal(2, shares.Count)
        Assert.Equal("MOV", shares(0).DisplayName)
        Assert.Equal("I:\MOV", shares(0).RootPath)
        Assert.False(shares(0).ReadOnlyHint)
        Assert.Equal("C:\Common", shares(1).RootPath)
        Assert.True(shares(1).ReadOnlyHint)
    End Sub

    <Fact>
    Public Sub CombinedPutsTheRegisteredResourcesFirst_AndCollapsesMirrorDuplicates()
        Dim registered As New List(Of ResourceDescriptor) From {
            New ResourceDescriptor With {.Id = "sftp1", .Kind = ResourceKind.Sftp, .DisplayName = "NAS"}}
        Dim mirrored As New List(Of ResourceDescriptor) From {
            Managed("MOV", "D:\Media"), Managed("Photos", "E:\Фото"), Managed("MOV", "D:\Other")}
        Dim merged As List(Of ResourceDescriptor) = WorkerShares.Combined(registered, mirrored)
        Assert.Equal({"sftp1", WorkerShares.ManagedIdPrefix & "MOV", WorkerShares.ManagedIdPrefix & "Photos"},
                     merged.Select(Function(r) r.Id))
        Assert.Equal("D:\Media", merged(1).RootPath)

        ' Nothing on either side is handled: a mirror-only list and an empty call both work.
        Assert.Equal(2, WorkerShares.Combined(Nothing, mirrored).Count)
        Assert.Empty(WorkerShares.Combined(Nothing, Nothing))
    End Sub

    <Fact>
    Public Sub IsManagedNamesTheBoundary_ThePrefixAndTheLocalKind()
        Assert.True(WorkerShares.IsManaged(Managed("MOV", "D:\Media")))
        Assert.True(WorkerShares.IsManaged(ManagedDisk("Movies", "E:\", True)))
        Assert.True(WorkerShares.IsManagedFolder(Managed("MOV", "D:\Media")))
        Assert.False(WorkerShares.IsManagedFolder(ManagedDisk("Movies", "E:\", True)))
        Assert.True(WorkerShares.IsManagedDisk(ManagedDisk("Movies", "E:\", True)))
        Assert.False(WorkerShares.IsManagedDisk(Managed("MOV", "D:\Media")))
        ' A registered resource the user set up is never the server's, whatever its id says.
        Assert.False(WorkerShares.IsManaged(New ResourceDescriptor With {
                                                .Id = WorkerShares.ManagedIdPrefix & "MOV", .Kind = ResourceKind.Sftp, .Host = "nas.local", .Port = 22}))
        Assert.False(WorkerShares.IsManaged(New ResourceDescriptor With {.Id = "guid", .Kind = ResourceKind.Local}))
        Assert.False(WorkerShares.IsManaged(Nothing))
    End Sub

    <Fact>
    Public Sub ParseDisksTurnsEachSharedDiskIntoAManagedRow_AndHoldsTheVersionLine()
        Dim json As String = "{" &
            """version"": 1," &
            """disks"": [" &
            "{""containerId"": ""c1"", ""containerPath"": ""D:\\Containers\\movies.fdsk"", ""rootName"": ""Movies"", ""readOnly"": true, ""mountPath"": ""E:\\""}," &
            "{""containerId"": ""c2"", ""containerPath"": ""D:\\Containers\\music.fdsk"", ""rootName"": ""Music""}," &
            "{""containerPath"": ""D:\\Containers\\noname.fdsk"", ""rootName"": ""NoIdentity""}," &
            "{""containerId"": ""c4"", ""containerPath"": ""D:\\Containers\\unnamed.fdsk""}," &
            "{""containerId"": ""c5"", ""containerPath"": ""D:\\Containers\\dupe.fdsk"", ""rootName"": ""Movies""}]}"
        Dim disks As List(Of ResourceDescriptor) = WorkerShares.ParseDisks(json)
        Assert.Equal(2, disks.Count)
        Assert.Equal(WorkerShares.ManagedDiskIdPrefix & "Movies", disks(0).Id)
        Assert.Equal("Movies", disks(0).DisplayName)
        Assert.Equal(ResourceKind.Local, disks(0).Kind)
        Assert.Equal("E:\", disks(0).RootPath)
        Assert.True(disks(0).ReadOnlyHint)
        ' An unmounted disk still appears - it is a resource the server distributes, whose
        ' folder is not on the disk right now.
        Assert.Equal(WorkerShares.ManagedDiskIdPrefix & "Music", disks(1).Id)
        Assert.Equal("", disks(1).RootPath)
        Assert.False(disks(1).ReadOnlyHint)

        ' Any other version is refused outright, exactly as the worker refuses it.
        Assert.Empty(WorkerShares.ParseDisks("{""version"": 2, ""disks"": [{""containerId"": ""c"", ""rootName"": ""X""}]}"))
        Assert.Empty(WorkerShares.ParseDisks("{""disks"": [{""containerId"": ""c"", ""rootName"": ""X""}]}"))
        Assert.Empty(WorkerShares.ParseDisks("[]"))
        Assert.Empty(WorkerShares.ParseDisks("[{broken"))
        Assert.Empty(WorkerShares.ParseDisks(""))
    End Sub

    <Fact>
    Public Sub LoadReadsBothRegistriesBestEffort_AndNeverThrows()
        Dim directoryWithFiles As String = Path.Combine(Path.GetTempPath(), "fms-worker-shares-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(directoryWithFiles)
        Try
            File.WriteAllText(Path.Combine(directoryWithFiles, "shares.json"),
                              "[{""name"": ""MOV"", ""hostPath"": ""D:\\Media\\MOV"", ""readOnly"": true}]")
            File.WriteAllText(Path.Combine(directoryWithFiles, "diskshares.json"),
                              "{""version"": 1, ""disks"": [{""containerId"": ""c1"", ""rootName"": ""Movies"", ""readOnly"": false, ""mountPath"": ""E:\\""}]}")
            Dim loaded As List(Of ResourceDescriptor) = WorkerShares.Load(
                directoryWithFiles, Function(path As String) File.ReadAllText(path))
            Assert.Equal(2, loaded.Count)
            ' The folder shares first, the disks behind them.
            Assert.Equal(WorkerShares.ManagedIdPrefix & "MOV", loaded(0).Id)
            Assert.Equal(WorkerShares.ManagedDiskIdPrefix & "Movies", loaded(1).Id)
            Assert.True(loaded(0).ReadOnlyHint)

            ' A missing registry is simply no rows of its kind.
            File.Delete(Path.Combine(directoryWithFiles, "diskshares.json"))
            Assert.Single(WorkerShares.Load(directoryWithFiles, Function(path As String) File.ReadAllText(path)))

            ' An unreadable one, no directory and no reader are all "no shares".
            Dim unreadable As Func(Of String, String) =
                Function(path As String)
                    Throw New IOException("unreadable")
                End Function
            Assert.Empty(WorkerShares.Load(directoryWithFiles, unreadable))
            Assert.Empty(WorkerShares.Load(Path.Combine(directoryWithFiles, "absent"), Function(path As String) File.ReadAllText(path)))
            Assert.Empty(WorkerShares.Load("", Function(path As String) ""))
            Assert.Empty(WorkerShares.Load(directoryWithFiles, Nothing))
        Finally
            Directory.Delete(directoryWithFiles, True)
        End Try
    End Sub

    ' ------------------------------------------------------------------------ panel ----

    <Fact>
    Public Sub TheServersSharesSitInTheListAsManagedRows_AndTheirCommandsHandOverToTheShareManager()
        OnSta(Sub()
                  Dim hive As New MemoryRegistry()
                  hive.Write(ResourceRegistry.Value_Name, ResourceRegistry.Serialize({
                      New ResourceDescriptor With {
                          .Id = "sftp1", .Kind = ResourceKind.Sftp, .DisplayName = "NAS", .Host = "nas.local", .Port = 22,
                          .UserName = "alice", .RootPath = "/Photos"}}))
                  Dim existingFolder As String = Path.GetTempPath()
                  Dim missingFolder As String = Path.Combine(Path.GetTempPath(), "fms-absent-" & Guid.NewGuid().ToString("N"))
                  Dim shares As New List(Of ResourceDescriptor) From {
                      Managed("share1", existingFolder, True), Managed("share2", missingFolder, False),
                      ManagedDisk("disk1", existingFolder, False)}
                  Dim launched As Integer = 0
                  Using panel As New Resource_List_Panel(1.0F, AddressOf hive.Read, AddressOf hive.Write, New MemoryStore(),
                                                         managedShares:=Function() shares,
                                                         openShareManager:=Sub() launched += 1)
                      Dim list As ListView = DirectCast(panel.Controls.Find("res_List", True)(0), ListView)
                      Dim listHandle As IntPtr = list.Handle
                      Assert.Equal(4, list.Items.Count)

                      Dim row As ListViewItem = list.Items.OfType(Of ListViewItem)().Single(
                          Function(i) DirectCast(i.Tag, ResourceDescriptor).Id = WorkerShares.ManagedIdPrefix & "share1")
                      Assert.Equal(Localization.T("Общая папка (Share Manager)"), row.SubItems(1).Text)
                      Assert.Equal(existingFolder, row.SubItems(2).Text)
                      Dim diskRow As ListViewItem = list.Items.OfType(Of ListViewItem)().Single(
                          Function(i) DirectCast(i.Tag, ResourceDescriptor).Id = WorkerShares.ManagedDiskIdPrefix & "disk1")
                      Assert.Equal(Localization.T("Диск (Share Manager)"), diskRow.SubItems(1).Text)

                      ' Edit on a managed row wakes the Share Manager and leaves the registry alone.
                      row.Selected = True
                      Dim registryBefore As String = hive.Read(ResourceRegistry.Value_Name)
                      panel.GetType().GetMethod("OnEdit", BindingFlags.NonPublic Or BindingFlags.Instance).
                          Invoke(panel, New Object() {Nothing, EventArgs.Empty})
                      Assert.Equal(1, launched)
                      Assert.Equal(registryBefore, hive.Read(ResourceRegistry.Value_Name))
                      Assert.Equal(Localization.T("Этой общей папкой управляет Share Manager - открываю его.."),
                                   DirectCast(panel.Controls.Find("res_Status", True)(0), Label).Text)

                      ' Remove is the same handover - no confirmation, no registry write, no removed id.
                      panel.GetType().GetMethod("OnRemove", BindingFlags.NonPublic Or BindingFlags.Instance).
                          Invoke(panel, New Object() {Nothing, EventArgs.Empty})
                      Assert.Equal(2, launched)
                      Assert.Equal(registryBefore, hive.Read(ResourceRegistry.Value_Name))
                      Assert.Empty(panel.RemovedIds)

                      ' Export is refused with the handover as the reason; the imports stay on.
                      Dim overflow As IEnumerable(Of ChromeMenuItem) = panel.BuildOverflowItems()
                      Dim export As ChromeMenuItem = overflow.Single(Function(item) item.Id = "res_Export")
                      Assert.False(export.Enabled)
                      Assert.Equal(Localization.T("Этой общей папкой управляет Share Manager"), export.DisabledReason)
                      Assert.All(overflow.Where(Function(item) item.Id.StartsWith("res_Import", StringComparison.Ordinal)),
                                 Sub(item) Assert.True(item.Enabled))

                      ' Test asks the folder itself: one that exists opens, one that does not
                      ' says where to look.
                      panel.GetType().GetMethod("OnTest", BindingFlags.NonPublic Or BindingFlags.Instance).
                          Invoke(panel, New Object() {Nothing, EventArgs.Empty})
                      Assert.Equal(Localization.T("Папка открывается"),
                                   DirectCast(panel.Controls.Find("res_Status", True)(0), Label).Text)
                      Dim missing As ListViewItem = list.Items.OfType(Of ListViewItem)().Single(
                          Function(i) DirectCast(i.Tag, ResourceDescriptor).Id = WorkerShares.ManagedIdPrefix & "share2")
                      missing.Selected = True
                      panel.GetType().GetMethod("OnTest", BindingFlags.NonPublic Or BindingFlags.Instance).
                          Invoke(panel, New Object() {Nothing, EventArgs.Empty})
                      Assert.Equal(Localization.T("Папки нет на диске - проверьте общую папку в Share Manager"),
                                   DirectCast(panel.Controls.Find("res_Status", True)(0), Label).Text)
                  End Using
              End Sub)
    End Sub

    ' ------------------------------------------------------------------ presence ----

    <Fact>
    Public Sub ClassifyFolderTellsThereFromNotThere()
        Dim present As String = Path.GetTempPath()
        Dim missing As String = Path.Combine(Path.GetTempPath(), "fms-absent-" & Guid.NewGuid().ToString("N"))
        Assert.Equal(WorkerShares.FolderPresence.Present, WorkerShares.ClassifyFolder(present))
        Assert.Equal(WorkerShares.FolderPresence.Missing, WorkerShares.ClassifyFolder(missing))
        Assert.Equal(WorkerShares.FolderPresence.Missing, WorkerShares.ClassifyFolder(""))
    End Sub

End Class
#End If
