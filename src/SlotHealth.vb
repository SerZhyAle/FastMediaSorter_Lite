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

''' <summary>
''' Why a destination is <see cref="SlotState.Unreachable"/>, in the words the user can act on
''' (owner request 2026-10-17: the status line names the real cause, not just "no connection").
''' Read from an error CODE the operating system returned - never from its text, which is in
''' whatever language Windows speaks (APP-BEHAVIOUR rule 6). <see cref="None"/> is "the system
''' gave no code we name": the generic sentence stays, and the code goes to the log.
''' </summary>
Public Enum SlotCause
    None
    ''' <summary>The computer in the path does not answer by name: off, on another network, or a wrong
    ''' name (ERROR_BAD_NETPATH and its neighbours).</summary>
    HostNotFound
    ''' <summary>The computer answered and has no share of that name (ERROR_BAD_NET_NAME).</summary>
    ShareNotFound
    ''' <summary>There is no network to the computer (cable, VPN, adapter).</summary>
    NetworkDown
    ''' <summary>The session existed and was cut.</summary>
    ConnectionLost
    ''' <summary>A drive, card or mapped network drive is not connected.</summary>
    NotReady
    ''' <summary>Nothing answered inside the probe's bound - the cause behind that is not known (yet).</summary>
    NoAnswer
End Enum

''' <summary>One cached answer about one destination path. Keyed by PATH, not by slot
''' index (D3): two slots can share a root, and the overlay and the settings grid read the
''' same table.</summary>
Public NotInheritable Class SlotVerdict
    Public Property State As SlotState
    Public Property CheckedUtc As DateTime
    ''' <summary>For the log. Never shown as it is - the user reads <see cref="Cause"/> or
    ''' <see cref="ReasonText"/> through the status sentence (invariant 2, as amended 2026-10-17).</summary>
    Public Property Detail As String = ""

    ''' <summary>The nameable reason behind an Unreachable verdict, or None.</summary>
    Public Property Cause As SlotCause = SlotCause.None

    ''' <summary>A finished, localized sentence from the resource layer for a server folder
    ''' (<c>ResourceMessages.ForFailure</c>): "the server did not accept the login", "no such folder on the
    ''' server". Empty for a plain folder. When set it wins over <see cref="Cause"/>.</summary>
    Public Property ReasonText As String = ""

    Public Shared Function ForState(state As SlotState, checkedUtc As DateTime,
                                    Optional detail As String = "",
                                    Optional cause As SlotCause = SlotCause.None) As SlotVerdict
        Return New SlotVerdict With {.State = state, .CheckedUtc = checkedUtc, .Detail = detail, .Cause = cause}
    End Function

    ''' <summary>The same verdict carrying a resource-layer sentence; returns itself so a Return line stays one line.</summary>
    Public Function WithReason(text As String) As SlotVerdict
        ReasonText = If(text, "")
        Return Me
    End Function
End Class

''' <summary>
''' The pure half of "name the real reason": which cause a Windows error code is, and which place
''' in the path the sentence should name. No I/O, no clock, no localization - the sentences are
''' built where the language is known (Main_Form.SlotHealth.vb).
''' </summary>
Public Module SlotCauses

    ' The Win32 codes, by name - the same ones a user finds in "net helpmsg <n>".
    Private Const ErrorNotReady As Integer = 21              ' The device is not ready.
    Private Const ErrorRemNotList As Integer = 51            ' Windows cannot find the network path.
    Private Const ErrorBadNetPath As Integer = 53            ' The network path was not found.
    Private Const ErrorDevNotExist As Integer = 55           ' The specified network resource or device is no longer available.
    Private Const ErrorUnexpNetErr As Integer = 59           ' An unexpected network error occurred.
    Private Const ErrorNetNameDeleted As Integer = 64        ' The specified network name is no longer available.
    Private Const ErrorBadNetName As Integer = 67            ' The network name cannot be found.
    Private Const ErrorSemTimeout As Integer = 121           ' The semaphore timeout period has expired.
    Private Const ErrorNoNetOrBadPath As Integer = 1203      ' The network path was either typed incorrectly, does not exist...
    Private Const ErrorDeviceNotConnected As Integer = 1167  ' The device is not connected.
    Private Const ErrorNoNetwork As Integer = 1222           ' The network is not present or not started.
    Private Const ErrorNetworkUnreachable As Integer = 1231  ' The network location cannot be reached.
    Private Const ErrorHostUnreachable As Integer = 1232     ' The network location cannot be reached (host).
    Private Const ErrorConnectionAborted As Integer = 1236   ' The network connection was aborted by the local system.

    ''' <summary>An HRESULT built from a Win32 code has this facility in its top half (0x8007xxxx).</summary>
    Private Const FacilityWin32 As Integer = &H8007

    Public Function OfWin32(code As Integer) As SlotCause
        Select Case code
            Case ErrorBadNetPath, ErrorRemNotList, ErrorNoNetOrBadPath
                Return SlotCause.HostNotFound
            Case ErrorBadNetName
                Return SlotCause.ShareNotFound
            Case ErrorNoNetwork, ErrorNetworkUnreachable, ErrorHostUnreachable
                Return SlotCause.NetworkDown
            Case ErrorNetNameDeleted, ErrorUnexpNetErr, ErrorSemTimeout, ErrorConnectionAborted, ErrorDevNotExist
                Return SlotCause.ConnectionLost
            Case ErrorNotReady, ErrorDeviceNotConnected
                Return SlotCause.NotReady
            Case Else
                Return SlotCause.None
        End Select
    End Function

    ''' <summary>The cause behind a failed listing: the Win32 code carried in an IOException's HRESULT.
    ''' A missing folder, a denial and a bad path are other states with their own sentences and give None here.</summary>
    Public Function OfException(ex As Exception) As SlotCause
        Dim real As Exception = ex
        For hop As Integer = 0 To 7
            Dim aggregate As AggregateException = TryCast(real, AggregateException)
            If aggregate IsNot Nothing AndAlso aggregate.InnerExceptions.Count > 0 Then
                real = aggregate.InnerExceptions(0)
                Continue For
            End If
            Exit For
        Next
        If real Is Nothing Then Return SlotCause.None
        If TypeOf real Is TimeoutException Then Return SlotCause.NoAnswer
        If TypeOf real Is System.IO.IOException AndAlso ((real.HResult >> 16) And &HFFFF) = FacilityWin32 Then
            Return OfWin32(real.HResult And &HFFFF)
        End If
        Return SlotCause.None
    End Function

    ''' <summary>The computer in <c>\\host\share\..</c> (also <c>\\?\UNC\host\share</c>), or "" for any other path.</summary>
    Public Function HostOf(destination As String) As String
        Dim parts As String() = UncParts(destination)
        Return If(parts.Length > 0, parts(0), "")
    End Function

    ''' <summary><c>\\host\share</c> of a UNC path, or "" for any other path.</summary>
    Public Function ShareRootOf(destination As String) As String
        Dim parts As String() = UncParts(destination)
        If parts.Length < 2 Then Return ""
        Return "\\" & parts(0) & "\" & parts(1)
    End Function

    ''' <summary>The place a sentence names for this cause: the share for a missing share, otherwise the computer of a
    ''' UNC path, the drive of a lettered path, or the path itself.</summary>
    Public Function PlaceFor(destination As String, cause As SlotCause) As String
        Dim path As String = If(destination, "").Trim()
        If cause = SlotCause.ShareNotFound Then
            Dim share As String = ShareRootOf(path)
            If share.Length > 0 Then Return share
        End If
        Dim host As String = HostOf(path)
        If host.Length > 0 Then Return host
        If path.Length >= 2 AndAlso path(1) = ":"c Then Return path.Substring(0, 2)
        Return path
    End Function

    Private Function UncParts(destination As String) As String()
        Dim path As String = If(destination, "").Trim()
        If path.StartsWith("\\?\UNC\", StringComparison.OrdinalIgnoreCase) Then
            path = "\\" & path.Substring(8)
        ElseIf path.StartsWith("\\?\", StringComparison.Ordinal) OrElse path.StartsWith("\\.\", StringComparison.Ordinal) Then
            Return New String() {}
        End If
        If Not path.StartsWith("\\", StringComparison.Ordinal) Then Return New String() {}
        Dim parts As String() = path.Substring(2).Split(New Char() {"\"c}, StringSplitOptions.RemoveEmptyEntries)
        Return parts
    End Function

End Module

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

    ''' <summary>
    ''' The one spelling of a slot path, applied where a slot is STORED (the registry load,
    ''' the folder picker, a typed grid cell) - audit 047, C-24. The probe keyed its verdict
    ''' on the trimmed value while the action used the raw one, so "D:\Sorted " probed
    ''' Ready and then moved into "D:\Sorted \x.jpg", failed and rolled back. Surrounding
    ''' white space goes; a fully qualified path is made canonical; anything else (a
    ''' relative path, which would resolve against the working directory) is left as typed
    ''' so the probe can say it is invalid.
    ''' </summary>
    Public Function NormalizeSlotPath(raw As String) As String
        If raw Is Nothing Then Return ""
        Dim trimmed As String = raw.Trim()
        If trimmed.Length = 0 Then Return ""
        If ResourceLocator.IsLocator(trimmed) Then Return trimmed
        Try
            If System.IO.Path.IsPathFullyQualified(trimmed) Then Return System.IO.Path.GetFullPath(trimmed)
        Catch
            ' Characters no path may hold: keep it as typed - the probe reports it Invalid.
        End Try
        Return trimmed
    End Function

    ''' <summary>
    ''' The verdict for a folder that refused to be LISTED, given what reading its
    ''' attributes said (audit 057.S05-7). A write-only drop-box - add right, no list right,
    ''' common on a NAS - refuses the listing and still accepts a file, so a listing refusal
    ''' alone is not "no access": the folder being there (as a folder) is enough to let the
    ''' operation try, and a genuine write refusal is then reported by the operation itself,
    ''' which names the folder. A path that turns out to be a file is Invalid, as it is when
    ''' the listing says so; anything else keeps the denial.
    ''' </summary>
    Public Function StateAfterDeniedListing(attributes As PathFailureKind) As SlotState
        Select Case attributes
            Case PathFailureKind.None : Return SlotState.Ready
            Case PathFailureKind.Invalid : Return SlotState.Invalid
            Case Else : Return SlotState.Denied
        End Select
    End Function

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
