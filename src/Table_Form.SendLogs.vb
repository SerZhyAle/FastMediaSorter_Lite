#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms

' "Send the logs to the author" - the support action on the About tab of the .NET 10
' settings shell (SPECIFICATION_SEND_LOGS_TO_AUTHOR.md).
'
' Modern build only, for the same two reasons as the Share button next door: the About
' tab exists only in Table_Form.ModernLayout.vb, and the x86 fallback takes no new
' features (CLAUDE.md maintenance policy).
'
' The whole flow is user-driven and offline: LogPackage writes a ZIP into %TEMP%, the
' user is shown exactly what is in it, and only then does the file go to the user's own
' mail client. The app itself never opens a network connection - that is what keeps the
' "no telemetry" promise on the privacy page true.
Partial Public Class Table_Form

    Private btn_Send_Logs As Button
    Private _sendLogsBusy As Boolean

    ''' <summary>Creates the button before the About page asks for it.</summary>
    Private Sub EnsureSendLogsButton()
        If btn_Send_Logs IsNot Nothing Then Return
        btn_Send_Logs = New Button With {.UseVisualStyleBackColor = True, .Height = 34}
        AddHandler btn_Send_Logs.Click, AddressOf OnSendLogsClick
    End Sub

    Private Async Sub OnSendLogsClick(sender As Object, e As EventArgs)
        If _sendLogsBusy Then Return
        _sendLogsBusy = True
        btn_Send_Logs.Enabled = False
        Dim previousCursor As Cursor = Me.Cursor
        Me.Cursor = Cursors.WaitCursor

        Try
            Dim result As LogPackage.LogPackageResult =
                Await Task.Run(Function()
                                   LogPackage.SweepOldPackages()
                                   Return LogPackage.Build()
                               End Function)

            Me.Cursor = previousCursor

            If String.IsNullOrEmpty(result.Path) Then
                MessageBox.Show(Me,
                    Localization.TF("Не удалось собрать архив: {0}", If(result.ErrorText, "")),
                    Me.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            ShowLogPackageDialog(result)
        Catch ex As Exception
            AppFileLogger.LogException("Table_Form send-logs click", ex)
            MessageBox.Show(Me, Localization.TF("Не удалось собрать архив: {0}", ex.Message),
                            Me.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            Me.Cursor = previousCursor
            btn_Send_Logs.Enabled = True
            _sendLogsBusy = False
        End Try
    End Sub

    ''' <summary>
    ''' The consent step. It is not a formality: the log carries the paths and names of
    ''' the files the user opened, so the dialog says so and offers to open the archive
    ''' before anything is sent.
    ''' </summary>
    Private Sub ShowLogPackageDialog(package As LogPackage.LogPackageResult)
        Dim sent As Boolean = False

        Using dialog As New Form With {
            .Text = Me.Text,
            .FormBorderStyle = FormBorderStyle.FixedDialog,
            .StartPosition = FormStartPosition.CenterParent,
            .MinimizeBox = False,
            .MaximizeBox = False,
            .ShowInTaskbar = False,
            .ClientSize = New Size(560, 300)
        }
            ' A topmost owner does not reliably promote a WinForms modal child to the
            ' topmost band.  Without this explicit flag the consent window can be
            ' waiting behind the pinned viewer/settings windows, which looks exactly
            ' like the button froze until Esc happens to cancel it.
            dialog.TopMost = Me.TopMost OrElse MainWindowIsTopMost()

            Dim header As New Label With {
                .AutoSize = False, .Location = New Point(16, 14), .Size = New Size(528, 22),
                .Font = New Font(dialog.Font, FontStyle.Bold),
                .Text = Localization.TF("Готов архив {0} ({1}).",
                                        Path.GetFileName(package.Path),
                                        LogPackage.DescribeSize(package.SizeBytes))
            }

            Dim contents As New TextBox With {
                .Location = New Point(16, 44), .Size = New Size(528, 96),
                .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical,
                .Text = String.Join(Environment.NewLine, package.Entries.ToArray())
            }

            Dim warning As New Label With {
                .AutoSize = False, .Location = New Point(16, 150), .Size = New Size(528, 58),
                .Text = Localization.T("В журнале есть пути и имена файлов, которые вы открывали, и названия ваших каталогов. Откройте архив и посмотрите, что уходит, если это важно.")
            }

            Dim oversize As New Label With {
                .AutoSize = False, .Location = New Point(16, 208), .Size = New Size(528, 34),
                .ForeColor = Color.Firebrick,
                .Visible = package.OverSizeLimit,
                .Text = Localization.TF("Архив получился больше {0} МБ - такое письмо почта не пропустит. Откройте архив и пришлите только нужное.",
                                        (LogPackage.MaxPackageBytes \ (1024L * 1024L)).ToString(CultureInfo.InvariantCulture))
            }

            Dim sendButton As New Button With {
                .Location = New Point(232, 252), .Size = New Size(150, 32),
                .Text = Localization.T("Отправить"),
                .Enabled = Not package.OverSizeLimit,
                .UseVisualStyleBackColor = True
            }
            Dim openButton As New Button With {
                .Location = New Point(16, 252), .Size = New Size(150, 32),
                .Text = Localization.T("Открыть архив"),
                .UseVisualStyleBackColor = True
            }
            Dim cancelButton As New Button With {
                .Location = New Point(394, 252), .Size = New Size(150, 32),
                .Text = Localization.T("Отмена"),
                .DialogResult = DialogResult.Cancel,
                .UseVisualStyleBackColor = True
            }

            AddHandler openButton.Click, Sub() RevealPackage(package.Path)
            AddHandler sendButton.Click,
                Sub()
                    sent = True
                    dialog.DialogResult = DialogResult.OK
                End Sub

            dialog.Controls.AddRange(New Control() {header, contents, warning, oversize, sendButton, openButton, cancelButton})
            dialog.AcceptButton = If(package.OverSizeLimit, openButton, sendButton)
            dialog.CancelButton = cancelButton

            ' RightToLeft text only, never RightToLeftLayout - the mirrored device
            ' context is what turned Arabic into a mirror image everywhere else.
            If Localization.IsRightToLeft() Then
                dialog.RightToLeft = RightToLeft.Yes
                For Each child As Control In dialog.Controls
                    child.RightToLeft = RightToLeft.Yes
                Next
            End If
            ApplySendLogsScriptFont(dialog, header)

            dialog.ShowDialog(Me)
        End Using

        If sent Then
            SendPackage(package.Path)
        Else
            DeletePackage(package.Path)
        End If
    End Sub

    ''' <summary>
    ''' Devanagari, Bengali, Chinese, Arabic and Urdu have no glyphs in the pinned
    ''' Microsoft Sans Serif. Same rule as the main window: the font goes on the
    ''' individual controls, not on the form, so nothing shifts on the other languages.
    ''' </summary>
    Private Shared Sub ApplySendLogsScriptFont(dialog As Form, header As Label)
        Dim family As String = Localization.FontFamily()
        If family Is Nothing Then Return

        Try
            Dim body As New Font(family, 8.25!, FontStyle.Regular, GraphicsUnit.Point)
            For Each child As Control In dialog.Controls
                child.Font = body
            Next
            header.Font = New Font(family, 8.25!, FontStyle.Bold, GraphicsUnit.Point)
        Catch ex As Exception
            AppFileLogger.LogException("Table_Form send-logs script font", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Hands the archive to the user's mail client. Simple MAPI is the only path that
    ''' can carry an attachment - a "mailto:" URI cannot - so a machine without a MAPI
    ''' client (anyone living in web Gmail) gets the manual route instead.
    ''' </summary>
    Private Sub SendPackage(zipPath As String)
        Dim subject As String = "Fast Media Sorter for Windows " & Application.ProductVersion & " - logs"
        Dim body As String = MailBodyTemplate()

        ' Simple MAPI puts its editor up synchronously.  A viewer pinned above all
        ' windows would otherwise cover that editor and make it appear hung.  The
        ' setting is restored as soon as the editor returns.
        Dim restoreMainTopMost As Boolean = MainWindowIsTopMost()
        If restoreMainTopMost Then Main_Form.TopMost = False
        Try
            If MailSender.SendFile(zipPath, subject, body) Then Return
        Finally
            If restoreMainTopMost AndAlso Main_Form IsNot Nothing AndAlso Not Main_Form.IsDisposed Then
                Main_Form.TopMost = True
            End If
        End Try

        Try
            Clipboard.SetFileDropList(New Specialized.StringCollection() From {zipPath})
        Catch ex As Exception
            AppFileLogger.LogException("Table_Form send-logs clipboard", ex)
        End Try

        RevealPackage(zipPath)

        Try
            Dim url As String = "mailto:" & Author_Email &
                                "?subject=" & Uri.EscapeDataString(subject) &
                                "&body=" & Uri.EscapeDataString(body)
            Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
        Catch ex As Exception
            AppFileLogger.LogException("Table_Form send-logs mailto", ex)
        End Try

        MessageBox.Show(Me,
            Localization.T("Почтовый клиент с вложением открыть не удалось. Архив скопирован в буфер обмена и показан в папке - вложите его в письмо вручную."),
            Me.Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    ''' <summary>The three fields the author needs, plus one English identification
    ''' line so the version is readable whatever language the text is in.</summary>
    Private Function MailBodyTemplate() As String
        Dim sb As New StringBuilder()
        sb.AppendLine(Localization.T("Что произошло:"))
        sb.AppendLine()
        sb.AppendLine(Localization.T("Что ожидалось:"))
        sb.AppendLine()
        sb.AppendLine(Localization.T("Шаги для повторения:"))
        sb.AppendLine()
        sb.AppendLine("--")
        sb.AppendLine(Application.ProductVersion & ", " & Environment.OSVersion.ToString() &
                      ", " & If(Environment.Is64BitProcess, "x64", "x86"))
        Return sb.ToString()
    End Function

    Private Sub RevealPackage(zipPath As String)
        Try
            Process.Start(New ProcessStartInfo("explorer.exe", "/select,""" & zipPath & """") With {.UseShellExecute = True})
        Catch ex As Exception
            AppFileLogger.LogException("Table_Form send-logs reveal", ex)
        End Try
    End Sub

    Private Sub DeletePackage(zipPath As String)
        Try
            If File.Exists(zipPath) Then File.Delete(zipPath)
        Catch ex As Exception
            AppFileLogger.LogException("Table_Form send-logs cleanup", ex)
        End Try
    End Sub

    ''' <summary>Reads the pinned-viewer state defensively: settings may be closing
    ''' while an async archive build is completing.</summary>
    Private Shared Function MainWindowIsTopMost() As Boolean
        Try
            Return Main_Form IsNot Nothing AndAlso Not Main_Form.IsDisposed AndAlso Main_Form.TopMost
        Catch
            Return False
        End Try
    End Function

End Class
#End If
