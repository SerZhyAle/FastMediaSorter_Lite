#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq

Friend Enum ManagerFailure
    Permission
    SourceChanged
    LinkOrRoot
    IncompleteTree
    MissingOrDenied
    InvalidName
    Conflict
    Dependency
    NotEmpty
    ScratchSpace
End Enum

Friend NotInheritable Class ManagerCommandException
    Inherits IOException

    Friend Sub New(path As String, reason As ManagerFailure)
        MyBase.New("Manager command refused: " & reason.ToString())
        Me.Path = path
        Me.Reason = reason
    End Sub

    Friend ReadOnly Property Path As String
    Friend ReadOnly Property Reason As ManagerFailure
End Class

''' <summary>One capability projection is used for menus, admission and execution.</summary>
Friend Module ManagerCommandPlanner
    Friend Function Verb(kind As ManagerCommandKind) As ResourceVerb
        Select Case kind
            Case ManagerCommandKind.Copy : Return ResourceVerb.Copy
            Case ManagerCommandKind.Move : Return ResourceVerb.Move
            Case ManagerCommandKind.Rename : Return ResourceVerb.Rename
            Case ManagerCommandKind.CreateDirectory : Return ResourceVerb.CreateFolder
            Case Else : Return ResourceVerb.Delete
        End Select
    End Function

    Friend Function Allows(kind As ManagerCommandKind, source As ProviderCapabilities,
                           rows As IEnumerable(Of ManagerEntry), location As ManagerLocation,
                           Optional destination As ProviderCapabilities = Nothing) As Boolean
        If location Is Nothing Then Return False
        Dim selected As List(Of ManagerEntry) = If(rows, Enumerable.Empty(Of ManagerEntry)()).ToList()
        If kind <> ManagerCommandKind.CreateDirectory AndAlso selected.Count = 0 Then Return False
        If kind = ManagerCommandKind.Rename AndAlso selected.Count <> 1 Then Return False
        Dim context As New ResourceOperationContext With {.SourceCapabilities = source, .DestinationCapabilities = destination}
        If Not ResourceRefusalPolicy.IsAllowed(context, Verb(kind)) Then Return False
        If kind = ManagerCommandKind.CreateDirectory Then Return True
        If destination IsNot Nothing AndAlso selected.Any(Function(e) e.IsDirectory) AndAlso
           Not ResourceRefusalPolicy.IsAllowed(destination, ResourceVerb.CreateFolder) Then Return False
        Return selected.All(Function(e) String.Equals(e.Id, ManagerEntry.IdOf(location.Place, e.ProviderPath), StringComparison.Ordinal) AndAlso
                                      Not e.IsLink AndAlso ManagerPathRules.Contains(location.Place.Kind, location.Place.RootPath, e.ProviderPath) AndAlso
                                      Not ManagerPathRules.AreEqual(location.Place.Kind, location.Place.RootPath, e.ProviderPath))
    End Function

    Friend Function ValidLeaf(kind As ResourceKind, name As String) As Boolean
        If String.IsNullOrWhiteSpace(name) OrElse name = "." OrElse name = ".." OrElse
           name.IndexOfAny({"/"c, "\"c, ChrW(0)}) >= 0 Then Return False
        If kind = ResourceKind.Sftp Then Return True
        If Not FileOpRules.IsValidNewFileName(name) OrElse name.EndsWith("."c) OrElse name.EndsWith(" "c) Then Return False
        Dim stem As String = name.Split("."c)(0).TrimEnd(" "c).ToUpperInvariant()
        If {"CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$"}.Contains(stem) Then Return False
        If stem.Length = 4 AndAlso (stem.StartsWith("COM", StringComparison.Ordinal) OrElse stem.StartsWith("LPT", StringComparison.Ordinal)) AndAlso
           "123456789¹²³".IndexOf(stem(3)) >= 0 Then Return False
        Return True
    End Function

    Friend Function Failure(path As String, reason As ManagerFailure) As ManagerCommandException
        Return New ManagerCommandException(path, reason)
    End Function

    Friend Function ReasonText(reason As ManagerFailure) As String
        Select Case reason
            Case ManagerFailure.ScratchSpace : Return Localization.T("Недостаточно временного места для двух проверяемых копий файла")
            Case ManagerFailure.Permission : Return Localization.T("Этот ресурс не позволяет эту операцию с файлами")
            Case ManagerFailure.SourceChanged : Return Localization.T("Исходный объект изменился")
            Case ManagerFailure.LinkOrRoot : Return Localization.T("Операция с корнем, ссылкой или путём за пределами места запрещена")
            Case ManagerFailure.IncompleteTree : Return Localization.T("Не удалось полностью прочитать дерево папки")
            Case ManagerFailure.MissingOrDenied : Return Localization.T("Объект исчез или доступ запрещён")
            Case ManagerFailure.InvalidName : Return Localization.T("Недопустимое имя")
            Case ManagerFailure.Conflict : Return Localization.T("Имя уже занято")
            Case ManagerFailure.NotEmpty : Return Localization.T("Папка не пуста - оставлена на месте")
            Case Else : Return Localization.T("Предыдущая операция не завершена - объект оставлен на месте")
        End Select
    End Function

    ''' <summary>Consumes captured metadata and typed probes. All mutations are queued payloads.</summary>
    Friend Function Build(request As ManagerCommandRequest, tree As ManagerTreePlan,
                          probe As Func(Of ManagerEndpointLease, String, StatResult),
                          deleteDecision As Func(Of ResourceEntry, DeleteDecision),
                          Optional answer As CollisionAnswer? = Nothing) As BatchPlan
        If Not Allows(request.Kind, request.Source.Capabilities, request.Entries, request.Origin, request.Destination?.Capabilities) Then
            Throw Failure(request.Origin.DirectoryPath, ManagerFailure.Permission)
        End If
        Dim ops As New List(Of BatchOp)()
        Dim skips As New List(Of BatchSkip)()
        Dim collisions As New List(Of String)()
        Dim destination As ManagerEndpointLease = If(request.Destination, request.Source)
        Dim reserved As New HashSet(Of String)(If(destination.Place.IsRemotePosix, StringComparer.Ordinal, StringComparer.OrdinalIgnoreCase))
        Dim needsConfirm As Boolean
        Dim policy As String = request.CollisionPolicy
        If answer.HasValue Then
            policy = If(answer.Value = CollisionAnswer.Replace, "replace", If(answer.Value = CollisionAnswer.KeepBoth, "rename", "skip"))
        End If
        If request.Kind = ManagerCommandKind.CreateDirectory Then
            If Not ValidLeaf(request.Source.Place.Kind, request.LeafName) Then Throw Failure(request.LeafName, ManagerFailure.InvalidName)
            Dim target As String = ManagerPathRules.Combine(request.Source.Place.Kind, request.Origin.DirectoryPath, request.LeafName)
            If probe(request.Source, target).Outcome <> StatOutcome.Missing Then Throw Failure(target, ManagerFailure.Conflict)
            ops.Add(MakeOp(request, FileOpKind.CreateDirectory, Nothing, target))
        Else
            For Each root As ManagerTreeNode In tree.Roots
                Dim rootOps As New List(Of BatchOp)()
                Dim rootPath As String = root.Entry.FullPath
                Dim targetRoot As String = ""
                Dim deleting As Boolean = request.Kind = ManagerCommandKind.Delete OrElse request.Kind = ManagerCommandKind.DeletePermanently
                Dim decision As DeleteDecision = If(deleting, deleteDecision(root.Entry), Nothing)
                If deleting Then
                    needsConfirm = needsConfirm OrElse DeletePolicy.ShouldConfirm(request.ConfirmDelete, decision)
                    If decision IsNot Nothing AndAlso Not decision.IsPermanent Then
                        ops.Add(MakeOp(request, FileOpKind.RecycleDelete, root, "", decision))
                        Continue For
                    End If
                Else
                    Dim leaf As String = If(request.Kind = ManagerCommandKind.Rename, request.LeafName, root.Entry.Name)
                    If Not ValidLeaf(destination.Place.Kind, leaf) Then Throw Failure(leaf, ManagerFailure.InvalidName)
                    Dim folder As String = If(request.Kind = ManagerCommandKind.Rename, request.Source.Directory.ParentOf(rootPath), request.DestinationPath)
                    targetRoot = ManagerPathRules.Combine(destination.Place.Kind, folder, leaf)
                    Dim sameResource As Boolean = request.Source.Endpoint(rootPath).IsSameResourceAs(destination.Endpoint(targetRoot))
                    Dim caseRename As Boolean = False
                    If sameResource Then
                        Dim samePath As Boolean = ManagerPathRules.AreEqual(destination.Place.Kind, rootPath, targetRoot)
                        caseRename = request.Kind = ManagerCommandKind.Rename AndAlso samePath AndAlso Not String.Equals(rootPath, targetRoot, StringComparison.Ordinal)
                        If (samePath AndAlso Not caseRename) OrElse (root.Entry.IsDirectory AndAlso Not caseRename AndAlso
                           ManagerPathRules.Contains(destination.Place.Kind, rootPath, targetRoot)) Then Throw Failure(targetRoot, ManagerFailure.Conflict)
                    End If
                    Dim existing As StatResult = probe(destination, targetRoot)
                    Dim caseAlias As Boolean = request.Kind = ManagerCommandKind.Rename AndAlso sameResource AndAlso
                        existing.Outcome = StatOutcome.Found AndAlso (caseRename OrElse String.Equals(existing.Entry.FullPath, rootPath, StringComparison.Ordinal))
                    If Not caseAlias AndAlso Not ResolveTarget(request, root.Entry, destination, targetRoot, existing, policy, probe, reserved, skips, collisions) Then
                        For Each descendant In tree.Nodes.Where(Function(n) n.TopId = root.TopId AndAlso n.Entry.FullPath <> rootPath)
                            skips.Add(New BatchSkip With {.Path = descendant.Entry.FullPath, .Reason = SkipReason.NameTaken})
                        Next
                        Continue For
                    End If
                    If request.Kind = ManagerCommandKind.Rename Then
                        Dim renameOp As BatchOp = MakeOp(request, FileOpKind.Rename, root, targetRoot)
                        renameOp.ReplaceExisting = policy = "replace" AndAlso existing.Outcome = StatOutcome.Found AndAlso Not caseAlias
                        ops.Add(renameOp)
                        Continue For
                    End If
                End If
                Dim directoryTargets As New Dictionary(Of String, String)(If(request.Source.Place.IsRemotePosix, StringComparer.Ordinal, StringComparer.OrdinalIgnoreCase))
                Dim blockedDirs As New HashSet(Of String)(directoryTargets.Comparer)
                For Each node As ManagerTreeNode In tree.Nodes.Where(Function(n) n.TopId = root.TopId)
                    Dim entry As ResourceEntry = node.Entry
                    Dim target As String = ""
                    If Not deleting Then
                        If Not ValidLeaf(destination.Place.Kind, entry.Name) Then Throw Failure(entry.Name, ManagerFailure.InvalidName)
                        Dim parent As String = request.Source.Directory.ParentOf(entry.FullPath)
                        If blockedDirs.Any(Function(p) ManagerPathRules.Contains(request.Source.Place.Kind, p, entry.FullPath)) Then
                            skips.Add(New BatchSkip With {.Path = entry.FullPath, .Reason = SkipReason.NameTaken})
                            Continue For
                        End If
                        target = If(entry.FullPath = rootPath, targetRoot,
                                    ManagerPathRules.Combine(destination.Place.Kind, directoryTargets(parent), entry.Name))
                        If entry.FullPath <> rootPath AndAlso Not ResolveTarget(request, entry, destination, target, probe(destination, target), policy, probe, reserved, skips, collisions) Then
                            If entry.IsDirectory Then blockedDirs.Add(entry.FullPath)
                            Continue For
                        End If
                        If entry.IsDirectory Then directoryTargets(entry.FullPath) = target
                    End If
                    Dim kind As FileOpKind = If(deleting, If(entry.IsDirectory, FileOpKind.RemoveEmptyDirectory, FileOpKind.Delete),
                                               If(entry.IsDirectory, FileOpKind.CreateDirectory, If(request.Kind = ManagerCommandKind.Copy, FileOpKind.Copy, FileOpKind.Move)))
                    If deleting AndAlso entry.IsDirectory Then Continue For
                    Dim op As BatchOp = MakeOp(request, kind, node, target, decision)
                    op.ReplaceExisting = policy = "replace"
                    op.ManagerOperation.Dependencies.AddRange(rootOps.Where(Function(o) o.Kind = FileOpKind.CreateDirectory AndAlso
                        ManagerPathRules.Contains(request.Source.Place.Kind, o.Source, entry.FullPath)).Select(Function(o) o.ManagerOperation))
                    rootOps.Add(op)
                Next
                If deleting OrElse request.Kind = ManagerCommandKind.Move Then
                    For Each node As ManagerTreeNode In tree.Nodes.Where(Function(n) n.TopId = root.TopId AndAlso n.Entry.IsDirectory).Reverse()
                        Dim op As BatchOp = MakeOp(request, FileOpKind.RemoveEmptyDirectory, node, "", decision)
                        op.ManagerOperation.Dependencies.AddRange(rootOps.Where(Function(o) ManagerPathRules.Contains(request.Source.Place.Kind, node.Entry.FullPath, o.Source)).Select(Function(o) o.ManagerOperation))
                        op.ManagerOperation.HasSkippedChildren = skips.Any(Function(s) ManagerPathRules.Contains(request.Source.Place.Kind, node.Entry.FullPath, s.Path))
                        rootOps.Add(op)
                    Next
                End If
                ops.AddRange(rootOps)
            Next
        End If
        Return BatchPlan.FromExplicitOperations(ops, skips, collisions, needsConfirm)
    End Function

    Private Function ResolveTarget(request As ManagerCommandRequest, entry As ResourceEntry, destination As ManagerEndpointLease,
                                   ByRef target As String, existing As StatResult, policy As String,
                                   probe As Func(Of ManagerEndpointLease, String, StatResult), reserved As HashSet(Of String),
                                   skips As List(Of BatchSkip), collisions As List(Of String)) As Boolean
        If existing.Outcome <> StatOutcome.Found AndAlso existing.Outcome <> StatOutcome.Missing Then Throw Failure(target, ManagerFailure.MissingOrDenied)
        Dim taken As Boolean = existing.Outcome = StatOutcome.Found OrElse reserved.Contains(target)
        If Not taken Then
            reserved.Add(target)
            Return True
        End If
        If policy = "ask" Then
            collisions.Add(target)
            ' A provisional plan only: no admission until rebuilt with the answer.
            Return True
        End If
        If policy = "skip" Then
            skips.Add(New BatchSkip With {.Path = entry.FullPath, .Reason = SkipReason.NameTaken})
            Return False
        End If
        If policy = "replace" Then
            If reserved.Contains(target) OrElse existing.Entry.IsDirectory <> entry.IsDirectory OrElse existing.Entry.IsLink Then Throw Failure(target, ManagerFailure.Conflict)
            reserved.Add(target)
            Return True
        End If
        Dim parent As String = destination.Directory.ParentOf(target)
        Dim name As String = ManagerPathRules.LeafName(destination.Place.Kind, target)
        Dim ext As String = If(entry.IsDirectory, "", Path.GetExtension(name))
        Dim stem As String = name.Substring(0, name.Length - ext.Length)
        Dim counter As Integer = 2
        Do
            target = ManagerPathRules.Combine(destination.Place.Kind, parent, stem & " (" & counter.ToString(Globalization.CultureInfo.InvariantCulture) & ")" & ext)
            counter += 1
            existing = probe(destination, target)
            If existing.Outcome <> StatOutcome.Found AndAlso existing.Outcome <> StatOutcome.Missing Then Throw Failure(target, ManagerFailure.MissingOrDenied)
        Loop While existing.Outcome <> StatOutcome.Missing OrElse reserved.Contains(target)
        reserved.Add(target)
        Return True
    End Function

    Private Function MakeOp(request As ManagerCommandRequest, kind As FileOpKind, node As ManagerTreeNode,
                            destination As String, Optional decision As DeleteDecision = Nothing) As BatchOp
        Dim source As String = If(node Is Nothing, request.Origin.DirectoryPath, node.Entry.FullPath)
        Dim op As New BatchOp With {.Kind = kind, .Source = source, .Destination = destination,
            .SizeBytes = If(node Is Nothing OrElse node.Entry.IsDirectory, 0, node.Entry.Length),
            .PermanentDeleteReason = If(decision Is Nothing, PermanentReason.NotPermanent, decision.Reason)}
        op.ManagerOperation = New ManagerOperation(request, op, If(node Is Nothing, Nothing, node.Entry), If(node Is Nothing, "", node.TopId))
        Return op
    End Function
End Module
#End If
