Option Strict On

Imports System.IO

''' <summary>
''' The pure rules behind the file operations of ticket 047 - what a rename may be called
''' and which list a finished operation may still touch. Kept out of Main_Form so both are
''' questions a test can answer without a window, a share or a slow copy.
'''
''' Shared by both viewers: a rename that walks out of its folder (C-18) and a rollback
''' that lands in another folder's list (C-13) are bugs on either runtime.
''' </summary>
Public Module FileOpRules

    ''' <summary>
    ''' True when <paramref name="newName"/> can only ever name a file in the SAME folder
    ''' (audit 047, C-18). The rename composes "folder + input + extension" with
    ''' Path.Combine, whose second argument wins when it is rooted - so "D:\other\x" moved
    ''' the file to another drive, unstaged, on the UI thread, while the list kept it as if
    ''' it were still here. Separators, a drive colon, the characters Windows refuses in a
    ''' name, and the two relative names are all refused rather than repaired: silently
    ''' changing what the user typed would rename the file to something else again.
    ''' </summary>
    Public Function IsValidNewFileName(newName As String) As Boolean
        If String.IsNullOrWhiteSpace(newName) Then Return False
        If newName = "." OrElse newName = ".." Then Return False
        If newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 Then Return False
        ' GetInvalidFileNameChars already holds both separators and the colon on Windows;
        ' named anyway, because the rule is about them and must not depend on that list.
        If newName.IndexOfAny({"\"c, "/"c, ":"c}) >= 0 Then Return False
        Return True
    End Function

    ''' <summary>
    ''' True when the file list on screen is still the one an operation was started from
    ''' (audit 047, C-13). A completion, a rollback or an undo arrives long after the key
    ''' press - a slow SMB move, a U pressed in another folder - and editing whatever list
    ''' is on screen then put one folder's path into another folder's list.
    '''
    ''' The stamp is a GENERATION, bumped every time the list is replaced, rather than the
    ''' folder's name: the same folder rescanned in a new order is a new list whose indexes
    ''' mean something else, and a one-file list opened from Explorer has no scanned folder
    ''' to name. Generation 0 is "no list yet" and matches nothing.
    ''' </summary>
    Public Function ListStampMatches(opListGeneration As Integer, shownListGeneration As Integer) As Boolean
        If opListGeneration <= 0 OrElse shownListGeneration <= 0 Then Return False
        Return opListGeneration = shownListGeneration
    End Function

End Module
