#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' Media content classifier (003_SPECIFICATION_SORTABLE_CONTENT_KINDS_DOTNET10.md PK-1).
''' A single source of truth for what kind of file we are dealing with.
''' </summary>
Public Enum MediaKind
    Image       ' Something the IImageDecoder seam or GDI+ can turn into pixels
    Video       ' Something LibVLC plays with a picture (video/WebM/MKV/3GP/MOV/AVI/WMV/etc.)
    Audio       ' Something LibVLC plays without a picture (MP3/WAV/M4A/WMA/OGG)
    Document    ' Text, PDF, EPUB - sortable now, previewable later (PD)
    Other       ' User-configured, no built-in renderer
End Enum

Public Module MediaKindClassifier
    ''' <summary>
    ''' Classify a file by its extension. The table is the single source of truth,
    ''' and existing extension sets are derived from it.
    ''' </summary>
    Friend Function KindOf(extension As String) As MediaKind
        If String.IsNullOrEmpty(extension) Then Return MediaKind.Other

        Dim ext = extension.ToLowerInvariant()

        ' Image formats: all decodable image types on modern build
        If IsImageExtension(ext) Then Return MediaKind.Image

        ' Audio: no video picture, handled by LibVLC
        If IsAudioExtension(ext) Then Return MediaKind.Audio

        ' Document: text and ebook formats (PD will render these later)
        If IsDocumentExtension(ext) Then Return MediaKind.Document

        ' Video: handled by LibVLC with a picture
        If IsVideoExtension(ext) Then Return MediaKind.Video

        ' Everything else
        Return MediaKind.Other
    End Function

    Private Function IsImageExtension(ext As String) As Boolean
        Return ext = ".jpg" OrElse ext = ".jpeg" OrElse ext = ".png" OrElse ext = ".gif" OrElse
               ext = ".bmp" OrElse ext = ".tiff" OrElse ext = ".ico" OrElse ext = ".wmf" OrElse
               ext = ".emf" OrElse ext = ".exif" OrElse ext = ".webp" OrElse ext = ".avif" OrElse
               ext = ".heic" OrElse ext = ".heif"
    End Function

    Private Function IsAudioExtension(ext As String) As Boolean
        Return ext = ".mp3" OrElse ext = ".wav" OrElse ext = ".m4a" OrElse ext = ".wma" OrElse ext = ".ogg"
    End Function

    Private Function IsDocumentExtension(ext As String) As Boolean
        Return ext = ".txt" OrElse ext = ".md" OrElse ext = ".log" OrElse ext = ".csv" OrElse
               ext = ".json" OrElse ext = ".xml" OrElse ext = ".html" OrElse ext = ".htm" OrElse
               ext = ".pdf" OrElse ext = ".epub" OrElse ext = ".fb2"
    End Function

    Private Function IsVideoExtension(ext As String) As Boolean
        Return ext = ".webm" OrElse ext = ".3g2" OrElse ext = ".mkv" OrElse ext = ".3gp" OrElse
               ext = ".mp4" OrElse ext = ".m4v" OrElse ext = ".mov" OrElse ext = ".avi" OrElse
               ext = ".wmv" OrElse ext = ".asf" OrElse ext = ".mpg" OrElse ext = ".mpeg" OrElse
               ext = ".flv"
    End Function
End Module
#End If
