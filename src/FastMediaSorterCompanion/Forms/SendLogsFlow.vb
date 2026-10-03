Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' "Send the log to the author" from the Share Manager itself (APP-BEHAVIOUR rule 6,
''' 028_SPECIFICATION_CONTRACT_SYNC_DESKTOP_APP_UX.md Ф-3). Before it, a Share Manager user
''' had to find the viewer's settings window to send companion.log - the log the failure
''' they were looking at had just been written to.
'''
''' The archive is the viewer's own LogPackage, linked: one report format, one allow-list
''' of settings, one redaction of the user-profile path, both logs in one ZIP. The flow
''' around it is the viewer's too - build in %TEMP%, show exactly what is inside, and only
''' then hand the file to the user's own mail client through Simple MAPI (MailSender,
''' linked). Nothing here opens a network connection.
''' </summary>
Friend Module SendLogsFlow

    Private _busy As Boolean

    ''' <param name="owner">The window to centre on, or Nothing from the tray while the
    ''' main window is hidden - the dialogs then come up on top of everything.</param>
    Friend Async Function RunAsync(owner As IWin32Window) As Task
        If _busy Then Return
        _busy = True
        Try
            Dim result As LogPackage.LogPackageResult =
                Await Task.Run(Function()
                                   LogPackage.SweepOldPackages()
                                   Return LogPackage.Build()
                               End Function).ConfigureAwait(True)

            If String.IsNullOrEmpty(result.Path) Then
                AppFileLogger.LogException("Send logs: building the archive", result.Failure)
                ShowMessage(owner, Localization.T("Не удалось собрать архив журналов. Подробности записаны в журнал программы."), MessageBoxIcon.Warning)
                Return
            End If

            If AskConsent(owner, result) Then
                SendPackage(owner, result.Path)
            Else
                DeletePackage(result.Path)
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Send logs", ex)
            ShowMessage(owner, Localization.T("Не удалось собрать архив журналов. Подробности записаны в журнал программы."), MessageBoxIcon.Warning)
        Finally
            _busy = False
        End Try
    End Function

    Private ReadOnly Property Title As String
        Get
            Return Localization.T("Отправить журнал автору")
        End Get
    End Property

    ''' <summary>
    ''' The consent step, laid out in a self-sizing table so every language fits (rule 2).
    ''' Send is the default, Cancel the one no-action exit - Escape and the close box take
    ''' it too, and the archive is then deleted.
    ''' </summary>
    Private Function AskConsent(owner As IWin32Window, package As LogPackage.LogPackageResult) As Boolean
        Using dialog As New Form With {
            .Text = Title,
            .FormBorderStyle = FormBorderStyle.FixedDialog,
            .StartPosition = If(owner Is Nothing, FormStartPosition.CenterScreen, FormStartPosition.CenterParent),
            .MinimizeBox = False,
            .MaximizeBox = False,
            .ShowInTaskbar = owner Is Nothing,
            .TopMost = owner Is Nothing,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .AutoScaleMode = AutoScaleMode.Dpi,
            .Padding = New Padding(12)
        }
            UiLanguage.ApplyTo(dialog)
            Dim layout As New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                     .ColumnCount = 1, .Dock = DockStyle.Fill}
            Using boldFont As New Font(dialog.Font, FontStyle.Bold)
                Dim header As New Label With {
                    .AutoSize = True, .MaximumSize = New Size(520, 0), .Margin = New Padding(0, 0, 0, 8),
                    .Font = boldFont,
                    .Text = Localization.TF("Готов архив {0} ({1}).", Path.GetFileName(package.Path),
                                            LogPackage.DescribeSize(package.SizeBytes))}
            Dim contents As New TextBox With {
                .Width = 520, .Height = 96, .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical,
                .Margin = New Padding(0, 0, 0, 8),
                .Text = String.Join(Environment.NewLine, package.Entries.ToArray())}
            Dim warning As New Label With {
                .AutoSize = True, .MaximumSize = New Size(520, 0), .Margin = New Padding(0, 0, 0, 8),
                .Text = Localization.T("В журнале есть пути и имена файлов, которые вы открывали, и названия ваших каталогов. Откройте архив и посмотрите, что уходит, если это важно.")}
            Dim oversize As New Label With {
                .AutoSize = True, .MaximumSize = New Size(520, 0), .Margin = New Padding(0, 0, 0, 8),
                .ForeColor = CollapsibleSection.AttentionColor, .Visible = package.OverSizeLimit,
                .Text = Localization.TF("Архив получился больше {0} МБ - такое письмо почта не пропустит. Откройте архив и пришлите только нужное.",
                                        (LogPackage.MaxPackageBytes \ (1024L * 1024L)).ToString(CultureInfo.InvariantCulture))}

            Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                     .FlowDirection = FlowDirection.RightToLeft, .Dock = DockStyle.Fill,
                                                     .WrapContents = False, .Margin = New Padding(0)}
            Dim cancel As New Button With {.AutoSize = True, .MinimumSize = New Size(110, 30),
                                           .Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel}
            Dim send As New Button With {.AutoSize = True, .MinimumSize = New Size(110, 30),
                                         .Text = Localization.T("Отправить"), .Enabled = Not package.OverSizeLimit,
                                         .DialogResult = DialogResult.OK}
            Dim reveal As New Button With {.AutoSize = True, .MinimumSize = New Size(110, 30),
                                           .Text = Localization.T("Открыть архив")}
            AddHandler reveal.Click, Sub() RevealPackage(package.Path)
            buttons.Controls.Add(cancel)
            buttons.Controls.Add(send)
            buttons.Controls.Add(reveal)

            layout.Controls.Add(header)
            layout.Controls.Add(contents)
            layout.Controls.Add(warning)
            layout.Controls.Add(oversize)
            layout.Controls.Add(buttons)
            dialog.Controls.Add(layout)
            dialog.AcceptButton = If(package.OverSizeLimit, reveal, send)
            dialog.CancelButton = cancel

                Dim answer As DialogResult = If(owner Is Nothing, dialog.ShowDialog(), dialog.ShowDialog(owner))
                Return answer = DialogResult.OK
            End Using
        End Using
    End Function

    ''' <summary>Simple MAPI first (the only path that carries an attachment); without a
    ''' MAPI client, the archive on the clipboard, shown in its folder, and a plain mailto.</summary>
    Private Sub SendPackage(owner As IWin32Window, zipPath As String)
        Dim subject As String = ProductNames.ShareManager & " " & Application.ProductVersion & " - logs"
        Dim body As String = MailBodyTemplate()
        Dim ownerHandle As IntPtr = If(owner Is Nothing, IntPtr.Zero, owner.Handle)
        Select Case MailSender.FollowUpFor(MailSender.Send(zipPath, subject, body, ownerHandle))
            Case MailSender.MailFollowUp.Finished
                Return
            Case MailSender.MailFollowUp.DiscardArchive
                ' Nothing was sent - do not leave the archive behind in %TEMP% (053 F-14).
                Try
                    IO.File.Delete(zipPath)
                Catch ex As Exception
                    AppFileLogger.LogException("Send logs: cleanup after cancel", ex)
                End Try
                Return
        End Select

        Try
            Clipboard.SetFileDropList(New Specialized.StringCollection() From {zipPath})
        Catch ex As Exception
            AppFileLogger.LogException("Send logs: clipboard", ex)
        End Try
        RevealPackage(zipPath)
        Try
            Dim url As String = "mailto:" & MailSender.AuthorAddress &
                                "?subject=" & Uri.EscapeDataString(subject) &
                                "&body=" & Uri.EscapeDataString(body)
            Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
        Catch ex As Exception
            AppFileLogger.LogException("Send logs: mailto", ex)
        End Try
        ShowMessage(owner, Localization.T("Почтовый клиент с вложением открыть не удалось. Архив скопирован в буфер обмена и показан в папке - вложите его в письмо вручную."),
                    MessageBoxIcon.Information)
    End Sub

    Private Function MailBodyTemplate() As String
        Dim sb As New StringBuilder()
        sb.AppendLine(Localization.T("Что произошло:"))
        sb.AppendLine()
        sb.AppendLine(Localization.T("Что ожидалось:"))
        sb.AppendLine()
        sb.AppendLine(Localization.T("Шаги для повторения:"))
        sb.AppendLine()
        sb.AppendLine("--")
        sb.AppendLine("Share Manager " & Application.ProductVersion & ", " & Environment.OSVersion.ToString() &
                      ", " & If(Environment.Is64BitProcess, "x64", "x86"))
        Return sb.ToString()
    End Function

    Private Sub RevealPackage(zipPath As String)
        Try
            Process.Start(New ProcessStartInfo("explorer.exe", "/select,""" & zipPath & """") With {.UseShellExecute = True})
        Catch ex As Exception
            AppFileLogger.LogException("Send logs: reveal", ex)
        End Try
    End Sub

    Private Sub DeletePackage(zipPath As String)
        Try
            If File.Exists(zipPath) Then File.Delete(zipPath)
        Catch ex As Exception
            AppFileLogger.LogException("Send logs: cleanup", ex)
        End Try
    End Sub

    Private Sub ShowMessage(owner As IWin32Window, text As String, icon As MessageBoxIcon)
        If owner Is Nothing Then
            MessageBox.Show(text, Title, MessageBoxButtons.OK, icon, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly)
        Else
            MessageBox.Show(owner, text, Title, MessageBoxButtons.OK, icon)
        End If
    End Sub

End Module
