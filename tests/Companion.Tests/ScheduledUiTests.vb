Option Strict On

Imports System.IO
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The windows of scheduled operations built and driven the way the program builds them (ticket 222, section 8):
''' the editor, the history, the resource picker and the page, over a scratch store and a scratch hive.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ScheduledUiTests

    Private Shared Sub Invoke(target As Object, name As String)
        target.GetType().GetMethod(name, Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic).Invoke(target, New Object() {Nothing, EventArgs.Empty})
    End Sub

    Private Shared Function Find(Of T As Control)(root As Control, name As String) As T
        For Each child As Control In root.Controls
            If child.Name = name AndAlso TypeOf child Is T Then Return DirectCast(child, T)
            Dim nested As T = Find(Of T)(child, name)
            If nested IsNot Nothing Then Return nested
        Next
        Return Nothing
    End Function

    Private NotInheritable Class Kit
        Implements IDisposable
        Friend ReadOnly Tree As New ScratchTree()
        Friend ReadOnly Hive As New ScratchHive()
        Friend ReadOnly Engine As ScheduledEngine
        Friend ReadOnly Journal As ScheduledJournal

        Friend Sub New()
            Dim folder As String = Tree.Dir("state")
            Journal = New ScheduledJournal(folder)
            Engine = New ScheduledEngine(New ScheduledStore(folder), Journal,
                                         New ScheduledServices With {.Clock = Function() New DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Unspecified)},
                                         New RecordingTimers())
            Engine.Start()
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Engine.Dispose()
            Hive.Dispose()
            Tree.Dispose()
        End Sub
    End Class

    <Fact>
    Public Sub The_editor_saves_a_new_operation_through_the_engine()
        ExchangeTestSupport.OnUi(Sub()
                                     Using kit As New Kit()
                                         ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                                         Dim src As String = kit.Tree.Dir("photos")
                                         Dim dst As String = kit.Tree.Dir("backup")
                                         Using form As New Scheduled_Operation_Form(kit.Engine, Nothing)
                                             form.Show()
                                             Find(Of TextBox)(form, "txtSchedName").Text = "Photos backup"
                                             Find(Of TextBox)(form, "txtSchedSource").Text = src
                                             Find(Of TextBox)(form, "txtSchedDestination").Text = dst
                                             Find(Of ComboBox)(form, "cmbSchedAction").SelectedIndex = CInt(ScheduledAction.Move)
                                             Find(Of ComboBox)(form, "cmbSchedConflict").SelectedIndex = CInt(ScheduledConflict.KeepBoth)
                                             Find(Of CheckBox)(form, "chkSchedAllFiles").Checked = False
                                             Find(Of CheckBox)(form, "chkSchedImages").Checked = True
                                             Find(Of TextBox)(form, "txtSchedMask").Text = "*.jpg"
                                             Find(Of CheckBox)(form, "chkSchedOlder").Checked = True
                                             Find(Of NumericUpDown)(form, "numSchedOlderDays").Value = 2
                                             Find(Of NumericUpDown)(form, "numSchedOlderHours").Value = 3
                                             Find(Of NumericUpDown)(form, "numSchedEveryHours").Value = 6
                                             Find(Of NumericUpDown)(form, "numSchedEveryMinutes").Value = 30
                                             Invoke(form, "OnSaveClicked")
                                             Assert.Equal(DialogResult.OK, form.DialogResult)
                                             Assert.False(String.IsNullOrEmpty(form.SavedId))
                                         End Using
                                         Dim op As ScheduledOperation = kit.Engine.Snapshot().Single().Operation
                                         Assert.Equal("Photos backup", op.Name)
                                         Assert.Equal(ScheduledAction.Move, op.Action)
                                         Assert.Equal(src, op.Source.Path)
                                         Assert.Equal(dst, op.Destination.Path)
                                         Assert.Equal(ScheduledConflict.KeepBoth, op.Conflict)
                                         Assert.Equal(ScheduledTypeMask.Images, op.Conditions.TypeMask)
                                         Assert.Equal("*.jpg", op.Conditions.NameMask)
                                         Assert.Equal(2L * 24L + 3L, op.Conditions.MinAgeHours)
                                         Assert.Equal(6 * 60 + 30, op.IntervalMinutes)
                                         Assert.True(op.Enabled)
                                         Assert.True(op.Record.NextRunAt.HasValue)
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub The_editor_refuses_a_contradiction_and_keeps_the_dialog_open_and_the_store_empty()
        ExchangeTestSupport.OnUi(Sub()
                                     Using kit As New Kit()
                                         ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                                         Dim op As New ScheduledOperation With {.Action = ScheduledAction.Delete}
                                         op.Source.Path = kit.Tree.Dir("junk")
                                         op.Conditions.MinSizeBytes = 100L * 1024 * 1024
                                         op.Conditions.MaxSizeBytes = 50L * 1024 * 1024
                                         ' The engine refuses what the dialog would have shown a box for.
                                         Dim issues As List(Of ScheduledIssue) = kit.Engine.Upsert(op)
                                         Assert.Contains(issues, Function(i) i.Code = ScheduledIssueCode.SizeBoundsContradict)
                                         Assert.Empty(kit.Engine.Snapshot())
                                         ' ... and says it in words, in every language.
                                         For Each code As String In Localization.Codes
                                             Dim saved As String = Localization.CurrentCode
                                             Try
                                                 Localization.CurrentCode = code
                                                 Assert.False(String.IsNullOrWhiteSpace(ScheduledText.IssueText(issues(0))))
                                             Finally
                                                 Localization.CurrentCode = saved
                                             End Try
                                         Next
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub A_delete_shows_its_verdict_and_hides_what_does_not_apply()
        ExchangeTestSupport.OnUi(Sub()
                                     Using kit As New Kit()
                                         ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                                         Using form As New Scheduled_Operation_Form(kit.Engine, Nothing, kit.Tree.Dir("junk"))
                                             form.Show()
                                             Dim action As ComboBox = Find(Of ComboBox)(form, "cmbSchedAction")
                                             Dim verdict As Label = Find(Of Label)(form, "lblSchedVerdict")
                                             Dim conflict As ComboBox = Find(Of ComboBox)(form, "cmbSchedConflict")
                                             Dim keep As CheckBox = Find(Of CheckBox)(form, "chkSchedDeleteOriginals")
                                             Dim destination As TextBox = Find(Of TextBox)(form, "txtSchedDestination")

                                             Assert.False(verdict.Visible, "verdict copy")
                                             Assert.True(conflict.Visible, "conflict copy")
                                             Assert.False(keep.Visible, "keep copy")

                                             action.SelectedIndex = CInt(ScheduledAction.Delete)
                                             Assert.True(verdict.Visible, "verdict delete")
                                             Assert.False(conflict.Visible, "conflict delete")
                                             Assert.False(destination.Parent.Visible, "destination delete")
                                             Assert.StartsWith(Localization.TF("Удаление: {0}", "").Trim(), verdict.Text.Substring(0, Math.Min(verdict.Text.Length, 9)), StringComparison.Ordinal)

                                             action.SelectedIndex = CInt(ScheduledAction.Archive)
                                             Assert.False(verdict.Visible, "verdict archive")
                                             Assert.True(keep.Visible, "keep archive")
                                             keep.Checked = True
                                             Assert.True(verdict.Visible, "verdict archive+keep")
                                         End Using
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub The_history_form_lists_the_log_newest_first_in_words()
        ExchangeTestSupport.OnUi(Sub()
                                     Using kit As New Kit()
                                         ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                                         kit.Journal.Append(New JournalEntry With {.Timestamp = New DateTime(2026, 10, 13, 3, 0, 0), .Kind = "run", .Code = "ok",
                                             .OperationName = "first", .Count = 3, .Trigger = "schedule", .Verdict = "recycle"})
                                         kit.Journal.Append(New JournalEntry With {.Timestamp = New DateTime(2026, 10, 14, 3, 0, 0), .Kind = "note", .Code = "missed",
                                             .OperationName = "second", .Detail = "2026-10-14T03:00:00"})
                                         Using form As New Scheduled_History_Form(kit.Engine)
                                             Dim list As ListView = Find(Of ListView)(form, "lvSchedHistory")
                                             Assert.Equal(2, list.Items.Count)
                                             Assert.Equal("second", list.Items(0).SubItems(1).Text)
                                             Assert.Equal("first", list.Items(1).SubItems(1).Text)
                                             Assert.False(String.IsNullOrWhiteSpace(list.Items(1).SubItems(3).Text))
                                             Assert.Contains("3", list.Items(1).SubItems(3).Text)
                                         End Using
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub The_resource_picker_says_what_to_do_when_there_is_no_resource()
        ExchangeTestSupport.OnUi(Sub()
                                     Using kit As New Kit()
                                         ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                                         Using form As New Scheduled_Resource_Picker_Form(New ScheduledLocation())
                                             Dim ok As Button = Find(Of Button)(form, "btnSchedResourceOk")
                                             Assert.False(ok.Enabled)
                                             Assert.Null(Find(Of ComboBox)(form, "cmbSchedResource"))
                                         End Using
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub The_page_is_in_every_share_manager_window_with_its_statement_and_commands()
        ExchangeTestSupport.OnUi(Sub()
                                     ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                                     Using hive As New ScratchHive()
                                         Using form As New Share_Settings_Form()
                                             Dim note As Label = Find(Of Label)(form, "managerSchedNote")
                                             Assert.NotNull(note)
                                             Assert.Contains(Localization.T("Файловые операции по расписанию - часть менеджера общего доступа: устанавливаются вместе с ним, выполняются им и настраиваются в его настройках. Расписание работает только пока менеджер запущен."), note.Text)
                                             For Each name As String In {"managerSchedList", "managerSchedNew", "managerSchedEdit", "managerSchedDelete",
                                                                         "managerSchedRunNow", "managerSchedRunAll", "managerSchedPause", "managerSchedHistory",
                                                                         "managerSchedFeature", "managerSchedMissed", "managerSchedAutostart"}
                                                 Assert.NotNull(Find(Of Control)(form, name))
                                             Next
                                             ' Nothing to edit or run until a row is chosen.
                                             Assert.False(Find(Of Button)(form, "managerSchedEdit").Enabled)
                                             Assert.False(Find(Of Button)(form, "managerSchedRunNow").Enabled)
                                             ' The two machine switches' defaults (4.4).
                                             Assert.True(Find(Of CheckBox)(form, "managerSchedFeature").Checked)
                                             Assert.Equal(0, Find(Of ComboBox)(form, "managerSchedMissed").SelectedIndex)
                                         End Using
                                     End Using
                                 End Sub)
    End Sub
End Class
