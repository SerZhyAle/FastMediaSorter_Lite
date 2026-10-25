#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading

Friend NotInheritable Class ManagerArchiveOptions
    Friend Property Extract As Boolean
    Friend Property DeleteOriginals As Boolean ' False unless explicitly checked in the dialog.
End Class

Friend Module ManagerArchiveMessages
    Friend Function Describe(reason As ArchiveFailure) As String
        Select Case reason
            Case ArchiveFailure.Encrypted : Return Localization.T("Архив защищён паролем - откройте его архиватором")
            Case ArchiveFailure.MultiVolume : Return Localization.T("Многотомные архивы не поддерживаются")
            Case ArchiveFailure.UnsafeName, ArchiveFailure.Link : Return ManagerCommandPlanner.ReasonText(ManagerFailure.LinkOrRoot)
            Case ArchiveFailure.DuplicateName : Return ManagerCommandPlanner.ReasonText(ManagerFailure.Conflict)
            Case ArchiveFailure.ChangedInput : Return ManagerCommandPlanner.ReasonText(ManagerFailure.SourceChanged)
            Case ArchiveFailure.EntryLimit : Return Localization.TF("Превышен лимит распаковки: {0}", Localization.TF("{0}: {1}", Localization.T("Лимит записей в архиве"), ArchiveExtractionPlan.MaxEntries))
            Case ArchiveFailure.EntrySizeLimit : Return Localization.TF("Превышен лимит распаковки: {0}", Localization.TF("Одна запись: {0} байт", ArchiveSession.Default_Max_Entry_Bytes))
            Case ArchiveFailure.TotalSizeLimit : Return Localization.TF("Превышен лимит распаковки: {0}", Localization.TF("Все записи: {0} байт", ArchiveSession.Default_Max_Cache_Bytes))
            Case ArchiveFailure.ExpansionLimit : Return Localization.T("Похоже на архив-бомбу")
            Case ArchiveFailure.Space : Return Localization.T("На диске не хватает места. Освободите место и повторите попытку.")
            Case ArchiveFailure.Unsupported : Return Localization.TF("Формат архива не поддерживается ({0})", String.Join("/", {"ZIP", "CBZ", "7z", "RAR", "CBR"}))
            Case Else : Return Localization.T("Не удалось прочитать архив")
        End Select
    End Function
End Module

''' <summary>Owns scratch and leases through publication and conditional cleanup, independently of navigation.</summary>
Friend NotInheritable Class ManagerArchiveJob
    Implements IDisposable

    Private ReadOnly original As ManagerCommandRequest
    Private ReadOnly captured As ManagerTreePlan
    Private ReadOnly ownedStage As String
    Private cleanup As ManagerCommandRequest
    Private publication As IReadOnlyList(Of BatchOp)
    Private ReadOnly blockedRoots As New HashSet(Of String)(StringComparer.Ordinal)
    Private ReadOnly cleanupOps As New List(Of BatchOp)()
    Private ReadOnly proofs As New Dictionary(Of String, ArchiveWriteRecord)(StringComparer.OrdinalIgnoreCase)
    Private zipArtifact As VerifiedZipArtifact
    Friend Property Request As ManagerCommandRequest
    Friend ReadOnly Property DeleteOriginals As Boolean
    Friend ReadOnly Property UseRecycleBin As Boolean
        Get
            Return original.UseRecycleBin
        End Get
    End Property
    Friend ReadOnly Property ManifestText As String
        Get
            Return Localization.TF("Файлов: {0}; папок: {1}; байт: {2}", captured.FileCount, captured.FolderCount, captured.TotalBytes)
        End Get
    End Property

    Private Sub New(original As ManagerCommandRequest, captured As ManagerTreePlan, stage As String, deleteOriginals As Boolean)
        Me.original = original
        Me.captured = captured
        Me.ownedStage = stage
        Me.DeleteOriginals = deleteOriginals
    End Sub

    Friend Shared Function Prepare(original As ManagerCommandRequest, options As ManagerArchiveOptions, scratch As String,
                                   Optional progress As Action(Of Integer, Long) = Nothing) As ManagerArchiveJob
        If original.Destination Is Nothing OrElse Not ResourceRefusalPolicy.IsAllowed(original.Destination.Capabilities, ResourceVerb.WriteDestination) Then
            Throw ManagerCommandPlanner.Failure(original.DestinationPath, ManagerFailure.Permission)
        End If
        Dim tree As ManagerTreePlan = ManagerTreePlan.Capture(original)
        If tree.Roots.Count = 0 Then Throw New ArchiveSafetyException(ArchiveFailure.UnsafeName)
        Dim stage As String = Path.Combine(scratch, "manager-archive-" & Guid.NewGuid().ToString("N"))
        ArchiveExtractionPlan.RejectReparseAncestors(stage)
        Directory.CreateDirectory(stage)
        Dim job As New ManagerArchiveJob(original, tree, stage, options.DeleteOriginals AndAlso Not options.Extract)
        Try
            Dim payload As String = Path.Combine(stage, "payload")
            Directory.CreateDirectory(payload)
            If options.Extract Then
                If tree.Roots.Count <> 1 OrElse tree.Roots(0).Entry.IsDirectory Then Throw New ArchiveSafetyException(ArchiveFailure.Unsupported)
                Dim source As ResourceEntry = tree.Roots(0).Entry
                Dim materialized As String = source.FullPath
                job.CheckVersion(source)
                If original.Source.Capabilities.NeedsMaterialisation Then
                    ArchiveExtractionReader.PreflightSpace(stage, source.Length)
                    materialized = Path.Combine(stage, "input" & Path.GetExtension(source.Name))
                    original.Source.Provider.Download(source.FullPath, materialized, Nothing, original.Token)
                End If
                job.CheckVersion(source)
                Dim plan As ArchiveExtractionPlan = ArchiveExtractionReader.Extract(materialized, payload, original.Token, progress)
                job.CheckVersion(source)
                ' Apply provider naming and containment rules to every hierarchy component before publishing.
                For Each item In plan.Items
                    If item.Digest IsNot Nothing Then job.proofs.Add(item.TargetPath, item.Digest)
                    Dim target As String = original.DestinationPath
                    For Each leaf In item.RelativePath.Split("/"c)
                        If Not ManagerCommandPlanner.ValidLeaf(original.Destination.Place.Kind, leaf) Then Throw New ArchiveSafetyException(ArchiveFailure.UnsafeName)
                        target = ManagerPathRules.Combine(original.Destination.Place.Kind, target, leaf)
                    Next
                    ManagerTreePlan.ValidatePath(original.Destination, target, False, True)
                    If original.Source.Endpoint(source.FullPath).IsSameResourceAs(original.Destination.Endpoint(target)) AndAlso
                       original.Source.Directory.PathsEqual(source.FullPath, target) Then Throw ManagerCommandPlanner.Failure(target, ManagerFailure.Conflict)
                Next
            Else
                If Not ManagerCommandPlanner.ValidLeaf(original.Destination.Place.Kind, original.LeafName) OrElse
                   Not original.LeafName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) Then Throw New ArchiveSafetyException(ArchiveFailure.UnsafeName)
                Dim target As String = ManagerPathRules.Combine(original.Destination.Place.Kind, original.DestinationPath, original.LeafName)
                For Each root In tree.Roots
                    If original.Source.Endpoint(root.Entry.FullPath).IsSameResourceAs(original.Destination.Endpoint(target)) AndAlso
                       (original.Source.Directory.PathsEqual(root.Entry.FullPath, target) OrElse
                        (root.Entry.IsDirectory AndAlso ManagerPathRules.Contains(original.Source.Place.Kind, root.Entry.FullPath, target))) Then
                        Throw ManagerCommandPlanner.Failure(target, ManagerFailure.Conflict)
                    End If
                Next
                ' Conservative ZIP overhead plus one materialised input on a remote source.
                Dim maximumInput As Long = tree.Nodes.Where(Function(n) Not n.Entry.IsDirectory).Select(Function(n) n.Entry.Length).DefaultIfEmpty(0).Max()
                Dim required As Long = VerifiedZipWriter.CheckedAdd(tree.TotalBytes, VerifiedZipWriter.CheckedAdd(tree.TotalBytes \ 100, CLng(tree.Nodes.Count) * 1024L))
                If original.Source.Capabilities.NeedsMaterialisation Then required = VerifiedZipWriter.CheckedAdd(required, maximumInput)
                ArchiveExtractionReader.PreflightSpace(stage, required)
                Dim inputs As New List(Of ArchiveWriteInput)()
                For Each node In tree.Nodes
                    Dim entry As ResourceEntry = node.Entry
                    Dim root As ManagerTreeNode = tree.Roots.Single(Function(n) n.TopId = node.TopId)
                    Dim name As String = root.Entry.Name & entry.FullPath.Substring(root.Entry.FullPath.Length).Replace("\"c, "/"c)
                    If entry.IsDirectory Then name &= "/"
                    inputs.Add(New ArchiveWriteInput With {.Name = name, .IsDirectory = entry.IsDirectory, .Length = If(entry.IsDirectory, 0, entry.Length),
                        .ModifiedUtc = entry.LastWriteUtc, .ValidateVersion = Sub() job.CheckVersion(entry),
                        .OpenRead = Function(ct) job.OpenInput(entry, ct)})
                Next
                Dim artifact As VerifiedZipArtifact = VerifiedZipWriter.Write(Path.Combine(stage, "archive.part"), inputs, original.Token, progress)
                ' Re-enumeration catches additions/removals that a provider's directory timestamp might miss.
                tree.RevalidateVersions(original, ManagerTreePlan.Capture(original))
                File.Move(artifact.Path, Path.Combine(payload, original.LeafName))
                artifact.Path = Path.Combine(payload, original.LeafName)
                job.zipArtifact = artifact
            End If
            original.Token.ThrowIfCancellationRequested()
            Dim place As New ManagerPlace("archive-stage:" & original.Id, "Archive stage", ResourceKind.Local, payload)
            Dim provider As New LocalProvider(Function() False)
            Dim opened As ManagerOpenResult = ManagerOpenResult.Ok(provider)
            Dim lease As New ManagerEndpointLease(place, opened)
            Dim rows As New List(Of ManagerEntry)()
            For Each entry In lease.Directory.ListChildren(payload, original.Token).Entries
                ' NTFS find data for a just-written directory can lag its stat metadata.
                rows.Add(ManagerEntry.FromResource(place, ManagerTreePlan.RequireEntry(lease, entry.FullPath)))
            Next
            job.Request = New ManagerCommandRequest(ManagerCommandKind.Copy, New ManagerLocation(place, payload), original.Generation,
                rows, lease, original.Destination.Fork(), original.DestinationPath, collisionPolicy:=original.CollisionPolicy)
            job.Request.ArchiveJob = job
            Return job
        Catch
            job.Dispose()
            Throw
        End Try
    End Function

    Private Sub CheckVersion(expected As ResourceEntry)
        original.Token.ThrowIfCancellationRequested()
        ManagerTreePlan.ValidatePath(original.Source, expected.FullPath, False)
        Dim actual As ResourceEntry = ManagerTreePlan.RequireEntry(original.Source, expected.FullPath)
        If actual.IsDirectory <> expected.IsDirectory OrElse Not actual.SameVersionAs(expected) Then Throw ManagerCommandPlanner.Failure(expected.FullPath, ManagerFailure.SourceChanged)
    End Sub

    Private Function OpenInput(entry As ResourceEntry, ct As CancellationToken) As Stream
        If Not original.Source.Capabilities.NeedsMaterialisation Then Return New FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete, 81920, FileOptions.SequentialScan)
        Dim file As String = Path.Combine(ownedStage, Guid.NewGuid().ToString("N") & ".input")
        original.Source.Provider.Download(entry.FullPath, file, Nothing, ct)
        Return New FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan Or FileOptions.DeleteOnClose)
    End Function

    Friend Function AddCleanup(plan As BatchPlan, decide As Func(Of ManagerEndpointLease, ResourceEntry, DeleteDecision)) As BatchPlan
        publication = plan.Operations.ToArray()
        For Each op As BatchOp In publication
            Dim target As String = op.Destination
            Dim operation As BatchOp = op
            op.ManagerOperation.ReportPath = If(zipArtifact IsNot Nothing, target,
                captured.Roots(0).Entry.FullPath & "!/" & Path.GetRelativePath(Request.Origin.DirectoryPath, op.Source).Replace("\"c, "/"c))
            op.ManagerOperation.BeforeExecute = Sub()
                                                    If original.Token.IsCancellationRequested Then original.Token.ThrowIfCancellationRequested()
                                                    ' The archive selected for extraction is never a replacement target.
                                                    For Each root As ManagerTreeNode In captured.Roots
                                                        If original.Source.Endpoint(root.Entry.FullPath).IsSameResourceAs(original.Destination.Endpoint(target)) AndAlso
                                                           original.Source.Directory.PathsEqual(root.Entry.FullPath, target) Then Throw ManagerCommandPlanner.Failure(target, ManagerFailure.Conflict)
                                                    Next
                                                End Sub
            If op.Kind = FileOpKind.Copy Then op.ManagerOperation.AfterExecute = Sub() ProvePublication(operation)
        Next
        If Not DeleteOriginals Then Return plan
        cleanup = New ManagerCommandRequest(ManagerCommandKind.Delete, original.Origin, original.Generation, original.Entries,
            original.Source.Fork(), confirmDelete:=original.ConfirmDelete, useRecycleBin:=original.UseRecycleBin)
        Dim ops As New List(Of BatchOp)(plan.Operations)
        Dim confirm As Boolean
        For Each root In captured.Roots
            Dim members As List(Of ManagerTreeNode) = captured.Nodes.Where(Function(n) n.TopId = root.TopId).ToList()
            Dim rootOps As New List(Of BatchOp)()
            ' Files individually: no recursive directory recycling or recursive provider deletion.
            For Each node In members.Where(Function(n) Not n.Entry.IsDirectory).Concat(members.Where(Function(n) n.Entry.IsDirectory).Reverse())
                Dim decision As DeleteDecision = decide(original.Source, node.Entry)
                confirm = confirm OrElse DeletePolicy.ShouldConfirm(original.ConfirmDelete, decision)
                Dim op As New BatchOp With {.Kind = If(node.Entry.IsDirectory, FileOpKind.RemoveEmptyDirectory, If(decision.IsPermanent, FileOpKind.Delete, FileOpKind.RecycleDelete)),
                    .Source = node.Entry.FullPath, .SizeBytes = If(node.Entry.IsDirectory, 0, node.Entry.Length), .PermanentDeleteReason = decision.Reason}
                op.ManagerOperation = New ManagerOperation(cleanup, op, node.Entry, node.TopId)
                Dim capturedRoot As ManagerTreeNode = root
                op.ManagerOperation.BeforeExecute = Sub() GuardCleanup(capturedRoot)
                op.ManagerOperation.Dependencies.AddRange(publication.Select(Function(p) p.ManagerOperation))
                If node.Entry.IsDirectory Then op.ManagerOperation.Dependencies.AddRange(rootOps.Select(Function(p) p.ManagerOperation))
                rootOps.Add(op)
                cleanupOps.Add(op)
            Next
            ops.AddRange(rootOps)
        Next
        Return BatchPlan.FromExplicitOperations(ops, plan.Skipped.ToList(), plan.NeedsCollisionAnswer.ToList(), confirm)
    End Function

    Private Sub ProvePublication(op As BatchOp)
        Dim ct As CancellationToken = Request.Token
        ct.ThrowIfCancellationRequested()
        Dim expectedVersion As ResourceEntry = op.ManagerOperation.Outcome?.PublishedEntry
        ManagerTreePlan.ValidatePath(original.Destination, op.Destination, False)
        Dim version As ResourceEntry = ManagerTreePlan.RequireEntry(original.Destination, op.Destination)
        If expectedVersion Is Nothing OrElse Not version.SameVersionAs(expectedVersion) Then Throw ManagerCommandPlanner.Failure(op.Destination, ManagerFailure.SourceChanged)
        Dim readback As String = Path.Combine(ownedStage, "publication.part")
        Try
            ArchiveExtractionReader.PreflightSpace(readback, version.Length)
            original.Destination.Provider.Download(op.Destination, readback, Nothing, ct)
            If zipArtifact IsNot Nothing Then
                VerifiedZipWriter.Verify(readback, zipArtifact.Records, ct)
            Else
                Dim proof As ArchiveWriteRecord = proofs(op.Source)
                Using source As New FileStream(readback, FileMode.Open, FileAccess.Read, FileShare.Read)
                    Dim actual As ArchiveWriteRecord = VerifiedZipWriter.Digest(source, proof.Length, ct)
                    If actual.Length <> proof.Length OrElse Not actual.Sha256.SequenceEqual(proof.Sha256) Then Throw New ArchiveSafetyException(ArchiveFailure.Verification)
                End Using
            End If
            If Not ManagerTreePlan.RequireEntry(original.Destination, op.Destination).SameVersionAs(version) Then Throw ManagerCommandPlanner.Failure(op.Destination, ManagerFailure.SourceChanged)
        Finally
            If File.Exists(readback) Then File.Delete(readback)
        End Try
    End Sub

    Private Sub GuardCleanup(root As ManagerTreeNode)
        original.Token.ThrowIfCancellationRequested()
        Request.Token.ThrowIfCancellationRequested()
        If publication.Count = 0 OrElse publication.Any(Function(op) Not op.ManagerOperation.Completed) Then Throw ManagerCommandPlanner.Failure(root.Entry.FullPath, ManagerFailure.Dependency)
        If blockedRoots.Contains(root.TopId) Then Throw ManagerCommandPlanner.Failure(root.Entry.FullPath, ManagerFailure.SourceChanged)
        Try
            For Each op As BatchOp In publication
                Dim version As ResourceEntry = op.ManagerOperation.Outcome?.PublishedEntry
                If version Is Nothing OrElse Not ManagerTreePlan.RequireEntry(original.Destination, op.Destination).SameVersionAs(version) Then
                    Throw ManagerCommandPlanner.Failure(op.Destination, ManagerFailure.SourceChanged)
                End If
            Next
            For Each member In captured.Nodes.Where(Function(n) n.TopId = root.TopId)
                Dim own As BatchOp = cleanupOps.Single(Function(op) original.Source.Directory.PathsEqual(op.Source, member.Entry.FullPath))
                If own.ManagerOperation.Completed Then Continue For
                ' Directory times change when our own files are removed. Every remaining file is still version-checked.
                If Not member.Entry.IsDirectory Then CheckVersion(member.Entry)
                If member.Entry.IsDirectory Then
                    ManagerTreePlan.ValidatePath(original.Source, member.Entry.FullPath, False)
                    Dim current As ResourceEntry = ManagerTreePlan.RequireEntry(original.Source, member.Entry.FullPath)
                    If Not current.IsDirectory Then Throw ManagerCommandPlanner.Failure(member.Entry.FullPath, ManagerFailure.SourceChanged)
                    If Not cleanupOps.Any(Function(op) op.ManagerOperation.Completed AndAlso ManagerPathRules.Contains(original.Source.Place.Kind, member.Entry.FullPath, op.Source)) AndAlso
                       Not current.SameVersionAs(member.Entry) Then Throw ManagerCommandPlanner.Failure(member.Entry.FullPath, ManagerFailure.SourceChanged)
                    Dim children As DirectoryListing = original.Source.Directory.ListChildren(member.Entry.FullPath, original.Token)
                    Dim expected As List(Of ManagerTreeNode) = captured.Nodes.Where(Function(n) n.TopId = root.TopId AndAlso
                        original.Source.Directory.PathsEqual(original.Source.Directory.ParentOf(n.Entry.FullPath), member.Entry.FullPath) AndAlso
                        Not cleanupOps.Any(Function(op) op.ManagerOperation.Completed AndAlso original.Source.Directory.PathsEqual(op.Source, n.Entry.FullPath))).ToList()
                    If children.Outcome <> DirectoryOutcome.Listed OrElse children.IsTruncated OrElse children.Entries.Count <> expected.Count OrElse
                       children.Entries.Any(Function(e) e.IsLink OrElse Not expected.Any(Function(n) original.Source.Directory.PathsEqual(n.Entry.FullPath, e.FullPath) AndAlso e.IsDirectory = n.Entry.IsDirectory AndAlso
                           (e.IsDirectory OrElse e.SameVersionAs(n.Entry)))) Then
                        Throw ManagerCommandPlanner.Failure(member.Entry.FullPath, ManagerFailure.SourceChanged)
                    End If
                End If
            Next
        Catch
            blockedRoots.Add(root.TopId)
            Throw
        End Try
    End Sub

    Friend Sub Cancel()
        original.Cancel()
        cleanup?.Cancel()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        original.Dispose()
        cleanup?.Dispose()
        Try
            If Directory.Exists(ownedStage) Then Directory.Delete(ownedStage, True)
        Catch ex As IOException
        Catch ex As UnauthorizedAccessException
        End Try
    End Sub
End Class
#End If
