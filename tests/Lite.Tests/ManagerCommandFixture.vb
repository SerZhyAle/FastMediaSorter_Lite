#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading

''' <summary>Exact POSIX names and injectable faults, with no network or user data.</summary>
Friend NotInheritable Class ManagerFakeProvider
    Implements IFileSystemProvider, IManagerDirectoryProvider, ITypedStatProvider, IDisposable

    Friend ReadOnly Entries As New Dictionary(Of String, ResourceEntry)(StringComparer.Ordinal)
    Friend ReadOnly Bytes As New Dictionary(Of String, Byte())(StringComparer.Ordinal)
    Friend Property Caps As ProviderCapabilities = New ProviderCapabilities(supportsCheckedSourceDelete:=True)
    Friend Property DeleteCalls As Integer
    Friend Property RemoveDirectoryCalls As Integer
    Friend Property DisposeCalls As Integer
    Friend Property CorruptUpload As Boolean
    Friend Property DownloadHook As Action(Of String, String)
    Friend Property DenyDelete As Boolean
    Friend Property DeniedTree As String
    Friend Property TruncatedTree As String
    Friend Property StatHook As Action(Of String)
    Friend Property UnknownAfterDelete As Boolean
    Private deleted As String
    Private serial As Integer

    Friend Sub New()
        PutDirectory("/")
        PutDirectory("/root")
    End Sub

    Friend Sub PutDirectory(path As String)
        Put(path, True, Nothing)
    End Sub
    Friend Sub PutFile(path As String, Optional content As String = "payload")
        Put(path, False, Text.Encoding.UTF8.GetBytes(content))
    End Sub
    Private Sub Put(path As String, directory As Boolean, content As Byte())
        serial += 1
        Entries(path) = New ResourceEntry With {.FullPath = path, .Name = ManagerPathRules.LeafName(ResourceKind.Sftp, path),
            .IsDirectory = directory, .Length = If(directory, -1L, content.LongLength),
            .LastWriteUtc = New DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(serial), .VersionTag = serial.ToString()}
        If content IsNot Nothing Then Bytes(path) = content
    End Sub

    Public ReadOnly Property Kind As ResourceKind Implements IFileSystemProvider.Kind
        Get
            Return ResourceKind.Sftp
        End Get
    End Property
    Public ReadOnly Property Capabilities As ProviderCapabilities Implements IFileSystemProvider.Capabilities
        Get
            Return Caps
        End Get
    End Property
    Public Function StatEntry(path As String) As StatResult Implements IManagerDirectoryProvider.StatEntry, ITypedStatProvider.Stat
        StatHook?.Invoke(path)
        If UnknownAfterDelete AndAlso path = deleted Then Return StatResult.Failed(New ResourceFailure(ResourceFailureKind.Transport))
        Dim entry As ResourceEntry = Nothing
        Return If(Entries.TryGetValue(path, entry), StatResult.Found(entry), StatResult.Missing())
    End Function
    Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
        Dim result As StatResult = StatEntry(path)
        entry = result.Entry
        Return result.Outcome = StatOutcome.Found
    End Function
    Public Function ListChildren(path As String, ct As CancellationToken) As DirectoryListing Implements IManagerDirectoryProvider.ListChildren
        ct.ThrowIfCancellationRequested()
        If path = DeniedTree Then Return DirectoryListing.Denied()
        If Not Entries.ContainsKey(path) Then Return DirectoryListing.Missing()
        Return DirectoryListing.Listed(Entries.Values.Where(Function(e) e.FullPath <> path AndAlso ParentOf(e.FullPath) = path).ToList(), path = TruncatedTree)
    End Function
    Public Function ListEntries(path As String, ct As CancellationToken) As IReadOnlyList(Of ResourceEntry) Implements IFileSystemProvider.ListEntries
        Return ListChildren(path, ct).Entries
    End Function
    Public Function PathsEqual(a As String, b As String) As Boolean Implements IManagerDirectoryProvider.PathsEqual
        Return ManagerPathRules.AreEqual(Kind, a, b)
    End Function
    Public Function ParentOf(path As String) As String Implements IManagerDirectoryProvider.ParentOf
        Return ManagerPathRules.ParentOf(Kind, path)
    End Function
    Public Function IsWithinRoot(root As String, path As String) As Boolean Implements IManagerDirectoryProvider.IsWithinRoot
        Return ManagerPathRules.Contains(Kind, root, path)
    End Function
    Public Sub Download(path As String, local As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Download
        ct.ThrowIfCancellationRequested()
        File.WriteAllBytes(local, Bytes(path))
        DownloadHook?.Invoke(path, local)
    End Sub
    Public Sub Upload(local As String, path As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
        ct.ThrowIfCancellationRequested()
        Dim content As Byte() = File.ReadAllBytes(local)
        If CorruptUpload AndAlso content.Length > 0 Then content(0) = content(0) Xor CByte(1)
        Put(path, False, content)
    End Sub
    Public Sub Rename(source As String, target As String) Implements IFileSystemProvider.Rename
        If Entries.ContainsKey(target) Then Throw New IOException("Collision")
        Dim entry As ResourceEntry = Entries(source)
        Entries.Remove(source)
        Entries(target) = New ResourceEntry With {.FullPath = target, .Name = ManagerPathRules.LeafName(Kind, target), .IsDirectory = entry.IsDirectory,
            .Length = entry.Length, .LastWriteUtc = entry.LastWriteUtc, .VersionTag = entry.VersionTag}
        If Bytes.ContainsKey(source) Then
            Bytes(target) = Bytes(source)
            Bytes.Remove(source)
        End If
    End Sub
    Public Sub DeleteFile(path As String) Implements IFileSystemProvider.DeleteFile
        DeleteCalls += 1
        If DenyDelete Then Throw New UnauthorizedAccessException("Denied")
        Entries.Remove(path)
        Bytes.Remove(path)
        deleted = path
    End Sub
    Public Sub CreateDirectory(path As String) Implements IFileSystemProvider.CreateDirectory
        If Entries.ContainsKey(path) Then Throw New IOException("Collision")
        PutDirectory(path)
    End Sub
    Public Function RemoveEmptyDirectory(root As String, path As String) As DirectoryRemoval Implements IManagerDirectoryProvider.RemoveEmptyDirectory
        RemoveDirectoryCalls += 1
        If path = root OrElse Not IsWithinRoot(root, path) Then Return DirectoryRemoval.Refused
        If ListChildren(path, CancellationToken.None).Entries.Count <> 0 Then Return DirectoryRemoval.NotEmpty
        Entries.Remove(path)
        Return DirectoryRemoval.Removed
    End Function
    Public Sub Dispose() Implements IDisposable.Dispose
        DisposeCalls += 1
    End Sub
End Class

Friend Module ManagerCommandFixture
    Friend Function ManagerTestLease(provider As ManagerFakeProvider, Optional id As String = "source", Optional root As String = "/root") As ManagerEndpointLease
        Return New ManagerEndpointLease(New ManagerPlace(id, id, ResourceKind.Sftp, root, id), ManagerOpenResult.Ok(provider, provider))
    End Function
    Friend Function ManagerTestRow(lease As ManagerEndpointLease, path As String) As ManagerEntry
        Return ManagerEntry.FromResource(lease.Place, ManagerTreePlan.RequireEntry(lease, path))
    End Function
    Friend Function MakeManagerRequest(kind As ManagerCommandKind, source As ManagerEndpointLease, paths As String(),
                            Optional destination As ManagerEndpointLease = Nothing, Optional destinationPath As String = "/root",
                            Optional collision As String = "skip", Optional leaf As String = "") As ManagerCommandRequest
        Return New ManagerCommandRequest(kind, ManagerLocation.AtRoot(source.Place), 7,
            paths.Select(Function(p) ManagerTestRow(source, p)), source, destination, destinationPath, leaf, collision, "never", False)
    End Function
    Friend Function BuildManagerTestPlan(request As ManagerCommandRequest, Optional answer As CollisionAnswer? = Nothing) As BatchPlan
        Return ManagerCommandPlanner.Build(request, ManagerTreePlan.Capture(request), Function(lease, path) lease.Directory.StatEntry(path),
            Function(entry) New DeleteDecision With {.Outcome = DeleteOutcome.Permanent, .Reason = PermanentReason.UserAsked}, answer)
    End Function
    Friend Sub ExecuteManagerTestPlan(plan As BatchPlan)
        Dim scratch As String = Path.Combine(Path.GetTempPath(), "fms-manager-execute-" & Guid.NewGuid().ToString("N"))
        Try
            For Each op As BatchOp In plan.Operations
                op.ManagerOperation.Execute(scratch)
            Next
        Finally
            For Each op As BatchOp In plan.Operations
                op.ManagerOperation.UndoData?.Dispose()
            Next
            If Directory.Exists(scratch) Then Directory.Delete(scratch, True)
        End Try
    End Sub
End Module
#End If
