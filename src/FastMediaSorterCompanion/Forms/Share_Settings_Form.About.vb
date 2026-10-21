Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The About group of the Share Manager's "Справка" page (ticket 243 T-6, SETTINGS-NAVIGATION 2.0 section 3 rules 3 and 4).
''' Twelve required items, each under the control name <see cref="AboutItems.ManagerControl"/> gives it:
''' the description, the running version, the update statement, the diagnostic export, the help, the project site, the
''' repository, the licence (its text, from the file bundled with the program), the third-party notices, the privacy
''' policy, the contact and the related projects as one compact group. The texts and addresses come from
''' <see cref="ProductLinks"/> - the table the viewer's About page is built from - so an address that moves is corrected once.
'''
''' Rule 3.4: building this page contacts nothing. A row is a label or a link; an address is opened by
''' <see cref="OnAboutLinkClicked"/> on a click and nowhere else, through <see cref="AboutResources"/> (the licence, the
''' notices and the help open from local disk when the file is there, their web page otherwise). A link row is a
''' <see cref="UiSizing.PointerTarget"/> high hit area (UI-SIZING 1.1).
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    ''' <summary>The licence's name as the About rows show it - a name, not a sentence, so it is not translated; it is
    ''' a constant (and not a literal argument) so that a placeholder can carry it and the parity scan does not read it
    ''' as a key.</summary>
    Private Const LicenseName As String = "MIT"

    ''' <summary>The help surface of this page in help-map.json (<c>share-settings.help</c>).</summary>
    Private Const AboutHelpSurface As String = "share-settings.help"

    ''' <summary>What a link row opens: its key in <see cref="ProductLinks"/> and, for the help, the surface id. The
    ''' address itself is built at the click, so a language switched while the window is open is honoured.</summary>
    Private NotInheritable Class AboutTarget
        Friend ReadOnly LinkKey As String
        Friend ReadOnly HelpSurface As String
        Friend Sub New(linkKey As String, helpSurface As String)
            Me.LinkKey = linkKey
            Me.HelpSurface = helpSurface
        End Sub
    End Class

    ''' <summary>The About controls whose text depends on the language or on a value: the control and what makes its text.</summary>
    Private ReadOnly _aboutTexts As New List(Of KeyValuePair(Of Control, Func(Of String)))()

    Private _aboutHeadingFont As Font

    Private Shared Function AboutVersion() As String
        Return Application.ProductVersion.Split("+"c)(0)
    End Function

    ''' <summary>Gives <paramref name="target"/> its text and remembers how to make it again after a language switch.</summary>
    Private Sub BindAboutText(target As Control, text As Func(Of String))
        _aboutTexts.Add(New KeyValuePair(Of Control, Func(Of String))(target, text))
        ApplyAboutText(target, text)
    End Sub

    Private Shared Sub ApplyAboutText(target As Control, text As Func(Of String))
        target.Text = text()
        If TypeOf target Is LinkLabel Then target.AccessibleName = target.Text
    End Sub

    Private Sub RefreshAboutTexts()
        For Each entry In _aboutTexts
            ApplyAboutText(entry.Key, entry.Value)
        Next
    End Sub

    Private Sub BuildAboutRows(section As CollapsibleSection)
        ' The order of AboutItems.Required. _aboutStart opens the "about" group (BuildContractGroups).
        _aboutStart = New Label With {.AutoSize = True, .Dock = DockStyle.Top, .Name = "managerProductName", .Text = ProductNames.FullForLabel}
        section.AddBodyRow(_aboutStart)

        Dim description As New Label With {.AutoSize = True, .Dock = DockStyle.Top, .Name = "managerDescription"}
        BindAboutText(description, Function() Localization.T("Раздаёт папки этого компьютера Android-телефону, подключается к серверу обмена медиа и по расписанию выполняет операции с файлами."))
        section.AddBodyRow(description)

        Dim version As New Label With {.AutoSize = True, .Dock = DockStyle.Top, .Name = "managerVersion"}
        BindAboutText(version, Function() Localization.TF("Версия: {0}", AboutVersion()))
        section.AddBodyRow(version)

        ' Not a check and not a download: this program never asks the network whether a newer version exists, and the row
        ' says so, with the running version and the three places an update comes from. The link opens the releases page.
        section.AddBodyRow(NewAboutLinkRow("managerUpdates", ProductLinks.Releases, "",
            Function() Localization.T("Новые версии"),
            Function() Localization.TF("Установлена версия {0}. Программа сама не проверяет обновления и ничего не скачивает: новые версии выходят на странице релизов GitHub, в winget и в Microsoft Store.", AboutVersion())))

        ' The diagnostic export is the Manager's one support command (owner decision): Forms/SendLogsFlow.vb packs the
        ' same archive the viewer packs. It stays the only in-settings support-bundle button.
        Dim support As New Button With {.Text = Localization.T("Отправить журнал автору.."), .AutoSize = True,
            .Name = "managerSupportBundle", .Margin = New Padding(0, 8, 0, 0)}
        ButtonGlyphs.Apply(support, "action.email")
        AddHandler support.Click, Async Sub() Await SendLogsFlow.RunAsync(Me)
        section.AddBodyRow(support)

        section.AddBodyRow(NewAboutLinkRow("managerHelp", ProductLinks.Help, AboutHelpSurface,
            Function() Localization.T("Справка по программе"),
            Function() Localization.T("Встроенная справка на языке интерфейса. Если её нет на этом компьютере, откроется страница с темами на сайте.")))
        section.AddBodyRow(NewAboutLinkRow("managerSite", ProductLinks.Site, "",
            Function() Localization.T("Сайт проекта"),
            Function() Localization.T("Описание возможностей, инструкции и материалы проекта.")))
        section.AddBodyRow(NewAboutLinkRow("managerRepository", ProductLinks.Repository, "",
            Function() "GitHub",
            Function() Localization.T("Исходный код, задачи и техническая документация.")))

        ' The licence is read here, from the file bundled with the program: no link, no network.
        Dim license As New Label With {.AutoSize = True, .Dock = DockStyle.Top, .Name = "managerLicense"}
        BindAboutText(license, Function() Localization.TF("Лицензия: {0}", LicenseName))
        section.AddBodyRow(license)
        Dim licenseText As String
        Using stream = CompanionAssets.OpenBundledAsset("LICENSE"), reader As New IO.StreamReader(stream)
            licenseText = reader.ReadToEnd().Replace(vbCrLf, vbLf).Replace(vbLf, Environment.NewLine)
        End Using
        section.AddBodyRow(New TextBox With {.Text = licenseText, .Multiline = True, .ReadOnly = True,
            .ScrollBars = ScrollBars.Vertical, .Dock = DockStyle.Top, .Height = 140, .Width = 400,
            .AccessibleName = Localization.T("Лицензия"), .Name = "managerLicenseText"})

        section.AddBodyRow(NewAboutLinkRow("managerNotices", ProductLinks.Notices, "",
            Function() Localization.T("Сторонние компоненты и лицензии"),
            Function() Localization.T("Используемые библиотеки и тексты их лицензий. Открывается файл рядом с программой; без него - страница на GitHub.")))
        section.AddBodyRow(NewAboutLinkRow("managerPrivacy", ProductLinks.Privacy, "",
            Function() Localization.T("Политика конфиденциальности"),
            Function() Localization.T("Как приложение обрабатывает пользовательские данные.")))
        section.AddBodyRow(NewAboutLinkRow("managerContact", ProductLinks.Contact, "",
            Function() Localization.T("Связаться с автором"),
            Function() Localization.T("Написать автору проекта по электронной почте.")))

        section.AddBodyRow(NewAboutRelatedGroup())
    End Sub

    ''' <summary>The related projects as one compact group: a heading, the sentence that says the pages open on a click
    ''' only, and one link row each with its purpose.</summary>
    Private Function NewAboutRelatedGroup() As Control
        Dim group As New FlowLayoutPanel With {.Name = "managerRelated", .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Dock = DockStyle.Top, .Margin = New Padding(0, 8, 0, 0)}
        _aboutHeadingFont = New Font(Me.Font, FontStyle.Bold)
        Dim heading As New Label With {.AutoSize = True, .Name = "managerRelatedHeading", .Font = _aboutHeadingFont, .Margin = New Padding(0, 0, 0, 2)}
        BindAboutText(heading, Function() Localization.T("Связанные проекты"))
        group.Controls.Add(heading)
        Dim intro As New Label With {.AutoSize = True, .Name = "managerRelatedIntro", .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 0, 0, 2)}
        BindAboutText(intro, Function() Localization.T("Другие программы автора, которые работают вместе с этой. Страницы открываются только по щелчку."))
        group.Controls.Add(intro)
        For Each key In ProductLinks.RelatedKeys
            Select Case key
                Case ProductLinks.AndroidApp
                    group.Controls.Add(NewAboutLinkRow("managerRelatedAndroid", key, "",
                        Function() Localization.T("Приложение для Android"),
                        Function() Localization.T("Страница приложения для Android.")))
                Case ProductLinks.Portal
                    group.Controls.Add(NewAboutLinkRow("managerRelatedPortal", key, "",
                        Function() Localization.T("Портал обмена медиа"),
                        Function() Localization.T("Как открыть папки ПК и телефона на других устройствах: домашняя сеть, проброс порта, постоянный сервер.")))
                Case ProductLinks.DocHtml
                    group.Controls.Add(NewAboutLinkRow("managerRelatedDocHtml", key, "",
                        Function() Localization.T("Сайт Doc HTML Translate"),
                        Function() Localization.T("Описание возможностей, поддерживаемых форматов, установки и работы с документами и изображениями.")))
            End Select
        Next
        Return group
    End Function

    ''' <summary>One link row: the link itself (named <paramref name="name"/>, a pointer-target high hit area) and,
    ''' beneath it, what it is for. <paramref name="linkKey"/> is a <see cref="ProductLinks"/> key.</summary>
    Private Function NewAboutLinkRow(name As String, linkKey As String, helpSurface As String,
                                     caption As Func(Of String), purpose As Func(Of String)) As FlowLayoutPanel
        Dim row As New FlowLayoutPanel With {.Name = name & "Row", .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Dock = DockStyle.Top, .Margin = New Padding(0, 0, 0, 4)}
        Dim link As New LinkLabel With {.AutoSize = True, .Name = name, .Margin = Padding.Empty,
            .MinimumSize = New Size(0, UiSizing.PointerTarget), .TextAlign = ContentAlignment.MiddleLeft,
            .Tag = New AboutTarget(linkKey, helpSurface)}
        BindAboutText(link, caption)
        AddHandler link.LinkClicked, AddressOf OnAboutLinkClicked
        row.Controls.Add(link)
        If purpose IsNot Nothing Then
            Dim note As New Label With {.AutoSize = True, .Name = name & "Purpose", .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 0, 0, 2)}
            BindAboutText(note, purpose)
            row.Controls.Add(note)
        End If
        Return row
    End Function

    ''' <summary>The one place an About address is opened, and only from a click (section 3 rule 3.4). A failure is logged
    ''' and the address is printed in the message, so the reader can open it by hand (APP-BEHAVIOUR rule 6).</summary>
    Private Sub OnAboutLinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Dim link As LinkLabel = TryCast(sender, LinkLabel)
        Dim target As AboutTarget = If(link Is Nothing, Nothing, TryCast(link.Tag, AboutTarget))
        If target Is Nothing Then Return
        Dim language As String = Localization.CurrentCode
        Dim address As String = ProductLinks.Url(target.LinkKey, language, target.HelpSurface)
        Try
            AboutResources.Open(address, language)
        Catch ex As Exception
            AppFileLogger.LogException("Manager About: opening a link", ex)
            Dim manual As String = If(address.StartsWith("mailto:", StringComparison.Ordinal), MailSender.AuthorAddress,
                                      AboutResources.ManualAddress(address, language))
            MessageBox.Show(Me, Localization.TF("Не удалось открыть ссылку в браузере. Откройте её вручную: {0}", manual),
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub
End Class
