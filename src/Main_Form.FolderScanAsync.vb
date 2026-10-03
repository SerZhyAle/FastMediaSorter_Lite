#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Runtime.ExceptionServices
Imports System.Threading
Imports System.Threading.Tasks

' Reading a folder without holding the window (014_SPECIFICATION_VIEWER_CORE_S11_MODERN_ASYNC.md,
' U-03(c)). Modern-only.
'
' The walk of a folder is a round trip per directory page on a share, and it used to run on the
' UI thread: measured on \\p7\_i\output\C (15 779 files, cold) the window stood still for 1.1 s
' every time that folder was opened, re-read or sorted differently.
'
' WHY THE PIPELINE IS NOT MADE ASYNC. ReadShowMediaFile has some thirty callers, several of which
' go on to read the state it has just produced. Turning it into an Async Function would spread
' "Await" through all of them. Instead the asynchrony is locked in ONE place - EnsureFolderList -
' and the pipeline stays what it was, a synchronous walk down a list of cases:
'
'   * the scan starts on the pool and the UI thread gives it a short GRACE. A local disk or a
'     warm share answers well inside it, and then nothing differs from before: the call goes on
'     in the same breath, so every caller that reads state after the call still finds it.
'   * a scan that outlives the grace makes the call return False. The window is live again,
'     the status line says what it is waiting for, and when the listing lands the SAME read mode
'     is run again with is_Continuation:=True - which skips the throttle and the "user
'     navigated" bookkeeping, because it is the second half of the user's one action.
'
' What the scan hands over is the LISTING only, not rows. The extension filter, the sort and the
' metadata table all read state the UI thread owns (all_Supported_Extensions, the sort box), so
' they still run on it, in GetFiles, exactly as before - the scan result is just consumed by
' ProviderFileEntries instead of ListEntries being called there.
'
' Staleness. Every scan is stamped with a ticket (OpenRequestSequence, the same "latest request
' wins" rule the open coordinator uses). A call that replaces the list - another folder, a
' rescan, an external open - invalidates the running scan, so a slow first scan can never land
' on top of the second one. While a scan runs, every OTHER read mode is refused with the status
' line: a flip, a delete or a slideshow tick would act on the list that is about to be thrown
' away, and the continuation re-displays whatever is current anyway.
Partial Public Class Main_Form

    ''' <summary>How long the UI thread waits for a scan before it lets go of the call. Long
    ''' enough that an ordinary local folder or a warm share never takes the asynchronous path
    ''' (the cold 15k-file folder takes 1.1 s, a warm one tens of ms), short enough to be
    ''' below anything a user perceives as a stall.</summary>
    Private Const folder_Scan_Grace_Ms As Integer = 100

    ''' <summary>What a scan produced: the listing, or why there is none.</summary>
    Private NotInheritable Class FolderScanResult
        Public Folder As String
        Public Listing As IReadOnlyList(Of ResourceEntry)
        ''' <summary>The exception the scan died with, rethrown to GetFiles so the failure
        ''' is classified (missing / denied / no connection) by the one code that always did.</summary>
        Public Failure As Exception
        Public IsCancelled As Boolean
    End Class

    ''' <summary>Which scan is the latest. Moved by starting one and by anything that makes the
    ''' running one obsolete.</summary>
    Private ReadOnly folder_Scan_Requests As New OpenRequestSequence()
    Private folder_Scan_Cts As CancellationTokenSource
    Private folder_Scan_Running As Boolean
    ''' <summary>A finished scan waiting for the pipeline to take it - set for the span of one
    ''' ReadShowMediaFile call, cleared by whoever takes it and by the call itself.</summary>
    Private folder_Scan_Ready As FolderScanResult

    ''' <summary>True from the moment a scan outlived its grace until it lands or is superseded.</summary>
    Private Function IsFolderScanRunning() As Boolean
        Return folder_Scan_Running
    End Function

    ''' <summary>
    ''' The one place the pipeline waits for a folder. True: the list can be read now, in this
    ''' same call (either no scan is needed or it finished inside the grace). False: a scan is
    ''' running, ReadShowMediaFile returns, and it will be called again with the same mode when
    ''' the listing lands.
    ''' </summary>
    Private Function EnsureFolderList(read_Mode_Type As String, is_Continuation As Boolean) As Boolean
        ' The second half of a scan that did not fit the grace: the listing is waiting.
        If is_Continuation Then Return True

        ' A call that gets this far either replaces the list (the gate in ReadShowMediaFile lets
        ' nothing else through while a scan runs) or runs with no scan in flight: an older scan
        ' is obsolete in both cases, and so is a listing nobody took.
        CancelFolderScan()

        If Not NavigationRules.ModeReadsFolder(read_Mode_Type, IsFolderListLoaded(), was_External_Input_Previously) Then Return True
        If Not CanScanInBackground() Then Return True

        Dim folder As String = Current_Folder_Path
        Dim ticket As Integer = folder_Scan_Requests.Accept()
        Dim cts As New CancellationTokenSource()
        folder_Scan_Cts = cts
        Dim scan As Task(Of FolderScanResult) = Task.Run(Function() ListFolderInBackground(folder, cts.Token))

        If Task.WaitAny(New Task() {scan}, folder_Scan_Grace_Ms) = 0 Then
            ' Never faulted: ListFolderInBackground keeps every exception in the result.
            folder_Scan_Ready = scan.Result
            folder_Scan_Cts = Nothing
            Return True
        End If

        folder_Scan_Running = True
        lbl_Status.Text = Localization.T("чтение каталога.. ждите!")
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1098: folder scan outlived its grace, window released: " & folder)
        FinishFolderScanAsync(scan, ticket, read_Mode_Type, folder)
        Return False
    End Function

    ''' <summary>Only a plain folder is walked off the UI thread. An archive's entries and a
    ''' resource's listing are already in memory; "-NoBack" is the user's request for no
    ''' background work at all; and with no folder there is nothing to walk.</summary>
    Private Function CanScanInBackground() As Boolean
        If String.IsNullOrEmpty(Current_Folder_Path) Then Return False
        If Is_No_Background_Tasks Then Return False
        If archive_Session IsNot Nothing Then Return False
        If ActiveResourceSession() IsNot Nothing Then Return False
        Return True
    End Function

    ''' <summary>The pool half. Every outcome - listing, cancellation, failure - is a value in
    ''' the result: a task that faulted would have to be observed by someone, and the one
    ''' waiting is not the one that reads the verdict.</summary>
    Private Function ListFolderInBackground(folder As String, token As CancellationToken) As FolderScanResult
        Dim result As New FolderScanResult With {.Folder = folder}
        Try
            result.Listing = local_Provider.ListEntries(folder, token)
        Catch ex As OperationCanceledException
            result.IsCancelled = True
        Catch ex As Exception
            result.Failure = ex
        End Try
        Return result
    End Function

    ''' <summary>The UI half of a scan that did not fit the grace: wait for it without blocking,
    ''' and - only if it is still the latest and still about the folder on screen - run the
    ''' read mode again with its listing in hand.</summary>
    Private Async Sub FinishFolderScanAsync(scan As Task(Of FolderScanResult), ticket As Integer,
                                            read_Mode_Type As String, folder As String)
        Dim result As FolderScanResult = Nothing
        Try
            result = Await scan
        Catch ex As Exception
            ' Cannot happen - see ListFolderInBackground. An Async Sub that threw would take the
            ' process down, so the impossible case is logged rather than trusted.
            AppFileLogger.LogException("Folder scan task faulted", ex)
        End Try

        ' Superseded: whoever superseded it (CancelFolderScan) already owns the state.
        If Not folder_Scan_Requests.IsCurrent(ticket) Then Return

        ' It is the latest, so it is over whatever happens next. The flag comes down FIRST and on
        ' every path below: left up, the gate in ReadShowMediaFile would refuse every flip for a
        ' scan nobody is waiting for any more.
        folder_Scan_Running = False
        folder_Scan_Cts = Nothing
        If result Is Nothing OrElse IsDisposed OrElse is_Form_Closed Then Return

        ' Give the status line back - but only if it is still ours: something may have written
        ' a more useful line while the scan ran.
        If String.Equals(lbl_Status.Text, Localization.T("чтение каталога.. ждите!"), StringComparison.Ordinal) Then
            lbl_Status.Text = ""
        End If

        ' The window has moved to another folder without going through a call that supersedes a
        ' scan (a resource restoring the folder it left, say): this listing is about the old one.
        If Not String.Equals(Current_Folder_Path, folder, StringComparison.OrdinalIgnoreCase) Then Return

        folder_Scan_Ready = result
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1099: folder scan landed, continuing " & read_Mode_Type)
        Try
            ReadShowMediaFile(read_Mode_Type, is_Continuation:=True)
        Catch ex As Exception
            AppFileLogger.LogException("Continuing after the folder scan: " & read_Mode_Type, ex)
        End Try
    End Sub

    ''' <summary>Makes whatever scan is running, and any listing nobody took, obsolete. Safe to
    ''' call at any time, from any place that changes which folder the window is about.</summary>
    Friend Sub CancelFolderScan()
        folder_Scan_Requests.Invalidate()
        folder_Scan_Running = False
        folder_Scan_Ready = Nothing
        Dim cts As CancellationTokenSource = folder_Scan_Cts
        folder_Scan_Cts = Nothing
        If cts IsNot Nothing Then
            Try
                cts.Cancel()
            Catch ex As ObjectDisposedException
                ' Nothing left to cancel.
            End Try
        End If
    End Sub

    ''' <summary>
    ''' The listing a finished scan holds for <paramref name="folder"/>, taken exactly once; Nothing
    ''' when none is waiting (the caller then lists synchronously, as it always did). A scan that
    ''' failed rethrows ITS exception here, on the thread and in the place GetFiles already
    ''' classifies a failed synchronous scan - so "folder not found" and "no connection" read
    ''' the same whichever way the folder was walked.
    ''' </summary>
    Private Function TakeScannedListing(folder As String) As IReadOnlyList(Of ResourceEntry)
        Dim ready As FolderScanResult = folder_Scan_Ready
        folder_Scan_Ready = Nothing
        If ready Is Nothing OrElse ready.IsCancelled Then Return Nothing
        If Not String.Equals(ready.Folder, folder, StringComparison.OrdinalIgnoreCase) Then Return Nothing
        If ready.Failure IsNot Nothing Then ExceptionDispatchInfo.Capture(ready.Failure).Throw()
        Return ready.Listing
    End Function

End Class
#End If
