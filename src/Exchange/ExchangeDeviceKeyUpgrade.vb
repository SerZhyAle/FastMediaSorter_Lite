#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

' A device that enrolled before it had a key (DEVICE-EXCHANGE 27 item DF) is no recipient of anything sealed, and so it can open nothing
' its account's other devices publish (item DP). The server keeps such a device and lets it present its FIRST key by enrolling again
' under the same deviceId. Nobody has to do anything for that: the device still holds a live stream, so it asks that stream for a
' one-time pairing code (5.3) and enrolls with the code, on its own, with no password.
'
' The two hosts - the viewer's per-entry session and the Share Manager's agent - share the decision and the frame here; each keeps
' its own swap of the stream, because each owns its session differently. In both the order is the same:
'
'   1. the key is made and kept first; a key that cannot be kept is not presented (it would be lost at the next start);
'   2. the code is asked for over the live stream; a refusal or no answer leaves the session exactly as it was;
'   3. the old stream goes, BEFORE `enroll`: the server closes it when the token rotates, and a session left running would answer
'      that with a `hello` under the dead token and land on "sign in again";
'   4. `enroll` carries the whole description of the device (the server replaces the record) and the key; on `enrolled` the new
'      token is stored and the stream it arrived on becomes the control stream; otherwise the old token is used to start again.
'
' A key is never taken back on this road: it belongs to the deviceId for as long as the id lives, whatever the server answered.
' The code and the token are secrets and reach no log, and neither does a server, a login or a key.

''' <summary>The decision and the frame of presenting a first key.</summary>
Friend NotInheritable Class ExchangeKeyUpgrade

    Private Sub New()
    End Sub

    ''' <summary>This session's own record in its directory, or Nothing while it is not listed.</summary>
    Public Shared Function OwnRecordOf(session As ExchangeControlSession) As ExchangeDeviceRecord
        If session Is Nothing OrElse Not session.Model.HasSnapshot Then Return Nothing
        Dim own As String = session.ConnectionOptions.Credentials.DeviceId
        Return session.Model.Snapshot().Devices.FirstOrDefault(Function(d) String.Equals(d.DeviceId, own, StringComparison.Ordinal))
    End Function

    ''' <summary>True when this device can and must present its first key: online with a directory, on a server that seals and pairs, and
    ''' listed by that server without a key. A device that is not listed is not due: nothing says it has no key.</summary>
    Public Shared Function IsDue(session As ExchangeControlSession) As Boolean
        If session Is Nothing OrElse session.State <> ExchangeAgentState.Online Then Return False
        Dim features As IReadOnlyList(Of String) = session.Features
        If Not features.Contains(ExchangeSealedNames.FeatureSealed) OrElse Not features.Contains(ExchangeWire.FeaturePairing) Then Return False
        Dim own As ExchangeDeviceRecord = OwnRecordOf(session)
        Return own IsNot Nothing AndAlso own.E2eKem.Length = 0
    End Function

    ''' <summary>The name the server holds for this device, so the upgrade renames nothing; the given name when it holds none.</summary>
    Public Shared Function NameFor(session As ExchangeControlSession, fallback As String) As String
        Dim own As ExchangeDeviceRecord = OwnRecordOf(session)
        Dim listed As String = If(own Is Nothing, "", ExchangeDeviceDescription.Clip(own.DeviceName, 64))
        If listed.Length > 0 Then Return listed
        Dim given As String = ExchangeDeviceDescription.Clip(fallback, 64)
        Return If(given.Length > 0, given, "Windows PC")
    End Function

    ''' <summary>TLS on the session's own terms, then `enroll` with the pairing code and the key. The frame's copy of the code goes once sent.
    ''' The caller owns the result, and the stream in it on success.</summary>
    Public Shared Async Function PresentFirstKeyAsync(options As ExchangeControlOptions, login As String, code As String, description As ExchangeDeviceDescription,
                                                      ct As CancellationToken) As Task(Of ExchangeOpenResult)
        Dim enroll As ExchangeEnvelope = ExchangeMessages.Enroll(login, "", If(code, "").Trim().ToUpperInvariant(), description, ExchangeWire.ProposedKeepaliveSeconds)
        Try
            Return Await ExchangeSessions.EnrollAsync(options.Transport, options.Server, options.Pin, enroll, ct).ConfigureAwait(False)
        Finally
            enroll.SetText("password", "").SetText("pairingCode", "")
        End Try
    End Function

End Class

''' <summary>How often a device tries. A try costs a dropped stream, so a server that keeps refusing is asked once a minute at first and
''' at most once an hour later, and a server that answers `enrolled` and still lists no key is paced the same way.</summary>
Friend NotInheritable Class ExchangeKeyUpgradeGate

    Friend Const FirstPauseMs As Long = 60000
    Friend Const LongestPauseMs As Long = 3600000

    Private ReadOnly _gate As New Object()
    Private _running As Boolean
    Private _notBefore As Long = Long.MinValue
    Private _nextPause As Long = FirstPauseMs

    ''' <summary>True when a try may start now; the caller then owes one <see cref="Finish"/>.</summary>
    Public Function TryBegin(now As Long) As Boolean
        SyncLock _gate
            If _running OrElse now < _notBefore Then Return False
            _running = True
            Return True
        End SyncLock
    End Function

    Public Sub Finish(now As Long)
        SyncLock _gate
            _running = False
            _notBefore = now + _nextPause
            _nextPause = Math.Min(_nextPause * 2, LongestPauseMs)
        End SyncLock
    End Sub

    ''' <summary>True while a try runs or may start now: a question about the key is worth waiting for only then.</summary>
    Public Function IsPending(now As Long) As Boolean
        SyncLock _gate
            Return _running OrElse now >= _notBefore
        End SyncLock
    End Function

End Class
#End If
