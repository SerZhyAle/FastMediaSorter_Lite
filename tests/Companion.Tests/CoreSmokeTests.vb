Option Strict On

Imports System.Net
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Lighter guards for the localized text + environment probes: they must be robust
''' (never throw, always return usable strings) since they run on the UI/hot paths.
''' Machine-dependent results are asserted for shape, not exact value.
''' </summary>
Public Class CoreSmokeTests

    ' ShareText no longer takes a "rus" flag - it reads the active UI language
    ' (013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A'), so these drive it by code.

    <Theory>
    <InlineData("ru")>
    <InlineData("en")>
    <InlineData("de")>
    <InlineData("ar")>
    <InlineData("zh")>
    Public Sub ShareText_Localized_NeverEmpty(code As String)
        Dim saved = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            Assert.False(String.IsNullOrWhiteSpace(ShareText.SecurityText()))
            Assert.False(String.IsNullOrWhiteSpace(ShareText.LanHintText()))
            Assert.False(String.IsNullOrWhiteSpace(ShareText.CombinedHintText()))
            Assert.False(String.IsNullOrWhiteSpace(ShareText.QrOverflowText()))
            Assert.False(String.IsNullOrWhiteSpace(ShareText.CgnatText()))
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub ShareText_Differs_Between_Languages()
        ' A crude guard against a table that returns the same string whatever is chosen.
        Dim saved = Localization.CurrentCode
        Try
            Localization.CurrentCode = "ru"
            Dim ru = ShareText.SecurityText()
            Localization.CurrentCode = "en"
            Dim en = ShareText.SecurityText()
            Localization.CurrentCode = "de"
            Dim de = ShareText.SecurityText()
            Assert.NotEqual(ru, en)
            Assert.NotEqual(en, de)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub AccessNote_DoesNotThrow_ForNullReach()
        Dim note As String = ShareText.AccessNote(Nothing, 2222, True)
        Assert.NotNull(note)  ' "" is fine (nothing to say); must never be Nothing or throw
    End Sub

    <Fact>
    Public Sub AccessNote_Cgnat_MentionsSomething()
        Dim reach As New WorkerReachability With {.IsCgnat = True, .LanAddress = "192.168.1.5"}
        Dim saved = Localization.CurrentCode
        Try
            Localization.CurrentCode = "ru"
            Assert.False(String.IsNullOrWhiteSpace(ShareText.AccessNote(reach, 2222, includeExternal:=True)))
            Localization.CurrentCode = "en"
            Assert.False(String.IsNullOrWhiteSpace(ShareText.AccessNote(reach, 2222, includeExternal:=True)))
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub NetworkInfo_LocalIPv4_IsEmptyOrValidIPv4()
        Dim ip As String = NetworkInfo.LocalIPv4()
        Assert.NotNull(ip)
        If ip.Length > 0 Then
            Dim parsed As IPAddress = Nothing
            Assert.True(IPAddress.TryParse(ip, parsed), $"LocalIPv4 returned non-IP '{ip}'")
            Assert.Equal(Sockets.AddressFamily.InterNetwork, parsed.AddressFamily)
        End If
    End Sub

    <Fact>
    Public Sub ServerFeatures_Probes_DoNotThrow()
        ' Gate probes read a marker file / HKCU / package identity - must be safe to call
        ' anytime (they gate the whole Share surface). Value is environment-dependent.
        Dim enabled As Boolean = ServerFeatures.IsEnabled()
        Dim canEnable As Boolean = ServerFeatures.CanEnable()
        Assert.False(String.IsNullOrEmpty(ServerFeatures.MarkerPath()))
        Assert.True(enabled = True OrElse enabled = False)
        Assert.True(canEnable = True OrElse canEnable = False)
    End Sub

End Class
