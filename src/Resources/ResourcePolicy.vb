#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.Linq

''' <summary>
''' Immutable policy object for a server profile (100 §15.4 item 2).
''' Groups endpoint candidates, username, secret handle reference (DPAPI) and host key pin.
''' Never holds password/PIN plaintext.
''' </summary>
Friend NotInheritable Class ResourceServerProfile

    Friend Sub New(profileId As String,
                   userName As String,
                   endpoints As IEnumerable(Of FmsEndpoint),
                   Optional secretHandle As String = "",
                   Optional pinnedFingerprint As String = "",
                   Optional accessNote As String = "")
        If String.IsNullOrWhiteSpace(profileId) Then Throw New ArgumentException("ProfileId must not be empty.", NameOf(profileId))
        Me.ProfileId = profileId
        Me.UserName = If(userName, "")
        Me.Endpoints = New ReadOnlyCollection(Of FmsEndpoint)(If(endpoints, Enumerable.Empty(Of FmsEndpoint)()).Where(Function(e) e IsNot Nothing).ToList())
        Me.SecretHandle = If(secretHandle, "")
        Me.PinnedFingerprint = If(pinnedFingerprint, "")
        Me.AccessNote = If(accessNote, "")
    End Sub

    Friend ReadOnly Property ProfileId As String
    Friend ReadOnly Property UserName As String
    Friend ReadOnly Property Endpoints As IReadOnlyList(Of FmsEndpoint)
    ''' <summary>References a DPAPI-encrypted record in IResourceSecretStore; never plaintext.</summary>
    Friend ReadOnly Property SecretHandle As String
    ''' <summary>Canonical OpenSSH host key fingerprint ("SHA256:..."), or "" for unpinned.</summary>
    Friend ReadOnly Property PinnedFingerprint As String
    Friend ReadOnly Property AccessNote As String

    Friend ReadOnly Property IsPinned As Boolean
        Get
            Return PinnedFingerprint.Length > 0
        End Get
    End Property

    ''' <summary>Matches server identity by host (case-insensitive), port, and username.</summary>
    Friend Function MatchesEndpoint(host As String, port As Integer, userName As String) As Boolean
        If Not String.Equals(Me.UserName, If(userName, ""), StringComparison.Ordinal) Then Return False
        For Each ep In Endpoints
            If ep.Port = port AndAlso String.Equals(ep.Host, host, StringComparison.OrdinalIgnoreCase) Then
                Return True
            End If
        Next
        Return False
    End Function

End Class

''' <summary>
''' Immutable policy object for a configured resource root (100 §15.4 item 2).
''' Root: stable GUID, server-profile ID, canonical virtual root, effective policy, per-resource v2 preferences.
''' Capability flags are derived, not persisted as authority.
''' Default untrusted imported roots are read-only unless the catalog OR rule permits writes.
''' </summary>
Friend NotInheritable Class ResourceRootPolicy

    Friend Sub New(rootId As String,
                   serverProfileId As String,
                   virtualPath As String,
                   Optional label As String = "",
                   Optional readOnlyRaw As Boolean = True,
                   Optional isDestination As Boolean = False,
                   Optional profile As String = "",
                   Optional mediaTypes As IEnumerable(Of String) = Nothing,
                   Optional scanSubdirectories As Boolean = True,
                   Optional showSubfoldersAsItems As Boolean = False,
                   Optional showHiddenFiles As Boolean = False,
                   Optional allFiles As Boolean = False,
                   Optional destinationColor As Integer? = Nothing,
                   Optional comment As String = "",
                   Optional slideshowIntervalSeconds As Integer? = Nothing,
                   Optional hasPin As Boolean = False)
        If String.IsNullOrWhiteSpace(rootId) Then Throw New ArgumentException("RootId must not be empty.", NameOf(rootId))
        If String.IsNullOrWhiteSpace(serverProfileId) Then Throw New ArgumentException("ServerProfileId must not be empty.", NameOf(serverProfileId))
        Dim canonical As String = FmsConfigReader.CanonicalVirtualPath(virtualPath)
        If canonical Is Nothing Then Throw New ArgumentException("VirtualPath must be a valid canonical POSIX path.", NameOf(virtualPath))

        Me.RootId = rootId
        Me.ServerProfileId = serverProfileId
        Me.VirtualPath = canonical
        Me.Label = If(label, "")
        Me.ReadOnlyRaw = readOnlyRaw
        Me.IsDestination = isDestination
        Me.Profile = If(profile, "")
        Me.MediaTypes = New ReadOnlyCollection(Of String)(If(mediaTypes, Enumerable.Empty(Of String)()).Where(Function(s) Not String.IsNullOrEmpty(s)).ToList())
        Me.ScanSubdirectories = scanSubdirectories
        Me.ShowSubfoldersAsItems = showSubfoldersAsItems
        Me.ShowHiddenFiles = showHiddenFiles
        Me.AllFiles = allFiles
        Me.DestinationColor = destinationColor
        Me.Comment = If(comment, "")
        Me.SlideshowIntervalSeconds = If(slideshowIntervalSeconds.HasValue AndAlso slideshowIntervalSeconds.Value > 0, slideshowIntervalSeconds, Nothing)
        Me.HasPin = hasPin
    End Sub

    Friend ReadOnly Property RootId As String
    Friend ReadOnly Property ServerProfileId As String
    ''' <summary>Canonical POSIX path (e.g. "/Photos", "/"). Case-sensitive.</summary>
    Friend ReadOnly Property VirtualPath As String
    Friend ReadOnly Property Label As String
    Friend ReadOnly Property ReadOnlyRaw As Boolean
    Friend ReadOnly Property IsDestination As Boolean

    ''' <summary>Writability rule: (!readOnly) || isDestination. Catalog amendment A.</summary>
    Friend ReadOnly Property EffectiveWritable As Boolean
        Get
            Return (Not ReadOnlyRaw) OrElse IsDestination
        End Get
    End Property

    Friend ReadOnly Property Profile As String
    Friend ReadOnly Property MediaTypes As IReadOnlyList(Of String)
    Friend ReadOnly Property ScanSubdirectories As Boolean
    Friend ReadOnly Property ShowSubfoldersAsItems As Boolean
    Friend ReadOnly Property ShowHiddenFiles As Boolean
    Friend ReadOnly Property AllFiles As Boolean
    Friend ReadOnly Property DestinationColor As Integer?
    Friend ReadOnly Property Comment As String
    Friend ReadOnly Property SlideshowIntervalSeconds As Integer?
    ''' <summary>True if the root carried an accessPin (unsupported PIN lock).</summary>
    Friend ReadOnly Property HasPin As Boolean

    ''' <summary>
    ''' Derives effective runtime ProviderCapabilities from the root's policy and base provider capabilities.
    ''' An unsupported PIN or non-writable policy disallows write/delete/rename operations.
    ''' </summary>
    Friend Function DeriveCapabilities(baseCapabilities As ProviderCapabilities) As ProviderCapabilities
        Dim baseCaps As ProviderCapabilities = If(baseCapabilities, New ProviderCapabilities())
        If HasPin Then
            ' Locked root: cannot write, delete, rename or create folder
            Return New ProviderCapabilities(canWrite:=False, canDelete:=False, canRename:=False, canCreateFolder:=False,
                                            hasRecycleBin:=baseCaps.HasRecycleBin,
                                            needsMaterialisation:=baseCaps.NeedsMaterialisation,
                                            supportsServerSideRename:=False,
                                            isMetered:=baseCaps.IsMetered,
                                            costHint:=baseCaps.CostHint,
                                            supportsAtomicOverwriteRename:=False,
                                            supportsStableSourceDelete:=False)
        End If

        Dim writable As Boolean = EffectiveWritable AndAlso baseCaps.CanWrite
        Dim deletable As Boolean = EffectiveWritable AndAlso baseCaps.CanDelete
        Dim renamable As Boolean = EffectiveWritable AndAlso baseCaps.CanRename
        Dim creatable As Boolean = EffectiveWritable AndAlso baseCaps.CanCreateFolder

        Return New ProviderCapabilities(canWrite:=writable,
                                        canDelete:=deletable,
                                        canRename:=renamable,
                                        canCreateFolder:=creatable,
                                        hasRecycleBin:=baseCaps.HasRecycleBin,
                                        needsMaterialisation:=baseCaps.NeedsMaterialisation,
                                        supportsServerSideRename:=renamable AndAlso baseCaps.SupportsServerSideRename,
                                        isMetered:=baseCaps.IsMetered,
                                        costHint:=baseCaps.CostHint,
                                        supportsAtomicOverwriteRename:=writable AndAlso baseCaps.SupportsAtomicOverwriteRename,
                                        supportsStableSourceDelete:=deletable AndAlso baseCaps.SupportsStableSourceDelete)
    End Function

End Class

''' <summary>
''' Matching and preview resolution helpers for server profiles and roots (100 §15.4 item 5).
''' Matches using server identity + exact virtual root, not display name or mutable IP alone.
''' </summary>
Friend Module ResourceMatching

    ''' <summary>
    ''' Finds an existing descriptor matching a server identity and exact virtual path (or UNC path).
    ''' Remote paths are matched case-sensitively; hosts case-insensitively; user names case-sensitively; UNC paths case-insensitively.
    ''' </summary>
    Friend Function FindMatchingDescriptor(existing As IEnumerable(Of ResourceDescriptor),
                                           host As String,
                                           port As Integer,
                                           userName As String,
                                           virtualPathOrUnc As String) As ResourceDescriptor
        If existing Is Nothing OrElse String.IsNullOrEmpty(virtualPathOrUnc) Then Return Nothing

        For Each d In existing
            If d Is Nothing Then Continue For
            If d.Kind = ResourceKind.Sftp Then
                Dim canonicalVirtual As String = FmsConfigReader.CanonicalVirtualPath(virtualPathOrUnc)
                If canonicalVirtual IsNot Nothing AndAlso d.Port = port AndAlso
                   String.Equals(d.Host, host, StringComparison.OrdinalIgnoreCase) AndAlso
                   String.Equals(d.UserName, If(userName, ""), StringComparison.Ordinal) Then
                    Dim dPath As String = FmsConfigReader.CanonicalVirtualPath(d.RootPath)
                    If dPath IsNot Nothing AndAlso String.Equals(dPath, canonicalVirtual, StringComparison.Ordinal) Then
                        Return d
                    End If
                End If
            ElseIf d.Kind = ResourceKind.Unc Then
                If String.Equals(d.RootPath, virtualPathOrUnc, StringComparison.OrdinalIgnoreCase) Then
                    Return d
                End If
            End If
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' The saved resource an ADDRESS belongs to (a typed or restored <c>sftp://user@host:port/path</c>): the same
    ''' server through any of its addresses, the same port, the user if the address names one - and, among several
    ''' roots of one server, the deepest one the path is under, or - when the path is outside every saved root -
    ''' the first of that server's resources (same server, same user: the same credential, opened at the path
    ''' given). Nothing when no saved resource is that server: the address is then offered to the user as a NEW
    ''' resource, because a typed address cannot carry the password.
    ''' </summary>
    Friend Function FindForAddress(resources As IEnumerable(Of ResourceDescriptor), host As String, port As Integer,
                                   user As String, remotePath As String) As ResourceDescriptor
        If resources Is Nothing Then Return Nothing
        Dim wantedPort As Integer = If(port > 0, port, 22)
        Dim candidates As New List(Of ResourceDescriptor)()
        For Each r As ResourceDescriptor In resources
            If r Is Nothing OrElse r.Kind <> ResourceKind.Sftp Then Continue For
            If Not String.IsNullOrEmpty(user) AndAlso Not String.Equals(r.UserName, user, StringComparison.Ordinal) Then Continue For
            For Each e As FmsEndpoint In ResourceConnect.CandidatesOf(r)
                If String.Equals(e.Host, host, StringComparison.OrdinalIgnoreCase) AndAlso e.Port = wantedPort Then
                    candidates.Add(r)
                    Exit For
                End If
            Next
        Next
        If candidates.Count = 0 Then Return Nothing
        Dim path As String = If(FmsConfigReader.CanonicalVirtualPath(remotePath), "/")
        Dim best As ResourceDescriptor = Nothing
        Dim bestLength As Integer = -1
        For Each r As ResourceDescriptor In candidates
            Dim root As String = If(FmsConfigReader.CanonicalVirtualPath(r.RootPath), "/")
            If IsUnder(path, root) AndAlso root.Length > bestLength Then
                best = r
                bestLength = root.Length
            End If
        Next
        Return If(best, candidates(0))
    End Function

    Private Function IsUnder(path As String, root As String) As Boolean
        If root = "/" Then Return True
        Return String.Equals(path, root, StringComparison.Ordinal) OrElse path.StartsWith(root & "/", StringComparison.Ordinal)
    End Function
End Module
''' <summary>The preflight warning for a large batch over a slow or metered resource (007 §4.3).
''' Uses the final plan's bytes, after collision skips, and never probes or connects.</summary>
Friend Module RemoteBatchPolicy
    Friend Const WarningThresholdBytes As Long = 1024L * 1024L * 1024L

    Friend Function NeedsWarning(totalBytes As Long, source As ProviderCapabilities,
                                 destination As ProviderCapabilities) As Boolean
        Return totalBytes > WarningThresholdBytes AndAlso (IsCostly(source) OrElse IsCostly(destination))
    End Function

    Private Function IsCostly(capabilities As ProviderCapabilities) As Boolean
        Return capabilities IsNot Nothing AndAlso
               (capabilities.IsMetered OrElse capabilities.CostHint = ProviderCostHint.Slow)
    End Function
End Module
#End If
