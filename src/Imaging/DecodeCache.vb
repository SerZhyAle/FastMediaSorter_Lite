#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO

''' <summary>
''' The decode cache seen from the outside: ONE call that answers "give me this
''' decoder-backed picture", from a file if we have already paid for it and from the
''' decoder if we have not
''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §3.1).
'''
''' It exists as its own module so FileManager - shared by both builds - gains exactly one
''' modern-only branch instead of a cache spread through it.
'''
''' Both paths end in the same two lines, on purpose:
'''     Dim stream As New MemoryStream(payload.Bytes)
'''     Image.FromStream(stream)
''' so a hit and a miss cannot produce different pixels. The stream is handed to the caller
''' and deliberately NOT disposed: GDI+ streams the later frames of an animation out of it
''' for the lifetime of the Image, which is the same contract LoadImageWithStream has
''' always had with the file's own bytes.
''' </summary>
Friend Module DecodeCache

    ''' <summary>
    ''' Rides in every key, and exists for one job: BUMP IT IN ANY CHANGE THAT ALTERS THE
    ''' CONTENT OF A PAYLOAD - the GIF frame-delay mapping, the PNG re-encode, an ImageSharp
    ''' major upgrade that decodes differently. Never for a change that only reads faster.
    '''
    ''' The same discipline as OcrPipeline.OcrPipelineVersion, and for the same reason: a
    ''' stale payload served by a new algorithm is the single way this feature can show
    ''' wrong pixels.
    ''' </summary>
    ''' History: 2 = animations above GifTranscodePolicy.Dither_Max_Total_Pixels are no
    ''' longer dithered (spec 241 rule 4), so their payload changed content.
    Friend Const Cache_Format_Version As Integer = 2

    ''' <summary>
    ''' The image for a decoder-backed file, with the MemoryStream GDI+ reads it from.
    ''' Nothing when it cannot be decoded at all - the caller treats that exactly as it
    ''' treats a Nothing from DecodeToImage today.
    ''' </summary>
    Friend Function LoadImage(filePath As String) As Tuple(Of Image, MemoryStream)
        Dim ignored As PathFailureKind = PathFailureKind.Content
        Return LoadImage(filePath, ignored)
    End Function

    ''' <param name="failure">Set only when the file's bytes could not be READ (audit 048,
    ''' A-06) - Locked, Denied, Transport, Missing. A decode that fails leaves it as the
    ''' caller set it (Content).</param>
    Friend Function LoadImage(filePath As String, ByRef failure As PathFailureKind) As Tuple(Of Image, MemoryStream)
        Return LoadImage(filePath, failure, ImageLoadStage.Whole)
    End Function

    ''' <param name="stage">How much of a multi-frame file the caller wants (spec 241): <c>Whole</c> is
    ''' today's behaviour; <c>Staged</c> answers a long animation that is not in the cache with its
    ''' first frame, marked through <see cref="AnimationStaging"/>; <c>FirstFrame</c> does so for every
    ''' animation and never takes a cached transcode.</param>
    Friend Function LoadImage(filePath As String, ByRef failure As PathFailureKind, stage As ImageLoadStage) As Tuple(Of Image, MemoryStream)
        Dim payloadDecoder As IImageDecoderPayload = TryCast(ImageDecoderProvider.Current, IImageDecoderPayload)
        Dim budgetMb As Integer = DecodeCacheStore.BudgetMb

        ' No key when the cache is off or the file cannot be stat'ed - the decode below
        ' then runs exactly as it did before this feature existed.
        ' No key either for an archive entry's temporary copy (audit 052 G-21): its path
        ' carries the session's random token, so an entry filed under it can never be hit
        ' again and would only evict entries that can.
        Dim key As String = ""
        If payloadDecoder IsNot Nothing AndAlso DecodeCachePolicy.IsEnabled(budgetMb) AndAlso
           Not ArchivePaths.IsUnderArchiveCache(filePath) Then
            key = TryBuildKey(filePath)
        End If

        If key.Length > 0 Then
            Dim hit As DecodedPayload = DecodeCacheStore.TryLoad(DecodeCacheStore.CacheDir(), key)
            ' A thumbnail wants the first frame, and reading a transcoded animation out of the cache
            ' (up to 128 MB) to take its first frame is the cost this stage exists to avoid.
            If hit IsNot Nothing AndAlso stage = ImageLoadStage.FirstFrame AndAlso hit.IsAnimation Then hit = Nothing
            If hit IsNot Nothing Then
                Dim materialized As Tuple(Of Image, MemoryStream) = Materialize(hit)
                If materialized IsNot Nothing Then Return materialized
                ' GDI+ refused what we stored. Drop it and decode normally - never a crash,
                ' never somebody else's picture (invariant 1).
                DecodeCacheStore.Invalidate(DecodeCacheStore.CacheDir(), key, hit.Kind)
            End If
        End If

        Dim sourceBytes As Byte() = FileManager.ReadSourceBytes(filePath, failure)
        If sourceBytes Is Nothing Then Return Nothing

        ' A decoder with no payload interface (there is none today, but the seam allows one)
        ' keeps the historical path untouched, cache or no cache.
        If payloadDecoder Is Nothing Then
            Dim sourceStream As New MemoryStream(sourceBytes)
            Dim decoded As Image = ImageDecoderProvider.Current.DecodeToImage(sourceStream)
            If decoded Is Nothing Then
                sourceStream.Dispose()
                Return Nothing
            End If
            Return Tuple.Create(decoded, sourceStream)
        End If

        Dim payload As DecodedPayload
        Using source As New MemoryStream(sourceBytes)
            If stage = ImageLoadStage.Whole Then
                payload = payloadDecoder.DecodeToPayload(source, filePath)
            Else
                payload = payloadDecoder.DecodeFirstFramePayload(source, filePath)
                ' A small animation keeps today's one step (spec 241 rule 9); the first frame it cost
                ' was milliseconds. A thumbnail wants the first frame whatever the size.
                If stage = ImageLoadStage.Staged AndAlso payload IsNot Nothing AndAlso payload.IsPreview AndAlso
                   Not AnimationStaging.IsStaged(payload) Then
                    source.Position = 0
                    payload = payloadDecoder.DecodeToPayload(source, filePath)
                End If
            End If
        End Using
        If payload Is Nothing OrElse payload.Bytes Is Nothing OrElse payload.Bytes.Length = 0 Then Return Nothing

        ' The first frame of an animation that is still to be prepared: never stored (the policy
        ' refuses it too), never logged as a prepared animation.
        If payload.IsPreview Then
            Dim preview As Tuple(Of Image, MemoryStream) = Materialize(payload)
            If preview IsNot Nothing Then AnimationStaging.Mark(preview.Item1, New StagedInfo(payload))
            Return preview
        End If

        ' Stored before the image is built, so a payload that GDI+ then refuses is still
        ' the one on disk - and the load path above will delete it the next time round
        ' rather than this one silently succeeding at nothing.
        Dim stored As Boolean = False
        If key.Length > 0 Then stored = DecodeCacheStore.TryStore(DecodeCacheStore.CacheDir(), key, payload, budgetMb)
        LogAnimationPrepared(payload, stored, filePath)

        Return Materialize(payload)
    End Function

    ''' <summary>
    ''' One line per prepared animation (spec 241 rule 6), written through AppFileLogger
    ''' because Release strips Debug.WriteLine: a decode that is slow but finishes would
    ''' otherwise leave no trace, and only an abandoned one did. Never per frame.
    ''' </summary>
    Friend Function AnimationPreparedText(payload As DecodedPayload, cached As Boolean, filePath As String) As String
        Return "Animation prepared: " & payload.Width.ToString() & "x" & payload.Height.ToString() &
               " x " & payload.Frames.ToString() & " frames, decode " & Math.Max(0L, payload.DecodeMs - payload.EncodeMs).ToString() &
               " ms, gif " & payload.EncodeMs.ToString() & " ms, " &
               (payload.Bytes.LongLength \ (1024L * 1024L)).ToString() & " MB, dither " & If(payload.Dithered, "on", "off") &
               ", cached " & If(cached, "yes", "no") & ": " & filePath
    End Function

    Private Sub LogAnimationPrepared(payload As DecodedPayload, cached As Boolean, filePath As String)
        ' Only a transcode that measured itself: Frames = 0 is a Magick animation or a still.
        If payload Is Nothing OrElse Not payload.IsAnimation OrElse payload.Frames <= 0 Then Return
        AppFileLogger.WriteLine(AnimationPreparedText(payload, cached, filePath))
    End Sub

    ''' <summary>
    ''' The key, or "" when the file cannot be measured. A network hiccup here is not a
    ''' reason to fail the load - it is a reason to decode without a cache.
    ''' </summary>
    Private Function TryBuildKey(filePath As String) As String
        Try
            Dim info As New FileInfo(filePath)
            Return DecodeCacheKey.Build(filePath, info.LastWriteTimeUtc.Ticks, info.Length,
                                        Is_Exif_AutoRotate, Cache_Format_Version)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " decode-cache: no key for " & filePath & ": " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Payload bytes -> a GDI+ image over a live stream for animations, or a detached Bitmap
    ''' with stream disposed for static images (018_SPECIFICATION_VIEWER_CORE_S7_MODERN_PIPELINE.md U-13).
    ''' Nothing when GDI+ refuses them, which is what turns a corrupt entry into a plain miss.
    ''' </summary>
    Private Function Materialize(payload As DecodedPayload) As Tuple(Of Image, MemoryStream)
        Dim stream As New MemoryStream(payload.Bytes)
        Try
            If payload.IsAnimation Then
                Dim image As Image = Image.FromStream(stream)
                Return Tuple.Create(image, stream)
            Else
                Using stream
                    Using rawImage As Image = Image.FromStream(stream)
                        Dim bmp As New Bitmap(rawImage)
                        Return Tuple.Create(CType(bmp, Image), CType(Nothing, MemoryStream))
                    End Using
                End Using
            End If
        Catch ex As Exception
            stream.Dispose()
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " decode-cache: payload refused by GDI+: " & ex.Message)
            Return Nothing
        End Try
    End Function

End Module
#End If
