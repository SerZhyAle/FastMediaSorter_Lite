#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 263 T-3 (CR-14): the credential pairs of ONE live broadcast and the verifiers the relay checks a listener against.
'
' Contracts: DEVICE-EXCHANGE item BF (the relay checks Basic against uploaded `verifiers`, one entry per pair) and item DV (the
' member is sent on every `broadcast-start` and `broadcast-update`, `[]` when the link has no password); LIVE-BROADCAST items M
' and R. One set serves BOTH checks, so a pair can never be valid at the relay and refused at the producer or the other way
' round: the producer's Digest asks this set for the password of the user a listener names (<see cref="LiveCredentialPairs.PasswordFor"/>),
' and the wire gets the verifier list of the same set (<see cref="LiveCredentialPairs.VerifierRecords"/>). Dropping one pair removes
' it from both at once (<see cref="LiveCredentialPairs.Revoke"/>); the caller then announces the new list with a `broadcast-update`.
'
' Cost: a verifier is PBKDF2-HMAC-SHA256 at 600000 rounds, about 150-300 ms. It is made lazily, ONCE per pair, by whoever first asks
' for the wire list (the agent does it on a pool thread), and never per request: the Digest check needs the password, not the hash.
' Nothing here logs. A password never reaches a message, an exception or a ToString.

''' <summary>One entry of the `verifiers` member (item BF): <c>{"user", "salt", "hash"}</c>. It is derived from a password and
''' holds nothing a reader could use to play, but it is secret-derived: <see cref="ToString"/> prints no member.</summary>
Friend NotInheritable Class ListenerVerifier

    ''' <summary>The PBKDF2 round count of item BF. Part of the contract, not of the frame: another count never matches.</summary>
    Public Const Iterations As Integer = 600000
    ''' <summary>Decoded size of <see cref="Salt"/>.</summary>
    Public Const SaltLength As Integer = 16
    ''' <summary>Decoded size of <see cref="Hash"/>.</summary>
    Public Const HashLength As Integer = 32

    Public ReadOnly Property User As String
    ''' <summary>16 random bytes, base64url without padding.</summary>
    Public ReadOnly Property Salt As String
    ''' <summary>32 bytes, base64url without padding.</summary>
    Public ReadOnly Property Hash As String

    Private Sub New(user As String, salt As String, hash As String)
        Me.User = user
        Me.Salt = salt
        Me.Hash = hash
    End Sub

    ''' <summary>The entry for one pair under a fresh random salt. <paramref name="rounds"/> exists so that a test need not pay
    ''' 600000 rounds for every hash; the wire always carries <see cref="Iterations"/>.</summary>
    Public Shared Function Create(user As String, password As String, Optional rounds As Integer = Iterations) As ListenerVerifier
        Dim salt As Byte() = RandomNumberGenerator.GetBytes(SaltLength)
        Dim hash As Byte() = Derive(password, salt, rounds)
        Try
            Return New ListenerVerifier(user, Base64Url(salt), Base64Url(hash))
        Finally
            Array.Clear(hash, 0, hash.Length)
        End Try
    End Function

    ''' <summary>PBKDF2-HMAC-SHA256 over the raw UTF-8 bytes of the password (no normalisation, no trimming).</summary>
    Friend Shared Function Derive(password As String, salt As Byte(), rounds As Integer) As Byte()
        Dim raw As Byte() = Encoding.UTF8.GetBytes(If(password, ""))
        Try
            Return Rfc2898DeriveBytes.Pbkdf2(raw, salt, rounds, HashAlgorithmName.SHA256, HashLength)
        Finally
            Array.Clear(raw, 0, raw.Length)
        End Try
    End Function

    ''' <summary>True when <paramref name="password"/> is the one this entry was made from (a test seam and a self-check; the relay
    ''' does the same with the bytes it decoded).</summary>
    Public Function Matches(password As String, Optional rounds As Integer = Iterations) As Boolean
        Dim salt As Byte() = FromBase64Url(Me.Salt)
        Dim expected As Byte() = FromBase64Url(Me.Hash)
        Dim actual As Byte() = Derive(password, salt, rounds)
        Try
            Return CryptographicOperations.FixedTimeEquals(expected, actual)
        Finally
            Array.Clear(actual, 0, actual.Length)
        End Try
    End Function

    ''' <summary>The wire object, members in the contract's order.</summary>
    Public Function ToJson() As String
        Using ms As New IO.MemoryStream()
            Using w As New Utf8JsonWriter(ms)
                w.WriteStartObject()
                w.WriteString("user", User)
                w.WriteString("salt", Salt)
                w.WriteString("hash", Hash)
                w.WriteEndObject()
            End Using
            Return Encoding.UTF8.GetString(ms.ToArray())
        End Using
    End Function

    Friend Shared Function Base64Url(bytes As Byte()) As String
        Return Convert.ToBase64String(bytes).TrimEnd("="c).Replace("+"c, "-"c).Replace("/"c, "_"c)
    End Function

    Friend Shared Function FromBase64Url(text As String) As Byte()
        Dim s As String = text.Replace("-"c, "+"c).Replace("_"c, "/"c)
        Select Case s.Length Mod 4
            Case 2 : s &= "=="
            Case 3 : s &= "="
        End Select
        Return Convert.FromBase64String(s)
    End Function

    ''' <summary>Never a member.</summary>
    Public Overrides Function ToString() As String
        Return "ListenerVerifier"
    End Function

End Class

''' <summary>The credential pairs of one broadcast, in the order they were made. A broadcast with a link password holds the LINK
''' pair (user <see cref="LinkUser"/>, the password the person typed) and the OWN pair (user <see cref="OwnUser"/>, a random
''' password the broadcaster carries to its account's own devices in the sealed box, item DK). A broadcast with no password holds
''' no pair at all and <see cref="IsProtected"/> is False: the relay is told so with an empty `verifiers` (item DV).
''' Guest and per-link pairs are NOT made here (ticket 263 T-4 waits for the worker's per-grant login).</summary>
Friend NotInheritable Class LiveCredentialPairs

    ''' <summary>The user of the pair the person typed; the descriptor's `auth.user` (item M).</summary>
    Public Const LinkUser As String = "listener"
    ''' <summary>The user of the pair for the account's own devices (item BF, carried as `credentials` in the sealed box).</summary>
    Public Const OwnUser As String = "own"
    ''' <summary>The server refuses more entries than this (validate.go maxVerifiers).</summary>
    Public Const MaxPairs As Integer = 64
    ''' <summary>The server refuses a longer user name (validate.go maxVerifierUser).</summary>
    Public Const MaxUserChars As Integer = 64

    Private NotInheritable Class Pair
        Public ReadOnly User As String
        Public ReadOnly Password As String
        Public ReadOnly Verifier As Lazy(Of ListenerVerifier)

        Public Sub New(user As String, password As String, iterations As Integer)
            Me.User = user
            Me.Password = password
            Verifier = New Lazy(Of ListenerVerifier)(Function() ListenerVerifier.Create(user, password, iterations),
                                                     LazyThreadSafetyMode.ExecutionAndPublication)
        End Sub
    End Class

    Private ReadOnly _gate As New Object()
    Private ReadOnly _pairs As New List(Of Pair)()
    Private ReadOnly _iterations As Integer
    Private _protected As Boolean

    Private Sub New(iterations As Integer)
        _iterations = iterations
    End Sub

    ''' <summary>A set that protects nothing: no pair, <see cref="IsProtected"/> False.</summary>
    Public Shared Function None() As LiveCredentialPairs
        Return New LiveCredentialPairs(ListenerVerifier.Iterations)
    End Function

    ''' <summary>The pairs of a broadcast whose link password is <paramref name="linkPassword"/>: empty or blank gives
    ''' <see cref="None"/>; otherwise the link pair and a fresh own pair. <paramref name="ownPassword"/> is for tests (Nothing: random,
    ''' 32 characters of base64url). <paramref name="iterations"/> is for tests; the wire always claims the contract's count.</summary>
    Public Shared Function ForLink(linkPassword As String, Optional ownPassword As String = Nothing,
                                   Optional iterations As Integer = ListenerVerifier.Iterations) As LiveCredentialPairs
        Dim set_ As New LiveCredentialPairs(iterations)
        If String.IsNullOrEmpty(linkPassword) Then Return set_
        set_.Add(LinkUser, linkPassword)
        set_.Add(OwnUser, If(String.IsNullOrEmpty(ownPassword), NewOwnPassword(), ownPassword))
        Return set_
    End Function

    ''' <summary>24 random bytes as base64url: 32 characters, no ':' and no control character, so it is a valid Basic password.</summary>
    Public Shared Function NewOwnPassword() As String
        Return ListenerVerifier.Base64Url(RandomNumberGenerator.GetBytes(24))
    End Function

    ''' <summary>True when the set says what the server enforces: a user name of 1 to 64 characters with no ':' (a Basic user-id
    ''' cannot hold one) and no control character.</summary>
    Public Shared Function IsValidUser(user As String) As Boolean
        If String.IsNullOrEmpty(user) OrElse user.Length > MaxUserChars OrElse user.Contains(":"c) Then Return False
        For Each ch As Char In user
            If Char.IsControl(ch) Then Return False
        Next
        Return True
    End Function

    ''' <summary>Adds one pair. False (and no change) for an invalid or already present user, an empty password, or a full set.</summary>
    Public Function Add(user As String, password As String) As Boolean
        If Not IsValidUser(user) OrElse String.IsNullOrEmpty(password) Then Return False
        SyncLock _gate
            If _pairs.Count >= MaxPairs OrElse Find(user) IsNot Nothing Then Return False
            _pairs.Add(New Pair(user, password, _iterations))
            _protected = True
            Return True
        End SyncLock
    End Function

    ''' <summary>True once the set held a pair, and it stays True when pairs are revoked: a protected broadcast never turns open by
    ''' losing credentials. The producer's Digest keys on this.</summary>
    Public ReadOnly Property IsProtected As Boolean
        Get
            SyncLock _gate
                Return _protected
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property Count As Integer
        Get
            SyncLock _gate
                Return _pairs.Count
            End SyncLock
        End Get
    End Property

    ''' <summary>The users now held, in order.</summary>
    Public Function Users() As IReadOnlyList(Of String)
        SyncLock _gate
            Dim list As New List(Of String)(_pairs.Count)
            For Each p As Pair In _pairs
                list.Add(p.User)
            Next
            Return list
        End SyncLock
    End Function

    ''' <summary>The password of a held user, Nothing for any other (the producer's Digest lookup). Ordinal, case-sensitive.</summary>
    Public Function PasswordFor(user As String) As String
        If user Is Nothing Then Return Nothing
        SyncLock _gate
            Dim p As Pair = Find(user)
            Return If(p Is Nothing, Nothing, p.Password)
        End SyncLock
    End Function

    ''' <summary>Drops ONE pair: it leaves the Digest lookup and the verifier list at once. False when the user is not held, and also
    ''' when it is the LAST pair: the wire cannot say "nobody may listen" (an empty `verifiers` means the relay checks nobody, item DV),
    ''' so a protected broadcast keeps at least one pair; opening the link is the person's choice of "no password", a change of its own.</summary>
    Public Function Revoke(user As String) As Boolean
        If user Is Nothing Then Return False
        SyncLock _gate
            Dim p As Pair = Find(user)
            If p Is Nothing OrElse _pairs.Count <= 1 Then Return False
            _pairs.Remove(p)
            Return True
        End SyncLock
    End Function

    ''' <summary>The verifiers of the pairs held now, as the wire objects of item BF in the order of the pairs; an empty list for a set
    ''' with no pair (item DV: that is sent as `[]`). The first call pays the PBKDF2 of every pair that has none yet, in parallel -
    ''' call it off the UI thread; a later call is free. The result is a snapshot: a pair revoked afterwards is absent from the next one.</summary>
    Public Function VerifierRecords() As IReadOnlyList(Of String)
        Dim snapshot As List(Of Pair)
        SyncLock _gate
            snapshot = New List(Of Pair)(_pairs)
        End SyncLock
        Dim pending As New List(Of Pair)()
        For Each p As Pair In snapshot
            If Not p.Verifier.IsValueCreated Then pending.Add(p)
        Next
        If pending.Count > 1 Then
            Parallel.ForEach(pending, Sub(p As Pair) Made(p))
        End If
        Dim records As New List(Of String)(snapshot.Count)
        For Each p As Pair In snapshot
            records.Add(p.Verifier.Value.ToJson())
        Next
        Return records
    End Function

    ''' <summary>Forces the pair's verifier (the PBKDF2 runs here, once).</summary>
    Private Shared Function Made(p As Pair) As Boolean
        Return p.Verifier.Value IsNot Nothing
    End Function

    Private Function Find(user As String) As Pair
        For Each p As Pair In _pairs
            If String.Equals(p.User, user, StringComparison.Ordinal) Then Return p
        Next
        Return Nothing
    End Function

    ''' <summary>Never a password.</summary>
    Public Overrides Function ToString() As String
        Return "LiveCredentialPairs"
    End Function

End Class
#End If
