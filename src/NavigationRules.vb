Option Strict On

Imports System.Collections.Generic
Imports System.IO

''' <summary>
''' The pure decisions of the "which file is next -> picture on screen" path (audit 048).
''' Each lives here rather than inside Main_Form for the reason AutoSkipPolicy does: it is
''' the part of the machinery that can be tested without a form, a folder or a decoder, and
''' every one of them was wrong once in a way only a test would have pinned down.
''' Both builds compile it.
''' </summary>
Public Module NavigationRules

    ' The read modes of Main_Form.ReadShowMediaFile. Defined here, once, so the pure rules
    ' below and their tests speak the same strings the form sends; Main_Form's Mode_*
    ' constants are these values.
    Public Const ReadMode_Next As String = "ReadNextFile"
    Public Const ReadMode_Prev As String = "ReadPrevFile"
    Public Const ReadMode_SetFile As String = "SetFile"
    Public Const ReadMode_Files As String = "ReadFiles"
    Public Const ReadMode_FolderAndFile As String = "ReadFolderAndFile"
    Public Const ReadMode_FolderAndKnownFile As String = "ReadFolderAndKnownFile"
    Public Const ReadMode_Delete As String = "DeleteFile"
    Public Const ReadMode_InSlideShow As String = "InSlideShow"
    Public Const ReadMode_ForRandom As String = "ReadForRandom"
    Public Const ReadMode_ForSlideShow As String = "ReadForSlideShow"
    Public Const ReadMode_AfterUndo As String = "AfterUndo"
    Public Const ReadMode_JumpBy As String = "JumpBy"
    Public Const ReadMode_JumpTo As String = "JumpTo"

    ''' <summary>
    ''' May the 40 ms key-repeat throttle refuse this read mode (audit 057, S02-4)?
    '''
    ''' The throttle exists to tame key repeat and fast slideshow ticks. It used to refuse
    ''' EVERY mode but an auto-skip - including the ones whose callers have already changed
    ''' state before calling in: an external open (Current_File_Name, a one-file list),
    ''' a folder pick (Current_Folder_Path), entering an archive, a file operation that has
    ''' already edited the list (SetFile/AfterUndo). Refusing those dropped the display and
    ''' left the old picture up while DEL / 0-9 already aimed at the new file.
    '''
    ''' So only the modes a held key or a timer repeats are throttled: Next, Prev, JumpBy,
    ''' ForRandom, InSlideShow - and Delete, which is also a held key (D / Del) and whose
    ''' caller mutates nothing before the call (the Shift+DEL flag is cleared in a Finally,
    ''' audit 047 C-15), so refusing a repeat of it loses nothing and keeps the destructive
    ''' key's rate where it always was.
    ''' </summary>
    Public Function IsThrottledReadMode(read_Mode As String) As Boolean
        Select Case read_Mode
            Case ReadMode_Next, ReadMode_Prev, ReadMode_JumpBy, ReadMode_ForRandom,
                 ReadMode_InSlideShow, ReadMode_Delete
                Return True
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>
    ''' Does this read mode REPLACE the list the window holds - an explicit folder (re)load or an
    ''' external open (014 U-03c)? Only such a call may arrive while a background folder scan is
    ''' running: it supersedes the scan, because it either names another folder or asks for the
    ''' same one afresh. Every other mode (a flip, a jump, a delete, the display after a file
    ''' operation) is about the list the scan is about to replace, so it waits out the scan's
    ''' status line instead of acting on a list that is already going away.
    ''' </summary>
    Public Function IsListReplacingMode(read_Mode As String) As Boolean
        Select Case read_Mode
            Case ReadMode_Files, ReadMode_FolderAndFile, ReadMode_FolderAndKnownFile
                Return True
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>
    ''' Will the pipeline walk the folder for this read mode (014 U-03c)? The same four places
    ''' that call GetFiles decide it: an explicit (re)load always does; the slideshow and
    ''' random modes only when the list in memory is not the scan of the current folder; a
    ''' flip only when a file opened from outside still holds a one-file list. An external
    ''' open itself never scans - the folder is read at the first flip.
    ''' </summary>
    ''' <param name="is_List_Loaded">files_List/files_Array really describe the current folder.</param>
    ''' <param name="is_External_Pending">A file opened from outside is still the whole list.</param>
    Public Function ModeReadsFolder(read_Mode As String, is_List_Loaded As Boolean,
                                    is_External_Pending As Boolean) As Boolean
        Select Case read_Mode
            Case ReadMode_Files, ReadMode_FolderAndFile
                Return True
            Case ReadMode_ForRandom, ReadMode_ForSlideShow
                Return Not is_List_Loaded
            Case ReadMode_Next, ReadMode_Prev
                Return is_External_Pending
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>
    ''' The index of the file the prefetch should decode while <paramref name="current"/> is on
    ''' screen: the neighbour in the direction the user is going, wrapping at both ends like every
    ''' other flip (014 U-01). It used to be "the next file" whichever way the user went, so paging
    ''' backwards decoded the wrong picture ahead of every step and each step back paid a decode in
    ''' front of the user. -1 for an empty list; a one-file list names itself, which the worker
    ''' recognises as nothing to do.
    ''' </summary>
    Public Function PrefetchIndex(current As Integer, total As Integer, backwards As Boolean) As Integer
        If total <= 0 Then Return -1
        Dim at As Integer = Math.Min(Math.Max(current, 0), total - 1)
        If backwards Then Return If(at = 0, total - 1, at - 1)
        Return If(at = total - 1, 0, at + 1)
    End Function

    ''' <summary>
    ''' Records <paramref name="folder"/> as the most recent folder (audit 057, S02-6) and
    ''' says whether the list changed. Moves it to the front (once - Ordinal, as the list has
    ''' always compared), then holds the list to <paramref name="limit"/>: 0 records nothing
    ''' and empties whatever an earlier, higher limit left (053 F-12).
    '''
    ''' It used to run only when the folder box's text differed from the folder - and every
    ''' ingress but the folder dialog writes the box first, so a folder opened from Explorer
    ''' never entered the list and re-picking one never moved it to the top.
    ''' </summary>
    Public Function PromoteRecentFolder(list As List(Of String), folder As String, limit As Integer) As Boolean
        If list Is Nothing OrElse String.IsNullOrEmpty(folder) Then Return False
        Dim changed As Boolean = False
        If limit > 0 AndAlso (list.Count = 0 OrElse Not String.Equals(list(0), folder, StringComparison.Ordinal)) Then
            list.Remove(folder)
            list.Insert(0, folder)
            changed = True
        End If
        Dim keep As Integer = Math.Max(0, limit)
        If list.Count > keep Then
            list.RemoveRange(keep, list.Count - keep)
            changed = True
        End If
        Return changed
    End Function

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
#If Not NETFRAMEWORK Then
        Return MediaKindClassifier.ExtensionOf(file_Path)
#Else
        If String.IsNullOrEmpty(file_Path) Then Return ""
        Dim extension As String = Path.GetExtension(file_Path)
        If String.IsNullOrEmpty(extension) Then Return ""
        Return extension.ToLowerInvariant()
#End If
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

''' <summary>
''' Which "open this path" request is the latest one (ticket 106, 057.S13-5).
'''
''' An argument, a startup resume and an unlock retry each probe the filesystem off the UI
''' thread and open the file when the probe comes back. They used to compare
''' <c>media_Generation</c> before and after the probe - but that counter moves when media is
''' DISPLAYED, not when an open is REQUESTED. Request A, then B, both probes pending: both
''' captured the same generation, A finished first and displayed (generation + 1), and B -
''' the file the user asked for last - was dropped as "the user moved on".
'''
''' Here the identity moves at the two moments that matter: <see cref="Accept"/> when a new
''' request is taken (it makes every earlier one obsolete) and <see cref="Invalidate"/> when
''' the user navigates or the window closes (it makes every pending one obsolete without
''' starting a new one). A completion opens its file only while <see cref="IsCurrent"/> still
''' holds for the ticket it took. Thread-safe on purpose: the probe loop on a worker thread
''' polls it to stop retrying a request nobody is waiting for any more.
''' Both builds compile it.
''' </summary>
Public NotInheritable Class OpenRequestSequence

    Private current_Ticket As Integer

    ''' <summary>Takes a new request. Every ticket handed out before this one is obsolete.</summary>
    Public Function Accept() As Integer
        Return System.Threading.Interlocked.Increment(current_Ticket)
    End Function

    ''' <summary>Navigation or close: every pending request is obsolete, none is the latest.</summary>
    Public Sub Invalidate()
        System.Threading.Interlocked.Increment(current_Ticket)
    End Sub

    ''' <summary>True while nothing has been accepted or invalidated since <paramref name="ticket"/>.</summary>
    Public Function IsCurrent(ticket As Integer) As Boolean
        Return System.Threading.Volatile.Read(current_Ticket) = ticket
    End Function

End Class
