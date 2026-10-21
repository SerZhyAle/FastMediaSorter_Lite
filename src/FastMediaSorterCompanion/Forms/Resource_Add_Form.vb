Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary
Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

Friend NotInheritable Class Resource_Add_Form
    Inherits Form
    Implements IThemeAware
    Private Const ContentWidth As Integer = 600
    Private ReadOnly actions As IResourceAddActions
    Private ReadOnly entry As ResourceAddEntry
    Private ReadOnly publishInitially As Boolean
    Private ReadOnly panels As New List(Of FlowLayoutPanel)
    Private ReadOnly headings As New List(Of Label)
    Private ReadOnly ownedFonts As New List(Of Font)
    Private position As ResourceAddStep
    Private picked As String = ""
    Private draft As ShareRootParams
    Private editor As Share_Root_Params_Form
    Private busy As Boolean
    Private committed As Boolean
    Private result As ResourceAddResult
    Private badge As StepBadge
    Private dots As StepDots
    Private title As Label
    Private counter As Label
    Private chosen As TextBox
    Private problem As Label
    Private facts As Label
    Private publish As CheckBox
    Private backButton As Button
    Private nextButton As Button
    Private answerRow As DialogButtonRow
    Private cancelCommand As Button
    Private pairButton As Button
    Private settingsButton As Button

    Friend Sub New(hostActions As IResourceAddActions, kind As ResourceAddEntry, Optional initialPath As String = Nothing, Optional currentPublish As Boolean = False)
        If ResourceAddModel.Steps(kind, ExchangeFirstRunModel.GateOpen()).Length = 0 Then Throw New InvalidOperationException("This entry does not offer guided resource addition.")
        actions = hostActions
        entry = kind
        publishInitially = currentPublish
        BuildUi()
        ShowStep(ResourceAddModel.Start(kind))
        If Not String.IsNullOrEmpty(initialPath) Then AcceptPick(initialPath)
    End Sub

    Private Function TextRow(panel As FlowLayoutPanel, text As String, Optional heading As Boolean = False) As Label
        Dim label As New Label With {.AutoSize = True, .Text = text, .MaximumSize = New Size(ContentWidth, 0),
            .Margin = New Padding(0, If(heading, 10, 3), 0, 2), .UseMnemonic = False}
        If heading Then
            Dim font As New Font(Me.Font, FontStyle.Bold)
            ownedFonts.Add(font) : label.Font = font : headings.Add(label)
        End If
        panel.Controls.Add(label)
        Return label
    End Function
    Private Function Command(text As String, glyph As String, act As Action) As Button
        Dim button As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .MinimumSize = New Size(120, 44), .Padding = New Padding(10, 0, 10, 0), .Text = text}
        ButtonGlyphs.Apply(button, glyph)
        AddHandler button.Click, Sub() act()
        Return button
    End Function
    Private Sub Explain(panel As FlowLayoutPanel, kind As ResourceAddStep)
        Dim paragraphs = ResourceAddText.Explanation(kind)
        Dim captions = {Localization.T("Что это"), Localization.T("Зачем"), Localization.T("Как это будет работать"), Localization.T("Где это менять")}
        For i = 0 To captions.Length - 1
            TextRow(panel, captions(i), True)
            TextRow(panel, paragraphs(i))
        Next
    End Sub
    Private Sub BuildUi()
        UiLanguage.ApplyTo(Me)
        Text = Localization.T("Добавление ресурса")
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False : MinimizeBox = False : ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(650, 720)
        Dim header As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(16, 12, 16, 6), .WrapContents = False}
        badge = New StepBadge(AddressOf CompanionAssets.OpenBundledAsset, 64)
        Dim titles As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.TopDown, .WrapContents = False}
        counter = New Label With {.AutoSize = True}
        Dim font As New Font(Me.Font.FontFamily, 14.0F, FontStyle.Bold)
        ownedFonts.Add(font)
        title = New Label With {.AutoSize = True, .Font = font, .MaximumSize = New Size(ContentWidth - 80, 0)}
        dots = New StepDots With {.Count = 4}
        titles.Controls.AddRange({counter, title, dots})
        header.Controls.AddRange({badge, titles})
        Dim body As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Padding = New Padding(16, 0, 16, 8)}
        For Each kind As ResourceAddStep In [Enum].GetValues(Of ResourceAddStep)()
            Dim panel As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Visible = False, .Location = New Point(16, 0)}
            Explain(panel, kind)
            panels.Add(panel) : body.Controls.Add(panel)
        Next
        chosen = New TextBox With {.ReadOnly = True, .AutoSize = False, .MinimumSize = New Size(0, 45), .Height = 45, .Width = ContentWidth, .AccessibleName = Localization.T("Папка")}
        panels(1).Controls.Add(chosen)
        panels(1).Controls.Add(Command(Localization.T("Выбрать папку.."), "content.folder", Sub() Pick(False)))
        If actions.CanPickDisks Then panels(1).Controls.Add(Command(Localization.T("Диски FileDO.."), "content.folder", Sub() Pick(True)))
        problem = TextRow(panels(1), "")
        facts = TextRow(panels(3), "")
        If entry = ResourceAddEntry.Exchange Then
            ' A boolean row (BOOLEAN-OPTION 2.0, ticket 243 T-11c-b2a): the check box keeps its field and default, the note under
            ' it is the row's explanation, and the row takes the width of the step (it is as tall as caption and note need).
            publish = New CheckBox With {.AutoSize = True, .Text = ExchangePublishText.PublishLabel(), .Checked = publishInitially}
            Dim publishNote As Label = TextRow(panels(3), ExchangePublishText.PublishNote())
            Dim publishRow As OptionRowHost = OptionRows.InFlow(publish, publishNote)
            publishRow.Dock = DockStyle.None
            publishRow.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            publishRow.Width = ContentWidth
            panels(3).Controls.Add(publishRow)
        End If
        pairButton = Command(Localization.T("Показать QR-код"), "action.share", Sub() actions.Pair(Me))
        settingsButton = Command(Localization.T("Настройки.."), "app.settings", Sub() actions.Settings(Me))
        pairButton.Visible = False : settingsButton.Visible = False
        panels(3).Controls.AddRange({pairButton, settingsButton})
        ' The wizard's navigation rides the shared bottom row (ticket 243 T-8b batch C, BUTTONS 2.0 sections 3 and 4): Cancel,
        ' Back, then the forward answer last - the reading order the footer already had, now with content-fit widths and
        ' whole-button wrap. The commands inside the steps (pick a folder, disks, QR, settings) stay where they are. Escape
        ' is the window's own (ProcessCmdKey); Enter is NOT bound (bindAccept:=False) because it belongs to the fields of
        ' the hosted editor - it never advanced the wizard and must not add a resource from a text field.
        cancelCommand = New Button With {.Text = Localization.T("Отмена"), .Name = "xra_Cancel"}
        AddHandler cancelCommand.Click, Sub() Close()
        backButton = New Button With {.Text = Localization.T("Назад"), .Name = "xra_Back"}
        AddHandler backButton.Click, Sub() ShowStep(CType(CInt(position) - 1, ResourceAddStep))
        nextButton = New Button With {.Text = Localization.T("Далее"), .Name = "xra_Next"}
        AddHandler nextButton.Click, Sub() OnNext()
        answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xra_Buttons", .Padding = New Padding(16, 8, 16, 12)}
        DpiLayout.AddAnswer(answerRow, cancelCommand, DialogButtonRole.Dismissing, "action.cancel")
        DpiLayout.AddAnswer(answerRow, backButton, DialogButtonRole.Neutral, "nav.back")
        DpiLayout.AddAnswer(answerRow, nextButton, DialogButtonRole.Affirmative, "nav.forward")
        Controls.Add(body) : Controls.Add(header) : Controls.Add(answerRow)
        answerRow.Bind(Me, bindAccept:=False)
        AddHandler ThemeService.Changed, AddressOf OnThemeChanged
        DpiLayout.ApplyAutoScale(Me)
        ThemeService.Attach(Me)
    End Sub
    Private Sub Pick(disk As Boolean)
        If busy OrElse committed Then Return
        Dim value = actions.Pick(Me, disk)
        If Not String.IsNullOrWhiteSpace(value) Then AcceptPick(value)
    End Sub
    Private Sub AcceptPick(value As String)
        If actions.Duplicate(value) Then
            problem.Text = Localization.T("Эта папка уже в списке.")
            Return
        End If
        actions.WarnNetwork(Me, value)
        If editor IsNot Nothing Then
            Dim oldHost As Control = panels(2).Controls(panels(2).Controls.Count - 1)
            panels(2).Controls.Remove(oldHost)
            oldHost.Dispose()
            editor.Dispose()
        End If
        If draft IsNot Nothing Then draft.AccessPin = ""
        picked = value
        chosen.Text = value
        editor = New Share_Root_Params_Form(actions.FolderName(value), actions.Parameters(value))
        Dim hosted = editor.CreateHostedEditor(ContentWidth, DeviceDpi)
        If IsHandleCreated Then hosted.Scale(New SizeF(DeviceDpi / 96.0F, DeviceDpi / 96.0F))
        panels(2).Controls.Add(hosted)
        ' The editor's boolean rows are laid out once the window that holds them has its scale (ticket 243 T-11c-b).
        If IsHandleCreated Then OptionRows.ArrangeAll(hosted)
        problem.Text = ""
        ThemeService.Attach(Me)
        RefreshNavigation()
    End Sub
    Private Sub ShowStep(kind As ResourceAddStep)
        If busy OrElse CInt(kind) < 0 OrElse CInt(kind) > 3 Then Return
        position = kind
        For i = 0 To panels.Count - 1 : panels(i).Visible = i = CInt(kind) : Next
        title.Text = ResourceAddText.Title(kind)
        counter.Text = Localization.TF("Шаг {0} из {1}", CInt(kind) + 1, 4)
        badge.GlyphId = If(committed, "status.ok", "content.folder")
        dots.Index = CInt(kind)
        If kind = ResourceAddStep.Finish AndAlso Not committed Then facts.Text = SelectionFacts()
        RefreshNavigation()
    End Sub
    Private Function SelectionFacts() As String
        If draft Is Nothing OrElse editor Is Nothing Then Return ""
        Dim name As String = If(draft.Label.Trim().Length > 0, draft.Label, actions.FolderName(picked))
        Return String.Join(Environment.NewLine, {
            Localization.TF("{0}: {1}", Localization.T("Папка"), picked),
            Localization.TF("{0}: {1}", Localization.T("Название"), name),
            Localization.TF("{0}: {1}", Localization.T("Тип"), editor.ProfileCaption)})
    End Function

    Private Sub RefreshNavigation()
        backButton.Enabled = Not busy AndAlso Not committed AndAlso position > ResourceAddStep.Introduction
        cancelCommand.Enabled = Not busy AndAlso Not committed
        cancelCommand.Visible = Not committed
        nextButton.Enabled = committed OrElse ResourceAddModel.CanAdvance(position, picked.Length > 0, busy, committed)
        nextButton.Text = Localization.T(If(committed, "Закрыть", If(position = ResourceAddStep.Finish, "Готово", "Далее")))
        ButtonGlyphs.Apply(nextButton, If(committed, "nav.close", If(position = ResourceAddStep.Finish, "action.confirm", "nav.forward")),
                           If(committed, ButtonGlyphRole.Neutral, ButtonGlyphRole.Affirmative))
        backButton.Visible = Not committed
        If publish IsNot Nothing Then publish.Enabled = Not busy AndAlso Not committed
    End Sub
    Private Async Sub OnNext()
        Await AdvanceAsync()
    End Sub
    Private Async Function AdvanceAsync() As Task
        If busy Then Return
        If committed Then Close() : Return
        If Not ResourceAddModel.CanAdvance(position, picked.Length > 0, busy, committed) Then Return
        If position = ResourceAddStep.Questions Then draft = editor.ReadDraft()
        If position <> ResourceAddStep.Finish Then
            ShowStep(CType(CInt(position) + 1, ResourceAddStep))
            Return
        End If
        If actions.Duplicate(picked) Then
            ShowStep(ResourceAddStep.Pick)
            problem.Text = Localization.T("Эта папка уже в списке.")
            Return
        End If
        busy = True : RefreshNavigation()
        Try
            result = Await actions.CommitAsync(Me, picked, draft.Clone(), entry = ResourceAddEntry.Exchange, publish IsNot Nothing AndAlso publish.Checked)
            committed = result IsNot Nothing AndAlso result.Added
            If committed Then
                For i = 0 To 5 : panels(3).Controls(i).Visible = False : Next
                badge.GlyphId = "status.ok"
            End If
            facts.Text = If(committed, SelectionFacts() & Environment.NewLine & Environment.NewLine & ResourceAddText.Finish(result), Localization.T("Не удалось добавить ресурс. Настройки не сохранены; попробуйте снова."))
            pairButton.Visible = committed AndAlso (result.Delivery = ResourceAddDelivery.Connected OrElse result.Delivery = ResourceAddDelivery.NoPhone)
            settingsButton.Visible = committed
            DialogResult = DialogResult.None
        Catch
            facts.Text = Localization.T("Не удалось добавить ресурс. Настройки не сохранены; попробуйте снова.")
        Finally
            busy = False : RefreshNavigation()
        End Try
    End Function
    Private ReadOnly Property ThemesItself As Boolean Implements IThemeAware.ThemesItself
        Get
            Return False
        End Get
    End Property
    Private Sub ApplyTheme(palette As ThemePalette) Implements IThemeAware.ApplyTheme
        For Each heading In headings : heading.ForeColor = ThemePalette.ReadableText(palette.Accent, palette.SurfaceWindow) : Next
    End Sub
    Private Sub OnThemeChanged(sender As Object, e As EventArgs)
        If Not IsDisposed Then ApplyTheme(ThemeService.Palette)
    End Sub
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' A folder picked before the window was shown has its editor's rows unarranged: they take the window's scale now.
        OptionRows.ArrangeAll(Me)
        DpiLayout.ClampToWorkingArea(Me)
    End Sub
    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If busy Then e.Cancel = True
        MyBase.OnFormClosing(e)
    End Sub
    Protected Overrides Function ProcessCmdKey(ByRef message As Message, keyData As Keys) As Boolean
        If keyData = Keys.Escape Then
            If Not busy Then Close()
            Return True
        End If
        Return MyBase.ProcessCmdKey(message, keyData)
    End Function
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then RemoveHandler ThemeService.Changed, AddressOf OnThemeChanged
        MyBase.Dispose(disposing)
        If disposing Then
            If draft IsNot Nothing Then draft.AccessPin = ""
            If editor IsNot Nothing Then editor.Dispose()
            For Each font As Font In ownedFonts : font.Dispose() : Next
            ownedFonts.Clear()
        End If
    End Sub
End Class
