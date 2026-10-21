Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 244, WEB-BINDING (the operator surface of a server that serves the browser client):
'''  - the canonical texts of certificate-trust 4 (the warning, and the sentence of the surface that renews) and of
'''    page-served-by-the-server 3 (the page-served sentence) are pinned in English, Russian and Ukrainian, copied from the catalog;
'''  - the browser dialog says the truth when there is no fingerprint to show (no empty box, no "shown below");
'''  - the sign-in link of page-served-by-the-server 4 (https://&lt;x&gt;/#&lt;connection string&gt;) is a pure builder and the connection-string
'''    window offers it only when there is one.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeBrowserSurfaceTests

    ' ---- the canonical texts, verbatim from the catalog ------------------------------------------------------------

    Private Const WarningEn As String = "The browser will warn that the connection is not private, because this server made its own certificate. Its fingerprint is shown below; where your browser shows the certificate, you can compare the two. Go on only if you trust this server."
    Private Const WarningRu As String = "Браузер предупредит, что соединение не защищено, потому что сервер создал сертификат сам. Ниже его отпечаток: если ваш браузер показывает сертификат, можно сравнить их. Продолжайте, только если доверяете этому серверу."
    Private Const WarningUk As String = "Браузер попередить, що з'єднання не захищене, бо сервер створив сертифікат сам. Нижче його відбиток: якщо ваш браузер показує сертифікат, можна їх порівняти. Продовжуйте, лише якщо довіряєте цьому серверу."

    Private Const PageServedEn As String = "This page is served by the exchange server itself, so whoever runs the server could change it to read what you send. Use it only on a server run by you or by someone you trust."
    Private Const PageServedRu As String = "Эту страницу отдаёт сам сервер обмена, поэтому тот, кто им управляет, мог бы изменить её так, чтобы читать то, что вы отправляете. Пользуйтесь ею только на сервере, которым управляете вы сами или человек, которому вы доверяете."
    Private Const PageServedUk As String = "Цю сторінку віддає сам сервер обміну, тому той, хто ним керує, міг би змінити її так, щоб читати те, що ви надсилаєте. Користуйтеся нею лише на сервері, яким керуєте ви самі або людина, якій ви довіряєте."

    ' the catalog gives the renew sentence in English only ("every browser will ask again, so renew only when needed"); the Russian and the
    ' Ukrainian are the product's translations of it
    Private Const RenewEn As String = "Every browser will ask again, so renew only when needed."
    Private Const RenewRu As String = "Каждый браузер спросит снова, поэтому продлевайте сертификат только при необходимости."
    Private Const RenewUk As String = "Кожен браузер запитає знову, тому поновлюйте сертифікат лише за потреби."

    Private Shared Sub InLanguage(code As String, body As Action)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            body()
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Theory>
    <InlineData("en", WarningEn)>
    <InlineData("ru", WarningRu)>
    <InlineData("uk", WarningUk)>
    Public Sub The_browser_warning_is_the_catalog_text_verbatim(code As String, catalog As String)
        InLanguage(code, Sub() Assert.Equal(catalog, ExchangeAdminText.BrowserWarning()))
    End Sub

    <Theory>
    <InlineData("en", PageServedEn)>
    <InlineData("ru", PageServedRu)>
    <InlineData("uk", PageServedUk)>
    Public Sub The_page_served_sentence_is_the_catalog_text_verbatim(code As String, catalog As String)
        InLanguage(code, Sub() Assert.Equal(catalog, ExchangeAdminText.PageServedNotice()))
    End Sub

    <Theory>
    <InlineData("en", RenewEn)>
    <InlineData("ru", RenewRu)>
    <InlineData("uk", RenewUk)>
    Public Sub The_surface_that_renews_adds_the_catalog_sentence(code As String, expected As String)
        InLanguage(code, Sub() Assert.Equal(expected, ExchangeAdminText.RenewBrowserNote()))
        InLanguage("en", Sub() Assert.Contains("every browser will ask again, so renew only when needed", ExchangeAdminText.RenewBrowserNote().ToLowerInvariant()))
    End Sub

    <Fact>
    Public Sub The_renewal_note_no_longer_says_a_renewal_alone_spares_every_device()
        InLanguage("en", Sub()
                             Dim text As String = ExchangeAdminText.CertificateRenewalNote()
                             Assert.NotEqual("Renewal keeps the key. Replacing the key requires every device to confirm the certificate again.", text)
                             ' a browser asks again after ANY new certificate (certificate-trust 1 and 2), the programs of the product accept a same-key one
                             Assert.Contains("browser asks again after any new certificate", text)
                             Assert.Contains("programs accept such a certificate without asking", text)
                         End Sub)
    End Sub

    Private Shared Function NewTexts() As String()
        Return New String() {ExchangeAdminText.CertificateRenewalNote(), ExchangeAdminText.RenewBrowserNote(),
                             ExchangeAdminText.BrowserNoFingerprintNotice(), ExchangeAdminText.BrowserWarning(),
                             ExchangeAdminText.PageServedNotice(), ExchangeConnectionStringText.CopySignInLinkButton()}
    End Function

    <Fact>
    Public Sub Every_new_sentence_is_translated_into_every_language_and_free_of_smart_quotes()
        Dim russian As String() = Nothing
        InLanguage("ru", Sub() russian = NewTexts())
        For Each code As String In Localization.Codes
            Dim languageCode As String = code
            InLanguage(languageCode, Sub()
                                         Dim texts As String() = NewTexts()
                                         For i As Integer = 0 To texts.Length - 1
                                             Dim text As String = texts(i)
                                             Assert.False(String.IsNullOrWhiteSpace(text), languageCode & " #" & i)
                                             Assert.False(text.Contains(ChrW(&H201C)), "U+201C in " & languageCode & " #" & i)
                                             Assert.False(text.Contains(ChrW(&H201D)), "U+201D in " & languageCode & " #" & i)
                                             Assert.DoesNotContain("...", text)
                                             ' the table has the column: the Russian source is never what a reader of another language gets
                                             If languageCode <> "ru" Then Assert.NotEqual(russian(i), text)
                                         Next
                                     End Sub)
        Next
    End Sub

    ' ---- the browser dialog when there is no fingerprint -----------------------------------------------------------

    <Fact>
    Public Sub The_trust_notice_is_the_warning_with_a_fingerprint_and_the_honest_notice_without_one()
        Const hex As String = "AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99"
        For Each code As String In New String() {"en", "ru", "uk"}
            Dim languageCode As String = code
            InLanguage(languageCode, Sub()
                                         Assert.Equal(ExchangeAdminText.BrowserWarning(), ExchangeAdminText.BrowserTrustNotice(hex))
                                         Assert.Equal(ExchangeAdminText.BrowserNoFingerprintNotice(), ExchangeAdminText.BrowserTrustNotice(""))
                                         Assert.Equal(ExchangeAdminText.BrowserNoFingerprintNotice(), ExchangeAdminText.BrowserTrustNotice("   "))
                                         Assert.Equal(ExchangeAdminText.BrowserNoFingerprintNotice(), ExchangeAdminText.BrowserTrustNotice(Nothing))
                                     End Sub)
        Next
        ' the canonical warning promises a fingerprint "below"; the notice for a leaf with none must not
        InLanguage("en", Sub() Assert.DoesNotContain("below", ExchangeAdminText.BrowserNoFingerprintNotice()))
        InLanguage("ru", Sub() Assert.DoesNotContain("Ниже", ExchangeAdminText.BrowserNoFingerprintNotice()))
        InLanguage("uk", Sub() Assert.DoesNotContain("Нижче", ExchangeAdminText.BrowserNoFingerprintNotice()))
    End Sub

    Private Shared Function AllControls(root As Control) As List(Of Control)
        Dim all As New List(Of Control)()
        For Each child As Control In root.Controls
            all.Add(child)
            all.AddRange(AllControls(child))
        Next
        Return all
    End Function

    <Theory>
    <InlineData("", False)>
    <InlineData("AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99", True)>
    Public Sub The_browser_dialog_shows_the_fingerprint_box_only_when_there_is_a_fingerprint(hex As String, expectBox As Boolean)
        ExchangeTestSupport.OnUi(
            Sub()
                Dim saved As String = Localization.CurrentCode
                Try
                    Localization.CurrentCode = "en"
                    Using form As New Exchange_Admin_Browser_Form("https://exch.example.com:22022/#admin", hex)
                        Dim controls As List(Of Control) = AllControls(form)
                        Dim boxes As TextBox() = controls.OfType(Of TextBox)().ToArray()
                        Dim labels As String() = controls.OfType(Of Label)().Select(Function(l) l.Text).ToArray()
                        ' the URL box is always there; the fingerprint box (and its caption) only with a fingerprint
                        Assert.Equal(If(expectBox, 2, 1), boxes.Length)
                        Assert.Equal(expectBox, boxes.Any(Function(b) b.Text = hex))
                        Assert.False(boxes.Any(Function(b) b.Text.Length = 0), "an empty box is never shown")
                        Assert.Equal(expectBox, labels.Any(Function(t) t.Contains("SHA-256")))
                        Assert.Contains(If(expectBox, WarningEn, ExchangeAdminText.BrowserNoFingerprintNotice()), labels)
                    End Using
                Finally
                    Localization.CurrentCode = saved
                End Try
            End Sub)
    End Sub

    ' ---- the sign-in link (page-served-by-the-server 4, item AE) ---------------------------------------------------

    Private Shared Function WithWeb(Optional webDir As String = "C:\web") As ExchangeProcess.InspectReport
        Return New ExchangeProcess.InspectReport With {.WebDir = webDir}
    End Function

    Private Shared Function StringOf(endpoint As String, name As String, login As String) As String
        Dim built As ExchangeConnectionStringResult = ExchangeConnectionString.Build(endpoint, "", name, login)
        Assert.True(built.Succeeded, "test string: " & endpoint)
        Dim refusal As ExchangeConnectionStringResult = Nothing
        Dim text As String = ExchangeConnectionString.Format(built.Value, refusal)
        Assert.NotNull(text)
        Return text
    End Function

    <Fact>
    Public Sub The_link_is_https_endpoint_hash_and_the_string_unchanged()
        Dim text As String = "FMSEXCH1:x=203.0.113.7%3A22022&n=Home+NAS&u=alice"
        Assert.Equal("https://203.0.113.7:22022/#" & text, ExchangeProcess.BrowserSignInUrl(WithWeb(), text))
        Dim named As String = StringOf("exch.example.com:22022", "Home", "alice")
        Assert.Equal("https://exch.example.com:22022/#" & named, ExchangeProcess.BrowserSignInUrl(WithWeb(), named))
    End Sub

    <Fact>
    Public Sub The_host_of_the_link_is_the_one_of_the_string_in_canonical_form()
        Dim text As String = "FMSEXCH1:x=Exch.Example.COM%3A22022"
        Assert.Equal("https://exch.example.com:22022/#" & text, ExchangeProcess.BrowserSignInUrl(WithWeb(), text))
    End Sub

    <Fact>
    Public Sub An_IPv6_endpoint_is_bracketed()
        Dim text As String = StringOf("[2001:0DB8:0:0:0:0:0:7]:22022", "", "")
        Assert.Equal("https://[2001:db8::7]:22022/#" & text, ExchangeProcess.BrowserSignInUrl(WithWeb(), text))
    End Sub

    <Fact>
    Public Sub The_white_space_around_a_pasted_string_is_not_carried_into_the_link()
        Dim text As String = "FMSEXCH1:x=203.0.113.7%3A22022"
        Assert.Equal("https://203.0.113.7:22022/#" & text, ExchangeProcess.BrowserSignInUrl(WithWeb(), "  " & text & vbCrLf))
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("   ")>
    <InlineData("FMSEXCH1:")>
    <InlineData("FMSEXCH1:n=Home+NAS")>
    <InlineData("FMSEXCH1:x=")>
    <InlineData("FMSEXCH1:x=%3A22022")>
    <InlineData("FMSEXCH1:x=203.0.113.7")>
    <InlineData("https://exch.example.com:22022/#admin")>
    <InlineData("not a connection string")>
    Public Sub An_empty_or_unreadable_endpoint_means_no_link(text As String)
        Assert.Equal("", ExchangeProcess.BrowserSignInUrl(WithWeb(), text))
    End Sub

    <Fact>
    Public Sub No_string_means_no_link()
        Assert.Equal("", ExchangeProcess.BrowserSignInUrl(WithWeb(), Nothing))
    End Sub

    <Fact>
    Public Sub A_server_that_serves_no_web_client_has_no_link()
        Dim text As String = "FMSEXCH1:x=203.0.113.7%3A22022"
        Assert.Equal("", ExchangeProcess.BrowserSignInUrl(Nothing, text))
        Assert.Equal("", ExchangeProcess.BrowserSignInUrl(WithWeb(""), text))
        Assert.Equal("", ExchangeProcess.BrowserSignInUrl(WithWeb("   "), text))
    End Sub

    <Fact>
    Public Sub The_link_does_not_depend_on_the_administration_page()
        Dim report As New ExchangeProcess.InspectReport With {.WebDir = "C:\web", .AdminOn = False, .Listen = "", .PublicEndpoint = ""}
        Dim text As String = "FMSEXCH1:x=203.0.113.7%3A22022"
        Assert.Equal("https://203.0.113.7:22022/#" & text, ExchangeProcess.BrowserSignInUrl(report, text))
    End Sub

    ' ---- the connection-string window ------------------------------------------------------------------------------

    Private Shared Function Made(text As String) As Func(Of String, String, ExchangeProcess.ConnectionStringResult)
        Return Function(name, login) New ExchangeProcess.ConnectionStringResult With {.Outcome = ExchangeProcess.ConnectionStringOutcome.Made, .Text = text}
    End Function

    Private Shared Sub WaitForString(form As Exchange_Connection_String_Form)
        Dim until As DateTime = DateTime.UtcNow.AddSeconds(10)
        While form.CurrentText.Length = 0 AndAlso DateTime.UtcNow < until
            Application.DoEvents()
            Thread.Sleep(10)
        End While
        Assert.True(form.CurrentText.Length > 0, "the string was not made in time")
        Application.DoEvents()
    End Sub

    Private Shared Function LinkButtons(form As Exchange_Connection_String_Form) As Button()
        Dim caption As String = ExchangeConnectionStringText.CopySignInLinkButton()
        Return AllControls(form).OfType(Of Button)().Where(Function(b) b.Text = caption).ToArray()
    End Function

    <Fact>
    Public Sub The_window_offers_the_sign_in_link_beside_the_string_when_the_server_serves_the_client()
        ExchangeTestSupport.OnUi(
            Sub()
                Dim text As String = "FMSEXCH1:x=203.0.113.7%3A22022&n=Home+NAS"
                Dim report As ExchangeProcess.InspectReport = WithWeb()
                Using form As New Exchange_Connection_String_Form(Made(text), Function(s As String) ExchangeProcess.BrowserSignInUrl(report, s))
                    form.Show()
                    WaitForString(form)
                    Assert.Equal("https://203.0.113.7:22022/#" & text, form.CurrentSignInLink)
                    Dim buttons As Button() = LinkButtons(form)
                    Assert.Single(buttons)
                    Assert.True(buttons(0).Visible)
                    Assert.True(buttons(0).Enabled)
                    form.Close()
                End Using
            End Sub)
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub The_window_hides_the_sign_in_link_when_there_is_none(withBuilder As Boolean)
        ExchangeTestSupport.OnUi(
            Sub()
                Dim text As String = "FMSEXCH1:x=203.0.113.7%3A22022"
                ' a builder that finds no web client (the report names no directory) is the same as no builder at all
                Dim report As ExchangeProcess.InspectReport = WithWeb("")
                Dim builder As Func(Of String, String) = If(withBuilder, Function(s As String) ExchangeProcess.BrowserSignInUrl(report, s), Nothing)
                Using form As New Exchange_Connection_String_Form(Made(text), builder)
                    form.Show()
                    WaitForString(form)
                    Assert.Equal("", form.CurrentSignInLink)
                    For Each b As Button In LinkButtons(form)
                        Assert.False(b.Visible)
                    Next
                    form.Close()
                End Using
            End Sub)
    End Sub

End Class
