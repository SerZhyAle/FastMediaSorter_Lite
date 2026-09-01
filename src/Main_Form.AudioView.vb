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
    Private audio_Playback_Started_Utc As DateTime
    Private audio_Sleep_Stopped As Boolean
    Private audio_Visualiser_Buffer As Bitmap
    Private audio_Visualiser_Particles() As AudioVisualiserParticle
    Private audio_Visualiser_Last_Frame_Utc As DateTime
    Private audio_Visualiser_Time As Single
    Private audio_Visualiser_Startup_Frame_Count As Integer
    Private audio_Visualiser_Wave_Count As Integer
    Private audio_Visualiser_Step_Px As Single
    Private audio_Visualiser_Stroke_Width As Single
    Private audio_Visualiser_Base_Wave_Hue As Single
    Private audio_Visualiser_Wave_Hue_Step As Single
    Private audio_Visualiser_Wave_Amplitude As Single
    Private audio_Visualiser_Particle_Speed_Multiplier As Single
    Private audio_Visualiser_Particle_Hue_Base As Single
    Private audio_Visualiser_Direction_X As Single
    Private audio_Visualiser_Direction_Y As Single
    Private audio_Visualiser_Normal_X As Single
    Private audio_Visualiser_Normal_Y As Single
    Private audio_Visualiser_Light_Theme As Boolean
    Private WithEvents audio_Visualiser_Timer As New Timer() With {.Interval = 16}

    Private Structure AudioVisualiserParticle
        Public X As Single
        Public Y As Single
        Public Dx As Single
        Public Dy As Single
        Public Radius As Single
        Public Hue As Single
    End Structure

    ''' <summary>Parses tags off the UI thread. The generation check prevents a late
    ''' answer from being painted on a file the user has already left.</summary>
    Friend Sub RequestAudioMetadataAsync(filePath As String, generation As Integer)
        Task.Run(Sub()
                     Try
                         Dim watch As Stopwatch = Stopwatch.StartNew()
                         Using media As Media = CreateVlcMedia(filePath)
                             media.Parse(MediaParseOptions.ParseLocal)
                             If watch.Elapsed > TimeSpan.FromSeconds(2) Then Return
                             Dim track As MediaTrack
                             Dim hasTrack As Boolean = False
                             If media.Tracks IsNot Nothing Then
                                 For Each candidate As MediaTrack In media.Tracks
                                     If candidate.TrackType = TrackType.Audio Then
                                         track = candidate
                                         hasTrack = True
                                         Exit For
                                     End If
                                 Next
                             End If
                             Dim size As Long = -1
                             Try : size = New FileInfo(filePath).Length : Catch : End Try
                             Dim codec As String = ""
                             Dim bitrate As Long = 0
                             Dim sampleRate As Long = 0
                             If hasTrack Then
                                 codec = track.Codec.ToString()
                                 bitrate = CLng(track.Bitrate)
                                 sampleRate = CLng(track.Data.Audio.Rate)
                             End If
                             Dim tags = AudioMetadata.FromValues(filePath, MediaMeta(media, MetadataType.Title), MediaMeta(media, MetadataType.Artist), MediaMeta(media, MetadataType.Album), MediaMeta(media, MetadataType.ArtworkURL), codec, bitrate, sampleRate, media.Duration, size)
                             If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
                             Me.BeginInvoke(Sub()
                                                If generation <> media_Generation OrElse Not String.Equals(Current_File_Name, filePath, StringComparison.Ordinal) Then Return
                                                audio_Current_Metadata = tags
                                                LoadAudioCover(tags.CoverPath, filePath)
                                                RepaintMedia()
                                            End Sub)
                         End Using
                     Catch ex As Exception
                         Debug.WriteLine("Audio metadata parse skipped: " & ex.Message)
                     End Try
                 End Sub)
    End Sub

    Private Shared Function MediaMeta(media As Media, kind As MetadataType) As String
        Try : Return If(media.Meta(kind), "") : Catch : Return "" : End Try
    End Function

    Friend Sub PaintAudioSurface(g As Graphics, bounds As Rectangle)
        If Not IsAudioNowPlaying() OrElse audio_Current_Metadata Is Nothing Then Return
        If audio_Cover_Bitmap IsNot Nothing Then
            audio_Visualiser_Timer.Stop()
            DrawAudioCover(g, audio_Cover_Bitmap, bounds)
        ElseIf modern_Preferences IsNot Nothing AndAlso modern_Preferences.AudioVisualiser Then
            PaintAudioVisualiser(g, bounds)
            audio_Visualiser_Timer.Start()
        Else
            audio_Visualiser_Timer.Stop()
        End If
        Dim top = Math.Max(12, bounds.Height - 118)
        Using plate As New SolidBrush(Color.FromArgb(175, 0, 0, 0)), white As New SolidBrush(Color.White), muted As New SolidBrush(Color.Gainsboro), titleFont As New Font("Segoe UI", 15.0F, FontStyle.Bold), detailFont As New Font("Segoe UI", 10.0F), fmt As New StringFormat With {.Trimming = StringTrimming.EllipsisCharacter, .FormatFlags = StringFormatFlags.NoWrap}
            g.FillRectangle(plate, New Rectangle(12, top, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - top - 8)))
            Dim line = top + 8
            g.DrawString(audio_Current_Metadata.Title, titleFont, white, New Rectangle(22, line, bounds.Width - 44, 28), fmt) : line += 30
            If audio_Current_Metadata.Artist <> "" Then g.DrawString(audio_Current_Metadata.Artist, detailFont, white, New Rectangle(22, line, bounds.Width - 44, 20), fmt) : line += 20
            If audio_Current_Metadata.Album <> "" Then g.DrawString(audio_Current_Metadata.Album, detailFont, white, New Rectangle(22, line, bounds.Width - 44, 20), fmt) : line += 20
            g.DrawString(audio_Current_Metadata.FormatLine, detailFont, muted, New Rectangle(22, line, bounds.Width - 44, 20), fmt)
        End Using
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

    ''' <summary>Branded no-cover fallback: sine waves and independently drifting
    ''' particles with a persistent, translucent wash for a motion-blur trail. It
    ''' Mirrors Android's AudioWaveParticleView parameter-for-parameter: each playback
    ''' session gets a new direction, palette, wave field and particle population.</summary>
    Private Sub PaintAudioVisualiser(g As Graphics, area As Rectangle)
        If area.Width <= 1 OrElse area.Height <= 1 Then Return
        EnsureAudioVisualiserBuffer(area.Size)
        If audio_Visualiser_Buffer Is Nothing Then Return
        TickAudioVisualiser()
        g.DrawImageUnscaled(audio_Visualiser_Buffer, area.Location)
    End Sub

    Private Sub EnsureAudioVisualiserBuffer(size As Size)
        If audio_Visualiser_Buffer IsNot Nothing AndAlso audio_Visualiser_Buffer.Size = size Then Return
        If audio_Visualiser_Buffer IsNot Nothing Then audio_Visualiser_Buffer.Dispose()
        audio_Visualiser_Buffer = New Bitmap(size.Width, size.Height)
        Dim random As New Random()
        audio_Visualiser_Light_Theme = BackColor.GetBrightness() > 0.5F
        audio_Visualiser_Wave_Count = random.Next(5, 13)
        audio_Visualiser_Step_Px = CSng(20.0R * (0.8R + random.NextDouble() * 0.4R))
        audio_Visualiser_Stroke_Width = CSng(3.0R + random.NextDouble() * 3.0R)
        audio_Visualiser_Base_Wave_Hue = CSng(random.NextDouble() * 360.0R)
        audio_Visualiser_Wave_Hue_Step = CSng(8.0R + random.NextDouble() * 12.0R)
        audio_Visualiser_Wave_Amplitude = CSng(0.28R + random.NextDouble() * 0.20R)
        audio_Visualiser_Particle_Speed_Multiplier = CSng(0.5R + random.NextDouble())
        audio_Visualiser_Particle_Hue_Base = CSng(random.NextDouble() * 360.0R)
        Dim direction = random.NextDouble() * Math.PI * 2.0R
        audio_Visualiser_Direction_X = CSng(Math.Cos(direction)) : audio_Visualiser_Direction_Y = CSng(Math.Sin(direction))
        audio_Visualiser_Normal_X = -audio_Visualiser_Direction_Y : audio_Visualiser_Normal_Y = audio_Visualiser_Direction_X
        ReDim audio_Visualiser_Particles(random.Next(15, 56) - 1)
        For i = 0 To audio_Visualiser_Particles.Length - 1
            Dim directionalSpeed = CSng((0.12R + random.NextDouble() * 0.42R) * audio_Visualiser_Particle_Speed_Multiplier)
            Dim driftSign = If(random.NextDouble() < 0.18R, -0.35F, 1.0F)
            audio_Visualiser_Particles(i) = New AudioVisualiserParticle With {
                .X = CSng(random.NextDouble() * size.Width), .Y = CSng(random.NextDouble() * size.Height),
                .Dx = audio_Visualiser_Direction_X * directionalSpeed * driftSign + CSng((random.NextDouble() - 0.5R) * 0.28R * audio_Visualiser_Particle_Speed_Multiplier),
                .Dy = audio_Visualiser_Direction_Y * directionalSpeed * driftSign + CSng((random.NextDouble() - 0.5R) * 0.28R * audio_Visualiser_Particle_Speed_Multiplier),
                .Radius = CSng(1.0R + random.NextDouble() * 5.0R), .Hue = CSng((audio_Visualiser_Particle_Hue_Base + (random.NextDouble() - 0.5R) * 108.0R + 360.0R) Mod 360.0R)}
        Next
        audio_Visualiser_Time = 0.0F : audio_Visualiser_Startup_Frame_Count = 0
        Using canvas = Graphics.FromImage(audio_Visualiser_Buffer)
            canvas.Clear(If(audio_Visualiser_Light_Theme, Color.White, Color.Black))
        End Using
    End Sub

    Private Sub TickAudioVisualiser()
        Dim w = audio_Visualiser_Buffer.Width : Dim h = audio_Visualiser_Buffer.Height
        audio_Visualiser_Time += 0.003F
        audio_Visualiser_Startup_Frame_Count = Math.Min(36, audio_Visualiser_Startup_Frame_Count + 1)
        Dim startupProgress = audio_Visualiser_Startup_Frame_Count / 36.0F
        Dim startupGain = 0.35F + 0.65F * startupProgress * (2.0F - startupProgress)
        Using canvas = Graphics.FromImage(audio_Visualiser_Buffer)
            canvas.SmoothingMode = SmoothingMode.AntiAlias
            Using wash As New SolidBrush(Color.FromArgb(38, If(audio_Visualiser_Light_Theme, 245, 10), If(audio_Visualiser_Light_Theme, 245, 10), If(audio_Visualiser_Light_Theme, 245, 10)))
                canvas.FillRectangle(wash, 0, 0, w, h)
            End Using
            Dim travelSpan = CSng(Math.Sqrt(w * w + h * h)) + audio_Visualiser_Step_Px * 6.0F
            Dim centerDrift = CSng(Math.Sin(audio_Visualiser_Time * 0.45F)) * Math.Min(w, h) * 0.02F
            Dim centerX = w * 0.5F + audio_Visualiser_Direction_X * centerDrift
            Dim centerY = h * 0.5F + audio_Visualiser_Direction_Y * centerDrift
            Dim laneSpacing = Math.Min(w, h) * 0.038F
            Dim waveAlpha = 0.28F + 0.16F * startupGain
            For wave = 0 To audio_Visualiser_Wave_Count - 1
                Dim envelope = 0.40F + 0.60F * CSng(Math.Abs(Math.Sin(audio_Visualiser_Time * 0.4F + wave * 0.2F)))
                Dim amplitude = h * audio_Visualiser_Wave_Amplitude * startupGain * envelope
                Dim bandOffset = (wave - (audio_Visualiser_Wave_Count - 1) * 0.5F) * laneSpacing
                Using path As New GraphicsPath(), pen As New Pen(HslColor((audio_Visualiser_Base_Wave_Hue + wave * audio_Visualiser_Wave_Hue_Step) Mod 360.0F, 0.8F, If(audio_Visualiser_Light_Theme, 0.35F, 0.65F), waveAlpha), audio_Visualiser_Stroke_Width)
                    Dim distance = -travelSpan * 0.5F : Dim first = True : Dim previous As PointF = PointF.Empty
                    While distance <= travelSpan * 0.5F
                        Dim displacement = CSng(Math.Sin(distance * 0.0105F + audio_Visualiser_Time + wave * 0.8F)) * amplitude
                        Dim point As New PointF(centerX + audio_Visualiser_Direction_X * distance + audio_Visualiser_Normal_X * (bandOffset + displacement), centerY + audio_Visualiser_Direction_Y * distance + audio_Visualiser_Normal_Y * (bandOffset + displacement))
                        If first Then path.AddLine(point, point) : first = False Else path.AddLine(previous, point)
                        previous = point
                        distance += audio_Visualiser_Step_Px
                    End While
                    canvas.DrawPath(pen, path)
                End Using
            Next
            For i = 0 To audio_Visualiser_Particles.Length - 1
                Dim particle = audio_Visualiser_Particles(i)
                particle.X += particle.Dx : particle.Y += particle.Dy
                If particle.X < 0 OrElse particle.X > w Then particle.Dx = -particle.Dx
                If particle.Y < 0 OrElse particle.Y > h Then particle.Dy = -particle.Dy
                audio_Visualiser_Particles(i) = particle
                Using brush As New SolidBrush(HslColor(particle.Hue, 0.9F, If(audio_Visualiser_Light_Theme, 0.3F, 0.7F), 0.38F + 0.32F * startupGain))
                    canvas.FillEllipse(brush, particle.X - particle.Radius, particle.Y - particle.Radius, particle.Radius * 2, particle.Radius * 2)
                End Using
            Next
        End Using
    End Sub

    Private Shared Function HslColor(hue As Single, saturation As Single, lightness As Single, alpha As Single) As Color
        Dim c = (1.0F - Math.Abs(2.0F * lightness - 1.0F)) * saturation
        Dim x = c * (1.0F - Math.Abs((hue / 60.0F) Mod 2.0F - 1.0F))
        Dim m = lightness - c / 2.0F
        Dim r As Single = m, green As Single = m, blue As Single = m
        If hue < 60 Then
            r = c + m : green = x + m
        ElseIf hue < 120 Then
            r = x + m : green = c + m
        ElseIf hue < 180 Then
            green = c + m : blue = x + m
        ElseIf hue < 240 Then
            green = x + m : blue = c + m
        ElseIf hue < 300 Then
            r = x + m : blue = c + m
        Else
            r = c + m : blue = x + m
        End If
        Return Color.FromArgb(CInt(alpha * 255.0F), CInt(r * 255.0F), CInt(green * 255.0F), CInt(blue * 255.0F))
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
                Using canvas = Graphics.FromImage(copy)
                    canvas.InterpolationMode = If(Is_HighQuality_Scaling, InterpolationMode.HighQualityBicubic, InterpolationMode.Default)
                    canvas.DrawImage(source, 0, 0, copy.Width, copy.Height)
                End Using
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
            Using canvas = Graphics.FromImage(copy)
                canvas.DrawImageUnscaled(source, 0, 0)
            End Using
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
        If Not IsAudioNowPlaying() OrElse audio_Cover_Bitmap IsNot Nothing OrElse modern_Preferences Is Nothing OrElse Not modern_Preferences.AudioVisualiser Then
            audio_Visualiser_Timer.Stop()
            Return
        End If
        RepaintMedia()
    End Sub

    Friend Sub BeginAudioPlayback()
        audio_Playback_Started_Utc = DateTime.UtcNow
        audio_Sleep_Stopped = False
    End Sub
    Friend Sub CheckAudioSleepTimer()
        If Not IsAudioNowPlaying() OrElse audio_Sleep_Stopped OrElse modern_Preferences Is Nothing OrElse modern_Preferences.SleepTimerMinutes <= 0 Then Return
        If DateTime.UtcNow - audio_Playback_Started_Utc < TimeSpan.FromMinutes(modern_Preferences.SleepTimerMinutes) Then Return
        audio_Sleep_Stopped = True : StopVlcPlayback() : lbl_Status.Text = Localization.T("Таймер сна остановил воспроизведение")
    End Sub
    Friend Sub ClearAudioSurface()
        audio_Visualiser_Timer.Stop()
        audio_Current_Metadata = Nothing
        If audio_Cover_Bitmap IsNot Nothing Then audio_Cover_Bitmap.Dispose() : audio_Cover_Bitmap = Nothing
        If audio_Visualiser_Buffer IsNot Nothing Then audio_Visualiser_Buffer.Dispose() : audio_Visualiser_Buffer = Nothing
        audio_Visualiser_Particles = Nothing
        audio_Sleep_Stopped = False
    End Sub
End Class
#End If
