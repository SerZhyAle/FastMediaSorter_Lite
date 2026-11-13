#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports Xunit

' Specification 276 Step 3.2: the job cache - the viewer's own ticket, reuse by the 7.3 rule 5 comparison, retention,
' persistent leases and Clear. The converter records here are this ticket's own (OCR-INVOCATION 1.2 item I owed).
Public Class DocHtmlJobCacheTests

    Private Const Source As String = "C:\Books\a book.epub"

    Private Shared Function Request(mode As String, Optional options As DocHtmlJobOptions = Nothing) As DocHtmlJobTicket
        Dim t = DocHtmlJobCache.TicketFor(Source, mode, options)
        t.SourceLength = 1234
        t.SourceWriteUtcTicks = 638000000000000000
        Return t
    End Function

    Private Shared Function Completed(engine As String, translation As String) As DocHtmlResult
        Return New DocHtmlResult With {.State = DocHtmlResultState.Completed, .EntryPath = "C:\cache\j1\a book\index.html",
                                       .Engine = engine, .Translation = translation}
    End Function

    <Fact>
    Public Sub Ticket_RoundTripsThroughJson()
        Dim t = Request(DocHtmlJobArguments.ModeLocal, New DocHtmlJobOptions With {.OcrForce = True, .OcrLanguages = "rus+eng",
            .SourceLanguage = "en", .TargetLanguage = "uk", .LocalModel = "llama3.1:8b"})
        t.Leased = True
        Dim back = DocHtmlJobCache.Parse(DocHtmlJobCache.Serialize(t))
        Assert.NotNull(back)
        Assert.True(DocHtmlJobCache.SameRequest(back, t))
        Assert.True(back.Leased)
        Assert.Equal("llama3.1:8b", back.LocalModel)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("not json")>
    <InlineData("{""SchemaVersion"":2,""Mode"":""browser""}")>
    <InlineData("{""SchemaVersion"":1,""Mode"":""google""}")>
    Public Sub Parse_RefusesWhatThisCodeDidNotWrite(json As String)
        Assert.Null(DocHtmlJobCache.Parse(json))
    End Sub

    <Fact>
    Public Sub SameRequest_NeedsTheSameSourceState()
        Dim a = Request(DocHtmlJobArguments.ModeBrowser)
        Dim b = Request(DocHtmlJobArguments.ModeBrowser)
        Assert.True(DocHtmlJobCache.SameRequest(a, b))
        b.SourceLength += 1
        Assert.False(DocHtmlJobCache.SameRequest(a, b))
        b = Request(DocHtmlJobArguments.ModeBrowser)
        b.SourceWriteUtcTicks += 1
        Assert.False(DocHtmlJobCache.SameRequest(a, b))
        b = Request(DocHtmlJobArguments.ModeBrowser)
        b.SourcePath = "C:\Books\other.epub"
        Assert.False(DocHtmlJobCache.SameRequest(a, b))
        b = Request(DocHtmlJobArguments.ModeBrowser)
        b.SourcePath = "c:\books\A BOOK.EPUB"
        Assert.True(DocHtmlJobCache.SameRequest(a, b))
    End Sub

    <Fact>
    Public Sub SameRequest_ComparesModeAndRecognition()
        Assert.False(DocHtmlJobCache.SameRequest(Request(DocHtmlJobArguments.ModeBrowser), Request(DocHtmlJobArguments.ModeConvert)))
        Assert.False(DocHtmlJobCache.SameRequest(Request(DocHtmlJobArguments.ModeBrowser),
                                                 Request(DocHtmlJobArguments.ModeBrowser, New DocHtmlJobOptions With {.OcrForce = True})))
        Assert.False(DocHtmlJobCache.SameRequest(Request(DocHtmlJobArguments.ModeBrowser),
                                                 Request(DocHtmlJobArguments.ModeBrowser, New DocHtmlJobOptions With {.OcrLanguages = "eng"})))
    End Sub

    <Fact>
    Public Sub SameRequest_ComparesLanguagesAndModelOnlyWhenTranslatingLocally()
        Dim withLanguages As New DocHtmlJobOptions With {.SourceLanguage = "en", .TargetLanguage = "de", .LocalModel = "m1"}
        Assert.True(DocHtmlJobCache.SameRequest(Request(DocHtmlJobArguments.ModeConvert),
                                                Request(DocHtmlJobArguments.ModeConvert, withLanguages)))
        Assert.False(DocHtmlJobCache.SameRequest(Request(DocHtmlJobArguments.ModeLocal),
                                                 Request(DocHtmlJobArguments.ModeLocal, withLanguages)))
        Assert.False(DocHtmlJobCache.SameRequest(Request(DocHtmlJobArguments.ModeLocal, withLanguages),
                                                 Request(DocHtmlJobArguments.ModeLocal, New DocHtmlJobOptions With {.SourceLanguage = "en", .TargetLanguage = "de", .LocalModel = "m2"})))
    End Sub

    <Fact>
    Public Sub IsReusable_NeedsAVerifiedPageWithTheModesEngine()
        Dim r = Request(DocHtmlJobArguments.ModeBrowser)
        Assert.True(DocHtmlJobCache.IsReusable(r, Completed("none", "none"), r))
        Assert.False(DocHtmlJobCache.IsReusable(r, Completed("ollama", "full"), r))
        Assert.False(DocHtmlJobCache.IsReusable(r, New DocHtmlResult With {.State = DocHtmlResultState.Incomplete}, r))
        Assert.False(DocHtmlJobCache.IsReusable(r, New DocHtmlResult With {.State = DocHtmlResultState.Unsafe}, r))
        Assert.False(DocHtmlJobCache.IsReusable(Nothing, Completed("none", "none"), r))
    End Sub

    <Fact>
    Public Sub IsReusable_ATranslationOnlyWhenItWasFull()
        Dim r = Request(DocHtmlJobArguments.ModeLocal)
        Assert.True(DocHtmlJobCache.IsReusable(r, Completed("ollama", "full"), r))
        Assert.False(DocHtmlJobCache.IsReusable(r, Completed("ollama", "partial"), r))
        Assert.False(DocHtmlJobCache.IsReusable(r, Completed("ollama", "none"), r))
        Assert.False(DocHtmlJobCache.IsReusable(r, Completed("none", "none"), r))
    End Sub

    <Fact>
    Public Sub IsExpired_KeepsLeasedAndRecentFolders()
        Dim now As New DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc)
        Assert.True(DocHtmlJobCache.IsExpired(now.AddDays(-8), False, now, 7))
        Assert.False(DocHtmlJobCache.IsExpired(now.AddDays(-6), False, now, 7))
        Assert.False(DocHtmlJobCache.IsExpired(now.AddDays(-80), True, now, 7))
        Assert.False(DocHtmlJobCache.IsExpired(DateTime.MaxValue, False, now, 7))
    End Sub

    <Fact>
    Public Sub Folders_LeaseRetentionReuseAndClear()
        Dim root = Path.Combine(Path.GetTempPath(), "fms-l0276-cache-" & Guid.NewGuid().ToString("N"))
        Try
            Dim sourceFile = Path.Combine(root, "src", "book.epub")
            Directory.CreateDirectory(Path.GetDirectoryName(sourceFile))
            File.WriteAllText(sourceFile, "x")
            Dim cache = Path.Combine(root, "cache")
            Dim info As New FileInfo(sourceFile)
            Dim req = DocHtmlJobCache.TicketFor(sourceFile, DocHtmlJobArguments.ModeBrowser, Nothing)
            req.SourceLength = info.Length
            req.SourceWriteUtcTicks = info.LastWriteTimeUtc.Ticks

            ' Two finished jobs of the same request and one foreign folder without a ticket.
            Dim oldJob = MakeJob(cache, "j-old", req, sourceFile)
            Dim newJob = MakeJob(cache, "j-new", req, sourceFile)
            Directory.CreateDirectory(Path.Combine(cache, "foreign"))
            SetTreeTime(oldJob, DateTime.UtcNow.AddDays(-30))

            Dim found = DocHtmlJobCache.FindReusable(cache, req)
            Assert.True(found.HasValue)
            Assert.Equal(newJob, found.Value.JobRoot)

            Assert.True(DocHtmlJobCache.Lease(newJob))
            Assert.Equal({newJob}, DocHtmlJobCache.LeasedFolders(cache).ToArray())

            ' Retention removes the old unleased job only; a 30-day-old leased job would stay.
            Assert.Equal(1, DocHtmlJobCache.ApplyRetention(cache, 7, DateTime.UtcNow, ""))
            Assert.False(Directory.Exists(oldJob))
            Assert.True(Directory.Exists(newJob))

            ' A changed source is never reused.
            File.AppendAllText(sourceFile, "y")
            Dim changed = DocHtmlJobCache.TicketFor(sourceFile, DocHtmlJobArguments.ModeBrowser, Nothing)
            changed.SourceLength = New FileInfo(sourceFile).Length
            changed.SourceWriteUtcTicks = New FileInfo(sourceFile).LastWriteTimeUtc.Ticks
            Assert.False(DocHtmlJobCache.FindReusable(cache, changed).HasValue)

            ' Clear takes everything except the running job's folder, leased folders included.
            Dim running = MakeJob(cache, "j-running", req, sourceFile)
            Dim cleared = DocHtmlJobCache.ClearAll(cache, running)
            Assert.Equal(2, cleared.Removed)
            Assert.Equal(0, cleared.Kept)
            Assert.True(Directory.Exists(running))
            Assert.False(Directory.Exists(newJob))
            Assert.True(DocHtmlJobCache.TotalBytes(cache) > 0)
        Finally
            If Directory.Exists(root) Then Directory.Delete(root, True)
        End Try
    End Sub

    <Fact>
    Public Sub SaveResultCopy_CopiesTheWholeTreeUnderAFreeName()
        Dim root = Path.Combine(Path.GetTempPath(), "fms-l0276-save-" & Guid.NewGuid().ToString("N"))
        Try
            Dim result = Path.Combine(root, "cache", "job", "book")
            Directory.CreateDirectory(Path.Combine(result, "images"))
            File.WriteAllText(Path.Combine(result, DocHtmlResultReader.EntryFileName), "<html><img src=""images/a.png""></html>")
            File.WriteAllText(Path.Combine(result, "images", "a.png"), "png")
            Dim target = Path.Combine(root, "saved")
            Directory.CreateDirectory(Path.Combine(target, "book"))
            File.WriteAllText(Path.Combine(target, "book", "mine.txt"), "user file")

            Dim first = DocHtmlJobCache.SaveResultCopy(result, target)
            Assert.Equal(Path.Combine(target, "book (2)"), first)
            Assert.True(File.Exists(Path.Combine(first, DocHtmlResultReader.EntryFileName)))
            Assert.Equal("png", File.ReadAllText(Path.Combine(first, "images", "a.png")))
            Assert.Equal("user file", File.ReadAllText(Path.Combine(target, "book", "mine.txt")))
            Assert.False(File.Exists(Path.Combine(target, "book", DocHtmlResultReader.EntryFileName)))

            Dim second = DocHtmlJobCache.SaveResultCopy(result, target)
            Assert.Equal(Path.Combine(target, "book (3)"), second)
            ' No temporary folder is left behind, and the copy survives the cache going away.
            Assert.Equal(3, Directory.GetDirectories(target).Length)
            Directory.Delete(Path.Combine(root, "cache"), True)
            Assert.True(File.Exists(Path.Combine(first, "images", "a.png")))
        Finally
            If Directory.Exists(root) Then Directory.Delete(root, True)
        End Try
    End Sub

    <Fact>
    Public Sub SaveResultCopy_RefusesAMissingResultOrDestination()
        Dim root = Path.Combine(Path.GetTempPath(), "fms-l0276-save-" & Guid.NewGuid().ToString("N"))
        Try
            Directory.CreateDirectory(Path.Combine(root, "book"))
            Assert.Throws(Of DirectoryNotFoundException)(Function() DocHtmlJobCache.SaveResultCopy(Path.Combine(root, "gone"), root))
            Assert.Throws(Of DirectoryNotFoundException)(Function() DocHtmlJobCache.SaveResultCopy(Path.Combine(root, "book"), Path.Combine(root, "nowhere")))
            Assert.Equal(Path.Combine(root, "book (2)"), DocHtmlJobCache.FreeFolderName(root, "book"))
            Assert.Equal(Path.Combine(root, "other"), DocHtmlJobCache.FreeFolderName(root, "other"))
        Finally
            If Directory.Exists(root) Then Directory.Delete(root, True)
        End Try
    End Sub

    Private Shared Function MakeJob(cache As String, name As String, ticket As DocHtmlJobTicket, sourceFile As String) As String
        Dim jobRoot = Path.Combine(cache, name)
        Dim output = Path.Combine(jobRoot, "book")
        Directory.CreateDirectory(output)
        File.WriteAllText(Path.Combine(output, DocHtmlResultReader.EntryFileName), "<html></html>")
        File.WriteAllText(Path.Combine(output, DocHtmlResultReader.RecordFileName),
            "{""tool"":""doc-html-translate"",""source"":" & System.Text.Json.JsonSerializer.Serialize(sourceFile) &
            ",""complete"":{""completed"":""2026-11-12T10:00:00Z"",""toolVersion"":""doc-html-translate 1.0"",""options"":{""engine"":""none"",""ocr"":false},""translation"":""none""}}")
        Assert.True(DocHtmlJobCache.WriteTicket(jobRoot, ticket))
        Return jobRoot
    End Function

    Private Shared Sub SetTreeTime(folder As String, utc As DateTime)
        For Each f In Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            File.SetLastWriteTimeUtc(f, utc)
        Next
        For Each d In Directory.GetDirectories(folder, "*", SearchOption.AllDirectories)
            Directory.SetLastWriteTimeUtc(d, utc)
        Next
        Directory.SetLastWriteTimeUtc(folder, utc)
    End Sub

    ' ---- Step 5.1: the paid mode ----

    <Fact>
    Public Sub PaidMode_ExpectsTheGoogleEngine()
        Assert.Equal("google", DocHtmlJobCache.ExpectedEngine(DocHtmlJobArguments.ModePaid))
        Assert.Equal("ollama", DocHtmlJobCache.ExpectedEngine(DocHtmlJobArguments.ModeLocal))
        Assert.Equal("none", DocHtmlJobCache.ExpectedEngine(DocHtmlJobArguments.ModeConvert))
    End Sub

    <Fact>
    Public Sub PaidMode_ReusesOnlyAFullGoogleTranslationOfTheSameLanguages()
        Dim o As New DocHtmlJobOptions With {.SourceLanguage = "en", .TargetLanguage = "uk", .PaidCostLimit = 5}
        Dim earlier = Request(DocHtmlJobArguments.ModePaid, o)
        Dim same = Request(DocHtmlJobArguments.ModePaid, New DocHtmlJobOptions With {.SourceLanguage = "en", .TargetLanguage = "uk", .PaidCostLimit = 20})
        Dim other = Request(DocHtmlJobArguments.ModePaid, New DocHtmlJobOptions With {.SourceLanguage = "en", .TargetLanguage = "de", .PaidCostLimit = 5})
        ' The cap is an approval, not a result-shaping choice: a higher cap reopens the same page.
        Assert.True(DocHtmlJobCache.IsReusable(earlier, Completed("google", "full"), same))
        Assert.False(DocHtmlJobCache.IsReusable(earlier, Completed("google", "partial"), same))
        Assert.False(DocHtmlJobCache.IsReusable(earlier, Completed("google", "none"), same))
        Assert.False(DocHtmlJobCache.IsReusable(earlier, Completed("ollama", "full"), same))
        Assert.False(DocHtmlJobCache.IsReusable(earlier, Completed("google", "full"), other))
    End Sub

    <Fact>
    Public Sub PaidAndLocal_AreDifferentRequests()
        Assert.False(DocHtmlJobCache.SameRequest(Request(DocHtmlJobArguments.ModePaid), Request(DocHtmlJobArguments.ModeLocal)))
    End Sub
End Class
#End If
