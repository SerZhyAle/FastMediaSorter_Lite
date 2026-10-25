#If Not NETFRAMEWORK Then
Option Strict On

Imports System
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' 257_SPECIFICATION_FILE_MANAGER_NAVIGATION_AND_PROVIDER_CONTEXT.md, slices N2/N3: the state of the F3 panel's file manager
''' mode that is not a control. History, the rule "a failed navigation changes nothing", the generation guard that lets only the
''' newest listing through, the address bar's meaning, row identity and the folder-first order are all functions of values and
''' of a fake provider - no window, no disk, no server. Modern-only, like the feature: the net48 leg compiles this to nothing.
''' </summary>
Public Class ManagerNavigationTests

    Private Shared ReadOnly Drive As ManagerPlace = ManagerPlace.ForDrive("C:\", "C:")

    Private Shared ReadOnly Server As ManagerPlace = ManagerPlace.ForResource(New ResourceDescriptor With {
        .Id = "r1", .Kind = ResourceKind.Sftp, .DisplayName = "NAS", .Host = "nas.example", .Port = 2222,
        .UserName = "kim", .RootPath = "/MOV"})

    Private Shared Function Loc(path As String) As ManagerLocation
        Return New ManagerLocation(Drive, path)
    End Function

    Private Shared Function Remote(path As String) As ManagerLocation
        Return New ManagerLocation(Server, path)
    End Function

    Private Shared Function Result(intent As ManagerIntent, target As ManagerLocation,
                                   Optional status As ManagerStatusKind = ManagerStatusKind.Ok) As ManagerListingResult
        Return New ManagerListingResult(1, intent, target, status, Nothing, Nothing)
    End Function

    Private Shared Function Res(name As String, fullPath As String, isDirectory As Boolean,
                                Optional isLink As Boolean = False, Optional length As Long = 0) As ResourceEntry
        Return New ResourceEntry With {.Name = name, .FullPath = fullPath, .IsDirectory = isDirectory, .IsLink = isLink,
                                       .Length = length, .LastWriteUtc = New DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)}
    End Function

    Private Shared Sub WaitFor(task As Task)
        Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "the listing did not finish")
    End Sub

    ' ====================================================================== history ====

    ''' <summary>AC257-1: A -> B -> Back -> Forward -> Up, each step landing where the history says.</summary>
    <Fact>
    Public Sub History_walks_A_B_back_forward_up()
        Dim nav As New ManagerNavigation()
        Assert.Equal(ManagerApply.Moved, nav.Apply(Result(ManagerIntent.Start, Loc("C:\A"))))
        Assert.False(nav.CanBack)
        Assert.False(nav.CanForward)
        Assert.True(nav.CanGoUp)

        Assert.Equal(ManagerApply.Moved, nav.Apply(Result(ManagerIntent.Go, Loc("C:\A\B"))))
        Assert.True(nav.CanBack)
        Assert.True(nav.PeekBack().SameAs(Loc("C:\A")))

        Assert.Equal(ManagerApply.Moved, nav.Apply(Result(ManagerIntent.Back, Loc("C:\A"))))
        Assert.True(nav.Current.SameAs(Loc("C:\A")))
        Assert.False(nav.CanBack)
        Assert.True(nav.CanForward)
        Assert.True(nav.PeekForward().SameAs(Loc("C:\A\B")))

        Assert.Equal(ManagerApply.Moved, nav.Apply(Result(ManagerIntent.Forward, Loc("C:\A\B"))))
        Assert.True(nav.Current.SameAs(Loc("C:\A\B")))
        Assert.False(nav.CanForward)

        Assert.True(nav.ParentTarget().SameAs(Loc("C:\A")))
        Assert.Equal(ManagerApply.Moved, nav.Apply(Result(ManagerIntent.Up, nav.ParentTarget())))
        Assert.True(nav.Current.SameAs(Loc("C:\A")))
        Assert.Equal(2, nav.BackCount)                                   ' A and B: Up is a navigation like any other
        Assert.False(nav.CanForward)                                     ' and a new navigation drops the forward branch
    End Sub

    ''' <summary>AC257-2: denied, missing, cancelled, disconnected, outside the root - none of them moves anything.</summary>
    <Fact>
    Public Sub A_listing_that_did_not_succeed_changes_nothing()
        Dim nav As New ManagerNavigation()
        nav.Apply(Result(ManagerIntent.Start, Loc("C:\A")))
        nav.Apply(Result(ManagerIntent.Go, Loc("C:\A\B")))
        nav.RememberSelection({"x", "y"}, "y")
        Dim generation As Integer = nav.RefreshGeneration

        Dim failures As IEnumerable(Of ManagerStatusKind) = [Enum].GetValues(GetType(ManagerStatusKind)).Cast(Of ManagerStatusKind)().
            Where(Function(s) s <> ManagerStatusKind.Ok AndAlso s <> ManagerStatusKind.Truncated)
        Assert.NotEmpty(failures)
        For Each status As ManagerStatusKind In failures
            For Each intent As ManagerIntent In [Enum].GetValues(GetType(ManagerIntent)).Cast(Of ManagerIntent)()
                Assert.Equal(ManagerApply.Kept, nav.Apply(Result(intent, Loc("C:\Elsewhere"), status)))
            Next
        Next
        Assert.True(nav.Current.SameAs(Loc("C:\A\B")))
        Assert.Equal(1, nav.BackCount)
        Assert.Equal(0, nav.ForwardCount)
        Assert.Equal(generation, nav.RefreshGeneration)
        Assert.Equal(New String() {"x", "y"}, nav.SelectedIds.OrderBy(Function(s) s).ToArray())
        Assert.Equal("y", nav.FocusedId)
        Assert.Equal(ManagerApply.Kept, nav.Apply(Nothing))
    End Sub

    <Fact>
    Public Sub Every_failure_has_its_own_status_and_an_empty_folder_is_not_one_of_them()
        Dim statuses As ManagerStatusKind() = {ManagerStatusKind.Missing, ManagerStatusKind.NotADirectory, ManagerStatusKind.Denied,
                                               ManagerStatusKind.Disconnected, ManagerStatusKind.Cancelled, ManagerStatusKind.OutsideRoot,
                                               ManagerStatusKind.SignInNeeded, ManagerStatusKind.HostKeyUnknown, ManagerStatusKind.Unavailable}
        Assert.Equal(statuses.Length, statuses.Distinct().Count())
        For Each status As ManagerStatusKind In statuses
            Assert.False(Result(ManagerIntent.Go, Loc("C:\A"), status).IsListed, status.ToString())
        Next
        Assert.True(Result(ManagerIntent.Go, Loc("C:\A"), ManagerStatusKind.Ok).IsListed)              ' an empty folder is a success
        Assert.True(Result(ManagerIntent.Go, Loc("C:\A"), ManagerStatusKind.Truncated).IsTruncated)
        Assert.True(Result(ManagerIntent.Go, Loc("C:\A"), ManagerStatusKind.Truncated).IsListed)
    End Sub

    <Fact>
    Public Sub A_truncated_listing_still_moves_the_manager_and_says_it_is_partial()
        Dim nav As New ManagerNavigation()
        nav.Apply(Result(ManagerIntent.Start, Loc("C:\A")))
        Assert.Equal(ManagerApply.Moved, nav.Apply(Result(ManagerIntent.Go, Loc("C:\A\Big"), ManagerStatusKind.Truncated)))
        Assert.True(nav.Current.SameAs(Loc("C:\A\Big")))
    End Sub

    <Fact>
    Public Sub Back_and_forward_commit_only_where_the_history_leads()
        Dim nav As New ManagerNavigation()
        nav.Apply(Result(ManagerIntent.Start, Loc("C:\A")))
        nav.Apply(Result(ManagerIntent.Go, Loc("C:\B")))
        Assert.Equal(ManagerApply.Kept, nav.Apply(Result(ManagerIntent.Back, Loc("C:\Z"))))     ' not where Back leads
        Assert.Equal(ManagerApply.Kept, nav.Apply(Result(ManagerIntent.Forward, Loc("C:\A"))))  ' nothing to go forward to
        Assert.True(nav.Current.SameAs(Loc("C:\B")))
        Assert.Equal(1, nav.BackCount)
    End Sub

    <Fact>
    Public Sub Retyping_the_current_folder_is_a_reload_not_a_history_entry()
        Dim nav As New ManagerNavigation()
        nav.Apply(Result(ManagerIntent.Start, Loc("C:\A")))
        Assert.Equal(ManagerApply.Reloaded, nav.Apply(Result(ManagerIntent.Go, Loc("c:\a\"))))
        Assert.Equal(0, nav.BackCount)
    End Sub

    <Fact>
    Public Sub Refresh_reloads_the_folder_it_is_in_and_ignores_one_it_has_left()
        Dim nav As New ManagerNavigation()
        nav.Apply(Result(ManagerIntent.Start, Loc("C:\A")))
        nav.Apply(Result(ManagerIntent.Go, Loc("C:\B")))
        Assert.Equal(ManagerApply.Reloaded, nav.Apply(Result(ManagerIntent.Refresh, Loc("C:\B"))))
        Assert.Equal(ManagerApply.Kept, nav.Apply(Result(ManagerIntent.Refresh, Loc("C:\A"))))
        Assert.Equal(1, nav.BackCount)
        Assert.True(nav.Current.SameAs(Loc("C:\B")))
    End Sub

    ''' <summary>The newest 100 successful locations are kept; the oldest fall off.</summary>
    <Fact>
    Public Sub History_keeps_the_newest_hundred()
        Dim nav As New ManagerNavigation()
        nav.Apply(Result(ManagerIntent.Start, Loc("C:\d0")))
        For i As Integer = 1 To 150
            nav.Apply(Result(ManagerIntent.Go, Loc("C:\d" & i.ToString(Globalization.CultureInfo.InvariantCulture))))
        Next
        Assert.Equal(ManagerNavigation.MaxHistory, nav.BackCount)
        Dim steps As Integer = 0
        While nav.CanBack
            Assert.Equal(ManagerApply.Moved, nav.Apply(Result(ManagerIntent.Back, nav.PeekBack())))
            steps += 1
        End While
        Assert.Equal(100, steps)
        Assert.True(nav.Current.SameAs(Loc("C:\d50")))                   ' d0..d49 fell off
    End Sub

    ' ==================================================================== selection ====

    <Fact>
    Public Sub Moving_to_another_folder_clears_the_selection_and_a_refresh_keeps_what_survives()
        Dim nav As New ManagerNavigation()
        nav.Apply(Result(ManagerIntent.Start, Loc("C:\A")))
        nav.RememberSelection({"a", "b", "c"}, "b")
        Assert.Equal(ManagerApply.Reloaded, nav.Apply(Result(ManagerIntent.Refresh, Loc("C:\A"))))
        Assert.Equal(3, nav.SelectedIds.Count)                           ' a refresh does not clear it..

        Dim kept As List(Of String) = nav.Reconcile({"b", "c", "d"})     ' ..it reconciles by identity: a left, d is new
        Assert.Equal(New String() {"b", "c"}, kept.OrderBy(Function(s) s).ToArray())
        Assert.DoesNotContain("d", nav.SelectedIds)
        Assert.Equal("b", nav.FocusedId)

        nav.Reconcile({"c"})
        Assert.Null(nav.FocusedId)                                       ' the focused entry is gone

        Assert.Equal(ManagerApply.Moved, nav.Apply(Result(ManagerIntent.Go, Loc("C:\B"))))
        Assert.Empty(nav.SelectedIds)
    End Sub

    ''' <summary>AC257-3: two remote names that differ only by case are two rows, and each can be selected alone.</summary>
    <Fact>
    Public Sub Ordinal_selection_tells_names_that_differ_only_by_case_apart()
        Dim lower As String = ManagerEntry.IdOf(Server, "/MOV/a.jpg")
        Dim upper As String = ManagerEntry.IdOf(Server, "/MOV/A.jpg")
        Dim model As New SelectionModel(StringComparer.Ordinal)
        model.SetItems({lower, upper})

        Assert.Equal(0, model.IndexOf(lower))
        Assert.Equal(1, model.IndexOf(upper))
        model.Click(0)
        Assert.True(model.IsSelectedPath(lower))
        Assert.False(model.IsSelectedPath(upper))
        model.SelectAll()
        Assert.Equal(2, model.SelectedPaths().Count)
    End Sub

    ' ====================================================================== entries ====

    <Fact>
    Public Sub A_server_entry_has_an_exact_opaque_id_and_no_disk_path()
        Dim entry As ManagerEntry = ManagerEntry.FromResource(Server, Res("a.jpg", "/MOV/a.jpg", isDirectory:=False, length:=10))
        Assert.Equal("res:r1|/MOV/a.jpg", entry.Id)
        Assert.NotEqual(entry.Id, ManagerEntry.FromResource(Server, Res("A.jpg", "/MOV/A.jpg", isDirectory:=False)).Id)
        Assert.Equal("", entry.DiskPath)                                  ' nothing is downloaded to browse

        Dim row As PanelFileRow = entry.ToRow()
        Assert.Equal(entry.Id, row.Identity)
        Assert.Equal("", row.Path)
        Assert.False(row.AllowThumbnail)                                  ' no bytes here: no thumbnail request
        Assert.Equal("a.jpg", row.Name)
    End Sub

    <Fact>
    Public Sub A_local_media_file_is_keyed_by_its_entry_id_and_may_have_a_thumbnail()
        Dim entry As ManagerEntry = ManagerEntry.FromResource(Drive, Res("a.jpg", "C:\in\a.jpg", isDirectory:=False, length:=10))
        Dim row As PanelFileRow = entry.ToRow()
        Assert.Equal(entry.Id, row.Identity)
        Assert.NotEqual(row.Path, row.Identity)
        Assert.Equal("C:\in\a.jpg", row.Path)
        Assert.True(row.AllowThumbnail)
        Assert.Equal(10L, row.SizeBytes)
    End Sub

    <Fact>
    Public Sub A_folder_has_no_size_no_thumbnail_and_a_link_is_never_entered()
        Dim folder As ManagerEntry = ManagerEntry.FromResource(Drive, Res("pics", "C:\in\pics", isDirectory:=True, length:=999))
        Assert.Equal(-1L, folder.SizeBytes)
        Assert.False(folder.SizeKnown)
        Assert.True(folder.CanEnter)
        Assert.False(folder.ToRow().AllowThumbnail)
        Assert.True(folder.ToRow().IsDirectory)

        Dim link As ManagerEntry = ManagerEntry.FromResource(Drive, Res("jump", "C:\in\jump", isDirectory:=True, isLink:=True))
        Assert.True(link.IsLink)
        Assert.False(link.CanEnter)
        Assert.True(link.ToRow().IsLink)
    End Sub

    <Fact>
    Public Sub Folders_come_before_files_in_either_direction_and_names_sort_naturally()
        Dim rows As New List(Of PanelFileRow) From {
            ManagerEntry.FromResource(Drive, Res("b.jpg", "C:\in\b.jpg", False, length:=5)).ToRow(),
            ManagerEntry.FromResource(Drive, Res("IMG_10", "C:\in\IMG_10", True)).ToRow(),
            ManagerEntry.FromResource(Drive, Res("a.jpg", "C:\in\a.jpg", False, length:=9)).ToRow(),
            ManagerEntry.FromResource(Drive, Res("IMG_2", "C:\in\IMG_2", True)).ToRow()}

        Assert.Equal(New String() {"IMG_2", "IMG_10", "a.jpg", "b.jpg"},
                     PanelSort.Order(rows, PanelSortColumn.Name, descending:=False).Select(Function(r) r.Name).ToArray())
        Assert.Equal(New String() {"IMG_10", "IMG_2", "b.jpg", "a.jpg"},
                     PanelSort.Order(rows, PanelSortColumn.Name, descending:=True).Select(Function(r) r.Name).ToArray())
        ' A folder has no size to sort by: by size the folders fall back to their names, the files go by size.
        Assert.Equal(New String() {"IMG_2", "IMG_10", "b.jpg", "a.jpg"},
                     PanelSort.Order(rows, PanelSortColumn.Size, descending:=False).Select(Function(r) r.Name).ToArray())
    End Sub

    ' ================================================================== path rules ====

    <Fact>
    Public Sub Local_paths_are_absolute_and_free_of_traversal()
        Assert.Equal("C:\a\b", ManagerPathRules.NormalizeLocal("C:\a\b\"))
        Assert.Equal("C:\a\b", ManagerPathRules.NormalizeLocal("C:/a/b"))
        Assert.Equal("C:\", ManagerPathRules.NormalizeLocal("C:\"))
        For Each bad As String In {Nothing, "", "   ", "a\b", "C:a", "C:\a\..\b", "..\x", "C:\a" & ChrW(0) & "b"}
            Assert.Null(ManagerPathRules.NormalizeLocal(bad))
        Next
    End Sub

    <Fact>
    Public Sub Remote_paths_are_canonical_posix_without_traversal()
        Assert.Equal("/MOV/a", ManagerPathRules.NormalizeRemote("/MOV/a/"))
        Assert.Equal("/", ManagerPathRules.NormalizeRemote("/"))
        For Each bad As String In {Nothing, "", "MOV/a", "/MOV/../x", "/MOV/./x", "/MOV\a", "/MOV//a"}
            Assert.Null(ManagerPathRules.NormalizeRemote(bad))
        Next
    End Sub

    <Fact>
    Public Sub Containment_counts_whole_segments_and_follows_the_family_s_case_rule()
        Assert.True(ManagerPathRules.Contains(ResourceKind.Local, "C:\Media", "C:\media\x"))
        Assert.True(ManagerPathRules.Contains(ResourceKind.Local, "C:\Media", "C:\Media"))
        Assert.False(ManagerPathRules.Contains(ResourceKind.Local, "C:\Media", "C:\Media2"))
        Assert.False(ManagerPathRules.Contains(ResourceKind.Local, "C:\Media", "C:\Media\..\Other"))

        Assert.True(ManagerPathRules.Contains(ResourceKind.Sftp, "/MOV", "/MOV/x"))
        Assert.False(ManagerPathRules.Contains(ResourceKind.Sftp, "/MOV", "/MOV2"))
        Assert.False(ManagerPathRules.Contains(ResourceKind.Sftp, "/MOV", "/mov/x"))                ' a remote path is never lowercased
        Assert.True(ManagerPathRules.Contains(ResourceKind.Sftp, "/", "/anything"))

        Assert.True(ManagerPathRules.AreEqual(ResourceKind.Local, "C:\Media", "c:\MEDIA\"))
        Assert.False(ManagerPathRules.AreEqual(ResourceKind.Sftp, "/MOV/a", "/MOV/A"))
        Assert.True(ManagerPathRules.AreEqual(ResourceKind.Sftp, "/MOV/a/", "/MOV/a"))
        Assert.False(ManagerPathRules.AreEqual(ResourceKind.Local, "C:\a\..\b", "C:\b"))             ' cannot be normalised: never equal
    End Sub

    <Fact>
    Public Sub Parents_stop_at_the_root_of_the_file_system_and_names_join_in_the_family_s_spelling()
        Assert.Equal("C:\a", ManagerPathRules.ParentOf(ResourceKind.Local, "C:\a\b"))
        Assert.Equal("C:\", ManagerPathRules.ParentOf(ResourceKind.Local, "C:\a"))
        Assert.Null(ManagerPathRules.ParentOf(ResourceKind.Local, "C:\"))
        Assert.Equal("/a", ManagerPathRules.ParentOf(ResourceKind.Sftp, "/a/b"))
        Assert.Equal("/", ManagerPathRules.ParentOf(ResourceKind.Sftp, "/a"))
        Assert.Null(ManagerPathRules.ParentOf(ResourceKind.Sftp, "/"))

        Assert.Equal("/a", ManagerPathRules.Combine(ResourceKind.Sftp, "/", "a"))
        Assert.Equal("/MOV/a", ManagerPathRules.Combine(ResourceKind.Sftp, "/MOV/", "a"))
        Assert.Equal("C:\x\a.jpg", ManagerPathRules.Combine(ResourceKind.Local, "C:\x", "a.jpg"))
        Assert.Equal("a b.jpg", ManagerPathRules.LeafName(ResourceKind.Sftp, "/MOV/a b.jpg"))
        Assert.Equal("y", ManagerPathRules.LeafName(ResourceKind.Local, "C:\x\y\"))
    End Sub

    ''' <summary>Up stops at the configured root of a resource and at the drive root of a disk.</summary>
    <Fact>
    Public Sub Up_stops_at_the_root_of_the_place()
        Assert.True(Loc("C:\").IsAtRoot)
        Assert.Null(Loc("C:\").Parent())
        Assert.Equal("C:\", Loc("C:\A").Parent().DirectoryPath)

        Assert.True(Remote("/MOV").IsAtRoot)
        Assert.Null(Remote("/MOV").Parent())                              ' the root of the resource, not the server's /
        Assert.Equal("/MOV", Remote("/MOV/a").Parent().DirectoryPath)
        Assert.Equal("/MOV/a", Remote("/MOV/a/b").Parent().DirectoryPath)

        Dim nav As New ManagerNavigation()
        nav.Apply(Result(ManagerIntent.Start, Remote("/MOV")))
        Assert.False(nav.CanGoUp)
        Assert.Null(nav.ParentTarget())
    End Sub

    ' ===================================================================== address ====

    Private Shared Function Resources() As List(Of ResourceDescriptor)
        Return New List(Of ResourceDescriptor) From {
            New ResourceDescriptor With {.Id = "r1", .Kind = ResourceKind.Sftp, .DisplayName = "NAS", .Host = "nas.example", .Port = 2222,
                                         .UserName = "kim", .RootPath = "/MOV"},
            New ResourceDescriptor With {.Id = "r2", .Kind = ResourceKind.Sftp, .DisplayName = "Default port", .Host = "box.example", .Port = 0,
                                         .UserName = "lee", .RootPath = "/"},
            New ResourceDescriptor With {.Id = "r3", .Kind = ResourceKind.Smb, .DisplayName = "Share", .RootPath = "\\nas\photos"}}
    End Function

    <Fact>
    Public Sub Places_list_the_drives_first_then_the_remote_resources_without_duplicates()
        Dim places As List(Of ManagerPlace) = ManagerAddress.BuildPlaces({"C:\", "D:\", "C:\", "", Nothing}, Resources())
        Assert.Equal(New String() {"local:C:\", "local:D:\", "res:r1", "res:r2", "res:r3"}, places.Select(Function(p) p.Id).ToArray())
        Assert.Equal("C:", places(0).Title)
        Assert.Equal("NAS", places(2).Title)
        Assert.True(places(2).NeedsConnection)
        Assert.False(places(0).NeedsConnection)

        Dim withLocal As List(Of ResourceDescriptor) = Resources()
        withLocal.Add(New ResourceDescriptor With {.Id = "r9", .Kind = ResourceKind.Local, .RootPath = "E:\x"})   ' a plain folder is not a place
        withLocal.Add(New ResourceDescriptor With {.Id = "", .Kind = ResourceKind.Sftp, .RootPath = "/x"})        ' no id: not addressable
        Assert.Equal(5, ManagerAddress.BuildPlaces({"C:\", "D:\"}, withLocal).Count)
        Assert.Empty(ManagerAddress.BuildPlaces(Nothing, Nothing))
    End Sub

    <Fact>
    Public Sub A_local_path_resolves_to_its_drive_and_is_normalised()
        Dim typed As ManagerAddressResult = ManagerAddress.Resolve("  ""C:\Users\kim\Pictures\""  ", Nothing)
        Assert.Equal(ManagerAddressKind.Ok, typed.Kind)
        Assert.Equal("local:C:\", typed.Location.Place.Id)
        Assert.Equal("C:\Users\kim\Pictures", typed.Location.DirectoryPath)
        Assert.Equal(ResourceKind.Local, typed.Location.Place.Kind)

        Dim root As ManagerAddressResult = ManagerAddress.Resolve("D:\", Resources())
        Assert.Equal(ManagerAddressKind.Ok, root.Kind)
        Assert.True(root.Location.IsAtRoot)
    End Sub

    <Fact>
    Public Sub Text_that_is_not_an_absolute_path_is_invalid_and_nothing_is_made_of_it()
        For Each bad As String In {Nothing, "", "   ", """""", "pictures", "C:", "..\x", "C:\a\..\b", "C:\a" & ChrW(0), "sftp://kim@nas.example:2222/MOV/../x"}
            Dim answer As ManagerAddressResult = ManagerAddress.Resolve(bad, Resources())
            Assert.Equal(ManagerAddressKind.Invalid, answer.Kind)
            Assert.Null(answer.Location)
        Next
    End Sub

    <Fact>
    Public Sub A_locator_names_a_configured_resource_or_is_refused()
        Dim known As ManagerAddressResult = ManagerAddress.Resolve("sftp://kim@nas.example:2222/MOV/trip", Resources())
        Assert.Equal(ManagerAddressKind.Ok, known.Kind)
        Assert.Equal("res:r1", known.Location.Place.Id)
        Assert.Equal("/MOV/trip", known.Location.DirectoryPath)

        ' The default port, the host in any case, no user: still that resource. The remote path keeps its case.
        Dim loose As ManagerAddressResult = ManagerAddress.Resolve("sftp://BOX.example/Docs/Mixed", Resources())
        Assert.Equal(ManagerAddressKind.Ok, loose.Kind)
        Assert.Equal("res:r2", loose.Location.Place.Id)
        Assert.Equal("/Docs/Mixed", loose.Location.DirectoryPath)

        Assert.Equal(ManagerAddressKind.UnknownResource, ManagerAddress.Resolve("sftp://kim@nas.example:2200/MOV", Resources()).Kind)   ' other port
        Assert.Equal(ManagerAddressKind.UnknownResource, ManagerAddress.Resolve("sftp://eve@nas.example:2222/MOV", Resources()).Kind)   ' other user
        Assert.Equal(ManagerAddressKind.UnknownResource, ManagerAddress.Resolve("sftp://kim@stranger.example/x", Resources()).Kind)
        Assert.Equal(ManagerAddressKind.UnknownResource, ManagerAddress.Resolve("sftp://kim@nas.example:2222/MOV", Nothing).Kind)
        Assert.Equal(ManagerAddressKind.Invalid, ManagerAddress.Resolve("sftp://kim:secret@nas.example:2222/MOV", Resources()).Kind)   ' no password in an address
    End Sub

    <Fact>
    Public Sub A_configured_share_that_owns_the_path_wins_over_the_bare_share()
        Dim owned As ManagerAddressResult = ManagerAddress.Resolve("\\nas\photos\2026\trip", Resources())
        Assert.Equal(ManagerAddressKind.Ok, owned.Kind)
        Assert.Equal("res:r3", owned.Location.Place.Id)
        Assert.Equal("\\nas\photos\2026\trip", owned.Location.DirectoryPath)

        Dim bare As ManagerAddressResult = ManagerAddress.Resolve("\\nas\photos\2026\trip", Nothing)
        Assert.Equal(ManagerAddressKind.Ok, bare.Kind)
        Assert.StartsWith("local:", bare.Location.Place.Id, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub The_highlighted_place_is_the_one_with_the_same_id()
        Dim places As List(Of ManagerPlace) = ManagerAddress.BuildPlaces({"C:\"}, Resources())
        Assert.Equal("res:r1", ManagerAddress.PlaceFor(Remote("/MOV/a"), places).Id)
        Assert.Equal("local:C:\", ManagerAddress.PlaceFor(Loc("C:\x"), places).Id)
        Assert.Null(ManagerAddress.PlaceFor(New ManagerLocation(ManagerPlace.ForDrive("Z:\", "Z:"), "Z:\x"), places))
        Assert.Null(ManagerAddress.PlaceFor(Nothing, places))
    End Sub

    ' ============================================================ the listing engine ====

    ''' <summary>A provider with no disk and no server: what each directory answers, and a gate that holds one open.</summary>
    Private NotInheritable Class FakeProvider
        Implements IManagerDirectoryProvider

        Friend ReadOnly Listings As New ConcurrentDictionary(Of String, DirectoryListing)(StringComparer.OrdinalIgnoreCase)
        Friend ReadOnly Gates As New ConcurrentDictionary(Of String, ManualResetEventSlim)(StringComparer.OrdinalIgnoreCase)
        Friend ReadOnly Started As New ConcurrentDictionary(Of String, ManualResetEventSlim)(StringComparer.OrdinalIgnoreCase)
        Friend ReadOnly Asked As New ConcurrentQueue(Of String)()
        Friend Throws As Exception
        ''' <summary>A provider that does not look at its token: the guard must hold without its help.</summary>
        Friend IgnoreCancel As Boolean

        Friend Function StartedEvent(dir As String) As ManualResetEventSlim
            Return Started.GetOrAdd(dir, Function(k) New ManualResetEventSlim(False))
        End Function

        Friend Function GateFor(dir As String) As ManualResetEventSlim
            Return Gates.GetOrAdd(dir, Function(k) New ManualResetEventSlim(False))
        End Function

        Public Function ListChildren(dirPath As String, ct As CancellationToken) As DirectoryListing Implements IManagerDirectoryProvider.ListChildren
            Asked.Enqueue(dirPath)
            StartedEvent(dirPath).Set()
            If Throws IsNot Nothing Then Throw Throws
            Dim gate As ManualResetEventSlim = Nothing
            If Gates.TryGetValue(dirPath, gate) Then
                If IgnoreCancel Then gate.Wait() Else gate.Wait(ct)
            End If
            Dim listing As DirectoryListing = Nothing
            If Listings.TryGetValue(dirPath, listing) Then Return listing
            Return DirectoryListing.Missing()
        End Function

        Public Function StatEntry(path As String) As StatResult Implements IManagerDirectoryProvider.StatEntry
            Return StatResult.Missing()
        End Function

        Public Function IsWithinRoot(rootPath As String, path As String) As Boolean Implements IManagerDirectoryProvider.IsWithinRoot
            Return ManagerPathRules.Contains(ResourceKind.Local, rootPath, path)
        End Function

        Public Function PathsEqual(a As String, b As String) As Boolean Implements IManagerDirectoryProvider.PathsEqual
            Return ManagerPathRules.AreEqual(ResourceKind.Local, a, b)
        End Function

        Public Function ParentOf(path As String) As String Implements IManagerDirectoryProvider.ParentOf
            Return ManagerPathRules.ParentOf(ResourceKind.Local, path)
        End Function

        Public Function RemoveEmptyDirectory(rootPath As String, path As String) As DirectoryRemoval Implements IManagerDirectoryProvider.RemoveEmptyDirectory
            Return DirectoryRemoval.Refused
        End Function
    End Class

    Private NotInheritable Class Probe
        Implements IDisposable

        Private ReadOnly onDispose As Action

        Friend Sub New(onDispose As Action)
            Me.onDispose = onDispose
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            onDispose()
        End Sub
    End Class

    Private NotInheritable Class Rig
        Implements IDisposable

        Friend ReadOnly Provider As New FakeProvider()
        Friend ReadOnly Browser As ManagerBrowser
        Friend ReadOnly Published As New ConcurrentQueue(Of ManagerListingResult)()
        Friend OpenCount As Integer
        Friend DisposedCount As Integer
        Friend Faults As Integer
        ''' <summary>When set, the opener answers this instead of the fake provider.</summary>
        Friend OpenOverride As ManagerOpenResult

        Friend Sub New()
            Browser = New ManagerBrowser(AddressOf Open, Sub(ex As Exception) Interlocked.Increment(Faults))
            AddHandler Browser.Completed, Sub(sender As Object, r As ManagerListingResult) Published.Enqueue(r)
        End Sub

        Private Function Open(place As ManagerPlace, ct As CancellationToken) As ManagerOpenResult
            Interlocked.Increment(OpenCount)
            If OpenOverride IsNot Nothing Then Return OpenOverride
            Return ManagerOpenResult.Ok(Provider, New Probe(Sub() Interlocked.Increment(DisposedCount)))
        End Function

        Friend Sub Serve(dir As String, ParamArray entries As ResourceEntry())
            Provider.Listings(dir) = DirectoryListing.Listed(entries.ToList())
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Browser.Dispose()
        End Sub
    End Class

    <Fact>
    Public Sub Only_the_newest_listing_is_published_when_a_slow_one_is_overtaken()
        Using rig As New Rig()
            rig.Serve("C:\A", Res("a1.jpg", "C:\A\a1.jpg", False))
            rig.Serve("C:\B", Res("b1.jpg", "C:\B\b1.jpg", False))
            Dim gate As ManualResetEventSlim = rig.Provider.GateFor("C:\A")

            Dim first As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Loc("C:\A"))
            Assert.True(rig.Provider.StartedEvent("C:\A").Wait(TimeSpan.FromSeconds(10)))
            Assert.True(rig.Browser.IsBusy)

            Dim second As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Loc("C:\B"))
            WaitFor(second)
            gate.Set()
            WaitFor(first)

            Assert.Equal(1, rig.Published.Count)
            Dim shown As ManagerListingResult = rig.Published.Single()
            Assert.Equal("C:\B", shown.Target.DirectoryPath)
            Assert.Equal("b1.jpg", shown.Entries.Single().Name)
            Assert.True(rig.Browser.IsCurrent(shown.Generation))
            Assert.False(rig.Browser.IsCurrent(first.Result.Generation))
            Assert.Equal(ManagerStatusKind.Cancelled, first.Result.Status)
            Assert.False(rig.Browser.IsBusy)
            Assert.Equal(1, rig.OpenCount)                                ' the second listing reused the connection
        End Using
    End Sub

    <Fact>
    Public Sub A_provider_that_ignores_its_token_still_cannot_publish_a_stale_listing()
        Using rig As New Rig()
            rig.Provider.IgnoreCancel = True
            rig.Serve("C:\A", Res("a1.jpg", "C:\A\a1.jpg", False))
            rig.Serve("C:\B", Res("b1.jpg", "C:\B\b1.jpg", False))
            Dim gate As ManualResetEventSlim = rig.Provider.GateFor("C:\A")

            Dim first As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Loc("C:\A"))
            Assert.True(rig.Provider.StartedEvent("C:\A").Wait(TimeSpan.FromSeconds(10)))
            WaitFor(rig.Browser.Begin(ManagerIntent.Go, Loc("C:\B")))
            gate.Set()                                                    ' A answers late, with a perfectly good listing
            WaitFor(first)

            Assert.Equal(1, rig.Published.Count)
            Assert.Equal("C:\B", rig.Published.Single().Target.DirectoryPath)
        End Using
    End Sub

    <Fact>
    Public Sub Cancel_publishes_nothing_and_leaves_the_browser_idle()
        Using rig As New Rig()
            rig.Serve("C:\A", Res("a1.jpg", "C:\A\a1.jpg", False))
            rig.Provider.GateFor("C:\A")

            Dim first As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Loc("C:\A"))
            Assert.True(rig.Provider.StartedEvent("C:\A").Wait(TimeSpan.FromSeconds(10)))
            rig.Browser.Cancel()
            Assert.False(rig.Browser.IsBusy)
            WaitFor(first)

            Assert.Equal(ManagerStatusKind.Cancelled, first.Result.Status)
            Assert.Empty(rig.Published)
        End Using
    End Sub

    <Fact>
    Public Sub A_traversal_or_a_path_outside_the_root_never_reaches_a_provider()
        Using rig As New Rig()
            Dim dotdot As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Loc("C:\a\..\b"))
            WaitFor(dotdot)
            Assert.Equal(ManagerStatusKind.OutsideRoot, dotdot.Result.Status)

            Dim outside As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Remote("/OTHER"))
            WaitFor(outside)
            Assert.Equal(ManagerStatusKind.OutsideRoot, outside.Result.Status)

            Dim lexical As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Remote("/MOV/../etc"))
            WaitFor(lexical)
            Assert.Equal(ManagerStatusKind.OutsideRoot, lexical.Result.Status)

            Assert.Equal(0, rig.OpenCount)
            Assert.Empty(rig.Provider.Asked)
        End Using
    End Sub

    ''' <summary>AC257-2: every way a listing can come to nothing arrives as its own status, and none of them as an empty folder.</summary>
    <Fact>
    Public Sub Each_provider_outcome_becomes_its_own_status()
        Dim cases As New List(Of Tuple(Of DirectoryListing, ManagerStatusKind)) From {
            Tuple.Create(DirectoryListing.Missing(), ManagerStatusKind.Missing),
            Tuple.Create(DirectoryListing.NotADirectory(), ManagerStatusKind.NotADirectory),
            Tuple.Create(DirectoryListing.Denied(), ManagerStatusKind.Denied),
            Tuple.Create(DirectoryListing.Failed(New ResourceFailure(ResourceFailureKind.Transport)), ManagerStatusKind.Disconnected),
            Tuple.Create(DirectoryListing.Cancelled(), ManagerStatusKind.Cancelled)}
        For Each [case] As Tuple(Of DirectoryListing, ManagerStatusKind) In cases
            Using rig As New Rig()
                rig.Provider.Listings("C:\X") = [case].Item1
                Dim task As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Loc("C:\X"))
                WaitFor(task)
                Assert.Equal([case].Item2, task.Result.Status)
                Assert.False(task.Result.IsListed)
                Assert.Empty(task.Result.Entries)
            End Using
        Next
    End Sub

    <Fact>
    Public Sub An_empty_folder_is_a_success_and_a_truncated_one_says_so()
        Using rig As New Rig()
            rig.Provider.Listings("C:\Empty") = DirectoryListing.Listed(New List(Of ResourceEntry)())
            rig.Provider.Listings("C:\Big") = DirectoryListing.Listed(New List(Of ResourceEntry) From {Res("a.jpg", "C:\Big\a.jpg", False)}, truncated:=True)

            Dim empty As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Loc("C:\Empty"))
            WaitFor(empty)
            Assert.Equal(ManagerStatusKind.Ok, empty.Result.Status)
            Assert.True(empty.Result.IsListed)
            Assert.Empty(empty.Result.Entries)

            Dim big As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Loc("C:\Big"))
            WaitFor(big)
            Assert.Equal(ManagerStatusKind.Truncated, big.Result.Status)
            Assert.True(big.Result.IsListed)
            Assert.Equal(1, big.Result.Entries.Count)
        End Using
    End Sub

    <Fact>
    Public Sub Dot_entries_are_dropped_and_the_target_is_committed_in_canonical_form()
        Using rig As New Rig()
            rig.Serve("C:\A", Res(".", "C:\A\.", True), Res("..", "C:\A\..", True), Res("keep.jpg", "C:\A\keep.jpg", False))
            Dim task As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Loc("C:\A\"))
            WaitFor(task)
            Assert.Equal("C:\A", task.Result.Target.DirectoryPath)
            Assert.Equal(New String() {"keep.jpg"}, task.Result.Entries.Select(Function(e) e.Name).ToArray())
        End Using
    End Sub

    <Fact>
    Public Sub A_connection_is_reused_kept_through_a_refusal_and_dropped_after_a_dead_link()
        Using rig As New Rig()
            rig.Serve("C:\A", Res("a.jpg", "C:\A\a.jpg", False))
            rig.Provider.Listings("C:\Locked") = DirectoryListing.Denied()
            rig.Provider.Listings("C:\Gone") = DirectoryListing.Failed(New ResourceFailure(ResourceFailureKind.Transport))

            WaitFor(rig.Browser.Begin(ManagerIntent.Go, Loc("C:\A")))
            WaitFor(rig.Browser.Begin(ManagerIntent.Go, Loc("C:\Locked")))
            Assert.Equal(1, rig.OpenCount)
            Assert.Equal(1, rig.Browser.LeaseCount)                       ' a refusal says nothing against the connection

            WaitFor(rig.Browser.Begin(ManagerIntent.Go, Loc("C:\Gone")))
            Assert.Equal(0, rig.Browser.LeaseCount)                       ' a link that did not answer is not trusted again
            Assert.Equal(1, rig.DisposedCount)

            WaitFor(rig.Browser.Begin(ManagerIntent.Go, Loc("C:\A")))
            Assert.Equal(2, rig.OpenCount)                                ' the next try dials again
        End Using
    End Sub

    <Fact>
    Public Sub Keep_only_releases_the_connections_of_other_places_and_dispose_releases_the_rest_at_once()
        Dim rig As New Rig()
        rig.Serve("C:\A", Res("a.jpg", "C:\A\a.jpg", False))
        WaitFor(rig.Browser.Begin(ManagerIntent.Go, Loc("C:\A")))
        Assert.Equal(1, rig.Browser.LeaseCount)

        rig.Browser.KeepOnly(Drive.Id)                                    ' the place it is standing in: nothing goes
        Assert.Equal(1, rig.Browser.LeaseCount)
        rig.Browser.KeepOnly(Server.Id)                                   ' it moved to another place: the disk's connection is stale
        Assert.Equal(0, rig.Browser.LeaseCount)
        Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(10)
        While Volatile.Read(rig.DisposedCount) = 0 AndAlso DateTime.UtcNow < deadline
            Thread.Sleep(10)
        End While
        Assert.Equal(1, rig.DisposedCount)

        WaitFor(rig.Browser.Begin(ManagerIntent.Go, Loc("C:\A")))        ' dialled again
        Assert.Equal(2, rig.OpenCount)
        rig.Dispose()
        Assert.Equal(2, rig.DisposedCount)                                ' Dispose lets go before it returns
        Assert.Equal(0, rig.Browser.LeaseCount)

        Dim publishedBefore As Integer = rig.Published.Count
        Dim late As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Loc("C:\A"))
        WaitFor(late)
        Assert.Equal(ManagerStatusKind.Cancelled, late.Result.Status)     ' a disposed browser lists nothing
        Assert.Equal(publishedBefore, rig.Published.Count)
        Assert.Equal(2, rig.OpenCount)
    End Sub

    <Fact>
    Public Sub A_place_that_cannot_be_opened_is_a_status_and_the_provider_is_never_asked()
        Using rig As New Rig()
            rig.OpenOverride = ManagerOpenResult.Fail(ManagerStatusKind.SignInNeeded)
            Dim task As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Remote("/MOV/a"))
            WaitFor(task)
            Assert.Equal(ManagerStatusKind.SignInNeeded, task.Result.Status)
            Assert.Empty(rig.Provider.Asked)
            Assert.Equal(0, rig.Browser.LeaseCount)
        End Using
    End Sub

    <Fact>
    Public Sub A_fault_in_a_provider_becomes_a_status_and_is_reported_for_the_log()
        Using rig As New Rig()
            rig.Provider.Throws = New InvalidOperationException("boom")
            Dim task As Task(Of ManagerListingResult) = rig.Browser.Begin(ManagerIntent.Go, Loc("C:\A"))
            WaitFor(task)
            Assert.Equal(ManagerStatusKind.Disconnected, task.Result.Status)
            Assert.Equal(1, rig.Faults)
            Assert.Equal(1, rig.Published.Count)                          ' the user is told, not left waiting
        End Using
    End Sub

    ' ================================================================ no side effects ====

    Private Shared Function PanelFileManagerSource() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Image_Panel_Form.FileManager.vb")
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src\Image_Panel_Form.FileManager.vb not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function CodeLines(source As String) As List(Of String)
        Return source.Split({vbLf}, StringSplitOptions.None).
            Where(Function(l) Not l.TrimStart().StartsWith("'", StringComparison.Ordinal)).ToList()
    End Function

    ''' <summary>
    ''' AC257-1 / AC257-5 as a reading: browsing, selecting and closing the manager never change the viewer's list or media. A form
    ''' cannot be built in a test, so the partial that owns the mode is read for the calls that would.
    ''' </summary>
    <Fact>
    Public Sub The_manager_partial_never_touches_the_viewers_list_or_choice()
        Dim code As String = String.Join(vbLf, CodeLines(PanelFileManagerSource()))
        For Each forbidden As String In {"SnapshotFileList", "Choosen_Picture_From_Panel", "StartBatch", "ExecuteRecipientAction",
                                         "PoMove", "BatchRunner", "FileManager.", "File.Delete", "File.Move", "File.Copy",
                                         "Directory.Delete", "Directory.Move", "Process.Start", "AttachSession", "ActiveResourceSession"}
            Assert.False(code.Contains(forbidden, StringComparison.Ordinal), "the file manager partial must not use " & forbidden)
        Next
    End Sub

    ''' <summary>Opening a picture in the main window stays exactly where it was: the triage branch of the one activation method.</summary>
    <Fact>
    Public Sub Only_the_triage_branch_opens_a_file_in_the_main_window()
        Dim source As String = PanelFileManagerSource()
        Dim code As List(Of String) = CodeLines(source)
        Assert.Equal(1, code.Where(Function(l) l.Contains("OpenInMain(", StringComparison.Ordinal)).Count())

        Dim start As Integer = source.IndexOf("Private Sub ActivateItem(", StringComparison.Ordinal)
        Assert.True(start >= 0, "ActivateItem is missing")
        Dim finish As Integer = source.IndexOf("End Sub", start, StringComparison.Ordinal)
        Dim body As String = source.Substring(start, finish - start)
        Assert.Matches(New Regex("If manager_Mode Then\s+ManagerActivate\(index\)\s+Else\s+OpenInMain\(index\)", RegexOptions.Singleline), body)
    End Sub

    ''' <summary>A new modern-only file must be whole-file "#If Not NETFRAMEWORK" and listed by hand in the old-style project.</summary>
    <Fact>
    Public Sub The_manager_files_are_listed_in_the_fallback_project_and_fenced_off_its_runtime()
        Dim root As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)
        While root IsNot Nothing AndAlso Not File.Exists(Path.Combine(root.FullName, "src", "FastMediaSorter.vbproj"))
            root = root.Parent
        End While
        Assert.NotNull(root)
        Dim project As String = File.ReadAllText(Path.Combine(root.FullName, "src", "FastMediaSorter.vbproj"))
        For Each relativePath As String In {"FileManager\ManagerLocation.vb", "FileManager\ManagerEntry.vb", "FileManager\ManagerNavigation.vb",
                                            "FileManager\ManagerAddress.vb", "FileManager\ManagerBrowser.vb", "FileManager\ManagerProviderOpener.vb",
                                            "Resources\IManagerDirectoryProvider.vb", "Image_Panel_Form.FileManager.vb"}
            Assert.Contains("<Compile Include=""" & relativePath & """", project, StringComparison.Ordinal)
            Dim text As String = File.ReadAllText(Path.Combine(root.FullName, "src", relativePath))
            Assert.StartsWith("#If Not NETFRAMEWORK Then", text.TrimStart(ChrW(&HFEFF)), StringComparison.Ordinal)
            Assert.EndsWith("#End If", text.TrimEnd(), StringComparison.Ordinal)
        Next
    End Sub

End Class
#End If
