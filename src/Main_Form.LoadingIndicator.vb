Option Strict On

Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
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

    ''' <summary>
    ''' LoadImageWithStream, but it tells the user when it is taking its time.
    ''' Drop-in: same arguments, same result (Nothing on failure - the decoder
    ''' swallows its own errors), same thread by the time it returns.
    ''' </summary>
    Private Function LoadImageWithProgress(file_Path As String, file_Size_Bytes As Long) As Tuple(Of Image, IO.MemoryStream)
        ' Started before the task, so the counter shown to the user is the wait they
        ' have actually sat through, not the wait since the badge appeared.
        Dim watch As Stopwatch = Stopwatch.StartNew()

        ' The path travels as an argument, never as a global read from the pool
        ' thread: the user goes on flipping and Current_File_Name moves under us.
        Dim decode As System.Threading.Tasks.Task(Of Tuple(Of Image, IO.MemoryStream)) =
            System.Threading.Tasks.Task.Run(Function() LoadImageWithStream(file_Path))

        ' The common case - no badge is ever built and nothing on screen moves.
        If decode.Wait(loading_Badge_Delay_Ms) Then Return decode.GetAwaiter().GetResult()

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
                AbandonDecode(decode, file_Path, watch.ElapsedMilliseconds)
                Return Nothing
            End If

            ' GetResult, not .Result: it rethrows what the decode actually threw
            ' instead of an AggregateException wrapping it, so the caller's
            ' ArgumentException / OutOfMemoryException catches still match.
            result = decode.GetAwaiter().GetResult()
        Finally
            HideLoadingBadge()
            RestoreLoadingStatus(saved_Status)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0912: slow decode took " &
                            watch.ElapsedMilliseconds.ToString() & " ms: " & file_Path)
        End Try
        Return result
    End Function

    ''' <summary>Lets go of a decode that outran its deadline, releasing its result whenever
    ''' (if ever) it lands.</summary>
    Private Shared Sub AbandonDecode(decode As System.Threading.Tasks.Task(Of Tuple(Of Image, IO.MemoryStream)),
                                     file_Path As String,
                                     waited_Ms As Long)
        AppFileLogger.WriteLine("Decode abandoned after " & waited_Ms.ToString() & " ms: " & file_Path)
        decode.ContinueWith(Sub(finished)
                                Try
                                    If finished.Status <> System.Threading.Tasks.TaskStatus.RanToCompletion Then Return
                                    Dim late As Tuple(Of Image, IO.MemoryStream) = finished.Result
                                    If late Is Nothing Then Return
                                    late.Item1?.Dispose()
                                    late.Item2?.Dispose()
                                Catch
                                End Try
                            End Sub, System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously)
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
            lbl_Status.Text = Localization.T("Загрузка: ") & Path.GetFileName(file_Path)
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
    Private Sub ShowLoadingBadge(file_Path As String, file_Size_Bytes As Long, elapsed As TimeSpan, frame As Integer)
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

            loading_Badge.Text = LoadingBadgeText(file_Path, file_Size_Bytes, elapsed, frame)
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

    Private Sub HideLoadingBadge()
        If loading_Badge Is Nothing Then Return
        Try
            ' Left built for the next slow file; hiding invalidates the area under it,
            ' which repaints normally once the caller is back at the message loop.
            If loading_Badge.Visible Then loading_Badge.Visible = False
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0916: loading badge hide skipped: " & ex.Message)
        End Try
    End Sub

    ''' <summary>What the badge says: the file, why it is slow (its size), how long it
    ''' has been going, and a spinner that proves the app is alive rather than wedged -
    ''' the thing the user could not tell before.</summary>
    Private Function LoadingBadgeText(file_Path As String, file_Size_Bytes As Long, elapsed As TimeSpan, frame As Integer) As String
        ' Plain ASCII: every font has it, and this text can land on any machine.
        Const spinner As String = "|/-\"
        Dim tick As String = spinner.Substring(frame Mod spinner.Length, 1)

        Dim head As String = Localization.T("Загрузка.. ") & tick
        Dim seconds As String = CInt(Math.Floor(elapsed.TotalSeconds)).ToString() & Localization.T(" с")

        Return head & Environment.NewLine &
               Path.GetFileName(file_Path) & Environment.NewLine &
               FormatLoadingSize(file_Size_Bytes) & "   " & seconds
    End Function

    ''' <summary>Binary units, like the file-size column in the status bar: KiB/MiB are
    ''' powers of 1024 by definition, so the number and the unit come from the same
    ''' system.</summary>
    Private Function FormatLoadingSize(size_Bytes As Long) As String
        If size_Bytes >= 1024L * 1024L Then
            Return (size_Bytes / (1024.0 * 1024.0)).ToString("F1") & Localization.T(" МиБ")
        ElseIf size_Bytes >= 1024L Then
            Return (size_Bytes / 1024.0).ToString("F1") & Localization.T(" КиБ")
        End If
        Return size_Bytes.ToString() & Localization.T(" Б")
    End Function

End Class
