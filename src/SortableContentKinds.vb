#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq

' Split out of MediaKind.vb (ticket 222) so the classifier file can be linked into the Share Manager.

Friend Module SortableContentKinds
    Friend Function BuildExtensions(images As IEnumerable(Of String), playback As IEnumerable(Of String),
                                    unsupportedImages As IEnumerable(Of String), preferences As ModernViewerPreferences) As HashSet(Of String)
        Dim result As New HashSet(Of String)(images, StringComparer.OrdinalIgnoreCase)
        result.UnionWith(unsupportedImages)
        For Each extension As String In playback
            Dim kind As MediaKind = KindOf(extension)
            If kind = MediaKind.Audio AndAlso Not preferences.IncludeAudioInNavigation Then Continue For
            If kind = MediaKind.Video AndAlso Not preferences.IncludeVideoInNavigation Then Continue For
            result.Add(extension)
        Next
        If preferences.IncludeDocumentInNavigation Then result.UnionWith(preferences.DocumentExtensionSet())
        If preferences.SortOtherFiles Then result.UnionWith(preferences.OtherExtensionSet())
        Return result
    End Function

    Friend Function CanDecodeImage(path As String, decoderExtensions As IEnumerable(Of String)) As Boolean
        Return KindOf(path) = MediaKind.Image AndAlso decoderExtensions.Contains(MediaKindClassifier.ExtensionOf(path), StringComparer.OrdinalIgnoreCase)
    End Function
End Module
#End If
