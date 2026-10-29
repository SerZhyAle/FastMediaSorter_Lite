#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Globalization
Imports System.IO
Imports System.Threading.Tasks

' Specification 276 T-2: the document command of doc-html-translate behind DocumentConverterEnabled. The image
' route (Main_Form.BrowserTranslate.vb, DocHtmlTranslateLauncher.vb) is frozen and only read from here.
Partial Public Class Main_Form
    ''' <summary>The document jobs' cache budget; the oldest job folders go first, so the job just started is kept.</summary>
    Private Const DocumentConvertCacheBudgetMb As Integer = 1024
    Private document_Convert_Busy As Boolean

    ''' <summary>%LOCALAPPDATA%\SZA\FastMediaSorter\document-convert-cache - one folder per job, never beside the
    ''' user's document (section 6).</summary>
    Private Shared Function DocumentConvertCacheRoot() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            App_name, Second_App_Name, "document-convert-cache")
    End Function

    ''' <summary>Journey steps 1-4: readiness off the UI thread, then the install offer or the job panel. A result
    ''' that arrives after the user has moved to another file is dropped, so the panel never names a document that
    ''' is no longer on screen.</summary>
    Private Async Sub BeginDocumentConversion(path As String)
        If document_Convert_Busy Then Return
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
                    If ChooseDocumentConverter() Then BeginDocumentConversion(path)
                Case AskSpecs.DocumentConverterCheckAgain
                    BeginDocumentConversion(path)
            End Select
            Return
        End If

        Dim preferences = GetModernPreferences()
        Dim mode As String
        Select Case AskDialogs.Choose(Me, AskSpecs.DocumentConvertPanel(IO.Path.GetFileName(path), probe.VersionText, preferences.DocumentProcessingMode))
            Case AskSpecs.DocumentConvertBrowser : mode = DocHtmlJobArguments.ModeBrowser
            Case AskSpecs.DocumentConvertOnly : mode = DocHtmlJobArguments.ModeConvert
            Case AskSpecs.DocumentConvertLocal : mode = DocHtmlJobArguments.ModeLocal
            Case Else : Return
        End Select
        preferences.DocumentProcessingMode = mode
        StartDocumentConversion(probe.ExePath, path, mode)
    End Sub

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

    ''' <summary>Journey step 5 for T-2: the verified executable, an argument list (no shell), a fresh owned job
    ''' folder. The converter opens its own result (no -noopen until the producer profile exists, 2.5); its exit
    ''' code is reported by meaning, and only the job id, mode and code reach the log.</summary>
    Private Async Sub StartDocumentConversion(exe As String, path As String, mode As String)
        Dim root As String = DocumentConvertCacheRoot()
        Dim jobId As String = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) & "-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim jobRoot As String = IO.Path.Combine(root, jobId)
        Dim fileName As String = IO.Path.GetFileName(path)
        Try
            Await Task.Run(Sub()
                               Directory.CreateDirectory(root)
                               DiskCacheTrim.TrimToBudget(root, DocumentConvertCacheBudgetMb, entries_Are_Folders:=True)
                               Directory.CreateDirectory(jobRoot)
                           End Sub).ConfigureAwait(True)
        Catch ex As Exception
            AppFileLogger.LogException("doc-html-translate document job folder", ex)
            Notify(Localization.TF("Не удалось подготовить папку результата: {0}", FailureText.Describe(ex)), NotifyLevel.Warning)
            Return
        End Try
        If IsDisposed OrElse Disposing Then Return

        Dim args = DocHtmlJobArguments.Build(path, jobRoot, mode, False)
        If args Is Nothing Then
            Notify(Localization.TF("Не удалось открыть файл: {0}", fileName), NotifyLevel.Warning)
            Return
        End If
        Dim psi As New ProcessStartInfo(exe) With {.UseShellExecute = False, .CreateNoWindow = True, .WorkingDirectory = jobRoot}
        For Each item In args
            psi.ArgumentList.Add(item)
        Next
        Dim proc As New Process With {.StartInfo = psi, .EnableRaisingEvents = True}
        Try
            AddHandler proc.Exited,
                Sub()
                    Dim code As Integer = -1
                    Try
                        code = proc.ExitCode
                    Catch ex As InvalidOperationException
                    End Try
                    proc.Dispose()
                    AppFileLogger.LogMessage(String.Format(CultureInfo.InvariantCulture, "doc-html-translate document job {0} mode={1} exit={2}", jobId, mode, code))
                    If IsDisposed OrElse Disposing OrElse Not IsHandleCreated Then Return
                    Try
                        BeginInvoke(Sub() ReportDocumentConversionExit(code, fileName))
                    Catch ex As InvalidOperationException
                    End Try
                End Sub
            proc.Start()
            AppFileLogger.LogMessage(String.Format(CultureInfo.InvariantCulture, "doc-html-translate document job {0} mode={1} started", jobId, mode))
            Notify(Localization.TF("doc-html-translate обрабатывает «{0}». Страница откроется в браузере.", fileName), NotifyLevel.Info)
        Catch ex As Exception
            proc.Dispose()
            AppFileLogger.LogException("doc-html-translate document job launch", ex)
            Notify(Localization.TF("Не удалось запустить doc-html-translate: {0}", FailureText.Describe(ex)), NotifyLevel.Warning)
        End Try
    End Sub

    ''' <summary>The producer's exit codes (2.2). Exit 4 still produced and opened the book, so it is a partial
    ''' translation, not a failure; exit 0 needs no news, the page is already in the browser.</summary>
    Private Sub ReportDocumentConversionExit(code As Integer, fileName As String)
        Select Case code
            Case 0
            Case 4
                Notify(Localization.TF("«{0}»: страница готова, но перевод выполнен не полностью - часть текста осталась на исходном языке.", fileName), NotifyLevel.Warning)
            Case 3
                Notify(Localization.TF("doc-html-translate не смогла прочитать «{0}».", fileName), NotifyLevel.Warning)
            Case 130
                Notify(Localization.TF("Обработка «{0}» прервана.", fileName), NotifyLevel.Warning)
            Case Else
                Notify(Localization.TF("doc-html-translate завершилась с ошибкой (код {0}).", code), NotifyLevel.Warning)
        End Select
    End Sub
End Class
#End If
