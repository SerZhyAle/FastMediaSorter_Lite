Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Windows.Forms

''' <summary>
''' The "Scheduled file operations" page of the Share Manager's settings (ticket 222, section 7.1). This is
''' where the feature is MANAGED - the list, the commands, the machine switches, the history; the viewer has
''' one launcher entry and nothing else. The page exists in every Share Manager (it is not behind the Share
''' opt-in: it opens no port and needs no elevation), and says once, in words, what it is.
'''
''' It holds no state of its own: every row is read from <see cref="ScheduledHost.Engine"/> and every
''' command goes to it, so the page, the tray menu and a second window can never disagree.
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    Private _secScheduled As CollapsibleSection
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
    ''' <summary>A folder the viewer's launcher asked to start an operation from; used once, when the window is up.</summary>
    Private _schedPrefill As String = ""
    Private _schedOpenEditor As Boolean

    ''' <summary>The window's sections with this page before Help (and after the exchange account group).</summary>
    Private Function CreateScheduledSection() As CollapsibleSection
        Dim sec As CollapsibleSection = CreateSettingsSection("scheduled", ScheduledPageCaption())
        _secScheduled = sec
        BuildScheduledSection(sec)
        Return sec
    End Function

    Friend Shared Function ScheduledPageCaption() As String
        Return Localization.T("Файловые операции по расписанию")
    End Function

    Private Sub BuildScheduledSection(sec As CollapsibleSection)
        ' The statement of section 0, once: what this is and where it lives. The sentence every surface repeats.
        _schedNote = NoteLabel(Localization.T("Файловые операции по расписанию - часть менеджера общего доступа: устанавливаются вместе с ним, выполняются им и настраиваются в его настройках. Расписание работает только пока менеджер запущен."))
        _schedNote.Name = "managerSchedNote"
        sec.AddBodyRow(_schedNote)

        _schedList = New ListView With {.View = View.Details, .FullRowSelect = True, .HideSelection = False, .MultiSelect = False,
            .CheckBoxes = True, .Height = 220, .Dock = DockStyle.Top, .Margin = New Padding(0, 4, 0, 6), .Name = "managerSchedList",
            .AccessibleName = ScheduledPageCaption()}
        AddScheduledColumns()
        AddHandler _schedList.SelectedIndexChanged, Sub() RefreshScheduledButtons()
        AddHandler _schedList.ItemChecked, AddressOf OnScheduledItemChecked
        AddHandler _schedList.DoubleClick, Sub() OnScheduledEdit()
        sec.AddBodyRow(_schedList)

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
        sec.AddBodyRow(commands)

        _schedFailure = New Label With {.AutoSize = True, .MaximumSize = New Size(HostingContentWidth, 0), .Margin = New Padding(0, 0, 0, 8),
            .Name = "managerSchedFailure", .Visible = False}
        sec.AddBodyRow(_schedFailure)
        _schedDiagnose = SchedButton("managerSchedDiagnose", ConnectionDiagnosisLocalization.DoorButtonText(Localization.CurrentCode), "action.test-connection", AddressOf OnScheduledDiagnoseClicked)
        _schedDiagnose.Visible = False
        sec.AddBodyRow(_schedDiagnose)

        ' --- this PC ---
        _schedFeature = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 6, 0, 2), .Name = "managerSchedFeature",
            .Text = Localization.T("Выполнять операции по расписанию на этом компьютере")}
        AddHandler _schedFeature.CheckedChanged, AddressOf OnScheduledFeatureChanged
        sec.AddBodyRow(_schedFeature)

        _schedAutostart = NoteLabel("")
        _schedAutostart.Name = "managerSchedAutostart"
        sec.AddBodyRow(_schedAutostart)
        _schedAutostartLink = New LinkLabel With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 6), .Name = "managerSchedAutostartLink",
            .Text = Localization.T("Открыть настройку автозапуска")}
        AddHandler _schedAutostartLink.LinkClicked, Sub() SelectSettingsPage("startup")
        sec.AddBodyRow(_schedAutostartLink)

        ApplyScheduledState()
        If ScheduledHost.Engine IsNot Nothing Then
            AddHandler ScheduledHost.Engine.Changed, AddressOf OnScheduledEngineChanged
            AddHandler Disposed, Sub() RemoveHandler ScheduledHost.Engine.Changed, AddressOf OnScheduledEngineChanged
        End If
    End Sub

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

    ''' <summary>Fills the page from the engine: the list, the buttons, the machine switches.</summary>
    Private Sub ApplyScheduledState()
        If _schedList Is Nothing Then Return
        Dim engine As ScheduledEngine = ScheduledHost.Engine
        _schedUpdating = True
        Try
            Dim selected As String = SelectedScheduledId()
            _schedList.BeginUpdate()
            _schedList.Items.Clear()
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
            _schedList.EndUpdate()

            _schedFeature.Checked = engine.FeatureEnabled
            _schedPause.Text = If(engine.Paused, Localization.T("Возобновить все"), Localization.T("Приостановить все"))
            ButtonGlyphs.Apply(_schedPause, If(engine.Paused, "media.play", "media.pause"))

            Dim failure As ScheduledFailureInfo = engine.LastFailure
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
                autostart = _settings.AutostartEnabled
            Catch
            End Try
            _schedAutostart.Text = ScheduledText.AutostartLine(autostart, engine.Paused)
        Finally
            _schedUpdating = False
        End Try
        RefreshScheduledButtons()
        If _secScheduled IsNot Nothing Then
            Dim count As Integer = _schedList.Items.Count
            _secScheduled.Summary = If(count = 0, Localization.T("Операций нет"), Localization.TF("Операций: {0}", count))
            _secScheduled.SummaryColor = ThemeService.Palette.TextMuted
        End If
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

    ''' <summary>The language changed: the captions of the page's own controls and its columns.</summary>
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
        _schedFeature.Text = Localization.T("Выполнять операции по расписанию на этом компьютере")
        _schedAutostartLink.Text = Localization.T("Открыть настройку автозапуска")
        _schedList.AccessibleName = ScheduledPageCaption()
        ApplyScheduledState()
    End Sub

    ' --- commands --------------------------------------------------------------------------------------

    Private Sub OnScheduledNew()
        OpenScheduledEditor(Nothing, "")
    End Sub

    Private Sub OnScheduledEdit()
        Dim id As String = SelectedScheduledId()
        If id.Length = 0 Then Return
        Dim op As ScheduledOperation = ScheduledHost.Engine.TryGet(id)
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
            dialog.ShowDialog(Me)
        End Using
        ApplyScheduledState()
    End Sub

    Private Sub OnScheduledDelete()
        Dim id As String = SelectedScheduledId()
        If id.Length = 0 Then Return
        Dim op As ScheduledOperation = ScheduledHost.Engine.TryGet(id)
        If op Is Nothing Then Return
        If ManagerBox.Show(Me, Localization.TF("Удалить операцию «{0}»? Файлы она не затрагивает.", op.DisplayName()),
                           ScheduledPageCaption(), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                           MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        ScheduledHost.Engine.Delete(id)
    End Sub

    Private Sub OnScheduledRunNow()
        Dim id As String = SelectedScheduledId()
        If id.Length = 0 Then Return
        Select Case ScheduledHost.Engine.RunNow(id)
            Case TriggerOutcome.AlreadyRunning
                ShowScheduledNotice(Localization.T("Эта операция уже выполняется или ждёт очереди."))
            Case TriggerOutcome.NotFound
                ApplyScheduledState()
        End Select
    End Sub

    Private Sub OnScheduledRunAll()
        Select Case ScheduledHost.Engine.RunAllNow()
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
        engine.Paused = Not engine.Paused
        ApplyScheduledState()
    End Sub

    Private Sub OnScheduledHistory()
        Using dialog As New Scheduled_History_Form(ScheduledHost.Engine)
            dialog.ShowDialog(Me)
        End Using
    End Sub

    Private Sub OnScheduledClearHistory()
        If ManagerBox.Show(Me, Localization.T("Очистить журнал операций по расписанию? Сами операции и их расписание не изменятся."),
                           ScheduledPageCaption(), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                           MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        ScheduledHost.Engine.Journal.ClearHistory()
    End Sub

    Private Sub OnScheduledExport()
        Using dialog As New SaveFileDialog() With {
            .Title = Localization.T("Экспорт операций по расписанию"),
            .Filter = "JSON (*.json)|*.json",
            .DefaultExt = "json",
            .FileName = "scheduled-operations.json"}
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
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
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
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
        ScheduledHost.Engine.SetEnabled(id, e.Item.Checked)
    End Sub

    Private Sub OnScheduledFeatureChanged(sender As Object, e As EventArgs)
        If _schedUpdating Then Return
        ScheduledHost.Engine.FeatureEnabled = _schedFeature.Checked
    End Sub

    ' --- entry from the outside ------------------------------------------------------------------------

    ''' <summary>The tray menu and the viewer's launcher open this page. A folder starts a new operation
    ''' with that source in the editor (7.4), once the window is up.</summary>
    Friend Sub FocusScheduledPage(Optional folder As String = "")
        SelectSettingsPage("scheduled")
        _schedPrefill = If(folder, "")
        _schedOpenEditor = _schedPrefill.Length > 0
        ' Already on screen (a second request while the window is open): there is no OnShown to wait for.
        If _schedOpenEditor AndAlso settingsWasPresented AndAlso IsHandleCreated Then HandleScheduledPrefill()
    End Sub

    ''' <summary>Called once the window is shown (from its one OnShown): the editor the launcher asked for.</summary>
    Private Sub HandleScheduledPrefill()
        If Not _schedOpenEditor Then Return
        _schedOpenEditor = False
        Dim folder As String = _schedPrefill
        _schedPrefill = ""
        BeginInvoke(New Action(Sub()
                                   If IsDisposed OrElse Disposing Then Return
                                   OpenScheduledEditor(Nothing, folder)
                               End Sub))
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
        Dim ignored = Connection_Diagnosis_Dialog.RunAndShowAsync(Me, ctx)
    End Sub

End Class
