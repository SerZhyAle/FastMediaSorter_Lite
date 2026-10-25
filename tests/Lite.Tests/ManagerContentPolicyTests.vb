#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports Xunit

Public Class ManagerContentPolicyTests

    Private Function MakeEntry(name As String, Optional isDir As Boolean = False, Optional isHidden As Boolean = False,
                               Optional isSystem As Boolean = False, Optional length As Long = 1024L) As ManagerEntry
        Dim place As ManagerPlace = ManagerPlace.ForDrive("C:\", "C:")
        Return New ManagerEntry(ManagerEntry.IdOf(place, "C:\" & name), name, "C:\" & name,
                                isDir, False, isHidden, isSystem, If(isDir, -1L, length),
                                DateTime.UtcNow, "C:\" & name)
    End Function

    <Fact>
    Public Sub Folders_AlwaysParticipate_InAllThreeModes()
        Dim folder = MakeEntry("SubFolder", isDir:=True)

        Assert.True(ManagerContentPolicy.IsVisible(folder, ManagerContentMode.Media, showHiddenFiles:=False))
        Assert.True(ManagerContentPolicy.IsVisible(folder, ManagerContentMode.Documents, showHiddenFiles:=False))
        Assert.True(ManagerContentPolicy.IsVisible(folder, ManagerContentMode.All, showHiddenFiles:=False))
    End Sub

    <Fact>
    Public Sub HiddenAndSystemFolders_RespectHiddenFilesSwitch()
        Dim hiddenFolder = MakeEntry(".hiddenDir", isDir:=True, isHidden:=True)
        Dim systemFolder = MakeEntry("SystemVolume", isDir:=True, isSystem:=True)

        ' With showHiddenFiles:=False, excluded
        Assert.False(ManagerContentPolicy.IsVisible(hiddenFolder, ManagerContentMode.Media, showHiddenFiles:=False))
        Assert.False(ManagerContentPolicy.IsVisible(systemFolder, ManagerContentMode.Media, showHiddenFiles:=False))
        Assert.False(ManagerContentPolicy.IsVisible(hiddenFolder, ManagerContentMode.All, showHiddenFiles:=False))

        ' With showHiddenFiles:=True, visible
        Assert.True(ManagerContentPolicy.IsVisible(hiddenFolder, ManagerContentMode.Media, showHiddenFiles:=True))
        Assert.True(ManagerContentPolicy.IsVisible(systemFolder, ManagerContentMode.Media, showHiddenFiles:=True))
        Assert.True(ManagerContentPolicy.IsVisible(hiddenFolder, ManagerContentMode.All, showHiddenFiles:=True))
    End Sub

    <Theory>
    <InlineData("photo.jpg", True, True, True)>
    <InlineData("PHOTO.PNG", True, True, True)>
    <InlineData("clip.mp4", True, True, True)>
    <InlineData("song.mp3", True, True, True)>
    <InlineData("stream.m3u", True, True, True)>
    <InlineData("archive.zip", True, True, True)>
    <InlineData("archive.7z", True, True, True)>
    <InlineData("vault.fd-sec", True, True, True)>
    Public Sub SupportedMedia_VisibleInAllModes_CaseInsensitive(name As String, mediaOk As Boolean, docOk As Boolean, allOk As Boolean)
        Dim entry = MakeEntry(name)
        Assert.Equal(mediaOk, ManagerContentPolicy.IsVisible(entry, ManagerContentMode.Media, False))
        Assert.Equal(docOk, ManagerContentPolicy.IsVisible(entry, ManagerContentMode.Documents, False))
        Assert.Equal(allOk, ManagerContentPolicy.IsVisible(entry, ManagerContentMode.All, False))
    End Sub

    <Theory>
    <InlineData("document.pdf")>
    <InlineData("DOCUMENT.PDF")>
    <InlineData("book.epub")>
    <InlineData("notes.txt")>
    <InlineData("sheet.xlsx")>
    <InlineData("report.docx")>
    Public Sub DocumentsAndBooks_HiddenInMedia_VisibleInDocsAndAll(name As String)
        Dim entry = MakeEntry(name)
        Assert.False(ManagerContentPolicy.IsVisible(entry, ManagerContentMode.Media, False))
        Assert.True(ManagerContentPolicy.IsVisible(entry, ManagerContentMode.Documents, False))
        Assert.True(ManagerContentPolicy.IsVisible(entry, ManagerContentMode.All, False))
    End Sub

    <Theory>
    <InlineData("unknown.xyz")>
    <InlineData("disk.iso")>
    <InlineData("archive.tar")>
    <InlineData("program.exe")>
    <InlineData("script.bat")>
    Public Sub UnknownAndUnsupported_OnlyVisibleInAllFiles(name As String)
        Dim entry = MakeEntry(name)
        Assert.False(ManagerContentPolicy.IsVisible(entry, ManagerContentMode.Media, False))
        Assert.False(ManagerContentPolicy.IsVisible(entry, ManagerContentMode.Documents, False))
        Assert.True(ManagerContentPolicy.IsVisible(entry, ManagerContentMode.All, False))
    End Sub

    <Fact>
    Public Sub ZeroLengthFile_FilteredByKind_NotLength()
        Dim zeroJpg = MakeEntry("empty.jpg", length:=0L)
        Dim zeroPdf = MakeEntry("empty.pdf", length:=0L)
        Dim zeroXyz = MakeEntry("empty.xyz", length:=0L)

        ' JPG is supported media
        Assert.True(ManagerContentPolicy.IsVisible(zeroJpg, ManagerContentMode.Media, False))
        ' PDF is doc
        Assert.False(ManagerContentPolicy.IsVisible(zeroPdf, ManagerContentMode.Media, False))
        Assert.True(ManagerContentPolicy.IsVisible(zeroPdf, ManagerContentMode.Documents, False))
        ' XYZ is unknown
        Assert.False(ManagerContentPolicy.IsVisible(zeroXyz, ManagerContentMode.Media, False))
        Assert.True(ManagerContentPolicy.IsVisible(zeroXyz, ManagerContentMode.All, False))
    End Sub

    <Theory>
    <InlineData("Thumbs.db")>
    <InlineData("desktop.ini")>
    <InlineData("scratch.tmp")>
    <InlineData(".DS_Store")>
    Public Sub JunkFiles_ExcludedFromMediaAndDocs_AllowedInAllFiles(junkName As String)
        Dim entry = MakeEntry(junkName)
        Assert.False(ManagerContentPolicy.IsVisible(entry, ManagerContentMode.Media, False))
        Assert.False(ManagerContentPolicy.IsVisible(entry, ManagerContentMode.Documents, False))
        Assert.True(ManagerContentPolicy.IsVisible(entry, ManagerContentMode.All, False))
    End Sub

    <Fact>
    Public Sub ParseAndModeToString_Roundtrips()
        Assert.Equal(ManagerContentMode.Media, ManagerContentPolicy.ParseMode("media"))
        Assert.Equal(ManagerContentMode.Documents, ManagerContentPolicy.ParseMode("documents"))
        Assert.Equal(ManagerContentMode.All, ManagerContentPolicy.ParseMode("all"))
        Assert.Equal(ManagerContentMode.Media, ManagerContentPolicy.ParseMode("unknown"))

        Assert.Equal("media", ManagerContentPolicy.ModeToString(ManagerContentMode.Media))
        Assert.Equal("documents", ManagerContentPolicy.ModeToString(ManagerContentMode.Documents))
        Assert.Equal("all", ManagerContentPolicy.ModeToString(ManagerContentMode.All))
    End Sub

End Class
#End If
