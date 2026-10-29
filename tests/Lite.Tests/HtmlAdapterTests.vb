#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

' Specification 275 H13/H16 and the section 11.3/13 pure halves: the EPUB, single-entry and
' converter adapters over the host source seam, the online page address rule and the browser
' resolution. Synthetic packages and a fake producer only; the joint 274/276 runs are owed
' separately. Modern leg only.
Public Class HtmlAdapterTests
    Implements IDisposable

    Private ReadOnly _temp As String = Path.Combine(Path.GetTempPath(), "fms-html-adapter-" & Guid.NewGuid().ToString("N"))

    Public Sub New()
        Directory.CreateDirectory(_temp)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Directory.Delete(_temp, True)
        Catch ex As IOException
        Catch ex As UnauthorizedAccessException
        End Try
    End Sub

    Private Function Put(relative As String, content As String) As String
        Dim target = Path.Combine(_temp, relative.Replace("/"c, "\"c))
        Directory.CreateDirectory(Path.GetDirectoryName(target))
        File.WriteAllText(target, content, New UTF8Encoding(False))
        Return target
    End Function

    Private Shared Function Epub(entries As Dictionary(Of String, String)) As MemoryStream
        Dim stream As New MemoryStream()
        Using zip As New ZipArchive(stream, ZipArchiveMode.Create, leaveOpen:=True)
            For Each item In entries
                Using writer As New StreamWriter(zip.CreateEntry(item.Key).Open(), New UTF8Encoding(False))
                    writer.Write(item.Value)
                End Using
            Next
        End Using
        stream.Position = 0
        Return stream
    End Function

    Private Shared Function SampleBook() As Dictionary(Of String, String)
        Return New Dictionary(Of String, String) From {
            {"META-INF/container.xml", "<container><rootfiles><rootfile full-path='OPS/book.opf' media-type='application/oebps-package+xml'/></rootfiles></container>"},
            {"OPS/book.opf", "<package xmlns:dc='http://purl.org/dc/elements/1.1/'><metadata><dc:title>Synthetic</dc:title></metadata><manifest>" &
                "<item id='c1' href='text/one.xhtml' media-type='application/xhtml+xml'/>" &
                "<item id='css' href='style/book.css' media-type='text/css'/>" &
                "<item id='img' href='images/cover.png' media-type='image/png'/>" &
                "<item id='js' href='scripts/run.js' media-type='application/javascript'/>" &
                "</manifest><spine><itemref idref='c1'/></spine></package>"},
            {"OPS/text/one.xhtml", "<html><head><link rel='stylesheet' href='../style/book.css'/></head><body><p id='start'>Chapter one</p></body></html>"},
            {"OPS/style/book.css", "p{color:#123}"},
            {"OPS/images/cover.png", "PNG"},
            {"OPS/scripts/run.js", "alert(1)"},
            {"OPS/undeclared.css", "p{}"}}
    End Function

    Private Shared Function EpubSource(stream As MemoryStream, Optional lease As IDisposable = Nothing) As PackageHtmlDocumentSource
        Dim book = EpubPackage.Read(stream)
        Return HtmlDocumentAdapters.FromEpubPackage(book, "OPS/text/one.xhtml", "C:\books\synthetic.epub",
            Function(key, token)
                SyncLock stream
                    stream.Position = 0
                    Return EpubPackage.ReadResource(stream, key)
                End SyncLock
            End Function, lease)
    End Function

    ' ---- H13: EPUB package ------------------------------------------------------------------

    <Fact>
    Public Async Function Epub_chapter_and_declared_assets_are_served_with_package_kinds() As Task
        Using stream = Epub(SampleBook()), source = EpubSource(stream)
            Assert.Equal(HtmlSourceKind.EpubPackage, source.Request.SourceKind)
            Assert.True(source.Request.IsReadOnly)
            Assert.StartsWith("epub:", source.Request.Provenance, StringComparison.Ordinal)
            Assert.Equal("Synthetic", source.Request.DisplayName)

            Dim chapter = Await source.ReadEntryAsync(CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, chapter.Status)
            Assert.Equal(HtmlResourceKind.Document, chapter.Kind)
            Assert.Contains("Chapter one", Encoding.UTF8.GetString(chapter.Bytes))
            Assert.NotNull(source.Request.SourceVersion)
            Assert.Equal(chapter.Bytes.LongLength, source.Request.SourceVersion.Length)
            Assert.Equal(64, source.Request.SourceVersion.Digest.Length)

            Dim css = Await source.ReadResourceAsync("OPS/style/book.css", CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, css.Status)
            Assert.Equal(HtmlResourceKind.Stylesheet, css.Kind)
            Assert.Equal("text/css", css.MimeType)
            Assert.Equal(HtmlResourceKind.Image, source.KindOf("OPS/images/cover.png"))
        End Using
    End Function

    <Fact>
    Public Async Function Epub_scripts_undeclared_entries_and_escapes_are_never_read() As Task
        Using stream = Epub(SampleBook()), source = EpubSource(stream)
            ' A script is not a servable kind, so it is not even part of the key set.
            Assert.Equal(HtmlOutcome.SourceMissing, source.ResolveKey("OPS/scripts/run.js").Status)
            Assert.Equal(HtmlOutcome.SourceMissing, (Await source.ReadResourceAsync("OPS/scripts/run.js", CancellationToken.None)).Status)
            ' Present in the ZIP but absent from the manifest: refused, nothing is listed from the archive.
            Assert.Equal(HtmlOutcome.SourceMissing, source.ResolveKey("OPS/undeclared.css").Status)
            Assert.False(source.ResolveKey("OPS/../META-INF/container.xml").IsAllowed)
            Assert.False(source.ResolveKey("/OPS/style/book.css").IsAllowed)
            ' Package keys are case-sensitive.
            Assert.Equal(HtmlOutcome.SourceMissing, source.ResolveKey("ops/style/book.css").Status)
        End Using
    End Function

    <Fact>
    Public Sub Epub_entry_that_is_not_a_declared_document_is_rejected()
        Using stream = Epub(SampleBook())
            Dim book = EpubPackage.Read(stream)
            Dim read As Func(Of String, CancellationToken, Byte()) = Function(key, token) New Byte() {}
            Assert.Throws(Of ArgumentException)(Function() HtmlDocumentAdapters.FromEpubPackage(book, "OPS/style/book.css", "", read))
            Assert.Throws(Of ArgumentException)(Function() HtmlDocumentAdapters.FromEpubPackage(book, "OPS/missing.xhtml", "", read))
        End Using
    End Sub

    <Theory>
    <InlineData("application/xhtml+xml", HtmlResourceKind.Document)>
    <InlineData("TEXT/HTML", HtmlResourceKind.Document)>
    <InlineData("text/css", HtmlResourceKind.Stylesheet)>
    <InlineData("image/svg+xml", HtmlResourceKind.Image)>
    <InlineData("font/woff2", HtmlResourceKind.Font)>
    <InlineData("application/vnd.ms-opentype", HtmlResourceKind.Font)>
    <InlineData("application/javascript", HtmlResourceKind.Other)>
    <InlineData("application/x-dtbncx+xml", HtmlResourceKind.Other)>
    <InlineData("", HtmlResourceKind.Other)>
    Public Sub Epub_media_types_map_to_servable_kinds_only(mediaType As String, expected As HtmlResourceKind)
        Assert.Equal(expected, HtmlDocumentAdapters.KindOfMediaType(mediaType))
    End Sub

    ' ---- the package source's failure mapping and lifetime -----------------------------------------

    Private NotInheritable Class Lease
        Implements IDisposable
        Public Released As Integer
        Public Sub Dispose() Implements IDisposable.Dispose
            Released += 1
        End Sub
    End Class

    Private Shared Function PackageOf(read As Func(Of String, CancellationToken, Byte()), Optional lease As IDisposable = Nothing) As PackageHtmlDocumentSource
        Dim entries As New Dictionary(Of String, HtmlPackageEntry) From {
            {"index.html", New HtmlPackageEntry(HtmlResourceKind.Document, "text/html")},
            {"a.css", New HtmlPackageEntry(HtmlResourceKind.Stylesheet, "text/css")},
            {"blob.bin", New HtmlPackageEntry(HtmlResourceKind.Other, "")}}
        Dim request As New HtmlOpenRequest With {.SourceKind = HtmlSourceKind.ArchiveSnapshot, .EntryResourceKey = "index.html", .IsReadOnly = False}
        Return New PackageHtmlDocumentSource(request, entries, read, lease)
    End Function

    Public Shared Iterator Function ReadFailures() As IEnumerable(Of Object())
        Yield New Object() {New UnauthorizedAccessException(), HtmlOutcome.AccessDenied}
        Yield New Object() {New NotSupportedException(), HtmlOutcome.UnsupportedResource}
        Yield New Object() {New InvalidDataException(), HtmlOutcome.InvalidMarkup}
        Yield New Object() {New FileNotFoundException(), HtmlOutcome.SourceMissing}
        Yield New Object() {New IOException(), HtmlOutcome.SourceMissing}
    End Function

    <Theory>
    <MemberData(NameOf(ReadFailures))>
    Public Async Function Package_read_failures_become_typed_outcomes(failure As Exception, expected As HtmlOutcome) As Task
        Using source = PackageOf(Function(key, token) As Byte()
                                     Throw failure
                                     Return Nothing
                                 End Function)
            Assert.Equal(expected, (Await source.ReadEntryAsync(CancellationToken.None)).Status)
        End Using
    End Function

    <Fact>
    Public Async Function Package_limits_other_kinds_and_forces_read_only() As Task
        Using source = PackageOf(Function(key, token) If(key = "a.css", New Byte(CInt(HtmlDocumentPolicy.MaxBytesFor(HtmlResourceKind.Stylesheet))) {}, Encoding.UTF8.GetBytes("<p>x</p>")))
            Assert.True(source.Request.IsReadOnly)
            Assert.Equal(HtmlOutcome.Ok, (Await source.ReadEntryAsync(CancellationToken.None)).Status)
            Assert.Equal(HtmlOutcome.LimitExceeded, (Await source.ReadResourceAsync("a.css", CancellationToken.None)).Status)
            Assert.Equal(HtmlOutcome.UnsupportedResource, (Await source.ReadResourceAsync("blob.bin", CancellationToken.None)).Status)
        End Using
    End Function

    <Fact>
    Public Async Function Package_dispose_cancels_reads_and_releases_the_lease_once() As Task
        Dim lease As New Lease()
        Dim source = PackageOf(Function(key, token) Encoding.UTF8.GetBytes("<p>x</p>"), lease)
        source.Dispose()
        source.Dispose()
        Assert.Equal(1, lease.Released)
        Assert.Equal(HtmlOutcome.Cancelled, source.ResolveKey("a.css").Status)
        Assert.Equal(HtmlOutcome.Cancelled, (Await source.ReadEntryAsync(CancellationToken.None)).Status)
    End Function

    <Fact>
    Public Async Function Package_cancelled_token_answers_cancelled() As Task
        Using source = PackageOf(Function(key, token)
                                     token.ThrowIfCancellationRequested()
                                     Return New Byte() {1}
                                 End Function)
            Assert.Equal(HtmlOutcome.Cancelled, (Await source.ReadEntryAsync(New CancellationToken(True))).Status)
        End Using
    End Function

    ' ---- H16: a materialized archive/resource entry never exposes its siblings ---------------------------

    <Fact>
    Public Async Function Single_entry_serves_only_that_file_not_the_materialization_folder() As Task
        Dim entry = Put("cache/page #1.html", "<html><body><img src='secret.png'><link rel=stylesheet href='site.css'></body></html>")
        Put("cache/site.css", "p{}")
        Put("cache/secret.png", "PNG")
        Put("other-entry.html", "<p>other</p>")
        Using source = HtmlDocumentAdapters.SingleEntry(entry, HtmlSourceKind.ArchiveSnapshot, "archive:x|page.html", "x.zip|page.html")
            Assert.Equal(HtmlSourceKind.ArchiveSnapshot, source.Request.SourceKind)
            Assert.Equal("x.zip|page.html", source.Request.OriginalLocator)
            Dim result = Await source.ReadEntryAsync(CancellationToken.None)
            Assert.Equal(HtmlOutcome.Ok, result.Status)
            Assert.Contains("secret.png", Encoding.UTF8.GetString(result.Bytes))
            Assert.Equal(HtmlOutcome.SourceMissing, source.ResolveKey("site.css").Status)
            Assert.Equal(HtmlOutcome.SourceMissing, (Await source.ReadResourceAsync("secret.png", CancellationToken.None)).Status)
            Assert.False(source.ResolveKey("../other-entry.html").IsAllowed)
        End Using
    End Function

    <Fact>
    Public Async Function Single_entry_that_vanished_is_source_missing() As Task
        Dim entry = Put("gone.html", "<p>x</p>")
        Using source = HtmlDocumentAdapters.SingleEntry(entry, HtmlSourceKind.ResourceSnapshot, "resource:v1", Nothing)
            File.Delete(entry)
            Assert.Equal(HtmlOutcome.SourceMissing, (Await source.ReadEntryAsync(CancellationToken.None)).Status)
        End Using
    End Function

    <Fact>
    Public Async Function Online_source_reads_nothing_and_resolves_no_local_key() As Task
        Using source As New OnlineHtmlDocumentSource(New Uri("https://example.org/a/page.html?q=1#frag"))
            Assert.Equal(HtmlSourceKind.OnlinePage, source.Request.SourceKind)
            Assert.Equal("online:https://example.org", source.Request.Provenance)
            Assert.DoesNotContain("#", source.Request.EntryResourceKey)
            Assert.Equal(HtmlOutcome.NetworkBlocked, (Await source.ReadEntryAsync(CancellationToken.None)).Status)
            Assert.Equal(HtmlOutcome.NetworkBlocked, (Await source.ReadResourceAsync("page.html", CancellationToken.None)).Status)
            Assert.False(source.ResolveKey("page.html").IsAllowed)
        End Using
    End Function

    ' ---- H13: converter output only through producer verification ------------------------------------

    Private NotInheritable Class FakeProducer
        Implements IHtmlProducerVerification
        Public Answer As HtmlOutcome = HtmlOutcome.Ok
        Public Root As String = ""
        Public Entry As String = ""
        Public Failure As Exception
        Public Calls As Integer

        Public Function Verify(jobId As String, ByRef outputRoot As String, ByRef entryPath As String) As HtmlOutcome Implements IHtmlProducerVerification.Verify
            Calls += 1
            If Failure IsNot Nothing Then Throw Failure
            outputRoot = Root
            entryPath = Entry
            Return Answer
        End Function
    End Class

    <Fact>
    Public Sub Converter_output_opens_only_after_verification_with_converter_provenance()
        Dim entry = Put("job-7/out/index.html", "<html><body>converted</body></html>")
        Dim producer As New FakeProducer With {.Root = Path.Combine(_temp, "job-7", "out"), .Entry = entry}
        Dim source As IHtmlDocumentSource = Nothing
        Assert.Equal(HtmlOutcome.Ok, HtmlDocumentAdapters.OpenConverterOutput(producer, "job-7", 32L * 1024 * 1024, source))
        Using source
            Assert.Equal(HtmlSourceKind.ConverterOutput, source.Request.SourceKind)
            Assert.Equal("converter:job-7", source.Request.Provenance)
            Assert.False(source.Request.IsSecure)
        End Using
    End Sub

    <Fact>
    Public Sub Converter_output_is_not_opened_without_a_verified_root()
        Dim entry = Put("job-8/out/index.html", "<p>x</p>")
        Dim source As IHtmlDocumentSource = Nothing

        Dim refused As New FakeProducer With {.Answer = HtmlOutcome.AccessDenied, .Root = Path.GetDirectoryName(entry), .Entry = entry}
        Assert.Equal(HtmlOutcome.AccessDenied, HtmlDocumentAdapters.OpenConverterOutput(refused, "job-8", 1, source))
        Assert.Null(source)

        Dim empty As New FakeProducer With {.Root = "", .Entry = entry}
        Assert.Equal(HtmlOutcome.UnsupportedResource, HtmlDocumentAdapters.OpenConverterOutput(empty, "job-8", 1, source))
        Assert.Null(source)

        Dim throwing As New FakeProducer With {.Failure = New IOException()}
        Assert.Equal(HtmlOutcome.UnsupportedResource, HtmlDocumentAdapters.OpenConverterOutput(throwing, "job-8", 1, source))
        Assert.Null(source)

        Dim unused As New FakeProducer()
        Assert.Equal(HtmlOutcome.UnsupportedResource, HtmlDocumentAdapters.OpenConverterOutput(unused, " ", 1, source))
        Assert.Equal(0, unused.Calls)
        Assert.Equal(HtmlOutcome.UnsupportedResource, HtmlDocumentAdapters.OpenConverterOutput(Nothing, "job-8", 1, source))
    End Sub

    <Fact>
    Public Sub Converter_entry_outside_the_verified_root_is_refused()
        Put("job-9/out/index.html", "<p>inside</p>")
        Dim outside = Put("job-9/elsewhere.html", "<p>outside</p>")
        Dim producer As New FakeProducer With {.Root = Path.Combine(_temp, "job-9", "out"), .Entry = outside}
        Dim source As IHtmlDocumentSource = Nothing
        Assert.NotEqual(HtmlOutcome.Ok, HtmlDocumentAdapters.OpenConverterOutput(producer, "job-9", 1024 * 1024, source))
        Assert.Null(source)
    End Sub

    ' ---- section 13: the typed address of an online page --------------------------------------------

    <Theory>
    <InlineData("https://example.org/page.html")>
    <InlineData("  https://example.org/a/b?x=1#top  ")>
    <InlineData("http://example.org/")>
    <InlineData("https://xn--e1afmkfd.xn--p1ai/")>
    <InlineData("https://93.184.216.34/")>
    Public Sub Online_page_address_accepts_public_http_and_https(text As String)
        Dim page As Uri = Nothing
        Assert.True(HtmlDocumentAdapters.TryParseOnlinePage(text, page))
        Assert.NotNull(page)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("example.org/page.html")>
    <InlineData("ftp://example.org/")>
    <InlineData("file:///C:/page.html")>
    <InlineData("javascript:alert(1)")>
    <InlineData("https://user:pass@example.org/")>
    <InlineData("https://example.org:8443/")>
    <InlineData("http://example.org:443/")>
    <InlineData("https://localhost/")>
    <InlineData("https://intranet/")>
    <InlineData("https://printer.local/")>
    <InlineData("https://127.0.0.1/")>
    <InlineData("https://192.168.1.10/")>
    <InlineData("https://10.0.0.1/")>
    <InlineData("https://[::1]/")>
    <InlineData("https://[::ffff:127.0.0.1]/")>
    <InlineData("https://169.254.169.254/latest/meta-data")>
    Public Sub Online_page_address_refuses_other_schemes_credentials_ports_and_private_targets(text As String)
        Dim page As Uri = Nothing
        Assert.False(HtmlDocumentAdapters.TryParseOnlinePage(text, page))
        Assert.Null(page)
    End Sub

    <Fact>
    Public Sub Online_page_address_has_a_length_bound()
        Dim page As Uri = Nothing
        Dim longPath = "https://example.org/" & New String("a"c, HtmlDocumentAdapters.MaxPageAddressLength)
        Assert.False(HtmlDocumentAdapters.TryParseOnlinePage(longPath, page))
    End Sub

    ' ---- section 11.3: the browser handoff ---------------------------------------------------------

    <Fact>
    Public Sub Browser_resolution_takes_only_an_existing_foreign_executable()
        Dim browser = Put("Browser\browser.exe", "MZ")
        Dim ownMain = Put("Own\FastMediaSorter_LITE.exe", "MZ")
        Dim ownFallback = Put("Own\fastmediasorter_x86.EXE", "MZ")
        Dim script = Put("Browser\browser.cmd", "@echo")

        Assert.Equal(browser, HtmlBrowserLauncher.UsableBrowser(browser))
        Assert.Equal(browser, HtmlBrowserLauncher.UsableBrowser("""" & browser & """"))
        Assert.Equal("", HtmlBrowserLauncher.UsableBrowser(ownMain))
        Assert.Equal("", HtmlBrowserLauncher.UsableBrowser(ownFallback))
        Assert.Equal("", HtmlBrowserLauncher.UsableBrowser(script))
        Assert.Equal("", HtmlBrowserLauncher.UsableBrowser(Path.Combine(_temp, "Browser", "missing.exe")))
        Assert.Equal("", HtmlBrowserLauncher.UsableBrowser("browser.exe"))
        Assert.Equal("", HtmlBrowserLauncher.UsableBrowser(""))
        Assert.Equal("", HtmlBrowserLauncher.UsableBrowser(Environment.ProcessPath))
    End Sub

    <Fact>
    Public Sub Browser_gets_a_file_uri_with_real_path_characters_escaped()
        Dim local = Put("dir #1\100% page.html", "<p>x</p>")
        Dim uri = HtmlBrowserLauncher.FileUriOf(local)
        Assert.StartsWith("file:///", uri, StringComparison.Ordinal)
        Assert.Contains("%23", uri)
        Assert.Contains("%25", uri)
        Assert.DoesNotContain("#", uri)
        Assert.Equal(Path.GetFullPath(local), New Uri(uri).LocalPath)
    End Sub
End Class
#End If
