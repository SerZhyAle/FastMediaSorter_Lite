#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Linq
Imports System.Text
Imports Xunit

''' <summary>
''' FMSCFG 2.4 from the CONSUMER's side: the access-path kind <c>rendezvousTunnel</c> (CONFIG_FORMAT.md
''' "Amendment 2026-10-07 (2.4)") against rule 2 (an unknown kind is ignored, never fatal; the list is
''' tried in order) and rule 7 (a tunnel path always carries a valid host and port and is written last).
'''
''' The viewer's reader does not know the kind, so it takes rule 2 whole: a tunnel path is skipped
''' WITHOUT being validated - which is what makes a producer that breaks rule 7's duty harmless to this
''' reader, where the shipped Android reader (which validates every path) would refuse the file - and is
''' never dialled, never kept as an endpoint and never counted as a usable path. These cases name the kind
''' instead of the generic "quantum" the older test uses, so a later change that teaches the reader the
''' kind has to come through here on purpose. The well-formed synthetic file of the fixture corpus
''' (<c>tunnel-path-synthetic.fmscfg</c>) is covered by FmsFixtureCorpusTests; reading and previewing open
''' no connection (FmsConfigReaderTests).
''' </summary>
Public Class FmsConfigTunnelKindTests

    Private Const Pin As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    Private Const GoodSid As String = "q83vEjRWeJq83vEjRWeJqg"
    Private Const Lan As String = "{""kind"":""lan"",""host"":""192.168.1.23"",""port"":2022}"
    Private Const Forward As String = "{""kind"":""portforward"",""host"":""203.0.113.7"",""port"":2022}"

    Private Shared Function Tunnel(Optional host As String = """198.51.100.20""", Optional port As String = "7443", Optional shareId As String = """" & GoodSid & """") As String
        Return "{""kind"":""rendezvousTunnel"",""host"":" & host & ",""port"":" & port & ",""shareId"":" & shareId & "}"
    End Function

    Private Shared Function Document(accessPaths As String) As String
        Return "{""schemaVersion"":2,""resourceName"":""Tunnel PC"",""protocol"":""sftp"",""accessPaths"":[" & accessPaths & "]," &
               """username"":""fms"",""password"":""dummy-not-a-real-secret"",""hostKeyFingerprintSha256"":""" & Pin & """," &
               """roots"":[{""virtualPath"":""/Photos"",""label"":""Photos"",""readOnly"":true}],""createdAt"":""2026-10-07T09:00:00Z""}"
    End Function

    Private Shared Function Read(json As String) As FmsConfigReadResult
        Dim asBytes As FmsConfigReadResult = FmsConfigReader.ReadBytes(Encoding.UTF8.GetBytes(json))
        Dim asText As FmsConfigReadResult = FmsConfigReader.ReadText(json)
        ' One reader for every surface (file, clipboard, QR text): the two entry points must agree.
        Assert.Equal(asBytes.Failure, asText.Failure)
        Assert.Equal(asBytes.Succeeded, asText.Succeeded)
        If asBytes.Succeeded Then
            Assert.Equal(asBytes.Plan.Endpoints.Select(Function(e) e.Key()), asText.Plan.Endpoints.Select(Function(e) e.Key()))
        End If
        Return asBytes
    End Function

    ''' <summary>Rule 2 and rule 7 together: a well-formed tunnel path, last, is skipped; the direct
    ''' paths before it are all the viewer keeps, in the producer's order.</summary>
    <Fact>
    Public Sub A_well_formed_tunnel_path_written_last_is_skipped_and_the_direct_paths_are_kept()
        Dim result As FmsConfigReadResult = Read(Document(Lan & "," & Forward & "," & Tunnel()))
        Assert.True(result.Succeeded, result.Failure.ToString() & " " & result.FieldPath)
        Assert.Equal({"lan", "portforward"}, result.Plan.Endpoints.Select(Function(e) e.Kind).ToArray())
        ' Never dialled: the exchange server's address is not a candidate for the share.
        Assert.DoesNotContain(result.Plan.Endpoints, Function(e) e.Host = "198.51.100.20")
        Assert.Equal(Pin, result.Plan.PinnedFingerprint)   ' the pin is the share's own, as for every path
    End Sub

    ''' <summary>What a producer that breaks rule 7's duty would write - no usable host or port - does
    ''' not matter to a reader that takes rule 2: the entry is skipped before it is looked at. (The
    ''' shipped Android reader validates every path and would refuse such a file; that is why the duty is
    ''' the producer's, and why this reader's leniency is not a licence to write one.)</summary>
    <Theory>
    <InlineData("""""", "7443")>          ' empty host
    <InlineData("""   """, "7443")>       ' blank host
    <InlineData("""user@relay.example""", "7443")>
    <InlineData("""relay.example/path""", "7443")>
    <InlineData("null", "7443")>          ' no host at all
    <InlineData("""198.51.100.20""", "0")>
    <InlineData("""198.51.100.20""", "65536")>
    <InlineData("""198.51.100.20""", "-1")>
    <InlineData("""198.51.100.20""", """7443""")>   ' a port that is a string
    <InlineData("""198.51.100.20""", "null")>
    Public Sub A_tunnel_path_without_a_valid_endpoint_is_skipped_unvalidated_not_refused(host As String, port As String)
        Dim result As FmsConfigReadResult = Read(Document(Lan & "," & Tunnel(host, port)))
        Assert.True(result.Succeeded, result.Failure.ToString() & " " & result.FieldPath)
        Assert.Equal({"lan"}, result.Plan.Endpoints.Select(Function(e) e.Kind).ToArray())
    End Sub

    ''' <summary><c>shareId</c> is an access-path member like any other: matched by name, and a reader
    ''' that does not know the kind ignores the whole path with it (amendment C) - whatever the value,
    ''' type included.</summary>
    <Theory>
    <InlineData("""AAAAAAAAAAAAAAAAAAAAAA""")>
    <InlineData("""""")>
    <InlineData("""not 22 characters""")>
    <InlineData("12345")>
    <InlineData("null")>
    <InlineData("{""nested"":true}")>
    <InlineData("[1,2]")>
    Public Sub The_share_id_of_a_tunnel_path_is_ignored_with_its_path(shareId As String)
        Dim result As FmsConfigReadResult = Read(Document(Lan & "," & Tunnel(shareId:=shareId)))
        Assert.True(result.Succeeded, result.Failure.ToString() & " " & result.FieldPath)
        Assert.Single(result.Plan.Endpoints)
    End Sub

    ''' <summary>A tunnel path out of order (first) displaces nothing: the direct ones keep their order.
    ''' Rule 7 asks the producer to write it last; a file that does not is read the same way here.</summary>
    <Fact>
    Public Sub A_tunnel_path_written_first_displaces_no_direct_path()
        Dim result As FmsConfigReadResult = Read(Document(Tunnel() & "," & Lan & "," & Forward))
        Assert.True(result.Succeeded)
        Assert.Equal({"lan", "portforward"}, result.Plan.Endpoints.Select(Function(e) e.Kind).ToArray())
    End Sub

    ''' <summary>The contract does not decide a file whose only path is the tunnel ("the reading assumes
    ''' a direct path accompanies the tunnel path"). This reader has no way to use the kind, so it refuses
    ''' such a file as having no usable path - the same sentence as for any unknown-only list - and creates
    ''' nothing. Pinned as the present behaviour, not as a claim about the contract.</summary>
    <Fact>
    Public Sub A_file_whose_only_path_is_a_tunnel_has_no_usable_path()
        Dim result As FmsConfigReadResult = Read(Document(Tunnel()))
        Assert.False(result.Succeeded)
        Assert.Equal(FmsConfigFailure.NoUsableAccessPath, result.Failure)
        Assert.Null(result.Plan)
    End Sub

    ''' <summary>The 16-path limit counts the tunnel like any other entry, BEFORE unknown kinds are
    ''' dropped: a producer cannot hide an oversized list behind a kind this reader does not know.</summary>
    <Fact>
    Public Sub The_sixteen_path_limit_counts_a_tunnel_path()
        Dim fifteenLan As String = String.Join(",", Enumerable.Range(1, 15).Select(Function(i) "{""kind"":""lan"",""host"":""h" & i.ToString() & ".example"",""port"":22}"))
        Assert.True(Read(Document(fifteenLan & "," & Tunnel())).Succeeded)               ' 16 entries
        Dim failed As FmsConfigReadResult = Read(Document(fifteenLan & "," & Forward & "," & Tunnel()))   ' 17 entries
        Assert.False(failed.Succeeded)
        Assert.Equal(FmsConfigFailure.BadAccessPaths, failed.Failure)
    End Sub

    ''' <summary>The kind is spelled exactly; a near miss is just another unknown kind (case-sensitive
    ''' names, as every field of the format).</summary>
    <Theory>
    <InlineData("RendezvousTunnel")>
    <InlineData("rendezvoustunnel")>
    <InlineData("rendezvous-tunnel")>
    Public Sub A_misspelled_tunnel_kind_is_an_unknown_kind_too(kind As String)
        Dim entry As String = Tunnel().Replace("rendezvousTunnel", kind)
        Dim result As FmsConfigReadResult = Read(Document(Lan & "," & entry))
        Assert.True(result.Succeeded)
        Assert.Single(result.Plan.Endpoints)
    End Sub

End Class

''' <summary>
''' FMSCFG 2.3 from the consumer's side: <c>accessNote</c> is shown verbatim, "its language is the exporter's", and an
''' exporter whose language is not English SHOULD append the same note in English (CONFIG_FORMAT.md "Amendment 2026-10-07
''' (2.3)"). The Share Manager writes the two texts as <c>own</c> + one line feed + <c>English</c>; an importer parses
''' nothing out of the note, so this reader has to take that string - control characters and all - as one text and not
''' refuse the file for it, in either a left-to-right or a right-to-left first line.
''' </summary>
Public Class FmsConfigAccessNoteTests

    Private Shared Function Document(noteJson As String) As String
        Return "{""schemaVersion"":1,""resourceName"":""Home"",""protocol"":""sftp"",""accessPaths"":[{""kind"":""lan"",""host"":""192.0.2.1"",""port"":2022}]," &
               """username"":""fms"",""password"":""pw"",""hostKeyFingerprintSha256"":""SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk""," &
               """roots"":[{""virtualPath"":""/Photos"",""label"":""Photos"",""readOnly"":false}],""createdAt"":""2026-10-07T09:00:00Z""," &
               """accessNote"":" & noteJson & "}"
    End Function

    <Theory>
    <InlineData("Funktioniert im selben WLAN.\nWorks on the same Wi-Fi.")>
    <InlineData("يعمل على الشبكة نفسها.\nWorks on the same Wi-Fi.")>
    <InlineData("Работает в той же сети Wi-Fi.\nWorks on the same Wi-Fi.")>
    <InlineData("只在同一 Wi-Fi 下可用。\nWorks on the same Wi-Fi.")>
    Public Sub A_two_language_note_with_a_line_break_is_read_verbatim_and_the_file_is_accepted(escaped As String)
        Dim json As String = Document("""" & escaped & """")
        Dim result As FmsConfigReadResult = FmsConfigReader.ReadText(json)
        Assert.True(result.Succeeded, result.Failure.ToString() & " " & result.FieldPath)
        ' "\n" in the JSON text above is the escape; the reader hands back the decoded string.
        Assert.Equal(escaped.Replace("\n", vbLf), result.Plan.AccessNote)
        Assert.Equal(2, result.Plan.AccessNote.Split(ChrW(10)).Length)
    End Sub

    ''' <summary>The note is optional, additive and never required: absent and empty both read as no note.</summary>
    <Fact>
    Public Sub An_absent_or_empty_note_reads_as_no_note()
        Dim without As String = Document("""x""").Replace(",""accessNote"":""x""", "")
        Assert.Equal("", FmsConfigReader.ReadText(without).Plan.AccessNote)
        Assert.Equal("", FmsConfigReader.ReadText(Document("""""")).Plan.AccessNote)
    End Sub
End Class
#End If
