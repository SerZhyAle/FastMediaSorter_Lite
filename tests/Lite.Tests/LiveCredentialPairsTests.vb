#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Net
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports Xunit

''' <summary>
''' Ticket 263 T-3 (CR-14), the producer side of DEVICE-EXCHANGE items BF and DV: the listener verifier (PBKDF2-HMAC-SHA256, 600000
''' rounds, 32 bytes, 16-byte salt, base64url without padding - the exchange server's own check, written out here independently),
''' the pair set that yields the wire `verifiers` list and the producer's Digest lookup, and revocation of ONE pair.
''' Nothing here talks to the Go server; the wire shape is held by ExchangeWireTests and the agent by the Companion suite.
''' </summary>
Public Class ListenerVerifierTests

    ''' <summary>RFC 7914 section 11: PBKDF2-HMAC-SHA-256, P="passwd", S="salt", c=1; the first 32 of its 64 output bytes.</summary>
    <Fact>
    Public Sub Derive_is_PBKDF2_HMAC_SHA256_and_matches_the_RFC_7914_vector()
        Dim key As Byte() = ListenerVerifier.Derive("passwd", Encoding.ASCII.GetBytes("salt"), 1)
        Assert.Equal("55ac046e56e3089fec1691c22544b605f94185216dde0465e68b9d57c20dacbc", Convert.ToHexString(key).ToLowerInvariant())
    End Sub

    <Fact>
    Public Sub The_contract_constants_are_the_servers_600000_rounds_32_byte_hash_and_16_byte_salt()
        Assert.Equal(600000, ListenerVerifier.Iterations)
        Assert.Equal(32, ListenerVerifier.HashLength)
        Assert.Equal(16, ListenerVerifier.SaltLength)
    End Sub

    <Fact>
    Public Sub A_verifier_at_the_contracts_rounds_equals_an_independent_derivation()
        Dim v As ListenerVerifier = ListenerVerifier.Create("listener", "p4ss-wörd-✓")
        Dim salt As Byte() = ListenerVerifier.FromBase64Url(v.Salt)
        Dim expected As Byte() = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes("p4ss-wörd-✓"), salt, 600000, HashAlgorithmName.SHA256, 32)
        Assert.Equal(Convert.ToBase64String(expected), Convert.ToBase64String(ListenerVerifier.FromBase64Url(v.Hash)))
        Assert.True(v.Matches("p4ss-wörd-✓"))
        Assert.False(v.Matches("p4ss-word-✓"))
    End Sub

    <Fact>
    Public Sub Salt_and_hash_are_base64url_without_padding_of_the_contracts_sizes()
        Dim v As ListenerVerifier = ListenerVerifier.Create("own", "secret", 2)
        For Each text As String In New String() {v.Salt, v.Hash}
            Assert.DoesNotContain("=", text)
            Assert.DoesNotContain("+", text)
            Assert.DoesNotContain("/", text)
        Next
        Assert.Equal(16, ListenerVerifier.FromBase64Url(v.Salt).Length)
        Assert.Equal(32, ListenerVerifier.FromBase64Url(v.Hash).Length)
        Assert.Equal(22, v.Salt.Length)     ' 16 bytes -> 22 characters
        Assert.Equal(43, v.Hash.Length)     ' 32 bytes -> 43 characters
    End Sub

    <Fact>
    Public Sub Every_verifier_gets_its_own_random_salt()
        Dim a As ListenerVerifier = ListenerVerifier.Create("listener", "same", 2)
        Dim b As ListenerVerifier = ListenerVerifier.Create("listener", "same", 2)
        Assert.NotEqual(a.Salt, b.Salt)
        Assert.NotEqual(a.Hash, b.Hash)
        Assert.True(a.Matches("same", 2))
        Assert.True(b.Matches("same", 2))
    End Sub

    <Fact>
    Public Sub The_wire_object_holds_user_salt_and_hash_in_that_order_and_never_the_password()
        Dim v As ListenerVerifier = ListenerVerifier.Create("listener", "very-secret-word", 2)
        Dim json As String = v.ToJson()
        Assert.DoesNotContain("very-secret-word", json)
        Using doc As JsonDocument = JsonDocument.Parse(json)
            Dim names As New List(Of String)()
            For Each p As JsonProperty In doc.RootElement.EnumerateObject()
                names.Add(p.Name)
            Next
            Assert.Equal(New String() {"user", "salt", "hash"}, names.ToArray())
            Assert.Equal("listener", doc.RootElement.GetProperty("user").GetString())
            Assert.Equal(v.Salt, doc.RootElement.GetProperty("salt").GetString())
            Assert.Equal(v.Hash, doc.RootElement.GetProperty("hash").GetString())
        End Using
        Assert.DoesNotContain("very-secret-word", v.ToString())
        Assert.DoesNotContain(v.Hash, v.ToString())
    End Sub

    <Fact>
    Public Sub The_password_is_hashed_as_its_UTF8_bytes()
        Dim v As ListenerVerifier = ListenerVerifier.Create("listener", "пароль-ё", 3)
        Dim expected As Byte() = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes("пароль-ё"), ListenerVerifier.FromBase64Url(v.Salt), 3, HashAlgorithmName.SHA256, 32)
        Assert.Equal(Convert.ToHexString(expected), Convert.ToHexString(ListenerVerifier.FromBase64Url(v.Hash)))
        Assert.True(v.Matches("пароль-ё", 3))
        Assert.False(v.Matches("пароль-е", 3))
    End Sub
End Class

Public Class LiveCredentialPairsTests

    Private Const LinkPassword As String = "link-secret-0042"

    ''' <summary>The wire entries as (user, salt, hash) triples, parsed from the set's records.</summary>
    Private Shared Function Parsed(pairs As LiveCredentialPairs) As List(Of Tuple(Of String, String, String))
        Dim list As New List(Of Tuple(Of String, String, String))()
        For Each record As String In pairs.VerifierRecords()
            Using doc As JsonDocument = JsonDocument.Parse(record)
                list.Add(Tuple.Create(doc.RootElement.GetProperty("user").GetString(), doc.RootElement.GetProperty("salt").GetString(), doc.RootElement.GetProperty("hash").GetString()))
            End Using
        Next
        Return list
    End Function

    ''' <summary>The server's check, independently: does this wire entry accept this password at the given rounds?</summary>
    Private Shared Function Accepts(entry As Tuple(Of String, String, String), password As String, rounds As Integer) As Boolean
        Dim expected As Byte() = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), ListenerVerifier.FromBase64Url(entry.Item2), rounds, HashAlgorithmName.SHA256, 32)
        Return CryptographicOperations.FixedTimeEquals(expected, ListenerVerifier.FromBase64Url(entry.Item3))
    End Function

    ' ------------------------------------------------------------------------------------------------ building the set

    <Fact>
    Public Sub No_password_means_no_pairs_and_an_empty_list()
        For Each none As LiveCredentialPairs In New LiveCredentialPairs() {LiveCredentialPairs.None(), LiveCredentialPairs.ForLink(""), LiveCredentialPairs.ForLink(Nothing)}
            Assert.False(none.IsProtected)
            Assert.Equal(0, none.Count)
            Assert.Empty(none.Users())
            Assert.Empty(none.VerifierRecords())
            Assert.Null(none.PasswordFor(LiveCredentialPairs.LinkUser))
            Assert.Null(none.PasswordFor(LiveCredentialPairs.OwnUser))
        Next
    End Sub

    <Fact>
    Public Sub A_link_password_gives_the_listener_pair_and_a_random_own_pair()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, iterations:=2)
        Assert.True(pairs.IsProtected)
        Assert.Equal(New String() {"listener", "own"}, pairs.Users().ToArray())
        Assert.Equal(LinkPassword, pairs.PasswordFor("listener"))
        Dim own As String = pairs.PasswordFor("own")
        Assert.True(own.Length >= 24, "the own password is long and random")
        Assert.NotEqual(LinkPassword, own)
        Assert.DoesNotContain(":", own)                       ' a Basic password may carry one, but the user-id may not; keep it plain
        For Each ch As Char In own
            Assert.False(Char.IsControl(ch))
        Next
        Dim other As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, iterations:=2)
        Assert.NotEqual(own, other.PasswordFor("own"))        ' fresh per broadcast
    End Sub

    <Fact>
    Public Sub A_test_can_pin_the_own_password()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, "pinned-own-password-0123456789", 2)
        Assert.Equal("pinned-own-password-0123456789", pairs.PasswordFor("own"))
    End Sub

    <Fact>
    Public Sub The_lookup_is_exact_and_case_sensitive_and_unknown_users_have_no_password()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, iterations:=2)
        Assert.Null(pairs.PasswordFor("Listener"))
        Assert.Null(pairs.PasswordFor("OWN"))
        Assert.Null(pairs.PasswordFor("nobody"))
        Assert.Null(pairs.PasswordFor(""))
        Assert.Null(pairs.PasswordFor(Nothing))
    End Sub

    <Fact>
    Public Sub Add_refuses_what_the_server_would_refuse()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, iterations:=2)
        Assert.False(pairs.Add("", "pw"))
        Assert.False(pairs.Add("a:b", "pw"))                  ' a Basic user-id cannot hold a colon
        Assert.False(pairs.Add("tab" & vbTab, "pw"))
        Assert.False(pairs.Add(New String("u"c, 65), "pw"))
        Assert.False(pairs.Add("listener", "other"))          ' already there
        Assert.False(pairs.Add("guest", ""))
        Assert.Equal(2, pairs.Count)
        Assert.True(pairs.Add(New String("u"c, 64), "pw"))
        Assert.True(pairs.Add("guest", "pw"))
        Assert.Equal(4, pairs.Count)
    End Sub

    <Fact>
    Public Sub The_set_holds_at_most_64_pairs_like_the_server()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.None()
        For i As Integer = 1 To 64
            Assert.True(pairs.Add("u" & i.ToString(Globalization.CultureInfo.InvariantCulture), "pw"))
        Next
        Assert.False(pairs.Add("one-too-many", "pw"))
        Assert.Equal(64, pairs.Count)
    End Sub

    ' ------------------------------------------------------------------------------------------------ the wire list

    <Fact>
    Public Sub The_records_list_one_verifier_per_pair_in_order_and_each_accepts_only_its_own_password()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, "own-password-aaaaaaaaaaaaaaaa", 5)
        Dim entries = Parsed(pairs)
        Assert.Equal(2, entries.Count)
        Assert.Equal("listener", entries(0).Item1)
        Assert.Equal("own", entries(1).Item1)
        Assert.True(Accepts(entries(0), LinkPassword, 5))
        Assert.True(Accepts(entries(1), "own-password-aaaaaaaaaaaaaaaa", 5))
        Assert.False(Accepts(entries(0), "own-password-aaaaaaaaaaaaaaaa", 5))
        Assert.False(Accepts(entries(1), LinkPassword, 5))
        For Each record As String In pairs.VerifierRecords()
            Assert.DoesNotContain(LinkPassword, record)
            Assert.DoesNotContain("own-password-aaaaaaaaaaaaaaaa", record)
        Next
    End Sub

    <Fact>
    Public Sub The_records_at_the_default_rounds_are_what_the_server_recomputes()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword)       ' the contract's 600000 rounds
        Dim entries = Parsed(pairs)
        Assert.Equal(2, entries.Count)
        Assert.True(Accepts(entries(0), LinkPassword, ListenerVerifier.Iterations))
        Assert.True(Accepts(entries(1), pairs.PasswordFor("own"), ListenerVerifier.Iterations))
    End Sub

    <Fact>
    Public Sub The_hashes_are_made_once_and_a_later_call_returns_the_same_entries()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, iterations:=2)
        Dim first As IReadOnlyList(Of String) = pairs.VerifierRecords()
        Dim second As IReadOnlyList(Of String) = pairs.VerifierRecords()
        Assert.Equal(first.ToArray(), second.ToArray())      ' not re-salted on a reconnect or an update
    End Sub

    <Fact>
    Public Sub A_pair_added_later_is_hashed_on_the_next_call_and_the_earlier_ones_are_untouched()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, iterations:=2)
        Dim before As IReadOnlyList(Of String) = pairs.VerifierRecords()
        Assert.True(pairs.Add("guest", "guest-secret"))
        Dim after As IReadOnlyList(Of String) = pairs.VerifierRecords()
        Assert.Equal(3, after.Count)
        Assert.Equal(before(0), after(0))
        Assert.Equal(before(1), after(1))
        Assert.True(Accepts(Parsed(pairs)(2), "guest-secret", 2))
    End Sub

    ' ------------------------------------------------------------------------------------------------ revoking ONE pair

    <Fact>
    Public Sub Revoking_one_pair_drops_it_from_the_lookup_and_the_wire_list_and_leaves_the_other_as_it_was()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, iterations:=2)
        Dim before As IReadOnlyList(Of String) = pairs.VerifierRecords()
        Dim ownPassword As String = pairs.PasswordFor("own")

        Assert.True(pairs.Revoke("listener"))
        Assert.Null(pairs.PasswordFor("listener"))
        Assert.Equal(ownPassword, pairs.PasswordFor("own"))
        Assert.Equal(New String() {"own"}, pairs.Users().ToArray())
        Dim after As IReadOnlyList(Of String) = pairs.VerifierRecords()
        Assert.Single(after)
        Assert.Equal(before(1), after(0))                    ' the survivor is the same entry, not re-hashed
        Assert.True(pairs.IsProtected)                       ' a protected broadcast never turns open by losing a pair
    End Sub

    <Fact>
    Public Sub Revoking_either_one_works_alone()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, "own-password-bbbbbbbbbbbbbbbb", 2)
        Assert.True(pairs.Revoke("own"))
        Assert.Equal(LinkPassword, pairs.PasswordFor("listener"))
        Assert.Null(pairs.PasswordFor("own"))
        Assert.Equal("listener", Parsed(pairs).Single().Item1)
    End Sub

    <Fact>
    Public Sub An_unknown_user_and_the_last_pair_cannot_be_revoked()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, iterations:=2)
        Assert.False(pairs.Revoke("nobody"))
        Assert.False(pairs.Revoke(""))
        Assert.False(pairs.Revoke(Nothing))
        Assert.Equal(2, pairs.Count)
        Assert.True(pairs.Revoke("listener"))
        Assert.False(pairs.Revoke("listener"))               ' already gone
        ' the last pair: an empty `verifiers` means "the relay checks nobody" (item DV), so a revoke must never empty a protected list
        Assert.False(pairs.Revoke("own"))
        Assert.Equal(1, pairs.Count)
        Assert.True(pairs.IsProtected)
        Assert.Single(pairs.VerifierRecords())
    End Sub

    <Fact>
    Public Sub Nothing_leaks_a_password_through_ToString()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, "own-password-cccccccccccccccc", 2)
        Dim text As String = pairs.ToString()
        Assert.DoesNotContain(LinkPassword, text)
        Assert.DoesNotContain("own-password-cccccccccccccccc", text)
    End Sub

    <Fact>
    Public Sub Concurrent_hashing_revoking_and_reading_stay_consistent()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, iterations:=2)
        For i As Integer = 1 To 10
            Assert.True(pairs.Add("guest" & i.ToString(Globalization.CultureInfo.InvariantCulture), "guest-secret-" & i.ToString(Globalization.CultureInfo.InvariantCulture)))
        Next
        Dim failures As New System.Collections.Concurrent.ConcurrentQueue(Of String)()
        Dim tasks As New List(Of Threading.Tasks.Task)()
        For worker As Integer = 1 To 4
            tasks.Add(Threading.Tasks.Task.Run(Sub()
                                                   Try
                                                       For round As Integer = 1 To 20
                                                           Dim records As IReadOnlyList(Of String) = pairs.VerifierRecords()
                                                           If records.Count < 1 OrElse records.Count > 12 Then failures.Enqueue("count " & records.Count.ToString(Globalization.CultureInfo.InvariantCulture))
                                                       Next
                                                   Catch ex As Exception
                                                       failures.Enqueue(ex.GetType().Name)
                                                   End Try
                                               End Sub))
        Next
        tasks.Add(Threading.Tasks.Task.Run(Sub()
                                               For i As Integer = 1 To 10
                                                   pairs.Revoke("guest" & i.ToString(Globalization.CultureInfo.InvariantCulture))
                                               Next
                                           End Sub))
        Threading.Tasks.Task.WaitAll(tasks.ToArray())
        Assert.Empty(failures)
        Assert.Equal(New String() {"listener", "own"}, pairs.Users().ToArray())
    End Sub
End Class

Public Class LiveCredentialDigestTests

    Private Const Realm As String = "fms-broadcast"

    Private Shared Function Sha256Hex(value As String) As String
        Return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()
    End Function

    ''' <summary>The Authorization header a client sends for GET `path` as `user` with `password` (SHA-256, qop auth, cnonce "client").</summary>
    Private Shared Function Answer(user As String, nonce As String, password As String, nc As String, path As String) As String
        Dim response As String = Sha256Hex(Sha256Hex(user & ":" & Realm & ":" & password) & ":" & nonce & ":" & nc & ":client:auth:" & Sha256Hex("GET:" & path))
        Return "Digest username=""" & user & """, realm=""" & Realm & """, nonce=""" & nonce & """, uri=""" & path &
               """, algorithm=SHA-256, qop=auth, nc=" & nc & ", cnonce=""client"", response=""" & response & """"
    End Function

    Private NotInheritable Class Rig
        Public Now As Long
        Public ReadOnly Auth As LiveProducerDigest
        Public ReadOnly Nonce As String
        Private _nc As Integer

        Public Sub New()
            Auth = New LiveProducerDigest(Function() Now)
            Dim challenge As String = Encoding.ASCII.GetString(Auth.Challenge())
            Nonce = System.Text.RegularExpressions.Regex.Match(challenge, "nonce=""([^""]+)""").Groups(1).Value
        End Sub

        ''' <summary>One authorised GET /live.ts as `user` with `password`; the clock moves on first so the 1-check-per-second limit never gets in the way.</summary>
        Public Function Try_(passwordFor As Func(Of String, String), user As String, password As String) As Boolean
            Now += 1500
            _nc += 1
            Dim request As New LiveProducerWire.LiveWireRequest With {.Method = "GET", .RawPath = "/live.ts", .Path = "/live.ts"}
            request.Headers("Authorization") = Answer(user, Nonce, password, _nc.ToString("x8", Globalization.CultureInfo.InvariantCulture), "/live.ts")
            Return Auth.Verify(request, passwordFor, "client")
        End Function
    End Class

    <Fact>
    Public Sub Any_pair_of_the_set_opens_the_digest_for_its_own_user_only()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink("link-secret", "own-secret-dddddddddddddddd", 2)
        Dim lookup As Func(Of String, String) = AddressOf pairs.PasswordFor
        Dim rig As New Rig()
        Assert.True(rig.Try_(lookup, "listener", "link-secret"))
        Assert.True(rig.Try_(lookup, "own", "own-secret-dddddddddddddddd"))
        ' user-bound: one user's password does not open for the other, nor for a user outside the set
        Assert.False(rig.Try_(lookup, "own", "link-secret"))
        Assert.False(rig.Try_(lookup, "listener", "own-secret-dddddddddddddddd"))
        Assert.False(rig.Try_(lookup, "guest", "link-secret"))
        Assert.False(rig.Try_(lookup, "guest", ""))
        Assert.False(rig.Try_(lookup, "listener", "wrong"))
    End Sub

    <Fact>
    Public Sub A_revoked_pair_stops_opening_at_once_and_the_other_keeps_opening()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink("link-secret", "own-secret-eeeeeeeeeeeeeeee", 2)
        Dim lookup As Func(Of String, String) = AddressOf pairs.PasswordFor
        Dim rig As New Rig()
        Assert.True(rig.Try_(lookup, "listener", "link-secret"))
        Assert.True(pairs.Revoke("listener"))
        Assert.False(rig.Try_(lookup, "listener", "link-secret"))
        Assert.True(rig.Try_(lookup, "own", "own-secret-eeeeeeeeeeeeeeee"))
    End Sub

    <Fact>
    Public Sub A_lookup_that_knows_nobody_or_is_missing_refuses_everything()
        Dim rig As New Rig()
        Assert.False(rig.Try_(Function(user As String) CType(Nothing, String), "listener", "link-secret"))
        Assert.False(rig.Try_(Nothing, "listener", "link-secret"))
        Assert.False(rig.Try_(Function(user As String) "", "listener", ""))        ' an empty password never opens
    End Sub

    <Fact>
    Public Sub The_single_password_overload_still_serves_the_listener_user_alone()
        Dim rig As New Rig()
        Dim request As New LiveProducerWire.LiveWireRequest With {.Method = "GET", .RawPath = "/live.ts", .Path = "/live.ts"}
        rig.Now = 1500
        request.Headers("Authorization") = Answer("listener", rig.Nonce, "secret", "00000001", "/live.ts")
        Assert.True(rig.Auth.Verify(request, "secret", "client"))
        rig.Now = 3000
        request.Headers("Authorization") = Answer("own", rig.Nonce, "secret", "00000002", "/live.ts")
        Assert.False(rig.Auth.Verify(request, "secret", "client"))
    End Sub

    <Fact>
    Public Sub The_one_check_per_second_limit_holds_across_users()
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink("link-secret", "own-secret-ffffffffffffffff", 2)
        Dim lookup As Func(Of String, String) = AddressOf pairs.PasswordFor
        Dim rig As New Rig()
        rig.Now = 1500
        Dim first As New LiveProducerWire.LiveWireRequest With {.Method = "GET", .RawPath = "/live.ts", .Path = "/live.ts"}
        first.Headers("Authorization") = Answer("listener", rig.Nonce, "link-secret", "00000001", "/live.ts")
        Assert.True(rig.Auth.Verify(first, lookup, "client"))
        ' a second try 100 ms later - by the other user, with the right password - is still inside the same second
        rig.Now = 1600
        Dim second As New LiveProducerWire.LiveWireRequest With {.Method = "GET", .RawPath = "/live.ts", .Path = "/live.ts"}
        second.Headers("Authorization") = Answer("own", rig.Nonce, "own-secret-ffffffffffffffff", "00000002", "/live.ts")
        Assert.False(rig.Auth.Verify(second, lookup, "client"))
    End Sub
End Class

Public Class LiveCredentialServerTests
    Implements IDisposable

    Private ReadOnly _servers As New List(Of LiveBroadcastServer)()

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each s As LiveBroadcastServer In _servers
            s.Dispose()
        Next
    End Sub

    Private Shared Async Function StatusOfAsync(port As Integer, user As String, password As String) As Threading.Tasks.Task(Of HttpStatusCode)
        Using handler As New Http.HttpClientHandler With {.UseProxy = False, .Credentials = New NetworkCredential(user, password)}
            Using client As New Http.HttpClient(handler) With {.Timeout = TimeSpan.FromSeconds(5)}
                Using response As Http.HttpResponseMessage = Await client.GetAsync("http://127.0.0.1:" & port.ToString(Globalization.CultureInfo.InvariantCulture) & "/live-audio.aac", Http.HttpCompletionOption.ResponseHeadersRead)
                    Return response.StatusCode
                End Using
            End Using
        End Using
    End Function

    ''' <summary>A real HTTP client against the producer's server holding two pairs: each user opens with its own password, a wrong or
    ''' foreign password does not, and a revoked pair stops opening while the other keeps going. The pause between attempts is the
    ''' server's own one-check-per-second limit per source.</summary>
    <Fact>
    Public Async Function A_real_client_opens_with_either_pair_and_a_revoked_pair_is_refused() As Threading.Tasks.Task
        Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink("link-secret", "own-secret-gggggggggggggggg", 2)
        Dim port As Integer = LiveTestPorts.FreePort()
        Dim config As New LiveServerConfig With {
            .IcyName = "Kitchen PC", .IcyGenre = "live", .AudioBitrateKbps = 128,
            .AudioHub = New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() 0L),
            .Credentials = pairs
        }
        Dim server As New LiveBroadcastServer(IPAddress.Loopback, port, config)
        _servers.Add(server)
        Assert.Equal(LiveServerStartResult.Ok, server.Start())

        Assert.Equal(HttpStatusCode.OK, Await StatusOfAsync(port, "own", "own-secret-gggggggggggggggg"))
        Await Threading.Tasks.Task.Delay(1100)
        Assert.Equal(HttpStatusCode.OK, Await StatusOfAsync(port, "listener", "link-secret"))
        Await Threading.Tasks.Task.Delay(1100)
        Assert.Equal(HttpStatusCode.Unauthorized, Await StatusOfAsync(port, "own", "link-secret"))
        Await Threading.Tasks.Task.Delay(1100)

        Assert.True(pairs.Revoke("listener"))
        Assert.Equal(HttpStatusCode.Unauthorized, Await StatusOfAsync(port, "listener", "link-secret"))
        Await Threading.Tasks.Task.Delay(1100)
        Assert.Equal(HttpStatusCode.OK, Await StatusOfAsync(port, "own", "own-secret-gggggggggggggggg"))
    End Function

    ''' <summary>A server given a set with no pair serves openly (no password was chosen); one given a set with pairs challenges.</summary>
    <Fact>
    Public Async Function A_pair_set_without_pairs_is_open_like_no_password_and_a_protected_one_never_is() As Threading.Tasks.Task
        Dim openPort As Integer = LiveTestPorts.FreePort()
        Dim openConfig As New LiveServerConfig With {
            .IcyName = "t", .IcyGenre = "t", .AudioBitrateKbps = 128,
            .AudioHub = New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() 0L),
            .Credentials = LiveCredentialPairs.None()
        }
        Dim open As New LiveBroadcastServer(IPAddress.Loopback, openPort, openConfig)
        _servers.Add(open)
        Assert.Equal(LiveServerStartResult.Ok, open.Start())
        Using client As New Http.HttpClient() With {.Timeout = TimeSpan.FromSeconds(5)}
            Using response As Http.HttpResponseMessage = Await client.GetAsync("http://127.0.0.1:" & openPort.ToString(Globalization.CultureInfo.InvariantCulture) & "/live-audio.aac", Http.HttpCompletionOption.ResponseHeadersRead)
                Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            End Using
        End Using

        Dim lockedPort As Integer = LiveTestPorts.FreePort()
        Dim lockedConfig As New LiveServerConfig With {
            .IcyName = "t", .IcyGenre = "t", .AudioBitrateKbps = 128,
            .AudioHub = New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() 0L),
            .Credentials = LiveCredentialPairs.ForLink("link-secret", iterations:=2)
        }
        Dim locked As New LiveBroadcastServer(IPAddress.Loopback, lockedPort, lockedConfig)
        _servers.Add(locked)
        Assert.Equal(LiveServerStartResult.Ok, locked.Start())
        Using client As New Http.HttpClient() With {.Timeout = TimeSpan.FromSeconds(5)}
            Using response As Http.HttpResponseMessage = Await client.GetAsync("http://127.0.0.1:" & lockedPort.ToString(Globalization.CultureInfo.InvariantCulture) & "/live-audio.aac", Http.HttpCompletionOption.ResponseHeadersRead)
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode)
            End Using
        End Using
    End Function
End Class
#End If
