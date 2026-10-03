Option Strict On

Imports Xunit

' Audit 051: the translator-side decisions that throw a translation away or keep a failure.
' Both legs - the translators ship in both viewers.
Public Class TranslationQualityTests

    ' Ticket 093: ordinary non-Latin translations must survive quality checks.
    <Theory>
    <InlineData("hi", "तुम कहाँ जा रहे हो?")>
    <InlineData("bn", "তুমি কোথায় যাচ্ছ?")>
    <InlineData("ja", "「待って！」「どこへ行くの？」")>
    Public Sub NonLatinDialogue_IsNotNoise(target As String, candidate As String)
        Assert.Equal(0.0, SymbolRatio(candidate))
        Assert.Equal("", TranslationQuality.BadTranslationReason("Where are you going?", candidate, target))
    End Sub

    <Fact>
    Public Sub CombiningMarkCategories_AreNotNoise()
        Assert.Equal(0.0, SymbolRatio("a" & ChrW(&H301) & ChrW(&H93E) & ChrW(&H20DD)))
    End Sub

    <Theory>
    <InlineData("zh", "我已经告诉过你了。我们今晚不能待在这里。暴风雨要来了。")>
    <InlineData("ja", "もう言いました。今夜ここにはいられません。嵐が来ています。")>
    Public Sub CompleteCjkSentences_AreNotTruncated(target As String, candidate As String)
        Assert.Equal("", TranslationQuality.BadTranslationReason(
            "I have already told you. We cannot stay here tonight. The storm is coming.", candidate, target))
    End Sub

    <Theory>
    <InlineData(12290)>
    <InlineData(65281)>
    <InlineData(65311)>
    <InlineData(2404)>
    <InlineData(1748)>
    <InlineData(1567)>
    Public Sub LocalSentenceEnds_CountAsCompleteSentences(marker As Integer)
        Dim ending As String = ChrW(marker)
        Dim candidate As String = "你好我的朋友们" & ending & "今晚我们要离开" & ending & "暴风雨马上来了" & ending
        Assert.Equal(0.0, SymbolRatio(candidate))
        Assert.False(TranslationQuality.LooksTruncated(
            "I have already told you. We cannot stay here tonight. The storm is coming.", candidate, "zh"))
    End Sub

    <Theory>
    <InlineData("zh", "どこへ行くの？")>
    <InlineData("chi_tra", "どこへ行くの？")>
    <InlineData("ko", "どこへ行くの？")>
    <InlineData("ja", "어디로 가세요?")>
    <InlineData("zh-cn", "어디로 가세요?")>
    Public Sub ForeignCjkScript_EchoRemainsRetryable(target As String, source As String)
        Assert.Equal("echo", TranslationQuality.BadTranslationReason(source, source, target))
    End Sub

    <Theory>
    <InlineData("zh", "明天見")>
    <InlineData("ja", "明天見")>
    <InlineData("ja", "どこへ行くの？")>
    <InlineData("ko", "어디로 가세요?")>
    Public Sub CompatibleCjkScript_EchoIsAccepted(target As String, source As String)
        Assert.Equal("", TranslationQuality.BadTranslationReason(source, source, target))
    End Sub

    <Theory>
    <InlineData("Ich kann das nicht übersetzen.")>
    <InlineData("No puedo traducir esto.")>
    <InlineData("আমি এটা অনুবাদ করতে পারি না।")>
    Public Sub DialogueAboutTranslation_InOtherSourceLanguages_IsNotRefusal(source As String)
        Assert.Equal("", TranslationQuality.BadTranslationReason(source, "Я не могу это перевести.", "ru"))
    End Sub

    ' --- E-03: a refusal is judged by shape, not by vocabulary ------------------

    <Fact>
    Public Sub PleaseWait_IntoRussian_IsNotARefusal()
        ' "Пожалуйста" was a bare refusal marker: the translation was rejected twice and the
        ' English source shown - and cached - instead.
        Assert.Equal("", TranslationQuality.BadTranslationReason("Please wait!", "Пожалуйста, подождите!", "ru"))
    End Sub

    <Fact>
    Public Sub SorryICant_IntoEnglish_IsNotARefusal()
        Assert.Equal("", TranslationQuality.BadTranslationReason("Извини, я не могу", "Sorry, I can't", "en"))
    End Sub

    <Fact>
    Public Sub APageThatTalksAboutTranslating_StillTranslates()
        ' The source mentions translation itself, so the candidate may too.
        Assert.Equal("", TranslationQuality.BadTranslationReason("I can't translate this letter", "Я не могу перевести это письмо", "ru"))
    End Sub

    <Fact>
    Public Sub ARealRefusal_IsStillARefusal()
        Assert.Equal("refusal", TranslationQuality.BadTranslationReason(
            "Привет, как дела у тебя сегодня?", "I'm sorry, but I cannot translate this text.", "en"))
    End Sub

    <Fact>
    Public Sub InstructionTalk_WithoutAnApology_IsStillARefusal()
        ' Two meta concepts the source never used: the model is describing the request.
        Assert.Equal("refusal", TranslationQuality.BadTranslationReason(
            "Добрый вечер", "Output only the JSON translation of the input segment.", "en"))
    End Sub

    ' --- E-03: an echo that no retry improves is accepted -----------------------

    <Fact>
    Public Sub ABrandName_EchoedIntoRussian_IsAccepted()
        Assert.Equal("", TranslationQuality.BadTranslationReason("Samsung", "Samsung", "ru"))
    End Sub

    <Fact>
    Public Sub EnglishText_EchoedIntoEnglish_IsAccepted()
        ' Already in the target language - retrying cost two generations per block.
        Assert.Equal("", TranslationQuality.BadTranslationReason("Keep calm and carry on", "Keep calm and carry on", "en"))
    End Sub

    <Fact>
    Public Sub UkrainianText_EchoedForARussianTarget_IsStillAnEcho()
        ' One script, two languages: this is an untranslated page, and it must stay retryable
        ' rather than be cached as a translation.
        Assert.Equal("echo", TranslationQuality.BadTranslationReason("Привіт, як справи?", "Привіт, як справи?", "ru"))
    End Sub

    <Fact>
    Public Sub ACyrillicWord_EchoedIntoEnglish_IsStillAnEcho()
        Assert.Equal("echo", TranslationQuality.BadTranslationReason("Привет", "Привет", "en"))
    End Sub

    <Fact>
    Public Sub GermanText_EchoedIntoEnglish_IsStillAnEcho()
        Assert.Equal("echo", TranslationQuality.BadTranslationReason("Schöne Grüße aus Köln", "Schöne Grüße aus Köln", "en"))
    End Sub

    ' --- the checks that did not change still work --------------------------------

    <Fact>
    Public Sub WrongScript_IsStillCaught()
        Assert.Equal("wrong-script", TranslationQuality.BadTranslationReason("Hello, my friend", "你好，我的朋友", "ru"))
    End Sub

    <Fact>
    Public Sub Empty_IsStillCaught()
        Assert.Equal("empty", TranslationQuality.BadTranslationReason("Hello", "  ", "ru"))
    End Sub

    ' --- E-10: the punctuation whitelist ---------------------------------------------

    <Fact>
    Public Sub CurlyQuotes_AreNotNoise()
        Dim quoted As String = ChrW(&H201C) & "hello" & ChrW(&H201D)
        Assert.Equal(0.0, SymbolRatio(quoted))
    End Sub

    <Fact>
    Public Sub DialogueDashesAndEllipsis_AreNotNoise()
        Dim line As String = ChrW(&H2014) & " Да. " & ChrW(&H2014) & " Нет" & ChrW(&H2026)
        Assert.Equal(0.0, SymbolRatio(line))
    End Sub

    <Fact>
    Public Sub RealSymbolNoise_IsStillNoise()
        Assert.True(SymbolRatio("#$%^&*@~") > 0.9)
    End Sub

    ' --- ticket 093: Indic and CJK answers are not rejected by rules written for Latin ---

    <Fact>
    Public Sub HindiTranslation_IsNotNoise()
        ' Vowel signs and virama are Mn/Mc: neither letters nor punctuation, 6 of 15 counted as noise.
        Dim hindi As String = "तुम कहाँ जा रहे हो?"
        Assert.True(SymbolRatio(hindi) <= 0.18)
        Assert.Equal("", TranslationQuality.BadTranslationReason("Where are you going?", hindi, "hi"))
    End Sub

    <Fact>
    Public Sub BengaliTranslation_IsNotNoise()
        Assert.Equal("", TranslationQuality.BadTranslationReason("Where are you going?", "তুমি কোথায় যাচ্ছো?", "bn"))
    End Sub

    <Fact>
    Public Sub JapaneseBrackets_AreNotNoise()
        Dim line As String = "「待って！」「どこへ行くの？」"
        Assert.True(SymbolRatio(line) <= 0.18)
        Assert.Equal("", TranslationQuality.BadTranslationReason("Wait! Where are you going?", line, "ja"))
    End Sub

    <Fact>
    Public Sub RealSymbolNoise_IsStillNoise_AmongMarks()
        Assert.Equal("noisy", TranslationQuality.BadTranslationReason("Where are you going?", "तुम #$%^&*@~ ~~ %%$#", "hi"))
    End Sub

    <Fact>
    Public Sub ThreeSentenceBalloon_IntoChinese_IsNotTruncated()
        ' Sentence ends counted in ASCII only made "。" worth zero, so 24 letters against 3 ends looked cut off.
        Assert.Equal("", TranslationQuality.BadTranslationReason(
            "I already told you. We can't stay here tonight. A storm is coming.",
            "我已经告诉过你了。我们今晚不能待在这里。暴风雨要来了。", "zh"))
    End Sub

    <Fact>
    Public Sub DandaAndArabicFullStop_AreSentenceEnds()
        ' Same rule for the other scripts: three sentences in, three sentences out.
        Assert.Equal("", TranslationQuality.BadTranslationReason(
            "I already told you. We can't stay here tonight. A storm is coming.",
            "मैंने तुम्हें बताया। हम आज रात यहाँ नहीं रुक सकते। तूफ़ान आ रहा है।", "hi"))
    End Sub

    <Fact>
    Public Sub JapaneseSource_EchoedForAChineseTarget_IsAnEcho()
        Assert.Equal("echo", TranslationQuality.BadTranslationReason("どこへ行くの？", "どこへ行くの？", "zh"))
    End Sub

    <Fact>
    Public Sub HangulSource_EchoedForAJapaneseTarget_IsAnEcho()
        Assert.Equal("echo", TranslationQuality.BadTranslationReason("어디 가는 거야?", "어디 가는 거야?", "ja"))
    End Sub

    <Fact>
    Public Sub HanOnlySource_EchoedForAChineseTarget_IsAccepted()
        ' Han is shared by Chinese and Japanese: it is not evidence of the wrong language.
        Assert.Equal("", TranslationQuality.BadTranslationReason("你要去哪里？", "你要去哪里？", "zh"))
    End Sub

    <Fact>
    Public Sub GermanPageAboutTranslating_IntoRussian_IsNotARefusal()
        Assert.Equal("", TranslationQuality.BadTranslationReason("Ich kann das nicht übersetzen.", "Я не могу это перевести.", "ru"))
    End Sub

    ' --- E-11: paragraph direction follows the drawn text --------------------------

    <Fact>
    Public Sub ArabicText_IsRightToLeft_WhateverTheTarget()
        Assert.True(IsRightToLeftText("مرحبا!", "en"))
        Assert.True(IsRightToLeftText("שלום", "ru"))
    End Sub

    <Fact>
    Public Sub LatinText_IsLeftToRight_EvenForAnArabicTarget()
        ' A partial document keeps the source text on a plate whose target is Arabic.
        Assert.False(IsRightToLeftText("Hello!", "ar"))
    End Sub

    <Fact>
    Public Sub TextWithoutLetters_TakesTheTargetDirection()
        Assert.True(IsRightToLeftText("123!", "ar"))
        Assert.False(IsRightToLeftText("123!", "de"))
    End Sub

    ' --- E-06: every language the pickers offer has a name for the prompt -----------

    <Fact>
    Public Sub EveryTargetLanguage_HasADisplayName()
        For Each entry As LanguageEntry In OcrLanguageCatalog.TargetLanguages()
            Assert.NotEqual(entry.Code, TranslateLang.DisplayName(entry.Code))
        Next
    End Sub

    <Fact>
    Public Sub EveryInterfaceLanguage_HasADisplayName()
        ' The default target IS the interface language.
        For Each code As String In Localization.Codes
            Assert.NotEqual(code, TranslateLang.DisplayName(code))
        Next
    End Sub

    ' --- E-13: LibreTranslate's own codes --------------------------------------------

    <Theory>
    <InlineData("zh-TW", "zt")>
    <InlineData("no", "nb")>
    <InlineData("chi_sim", "zh")>
    <InlineData("rus", "ru")>
    <InlineData("de", "de")>
    <InlineData("auto", "auto")>
    Public Sub LibreTranslateCodes_AreTheServersOwn(ours As String, theirs As String)
        Assert.Equal(theirs, TranslateLang.LibreTranslateCode(ours))
    End Sub

    <Fact>
    Public Sub Belarusian_IsRefusedUpFront_ForLibreTranslate()
        Assert.Equal("", TranslateLang.LibreTranslateCode("be"))
    End Sub

End Class
