#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO

' The window's side of the decoded-picture cache (014_SPECIFICATION_VIEWER_CORE_S11_MODERN_ASYNC.md,
' U-01). Modern-only. src/MediaCache.vb explains the ownership rule - a decoded Image has ONE
' owner at a time, a picture box or the cache, and is MOVED between them - and this partial is
' where the picture boxes obey it.
'
' What a box has to remember is WHERE its picture came from (PictureOrigin: path, the file's write
' time and length when it was read, the decode setting). Without that a picture handed back to the
' cache could not be told from a different version of the same file, and a file rewritten on disk
' would be shown from memory. The origin lives beside the box (one per box), is set by the two
' places that put a decoded picture on one, and is cleared by every place that empties one - so a
' picture without an origin is simply never offered, which costs a decode and never a wrong picture.
'
' Prefetch direction. The worker used to decode "the file after this one" whichever way the user
' was going, so paging BACKWARDS got no prefetch at all and every step decoded in front of the
' user. The neighbour in the direction of travel is chosen here (NavigationRules.PrefetchIndex);
' stepping back the way you came is what the cache is for.
Partial Public Class Main_Form

    ''' <summary>Where the picture on a box was read from - enough to know whether it still pictures
    ''' that file, and whether it may be kept at all.</summary>
    Private NotInheritable Class PictureOrigin
        Public Path As String = ""
        Public WriteTimeUtc As DateTime
        Public Length As Long
        Public ExifAutoRotate As Boolean
        ''' <summary>Rotated in place on screen: the pixels are no longer the file's.</summary>
        Public Mutated As Boolean
    End Class

    Private ReadOnly media_Cache As New MediaCache()
    Private box1_Origin As PictureOrigin
    Private box2_Origin As PictureOrigin

    ''' <summary>The origin of a picture decoded from <paramref name="file_Path"/> as it is now;
    ''' Nothing when the file cannot be asked about (nothing to compare against later, so nothing
    ''' to keep). Reads the file's attributes: call it where a stat is already being paid (the
    ''' display path) or off the UI thread (the prefetch worker).</summary>
    Private Shared Function OriginOf(file_Path As String) As PictureOrigin
        Try
            Dim info As New FileInfo(file_Path)
            If Not info.Exists Then Return Nothing
            Return OriginOf(file_Path, info)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0922: no origin for " & file_Path & ": " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>The same, from attributes that were already read - no second stat.</summary>
    Private Shared Function OriginOf(file_Path As String, info As FileInfo) As PictureOrigin
        Return New PictureOrigin With {
            .Path = file_Path,
            .WriteTimeUtc = info.LastWriteTimeUtc,
            .Length = info.Length,
            .ExifAutoRotate = Is_Exif_AutoRotate
        }
    End Function

    ''' <summary>Records where the picture the display path has just placed came from. It is the
    ''' ACTIVE box's: PlaceDecodedImage leaves is_Second_PictureBox_Active naming the box it used.</summary>
    Private Sub SetActiveBoxOrigin(origin As PictureOrigin)
        If is_Second_PictureBox_Active Then
            box2_Origin = origin
        Else
            box1_Origin = origin
        End If
    End Sub

    ''' <summary>The cached picture of <paramref name="file_Path"/>, as a decode result the display
    ''' path can place; Nothing on a miss or a stale entry. The attributes are the ones the display
    ''' path has just read - the entry must match the file as it is now.</summary>
    Private Function TakeCachedPicture(file_Path As String, info As FileInfo) As Tuple(Of Image, IO.MemoryStream)
        Dim hit As MediaEntry = media_Cache.TryTake(file_Path, info.LastWriteTimeUtc, info.Length, Is_Exif_AutoRotate)
        If hit Is Nothing Then Return Nothing
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0921: picture taken from the cache: " & file_Path)
        Return Tuple.Create(hit.Picture, hit.Data)
    End Function

    ''' <summary>
    ''' Takes box N's picture and stream OUT of the box: offered to the cache when it qualifies,
    ''' disposed otherwise. The box is left empty and without an origin - the caller is about to
    ''' put the next picture on it. This is the ONE place a box's picture is replaced during
    ''' navigation, and so the one place it can be saved from the dispose.
    ''' </summary>
    Private Sub RetireBoxPicture(box_Index As Integer)
        Dim box As PictureBox = If(box_Index = 2, Picture_Box_2, Picture_Box_1)
        Dim origin As PictureOrigin = If(box_Index = 2, box2_Origin, box1_Origin)
        Dim stream As IO.MemoryStream = If(box_Index = 2, pictureBox2_Stream, pictureBox1_Stream)
        Dim picture As Image = box.Image

        If box_Index = 2 Then
            box2_Origin = Nothing
            pictureBox2_Stream = Nothing
        Else
            box1_Origin = Nothing
            pictureBox1_Stream = Nothing
        End If

        If picture Is Nothing Then
            stream?.Dispose()
            Return
        End If

        ' Off the box first: the cache may dispose it at once (over budget), and a disposed
        ' bitmap left on a control throws on its next repaint.
        box.Image = Nothing

        If origin IsNot Nothing AndAlso
           MediaCache.IsCacheable(True, MediaCache.IsAnimatedFormat(picture), origin.Mutated) Then
            media_Cache.Offer(New MediaEntry With {
                .Path = origin.Path,
                .WriteTimeUtc = origin.WriteTimeUtc,
                .Length = origin.Length,
                .ExifAutoRotate = origin.ExifAutoRotate,
                .Picture = picture,
                .Data = stream
            })
        Else
            picture.Dispose()
            stream?.Dispose()
        End If
    End Sub

    ''' <summary>The picture on the box was changed in place (rotated): it no longer pictures the
    ''' file, so it must never be handed back later as if it did.</summary>
    Private Sub MarkBoxPictureMutated(box_Index As Integer)
        Dim origin As PictureOrigin = If(box_Index = 2, box2_Origin, box1_Origin)
        If origin IsNot Nothing Then origin.Mutated = True
    End Sub

    ''' <summary>The box was emptied by someone who disposes its picture themselves (a file
    ''' operation, an unreadable file): there is no picture left for the origin to describe.</summary>
    Private Sub ForgetBoxOrigin(box_Index As Integer)
        If box_Index = 2 Then
            box2_Origin = Nothing
        Else
            box1_Origin = Nothing
        End If
    End Sub

    ''' <summary>The file the prefetch should decode next: the neighbour in the direction the user
    ''' is going. "" for an empty folder.</summary>
    Private Function PrefetchNeighbourPath() As String
        If total_File_Count <= 0 Then Return ""
        Dim index As Integer = NavigationRules.PrefetchIndex(current_File_Index, total_File_Count, is_Nav_Backwards)
        If index < 0 Then Return ""
        Return If(is_Files_Array_Active, files_Array(index), files_List(index))
    End Function

End Class
#End If
