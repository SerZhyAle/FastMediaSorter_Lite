#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports Xunit

''' <summary>
''' Ticket 180 T-1 and section 3: where the secret of an enrolled device lives. The token is stored under DPAPI in a file, bound to the
''' device id, replaced atomically; the account entry (server, login, pin, device id) is not secret and lives in the settings; neither
''' is ever created by a read (the dormant rule of 204 section 2).
''' </summary>
Public Class ExchangeStoresTests

    ''' <summary>A cipher that does not need Windows: XOR with a fixed key. It can be told to fail.</summary>
    Private NotInheritable Class XorCipher
        Implements IExchangeSecretCipher

        Public Fail As Boolean

        Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
            If Fail Then Throw New CryptographicException("no key")
            Return plain.Select(Function(b) CByte(b Xor &H5A)).ToArray()
        End Function

        Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
            If Fail Then Throw New CryptographicException("no key")
            Return blob.Select(Function(b) CByte(b Xor &H5A)).ToArray()
        End Function
    End Class

    ''' <summary>An in-memory settings port that counts writes and reads.</summary>
    Friend NotInheritable Class MemorySettings
        Implements IExchangeSettingsPort

        Public ReadOnly Values As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Public Writes As Integer
        Public Reads As Integer

        Public Function GetValue(name As String) As String Implements IExchangeSettingsPort.GetValue
            Reads += 1
            Dim v As String = Nothing
            Return If(Values.TryGetValue(name, v), v, "")
        End Function

        Public Sub SetValue(name As String, value As String) Implements IExchangeSettingsPort.SetValue
            Writes += 1
            Values(name) = value
        End Sub

        Public Sub DeleteValue(name As String) Implements IExchangeSettingsPort.DeleteValue
            Writes += 1
            Values.Remove(name)
        End Sub
    End Class

    Private Shared Function TempDir() As String
        Return Path.Combine(Path.GetTempPath(), "fms-exchange-test-" & Guid.NewGuid().ToString("N"))
    End Function

    Private Shared ReadOnly Id As String = New String("D"c, 21) & "A"
    Private Shared ReadOnly Token As String = New String("T"c, 42) & "A"

    ' ---------------------------------------------------------------- the token file

    <Fact>
    Public Sub A_token_round_trips_and_the_file_does_not_hold_it_in_the_clear()
        Dim dir As String = TempDir()
        Try
            Dim store As New ExchangeCredentialStore(dir, New XorCipher())
            Assert.True(store.Save(Id, Token))
            Assert.True(store.Exists())
            Dim loaded As String = ""
            Assert.True(store.TryLoad(Id, loaded))
            Assert.Equal(Token, loaded)
            Dim raw As String = Encoding.UTF8.GetString(File.ReadAllBytes(store.FilePath))
            Assert.DoesNotContain(Token, raw)
            Assert.DoesNotContain(Id, raw)
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"))   ' the temporary file did not stay
        Finally
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        End Try
    End Sub

    <Fact>
    Public Sub The_real_DPAPI_round_trips_and_its_file_holds_neither_the_token_nor_the_id()
        Dim dir As String = TempDir()
        Try
            Dim store As New ExchangeCredentialStore(dir, ExchangeDpapiCipher.Instance)
            Assert.True(store.Save(Id, Token))
            Dim loaded As String = ""
            Assert.True(store.TryLoad(Id, loaded))
            Assert.Equal(Token, loaded)
            Dim bytes As Byte() = File.ReadAllBytes(store.FilePath)
            Dim latin As String = Encoding.Latin1.GetString(bytes)
            Assert.DoesNotContain(Token, latin)
            Assert.DoesNotContain(Id, latin)
        Finally
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        End Try
    End Sub

    <Fact>
    Public Sub A_token_is_bound_to_its_device_id_and_a_damaged_file_loads_nothing()
        Dim dir As String = TempDir()
        Try
            Dim store As New ExchangeCredentialStore(dir, New XorCipher())
            store.Save(Id, Token)
            Dim t As String = "x"
            Assert.False(store.TryLoad(New String("E"c, 21) & "A", t))   ' another device id
            Assert.Equal("", t)
            File.WriteAllBytes(store.FilePath, New Byte() {1, 2, 3, 4})
            Assert.False(store.TryLoad(Id, t))
            File.WriteAllBytes(store.FilePath, Array.Empty(Of Byte)())
            Assert.False(store.TryLoad(Id, t))
        Finally
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        End Try
    End Sub

    <Fact>
    Public Sub A_token_that_cannot_be_protected_is_not_stored_at_all()
        Dim dir As String = TempDir()
        Try
            Dim store As New ExchangeCredentialStore(dir, New XorCipher() With {.Fail = True})
            Assert.False(store.Save(Id, Token))
            Assert.False(store.Exists())
            Assert.False(Directory.Exists(dir) AndAlso Directory.GetFiles(dir).Length > 0)
            ' and a malformed id or token is refused before anything is written
            Dim good As New ExchangeCredentialStore(dir, New XorCipher())
            Assert.False(good.Save("short", Token))
            Assert.False(good.Save(Id, "short"))
            Assert.False(good.Exists())
        Finally
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        End Try
    End Sub

    <Fact>
    Public Sub Replacing_a_token_is_atomic_and_Delete_forgets_it()
        Dim dir As String = TempDir()
        Try
            Dim store As New ExchangeCredentialStore(dir, New XorCipher())
            store.Save(Id, Token)
            Dim other As String = New String("U"c, 42) & "A"
            Assert.True(store.Save(Id, other))
            Dim t As String = ""
            Assert.True(store.TryLoad(Id, t))
            Assert.Equal(other, t)
            store.Delete()
            Assert.False(store.Exists())
            store.Delete()   ' twice is fine
        Finally
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        End Try
    End Sub

    <Fact>
    Public Sub Asking_a_store_for_a_token_that_was_never_saved_creates_no_folder_and_no_file()
        Dim dir As String = TempDir()
        Dim store As New ExchangeCredentialStore(dir, New XorCipher())
        Dim t As String = ""
        Assert.False(store.Exists())
        Assert.False(store.TryLoad(Id, t))
        store.Delete()
        Assert.False(Directory.Exists(dir))
    End Sub

    <Fact>
    Public Sub The_default_folder_is_under_the_products_local_data_and_never_the_registry()
        Dim p As String = ExchangeCredentialStore.DefaultDirectory()
        Assert.EndsWith(Path.Combine("SZA", "FastMediaSorter", "exchange"), p)
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), p)
    End Sub

    ' ---------------------------------------------------------------- the account entry

    <Fact>
    Public Sub With_nothing_stored_there_is_no_account_and_reading_writes_nothing()
        Dim port As New MemorySettings()
        Dim store As New ExchangeAccountStore(port)
        Assert.Null(store.Load())
        Assert.False(store.IsConfigured())
        Assert.Equal(0, port.Writes)
        Assert.Empty(port.Values)
    End Sub

    <Fact>
    Public Sub The_device_id_is_created_once_on_first_use_and_then_kept()
        Dim port As New MemorySettings()
        Dim store As New ExchangeAccountStore(port)
        Dim first As String = store.EnsureDeviceId()
        Assert.True(ExchangeIds.IsValidId(first))
        Assert.Equal(first, store.EnsureDeviceId())
        Assert.Equal(1, port.Writes)
        Assert.Equal(first, port.Values(ExchangeAccountStore.DeviceIdName))
        ' an id alone is not an account
        Assert.False(store.IsConfigured())
    End Sub

    <Fact>
    Public Sub An_account_entry_round_trips_and_forgetting_it_keeps_only_the_device_id()
        Dim port As New MemorySettings()
        Dim store As New ExchangeAccountStore(port)
        Dim pin As String = ExchangeTrust.FingerprintOf(New Byte() {1, 2, 3})
        store.Save(New ExchangeAccount("host.example:22022", "alice", pin, ExchangeCertificateKind.Pinned, Id, "My PC"))
        Dim a As ExchangeAccount = store.Load()
        Assert.NotNull(a)
        Assert.Equal("host.example:22022", a.Server)
        Assert.Equal("alice", a.Login)
        Assert.Equal(pin, a.Pin)
        Assert.Equal(ExchangeCertificateKind.Pinned, a.Certificate)
        Assert.Equal(Id, a.DeviceId)
        Assert.Equal("My PC", a.DeviceName)
        Dim addr As ExchangeServerAddress = Nothing
        Assert.True(a.TryGetAddress(addr))
        store.Forget()
        Assert.Null(store.Load())
        Assert.Equal(Id, port.Values(ExchangeAccountStore.DeviceIdName))   ' it names this Windows user, not the account
        Assert.Single(port.Values)
    End Sub

    <Fact>
    Public Sub A_certificate_the_platform_validates_is_recorded_and_not_pinned()
        Dim port As New MemorySettings()
        Dim store As New ExchangeAccountStore(port)
        store.Save(New ExchangeAccount("host.example:22022", "alice", "", ExchangeCertificateKind.CaSigned, Id, "PC"))
        Dim a As ExchangeAccount = store.Load()
        Assert.Equal(ExchangeCertificateKind.CaSigned, a.Certificate)
        Assert.Equal("", a.Pin)
        Assert.Equal("ca-signed", port.Values(ExchangeAccountStore.CertificateName))
    End Sub

    <Fact>
    Public Sub A_hand_edited_pin_that_is_not_a_fingerprint_reads_as_no_pin()
        Dim port As New MemorySettings()
        Dim store As New ExchangeAccountStore(port)
        store.Save(New ExchangeAccount("h:1", "alice", "SHA256:nonsense", ExchangeCertificateKind.Pinned, Id, "PC"))
        Assert.Equal("", store.Load().Pin)
    End Sub

    <Fact>
    Public Sub Replacing_the_pin_touches_only_the_pin_and_its_kind()
        Dim port As New MemorySettings()
        Dim store As New ExchangeAccountStore(port)
        store.Save(New ExchangeAccount("h:1", "alice", ExchangeTrust.FingerprintOf(New Byte() {1}), ExchangeCertificateKind.Pinned, Id, "PC"))
        Dim newPin As String = ExchangeTrust.FingerprintOf(New Byte() {2})
        store.ReplacePin(newPin, ExchangeCertificateKind.Pinned)
        Dim a As ExchangeAccount = store.Load()
        Assert.Equal(newPin, a.Pin)
        Assert.Equal("alice", a.Login)
    End Sub

    <Fact>
    Public Sub A_hand_edited_server_that_does_not_parse_reaches_nothing()
        Dim a As New ExchangeAccount("not a server", "alice", "", ExchangeCertificateKind.None, Id, "PC")
        Dim addr As ExchangeServerAddress = Nothing
        Assert.False(a.TryGetAddress(addr))
        Assert.Null(addr)
    End Sub

    <Fact>
    Public Sub No_value_this_library_owns_is_a_secret_name_and_none_is_in_the_log_packages_allow_list()
        For Each n As String In ExchangeAccountStore.AllNames
            Assert.StartsWith("Exchange_", n)
            Assert.DoesNotContain("Token", n)
            Assert.DoesNotContain("Password", n)
        Next
        ' LogPackage.SafeSettingNames is an allow-list: a new setting is withheld from a report until someone adds it on purpose,
        ' and these must stay withheld
        Dim source As String = File.ReadAllText(Path.Combine(ExchangeWireTestKit.RepoRoot(), "src", "Support", "LogPackage.vb"))
        For Each n As String In ExchangeAccountStore.AllNames
            Assert.DoesNotContain("""" & n & """", source)
        Next
    End Sub

    ' ---------------------------------------------------------------- the device description

    <Fact>
    Public Sub A_name_and_a_platform_are_cut_to_what_the_server_accepts_and_stripped_of_control_characters()
        Assert.Equal("PC", ExchangeDeviceDescription.Clip("  PC" & ChrW(7) & vbLf, 64))
        Assert.Equal(64, ExchangeDeviceDescription.Clip(New String("n"c, 100), 64).Length)
        ' a surrogate pair is not cut in half
        Dim s As String = New String("a"c, 63) & ChrW(&HD83D) & ChrW(&HDE00)
        Dim cut As String = ExchangeDeviceDescription.Clip(s, 64)
        Assert.Equal(63, cut.Length)
        Assert.Equal("", ExchangeDeviceDescription.Clip(Nothing, 64))
        Dim platform As String = ExchangeDeviceDescription.PlatformText()
        Assert.StartsWith("windows ", platform)
        Assert.True(platform.Length <= 32)
    End Sub

    Private NotInheritable Class Facts
        Implements IExchangeRoleFacts

        Public Producer As Boolean
        Public Consumer As Boolean
        Public Receiver As Boolean
        Public Broadcaster As Boolean
        Public Clipboard As Boolean

        Public Function SharesPublished() As Boolean Implements IExchangeRoleFacts.SharesPublished
            Return Producer
        End Function

        Public Function ViewerAttaches() As Boolean Implements IExchangeRoleFacts.ViewerAttaches
            Return Consumer
        End Function

        Public Function CanReceive() As Boolean Implements IExchangeRoleFacts.CanReceive
            Return Receiver
        End Function

        Public Function LiveBroadcastEnabled() As Boolean Implements IExchangeRoleFacts.LiveBroadcastEnabled
            Return Broadcaster
        End Function

        Public Function TakesPartInClipboard() As Boolean Implements IExchangeRoleFacts.TakesPartInClipboard
            Return Clipboard
        End Function
    End Class

    <Fact>
    Public Sub Roles_are_computed_from_what_is_installed_and_a_role_not_declared_is_absent()
        Assert.Empty(ExchangeRoles.Compute(New Facts()))
        Assert.Empty(ExchangeRoles.Compute(Nothing))
        Assert.Equal({"resource-producer"}, ExchangeRoles.Compute(New Facts() With {.Producer = True}).ToArray())
        Assert.Equal({"resource-producer", "resource-consumer", "receiver", "broadcaster"},
                     ExchangeRoles.Compute(New Facts() With {.Producer = True, .Consumer = True, .Receiver = True, .Broadcaster = True}).ToArray())
    End Sub

    ''' <summary>DEVICE-EXCHANGE items BV and CE: a device that takes part in the clipboard declares the role `clipboard` at enroll
    ''' (ticket 239 CR-3); without the fact it is absent, and it comes last so the other four keep their places.</summary>
    <Fact>
    Public Sub The_clipboard_role_is_declared_when_the_device_takes_part_in_the_clipboard_and_comes_last()
        Assert.Equal("clipboard", ExchangeWire.RoleClipboard)
        Assert.Equal({"clipboard"}, ExchangeRoles.Compute(New Facts() With {.Clipboard = True}).ToArray())
        Assert.DoesNotContain("clipboard", ExchangeRoles.Compute(New Facts() With {.Producer = True, .Consumer = True}))
        Assert.Equal({"resource-producer", "resource-consumer", "receiver", "broadcaster", "clipboard"},
                     ExchangeRoles.Compute(New Facts() With {.Producer = True, .Consumer = True, .Receiver = True, .Broadcaster = True, .Clipboard = True}).ToArray())
    End Sub

End Class
#End If
