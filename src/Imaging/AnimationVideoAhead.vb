#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>Where the video prepared AHEAD of the one on screen is (spec 242 rules 20 and 24).</summary>
Friend Enum AheadPhase As Integer
    ''' <summary>Nothing is prepared or being prepared.</summary>
    None = 0
    ''' <summary>The encoder (or the sealing after it) is working for the target.</summary>
    Preparing = 1
    ''' <summary>The sealed container of the target waits in the slot.</summary>
    Ready = 2
End Enum

''' <summary>What landing on a file means for the slot.</summary>
Friend Enum AheadPromotion As Integer
    ''' <summary>The slot is not about this file (or about an older version of it): prepare it the ordinary way.</summary>
    NotAhead = 0
    ''' <summary>The slot is preparing exactly this file: the landing waits, and the finished container is handed to it.</summary>
    Waiting = 1
    ''' <summary>The container was ready and has been taken out of the slot.</summary>
    Ready = 2
End Enum

''' <summary>What a finished preparation turned into.</summary>
Friend Enum AheadCompletion As Integer
    ''' <summary>Nobody wants it any more (the target changed, the slot was emptied): the caller disposes the container.</summary>
    Stale = 0
    ''' <summary>The slot keeps the container for the file that may come next.</summary>
    Parked = 1
    ''' <summary>The user already landed on the file: the caller owns the container and plays it.</summary>
    Delivered = 2
End Enum

''' <summary>
''' The single slot of the video prepared one file ahead (spec 242 rules 20 and 24). Pure and thread-safe: the
''' viewer's glue asks it questions from the UI thread and from the pool, and it never touches a process, a
''' container or a form - the lease is only held, handed over and, by the caller, disposed.
'''
''' The promises, each a unit test:
'''  * ONE target at a time. <see cref="TryBegin"/> refuses while the slot is occupied, so the caller empties it
'''    (<see cref="Discard"/>) before aiming elsewhere.
'''  * A late answer is recognised by its ticket: after a discard, or after the slot was aimed at another file, the
'''    old preparation completes as <see cref="AheadCompletion.Stale"/> and its container is the caller's to dispose.
'''  * A landing on the target while it is still being prepared (<see cref="Promote"/> = Waiting) is delivered
'''    exactly once, with the container, to the waiter the landing left.
'''  * The target is identified by path AND a stamp of the file (length + write time), so a file that changed after
'''    the preparation began is never served from an older video (Q242-6).
''' </summary>
Friend NotInheritable Class AnimationVideoAheadSlot(Of TLease As {Class, IDisposable})

    Private ReadOnly gate As New Object()
    Private phaseValue As AheadPhase = AheadPhase.None
    Private pathValue As String = ""
    Private stampValue As String = ""
    Private ticketValue As Integer
    Private ticketCounter As Integer
    Private leaseValue As TLease
    Private waiterValue As Object

    Friend ReadOnly Property Phase As AheadPhase
        Get
            SyncLock gate
                Return phaseValue
            End SyncLock
        End Get
    End Property

    ''' <summary>The file the slot is aimed at; "" when it is empty.</summary>
    Friend ReadOnly Property TargetPath As String
        Get
            SyncLock gate
                Return pathValue
            End SyncLock
        End Get
    End Property

    ''' <summary>Aims the empty slot at <paramref name="path"/> and returns the ticket of that preparation, or 0 when
    ''' there is nothing to do: the slot is occupied (by this file or another) or the arguments are empty.</summary>
    Friend Function TryBegin(path As String, stamp As String) As Integer
        If String.IsNullOrEmpty(path) OrElse String.IsNullOrEmpty(stamp) Then Return 0
        SyncLock gate
            If phaseValue <> AheadPhase.None Then Return 0
            ticketCounter += 1
            ticketValue = ticketCounter
            phaseValue = AheadPhase.Preparing
            pathValue = path
            stampValue = stamp
            leaseValue = Nothing
            waiterValue = Nothing
            Return ticketValue
        End SyncLock
    End Function

    ''' <summary>True while <paramref name="ticket"/> is the preparation the slot is waiting for.</summary>
    Friend Function IsCurrent(ticket As Integer) As Boolean
        SyncLock gate
            Return ticket <> 0 AndAlso phaseValue = AheadPhase.Preparing AndAlso ticket = ticketValue
        End SyncLock
    End Function

    ''' <summary>The preparation <paramref name="ticket"/> produced <paramref name="lease"/>. Stale: the caller disposes
    ''' it. Parked: the slot holds it. Delivered: the slot is empty again and <paramref name="waiter"/> is whoever
    ''' landed on the file meanwhile; the caller owns the lease.</summary>
    Friend Function Complete(ticket As Integer, lease As TLease, ByRef waiter As Object) As AheadCompletion
        waiter = Nothing
        SyncLock gate
            If lease Is Nothing OrElse Not (ticket <> 0 AndAlso phaseValue = AheadPhase.Preparing AndAlso ticket = ticketValue) Then
                Return AheadCompletion.Stale
            End If
            If waiterValue IsNot Nothing Then
                waiter = waiterValue
                ClearLocked()
                Return AheadCompletion.Delivered
            End If
            leaseValue = lease
            phaseValue = AheadPhase.Ready
            Return AheadCompletion.Parked
        End SyncLock
    End Function

    ''' <summary>The preparation <paramref name="ticket"/> ended without a container (not eligible, failed). True when it
    ''' was the current one - the slot is empty again and <paramref name="waiter"/> is whoever was waiting for it, who
    ''' now needs the ordinary route. False when the answer is stale and nobody is waiting.</summary>
    Friend Function Fail(ticket As Integer, ByRef waiter As Object) As Boolean
        waiter = Nothing
        SyncLock gate
            If Not (ticket <> 0 AndAlso phaseValue = AheadPhase.Preparing AndAlso ticket = ticketValue) Then Return False
            waiter = waiterValue
            ClearLocked()
            Return True
        End SyncLock
    End Function

    ''' <summary>The user landed on <paramref name="path"/> (whose file stamp is <paramref name="stamp"/>).
    ''' <see cref="AheadPromotion.Ready"/>: <paramref name="lease"/> is the container, the slot is empty.
    ''' <see cref="AheadPromotion.Waiting"/>: the slot is preparing exactly this file and will deliver to
    ''' <paramref name="waiter"/> (a later landing replaces an earlier waiter). Anything else is NotAhead and leaves the
    ''' slot alone - the caller empties it with <see cref="Discard"/>.</summary>
    Friend Function Promote(path As String, stamp As String, waiter As Object, ByRef lease As TLease) As AheadPromotion
        lease = Nothing
        SyncLock gate
            If phaseValue = AheadPhase.None OrElse Not IsTargetLocked(path, stamp) Then Return AheadPromotion.NotAhead
            If phaseValue = AheadPhase.Ready Then
                lease = leaseValue
                ClearLocked()
                Return AheadPromotion.Ready
            End If
            waiterValue = waiter
            Return AheadPromotion.Waiting
        End SyncLock
    End Function

    ''' <summary>Empties the slot. Returns the container that was waiting in it (the caller disposes it);
    ''' <paramref name="wasPreparing"/> says a preparation is still running and must be cancelled by the caller.</summary>
    Friend Function Discard(ByRef wasPreparing As Boolean) As TLease
        SyncLock gate
            wasPreparing = (phaseValue = AheadPhase.Preparing)
            Dim held As TLease = leaseValue
            ClearLocked()
            Return held
        End SyncLock
    End Function

    ''' <summary>Empties the slot unless it is aimed at <paramref name="currentPath"/> - the file now on screen, which
    ''' the slot may be about to deliver. Same results as <see cref="Discard"/>.</summary>
    Friend Function Reconcile(currentPath As String, ByRef wasPreparing As Boolean) As TLease
        SyncLock gate
            wasPreparing = False
            If phaseValue = AheadPhase.None Then Return Nothing
            If String.Equals(pathValue, currentPath, StringComparison.OrdinalIgnoreCase) Then Return Nothing
            wasPreparing = (phaseValue = AheadPhase.Preparing)
            Dim held As TLease = leaseValue
            ClearLocked()
            Return held
        End SyncLock
    End Function

    Private Function IsTargetLocked(path As String, stamp As String) As Boolean
        Return String.Equals(pathValue, path, StringComparison.OrdinalIgnoreCase) AndAlso
               String.Equals(stampValue, stamp, StringComparison.Ordinal)
    End Function

    Private Sub ClearLocked()
        phaseValue = AheadPhase.None
        pathValue = ""
        stampValue = ""
        ticketValue = 0
        leaseValue = Nothing
        waiterValue = Nothing
    End Sub

End Class
#End If
