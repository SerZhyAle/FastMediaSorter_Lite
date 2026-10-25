Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

Partial Friend NotInheritable Class ExchangeDeviceAgent
    Private ReadOnly _guestGate As New SemaphoreSlim(1, 1)
    Private ReadOnly _guestStamps As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private ReadOnly _guestKeys As New Dictionary(Of String, Tuple(Of ExchangeSealedBroadcastSession, String))(StringComparer.Ordinal)
    Private _guestSession As ExchangeControlSession
    Private _guestGeneration As Integer
    Private _guestSeenGeneration As Integer

    ' A successful result means the request was sent. The directory is the authority for the grant's existence.
    Public Async Function GrantGuestAsync(guest As String, targetId As String, broadcast As Boolean, remove As Boolean, ct As CancellationToken) As Task(Of String)
        Dim view = GetSealedRecipients()
        If view Is Nothing Then Return "not-connected"
        If Not broadcast AndAlso Not remove AndAlso Not OutwardAccessFeatures.IsOffered() Then Return "no-access"
        If Not view.CanSealThroughServer Then Return If(view.ServerMessage?.Code, "e2e-missing")
        guest = ExchangeLoginRules.Normalize(guest)
        If guest.Length = 0 OrElse Not ExchangeIds.IsValidId(targetId) Then Return "bad-request"
        Dim snapshot = GetFullDirectory()
        If broadcast Then
            If Not snapshot.Broadcasts.Any(Function(b) b.BroadcastId = targetId AndAlso b.DeviceId = view.OwnDeviceId) Then Return "unknown-resource"
        ElseIf Not snapshot.Resources.Any(Function(r) r.ResourceId = targetId AndAlso r.DeviceId = view.OwnDeviceId) Then
            Return "unknown-resource"
        End If
        Dim session As ExchangeControlSession
        SyncLock _gate
            session = _session
        End SyncLock
        If session Is Nothing Then Return "not-connected"
        For Each raw In snapshot.Grants
            Using doc = JsonDocument.Parse(raw)
                Dim g = doc.RootElement
                If GrantText(g, "guest") <> guest OrElse GrantText(g, If(broadcast, "broadcastId", "resourceId")) <> targetId Then Continue For
                If Not remove Then
                    Await RefreshGuestGrantsAsync().ConfigureAwait(False)
                    Return ""
                End If
                Return If(Await session.SendAsync(ExchangeMessages.Ungrant(GrantText(g, "grantId")), ct).ConfigureAwait(False), "", "not-connected")
            End Using
        Next
        If remove Then Return "no-access"
        Dim grantId As String = LiveProducerTokens.NewId()
        Using key = KeyStore().TryLoad(view.OwnDeviceId)
            If key Is Nothing Then Return "e2e-missing"
            Dim box = ExchangeSealedGrants.RegisteringBox(grantId, view.OwnDeviceId, key)
            If box.Length = 0 Then Return "sealed-failed"
            Dim frame = ExchangeMessages.GrantTo(grantId, guest, If(broadcast, "", targetId), If(broadcast, targetId, ""), "", "", box)
            Dim reply = Await session.RequestAsync(frame, New String() {ExchangeEnvelopeTypes.Granted}, 4000, ct).ConfigureAwait(False)
            If reply Is Nothing Then Return "not-connected"
            If reply.Is(ExchangeEnvelopeTypes.Refused) Then Return reply.Text("reason")
            Return ""
        End Using
    End Function

    Private Async Function RefreshGuestGrantsAsync() As Task
        Await _guestGate.WaitAsync().ConfigureAwait(False)
        Try
            Dim publisher As ExchangeSealedBroadcastPublisher
            SyncLock _gate
                publisher = _liveSealedPublisher
            End SyncLock
            If publisher IsNot Nothing Then
                Await publisher.PublishAsync(AddressOf RefreshGuestGrantsCoreAsync).ConfigureAwait(False)
            Else
                Await RefreshGuestGrantsCoreAsync().ConfigureAwait(False)
            End If
        Catch ex As OperationCanceledException
        Catch ex As ObjectDisposedException
        Finally
            _guestGate.Release()
        End Try
    End Function

    Private Async Function RefreshGuestGrantsCoreAsync() As Task
        Try
            Dim session As ExchangeControlSession
            Dim ct As CancellationToken
            SyncLock _gate
                session = _session
                If session Is Nothing OrElse _cts Is Nothing Then Return
                ct = _cts.Token
            End SyncLock
            If Not ReferenceEquals(_guestSession, session) OrElse _guestSeenGeneration <> Volatile.Read(_guestGeneration) Then
                _guestStamps.Clear()
                If Not ReferenceEquals(_guestSession, session) Then _guestKeys.Clear()
                _guestSession = session
                _guestSeenGeneration = Volatile.Read(_guestGeneration)
            End If
            Dim view = session.Sealed
            If Not view.CanSealThroughServer Then Return
            Dim snapshot = GetFullDirectory()
            Dim present As New HashSet(Of String)(StringComparer.Ordinal)
            Using key = KeyStore().TryLoad(view.OwnDeviceId)
                If key Is Nothing Then Return
                For Each raw In snapshot.Grants
                    Using doc = JsonDocument.Parse(raw)
                        Dim g = doc.RootElement
                        Dim id = GrantText(g, "grantId")
                        If Not ExchangeIds.IsValidId(id) Then Continue For
                        present.Add(id)
                        Dim resourceId = GrantText(g, "resourceId")
                        Dim broadcastId = GrantText(g, "broadcastId")
                        Dim payload As String = ""
                        Dim live As ExchangeSealedBroadcastSession = Nothing
                        If resourceId.Length > 0 Then
                            If Not OutwardAccessFeatures.IsOffered() Then Continue For
                            Dim record = snapshot.Resources.FirstOrDefault(Function(r) r.ResourceId = resourceId AndAlso r.DeviceId = view.OwnDeviceId)
                            If record Is Nothing Then Continue For
                            Dim access = ExchangeSealedAccess.Resolve(record, view, TrustStore(), key)
                            If Not access.Opened Then Continue For
                            payload = ExchangeSealedGrants.ResourcePayload(access.AccessJson)
                        Else
                            Dim record = snapshot.Broadcasts.FirstOrDefault(Function(b) b.BroadcastId = broadcastId AndAlso b.DeviceId = view.OwnDeviceId)
                            live = CurrentSealed()
                            If record Is Nothing OrElse live Is Nothing OrElse live.BroadcastId <> broadcastId Then Continue For
                            Dim access = ExchangeSealedBroadcastAccess.Resolve(record, view, TrustStore(), key)
                            If Not access.Opened Then Continue For
                            payload = ExchangeSealedGrants.BroadcastPayload(access.AccessJson)
                        End If
                        Dim recipients = ExchangeSealedAccess.GuestRecipientsOf(raw)
                        ' Keep only a digest of a payload, never its plaintext, in the change detector.
                        Dim stamp = Convert.ToHexString(Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw & payload)))
                        Dim previous As String = Nothing
                        If _guestStamps.TryGetValue(id, previous) AndAlso previous = stamp Then Continue For
                        DropGuestKey(id)
                        If live IsNot Nothing AndAlso recipients.Count > 0 AndAlso ExchangeSealedGrants.HasRelayKey(payload) Then
                            Dim groupKey = live.AddLink()
                            If groupKey.Length = 0 Then Continue For
                            _guestKeys(id) = Tuple.Create(live, groupKey)
                            payload = ExchangeSealedGrants.WithRelayKey(payload, groupKey)
                        End If
                        If recipients.Count > 0 Then
                            Dim box = ExchangeSealedGrants.SealPayload(payload, id, view.OwnDeviceId, key, recipients)
                            If String.IsNullOrEmpty(box) Then Continue For
                            If Not Await session.SendAsync(ExchangeMessages.GrantTo(id, GrantText(g, "guest"), resourceId, broadcastId, "", "", box), ct).ConfigureAwait(False) Then Continue For
                        End If
                        _guestStamps(id) = stamp
                    End Using
                Next
            End Using
            For Each id In _guestKeys.Keys.Where(Function(k) Not present.Contains(k)).ToArray()
                DropGuestKey(id)
                _guestStamps.Remove(id)
            Next
        Catch ex As OperationCanceledException
        Catch ex As ObjectDisposedException
        Catch ex As JsonException
        End Try
    End Function

    Private Sub DropGuestKey(id As String)
        Dim kept As Tuple(Of ExchangeSealedBroadcastSession, String) = Nothing
        If _guestKeys.TryGetValue(id, kept) Then
            kept.Item1.WithdrawLink(kept.Item2)
            _guestKeys.Remove(id)
        End If
    End Sub

    Private Shared Function GrantText(root As JsonElement, name As String) As String
        Dim value As JsonElement
        If root.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.String Then Return value.GetString()
        Return ""
    End Function
End Class
