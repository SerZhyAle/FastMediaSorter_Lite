Option Strict On

Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Globalization
Imports System.Windows.Forms

' The "Загрузка.." badge shown over the media surface while a slow image decodes.
'
' WHY IT IS BUILT THIS WAY - the obvious two approaches both fail:
'
' 1. "Set lbl_Status before the decode." The decode (LoadImageWithStream) is called
'    straight from the display pipeline ON THE UI THREAD, so while it runs there is
'    no thread left to paint anything - which is the whole bug: a big WEBP goes
'    through the fully-managed ImageSharp path (seconds, not the milliseconds GDI+
'    takes on a JPEG), the window stops repainting, the PREVIOUS file stays on
'    screen, and the app reads as hung.
' 2. "Show the badge unconditionally, up front." Nothing can know in advance which
'    file will be slow, and flashing a badge for 15 ms on every fast file is worse
'    than saying nothing.
'
' So the decode moves to a pool thread - where it already runs for the prefetch
' worker (Main_Form.FileScanning.vb calls the very same LoadImageWithStream), so
' this is not a new threading model - and the UI thread waits on it in short
' slices. Only once the wait passes loading_Badge_Delay_Ms does the badge go up;
' anything faster than that never builds one, so nothing flickers.
'
' The wait deliberately does NOT pump the message queue (no Application.DoEvents):
' the badge is painted by asking the control itself to repaint, which sends WM_PAINT
' straight to that window without dispatching input. That matters - DoEvents here
' would let a keypress reenter ReadShowMediaFile from inside the load of the file it
' is displaying. So LoadStandardImageInPictureBox stays synchronous and everything
' around it keeps running in exactly the order it always has; the window is still
' busy, it just finally says what it is busy with.
Partial Public Class Main_Form

    ''' <summary>How long a decode may take before the user is told anything at all.
    ''' Below this the badge would only flash - most files decode in a few ms.</summary>
    Private Const loading_Badge_Delay_Ms As Integer = 250

    ''' <summary>How often the spinner and the elapsed counter are redrawn while we
    ''' wait - also the granularity with which we notice the decode has landed.</summary>
    Private Const loading_Badge_Frame_Ms As Integer = 120

    ''' <summary>
    ''' How long a single decode may hold the window before we give up on it.
    '''
    ''' The wait deliberately does not pump messages (see the file header - pumping would let
    ''' a keypress reenter the load of the file being displayed), so with no ceiling the
    ''' window was hostage to the decode: a synchronous read on a dead SMB session blocks for
    ''' the redirector timeout PER BLOCK, which on a multi-megabyte file is minutes of a
    ''' window that takes no keys, no mouse and no close button. Past this the file is
    ''' reported unreadable - the same path a corrupt file takes, which auto-skips and carries
    ''' on - and the abandoned decode's result is released if it ever arrives.
    ''' </summary>
    Private Const loading_Decode_Deadline_Ms As Integer = 20000

    Private loading_Badge As Label
    Private loading_Badge_Path As String = Nothing
    Private loading_Badge_Last_Sec As Integer = -1
    Private loading_Badge_Cached_Percent As Integer = 0
    Private loading_Badge_Cached_Remaining_Sec As Integer = -1

    ''' <summary>
    ''' LoadImageWithStream, but it tells the user when it is taking its time.
    ''' Drop-in: same arguments, same result (Nothing on failure - the decoder
    ''' swallows its own errors), same thread by the time it returns.
    ''' </summary>
    ''' <param name="failure_Kind">Why the result is Nothing, when it is. Two very different
    ''' things arrive here as one null: a file that is not a picture (Content) and a decode
    ''' abandoned at its deadline because the read blocked on a dead SMB session (Transport).
    ''' The caller drops the first from the list and must not drop the second - which is what
    ''' the header above describes doing, and why it needed a second value to say it with.</param>
    Private Function LoadImageWithProgress(file_Path As String, file_Size_Bytes As Long,
                                           ByRef failure_Kind As PathFailureKind) As Tuple(Of Image, IO.MemoryStream)
        failure_Kind = PathFailureKind.Content

        ' Started before the task, so the counter shown to the user is the wait they
        ' have actually sat through, not the wait since the badge appeared.
        Dim watch As Stopwatch = Stopwatch.StartNew()

        ' The path travels as an argument, never as a global read from the pool
        ' thread: the user goes on flipping and Current_File_Name moves under us.
        ' The loader says WHY it returned Nothing (audit 048, A-06): a locked or access-denied
        ' file is not "not a picture" - carried in the outcome, so a caller that JOINS an
        ' abandoned decode gets the verdict along with the pixels.
        '
        ' One decode per path (audit 057, S01-6): a decode of this file abandoned at an
        ' earlier visit's deadline and still running is taken over instead of a second one
        ' started beside it. It is handed over, never shared - the registry decides under
        ' one lock whether the late result goes to this caller or gets released, so the
        ' Image and its stream keep exactly one owner (see InFlightDecodeRegistry).
        Dim lease As InFlightDecodeRegistry(Of DecodeOutcome).Lease =
            in_Flight_Decodes.Acquire(file_Path, DecodeGroup(file_Path),
                                      Function() System.Threading.Tasks.Task.Run(Function() DecodeForDisplay(file_Path)))
        Select Case lease.Admission
            Case DecodeAdmission.Refused, DecodeAdmission.Busy
                ' Too many decodes of this volume are already stuck: starting another would
                ' park one more pool thread in the same dead read. Kept in the list, with the
                ' sentence the deadline would have produced - just without the 20 s wait.
                failure_Kind = DeadlineFailureKind(file_Path)
                AppFileLogger.WriteLine("Decode not started (" & lease.Admission.ToString() & ", " &
                                        in_Flight_Decodes.AbandonedCount.ToString() & " abandoned): " & file_Path)
                Return Nothing
            Case DecodeAdmission.Joined
                AppFileLogger.WriteLine("Decode joined (still running from an earlier visit): " & file_Path)
        End Select
        Dim decode As System.Threading.Tasks.Task(Of DecodeOutcome) = lease.Task

        ' The common case - no badge is ever built and nothing on screen moves.
        If decode.Wait(loading_Badge_Delay_Ms) Then
            Return TakeOutcome(decode, failure_Kind)
        End If

        Dim saved_Status As String = ShowLoadingStatus(file_Path)
        Dim result As Tuple(Of Image, IO.MemoryStream)
        Dim gave_Up As Boolean = False
        Try
            Dim frame As Integer = 0
            Do
                ShowLoadingBadge(file_Path, file_Size_Bytes, watch.Elapsed, frame)
                frame += 1
                If watch.ElapsedMilliseconds >= loading_Decode_Deadline_Ms Then
                    gave_Up = True
                    Exit Do
                End If
            Loop Until decode.Wait(loading_Badge_Frame_Ms)

            If gave_Up Then
                ' Nothing can cancel a blocking file read, so the task is abandoned rather
                ' than waited on - with a continuation that releases whatever it eventually
                ' produces, or the Image and its MemoryStream would leak.
                '
                ' Twenty seconds without a first block is not a verdict on the file: it is
                ' the redirector timing out per block on a session that is gone. Saying so
                ' is what keeps the file - and the other 199 behind it - in the list.
                failure_Kind = DeadlineFailureKind(file_Path)
                AbandonDecode(lease, file_Path, watch.ElapsedMilliseconds)
                Return Nothing
            End If

            result = TakeOutcome(decode, failure_Kind)
        Finally
            HideLoadingBadge()
            RestoreLoadingStatus(saved_Status)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0912: slow decode took " &
                            watch.ElapsedMilliseconds.ToString() & " ms: " & file_Path)
        End Try
        Return result
    End Function

    ''' <summary>The loader's own verdict as the display path's failure kind. None cannot
    ''' arrive with a Nothing result; if it ever did, it is the historical Content.</summary>
    Private Shared Function LoaderFailureKind(reported As PathFailureKind) As PathFailureKind
        If reported = PathFailureKind.None Then Return PathFailureKind.Content
        Return reported
    End Function

    ''' <summary>
    ''' What a decode abandoned at its deadline says (audit 048, A-18). Twenty seconds without
    ''' a result on a share is the redirector timing out per block - Transport. On a local,
    ''' ready volume there is no redirector: the decode itself is slow (a huge animated WEBP
    ''' or HEIC on a slow CPU), and "no connection to the folder" was a false sentence. That
    ''' is SlowDecode - the file stays in the list either way, since the abandoned decode may
    ''' still finish and fill the decode cache. The x86 build keeps Transport (it ignores the
    ''' kind anyway, invariant 10 of 011).
    ''' </summary>
    Private Function DeadlineFailureKind(file_Path As String) As PathFailureKind
#If NETFRAMEWORK Then
        Return PathFailureKind.Transport
#Else
        Return If(IsOnLocalReadyVolume(file_Path), PathFailureKind.SlowDecode, PathFailureKind.Transport)
#End If
    End Function

    ''' <summary>What one display decode produced: the picture (or Nothing) and, when
    ''' Nothing, the loader's own reason.</summary>
    Private NotInheritable Class DecodeOutcome
        Public Loaded As Tuple(Of Image, IO.MemoryStream)
        Public Failure As PathFailureKind = PathFailureKind.Content
    End Class

    ''' <summary>How many decodes of one volume may be abandoned and still running before a
    ''' request for a NEW file there is refused outright (audit 057, S01-6). Each one is a
    ''' pool thread parked in a read (or a CPU busy with a huge HEIC); four is below the
    ''' pool's minimum on any machine this runs on, so the healthy decode that comes next
    ''' still gets a thread at once.</summary>
    Private Const abandoned_Decode_Cap As Integer = 4

    ''' <summary>The display path's running decodes, one per path, for the whole process
    ''' (every window decodes on the one UI thread and shares the one thread pool).</summary>
    Private Shared ReadOnly in_Flight_Decodes As New InFlightDecodeRegistry(Of DecodeOutcome)(
        AddressOf ReleaseDecodeOutcome, abandoned_Decode_Cap)

    ''' <summary>The pool-thread half: decode and keep the loader's verdict with the result.</summary>
    Private Shared Function DecodeForDisplay(file_Path As String) As DecodeOutcome
        Dim outcome As New DecodeOutcome()
        Dim reported As PathFailureKind = PathFailureKind.Content
#If NETFRAMEWORK Then
        outcome.Loaded = LoadImageWithStream(file_Path, reported)
#Else
        ' A long animation arrives as its first frame (spec 241); the viewer upgrades it afterwards.
        outcome.Loaded = LoadImageStaged(file_Path, reported, ImageLoadStage.Staged)
#End If
        outcome.Failure = reported
        Return outcome
    End Function

    ''' <summary>The result of a completed decode, now owned by the caller.</summary>
    Private Shared Function TakeOutcome(decode As System.Threading.Tasks.Task(Of DecodeOutcome),
                                        ByRef failure_Kind As PathFailureKind) As Tuple(Of Image, IO.MemoryStream)
        ' GetResult, not .Result: it rethrows what the decode actually threw
        ' instead of an AggregateException wrapping it, so the caller's
        ' ArgumentException / OutOfMemoryException catches still match.
        Dim outcome As DecodeOutcome = decode.GetAwaiter().GetResult()
        Dim loaded As Tuple(Of Image, IO.MemoryStream) = If(outcome Is Nothing, Nothing, outcome.Loaded)
        If loaded Is Nothing Then failure_Kind = LoaderFailureKind(If(outcome Is Nothing, PathFailureKind.Content, outcome.Failure))
        Return loaded
    End Function

    ''' <summary>Disposes a result nobody owns any more - an abandoned decode that landed.</summary>
    Private Shared Sub ReleaseDecodeOutcome(outcome As DecodeOutcome)
        Dim late As Tuple(Of Image, IO.MemoryStream) = outcome.Loaded
        If late Is Nothing Then Return
        late.Item1?.Dispose()
        late.Item2?.Dispose()
    End Sub

    ''' <summary>What the abandoned-decode cap counts by: the volume root (C:\, or
    ''' \\server\share), so a dead share cannot refuse a local file its decode.</summary>
    Private Shared Function DecodeGroup(file_Path As String) As String
        Try
            Return If(Path.GetPathRoot(file_Path), "")
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Lets go of a decode that outran its deadline. Its result is released
    ''' whenever (if ever) it lands - unless the user comes back to the file first, in
    ''' which case that visit takes the decode over instead of starting another.</summary>
    Private Shared Sub AbandonDecode(lease As InFlightDecodeRegistry(Of DecodeOutcome).Lease,
                                     file_Path As String,
                                     waited_Ms As Long)
        AppFileLogger.WriteLine("Decode abandoned after " & waited_Ms.ToString() & " ms: " & file_Path)
        lease.Abandon()
    End Sub

    ''' <summary>True when it is safe to touch controls from here at all. The badge is
    ''' a courtesy - if the form has no handle yet (the first file opens inside
    ''' Form1_Load) or we somehow got here off the UI thread, skip it and just decode:
    ''' a missing badge is a cosmetic loss, a cross-thread control touch is a crash.</summary>
    Private Function CanShowLoadingBadge() As Boolean
        If Me.IsDisposed Then Return False
        If Not Me.IsHandleCreated Then Return False
        If Me.InvokeRequired Then Return False
        Return True
    End Function

    ''' <summary>Puts "Загрузка: file" in the status bar and returns what was there
    ''' before, for RestoreLoadingStatus. Nothing = we did not touch it.
    '''
    ''' It has to be put BACK, not cleared: the line already on screen is often the
    ''' announcement of the very jump that brought us here ("+100 файлов", "не удалось
    ''' загрузить X"), and those are meant to stay up.</summary>
    Private Function ShowLoadingStatus(file_Path As String) As String
        If Not CanShowLoadingBadge() OrElse lbl_Status Is Nothing Then Return Nothing

        Dim previous_Text As String = lbl_Status.Text
        Try
            lbl_Status.Text = Localization.TF("Загрузка файла: {0}", Path.GetFileName(file_Path))
            lbl_Status.Update()
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0913: loading status skipped: " & ex.Message)
        End Try
        Return previous_Text
    End Function

    Private Sub RestoreLoadingStatus(saved_Status As String)
        If saved_Status Is Nothing OrElse lbl_Status Is Nothing Then Return
        Try
            lbl_Status.Text = saved_Status
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0914: loading status restore skipped: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Shows (building it on first use) and repaints the badge. Safe to call
    ''' on every frame - only the text changes after the first one.</summary>
    Private Sub ShowLoadingBadge(file_Path As String, file_Size_Bytes As Long, elapsed As TimeSpan, frame As Integer, Optional loaded_Bytes As Long = -1L)
        If Not CanShowLoadingBadge() Then Return
        If panel_Media Is Nothing OrElse panel_Media.IsDisposed Then Return

        Try
            If loading_Badge Is Nothing Then
                ' BackColor is opaque on purpose: the badge sits over a sibling picture
                ' box, and WinForms transparency only ever composites against the
                ' PARENT - a translucent back colour would show the panel, not the photo.
                loading_Badge = New Label With {
                    .Name = "loading_Badge",
                    .AutoSize = True,
                    .TextAlign = ContentAlignment.MiddleCenter,
                    .BackColor = Color.FromArgb(24, 24, 24),
                    .ForeColor = Color.White,
                    .BorderStyle = BorderStyle.FixedSingle,
                    .Font = New Font("Segoe UI", 11.0F, FontStyle.Regular),
                    .Padding = New Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(12),
                                           LogicalToDeviceUnits(16), LogicalToDeviceUnits(12)),
                    .Visible = False
                }
                panel_Media.Controls.Add(loading_Badge)
            End If

            loading_Badge.Text = LoadingBadgeText(file_Path, file_Size_Bytes, elapsed, frame, loaded_Bytes)
            loading_Badge.Location = New Point(
                Math.Max(0, (panel_Media.ClientSize.Width - loading_Badge.Width) \ 2),
                Math.Max(0, (panel_Media.ClientSize.Height - loading_Badge.Height) \ 2))

            If Not loading_Badge.Visible Then
                loading_Badge.Visible = True
                loading_Badge.BringToFront()
            End If

            ' The one paint that matters. We are on the UI thread and about to block on
            ' the decode again, so nobody else will service this control's WM_PAINT -
            ' Update() delivers it now, without pumping the queue (see the file header).
            loading_Badge.Update()
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0915: loading badge skipped: " & ex.Message)
        End Try
    End Sub

    Private Sub ResetLoadingBadgeState()
        loading_Badge_Path = Nothing
        loading_Badge_Last_Sec = -1
        loading_Badge_Cached_Percent = 0
        loading_Badge_Cached_Remaining_Sec = -1
    End Sub

    Private Sub HideLoadingBadge()
        ResetLoadingBadgeState()
        If loading_Badge Is Nothing Then Return
        Try
            ' Left built for the next slow file; hiding invalidates the area under it,
            ' which repaints normally once the caller is back at the message loop.
            If loading_Badge.Visible Then loading_Badge.Visible = False
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0916: loading badge hide skipped: " & ex.Message)
        End Try
    End Sub

    Private Sub UpdateLoadingBadgeMetrics(file_Path As String, file_Size_Bytes As Long, elapsed As TimeSpan, loaded_Bytes As Long)
        If Not String.Equals(loading_Badge_Path, file_Path, StringComparison.OrdinalIgnoreCase) Then
            loading_Badge_Path = file_Path
            loading_Badge_Last_Sec = -1
            loading_Badge_Cached_Percent = 0
            loading_Badge_Cached_Remaining_Sec = -1
        End If

        LoadingIndicatorRules.UpdateMetricsOncePerSecond(
            loaded_Bytes, file_Size_Bytes, elapsed,
            loading_Badge_Last_Sec, loading_Badge_Cached_Percent, loading_Badge_Cached_Remaining_Sec)
    End Sub

    ''' <summary>What the badge says: the file, why it is slow (its size), how long it
    ''' has been going or its percent and estimated remaining time, and a spinner that
    ''' proves the app is alive rather than wedged.</summary>
    Private Function LoadingBadgeText(file_Path As String,
                                      file_Size_Bytes As Long,
                                      elapsed As TimeSpan,
                                      frame As Integer,
                                      Optional loaded_Bytes As Long = -1L) As String
        ' Plain ASCII: every font has it, and this text can land on any machine.
        Const spinner As String = "|/-\"
        Dim tick As String = spinner.Substring(frame Mod spinner.Length, 1)

        Dim head As String = Localization.TF("Загрузка.. {0}", tick)
        Dim fileName As String = Path.GetFileName(file_Path)
        If String.IsNullOrEmpty(fileName) Then fileName = file_Path

        Dim line3 As String
        If loaded_Bytes >= 0 AndAlso file_Size_Bytes > 0 Then
            UpdateLoadingBadgeMetrics(file_Path, file_Size_Bytes, elapsed, loaded_Bytes)
            Dim sizeText As String = FormatLoadingSize(file_Size_Bytes)
            Dim percentText As String = loading_Badge_Cached_Percent.ToString(CultureInfo.InvariantCulture) & "%"
            If loading_Badge_Cached_Remaining_Sec >= 0 Then
                Dim remainingText As String = LoadingIndicatorRules.FormatRemainingTime(loading_Badge_Cached_Remaining_Sec)
                line3 = sizeText & "   " & percentText & "   " & remainingText
            Else
                line3 = sizeText & "   " & percentText
            End If
        Else
            Dim seconds As String = Localization.TF("{0} с", CInt(Math.Floor(elapsed.TotalSeconds)).ToString(CultureInfo.InvariantCulture))
            If file_Size_Bytes > 0 Then
                line3 = FormatLoadingSize(file_Size_Bytes) & "   " & seconds
            Else
                line3 = seconds
            End If
        End If

        Return head & Environment.NewLine &
               fileName & Environment.NewLine &
               line3
    End Function

    ''' <summary>Binary units, like the file-size column in the status bar: KiB/MiB are
    ''' powers of 1024 by definition, so the number and the unit come from the same
    ''' system.</summary>
    Private Function FormatLoadingSize(size_Bytes As Long) As String
        Return LoadingIndicatorRules.FormatLoadingSize(size_Bytes)
    End Function

End Class
