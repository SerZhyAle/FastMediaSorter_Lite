Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' OCR + Translation overlay — pipeline, toolbar control, hotkey entry points and
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

    Private WithEvents btn_Translate As Button
    Private WithEvents ocr_Auto_Timer As New System.Windows.Forms.Timer()

    Private NotInheritable Class OcrJob
        Public FilePath As String
        Public FileWriteTicks As Long
        Public Bitmap As Bitmap
        Public ImageSize As Size
        Public OcrLanguages As String
        Public SourceLang As String
        Public TargetLang As String
    End Class

    ' --- setup / teardown -----------------------------------------------------

    ''' <summary>Creates the toolbar "Translate" button. Called from
    ''' BuildModernLayout BEFORE styling so it inherits the uniform chrome.</summary>
    Friend Sub BuildOcrToolbarControls(host As FlowLayoutPanel)
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
        ocr_Settings.Load(If(Is_Russian_Language, "ru", "en"))
        ocr_Overlay_Visible = ocr_Settings.OverlayVisible
        ocr_Engine = New TesseractOcrEngine()
        ocr_Cache = New TranslationCache()
        ocr_Auto_Timer.Interval = 350
        ocr_Auto_Timer.Stop()
        UpdateOcrButtonVisual()
        If toolTip IsNot Nothing AndAlso btn_Translate IsNot Nothing Then
            toolTip.SetToolTip(btn_Translate, If(Is_Russian_Language,
                "OCR + перевод текущего изображения (T). ПКМ — настройки. Shift+T — авто-режим.",
                "OCR + translate the current image (T). Right-click for settings. Shift+T = auto mode."))
        End If
    End Sub

    Friend Sub SaveOcrSettings()
        If ocr_Settings IsNot Nothing Then ocr_Settings.Save()
    End Sub

    Friend Sub ShutdownOcrTranslate()
        CancelOcrJob()
        Try
            ocr_Auto_Timer.Stop()
        Catch
        End Try
        Dim te As TesseractOcrEngine = TryCast(ocr_Engine, TesseractOcrEngine)
        If te IsNot Nothing Then te.DisposeEngine()
    End Sub

    ' --- navigation hook ------------------------------------------------------

    ''' <summary>Called whenever a new media file finishes displaying.</summary>
    Friend Sub OnMediaDisplayed()
        If ocr_Settings Is Nothing Then Return

        ' The overlay belonged to the previous image: cancel + clear.
        CancelOcrJob()
        current_Overlay_Document = Nothing
        InvalidateOverlay()
        UpdateOcrButtonVisual()

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
        If CurrentDocBelongsToCurrentFile() Then
            ToggleOverlayVisibility()
        Else
            TranslateCurrentImage(False)
        End If
    End Sub

    ''' <summary>Shift+T: toggle auto mode.</summary>
    Friend Sub ToggleOcrAutoMode()
        If ocr_Settings Is Nothing Then Return
        ocr_Settings.Enabled = True
        ocr_Settings.AutoMode = Not ocr_Settings.AutoMode
        SetOcrStatus(If(Is_Russian_Language,
            If(ocr_Settings.AutoMode, "Авто-перевод включён", "Авто-перевод выключен"),
            If(ocr_Settings.AutoMode, "Auto-translate on", "Auto-translate off")))
        UpdateOcrButtonVisual()
        If ocr_Settings.AutoMode AndAlso IsCurrentFileEligibleImage() Then TranslateCurrentImage(True)
    End Sub

    Private Sub ToggleOverlayVisibility()
        ocr_Overlay_Visible = Not ocr_Overlay_Visible
        ocr_Settings.OverlayVisible = ocr_Overlay_Visible
        InvalidateOverlay()
        SetOcrStatus(If(Is_Russian_Language,
            If(ocr_Overlay_Visible, "Наложение показано", "Наложение скрыто"),
            If(ocr_Overlay_Visible, "Overlay shown", "Overlay hidden")))
        UpdateOcrButtonVisual()
    End Sub

    Private Sub btn_Translate_Click(sender As Object, e As EventArgs) Handles btn_Translate.Click
        If ocr_Settings Is Nothing Then Return
        ocr_Settings.Enabled = True
        UpdateOcrButtonVisual()
        TriggerOcrHotkey()
    End Sub

    Private Sub btn_Translate_MouseUp(sender As Object, e As MouseEventArgs) Handles btn_Translate.MouseUp
        If e.Button = MouseButtons.Right Then ShowOcrTranslateSettings()
    End Sub

    ' --- pipeline -------------------------------------------------------------

    Friend Sub TranslateCurrentImage(isAuto As Boolean)
        If ocr_Settings Is Nothing Then Return
        If Not ocr_Settings.Enabled Then
            ocr_Settings.Enabled = True
            UpdateOcrButtonVisual()
        End If

        If Not IsCurrentFileEligibleImage() Then
            If Not isAuto Then SetOcrStatus(If(Is_Russian_Language, "Это не изображение", "Not an image"))
            Return
        End If

        Dim pb As PictureBox = GetActivePictureBox()
        If pb Is Nothing OrElse pb.Image Is Nothing Then Return

        ' Snapshot the bitmap on the UI thread so navigation can dispose the
        ' original freely while OCR runs on its private copy.
        Dim snapshot As Bitmap
        Try
            snapshot = New Bitmap(pb.Image)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: snapshot failed: " & ex.Message)
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
            .OcrLanguages = ocr_Settings.OcrLanguages(),
            .SourceLang = ocr_Settings.SourceLang,
            .TargetLang = ocr_Settings.TargetLang
        }

        CancelOcrJob()
        ocr_Job_Cts = New CancellationTokenSource()
        SetOcrStatus(If(Is_Russian_Language, "Распознавание…", "OCR…"))

        ' Fire-and-forget; RunOcrPipeline never throws (it catches internally).
        RunOcrPipeline(job, ocr_Job_Cts.Token)
    End Sub

    Private Async Function RunOcrPipeline(job As OcrJob, token As CancellationToken) As Task
        Try
            Dim provider As String = ocr_Settings.Provider
            Dim engineName As String = ocr_Engine.Name
            Dim key As String = TranslationCache.BuildKey(job.FilePath, job.FileWriteTicks, engineName, provider, job.SourceLang, job.TargetLang)

            ' Cache (memory, then disk).
            Dim cached As OcrOverlayDocument = ocr_Cache.TryGetMemory(key)
            If cached Is Nothing AndAlso ocr_Settings.DiskCache Then
                cached = ocr_Cache.TryLoadFromDisk(key)
                If cached IsNot Nothing Then ocr_Cache.PutMemory(key, cached)
            End If
            If cached IsNot Nothing Then
                ApplyOnUi(cached, If(Is_Russian_Language, "Из кэша", "Result loaded from cache"), token)
                Return
            End If

            token.ThrowIfCancellationRequested()

            ' OCR off the UI thread.
            Dim ocrResult As OcrResult = Await Task.Run(Function() ocr_Engine.Recognize(job.Bitmap, job.OcrLanguages), token).ConfigureAwait(False)
            token.ThrowIfCancellationRequested()

            Select Case ocrResult.Status
                Case OcrStatus.RuntimeMissing
                    SetStatusOnUi(If(Is_Russian_Language, "OCR-движок недоступен", "OCR runtime missing"))
                    Return
                Case OcrStatus.Failed
                    SetStatusOnUi(If(Is_Russian_Language, "Ошибка OCR", "OCR error"))
                    Return
            End Select

            Dim blocks As List(Of OcrBlock) = OcrBlockBuilder.BuildBlocks(ocrResult.Lines)
            If blocks.Count = 0 Then
                Dim emptyDoc As OcrOverlayDocument = BuildDoc(job, engineName, provider, blocks)
                ocr_Cache.PutMemory(key, emptyDoc)
                If ocr_Settings.DiskCache Then ocr_Cache.SaveToDisk(key, emptyDoc)
                ApplyOnUi(emptyDoc, If(Is_Russian_Language, "Текст не найден", "No text found"), token)
                Return
            End If

            ' Translate.
            SetStatusOnUi(If(Is_Russian_Language, "Перевод…", "Translating…"))
            Dim translator As ITranslator = CreateTranslator()
            Dim available As Boolean = False
            If translator IsNot Nothing Then
                available = Await translator.ProbeAsync(token).ConfigureAwait(False)
            End If
            token.ThrowIfCancellationRequested()

            If Not available Then
                ' Show the recognized text untranslated; don't cache (allow retry).
                For Each b As OcrBlock In blocks
                    b.TranslatedText = b.SourceText
                Next
                ApplyOnUi(BuildDoc(job, engineName, provider, blocks),
                          If(Is_Russian_Language, "Переводчик недоступен", "Translator unavailable"), token)
                Return
            End If

            Dim sources As List(Of String) = blocks.Select(Function(b) b.SourceText).ToList()
            Dim translations As List(Of String) = Await translator.TranslateAsync(sources, job.SourceLang, job.TargetLang, token).ConfigureAwait(False)
            token.ThrowIfCancellationRequested()

            For i As Integer = 0 To blocks.Count - 1
                blocks(i).TranslatedText = If(i < translations.Count AndAlso Not String.IsNullOrWhiteSpace(translations(i)), translations(i), blocks(i).SourceText)
            Next

            Dim finalDoc As OcrOverlayDocument = BuildDoc(job, engineName, provider, blocks)
            ocr_Cache.PutMemory(key, finalDoc)
            If ocr_Settings.DiskCache Then ocr_Cache.SaveToDisk(key, finalDoc)
            ApplyOnUi(finalDoc, "", token)

        Catch ex As OperationCanceledException
            ' Navigated away — discard silently.
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ocr: pipeline error: " & ex.Message)
            SetStatusOnUi(If(Is_Russian_Language, "Ошибка OCR", "OCR error"))
        Finally
            Try
                job.Bitmap.Dispose()
            Catch
            End Try
        End Try
    End Function

    Private Function CreateTranslator() As ITranslator
        Select Case If(ocr_Settings.Provider, "").Trim().ToLowerInvariant()
            Case "libretranslate", "libre"
                Return New LibreTranslateTranslator(ocr_Settings.Endpoint, ocr_Settings.ApiKey)
            Case Else
                Return New OllamaTranslator(ocr_Settings.Endpoint, ocr_Settings.OllamaModel)
        End Select
    End Function

    Private Function BuildDoc(job As OcrJob, engineName As String, provider As String, blocks As List(Of OcrBlock)) As OcrOverlayDocument
        Return New OcrOverlayDocument With {
            .FilePath = job.FilePath,
            .FileWriteTicks = job.FileWriteTicks,
            .ImageSize = job.ImageSize,
            .SourceLanguage = job.SourceLang,
            .TargetLanguage = job.TargetLang,
            .Engine = engineName,
            .Translator = provider,
            .Blocks = blocks
        }
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
        If Not Image_File_Extensions.Contains(ext) Then Return False
        If Not File.Exists(Current_File_Name) Then Return False
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

    Private Sub UpdateOcrButtonVisual()
        If btn_Translate Is Nothing OrElse ocr_Settings Is Nothing Then Return
        Dim caption As String = If(Is_Russian_Language, "Перевод", "Translate")
        If ocr_Settings.AutoMode Then caption &= " ⟳"
        btn_Translate.Text = caption
        btn_Translate.Font = New Font(btn_Translate.Font, If(ocr_Settings.Enabled, FontStyle.Bold, FontStyle.Regular))
    End Sub

    ' --- settings window ------------------------------------------------------

    ''' <summary>Opens the dedicated "OCR и перевод" settings form and applies it.
    ''' Public-ish (Friend) so the Settings window (Table_Form) can open it too.</summary>
    Friend Sub ShowOcrTranslateSettings()
        If ocr_Settings Is Nothing Then
            ocr_Settings = New OcrTranslateSettings()
            ocr_Settings.Load(If(Is_Russian_Language, "ru", "en"))
        End If

        Using f As New OCR_Translate_Form(ocr_Settings)
            If f.ShowDialog(Me) = DialogResult.OK Then
                ocr_Settings.Save()
                ocr_Overlay_Visible = ocr_Settings.OverlayVisible
                UpdateOcrButtonVisual()
                InvalidateOverlay()
                If ocr_Settings.Enabled AndAlso ocr_Settings.AutoMode AndAlso IsCurrentFileEligibleImage() Then
                    TranslateCurrentImage(True)
                End If
            End If
        End Using
    End Sub

End Class
