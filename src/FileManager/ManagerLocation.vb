#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO

' Where the file manager mode of the F3 panel is standing (257_SPECIFICATION_FILE_MANAGER_NAVIGATION_AND_PROVIDER_CONTEXT.md).
'
' A PLACE is a root the user can browse from (a drive, a share, a configured resource); a LOCATION is a
' place plus one directory under it. Neither ever holds a cache path, a credential or a locator that
' carries one: a resource is named by its registry id, and a path is the provider's own spelling -
' a Windows path for a disk or a share, a POSIX path for a server.
' Modern build only.

''' <summary>A root the file manager can browse from. Immutable.</summary>
Friend NotInheritable Class ManagerPlace

    Friend Sub New(id As String, title As String, kind As ResourceKind, rootPath As String, Optional resourceId As String = "",
                   Optional exchangeEntryId As String = "", Optional exchangeDeviceId As String = "", Optional exchangeResourceId As String = "")
        Me.Id = If(id, "")
        Me.Title = If(title, "")
        Me.Kind = kind
        Me.RootPath = If(rootPath, "")
        Me.ResourceId = If(resourceId, "")
        Me.ExchangeEntryId = If(exchangeEntryId, "")
        Me.ExchangeDeviceId = If(exchangeDeviceId, "")
        Me.ExchangeResourceId = If(exchangeResourceId, "")
    End Sub

    ''' <summary>Stable across refreshes and sessions: "local:" + the drive root for a disk, "res:" + the registry id for a resource.</summary>
    Friend ReadOnly Property Id As String
    Friend ReadOnly Property Title As String
    Friend ReadOnly Property Kind As ResourceKind
    ''' <summary>The boundary Up and the address bar may not cross, in the provider's own spelling.</summary>
    Friend ReadOnly Property RootPath As String
    ''' <summary>The registry id of the resource, or "" for a place that is a plain disk.</summary>
    Friend ReadOnly Property ResourceId As String
    Friend ReadOnly Property ExchangeEntryId As String
    Friend ReadOnly Property ExchangeDeviceId As String
    Friend ReadOnly Property ExchangeResourceId As String
    Friend ReadOnly Property ExchangeIdentity As String
        Get
            Return BindingIdentity(ExchangeEntryId, ExchangeDeviceId, ExchangeResourceId, RootPath)
        End Get
    End Property

    Friend Shared Function BindingIdentity(ParamArray parts As String()) As String
        Dim result As New System.Text.StringBuilder()
        For Each part As String In parts
            Dim value As String = If(part, "")
            result.Append(value.Length.ToString(Globalization.CultureInfo.InvariantCulture)).Append(":"c).Append(value)
        Next
        Return result.ToString()
    End Function

    ''' <summary>True for a place that needs a connection of its own (an SFTP server, an SMB share with a lease).</summary>
    Friend ReadOnly Property NeedsConnection As Boolean
        Get
            Return Kind = ResourceKind.Sftp OrElse Kind = ResourceKind.Smb
        End Get
    End Property

    ''' <summary>Paths of this place are POSIX paths on a server, compared case-sensitively.</summary>
    Friend ReadOnly Property IsRemotePosix As Boolean
        Get
            Return Kind = ResourceKind.Sftp
        End Get
    End Property

    ''' <summary>What a list box shows for the place.</summary>
    Public Overrides Function ToString() As String
        Return Title
    End Function

    Friend Shared Function ForDrive(rootPath As String, title As String) As ManagerPlace
        Dim root As String = If(ManagerPathRules.NormalizeLocal(rootPath), If(rootPath, ""))
        Return New ManagerPlace("local:" & root.ToUpperInvariant(), title, ResourceKind.Local, root)
    End Function

    Friend Shared Function ForResource(descriptor As ResourceDescriptor) As ManagerPlace
        If descriptor Is Nothing Then Throw New ArgumentNullException(NameOf(descriptor))
        Dim root As String = If(descriptor.RootPath, "")
        If descriptor.Kind <> ResourceKind.Sftp Then root = If(ManagerPathRules.NormalizeLocal(root), root)
        Dim title As String = If(String.IsNullOrWhiteSpace(descriptor.DisplayName), root, descriptor.DisplayName)
        Dim id As String = "res:" & descriptor.Id
        If Not String.IsNullOrEmpty(descriptor.ExchangeEntryId) Then id &= ":" & BindingIdentity(descriptor.ExchangeEntryId, descriptor.ExchangeDeviceId, descriptor.ExchangeResourceId, root)
        Return New ManagerPlace(id, title, descriptor.Kind, root, descriptor.Id,
            descriptor.ExchangeEntryId, descriptor.ExchangeDeviceId, descriptor.ExchangeResourceId)
    End Function
End Class

''' <summary>One directory of one place. Immutable; a navigation produces a new one.</summary>
Friend NotInheritable Class ManagerLocation

    Friend Sub New(place As ManagerPlace, directoryPath As String)
        If place Is Nothing Then Throw New ArgumentNullException(NameOf(place))
        Me.Place = place
        Me.DirectoryPath = If(directoryPath, place.RootPath)
    End Sub

    Friend ReadOnly Property Place As ManagerPlace
    ''' <summary>The canonical provider directory.</summary>
    Friend ReadOnly Property DirectoryPath As String

    Friend Shared Function AtRoot(place As ManagerPlace) As ManagerLocation
        Return New ManagerLocation(place, place.RootPath)
    End Function

    Friend Function WithDirectory(path As String) As ManagerLocation
        Return New ManagerLocation(Place, path)
    End Function

    Friend ReadOnly Property IsAtRoot As Boolean
        Get
            Return ManagerPathRules.AreEqual(Place.Kind, DirectoryPath, Place.RootPath)
        End Get
    End Property

    ''' <summary>The parent directory, or Nothing at the place's root: Up stops at the configured root of a
    ''' resource and at the drive or share root of a disk.</summary>
    Friend Function Parent() As ManagerLocation
        If IsAtRoot Then Return Nothing
        Dim up As String = ManagerPathRules.ParentOf(Place.Kind, DirectoryPath)
        If up Is Nothing OrElse Not ManagerPathRules.Contains(Place.Kind, Place.RootPath, up) Then Return Nothing
        Return WithDirectory(up)
    End Function

    Friend Function SameAs(other As ManagerLocation) As Boolean
        Return other IsNot Nothing AndAlso
               Place.Kind = other.Place.Kind AndAlso
               String.Equals(Place.Id, other.Place.Id, StringComparison.Ordinal) AndAlso
               ManagerPathRules.AreEqual(Place.Kind, DirectoryPath, other.DirectoryPath)
    End Function

    Friend Function IsAffectedBy(location As ManagerLocation, includeDescendants As Boolean) As Boolean
        If SameAs(location) Then Return True
        Return includeDescendants AndAlso location IsNot Nothing AndAlso Place.Kind = location.Place.Kind AndAlso
               String.Equals(Place.Id, location.Place.Id, StringComparison.Ordinal) AndAlso
               ManagerPathRules.Contains(Place.Kind, location.DirectoryPath, DirectoryPath)
    End Function
End Class

''' <summary>
''' The path rules of the two path families the manager meets, as pure functions: a Windows path (a disk, a UNC
''' share, an SMB resource) compares case-insensitively; a POSIX path on a server is case-sensitive and is never
''' lowercased (257: "Never lowercase a remote path"). A path that carries a ".." segment is refused outright -
''' lexical traversal is rejected before any provider is asked.
''' </summary>
Friend Module ManagerPathRules

    ''' <summary>The full, separator-trimmed Windows path, or Nothing when it is empty, relative, drive-relative,
    ''' carries a NUL or a ".." segment, or is not a path at all.</summary>
    Friend Function NormalizeLocal(path As String) As String
        If String.IsNullOrWhiteSpace(path) Then Return Nothing
        If path.IndexOf(ChrW(0)) >= 0 Then Return Nothing
        If HasDotDotSegment(path) Then Return Nothing
        Try
            If Not System.IO.Path.IsPathFullyQualified(path) Then Return Nothing
            Dim full As String = System.IO.Path.GetFullPath(path)
            Dim root As String = If(System.IO.Path.GetPathRoot(full), "")
            If full.Length > root.Length Then
                full = full.TrimEnd("\"c, "/"c)
                If full.Length < root.Length Then full = root
            End If
            Return full
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException OrElse TypeOf ex Is PathTooLongException
            Return Nothing
        End Try
    End Function

    Private Function HasDotDotSegment(path As String) As Boolean
        For Each segment As String In path.Split("\"c, "/"c)
            If segment = ".." Then Return True
        Next
        Return False
    End Function

    ''' <summary>The canonical POSIX path, or Nothing (the one definition the resource layer already has).</summary>
    Friend Function NormalizeRemote(path As String) As String
        Return FmsConfigReader.CanonicalVirtualPath(path)
    End Function

    Friend Function Normalize(kind As ResourceKind, path As String) As String
        Return If(kind = ResourceKind.Sftp, NormalizeRemote(path), NormalizeLocal(path))
    End Function

    ''' <summary>The same directory? Case-insensitive for Windows paths, exact for POSIX; two paths that cannot be
    ''' normalised are never equal.</summary>
    Friend Function AreEqual(kind As ResourceKind, a As String, b As String) As Boolean
        Dim na As String = Normalize(kind, a)
        Dim nb As String = Normalize(kind, b)
        If na Is Nothing OrElse nb Is Nothing Then Return False
        Return String.Equals(na, nb, If(kind = ResourceKind.Sftp, StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>Is <paramref name="path"/> the root itself or somewhere under it? Whole segments only: "/MOV2" is not under "/MOV".</summary>
    Friend Function Contains(kind As ResourceKind, rootPath As String, path As String) As Boolean
        Dim root As String = Normalize(kind, rootPath)
        Dim target As String = Normalize(kind, path)
        If root Is Nothing OrElse target Is Nothing Then Return False
        Dim comparison As StringComparison = If(kind = ResourceKind.Sftp, StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase)
        If String.Equals(root, target, comparison) Then Return True
        Dim separator As Char = If(kind = ResourceKind.Sftp, "/"c, "\"c)
        Dim prefix As String = If(root.EndsWith(separator), root, root & separator)
        Return target.StartsWith(prefix, comparison)
    End Function

    ''' <summary>The parent path, or Nothing at the root of the file system.</summary>
    Friend Function ParentOf(kind As ResourceKind, path As String) As String
        Dim normalized As String = Normalize(kind, path)
        If normalized Is Nothing Then Return Nothing
        If kind = ResourceKind.Sftp Then
            If normalized = "/" Then Return Nothing
            Dim slash As Integer = normalized.LastIndexOf("/"c)
            Return If(slash <= 0, "/", normalized.Substring(0, slash))
        End If
        Dim root As String = If(System.IO.Path.GetPathRoot(normalized), "")
        If String.Equals(normalized, root, StringComparison.OrdinalIgnoreCase) Then Return Nothing
        Dim parent As String = System.IO.Path.GetDirectoryName(normalized)
        Return If(parent Is Nothing, Nothing, NormalizeLocal(parent))
    End Function

    ''' <summary>A child of a directory, in the family's own spelling.</summary>
    Friend Function Combine(kind As ResourceKind, directory As String, name As String) As String
        If kind = ResourceKind.Sftp Then
            If String.IsNullOrEmpty(directory) OrElse directory = "/" Then Return "/" & name
            Return directory.TrimEnd("/"c) & "/" & name
        End If
        Return System.IO.Path.Combine(directory, name)
    End Function

    ''' <summary>The final segment of a path in the family's spelling ("" for a root).</summary>
    Friend Function LeafName(kind As ResourceKind, path As String) As String
        If String.IsNullOrEmpty(path) Then Return ""
        Dim trimmed As String = path.TrimEnd("\"c, "/"c)
        Dim cut As Integer = If(kind = ResourceKind.Sftp, trimmed.LastIndexOf("/"c), trimmed.LastIndexOfAny({"\"c, "/"c}))
        Return If(cut >= 0, trimmed.Substring(cut + 1), trimmed)
    End Function

End Module
#End If
