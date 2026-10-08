Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Text
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 170 rules 1, 2 and 7: the two registry choices (off by default, a plain "1" or absent) and the DPAPI-protected file of ids - one
''' resource id per share, kept; an optional outsider share id; the granted third-party port. A read creates nothing, a file of another device
''' is nothing, a damaged one is nothing, and a share id is never in the clear on disk.
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangeProducerStoreTests

    Private Const Device As String = "AAECAwQFBgcICQoLDA0ODw"
    Private Const Rid1 As String = "EBESExQVFhcYGRobHB0eHw"
    Private Const Rid2 As String = "ICEiIyQlJicoKSorLC0uLw"
    Private Const Sid As String = "q3Vb7YtK0xP2mN9sLfR4wA"

    Private Shared Function NewFolder() As String
        Return Path.Combine(Path.GetTempPath(), "fms-producer-store-" & Guid.NewGuid().ToString("N"))
    End Function

    Private Shared Sub Cleanup(folder As String)
        Try
            If Directory.Exists(folder) Then Directory.Delete(folder, True)
        Catch ex As IOException
        End Try
    End Sub

    ' --- the registry choices ---

    <Fact>
    Public Sub Both_choices_are_off_by_default_and_a_read_writes_nothing()
        Dim port As New ExchangeDeviceAgentTests.MemoryPort()
        Dim s As New ExchangePublishSettings(port)
        Assert.False(s.PublishShares())
        Assert.False(s.ThirdPartyPort())
        Assert.Equal(0, port.Writes)
    End Sub

    <Fact>
    Public Sub A_choice_is_a_plain_one_or_absent_and_reset_removes_both()
        Dim port As New ExchangeDeviceAgentTests.MemoryPort()
        Dim s As New ExchangePublishSettings(port)
        s.SetPublishShares(True)
        s.SetThirdPartyPort(True)
        Assert.Equal("1", port.Values(ExchangePublishSettings.PublishName))
        Assert.Equal("1", port.Values(ExchangePublishSettings.ThirdPartyName))
        Assert.True(s.PublishShares())
        s.SetPublishShares(False)
        Assert.False(port.Values.ContainsKey(ExchangePublishSettings.PublishName))
        s.SetPublishShares(True)
        s.Reset()
        Assert.False(s.PublishShares())
        Assert.False(s.ThirdPartyPort())
        Assert.Empty(port.Values)
    End Sub

    <Fact>
    Public Sub Anything_but_a_plain_one_reads_as_off()
        Dim port As New ExchangeDeviceAgentTests.MemoryPort()
        Dim s As New ExchangePublishSettings(port)
        For Each v As String In {"0", "true", "yes", " 1", "2", ""}
            port.Values(ExchangePublishSettings.PublishName) = v
            Assert.False(s.PublishShares(), "'" & v & "'")
        Next
    End Sub

    <Fact>
    Public Sub The_value_names_are_caught_by_the_dual_mode_baseline_and_kept_off_the_log_package()
        For Each n As String In ExchangePublishSettings.AllNames
            Assert.StartsWith("Exchange_Agent", n, StringComparison.Ordinal)   ' the baseline's value pattern (177) names Exchange_Agent*
        Next
        Dim lp As String = File.ReadAllText(Path.Combine(ExchangeTestSupport.RepoRoot(), "src", "Support", "LogPackage.vb"))
        For Each n As String In ExchangePublishSettings.AllNames
            Assert.DoesNotContain(n, lp, StringComparison.Ordinal)
        Next
    End Sub

    ' --- the ids file ---

    <Fact>
    Public Sub Reading_a_store_that_never_wrote_creates_nothing()
        Dim folder As String = NewFolder()
        Try
            Dim store As New ExchangeProducerStore(folder, New ExchangeDeviceAgentTests.XorCipher())
            Assert.False(store.Exists())
            Assert.Empty(store.Load(Device).Entries)
            Assert.False(Directory.Exists(folder))
        Finally
            Cleanup(folder)
        End Try
    End Sub

    <Fact>
    Public Sub Ids_and_the_carrier_survive_a_round_trip_and_the_file_is_not_readable_text()
        Dim folder As String = NewFolder()
        Try
            Dim store As New ExchangeProducerStore(folder, New ExchangeDeviceAgentTests.XorCipher())
            Dim state As ExchangeProducerState = ExchangeProducerState.Empty.
                WithEntry("Photos", New ExchangeProducerEntry(Rid1, Sid)).
                WithEntry("Videos", New ExchangeProducerEntry(Rid2, "")).
                WithCarrier("Videos", 40007)
            Assert.True(store.Save(Device, state))
            Dim back As ExchangeProducerState = store.Load(Device)
            Assert.Equal(Rid1, back.Get("Photos").ResourceId)
            Assert.Equal(Sid, back.Get("Photos").ShareId)
            Assert.Equal(Rid2, back.Get("Videos").ResourceId)
            Assert.Equal("", back.Get("Videos").ShareId)
            Assert.Equal("Videos", back.CarrierName)
            Assert.Equal(40007, back.CarrierPort)

            Dim bytes As Byte() = File.ReadAllBytes(store.FilePath)
            Dim raw As String = Encoding.Latin1.GetString(bytes)
            Assert.DoesNotContain(Rid1, raw, StringComparison.Ordinal)
            Assert.DoesNotContain(Sid, raw, StringComparison.Ordinal)
            Assert.DoesNotContain("Photos", raw, StringComparison.Ordinal)
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"))
        Finally
            Cleanup(folder)
        End Try
    End Sub

    <Fact>
    Public Sub A_file_of_another_device_a_damaged_one_and_a_cipher_that_fails_are_nothing()
        Dim folder As String = NewFolder()
        Try
            Dim cipher As New ExchangeDeviceAgentTests.XorCipher()
            Dim store As New ExchangeProducerStore(folder, cipher)
            Dim state As ExchangeProducerState = ExchangeProducerState.Empty.WithEntry("Photos", New ExchangeProducerEntry(Rid1, ""))
            Assert.True(store.Save(Device, state))
            Assert.Empty(store.Load("ICEiIyQlJicoKSorLC0uLw").Entries)        ' another device id

            cipher.Fail = True
            Assert.Empty(store.Load(Device).Entries)                          ' DPAPI of another user or machine
            Assert.False(store.Save(Device, state))                           ' and nothing half-written
            cipher.Fail = False

            File.WriteAllBytes(store.FilePath, New Byte() {1, 2, 3, 4})
            Assert.Empty(store.Load(Device).Entries)                          ' damaged
        Finally
            Cleanup(folder)
        End Try
    End Sub

    <Fact>
    Public Sub An_entry_whose_ids_are_not_the_id_form_is_dropped_and_a_carrier_without_an_entry_is_forgotten()
        Dim folder As String = NewFolder()
        Try
            Dim store As New ExchangeProducerStore(folder, New ExchangeDeviceAgentTests.XorCipher())
            Dim state As ExchangeProducerState = ExchangeProducerState.Empty.
                WithEntry("Photos", New ExchangeProducerEntry("not-an-id", "")).
                WithEntry("Videos", New ExchangeProducerEntry(Rid2, "bad-share-id")).
                WithCarrier("Photos", 40007)
            Assert.True(store.Save(Device, state))
            Dim back As ExchangeProducerState = store.Load(Device)
            Assert.Null(back.Get("Photos"))
            Assert.Equal(Rid2, back.Get("Videos").ResourceId)
            Assert.Equal("", back.Get("Videos").ShareId)
            Assert.Equal("", back.CarrierName)
        Finally
            Cleanup(folder)
        End Try
    End Sub

    <Fact>
    Public Sub Removing_the_carriers_entry_releases_the_port()
        Dim state As ExchangeProducerState = ExchangeProducerState.Empty.WithEntry("Photos", New ExchangeProducerEntry(Rid1, "")).WithCarrier("Photos", 40007)
        Dim without As ExchangeProducerState = state.WithoutEntry("Photos")
        Assert.Equal("", without.CarrierName)
        Assert.Equal(0, without.CarrierPort)
    End Sub

    <Fact>
    Public Sub Delete_removes_the_file_and_nothing_else()
        Dim folder As String = NewFolder()
        Try
            Dim store As New ExchangeProducerStore(folder, New ExchangeDeviceAgentTests.XorCipher())
            Assert.True(store.Save(Device, ExchangeProducerState.Empty))
            File.WriteAllText(Path.Combine(folder, "device-token.dat"), "x")
            store.Delete()
            Assert.False(store.Exists())
            Assert.True(File.Exists(Path.Combine(folder, "device-token.dat")))
        Finally
            Cleanup(folder)
        End Try
    End Sub

    <Fact>
    Public Sub The_file_lives_beside_the_token_and_never_in_the_workers_shares_json()
        Dim defaultStore As ExchangeProducerStore = ExchangeProducerStore.CreateDefault()
        Assert.Equal(ExchangeCredentialStore.DefaultDirectory(), Path.GetDirectoryName(defaultStore.FilePath))
        Dim src As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Core\ExchangeProducerStore.vb"))
        Assert.DoesNotContain("shares.json", src, StringComparison.Ordinal)
        Assert.DoesNotContain("SaveSetting", src, StringComparison.Ordinal)
        Assert.DoesNotContain("Registry", src, StringComparison.Ordinal)
    End Sub

End Class
