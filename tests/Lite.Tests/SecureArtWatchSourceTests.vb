Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Every place the viewer starts a LibVLC media built by CreateVlcMedia must attach the
''' art-cache watch first (ticket 092, a gap in audit 046 G-04).
'''
''' LibVLC writes an embedded cover - and the sealed artist/album as folder names - to its
''' art cache for ANY input, a container's decrypting stream included, which breaks the
''' .fd-sec boundary (no decrypted byte ever reaches the disk). The watch
''' (Main_Form.Secure.vb WatchSecureMediaArt) is what deletes it, and it has to be attached
''' BEFORE Play, because LibVLC writes the cover while the input starts. The first play
''' path had it; the "reopen at the same second" path (RestartCurrentVideoPreservingPosition,
''' which Repeat, A-B and the end of a pass go through) built its own Media and played it
''' bare, so the cover stayed on disk for the rest of the playback.
'''
''' What this proves is the ROUTING, not the extraction: Main_Form is a WinForms class the
''' test project does not link, so the shipped source is read the way FileIngressTests reads
''' it. The behaviour itself - nothing in the art roots after a reopen - is the hand-run
''' tests\Integration\SecureRepeatArtScenes.ps1, because the extraction happens inside
''' LibVLC's demuxer and a unit test cannot see it.
''' </summary>
Public Class SecureArtWatchSourceTests

    ''' <summary>"vlc_Media_Player.Play(media)" - Play WITH a media. A bare Play() only
    ''' resumes the one already loaded and starts nothing new.</summary>
    Private Shared ReadOnly PlayWithMedia As New Regex("\bvlc_Media_Player\.Play\(\s*(?<arg>[A-Za-z_][\w.]*)\s*\)", RegexOptions.Compiled)

    ''' <summary>How far above a Play the Media it plays may have been created.</summary>
    Private Const Look_Back_Lines As Integer = 40

    ''' <summary>How far below a Play the release of that Media has to appear.</summary>
    Private Const Look_Ahead_Lines As Integer = 3

    <Fact>
    Public Sub Every_play_of_a_CreateVlcMedia_media_attaches_the_art_watch_first()
        Dim sites As New List(Of String)()
        Dim problems As New List(Of String)()

        For Each srcPath In Directory.GetFiles(SourceDir(), "*.vb", SearchOption.AllDirectories)
            If IsBuildOutput(srcPath) Then Continue For
            Dim lines = BlankComments(File.ReadAllText(srcPath)).Replace(vbCr, "").Split(ChrW(10))
            Dim fileName = Path.GetFileName(srcPath)

            For i As Integer = 0 To lines.Length - 1
                Dim play = PlayWithMedia.Match(lines(i))
                If Not play.Success Then Continue For
                Dim media As String = play.Groups("arg").Value

                ' Which Media is this? Only one built here by CreateVlcMedia concerns the
                ' watch - the live broadcast plays its own network media, never a container.
                Dim created As Integer = -1
                Dim creation As New Regex("\b" & Regex.Escape(media) & "\s+As\s+[\w.]+\s*=\s*CreateVlcMedia\(")
                For j As Integer = i - 1 To Math.Max(0, i - Look_Back_Lines) Step -1
                    If creation.IsMatch(lines(j)) Then
                        created = j
                        Exit For
                    End If
                Next
                If created < 0 Then Continue For

                Dim site As String = fileName & ":" & (i + 1)
                sites.Add(fileName)

                Dim between = String.Join(vbLf, lines.Skip(created + 1).Take(i - created - 1))
                If Not Regex.IsMatch(between, "WatchSecureMediaArt\(\s*\w+\s*,\s*" & Regex.Escape(media) & "\s*\)") Then
                    problems.Add(site & " plays '" & media & "' without WatchSecureMediaArt(path, " & media & ") before it")
                End If

                ' A watched media is KEPT (the watch owns it, ReleaseSecureArtWatch lets it
                ' go); disposing it right after Play would end the watch before it began.
                Dim after = String.Join(vbLf, lines.Skip(i + 1).Take(Look_Ahead_Lines))
                If Not Regex.IsMatch(after, "If\s+Not\s+\w+\s+Then\s+" & Regex.Escape(media) & "\.Dispose\(\)") Then
                    problems.Add(site & " does not release '" & media & "' conditionally (If Not <watched> Then " & media & ".Dispose())")
                End If
            Next
        Next

        ' A scan that found nothing would pass every check above, so pin the two landmarks
        ' the real code cannot lose: the first play and the reopen.
        Assert.Contains("Main_Form.VideoPlayer.vb", sites)
        Assert.Contains("Main_Form.VideoMenu.vb", sites)
        Assert.True(problems.Count = 0, String.Join(vbLf, problems))
    End Sub

    ' ------------------------------------------------------------------ helpers ----

    ''' <summary>obj\ and bin\ hold generated copies that would double every count.</summary>
    Private Shared Function IsBuildOutput(srcPath As String) As Boolean
        Return srcPath.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               srcPath.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    ''' <summary>Whole-line comments only - a comment quoting the call is documentation,
    ''' not a call. The line count is kept.</summary>
    Private Shared Function BlankComments(text As String) As String
        Return Regex.Replace(text, "(?m)^[ \t]*'.*$", "")
    End Function

    Private Shared Function SourceDir() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then
                Return candidate
            End If
            dir = dir.Parent
        End While
        Assert.True(False, "Could not locate the src\ directory from the test binary.")
        Return Nothing
    End Function

End Class
