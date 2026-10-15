#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic

' The "name (2).ext" rule, once. Ticket 222 extracted it from BatchPlan.FreeName so the Share Manager can
' link the SAME rule instead of copying it: BatchPlan itself reaches half the viewer and cannot be linked.
' Pure: no file system - the caller says what exists and how a folder and a name are glued, which is what
' lets a viewer path, a server locator and a Windows path all use it.

''' <summary>"IMG_0001.jpg" taken -> "IMG_0001 (2).jpg", the first free one, Explorer-style.</summary>
Friend Module FreeNameRule

    ''' <summary>The last number tried; 9998 namesakes in one folder is not a collision any more.</summary>
    Friend Const LastNumber As Integer = 9999

    ''' <summary>
    ''' The first candidate that is free in the destination AND not claimed by another file of the same
    ''' run (<paramref name="reserved"/>), or Nothing when none is.
    ''' </summary>
    ''' <param name="folder">The destination folder, in the caller's own spelling.</param>
    ''' <param name="fileName">The wanted name - split at the LAST dot, a leading dot is not an extension.</param>
    ''' <param name="combine">Glues a folder and a name the way the caller's paths are written.</param>
    Friend Function Find(folder As String, fileName As String, combine As Func(Of String, String, String),
                         exists As Func(Of String, Boolean), reserved As ISet(Of String)) As String
        Dim dot As Integer = fileName.LastIndexOf("."c)
        Dim base As String = If(dot > 0, fileName.Substring(0, dot), fileName)
        Dim ext As String = If(dot > 0, fileName.Substring(dot), "")
        For n As Integer = 2 To LastNumber
            Dim candidate As String = combine(folder, base & " (" & n.ToString() & ")" & ext)
            If reserved IsNot Nothing AndAlso reserved.Contains(candidate) Then Continue For
            If Not exists(candidate) Then Return candidate
        Next
        Return Nothing
    End Function

End Module
#End If