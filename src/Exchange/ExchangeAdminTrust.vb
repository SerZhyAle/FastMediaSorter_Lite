#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Threading
Imports System.Threading.Tasks

' L0278 T-1 (specification section 5.4). The trust decision of a remote-administration sign-in: ONE pure function over what a
' handshake showed (the presented leaf, its key, whether the platform trust store validates the chain) and the pins the target
' record holds. Pure on purpose: the table is tested without a socket, and nothing here knows a word a person reads.
'
'   held pin | platform chain | leaf equals pin or key pin matches | decision
'   none     | valid          | -                                  | PlatformValid  (nothing is pinned; the platform is re-checked on the connection that carries the secret)
'   none     | not valid      | -                                  | AskFirstContact (the fingerprint and the key pin go to the question)
'   held     | valid          | no                                 | PlatformValid  (the held pin is LEFT AS IT IS: this decision writes nothing)
'   held     | valid          | yes                                | PinMatch
'   held     | not valid      | yes                                | PinMatch       (a renewal that kept the key; nothing to write back)
'   held     | not valid      | no                                 | StopChanged    (both fingerprints, no password sent)
'
' ExchangeTrust.Evaluate is NOT used and NOT changed: it is pin-first (a held pin that differs wins over a platform acceptance), which
' is what the exchange client wants and the opposite of row 3. The inputs come from ExchangeTlsTransport: a pinned connect first when
' a pin is held, one probe when that connect fails with PinMismatch; the probe first when there is no pin. No application byte is
' ever written by anything in this file.

''' <summary>What the trust decision came to (L0278 5.4).</summary>
Friend Enum ExchangeAdminTrustOutcome
    ''' <summary>Accept: the platform validated the chain. Nothing is pinned or recorded; a pin that is held stays as it is.</summary>
    PlatformValid = 0
    ''' <summary>Accept: the presented leaf is the held pin, or its key is the held key pin.</summary>
    PinMatch = 1
    ''' <summary>First contact: no pin and the platform rejects the chain; only a person may accept the presented fingerprint.</summary>
    AskFirstContact = 2
    ''' <summary>A pin is held and the presented leaf is neither it nor on the key pin: a stop; nothing is recorded and no password is sent.</summary>
    StopChanged = 3
End Enum

''' <summary>One decision with what the question or the stop needs to show: the presented leaf fingerprint, its key pin and (when a pin
''' is held) the pinned fingerprint.</summary>
Friend NotInheritable Class ExchangeAdminTrustDecision

    Public ReadOnly Outcome As ExchangeAdminTrustOutcome
    ''' <summary>The presented leaf ("SHA256:" plus 43 base64 characters), "" when no certificate was seen.</summary>
    Public ReadOnly Fingerprint As String
    ''' <summary>The key pin of the presented leaf, "" when not known.</summary>
    Public ReadOnly KeyPin As String
    ''' <summary>The leaf pin the target holds ("" for none).</summary>
    Public ReadOnly PinnedFingerprint As String

    Public Sub New(outcome As ExchangeAdminTrustOutcome, fingerprint As String, keyPin As String, pinnedFingerprint As String)
        Me.Outcome = outcome
        Me.Fingerprint = If(fingerprint, "")
        Me.KeyPin = If(keyPin, "")
        Me.PinnedFingerprint = If(pinnedFingerprint, "")
    End Sub

    ''' <summary>True for the two Accept outcomes: the password may be read from the field and sent on a connection that passes this again.</summary>
    Public ReadOnly Property IsAccepted As Boolean
        Get
            Return Outcome = ExchangeAdminTrustOutcome.PlatformValid OrElse Outcome = ExchangeAdminTrustOutcome.PinMatch
        End Get
    End Property

End Class

''' <summary>The two handshakes the decision is fed from, behind an interface so a test hands back what a server would show.
''' Neither writes an application byte.</summary>
Friend Interface IExchangeAdminHandshake
    ''' <summary>A TLS connect that accepts only the pinned leaf or its key (or a platform-valid chain when no pin is held). Throws
    ''' <c>ExchangeConnectException</c>; on a pin that does not match, its failure is PinMismatch.</summary>
    Function ConnectPinnedAsync(host As String, port As Integer, leafPin As String, keyPin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection)
    ''' <summary>A handshake that accepts any certificate, reports it and the platform verdict, and closes.</summary>
    Function ProbeAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of ExchangeCertificateInfo)
End Interface

''' <summary>The real handshakes: <c>ExchangeTlsTransport</c>.</summary>
Friend NotInheritable Class ExchangeAdminHandshake
    Implements IExchangeAdminHandshake

    Public Shared ReadOnly Instance As New ExchangeAdminHandshake()

    Private Sub New()
    End Sub

    Public Function ConnectPinnedAsync(host As String, port As Integer, leafPin As String, keyPin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeAdminHandshake.ConnectPinnedAsync
        Return ExchangeTlsTransport.Instance.ConnectPinnedAsync(host, port, leafPin, keyPin, ct)
    End Function

    Public Function ProbeAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeAdminHandshake.ProbeAsync
        Return ExchangeTlsTransport.ProbeAsync(host, port, ct)
    End Function

End Class

''' <summary>The decision and, in the common case of a held pin that matched, the connection that proved it. No byte has been written
''' on the connection; the caller owns it (Dispose) and sends on it only when the decision is an Accept.</summary>
Friend NotInheritable Class ExchangeAdminTrustResolution
    Implements IDisposable

    Public ReadOnly Decision As ExchangeAdminTrustDecision
    ''' <summary>The open pinned connection when the pinned connect itself succeeded, else Nothing (a probe closes its own).</summary>
    Public ReadOnly Connection As ExchangeTlsConnection

    Public Sub New(decision As ExchangeAdminTrustDecision, connection As ExchangeTlsConnection)
        Me.Decision = decision
        Me.Connection = connection
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If Connection IsNot Nothing Then Connection.Dispose()
    End Sub

End Class

Friend NotInheritable Class ExchangeAdminTrust

    Private Sub New()
    End Sub

    ''' <summary>The table above, chain first. A pin is "held" when the leaf pin is not empty (as in <c>ExchangeTrust.Evaluate</c>); the
    ''' key pin counts only when it has the pin form. Empty or Nothing inputs never match a pin.</summary>
    Public Shared Function Decide(platformValid As Boolean, presentedFingerprint As String, presentedKeyPin As String,
                                  heldLeafPin As String, heldKeyPin As String) As ExchangeAdminTrustDecision
        Dim fp As String = If(presentedFingerprint, "")
        Dim key As String = If(presentedKeyPin, "")
        Dim held As String = If(heldLeafPin, "")
        Dim hasPin As Boolean = held.Length > 0
        Dim matches As Boolean = hasPin AndAlso
            (String.Equals(held, fp, StringComparison.Ordinal) OrElse
             (ExchangeTrust.IsFingerprint(heldKeyPin) AndAlso String.Equals(heldKeyPin, key, StringComparison.Ordinal)))

        If platformValid Then
            Return New ExchangeAdminTrustDecision(If(matches, ExchangeAdminTrustOutcome.PinMatch, ExchangeAdminTrustOutcome.PlatformValid), fp, key, held)
        End If
        If Not hasPin Then Return New ExchangeAdminTrustDecision(ExchangeAdminTrustOutcome.AskFirstContact, fp, key, "")
        Return New ExchangeAdminTrustDecision(If(matches, ExchangeAdminTrustOutcome.PinMatch, ExchangeAdminTrustOutcome.StopChanged), fp, key, held)
    End Function

    ''' <summary>The inputs of <c>Decide</c> from the wire, with no application byte sent. With a pin held: the pinned connect
    ''' first (no extra handshake in the common case - its connection is handed over), and on PinMismatch one probe. Without a pin: the
    ''' probe. Any other <c>ExchangeConnectException</c> (network, TLS, cancelled, timed out) is not caught here.</summary>
    Public Shared Async Function ResolveAsync(tls As IExchangeAdminHandshake, host As String, port As Integer, leafPin As String, keyPin As String,
                                              ct As CancellationToken) As Task(Of ExchangeAdminTrustResolution)
        Dim held As String = If(leafPin, "")
        If held.Length > 0 Then
            Dim conn As ExchangeTlsConnection = Nothing
            Try
                conn = Await tls.ConnectPinnedAsync(host, port, held, keyPin, ct).ConfigureAwait(False)
            Catch ex As ExchangeConnectException When ex.Failure = ExchangeConnectFailure.PinMismatch
                conn = Nothing
            End Try
            If conn IsNot Nothing Then
                Return New ExchangeAdminTrustResolution(Decide(conn.Verdict = ExchangeTrustVerdict.PlatformValid, conn.Fingerprint, conn.KeyPin, held, keyPin), conn)
            End If
        End If
        Dim info As ExchangeCertificateInfo = Await tls.ProbeAsync(host, port, ct).ConfigureAwait(False)
        Return New ExchangeAdminTrustResolution(Decide(info.PlatformValid, info.Fingerprint, info.KeyPin, held, keyPin), Nothing)
    End Function

    ''' <summary>The overload above over the real TLS transport (<c>ExchangeAdminHandshake.Instance</c>).</summary>
    Public Shared Function ResolveAsync(host As String, port As Integer, leafPin As String, keyPin As String, ct As CancellationToken) As Task(Of ExchangeAdminTrustResolution)
        Return ResolveAsync(ExchangeAdminHandshake.Instance, host, port, leafPin, keyPin, ct)
    End Function

End Class
#End If
