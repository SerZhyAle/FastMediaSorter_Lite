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

End Class
