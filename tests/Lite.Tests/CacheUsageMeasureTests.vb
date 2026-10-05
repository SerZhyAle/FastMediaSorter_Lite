#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports Xunit

''' <summary>
''' The Storage page's measurement of a cache directory
''' (130_SPECIFICATION_CACHE_MANAGEMENT_ON_STORAGE.md §3.3).
'''
''' Two behaviours are load-bearing and both need a real directory tree to be honest:
''' a cache that does not exist YET is an honest zero (nothing was ever stored - the
''' row must not frighten the user with "unknown"), while a directory that exists but
''' refuses to be listed must REACH the caller as an exception, because the row's answer
''' to that is "unknown", never a confident 0.0. Unreadable session subtrees must
''' propagate errors too, rather than disappearing from an apparently complete sum.
''' </summary>
Public Class CacheUsageMeasureTests
    Implements IDisposable

    Private ReadOnly root As String

    Public Sub New()
        root = Path.Combine(Path.GetTempPath(), "fms-cache-measure-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Directory.Delete(root, recursive:=True)
        Catch
        End Try
    End Sub

    Private Function Touch(relativePath As String, sizeBytes As Integer) As String
        Dim full As String = Path.Combine(root, relativePath)
        Directory.CreateDirectory(Path.GetDirectoryName(full))
        File.WriteAllBytes(full, New Byte(sizeBytes - 1) {})
        Return full
    End Function

    <Fact>
    Public Sub FlatBytes_sums_only_the_pattern_and_reports_a_missing_dir_as_zero()
        Touch("a.cache", 100)
        Touch("b.cache", 250)
        Touch("other.tmp", 5000)
        Assert.Equal(350L, CacheUsageMeasure.FlatBytes(root, "*.cache"))
        ' Nothing was ever cached: an honest zero, not an error.
        Assert.Equal(0L, CacheUsageMeasure.FlatBytes(Path.Combine(root, "gone"), "*.cache"))
    End Sub

    <Fact>
    Public Sub TreeBytes_sums_session_trees_recursively()
        Touch("123-abcd/00001.jpg", 400)
        Touch("123-abcd/sub/00002.jpg", 60)
        Touch("456-efgh/00001.mp4", 1000)
        Touch("stray.txt", 12345)
        ' Everything under the root counts - the whole root belongs to the cache.
        Assert.Equal(13805L, CacheUsageMeasure.TreeBytes(root))
        ' Never opened a resource: the root does not exist yet - zero.
        Assert.Equal(0L, CacheUsageMeasure.TreeBytes(Path.Combine(root, "gone")))
    End Sub

    <Fact>
    Public Sub A_directory_that_cannot_be_listed_throws_instead_of_lying()
        ' A file where the cache directory should be: the path exists, but it cannot be
        ' enumerated - the exact shape "denied disk" takes from the row's point of view.
        Dim blocker As String = Path.Combine(root, "blocked")
        File.WriteAllText(blocker, "not a directory")
        Assert.Throws(Of IOException)(Function() CacheUsageMeasure.FlatBytes(blocker, "*.cache"))
        Assert.Throws(Of IOException)(Function() CacheUsageMeasure.TreeBytes(blocker))
    End Sub

    <Fact>
    Public Sub An_unreadable_session_propagates_instead_of_reporting_a_partial_sum()
        Touch("readable.cache", 100)
        Touch("session/hidden.cache", 500)
        Dim session As New DirectoryInfo(Path.Combine(root, "session"))
        Dim original As DirectorySecurity = session.GetAccessControl()
        Dim denied As DirectorySecurity = session.GetAccessControl()
        denied.AddAccessRule(New FileSystemAccessRule(WindowsIdentity.GetCurrent().User,
                            FileSystemRights.ListDirectory, AccessControlType.Deny))
        Try
            session.SetAccessControl(denied)
            Assert.Throws(Of UnauthorizedAccessException)(Function() CacheUsageMeasure.TreeBytes(root))
            Assert.Throws(Of UnauthorizedAccessException)(Function() CacheUsageMeasure.FlatBytes(session.FullName, "*.cache"))
            Assert.Throws(Of UnauthorizedAccessException)(Sub() CacheMaintenance.ClearFiles(session.FullName, "*.cache"))
        Finally
            session.SetAccessControl(original)
        End Try
    End Sub

End Class
#End If
