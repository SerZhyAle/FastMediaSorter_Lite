#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq

''' <summary>MEDIA-CLASSIFICATION 0.10 rule 1: categories describe content, not renderer support.</summary>
Public Enum MediaKind
    Image = 0
    Video = 1
    Audio = 2
    Document = 3
    Container = 4
    Other = 5 ' Contract UNKNOWN
    Book = 6
    Stream = 7
End Enum

Public Module MediaKindClassifier
    Private ReadOnly Categories As Dictionary(Of String, MediaKind) = CreateCategories()
    Private ReadOnly TemporarySuffixes As String() = {".temp_copy", ".download"}

    Private Function CreateCategories() As Dictionary(Of String, MediaKind)
        Dim result As New Dictionary(Of String, MediaKind)(StringComparer.OrdinalIgnoreCase)
        AddCategory(result, MediaKind.Image, ".jpg .jpeg .png .webp .gif .bmp .avif .heic .heif .svg .tiff .ico .wmf .emf .tif .exif")
        AddCategory(result, MediaKind.Video, ".mp4 .mkv .mov .webm .3gp .flv .wmv .m4v .avi .mpg .mpeg .ts .m2ts .vob .ogv .divx .m2v .mts .3g2 .asf")
        AddCategory(result, MediaKind.Audio, ".mp3 .m4a .flac .aac .ogg .wma .opus .amr .awb .ac3 .ec3 .ac4 .adts .thd .mka .oga .caf .alac .mia .mid .midi .wav .wave .m4b")
        AddCategory(result, MediaKind.Book, ".epub .pdf .cbz .cbr .fb2 .mobi")
        AddCategory(result, MediaKind.Stream, ".m3u .m3u8 .pls .xspf .fmsbcast")
        AddCategory(result, MediaKind.Container, ".fd-sec .zip .rar .7z .tar .gz .bz2 .xz .cab .arj .lzh .iso .dmg .img .vhd .vdi .qcow2 .vmdk")
        AddCategory(result, MediaKind.Document, ".doc .docx .rtf .odt .xls .xlsx .ods .ppt .pptx .odp .txt .md .json .xml .csv .tsv .yaml .yml .toml .ini .conf .log .html .htm")
        Return result
    End Function

    Private Sub AddCategory(table As Dictionary(Of String, MediaKind), kind As MediaKind, extensions As String)
        For Each extension As String In extensions.Split(" "c)
            table.Add(extension, kind)
        Next
    End Sub

    ''' <summary>Classify an extension or filename. No I/O; .m4b defaults to AUDIO unless
    ''' the caller supplies chapter/container evidence (rule 2). Local aliases .tif/.exif
    ''' and .log/.html/.htm remain supported. This table never expands navigation.</summary>
    Friend Function KindOf(nameOrExtension As String, Optional hasBookCues As Boolean = False) As MediaKind
        Dim extension As String = ExtensionOf(nameOrExtension)
        If extension = ".m4b" AndAlso hasBookCues Then Return MediaKind.Book
        Dim kind As MediaKind
        If Categories.TryGetValue(extension, kind) Then Return kind
        Return MediaKind.Other
    End Function

    ''' <summary>Rule 3. Strip repeated temporary suffixes, trailing spaces and dots,
    ''' then take the final extension. Works for Windows paths and archive names without
    ''' Path parsing (an archive name may contain a colon). Never changes the actual path.</summary>
    Friend Function ExtensionOf(nameOrExtension As String) As String
        Dim name As String = NormalizeSuffix(nameOrExtension)
        Dim slash As Integer = name.LastIndexOfAny(New Char() {"/"c, "\"c})
        Dim dot As Integer = name.LastIndexOf("."c)
        If dot <= slash Then Return ""
        Return name.Substring(dot).ToLowerInvariant()
    End Function

    Friend Function NormalizeSuffix(name As String) As String
        name = If(name, "").TrimEnd(" "c, "."c)
        Do
            Dim removed As Boolean = False
            For Each suffix As String In TemporarySuffixes
                If name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) Then
                    name = name.Substring(0, name.Length - suffix.Length).TrimEnd(" "c, "."c)
                    removed = True
                    Exit For
                End If
            Next
            If Not removed Then Return name
        Loop
    End Function
End Module

' SortableContentKinds moved to SortableContentKinds.vb (ticket 222): MediaKind.vb is linked into the Share Manager,
' which has no ModernViewerPreferences, so the preference-dependent module cannot live beside the classifier.

''' <summary>Rule 5 excludes junk before browsing/classification, never from storage verbs.</summary>
Friend Module MediaBrowsing
    Friend Function IsJunk(path As String) As Boolean
        If String.IsNullOrEmpty(path) Then Return True
        Dim segments As String() = path.Split(New Char() {"/"c, "\"c})
        For Each segment As String In segments
            If segment.Length = 0 OrElse segment = "." OrElse segment = ".." Then Continue For
            If segment.StartsWith(".", StringComparison.Ordinal) OrElse
               String.Equals(segment, "__MACOSX", StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Dim leaf As String = MediaKindClassifier.NormalizeSuffix(segments(segments.Length - 1))
        Return leaf.Length = 0 OrElse
            String.Equals(leaf, "Thumbs.db", StringComparison.OrdinalIgnoreCase) OrElse
            String.Equals(leaf, "desktop.ini", StringComparison.OrdinalIgnoreCase) OrElse
            leaf.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) OrElse
            leaf.EndsWith(".swp", StringComparison.OrdinalIgnoreCase) OrElse
            leaf.IndexOf(".~", StringComparison.Ordinal) >= 0
    End Function

    Friend Function IsEligible(path As String, supportedExtensions As ISet(Of String)) As Boolean
        Return Not IsJunk(path) AndAlso supportedExtensions IsNot Nothing AndAlso
            supportedExtensions.Contains(MediaKindClassifier.ExtensionOf(path))
    End Function
End Module
#End If
