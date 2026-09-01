#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' Whether a recipient slot can actually take a file, and how long that answer is worth
''' trusting. 011_SPECIFICATION_SLOT_HEALTH_AND_HONEST_FAILURES_DOTNET10.md §3.2.
'''
''' It exists because a slot was never validated at any point in its life: the registry read,
''' the folder picker, the typed grid cell and IsRecipientSlotConfigured all accept anything
''' non-blank. Pressing "3" into a sleeping NAS therefore cost twenty queued operations,
''' twenty files optimistically removed from the list, and twenty SMB timeouts arriving
''' minutes later in an order unrelated to what is on screen (§0.2). One probe on the first
''' press replaces all of it with one honest sentence.
'''
''' The POLICY is here, pure, with no clock and no I/O of its own - it is handed the time.
''' That is what makes "twenty presses cost one probe" a property provable in a test rather
''' than a claim about a NAS somebody has to put to sleep by hand. The probe itself, which
''' does touch the network, lives in Main_Form.SlotHealth.vb.
'''
''' Modern-only (whole file). The x86 fallback is frozen by the maintenance policy in
''' CLAUDE.md, and its file-operation transport is a single BackgroundWorker that refuses a
''' second operation anyway - the twenty-presses shape this guards against cannot arise
''' there (invariant 10).
''' </summary>
Public Enum SlotState
    ''' <summary>No path configured. Never probed - there is nothing to ask about.</summary>
    NotConfigured
    ''' <summary>The folder answered. The operation may proceed.</summary>
    Ready
    ''' <summary>The final segment is missing, its parent answered, and auto-create is on.
    ''' The operation may proceed and the worker will make the folder (D6, §3.6).</summary>
    WillBeCreated
    ''' <summary>The folder is genuinely not there, and we are not going to invent it.</summary>
    Missing
    ''' <summary>Something answered and said no.</summary>
    Denied
    ''' <summary>Nothing between us and the folder answered - a sleeping NAS, a dropped
    ''' SMB session, a share name that does not resolve. Also the verdict for a probe that
    ''' ran out of time, which is the honest answer anyway: a destination that cannot reply
    ''' in two seconds cannot absorb a 200 MB clip at sorting speed either.</summary>
    Unreachable
    ''' <summary>The path itself cannot be used.</summary>
    Invalid
End Enum

''' <summary>One cached answer about one destination path. Keyed by PATH, not by slot
''' index (D3): two slots can share a root, and the overlay and the settings grid read the
''' same table.</summary>
Public NotInheritable Class SlotVerdict
    Public Property State As SlotState
    Public Property CheckedUtc As DateTime
    ''' <summary>For the log. Never for the user - the user gets a category (invariant 2).</summary>
    Public Property Detail As String = ""

    Public Shared Function ForState(state As SlotState, checkedUtc As DateTime,
                                    Optional detail As String = "") As SlotVerdict
        Return New SlotVerdict With {.State = state, .CheckedUtc = checkedUtc, .Detail = detail}
    End Function
End Class

Public Module SlotHealthPolicy

    ''' <summary>A healthy share does not die every minute, and re-probing during a fast
    ''' sorting run costs tempo for nothing.</summary>
    Public Const Good_Ttl_Seconds As Integer = 120

    ''' <summary>Long enough that a burst of presses costs one probe, short enough that a
    ''' NAS waking up is noticed without the user having to do anything.</summary>
    Public Const Bad_Ttl_Seconds As Integer = 30

    ''' <summary>D4: the same slot pressed twice inside this window is the user saying
    ''' "it is awake now, look again" - the retry gesture, without a new button.</summary>
    Public Const Retry_Window_Seconds As Integer = 2

    ''' <summary>The probe's own bound (D2, §3.3). Deliberately NOT ProbeArgument's retry
    ''' ladder: that one can sleep through several SMB timeouts, which is right when the
    ''' user asked to open a specific file and catastrophic as an answer to "is slot 3
    ''' alive".</summary>
    Public Const Probe_Timeout_Ms As Integer = 2000

    ''' <summary>May an action against a slot in this state go ahead?</summary>
    Public Function IsUsable(state As SlotState) As Boolean
        Return state = SlotState.Ready OrElse state = SlotState.WillBeCreated
    End Function

    ''' <summary>Two presses of the same slot inside the retry window (D4).</summary>
    Public Function IsRepeatPress(nowUtc As DateTime, lastPressUtc As DateTime) As Boolean
        If lastPressUtc = DateTime.MinValue Then Return False
        Dim gap As Double = (nowUtc - lastPressUtc).TotalSeconds
        If gap < 0 Then Return False
        Return gap <= Retry_Window_Seconds
    End Function

    ''' <summary>
    ''' Should this press pay for a probe?
    '''
    ''' A good verdict is trusted for <see cref="Good_Ttl_Seconds"/>, a bad one for
    ''' <see cref="Bad_Ttl_Seconds"/>, and an explicit retry re-probes - but ONLY once the
    ''' standing answer is older than the retry window itself. That last clause is what
    ''' keeps D4 and §4's "twenty presses cost one probe" from contradicting each other:
    ''' at sorting speed every press looks like a double-press, and a verdict younger than
    ''' the window IS the answer to the retry, so paying two seconds to hear it again would
    ''' turn the retry gesture into a tax on fast sorting.
    ''' </summary>
    Public Function ShouldProbe(nowUtc As DateTime, verdict As SlotVerdict, isRepeatPress As Boolean) As Boolean
        ' Nothing known: the first press of the session pays, and it is the press that
        ' pays for itself twenty times over.
        If verdict Is Nothing Then Return True

        ' Nothing to ask about - the slot has no path.
        If verdict.State = SlotState.NotConfigured Then Return False

        Dim age As Double = (nowUtc - verdict.CheckedUtc).TotalSeconds
        ' The clock moved backwards (a DST change, an NTP correction). Trusting an answer
        ' from the future is the one thing an age-based cache must not do.
        If age < 0 Then Return True

        If isRepeatPress AndAlso age >= Retry_Window_Seconds Then Return True

        Dim ttl As Integer = If(IsUsable(verdict.State), Good_Ttl_Seconds, Bad_Ttl_Seconds)
        ' At exactly the TTL the answer has expired: an inclusive bound keeps "trusted for
        ' N seconds" literally true.
        Return age >= ttl
    End Function

End Module
#End If
