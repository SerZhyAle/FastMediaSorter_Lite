Option Strict On
Imports FastMediaSorterCompanion
Imports Xunit

Public Class ExchangeRelayConsentTests
    <Fact>
    Public Sub Only_acceptance_is_kept_and_a_different_server_or_leaf_asks_again()
        Dim values As New Dictionary(Of String, String)()
        Dim prompts As Integer
        Dim accepted As Boolean = False
        Dim read As Func(Of String, String) = Function(key) If(values.ContainsKey(key), values(key), "")
        Dim write As Action(Of String, String) = Sub(key, value) values(key) = value
        Dim confirm As Func(Of Boolean) = Function()
                                             prompts += 1
                                             Return accepted
                                         End Function
        Assert.False(ExchangeRelayConsent.Accept("example.net:443", "leaf1", read, write, confirm))
        Assert.Empty(values)
        accepted = True
        Assert.True(ExchangeRelayConsent.Accept("example.net:443", "leaf1", read, write, confirm))
        Assert.True(ExchangeRelayConsent.Accept("EXAMPLE.NET:443", "leaf1", read, write, confirm))
        Assert.Equal(2, prompts)
        Assert.True(ExchangeRelayConsent.Accept("example.net:443", "leaf2", read, write, confirm))
        Assert.True(ExchangeRelayConsent.Accept("other.net:443", "leaf2", read, write, confirm))
        Assert.Equal(4, prompts)
    End Sub
End Class
