Option Strict On

Imports System.Collections.Generic
Imports System.Linq

' Specification 203 section 5.11, item 3 of 5.9: the directory the viewer reads is the exchange server's directory plus what the
' account's other devices list on Google Drive. One device, one id (owner decision 3 of 5.10), so a device or a folder seen on both
' channels is ONE row; a row on Drive alone is a row the server does not know.
'
'   - Same id on both: the server's record wins - it carries the capabilities, the sealed box and a presence the server stamps - unless
'     the server says the row is not online and Drive says it is (the server is down, or that PC left it, and the Drive record is
'     alive): then Drive's record stands in, so the person still sees the PC and its folders.
'   - A Drive resource has no `access` (item X), so such a row is listed and cannot be attached; the local channel answers "no access"
'     for it, never a made-up address.
'   - Drive broadcast announcements are NOT merged: nothing in this build can open a broadcast from Drive (the consumer is a later
'     slice), and a row the viewer cannot open is worse than no row.
'   - The revision is the sum of the two sources' revisions. Both only grow while their source lives, so a change on either side moves
'     the sum and a viewer holding the old number is answered; the number means "something moved" and nothing else.

Friend NotInheritable Class DriveDirectoryMerge

    Private Sub New()
    End Sub

    Public Shared Function Union(server As ExchangeDirectorySnapshot, drive As ExchangeDirectorySnapshot) As ExchangeDirectorySnapshot
        If server Is Nothing Then server = ExchangeDirectorySnapshot.Empty
        If drive Is Nothing OrElse (drive.Devices.Count = 0 AndAlso drive.Resources.Count = 0 AndAlso drive.Revision = 0) Then Return server

        Dim devices As New List(Of ExchangeDeviceRecord)(server.Devices)
        Dim deviceAt As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        For i As Integer = 0 To devices.Count - 1
            deviceAt(devices(i).DeviceId) = i
        Next
        For Each d As ExchangeDeviceRecord In drive.Devices
            Dim at As Integer
            If Not deviceAt.TryGetValue(d.DeviceId, at) Then
                deviceAt(d.DeviceId) = devices.Count
                devices.Add(d)
            ElseIf Not devices(at).IsOnline AndAlso d.IsOnline Then
                devices(at) = d
            End If
        Next

        Dim resources As New List(Of ExchangeResourceRecord)(server.Resources)
        Dim resourceAt As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        For i As Integer = 0 To resources.Count - 1
            resourceAt(resources(i).ResourceId) = i
        Next
        For Each r As ExchangeResourceRecord In drive.Resources
            Dim at As Integer
            If Not resourceAt.TryGetValue(r.ResourceId, at) Then
                resourceAt(r.ResourceId) = resources.Count
                resources.Add(r)
            ElseIf Not String.Equals(resources(at).Presence, ExchangeWire.PresenceOnline, StringComparison.Ordinal) AndAlso
                   String.Equals(r.Presence, ExchangeWire.PresenceOnline, StringComparison.Ordinal) Then
                resources(at) = r
            End If
        Next

        Return New ExchangeDirectorySnapshot(server.Revision + drive.Revision, devices, resources, server.Broadcasts, server.Grants)
    End Function

End Class
