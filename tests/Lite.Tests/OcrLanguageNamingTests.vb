Option Strict On

Imports Xunit

' Audit 051 E-06 and ticket 033 Ф-6: every language the OCR feature talks about has a name a
' person can read - in the picker, in the prompt, and in the "missing language pack" status.
Public Class OcrLanguageNamingTests

    <Fact>
    Public Sub EveryInterfaceLanguage_IsATranslationTarget()
        ' The default target is the interface language; one the picker cannot show was
        ' displayed as "English" while every request asked for it by code.
        For Each code As String In Localization.Codes
            Assert.True(OcrLanguageCatalog.IsTargetLanguage(code), code & " is not in the target picker")
        Next
    End Sub

    <Fact>
    Public Sub AnUnknownTarget_IsNotATarget()
        Assert.False(OcrLanguageCatalog.IsTargetLanguage("xx"))
        Assert.False(OcrLanguageCatalog.IsTargetLanguage("auto"))
    End Sub

    <Fact>
    Public Sub MissingPacks_AreNamedByTheirLanguage()
        Assert.Equal("Русский, English", OcrLanguageCatalog.DescribeTesseractCodes({"rus", "eng"}))
    End Sub

    <Fact>
    Public Sub AnUnknownPack_IsStillNamed_NotDropped()
        Assert.Equal("xyz", OcrLanguageCatalog.DescribeTesseractCodes({"xyz"}))
    End Sub

    <Fact>
    Public Sub EveryCatalogLanguage_MapsBackFromItsTesseractCode()
        ' The status names the language the engine asked for; a code that maps to no entry
        ' would be printed raw ("chi_tra") instead.
        For Each entry As LanguageEntry In OcrLanguageCatalog.TargetLanguages()
            Dim tess As String = OcrTranslateSettings.NormalizeOcrSourceCode(entry.Code)
            Assert.Equal(entry.Name, OcrLanguageCatalog.DescribeTesseractCodes({tess}))
        Next
    End Sub

    <Fact>
    Public Sub TheMissingPackStatus_CarriesTheLanguageName()
        ' Whatever the interface language (the global is not switched here - other tests do
        ' that concurrently); LocalizationParityTests holds the 12 translations and their arity.
        Dim key As String = "Нет языкового пакета OCR ({0}) - скачайте его: Настройки -> OCR и перевод -> Распознавание (OCR)"
        Dim text As String = Localization.TF(key, OcrLanguageCatalog.DescribeTesseractCodes({"rus"}))
        Assert.Contains("Русский", text)
        Assert.DoesNotContain("{0}", text)
        Assert.Contains("OCR", text)
    End Sub

End Class
