Option Strict On

''' <summary>Where the access check of the CURRENT export payload stands.</summary>
Friend Enum PreflightState
    ''' <summary>No verdict for the current selection.</summary>
    NotChecked = 0
    ''' <summary>A scan is in flight. Not a permission.</summary>
    Checking = 1
    ''' <summary>The serving account can read what the code promises.</summary>
    Allowed = 2
    ''' <summary>Every shared root is unreadable: the code would open an empty tree.</summary>
    Blocked = 3
    ''' <summary>The scan did not finish inside its budget. Not a permission.</summary>
    TimedOut = 4
    ''' <summary>The check was abandoned (window closed, user retried). Not a permission.</summary>
    Cancelled = 5
    ''' <summary>The scan faulted. Not a permission.</summary>
    Failed = 6
End Enum

''' <summary>What a finished scan concluded.</summary>
Friend Enum PreflightOutcome
    Allowed = 0
    Blocked = 1
    TimedOut = 2
    Failed = 3
End Enum

''' <summary>
''' Who may be handed a code, a file or an e-mail (108 H02, 057.S22-4). One object decides it for
''' every surface, from two facts that are never mixed up:
'''
'''  - the VERDICT belongs to what the scan looked at (the access key: the roots, their write flags
'''    and the host mode), and a scan's answer is applied only if it is the scan in flight for the
'''    current key - an older scan finishing late changes nothing;
'''  - the PAYLOAD belongs to a revision: every edit of a root, an option or the serving mode makes a
'''    new revision and invalidates the built payload at once. It is offered only when a payload built
'''    for exactly the current revision meets an Allowed verdict.
'''
''' Pending, timed-out, failed and cancelled are never permission. There is no override here.
''' UI-thread object; no framework types, so every sequence is a unit test.
''' </summary>
Friend NotInheritable Class PreflightTracker

    Private revisionValue As Integer
    Private builtRevision As Integer
    Private stateValue As PreflightState = PreflightState.NotChecked
    Private scanId As Integer
    Private currentKey As String
    Private cachedKey As String
    Private cachedOutcome As PreflightOutcome

    Friend ReadOnly Property Revision As Integer
        Get
            Return revisionValue
        End Get
    End Property

    Friend ReadOnly Property State As PreflightState
        Get
            Return stateValue
        End Get
    End Property

    ''' <summary>The id of the scan whose answer would be applied now (0 = none in flight).</summary>
    Friend ReadOnly Property ScanInFlight As Integer
        Get
            Return If(stateValue = PreflightState.Checking, scanId, 0)
        End Get
    End Property

    ''' <summary>May the payload (QR, file, e-mail) be handed out right now?</summary>
    Friend Function CanOffer() As Boolean
        Return stateValue = PreflightState.Allowed AndAlso builtRevision <> 0 AndAlso builtRevision = revisionValue
    End Function

    ''' <summary>Any edit - a root, an option, the serving mode. The previous payload is void at
    ''' once. A verdict survives only if the edit left what the scan looks at unchanged.</summary>
    Friend Sub Edit(accessKey As String)
        revisionValue += 1
        builtRevision = 0
        If Not String.Equals(accessKey, currentKey, StringComparison.Ordinal) Then
            currentKey = accessKey
            ' A scan for the previous key must never answer for this one.
            scanId += 1
            stateValue = PreflightState.NotChecked
        End If
    End Sub

    ''' <summary>The payload for the CURRENT revision has been built.</summary>
    Friend Sub PayloadBuilt()
        builtRevision = revisionValue
    End Sub

    ''' <summary>
    ''' Starts (or joins) the check for the current key. Returns the id of a scan the caller must now
    ''' run, or 0 when none is needed: a definitive verdict for this key is reused, and a scan already
    ''' in flight for it is joined, never doubled.
    ''' </summary>
    Friend Function Begin() As Integer
        If stateValue = PreflightState.Checking Then Return 0
        If cachedKey IsNot Nothing AndAlso String.Equals(cachedKey, currentKey, StringComparison.Ordinal) AndAlso
           (stateValue = PreflightState.Allowed OrElse stateValue = PreflightState.Blocked) Then Return 0
        If cachedKey IsNot Nothing AndAlso String.Equals(cachedKey, currentKey, StringComparison.Ordinal) AndAlso
           stateValue = PreflightState.NotChecked Then
            stateValue = If(cachedOutcome = PreflightOutcome.Allowed, PreflightState.Allowed, PreflightState.Blocked)
            Return 0
        End If
        scanId += 1
        stateValue = PreflightState.Checking
        Return scanId
    End Function

    ''' <summary>A scan answered. Applied only for the scan in flight; a stale or repeated answer
    ''' is ignored and reported as False.</summary>
    Friend Function Complete(id As Integer, outcome As PreflightOutcome) As Boolean
        If stateValue <> PreflightState.Checking OrElse id <> scanId Then Return False
        Select Case outcome
            Case PreflightOutcome.Allowed, PreflightOutcome.Blocked
                stateValue = If(outcome = PreflightOutcome.Allowed, PreflightState.Allowed, PreflightState.Blocked)
                cachedKey = currentKey
                cachedOutcome = outcome
            Case PreflightOutcome.TimedOut
                stateValue = PreflightState.TimedOut
            Case Else
                stateValue = PreflightState.Failed
        End Select
        Return True
    End Function

    ''' <summary>The wizard is going away (or the scan is abandoned): nothing may land afterwards.</summary>
    Friend Sub Cancel()
        scanId += 1
        If stateValue = PreflightState.Checking Then stateValue = PreflightState.Cancelled
    End Sub

    ''' <summary>"Check again": forget any verdict for this key and start over. Used after a timeout,
    ''' a failure, a cancellation - or a repaired folder after a Blocked verdict.</summary>
    Friend Function Retry() As Integer
        cachedKey = Nothing
        stateValue = PreflightState.NotChecked
        Return Begin()
    End Function

    ''' <summary>True for the states from which the user may ask again.</summary>
    Friend ReadOnly Property CanRetry As Boolean
        Get
            Return stateValue = PreflightState.TimedOut OrElse stateValue = PreflightState.Failed OrElse
                   stateValue = PreflightState.Cancelled OrElse stateValue = PreflightState.Blocked
        End Get
    End Property

End Class

Friend Module PreflightLogic

    ''' <summary>
    ''' What a finished scan means, as a function. Only a COMPLETE scan can allow: a scan that ran out
    ''' of its budget is a timeout however clean the part it saw, and a faulted one is a failure - the
    ''' old reading ("a faulted scan reads as nothing blocked") handed out codes nobody had checked.
    ''' Every root unreadable is Blocked whether or not the walk finished.
    ''' </summary>
    Friend Function Judge(faulted As Boolean, completed As Boolean, blockedCount As Integer, allRootsBlocked As Boolean) As PreflightOutcome
        If faulted Then Return PreflightOutcome.Failed
        If allRootsBlocked Then Return PreflightOutcome.Blocked
        If Not completed Then Return PreflightOutcome.TimedOut
        Return PreflightOutcome.Allowed
    End Function

End Module
