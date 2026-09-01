#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' The two windows of "Replace with video"
''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §9.4, §10.1).
'''
''' Both are built in code rather than in the Designer, and both are sized in plain pixels:
''' the viewer is pinned to DpiUnaware and to Microsoft Sans Serif 8.25 (see the
''' compatibility pins in CLAUDE.md), so a form that scaled itself would be the odd one out
''' on this window band, not the modern one.
'''
''' The progress dialog is MODAL on purpose. The file on screen is about to be deleted, and
''' a keypress that navigated away mid-conversion would leave the delete pointing at a file
''' the user is no longer looking at.
''' </summary>
Friend NotInheritable Class Video_Convert_Form
    Inherits Form

    Private ReadOnly cancellation As New CancellationTokenSource()
    Private ReadOnly caption As Label
    Private ReadOnly bar As ProgressBar
    Private ReadOnly cancelButton As Button

    ''' <summary>Set once the caller's work has finished, so closing the window from the
    ''' title bar cannot be mistaken for pressing Cancel.</summary>
    Private finished As Boolean

    Friend ReadOnly Property Token As CancellationToken
        Get
            Return cancellation.Token
        End Get
    End Property

    ''' <param name="determinate">False when the source's duration is unknown - then the bar
    ''' marquees instead of inventing a number.</param>
    Friend Sub New(determinate As Boolean)
        Me.Text = Localization.T("Преобразование в видео")
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(400, 116)

        caption = New Label With {
            .AutoSize = False,
            .Location = New Point(14, 14),
            .Size = New Size(372, 20),
            .TextAlign = ContentAlignment.MiddleLeft,
            .Text = Localization.T("Создаю видео..")
        }
        bar = New ProgressBar With {
            .Location = New Point(14, 40),
            .Size = New Size(372, 20),
            .Minimum = 0,
            .Maximum = 100,
            .Style = If(determinate, ProgressBarStyle.Continuous, ProgressBarStyle.Marquee)
        }
        cancelButton = New Button With {
            .Location = New Point(296, 74),
            .Size = New Size(90, 28),
            .Text = Localization.T("Отмена")
        }
        AddHandler cancelButton.Click, AddressOf CancelRequested

        Me.Controls.Add(caption)
        Me.Controls.Add(bar)
        Me.Controls.Add(cancelButton)
        Me.CancelButton = cancelButton
    End Sub

    ''' <summary>Called from the encoder's progress reports, already marshalled onto this
    ''' thread by the Progress(Of Integer) the caller creates on the UI thread.</summary>
    Friend Sub ReportPercent(percent As Integer)
        If Me.IsDisposed OrElse finished Then Return
        If bar.Style <> ProgressBarStyle.Continuous Then Return
        bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, percent))
        caption.Text = Localization.TF("Создаю видео.. {0} %", percent.ToString(Globalization.CultureInfo.CurrentCulture))
    End Sub

    ''' <summary>The work is over - close without the closing being read as a cancel.</summary>
    Friend Sub Finish()
        finished = True
        If Not Me.IsDisposed AndAlso Me.Visible Then Me.Close()
    End Sub

    Private Sub CancelRequested(sender As Object, e As EventArgs)
        If finished Then Return
        cancelButton.Enabled = False
        caption.Text = Localization.T("Преобразование отменено")
        Cancel()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        ' Closing the window while the encoder runs means the same thing as Cancel - but the
        ' window stays until the caller has actually stopped it and cleaned up, otherwise the
        ' temp file would be deleted under a process still writing to it.
        If Not finished Then
            e.Cancel = True
            CancelRequested(Me, EventArgs.Empty)
            Return
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Private Sub Cancel()
        Try
            cancellation.Cancel()
        Catch
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then cancellation.Dispose()
        MyBase.Dispose(disposing)
    End Sub

End Class

''' <summary>
''' The confirmation (§10.1). It exists as a window of its own rather than as a MessageBox
''' for exactly one reason: the "do not ask again" checkbox. A suppressible warning that
''' cannot be suppressed from the warning itself is one the user learns to click through.
'''
''' It names the consequence instead of asking "Continue?" - the whole point of stopping
''' here is that the next step is irreversible.
''' </summary>
Friend NotInheritable Class Video_Replace_Confirm_Form
    Inherits Form

    Private ReadOnly suppress As CheckBox

    ''' <summary>True when the user ticked "do not ask again". Read only after an OK.</summary>
    Friend ReadOnly Property DoNotAskAgain As Boolean
        Get
            Return suppress.Checked
        End Get
    End Property

    ''' <param name="warnAboutAlpha">Added only when the source really has an alpha channel -
    ''' a warning about a loss that is not happening teaches people to ignore warnings.</param>
    Friend Sub New(sourceName As String, targetName As String, warnAboutAlpha As Boolean)
        Me.Text = Localization.T("Преобразование в видео")
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(460, If(warnAboutAlpha, 176, 156))

        Dim message As String = Localization.TF("{0} будет преобразован в {1}. Оригинал будет удалён безвозвратно, минуя Корзину.",
                                                sourceName, targetName)
        If warnAboutAlpha Then
            message &= Environment.NewLine & Environment.NewLine & Localization.T("Прозрачность будет залита чёрным.")
        End If

        Dim text As New Label With {
            .AutoSize = False,
            .Location = New Point(14, 14),
            .Size = New Size(432, If(warnAboutAlpha, 84, 64)),
            .TextAlign = ContentAlignment.TopLeft,
            .Text = message
        }

        suppress = New CheckBox With {
            .AutoSize = True,
            .Location = New Point(14, text.Bottom + 10),
            .Text = Localization.T("Больше не спрашивать")
        }

        Dim accept As New Button With {
            .Location = New Point(232, suppress.Bottom + 12),
            .Size = New Size(120, 28),
            .Text = Localization.T("Преобразовать"),
            .DialogResult = DialogResult.OK
        }
        Dim decline As New Button With {
            .Location = New Point(358, suppress.Bottom + 12),
            .Size = New Size(88, 28),
            .Text = Localization.T("Отмена"),
            .DialogResult = DialogResult.Cancel
        }

        Me.Controls.Add(text)
        Me.Controls.Add(suppress)
        Me.Controls.Add(accept)
        Me.Controls.Add(decline)
        ' Cancel is the default: an irreversible action should not be one Enter away.
        Me.AcceptButton = Nothing
        Me.CancelButton = decline
    End Sub

End Class
#End If
