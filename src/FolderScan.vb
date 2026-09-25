Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Threading

''' <summary>
''' The folder scan both viewers run - LocalProvider.ListEntries on the mainline,
''' Main_Form.EnumerateConfiguredFiles on the x86 fallback - in one place (audit 048, A-07).
'''
''' The ROOT is not allowed to fail quietly. The scan used to return an empty list when the
''' root did not exist and to swallow every exception of its enumeration, so a sleeping NAS,
''' a deleted folder or a typo in the folder box read as "the folder is empty" - and went
''' into the recent-folders list - while an enumeration that died half-way (an SMB session
''' dropped mid-scan) handed back a TRUNCATED list as if it were the whole folder. Done spec
''' S3 Б-23 had introduced is_Read_Error precisely to tell those apart; the scan defeated it.
''' Now the root's failure reaches the caller, which classifies it (PathFailure).
'''
''' Subfolders of a recursive scan keep the old rule: an inaccessible or unreadable child is
''' skipped, just like Explorer's own scan.
''' </summary>
Public Module FolderScan

    ''' <summary>
    ''' Every file of <paramref name="root"/> (and, when asked, of its subfolders). Recursive
    ''' scans deliberately do not use AllDirectories: Windows lets a junction or symlink
    ''' point back to an ancestor (or outside the selected tree), and blindly enumerating it
    ''' can loop forever - reparse directories are therefore leaves.
    ''' </summary>
    ''' <returns>An empty list for an empty root path; never an empty list for a root that
    ''' could not be read.</returns>
    ''' <exception cref="DirectoryNotFoundException">The root does not exist.</exception>
    ''' <exception cref="IOException">The root could not be enumerated (a transport failure,
    ''' classified by the caller).</exception>
    ''' <exception cref="UnauthorizedAccessException">The root may not be listed.</exception>
    Public Function EnumerateFiles(root As String, include_Subfolders As Boolean,
                                   ct As CancellationToken) As List(Of FileInfo)
        Dim result As New List(Of FileInfo)()
        If String.IsNullOrEmpty(root) Then Return result

        ' Directory.Exists answers False for a missing folder AND for one it could not ask
        ' about; the enumeration below says which, with the exception the caller classifies.
        ' The explicit check stays only so a missing root is a DirectoryNotFoundException on
        ' both runtimes whatever the enumeration's own wording.
        If Not Directory.Exists(root) Then
            ' Let the real enumeration speak first: it throws the precise exception (access
            ' denied, network path not found) where there is one.
            Dim probe As New DirectoryInfo(root)
            For Each ignored As FileInfo In probe.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                Exit For
            Next
            Throw New DirectoryNotFoundException("Folder not found: " & root)
        End If

        If Not include_Subfolders Then
            ' No catch: a failure here - at the first entry or half-way - is the ROOT's.
            For Each f As FileInfo In New DirectoryInfo(root).EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                result.Add(f)
            Next
            Return result
        End If

        Dim pending As New Stack(Of String)()
        pending.Push(root)
        While pending.Count > 0
            ct.ThrowIfCancellationRequested()
            Dim directory As String = pending.Pop()
            Dim is_Root As Boolean = String.Equals(directory, root, StringComparison.OrdinalIgnoreCase)
            Try
                Dim info As New DirectoryInfo(directory)
                If (info.Attributes And FileAttributes.ReparsePoint) <> 0 AndAlso Not is_Root Then Continue While
                ' Added one by one: a subfolder that fails half-way keeps the files already read.
                For Each f As FileInfo In info.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    result.Add(f)
                Next
                For Each child As DirectoryInfo In info.EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                    If (child.Attributes And FileAttributes.ReparsePoint) = 0 Then pending.Push(child.FullName)
                Next
            Catch ex As UnauthorizedAccessException When Not is_Root
                Debug.WriteLine("Skipping inaccessible directory: " & directory)
            Catch ex As IOException When Not is_Root
                Debug.WriteLine("Skipping unreadable directory: " & directory)
            End Try
        End While
        Return result
    End Function

End Module
