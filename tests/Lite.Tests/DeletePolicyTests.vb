#If Not NETFRAMEWORK Then
Option Strict On

Imports Xunit

' The rule matrix behind "to the Recycle Bin" versus "gone for good"
' (017_SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.1). Modern-only, exactly like the
' feature: DeletePolicy.vb is whole-file "#If Not NETFRAMEWORK", so on the net48 leg of
' this project both it and this file compile to nothing.
'
' These exist because the load-bearing fact of the whole feature - Windows keeps NO bin
' on a network share or a removable drive, and those are the two places this application
' is opened on - would otherwise be checkable only with a NAS and a USB stick in hand.
' The order tests are not decoration either: the order decides which reason the user is
' shown when two of them apply, and that is a sentence a person reads, not an internal.
Public Class DeletePolicyTests

    Private Shared Function Facts(kind As DeleteVolumeKind,
                                  Optional binDisabled As Boolean = False,
                                  Optional quotaBytes As Long = -1) As DeleteVolumeFacts
        Return New DeleteVolumeFacts With {.Kind = kind,
                                           .BinDisabled = binDisabled,
                                           .BinQuotaBytes = quotaBytes}
    End Function

    Private Shared Function Decide(facts As DeleteVolumeFacts,
                                   Optional size As Long = 1024,
                                   Optional binEnabled As Boolean = True,
                                   Optional forced As Boolean = False) As DeleteDecision
        Return DeletePolicy.Decide(facts, size, binEnabled, forced)
    End Function

    ' --- when the deletion is worth stopping for (Ф4, §3.7) --------------------
    '
    ' The middle value is the reason the setting exists: one flag could only ask about
    ' everything or about nothing, so a fast sorting run meant switching off the question
    ' that guards the one deletion nobody can take back.

    <Fact>
    Public Sub Always_asks_about_both_kinds()
        Assert.True(DeletePolicy.ShouldConfirm("always", Decide(Facts(DeleteVolumeKind.FixedDisk))))
        Assert.True(DeletePolicy.ShouldConfirm("always", Decide(Facts(DeleteVolumeKind.Network))))
    End Sub

    <Fact>
    Public Sub Never_asks_about_neither_including_a_forced_permanent_one()
        Assert.False(DeletePolicy.ShouldConfirm("never", Decide(Facts(DeleteVolumeKind.FixedDisk))))
        Assert.False(DeletePolicy.ShouldConfirm("never", Decide(Facts(DeleteVolumeKind.FixedDisk), forced:=True)))
    End Sub

    <Fact>
    Public Sub PermanentOnly_lets_a_recycled_delete_through_and_stops_the_others()
        Assert.False(DeletePolicy.ShouldConfirm("permanentOnly", Decide(Facts(DeleteVolumeKind.FixedDisk))))
        Assert.True(DeletePolicy.ShouldConfirm("permanentOnly", Decide(Facts(DeleteVolumeKind.Network))))
        Assert.True(DeletePolicy.ShouldConfirm("permanentOnly", Decide(Facts(DeleteVolumeKind.FixedDisk), forced:=True)))
    End Sub

    ''' <summary>Not knowing errs towards asking, exactly as the classifier itself does.</summary>
    <Fact>
    Public Sub An_unreadable_setting_or_a_missing_decision_still_asks()
        Assert.True(DeletePolicy.ShouldConfirm("", Decide(Facts(DeleteVolumeKind.FixedDisk))))
        Assert.True(DeletePolicy.ShouldConfirm(Nothing, Decide(Facts(DeleteVolumeKind.FixedDisk))))
        Assert.True(DeletePolicy.ShouldConfirm("permanentOnly", Nothing))
    End Sub

    ''' <summary>"Use the Recycle Bin" off is the same verdict Shift+DEL reaches, and for
    ''' the same reason - the user asked - so it must not report the volume instead.</summary>
    <Fact>
    Public Sub Switching_the_bin_off_reads_as_the_users_own_choice()
        Dim d = Decide(Facts(DeleteVolumeKind.FixedDisk), binEnabled:=False)

        Assert.Equal(DeleteOutcome.Permanent, d.Outcome)
        Assert.Equal(PermanentReason.UserAsked, d.Reason)
    End Sub

    ' --- the ordinary case -----------------------------------------------------

    <Fact>
    Public Sub FixedDisk_WithABin_Recycles()
        Dim d = Decide(Facts(DeleteVolumeKind.FixedDisk))
        Assert.Equal(DeleteOutcome.Recycle, d.Outcome)
        Assert.Equal(PermanentReason.NotPermanent, d.Reason)
        Assert.False(d.IsPermanent)
    End Sub

    ' --- the two places that have no bin at all (§0.3) --------------------------

    <Fact>
    Public Sub NetworkShare_IsPermanent_AndSaysWhy()
        Dim d = Decide(Facts(DeleteVolumeKind.Network))
        Assert.Equal(DeleteOutcome.Permanent, d.Outcome)
        Assert.Equal(PermanentReason.NoBinOnNetwork, d.Reason)
    End Sub

    <Fact>
    Public Sub RemovableDrive_IsPermanent_AndSaysWhy()
        Dim d = Decide(Facts(DeleteVolumeKind.Removable))
        Assert.Equal(DeleteOutcome.Permanent, d.Outcome)
        Assert.Equal(PermanentReason.NoBinOnRemovable, d.Reason)
    End Sub

    <Fact>
    Public Sub UnknownVolume_ErrsTowardsTheScarierText()
        ' A volume we could not classify must never be promised a bin: the honest,
        ' scarier sentence is the safe direction (§3.2's known limit).
        Dim d = Decide(Facts(DeleteVolumeKind.Unknown))
        Assert.Equal(DeleteOutcome.Permanent, d.Outcome)
        Assert.Equal(PermanentReason.VolumeUnknown, d.Reason)
    End Sub

    <Fact>
    Public Sub NoFactsAtAll_IsTreatedAsUnknown()
        Dim d = DeletePolicy.Decide(Nothing, 1024, True, False)
        Assert.Equal(DeleteOutcome.Permanent, d.Outcome)
        Assert.Equal(PermanentReason.VolumeUnknown, d.Reason)
    End Sub

    ' --- the volume's own configuration ----------------------------------------

    <Fact>
    Public Sub NukeOnDelete_IsPermanent_AndNamesTheVolume()
        Dim d = Decide(Facts(DeleteVolumeKind.FixedDisk, binDisabled:=True))
        Assert.Equal(DeleteOutcome.Permanent, d.Outcome)
        Assert.Equal(PermanentReason.BinDisabledOnVolume, d.Reason)
    End Sub

    <Fact>
    Public Sub FileLargerThanTheQuota_IsPermanent()
        ' The shell hard-deletes such a file silently, so promising the bin here would
        ' be a lie we had the facts to avoid.
        Dim d = Decide(Facts(DeleteVolumeKind.FixedDisk, quotaBytes:=1000), size:=1001)
        Assert.Equal(DeleteOutcome.Permanent, d.Outcome)
        Assert.Equal(PermanentReason.FileExceedsBinQuota, d.Reason)
    End Sub

    <Fact>
    Public Sub FileExactlyTheQuota_StillFits()
        Dim d = Decide(Facts(DeleteVolumeKind.FixedDisk, quotaBytes:=1000), size:=1000)
        Assert.Equal(DeleteOutcome.Recycle, d.Outcome)
    End Sub

    <Fact>
    Public Sub UnknownQuota_DoesNotBlockTheBin()
        ' -1 means "not established", not "zero" - reading it as a limit would send every
        ' deletion on a normal disk down the permanent path.
        Dim d = Decide(Facts(DeleteVolumeKind.FixedDisk, quotaBytes:=-1), size:=Long.MaxValue)
        Assert.Equal(DeleteOutcome.Recycle, d.Outcome)
    End Sub

    <Fact>
    Public Sub UnknownFileSize_DoesNotTripTheQuota()
        Dim d = Decide(Facts(DeleteVolumeKind.FixedDisk, quotaBytes:=1000), size:=-1)
        Assert.Equal(DeleteOutcome.Recycle, d.Outcome)
    End Sub

    ' --- the user outranks the volume ------------------------------------------

    <Fact>
    Public Sub ShiftDelete_OnAFixedDisk_IsUserAsked()
        Dim d = Decide(Facts(DeleteVolumeKind.FixedDisk), forced:=True)
        Assert.Equal(DeleteOutcome.Permanent, d.Outcome)
        Assert.Equal(PermanentReason.UserAsked, d.Reason)
    End Sub

    <Fact>
    Public Sub SettingOff_IsUserAsked_NotAVolumeProblem()
        Dim d = Decide(Facts(DeleteVolumeKind.FixedDisk), binEnabled:=False)
        Assert.Equal(PermanentReason.UserAsked, d.Reason)
    End Sub

    ' --- where two reasons apply at once: THE ORDER IS THE CONTRACT -------------

    <Fact>
    Public Sub ShiftDelete_OnAShare_SaysUserAsked_NotNoBinOnNetwork()
        ' Both are true. The user held Shift on purpose and does not need a lecture about
        ' their share - reversing these two produces a message that is true and unhelpful.
        Dim d = Decide(Facts(DeleteVolumeKind.Network), forced:=True)
        Assert.Equal(PermanentReason.UserAsked, d.Reason)
    End Sub

    <Fact>
    Public Sub DisabledBin_OnARemovableDrive_SaysRemovable()
        ' Also both true, and here the volume wins: "no bin on removable media" explains
        ' the situation, while "the bin is switched off for this drive" invites the user
        ' to go and switch a bin back on that was never going to exist.
        Dim d = Decide(Facts(DeleteVolumeKind.Removable, binDisabled:=True))
        Assert.Equal(PermanentReason.NoBinOnRemovable, d.Reason)
    End Sub

    <Fact>
    Public Sub DisabledBin_OutranksTheQuota()
        Dim d = Decide(Facts(DeleteVolumeKind.FixedDisk, binDisabled:=True, quotaBytes:=1), size:=1000)
        Assert.Equal(PermanentReason.BinDisabledOnVolume, d.Reason)
    End Sub

    ' --- and the invariant the wording depends on ------------------------------

    ' Integer rather than the enum itself: DeleteVolumeKind is Friend (it belongs to the
    ' viewer, not to a public API), and a Public test method cannot expose it.
    <Theory>
    <InlineData(CInt(DeleteVolumeKind.Network))>
    <InlineData(CInt(DeleteVolumeKind.Removable))>
    <InlineData(CInt(DeleteVolumeKind.Unknown))>
    Public Sub EveryPermanentAnswerNamesAReason(kind As Integer)
        ' Invariant 3: "permanently" on its own is not an acceptable message.
        Dim d = Decide(Facts(CType(kind, DeleteVolumeKind)))
        Assert.Equal(DeleteOutcome.Permanent, d.Outcome)
        Assert.NotEqual(PermanentReason.NotPermanent, d.Reason)
    End Sub

    <Fact>
    Public Sub RecycleNeverCarriesAReason()
        Assert.Equal(PermanentReason.NotPermanent, Decide(Facts(DeleteVolumeKind.FixedDisk)).Reason)
    End Sub

End Class
#End If
