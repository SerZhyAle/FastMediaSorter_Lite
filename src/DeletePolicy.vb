#If Not NETFRAMEWORK Then
Option Strict On

' Whether a deletion can go to the Recycle Bin - and, when it cannot, WHY.
' 017_SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.1. Modern build only, like the
' rest of the feature, so this whole file compiles to nothing in the x86 viewer.
'
' WHY IT IS SEPARATE FROM THE PROBE: everything here is a pure function of its
' arguments - no registry, no DriveInfo, no WinForms. That is what makes the load-
' bearing fact of this feature (§0.3: a network share and a USB stick have no bin at
' all, and those are the two places this application is opened on) provable in a test
' instead of needing a NAS and a memory card to check by hand.
'
' The decision only ever chooses WORDING plus which executor runs; it never decides
' whether the file is deleted. That is the mitigation for §6.1 - a prediction can be
' wrong, and the worst case has to be a scarier sentence than necessary, never a file
' the user was told was recoverable.

''' <summary>What kind of volume the file lives on. Only FixedDisk has a Recycle Bin.</summary>
Friend Enum DeleteVolumeKind
    FixedDisk
    Network
    Removable
    Unknown
End Enum

''' <summary>Where the file actually goes.</summary>
Friend Enum DeleteOutcome
    Recycle
    Permanent
End Enum

''' <summary>
''' Why a deletion is permanent. It exists so the confirmation and the status line can
''' NAME the reason instead of saying "permanently" and leaving the user to guess
''' whether that was their setting or their share (invariant 3).
''' </summary>
Friend Enum PermanentReason
    NotPermanent
    ''' <summary>Shift+DEL, or "use the Recycle Bin" switched off.</summary>
    UserAsked
    NoBinOnNetwork
    NoBinOnRemovable
    ''' <summary>NukeOnDelete, or the NoRecycleFiles policy.</summary>
    BinDisabledOnVolume
    ''' <summary>The file is bigger than this volume's bin: the shell would hard-delete it.</summary>
    FileExceedsBinQuota
    VolumeUnknown
End Enum

''' <summary>What the probe found out about a volume. Filled by DeleteVolumeProbe.</summary>
Friend NotInheritable Class DeleteVolumeFacts
    Public Property Kind As DeleteVolumeKind = DeleteVolumeKind.Unknown
    Public Property BinDisabled As Boolean
    ''' <summary>-1 when unknown. Bytes.</summary>
    Public Property BinQuotaBytes As Long = -1
End Class

''' <summary>The answer. Both the confirmation and the status line are built from this
''' one object, which is what keeps them from ever disagreeing (invariant 2).</summary>
Friend NotInheritable Class DeleteDecision
    Public Property Outcome As DeleteOutcome
    Public Property Reason As PermanentReason

    Public ReadOnly Property IsPermanent As Boolean
        Get
            Return Outcome = DeleteOutcome.Permanent
        End Get
    End Property
End Class

Friend Module DeletePolicy

    ''' <summary>
    ''' The whole rule set.
    '''
    ''' THE ORDER IS PART OF THE CONTRACT, because it decides which reason the user is
    ''' shown when two of them apply at once. A Shift+DEL on a network share reports
    ''' "you asked for it", not "there is no bin on a network drive": the user held Shift
    ''' on purpose and does not need to be told about the share. Reversing these two
    ''' would produce a message that is true and unhelpful.
    ''' </summary>
    Friend Function Decide(facts As DeleteVolumeFacts, fileSizeBytes As Long,
                           binEnabledBySetting As Boolean, forcedPermanent As Boolean) As DeleteDecision

        ' 1-2: the user's own choice outranks every property of the volume.
        If forcedPermanent Then Return Permanent(PermanentReason.UserAsked)
        If Not binEnabledBySetting Then Return Permanent(PermanentReason.UserAsked)

        ' No facts at all is not the same as "fine": an unprobed path errs towards the
        ' honest, scarier text (rule 5), never towards promising a bin that may not exist.
        Dim volume As DeleteVolumeFacts = If(facts, New DeleteVolumeFacts())

        ' 3-5: Windows keeps no bin on these, whatever we would prefer.
        Select Case volume.Kind
            Case DeleteVolumeKind.Network
                Return Permanent(PermanentReason.NoBinOnNetwork)
            Case DeleteVolumeKind.Removable
                Return Permanent(PermanentReason.NoBinOnRemovable)
            Case DeleteVolumeKind.Unknown
                Return Permanent(PermanentReason.VolumeUnknown)
        End Select

        ' 6: this volume's bin is switched off - by the user, or by policy.
        If volume.BinDisabled Then Return Permanent(PermanentReason.BinDisabledOnVolume)

        ' 7: too big to fit. The shell deletes such a file outright, silently, so saying
        ' "to the Recycle Bin" here would be a lie we could have avoided.
        If volume.BinQuotaBytes >= 0 AndAlso fileSizeBytes > volume.BinQuotaBytes Then
            Return Permanent(PermanentReason.FileExceedsBinQuota)
        End If

        Return New DeleteDecision With {.Outcome = DeleteOutcome.Recycle,
                                        .Reason = PermanentReason.NotPermanent}
    End Function

    ''' <summary>
    ''' Whether this deletion has to be confirmed (§3.7). Pure, and separate from the two
    ''' surfaces that ask - the viewer and the F3 panel - because the whole point of the
    ''' setting is that they agree.
    '''
    ''' The middle value is the reason the setting exists. One flag could only say "ask
    ''' about everything" or "ask about nothing", so a person who wanted a fast sorting run
    ''' had to switch off the question that guards the one deletion that cannot be taken
    ''' back. Now a recycled deletion can go through in silence while a permanent one still
    ''' stops - the two are different questions and they were never the same answer.
    '''
    ''' An unclassified decision counts as permanent: as everywhere else in this file, not
    ''' knowing errs towards asking.
    ''' </summary>
    Friend Function ShouldConfirm(policy As String, decision As DeleteDecision) As Boolean
        Select Case If(policy, "")
            Case "never"
                Return False
            Case "permanentOnly"
                Return decision Is Nothing OrElse decision.IsPermanent
            Case Else
                Return True
        End Select
    End Function

    Private Function Permanent(reason As PermanentReason) As DeleteDecision
        Return New DeleteDecision With {.Outcome = DeleteOutcome.Permanent, .Reason = reason}
    End Function

End Module
#End If
