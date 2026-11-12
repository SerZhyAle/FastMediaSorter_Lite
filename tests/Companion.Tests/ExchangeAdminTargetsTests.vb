Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket L0278 T-5, specification section 7.3 (row <c>ExchangeAdminTargetsTests</c>) and 5.4: the store of administration targets.
''' The stored JSON has exactly the members of the record (a whitelist: a new member fails the test and asks for a review); no
''' password, token, invitation or device token can be put in a record (there is no member for it); at most 32 records; one record per
''' host:port; editing the address clears the pins; a read of a never-written store creates no registry value; the value is not in
''' the log package's allow-list. No sockets, no registry: the settings seam is the in-memory <c>MemoryPort</c>.
''' </summary>
Public Class ExchangeAdminTargetsTests

    ' The members of the stored record. Section 5.4 has six TABLE ROWS; the host/port row is two members, so seven names are stored.
    Private Shared ReadOnly StoredMembers As String() = {"id", "name", "host", "port", "leafPin", "keyPin", "lastUsedUtc"}

    ' The data members of the record type: the seven stored ones plus the computed, never stored host:port.
    Private Shared ReadOnly RecordMembers As String() = {"Id", "Name", "Host", "Port", "LeafPin", "KeyPin", "LastUsedUtc", "Address"}

    Private Shared ReadOnly SecretWords As New Regex("password|passwd|token|secret|invitation|credential", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)

    Private Shared ReadOnly LeafA As String = "SHA256:" & New String("A"c, 43)
    Private Shared ReadOnly LeafB As String = "SHA256:" & New String("B"c, 43)
    Private Shared ReadOnly KeyA As String = "SHA256:" & New String("k"c, 43)

    Private NotInheritable Class StepClock
        Private _now As DateTime = New DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc)

        Public Function Tick() As DateTime
            _now = _now.AddMinutes(1)
            Return _now
        End Function
    End Class

    Private Shared Function NewPort() As ExchangeDeviceAgentTests.MemoryPort
        Return New ExchangeDeviceAgentTests.MemoryPort()
    End Function

    Private Shared Function AddOne(store As ExchangeAdminTargets, name As String, address As String) As ExchangeAdminTarget
        Dim t As ExchangeAdminTarget = Nothing
        Assert.Equal(ExchangeAdminTargetResult.Saved, store.Add(name, address, t))
        Assert.NotNull(t)
        Return t
    End Function

    ''' <summary>One stored element written by hand; single quotes stand for double quotes so the JSON stays readable.</summary>
    Private Shared Function RecordJson(id As String, host As String, portJson As String, Optional name As String = "N", Optional leaf As String = "", Optional key As String = "",
                                       Optional used As String = "", Optional extra As String = "") As String
        Dim text As String = "{'id':'" & id & "','name':'" & name & "','host':'" & host & "','port':" & portJson &
                             ",'leafPin':'" & leaf & "','keyPin':'" & key & "','lastUsedUtc':'" & used & "'" & extra & "}"
        Return text.Replace("'"c, """"c)
    End Function

    Private Shared Function NewId() As String
        Return Guid.NewGuid().ToString("D")
    End Function

    ''' <summary>The text of LogPackage.vb (the viewer's log package; the Companion tests cannot reference it, so they read its source).</summary>
    Friend Shared Function LogPackageSource() As String
        Dim path As String = System.IO.Path.Combine(ExchangeTestSupport.RepoRoot(), "src", "Support", "LogPackage.vb")
        Assert.True(File.Exists(path), "LogPackage.vb not found at " & path)
        Return File.ReadAllText(path)
    End Function

    ''' <summary>The initializer text of LogPackage.SafeSettingNames, up to its closing brace.</summary>
    Friend Shared Function SafeSettingNamesBlock(source As String) As String
        Dim start As Integer = source.IndexOf("SafeSettingNames As New HashSet", StringComparison.Ordinal)
        Assert.True(start >= 0, "LogPackage.SafeSettingNames not found")
        Dim finish As Integer = source.IndexOf("}"c, start)
        Assert.True(finish > start, "the end of LogPackage.SafeSettingNames not found")
        Return source.Substring(start, finish - start)
    End Function

    ' ---- dormant --------------------------------------------------------------------------------------------

    <Fact>
    Public Sub A_read_of_a_never_written_store_creates_nothing()
        Dim port = NewPort()
        Dim store As New ExchangeAdminTargets(port)
        Assert.Empty(store.Load())
        Assert.Empty(store.LoadByRecentUse())
        Assert.Equal(0, store.Count())
        Assert.Null(store.Find(NewId()))
        Assert.Null(store.Find(Nothing))
        ' Mutators that find nothing to change write nothing either.
        Assert.False(store.Remove(NewId()))
        Assert.False(store.Touch(NewId()))
        Assert.False(store.ForgetCertificate(NewId()))
        Assert.False(store.SetPins(NewId(), LeafA, KeyA))
        Assert.Equal(0, port.Writes)
        Assert.Empty(port.Values)
    End Sub

    ' ---- the record ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub The_stored_json_has_exactly_the_members_of_the_record()
        Dim port = NewPort()
        Dim clock As New StepClock()
        Dim store As New ExchangeAdminTargets(port, AddressOf clock.Tick)
        Dim t As ExchangeAdminTarget = AddOne(store, "Office", "office.example:8443")
        Assert.True(store.SetPins(t.Id, LeafA, KeyA))
        Assert.True(store.Touch(t.Id))

        Dim json As String = port.Values(ExchangeAdminTargets.ValueName)
        Using doc As JsonDocument = JsonDocument.Parse(json)
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind)
            Assert.Equal(1, doc.RootElement.GetArrayLength())
            Dim item As JsonElement = doc.RootElement.EnumerateArray().First()
            Dim names As List(Of String) = item.EnumerateObject().Select(Function(p) p.Name).ToList()
            ' A whitelist: a new member fails here and asks for a review (nothing secret may ever be added).
            Assert.Equal(String.Join("|", StoredMembers.OrderBy(Function(s) s, StringComparer.Ordinal)),
                         String.Join("|", names.OrderBy(Function(s) s, StringComparer.Ordinal)))
            Assert.Equal(JsonValueKind.Number, item.GetProperty("port").ValueKind)
            Assert.Equal(8443, item.GetProperty("port").GetInt32())
            Assert.Equal("office.example", item.GetProperty("host").GetString())
            Assert.Equal(LeafA, item.GetProperty("leafPin").GetString())
            Assert.Equal(KeyA, item.GetProperty("keyPin").GetString())
            Assert.Equal(t.Id, item.GetProperty("id").GetString())
        End Using
    End Sub

    <Fact>
    Public Sub The_record_has_no_member_for_a_secret()
        Dim flags As BindingFlags = BindingFlags.Public Or BindingFlags.NonPublic Or BindingFlags.Instance Or BindingFlags.DeclaredOnly
        Dim recordType As Type = GetType(ExchangeAdminTarget)
        Dim data As List(Of String) = recordType.GetFields(flags).Select(Function(f) f.Name).
            Concat(recordType.GetProperties(flags).Select(Function(p) p.Name)).ToList()
        Assert.Equal(String.Join("|", RecordMembers.OrderBy(Function(s) s, StringComparer.Ordinal)),
                     String.Join("|", data.OrderBy(Function(s) s, StringComparer.Ordinal)))
        ' No member of the record, and no method or parameter of the store, is named after a secret.
        For Each m As MemberInfo In recordType.GetMembers(flags Or BindingFlags.Static)
            Assert.False(SecretWords.IsMatch(m.Name), "a record member is named after a secret: " & m.Name)
        Next
        For Each m As MethodInfo In GetType(ExchangeAdminTargets).GetMethods(BindingFlags.Public Or BindingFlags.NonPublic Or BindingFlags.Instance Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
            Assert.False(SecretWords.IsMatch(m.Name), "a store method is named after a secret: " & m.Name)
            For Each prm As ParameterInfo In m.GetParameters()
                Assert.False(SecretWords.IsMatch(If(prm.Name, "")), "a store parameter is named after a secret: " & m.Name & "." & prm.Name)
            Next
        Next
        Assert.Equal("ExchangeAdminTarget", New ExchangeAdminTarget(NewId(), "n", "h.example", 1, LeafA, KeyA, DateTime.MinValue).ToString())
    End Sub

    <Fact>
    Public Sub Members_a_stored_element_carries_beyond_the_record_are_dropped_on_the_next_save()
        Dim port = NewPort()
        Dim id As String = NewId()
        port.Values(ExchangeAdminTargets.ValueName) = "[" & RecordJson(id, "x.example", "443", extra:=",'password':'hunter2','token':'tok','invitation':'inv','deviceToken':'dev'") & "]"
        Dim store As New ExchangeAdminTargets(port)
        Assert.Equal(1, store.Count())
        Assert.True(store.Touch(id))
        Dim json As String = port.Values(ExchangeAdminTargets.ValueName)
        Assert.DoesNotContain("hunter2", json)
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase)
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase)
        Assert.DoesNotContain("invitation", json, StringComparison.OrdinalIgnoreCase)
        Using doc As JsonDocument = JsonDocument.Parse(json)
            Dim names As List(Of String) = doc.RootElement.EnumerateArray().First().EnumerateObject().Select(Function(p) p.Name).ToList()
            Assert.Equal(StoredMembers.Length, names.Count)
        End Using
    End Sub

    ' ---- the address ----------------------------------------------------------------------------------------

    <Theory>
    <InlineData("")>
    <InlineData("   ")>
    <InlineData("host.example")>
    <InlineData("host.example:")>
    <InlineData(":443")>
    <InlineData("host.example:0")>
    <InlineData("host.example:65536")>
    <InlineData("host.example:abc")>
    <InlineData("host.example:-1")>
    <InlineData("ho st.example:443")>
    <InlineData("https://host.example:443")>
    <InlineData("user@host.example:443")>
    <InlineData("host.example:443/path")>
    <InlineData("a:b:443")>
    <InlineData("[::1]")>
    Public Sub A_bare_host_or_a_malformed_address_is_refused_and_nothing_is_written(address As String)
        Dim port = NewPort()
        Dim store As New ExchangeAdminTargets(port)
        Dim t As ExchangeAdminTarget = New ExchangeAdminTarget(NewId(), "x", "x", 1, "", "", DateTime.MinValue)
        Assert.Equal(ExchangeAdminTargetResult.BadAddress, store.Add("Name", address, t))
        Assert.Null(t)
        Assert.Equal(0, port.Writes)
        Assert.Empty(port.Values)
        ' The same refusal on an edit, with the stored list untouched.
        Dim kept As ExchangeAdminTarget = AddOne(store, "Kept", "kept.example:443")
        Dim writes As Integer = port.Writes
        Dim stored As String = port.Values(ExchangeAdminTargets.ValueName)
        Assert.Equal(ExchangeAdminTargetResult.BadAddress, store.Edit(kept.Id, "Kept", address, t))
        Assert.Null(t)
        Assert.Equal(writes, port.Writes)
        Assert.Equal(stored, port.Values(ExchangeAdminTargets.ValueName))
    End Sub

    <Fact>
    Public Sub An_address_is_stored_with_a_lower_case_host_and_an_ipv6_literal_without_brackets()
        Dim port = NewPort()
        Dim store As New ExchangeAdminTargets(port)
        Dim a As ExchangeAdminTarget = AddOne(store, "", "  Host.Example:443 ")
        Assert.Equal("host.example", a.Host)
        Assert.Equal(443, a.Port)
        Assert.Equal("host.example:443", a.Address)
        Dim v6 As ExchangeAdminTarget = AddOne(store, "Loopback", "[::1]:8443")
        Assert.Equal("::1", v6.Host)
        Assert.Equal("[::1]:8443", v6.Address)
        Dim again As ExchangeAdminTargets = New ExchangeAdminTargets(port)
        Assert.Equal(2, again.Count())
        Assert.Equal("[::1]:8443", again.Load()(1).Address)
    End Sub

    ' ---- the name -------------------------------------------------------------------------------------------

    <Fact>
    Public Sub A_name_is_one_to_sixty_four_characters_and_defaults_to_the_host()
        Dim port = NewPort()
        Dim store As New ExchangeAdminTargets(port)
        Assert.Equal("Office server", AddOne(store, "  Office server ", "a.example:1").Name)
        Assert.Equal("b.example", AddOne(store, "", "b.example:1").Name)
        Assert.Equal("c.example", AddOne(store, "   ", "c.example:1").Name)
        Assert.Equal(New String("n"c, 64), AddOne(store, New String("n"c, 64), "d.example:1").Name)
        Dim writes As Integer = port.Writes
        Dim t As ExchangeAdminTarget = Nothing
        Assert.Equal(ExchangeAdminTargetResult.BadName, store.Add(New String("n"c, 65), "e.example:1", t))
        Assert.Null(t)
        Assert.Equal(ExchangeAdminTargetResult.BadName, store.Add("tab" & vbTab & "inside", "e.example:1", t))
        Assert.Equal(ExchangeAdminTargetResult.BadName, store.Add("line" & vbLf & "break", "e.example:1", t))
        Assert.Equal(writes, port.Writes)
        Assert.Equal(4, store.Count())
    End Sub

    ' ---- the cap and the one-per-address rule -----------------------------------------------------------------

    <Fact>
    Public Sub At_most_thirty_two_targets_are_kept()
        Dim port = NewPort()
        Dim store As New ExchangeAdminTargets(port)
        Assert.Equal(32, ExchangeAdminTargets.MaxTargets)
        For i As Integer = 1 To 32
            AddOne(store, "", "h" & i.ToString(CultureInfo.InvariantCulture) & ".example:8443")
        Next
        Dim writes As Integer = port.Writes
        Dim t As ExchangeAdminTarget = Nothing
        Assert.Equal(ExchangeAdminTargetResult.Full, store.Add("", "h33.example:8443", t))
        Assert.Null(t)
        Assert.Equal(32, store.Count())
        Assert.Equal(writes, port.Writes)
        ' Removing one makes room again.
        Assert.True(store.Remove(store.Load()(0).Id))
        Assert.Equal(ExchangeAdminTargetResult.Saved, store.Add("", "h33.example:8443", t))
        Assert.Equal(32, store.Count())
    End Sub

    <Fact>
    Public Sub A_stored_value_with_more_than_thirty_two_records_reads_thirty_two()
        Dim port = NewPort()
        Dim parts As New List(Of String)()
        For i As Integer = 1 To 40
            parts.Add(RecordJson(NewId(), "h" & i.ToString(CultureInfo.InvariantCulture) & ".example", "443"))
        Next
        port.Values(ExchangeAdminTargets.ValueName) = "[" & String.Join(",", parts) & "]"
        Assert.Equal(32, New ExchangeAdminTargets(port).Count())
    End Sub

    <Fact>
    Public Sub There_is_one_record_per_host_and_port()
        Dim port = NewPort()
        Dim store As New ExchangeAdminTargets(port)
        Dim first As ExchangeAdminTarget = AddOne(store, "First", "A.Example:8443")
        Dim writes As Integer = port.Writes
        Dim held As ExchangeAdminTarget = Nothing
        Assert.Equal(ExchangeAdminTargetResult.Duplicate, store.Add("Twin", "a.example:8443", held))
        Assert.NotNull(held)
        Assert.Equal(first.Id, held.Id)
        Assert.Equal(writes, port.Writes)
        ' Another port, or another host, is another record.
        AddOne(store, "", "a.example:8444")
        AddOne(store, "", "b.example:8443")
        Assert.Equal(3, store.Count())
        ' An edit onto the address of another record is refused and names it; the edited record stays as it was.
        Dim other As ExchangeAdminTarget = store.Load()(2)
        Dim stored As String = port.Values(ExchangeAdminTargets.ValueName)
        Assert.Equal(ExchangeAdminTargetResult.Duplicate, store.Edit(other.Id, "Moved", "A.EXAMPLE:8443", held))
        Assert.Equal(first.Id, held.Id)
        Assert.Equal(stored, port.Values(ExchangeAdminTargets.ValueName))
    End Sub

    ' ---- editing the address clears the pins -------------------------------------------------------------------

    <Fact>
    Public Sub Editing_the_host_or_the_port_clears_the_pins_and_editing_the_name_does_not()
        Dim port = NewPort()
        Dim store As New ExchangeAdminTargets(port)
        Dim t As ExchangeAdminTarget = AddOne(store, "Home", "home.example:8443")
        Dim e As ExchangeAdminTarget = Nothing

        ' The name only (and the host in another case, which is the same endpoint): the pins and the id stay.
        Assert.True(store.SetPins(t.Id, LeafA, KeyA))
        Assert.Equal(ExchangeAdminTargetResult.Saved, store.Edit(t.Id, "Renamed", "HOME.example:8443", e))
        Assert.Equal("Renamed", e.Name)
        Assert.Equal(LeafA, e.LeafPin)
        Assert.Equal(KeyA, e.KeyPin)
        Assert.Equal(t.Id, e.Id)
        Assert.Equal(LeafA, store.Find(t.Id).LeafPin)

        ' The port: both pins go.
        Assert.Equal(ExchangeAdminTargetResult.Saved, store.Edit(t.Id, "Renamed", "home.example:9443", e))
        Assert.Equal(9443, e.Port)
        Assert.Equal("", e.LeafPin)
        Assert.Equal("", e.KeyPin)
        Assert.Equal("", store.Find(t.Id).LeafPin)
        Assert.Equal("", store.Find(t.Id).KeyPin)

        ' The host: both pins go.
        Assert.True(store.SetPins(t.Id, LeafB, ""))
        Assert.Equal(ExchangeAdminTargetResult.Saved, store.Edit(t.Id, "Renamed", "elsewhere.example:9443", e))
        Assert.Equal("elsewhere.example", e.Host)
        Assert.Equal("", store.Find(t.Id).LeafPin)
        Assert.Equal("", store.Find(t.Id).KeyPin)
        Assert.Equal(t.Id, store.Find(t.Id).Id)
        Assert.Equal(1, store.Count())
    End Sub

    <Fact>
    Public Sub An_edit_that_changes_nothing_writes_nothing_and_a_missing_id_or_a_bad_name_changes_nothing()
        Dim port = NewPort()
        Dim store As New ExchangeAdminTargets(port)
        Dim t As ExchangeAdminTarget = AddOne(store, "Home", "home.example:8443")
        Dim writes As Integer = port.Writes
        Dim e As ExchangeAdminTarget = Nothing
        Assert.Equal(ExchangeAdminTargetResult.Saved, store.Edit(t.Id, "Home", "home.example:8443", e))
        Assert.Equal(writes, port.Writes)
        Assert.Equal(ExchangeAdminTargetResult.NotFound, store.Edit(NewId(), "Home", "home.example:8443", e))
        Assert.Null(e)
        Assert.Equal(ExchangeAdminTargetResult.BadName, store.Edit(t.Id, New String("n"c, 65), "home.example:8443", e))
        Assert.Null(e)
        Assert.Equal(writes, port.Writes)
        Assert.Equal("Home", store.Find(t.Id).Name)
    End Sub

    ' ---- the pins --------------------------------------------------------------------------------------------

    <Fact>
    Public Sub Pins_are_taken_only_in_the_fingerprint_form_and_can_be_forgotten()
        Assert.True(ExchangeTrust.IsFingerprint(LeafA))
        Assert.True(ExchangeTrust.IsFingerprint(KeyA))
        Dim port = NewPort()
        Dim store As New ExchangeAdminTargets(port)
        Dim t As ExchangeAdminTarget = AddOne(store, "", "pin.example:443")
        Dim writes As Integer = port.Writes
        Assert.False(store.SetPins(t.Id, "", KeyA))
        Assert.False(store.SetPins(t.Id, "SHA256:short", ""))
        Assert.False(store.SetPins(t.Id, LeafA, "not-a-fingerprint"))
        Assert.False(store.SetPins(NewId(), LeafA, KeyA))
        Assert.Equal(writes, port.Writes)
        Assert.Equal("", store.Find(t.Id).LeafPin)

        Assert.True(store.SetPins(t.Id, LeafA, ""))
        Assert.Equal(LeafA, store.Find(t.Id).LeafPin)
        Assert.Equal("", store.Find(t.Id).KeyPin)
        Assert.True(store.SetPins(t.Id, LeafA, KeyA))
        Assert.Equal(KeyA, store.Find(t.Id).KeyPin)
        writes = port.Writes
        Assert.True(store.SetPins(t.Id, LeafA, KeyA))
        Assert.Equal(writes, port.Writes)

        Assert.True(store.ForgetCertificate(t.Id))
        Assert.Equal("", store.Find(t.Id).LeafPin)
        Assert.Equal("", store.Find(t.Id).KeyPin)
        Assert.Equal("pin.example", store.Find(t.Id).Host)
        writes = port.Writes
        Assert.True(store.ForgetCertificate(t.Id))
        Assert.Equal(writes, port.Writes)
        Assert.False(store.ForgetCertificate(NewId()))
    End Sub

    ' ---- ordering and removal ---------------------------------------------------------------------------------

    <Fact>
    Public Sub The_last_used_time_orders_the_picker_and_never_used_targets_keep_their_order()
        Dim port = NewPort()
        Dim clock As New StepClock()
        Dim store As New ExchangeAdminTargets(port, AddressOf clock.Tick)
        Dim a As ExchangeAdminTarget = AddOne(store, "A", "a.example:1")
        Dim b As ExchangeAdminTarget = AddOne(store, "B", "b.example:1")
        Dim c As ExchangeAdminTarget = AddOne(store, "C", "c.example:1")
        Assert.Equal(DateTime.MinValue, a.LastUsedUtc)
        Assert.True(store.Touch(a.Id))
        Assert.True(store.Touch(c.Id))
        Assert.False(store.Touch(NewId()))
        Assert.Equal("A|B|C", String.Join("|", store.Load().Select(Function(t) t.Name)))
        Assert.Equal("C|A|B", String.Join("|", store.LoadByRecentUse().Select(Function(t) t.Name)))
        Dim again As New ExchangeAdminTargets(port)
        Assert.Equal(DateTimeKind.Utc, again.Find(c.Id).LastUsedUtc.Kind)
        Assert.Equal(New DateTime(2026, 10, 30, 12, 2, 0, DateTimeKind.Utc), again.Find(c.Id).LastUsedUtc)
        Assert.Equal(DateTime.MinValue, again.Find(b.Id).LastUsedUtc)
        Using doc As JsonDocument = JsonDocument.Parse(port.Values(ExchangeAdminTargets.ValueName))
            Dim items As List(Of JsonElement) = doc.RootElement.EnumerateArray().ToList()
            Assert.Equal("", items(1).GetProperty("lastUsedUtc").GetString())
            Assert.EndsWith("Z", items(0).GetProperty("lastUsedUtc").GetString())
        End Using
    End Sub

    <Fact>
    Public Sub Removing_the_last_target_removes_the_value()
        Dim port = NewPort()
        Dim store As New ExchangeAdminTargets(port)
        Dim a As ExchangeAdminTarget = AddOne(store, "", "a.example:1")
        Dim b As ExchangeAdminTarget = AddOne(store, "", "b.example:1")
        Assert.True(store.Remove(a.Id))
        Assert.True(port.Values.ContainsKey(ExchangeAdminTargets.ValueName))
        Assert.Equal(1, store.Count())
        Assert.Null(store.Find(a.Id))
        Assert.NotNull(store.Find(b.Id))
        Assert.True(store.Remove(b.Id))
        Assert.False(port.Values.ContainsKey(ExchangeAdminTargets.ValueName))
        Dim writes As Integer = port.Writes
        Assert.False(store.Remove(b.Id))
        Assert.Equal(writes, port.Writes)
    End Sub

    ' ---- damaged text -----------------------------------------------------------------------------------------

    <Theory>
    <InlineData("{ not json")>
    <InlineData("{""a"":1}")>
    <InlineData("""text""")>
    <InlineData("12")>
    <InlineData("[1, 2, 3]")>
    Public Sub A_value_that_does_not_parse_reads_as_empty_and_is_not_overwritten_by_a_read(damaged As String)
        Dim port = NewPort()
        port.Values(ExchangeAdminTargets.ValueName) = damaged
        Dim store As New ExchangeAdminTargets(port)
        Assert.Empty(store.Load())
        Assert.Equal(0, store.Count())
        Assert.Null(store.Find(NewId()))
        Assert.False(store.Touch(NewId()))
        Assert.False(store.Remove(NewId()))
        Assert.Equal(0, port.Writes)
        Assert.Equal(damaged, port.Values(ExchangeAdminTargets.ValueName))
        ' The next real save replaces the whole value.
        AddOne(store, "", "fresh.example:443")
        Assert.Equal(1, port.Writes)
        Assert.Equal(1, store.Count())
        Using doc As JsonDocument = JsonDocument.Parse(port.Values(ExchangeAdminTargets.ValueName))
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind)
        End Using
    End Sub

    <Fact>
    Public Sub A_damaged_element_is_skipped_and_the_others_are_read()
        Dim port = NewPort()
        Dim good As String = NewId()
        Dim noPins As String = NewId()
        Dim controlName As String = NewId()
        Dim longName As String = NewId()
        Dim upper As String = NewId()
        Dim parts As New List(Of String) From {
            RecordJson(good, "good.example", "443", "Good", LeafA, KeyA, "2026-10-30T12:00:00.0000000Z"),
            "7",
            RecordJson("not-a-guid", "x.example", "443"),
            RecordJson(NewId(), "string-port.example", """443"""),
            RecordJson(NewId(), "zero-port.example", "0"),
            RecordJson(NewId(), "", "443"),
            RecordJson(NewId(), "good.example", "443", "Same endpoint as the first"),
            RecordJson(good, "same-id.example", "443"),
            RecordJson(noPins, "nopins.example", "443", "", "garbage", "SHA256:short"),
            RecordJson(controlName, "control.example", "443", "tab\tinside"),
            RecordJson(longName, "long.example", "443", New String("L"c, 70)),
            RecordJson(upper, "UPPER.Example", "443", "Upper")}
        port.Values(ExchangeAdminTargets.ValueName) = "[" & String.Join(",", parts) & "]"
        Dim store As New ExchangeAdminTargets(port)
        Dim list As List(Of ExchangeAdminTarget) = store.Load()
        Assert.Equal(String.Join("|", {good, noPins, controlName, longName, upper}), String.Join("|", list.Select(Function(t) t.Id)))
        Assert.Equal(LeafA, list(0).LeafPin)
        Assert.Equal(KeyA, list(0).KeyPin)
        Assert.Equal(New DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc), list(0).LastUsedUtc)
        Assert.Equal("nopins.example", list(1).Name)
        Assert.Equal("", list(1).LeafPin)
        Assert.Equal("", list(1).KeyPin)
        Assert.Equal("control.example", list(2).Name)
        Assert.Equal(New String("L"c, 64), list(3).Name)
        Assert.Equal("upper.example", list(4).Host)
        Assert.Equal(0, port.Writes)
    End Sub

    <Fact>
    Public Sub A_second_store_over_the_same_value_reads_what_the_first_wrote()
        Dim port = NewPort()
        Dim clock As New StepClock()
        Dim first As New ExchangeAdminTargets(port, AddressOf clock.Tick)
        Dim a As ExchangeAdminTarget = AddOne(first, "Alpha", "alpha.example:8443")
        AddOne(first, "Beta", "[2001:db8::1]:9443")
        Assert.True(first.SetPins(a.Id, LeafA, KeyA))
        Assert.True(first.Touch(a.Id))
        Dim second As New ExchangeAdminTargets(port)
        Dim x As List(Of ExchangeAdminTarget) = first.Load()
        Dim y As List(Of ExchangeAdminTarget) = second.Load()
        Assert.Equal(2, y.Count)
        For i As Integer = 0 To 1
            Assert.True(x(i).SameAs(y(i)), "record " & i.ToString(CultureInfo.InvariantCulture) & " differs after a round trip")
        Next
        Assert.Equal("[2001:db8::1]:9443", y(1).Address)
    End Sub

    ' ---- the value name ---------------------------------------------------------------------------------------

    <Fact>
    Public Sub The_value_name_is_fixed_and_names_no_server_login_or_password()
        Assert.Equal("RemoteAdmin_Targets", ExchangeAdminTargets.ValueName)
        Assert.Equal(New String() {"RemoteAdmin_Targets"}, ExchangeAdminTargets.AllNames)
        Dim words As New Regex("server|login|password|passwd|host|address|secret|token|invitation", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
        For Each name As String In ExchangeAdminTargets.AllNames
            Assert.False(words.IsMatch(name), name & " names something a value name must not")
            Assert.StartsWith("RemoteAdmin", name, StringComparison.Ordinal)
        Next
    End Sub

    <Fact>
    Public Sub The_value_is_not_in_the_log_package_allow_list_and_the_log_package_never_mentions_it()
        Dim source As String = LogPackageSource()
        Dim block As String = SafeSettingNamesBlock(source)
        Assert.True(block.Length > 200, "the SafeSettingNames block looks empty; the guard would be vacuous")
        For Each name As String In ExchangeAdminTargets.AllNames
            Assert.DoesNotContain(name, block, StringComparison.OrdinalIgnoreCase)
            Assert.DoesNotContain(name, source, StringComparison.OrdinalIgnoreCase)
        Next
        Assert.DoesNotContain("RemoteAdmin_", source, StringComparison.OrdinalIgnoreCase)
    End Sub

End Class
