Option Strict On

Imports System.Globalization
Imports Xunit

''' <summary>
''' What language a given machine starts in - the part of
''' SPECIFICATION_THIRTEEN_UI_LANGUAGES.md §7.3 that can be proved without a German
''' Windows to hand. Manual check 6 ("first run on a German Windows shows German") is
''' exactly MatchCulture plus DefaultCode's order of preference, so it is pinned here
''' rather than left to a machine nobody has.
''' </summary>
Public Class LocalizationStartupTests

    <Theory>
    <InlineData("de-DE", "de")>
    <InlineData("de-AT", "de")>          ' a regional variant resolves to its language
    <InlineData("pt-BR", "pt")>
    <InlineData("zh-Hans-CN", "zh")>     ' three-part name, still Chinese
    <InlineData("ar-EG", "ar")>
    <InlineData("uk-UA", "uk")>
    <InlineData("ru-RU", "ru")>
    <InlineData("en-GB", "en")>
    Public Sub A_shipped_language_is_recognised_from_its_culture(culture As String, expected As String)
        Assert.Equal(expected, Localization.MatchCulture(New CultureInfo(culture)))
    End Sub

    <Theory>
    <InlineData("ja-JP")>                ' translated for OCR, but the UI is not in it
    <InlineData("pl-PL")>
    <InlineData("he-IL")>
    Public Sub An_unshipped_language_matches_nothing(culture As String)
        ' Nothing, not "en": DefaultCode is what decides the fallback, and it only gets
        ' to decide if this returns Nothing.
        Assert.Null(Localization.MatchCulture(New CultureInfo(culture)))
    End Sub

    <Fact>
    Public Sub The_invariant_culture_matches_nothing()
        Assert.Null(Localization.MatchCulture(CultureInfo.InvariantCulture))
        Assert.Null(Localization.MatchCulture(Nothing))
    End Sub

    ''' <summary>
    ''' The DISPLAY language wins over the language Windows was installed in: someone who
    ''' installed an English Windows and then switched the display to German is reading a
    ''' German desktop, and that is what "the OS language" means to them.
    ''' </summary>
    <Fact>
    Public Sub The_display_language_decides_the_first_run()
        Dim savedUi = CultureInfo.CurrentUICulture
        Try
            CultureInfo.CurrentUICulture = New CultureInfo("de-DE")
            Assert.Equal("de", Localization.DefaultCode())

            CultureInfo.CurrentUICulture = New CultureInfo("ar-SA")
            Assert.Equal("ar", Localization.DefaultCode())
        Finally
            CultureInfo.CurrentUICulture = savedUi
        End Try
    End Sub

    ''' <summary>
    ''' A language nobody translated falls back to ENGLISH, never to Russian - Russian is
    ''' only the translation table's source language, not a default for the world.
    ''' </summary>
    <Fact>
    Public Sub An_untranslated_display_language_falls_back_to_English_not_Russian()
        Dim savedUi = CultureInfo.CurrentUICulture
        Try
            CultureInfo.CurrentUICulture = New CultureInfo("ja-JP")
            Dim code = Localization.DefaultCode()
            ' InstalledUICulture is whatever this machine has; the only wrong answer is ru.
            Assert.NotEqual("ru", code)
            If Localization.MatchCulture(CultureInfo.InstalledUICulture) Is Nothing Then
                Assert.Equal("en", code)
            End If
        Finally
            CultureInfo.CurrentUICulture = savedUi
        End Try
    End Sub

    ''' <summary>
    ''' A code written by a newer build - or by the x64 exe when the x86 one is started
    ''' next - must not stop the program. Normalize answers with something shippable.
    ''' </summary>
    <Theory>
    <InlineData("zz")>
    <InlineData("")>
    <InlineData("de-DE")>   ' a full culture name is not one of our codes
    Public Sub An_unknown_stored_code_normalises_to_a_shipped_one(stored As String)
        Dim code = Localization.Normalize(stored)
        Assert.Contains(code, Localization.Codes)
    End Sub

    <Fact>
    Public Sub Setting_an_unshipped_code_leaves_the_app_on_a_shipped_one()
        Dim saved = Localization.CurrentCode
        Try
            Localization.CurrentCode = "zz"
            Assert.Contains(Localization.CurrentCode, Localization.Codes)
            Assert.Equal("en", Localization.CurrentCode)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

End Class
