Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms

' Second, "out to the browser" translation route next to our own in-app OCR
' overlay: a toolbar button that opens the current STILL image in the SZA companion
' "doc-html-translate.exe" for a free Google page-translation (no API key, no
' account). See SPECIFICATION_DOC_HTML_TRANSLATE_INTEGRATION.md. The launch itself
' lives in DocHtmlTranslate (DocHtmlTranslateLauncher.vb); this partial owns the
' button, its visibility and the discoverability entry in the "Translate" button's
' right-click menu.
Partial Public Class Main_Form

    Private WithEvents btn_TranslateBrowser As Button
    ' Right-click menu on the OCR "Translate" button - carries the "install the
    ' browser-translate companion" discoverability item when it isn't installed.
    Private translate_Menu As ContextMenuStrip

    ''' <summary>Creates the "Translate in browser" toolbar button. Called from
    ''' BuildModernLayout right after BuildOcrToolbarControls and before styling, so
    ''' it inherits the uniform chrome and joins the overflow set.</summary>
    Friend Sub BuildBrowserTranslateToolbarControls(host As Panel)
        If btn_TranslateBrowser IsNot Nothing OrElse host Is Nothing Then Return
        btn_TranslateBrowser = New Button With {
            .Name = "btn_TranslateBrowser",
            .Text = "",
            .AutoSize = True,
            .TabStop = False,
            .Visible = False
        }
        Dim icon As Image = LoadCompanionIcon()
        If icon IsNot Nothing Then btn_TranslateBrowser.Image = icon
        ' No icon -> keep a short text caption so the button is never blank.
        If icon Is Nothing Then btn_TranslateBrowser.Text = Localization.T("В браузере")
        host.Controls.Add(btn_TranslateBrowser)
    End Sub

    ''' <summary>Sets the tooltip, refreshes companion availability off-thread and
    ''' relayouts once the answer arrives. Called from Form1_Load after the OCR
    ''' init.</summary>
    Friend Sub InitializeBrowserTranslate()
        LocalizeBrowserTranslate()
        DocHtmlTranslate.RefreshAvailabilityAsync(
            Sub(available As Boolean)
                Try
                    If Me.IsDisposed Then Return
                    Me.BeginInvoke(Sub() ApplyTranslateButtonsVisibility())
                Catch
                End Try
            End Sub)
    End Sub

    ''' <summary>Re-localizes the browser-translate button tooltip. Called from
    ''' LngCh and InitializeBrowserTranslate.</summary>
    Friend Sub LocalizeBrowserTranslate()
        If toolTip IsNot Nothing AndAlso btn_TranslateBrowser IsNot Nothing Then
            toolTip.SetToolTip(btn_TranslateBrowser, Localization.T("Открыть картинку в браузере с бесплатным переводом Google (doc-html-translate). ЛКМ - открыть. Shift - заново распознать."))
        End If
        ' The blank-icon fallback caption is language-dependent.
        If btn_TranslateBrowser IsNot Nothing AndAlso btn_TranslateBrowser.Image Is Nothing Then
            btn_TranslateBrowser.Text = Localization.T("В браузере")
        End If
    End Sub

    ''' <summary>Static image: an image extension, the file exists, a PictureBox is
    ''' showing it, and it is NOT an active animation (a multi-frame GIF spins
    ''' gif_Restart_Timer, so gif_Restart_Image_Ref is non-Nothing only then).</summary>
    Private Function IsCurrentStillImage() As Boolean
        Return IsCurrentFileEligibleImage() AndAlso gif_Restart_Image_Ref Is Nothing
    End Function

    ''' <summary>Applies the visibility rule to BOTH translate buttons. The OCR
    ''' button is now shown only for still images (a behaviour change); the browser
    ''' companion button additionally needs the companion installed.</summary>
    Friend Sub ApplyTranslateButtonsVisibility()
        Dim still As Boolean = IsCurrentStillImage()
        SetToolbarItemHidden(btn_Translate, Not still)
        SetToolbarItemHidden(btn_TranslateBrowser, Not (still AndAlso DocHtmlTranslate.IsAvailableCached()))
#If Not NETFRAMEWORK Then
        ' The editor's button rides the same predicate and the same single recompute
        ' point (SPECIFICATION_IMAGE_EDITOR_DOTNET10.md §2): a still image can be edited,
        ' a video or a running animation cannot. Hidden only through SetToolbarItemHidden -
        ' PlaceControl force-shows anything it lays out, so a bare Visible = False would
        ' be undone by the next LayoutToolbar.
        SetToolbarItemHidden(btn_Edit, Not still)
#End If
        LayoutToolbar()
    End Sub

    ' --- button entry points --------------------------------------------------

    Private Sub btn_TranslateBrowser_Click(sender As Object, e As EventArgs) Handles btn_TranslateBrowser.Click
        Dim force As Boolean = (Control.ModifierKeys And Keys.Shift) <> 0
        LaunchBrowserTranslate(force)
    End Sub

    Private Sub LaunchBrowserTranslate(force As Boolean)
        If Not IsCurrentStillImage() Then Return
        Dim imagePath As String = Current_File_Name
        Dim ocrLang As String = BuildOcrLangArgument()

        SetOcrStatus(Localization.T("Открываю в браузере (doc-html-translate).."))

        Dim errMsg As String = ""
        Dim ok As Boolean = DocHtmlTranslate.TranslateImage(imagePath, ocrLang, force, errMsg,
            Sub(code As Integer)
                Try
                    If Me.IsDisposed Then Return
                    Me.BeginInvoke(Sub() SetOcrStatus(Localization.TF("Не удалось открыть перевод (код {0})", code.ToString())))
                Catch
                End Try
            End Sub)

        If Not ok Then
            SetOcrStatus(Localization.T("Не удалось запустить doc-html-translate"))
        End If
    End Sub

    ''' <summary>Maps the OCR source-language setting to a doc-html-translate
    ''' -ocr-lang value (Tesseract codes). "auto" becomes the first ~3 attempt codes
    ''' joined by "+"; an empty result means "don't pass -ocr-lang".</summary>
    Private Function BuildOcrLangArgument() As String
        If ocr_Settings Is Nothing Then Return ""
        Dim primary As String = OcrTranslateSettings.NormalizeOcrSourceCode(ocr_Settings.SourceLang)
        If primary <> "auto" Then Return primary
        Dim codes As List(Of String) = OcrTranslateSettings.OcrAttemptCodes("auto")
        If codes Is Nothing OrElse codes.Count = 0 Then Return ""
        Return String.Join("+", codes.Take(3).ToArray())
    End Function

    ' --- discoverability menu on the OCR "Translate" button -------------------

    ''' <summary>Right-click on the OCR "Translate" button. When the browser
    ''' companion isn't installed, offer to install it (spec §7); otherwise keep the
    ''' existing straight-to-OCR-settings behaviour.</summary>
    Friend Sub HandleTranslateRightClick(anchor As Control)
        If DocHtmlTranslate.IsAvailableCached() Then
            ShowOcrTranslateSettings()
            Return
        End If

        ' Recheck in the background so reopening the menu reflects a fresh install.
        DocHtmlTranslate.RefreshAvailabilityAsync(
            Sub(available As Boolean)
                Try
                    If Me.IsDisposed Then Return
                    Me.BeginInvoke(Sub() ApplyTranslateButtonsVisibility())
                Catch
                End Try
            End Sub)

        If translate_Menu Is Nothing Then translate_Menu = New ContextMenuStrip()
        ClearAndDisposeItems(translate_Menu.Items)

        Dim settingsItem As New ToolStripMenuItem(Localization.T("Настройки OCR.."))
        AddHandler settingsItem.Click, Sub() ShowOcrTranslateSettings()
        translate_Menu.Items.Add(settingsItem)

        translate_Menu.Items.Add(New ToolStripSeparator())

        Dim installItem As New ToolStripMenuItem(Localization.T("Перевод в браузере - установить doc-html-translate.."))
        AddHandler installItem.Click, Sub() OfferInstallCompanion()
        translate_Menu.Items.Add(installItem)

        Dim host As Control = If(anchor, btn_Translate)
        If host IsNot Nothing Then
            translate_Menu.Show(host, New Point(0, host.Height))
        End If
    End Sub

    Private Sub OfferInstallCompanion()
        Dim result As DialogResult = MessageBox.Show(Me,
            Localization.T("Установить бесплатный перевод изображения в браузере (doc-html-translate)?" & vbCrLf & "" & vbCrLf & "Да - установить через winget." & vbCrLf & "Нет - открыть страницу в магазине приложений."),
            Localization.T("Перевод в браузере"),
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)

        Select Case result
            Case DialogResult.Yes
                DocHtmlTranslate.StartWingetInstall()
                SetOcrStatus(Localization.T("Устанавливаю doc-html-translate.. после установки откройте меню снова"))
            Case DialogResult.No
                DocHtmlTranslate.OpenStorePage()
        End Select
    End Sub

    ' --- companion icon -------------------------------------------------------

    ''' <summary>Loads the embedded doc-html-translate PNG scaled to ~16 px (owner
    ''' chose the embedded resource over ExtractAssociatedIcon). Returns Nothing on
    ''' failure, in which case the button falls back to a text caption.</summary>
    Private Function LoadCompanionIcon() As Image
        Try
            Using bundled As Stream = RuntimeBootstrap.OpenBundledAsset("icons/doc-html-translate.png")
                If bundled Is Nothing Then Return Nothing
                Using src As Image = Image.FromStream(bundled)
                    Dim dest As New Bitmap(16, 16)
                    Using g As Graphics = Graphics.FromImage(dest)
                        g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                        g.DrawImage(src, New Rectangle(0, 0, 16, 16))
                    End Using
                    Return dest
                End Using
            End Using
        Catch
            Return Nothing
        End Try
    End Function

End Class
