Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' OCR + Translation overlay - pipeline, toolbar control, hotkey entry points and
' the settings dialog. Overlay PAINTING lives in Main_Form.OcrOverlay.vb.
'
' Threading model (spec 6.6): a separate, serialized Task pipeline with
' latest-wins cancellation. It never reuses BgWorker (which preloads images and
' metadata), so OCR latency cannot stutter navigation. Each navigation cancels
' the in-flight job and clears the overlay; results are only applied after
' re-checking that the displayed file is still the one that was OCR'd.
Partial Public Class Main_Form

    Private ocr_Settings As OcrTranslateSettings
    Private ocr_Engine As IOcrEngine
    Private ocr_Cache As TranslationCache
    Private current_Overlay_Document As OcrOverlayDocument
    Private ocr_Overlay_Visible As Boolean = True
    Private ocr_Job_Cts As CancellationTokenSource

    ''' <summary>Quarter turns applied on screen (R / Shift+R) since the current picture was
    ''' displayed, clockwise positive, 0..3. Part of the cache key: the file does not change
    ''' when the picture is turned, but the grid the boxes are measured in does (audit 051
    ''' E-01). Reset on every display, because a fresh load is upright again.</summary>
    Private ocr_Quarter_Turns As Integer

    Private WithEvents btn_Translate As Button
    Private WithEvents ocr_Auto_Timer As New System.Windows.Forms.Timer()

    Private NotInheritable Class OcrJob
        Public FilePath As String
        Public FileWriteTicks As Long
        Public Bitmap As Bitmap
        Public ImageSize As Size
        Public QuarterTurns As Integer
        Public ExifAutoRotate As Boolean
        Public OcrSourceHint As String
        Public SourceLang As String
        Public TargetLang As String
#If Not NETFRAMEWORK Then
        ''' <summary>False for a secure container: the disk cache keys on path and write
        ''' time and persists RECOGNISED TEXT, which for a container is the text of a
        ''' secret (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §5.4 rule 2). The
        ''' memory cache still works, so re-showing the same page costs nothing; nothing
        ''' reaches the disk in either direction.</summary>
        Public AllowDiskCache As Boolean = True
        ''' <summary>True for a picture out of a secure container: its recognised text is
        ''' the text of a secret and never goes into the developer dump either (audit 052
        ''' E-12) - "not a log" includes a log someone switched on by hand.</summary>
        Public IsSealedContent As Boolean
#End If
    End Class

    ' --- setup / teardown -----------------------------------------------------

    ''' <summary>Creates the toolbar "Translate" button. Called from
    ''' BuildModernLayout BEFORE styling so it inherits the uniform chrome.</summary>
    Friend Sub BuildOcrToolbarControls(host As Panel)
        If btn_Translate IsNot Nothing OrElse host Is Nothing Then Return
        btn_Translate = New Button With {
            .Name = "btn_Translate",
            .Text = "Translate",
            .AutoSize = True,
            .TabStop = False
        }
        host.Controls.Add(btn_Translate)
    End Sub

    Friend Sub InitializeOcrTranslate()
        ocr_Settings = New OcrTranslateSettings()
        ocr_Settings.Load(Localization.CurrentCode)
        ocr_Overlay_Visible = ocr_Settings.OverlayVisible
        ocr_Engine = New TesseractOcrEngine()
        ocr_Cache = New TranslationCache()
        ApplyOcrCacheBudget()
        ApplyEngineOptions()
        ocr_Auto_Timer.Interval = 350
        ocr_Auto_Timer.Stop()
        UpdateOcrButtonVisual()
        If toolTip IsNot Nothing AndAlso btn_Translate IsNot Nothing Then
            toolTip.SetToolTip(btn_Translate, Localization.T("OCR + перевод текущего изображения (T) - притворимся, что понимаем эту мангу. ПКМ - настройки. Shift+T - авто-режим."))
        End If
    End Sub

    ''' <summary>
    ''' Hands the user's disk-cache limit to the cache that has to honour it. The setting only
    ''' exists on the modern build (ModernViewerPreferences); net48 keeps the cache's own
    ''' default, which is the point - both builds are bounded either way, and until now
    ''' NEITHER was.
    ''' </summary>
    Friend Sub ApplyOcrCacheBudget()
        If ocr_Cache Is Nothing Then Return
#If Not NETFRAMEWORK Then
        If modern_Preferences IsNot Nothing Then ocr_Cache.DiskBudgetMb = modern_Preferences.OcrDiskCacheMaxMb
#End If
    End Sub

    Friend Sub SaveOcrSettings()
        If MultiWindowPolicy.IsSecondaryInstance() Then Return
        If ocr_Settings IsNot Nothing Then ocr_Settings.Save()
    End Sub

    ''' <summary>Pushes the OCR model(quality)/mode settings onto the engine.</summary>
    Private Sub ApplyEngineOptions()
        Dim te As TesseractOcrEngine = TryCast(ocr_Engine, TesseractOcrEngine)
        If te Is Nothing OrElse ocr_Settings Is Nothing Then Return
        te.PreferBest = String.Equals(ocr_Settings.OcrModelQuality, "best", StringComparison.OrdinalIgnoreCase)
        te.ForcedPageMode = ocr_Settings.OcrPageMode
    End Sub

    Friend Sub ShutdownOcrTranslate()
        CancelOcrJob()
        Try
            ocr_Auto_Timer.Stop()
        Catch
        End Try
        ' Never wait for a pass in flight: cancellation cannot interrupt engine.Process, and
        ' waiting for it on the UI thread hung the window for seconds on close with "best"
        ' models (audit 051 E-15). Process exit reclaims an engine that is still busy.
        Dim te As TesseractOcrEngine = TryCast(ocr_Engine, TesseractOcrEngine)
        If te IsNot Nothing Then te.RetireEngine()
    End Sub

    ' --- navigation hook ------------------------------------------------------

    ''' <summary>Called whenever a new media file finishes displaying.</summary>
    Friend Sub OnMediaDisplayed()
        If ocr_Settings Is Nothing Then Return

        ' The overlay belonged to the previous image: cancel + clear.
        CancelOcrJob()
        ocr_Quarter_Turns = 0
        current_Overlay_Document = Nothing
        InvalidateOverlay()
        UpdateOcrButtonVisual()

        ' Both translate buttons are visible only for a still image (browser
        ' companion additionally needs doc-html-translate installed). This is the
        ' single, final recompute point - see SPECIFICATION_DOC_HTML_TRANSLATE_INTEGRATION.md §4.3.
        ApplyTranslateButtonsVisibility()

        ocr_Auto_Timer.Stop()
        If ocr_Settings.Enabled AndAlso ocr_Settings.AutoMode AndAlso IsCurrentFileEligibleImage() Then
            ocr_Auto_Timer.Start() ' debounce; tick runs the job
        End If
    End Sub

    Private Sub ocr_Auto_Timer_Tick(sender As Object, e As EventArgs) Handles ocr_Auto_Timer.Tick
        ocr_Auto_Timer.Stop()
        TranslateCurrentImage(True)
    End Sub

    ' --- hotkey / button entry points -----------------------------------------

    ''' <summary>T key / button: translate, or toggle overlay if already done.</summary>
    Friend Sub TriggerOcrHotkey()
        If ocr_Settings Is Nothing Then Return
        ' A partial document - shown, never cached - is the one state where T must run
        ' again rather than hide the plates: "T will retry" is what its status line says.
        If CurrentDocBelongsToCurrentFile() AndAlso Not current_Overlay_Document.IsPartial Then
            ToggleOverlayVisibility()
        Else
            TranslateCurrentImage(False)
        End If
    End Sub

    ''' <summary>
    ''' The picture on screen was turned a quarter (R / Shift+R). Its boxes are now in a
    ''' different grid, so the overlay goes, a job still running on the old grid is cancelled
    ''' - it used to finish and paint the pre-rotation plates over the turned picture - and
    ''' the turn is counted so the next run is filed under the new grid (audit 051 E-01).
    ''' </summary>
    Friend Sub OnActiveImageRotated(clockwise As Boolean)
        CancelOcrJob()
        ocr_Quarter_Turns = (ocr_Quarter_Turns + If(clockwise, 1, 3)) Mod 4
        current_Overlay_Document = Nothing
        InvalidateOverlay()
        UpdateOcrButtonVisual()
    End Sub

    ''' <summary>Shift+T: toggle auto mode.</summary>
    Friend Sub ToggleOcrAutoMode()
        If ocr_Settings Is Nothing Then Return
        ocr_Settings.Enabled = True
        ocr_Settings.AutoMode = Not ocr_Settings.AutoMode
        SetOcrStatus(Localization.T(If(ocr_Settings.AutoMode, "Авто-перевод включён", "Авто-перевод выключен")))
        UpdateOcrButtonVisual()
        If ocr_Settings.AutoMode AndAlso IsCurrentFileEligibleImage() Then TranslateCurrentImage(True)
    End Sub

    Private Sub ToggleOverlayVisibility()
        SetOcrOverlayVisibility(Not ocr_Overlay_Visible)
    End Sub

    ''' <summary>Changes overlay visibility from the viewer or the Settings window.</summary>
    Friend Sub SetOcrOverlayVisibility(visible As Boolean)
        If ocr_Settings Is Nothing Then Return
        ocr_Overlay_Visible = visible
        ocr_Settings.OverlayVisible = visible
        InvalidateOverlay()
        SetOcrStatus(Localization.T(If(ocr_Overlay_Visible, "Наложение показано", "Наложение скрыто")))
        UpdateOcrButtonVisual()
    End Sub

    Private Sub btn_Translate_Click(sender As Object, e As EventArgs) Handles btn_Translate.Click
        If ocr_Settings Is Nothing Then Return
        ocr_Settings.Enabled = True
        UpdateOcrButtonVisual()
        TriggerOcrHotkey()
    End Sub

    Private Sub btn_Translate_MouseUp(sender As Object, e As MouseEventArgs) Handles btn_Translate.MouseUp
        ' Right-click opens OCR settings; when the browser companion isn't installed
        ' it instead shows a small menu offering to install it (spec §7).
        If e.Button = MouseButtons.Right Then HandleTranslateRightClick(TryCast(sender, Control))
    End Sub

    ' --- pipeline -------------------------------------------------------------

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' Language data is fetched only because the user said yes (APP-BEHAVIOUR rule 4,
    ''' ticket 028 Ф-6). It used to be downloaded from INSIDE recognition, silently - and with
    ''' auto-OCR on, that meant on an image flip. Now:
    '''   * a manual run asks, the same way the runtime download does, and downloads behind
    '''     the dialog with progress and Cancel;
    '''   * auto-OCR never downloads: it uses whatever language data exists, and when none of
    '''     the languages has any it skips the image and says why and how to fix it.
    ''' False = do not recognize this image.
    ''' </summary>
    Private Async Function EnsureOcrLanguageDataAsync(isAuto As Boolean) As Task(Of Boolean)
        Dim preferBest As Boolean = String.Equals(ocr_Settings.OcrModelQuality, "best", StringComparison.OrdinalIgnoreCase)
        Dim codes As List(Of String) = TesseractOcrEngine.RecognitionLanguageCodes(ocr_Settings.OcrSourceHint())
        Dim wanted As List(Of String) = TesseractOcrEngine.MissingLanguageCodes(codes, preferBest)
        If wanted.Count = 0 Then Return True

        ' Something usable already on disk - the engine skips the languages with no data.
        Dim usableExists As Boolean = TesseractOcrEngine.MissingLanguageCodes(codes, False).Count < codes.Count

        If isAuto Then
            If usableExists Then Return True
            SetOcrStatus(Localization.TF("Нет языкового пакета OCR ({0}) - автоперевод пропустил изображение. Нажмите T, чтобы скачать его.",
                                         OcrLanguageCatalog.DescribeTesseractCodes(wanted)))
            Return False
        End If

        Dim question As String = Localization.TF("Для распознавания нужны языковые пакеты OCR: {0}. Скачать их сейчас?",
                                                 OcrLanguageCatalog.DescribeTesseractCodes(wanted))
        If MessageBox.Show(Me, question, "Fast Media Sorter", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
            Dim toFetch As List(Of String) = wanted
            If Await OptionalRuntimeManager.DownloadWithDialogAsync(
                    Me, Localization.T("Языковые пакеты OCR"),
                    Function(p, t) TesseractOcrEngine.DownloadLanguagesAsync(toFetch, preferBest, p, t)).ConfigureAwait(True) Then
                Return True
            End If
        End If

        If usableExists Then Return True
        SetOcrStatus(LanguagePackMissingStatus(wanted))
        Return False
    End Function
#End If

    ''' <summary>
    ''' The status for "no language data": which language, and where to get it
    ''' (`OCR-OVERLAY` rule 10 asks for the fix to be named; ticket 033 Ф-6, finding F7). It
    ''' used to read "OCR engine unavailable" - on the x86 viewer always, on the mainline for
    ''' a pack missing at recognition time - which sent people after the engine instead of
    ''' the language.
    ''' </summary>
    Private Shared Function LanguagePackMissingStatus(codes As IEnumerable(Of String)) As String
        Return Localization.TF("Нет языкового пакета OCR ({0}) - скачайте его: Настройки -> OCR и перевод -> Распознавание (OCR)",
                               OcrLanguageCatalog.DescribeTesseractCodes(codes))
    End Function

    Friend Async Sub TranslateCurrentImage(isAuto As Boolean)
        If ocr_Settings Is Nothing Then Return
        If Not ocr_Settings.Enabled Then
            ocr_Settings.Enabled = True
            UpdateOcrButtonVisual()
        End If

        Dim runtimeReason As String = ""
        If Not OptionalRuntimeManager.TryPrepareOcrRuntime(runtimeReason) Then
            If isAuto Then
                SetOcrStatus(OptionalRuntimeManager.GetOcrUnavailableStatusText())
                Return
            End If

            If Not Await OptionalRuntimeManager.EnsureOcrRuntimeInteractiveAsync(Me) Then
                SetOcrStatus(OptionalRuntimeManager.GetOcrUnavailableStatusText())
                Return
            End If
        End If

        If Not IsCurrentFileEligibleImage() Then
            If Not isAuto Then SetOcrStatus(Localization.T("Это не изображение"))
            Return
        End If

#If Not NETFRAMEWORK Then
        ' Sealed content is never recognised automatically, and its text goes to a
        ' translator off this machine only after the user said so (audit 052 G-13).
        If isAuto AndAlso Not SecureAllowsAutomaticOcr() Then Return
        If Not isAuto AndAlso Not SecureConfirmsTranslatorEndpoint(ocr_Settings.Endpoint) Then Return

        If Not Await EnsureOcrLanguageDataAsync(isAuto) Then Return
#End If

        Dim pb As PictureBox = GetActivePictureBox()
        If pb Is Nothing OrElse pb.Image Is Nothing Then Return

        ' Snapshot the bitmap on the UI thread so navigation can dispose the
        ' original freely while OCR runs on its private copy.
        Dim snapshot As Bitmap
        Try
            snapshot = New Bitmap(pb.Image)
        Catch ex As Exception
            ' Said, not swallowed: on the 32-bit viewer a large photo can run out of memory
            ' right here, and T then looked like it did nothing at all (audit 051 E-08).
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: snapshot failed: " & ex.Message)
            SetOcrStatus(Localization.T("Не удалось подготовить изображение для OCR"))
            Return
        End Try

        Dim path As String = Current_File_Name
        Dim ticks As Long = 0
        Try
            ticks = File.GetLastWriteTimeUtc(path).Ticks
        Catch
        End Try

        Dim job As New OcrJob With {
            .FilePath = path,
            .FileWriteTicks = ticks,
            .Bitmap = snapshot,
            .ImageSize = New Size(snapshot.Width, snapshot.Height),
            .QuarterTurns = ocr_Quarter_Turns,
            .ExifAutoRotate = Is_Exif_AutoRotate,
            .OcrSourceHint = ocr_Settings.OcrSourceHint(),
            .SourceLang = ocr_Settings.SourceLang,
            .TargetLang = ocr_Settings.TargetLang
        }
#If Not NETFRAMEWORK Then
        ' An archive entry's path carries the session's random token, so a result filed
        ' under it can never be found again - it would only evict entries that can
        ' (audit 052 G-21). Memory still serves the session.
        job.IsSealedContent = Not SecureAllowsDiskCache()
        job.AllowDiskCache = Not job.IsSealedContent AndAlso Not ArchivePaths.IsUnderArchiveCache(path)
#End If

        CancelOcrJob()
        ocr_Job_Cts = New CancellationTokenSource()
        SetOcrStatus(Localization.T("Распознавание.."))

        ' Fire-and-forget; RunOcrPipeline never throws (it catches internally).
        Forget(RunOcrPipeline(job, ocr_Job_Cts.Token))
    End Sub

    Private Async Function RunOcrPipeline(job As OcrJob, token As CancellationToken) As Task
        Try
            ' Engine/translator identity in the cache key includes the model,
            ' OCR quality and OCR mode, so changing any of them invalidates results.
            ' OcrPipelineVersion covers everything downstream of the engine (clustering,
            ' filter, plate colours) - without it a cached document built by the previous
            ' algorithm would come back and the change would look like it never applied.
            Dim engineName As String = ocr_Engine.Name & "|" & ocr_Settings.OcrModelQuality & "|" & ocr_Settings.OcrPageMode &
                                       "|v" & OcrPipelineVersion.ToString(Globalization.CultureInfo.InvariantCulture)
            Dim provider As String = ocr_Settings.Provider & "|" & ocr_Settings.OllamaModel
            Dim key As String = OcrResultPolicy.BuildCacheKey(job.FilePath, job.FileWriteTicks,
                                                              job.ImageSize.Width, job.ImageSize.Height,
                                                              job.QuarterTurns, job.ExifAutoRotate,
                                                              engineName, provider, job.SourceLang, job.TargetLang)

            ' Cache (memory, then disk).
            Dim diskCacheAllowed As Boolean = ocr_Settings.DiskCache
#If Not NETFRAMEWORK Then
            diskCacheAllowed = diskCacheAllowed AndAlso job.AllowDiskCache
#End If

            Dim cached As OcrOverlayDocument = ocr_Cache.TryGetMemory(key)
            Dim fromDisk As Boolean = False
            If cached Is Nothing AndAlso diskCacheAllowed Then
                cached = ocr_Cache.TryLoadFromDisk(key)
                fromDisk = cached IsNot Nothing
            End If
            ' Boxes in another grid than the one on screen are not this picture's plates,
            ' whatever the key says (audit 051 E-01, fix c): run afresh instead.
            If cached IsNot Nothing AndAlso Not OcrResultPolicy.DocumentFitsGrid(cached.ImageSize.Width, cached.ImageSize.Height,
                                                                                  job.ImageSize.Width, job.ImageSize.Height) Then
                cached = Nothing
            End If
            If cached IsNot Nothing AndAlso fromDisk Then ocr_Cache.PutMemory(key, cached)
            If cached IsNot Nothing Then
#If Not NETFRAMEWORK Then
                ' A document written by the x86 viewer - or by any build older than the plate
                ' colours - lands under the SAME key and arrives here with no colours at all.
                ' The snapshot is still alive on this branch, so it is coloured in on the spot;
                ' without that, such a file would keep its white plate forever. Not written
                ' back: sampling 6000 pixels again is cheaper than a write path inside a read.
                ApplyPlateColors(job.Bitmap, cached.Blocks)
#End If
                ApplyOnUi(cached, Localization.T("Из кэша"), token)
                Return
            End If

            token.ThrowIfCancellationRequested()

            ' OCR off the UI thread.
            ' The token now goes INSIDE the engine too, not just to Task.Run: the latter only
            ' stops a job that has not started yet.
            Dim ocrResult As OcrResult = Await Task.Run(Function() ocr_Engine.Recognize(job.Bitmap, job.OcrSourceHint, token), token).ConfigureAwait(False)
            token.ThrowIfCancellationRequested()

            Select Case ocrResult.Status
                Case OcrStatus.RuntimeMissing
                    If ocrResult.Message = TesseractOcrEngine.LanguageDataMissing Then
                        ' Name the language that is missing, and where the download is.
                        Dim wanted As List(Of String) = TesseractOcrEngine.MissingLanguageCodes(
                            TesseractOcrEngine.RecognitionLanguageCodes(job.OcrSourceHint), False)
                        If wanted.Count = 0 Then wanted = TesseractOcrEngine.RecognitionLanguageCodes(job.OcrSourceHint)
                        SetStatusOnUi(LanguagePackMissingStatus(wanted))
                    Else
                        SetStatusOnUi(Localization.T("OCR-движок недоступен"))
                    End If
                    Return
                Case OcrStatus.Failed
                    If ocrResult.Message = TesseractOcrEngine.ImagePreparationFailed Then
                        SetStatusOnUi(Localization.T("Не удалось подготовить изображение для OCR"))
                    Else
                        SetStatusOnUi(Localization.T("Ошибка OCR"))
                    End If
                    Return
            End Select

            ' What the two thresholds refused on this run: the engine's line rule filled its
            ' half inside Recognize, the block filter fills the rest below. Collected in both
            ' builds, used only by the modern one's diagnostics - a handful of small objects
            ' per image is not worth a compile seam, and one code path here cannot disagree
            ' with itself the way two would.
            Dim dropped As New List(Of OcrDroppedLine)(ocrResult.Dropped)
            ' preserveOrder: only when S7 actually cut a stitched line on this page, in which
            ' case the lines already arrive column by column and the builder's top-then-left
            ' sort would interleave them straight back.
            Dim blocks As List(Of OcrBlock) = OcrBlockBuilder.BuildBlocks(
                ocrResult.Lines, job.ImageSize, dropped, ocrResult.LinesRegrouped)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") &
                            " ocr: blocks=" & blocks.Count.ToString() &
                            " dropped=" & dropped.Count.ToString() &
                            " srcUseful=" & String.Join(",", blocks.Select(Function(b) CountUsefulDebugChars(b.SourceText)).ToArray()))
            ' OCR-OVERLAY rule 10, and it runs BEFORE the empty check because the failure it
            ' catches is not an empty page: a ladder that cannot read the script still returns
            ' text, and the filter then refuses nearly all of it. Handing that to the user as
            ' "no text found" hides the one fact that would let them fix it. Never cached -
            ' picking the language is exactly the thing that makes the next run different.
            If OcrLanguageGuard.ShouldRefuse(String.Equals(job.OcrSourceHint, "auto", StringComparison.Ordinal),
                                             blocks, dropped) Then
#If Not NETFRAMEWORK Then
                ' Nothing is painted, so nothing would dump - and a refused run is precisely
                ' the scene the record exists for.
                OcrDiagnostics.DumpDocument(BuildDoc(job, engineName, provider, New List(Of OcrBlock)(), dropped), mayPersist:=Not job.IsSealedContent)
#End If
                SetStatusOnUi(Localization.T("Язык оригинала не определён - выберите его в настройках OCR"))
                Return
            End If

            If blocks.Count = 0 Then
#If Not NETFRAMEWORK Then
                ' Nothing will be painted, so nothing would dump - and this is precisely the
                ' scene the record exists for: "the engine found nothing" and "a threshold
                ' threw away what it found" are the same picture on screen and different
                ' facts. Dumped from here, once, instead of from a paint that never happens.
                OcrDiagnostics.DumpDocument(BuildDoc(job, engineName, provider, blocks, dropped), mayPersist:=Not job.IsSealedContent)
#End If
                SetStatusOnUi(Localization.T("Текст не найден"))
                Return
            End If

#If Not NETFRAMEWORK Then
            ' Sample the plate colours here, on the background thread, while the pipeline's
            ' private snapshot still exists - it is disposed in the Finally below, and by
            ' paint time the original pixels are gone.
            ApplyPlateColors(job.Bitmap, blocks)
#End If

            ' Translate.
            SetStatusOnUi(Localization.T("Перевод.."))
            Dim translator As ITranslator = CreateTranslator()
            Dim available As Boolean = False
            If translator IsNot Nothing Then
                available = Await translator.ProbeAsync(token).ConfigureAwait(False)
            End If
            token.ThrowIfCancellationRequested()

            If Not available Then
                ' Show the recognized text untranslated; don't cache (allow retry).
                ShowUntranslated(job, engineName, provider, blocks, dropped, Localization.T("Переводчик недоступен"), token)
                Return
            End If

            Dim sources As List(Of String) = blocks.Select(Function(b) b.SourceText).ToList()
            Dim batch As TranslationBatch
            Try
                batch = Await translator.TranslateAsync(sources, job.SourceLang, job.TargetLang, token).ConfigureAwait(False)
            Catch ex As OperationCanceledException When Not token.IsCancellationRequested
                ' Backstop for a translator that lets its HTTP timeout escape: that is not
                ' navigation, and the recognized text is still worth showing (audit 051 E-04).
                batch = TranslationBatch.AllFailed(sources, TranslationFailure.Timeout)
            End Try
            token.ThrowIfCancellationRequested()
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") &
                            " ocr: translations=" & batch.Texts.Count.ToString() &
                            " failed=" & batch.FailedCount.ToString() &
                            " dstUseful=" & String.Join(",", batch.Texts.Select(Function(t) CountUsefulDebugChars(t)).ToArray()))

            For i As Integer = 0 To blocks.Count - 1
                Dim ok As Boolean = i < batch.Texts.Count AndAlso batch.Translated(i) AndAlso Not String.IsNullOrWhiteSpace(batch.Texts(i))
                blocks(i).TranslatedText = If(ok, batch.Texts(i), blocks(i).SourceText)
            Next

            ' The model that really answered, not only the one configured: Ollama falls back
            ' to another installed model when the configured one is missing.
            Dim answeredBy As String = If(batch.ModelUsed.Length > 0, ocr_Settings.Provider & "|" & batch.ModelUsed, provider)
            Dim outcome As OcrResultPolicy.TranslationOutcome = OcrResultPolicy.Classify(batch)
            If Not OcrResultPolicy.IsCacheable(outcome) Then
                ' Shown, never cached, and T runs it again (audit 051 E-02): a failure used to
                ' be written to both tiers as a translation and come back "from cache" for ever.
                ShowUntranslated(job, engineName, answeredBy, blocks, dropped, TranslationFailureStatus(batch, job.TargetLang), token)
                Return
            End If

            Dim finalDoc As OcrOverlayDocument = BuildDoc(job, engineName, answeredBy, blocks, dropped)
            ocr_Cache.PutMemory(key, finalDoc)
            If diskCacheAllowed Then ocr_Cache.SaveToDisk(key, finalDoc)
            ApplyOnUi(finalDoc, "", token)

        Catch ex As OperationCanceledException When token.IsCancellationRequested
            ' Navigated away (or rotated, or closed) - discard silently.
        Catch ex As OperationCanceledException
            ' Cancelled by something other than our token - a timeout. Never silent (E-04).
            SetStatusOnUi(Localization.T("Переводчик не ответил вовремя - T повторит"))
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: pipeline error: " & ex.Message)
            SetStatusOnUi(Localization.T("Ошибка OCR"))
        Finally
            Try
                job.Bitmap.Dispose()
            Catch
            End Try
        End Try
    End Function

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' Fills in every block that has no plate colours yet, sampling the image once for all
    ''' of them: one LockBits, one ARGB copy, then a pure computation per block. Doing it per
    ''' block would lock and unlock the bitmap dozens of times for the same picture.
    '''
    ''' Failure is silent by design - a plate keeps its old constant colour, which is exactly
    ''' the pre-S4 appearance. Colour sampling must never be able to cost the user the
    ''' translation itself.
    ''' </summary>
    Private Shared Sub ApplyPlateColors(source As Bitmap, blocks As List(Of OcrBlock))
        If source Is Nothing OrElse blocks Is Nothing OrElse blocks.Count = 0 Then Return
        If Not blocks.Any(Function(b) b.PlateBackgroundArgb = 0) Then Return

        Try
            Dim w As Integer = source.Width
            Dim h As Integer = source.Height
            If w <= 0 OrElse h <= 0 Then Return

            Dim pixels(w * h - 1) As Integer
            Dim data As Imaging.BitmapData = source.LockBits(New Rectangle(0, 0, w, h),
                                                            Imaging.ImageLockMode.ReadOnly,
                                                            Imaging.PixelFormat.Format32bppArgb)
            Try
                ' A negative or short stride means a layout this row-wise copy would misread;
                ' skip rather than sample garbage.
                If data.Stride < w * 4 Then Return
                For y As Integer = 0 To h - 1
                    Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * w, w)
                Next
            Finally
                source.UnlockBits(data)
            End Try

            For Each b As OcrBlock In blocks
                If b.PlateBackgroundArgb <> 0 Then Continue For
                Dim palette As OcrPlateColors.PlatePalette =
                    OcrPlateColors.Compute(pixels, w, h, b.Box, OcrOverlayFit.MedianLineHeight(b))
                b.PlateBackgroundArgb = palette.Background
                b.PlateInkArgb = palette.Ink
            Next
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: plate colours failed: " & ex.Message)
        End Try
    End Sub
#End If

    Private Function CreateTranslator() As ITranslator
        Select Case If(ocr_Settings.Provider, "").Trim().ToLowerInvariant()
            Case "libretranslate", "libre"
                Return New LibreTranslateTranslator(ocr_Settings.Endpoint, ocr_Settings.ApiKey)
            Case Else
                Return New OllamaTranslator(ocr_Settings.Endpoint, ocr_Settings.OllamaModel)
        End Select
    End Function

    ''' <param name="dropped">Refusals measured on THIS run, or Nothing when they were not
    ''' measured. A document that came back from the disk cache passes Nothing and says so in
    ''' the dump; one served from memory keeps the refusals of the run that built it, because
    ''' those are genuinely its own.</param>
    Private Function BuildDoc(job As OcrJob, engineName As String, provider As String, blocks As List(Of OcrBlock),
                              dropped As List(Of OcrDroppedLine)) As OcrOverlayDocument
        Return New OcrOverlayDocument With {
            .FilePath = job.FilePath,
            .FileWriteTicks = job.FileWriteTicks,
            .ImageSize = job.ImageSize,
            .SourceLanguage = job.SourceLang,
            .TargetLanguage = job.TargetLang,
            .Engine = engineName,
            .Translator = provider,
            .Blocks = blocks,
            .Dropped = dropped
        }
    End Function

    ''' <summary>
    ''' Shows the blocks with whatever translation they got - the source text where none -
    ''' as a PARTIAL document: never cached, and T runs the pipeline again (audit 051 E-02).
    ''' </summary>
    Private Sub ShowUntranslated(job As OcrJob, engineName As String, provider As String, blocks As List(Of OcrBlock),
                                 dropped As List(Of OcrDroppedLine), statusText As String, token As CancellationToken)
        For Each b As OcrBlock In blocks
            If String.IsNullOrWhiteSpace(b.TranslatedText) Then b.TranslatedText = b.SourceText
        Next
        Dim doc As OcrOverlayDocument = BuildDoc(job, engineName, provider, blocks, dropped)
        doc.IsPartial = True
        ApplyOnUi(doc, statusText, token)
    End Sub

    ''' <summary>The status line for a translation that is not complete - it names the
    ''' reason when there is one, and always that T will try again.</summary>
    Private Shared Function TranslationFailureStatus(batch As TranslationBatch, targetLang As String) As String
        Select Case batch.Failure
            Case TranslationFailure.Timeout
                Return Localization.T("Переводчик не ответил вовремя - T повторит")
            Case TranslationFailure.UnsupportedLanguage
                Return Localization.TF("Переводчик не поддерживает язык {0} - выберите другой в настройках OCR",
                                       OcrLanguageCatalog.NameFor(targetLang))
        End Select
        Dim total As Integer = batch.Translated.Count
        Dim failed As Integer = batch.FailedCount
        If total = 0 OrElse failed >= total Then Return Localization.T("Перевод не удался - T повторит")
        Return Localization.TF("Не переведено фрагментов: {0} из {1} - T повторит",
                               failed.ToString(Globalization.CultureInfo.CurrentCulture),
                               total.ToString(Globalization.CultureInfo.CurrentCulture))
    End Function

    Private Sub ApplyOnUi(doc As OcrOverlayDocument, statusText As String, token As CancellationToken)
        If Me.IsDisposed Then Return
        Try
            Me.BeginInvoke(Sub() ApplyOverlayResult(doc, statusText, token))
        Catch
        End Try
    End Sub

    Private Sub ApplyOverlayResult(doc As OcrOverlayDocument, statusText As String, token As CancellationToken)
        If Me.IsDisposed Then Return
        If token.IsCancellationRequested Then Return
        ' Step 9 of the pipeline: only apply if the same file is still shown.
        If Not String.Equals(doc.FilePath, Current_File_Name, StringComparison.Ordinal) Then Return
        ' And only onto the grid it was measured in: a turn since the job started cancels
        ' it, but a result already queued on the UI thread is caught here (audit 051 E-01).
        Dim pb As PictureBox = GetActivePictureBox()
        If pb Is Nothing OrElse pb.Image Is Nothing OrElse
           Not OcrResultPolicy.DocumentFitsGrid(doc.ImageSize.Width, doc.ImageSize.Height, pb.Image.Width, pb.Image.Height) Then Return

        current_Overlay_Document = doc
        ocr_Overlay_Visible = True
        ocr_Settings.OverlayVisible = True
        lbl_Status.Text = statusText
        InvalidateOverlay()
        UpdateOcrButtonVisual()
    End Sub

    Private Sub CancelOcrJob()
        If ocr_Job_Cts IsNot Nothing Then
            Try
                ocr_Job_Cts.Cancel()
            Catch
            End Try
            Try
                ocr_Job_Cts.Dispose()
            Catch
            End Try
            ocr_Job_Cts = Nothing
        End If
    End Sub

    ' --- helpers --------------------------------------------------------------

    Private Function IsCurrentFileEligibleImage() As Boolean
        If String.IsNullOrEmpty(Current_File_Name) Then Return False
        Dim ext As String = Path.GetExtension(Current_File_Name).ToLowerInvariant()
#If Not NETFRAMEWORK Then
        ' A picture out of a secure container is a picture: its own name ends in .fd-sec
        ' and says nothing about what is on screen, so the extension test would refuse the
        ' one thing §0 promises works exactly as usual. The overlay it produces is kept in
        ' memory only - the disk half of the cache is off for containers (§5.4 rule 2).
        If IsSecureImageOnScreen() Then Return is_PictureBox1_Visible OrElse is_PictureBox2_Visible
#End If
        If Not Image_File_Extensions.Contains(ext) Then Return False

        ' No File.Exists here. OnMediaDisplayed calls this after EVERY flip, and on a share
        ' that stopped answering a single Exists costs the whole network timeout on the UI
        ' thread - a per-file stall for a question already answered: a picture box is only
        ' visible because the file was just read successfully.
        Return is_PictureBox1_Visible OrElse is_PictureBox2_Visible
    End Function

    Private Function GetActivePictureBox() As PictureBox
        If is_PictureBox1_Visible Then Return Picture_Box_1
        If is_PictureBox2_Visible Then Return Picture_Box_2
        Return Nothing
    End Function

    Private Function CurrentDocBelongsToCurrentFile() As Boolean
        Return current_Overlay_Document IsNot Nothing AndAlso
               String.Equals(current_Overlay_Document.FilePath, Current_File_Name, StringComparison.Ordinal)
    End Function

    Private Sub InvalidateOverlay()
        If Picture_Box_1 IsNot Nothing AndAlso Picture_Box_1.Visible Then Picture_Box_1.Invalidate()
        If Picture_Box_2 IsNot Nothing AndAlso Picture_Box_2.Visible Then Picture_Box_2.Invalidate()
    End Sub

    Private Sub SetOcrStatus(text As String)
        If Me.IsDisposed Then Return
        lbl_Status.Text = text
    End Sub

    Private Sub SetStatusOnUi(text As String)
        If Me.IsDisposed Then Return
        Try
            Me.BeginInvoke(Sub()
                               If Not Me.IsDisposed Then lbl_Status.Text = text
                           End Sub)
        Catch
        End Try
    End Sub

    ''' <summary>The two weights the Translate button ever wears, built once. This runs on
    ''' every navigation (OnMediaDisplayed), and a New Font per call abandoned the previous
    ''' one - a GDI font handle each time, thousands per sorting session. Rebuilt lazily
    ''' whenever the base font changes (a language switch can change the family).</summary>
    Private ocr_Button_Font_Bold As Font
    Private ocr_Button_Font_Regular As Font
    Private ocr_Button_Font_Source As Font

    Private Sub UpdateOcrButtonVisual()
        If btn_Translate Is Nothing OrElse ocr_Settings Is Nothing Then Return

        Dim caption As String = Localization.T("Перевод")
#If NETFRAMEWORK Then
        If ocr_Settings.AutoMode Then caption &= " ⟳"
#Else
        ' A word, not "⟳": that arrow is action.refresh, which the folder button on the
        ' same toolbar already draws (ICON-SET rule 1, spec 027 question 3).
        If ocr_Settings.AutoMode Then caption &= " " & Localization.T("(авто)")
#End If

        Dim wanted_Font As Font = OcrButtonFont(ocr_Settings.Enabled)
        Dim changed As Boolean = (btn_Translate.Text <> caption) OrElse (btn_Translate.Font IsNot wanted_Font)

        btn_Translate.Text = caption
        btn_Translate.Font = wanted_Font
        ' The caption/weight change resizes the button - re-flow the toolbar. Nothing
        ' changed means nothing to re-flow, and this is a per-navigation path.
        If changed Then LayoutToolbar()
    End Sub

    Private Function OcrButtonFont(bold As Boolean) As Font
        ' Baseline = whatever font the button carries, EXCEPT one of our own two: that is
        ' how a language switch (script fonts follow the UI language) gets picked up while
        ' our own assignment does not re-trigger a rebuild.
        Dim baseline As Font = btn_Translate.Font
        If baseline Is ocr_Button_Font_Bold OrElse baseline Is ocr_Button_Font_Regular Then
            baseline = ocr_Button_Font_Source
        End If

        If baseline IsNot Nothing AndAlso baseline IsNot ocr_Button_Font_Source Then
            If ocr_Button_Font_Bold IsNot Nothing Then ocr_Button_Font_Bold.Dispose()
            If ocr_Button_Font_Regular IsNot Nothing Then ocr_Button_Font_Regular.Dispose()
            ocr_Button_Font_Source = baseline
            ocr_Button_Font_Bold = New Font(baseline, FontStyle.Bold)
            ocr_Button_Font_Regular = New Font(baseline, FontStyle.Regular)
        End If

        Return If(bold, ocr_Button_Font_Bold, ocr_Button_Font_Regular)
    End Function

    Private Shared Function CountUsefulDebugChars(text As String) As Integer
        Dim count As Integer = 0
        For Each ch As Char In If(text, "")
            If Char.IsLetterOrDigit(ch) Then count += 1
        Next
        Return count
    End Function

    ' --- settings window ------------------------------------------------------

    ''' <summary>Ensures the OCR/translate settings object exists (loaded from the
    ''' registry). The Settings window (Table_Form) edits this same live object via
    ''' its "OCR и перевод" tab.</summary>
    Friend Function EnsureOcrSettings() As OcrTranslateSettings
        If ocr_Settings Is Nothing Then
            ocr_Settings = New OcrTranslateSettings()
            ocr_Settings.Load(Localization.CurrentCode)
        End If
        Return ocr_Settings
    End Function

    ''' <summary>Drops every cached OCR/translation result and clears the current
    ''' overlay. Called when the OCR tab of the Settings window is opened so a
    ''' parameter change can't reuse a stale result.</summary>
    Friend Sub ClearOcrResultCache()
        If ocr_Cache IsNot Nothing Then ocr_Cache.Clear()
        CancelOcrJob()
        current_Overlay_Document = Nothing
        InvalidateOverlay()
    End Sub

    ''' <summary>Applies a live edit made on the Settings window's OCR tab: pushes
    ''' OCR model/page-mode onto the engine (when <paramref name="rebuildEngine"/>),
    ''' refreshes the toolbar button and overlay, and optionally re-runs auto-mode.</summary>
    Friend Sub OnOcrSettingsEditedFromSettingsWindow(rebuildEngine As Boolean, maybeAutoRun As Boolean)
        If ocr_Settings Is Nothing Then Return
        If rebuildEngine Then ApplyEngineOptions()
        UpdateOcrButtonVisual()
        InvalidateOverlay()
        If maybeAutoRun AndAlso ocr_Settings.Enabled AndAlso ocr_Settings.AutoMode AndAlso IsCurrentFileEligibleImage() Then
            TranslateCurrentImage(True)
        End If
    End Sub

    ''' <summary>Opens the unified Settings window on its "OCR и перевод" tab.
    ''' Public-ish (Friend) so the toolbar Translate button's right-click can call it.</summary>
    Friend Sub ShowOcrTranslateSettings()
        EnsureOcrSettings()
        EnsureSettingsWindow()
        Table_Form.PrepareForDisplay()
        Table_Form.SelectOcrTab()
        PinToViewerBand(Table_Form)
        Table_Form.Show(Me)
        Table_Form.Activate()
    End Sub

End Class
