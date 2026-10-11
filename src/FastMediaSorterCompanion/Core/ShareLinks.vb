Option Strict On

''' <summary>
''' Central registry of external documentation and site links used by Share Manager.
''' </summary>
Public Module ShareLinks
    ''' <summary>The Windows product's website (GitHub Pages).</summary>
    Public Const SiteUrl As String = "https://serzhyale.github.io/FastMediaSorter_Lite/"

    ''' <summary>Online trilingual "How to publish your folders for Android" page.</summary>
    Public Const SiteGuideUrl As String = "https://serzhyale.github.io/FastMediaSorter_Lite/publish-folders-android.html"

    ''' <summary>Where the Server edition is obtained.</summary>
    Public Const ServerEditionUrl As String = "https://serzhyale.github.io/FastMediaSorter_Lite/server.html"

    ''' <summary>FileDO's own guide to its virtual disks (the Disk Manager, mounting, unmounting).</summary>
    Public Const FileDoDisksGuideUrl As String = "https://serzhyale.github.io/FileDO/guides/virtual-disks.html"

    ''' <summary>FileDO project website.</summary>
    Public Const FileDoSiteUrl As String = "https://serzhyale.github.io/FileDO/"

    ''' <summary>FileDO reciprocal guide for sharing disks with Fast Media Sorter.</summary>
    Public Const FileDoSharingGuideUrl As String = "https://serzhyale.github.io/FileDO/guides/fms-sharing.html"

    ''' <summary>A page of this product's site (the constants above) in the language the Share Manager is
    ''' speaking - see <see cref="SitePages.ForLanguage"/>.</summary>
    Public Function Localized(address As String) As String
        Return SitePages.ForLanguage(address, Localization.CurrentCode)
    End Function

    ''' <summary>
    ''' A page of the media exchange portal (the product family's shared "media exchange" section, on the
    ''' Android app's site) in the UI language: <paramref name="page"/> is "" for the start page or one of the
    ''' names <see cref="SitePages.ExchangePortalUrl"/> documents. The portal is not on this product's address
    ''' list - see <see cref="SitePages.ExchangePortalRoot"/>.
    ''' </summary>
    Public Function ExchangePortalUrl(page As String) As String
        Return SitePages.ExchangePortalUrl(page, Localization.CurrentCode)
    End Function

    ''' <summary>
    ''' A page of FileDO's site in the UI language. FileDO has a full copy in German and French (a folder in
    ''' front of the path); its Russian and Ukrainian landing is a stub at ru/ and ua/ that remembers the
    ''' language and opens the root page, which switches in-page. Its guides have no Russian or Ukrainian
    ''' address, so for those the English one stays.
    ''' </summary>
    Public Function FileDoLocalized(address As String) As String
        Dim code As String = Localization.CurrentCode
        If String.IsNullOrEmpty(address) OrElse Not address.StartsWith(FileDoSiteUrl, StringComparison.Ordinal) Then Return address
        Dim rest As String = address.Substring(FileDoSiteUrl.Length)
        Select Case code
            Case "de", "fr" : Return FileDoSiteUrl & code & "/" & rest
            Case "ru" : Return If(rest.Length = 0, FileDoSiteUrl & "ru/", address)
            Case "uk" : Return If(rest.Length = 0, FileDoSiteUrl & "ua/", address)
            Case Else : Return address
        End Select
    End Function
End Module
