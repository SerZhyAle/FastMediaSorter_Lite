#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Threading

Friend Enum HostKeyVerdict
    ''' <summary>The key is the pinned one (or was just accepted and the pin persisted).</summary>
    Accepted = 0
    ''' <summary>A pin exists and the offered key is not it. Terminal for the whole attempt.</summary>
    Mismatch = 1
    ''' <summary>Unpinned server and the user did not accept (Cancel is the default).</summary>
    ConsentDeclined = 2
    ''' <summary>The user accepted but the pin could not be stored: authentication must NOT
    ''' proceed, and there is no fall back to "accept any key".</summary>
    PersistFailed = 3
    ''' <summary>The offered key or the stored pin is unusable. Fails closed.</summary>
    Malformed = 4
    Cancelled = 5
End Enum

Friend NotInheritable Class HostKeyDecision
    Friend Sub New(verdict As HostKeyVerdict, Optional expected As String = "", Optional received As String = "")
        Me.Verdict = verdict
        Me.Expected = If(expected, "")
        Me.Received = If(received, "")
    End Sub
    Friend ReadOnly Property Verdict As HostKeyVerdict
    ''' <summary>The pinned fingerprint, for the "host key changed" dialog. A fingerprint is
    ''' public information, not a secret.</summary>
    Friend ReadOnly Property Expected As String
    Friend ReadOnly Property Received As String
    Friend ReadOnly Property IsAccepted As Boolean
        Get
            Return Verdict = HostKeyVerdict.Accepted
        End Get
    End Property

    ''' <summary>The failure class a refusal is reported as; Nothing when accepted.</summary>
    Friend Function ToFailure() As ResourceFailure
        Select Case Verdict
            Case HostKeyVerdict.Accepted
                Return Nothing
            Case HostKeyVerdict.Cancelled, HostKeyVerdict.ConsentDeclined
                Return New ResourceFailure(ResourceFailureKind.Cancelled)
            Case HostKeyVerdict.PersistFailed
                Return New ResourceFailure(ResourceFailureKind.LocalIO, Nothing, "pin not stored")
            Case Else
                Return New ResourceFailure(ResourceFailureKind.KeyMismatch)
        End Select
    End Function
End Class

''' <summary>
''' The host-key decision for ONE logical server (one profile, any number of alternate
''' endpoints), made before authentication (100 §15.5 item 4):
''' pinned -> strict constant-time match, a mismatch is terminal; unpinned -> exactly one
''' consent at a time, Cancel by default, the accepted pin is <b>persisted before</b> the
''' key is accepted, and every later endpoint of the same server is matched strictly against
''' it. A competing attempt that loses the race can never overwrite the pin: the pin only
''' moves from empty to a value, under the gate's lock.
''' </summary>
Friend NotInheritable Class HostKeyGate

    Private ReadOnly gate As New SemaphoreSlim(1, 1)
    Private ReadOnly consent As Func(Of String, CancellationToken, Boolean)
    Private ReadOnly persist As Func(Of String, Boolean)
    Private ReadOnly nowMs As Func(Of Long)
    Private pinned As String
    Private consent_Ms As Long
    Private consent_Started As Long = -1

    ''' <param name="pinnedFingerprint">Empty for an unpinned server.</param>
    ''' <param name="requestConsent">Asks the user about the offered fingerprint; returns True
    ''' only on an explicit accept. Runs while the gate is held, so a second endpoint's
    ''' handshake waits instead of producing a second prompt.</param>
    ''' <param name="persistPin">Stores the accepted pin; False or a throw means it was not stored.</param>
    Friend Sub New(pinnedFingerprint As String, requestConsent As Func(Of String, CancellationToken, Boolean),
                   persistPin As Func(Of String, Boolean), clockMs As Func(Of Long))
        pinned = If(pinnedFingerprint, "")
        consent = requestConsent
        persist = persistPin
        nowMs = clockMs
    End Sub

    ''' <summary>The pin in force: the imported/stored one, or the one just accepted.</summary>
    Friend ReadOnly Property Pin As String
        Get
            Return Volatile.Read(pinned)
        End Get
    End Property

    ''' <summary>Consent time so far INCLUDING a prompt still open - what a network deadline
    ''' subtracts, so a user thinking is never counted as a slow server.</summary>
    Friend Function ConsentMsSoFar() As Long
        Dim started As Long = Interlocked.Read(consent_Started)
        Dim open As Long = If(started >= 0, Math.Max(0L, nowMs() - started), 0L)
        Return Interlocked.Read(consent_Ms) + open
    End Function

    Friend ReadOnly Property ConsentOutstanding As Boolean
        Get
            Return Interlocked.Read(consent_Started) >= 0
        End Get
    End Property

    ''' <summary>Decides whether the offered host key may be used. Thread-safe.</summary>
    Friend Function Verify(hostKeyBlob As Byte(), ct As CancellationToken) As HostKeyDecision
        If hostKeyBlob Is Nothing OrElse hostKeyBlob.Length = 0 Then Return New HostKeyDecision(HostKeyVerdict.Malformed)
        Dim received As String = SshFingerprint.OfHostKey(hostKeyBlob)

        Try
            gate.Wait(ct)
        Catch ex As OperationCanceledException
            Return New HostKeyDecision(HostKeyVerdict.Cancelled, Pin, received)
        End Try
        Try
            Dim current As String = Pin
            If current.Length > 0 Then
                Dim digest As Byte() = Nothing
                If Not SshFingerprint.TryParse(current, digest) Then Return New HostKeyDecision(HostKeyVerdict.Malformed, current, received)
                Return If(SshFingerprint.MatchesHostKey(current, hostKeyBlob),
                          New HostKeyDecision(HostKeyVerdict.Accepted, current, received),
                          New HostKeyDecision(HostKeyVerdict.Mismatch, current, received))
            End If

            ' Unpinned: one prompt, here, with everything else waiting at the gate.
            Dim started As Long = nowMs()
            Interlocked.Exchange(consent_Started, started)
            Dim accepted As Boolean
            Try
                accepted = consent IsNot Nothing AndAlso consent(received, ct)
            Catch ex As OperationCanceledException
                Return New HostKeyDecision(HostKeyVerdict.Cancelled, "", received)
            Catch ex As Exception
                accepted = False
            Finally
                Interlocked.Add(consent_Ms, Math.Max(0L, nowMs() - started))
                Interlocked.Exchange(consent_Started, -1L)
            End Try
            If ct.IsCancellationRequested Then Return New HostKeyDecision(HostKeyVerdict.Cancelled, "", received)
            If Not accepted Then Return New HostKeyDecision(HostKeyVerdict.ConsentDeclined, "", received)

            Dim stored As Boolean
            Try
                stored = persist IsNot Nothing AndAlso persist(received)
            Catch ex As Exception
                stored = False
            End Try
            If Not stored Then Return New HostKeyDecision(HostKeyVerdict.PersistFailed, "", received)

            Volatile.Write(pinned, received)
            Return New HostKeyDecision(HostKeyVerdict.Accepted, received, received)
        Finally
            gate.Release()
        End Try
    End Function

End Class
#End If
