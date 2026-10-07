Option Strict On

Imports System.IO
Imports System.Text

''' <summary>
''' Opens the bundled, offline port-forwarding guide in the default browser. The
''' guide (assets\help\port-forward.html, embedded as FmsPayloadhelp/port-forward.html)
''' is trilingual (EN/RU/UK, switchable in-page) and assumes an English router UI.
''' The user's concrete values (router URL, external port, LAN IP, internal port)
''' are injected into the template, then it is written next to the temp folder and
''' opened - no internet needed.
''' </summary>
Public Module ShareGuide

    Private Const AssetPath As String = "help/port-forward.html"

    ''' <summary>The Windows product's website (GitHub Pages).</summary>
    Public Const SiteUrl As String = ShareLinks.SiteUrl

    ''' <summary>Online trilingual "How to publish your folders for Android" page.</summary>
    Public Const SiteGuideUrl As String = ShareLinks.SiteGuideUrl

    ''' <summary>FileDO's own guide to its virtual disks.</summary>
    Public Const FileDoDisksGuideUrl As String = ShareLinks.FileDoDisksGuideUrl

    ''' <summary>FileDO project website.</summary>
    Public Const FileDoSiteUrl As String = ShareLinks.FileDoSiteUrl

    ''' <summary>FileDO reciprocal guide for sharing disks with Fast Media Sorter.</summary>
    Public Const FileDoSharingGuideUrl As String = ShareLinks.FileDoSharingGuideUrl

    ''' <summary>The Android app's landing page (per language).</summary>
    Public Const AndroidSiteEn As String = SitePages.AndroidAppSiteRoot
    Public Const AndroidSiteRu As String = SitePages.AndroidAppSiteRoot & "index-ru.html"
    Public Const AndroidSiteUk As String = SitePages.AndroidAppSiteRoot & "index-uk.html"

    ''' <summary>Android app site URL for the current UI language. Only three landing
    ''' pages exist, so anything outside ru/uk lands on the English one - same rule as
    ''' Localization.GuideCode(), which is where that decision lives.</summary>
    Public Function AndroidSite() As String
        Select Case Localization.GuideCode()
            Case "ru" : Return AndroidSiteRu
            Case "uk" : Return AndroidSiteUk
            Case Else : Return AndroidSiteEn
        End Select
    End Function

    ''' <summary>Renders the guide with the given values + UI language and opens it.
    ''' <paramref name="routerModel"/>/<paramref name="routerSearchUrl"/> (optional)
    ''' fill the "detected router" block + its web-search link. Returns False if the
    ''' template is missing or the browser could not open.</summary>
    Public Function OpenPortForwardGuide(routerUrl As String, extPort As Integer, lanIp As String, port As Integer,
                                         Optional routerModel As String = "", Optional routerSearchUrl As String = "") As Boolean
        Dim html As String
        Try
            Using s As Stream = CompanionAssets.OpenBundledAsset(AssetPath)
                If s Is Nothing Then Return False
                Using sr As New StreamReader(s, Encoding.UTF8)
                    html = sr.ReadToEnd()
                End Using
            End Using
        Catch
            Return False
        End Try

        html = RenderGuide(html, routerUrl, extPort, lanIp, port, routerModel, routerSearchUrl)

        Try
            Dim outDir As String = Path.Combine(Path.GetTempPath(), "FastMediaSorter")
            Directory.CreateDirectory(outDir)
            Dim outPath As String = Path.Combine(outDir, "port-forward-help.html")
            File.WriteAllText(outPath, html, New UTF8Encoding(False))
            Return NetworkInfo.OpenInBrowser(outPath)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' The template with the values in. Every value is HTML-encoded: the router model comes
    ''' from whatever device answered the SSDP search, and it lands inside markup of a page
    ''' this app writes to disk and opens in the browser (ticket 054 H-17). Encoding covers the
    ''' attribute uses too (href="..") - quotes included.
    ''' </summary>
    Friend Function RenderGuide(template As String, routerUrl As String, extPort As Integer, lanIp As String, port As Integer,
                                routerModel As String, routerSearchUrl As String) As String
        Dim enc As Func(Of String, String) = Function(v) System.Net.WebUtility.HtmlEncode(If(v, ""))
        Return If(template, "").
            Replace("__ROUTER_URL__", enc(routerUrl)).
            Replace("__EXT_PORT__", If(extPort > 0, extPort.ToString(), "-")).
            Replace("__LAN_IP__", enc(If(String.IsNullOrEmpty(lanIp), "-", lanIp))).
            Replace("__PORT__", If(port > 0, port.ToString(), "-")).
            Replace("__ROUTER_MODEL__", enc(routerModel)).
            Replace("__ROUTER_SEARCH_URL__", enc(If(String.IsNullOrEmpty(routerSearchUrl), "https://www.google.com/search?q=how+to+set+up+port+forwarding", routerSearchUrl))).
            Replace("__SITE_GUIDE_URL__", ShareLinks.Localized(SiteGuideUrl)).
            Replace("__SITE_URL__", ShareLinks.Localized(SiteUrl)).
            Replace("__LANG__", Localization.GuideCode())
    End Function

End Module
