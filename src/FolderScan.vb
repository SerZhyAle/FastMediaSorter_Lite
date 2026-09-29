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
''' Subfolders of a recursive scan keep the old rule for failures that are about the child -
''' denied, gone, a path too long: it is skipped, just like Explorer's own scan. A TRANSPORT
''' failure of a child is not about it and fails the scan (audit 057, S01-5).
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

        Return ScanTree(root, include_Subfolders, ct, RealLister)
    End Function

    ''' <summary>
    ''' What the scan needs from the file system, as a seam (audit 057, S01-5): no real
    ''' volume drops an SMB session on demand half-way through a tree, so the rule "which
    ''' child failure is skipped" could not be tested against a real folder.
    ''' </summary>
    Friend Interface IDirectoryLister
        ''' <summary>True when <paramref name="directory"/> is a junction or symlink.</summary>
        Function IsReparsePoint(directory As String) As Boolean
        ''' <summary>The files directly inside <paramref name="directory"/>, lazily.</summary>
        Function ListFiles(directory As String) As IEnumerable(Of FileInfo)
        ''' <summary>The full paths of the subfolders directly inside
        ''' <paramref name="directory"/> that are NOT reparse points, lazily.</summary>
        Function ListSubdirectories(directory As String) As IEnumerable(Of String)
    End Interface

    ''' <summary>The lister over the real file system - DirectoryInfo, as the scan always used.</summary>
    Private NotInheritable Class FileSystemLister
        Implements IDirectoryLister

        Public Function IsReparsePoint(directory As String) As Boolean Implements IDirectoryLister.IsReparsePoint
            Return (New DirectoryInfo(directory).Attributes And FileAttributes.ReparsePoint) <> 0
        End Function

        Public Function ListFiles(directory As String) As IEnumerable(Of FileInfo) Implements IDirectoryLister.ListFiles
            Return New DirectoryInfo(directory).EnumerateFiles("*", SearchOption.TopDirectoryOnly)
        End Function

        Public Iterator Function ListSubdirectories(directory As String) As IEnumerable(Of String) Implements IDirectoryLister.ListSubdirectories
            For Each child As DirectoryInfo In New DirectoryInfo(directory).EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                If (child.Attributes And FileAttributes.ReparsePoint) = 0 Then Yield child.FullName
            Next
        End Function
    End Class

    Private ReadOnly RealLister As IDirectoryLister = New FileSystemLister()

    ''' <summary>
    ''' The walk itself, over any lister. The root's existence has already been settled by
    ''' the caller; every failure of the ROOT still reaches the caller.
    ''' </summary>
    Friend Function ScanTree(root As String, include_Subfolders As Boolean,
                             ct As CancellationToken, lister As IDirectoryLister) As List(Of FileInfo)
        Dim result As New List(Of FileInfo)()

        If Not include_Subfolders Then
            ' No catch: a failure here - at the first entry or half-way - is the ROOT's.
            For Each f As FileInfo In lister.ListFiles(root)
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
                If Not is_Root AndAlso lister.IsReparsePoint(directory) Then Continue While
                ' Added one by one: a subfolder that fails half-way keeps the files already read.
                For Each f As FileInfo In lister.ListFiles(directory)
                    result.Add(f)
                Next
                For Each child As String In lister.ListSubdirectories(directory)
                    pending.Push(child)
                Next
            Catch ex As Exception When Not is_Root AndAlso IsSkippableChildFailure(ex)
                Debug.WriteLine("Skipping unreadable directory: " & directory)
            End Try
        End While
        Return result
    End Function

    ''' <summary>
    ''' May a SUBFOLDER's failure be skipped, Explorer-style (audit 057, S01-5)? Only when it
    ''' is about that subfolder: denied, gone since it was listed, a path too long, locked.
    ''' A transport failure is not about the child - it is the session under the whole tree
    ''' dropping - and skipping it handed back a truncated tree as the whole one, with the
    ''' counter, End and the recent-folders list all describing it. It fails the scan, so
    ''' GetFiles reports it as the read error it is (the rule 048 A-07 set for the root).
    ''' </summary>
    Friend Function IsSkippableChildFailure(ex As Exception) As Boolean
        If Not (TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException) Then Return False
        Return PathFailure.Classify(ex) <> PathFailureKind.Transport
    End Function

End Module
