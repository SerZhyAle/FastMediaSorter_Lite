#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Text.Json

''' <summary>
''' The mirror of the local SFTP server's share list - the worker behind the Share Manager
''' (src/FastMediaSorterCompanion). Every folder the server serves appears in the viewer's
''' resource list, but the viewer owns none of it: a mirror row is never persisted here,
''' never edited and never removed - the Share Manager owns the list, so changing or
''' removing a share is a handover to that app, not a dialog here.
'''
''' Two registries are mirrored, from the worker's own data dir:
'''   - shares.json - the folder shares ([]sftpserver.Root: name, hostPath, readOnly);
'''   - diskshares.json - the FileDO disks ({version:1, disks:[..]}), a disk served under
'''     its rootName from its mount path while it is open, and from the worker's own
'''     "unavailable" path while it is not - the mirror shows the empty mount path then,
'''     and open/test honestly say the folder is not on the disk.
'''
''' A served share is a folder of this machine, so its row opens the plain local way - no
''' credential, no loopback connection, and it works while the server is not running. The
''' share's read-only rule stays the server's own business (its phone side).
'''
''' Best effort by contract: a missing or unreadable file means "no shares", never an error.
''' </summary>
Friend Module WorkerShares

    ''' <summary>What a mirror row's Id starts with. Kind=Local alone would also cover a
    ''' plain folder the viewer happens to hold, so the prefix is the management boundary's
    ''' test - the one question every surface asks before it treats a row as the server's.
    ''' One prefix per registry: the two lists name their entries in separate namespaces,
    ''' and the panel names the kind of thing a row is by it.</summary>
    Friend Const ManagedIdPrefix As String = "shareserver:"
    Friend Const ManagedDiskIdPrefix As String = "filedo:"

    Private Const SharesFileName As String = "shares.json"
    Private Const DisksFileName As String = "diskshares.json"

    ''' <summary>Is this row the server's mirror rather than a resource the user set up? Pure.</summary>
    Friend Function IsManaged(resource As ResourceDescriptor) As Boolean
        Return IsManagedFolder(resource) OrElse IsManagedDisk(resource)
    End Function

    Friend Function IsManagedFolder(resource As ResourceDescriptor) As Boolean
        Return resource IsNot Nothing AndAlso resource.Kind = ResourceKind.Local AndAlso
               If(resource.Id, "").StartsWith(ManagedIdPrefix, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>A FileDO disk: its own prefix, so a folder share and a disk never collide
    ''' even when they carry the same name (the worker's virtual root set would refuse
    ''' that name pair anyway).</summary>
    Friend Function IsManagedDisk(resource As ResourceDescriptor) As Boolean
        Return resource IsNot Nothing AndAlso resource.Kind = ResourceKind.Local AndAlso
               If(resource.Id, "").StartsWith(ManagedDiskIdPrefix, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>The registered resources first, the server's shares behind them. Duplicate
    ''' mirror names collapse (first wins) and a registered resource always beats a mirror
    ''' with the same id, so the user's own list is never crowded out. Pure.</summary>
    Friend Function Combined(registered As IEnumerable(Of ResourceDescriptor),
                             managed As IEnumerable(Of ResourceDescriptor)) As List(Of ResourceDescriptor)
        Dim result As New List(Of ResourceDescriptor)()
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each source As IEnumerable(Of ResourceDescriptor) In {registered, managed}
            If source Is Nothing Then Continue For
            For Each resource As ResourceDescriptor In source
                If resource Is Nothing OrElse Not seen.Add(resource.Id) Then Continue For
                result.Add(resource)
            Next
        Next
        Return result
    End Function

    ''' <summary>The registered list plus the machine's real share list - what the viewer's
    ''' resource surfaces read when they want "every resource this viewer can open".</summary>
    Friend Function Catalog() As List(Of ResourceDescriptor)
        Return Combined(ResourceRegistry.Load(), Load())
    End Function

    ''' <summary>The shares served from the worker's data dir, per its own resolution order.
    ''' Best effort, never throws.</summary>
    Friend Function Load() As List(Of ResourceDescriptor)
        Dim directoryPath As String = ""
        Try
            directoryPath = DataDir()
        Catch
        End Try
        If directoryPath.Length = 0 Then Return New List(Of ResourceDescriptor)()
        Return Load(directoryPath, Function(path As String) File.ReadAllText(path))
    End Function

    ''' <summary>The injectable half: the tests name the directory and the reader, so the
    ''' shipped loader and the tests drive the same parse. The folder shares come first,
    ''' the disks behind them; each registry is read on its own, so a missing one is
    ''' simply no rows of its kind.</summary>
    Friend Function Load(directoryPath As String, readFile As Func(Of String, String)) As List(Of ResourceDescriptor)
        If String.IsNullOrEmpty(directoryPath) OrElse readFile Is Nothing Then Return New List(Of ResourceDescriptor)()
        Dim shares As String = ""
        Try
            shares = readFile(Path.Combine(directoryPath, SharesFileName))
        Catch
        End Try
        Dim disks As String = ""
        Try
            disks = readFile(Path.Combine(directoryPath, DisksFileName))
        Catch
        End Try
        Return Combined(Parse(shares), ParseDisks(disks))
    End Function

    ''' <summary>The worker's share list: one mirror row per served root, in file order.
    ''' A malformed element is skipped; a malformed file shows nothing - half a list with
    ''' invented names would be worse than none. Pure.</summary>
    Friend Function Parse(json As String) As List(Of ResourceDescriptor)
        Dim result As New List(Of ResourceDescriptor)()
        If String.IsNullOrWhiteSpace(json) Then Return result
        Try
            Using document As JsonDocument = JsonDocument.Parse(json)
                If document.RootElement.ValueKind <> JsonValueKind.Array Then Return result
                Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                For Each element As JsonElement In document.RootElement.EnumerateArray()
                    Dim share As ResourceDescriptor = FromElement(element)
                    If share Is Nothing OrElse Not seen.Add(share.Id) Then Continue For
                    result.Add(share)
                Next
            End Using
        Catch
            ' A half-written or foreign file is "no shares".
        End Try
        Return result
    End Function

    ''' <summary>The FileDO disk registry: one mirror row per shared disk, in file order.
    ''' Version 1 only - the worker refuses any other version outright, and so does the
    ''' mirror. A record without its identity (rootName, containerId) is ignored the way
    ''' the worker ignores it on load. An unmounted disk carries an empty mount path and
    ''' still appears: it is a resource the server distributes, whose folder is not here
    ''' right now. A malformed file shows nothing - half a list with invented names would
    ''' be worse than none. Pure.</summary>
    Friend Function ParseDisks(json As String) As List(Of ResourceDescriptor)
        Dim result As New List(Of ResourceDescriptor)()
        If String.IsNullOrWhiteSpace(json) Then Return result
        Try
            Using document As JsonDocument = JsonDocument.Parse(json)
                Dim root As JsonElement = document.RootElement
                Dim version As JsonElement
                If root.ValueKind <> JsonValueKind.Object OrElse
                   Not TryField(root, "version", version) OrElse version.ValueKind <> JsonValueKind.Number OrElse
                   version.GetInt32() <> 1 Then Return result
                Dim disks As JsonElement
                If Not TryField(root, "disks", disks) OrElse disks.ValueKind <> JsonValueKind.Array Then Return result
                Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                For Each element As JsonElement In disks.EnumerateArray()
                    Dim disk As ResourceDescriptor = FromDiskElement(element)
                    If disk Is Nothing OrElse Not seen.Add(disk.Id) Then Continue For
                    result.Add(disk)
                Next
            End Using
        Catch
            ' A half-written or foreign file is "no disks".
        End Try
        Return result
    End Function

    Private Function FromDiskElement(element As JsonElement) As ResourceDescriptor
        If element.ValueKind <> JsonValueKind.Object Then Return Nothing
        Dim name As String = TextField(element, "rootName")
        Dim containerId As String = TextField(element, "containerId")
        If name.Length = 0 OrElse containerId.Length = 0 Then Return Nothing
        Dim mountPath As String = TextField(element, "mountPath")
        Dim readOnlyDisk As Boolean = False
        Dim flag As JsonElement
        If TryField(element, "readOnly", flag) AndAlso flag.ValueKind = JsonValueKind.True Then readOnlyDisk = True
        Return New ResourceDescriptor With {
            .Id = ManagedDiskIdPrefix & name, .DisplayName = name, .Kind = ResourceKind.Local,
            .RootPath = mountPath, .ReadOnlyHint = readOnlyDisk}
    End Function

    ''' <summary>A registry property by name, case-insensitively. The worker's own two
    ''' registries disagree on casing: the folder-share file marshals Go's field names
    ''' ("Name", "HostPath" - the struct carries no tags), the disk registry carries
    ''' explicit lowercase tags. Both are the worker's voice.</summary>
    Private Function TryField(element As JsonElement, propertyName As String, ByRef found As JsonElement) As Boolean
        If element.ValueKind <> JsonValueKind.Object Then Return False
        If element.TryGetProperty(propertyName, found) Then Return True
        For Each candidate As JsonProperty In element.EnumerateObject()
            If String.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase) Then
                found = candidate.Value
                Return True
            End If
        Next
        Return False
    End Function

    Private Function FromElement(element As JsonElement) As ResourceDescriptor
        If element.ValueKind <> JsonValueKind.Object Then Return Nothing
        Dim name As String = TextField(element, "name")
        Dim hostPath As String = TextField(element, "hostPath")
        If name.Length = 0 OrElse hostPath.Length = 0 Then Return Nothing
        Dim readOnlyShare As Boolean = False
        Dim flag As JsonElement
        If TryField(element, "readOnly", flag) AndAlso flag.ValueKind = JsonValueKind.True Then readOnlyShare = True
        Return New ResourceDescriptor With {
            .Id = ManagedIdPrefix & name, .DisplayName = name, .Kind = ResourceKind.Local,
            .RootPath = hostPath, .ReadOnlyHint = readOnlyShare}
    End Function

    ''' <summary>How a managed folder sits on this machine: there, not there, or behind a
    ''' refusal - a service-held FileDO disk is mounted with a private DACL (FileDO's own
    ''' rule: SYSTEM only), so "cannot see it" and "is not there" are different sentences.</summary>
    Friend Enum FolderPresence
        Present
        Missing
        Denied
    End Enum

    ''' <summary>The honest three-way answer about a managed folder. Pure-ish: one probe.</summary>
    Friend Function ClassifyFolder(path As String) As FolderPresence
        If String.IsNullOrEmpty(path) Then Return FolderPresence.Missing
        Try
            If Directory.Exists(path) Then Return FolderPresence.Present
            File.GetAttributes(path)
            Return FolderPresence.Missing
        Catch ex As UnauthorizedAccessException
            Return FolderPresence.Denied
        Catch ex As System.ComponentModel.Win32Exception
            Return FolderPresence.Denied
        Catch
            Return FolderPresence.Missing
        End Try
    End Function

    Private Function TextField(element As JsonElement, propertyName As String) As String
        Dim value As JsonElement
        If Not TryField(element, propertyName, value) OrElse value.ValueKind <> JsonValueKind.String Then Return ""
        Return value.GetString()
    End Function

    ''' <summary>Where the worker keeps its files - the ONE host-side resolution
    ''' (<see cref="WorkerStateDir.Resolve"/>, the worker's own order: the explicit override, then a
    ''' machine data dir this account can read, then the user's). Empty when the chosen store is not
    ''' there to read: an override is returned as named, but a per-user directory that was never
    ''' created is "no store yet", as it always was.</summary>
    Friend Function DataDir() As String
        Dim chosen As String = WorkerStateDir.Resolve()
        If chosen.Length = 0 Then Return ""
        If WorkerStateDir.ExplicitOverride().Length > 0 OrElse Directory.Exists(chosen) Then Return chosen
        Return ""
    End Function

End Module
#End If
