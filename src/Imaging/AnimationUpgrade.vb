#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Threading

''' <summary>Where a long animation stands on screen (spec 241 rule 13).</summary>
Friend Enum AnimationUpgradePhase
    ''' <summary>No stand-in picture is on screen.</summary>
    None
    ''' <summary>The first frame is on screen and the whole animation is being prepared.</summary>
    Preparing
    ''' <summary>The first frame stays because the user changed it in place (turned it): the prepared
    ''' animation would undo that, so it is not swapped in.</summary>
    Held
    ''' <summary>Preparing failed; the first frame stays.</summary>
    Failed
    ''' <summary>The animation is over the decode budget: the first frame is all there will be.</summary>
    TooLong
    ''' <summary>The animation replaced the first frame.</summary>
    Landed
End Enum

''' <summary>
''' The transitions of the stand-in picture of a long animation (spec 241 rule 13), pure so they can
''' be tested without a form. The form owns one of these and asks it, never the other way round.
'''
''' Every landing and every failure is for ONE showing of the file: <c>generation</c> is the viewer's
''' <c>media_Generation</c> when the first frame was put up, so a result that belongs to a file the
''' user has since left is refused here, whatever order things arrive in.
''' </summary>
Friend NotInheritable Class AnimationUpgradeState

    Friend ReadOnly Property Phase As AnimationUpgradePhase = AnimationUpgradePhase.None
    Friend ReadOnly Property Generation As Integer
    Friend ReadOnly Property FilePath As String = ""

    ''' <summary>True while a first-frame stand-in is the picture on screen: the file is an animation that
    ''' cannot play yet, so what only suits a still picture (the editor, OCR) stays hidden.</summary>
    Friend ReadOnly Property IsPreviewShown As Boolean
        Get
            Return Phase = AnimationUpgradePhase.Preparing OrElse Phase = AnimationUpgradePhase.Held OrElse
                   Phase = AnimationUpgradePhase.Failed OrElse Phase = AnimationUpgradePhase.TooLong
        End Get
    End Property

    ''' <summary>A first frame was put on screen for <paramref name="path"/>. Whatever came before is over.</summary>
    Friend Sub ShowPreview(generation As Integer, path As String, tooLong As Boolean)
        _Generation = generation
        _FilePath = If(path, "")
        _Phase = If(tooLong, AnimationUpgradePhase.TooLong, AnimationUpgradePhase.Preparing)
    End Sub

    ''' <summary>The prepared animation arrived. True (and the phase Landed) only when the first frame of
    ''' that very showing is still being waited on.</summary>
    Friend Function TryLand(generation As Integer) As Boolean
        If Phase <> AnimationUpgradePhase.Preparing OrElse generation <> Me.Generation Then Return False
        _Phase = AnimationUpgradePhase.Landed
        Return True
    End Function

    ''' <summary>Preparing failed. True only for the showing still waiting on it.</summary>
    Friend Function TryFail(generation As Integer) As Boolean
        If Phase <> AnimationUpgradePhase.Preparing OrElse generation <> Me.Generation Then Return False
        _Phase = AnimationUpgradePhase.Failed
        Return True
    End Function

    ''' <summary>The user changed the first frame in place; the animation is no longer swapped in.</summary>
    Friend Sub Hold()
        If Phase = AnimationUpgradePhase.Preparing Then _Phase = AnimationUpgradePhase.Held
    End Sub

    ''' <summary>The picture is gone from the screen (navigation, a file operation, closing).</summary>
    Friend Sub Leave()
        _Phase = AnimationUpgradePhase.None
        _FilePath = ""
    End Sub

End Class

''' <summary>
''' The single place where long animations are prepared (spec 241 rule 11): ONE dedicated worker, at
''' most one job running in the whole process, and while it runs only the NEWEST waiting request is
''' kept - the file on screen always wins. Not the thread pool: an animation job lasts seconds and
''' must not starve the pool that the display path and the prefetch share.
'''
''' Generic and delegate-driven so the rules above are testable with a fake job. A running job is
''' never interrupted (the decoder offers no way to); it finishes and its result goes to
''' <c>finished</c>, which owns it from then on - and must dispose it if nobody wants it.
''' </summary>
Friend NotInheritable Class AnimationUpgradeRunner(Of TJob As Class, TResult As Class)

    Private ReadOnly work As Func(Of TJob, TResult)
    Private ReadOnly isWanted As Func(Of TJob, Boolean)
    Private ReadOnly finished As Action(Of TJob, TResult, Exception)
    Private ReadOnly gate As New Object()
    Private running As Boolean
    Private waiting As TJob

    ''' <param name="work">The job itself; runs on the worker.</param>
    ''' <param name="isWanted">Asked just before a job starts: a job nobody is waiting for any more is
    ''' dropped without being run. Must be safe from any thread.</param>
    ''' <param name="finished">Takes over the result (or the exception) of a job that ran, on the worker.
    ''' Never called for a dropped job.</param>
    Friend Sub New(work As Func(Of TJob, TResult), isWanted As Func(Of TJob, Boolean), finished As Action(Of TJob, TResult, Exception))
        Me.work = work
        Me.isWanted = isWanted
        Me.finished = finished
    End Sub

    ''' <summary>True while a job is running or waiting.</summary>
    Friend ReadOnly Property IsBusy As Boolean
        Get
            SyncLock gate
                Return running
            End SyncLock
        End Get
    End Property

    ''' <summary>Queues a job. If one is running, this one waits and replaces any job that was already
    ''' waiting.</summary>
    Friend Sub Submit(job As TJob)
        If job Is Nothing Then Return
        SyncLock gate
            If running Then
                waiting = job
                Return
            End If
            running = True
        End SyncLock
        Dim worker As New Thread(AddressOf RunLoop) With {.IsBackground = True, .Name = "FMS animation upgrade"}
        worker.Start(job)
    End Sub

    Private Sub RunLoop(first As Object)
        Dim job As TJob = DirectCast(first, TJob)
        Do While job IsNot Nothing
            Dim wanted As Boolean = False
            Try
                wanted = isWanted(job)
            Catch ex As Exception
                wanted = False
            End Try
            If wanted Then
                Dim result As TResult = Nothing
                Dim failure As Exception = Nothing
                Try
                    result = work(job)
                Catch ex As Exception
                    failure = ex
                End Try
                Try
                    finished(job, result, failure)
                Catch ex As Exception
                    ' finished owns the result and must not throw; if it does, nothing more can be done
                    ' for this job, and the worker must still go on with the next.
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " animation upgrade: finished failed: " & ex.Message)
                End Try
            End If
            SyncLock gate
                job = waiting
                waiting = Nothing
                If job Is Nothing Then
                    running = False
                    Return
                End If
            End SyncLock
        Loop
    End Sub

End Class
#End If
