#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports LibVLCSharp.Shared

Partial Public Class Main_Form
    Friend Sub SetMediaDisplayKind(kind As MediaKind)
        current_Media_Display_Kind = kind
    End Sub

    Friend Function IsAudioNowPlaying() As Boolean
        Return current_Media_Display_Kind = MediaKind.Audio
    End Function

    Private audio_Current_Metadata As AudioMetadata
    Private audio_Cover_Bitmap As Bitmap
    Private audio_Sleep_Stopped As Boolean
    ' The no-cover backdrop is WAVE-PARTICLES (ticket 029): the constants and the session arithmetic
    ' live in src/Backdrop/ (WaveParticlesConstants, WaveParticlesSession), this file only draws what
    ' the session hands back and decides when it may move. Surface class: audio player backdrop -
    ' intent `ambient`, profile `full`, no user tuning (contract section 5).
    Private audio_Visualiser_Buffer As Bitmap
    Private audio_Visualiser_Session As WaveParticlesSession
    ''' <summary>Stopwatch timestamp of the previous frame; -1 = none (a start, or a resume after a hold,
    ''' so the gap is never replayed as elapsed time - rule 10).</summary>
    Private audio_Visualiser_Last_Ticks As Long = -1
    ''' <summary>The buffer currently holds the settled still frame of rule 9.</summary>
    Private audio_Visualiser_Still As Boolean
    Private Const Visualiser_Frame_Interval_Ms As Integer = 16
    ''' <summary>While the backdrop is held (minimised, cloaked, motion off, energy saver) the timer only
    ''' polls whether it may move again - no frame, no clock, no repaint.</summary>
    Private Const Visualiser_Hold_Poll_Ms As Integer = 250
    Private WithEvents audio_Visualiser_Timer As New Timer() With {.Interval = Visualiser_Frame_Interval_Ms}

    ''' <summary>Polls the sleep timer while audio plays. Its own timer, NOT the transport bar's tick (121 UX-404): that tick
    ''' stops with the bar, and the bar is never shown in the full screen without panels - music went on past the deadline.</summary>
    Private WithEvents audio_Sleep_Poll_Timer As New Timer() With {.Interval = 1000}

    ''' <summary>How long the tag parse may take before the plate falls back to the file
    ''' name. Handed to libvlc itself, so a slow share ends the parse instead of a guard
    ''' nobody reaches.</summary>
    Private Const Audio_Parse_Timeout_Ms As Integer = 2000

    ''' <summary>
    ''' Parses tags off the UI thread. The generation check prevents a late answer from
    ''' being painted on a file the user has already left.
    '''
    ''' The parse is AWAITED (audit 050 D-02). Media.Parse returns a Task in LibVLCSharp 3,
    ''' and it used to be dropped: the tags were read at once, before libvlc had parsed
    ''' anything, and the Using then released the media - which cancels a pending preparse.
    ''' So every plate showed the file name and "MP3 - 5.2 MB", and no cover but the one
    ''' the hand-written ID3 reader found.
    ''' </summary>
    Friend Async Sub RequestAudioMetadataAsync(filePath As String, generation As Integer)
        Try
            ' The first audio file after a runtime download can get here before libvlc
            ' exists; wait for the same initialisation the player awaits (never a second one).
            If libVlc Is Nothing Then
                If Not Await EnsureVlcInitializedAsync().ConfigureAwait(True) Then Return
                If generation <> media_Generation Then Return
            End If
            Dim tags As AudioMetadata = Await Task.Run(Function() ParseAudioMetadataAsync(filePath, generation)).ConfigureAwait(True)
            If Me.IsDisposed OrElse generation <> media_Generation OrElse
               Not String.Equals(Current_File_Name, filePath, StringComparison.Ordinal) Then Return
            audio_Current_Metadata = tags
            LoadAudioCover(tags.CoverPath, filePath)
            RepaintMedia()
        Catch ex As Exception
            Debug.WriteLine("Audio metadata parse skipped: " & ex.Message)
            ' The only trace in Release (057.S08-7): the track is left without its tag plate.
            AppFileLogger.WriteLine("Audio tags not read [" & ex.GetType().Name & "]: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Reads the tags of one file. Values are taken only from a parse that
    ''' finished; anything else keeps the file-name fallback of AudioMetadata.FromValues.</summary>
    ''' <summary>Ticket 115: one parse at a time. A parse is native work on libvlc's own
    ''' threads, and rapid flipping used to start a new one on top of the last (and on top of
    ''' the player's Play) for every track.</summary>
    Private ReadOnly audio_Parse_Gate As New System.Threading.SemaphoreSlim(1, 1)

    Private Async Function ParseAudioMetadataAsync(filePath As String, generation As Integer) As Task(Of AudioMetadata)
        Dim size As Long = -1
        Try : size = New FileInfo(filePath).Length : Catch : End Try

        ' The window is closing, or the user has left this track already: the answer would
        ' be dropped anyway, so touch nothing native for it.
        If generation <> media_Generation OrElse Not vlc_Lease.TryEnter() Then
            Return AudioMetadata.FromValues(filePath, fileSizeBytes:=size)
        End If
        Try
            Await audio_Parse_Gate.WaitAsync().ConfigureAwait(False)
            Try
                If generation <> media_Generation OrElse libVlc Is Nothing Then
                    Return AudioMetadata.FromValues(filePath, fileSizeBytes:=size)
                End If
                Return Await ParseOneAudioFileAsync(filePath, size).ConfigureAwait(False)
            Finally
                audio_Parse_Gate.Release()
            End Try
        Finally
            vlc_Lease.Leave()
        End Try
    End Function

    Private Async Function ParseOneAudioFileAsync(filePath As String, size As Long) As Task(Of AudioMetadata)
        ' A native crash leaves no managed trace (ticket 116), so the last line of the log is
        ' the only clue to what was open. The file name only, never an address (it can carry a
        ' password) and never the folder.
        If Not IsRemoteMediaUrl(filePath) Then AppFileLogger.WriteLine("Reading audio tags: " & Path.GetFileName(filePath))
        Using media As Media = CreateVlcMedia(filePath)
            ' FetchLocal as well: it is what turns an embedded picture ("attachment://") into
            ' a file:// ArtworkURL the cover loader can open.
            Dim status As MediaParsedStatus = Await media.Parse(MediaParseOptions.ParseLocal Or MediaParseOptions.FetchLocal,
                                                                Audio_Parse_Timeout_Ms).ConfigureAwait(False)
            ' Ticket 115: the continuation of that await runs INLINE on the libvlc thread that
            ' raised the parsed event. Everything below - reading the tracks and, above all,
            ' releasing the Media (which tears down its event manager) - must not happen on
            ' that thread, inside its own callback. Yield puts the rest on the pool.
            Await Task.Yield()
            If status <> MediaParsedStatus.Done Then
                Debug.WriteLine("Audio metadata parse: " & status.ToString() & " for " & filePath)
                Return AudioMetadata.FromValues(filePath, fileSizeBytes:=size)
            End If

            Dim codec As String = ""
            Dim bitrate As Long = 0
            Dim sampleRate As Long = 0
            If media.Tracks IsNot Nothing Then
                For Each candidate As MediaTrack In media.Tracks
                    If candidate.TrackType = TrackType.Audio Then
                        codec = AudioMetadata.CodecName(candidate.Codec)
                        bitrate = CLng(candidate.Bitrate)
                        sampleRate = CLng(candidate.Data.Audio.Rate)
                        Exit For
                    End If
                Next
            End If
            Return AudioMetadata.FromValues(filePath, MediaMeta(media, MetadataType.Title), MediaMeta(media, MetadataType.Artist),
                                            MediaMeta(media, MetadataType.Album), MediaMeta(media, MetadataType.ArtworkURL),
                                            codec, bitrate, sampleRate, media.Duration, size)
        End Using
    End Function

    Private Shared Function MediaMeta(media As Media, kind As MetadataType) As String
        Try : Return If(media.Meta(kind), "") : Catch : Return "" : End Try
    End Function

    Friend Sub PaintAudioSurface(g As Graphics, bounds As Rectangle)
        If Not IsAudioNowPlaying() Then Return
        ' The surface is ours, all of it (audit 050 D-04): Picture_Box_1 may still hold the
        ' previous photo, or receive the next one from the prefetch while the track plays,
        ' and the aspect-fit cover and the plate used to be drawn over it - the photo showed
        ' around a square cover and under the plate. The base class has painted the image
        ' by the time this Paint handler runs, so clearing here covers every such case.
        g.Clear(Me.BackColor)
        If audio_Current_Metadata Is Nothing Then Return
        If audio_Cover_Bitmap IsNot Nothing Then
            audio_Visualiser_Timer.Stop()
            DrawAudioCover(g, audio_Cover_Bitmap, bounds)
        ElseIf modern_Preferences IsNot Nothing AndAlso modern_Preferences.AudioVisualiser Then
            PaintAudioVisualiser(g, bounds)
            StartAudioVisualiserTimer()
        Else
            audio_Visualiser_Timer.Stop()
        End If
        Dim top = Math.Max(12, bounds.Height - 118)
        EnsureAudioPaintObjects()
        g.FillRectangle(audio_Plate_Brush, New Rectangle(12, top, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - top - 8)))
        Dim line = top + 8
        g.DrawString(audio_Current_Metadata.Title, audio_Title_Font, audio_Title_Brush, New Rectangle(22, line, bounds.Width - 44, 28), audio_Plate_Format) : line += 30
        If audio_Current_Metadata.Artist <> "" Then g.DrawString(audio_Current_Metadata.Artist, audio_Detail_Font, audio_Detail_Brush, New Rectangle(22, line, bounds.Width - 44, 20), audio_Plate_Format) : line += 20
        If audio_Current_Metadata.Album <> "" Then g.DrawString(audio_Current_Metadata.Album, audio_Detail_Font, audio_Detail_Brush, New Rectangle(22, line, bounds.Width - 44, 20), audio_Plate_Format) : line += 20
        g.DrawString(audio_Current_Metadata.FormatLine, audio_Detail_Font, audio_Format_Brush, New Rectangle(22, line, bounds.Width - 44, 20), audio_Plate_Format)
    End Sub

    ' The plate's and the visualiser's GDI+ objects, made once and reused (audit 050 D-21).
    ' They used to be created and disposed inside every 16 ms frame - two fonts, three
    ' brushes and a StringFormat per paint, a Pen and a GraphicsPath per wave and a brush per
    ' particle per tick: thousands of GDI+ handles a second for as long as an uncovered
    ' track was on screen. Disposed in ClearAudioSurface.
    '
    ' Every colour below comes from AudioOverlayColours: this surface is a content overlay declared outside the
    ' theme (APP-STYLE rule 5), its pair of plate and ink is held at 4.5 : 1 for text and 3 : 1 for strokes by
    ' AudioOverlayContrastTests, and this file carries no colour literal (the same test reads it).
    Private audio_Plate_Brush As SolidBrush
    Private audio_Title_Brush As SolidBrush
    Private audio_Detail_Brush As SolidBrush
    Private audio_Format_Brush As SolidBrush
    Private audio_Title_Font As Font
    Private audio_Detail_Font As Font
    Private audio_Plate_Format As StringFormat
    Private audio_Visualiser_Pen As Pen
    Private audio_Visualiser_Fill As SolidBrush

    Private Sub EnsureAudioPaintObjects()
        If audio_Plate_Brush Is Nothing Then audio_Plate_Brush = New SolidBrush(AudioOverlayColours.PlateBacking)
        If audio_Title_Brush Is Nothing Then audio_Title_Brush = New SolidBrush(AudioOverlayColours.TitleInk)
        If audio_Detail_Brush Is Nothing Then audio_Detail_Brush = New SolidBrush(AudioOverlayColours.DetailInk)
        If audio_Format_Brush Is Nothing Then audio_Format_Brush = New SolidBrush(AudioOverlayColours.FormatInk)
        If audio_Title_Font Is Nothing Then audio_Title_Font = New Font("Segoe UI", 15.0F, FontStyle.Bold)
        If audio_Detail_Font Is Nothing Then audio_Detail_Font = New Font("Segoe UI", 10.0F)
        If audio_Plate_Format Is Nothing Then audio_Plate_Format = New StringFormat With {.Trimming = StringTrimming.EllipsisCharacter, .FormatFlags = StringFormatFlags.NoWrap}
        ' Round caps and joins: WAVE-PARTICLES section 3.2 / 4 ("stroke path, round cap").
        If audio_Visualiser_Pen Is Nothing Then audio_Visualiser_Pen = New Pen(AudioOverlayColours.StrokeInk, 1.0F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
        If audio_Visualiser_Fill Is Nothing Then audio_Visualiser_Fill = New SolidBrush(AudioOverlayColours.StrokeInk)
    End Sub

    Private Sub DisposeAudioPaintObjects()
        If audio_Plate_Brush IsNot Nothing Then audio_Plate_Brush.Dispose() : audio_Plate_Brush = Nothing
        If audio_Title_Brush IsNot Nothing Then audio_Title_Brush.Dispose() : audio_Title_Brush = Nothing
        If audio_Detail_Brush IsNot Nothing Then audio_Detail_Brush.Dispose() : audio_Detail_Brush = Nothing
        If audio_Format_Brush IsNot Nothing Then audio_Format_Brush.Dispose() : audio_Format_Brush = Nothing
        If audio_Title_Font IsNot Nothing Then audio_Title_Font.Dispose() : audio_Title_Font = Nothing
        If audio_Detail_Font IsNot Nothing Then audio_Detail_Font.Dispose() : audio_Detail_Font = Nothing
        If audio_Plate_Format IsNot Nothing Then audio_Plate_Format.Dispose() : audio_Plate_Format = Nothing
        If audio_Visualiser_Pen IsNot Nothing Then audio_Visualiser_Pen.Dispose() : audio_Visualiser_Pen = Nothing
        If audio_Visualiser_Fill IsNot Nothing Then audio_Visualiser_Fill.Dispose() : audio_Visualiser_Fill = Nothing
    End Sub

    ''' <summary>Fills the media surface as far as the source aspect ratio permits;
    ''' an album cover is never stretched into the shape of the application window.</summary>
    Private Shared Sub DrawAudioCover(g As Graphics, cover As Image, bounds As Rectangle)
        If cover.Width <= 0 OrElse cover.Height <= 0 Then Return
        Dim scale = Math.Min(bounds.Width / CDbl(cover.Width), bounds.Height / CDbl(cover.Height))
        Dim width = Math.Max(1, CInt(Math.Round(cover.Width * scale)))
        Dim height = Math.Max(1, CInt(Math.Round(cover.Height * scale)))
        Dim target As New Rectangle(bounds.Left + (bounds.Width - width) \ 2, bounds.Top + (bounds.Height - height) \ 2, width, height)
        g.InterpolationMode = InterpolationMode.HighQualityBicubic
        g.DrawImage(cover, target)
    End Sub

    ''' <summary>
    ''' The no-cover backdrop of an audio file: lines and particles over a trail the frames themselves
    ''' accumulate - the WAVE-PARTICLES contract, surface class "audio player backdrop" (intent
    ''' `ambient`, profile `full`, no tuning). The paint handler only PRESENTS the buffer; the clock
    ''' lives in <see cref="audio_Visualiser_Timer_Tick"/>, so a repaint for any other reason - the video
    ''' controls, an overlay, a resize - never moves the animation (rule 2).
    ''' </summary>
    Private Sub PaintAudioVisualiser(g As Graphics, area As Rectangle)
        If area.Width <= 1 OrElse area.Height <= 1 Then Return
        EnsureAudioVisualiserBuffer(area.Size)
        If audio_Visualiser_Buffer Is Nothing OrElse audio_Visualiser_Session Is Nothing Then Return
        ' Rule 9: motion not allowed means a settled still frame, never an empty or half-washed one.
        ' A session frozen by the power policy that has already been drawing holds its last frame instead.
        If Not audio_Visualiser_Still AndAlso (ReduceMotionActive() OrElse
           (AudioVisualiserPowerFrozen() AndAlso audio_Visualiser_Session.FramesDrawn <= 0.0R)) Then
            RenderAudioVisualiserStillFrame()
        End If
        g.DrawImageUnscaled(audio_Visualiser_Buffer, area.Location)
    End Sub

    ''' <summary>A session is rolled only here, when none exists (a new file - ClearAudioSurface - or a
    ''' preference switched back on after the buffer was dropped: rule 3). A size change keeps it
    ''' (rule 12): the old buffer is carried into the new one stretched, the ramp is not re-run, and only
    ''' the particle positions are re-seeded inside the new bounds.</summary>
    Private Sub EnsureAudioVisualiserBuffer(size As Size)
        If audio_Visualiser_Buffer IsNot Nothing AndAlso audio_Visualiser_Session IsNot Nothing AndAlso audio_Visualiser_Buffer.Size = size Then Return
        Dim previous As Bitmap = audio_Visualiser_Buffer
        audio_Visualiser_Buffer = New Bitmap(size.Width, size.Height)
        If audio_Visualiser_Session Is Nothing Then
            audio_Visualiser_Session = New WaveParticlesSession(New Random(), size.Width, size.Height, BackColor.GetBrightness() > 0.5F)
            audio_Visualiser_Last_Ticks = -1
            audio_Visualiser_Still = False
            FillAudioVisualiserWash()
        Else
            audio_Visualiser_Session.Resize(size.Width, size.Height)
            FillAudioVisualiserWash()
            If previous IsNot Nothing Then
                Using canvas = Graphics.FromImage(audio_Visualiser_Buffer)
                    canvas.InterpolationMode = InterpolationMode.Bilinear
                    canvas.DrawImage(previous, 0, 0, size.Width, size.Height)
                End Using
            End If
            ' A still frame stretched from another size is soft; re-render it at the new one.
            audio_Visualiser_Still = False
        End If
        If previous IsNot Nothing Then previous.Dispose()
    End Sub

    Private Sub FillAudioVisualiserWash()
        Using canvas = Graphics.FromImage(audio_Visualiser_Buffer)
            canvas.Clear(audio_Visualiser_Session.WashColour)
        End Using
    End Sub

    ''' <summary>The surface changed class (a colour scheme change): lightness follows, hues never do
    ''' (rule 7), and the wash is laid again so a dark trail is not left on a light surface.</summary>
    Private Sub SyncAudioVisualiserLightness()
        Dim light As Boolean = BackColor.GetBrightness() > 0.5F
        If light = audio_Visualiser_Session.Light Then Return
        audio_Visualiser_Session.Light = light
        FillAudioVisualiserWash()
        audio_Visualiser_Still = False
    End Sub

    ''' <summary>Rule 9: the frame the animation reaches after RAMP_FRAMES frames, from a buffer filled
    ''' with the wash colour at full opacity, then held.</summary>
    Private Sub RenderAudioVisualiserStillFrame()
        SyncAudioVisualiserLightness()
        Using canvas = Graphics.FromImage(audio_Visualiser_Buffer)
            canvas.Clear(audio_Visualiser_Session.WashColour)
            audio_Visualiser_Session.SettledFrame(Sub(washAlpha As Double) DrawAudioVisualiserPass(canvas, washAlpha))
        End Using
        audio_Visualiser_Still = True
    End Sub

    ''' <summary>One drawn frame covering <paramref name="referenceFrames"/> reference frames.</summary>
    Private Sub AdvanceAudioVisualiser(referenceFrames As Double)
        SyncAudioVisualiserLightness()
        Dim washAlpha As Double = audio_Visualiser_Session.Advance(referenceFrames)
        Using canvas = Graphics.FromImage(audio_Visualiser_Buffer)
            DrawAudioVisualiserPass(canvas, washAlpha)
        End Using
        audio_Visualiser_Still = False
    End Sub

    ''' <summary>Section 4 after <see cref="WaveParticlesSession.Advance"/>: wash, then the lines, then the
    ''' particles. No blur, no second pass - the trail is what the wash leaves behind.</summary>
    Private Sub DrawAudioVisualiserPass(canvas As Graphics, washAlpha As Double)
        Dim session As WaveParticlesSession = audio_Visualiser_Session
        EnsureAudioPaintObjects()
        Dim pen As Pen = audio_Visualiser_Pen
        Dim fill As SolidBrush = audio_Visualiser_Fill
        canvas.SmoothingMode = SmoothingMode.AntiAlias
        fill.Color = Color.FromArgb(CInt(Math.Round(washAlpha * 255.0R)), session.WashColour)
        canvas.FillRectangle(fill, 0, 0, session.Width, session.Height)
        pen.Width = CSng(session.StrokeWidth)
        For lane As Integer = 0 To session.LineCount - 1
            Dim points As PointF() = session.LinePoints(lane)
            If points.Length < 2 Then Continue For
            pen.Color = session.LineColour(lane)
            canvas.DrawLines(pen, points)
        Next
        For i As Integer = 0 To session.Particles.Length - 1
            Dim p As WaveParticlesSession.Particle = session.Particles(i)
            fill.Color = session.ParticleColour(i)
            canvas.FillEllipse(fill, CSng(p.X - p.Radius), CSng(p.Y - p.Radius), CSng(p.Radius * 2.0R), CSng(p.Radius * 2.0R))
        Next
    End Sub

    ''' <summary>Starts the timer. A timer that was stopped (a pause, a cover, the preference off) restarts
    ''' from a fresh timestamp: the time it stood still is not elapsed time (rule 10).</summary>
    Private Sub StartAudioVisualiserTimer()
        If audio_Visualiser_Timer.Enabled Then Return
        audio_Visualiser_Last_Ticks = -1
        audio_Visualiser_Timer.Start()
    End Sub

    ''' <summary>Rule 10: nobody can see it - minimised, hidden, or cloaked (another virtual desktop).
    ''' A window merely covered by another cannot be detected cheaply from WinForms and still draws.</summary>
    Private Function AudioVisualiserUnseen() As Boolean
        Return Not Visible OrElse WindowState = FormWindowState.Minimized OrElse BackdropHostSignals.IsWindowCloaked(Handle)
    End Function

    ''' <summary>Rule 11, intent `ambient`: freezes only at the stricter power level - energy saver on AND
    ''' on battery.</summary>
    Private Shared Function AudioVisualiserPowerFrozen() As Boolean
        Return BackdropHostSignals.IsEnergySaverOnBattery()
    End Function

    Private Sub LoadAudioCover(artworkUrl As String, audioPath As String)
        Try
            Dim coverPath = artworkUrl
            If coverPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase) Then coverPath = New Uri(coverPath).LocalPath
            ' ArtworkURL is supplied by LibVLC for the image embedded in this media
            ' file. Deliberately do not probe cover.jpg/folder.jpg: a neighbouring
            ' image is not this track's miniature and must not suppress the fallback.
            If String.IsNullOrWhiteSpace(coverPath) OrElse Not File.Exists(coverPath) Then
                LoadEmbeddedMp3Cover(audioPath)
                Return
            End If
            Using source As New Bitmap(coverPath)
                Dim copy As New Bitmap(source.Width, source.Height)
                Try
                    Using canvas = Graphics.FromImage(copy)
                        canvas.InterpolationMode = If(Is_HighQuality_Scaling, InterpolationMode.HighQualityBicubic, InterpolationMode.Default)
                        canvas.DrawImage(source, 0, 0, copy.Width, copy.Height)
                    End Using
                Catch
                    ' Ours until it becomes audio_Cover_Bitmap (057.S08-8).
                    copy.Dispose()
                    Throw
                End Try
                If audio_Cover_Bitmap IsNot Nothing Then audio_Cover_Bitmap.Dispose()
                audio_Cover_Bitmap = copy
            End Using
        Catch ex As Exception
            Debug.WriteLine("Audio cover skipped: " & ex.Message)
        End Try
    End Sub

    ''' <summary>LibVLC does not expose APIC on every MP3 build. Read that embedded
    ''' ID3v2 frame directly, without ever treating a neighbouring file as cover art.</summary>
    Private Sub LoadEmbeddedMp3Cover(audioPath As String)
        If Not String.Equals(Path.GetExtension(audioPath), ".mp3", StringComparison.OrdinalIgnoreCase) Then Return
        Try
            Using stream As New FileStream(audioPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Dim header(9) As Byte
                If stream.Read(header, 0, header.Length) <> header.Length OrElse header(0) <> AscW("I"c) OrElse header(1) <> AscW("D"c) OrElse header(2) <> AscW("3"c) Then Return
                Dim version = CInt(header(3))
                If version < 2 OrElse version > 4 Then Return
                Dim tagEnd = Math.Min(stream.Length, 10L + SyncSafeInt(header, 6))
                stream.Position = 10
                While stream.Position < tagEnd
                    Dim idLength = If(version = 2, 3, 4)
                    Dim idBytes(idLength - 1) As Byte
                    If stream.Read(idBytes, 0, idLength) <> idLength Then Return
                    Dim id = System.Text.Encoding.ASCII.GetString(idBytes)
                    If String.IsNullOrWhiteSpace(id.Trim(ChrW(0))) Then Return
                    Dim frameSize As Integer
                    If version = 2 Then
                        Dim sizeBytes(2) As Byte
                        If stream.Read(sizeBytes, 0, sizeBytes.Length) <> sizeBytes.Length Then Return
                        frameSize = (CInt(sizeBytes(0)) << 16) Or (CInt(sizeBytes(1)) << 8) Or sizeBytes(2)
                    Else
                        Dim sizeBytes(3) As Byte
                        If stream.Read(sizeBytes, 0, sizeBytes.Length) <> sizeBytes.Length Then Return
                        frameSize = If(version = 4, SyncSafeInt(sizeBytes, 0), BigEndianInt(sizeBytes))
                        stream.Position += 2 ' frame flags
                    End If
                    If frameSize <= 0 OrElse stream.Position + frameSize > tagEnd Then Return
                    If (version = 2 AndAlso id = "PIC") OrElse (version >= 3 AndAlso id = "APIC") Then
                        Dim payload(frameSize - 1) As Byte
                        If stream.Read(payload, 0, frameSize) = frameSize Then SetAudioCoverFromApic(payload, version)
                        Return
                    End If
                    stream.Position += frameSize
                End While
            End Using
        Catch ex As Exception
            Debug.WriteLine("Embedded MP3 cover skipped: " & ex.Message)
        End Try
    End Sub

    Private Sub SetAudioCoverFromApic(payload() As Byte, version As Integer)
        Dim imageStart As Integer = 0
        For i = 0 To payload.Length - 2
            If (payload(i) = &HFF AndAlso payload(i + 1) = &HD8) OrElse
               (i + 3 < payload.Length AndAlso payload(i) = &H89 AndAlso payload(i + 1) = &H50 AndAlso payload(i + 2) = &H4E AndAlso payload(i + 3) = &H47) Then
                imageStart = i : Exit For
            End If
        Next
        If imageStart <= 0 Then Return
        Using bytes As New MemoryStream(payload, imageStart, payload.Length - imageStart, False), source As New Bitmap(bytes)
            Dim copy As New Bitmap(source.Width, source.Height)
            Try
                Using canvas = Graphics.FromImage(copy)
                    canvas.DrawImageUnscaled(source, 0, 0)
                End Using
            Catch
                ' Ours until it becomes audio_Cover_Bitmap (057.S08-8).
                copy.Dispose()
                Throw
            End Try
            If audio_Cover_Bitmap IsNot Nothing Then audio_Cover_Bitmap.Dispose()
            audio_Cover_Bitmap = copy
        End Using
    End Sub

    Private Shared Function SyncSafeInt(bytes() As Byte, Optional offset As Integer = 0) As Integer
        Return (CInt(bytes(offset)) << 21) Or (CInt(bytes(offset + 1)) << 14) Or (CInt(bytes(offset + 2)) << 7) Or bytes(offset + 3)
    End Function

    Private Shared Function BigEndianInt(bytes() As Byte) As Integer
        Return (CInt(bytes(0)) << 24) Or (CInt(bytes(1)) << 16) Or (CInt(bytes(2)) << 8) Or bytes(3)
    End Function

    Private Sub audio_Visualiser_Timer_Tick(sender As Object, e As EventArgs) Handles audio_Visualiser_Timer.Tick
        ' Not while nothing plays - after the sleep timer or a stop there is nothing to
        ' animate, and the tick was the one thing still spending the UI thread (050 D-21).
        If Not IsAudioNowPlaying() OrElse Not is_Vlc_Playing OrElse audio_Cover_Bitmap IsNot Nothing OrElse modern_Preferences Is Nothing OrElse Not modern_Preferences.AudioVisualiser Then
            audio_Visualiser_Timer.Stop()
            Return
        End If
        If audio_Visualiser_Buffer Is Nothing OrElse audio_Visualiser_Session Is Nothing Then Return   ' the paint handler makes it

        ' Held: nobody can see it (rule 10), motion is not allowed (rule 9) or the power policy says
        ' stop (rule 11). No frame, no clock, no repaint - the timer only polls for the way back, and
        ' the next frame starts from a fresh timestamp so the gap is not replayed as elapsed time.
        Dim reduced As Boolean = ReduceMotionActive()
        If AudioVisualiserUnseen() OrElse reduced OrElse AudioVisualiserPowerFrozen() Then
            audio_Visualiser_Last_Ticks = -1
            If audio_Visualiser_Timer.Interval <> Visualiser_Hold_Poll_Ms Then audio_Visualiser_Timer.Interval = Visualiser_Hold_Poll_Ms
            ' Motion switched off while a session is running: settle on the still frame, once.
            If reduced AndAlso Not audio_Visualiser_Still AndAlso Not AudioVisualiserUnseen() Then
                RenderAudioVisualiserStillFrame()
                RepaintMedia()
            End If
            Return
        End If
        If audio_Visualiser_Timer.Interval <> Visualiser_Frame_Interval_Ms Then audio_Visualiser_Timer.Interval = Visualiser_Frame_Interval_Ms

        ' Rule 2: the clock is real time. The step is the elapsed time in reference frames of 1/60 s -
        ' not one step per tick, and not one per paint - so the lines cross the window in the same
        ' time on a 60 Hz and a 165 Hz monitor and whatever the system timer resolution is.
        Dim now As Long = Stopwatch.GetTimestamp()
        Dim frames As Double = If(audio_Visualiser_Last_Ticks < 0, 1.0R,
                                  WaveParticlesSession.FramesIn((now - audio_Visualiser_Last_Ticks) / CDbl(Stopwatch.Frequency)))
        audio_Visualiser_Last_Ticks = now
        AdvanceAudioVisualiser(frames)
        RepaintMedia()
    End Sub

    ''' <summary>Set by the end-of-track advance while it opens the next file, so the sleep
    ''' deadline knows the listener did not ask for it (audit 050 D-03).</summary>
    Private audio_Auto_Advancing As Boolean
    Private ReadOnly audio_Sleep_Deadline As New SleepDeadline()

    Friend Sub BeginAudioPlayback()
        ' Kept across an automatic advance (end of track, slideshow step), re-armed by a
        ' track the user chose: the timer now counts from when listening began, not from
        ' the start of the current track.
        audio_Sleep_Deadline.TrackStarted(Environment.TickCount64,
                                          If(modern_Preferences Is Nothing, 0, modern_Preferences.SleepTimerMinutes),
                                          automatic:=audio_Auto_Advancing OrElse Is_slide_show_mode)
        audio_Sleep_Stopped = False
        UpdateAudioSleepPoll()
    End Sub

    ''' <summary>The poll runs exactly while TransportRules.SleepCheckNeeded says so.</summary>
    Private Sub UpdateAudioSleepPoll()
        If TransportRules.SleepCheckNeeded(IsAudioNowPlaying(), is_Vlc_Playing, audio_Sleep_Stopped) Then
            If Not audio_Sleep_Poll_Timer.Enabled Then audio_Sleep_Poll_Timer.Start()
        Else
            audio_Sleep_Poll_Timer.Stop()
        End If
    End Sub

    Private Sub Audio_Sleep_Poll_Timer_Tick(sender As Object, e As EventArgs) Handles audio_Sleep_Poll_Timer.Tick
        CheckAudioSleepTimer()
        UpdateAudioSleepPoll()
    End Sub

    Friend Sub CheckAudioSleepTimer()
        If Not IsAudioNowPlaying() OrElse audio_Sleep_Stopped OrElse modern_Preferences Is Nothing Then Return
        If Not audio_Sleep_Deadline.IsDue(Environment.TickCount64, modern_Preferences.SleepTimerMinutes) Then Return
        audio_Sleep_Deadline.Disarm()
        audio_Sleep_Stopped = True : StopVlcPlayback()
        ' The slideshow goes with the listening (057.S08-5): its tick only waits while a
        ' track PLAYS, so left alone it opened the next track at once - and that track
        ' armed a FRESH deadline. Playback then stopped for a few seconds every N minutes
        ' and never ended. The timer has ended the listening; the slideshow ends with it.
        SlideShowStop()
        ' A warning, so that where the status line is not on screen (the full screen without panels) the toast says
        ' why the music stopped (121 UX-404).
        Notify(Localization.T("Таймер сна остановил воспроизведение"), NotifyLevel.Warning)
        UpdateAudioSleepPoll()
    End Sub

    ''' <summary>A non-audio file is being shown: the listening session is over, and a
    ''' deadline armed for it must never stop the NEXT one (057.S08-6). A track played
    ''' with a 30-minute timer, an hour of photos, then a slideshow step onto an audio
    ''' file used to keep the long-passed deadline and be cut off at its first tick.
    ''' The audio branch of the display path must NOT come through here - an automatic
    ''' advance has to keep its running deadline (050 D-03).</summary>
    Friend Sub EndAudioListening()
        audio_Sleep_Deadline.Disarm()
        audio_Sleep_Poll_Timer.Stop()
    End Sub
    Friend Sub ClearAudioSurface()
        audio_Visualiser_Timer.Stop()
        DisposeAudioPaintObjects()
        audio_Current_Metadata = Nothing
        If audio_Cover_Bitmap IsNot Nothing Then audio_Cover_Bitmap.Dispose() : audio_Cover_Bitmap = Nothing
        If audio_Visualiser_Buffer IsNot Nothing Then audio_Visualiser_Buffer.Dispose() : audio_Visualiser_Buffer = Nothing
        ' A new file is a fresh start (rule 3): the next paint rolls a new session.
        audio_Visualiser_Session = Nothing
        audio_Visualiser_Last_Ticks = -1
        audio_Visualiser_Still = False
        audio_Visualiser_Timer.Interval = Visualiser_Frame_Interval_Ms
        audio_Sleep_Stopped = False
        audio_Sleep_Poll_Timer.Stop()
    End Sub
End Class
#End If
