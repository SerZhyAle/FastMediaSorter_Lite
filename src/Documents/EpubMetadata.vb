#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Xml.Linq
Imports System.Xml

Public NotInheritable Class EpubInfo
        Public Property Title As String = ""
        Public Property Author As String = ""
        Public Property CoverBytes As Byte()
        Public Property HasCover As Boolean
        Public Property Success As Boolean
        Public Property ErrorMessage As String = ""
    End Class

    Public Module EpubMetadata

        Public Function Read(filePath As String) As EpubInfo
            Dim info As New EpubInfo()
            If String.IsNullOrEmpty(filePath) OrElse Not File.Exists(filePath) Then
                info.Success = False
                info.ErrorMessage = "File not found"
                Return info
            End If

            Try
                Using fs As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                    Return Read(fs)
                End Using
            Catch ex As Exception
                info.Success = False
                info.ErrorMessage = FailureText.Describe(ex)
                Return info
            End Try
        End Function

        Public Function Read(bytes As Byte()) As EpubInfo
            If bytes Is Nothing OrElse bytes.Length = 0 Then
                Dim emptyInfo As New EpubInfo()
                emptyInfo.Success = False
                emptyInfo.ErrorMessage = "Empty data"
                Return emptyInfo
            End If

            Using ms As New MemoryStream(bytes)
                Return Read(ms)
            End Using
        End Function

        Public Function Read(stream As Stream) As EpubInfo
            Dim info As New EpubInfo()
            Try
                Using archive As New ZipArchive(stream, ZipArchiveMode.Read, leaveOpen:=True)
                    ' 1. Find META-INF/container.xml
                    Dim containerEntry = archive.Entries.FirstOrDefault(Function(e) Not e.FullName.EndsWith("/") AndAlso
                        String.Equals(e.FullName.Replace("\"c, "/"c), "META-INF/container.xml", StringComparison.OrdinalIgnoreCase))

                    If containerEntry Is Nothing Then
                        info.Success = False
                        info.ErrorMessage = "container.xml not found in EPUB"
                        Return info
                    End If

                    Dim opfPath As String = ""
                    Using entryStream = containerEntry.Open()
                        Dim doc As XDocument = ReadXml(entryStream)
                        Dim rootfileElem = doc.Descendants().FirstOrDefault(Function(elem) elem.Name.LocalName = "rootfile" AndAlso elem.Attribute("full-path") IsNot Nothing)
                        If rootfileElem IsNot Nothing Then
                            opfPath = rootfileElem.Attribute("full-path").Value
                        End If
                    End Using

                    If String.IsNullOrEmpty(opfPath) Then
                        info.Success = False
                        info.ErrorMessage = "OPF path not found in container.xml"
                        Return info
                    End If

                    opfPath = opfPath.Replace("\"c, "/"c).TrimStart("/"c)
                    Dim opfDir As String = Path.GetDirectoryName(opfPath)
                    If opfDir Is Nothing Then opfDir = ""
                    opfDir = opfDir.Replace("\"c, "/"c).TrimEnd("/"c)

                    ' 2. Find OPF entry
                    Dim opfEntry = archive.Entries.FirstOrDefault(Function(e) Not e.FullName.EndsWith("/") AndAlso
                        String.Equals(e.FullName.Replace("\"c, "/"c), opfPath, StringComparison.OrdinalIgnoreCase))

                    If opfEntry Is Nothing Then
                        info.Success = False
                        info.ErrorMessage = "OPF entry not found: " & opfPath
                        Return info
                    End If

                    Dim coverHref As String = ""
                    Dim coverId As String = ""

                    Using opfStream = opfEntry.Open()
                        Dim opfDoc As XDocument = ReadXml(opfStream)

                        ' Extract title
                        Dim titleElem = opfDoc.Descendants().FirstOrDefault(Function(elem) elem.Name.LocalName.Equals("title", StringComparison.OrdinalIgnoreCase))
                        If titleElem IsNot Nothing Then
                            info.Title = titleElem.Value.Trim()
                        End If

                        ' Extract author / creator
                        Dim creatorElem = opfDoc.Descendants().FirstOrDefault(Function(elem) elem.Name.LocalName.Equals("creator", StringComparison.OrdinalIgnoreCase))
                        If creatorElem IsNot Nothing Then
                            info.Author = creatorElem.Value.Trim()
                        End If

                        ' Extract cover reference
                        ' Check <meta name="cover" content="cover-id" />
                        Dim metaCover = opfDoc.Descendants().FirstOrDefault(Function(elem) elem.Name.LocalName.Equals("meta", StringComparison.OrdinalIgnoreCase) AndAlso
                            String.Equals(CStr(elem.Attribute("name")), "cover", StringComparison.OrdinalIgnoreCase))
                        If metaCover IsNot Nothing AndAlso metaCover.Attribute("content") IsNot Nothing Then
                            coverId = metaCover.Attribute("content").Value
                        End If

                        ' Check manifest items
                        Dim manifestItems = opfDoc.Descendants().Where(Function(elem) elem.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase)).ToList()

                        ' Priority 1: item with properties="cover-image"
                        Dim coverItem = manifestItems.FirstOrDefault(Function(elem)
                            Dim props As String = CStr(elem.Attribute("properties"))
                            Return props IsNot Nothing AndAlso props.Split(New Char() {" "c, ChrW(9), ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries).Contains("cover-image", StringComparer.OrdinalIgnoreCase)
                        End Function)

                        ' Priority 2: item with id = coverId
                        If coverItem Is Nothing AndAlso Not String.IsNullOrEmpty(coverId) Then
                            coverItem = manifestItems.FirstOrDefault(Function(elem) String.Equals(CStr(elem.Attribute("id")), coverId, StringComparison.OrdinalIgnoreCase))
                        End If

                        ' Priority 3: item with id containing "cover" and image media-type
                        If coverItem Is Nothing Then
                            coverItem = manifestItems.FirstOrDefault(Function(elem)
                                Dim id As String = CStr(elem.Attribute("id"))
                                Dim mediaType As String = CStr(elem.Attribute("media-type"))
                                Return id IsNot Nothing AndAlso id.IndexOf("cover", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
                                       mediaType IsNot Nothing AndAlso mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                            End Function)
                        End If

                        ' Priority 4: item with href containing "cover" and image media-type
                        If coverItem Is Nothing Then
                            coverItem = manifestItems.FirstOrDefault(Function(elem)
                                Dim href As String = CStr(elem.Attribute("href"))
                                Dim mediaType As String = CStr(elem.Attribute("media-type"))
                                Return href IsNot Nothing AndAlso href.IndexOf("cover", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
                                       mediaType IsNot Nothing AndAlso mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                            End Function)
                        End If

                        If coverItem IsNot Nothing AndAlso coverItem.Attribute("href") IsNot Nothing Then
                            coverHref = Uri.UnescapeDataString(coverItem.Attribute("href").Value.Split("#"c)(0))
                        End If
                    End Using

                    ' 3. Extract cover image bytes if found
                    If Not String.IsNullOrEmpty(coverHref) Then
                        ' Combine with opfDir
                        Dim fullCoverKey As String = If(String.IsNullOrEmpty(opfDir), coverHref, opfDir & "/" & coverHref)
                        fullCoverKey = NormalizeZipPath(fullCoverKey)

                        Dim coverEntry = archive.Entries.FirstOrDefault(Function(e) Not e.FullName.EndsWith("/") AndAlso
                            String.Equals(NormalizeZipPath(e.FullName), fullCoverKey, StringComparison.OrdinalIgnoreCase))

                        If coverEntry IsNot Nothing AndAlso coverEntry.Length <= 8L * 1024L * 1024L Then
                            Using coverStream = coverEntry.Open()
                                Using ms As New MemoryStream()
                                    Dim buffer(81919) As Byte
                                    Dim count As Integer = coverStream.Read(buffer, 0, buffer.Length)
                                    While count > 0
                                        If ms.Length + count > 8L * 1024L * 1024L Then Exit While
                                        ms.Write(buffer, 0, count)
                                        count = coverStream.Read(buffer, 0, buffer.Length)
                                    End While
                                    If count = 0 Then
                                        info.CoverBytes = ms.ToArray()
                                        info.HasCover = info.CoverBytes.Length > 0
                                    End If
                                End Using
                            End Using
                        End If
                    End If

                    info.Success = True
                    Return info
                End Using
            Catch ex As Exception
                info.Success = False
                info.ErrorMessage = FailureText.Describe(ex)
                Return info
            End Try
        End Function

        Private Function ReadXml(stream As Stream) As XDocument
            Dim settings As New XmlReaderSettings With {.DtdProcessing = DtdProcessing.Prohibit,
                .XmlResolver = Nothing, .MaxCharactersInDocument = 2L * 1024L * 1024L}
            Using reader As XmlReader = XmlReader.Create(stream, settings)
                Return XDocument.Load(reader)
            End Using
        End Function

        Private Function NormalizeZipPath(path As String) As String
            If String.IsNullOrEmpty(path) Then Return ""
            Dim segments As String() = path.Replace("\"c, "/"c).Split("/"c)
            Dim result As New List(Of String)()
            For Each segment As String In segments
                If segment = "" OrElse segment = "." Then
                    Continue For
                ElseIf segment = ".." Then
                    If result.Count > 0 Then result.RemoveAt(result.Count - 1)
                Else
                    result.Add(segment)
                End If
            Next
            Return String.Join("/", result)
        End Function

    End Module
#End If
