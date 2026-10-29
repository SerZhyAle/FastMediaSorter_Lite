#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

' Specification 275 H03/H04: the local-file adapter and the handle-based file access on a
' disposable temp tree. Links are real NTFS junctions (mklink /J needs no elevation) and the
' case-sensitive directories real fsutil flags; a test that cannot create its fixture says so
' in a comment where it returns early. Modern leg only.
Public Class HtmlLocalSourceTests
    Implements IDisposable

    Private ReadOnly _temp As String
    Private ReadOnly _links As New List(Of String)()
    Private ReadOnly _sources As New List(Of LocalHtmlDocumentSource)()

    Public Sub New()
        _temp = Path.Combine(Path.GetTempPath(), "fms-html-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(_temp)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each source As LocalHtmlDocumentSource In _sources
            source.Dispose()
        Next
        ' Junctions go first so the recursive delete can never follow one out of the tree.
        For Each link As String In _links
            Try
                Directory.Delete(link, False)
            Catch ex As IOException
            Catch ex As UnauthorizedAccessException
            End Try
        Next
        Try
            Directory.Delete(_temp, True)
        Catch ex As IOException
        Catch ex As UnauthorizedAccessException
        End Try
    End Sub

    ' ---- fixture helpers ------------------------------------------------------------------

    Private Function PathOf(relative As String) As String
        Return Path.Combine(_temp, relative.Replace("/"c, "\"c))
    End Function

    Private Function Put(relative As String, content As String) As String
        Return PutBytes(relative, New UTF8Encoding(False).GetBytes(content))
    End Function

    Private Function PutBytes(relative As String, bytes As Byte()) As String
        Dim full As String = PathOf(relative)
        Directory.CreateDirectory(Path.GetDirectoryName(full))
        File.WriteAllBytes(full, bytes)
        Return full
    End Function

    Private Shared Function RunTool(fileName As String, arguments As String) As Integer
        Dim info As New ProcessStartInfo(fileName, arguments) With {
            .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True}
        Using process As Process = Process.Start(info)
            process.StandardOutput.ReadToEnd()
            process.StandardError.ReadToEnd()
            process.WaitForExit()
            Return process.ExitCode
        End Using
    End Function

    Private Sub MakeJunction(link As String, target As String)
        Directory.CreateDirectory(Path.GetDirectoryName(link))
        Assert.Equal(0, RunTool("cmd.exe", "/c mklink /J """ & link & """ """ & target & """"))
        _links.Add(link)
    End Sub

    Private Function TryCaseSensitive(directory As String) As Boolean
        Try
            Return RunTool("fsutil.exe", "file setCaseSensitiveInfo """ & directory & """ enable") = 0
        Catch ex As System.ComponentModel.Win32Exception
            Return False
        End Try
    End Function

    Private Function OpenOk(entryRelative As String, Optional rootRelative As String = Nothing, Optional cacheBytes As Long = 1048576L) As LocalHtmlDocumentSource
        Dim source As LocalHtmlDocumentSource = Nothing
        Dim root As String = If(rootRelative Is Nothing, Nothing, PathOf(rootRelative))
        Dim status As HtmlOutcome = LocalHtmlDocumentSource.Open(PathOf(entryRelative), root, False, cacheBytes, source)
        Assert.Equal(HtmlOutcome.Ok, status)
        Assert.NotNull(source)
        _sources.Add(source)
        Return source
    End Function

    Private Function OpenStatus(entryRelative As String, Optional rootRelative As String = Nothing) As HtmlOutcome
        Dim source As LocalHtmlDocumentSource = Nothing
        Dim root As String = If(rootRelative Is Nothing, Nothing, PathOf(rootRelative))
        Dim status As HtmlOutcome = LocalHtmlDocumentSource.Open(PathOf(entryRelative), root, False, 0L, source)
        If source IsNot Nothing Then
            _sources.Add(source)
            Assert.Equal(HtmlOutcome.Ok, status)
        Else
            Assert.NotEqual(HtmlOutcome.Ok, status)
        End If
        Return status
    End Function

    Private Shared Function Text(result As HtmlResourceResult) As String
        Return Encoding.UTF8.GetString(result.Bytes)
    End Function

    Private Shared Function Cyrillic() As String
        Return ChrW(&H434) & ChrW(&H43E) & ChrW(&H43A) & ChrW(&H443) & ChrW(&H43C) & ChrW(&H435) & ChrW(&H43D) & ChrW(&H442)
    End Function

    ' ---- names and request description ----------------------------------------------------

    <Fact>
    Public Async Function Unicode_and_special_character_file_names_are_read_exactly() As Task
        Dim entryName As String = Cyrillic() & " #1 (a&b) 100%.html"
        Dim japanese As String = ChrW(&H65E5) & ChrW(&H672C)
        Put("site/" & entryName, "<p>entry</p>")
        Put("site/css/style #1 & 100%.css", "p{color:red}")
        Put("site/" & japanese & "/" & Cyrillic() & ".png", "png")
        Put("site/literal%2e%2e.svg", "<svg/>")
        Dim source As LocalHtmlDocumentSource = OpenOk("site/" & entryName)
        Assert.Equal(entryName, source.Request.EntryResourceKey)
        Assert.Equal(entryName, source.Request.DisplayName)

        Dim entry As HtmlResourceResult = Await source.ReadEntryAsync(CancellationToken.None)
        Assert.True(entry.IsOk, entry.Status.ToString())
        Assert.Equal("<p>entry</p>", Text(entry))
        Assert.Equal(HtmlResourceKind.Document, entry.Kind)
        Assert.Equal("text/html; charset=utf-8", entry.MimeType)

        Dim keys As String() = {"css/style #1 & 100%.css", japanese & "/" & Cyrillic() & ".png", "literal%2e%2e.svg"}
        Dim kinds As HtmlResourceKind() = {HtmlResourceKind.Stylesheet, HtmlResourceKind.Image, HtmlResourceKind.Image}
        For i As Integer = 0 To keys.Length - 1
            ' The whole request path: escape, parse the raw request text, read.
            Dim origin As Uri = HtmlDocumentPolicy.NewSessionOrigin()
            Dim parsed As HtmlKeyResolution = HtmlDocumentPolicy.ParseRequestKey(HtmlDocumentPolicy.EntryUrl(origin, keys(i)), origin, "GET")
            Assert.True(parsed.IsAllowed, keys(i))
            Assert.Equal(keys(i), parsed.Key)
            Dim result As HtmlResourceResult = Await source.ReadResourceAsync(parsed.Key, CancellationToken.None)
            Assert.True(result.IsOk, keys(i) & " -> " & result.Status.ToString())
            Assert.Equal(kinds(i), result.Kind)
        Next
    End Function

    <Fact>
    Public Async Function Open_describes_the_request_and_the_entry_read_fills_the_version() As Task
        Dim entryPath As String = Put("doc/index.html", "<p>x</p>")
        Dim source As LocalHtmlDocumentSource = OpenOk("doc/index.html")
        Dim request As HtmlOpenRequest = source.Request
        Assert.Equal(HtmlSourceKind.LocalFile, request.SourceKind)
        Assert.Matches("^[0-9a-f]{32}$", request.SourceId)
        Assert.Equal("index.html", request.DisplayName)
        Assert.Equal(entryPath, request.OriginalLocator)
        Assert.Equal("index.html", request.EntryResourceKey)
        Assert.True(request.IsReadOnly)
        Assert.False(request.IsSecure)
        Assert.EndsWith("\doc", source.RootPath, StringComparison.OrdinalIgnoreCase)
        Assert.Equal(source.RootPath, request.RootDescriptor)
        Assert.Null(request.SourceVersion)

        Dim entry As HtmlResourceResult = Await source.ReadEntryAsync(CancellationToken.None)
        Assert.True(entry.IsOk)
        Assert.NotNull(request.SourceVersion)
        Assert.Equal(8L, request.SourceVersion.Length)
        Assert.Equal(File.GetLastWriteTimeUtc(entryPath).Ticks, request.SourceVersion.LastWriteUtcTicks)
        Dim expected As String = BitConverter.ToString(SHA256.HashData(New UTF8Encoding(False).GetBytes("<p>x</p>"))).Replace("-", "").ToLowerInvariant()
        Assert.Equal(expected, request.SourceVersion.Digest)
        Assert.True(request.SourceVersion.Matches(entry.ResourceVersion))
        Assert.Equal(source.LocalPathOf("index.html"), Path.Combine(source.RootPath, "index.html"))
        Assert.Equal("", source.LocalPathOf("../index.html"))
    End Function

    <Fact>
    Public Async Function Default_root_is_the_entry_directory_and_its_parents_are_unreachable() As Task
        Put("top.css", "top")
        Put("doc/index.html", "e")
        Put("doc/sub/a.css", "a")
        Dim source As LocalHtmlDocumentSource = OpenOk("doc/index.html")
        Assert.Equal("index.html", source.Request.EntryResourceKey)
        Assert.Equal("a", Text(Await source.ReadResourceAsync("sub/a.css", CancellationToken.None)))
        Dim outside As HtmlResourceResult = Await source.ReadResourceAsync("../top.css", CancellationToken.None)
        Assert.Equal(HtmlOutcome.RootEscape, outside.Status)
        Assert.Empty(outside.Bytes)
        Assert.Equal(HtmlOutcome.RootEscape, source.ResolveKey("../top.css").Status)
    End Function

    <Fact>
    Public Async Function An_explicit_root_that_contains_the_entry_is_used_and_the_key_is_root_relative() As Task
        Put("site/pages/index.html", "e")
        Put("site/assets/a.css", "a")
        Dim source As LocalHtmlDocumentSource = OpenOk("site/pages/index.html", "site")
        Assert.Equal("pages/index.html", source.Request.EntryResourceKey)
        Assert.EndsWith("\site", source.RootPath, StringComparison.OrdinalIgnoreCase)
        Assert.Equal("a", Text(Await source.ReadResourceAsync("assets/a.css", CancellationToken.None)))
        Assert.True((Await source.ReadEntryAsync(CancellationToken.None)).IsOk)
    End Function

    <Fact>
    Public Sub The_entry_key_has_the_on_disk_case_whatever_spelling_the_caller_used()
        Put("Site/Pages/Index.html", "e")
        Dim source As LocalHtmlDocumentSource = Nothing
        Dim status As HtmlOutcome = LocalHtmlDocumentSource.Open(PathOf("SITE/PAGES/INDEX.HTML"), PathOf("site"), False, 0L, source)
        Assert.Equal(HtmlOutcome.Ok, status)
        _sources.Add(source)
        Assert.Equal("Pages/Index.html", source.Request.EntryResourceKey)
    End Sub

    <Fact>
    Public Sub An_entry_outside_the_explicit_root_is_a_root_escape()
        Put("root/a.html", "a")
        Put("other/b.html", "b")
        Assert.Equal(HtmlOutcome.RootEscape, OpenStatus("other/b.html", "root"))
    End Sub

    <Fact>
    Public Sub A_directory_is_not_a_document_and_a_file_is_not_a_root()
        Put("d/index.html", "e")
        Directory.CreateDirectory(PathOf("d/folder.html"))
        Assert.Equal(HtmlOutcome.UnsupportedResource, OpenStatus("d/folder.html"))
        Assert.Equal(HtmlOutcome.UnsupportedResource, OpenStatus("d/index.html", "d/index.html"))
    End Sub

    <Fact>
    Public Sub Secure_container_is_refused_before_any_filesystem_work()
        Dim source As LocalHtmlDocumentSource = Nothing
        ' Neither path exists: only a check that runs first can answer SecureRefused.
        Assert.Equal(HtmlOutcome.SecureRefused, LocalHtmlDocumentSource.Open("Z:\no\such\file.html", Nothing, True, 0L, source))
        Assert.Null(source)
        Put("d/index.html", "e")
        Assert.Equal(HtmlOutcome.SecureRefused, LocalHtmlDocumentSource.Open(PathOf("d/index.html"), PathOf("d"), True, 1L, source))
        Assert.Null(source)
    End Sub

    ' ---- root containment -----------------------------------------------------------------

    <Fact>
    Public Sub IsUnderRoot_requires_a_separator_boundary()
        Assert.True(HtmlFileAccess.IsUnderRoot("C:\x\doc", "C:\x\doc\b.html"))
        Assert.True(HtmlFileAccess.IsUnderRoot("C:\x\doc", "C:\x\DOC\sub\b.html"))
        Assert.False(HtmlFileAccess.IsUnderRoot("C:\x\doc", "C:\x\doc2\b.html"))
        Assert.False(HtmlFileAccess.IsUnderRoot("C:\x\doc", "C:\x\doc"))
        Assert.False(HtmlFileAccess.IsUnderRoot("C:\x\doc", "C:\x"))
        Assert.True(HtmlFileAccess.IsUnderRoot("C:\", "C:\a.html"))
        Assert.True(HtmlFileAccess.IsUnderRoot("\\server\share", "\\server\share\a.html"))
        Assert.False(HtmlFileAccess.IsUnderRoot("\\server\share", "\\server\share2\a.html"))
        Assert.False(HtmlFileAccess.IsUnderRoot("", "C:\a.html"))
        Assert.False(HtmlFileAccess.IsUnderRoot("C:\x", Nothing))
    End Sub

    <Fact>
    Public Sub A_sibling_directory_sharing_the_root_name_prefix_is_not_inside_the_root()
        Put("doc/a.html", "a")
        Put("doc2/b.html", "b")
        Assert.Equal(HtmlOutcome.RootEscape, OpenStatus("doc2/b.html", "doc"))
        Assert.Equal(HtmlOutcome.Ok, OpenStatus("doc/a.html", "doc"))
    End Sub

    <Fact>
    Public Sub The_handle_check_rejects_a_final_path_in_a_sibling_that_shares_the_root_prefix()
        ' ...\doc is a junction whose target ...\doc2 starts with the same text: a string-prefix
        ' check on the opened file's final path would accept it, the separator boundary does not.
        Put("doc2/x.css", "secret")
        MakeJunction(PathOf("doc"), PathOf("doc2"))
        Dim read As HtmlFileRead = HtmlFileAccess.ReadUnderRoot(PathOf("doc"), "x.css", 1024L, CancellationToken.None)
        Assert.Equal(HtmlOutcome.RootEscape, read.Status)
        Assert.Empty(read.Bytes)
        Assert.Null(read.Version)
    End Sub

    <Fact>
    Public Sub A_link_swapped_in_after_the_preliminary_check_fails_the_handle_check()
        ' The root text itself leads through a junction, so no directory component below it can
        ' trip the preliminary check - exactly the state of a swap between check and open.
        Put("outside/secret.css", "secret")
        Put("real/x.css", "real")
        MakeJunction(PathOf("swapped"), PathOf("outside"))
        Dim read As HtmlFileRead = HtmlFileAccess.ReadUnderRoot(PathOf("swapped"), "secret.css", 1024L, CancellationToken.None)
        Assert.Equal(HtmlOutcome.RootEscape, read.Status)
        Assert.Empty(read.Bytes)

        Dim canonical As String = ""
        Assert.Equal(HtmlOutcome.Ok, HtmlFileAccess.CanonicalRoot(PathOf("real"), canonical))
        Dim control As HtmlFileRead = HtmlFileAccess.ReadUnderRoot(canonical, "x.css", 1024L, CancellationToken.None)
        Assert.Equal(HtmlOutcome.Ok, control.Status)
        Assert.Equal("real", Encoding.UTF8.GetString(control.Bytes))
    End Sub

    <Fact>
    Public Async Function A_junction_inside_the_root_pointing_outside_is_refused_for_its_children() As Task
        Put("outside/secret.css", "secret")
        Put("root/index.html", "e")
        Put("root/ok.css", "ok")
        MakeJunction(PathOf("root/link"), PathOf("outside"))
        Directory.CreateDirectory(PathOf("root/a"))
        MakeJunction(PathOf("root/a/deeper"), PathOf("outside"))

        Dim source As LocalHtmlDocumentSource = OpenOk("root/index.html")
        For Each key As String In {"link/secret.css", "a/deeper/secret.css"}
            Dim result As HtmlResourceResult = Await source.ReadResourceAsync(key, CancellationToken.None)
            Assert.Equal(HtmlOutcome.RootEscape, result.Status)
            Assert.Empty(result.Bytes)
        Next
        Assert.Equal("ok", Text(Await source.ReadResourceAsync("ok.css", CancellationToken.None)))
    End Function

    <Fact>
    Public Sub A_junction_as_the_root_is_refused()
        Put("real/index.html", "e")
        MakeJunction(PathOf("viaLink"), PathOf("real"))
        Dim canonical As String = "unchanged"
        Assert.Equal(HtmlOutcome.RootEscape, HtmlFileAccess.CanonicalRoot(PathOf("viaLink"), canonical))
        Assert.Equal("", canonical)
        ' The default root is the entry's directory: the junction itself.
        Assert.Equal(HtmlOutcome.RootEscape, OpenStatus("viaLink/index.html"))
        Assert.Equal(HtmlOutcome.RootEscape, OpenStatus("viaLink/index.html", "viaLink"))
        ' The same file reached by its real root is fine; the junction was only another spelling.
        Assert.Equal(HtmlOutcome.Ok, OpenStatus("viaLink/index.html", "real"))
    End Sub

    ' ---- case-sensitive trees -------------------------------------------------------------

    <Fact>
    Public Async Function A_case_sensitive_directory_tree_is_refused_as_root_and_below_the_root() As Task
        Put("root/index.html", "e")
        Put("root/ok.css", "ok")
        Directory.CreateDirectory(PathOf("root/cs"))
        Directory.CreateDirectory(PathOf("cs-root"))
        ' The flag needs an empty directory; where the system refuses to set it the fixture cannot
        ' exist and this case is unproven on that machine (it ran on the development machine).
        If Not TryCaseSensitive(PathOf("root/cs")) OrElse Not TryCaseSensitive(PathOf("cs-root")) Then Return
        Put("root/cs/a.css", "a")
        Put("cs-root/index.html", "e")

        Dim source As LocalHtmlDocumentSource = OpenOk("root/index.html")
        Dim below As HtmlResourceResult = Await source.ReadResourceAsync("cs/a.css", CancellationToken.None)
        Assert.Equal(HtmlOutcome.UnsupportedResource, below.Status)
        Assert.Empty(below.Bytes)
        Assert.Equal("ok", Text(Await source.ReadResourceAsync("ok.css", CancellationToken.None)))

        Dim canonical As String = ""
        Assert.Equal(HtmlOutcome.UnsupportedResource, HtmlFileAccess.CanonicalRoot(PathOf("cs-root"), canonical))
        Assert.Equal(HtmlOutcome.UnsupportedResource, OpenStatus("cs-root/index.html"))
    End Function

    ' ---- version, change, lock, missing ---------------------------------------------------

    <Fact>
    Public Async Function A_replaced_entry_is_reported_as_changed_and_an_explicit_reload_accepts_it() As Task
        Dim entryPath As String = Put("d/index.html", "<p>one</p>")
        Put("d/a.css", "a")
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html")
        Dim first As HtmlResourceResult = Await source.ReadEntryAsync(CancellationToken.None)
        Dim firstDigest As String = source.Request.SourceVersion.Digest

        Dim same As HtmlResourceResult = Await source.ReadResourceAsync("index.html", CancellationToken.None)
        Assert.True(same.IsOk, same.Status.ToString())
        Assert.Equal(Text(first), Text(same))

        File.WriteAllText(entryPath, "<p>two, and longer</p>")
        Dim changed As HtmlResourceResult = Await source.ReadResourceAsync("index.html", CancellationToken.None)
        Assert.Equal(HtmlOutcome.SourceChanged, changed.Status)
        Assert.Empty(changed.Bytes)
        Assert.Equal("a", Text(Await source.ReadResourceAsync("a.css", CancellationToken.None)))

        Dim reload As HtmlResourceResult = Await source.ReadEntryAsync(CancellationToken.None)
        Assert.True(reload.IsOk)
        Assert.Equal("<p>two, and longer</p>", Text(reload))
        Assert.NotEqual(firstDigest, source.Request.SourceVersion.Digest)
        Assert.True((Await source.ReadResourceAsync("index.html", CancellationToken.None)).IsOk)
    End Function

    <Fact>
    Public Async Function A_same_length_same_time_replacement_is_still_caught_by_the_digest() As Task
        Dim entryPath As String = Put("d/index.html", "<p>one</p>")
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html")
        Await source.ReadEntryAsync(CancellationToken.None)
        Dim stamp As DateTime = File.GetLastWriteTimeUtc(entryPath)
        File.WriteAllText(entryPath, "<p>two</p>")
        File.SetLastWriteTimeUtc(entryPath, stamp)
        Assert.Equal(HtmlOutcome.SourceChanged, (Await source.ReadResourceAsync("index.html", CancellationToken.None)).Status)
    End Function

    <Fact>
    Public Async Function A_file_held_exclusively_by_another_program_is_access_denied_and_readable_again_afterwards() As Task
        Dim entryPath As String = Put("d/index.html", "e")
        Put("d/locked.css", "c")
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html")
        Using hold As New FileStream(PathOf("d/locked.css"), FileMode.Open, FileAccess.Read, FileShare.None)
            Dim result As HtmlResourceResult = Await source.ReadResourceAsync("locked.css", CancellationToken.None)
            Assert.Equal(HtmlOutcome.AccessDenied, result.Status)
            Assert.Empty(result.Bytes)
        End Using
        Assert.Equal("c", Text(Await source.ReadResourceAsync("locked.css", CancellationToken.None)))

        Using hold As New FileStream(entryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            Assert.Equal(HtmlOutcome.AccessDenied, (Await source.ReadEntryAsync(CancellationToken.None)).Status)
        End Using
        Assert.True((Await source.ReadEntryAsync(CancellationToken.None)).IsOk)
    End Function

    <Fact>
    Public Async Function Missing_things_are_reported_as_missing() As Task
        Put("d/index.html", "e")
        Assert.Equal(HtmlOutcome.SourceMissing, OpenStatus("d/nope.html"))
        Assert.Equal(HtmlOutcome.SourceMissing, OpenStatus("nodir/x.html"))
        Assert.Equal(HtmlOutcome.SourceMissing, OpenStatus("d/index.html", "missing-root"))
        Dim ignored As LocalHtmlDocumentSource = Nothing
        Assert.Equal(HtmlOutcome.SourceMissing, LocalHtmlDocumentSource.Open("", Nothing, False, 0L, ignored))
        Assert.Equal(HtmlOutcome.SourceMissing, LocalHtmlDocumentSource.Open(Nothing, Nothing, False, 0L, ignored))

        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html")
        Assert.Equal(HtmlOutcome.SourceMissing, (Await source.ReadResourceAsync("missing.css", CancellationToken.None)).Status)
        Assert.Equal(HtmlOutcome.SourceMissing, (Await source.ReadResourceAsync("nodir/x.css", CancellationToken.None)).Status)
        File.Delete(PathOf("d/index.html"))
        Assert.Equal(HtmlOutcome.SourceMissing, (Await source.ReadEntryAsync(CancellationToken.None)).Status)
    End Function

    <Fact>
    Public Async Function Reading_leaves_no_handle_so_the_original_can_be_moved_and_deleted() As Task
        Dim entryPath As String = Put("d/index.html", "e")
        Put("d/a.css", "a")
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html")
        ' Nothing has been read yet: Open itself must not hold the entry either.
        File.Move(entryPath, PathOf("d/moved.html"))
        File.Move(PathOf("d/moved.html"), entryPath)
        Assert.True((Await source.ReadEntryAsync(CancellationToken.None)).IsOk)
        Assert.True((Await source.ReadResourceAsync("a.css", CancellationToken.None)).IsOk)
        Assert.True((Await source.ReadResourceAsync("a.css", CancellationToken.None)).IsOk)

        File.Move(PathOf("d/a.css"), PathOf("d/b.css"))
        File.Delete(PathOf("d/b.css"))
        File.Move(entryPath, PathOf("d/elsewhere.html"))
        File.Delete(PathOf("d/elsewhere.html"))
        Assert.False(File.Exists(PathOf("d/b.css")))
        Assert.False(File.Exists(entryPath))
        Assert.False(File.Exists(PathOf("d/elsewhere.html")))
    End Function

    ' ---- limits ---------------------------------------------------------------------------

    <Fact>
    Public Async Function Per_kind_byte_limits_refuse_an_oversized_file_without_returning_bytes() As Task
        Put("d/index.html", "e")
        PutBytes("d/exact.css", New Byte(CInt(HtmlDocumentPolicy.MaxStylesheetBytes) - 1) {})
        PutBytes("d/big.css", New Byte(CInt(HtmlDocumentPolicy.MaxStylesheetBytes)) {})
        PutBytes("d/big.woff2", New Byte(CInt(HtmlDocumentPolicy.MaxFontBytes)) {})
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html", cacheBytes:=0L)

        Assert.Equal(HtmlDocumentPolicy.MaxStylesheetBytes, CLng((Await source.ReadResourceAsync("exact.css", CancellationToken.None)).Bytes.Length))
        For Each key As String In {"big.css", "big.woff2"}
            Dim result As HtmlResourceResult = Await source.ReadResourceAsync(key, CancellationToken.None)
            Assert.Equal(HtmlOutcome.LimitExceeded, result.Status)
            Assert.Empty(result.Bytes)
        Next
    End Function

    <Fact>
    Public Async Function An_entry_over_sixteen_mebibytes_is_refused() As Task
        PutBytes("d/index.html", New Byte(CInt(HtmlDocumentPolicy.MaxEntryBytes)) {})
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html")
        Dim result As HtmlResourceResult = Await source.ReadEntryAsync(CancellationToken.None)
        Assert.Equal(HtmlOutcome.LimitExceeded, result.Status)
        Assert.Empty(result.Bytes)
    End Function

    <Fact>
    Public Sub The_size_is_checked_before_any_byte_is_read()
        Dim root As String = ""
        Dim huge As String = PathOf("d/huge.css")
        Directory.CreateDirectory(PathOf("d"))
        ' A 5 GiB file whose data was never written: reading it would allocate and zero-fill.
        Using stream As New FileStream(huge, FileMode.CreateNew, FileAccess.Write)
            stream.SetLength(5L * 1024L * 1024L * 1024L)
        End Using
        Assert.Equal(HtmlOutcome.Ok, HtmlFileAccess.CanonicalRoot(PathOf("d"), root))
        Dim started As Long = Stopwatch.GetTimestamp()
        Dim read As HtmlFileRead = HtmlFileAccess.ReadUnderRoot(root, "huge.css", HtmlDocumentPolicy.MaxStylesheetBytes, CancellationToken.None)
        Assert.Equal(HtmlOutcome.LimitExceeded, read.Status)
        Assert.Empty(read.Bytes)
        Assert.True(Stopwatch.GetTimestamp() - started < Stopwatch.Frequency * 5L)
    End Sub

    <Fact>
    Public Sub ReadUnderRoot_returns_exactly_the_bytes_and_honours_maxBytes_and_cancellation()
        Put("d/four.css", "four")
        Dim root As String = ""
        Assert.Equal(HtmlOutcome.Ok, HtmlFileAccess.CanonicalRoot(PathOf("d"), root))
        Assert.Equal(HtmlOutcome.LimitExceeded, HtmlFileAccess.ReadUnderRoot(root, "four.css", 3L, CancellationToken.None).Status)
        Dim ok As HtmlFileRead = HtmlFileAccess.ReadUnderRoot(root, "four.css", 4L, CancellationToken.None)
        Assert.Equal(HtmlOutcome.Ok, ok.Status)
        Assert.Equal("four", Encoding.UTF8.GetString(ok.Bytes))
        Assert.Equal(4L, ok.Version.Length)
        Using cancelled As New CancellationTokenSource()
            cancelled.Cancel()
            Assert.Equal(HtmlOutcome.Cancelled, HtmlFileAccess.ReadUnderRoot(root, "four.css", 4L, cancelled.Token).Status)
        End Using
        Put("d/empty.css", "")
        Dim empty As HtmlFileRead = HtmlFileAccess.ReadUnderRoot(root, "empty.css", 4L, CancellationToken.None)
        Assert.Equal(HtmlOutcome.Ok, empty.Status)
        Assert.Empty(empty.Bytes)
        Assert.Equal(0L, empty.Version.Length)
    End Sub

    <Fact>
    Public Sub ReadUnderRoot_revalidates_a_key_that_was_not_parsed_by_the_policy()
        Put("top.css", "t")
        Put("d/a.css", "a")
        Dim root As String = ""
        Assert.Equal(HtmlOutcome.Ok, HtmlFileAccess.CanonicalRoot(PathOf("d"), root))
        For Each key As String In {"../top.css", "x/../../top.css", "/a.css", "a\..\a.css", "con.css", "a.css:stream", "a.css."}
            Dim read As HtmlFileRead = HtmlFileAccess.ReadUnderRoot(root, key, 1024L, CancellationToken.None)
            Assert.NotEqual(HtmlOutcome.Ok, read.Status)
            Assert.Empty(read.Bytes)
        Next
    End Sub

    <Fact>
    Public Sub CanonicalRoot_normalizes_the_spelling_and_refuses_what_is_not_a_directory()
        Put("MixedCase/a.html", "a")
        Dim canonical As String = ""
        Assert.Equal(HtmlOutcome.Ok, HtmlFileAccess.CanonicalRoot(PathOf("MIXEDCASE") & "\", canonical))
        Assert.EndsWith("\MixedCase", canonical, StringComparison.Ordinal)
        Dim viaDots As String = ""
        Assert.Equal(HtmlOutcome.Ok, HtmlFileAccess.CanonicalRoot(PathOf("MixedCase\..\MixedCase"), viaDots))
        Assert.Equal(canonical, viaDots)
        Assert.False(canonical.StartsWith("\\?\", StringComparison.Ordinal))

        Assert.Equal(HtmlOutcome.UnsupportedResource, HtmlFileAccess.CanonicalRoot(PathOf("MixedCase/a.html"), canonical))
        Assert.Equal(HtmlOutcome.SourceMissing, HtmlFileAccess.CanonicalRoot(PathOf("nowhere"), canonical))
        Assert.Equal(HtmlOutcome.SourceMissing, HtmlFileAccess.CanonicalRoot("", canonical))
        Assert.Equal(HtmlOutcome.SourceMissing, HtmlFileAccess.CanonicalRoot(Nothing, canonical))
        Assert.Equal(HtmlOutcome.UnsupportedResource, HtmlFileAccess.CanonicalRoot("\\.\C:", canonical))

        Dim driveRoot As String = Path.GetPathRoot(_temp)
        If driveRoot.Length = 3 AndAlso driveRoot(1) = ":"c Then
            Assert.Equal(HtmlOutcome.Ok, HtmlFileAccess.CanonicalRoot(driveRoot, canonical))
            Assert.Equal(driveRoot, canonical, StringComparer.OrdinalIgnoreCase)
        End If
    End Sub

    <Fact>
    Public Async Function Paths_longer_than_the_legacy_limit_are_read() As Task
        Dim segment As String = New String("d"c, 100)
        Dim relative As String = "long/" & segment & "/" & segment & "/" & segment
        Put(relative & "/index.html", "e")
        Put(relative & "/a.css", "deep")
        Assert.True(PathOf(relative & "/a.css").Length > 260)
        Dim source As LocalHtmlDocumentSource = OpenOk(relative & "/index.html", "long")
        Assert.Equal(segment & "/" & segment & "/" & segment & "/index.html", source.Request.EntryResourceKey)
        Assert.Equal("deep", Text(Await source.ReadResourceAsync(segment & "/" & segment & "/" & segment & "/a.css", CancellationToken.None)))
    End Function

    ' ---- key resolution -------------------------------------------------------------------

    <Fact>
    Public Sub The_2049th_distinct_key_is_refused_and_repeats_do_not_count()
        Put("d/index.html", "e")
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html")
        For i As Integer = 0 To HtmlDocumentPolicy.MaxUniqueResources - 1
            Assert.True(source.ResolveKey("r" & i.ToString() & ".css").IsAllowed)
        Next
        Assert.Equal(HtmlOutcome.LimitExceeded, source.ResolveKey("over.css").Status)
        Assert.True(source.ResolveKey("r0.css").IsAllowed)
        Assert.True(source.ResolveKey("R0.CSS").IsAllowed)
        Assert.Equal(HtmlOutcome.LimitExceeded, source.ReadResourceAsync("over2.css", CancellationToken.None).Result.Status)
    End Sub

    <Theory>
    <InlineData("../x.css")>
    <InlineData("a/../b.css")>
    <InlineData("a/./b.css")>
    <InlineData("/abs.css")>
    <InlineData("a\b.css")>
    <InlineData("C:\x.css")>
    <InlineData("con.css")>
    <InlineData("a:b.css")>
    <InlineData("a//b.css")>
    <InlineData("x.css.")>
    <InlineData("")>
    Public Sub A_key_the_parser_would_refuse_is_refused_by_the_source_and_never_counted(key As String)
        Put("d/index.html", "e")
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html")
        Assert.False(source.ResolveKey(key).IsAllowed)
        Assert.Equal("", source.LocalPathOf(key))
        For i As Integer = 0 To HtmlDocumentPolicy.MaxUniqueResources - 1
            Assert.True(source.ResolveKey("r" & i.ToString() & ".css").IsAllowed)
        Next
    End Sub

    <Fact>
    Public Async Function Only_stylesheet_image_font_and_document_files_are_served() As Task
        Put("d/index.html", "e")
        Put("d/x.exe", "MZ")
        Put("d/x.js", "alert(1)")
        Put("d/x.txt", "t")
        Put("d/noext", "n")
        Put("d/page.xhtml", "<p/>")
        Put("d/pic.png", "p")
        Put("d/f.woff", "f")
        Directory.CreateDirectory(PathOf("d/dir.css"))
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html")
        For Each key As String In {"x.exe", "x.js", "x.txt", "noext"}
            Assert.Equal(HtmlOutcome.UnsupportedResource, (Await source.ReadResourceAsync(key, CancellationToken.None)).Status)
            Assert.Equal(HtmlResourceKind.Other, source.KindOf(key))
        Next
        Assert.Equal(HtmlOutcome.UnsupportedResource, (Await source.ReadResourceAsync("dir.css", CancellationToken.None)).Status)
        Dim page As HtmlResourceResult = Await source.ReadResourceAsync("page.xhtml", CancellationToken.None)
        Assert.True(page.IsOk)
        Assert.Equal(HtmlResourceKind.Document, page.Kind)
        Assert.Equal("application/xhtml+xml", page.MimeType)
        Assert.Equal("image/png", (Await source.ReadResourceAsync("pic.png", CancellationToken.None)).MimeType)
        Assert.Equal("font/woff", (Await source.ReadResourceAsync("f.woff", CancellationToken.None)).MimeType)
        Assert.Equal(HtmlResourceKind.Font, source.KindOf("f.woff"))
    End Function

    ' ---- snapshot cache -------------------------------------------------------------------

    <Fact>
    Public Async Function A_repeat_request_is_served_from_the_immutable_snapshot() As Task
        Dim a As String = Put("d/a.css", New String("a"c, 100))
        Put("d/index.html", "e")
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html")
        Dim first As HtmlResourceResult = Await source.ReadResourceAsync("a.css", CancellationToken.None)
        File.WriteAllText(a, "changed on disk")
        Dim second As HtmlResourceResult = Await source.ReadResourceAsync("A.CSS", CancellationToken.None)
        Assert.Same(first.Bytes, second.Bytes)
        Assert.Equal(New String("a"c, 100), Text(second))
    End Function

    <Fact>
    Public Async Function The_cache_evicts_the_least_recently_used_and_never_holds_more_than_its_budget() As Task
        Put("d/index.html", "e")
        Put("d/a.css", New String("a"c, 100))
        Put("d/b.css", New String("b"c, 100))
        Put("d/c.css", New String("c"c, 100))
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html", cacheBytes:=250L)
        Dim a1 As HtmlResourceResult = Await source.ReadResourceAsync("a.css", CancellationToken.None)
        Dim b1 As HtmlResourceResult = Await source.ReadResourceAsync("b.css", CancellationToken.None)
        Assert.Same(a1.Bytes, (Await source.ReadResourceAsync("a.css", CancellationToken.None)).Bytes)
        Dim c1 As HtmlResourceResult = Await source.ReadResourceAsync("c.css", CancellationToken.None)
        ' b was the least recently used: it left the cache, a and c stayed.
        Assert.Same(a1.Bytes, (Await source.ReadResourceAsync("a.css", CancellationToken.None)).Bytes)
        Assert.Same(c1.Bytes, (Await source.ReadResourceAsync("c.css", CancellationToken.None)).Bytes)
        Dim b2 As HtmlResourceResult = Await source.ReadResourceAsync("b.css", CancellationToken.None)
        Assert.NotSame(b1.Bytes, b2.Bytes)
        Assert.Equal(Text(b1), Text(b2))
    End Function

    <Fact>
    Public Async Function A_resource_bigger_than_the_cache_is_served_but_not_cached() As Task
        Put("d/index.html", "e")
        Put("d/a.css", New String("a"c, 100))
        Dim small As LocalHtmlDocumentSource = OpenOk("d/index.html", cacheBytes:=10L)
        Dim s1 As HtmlResourceResult = Await small.ReadResourceAsync("a.css", CancellationToken.None)
        Dim s2 As HtmlResourceResult = Await small.ReadResourceAsync("a.css", CancellationToken.None)
        Assert.True(s1.IsOk)
        Assert.True(s2.IsOk)
        Assert.NotSame(s1.Bytes, s2.Bytes)

        Dim none As LocalHtmlDocumentSource = OpenOk("d/index.html", cacheBytes:=0L)
        Assert.NotSame((Await none.ReadResourceAsync("a.css", CancellationToken.None)).Bytes, (Await none.ReadResourceAsync("a.css", CancellationToken.None)).Bytes)
    End Function

    ' ---- concurrency, deadline, cancellation, disposal ------------------------------------

    <Fact>
    Public Async Function Many_parallel_reads_all_complete_through_the_four_slot_gate() As Task
        Put("d/index.html", "e")
        For i As Integer = 0 To 39
            Put("d/f" & i.ToString() & ".css", "body" & i.ToString())
        Next
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html", cacheBytes:=0L)
        Dim tasks As New List(Of Task(Of HtmlResourceResult))()
        For i As Integer = 0 To 39
            tasks.Add(source.ReadResourceAsync("f" & i.ToString() & ".css", CancellationToken.None))
        Next
        Dim all As Task(Of HtmlResourceResult()) = Task.WhenAll(tasks)
        Dim finished As Task = Await Task.WhenAny(all, Task.Delay(30000))
        Assert.Same(all, finished)
        For i As Integer = 0 To 39
            Assert.True(all.Result(i).IsOk, all.Result(i).Status.ToString())
            Assert.Equal("body" & i.ToString(), Text(all.Result(i)))
        Next
    End Function

    <Fact>
    Public Async Function A_resource_that_misses_its_deadline_is_timed_out_and_the_source_stays_usable() As Task
        Put("d/index.html", "e")
        Put("d/a.css", "a")
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html", cacheBytes:=0L)
        source.DeadlineMs = 0
        Assert.Equal(HtmlOutcome.TimedOut, (Await source.ReadResourceAsync("a.css", CancellationToken.None)).Status)
        source.DeadlineMs = HtmlDocumentPolicy.LocalResourceDeadlineMs
        Assert.True((Await source.ReadResourceAsync("a.css", CancellationToken.None)).IsOk)
    End Function

    <Fact>
    Public Async Function A_cancelled_caller_gets_Cancelled_not_TimedOut() As Task
        Put("d/index.html", "e")
        Put("d/a.css", "a")
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html", cacheBytes:=0L)
        Using cts As New CancellationTokenSource()
            cts.Cancel()
            Assert.Equal(HtmlOutcome.Cancelled, (Await source.ReadResourceAsync("a.css", cts.Token)).Status)
            Assert.Equal(HtmlOutcome.Cancelled, (Await source.ReadEntryAsync(cts.Token)).Status)
        End Using
        Assert.True((Await source.ReadResourceAsync("a.css", CancellationToken.None)).IsOk)
    End Function

    <Fact>
    Public Async Function Dispose_is_idempotent_and_ends_all_further_work() As Task
        Put("d/index.html", "e")
        Put("d/a.css", "a")
        Dim source As LocalHtmlDocumentSource = OpenOk("d/index.html")
        Assert.True((Await source.ReadResourceAsync("a.css", CancellationToken.None)).IsOk)
        source.Dispose()
        source.Dispose()
        Assert.Equal(HtmlOutcome.Cancelled, (Await source.ReadResourceAsync("a.css", CancellationToken.None)).Status)
        Assert.Equal(HtmlOutcome.Cancelled, (Await source.ReadEntryAsync(CancellationToken.None)).Status)
        Assert.False(source.ResolveKey("b.css").IsAllowed)
    End Function
End Class
#End If
