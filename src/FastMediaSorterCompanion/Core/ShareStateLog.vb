Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text.Json

''' <summary>Host observations only. The worker owns detailed SFTP events; polling cannot reconstruct them.
''' Use an explicit field allow-list: WorkerStatus also contains the password and must never be serialized.</summary>
Friend NotInheritable Class ShareStateLog
    Private ReadOnly gate As New Object()
    Private ReadOnly write As Action(Of String)
    Private ReadOnly previous As New Dictionary(Of String, String)(StringComparer.Ordinal)

    Friend Sub New(writer As Action(Of String))
        write = writer
    End Sub

    Friend Sub EventOccurred(eventName As String)
        write(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) & " [SHARE-STATE] " & eventName)
    End Sub

    Friend Sub Observe(status As WorkerStatus)
        If status Is Nothing Then Return
        SyncLock gate
            Changed("worker", JsonSerializer.Serialize(New With {
                .running = status.Running, .listenPort = status.ListenPort, .desiredPort = status.DesiredPort,
                .roots = If(status.Roots Is Nothing, 0, status.Roots.Count),
                .broadcasts = status.BroadcastCount, .broadcastEnabled = status.BroadcastEnabled}))
            If status.Reachability IsNot Nothing Then
                Dim r = status.Reachability
                Changed("reachability", JsonSerializer.Serialize(New With {
                    .lan = r.LanAddress, .mdns = r.LanMdnsActive, .ipv6 = r.Ipv6Address,
                    .portMapping = r.PortMapMethod, .externalPort = r.ExternalPort,
                    .cgnat = r.IsCgnat, .verified = r.ExternalVerified,
                    .checked = r.ExternalPortChecked, .open = r.ExternalPortOpen}))
            End If
            If status.Stats IsNot Nothing Then
                Dim s = status.Stats
                Changed("usage-counters", JsonSerializer.Serialize(New With {
                    .connections = s.TotalConnections, .connectionsSinceStart = s.ConnectionsSinceStart,
                    .files = s.FilesServedTotal, .filesSinceStart = s.FilesServedSinceStart}))
            End If
        End SyncLock
    End Sub

    Private Sub Changed(key As String, value As String)
        Dim old As String = Nothing
        If previous.TryGetValue(key, old) AndAlso String.Equals(old, value, StringComparison.Ordinal) Then Return
        previous(key) = value
        EventOccurred(key & " " & value)
    End Sub

    Friend Sub CommandCompleted(operation As String, ok As Boolean)
        ' Never emit an arbitrary request type, response, error text, folder name, PIN or configuration.
        Select Case operation
            Case "StartServer", "StopServer", "SetSharedFolders", "SetNetworkPolicy", "InvalidatePinSessions",
                 "CreateBroadcast", "RefreshBroadcast", "RevokeBroadcast", "SetBroadcastPolicy", "ResetStats"
                EventOccurred(operation & If(ok, " OK", " Failed"))
        End Select
    End Sub
End Class
