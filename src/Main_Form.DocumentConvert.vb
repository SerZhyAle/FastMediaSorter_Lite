#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Globalization
Imports System.IO
Imports System.Threading.Tasks

' Specification 276 T-2 and T-3: the document command of doc-html-translate behind DocumentConverterEnabled, and the
' one job it owns (OCR-INVOCATION 1.2: -noopen, a per-job folder, the verified result opened by the viewer). The image
' route (Main_Form.BrowserTranslate.vb, DocHtmlTranslateLauncher.vb) is frozen and only read from here.
Partial Public Class Main_Form
    Private document_Convert_Busy As Boolean

    ''' <summary>%LOCALAPPDATA%\SZA\FastMediaSorter\document-convert-cache - one folder per job, never beside the
    ''' user's document (section 6).</summary>
    Private Shared Function DocumentConvertCacheRoot() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            App_name, Second_App_Name, "document-convert-cache")
    End Function

    ''' <summary>Journey steps 1-4: readiness off the UI thread, then the install offer or the job panel. A result
    ''' that arrives after the user has moved to another file is dropped, so the panel never names a document that
    ''' is no longer on screen. <paramref name="again"/> is Process again of a shown result: it never reuses a finished page.</summary>
    ''' <summary>%LOCALAPPDATA%\SZA\FastMediaSorter\document-convert-input - one folder per job for an editor snapshot,
    ''' apart from the result cache (its trim and Clear never see it), removed when the job ends (Step 5.1).</summary>
    Private Shared Function DocumentSnapshotRoot() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            App_name, Second_App_Name, "document-convert-input")
    End Function

    ''' <summary>An editor snapshot asked for by the text editor (ticket 277): the document it came from, what part of it,
    ''' and the text captured at the moment of the request - the job's immutable input (section 6).</summary>
    Private NotInheritable Class DocumentSnapshotRequest
        Friend OriginPath As String = ""
        Friend Scope As DocHtmlSnapshotScope
        Friend Text As String = ""

        Friend ReadOnly Property DisplayName As String
            Get
                Return DocHtmlSnapshot.FileNameFor(OriginPath, Scope)
            End Get
        End Property

        ''' <summary>Section 3: a selection never pretends to be the whole document, and an unsaved buffer says so.</summary>
        Friend ReadOnly Property ScopeNote As String
            Get
                Return If(Scope = DocHtmlSnapshotScope.Selection,
                          Localization.T("Будет обработан только выделенный фрагмент, а не весь документ."),
                          Localization.T("Будет обработан текущий текст редактора вместе с несохранёнными изменениями; файл при этом не сохраняется."))
            End Get
        End Property
    End Class

    ''' <summary>Step 5.1, the entry of the text editor (ticket 277): convert or translate the editor's whole text or its
    ''' selection without saving the file. The text is copied now, so later edits never reach the job; a secure-container
    ''' document never becomes a job (section 6). The page of a snapshot opens in the browser, so the editor and its
    ''' unsaved changes stay where they are.</summary>
    Friend Sub BeginTextSnapshotConversion(originPath As String, scope As DocHtmlSnapshotScope, text As String)
        If DocHtmlSnapshot.IsSecureOrigin(originPath) OrElse IsSecureContentOnScreen() Then
            Notify(Localization.T("Текст защищённого файла не передаётся в doc-html-translate."), NotifyLevel.Warning)
            Return
        End If
        If String.IsNullOrWhiteSpace(text) Then
            Notify(Localization.T("Нет текста для обработки."), NotifyLevel.Warning)
            Return
        End If
        BeginDocumentConversion(originPath, False, New DocumentSnapshotRequest With {.OriginPath = If(originPath, ""), .Scope = scope, .Text = text})
    End Sub

    Private Async Sub BeginDocumentConversion(path As String, Optional again As Boolean = False, Optional snapshot As DocumentSnapshotRequest = Nothing)
        If document_Convert_Busy OrElse AskAboutRunningDocumentJob() Then Return
        document_Convert_Busy = True
        Dim generation As Integer = media_Generation
        Dim location As String = GetModernPreferences().DocumentConverterLocation
        Dim probe As DocHtmlConverterProbe
        Try
            SayProgress(Localization.T("Проверяю doc-html-translate.."))
            probe = Await Task.Run(Function() DocHtmlConverterLocator.Probe(location)).ConfigureAwait(True)
        Catch ex As Exception
            AppFileLogger.LogException("doc-html-translate readiness", ex)
            probe = New DocHtmlConverterProbe With {.State = DocHtmlConverterState.NotRunnable, .ExePath = location}
        Finally
            document_Convert_Busy = False
        End Try
        If IsDisposed OrElse Disposing OrElse generation <> media_Generation Then Return

        If probe.State <> DocHtmlConverterState.Ready Then
            Dim shownPath As String = If(probe.FromExplicitChoice OrElse probe.State = DocHtmlConverterState.NotRunnable, probe.ExePath, "")
            Select Case AskDialogs.Choose(Me, AskSpecs.DocumentConverterMissing(probe.State = DocHtmlConverterState.NotRunnable, shownPath))
                Case AskSpecs.DocumentConverterInstall
                    InstallDocumentConverter()
                Case AskSpecs.DocumentConverterChoose
                    If ChooseDocumentConverter() Then BeginDocumentConversion(path, again, snapshot)
                Case AskSpecs.DocumentConverterCheckAgain
                    BeginDocumentConversion(path, again, snapshot)
            End Select
            Return
        End If

        Dim preferences = GetModernPreferences()
        Dim mode As String
        Dim shownName = If(snapshot IsNot Nothing, snapshot.DisplayName, IO.Path.GetFileName(path))
        Dim scopeNote = If(snapshot IsNot Nothing, snapshot.ScopeNote, "")
        Select Case AskDialogs.Choose(Me, AskSpecs.DocumentConvertPanel(shownName, probe.VersionText, preferences.DocumentProcessingMode,
                                                                        opensInViewer:=snapshot Is Nothing AndAlso ResultOpensInViewer(preferences),
                                                                        recognitionForced:=String.Equals(preferences.DocumentOcrMode, "force", StringComparison.Ordinal),
                                                                        scopeNote:=scopeNote))
            Case AskSpecs.DocumentConvertBrowser : mode = DocHtmlJobArguments.ModeBrowser
            Case AskSpecs.DocumentConvertOnly : mode = DocHtmlJobArguments.ModeConvert
            Case AskSpecs.DocumentConvertLocal : mode = DocHtmlJobArguments.ModeLocal
            Case AskSpecs.DocumentConvertPaid : mode = DocHtmlJobArguments.ModePaid
            Case Else : Return
        End Select
        preferences.DocumentProcessingMode = mode
        If mode = DocHtmlJobArguments.ModePaid AndAlso Not ApprovePaidDocumentRun(probe.ExePath, shownName, scopeNote, snapshot IsNot Nothing) Then Return
        StartDocumentConversion(probe.ExePath, path, mode, again, snapshot)
    End Sub

    ''' <summary>Step 5.1, section 5: a paid run starts only with a positive finite cap and the user's answer for this very
    ''' run. Without a cap nothing paid starts from the viewer: a document goes to the converter's own window, whose
    ''' question about the cost the user answers there (OCR-INVOCATION 1.2 item G); an editor snapshot has no file that
    ''' window could open, so it asks for the cap instead.</summary>
    Private Function ApprovePaidDocumentRun(exe As String, shownName As String, scopeNote As String, isSnapshot As Boolean) As Boolean
        Dim cap = GetModernPreferences().DocumentPaidCostLimit
        If DocHtmlJobArguments.IsAcceptableCostLimit(cap) Then
            Return AskDialogs.Choose(Me, AskSpecs.DocumentPaidApproval(shownName, cap, scopeNote)) = AskSpecs.DocumentPaidSend
        End If
        If isSnapshot Then
            Notify(Localization.T("Для перевода текста редактора через Google задайте предел расходов в настройках: Документы > doc-html-translate."), NotifyLevel.Warning)
            Return False
        End If
        If AskDialogs.Choose(Me, AskSpecs.DocumentPaidNoLimit(shownName)) = AskSpecs.DocumentPaidOpenWindow Then OpenDocumentConverterWindow(exe)
        Return False
    End Function

    ''' <summary>The converter's window, <c>doc-html-ui.exe</c> beside the verified command-line program - a handoff, never
    ''' embedded and never given the document on its command line (its documented interface has none).</summary>
    Private Sub OpenDocumentConverterWindow(exe As String)
        Dim folder = IO.Path.GetDirectoryName(exe)
        Dim window = If(String.IsNullOrEmpty(folder), "", IO.Path.Combine(folder, "doc-html-ui.exe"))
        If window.Length = 0 OrElse Not File.Exists(window) Then
            Notify(Localization.TF("Окно doc-html-translate не найдено рядом с программой: {0}", If(folder, "")), NotifyLevel.Warning)
            Return
        End If
        Try
            Using started = Process.Start(New ProcessStartInfo(window) With {.UseShellExecute = False, .WorkingDirectory = folder})
            End Using
            AppFileLogger.LogMessage("doc-html-translate window opened for a paid translation")
        Catch ex As Exception When TypeOf ex Is ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidOperationException
            AppFileLogger.LogMessage("doc-html-translate window: " & ex.GetType().Name)
            Notify(Localization.TF("Не удалось запустить doc-html-translate: {0}", FailureText.Describe(ex)), NotifyLevel.Warning)
        End Try
    End Sub

    ''' <summary>Step 4.1: the viewer destination needs the 275 host; without it the page goes to the browser.</summary>
    Private Shared Function ResultOpensInViewer(preferences As ModernViewerPreferences) As Boolean
        Return String.Equals(preferences.DocumentResultDestination, "viewer", StringComparison.Ordinal) AndAlso preferences.HtmlViewerEnabled
    End Function

    ''' <summary>winget first; when it cannot start (Server, LTSC, an old profile) the Store page. Nothing installs
    ''' without the user seeing the installer, and Check again finds an install made after the viewer started.</summary>
    Private Sub InstallDocumentConverter()
        If DocHtmlTranslate.StartWingetInstall() Then
            Notify(Localization.T("Устанавливаю doc-html-translate.. после установки нажмите кнопку doc-html-translate ещё раз"), NotifyLevel.Info)
        ElseIf Not DocHtmlTranslate.OpenStorePage() Then
            Notify(Localization.T("Не удалось открыть страницу doc-html-translate"), NotifyLevel.Warning)
        End If
    End Sub

    ''' <summary>The explicit executable of section 5 (ConverterLocation). Only the command-line program is
    ''' accepted; the choice is remembered and validated again on every run.</summary>
    Private Function ChooseDocumentConverter() As Boolean
        Using dialog As New OpenFileDialog()
            dialog.Filter = Localization.TF("Программа doc-html-translate|{0}", DocHtmlTranslate.ExeFileName)
            dialog.Title = Localization.T("Выбрать программу..")
            dialog.CheckFileExists = True
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return False
            If Not DocHtmlConverterLocator.IsAcceptableExplicitPath(dialog.FileName) Then
                Notify(Localization.TF("Нужен файл {0}", DocHtmlTranslate.ExeFileName), NotifyLevel.Warning)
                Return False
            End If
            GetModernPreferences().DocumentConverterLocation = dialog.FileName
            Return True
        End Using
    End Function

    ''' <summary>The one job the viewer owns (DocumentJobConcurrency 1); Nothing when none runs.</summary>
    Private document_Job As DocumentConvertJob

    ''' <summary>One owned job (section 7.4): the exact process the viewer started, its folder and the session that
    ''' asked for it. The stop reason is written by the UI thread or the timer and read by the exit handler.</summary>
    Private NotInheritable Class DocumentConvertJob
        Friend Id As String = ""
        ''' <summary>The file the converter reads: the document, or the snapshot written for this job.</summary>
        Friend SourcePath As String = ""
        ''' <summary>The document the user works on; for a snapshot the editor's file, which the converter never reads.</summary>
        Friend OriginPath As String = ""
        ''' <summary>The snapshot's own folder (Step 5.1), removed when the job ends; "" for a document.</summary>
        Friend SnapshotFolder As String = ""
        Friend FileName As String = ""
        Friend Mode As String = ""
        Friend JobRoot As String = ""
        Friend Generation As Integer
        Friend TimeoutMinutes As Integer
        Friend Process As Process
        Friend ReadOnly Timer As New Threading.CancellationTokenSource()
        Private stopReason As Integer

        Friend ReadOnly Property Cancelled As Boolean
            Get
                Return Threading.Volatile.Read(stopReason) = 1
            End Get
        End Property

        Friend ReadOnly Property TimedOut As Boolean
            Get
                Return Threading.Volatile.Read(stopReason) = 2
            End Get
        End Property

        ''' <summary>Ends the process tree the viewer started - never a process found by name (section 6). The first
        ''' reason wins, so a timeout that races a user's Stop is reported once.</summary>
        Friend Sub [Stop](timedOut As Boolean)
            Threading.Interlocked.CompareExchange(stopReason, If(timedOut, 2, 1), 0)
            Try
                If Process IsNot Nothing AndAlso Not Process.HasExited Then Process.Kill(entireProcessTree:=True)
            Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ComponentModel.Win32Exception OrElse
                                       TypeOf ex Is NotSupportedException
                AppFileLogger.LogMessage("doc-html-translate document job " & Id & " stop: " & ex.GetType().Name)
            End Try
        End Sub
    End Class

    ''' <summary>The command pressed while the viewer's own job runs: Stop or Continue, Continue on Escape. False when
    ''' no job runs.</summary>
    Private Function AskAboutRunningDocumentJob() As Boolean
        Dim job = document_Job
        If job Is Nothing Then Return False
        If AskDialogs.Choose(Me, AskSpecs.DocumentJobRunning(job.FileName)) = AskSpecs.DocumentJobStop Then job.Stop(False)
        Return True
    End Function

    ''' <summary>Journey step 5 (T-3, OCR-INVOCATION 1.2 item B): the verified executable, an argument list (no
    ''' shell), a fresh owned job folder, <c>-noopen</c> so the viewer opens only a verified result, the standard
    ''' streams redirected and drained (rule 4: a console run that fails waits for Enter otherwise), and a timeout
    ''' distinct from a cancel. Only the job id, mode and outcome reach the log.</summary>
    Private Async Sub StartDocumentConversion(exe As String, path As String, mode As String, again As Boolean,
                                              Optional snapshot As DocumentSnapshotRequest = Nothing)
        If document_Job IsNot Nothing Then Return
        Dim preferences = GetModernPreferences()
        Dim root As String = DocumentConvertCacheRoot()
        Dim jobId As String = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) & "-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim job As New DocumentConvertJob With {.Id = jobId, .SourcePath = path, .OriginPath = path, .FileName = IO.Path.GetFileName(path), .Mode = mode,
                                                .JobRoot = IO.Path.Combine(root, jobId), .Generation = media_Generation,
                                                .TimeoutMinutes = preferences.DocumentJobTimeoutMinutes}
        If snapshot IsNot Nothing Then
            job.SnapshotFolder = IO.Path.Combine(DocumentSnapshotRoot(), jobId)
            job.FileName = snapshot.DisplayName
        End If
        document_Job = job
        Dim options = DocumentJobOptions(preferences)
        Dim request = DocHtmlJobCache.TicketFor(path, mode, options)
        ' A snapshot is new text every time; reuse compares a file's path, size and write time, which a snapshot has not.
        Dim reuse = preferences.DocumentReuseResults AndAlso Not again AndAlso snapshot Is Nothing
        Dim budgetMb = preferences.DocumentResultCacheMb
        Dim retentionDays = preferences.DocumentResultRetentionDays
        Dim reused As (JobRoot As String, Result As DocHtmlResult)? = Nothing
        Dim usedBytes As Long
        Try
            ' Section 6 and 7.3 rule 5, off the UI thread: the source state, a finished page of the same request, then
            ' retention and the quota - a page a browser may still show is never removed by this pass.
            Await Task.Run(Sub()
                               If snapshot Is Nothing Then
                                   Dim source As New FileInfo(path)
                                   request.SourceLength = source.Length
                                   request.SourceWriteUtcTicks = source.LastWriteTimeUtc.Ticks
                               End If
                               If reuse Then
                                   reused = DocHtmlJobCache.FindReusable(root, request)
                                   If reused.HasValue Then Return
                               End If
                               Directory.CreateDirectory(root)
                               DocHtmlJobCache.ApplyRetention(root, retentionDays, DateTime.UtcNow, "")
                               DiskCacheTrim.TrimToBudget(root, budgetMb, entries_Are_Folders:=True, keep:=DocHtmlJobCache.LeasedFolders(root))
                               usedBytes = DocHtmlJobCache.TotalBytes(root)
                           End Sub).ConfigureAwait(True)
        Catch ex As Exception
            document_Job = Nothing
            job.Timer.Dispose()
            AppFileLogger.LogException("doc-html-translate document job folder", ex)
            Notify(Localization.TF("Не удалось подготовить папку результата: {0}", FailureText.Describe(ex)), NotifyLevel.Warning)
            Return
        End Try
        If IsDisposed OrElse Disposing Then
            document_Job = Nothing
            job.Timer.Dispose()
            Return
        End If

        If reused.HasValue Then
            document_Job = Nothing
            job.Timer.Dispose()
            AppFileLogger.LogMessage(String.Format(CultureInfo.InvariantCulture, "doc-html-translate document job mode={0} reused", mode))
            Notify(Localization.TF("«{0}»: документ не менялся - открыта страница, сделанная раньше.", job.FileName), NotifyLevel.Info)
            PresentDocumentPage(job, reused.Value.Result.EntryPath, reused.Value.JobRoot, announceReady:=False)
            Return
        End If

        If usedBytes > CLng(budgetMb) * 1024L * 1024L Then
            Dim usedMb = (usedBytes + 1024L * 1024L - 1) \ (1024L * 1024L)
            If AskDialogs.Choose(Me, AskSpecs.DocumentResultsFull(usedMb, budgetMb)) <> AskSpecs.DocumentResultsClear Then
                document_Job = Nothing
                job.Timer.Dispose()
                Return
            End If
            Dim cleared = Await Task.Run(Function() DocHtmlJobCache.ClearAll(root, "")).ConfigureAwait(True)
            AppFileLogger.LogMessage(String.Format(CultureInfo.InvariantCulture, "doc-html-translate results cleared before a job: removed={0} kept={1}", cleared.Removed, cleared.Kept))
            If IsDisposed OrElse Disposing Then
                document_Job = Nothing
                job.Timer.Dispose()
                Return
            End If
        End If

        Dim written As DocHtmlSnapshotFile = Nothing
        Try
            Await Task.Run(Sub()
                               If snapshot IsNot Nothing Then
                                   ' Step 5.1: the immutable input, written apart from the result cache; a folder a viewer that
                                   ' ended mid-job left behind goes first.
                                   DocHtmlSnapshot.SweepStale(DocumentSnapshotRoot(), DateTime.UtcNow, "")
                                   written = DocHtmlSnapshot.Write(job.SnapshotFolder, snapshot.OriginPath, snapshot.Scope, snapshot.Text)
                                   If written Is Nothing Then Return
                                   job.SourcePath = written.Path
                                   request.SourcePath = written.Path
                                   request.SourceLength = written.Length
                               End If
                               Directory.CreateDirectory(job.JobRoot)
                               DocHtmlJobCache.WriteTicket(job.JobRoot, request)
                           End Sub).ConfigureAwait(True)
        Catch ex As Exception
            AbandonDocumentJob(job)
            AppFileLogger.LogException("doc-html-translate document job folder", ex)
            Notify(Localization.TF("Не удалось подготовить папку результата: {0}", FailureText.Describe(ex)), NotifyLevel.Warning)
            Return
        End Try
        If snapshot IsNot Nothing AndAlso written Is Nothing Then
            AbandonDocumentJob(job)
            Notify(Localization.T("Текст слишком большой или пустой - doc-html-translate его не получит."), NotifyLevel.Warning)
            Return
        End If
        If IsDisposed OrElse Disposing Then
            AbandonDocumentJob(job)
            Return
        End If

        Dim args = DocHtmlJobArguments.Build(job.SourcePath, job.JobRoot, mode, False, noOpen:=True, options:=options)
        If args Is Nothing Then
            AbandonDocumentJob(job)
            Notify(Localization.TF("Не удалось открыть файл: {0}", job.FileName), NotifyLevel.Warning)
            Return
        End If
        Dim psi As New ProcessStartInfo(exe) With {.UseShellExecute = False, .CreateNoWindow = True, .WorkingDirectory = job.JobRoot,
                                                   .RedirectStandardOutput = True, .RedirectStandardError = True}
        For Each item In args
            psi.ArgumentList.Add(item)
        Next
        Dim proc As New Process With {.StartInfo = psi, .EnableRaisingEvents = True}
        job.Process = proc
        Try
            ' Drained and dropped: the producer's console text may carry paths and document titles (section 5).
            AddHandler proc.OutputDataReceived, Sub(sender, e)
                                                End Sub
            AddHandler proc.ErrorDataReceived, Sub(sender, e)
                                               End Sub
            AddHandler proc.Exited, Sub() OnDocumentJobExited(job)
            proc.Start()
            proc.BeginOutputReadLine()
            proc.BeginErrorReadLine()
            AppFileLogger.LogMessage(String.Format(CultureInfo.InvariantCulture, "doc-html-translate document job {0} mode={1} started", jobId, mode))
            Notify(If(Not preferences.DocumentOpenResultAutomatically, Localization.TF("doc-html-translate обрабатывает «{0}».", job.FileName),
                      If(snapshot Is Nothing AndAlso ResultOpensInViewer(preferences),
                         Localization.TF("doc-html-translate обрабатывает «{0}». Страница откроется в просмотрщике.", job.FileName),
                         Localization.TF("doc-html-translate обрабатывает «{0}». Страница откроется в браузере.", job.FileName))), NotifyLevel.Info)
            Dim timeout = Task.Delay(TimeSpan.FromMinutes(job.TimeoutMinutes), job.Timer.Token).ContinueWith(
                Sub(t)
                    If Not t.IsCanceled Then job.Stop(True)
                End Sub, TaskScheduler.Default)
        Catch ex As Exception
            AbandonDocumentJob(job)
            proc.Dispose()
            AppFileLogger.LogException("doc-html-translate document job launch", ex)
            Notify(Localization.TF("Не удалось запустить doc-html-translate: {0}", FailureText.Describe(ex)), NotifyLevel.Warning)
        End Try
    End Sub

    ''' <summary>Runs on the process's exit thread: the folder is read and verified here, off the UI thread, and only
    ''' the verdict is marshalled back.</summary>
    Private Sub OnDocumentJobExited(job As DocumentConvertJob)
        Dim code As Integer = -1
        Try
            code = job.Process.ExitCode
        Catch ex As InvalidOperationException
        End Try
        job.Timer.Cancel()
        Dim result = DocHtmlResultReader.Read(job.JobRoot, job.SourcePath)
        Dim outcome = DocHtmlResultReader.Outcome(result, code, job.Cancelled, job.TimedOut)
        ' The snapshot was this job's input only: the page in the job folder no longer needs it (Step 5.1).
        If job.SnapshotFolder.Length > 0 AndAlso Not DocHtmlSnapshot.TryDelete(job.SnapshotFolder) Then
            AppFileLogger.LogMessage("doc-html-translate document job " & job.Id & " snapshot left for the next sweep")
        End If
        AppFileLogger.LogMessage(String.Format(CultureInfo.InvariantCulture, "doc-html-translate document job {0} mode={1} exit={2} outcome={3} tool={4}",
                                               job.Id, job.Mode, code, outcome, result.ToolVersion))
        If IsDisposed OrElse Disposing OrElse Not IsHandleCreated Then
            DisposeDocumentJob(job)
            Return
        End If
        Try
            BeginInvoke(Sub() FinishDocumentJob(job, outcome, result, code))
        Catch ex As InvalidOperationException
            DisposeDocumentJob(job)
        End Try
    End Sub

    ''' <summary>A job that never started its process: the slot is free again and its snapshot, if any, goes.</summary>
    Private Sub AbandonDocumentJob(job As DocumentConvertJob)
        If ReferenceEquals(document_Job, job) Then document_Job = Nothing
        job.Timer.Dispose()
        If job.SnapshotFolder.Length > 0 Then
            Dim folder = job.SnapshotFolder
            Task.Run(Sub() DocHtmlSnapshot.TryDelete(folder))
        End If
    End Sub

    Private Shared Sub DisposeDocumentJob(job As DocumentConvertJob)
        job.Timer.Dispose()
        job.Process?.Dispose()
    End Sub

    ''' <summary>Journey step 6: the honest outcome (OCR-INVOCATION 1.2 item D, never "exit 0" alone). A page opens
    ''' in the browser at once only while the initiating document is still on screen; a result of an earlier
    ''' document is announced with its place and never replaces what the user has opened since.</summary>
    Private Sub FinishDocumentJob(job As DocumentConvertJob, outcome As DocHtmlJobOutcome, result As DocHtmlResult, code As Integer)
        If ReferenceEquals(document_Job, job) Then document_Job = Nothing
        DisposeDocumentJob(job)
        Dim name = job.FileName
        Select Case outcome
            Case DocHtmlJobOutcome.PartiallyTranslated
                Notify(Localization.TF("«{0}»: страница готова, но перевод выполнен не полностью - часть текста осталась на исходном языке.", name), NotifyLevel.Warning)
            Case DocHtmlJobOutcome.TranslationSkipped
                Notify(Localization.TF("«{0}»: перевод пропущен - страница только преобразована.", name), NotifyLevel.Warning)
            Case DocHtmlJobOutcome.TranslationUnavailable
                Notify(Localization.TF("«{0}»: служба перевода недоступна - страница только преобразована.", name), NotifyLevel.Warning)
            Case DocHtmlJobOutcome.Cancelled
                Notify(Localization.TF("Обработка «{0}» прервана.", name), NotifyLevel.Warning)
            Case DocHtmlJobOutcome.TimedOut
                Notify(Localization.TF("Обработка «{0}» остановлена: она шла дольше {1} мин.", name, job.TimeoutMinutes), NotifyLevel.Warning)
            Case DocHtmlJobOutcome.Unreadable
                Notify(Localization.TF("doc-html-translate не смогла прочитать «{0}».", name), NotifyLevel.Warning)
            Case DocHtmlJobOutcome.Incomplete
                Notify(Localization.TF("doc-html-translate не закончила страницу для «{0}».", name), NotifyLevel.Warning)
            Case DocHtmlJobOutcome.NewerSchema
                Notify(Localization.TF("Результат для «{0}» записан более новой версией doc-html-translate и не открыт.", name), NotifyLevel.Warning)
            Case DocHtmlJobOutcome.Unsafe
                Notify(Localization.TF("Результат для «{0}» не прошёл проверку и не открыт.", name), NotifyLevel.Warning)
            Case DocHtmlJobOutcome.BadArguments, DocHtmlJobOutcome.IoError, DocHtmlJobOutcome.Failed
                Notify(Localization.TF("doc-html-translate завершилась с ошибкой (код {0}).", code), NotifyLevel.Warning)
        End Select
        If Not DocHtmlResultReader.HasPage(outcome) Then Return
        PresentDocumentPage(job, result.EntryPath, job.JobRoot, announceReady:=True)
    End Sub

    ''' <summary>A verified page opens only while its document is still on screen and the user wants it opened
    ''' (DocumentOpenResultAutomatically): in the 275 host over the document (Step 4.1, DocumentResultDestination) or in the
    ''' browser. A page shown in either place is leased in its folder's ticket, so no automatic cleanup removes it, across
    ''' restarts too (section 6); with DocumentKeepCompletedResults every finished page is. Otherwise the page is announced
    ''' with its place.</summary>
    Private Sub PresentDocumentPage(job As DocumentConvertJob, entryPath As String, jobRoot As String, announceReady As Boolean)
        Dim preferences = GetModernPreferences()
        Dim current = job.Generation = media_Generation AndAlso Not IsDisposed
        Dim opened As Boolean
        If current AndAlso preferences.DocumentOpenResultAutomatically Then
            ' A snapshot's page goes to the browser: over the editor it would release the text the user is still editing.
            If job.SnapshotFolder.Length = 0 AndAlso ResultOpensInViewer(preferences) Then
                ShowDocumentResultInViewer(New DocumentResultView With {.SourcePath = job.OriginPath, .FileName = job.FileName,
                                                                        .JobRoot = jobRoot, .EntryPath = entryPath})
                opened = True
            Else
                Try
                    opened = HtmlBrowserLauncher.OpenLocalFile(entryPath)
                Catch ex As Exception When TypeOf ex Is ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidOperationException
                    AppFileLogger.LogMessage("doc-html-translate document page browser: " & ex.GetType().Name)
                End Try
                If opened AndAlso job.SnapshotFolder.Length = 0 AndAlso String.Equals(preferences.DocumentResultDestination, "viewer", StringComparison.Ordinal) Then
                    Notify(Localization.T("Просмотр HTML выключен в настройках, поэтому страница открыта в браузере."), NotifyLevel.Info)
                End If
            End If
        End If
        If opened OrElse preferences.DocumentKeepCompletedResults Then LeaseDocumentResult(jobRoot)
        If Not opened AndAlso (announceReady OrElse current) Then
            ' A warning before this already named the outcome; a page that did not open is still announced with its place.
            Notify(Localization.TF("«{0}»: страница готова: {1}", job.FileName, entryPath), NotifyLevel.Info)
        End If
    End Sub

    Private Shared Sub LeaseDocumentResult(jobRoot As String)
        Task.Run(Sub()
                     If Not DocHtmlJobCache.Lease(jobRoot) Then AppFileLogger.LogMessage("doc-html-translate document page lease not written")
                 End Sub)
    End Sub

    ''' <summary>A verified result shown in the 275 host (Step 4.1): the document it came from and the job folder that holds
    ''' it. The result is never a file of the sorting list - the document stays the current file underneath.</summary>
    Private NotInheritable Class DocumentResultView
        Friend SourcePath As String = ""
        Friend FileName As String = ""
        Friend JobRoot As String = ""
        Friend EntryPath As String = ""

        ''' <summary>The converter's own folder inside the job folder: the page and everything it links to.</summary>
        Friend ReadOnly Property ResultFolder As String
            Get
                Return IO.Path.GetDirectoryName(EntryPath)
            End Get
        End Property
    End Class

    ''' <summary>Set while the HTML view shows a converter result; cleared by <c>ReleaseHtmlView</c>.</summary>
    Private document_Result_View As DocumentResultView

    ''' <summary>Journey step 7: the page over the document, the way an online page stands over the folder's file - every
    ''' file verb refuses while it is on screen (is_Stream_On_Screen) and the next navigation leaves it. Only the result's
    ''' own folder is the page's root, so nothing outside it loads. A page the host cannot open returns to the document.</summary>
    Private Async Sub ShowDocumentResultInViewer(view As DocumentResultView)
        If is_Vlc_Playing Then RememberCurrentVideoPosition()
        media_Generation += 1
        StopAnimationVideo()
        open_Coordinator.NoteNavigation()
        If IsSecureContentOnScreen() Then
            If is_Vlc_Playing Then StopVlcPlayback()
            SecureReleaseCurrent()
        End If
        ReleasePdfDocument()
        ReleaseEpubReader()
        ReleaseTextDocument()
        ReleaseHtmlView()
        ClearAudioSurface()
        BlankActiveMediaSurface()
        is_Stream_On_Screen = True
        Dim generation = media_Generation
        Dim local As LocalHtmlDocumentSource = Nothing
        Dim status = LocalHtmlDocumentSource.Open(view.EntryPath, view.ResultFolder, False,
                                                  CLng(GetModernPreferences().HtmlSnapshotCacheMb) * 1024L * 1024L, local)
        If status <> HtmlOutcome.Ok Then
            Notify(HtmlViewerText.Outcome(status), NotifyLevel.Warning)
            ReturnToDocumentOriginal(view)
            Return
        End If
        html_View_File = view.EntryPath
        html_View_Identity = HtmlViewIdentity(view.EntryPath)
        html_View_Online = Nothing
        document_Result_View = view
        AppFileLogger.LogMessage("doc-html-translate document page shown in the viewer")
        Await OpenHtmlHostAsync(local, Nothing, False, generation, "")
        If generation = media_Generation AndAlso Not IsDisposed AndAlso Not IsHtmlViewShown() Then ReturnToDocumentOriginal(view)
    End Sub

    ''' <summary>Open original: the document again, through the ordinary display path, so its reader restores its own place,
    ''' zoom and unsaved text. A document the user has left meanwhile is opened by its path.</summary>
    Private Sub ReturnToDocumentOriginal(view As DocumentResultView)
        If view Is Nothing Then Return
        ReleaseHtmlView()
        If DocHtmlResultReader.SamePath(Current_File_Name, view.SourcePath) AndAlso File.Exists(view.SourcePath) Then
            ReadShowMediaFile(Mode_SetFile)
        Else
            ProcessArgument(view.SourcePath)
        End If
    End Sub

    ''' <summary>The result's menu on the document panel (journey step 7). Open in browser is the panel's own button.</summary>
    Private Function DocumentResultMenu() As IEnumerable(Of ChromeMenuItem)
        Dim entries As New List(Of ChromeMenuItem)()
        Dim view = document_Result_View
        If view Is Nothing Then Return entries
        entries.Add(New ChromeMenuItem("doc_result_original") With {.Caption = Localization.T("Открыть исходный документ"), .GlyphId = "nav.back",
                                                                    .Action = Sub() ReturnToDocumentOriginal(view)})
        entries.Add(New ChromeMenuItem("doc_result_browser") With {.Caption = Localization.T("Открыть в браузере"), .GlyphId = "nav.open-external",
                                                                   .Action = AddressOf OpenHtmlViewInBrowser})
        entries.Add(New ChromeMenuItem("doc_result_folder") With {.Caption = Localization.T("Показать папку результата"), .GlyphId = "content.folder",
                                                                  .Action = Sub() ShowDocumentResultFolder(view)})
        entries.Add(New ChromeMenuItem("doc_result_save") With {.Caption = Localization.T("Сохранить результат.."), .GlyphId = "action.save",
                                                                .Action = Sub() SaveDocumentResult(view)})
        entries.Add(ChromeMenuItem.Separator())
        entries.Add(New ChromeMenuItem("doc_result_again") With {.Caption = Localization.T("Обработать снова"), .GlyphId = "action.refresh",
                                                                 .Action = Sub() BeginDocumentConversion(view.SourcePath, again:=True)})
        Return entries
    End Function

    Private Sub ShowDocumentResultFolder(view As DocumentResultView)
        Try
            Process.Start(New ProcessStartInfo("explorer.exe", "/select,""" & view.EntryPath & """") With {.UseShellExecute = True})
        Catch ex As Exception When TypeOf ex Is ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidOperationException
            AppFileLogger.LogMessage("doc-html-translate result folder: " & ex.GetType().Name)
            Notify(Localization.T("Файл недоступен"), NotifyLevel.Warning)
        End Try
    End Sub

    ''' <summary>Save result (section 6): the complete page folder - never the entry file alone - copied off the UI thread to
    ''' a new folder the user picks the place of. The copy is the user's file: no cache cleanup knows about it.</summary>
    Private Async Sub SaveDocumentResult(view As DocumentResultView)
        Dim destination As String
        Using dialog As New FolderBrowserDialog With {.Description = Localization.T("Сохранить результат.."), .UseDescriptionForTitle = True,
                                                      .ShowNewFolderButton = True}
            Try
                Dim source = Path.GetDirectoryName(view.SourcePath)
                If Not String.IsNullOrEmpty(source) AndAlso Directory.Exists(source) Then dialog.InitialDirectory = source
            Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is PathTooLongException
            End Try
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            destination = dialog.SelectedPath
        End Using
        Try
            Dim saved = Await Task.Run(Function() DocHtmlJobCache.SaveResultCopy(view.ResultFolder, destination)).ConfigureAwait(True)
            AppFileLogger.LogMessage("doc-html-translate result saved")
            Notify(Localization.TF("Результат сохранён: {0}", saved), NotifyLevel.Info)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is Security.SecurityException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException
            AppFileLogger.LogMessage("doc-html-translate result save: " & ex.GetType().Name)
            Notify(Localization.TF("Не удалось сохранить результат: {0}", FailureText.Describe(ex)), NotifyLevel.Warning)
        End Try
    End Sub

    ''' <summary>Section 5 to the argument builder. An empty target language is the interface language at the time of
    ''' the run (section 5: the initial choice); a value the builder would refuse never reaches it (normalized).</summary>
    Private Shared Function DocumentJobOptions(preferences As ModernViewerPreferences) As DocHtmlJobOptions
        Dim target = preferences.DocumentTargetLanguage
        If target.Length = 0 AndAlso DocHtmlJobArguments.IsLanguageList(Localization.CurrentCode) Then target = Localization.CurrentCode
        Return New DocHtmlJobOptions With {
            .OcrForce = String.Equals(preferences.DocumentOcrMode, "force", StringComparison.Ordinal),
            .OcrLanguages = preferences.DocumentOcrLanguages, .SourceLanguage = preferences.DocumentSourceLanguage,
            .TargetLanguage = target, .LocalModel = preferences.DocumentLocalModel, .PaidCostLimit = preferences.DocumentPaidCostLimit}
    End Function

    ''' <summary>The settings window's Clear (section 6): every job folder except the one a running job writes.</summary>
    Friend Async Function ClearDocumentResultsAsync() As Task(Of (Removed As Integer, Kept As Integer))
        Dim running As String = If(document_Job?.JobRoot, "")
        Dim root As String = DocumentConvertCacheRoot()
        Dim cleared = Await Task.Run(Function() DocHtmlJobCache.ClearAll(root, running)).ConfigureAwait(True)
        AppFileLogger.LogMessage(String.Format(CultureInfo.InvariantCulture, "doc-html-translate results cleared: removed={0} kept={1}", cleared.Removed, cleared.Kept))
        Return cleared
    End Function

    ''' <summary>The viewer closes: the job it started ends with it, so no converter keeps writing into a cache
    ''' nobody reads (the producer offers no safe detachment, OCR-INVOCATION 1.2 item H).</summary>
    Private Sub StopDocumentJobOnClose(sender As Object, e As FormClosedEventArgs) Handles MyBase.FormClosed
        document_Job?.Stop(False)
    End Sub
End Class
#End If
