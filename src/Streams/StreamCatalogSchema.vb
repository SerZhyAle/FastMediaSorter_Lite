#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Collections.ObjectModel

Namespace Streams

    ''' <summary>
    ''' Frozen constants and schema definitions for the stream catalog format under
    ''' contract STREAM-BANK (specification 022). Read through 2.2 on 2026-10-07: 2.2's rule 13 (a
    ''' blank is_live cell means "not stated") needs nothing of a producer that never writes the
    ''' column - it stays blank - so the producer rules below are those of 2.1, unchanged.
    ''' </summary>
    Friend Module StreamCatalogSchema

        Public Const ContractId As String = "STREAM-BANK"
        Public Const ContractVersion As String = "2.2"
        Public Const ContractTag As String = "STREAM-BANK 2.2"

        Public Const ArchiveFileName As String = "stream-catalog.zip"
        Public Const CatalogCsvEntryName As String = "streams.csv"
        Public Const FaviconAtlasEntryName As String = "favicon-atlas.png"
        Public Const ReportFileName As String = "stream-catalog-report.txt"

        ''' <summary>Max archive file size ceiling: 128 MiB (134,217,728 bytes).</summary>
        Public Const MaxArchiveSizeBytes As Long = 134217728L

        ''' <summary>Max favicon atlas byte size: 30 MiB (31,457,280 bytes) - spec 022 §4.</summary>
        Public Const MaxAtlasSizeBytes As Long = 31457280L

        Public Const AtlasWidth As Integer = 512
        Public Const TileSize As Integer = 32
        Public Const AtlasColumns As Integer = 16

        ''' <summary>Churn threshold percentage: 20% lost URLs triggers churn guard.</summary>
        Public Const ChurnThresholdPercent As Double = 20.0

        ''' <summary>Minimum row count in previous catalog to activate churn guard.</summary>
        Public Const ChurnThresholdMinRows As Integer = 50

        ''' <summary>
        ''' The 19 canonical columns in exact producer order (STREAM-BANK 2.1 / 03 §1.1).
        ''' </summary>
        Public ReadOnly ProducerColumns As ReadOnlyCollection(Of String) = New ReadOnlyCollection(Of String)(New String() {
            "category",
            "topic",
            "name",
            "url",
            "media_kind",
            "protocol",
            "format",
            "bitrate",
            "is_live",
            "https",
            "language",
            "country",
            "homepage",
            "source_kind",
            "license_note",
            "notes",
            "confidence",
            "favicon_index",
            "access"
        })

        ''' <summary>
        ''' Launchable URL schemes admitted into the published catalog.
        ''' </summary>
        Public ReadOnly LaunchableSchemes As ReadOnlyCollection(Of String) = New ReadOnlyCollection(Of String)(New String() {
            "http",
            "https",
            "rtsp"
        })

        ''' <summary>
        ''' File extensions classified as VIDEO when not otherwise declared.
        ''' </summary>
        Public ReadOnly VideoExtensions As ReadOnlyCollection(Of String) = New ReadOnlyCollection(Of String)(New String() {
            ".m3u8",
            ".mpd",
            ".mp4",
            ".mkv",
            ".webm",
            ".ts",
            ".mov"
        })

        ''' <summary>
        ''' Playlist file extensions that must be refused as nested playlist references.
        ''' </summary>
        Public ReadOnly RefusedPlaylistExtensions As ReadOnlyCollection(Of String) = New ReadOnlyCollection(Of String)(New String() {
            ".m3u",
            ".pls"
        })

        Public Const MediaKindAudio As String = "AUDIO"
        Public Const MediaKindVideo As String = "VIDEO"
        Public Const MediaKindRtsp As String = "RTSP"

    End Module

End Namespace
#End If
