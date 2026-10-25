#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq

Friend Enum SidecarKind
    Subtitle
    Lyrics
    Artwork
    Metadata
End Enum

Friend NotInheritable Class SidecarFile
    Friend Sub New(path As String, kind As SidecarKind)
        Me.Path = path
        Me.Kind = kind
    End Sub
    Friend ReadOnly Property Path As String
    Friend ReadOnly Property Kind As SidecarKind
End Class

''' <summary>MEDIA-CLASSIFICATION rule 4. Associations never change the primary category or move files.</summary>
Friend Module SidecarAssociations
    Friend Function Match(primary As String, candidates As IEnumerable(Of String)) As List(Of SidecarFile)
        Dim result As New List(Of SidecarFile)()
        If String.IsNullOrWhiteSpace(primary) Then Return result
        Dim directory = Path.GetDirectoryName(Path.GetFullPath(primary))
        Dim basename = Path.GetFileNameWithoutExtension(primary)
        Dim primaryKind = MediaKindClassifier.KindOf(primary)
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each candidate In If(candidates, Enumerable.Empty(Of String)())
            If String.IsNullOrWhiteSpace(candidate) Then Continue For
            Dim full = Path.GetFullPath(candidate)
            If Not String.Equals(directory, Path.GetDirectoryName(full), StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(Path.GetFullPath(primary), full, StringComparison.OrdinalIgnoreCase) OrElse Not seen.Add(full) Then Continue For
            Dim stem = Path.GetFileNameWithoutExtension(full)
            Dim exact = String.Equals(stem, basename, StringComparison.OrdinalIgnoreCase)
            Dim kind As SidecarKind
            Select Case Path.GetExtension(full).ToLowerInvariant()
                Case ".srt", ".vtt", ".ass", ".sub"
                    Dim language = If(stem.StartsWith(basename & ".", StringComparison.OrdinalIgnoreCase), stem.Substring(basename.Length + 1), "")
                    If Not exact AndAlso (language.Length = 0 OrElse Not language.All(Function(c) Char.IsLetter(c) OrElse c = "-"c)) Then Continue For
                    kind = SidecarKind.Subtitle
                Case ".lrc", ".txt"
                    If Not exact Then Continue For
                    kind = SidecarKind.Lyrics
                Case ".jpg", ".png", ".webp"
                    ' Rule 4 names a cover or poster of audio and video; a picture is never the cover of another picture.
                    If primaryKind = MediaKind.Image Then Continue For
                    If Not exact AndAlso Not String.Equals(stem, "folder", StringComparison.OrdinalIgnoreCase) AndAlso
                       Not String.Equals(stem, "cover", StringComparison.OrdinalIgnoreCase) Then Continue For
                    kind = SidecarKind.Artwork
                Case ".nfo", ".json", ".xml"
                    If Not exact Then Continue For
                    kind = SidecarKind.Metadata
                Case Else
                    Continue For
            End Select
            result.Add(New SidecarFile(full, kind))
        Next
        Return result.OrderBy(Function(s) If(String.Equals(Path.GetFileNameWithoutExtension(s.Path), basename, StringComparison.OrdinalIgnoreCase), 0, 1)).
            ThenBy(Function(s) s.Path, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    ' Callers run discovery with their existing background media/file-detail work.
    Friend Function Discover(primary As String) As List(Of SidecarFile)
        Try
            If String.IsNullOrWhiteSpace(primary) OrElse Not File.Exists(primary) OrElse
               String.Equals(Path.GetExtension(primary), ".fd-sec", StringComparison.OrdinalIgnoreCase) Then Return New List(Of SidecarFile)()
            Return Match(primary, Directory.EnumerateFiles(Path.GetDirectoryName(Path.GetFullPath(primary))))
        Catch ex As IOException
            Return New List(Of SidecarFile)()
        Catch ex As UnauthorizedAccessException
            Return New List(Of SidecarFile)()
        End Try
    End Function
End Module
#End If
