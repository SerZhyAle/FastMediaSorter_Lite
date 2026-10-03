Option Strict On

Imports Xunit

' Audit 057.S10-4: Ollama for Windows is a 64-bit program for Windows 10 and newer. The offer
' to download its installer is made only where it can run.
Public Class OllamaManagerTests

    <Theory>
    <InlineData(10, True, True)>
    <InlineData(11, True, True)>
    <InlineData(10, False, False)>   ' 32-bit Windows 10
    <InlineData(6, True, False)>     ' Windows 7 / 8.1, 64-bit
    <InlineData(6, False, False)>
    Public Sub IsSupportedOs_FollowsBitnessAndMajorVersion(major As Integer, is64 As Boolean, expected As Boolean)
        Assert.Equal(expected, OllamaManager.IsSupportedOs(major, is64))
    End Sub

    <Fact>
    Public Sub IsSupportedOs_ReadsTheRealMachineWithoutThrowing()
        ' The test host is a 64-bit Windows 10+ machine on the mainline leg; on the x86 leg
        ' it is the same OS. Either way the answer must come back, and be True here.
        Assert.True(OllamaManager.IsSupportedOs())
    End Sub

End Class
