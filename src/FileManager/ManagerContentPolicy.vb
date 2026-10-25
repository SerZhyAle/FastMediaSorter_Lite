#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic

' Content filtering policy for the file manager (258_SPECIFICATION_FILE_MANAGER_CONTENT_SETTINGS_AND_SAFE_OPEN.md, Slice C1).
' Pure decision module: decides which entries appear in the listing given content mode and hidden file settings.
' Modern build only.

Public Enum ManagerContentMode
    Media = 0
    Documents = 1
    All = 2
End Enum

Public Module ManagerContentPolicy

    Private ReadOnly ViewerContainers As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".zip", ".rar", ".7z", ".fd-sec"
    }

    ''' <summary>Converts preference string to typed content mode.</summary>
    Friend Function ParseMode(mode As String) As ManagerContentMode
        If String.Equals(mode, "documents", StringComparison.OrdinalIgnoreCase) Then
            Return ManagerContentMode.Documents
        ElseIf String.Equals(mode, "all", StringComparison.OrdinalIgnoreCase) Then
            Return ManagerContentMode.All
        Else
            Return ManagerContentMode.Media
        End If
    End Function

    ''' <summary>Converts typed content mode to preference string.</summary>
    Friend Function ModeToString(mode As ManagerContentMode) As String
        Select Case mode
            Case ManagerContentMode.Documents : Return "documents"
            Case ManagerContentMode.All : Return "all"
            Case Else : Return "media"
        End Select
    End Function

    ''' <summary>Whether this extension is a container the viewer supports directly.</summary>
    Friend Function IsViewerContainer(extension As String) As Boolean
        If String.IsNullOrEmpty(extension) Then Return False
        If Not extension.StartsWith(".", StringComparison.Ordinal) Then extension = "." & extension
        Return ViewerContainers.Contains(extension)
    End Function

    ''' <summary>Whether this extension or kind is eligible media intake for the viewer.</summary>
    Friend Function IsSupportedMedia(extension As String, kind As MediaKind) As Boolean
        If kind = MediaKind.Image OrElse kind = MediaKind.Video OrElse kind = MediaKind.Audio OrElse kind = MediaKind.Stream Then
            Return True
        End If
        Return IsViewerContainer(extension)
    End Function

    ''' <summary>
    ''' Evaluates whether a manager entry is visible under the given content mode and hidden files switch.
    ''' Folders always participate in navigation. Hidden/system filter is independent.
    ''' Media modes retain MediaBrowsing.IsJunk exclusions; All files is raw unclassified storage listing.
    ''' </summary>
    Friend Function IsVisible(entry As ManagerEntry, mode As ManagerContentMode, showHiddenFiles As Boolean) As Boolean
        If entry Is Nothing Then Return False

        ' Hidden/system switch is an independent filter applying to both folders and files.
        If Not showHiddenFiles AndAlso (entry.IsHidden OrElse entry.IsSystem) Then
            Return False
        End If

        ' Folders always participate in navigation once past the hidden/system filter.
        If entry.IsDirectory Then
            Return True
        End If

        ' All files mode: explicit storage listing showing all files, including junk and raw unclassified entries.
        If mode = ManagerContentMode.All Then
            Return True
        End If

        ' Media and Documents modes exclude junk files under MEDIA-CLASSIFICATION rule 5.
        If MediaBrowsing.IsJunk(entry.Name) Then
            Return False
        End If

        Dim ext As String = MediaKindClassifier.ExtensionOf(entry.Name)
        Dim kind As MediaKind = MediaKindClassifier.KindOf(entry.Name)

        ' Supported media (Image, Video, Audio, Stream, and viewer-supported containers).
        If IsSupportedMedia(ext, kind) Then
            Return True
        End If

        ' Media + documents adds both Document and Book (so PDF and office documents are included).
        If mode = ManagerContentMode.Documents Then
            If kind = MediaKind.Document OrElse kind = MediaKind.Book Then
                Return True
            End If
        End If

        Return False
    End Function

    ''' <summary>Filters an enumeration of manager entries according to the policy.</summary>
    Friend Function Filter(entries As IEnumerable(Of ManagerEntry), mode As ManagerContentMode, showHiddenFiles As Boolean) As List(Of ManagerEntry)
        Dim result As New List(Of ManagerEntry)()
        If entries Is Nothing Then Return result
        For Each entry As ManagerEntry In entries
            If IsVisible(entry, mode, showHiddenFiles) Then
                result.Add(entry)
            End If
        Next
        Return result
    End Function

End Module
#End If
