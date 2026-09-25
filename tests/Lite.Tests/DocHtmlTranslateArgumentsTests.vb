Option Strict On

Imports System.IO
Imports Xunit

' Ticket 033 Ф-7: the command line this product sends across the OCR-INVOCATION boundary.
' Both legs - the browser-translate button ships in both viewers.
Public Class DocHtmlTranslateArgumentsTests

    <Fact>
    Public Sub Auto_SendsNoLanguage()
        ' An assumed ladder sent as -ocr-lang switched off the producer's own script pass.
        Assert.Equal("", DocHtmlTranslate.OcrLangArgument("auto"))
        Assert.Equal("", DocHtmlTranslate.OcrLangArgument(""))
        Assert.Equal("", DocHtmlTranslate.OcrLangArgument(Nothing))
    End Sub

    <Fact>
    Public Sub AChosenLanguage_IsPassed()
        Assert.Equal("rus", DocHtmlTranslate.OcrLangArgument("rus"))
        Assert.Equal("jpn", DocHtmlTranslate.OcrLangArgument(OcrTranslateSettings.NormalizeOcrSourceCode("ja")))
    End Sub

    <Fact>
    Public Sub NoLanguage_MeansNoFlagAtAll()
        Dim args As String = DocHtmlTranslate.BuildArguments("", False, "C:\cache\x", "C:\pics\a b.png")
        Assert.DoesNotContain("-ocr-lang", args)
    End Sub

    <Fact>
    Public Sub TheImagePath_IsLastAndQuoted()
        Dim args As String = DocHtmlTranslate.BuildArguments("rus", True, "C:\cache\x", "C:\pics\a b.png")
        Assert.Equal("-ocr-lang rus -force -folder ""C:\cache\x"" ""C:\pics\a b.png""", args)
        Assert.EndsWith("""C:\pics\a b.png""", args)
    End Sub

    <Fact>
    Public Sub TheOutputFolder_IsNeverThePicturesOwnFolder()
        ' The producer writes its HTML into -folder; it must land in our cache, not beside
        ' the user's photos.
        Dim root As String = DocHtmlTranslate.OutputCacheRoot()
        Dim pictureFolder As String = Path.GetDirectoryName(Path.GetFullPath("C:\pics\a.png"))
        Assert.False(String.Equals(root, pictureFolder, StringComparison.OrdinalIgnoreCase))
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), root, StringComparison.OrdinalIgnoreCase)
    End Sub

End Class
