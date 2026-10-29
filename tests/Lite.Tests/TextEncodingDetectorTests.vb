#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Linq
Imports System.Text
Imports Xunit

' Specification 277 section 13, VM-01..VM-10, and the ranking of each legacy candidate.
' Fixtures are generated here by encoding a string through its code page (plan step P1.1).
Public Class TextEncodingDetectorTests

    Private Const Russian As String = "Съешь же ещё этих мягких французских булок, да выпей чаю. Широкая электрификация южных губерний даст мощный толчок подъёму сельского хозяйства."
    Private Const Ukrainian As String = "Україна має дуже гарну природу, її ліси й поля відомі всім. Пісні та історія нашого народу живуть у серцях."
    Private Const Western As String = "Le cœur déçu mais l'âme plutôt naïve, Louÿs rêva de crapaüter en canoë au delà des îles. Zwölf Boxkämpfer jagen Viktor quer über den großen Sylter Deich."
    Private Const Japanese As String = "日本語の文章を正しく判定できるかどうかを確認するための例文です。東京は日本の首都であり、多くの人が住んでいます。"
    Private Const Simplified As String = "中文文本编码检测的示例句子。北京是中华人民共和国的首都，也是全国的政治和文化中心。"
    Private Const Traditional As String = "繁體中文編碼檢測的範例句子。臺北是臺灣的政治、經濟與文化中心，擁有許多歷史建築。"
    Private Const Korean As String = "한국어 인코딩 감지를 위한 예문입니다. 서울은 대한민국의 수도이며 많은 사람들이 살고 있습니다."

    Private Shared Function Encode(codePage As Integer, text As String) As Byte()
#If Not NETFRAMEWORK Then
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
#End If
        Return Encoding.GetEncoding(codePage).GetBytes(text)
    End Function

    Private Shared Function Detect(bytes As Byte(), Optional ansiCodePage As Integer = 1252) As EncodingDetectionResult
        Return TextEncodingDetector.Detect(bytes, bytes.Length, False, ansiCodePage)
    End Function

    <Fact>
    Public Sub Vm01_empty_input_is_ascii_utf8_without_bom()
        Dim result = Detect(Array.Empty(Of Byte)())
        Assert.False(result.IsBinary)
        Assert.Equal(EncodingEvidence.AsciiOnly, result.Evidence)
        Assert.Equal(65001, result.Encoding.CodePage)
        Assert.False(result.HasBom)
        Assert.Equal(LineEndingKind.None, result.DetectedLineEnding)
    End Sub

    <Fact>
    Public Sub Vm02_utf8_bom_is_evidence_and_its_length_is_reported()
        Dim bytes = New Byte() {&HEF, &HBB, &HBF}.Concat(Encode(65001, Russian)).ToArray()
        Dim result = Detect(bytes)
        Assert.Equal(EncodingEvidence.ByteOrderMark, result.Evidence)
        Assert.True(result.HasBom)
        Assert.Equal(3, result.BomLength)
        Assert.Equal(65001, result.Encoding.CodePage)
        Assert.Equal(1.0F, result.Confidence, 3)
    End Sub

    <Fact>
    Public Sub Vm03_utf8_without_bom_is_strict_utf8_and_writes_no_preamble()
        Dim result = Detect(Encode(65001, Russian))
        Assert.Equal(EncodingEvidence.StrictUtf8, result.Evidence)
        Assert.Equal(65001, result.Encoding.CodePage)
        Assert.Empty(result.Encoding.GetPreamble())
        Assert.Equal(0.99F, result.Confidence, 3)

        Dim ascii = Detect(Encode(65001, "plain ascii text"))
        Assert.Equal(EncodingEvidence.AsciiOnly, ascii.Evidence)
        Assert.Equal(1.0F, ascii.Confidence, 3)
    End Sub

    <Fact>
    Public Sub Vm04_utf16_byte_order_marks_select_their_byte_order()
        Dim le = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Russian)).ToArray()
        Dim be = Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(Russian)).ToArray()
        Assert.Equal(1200, Detect(le).Encoding.CodePage)
        Assert.Equal(1201, Detect(be).Encoding.CodePage)
        Assert.Equal(2, Detect(le).BomLength)
    End Sub

    <Fact>
    Public Sub Vm05_utf32_marks_win_over_the_utf16_prefix()
        Dim le As New UTF32Encoding(False, True)
        Dim be As New UTF32Encoding(True, True)
        Dim leResult = Detect(le.GetPreamble().Concat(le.GetBytes("Text Ж 😀")).ToArray())
        Dim beResult = Detect(be.GetPreamble().Concat(be.GetBytes("Text Ж 😀")).ToArray())
        Assert.Equal(12000, leResult.Encoding.CodePage)
        Assert.Equal(4, leResult.BomLength)
        Assert.Equal(12001, beResult.Encoding.CodePage)
    End Sub

    <Fact>
    Public Sub Vm06_windows1251_is_found_by_ranking_when_ansi_is_another_page()
        Dim result = Detect(Encode(1251, Russian), ansiCodePage:=1252)
        Assert.Equal(1251, result.Encoding.CodePage)
        Assert.Equal(EncodingEvidence.HeuristicCandidate, result.Evidence)
        Assert.True(result.Confidence >= TextEncodingDetector.FallbackConfidence)
        Assert.Equal(1251, result.CandidateRankings(0).CodePage)
    End Sub

    <Fact>
    Public Sub Vm07_cp866_koi8r_and_koi8u_rank_first_for_their_own_bytes()
        Assert.Equal(866, Detect(Encode(866, Russian)).Encoding.CodePage)
        Assert.Equal(20866, Detect(Encode(20866, Russian)).Encoding.CodePage)
        Assert.Equal(21866, Detect(Encode(21866, Ukrainian)).Encoding.CodePage)
    End Sub

    <Fact>
    Public Sub Vm07_manual_reopen_reads_the_same_bytes_with_the_chosen_page()
        Dim bytes = Encode(1251, Russian)
        Dim result = TextEncodingDetector.Detect(bytes, bytes.Length, False, 1252, 20866)
        Assert.Equal(EncodingEvidence.UserOverride, result.Evidence)
        Assert.Equal(20866, result.Encoding.CodePage)
        Assert.Contains(TextEncodingDetector.ManualCandidates, Function(c) c.CodePage = 20866)
    End Sub

    <Fact>
    Public Sub Vm08_nul_bytes_and_dense_controls_are_binary()
        Dim withNul = New Byte() {&H61, &H62, 0, 1, 2, &H63}
        Dim nul = Detect(withNul)
        Assert.True(nul.IsBinary)
        Assert.Null(nul.Encoding)

        Dim controls = Enumerable.Range(1, 200).Select(Function(i) If(i Mod 10 = 0, CByte(1), CByte(65))).ToArray()
        Assert.True(Detect(controls).IsBinary)
    End Sub

    <Fact>
    Public Sub Vm09_a_sequence_cut_by_the_probe_boundary_is_held()
        Dim bytes = Enumerable.Repeat(CByte(65), 65535).Concat(New Byte() {&HE2, &H82, &HAC}).ToArray()
        Dim result = TextEncodingDetector.Detect(bytes, 65536, True, 1252)
        Assert.False(result.IsBinary)
        Assert.Equal(65001, result.Encoding.CodePage)
    End Sub

    <Fact>
    Public Sub Vm10_overlong_and_dangling_bytes_leave_utf8_without_failing()
        Dim overlong = Detect(New Byte() {&H41, &H42, &HC0, &HAF, &H43})
        Assert.False(overlong.IsBinary)
        Assert.NotEqual(65001, overlong.Encoding.CodePage)
        Dim dangling = Detect(New Byte() {&H41, &H80, &H42})
        Assert.NotEqual(65001, dangling.Encoding.CodePage)
    End Sub

    <Theory>
    <InlineData(1252, 1251)>
    <InlineData(932, 1252)>
    <InlineData(936, 1252)>
    <InlineData(950, 1252)>
    <InlineData(949, 1252)>
    Public Sub Each_legacy_page_ranks_first_for_its_own_text(codePage As Integer, ansiCodePage As Integer)
        Dim text As String
        Select Case codePage
            Case 1252 : text = Western
            Case 932 : text = Japanese
            Case 936 : text = Simplified
            Case 950 : text = Traditional
            Case Else : text = Korean
        End Select
        Dim result = Detect(Encode(codePage, text), ansiCodePage)
        Assert.Equal(codePage, result.Encoding.CodePage)
        Assert.Equal(EncodingEvidence.HeuristicCandidate, result.Evidence)
    End Sub

    <Fact>
    Public Sub Too_little_evidence_falls_back_to_the_named_ansi_page()
        Dim result = Detect(Encode(1251, "ANSI sample Текст"), ansiCodePage:=1251)
        Assert.Equal(1251, result.Encoding.CodePage)
        Assert.Equal(EncodingEvidence.SystemAnsiFallback, result.Evidence)
        Assert.True(result.Confidence < TextEncodingDetector.FallbackConfidence)
    End Sub

    <Fact>
    Public Sub Line_endings_are_reported_per_kind_and_mixed()
        Dim mixed = Detect(Encode(65001, "a" & vbCrLf & "b" & vbLf & "c"))
        Assert.Equal(LineEndingKind.Mixed, mixed.DetectedLineEnding)
        Assert.True(mixed.HasMixedLineEndings)
        Assert.Equal(LineEndingKind.Lf, Detect(Encode(65001, "a" & vbLf & "b" & vbLf)).DetectedLineEnding)
        Assert.Equal(LineEndingKind.Crlf, Detect(Encode(1251, Russian & vbCrLf & Russian & vbCrLf)).DetectedLineEnding)
        Assert.Equal(LineEndingKind.None, Detect(Encode(65001, "single line")).DetectedLineEnding)
    End Sub

End Class
#End If
