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
End Class
#End If
