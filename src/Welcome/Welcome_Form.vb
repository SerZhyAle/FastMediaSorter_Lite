#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

' <summary>
' The welcome wizard window (spec 184 T-1, T-2): six steps, one screen each. Every screen has the four named
' parts the owner asked for - what it is, why it exists, how the viewer will behave, and where to change it
' later (spec 211 §4.1) - and the controls that step owns.
'
' The window edits DRAFTS only. A step's draft reaches the viewer on Next (spec 184 rule 2: each step applies through
' the viewer's own settings block, flushed at once). Skip jumps to the finish screen and applies nothing more. Esc and
' Finish keep whatever was applied. The language is the one value shown live: picking it re-renders the wizard in that
' language as a preview, and only Next makes it the viewer's language.
'
' All six screens are built once, in the constructor, so the DPI scaling of <see cref="DialogLayout.ApplyAutoScale"/>
' reaches every control; a step change only shows another panel and refreshes the texts. Which value is applied and
' when lives in <see cref="WelcomeRules"/> and in the host (Main_Form.Welcome.vb); this file lays the screens out.
' </summary>
Friend NotInheritable Class Welcome_Form
    Inherits Form

    ' Native ComboBox.GetPreferredSize uses font height even with owner-drawn tall items.
    ' FlowLayoutPanel needs the real target height or it clips the selection under the footer.
    Private NotInheritable Class WelcomeComboBox
        Inherits ComboBox

        Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
            Dim preferred As Size = MyBase.GetPreferredSize(proposedSize)
            Return New Size(preferred.Width, Math.Max(preferred.Height, ItemHeight + LogicalToDeviceUnits(6)))
        End Function
    End Class

    ''' <summary>The width of the text column, in 96-dpi pixels; every paragraph wraps inside it.</summary>
    Private Const ContentWidth As Integer = 520

    ''' <summary>Spec 184 rule 6: the 44 px pointer target for every button and every checkbox row.</summary>
    Private Const TargetHeight As Integer = 44

    ''' <summary>The step badge's diameter in 96-dpi pixels.</summary>
    Private Const BadgeSize As Integer = 64

    Private ReadOnly host As Main_Form
    Private ReadOnly steps As IReadOnlyList(Of WelcomeStep)
    Private ReadOnly finishIndex As Integer
    Private ReadOnly baseFont As Font
    Private script_Font As Font
    Private bold_Font As Font
    ' WinForms may keep a scaled child font after its parent's font changes. Own every
    ' script font until the controls are disposed; cache by family so switching is bounded.
    Private ReadOnly scriptFonts As New Dictionary(Of String, Font)(StringComparer.Ordinal)
    Private ReadOnly headingFonts As New Dictionary(Of String, Font)(StringComparer.Ordinal)
    Private ReadOnly titleFonts As New Dictionary(Of String, Font)(StringComparer.Ordinal)

    ' The language the viewer runs in (committed) and the one the wizard shows (a preview until Next).
    Private appliedLanguage As String
    Private draftLanguage As String

    ' The values as they were when the wizard opened, so that only what the user changed is applied.
    Private openedScheme As Integer
    Private openedWheelZooms As Boolean
    Private openedKeyOne As String
    Private openedDeleteBin As Boolean

    Private draftScheme As Integer
    Private draftWheelZooms As Boolean
    Private draftKeyOne As String
    Private draftDeleteBin As Boolean
    Private draftAssociations As Boolean

    Private position As Integer

    ' Every text on the window, as a function of the current draft and language. RetextAll runs them all.
    Private ReadOnly localizers As New List(Of (StepIndex As Integer, Apply As Action))()
    Private localizerStep As Integer = -1
    Private ReadOnly panels As New List(Of FlowLayoutPanel)()
    Private ReadOnly headings As New List(Of Label)()

    ''' <summary>One row of the finish screen's installed-servers block (spec 215): built once for each server the
    ''' block can name, shown or hidden by what <see cref="RefreshServers"/> reads - so the DPI scaling of the
    ''' constructor reaches every control, as it does for the six screens.</summary>
    Private NotInheritable Class ServerRowView
        Friend ReadOnly Server As WelcomeServer
        Friend ReadOnly Container As New FlowLayoutPanel()
        Friend ReadOnly NameLabel As New Label()
        Friend ReadOnly WhatLabel As New Label()
        Friend ReadOnly Dot As New Label()
        Friend ReadOnly Word As New Label()
        Friend ReadOnly ActButton As New Button()
        Friend Pill As ServerPill = ServerPill.Absent
        Friend Act As ServerAct = ServerAct.OpenManager

        Friend Sub New(forServer As WelcomeServer)
            Server = forServer
        End Sub
    End Class

    ''' <summary>The pill's dot: decoration only - the word beside it is the meaning (spec 215 section 5).</summary>
    Private Const PillDot As String = "●"

    Private ReadOnly serverRows As New List(Of ServerRowView)()
    Private serversBlock As FlowLayoutPanel

    Private ReadOnly stepLabel As New Label()
    Private keyOneValueLabel As Label
    Private ReadOnly dots As New StepDots()
    Private ReadOnly badge As New StepBadge(AddressOf RuntimeBootstrap.OpenBundledAsset, BadgeSize)
    Private ReadOnly titleLabel As New Label()
    Private ReadOnly body As New TableLayoutPanel()
    Private ReadOnly skipButton As New Button()
    Private ReadOnly backButton As New Button()
    Private ReadOnly nextButton As New Button()

    ''' <summary>Opens the wizard on the viewer's current values: they are the code defaults on a fresh profile.</summary>
    Friend Sub New(owner As Main_Form, isPackaged As Boolean)
        host = owner
        steps = WelcomeRules.StepsFor(isPackaged)
        finishIndex = WelcomeRules.FinishIndex(steps)
        baseFont = DirectCast(Me.Font.Clone(), Font)

        appliedLanguage = Localization.CurrentCode
        draftLanguage = appliedLanguage
        openedScheme = Form_Color_Scheme
        draftScheme = Form_Color_Scheme
        openedWheelZooms = Zoom_Wheel_Zooms
        draftWheelZooms = Zoom_Wheel_Zooms
        openedKeyOne = If(Hardkeys_to_move_mediafile(1), "")
        draftKeyOne = openedKeyOne
        ' Spec 211 §4.2: the question opens on the value the viewer holds now - the code default (True) on a fresh
        ' profile. Walking through with no touch applies nothing (spec 211 rule 4).
        openedDeleteBin = host.GetModernPreferences().DeleteToRecycleBin
        draftDeleteBin = openedDeleteBin
        draftAssociations = False

        BuildWindow()
        For Each wizardStep As WelcomeStep In steps
            BuildPanel(wizardStep)
        Next
        body.RowCount = panels.Count
        ApplyScript()
        ShowStep(0)
        AddHandler ThemeService.Changed, AddressOf OnThemeChanged

        ' LAST statement, as in Ask_Dialog: scaling touches only the children that exist now, and all of them do.
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    ' ---------------------------------------------------------------- the frame ----

    Private Sub BuildWindow()
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        AutoScroll = True
        KeyPreview = False

        stepLabel.AutoSize = True
        stepLabel.ForeColor = SystemColors.GrayText
        stepLabel.Margin = New Padding(0, 0, 0, 2)

        dots.Count = steps.Count

        titleLabel.AutoSize = True
        titleLabel.MaximumSize = New Size(ContentWidth - BadgeSize - 16, 0)
        titleLabel.Margin = New Padding(0)

        ' The header: the step's round badge beside "Step N of M" and the step's title.
        Dim titleColumn As New FlowLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False,
            .Anchor = AnchorStyles.Left,
            .Margin = Padding.Empty}
        titleColumn.Controls.Add(stepLabel)
        titleColumn.Controls.Add(titleLabel)

        Dim header As New TableLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2,
            .Margin = New Padding(0, 0, 0, 12)}
        header.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        header.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        badge.Margin = New Padding(0, 0, 16, 0)
        header.Controls.Add(badge, 0, 0)
        header.Controls.Add(titleColumn, 1, 0)

        body.AutoSize = True
        body.AutoSizeMode = AutoSizeMode.GrowAndShrink
        body.ColumnCount = 1
        body.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        body.Margin = New Padding(0)

        Dim root As New TableLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 1,
            .Padding = New Padding(16),
            .Margin = New Padding(0)}
        root.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        root.Controls.Add(header, 0, 0)
        root.Controls.Add(dots, 0, 1)
        root.Controls.Add(body, 0, 2)
        root.Controls.Add(BuildFooter(), 0, 3)
        Controls.Add(root)

        Localize(Sub() Me.Text = Localization.T("Добро пожаловать"))
        Localize(Sub() stepLabel.Text = Localization.TF("Шаг {0} из {1}", position + 1, steps.Count))
        Localize(Sub() dots.Index = position)
        Localize(Sub() badge.GlyphId = StepGlyph())
        Localize(Sub() titleLabel.Text = TitleText())
        Localize(Sub() skipButton.Text = Localization.T("Пропустить настройку"))
        Localize(Sub() backButton.Text = Localization.T("Назад"))
        Localize(Sub() nextButton.Text = If(position = finishIndex, Localization.T("Готово"), Localization.T("Далее")))

        ' A glyph before each caption (WINDOWS-UI 0.4 section 11): the answers are hued, navigation is mono.
        ' Next and Done are one button whose caption flips on the last step, so its glyph flips with it.
        ButtonGlyphs.Apply(skipButton, "media.next", ButtonGlyphRole.Caution)
        ButtonGlyphs.Apply(backButton, "nav.back")
        Localize(Sub()
                     Dim wanted As String = If(position = finishIndex, "action.confirm", "nav.forward")
                     If Not String.Equals(ButtonGlyphs.GlyphOf(nextButton), wanted, StringComparison.Ordinal) Then
                         ButtonGlyphs.Apply(nextButton, wanted, ButtonGlyphRole.Affirmative)
                     End If
                 End Sub)

        AcceptButton = nextButton
    End Sub

    Private Function BuildFooter() As TableLayoutPanel
        Dim footer As New TableLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 3,
            .Margin = New Padding(0, 12, 0, 0)}
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        StyleButton(skipButton)
        AddHandler skipButton.Click, AddressOf SkipToFinish
        StyleButton(backButton)
        AddHandler backButton.Click, AddressOf GoBack
        StyleButton(nextButton)
        AddHandler nextButton.Click, AddressOf GoNext

        Dim navigation As New FlowLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .Margin = Padding.Empty}
        navigation.Controls.Add(backButton)
        navigation.Controls.Add(nextButton)

        footer.Controls.Add(skipButton, 0, 0)
        footer.Controls.Add(navigation, 2, 0)
        Return footer
    End Function

    Private Shared Sub StyleButton(button As Button)
        button.AutoSize = True
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink
        button.MinimumSize = New Size(120, TargetHeight)
        button.Padding = New Padding(10, 0, 10, 0)
        button.Margin = New Padding(8, 0, 0, 0)
    End Sub

    ' ---------------------------------------------------------------- the panels ----

    Private Sub BuildPanel(wizardStep As WelcomeStep)
        localizerStep = panels.Count
        Dim panel As New FlowLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False,
            .Margin = New Padding(0)}
        Select Case wizardStep
            Case WelcomeStep.Language : BuildLanguagePanel(panel)
            Case WelcomeStep.Appearance : BuildAppearancePanel(panel)
            Case WelcomeStep.Sorting : BuildSortingPanel(panel)
            Case WelcomeStep.Delete : BuildDeletePanel(panel)
            Case WelcomeStep.Associations : BuildAssociationsPanel(panel)
            Case WelcomeStep.Finish : BuildFinishPanel(panel)
        End Select
        panels.Add(panel)
        body.Controls.Add(panel, 0, panels.Count - 1)
        localizerStep = -1
    End Sub

    ''' <summary>The four named parts of a step, in the order the owner asked for: what, why, how - and where to change it later (spec 211 §4.1).</summary>
    Private Sub AddWhatWhyHowWhere(panel As FlowLayoutPanel, what As Func(Of String), why As Func(Of String), how As Func(Of String), where As Func(Of String))
        AddPart(panel, Function() Localization.T("Что это"), what)
        AddPart(panel, Function() Localization.T("Зачем"), why)
        AddPart(panel, Function() Localization.T("Как будет работать"), how)
        AddPart(panel, Function() Localization.T("Где это менять"), where)
    End Sub

    ''' <summary>
    ''' The settings path a step's where-part names: the settings window's own name, then page, group and row - each
    ''' the caption the window really renders (spec 211 §4.1). The row ids and their captions are paired in
    ''' <see cref="WelcomeRules"/>; the pairing test holds them against the placement catalog. Right-to-left languages
    ''' read the path the other way, so the separator points the way they read.
    ''' </summary>
    Private Function WherePath(rowId As String) As String
        Dim keys As String() = WelcomeRules.WherePathKeys(rowId)
        If keys.Length = 0 Then Return String.Empty
        Dim parts(keys.Length) As String
        parts(0) = Localization.T("Настройки")
        For i As Integer = 0 To keys.Length - 1
            parts(i + 1) = Localization.T(keys(i))
        Next
        Return String.Join(If(Localization.IsRightToLeft(), " ← ", " → "), parts)
    End Function

    Private Sub AddPart(panel As FlowLayoutPanel, heading As Func(Of String), text As Func(Of String))
        Dim head As New Label With {.AutoSize = True, .Margin = New Padding(0, 10, 0, 0)}
        headings.Add(head)
        Localize(Sub() head.Text = heading())

        Dim paragraph As New Label With {
            .AutoSize = True,
            .MaximumSize = New Size(ContentWidth, 0),
            .UseMnemonic = False,
            .Margin = New Padding(0, 2, 0, 0)}
        Localize(Sub() paragraph.Text = text())

        panel.Controls.Add(head)
        panel.Controls.Add(paragraph)
    End Sub

    Private Sub BuildLanguagePanel(panel As FlowLayoutPanel)
        AddWhatWhyHowWhere(panel,
            Function() Localization.T("Язык, на котором говорят окна, подсказки и сообщения программы."),
            Function() Localization.T("Программа выбрала язык по настройкам Windows. Если вам нужен другой, выберите его здесь."),
            Function() Localization.T("Все окна, подсказки и вопросы сразу станут на этом языке. Сменить его можно в любой момент в окне настроек."),
            Function() Localization.TF("Позже меняется здесь: {0}.", WherePath("interface_language")))

        Dim language As New WelcomeComboBox With {
            .AutoSize = True,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Width = ContentWidth,
            .Margin = New Padding(0, 12, 0, 0)}
        ThemedComboBox.Apply(language, ThemeService.Palette)
        For Each name As String In Localization.Names
            language.Items.Add(name)
        Next
        Localize(Sub() language.SelectedIndex = Math.Max(0, Array.IndexOf(Localization.Codes, draftLanguage)))
        AddHandler language.SelectedIndexChanged,
            Sub()
                If language.SelectedIndex >= 0 Then PreviewLanguage(Localization.Codes(language.SelectedIndex))
            End Sub
        panel.Controls.Add(language)
    End Sub

    Private Sub BuildAppearancePanel(panel As FlowLayoutPanel)
        AddWhatWhyHowWhere(panel,
            Function() Localization.T("Фон вокруг картинки и то, что делает колесо мыши."),
            Function() Localization.T("Фон должен спокойно обрамлять снимок, а колесо не должно случайно менять масштаб, когда вы листаете."),
            Function() AppearanceHow(),
            Function() Localization.TF("Фон - {0}; колесо - {1}.", WherePath("background_color"), WherePath("wheel_zooms")))

        Dim schemeLabel As New Label With {.AutoSize = True, .Margin = New Padding(0, 12, 0, 0)}
        Localize(Sub() schemeLabel.Text = Localization.T("Цвет фона:"))
        panel.Controls.Add(schemeLabel)

        Dim scheme As New WelcomeComboBox With {.AutoSize = True, .DropDownStyle = ComboBoxStyle.DropDownList, .Width = 260, .Margin = New Padding(0, 4, 0, 0),
                                                .ItemHeight = TargetHeight}
        ThemedComboBox.Apply(scheme, ThemeService.Palette)
        ' The same six entries, in the same order, as the settings window's background list.
        Localize(Sub()
                     Dim selected As Integer = draftScheme
                     If selected < 0 OrElse selected >= Utils.Color_Scheme_Count Then selected = WelcomeRules.DefaultColorScheme
                     schemeLoading = True
                     Try
                         scheme.Items.Clear()
                         scheme.Items.Add(Localization.T("По углу"))
                         scheme.Items.Add(Localization.T("Чёрный"))
                         scheme.Items.Add(Localization.T("Белый"))
                         scheme.Items.Add(Localization.T("По краю"))
                         scheme.Items.Add(Localization.T("По верху"))
                         scheme.Items.Add(Localization.T("По низу"))
                         scheme.SelectedIndex = selected
                     Finally
                         schemeLoading = False
                     End Try
                 End Sub)
        AddHandler scheme.SelectedIndexChanged,
            Sub()
                If Not schemeLoading AndAlso scheme.SelectedIndex >= 0 Then draftScheme = scheme.SelectedIndex
            End Sub
        panel.Controls.Add(scheme)

        Dim wheel As New CheckBox With {
            .AutoSize = True,
            .MinimumSize = New Size(0, TargetHeight),
            .Margin = New Padding(0, 8, 0, 0),
            .Checked = draftWheelZooms}
        Localize(Sub() wheel.Text = Localization.T("Колесо мыши меняет масштаб, а не файлы"))
        AddHandler wheel.CheckedChanged,
            Sub()
                draftWheelZooms = wheel.Checked
                RetextAll()
            End Sub
        panel.Controls.Add(wheel)
    End Sub

    ''' <summary>Spec 184 W-2: the sentence names the wheel's behaviour that is actually in effect.</summary>
    Private Function AppearanceHow() As String
        If draftWheelZooms Then
            Return Localization.T("Колесо меняет масштаб у курсора, файлы листают стрелки.")
        End If
        Return Localization.T("Колесо переходит к следующему или предыдущему файлу, а Ctrl с колесом меняет масштаб.")
    End Function

    Private Sub BuildSortingPanel(panel As FlowLayoutPanel)
        AddWhatWhyHowWhere(panel,
            Function() Localization.T("Цифры 0-9 переносят открытый файл в папку, закреплённую за этой клавишей."),
            Function() Localization.T("Сортировка одним нажатием - главное, ради чего существует программа. Папки назначаются заранее, поэтому нажатие не требует раздумий."),
            Function() Localization.T("Цифра переносит файл. Shift с цифрой верхнего ряда копирует его в ту же папку вместо переноса."),
            Function() Localization.TF("Папки назначаются {0}; переход после копирования - {1}.",
                                       Localization.T("в меню папок программы"), WherePath("advance_after_copy")))

        panel.Controls.Add(Chips("0-9", "->", "@content.folder"))
        panel.Controls.Add(Chips("Shift", "+", "1-9", "->", "@action.copy"))

        Dim keyLabel As New Label With {.AutoSize = True, .Margin = New Padding(0, 12, 0, 0)}
        Localize(Sub() keyLabel.Text = Localization.TF("Папка для клавиши {0}", 1))
        panel.Controls.Add(keyLabel)

        keyOneValueLabel = New Label With {
            .AutoSize = True,
            .MaximumSize = New Size(ContentWidth, 0),
            .UseMnemonic = False,
            .Margin = New Padding(0, 2, 0, 0)}
        Localize(Sub() keyOneValueLabel.Text = KeyOneText())
        panel.Controls.Add(keyOneValueLabel)

        Dim pick As New Button()
        StyleButton(pick)
        Localize(Sub() pick.Text = Localization.T("Выбрать папку.."))
        ButtonGlyphs.Apply(pick, IconMap.GetMeaning("btn_Select_Folder").GlyphId)
        AddHandler pick.Click, AddressOf PickKeyOneFolder

        Dim clear As New Button()
        StyleButton(clear)
        Localize(Sub() clear.Text = Localization.T("Очистить"))
        ButtonGlyphs.Apply(clear, "action.clear-all")
        AddHandler clear.Click,
            Sub()
                draftKeyOne = String.Empty
                RetextAll()
            End Sub

        Dim row As New FlowLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .Margin = New Padding(0, 4, 0, 0)}
        row.Controls.Add(pick)
        row.Controls.Add(clear)
        panel.Controls.Add(row)

        Dim note As New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .UseMnemonic = False, .Margin = New Padding(0, 8, 0, 0)}
        Localize(Sub() note.Text = Localization.T("Остальные девять папок задаются в меню папок."))
        panel.Controls.Add(note)
    End Sub

    Private Function KeyOneText() As String
        If String.IsNullOrEmpty(draftKeyOne) Then Return Localization.T("Не выбрана")
        Return draftKeyOne
    End Function

    Private Sub PickKeyOneFolder(sender As Object, e As EventArgs)
        Using picker As New FolderBrowserDialog()
            picker.Description = Localization.TF("Выберите папку для клавиши {0}", 1)
            picker.ShowNewFolderButton = True
            If Not String.IsNullOrEmpty(draftKeyOne) AndAlso Directory.Exists(draftKeyOne) Then picker.SelectedPath = draftKeyOne
            If picker.ShowDialog(Me) = DialogResult.OK Then
                draftKeyOne = picker.SelectedPath
                RetextAll()
            End If
        End Using
    End Sub

    Private Sub BuildDeletePanel(panel As FlowLayoutPanel)
        AddWhatWhyHowWhere(panel,
            Function() Localization.T("Del отправляет файл в Корзину, откуда его можно восстановить. Shift+Del удаляет окончательно."),
            Function() Localization.T("Ошибка при сортировке не должна стоить файла. Корзина - страховка, которая есть почти всегда."),
            Function() Localization.T("Если на диске нет Корзины (сетевой диск, флешка), программа скажет об этом до удаления."),
            Function() Localization.TF("Позже меняется здесь: {0} и {1}.",
                                       WherePath("delete_to_recycle_bin"), WherePath("confirm_delete")))

        panel.Controls.Add(Chips("Del", "->", "@action.delete"))
        panel.Controls.Add(Chips("Shift", "+", "Del", "->", "@status.warning"))

        ' Spec 211 §4.2: the step asks, like every other step. The checkbox is bound to the settings row
        ' delete_to_recycle_bin, opens on the value the viewer holds (the code default on a fresh profile) and is
        ' applied on Next through the settings block - nothing is written unless the user changes it.
        Dim check As New CheckBox With {
            .AutoSize = True,
            .MinimumSize = New Size(0, TargetHeight),
            .Margin = New Padding(0, 12, 0, 0),
            .Checked = draftDeleteBin}
        Localize(Sub() check.Text = Localization.T("Удалять в Корзину"))
        AddHandler check.CheckedChanged, Sub() draftDeleteBin = check.Checked
        panel.Controls.Add(check)
    End Sub

    ''' <summary>A row of key caps and catalog glyphs (see <see cref="StepChips"/>); it carries no words.</summary>
    Private Function Chips(ParamArray items As String()) As StepChips
        Return New StepChips(AddressOf RuntimeBootstrap.OpenBundledAsset, items)
    End Function

    Private Sub BuildAssociationsPanel(panel As FlowLayoutPanel)
        AddWhatWhyHowWhere(panel,
            Function() Localization.T("Двойной щелчок по картинке в Проводнике откроет её в этой программе."),
            Function() Localization.T("Файловые связи меняют то, что делает Проводник, поэтому без вашего согласия ничего не меняется."),
            Function() Localization.T("Если включить, связи записываются только для вашей учётной записи и не требуют прав администратора. Изменить это можно в настройках."),
            Function() Localization.TF("Позже меняется здесь: {0} и {1}.",
                                       WherePath("image_associations"), WherePath("video_associations")))

        panel.Controls.Add(Chips("JPG", "PNG", "WEBP", "GIF", "MP4"))

        Dim check As New CheckBox With {
            .AutoSize = True,
            .MinimumSize = New Size(0, TargetHeight),
            .Margin = New Padding(0, 12, 0, 0),
            .Checked = draftAssociations}
        Localize(Sub() check.Text = Localization.T("Открывать картинки в этой программе"))
        AddHandler check.CheckedChanged, Sub() draftAssociations = check.Checked
        panel.Controls.Add(check)
    End Sub

    Private Sub BuildFinishPanel(panel As FlowLayoutPanel)
        AddWhatWhyHowWhere(panel,
            Function() Localization.T("Выбранное сохранено. Всё остальное осталось по умолчанию."),
            Function() Localization.T("Эти шаги - всё, что нужно понять новичку. Остальные параметры собраны в окне настроек, где работает поиск."),
            Function() Localization.T("Откройте папку, чтобы начать. Окно настроек и F1 вернут эту справку в любой момент."),
            Function() Localization.TF("Вернуть эти шаги: {0} или клавиша F1.", WherePath("welcome_replay")))
        Dim summary As New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0),
                                        .UseMnemonic = False, .Margin = New Padding(0, 12, 0, 0)}
        Localize(Sub() summary.Text = FinishSummary())
        panel.Controls.Add(summary)
        BuildServersBlock(panel)
    End Sub

    ''' <summary>
    ''' The installed-servers block (spec 215): below the recap, one row per server component installed with this
    ''' program - what it is, a pill saying whether its process is up, and one button. An offer, never an act: a user
    ''' who presses nothing gets exactly the wizard 211 ends with (rule 4). Both rows are built here and shown or
    ''' hidden by <see cref="RefreshServers"/>; a block with no installed component stays hidden whole.
    ''' </summary>
    Private Sub BuildServersBlock(panel As FlowLayoutPanel)
        serversBlock = New FlowLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False,
            .Margin = New Padding(0, 14, 0, 0),
            .Visible = False}

        Dim title As New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .UseMnemonic = False, .Margin = New Padding(0)}
        headings.Add(title)
        Localize(Sub() title.Text = Localization.T("Серверы на этом ПК"))
        serversBlock.Controls.Add(title)

        For Each server As WelcomeServer In {WelcomeServer.ShareServer, WelcomeServer.ExchangeServer}
            Dim view As New ServerRowView(server)
            BuildServerRow(view)
            serverRows.Add(view)
            serversBlock.Controls.Add(view.Container)
        Next
        panel.Controls.Add(serversBlock)
        RefreshServers()
    End Sub

    Private Sub BuildServerRow(view As ServerRowView)
        view.Container.AutoSize = True
        view.Container.AutoSizeMode = AutoSizeMode.GrowAndShrink
        view.Container.FlowDirection = FlowDirection.TopDown
        view.Container.WrapContents = False
        view.Container.Margin = New Padding(0, 10, 0, 0)
        view.Container.Visible = False

        view.NameLabel.AutoSize = True
        view.NameLabel.MaximumSize = New Size(ContentWidth, 0)
        view.NameLabel.UseMnemonic = False
        view.NameLabel.Margin = New Padding(0)
        headings.Add(view.NameLabel)

        view.WhatLabel.AutoSize = True
        view.WhatLabel.MaximumSize = New Size(ContentWidth, 0)
        view.WhatLabel.UseMnemonic = False
        view.WhatLabel.Margin = New Padding(0, 2, 0, 0)

        view.Dot.AutoSize = True
        view.Dot.UseMnemonic = False
        view.Dot.Text = PillDot
        view.Dot.Margin = New Padding(0, 0, 6, 0)
        view.Dot.AccessibleRole = AccessibleRole.None

        view.Word.AutoSize = True
        view.Word.MaximumSize = New Size(ContentWidth - 24, 0)
        view.Word.UseMnemonic = False
        view.Word.Margin = New Padding(0)

        Dim pillRow As New FlowLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .Margin = New Padding(0, 6, 0, 0)}
        pillRow.Controls.Add(view.Dot)
        pillRow.Controls.Add(view.Word)

        StyleButton(view.ActButton)
        view.ActButton.Margin = New Padding(0, 6, 0, 0)
        Dim captured As ServerRowView = view
        AddHandler view.ActButton.Click, Sub() host.WelcomeOpenServerManager(captured.Act)

        view.Container.Controls.Add(view.NameLabel)
        view.Container.Controls.Add(view.WhatLabel)
        view.Container.Controls.Add(pillRow)
        view.Container.Controls.Add(view.ActButton)

        Localize(Sub() UpdateServerRowText(captured))
    End Sub

    Private Shared Function ServerName(server As WelcomeServer) As String
        Return If(server = WelcomeServer.ExchangeServer, ProductNames.ExchangeServer, Localization.T("Сервер папок для Android"))
    End Function

    Private Shared Function ServerWhat(server As WelcomeServer) As String
        Return Localization.T(If(server = WelcomeServer.ExchangeServer,
                                 "Собственный сервер обмена этого ПК. Управляется в Share Manager.",
                                 "Раздаёт папки этого ПК на телефон. Управляется в Share Manager."))
    End Function

    ''' <summary>The words of one row in the language being shown: name, what it is, the pill's word and the button's.
    ''' The amber word carries the one clause of what to do (spec 215 section 5), so colour is never the only signal.</summary>
    Private Sub UpdateServerRowText(view As ServerRowView)
        view.NameLabel.Text = ServerName(view.Server)
        view.WhatLabel.Text = ServerWhat(view.Server)
        view.Word.Text = Localization.T(If(view.Pill = ServerPill.Green, "Работает", "Не запущен - менеджер включит его по вашему выбору"))
        Dim isAdd As Boolean = view.Act = ServerAct.AddResource
        view.ActButton.Text = Localization.T(If(isAdd, "Добавить ресурс", "Открыть менеджер"))
        view.ActButton.AccessibleName = Localization.TF("{0} - {1}", view.ActButton.Text, view.NameLabel.Text)
        Dim glyph As String = If(isAdd, "action.add", "nav.open-external")
        If Not String.Equals(ButtonGlyphs.GlyphOf(view.ActButton), glyph, StringComparison.Ordinal) Then
            ButtonGlyphs.Apply(view.ActButton, glyph)
        End If
    End Sub

    ''' <summary>
    ''' Reads the block's facts now (the component probes and one process lookup per installed server) and redraws it.
    ''' Called when the wizard opens and each time the finish screen becomes current, so a server started behind the
    ''' wizard's back shows the truth at the end - no timer, no polling (spec 215 section 5).
    ''' </summary>
    Private Sub RefreshServers()
        If serversBlock Is Nothing Then Return
        Dim block As IReadOnlyList(Of WelcomeServerRow) = host.WelcomeServerBlock()
        serversBlock.SuspendLayout()
        Try
            For Each view As ServerRowView In serverRows
                view.Pill = ServerPill.Absent
                For Each row As WelcomeServerRow In block
                    If row.Server = view.Server Then
                        view.Pill = row.Pill
                        view.Act = row.Act
                    End If
                Next
                view.Container.Visible = view.Pill <> ServerPill.Absent
                UpdateServerRowText(view)
            Next
            serversBlock.Visible = block.Count > 0
            ApplyServerPills()
        Finally
            serversBlock.ResumeLayout(True)
        End Try
    End Sub

    ''' <summary>The dot's colour for the current theme, never lighter or darker than 4.5:1 on the window (spec 215
    ''' section 5). The word beside it is plain text, so a high-contrast or colour-blind reader loses nothing.</summary>
    Private Sub ApplyServerPills()
        Dim palette As ThemePalette = ThemeService.Palette
        For Each view As ServerRowView In serverRows
            Dim ink As Color = If(view.Pill = ServerPill.Green, palette.Success, palette.Warning)
            view.Dot.ForeColor = ThemePalette.ReadableText(ink, palette.SurfaceWindow)
        Next
    End Sub

    ''' <summary>
    ''' The finish recap (spec 211 §4.4): what was chosen, and - after a plain hyphen - the settings page and row
    ''' where each choice lives later, from the same pairing the steps' where-parts are built on.
    ''' </summary>
    Private Function FinishSummary() As String
        Dim schemes As String() = {"По углу", "Чёрный", "Белый", "По краю", "По верху", "По низу"}
        Dim scheme As Integer = Math.Max(0, Math.Min(schemes.Length - 1, openedScheme))
        Return String.Join(Environment.NewLine, {
            Localization.TF("{0}: {1} - {2}", Localization.T("Язык интерфейса"),
                            Localization.Names(Localization.IndexOf(appliedLanguage)), WherePath("interface_language")),
            Localization.TF("{0}: {1} - {2}", Localization.T("Внешний вид"), Localization.T(schemes(scheme)),
                            WherePath("background_color")),
            Localization.TF("{0}: {1} - {2}", Localization.T("Масштабирование колёсиком"),
                            Localization.T(If(openedWheelZooms, "Колесо меняет масштаб у курсора, файлы листают стрелки.",
                                               "Колесо переходит к следующему или предыдущему файлу, а Ctrl с колесом меняет масштаб.")), WherePath("wheel_zooms")),
            Localization.TF("{0}: {1} - {2}", Localization.TF("Папка для клавиши {0}", 1),
                            If(String.IsNullOrEmpty(openedKeyOne), Localization.T("Не выбрана"), openedKeyOne),
                            Localization.T("в меню папок программы")),
            Localization.TF("{0}: {1} - {2}", Localization.T("Удалять в Корзину"),
                            Localization.T(If(openedDeleteBin, "Файлы после Del уходят в Корзину.",
                                              "Файлы после Del удаляются окончательно.")), WherePath("delete_to_recycle_bin"))})
    End Function

    ' ---------------------------------------------------------------- texts ----

    Private schemeLoading As Boolean

    ''' <summary>Registers a text: it is applied now and again on every language or draft change.</summary>
    Private Sub Localize(apply As Action)
        localizers.Add((localizerStep, apply))
        apply()
    End Sub

    ''' <summary>Updates a whole screen before layout. Preview language never escapes into the viewer's settings flush.</summary>
    Private Sub RetextAll()
        Dim current As String = Localization.CurrentCode
        SuspendTree(Me)
        Try
            Localization.CurrentCode = draftLanguage
            For Each entry In localizers
                If entry.StepIndex = -1 OrElse entry.StepIndex = position Then entry.Apply()
            Next
        Finally
            Localization.CurrentCode = current
            ResumeTree(Me)
        End Try
        If IsHandleCreated Then ApplyTargetSizes()
    End Sub

    Private Shared Sub SuspendTree(control As Control)
        control.SuspendLayout()
        For Each child As Control In control.Controls
            SuspendTree(child)
        Next
    End Sub

    Private Shared Sub ResumeTree(control As Control)
        For Each child As Control In control.Controls
            ResumeTree(child)
        Next
        control.ResumeLayout(True)
    End Sub

    Private Function TitleText() As String
        Select Case steps(position)
            Case WelcomeStep.Language : Return Localization.T("Язык интерфейса")
            Case WelcomeStep.Appearance : Return Localization.T("Внешний вид")
            Case WelcomeStep.Sorting : Return Localization.T("Как устроена сортировка")
            Case WelcomeStep.Delete : Return Localization.T("Безопасное удаление")
            Case WelcomeStep.Associations : Return Localization.T("Открывать картинки здесь")
            Case Else : Return Localization.T("Всё готово")
        End Select
    End Function

    ''' <summary>The catalog glyph that stands for the current step, drawn in the header's round badge.</summary>
    Private Function StepGlyph() As String
        Select Case steps(position)
            Case WelcomeStep.Language : Return "system.internet"
            Case WelcomeStep.Appearance : Return "view.screen"
            Case WelcomeStep.Sorting : Return "action.send-to"
            Case WelcomeStep.Delete : Return "action.delete"
            Case WelcomeStep.Associations : Return "content.image"
            Case Else : Return "status.ok"
        End Select
    End Function

    ''' <summary>Applies the script of the current language: the font its alphabet needs, and the reading direction.</summary>
    Private Sub ApplyScript()
        Dim current As String = Localization.CurrentCode
        SuspendTree(Me)
        Try
            Localization.CurrentCode = draftLanguage
            Dim family As String = If(Localization.FontFamily(), baseFont.FontFamily.Name)
            If Not scriptFonts.TryGetValue(family, script_Font) Then
                script_Font = New Font(family, baseFont.Size, FontStyle.Regular)
                scriptFonts.Add(family, script_Font)
                headingFonts.Add(family, New Font(script_Font, FontStyle.Bold))
                titleFonts.Add(family, New Font(family, baseFont.Size * 1.5F, FontStyle.Bold))
            End If
            bold_Font = headingFonts(family)
            Font = script_Font
            RightToLeft = If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No)
            For Each head As Label In headings
                head.Font = bold_Font
            Next
            titleLabel.Font = titleFonts(family)
            ApplyAccents()
        Finally
            Localization.CurrentCode = current
            ResumeTree(Me)
        End Try
    End Sub

    ''' <summary>Keep the accent on headings only when it reads against the window; the art uses the palette itself.</summary>
    Private Sub ApplyAccents()
        Dim palette As ThemePalette = ThemeService.Palette
        Dim accent As Color = If(Utils.ContrastRatio(palette.Accent, palette.SurfaceWindow) >= 4.5R,
                                 palette.Accent, palette.TextPrimary)
        For Each head As Label In headings
            head.ForeColor = accent
        Next
        badge.Invalidate()
        dots.Invalidate()
    End Sub

    ' ---------------------------------------------------------------- navigation ----

    Private Sub ShowStep(index As Integer)
        SuspendTree(Me)
        Try
            position = index
            For i As Integer = 0 To panels.Count - 1
                panels(i).Visible = (i = index)
            Next
            backButton.Enabled = index > 0
            skipButton.Visible = index < finishIndex
            ' Spec 215 section 5: a server started behind the wizard's back shows the truth when the finish screen is
            ' reached - the second of the two reads (the first is at open time); never polled in between.
            If steps(index) = WelcomeStep.Finish Then RefreshServers()
            RetextAll()
        Finally
            ResumeTree(Me)
        End Try
        If IsHandleCreated Then DialogLayout.ClampToWorkingArea(Me)
    End Sub

    ''' <summary>A language picked in the first step is shown at once; the viewer adopts it only on Next.</summary>
    Private Sub PreviewLanguage(code As String)
        If String.Equals(code, draftLanguage, StringComparison.Ordinal) Then Return
        draftLanguage = code
        ApplyScript()
        RetextAll()
    End Sub

    Private Sub GoBack(sender As Object, e As EventArgs)
        If position > 0 Then ShowStep(position - 1)
    End Sub

    Private Sub GoNext(sender As Object, e As EventArgs)
        If position = finishIndex Then
            Close()
            Return
        End If
        If CommitStep(steps(position)) Then ShowStep(position + 1)
    End Sub

    Private Sub SkipToFinish(sender As Object, e As EventArgs)
        draftLanguage = appliedLanguage
        ApplyScript()
        ShowStep(finishIndex)
    End Sub

    ''' <summary>Applies the draft of one step, and only what the user changed. False keeps the wizard on this step.</summary>
    Private Function CommitStep(wizardStep As WelcomeStep) As Boolean
        Select Case wizardStep
            Case WelcomeStep.Language
                CommitLanguage()

            Case WelcomeStep.Appearance
                If draftScheme <> openedScheme OrElse draftWheelZooms <> openedWheelZooms Then
                    host.WelcomeApplyAppearance(draftScheme, draftWheelZooms)
                    openedScheme = draftScheme
                    openedWheelZooms = draftWheelZooms
                End If

            Case WelcomeStep.Sorting
                If draftKeyOne <> openedKeyOne Then
                    If Not WelcomeRules.KeyOneMayBind(draftKeyOne, Directory.Exists(draftKeyOne)) Then
                        AskDialogs.Tell(Me, Me.Text, Localization.T("Папка больше не доступна. Выберите другую."), AskIcon.Warning)
                        Return False
                    End If
                    host.WelcomeApplyKeyOne(draftKeyOne)
                    openedKeyOne = draftKeyOne
                End If

            Case WelcomeStep.Delete
                ' Spec 211 rule 4: a user who walks through with Next gets exactly today's behaviour.
                If draftDeleteBin <> openedDeleteBin Then
                    host.WelcomeApplyDeleteToRecycleBin(draftDeleteBin)
                    openedDeleteBin = draftDeleteBin
                End If

            Case WelcomeStep.Associations
                If draftAssociations Then
                    host.WelcomeRegisterImages()
                    draftAssociations = False
                End If
        End Select
        Return True
    End Function

    Private Sub CommitLanguage()
        If String.Equals(draftLanguage, appliedLanguage, StringComparison.Ordinal) Then Return
        host.SetUiLanguage(draftLanguage)
        host.SaveSettingsNow()
        appliedLanguage = Localization.CurrentCode
    End Sub

    ' ---------------------------------------------------------------- window events ----

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        ' Esc closes the wizard, except while a drop-down list is open: Esc there only closes the list.
        Dim combo As ComboBox = TryCast(ActiveControl, ComboBox)
        If keyData = Keys.Escape AndAlso (combo Is Nothing OrElse Not combo.DroppedDown) Then
            Close()
            Return True
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ApplyTargetSizes()
        Dim area As Rectangle = Screen.FromControl(Me).WorkingArea
        MaximumSize = area.Size
        DialogLayout.ClampToWorkingArea(Me)
    End Sub

    Private Sub ApplyTargetSizes()
        For Each panel As FlowLayoutPanel In panels
            For Each child As Control In panel.Controls
                Dim combo As ComboBox = TryCast(child, ComboBox)
                ' WinForms scales a combo's bounds but leaves its owner-drawn ItemHeight in pixels.
                If combo IsNot Nothing Then
                    combo.ItemHeight = LogicalToDeviceUnits(TargetHeight)
                End If
            Next
            panel.PerformLayout()
        Next
        body.PerformLayout()
        Controls(0).PerformLayout()
    End Sub

    Private Sub OnThemeChanged(sender As Object, e As EventArgs)
        If IsDisposed Then Return
        ApplyTargetSizes()
        ApplyAccents()
        ApplyServerPills()
    End Sub

    Protected Overrides Sub OnDpiChanged(e As DpiChangedEventArgs)
        MyBase.OnDpiChanged(e)
        ApplyTargetSizes()
        MaximumSize = Screen.FromControl(Me).WorkingArea.Size
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        nextButton.Focus()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        ' A language that was shown but never applied goes back to the viewer's own (spec 184 rule 5).
        If Not String.Equals(Localization.CurrentCode, appliedLanguage, StringComparison.Ordinal) Then
            Localization.CurrentCode = appliedLanguage
        End If
        host.WelcomeRecordExit()
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            RemoveHandler ThemeService.Changed, AddressOf OnThemeChanged
        End If
        MyBase.Dispose(disposing)
        If disposing Then
            For Each owned As Font In scriptFonts.Values
                owned.Dispose()
            Next
            For Each owned As Font In headingFonts.Values
                owned.Dispose()
            Next
            For Each owned As Font In titleFonts.Values
                owned.Dispose()
            Next
            baseFont.Dispose()
        End If
    End Sub

End Class

#End If
