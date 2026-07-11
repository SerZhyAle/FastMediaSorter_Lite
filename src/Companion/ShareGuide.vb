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

    ''' <summary>Fast Media Sorter for Windows website (GitHub Pages).</summary>
    Public Const SiteUrl As String = "https://serzhyale.github.io/FastMediaSorter_Lite/"

    ''' <summary>Online trilingual "How to publish your folders for Android" page.</summary>
    Public Const SiteGuideUrl As String = "https://serzhyale.github.io/FastMediaSorter_Lite/publish-folders-android.html"

    ''' <summary>The companion FastMediaSorter Android app landing page (per language).</summary>
    Public Const AndroidSiteEn As String = "https://serzhyale.github.io/FastMediaSorter_mob_v2/"
    Public Const AndroidSiteRu As String = "https://serzhyale.github.io/FastMediaSorter_mob_v2/index-ru.html"
    Public Const AndroidSiteUk As String = "https://serzhyale.github.io/FastMediaSorter_mob_v2/index-uk.html"

    ''' <summary>Android app site URL for the current UI language.</summary>
    Public Function AndroidSite(rus As Boolean) As String
        Return If(rus, AndroidSiteRu, AndroidSiteEn)
    End Function

    ''' <summary>Renders the guide with the given values + UI language and opens it.
    ''' <paramref name="routerModel"/>/<paramref name="routerSearchUrl"/> (optional)
    ''' fill the "detected router" block + its web-search link. Returns False if the
    ''' template is missing or the browser could not open.</summary>
    Public Function OpenPortForwardGuide(routerUrl As String, extPort As Integer, lanIp As String, port As Integer, rus As Boolean,
                                         Optional routerModel As String = "", Optional routerSearchUrl As String = "") As Boolean
        Dim html As String
        Try
            Using s As Stream = RuntimeBootstrap.OpenBundledAsset(AssetPath)
                If s Is Nothing Then Return False
                Using sr As New StreamReader(s, Encoding.UTF8)
                    html = sr.ReadToEnd()
                End Using
            End Using
        Catch
            Return False
        End Try

        html = html.
            Replace("__ROUTER_URL__", If(routerUrl, "")).
            Replace("__EXT_PORT__", If(extPort > 0, extPort.ToString(), "-")).
            Replace("__LAN_IP__", If(String.IsNullOrEmpty(lanIp), "-", lanIp)).
            Replace("__PORT__", If(port > 0, port.ToString(), "-")).
            Replace("__ROUTER_MODEL__", If(routerModel, "")).
            Replace("__ROUTER_SEARCH_URL__", If(String.IsNullOrEmpty(routerSearchUrl), "https://www.google.com/search?q=how+to+set+up+port+forwarding", routerSearchUrl)).
            Replace("__SITE_GUIDE_URL__", SiteGuideUrl).
            Replace("__SITE_URL__", SiteUrl).
            Replace("__LANG__", If(rus, "ru", "en"))

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

End Module
