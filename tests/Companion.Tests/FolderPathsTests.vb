Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Two path rules of the folder list (tickets 054 H-25, H-19):
'''
'''   * a blocked subfolder inherits the read/write level of the shared folder it sits
'''     under, matched on a path boundary and by the LONGEST such folder - a raw prefix
'''     match put "D:\Photos2\x" under a writable "D:\Photos" listed first and granted it
'''     read/write, a wider ACL than the share needs;
'''   * a mapped network drive letter is a network path for every purpose that matters:
'''     it looked local to each check, the elevated helper could not see the letter, and
'''     the service could never serve it.
''' </summary>
Public Class FolderPathsTests

    Private Shared Function Root(path As String, [readOnly] As Boolean) As ShareFolder
        Return New ShareFolder With {.hostPath = path, .readOnly = [readOnly]}
    End Function

    <Fact>
    Public Sub BlockedReadOnlySubset_MatchesOnAPathBoundary()
        Dim folders As New List(Of ShareFolder) From {Root("D:\Photos", False)}
        Dim ro As List(Of String) = MainWindow.BlockedReadOnlySubset(
            New List(Of String) From {"D:\Photos2\x"}, folders)
        ' The raw-prefix match would have granted read/write for a read-only root's child.
        Assert.Empty(ro)
        Dim rw As List(Of String) = MainWindow.BlockedReadOnlySubset(
            New List(Of String) From {"D:\Photos\y", "D:\Photos"}, folders)
        Assert.Empty(rw)
    End Sub

    <Fact>
    Public Sub BlockedReadOnlySubset_PrefersTheLongestContainingRoot()
        Dim folders As New List(Of ShareFolder) From {Root("D:\A", False), Root("D:\A\B", True)}
        Dim ro As List(Of String) = MainWindow.BlockedReadOnlySubset(
            New List(Of String) From {"D:\A\B\c"}, folders)
        Assert.Equal(New List(Of String) From {"D:\A\B\c"}, ro)
    End Sub

    <Fact>
    Public Sub BlockedReadOnlySubset_TrailingBackslashesDoNotChangeContainment()
        Dim folders As New List(Of ShareFolder) From {Root("D:\Photos\", True)}
        Dim ro As List(Of String) = MainWindow.BlockedReadOnlySubset(
            New List(Of String) From {"D:\Photos\x"}, folders)
        Assert.Equal(New List(Of String) From {"D:\Photos\x"}, ro)
    End Sub

    <Fact>
    Public Sub BlockedReadOnlySubset_UnrelatedPathsGetNoOwner()
        Dim folders As New List(Of ShareFolder) From {Root("D:\Photos", True)}
        Assert.Empty(MainWindow.BlockedReadOnlySubset(New List(Of String) From {"E:\Elsewhere\x"}, folders))
    End Sub

    ' --- mapped network drives (H-19) ---------------------------------------------

    <Fact>
    Public Sub IsNetworkPath_RecognizesUncAndMappedLetters()
        Assert.True(FolderAccess.IsNetworkPath("\\server\share"))
        Assert.True(FolderAccess.IsNetworkPath("//server/share"))
        ' The drive type is read through the probe, so the rule needs no network to test.
        Assert.True(FolderAccess.IsNetworkPath("Z:\data", Function(root) IO.DriveType.Network))
        Assert.True(FolderAccess.IsNetworkPath("Z:", Function(root) IO.DriveType.Network))
        Assert.False(FolderAccess.IsNetworkPath("C:\data", Function(root) IO.DriveType.Fixed))
        ' Not a drive path at all: not ours to call network, and not local-file either.
        Assert.False(FolderAccess.IsNetworkPath("relative\path", Function(root) IO.DriveType.Network))
        Assert.False(FolderAccess.IsNetworkPath(""))
        Assert.False(FolderAccess.IsNetworkPath(Nothing))
    End Sub

End Class
