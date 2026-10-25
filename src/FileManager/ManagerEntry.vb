#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' One row of a directory in the file manager mode of the F3 panel (257). Immutable: a listing is published whole and
' never edited, so a view that holds an entry holds a fact about one moment. Modern build only.

''' <summary>
''' A file or folder as a provider reported it. The <see cref="Id"/> is opaque and exact - the place's id plus the
''' provider's own spelling of the path - so two remote names that differ only by case are two entries and the
''' selection can tell them apart. It never names a cache path or carries a credential.
''' </summary>
Friend NotInheritable Class ManagerEntry

    Friend Sub New(id As String, name As String, providerPath As String, isDirectory As Boolean,
                   isLink As Boolean, isHidden As Boolean, isSystem As Boolean,
                   sizeBytes As Long, modifiedUtc As DateTime, diskPath As String, Optional versionTag As String = "")
        Me.Id = If(id, "")
        Me.Name = If(name, "")
        Me.ProviderPath = If(providerPath, "")
        Me.IsDirectory = isDirectory
        Me.IsLink = isLink
        Me.IsHidden = isHidden
        Me.IsSystem = isSystem
        ' A folder has no size of its own: unknown, not a zero that reads as "0 bytes of content".
        Me.SizeBytes = If(isDirectory, -1L, If(sizeBytes < 0, -1L, sizeBytes))
        Me.ModifiedUtc = modifiedUtc
        Me.DiskPath = If(diskPath, "")
        Me.VersionTag = If(versionTag, "")
    End Sub

    Friend ReadOnly Property Id As String
    Friend ReadOnly Property Name As String
    ''' <summary>The provider's own path: a Windows path for a disk or a share, a POSIX path for a server.</summary>
    Friend ReadOnly Property ProviderPath As String
    Friend ReadOnly Property IsDirectory As Boolean
    ''' <summary>A symlink or reparse point: shown as such, never entered or followed (257).</summary>
    Friend ReadOnly Property IsLink As Boolean
    Friend ReadOnly Property IsHidden As Boolean
    Friend ReadOnly Property IsSystem As Boolean
    ''' <summary>-1 when unknown, and always -1 for a folder.</summary>
    Friend ReadOnly Property SizeBytes As Long
    Friend ReadOnly Property ModifiedUtc As DateTime
    Friend ReadOnly Property VersionTag As String
    ''' <summary>Where the bytes are on THIS machine: the file itself for a disk or a share, "" for a server entry (nothing is
    ''' downloaded to browse; the open in 258 materialises it).</summary>
    Friend ReadOnly Property DiskPath As String

    Friend ReadOnly Property SizeKnown As Boolean
        Get
            Return SizeBytes >= 0
        End Get
    End Property

    Friend ReadOnly Property DateKnown As Boolean
        Get
            Return ModifiedUtc > New DateTime(1700, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        End Get
    End Property

    ''' <summary>A folder that Enter, a double click and Up/Back may go into: not a link.</summary>
    Friend ReadOnly Property CanEnter As Boolean
        Get
            Return IsDirectory AndAlso Not IsLink
        End Get
    End Property

    Friend Shared Function IdOf(place As ManagerPlace, providerPath As String) As String
        Return place.Id & "|" & providerPath
    End Function

    Friend Shared Function FromResource(place As ManagerPlace, entry As ResourceEntry) As ManagerEntry
        If place Is Nothing Then Throw New ArgumentNullException(NameOf(place))
        If entry Is Nothing Then Throw New ArgumentNullException(NameOf(entry))
        Dim name As String = If(String.IsNullOrEmpty(entry.Name), ManagerPathRules.LeafName(place.Kind, entry.FullPath), entry.Name)
        Return New ManagerEntry(IdOf(place, entry.FullPath), name, entry.FullPath, entry.IsDirectory,
                                entry.IsLink, entry.IsHidden, entry.IsSystem,
                                entry.Length, entry.LastWriteUtc,
                                If(place.Kind = ResourceKind.Sftp, "", entry.FullPath), entry.VersionTag)
    End Function

    ''' <summary>The row both panel renderers draw. Selection and the thumbnail memory key it by <see cref="Id"/>;
    ''' only a decodable media FILE with a disk path is ever handed to the thumbnail loader.</summary>
    Friend Function ToRow() As PanelFileRow
        Dim kind As MediaKind = MediaKind.Other
        If Not IsDirectory Then
            Try
                kind = MediaKindClassifier.KindOf(Name)
            Catch ex As ArgumentException
                kind = MediaKind.Other
            End Try
        End If
        Return New PanelFileRow With {
            .EntryId = Id,
            .DisplayName = Name,
            .Path = DiskPath,
            .Kind = kind,
            .SizeBytes = SizeBytes,
            .Modified = If(DateKnown, ModifiedUtc.ToLocalTime(), DateTime.MinValue),
            .IsDirectory = IsDirectory,
            .IsLink = IsLink,
            .IsHidden = IsHidden,
            .IsSystem = IsSystem,
            .AllowThumbnail = Not IsDirectory AndAlso DiskPath.Length > 0 AndAlso
                              (kind = MediaKind.Image OrElse kind = MediaKind.Video)}
    End Function

    ''' <summary>The rows for a whole listing, in the order the provider gave them.</summary>
    Friend Shared Function RowsOf(entries As IEnumerable(Of ManagerEntry)) As List(Of PanelFileRow)
        Dim rows As New List(Of PanelFileRow)()
        If entries Is Nothing Then Return rows
        For Each entry As ManagerEntry In entries
            rows.Add(entry.ToRow())
        Next
        Return rows
    End Function
End Class
#End If
