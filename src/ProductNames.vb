Option Strict On

''' <summary>
''' The one place the product's visible names are spelled. Everything a person can read cites a
''' constant from here (or the matching row of docs/guides/PRODUCT_NAMING.md), so the next rename is
''' one edit instead of a hunt - the previous rename left 33 files saying the old name.
'''
''' Both viewers and the Share Manager compile this file (the net48 project lists it by hand, the
''' modern project links ..\**\*.vb, the Share Manager links it explicitly). It carries no seam: the
''' names do not differ between the runtimes.
'''
''' What is NOT here, on purpose: every frozen technical anchor (exe names, mutexes, the registry
''' hive, ProgIDs, the Inno AppId, the MSIX identity, the winget identifier). Those are correlated on
''' by programs and stores, not read by people, and PRODUCT_NAMING.md section 2 lists them.
''' </summary>
Friend Module ProductNames

    ''' <summary>The full name. Window title, About page, exe properties, installer, README, site.</summary>
    Public Const Full As String = "Fast Media Sorter & Sharing for Windows"

    ''' <summary>The compact name, for title-length and label-length places (page titles, tray and menu
    ''' items, file-type descriptions). It is NOT the Microsoft Store name: the Store listing, the MSIX
    ''' DisplayName, Add/Remove Programs and winget keep the frozen FastMediaSorter LITE.</summary>
    Public Const Compact As String = "Fast Media Sorter & Sharing"

    ''' <summary>The previous short name, kept but outdated. Running prose after the first full-name
    ''' mention only - never a title, a heading, a store field or a window caption.</summary>
    Public Const ShortLegacy As String = "Fast Media Sorter"

    ''' <summary>Alias for this product (Windows). Technical text only, never UI.</summary>
    Public Const AliasW As String = "FMS_W"

    ''' <summary>Alias for the Android product. Technical text only, never UI.</summary>
    Public Const AliasA As String = "FMS_A"

    ''' <summary>The byline the main window's title bar carries after the full name.</summary>
    Public Const Author As String = "SZA"

    ''' <summary>The main window's title bar text.</summary>
    Public Const WindowTitle As String = Full & " by " & Author

    ''' <summary>
    ''' The same names with the ampersand doubled, for a WinForms Label, Button or menu item whose
    ''' UseMnemonic is on: a single ampersand there underlines the next letter and is not drawn. A
    ''' Form.Text, a NotifyIcon.Text and a MessageBox need the plain forms above.
    ''' </summary>
    Public Const FullForLabel As String = "Fast Media Sorter && Sharing for Windows"

    Public Const CompactForLabel As String = "Fast Media Sorter && Sharing"

    ''' <summary>The Share Manager's visible name (PRODUCT_NAMING.md Q-2, answer B: component first,
    ''' product as suffix). Its FindWindowW handshake caption is a different, frozen string.</summary>
    Public Const ShareManager As String = "Share Manager - " & Compact

    Public Const ShareManagerForLabel As String = "Share Manager - " & CompactForLabel

    ''' <summary>The Server edition's visible installer and shortcut name (Q-3). Its Add/Remove
    ''' Programs entry and winget PackageName are frozen once published and are not read from here.</summary>
    Public Const ServerEdition As String = Compact & ": Folder Share Server"

    ''' <summary>The exchange server's visible name (tickets 133, 142; PRODUCT_NAMING.md section 1). Never
    ''' shortened by dropping its first word, which would read as Microsoft's product; not translated and Latin
    ''' in every language - only the role description around it (Russian: "сервер обмена") translates. A
    ''' caption cites this constant or a placeholder (<c>TF("... {0} ...", ProductNames.ExchangeServer)</c>),
    ''' never a literal. Its frozen anchors (exe, data folder, service name) are listed in section 2 of
    ''' the naming table and are not read from here.</summary>
    Public Const ExchangeServer As String = "Media Exchange Server"

End Module
