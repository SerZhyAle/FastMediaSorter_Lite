Option Strict On

Imports System.Collections.Generic
Imports System.IO

''' <summary>
''' The names shared folders go to the worker under - the one place they are made.
'''
''' The worker refuses the WHOLE folder set when any name is empty, contains "/" or "\", or
''' repeats another one (sftpserver.NewRootSet), and it refuses at start - so the list
''' persisted fine, failed at every start, and the window blamed the port (ticket 054 H-03).
''' Two folders called "Photos" on different disks, a whole drive ("D:\" is its own name in
''' .NET), or a label typed as "Photos/2024" were each enough. Every name therefore passes
''' through here: path separators replaced, a drive root named after its volume or letter,
''' and duplicates numbered "Photos (2)" in list order - so the first folder keeps the name
''' codes already handed out carry.
''' </summary>
Public Module ShareRootNames

    ''' <summary>A name the worker accepts, from a label or folder name: "/" and "\" become "-",
    ''' surrounding blanks go. Empty when nothing usable is left. ":" is left alone on purpose:
    ''' the worker accepts it, and a label that has been working with one is part of every
    ''' code already handed out - renaming it would move the folder out from under them.</summary>
    Public Function Clean(name As String) As String
        Dim s As String = If(name, "").Replace("/"c, "-"c).Replace("\"c, "-"c).Trim()
        ' A name made only of separators would come out as dashes - treat that as no name.
        If s.Trim("-"c, " "c).Length = 0 Then Return ""
        Return s
    End Function

    ''' <summary>The default share name for a host folder: its leaf, or - for a drive root,
    ''' which has no leaf - the volume label, else the drive letter.</summary>
    Public Function ForFolder(hostPath As String, Optional volumeLabel As Func(Of String, String) = Nothing) As String
        Dim path As String = If(hostPath, "").Trim()
        If path.Length = 0 Then Return ""
        Dim trimmed As String = path.TrimEnd("\"c, "/"c)
        If trimmed.Length = 2 AndAlso trimmed(1) = ":"c Then
            Dim letter As String = trimmed.Substring(0, 1).ToUpperInvariant()
            Dim label As String = ""
            Dim reader As Func(Of String, String) = volumeLabel
            If reader Is Nothing Then reader = AddressOf ReadVolumeLabel
            Try
                label = reader(letter & ":\")
            Catch
                label = ""
            End Try
            Dim fromLabel As String = Clean(label)
            Return If(fromLabel.Length > 0, fromLabel, letter)
        End If
        Dim leaf As String = ""
        Try
            leaf = System.IO.Path.GetFileName(trimmed)
        Catch
            leaf = ""
        End Try
        Dim cleaned As String = Clean(leaf)
        If cleaned.Length > 0 Then Return cleaned
        ' A UNC share root and anything else without a usable leaf: the whole path, cleaned.
        Dim whole As String = Clean(trimmed)
        Return If(whole.Length > 0, whole, "Folder")
    End Function

    Private Function ReadVolumeLabel(root As String) As String
        Dim d As New DriveInfo(root)
        If Not d.IsReady Then Return ""
        Return d.VolumeLabel
    End Function

    ''' <summary>
    ''' The final names for a folder list, in order: each wanted name (a label, or empty for
    ''' "use the folder's"), cleaned, then made unique case-insensitively with " (2)",
    ''' " (3)".. - the worker compares names exactly, but two names a phone shows as the same
    ''' folder differing only in case would be a trap of their own.
    ''' </summary>
    Public Function MakeUnique(wanted As IList(Of String), hostPaths As IList(Of String),
                               Optional volumeLabel As Func(Of String, String) = Nothing) As List(Of String)
        Dim result As New List(Of String)()
        Dim taken As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim count As Integer = If(hostPaths Is Nothing, 0, hostPaths.Count)
        For i As Integer = 0 To count - 1
            Dim w As String = If(wanted IsNot Nothing AndAlso i < wanted.Count, Clean(wanted(i)), "")
            Dim baseName As String = If(w.Length > 0, w, ForFolder(hostPaths(i), volumeLabel))
            Dim candidate As String = baseName
            Dim n As Integer = 2
            While taken.Contains(candidate)
                candidate = baseName & " (" & n.ToString(Globalization.CultureInfo.InvariantCulture) & ")"
                n += 1
            End While
            taken.Add(candidate)
            result.Add(candidate)
        Next
        Return result
    End Function

End Module
