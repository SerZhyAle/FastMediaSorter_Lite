#If NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Net
Imports Xunit

''' <summary>
''' WebVideoMarkup - the x86 viewer's &lt;source&gt; tag (audit 046, D-06). The file URI
''' went into a single-quoted attribute unencoded, and Uri.AbsoluteUri leaves an apostrophe
''' alone: "Don't.mp4" cut the src short, and a crafted name added its own event handler to
''' a page whose window.external was the main window. net48 only, like the WebBrowser.
''' </summary>
Public Class WebVideoMarkupTests

    Private Const Prefix As String = "<source src='"
    Private Const Suffix As String = "' onerror=""fmsReportVideoError('Error: Unsupported video type (source failed to load)');"">"

    ''' <summary>The attribute value as the browser sees it: everything up to the FIRST
    ''' closing apostrophe, then HTML-decoded. Whatever follows must be our own suffix,
    ''' byte for byte - or the name has broken out of the attribute.</summary>
    Private Shared Function SrcAsTheBrowserReadsIt(tag As String) As String
        Assert.StartsWith(Prefix, tag)
        Dim close As Integer = tag.IndexOf("'"c, Prefix.Length)
        Assert.True(close > 0)
        Assert.Equal(Suffix, tag.Substring(close))
        Return WebUtility.HtmlDecode(tag.Substring(Prefix.Length, close - Prefix.Length))
    End Function

    Private Shared Function UriOf(fileName As String) As String
        Return New Uri(Path.Combine("C:\videos", fileName)).AbsoluteUri
    End Function

    <Theory>
    <InlineData("Don't stop.mp4")>
    <InlineData("a'onerror='external.SetVolume(0.01)'.mp4")>
    <InlineData("R&B 100%.mp4")>
    <InlineData("(1) ;=x.mp4")>
    Public Sub The_file_uri_stays_inside_its_attribute_and_decodes_back_unchanged(fileName As String)
        Dim uri As String = UriOf(fileName)

        Dim tag As String = WebVideoMarkup.SourceTag(uri)

        Assert.Equal(uri, SrcAsTheBrowserReadsIt(tag))
    End Sub

    <Fact>
    Public Sub The_injection_payload_adds_no_handler_of_its_own()
        Dim tag As String = WebVideoMarkup.SourceTag(UriOf("a'onerror='external.SetVolume(0.01)'.mp4"))

        ' The payload's text is still there - as inert, encoded characters of the URL -
        ' and never as markup: no raw apostrophe can close the attribute early. That the
        ' only thing after the value is our own onerror is the theory above.
        Assert.Contains("&#39;onerror=&#39;external.SetVolume(0.01)&#39;", tag)
        Assert.DoesNotContain("'onerror='", tag)
    End Sub

    <Fact>
    Public Sub Every_character_that_can_end_or_open_markup_is_encoded()
        Assert.Equal("&amp;&#39;&quot;&lt;&gt;plain", WebVideoMarkup.AttributeValue("&'""<>plain"))
        Assert.Equal("", WebVideoMarkup.AttributeValue(Nothing))
    End Sub

End Class
#End If
