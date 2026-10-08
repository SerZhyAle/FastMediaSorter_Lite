#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq

''' <summary>Why a whole import was refused before anything was stored.</summary>
Friend Enum ImportRefusal
    None = 0
    ''' <summary>At least one root carries an access PIN. No PIN is implemented on this side, so the
    ''' root can neither be saved as an unlocked resource nor opened; the file is refused WHOLE
    ''' until the owner decides on a selective import (100 Q100-1, interim policy §15.10).</summary>
    PinnedRoot = 1
End Enum

''' <summary>One resource an import would create or update.</summary>
Friend NotInheritable Class ImportItem
    Friend Sub New(descriptor As ResourceDescriptor, root As FmsRootPlan, isUpdate As Boolean, pin As FmsPinDecision)
        Me.Descriptor = descriptor
        Me.Root = root
        Me.IsUpdate = isUpdate
        Me.Pin = pin
    End Sub

    ''' <summary>The descriptor as it will be stored, without a credential and without a handle for a new one.</summary>
    Friend ReadOnly Property Descriptor As ResourceDescriptor
    Friend ReadOnly Property Root As FmsRootPlan
    ''' <summary>True when it replaces a resource already in the list (same server, user and exact root).</summary>
    Friend ReadOnly Property IsUpdate As Boolean
    Friend ReadOnly Property Pin As FmsPinDecision
End Class

''' <summary>What an import would do, worked out without a connection and without writing anything -
''' the text of the confirmation, and the input of <see cref="ResourceImport.Apply"/>.</summary>
Friend NotInheritable Class ImportPreview
    Friend Sub New(plan As FmsConfigPlan, items As List(Of ImportItem), refusal As ImportRefusal)
        Me.Plan = plan
        Me.Items = items
        Me.Refusal = refusal
    End Sub

    Friend ReadOnly Property Plan As FmsConfigPlan
    Friend ReadOnly Property Items As List(Of ImportItem)
    Friend ReadOnly Property Refusal As ImportRefusal

    Friend ReadOnly Property CanApply As Boolean
        Get
            Return Refusal = ImportRefusal.None AndAlso Items.Count > 0
        End Get
    End Property

    ''' <summary>True when the file has no credential and at least one resource it creates would have none either.</summary>
    Friend ReadOnly Property NeedsPassword As Boolean
        Get
            Return Plan.NeedsPassword AndAlso Items.Any(Function(i) Not i.IsUpdate)
        End Get
    End Property

    ''' <summary>A re-import whose pin differs from the stored one: the user must say yes, with Cancel as default.</summary>
    Friend ReadOnly Property RequiresPinConfirmation As Boolean
        Get
            Return Items.Any(Function(i) i.Pin.RequiresConfirmation)
        End Get
    End Property
End Class

Friend Enum ImportApplyOutcome
    Applied = 0
    Refused = 1
    ''' <summary>A new resource needs a credential and none was given.</summary>
    SecretRequired = 2
    ''' <summary>Stopped part-way: <see cref="ImportApplyResult.Committed"/> resources are stored, the rest are not.</summary>
    Failed = 3
End Enum

Friend NotInheritable Class ImportApplyResult
    Friend Sub New(outcome As ImportApplyOutcome, committed As Integer, Optional failure As ResourceCommitOutcome = ResourceCommitOutcome.Committed,
                   Optional ids As IReadOnlyList(Of String) = Nothing)
        Me.Outcome = outcome
        Me.Committed = committed
        Me.Failure = failure
        Me.Ids = If(ids, New List(Of String)())
    End Sub

    ''' <summary>The ids of the resources that were stored, in the document's order.</summary>
    Friend ReadOnly Property Ids As IReadOnlyList(Of String)

    Friend ReadOnly Property Outcome As ImportApplyOutcome
    Friend ReadOnly Property Committed As Integer
    Friend ReadOnly Property Failure As ResourceCommitOutcome
End Class

''' <summary>
''' Turns an <see cref="FmsConfigPlan"/> - the same document the phone imports, read by the one
''' reader - into resources (007 §3.1). Pure up to the two delegates <see cref="Apply"/> takes, so the
''' whole of it is tested without a registry or a DPAPI. Nothing here connects to anything: the host
''' key arrives pinned FROM THE DOCUMENT, which is why the first connection is already verified.
''' </summary>
Friend Module ResourceImport

    ''' <param name="existing">The resources now in the list, for matching a re-import by server identity
    ''' and exact root (never by display name or a mutable address).</param>
    ''' <param name="newId">Source of ids for resources that are really new.</param>
    Friend Function Preview(plan As FmsConfigPlan, existing As IEnumerable(Of ResourceDescriptor),
                            newId As Func(Of String)) As ImportPreview
        If plan Is Nothing Then Throw New ArgumentNullException(NameOf(plan))
        Dim items As New List(Of ImportItem)()
        Dim current As List(Of ResourceDescriptor) = If(existing, Enumerable.Empty(Of ResourceDescriptor)()).ToList()
        If Not plan.CanCommitAll Then Return New ImportPreview(plan, items, ImportRefusal.PinnedRoot)

        Dim primary As FmsEndpoint = plan.Endpoints.FirstOrDefault()
        If primary Is Nothing Then Return New ImportPreview(plan, items, ImportRefusal.None)

        For Each root As FmsRootPlan In plan.Roots
            Dim matched As ResourceDescriptor = FindExisting(current, plan, root)
            Dim descriptor As ResourceDescriptor
            Dim decision As FmsPinDecision
            If matched IsNot Nothing Then
                descriptor = Clone(matched)
                decision = FmsReimport.EvaluatePin(matched.PinnedFingerprint, plan.PinnedFingerprint)
            Else
                descriptor = New ResourceDescriptor With {.Id = newId(), .Kind = ResourceKind.Sftp,
                                                          .DisplayName = NameFor(plan, root, plan.Roots.Count)}
                decision = FmsReimport.EvaluatePin("", plan.PinnedFingerprint)
            End If

            descriptor.Host = primary.Host
            descriptor.Port = primary.Port
            descriptor.UserName = plan.UserName
            descriptor.RootPath = root.VirtualPath
            descriptor.ReadOnlyHint = Not root.EffectiveWritable
            descriptor.IncludeSubfolders = root.ScanSubdirectories
            descriptor.Endpoints = plan.Endpoints.Select(Function(e) New ResourceEndpoint With {.Kind = e.Kind, .Host = e.Host, .Port = e.Port}).ToList()
            ' Without an explicit yes a changed pin is never taken, and absence never clears one.
            descriptor.PinnedFingerprint = decision.PinToPersist(confirmed:=False)
            ' Ticket 171 C4: a document that carries an exchange tunnel makes the resource a LINK (no account; the share id opens it). An
            ' attached resource of the account keeps its own, better tunnel. The share id itself is staged as a DPAPI blob by Apply.
            If plan.Tunnel IsNot Nothing AndAlso Not ResourceOrigin.IsExchange(descriptor) Then
                descriptor.Origin = ResourceOrigin.Link
                descriptor.TunnelHost = plan.Tunnel.Host
                descriptor.TunnelPort = plan.Tunnel.Port
            End If
            items.Add(New ImportItem(descriptor, root, matched IsNot Nothing, decision))
        Next
        Return New ImportPreview(plan, items, ImportRefusal.None)
    End Function

    ''' <summary>
    ''' Stores the previewed resources, one registry commit each (the credential first, the registry
    ''' reference last - <see cref="ResourceCommit"/>). Nothing is stored for a refused preview.
    ''' </summary>
    ''' <param name="password">The credential for a document that carries none; Nothing keeps the stored
    ''' one for an update. Never logged.</param>
    ''' <param name="confirmedPinChange">The user's explicit yes to a changed fingerprint.</param>
    Friend Function Apply(preview As ImportPreview, password As String, confirmedPinChange As Boolean,
                          readValue As Func(Of String, String), writeValue As Action(Of String, String),
                          secrets As IResourceSecretStore,
                          Optional protect As Func(Of String, SecretProtectionResult) = Nothing) As ImportApplyResult
        If preview Is Nothing OrElse Not preview.CanApply Then Return New ImportApplyResult(ImportApplyOutcome.Refused, 0)
        ' The document's own credential wins; otherwise what the user typed for this import.
        Dim secret As String = If(Not preview.Plan.NeedsPassword, preview.Plan.Password, password)
        If preview.NeedsPassword AndAlso String.IsNullOrEmpty(secret) Then Return New ImportApplyResult(ImportApplyOutcome.SecretRequired, 0)

        Dim committed As Integer
        Dim ids As New List(Of String)()
        For Each item As ImportItem In preview.Items
            Dim descriptor As ResourceDescriptor = item.Descriptor
            descriptor.PinnedFingerprint = item.Pin.PinToPersist(confirmedPinChange)
            Dim plain As String = If(String.IsNullOrEmpty(secret), Nothing, secret)

            ' the link's share id is a second secret of the resource: staged under its own handle BEFORE the registry reference is
            ' committed, and cleaned up again if the commit does not happen (the order of ResourceCommit, held for both secrets)
            Dim previousTunnelHandle As String = descriptor.TunnelSecretHandle
            Dim stagedTunnelHandle As String = ""
            If preview.Plan.Tunnel IsNot Nothing AndAlso ResourceOrigin.IsLink(descriptor) Then
                Dim protector As Func(Of String, SecretProtectionResult) = If(protect, Function(s As String) DpapiSecretChecks.ProtectChecked(s))
                Dim sealed As SecretProtectionResult = protector(preview.Plan.Tunnel.ShareId)
                If Not sealed.Succeeded Then Return New ImportApplyResult(ImportApplyOutcome.Failed, committed, ResourceCommitOutcome.ProtectionFailed, ids)
                stagedTunnelHandle = "cred:" & Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
                Try
                    secrets.PutBlob(stagedTunnelHandle, sealed.Value)
                Catch ex As Exception
                    Return New ImportApplyResult(ImportApplyOutcome.Failed, committed, ResourceCommitOutcome.StageFailed, ids)
                End Try
                descriptor.TunnelSecretHandle = stagedTunnelHandle
            End If

            Dim result As ResourceCommitResult = ResourceCommit.Commit(descriptor, plain, readValue, writeValue, secrets, protect)
            If Not result.Succeeded Then
                If stagedTunnelHandle.Length > 0 Then
                    Try : secrets.DeleteBlob(stagedTunnelHandle) : Catch : End Try
                End If
                Return New ImportApplyResult(ImportApplyOutcome.Failed, committed, result.Outcome, ids)
            End If
            If stagedTunnelHandle.Length > 0 AndAlso Not String.IsNullOrEmpty(previousTunnelHandle) AndAlso
               Not String.Equals(previousTunnelHandle, stagedTunnelHandle, StringComparison.Ordinal) Then
                Try : secrets.DeleteBlob(previousTunnelHandle) : Catch : End Try   ' the superseded share id goes only after the registry holds the new reference
            End If
            committed += 1
            ids.Add(descriptor.Id)
        Next
        Return New ImportApplyResult(ImportApplyOutcome.Applied, committed, ResourceCommitOutcome.Committed, ids)
    End Function

    ''' <summary>The same server, user and exact root as an entry of the list - the identity a re-import
    ''' updates. Every address of the document is tried, because the producer's LAN address goes stale
    ''' while the server it names does not.</summary>
    Private Function FindExisting(existing As List(Of ResourceDescriptor), plan As FmsConfigPlan, root As FmsRootPlan) As ResourceDescriptor
        For Each endpoint As FmsEndpoint In plan.Endpoints
            Dim found As ResourceDescriptor = ResourceMatching.FindMatchingDescriptor(existing, endpoint.Host, endpoint.Port, plan.UserName, root.VirtualPath)
            If found IsNot Nothing Then Return found
        Next
        ' A stored resource knows its other addresses too: the document's primary may be a stored alternate.
        Dim canonical As String = FmsConfigReader.CanonicalVirtualPath(root.VirtualPath)
        For Each d As ResourceDescriptor In existing
            If d.Kind <> ResourceKind.Sftp OrElse d.Endpoints Is Nothing Then Continue For
            If Not String.Equals(d.UserName, plan.UserName, StringComparison.Ordinal) Then Continue For
            If Not String.Equals(FmsConfigReader.CanonicalVirtualPath(d.RootPath), canonical, StringComparison.Ordinal) Then Continue For
            For Each stored As ResourceEndpoint In d.Endpoints
                For Each endpoint As FmsEndpoint In plan.Endpoints
                    If String.Equals(stored.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase) AndAlso stored.Port = endpoint.Port Then Return d
                Next
            Next
        Next
        Return Nothing
    End Function

    Private Function NameFor(plan As FmsConfigPlan, root As FmsRootPlan, rootCount As Integer) As String
        Dim baseName As String = If(String.IsNullOrWhiteSpace(plan.ResourceName), plan.Endpoints.First().Host, plan.ResourceName.Trim())
        Dim label As String = If(root.Label, "").Trim()
        If label.Length = 0 AndAlso rootCount > 1 Then
            Dim slash As Integer = root.VirtualPath.TrimEnd("/"c).LastIndexOf("/"c)
            label = If(slash >= 0, root.VirtualPath.TrimEnd("/"c).Substring(slash + 1), "")
        End If
        If label.Length = 0 OrElse String.Equals(label, baseName, StringComparison.OrdinalIgnoreCase) Then Return baseName
        Return baseName & " - " & label
    End Function

    ''' <summary>A copy that keeps every field, including the ones this version does not know (extension data),
    ''' so a re-import never erases what a newer build stored.</summary>
    Private Function Clone(source As ResourceDescriptor) As ResourceDescriptor
        Dim json As String = System.Text.Json.JsonSerializer.Serialize(source)
        Return System.Text.Json.JsonSerializer.Deserialize(Of ResourceDescriptor)(json)
    End Function

    ''' <summary>The next free id, short enough to read in a log and unique within the list.</summary>
    Friend Function NewResourceId() As String
        Return "res:" & Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
    End Function

End Module
#End If
