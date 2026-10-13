Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>Six explain-first steps that use the manager's existing consent, folder and status acts.</summary>
Friend NotInheritable Class Manager_Welcome_Form
    Inherits Form
    Implements IThemeAware

    Private NotInheritable Class WelcomeLanguageCombo
        Inherits ComboBox
        Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
            Dim size As Size = MyBase.GetPreferredSize(proposedSize)
            Return New Size(size.Width, Math.Max(size.Height, ItemHeight + LogicalToDeviceUnits(6)))
        End Function
    End Class

    Private Const ContentWidth As Integer = 460
    Private ReadOnly host As MainWindow
    Private ReadOnly panels As New List(Of FlowLayoutPanel)()
    Private ReadOnly fonts As New List(Of Font)()
    Private ReadOnly headings As New List(Of Label)()
    Private ReadOnly counter As New Label With {.AutoSize = True}
    Private ReadOnly title As New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth - 80, 0)}
    Private ReadOnly badge As New StepBadge(AddressOf CompanionAssets.OpenBundledAsset, 64)
    Private ReadOnly dots As New StepDots With {.Count = 6}
    Private ReadOnly nextButton As New Button()
    Private ReadOnly backButton As New Button()
    Private ReadOnly laterButton As New Button()
    Private ReadOnly declineButton As New Button()
    Private ReadOnly refreshButton As New Button()
    Private ReadOnly pairButton As New Button()
    Private ReadOnly stateLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth - 32, 0)}
    Private ReadOnly stateGlyph As New PictureBox With {.Size = New Size(24, 24), .SizeMode = PictureBoxSizeMode.CenterImage}
    Private ReadOnly actionStatus As New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0)}
    Private enableButton As Button
    Private folderButton As Button
    Private language As ComboBox
    Private autostart As CheckBox
    Private openWindow As CheckBox
    Private lanOnly As CheckBox
    Private systemPort As RadioButton
    Private fixedPort As RadioButton
    Private port As NumericUpDown
    Private position As ManagerWelcomeStep
    Private busy As Boolean
    Private ReadOnly opened As New ShareSettings()

    Friend Sub New(manager As MainWindow)
        host = manager
        opened.Load()
        UiLanguage.ApplyTo(Me)
        Text = Localization.T("Расшарь свою первую папку")
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        AutoScroll = True
        Dim headingFont As New Font(Font, FontStyle.Bold)
        fonts.Add(headingFont)
        title.Font = headingFont

        Dim root As New TableLayoutPanel With {.AutoSize = True, .ColumnCount = 1, .Padding = New Padding(16)}
        root.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim header As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        badge.Margin = New Padding(0, 0, 16, 0)
        Dim titles As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.TopDown, .WrapContents = False}
        titles.Controls.Add(counter)
        titles.Controls.Add(title)
        header.Controls.Add(badge)
        header.Controls.Add(titles)
        root.Controls.Add(header, 0, 0)
        root.Controls.Add(dots, 0, 1)
        For Each kind As ManagerWelcomeStep In ManagerWelcomeModel.StepsFor()
            Dim panel As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Margin = Padding.Empty}
            BuildStep(panel, kind)
            panels.Add(panel)
            root.Controls.Add(panel, 0, panels.Count + 1)
        Next
        Dim footer As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 12, 0, 0)}
        SetupButton(laterButton, Localization.T("Позже"), "action.cancel")
        SetupButton(backButton, Localization.T("Назад"), "nav.back")
        SetupButton(nextButton, Localization.T("Далее"), "nav.forward")
        AddHandler laterButton.Click, Sub() Close()
        AddHandler backButton.Click, Sub() ShowStep(CType(CInt(position) - 1, ManagerWelcomeStep))
        AddHandler nextButton.Click, AddressOf OnNext
        footer.Controls.Add(laterButton)
        footer.Controls.Add(backButton)
        footer.Controls.Add(nextButton)
        root.Controls.Add(footer, 0, 8)
        Controls.Add(root)
        AcceptButton = nextButton
        AddHandler ThemeService.Changed, AddressOf ThemeChanged
        Dim start As ManagerWelcomeStep = ManagerWelcomeModel.StartStep(ServerFeatures.IsEnabled(), host.WelcomeStatusSnapshot().Green)
        If ManagerWelcomeStore.FreshAtLaunch AndAlso Not host.WelcomeStatusSnapshot().Green Then start = ManagerWelcomeStep.Introduction
        ShowStep(start)
        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Private Sub SetupButton(button As Button, text As String, glyph As String)
        button.Text = text
        button.AutoSize = True
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink
        button.MinimumSize = New Size(0, 44)
        button.Padding = New Padding(12, 4, 12, 4)
        ButtonGlyphs.Apply(button, glyph)
    End Sub

    Private Function AddText(panel As FlowLayoutPanel, text As String, Optional heading As Boolean = False) As Label
        Dim label As New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0),
            .Text = text, .UseMnemonic = False, .Margin = New Padding(0, If(heading, 10, 2), 0, 0)}
        If heading Then
            label.Font = title.Font
            headings.Add(label)
        End If
        panel.Controls.Add(label)
        Return label
    End Function

    Private Sub Explain(panel As FlowLayoutPanel, what As String, why As String, how As String, where As String)
        AddText(panel, Localization.T("Что это"), True)
        AddText(panel, what)
        AddText(panel, Localization.T("Зачем"), True)
        AddText(panel, why)
        AddText(panel, Localization.T("Как это будет работать"), True)
        AddText(panel, how)
        AddText(panel, Localization.T("Где это менять"), True)
        AddText(panel, where)
    End Sub

    Private Sub BuildStep(panel As FlowLayoutPanel, kind As ManagerWelcomeStep)
        Select Case kind
            Case ManagerWelcomeStep.Introduction
                Explain(panel, Localization.TF("{0} управляет сервером папок для телефона и SFTP-клиентов.", ProductNames.ShareManager),
                    Localization.T("Вы выбираете папки, а телефон получает доступ по QR-коду."),
                    Localization.T("Включите сервер, выберите первую папку и отсканируйте код. Закрытие окна оставляет программу в трее; двойной щелчок по значку открывает окно."),
                    Localization.T("Окно открывается из трея или кнопкой менеджера в просмотрщике. Повторить эти шаги: Настройки - Общие - Приветствие.."))
                SetupButton(declineButton, Localization.T("Не настраивать"), "action.cancel")
                AddHandler declineButton.Click, Sub()
                    ManagerWelcomeStore.SetState(ManagerWelcomeModel.Done)
                    DialogResult = DialogResult.Abort
                    Close()
                End Sub
                panel.Controls.Add(declineButton)
            Case ManagerWelcomeStep.Enable
                Explain(panel,
                    Localization.T("SFTP-сервер для выбранных папок: телефон с приложением или любой SFTP-клиент откроет их по сети."),
                    Localization.T("Телефон не видит папки на компьютере, пока сервер не включён. Включённый сервер ничего не открывает, пока вы не выбрали папки."),
                    If(ServerFeatures.IsEnabled(),
                       Localization.T("Сервер уже включён. Дополнительный запрос прав не нужен. На следующем шаге выберите папку для телефона."),
                       Localization.T("Сервер выключен до этого шага. Кнопка «Включить» запросит права администратора один раз и добавит одно правило брандмауэра Windows для этой программы. Телефоны подключаются сканированием QR-кода, который показывает окно программы.")),
                    Localization.T("Включить или выключить сервер: Настройки - Хостинг. Отмена запроса прав оставляет сервер выключенным; можно попробовать снова."))
                enableButton = New Button()
                SetupButton(enableButton, Localization.T("Включить сервер"), "action.run")
                AddHandler enableButton.Click, AddressOf OnEnable
                panel.Controls.Add(enableButton)
                panel.Controls.Add(actionStatus)
            Case ManagerWelcomeStep.Folder
                Explain(panel, Localization.T("Первая папка, которую телефон сможет открыть на этом компьютере."),
                    Localization.T("Без выбранной папки работающий сервер ещё ничего не раздаёт."),
                    Localization.T("Выберите папку, затем её имя, профиль и доступ. Настройки папки передаются серверу. Для продолжения нужна хотя бы одна папка; «Позже» сохранит уже сделанное."),
                    Localization.T("Папки и их параметры меняются в списке папок главного окна."))
                folderButton = New Button()
                SetupButton(folderButton, Localization.T("Добавить первый ресурс сейчас.."), "content.folder")
                AddHandler folderButton.Click, AddressOf OnFolder
                panel.Controls.Add(folderButton)
                AddText(panel, HostingText.FolderNoAccessTip())
            Case ManagerWelcomeStep.Port
                Explain(panel, Localization.T("Номер порта, на котором сервер принимает подключения."),
                    Localization.T("Номер записан в каждый QR-код и файл .fmscfg. Если его поменять, уже выданные коды перестанут работать."),
                    Localization.TF("«Выбирает система» - порт назначается один раз и больше не меняется. «Фиксированный» - вы задаёте номер сами, например для проброса порта на роутере. По умолчанию предлагается {0}.", ServerWelcomeModel.SuggestedFixedPort),
                    Localization.T("Порт и режим сети: Настройки - Сеть. Оставьте текущий выбор, если проброс порта на роутере не нужен."))
                systemPort = New RadioButton With {.Text = Localization.T("Выбирает система"), .AutoSize = True, .MinimumSize = New Size(0, 44)}
                fixedPort = New RadioButton With {.Text = Localization.T("Фиксированный порт:"), .AutoSize = True, .MinimumSize = New Size(0, 44)}
                port = New NumericUpDown With {.Minimum = ShareSettings.MinFixedPort, .Maximum = ShareSettings.MaxFixedPort, .Width = 130}
                Dim numberFont As New Font(Font.FontFamily, 26.0F, FontStyle.Regular)
                fonts.Add(numberFont)
                port.Font = numberFont
                port.Width = 200
                systemPort.Checked = opened.ListenPort = 0
                fixedPort.Checked = Not systemPort.Checked
                port.Value = If(systemPort.Checked, ShareSettings.SuggestedFixedPort, opened.ListenPort)
                port.Enabled = fixedPort.Checked
                AddHandler fixedPort.CheckedChanged, Sub() port.Enabled = fixedPort.Checked
                panel.Controls.Add(systemPort)
                panel.Controls.Add(fixedPort)
                panel.Controls.Add(port)
                AddText(panel, Localization.T("Правило брандмауэра относится к программе, а не к порту, поэтому смена порта его не требует."))
            Case ManagerWelcomeStep.Basics
                Explain(panel, Localization.T("Язык, запуск с Windows, открытие окна и доступ только из локальной сети."),
                    Localization.T("Эти параметры определяют, как программа встречает вас и откуда к ней можно подключиться."),
                    Localization.T("Без автозапуска программу запускаете вы. Без открытия окна обычный запуск показывает значок в трее и подсказку. Режим локальной сети отключает проброс порта и адрес из интернета; выключенный режим допускает оба пути."),
                    Localization.T("Язык и запуск: Настройки - Общие. Режим доступа: Настройки - Сеть."))
                AddText(panel, Localization.T("Язык интерфейса"))
                language = New WelcomeLanguageCombo With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 280, .AutoSize = True}
                ThemedComboBox.Apply(language, ThemeService.Palette)
                language.ItemHeight = 44
                language.Items.AddRange(Localization.Names)
                language.SelectedIndex = Localization.CurrentIndex
                panel.Controls.Add(language)
                autostart = Question(panel, Localization.T("Запускать при входе в Windows"), opened.AutostartEnabled)
                autostart.Enabled = Not AutostartManager.IsPackaged()
                If Not autostart.Enabled Then AddText(panel, Localization.T("Автозапуск установленной версии из Store управляется в настройках Windows."))
                openWindow = Question(panel, Localization.T("Открывать окно менеджера при запуске"), opened.OpenWindowOnStartup)
                lanOnly = Question(panel, Localization.T("Только локальная сеть (без доступа из интернета)"), opened.LanOnly)
            Case ManagerWelcomeStep.Finish
                Explain(panel, Localization.T("Состояние сервера и выбранных папок проверяется сейчас."),
                    Localization.T("Зелёный статус означает работающую раздачу с доступом к папкам, а не обещание мастера."),
                    Localization.T("Откройте QR-код и отсканируйте его телефоном. Если статус требует внимания, исправьте причину в главном окне или вернитесь к папке."),
                    Localization.T("Папки: главное окно. Сервер: Настройки - Хостинг. Порт и доступ: Настройки - Сеть. Язык и запуск: Настройки - Общие. Быстрые действия: меню трея."))
                Dim stateRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
                stateRow.Controls.Add(stateGlyph)
                stateRow.Controls.Add(stateLabel)
                panel.Controls.Add(stateRow)
                SetupButton(refreshButton, Localization.T("Обновить"), "action.refresh")
                AddHandler refreshButton.Click, Async Sub() Await RefreshFinishAsync()
                panel.Controls.Add(refreshButton)
                SetupButton(pairButton, Localization.T("QR-код для телефона"), "action.share")
                AddHandler pairButton.Click, Sub() host.WelcomePairPhone(Me)
                panel.Controls.Add(pairButton)
        End Select
    End Sub

    Private Shared Function Question(panel As FlowLayoutPanel, text As String, value As Boolean) As CheckBox
        Dim check As New CheckBox With {.Text = text, .Checked = value, .AutoSize = True,
            .MaximumSize = New Size(ContentWidth, 0), .MinimumSize = New Size(0, 44)}
        panel.Controls.Add(check)
        Return check
    End Function

    Private Sub ShowStep(kind As ManagerWelcomeStep)
        position = kind
        For i As Integer = 0 To panels.Count - 1
            panels(i).Visible = i = CInt(kind)
        Next
        Dim titles As String() = {"Расшарь свою первую папку", "Включить сервер", "Расшарь свою первую папку", "Порт", "Основные настройки", "Готово"}
        Dim glyphs As String() = {"action.share", "action.run", "content.folder", "system.wifi", "app.settings", "status.ok"}
        title.Text = Localization.T(titles(CInt(kind)))
        badge.GlyphId = glyphs(CInt(kind))
        counter.Text = Localization.TF("Шаг {0} из {1}", CInt(kind) + 1, panels.Count)
        dots.Index = CInt(kind)
        backButton.Enabled = kind > ManagerWelcomeStep.Introduction
        nextButton.Text = Localization.T(If(kind = ManagerWelcomeStep.Finish, "Готово", "Далее"))
        ButtonGlyphs.Apply(nextButton, If(kind = ManagerWelcomeStep.Finish, "action.confirm", "nav.forward"), ButtonGlyphRole.Affirmative)
        RefreshActions()
        ThemeChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub RefreshActions()
        nextButton.Enabled = Not busy AndAlso ManagerWelcomeModel.CanAdvance(position, ServerFeatures.IsEnabled(), host.WelcomeStatusSnapshot().FolderCount)
        enableButton.Enabled = Not busy AndAlso Not ServerFeatures.IsEnabled()
        folderButton.Enabled = Not busy
        actionStatus.Text = Localization.T(If(ServerFeatures.IsEnabled(), "Сервер включён.", "Сервер пока выключен. Это не ошибка: включить его можно позже в настройках."))
        laterButton.Enabled = Not busy
        backButton.Enabled = Not busy AndAlso position > ManagerWelcomeStep.Introduction
        refreshButton.Enabled = Not busy
    End Sub

    Private Async Sub OnEnable(sender As Object, e As EventArgs)
        Using dialog As New Share_Enable_Form()
            dialog.ShowDialog(Me)
        End Using
        busy = True
        RefreshActions()
        Try
            Await host.WelcomeRefreshAsync(True)
        Finally
            busy = False
            If Not IsDisposed Then RefreshActions()
        End Try
    End Sub

    Private Async Sub OnFolder(sender As Object, e As EventArgs)
        busy = True
        RefreshActions()
        Try
            Await host.AddWelcomeFolderAsync(Me)
            Await host.WelcomeRefreshAsync()
            If Not IsDisposed AndAlso host.WelcomeHint <> "" Then
                MessageBox.Show(Me, host.WelcomeHint, Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
            End If
        Finally
            busy = False
            If Not IsDisposed Then RefreshActions()
        End Try
    End Sub

    Private Async Sub OnNext(sender As Object, e As EventArgs)
        If busy OrElse Not ManagerWelcomeModel.CanAdvance(position, ServerFeatures.IsEnabled(), host.WelcomeStatusSnapshot().FolderCount) Then Return
        busy = True
        RefreshActions()
        Try
            If position = ManagerWelcomeStep.Finish Then
                ManagerWelcomeStore.SetState(ManagerWelcomeModel.Done)
                DialogResult = DialogResult.OK
                Close()
                Return
            End If
            If position = ManagerWelcomeStep.Port Then Await CommitPortAsync()
            If position = ManagerWelcomeStep.Basics Then Await CommitBasicsAsync()
            If IsDisposed Then Return
            ShowStep(CType(CInt(position) + 1, ManagerWelcomeStep))
            If position = ManagerWelcomeStep.Finish Then
                Await RefreshFinishAsync()
                ManagerWelcomeStore.SetState(ManagerWelcomeModel.Done)
            End If
        Catch
            If Not IsDisposed Then MessageBox.Show(Me, Localization.T("Не удалось сохранить настройки. Попробуйте ещё раз."), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            busy = False
            If Not IsDisposed Then RefreshActions()
        End Try
    End Sub

    Private Async Function CommitPortAsync() As Task
        Dim chosen As Integer = ServerWelcomeModel.PortToStore(systemPort.Checked, CInt(port.Value))
        If chosen = opened.ListenPort Then Return
        Dim settings As New ShareSettings()
        settings.Load()
        settings.ListenPort = chosen
        settings.Save()
        opened.ListenPort = chosen
        Await ShareController.PushNetworkPolicyAsync()
    End Function

    Private Async Function CommitBasicsAsync() As Task
        Dim settings As New ShareSettings()
        settings.Load()
        Dim changed As Boolean = False
        If autostart.Checked <> opened.AutostartEnabled AndAlso autostart.Enabled Then
            AutostartManager.SetEnabled(autostart.Checked)
            settings.AutostartEnabled = AutostartManager.IsEnabled()
            changed = True
        End If
        If openWindow.Checked <> opened.OpenWindowOnStartup Then
            settings.OpenWindowOnStartup = openWindow.Checked
            changed = True
        End If
        Dim policyChanged As Boolean = lanOnly.Checked <> opened.LanOnly
        If policyChanged Then
            settings.LanOnly = lanOnly.Checked
            changed = True
        End If
        If changed Then settings.Save()
        opened.Load()
        If policyChanged Then Await ShareController.PushNetworkPolicyAsync()
        If language.SelectedIndex >= 0 Then UiLanguage.Switch(Localization.Codes(language.SelectedIndex))
    End Function

    Private Async Function RefreshFinishAsync() As Task
        Await host.WelcomeRefreshAsync()
        If IsDisposed Then Return
        Dim snapshot As ManagerWelcomeStatus = host.WelcomeStatusSnapshot()
        RenderFinish(snapshot)
    End Function

    Private Sub RenderFinish(snapshot As ManagerWelcomeStatus)
        stateLabel.Text = snapshot.Text
        stateLabel.ForeColor = snapshot.Color
        Dim meaning As IconMeaning = IconMap.GetMeaning(snapshot.Glyph)
        Dim previous As Image = stateGlyph.Image
        stateGlyph.Image = GlyphRenderer.Render(meaning.GlyphId, LogicalToDeviceUnits(24), snapshot.Color, AddressOf CompanionAssets.OpenBundledAsset)
        stateGlyph.AccessibleName = snapshot.Text
        If previous IsNot Nothing Then previous.Dispose()
        pairButton.Enabled = snapshot.Green
    End Sub

    Private Sub ThemeChanged(sender As Object, e As EventArgs)
        ApplyWelcomeTheme(ThemeService.Palette)
    End Sub

    Private ReadOnly Property ThemesItself As Boolean Implements IThemeAware.ThemesItself
        Get
            Return False
        End Get
    End Property

    Private Sub ApplyWelcomeTheme(palette As ThemePalette) Implements IThemeAware.ApplyTheme
        For Each heading As Label In headings
            heading.ForeColor = ThemePalette.ReadableText(palette.Accent, palette.SurfaceWindow)
        Next
        badge.Invalidate()
        dots.Invalidate()
        If position = ManagerWelcomeStep.Finish Then RenderFinish(host.WelcomeStatusSnapshot())
    End Sub

    Protected Overrides Async Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If position = ManagerWelcomeStep.Finish Then Await RefreshFinishAsync()
        nextButton.Focus()
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        language.ItemHeight = LogicalToDeviceUnits(44)
        panels(CInt(ManagerWelcomeStep.Basics)).PerformLayout()
        MaximumSize = Screen.FromControl(Me).WorkingArea.Size
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    Protected Overrides Sub OnDpiChanged(e As DpiChangedEventArgs)
        MyBase.OnDpiChanged(e)
        language.ItemHeight = LogicalToDeviceUnits(44)
        MaximumSize = Screen.FromControl(Me).WorkingArea.Size
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If keyData = Keys.Escape Then
            If language.DroppedDown Then
                language.DroppedDown = False
            ElseIf Not busy Then
                Close()
            End If
            Return True
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If busy AndAlso DialogResult <> DialogResult.OK Then e.Cancel = True
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            RemoveHandler ThemeService.Changed, AddressOf ThemeChanged
            If stateGlyph.Image IsNot Nothing Then stateGlyph.Image.Dispose()
        End If
        MyBase.Dispose(disposing)
        If disposing Then
            For Each owned As Font In fonts
                owned.Dispose()
            Next
        End If
    End Sub
End Class
