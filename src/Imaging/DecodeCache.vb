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
    Friend Const Cache_Format_Version As Integer = 1

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
            payload = payloadDecoder.DecodeToPayload(source)
        End Using
        If payload Is Nothing OrElse payload.Bytes Is Nothing OrElse payload.Bytes.Length = 0 Then Return Nothing

        ' Stored before the image is built, so a payload that GDI+ then refuses is still
        ' the one on disk - and the load path above will delete it the next time round
        ' rather than this one silently succeeding at nothing.
        If key.Length > 0 Then DecodeCacheStore.TryStore(DecodeCacheStore.CacheDir(), key, payload, budgetMb)

        Return Materialize(payload)
    End Function

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
