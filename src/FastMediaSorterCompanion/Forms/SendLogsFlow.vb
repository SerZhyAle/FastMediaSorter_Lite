Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The Manager's local server-log export (specification 272). The linked LogPackage owns
''' discovery and redaction; this flow owns review, explicit handoffs and archive lifetime.
''' Neither opening a mail composer nor revealing a file asserts delivery.
''' </summary>
Friend Module SendLogsFlow

    Private _busy As Boolean

    ''' <summary>Once the user asks for either handoff, cancelling must preserve the same reviewed ZIP.</summary>
    Friend NotInheritable Class ExportReviewState
        Friend Property RetainArchive As Boolean
        Friend Sub RequestHandoff()
            RetainArchive = True
        End Sub
        Friend ReadOnly Property DeleteOnClose As Boolean
            Get
                Return Not RetainArchive
            End Get
        End Property
    End Class

    ''' <param name="owner">The window to centre on, or Nothing from the tray while the
    ''' main window is hidden - the dialogs then come up on top of everything.</param>
    Friend Async Function RunAsync(owner As IWin32Window, Optional command As Control = Nothing) As Task
        If _busy Then Return
        _busy = True
        Dim previousText As String = If(command Is Nothing, "", command.Text)
        Try
            If command IsNot Nothing Then
                command.Enabled = False
                command.Text = Localization.T("Сбор журналов..")
            End If
            Dim result As LogPackage.LogPackageResult =
                Await Task.Run(Function()
                                   LogPackage.SweepOldPackages()
                                   Return LogPackage.BuildServerLogs()
                               End Function).ConfigureAwait(True)

            If String.IsNullOrEmpty(result.Path) Then
                AppFileLogger.LogException("Send logs: building the archive", result.Failure)
                ShowMessage(owner, Localization.T("Не удалось собрать архив журналов. Подробности записаны в журнал программы."), MessageBoxIcon.Warning)
                Return
            End If

            Dim reviewState As ExportReviewState
            Using protection As IDisposable = LogPackage.ProtectPackage(result.Path)
                reviewState = AskConsent(owner, result)
            End Using
            FinishReview(result.Path, reviewState)
        Catch ex As Exception
            AppFileLogger.LogException("Send logs", ex)
            ShowMessage(owner, Localization.T("Не удалось собрать архив журналов. Подробности записаны в журнал программы."), MessageBoxIcon.Warning)
        Finally
            _busy = False
            If command IsNot Nothing AndAlso Not command.IsDisposed Then
                command.Enabled = True
                command.Text = previousText
            End If
        End Try
    End Function

    Private ReadOnly Property Title As String
        Get
            Return Localization.T("Экспорт журналов для анализа")
        End Get
    End Property

    ''' <summary>
    ''' The shared answer row stays outside the scrolling source list. Cancel is the safe default;
    ''' after either handoff is requested it closes the review while preserving the archive.
    ''' </summary>
    Private Function AskConsent(owner As IWin32Window, package As LogPackage.LogPackageResult) As ExportReviewState
        Dim state As New ExportReviewState()
        Using dialog As Form = BuildConsentDialog(owner, package, state)
            Do
                Dim answer As DialogResult = If(owner Is Nothing, dialog.ShowDialog(), dialog.ShowDialog(owner))
                If answer <> DialogResult.OK Then Exit Do
                state.RequestHandoff()
                SendPackage(owner, package.Path)
                ' MAPI cannot prove delivery. Return to the same review after composition or cancellation.
                dialog.DialogResult = DialogResult.None
            Loop
        End Using
        Return state
    End Function

    Friend Sub FinishReview(zipPath As String, state As ExportReviewState)
        If state.DeleteOnClose Then
            DeletePackage(zipPath)
        Else
            Try
                File.SetLastWriteTimeUtc(zipPath, DateTime.UtcNow)
            Catch ex As Exception
                AppFileLogger.LogException("Send logs: refreshing archive retention", ex)
            End Try
        End If
    End Sub

    ''' <summary>The consent window, built and not shown (a test lays it out; <see cref="AskConsent"/> shows it).</summary>
    Friend Function BuildConsentDialog(owner As IWin32Window, package As LogPackage.LogPackageResult,
                                      Optional state As ExportReviewState = Nothing,
                                      Optional revealArchive As Func(Of String, Boolean) = Nothing) As Form
        Dim review As ExportReviewState = If(state, New ExportReviewState())
        Dim revealAction As Func(Of String, Boolean) = If(revealArchive, AddressOf RevealPackage)
        Dim dialog As New Form With {
            .Text = Title,
            .FormBorderStyle = FormBorderStyle.Sizable,
            .StartPosition = If(owner Is Nothing, FormStartPosition.CenterScreen, FormStartPosition.CenterParent),
            .MinimizeBox = False,
            .MaximizeBox = False,
            .ShowInTaskbar = owner Is Nothing,
            .TopMost = owner Is Nothing,
            .ClientSize = New Size(680, 520),
            .MinimumSize = New Size(480, 380),
            .AutoScaleMode = AutoScaleMode.Dpi,
            .Padding = New Padding(12)
        }
        UiLanguage.ApplyTo(dialog)
        dialog.Tag = review
        Dim layout As New TableLayoutPanel With {.ColumnCount = 1, .RowCount = 7, .Dock = DockStyle.Fill}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For index As Integer = 0 To 6
            layout.RowStyles.Add(New RowStyle(If(index = 2, SizeType.Percent, SizeType.AutoSize), If(index = 2, 100.0F, 0.0F)))
        Next
        Dim boldFont As New Font(dialog.Font, FontStyle.Bold)
        AddHandler dialog.Disposed, Sub() boldFont.Dispose()
        Dim header As New Label With {
            .AutoSize = True, .MaximumSize = New Size(520, 0), .Margin = New Padding(0, 0, 0, 8),
            .Font = boldFont,
            .Text = Localization.TF("Готов архив {0} ({1}).", Path.GetFileName(package.Path),
                                    LogPackage.DescribeSize(package.SizeBytes))}
        Dim contents As New TextBox With {
            .Dock = DockStyle.Fill, .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical,
            .Name = "xsl_Sources", .AccessibleName = Localization.T("Источники журналов"),
            .Margin = New Padding(0, 0, 0, 8),
            .Text = String.Join(Environment.NewLine, package.Entries.ToArray())}
        Dim warning As New Label With {
            .AutoSize = True, .MaximumSize = New Size(520, 0), .Margin = New Padding(0, 0, 0, 8),
            .Text = Localization.T("Секреты удалены. Проверьте архив перед отправкой: в журналах могут остаться имена файлов. Программа ничего не отправляет сама.")}
        Dim status As New Label With {.AutoSize = True, .MaximumSize = New Size(640, 0), .Name = "xsl_Status",
            .Text = If(package.IsPartial, Localization.T("Архив неполный: часть журналов сокращена, пропущена или недоступна."), Localization.T("Сбор доступных журналов завершён."))}
        If Not package.ServerSourcesFound Then
            status.Text &= Environment.NewLine & Localization.T("Локальные серверные журналы не найдены. В архиве только доступная диагностика приложения; удалённые журналы не собираются.")
        End If
        Dim archivePath As New TextBox With {.Text = package.Path, .ReadOnly = True, .Dock = DockStyle.Top,
            .Name = "xsl_Path", .AccessibleName = Localization.T("Путь к архиву"), .RightToLeft = RightToLeft.No}
        Dim oversize As New Label With {
            .AutoSize = True, .MaximumSize = New Size(520, 0), .Margin = New Padding(0, 0, 0, 8),
            .ForeColor = CollapsibleSection.AttentionColor, .Visible = package.OverSizeLimit,
            .Text = Localization.TF("Архив больше {0} МБ. Отправка через почтовую программу недоступна; скопируйте ZIP через Проводник.",
                                    (LogPackage.MaxPackageBytes \ (1024L * 1024L)).ToString(CultureInfo.InvariantCulture))}

        Dim cancel As New Button With {.Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel, .Name = "xsl_Cancel"}
        Dim send As New Button With {.Text = Localization.T("Отправить автору"), .Enabled = Not package.OverSizeLimit,
                                     .DialogResult = DialogResult.OK, .Name = "xsl_Send"}
        Dim reveal As New Button With {.Text = Localization.T("Показать в Проводнике"), .Name = "xsl_Reveal"}
        ButtonGlyphs.Apply(reveal, "nav.open-external")
        AddHandler reveal.Click, Sub()
                                     review.RequestHandoff()
                                     If Not revealAction(package.Path) Then
                                         ShowMessage(dialog, Localization.T("Не удалось открыть Проводник. Скопируйте путь к архиву из окна проверки и откройте папку вручную."), MessageBoxIcon.Information)
                                     End If
                                 End Sub
        Dim row As New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xsl_Buttons", .Padding = New Padding(0, 4, 0, 0)}
        DpiLayout.AddAnswer(row, reveal, DialogButtonRole.Command, "nav.open-external")
        DpiLayout.AddAnswer(row, cancel, DialogButtonRole.Dismissing, "action.cancel")
        DpiLayout.AddAnswer(row, send, DialogButtonRole.Affirmative, "action.email")

        layout.Controls.Add(header, 0, 0)
        layout.Controls.Add(status, 0, 1)
        layout.Controls.Add(contents, 0, 2)
        layout.Controls.Add(warning, 0, 3)
        layout.Controls.Add(archivePath, 0, 4)
        layout.Controls.Add(oversize, 0, 5)
        dialog.Controls.Add(layout)
        ' Last in the collection: the docked row takes its edge first and reads after the text in the Tab order.
        dialog.Controls.Add(row)
        row.Bind(dialog, bindAccept:=False)
        ' The safe default is the Cancel side: Enter never sends (it used to - the archive holds paths and file names).
        dialog.AcceptButton = cancel
        AddHandler layout.SizeChanged,
            Sub()
                Dim width As Integer = Math.Max(1, layout.ClientSize.Width - 8)
                For Each label As Label In New Label() {header, status, warning, oversize}
                    label.MaximumSize = New Size(width, 0)
                Next
            End Sub
        DpiLayout.ApplyAutoScale(dialog)
        ThemeService.Attach(dialog)
        Return dialog
    End Function

    ''' <summary>Simple MAPI first (the only path that carries an attachment); without a
    ''' MAPI client, the archive on the clipboard, shown in its folder, and a plain mailto.</summary>
    Private Sub SendPackage(owner As IWin32Window, zipPath As String)
        Dim subject As String = ProductNames.ShareManager & " " & Application.ProductVersion & " - logs"
        Dim body As String = MailBodyTemplate()
        Dim ownerHandle As IntPtr = If(owner Is Nothing, IntPtr.Zero, owner.Handle)
        If MailSender.Send(zipPath, subject, body, ownerHandle) <> MailSender.MailSendOutcome.Failed Then Return

        Try
            Clipboard.SetFileDropList(New Specialized.StringCollection() From {zipPath})
        Catch ex As Exception
            AppFileLogger.LogException("Send logs: clipboard", ex)
        End Try
        RevealPackage(zipPath)
        ' A blind mailto on a machine with no mail handler (every Windows Server) raises a "choose an app"
        ' prompt for nothing (ticket 182 L-6): without a registered handler it is skipped, and the message
        ' carries the archive's FULL path, so it can be read out or typed on another machine.
        If MailSender.ManualRouteFor(MailSender.HasMailHandler()) = MailSender.ManualRoute.PrintPathOnly Then
            AppFileLogger.WriteLine("Send logs: no mail handler is registered - the mailto was skipped, the archive path was shown")
            ShowMessage(owner, Localization.TF("Почтовая программа не найдена. Архив сохранён. Полный путь к архиву: {0}", zipPath),
                        MessageBoxIcon.Information)
            Return
        End If
        Try
            Dim url As String = "mailto:" & MailSender.AuthorAddress &
                                "?subject=" & Uri.EscapeDataString(subject) &
                                "&body=" & Uri.EscapeDataString(body)
            Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
        Catch ex As Exception
            AppFileLogger.LogException("Send logs: mailto", ex)
        End Try
        ShowMessage(owner, Localization.T("Открыть письмо с вложением не удалось. Архив сохранён; добавьте ZIP в письмо вручную. Путь можно скопировать из окна проверки."),
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

    Private Function RevealPackage(zipPath As String) As Boolean
        Try
            Process.Start(New ProcessStartInfo("explorer.exe", "/select,""" & zipPath & """") With {.UseShellExecute = True})
            Return True
        Catch ex As Exception
            AppFileLogger.LogException("Send logs: reveal", ex)
            Return False
        End Try
    End Function

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
