Option Strict On

Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The StartupApproved record layout, the one part of logon autostart that is our
''' reading of someone else's undocumented format.
'''
''' Why it matters: Task Manager's "Startup apps" page and Settings -> Apps -> Startup
''' do NOT remove the HKCU Run value when an item is switched off - they leave it exactly
''' as written and record the veto in a parallel StartupApproved key. Code that reads
''' only the Run value therefore reports an autostart that never runs, which is how a
''' ticked checkbox, a correct Run value and no tray icon at logon coexisted in the field.
''' </summary>
Public Class AutostartApprovalTests

    <Fact>
    Public Sub NoRecordIsNotAVeto()
        ' The shell writes here only once an item has been switched off or back on, so
        ' the absence of a record is the untouched state - allowed, not blocked.
        Assert.False(AutostartManager.IsShellVetoRecord(Nothing))
        Assert.False(AutostartManager.IsShellVetoRecord(New Byte() {}))
    End Sub

    <Fact>
    Public Sub EnabledRecordIsNotAVeto()
        ' What Task Manager writes for an enabled item: state 2, zeroed change time.
        Assert.False(AutostartManager.IsShellVetoRecord(
            New Byte() {2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}))
    End Sub

    <Fact>
    Public Sub DisabledRecordIsAVeto()
        ' The real record read off the machine this bug was found on: state 3 followed by
        ' the FILETIME of the moment the user switched the item off in Task Manager.
        Assert.True(AutostartManager.IsShellVetoRecord(
            New Byte() {3, 0, 0, 0, &H9E, &HF7, &H75, &H45, &H54, &H1D, &HDD, 1}))
    End Sub

    <Fact>
    Public Sub StateIsTheLowBitOfTheFirstByte()
        ' Windows has used several state values over the years (0/2/6 allowed,
        ' 1/3/5/7 blocked) and the low bit is what separates them - including the 01
        ' variant that Settings -> Apps -> Startup writes, which a plain "= 3" test
        ' would have read as "enabled" and left the bug in place.
        For state As Byte = 0 To 7
            Dim record = New Byte() {state, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
            Assert.Equal((state And 1) <> 0, AutostartManager.IsShellVetoRecord(record))
        Next
    End Sub

    <Fact>
    Public Sub AllowRecordClearsTheVeto()
        ' Round trip: what we write to lift a veto must itself read back as "allowed",
        ' or enabling autostart would leave the entry blocked.
        Assert.False(AutostartManager.IsShellVetoRecord(AutostartManager.ShellAllowRecord()))
        Assert.Equal(12, AutostartManager.ShellAllowRecord().Length)
    End Sub

    <Fact>
    Public Sub TruncatedRecordIsReadNotThrown()
        ' A one-byte record is malformed, but a startup checkbox must never crash on it.
        Assert.True(AutostartManager.IsShellVetoRecord(New Byte() {3}))
        Assert.False(AutostartManager.IsShellVetoRecord(New Byte() {2}))
    End Sub

End Class
