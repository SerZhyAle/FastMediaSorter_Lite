#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The compact shortcuts dialog of §3.5 - deliberately its own window rather than a long
''' table on the Files page, which is what the specification asks for and what keeps forty
''' actions from burying the eight settings around them.
'''
''' It edits a COPY of the override map and hands it back only on OK, so a session spent
''' experimenting with bindings and then cancelled leaves the profile exactly as it was.
''' Every rule it enforces - what may be assigned, who already owns a combination, what
''' "no shortcut" means - comes from CustomHotkeys, which is pure and tested; this file is
''' the window around it.
''' </summary>
Public NotInheritable Class Hotkeys_Form
    Inherits Form

    Private ReadOnly grid As New ListView()
    Private ReadOnly captureBox As New HotkeyCaptureBox()
    Private ReadOnly hint As New Label()
    Private ReadOnly btnReset As New Button()
    Private ReadOnly btnClear As New Button()
    Private ReadOnly btnResetAll As New Button()
    Private ReadOnly btnOk As New Button()
    Private ReadOnly btnCancel As New Button()

    Private ReadOnly working As Dictionary(Of String, String)

    ''' <summary>The edited map, valid once ShowDialog returned OK.</summary>
    Public ReadOnly Property Result As Dictionary(Of String, String)
        Get
            Return working
        End Get
    End Property

    Public Sub New(current As Dictionary(Of String, String))
        working = New Dictionary(Of String, String)(StringComparer.Ordinal)
        If current IsNot Nothing Then
            For Each pair As KeyValuePair(Of String, String) In current
                working(pair.Key) = pair.Value
            Next
        End If

        Build()
        Fill()
    End Sub

    ''' <summary>The form's own font: Form.Font does not take ownership of a Font it was
    ''' handed, so without this it lived until the finalizer ran.</summary>
    Private ui_Font As Font

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing AndAlso ui_Font IsNot Nothing Then
            ui_Font.Dispose()
            ui_Font = Nothing
        End If
    End Sub

    Private Sub Build()
        ui_Font = New Font(Localization.FontFamily(), 9.0F)
        Dim uiFont As Font = ui_Font

        Text = Localization.T("Сочетания клавиш")
        FormBorderStyle = FormBorderStyle.Sizable
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(Localization.Scaled(640), 560)
        MinimumSize = New Size(Localization.Scaled(560), 420)
        Font = uiFont
        ' Text direction only - never RightToLeftLayout, which mirrors the whole device
        ' context (see the localization rules in CLAUDE.md).
        If Localization.IsRightToLeft() Then RightToLeft = RightToLeft.Yes

        grid.View = View.Details
        grid.FullRowSelect = True
        grid.MultiSelect = False
        grid.HideSelection = False
        grid.HeaderStyle = ColumnHeaderStyle.Nonclickable
        grid.Columns.Add(Localization.T("Действие"), Localization.Scaled(360))
        grid.Columns.Add(Localization.T("Сочетание"), Localization.Scaled(210))
        Dim documentHelp As New Label With {.AutoSize = False,
            .Bounds = New Rectangle(12, 8, ClientSize.Width - 24, 56),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
            .Text = Localization.T("Документы: колесо и PgUp/PgDn - прокрутка или страницы; Ctrl+колесо - масштаб; Home/End - начало/конец документа, Ctrl+Home/End - первый/последний файл. Стрелки - файлы.")}
        Controls.Add(documentHelp)
        grid.Bounds = New Rectangle(12, 68, ClientSize.Width - 24, ClientSize.Height - 206)
        grid.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        AddHandler grid.SelectedIndexChanged, Sub() ShowCurrentCombo()
        AddHandler grid.DoubleClick, Sub() captureBox.Focus()
        Controls.Add(grid)

        hint.AutoSize = False
        hint.Bounds = New Rectangle(12, grid.Bottom + 8, ClientSize.Width - 24, 34)
        hint.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        hint.Text = Localization.T("Выберите действие, поставьте курсор в поле и нажмите сочетание. Esc отменяет запись.")
        Controls.Add(hint)

        captureBox.ReadOnly = True
        captureBox.TextAlign = HorizontalAlignment.Center
        captureBox.Bounds = New Rectangle(12, hint.Bottom + 4, Localization.Scaled(210), 26)
        captureBox.Anchor = AnchorStyles.Left Or AnchorStyles.Bottom
        AddHandler captureBox.ComboCaptured, AddressOf CaptureCombo
        AddHandler captureBox.Enter, Sub() captureBox.BackColor = SystemColors.Info
        AddHandler captureBox.Leave, Sub() captureBox.BackColor = SystemColors.Window
        Controls.Add(captureBox)

        LayoutButton(btnReset, Localization.T("Сбросить"), captureBox.Right + 8, captureBox.Top, 130)
        AddHandler btnReset.Click, Sub() ApplyToSelection(Nothing)

        LayoutButton(btnClear, Localization.T("Снять сочетание"), btnReset.Right + 8, captureBox.Top, 150)
        AddHandler btnClear.Click, Sub() ApplyToSelection(CustomHotkeys.NoShortcut)

        ' On the OK / Cancel row, at the start edge: on the capture row it started at x = 488 while "Снять сочетание"
        ' ends at x = 518, so the two overlapped by 30 px and one hid part of the other's caption at every scale
        ' in English and Arabic (the 124 layout audit finds it).
        LayoutButton(btnResetAll, Localization.T("Сбросить всё"), 12, ClientSize.Height - 42, 140)
        btnResetAll.Anchor = AnchorStyles.Left Or AnchorStyles.Bottom
        AddHandler btnResetAll.Click, AddressOf ResetAll

        LayoutButton(btnOk, Localization.T("ОК"), ClientSize.Width - 24 - 220, ClientSize.Height - 42, 105)
        btnOk.Anchor = AnchorStyles.Right Or AnchorStyles.Bottom
        btnOk.DialogResult = DialogResult.OK

        LayoutButton(btnCancel, Localization.T("Отмена"), ClientSize.Width - 12 - 105, ClientSize.Height - 42, 105)
        btnCancel.Anchor = AnchorStyles.Right Or AnchorStyles.Bottom
        btnCancel.DialogResult = DialogResult.Cancel

        AcceptButton = btnOk
        CancelButton = btnCancel

        ' LAST line of the builder: scaling touches only the children that exist by now (124 UX-202).
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' WinForms never scales a ListView's column widths: logical 96-dpi pixels until the window has its DPI.
        grid.Columns(0).Width = DialogLayout.Device(grid, Localization.Scaled(360))
        grid.Columns(1).Width = DialogLayout.Device(grid, Localization.Scaled(210))
        ' The window is taller than a small laptop screen at a high scale: keep it on the monitor.
        DialogLayout.ClampToWorkingArea(Me)
        ' OnLoad can clamp while the form's initial anchor layout is still pending.
        ' Restate the footer against the final client size instead of retaining its
        ' pre-clamp bottom coordinates at 175 percent.
        For Each command As Button In New Button() {btnResetAll, btnOk, btnCancel}
            command.Top = ClientSize.Height - command.Height - DialogLayout.Device(Me, 14)
        Next
        Dim captureHeight As Integer = Math.Max(captureBox.Height, Math.Max(btnReset.Height, btnClear.Height))
        Dim captureTop As Integer = btnOk.Top - captureHeight - DialogLayout.Device(Me, 8)
        captureBox.Top = captureTop
        btnReset.Top = captureTop
        btnClear.Top = captureTop
        hint.Top = captureTop - hint.Height - DialogLayout.Device(Me, 4)
        grid.Height = Math.Max(1, hint.Top - grid.Top - DialogLayout.Device(Me, 8))
    End Sub

    Private Sub LayoutButton(button As Button, caption As String, left As Integer, top As Integer, width As Integer)
        button.Text = caption
        button.Bounds = New Rectangle(left, top, width, 28)
        button.Anchor = AnchorStyles.Left Or AnchorStyles.Bottom
        Controls.Add(button)
    End Sub

    Private Sub Fill()
        grid.BeginUpdate()
        grid.Items.Clear()
        For Each action As HotkeyAction In CustomHotkeys.Catalog
            Dim item As New ListViewItem(ActionCaption(action)) With {.Tag = action.Id}
            item.SubItems.Add(ComboText(action))
            grid.Items.Add(item)
        Next
        grid.EndUpdate()
        If grid.Items.Count > 0 Then grid.Items(0).Selected = True
        ShowCurrentCombo()
    End Sub

    ''' <summary>Recipient slots carry no caption of their own - one placeholder string
    ''' names all ten, which is also ten fewer entries to translate into twelve
    ''' languages.</summary>
    Private Shared Function ActionCaption(action As HotkeyAction) As String
        Dim slot As Integer = CustomHotkeys.RecipientSlotOf(action.Id)
        If slot > 0 Then Return Localization.TF("Переместить в папку {0}", action.DefaultCombo)
        Dim copySlot As Integer = CustomHotkeys.CopySlotOf(action.Id)
        If copySlot > 0 Then Return Localization.TF("Копировать в папку {0}", CustomHotkeys.SlotDigit(copySlot))
        Return Localization.T(action.Caption)
    End Function

    Private Function ComboText(action As HotkeyAction) As String
        Dim combo As String = CustomHotkeys.EffectiveCombo(working, action)
        If combo.Length = 0 Then Return Localization.T("Не назначено")
        Return combo
    End Function

    Private Function SelectedAction() As HotkeyAction
        If grid.SelectedItems.Count = 0 Then Return Nothing
        Return CustomHotkeys.Find(Convert.ToString(grid.SelectedItems(0).Tag))
    End Function

    Private Sub ShowCurrentCombo()
        Dim action As HotkeyAction = SelectedAction()
        captureBox.Text = If(action Is Nothing, String.Empty, ComboText(action))
    End Sub

    ''' <summary>
    ''' One captured key press. Esc leaves the field without recording anything, which is
    ''' the escape hatch §3.5 asks for; a reserved combination is refused by name rather
    ''' than silently ignored, so the user learns why nothing happened.
    ''' </summary>
    Private Sub CaptureCombo(keyData As Keys)
        If (keyData And Keys.KeyCode) = Keys.Escape Then
            ShowCurrentCombo()
            grid.Focus()
            Return
        End If

        Dim action As HotkeyAction = SelectedAction()
        If action Is Nothing Then Return

        Dim combo As String = CustomHotkeys.Format(keyData)
        If combo.Length = 0 Then
            If CustomHotkeys.IsReserved(keyData) Then
                ViewerBox.Show(Me,
                    Localization.T("Это сочетание зарезервировано системой или программой и не может быть назначено."),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
            End If
            Return
        End If

        Dim ownerId As String = CustomHotkeys.OwnerOfCombo(working, combo)
        If ownerId.Length > 0 AndAlso Not String.Equals(ownerId, action.Id, StringComparison.Ordinal) Then
            If Not ResolveConflict(action, CustomHotkeys.Find(ownerId), combo) Then Return
        End If

        ApplyToSelection(combo)
    End Sub

    ''' <summary>
    ''' The conflict rule of §3.5: name the owner and offer to swap or to clear. Returns
    ''' False when the user backed out, in which case nothing at all has been changed.
    ''' </summary>
    Private Function ResolveConflict(action As HotkeyAction, owner As HotkeyAction, combo As String) As Boolean
        If owner Is Nothing Then Return True

        ' Named answers (124 UX-205, 125 UX-331): "No" used to read as "cancel" and the meaning sat in a sentence.
        Dim answer As Integer = AskDialogs.Choose(Me, AskSpecs.HotkeyConflict(combo, ActionCaption(owner)))

        Select Case answer
            Case AskSpecs.ConflictSwap
                ' The swap gives the previous owner what THIS action answers to now, which
                ' may itself be "nothing" - and that is a legitimate binding.
                Dim freed As String = CustomHotkeys.EffectiveCombo(working, action)
                Assign(owner, If(freed.Length = 0, CustomHotkeys.NoShortcut, freed))
            Case AskSpecs.ConflictClear
                Assign(owner, CustomHotkeys.NoShortcut)
            Case Else
                Return False
        End Select
        Return True
    End Function

    ''' <summary>Nothing means "back to the factory binding" - the map holds overrides
    ''' only, so the way to say that is to have no entry.</summary>
    Private Sub Assign(action As HotkeyAction, combo As String)
        If action Is Nothing Then Return
        If combo Is Nothing OrElse String.Equals(combo, CustomHotkeys.Canonical(action.DefaultCombo), StringComparison.OrdinalIgnoreCase) Then
            working.Remove(action.Id)
        Else
            working(action.Id) = combo
        End If
    End Sub

    Private Sub ApplyToSelection(combo As String)
        Dim action As HotkeyAction = SelectedAction()
        If action Is Nothing Then Return
        Assign(action, combo)
        Refill()
    End Sub

    Private Sub ResetAll(sender As Object, e As EventArgs)
        If AskDialogs.Choose(Me, AskSpecs.ResetHotkeys()) <> 0 Then Return
        working.Clear()
        Refill()
    End Sub

    ''' <summary>Redraws the whole list: a swap changes two rows, and a reset can change
    ''' any of them.</summary>
    Private Sub Refill()
        Dim selectedId As String = If(grid.SelectedItems.Count = 0, String.Empty, Convert.ToString(grid.SelectedItems(0).Tag))
        Fill()
        For Each item As ListViewItem In grid.Items
            If String.Equals(Convert.ToString(item.Tag), selectedId, StringComparison.Ordinal) Then
                item.Selected = True
                item.EnsureVisible()
                Exit For
            End If
        Next
        ShowCurrentCombo()
    End Sub

    ''' <summary>
    ''' A text box that reports key presses instead of reacting to them. ProcessCmdKey is
    ''' the only hook that sees Tab, Enter and Esc before the dialog claims them - which
    ''' is exactly what a capture field has to see - so the whole capture happens there.
    ''' Alt+F4 is deliberately let through: a window that cannot be closed while one field
    ''' has focus is a worse bug than a shortcut nobody can assign anyway.
    ''' </summary>
    Private NotInheritable Class HotkeyCaptureBox
        Inherits TextBox

        Public Event ComboCaptured(keyData As Keys)

        Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
            If Not Focused Then Return MyBase.ProcessCmdKey(msg, keyData)
            If keyData = (Keys.Alt Or Keys.F4) Then Return MyBase.ProcessCmdKey(msg, keyData)

            Select Case keyData And Keys.KeyCode
                Case Keys.ControlKey, Keys.ShiftKey, Keys.Menu
                    Return True         ' a modifier on its own is not a combination yet
            End Select

            RaiseEvent ComboCaptured(keyData)
            Return True
        End Function

        Protected Overrides Sub OnKeyPress(e As KeyPressEventArgs)
            e.Handled = True
        End Sub
    End Class

End Class
#End If
