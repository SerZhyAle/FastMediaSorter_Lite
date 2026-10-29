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
Imports System.Text
Imports System.Threading
Imports System.Xml
Imports System.Xml.Linq

Public NotInheritable Class EpubResource
    Public Property Id As String = ""
    Public Property Key As String = ""
    Public Property MediaType As String = ""
    Public Property Properties As String = ""
    Public Property FixedLayout As Boolean
End Class

Public NotInheritable Class EpubTocItem
    Public Property Label As String = ""
    Public Property Key As String = ""
    Public Property Anchor As String = ""
    Public Property Children As New List(Of EpubTocItem)()
End Class

Public NotInheritable Class EpubSearchHit
    Public Property SpineKey As String = ""
    Public Property TextOffset As Integer
    Public Property Snippet As String = ""
End Class

' Package model only: 275 owns chapter rendering and the restricted content host.
' No extraction to disk, package-wide expansion, network fetch or script execution.
Public NotInheritable Class EpubPackage
    Public Const MaxEntries As Integer = 20000
    Public Const MaxExpandedBytes As Long = 512L * 1024 * 1024
    Public Const MaxResourceBytes As Integer = 16 * 1024 * 1024
    Public Const MaxXmlBytes As Integer = 2 * 1024 * 1024
    Public Const MaxXmlDepth As Integer = 64
    Public Const MaxDirectoryBytes As Integer = 32 * 1024 * 1024
    Public Property Title As String = ""
    Public Property Author As String = ""
    Public Property RightToLeft As Boolean
    Public ReadOnly Property Resources As New Dictionary(Of String, EpubResource)(StringComparer.Ordinal)
    Public ReadOnly Property Spine As New List(Of EpubResource)()
    Public ReadOnly Property Toc As New List(Of EpubTocItem)()

    ' ZipArchive materializes the central directory before Entries.Count can be
    ' checked. Bound and count that directory first, without expanding any entry.
    Private Shared Sub ValidateDirectory(stream As Stream, token As CancellationToken)
        If Not stream.CanSeek OrElse Not stream.CanRead Then Throw New InvalidDataException("EPUB input requires an owned seekable snapshot.")
        Dim original = stream.Position
        Try
            If stream.Length < 22 OrElse stream.Length > MaxExpandedBytes + MaxDirectoryBytes Then Throw New InvalidDataException("EPUB package size exceeds the limit.")
            Dim tailLength = CInt(Math.Min(stream.Length, 65557L))
            Dim tail(tailLength - 1) As Byte
            stream.Position = stream.Length - tailLength
            ReadExactly(stream, tail, token)
            Dim endOffset As Integer = -1
            For index = tail.Length - 22 To 0 Step -1
                If BitConverter.ToUInt32(tail, index) = &H6054B50UI AndAlso index + 22 + BitConverter.ToUInt16(tail, index + 20) = tail.Length Then
                    endOffset = index
                    Exit For
                End If
            Next
            If endOffset < 0 Then Throw New InvalidDataException("EPUB ZIP directory is missing.")
            If BitConverter.ToUInt16(tail, endOffset + 4) <> 0 OrElse BitConverter.ToUInt16(tail, endOffset + 6) <> 0 Then Throw New InvalidDataException("Split EPUB archives are not supported.")
            Dim declared = CInt(BitConverter.ToUInt16(tail, endOffset + 10))
            If declared > MaxEntries OrElse BitConverter.ToUInt16(tail, endOffset + 8) <> declared Then Throw New InvalidDataException("EPUB entry count exceeds the limit.")
            Dim size = CLng(BitConverter.ToUInt32(tail, endOffset + 12))
            Dim offset = CLng(BitConverter.ToUInt32(tail, endOffset + 16))
            Dim endPosition = stream.Length - tailLength + endOffset
            If size > MaxDirectoryBytes OrElse offset + size <> endPosition Then Throw New InvalidDataException("Invalid EPUB ZIP directory bounds.")
            stream.Position = offset
            Dim count As Integer
            Dim header(45) As Byte
            While stream.Position < endPosition
                token.ThrowIfCancellationRequested()
                If endPosition - stream.Position < header.Length Then Throw New InvalidDataException("Truncated EPUB ZIP directory.")
                ReadExactly(stream, header, token)
                If BitConverter.ToUInt32(header, 0) <> &H2014B50UI Then Throw New InvalidDataException("Invalid EPUB ZIP directory entry.")
                count += 1
                If count > MaxEntries Then Throw New InvalidDataException("EPUB entry count exceeds the limit.")
                Dim nameLength = CInt(BitConverter.ToUInt16(header, 28))
                Dim trailing = nameLength + CInt(BitConverter.ToUInt16(header, 30)) + CInt(BitConverter.ToUInt16(header, 32))
                If nameLength = 0 OrElse nameLength > 4096 OrElse trailing > endPosition - stream.Position OrElse BitConverter.ToUInt16(header, 34) <> 0 Then Throw New InvalidDataException("Invalid EPUB ZIP entry metadata.")
                If BitConverter.ToUInt32(header, 24) = UInteger.MaxValue OrElse BitConverter.ToUInt32(header, 42) = UInteger.MaxValue Then Throw New InvalidDataException("ZIP64 EPUB exceeds the supported package limits.")
                stream.Seek(trailing, SeekOrigin.Current)
            End While
            If count <> declared Then Throw New InvalidDataException("EPUB ZIP entry count disagrees with its directory.")
        Finally
            stream.Position = original
        End Try
    End Sub

    Private Shared Sub ReadExactly(stream As Stream, buffer As Byte(), token As CancellationToken)
        Dim offset As Integer
        While offset < buffer.Length
            token.ThrowIfCancellationRequested()
            Dim count = stream.Read(buffer, offset, buffer.Length - offset)
            If count = 0 Then Throw New InvalidDataException("Truncated EPUB package.")
            offset += count
        End While
    End Sub

    Public Shared Function ResolveKey(baseKey As String, reference As String) As String
        If String.IsNullOrWhiteSpace(reference) Then Throw New InvalidDataException("Empty EPUB resource reference.")
        Dim fragment = reference.IndexOf("#"c)
        If fragment >= 0 Then reference = reference.Substring(0, fragment)
        If reference.Length = 0 Then Return baseKey
        reference = Uri.UnescapeDataString(reference)
        If reference.Contains("\") OrElse reference.StartsWith("/", StringComparison.Ordinal) OrElse reference.Contains(":"c) OrElse reference.Contains("?"c) OrElse reference.Any(Function(c) Char.IsControl(c)) Then Throw New InvalidDataException("Unsafe EPUB resource reference.")
        Dim parts As New List(Of String)()
        Dim slash = baseKey.LastIndexOf("/"c)
        If slash >= 0 Then parts.AddRange(baseKey.Substring(0, slash).Split("/"c))
        For Each part In reference.Split("/"c)
            If part = "" OrElse part = "." Then Continue For
            If part = ".." Then
                If parts.Count = 0 Then Throw New InvalidDataException("EPUB resource escapes the package.")
                parts.RemoveAt(parts.Count - 1)
            Else
                parts.Add(part)
            End If
        Next
        If parts.Count = 0 Then Throw New InvalidDataException("Empty EPUB resource key.")
        Return String.Join("/", parts)
    End Function

    Private Shared Function Entries(archive As ZipArchive) As Dictionary(Of String, ZipArchiveEntry)
        If archive.Entries.Count > MaxEntries Then Throw New InvalidDataException("EPUB entry count exceeds the limit.")
        Dim result As New Dictionary(Of String, ZipArchiveEntry)(StringComparer.Ordinal)
        Dim expanded As Long
        For Each entry In archive.Entries
            If entry.FullName.EndsWith("/", StringComparison.Ordinal) Then Continue For
            ' Entry names are names, never URI-decoded. References are decoded exactly once.
            If entry.FullName.Split("/"c).Any(Function(part) part = ".." OrElse part = ".") OrElse entry.FullName.Contains("\") OrElse
                entry.FullName.StartsWith("/", StringComparison.Ordinal) OrElse entry.FullName.Contains(":"c) OrElse entry.FullName.Any(Function(c) Char.IsControl(c)) Then Throw New InvalidDataException("Unsafe EPUB entry name.")
            If entry.Length < 0 OrElse entry.Length > MaxExpandedBytes - expanded Then Throw New InvalidDataException("EPUB expanded size exceeds the limit.")
            expanded += entry.Length
            If result.ContainsKey(entry.FullName) Then Throw New InvalidDataException("Duplicate EPUB entry.")
            result.Add(entry.FullName, entry)
        Next
        Return result
    End Function

    Private Shared Function ReadEntry(entry As ZipArchiveEntry, ceiling As Integer, token As CancellationToken) As Byte()
        If entry.Length > ceiling Then Throw New InvalidDataException("EPUB resource exceeds the limit.")
        Using input = entry.Open(), output As New MemoryStream()
            Dim buffer(8191) As Byte
            Do
                token.ThrowIfCancellationRequested()
                Dim count = input.Read(buffer, 0, buffer.Length)
                If count = 0 Then Exit Do
                If output.Length + count > ceiling Then Throw New InvalidDataException("EPUB resource exceeds the limit.")
                output.Write(buffer, 0, count)
            Loop
            Return output.ToArray()
        End Using
    End Function

    Private Shared Function Xml(entries As Dictionary(Of String, ZipArchiveEntry), key As String, token As CancellationToken) As XDocument
        Dim entry As ZipArchiveEntry = Nothing
        If Not entries.TryGetValue(key, entry) Then Throw New InvalidDataException("EPUB package resource is missing.")
        Dim settings As New XmlReaderSettings With {.DtdProcessing = DtdProcessing.Prohibit, .XmlResolver = Nothing, .MaxCharactersInDocument = MaxXmlBytes}
        Dim bytes = ReadEntry(entry, MaxXmlBytes, token)
        ' Check depth before building the tree, including empty elements.
        Using input As New MemoryStream(bytes), reader = XmlReader.Create(input, settings)
            While reader.Read()
                token.ThrowIfCancellationRequested()
                If reader.Depth > MaxXmlDepth Then Throw New InvalidDataException("EPUB XML nesting exceeds the limit.")
            End While
        End Using
        Using input As New MemoryStream(bytes), reader = XmlReader.Create(input, settings)
            Return XDocument.Load(reader)
        End Using
    End Function

    Public Shared Function Read(stream As Stream, Optional token As CancellationToken = Nothing) As EpubPackage
        ValidateDirectory(stream, token)
        Using archive As New ZipArchive(stream, ZipArchiveMode.Read, leaveOpen:=True)
            Dim items = Entries(archive)
            If items.ContainsKey("META-INF/rights.xml") Then Throw New NotSupportedException("DRM-protected EPUB must be opened in an authorized external reader.")
            If items.ContainsKey("META-INF/encryption.xml") Then
                Dim encryption = Xml(items, "META-INF/encryption.xml", token)
                For Each method In encryption.Descendants().Where(Function(item) item.Name.LocalName = "EncryptionMethod")
                    Dim algorithm = CStr(method.Attribute("Algorithm"))
                    If algorithm <> "http://www.idpf.org/2008/embedding" AndAlso algorithm <> "http://ns.adobe.com/pdf/enc#RC" Then Throw New NotSupportedException("DRM-protected EPUB must be opened in an authorized external reader.")
                Next
            End If
            Dim container = Xml(items, "META-INF/container.xml", token)
            Dim root = container.Descendants().FirstOrDefault(Function(item) item.Name.LocalName = "rootfile" AndAlso CStr(item.Attribute("media-type")) = "application/oebps-package+xml")
            If root Is Nothing Then root = container.Descendants().FirstOrDefault(Function(item) item.Name.LocalName = "rootfile")
            If root Is Nothing Then Throw New InvalidDataException("EPUB has no package root.")
            Dim opfKey = ResolveKey("", CStr(root.Attribute("full-path")))
            Dim opf = Xml(items, opfKey, token)
            Dim book As New EpubPackage()
            Dim title = opf.Descendants().FirstOrDefault(Function(item) item.Name.LocalName = "title")
            Dim author = opf.Descendants().FirstOrDefault(Function(item) item.Name.LocalName = "creator")
            book.Title = If(title Is Nothing, "", title.Value.Trim())
            book.Author = If(author Is Nothing, "", author.Value.Trim())
            Dim fixed = opf.Descendants().Any(Function(item) item.Name.LocalName = "meta" AndAlso CStr(item.Attribute("property")) = "rendition:layout" AndAlso item.Value.Trim() = "pre-paginated")
            For Each item In opf.Descendants().Where(Function(node) node.Name.LocalName = "manifest").Elements().Where(Function(node) node.Name.LocalName = "item")
                token.ThrowIfCancellationRequested()
                Dim resource As New EpubResource With {.Id = CStr(item.Attribute("id")), .Key = ResolveKey(opfKey, CStr(item.Attribute("href"))),
                    .MediaType = If(CStr(item.Attribute("media-type")), ""), .Properties = If(CStr(item.Attribute("properties")), ""), .FixedLayout = fixed}
                If String.IsNullOrWhiteSpace(resource.Id) OrElse book.Resources.ContainsKey(resource.Id) OrElse Not items.ContainsKey(resource.Key) Then Throw New InvalidDataException("Invalid EPUB manifest.")
                book.Resources.Add(resource.Id, resource)
            Next
            Dim spine = opf.Descendants().FirstOrDefault(Function(item) item.Name.LocalName = "spine")
            If spine Is Nothing Then Throw New InvalidDataException("EPUB has no spine.")
            book.RightToLeft = CStr(spine.Attribute("page-progression-direction")) = "rtl"
            For Each item In spine.Elements().Where(Function(node) node.Name.LocalName = "itemref")
                Dim resource As EpubResource = Nothing
                Dim id = CStr(item.Attribute("idref"))
                If id Is Nothing OrElse Not book.Resources.TryGetValue(id, resource) Then Throw New InvalidDataException("EPUB spine references an unknown item.")
                If resource.MediaType <> "application/xhtml+xml" AndAlso resource.MediaType <> "image/svg+xml" Then Throw New NotSupportedException("Unsupported EPUB spine media type.")
                Dim props = If(CStr(item.Attribute("properties")), "")
                If props.Contains("rendition:layout-pre-paginated") Then resource.FixedLayout = True
                If props.Contains("rendition:layout-reflowable") Then resource.FixedLayout = False
                If CStr(item.Attribute("linear")) <> "no" Then book.Spine.Add(resource)
            Next
            If book.Spine.Count = 0 Then Throw New InvalidDataException("EPUB has no readable spine items.")
            Dim nav = book.Resources.Values.FirstOrDefault(Function(item) item.Properties.Split(" "c).Contains("nav"))
            If nav IsNot Nothing Then
                Dim doc = Xml(items, nav.Key, token)
                Dim toc = doc.Descendants().FirstOrDefault(Function(node) node.Name.LocalName = "nav" AndAlso node.Attributes().Any(Function(attr) attr.Name.LocalName = "type" AndAlso attr.Value.Split(" "c).Contains("toc")))
                If toc IsNot Nothing Then book.Toc.AddRange(ReadNav(toc, nav.Key, 0))
            Else
                Dim ncx As EpubResource = Nothing
                Dim tocId = CStr(spine.Attribute("toc"))
                If tocId IsNot Nothing Then book.Resources.TryGetValue(tocId, ncx)
                If ncx Is Nothing Then ncx = book.Resources.Values.FirstOrDefault(Function(item) item.MediaType = "application/x-dtbncx+xml")
                If ncx IsNot Nothing Then
                    Dim doc = Xml(items, ncx.Key, token)
                    Dim map = doc.Descendants().FirstOrDefault(Function(node) node.Name.LocalName = "navMap")
                    If map IsNot Nothing Then book.Toc.AddRange(ReadNcx(map, ncx.Key, 0))
                End If
            End If
            If book.Toc.Count = 0 Then
                For Each chapter In book.Spine
                    book.Toc.Add(New EpubTocItem With {.Label = chapter.Id, .Key = chapter.Key})
                Next
            End If
            Return book
        End Using
    End Function

    Private Shared Function Location(label As String, base As String, href As String) As EpubTocItem
        Dim fragment = href.IndexOf("#"c)
        Return New EpubTocItem With {.Label = label.Trim(), .Key = ResolveKey(base, href), .Anchor = If(fragment < 0, "", Uri.UnescapeDataString(href.Substring(fragment + 1)))}
    End Function

    Private Shared Function ReadNav(parent As XElement, key As String, depth As Integer) As List(Of EpubTocItem)
        Dim result As New List(Of EpubTocItem)()
        If depth >= 32 Then Return result
        For Each list In parent.Elements().Where(Function(node) node.Name.LocalName = "ol")
            For Each li In list.Elements().Where(Function(node) node.Name.LocalName = "li")
                Dim anchor = li.Elements().FirstOrDefault(Function(node) node.Name.LocalName = "a" AndAlso node.Attribute("href") IsNot Nothing)
                Dim item = If(anchor Is Nothing, New EpubTocItem With {.Label = li.Elements().FirstOrDefault()?.Value}, Location(anchor.Value, key, CStr(anchor.Attribute("href"))))
                item.Children = ReadNav(li, key, depth + 1)
                result.Add(item)
            Next
        Next
        Return result
    End Function

    Private Shared Function ReadNcx(parent As XElement, key As String, depth As Integer) As List(Of EpubTocItem)
        Dim result As New List(Of EpubTocItem)()
        If depth >= 32 Then Return result
        For Each point In parent.Elements().Where(Function(node) node.Name.LocalName = "navPoint")
            Dim content = point.Elements().FirstOrDefault(Function(node) node.Name.LocalName = "content")
            Dim label = point.Elements().FirstOrDefault(Function(node) node.Name.LocalName = "navLabel")
            If content Is Nothing OrElse content.Attribute("src") Is Nothing Then Continue For
            Dim item = Location(If(label Is Nothing, "", label.Value), key, CStr(content.Attribute("src")))
            item.Children = ReadNcx(point, key, depth + 1)
            result.Add(item)
        Next
        Return result
    End Function

    Public Shared Function ReadResource(stream As Stream, key As String, Optional token As CancellationToken = Nothing) As Byte()
        ValidateDirectory(stream, token)
        Using archive As New ZipArchive(stream, ZipArchiveMode.Read, leaveOpen:=True)
            Dim items = Entries(archive)
            Dim entry As ZipArchiveEntry = Nothing
            If Not items.TryGetValue(key, entry) Then Throw New FileNotFoundException("EPUB resource is missing.")
            Return ReadEntry(entry, MaxResourceBytes, token)
        End Using
    End Function

    Public Function Search(stream As Stream, query As String, caseSensitive As Boolean,
                           Optional currentChapter As String = Nothing, Optional token As CancellationToken = Nothing) As List(Of EpubSearchHit)
        Dim result As New List(Of EpubSearchHit)()
        If String.IsNullOrWhiteSpace(query) Then Return result
        ValidateDirectory(stream, token)
        Using archive As New ZipArchive(stream, ZipArchiveMode.Read, leaveOpen:=True)
            Dim items = Entries(archive)
            For Each chapter In Spine
                token.ThrowIfCancellationRequested()
                If currentChapter IsNot Nothing AndAlso chapter.Key <> currentChapter Then Continue For
                Dim document = Xml(items, chapter.Key, token)
                Dim body = document.Descendants().FirstOrDefault(Function(node) node.Name.LocalName = "body")
                If body Is Nothing Then body = document.Root
                Dim text = String.Join(" ", body.DescendantNodes().OfType(Of XText)().Where(
                    Function(node) Not node.Ancestors().Any(Function(parent) {"script", "style", "noscript"}.Contains(parent.Name.LocalName))).Select(Function(node) node.Value))
                For Each offset In DocumentReadingState.FindMatches(text, query, caseSensitive, 500 - result.Count)
                    Dim start = Math.Max(0, offset - 30)
                    result.Add(New EpubSearchHit With {.SpineKey = chapter.Key, .TextOffset = offset, .Snippet = text.Substring(start, Math.Min(100, text.Length - start))})
                Next
                If result.Count >= 500 Then Exit For
            Next
        End Using
        Return result
    End Function
End Class
#End If
