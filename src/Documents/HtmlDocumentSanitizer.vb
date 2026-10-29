#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports AngleSharp.Dom
Imports AngleSharp.Html.Parser

' Specification 275 section 10.4: HTML5 parse from bytes, active-markup removal and UTF-8
' serialization. AngleSharp HtmlParser only - no browsing context, loader or scripting.
' Never regex over markup: the tree is walked iteratively and edited node by node.

Friend NotInheritable Class HtmlSanitizeOptions
    ' Session origin, e.g. https://sabc.fms-doc.invalid/ (local) or the real page origin (online).
    Public Property Origin As Uri
    ' Logical key of the document ("dir/page.html"); the base for relative references.
    Public Property EntryKey As String = ""
    ' HTTP charset of an online response; "" for local files.
    Public Property TransportCharset As String = ""
    Public Property Profile As HtmlSourceKind = HtmlSourceKind.LocalFile
    ' Extra http(s) origins (scheme://host[:port], lowercase) whose resources may stay referenced.
    Public Property ApprovedOrigins As ISet(Of String)
End Class

Friend NotInheritable Class HtmlSanitizeResult
    Public Property Status As HtmlOutcome
    ' UTF-8 without BOM, starting with the HTML5 doctype.
    Public Property Bytes As Byte() = Array.Empty(Of Byte)()
    Public Property Title As String = ""
    Public Property Language As String = ""
    ' "ltr", "rtl", "auto" or "".
    Public Property Direction As String = ""
    ' Web name of the encoding the source was decoded with.
    Public Property CharacterSet As String = ""
    ' External http(s) resource references removed (not counting links the reader may click).
    Public Property BlockedResources As Integer
    ' Scripts, frames, embeds, media and form controls removed or flattened.
    Public Property RemovedActiveElements As Integer
    ' Distinct lowercase origins of the blocked external references, in document order.
    Public Property ExternalOrigins As IReadOnlyList(Of String) = Array.Empty(Of String)()
    ' Elements of the final tree.
    Public Property ElementCount As Integer
End Class

Friend Module HtmlDocumentSanitizer
    Private Const MaxInputBytes As Integer = 16 * 1024 * 1024
    Private Const MaxElements As Integer = 100000
    Private Const MaxTokens As Integer = 250000
    Private Const MaxNodes As Integer = 250000
    Private Const MaxDepth As Integer = 512
    Private Const MaxDataImageChars As Integer = 4 * 1024 * 1024
    Private Const MaxExternalOrigins As Integer = 256
    Private Const PrescanBytes As Integer = 8192
    Private Const MaxTitleChars As Integer = 512

    Private Const HtmlNs As String = "http://www.w3.org/1999/xhtml"
    Private Const SvgNs As String = "http://www.w3.org/2000/svg"
    Private Const MathNs As String = "http://www.w3.org/1998/Math/MathML"

    Private ReadOnly Utf8Input As New UTF8Encoding(False, False)
    Private ReadOnly Utf8Output As New UTF8Encoding(False)

    ' Attributes that never survive, on any element.
    Private ReadOnly AlwaysRemovedAttributes As New HashSet(Of String)(StringComparer.Ordinal) From {
        "ping", "download", "autofocus", "contenteditable", "formaction", "formtarget", "formmethod", "formenctype",
        "formnovalidate", "target", "srcdoc", "nonce", "integrity", "popover", "popovertarget", "popovertargetaction",
        "action", "longdesc", "lowsrc", "dynsrc", "manifest", "codebase", "archive", "classid", "contextmenu"}

    ' A stylesheet link carrying any of these is a different kind of link.
    Private ReadOnly NonStylesheetRels As New HashSet(Of String)(StringComparer.Ordinal) From {
        "alternate", "preload", "prefetch", "modulepreload", "preconnect", "dns-prefetch", "prerender", "import",
        "manifest", "serviceworker", "pingback", "icon"}

    Private ReadOnly RasterDataTypes As New HashSet(Of String)(StringComparer.Ordinal) From {
        "image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp", "image/bmp", "image/avif", "image/apng",
        "image/x-icon", "image/vnd.microsoft.icon"}

    Private ReadOnly SafeCopiedAttributes As New HashSet(Of String)(StringComparer.Ordinal) From {
        "id", "class", "style", "title", "lang", "dir", "hidden", "role"}

    Friend Function Sanitize(bytes As Byte(), options As HtmlSanitizeOptions, token As CancellationToken) As HtmlSanitizeResult
        Try
            If bytes Is Nothing Then bytes = Array.Empty(Of Byte)()
            If options Is Nothing Then options = New HtmlSanitizeOptions()
            If bytes.Length > MaxInputBytes Then Return Failed(HtmlOutcome.LimitExceeded)

            Dim decoded = Decode(bytes, options.TransportCharset)
            If decoded Is Nothing Then Return Failed(HtmlOutcome.InvalidEncoding)

            Dim budget As New ParseBudget(token)
            Dim parserOptions As New HtmlParserOptions With {
                .IsScripting = False,
                .SkipComments = True,
                .OnCreated = Sub(element, position) budget.OnElement(element),
                .OnToken = Sub(parsedToken, position) budget.OnToken()}
            Dim parser As New HtmlParser(parserOptions)

            Using document = parser.ParseDocument(decoded.Text)
                If document.DocumentElement Is Nothing Then Return Failed(HtmlOutcome.InvalidMarkup)

                Dim walker As New SanitizeWalker(document, options, token)
                walker.Run(document)
                EnsureCharsetMeta(document)

                Dim elements = 0
                Dim nodes = 0
                CountTree(document, token, elements, nodes)
                If elements > MaxElements OrElse nodes > MaxNodes Then Return Failed(HtmlOutcome.LimitExceeded)

                Dim html = document.DocumentElement
                Dim result As New HtmlSanitizeResult With {
                    .Status = HtmlOutcome.Ok,
                    .Bytes = Utf8Output.GetBytes("<!DOCTYPE html>" & html.OuterHtml),
                    .Title = ReadTitle(document),
                    .Language = ReadLanguage(html),
                    .Direction = ReadDirection(html, document.Body),
                    .CharacterSet = decoded.Name,
                    .BlockedResources = walker.BlockedResources,
                    .RemovedActiveElements = walker.RemovedActive,
                    .ExternalOrigins = walker.ExternalOrigins,
                    .ElementCount = elements}
                Return result
            End Using
        Catch ex As OperationCanceledException
            Return Failed(HtmlOutcome.Cancelled)
        Catch ex As BudgetExceededException
            Return Failed(HtmlOutcome.LimitExceeded)
        Catch ex As Exception
            Return Failed(HtmlOutcome.InvalidMarkup)
        End Try
    End Function

    Private Function Failed(status As HtmlOutcome) As HtmlSanitizeResult
        Return New HtmlSanitizeResult With {.Status = status}
    End Function

    Private NotInheritable Class BudgetExceededException
        Inherits Exception
    End Class

    ' Parser callbacks: counted while the tree is built, so a hostile document is refused
    ' before it is fully allocated.
    Private NotInheritable Class ParseBudget
        Private ReadOnly _token As CancellationToken
        Private _elements As Integer
        Private _tokens As Integer

        Friend Sub New(token As CancellationToken)
            _token = token
        End Sub

        Friend Sub OnToken()
            _tokens += 1
            If _tokens > MaxTokens Then Throw New BudgetExceededException()
            _token.ThrowIfCancellationRequested()
        End Sub

        Friend Sub OnElement(element As IElement)
            _elements += 1
            If _elements > MaxElements Then Throw New BudgetExceededException()
            ' Sampled so a very deep nesting is refused before the parser's own quadratic scans pile up.
            If (_elements And 255) = 0 AndAlso element IsNot Nothing Then
                Dim depth = 0
                Dim parent = element.ParentElement
                While parent IsNot Nothing
                    depth += 1
                    If depth > MaxDepth Then Throw New BudgetExceededException()
                    parent = parent.ParentElement
                End While
            End If
        End Sub
    End Class

#Region "Encoding"

    Private NotInheritable Class DecodedHtml
        Friend Text As String = ""
        Friend Name As String = ""
    End Class

    ' BOM, then the transport charset, then (BOM-less UTF-16 sniff and) the meta prescan, then
    ' the fallback. An unknown declared charset falls through to the next source like a browser.
    Private Function Decode(bytes As Byte(), transportCharset As String) As DecodedHtml
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
        Dim enc As Encoding
        Dim skip = 0
        If bytes.Length >= 3 AndAlso bytes(0) = &HEF AndAlso bytes(1) = &HBB AndAlso bytes(2) = &HBF Then
            enc = Utf8Input
            skip = 3
        ElseIf bytes.Length >= 2 AndAlso bytes(0) = &HFF AndAlso bytes(1) = &HFE Then
            enc = New UnicodeEncoding(False, False, False)
            skip = 2
        ElseIf bytes.Length >= 2 AndAlso bytes(0) = &HFE AndAlso bytes(1) = &HFF Then
            enc = New UnicodeEncoding(True, False, False)
            skip = 2
        Else
            enc = ResolveLabel(transportCharset)
            If enc Is Nothing Then enc = SniffUtf16(bytes)
            If enc Is Nothing Then enc = PrescanMeta(bytes)
            If enc Is Nothing Then enc = If(System.Text.Unicode.Utf8.IsValid(bytes), Utf8Input, Windows1252())
        End If

        Dim text = enc.GetString(bytes, skip, bytes.Length - skip)
        ' Binary data decodes into mostly NUL characters; that is not a document.
        Dim nulls = 0
        For Each ch In text
            If ch = ChrW(0) Then nulls += 1
        Next
        If nulls > 8 AndAlso nulls * 100 >= text.Length Then Return Nothing
        Return New DecodedHtml With {.Text = text, .Name = enc.WebName}
    End Function

    Private Function Windows1252() As Encoding
        Return Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback)
    End Function

    ' WHATWG label to a decoding with replacement fallback; Nothing when the label is unknown or
    ' names an encoding the HTML standard does not allow (UTF-7, UTF-32).
    Private Function ResolveLabel(label As String) As Encoding
        If label Is Nothing Then Return Nothing
        Dim name = label.Trim().Trim("'"c, """"c).Trim().ToLowerInvariant()
        If name.Length = 0 OrElse name.Length > 64 Then Return Nothing
        Select Case name
            Case "utf-8", "utf8", "unicode-1-1-utf-8", "unicode11utf8", "x-unicode20utf8"
                Return Utf8Input
            Case "utf-16", "utf-16le", "ucs-2", "unicode", "csunicode", "iso-10646-ucs-2", "unicodefeff"
                Return New UnicodeEncoding(False, False, False)
            Case "utf-16be", "unicodefffe"
                Return New UnicodeEncoding(True, False, False)
            Case "iso-8859-1", "latin1", "l1", "ascii", "us-ascii", "iso8859-1", "iso_8859-1", "iso-ir-100", "cp819", "ibm819",
                 "cp1252", "x-cp1252", "windows-1252", "ansi_x3.4-1968", "csisolatin1", "x-user-defined"
                Return Windows1252()
            Case "gb2312", "gbk", "x-gbk", "gb18030", "chinese", "csgb2312", "csiso58gb231280", "gb_2312", "gb_2312-80", "iso-ir-58"
                Return Encoding.GetEncoding(54936, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback)
            Case "shift_jis", "shift-jis", "sjis", "x-sjis", "ms_kanji", "csshiftjis", "windows-31j", "ms932"
                Return Encoding.GetEncoding(932, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback)
            Case "utf-7", "utf-32", "utf-32le", "utf-32be", "replacement"
                Return Nothing
        End Select
        Try
            Dim found = Encoding.GetEncoding(name, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback)
            If found.CodePage = 65000 OrElse found.CodePage = 12000 OrElse found.CodePage = 12001 Then Return Nothing
            Return found
        Catch ex As ArgumentException
            Return Nothing
        Catch ex As NotSupportedException
            Return Nothing
        End Try
    End Function

    ' A BOM-less UTF-16 document starts with ASCII markup, so one byte of every pair is NUL.
    Private Function SniffUtf16(bytes As Byte()) As Encoding
        Dim count = Math.Min(bytes.Length, 256) \ 2
        If count < 2 Then Return Nothing
        Dim evenNul = 0
        Dim oddNul = 0
        For i = 0 To count - 1
            If bytes(2 * i) = 0 Then evenNul += 1
            If bytes(2 * i + 1) = 0 Then oddNul += 1
        Next
        If oddNul * 10 >= count * 4 AndAlso evenNul * 20 < count Then Return New UnicodeEncoding(False, False, False)
        If evenNul * 10 >= count * 4 AndAlso oddNul * 20 < count Then Return New UnicodeEncoding(True, False, False)
        Return Nothing
    End Function

    Private Function IsAsciiSpace(b As Byte) As Boolean
        Return b = 9 OrElse b = 10 OrElse b = 12 OrElse b = 13 OrElse b = 32
    End Function

    Private Function LowerAscii(b As Byte) As Byte
        Return If(b >= 65 AndAlso b <= 90, CByte(b + 32), b)
    End Function

    Private Function StartsWithAscii(bytes As Byte(), index As Integer, limit As Integer, word As String) As Boolean
        If index + word.Length > limit Then Return False
        For i = 0 To word.Length - 1
            If LowerAscii(bytes(index + i)) <> AscW(word(i)) Then Return False
        Next
        Return True
    End Function

    ' Byte-level prescan of the head for a meta charset declaration (HTML standard "prescan a byte
    ' stream"): attributes of every tag are read, comments are skipped, the first meta whose label
    ' names a known encoding wins.
    Private Function PrescanMeta(bytes As Byte()) As Encoding
        Dim limit = Math.Min(bytes.Length, PrescanBytes)
        Dim i = 0
        While i < limit
            If bytes(i) <> AscW("<"c) Then
                i += 1
                Continue While
            End If
            If StartsWithAscii(bytes, i, limit, "<!--") Then
                i += 4
                While i < limit AndAlso Not (bytes(i) = AscW("-"c) AndAlso i + 2 < limit AndAlso bytes(i + 1) = AscW("-"c) AndAlso bytes(i + 2) = AscW(">"c))
                    i += 1
                End While
                i += 3
                Continue While
            End If
            Dim isMeta = StartsWithAscii(bytes, i, limit, "<meta") AndAlso i + 5 < limit AndAlso (IsAsciiSpace(bytes(i + 5)) OrElse bytes(i + 5) = AscW("/"c))
            If isMeta OrElse (i + 1 < limit AndAlso ((bytes(i + 1) >= 65 AndAlso bytes(i + 1) <= 90) OrElse (bytes(i + 1) >= 97 AndAlso bytes(i + 1) <= 122))) Then
                i += If(isMeta, 5, 1)
                If Not isMeta Then
                    While i < limit AndAlso Not IsAsciiSpace(bytes(i)) AndAlso bytes(i) <> AscW(">"c) AndAlso bytes(i) <> AscW("/"c)
                        i += 1
                    End While
                End If
                Dim charsetLabel As String = Nothing
                Dim contentLabel As String = Nothing
                Dim pragma = False
                Dim name As String = Nothing
                Dim value As String = Nothing
                While ReadAttribute(bytes, i, limit, name, value)
                    Select Case name
                        Case "charset"
                            If charsetLabel Is Nothing Then charsetLabel = value
                        Case "content"
                            If contentLabel Is Nothing Then contentLabel = CharsetFromContent(value)
                        Case "http-equiv"
                            If String.Equals(value, "content-type", StringComparison.OrdinalIgnoreCase) Then pragma = True
                    End Select
                End While
                If isMeta Then
                    Dim label = If(charsetLabel, If(pragma, contentLabel, Nothing))
                    If label IsNot Nothing Then
                        Dim found = ResolveLabel(label)
                        If found IsNot Nothing Then
                            ' A page that declares UTF-16 in an ASCII-compatible byte stream is UTF-8.
                            If found.CodePage = 1200 OrElse found.CodePage = 1201 Then Return Utf8Input
                            Return found
                        End If
                    End If
                End If
                Continue While
            End If
            ' <!doctype>, </x>, <?pi>: skip to the closing bracket.
            While i < limit AndAlso bytes(i) <> AscW(">"c)
                i += 1
            End While
            i += 1
        End While
        Return Nothing
    End Function

    ' Reads one attribute; False at the end of the tag or the scanned area.
    Private Function ReadAttribute(bytes As Byte(), ByRef i As Integer, limit As Integer, ByRef name As String, ByRef value As String) As Boolean
        While i < limit AndAlso (IsAsciiSpace(bytes(i)) OrElse bytes(i) = AscW("/"c))
            i += 1
        End While
        If i >= limit OrElse bytes(i) = AscW(">"c) Then
            i += 1
            Return False
        End If
        Dim nameBuilder As New StringBuilder()
        While i < limit AndAlso bytes(i) <> AscW("="c) AndAlso Not IsAsciiSpace(bytes(i)) AndAlso bytes(i) <> AscW("/"c) AndAlso bytes(i) <> AscW(">"c)
            nameBuilder.Append(ChrW(LowerAscii(bytes(i))))
            i += 1
        End While
        name = nameBuilder.ToString()
        value = ""
        While i < limit AndAlso IsAsciiSpace(bytes(i))
            i += 1
        End While
        If i >= limit OrElse bytes(i) <> AscW("="c) Then Return True
        i += 1
        While i < limit AndAlso IsAsciiSpace(bytes(i))
            i += 1
        End While
        Dim valueBuilder As New StringBuilder()
        If i < limit AndAlso (bytes(i) = AscW(""""c) OrElse bytes(i) = AscW("'"c)) Then
            Dim quote = bytes(i)
            i += 1
            While i < limit AndAlso bytes(i) <> quote
                valueBuilder.Append(ChrW(LowerAscii(bytes(i))))
                i += 1
            End While
            i += 1
        Else
            While i < limit AndAlso Not IsAsciiSpace(bytes(i)) AndAlso bytes(i) <> AscW(">"c)
                valueBuilder.Append(ChrW(LowerAscii(bytes(i))))
                i += 1
            End While
        End If
        value = valueBuilder.ToString()
        Return True
    End Function

    ' "text/html; charset=windows-1251" -> "windows-1251".
    Private Function CharsetFromContent(content As String) As String
        Dim at = content.IndexOf("charset", StringComparison.Ordinal)
        While at >= 0
            Dim p = at + 7
            While p < content.Length AndAlso content(p) <= " "c
                p += 1
            End While
            If p < content.Length AndAlso content(p) = "="c Then
                p += 1
                While p < content.Length AndAlso content(p) <= " "c
                    p += 1
                End While
                Dim quote = If(p < content.Length AndAlso (content(p) = """"c OrElse content(p) = "'"c), content(p), ChrW(0))
                If quote <> ChrW(0) Then p += 1
                Dim startAt = p
                While p < content.Length AndAlso content(p) <> quote AndAlso content(p) > " "c AndAlso content(p) <> ";"c
                    p += 1
                End While
                Return content.Substring(startAt, p - startAt)
            End If
            at = content.IndexOf("charset", at + 7, StringComparison.Ordinal)
        End While
        Return Nothing
    End Function

#End Region

#Region "Document facts"

    Private Sub EnsureCharsetMeta(document As IDocument)
        Dim head = document.Head
        If head Is Nothing Then Return
        Dim meta = document.CreateElement("meta")
        meta.SetAttribute("charset", "utf-8")
        If head.FirstChild Is Nothing Then
            head.AppendChild(meta)
        Else
            head.InsertBefore(meta, head.FirstChild)
        End If
    End Sub

    Private Function CollapseSpace(text As String, maxChars As Integer) As String
        Dim sb As New StringBuilder()
        Dim pendingSpace = False
        For Each ch In text
            If ch = " "c OrElse ch = ControlChars.Tab OrElse ch = ControlChars.Lf OrElse ch = ControlChars.Cr OrElse ch = ControlChars.FormFeed Then
                pendingSpace = sb.Length > 0
            Else
                If pendingSpace Then sb.Append(" "c)
                pendingSpace = False
                sb.Append(ch)
            End If
            If sb.Length >= maxChars Then Exit For
        Next
        Dim result = sb.ToString()
        If result.Length > 0 AndAlso Char.IsHighSurrogate(result(result.Length - 1)) Then result = result.Substring(0, result.Length - 1)
        Return result
    End Function

    Private Function ReadTitle(document As IDocument) As String
        Dim head = document.Head
        Dim child = If(head Is Nothing, Nothing, head.FirstElementChild)
        While child IsNot Nothing
            If child.NamespaceUri = HtmlNs AndAlso String.Equals(child.LocalName, "title", StringComparison.OrdinalIgnoreCase) Then
                Return CollapseSpace(child.TextContent, MaxTitleChars)
            End If
            child = child.NextElementSibling
        End While
        Return ""
    End Function

    Private Function ReadLanguage(html As IElement) As String
        Dim lang = If(html.GetAttribute("lang"), If(html.GetAttribute("xml:lang"), "")).Trim()
        Return If(lang.Length > 64, "", lang)
    End Function

    Private Function ReadDirection(html As IElement, body As IElement) As String
        Dim dir = NormalizeDirection(html.GetAttribute("dir"))
        If dir.Length = 0 AndAlso body IsNot Nothing Then dir = NormalizeDirection(body.GetAttribute("dir"))
        Return dir
    End Function

    Private Function NormalizeDirection(value As String) As String
        If value Is Nothing Then Return ""
        Dim v = value.Trim().ToLowerInvariant()
        Return If(v = "ltr" OrElse v = "rtl" OrElse v = "auto", v, "")
    End Function

    ' Final tree re-count (iterative: depth must not matter).
    Private Sub CountTree(root As INode, token As CancellationToken, ByRef elements As Integer, ByRef nodes As Integer)
        Dim node = root.FirstChild
        Dim visited = 0
        While node IsNot Nothing
            visited += 1
            If (visited And 1023) = 0 Then token.ThrowIfCancellationRequested()
            nodes += 1
            If node.NodeType = NodeType.Element Then elements += 1
            If node.FirstChild IsNot Nothing Then
                node = node.FirstChild
            Else
                Dim n = node
                node = Nothing
                While n IsNot Nothing AndAlso n IsNot root
                    If n.NextSibling IsNot Nothing Then
                        node = n.NextSibling
                        Exit While
                    End If
                    n = n.Parent
                End While
            End If
        End While
    End Sub

#End Region

#Region "References"

    Private Enum RefKind
        Empty
        Fragment
        SameOrigin
        Approved
        External
        Mail
        Data
        [Unsafe]
    End Enum

    Private Structure Reference
        Friend ReadOnly Kind As RefKind
        Friend ReadOnly Origin As String

        Friend Sub New(kind As RefKind, Optional origin As String = "")
            Me.Kind = kind
            Me.Origin = origin
        End Sub
    End Structure

    Private Function IsSchemeName(text As String) As Boolean
        If text.Length = 0 OrElse Not ((text(0) >= "a"c AndAlso text(0) <= "z"c) OrElse (text(0) >= "A"c AndAlso text(0) <= "Z"c)) Then Return False
        For Each ch In text
            If Not ((ch >= "a"c AndAlso ch <= "z"c) OrElse (ch >= "A"c AndAlso ch <= "Z"c) OrElse (ch >= "0"c AndAlso ch <= "9"c) OrElse ch = "+"c OrElse ch = "-"c OrElse ch = "."c) Then Return False
        Next
        Return True
    End Function

    ' What the URL parser does before it looks at the scheme: tab/CR/LF vanish, edge C0/space trim.
    Private Function NormalizeReference(raw As String) As String
        If raw Is Nothing Then Return ""
        Dim sb As New StringBuilder(raw.Length)
        For Each ch In raw
            If ch <> ControlChars.Tab AndAlso ch <> ControlChars.Lf AndAlso ch <> ControlChars.Cr Then sb.Append(ch)
        Next
        Dim s = sb.ToString()
        Dim startAt = 0
        Dim endAt = s.Length
        While startAt < endAt AndAlso s(startAt) <= " "c
            startAt += 1
        End While
        While endAt > startAt AndAlso s(endAt - 1) <= " "c
            endAt -= 1
        End While
        Return s.Substring(startAt, endAt - startAt)
    End Function

    Private Function IsAllowedDataImage(normalized As String) As Boolean
        If normalized.Length > MaxDataImageChars OrElse Not normalized.StartsWith("data:", StringComparison.OrdinalIgnoreCase) Then Return False
        Dim comma = normalized.IndexOf(","c)
        If comma < 0 Then Return False
        Dim header = normalized.Substring(5, comma - 5)
        Dim semi = header.IndexOf(";"c)
        Dim mediaType = (If(semi >= 0, header.Substring(0, semi), header)).Trim().ToLowerInvariant()
        Return RasterDataTypes.Contains(mediaType)
    End Function

    Private Function OriginOf(uri As Uri) As String
        Dim host = uri.IdnHost.ToLowerInvariant()
        If host.IndexOf(":"c) >= 0 AndAlso Not host.StartsWith("[", StringComparison.Ordinal) Then host = "[" & host & "]"
        Return uri.Scheme & "://" & host & If(uri.IsDefaultPort, "", ":" & uri.Port.ToString(Globalization.CultureInfo.InvariantCulture))
    End Function

    Private NotInheritable Class SanitizeWalker
        Private ReadOnly _document As IDocument
        Private ReadOnly _options As HtmlSanitizeOptions
        Private ReadOnly _token As CancellationToken
        Private ReadOnly _origin As Uri
        Private ReadOnly _documentUri As Uri
        Private ReadOnly _externalSeen As New HashSet(Of String)(StringComparer.Ordinal)
        Private ReadOnly _externalOrder As New List(Of String)()
        Private _depth As Integer
        Private _baseSeen As Boolean

        Friend BlockedResources As Integer
        Friend RemovedActive As Integer

        Friend ReadOnly Property ExternalOrigins As IReadOnlyList(Of String)
            Get
                Return _externalOrder
            End Get
        End Property

        Friend Sub New(document As IDocument, options As HtmlSanitizeOptions, token As CancellationToken)
            _document = document
            _options = options
            _token = token
            _origin = options.Origin
            If _origin IsNot Nothing AndAlso _origin.IsAbsoluteUri Then
                Dim root As Uri = Nothing
                Dim key = If(options.EntryKey, "").Replace("\"c, "/"c).TrimStart("/"c)
                Dim path = String.Join("/", key.Split("/"c).Select(Function(segment) Uri.EscapeDataString(segment)))
                If Uri.TryCreate(OriginOf(_origin) & "/", UriKind.Absolute, root) Then Uri.TryCreate(root, path, _documentUri)
            End If
        End Sub

        Friend Sub Run(root As INode)
            _depth = 0
            Dim visited = 0
            Dim node As INode = root.FirstChild
            While node IsNot Nothing
                visited += 1
                If (visited And 511) = 0 Then _token.ThrowIfCancellationRequested()
                If _depth > MaxDepth Then Throw New BudgetExceededException()
                Select Case node.NodeType
                    Case NodeType.Element
                        node = VisitElement(DirectCast(node, IElement), root)
                    Case NodeType.Comment, NodeType.ProcessingInstruction
                        node = RemoveNode(node, root)
                    Case Else
                        node = NextInOrder(node, root)
                End Select
            End While
        End Sub

#Region "Traversal"

        Private Function NextInOrder(node As INode, root As INode) As INode
            If node.FirstChild IsNot Nothing Then
                _depth += 1
                Return node.FirstChild
            End If
            Return Following(node, root)
        End Function

        ' The node after this subtree in document order; keeps _depth in step with the climb.
        Private Function Following(node As INode, root As INode) As INode
            Dim n = node
            While n IsNot Nothing AndAlso n IsNot root
                If n.NextSibling IsNot Nothing Then Return n.NextSibling
                n = n.Parent
                _depth -= 1
            End While
            Return Nothing
        End Function

        Private Function RemoveNode(node As INode, root As INode) As INode
            Dim nextNode = Following(node, root)
            node.Parent.RemoveChild(node)
            Return nextNode
        End Function

        ' Children take the element's place; the walk continues at the first of them.
        Private Function Unwrap(element As IElement, root As INode) As INode
            Dim parent = element.Parent
            Dim first = element.FirstChild
            If first Is Nothing Then Return RemoveNode(element, root)
            Dim child = first
            Dim moved = 0
            While child IsNot Nothing
                Dim nextChild = child.NextSibling
                parent.InsertBefore(child, element)
                child = nextChild
                moved += 1
                If (moved And 255) = 0 Then _token.ThrowIfCancellationRequested()
            End While
            parent.RemoveChild(element)
            Return first
        End Function

        Private Function ReplaceKeepingChildren(element As IElement, newName As String, root As INode) As INode
            Dim replacement = _document.CreateElement(newName)
            CopySafeAttributes(element, replacement)
            Dim parent = element.Parent
            parent.InsertBefore(replacement, element)
            Dim child = element.FirstChild
            Dim moved = 0
            While child IsNot Nothing
                Dim nextChild = child.NextSibling
                replacement.AppendChild(child)
                child = nextChild
                moved += 1
                If (moved And 255) = 0 Then _token.ThrowIfCancellationRequested()
            End While
            parent.RemoveChild(element)
            Return NextInOrder(replacement, root)
        End Function

        Private Function ReplaceWithText(element As IElement, text As String, root As INode, Optional preWrap As Boolean = False) As INode
            Dim span = _document.CreateElement("span")
            CopySafeAttributes(element, span)
            If preWrap Then
                Dim existing = If(span.GetAttribute("style"), "").Trim()
                If existing.Length > 0 AndAlso Not existing.EndsWith(";", StringComparison.Ordinal) Then existing &= ";"
                span.SetAttribute("style", existing & "white-space:pre-wrap")
            End If
            If text IsNot Nothing AndAlso text.Length > 0 Then span.TextContent = text
            Dim parent = element.Parent
            parent.InsertBefore(span, element)
            Dim nextNode = Following(element, root)
            parent.RemoveChild(element)
            Return nextNode
        End Function

        Private Sub CopySafeAttributes(source As IElement, target As IElement)
            For Each attribute In source.Attributes.ToArray()
                Dim n = attribute.Name.ToLowerInvariant()
                If SafeCopiedAttributes.Contains(n) OrElse n.StartsWith("aria-", StringComparison.Ordinal) OrElse n.StartsWith("data-", StringComparison.Ordinal) Then
                    Try
                        target.SetAttribute(attribute.Name, attribute.Value)
                    Catch ex As DomException
                        ' An attribute name the DOM refuses to recreate is simply not carried over.
                    End Try
                End If
            Next
        End Sub

#End Region

#Region "Elements"

        Private Function VisitElement(element As IElement, root As INode) As INode
            Dim ns = If(element.NamespaceUri, HtmlNs)
            Dim name = element.LocalName.ToLowerInvariant()
            If ns = SvgNs Then
                Select Case name
                    Case "script"
                        RemovedActive += 1
                        Return RemoveNode(element, root)
                    Case "handler", "listener"
                        Return RemoveNode(element, root)
                    Case "set", "animate"
                        If AnimatesHref(element) Then
                            RemovedActive += 1
                            Return RemoveNode(element, root)
                        End If
                End Select
            ElseIf ns = HtmlNs Then
                Select Case name
                    Case "script", "iframe", "frame", "frameset", "object", "embed", "applet", "portal", "fencedframe", "template", "keygen", "datalist"
                        RemovedActive += 1
                        Return RemoveNode(element, root)
                    Case "param", "track", "noembed", "noframes", "bgsound", "option", "optgroup", "isindex"
                        Return RemoveNode(element, root)
                    Case "source"
                        ' Only a picture source carries an image alternative; media sources go with their media.
                        Dim parentElement = element.ParentElement
                        If parentElement Is Nothing OrElse parentElement.NamespaceUri <> HtmlNs OrElse Not String.Equals(parentElement.LocalName, "picture", StringComparison.OrdinalIgnoreCase) Then
                            Return RemoveNode(element, root)
                        End If
                    Case "audio", "video"
                        RemovedActive += 1
                        Return Unwrap(element, root)
                    Case "noscript"
                        Return Unwrap(element, root)
                    Case "form"
                        RemovedActive += 1
                        Return ReplaceKeepingChildren(element, "div", root)
                    Case "button", "output"
                        RemovedActive += 1
                        Return ReplaceKeepingChildren(element, "span", root)
                    Case "input"
                        RemovedActive += 1
                        Return VisitInput(element, root)
                    Case "select"
                        RemovedActive += 1
                        Return ReplaceWithText(element, SelectedOptionText(element), root)
                    Case "textarea"
                        RemovedActive += 1
                        Return ReplaceWithText(element, element.TextContent, root, preWrap:=True)
                    Case "meta"
                        Return VisitMeta(element, root)
                    Case "link"
                        Return VisitLink(element, root)
                    Case "base"
                        Return VisitBase(element, root)
                End Select
            End If
            SanitizeAttributes(element, ns, name)
            Return NextInOrder(element, root)
        End Function

        Private Function AnimatesHref(element As IElement) As Boolean
            For Each attribute In element.Attributes
                If String.Equals(attribute.Name, "attributeName", StringComparison.OrdinalIgnoreCase) Then
                    Dim target = attribute.Value.Trim()
                    Dim colon = target.LastIndexOf(":"c)
                    If colon >= 0 Then target = target.Substring(colon + 1)
                    If String.Equals(target, "href", StringComparison.OrdinalIgnoreCase) Then Return True
                End If
            Next
            Return False
        End Function

        Private Function VisitInput(element As IElement, root As INode) As INode
            Dim kind = If(element.GetAttribute("type"), "").Trim().ToLowerInvariant()
            Select Case kind
                Case "hidden", "password", "file"
                    Return RemoveNode(element, root)
                Case "checkbox", "radio"
                    Return ReplaceWithText(element, If(element.HasAttribute("checked"), "[x]", "[ ]"), root)
                Case "submit", "reset", "button"
                    Dim label = If(element.GetAttribute("value"), "")
                    If label.Length = 0 Then Return RemoveNode(element, root)
                    Return ReplaceWithText(element, label, root)
                Case "image"
                    Return ReplaceImageInput(element, root)
                Case Else
                    Return ReplaceWithText(element, element.GetAttribute("value"), root)
            End Select
        End Function

        ' An image button is just a picture here; it goes through the same image reference rules.
        Private Function ReplaceImageInput(element As IElement, root As INode) As INode
            Dim image = _document.CreateElement("img")
            CopySafeAttributes(element, image)
            Dim src = element.GetAttribute("src")
            If src IsNot Nothing Then image.SetAttribute("src", src)
            Dim alt = If(element.GetAttribute("alt"), element.GetAttribute("value"))
            If alt IsNot Nothing Then image.SetAttribute("alt", alt)
            For Each dimension In {"width", "height"}
                Dim size = element.GetAttribute(dimension)
                If size IsNot Nothing Then image.SetAttribute(dimension, size)
            Next
            SanitizeAttributes(image, HtmlNs, "img")
            If Not image.HasAttribute("src") Then Return RemoveNode(element, root)
            Dim parent = element.Parent
            parent.InsertBefore(image, element)
            Dim nextNode = Following(element, root)
            parent.RemoveChild(element)
            Return nextNode
        End Function

        Private Function SelectedOptionText(element As IElement) As String
            Dim first As String = Nothing
            Dim chosen As New List(Of String)()
            For Each [option] In element.QuerySelectorAll("option")
                Dim text = OptionText([option])
                If first Is Nothing Then first = text
                If [option].HasAttribute("selected") Then chosen.Add(text)
            Next
            If chosen.Count > 0 Then Return String.Join(", ", chosen)
            Return If(first, "")
        End Function

        Private Function OptionText([option] As IElement) As String
            Dim label = [option].GetAttribute("label")
            If Not String.IsNullOrWhiteSpace(label) Then Return label.Trim()
            Return CollapseSpace([option].TextContent, 1024)
        End Function

        Private Function VisitMeta(element As IElement, root As INode) As INode
            ' The single charset declaration is written once, first in head, after the walk.
            If element.HasAttribute("charset") Then Return RemoveNode(element, root)
            Dim equiv = If(element.GetAttribute("http-equiv"), "").Trim().ToLowerInvariant()
            If equiv.Length > 0 Then
                ' An authored CSP only adds restrictions; every other pragma is dropped.
                If equiv <> "content-security-policy" Then
                    If equiv = "refresh" OrElse equiv = "set-cookie" Then RemovedActive += 1
                    Return RemoveNode(element, root)
                End If
            ElseIf String.Equals(If(element.GetAttribute("name"), "").Trim(), "referrer", StringComparison.OrdinalIgnoreCase) Then
                Return RemoveNode(element, root)
            End If
            SanitizeAttributes(element, HtmlNs, "meta")
            Return NextInOrder(element, root)
        End Function

        Private Function VisitLink(element As IElement, root As INode) As INode
            Dim rels = If(element.GetAttribute("rel"), "").ToLowerInvariant().Split(New Char() {" "c, ControlChars.Tab, ControlChars.Lf, ControlChars.Cr, ControlChars.FormFeed}, StringSplitOptions.RemoveEmptyEntries)
            If Not rels.Contains("stylesheet") OrElse rels.Any(Function(rel) NonStylesheetRels.Contains(rel)) Then Return RemoveNode(element, root)
            Dim reference = Classify(element.GetAttribute("href"))
            Select Case reference.Kind
                Case RefKind.SameOrigin, RefKind.Approved
                    SanitizeAttributes(element, HtmlNs, "link")
                    Return NextInOrder(element, root)
                Case RefKind.External
                    BlockExternal(reference.Origin)
            End Select
            Return RemoveNode(element, root)
        End Function

        ' Only a base that stays on the session origin survives, as a root-relative path.
        Private Function VisitBase(element As IElement, root As INode) As INode
            Dim normalized As String = Nothing
            If _baseSeen OrElse Not TryNormalizeBase(element.GetAttribute("href"), normalized) Then Return RemoveNode(element, root)
            _baseSeen = True
            For Each attribute In element.Attributes.ToArray()
                RemoveAttribute(element, attribute)
            Next
            element.SetAttribute("href", normalized)
            Return NextInOrder(element, root)
        End Function

        Private Function TryNormalizeBase(href As String, ByRef normalized As String) As Boolean
            Dim raw = NormalizeReference(href)
            If raw.Length = 0 OrElse _documentUri Is Nothing Then Return False
            Dim resolved As Uri = Nothing
            If Not Uri.TryCreate(_documentUri, raw, resolved) OrElse Not IsSameOrigin(resolved) Then Return False
            normalized = resolved.AbsolutePath
            Return normalized.Length > 0
        End Function

#End Region

#Region "Attributes"

        Private Shared Sub RemoveAttribute(element As IElement, attribute As IAttr)
            If Not String.IsNullOrEmpty(attribute.NamespaceUri) Then
                element.RemoveAttribute(attribute.NamespaceUri, attribute.LocalName)
            Else
                element.RemoveAttribute(attribute.Name)
            End If
        End Sub

        Private Sub SanitizeAttributes(element As IElement, ns As String, name As String)
            For Each attribute In element.Attributes.ToArray()
                Dim n = attribute.Name.ToLowerInvariant()
                If n.Length > 2 AndAlso n.StartsWith("on", StringComparison.Ordinal) OrElse AlwaysRemovedAttributes.Contains(n) Then
                    RemoveAttribute(element, attribute)
                    Continue For
                End If
                Select Case n
                    Case "http-equiv"
                        If Not (ns = HtmlNs AndAlso name = "meta") Then RemoveAttribute(element, attribute)
                    Case "href", "xlink:href"
                        If ns = SvgNs AndAlso name <> "a" Then
                            FilterResource(element, attribute, name = "image" OrElse name = "feimage")
                        Else
                            FilterLink(element, attribute, allowMail:=(name = "a"))
                        End If
                    Case "src"
                        FilterResource(element, attribute, ns = HtmlNs AndAlso (name = "img" OrElse name = "source"))
                    Case "poster", "background"
                        FilterResource(element, attribute, False)
                    Case "srcset", "imagesrcset"
                        FilterSrcset(element, attribute)
                    Case "cite"
                        FilterLink(element, attribute, allowMail:=False)
                    Case "usemap"
                        If Not NormalizeReference(attribute.Value).StartsWith("#", StringComparison.Ordinal) Then RemoveAttribute(element, attribute)
                End Select
            Next
        End Sub

        ' Navigation target: external http(s) stays (the host decides what a click does).
        Private Sub FilterLink(element As IElement, attribute As IAttr, allowMail As Boolean)
            Dim reference = Classify(attribute.Value)
            Select Case reference.Kind
                Case RefKind.Empty, RefKind.Fragment, RefKind.SameOrigin, RefKind.Approved, RefKind.External
                    Return
                Case RefKind.Mail
                    If allowMail Then Return
            End Select
            RemoveAttribute(element, attribute)
        End Sub

        ' Resource that the page would fetch by itself.
        Private Sub FilterResource(element As IElement, attribute As IAttr, allowDataImage As Boolean)
            Dim reference = Classify(attribute.Value)
            Select Case reference.Kind
                Case RefKind.Empty, RefKind.Fragment, RefKind.SameOrigin, RefKind.Approved
                    Return
                Case RefKind.External
                    BlockExternal(reference.Origin)
                Case RefKind.Data
                    If allowDataImage AndAlso IsAllowedDataImage(NormalizeReference(attribute.Value)) Then Return
            End Select
            RemoveAttribute(element, attribute)
        End Sub

        ' HTML "parse a srcset attribute": only the bad candidates are dropped.
        Private Sub FilterSrcset(element As IElement, attribute As IAttr)
            Dim value = attribute.Value
            Dim kept As New StringBuilder()
            Dim changed = False
            Dim pos = 0
            Dim n = value.Length
            While pos < n
                While pos < n AndAlso (IsSpace(value(pos)) OrElse value(pos) = ","c)
                    pos += 1
                End While
                If pos >= n Then Exit While
                Dim urlStart = pos
                While pos < n AndAlso Not IsSpace(value(pos))
                    pos += 1
                End While
                Dim url = value.Substring(urlStart, pos - urlStart)
                Dim descriptor = ""
                If url.EndsWith(","c) Then
                    url = url.TrimEnd(","c)
                Else
                    Dim descStart = pos
                    Dim inParens = False
                    While pos < n
                        Dim c = value(pos)
                        If inParens Then
                            If c = ")"c Then inParens = False
                        ElseIf c = "("c Then
                            inParens = True
                        ElseIf c = ","c Then
                            Exit While
                        End If
                        pos += 1
                    End While
                    descriptor = value.Substring(descStart, pos - descStart).Trim()
                End If
                If url.Length = 0 Then Continue While
                Dim reference = Classify(url)
                Dim keep = False
                Select Case reference.Kind
                    Case RefKind.Empty, RefKind.Fragment, RefKind.SameOrigin, RefKind.Approved
                        keep = True
                    Case RefKind.External
                        BlockExternal(reference.Origin)
                    Case RefKind.Data
                        keep = IsAllowedDataImage(NormalizeReference(url))
                End Select
                If keep Then
                    If kept.Length > 0 Then kept.Append(", ")
                    kept.Append(url)
                    If descriptor.Length > 0 Then kept.Append(" "c).Append(descriptor)
                Else
                    changed = True
                End If
            End While
            If kept.Length = 0 Then
                RemoveAttribute(element, attribute)
            ElseIf changed Then
                attribute.Value = kept.ToString()
            End If
        End Sub

        Private Shared Function IsSpace(c As Char) As Boolean
            Return c = " "c OrElse c = ControlChars.Tab OrElse c = ControlChars.Lf OrElse c = ControlChars.Cr OrElse c = ControlChars.FormFeed
        End Function

        Private Sub BlockExternal(origin As String)
            BlockedResources += 1
            If origin.Length > 0 AndAlso _externalOrder.Count < MaxExternalOrigins AndAlso _externalSeen.Add(origin) Then _externalOrder.Add(origin)
        End Sub

        Private Function IsSameOrigin(uri As Uri) As Boolean
            Return _origin IsNot Nothing AndAlso _origin.IsAbsoluteUri AndAlso String.Equals(uri.Scheme, _origin.Scheme, StringComparison.OrdinalIgnoreCase) AndAlso
                String.Equals(uri.IdnHost, _origin.IdnHost, StringComparison.OrdinalIgnoreCase) AndAlso uri.Port = _origin.Port
        End Function

        Private Function Classify(raw As String) As Reference
            Dim s = NormalizeReference(raw)
            If s.Length = 0 Then Return New Reference(RefKind.Empty)
            If s(0) = "#"c Then Return New Reference(RefKind.Fragment)
            Dim segmentEnd = s.IndexOfAny(New Char() {"/"c, "?"c, "#"c, "\"c})
            Dim colon = s.IndexOf(":"c)
            If colon >= 0 AndAlso (segmentEnd < 0 OrElse colon < segmentEnd) Then
                ' A colon in the first segment is a scheme or something no local file name can be.
                Dim scheme = s.Substring(0, colon)
                If Not IsSchemeName(scheme) Then Return New Reference(RefKind.Unsafe)
                Select Case scheme.ToLowerInvariant()
                    Case "http", "https"
                        Return ClassifyAbsolute(s)
                    Case "mailto"
                        Return New Reference(RefKind.Mail)
                    Case "data"
                        Return New Reference(RefKind.Data)
                    Case Else
                        Return New Reference(RefKind.Unsafe)
                End Select
            End If
            Dim queryAt = s.IndexOfAny(New Char() {"?"c, "#"c})
            Dim pathPart = (If(queryAt >= 0, s.Substring(0, queryAt), s)).Replace("\"c, "/"c)
            If pathPart.StartsWith("//", StringComparison.Ordinal) Then
                Dim scheme = If(_origin IsNot Nothing AndAlso _origin.IsAbsoluteUri, _origin.Scheme, "https")
                Return ClassifyAbsolute(scheme & ":" & pathPart)
            End If
            Return New Reference(RefKind.SameOrigin)
        End Function

        Private Function ClassifyAbsolute(text As String) As Reference
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(text, UriKind.Absolute, uri) Then Return New Reference(RefKind.Unsafe)
            If (uri.Scheme <> Uri.UriSchemeHttp AndAlso uri.Scheme <> Uri.UriSchemeHttps) OrElse uri.UserInfo.Length > 0 OrElse uri.Host.Length = 0 Then Return New Reference(RefKind.Unsafe)
            Dim origin = OriginOf(uri)
            If IsSameOrigin(uri) Then Return New Reference(RefKind.SameOrigin, origin)
            If _options.ApprovedOrigins IsNot Nothing AndAlso _options.ApprovedOrigins.Contains(origin) Then Return New Reference(RefKind.Approved, origin)
            Return New Reference(RefKind.External, origin)
        End Function

#End Region
    End Class

#End Region
End Module
#End If
