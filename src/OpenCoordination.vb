Option Strict On

Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>What the filesystem said about an argument (moved out of Main_Form so the
''' ordering policy around it can be exercised without a window).</summary>
Friend NotInheritable Class ArgumentProbe
    Friend Property IsDirectory As Boolean
    Friend Property Exists As Boolean
    Friend Property Missing As Boolean
    Friend Property Denied As Boolean
    Friend Property LastError As String = ""
End Class

''' <summary>The filesystem calls the argument probe makes - injectable so the retry policy
''' can be driven without a share that stopped answering.</summary>
Friend Interface IArgumentFileSystem
    Function DirectoryExists(path As String) As Boolean
    Function FileExists(path As String) As Boolean
    Sub GetAttributes(path As String)
    Sub Sleep(milliseconds As Integer)
End Interface

Friend NotInheritable Class RealArgumentFileSystem
    Implements IArgumentFileSystem

    Friend Shared ReadOnly Instance As New RealArgumentFileSystem()

    Public Function DirectoryExists(path As String) As Boolean Implements IArgumentFileSystem.DirectoryExists
        Return Directory.Exists(path)
    End Function

    Public Function FileExists(path As String) As Boolean Implements IArgumentFileSystem.FileExists
        Return File.Exists(path)
    End Function

    Public Sub GetAttributes(path As String) Implements IArgumentFileSystem.GetAttributes
        File.GetAttributes(path)
    End Sub

    Public Sub Sleep(milliseconds As Integer) Implements IArgumentFileSystem.Sleep
        Thread.Sleep(milliseconds)
    End Sub
End Class

Friend Module ArgumentProbing

    ''' <summary>
    ''' Asks the filesystem about the argument, with the retry policy this app has always
    ''' had: not found = gone, fail fast; access denied = almost always a writer still
    ''' holding the file, three quick retries; any other IO failure = share dropped, retry
    ''' longer. <paramref name="isObsolete"/> is polled before every call and every sleep so
    ''' a request nobody waits for stops retrying; it cannot interrupt a call already blocked.
    ''' </summary>
    Friend Function Probe(path As String, isObsolete As Func(Of Boolean),
                          Optional fs As IArgumentFileSystem = Nothing) As ArgumentProbe
        Dim files As IArgumentFileSystem = If(fs, RealArgumentFileSystem.Instance)
        Dim result As New ArgumentProbe()

        Try
            If files.DirectoryExists(path) Then
                result.IsDirectory = True
                result.Exists = True
                Return result
            End If
        Catch ex As Exception
            result.LastError = FailureText.Describe(ex)
        End Try

        If isObsolete() Then Return result

        result.Exists = files.FileExists(path)
        Dim deniedTries As Integer = 0
        Dim netTries As Integer = 0

        Do While Not result.Exists
            If isObsolete() Then Exit Do
            Try
                files.GetAttributes(path)
                result.Exists = True
            Catch ex As FileNotFoundException
                result.Missing = True : result.LastError = FailureText.Describe(ex) : Exit Do
            Catch ex As DirectoryNotFoundException
                result.Missing = True : result.LastError = FailureText.Describe(ex) : Exit Do
            Catch ex As UnauthorizedAccessException
                result.Denied = True : result.LastError = FailureText.Describe(ex)
                deniedTries += 1
                If deniedTries >= 3 Then Exit Do
                If isObsolete() Then Exit Do
                files.Sleep(250)
            Catch ex As Exception
                result.LastError = FailureText.Describe(ex)
                netTries += 1
                If netTries >= 8 Then Exit Do
                If isObsolete() Then Exit Do
                files.Sleep(250)
            End Try
        Loop

        Return result
    End Function

End Module

Friend Enum OpenKind
    ''' <summary>The user (or a forwarded launch) asked for this path.</summary>
    Explicit = 0
    ''' <summary>Startup "continue where I left off" - the lowest priority.</summary>
    LastFile = 1
End Enum

''' <summary>The identity of one accepted open request.</summary>
Friend NotInheritable Class OpenRequest
    Friend ReadOnly Property Ticket As Integer
    Friend ReadOnly Property Kind As OpenKind
    Friend ReadOnly Property Path As String
    Friend ReadOnly Property NoBack As Boolean

    Friend Sub New(ticket As Integer, kind As OpenKind, path As String, noBack As Boolean)
        Me.Ticket = ticket
        Me.Kind = kind
        Me.Path = path
        Me.NoBack = noBack
    End Sub
End Class

Friend Enum OpenOutcome
    ''' <summary>The probe answered and the host committed it.</summary>
    Applied = 0
    ''' <summary>A newer request or a navigation took over; nothing was changed.</summary>
    Superseded = 1
    ''' <summary>The window had actually closed.</summary>
    Closed = 2
    ''' <summary>A close drain owns the UI; the completion waits for the veto (or is dropped).</summary>
    DeferredByClose = 3
    ''' <summary>The current request got no answer (timeout, queue full, fault).</summary>
    Unavailable = 4
    ''' <summary>A resume was declined because explicit intent already exists.</summary>
    ResumeDeclined = 5
End Enum

''' <summary>What the window gives the coordinator: the UI thread, the lifetime facts and
''' the commit. Everything that touches a control lives behind it.</summary>
Friend Interface IOpenHost
    ''' <summary>Actual FormClosed / disposal - not a cancellable FormClosing.</summary>
    ReadOnly Property IsClosed As Boolean
    ''' <summary>A close drain owns the window (107): no commit may start file work.</summary>
    ReadOnly Property IsCloseDraining As Boolean
    ''' <summary>Runs <paramref name="action"/> on the UI thread.</summary>
    Sub Post(action As Action)
    Sub ShowChecking(request As OpenRequest)
    ''' <summary>UI thread, request still current: commit what the probe found.</summary>
    Sub Apply(request As OpenRequest, probe As ArgumentProbe)
    ''' <summary>UI thread, request still current: it got no answer at all.</summary>
    Sub ReportUnavailable(request As OpenRequest, status As ProbeStatus)
    ''' <summary>Sanitised diagnostic - never changes visible state.</summary>
    Sub Diagnostic(message As String)
End Interface

''' <summary>
''' The ordering policy of "open this path" (ticket 106, 105 L01): a request takes its
''' identity when it is ACCEPTED, its probe goes through the bounded scheduler, and the
''' completion is posted to the UI thread where ownership, window lifetime and close
''' admission are checked AGAIN immediately before anything changes. Check-then-await is
''' never taken as authorisation.
''' </summary>
Friend NotInheritable Class OpenCoordinator

    Private ReadOnly requests As OpenRequestSequence
    Private ReadOnly scheduler As ProbeScheduler(Of ArgumentProbe)
    Private ReadOnly host As IOpenHost
    Private ReadOnly fileSystem As IArgumentFileSystem
    Private ReadOnly owner As New Object()
    Private explicitSinceStartup As Boolean
    Private deferred As Action

    Friend Sub New(requests As OpenRequestSequence, scheduler As ProbeScheduler(Of ArgumentProbe),
                   host As IOpenHost, Optional fileSystem As IArgumentFileSystem = Nothing)
        Me.requests = requests
        Me.scheduler = scheduler
        Me.host = host
        Me.fileSystem = fileSystem
    End Sub

    ''' <summary>True once an explicit open or a navigation has been accepted this run.</summary>
    Friend ReadOnly Property HasExplicitIntent As Boolean
        Get
            Return Volatile.Read(explicitSinceStartup)
        End Get
    End Property

    ''' <summary>Navigation was accepted (key, click, jump). Every pending request is obsolete
    ''' now - not when the new media finishes displaying, which a failed navigation never reaches.</summary>
    Friend Sub NoteNavigation()
        Volatile.Write(explicitSinceStartup, True)
        requests.Invalidate()
    End Sub

    ''' <summary>The window really closed: nothing pending may land, nothing deferred may revive.</summary>
    Friend Sub NoteClosed()
        requests.Invalidate()
        SyncLock Me
            deferred = Nothing
        End SyncLock
        scheduler.CancelOwner(owner)
    End Sub

    Friend ReadOnly Property IsCloseDraining As Boolean
        Get
            Return host.IsCloseDraining
        End Get
    End Property

    ''' <summary>The unlock wait found its file free. It continues the request that started
    ''' it - same ticket, no new Accept - so it can never outrank a request accepted since.</summary>
    Friend Function ContinueUnlocked(ticket As Integer, path As String, noBack As Boolean) As Task(Of OpenOutcome)
        If Not requests.IsCurrent(ticket) Then Return Task.FromResult(OpenOutcome.Superseded)
        Return Run(New OpenRequest(ticket, OpenKind.Explicit, path, noBack))
    End Function

    Friend Function BeginExplicit(path As String, noBack As Boolean) As Task(Of OpenOutcome)
        Volatile.Write(explicitSinceStartup, True)
        Dim request As New OpenRequest(requests.Accept(), OpenKind.Explicit, path, noBack)
        Return Run(request)
    End Function

    ''' <summary>Eligibility is decided BEFORE a ticket is taken, so a late resume cannot take
    ''' the identity away from explicit intent that was accepted first.</summary>
    Friend Function BeginResume(path As String) As Task(Of OpenOutcome)
        If HasExplicitIntent Then Return Task.FromResult(OpenOutcome.ResumeDeclined)
        Dim request As New OpenRequest(requests.Accept(), OpenKind.LastFile, path, False)
        Return Run(request)
    End Function

    Private Function Run(request As OpenRequest) As Task(Of OpenOutcome)
        Try
            host.ShowChecking(request)
        Catch ex As Exception
            host.Diagnostic("ShowChecking failed: " & ex.GetType().Name)
        End Try

        Dim probeTask As Task(Of ProbeResult(Of ArgumentProbe)) =
            scheduler.Submit(owner, request.Path,
                             Function() ArgumentProbing.Probe(request.Path, Function() Not requests.IsCurrent(request.Ticket), fileSystem),
                             Function() requests.IsCurrent(request.Ticket) AndAlso Not host.IsClosed)
        Return Marshal(request, probeTask)
    End Function

    Private Async Function Marshal(request As OpenRequest, probeTask As Task(Of ProbeResult(Of ArgumentProbe))) As Task(Of OpenOutcome)
        Dim result As ProbeResult(Of ArgumentProbe) = Await probeTask.ConfigureAwait(False)
        Dim landed As New TaskCompletionSource(Of OpenOutcome)(TaskCreationOptions.RunContinuationsAsynchronously)
        Try
            host.Post(Sub() landed.TrySetResult(Complete(request, result)))
        Catch ex As Exception
            host.Diagnostic("Post failed: " & ex.GetType().Name)
            landed.TrySetResult(OpenOutcome.Closed)
        End Try
        Return Await landed.Task.ConfigureAwait(False)
    End Function

    ''' <summary>UI thread. Ownership, lifetime and close admission are all asked again here.</summary>
    Private Function Complete(request As OpenRequest, result As ProbeResult(Of ArgumentProbe)) As OpenOutcome
        If host.IsClosed Then Return OpenOutcome.Closed
        If Not requests.IsCurrent(request.Ticket) Then
            ' An obsolete request that failed may leave a sanitised note, never a status.
            If result.Status <> ProbeStatus.Completed AndAlso result.Status <> ProbeStatus.Superseded Then
                host.Diagnostic("Obsolete open ended: " & result.Status.ToString())
            End If
            Return OpenOutcome.Superseded
        End If
        If host.IsCloseDraining Then
            SyncLock Me
                deferred = Sub() Complete(request, result)
            End SyncLock
            Return OpenOutcome.DeferredByClose
        End If

        Select Case result.Status
            Case ProbeStatus.Completed
                host.Apply(request, result.Value)
                Return OpenOutcome.Applied
            Case ProbeStatus.Superseded
                Return OpenOutcome.Superseded
            Case Else
                If result.Failure IsNot Nothing Then host.Diagnostic("Open probe failed: " & result.Failure.GetType().Name)
                host.ReportUnavailable(request, result.Status)
                Return OpenOutcome.Unavailable
        End Select
    End Function

    ''' <summary>A vetoed close: the deferred completion is re-evaluated from scratch, so a
    ''' request that went obsolete meanwhile (or a window that closed) still does nothing.</summary>
    Friend Sub ResumeDeferred()
        Dim action As Action
        SyncLock Me
            action = deferred
            deferred = Nothing
        End SyncLock
        If action IsNot Nothing Then action()
    End Sub

End Class

''' <summary>The wait for a file a writer still holds: the identity is (ticket, path), so two
''' waits for the same path are two waits, and a stale tick can only touch its own.</summary>
Friend NotInheritable Class PendingUnlock

    Friend Structure Capture
        Friend Ticket As Integer
        Friend Path As String
    End Structure

    Private active As Boolean
    Private ticketValue As Integer
    Private pathValue As String
    Private deadlineMs As Long
    Private probeRunning As Boolean
    Private noBackValue As Boolean

    ''' <summary>The -noback flag the original request carried.</summary>
    Friend ReadOnly Property NoBack As Boolean
        Get
            Return noBackValue
        End Get
    End Property

    Friend ReadOnly Property IsActive As Boolean
        Get
            Return active
        End Get
    End Property

    Friend ReadOnly Property Path As String
        Get
            Return pathValue
        End Get
    End Property

    Friend ReadOnly Property Ticket As Integer
        Get
            Return ticketValue
        End Get
    End Property

    Friend Property ProbeInFlight As Boolean
        Get
            Return probeRunning
        End Get
        Set(value As Boolean)
            probeRunning = value
        End Set
    End Property

    Friend Sub Begin(ticket As Integer, path As String, deadline As Long, Optional noBack As Boolean = False)
        active = True
        ticketValue = ticket
        pathValue = path
        deadlineMs = deadline
        probeRunning = False
        noBackValue = noBack
    End Sub

    Friend Function Expired(nowMs As Long) As Boolean
        Return active AndAlso nowMs > deadlineMs
    End Function

    Friend Function Snapshot() As Capture
        Return New Capture With {.Ticket = ticketValue, .Path = pathValue}
    End Function

    ''' <summary>True only while the wait that was captured is still THE wait.</summary>
    Friend Function Owns(captured As Capture) As Boolean
        Return active AndAlso captured.Ticket = ticketValue AndAlso
               String.Equals(captured.Path, pathValue, StringComparison.Ordinal)
    End Function

    Friend Sub Clear()
        active = False
        pathValue = Nothing
        probeRunning = False
    End Sub

End Class
