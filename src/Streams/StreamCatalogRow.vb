#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System

Namespace Streams

    ''' <summary>
    ''' Represents one row in streams.csv under the STREAM-BANK 2.1 schema.
    ''' </summary>
    Friend Class StreamCatalogRow

        Public Property Category As String = String.Empty
        Public Property Topic As String = String.Empty
        Public Property Name As String = String.Empty
        Public Property Url As String = String.Empty
        Public Property MediaKind As String = StreamCatalogSchema.MediaKindAudio
        Public Property Protocol As String = String.Empty
        Public Property Format As String = String.Empty
        Public Property Bitrate As String = String.Empty
        Public Property IsLive As String = String.Empty
        Public Property Https As String = String.Empty
        Public Property Language As String = String.Empty
        Public Property Country As String = String.Empty
        Public Property Homepage As String = String.Empty
        Public Property SourceKind As String = String.Empty
        Public Property LicenseNote As String = String.Empty
        Public Property Notes As String = String.Empty
        Public Property Confidence As String = String.Empty
        Public Property FaviconIndex As String = String.Empty
        Public Property Access As String = String.Empty

        ' Source metadata (for reporting and deduplication)
        Public Property SourceFile As String = String.Empty
        Public Property SourceLine As Integer = 0
        Public Property NormalizedIdentity As String = String.Empty
        Public Property DeclaredKind As String = String.Empty
        Public Property UnrecognizedDeclaredKind As String = String.Empty

        Public Function GetFieldValue(columnName As String) As String
            Select Case columnName
                Case "category" : Return Category
                Case "topic" : Return Topic
                Case "name" : Return Name
                Case "url" : Return Url
                Case "media_kind" : Return MediaKind
                Case "protocol" : Return Protocol
                Case "format" : Return Format
                Case "bitrate" : Return Bitrate
                Case "is_live" : Return IsLive
                Case "https" : Return Https
                Case "language" : Return Language
                Case "country" : Return Country
                Case "homepage" : Return Homepage
                Case "source_kind" : Return SourceKind
                Case "license_note" : Return LicenseNote
                Case "notes" : Return Notes
                Case "confidence" : Return Confidence
                Case "favicon_index" : Return FaviconIndex
                Case "access" : Return Access
                Case Else : Return String.Empty
            End Select
        End Function

        Public Sub SetFieldValue(columnName As String, value As String)
            Select Case columnName
                Case "category" : Category = value
                Case "topic" : Topic = value
                Case "name" : Name = value
                Case "url" : Url = value
                Case "media_kind" : MediaKind = value
                Case "protocol" : Protocol = value
                Case "format" : Format = value
                Case "bitrate" : Bitrate = value
                Case "is_live" : IsLive = value
                Case "https" : Https = value
                Case "language" : Language = value
                Case "country" : Country = value
                Case "homepage" : Homepage = value
                Case "source_kind" : SourceKind = value
                Case "license_note" : LicenseNote = value
                Case "notes" : Notes = value
                Case "confidence" : Confidence = value
                Case "favicon_index" : FaviconIndex = value
                Case "access" : Access = value
            End Select
        End Sub

    End Class

End Namespace
#End If
