Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Windows.Forms

''' <summary>
''' The management body of Scheduled file operations (L0297): ONE live view of the engine that two
''' hosts embed - the Manager settings' scheduled page (ticket 222, 7.1) and the standalone Scheduled
''' operations window. It holds no scheduler data of its own: every row is read from
''' <see cref="ScheduledHost.Engine"/> and every command goes to it, so the page, the window and the
''' tray menu can never disagree. The editor and history dialogs open over the hosting form.
''' The panel applies no language of its own: the host applies the script font and reading direction
''' to the whole window before building, and the children inherit both.
''' </summary>
Friend NotInheritable Class Scheduled_Manager_Panel
    Inherits UserControl

    ''' <summary>Wrap width of the prose rows, 96-DPI units - the settings page's own content width,
    ''' kept identical so the page and the window read the same.</summary>
    Private Const ContentWidth As Integer = 520

    Private _schedNote As Label
    Private _schedList As ListView
    Private _schedNew As Button
    Private _schedEdit As Button
    Private _schedDelete As Button
    Private _schedRunNow As Button
    Private _schedRunAll As Button
    Private _schedPause As Button
    Private _schedHistory As Button
    Private _schedClearHistory As Button
    Private _schedExport As Button
    Private _schedImport As Button
    Private _schedFailure As Label
    Private _schedDiagnose As Button
    Private _schedFeature As CheckBox
    Private _schedAutostart As Label
    Private _schedAutostartLink As LinkLabel
    Private _schedUpdating As Boolean
    ''' <summary>A folder the viewer's launcher asked to start an operation from; used once, when the
    ''' host is up.</summary>
    Private _schedPrefill As String = ""
    Private _schedOpenEditor As Boolean
    ''' <summary>Read-only use: the autostart line mirrors the persisted switch. Loaded once, in the
    ''' constructor - the engine events refresh everything else live.</summary>
    Private ReadOnly _panelSettings As New ShareSettings()

    ''' <summary>Raised after every refresh of the list, so a host (the settings page's section
    ''' summary) can keep its own counter honest without reading the engine a second time.</summary>
    Friend Event StateChanged As EventHandler

    ''' <summary>The autostart link was clicked: each host routes it its own way - the settings page
    ''' to its startup page, the standalone window to the settings window's startup page.</summary>
    Friend Event AutostartLinkRequested As EventHandler

    Public Sub New()
        _panelSettings.Load()
        BuildPanel()
        ApplyScheduledState()
        If ScheduledHost.Engine IsNot Nothing Then
            AddHandler ScheduledHost.Engine.Changed, AddressOf OnScheduledEngineChanged
            AddHandler Disposed, Sub() RemoveHandler ScheduledHost.Engine.Changed, AddressOf OnScheduledEngineChanged
        End If
    End Sub

    Private Sub BuildPanel()
        Dim root As New TableLayoutPanel With {.Name = "schedPanelRoot", .Dock = DockStyle.Top, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 1, .Margin = New Padding(0)}
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        ' --- this PC ---
        _schedFeature = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 6, 0, 2), .Name = "managerSchedFeature",
            .Text = Localization.T("Выполнять операции по расписанию на этом компьютере")}
        AddHandler _schedFeature.CheckedChanged, AddressOf OnScheduledFeatureChanged
        Dim featureRow As OptionRowHost = OptionRows.InFlow(_schedFeature,
            OptionHint(_schedFeature, Localization.T("Выключите, чтобы остановить все расписания. Запуск вручную остаётся доступным.")))
        featureRow.Margin = New Padding(0, 6, 0, 2)
        AddRow(root, featureRow)

        _schedAutostart = NoteLabel("")
        _schedAutostart.Name = "managerSchedAutostart"
        AddRow(root, _schedAutostart)
        _schedAutostartLink = New LinkLabel With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 6), .Name = "managerSchedAutostartLink",
            .Text = Localization.T("Открыть настройку автозапуска")}
        AddHandler _schedAutostartLink.LinkClicked, Sub() RaiseEvent AutostartLinkRequested(Me, EventArgs.Empty)
        AddRow(root, _schedAutostartLink)

        ' The statement of section 0, once: what this is and where it lives. The sentence every surface repeats.
        _schedNote = NoteLabel(Localization.T("Файловые операции по расписанию - часть менеджера общего доступа: устанавливаются вместе с ним, выполняются им и настраиваются в его настройках. Расписание работает только пока менеджер запущен."))
        _schedNote.Name = "managerSchedNote"
        AddRow(root, _schedNote)

        _schedList = New ListView With {.View = View.Details, .FullRowSelect = True, .HideSelection = False, .MultiSelect = False,
            .CheckBoxes = True, .Height = 220, .Dock = DockStyle.Top, .Margin = New Padding(0, 4, 0, 6), .Name = "managerSchedList",
            .AccessibleName = Share_Settings_Form.ScheduledPageCaption()}
        AddScheduledColumns()
        AddHandler _schedList.SelectedIndexChanged, Sub() RefreshScheduledButtons()
        AddHandler _schedList.ItemChecked, AddressOf OnScheduledItemChecked
        AddHandler _schedList.DoubleClick, Sub() OnScheduledEdit()
        AddRow(root, _schedList)

        Dim commands As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = True,
            .Dock = DockStyle.Top, .Margin = New Padding(0, 2, 0, 4), .Name = "managerSchedCommands"}
        _schedNew = SchedButton("managerSchedNew", Localization.T("Создать.."), "action.add", AddressOf OnScheduledNew)
        _schedEdit = SchedButton("managerSchedEdit", Localization.T("Изменить.."), "action.edit", AddressOf OnScheduledEdit)
        _schedDelete = SchedButton("managerSchedDelete", Localization.T("Удалить"), "action.delete", AddressOf OnScheduledDelete)
        _schedRunNow = SchedButton("managerSchedRunNow", Localization.T("Запустить сейчас"), "action.run", AddressOf OnScheduledRunNow)
        _schedRunAll = SchedButton("managerSchedRunAll", Localization.T("Запустить все сейчас"), "media.play", AddressOf OnScheduledRunAll)
        _schedPause = SchedButton("managerSchedPause", Localization.T("Приостановить все"), "media.pause", AddressOf OnScheduledPause)
        _schedHistory = SchedButton("managerSchedHistory", Localization.T("Журнал.."), "source.recent", AddressOf OnScheduledHistory)
        _schedClearHistory = SchedButton("managerSchedClearHistory", Localization.T("Очистить журнал"), "action.clear-all", AddressOf OnScheduledClearHistory)
        _schedExport = SchedButton("managerSchedExport", Localization.T("Экспорт.."), "action.export", AddressOf OnScheduledExport)
        _schedImport = SchedButton("managerSchedImport", Localization.T("Импорт.."), "action.import", AddressOf OnScheduledImport)
        commands.Controls.AddRange({_schedNew, _schedEdit, _schedDelete, _schedRunNow, _schedRunAll, _schedPause, _schedHistory, _schedClearHistory, _schedExport, _schedImport})
        AddRow(root, commands)

        _schedFailure = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 0, 0, 8),
            .Name = "managerSchedFailure", .Visible = False}
        AddRow(root, _schedFailure)
        _schedDiagnose = SchedButton("managerSchedDiagnose", ConnectionDiagnosisLocalization.DoorButtonText(Localization.CurrentCode), "action.test-connection", AddressOf OnScheduledDiagnoseClicked)
        _schedDiagnose.Visible = False
        AddRow(root, _schedDiagnose)

        Controls.Add(root)
    End Sub

    Private Sub AddRow(root As TableLayoutPanel, control As Control)
        root.RowCount += 1
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        root.Controls.Add(control, 0, root.RowCount - 1)
        control.Dock = DockStyle.Top
    End Sub

    ''' <summary>The explanation that stands under a check box's caption inside its row - the same
    ''' shape the settings window's rows use (named after the check box, which is how the window
    ''' contract finds it). Nothing for an empty text.</summary>
    Private Shared Function OptionHint(check As CheckBox, text As String) As Label
        If text.Length = 0 Then Return Nothing
        Return New Label With {.Name = check.Name & "Hint", .Text = text, .ForeColor = ThemeService.Palette.TextMuted}
    End Function

    Private Shared Function NoteLabel(text As String) As Label
        Return New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0),
            .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 0, 0, 10), .Text = text}
    End Function

    Private Function SchedButton(name As String, text As String, glyph As String, onClick As Action) As Button
        Dim b As New Button With {.Name = name, .Text = text, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Margin = New Padding(0, 2, 8, 2), .Padding = New Padding(12, 5, 12, 5)}
        ButtonGlyphs.Apply(b, glyph)
        AddHandler b.Click, Sub() onClick()
        Return b
    End Function

    Private Sub AddScheduledColumns()
        _schedList.Columns.Clear()
        _schedList.Columns.Add(Localization.T("Название"), LogicalToDeviceUnits(150))
        _schedList.Columns.Add(Localization.T("Действие"), LogicalToDeviceUnits(90))
        _schedList.Columns.Add(Localization.T("Источник и назначение"), LogicalToDeviceUnits(200))
        _schedList.Columns.Add(Localization.T("Расписание"), LogicalToDeviceUnits(130))
        _schedList.Columns.Add(Localization.T("Последний запуск"), LogicalToDeviceUnits(200))
        _schedList.Columns.Add(Localization.T("Следующий запуск"), LogicalToDeviceUnits(120))
    End Sub

    ' --- host API --------------------------------------------------------------------------------------

    ''' <summary>How many operations the list holds right now - the host's summary line.</summary>
    Friend ReadOnly Property OperationCount As Integer
        Get
            Return If(_schedList Is Nothing, 0, _schedList.Items.Count)
        End Get
    End Property

    ''' <summary>Refreshes every row, button and switch from the engine.</summary>
    Friend Sub ApplyState()
        ApplyScheduledState()
    End Sub

    ''' <summary>The language changed: the captions of the panel's own controls and its columns.</summary>
    Friend Sub RefreshLanguage()
        RefreshScheduledLanguage()
    End Sub

    ''' <summary>A folder the viewer's launcher asked to start a new operation from. Once the host is
    ''' presented the editor opens at once; otherwise the host calls <see cref="HandleScheduledPrefill"/>
    ''' from its own OnShown.</summary>
    Friend Sub PrefillFromFolder(folder As String)
        _schedPrefill = If(folder, "")
        _schedOpenEditor = _schedPrefill.Length > 0
        If _schedOpenEditor AndAlso IsHandleCreated AndAlso Visible Then HandleScheduledPrefill()
    End Sub

    ''' <summary>Called once the host is shown (from its one OnShown): the editor the launcher asked for.</summary>
    Friend Sub HandleScheduledPrefill()
        If Not _schedOpenEditor Then Return
        _schedOpenEditor = False
        Dim folder As String = _schedPrefill
        _schedPrefill = ""
        BeginInvoke(New Action(Sub()
                                   If IsDisposed OrElse Disposing Then Return
                                   OpenScheduledEditor(Nothing, folder)
                               End Sub))
    End Sub

    ''' <summary>The form the panel is embedded in - the owner of every dialog and question. The panel
    ''' itself before it is parented (a window being built): every call site takes an IWin32Window.</summary>
    Private Function HostDialogOwner() As IWin32Window
        Dim owner As Control = If(Me.FindForm(), CType(Me, Control))
        Return owner
    End Function

    ' --- state -----------------------------------------------------------------------------------------

    Private Sub OnScheduledEngineChanged(sender As Object, e As EventArgs)
        Try
            If IsDisposed OrElse Disposing OrElse Not IsHandleCreated Then Return
            BeginInvoke(New Action(Sub()
                                       If IsDisposed OrElse Disposing Then Return
                                       ApplyScheduledState()
                                   End Sub))

        Catch ex As InvalidOperationException
            ' The window is going away.
        End Try
    End Sub

    ''' <summary>Fills the panel from the engine: the list, the buttons, the machine switches.</summary>
    Private Sub ApplyScheduledState()
        If _schedList Is Nothing Then Return
        Dim engine As ScheduledEngine = ScheduledHost.Engine
        _schedUpdating = True
        Try
            Dim selected As String = SelectedScheduledId()
            _schedList.BeginUpdate()
            _schedList.Items.Clear()
            If engine IsNot Nothing Then
                For Each state As ScheduledOperationState In engine.Snapshot()
                    Dim op As ScheduledOperation = state.Operation
                    Dim row As New ListViewItem(ScheduledText.RowTitleText(op)) With {.Tag = op.Id, .Checked = op.Enabled}
                    row.SubItems.Add(ScheduledText.ActionName(op.Action))
                    row.SubItems.Add(ScheduledText.WhereText(op))
                    row.SubItems.Add(ScheduledText.ScheduleText(op))
                    row.SubItems.Add(ScheduledText.LastResultText(state))
                    row.SubItems.Add(If(op.IsArmable, ScheduledText.WhenText(op.Record.NextRunAt, DateTime.Today), "-"))
                    _schedList.Items.Add(row)
                    If op.Id = selected Then row.Selected = True
                Next
            End If
            _schedList.EndUpdate()

            _schedFeature.Checked = engine IsNot Nothing AndAlso engine.FeatureEnabled
            _schedPause.Text = If(engine IsNot Nothing AndAlso engine.Paused, Localization.T("Возобновить все"), Localization.T("Приостановить все"))
            ButtonGlyphs.Apply(_schedPause, If(engine IsNot Nothing AndAlso engine.Paused, "media.play", "media.pause"))

            Dim failure As ScheduledFailureInfo = If(engine IsNot Nothing, engine.LastFailure, Nothing)
            _schedFailure.Visible = failure IsNot Nothing
            Dim diagHost As String = ""
            Dim diagPort As Integer = 0
            _schedDiagnose.Visible = failure IsNot Nothing AndAlso
                (failure.Outcome = ScheduledRunOutcome.Wall OrElse failure.Outcome = ScheduledRunOutcome.Failed OrElse failure.Outcome = ScheduledRunOutcome.Refused) AndAlso
                TryFailureEndpoint(failure, diagHost, diagPort)
            If failure IsNot Nothing Then
                _schedFailure.Text = ScheduledText.FailureHint(failure)
                _schedFailure.ForeColor = CollapsibleSection.AttentionColor
                _schedDiagnose.Text = ConnectionDiagnosisLocalization.DoorButtonText(Localization.CurrentCode)
            End If

            Dim autostart As Boolean = False
            Try
                autostart = _panelSettings.AutostartEnabled
            Catch
            End Try
            _schedAutostart.Text = ScheduledText.AutostartLine(autostart, engine IsNot Nothing AndAlso engine.Paused)
        Finally
            _schedUpdating = False
        End Try
        RefreshScheduledButtons()
        RaiseEvent StateChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub RefreshScheduledButtons()
        Dim has As Boolean = SelectedScheduledId().Length > 0
        _schedEdit.Enabled = has
        _schedDelete.Enabled = has
        _schedRunNow.Enabled = has
        _schedRunAll.Enabled = _schedList.Items.Count > 0
    End Sub

    Private Function SelectedScheduledId() As String
        If _schedList Is Nothing OrElse _schedList.SelectedItems.Count = 0 Then Return ""
        Return Convert.ToString(_schedList.SelectedItems(0).Tag)
    End Function

    ''' <summary>The language changed: the captions of the panel's own controls and its columns.</summary>
    Private Sub RefreshScheduledLanguage()
        If _schedList Is Nothing Then Return
        _schedNote.Text = Localization.T("Файловые операции по расписанию - часть менеджера общего доступа: устанавливаются вместе с ним, выполняются им и настраиваются в его настройках. Расписание работает только пока менеджер запущен.")
        AddScheduledColumns()
        _schedNew.Text = Localization.T("Создать..")
        _schedEdit.Text = Localization.T("Изменить..")
        _schedDelete.Text = Localization.T("Удалить")
        _schedRunNow.Text = Localization.T("Запустить сейчас")
        _schedRunAll.Text = Localization.T("Запустить все сейчас")
        _schedHistory.Text = Localization.T("Журнал..")
        _schedClearHistory.Text = Localization.T("Очистить журнал")
        _schedExport.Text = Localization.T("Экспорт..")
        _schedImport.Text = Localization.T("Импорт..")
        OptionRows.RowOf(_schedFeature).SetCaption(Localization.T("Выполнять операции по расписанию на этом компьютере"))
        _schedAutostartLink.Text = Localization.T("Открыть настройку автозапуска")
        _schedList.AccessibleName = Share_Settings_Form.ScheduledPageCaption()
        ApplyScheduledState()
    End Sub

    ' --- commands --------------------------------------------------------------------------------------

    Private Sub OnScheduledNew()
        OpenScheduledEditor(Nothing, "")
    End Sub

    Private Sub OnScheduledEdit()
        Dim id As String = SelectedScheduledId()
        If id.Length = 0 Then Return
        Dim engine As ScheduledEngine = ScheduledHost.Engine
        If engine Is Nothing Then Return
        Dim op As ScheduledOperation = engine.TryGet(id)
        If op Is Nothing Then Return
        If op.Halt = ScheduledHalt.InvalidDefinition Then
            ' A newer build wrote it: it can be deleted here, never edited into something this build understands.
            ShowScheduledNotice(ScheduledText.IssueText(New ScheduledIssue(ScheduledIssueCode.DefinitionUnreadable)))
            Return
        End If
        OpenScheduledEditor(op, "")
    End Sub

    Private Sub OpenScheduledEditor(operation As ScheduledOperation, prefill As String)
        Using dialog As New Scheduled_Operation_Form(ScheduledHost.Engine, operation, prefill)
            dialog.ShowDialog(HostDialogOwner())
        End Using
        ApplyScheduledState()
    End Sub

    Private Sub OnScheduledDelete()
        Dim id As String = SelectedScheduledId()
        If id.Length = 0 Then Return
        Dim engine As ScheduledEngine = ScheduledHost.Engine
        If engine Is Nothing Then Return
        Dim op As ScheduledOperation = engine.TryGet(id)
        If op Is Nothing Then Return
        If ManagerBox.Show(HostDialogOwner(), Localization.TF("Удалить операцию «{0}»? Файлы она не затрагивает.", op.DisplayName()),
                           Share_Settings_Form.ScheduledPageCaption(), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                           MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        engine.Delete(id)
    End Sub

    Private Sub OnScheduledRunNow()
        Dim id As String = SelectedScheduledId()
        If id.Length = 0 Then Return
        Dim engine As ScheduledEngine = ScheduledHost.Engine
        If engine Is Nothing Then Return
        Select Case engine.RunNow(id)
            Case TriggerOutcome.AlreadyRunning
                ShowScheduledNotice(Localization.T("Эта операция уже выполняется или ждёт очереди."))
            Case TriggerOutcome.NotFound
                ApplyScheduledState()
        End Select
    End Sub

    Private Sub OnScheduledRunAll()
        Dim engine As ScheduledEngine = ScheduledHost.Engine
        If engine Is Nothing Then Return
        Select Case engine.RunAllNow()
            Case TriggerOutcome.Paused
                ShowScheduledNotice(Localization.T("Расписание приостановлено: сначала возобновите его."))
            Case TriggerOutcome.NothingToRun
                ShowScheduledNotice(Localization.T("Нет включённых операций."))
        End Select
    End Sub

    Private Sub ShowScheduledNotice(text As String)
        _schedFailure.Text = text
        _schedFailure.ForeColor = ThemeService.Palette.TextMuted
        _schedFailure.Visible = True
    End Sub

    Private Sub OnScheduledPause()
        Dim engine As ScheduledEngine = ScheduledHost.Engine
        If engine Is Nothing Then Return
        engine.Paused = Not engine.Paused
        ApplyScheduledState()
    End Sub

    Private Sub OnScheduledHistory()
        Using dialog As New Scheduled_History_Form(ScheduledHost.Engine)
            dialog.ShowDialog(HostDialogOwner())
        End Using
    End Sub

    Private Sub OnScheduledClearHistory()
        If ManagerBox.Show(HostDialogOwner(), Localization.T("Очистить журнал операций по расписанию? Сами операции и их расписание не изменятся."),
                           Share_Settings_Form.ScheduledPageCaption(), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                           MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Dim engine As ScheduledEngine = ScheduledHost.Engine
        If engine IsNot Nothing Then engine.Journal.ClearHistory()
    End Sub

    Private Sub OnScheduledExport()
        Using dialog As New SaveFileDialog() With {
            .Title = Localization.T("Экспорт операций по расписанию"),
            .Filter = "JSON (*.json)|*.json",
            .DefaultExt = "json",
            .FileName = "scheduled-operations.json"}
            If dialog.ShowDialog(HostDialogOwner()) <> DialogResult.OK Then Return
            Try
                Dim store As New ScheduledStore(ScheduledStore.DefaultFolder())
                ShareSettingsTransfer.ExportToFile(store, dialog.FileName)
            Catch ex As Exception
                AppFileLogger.WriteLine("Scheduled operations: export failed (" & ex.GetType().Name & ")")
                ShowScheduledNotice(Localization.T("Не удалось записать файл экспорта."))
            End Try
        End Using
    End Sub

    Private Sub OnScheduledImport()
        Using dialog As New OpenFileDialog() With {
            .Title = Localization.T("Импорт операций по расписанию"),
            .Filter = "JSON (*.json)|*.json"}
            If dialog.ShowDialog(HostDialogOwner()) <> DialogResult.OK Then Return
            Try
                Dim store As New ScheduledStore(ScheduledStore.DefaultFolder())
                Dim outcome As ShareSettingsImportResult = ShareSettingsTransfer.ImportFromFile(dialog.FileName, store, AddressOf ScheduledResourceRegistry.Lookup, ScheduledHost.Engine)
                ApplyScheduledState()
                ' After the refresh: it hides the notice line when nothing failed to run.
                ShowScheduledNotice(If(outcome.Readable,
                                       Localization.TF("Импортировано операций: {0}. Не импортировано: {1}.", outcome.Imported, outcome.NotImported),
                                       Localization.T("Импорт не удался: файл не читается как экспорт операций по расписанию.")))
            Catch ex As Exception
                AppFileLogger.WriteLine("Scheduled operations: import failed (" & ex.GetType().Name & ")")
                ShowScheduledNotice(Localization.T("Импорт не удался: файл не читается как экспорт операций по расписанию."))
            End Try
        End Using
    End Sub

    Private Sub OnScheduledItemChecked(sender As Object, e As ItemCheckedEventArgs)
        If _schedUpdating Then Return
        Dim id As String = Convert.ToString(e.Item.Tag)
        If id.Length = 0 Then Return
        Dim engine As ScheduledEngine = ScheduledHost.Engine
        If engine Is Nothing Then Return
        engine.SetEnabled(id, e.Item.Checked)
    End Sub

    Private Sub OnScheduledFeatureChanged(sender As Object, e As EventArgs)
        If _schedUpdating Then Return
        Dim engine As ScheduledEngine = ScheduledHost.Engine
        If engine Is Nothing Then Return
        engine.FeatureEnabled = _schedFeature.Checked
    End Sub

    ''' <summary>
    ''' The server the failed operation talks to: the registered resource behind its source or destination. The
    ''' run's own detail is a folder or a reason, never a host, so it is not used here. False when the operation
    ''' has no remote side to point a diagnosis at (two local folders, Drive, a resource since removed).
    ''' </summary>
    Private Shared Function TryFailureEndpoint(failure As ScheduledFailureInfo, ByRef host As String, ByRef port As Integer) As Boolean
        host = ""
        port = 0
        Dim engine As ScheduledEngine = ScheduledHost.Engine
        If failure Is Nothing OrElse engine Is Nothing Then Return False
        Dim op As ScheduledOperation = engine.TryGet(failure.OperationId)
        If op Is Nothing Then Return False
        For Each side As ScheduledLocation In {op.Source, op.Destination}
            If side.Kind <> ScheduledLocationKind.Resource Then Continue For
            Dim descriptor As ResourceDescriptor = ScheduledResourceRegistry.Find(side.ResourceId)
            If descriptor IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(descriptor.Host) Then
                host = descriptor.Host
                port = descriptor.Port
                Return True
            End If
        Next
        Return False
    End Function

    Private Sub OnScheduledDiagnoseClicked()
        Dim engine As ScheduledEngine = ScheduledHost.Engine
        Dim failure As ScheduledFailureInfo = If(engine IsNot Nothing, engine.LastFailure, Nothing)
        Dim host As String = ""
        Dim port As Integer = 0
        If Not TryFailureEndpoint(failure, host, port) Then Return
        Dim ctx As New ConnectionDiagnosisContext(host, port, ConnectionDiagnosisTarget.Server, ConnectionDiagnosisMessages.UnreachableServer)
        Dim ignored = Connection_Diagnosis_Dialog.RunAndShowAsync(HostDialogOwner(), ctx)
    End Sub

End Class
