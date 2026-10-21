#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics
Imports System.Globalization
Imports System.Threading
Imports System.Threading.Tasks

' Spec 242, step T-6b: the ROUTE of "animated graphics through a temporary encrypted video" - the part between
' the finished building blocks that decides what happens in which order and what is left behind when it stops.
'
'   encode (FFmpeg -> memory)  ->  seal (memory -> encrypted container)  ->  play (the video surface)
'
' Nothing here knows a form, a registry value, the log file or a process: the three steps, the fall-back, the
' logger and the progress sink are handed in as delegates (AnimationVideoRouteSteps), so every failure the route
' can meet is a unit test with a fake step. The viewer's glue is src/Main_Form.AnimationVideoRoute.vb.
'
' The promises (spec 242 rules 14, 23, 27):
'   * ONE job at a time. A new request cancels the running one and waits until its encoder has ended and its
'     in-memory video is wiped before it starts its own - there is never a second FFmpeg process, and the
'     fall-back of a failed job runs only after its encoder is gone (a video job and a GIF job never overlap).
'   * A failure - the encoder, the ceiling, the deadline, an empty output, the seal, the player - reports ONE
'     reason code, writes ONE "Animation video failed" line and calls the fall-back EXACTLY once. The player's own
'     failures (the surface logs them itself) are reported to the fall-back without a second line.
'   * A cancel (the user left, the setting went off, the form closes) kills the encoder through the token and
'     leaves no container, no process and no half file; the fall-back is NOT called - nobody is waiting for it.
'   * The encoded video exists only in memory and is wiped (EncodedVideo.Dispose) the moment the seal has read it,
'     before the player is asked to start.
'   * Nothing a delegate throws escapes: not the encoder's, not the seal's and not the fall-back's.

''' <summary>One request to turn the animation at <see cref="SourcePath"/> into a video (rule 10). Plain facts, read
''' by the caller when the job starts.</summary>
Friend NotInheritable Class AnimationVideoRequest
    ''' <summary>The animation, fully qualified (<see cref="AnimationVideoPlan.BuildArguments"/> refuses anything else).</summary>
    Friend ReadOnly SourcePath As String
    ''' <summary>The showing it belongs to (<c>media_Generation</c>).</summary>
    Friend ReadOnly Generation As Integer
    Friend ReadOnly Fps As Integer
    Friend ReadOnly HasAlpha As Boolean
    Friend ReadOnly CanvasWidth As Integer
    Friend ReadOnly CanvasHeight As Integer
    ''' <summary>The picture box's colour as 0xRRGGBB, read when the job starts (rule 9).</summary>
    Friend ReadOnly BackgroundRgb As Integer
    ''' <summary>The source's length for the percentage; 0 = unknown.</summary>
    Friend ReadOnly ExpectedDurationMs As Integer

    Friend Sub New(sourcePath As String, generation As Integer, fps As Integer, hasAlpha As Boolean,
                   canvasWidth As Integer, canvasHeight As Integer, backgroundRgb As Integer, expectedDurationMs As Integer)
        If String.IsNullOrEmpty(sourcePath) Then Throw New ArgumentException("The source path is empty.", NameOf(sourcePath))
        Me.SourcePath = sourcePath
        Me.Generation = generation
        Me.Fps = fps
        Me.HasAlpha = hasAlpha
        Me.CanvasWidth = canvasWidth
        Me.CanvasHeight = canvasHeight
        Me.BackgroundRgb = backgroundRgb
        Me.ExpectedDurationMs = Math.Max(0, expectedDurationMs)
    End Sub
End Class

''' <summary>What a failure is about, in the four words a user can use: the converter, a limit, the temporary store
''' or the player. The viewer maps each to one short phrase of its own; no exception text and no raw code reaches the
''' user (APP-BEHAVIOUR rule 6).</summary>
Friend Enum AnimationVideoReasonKind
    ''' <summary>FFmpeg could not make the video (it failed, or made nothing).</summary>
    Converter
    ''' <summary>The video would be too large or take too long.</summary>
    Limit
    ''' <summary>The temporary encrypted container could not be made.</summary>
    Store
    ''' <summary>LibVLC could not open or play it.</summary>
    Player
End Enum

''' <summary>The reason codes the route itself reports (the player's are <see cref="AnimationVideoSurfaceRules"/>'s) and
''' what they mean. Codes are for the log and for the mapping below, never for the user.</summary>
Friend Module AnimationVideoRouteRules

    Friend Const Reason_Encoder_Failed As String = "encoder"
    Friend Const Reason_Encode_Timeout As String = "encode-timeout"
    Friend Const Reason_Too_Large As String = "too-large"
    Friend Const Reason_Empty As String = "empty"
    Friend Const Reason_Seal_Failed As String = "seal"
    Friend Const Reason_Play_Failed As String = "play"

    ''' <summary>The code of an encode that did not succeed. <see cref="AnimationVideoOutcome.Cancelled"/> is the caller's
    ''' own doing and reaches here only when the route's token was not cancelled, which is a fault of the encoder.</summary>
    Friend Function ReasonOfOutcome(outcome As AnimationVideoOutcome) As String
        Select Case outcome
            Case AnimationVideoOutcome.TimedOut
                Return Reason_Encode_Timeout
            Case AnimationVideoOutcome.OutputTooLarge
                Return Reason_Too_Large
            Case AnimationVideoOutcome.EmptyOutput
                Return Reason_Empty
            Case Else
                Return Reason_Encoder_Failed
        End Select
    End Function

    ''' <summary>Which of the four phrases a code is shown as. Anything the route does not know (a code of the player
    ''' surface, a future one) is the player's.</summary>
    Friend Function ReasonKindOf(reason As String) As AnimationVideoReasonKind
        Select Case reason
            Case Reason_Encoder_Failed, Reason_Empty
                Return AnimationVideoReasonKind.Converter
            Case Reason_Encode_Timeout, Reason_Too_Large
                Return AnimationVideoReasonKind.Limit
            Case Reason_Seal_Failed
                Return AnimationVideoReasonKind.Store
            Case Else
                Return AnimationVideoReasonKind.Player
        End Select
    End Function

End Module

''' <summary>The steps of ONE request, handed in by whoever starts it (the closure carries what the fall-back needs,
''' such as the first-frame picture). Every delegate may be called on a pool thread; marshalling to the UI thread is
''' the caller's job, inside the delegate.</summary>
Friend NotInheritable Class AnimationVideoRouteSteps

    ''' <summary>Runs the encoder; completes only when the process has ended. Production:
    ''' <c>AnimationVideoEncoder.RunAsync</c> with the arguments of <c>AnimationVideoPlan.BuildArguments</c>.</summary>
    Friend ReadOnly Encode As Func(Of AnimationVideoRequest, IProgress(Of Integer), CancellationToken, Task(Of EncodedVideo))

    ''' <summary>Seals the encoded bytes into a lease (<c>AnimationVideoStore.Seal</c>). The route wipes the bytes right after.</summary>
    Friend ReadOnly Seal As Func(Of ReadOnlyMemory(Of Byte), CancellationToken, Task(Of AnimationVideoLease))

    ''' <summary>Plays the lease on the video surface. OWNERSHIP of the lease passes to this delegate (the surface disposes
    ''' it in every outcome). True = it plays; False without calling the callback = stale, nothing to do; False after the
    ''' callback(reason) = failed. The callback may also fire later, once, when the player errs while it plays.</summary>
    Friend ReadOnly Play As Func(Of AnimationVideoRequest, AnimationVideoLease, Action(Of String), Task(Of Boolean))

    ''' <summary>Do what spec 241 does for this file. Called at most once per request, after the encoder has ended, never
    ''' for a cancelled request. The reason code is for the log and for <see cref="AnimationVideoRouteRules.ReasonKindOf"/>.</summary>
    Friend ReadOnly Fallback As Action(Of AnimationVideoRequest, String)

    ''' <summary>One log line (the route's own failures and the success line); optional.</summary>
    Friend ReadOnly Log As Action(Of String)

    ''' <summary>Percent 0..100 while the request is still wanted; optional.</summary>
    Friend ReadOnly Progress As Action(Of Integer)

    Friend Sub New(encode As Func(Of AnimationVideoRequest, IProgress(Of Integer), CancellationToken, Task(Of EncodedVideo)),
                   seal As Func(Of ReadOnlyMemory(Of Byte), CancellationToken, Task(Of AnimationVideoLease)),
                   play As Func(Of AnimationVideoRequest, AnimationVideoLease, Action(Of String), Task(Of Boolean)),
                   fallback As Action(Of AnimationVideoRequest, String),
                   Optional log As Action(Of String) = Nothing,
                   Optional progress As Action(Of Integer) = Nothing)
        If encode Is Nothing Then Throw New ArgumentNullException(NameOf(encode))
        If seal Is Nothing Then Throw New ArgumentNullException(NameOf(seal))
        If play Is Nothing Then Throw New ArgumentNullException(NameOf(play))
        If fallback Is Nothing Then Throw New ArgumentNullException(NameOf(fallback))
        Me.Encode = encode
        Me.Seal = seal
        Me.Play = play
        Me.Fallback = fallback
        Me.Log = log
        Me.Progress = progress
    End Sub
End Class

''' <summary>
''' The one-job-at-a-time runner of the route (see the header of this file). <see cref="Start"/> returns a task that
''' completes when THAT request is completely over (a test awaits it; the viewer lets it run); <see cref="CancelCurrent"/>
''' is what a leave, a setting change and the form closing call.
''' </summary>
Friend NotInheritable Class AnimationVideoRoute
    Implements IDisposable

    Private Const State_Open As Integer = 0
    Private Const State_Failed As Integer = 1
    Private Const State_Cancelled As Integer = 2

    ''' <summary>Everything one request holds. <c>State</c> is the latch that makes "fall-back exactly once, never after a
    ''' cancel" a single compare-and-swap.</summary>
    Private NotInheritable Class Job
        Friend ReadOnly Request As AnimationVideoRequest
        Friend ReadOnly Steps As AnimationVideoRouteSteps
        Friend ReadOnly Cts As New CancellationTokenSource()
        ''' <summary>The previous job's "encoder gone and bytes wiped".</summary>
        Friend ReadOnly Previous As Task
        Friend ReadOnly EncodeStageDone As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Friend ReadOnly Finished As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Friend State As Integer

        Friend Sub New(request As AnimationVideoRequest, steps As AnimationVideoRouteSteps, previous As Task)
            Me.Request = request
            Me.Steps = steps
            Me.Previous = previous
        End Sub
    End Class

    Private NotInheritable Class JobProgress
        Implements IProgress(Of Integer)

        Private ReadOnly job As Job

        Friend Sub New(job As Job)
            Me.job = job
        End Sub

        Public Sub Report(value As Integer) Implements IProgress(Of Integer).Report
            If Volatile.Read(job.State) <> State_Open Then Return
            Dim sink As Action(Of Integer) = job.Steps.Progress
            If sink Is Nothing Then Return
            Try
                sink(Math.Max(0, Math.Min(100, value)))
            Catch
                ' a progress sink is cosmetic
            End Try
        End Sub
    End Class

    Private ReadOnly gate As New Object()
    Private current As Job
    Private disposed As Boolean

    ''' <summary>A request is still being encoded or sealed (its encoder may be running, its bytes may still be held).</summary>
    Friend ReadOnly Property IsBusy As Boolean
        Get
            SyncLock gate
                Return current IsNot Nothing AndAlso Not current.EncodeStageDone.Task.IsCompleted
            End SyncLock
        End Get
    End Property

    ''' <summary>
    ''' Starts a request. The running one, if any, is cancelled and this one waits for its encoder to be gone first.
    ''' Returns the task of THIS request; it never faults. A disposed route returns a completed task and does nothing.
    ''' </summary>
    Friend Function Start(request As AnimationVideoRequest, steps As AnimationVideoRouteSteps) As Task
        If request Is Nothing Then Throw New ArgumentNullException(NameOf(request))
        If steps Is Nothing Then Throw New ArgumentNullException(NameOf(steps))

        Dim job As Job
        SyncLock gate
            If disposed Then Return Task.CompletedTask
            Dim previous As Job = current
            job = New Job(request, steps, If(previous Is Nothing, Task.CompletedTask, previous.EncodeStageDone.Task))
            If previous IsNot Nothing Then CancelJob(previous)
            current = job
        End SyncLock

        Task.Run(Function() RunAsync(job))
        Return job.Finished.Task
    End Function

    ''' <summary>The user left, the setting went off or the form closes: cancels the running request (no fall-back). The
    ''' returned task completes when its encoder has ended and its in-memory video is wiped - for a caller that must know
    ''' before it deletes a container. A request already playing is stopped by the viewer's surface, not here.</summary>
    Friend Function CancelCurrent() As Task
        Dim job As Job
        SyncLock gate
            job = current
        End SyncLock
        If job Is Nothing Then Return Task.CompletedTask
        CancelJob(job)
        Return job.EncodeStageDone.Task
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Dim job As Job
        SyncLock gate
            If disposed Then Return
            disposed = True
            job = current
        End SyncLock
        If job IsNot Nothing Then CancelJob(job)
    End Sub

    ' --- one request --------------------------------------------------------------------------------------

    Private Shared Sub CancelJob(job As Job)
        Interlocked.CompareExchange(job.State, State_Cancelled, State_Open)
        Try
            job.Cts.Cancel()
        Catch ex As ObjectDisposedException
            ' the job is over; there is nothing left to stop
        End Try
    End Sub

    Private Shared Async Function RunAsync(job As Job) As Task
        Try
            Try
                Await job.Previous.ConfigureAwait(False)
            Catch
                ' the previous job's gate is a result, never a fault
            End Try
            ' Superseded or left before its turn: nothing was started.
            If Volatile.Read(job.State) <> State_Open Then Return
            Await RunStagesAsync(job).ConfigureAwait(False)
        Catch ex As Exception
            ' Nothing escapes a job nobody awaits. The user must not be left looking at "preparing", so it is a failure.
            WriteLog(job.Steps, "Animation video route fault: " & FailureText.Describe(ex))
            Fail(job, AnimationVideoRouteRules.Reason_Encoder_Failed, True)
        Finally
            job.EncodeStageDone.TrySetResult(True)
            job.Finished.TrySetResult(True)
            Try
                job.Cts.Dispose()
            Catch
            End Try
        End Try
    End Function

    Private Shared Async Function RunStagesAsync(job As Job) As Task
        Dim token As CancellationToken = job.Cts.Token
        Dim request As AnimationVideoRequest = job.Request
        Dim steps As AnimationVideoRouteSteps = job.Steps
        Dim clock As Stopwatch = Stopwatch.StartNew()
        Dim lease As AnimationVideoLease = Nothing
        Dim reason As String = Nothing

        Dim encoded As EncodedVideo = Nothing
        Try
            Try
                encoded = Await steps.Encode(request, New JobProgress(job), token).ConfigureAwait(False)
            Catch ex As OperationCanceledException When token.IsCancellationRequested
                Return
            Catch ex As Exception
                reason = AnimationVideoRouteRules.Reason_Encoder_Failed
                WriteLog(steps, "Animation video encoder fault: " & FailureText.Describe(ex))
            End Try

            If reason Is Nothing Then
                ' The user left while the encoder was finishing: whatever it made is not wanted.
                If token.IsCancellationRequested Then Return
                If encoded Is Nothing Then
                    reason = AnimationVideoRouteRules.Reason_Encoder_Failed
                ElseIf encoded.Outcome <> AnimationVideoOutcome.Succeeded Then
                    reason = AnimationVideoRouteRules.ReasonOfOutcome(encoded.Outcome)
                    If Not String.IsNullOrEmpty(encoded.Detail) Then
                        WriteLog(steps, "Animation video encoder: " & encoded.Outcome.ToString() & ": " & encoded.Detail)
                    End If
                End If
            End If

            If reason Is Nothing Then
                Try
                    lease = Await steps.Seal(encoded.Bytes, token).ConfigureAwait(False)
                    If lease Is Nothing Then reason = AnimationVideoRouteRules.Reason_Seal_Failed
                Catch ex As OperationCanceledException When token.IsCancellationRequested
                    Return
                Catch ex As Exception
                    reason = AnimationVideoRouteRules.Reason_Seal_Failed
                    WriteLog(steps, "Animation video seal fault: " & FailureText.Describe(ex))
                End Try
            End If
        Finally
            ' Rule 27 and the header: the plaintext video does not outlive the seal, whatever the seal did.
            encoded?.Dispose()
            job.EncodeStageDone.TrySetResult(True)
        End Try

        If token.IsCancellationRequested Then
            DisposeQuietly(lease)
            Return
        End If
        If reason IsNot Nothing Then
            DisposeQuietly(lease)
            Fail(job, reason, True)
            Return
        End If

        WriteLog(steps, "Animation video prepared: " & request.SourcePath & " (" &
                 lease.Length.ToString(CultureInfo.InvariantCulture) & " bytes, " &
                 clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) & " ms)")

        Try
            ' The surface owns the lease from here. True: it plays, and a later failure arrives through the callback.
            ' False without the callback: the showing changed meanwhile, there is nothing to do.
            Await steps.Play(request, lease, Sub(why As String) Fail(job, If(String.IsNullOrEmpty(why), AnimationVideoRouteRules.Reason_Play_Failed, why), False)).ConfigureAwait(False)
        Catch ex As Exception
            ' Whether the delegate took the lease before it threw is unknown; a lease is safe to dispose twice.
            DisposeQuietly(lease)
            WriteLog(steps, "Animation video play fault: " & FailureText.Describe(ex))
            Fail(job, AnimationVideoRouteRules.Reason_Play_Failed, True)
        End Try
    End Function

    ''' <summary>The one door to the fall-back: only while the request is open (not cancelled, not already failed),
    ''' exactly once, and whatever the fall-back throws stays here.</summary>
    Private Shared Sub Fail(job As Job, reason As String, writeLine As Boolean)
        If Interlocked.CompareExchange(job.State, State_Failed, State_Open) <> State_Open Then Return
        If writeLine Then WriteLog(job.Steps, "Animation video failed: " & reason & ": " & job.Request.SourcePath)
        Try
            job.Steps.Fallback(job.Request, reason)
        Catch ex As Exception
            WriteLog(job.Steps, "Animation video fall-back failed: " & FailureText.Describe(ex))
        End Try
    End Sub

    Private Shared Sub WriteLog(steps As AnimationVideoRouteSteps, message As String)
        Dim sink As Action(Of String) = steps.Log
        If sink Is Nothing Then Return
        Try
            sink(message)
        Catch
            ' a logger never decides the route
        End Try
    End Sub

    Private Shared Sub DisposeQuietly(part As IDisposable)
        If part Is Nothing Then Return
        Try
            part.Dispose()
        Catch
        End Try
    End Sub

End Class
#End If
