Option Strict On

Imports System.Linq

' Ticket 170 rules 1-5 and 8, the PURE half: given what the worker reports, the user's two choices, the server's public endpoint and
' the kept ids, which resources does this device publish, and if none, why not. No I/O, no clock, no text - the publisher applies the
' plan, the tests pin it, and the window turns a block into one sentence.
'
'   - Publication is a choice, off by default (rule 1).
'   - A resource per share (root): `sftp-share`, the share's name, a resource id generated on first publication and kept (rule 2).
'   - `access` is an FMSSFTP1 code, or FMSSFTP2 when an outsider link exists, built by the shared FmsSftpCode; the hosts are the same
'     selection ShareConfigBuilder makes today (rule 3). The descriptor carries one port, so an external address whose port differs
'     from the listen port cannot be expressed and is left out rather than dialled on the wrong port.
'   - A PIN-locked root stops publication altogether (rule 4): the record has no member for a lock, a consumer that ignored it would
'     open the root unlocked, and the credential opens the whole server regardless (Q170-3).
'   - With publication off, or no usable server, nothing is desired: the publisher unpublishes what it published, and a stopped share
'     reads on the other devices as "no longer published" (rule 5).

''' <summary>Why nothing is published (or None when something is).</summary>
Friend Enum ExchangePublishBlock
    ''' <summary>Publishing: <see cref="ExchangePublishPlan.Shares"/> is what is wanted on the server.</summary>
    None = 0
    ''' <summary>"Publish my shares" is off (the default).</summary>
    Off = 1
    ''' <summary>No worker, not serving, or a status that has no port, user, password or host-key fingerprint to publish.</summary>
    ServerDown = 2
    ''' <summary>A root has an access PIN (rule 4).</summary>
    PinLocked = 3
    ''' <summary>The worker reports no address a descriptor can carry.</summary>
    NoAddress = 4
    ''' <summary>The server serves, and nothing is shared.</summary>
    NoShares = 5
    ''' <summary>Spec 234 item DP: the connected exchange server does not list `sealed`, so no folder goes through it - not even without
    ''' `access` (state `server-no-e2e`). The folder keeps serving on the person's own network.</summary>
    ServerNoE2e = 6
    ''' <summary>Spec 234 items DF, DP: this device holds no key of its own (or is not connected yet), so it cannot seal a folder
    ''' (state `e2e-missing`).</summary>
    NoOwnKey = 7
End Enum

Friend NotInheritable Class ExchangePlannedShare

    Public ReadOnly Name As String
    Public ReadOnly ResourceId As String
    ''' <summary>The outsider share id the record carries ("" when no link exists, or the server endpoint is not usable).</summary>
    Public ReadOnly ShareId As String
    ''' <summary>The `access` member as JSON (a secret: never logged).</summary>
    Public ReadOnly AccessJson As String
    ''' <summary>The third-party port REQUEST for this resource: Nothing = none, 0 = any, N = that one.</summary>
    Public ReadOnly PublicPortRequest As Integer?
    Public ReadOnly PinProtected As Boolean

    Public Sub New(name As String, resourceId As String, shareId As String, accessJson As String, publicPortRequest As Integer?, Optional pinProtected As Boolean = False)
        Me.Name = name
        Me.ResourceId = resourceId
        Me.ShareId = If(shareId, "")
        Me.AccessJson = accessJson
        Me.PublicPortRequest = publicPortRequest
        Me.PinProtected = pinProtected
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangePlannedShare"
    End Function

End Class

Friend NotInheritable Class ExchangePublishInputs

    Public Property Publish As Boolean
    Public Property ThirdPartyPort As Boolean
    Public Property Status As WorkerStatus
    ''' <summary>This device's id (the record's `deviceId`).</summary>
    Public Property DeviceId As String = ""
    ''' <summary>`welcome.publicEndpoint` of the current connection ("" when not connected).</summary>
    Public Property PublicEndpoint As String = ""
    ''' <summary>False under the server-wide "LAN only" setting: no address reachable from outside travels in a descriptor.</summary>
    Public Property IncludeExternal As Boolean = True
    ''' <summary>The address ShareConfigBuilder falls back to when the worker reports none (NetworkInfo.LocalIPv4()).</summary>
    Public Property LanFallback As String = ""
    ''' <summary>Whether the root at this host path has an access PIN.</summary>
    Public Property HasPin As Func(Of String, Boolean) = Function(hostPath) False
    Public Property State As ExchangeProducerState = ExchangeProducerState.Empty
    Public Property NewId As Func(Of String) = AddressOf ExchangeIds.NewId
    ''' <summary>Spec 234 item DP: False when the connected server does not list `sealed`. Then nothing is published through it - not even
    ''' without `access` (state `server-no-e2e`). True while the server's features are not known yet.</summary>
    Public Property ServerSealed As Boolean = True
    ''' <summary>Spec 234 items DF, DG: False when this device cannot seal a folder - no key of its own, or its own device is not a ready
    ''' recipient in the directory (yet) - so a `sealedAccess` cannot be made (state `e2e-missing`).</summary>
    Public Property OwnKeyReady As Boolean = True

End Class

Friend NotInheritable Class ExchangePublishPlan

    Public ReadOnly Block As ExchangePublishBlock
    Public ReadOnly Shares As IReadOnlyList(Of ExchangePlannedShare)
    ''' <summary>The kept state with the ids created for shares seen for the first time (the publisher saves it).</summary>
    Public ReadOnly State As ExchangeProducerState
    ''' <summary>The name of the share the third-party port is bound to ("" when none is asked for).</summary>
    Public ReadOnly CarrierName As String

    Public Sub New(block As ExchangePublishBlock, shares As IReadOnlyList(Of ExchangePlannedShare), state As ExchangeProducerState, carrierName As String)
        Me.Block = block
        Me.Shares = shares
        Me.State = state
        Me.CarrierName = If(carrierName, "")
    End Sub

    Public Function Find(name As String) As ExchangePlannedShare
        For Each s As ExchangePlannedShare In Shares
            If String.Equals(s.Name, name, StringComparison.Ordinal) Then Return s
        Next
        Return Nothing
    End Function

    ' --- the hosts ---------------------------------------------------------------------------------------------------------

    ''' <summary>The hosts a descriptor carries, in the order of the .fmscfg access paths: the LAN address, then IPv6, then the external
    ''' address - the same selection <see cref="ShareConfigBuilder.Build"/> makes. IPv6 and the external address only when the export
    ''' may leave the LAN (<paramref name="includeExternal"/>); the external one only when it is not behind CGNAT and answers on the
    ''' LISTEN port (a descriptor has one port, so a forward to another number cannot be written).</summary>
    Public Shared Function SelectHosts(status As WorkerStatus, includeExternal As Boolean, lanFallback As String) As List(Of String)
        Dim hosts As New List(Of String)
        If status Is Nothing Then Return hosts
        Dim reach As WorkerReachability = status.Reachability
        Dim lan As String = If(reach IsNot Nothing AndAlso Not String.IsNullOrEmpty(reach.LanAddress), reach.LanAddress, If(lanFallback, ""))
        If lan.Length > 0 Then hosts.Add(lan)
        If includeExternal AndAlso reach IsNot Nothing Then
            If Not String.IsNullOrEmpty(reach.Ipv6Address) AndAlso Not hosts.Contains(reach.Ipv6Address) Then hosts.Add(reach.Ipv6Address)
            If Not reach.IsCgnat AndAlso Not String.IsNullOrEmpty(reach.ExternalHost) AndAlso
               (reach.ExternalPort <= 0 OrElse reach.ExternalPort = status.ListenPort) AndAlso Not hosts.Contains(reach.ExternalHost) Then
                hosts.Add(reach.ExternalHost)
            End If
        End If
        Return hosts
    End Function

    ' --- the plan ----------------------------------------------------------------------------------------------------------

    Public Shared Function Build(inputs As ExchangePublishInputs) As ExchangePublishPlan
        Dim none As New List(Of ExchangePlannedShare)
        Dim state As ExchangeProducerState = If(inputs.State, ExchangeProducerState.Empty)
        If Not inputs.Publish Then Return New ExchangePublishPlan(ExchangePublishBlock.Off, none, state, "")

        Dim st As WorkerStatus = inputs.Status
        If st Is Nothing OrElse Not st.Running OrElse st.ListenPort < 1 OrElse st.ListenPort > 65535 OrElse
           String.IsNullOrEmpty(st.Username) OrElse String.IsNullOrEmpty(st.Password) OrElse Not ExchangeTrust.IsFingerprint(st.Fingerprint) Then
            Return New ExchangePublishPlan(ExchangePublishBlock.ServerDown, none, state, "")
        End If

        ' The roots, one per NAME (the worker's own key); an empty name is not a share.
        Dim roots As New List(Of ShareFolder)
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        If st.Roots IsNot Nothing Then
            For Each r As ShareFolder In st.Roots
                If r Is Nothing OrElse String.IsNullOrEmpty(r.name) OrElse Not seen.Add(r.name) Then Continue For
                roots.Add(r)
            Next
        End If
        If roots.Count = 0 Then Return New ExchangePublishPlan(ExchangePublishBlock.NoShares, none, state, "")

        ' item DP: a folder goes through the server sealed or not at all - there is no clear fallback. The folder keeps serving on the
        ' person's own network (the .fmscfg and the QR are not touched by this).
        If Not inputs.ServerSealed Then Return New ExchangePublishPlan(ExchangePublishBlock.ServerNoE2e, none, state, "")
        If Not inputs.OwnKeyReady Then Return New ExchangePublishPlan(ExchangePublishBlock.NoOwnKey, none, state, "")

        For Each r As ShareFolder In roots
            If (inputs.HasPin(If(r.hostPath, "")) OrElse r.pinProtected) AndAlso
                (Not st.ResourcePinSupported OrElse Not r.pinProtected) Then Return New ExchangePublishPlan(ExchangePublishBlock.PinLocked, none, state, "")
        Next

        Dim hosts As List(Of String) = SelectHosts(st, inputs.IncludeExternal, inputs.LanFallback)
        If hosts.Count = 0 Then Return New ExchangePublishPlan(ExchangePublishBlock.NoAddress, none, state, "")

        ' the server channel of a descriptor needs a usable endpoint: a loopback or malformed one is never written into a code
        Dim exchangeEndpoint As String = ""
        Dim ep As ExchangeEndpoint = Nothing
        If ExchangeEndpointRules.TryParse(inputs.PublicEndpoint, ep) AndAlso Not ExchangeEndpointRules.IsLoopbackOrUnspecified(ep.Host) Then
            exchangeEndpoint = ep.ToString()
        End If

        ' the third-party port rides on ONE resource: every root of this server is reachable through any of them (one worker, one login)
        Dim carrier As String = ""
        If inputs.ThirdPartyPort Then
            carrier = If(roots.Any(Function(r) String.Equals(r.name, state.CarrierName, StringComparison.Ordinal)), state.CarrierName, roots(0).name)
        End If

        Dim planned As New List(Of ExchangePlannedShare)
        Dim current As ExchangeProducerState = state
        For Each r As ShareFolder In roots
            Dim entry As ExchangeProducerEntry = current.Get(r.name)
            If entry Is Nothing Then
                entry = New ExchangeProducerEntry(inputs.NewId.Invoke(), "")
                current = current.WithEntry(r.name, entry)
            End If
            If r.pinProtected AndAlso entry.ShareId.Length > 0 Then
                entry = entry.WithShareId("")
                current = current.WithEntry(r.name, entry)
            End If
            Dim shareId As String = If(exchangeEndpoint.Length > 0 AndAlso entry.ShareId.Length > 0, entry.ShareId, "")
            Dim code As New FmsSftpCode(hosts, st.ListenPort, st.Username, st.Password, st.Fingerprint,
                                        If(shareId.Length > 0, exchangeEndpoint, ""), shareId, False)
            Dim access As String = Nothing
            If Not ExchangeResourceAccess.TryBuild(code, ExchangeResourceAccess.FallbackRoot(r.name), access) Then
                Return New ExchangePublishPlan(ExchangePublishBlock.ServerDown, none, state, "")
            End If
            Dim request As Integer? = Nothing
            If String.Equals(r.name, carrier, StringComparison.Ordinal) Then request = If(state.CarrierPort > 0, state.CarrierPort, 0)
            planned.Add(New ExchangePlannedShare(r.name, entry.ResourceId, shareId, access, request, r.pinProtected))
        Next
        Return New ExchangePublishPlan(ExchangePublishBlock.None, planned, current, carrier)
    End Function

    Public Overrides Function ToString() As String
        Return "ExchangePublishPlan " & Block.ToString()
    End Function

End Class
