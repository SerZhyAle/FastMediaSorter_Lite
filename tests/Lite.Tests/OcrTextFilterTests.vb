Option Strict On

Imports Xunit

' The translatability filter (SPECIFICATION_OCR_OVERLAY_ACCURACY.md, stage S3). Both legs.
'
' This filter is STRICTER than the letter count it replaces, so its real risk is the
' opposite of the one it was written for: a translation that used to appear and now does
' not. Half these tests are therefore about what must still pass.
Public Class OcrTextFilterTests

    Private Shared Function Keep(text As String, Optional lines As Integer = 1) As Boolean
        Return OcrTextFilter.ShouldTranslate(text, lines)
    End Function

    ' --- noise goes -----------------------------------------------------------

    <Fact>
    Public Sub ConsonantMash_IsRejected()
        ' The recognizer grinding on a texture. Long enough to pass a letter count, which is
        ' exactly why the letter count was not enough.
        Assert.False(Keep("сссчщ"))
        Assert.False(Keep("bcdfg"))
    End Sub

    <Theory>
    <InlineData("https://example.com/x")>
    <InlineData("www.example.com")>
    <InlineData("example.com")>
    <InlineData("someone@example.com")>
    <InlineData("C:\Users\x\a.jpg")>
    <InlineData("\\server\share\file.png")>
    Public Sub WholeAddresses_AreRejected(text As String)
        Assert.False(Keep(text))
    End Sub

    <Fact>
    Public Sub ShortFragments_AreRejected()
        Assert.False(Keep("abc"))
        Assert.False(Keep(""))
        Assert.False(Keep("   "))
        Assert.False(Keep(Nothing))
    End Sub

    ' --- language stays -------------------------------------------------------

    <Fact>
    Public Sub OrdinarySpeech_Passes()
        Assert.True(Keep("Да, хорошо"))
        Assert.True(Keep("What are you doing here?"))
    End Sub

    <Fact>
    Public Sub AddressInsideASentence_DoesNotRejectTheSentence()
        ' "Whole address", not "contains an address" - a line that merely mentions a site is
        ' still a line worth translating.
        Assert.True(Keep("Смотри на example.com завтра"))
    End Sub

    <Fact>
    Public Sub OneMisreadCharacter_DoesNotRejectTheLine()
        ' The filter drops noise; it does not spell-check the recognizer. A word carrying one
        ' wrong glyph still looks like a word, and the majority rule absorbs it.
        Assert.True(Keep("Пpивет, как дела?"))
        Assert.True(Keep("Hellо there friend"))
    End Sub

    <Fact>
    Public Sub TwoLineBlock_KeepsTheOlderLeniency()
        ' Four letters is below the single-line floor of five, but several short lines
        ' together are a speech balloon far more often than they are noise - the leniency the
        ' previous filter already granted, kept as it was.
        Assert.False(Keep("Стоп!", lines:=1))
        Assert.True(Keep("Стоп!", lines:=2))
    End Sub

    ' --- CJK is a branch, not a relaxation ------------------------------------

    <Fact>
    Public Sub Cjk_PassesOnTwoCharacters()
        Assert.True(Keep("日本語"))
        Assert.True(Keep("한국어"))
        Assert.True(Keep("ありがとう"))
    End Sub

    <Fact>
    Public Sub Cjk_SingleIdeograph_IsRejected()
        ' One ideograph on its own is as likely to be a mark on the artwork as a word.
        Assert.False(Keep("光"))
    End Sub

    <Fact>
    Public Sub MixedCjkAndLatin_GoesDownTheCjkBranch()
        ' Judged on the sentence, not on the two Latin letters it happens to carry - the
        ' Latin rules would have rejected this for having no vowel-bearing words.
        Assert.True(Keep("PC 設定"))
    End Sub

    ' --- scripts that do not write vowels -------------------------------------

    <Fact>
    Public Sub AbjadAndAbugida_AreNotJudgedByTheVowelRule()
        ' Arabic, Hebrew and Devanagari do not write vowels. Demanding one would have
        ' rejected every line of those scripts on the page - a whole-language false negative,
        ' which is precisely the risk this stage carries.
        Assert.True(Keep("مرحبا بالعالم"))
        Assert.True(Keep("שלום עולם"))
        Assert.True(Keep("नमस्ते दुनिया"))
    End Sub

    ' --- one predicate decides and records (section 16.1) ----------------------

    <Fact>
    Public Sub Decision_And_Reason_Are_The_Same_Call()
        ' The whole value of recording what the filter dropped rests on the record coming out
        ' of the rule that is actually applied. Two copies of one condition would pass any
        ' review of the constants and drift apart the first time one of them changed - after
        ' which the dump would describe a decision nobody takes. So: for every sample, a
        ' non-empty reason means rejected and an empty reason means kept, with no third
        ' outcome available to either side.
        Dim samples As String() = {
            "сссчщ", "https://example.com/x", "abc", "Да, хорошо", "What are you doing here?",
            "光", "日本語", "PC 設定", "مرحبا بالعالم", "C:\Users\x\a.jpg", "", "   "
        }
        For Each s As String In samples
            For Each lineCount As Integer In New Integer() {1, 2}
                Dim reason As String = OcrTextFilter.RejectionReason(s, lineCount)
                Assert.Equal(reason.Length = 0, OcrTextFilter.ShouldTranslate(s, lineCount))
            Next
        Next
    End Sub

    <Theory>
    <InlineData("сссчщ", "no-vowel")>
    <InlineData("abc", "too-few-letters")>
    <InlineData("https://example.com/x", "address")>
    <InlineData("光", "cjk-too-short")>
    <InlineData("", "empty")>
    Public Sub Reason_Names_The_Rule_That_Refused(text As String, expected As String)
        ' The names travel into the diagnostics dump, so a scene can be read without
        ' re-deriving which threshold bit. They are part of the contract, not a debug string.
        Assert.Equal(expected, OcrTextFilter.RejectionReason(text, 1))
    End Sub

    ' --- the line rule, one step earlier --------------------------------------

    <Fact>
    Public Sub LineRule_Keeps_A_Real_Word_At_Any_Confidence()
        ' Deliberately unchanged, and now deliberately written down: a line carrying a real
        ' word survives however low the engine's confidence is. Downscaled page text scores
        ' low and is correct. The other project measured the cost of the same leniency and
        ' rolled theirs back - safe on average, wrong on a page recognized in the wrong
        ' language - so this test exists to make a future change to it a decision.
        Assert.Equal("", OcrTextFilter.LineRejection("Привет", 0.05F))
        Assert.Equal("", OcrTextFilter.LineRejection("stop", 0.0F))
    End Sub

    <Fact>
    Public Sub LineRule_Rejects_Short_Unconfident_Noise()
        Assert.Equal("short-low-confidence", OcrTextFilter.LineRejection("in", 0.4F))
        Assert.Equal("no-letters-or-digits", OcrTextFilter.LineRejection("\", 0.9F))
        Assert.Equal("mostly-punctuation", OcrTextFilter.LineRejection("a....... ", 0.9F))
    End Sub

    <Fact>
    Public Sub LineRule_Keeps_A_Confident_Short_Token()
        ' "OK", "Да" - two useful characters and the engine is sure.
        Assert.Equal("", OcrTextFilter.LineRejection("OK", 0.9F))
        Assert.Equal("short-low-confidence", OcrTextFilter.LineRejection("OK", 0.5F))
    End Sub

End Class
