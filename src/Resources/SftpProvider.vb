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
    Private pin_Rejected As Boolean
    Private lastHostKeyFailure As ResourceFailure
    Private connection_Cancellation As CancellationToken
    Private listing_Truncated As Integer
    Private ReadOnly listing_Max_Depth As Integer
    Private ReadOnly listing_Max_Entries As Integer
    Private ReadOnly resource_Pin As String

    Private Const ConnectTimeoutSeconds As Integer = 5
    Private Const KeepAliveIntervalSeconds As Integer = 30
    Private Const StallWatchdogSeconds As Integer = 60
    Private Const ListingTimeoutSeconds As Integer = 10
    Private Const MaxListingDepth As Integer = 32
    Private Const MaxListingEntries As Integer = 100000

    Friend Sub New(descriptor As ResourceDescriptor,
                   password As String,
                   hostKeyGate As HostKeyGate,
                   Optional capabilities As ProviderCapabilities = Nothing,
                   Optional includeSubfolders As Func(Of Boolean) = Nothing,
                   Optional maxListingDepth As Integer = MaxListingDepth,
                   Optional maxListingEntries As Integer = MaxListingEntries,
                   Optional resourcePin As String = Nothing)
        If descriptor Is Nothing Then Throw New ArgumentNullException(NameOf(descriptor))
        If maxListingDepth < 0 Then Throw New ArgumentOutOfRangeException(NameOf(maxListingDepth))
        If maxListingEntries < 1 Then Throw New ArgumentOutOfRangeException(NameOf(maxListingEntries))
        listing_Max_Depth = maxListingDepth
        listing_Max_Entries = maxListingEntries
        descriptor_Value = descriptor
        resource_Pin = If(resourcePin, ResourcePinMemory.GetPin(descriptor))
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
            supportsStableSourceDelete:=False,
            supportsCheckedSourceDelete:=canWrite)
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

    ''' <summary>Ensures the SFTP client is connected, reconnecting with backoff (1 s, 4 s, 10 s)
    ''' when the link itself failed. A refusal - wrong credential, a host key that is not the
    ''' pin, a trust prompt answered No - stops at once: repeating it would re-prompt the user
    ''' and, for a credential, count against the server's lockout. Throws a
    ''' <see cref="ResourceFailureException"/> whose class is the one in 007 §2.2.</summary>
    Friend ReadOnly Property WasTruncated As Boolean
        Get
            Return Volatile.Read(listing_Truncated) <> 0
        End Get
    End Property

    ''' <summary>A cached protected file is usable only while an authenticated SSH connection proves access to its root.</summary>
    Friend Sub EnsureResourceAuthorization(ct As CancellationToken)
        If Not descriptor_Value.PinRequired Then Return
        SyncLock gate
            Try
                ConnectLocked(retry:=False, ct:=ct)
                ct.ThrowIfCancellationRequested()
                client.GetAttributes(descriptor_Value.RootPath)
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                Dim failure As ResourceFailure = Classify(ex)
                If failure.Kind = ResourceFailureKind.Auth OrElse (failure.Kind = ResourceFailureKind.SftpStatus AndAlso failure.StatusCode = 3) Then
                    pin_Rejected = True
                    ResourcePinMemory.Forget(descriptor_Value)
                    failure = New ResourceFailure(ResourceFailureKind.Auth)
                End If
                DisposeClientLocked()
                Throw New ResourceFailureException(failure)
            End Try
        End SyncLock
    End Sub

    Friend Sub EnsureConnected(Optional ct As CancellationToken = Nothing)
        CheckNotDisposed()
        SyncLock gate
            ConnectLocked(retry:=True, ct:=ct)
        End SyncLock
    End Sub

    ''' <summary>One attempt, no backoff: what an endpoint race dials with, so a candidate that
    ''' runs out of its 5 s budget does not keep a thread reconnecting for another 15 s.</summary>
    Friend Sub ConnectOnce(Optional ct As CancellationToken = Nothing)
        CheckNotDisposed()
        SyncLock gate
            ConnectLocked(retry:=False, ct:=ct)
        End SyncLock
    End Sub

    Private Sub ConnectLocked(retry As Boolean, ct As CancellationToken)
        ct.ThrowIfCancellationRequested()
        CheckNotDisposed()
        If hostKeyGate_Value Is Nothing Then
            Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.KeyMismatch))
        End If
        If client IsNot Nothing AndAlso client.IsConnected Then Return
        If descriptor_Value.PinRequired AndAlso pin_Rejected Then
            ResourcePinMemory.Forget(descriptor_Value)
            Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Auth))
        End If

        Dim port As Integer = If(descriptor_Value.Port > 0, descriptor_Value.Port, 22)
        Dim user As String = descriptor_Value.UserName
        Dim host As String = descriptor_Value.Host

        Dim backoffMs As Integer() = If(retry, New Integer() {0, 1000, 4000, 10000}, New Integer() {0})
        Dim last As ResourceFailure = Nothing

        For attempt As Integer = 0 To backoffMs.Length - 1
            If backoffMs(attempt) > 0 AndAlso ct.WaitHandle.WaitOne(backoffMs(attempt)) Then ct.ThrowIfCancellationRequested()
            ct.ThrowIfCancellationRequested()
            CheckNotDisposed()

            Try
                DisposeClientLocked()
                lastHostKeyFailure = Nothing

                Dim pinChallengeAnswered As Boolean = False
                Dim authMethod As AuthenticationMethod
                If descriptor_Value.PinRequired Then
                    If String.IsNullOrEmpty(resource_Pin) Then Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Auth))
                    Dim interactive As New KeyboardInteractiveAuthenticationMethod(user)
                    AddHandler interactive.AuthenticationPrompt, Sub(sender, args)
                        If args.Prompts.Count <> 3 Then Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Auth))
                        Dim expected As String() = {"Resource", "Password", "PIN"}
                        Dim i As Integer = 0
                        For Each prompt As AuthenticationPrompt In args.Prompts
                            If prompt.Request <> expected(i) OrElse prompt.IsEchoed Then Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Auth))
                            i += 1
                        Next
                        i = 0
                        Dim answers As String() = {descriptor_Value.RootPath.Trim("/"c).Split("/"c)(0), password_Value, resource_Pin}
                        For Each prompt As AuthenticationPrompt In args.Prompts
                            prompt.Response = answers(i)
                            i += 1
                        Next
                        pinChallengeAnswered = True
                    End Sub
                    authMethod = interactive
                Else
                    authMethod = New PasswordAuthenticationMethod(user, password_Value)
                End If
                Dim connInfo As New ConnectionInfo(host, port, user, authMethod) With {
                    .Timeout = TimeSpan.FromSeconds(ConnectTimeoutSeconds)
                }

                client = New SftpClient(connInfo)
                client.KeepAliveInterval = TimeSpan.FromSeconds(KeepAliveIntervalSeconds)
                client.OperationTimeout = TimeSpan.FromSeconds(ListingTimeoutSeconds)
                AddHandler client.HostKeyReceived, AddressOf OnHostKeyReceived

                connection_Cancellation = ct
                client.ConnectAsync(ct).GetAwaiter().GetResult()
                If descriptor_Value.PinRequired AndAlso Not pinChallengeAnswered Then Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Auth))
                If client.IsConnected Then
                    Return
                End If
                last = New ResourceFailure(ResourceFailureKind.Transport)
            Catch ex As Exception
                ' The gate's own verdict is the more exact reason: SSH.NET reports a refused
                ' host key as a generic connection failure.
                Dim keyFailure As ResourceFailure = lastHostKeyFailure
                DisposeClientLocked()
                If keyFailure IsNot Nothing Then Throw New ResourceFailureException(keyFailure)
                last = Classify(ex)
                If descriptor_Value.PinRequired AndAlso last.Kind = ResourceFailureKind.Auth Then
                    pin_Rejected = True
                    ResourcePinMemory.Forget(descriptor_Value)
                End If
                If Not last.IsRetryable Then Throw New ResourceFailureException(last)
            End Try
        Next

        Throw New ResourceFailureException(If(last, New ResourceFailure(ResourceFailureKind.Transport)))
    End Sub

    ''' <summary>
    ''' Runs one SSH.NET transfer with the caller's cancellation and the 60 s stall watchdog.
    '''
    ''' The transfer is the ASYNC form on purpose. The callback overloads (<c>DownloadFile(.., Action(Of
    ''' ULong))</c>) invoke the callback on a THREAD-POOL thread, so the old way of stopping one - throwing from
    ''' the callback - threw on a thread nobody awaits and took the whole process down (found by the live run
    ''' against a real server, the first time a download was cancelled). Cancellation is now a token the
    ''' library honours, and the progress sink is only ever called, never allowed to throw into the library.
    ''' </summary>
    Private Sub RunTransfer(ct As CancellationToken, sink As IProgress(Of Long), start As Func(Of TransferWatch, CancellationToken, Task))
        Dim watch As New TransferWatch(sink)
        Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
            Dim stalled As Boolean
            Using timer As New System.Threading.Timer(
                Sub(state As Object)
                    If watch.IdleSeconds > StallWatchdogSeconds Then
                        stalled = True
                        Try
                            linked.Cancel()
                        Catch ex As ObjectDisposedException
                        End Try
                    End If
                End Sub, Nothing, 2000, 2000)
                Try
                    start(watch, linked.Token).GetAwaiter().GetResult()
                Catch ex As OperationCanceledException When stalled AndAlso Not ct.IsCancellationRequested
                    Throw New TimeoutException("SFTP transfer stalled: no progress for 60 seconds.")
                End Try
            End Using
        End Using
    End Sub

    ''' <summary>The library's progress reports of both directions, reduced to "how many bytes so far" and "when
    ''' did that last grow". Runs on the library's pool threads: it records and forwards, nothing more.</summary>
    Private NotInheritable Class TransferWatch
        Implements IProgress(Of DownloadFileProgressReport)
        Implements IProgress(Of UploadFileProgressReport)

        Private ReadOnly sink As IProgress(Of Long)
        Private lastBytes As Long
        Private lastTick As Long = Environment.TickCount64

        Friend Sub New(sink As IProgress(Of Long))
            Me.sink = sink
        End Sub

        Friend ReadOnly Property IdleSeconds As Double
            Get
                Return (Environment.TickCount64 - Interlocked.Read(lastTick)) / 1000.0
            End Get
        End Property

        Public Sub ReportDownload(value As DownloadFileProgressReport) Implements IProgress(Of DownloadFileProgressReport).Report
            Note(CLng(value.TotalBytesDownloaded))
        End Sub

        Public Sub ReportUpload(value As UploadFileProgressReport) Implements IProgress(Of UploadFileProgressReport).Report
            Note(CLng(value.TotalBytesUploaded))
        End Sub

        Private Sub Note(bytes As Long)
            If bytes <= Interlocked.Read(lastBytes) Then Return
            Interlocked.Exchange(lastBytes, bytes)
            Interlocked.Exchange(lastTick, Environment.TickCount64)
            Try
                sink?.Report(bytes)
            Catch ex As Exception
                ' A progress sink that throws must not reach the library's thread.
            End Try
        End Sub
    End Class
    ''' <summary>The fixed mapping of 007 §2.2, kept inside this file so nothing else learns what
    ''' SSH.NET throws. Status codes are the SFTP ones: 2 = no such file, 3 = permission denied.</summary>
    Private Shared Function Classify(ex As Exception) As ResourceFailure
        If ex Is Nothing Then Return New ResourceFailure(ResourceFailureKind.None)
        Dim typed As ResourceFailureException = TryCast(ex, ResourceFailureException)
        If typed IsNot Nothing Then Return typed.Failure
        Dim aggregate As AggregateException = TryCast(ex, AggregateException)
        If aggregate IsNot Nothing AndAlso aggregate.InnerExceptions.Count = 1 Then Return Classify(aggregate.InnerExceptions(0))
        If TypeOf ex Is OperationCanceledException Then Return New ResourceFailure(ResourceFailureKind.Cancelled)
        If TypeOf ex Is SshAuthenticationException Then Return New ResourceFailure(ResourceFailureKind.Auth)
        If TypeOf ex Is SftpPathNotFoundException Then Return New ResourceFailure(ResourceFailureKind.SftpStatus, 2, "no such file")
        If TypeOf ex Is SftpPermissionDeniedException Then Return New ResourceFailure(ResourceFailureKind.SftpStatus, 3, "permission denied")
        If TypeOf ex Is SshOperationTimeoutException OrElse TypeOf ex Is TimeoutException Then Return New ResourceFailure(ResourceFailureKind.Timeout)
        If TypeOf ex Is SocketException Then Return New ResourceFailure(ResourceFailureKind.Transport, DirectCast(ex, SocketException).ErrorCode)
        If TypeOf ex Is SshConnectionException OrElse TypeOf ex Is ProxyException Then Return New ResourceFailure(ResourceFailureKind.Transport)
        If TypeOf ex Is SftpException Then Return New ResourceFailure(ResourceFailureKind.SftpStatus, 4, "server refused the operation")
        If TypeOf ex Is SshException Then Return New ResourceFailure(ResourceFailureKind.Transport)
        Return ResourceFailure.FromException(ex)
    End Function

    ''' <summary>Runs a provider verb and turns anything it throws into a classified failure.
    ''' A caller never has to know SSH.NET to tell "the link dropped" from "no such file".</summary>
    Private Function Guarded(Of T)(work As Func(Of T)) As T
        Try
            Return work()
        Catch ex As ResourceFailureException
            Throw
        Catch ex As ObjectDisposedException
            Throw
        Catch ex As Exception
            Throw New ResourceFailureException(Classify(ex))
        End Try
    End Function

    Private Sub Guarded(work As Action)
        Guarded(Of Integer)(Function()
                                work()
                                Return 0
                            End Function)
    End Sub

    Private Sub OnHostKeyReceived(sender As Object, e As HostKeyEventArgs)
        If hostKeyGate_Value Is Nothing Then
            e.CanTrust = False
            lastHostKeyFailure = New ResourceFailure(ResourceFailureKind.KeyMismatch)
            Return
        End If

        Dim decision As HostKeyDecision = hostKeyGate_Value.Verify(e.HostKey, connection_Cancellation)
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
        Return Guarded(Of IReadOnlyList(Of ResourceEntry))(
            Function()
                EnsureConnected(ct)

                Dim canonicalDir As String = CanonicalizePath(dirPath)
                Dim includeSubfolders As Boolean = include_Subfolders IsNot Nothing AndAlso include_Subfolders()
                Dim results As New List(Of ResourceEntry)()

                Dim truncated As Boolean
                EnumerateDirectoryInternal(canonicalDir, results, currentDepth:=0, includeSubfolders:=includeSubfolders, ct:=ct, truncated:=truncated)
                Volatile.Write(listing_Truncated, If(truncated, 1, 0))
                Return results
            End Function)
    End Function

    Private Sub EnumerateDirectoryInternal(dirPath As String,
                                           results As List(Of ResourceEntry),
                                           currentDepth As Integer,
                                           includeSubfolders As Boolean,
                                           ct As CancellationToken, ByRef truncated As Boolean)
        ct.ThrowIfCancellationRequested()

        Dim subdirs As New List(Of String)()
        SyncLock gate
            EnsureConnected(ct)
            client.OperationTimeout = TimeSpan.FromSeconds(ListingTimeoutSeconds)
            Using deadline As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                deadline.CancelAfter(TimeSpan.FromSeconds(ListingTimeoutSeconds))
                Dim files = client.ListDirectoryAsync(dirPath, deadline.Token).GetAsyncEnumerator(deadline.Token)
                Try
                    While files.MoveNextAsync().AsTask().GetAwaiter().GetResult()
                        Dim f As ISftpFile = files.Current
                        If f.Name = "." OrElse f.Name = ".." Then Continue While
                        ct.ThrowIfCancellationRequested()
                        If results.Count >= listing_Max_Entries Then
                            truncated = True
                            Exit While
                        End If

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

                        If isDir AndAlso includeSubfolders AndAlso Not f.IsSymbolicLink Then
                            If currentDepth >= listing_Max_Depth Then
                                truncated = True
                            Else
                                subdirs.Add(fullPath)
                            End If
                        End If
                    End While
                Catch ex As OperationCanceledException When Not ct.IsCancellationRequested
                    Throw New TimeoutException("SFTP listing timed out.")
                Finally
                    files.DisposeAsync().AsTask().GetAwaiter().GetResult()
                End Try
            End Using
        End SyncLock

        If includeSubfolders Then
            For Each subDir In subdirs
                If results.Count >= listing_Max_Entries Then
                    truncated = True
                    Exit For
                End If
                Try
                    EnumerateDirectoryInternal(subDir, results, currentDepth + 1, includeSubfolders, ct, truncated)
                Catch ex As Exception When IsSkippableSubfolderFailure(ex)
                    ' Only a missing or denied child may be skipped. A broken link must fail the
                    ' entire refresh so ResourceSession retains the previous complete listing.
                    truncated = True
                End Try
            Next
        End If
    End Sub

    Friend Shared Function IsSkippableSubfolderFailure(ex As Exception) As Boolean
        Dim failure As ResourceFailure = Classify(ex)
        Return failure.Kind = ResourceFailureKind.SftpStatus AndAlso
               failure.StatusCode.HasValue AndAlso
               (failure.StatusCode.Value = 2 OrElse failure.StatusCode.Value = 3)
    End Function

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
        Catch ex As Exception
            Dim failure As ResourceFailure = Classify(ex)
            If failure.Kind = ResourceFailureKind.SftpStatus AndAlso failure.StatusCode = 2 Then Return StatResult.Missing()
            If failure.Kind = ResourceFailureKind.Auth OrElse
               (failure.Kind = ResourceFailureKind.SftpStatus AndAlso failure.StatusCode = 3) Then Return StatResult.Denied(failure)
            Return StatResult.Failed(failure)
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
                EnsureConnected(ct)
                Using fs As New FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None)
                    ' No timeout on a transfer as such (a big file is slow, not stuck), but 60 s
                    ' without a single answer from the server is the stall watchdog of 007 §2.2.
                    client.OperationTimeout = TimeSpan.FromSeconds(StallWatchdogSeconds)
                    RunTransfer(ct, progress, Function(watch, token) client.DownloadFileAsync(canonical, fs, watch, token))
                End Using
            End SyncLock

            If File.Exists(localPath) Then File.Delete(localPath)
            File.Move(partPath, localPath)
        Catch ex As Exception
            Try
                If File.Exists(partPath) Then File.Delete(partPath)
            Catch
            End Try
            If (TypeOf ex Is IOException AndAlso Not TypeOf ex.InnerException Is SocketException) OrElse
               TypeOf ex Is UnauthorizedAccessException Then
                ' The local side of the transfer (the part file, the rename): not the server's fault.
                Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.LocalIO, Nothing, ex.GetType().Name))
            End If
            If TypeOf ex Is ResourceFailureException Then Throw
            Throw New ResourceFailureException(Classify(ex))
        End Try
    End Sub

    Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
        CheckNotDisposed()
        RequireCapability(Capabilities.CanWrite)
        Dim canonical As String = CanonicalizePath(remotePath)

        Guarded(Sub()
                    SyncLock gate
                        EnsureConnected(ct)
                        client.OperationTimeout = TimeSpan.FromSeconds(StallWatchdogSeconds)
                        Using fs As New FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read)
                            RunTransfer(ct, progress, Function(watch, token) client.UploadFileAsync(fs, canonical, True, watch, token))
                        End Using
                    End SyncLock
                End Sub)
    End Sub

    Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
        CheckNotDisposed()
        RequireCapability(Capabilities.CanDelete)
        Dim canonical As String = CanonicalizePath(remotePath)
        Guarded(Sub()
                    SyncLock gate
                        EnsureConnected()
                        client.OperationTimeout = TimeSpan.FromSeconds(ListingTimeoutSeconds)
                        client.DeleteFile(canonical)
                    End SyncLock
                End Sub)
    End Sub

    Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
        CheckNotDisposed()
        RequireCapability(Capabilities.CanRename)
        Dim canonicalSrc As String = CanonicalizePath(remotePath)
        Dim canonicalDst As String = CanonicalizePath(newRemotePath)
        Guarded(Sub()
                    SyncLock gate
                        EnsureConnected()
                        client.OperationTimeout = TimeSpan.FromSeconds(ListingTimeoutSeconds)
                        client.RenameFile(canonicalSrc, canonicalDst)
                    End SyncLock
                End Sub)
    End Sub

    Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
        CheckNotDisposed()
        RequireCapability(Capabilities.CanCreateFolder)
        Dim canonical As String = CanonicalizePath(remotePath)
        Guarded(Sub()
                    SyncLock gate
                        EnsureConnected()
                        client.OperationTimeout = TimeSpan.FromSeconds(ListingTimeoutSeconds)
                        client.CreateDirectory(canonical)
                    End SyncLock
                End Sub)
    End Sub

    Private Shared Sub RequireCapability(allowed As Boolean)
        If Not allowed Then Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Unsupported))
    End Sub

    ''' <summary>Something that must live exactly as long as this session and be closed with it: the loopback forwarder of an exchange tunnel
    ''' (ticket 171), so a session that came through a tunnel never leaves its listener behind.</summary>
    Friend Property OwnedResource As IDisposable

    Public Sub Dispose() Implements IDisposable.Dispose
        If Not is_Disposed Then
            is_Disposed = True
            SyncLock gate
                DisposeClientLocked()
            End SyncLock
            Dim owned As IDisposable = OwnedResource
            OwnedResource = Nothing
            Try : owned?.Dispose() : Catch : End Try
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
