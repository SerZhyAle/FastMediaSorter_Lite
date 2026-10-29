#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System

Public Enum DocumentContentRoute
    Other
    PdfPreview
    PdfReader
    EpubMetadata
    EpubReader
End Enum

Public Module DocumentContentPolicy
    ' Selection is pure. A disabled reader never probes or constructs a runtime.
    Public Function SelectRoute(extension As String, fullPdf As Boolean, fullEpub As Boolean, epubHostAvailable As Boolean) As DocumentContentRoute
        If String.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase) Then Return If(fullPdf, DocumentContentRoute.PdfReader, DocumentContentRoute.PdfPreview)
        If String.Equals(extension, ".epub", StringComparison.OrdinalIgnoreCase) Then Return If(fullEpub AndAlso epubHostAvailable, DocumentContentRoute.EpubReader, DocumentContentRoute.EpubMetadata)
        Return DocumentContentRoute.Other
    End Function
End Module
#End If
