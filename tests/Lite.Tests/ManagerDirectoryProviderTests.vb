#If Not NETFRAMEWORK Then
Option Strict On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports Xunit

''' <summary>
''' 257_SPECIFICATION_FILE_MANAGER_NAVIGATION_AND_PROVIDER_CONTEXT.md, slice N1: the directory side of a provider. The local
''' provider is proved against a temporary directory (empty is not missing, a file is not a directory, folders are listed and never
''' recursed into, a link is flagged, containment counts whole segments, only an empty directory under the root is removed); the
''' typed <see cref="DirectoryListing"/> is proved as values. The SFTP and SMB providers need a server and are proved by the
''' hand-run live scripts. Modern-only: the net48 leg compiles this to nothing.
''' </summary>
Public Class ManagerDirectoryProviderTests
    Implements IDisposable

    Private ReadOnly root As String

    Public Sub New()
        root = Path.Combine(Path.GetTempPath(), "fms-managerdir-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(root) Then Directory.Delete(root, True)
        Catch ex As Exception
            ' A temporary folder that will not go away is the temp cleaner's problem.
        End Try
    End Sub

    Private Shared Function NewProvider(Optional includeSubfolders As Boolean = False) As LocalProvider
        Return New LocalProvider(Function() includeSubfolders)
    End Function

    Private Function Make(ParamArray relative As String()) As String
        Dim full As String = Path.Combine(root, Path.Combine(relative))
        Directory.CreateDirectory(Path.GetDirectoryName(full))
        File.WriteAllText(full, "x")
        Return full
    End Function

    Private Function MakeDir(ParamArray relative As String()) As String
        Dim full As String = Path.Combine(root, Path.Combine(relative))
        Directory.CreateDirectory(full)
        Return full
    End Function

    ' ================================================================ ListChildren ====

    ''' <summary>AC257-2: a truly empty folder is a successful, complete, empty listing - not a failure, not a missing folder.</summary>
    <Fact>
    Public Sub An_empty_folder_lists_as_a_complete_empty_result()
        Dim empty As String = MakeDir("empty")
        Dim listing As DirectoryListing = NewProvider().ListChildren(empty, CancellationToken.None)
        Assert.Equal(DirectoryOutcome.Listed, listing.Outcome)
        Assert.True(listing.IsComplete)
        Assert.False(listing.IsTruncated)
        Assert.Empty(listing.Entries)
        Assert.Null(listing.Failure)
    End Sub

    <Fact>
    Public Sub A_missing_path_and_a_file_and_a_bad_path_are_three_different_answers_none_of_them_empty()
        Dim plainFile As String = Make("a.txt")
        Dim provider As LocalProvider = NewProvider()

        Dim missing As DirectoryListing = provider.ListChildren(Path.Combine(root, "nope"), CancellationToken.None)
        Assert.Equal(DirectoryOutcome.Missing, missing.Outcome)
        Assert.False(missing.IsComplete)

        Dim notDirectory As DirectoryListing = provider.ListChildren(plainFile, CancellationToken.None)
        Assert.Equal(DirectoryOutcome.NotADirectory, notDirectory.Outcome)

        Assert.Equal(DirectoryOutcome.Missing, provider.ListChildren("relative\path", CancellationToken.None).Outcome)
        Assert.Equal(DirectoryOutcome.Missing, provider.ListChildren(Path.Combine(root, "x", "..", "y"), CancellationToken.None).Outcome)
        Assert.Equal(DirectoryOutcome.Missing, provider.ListChildren("", CancellationToken.None).Outcome)
    End Sub

    <Fact>
    Public Sub A_cancelled_token_is_a_cancelled_listing_not_a_failure_of_the_folder()
        Dim folder As String = MakeDir("busy")
        Make("busy", "a.txt")
        Using cts As New CancellationTokenSource()
            cts.Cancel()
            Dim listing As DirectoryListing = NewProvider().ListChildren(folder, cts.Token)
            Assert.Equal(DirectoryOutcome.Cancelled, listing.Outcome)
            Assert.Equal(ResourceFailureKind.Cancelled, listing.Failure.Kind)
        End Using
    End Sub

    ''' <summary>Folders are listed with the files, one level only - whatever the viewer's "include subfolders" preference says.</summary>
    <Fact>
    Public Sub Immediate_children_include_folders_and_never_recurse()
        MakeDir("pics")
        Make("pics", "deep", "inner.jpg")
        Make("a.jpg")
        Make("b.txt")

        For Each include As Boolean In {False, True}
            Dim listing As DirectoryListing = NewProvider(include).ListChildren(root, CancellationToken.None)
            Assert.Equal(DirectoryOutcome.Listed, listing.Outcome)
            Dim names As String() = listing.Entries.Select(Function(e) e.Name).OrderBy(Function(n) n, StringComparer.Ordinal).ToArray()
            Assert.Equal(New String() {"a.jpg", "b.txt", "pics"}, names)
            Dim folder As ResourceEntry = listing.Entries.Single(Function(e) e.Name = "pics")
            Assert.True(folder.IsDirectory)
            Assert.False(folder.IsLink)
            Assert.Equal(Path.Combine(root, "pics"), folder.FullPath)
            Dim leaf As ResourceEntry = listing.Entries.Single(Function(e) e.Name = "a.jpg")
            Assert.False(leaf.IsDirectory)
            Assert.Equal(1L, leaf.Length)
        Next
    End Sub

    ''' <summary>A symlink is listed as a link and never followed. Creating one needs a privilege the test account may lack.</summary>
    <Fact>
    Public Sub A_directory_link_is_flagged_as_a_link()
        Dim target As String = MakeDir("target")
        Dim link As String = Path.Combine(root, "jump")
        Try
            Directory.CreateSymbolicLink(link, target)
        Catch ex As Exception When TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is PlatformNotSupportedException
            Return                                                        ' no symlink privilege on this account: nothing to prove here
        End Try

        Dim listing As DirectoryListing = NewProvider().ListChildren(root, CancellationToken.None)
        Dim entry As ResourceEntry = listing.Entries.Single(Function(e) e.Name = "jump")
        Assert.True(entry.IsLink)
        Assert.False(listing.Entries.Single(Function(e) e.Name = "target").IsLink)
        Assert.True(ManagerEntry.FromResource(ManagerPlace.ForDrive(Path.GetPathRoot(root), "drive"), entry).IsLink)
        Assert.False(ManagerEntry.FromResource(ManagerPlace.ForDrive(Path.GetPathRoot(root), "drive"), entry).CanEnter)
    End Sub

    ' ======================================================================== stat ====

    <Fact>
    Public Sub Stat_answers_for_a_directory_and_a_file_and_says_missing_for_neither()
        Dim plainFile As String = Make("s", "f.txt")
        Dim provider As LocalProvider = NewProvider()

        Dim folderStat As StatResult = provider.StatEntry(Path.Combine(root, "s"))
        Assert.True(folderStat.Exists)
        Assert.True(folderStat.Entry.IsDirectory)

        Dim fileStat As StatResult = provider.StatEntry(plainFile)
        Assert.True(fileStat.Exists)
        Assert.False(fileStat.Entry.IsDirectory)

        Assert.True(provider.StatEntry(Path.Combine(root, "gone")).IsDefinitelyMissing)
        Assert.True(provider.StatEntry("relative").IsDefinitelyMissing)
    End Sub

    ' ================================================================ containment ====

    <Fact>
    Public Sub Containment_counts_whole_segments_and_refuses_traversal()
        Dim provider As LocalProvider = NewProvider()
        Dim a As String = Path.Combine(root, "root")
        Assert.True(provider.IsWithinRoot(a, a))
        Assert.True(provider.IsWithinRoot(a, Path.Combine(a, "sub", "deep")))
        Assert.True(provider.IsWithinRoot(a.ToUpperInvariant(), Path.Combine(a, "sub")))             ' Windows paths ignore case
        Assert.False(provider.IsWithinRoot(a, a & "X"))                                              ' a sibling that shares the prefix
        Assert.False(provider.IsWithinRoot(a, Path.Combine(root, "other")))
        Assert.False(provider.IsWithinRoot(a, Path.Combine(a, "sub", "..", "..", "other")))
        Assert.False(provider.IsWithinRoot(a, Path.Combine(a, "..")))
    End Sub

    <Fact>
    Public Sub Parent_and_equality_follow_the_windows_rules()
        Dim provider As LocalProvider = NewProvider()
        Dim child As String = Path.Combine(root, "p", "q")
        Assert.Equal(Path.Combine(root, "p"), provider.ParentOf(child))
        Assert.Null(provider.ParentOf(Path.GetPathRoot(root)))
        Assert.True(provider.PathsEqual(child, child.ToUpperInvariant() & "\"))
        Assert.False(provider.PathsEqual(child, Path.Combine(root, "p")))
        Assert.False(provider.PathsEqual(child, "relative"))
    End Sub

    ' ============================================================ RemoveEmptyDirectory ====

    <Fact>
    Public Sub Only_an_empty_directory_under_the_root_is_removed()
        Dim provider As LocalProvider = NewProvider()
        Dim parent As String = MakeDir("area")
        Dim empty As String = MakeDir("area", "empty")
        Dim full As String = MakeDir("area", "full")
        Make("area", "full", "keep.txt")
        Dim plainFile As String = Make("area", "file.txt")

        Assert.Equal(DirectoryRemoval.NotEmpty, provider.RemoveEmptyDirectory(parent, full))
        Assert.True(Directory.Exists(full))
        Assert.True(File.Exists(Path.Combine(full, "keep.txt")))                                     ' never removes content

        Assert.Equal(DirectoryRemoval.Refused, provider.RemoveEmptyDirectory(parent, parent))        ' the root itself
        Assert.Equal(DirectoryRemoval.Refused, provider.RemoveEmptyDirectory(parent, root))          ' outside it
        Assert.Equal(DirectoryRemoval.Refused, provider.RemoveEmptyDirectory(parent, Path.Combine(parent, "..", "area", "empty")))
        Assert.Equal(DirectoryRemoval.Refused, provider.RemoveEmptyDirectory(parent, plainFile))          ' not a directory
        Assert.Equal(DirectoryRemoval.Missing, provider.RemoveEmptyDirectory(parent, Path.Combine(parent, "ghost")))
        Assert.True(Directory.Exists(parent))

        Assert.Equal(DirectoryRemoval.Removed, provider.RemoveEmptyDirectory(parent, empty))
        Assert.False(Directory.Exists(empty))
    End Sub

    ' ============================================================ DirectoryListing ====

    <Fact>
    Public Sub A_listing_is_complete_only_when_it_was_read_to_its_end()
        Dim entries As New List(Of ResourceEntry) From {New ResourceEntry With {.Name = "a", .FullPath = "/a"}}
        Assert.True(DirectoryListing.Listed(entries).IsComplete)
        Assert.False(DirectoryListing.Listed(entries, truncated:=True).IsComplete)
        Assert.True(DirectoryListing.Listed(entries, truncated:=True).IsTruncated)
        Assert.False(DirectoryListing.Missing().IsComplete)
        Assert.False(DirectoryListing.Denied().IsComplete)
        Assert.Null(DirectoryListing.Listed(Nothing).Failure)
        Assert.Empty(DirectoryListing.Listed(Nothing).Entries)
    End Sub

    ''' <summary>A classified failure reads as the outcome it stands for; nothing a server answers with becomes "empty".</summary>
    <Fact>
    Public Sub A_failure_is_read_as_the_outcome_it_stands_for()
        Assert.Equal(DirectoryOutcome.Cancelled, DirectoryListing.FromFailure(New ResourceFailure(ResourceFailureKind.Cancelled)).Outcome)
        Assert.Equal(DirectoryOutcome.Denied, DirectoryListing.FromFailure(New ResourceFailure(ResourceFailureKind.Auth)).Outcome)
        Assert.Equal(DirectoryOutcome.Missing, DirectoryListing.FromFailure(New ResourceFailure(ResourceFailureKind.SftpStatus, 2)).Outcome)
        Assert.Equal(DirectoryOutcome.Denied, DirectoryListing.FromFailure(New ResourceFailure(ResourceFailureKind.SftpStatus, 3)).Outcome)
        Assert.Equal(DirectoryOutcome.Failed, DirectoryListing.FromFailure(New ResourceFailure(ResourceFailureKind.SftpStatus, 4)).Outcome)
        Assert.Equal(DirectoryOutcome.Failed, DirectoryListing.FromFailure(New ResourceFailure(ResourceFailureKind.Transport)).Outcome)
        Assert.Equal(DirectoryOutcome.Failed, DirectoryListing.FromFailure(New ResourceFailure(ResourceFailureKind.Timeout)).Outcome)
        Assert.Equal(DirectoryOutcome.Failed, DirectoryListing.FromFailure(Nothing).Outcome)

        For Each listing As DirectoryListing In {DirectoryListing.FromFailure(New ResourceFailure(ResourceFailureKind.Transport)),
                                                 DirectoryListing.FromFailure(New ResourceFailure(ResourceFailureKind.Auth)),
                                                 DirectoryListing.FromFailure(New ResourceFailure(ResourceFailureKind.SftpStatus, 2))}
            Assert.Empty(listing.Entries)
            Assert.False(listing.IsComplete)
        Next
    End Sub

    <Fact>
    Public Sub Every_directory_outcome_is_a_different_value()
        Dim values As DirectoryOutcome() = [Enum].GetValues(GetType(DirectoryOutcome)).Cast(Of DirectoryOutcome)().ToArray()
        Assert.Equal(values.Length, values.Select(Function(v) CInt(v)).Distinct().Count())
        Assert.Equal(6, values.Length)
    End Sub

End Class
#End If
