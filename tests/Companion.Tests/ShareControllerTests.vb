Option Strict On

Imports System.Collections.Generic
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The resume reconcile's read-only rule and the LAN-only split (tickets 054 H-18, H-06).
'''
''' ReconciledReadOnly: the worker's own read-only may only ever be TIGHTENED by a local
''' hard read-only - never loosened. The local params are this account's registry, and they
''' can be missing or unreadable (a second enrolled account on a Server machine, a reset
''' profile, a blob that failed to parse), and "missing" reads as the writable default;
''' loosening from that silently re-served every read-only folder as WRITABLE on the
''' machine-wide server.
'''
''' The LAN-only split: <c>Share_LanOnlyExport</c> shapes one export, <c>Share_LanOnly</c>
''' is the server-wide enforcement <c>SetNetworkPolicy</c> carries. They were one flag
''' until H-06 split them, and the per-code checkbox used to reach the worker as policy.
''' </summary>
Public Class ShareControllerTests

    ' --- ReconciledReadOnly (H-18) --------------------------------------------

    <Fact>
    Public Sub Reconcile_NeverLoosensAReadOnlyRoot()
        ' Worker read-only + a local entry that says writable: the worker wins.
        Assert.True(ShareController.ReconciledReadOnly(True, New ShareRootParams With {.IsReadOnly = False}))
        ' Worker read-only + no local entry at all (the unreadable-blob case): still read-only.
        Assert.True(ShareController.ReconciledReadOnly(True, Nothing))
    End Sub

    <Fact>
    Public Sub Reconcile_TightensAWritableRootOnALocalHardReadOnly()
        Assert.True(ShareController.ReconciledReadOnly(False, New ShareRootParams With {.IsReadOnly = True}))
        ' The soft hint is not the server's restriction - it must not tighten the server.
        Assert.False(ShareController.ReconciledReadOnly(False, New ShareRootParams With {.SoftReadOnly = True}))
    End Sub

    <Fact>
    Public Sub Reconcile_LeavesAWritableRootWritableWhenNothingSaysOtherwise()
        ' The empty-params-store case: "missing" reads as the writable default and must
        ' not be turned into either direction.
        Assert.False(ShareController.ReconciledReadOnly(False, Nothing))
        Assert.False(ShareController.ReconciledReadOnly(False, New ShareRootParams With {.IsReadOnly = False}))
    End Sub

    ' --- the LAN-only split (H-06) ---------------------------------------------

    <Fact>
    Public Sub NetworkPolicyRequest_CarriesTheEnforcingFlagAsGiven()
        Dim req As WorkerRequest = ShareController.NetworkPolicyRequest(10, True, 2222)
        Assert.Equal("SetNetworkPolicy", req.type)
        Assert.True(req.lanOnly)
        Assert.Equal(2222, req.port)

        req = ShareController.NetworkPolicyRequest(10, False, ShareSettings.UnsetPort)
        Assert.False(req.lanOnly)
        ' WORKER-IPC rule 4: an unset knob is omitted, never sent as 0.
        Assert.Null(req.port)
    End Sub

    ''' <summary>The enforcement verdict behind the persistent warning: a worker that
    ''' answered and did not accept the policy leaves LAN only unenforced; a push nobody
    ''' answered teaches nothing.</summary>
    <Fact>
    Public Sub LanOnlyNotEnforced_OnlyWhenTheWorkerRefusedWhileItIsOn()
        Dim ok As New WorkerResponse With {.Ok = True}
        Dim refused As New WorkerResponse With {.Ok = False}
        Assert.False(ShareController.LanOnlyNotEnforcedAfter(ok, True, False))
        Assert.True(ShareController.LanOnlyNotEnforcedAfter(refused, True, False))
        Assert.False(ShareController.LanOnlyNotEnforcedAfter(refused, False, True))
        Assert.True(ShareController.LanOnlyNotEnforcedAfter(Nothing, True, True))
    End Sub

End Class
