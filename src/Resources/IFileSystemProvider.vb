#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Threading

''' <summary>What kind of place a resource is. Local covers UNC paths too: both are
''' openable by path, and one provider serves them (LocalProvider).</summary>
Friend Enum ResourceKind
    Local = 0
    Unc = 1
    Sftp = 2
    Smb = 3
End Enum

''' <summary>How expensive the link behind a provider is - what the prefetch depth and a
''' batch warning are decided from (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md §2).</summary>
Friend Enum ProviderCostHint
    Local = 0
    Fast = 1
    Slow = 2
End Enum

''' <summary>The file verbs a UI may offer, as the refusal point asks about them (PV-7).</summary>
Friend Enum ResourceVerb
    ''' <summary>A recipient slot's copy: reads the source, never changes it.</summary>
    Copy = 0
    ''' <summary>A recipient slot's move: the source goes away afterwards.</summary>
    Move = 1
    Delete = 2
    Rename = 3
    ''' <summary>Writes a new file next to the current one and removes the original
    ''' ("replace with video").</summary>
    WriteBeside = 4
End Enum

''' <summary>
''' What a provider can do, as plain booleans - so that no UI ever asks "is this SFTP?",
''' only "can this resource delete?" (006 §2). Immutable: a provider hands out one instance
''' and nobody can talk it into a verb it does not have.
''' </summary>
Friend NotInheritable Class ProviderCapabilities

    Friend Sub New(Optional canWrite As Boolean = True,
                   Optional canDelete As Boolean = True,
                   Optional canRename As Boolean = True,
                   Optional canCreateFolder As Boolean = True,
                   Optional hasRecycleBin As Boolean = False,
                   Optional needsMaterialisation As Boolean = True,
                   Optional supportsServerSideRename As Boolean = False,
                   Optional isMetered As Boolean = False,
                   Optional costHint As ProviderCostHint = ProviderCostHint.Slow,
                   Optional supportsAtomicOverwriteRename As Boolean = False,
                   Optional supportsStableSourceDelete As Boolean = False,
                   Optional supportsCheckedSourceDelete As Boolean = False)
        Me.CanWrite = canWrite
        Me.CanDelete = canDelete
        Me.CanRename = canRename
        Me.CanCreateFolder = canCreateFolder
        Me.HasRecycleBin = hasRecycleBin
        Me.NeedsMaterialisation = needsMaterialisation
        Me.SupportsServerSideRename = supportsServerSideRename
        Me.IsMetered = isMetered
        Me.CostHint = costHint
        Me.SupportsAtomicOverwriteRename = supportsAtomicOverwriteRename
        Me.SupportsStableSourceDelete = supportsStableSourceDelete
        Me.SupportsCheckedSourceDelete = supportsCheckedSourceDelete
    End Sub

    Friend ReadOnly Property CanWrite As Boolean
    Friend ReadOnly Property CanDelete As Boolean
    Friend ReadOnly Property CanRename As Boolean
    Friend ReadOnly Property CanCreateFolder As Boolean
    ''' <summary>Whether a deletion can be taken back - what DeletePolicy words a deletion
    ''' by (umbrella invariant 6).</summary>
    Friend ReadOnly Property HasRecycleBin As Boolean
    ''' <summary>False when the file is already openable by path, so nothing is ever
    ''' copied. This is the property that keeps the local build exactly as fast as it was.</summary>
    Friend ReadOnly Property NeedsMaterialisation As Boolean
    ''' <summary>A move inside one resource is one command, not download + upload + delete.</summary>
    Friend ReadOnly Property SupportsServerSideRename As Boolean
    Friend ReadOnly Property IsMetered As Boolean
    Friend ReadOnly Property CostHint As ProviderCostHint
    ''' <summary>A rename onto an existing target replaces it in ONE step, so no moment exists with the old
    ''' target gone and the new one absent. When False, a replace goes through an owned backup.</summary>
    Friend ReadOnly Property SupportsAtomicOverwriteRename As Boolean
    ''' <summary>The source can be deleted only if it is still the version that was copied: the provider
    ''' either offers a conditional delete or the file is owned locally, where re-checking the version
    ''' immediately before the delete is what the app has always done. Arbitrary SFTP does NOT have
    ''' this, and is never assumed to. Ordinary SFTP moves use SupportsCheckedSourceDelete instead.</summary>
    Friend ReadOnly Property SupportsStableSourceDelete As Boolean
    ''' <summary>Ordinary move: re-check source metadata, remove, then confirm absence.
    ''' Does not promise an atomic conditional delete (ticket 221).</summary>
    Friend ReadOnly Property SupportsCheckedSourceDelete As Boolean

    ''' <summary>A local disk or a UNC share: every verb, a Recycle Bin, nothing to
    ''' materialise.</summary>
    Friend Shared ReadOnly LocalDisk As New ProviderCapabilities(
        hasRecycleBin:=True, needsMaterialisation:=False,
        supportsServerSideRename:=True, costHint:=ProviderCostHint.Local,
        supportsStableSourceDelete:=True)

End Class

''' <summary>
''' One entry of a listing, shaped like the viewer's private FileEntry so the list builder
''' converts rather than rewrites (006 §2). <see cref="LocalPath"/> is the path the file
''' list carries: the file itself for a local provider, a name in the cache session
''' directory for one that has to materialise.
''' </summary>
Friend NotInheritable Class ResourceEntry
    ''' <summary>The provider's own path - what Download/Rename/DeleteFile are called with.</summary>
    Friend Property FullPath As String = ""
    Friend Property Name As String = ""
    Friend Property Length As Long
    Friend Property LastWriteUtc As Date
    Friend Property IsDirectory As Boolean
    ''' <summary>Where the bytes are, or will be, on this machine.</summary>
    Friend Property LocalPath As String = ""
    ''' <summary>A provider's own evidence of WHICH version of the file this entry describes
    ''' (an etag, a change token). Empty when the provider has none - then size and write time
    ''' are the evidence, see <see cref="SameVersionAs"/>.</summary>
    Friend Property VersionTag As String = ""

    ''' <summary>Do two listings of one path describe the same bytes? Used before a downloaded
    ''' file may replace a cached one: a late download of an OLD version must lose.</summary>
    Friend Function SameVersionAs(other As ResourceEntry) As Boolean
        If other Is Nothing Then Return False
        If VersionTag.Length > 0 OrElse other.VersionTag.Length > 0 Then
            Return String.Equals(VersionTag, other.VersionTag, StringComparison.Ordinal)
        End If
        Return Length = other.Length AndAlso LastWriteUtc = other.LastWriteUtc
    End Function
End Class

''' <summary>
''' What a sorter needs from a place that holds files - deliberately not a file system
''' (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md §2). Paths are the provider's
''' own: Windows paths for LocalProvider, "/dir/name" for a remote one.
''' </summary>
Friend Interface IFileSystemProvider
    ReadOnly Property Kind As ResourceKind
    ReadOnly Property Capabilities As ProviderCapabilities

    ''' <summary>Files (and, for a provider that reports them, folders) under
    ''' <paramref name="dirPath"/>. A folder that is missing or unreadable is an empty
    ''' list, as it always was for the viewer's own scan.</summary>
    Function ListEntries(dirPath As String, ct As CancellationToken) As IReadOnlyList(Of ResourceEntry)
    ' FullPath in a successful stat is the canonical stored spelling. A provider must
    ' resolve aliases/case before returning it, so rename can distinguish the source
    ' itself from a differently-cased destination on a case-sensitive server (085).
    Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean

    ' Bytes in, bytes out. Both cancellable and both report progress: on a slow link a 2 GB
    ' clip is the difference between a UI that explains itself and one that looks hung.
    Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As CancellationToken)
    Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken)

    Sub DeleteFile(remotePath As String)
    Sub Rename(remotePath As String, newRemotePath As String)
    Sub CreateDirectory(remotePath As String)
End Interface
#End If
