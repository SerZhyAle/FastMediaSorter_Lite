Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The subtree preflight - the check that was missing on 2026-09-01, when a phone
''' received an empty listing for a folder holding 758 files. The share ROOT was
''' readable and every existing check looked only at the root, so nothing on the PC
''' knew that four folders deep inside it had lost the serving account from their
''' ACL.
'''
''' These tests pin the two properties the UI depends on: the walk actually finds a
''' folder the current process cannot open, and it stays inside its budget instead of
''' running a whole disk while a person waits.
''' </summary>
Public Class FolderAccessScanTests

    ''' <summary>A temp tree that removes itself, deny-ACEs and all.</summary>
    Private NotInheritable Class TempTree
        Implements IDisposable

        Public ReadOnly Property Root As String
        Private ReadOnly _denied As New List(Of String)()

        Public Sub New()
            _Root = Path.Combine(Path.GetTempPath(), "fms-acl-" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(Root)
        End Sub

        Public Function Dir(ParamArray parts As String()) As String
            Dim full As String = Path.Combine(New String() {Root}.Concat(parts).ToArray())
            Directory.CreateDirectory(full)
            Return full
        End Function

        ''' <summary>Makes a folder genuinely unopenable by this process. A Deny ACE
        ''' beats every Allow, so it holds even when the tests run elevated - unlike
        ''' simply stripping the Allow entries.</summary>
        Public Function Deny(path As String) As Boolean
            Try
                Dim me_ As SecurityIdentifier = WindowsIdentity.GetCurrent().User
                If me_ Is Nothing Then Return False
                Dim di As New DirectoryInfo(path)
                Dim acl As DirectorySecurity = di.GetAccessControl()
                acl.AddAccessRule(New FileSystemAccessRule(
                    me_, FileSystemRights.ReadAndExecute Or FileSystemRights.ListDirectory,
                    InheritanceFlags.ObjectInherit Or InheritanceFlags.ContainerInherit,
                    PropagationFlags.None, AccessControlType.Deny))
                di.SetAccessControl(acl)
                _denied.Add(path)
                ' Only useful if it actually took effect.
                Try
                    Directory.GetDirectories(path)
                    Return False
                Catch ex As UnauthorizedAccessException
                    Return True
                End Try
            Catch
                Return False
            End Try
        End Function

        ''' <summary>Gives LOCAL SERVICE inheritable read access, the way the elevated
        ''' helper does on a shared root.</summary>
        Public Function GrantLocalService(path As String) As Boolean
            Try
                Dim di As New DirectoryInfo(path)
                Dim acl As DirectorySecurity = di.GetAccessControl()
                acl.AddAccessRule(New FileSystemAccessRule(
                    New SecurityIdentifier("S-1-5-19"), FileSystemRights.ReadAndExecute,
                    InheritanceFlags.ObjectInherit Or InheritanceFlags.ContainerInherit,
                    PropagationFlags.None, AccessControlType.Allow))
                di.SetAccessControl(acl)
                Return True
            Catch
                Return False
            End Try
        End Function

        ''' <summary>Disables ACL inheritance and keeps nothing but this user's own
        ''' access - the state a third-party tool leaves a folder in, and the wall an
        ''' inheritable grant on the root cannot get past.</summary>
        Public Function BreakInheritance(path As String) As Boolean
            Try
                Dim di As New DirectoryInfo(path)
                Dim acl As DirectorySecurity = di.GetAccessControl()
                acl.SetAccessRuleProtection(True, False)   ' protected, inherited rules NOT copied
                acl.AddAccessRule(New FileSystemAccessRule(
                    WindowsIdentity.GetCurrent().User, FileSystemRights.FullControl,
                    InheritanceFlags.ObjectInherit Or InheritanceFlags.ContainerInherit,
                    PropagationFlags.None, AccessControlType.Allow))
                di.SetAccessControl(acl)
                Return True
            Catch
                Return False
            End Try
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            For Each p As String In _denied
                Try
                    Dim di As New DirectoryInfo(p)
                    Dim acl As DirectorySecurity = di.GetAccessControl()
                    acl.RemoveAccessRuleAll(New FileSystemAccessRule(
                        WindowsIdentity.GetCurrent().User, FileSystemRights.ReadAndExecute,
                        AccessControlType.Deny))
                    di.SetAccessControl(acl)
                Catch
                End Try
            Next
            Try : Directory.Delete(Root, True) : Catch : End Try
        End Sub
    End Class

    <Fact>
    Public Sub ScanSubtree_FindsAFolderTheProcessCannotOpen()
        Using tree As New TempTree()
            tree.Dir("readable", "pdf_images")
            Dim blocked As String = tree.Dir("Telegram Desktop", "Chapter 12 Trigonometry Special HW")
            If Not tree.Deny(blocked) Then Return   ' cannot build the premise - proves nothing

            Dim scan As FolderAccess.SubtreeScan = FolderAccess.ScanSubtree(tree.Root, writable:=False, forServiceAccount:=False)

            Assert.True(scan.Completed)
            Assert.Contains(blocked, scan.Blocked)
            ' The readable sibling is NOT reported: a false positive sends the user
            ' chasing a folder that works.
            Assert.DoesNotContain(scan.Blocked, Function(p) p.Contains("readable"))
        End Using
    End Sub

    <Fact>
    Public Sub ScanSubtree_DoesNotDescendIntoABlockedFolder()
        Using tree As New TempTree()
            Dim blocked As String = tree.Dir("outer")
            Dim inner As String = tree.Dir("outer", "inner")
            If Not tree.Deny(blocked) Then Return

            Dim scan As FolderAccess.SubtreeScan = FolderAccess.ScanSubtree(tree.Root, writable:=False, forServiceAccount:=False)

            ' The blocked folder is reported once; its children are unreachable anyway
            ' and listing them would turn one real problem into a wall of noise.
            Assert.Contains(blocked, scan.Blocked)
            Assert.DoesNotContain(inner, scan.Blocked)
        End Using
    End Sub

    <Fact>
    Public Sub ScanSubtree_CleanTreeReportsNothing()
        Using tree As New TempTree()
            tree.Dir("a", "b", "c")
            tree.Dir("d")

            Dim scan As FolderAccess.SubtreeScan = FolderAccess.ScanSubtree(tree.Root, writable:=False, forServiceAccount:=False)

            Assert.True(scan.Completed)
            Assert.Empty(scan.Blocked)
            Assert.True(scan.Scanned >= 5, "every folder should have been visited, got " & scan.Scanned)
        End Using
    End Sub

    <Fact>
    Public Sub ScanSubtree_HonoursItsTimeBudgetAndSaysSo()
        Using tree As New TempTree()
            ' Enough folders that a zero-length budget is certain to cut the walk short.
            For i As Integer = 0 To 40
                tree.Dir("branch" & i.ToString(), "leaf")
            Next

            Dim clock As Stopwatch = Stopwatch.StartNew()
            Dim scan As FolderAccess.SubtreeScan =
                FolderAccess.ScanSubtree(tree.Root, writable:=False, budget:=TimeSpan.FromMilliseconds(1), forServiceAccount:=False)
            clock.Stop()

            ' The honest half: a truncated scan must never be reported as a clean one.
            Assert.False(scan.Completed)
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5),
                        "a budgeted scan must return promptly, took " & clock.Elapsed.ToString())
        End Using
    End Sub

    <Fact>
    Public Sub ScanSubtree_IgnoresPathsItCannotAct0n()
        ' A UNC root cannot be fixed by an ACL from here (LOCAL SERVICE authenticates
        ' anonymously on the network), and a missing folder is not an access problem.
        Assert.Empty(FolderAccess.ScanSubtree("\\server\share", writable:=False, forServiceAccount:=False).Blocked)
        Assert.Empty(FolderAccess.ScanSubtree("", writable:=False, forServiceAccount:=False).Blocked)
        Assert.Empty(FolderAccess.ScanSubtree(Path.Combine(Path.GetTempPath(), "fms-not-here-" & Guid.NewGuid().ToString("N")),
                                              writable:=False, forServiceAccount:=False).Blocked)
    End Sub

    ''' <summary>
    ''' The field incident, rebuilt exactly: a shared root that DOES carry an inheritable
    ''' LOCAL SERVICE grant, and a folder inside it whose ACL is protected and therefore
    ''' never received that grant. The root looks perfect to every root-level check, this
    ''' process (running as the user) can read the whole tree, and the phone still gets an
    ''' empty listing for the protected folder.
    '''
    ''' Both halves are asserted, because the asymmetry IS the bug: the walk judged by
    ''' this process's own access calls the tree clean, and only the DACL check for the
    ''' service account finds anything.
    ''' </summary>
    <Fact>
    Public Sub ScanSubtree_FindsAnInheritanceBreakBelowAGrantedRoot()
        Using tree As New TempTree()
            Dim readable As String = tree.Dir("readable")
            Dim broken As String = tree.Dir("Invoice_INVK1960", "pdf_images")
            If Not tree.GrantLocalService(tree.Root) Then Return   ' premise not buildable here
            If Not tree.BreakInheritance(broken) Then Return

            Dim asUser As FolderAccess.SubtreeScan =
                FolderAccess.ScanSubtree(tree.Root, writable:=False, forServiceAccount:=False)
            Dim asService As FolderAccess.SubtreeScan =
                FolderAccess.ScanSubtree(tree.Root, writable:=False, forServiceAccount:=True)

            ' The user sees nothing wrong - which is precisely why this went unnoticed.
            Assert.Empty(asUser.Blocked)

            ' The service account sees the one folder that lost the grant, and only it:
            ' the root and its readable sibling inherit the grant and are fine.
            Assert.Contains(broken, asService.Blocked)
            Assert.DoesNotContain(tree.Root, asService.Blocked)
            Assert.DoesNotContain(readable, asService.Blocked)
            ' Missing, not Denied - so it is offered to the elevated helper to fix.
            Assert.Contains(broken, asService.Grantable)
        End Using
    End Sub

    <Fact>
    Public Sub ScanRoots_SharesOneBudgetAcrossFolders()
        Using tree As New TempTree()
            Dim a As String = tree.Dir("one")
            Dim b As String = tree.Dir("two")
            tree.Dir("one", "deep")

            Dim roots As New List(Of ShareFolder) From {
                New ShareFolder With {.name = "one", .hostPath = a, .readOnly = True},
                New ShareFolder With {.name = "two", .hostPath = b, .readOnly = True}}

            Dim scan As FolderAccess.SubtreeScan = FolderAccess.ScanRoots(roots, forServiceAccount:=False)

            Assert.True(scan.Completed)
            Assert.True(scan.Scanned >= 3, "both roots should have been walked, got " & scan.Scanned)
        End Using
    End Sub

End Class
