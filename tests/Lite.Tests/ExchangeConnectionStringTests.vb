#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports Xunit

''' <summary>A fact that needs the Go module's generated connection-string vectors: skipped, loudly and by name, when the file
''' is not there. A vector this test made up would prove only that the VB codec agrees with itself.</summary>
Public NotInheritable Class ExchangeConnectionStringVectorFactAttribute
    Inherits FactAttribute

    Public Sub New()
        If Not File.Exists(ExchangeConnectionStringCorpus.Path(ExchangeConnectionStringCorpus.FileName)) Then
            Skip = "the Go module's connection-string vectors are absent (exchange-server/testdata/vectors/connection-string-v1.json); run `go run ./cmd/exchange-vectors -out testdata/vectors` in exchange-server"
        End If
    End Sub
End Class

Friend NotInheritable Class ExchangeConnectionStringCorpus

    Public Const FileName As String = "connection-string-v1.json"

    Private Sub New()
    End Sub

    Friend Shared Function Path(name As String) As String
        Dim root As String = ExchangeWireTestKit.RepoRoot()
        If root.Length = 0 Then Return ""
        Return System.IO.Path.Combine(root, "exchange-server", "testdata", "vectors", name)
    End Function

    Friend Shared Function Sha256Hex(bytes As Byte()) As String
        Using h As SHA256 = SHA256.Create()
            Return Convert.ToHexString(h.ComputeHash(bytes)).ToLowerInvariant()
        End Using
    End Function

    ''' <summary>The hash PROVENANCE.txt names for the file, or "".</summary>
    Friend Shared Function ProvenanceHash(name As String) As String
        Dim p As String = Path("PROVENANCE.txt")
        If Not File.Exists(p) Then Return ""
        For Each line As String In File.ReadAllLines(p, New UTF8Encoding(False))
            Dim f() As String = line.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
            If f.Length = 2 AndAlso f(0) = name Then Return f(1)
        Next
        Return ""
    End Function

    Friend NotInheritable Class VectorCase
        Public Name As String = ""
        Public Kind As String = ""
        Public Verdict As String = ""
        Public Reason As String = ""
        Public Looks As String = ""
        Public Canonical As String = ""
        Public Raw As Byte() = New Byte() {}
        Public HasFields As Boolean
        Public X As String = ""
        Public Host As String = ""
        Public Port As Integer
        Public Fp As String = ""
        Public N As String = ""
        Public U As String = ""
    End Class

    Friend Shared Function Load() As List(Of VectorCase)
        Dim list As New List(Of VectorCase)
        Using doc As JsonDocument = JsonDocument.Parse(File.ReadAllBytes(Path(FileName)))
            For Each item As JsonElement In doc.RootElement.GetProperty("cases").EnumerateArray()
                Dim c As New VectorCase()
                c.Name = item.GetProperty("name").GetString()
                c.Kind = item.GetProperty("kind").GetString()
                c.Verdict = item.GetProperty("verdict").GetString()
                c.Reason = Str(item, "reason")
                c.Looks = Str(item, "looks")
                c.Canonical = Str(item, "canonical")
                Dim hexText As String = Str(item, "inputHex")
                c.Raw = If(hexText.Length > 0, Convert.FromHexString(hexText), Encoding.UTF8.GetBytes(Str(item, "input")))
                Dim f As JsonElement
                If item.TryGetProperty("fields", f) Then
                    c.HasFields = True
                    c.X = f.GetProperty("x").GetString()
                    c.Host = f.GetProperty("host").GetString()
                    c.Port = f.GetProperty("port").GetInt32()
                    c.Fp = Str(f, "fp")
                    c.N = Str(f, "n")
                    c.U = Str(f, "u")
                End If
                list.Add(c)
            Next
        End Using
        Return list
    End Function

    Private Shared Function Str(e As JsonElement, name As String) As String
        Dim v As JsonElement
        If e.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then Return v.GetString()
        Return ""
    End Function
End Class

''' <summary>
''' Ticket 187 T-1. The VB codec of the exchange connection string is a second implementation, held to the vectors the Go
''' module GENERATES (exchange-server/cmd/exchange-vectors): every case is read as bytes, the verdict, the reason, the members
''' and the canonical text a writer produces are compared; the file's SHA-256 is compared with its PROVENANCE.txt first, so a
''' hand edit of a vector fails here by name and the replay below can never be made to agree with itself.
''' </summary>
Public Class ExchangeConnectionStringTests

    <ExchangeConnectionStringVectorFact>
    Public Sub The_vector_file_is_the_one_its_provenance_names()
        Dim bytes As Byte() = File.ReadAllBytes(ExchangeConnectionStringCorpus.Path(ExchangeConnectionStringCorpus.FileName))
        Dim named As String = ExchangeConnectionStringCorpus.ProvenanceHash(ExchangeConnectionStringCorpus.FileName)
        Assert.NotEqual("", named)
        Assert.Equal(named, ExchangeConnectionStringCorpus.Sha256Hex(bytes))
    End Sub

    <ExchangeConnectionStringVectorFact>
    Public Sub One_changed_byte_in_the_vector_file_no_longer_matches_its_provenance()
        Dim bytes As Byte() = File.ReadAllBytes(ExchangeConnectionStringCorpus.Path(ExchangeConnectionStringCorpus.FileName))
        bytes(bytes.Length \ 2) = CByte(bytes(bytes.Length \ 2) Xor 1)
        Assert.NotEqual(ExchangeConnectionStringCorpus.ProvenanceHash(ExchangeConnectionStringCorpus.FileName), ExchangeConnectionStringCorpus.Sha256Hex(bytes))
    End Sub

    <ExchangeConnectionStringVectorFact>
    Public Sub Every_vector_case_is_read_the_way_the_file_says()
        Dim cases As List(Of ExchangeConnectionStringCorpus.VectorCase) = ExchangeConnectionStringCorpus.Load()
        Assert.True(cases.Count >= 100, "the file is meant to be broad")
        Dim failures As New List(Of String)
        For Each c As ExchangeConnectionStringCorpus.VectorCase In cases
            Dim r As ExchangeConnectionStringResult =
                If(c.Kind = "file", ExchangeConnectionString.ParseFile(c.Raw), ExchangeConnectionString.Parse(Encoding.UTF8.GetString(c.Raw)))
            If c.Verdict = "refused" Then
                If r.Succeeded OrElse r.Reason <> c.Reason OrElse r.Looks <> c.Looks Then
                    failures.Add(c.Name & ": want refusal " & c.Reason & "/" & c.Looks & ", got " & If(r.Succeeded, "a value", r.Reason & "/" & r.Looks))
                End If
                Continue For
            End If
            If Not r.Succeeded Then
                failures.Add(c.Name & ": want a value, got " & r.Reason)
                Continue For
            End If
            Dim v As ExchangeConnectionStringValue = r.Value
            If Not c.HasFields OrElse v.Endpoint <> c.X OrElse v.Host <> c.Host OrElse v.Port <> c.Port OrElse v.Fingerprint <> c.Fp OrElse v.Name <> c.N OrElse v.Login <> c.U Then
                failures.Add(c.Name & ": members differ (" & v.Endpoint & " / " & v.Fingerprint & " / " & v.Name & " / " & v.Login & ")")
            End If
            Dim refusal As ExchangeConnectionStringResult = Nothing
            Dim text As String = ExchangeConnectionString.Format(v, refusal)
            If text Is Nothing OrElse text <> c.Canonical Then
                failures.Add(c.Name & ": a writer produces '" & text & "', the vector says '" & c.Canonical & "'")
            End If
        Next
        Assert.True(failures.Count = 0, String.Join(vbLf, failures))
    End Sub

    <ExchangeConnectionStringVectorFact>
    Public Sub The_cases_the_proposals_asked_for_are_in_the_file()
        Dim names As New HashSet(Of String)(ExchangeConnectionStringCorpus.Load().Select(Function(c) c.Name))
        For Each n As String In {"with-fp-plus-slash", "name-escapes", "ipv6-literal", "port-leading-zero", "login-upper-case", "lower-case-hex",
                                 "fp-url-safe-alphabet", "fp-padded", "fp-lower-prefix", "raw-non-ascii", "name-empty", "field-without-equals",
                                 "file-bom", "file-two-lines", "file-4097", "share-code", "newer-major", "length-512"}
            Assert.True(names.Contains(n), "the vector " & n & " is missing")
        Next
    End Sub

    ' ---- direct cases, so a machine without the Go module still holds the codec to its own rules ----

    <Fact>
    Public Sub A_writer_orders_the_members_and_escapes_with_the_form_serializer()
        Dim r As ExchangeConnectionStringResult = ExchangeConnectionString.Build("Exchange.Example.org:22022", "SHA256:AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8", "Home~NAS* ", "Alice")
        Assert.True(r.Succeeded, r.Reason)
        Dim refusal As ExchangeConnectionStringResult = Nothing
        Dim text As String = ExchangeConnectionString.Format(r.Value, refusal)
        Assert.Equal("FMSEXCH1:x=exchange.example.org%3A22022&fp=SHA256%3AAAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8&n=Home%7ENAS*+&u=alice", text)
        Assert.Equal(text, ExchangeConnectionString.Format(ExchangeConnectionString.Parse(text).Value, refusal))
    End Sub

    <Fact>
    Public Sub A_string_carries_no_secret_member()
        Dim r As ExchangeConnectionStringResult = ExchangeConnectionString.Parse("FMSEXCH1:x=192.0.2.10%3A22022&password=hunter2&token=abc&code=123456")
        Assert.True(r.Succeeded)
        Dim refusal As ExchangeConnectionStringResult = Nothing
        Assert.Equal("FMSEXCH1:x=192.0.2.10%3A22022", ExchangeConnectionString.Format(r.Value, refusal))
    End Sub

    <Fact>
    Public Sub Another_prefix_and_other_families_are_refused_by_name()
        Assert.Equal(ExchangeConnectionStringReason.NewerMajor, ExchangeConnectionString.Parse("FMSEXCH2:x=a.b%3A1").Reason)
        Dim share As ExchangeConnectionStringResult = ExchangeConnectionString.Parse("FMSSFTP2:abc")
        Assert.Equal(ExchangeConnectionStringReason.NotAString, share.Reason)
        Assert.Equal(ExchangeConnectionStringReason.LooksShareCode, share.Looks)
    End Sub

    <Fact>
    Public Sub An_ipv6_literal_is_written_in_its_short_form()
        Dim r As ExchangeConnectionStringResult = ExchangeConnectionString.Build("[2001:0DB8:0:0:0:0:0:7]:22022", "", "", "")
        Assert.True(r.Succeeded)
        Assert.Equal("[2001:db8::7]:22022", r.Value.Endpoint)
        Assert.False(ExchangeConnectionString.Build("[fe80::1%eth0]:22022", "", "", "").Succeeded)
        Assert.False(ExchangeConnectionString.Build("2001:db8::7:22022", "", "", "").Succeeded)
    End Sub
End Class
#End If
