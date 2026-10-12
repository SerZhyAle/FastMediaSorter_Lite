Option Strict On

' Ticket 170 rules 1 and 7. The two choices of the producer, both OFF by default and both read from one place:
'   "Publish my shares to my exchange account"                               Exchange_AgentPublishShares
'   "Let other SFTP programs (WinSCP, FileZilla) reach my shares through the server"   Exchange_AgentThirdPartyPort
'
' Enrolling an account publishes nothing: a value that is absent is "off". The names start with "Exchange_Agent" so that the dual-mode
' baseline of ticket 204 (the value pattern in DualModeFixtures) would catch one leaking into a profile that never configured an
' account, and they are NEVER added to the log package's allow-list (a new setting is withheld from a report until added on purpose).
'
' A value is a plain "1" or absent; reading never creates a key. Leaving the account ("Disconnect: keep nothing") removes both, so a
' later enrollment into another account publishes nothing until the user chooses again.

Friend NotInheritable Class ExchangePublishSettings

    Friend Const PublishName As String = "Exchange_AgentPublishShares"
    Friend Const ThirdPartyName As String = "Exchange_AgentThirdPartyPort"

    ''' <summary>Every value this class owns, so a test can assert that a dormant run wrote none of them.</summary>
    Friend Shared ReadOnly AllNames As String() = {PublishName, ThirdPartyName}

    Private ReadOnly _port As IExchangeSettingsPort

    Public Sub New(port As IExchangeSettingsPort)
        _port = If(port, New ExchangeRegistrySettings())
    End Sub

    Public Shared Function CreateDefault() As ExchangePublishSettings
        Return New ExchangePublishSettings(New ExchangeRegistrySettings())
    End Function

    ''' <summary>"Publish my shares" - off unless the value is exactly "1".</summary>
    Public Function PublishShares() As Boolean
        Return String.Equals(_port.GetValue(PublishName), "1", StringComparison.Ordinal)
    End Function

    ''' <summary>"Let other SFTP programs reach my shares through the server" - off unless the value is exactly "1".</summary>
    Public Function ThirdPartyPort() As Boolean
        Return String.Equals(_port.GetValue(ThirdPartyName), "1", StringComparison.Ordinal)
    End Function

    Public Sub SetPublishShares(on_ As Boolean)
        If on_ Then _port.SetValue(PublishName, "1") Else _port.DeleteValue(PublishName)
    End Sub

    Public Sub SetThirdPartyPort(on_ As Boolean)
        If on_ Then _port.SetValue(ThirdPartyName, "1") Else _port.DeleteValue(ThirdPartyName)
    End Sub

    ''' <summary>"Keep nothing": both choices go back to off and the values are removed.</summary>
    Public Sub Reset()
        _port.DeleteValue(PublishName)
        _port.DeleteValue(ThirdPartyName)
    End Sub

End Class
