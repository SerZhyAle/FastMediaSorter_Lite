Option Strict On

Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Router detection text versus the port-forward guide (ticket 054 H-17). Whatever answers
''' the SSDP search - or sits at the gateway address - controls the model and manufacturer
''' strings, and those land in a local HTML page this app writes to %TEMP% and opens in the
''' browser. The rule under test: a device-supplied value never reaches the page as live
''' markup, no matter how it was encoded on the way in.
''' </summary>
Public Class RouterInfoTests

    <Fact>
    Public Sub ParseDeviceXml_EncodedMarkupIsStrippedNotRevived()
        Dim id As RouterIdentity = RouterInfo.ParseDeviceXml(
            "<root><device>" &
            "<manufacturer>ACME</manufacturer>" &
            "<modelName>&lt;b&gt;Archer&lt;/b&gt; C7&lt;img src=x onerror=alert(1)&gt;</modelName>" &
            "</device></root>")
        Assert.DoesNotContain("<", id.Model)
        Assert.DoesNotContain(">", id.Model)
        ' The words survive; the markup - a decoded tag with everything inside it - does not.
        Assert.Equal("Archer C7", id.Model)
    End Sub

    <Fact>
    Public Sub ParseDeviceXml_DoubleEncodedMarkupIsStrippedToo()
        ' Decode-then-strip, in that order: the other way round, "&amp;lt;img..&amp;gt;"
        ' survived the tag strip and was decoded into live markup afterwards.
        Dim id As RouterIdentity = RouterInfo.ParseDeviceXml(
            "<root><device><modelName>&amp;lt;img src=x onerror=alert(1)&amp;gt;</modelName></device></root>")
        Assert.DoesNotContain("<", id.Model)
        Assert.DoesNotContain(">", id.Model)
    End Sub

    <Fact>
    Public Sub CleanText_DropsEverythingHtmlOrAShellCouldMean()
        Dim cleaned As String = RouterInfo.CleanText("<b>TP-Link</b> "" `' & Archer")
        Assert.False(cleaned.Contains("<"c))
        Assert.False(cleaned.Contains(">"c))
        Assert.False(cleaned.Contains(""""c))
        Assert.False(cleaned.Contains("'"c))
        Assert.False(cleaned.Contains("`"c))
        Assert.False(cleaned.Contains("&"c))
        Assert.Contains("TP-Link", cleaned)
    End Sub

    <Fact>
    Public Sub CleanText_ControlCharactersBecomeSpaces()
        Dim cleaned As String = RouterInfo.CleanText("A" & ChrW(1) & "B" & vbCrLf & "C")
        Assert.False(cleaned.Contains(ChrW(1)))
        Assert.Equal("A B C", cleaned)
    End Sub

    <Theory>
    <InlineData("http://192.168.0.1:5000/rootDesc.xml")>
    <InlineData("http://10.0.0.1/desc.xml")>
    <InlineData("http://172.16.5.1:49152/igd.xml")>
    <InlineData("http://172.31.255.254/igd.xml")>
    <InlineData("http://169.254.1.1/igd.xml")>
    <InlineData("http://100.64.0.1/igd.xml")>
    <InlineData("http://[fe80::1]:5000/igd.xml")>
    <InlineData("http://[fd00::1]/igd.xml")>
    <InlineData("http://127.0.0.1:1900/igd.xml")>
    Public Sub IsLanLocation_AcceptsAddressesOnThisNetwork(url As String)
        Assert.True(RouterInfo.IsLanLocation(url))
    End Sub

    <Theory>
    <InlineData("http://203.0.113.5/igd.xml")>
    <InlineData("http://8.8.8.8/igd.xml")>
    <InlineData("http://172.32.0.1/igd.xml")>
    <InlineData("http://100.128.0.1/igd.xml")>
    <InlineData("http://[2001:db8::1]/igd.xml")>
    <InlineData("http://router.example.com/igd.xml")>
    <InlineData("https://192.168.0.1/igd.xml")>
    <InlineData("file:///C:/igd.xml")>
    <InlineData("192.168.0.1/igd.xml")>
    <InlineData("")>
    Public Sub IsLanLocation_RefusesPublicNamedOrNonHttpLocations(url As String)
        ' LOCATION is device-supplied text: a public address, a name (a DNS lookup) or another
        ' scheme must never be fetched on a LAN device's say-so (ticket 054 H-17, first hop).
        Assert.False(RouterInfo.IsLanLocation(url))
    End Sub

    <Fact>
    Public Sub RenderGuide_EncodesEveryDeviceSuppliedValue()
        Dim html As String = ShareGuide.RenderGuide(
            "<p id='m'>__ROUTER_MODEL__</p><a href='__ROUTER_URL__'>x</a>",
            "http://192.168.0.1/""onload=""x", 2222, "192.168.1.50", 2222,
            "<script>alert(1)</script>", "")
        Assert.DoesNotContain("<script>", html)
        Assert.Contains("&lt;script&gt;", html)
        ' The URL lands inside an href - quotes included in what encoding covers.
        Assert.DoesNotContain("""onload=", html)
    End Sub

End Class
