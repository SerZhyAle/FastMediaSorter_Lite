Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports Xunit

Public Class EpubPackageTests
    Private Shared Function Package(Optional epub3 As Boolean = True, Optional extraName As String = Nothing, Optional extraValue As String = "") As MemoryStream
        Dim stream As New MemoryStream()
        Using zip As New ZipArchive(stream, ZipArchiveMode.Create, leaveOpen:=True)
            Dim entries As New Dictionary(Of String, String) From {
                {"META-INF/container.xml", "<container><rootfiles><rootfile full-path='OPS/book.opf' media-type='application/oebps-package+xml'/></rootfiles></container>"},
                {"OPS/book.opf", "<package xmlns:dc='http://purl.org/dc/elements/1.1/'><metadata><dc:title>Book</dc:title><dc:creator>Author</dc:creator><meta property='rendition:layout'>pre-paginated</meta></metadata><manifest><item id='one' href='one.xhtml' media-type='application/xhtml+xml'/><item id='two' href='chapters/two.xhtml' media-type='application/xhtml+xml'/><item id='nav' href='" & If(epub3, "nav.xhtml' properties='nav' media-type='application/xhtml+xml", "toc.ncx' media-type='application/x-dtbncx+xml") & "'/></manifest><spine toc='nav' page-progression-direction='rtl'><itemref idref='two'/><itemref idref='one' properties='rendition:layout-reflowable'/></spine></package>"},
                {"OPS/one.xhtml", "<html><body><p>First chapter</p></body></html>"},
                {"OPS/chapters/two.xhtml", "<html><body><p>Second chapter</p></body></html>"}}
            entries.Add(If(epub3, "OPS/nav.xhtml", "OPS/toc.ncx"), If(epub3,
                "<html xmlns:epub='http://www.idpf.org/2007/ops'><body><nav epub:type='toc'><ol><li><a href='chapters/two.xhtml#start'>Second</a><ol><li><a href='one.xhtml'>First</a></li></ol></li></ol></nav></body></html>",
                "<ncx><navMap><navPoint><navLabel><text>Second</text></navLabel><content src='chapters/two.xhtml#start'/><navPoint><navLabel><text>First</text></navLabel><content src='one.xhtml'/></navPoint></navPoint></navMap></ncx>"))
            If extraName IsNot Nothing Then entries(extraName) = extraValue
            For Each item In entries
                Using writer As New StreamWriter(zip.CreateEntry(item.Key).Open(), New UTF8Encoding(False))
                    writer.Write(item.Value)
                End Using
            Next
        End Using
        stream.Position = 0
        Return stream
    End Function

    <Theory>
    <InlineData(True)>
    <InlineData(False)>
    Public Sub Epub2_and_3_preserve_spine_nested_toc_anchors_and_layout(epub3 As Boolean)
        Using stream = Package(epub3)
            Dim book = EpubPackage.Read(stream)
            Assert.Equal("Book", book.Title)
            Assert.Equal("Author", book.Author)
            Assert.True(book.RightToLeft)
            Assert.Equal("OPS/chapters/two.xhtml", book.Spine(0).Key)
            Assert.True(book.Spine(0).FixedLayout)
            Assert.False(book.Spine(1).FixedLayout)
            Assert.Equal("start", Assert.Single(book.Toc).Anchor)
            Assert.Equal("OPS/one.xhtml", Assert.Single(book.Toc(0).Children).Key)
            stream.Position = 0
            Assert.Contains("Second chapter", Encoding.UTF8.GetString(EpubPackage.ReadResource(stream, book.Spine(0).Key)))
        End Using
    End Sub

    <Theory>
    <InlineData("../../secret")>
    <InlineData("%2e%2e/%2e%2e/secret")>
    <InlineData("/absolute")>
    <InlineData("file:///secret")>
    <InlineData("https://example.com/chapter")>
    <InlineData("..\secret")>
    <InlineData("//server/share")>
    Public Sub Resource_references_cannot_escape_the_package(reference As String)
        Assert.Throws(Of InvalidDataException)(Sub() EpubPackage.ResolveKey("OPS/book.opf", reference))
    End Sub

    <Fact>
    Public Sub Relative_paths_decode_once_and_keep_package_case()
        Assert.Equal("OPS/images/a b%23.png", EpubPackage.ResolveKey("OPS/chapters/one.xhtml", "../images/a%20b%2523.png#anchor"))
        Assert.Equal("OPS/chapter.xhtml", EpubPackage.ResolveKey("OPS/chapter.xhtml", "#note"))
    End Sub

    <Fact>
    Public Sub Book_search_follows_spine_scope_and_cancellation_without_loading_all_chapters()
        Using stream = Package()
            Dim book = EpubPackage.Read(stream)
            stream.Position = 0
            Dim hits = book.Search(stream, "chapter", False)
            Assert.Equal(2, hits.Count)
            Assert.Equal(book.Spine(0).Key, hits(0).SpineKey)
            stream.Position = 0
            Assert.Single(book.Search(stream, "chapter", False, book.Spine(1).Key))
            stream.Position = 0
            Assert.Empty(book.Search(stream, "CHAPTER", True))
            stream.Position = 0
            Assert.Throws(Of OperationCanceledException)(Sub() book.Search(stream, "chapter", False, token:=New CancellationToken(True)))
        End Using
    End Sub

    <Fact>
    Public Sub A_hundred_MiB_package_is_indexed_without_expanding_its_large_resource()
        Using stream = Package()
            Using zip As New ZipArchive(stream, ZipArchiveMode.Update, leaveOpen:=True)
                Using entry = zip.CreateEntry("large.bin", CompressionLevel.Fastest).Open()
                    Dim chunk(1024 * 1024 - 1) As Byte
                    For index = 1 To 100
                        entry.Write(chunk, 0, chunk.Length)
                    Next
                End Using
            End Using
            stream.Position = 0
            Assert.Equal(2, EpubPackage.Read(stream).Spine.Count)
            stream.Position = 0
            Assert.Throws(Of InvalidDataException)(Sub() EpubPackage.ReadResource(stream, "large.bin"))
        End Using
    End Sub

    <Fact>
    Public Sub Directory_count_and_expansion_bombs_are_rejected_before_package_resources_are_read()
        Using stream = Package()
            Dim bytes = stream.ToArray()
            Dim finish = bytes.Length - 22
            Array.Copy(BitConverter.GetBytes(CUShort(EpubPackage.MaxEntries + 1)), 0, bytes, finish + 8, 2)
            Array.Copy(BitConverter.GetBytes(CUShort(EpubPackage.MaxEntries + 1)), 0, bytes, finish + 10, 2)
            Using hostile As New MemoryStream(bytes)
                Assert.Throws(Of InvalidDataException)(Sub() EpubPackage.Read(hostile))
            End Using
        End Using
        Using stream = Package()
            Dim bytes = stream.ToArray()
            Dim central As Integer = -1
            For index = 0 To bytes.Length - 46
                If BitConverter.ToUInt32(bytes, index) = &H2014B50UI Then
                    central = index
                    Exit For
                End If
            Next
            Assert.True(central >= 0)
            Array.Copy(BitConverter.GetBytes(CUInt(600 * 1024 * 1024)), 0, bytes, central + 24, 4)
            Using hostile As New MemoryStream(bytes)
                Assert.Throws(Of InvalidDataException)(Sub() EpubPackage.Read(hostile))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Drm_dtd_depth_traversal_and_cancellation_fail_without_extracting()
        Using stream = Package(extraName:="META-INF/rights.xml", extraValue:="<rights/>")
            Assert.Throws(Of NotSupportedException)(Sub() EpubPackage.Read(stream))
        End Using
        Using stream = Package(extraName:="META-INF/encryption.xml", extraValue:="<encryption><EncryptionMethod Algorithm='drm'/></encryption>")
            Assert.Throws(Of NotSupportedException)(Sub() EpubPackage.Read(stream))
        End Using
        Using stream = Package(extraName:="../escape.xhtml", extraValue:="bad")
            Assert.Throws(Of InvalidDataException)(Sub() EpubPackage.Read(stream))
        End Using
        Using stream = Package(extraName:="META-INF/container.xml", extraValue:="<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///private'>]><container>&a;</container>")
            Assert.Throws(Of Xml.XmlException)(Sub() EpubPackage.Read(stream))
        End Using
        Using stream = Package(extraName:="META-INF/container.xml", extraValue:=String.Concat(Enumerable.Repeat("<x>", 70)) & String.Concat(Enumerable.Repeat("</x>", 70)))
            Assert.Throws(Of InvalidDataException)(Sub() EpubPackage.Read(stream))
        End Using
        Using stream = Package()
            Assert.Throws(Of OperationCanceledException)(Sub() EpubPackage.Read(stream, New CancellationToken(True)))
        End Using
    End Sub
End Class
