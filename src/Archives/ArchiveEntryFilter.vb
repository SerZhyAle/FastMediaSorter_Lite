#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO

''' <summary>
''' Which entries of an archive become files in the list
''' (010_SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md §3.3, §6.3).
'''
''' Pure, and separate from <see cref="ArchiveSession"/> on purpose: it decides what the
''' user sees AND what is allowed to touch the disk, and both answers are worth pinning
''' down without an archive library in the room. Invariant 3 lives here - only media
''' entries are ever extracted, so an executable inside a comic archive is not written
''' out even temporarily.
''' </summary>
Friend Module ArchiveEntryFilter

    ''' <summary>
    ''' Containers we can read. A nested archive is deliberately NOT offered as an entry
    ''' (§1): stepping into a zip inside a zip is a different feature, and showing it as a
    ''' file the arrow keys can land on would be a dead end.
    ''' </summary>
    Friend ReadOnly Archive_Extensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".zip", ".cbz", ".7z", ".rar", ".cbr"}

    ''' <summary>
    ''' What the viewer opens as a folder. Phase Ф2 added 7z/RAR/CBR: a solid one is read in a
    ''' single sequential pass (<see cref="ArchiveSession.RunSequentialPass"/>) instead of one
    ''' slow seek per picture, so the whole set of containers it recognizes is now openable.
    ''' </summary>
    Friend ReadOnly Openable_Extensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".zip", ".cbz", ".7z", ".rar", ".cbr"}

    ''' <summary>Is this path an archive the viewer knows how to open at all?</summary>
    Friend Function IsArchivePath(filePath As String) As Boolean
        If String.IsNullOrEmpty(filePath) Then Return False
        Return Openable_Extensions.Contains(Path.GetExtension(filePath))
    End Function

    ''' <summary>
    ''' Should this entry appear in the list (and therefore be extractable)?
    '''
    ''' <paramref name="isSupportedMedia"/> is asked of the caller rather than computed
    ''' here, because the answer belongs to the viewer's own extension set - including the
    ''' user's narrowing of it - and duplicating that here would be a second, quietly
    ''' different idea of what the app can open.
    ''' </summary>
    Friend Function IsEligible(entryName As String, isDirectory As Boolean, size As Long,
                               isSupportedMedia As Boolean) As Boolean
        If isDirectory Then Return False
        If String.IsNullOrEmpty(entryName) Then Return False
        ' A zero-byte entry is a placeholder or a truncated write - never a picture, and
        ' the decoder would only fail on it later, one flip further from the cause.
        If size <= 0 Then Return False

        If MediaBrowsing.IsJunk(entryName) Then Return False

        Dim leaf As String = LastSegmentOf(entryName)
        If leaf.Length = 0 Then Return False

        If Archive_Extensions.Contains(ExtensionOf(leaf)) Then Return False
        Return isSupportedMedia
    End Function

    ''' <summary>
    ''' The entry's own name without its folders - what the "file name" label shows the
    ''' user, and what the temporary name is built from. Archives store forward slashes by
    ''' convention and backslashes in practice, so both count.
    ''' </summary>
    Friend Function LastSegmentOf(entryName As String) As String
        If String.IsNullOrEmpty(entryName) Then Return ""
        Dim cut As Integer = entryName.LastIndexOfAny(New Char() {"/"c, "\"c})
        Return If(cut >= 0, entryName.Substring(cut + 1), entryName)
    End Function

    ''' <summary>
    ''' The extension the viewer's own list has to be asked about. Taken from the last
    ''' segment, lowercased, so an entry called "PAGE.JPG" inside "A:B/" is answered the
    ''' same way the folder scanner would answer it.
    ''' </summary>
    Friend Function ExtensionOf(entryName As String) As String
        Dim leaf As String = LastSegmentOf(entryName)
        If leaf.StartsWith(".", StringComparison.Ordinal) Then Return ""
        Return MediaKindClassifier.ExtensionOf(leaf)
    End Function

    ''' <summary>
    ''' The largest entry the background prefetch extracts ahead (audit 052 G-14). A page of a
    ''' comic is a few megabytes; anything past this is extracted when the user actually
    ''' arrives, instead of holding the extraction lock while they go somewhere else.
    ''' </summary>
    Friend Const Max_Prefetch_Entry_Bytes As Long = 64L * 1024L * 1024L

    ''' <summary>
    ''' Is the entry ahead worth extracting in the background? Only a picture - the prefetch
    ''' decodes nothing else, so extracting a video ahead buys it nothing - and only one of
    ''' modest size, because the display path waits for the same lock.
    ''' </summary>
    Friend Function ShouldPrefetch(isImage As Boolean, size As Long) As Boolean
        Return isImage AndAlso size > 0 AndAlso size <= Max_Prefetch_Entry_Bytes
    End Function

End Module
#End If
