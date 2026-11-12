#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

' Specification 276 section 7.2 / T-2: the document command line of doc-html-translate. The image route keeps
' its own builder and its own facts (DocHtmlTranslateArgumentsTests); nothing here may touch it.
Public Class DocHtmlJobArgumentsTests

    Private Const Input As String = "C:\Books\a book.epub"
    Private Const Root As String = "C:\Users\u\AppData\Local\SZA\FastMediaSorter\document-convert-cache\j1"

    <Fact>
    Public Sub BrowserMode_SendsTheFolderAndTheInputOnly()
        Dim args = DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeBrowser, False)
        Assert.Equal(New String() {"-folder", Root, Input}, args.ToArray())
    End Sub

    <Fact>
    Public Sub ConvertMode_SendsNoTranslate()
        Dim args = DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeConvert, False)
        Assert.Equal(New String() {"-folder", Root, "-notranslate", Input}, args.ToArray())
    End Sub

    <Fact>
    Public Sub LocalMode_SendsOllamaAndNeverGoogle()
        Dim args = DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeLocal, False)
        Assert.Equal(New String() {"-folder", Root, "-ollama", Input}, args.ToArray())
        Assert.DoesNotContain("-google", args)
    End Sub

    <Fact>
    Public Sub Force_IsSentBeforeTheInput()
        Dim args = DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeBrowser, True)
        Assert.Equal(New String() {"-folder", Root, "-force", Input}, args.ToArray())
    End Sub

    <Fact>
    Public Sub AnOwnedJob_SendsNoOpenBeforeTheInput()
        ' OCR-INVOCATION 1.2 item B rule 1: a caller that opens the verified result itself always sends -noopen.
        Dim args = DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeConvert, False, noOpen:=True)
        Assert.Equal(New String() {"-folder", Root, "-notranslate", "-noopen", Input}, args.ToArray())
    End Sub

    <Fact>
    Public Sub TheHandoff_SendsNoNoOpen()
        Assert.DoesNotContain("-noopen", DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeBrowser, True))
    End Sub

    <Theory>
    <InlineData("browser")>
    <InlineData("convert")>
    <InlineData("local")>
    Public Sub NoMode_EverSendsAFlagOutsideTheT2Set(mode As String)
        ' -noopen waits for the producer profile; the paid route, registration, UI language, reports and
        ' secrets are never part of a T-2 job.
        Dim args = DocHtmlJobArguments.Build(Input, Root, mode, True)
        Dim allowed As New HashSet(Of String) From {"-folder", "-notranslate", "-ollama", "-force"}
        For Each item In args.Take(args.Count - 1)
            If item = Root Then Continue For
            Assert.Contains(item, allowed)
        Next
        Assert.Equal(Input, args.Last())
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("paid")>
    <InlineData("Browser")>
    Public Sub AnUnknownMode_IsRefused(mode As String)
        Assert.Null(DocHtmlJobArguments.Build(Input, Root, mode, False))
    End Sub

    <Theory>
    <InlineData("book.epub")>
    <InlineData("-version")>
    <InlineData("..\book.epub")>
    <InlineData("\book.epub")>
    <InlineData("\\?\C:\book.epub")>
    <InlineData("C:book.epub")>
    Public Sub ARelativeOrDeviceInput_IsRefused(path As String)
        Assert.Null(DocHtmlJobArguments.Build(path, Root, DocHtmlJobArguments.ModeBrowser, False))
    End Sub

    <Fact>
    Public Sub AControlCharacterInAPath_IsRefused()
        Assert.Null(DocHtmlJobArguments.Build("C:\Books\a" & ChrW(10) & "b.epub", Root, DocHtmlJobArguments.ModeBrowser, False))
        Assert.Null(DocHtmlJobArguments.Build(Input, "C:\cache" & ChrW(0) & "x", DocHtmlJobArguments.ModeBrowser, False))
    End Sub

    <Fact>
    Public Sub UncAndUnicodePaths_AreAccepted()
        Dim unc As String = "\\nas\books\Пушкин «Сказки».pdf"
        Dim args = DocHtmlJobArguments.Build(unc, Root, DocHtmlJobArguments.ModeBrowser, False)
        Assert.NotNull(args)
        Assert.Equal(unc, args.Last())
    End Sub

    <Fact>
    Public Sub ARelativeOutputRoot_IsRefused()
        Assert.Null(DocHtmlJobArguments.Build(Input, "cache\j1", DocHtmlJobArguments.ModeBrowser, False))
    End Sub

    ' ---- Step 3.2: the section 5 choices on the 7.2 rows ----

    <Fact>
    Public Sub LocalMode_SendsModelAndLanguagesBeforeTheInput()
        Dim o As New DocHtmlJobOptions With {.LocalModel = "llama3.1:8b", .SourceLanguage = "en", .TargetLanguage = "uk"}
        Dim args = DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeLocal, False, noOpen:=True, options:=o)
        Assert.Equal(New String() {"-folder", Root, "-ollama", "-ollama-model", "llama3.1:8b", "-src", "en", "-dst", "uk", "-noopen", Input}, args.ToArray())
    End Sub

    <Fact>
    Public Sub LanguagesAndModel_AreNotSentWithoutLocalTranslation()
        Dim o As New DocHtmlJobOptions With {.LocalModel = "m", .SourceLanguage = "en", .TargetLanguage = "uk"}
        Dim browser = DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeBrowser, False, options:=o)
        Dim convert = DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeConvert, False, options:=o)
        Assert.Equal(New String() {"-folder", Root, Input}, browser.ToArray())
        Assert.Equal(New String() {"-folder", Root, "-notranslate", Input}, convert.ToArray())
    End Sub

    <Fact>
    Public Sub ForcedRecognition_SendsOcrAndAnExplicitLanguageChoice()
        Dim o As New DocHtmlJobOptions With {.OcrForce = True, .OcrLanguages = "rus+eng"}
        Dim args = DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeConvert, False, options:=o)
        Assert.Equal(New String() {"-folder", Root, "-notranslate", "-ocr", "-ocr-lang", "rus+eng", Input}, args.ToArray())
    End Sub

    <Fact>
    Public Sub AutomaticRecognition_SendsNoOcrFlag()
        Dim args = DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeBrowser, False, options:=New DocHtmlJobOptions())
        Assert.DoesNotContain("-ocr", args)
        Assert.DoesNotContain("-ocr-lang", args)
    End Sub

    <Theory>
    <InlineData("-v")>
    <InlineData("eng rus")>
    <InlineData("eng;calc")>
    <InlineData("""eng""")>
    Public Sub AValueThatCouldBeReadAsAnotherFlag_IsRefused(value As String)
        Assert.Null(DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeBrowser, False, options:=New DocHtmlJobOptions With {.OcrLanguages = value}))
        Assert.Null(DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeLocal, False, options:=New DocHtmlJobOptions With {.TargetLanguage = value}))
        Assert.Null(DocHtmlJobArguments.Build(Input, Root, DocHtmlJobArguments.ModeLocal, False, options:=New DocHtmlJobOptions With {.LocalModel = value}))
    End Sub

    <Fact>
    Public Sub NoRouteSendsGoogleOrACostLimit()
        Dim o As New DocHtmlJobOptions With {.OcrForce = True, .OcrLanguages = "eng", .SourceLanguage = "en", .TargetLanguage = "de", .LocalModel = "m"}
        For Each mode In {DocHtmlJobArguments.ModeBrowser, DocHtmlJobArguments.ModeConvert, DocHtmlJobArguments.ModeLocal}
            Dim args = DocHtmlJobArguments.Build(Input, Root, mode, True, noOpen:=True, options:=o)
            Assert.DoesNotContain("-google", args)
            Assert.DoesNotContain("-max-cost", args)
            Assert.Equal(Input, args.Last())
        Next
    End Sub
End Class
#End If
