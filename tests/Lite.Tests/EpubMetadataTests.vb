#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports Xunit

Public Class EpubMetadataTests

        Private Function CreateMockEpub(title As String, author As String, hasCover As Boolean) As Byte()
            Using ms As New MemoryStream()
                Using zip As New ZipArchive(ms, ZipArchiveMode.Create, True)
                    ' 1. META-INF/container.xml
                    Dim containerEntry = zip.CreateEntry("META-INF/container.xml")
                    Using writer As New StreamWriter(containerEntry.Open(), Encoding.UTF8)
                        writer.Write("<?xml version=""1.0""?>" &
                            "<container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"">" &
                            "<rootfiles><rootfile full-path=""OEBPS/content.opf"" media-type=""application/oebps-package+xml""/></rootfiles>" &
                            "</container>")
                    End Using

                    ' 2. OEBPS/content.opf
                    Dim opfEntry = zip.CreateEntry("OEBPS/content.opf")
                    Using writer As New StreamWriter(opfEntry.Open(), Encoding.UTF8)
                        Dim coverItem As String = If(hasCover, "<item id=""cover"" href=""images/cover.jpg"" media-type=""image/jpeg"" properties=""cover-image""/>", "")
                        writer.Write("<?xml version=""1.0"" encoding=""utf-8""?>" &
                            "<package xmlns=""http://www.idpf.org/2007/opf"" version=""3.0"" unique-identifier=""BookId"">" &
                            "<metadata xmlns:dc=""http://purl.org/dc/elements/1.1/"">" &
                            "<dc:title>" & title & "</dc:title>" &
                            "<dc:creator>" & author & "</dc:creator>" &
                            "</metadata>" &
                            "<manifest>" & coverItem & "</manifest>" &
                            "</package>")
                    End Using

                    ' 3. OEBPS/images/cover.jpg
                    If hasCover Then
                        Dim coverEntry = zip.CreateEntry("OEBPS/images/cover.jpg")
                        Using stream = coverEntry.Open()
                            Dim dummyJpg As Byte() = New Byte() {&HFF, &HD8, &HFF, &HE0, 0, 16, 74, 70, 73, 70, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0, &HFF, &HD9}
                            stream.Write(dummyJpg, 0, dummyJpg.Length)
                        End Using
                    End If
                End Using
                Return ms.ToArray()
            End Using
        End Function

        <Fact>
        Public Sub Epub_with_title_author_and_cover_extracts_correctly()
            Dim epubBytes As Byte() = CreateMockEpub("The Great Book", "Jane Doe", True)
            Dim info As EpubInfo = EpubMetadata.Read(epubBytes)
            Assert.True(info.Success)
            Assert.Equal("The Great Book", info.Title)
            Assert.Equal("Jane Doe", info.Author)
            Assert.True(info.HasCover)
            Assert.NotNull(info.CoverBytes)
            Assert.NotEmpty(info.CoverBytes)
        End Sub

        <Fact>
        Public Sub Epub_without_cover_extracts_metadata_without_error()
            Dim epubBytes As Byte() = CreateMockEpub("Untitled Notes", "Unknown", False)
            Dim info As EpubInfo = EpubMetadata.Read(epubBytes)
            Assert.True(info.Success)
            Assert.Equal("Untitled Notes", info.Title)
            Assert.Equal("Unknown", info.Author)
            Assert.False(info.HasCover)
            Assert.Null(info.CoverBytes)
        End Sub

        <Fact>
        Public Sub Corrupt_epub_handles_gracefully()
            Dim garbage As Byte() = New Byte() {1, 2, 3, 4, 5, 6, 7, 8}
            Dim info As EpubInfo = EpubMetadata.Read(garbage)
            Assert.False(info.Success)
            Assert.False(info.HasCover)
        End Sub

        Private Function ReplaceEntry(bytes As Byte(), name As String, replacement As String) As Byte()
            Using memory As New MemoryStream()
                memory.Write(bytes, 0, bytes.Length)
                memory.Position = 0
                Using archive As New ZipArchive(memory, ZipArchiveMode.Update, True)
                    archive.GetEntry(name).Delete()
                    Using writer As New StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8)
                        writer.Write(replacement)
                    End Using
                End Using
                Return memory.ToArray()
            End Using
        End Function

        <Fact>
        Public Sub Dtd_and_entity_documents_are_refused()
            Dim bytes As Byte() = CreateMockEpub("Title", "Author", False)
            bytes = ReplaceEntry(bytes, "META-INF/container.xml",
                "<!DOCTYPE container [<!ENTITY injected 'OEBPS/content.opf'>]><container><rootfile full-path='&injected;'/></container>")
            Dim info As EpubInfo = EpubMetadata.Read(bytes)
            Assert.False(info.Success)
            Assert.False(info.HasCover)
        End Sub

        <Fact>
        Public Sub Epub2_cover_reference_and_relative_paths_are_resolved()
            Dim bytes As Byte() = CreateMockEpub("Title", "Author", True)
            bytes = ReplaceEntry(bytes, "OEBPS/content.opf",
                "<package><metadata><title>Title</title><creator>Author</creator><meta name='cover' content='art'/></metadata>" &
                "<manifest><item id='art' href='../OEBPS/images/cover.jpg' media-type='image/jpeg'/></manifest></package>")
            Dim info As EpubInfo = EpubMetadata.Read(bytes)
            Assert.True(info.Success)
            Assert.True(info.HasCover)
            Assert.Equal("Author", info.Author)
        End Sub

    End Class
#End If
