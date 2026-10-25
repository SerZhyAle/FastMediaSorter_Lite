Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json.Nodes

' Specification 203 section 5.11: the PURE half of what this PC writes into the Drive appDataFolder - the device record and one
' resource record per shared folder - and the plan that says which folders those are. No I/O, no clock of its own, no text: the
' publisher applies the plan, the tests pin it.
'
'   - A Drive resource record carries NO `access` (DEVICE-EXCHANGE item X: `access` reaches Drive only by the person's explicit
'     opt-in, off by default, worded with Google's name; this version has no such opt-in, so it never writes one). The record lists
'     the folder - name, ids, the host key fingerprint and the root, which are the resource's identity (item BA) and not secrets.
'     A reader lists it and says it cannot attach it (item X); the folder is reached through the packet queue (item CO).
'   - The resource ids are the ones the exchange publisher keeps (`ExchangeProducerStore`), so a PC on both channels presents ONE
'     resource whichever channel a reader sees it on (item BC). A PC without an exchange-server account creates the ids itself;
'     with one, the server publisher is the only writer of that file and this plan only reads (no lost update between the two).
'   - A folder whose root is locked by a PIN is listed only when the worker can carry the lock (`protection`, schemaVersion 2);
'     otherwise it is left out (the server plan stops the whole publication there; a Drive record exposes nothing, so only that
'     folder is left out).

''' <summary>One folder this PC lists on Drive.</summary>
Friend NotInheritable Class DrivePlannedResource

    Public ReadOnly Name As String
    Public ReadOnly ResourceId As String
    ''' <summary>The record body (compact JSON, no `access`).</summary>
    Public ReadOnly Json As String
    ''' <summary>What a rewrite would change: the id, the name, the fingerprint, the lock. Equal signatures need no new write.</summary>
    Public ReadOnly Signature As String

    Public Sub New(name As String, resourceId As String, json As String, signature As String)
        Me.Name = name
        Me.ResourceId = resourceId
        Me.Json = json
        Me.Signature = signature
    End Sub

    Public Overrides Function ToString() As String
        Return "DrivePlannedResource"
    End Function

End Class

Friend NotInheritable Class DriveSharePlan

    Public ReadOnly Resources As IReadOnlyList(Of DrivePlannedResource)
    ''' <summary>The kept ids with the ones created for folders seen for the first time (only when the plan may create them).</summary>
    Public ReadOnly State As ExchangeProducerState
    ''' <summary>True when <see cref="State"/> holds an id the store does not have yet: the publisher saves it before writing.</summary>
    Public ReadOnly StateChanged As Boolean
    ''' <summary>Folders left out because their id does not exist yet and this PC may not create it (the server publisher will).</summary>
    Public ReadOnly WaitingForIds As Integer

    Public Shared ReadOnly Empty As New DriveSharePlan(New List(Of DrivePlannedResource), ExchangeProducerState.Empty, False, 0)

    Public Sub New(resources As IReadOnlyList(Of DrivePlannedResource), state As ExchangeProducerState, stateChanged As Boolean, waitingForIds As Integer)
        Me.Resources = resources
        Me.State = state
        Me.StateChanged = stateChanged
        Me.WaitingForIds = waitingForIds
    End Sub

    Public Overrides Function ToString() As String
        Return "DriveSharePlan"
    End Function

End Class

Friend NotInheritable Class DriveShareRecords

    Private Sub New()
    End Sub

    ''' <summary>The device record (6.1): who this PC is and what it can do. `presence` and `lastSeen` are the reader's to derive from
    ''' Drive's clock, so they are not written. Nothing when an id is out of its form.</summary>
    Public Shared Function BuildDevice(deviceId As String, deviceName As String, productVersion As String, platform As String,
                                       roles As IEnumerable(Of String)) As String
        If Not ExchangeIds.IsValidId(deviceId) Then Return ""
        Dim name As String = ExchangeDeviceDescription.Clip(deviceName, 64)
        If name.Length = 0 Then name = "Windows PC"
        Dim o As New JsonObject()
        o("deviceId") = deviceId
        o("deviceName") = name
        o("product") = ExchangeWire.ProductWindows
        o("productVersion") = ExchangeDeviceDescription.Clip(productVersion, 64)
        o("platform") = ExchangeDeviceDescription.Clip(platform, 32)
        Dim list As New JsonArray()
        For Each role As String In If(roles, Array.Empty(Of String)())
            If Not String.IsNullOrEmpty(role) Then list.Add(role)
        Next
        o("roles") = list
        Return o.ToJsonString()
    End Function

    ''' <summary>The folders to list: one per root of the worker that is running, with the ids kept for this device.</summary>
    ''' <param name="status">The worker's status; Nothing or not running lists nothing.</param>
    ''' <param name="state">What the producer store holds for this device id.</param>
    ''' <param name="mayCreateIds">True on a PC without an exchange-server account: the plan then creates the id of a folder it sees
    ''' for the first time. False: it only reads, and such a folder waits for the server publisher.</param>
    Public Shared Function Plan(status As WorkerStatus, state As ExchangeProducerState, deviceId As String, mayCreateIds As Boolean,
                                newId As Func(Of String), hasPin As Func(Of String, Boolean), nowUtc As DateTime) As DriveSharePlan
        Dim kept As ExchangeProducerState = If(state, ExchangeProducerState.Empty)
        If Not ExchangeIds.IsValidId(deviceId) OrElse status Is Nothing OrElse Not status.Running OrElse status.Roots Is Nothing Then
            Return New DriveSharePlan(New List(Of DrivePlannedResource), kept, False, 0)
        End If

        Dim fingerprint As String = If(ExchangeTrust.IsFingerprint(status.Fingerprint), status.Fingerprint, "")
        Dim current As ExchangeProducerState = kept
        Dim changed As Boolean = False
        Dim waiting As Integer = 0
        Dim planned As New List(Of DrivePlannedResource)
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        For Each root As ShareFolder In status.Roots
            If root Is Nothing OrElse String.IsNullOrEmpty(root.name) OrElse Not seen.Add(root.name) Then Continue For

            ' a PIN-locked root: listed only when the lock can be declared in the record
            Dim locked As Boolean = (If(hasPin Is Nothing, False, hasPin.Invoke(If(root.hostPath, ""))) OrElse root.pinProtected)
            If locked AndAlso (Not status.ResourcePinSupported OrElse Not root.pinProtected) Then Continue For

            Dim entry As ExchangeProducerEntry = current.Get(root.name)
            If entry Is Nothing Then
                If Not mayCreateIds Then
                    waiting += 1
                    Continue For
                End If
                entry = New ExchangeProducerEntry(If(newId Is Nothing, ExchangeIds.NewId(), newId.Invoke()), "")
                current = current.WithEntry(root.name, entry)
                changed = True
            End If

            Dim json As String = Nothing
            ' no `access`, no shareId, no port request: the record is a listing (item X)
            If Not ExchangeResourceRecordBuilder.TryBuild(entry.ResourceId, deviceId, root.name, "", "", Nothing, nowUtc, json, root.pinProtected,
                                                          "", fingerprint, ExchangeResourceAccess.FallbackRoot(root.name)) Then Continue For
            planned.Add(New DrivePlannedResource(root.name, entry.ResourceId, json, SignatureOf(entry.ResourceId, root.name, fingerprint, root.pinProtected)))
        Next
        Return New DriveSharePlan(planned, current, changed, waiting)
    End Function

    Private Shared Function SignatureOf(resourceId As String, name As String, fingerprint As String, pinProtected As Boolean) As String
        Return resourceId & "|" & name & "|" & fingerprint & "|" & If(pinProtected, "pin", "")
    End Function

End Class
