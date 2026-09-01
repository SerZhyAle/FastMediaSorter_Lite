Option Strict On

Imports System.Globalization
Imports Xunit

''' <summary>
''' What language a given machine starts in - the part of
''' 013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md §7.3 that can be proved without a German
''' Windows to hand. Manual check 6 ("first run on a German Windows shows German") is
''' exactly MatchCulture plus DefaultCode's order of preference, so it is pinned here
''' rather than left to a machine nobody has.
'''
''' The whole class runs on BOTH legs, and the two legs ship different language sets:
''' "#If NETFRAMEWORK" cuts Localization.Codes to RU+EN in the x86 viewer, because the
''' font Windows needs for Hindi and Bengali arrived in Windows 8. So the expectation
''' is derived from Codes instead of hardcoded - which makes the seam itself the thing
''' under test: a language the build carries must be recognised, and one it does not
''' must match NOTHING so DefaultCode can fall back to English rather than render
''' boxes. Hardcoding thirteen languages here would fail the net48 leg by design and
''' leave a permanently red pre-flight (canon invariant 5).
''' </summary>
Public Class LocalizationStartupTests

    ''' <summary>Does THIS build carry that language? RU+EN on net48, all 13 on modern.</summary>
    Private Shared Function Ships(code As String) As Boolean
        Return Array.IndexOf(Localization.Codes, code) >= 0
    End Function

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
        Dim matched = Localization.MatchCulture(New CultureInfo(culture))
        If Ships(expected) Then
            Assert.Equal(expected, matched)
        Else
            ' The x86 leg: not "en" either - MatchCulture reports "no translation" and
            ' DefaultCode is the one place allowed to decide what happens next.
            Assert.Null(matched)
        End If
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
    <Theory>
    <InlineData("de-DE", "de")>
    <InlineData("ar-SA", "ar")>
    Public Sub The_display_language_decides_the_first_run(culture As String, expected As String)
        Dim savedUi = CultureInfo.CurrentUICulture
        Try
            CultureInfo.CurrentUICulture = New CultureInfo(culture)
            Dim code = Localization.DefaultCode()
            If Ships(expected) Then
                Assert.Equal(expected, code)
            Else
                ' The x86 leg does not carry this language, so the display language
                ' cannot win - but whatever wins is still one this build can draw.
                Assert.NotEqual(expected, code)
                Assert.Contains(code, Localization.Codes)
            End If
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
