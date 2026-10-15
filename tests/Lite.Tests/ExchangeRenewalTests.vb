#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Security.Cryptography
Imports System.Security.Cryptography.X509Certificates
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit
#If COMPANION_TESTS Then
Imports FastMediaSorterCompanion
#End If

Public Class ExchangeRenewalTests
    Private NotInheritable Class Settings
        Implements IExchangeSettingsPort
        Private ReadOnly _values As New Dictionary(Of String, String)
        Public Function GetValue(name As String) As String Implements IExchangeSettingsPort.GetValue
            Dim value As String = Nothing
            Return If(_values.TryGetValue(name, value), value, "")
        End Function
        Public Sub SetValue(name As String, value As String) Implements IExchangeSettingsPort.SetValue
            _values(name) = value
        End Sub
        Public Sub DeleteValue(name As String) Implements IExchangeSettingsPort.DeleteValue
            _values.Remove(name)
        End Sub
    End Class

    Private Shared Function Leaf(key As RSA, expired As Boolean) As X509Certificate2
        Dim request As New CertificateRequest("CN=renewal-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
        Using cert As X509Certificate2 = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-4), DateTimeOffset.UtcNow.AddDays(If(expired, -1, 3)))
            Return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), Nothing)
        End Using
    End Function

    <Fact>
    Public Async Function Legacy_entry_learns_key_then_accepts_renewal_after_reload_and_refuses_new_key() As Task
        Using rsa As RSA = RSA.Create(2048)
            Using first As X509Certificate2 = Leaf(rsa, False), renewed As X509Certificate2 = Leaf(rsa, True)
                Using server As New ExchangeStubTlsServer(Function(c) Task.CompletedTask)
                    server.Certificate = first
                    Dim settingsPort As New Settings()
                    Dim store As New ExchangeEntryStore(settingsPort)
                    Dim entry As New ExchangeEntry(ExchangeEntryStore.NewEntryId(), "127.0.0.1:" & server.Port.ToString(), "family", server.Fingerprint,
                        ExchangeCertificateKind.Pinned, ExchangeIds.NewId(), "desktop", "", False)
                    Assert.True(store.Upsert(entry))
                    Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                        Using conn As ExchangeTlsConnection = Await store.TransportFor(entry, ExchangeTlsTransport.Instance).ConnectAsync("127.0.0.1", server.Port, entry.Pin, cts.Token)
                            Assert.True(ExchangeTrust.IsFingerprint(conn.KeyPin))
                        End Using
                        Dim learned As ExchangeEntry = New ExchangeEntryStore(settingsPort).Find(entry.Id)
                        Assert.NotEmpty(learned.KeyPin)
                        Assert.Equal(learned.KeyPin, learned.WithName("renamed").WithAutoAttach(True).WithAddress(learned.Server).KeyPin)
                        server.Certificate = renewed
                        Dim reloaded As New ExchangeEntryStore(settingsPort)
                        Using conn As ExchangeTlsConnection = Await reloaded.TransportFor(learned, ExchangeTlsTransport.Instance).ConnectAsync("127.0.0.1", server.Port, learned.Pin, cts.Token)
                            Assert.Equal(ExchangeTrustVerdict.PinMatch, conn.Verdict)
                            Assert.NotEqual(entry.Pin, conn.Fingerprint)
                            Assert.Equal(server.Fingerprint, reloaded.Find(entry.Id).Pin)
                        End Using
                        server.Certificate = ExchangeWireTestKit.NewSelfSignedCertificate()
                        Dim saved As ExchangeEntry = reloaded.Find(entry.Id)
                        Dim ex As ExchangeConnectException = Await Assert.ThrowsAsync(Of ExchangeConnectException)(Function() reloaded.TransportFor(saved, ExchangeTlsTransport.Instance).ConnectAsync("127.0.0.1", server.Port, saved.Pin, cts.Token))
                        Assert.Equal(ExchangeConnectFailure.PinMismatch, ex.Failure)
                        Assert.Equal(saved.Pin, reloaded.Find(entry.Id).Pin)
                    End Using
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Sub Account_pair_survives_reload_and_explicit_repin_clears_old_key()
        Dim port As New Settings()
        Dim store As New ExchangeAccountStore(port)
        Dim pin As String = ExchangeTrust.FingerprintOf(New Byte() {1})
        Dim key As String = ExchangeTrust.FingerprintOf(New Byte() {2})
        store.Save(New ExchangeAccount("127.0.0.1:22022", "family", pin, ExchangeCertificateKind.Pinned, ExchangeIds.NewId(), "desktop", key))
        Assert.Equal(key, New ExchangeAccountStore(port).Load().KeyPin)
        Assert.Equal(key, store.Load().WithDeviceName("renamed").KeyPin)
        store.ReplacePin(pin, ExchangeCertificateKind.Pinned)
        Assert.Empty(store.Load().KeyPin)
    End Sub

    <Fact>
    Public Sub Client_and_probe_offer_only_TLS_12_and_13()
        Dim source As String = File.ReadAllText(Path.Combine(ExchangeWireTestKit.RepoRoot(), "src", "Exchange", "ExchangeTls.vb"))
        Assert.Contains(".EnabledSslProtocols = SslProtocols.Tls12 Or SslProtocols.Tls13", source)
        Assert.DoesNotContain("SslProtocols.Tls11", source)
        Assert.DoesNotContain("SslProtocols.Ssl", source)
    End Sub
End Class
#End If
