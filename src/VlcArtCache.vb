#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq

''' <summary>
''' LibVLC's cover-art cache, and the one thing the viewer does with it: take back what the
''' playback of a secure container put there (audit 046, G-04).
'''
''' LibVLC 3 extracts an embedded cover it is handed as an attachment - a FLAC PICTURE
''' block, an MP4 <c>covr</c>, a Matroska cover attachment - into
''' <c>&lt;user dir&gt;\art\artistalbum\&lt;artist&gt;\&lt;album&gt;\art.jpg</c> (or
''' <c>art\arturl\&lt;md5&gt;</c>, <c>art\title\&lt;title&gt;</c>) the moment the demuxer
''' opens, for ANY input - the decrypting StreamMediaInput included - and then points the
''' media's ArtworkURL at that file. For a container that is the sealed cover in plaintext
''' AND the sealed artist and album as directory names, on disk, for good.
'''
''' Proven with a harness against the shipped 3.0.21 runtime, and so was the absence of a
''' switch: <c>--no-metadata-network-access</c> and <c>--album-art</c> leave the attachment
''' path alone, and <c>:sout=#display</c> - the only thing that does skip it - detaches the
''' player's volume and mute. So the secure path deletes the file on sight instead, from
''' the media's MetaChanged, a few milliseconds after LibVLC wrote it. That residual window
''' is a recorded deviation from "no decrypted byte ever reaches the disk", not a cure.
'''
''' The planning half is pure and tested; only <see cref="Scrub"/> touches the disk.
''' Modern-only, like secure containers themselves.
''' </summary>
Friend Module VlcArtCache

    ''' <summary>What one scrub may remove: the art file, then the directories between it
    ''' and the cache's category folder (artistalbum, arturl, title), deepest first. Those
    ''' directories carry the sealed names, so they go too - but only while empty, because
    ''' a directory that holds anything else predates this playback.</summary>
    Friend NotInheritable Class ArtScrubPlan
        Friend ReadOnly Property FilePath As String
        Friend ReadOnly Property Directories As IReadOnlyList(Of String)

        Friend Sub New(filePath As String, directories As IReadOnlyList(Of String))
            Me.FilePath = filePath
            Me.Directories = directories
        End Sub
    End Class

    ''' <summary>
    ''' Where LibVLC 3 keeps its art cache on Windows: <c>&lt;exe dir&gt;\portable\art</c>
    ''' when a <c>portable</c> directory sits beside the executable, otherwise
    ''' <c>%APPDATA%\vlc\art</c>. Both are returned - which one is live is LibVLC's
    ''' decision at the moment it writes, and a scrub that guessed wrong would delete
    ''' nothing.
    ''' </summary>
    Friend Function ArtRoots() As String()
        Dim roots As New List(Of String)()
        Try
            Dim exeDir As String = Path.GetDirectoryName(Environment.ProcessPath)
            If Not String.IsNullOrEmpty(exeDir) Then roots.Add(Path.Combine(exeDir, "portable", "art"))
        Catch
        End Try
        Try
            Dim appData As String = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            If Not String.IsNullOrEmpty(appData) Then roots.Add(Path.Combine(appData, "vlc", "art"))
        Catch
        End Try
        Return roots.ToArray()
    End Function

    ''' <summary>
    ''' What a scrub of <paramref name="artUrl"/> may remove, or Nothing when the URL is not
    ''' a file LibVLC's art cache wrote. Deliberately narrow, because this deletes files: a
    ''' <c>file:</c> URL, inside one of <paramref name="cacheRoots"/>, at least one folder
    ''' below the category folder, named <c>art</c> or <c>art.&lt;ext&gt;</c> - which is the
    ''' only name LibVLC gives a cached cover. An <c>attachment://</c> URL (not extracted
    ''' yet) and anything else answer Nothing.
    ''' </summary>
    Friend Function PlanScrub(artUrl As String, cacheRoots As IEnumerable(Of String)) As ArtScrubPlan
        If String.IsNullOrEmpty(artUrl) OrElse cacheRoots Is Nothing Then Return Nothing
        If Not artUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase) Then Return Nothing

        Dim localPath As String
        Try
            localPath = Path.GetFullPath(New Uri(artUrl).LocalPath)
        Catch
            Return Nothing
        End Try

        Dim name As String = Path.GetFileName(localPath)
        If Not (String.Equals(name, "art", StringComparison.OrdinalIgnoreCase) OrElse
                name.StartsWith("art.", StringComparison.OrdinalIgnoreCase)) Then Return Nothing

        Dim separator As Char = Path.DirectorySeparatorChar
        For Each root As String In cacheRoots
            If String.IsNullOrEmpty(root) Then Continue For
            Dim rootPrefix As String
            Try
                rootPrefix = Path.GetFullPath(root).TrimEnd(separator) & separator
            Catch
                Continue For
            End Try
            If Not localPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) Then Continue For

            ' category\<one or more folders>\art.jpg - anything shallower is not a layout
            ' LibVLC writes, and is left alone.
            Dim parts As String() = localPath.Substring(rootPrefix.Length).Split(separator)
            If parts.Length < 3 Then Return Nothing

            Dim categoryDir As String = rootPrefix & parts(0)
            Dim directories As New List(Of String)()
            Dim current As String = Path.GetDirectoryName(localPath)
            While current IsNot Nothing AndAlso current.Length > categoryDir.Length AndAlso
                  current.StartsWith(categoryDir & separator, StringComparison.OrdinalIgnoreCase)
                directories.Add(current)
                current = Path.GetDirectoryName(current)
            End While
            Return New ArtScrubPlan(localPath, directories)
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' Removes what <see cref="PlanScrub"/> allows for <paramref name="artUrl"/>. True when
    ''' there was something of LibVLC's to remove. A failure to delete the art file itself
    ''' throws - the caller logs it WITHOUT the path, since the path is the sealed artist and
    ''' album - while a directory that cannot go simply stays: it is empty by then.
    ''' </summary>
    ' NOTE: not "artRoots" - VB is case-insensitive, and a parameter by that name shadows
    ' the ArtRoots() function it defaults to.
    Friend Function Scrub(artUrl As String, Optional cacheRoots As IEnumerable(Of String) = Nothing) As Boolean
        Dim plan As ArtScrubPlan = PlanScrub(artUrl, If(cacheRoots, ArtRoots()))
        If plan Is Nothing Then Return False

        If File.Exists(plan.FilePath) Then File.Delete(plan.FilePath)
        For Each dir As String In plan.Directories
            Try
                If Not Directory.Exists(dir) Then Continue For
                If Directory.EnumerateFileSystemEntries(dir).Any() Then Exit For
                Directory.Delete(dir, recursive:=False)
            Catch ex As Exception
                ' Never the path - its folder names ARE the sealed artist and album. The
                ' release scrub tries again; this line says it was needed (057.S08-9).
                AppFileLogger.WriteLine("LibVLC art cache: an emptied folder could not be removed (" & ex.GetType().Name & ")")
                Exit For
            End Try
        Next
        Return True
    End Function

End Module
#End If
