Option Strict On

Imports System.IO
Imports Xunit

''' <summary>
''' The disk half of the optional runtime installer (audit 050 D-09, D-13). Both legs: the
''' x86 viewer downloads its VLC and OCR runtimes through exactly this code, and is the
''' build that downloads them most (the packagers trim its 32-bit natives).
''' </summary>
Public Class RuntimeInstallPlanTests
    Implements IDisposable

    Private ReadOnly root As String = Path.Combine(Path.GetTempPath(), "fms-runtime-plan-" & Guid.NewGuid().ToString("N"))

    Public Sub New()
        Directory.CreateDirectory(root)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Directory.Delete(root, True)
        Catch
        End Try
    End Sub

    ' --- which entries, where ----------------------------------------------------

    <Fact>
    Public Sub Only_the_current_architecture_is_extracted()
        Assert.Equal("libvlc.dll", RuntimeInstallPlan.MapEntry(OptionalRuntimeKind.Vlc, "build/x64/libvlc.dll", True))
        Assert.Equal("plugins/access/libfilesystem_plugin.dll",
                     RuntimeInstallPlan.MapEntry(OptionalRuntimeKind.Vlc, "build/x64/plugins/access/libfilesystem_plugin.dll", True))
        Assert.Equal("", RuntimeInstallPlan.MapEntry(OptionalRuntimeKind.Vlc, "build/x86/libvlc.dll", True))
        Assert.Equal("libvlc.dll", RuntimeInstallPlan.MapEntry(OptionalRuntimeKind.Vlc, "build/x86/libvlc.dll", False))

        Assert.Equal("tesseract50.dll", RuntimeInstallPlan.MapEntry(OptionalRuntimeKind.Ocr, "x64/tesseract50.dll", True))
        Assert.Equal("", RuntimeInstallPlan.MapEntry(OptionalRuntimeKind.Ocr, "x86/tesseract50.dll", True))
        Assert.Equal("leptonica-1.82.0.dll", RuntimeInstallPlan.MapEntry(OptionalRuntimeKind.Ocr, "x86\leptonica-1.82.0.dll", False))
    End Sub

    <Fact>
    Public Sub Package_furniture_and_folders_are_not_extracted()
        Assert.Equal("", RuntimeInstallPlan.MapEntry(OptionalRuntimeKind.Vlc, "VideoLAN.LibVLC.Windows.nuspec", True))
        Assert.Equal("", RuntimeInstallPlan.MapEntry(OptionalRuntimeKind.Vlc, "build/x64/plugins/", True))
        Assert.Equal("", RuntimeInstallPlan.MapEntry(OptionalRuntimeKind.Ocr, "lib/net48/Tesseract.dll", True))
    End Sub

#If Not NETFRAMEWORK Then
    <Fact>
    Public Sub FFmpeg_takes_one_file_flattened()
        Assert.Equal("ffmpeg.exe", RuntimeInstallPlan.MapEntry(OptionalRuntimeKind.Ffmpeg, "ffmpeg-9.0.1-essentials_build/bin/ffmpeg.exe", True))
        Assert.Equal("", RuntimeInstallPlan.MapEntry(OptionalRuntimeKind.Ffmpeg, "ffmpeg-9.0.1-essentials_build/bin/ffplay.exe", True))
    End Sub
#End If

    ''' <summary>Zip-slip (D-13): the mapped path of a hostile entry is refused, so the
    ''' installer refuses the package instead of writing outside the runtime folder.</summary>
    <Fact>
    Public Sub An_entry_climbing_out_of_the_folder_is_refused()
        Dim mapped As String = RuntimeInstallPlan.MapEntry(OptionalRuntimeKind.Ocr, "x64/../x.dll", True)
        Assert.Equal("../x.dll", mapped)
        Assert.Null(RuntimeInstallPlan.ContainedDestination(root, mapped))
        Assert.Null(RuntimeInstallPlan.ContainedDestination(root, "a/../../evil.dll"))
        Assert.Null(RuntimeInstallPlan.ContainedDestination(root, "C:/Windows/evil.dll"))
        Assert.Null(RuntimeInstallPlan.ContainedDestination(root, "/evil.dll"))
        Assert.Null(RuntimeInstallPlan.ContainedDestination(root, "file.dll:stream"))
        Assert.Null(RuntimeInstallPlan.ContainedDestination(root, ".."))
    End Sub

    <Fact>
    Public Sub An_ordinary_entry_lands_inside()
        Assert.Equal(Path.Combine(root, "plugins", "a.dll"), RuntimeInstallPlan.ContainedDestination(root, "plugins/a.dll"))
        ' A ".." that stays inside is still inside.
        Assert.Equal(Path.Combine(root, "b.dll"), RuntimeInstallPlan.ContainedDestination(root, "plugins/../b.dll"))
    End Sub

    ' --- the pinned digests --------------------------------------------------------

    <Fact>
    Public Sub Both_NuGet_packages_are_pinned()
        For Each digest As String In {RuntimeInstallPlan.OcrPackageSha256, RuntimeInstallPlan.VlcPackageSha256}
            Assert.Equal(64, digest.Length)
            Assert.Matches("^[0-9a-f]{64}$", digest)
        Next
    End Sub

    ' --- the .complete marker (D-09) -------------------------------------------------

    <Fact>
    Public Sub A_missing_folder_is_missing()
        Assert.Equal(RuntimeTreeState.Missing,
                     RuntimeInstallPlan.ReadTreeState(Path.Combine(root, "none"), New RuntimeTreeTotals(1, 1)))
    End Sub

    <Fact>
    Public Sub A_marked_tree_is_complete_until_a_file_goes()
        Dim dir As String = MakeTree("vlc", ("libvlc.dll", 100), ("libvlccore.dll", 200), ("plugins/p.dll", 50))
        RuntimeInstallPlan.WriteCompleteMarker(dir)
        Assert.Equal(RuntimeTreeState.Complete, RuntimeInstallPlan.ReadTreeState(dir, New RuntimeTreeTotals(0, 0)))

        ' The interruption D-09 is about: a sibling DLL missing.
        File.Delete(Path.Combine(dir, "libvlccore.dll"))
        Assert.Equal(RuntimeTreeState.Incomplete, RuntimeInstallPlan.ReadTreeState(dir, New RuntimeTreeTotals(0, 0)))
    End Sub

    <Fact>
    Public Sub A_truncated_file_makes_a_marked_tree_incomplete()
        Dim dir As String = MakeTree("ff", ("ffmpeg.exe", 1000))
        RuntimeInstallPlan.WriteCompleteMarker(dir)
        File.WriteAllBytes(Path.Combine(dir, "ffmpeg.exe"), New Byte(499) {})
        Assert.Equal(RuntimeTreeState.Incomplete, RuntimeInstallPlan.ReadTreeState(dir, New RuntimeTreeTotals(0, 0)))
    End Sub

    ''' <summary>A tree from a build before the marker: adopted only when it holds exactly
    ''' what the pinned package holds; a half-extracted one of the same age is not.</summary>
    <Fact>
    Public Sub An_unmarked_tree_is_adopted_only_when_it_is_the_whole_package()
        Dim dir As String = MakeTree("ocr", ("tesseract50.dll", 300), ("leptonica-1.82.0.dll", 200))
        Assert.Equal(RuntimeTreeState.CompleteUnmarked, RuntimeInstallPlan.ReadTreeState(dir, New RuntimeTreeTotals(2, 500)))
        Assert.Equal(RuntimeTreeState.Incomplete, RuntimeInstallPlan.ReadTreeState(dir, New RuntimeTreeTotals(3, 700)))
        Assert.Equal(RuntimeTreeState.Incomplete, RuntimeInstallPlan.ReadTreeState(dir, New RuntimeTreeTotals(0, 0)))
    End Sub

    <Fact>
    Public Sub A_damaged_marker_is_not_a_runtime()
        Dim dir As String = MakeTree("bad", ("a.dll", 10))
        File.WriteAllText(Path.Combine(dir, RuntimeInstallPlan.CompleteMarkerName), "garbage")
        Assert.Equal(RuntimeTreeState.Incomplete, RuntimeInstallPlan.ReadTreeState(dir, New RuntimeTreeTotals(1, 10)))
    End Sub

    <Fact>
    Public Sub The_pinned_totals_are_known_for_every_kind()
        For Each is64 As Boolean In {True, False}
            Assert.True(RuntimeInstallPlan.PinnedTotals(OptionalRuntimeKind.Ocr, is64).Files > 0)
            Assert.True(RuntimeInstallPlan.PinnedTotals(OptionalRuntimeKind.Vlc, is64).Files > 0)
        Next
    End Sub

    <Fact>
    Public Sub Staging_folders_sit_beside_the_runtime_and_are_swept()
        Dim runtimeDir As String = Path.Combine(root, "vlc", "win-x64")
        Dim staging As String = RuntimeInstallPlan.StagingDirFor(runtimeDir)
        Assert.Equal(Path.Combine(root, "vlc"), Path.GetDirectoryName(staging))
        Assert.StartsWith("win-x64" & RuntimeInstallPlan.StagingInfix, Path.GetFileName(staging))

        Directory.CreateDirectory(staging)
        File.WriteAllText(Path.Combine(staging, "half.dll"), "x")
        Dim neighbour As String = Path.Combine(root, "vlc", "win-x86")
        Directory.CreateDirectory(neighbour)

        RuntimeInstallPlan.SweepStaging(runtimeDir)
        Assert.False(Directory.Exists(staging))
        Assert.True(Directory.Exists(neighbour))
    End Sub

    Private Function MakeTree(name As String, ParamArray files As (Path As String, Size As Integer)()) As String
        Dim dir As String = Path.Combine(root, name)
        For Each f In files
            Dim full As String = Path.Combine(dir, f.Path.Replace("/"c, Path.DirectorySeparatorChar))
            Directory.CreateDirectory(Path.GetDirectoryName(full))
            File.WriteAllBytes(full, New Byte(f.Size - 1) {})
        Next
        Return dir
    End Function

End Class
