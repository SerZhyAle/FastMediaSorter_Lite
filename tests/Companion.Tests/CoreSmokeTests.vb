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

    <Theory>
    <InlineData(True)>
    <InlineData(False)>
    Public Sub ShareText_Localized_NeverEmpty(rus As Boolean)
        Assert.False(String.IsNullOrWhiteSpace(ShareText.SecurityText(rus)))
        Assert.False(String.IsNullOrWhiteSpace(ShareText.LanHintText(rus)))
        Assert.False(String.IsNullOrWhiteSpace(ShareText.CombinedHintText(rus)))
        Assert.False(String.IsNullOrWhiteSpace(ShareText.QrOverflowText(rus)))
        Assert.False(String.IsNullOrWhiteSpace(ShareText.CgnatText(rus)))
    End Sub

    <Fact>
    Public Sub ShareText_RuAndEn_Differ()
        ' A crude guard against accidentally returning the same string for both langs.
        Assert.NotEqual(ShareText.SecurityText(True), ShareText.SecurityText(False))
    End Sub

    <Fact>
    Public Sub AccessNote_DoesNotThrow_ForNullReach()
        Dim note As String = ShareText.AccessNote(True, Nothing, 2222, True)
        Assert.NotNull(note)  ' "" is fine (nothing to say); must never be Nothing or throw
    End Sub

    <Fact>
    Public Sub AccessNote_Cgnat_MentionsSomething()
        Dim reach As New WorkerReachability With {.IsCgnat = True, .LanAddress = "192.168.1.5"}
        Dim ru As String = ShareText.AccessNote(True, reach, 2222, includeExternal:=True)
        Dim en As String = ShareText.AccessNote(False, reach, 2222, includeExternal:=True)
        Assert.False(String.IsNullOrWhiteSpace(ru))
        Assert.False(String.IsNullOrWhiteSpace(en))
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
