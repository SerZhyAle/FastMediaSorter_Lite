Option Strict On

Imports System.IO

''' <summary>
''' The pure decisions of the "which file is next -> picture on screen" path (audit 048).
''' Each lives here rather than inside Main_Form for the reason AutoSkipPolicy does: it is
''' the part of the machinery that can be tested without a form, a folder or a decoder, and
''' every one of them was wrong once in a way only a test would have pinned down.
''' Both builds compile it.
''' </summary>
Public Module NavigationRules

    ''' <summary>
    ''' May a finished prefetch be put into its picture box (audit 048, A-01)?
    '''
    ''' A result used to be accepted by the NAME of the file alone. A request queued while
    ''' the worker was busy survives a box swap, so a plain back-and-forth between two photos
    ''' could land "the next file" in the box that had meanwhile become the VISIBLE one: the
    ''' screen showed B while the counter, the name and DEL all meant A.
    '''
    ''' The request is stamped with the media generation it was made for, and only a result
    ''' for the generation still on screen, aimed at the box that is still the hidden one,
    ''' for the file that is still "the next one", is taken.
    ''' </summary>
    ''' <param name="request_Generation">media_Generation when the request was queued.</param>
    ''' <param name="current_Generation">media_Generation now.</param>
    ''' <param name="target_Is_Box1">The box the request was decoded FOR.</param>
    ''' <param name="is_Second_Box_Active">True when box 2 is the one on screen now.</param>
    Public Function IsPrefetchCurrent(request_Generation As Integer, current_Generation As Integer,
                                      target_Is_Box1 As Boolean, is_Second_Box_Active As Boolean,
                                      result_Next_File As String, expected_Next_File As String) As Boolean
        If request_Generation <> current_Generation Then Return False
        ' The target must be the box that is NOT on screen: box 1 while box 2 is active,
        ' box 2 while box 1 is.
        If target_Is_Box1 <> is_Second_Box_Active Then Return False
        If String.IsNullOrEmpty(result_Next_File) Then Return False
        Return String.Equals(result_Next_File, expected_Next_File, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>
    ''' May a progress report of the prefetch worker (the name/size/date line, the background
    ''' folder count) be written to the screen (audit 048, A-15)? Only when it was produced for
    ''' the media generation still on screen - a report about a file the user has already left
    ''' used to overwrite the label naming the file actually shown.
    ''' </summary>
    Public Function IsReportCurrent(report_Generation As Integer, current_Generation As Integer) As Boolean
        Return report_Generation = current_Generation
    End Function

    ''' <summary>
    ''' The lookup key of a file's extension: lower case in the INVARIANT culture (audit 048,
    ''' A-10). ToLower() follows the current culture, and under tr-TR / az ".HEIC".ToLower()
    ''' is ".heıc" with a dotless i - not in any extension set, so iPhone HEIC, camera AVI and
    ''' every .GIF/.TIFF/.ICO/.AVIF/.EXIF never entered the folder list on a Turkish or
    ''' Azerbaijani Windows. "" for a path without an extension or no path at all.
    ''' </summary>
    Public Function ExtensionKey(file_Path As String) As String
        If String.IsNullOrEmpty(file_Path) Then Return ""
        Dim extension As String = Path.GetExtension(file_Path)
        If String.IsNullOrEmpty(extension) Then Return ""
        Return extension.ToLowerInvariant()
    End Function

    ''' <summary>
    ''' True for a path that names a network location by itself: a UNC path (\\server\share)
    ''' or its long-path form (\\?\UNC\server\share). A mapped drive letter is not caught here
    ''' - the caller asks DriveInfo for that. Pure: string work only.
    ''' </summary>
    Public Function IsUncPath(file_Path As String) As Boolean
        If String.IsNullOrEmpty(file_Path) Then Return False
        If file_Path.StartsWith("\\?\UNC\", StringComparison.OrdinalIgnoreCase) Then Return True
        If file_Path.StartsWith("\\?\", StringComparison.Ordinal) OrElse
           file_Path.StartsWith("\\.\", StringComparison.Ordinal) Then Return False
        Return file_Path.StartsWith("\\", StringComparison.Ordinal) OrElse
               file_Path.StartsWith("//", StringComparison.Ordinal)
    End Function

End Module
