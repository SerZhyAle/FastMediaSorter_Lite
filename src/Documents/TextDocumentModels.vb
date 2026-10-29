#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Text

' Specification 277 section 10: Windows-internal text document models. No WinForms
' reference and no serialized cross-product contract. The snapshot, save and draft models
' of section 10 arrive with the editor and recovery phases that first read them.

Public Enum EncodingEvidence
    UserOverride
    ByteOrderMark
    StrictUtf8
    AsciiOnly
    HeuristicCandidate
    SystemAnsiFallback
End Enum

Public Enum LineEndingKind
    Crlf
    Lf
    Cr
    Mixed
    None
End Enum

Public Structure EncodingCandidate
    Public ReadOnly CodePage As Integer
    Public ReadOnly Name As String
    ' 0.0 to 1.0; always 0 in the manual list, which ranks nothing.
    Public ReadOnly Confidence As Single

    Public Sub New(codePage As Integer, name As String, confidence As Single)
        Me.CodePage = codePage
        Me.Name = name
        Me.Confidence = confidence
    End Sub
End Structure

Public NotInheritable Class EncodingDetectionResult
    ' Nothing only when IsBinary is True; Evidence and Confidence mean nothing for a binary result.
    Public Property Encoding As Encoding
    Public Property Evidence As EncodingEvidence
    Public Property Confidence As Single
    Public Property HasBom As Boolean
    Public Property BomLength As Integer
    Public Property DetectedLineEnding As LineEndingKind
    Public Property HasMixedLineEndings As Boolean
    Public Property IsBinary As Boolean
    ' Legacy candidates that decoded the probe, best first; empty unless the heuristic tier ran.
    Public Property CandidateRankings As IReadOnlyList(Of EncodingCandidate)
End Class
#End If
