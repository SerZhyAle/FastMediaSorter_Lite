Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

<Collection(UiLanguageCollection.Name)>
Public Class DiskAutostartDisplayTests
    Private Const Flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic

    <Fact>
    Public Sub Autostart_cells_and_owner_text_follow_the_record_in_every_language()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                Assert.Equal(Localization.T("Да"), Share_Disks_Form.AutostartText(True, True))
                Assert.Equal(Localization.T("Нет"), Share_Disks_Form.AutostartText(False, True))
                Assert.Equal("-", Share_Disks_Form.AutostartText(False, False))
                Assert.Equal("-", Share_Disks_Form.AutostartText(True, False))
                For Each state As String In {"open", "closed", "failed"}
                    Dim disk As New SharedDisk With {.State = state, .Autostart = True}
                    Assert.Equal(Localization.TF("FileDO - {0} - автозапуск", Share_Disks_Form.StateText(state)), MainWindow.DiskOwnerText(disk, True))
                    disk.Autostart = False
                    Assert.Equal(Localization.TF("FileDO - {0}", Share_Disks_Form.StateText(state)), MainWindow.DiskOwnerText(disk, True))
                    disk.Autostart = True
                    Assert.Equal(Localization.TF("FileDO - {0}", Share_Disks_Form.StateText(state)), MainWindow.DiskOwnerText(disk, False))
                Next
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Theory>
    <InlineData(96)>
    <InlineData(144)>
    <InlineData(192)>
    Public Sub Headers_and_autostart_cells_fit_the_default_width_at_scaled_font_sizes(dpi As Integer)
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                Using form As New Share_Disks_Form(), font As New Font(form.Font.FontFamily, form.Font.Size * CSng(dpi / 96.0))
                    ' Measure at the target scale without changing the desktop's DPI or settings.
                    Dim available As Integer = CInt(710 * dpi / 96.0)
                    Dim widths As Integer() = Share_Disks_Form.DiskColumnWidths(font, available, dpi, True)
                    Assert.True(widths.Sum() <= available, code & " at " & dpi & " DPI: " & widths.Sum() & " > " & available)
                    For Each text As String In {Localization.T("Автозапуск"), Localization.T("Да"), Localization.T("Нет"), "-"}
                        Assert.True(TextRenderer.MeasureText(text, font).Width + CInt(24 * dpi / 96.0) <= widths(2), code & ": " & text)
                    Next
                End Using
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub Disk_dialog_repaints_after_mutations_and_worker_restore_failures()
        RunSta(Sub()
                   Dim status As WorkerStatus = SupportedStatus()
                   Dim answer As String = DiskAnswer(True, False)
                   Using form As New Share_Disks_Form(Function() Task.FromResult(status),
                       Function() Task.FromResult(WorkerIpc.ParseResponse(answer, 2)), Sub(currentDisks) Assert.NotNull(currentDisks))
                       Prepare(form)
                       Dim notifications As Integer
                       AddHandler form.SnapshotChanged, Sub(st, disks)
                                                            Assert.Same(status, st)
                                                            notifications += 1
                                                        End Sub
                       form.Show()
                       Application.DoEvents()
                       Dim list As ListView = Field(Of ListView)(form, "_list")
                       Assert.Equal(5, list.Columns.Count)
                       Assert.Equal(Localization.T("Автозапуск"), list.Columns(2).Text)
                       AssertCells(list, True, False)
                       Assert.True(Field(Of Button)(form, "_auto").Enabled)
                       Assert.True(list.Height < 120 * form.DeviceDpi / 96.0, "The two-row list must fit its rows.")

                       ' A mutation is followed by the same reconciliation used by every action.
                       form.RunAsync(Function()
                                         answer = DiskAnswer(True, True)
                                         Return Task.CompletedTask
                                     End Function).GetAwaiter().GetResult()
                       AssertCells(list, True, True)
                       answer = DiskAnswer(False, True)
                       form.RunAsync(Function() Task.CompletedTask).GetAwaiter().GetResult()
                       AssertCells(list, False, True)

                       ' A rejected stored key clears the worker's flag; keep its error in the note.
                       answer = DiskAnswer(False, True, "failed", "stored key rejected\nretry")
                       form.RunAsync(Function() Task.CompletedTask).GetAwaiter().GetResult()
                       AssertCells(list, False, True)
                       Assert.Equal(Share_Disks_Form.StateText("failed"), list.Items(0).SubItems(1).Text)
                       Assert.Contains("stored key rejected retry", Field(Of Label)(form, "_note").Text)

                       ' A configured flag can remain true even while the disk is closed or failed.
                       answer = DiskAnswer(True, False, "closed", "restore failed")
                       form.RunAsync(Function() Task.CompletedTask).GetAwaiter().GetResult()
                       AssertCells(list, True, False)
                       Assert.Equal(Share_Disks_Form.StateText("closed"), list.Items(0).SubItems(1).Text)
                       Assert.Contains("restore failed", Field(Of Label)(form, "_note").Text)

                       answer = "{""schemaVersion"":2,""ok"":true}"
                       form.RunAsync(Function() Task.CompletedTask).GetAwaiter().GetResult()
                       Assert.Empty(list.Items.Cast(Of ListViewItem)())
                       answer = DiskAnswer(False, True)
                       form.RunAsync(Function() Task.CompletedTask).GetAwaiter().GetResult()
                       AssertCells(list, False, True)
                       Assert.Equal(7, notifications)

                       status.Capabilities("disk-autostart") = False
                       form.RunAsync(Function() Task.CompletedTask).GetAwaiter().GetResult()
                       Assert.All(list.Items.Cast(Of ListViewItem)(), Sub(row) Assert.Equal("-", row.SubItems(2).Text))
                       Assert.Contains(Localization.TF("Доступ: {0}; открытых файлов: {1}; автозапуск: {2}. {3}",
                           Localization.T("Чтение и запись"), 0, "-", ""), Field(Of Label)(form, "_note").Text)
                   End Using
               End Sub)
    End Sub

    <Fact>
    Public Sub Disk_dialog_bounds_long_lists_by_the_space_above_its_actions()
        RunSta(Sub()
                   Dim status As WorkerStatus = SupportedStatus()
                   Dim disks As List(Of SharedDisk) = Enumerable.Range(1, 20).Select(
                       Function(i) New SharedDisk With {.ContainerId = i.ToString(), .RootName = "Disk " & i,
                           .State = "closed", .Holder = "none", .Autostart = i Mod 2 = 0}).ToList()
                   Using form As New Share_Disks_Form(Function() Task.FromResult(status),
                       Function() Task.FromResult(New WorkerResponse With {.Ok = True, .SchemaVersion = 2, .Disks = disks}),
                       Sub(currentDisks) Assert.NotNull(currentDisks))
                       Prepare(form)
                       form.Show()
                       For Each height As Integer In {480, 400, 580}
                           form.ClientSize = New Size(CInt(740 * form.DeviceDpi / 96.0), CInt(height * form.DeviceDpi / 96.0))
                           Application.DoEvents()
                           Dim list As ListView = Field(Of ListView)(form, "_list")
                           Assert.True(list.Columns.Cast(Of ColumnHeader)().Sum(Function(c) c.Width) <= list.ClientSize.Width)
                           Dim note As Label = Field(Of Label)(form, "_note")
                           Dim noteBounds As Rectangle = form.RectangleToClient(note.RectangleToScreen(note.ClientRectangle))
                           Dim footer As DialogButtonRow = Field(Of DialogButtonRow)(form, "_answerRow")
                           Assert.True(noteBounds.Bottom <= footer.Top, "The table must leave room for the note and footer.")
                       Next
                   End Using
               End Sub)
    End Sub

    <Fact>
    Public Sub Main_list_and_menu_read_the_same_refreshed_snapshot()
        RunSta(Sub()
                   Using window As New MainWindow()
                       Dim status As WorkerStatus = SupportedStatus()
                       status.Roots = New List(Of ShareFolder) From {
                           New ShareFolder With {.name = "one", .owner = "filedo", .hostPath = "C:\one.fdd"},
                           New ShareFolder With {.name = "two", .owner = "filedo", .hostPath = "C:\two.fdd"}}
                       GetType(MainWindow).GetField("_status", Flags).SetValue(window, status)
                       Dim list As ListView = Field(Of ListView)(window, "lvFolders")
                       Dim unused = list.Handle
                       For Each enabled As Boolean In {True, False}
                           Dim disks As List(Of SharedDisk) = WorkerIpc.ParseResponse(DiskAnswer(enabled, False), 2).Disks
                           GetType(MainWindow).GetField("_diskSnapshot", Flags).SetValue(window, disks)
                           GetType(MainWindow).GetMethod("RefreshExternalRoots", Flags).Invoke(window, Nothing)
                           Assert.Equal(MainWindow.DiskOwnerText(disks(0), True), list.Items(0).SubItems(1).Text)
                           Assert.Equal(MainWindow.DiskOwnerText(disks(1), True), list.Items(1).SubItems(1).Text)
                           list.Items(0).Selected = True
                           GetType(MainWindow).GetMethod("OnDiskMenuOpening", Flags).Invoke(window, {Nothing, New System.ComponentModel.CancelEventArgs()})
                           Dim menu As ContextMenuStrip = Field(Of ContextMenuStrip)(window, "_diskMenu")
                           Dim auto As ToolStripMenuItem = menu.Items.OfType(Of ToolStripMenuItem)().Single(Function(item) item.Text = Localization.T("Автозапуск.."))
                           Assert.Equal(enabled, auto.Checked)
                           Assert.True(list.Columns(1).Width >= TextRenderer.MeasureText(list.Items(0).SubItems(1).Text, list.Font).Width)
                       Next
                   End Using
               End Sub)
    End Sub

    Private Shared Function SupportedStatus() As WorkerStatus
        Return New WorkerStatus With {.Capabilities = New Dictionary(Of String, Boolean) From {{"disk-sharing", True}, {"disk-autostart", True}}}
    End Function

    Private Shared Function DiskAnswer(first As Boolean, second As Boolean, Optional state As String = "open", Optional errorText As String = "") As String
        Return "{""schemaVersion"":2,""ok"":true,""disks"":[{""containerId"":""one"",""rootName"":""one"",""state"":""" & state &
            """,""holder"":""none"",""autostart"":" & first.ToString().ToLowerInvariant() & ",""errorMessage"":""" & errorText &
            """},{""containerId"":""two"",""rootName"":""two"",""state"":""closed"",""holder"":""none"",""autostart"":" & second.ToString().ToLowerInvariant() & "}]}"
    End Function

    Private Shared Sub AssertCells(list As ListView, first As Boolean, second As Boolean)
        Assert.Equal(2, list.Items.Count)
        Assert.Equal(Share_Disks_Form.AutostartText(first, True), list.Items(0).SubItems(2).Text)
        Assert.Equal(Share_Disks_Form.AutostartText(second, True), list.Items(1).SubItems(2).Text)
    End Sub

    Private Shared Function Field(Of T)(form As Form, name As String) As T
        Return DirectCast(form.GetType().GetField(name, Flags).GetValue(form), T)
    End Function

    Private Shared Sub Prepare(form As Form)
        form.ShowInTaskbar = False
        form.StartPosition = FormStartPosition.Manual
        form.Location = New Point(-5000, -5000)
    End Sub

    Private Shared Sub RunSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Disk display check did not finish.")
        If failure IsNot Nothing Then Throw failure
    End Sub
End Class
