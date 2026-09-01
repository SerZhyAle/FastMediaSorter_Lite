#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Text

''' <summary>
''' Where an archive session's extracted entries live, and what they are called
''' (010_SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md §3.2, §4.1).
'''
''' Every function here is pure - a string in, a string out, no file system - because
''' this is where the feature's second invariant is enforced: <b>the destination name is
''' built by us</b>, from the entry's last segment only, so a "zip slip" entry called
''' <c>..\..\Windows\System32\x.dll</c> cannot become a path outside the session
''' directory. That is a structural guarantee rather than a filter, and a structural
''' guarantee is worth a test.
'''
''' Modern-only, like the whole feature.
''' </summary>
Friend Module ArchivePaths

    ''' <summary>Longest stem we keep from the entry's own name (§3.2). Windows' limit is
    ''' 255 for the whole name; 100 leaves plenty of room for the index prefix and keeps
    ''' a deep path inside MAX_PATH on a machine without long paths enabled.</summary>
    Friend Const Max_Stem_Length As Integer = 100

    ''' <summary>Longest extension we keep, dot included. Every format the viewer reads
    ''' fits in five characters; a longer tail is not an extension and must not be a way
    ''' to grow the name past what the file system accepts.</summary>
    Friend Const Max_Extension_Length As Integer = 16

    ''' <summary>Name of the cache root under the app's own %LOCALAPPDATA% folder.</summary>
    Private Const Cache_Folder As String = "archive-cache"

    ''' <summary>
    ''' %LOCALAPPDATA%\SZA\FastMediaSorter\archive-cache
    '''
    ''' Under the app's existing root (the one OCR already uses), not %TEMP%: it is the
    ''' same container the rest of the app's state lives in - which matters under MSIX -
    ''' and a third-party %TEMP% cleaner will not delete the file LibVLC is playing.
    ''' </summary>
    Friend Function CacheRoot() As String
        Return Path.Combine(AppPaths.LocalAppDataRoot(), Cache_Folder)
    End Function

    ''' <summary>
    ''' Directory name for a session owned by <paramref name="processId"/>:
    ''' <c>&lt;pid&gt;-&lt;8 hex&gt;</c>.
    '''
    ''' The pid is in the name so the orphan sweep (§4.5) can tell a live owner from a
    ''' directory left behind by a process that was killed - two viewers running side by
    ''' side (x64 and x86, or two Windows sessions) must not clean up after each other.
    ''' </summary>
    Friend Function SessionDirName(processId As Integer, token As String) As String
        Return processId.ToString(Globalization.CultureInfo.InvariantCulture) & "-" & token
    End Function

    ''' <summary>
    ''' The pid a session directory name carries, or False when the name is not one of
    ''' ours. Anything unrecognised is left alone: the sweep deletes only what it can
    ''' positively identify as an archive session.
    ''' </summary>
    Friend Function TryParseSessionPid(dirName As String, ByRef processId As Integer) As Boolean
        processId = 0
        If String.IsNullOrEmpty(dirName) Then Return False

        Dim separator As Integer = dirName.IndexOf("-"c)
        If separator <= 0 OrElse separator = dirName.Length - 1 Then Return False

        Dim head As String = dirName.Substring(0, separator)
        Dim tail As String = dirName.Substring(separator + 1)
        If tail.Length = 0 Then Return False

        Dim parsed As Integer
        If Not Integer.TryParse(head, Globalization.NumberStyles.None,
                                Globalization.CultureInfo.InvariantCulture, parsed) Then Return False
        If parsed <= 0 Then Return False

        processId = parsed
        Return True
    End Function

    ''' <summary>
    ''' The file name an entry gets on disk: <c>00042_page12.jpg</c>.
    '''
    ''' The index prefix does three jobs at once. It keeps two entries with the same name
    ''' in different archive folders apart (the folder structure is deliberately not
    ''' recreated); it makes the name unique without hashing; and it means the name can
    ''' never come out as a reserved device name (CON, NUL, LPT1..), because it always
    ''' starts with digits.
    '''
    ''' The stem is the entry's LAST segment only - never the path. That is what makes
    ''' directory traversal impossible rather than merely filtered (§6.1).
    ''' </summary>
    Friend Function EntryFileName(index As Integer, entryName As String) As String
        Dim segment As String = LastSegment(entryName)

        ' Split the name by hand rather than through Path.GetExtension /
        ' GetFileNameWithoutExtension: those two read a colon as a volume separator, so an
        ' entry legitimately called "a:b.jpg" inside a ZIP came out as "b.jpg" - the part
        ' before the colon silently dropped. An archive entry is not a Windows path, and
        ' treating it as one is how names get mangled.
        Dim dot As Integer = segment.LastIndexOf("."c)
        ' dot > 0, not >= 0: ".gitignore" is a name, not an extension.
        Dim extension As String = Sanitize(If(dot > 0, segment.Substring(dot), ""))
        Dim stem As String = Sanitize(If(dot > 0, segment.Substring(0, dot), segment))

        If stem.Length > Max_Stem_Length Then stem = stem.Substring(0, Max_Stem_Length)
        If stem.Length = 0 Then stem = "entry"
        ' A real media extension is a handful of characters; anything longer is not one,
        ' and an unbounded tail would be a way to blow past the file system's name limit.
        If extension.Length > Max_Extension_Length Then extension = extension.Substring(0, Max_Extension_Length)

        Return index.ToString("00000", Globalization.CultureInfo.InvariantCulture) & "_" & stem & extension
    End Function

    ''' <summary>
    ''' What an archive entry is called, with every directory component dropped. Archives
    ''' store forward slashes by convention and backslashes in practice, so both count -
    ''' as does a drive-letter colon, which is why the sanitiser below removes it too.
    ''' </summary>
    Private Function LastSegment(entryName As String) As String
        If String.IsNullOrEmpty(entryName) Then Return ""
        Dim cut As Integer = entryName.LastIndexOfAny(New Char() {"/"c, "\"c})
        Dim segment As String = If(cut >= 0, entryName.Substring(cut + 1), entryName)
        ' Windows silently strips trailing dots and spaces from file names, which would
        ' make our name and the name on disk differ - and the size check in
        ' EnsureExtracted compares the file we think we wrote.
        Return segment.Trim().TrimEnd("."c, " "c)
    End Function

    ''' <summary>
    ''' Everything Windows forbids in a file name becomes an underscore. Not a security
    ''' boundary - the name has already lost its directory components - but a name that
    ''' cannot be created is an extraction failure on a perfectly good archive.
    ''' </summary>
    Private Function Sanitize(value As String) As String
        If String.IsNullOrEmpty(value) Then Return ""
        Dim invalid As Char() = Path.GetInvalidFileNameChars()
        Dim builder As New StringBuilder(value.Length)
        For Each c As Char In value
            If Array.IndexOf(invalid, c) >= 0 OrElse Char.IsControl(c) Then
                builder.Append("_"c)
            Else
                builder.Append(c)
            End If
        Next
        Return builder.ToString()
    End Function

End Module
#End If
