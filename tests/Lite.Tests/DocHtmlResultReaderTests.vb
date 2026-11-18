#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports Xunit

' Specification 276 section 7.3 / T-3 on OCR-INVOCATION 1.2 items C and D. The records here are written by this
' ticket itself, labelled as its own, until the producer's fixtures (item I) exist.
Public Class DocHtmlResultReaderTests

    Private Const Source As String = "C:\Books\a book.epub"

    Private Shared Function Record(Optional complete As String = Nothing, Optional schema As String = Nothing,
                                   Optional tool As String = "doc-html-translate", Optional source As String = "C:\\Books\\a book.epub") As String
        Dim head = "{" & If(schema Is Nothing, "", """schemaVersion"":" & schema & ",") &
                   """tool"":""" & tool & """,""source"":""" & source & """,""sourceSize"":10,""sourceModTime"":""2026-11-12T10:00:00Z"",""created"":""2026-11-12T10:00:01Z"""
        If complete IsNot Nothing Then head &= ",""complete"":" & complete
        Return head & ",""futureMember"":{""x"":1}}"
    End Function

    Private Shared Function Complete(engine As String, translation As String, Optional ocr As String = "false", Optional ocrForced As String = Nothing) As String
        Return "{""completed"":""2026-11-12T10:05:00Z"",""toolVersion"":""26.1021.0300"",""options"":{""engine"":""" & engine &
               """,""ocr"":" & ocr & ",""singlePage"":false,""splitSize"":0,""tocDepth"":0}" &
               If(ocrForced Is Nothing, "", ",""ocrForced"":" & ocrForced) & ",""translation"":""" & translation & """}"
    End Function

    Private Shared Function Parse(json As String, Optional source As String = Source) As DocHtmlResult
        Dim result As New DocHtmlResult()
        DocHtmlResultReader.ParseRecord(json, source, result)
        Return result
    End Function

    <Fact>
    Public Sub ACompletedRecord_WithoutSchemaVersion_IsSchemaOne()
        Dim r = Parse(Record(Complete("none", "none")))
        Assert.Equal(DocHtmlResultState.Completed, r.State)
        Assert.Equal(1, r.SchemaVersion)
        Assert.Equal("26.1021.0300", r.ToolVersion)
        Assert.Equal("none", r.Engine)
        Assert.True(r.CompletedUtc.HasValue)
    End Sub

    <Fact>
    Public Sub NoComplete_IsIncomplete()
        Assert.Equal(DocHtmlResultState.Incomplete, Parse(Record()).State)
    End Sub

    <Fact>
    Public Sub AHigherSchema_IsNotCompleted()
        Assert.Equal(DocHtmlResultState.NewerSchema, Parse(Record(Complete("none", "none"), schema:="2")).State)
        Assert.Equal(DocHtmlResultState.Completed, Parse(Record(Complete("none", "none"), schema:="1")).State)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("not json")>
    <InlineData("[1,2]")>
    <InlineData("{""tool"":""doc-html-translate"",""source"":5}")>
    Public Sub AMalformedRecord_IsForeign(json As String)
        Assert.Equal(DocHtmlResultState.Foreign, Parse(json).State)
    End Sub

    <Fact>
    Public Sub AForeignToolOrAnotherSource_IsForeign()
        Assert.Equal(DocHtmlResultState.Foreign, Parse(Record(Complete("none", "none"), tool:="other")).State)
        Assert.Equal(DocHtmlResultState.Foreign, Parse(Record(Complete("none", "none")), "C:\Books\other.epub").State)
    End Sub

    ' --- item C rule 3: the reader says which not-completed case it was (L0330) -----------------------------

    <Theory>
    <InlineData("")>
    <InlineData("not json")>
    <InlineData("[1,2]")>
    <InlineData("{""schemaVersion"":""two"",""tool"":""doc-html-translate""}")>
    Public Sub AnUnparseableRecord_NamesTheFault(json As String)
        Dim r = Parse(json)
        Assert.Equal(DocHtmlResultState.Foreign, r.State)
        Assert.Equal(DocHtmlRecordFault.Unparseable, r.Fault)
    End Sub

    <Fact>
    Public Sub AForeignToolAndAnotherSource_AreToldApart()
        Assert.Equal(DocHtmlRecordFault.ForeignTool, Parse(Record(Complete("none", "none"), tool:="other")).Fault)
        Assert.Equal(DocHtmlRecordFault.OtherSource, Parse(Record(Complete("none", "none")), "C:\Books\other.epub").Fault)
    End Sub

    <Fact>
    Public Sub ACompletedRecordAndANewerSchema_CarryNoFault()
        Assert.Equal(DocHtmlRecordFault.None, Parse(Record(Complete("none", "none"))).Fault)
        Assert.Equal(DocHtmlRecordFault.None, Parse(Record(Complete("none", "none"), schema:="2")).Fault)
        Assert.Equal(DocHtmlRecordFault.None, Parse(Record()).Fault)
    End Sub

    <Theory>
    <InlineData(0)>
    <InlineData(4)>
    Public Sub AnUnparseableOrForeignRecord_IsNotAnUnfinishedRun(exitCode As Integer)
        Dim foreign = Parse("not json")
        Dim outcome = DocHtmlResultReader.Outcome(foreign, exitCode, False, False)
        Assert.Equal(DocHtmlJobOutcome.RecordNotRecognized, outcome)
        Assert.NotEqual(DocHtmlJobOutcome.Incomplete, outcome)
        Assert.False(DocHtmlResultReader.HasPage(outcome))
        Assert.Equal(DocHtmlJobOutcome.RecordNotRecognized, DocHtmlResultReader.Outcome(Parse(Record(Complete("none", "none"), tool:="other")), exitCode, False, False))
        ' ..while the run that never wrote complete stays an unfinished run.
        Assert.Equal(DocHtmlJobOutcome.Incomplete, DocHtmlResultReader.Outcome(Parse(Record()), exitCode, False, False))
    End Sub

    <Theory>
    <InlineData(1, DocHtmlJobOutcome.BadArguments)>
    <InlineData(2, DocHtmlJobOutcome.IoError)>
    <InlineData(3, DocHtmlJobOutcome.Unreadable)>
    <InlineData(130, DocHtmlJobOutcome.Cancelled)>
    Public Sub AnUnparseableRecord_WithAnotherExit_StillNamesHowTheProcessEnded(exitCode As Integer, expected As DocHtmlJobOutcome)
        Assert.Equal(expected, DocHtmlResultReader.Outcome(Parse("not json"), exitCode, False, False))
    End Sub

    <Fact>
    Public Sub TheLogDetail_NamesTheCase_WithoutAPathOrTheRecordText()
        Assert.Equal("record-fault=Unparseable", DocHtmlResultReader.LogDetail(Parse("not json")))
        Assert.Equal("record-fault=ForeignTool", DocHtmlResultReader.LogDetail(Parse(Record(Complete("none", "none"), tool:="other"))))
        Assert.Equal("record-fault=OtherSource", DocHtmlResultReader.LogDetail(Parse(Record(Complete("none", "none")), "C:\Books\other.epub")))

        Dim newer = DocHtmlResultReader.LogDetail(Parse(Record(Complete("none", "none"), schema:="2")))
        Assert.Contains("OCR-INVOCATION 1.2", newer)
        Assert.Contains("record-schema=2", newer)
        Assert.Contains("reader-schema=1", newer)

        Assert.Equal("", DocHtmlResultReader.LogDetail(Parse(Record(Complete("none", "none")))))
        Assert.Equal("", DocHtmlResultReader.LogDetail(Parse(Record())))
        Assert.Equal("", DocHtmlResultReader.LogDetail(Nothing))
        ' The record's own text and the source path never reach the line.
        Dim withSource = DocHtmlResultReader.LogDetail(Parse(Record(Complete("none", "none")), "C:\Books\other.epub"))
        Assert.DoesNotContain("Books", withSource)
        Assert.DoesNotContain("epub", withSource)
    End Sub

    <Fact>
    Public Sub TheSourceIsComparedAsAPath()
        Assert.Equal(DocHtmlResultState.Completed, Parse(Record(Complete("none", "none")), "c:\books\A BOOK.epub").State)
    End Sub

    <Fact>
    Public Sub RecognitionIsRequestedNeverFound()
        Assert.False(Parse(Record(Complete("none", "none"))).RecognitionRequested)
        Assert.True(Parse(Record(Complete("none", "none", ocr:="true"))).RecognitionRequested)
        Assert.True(Parse(Record(Complete("none", "none", ocrForced:="true"))).RecognitionRequested)
    End Sub

    <Theory>
    <InlineData(0, "none", "none", DocHtmlJobOutcome.Converted)>
    <InlineData(0, "ollama", "full", DocHtmlJobOutcome.FullyTranslated)>
    <InlineData(4, "google", "partial", DocHtmlJobOutcome.PartiallyTranslated)>
    <InlineData(0, "google", "none", DocHtmlJobOutcome.TranslationSkipped)>
    <InlineData(4, "ollama", "none", DocHtmlJobOutcome.TranslationUnavailable)>
    <InlineData(3, "none", "none", DocHtmlJobOutcome.Failed)>
    Public Sub ACompletedRecordAndTheExitCode_GiveTheOutcome(exitCode As Integer, engine As String, translation As String, expected As DocHtmlJobOutcome)
        Dim r = Parse(Record(Complete(engine, translation)))
        Assert.Equal(expected, DocHtmlResultReader.Outcome(r, exitCode, False, False))
    End Sub

    <Theory>
    <InlineData(0, DocHtmlJobOutcome.Incomplete)>
    <InlineData(1, DocHtmlJobOutcome.BadArguments)>
    <InlineData(2, DocHtmlJobOutcome.IoError)>
    <InlineData(3, DocHtmlJobOutcome.Unreadable)>
    <InlineData(130, DocHtmlJobOutcome.Cancelled)>
    <InlineData(-1, DocHtmlJobOutcome.Failed)>
    Public Sub WithoutComplete_ExitZeroIsNeverAPage(exitCode As Integer, expected As DocHtmlJobOutcome)
        Dim outcome = DocHtmlResultReader.Outcome(Parse(Record()), exitCode, False, False)
        Assert.Equal(expected, outcome)
        Assert.False(DocHtmlResultReader.HasPage(outcome))
    End Sub

    <Fact>
    Public Sub CancelAndTimeout_WinOverTheFolder()
        Dim r = Parse(Record(Complete("none", "none")))
        Assert.Equal(DocHtmlJobOutcome.Cancelled, DocHtmlResultReader.Outcome(r, -1, True, False))
        Assert.Equal(DocHtmlJobOutcome.TimedOut, DocHtmlResultReader.Outcome(r, -1, True, True))
    End Sub

    <Fact>
    Public Sub Containment_RefusesEscapesAndTheRootItself()
        Assert.True(DocHtmlResultReader.IsInside("C:\cache\j1\book", "C:\cache\j1\book\index.html"))
        Assert.False(DocHtmlResultReader.IsInside("C:\cache\j1\book", "C:\cache\j1\book\..\other\index.html"))
        Assert.False(DocHtmlResultReader.IsInside("C:\cache\j1\book", "C:\cache\j1\book2\index.html"))
        Assert.False(DocHtmlResultReader.IsInside("C:\cache\j1\book", "C:\cache\j1\book"))
    End Sub

    <Fact>
    Public Sub Read_FindsTheSingleChildAndVerifiesTheEntry()
        Dim jobRoot = Path.Combine(Path.GetTempPath(), "fms-l0276-" & Guid.NewGuid().ToString("N"))
        Try
            Dim output = Path.Combine(jobRoot, "a book")
            Directory.CreateDirectory(output)
            Dim src = Path.Combine(jobRoot, "src.epub")
            File.WriteAllText(Path.Combine(output, DocHtmlResultReader.RecordFileName),
                              Record(Complete("none", "none"), source:=src.Replace("\", "\\")))
            ' Finished per the record, but no entry yet: not a page.
            Assert.Equal(DocHtmlResultState.Unsafe, DocHtmlResultReader.Read(jobRoot, src).State)
            File.WriteAllText(Path.Combine(output, DocHtmlResultReader.EntryFileName), "<html></html>")
            Dim r = DocHtmlResultReader.Read(jobRoot, src)
            Assert.Equal(DocHtmlResultState.Completed, r.State)
            Assert.Equal(Path.GetFullPath(Path.Combine(output, "index.html")), r.EntryPath)
        Finally
            If Directory.Exists(jobRoot) Then Directory.Delete(jobRoot, True)
        End Try
    End Sub

    <Fact>
    Public Sub Read_AnEmptyOrMissingFolder_IsMissing()
        Assert.Equal(DocHtmlResultState.Missing, DocHtmlResultReader.Read(Path.Combine(Path.GetTempPath(), "fms-l0276-absent-" & Guid.NewGuid().ToString("N")), Source).State)
    End Sub
End Class
#End If
