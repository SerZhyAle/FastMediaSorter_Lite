Option Strict On

Imports Xunit

' Audit 051: the translator-side decisions that throw a translation away or keep a failure.
' Both legs - the translators ship in both viewers.
Public Class TranslationQualityTests

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
