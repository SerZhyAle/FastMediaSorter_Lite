#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading

Friend Enum ManagerCommandKind
    Copy
    Move
    Rename
    CreateDirectory
    Delete
    DeletePermanently
End Enum

''' <summary>A counted connection reference: navigation and panel closure cannot revoke queued work.</summary>
Friend NotInheritable Class ManagerEndpointLease
    Implements IDisposable

    Private opened As ManagerOpenResult
    Private ReadOnly policy As Func(Of IFileSystemProvider, ProviderCapabilities)

    Friend Sub New(place As ManagerPlace, opened As ManagerOpenResult,
                   Optional policy As Func(Of IFileSystemProvider, ProviderCapabilities) = Nothing)
        Me.Place = place
        Me.opened = opened
        Me.ConnectedDescriptor = opened.ConnectedDescriptor
        Me.Provider = TryCast(opened.Provider, IFileSystemProvider)
        If Provider Is Nothing Then Throw New InvalidOperationException("The place has no operation provider.")
        Me.Directory = opened.Provider
        Me.policy = policy
    End Sub

    Friend ReadOnly Property Place As ManagerPlace
    Friend ReadOnly Property Provider As IFileSystemProvider
    Friend ReadOnly Property Directory As IManagerDirectoryProvider
    Friend ReadOnly Property ConnectedDescriptor As ResourceDescriptor
    Friend ReadOnly Property Capabilities As ProviderCapabilities
        Get
            If opened Is Nothing Then Throw New ObjectDisposedException(NameOf(ManagerEndpointLease))
            Return If(policy Is Nothing, Provider.Capabilities, policy(Provider))
        End Get
    End Property

    Friend Function Fork() As ManagerEndpointLease
        Dim held As ManagerOpenResult = opened
        If held Is Nothing OrElse Not held.Retain() Then Throw New ObjectDisposedException(NameOf(ManagerEndpointLease))
        Return New ManagerEndpointLease(Place, held, policy)
    End Function

    Friend Function Endpoint(path As String) As TransferEndpoint
        ' Plain Windows paths share a physical namespace, even across drive-place entries.
        Return New TransferEndpoint(Provider, path, If(Place.NeedsConnection, Place.Id, "manager:windows"))
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Interlocked.Exchange(opened, Nothing)?.Release()
    End Sub
End Class

''' <summary>Immutable command facts captured before prompts. Owns both endpoint leases until the run ends.</summary>
Friend NotInheritable Class ManagerCommandRequest
    Implements IDisposable

    Private ReadOnly cancellation As New CancellationTokenSource()
    Private disposed As Integer

    Friend Sub New(kind As ManagerCommandKind, origin As ManagerLocation, generation As Integer,
                   entries As IEnumerable(Of ManagerEntry), source As ManagerEndpointLease,
                   Optional destination As ManagerEndpointLease = Nothing, Optional destinationPath As String = "",
                   Optional leafName As String = "", Optional collisionPolicy As String = "ask",
                   Optional confirmDelete As String = "always", Optional useRecycleBin As Boolean = True)
        Me.Id = Guid.NewGuid().ToString("N")
        Me.Kind = kind
        Me.Origin = origin
        Me.Generation = generation
        Me.Entries = Array.AsReadOnly(If(entries, Enumerable.Empty(Of ManagerEntry)()).ToArray())
        Me.Source = source
        Me.Destination = destination
        Me.DestinationPath = destinationPath
        Me.LeafName = leafName
        Me.CollisionPolicy = collisionPolicy
        Me.ConfirmDelete = confirmDelete
        Me.UseRecycleBin = useRecycleBin
    End Sub

    Friend ReadOnly Property Id As String
    Friend ReadOnly Property Kind As ManagerCommandKind
    Friend ReadOnly Property Origin As ManagerLocation
    Friend ReadOnly Property Generation As Integer
    Friend ReadOnly Property Entries As IReadOnlyList(Of ManagerEntry)
    Friend ReadOnly Property Source As ManagerEndpointLease
    Friend ReadOnly Property Destination As ManagerEndpointLease
    Friend ReadOnly Property DestinationPath As String
    Friend ReadOnly Property LeafName As String
    Friend ReadOnly Property CollisionPolicy As String
    Friend ReadOnly Property ConfirmDelete As String
    Friend ReadOnly Property UseRecycleBin As Boolean
    Friend Property ArchiveJob As ManagerArchiveJob
    Friend ReadOnly Property Token As CancellationToken
        Get
            Return cancellation.Token
        End Get
    End Property

    Friend Sub Cancel()
        If Volatile.Read(disposed) <> 0 Then Return
        cancellation.Cancel()
        ArchiveJob?.Cancel()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If Interlocked.Exchange(disposed, 1) <> 0 Then Return
        Source?.Dispose()
        Destination?.Dispose()
        ArchiveJob?.Dispose()
        cancellation.Dispose()
    End Sub
End Class

''' <summary>One internal copy/cut selection, kept by the F3 session, never exported to the shell.</summary>
Friend NotInheritable Class ManagerSelectionClipboard
    Implements IDisposable

    Private source As ManagerEndpointLease
    Friend ReadOnly Property Location As ManagerLocation
    Friend ReadOnly Property Generation As Integer
    Friend ReadOnly Property Entries As IReadOnlyList(Of ManagerEntry)
    Friend ReadOnly Property IsCut As Boolean

    Friend Sub New(location As ManagerLocation, generation As Integer, entries As IEnumerable(Of ManagerEntry),
                   source As ManagerEndpointLease, isCut As Boolean)
        Me.Location = location
        Me.Generation = generation
        Me.Entries = Array.AsReadOnly(entries.ToArray())
        Me.source = source
        Me.IsCut = isCut
    End Sub

    Friend Function Paste(destination As ManagerEndpointLease, path As String, collision As String) As ManagerCommandRequest
        Return New ManagerCommandRequest(If(IsCut, ManagerCommandKind.Move, ManagerCommandKind.Copy), Location,
                                         Generation, Entries, source.Fork(), destination, path, collisionPolicy:=collision)
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Interlocked.Exchange(source, Nothing)?.Dispose()
    End Sub
End Class
#End If
