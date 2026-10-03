Option Strict On

Imports System.Collections.Generic

''' <summary>
''' When the main settings block reaches the registry, and what "saved" means (105 L09,
''' 057.S13-11). Dirty is an EDIT GENERATION compared with the last generation a flush completed
''' in full - not a flag a write can clear while a newer edit is waiting. A failed flush is retried
''' a bounded number of times at spaced delays (never a hot loop); a flush never overlaps another;
''' exhaustion keeps the state dirty and says so once; the next edit starts a fresh series.
'''
''' UI-thread object. The timer is an abstraction so the schedule is asserted, not waited for.
''' </summary>
Friend NotInheritable Class SettingsFlushScheduler

    ''' <summary>The lazy-flush debounce: measured from the LAST edit.</summary>
    Friend Const IdleDelayMs As Integer = 60000
    ''' <summary>Three automatic retries, 1 s / 4 s / 10 s after the failure.</summary>
    Friend Shared ReadOnly RetryDelaysMs As Integer() = {1000, 4000, 10000}

    Private ReadOnly flushAll As Func(Of Boolean)
    Private ReadOnly startTimer As Action(Of Integer)
    Private ReadOnly stopTimer As Action
    Private ReadOnly log As Action(Of String)

    Private generationValue As Integer
    Private flushedGeneration As Integer
    Private retryCount As Integer
    Private flushing As Boolean
    Private exhaustionReported As Boolean

    Friend Sub New(flush As Func(Of Boolean), startTimer As Action(Of Integer), stopTimer As Action, log As Action(Of String))
        Me.flushAll = flush
        Me.startTimer = startTimer
        Me.stopTimer = stopTimer
        Me.log = log
    End Sub

    ''' <summary>True while an edit exists that no complete flush has covered.</summary>
    Friend ReadOnly Property IsDirty As Boolean
        Get
            Return generationValue <> flushedGeneration
        End Get
    End Property

    Friend ReadOnly Property Generation As Integer
        Get
            Return generationValue
        End Get
    End Property

    Friend ReadOnly Property RetriesUsed As Integer
        Get
            Return retryCount
        End Get
    End Property

    ''' <summary>An edit worth keeping: a new generation, a fresh retry series, the debounce restarted.</summary>
    Friend Sub MarkDirty()
        generationValue += 1
        retryCount = 0
        exhaustionReported = False
        startTimer(IdleDelayMs)
    End Sub

    ''' <summary>The timer fired. A tick that finds nothing dirty, or a flush already running
    ''' (re-entry), does nothing.</summary>
    Friend Sub OnTimerTick()
        stopTimer()
        If Not IsDirty OrElse flushing Then Return
        RunFlush(scheduleRetries:=True)
    End Sub

    ''' <summary>A deliberate save or the close: now, no retry schedule (a close must not wait
    ''' out retry delays). False when it failed - the state stays dirty and the failure is logged.</summary>
    Friend Function SaveNow() As Boolean
        stopTimer()
        If flushing Then Return False
        ' Even with nothing pending a deliberate save writes the block (window geometry etc.).
        Return RunFlush(scheduleRetries:=False)
    End Function

    Private Function RunFlush(scheduleRetries As Boolean) As Boolean
        flushing = True
        Dim snapshot As Integer = generationValue
        Dim succeeded As Boolean = False
        Try
            succeeded = flushAll()
        Catch ex As Exception
            log("Settings: the save threw " & ex.GetType().Name)
        Finally
            flushing = False
        End Try

        If succeeded Then
            ' Complete only for the generation that was current when the flush began: an edit that
            ' arrived meanwhile is still unsaved and has already scheduled its own flush.
            flushedGeneration = snapshot
            retryCount = 0
            exhaustionReported = False
            Return True
        End If

        If generationValue <> snapshot Then Return False ' a newer edit owns the schedule now
        If Not scheduleRetries Then
            log("Settings: the save is incomplete and stays pending")
            Return False
        End If
        If retryCount < RetryDelaysMs.Length Then
            Dim delay As Integer = RetryDelaysMs(retryCount)
            retryCount += 1
            log("Settings: the save is incomplete, retry " & retryCount.ToString() & " of " & RetryDelaysMs.Length.ToString() &
                " in " & delay.ToString() & " ms")
            startTimer(delay)
        ElseIf Not exhaustionReported Then
            exhaustionReported = True
            log("Settings: the save is still incomplete after " & RetryDelaysMs.Length.ToString() &
                " retries; the changes stay pending until the next edit or a deliberate save")
        End If
        Return False
    End Function

End Class

Friend NotInheritable Class SettingsWriteResult
    Friend ReadOnly Failed As New List(Of String)()
    Friend Written As Integer

    Friend ReadOnly Property Succeeded As Boolean
        Get
            Return Failed.Count = 0
        End Get
    End Property
End Class

Friend Module SettingsWriter

    ''' <summary>
    ''' Writes every entry; a failing one does not stop the rest (a failed first slot must not
    ''' take the remaining nine with it) and is NAMED in the result. The whole list is idempotent
    ''' - a retry rewrites the complete block, which is why nothing here tracks "what is left".
    ''' </summary>
    Friend Function WriteAll(entries As IEnumerable(Of KeyValuePair(Of String, String)),
                             write As Action(Of String, String),
                             Optional onFailure As Action(Of String, Exception) = Nothing) As SettingsWriteResult
        Dim result As New SettingsWriteResult()
        For Each entry As KeyValuePair(Of String, String) In entries
            Try
                write(entry.Key, entry.Value)
                result.Written += 1
            Catch ex As Exception
                result.Failed.Add(entry.Key)
                If onFailure IsNot Nothing Then
                    Try : onFailure(entry.Key, ex) : Catch : End Try
                End If
            End Try
        Next
        Return result
    End Function

End Module

''' <summary>
''' "Only the latest answer may land" (057.S14-1): each asynchronous measurement takes a ticket
''' when it starts and may touch the UI only while that ticket is still the newest. Two
''' overlapping measurements of the same cache therefore cannot finish in the wrong order and
''' leave the older size on screen.
''' </summary>
Friend NotInheritable Class RefreshGate

    Private latest As Integer

    Friend Function Begin() As Integer
        Return Threading.Interlocked.Increment(latest)
    End Function

    Friend Function IsLatest(ticket As Integer) As Boolean
        Return Threading.Volatile.Read(latest) = ticket
    End Function

End Class

Friend Module SettingsRowRefresh

    ''' <summary>
    ''' A row is re-read from the model on a display refresh or a language switch - except a row
    ''' whose control is being edited and holds text that has not been committed to the model yet:
    ''' overwriting it would throw away what the user is typing. (Choice rows apply on selection,
    ''' so they are always in step with the model and are matched by their stable token, never by
    ''' the translated caption.)
    ''' </summary>
    Friend Function ShouldReload(controlHasFocus As Boolean, hasUncommittedEdit As Boolean) As Boolean
        Return Not (controlHasFocus AndAlso hasUncommittedEdit)
    End Function

    ''' <summary>The item to select after the choices were rebuilt: the one with the same token as
    ''' before; the first when the token is gone. Pure, so a language switch is provably not able
    ''' to change a selected value.</summary>
    Friend Function SelectedToken(tokens As IEnumerable(Of String), wanted As String) As Integer
        Dim index As Integer = 0
        For Each token As String In tokens
            If String.Equals(token, wanted, StringComparison.Ordinal) Then Return index
            index += 1
        Next
        Return 0
    End Function

End Module
