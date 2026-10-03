#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Threading

''' <summary>
''' Filesystem provider for SMB/UNC network shares with app-owned connection leases (100 §15.6 item 6).
''' Uses LocalProvider / standard Windows filesystem APIs after the WNet connection lease
''' is established. NeedsMaterialisation is False because files are directly accessible via UNC path.
''' </summary>
Friend NotInheritable Class SmbProvider
    Implements IFileSystemProvider
    Implements ITypedStatProvider
    Implements IDisposable

    Private ReadOnly descriptor_Value As ResourceDescriptor
    Private ReadOnly lease_Value As SmbConnectionLease
    Private ReadOnly inner_Provider As LocalProvider
    Private ReadOnly capabilities_Value As ProviderCapabilities
    Private is_Disposed As Boolean

    Friend Sub New(descriptor As ResourceDescriptor,
                   lease As SmbConnectionLease,
                   Optional capabilities As ProviderCapabilities = Nothing,
                   Optional includeSubfolders As Func(Of Boolean) = Nothing)
        Me.descriptor_Value = descriptor
        Me.lease_Value = lease
        Me.inner_Provider = New LocalProvider(If(includeSubfolders, Function() True))

        ' SMB capabilities: can write/delete/rename/createFolder, no RecycleBin, no materialisation,
        ' server-side rename supported, Fast cost hint, stable source delete supported.
        ' The user's "read only" choice is honoured here as it is for SFTP: a share they asked to be
        ' left alone must not offer a verb the viewer would then have to refuse after the fact.
        Dim canWrite As Boolean = descriptor Is Nothing OrElse Not descriptor.ReadOnlyHint
        Dim defaultCaps As New ProviderCapabilities(
            canWrite:=canWrite,
            canDelete:=canWrite,
            canRename:=canWrite,
            canCreateFolder:=canWrite,
            hasRecycleBin:=False,
            needsMaterialisation:=False,
            supportsServerSideRename:=True,
            costHint:=ProviderCostHint.Fast,
            supportsAtomicOverwriteRename:=False,
            supportsStableSourceDelete:=True)
        Me.capabilities_Value = If(capabilities, defaultCaps)
    End Sub

    Public ReadOnly Property Kind As ResourceKind Implements IFileSystemProvider.Kind
        Get
            Return ResourceKind.Smb
        End Get
    End Property

    Public ReadOnly Property Capabilities As ProviderCapabilities Implements IFileSystemProvider.Capabilities
        Get
            Return capabilities_Value
        End Get
    End Property

    Friend ReadOnly Property Lease As SmbConnectionLease
        Get
            Return lease_Value
        End Get
    End Property

    Friend ReadOnly Property Descriptor As ResourceDescriptor
        Get
            Return descriptor_Value
        End Get
    End Property

    Public Function ListEntries(dirPath As String, ct As CancellationToken) As IReadOnlyList(Of ResourceEntry) Implements IFileSystemProvider.ListEntries
        CheckNotDisposed()
        Return inner_Provider.ListEntries(dirPath, ct)
    End Function

    Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
        CheckNotDisposed()
        Return inner_Provider.TryStat(path, entry)
    End Function

    Public Function Stat(path As String) As StatResult Implements ITypedStatProvider.Stat
        CheckNotDisposed()
        Return inner_Provider.Stat(path)
    End Function

    Public Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Download
        CheckNotDisposed()
        inner_Provider.Download(remotePath, localPath, progress, ct)
    End Sub

    Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
        CheckNotDisposed()
        RequireCapability(Capabilities.CanWrite)
        inner_Provider.Upload(localPath, remotePath, progress, ct)
    End Sub

    Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
        CheckNotDisposed()
        RequireCapability(Capabilities.CanDelete)
        inner_Provider.DeleteFile(remotePath)
    End Sub

    Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
        CheckNotDisposed()
        RequireCapability(Capabilities.CanRename)
        inner_Provider.Rename(remotePath, newRemotePath)
    End Sub

    Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
        CheckNotDisposed()
        RequireCapability(Capabilities.CanCreateFolder)
        inner_Provider.CreateDirectory(remotePath)
    End Sub

    Private Shared Sub RequireCapability(allowed As Boolean)
        If Not allowed Then Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Unsupported))
    End Sub

    Private Sub CheckNotDisposed()
        If is_Disposed Then
            Throw New ObjectDisposedException(NameOf(SmbProvider), "The SMB provider and its connection lease have been disposed.")
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If Not is_Disposed Then
            is_Disposed = True
            lease_Value?.Dispose()
        End If
    End Sub

End Class

''' <summary>
''' Connects to an SMB resource descriptor by unprotecting credentials and acquiring an app-owned connection lease.
''' </summary>
Friend Module SmbResourceConnector

    Friend Function Connect(descriptor As ResourceDescriptor,
                            secrets As IResourceSecretStore,
                            Optional connectionManager As SmbConnectionManager = Nothing,
                            Optional includeSubfolders As Func(Of Boolean) = Nothing,
                            Optional unprotect As Func(Of String, SecretProtectionResult) = Nothing) As ResourceResult(Of SmbProvider)
        If descriptor Is Nothing Then Throw New ArgumentNullException(NameOf(descriptor))

        Dim rootPath As String = descriptor.RootPath
        If String.IsNullOrWhiteSpace(rootPath) Then
            Return ResourceResult(Of SmbProvider).Fail(New ResourceFailure(ResourceFailureKind.Unsupported, Nothing, "Empty SMB root path."))
        End If

        Dim uncRoot As String = SmbPathUtil.ExtractUncRoot(rootPath)
        If String.IsNullOrEmpty(uncRoot) Then
            Return ResourceResult(Of SmbProvider).Fail(New ResourceFailure(ResourceFailureKind.Unsupported, Nothing, "Invalid UNC root path: " & rootPath))
        End If

        Dim password As String = ""
        If Not String.IsNullOrEmpty(descriptor.SecretHandle) AndAlso secrets IsNot Nothing Then
            Dim blob As String = secrets.GetBlob(descriptor.SecretHandle)
            If Not String.IsNullOrEmpty(blob) Then
                Dim unprotector As Func(Of String, SecretProtectionResult) = If(unprotect, Function(b As String) DpapiSecretChecks.UnprotectChecked(b))
                Dim decResult As SecretProtectionResult = unprotector(blob)
                If decResult.Succeeded Then
                    password = decResult.Value
                Else
                    Return ResourceResult(Of SmbProvider).Fail(New ResourceFailure(ResourceFailureKind.Auth, Nothing, "Failed to decrypt stored credentials."))
                End If
            End If
        End If

        Dim mgr As SmbConnectionManager = If(connectionManager, SmbConnectionManager.SharedInstance)
        Dim leaseResult As ResourceResult(Of SmbConnectionLease) = mgr.AcquireLease(uncRoot, descriptor.UserName, password)
        If Not leaseResult.Succeeded Then
            Return ResourceResult(Of SmbProvider).Fail(leaseResult.Failure)
        End If

        Dim provider As New SmbProvider(descriptor, leaseResult.Value, includeSubfolders:=includeSubfolders)
        Return ResourceResult(Of SmbProvider).Ok(provider)
    End Function

End Module
#End If
