Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The editor of one scheduled file operation (ticket 222, section 7.2) - a dialog of the Share Manager,
''' opened from the "Scheduled file operations" page of its settings and never from the viewer.
'''
''' What it does and does not do:
'''   * it shows the DEFINITION; what a run would do is a read-only preview (D6), never a gate;
'''   * a save goes through <see cref="ScheduledEngine.Upsert"/>, which refuses a contradictory definition
'''     with the sentences of <see cref="ScheduledText.IssueText"/> and keeps this dialog open;
'''   * for a delete, and for an archive that removes its originals, the verdict - the Recycle Bin or
'''     permanently, and why - is a read-only line (D3): the run asks nothing, so the editor says it;
'''   * nothing is written until Save.
''' </summary>
Friend NotInheritable Class Scheduled_Operation_Form
    Inherits Form

    Private ReadOnly engine As ScheduledEngine
    Private ReadOnly editing As ScheduledOperation
    Private ReadOnly toolTip As New ToolTip()

    Private txtName As TextBox
    Private cmbAction As ComboBox
    Private txtSource As TextBox
    Private btnSourcePick As Button
    Private btnSourceResource As Button
    Private btnSourceDrive As Button
    ''' <summary>The two sides as chosen: a path typed or picked, or a registered resource and a folder in it.</summary>
    Private sourceLocation As New ScheduledLocation()
    Private destinationLocation As New ScheduledLocation()
    Private btnDestinationResource As Button
    Private btnDestinationDrive As Button
    Private chkRecursive As CheckBox
    Private lblDestination As Label
    Private txtDestination As TextBox
    Private btnDestinationPick As Button
    Private lblConflict As Label
    Private cmbConflict As ComboBox
    Private chkDeleteOriginals As CheckBox
    Private lblVerdict As Label
    ' The empty cell in front of a page row that can hide. Every page control stands in an explicit cell (AddRow), so a hidden
    ' row moves nothing; its spacer hides with it only so the row collapses to no height instead of an empty label's 18 px.
    Private spacerOriginals As Label
    Private spacerVerdict As Label
    Private spacerConditions As Label

    Private btnConditions As Button
    Private pnlConditions As TableLayoutPanel
    Private chkAllFiles As CheckBox
    Private chkImages As CheckBox
    Private chkAudio As CheckBox
    Private chkVideo As CheckBox
    Private chkDocuments As CheckBox
    Private cmbTime As ComboBox
    Private txtMask As TextBox
    Private txtExclude As TextBox
    Private chkOlder As CheckBox
    Private numOlderDays As NumericUpDown
    Private numOlderHours As NumericUpDown
    Private chkYounger As CheckBox
    Private numYoungerDays As NumericUpDown
    Private numYoungerHours As NumericUpDown
    Private chkLarger As CheckBox
    Private numLargerMb As NumericUpDown
    Private chkSmaller As CheckBox
    Private numSmallerMb As NumericUpDown
    Private chkHidden As CheckBox

    Private dtpStart As DateTimePicker
    Private numEveryHours As NumericUpDown
    Private numEveryMinutes As NumericUpDown
    Private lblNextRun As Label
    Private chkEnabled As CheckBox
    Private chkRetryMissed As CheckBox
    Private chkBalloonOnError As CheckBox
    Private lblAutostart As Label

    Private btnPreview As Button
    Private btnSave As Button
    Private btnCancel As Button
    Private answerRow As DialogButtonRow
    Private _iconHandle As IntPtr
    Private _loading As Boolean
    Private _previewing As Boolean

    ''' <summary>The id the operation was saved under, or "" when the dialog was cancelled.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property SavedId As String = ""

    ''' <param name="operation">The operation to edit, or Nothing for a new one.</param>
    ''' <param name="prefillSource">A folder to start from (the viewer's launcher), for a new operation.</param>
    Friend Sub New(engine As ScheduledEngine, operation As ScheduledOperation, Optional prefillSource As String = "")
        Me.engine = engine
        editing = If(operation IsNot Nothing, operation.Clone(), NewDefinition(prefillSource))
        BuildUi()
        LoadFromOperation(editing)
    End Sub

    ''' <summary>FMS_A's defaults, kept: all files, a 24 h interval, start time now, recursive.</summary>
    Private Shared Function NewDefinition(prefillSource As String) As ScheduledOperation
        Dim now As DateTime = DateTime.Now
        Dim op As New ScheduledOperation With {
            .Action = ScheduledAction.Copy,
            .AnchorMinutes = now.Hour * 60 + now.Minute,
            .IntervalMinutes = ScheduleMath.DefaultIntervalMinutes,
            .StartDate = now.Date,
            .Enabled = True}
        If Not String.IsNullOrWhiteSpace(prefillSource) Then op.Source.Path = prefillSource
        Return op
    End Function

    ' --- build ---------------------------------------------------------------------------------------

    Private Function Caption(text As String) As Label
        Return New Label With {.Text = text, .AutoSize = True, .Margin = New Padding(0, 8, 8, 0), .Anchor = AnchorStyles.Left Or AnchorStyles.Top}
    End Function

    Private Function Note(text As String) As Label
        Return New Label With {.Text = text, .AutoSize = True, .MaximumSize = New Size(560, 0),
            .ForeColor = ThemeService.Palette.TextMuted, .Margin = New Padding(0, 6, 0, 2)}
    End Function

    Private Shared Function Number(maximum As Integer) As NumericUpDown
        Return New NumericUpDown With {.Minimum = 0, .Maximum = maximum, .Width = 72, .Margin = New Padding(0, 4, 6, 0)}
    End Function

    Private Sub BuildUi()
        SuspendLayout()
        UiLanguage.ApplyTo(Me)
        Text = Localization.T("Операция по расписанию")
        Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler FormClosed, Sub()
                                   Try : toolTip.Dispose() : Catch : End Try
                                   ShareIcons.FreeIcon(Icon, _iconHandle)
                               End Sub
        FormBorderStyle = FormBorderStyle.Sizable
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        MinimumSize = New Size(560, 420)
        ClientSize = New Size(640, 640)

        ' The answers ride the shared bottom row (ticket 243 T-8b batch C, BUTTONS 2.0 sections 3 and 4): the read-only preview
        ' is the editor's own COMMAND (first, set apart from the answers, never the default), then Cancel, then Save last.
        ' The pick buttons beside the source and destination fields and the conditions switch are field-local commands and
        ' stay where they are. Escape is Cancel. Enter saves, as before - EXCEPT while the operation deletes files (a
        ' delete, or an archive that removes its originals): then no accept button exists (UpdateVerdict takes it away) and
        ' Enter in a field saves nothing, so arming a deletion is always a deliberate click on Save.
        btnCancel = New Button With {.Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel, .Name = "btnSchedCancel"}
        btnSave = New Button With {.Text = Localization.T("Сохранить"), .Name = "btnSchedSave"}
        btnPreview = New Button With {.Text = Localization.T("Что будет выполнено сейчас.."), .Name = "btnSchedPreview"}
        AddHandler btnSave.Click, AddressOf OnSaveClicked
        AddHandler btnPreview.Click, AddressOf OnPreviewClicked
        answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "btnSchedButtons", .Padding = New Padding(12, 8, 12, 8)}
        DpiLayout.AddAnswer(answerRow, btnPreview, DialogButtonRole.Command, "action.search")
        DpiLayout.AddAnswer(answerRow, btnCancel, DialogButtonRole.Dismissing, "action.cancel")
        DpiLayout.AddAnswer(answerRow, btnSave, DialogButtonRole.Affirmative, "action.confirm")
        answerRow.Bind(Me, bindAccept:=False)
        AcceptButton = btnSave

        Dim page As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2, .Padding = New Padding(14, 10, 14, 6)}
        page.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        page.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        txtName = New TextBox With {.Dock = DockStyle.Top, .Margin = New Padding(0, 4, 0, 0), .MaxLength = 120, .Name = "txtSchedName"}
        AddRow(page, Caption(Localization.T("Название")), txtName)

        cmbAction = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 220, .Margin = New Padding(0, 4, 0, 0), .Name = "cmbSchedAction"}
        For Each action As ScheduledAction In {ScheduledAction.Copy, ScheduledAction.Move, ScheduledAction.Delete, ScheduledAction.Archive}
            cmbAction.Items.Add(ScheduledText.ActionName(action))
        Next
        AddHandler cmbAction.SelectedIndexChanged, AddressOf OnShapeChanged
        AddRow(page, Caption(Localization.T("Действие")), cmbAction)

        txtSource = New TextBox With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 6, 0), .Name = "txtSchedSource"}
        btnSourcePick = New Button With {.Text = Localization.T("Выбрать.."), .AutoSize = True, .Name = "btnSchedSourcePick", .Margin = New Padding(0, 2, 0, 0)}
        ButtonGlyphs.Apply(btnSourcePick, "action.choose-folder")
        AddHandler btnSourcePick.Click, Sub() PickFolder(txtSource, sourceLocation)
        AddHandler txtSource.TextChanged, AddressOf OnShapeChanged
        btnSourceResource = New Button With {.Text = Localization.T("Ресурс.."), .AutoSize = True, .Name = "btnSchedSourceResource", .Margin = New Padding(6, 2, 0, 0)}
        ButtonGlyphs.Apply(btnSourceResource, "source.resource")
        AddHandler btnSourceResource.Click, Sub() PickResource(txtSource, sourceLocation)
        btnSourceDrive = New Button With {.Text = Localization.T("Через Google Диск"), .AutoSize = True, .Name = "btnSchedSourceDrive", .Margin = New Padding(6, 2, 0, 0)}
        ButtonGlyphs.Apply(btnSourceDrive, "system.internet")
        AddHandler btnSourceDrive.Click, Sub() PickDrive(txtSource, sourceLocation)
        AddRow(page, Caption(Localization.T("Источник")), PairRow(txtSource, btnSourcePick, btnSourceResource, btnSourceDrive))

        chkRecursive = New CheckBox With {.Text = Localization.T("Включая вложенные папки"), .AutoSize = True, .Checked = True,
            .Margin = New Padding(0, 2, 0, 2), .Name = "chkSchedRecursive"}
        AddRow(page, New Label With {.AutoSize = True, .Margin = New Padding(0)}, OptionRows.InFlow(chkRecursive))

        lblDestination = Caption(Localization.T("Назначение"))
        txtDestination = New TextBox With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 6, 0), .Name = "txtSchedDestination"}
        btnDestinationPick = New Button With {.Text = Localization.T("Выбрать.."), .AutoSize = True, .Name = "btnSchedDestinationPick", .Margin = New Padding(0, 2, 0, 0)}
        ButtonGlyphs.Apply(btnDestinationPick, "action.choose-folder")
        AddHandler btnDestinationPick.Click, Sub() PickFolder(txtDestination, destinationLocation)
        AddHandler txtDestination.TextChanged, AddressOf OnShapeChanged
        btnDestinationResource = New Button With {.Text = Localization.T("Ресурс.."), .AutoSize = True, .Name = "btnSchedDestinationResource", .Margin = New Padding(6, 2, 0, 0)}
        ButtonGlyphs.Apply(btnDestinationResource, "source.resource")
        AddHandler btnDestinationResource.Click, Sub() PickResource(txtDestination, destinationLocation)
        btnDestinationDrive = New Button With {.Text = Localization.T("Через Google Диск"), .AutoSize = True, .Name = "btnSchedDestinationDrive", .Margin = New Padding(6, 2, 0, 0)}
        ButtonGlyphs.Apply(btnDestinationDrive, "system.internet")
        AddHandler btnDestinationDrive.Click, Sub() PickDrive(txtDestination, destinationLocation)
        AddRow(page, lblDestination, PairRow(txtDestination, btnDestinationPick, btnDestinationResource, btnDestinationDrive))

        lblConflict = Caption(Localization.T("Если имя занято"))
        cmbConflict = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 220, .Margin = New Padding(0, 4, 0, 0), .Name = "cmbSchedConflict"}
        For Each mode As ScheduledConflict In {ScheduledConflict.Skip, ScheduledConflict.Overwrite, ScheduledConflict.KeepBoth}
            cmbConflict.Items.Add(ScheduledText.ConflictName(mode))
        Next
        AddRow(page, lblConflict, cmbConflict)

        chkDeleteOriginals = New CheckBox With {.Text = Localization.T("Удалить исходные файлы после проверки архива"), .AutoSize = True,
            .Margin = New Padding(0, 4, 0, 2), .Name = "chkSchedDeleteOriginals"}
        AddHandler chkDeleteOriginals.CheckedChanged, AddressOf OnShapeChanged
        spacerOriginals = New Label With {.AutoSize = True, .Margin = New Padding(0)}
        AddRow(page, spacerOriginals, OptionRows.InFlow(chkDeleteOriginals))

        lblVerdict = New Label With {.AutoSize = True, .MaximumSize = New Size(560, 0), .Margin = New Padding(0, 4, 0, 2),
            .Name = "lblSchedVerdict", .Font = New Font(Font, FontStyle.Bold)}
        AddHandler lblVerdict.Disposed, Sub() lblVerdict.Font.Dispose()
        spacerVerdict = New Label With {.AutoSize = True, .Margin = New Padding(0)}
        AddRow(page, spacerVerdict, lblVerdict)

        ' --- conditions (collapsed by default, as in FMS_A) ---
        btnConditions = New Button With {.Text = Localization.T("Условия отбора файлов"), .AutoSize = True, .Name = "btnSchedConditions",
            .Margin = New Padding(0, 10, 0, 2), .Padding = New Padding(10, 4, 10, 4)}
        ButtonGlyphs.Apply(btnConditions, "action.filter")
        AddHandler btnConditions.Click, Sub() SetConditionsOpen(Not pnlConditions.Visible)
        AddRow(page, New Label With {.AutoSize = True, .Margin = New Padding(0)}, btnConditions)

        BuildConditions()
        spacerConditions = New Label With {.AutoSize = True, .Margin = New Padding(0), .Visible = False}
        AddRow(page, spacerConditions, pnlConditions)

        ' --- schedule ---
        dtpStart = New DateTimePicker With {.Format = DateTimePickerFormat.Custom, .CustomFormat = "HH:mm", .ShowUpDown = True,
            .Width = 90, .Margin = New Padding(0, 4, 0, 0), .Name = "dtpSchedStart"}
        AddHandler dtpStart.ValueChanged, AddressOf OnScheduleChanged
        AddRow(page, Caption(Localization.T("Начало в")), dtpStart)

        numEveryHours = Number(24 * 366)
        numEveryMinutes = Number(59)
        numEveryHours.Name = "numSchedEveryHours"
        numEveryMinutes.Name = "numSchedEveryMinutes"
        AddHandler numEveryHours.ValueChanged, AddressOf OnScheduleChanged
        AddHandler numEveryMinutes.ValueChanged, AddressOf OnScheduleChanged
        Dim every As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = False, .Margin = New Padding(0)}
        every.Controls.AddRange({numEveryHours, New Label With {.Text = Localization.T("ч"), .AutoSize = True, .Margin = New Padding(0, 8, 10, 0)},
                                 numEveryMinutes, New Label With {.Text = Localization.T("мин"), .AutoSize = True, .Margin = New Padding(0, 8, 0, 0)}})
        AddRow(page, Caption(Localization.T("Повторять каждые")), every)

        lblNextRun = New Label With {.AutoSize = True, .MaximumSize = New Size(560, 0), .Margin = New Padding(0, 6, 0, 2), .Name = "lblSchedNextRun"}
        AddRow(page, New Label With {.AutoSize = True, .Margin = New Padding(0)}, lblNextRun)

        chkEnabled = New CheckBox With {.Text = Localization.T("Выполнять по расписанию"), .AutoSize = True, .Checked = True,
            .Margin = New Padding(0, 6, 0, 2), .Name = "chkSchedEnabled"}
        AddHandler chkEnabled.CheckedChanged, AddressOf OnScheduleChanged
        AddRow(page, New Label With {.AutoSize = True, .Margin = New Padding(0)}, OptionRows.InFlow(chkEnabled))

        chkRetryMissed = New CheckBox With {.Text = Localization.T("Выполнить пропущенный запуск при старте программы"), .AutoSize = True, .Checked = True,
            .Margin = New Padding(0, 2, 0, 2), .Name = "chkSchedRetryMissed"}
        AddRow(page, New Label With {.AutoSize = True, .Margin = New Padding(0)}, OptionRows.InFlow(chkRetryMissed))

        chkBalloonOnError = New CheckBox With {.Text = Localization.T("Показывать уведомление при ошибке"), .AutoSize = True, .Checked = False,
            .Margin = New Padding(0, 2, 0, 2), .Name = "chkSchedBalloonOnError"}
        AddRow(page, New Label With {.AutoSize = True, .Margin = New Padding(0)}, OptionRows.InFlow(chkBalloonOnError))

        lblAutostart = Note("")
        lblAutostart.Name = "lblSchedAutostart"
        AddRow(page, New Label With {.AutoSize = True, .Margin = New Padding(0)}, lblAutostart)

        Dim scroller As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True}
        scroller.Controls.Add(page)
        Controls.Add(scroller)
        ' Last in the collection: the docked row takes its edge first and reads after the fields in the Tab order.
        Controls.Add(answerRow)

        TipFor(chkRecursive, Localization.T("Если выключено, обрабатываются только файлы самой папки."))
        toolTip.SetToolTip(cmbConflict, Localization.T("Что делать, если в папке назначения уже есть файл с таким именем."))
        TipFor(chkDeleteOriginals, Localization.T("Исходные файлы удаляются только после того, как архив прочитан обратно и сверен."))
        toolTip.SetToolTip(btnPreview, Localization.T("Показывает список без каких-либо изменений. Запуск не требует просмотра."))

        ResumeLayout(True)
        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' The boolean rows (ticket 243 T-11c-b) are laid out now that the window has its scale; the page makes room for the
        ' taller rows and the body scrolls.
        OptionRows.ArrangeAll(Me)
        ' The editor is authored at 640 x 640: at a high scale it must still fit the monitor (the body scrolls, the row does not).
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    ''' <summary>A hint on the check box AND on its row's caption: the pointer rests on the caption now, not on the 32 px box.</summary>
    Private Sub TipFor(check As CheckBox, text As String)
        toolTip.SetToolTip(check, text)
        Dim row As OptionRowHost = OptionRows.RowOf(check)
        If row IsNot Nothing Then toolTip.SetToolTip(row.Caption, text)
    End Sub

    ''' <summary>Opens or folds the conditions group together with the empty cell in front of it (see <see cref="spacerConditions"/>).</summary>
    Private Sub SetConditionsOpen(open As Boolean)
        pnlConditions.Visible = open
        spacerConditions.Visible = open
    End Sub

    Private Sub BuildConditions()
        pnlConditions = New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 2,
            .Visible = False, .Margin = New Padding(0, 0, 0, 6), .Name = "pnlSchedConditions"}
        pnlConditions.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        pnlConditions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        chkAllFiles = New CheckBox With {.Text = Localization.T("Все файлы"), .AutoSize = True, .Checked = True, .Name = "chkSchedAllFiles"}
        chkImages = New CheckBox With {.Text = Localization.T("Изображения"), .AutoSize = True, .Name = "chkSchedImages"}
        chkAudio = New CheckBox With {.Text = Localization.T("Аудио"), .AutoSize = True, .Name = "chkSchedAudio"}
        chkVideo = New CheckBox With {.Text = Localization.T("Видео"), .AutoSize = True, .Name = "chkSchedVideo"}
        chkDocuments = New CheckBox With {.Text = Localization.T("Документы"), .AutoSize = True, .Name = "chkSchedDocuments"}
        AddHandler chkAllFiles.CheckedChanged, AddressOf OnAllFilesChanged
        Dim types As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0)}
        types.Controls.AddRange({OptionRows.InFlow(chkAllFiles, Nothing, OptionRowFit.Content), OptionRows.InFlow(chkImages, Nothing, OptionRowFit.Content),
                              OptionRows.InFlow(chkAudio, Nothing, OptionRowFit.Content), OptionRows.InFlow(chkVideo, Nothing, OptionRowFit.Content),
                              OptionRows.InFlow(chkDocuments, Nothing, OptionRowFit.Content)})
        AddRow(pnlConditions, Caption(Localization.T("Типы файлов")), types)

        cmbTime = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 260, .Margin = New Padding(0, 4, 0, 0), .Name = "cmbSchedTime"}
        For Each filter As ScheduledTimeFilter In {ScheduledTimeFilter.All, ScheduledTimeFilter.SinceLastRun, ScheduledTimeFilter.LastHour, ScheduledTimeFilter.LastDay}
            cmbTime.Items.Add(ScheduledText.TimeFilterName(filter))
        Next
        AddRow(pnlConditions, Caption(Localization.T("Время изменения")), cmbTime)

        txtMask = New TextBox With {.Dock = DockStyle.Top, .Margin = New Padding(0, 4, 0, 0), .Name = "txtSchedMask"}
        AddRow(pnlConditions, Caption(Localization.T("Маска имени")), txtMask)
        txtExclude = New TextBox With {.Dock = DockStyle.Top, .Margin = New Padding(0, 4, 0, 0), .Name = "txtSchedExclude"}
        AddRow(pnlConditions, Caption(Localization.T("Исключить по маске")), txtExclude)
        toolTip.SetToolTip(txtMask, Localization.T("Одна или несколько масок через «;», например *.jpg;IMG_??.png. Сравнивается имя файла."))
        toolTip.SetToolTip(txtExclude, Localization.T("Файлы, подходящие под эти маски, не обрабатываются."))

        chkOlder = New CheckBox With {.Text = Localization.T("Старше"), .AutoSize = True, .Name = "chkSchedOlder"}
        numOlderDays = Number(36500) : numOlderDays.Name = "numSchedOlderDays"
        numOlderHours = Number(23) : numOlderHours.Name = "numSchedOlderHours"
        AddRow(pnlConditions, OptionRows.InFlow(chkOlder, Nothing, OptionRowFit.Content), DaysHours(numOlderDays, numOlderHours))
        chkYounger = New CheckBox With {.Text = Localization.T("Младше"), .AutoSize = True, .Name = "chkSchedYounger"}
        numYoungerDays = Number(36500) : numYoungerDays.Name = "numSchedYoungerDays"
        numYoungerHours = Number(23) : numYoungerHours.Name = "numSchedYoungerHours"
        AddRow(pnlConditions, OptionRows.InFlow(chkYounger, Nothing, OptionRowFit.Content), DaysHours(numYoungerDays, numYoungerHours))
        chkLarger = New CheckBox With {.Text = Localization.T("Размер больше"), .AutoSize = True, .Name = "chkSchedLarger"}
        numLargerMb = Number(1000000) : numLargerMb.Name = "numSchedLargerMb"
        AddRow(pnlConditions, OptionRows.InFlow(chkLarger, Nothing, OptionRowFit.Content), MegaBytes(numLargerMb))
        chkSmaller = New CheckBox With {.Text = Localization.T("Размер меньше"), .AutoSize = True, .Name = "chkSchedSmaller"}
        numSmallerMb = Number(1000000) : numSmallerMb.Name = "numSchedSmallerMb"
        AddRow(pnlConditions, OptionRows.InFlow(chkSmaller, Nothing, OptionRowFit.Content), MegaBytes(numSmallerMb))

        chkHidden = New CheckBox With {.Text = Localization.T("Включать скрытые и системные файлы"), .AutoSize = True, .Name = "chkSchedHidden"}
        AddRow(pnlConditions, New Label With {.AutoSize = True, .Margin = New Padding(0)}, OptionRows.InFlow(chkHidden))
    End Sub

    Private Shared Function DaysHours(days As NumericUpDown, hours As NumericUpDown) As Control
        Dim flow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = False, .Margin = New Padding(0, 4, 0, 0)}
        flow.Controls.AddRange({days, New Label With {.Text = Localization.T("дн."), .AutoSize = True, .Margin = New Padding(0, 8, 10, 0)},
                                hours, New Label With {.Text = Localization.T("ч"), .AutoSize = True, .Margin = New Padding(0, 8, 0, 0)}})
        Return flow
    End Function

    Private Shared Function MegaBytes(box As NumericUpDown) As Control
        Dim flow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = False, .Margin = New Padding(0, 4, 0, 0)}
        flow.Controls.AddRange({box, New Label With {.Text = Localization.T("МБ"), .AutoSize = True, .Margin = New Padding(0, 8, 0, 0)}})
        Return flow
    End Function

    ''' <summary>One row of a two-column table. Both controls stand in an EXPLICIT cell: a table that places its children itself
    ''' skips a hidden one, so a row that hides (a full-width option row and the empty cell before it) moved every later
    ''' control one cell over - and a full-width row landed in the narrow first column. Every row adds exactly two controls, so
    ''' the row number is half the count so far.</summary>
    Private Shared Sub AddRow(table As TableLayoutPanel, left As Control, right As Control)
        Dim r As Integer = table.Controls.Count \ 2
        table.Controls.Add(left, 0, r)
        table.Controls.Add(right, 1, r)
    End Sub

    Private Shared Function PairRow(box As TextBox, pick As Button, resource As Button, drive As Button) As Control
        Dim table As New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 4, .Dock = DockStyle.Top,
            .Margin = New Padding(0)}
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        table.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        table.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        table.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        table.Controls.Add(box)
        table.Controls.Add(pick)
        table.Controls.Add(resource)
        table.Controls.Add(drive)
        Return table
    End Function

    ' --- model <-> controls ----------------------------------------------------------------------------

    Private Sub LoadFromOperation(op As ScheduledOperation)
        _loading = True
        Try
            txtName.Text = op.Name
            cmbAction.SelectedIndex = CInt(op.Action)
            sourceLocation = op.Source.Clone()
            destinationLocation = op.Destination.Clone()
            ShowLocation(txtSource, sourceLocation)
            chkRecursive.Checked = op.Source.Recursive
            ShowLocation(txtDestination, destinationLocation)
            cmbConflict.SelectedIndex = CInt(op.Conflict)
            chkDeleteOriginals.Checked = op.DeleteOriginalsAfterVerify

            Dim c As ScheduledConditions = op.Conditions
            chkAllFiles.Checked = (c.TypeMask And ScheduledTypeMask.AllFiles) <> 0
            chkImages.Checked = (c.TypeMask And ScheduledTypeMask.Images) <> 0
            chkAudio.Checked = (c.TypeMask And ScheduledTypeMask.Audio) <> 0
            chkVideo.Checked = (c.TypeMask And ScheduledTypeMask.Video) <> 0
            chkDocuments.Checked = (c.TypeMask And ScheduledTypeMask.Documents) <> 0
            cmbTime.SelectedIndex = CInt(c.TimeFilter)
            txtMask.Text = c.NameMask
            txtExclude.Text = c.ExcludeMask
            SetAge(chkOlder, numOlderDays, numOlderHours, c.MinAgeHours)
            SetAge(chkYounger, numYoungerDays, numYoungerHours, c.MaxAgeHours)
            SetSize(chkLarger, numLargerMb, c.MinSizeBytes)
            SetSize(chkSmaller, numSmallerMb, c.MaxSizeBytes)
            chkHidden.Checked = c.IncludeHidden
            ' A condition that is set shows the group open; an untouched definition keeps it folded.
            SetConditionsOpen(Not IsDefaultConditions(c))

            Dim start As DateTime = DateTime.Today.AddMinutes(op.AnchorMinutes)
            dtpStart.Value = start
            numEveryHours.Value = Math.Min(numEveryHours.Maximum, op.IntervalMinutes \ 60)
            numEveryMinutes.Value = op.IntervalMinutes Mod 60
            chkEnabled.Checked = op.Enabled
            chkRetryMissed.Checked = op.RetryMissedRunsAtStart
            chkBalloonOnError.Checked = op.ShowBalloonOnError
        Finally
            _loading = False
        End Try
        OnAllFilesChanged(Nothing, EventArgs.Empty)
        OnShapeChanged(Nothing, EventArgs.Empty)
        OnScheduleChanged(Nothing, EventArgs.Empty)
        lblAutostart.Text = AutostartText()
    End Sub

    Private Shared Function IsDefaultConditions(c As ScheduledConditions) As Boolean
        Return c.TypeMask = ScheduledTypeMask.AllFiles AndAlso c.TimeFilter = ScheduledTimeFilter.All AndAlso
               c.NameMask.Length = 0 AndAlso c.ExcludeMask.Length = 0 AndAlso Not c.MinAgeHours.HasValue AndAlso
               Not c.MaxAgeHours.HasValue AndAlso Not c.MinSizeBytes.HasValue AndAlso Not c.MaxSizeBytes.HasValue AndAlso
               Not c.IncludeHidden
    End Function

    Private Shared Sub SetAge(check As CheckBox, days As NumericUpDown, hours As NumericUpDown, value As Long?)
        check.Checked = value.HasValue
        Dim total As Long = If(value, 0L)
        days.Value = Math.Min(CLng(days.Maximum), total \ 24L)
        hours.Value = CInt(total Mod 24L)
    End Sub

    Private Shared Sub SetSize(check As CheckBox, megabytes As NumericUpDown, bytes As Long?)
        check.Checked = bytes.HasValue
        megabytes.Value = Math.Min(CLng(megabytes.Maximum), If(bytes, 0L) \ (1024L * 1024L))
    End Sub

    ''' <summary>The operation the dialog describes now (not yet validated or stored).</summary>
    Private Function ReadOperation() As ScheduledOperation
        Dim op As ScheduledOperation = editing.Clone()
        op.Name = txtName.Text.Trim()
        op.Action = CType(Math.Max(0, cmbAction.SelectedIndex), ScheduledAction)
        op.Source = ReadLocation(txtSource, sourceLocation)
        op.Source.Recursive = chkRecursive.Checked
        op.Destination = If(op.NeedsDestination, ReadLocation(txtDestination, destinationLocation), New ScheduledLocation())
        op.Conflict = CType(Math.Max(0, cmbConflict.SelectedIndex), ScheduledConflict)
        op.DeleteOriginalsAfterVerify = op.Action = ScheduledAction.Archive AndAlso chkDeleteOriginals.Checked

        Dim mask As ScheduledTypeMask = ScheduledTypeMask.None
        If chkAllFiles.Checked Then
            mask = ScheduledTypeMask.AllFiles
        Else
            If chkImages.Checked Then mask = mask Or ScheduledTypeMask.Images
            If chkAudio.Checked Then mask = mask Or ScheduledTypeMask.Audio
            If chkVideo.Checked Then mask = mask Or ScheduledTypeMask.Video
            If chkDocuments.Checked Then mask = mask Or ScheduledTypeMask.Documents
        End If
        op.Conditions = New ScheduledConditions With {
            .TypeMask = mask, .TimeFilter = CType(Math.Max(0, cmbTime.SelectedIndex), ScheduledTimeFilter),
            .NameMask = txtMask.Text.Trim(), .ExcludeMask = txtExclude.Text.Trim(),
            .MinAgeHours = If(chkOlder.Checked, CLng(numOlderDays.Value) * 24L + CLng(numOlderHours.Value), CType(Nothing, Long?)),
            .MaxAgeHours = If(chkYounger.Checked, CLng(numYoungerDays.Value) * 24L + CLng(numYoungerHours.Value), CType(Nothing, Long?)),
            .MinSizeBytes = If(chkLarger.Checked, CLng(numLargerMb.Value) * 1024L * 1024L, CType(Nothing, Long?)),
            .MaxSizeBytes = If(chkSmaller.Checked, CLng(numSmallerMb.Value) * 1024L * 1024L, CType(Nothing, Long?)),
            .IncludeHidden = chkHidden.Checked}

        op.AnchorMinutes = dtpStart.Value.Hour * 60 + dtpStart.Value.Minute
        op.IntervalMinutes = CInt(numEveryHours.Value) * 60 + CInt(numEveryMinutes.Value)
        op.Enabled = chkEnabled.Checked
        op.RetryMissedRunsAtStart = chkRetryMissed.Checked
        op.ShowBalloonOnError = chkBalloonOnError.Checked
        Return op
    End Function

    ' --- reactions -------------------------------------------------------------------------------------

    Private Sub OnAllFilesChanged(sender As Object, e As EventArgs)
        ' The interlock of FMS_A: all-files switches the other four off.
        For Each box As CheckBox In {chkImages, chkAudio, chkVideo, chkDocuments}
            box.Enabled = Not chkAllFiles.Checked
            If chkAllFiles.Checked Then box.Checked = False
        Next
    End Sub

    ''' <summary>Which rows exist depends on the action: a delete has no destination, only a copy or a move
    ''' has a conflict mode, only an archive offers to remove its originals.</summary>
    Private Sub OnShapeChanged(sender As Object, e As EventArgs)
        If cmbAction.SelectedIndex < 0 Then Return
        Dim action As ScheduledAction = CType(cmbAction.SelectedIndex, ScheduledAction)
        Dim hasDestination As Boolean = action <> ScheduledAction.Delete
        lblDestination.Visible = hasDestination
        txtDestination.Parent.Visible = hasDestination
        ' A control's own Visible getter answers for its parents too (False while the window is not shown), so the
        ' value is decided once, here, and assigned to both - never read back from the label.
        Dim showConflict As Boolean = action = ScheduledAction.Copy OrElse action = ScheduledAction.Move
        lblConflict.Visible = showConflict
        cmbConflict.Visible = showConflict
        ' The row (box, caption) is what stands in the page now, so the row is what hides - the check box inside it hiding
        ' alone would leave the caption behind.
        Dim showOriginals As Boolean = action = ScheduledAction.Archive
        OptionRows.RowOf(chkDeleteOriginals).Visible = showOriginals
        spacerOriginals.Visible = showOriginals
        ' An archive takes folders of this PC (or Google Drive) only for now (12.2 default 7).
        btnSourceResource.Visible = action <> ScheduledAction.Archive
        btnDestinationResource.Visible = action <> ScheduledAction.Archive
        Dim driveOffered As Boolean = ScheduledDriveRegistry.IsSignedIn()
        btnSourceDrive.Visible = (driveOffered OrElse sourceLocation.Kind = ScheduledLocationKind.Drive) AndAlso action <> ScheduledAction.Delete
        btnDestinationDrive.Visible = (driveOffered OrElse destinationLocation.Kind = ScheduledLocationKind.Drive) AndAlso hasDestination
        UpdateVerdict(action)
    End Sub

    ''' <summary>The read-only verdict line (D3): shown only when something is going to be deleted.</summary>
    Private Sub UpdateVerdict(action As ScheduledAction)
        Dim removes As Boolean = action = ScheduledAction.Delete OrElse (action = ScheduledAction.Archive AndAlso chkDeleteOriginals.Checked)
        ' The safe default while files are going to be removed: Enter does not save (see BuildUi).
        AcceptButton = If(removes, Nothing, btnSave)
        lblVerdict.Visible = removes
        spacerVerdict.Visible = removes
        If Not removes Then Return
        Dim location As ScheduledLocation = ReadLocation(txtSource, sourceLocation)
        Dim canonical As String = If(location.Kind = ScheduledLocationKind.Resource, location.ResourceId,
                                     If(location.Kind = ScheduledLocationKind.Drive, "drive", CompanionCommand.CanonicalFolder(location.Path)))
        If canonical.Length = 0 Then
            lblVerdict.Text = Localization.T("Удаление: куда пойдут файлы, станет известно, когда будет указан источник.")
            Return
        End If
        Dim decision As DeleteDecision = engine.RunExecutor.DeleteVerdictFor(location, 0)
        lblVerdict.Text = Localization.TF("Удаление: {0}", ScheduledText.VerdictText(decision))
    End Sub

    Private Sub OnScheduleChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        Dim op As ScheduledOperation = ReadOperation()
        op.IntervalMinutes = ScheduleMath.NormalizeInterval(op.IntervalMinutes)
        Dim slot As DateTime = ScheduleMath.NextSlot(op.StartDate, op.AnchorMinutes, op.IntervalMinutes, DateTime.Now)
        ' The same function the engine arms from: what is shown here is what will be armed.
        If Not op.Enabled Then
            lblNextRun.Text = Localization.T("Расписание выключено: операция выполняется только вручную.")
        Else
            lblNextRun.Text = Localization.TF("Следующий запуск: {0} ({1})", slot.ToString("g", CultureInfo.CurrentCulture),
                                              ScheduledText.ScheduleText(op))
        End If
    End Sub

    ''' <summary>The autostart setting's state and the warning that goes with it (D2): the schedule works
    ''' only while the Share Manager runs, and nothing here switches the setting.</summary>
    Private Shared Function AutostartText() As String
        Dim enabled As Boolean = False
        Try
            Dim settings As New ShareSettings()
            settings.Load()
            enabled = settings.AutostartEnabled
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: reading the autostart setting", ex)
        End Try
        Return ScheduledText.AutostartLine(enabled, ScheduledSettings.Paused)
    End Function

    ''' <summary>The text box shows a path, or - for a resource or Google Drive - its name and folder, read-only.</summary>
    Private Shared Sub ShowLocation(box As TextBox, location As ScheduledLocation)
        If location.Kind = ScheduledLocationKind.Resource Then
            box.Text = location.Describe()
            box.ReadOnly = True
        ElseIf location.Kind = ScheduledLocationKind.Drive Then
            box.Text = If(location.ResourceName.Length > 0, location.ResourceName, Localization.T("Через Google Диск"))
            box.ReadOnly = True
        Else
            box.Text = location.Path
            box.ReadOnly = False
        End If
    End Sub

    ''' <summary>What the box means now: the picked resource or Google Drive, or the path typed into it.</summary>
    Private Shared Function ReadLocation(box As TextBox, location As ScheduledLocation) As ScheduledLocation
        If location.Kind = ScheduledLocationKind.Resource OrElse location.Kind = ScheduledLocationKind.Drive Then Return location.Clone()
        Return New ScheduledLocation With {.Kind = ScheduledLocationKind.Path, .Path = box.Text.Trim()}
    End Function

    Private Sub PickFolder(target As TextBox, location As ScheduledLocation)
        Using dialog As New FolderBrowserDialog()
            If location.Kind = ScheduledLocationKind.Path AndAlso target.Text.Trim().Length > 0 AndAlso Directory.Exists(target.Text.Trim()) Then
                dialog.SelectedPath = target.Text.Trim()
            End If
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            ' A folder of this PC replaces a resource that was chosen before.
            location.Kind = ScheduledLocationKind.Path
            location.ResourceId = ""
            location.ResourceName = ""
            location.Path = dialog.SelectedPath
            ShowLocation(target, location)
        End Using
    End Sub

    Private Sub PickResource(target As TextBox, location As ScheduledLocation)
        Using dialog As New Scheduled_Resource_Picker_Form(location)
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            location.Kind = ScheduledLocationKind.Resource
            location.ResourceId = dialog.ChosenId
            location.ResourceName = dialog.ChosenName
            location.Path = dialog.ChosenFolder
            ShowLocation(target, location)
        End Using
    End Sub

    Private Sub PickDrive(target As TextBox, location As ScheduledLocation)
        location.Kind = ScheduledLocationKind.Drive
        location.ResourceId = ""
        location.ResourceName = Localization.T("Через Google Диск")
        location.Path = ""
        ShowLocation(target, location)
        OnShapeChanged(Nothing, EventArgs.Empty)
    End Sub

    ' --- save and preview ------------------------------------------------------------------------------

    Private Sub OnSaveClicked(sender As Object, e As EventArgs)
        Dim op As ScheduledOperation = ReadOperation()
        Dim storedId As String = ""
        Dim issues As List(Of ScheduledIssue) = engine.Upsert(op, storedId)
        If issues.Count > 0 Then
            MessageBox.Show(Me, String.Join(vbCrLf, issues.Select(Function(i) ScheduledText.IssueText(i))),
                            Localization.T("Операция по расписанию"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        SavedId = storedId
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Async Sub OnPreviewClicked(sender As Object, e As EventArgs)
        If _previewing Then Return
        _previewing = True
        btnPreview.Enabled = False
        Try
            Dim op As ScheduledOperation = ReadOperation()
            Dim result As ScheduledPreview = Await Task.Run(Function() engine.RunExecutor.Preview(op, CancellationToken.None))
            If IsDisposed Then Return
            ShowPreview(result)
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: preview", ex)
        Finally
            _previewing = False
            If Not IsDisposed Then btnPreview.Enabled = True
        End Try
    End Sub

    Private Sub ShowPreview(result As ScheduledPreview)
        If result.Issues.Count > 0 Then
            MessageBox.Show(Me, String.Join(vbCrLf, result.Issues.Select(Function(i) ScheduledText.IssueText(i))),
                            Localization.T("Что будет выполнено сейчас"), MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        If result.Problem.IsProblem Then
            MessageBox.Show(Me, Localization.TF("Папка недоступна: {0}", result.Problem.Detail),
                            Localization.T("Что будет выполнено сейчас"), MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Using dialog As New Scheduled_Preview_Form(result)
            dialog.ShowDialog(Me)
        End Using
    End Sub

End Class

''' <summary>The read-only "what would run now" list of the editor (D6): a capped listing and a summary line.</summary>
Friend NotInheritable Class Scheduled_Preview_Form
    Inherits Form

    Private _iconHandle As IntPtr

    Friend Sub New(result As ScheduledPreview)
        SuspendLayout()
        UiLanguage.ApplyTo(Me)
        Text = Localization.T("Что будет выполнено сейчас")
        Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler FormClosed, Sub() ShareIcons.FreeIcon(Icon, _iconHandle)
        FormBorderStyle = FormBorderStyle.Sizable
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(560, 420)

        Dim summary As New Label With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(12, 10, 12, 6),
            .Text = Localization.TF("Будет обработано файлов: {0}; будет пропущено: {1}; всего {2} МБ.",
                                    result.WouldAct, result.WouldSkip, CInt(result.Bytes \ (1024L * 1024L)))}
        Dim list As New ListBox With {.Dock = DockStyle.Fill, .IntegralHeight = False, .HorizontalScrollbar = True,
            .Name = "lstSchedPreview", .AccessibleName = Localization.T("Что будет выполнено сейчас")}
        list.Items.AddRange(result.Lines.Cast(Of Object)().ToArray())
        ' The one answer rides the shared bottom row (ticket 243 T-8b batch C); the "first N shown" line is not an answer and
        ' sits above it. Escape and Enter both close, as before - the window changes nothing.
        Dim close As New Button With {.Text = Localization.T("Закрыть"), .DialogResult = DialogResult.Cancel, .Name = "btnSchedPreviewClose"}
        Dim row As New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "btnSchedPreviewButtons", .Padding = New Padding(12, 8, 12, 8)}
        DpiLayout.AddAnswer(row, close, DialogButtonRole.Dismissing, "action.cancel")
        Controls.Add(list)
        Controls.Add(summary)
        If result.Truncated Then
            Controls.Add(New Label With {.Dock = DockStyle.Bottom, .AutoSize = True, .Padding = New Padding(12, 6, 12, 0),
                .Name = "lblSchedPreviewTruncated", .Text = Localization.TF("Показаны первые {0}.", ScheduledExecutor.PreviewLineCap)})
        End If
        Controls.Add(row)
        row.Bind(Me)
        ResumeLayout(True)
        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

End Class
