#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Net.Sockets
Imports System.Threading
Imports System.Threading.Tasks
Imports Renci.SshNet
Imports Renci.SshNet.Common
Imports Renci.SshNet.Sftp

''' <summary>
''' Filesystem provider for remote SFTP resources backed by SSH.NET (007_SPECIFICATION_REMOTE_RESOURCES_SFTP_SMB_DOTNET10.md).
''' Manages one connection per session, keepalive at 30s, reconnect with backoff (1s, 4s, 10s),
''' host key verification via <see cref="HostKeyGate"/>, and failure classification through <see cref="ResourceFailure"/>.
''' </summary>
Friend NotInheritable Class SftpProvider
    Implements IFileSystemProvider
    Implements ITypedStatProvider
    Implements IDisposable

    Private ReadOnly descriptor_Value As ResourceDescriptor
    Private ReadOnly hostKeyGate_Value As HostKeyGate
    Private ReadOnly capabilities_Value As ProviderCapabilities
    Private ReadOnly include_Subfolders As Func(Of Boolean)
    Private ReadOnly password_Value As String
    Private ReadOnly gate As New Object()

    Private client As SftpClient
    Private is_Disposed As Boolean
    Private lastHostKeyFailure As ResourceFailure

    Private Const ConnectTimeoutSeconds As Integer = 5
    Private Const KeepAliveIntervalSeconds As Integer = 30
    Private Const StallWatchdogSeconds As Integer = 60
    Private Const MaxListingDepth As Integer = 32
    Private Const MaxListingEntries As Integer = 100000

    Friend Sub New(descriptor As ResourceDescriptor,
                   password As String,
                   hostKeyGate As HostKeyGate,
                   Optional capabilities As ProviderCapabilities = Nothing,
                   Optional includeSubfolders As Func(Of Boolean) = Nothing)
        If descriptor Is Nothing Then Throw New ArgumentNullException(NameOf(descriptor))
        descriptor_Value = descriptor
        password_Value = If(password, "")
        hostKeyGate_Value = hostKeyGate
        include_Subfolders = includeSubfolders

        Dim canWrite As Boolean = Not descriptor.ReadOnlyHint
        Dim defaultCaps As New ProviderCapabilities(
            canWrite:=canWrite,
            canDelete:=canWrite,
            canRename:=canWrite,
            canCreateFolder:=canWrite,
            hasRecycleBin:=False,
            needsMaterialisation:=True,
            supportsServerSideRename:=True,
            costHint:=ProviderCostHint.Slow,
            isMetered:=False,
            supportsAtomicOverwriteRename:=False,
            supportsStableSourceDelete:=False)
        capabilities_Value = If(capabilities, defaultCaps)
    End Sub

    Public ReadOnly Property Kind As ResourceKind Implements IFileSystemProvider.Kind
        Get
            Return ResourceKind.Sftp
        End Get
    End Property

    Public ReadOnly Property Capabilities As ProviderCapabilities Implements IFileSystemProvider.Capabilities
        Get
            Return capabilities_Value
        End Get
    End Property

    Friend ReadOnly Property Descriptor As ResourceDescriptor
        Get
            Return descriptor_Value
        End Get
    End Property

    Friend ReadOnly Property HostKeyGate As HostKeyGate
        Get
            Return hostKeyGate_Value
        End Get
    End Property

    ''' <summary>Ensures the SFTP client is connected, reconnecting with backoff if needed.</summary>
    Friend Sub EnsureConnected()
        CheckNotDisposed()
        SyncLock gate
            If client IsNot Nothing AndAlso client.IsConnected Then Return

            Dim port As Integer = If(descriptor_Value.Port > 0, descriptor_Value.Port, 22)
            Dim user As String = descriptor_Value.UserName
            Dim host As String = descriptor_Value.Host

            Dim backoffMs As Integer() = {0, 1000, 4000, 10000}
            Dim lastEx As Exception = Nothing

            For attempt As Integer = 0 To backoffMs.Length - 1
                If backoffMs(attempt) > 0 Then
                    Thread.Sleep(backoffMs(attempt))
                End If

                Try
                    DisposeClientLocked()
                    lastHostKeyFailure = Nothing

                    Dim authMethod As New PasswordAuthenticationMethod(user, password_Value)
                    Dim connInfo As New ConnectionInfo(host, port, user, authMethod) With {
                        .Timeout = TimeSpan.FromSeconds(ConnectTimeoutSeconds)
                    }

                    client = New SftpClient(connInfo)
                    client.KeepAliveInterval = TimeSpan.FromSeconds(KeepAliveIntervalSeconds)
                    AddHandler client.HostKeyReceived, AddressOf OnHostKeyReceived

                    client.Connect()
                    If client.IsConnected Then Return
                Catch ex As SshAuthenticationException
                    DisposeClientLocked()
                    Throw
                Catch ex As Exception
                    lastEx = ex
                    DisposeClientLocked()
                    If lastHostKeyFailure IsNot Nothing AndAlso
                       lastHostKeyFailure.Kind = ResourceFailureKind.KeyMismatch Then
                        Throw New SshConnectionException("Host key mismatch: " & lastHostKeyFailure.Detail, ex)
                    End If
                End Try
            Next

            If lastEx IsNot Nothing Then
                Throw New SshConnectionException("Failed to connect to SFTP server after retries.", lastEx)
            Else
                Throw New SshConnectionException("Failed to connect to SFTP server.")
            End If
        End SyncLock
    End Sub

    Private Sub OnHostKeyReceived(sender As Object, e As HostKeyEventArgs)
        If hostKeyGate_Value Is Nothing Then
            e.CanTrust = True
            Return
        End If

        Dim decision As HostKeyDecision = hostKeyGate_Value.Verify(e.HostKey, CancellationToken.None)
        If decision.IsAccepted Then
            e.CanTrust = True
        Else
            e.CanTrust = False
            lastHostKeyFailure = decision.ToFailure()
        End If
    End Sub

    Private Sub DisposeClientLocked()
        If client IsNot Nothing Then
            Try
                RemoveHandler client.HostKeyReceived, AddressOf OnHostKeyReceived
                If client.IsConnected Then client.Disconnect()
                client.Dispose()
            Catch
            Finally
                client = Nothing
            End Try
        End If
    End Sub

    Private Sub CheckNotDisposed()
        If is_Disposed Then
            Throw New ObjectDisposedException(NameOf(SftpProvider), "The SFTP provider has been disposed.")
        End If
    End Sub

    Public Function ListEntries(dirPath As String, ct As CancellationToken) As IReadOnlyList(Of ResourceEntry) Implements IFileSystemProvider.ListEntries
        CheckNotDisposed()
        EnsureConnected()

        Dim canonicalDir As String = CanonicalizePath(dirPath)
        Dim includeSubfolders As Boolean = include_Subfolders IsNot Nothing AndAlso include_Subfolders()
        Dim results As New List(Of ResourceEntry)()

        EnumerateDirectoryInternal(canonicalDir, results, currentDepth:=0, includeSubfolders:=includeSubfolders, ct:=ct)
        Return results
    End Function

    Private Sub EnumerateDirectoryInternal(dirPath As String,
                                           results As List(Of ResourceEntry),
                                           currentDepth As Integer,
                                           includeSubfolders As Boolean,
                                           ct As CancellationToken)
        If currentDepth > MaxListingDepth OrElse results.Count >= MaxListingEntries Then Return
        ct.ThrowIfCancellationRequested()

        Dim files As IEnumerable(Of ISftpFile)
        SyncLock gate
            EnsureConnected()
            files = client.ListDirectory(dirPath)
        End SyncLock

        Dim subdirs As New List(Of String)()
        For Each f As ISftpFile In files
            If f.Name = "." OrElse f.Name = ".." Then Continue For
            ct.ThrowIfCancellationRequested()
            If results.Count >= MaxListingEntries Then Exit For

            Dim fullPath As String = CanonicalizePath(f.FullName)
            Dim isDir As Boolean = f.IsDirectory

            Dim entry As New ResourceEntry With {
                .FullPath = fullPath,
                .Name = f.Name,
                .Length = If(isDir, 0L, f.Attributes.Size),
                .LastWriteUtc = f.Attributes.LastWriteTimeUtc,
                .IsDirectory = isDir,
                .LocalPath = "",
                .VersionTag = f.Attributes.Size.ToString(CultureInfo.InvariantCulture) & ":" & f.Attributes.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
            }
            results.Add(entry)

            If isDir AndAlso includeSubfolders Then
                subdirs.Add(fullPath)
            End If
        Next

        If includeSubfolders AndAlso currentDepth < MaxListingDepth Then
            For Each subDir In subdirs
                If results.Count >= MaxListingEntries Then Exit For
                Try
                    EnumerateDirectoryInternal(subDir, results, currentDepth + 1, includeSubfolders, ct)
                Catch ex As Exception When Not (TypeOf ex Is OperationCanceledException)
                    ' Skip inaccessible subfolder and continue
                End Try
            Next
        End If
    End Sub

    Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
        entry = Nothing
        Try
            Dim result As StatResult = Stat(path)
            If result.Outcome = StatOutcome.Found Then
                entry = result.Entry
                Return True
            End If
            Return False
        Catch
            Return False
        End Try
    End Function

    Public Function Stat(path As String) As StatResult Implements ITypedStatProvider.Stat
        If is_Disposed Then Return StatResult.Failed(New ResourceFailure(ResourceFailureKind.LocalIO, Nothing, "Disposed"))
        If String.IsNullOrEmpty(path) Then Return StatResult.Missing()

        Dim canonical As String = CanonicalizePath(path)
        Try
            SyncLock gate
                EnsureConnected()
                Dim attrs As SftpFileAttributes = client.GetAttributes(canonical)
                Dim name As String = PosixFileName(canonical)
                Dim isDir As Boolean = attrs.IsDirectory
                Dim entry As New ResourceEntry With {
                    .FullPath = canonical,
                    .Name = name,
                    .Length = If(isDir, 0L, attrs.Size),
                    .LastWriteUtc = attrs.LastWriteTimeUtc,
                    .IsDirectory = isDir,
                    .LocalPath = "",
                    .VersionTag = attrs.Size.ToString(CultureInfo.InvariantCulture) & ":" & attrs.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
                }
                Return StatResult.Found(entry)
            End SyncLock
        Catch ex As SftpPathNotFoundException
            Return StatResult.Missing()
        Catch ex As SftpPermissionDeniedException
            Return StatResult.Denied(New ResourceFailure(ResourceFailureKind.SftpStatus, Nothing, "Permission denied"))
        Catch ex As SshAuthenticationException
            Return StatResult.Denied(New ResourceFailure(ResourceFailureKind.Auth, Nothing, "Authentication failed"))
        Catch ex As Exception
            Return StatResult.Failed(ResourceFailure.FromException(ex))
        End Try
    End Function

    Public Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Download
        CheckNotDisposed()
        Dim canonical As String = CanonicalizePath(remotePath)
        Dim partPath As String = localPath & ".fms-part"
        Dim localDir As String = Path.GetDirectoryName(localPath)
        If Not String.IsNullOrEmpty(localDir) AndAlso Not Directory.Exists(localDir) Then
            Directory.CreateDirectory(localDir)
        End If

        Try
            If File.Exists(partPath) Then File.Delete(partPath)

            SyncLock gate
                EnsureConnected()
                Using fs As New FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None)
                    Dim lastBytes As Long = 0
                    Dim lastActivityTime As DateTime = DateTime.UtcNow

                    Dim callback As Action(Of ULong) =
                        Sub(downloaded As ULong)
                            ct.ThrowIfCancellationRequested()
                            Dim current As Long = CLng(downloaded)
                            If current > lastBytes Then
                                lastBytes = current
                                lastActivityTime = DateTime.UtcNow
                                progress?.Report(current)
                            ElseIf (DateTime.UtcNow - lastActivityTime).TotalSeconds > StallWatchdogSeconds Then
                                Throw New TimeoutException("SFTP download stalled: no progress for 60 seconds.")
                            End If
                        End Sub

                    client.DownloadFile(canonical, fs, callback)
                End Using
            End SyncLock

            If File.Exists(localPath) Then File.Delete(localPath)
            File.Move(partPath, localPath)
        Catch ex As Exception
            Try
                If File.Exists(partPath) Then File.Delete(partPath)
            Catch
            End Try
            Throw
        End Try
    End Sub

    Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
        CheckNotDisposed()
        Dim canonical As String = CanonicalizePath(remotePath)

        SyncLock gate
            EnsureConnected()
            Using fs As New FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read)
                Dim lastBytes As Long = 0
                Dim lastActivityTime As DateTime = DateTime.UtcNow

                Dim callback As Action(Of ULong) =
                    Sub(uploaded As ULong)
                        ct.ThrowIfCancellationRequested()
                        Dim current As Long = CLng(uploaded)
                        If current > lastBytes Then
                            lastBytes = current
                            lastActivityTime = DateTime.UtcNow
                            progress?.Report(current)
                        ElseIf (DateTime.UtcNow - lastActivityTime).TotalSeconds > StallWatchdogSeconds Then
                            Throw New TimeoutException("SFTP upload stalled: no progress for 60 seconds.")
                        End If
                    End Sub

                client.UploadFile(fs, canonical, canOverride:=True, callback)
            End Using
        End SyncLock
    End Sub

    Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
        CheckNotDisposed()
        Dim canonical As String = CanonicalizePath(remotePath)
        SyncLock gate
            EnsureConnected()
            client.DeleteFile(canonical)
        End SyncLock
    End Sub

    Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
        CheckNotDisposed()
        Dim canonicalSrc As String = CanonicalizePath(remotePath)
        Dim canonicalDst As String = CanonicalizePath(newRemotePath)
        SyncLock gate
            EnsureConnected()
            client.RenameFile(canonicalSrc, canonicalDst)
        End SyncLock
    End Sub

    Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
        CheckNotDisposed()
        Dim canonical As String = CanonicalizePath(remotePath)
        SyncLock gate
            EnsureConnected()
            client.CreateDirectory(canonical)
        End SyncLock
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If Not is_Disposed Then
            is_Disposed = True
            SyncLock gate
                DisposeClientLocked()
            End SyncLock
        End If
    End Sub

    Private Shared Function CanonicalizePath(path As String) As String
        Return FmsConfigReader.CanonicalVirtualPath(path)
    End Function

    Private Shared Function PosixFileName(path As String) As String
        If String.IsNullOrEmpty(path) Then Return ""
        Dim slash As Integer = path.LastIndexOf("/"c)
        Return If(slash >= 0, path.Substring(slash + 1), path)
    End Function

End Class

''' <summary>
''' Connects to an SFTP resource descriptor by unprotecting credentials, verifying the host key,
''' and creating an <see cref="SftpProvider"/>.
''' </summary>
Friend Module SftpResourceConnector

    Friend Function Connect(descriptor As ResourceDescriptor,
                            secrets As IResourceSecretStore,
                            hostKeyGate As HostKeyGate,
                            Optional includeSubfolders As Func(Of Boolean) = Nothing,
                            Optional unprotect As Func(Of String, SecretProtectionResult) = Nothing) As ResourceResult(Of SftpProvider)
        If descriptor Is Nothing Then Throw New ArgumentNullException(NameOf(descriptor))

        Dim password As String = ""
        If Not String.IsNullOrEmpty(descriptor.SecretHandle) AndAlso secrets IsNot Nothing Then
            Dim blob As String = secrets.GetBlob(descriptor.SecretHandle)
            If Not String.IsNullOrEmpty(blob) Then
                Dim unprotector As Func(Of String, SecretProtectionResult) = If(unprotect, Function(b As String) DpapiSecretChecks.UnprotectChecked(b))
                Dim decResult As SecretProtectionResult = unprotector(blob)
                If decResult.Succeeded Then
                    password = decResult.Value
                Else
                    Return ResourceResult(Of SftpProvider).Fail(New ResourceFailure(ResourceFailureKind.Auth, Nothing, "Failed to decrypt stored SFTP credentials."))
                End If
            End If
        End If

        Try
            Dim provider As New SftpProvider(descriptor, password, hostKeyGate, includeSubfolders:=includeSubfolders)
            provider.EnsureConnected()
            Return ResourceResult(Of SftpProvider).Ok(provider)
        Catch ex As Exception
            Return ResourceResult(Of SftpProvider).Fail(ResourceFailure.FromException(ex))
        End Try
    End Function

End Module
#End If
