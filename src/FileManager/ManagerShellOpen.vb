#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Concurrent
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Safe shell opening, external program execution, picker dialog, and leased external cache management
''' for the file manager (Specification 258 Section 3 &amp; 4).
''' Modern build only.
''' </summary>
Friend NotInheritable Class ManagerShellOpen

    Private Sub New()
    End Sub

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Auto)>
    Private Structure OPENASINFO
        Public pcszFile As String
        Public pcszClass As String
        Public oaifInFlags As Integer
    End Structure

    Private Const OAIF_ALLOW_REGISTRATION As Integer = &H1
    Private Const OAIF_REGISTER_EXT As Integer = &H2
    Private Const OAIF_EXEC As Integer = &H4
    Private Const OAIF_FORCE_REGISTRATION As Integer = &H8

    <DllImport("shell32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function SHOpenWithDialog(hwndParent As IntPtr, ByRef poainfo As OPENASINFO) As Integer
    End Function

    <DllImport("shlwapi.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function AssocIsDangerous(association As String) As Boolean
    End Function

    ''' <summary>Test spy delegate for intercepting shell launches during unit tests.</summary>
    Friend Delegate Function ShellLaunchDelegate(path As String, usePicker As Boolean, ownerHwnd As IntPtr) As Boolean
    Friend Shared LauncherSpy As ShellLaunchDelegate = Nothing

    ''' <summary>Opens the local file using default shell association or picker if requested.</summary>
    Friend Shared Function OpenFile(ownerHwnd As IntPtr, localPath As String, usePicker As Boolean) As Boolean
        If String.IsNullOrWhiteSpace(localPath) OrElse Not File.Exists(localPath) Then Return False

        ' Execution guard recheck on the actual file on disk
        If Not ManagerOpenPolicy.RecheckMaterialized(localPath, AddressOf AssocIsDangerous) Then Return False

        If LauncherSpy IsNot Nothing Then
            Return LauncherSpy(localPath, usePicker, ownerHwnd)
        End If

        If usePicker Then
            Return OpenWithPicker(ownerHwnd, localPath)
        Else
            Return OpenDefault(ownerHwnd, localPath)
        End If
    End Function

    ''' <summary>Opens file with the system default program. Falls back to picker if no association is registered.</summary>
    Friend Shared Function OpenDefault(ownerHwnd As IntPtr, localPath As String) As Boolean
        Try
            Dim psi As New ProcessStartInfo With {
                .FileName = localPath,
                .UseShellExecute = True,
                .WorkingDirectory = "",
                .Arguments = ""
            }
            Process.Start(psi)
            Return True
        Catch ex As Win32Exception When ex.NativeErrorCode = 1155 OrElse ex.NativeErrorCode = 31
            ' 1155 = ERROR_NO_ASSOCIATION, 31 = SE_ERR_NOASSOC
            Return OpenWithPicker(ownerHwnd, localPath)
        Catch ex As Exception
            AppFileLogger.LogException("ManagerShellOpen.OpenDefault", ex)
            Return False
        End Try
    End Function

    ''' <summary>Shows the Windows "Open With" dialog (SHOpenWithDialog) and executes the chosen program.</summary>
    Friend Shared Function OpenWithPicker(ownerHwnd As IntPtr, localPath As String) As Boolean
        Try
            Dim info As New OPENASINFO With {
                .pcszFile = localPath,
                .pcszClass = String.Empty,
                .oaifInFlags = OAIF_EXEC Or OAIF_REGISTER_EXT
            }
            Dim hr As Integer = SHOpenWithDialog(ownerHwnd, info)
            Return hr >= 0
        Catch ex As Exception
            AppFileLogger.LogException("ManagerShellOpen.OpenWithPicker", ex)
            Return False
        End Try
    End Function

    ''' <summary>Reveals the file selected in Windows Explorer.</summary>
    Friend Shared Sub RevealInExplorer(localPath As String)
        If String.IsNullOrWhiteSpace(localPath) OrElse Not File.Exists(localPath) Then Return
        Try
            Dim psi As New ProcessStartInfo With {
                .FileName = "explorer.exe",
                .Arguments = "/select,""" & localPath & """",
                .UseShellExecute = True
            }
            Process.Start(psi)
        Catch ex As Exception
            AppFileLogger.LogException("ManagerShellOpen.RevealInExplorer", ex)
        End Try
    End Sub

End Class

''' <summary>
''' Leased external cache manager for remote files opened externally (Specification 258 Section 3 &amp; 4).
''' Retains leases until application shutdown so navigation does not evict files currently open in external applications.
''' Enforces RemoteCacheMaxMb budget.
''' </summary>
Friend NotInheritable Class ManagerExternalCache

    Private Sub New()
    End Sub

    Private Shared ReadOnly LeasedPaths As New ConcurrentDictionary(Of String, Byte)(StringComparer.OrdinalIgnoreCase)

    Friend Shared Function GetCacheRoot() As String
        Dim dir As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastMediaSorter", "manager_external_leases")
        If Not Directory.Exists(dir) Then
            Directory.CreateDirectory(dir)
        End If
        Return dir
    End Function

    ''' <summary>
    ''' Computes an isolated target directory that preserves the leaf name and extension.
    ''' </summary>
    Friend Shared Function PrepareLeasePath(resourceId As String, remoteVirtualPath As String, leafName As String) As String
        Dim root As String = GetCacheRoot()
        Dim folderKey As String = Math.Abs((resourceId & "|" & remoteVirtualPath).GetHashCode()).ToString("X8")
        Dim subDir As String = Path.Combine(root, folderKey)
        If Not Directory.Exists(subDir) Then
            Directory.CreateDirectory(subDir)
        End If
        Return Path.Combine(subDir, leafName)
    End Function

    ''' <summary>Registers an active lease on a file path so background cleanup won't delete it.</summary>
    Friend Shared Sub RetainLease(filePath As String)
        If Not String.IsNullOrWhiteSpace(filePath) Then
            LeasedPaths(filePath) = 0
        End If
    End Sub

    ''' <summary>Checks whether adding a file of the given size would exceed the budget.</summary>
    Friend Shared Function IsWithinBudget(additionalSizeBytes As Long, budgetMb As Integer) As Boolean
        If budgetMb <= 0 Then Return True
        Dim maxBytes As Long = CLng(budgetMb) * 1024L * 1024L
        If additionalSizeBytes > maxBytes Then Return False
        Return True
    End Function

    ''' <summary>
    ''' Cleans unleased files in the external cache directory if the directory exceeds the budget.
    ''' Leased files are always preserved.
    ''' </summary>
    Friend Shared Sub PruneUnleased(budgetMb As Integer)
        Try
            Dim root As String = GetCacheRoot()
            If Not Directory.Exists(root) Then Return
            Dim di As New DirectoryInfo(root)
            Dim files As FileInfo() = di.GetFiles("*", SearchOption.AllDirectories)
            Dim totalBytes As Long = 0
            For Each fi As FileInfo In files
                totalBytes += fi.Length
            Next

            Dim maxBytes As Long = CLng(budgetMb) * 1024L * 1024L
            If totalBytes <= maxBytes Then Return

            ' Sort unleased files oldest first
            Dim candidates = files.Where(Function(f) Not LeasedPaths.ContainsKey(f.FullName)).OrderBy(Function(f) f.LastAccessTimeUtc).ToList()
            For Each candidate As FileInfo In candidates
                If totalBytes <= maxBytes Then Exit For
                Try
                    Dim len As Long = candidate.Length
                    candidate.Delete()
                    totalBytes -= len
                Catch
                End Try
            Next
        Catch ex As Exception
            AppFileLogger.LogException("ManagerExternalCache.PruneUnleased", ex)
        End Try
    End Sub

    ''' <summary>Notice string displayed when opening a remote copy externally.</summary>
    Friend Shared Function RemoteNoticeText() As String
        Return Localization.T("Изменения в локальной копии не сохраняются на сервере.")
    End Function

End Class
#End If
