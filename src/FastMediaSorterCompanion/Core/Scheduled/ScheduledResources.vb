Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading

' Registered resources as a side of a scheduled operation (ticket 222, 4.3, R2).
'
' Nothing here is new machinery: the provider layer, the credential store, the host-key gate and the
' checked transfer are the viewer's own files, LINKED into the Share Manager (FastMediaSorterCompanion.vbproj).
' What this file adds is the UNATTENDED stance: a run asks nothing, so
'   * a password that is not stored is not asked for - the run stops as a wall;
'   * a host key that is not pinned is not trusted - the trust question is the viewer's, never this program's;
'   * a changed pin is never accepted from here;
'   * a transfer is the viewer's checked one (TransferExecutor), never a second copy-then-delete loop.

''' <summary>What a validation needs to know about a registered resource, without the registry's own types.</summary>
Friend NotInheritable Class ScheduledResourceFacts
    Friend Property Exists As Boolean
    Friend Property DisplayName As String = ""
    ''' <summary>The root was added read-only: copy out of it, never move, delete or write.</summary>
    Friend Property IsReadOnly As Boolean
    ''' <summary>The root is PIN-locked or its protection is not supported: refused (4.3).</summary>
    Friend Property IsPinLocked As Boolean
    ''' <summary>An exchange or link resource (a tunnelled one): not usable unattended in this version.</summary>
    Friend Property IsUnsupported As Boolean
    ''' <summary>The root folder in the provider's own vocabulary ("/MOV"), for the "inside the root" check.</summary>
    Friend Property RootPath As String = ""
    Friend Property IsSmb As Boolean
End Class

''' <summary>The registry of the viewer's resources, read here and never written: it belongs to another program.</summary>
Friend Module ScheduledResourceRegistry

    Friend Function Lookup(resourceId As String) As ScheduledResourceFacts
        Dim descriptor As ResourceDescriptor = Find(resourceId)
        If descriptor Is Nothing Then Return New ScheduledResourceFacts With {.Exists = False}
        Return FactsOf(descriptor)
    End Function

    Friend Function FactsOf(descriptor As ResourceDescriptor) As ScheduledResourceFacts
        If descriptor Is Nothing Then Return New ScheduledResourceFacts With {.Exists = False}
        Return New ScheduledResourceFacts With {
            .Exists = True, .DisplayName = descriptor.DisplayName, .IsReadOnly = descriptor.ReadOnlyHint,
            .IsPinLocked = (descriptor.PinRequired AndAlso (String.IsNullOrEmpty(descriptor.PinSecretHandle) OrElse descriptor.PinChanged)) OrElse descriptor.ProtectionUnsupported,
            .IsUnsupported = descriptor.Kind <> ResourceKind.Sftp AndAlso descriptor.Kind <> ResourceKind.Smb,
            .RootPath = descriptor.RootPath, .IsSmb = descriptor.Kind = ResourceKind.Smb}
    End Function

    Friend Function Find(resourceId As String) As ResourceDescriptor
        If String.IsNullOrWhiteSpace(resourceId) Then Return Nothing
        Try
            Return ResourceRegistry.Load().FirstOrDefault(
                Function(r As ResourceDescriptor) String.Equals(r.Id, resourceId, StringComparison.OrdinalIgnoreCase))
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: reading the resource registry", ex)
            Return Nothing
        End Try
    End Function

    ''' <summary>What the editor's picker offers: the usable resources, by name.</summary>
    Friend Function Offered() As List(Of ResourceDescriptor)
        Try
            Return ResourceRegistry.Load().
                Where(Function(r As ResourceDescriptor) (r.Kind = ResourceKind.Sftp OrElse r.Kind = ResourceKind.Smb) AndAlso
                                                        Not r.ProtectionUnsupported).
                OrderBy(Function(r As ResourceDescriptor) r.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList()
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: listing the resources", ex)
            Return New List(Of ResourceDescriptor)()
        End Try
    End Function

End Module

''' <summary>Google Drive exchange channel discovery and unattended opening (ticket 231, 3.2).</summary>
Friend Module ScheduledDriveRegistry

    Friend Property IsSignedInOverride As Func(Of Boolean)
    Friend Property OpenSideOverride As Func(Of ScheduledLocation, ScheduledSideOpen)

    Friend Function IsSignedIn() As Boolean
        If IsSignedInOverride IsNot Nothing Then Return IsSignedInOverride.Invoke()
        Try
            Dim raw As String = ResourceRegistry.HiveRead("Exchange_GoogleSignedIn")
            Return raw = "1"
        Catch
            Return False
        End Try
    End Function

    Friend Function OpenSide(location As ScheduledLocation) As ScheduledSideOpen
        If OpenSideOverride IsNot Nothing Then Return OpenSideOverride.Invoke(location)
        If Not IsSignedIn() Then
            Return ScheduledSideOpen.Failed(New SideProblem(SideProblemKind.SignedOut, location.Describe()))
        End If
        Return ScheduledSideOpen.Failed(New SideProblem(SideProblemKind.Offline, location.Describe()))
    End Function

End Module

''' <summary>A problem one side reports from deep inside a scan, so the executor can turn it into the wall.</summary>
Friend NotInheritable Class ScheduledSideException
    Inherits IOException

    Friend ReadOnly Problem As SideProblem

    Friend Sub New(problem As SideProblem)
        MyBase.New("side problem: " & problem.Kind.ToString())
        Me.Problem = problem
    End Sub
End Class

''' <summary>A transfer the viewer's executor did not complete, as a code (never the system's text).</summary>
Friend NotInheritable Class ScheduledTransferException
    Inherits IOException

    Friend ReadOnly FailureCode As String

    Friend Sub New(failureCode As String)
        MyBase.New("transfer: " & failureCode)
        Me.FailureCode = failureCode
    End Sub
End Class

''' <summary>The recursion switch a provider reads on every listing (<c>includeSubfolders</c>).</summary>
Friend NotInheritable Class RecursionFlag
    Friend Value As Boolean = True
End Class

''' <summary>A connected resource's folder as a side. Disposing it closes the session.</summary>
Friend NotInheritable Class ProviderSide
    Implements IScheduledSide

    Friend ReadOnly Provider As IFileSystemProvider
    Friend ReadOnly ResourceId As String
    Friend ReadOnly IsDrive As Boolean
    Private ReadOnly rootFolder As String
    Private ReadOnly display As String
    Private ReadOnly flag As RecursionFlag
    Private ReadOnly owner As IDisposable

    Friend Sub New(provider As IFileSystemProvider, resourceId As String, rootFolder As String, display As String,
                   flag As RecursionFlag, Optional owner As IDisposable = Nothing, Optional isDrive As Boolean = False)
        Me.Provider = provider
        Me.ResourceId = If(resourceId, "")
        Me.rootFolder = If(String.IsNullOrEmpty(rootFolder), "/", rootFolder)
        Me.display = If(display, "")
        Me.flag = If(flag, New RecursionFlag())
        Me.owner = owner
        Me.IsDrive = isDrive
    End Sub

    Public ReadOnly Property IsLocalPath As Boolean Implements IScheduledSide.IsLocalPath
        Get
            Return False
        End Get
    End Property

    Public ReadOnly Property Root As String Implements IScheduledSide.Root
        Get
            Return rootFolder
        End Get
    End Property

    Public Function Describe() As String Implements IScheduledSide.Describe
        Return If(display.Length > 0, display & ": " & rootFolder, rootFolder)
    End Function

    Public Function Combine(relative As String) As String Implements IScheduledSide.Combine
        Return ResourceTransfer.Combine(rootFolder, relative.Replace("\"c, "/"c))
    End Function

    Public Function Exists(relative As String) As Boolean Implements IScheduledSide.Exists
        Return ResourceProviderCalls.Stat(Provider, Combine(relative)).Outcome = StatOutcome.Found
    End Function

    Public Function CheckReadable() As SideProblem Implements IScheduledSide.CheckReadable
        Dim stat As StatResult = ResourceProviderCalls.Stat(Provider, rootFolder)
        Select Case stat.Outcome
            Case StatOutcome.Found : Return SideProblem.Ok
            Case StatOutcome.Missing : Return New SideProblem(SideProblemKind.Unreachable, Describe())
            Case StatOutcome.Denied : Return New SideProblem(SideProblemKind.Denied, Describe())
            Case Else : Return New SideProblem(SideProblemKind.Unreachable, Describe())
        End Select
    End Function

    Public Function CheckWritable() As SideProblem Implements IScheduledSide.CheckWritable
        If Not Provider.Capabilities.CanWrite Then Return New SideProblem(SideProblemKind.Denied, Describe())
        Try
            Dim stat As StatResult = ResourceProviderCalls.Stat(Provider, rootFolder)
            If stat.Outcome = StatOutcome.Missing Then
                If Not Provider.Capabilities.CanCreateFolder Then Return New SideProblem(SideProblemKind.Unreachable, Describe())
                Provider.CreateDirectory(rootFolder)
            ElseIf stat.Outcome <> StatOutcome.Found Then
                Return New SideProblem(SideProblemKind.Unreachable, Describe())
            End If
            ' A real write, then its removal: only a write answers "denied".
            Dim probeLocal As String = Path.Combine(Path.GetTempPath(), "fms-write-test-" & Guid.NewGuid().ToString("N") & ".tmp")
            File.WriteAllBytes(probeLocal, New Byte() {0})
            Try
                Dim probeRemote As String = ResourceTransfer.Combine(rootFolder, ".fms-write-test-" & Guid.NewGuid().ToString("N") & ".tmp")
                Provider.Upload(probeLocal, probeRemote, Nothing, CancellationToken.None)
                Try : Provider.DeleteFile(probeRemote) : Catch : End Try
            Finally
                Try : File.Delete(probeLocal) : Catch : End Try
            End Try
            Return SideProblem.Ok
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: writing to " & Describe(), ex)
            Return ProblemOf(ResourceFailure.FromException(ex), Describe())
        End Try
    End Function

    Friend Shared Function ProblemOf(failure As ResourceFailure, place As String, Optional forConnect As Boolean = False) As SideProblem
        If failure Is Nothing Then
            Return New SideProblem(If(forConnect, SideProblemKind.Offline, SideProblemKind.Unreachable), place)
        End If
        Select Case failure.Detail
            Case "device-revoked"
                Return New SideProblem(SideProblemKind.DeviceRevoked, place)
            Case ExchangeTunnelRules.CertificateDetail
                Return New SideProblem(SideProblemKind.ExchangeCertificateChanged, place)
            Case "signed-out"
                Return New SideProblem(SideProblemKind.SignedOut, place)
            Case "pin-locked"
                Return New SideProblem(SideProblemKind.PinLocked, place)
        End Select
        Select Case failure.Kind
            Case ResourceFailureKind.Auth : Return New SideProblem(SideProblemKind.NoCredential, place)
            Case ResourceFailureKind.KeyMismatch : Return New SideProblem(SideProblemKind.KeyNotPinned, place)
            Case ResourceFailureKind.SftpStatus
                Return New SideProblem(If(failure.StatusCode = 3, SideProblemKind.Denied, SideProblemKind.Unreachable), place)
            Case ResourceFailureKind.Transport, ResourceFailureKind.Timeout
                Return New SideProblem(If(forConnect, SideProblemKind.Offline, SideProblemKind.Unreachable), place)
            Case Else : Return New SideProblem(SideProblemKind.Unreachable, place)
        End Select
    End Function

    Public Function List(recursive As Boolean, ct As CancellationToken) As List(Of SideEntry) Implements IScheduledSide.List
        flag.Value = recursive
        Dim listed As ResourceResult(Of IReadOnlyList(Of ResourceEntry)) = ResourceProviderCalls.ListChecked(Provider, rootFolder, ct)
        If Not listed.Succeeded Then
            If listed.Failure.Kind = ResourceFailureKind.Cancelled Then Throw New OperationCanceledException()
            Throw New ScheduledSideException(ProblemOf(listed.Failure, Describe()))
        End If
        Dim prefix As String = If(rootFolder.EndsWith("/", StringComparison.Ordinal), rootFolder, rootFolder & "/")
        Dim result As New List(Of SideEntry)(listed.Value.Count)
        For Each entry As ResourceEntry In listed.Value
            If entry.IsDirectory Then Continue For
            Dim full As String = entry.FullPath.Replace("\"c, "/"c)
            Dim relative As String = If(full.StartsWith(prefix, StringComparison.Ordinal), full.Substring(prefix.Length), entry.Name)
            ' Dot-prefixed names and folders are the hidden of a server (the scan layer of 4.1).
            Dim hidden As Boolean = relative.Split("/"c).Any(Function(part As String) part.StartsWith(".", StringComparison.Ordinal))
            result.Add(New SideEntry With {.RelativePath = relative, .Name = entry.Name, .Length = entry.Length,
                .LastWriteUtc = entry.LastWriteUtc, .IsHiddenOrSystem = hidden})
        Next
        Return result
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            owner?.Dispose()
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: closing " & Describe(), ex)
        End Try
    End Sub

End Class

''' <summary>
''' Transfers where at least one end is a resource: the viewer's checked executor
''' (<c>TransferExecutor</c>, ticket 221) - stage, read back by content, publish, and only then remove a
''' source, re-stat first. This class only maps what it answers onto the engine's own outcomes.
''' </summary>
Friend NotInheritable Class ProviderTransfer
    Implements IScheduledTransfer

    ''' <summary>The provider that reads and writes plain Windows paths.</summary>
    Private Shared ReadOnly LocalEnd As New LocalProvider(Function() False)

    Private Shared Function EndpointOf(side As IScheduledSide, path As String) As TransferEndpoint
        Dim provider As ProviderSide = TryCast(side, ProviderSide)
        If provider IsNot Nothing Then Return New TransferEndpoint(provider.Provider, path, provider.ResourceId)
        Return New TransferEndpoint(LocalEnd, path, "")
    End Function

    Private Shared Function ScratchFolder() As String
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-scheduled-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(folder)
        Return folder
    End Function

    Private Shared Function Run(kind As TransferKind, source As IScheduledSide, entry As SideEntry, target As IScheduledSide,
                                targetRelative As String, overwrite As Boolean, ct As CancellationToken) As TransferOutcome
        Dim targetPath As String = target.Combine(targetRelative)
        EnsureParentFolders(target, targetPath)
        Dim plan As New TransferPlan(kind, EndpointOf(source, source.Combine(entry.RelativePath)),
                                     EndpointOf(target, targetPath),
                                     If(overwrite, ConflictPolicy.Replace, ConflictPolicy.Fail), ScratchFolder())
        Return TransferExecutor.Execute(plan, Nothing, ct)
    End Function

    ''' <summary>The structure of the source is kept (4.2), so a subfolder may not exist yet on the target. The
    ''' viewer's executor writes into a folder; it does not make one - so the folders are made here, one level at
    ''' a time from the side's root down, and only below it.</summary>
    Private Shared Sub EnsureParentFolders(side As IScheduledSide, filePath As String)
        Dim provider As ProviderSide = TryCast(side, ProviderSide)
        If provider Is Nothing Then
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath))
            Return
        End If
        Dim root As String = side.Root.TrimEnd("/"c, "\"c)
        Dim parent As String = ResourceTransfer.ParentOf(filePath)
        If parent.Length <= root.Length OrElse Not parent.StartsWith(root, StringComparison.OrdinalIgnoreCase) Then Return
        Dim separator As Char = If(side.Root.Contains("\"c) AndAlso Not side.Root.Contains("/"c), "\"c, "/"c)
        Dim current As String = root
        For Each part As String In parent.Substring(root.Length).Split({"/"c, "\"c}, StringSplitOptions.RemoveEmptyEntries)
            current = current & separator & part
            If ResourceProviderCalls.Stat(provider.Provider, current).Outcome = StatOutcome.Missing Then
                provider.Provider.CreateDirectory(current)
            End If
        Next
    End Sub

    ''' <summary>The code of a transfer that did not complete.</summary>
    Private Shared Function CodeOf(outcome As TransferOutcome) As String
        If outcome.Result = TransferResult.Cancelled Then Return "cancelled"
        Select Case outcome.Reason
            Case TransferReason.SourceMissing : Return "missing"
            Case TransferReason.SourceUnreadable, TransferReason.NotWritable, TransferReason.NotDeletable
                Return "denied"
            Case TransferReason.DestinationExists : Return "exists"
            Case TransferReason.SourceChanged : Return "changed"
            Case TransferReason.VerificationFailed : Return "mismatch"
            Case TransferReason.SamePath : Return "invalid"
            Case TransferReason.Uncertain : Return "transport"
            Case Else : Return "failed"
        End Select
    End Function

    Public Function CopyFile(source As IScheduledSide, entry As SideEntry, target As IScheduledSide,
                             targetRelative As String, overwrite As Boolean, ct As CancellationToken) As Long Implements IScheduledTransfer.CopyFile
        Dim outcome As TransferOutcome = Run(TransferKind.Copy, source, entry, target, targetRelative, overwrite, ct)
        If outcome.Result = TransferResult.Cancelled Then Throw New OperationCanceledException()
        If outcome.Result = TransferResult.Completed Then Return entry.Length
        If outcome.Reason = TransferReason.SourceChanged Then Throw New SourceChangedException(source.Combine(entry.RelativePath))
        Throw New ScheduledTransferException(CodeOf(outcome))
    End Function

    Public Function MoveFile(source As IScheduledSide, entry As SideEntry, target As IScheduledSide,
                             targetRelative As String, overwrite As Boolean, ct As CancellationToken) As MoveOutcome Implements IScheduledTransfer.MoveFile
        Dim outcome As TransferOutcome = Run(TransferKind.Move, source, entry, target, targetRelative, overwrite, ct)
        Select Case outcome.Result
            Case TransferResult.Completed
                Return MoveOutcome.Moved
            Case TransferResult.CopyOnly
                ' Published and verified; the source stayed (changed, or it cannot be removed): both copies kept.
                If outcome.Reason = TransferReason.SourceChanged Then Return MoveOutcome.SourceChanged
                Return MoveOutcome.CopiedSourceKept
            Case TransferResult.Cancelled
                Throw New OperationCanceledException()
            Case Else
                If outcome.Reason = TransferReason.SourceChanged Then Return MoveOutcome.SourceChanged
                Throw New ScheduledTransferException(CodeOf(outcome))
        End Select
    End Function

    ''' <summary>Deleting on a resource is permanent: there is no bin on a server (4.3). The file must still be
    ''' the one that was listed, and the removal is confirmed by a stat - never assumed.</summary>
    Public Function DeleteFile(side As IScheduledSide, entry As SideEntry, ct As CancellationToken) As String Implements IScheduledTransfer.DeleteFile
        Dim provider As ProviderSide = TryCast(side, ProviderSide)
        If provider Is Nothing Then Throw New NotSupportedException()
        Dim path As String = side.Combine(entry.RelativePath)
        If Not provider.Provider.Capabilities.CanDelete Then Throw New ScheduledTransferException("denied")
        Dim before As StatResult = ResourceProviderCalls.Stat(provider.Provider, path)
        If before.Outcome = StatOutcome.Missing Then Throw New ScheduledTransferException("missing")
        If before.Outcome <> StatOutcome.Found Then Throw New ScheduledTransferException("transport")
        If before.Entry.Length <> entry.Length OrElse before.Entry.LastWriteUtc <> entry.LastWriteUtc Then
            Throw New SourceChangedException(path)
        End If
        ct.ThrowIfCancellationRequested()
        provider.Provider.DeleteFile(path)
        Dim after As StatResult = ResourceProviderCalls.Stat(provider.Provider, path)
        If after.Outcome <> StatOutcome.Missing Then Throw New ScheduledTransferException("failed")
        Return "permanent:VolumeUnknown"
    End Function

End Class

''' <summary>Opens a resource location as a connected side, asking nothing.</summary>
Friend Module ScheduledResourceSides

    Friend Function Open(location As ScheduledLocation, recursive As Boolean, ct As CancellationToken,
                         Optional descriptorOverride As ResourceDescriptor = Nothing,
                         Optional secretStoreOverride As IResourceSecretStore = Nothing,
                         Optional dialerOverride As IEndpointDialer = Nothing,
                         Optional tunnelOverride As IExchangeTunnelSource = Nothing,
                         Optional clockOverride As IResolutionClock = Nothing,
                         Optional hiveReadOverride As Func(Of String, String) = Nothing,
                         Optional hiveWriteOverride As Action(Of String, String) = Nothing) As ScheduledSideOpen
        Dim descriptor As ResourceDescriptor = If(descriptorOverride, ScheduledResourceRegistry.Find(location.ResourceId))
        If descriptor Is Nothing Then
            Return ScheduledSideOpen.Failed(New SideProblem(SideProblemKind.ResourceRemoved,
                If(String.IsNullOrEmpty(location.ResourceName), location.ResourceId, location.ResourceName)))
        End If
        If descriptor.ProtectionUnsupported Then
            Return ScheduledSideOpen.Failed(New SideProblem(SideProblemKind.Denied, descriptor.DisplayName))
        End If
        Dim secretStore As IResourceSecretStore = If(secretStoreOverride, New RegistryResourceSecretStore())
        Dim pinValue As String = Nothing
        If descriptor.PinRequired Then
            If String.IsNullOrEmpty(descriptor.PinSecretHandle) OrElse descriptor.PinChanged Then
                Return ScheduledSideOpen.Failed(New SideProblem(SideProblemKind.PinLocked, descriptor.DisplayName))
            End If
            Dim pinRead As CredentialReadResult = ResourceCommit.ReadPin(descriptor, secretStore)
            If pinRead.Outcome <> CredentialReadOutcome.Ok OrElse String.IsNullOrEmpty(pinRead.Secret) Then
                Return ScheduledSideOpen.Failed(New SideProblem(SideProblemKind.PinLocked, descriptor.DisplayName))
            End If
            pinValue = pinRead.Secret
        End If

        ' The credential is READ - from the viewer's own DPAPI store - never asked for. Missing or unreadable
        ' is the wall: the user fixes it in the viewer's resource manager.
        Dim read As CredentialReadResult = ResourceCommit.ReadCredential(descriptor, secretStore)
        If read.Outcome <> CredentialReadOutcome.Ok Then
            Return ScheduledSideOpen.Failed(New SideProblem(SideProblemKind.NoCredential, descriptor.DisplayName))
        End If

        Dim tunnel As IExchangeTunnelSource = If(tunnelOverride,
            ExchangeTunnelSources.[For](descriptor, secretStore, Nothing, Nothing))

        Dim flag As New RecursionFlag With {.Value = recursive}
        Dim outcome As ConnectOutcome
        Try
            ' consent: never. persistPin: never. An unpinned or changed key is a refusal here, by construction.
            outcome = ResourceConnect.ConnectAsync(descriptor, read.Secret,
                                                   Function(fingerprint As String, token As CancellationToken) False,
                                                   Function(fingerprint As String) False,
                                                   Function() flag.Value, ct,
                                                   dialerOverride:=dialerOverride,
                                                   tunnelSource:=tunnel,
                                                   resourcePin:=pinValue,
                                                   clockOverride:=clockOverride,
                                                   secretsOverride:=secretStore).GetAwaiter().GetResult()
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: connecting " & descriptor.DisplayName, ex)
            Return ScheduledSideOpen.Failed(ProviderSide.ProblemOf(ResourceFailure.FromException(ex), descriptor.DisplayName, forConnect:=True))
        End Try
        If Not outcome.Succeeded Then
            If outcome.Failure IsNot Nothing AndAlso outcome.Failure.Kind = ResourceFailureKind.Auth AndAlso
               descriptor.PinRequired AndAlso String.IsNullOrEmpty(outcome.Failure.Detail) Then
                Dim readHive As Func(Of String, String) = If(hiveReadOverride, New Func(Of String, String)(AddressOf ResourceRegistry.HiveRead))
                Dim writeHive As Action(Of String, String) = If(hiveWriteOverride, New Action(Of String, String)(AddressOf ResourceRegistry.HiveWrite))
                ResourceCommit.MarkPinChanged(descriptor, readHive, writeHive)
                Return ScheduledSideOpen.Failed(New SideProblem(SideProblemKind.PinLocked, descriptor.DisplayName))
            End If
            If outcome.HostKeyRefusal IsNot Nothing Then
                Return ScheduledSideOpen.Failed(New SideProblem(SideProblemKind.KeyNotPinned, descriptor.DisplayName))
            End If
            Return ScheduledSideOpen.Failed(ProviderSide.ProblemOf(outcome.Failure, descriptor.DisplayName, forConnect:=True))
        End If

        Dim folder As String = If(String.IsNullOrWhiteSpace(location.Path), descriptor.RootPath, location.Path)
        Dim disposable As IDisposable = TryCast(outcome.Provider, IDisposable)
        Return ScheduledSideOpen.Opened(New ProviderSide(outcome.Provider, descriptor.Id, folder, descriptor.DisplayName, flag, disposable))
    End Function

End Module
