#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Windows.Forms

' Audio / subtitle track picking - modern build only
' (SPECIFICATION_MKV_ISO_PLAYBACK_DOTNET10.md, O-ISO-7). An MKV routinely carries a
' Russian and an English audio track plus a couple of subtitle streams, and until now
' the app played whatever VLC picked first with no way to say otherwise.
'
' WHY MODERN ONLY, and this one is NOT arbitrary: on net48 a video is handed to the IE
' WebBrowser first and only reaches LibVLC when IE refuses it. Track control exists
' only for the LibVLC half, so on net48 the same button would work for an MKV and do
' nothing for an MP4 - a control that silently depends on which decoder won is worse
' than no control. Modern has one engine, so the answer is always the same.
'
' The button follows the translate-buttons pattern: it lives in the toolbar row and is
' collapsed out of the layout entirely (SetToolbarItemHidden) unless there is actually
' a choice to make - no dead chrome on an app that is mostly about images.
Partial Public Class Main_Form

    Private WithEvents btn_Tracks As Button
    Private tracks_Menu As ContextMenuStrip

    ''' <summary>
    ''' The user's standing preference, keyed by ISO LANGUAGE CODE ("rus", "eng") - not
    ''' by id and not by name. Verified against the real engine: VLC's track ids are
    ''' per-file, so "track 2" means nothing in the next episode; and the display name
    ''' is a composite ("Russian - [Russian]") that only matches when two files came
    ''' from the same release. The language code is what actually survives, and it is
    ''' available: player.Media.Tracks carries a clean Language field even after the
    ''' Media object is disposed.
    ''' Empty = no preference, follow whatever VLC picks.
    ''' </summary>
    Private preferred_Audio_Language As String = ""
    Private preferred_Subtitle_Language As String = ""

    ''' <summary>"Subtitles off" is itself a preference worth keeping - it must be
    ''' distinguishable from "no preference". No ISO code collides with this.</summary>
    Private Const Subtitle_Off_Key As String = "::off::"

    ''' <summary>VLC's own id for "no track" - it appears as a "Disable" entry at the
    ''' head of every track list.</summary>
    Private Const Track_Disabled_Id As Integer = -1

    ''' <summary>Set while we apply a preference, so the resulting track change is not
    ''' mistaken for the user picking something and re-saved.</summary>
    Private applying_Track_Preference As Boolean = False

    ''' <summary>Creates the toolbar button. Called from BuildModernLayout next to the
    ''' other code-built buttons so it inherits the uniform chrome.</summary>
    Friend Sub BuildVideoTracksToolbarControls(host As Panel)
        If btn_Tracks IsNot Nothing OrElse host Is Nothing Then Return
        btn_Tracks = New Button With {
            .Name = "btn_Tracks",
            .Text = Localization.T("Дорожки"),
            .AutoSize = True,
            .TabStop = False,
            .Visible = False
        }
        host.Controls.Add(btn_Tracks)

        tracks_Menu = New ContextMenuStrip()
        AddHandler tracks_Menu.Opening, Sub(s, e) BuildTracksMenu()
    End Sub

    Friend Sub LocalizeVideoTracks()
        If btn_Tracks IsNot Nothing Then
            btn_Tracks.Text = Localization.T("Дорожки")
            If toolTip IsNot Nothing Then
                toolTip.SetToolTip(btn_Tracks, Localization.T("Звуковые дорожки и субтитры.  A - следующая звуковая, V - следующие субтитры."))
            End If
        End If
    End Sub

    ''' <summary>Counts the REAL entries of a track list. VLC puts a "Disable" item
    ''' (id -1) at the head of every list, so Length alone says a one-audio-track MP4
    ''' has two options - and the button would appear on files with nothing to pick.</summary>
    Private Shared Function RealTrackCount(tracks As LibVLCSharp.Shared.Structures.TrackDescription()) As Integer
        If tracks Is Nothing Then Return 0
        Dim n As Integer = 0
        For Each t In tracks
            If t.Id <> Track_Disabled_Id Then n += 1
        Next
        Return n
    End Function

    ''' <summary>
    ''' True when a video is playing and there is something to pick between: more than
    ''' one audio track, or any subtitle at all (subtitles are a choice even when there
    ''' is one - on or off). A plain MP4 with a single audio track and no subtitles is
    ''' not a choice, and gets no button.
    ''' </summary>
    Private Function HasTrackChoice() As Boolean
        If vlc_Media_Player Is Nothing OrElse Not is_Vlc_Playing Then Return False
        Try
            Return RealTrackCount(vlc_Media_Player.AudioTrackDescription) > 1 OrElse
                   RealTrackCount(vlc_Media_Player.SpuDescription) >= 1
        Catch
            Return False   ' the player can be torn down under us mid-query
        End Try
    End Function

    ''' <summary>
    ''' The ISO language code of a track, lowercased, or "" when the file does not say
    ''' ("und" is VLC's "undefined" - as good as nothing for remembering a choice).
    ''' Read off player.Media, which keeps its track list after the Media object we
    ''' handed to Play() has been disposed (verified against the engine).
    ''' </summary>
    Private Function TrackLanguage(id As Integer, audio As Boolean) As String
        If id = Track_Disabled_Id OrElse vlc_Media_Player Is Nothing Then Return ""
        Try
            Dim media = vlc_Media_Player.Media
            If media Is Nothing OrElse media.Tracks Is Nothing Then Return ""
            Dim want = If(audio, LibVLCSharp.Shared.TrackType.Audio, LibVLCSharp.Shared.TrackType.Text)
            For Each t In media.Tracks
                If t.TrackType = want AndAlso t.Id = id Then
                    Dim lang As String = If(t.Language, "").Trim().ToLowerInvariant()
                    If lang.Length = 0 OrElse lang = "und" Then Return ""
                    Return lang
                End If
            Next
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2475: TrackLanguage failed: " & ex.Message)
        End Try
        Return ""
    End Function

    ''' <summary>The id of the track speaking <paramref name="language"/>, or
    ''' Integer.MinValue when this file has no such track - in which case the caller
    ''' leaves VLC's own pick alone rather than forcing an arbitrary one.</summary>
    Private Function IdOfTrackInLanguage(language As String, audio As Boolean) As Integer
        If String.IsNullOrEmpty(language) OrElse vlc_Media_Player Is Nothing Then Return Integer.MinValue
        Try
            Dim media = vlc_Media_Player.Media
            If media Is Nothing OrElse media.Tracks Is Nothing Then Return Integer.MinValue
            Dim want = If(audio, LibVLCSharp.Shared.TrackType.Audio, LibVLCSharp.Shared.TrackType.Text)
            For Each t In media.Tracks
                If t.TrackType = want AndAlso String.Equals(If(t.Language, ""), language, StringComparison.OrdinalIgnoreCase) Then
                    Return t.Id
                End If
            Next
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2476: IdOfTrackInLanguage failed: " & ex.Message)
        End Try
        Return Integer.MinValue
    End Function

    ''' <summary>Shows or collapses the button. Cheap enough to call on every media
    ''' change and on VLC's Playing event (tracks are not known until then).</summary>
    Friend Sub ApplyVideoTracksButtonVisibility()
        If btn_Tracks Is Nothing Then Return
        SetToolbarItemHidden(btn_Tracks, Not HasTrackChoice())
        LayoutToolbar()
    End Sub

    Private Sub btn_Tracks_Click(sender As Object, e As EventArgs) Handles btn_Tracks.Click
        If tracks_Menu Is Nothing OrElse btn_Tracks Is Nothing Then Return
        tracks_Menu.Show(btn_Tracks, New Drawing.Point(0, btn_Tracks.Height))
    End Sub

    ''' <summary>Rebuilds the menu each time it opens - the track list belongs to the
    ''' file currently playing, not to the button.</summary>
    Private Sub BuildTracksMenu()
        tracks_Menu.Items.Clear()
        If vlc_Media_Player Is Nothing Then Return

        Try
            AddTrackSection(Localization.T("Звук"),
                            vlc_Media_Player.AudioTrackDescription,
                            vlc_Media_Player.AudioTrack,
                            Sub(id) SetAudioTrack(id))

            Dim spu = vlc_Media_Player.SpuDescription
            If spu IsNot Nothing AndAlso spu.Length > 0 Then
                tracks_Menu.Items.Add(New ToolStripSeparator())
                AddTrackSection(Localization.T("Субтитры"),
                                spu, vlc_Media_Player.Spu,
                                Sub(id) SetSubtitleTrack(id))
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2470: track menu failed: " & ex.Message)
        End Try
    End Sub

    Private Sub AddTrackSection(header As String,
                                tracks As LibVLCSharp.Shared.Structures.TrackDescription(),
                                current_Id As Integer,
                                apply As Action(Of Integer))
        Dim caption As New ToolStripMenuItem(header) With {.Enabled = False}
        tracks_Menu.Items.Add(caption)
        If tracks Is Nothing Then Return

        AddTrackItems(tracks_Menu.Items, tracks, current_Id, apply)
    End Sub

    ''' <summary>The track list as menu items, into whatever collection asked for them -
    ''' the button's flat menu here, a submenu of the video's right-click menu there.</summary>
    Private Shared Sub AddTrackItems(target As ToolStripItemCollection,
                                     tracks As LibVLCSharp.Shared.Structures.TrackDescription(),
                                     current_Id As Integer,
                                     apply As Action(Of Integer))
        For Each track In tracks
            Dim id As Integer = track.Id
            Dim item As New ToolStripMenuItem(TrackLabel(track)) With {.Checked = (id = current_Id)}
            AddHandler item.Click, Sub() apply(id)
            target.Add(item)
        Next
    End Sub

    ''' <summary>
    ''' The same track list the toolbar button shows, packaged as one submenu for the
    ''' video's right-click menu (Main_Form.VideoMenu.vb). Nothing when there is nothing to
    ''' pick - by the same rule HasTrackChoice uses for the button, so the two surfaces
    ''' never disagree about whether this file offers a choice.
    ''' </summary>
    Friend Function CreateTrackSubmenu(audio As Boolean) As ToolStripMenuItem
        If vlc_Media_Player Is Nothing OrElse Not is_Vlc_Playing Then Return Nothing

        Try
            Dim tracks = If(audio, vlc_Media_Player.AudioTrackDescription, vlc_Media_Player.SpuDescription)
            Dim real_Count As Integer = RealTrackCount(tracks)
            ' One audio track is not a choice; one subtitle track is (on or off).
            If If(audio, real_Count < 2, real_Count < 1) Then Return Nothing

            Dim apply As Action(Of Integer)
            If audio Then apply = Sub(id) SetAudioTrack(id) Else apply = Sub(id) SetSubtitleTrack(id)

            Dim root As New ToolStripMenuItem(If(audio,
                                                 Localization.T("Звуковая дорожка (A)"),
                                                 Localization.T("Субтитры (V)")))
            AddTrackItems(root.DropDownItems, tracks,
                          If(audio, vlc_Media_Player.AudioTrack, vlc_Media_Player.Spu), apply)
            Return root
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2477: track submenu failed: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' What to call a track in the menu. VLC hands back its own composite name
    ''' ("Russian - [Russian]"), which is fine, but its "Disable" placeholder comes back
    ''' in English regardless of our UI language - so that one entry is ours to name.
    ''' A nameless track falls back to its id rather than a blank line.
    ''' </summary>
    Private Shared Function TrackLabel(track As LibVLCSharp.Shared.Structures.TrackDescription) As String
        If track.Id = Track_Disabled_Id Then Return Localization.T("Отключить")
        If String.IsNullOrWhiteSpace(track.Name) Then Return "#" & track.Id.ToString()
        Return track.Name
    End Function

    Private Sub SetAudioTrack(id As Integer)
        Try
            vlc_Media_Player.SetAudioTrack(id)
            ' A track with no declared language cannot be remembered - clear the
            ' preference rather than keep a stale one that would fight the next pick.
            If Not applying_Track_Preference Then preferred_Audio_Language = TrackLanguage(id, audio:=True)
            AnnounceTrack(Localization.T("Звук"), CurrentTrackName(audio:=True))
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2471: SetAudioTrack failed: " & ex.Message)
        End Try
    End Sub

    Private Sub SetSubtitleTrack(id As Integer)
        Try
            vlc_Media_Player.SetSpu(id)
            If Not applying_Track_Preference Then
                ' "Off" is a preference in its own right: someone who turned subtitles
                ' off wants them off in the next file too.
                preferred_Subtitle_Language = If(id = Track_Disabled_Id, Subtitle_Off_Key, TrackLanguage(id, audio:=False))
            End If
            AnnounceTrack(Localization.T("Субтитры"), CurrentTrackName(audio:=False))
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2472: SetSpu failed: " & ex.Message)
        End Try
    End Sub

    ''' <summary>What to call the track now playing, for the status line.</summary>
    Private Function CurrentTrackName(audio As Boolean) As String
        Dim tracks = If(audio, vlc_Media_Player.AudioTrackDescription, vlc_Media_Player.SpuDescription)
        Dim id As Integer = If(audio, vlc_Media_Player.AudioTrack, vlc_Media_Player.Spu)
        If tracks Is Nothing Then Return ""
        For Each t In tracks
            If t.Id = id Then Return TrackLabel(t)
        Next
        Return ""
    End Function

    ''' <summary>The status line is the whole feedback channel for the hotkeys - cycling
    ''' blind would be useless.</summary>
    Private Sub AnnounceTrack(kind As String, name As String)
        If String.IsNullOrEmpty(name) Then Return
        lbl_Status.Text = kind & ": " & name
    End Sub

    ''' <summary>
    ''' Steps to the next entry of a track list. Returns True when it did something, so
    ''' the key handler knows whether to swallow the key.
    ''' </summary>
    Private Function CycleTrack(audio As Boolean) As Boolean
        If vlc_Media_Player Is Nothing OrElse Not is_Vlc_Playing Then Return False
        Try
            Dim tracks = If(audio, vlc_Media_Player.AudioTrackDescription, vlc_Media_Player.SpuDescription)
            If tracks Is Nothing OrElse tracks.Length < 2 Then Return False

            Dim current As Integer = If(audio, vlc_Media_Player.AudioTrack, vlc_Media_Player.Spu)
            Dim at As Integer = -1
            For i As Integer = 0 To tracks.Length - 1
                If tracks(i).Id = current Then at = i : Exit For
            Next
            Dim [next] As Integer = tracks((at + 1) Mod tracks.Length).Id

            If audio Then SetAudioTrack([next]) Else SetSubtitleTrack([next])
            Return True
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2473: CycleTrack failed: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' A / V - next audio / next subtitles. VLC's own keys are B and V; B is already
    ''' "previous file" here (14 years of muscle memory outrank VLC's), so audio takes
    ''' the free and more obvious A.
    ''' Returns True when the key was consumed.
    ''' </summary>
    Friend Function TryHandleTrackKey(e As KeyEventArgs) As Boolean
        Select Case e.KeyCode
            Case Keys.A
                Return CycleTrack(audio:=True)
            Case Keys.V
                Return CycleTrack(audio:=False)
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>
    ''' Re-applies the remembered choice to a newly started video. Called on VLC's
    ''' Playing event - the track list does not exist before that. Matching is by name
    ''' because ids are per-file; a file without the preferred language simply keeps
    ''' VLC's own pick rather than getting an arbitrary one.
    ''' </summary>
    Friend Sub ApplyPreferredTracks()
        If vlc_Media_Player Is Nothing Then Return
        applying_Track_Preference = True
        Try
            If preferred_Audio_Language.Length > 0 Then
                Dim id As Integer = IdOfTrackInLanguage(preferred_Audio_Language, audio:=True)
                If id <> Integer.MinValue Then vlc_Media_Player.SetAudioTrack(id)
            End If

            If preferred_Subtitle_Language = Subtitle_Off_Key Then
                vlc_Media_Player.SetSpu(Track_Disabled_Id)
            ElseIf preferred_Subtitle_Language.Length > 0 Then
                Dim id As Integer = IdOfTrackInLanguage(preferred_Subtitle_Language, audio:=False)
                If id <> Integer.MinValue Then vlc_Media_Player.SetSpu(id)
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2474: ApplyPreferredTracks failed: " & ex.Message)
        Finally
            applying_Track_Preference = False
        End Try
    End Sub

End Class
#End If
