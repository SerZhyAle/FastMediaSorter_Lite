#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Linq
Imports Xunit

Public Class MediaKindTests
    Private Shared ReadOnly Images As String() = {".jpg", ".gif", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".ico", ".wmf", ".emf", ".exif", ".webp", ".avif", ".heic", ".heif"}
    Private Shared ReadOnly Playback As String() = {".mp3", ".wav", ".m4a", ".wma", ".ogg", ".mp4", ".mkv"}

    <Fact>
    Public Sub Existing_images_and_playback_use_one_classifier()
        For Each ext As String In Images
            Assert.Equal(MediaKind.Image, KindOf(ext))
        Next
        For Each ext As String In Playback.Take(5)
            Assert.Equal(MediaKind.Audio, KindOf(ext))
        Next
        Assert.Equal(MediaKind.Video, KindOf(".mp4"))
        Assert.Equal(MediaKind.Video, KindOf(".mkv"))
        Assert.Equal(MediaKind.Other, KindOf(".unknown"))
        ' MEDIA-CLASSIFICATION distinguishes books; PK's document toggle admits both.
        For Each ext As String In New ModernViewerPreferences().DocumentExtensionSet()
            Assert.True(KindOf(ext) = MediaKind.Document OrElse KindOf(ext) = MediaKind.Book)
        Next
    End Sub

    <Fact>
    Public Sub Default_scan_keeps_existing_files_and_document_and_other_are_opt_in()
        Dim p As New ModernViewerPreferences()
        Dim extensions = SortableContentKinds.BuildExtensions(Images, Playback, {".svg"}, p)
        Assert.True(extensions.SetEquals(Images.Concat(Playback).Concat({".svg"})))
        Assert.False(extensions.Contains(".pdf"))
        Assert.False(extensions.Contains(".zip"))
        p.IncludeDocumentInNavigation = True
        p.SortOtherFiles = True
        p.OtherExtensions = ".zip"
        extensions = SortableContentKinds.BuildExtensions(Images, Playback, {".svg"}, p)
        Assert.Contains(".pdf", extensions)
        Assert.Contains(".zip", extensions)
        p.IncludeAudioInNavigation = False
        p.IncludeVideoInNavigation = False
        extensions = SortableContentKinds.BuildExtensions(Images, Playback, {".svg"}, p)
        Assert.DoesNotContain(".mp3", extensions)
        Assert.DoesNotContain(".mp4", extensions)
    End Sub

    <Fact>
    Public Sub Mixed_folder_listing_and_background_count_use_the_same_eligibility()
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-pk-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(folder)
        Try
            For Each name In {"photo.jpg", "clip.mp4", "song.mp3", "contract.pdf", "notes.txt", "archive.zip", "unlisted.xyz", ".hidden.pdf"}
                File.WriteAllText(Path.Combine(folder, name), "test")
            Next
            Dim provider As New LocalProvider(Function() False)
            For Each includeDocs In {False, True}
                Dim p As New ModernViewerPreferences With {.IncludeDocumentInNavigation = includeDocs, .SortOtherFiles = True, .OtherExtensions = ".zip"}
                Dim extensions = SortableContentKinds.BuildExtensions(Images, Playback, {".svg"}, p)
                Dim listing = provider.ListEntries(folder, Threading.CancellationToken.None)
                Dim visible = listing.Where(Function(entry) Not entry.IsDirectory AndAlso MediaBrowsing.IsEligible(entry.FullPath, extensions)).ToList()
                Dim count = Enumerable.Count(provider.ListEntries(folder, Threading.CancellationToken.None), Function(entry) Not entry.IsDirectory AndAlso MediaBrowsing.IsEligible(entry.FullPath, extensions))
                Assert.Equal(If(includeDocs, 6, 4), visible.Count)
                Assert.Equal(visible.Count, count)
            Next
        Finally
            Directory.Delete(folder, True)
        End Try
    End Sub

    <Fact>
    Public Sub Every_media_kind_listing_and_count_follow_the_navigation_toggles()
        ' PM acceptance 8: one folder holding every MediaKind, walked through the same
        ' eligibility the viewer's list and background counter share, for each video /
        ' audio / documents combination. Streams (a playlist) and admitted containers
        ' (a .zip via "other files") answer to no PM toggle, so they survive every case.
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-pm-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(folder)
        Try
            For Each name In {"photo.jpg", "clip.mp4", "song.mp3", "manual.pdf", "notes.txt",
                              "playlist.m3u", "archive.zip", "unlisted.xyz", ".hidden.pdf"}
                File.WriteAllText(Path.Combine(folder, name), "test")
            Next
            Dim provider As New LocalProvider(Function() False)
            Dim playbackWithStream = Playback.Concat({".m3u"}).ToArray()
            Dim cases = {
                New With {.Video = True, .Audio = True, .Documents = True, .Expected = 7},
                New With {.Video = False, .Audio = True, .Documents = True, .Expected = 6},
                New With {.Video = False, .Audio = False, .Documents = True, .Expected = 5},
                New With {.Video = True, .Audio = True, .Documents = False, .Expected = 5}}

            For Each [case] In cases
                Dim p As New ModernViewerPreferences With {
                    .IncludeVideoInNavigation = [case].Video,
                    .IncludeAudioInNavigation = [case].Audio,
                    .IncludeDocumentInNavigation = [case].Documents,
                    .SortOtherFiles = True, .OtherExtensions = ".zip"}
                Dim extensions = SortableContentKinds.BuildExtensions(Images, playbackWithStream, {".svg"}, p)
                Dim listing = provider.ListEntries(folder, Threading.CancellationToken.None)
                Dim visible = listing.Where(Function(entry) Not entry.IsDirectory AndAlso
                                                       MediaBrowsing.IsEligible(entry.FullPath, extensions)).ToList()
                Dim count = Enumerable.Count(listing, Function(entry) Not entry.IsDirectory AndAlso
                                                     MediaBrowsing.IsEligible(entry.FullPath, extensions))
                Assert.Equal([case].Expected, visible.Count)
                Assert.Equal(visible.Count, count)

                Assert.Contains(Path.Combine(folder, "playlist.m3u"), visible.Select(Function(entry) entry.FullPath))
                Assert.Contains(Path.Combine(folder, "archive.zip"), visible.Select(Function(entry) entry.FullPath))
                Assert.Equal([case].Video, visible.Any(Function(entry) entry.FullPath = Path.Combine(folder, "clip.mp4")))
                Assert.Equal([case].Audio, visible.Any(Function(entry) entry.FullPath = Path.Combine(folder, "song.mp3")))
                Assert.Equal([case].Documents, visible.Any(Function(entry) entry.FullPath = Path.Combine(folder, "manual.pdf")))
                Assert.Equal([case].Documents, visible.Any(Function(entry) entry.FullPath = Path.Combine(folder, "notes.txt")))
            Next
        Finally
            Directory.Delete(folder, True)
        End Try
    End Sub

    <Fact>
    Public Sub Non_images_never_enter_decode_even_if_the_user_lists_their_extensions()
        Dim all = Images.Concat({".zip", ".pdf", ".txt", ".mp3", ".mp4", ".iso"}).ToArray()
        For Each path In {"large.zip", "contract.pdf", "notes.txt", "song.mp3", "clip.mp4", "disk.iso"}
            Assert.False(SortableContentKinds.CanDecodeImage(path, all))
        Next
        Assert.True(SortableContentKinds.CanDecodeImage("photo.JPG.download", Images))
        Assert.False(SortableContentKinds.CanDecodeImage("drawing.svg", Images))
    End Sub

    <Fact>
    Public Sub Editable_extensions_normalize_and_round_trip_without_wildcards_or_paths()
        Dim p As New ModernViewerPreferences With {
            .IncludeDocumentInNavigation = True, .SortOtherFiles = True,
            .DocumentExtensions = "PDF .txt pdf;MD", .OtherExtensions = "ZIP;exe * *.iso C:\file . ../path"}
        p.Normalize()
        Assert.True(p.DocumentExtensionSet().SetEquals({".pdf", ".txt", ".md"}))
        Assert.True(p.OtherExtensionSet().SetEquals({".zip", ".exe"}))
        Dim restored = ModernViewerPreferences.FromJson(p.ExportJson())
        Assert.True(restored.IncludeDocumentInNavigation)
        Assert.True(restored.SortOtherFiles)
        Assert.Equal(p.DocumentExtensions, restored.DocumentExtensions)
        Assert.Equal(p.OtherExtensions, restored.OtherExtensions)
        p.DocumentExtensions = Nothing
        p.OtherExtensions = Nothing
        p.Normalize()
        Assert.Empty(p.DocumentExtensionSet())
        Assert.Empty(p.OtherExtensionSet())
    End Sub
End Class
#End If
