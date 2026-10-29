#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports AngleSharp.Dom
Imports AngleSharp.Html.Parser

' The EPUB adapter supplies bytes, never a filesystem URL or trusted authored script.
Friend NotInheritable Class HtmlReaderResource
    Public Property Bytes As Byte()
    Public Property MimeType As String = "application/octet-stream"
End Class

Friend Module HtmlReaderContent
    Friend Const Policy As String = "default-src 'none'; script-src 'none'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; media-src 'none'; frame-src 'none'; object-src 'none'; connect-src 'none'; base-uri 'none'; form-action 'none'"

    Friend Function Sanitize(bytes As Byte(), preferences As ModernViewerPreferences, fixedLayout As Boolean, dark As Boolean) As Byte()
        If bytes Is Nothing OrElse bytes.Length > EpubPackage.MaxResourceBytes Then Throw New IO.InvalidDataException("HTML resource exceeds the reader limit.")
        ' EPUB XHTML is UTF-8 or UTF-16. The parser has no browsing context/network loader.
        Dim text As String
        Using stream As New IO.MemoryStream(bytes, writable:=False), reader As New IO.StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks:=True)
            text = reader.ReadToEnd()
        End Using
        If text.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso text.IndexOf("<!ENTITY", StringComparison.OrdinalIgnoreCase) >= 0 Then Throw New IO.InvalidDataException("Document entities are prohibited.")
        Dim parser As New HtmlParser()
        Using document = parser.ParseDocument(text)
            If document.All.Length > 100000 Then Throw New IO.InvalidDataException("HTML node limit exceeded.")
            For Each element In document.QuerySelectorAll("script,iframe,frame,frameset,object,embed,applet,form,input,button,textarea,select,base,portal,foreignObject").ToArray()
                element.Remove()
            Next
            For Each element In document.All.ToArray()
                Dim depth As Integer
                Dim parent = element.ParentElement
                While parent IsNot Nothing AndAlso depth <= 128
                    depth += 1
                    parent = parent.ParentElement
                End While
                If depth > 128 Then Throw New IO.InvalidDataException("HTML depth limit exceeded.")
                For Each attribute In element.Attributes.ToArray()
                    Dim name = attribute.Name.ToLowerInvariant()
                    Dim value = attribute.Value.Trim()
                    If name.StartsWith("on", StringComparison.Ordinal) OrElse {"srcdoc", "autofocus", "download", "ping", "formaction", "action"}.Contains(name) OrElse
                        ((name = "href" OrElse name = "src" OrElse name.EndsWith(":href", StringComparison.Ordinal)) AndAlso
                         (value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) OrElse value.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase))) Then element.RemoveAttribute(attribute.Name)
                Next
                If element.LocalName = "meta" AndAlso element.HasAttribute("http-equiv") Then element.Remove()
                If Not preferences.EpubUseBookStyles AndAlso Not fixedLayout Then
                    element.RemoveAttribute("style")
                    If element.LocalName = "style" OrElse element.LocalName = "link" Then element.Remove()
                End If
            Next
            Dim charset = document.CreateElement("meta")
            charset.SetAttribute("charset", "utf-8")
            document.Head.Prepend(charset)
            Dim style = document.CreateElement("style")
            style.TextContent = Appearance(preferences, fixedLayout, dark)
            document.Head.AppendChild(style)
            Return Encoding.UTF8.GetBytes("<!doctype html>" & document.DocumentElement.OuterHtml)
        End Using
    End Function

    Friend Function Appearance(p As ModernViewerPreferences, fixedLayout As Boolean, dark As Boolean) As String
        Dim css As New StringBuilder("img,svg{max-width:100%;height:auto}a{overflow-wrap:anywhere}")
        If fixedLayout Then Return css.ToString()
        Dim theme = If(p.EpubTheme = "system", If(dark, "dark", "light"), p.EpubTheme)
        Dim colors = If(theme = "sepia", New String() {"#f5e6cc", "#5d4037", "#8d6e63"}, If(theme = "black", New String() {"#000000", "#cccccc", "#4fc3f7"},
            If(theme = "dark", New String() {"#1e1e1e", "#e0e0e0", "#64b5f6"}, New String() {"#ffffff", "#000000", "#1976d2"})))
        css.Append("html{background:").Append(colors(0)).Append("!important;color:").Append(colors(1)).Append("!important}body{background:transparent!important;color:inherit!important;")
        css.Append("font-size:").Append(p.EpubFontSize).Append("px!important;line-height:").Append(p.EpubLineHeight.ToString(CultureInfo.InvariantCulture)).Append("!important;")
        css.Append("padding:0 ").Append(p.EpubHorizontalMargin).Append("px!important;margin:0 auto!important;")
        If p.EpubColumnWidth > 0 Then css.Append("max-width:").Append(p.EpubColumnWidth).Append("ch!important;")
        If p.EpubAlignment <> "book" Then css.Append("text-align:").Append(p.EpubAlignment).Append("!important;")
        If p.EpubHyphenation <> "book" Then css.Append("hyphens:").Append(If(p.EpubHyphenation = "on", "auto", "none")).Append("!important;")
        If p.EpubFontFamily <> "book" Then
            Dim family = If(p.EpubFontFamily = "serif", "serif", If(p.EpubFontFamily = "sans-serif", "sans-serif", If(p.EpubFontFamily = "monospace", "monospace", "'" & p.EpubFontFamily.Replace("\", "\\").Replace("'", "\'").Replace(vbCr, "").Replace(vbLf, "") & "'")))
            css.Append("font-family:").Append(family).Append("!important;")
        End If
        css.Append("}p{margin-block-end:").Append(p.EpubParagraphSpacing.ToString(CultureInfo.InvariantCulture)).Append("em!important}a{color:").Append(colors(2)).Append("!important}")
        If p.EpubLayout = "paginated" Then css.Append("html{height:100%;overflow-y:hidden}body{height:calc(100vh - 2em);column-width:calc(100vw - 2em);column-gap:2em;max-width:none!important;padding-block:1em!important;column-fill:auto}")
        Return css.ToString()
    End Function

    Friend Function ResourceKey(uri As Uri, origin As Uri) As String
        If uri Is Nothing OrElse uri.Scheme <> "https" OrElse uri.Authority <> origin.Authority OrElse uri.UserInfo.Length > 0 Then Return Nothing
        Dim key = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart("/"c))
        If key.Length > 2048 OrElse key.IndexOfAny({ChrW(0), "\"c}) >= 0 OrElse key.Split("/"c).Any(Function(part) part = ".." OrElse part = ".") Then Return Nothing
        Return key
    End Function
End Module
#End If
