Option Strict On

Imports System.Text

''' <summary>
''' Where a line's text and a plate's text are made out of their parts - `OCR-PIPELINE` amendment
''' 1.8 A and B (2026-09-29), ticket 183.
'''
''' The defect is the recognizer's cut, not ours. Tesseract returns CJK lettering as single
''' characters or short runs, and a line's text is its words with a space between every two, so a
''' Japanese or Chinese balloon came out as <c>コモ ナ 市 長 の 私 が</c> and a Korean one as
''' <c>코 모 나 시 장 으로서,</c>. The spaces defeat a reader's search (<c>市長</c> does not match
''' <c>市 長</c>), give the translator a boundary the lettering does not have, and make the text longer
''' than the lettering it hides. Japanese and Chinese are written without spaces; Korean spaces its
''' words and never its syllables, and the recognizer cuts both - of the four spaces in
''' <c>코 모 나 시 장 으로서,</c> exactly one is real.
'''
''' Three places used to join with a bare space - the engine's own line text (<c>GetText(TextLine)</c>
''' in <c>TesseractOcrEngine.ReadPageLines</c>), the run <see cref="OcrLineSplitter.SplitLine"/> cuts
''' and <c>OcrBlockBuilder.MakeBlock</c> - and all three now come through here, so there is one rule
''' and one place a port of it can be read.
'''
''' What this changes is TEXT and nothing else. Every threshold of the pipeline was measured on the
''' spaced reading, and the contract's own regression run (1.8 E: 36 pages identical except for
''' whitespace) holds only because the verdicts stayed where they were - hence
''' <see cref="OcrLine.SpacedText"/>, which the line filter and the attempt scorer keep reading
''' (1.8 F's argument: a joined CJK line is one letter run where the spaced one was many short
''' ones, and the admission it feeds would otherwise change).
'''
''' Constant status (`OCR-OVERLAY` rule 13).
''' | Constant | Status | Source / reason |
''' | --- | --- | --- |
''' | <see cref="HangulJoinGapRatio"/> | inherited | `OCR-PIPELINE` section 5 and amendment 1.8 D: 0.33, `derived` THERE - the exact-lettering-line plateau 0.30-0.36 of the Korean corpus (12 Pepper&amp;Carrot pages, `RESEARCH_cjk-word-join_2026-09-29`), its geometric middle. Measured on the reference's material and engine, not on this product's. Ticket 033 owns deriving it here. |
''' </summary>
Public Module OcrTextJoin

    ''' <summary>
    ''' Two words that meet on Hangul join with no space only when the gap between their boxes is
    ''' under this many median word heights of their line. Korean puts a space between words and
    ''' never inside one, and the recognizer cuts at both; the gap is what tells them apart. The
    ''' reference's count of lettering lines it rebuilds exactly holds a plateau from 0.30 to 0.36 and
    ''' falls on both sides (43 at 0.28, 44 at 0.38), so 0.33 is the middle of what was measured.
    ''' INHERITED - see the constant status table on the module.
    ''' </summary>
    Public Const HangulJoinGapRatio As Double = 0.33

    ' ----------------------------------------------------------------------- the two joins --

    ''' <summary>
    ''' A line's text from its words (1.8 A). Consecutive words are joined with a space except where
    ''' they meet on CJK text - see <see cref="JoinsWithoutSpace"/>. Built once the line's words are
    ''' all known, because the Hangul gap is weighed against the MEDIAN word height of the line, and
    ''' rebuilt from a run's own words when the split cuts a line.
    ''' A list with no words gives "" - the caller keeps the engine's own text for a line the engine
    ''' returned no words for.
    ''' </summary>
    Public Function JoinLineWords(words As IList(Of OcrWord)) As String
        If words Is Nothing OrElse words.Count = 0 Then Return ""

        Dim median As Integer = OcrLineSplitter.MedianWordHeightOf(words)
        Dim sb As New StringBuilder()
        For i As Integer = 0 To words.Count - 1
            If i > 0 AndAlso Not JoinsWithoutSpace(words(i - 1), words(i), median) Then sb.Append(" "c)
            If words(i) IsNot Nothing Then sb.Append(words(i).Text)
        Next
        Return sb.ToString().Trim()
    End Function

    ''' <summary>
    ''' A plate's text from its lines, in reading order (1.8 B): a space between two lines except
    ''' where they meet on Han, kana or CJK punctuation - a Japanese or Chinese phrase wraps with no
    ''' space at the line break. A Hangul line break KEEPS its space: Korean breaks its lines between
    ''' words, and there is no gap left to measure across a line break. Nothing else changes - a
    ''' plate with no CJK in it is exactly <c>String.Join(" ", lines).Trim()</c>.
    ''' </summary>
    Public Function JoinPlateLines(lineTexts As IList(Of String)) As String
        If lineTexts Is Nothing OrElse lineTexts.Count = 0 Then Return ""

        Dim sb As New StringBuilder()
        For i As Integer = 0 To lineTexts.Count - 1
            Dim text As String = If(lineTexts(i), "")
            If i > 0 AndAlso Not LinesMeetWithoutSpace(If(lineTexts(i - 1), ""), text) Then sb.Append(" "c)
            sb.Append(text)
        Next
        Return sb.ToString().Trim()
    End Function

    ''' <summary>
    ''' Do two consecutive words of one line need no space between them? The last character of the
    ''' first and the first character of the second must each be a Han, kana or Hangul letter or
    ''' CJK punctuation (<see cref="IsCjkPunctuation"/> - it is Common script, so a rule on scripts
    ''' alone leaves <c>市长 ， 我</c> and <c>ケ ー キ</c> spaced). Then:
    ''' <list type="bullet">
    ''' <item>neither Hangul - join: Japanese and Chinese are written without spaces;</item>
    ''' <item>either Hangul - join only when the gap between the two boxes, measured BETWEEN them as the
    ''' line split measures it (<see cref="OcrLineSplitter.GapBetween"/>, so a right-to-left line reads
    ''' the same), is under <see cref="HangulJoinGapRatio"/> times the median word height. A line
    ''' whose words have no measurable height, or a word with no box, keeps the space: there is
    ''' nothing to measure against.</item>
    ''' </list>
    ''' A CJK character beside a Latin word, a digit or ASCII punctuation keeps its space.
    ''' </summary>
    ''' <param name="medianWordHeight">Median height of the LINE's words, see
    ''' <see cref="OcrLineSplitter.MedianWordHeightOf"/>; 0 = not measurable.</param>
    Public Function JoinsWithoutSpace(previous As OcrWord, nxt As OcrWord, medianWordHeight As Integer) As Boolean
        If previous Is Nothing OrElse nxt Is Nothing Then Return False

        Dim last As Integer = LastCodePoint(previous.Text)
        Dim first As Integer = FirstCodePoint(nxt.Text)
        If last < 0 OrElse first < 0 Then Return False
        If Not IsCjkLetterOrPunctuation(last) OrElse Not IsCjkLetterOrPunctuation(first) Then Return False

        If Not IsHangul(last) AndAlso Not IsHangul(first) Then Return True

        If medianWordHeight <= 0 Then Return False
        If previous.Box.Width <= 0 OrElse previous.Box.Height <= 0 Then Return False
        If nxt.Box.Width <= 0 OrElse nxt.Box.Height <= 0 Then Return False
        Return OcrLineSplitter.GapBetween(previous.Box, nxt.Box) < medianWordHeight * HangulJoinGapRatio
    End Function

    ''' <summary>
    ''' Do two consecutive LINES of one plate meet without a space (1.8 B)? Han, kana or CJK
    ''' punctuation on both sides of the break, and no Hangul on either.
    ''' </summary>
    Public Function LinesMeetWithoutSpace(previousLine As String, nextLine As String) As Boolean
        Dim last As Integer = LastCodePoint(previousLine)
        Dim first As Integer = FirstCodePoint(nextLine)
        If last < 0 OrElse first < 0 Then Return False
        If IsHangul(last) OrElse IsHangul(first) Then Return False
        Return IsCjkLetterOrPunctuation(last) AndAlso IsCjkLetterOrPunctuation(first)
    End Function

    ' ----------------------------------------------------------------------- the scripts ----
    ' Code-point ranges, not a script database: .NET has no script property to ask. The set is the
    ' blocks of the four scripts the contract names, which is the set `OcrTextFilter.CountCjk`
    ' (the section 2.6 CJK bypass) reads, plus the compatibility and supplementary planes of the
    ' same scripts - so a character that is joined here is never one the filter counts out.

    ''' <summary>Han: unified ideographs and their extensions, radicals, compatibility ideographs.</summary>
    Public Function IsHan(codePoint As Integer) As Boolean
        Return (codePoint >= &H2E80 AndAlso codePoint <= &H2FDF) OrElse    ' CJK + Kangxi radicals
               (codePoint >= &H3400 AndAlso codePoint <= &H4DBF) OrElse    ' Extension A
               (codePoint >= &H4E00 AndAlso codePoint <= &H9FFF) OrElse    ' Unified ideographs
               (codePoint >= &HF900 AndAlso codePoint <= &HFAFF) OrElse    ' Compatibility ideographs
               (codePoint >= &H20000 AndAlso codePoint <= &H323AF)         ' Extensions B to H
    End Function

    ''' <summary>Hiragana and Katakana (with the phonetic extensions and the kana supplements).</summary>
    Public Function IsKana(codePoint As Integer) As Boolean
        Return (codePoint >= &H3040 AndAlso codePoint <= &H30FF) OrElse    ' Hiragana + Katakana
               (codePoint >= &H31F0 AndAlso codePoint <= &H31FF) OrElse    ' Katakana phonetic ext.
               (codePoint >= &H1B000 AndAlso codePoint <= &H1B16F)         ' Kana supplements
    End Function

    ''' <summary>Hangul: Jamo, compatibility Jamo, Jamo extensions, syllables, halfwidth Hangul.</summary>
    Public Function IsHangul(codePoint As Integer) As Boolean
        Return (codePoint >= &H1100 AndAlso codePoint <= &H11FF) OrElse
               (codePoint >= &H3130 AndAlso codePoint <= &H318F) OrElse
               (codePoint >= &HA960 AndAlso codePoint <= &HA97F) OrElse
               (codePoint >= &HAC00 AndAlso codePoint <= &HD7FF) OrElse
               (codePoint >= &HFFA0 AndAlso codePoint <= &HFFDC)
    End Function

    ''' <summary>
    ''' CJK punctuation as the contract defines it: the CJK Symbols and Punctuation block
    ''' (U+3000-303F), the katakana middle dot and prolonged sound mark (U+30FB-30FC) and the
    ''' halfwidth and fullwidth forms (U+FF00-FFEF). Common script, which is why it is listed.
    ''' </summary>
    Public Function IsCjkPunctuation(codePoint As Integer) As Boolean
        Return (codePoint >= &H3000 AndAlso codePoint <= &H303F) OrElse
               (codePoint >= &H30FB AndAlso codePoint <= &H30FC) OrElse
               (codePoint >= &HFF00 AndAlso codePoint <= &HFFEF)
    End Function

    Private Function IsCjkLetterOrPunctuation(codePoint As Integer) As Boolean
        Return IsHan(codePoint) OrElse IsKana(codePoint) OrElse IsHangul(codePoint) OrElse IsCjkPunctuation(codePoint)
    End Function

    ' A surrogate pair is one character: Han beyond the BMP (Extension B and up) ends or starts a
    ' word as a pair, and half of it is no character at all.

    Private Function FirstCodePoint(text As String) As Integer
        If String.IsNullOrEmpty(text) Then Return -1
        If text.Length > 1 AndAlso Char.IsHighSurrogate(text(0)) AndAlso Char.IsLowSurrogate(text(1)) Then
            Return Char.ConvertToUtf32(text(0), text(1))
        End If
        Return AscW(text(0))
    End Function

    Private Function LastCodePoint(text As String) As Integer
        If String.IsNullOrEmpty(text) Then Return -1
        Dim n As Integer = text.Length
        If n > 1 AndAlso Char.IsLowSurrogate(text(n - 1)) AndAlso Char.IsHighSurrogate(text(n - 2)) Then
            Return Char.ConvertToUtf32(text(n - 2), text(n - 1))
        End If
        Return AscW(text(n - 1))
    End Function

End Module
