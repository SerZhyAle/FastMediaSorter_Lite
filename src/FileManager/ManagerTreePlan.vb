#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading

Friend NotInheritable Class ManagerTreeNode
    Friend Sub New(entry As ResourceEntry, topId As String)
        Me.Entry = New ResourceEntry With {.FullPath = entry.FullPath, .Name = entry.Name, .Length = entry.Length,
            .LastWriteUtc = entry.LastWriteUtc, .IsDirectory = entry.IsDirectory, .LocalPath = entry.LocalPath,
            .VersionTag = entry.VersionTag, .IsLink = entry.IsLink, .IsHidden = entry.IsHidden, .IsSystem = entry.IsSystem}
        Me.TopId = topId
    End Sub
    Friend ReadOnly Property Entry As ResourceEntry
    Friend ReadOnly Property TopId As String
End Class

''' <summary>A complete, unfiltered manifest. Planning never mutates a provider.</summary>
Friend NotInheritable Class ManagerTreePlan
    Friend ReadOnly Property Nodes As New List(Of ManagerTreeNode)()
    Friend ReadOnly Property Roots As New List(Of ManagerTreeNode)()
    Friend Property DestinationDirectory As ResourceEntry
    Friend ReadOnly Property FileCount As Integer
        Get
            Return Nodes.Where(Function(n) Not n.Entry.IsDirectory).Count()
        End Get
    End Property
    Friend ReadOnly Property FolderCount As Integer
        Get
            Return Nodes.Where(Function(n) n.Entry.IsDirectory).Count()
        End Get
    End Property
    Friend ReadOnly Property TotalBytes As Long
        Get
            Return Nodes.Where(Function(n) Not n.Entry.IsDirectory).Sum(Function(n) Math.Max(0L, n.Entry.Length))
        End Get
    End Property

    Friend Shared Function Capture(request As ManagerCommandRequest) As ManagerTreePlan
        Dim result As New ManagerTreePlan()
        Dim lease As ManagerEndpointLease = request.Source
        Dim selected As List(Of ManagerEntry) = request.Entries.ToList()
        If Not ManagerLocation.AtRoot(lease.Place).SameAs(ManagerLocation.AtRoot(request.Origin.Place)) OrElse
           (lease.Provider.Kind = ResourceKind.Sftp) <> lease.Place.IsRemotePosix Then Throw ManagerCommandPlanner.Failure(request.Origin.DirectoryPath, ManagerFailure.LinkOrRoot)
        ValidatePath(lease, request.Origin.DirectoryPath, allowRoot:=True)
        If Not RequireEntry(lease, request.Origin.DirectoryPath).IsDirectory Then Throw ManagerCommandPlanner.Failure(request.Origin.DirectoryPath, ManagerFailure.Conflict)
        If request.Kind = ManagerCommandKind.Copy OrElse request.Kind = ManagerCommandKind.Move Then
            If request.Destination Is Nothing Then Throw ManagerCommandPlanner.Failure(request.DestinationPath, ManagerFailure.MissingOrDenied)
            ValidatePath(request.Destination, request.DestinationPath, allowRoot:=True)
            Dim folder As ResourceEntry = RequireEntry(request.Destination, request.DestinationPath)
            If Not folder.IsDirectory Then Throw ManagerCommandPlanner.Failure(request.DestinationPath, ManagerFailure.Conflict)
            result.DestinationDirectory = New ManagerTreeNode(folder, "").Entry
        End If
        If request.Kind = ManagerCommandKind.CreateDirectory Then Return result
        Dim seen As New HashSet(Of String)(If(lease.Place.IsRemotePosix, StringComparer.Ordinal, StringComparer.OrdinalIgnoreCase))
        For Each row As ManagerEntry In selected
            request.Token.ThrowIfCancellationRequested()
            If Not String.Equals(row.Id, ManagerEntry.IdOf(lease.Place, row.ProviderPath), StringComparison.Ordinal) Then
                Throw ManagerCommandPlanner.Failure(row.ProviderPath, ManagerFailure.LinkOrRoot)
            End If
            If selected.Any(Function(parent) parent.IsDirectory AndAlso Not Object.ReferenceEquals(parent, row) AndAlso
                                Not lease.Directory.PathsEqual(parent.ProviderPath, row.ProviderPath) AndAlso
                                ManagerPathRules.Contains(lease.Place.Kind, parent.ProviderPath, row.ProviderPath)) Then Continue For
            If Not seen.Add(row.ProviderPath) Then Continue For
            ValidatePath(lease, row.ProviderPath, allowRoot:=False)
            Dim entry As ResourceEntry = RequireEntry(lease, row.ProviderPath)
            If entry.IsDirectory <> row.IsDirectory OrElse entry.IsLink OrElse
               ((row.VersionTag.Length > 0 OrElse entry.VersionTag.Length > 0) AndAlso Not String.Equals(row.VersionTag, entry.VersionTag, StringComparison.Ordinal)) OrElse
               (row.DateKnown AndAlso entry.LastWriteUtc <> row.ModifiedUtc) OrElse
               (Not row.IsDirectory AndAlso row.SizeKnown AndAlso entry.Length <> row.SizeBytes) Then
                Throw ManagerCommandPlanner.Failure(row.ProviderPath, ManagerFailure.SourceChanged)
            End If
            Dim root As New ManagerTreeNode(entry, row.Id)
            result.Roots.Add(root)
            Dim stack As New Stack(Of ResourceEntry)()
            stack.Push(entry)
            Dim visited As New HashSet(Of String)(seen.Comparer)
            While stack.Count > 0
                request.Token.ThrowIfCancellationRequested()
                Dim current As ResourceEntry = stack.Pop()
                ValidatePath(lease, current.FullPath, allowRoot:=False)
                If current.IsLink OrElse Not visited.Add(current.FullPath) Then
                    Throw ManagerCommandPlanner.Failure(current.FullPath, ManagerFailure.LinkOrRoot)
                End If
                result.Nodes.Add(New ManagerTreeNode(current, row.Id))
                If Not current.IsDirectory Then Continue While
                Dim listing As DirectoryListing = lease.Directory.ListChildren(current.FullPath, request.Token)
                If listing.Outcome <> DirectoryOutcome.Listed OrElse listing.IsTruncated Then
                    Throw ManagerCommandPlanner.Failure(current.FullPath, ManagerFailure.IncompleteTree)
                End If
                For Each child As ResourceEntry In listing.Entries.Reverse()
                    If child.Name = "." OrElse child.Name = ".." Then Continue For
                    If Not ManagerCommandPlanner.ValidLeaf(lease.Place.Kind, child.Name) OrElse
                       Not lease.Directory.PathsEqual(lease.Directory.ParentOf(child.FullPath), current.FullPath) Then
                        Throw ManagerCommandPlanner.Failure(child.FullPath, ManagerFailure.LinkOrRoot)
                    End If
                    stack.Push(child)
                Next
            End While
        Next
        Return result
    End Function

    Friend Shared Function RequireEntry(lease As ManagerEndpointLease, path As String) As ResourceEntry
        Dim stat As StatResult = lease.Directory.StatEntry(path)
        If stat.Outcome <> StatOutcome.Found Then Throw ManagerCommandPlanner.Failure(path, ManagerFailure.MissingOrDenied)
        Return stat.Entry
    End Function

    Friend Sub RevalidateVersions(request As ManagerCommandRequest, fresh As ManagerTreePlan)
        If fresh.Nodes.Count <> Nodes.Count Then Throw ManagerCommandPlanner.Failure(request.Origin.DirectoryPath, ManagerFailure.SourceChanged)
        Dim comparer As StringComparer = If(request.Source.Place.IsRemotePosix, StringComparer.Ordinal, StringComparer.OrdinalIgnoreCase)
        Dim versions As Dictionary(Of String, ResourceEntry) = fresh.Nodes.ToDictionary(Function(n) n.Entry.FullPath, Function(n) n.Entry, comparer)
        For Each node As ManagerTreeNode In Nodes
            Dim current As ResourceEntry = Nothing
            If Not versions.TryGetValue(node.Entry.FullPath, current) OrElse current.IsDirectory <> node.Entry.IsDirectory OrElse
               Not current.SameVersionAs(node.Entry) Then Throw ManagerCommandPlanner.Failure(node.Entry.FullPath, ManagerFailure.SourceChanged)
        Next
        If DestinationDirectory IsNot Nothing AndAlso (fresh.DestinationDirectory Is Nothing OrElse
           Not fresh.DestinationDirectory.SameVersionAs(DestinationDirectory)) Then
            Throw ManagerCommandPlanner.Failure(request.DestinationPath, ManagerFailure.SourceChanged)
        End If
    End Sub

    ''' <summary>Check every existing ancestor, including the configured root. No traversal through a link.</summary>
    Friend Shared Sub ValidatePath(lease As ManagerEndpointLease, path As String, allowRoot As Boolean,
                                  Optional allowMissing As Boolean = False)
        Dim root As String = lease.Place.RootPath
        If Not lease.Directory.IsWithinRoot(root, path) OrElse
           (Not allowRoot AndAlso lease.Directory.PathsEqual(root, path)) Then
            Throw ManagerCommandPlanner.Failure(path, ManagerFailure.LinkOrRoot)
        End If
        Dim cursor As String = path
        While cursor IsNot Nothing
            Dim stat As StatResult = lease.Directory.StatEntry(cursor)
            If stat.Outcome = StatOutcome.Found Then
                If stat.Entry.IsLink Then Throw ManagerCommandPlanner.Failure(cursor, ManagerFailure.LinkOrRoot)
            ElseIf Not allowMissing OrElse stat.Outcome <> StatOutcome.Missing Then
                Throw ManagerCommandPlanner.Failure(cursor, ManagerFailure.MissingOrDenied)
            End If
            If lease.Directory.PathsEqual(root, cursor) Then Return
            cursor = lease.Directory.ParentOf(cursor)
        End While
        Throw ManagerCommandPlanner.Failure(path, ManagerFailure.LinkOrRoot)
    End Sub
End Class
#End If
