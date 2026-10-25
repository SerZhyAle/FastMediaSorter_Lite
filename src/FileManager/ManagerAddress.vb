#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq

' The two pure questions the file manager's chrome asks before it navigates (257_SPECIFICATION_FILE_MANAGER_NAVIGATION_AND_PROVIDER_CONTEXT.md,
' slice N3): which places can the left list offer, and what does the text typed in the address bar mean. Neither touches a disk, a
' registry or a socket - the resource list arrives as a parameter - so both are unit tests. Typing an address NEVER creates a resource:
' a locator names an existing configured one or it is refused, and nothing here asks for a password or a host-key decision.
' Modern build only.

Friend Enum ManagerAddressKind
    ''' <summary>The text names a location the manager may ask a provider to list.</summary>
    Ok
    ''' <summary>Not a local absolute path and not a locator: a relative path, a drive without a slash, text with a NUL or a ".." segment.</summary>
    Invalid
    ''' <summary>A well-formed locator, but no configured resource answers to it. The address bar does not make one.</summary>
    UnknownResource
    AmbiguousResource
End Enum

Friend NotInheritable Class ManagerAddressResult

    Friend Sub New(kind As ManagerAddressKind, location As ManagerLocation)
        Me.Kind = kind
        Me.Location = location
    End Sub

    Friend ReadOnly Property Kind As ManagerAddressKind
    ''' <summary>Set only when <see cref="Kind"/> is Ok. It may still be refused by the browser (outside the root, missing, denied).</summary>
    Friend ReadOnly Property Location As ManagerLocation
End Class

Friend Module ManagerAddress

    Friend Const Default_Sftp_Port As Integer = 22

    ''' <summary>
    ''' The places the left list shows: every logical drive, then every configured SFTP/SMB resource (a UNC resource is also a
    ''' place, named by its display name). A drive that is also the root of a resource is not listed twice. Order is the order given.
    ''' </summary>
    Friend Function BuildPlaces(driveRoots As IEnumerable(Of String), resources As IEnumerable(Of ResourceDescriptor),
                                Optional showLocal As Boolean = True, Optional showResources As Boolean = True) As List(Of ManagerPlace)
        Dim places As New List(Of ManagerPlace)()
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        If showLocal AndAlso driveRoots IsNot Nothing Then
            For Each root As String In driveRoots
                If String.IsNullOrWhiteSpace(root) Then Continue For
                Dim place As ManagerPlace = ManagerPlace.ForDrive(root, root.TrimEnd("\"c, "/"c))
                If seen.Add(place.Id) Then places.Add(place)
            Next
        End If
        If showResources AndAlso resources IsNot Nothing Then
            For Each descriptor As ResourceDescriptor In resources
                If descriptor Is Nothing OrElse String.IsNullOrEmpty(descriptor.Id) Then Continue For
                If descriptor.Kind <> ResourceKind.Sftp AndAlso descriptor.Kind <> ResourceKind.Smb AndAlso descriptor.Kind <> ResourceKind.Unc Then Continue For
                Dim place As ManagerPlace = ManagerPlace.ForResource(descriptor)
                If place.RootPath.Length = 0 Then Continue For
                If seen.Add(place.Id) Then places.Add(place)
            Next
        End If
        Return places
    End Function

    ''' <summary>
    ''' What the address bar's text means. A local absolute path becomes a location on the configured resource that contains it (an SMB or
    ''' UNC resource), else on the drive or share it sits on. A locator becomes a location on the configured SFTP resource with the same
    ''' user, host and port. Anything else is Invalid, a locator nobody configured is UnknownResource.
    ''' </summary>
    Friend Function Resolve(text As String, resources As IEnumerable(Of ResourceDescriptor)) As ManagerAddressResult
        Dim typed As String = If(text, "").Trim()
        If typed.Length >= 2 AndAlso typed.StartsWith("""", StringComparison.Ordinal) AndAlso typed.EndsWith("""", StringComparison.Ordinal) Then
            typed = typed.Substring(1, typed.Length - 2).Trim()
        End If
        If typed.Length = 0 Then Return New ManagerAddressResult(ManagerAddressKind.Invalid, Nothing)

        Dim list As List(Of ResourceDescriptor) = If(resources, Enumerable.Empty(Of ResourceDescriptor)()).Where(Function(r) r IsNot Nothing).ToList()

        If ResourceLocator.IsLocator(typed) Then
            Dim user As String = ""
            Dim host As String = ""
            Dim port As Integer
            Dim remotePath As String = "/"
            If Not ResourceLocator.TryParse(typed, user, host, port, remotePath) Then
                Return New ManagerAddressResult(ManagerAddressKind.Invalid, Nothing)
            End If
            Dim normalized As String = ManagerPathRules.NormalizeRemote(remotePath)
            If normalized Is Nothing Then Return New ManagerAddressResult(ManagerAddressKind.Invalid, Nothing)

            Dim wanted As Integer = If(port > 0, port, Default_Sftp_Port)
            Dim matches As List(Of ResourceDescriptor) = list.Where(
                Function(r) r.Kind = ResourceKind.Sftp AndAlso
                            String.Equals(r.Host, host, StringComparison.OrdinalIgnoreCase) AndAlso
                            If(r.Port > 0, r.Port, Default_Sftp_Port) = wanted AndAlso
                            (user.Length = 0 OrElse String.Equals(r.UserName, user, StringComparison.Ordinal))).ToList()
            Dim contained As List(Of ResourceDescriptor) = matches.Where(Function(r) ManagerPathRules.Contains(r.Kind, r.RootPath, normalized)).ToList()
            If contained.Count > 0 Then
                Dim longest As Integer = contained.Max(Function(r) r.RootPath.Length)
                matches = contained.Where(Function(r) r.RootPath.Length = longest).ToList()
            End If
            ' A locator carries no account id. Identical addresses belonging to different entries require a place selection.
            If matches.Count > 1 AndAlso matches.Select(Function(r) ManagerPlace.ForResource(r).ExchangeIdentity).Distinct(StringComparer.Ordinal).Count() > 1 Then
                Return New ManagerAddressResult(ManagerAddressKind.AmbiguousResource, Nothing)
            End If
            Dim match As ResourceDescriptor = matches.FirstOrDefault()
            If match Is Nothing Then Return New ManagerAddressResult(ManagerAddressKind.UnknownResource, Nothing)
            Return New ManagerAddressResult(ManagerAddressKind.Ok, New ManagerLocation(ManagerPlace.ForResource(match), normalized))
        End If

        Dim local As String = ManagerPathRules.NormalizeLocal(typed)
        If local Is Nothing Then Return New ManagerAddressResult(ManagerAddressKind.Invalid, Nothing)

        ' The configured resource that owns the path wins over the bare drive/share: it carries the connection (SMB) and the root.
        For Each r As ResourceDescriptor In list
            If r.Kind = ResourceKind.Sftp Then Continue For
            Dim place As ManagerPlace = ManagerPlace.ForResource(r)
            If place.RootPath.Length > 0 AndAlso ManagerPathRules.Contains(place.Kind, place.RootPath, local) Then
                Return New ManagerAddressResult(ManagerAddressKind.Ok, New ManagerLocation(place, local))
            End If
        Next

        Dim root As String = Path.GetPathRoot(local)
        If String.IsNullOrEmpty(root) Then Return New ManagerAddressResult(ManagerAddressKind.Invalid, Nothing)
        Dim drive As ManagerPlace = ManagerPlace.ForDrive(root, root.TrimEnd("\"c, "/"c))
        Return New ManagerAddressResult(ManagerAddressKind.Ok, New ManagerLocation(drive, local))
    End Function

    ''' <summary>
    ''' The place that should be highlighted for a location, among the listed ones: the same id, else the configured resource or drive
    ''' that contains it. Nothing when the list has none (a UNC path Windows reaches by itself).
    ''' </summary>
    Friend Function PlaceFor(location As ManagerLocation, places As IEnumerable(Of ManagerPlace)) As ManagerPlace
        If location Is Nothing OrElse places Is Nothing Then Return Nothing
        Return places.FirstOrDefault(Function(p) String.Equals(p.Id, location.Place.Id, StringComparison.Ordinal))
    End Function
End Module
#End If
