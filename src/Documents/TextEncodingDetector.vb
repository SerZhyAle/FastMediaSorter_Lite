#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Text

' Specification 277 section 6.1: deterministic encoding detection over the first 64 KiB.
' Tiers: user override -> byte-order mark -> NUL/control binary check -> strict UTF-8 with the
' trailing sequence held -> statistical ranking of legacy code pages -> system ANSI fallback
' below FallbackConfidence. Pure: bytes in, result out, no UI and no file access.
Public Module TextEncodingDetector

    Public Const ProbeBytes As Integer = 65536
    Public Const FallbackConfidence As Single = 0.5F

    Private ReadOnly NoRankings As IReadOnlyList(Of EncodingCandidate) = Array.AsReadOnly(New EncodingCandidate() {})

    ' Non-ASCII bytes the ranking needs before its confidence can reach full strength.
    Private Const FullEvidenceBytes As Integer = 24
    ' The system ANSI code page wins a near tie: the file most likely came from this machine.
    Private Const AnsiTieMargin As Single = 0.05F

    Private Enum CandidateGroup
        Cyrillic
        Western
        EastAsian
    End Enum

    Private NotInheritable Class LegacyCodePage
        Public ReadOnly CodePage As Integer
        Public ReadOnly Name As String
        Public ReadOnly Group As CandidateGroup

        Public Sub New(codePage As Integer, name As String, group As CandidateGroup)
            Me.CodePage = codePage
            Me.Name = name
            Me.Group = group
        End Sub
    End Class

    ' Ranking order is also the tie order: an equal score keeps the earlier entry.
    Private ReadOnly LegacyCodePages As LegacyCodePage() = {
        New LegacyCodePage(1251, "Windows-1251", CandidateGroup.Cyrillic),
        New LegacyCodePage(866, "CP866 (DOS)", CandidateGroup.Cyrillic),
        New LegacyCodePage(20866, "KOI8-R", CandidateGroup.Cyrillic),
        New LegacyCodePage(21866, "KOI8-U", CandidateGroup.Cyrillic),
        New LegacyCodePage(1252, "Windows-1252", CandidateGroup.Western),
        New LegacyCodePage(28591, "ISO-8859-1", CandidateGroup.Western),
        New LegacyCodePage(932, "Shift_JIS", CandidateGroup.EastAsian),
        New LegacyCodePage(936, "GBK", CandidateGroup.EastAsian),
        New LegacyCodePage(950, "Big5", CandidateGroup.EastAsian),
        New LegacyCodePage(949, "EUC-KR", CandidateGroup.EastAsian)
    }

    ' Section 6.2: the choices the Re-open with Encoding dialog offers, in display order.
    Public ReadOnly ManualCandidates As IReadOnlyList(Of EncodingCandidate) = (New List(Of EncodingCandidate) From {
        New EncodingCandidate(65001, "UTF-8", 0.0F),
        New EncodingCandidate(1200, "UTF-16 LE", 0.0F),
        New EncodingCandidate(1201, "UTF-16 BE", 0.0F),
        New EncodingCandidate(12000, "UTF-32 LE", 0.0F),
        New EncodingCandidate(12001, "UTF-32 BE", 0.0F),
        New EncodingCandidate(1251, "Windows-1251", 0.0F),
        New EncodingCandidate(866, "CP866 (DOS)", 0.0F),
        New EncodingCandidate(20866, "KOI8-R", 0.0F),
        New EncodingCandidate(21866, "KOI8-U", 0.0F),
        New EncodingCandidate(1252, "Windows-1252", 0.0F),
        New EncodingCandidate(28591, "ISO-8859-1", 0.0F),
        New EncodingCandidate(932, "Shift_JIS", 0.0F),
        New EncodingCandidate(936, "GBK", 0.0F),
        New EncodingCandidate(950, "Big5", 0.0F),
        New EncodingCandidate(949, "EUC-KR", 0.0F)
    }).AsReadOnly()

    ' Russian letter frequencies (share of letters), plus the Ukrainian and Belarusian letters
    ' at nominal values so a text in those languages is not scored as noise.
    Private ReadOnly CyrillicFrequency As New Dictionary(Of Char, Double) From {
        {"о"c, 0.1097}, {"е"c, 0.0845}, {"а"c, 0.0801}, {"и"c, 0.0735}, {"н"c, 0.067},
        {"т"c, 0.0626}, {"с"c, 0.0547}, {"р"c, 0.0473}, {"в"c, 0.0454}, {"л"c, 0.044},
        {"к"c, 0.0349}, {"м"c, 0.0321}, {"д"c, 0.0298}, {"п"c, 0.0281}, {"у"c, 0.0262},
        {"я"c, 0.0201}, {"ы"c, 0.019}, {"ь"c, 0.0174}, {"г"c, 0.017}, {"з"c, 0.0165},
        {"б"c, 0.0159}, {"ч"c, 0.0144}, {"й"c, 0.0121}, {"х"c, 0.0097}, {"ж"c, 0.0094},
        {"ш"c, 0.0073}, {"ю"c, 0.0064}, {"ц"c, 0.0048}, {"щ"c, 0.0036}, {"э"c, 0.0032},
        {"ф"c, 0.0026}, {"ъ"c, 0.0004}, {"ё"c, 0.0004},
        {"і"c, 0.05}, {"ї"c, 0.006}, {"є"c, 0.004}, {"ґ"c, 0.0002}, {"ў"c, 0.01}
    }
    ' Mean frequency of a letter drawn from real Russian text, and of a uniformly random letter.
    Private Const NaturalMeanFrequency As Double = 0.0577
    Private Const RandomMeanFrequency As Double = 1.0 / 33.0

    Public Function Detect(bytes As Byte(), readLength As Integer, isTruncated As Boolean,
                           Optional ansiCodePage As Integer = 0,
                           Optional overrideCodePage As Integer = 0) As EncodingDetectionResult
        RegisterCodePages()
        Dim length As Integer = If(bytes Is Nothing, 0, Math.Max(0, Math.Min(readLength, bytes.Length)))
        Dim result As New EncodingDetectionResult With {
            .CandidateRankings = NoRankings,
            .DetectedLineEnding = LineEndingKind.None
        }

        If overrideCodePage <> 0 Then
            Return DetectWithOverride(result, bytes, length, isTruncated, overrideCodePage)
        End If

        If length = 0 Then
            result.Encoding = New UTF8Encoding(False, True)
            result.Evidence = EncodingEvidence.AsciiOnly
            result.Confidence = 1.0F
            Return result
        End If

        Dim probeLength As Integer = Math.Min(length, ProbeBytes)
        Dim probeIsWhole As Boolean = probeLength = length AndAlso Not isTruncated

        Dim bom As Encoding = Nothing
        Dim bomLength As Integer = MatchByteOrderMark(bytes, length, bom)
        If bom IsNot Nothing Then
            result.Encoding = bom
            result.Evidence = EncodingEvidence.ByteOrderMark
            result.Confidence = 1.0F
            result.HasBom = True
            result.BomLength = bomLength
            ApplyLineEnding(result, DecodeOrNothing(bom, bytes, bomLength, probeLength - bomLength, probeIsWhole))
            Return result
        End If

        For i As Integer = 0 To probeLength - 1
            If bytes(i) = 0 Then Return MarkBinary(result)
        Next

        Dim strictUtf8 As New UTF8Encoding(False, True)
        Dim utf8Text As String = DecodeOrNothing(strictUtf8, bytes, 0, probeLength, probeIsWhole)
        If utf8Text IsNot Nothing Then
            If HasBinaryControlChars(bytes, probeLength) Then Return MarkBinary(result)
            result.Encoding = strictUtf8
            If CountNonAscii(bytes, probeLength) = 0 Then
                result.Evidence = EncodingEvidence.AsciiOnly
                result.Confidence = 1.0F
            Else
                result.Evidence = EncodingEvidence.StrictUtf8
                result.Confidence = 0.99F
            End If
            ApplyLineEnding(result, utf8Text)
            Return result
        End If

        Return DetectLegacy(result, bytes, probeLength, probeIsWhole, ansiCodePage)
    End Function

    ' Section 6.1 tier 1: the user's choice only changes how the bytes are read, so undecodable
    ' bytes show as U+FFFD instead of refusing the file; saving stays strict (section 6.2).
    Private Function DetectWithOverride(result As EncodingDetectionResult, bytes As Byte(), length As Integer,
                                        isTruncated As Boolean, codePage As Integer) As EncodingDetectionResult
        Dim encoding As Encoding = CreateEncoding(codePage, False)
        result.Encoding = encoding
        result.Evidence = EncodingEvidence.UserOverride
        result.Confidence = 1.0F
        Dim preamble As Byte() = ByteOrderMarkOf(codePage, encoding)
        If preamble.Length > 0 AndAlso StartsWith(bytes, length, preamble) Then
            result.HasBom = True
            result.BomLength = preamble.Length
        End If
        Dim probeLength As Integer = Math.Min(length, ProbeBytes)
        Dim probeIsWhole As Boolean = probeLength = length AndAlso Not isTruncated
        ApplyLineEnding(result, DecodeOrNothing(encoding, bytes, result.BomLength,
                                                Math.Max(0, probeLength - result.BomLength), probeIsWhole))
        Return result
    End Function

    ''' <summary>The BOM of a code page; CreateEncoding builds UTF-8 without a preamble, so its BOM is named here.</summary>
    Public Function ByteOrderMarkOf(codePage As Integer, encoding As Encoding) As Byte()
        If codePage = 65001 Then Return New Byte() {&HEF, &HBB, &HBF}
        Return If(encoding Is Nothing, Array.Empty(Of Byte)(), encoding.GetPreamble())
    End Function

    Private Function DetectLegacy(result As EncodingDetectionResult, bytes As Byte(), probeLength As Integer,
                                  probeIsWhole As Boolean, ansiCodePage As Integer) As EncodingDetectionResult
        Dim scored As New List(Of KeyValuePair(Of EncodingCandidate, String))()
        For Each page As LegacyCodePage In LegacyCodePages
            Dim encoding As Encoding = CreateEncoding(page.CodePage, True)
            If encoding Is Nothing Then Continue For
            Dim text As String = DecodeOrNothing(encoding, bytes, 0, probeLength, probeIsWhole)
            If text Is Nothing Then Continue For
            Dim score As Single = ScoreCandidate(page, text, bytes, probeLength)
            scored.Add(New KeyValuePair(Of EncodingCandidate, String)(New EncodingCandidate(page.CodePage, page.Name, score), text))
        Next
        ' Stable sort: equal scores keep the LegacyCodePages order.
        Dim ordered As New List(Of KeyValuePair(Of EncodingCandidate, String))()
        For Each item In scored
            Dim index As Integer = ordered.Count
            While index > 0 AndAlso ordered(index - 1).Key.Confidence < item.Key.Confidence
                index -= 1
            End While
            ordered.Insert(index, item)
        Next
        Dim rankings As New List(Of EncodingCandidate)()
        For Each item In ordered
            rankings.Add(item.Key)
        Next
        result.CandidateRankings = rankings.AsReadOnly()

        Dim ansi As Encoding = CreateEncoding(ansiCodePage, True)
        Dim chosenIndex As Integer = -1
        If ordered.Count > 0 Then
            chosenIndex = 0
            If ansi IsNot Nothing Then
                For i As Integer = 1 To ordered.Count - 1
                    If ordered(i).Key.CodePage = ansi.CodePage AndAlso
                       ordered(i).Key.Confidence >= ordered(0).Key.Confidence - AnsiTieMargin Then
                        chosenIndex = i
                        Exit For
                    End If
                Next
            End If
        End If

        Dim confidence As Single = 0.0F
        If chosenIndex >= 0 Then
            confidence = RankingConfidence(ordered, chosenIndex, CountNonAscii(bytes, probeLength))
        End If

        If chosenIndex >= 0 AndAlso confidence >= FallbackConfidence Then
            result.Encoding = CreateEncoding(ordered(chosenIndex).Key.CodePage, True)
            result.Evidence = EncodingEvidence.HeuristicCandidate
            result.Confidence = confidence
            ApplyLineEnding(result, ordered(chosenIndex).Value)
        Else
            Dim ansiText As String = Nothing
            If ansi IsNot Nothing Then ansiText = DecodeOrNothing(ansi, bytes, 0, probeLength, probeIsWhole)
            If ansiText IsNot Nothing Then
                result.Encoding = ansi
                result.Evidence = EncodingEvidence.SystemAnsiFallback
                result.Confidence = confidence
                ApplyLineEnding(result, ansiText)
            ElseIf chosenIndex >= 0 Then
                ' The ANSI page cannot read these bytes; the best candidate that can is still better than refusing.
                result.Encoding = CreateEncoding(ordered(chosenIndex).Key.CodePage, True)
                result.Evidence = EncodingEvidence.HeuristicCandidate
                result.Confidence = confidence
                ApplyLineEnding(result, ordered(chosenIndex).Value)
            Else
                Return MarkBinary(result)
            End If
        End If

        If HasBinaryControlChars(bytes, probeLength) Then Return MarkBinary(result)
        Return result
    End Function

    ' Confidence = score x evidence size x separation from the best competitor, each competitor
    ' weighted by how much of the text it reads differently: KOI8-R and KOI8-U, or Windows-1252
    ' and ISO-8859-1, differ in a few characters and are barely competitors at all.
    Private Function RankingConfidence(ordered As List(Of KeyValuePair(Of EncodingCandidate, String)),
                                       chosenIndex As Integer, nonAsciiBytes As Integer) As Single
        Dim chosen = ordered(chosenIndex)
        Dim competitor As Double = 0.0
        For i As Integer = 0 To ordered.Count - 1
            If i = chosenIndex Then Continue For
            competitor = Math.Max(competitor, ordered(i).Key.Confidence * ReadingDifference(ordered(i).Value, chosen.Value))
        Next
        Dim score As Double = chosen.Key.Confidence
        If score <= 0.0 Then Return 0.0F
        Dim sizeFactor As Double = Math.Min(1.0, nonAsciiBytes / CDbl(FullEvidenceBytes))
        Dim marginFactor As Double = Math.Max(0.0, Math.Min(1.0, (score - competitor) / 0.3))
        Return CSng(score * sizeFactor * (0.5 + 0.5 * marginFactor))
    End Function

    ' 0 for an identical reading, 1 when half or more of the non-ASCII characters differ or the
    ' readings do not align character for character (a double-byte page against a single-byte one).
    Private Function ReadingDifference(a As String, b As String) As Double
        If a.Length <> b.Length Then Return 1.0
        Dim nonAscii As Integer = 0
        Dim differing As Integer = 0
        For i As Integer = 0 To a.Length - 1
            If AscW(a(i)) < &H80 AndAlso AscW(b(i)) < &H80 Then Continue For
            nonAscii += 1
            If a(i) <> b(i) Then differing += 1
        Next
        If nonAscii = 0 Then Return 0.0
        Return Math.Min(1.0, differing / (0.5 * nonAscii))
    End Function

    Private Function ScoreCandidate(page As LegacyCodePage, text As String, bytes As Byte(), length As Integer) As Single
        Select Case page.Group
            Case CandidateGroup.Cyrillic
                Return ScoreCyrillic(text)
            Case CandidateGroup.Western
                Return ScoreWestern(text)
            Case Else
                Return ScoreEastAsian(page.CodePage, text, bytes, length)
        End Select
    End Function

    ' Natural Cyrillic text: its non-ASCII characters are letters, those letters follow the
    ' Russian frequency profile, words do not mix scripts, and capitals do not sit mid-word.
    Private Function ScoreCyrillic(text As String) As Single
        Dim nonAscii As Integer = 0
        Dim letters As Integer = 0
        Dim mixed As Integer = 0
        Dim midCaps As Integer = 0
        Dim frequencySum As Double = 0.0
        For i As Integer = 0 To text.Length - 1
            Dim c As Char = text(i)
            If AscW(c) < &H80 Then Continue For
            nonAscii += 1
            If Not IsCyrillicLetter(c) Then Continue For
            letters += 1
            Dim frequency As Double
            If CyrillicFrequency.TryGetValue(Char.ToLowerInvariant(c), frequency) Then frequencySum += frequency
            Dim previous As Char = If(i > 0, text(i - 1), " "c)
            Dim following As Char = If(i < text.Length - 1, text(i + 1), " "c)
            If IsAsciiLetter(previous) OrElse IsAsciiLetter(following) Then mixed += 1
            If Char.IsUpper(c) AndAlso IsCyrillicLetter(previous) AndAlso Char.IsLower(previous) Then midCaps += 1
        Next
        If nonAscii = 0 OrElse letters = 0 Then Return 0.0F
        Dim letterRatio As Double = letters / CDbl(nonAscii)
        Dim cohesion As Double = 1.0 - mixed / CDbl(letters)
        Dim meanFrequency As Double = frequencySum / letters
        Dim frequencyScore As Double = Clamp01((meanFrequency - RandomMeanFrequency) / (NaturalMeanFrequency - RandomMeanFrequency))
        Dim caseScore As Double = Clamp01(1.0 - 5.0 * midCaps / CDbl(letters))
        Return CSng(letterRatio * cohesion * (0.65 * frequencyScore + 0.35 * caseScore))
    End Function

    ' Natural Western text: accented letters sit inside otherwise ASCII words, and the rest of
    ' the non-ASCII characters are typographic punctuation. C1 controls (ISO-8859-1 reading
    ' Windows-1252 bytes) count against it.
    Private Function ScoreWestern(text As String) As Single
        Dim nonAscii As Integer = 0
        Dim latin As Integer = 0
        Dim punctuation As Integer = 0
        Dim embedded As Integer = 0
        For i As Integer = 0 To text.Length - 1
            Dim c As Char = text(i)
            If AscW(c) < &H80 Then Continue For
            nonAscii += 1
            If IsWesternLetter(c) Then
                latin += 1
                Dim previous As Char = If(i > 0, text(i - 1), " "c)
                Dim following As Char = If(i < text.Length - 1, text(i + 1), " "c)
                If IsAsciiLetter(previous) OrElse IsAsciiLetter(following) Then embedded += 1
            ElseIf IsWesternPunctuation(c) Then
                punctuation += 1
            End If
        Next
        If nonAscii = 0 Then Return 0.0F
        Dim plausible As Double = (latin + punctuation) / CDbl(nonAscii)
        Dim embeddedRatio As Double = If(latin = 0, 1.0, embedded / CDbl(latin))
        Return CSng(plausible * (0.25 + 0.75 * embeddedRatio))
    End Function

    ' Natural East Asian text: characters from the common blocks, byte pairs from the code
    ' page's core table rather than its extension rows, and no space between Han or kana
    ' characters (Korean separates words, so Hangul is held to the opposite).
    Private Function ScoreEastAsian(codePage As Integer, text As String, bytes As Byte(), length As Integer) As Single
        Dim nonAscii As Integer = 0
        Dim common As Integer = 0
        Dim hanKana As Integer = 0
        Dim hanKanaBySpace As Integer = 0
        Dim hangul As Integer = 0
        Dim hangulBySpace As Integer = 0
        For i As Integer = 0 To text.Length - 1
            Dim code As Integer = AscW(text(i))
            If code < &H80 Then Continue For
            nonAscii += 1
            Dim bySpace As Boolean = (i > 0 AndAlso text(i - 1) = " "c) OrElse (i < text.Length - 1 AndAlso text(i + 1) = " "c)
            If (code >= &H4E00 AndAlso code <= &H9FFF) OrElse (code >= &H3400 AndAlso code <= &H4DBF) OrElse
               (code >= &H3040 AndAlso code <= &H30FF) Then
                common += 1
                hanKana += 1
                If bySpace Then hanKanaBySpace += 1
            ElseIf code >= &HAC00 AndAlso code <= &HD7AF Then
                common += 1
                hangul += 1
                If bySpace Then hangulBySpace += 1
            ElseIf (code >= &H3000 AndAlso code <= &H303F) OrElse (code >= &HFF01 AndAlso code <= &HFF5E) Then
                common += 1
            End If
        Next
        If nonAscii = 0 OrElse common = 0 Then Return 0.0F
        Dim ratio As Double = common / CDbl(nonAscii)
        Dim spacing As Double = 1.0
        If hanKana > 0 Then spacing = 1.0 - hanKanaBySpace / CDbl(hanKana)
        If codePage = 949 Then
            Dim hangulSpacing As Double = If(hangul = 0, 0.0, hangulBySpace / CDbl(hangul))
            spacing *= Math.Min(1.0, 0.5 + 2.0 * hangulSpacing)
        End If
        Return CSng(ratio * spacing * CorePairRatio(codePage, bytes, length))
    End Function

    ' Share of the double-byte units that come from the code page's core table. Big5 text uses
    ' trail bytes 0x40..0x7E for about two pairs in five, GB2312 never does: without them a Big5
    ' reading is held to 0.6, which keeps GBK text with GBK.
    Private Function CorePairRatio(codePage As Integer, bytes As Byte(), length As Integer) As Double
        Dim units As Integer = 0
        Dim core As Integer = 0
        Dim lowTrails As Integer = 0
        Dim i As Integer = 0
        While i < length
            Dim lead As Integer = bytes(i)
            If lead < &H80 Then
                i += 1
                Continue While
            End If
            If codePage = 932 AndAlso lead >= &HA1 AndAlso lead <= &HDF Then
                units += 1
                i += 1
                Continue While
            End If
            If i + 1 >= length Then Exit While
            Dim trail As Integer = bytes(i + 1)
            units += 1
            Select Case codePage
                Case 932
                    If (lead >= &H81 AndAlso lead <= &H9F) OrElse (lead >= &HE0 AndAlso lead <= &HEA) Then core += 1
                Case 936
                    If lead >= &HA1 AndAlso lead <= &HF7 AndAlso trail >= &HA1 AndAlso trail <= &HFE Then core += 1
                Case 950
                    If ((lead >= &HA1 AndAlso lead <= &HC6) OrElse (lead >= &HC9 AndAlso lead <= &HF9)) AndAlso
                       ((trail >= &H40 AndAlso trail <= &H7E) OrElse (trail >= &HA1 AndAlso trail <= &HFE)) Then core += 1
                    If trail >= &H40 AndAlso trail <= &H7E Then lowTrails += 1
                Case 949
                    If lead >= &HA1 AndAlso lead <= &HFE AndAlso trail >= &HA1 AndAlso trail <= &HFE Then core += 1
            End Select
            i += 2
        End While
        If units = 0 Then Return 0.0
        Dim ratio As Double = core / CDbl(units)
        If codePage = 950 Then ratio *= Math.Min(1.0, 0.6 + 4.0 * lowTrails / CDbl(units))
        Return ratio
    End Function

    Public Function DetectLineEnding(text As String, ByRef hasMixed As Boolean) As LineEndingKind
        hasMixed = False
        If String.IsNullOrEmpty(text) Then Return LineEndingKind.None
        Dim crlf As Integer = 0
        Dim lf As Integer = 0
        Dim cr As Integer = 0
        Dim i As Integer = 0
        While i < text.Length
            Dim c As Char = text(i)
            If c = ControlChars.Cr Then
                If i + 1 < text.Length AndAlso text(i + 1) = ControlChars.Lf Then
                    crlf += 1
                    i += 1
                ElseIf i + 1 < text.Length Then
                    cr += 1
                End If
            ElseIf c = ControlChars.Lf Then
                lf += 1
            End If
            i += 1
        End While
        Dim kinds As Integer = If(crlf > 0, 1, 0) + If(lf > 0, 1, 0) + If(cr > 0, 1, 0)
        If kinds = 0 Then
            ' A lone CR at the very end may be the first half of a CRLF cut by the probe.
            If text(text.Length - 1) = ControlChars.Cr Then Return LineEndingKind.Cr
            Return LineEndingKind.None
        End If
        If kinds > 1 Then
            hasMixed = True
            Return LineEndingKind.Mixed
        End If
        If crlf > 0 Then Return LineEndingKind.Crlf
        If lf > 0 Then Return LineEndingKind.Lf
        Return LineEndingKind.Cr
    End Function

    ' A strict (exception-fallback) encoding for detection and saving, or a replacing reader for
    ' a user override. Code page 0 is the system ANSI page. Nothing when the page is unknown.
    Public Function CreateEncoding(codePage As Integer, strictDecoding As Boolean) As Encoding
        RegisterCodePages()
        Select Case codePage
            Case 65001
                Return New UTF8Encoding(False, strictDecoding)
            Case 1200
                Return New UnicodeEncoding(False, True, strictDecoding)
            Case 1201
                Return New UnicodeEncoding(True, True, strictDecoding)
            Case 12000
                Return New UTF32Encoding(False, True, strictDecoding)
            Case 12001
                Return New UTF32Encoding(True, True, strictDecoding)
        End Select
        Try
            Dim decoderFallback As DecoderFallback = If(strictDecoding, DecoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback)
            Return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, decoderFallback)
        Catch ex As ArgumentException
            Return Nothing
        Catch ex As NotSupportedException
            Return Nothing
        End Try
    End Function

    Private Function MatchByteOrderMark(bytes As Byte(), length As Integer, ByRef encoding As Encoding) As Integer
        encoding = Nothing
        ' UTF-32 LE before UTF-16 LE: FF FE 00 00 starts with the UTF-16 LE mark.
        If length >= 4 AndAlso bytes(0) = &HFF AndAlso bytes(1) = &HFE AndAlso bytes(2) = 0 AndAlso bytes(3) = 0 Then
            encoding = New UTF32Encoding(False, True, True)
            Return 4
        End If
        If length >= 4 AndAlso bytes(0) = 0 AndAlso bytes(1) = 0 AndAlso bytes(2) = &HFE AndAlso bytes(3) = &HFF Then
            encoding = New UTF32Encoding(True, True, True)
            Return 4
        End If
        If length >= 2 AndAlso bytes(0) = &HFF AndAlso bytes(1) = &HFE Then
            encoding = New UnicodeEncoding(False, True, True)
            Return 2
        End If
        If length >= 2 AndAlso bytes(0) = &HFE AndAlso bytes(1) = &HFF Then
            encoding = New UnicodeEncoding(True, True, True)
            Return 2
        End If
        If length >= 3 AndAlso bytes(0) = &HEF AndAlso bytes(1) = &HBB AndAlso bytes(2) = &HBF Then
            encoding = New UTF8Encoding(True, True)
            Return 3
        End If
        Return 0
    End Function

    ' Decodes with the encoding's own fallback; Nothing when a strict decoder meets an invalid
    ' sequence. With flush False a sequence cut by the end of the probe is held, not judged.
    Private Function DecodeOrNothing(encoding As Encoding, bytes As Byte(), offset As Integer, count As Integer,
                                     flush As Boolean) As String
        If count <= 0 Then Return String.Empty
        Try
            Dim decoder As Decoder = encoding.GetDecoder()
            Dim chars(encoding.GetMaxCharCount(count) - 1) As Char
            Dim produced As Integer = decoder.GetChars(bytes, offset, count, chars, 0, flush)
            Return New String(chars, 0, produced)
        Catch ex As DecoderFallbackException
            Return Nothing
        End Try
    End Function

    Private Sub ApplyLineEnding(result As EncodingDetectionResult, text As String)
        Dim hasMixed As Boolean
        result.DetectedLineEnding = DetectLineEnding(text, hasMixed)
        result.HasMixedLineEndings = hasMixed
    End Sub

    Private Function MarkBinary(result As EncodingDetectionResult) As EncodingDetectionResult
        result.IsBinary = True
        result.Encoding = Nothing
        result.Confidence = 0.0F
        Return result
    End Function

    Private Function StartsWith(bytes As Byte(), length As Integer, prefix As Byte()) As Boolean
        If length < prefix.Length Then Return False
        For i As Integer = 0 To prefix.Length - 1
            If bytes(i) <> prefix(i) Then Return False
        Next
        Return True
    End Function

    Private Function CountNonAscii(bytes As Byte(), length As Integer) As Integer
        Dim count As Integer = 0
        For i As Integer = 0 To length - 1
            If bytes(i) >= &H80 Then count += 1
        Next
        Return count
    End Function

    ' More than 2 control bytes other than TAB, LF, FF, CR and ESC, and above 0.5 % of the probe.
    Private Function HasBinaryControlChars(bytes As Byte(), count As Integer) As Boolean
        Dim controlCount As Integer = 0
        For i As Integer = 0 To count - 1
            Dim b As Byte = bytes(i)
            If (b < 9) OrElse (b = 11) OrElse (b > 13 AndAlso b < 27) OrElse (b > 27 AndAlso b < 32) Then
                controlCount += 1
            End If
        Next
        Return controlCount > 2 AndAlso (controlCount * 100.0 / count) > 0.5
    End Function

    Private Function IsCyrillicLetter(c As Char) As Boolean
        Dim code As Integer = AscW(c)
        If code >= &H410 AndAlso code <= &H44F Then Return True
        Select Case code
            Case &H401, &H451, &H406, &H456, &H407, &H457, &H404, &H454, &H490, &H491, &H40E, &H45E
                Return True
        End Select
        Return False
    End Function

    Private Function IsAsciiLetter(c As Char) As Boolean
        Return (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "a"c AndAlso c <= "z"c)
    End Function

    Private Function IsWesternLetter(c As Char) As Boolean
        Dim code As Integer = AscW(c)
        If code >= &HC0 AndAlso code <= &HFF Then Return code <> &HD7 AndAlso code <> &HF7
        Select Case code
            Case &H152, &H153, &H160, &H161, &H178, &H17D, &H17E, &H192
                Return True
        End Select
        Return False
    End Function

    Private Function IsWesternPunctuation(c As Char) As Boolean
        Select Case AscW(c)
            Case &HA0, &HA1, &HA7, &HA9, &HAB, &HAE, &HB0, &HB1, &HB7, &HBB, &HBF,
                 &H2013, &H2014, &H2018, &H2019, &H201A, &H201C, &H201D, &H201E, &H2022, &H2026, &H20AC, &H2122
                Return True
        End Select
        Return False
    End Function

    Private Function Clamp01(value As Double) As Double
        Return Math.Max(0.0, Math.Min(1.0, value))
    End Function

    Private Sub RegisterCodePages()
#If Not NETFRAMEWORK Then
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
#End If
    End Sub

End Module
#End If
