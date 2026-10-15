Option Strict On

Imports System.Drawing
Imports System.IO
Imports Xunit

' OCR-PIPELINE amendment 1.8 A and B (ticket 183): a line's words and a plate's lines join with no
' space where they meet on Han, kana or CJK punctuation, and where Hangul is involved only under a
' gap bound. The producer's shared cases are replayed in OcrPipelineVectorTests; this file holds
' what those cases do not say - the edges of the rule, the places this product builds text, and the
' one thing the join must NOT touch: what the pipeline decides.
'
' Both legs: the join is shared code that both shipped exes compile.
Public Class OcrTextJoinTests

    ' --- fixtures --------------------------------------------------------------

    Private Shared Function W(text As String, left As Integer, width As Integer, Optional height As Integer = 30) As OcrWord
        Return New OcrWord With {.Text = text, .Box = New Rectangle(left, 0, width, height), .Confidence = 0.9F}
    End Function

    Private Shared Function Words(ParamArray items As OcrWord()) As List(Of OcrWord)
        Return New List(Of OcrWord)(items)
    End Function

    ''' <summary>A line the way the engine path builds one: the text is the join, the words are real.</summary>
    Private Shared Function LineOf(words As List(Of OcrWord), top As Integer, Optional confidence As Single = 0.9F) As OcrLine
        Dim box As Rectangle = words(0).Box
        For Each w As OcrWord In words
            box = Rectangle.Union(box, w.Box)
        Next
        box.Y = top
        Return New OcrLine With {
            .Words = words, .Box = box, .Confidence = confidence,
            .Text = OcrTextJoin.JoinLineWords(words)
        }
    End Function

    Private Shared Function Cp(codePoint As Integer) As String
        Return Char.ConvertFromUtf32(codePoint)
    End Function

    ' --- 1.8 A: the words of a line --------------------------------------------

    <Fact>
    Public Sub LatinWords_AreJoinedWithOneSpace_HoweverTightTheGap()
        ' A Latin line is untouched. Overlapping boxes are the tightest gap there is.
        Dim line As List(Of OcrWord) = Words(W("Hello", 0, 80), W("world", 70, 80), W("again", 150, 80))
        Assert.Equal("Hello world again", OcrTextJoin.JoinLineWords(line))
    End Sub

    <Fact>
    Public Sub HanAndKana_JoinWithNoSpace_WhateverTheGap()
        ' Japanese and Chinese are written without spaces, so no gap is read at all - only Hangul
        ' has a gap to read. A 200 px hole between two characters is still one word.
        Dim line As List(Of OcrWord) = Words(W("市", 0, 30), W("長", 230, 30), W("の", 500, 30))
        Assert.Equal("市長の", OcrTextJoin.JoinLineWords(line))
    End Sub

    <Theory>
    <InlineData(&HFF0C)>   ' fullwidth comma
    <InlineData(&H3001)>   ' ideographic comma
    <InlineData(&H3002)>   ' ideographic full stop
    <InlineData(&H300C)>   ' left corner bracket
    <InlineData(&H30FB)>   ' katakana middle dot (Common script)
    <InlineData(&H30FC)>   ' prolonged sound mark (Common script)
    <InlineData(&HFF01)>   ' fullwidth exclamation mark
    <InlineData(&HFF21)>   ' fullwidth Latin capital A: the halfwidth and fullwidth forms block
    Public Sub CjkPunctuation_JoinsLikeTheCharactersAroundIt(codePoint As Integer)
        ' It is Common script, so a rule on scripts alone leaves "市长 ， 我" spaced.
        Dim mark As String = Cp(codePoint)
        Dim line As List(Of OcrWord) = Words(W("市", 0, 30), W(mark, 40, 16), W("我", 70, 30))
        Assert.Equal("市" & mark & "我", OcrTextJoin.JoinLineWords(line))
    End Sub

    <Theory>
    <InlineData("市", ",")>        ' ASCII punctuation after CJK: the space is kept
    <InlineData("市", "!")>
    <InlineData("市", ".")>
    <InlineData("市", "Tower")>    ' a Latin word
    <InlineData("Tower", "市")>
    <InlineData("市", "3")>        ' a digit
    <InlineData("3", "市")>
    Public Sub AsciiPunctuationLatinAndDigits_KeepTheirSpaceBesideCjk(first As String, second As String)
        Dim line As List(Of OcrWord) = Words(W(first, 0, 40), W(second, 42, 40))
        Assert.Equal(first & " " & second, OcrTextJoin.JoinLineWords(line))
    End Sub

    <Fact>
    Public Sub OnlyTheMeetingCharactersDecide_NotTheWholeWord()
        ' "Tower" ends in Latin, "市長" starts in Han: the pair meets on Latin + Han, so it is spaced.
        ' "東京" then "Tower": Han meets Latin, spaced. But "A市" then "長B" meets on Han + Han.
        Dim line As List(Of OcrWord) = Words(W("A市", 0, 40), W("長B", 42, 40), W("C", 90, 20))
        Assert.Equal("A市長B C", OcrTextJoin.JoinLineWords(line))
    End Sub

    <Fact>
    Public Sub Hangul_JoinsOnlyWhenTheGapIsStrictlyUnderThirtyThreeHundredthsOfTheMedian()
        ' 30 px median -> 9.9 px: a 9 px gap joins, a 10 px gap does not.
        Assert.Equal("시장", OcrTextJoin.JoinLineWords(Words(W("시", 0, 24), W("장", 33, 24))))
        Assert.Equal("시 장", OcrTextJoin.JoinLineWords(Words(W("시", 0, 24), W("장", 34, 24))))
    End Sub

    <Fact>
    Public Sub Hangul_GapIsMeasuredBetweenTheBoxes_SoARightToLeftLineReadsTheSame()
        ' The words arrive in reading order and their boxes run right to left: a left-to-right
        ' difference would be negative on every pair and join everything - silently.
        Dim joined As List(Of OcrWord) = Words(W("시", 33, 24), W("장", 0, 24))      ' gap 33 - 24 = 9
        Dim spaced As List(Of OcrWord) = Words(W("시", 34, 24), W("장", 0, 24))      ' gap 34 - 24 = 10
        Assert.Equal("시장", OcrTextJoin.JoinLineWords(joined))
        Assert.Equal("시 장", OcrTextJoin.JoinLineWords(spaced))
    End Sub

    <Fact>
    Public Sub Hangul_GapIsWeighedAgainstTheLinesMedian_NotAgainstTheTwoWords()
        ' The same two syllables, 12 px apart. Alone they stand on a 30 px median (9.9 px bound) and
        ' stay apart; on a line whose type is 60 px tall the bound is 19.8 px and they join. The
        ' median is the LINE's, which is why the text is built once every word is known.
        Dim pair As List(Of OcrWord) = Words(W("시", 0, 24, 30), W("장", 36, 24, 30))
        Assert.Equal("시 장", OcrTextJoin.JoinLineWords(pair))

        Dim onTallLine As List(Of OcrWord) = Words(
            W("시", 0, 24, 30), W("장", 36, 24, 30),
            W("AAA", 200, 60, 60), W("BBB", 300, 60, 60), W("CCC", 400, 60, 60))
        Assert.Equal(60, OcrLineSplitter.MedianWordHeightOf(onTallLine))
        Assert.Equal("시장 AAA BBB CCC", OcrTextJoin.JoinLineWords(onTallLine))
    End Sub

    <Fact>
    Public Sub Hangul_BesideHanja_IsJudgedOnTheGapLikeHangulBesideHangul()
        Dim near As List(Of OcrWord) = Words(W("大韓", 0, 60), W("민국", 62, 60))
        Dim far As List(Of OcrWord) = Words(W("大韓", 0, 60), W("민국", 80, 60))
        Assert.Equal("大韓민국", OcrTextJoin.JoinLineWords(near))
        Assert.Equal("大韓 민국", OcrTextJoin.JoinLineWords(far))
    End Sub

    <Fact>
    Public Sub Hangul_CjkPunctuationBesideIt_IsStillJudgedOnTheGap()
        ' Either meeting character being Hangul puts the pair on the gap rule - the punctuation
        ' does not buy an unconditional join next to a syllable.
        Dim mark As String = Cp(&HFF0C)
        Assert.Equal("시" & mark, OcrTextJoin.JoinLineWords(Words(W("시", 0, 24), W(mark, 26, 16))))
        Assert.Equal("시 " & mark, OcrTextJoin.JoinLineWords(Words(W("시", 0, 24), W(mark, 60, 16))))
    End Sub

    <Fact>
    Public Sub Hangul_WithNoBoxOrNoHeight_KeepsTheSpace()
        ' Nothing to measure against: the space stays, never a guess in the joining direction.
        Dim noBox As List(Of OcrWord) = Words(
            New OcrWord With {.Text = "시"},
            New OcrWord With {.Text = "장"})
        Assert.Equal("시 장", OcrTextJoin.JoinLineWords(noBox))

        Dim noHeight As List(Of OcrWord) = Words(W("시", 0, 24, 0), W("장", 25, 24, 0))
        Assert.Equal(0, OcrLineSplitter.MedianWordHeightOf(noHeight))
        Assert.Equal("시 장", OcrTextJoin.JoinLineWords(noHeight))

        ' One word of the pair has no box while the line has a median: still spaced.
        Dim oneBox As List(Of OcrWord) = Words(W("시", 0, 24), New OcrWord With {.Text = "장"}, W("나", 40, 24))
        Assert.Equal("시 장 나", OcrTextJoin.JoinLineWords(oneBox))
    End Sub

    <Fact>
    Public Sub HanBeyondTheBasicPlane_JoinsAsOneCharacter()
        ' Extension B and up are surrogate pairs. Half of one is no character: reading the last UTF-16
        ' unit would find a lone low surrogate and keep the space.
        Dim rare As String = Cp(&H20000)
        Dim ends As List(Of OcrWord) = Words(W("市" & rare, 0, 60), W("長", 62, 30))
        Dim starts As List(Of OcrWord) = Words(W("市", 0, 30), W(rare & "長", 32, 60))
        Assert.Equal("市" & rare & "長", OcrTextJoin.JoinLineWords(ends))
        Assert.Equal("市" & rare & "長", OcrTextJoin.JoinLineWords(starts))
    End Sub

    <Fact>
    Public Sub NoWords_OneWord_AndNull()
        Assert.Equal("", OcrTextJoin.JoinLineWords(Nothing))
        Assert.Equal("", OcrTextJoin.JoinLineWords(New List(Of OcrWord)))
        Assert.Equal("市", OcrTextJoin.JoinLineWords(Words(W("市", 0, 30))))
    End Sub

    <Fact>
    Public Sub LatinLines_AreExactlyTheOldConcatenation()
        ' The old formula, kept as the oracle: every line that has no CJK in it must come out
        ' byte for byte as it did - this stage may change CJK text and nothing else.
        Dim samples As String()() = {
            New String() {"WHO", "GOES", "THERE"},
            New String() {" lead", "trail ", "x"},
            New String() {"Привет", "мир", "!"},
            New String() {"a", "", "b"},
            New String() {"123", "4.5", "6%"}
        }
        For Each sample As String() In samples
            Dim line As New List(Of OcrWord)
            For i As Integer = 0 To sample.Length - 1
                line.Add(W(sample(i), i * 100, 80))
            Next
            Assert.Equal(String.Join(" ", sample).Trim(), OcrTextJoin.JoinLineWords(line))
        Next
    End Sub

    ' --- 1.8 B: the lines of a plate -------------------------------------------

    <Fact>
    Public Sub PlateWithNoCjk_IsExactlyTheOldJoin()
        Dim samples As String()() = {
            New String() {"WHO GOES THERE", "ONLY THE WIND"},
            New String() {"one", "", "three"},
            New String() {" lead", "trail "},
            New String() {"Привет", "мир"}
        }
        For Each sample As String() In samples
            Assert.Equal(String.Join(" ", sample).Trim(), OcrTextJoin.JoinPlateLines(sample))
        Next
        Assert.Equal("", OcrTextJoin.JoinPlateLines(Nothing))
        Assert.Equal("", OcrTextJoin.JoinPlateLines(New String() {}))
    End Sub

    <Fact>
    Public Sub PlateLines_MeetingOnHanKanaOrCjkPunctuation_JoinWithNoSpace()
        Assert.Equal("魔法薬コンテスト", OcrTextJoin.JoinPlateLines({"魔法薬", "コンテスト"}))   ' Han + kana
        Assert.Equal("の私", OcrTextJoin.JoinPlateLines({"の", "私"}))                            ' kana + Han
        Assert.Equal("市长，我", OcrTextJoin.JoinPlateLines({"市长，", "我"}))                    ' fullwidth comma
        Assert.Equal("ケーキ", OcrTextJoin.JoinPlateLines({"ケー", "キ"}))                        ' prolonged mark
        Assert.Equal("市長の私が魔法薬", OcrTextJoin.JoinPlateLines({"市長の", "私が", "魔法薬"}))
    End Sub

    <Fact>
    Public Sub PlateLines_WithAHangulBreak_KeepTheirSpace()
        ' Korean breaks its lines between words; there is no gap left to measure across a break.
        Assert.Equal("코모나 시장", OcrTextJoin.JoinPlateLines({"코모나", "시장"}))
        ' Hangul on either side of the break, whatever is on the other: hanja, or punctuation.
        Assert.Equal("大韓 민국", OcrTextJoin.JoinPlateLines({"大韓", "민국"}))
        Assert.Equal("민국 大韓", OcrTextJoin.JoinPlateLines({"민국", "大韓"}))
        Assert.Equal("민국 " & Cp(&H3002), OcrTextJoin.JoinPlateLines({"민국", Cp(&H3002)}))
    End Sub

    <Fact>
    Public Sub PlateLines_BesideLatinOrDigits_KeepTheirSpace()
        Assert.Equal("I SAID 市長", OcrTextJoin.JoinPlateLines({"I SAID", "市長"}))
        Assert.Equal("市長 SAID", OcrTextJoin.JoinPlateLines({"市長", "SAID"}))
        Assert.Equal("第 3", OcrTextJoin.JoinPlateLines({"第", "3"}))
        Assert.Equal("市長, 私", OcrTextJoin.JoinPlateLines({"市長,", "私"}))
    End Sub

    ' --- the places this product builds text -----------------------------------

    <Fact>
    Public Sub CutRun_RebuildsItsTextFromItsOwnWords_WithTheJoin()
        ' Two Japanese phrases stitched into one recognizer line, a wide gap between them. Each run
        ' is its own line with its own text, joined the same way a whole line is.
        Dim stitched As OcrLine = LineOf(Words(
            W("コモ", 0, 60), W("ナ", 62, 30), W("市", 94, 30),
            W("魔", 700, 30), W("法", 732, 30), W("薬", 764, 30)), top:=100)
        Assert.Equal("コモナ市魔法薬", stitched.Text)

        Dim runs As List(Of OcrLine) = OcrLineSplitter.SplitLine(stitched)
        Assert.Equal(2, runs.Count)
        Assert.Equal("コモナ市", runs(0).Text)
        Assert.Equal("魔法薬", runs(1).Text)
    End Sub

    <Fact>
    Public Sub CutRun_WeighsItsHangulGapAgainstItsOwnMedian()
        ' The Latin half of the stitched line is set twice as tall as the Hangul half. Rebuilt from
        ' its own words, the Hangul run is weighed against ITS 30 px median (9.9 px), not the
        ' 60 px of the line it was cut from (19.8 px): the 12 px gap stays a space.
        Dim stitched As OcrLine = LineOf(Words(
            W("시", 0, 24, 30), W("장", 36, 24, 30),
            W("BIG", 800, 100, 60), W("TYPE", 920, 100, 60), W("HERE", 1040, 100, 60)), top:=100)
        Assert.Equal("시장 BIG TYPE HERE", stitched.Text)    ' on the line's median the gap joins

        Dim runs As List(Of OcrLine) = OcrLineSplitter.SplitLine(stitched)
        Assert.Equal(2, runs.Count)
        Assert.Equal("시 장", runs(0).Text)
        Assert.Equal("BIG TYPE HERE", runs(1).Text)
    End Sub

    <Fact>
    Public Sub Block_FromJapaneseLines_HasASpacelessSourceText_AndIsStillTranslatable()
        ' The translator is sent the block's source text and the cache stores it, so this is the
        ' text a CJK plate lives by. Three lines of one balloon, stacked in one column.
        Dim lines As New List(Of OcrLine) From {
            LineOf(Words(W("コモ", 0, 60), W("ナ", 62, 30), W("市", 94, 30), W("長", 126, 30), W("の", 158, 30)), top:=100),
            LineOf(Words(W("魔", 0, 30), W("法", 32, 30), W("薬", 64, 30), W("コンテスト", 96, 150)), top:=140),
            LineOf(Words(W("開会を", 0, 90), W("宣言", 92, 60), W("します!", 154, 100)), top:=180)
        }

        Dim blocks As List(Of OcrBlock) = OcrBlockBuilder.BuildBlocks(lines, New Size(1000, 1000))

        Assert.Single(blocks)
        Assert.Equal("コモナ市長の魔法薬コンテスト開会を宣言します!", blocks(0).SourceText)
        Assert.Equal(3, blocks(0).Lines.Count)
    End Sub

    <Fact>
    Public Sub JoinedCjkText_StillPassesTheTranslatabilityFilter()
        ' The filter's CJK branch counts characters, never spaces - so removing the spaces cannot
        ' make a block it kept untranslatable, and cannot make a one-character mark pass.
        Assert.Equal("", OcrTextFilter.RejectionReason("コモナ市長の私が魔法薬", 2))
        Assert.Equal("", OcrTextFilter.RejectionReason("市長", 1))
        Assert.Equal("", OcrTextFilter.RejectionReason("코모나 시장으로서,", 1))
        Assert.Equal("cjk-too-short", OcrTextFilter.RejectionReason("光", 1))
    End Sub

    ' --- what the join must not move --------------------------------------------

    <Fact>
    Public Sub SpacedText_IsTheWordsOneSpaceApart_WhateverTheJoinDid()
        Dim line As OcrLine = LineOf(Words(W("コモ", 0, 60), W("ナ", 62, 30), W("市", 94, 30)), top:=0)
        Assert.Equal("コモナ市", line.Text)
        Assert.Equal("コモ ナ 市", line.SpacedText)
    End Sub

    <Fact>
    Public Sub SpacedText_OfALineWithNoWords_IsItsOwnText()
        Dim bare As New OcrLine With {.Text = "市長 の", .Box = New Rectangle(0, 0, 100, 30)}
        Assert.Equal("市長 の", bare.SpacedText)

        Dim stub As New OcrLine With {
            .Text = "WHO GOES THERE", .Box = New Rectangle(0, 0, 300, 30),
            .Words = New List(Of OcrWord) From {New OcrWord With {.Text = "WHO GOES THERE", .Box = New Rectangle(0, 0, 300, 30)}}
        }
        Assert.Equal("WHO GOES THERE", stub.SpacedText)
    End Sub

    <Fact>
    Public Sub LineFilter_ReadsTheSpacedLine_SoTheJoinMovesNoVerdict()
        ' Why decisions read SpacedText. A recognizer line cut into short runs, read at 0.60: on the
        ' spaced text its longest letter run is 2, so the line is the "short, low-confidence" kind
        ' the filter drops. On the joined text it is one run of eight and would be kept at ANY
        ' confidence - the same drift the reference had to fix in its rescue admission (1.8 F).
        Dim line As OcrLine = LineOf(Words(
            W("コモ", 0, 60), W("ナ", 62, 30), W("市", 94, 30), W("長", 126, 30),
            W("の", 158, 30), W("私", 190, 30), W("が", 222, 30)), top:=0, confidence:=0.6F)

        Assert.Equal("コモナ市長の私が", line.Text)
        Assert.Equal("short-low-confidence", OcrTextFilter.LineRejection(line.SpacedText, line.Confidence))
        Assert.Equal("", OcrTextFilter.LineRejection(line.Text, line.Confidence))
    End Sub

    <Fact>
    Public Sub TheEngineDecisions_ReadTheSpacedText_NotTheJoinedOne()
        ' The engine class cannot be built without Tesseract, so the one place its text and its
        ' decisions part ways is held by reading the source: ScoreAttempt and the line gate read
        ' SpacedText, and the line's text is made by the join.
        Dim source As String = File.ReadAllText(Path.Combine(SourceDir(), "Ocr", "TesseractOcrEngine.vb"))

        Dim score As String = Body(source, "Private Shared Function ScoreAttempt(")
        Assert.Contains("line.SpacedText", score)
        Assert.DoesNotContain("line.Text", score)

        Dim extract As String = Body(source, "Private Shared Function ExtractLines(")
        Assert.Contains("LineRejection(line.SpacedText", extract)

        Dim read As String = Body(source, "Private Shared Function ReadPageLines(")
        Assert.Contains("OcrTextJoin.JoinLineWords(words)", read)

        Dim splitter As String = File.ReadAllText(Path.Combine(SourceDir(), "Ocr", "OcrLineSplitter.vb"))
        Assert.Contains("LineRejection(l.SpacedText", splitter)
        Assert.Contains("OcrTextJoin.JoinLineWords(own)", splitter)

        Dim builder As String = File.ReadAllText(Path.Combine(SourceDir(), "Ocr", "OcrBlockBuilder.vb"))
        Assert.Contains("OcrTextJoin.JoinPlateLines(", builder)
    End Sub

    <Fact>
    Public Sub PipelineVersion_CarriesTheSplitAndJoinBump()
        ' 1.7 A changes which lines exist and 1.8 changes every CJK plate's source text, which is
        ' cached content: the disk must not hand back a document the old code built. A revert of
        ' the bump would show here before it shows as a stale overlay.
        Assert.True(OcrPipeline.OcrPipelineVersion >= 11,
                    "OcrPipelineVersion must be at least 11 once 1.7 A and 1.8 are in the pipeline")
    End Sub

    ' --- helpers ----------------------------------------------------------------

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src/ not found above " & AppContext.BaseDirectory)
    End Function

    ''' <summary>The text of one procedure: from its declaration to the next "End Function".</summary>
    Private Shared Function Body(source As String, declaration As String) As String
        Dim start As Integer = source.IndexOf(declaration, StringComparison.Ordinal)
        Assert.True(start >= 0, "declaration not found: " & declaration)
        Dim finish As Integer = source.IndexOf("End Function", start, StringComparison.Ordinal)
        Assert.True(finish > start, "no End Function after: " & declaration)
        Return source.Substring(start, finish - start)
    End Function

End Class
