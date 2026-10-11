#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Security.Cryptography
Imports System.Linq
Imports Xunit

''' <summary>
''' Ticket 187 remainder - the entries proposal's asks 2 and 7.
'''
'''   - Ask 2: a welcome that announces another address is SHOWN and the in-place edit offered, never switched silently; the edit keeps
'''     the entry's id (the binding of every attachment), the device id, the pin decision and the token (only the address changes).
'''   - Ask 7 (optional): the string an entry hands its server on with - `x` from the last announced endpoint (never a guess, never a
'''     loopback), `fp` exactly when the entry holds a pin made under item D, no login hint - and it parses back to the same server.
''' </summary>
Public Class ExchangeEntryMoveTests

    ''' <summary>A cipher that does not need Windows, so the token test stays a pure file test.</summary>
    Private NotInheritable Class XorCipher
        Implements IExchangeSecretCipher

        Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
            Return plain.Select(Function(b) CByte(b Xor &H5A)).ToArray()
        End Function

        Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
            Return blob.Select(Function(b) CByte(b Xor &H5A)).ToArray()
        End Function
    End Class

    Private Shared ReadOnly PinText As String = "SHA256:" & New String("A"c, 43)

    Private Shared Function Entry(Optional server As String = "203.0.113.5:22022", Optional pin As String = "", Optional kind As ExchangeCertificateKind = ExchangeCertificateKind.None) As ExchangeEntry
        Return New ExchangeEntry(ExchangeEntryStore.NewEntryId(), server, "alice", pin, kind, ExchangeWireTestKit.IdOf("D"c), "PC (viewer)", "Home", False)
    End Function

    ' ---------------------------------------------------------------- ask 2: what a welcome announced

    <Fact>
    Public Sub Nothing_is_announced_when_the_value_is_not_usable_or_is_the_address_held()
        ' the same address, whatever the case of the host
        Assert.Equal("", ExchangeEntryMove.Announced("203.0.113.5:22022", "203.0.113.5:22022"))
        Assert.Equal("", ExchangeEntryMove.Announced("example.com:22022", "EXAMPLE.COM:22022"))
        Assert.Equal("", ExchangeEntryMove.Announced("[2001:db8::1]:22022", "[2001:DB8::1]:22022"))
        ' not an address, empty, loopback or unspecified: nothing to say, and never a guess
        Assert.Equal("", ExchangeEntryMove.Announced("example.com:22022", ""))
        Assert.Equal("", ExchangeEntryMove.Announced("example.com:22022", "not an address"))
        Assert.Equal("", ExchangeEntryMove.Announced("example.com:22022", "127.0.0.1:22022"))
        Assert.Equal("", ExchangeEntryMove.Announced("example.com:22022", "localhost:22022"))
        Assert.Equal("", ExchangeEntryMove.Announced("example.com:22022", "0.0.0.0:22022"))
        Assert.Equal("", ExchangeEntryMove.Announced("example.com:22022", "[::]:22022"))
    End Sub

    <Fact>
    Public Sub A_welcome_that_names_another_address_is_returned_in_canonical_form()
        Assert.Equal("203.0.113.9:22023", ExchangeEntryMove.Announced("203.0.113.5:22022", "203.0.113.9:22023"))
        Assert.Equal("example.net:22022", ExchangeEntryMove.Announced("example.com:22022", "Example.NET:22022"))
        Assert.Equal("[2001:db8::2]:22022", ExchangeEntryMove.Announced("[2001:db8::1]:22022", "[2001:db8::2]:22022"))
        Assert.Equal("example.com:22023", ExchangeEntryMove.Announced("example.com:22022", "example.com:22023"))
    End Sub

    <Fact>
    Public Sub The_in_place_edit_keeps_everything_but_the_address()
        Dim seen As New List(Of String) From {"r1"}
        Dim e As New ExchangeEntry("a", "203.0.113.5:22022", "alice", PinText, ExchangeCertificateKind.Pinned, ExchangeWireTestKit.IdOf("D"c), "PC (viewer)", "Home", True, seen)
        Dim moved As ExchangeEntry = e.WithAddress("203.0.113.9:22023")
        Assert.Equal("203.0.113.9:22023", moved.Server)
        ' the id is the binding of every attached resource (rule 6): it cannot change
        Assert.Equal(e.Id, moved.Id)
        Assert.Equal(e.Login, moved.Login)
        Assert.Equal(e.Pin, moved.Pin)
        Assert.Equal(e.Certificate, moved.Certificate)
        Assert.Equal(e.DeviceId, moved.DeviceId)
        Assert.Equal(e.DeviceName, moved.DeviceName)
        Assert.Equal(e.Name, moved.Name)
        Assert.Equal(e.AutoAttach, moved.AutoAttach)
        Assert.Equal(seen, moved.Seen)
    End Sub

    <Fact>
    Public Sub A_moved_entry_keeps_its_token_and_cannot_take_the_identity_of_another()
        Dim settings As New ExchangeStoresTests.MemorySettings()
        Dim root As String = Path.Combine(Path.GetTempPath(), "fms-move-" & Guid.NewGuid().ToString("N"))
        Try
            Dim store As New ExchangeEntryStore(settings, root, New XorCipher())
            Dim a As ExchangeEntry = store.Propose("203.0.113.5:22022", "alice", "", "PC (viewer)")
            Dim b As ExchangeEntry = store.Propose("203.0.113.9:22022", "alice", "", "PC (viewer)")
            Assert.True(store.Upsert(a))
            Assert.True(store.Upsert(b))
            Assert.True(store.TokenStoreFor(a.Id).Save(a.DeviceId, ExchangeWireTestKit.TokenOf("T"c)))

            ' the edit in place: the stored address changes, the token folder is untouched and still answers
            Assert.True(store.Upsert(a.WithAddress("203.0.113.7:22022")))
            Dim after As ExchangeEntry = store.Find(a.Id)
            Assert.NotNull(after)
            Assert.Equal("203.0.113.7:22022", after.Server)
            Assert.Equal(a.DeviceId, after.DeviceId)
            Dim token As String = ""
            Assert.True(store.TokenStoreFor(a.Id).TryLoad(a.DeviceId, token))
            Assert.Equal(ExchangeWireTestKit.TokenOf("T"c), token)

            ' a second entry already holds an identity: the move onto it is refused and nothing changed
            Dim move As ExchangeEntry = a.WithAddress("203.0.113.9:22022")
            Assert.False(store.Upsert(move))
            Assert.NotNull(store.FindByIdentity("203.0.113.7:22022", "alice"))
            Assert.Equal("203.0.113.7:22022", store.Find(a.Id).Server)
        Finally
            Try : Directory.Delete(root, True) : Catch : End Try
        End Try
    End Sub

    ' ---------------------------------------------------------------- ask 7: handing a server on from an entry

    <Fact>
    Public Sub The_hand_on_string_carries_the_announced_address_and_exactly_a_pinned_fingerprint()
        Dim text As String = ExchangeEntryMove.HandOn(Entry(pin:=PinText, kind:=ExchangeCertificateKind.Pinned), "Example.NET:22023")
        Assert.NotEqual("", text)
        Dim read As ExchangeConnectionStringResult = ExchangeConnectionString.Parse(text)
        Assert.True(read.Succeeded)
        Assert.Equal("example.net", read.Value.Host)
        Assert.Equal(22023, read.Value.Port)
        Assert.Equal(PinText, read.Value.Fingerprint)
        Assert.Equal("Home", read.Value.Name)
        ' no login hint (ask 7: none unless the user adds it)
        Assert.Equal("", read.Value.Login)
    End Sub

    <Fact>
    Public Sub A_certificate_validated_by_the_platform_hands_on_no_fingerprint()
        Dim text As String = ExchangeEntryMove.HandOn(Entry(kind:=ExchangeCertificateKind.CaSigned), "203.0.113.9:22022")
        Assert.NotEqual("", text)
        Dim read As ExchangeConnectionStringResult = ExchangeConnectionString.Parse(text)
        Assert.True(read.Succeeded)
        Assert.Equal("", read.Value.Fingerprint)
    End Sub

    <Fact>
    Public Sub Nothing_is_handed_on_without_a_usable_announced_endpoint()
        Assert.Equal("", ExchangeEntryMove.HandOn(Entry(), ""))
        Assert.Equal("", ExchangeEntryMove.HandOn(Entry(), "not an address"))
        ' never the entry's own address as a guess, and never a loopback
        Assert.Equal("", ExchangeEntryMove.HandOn(Entry(), "127.0.0.1:22022"))
        Assert.Equal("", ExchangeEntryMove.HandOn(Nothing, "203.0.113.9:22022"))
    End Sub

End Class
#End If
