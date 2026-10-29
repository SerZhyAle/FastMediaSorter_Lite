#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Security.Cryptography
Imports System.Text

' Specification 275 sections 3, 10.3 and 14: pure URI/scheme/root/resource-kind/navigation
' decisions and limits. No filesystem, WinForms or WebView2 reference. This file compiles on
' the net48 test leg too (DOCUMENT_PREVIEW_TESTS), so only APIs present on both runtimes.

' One navigation decision (section 11): what the host does with a clicked or attempted target.
Public NotInheritable Class HtmlNavigationDecision
    Public Property Action As HtmlNavigationAction
    ' Logical key for Internal / Anchor / OpenTyped, "" otherwise.
    Public Property Key As String = ""
    ' Raw escaped text after "#" (without the "#"); "" when the target has none.
    Public Property Fragment As String = ""
    ' Normalized absolute URI for External / Mail, "" otherwise.
    Public Property ExternalUrl As String = ""

    Friend Shared Function Block() As HtmlNavigationDecision
        Return New HtmlNavigationDecision With {.Action = HtmlNavigationAction.Blocked}
    End Function
End Class

Public Module HtmlDocumentPolicy
    ' Section 14 budgets. Bytes are Long, counts and milliseconds Integer.
    Public Const MaxEntryBytes As Long = 16L * 1024L * 1024L
    Public Const MaxCreatedElements As Integer = 100000
    Public Const MaxTokens As Integer = 250000
    Public Const MaxStylesheetBytes As Long = 2L * 1024L * 1024L
    Public Const MaxFontBytes As Long = 8L * 1024L * 1024L
    Public Const MaxImageBytes As Long = 32L * 1024L * 1024L
    Public Const MaxDataImageBytes As Long = 4L * 1024L * 1024L
    Public Const MaxUniqueResources As Integer = 2048
    Public Const MaxOutstandingReads As Integer = 4
    Public Const LocalResourceDeadlineMs As Integer = 10000
    Public Const FirstNavigationDeadlineMs As Integer = 15000
    Public Const MaxFindTermLength As Integer = 4096
    Public Const MaxNavigationEntries As Integer = 100
    Public Const OriginHostSuffix As String = ".fms-doc.invalid"

    Private Const MaxSegmentLength As Integer = 255
    Private Const MaxExternalUrlLength As Integer = 8192
    Private Const HexAlphabet As String = "0123456789abcdef"

    Private ReadOnly StrictUtf8 As New UTF8Encoding(False, True)
    Private ReadOnly DeviceNames As New HashSet(Of String)(StringComparer.Ordinal) From {"CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$"}
    Private ReadOnly InvalidNameChars As Char() = {"<"c, ">"c, """"c, "|"c, "?"c, "*"c}
    Private ReadOnly ExecutableExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".exe", ".bat", ".cmd", ".com", ".scr", ".msi", ".msp", ".ps1", ".ps1xml", ".psc1", ".psm1", ".vbs", ".vbe",
        ".js", ".jse", ".wsf", ".wsh", ".lnk", ".url", ".hta", ".cpl", ".jar", ".reg", ".dll", ".pif", ".msc",
        ".scf", ".inf", ".sct", ".gadget", ".application", ".appref-ms", ".chm"}
    Private ReadOnly AllowedDataImageTypes As New HashSet(Of String)(StringComparer.Ordinal) From {"png", "jpeg", "gif", "webp", "bmp", "avif"}

    ' ---- session origin -------------------------------------------------------------------

    ' https://s<32 lowercase hex>.fms-doc.invalid/ ; nothing listens on it, the host answers every request.
    Public Function NewSessionOrigin() As Uri
        Dim bytes(15) As Byte
        Using rng As RandomNumberGenerator = RandomNumberGenerator.Create()
            rng.GetBytes(bytes)
        End Using
        Return New Uri("https://s" & ToLowerHex(bytes) & OriginHostSuffix & "/")
    End Function

    Public Function IsSessionOrigin(candidate As Uri, origin As Uri) As Boolean
        If candidate Is Nothing OrElse origin Is Nothing Then Return False
        If Not candidate.IsAbsoluteUri OrElse Not origin.IsAbsoluteUri Then Return False
        If Not String.Equals(candidate.Scheme, "https", StringComparison.OrdinalIgnoreCase) Then Return False
        If Not String.Equals(origin.Scheme, "https", StringComparison.OrdinalIgnoreCase) Then Return False
        If candidate.Port <> 443 OrElse origin.Port <> 443 Then Return False
        If candidate.UserInfo.Length > 0 Then Return False
        If origin.Host.Length = 0 Then Return False
        Return String.Equals(candidate.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
    End Function

    ' Each "/"-separated segment is escaped once, so "a#b%c&d e.html" survives the round trip.
    Public Function EntryUrl(origin As Uri, key As String) As String
        If origin Is Nothing OrElse key Is Nothing Then Return ""
        Dim sb As New StringBuilder("https://", 96)
        sb.Append(origin.Host).Append("/"c)
        Dim segments As String() = key.Split("/"c)
        For i As Integer = 0 To segments.Length - 1
            If i > 0 Then sb.Append("/"c)
            sb.Append(Uri.EscapeDataString(segments(i)))
        Next
        Return sb.ToString()
    End Function

    ' ---- resolver steps 1-3 (section 10.3) ------------------------------------------------

    ' Works on the RAW request text: Uri canonicalization removes an encoded parent segment
    ' ("/a/%2e%2e/b" becomes "/b"), so Uri.AbsolutePath is never the authorization input.
    Public Function ParseRequestKey(requestUri As String, origin As Uri, method As String) As HtmlKeyResolution
        If Not (String.Equals(method, "GET", StringComparison.Ordinal) OrElse String.Equals(method, "HEAD", StringComparison.Ordinal)) Then
            Return HtmlKeyResolution.Refused(HtmlOutcome.UnsupportedResource)
        End If
        If requestUri Is Nothing OrElse origin Is Nothing OrElse Not origin.IsAbsoluteUri Then Return HtmlKeyResolution.Refused(HtmlOutcome.RootEscape)
        If HasControlOrOuterSpace(requestUri) Then Return HtmlKeyResolution.Refused(HtmlOutcome.RootEscape)

        Const schemePrefix As String = "https://"
        If requestUri.Length <= schemePrefix.Length OrElse Not requestUri.StartsWith(schemePrefix, StringComparison.OrdinalIgnoreCase) Then
            Return HtmlKeyResolution.Refused(HtmlOutcome.RootEscape)
        End If
        Dim parsed As Uri = Nothing
        If Not Uri.TryCreate(requestUri, UriKind.Absolute, parsed) OrElse Not IsSessionOrigin(parsed, origin) Then
            Return HtmlKeyResolution.Refused(HtmlOutcome.RootEscape)
        End If

        Dim rest As String = requestUri.Substring(schemePrefix.Length)
        Dim authorityEnd As Integer = rest.IndexOfAny(New Char() {"/"c, "?"c, "#"c})
        Dim authority As String = If(authorityEnd < 0, rest, rest.Substring(0, authorityEnd))
        If Not (String.Equals(authority, origin.Host, StringComparison.OrdinalIgnoreCase) OrElse
                String.Equals(authority, origin.Host & ":443", StringComparison.OrdinalIgnoreCase)) Then
            ' Userinfo, a different port, a trailing dot or a backslash all end here.
            Return HtmlKeyResolution.Refused(HtmlOutcome.RootEscape)
        End If

        Dim afterAuthority As String = If(authorityEnd < 0, "", rest.Substring(authorityEnd))
        Dim pathEnd As Integer = afterAuthority.IndexOfAny(New Char() {"?"c, "#"c})
        Dim rawPath As String = If(pathEnd < 0, afterAuthority, afterAuthority.Substring(0, pathEnd))
        If rawPath.Length <= 1 Then Return HtmlKeyResolution.Refused(HtmlOutcome.UnsupportedResource)
        If rawPath.IndexOf("\"c) >= 0 Then Return HtmlKeyResolution.Refused(HtmlOutcome.RootEscape)

        Dim rawSegments As String() = rawPath.Substring(1).Split("/"c)
        Dim decodedSegments As New List(Of String)(rawSegments.Length)
        Dim trailingSlash As Boolean = False
        For i As Integer = 0 To rawSegments.Length - 1
            Dim raw As String = rawSegments(i)
            If raw.Length = 0 Then
                If i = rawSegments.Length - 1 Then
                    trailingSlash = True
                Else
                    Return HtmlKeyResolution.Refused(HtmlOutcome.RootEscape)
                End If
                Continue For
            End If
            Dim decoded As String = Nothing
            If Not TryDecodeOnce(raw, decoded) Then Return HtmlKeyResolution.Refused(HtmlOutcome.RootEscape)
            Dim verdict As HtmlOutcome = ValidateSegment(decoded)
            If verdict <> HtmlOutcome.Ok Then Return HtmlKeyResolution.Refused(verdict)
            decodedSegments.Add(decoded)
        Next
        ' There is no directory index: a directory is never served.
        If trailingSlash Then Return HtmlKeyResolution.Refused(HtmlOutcome.UnsupportedResource)
        Return HtmlKeyResolution.Allowed(String.Join("/", decodedSegments.ToArray()))
    End Function

    ' The same segment rules for a key that did not come through ParseRequestKey (a package
    ' adapter, a restored history entry, a test). Returns the key unchanged when allowed.
    Public Function ValidateKey(key As String) As HtmlKeyResolution
        If String.IsNullOrEmpty(key) Then Return HtmlKeyResolution.Refused(HtmlOutcome.UnsupportedResource)
        Dim segments As String() = key.Split("/"c)
        For i As Integer = 0 To segments.Length - 1
            If segments(i).Length = 0 AndAlso i = segments.Length - 1 AndAlso i > 0 Then
                Return HtmlKeyResolution.Refused(HtmlOutcome.UnsupportedResource)
            End If
            Dim verdict As HtmlOutcome = ValidateSegment(segments(i))
            If verdict <> HtmlOutcome.Ok Then Return HtmlKeyResolution.Refused(verdict)
        Next
        Return HtmlKeyResolution.Allowed(key)
    End Function

    Public Function IsReservedDeviceName(segment As String) As Boolean
        If segment Is Nothing Then Return False
        Dim dot As Integer = segment.IndexOf("."c)
        Dim stem As String = If(dot >= 0, segment.Substring(0, dot), segment).TrimEnd(" "c)
        If stem.Length = 0 Then Return False
        Dim upper As String = stem.ToUpperInvariant()
        If DeviceNames.Contains(upper) Then Return True
        If upper.Length = 4 AndAlso (upper.StartsWith("COM", StringComparison.Ordinal) OrElse upper.StartsWith("LPT", StringComparison.Ordinal)) Then
            Dim d As Char = upper(3)
            ' ASCII digits and the superscript digits Windows also reserves.
            If (d >= "0"c AndAlso d <= "9"c) OrElse d = ChrW(&HB9) OrElse d = ChrW(&HB2) OrElse d = ChrW(&HB3) Then Return True
        End If
        Return False
    End Function

    ' ---- resource kinds and types ---------------------------------------------------------

    Public Function KindOfExtension(key As String) As HtmlResourceKind
        Select Case ExtensionOf(key)
            Case ".html", ".htm", ".xhtml"
                Return HtmlResourceKind.Document
            Case ".css"
                Return HtmlResourceKind.Stylesheet
            Case ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".avif", ".svg", ".ico"
                Return HtmlResourceKind.Image
            Case ".woff", ".woff2", ".ttf", ".otf"
                Return HtmlResourceKind.Font
            Case Else
                Return HtmlResourceKind.Other
        End Select
    End Function

    Public Function MimeTypeOf(key As String) As String
        Select Case ExtensionOf(key)
            Case ".html", ".htm" : Return "text/html; charset=utf-8"
            Case ".xhtml" : Return "application/xhtml+xml"
            Case ".css" : Return "text/css"
            Case ".png" : Return "image/png"
            Case ".jpg", ".jpeg" : Return "image/jpeg"
            Case ".gif" : Return "image/gif"
            Case ".webp" : Return "image/webp"
            Case ".bmp" : Return "image/bmp"
            Case ".avif" : Return "image/avif"
            Case ".svg" : Return "image/svg+xml"
            Case ".ico" : Return "image/x-icon"
            Case ".woff" : Return "font/woff"
            Case ".woff2" : Return "font/woff2"
            Case ".ttf" : Return "font/ttf"
            Case ".otf" : Return "font/otf"
            Case Else : Return "application/octet-stream"
        End Select
    End Function

    ' A CSS file requested as a document is refused: it can never become active HTML.
    Public Function IsAllowedInContext(kind As HtmlResourceKind, context As HtmlRequestContext) As Boolean
        Select Case kind
            Case HtmlResourceKind.Document : Return context = HtmlRequestContext.Document
            Case HtmlResourceKind.Stylesheet : Return context = HtmlRequestContext.Stylesheet
            Case HtmlResourceKind.Image : Return context = HtmlRequestContext.Image
            Case HtmlResourceKind.Font : Return context = HtmlRequestContext.Font
            Case Else : Return False
        End Select
    End Function

    Public Function MaxBytesFor(kind As HtmlResourceKind) As Long
        Select Case kind
            Case HtmlResourceKind.Document : Return MaxEntryBytes
            Case HtmlResourceKind.Stylesheet : Return MaxStylesheetBytes
            Case HtmlResourceKind.Image : Return MaxImageBytes
            Case HtmlResourceKind.Font : Return MaxFontBytes
            Case Else : Return 0L
        End Select
    End Function

    ' data:image/(png|jpeg|gif|webp|bmp|avif)[;param]*;base64,<payload>, payload <= 4 MiB encoded.
    Public Function IsAllowedDataImage(value As String) As Boolean
        Const prefix As String = "data:image/"
        If value Is Nothing OrElse value.Length <= prefix.Length Then Return False
        If Not value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then Return False
        Dim comma As Integer = value.IndexOf(","c)
        If comma < 0 Then Return False
        Dim parts As String() = value.Substring(5, comma - 5).Split(";"c)
        If parts.Length < 2 Then Return False
        If Not AllowedDataImageTypes.Contains(parts(0).Substring("image/".Length).ToLowerInvariant()) Then Return False
        If Not String.Equals(parts(parts.Length - 1), "base64", StringComparison.OrdinalIgnoreCase) Then Return False
        For i As Integer = 1 To parts.Length - 2
            If parts(i).Length = 0 Then Return False
            For Each c As Char In parts(i)
                If Not (IsAsciiLetterOrDigit(c) OrElse c = "="c OrElse c = "."c OrElse c = "_"c OrElse c = "-"c OrElse c = "+"c) Then Return False
            Next
        Next
        Dim payloadLength As Integer = value.Length - comma - 1
        If payloadLength <= 0 OrElse payloadLength > MaxDataImageBytes Then Return False
        Dim padding As Integer = 0
        For i As Integer = comma + 1 To value.Length - 1
            Dim c As Char = value(i)
            If c = "="c Then
                padding += 1
                If padding > 2 Then Return False
            ElseIf c = " "c OrElse c = ControlChars.Tab OrElse c = ControlChars.Cr OrElse c = ControlChars.Lf OrElse c = ControlChars.FormFeed Then
                ' base64 in markup may be wrapped; whitespace is ignored by the decoder
            ElseIf padding > 0 Then
                Return False
            ElseIf Not (IsAsciiLetterOrDigit(c) OrElse c = "+"c OrElse c = "/"c) Then
                Return False
            End If
        Next
        Return True
    End Function

    ' ---- navigation -----------------------------------------------------------------------

    Public Function ClassifyNavigation(target As String, origin As Uri, currentKey As String) As HtmlNavigationDecision
        If String.IsNullOrEmpty(target) OrElse HasControlOrOuterSpace(target) Then Return HtmlNavigationDecision.Block()

        If target(0) = "#"c Then
            If String.IsNullOrEmpty(currentKey) Then Return HtmlNavigationDecision.Block()
            Return New HtmlNavigationDecision With {.Action = HtmlNavigationAction.Anchor, .Key = currentKey, .Fragment = target.Substring(1)}
        End If

        Dim parsed As Uri = Nothing
        If Not Uri.TryCreate(target, UriKind.Absolute, parsed) Then Return HtmlNavigationDecision.Block()
        Select Case parsed.Scheme
            Case "https", "http"
                If IsSessionOrigin(parsed, origin) Then Return ClassifySameOrigin(target, origin, currentKey)
                Return ClassifyExternal(parsed, target)
            Case "mailto"
                Dim mail As String = parsed.AbsoluteUri
                If mail.Length <= "mailto:".Length OrElse mail.Length > MaxExternalUrlLength Then Return HtmlNavigationDecision.Block()
                Return New HtmlNavigationDecision With {.Action = HtmlNavigationAction.Mail, .ExternalUrl = mail}
            Case Else
                ' javascript:, vbscript:, data:, file:, blob:, about:, ftp: and any unknown scheme.
                Return HtmlNavigationDecision.Block()
        End Select
    End Function

    Private Function ClassifySameOrigin(target As String, origin As Uri, currentKey As String) As HtmlNavigationDecision
        Dim resolution As HtmlKeyResolution = ParseRequestKey(target, origin, "GET")
        If Not resolution.IsAllowed OrElse LooksExecutable(resolution.Key) Then Return HtmlNavigationDecision.Block()
        Dim key As String = resolution.Key
        Dim hash As Integer = target.IndexOf("#"c)
        Dim fragment As String = If(hash < 0, "", target.Substring(hash + 1))
        If KindOfExtension(key) <> HtmlResourceKind.Document Then
            Return New HtmlNavigationDecision With {.Action = HtmlNavigationAction.OpenTyped, .Key = key}
        End If
        Dim query As Integer = target.IndexOf("?"c)
        Dim hasQuery As Boolean = query >= 0 AndAlso (hash < 0 OrElse query < hash)
        If hash >= 0 AndAlso Not hasQuery AndAlso String.Equals(key, currentKey, StringComparison.OrdinalIgnoreCase) Then
            Return New HtmlNavigationDecision With {.Action = HtmlNavigationAction.Anchor, .Key = key, .Fragment = fragment}
        End If
        Return New HtmlNavigationDecision With {.Action = HtmlNavigationAction.Internal, .Key = key, .Fragment = fragment}
    End Function

    Private Function ClassifyExternal(parsed As Uri, target As String) As HtmlNavigationDecision
        If parsed.UserInfo.Length > 0 OrElse parsed.Host.Length = 0 Then Return HtmlNavigationDecision.Block()
        ' An origin of an earlier or foreign session is never offered to the system browser.
        If parsed.Host.EndsWith(OriginHostSuffix, StringComparison.OrdinalIgnoreCase) Then Return HtmlNavigationDecision.Block()
        If LooksExecutable(Uri.UnescapeDataString(parsed.AbsolutePath)) Then Return HtmlNavigationDecision.Block()
        Dim normalized As String = NormalizeExternalUrl(target)
        If normalized.Length = 0 Then Return HtmlNavigationDecision.Block()
        Return New HtmlNavigationDecision With {.Action = HtmlNavigationAction.External, .ExternalUrl = normalized}
    End Function

    ' Absolute http/https only, no userinfo, a real host, never a session-origin host.
    Public Function NormalizeExternalUrl(value As String) As String
        If String.IsNullOrWhiteSpace(value) Then Return ""
        Dim text As String = value.Trim()
        If text.Length > MaxExternalUrlLength OrElse HasControlOrOuterSpace(text) Then Return ""
        Dim parsed As Uri = Nothing
        If Not Uri.TryCreate(text, UriKind.Absolute, parsed) Then Return ""
        If Not (String.Equals(parsed.Scheme, "http", StringComparison.OrdinalIgnoreCase) OrElse
                String.Equals(parsed.Scheme, "https", StringComparison.OrdinalIgnoreCase)) Then Return ""
        If parsed.UserInfo.Length > 0 OrElse parsed.Host.Length = 0 Then Return ""
        If parsed.Host.EndsWith(OriginHostSuffix, StringComparison.OrdinalIgnoreCase) Then Return ""
        Dim result As String = parsed.AbsoluteUri
        Return If(result.Length > MaxExternalUrlLength, "", result)
    End Function

    ' ---- response policy (section 10.4) ---------------------------------------------------

    Public Function LocalContentSecurityPolicy() As String
        Return String.Join("; ", New String() {
            "default-src 'none'", "script-src 'none'", "connect-src 'none'",
            "style-src 'self' 'unsafe-inline'", "img-src 'self' data:", "font-src 'self'",
            "frame-src 'none'", "child-src 'none'", "worker-src 'none'", "object-src 'none'",
            "base-uri 'self'", "form-action 'none'", "frame-ancestors 'none'", "media-src 'none'"})
    End Function

    ' CRLF-joined header block for CoreWebView2Environment.CreateWebResourceResponse. A value that
    ' carries any control character (CR/LF injection) is replaced by the safe default.
    Public Function ResponseHeaders(mimeType As String, isDocument As Boolean, Optional csp As String = Nothing) As String
        Dim lines As New List(Of String)(8)
        lines.Add("Content-Type: " & SafeHeaderValue(mimeType, "application/octet-stream"))
        lines.Add("X-Content-Type-Options: nosniff")
        lines.Add("Cache-Control: no-store")
        lines.Add("Referrer-Policy: no-referrer")
        lines.Add("X-DNS-Prefetch-Control: off")
        lines.Add("Permissions-Policy: camera=(), microphone=(), geolocation=(), usb=(), serial=(), hid=(), bluetooth=(), payment=(), " &
                  "clipboard-read=(), clipboard-write=(), display-capture=(), fullscreen=(), autoplay=(), interest-cohort=(), browsing-topics=()")
        If isDocument Then lines.Add("Content-Security-Policy: " & SafeHeaderValue(csp, LocalContentSecurityPolicy()))
        Return String.Join(vbCrLf, lines.ToArray())
    End Function

    ' Bounded find query; never cuts a surrogate pair in half.
    Public Function NormalizeFindTerm(term As String) As String
        If term Is Nothing Then Return ""
        If term.Length <= MaxFindTermLength Then Return term
        Dim length As Integer = MaxFindTermLength
        If Char.IsHighSurrogate(term(length - 1)) Then length -= 1
        Return term.Substring(0, length)
    End Function

    ' ---- helpers --------------------------------------------------------------------------

    Friend Function ToLowerHex(bytes As Byte()) As String
        If bytes Is Nothing Then Return ""
        Dim sb As New StringBuilder(bytes.Length * 2)
        For Each b As Byte In bytes
            Dim v As Integer = b
            sb.Append(HexAlphabet.Chars(v >> 4)).Append(HexAlphabet.Chars(v And 15))
        Next
        Return sb.ToString()
    End Function

    Private Function SafeHeaderValue(value As String, fallback As String) As String
        If String.IsNullOrWhiteSpace(value) Then Return fallback
        For Each c As Char In value
            If Char.IsControl(c) Then Return fallback
        Next
        Return value.Trim()
    End Function

    Private Function HasControlOrOuterSpace(text As String) As Boolean
        If text.Length = 0 Then Return False
        If Char.IsWhiteSpace(text(0)) OrElse Char.IsWhiteSpace(text(text.Length - 1)) Then Return True
        For Each c As Char In text
            If Char.IsControl(c) Then Return True
        Next
        Return False
    End Function

    Private Function IsAsciiLetterOrDigit(c As Char) As Boolean
        Return (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "0"c AndAlso c <= "9"c)
    End Function

    Private Function LooksExecutable(path As String) As Boolean
        If String.IsNullOrEmpty(path) Then Return False
        Dim cut As Integer = Math.Max(path.LastIndexOf("/"c), path.LastIndexOf("\"c))
        Dim name As String = path.Substring(cut + 1).TrimEnd("."c, " "c)
        Dim dot As Integer = name.LastIndexOf("."c)
        Return dot >= 0 AndAlso ExecutableExtensions.Contains(name.Substring(dot))
    End Function

    Private Function ExtensionOf(key As String) As String
        If String.IsNullOrEmpty(key) Then Return ""
        Dim name As String = key.Substring(key.LastIndexOf("/"c) + 1)
        Dim dot As Integer = name.LastIndexOf("."c)
        If dot < 0 OrElse dot = name.Length - 1 Then Return ""
        Return name.Substring(dot).ToLowerInvariant()
    End Function

    Private Function HexValue(c As Char) As Integer
        If c >= "0"c AndAlso c <= "9"c Then Return AscW(c) - AscW("0"c)
        If c >= "a"c AndAlso c <= "f"c Then Return AscW(c) - AscW("a"c) + 10
        If c >= "A"c AndAlso c <= "F"c Then Return AscW(c) - AscW("A"c) + 10
        Return -1
    End Function

    ' Percent-decoding exactly once: "%252e" becomes the literal "%2e", which is never decoded again.
    ' Strict UTF-8 both ways: a malformed escape sequence, an invalid byte sequence or a lone
    ' surrogate refuses the request instead of being passed on as a lookalike name.
    Private Function TryDecodeOnce(raw As String, ByRef decoded As String) As Boolean
        decoded = ""
        Try
            Dim bytes As New List(Of Byte)(raw.Length)
            Dim i As Integer = 0
            While i < raw.Length
                Dim c As Char = raw(i)
                If c = "%"c Then
                    If i + 2 >= raw.Length Then Return False
                    Dim hi As Integer = HexValue(raw(i + 1))
                    Dim lo As Integer = HexValue(raw(i + 2))
                    If hi < 0 OrElse lo < 0 Then Return False
                    bytes.Add(CByte(hi * 16 + lo))
                    i += 3
                Else
                    Dim width As Integer = If(Char.IsHighSurrogate(c) AndAlso i + 1 < raw.Length AndAlso Char.IsLowSurrogate(raw(i + 1)), 2, 1)
                    bytes.AddRange(StrictUtf8.GetBytes(raw.Substring(i, width)))
                    i += width
                End If
            End While
            decoded = StrictUtf8.GetString(bytes.ToArray())
            Return True
        Catch ex As ArgumentException
            Return False
        End Try
    End Function

    ' One decoded path segment against Windows file-name meaning (step 3).
    Private Function ValidateSegment(segment As String) As HtmlOutcome
        If segment.Length = 0 OrElse segment = "." OrElse segment = ".." Then Return HtmlOutcome.RootEscape
        For Each c As Char In segment
            If c = "/"c OrElse c = "\"c OrElse c = ":"c OrElse Char.IsControl(c) Then Return HtmlOutcome.RootEscape
        Next
        Dim last As Char = segment(segment.Length - 1)
        If last = "."c OrElse last = " "c Then Return HtmlOutcome.RootEscape
        If IsReservedDeviceName(segment) Then Return HtmlOutcome.RootEscape
        If segment.Length > MaxSegmentLength Then Return HtmlOutcome.UnsupportedResource
        If segment.IndexOfAny(InvalidNameChars) >= 0 Then Return HtmlOutcome.UnsupportedResource
        Return HtmlOutcome.Ok
    End Function
End Module
#End If
