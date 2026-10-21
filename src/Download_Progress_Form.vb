Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>One progress report from a download: bytes so far, the total when the server
''' said it (-1 when it did not), and an optional line of its own (Ollama's pull states).</summary>
Public NotInheritable Class DownloadReport
    Public Property BytesDone As Long
    Public Property BytesTotal As Long = -1
    Public Property Detail As String = ""
    ''' <summary>The work has passed the point where it can stop (a runtime package is
    ''' downloaded and verified, and is being installed): the dialog greys Cancel out
    ''' instead of offering a button that would be ignored (audit 050 D-20).</summary>
    Public Property Committed As Boolean
End Class

''' <summary>What became of a download run through Download_Progress_Form:
''' finished with a value, cancelled by the user, or failed with the exception that ended it.
''' Cancelled is its own outcome and never a failure (APP-BEHAVIOUR rule 3).</summary>
Public NotInheritable Class DownloadRun(Of T)
    Public Property Value As T
    Public Property Cancelled As Boolean
    Public Property Failure As Exception
    Public ReadOnly Property Succeeded As Boolean
        Get
            Return Not Cancelled AndAlso Failure Is Nothing
        End Get
    End Property
End Class

#If Not NETFRAMEWORK Then
''' <summary>
''' The one download dialog of the viewer (028_SPECIFICATION_CONTRACT_SYNC_DESKTOP_APP_UX.md
''' Ф-6, APP-BEHAVIOUR rules 2 and 3): the runtime packages, OCR language data, the Ollama
''' installer and an Ollama model pull all go through it.
'''
'''   * real progress - bytes against the size the server announced, a marquee only when
'''     it announced none;
'''   * a Cancel that stops the transfer, after which the caller removes the partial file and
'''     reports "cancelled" as its own outcome, never as a failure;
'''   * no wait cursor: a live Cancel beside a wait cursor tells the user the application is
'''     not listening at the moment they are trying to tell it something;
'''   * modal and owned, so the same download cannot be started twice while it runs.
'''
''' Laid out in a self-sizing table, so a caption that is twice as long in German grows
''' the window instead of being cut.
''' </summary>
Friend NotInheritable Class Download_Progress_Form
    Inherits Form

    Private ReadOnly cancellation As New CancellationTokenSource()
    Private ReadOnly caption As Label
    Private ReadOnly amount As Label
    Private ReadOnly bar As ProgressBar
    Private ReadOnly btn_Cancel As Button
    Private finished As Boolean
    Private committed As Boolean

    Private Sub New(title As String, captionText As String)
        Me.Text = title
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink
        Me.Padding = New Padding(12)
        If Localization.IsRightToLeft() Then Me.RightToLeft = RightToLeft.Yes
        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then Me.Font = New Font(family, Me.Font.Size)

        Dim layout As New TableLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 1,
            .Dock = DockStyle.Fill
        }
        caption = New Label With {.AutoSize = True, .MaximumSize = New Size(460, 0), .MinimumSize = New Size(360, 0),
                                  .Margin = New Padding(0, 0, 0, 8), .Text = captionText}
        bar = New ProgressBar With {.Width = 360, .Height = 20, .Minimum = 0, .Maximum = 1000,
                                    .Style = ProgressBarStyle.Marquee, .Dock = DockStyle.Fill,
                                    .Margin = New Padding(0, 0, 0, 6),
                                    .RightToLeft = RightToLeft.No}
        amount = New Label With {.AutoSize = True, .MaximumSize = New Size(460, 0), .Margin = New Padding(0, 0, 0, 10),
                                 .ForeColor = SystemColors.GrayText, .Text = ""}
        btn_Cancel = New Button With {.Text = Localization.T("Отмена")}
        AddHandler btn_Cancel.Click, AddressOf CancelRequested
        ' The dismissing answer, in the shared button row (ticket 243 T-8a, BUTTONS 2.0 sections 3 and 4): the width
        ' follows the caption and its glyph, the row sizes itself and sits against the reading end. Bind makes it
        ' the window's CancelButton (Escape) and, being the only answer, the one Enter selects.
        Dim answers As New DialogButtonRow() With {.AutoSize = True, .Anchor = AnchorStyles.Right, .Name = "download_Buttons"}
        answers.Add(btn_Cancel, DialogButtonRole.Dismissing, "action.cancel")

        layout.Controls.Add(caption)
        layout.Controls.Add(bar)
        layout.Controls.Add(amount)
        layout.Controls.Add(answers)
        Me.Controls.Add(layout)
        answers.Bind(Me)
    End Sub

    ''' <summary>
    ''' Runs <paramref name="work"/> behind the dialog and returns how it ended. The work is
    ''' started from Shown, inside ShowDialog's message loop, so its awaited continuations
    ''' land on the UI thread and the window can be closed from inside the task it waits for
    ''' (the pattern of Main_Form.ToVideo). The window closes only once the work has really
    ''' stopped, so a caller never deletes a partial file under a transfer still writing it.
    ''' </summary>
    Friend Shared Async Function RunAsync(Of T)(owner As IWin32Window, title As String, captionText As String,
                                               work As Func(Of IProgress(Of DownloadReport), CancellationToken, Task(Of T))) As Task(Of DownloadRun(Of T))
        Using dialog As New Download_Progress_Form(title, captionText)
            Dim reporter As New Progress(Of DownloadReport)(AddressOf dialog.Report)
            Dim running As Task(Of DownloadRun(Of T)) = Nothing
            AddHandler dialog.Shown,
                Sub()
                    running = dialog.RunThenCloseAsync(work, reporter)
                End Sub

            Dim ownerForm As Form = TryCast(owner, Form)
            If ownerForm IsNot Nothing AndAlso ownerForm.TopMost Then dialog.TopMost = True
            DialogLayer.ShowViewerDialog(dialog, owner)

            ' Shown never fired - the window could not be displayed, so nothing ran.
            If running Is Nothing Then Return New DownloadRun(Of T) With {.Cancelled = True}
            Return Await running.ConfigureAwait(True)
        End Using
    End Function

    Private Async Function RunThenCloseAsync(Of T)(work As Func(Of IProgress(Of DownloadReport), CancellationToken, Task(Of T)),
                                                  reporter As IProgress(Of DownloadReport)) As Task(Of DownloadRun(Of T))
        Dim result As New DownloadRun(Of T)()
        Try
            ' A work item that RETURNED finished its job, even when Cancel was pressed meanwhile:
            ' the runtime installer stops honouring the token once the package is on disk, and
            ' a Cancel pressed during extraction used to be reported as "cancelled, nothing
            ' installed" over a runtime that was installed (audit 050 D-20). Every worker here
            ' throws OperationCanceledException when it actually stops on the token.
            result.Value = Await work(reporter, cancellation.Token).ConfigureAwait(True)
        Catch ex As OperationCanceledException When cancellation.IsCancellationRequested
            result.Cancelled = True
        Catch ex As Exception
            result.Failure = ex
        Finally
            Finish()
        End Try
        Return result
    End Function

    Private Sub Report(report As DownloadReport)
        If Me.IsDisposed OrElse finished OrElse report Is Nothing Then Return
        If report.Committed Then
            committed = True
            btn_Cancel.Enabled = False
            If bar.Style <> ProgressBarStyle.Marquee Then bar.Style = ProgressBarStyle.Marquee
            amount.Text = Localization.T("Установка..")
            Return
        End If
        If report.BytesTotal > 0 Then
            If bar.Style <> ProgressBarStyle.Continuous Then bar.Style = ProgressBarStyle.Continuous
            bar.Value = CInt(Math.Max(0, Math.Min(1000, report.BytesDone * 1000 \ report.BytesTotal)))
            amount.Text = Localization.TF("{0} из {1} МБ", Megabytes(report.BytesDone), Megabytes(report.BytesTotal))
        ElseIf report.BytesDone > 0 Then
            amount.Text = Localization.TF("{0} МБ", Megabytes(report.BytesDone))
        End If
        If Not String.IsNullOrEmpty(report.Detail) Then amount.Text = report.Detail
    End Sub

    Private Shared Function Megabytes(bytes As Long) As String
        Return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.CurrentCulture)
    End Function

    Private Sub Finish()
        finished = True
        If Not Me.IsDisposed AndAlso Me.Visible Then Me.Close()
    End Sub

    Private Sub CancelRequested(sender As Object, e As EventArgs)
        If finished OrElse committed Then Return
        btn_Cancel.Enabled = False
        amount.Text = Localization.T("Отменяю..")
        Try
            cancellation.Cancel()
        Catch
        End Try
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        ' The close box, Escape and Cancel are one path (APP-BEHAVIOUR rule 1): all of them
        ' cancel, and the window stays until the work has actually stopped.
        If Not finished Then
            e.Cancel = True
            CancelRequested(Me, EventArgs.Empty)
            Return
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then cancellation.Dispose()
        MyBase.Dispose(disposing)
    End Sub

End Class
#End If
