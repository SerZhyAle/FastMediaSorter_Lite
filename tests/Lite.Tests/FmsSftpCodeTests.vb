#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text.Json
Imports Xunit

''' <summary>
''' Ticket 170 T-1 / 171 rule 1: the FMSSFTP1 / FMSSFTP2 code of ANYWHERE-ACCESS 4.1 against the catalog's rung 1 vectors
''' (vendored under TestData\anywhere-descriptor-vectors beside a PROVENANCE.txt whose digest is checked first, the .fd-sec way).
''' Every valid code decodes to its fields and re-encodes byte for byte; every refused code decodes to nothing; a v1 code is read
''' by the v1 rules only. Modern-only: on the net48 leg the code and this file compile to nothing.
''' </summary>
Public Class FmsSftpCodeTests

    Private Shared ReadOnly VectorDirectory As String = Path.Combine(AppContext.BaseDirectory, "TestData", "anywhere-descriptor-vectors")
    Private Shared ReadOnly Vectors As JsonElement = LoadVectors()

    Private Shared Function LoadVectors() As JsonElement
        Using document As JsonDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(VectorDirectory, "descriptor-v2.json")))
            Return document.RootElement.Clone()
        End Using
    End Function

    <Fact>
    Public Sub Vendored_vector_matches_its_recorded_digest()
        Dim provenance As String = File.ReadAllText(Path.Combine(VectorDirectory, "PROVENANCE.txt"))
        Dim actual As String
        Using sha As SHA256 = SHA256.Create()
            actual = String.Concat(sha.ComputeHash(File.ReadAllBytes(Path.Combine(VectorDirectory, "descriptor-v2.json"))).Select(Function(b) b.ToString("x2")))
        End Using
        Assert.Contains("SHA-256  " & actual & "  descriptor-v2.json", provenance)
        Assert.Equal("ANYWHERE-ACCESS", Vectors.GetProperty("contract").GetString())
        Assert.Equal(1, Vectors.GetProperty("rung").GetInt32())
    End Sub

    <Fact>
    Public Sub Every_valid_code_decodes_to_its_fields_and_encodes_back_byte_for_byte()
        Dim seen As Integer = 0
        For Each v As JsonElement In Vectors.GetProperty("valid").EnumerateArray()
            Dim name As String = v.GetProperty("name").GetString()
            Dim text As String = v.GetProperty("code").GetString()
            Dim code As FmsSftpCode = Nothing
            Assert.True(FmsSftpCode.TryParse(text, code), name & " must be read")
            Assert.Equal(v.GetProperty("hosts").EnumerateArray().Select(Function(h) h.GetString()).ToList(), code.Hosts.ToList())
            Assert.Equal(v.GetProperty("port").GetInt32(), code.Port)
            Assert.Equal(v.GetProperty("username").GetString(), code.Username)
            Dim pw As JsonElement = v.GetProperty("password")
            If pw.ValueKind = JsonValueKind.Null Then Assert.Null(code.Password) Else Assert.Equal(pw.GetString(), code.Password)
            Assert.Equal(v.GetProperty("fingerprint").GetString(), code.Fingerprint)
            Dim x As JsonElement = v.GetProperty("exchange")
            Assert.Equal(If(x.ValueKind = JsonValueKind.Null, "", x.GetString()), code.Exchange)
            Dim sid As JsonElement = v.GetProperty("shareId")
            Assert.Equal(If(sid.ValueKind = JsonValueKind.Null, "", sid.GetString()), code.ShareId)
            Assert.Equal(v.GetProperty("drive").GetBoolean(), code.Drive)

            Dim rebuilt As String = Nothing
            Assert.True(code.TryBuild(rebuilt), name & " must be written")
            Assert.Equal(text, rebuilt)
            seen += 1
        Next
        Assert.Equal(5, seen)
    End Sub

    <Fact>
    Public Sub A_code_without_x_sid_and_drv_is_always_written_with_the_v1_prefix()
        For Each v As JsonElement In Vectors.GetProperty("valid").EnumerateArray()
            Dim code As FmsSftpCode = Nothing
            Assert.True(FmsSftpCode.TryParse(v.GetProperty("code").GetString(), code))
            Dim stripped As FmsSftpCode = New FmsSftpCode(code.Hosts, code.Port, code.Username, code.Password, code.Fingerprint, "", "", False)
            Dim text As String = Nothing
            Assert.True(stripped.TryBuild(text))
            Assert.StartsWith(FmsSftpCode.PrefixV1, text, StringComparison.Ordinal)
        Next
    End Sub

    <Fact>
    Public Sub A_v1_code_is_read_by_the_v1_rules_only()
        Dim seen As Integer = 0
        For Each v As JsonElement In Vectors.GetProperty("v1IgnoresV2Keys").EnumerateArray()
            Dim code As FmsSftpCode = Nothing
            Assert.True(FmsSftpCode.TryParse(v.GetProperty("code").GetString(), code))
            Assert.Equal("", code.Exchange)
            Assert.Equal("", code.ShareId)
            Assert.False(code.Drive)
            Dim text As String = Nothing
            Assert.True(code.TryBuild(text))
            Assert.Equal(v.GetProperty("reencoded").GetString(), text)
            seen += 1
        Next
        Assert.Equal(1, seen)
    End Sub

    <Fact>
    Public Sub Every_refused_code_decodes_to_nothing()
        Dim seen As Integer = 0
        For Each v As JsonElement In Vectors.GetProperty("refused").EnumerateArray()
            Dim code As FmsSftpCode = Nothing
            Assert.False(FmsSftpCode.TryParse(v.GetProperty("code").GetString(), code), v.GetProperty("reason").GetString())
            Assert.Null(code)
            seen += 1
        Next
        Assert.Equal(9, seen)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("FMSSFTP3:h=a&p=22&u=u&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    <InlineData("FMSSFTP1:")>
    <InlineData("FMSSFTP1:h=192.168.1.23&p=2022&u=fms&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk&")>
    <InlineData("FMSSFTP1:p=2022&h=192.168.1.23&u=fms&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    <InlineData("FMSSFTP1:h=192.168.1.23&h=192.168.1.24&p=2022&u=fms&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    <InlineData("FMSSFTP1:h=192.168.1.23&p=0&u=fms&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    <InlineData("FMSSFTP1:h=192.168.1.23&p=65536&u=fms&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    <InlineData("FMSSFTP1:h=192.168.1.23&p=02022&u=fms&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    <InlineData("FMSSFTP1:h=192.168.1.23&p=2022&u=&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    <InlineData("FMSSFTP1:h=192.168.1.23&p=2022&u=fms&fp=notafingerprint")>
    <InlineData("FMSSFTP1:h=192.168.1.23&p=2022&u=fms&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk&extra=1")>
    <InlineData("FMSSFTP1:h=192.168.1.23&p=2022&u=f%GGms&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    <InlineData("FMSSFTP1:h=192.168.1.23&p=2022&u=f%C3&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    <InlineData("FMSSFTP1:h=,&p=2022&u=fms&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    <InlineData("FMSSFTP1:h=192.168.1.23&p=2022&u=fms&pw=&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    Public Sub Malformed_codes_are_refused_whole(text As String)
        Dim code As FmsSftpCode = Nothing
        Assert.False(FmsSftpCode.TryParse(text, code))
        Assert.Null(code)
    End Sub

    <Fact>
    Public Sub Nothing_is_read_from_nothing()
        Dim code As FmsSftpCode = Nothing
        Assert.False(FmsSftpCode.TryParse(Nothing, code))
    End Sub

    <Fact>
    Public Sub The_writer_refuses_what_the_reader_refuses()
        Dim fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
        Dim sid As String = "q3Vb7YtK0xP2mN9sLfR4wA"
        Dim text As String = Nothing
        ' x without sid and sid without x
        Assert.False(New FmsSftpCode({"192.168.1.23"}, 2022, "fms", "pw", fp, "relay.example.net:44022", "", False).TryBuild(text))
        Assert.False(New FmsSftpCode({"192.168.1.23"}, 2022, "fms", "pw", fp, "", sid, False).TryBuild(text))
        ' a malformed x, a bare IPv6 x, a malformed sid
        Assert.False(New FmsSftpCode({"192.168.1.23"}, 2022, "fms", "pw", fp, "relay.example.net", sid, False).TryBuild(text))
        Assert.False(New FmsSftpCode({"192.168.1.23"}, 2022, "fms", "pw", fp, "2001:db8::7:44022", sid, False).TryBuild(text))
        Assert.False(New FmsSftpCode({"192.168.1.23"}, 2022, "fms", "pw", fp, "relay.example.net:44022", "q3Vb7YtK0xP2mN9sLfR4wB", False).TryBuild(text))
        ' no host, a bad port, a bad fingerprint, an empty user
        Assert.False(New FmsSftpCode(Array.Empty(Of String)(), 2022, "fms", "pw", fp, "", "", False).TryBuild(text))
        Assert.False(New FmsSftpCode({"192.168.1.23"}, 0, "fms", "pw", fp, "", "", False).TryBuild(text))
        Assert.False(New FmsSftpCode({"192.168.1.23"}, 2022, "fms", "pw", "SHA256:short", "", "", False).TryBuild(text))
        Assert.False(New FmsSftpCode({"192.168.1.23"}, 2022, "", "pw", fp, "", "", False).TryBuild(text))
        Assert.Equal("", text)
    End Sub

    <Fact>
    Public Sub Awkward_values_survive_a_round_trip()
        Dim fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
        Dim original As New FmsSftpCode({"2001:db8::7", "nas.local"}, 22022, "ivan ivanov", "p&ss=w%rd +é/1", fp, "", "", False)
        Dim text As String = Nothing
        Assert.True(original.TryBuild(text))
        Assert.DoesNotContain(" ", text, StringComparison.Ordinal)
        Assert.DoesNotContain("é", text, StringComparison.Ordinal)
        Dim back As FmsSftpCode = Nothing
        Assert.True(FmsSftpCode.TryParse(text, back))
        Assert.Equal(original.Hosts.ToList(), back.Hosts.ToList())
        Assert.Equal("ivan ivanov", back.Username)
        Assert.Equal("p&ss=w%rd +é/1", back.Password)
        Dim again As String = Nothing
        Assert.True(back.TryBuild(again))
        Assert.Equal(text, again)
    End Sub

    <Fact>
    Public Sub A_code_never_prints_a_field()
        Dim code As FmsSftpCode = Nothing
        Assert.True(FmsSftpCode.TryParse(Vectors.GetProperty("valid")(1).GetProperty("code").GetString(), code))
        Assert.Equal("FmsSftpCode", code.ToString())
    End Sub

End Class
#End If
