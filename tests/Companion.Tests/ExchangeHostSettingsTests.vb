Option Strict On

Imports System.Collections.Generic
Imports System.Text
Imports System.Text.Json
Imports System.Drawing
Imports System.Reflection
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

<Collection(UiLanguageCollection.Name)>
Public Class ExchangeHostSettingsTests
    <Fact>
    Public Sub Administration_rows_show_saved_values_and_preserve_pending_edits()
        ExchangeTestSupport.Reset()
        Dim dir As String = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        Try
            ExchangeProcess.ExeDirectoryOverride = dir
            ExchangeProcess.RunOverride = Function(args, stdin) New ExchangeProcess.RunResult With {.Started = True, .StdOut = "{}"}
            ExchangeAdmin.PipeNameOverride = "fms-225-no-server-" & Guid.NewGuid().ToString("N")
            ExchangeTestSupport.OnUi(Sub()
                Using form As New Share_Settings_Form(destination:=SettingsDestination.ExchangeServer) With {.Location = New Point(-6000, -6000), .StartPosition = FormStartPosition.Manual, .ShowInTaskbar = False}
                    form.Show()
                    Dim flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic
                    GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", flags).Invoke(form, New Object() {"exchange"})
                    Dim snap As New ExchangeSnapshot With {.Report = New ExchangeProcess.InspectReport With {.Answered = True, .Accounts = 1, .KeepAwake = True, .LogRetentionDays = 7}}
                    Dim apply As MethodInfo = GetType(Share_Settings_Form).GetMethod("ApplyExchangeState", flags)
                    apply.Invoke(form, New Object() {snap})
                    Dim awake As CheckBox = DirectCast(GetType(Share_Settings_Form).GetField("_chkXAwake", flags).GetValue(form), CheckBox)
                    Dim days As NumericUpDown = DirectCast(GetType(Share_Settings_Form).GetField("_numXLogDays", flags).GetValue(form), NumericUpDown)
                    Assert.True(awake.Checked)
                    Assert.Equal(7D, days.Value)
                    Assert.Equal(1D, days.Minimum)
                    days.Value = 9
                    awake.Checked = False
                    apply.Invoke(form, New Object() {snap})
                    Assert.Equal(9D, days.Value)
                    Assert.False(awake.Checked)
                End Using
            End Sub)
        Finally
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(dir)
        End Try
    End Sub
    <Fact>
    Public Sub Inspect_preserves_old_defaults_and_reads_configured_host_settings()
        Dim legacy As ExchangeProcess.InspectReport = ExchangeProcess.ParseInspect("{}", 0)
        Assert.False(legacy.KeepAwake)
        Assert.Equal(31, legacy.LogRetentionDays)
        Dim current As ExchangeProcess.InspectReport = ExchangeProcess.ParseInspect("{""keepAwake"":true,""logRetentionDays"":7}", 0)
        Assert.True(current.KeepAwake)
        Assert.Equal(7, current.LogRetentionDays)
        Dim status As ExchangeAdminStatus = ExchangeAdminParser.Status("{""keepAwakeActive"":true,""keepAwake"":false,""logRetentionDays"":7}")
        Assert.True(status.KeepAwakeActive)
        Assert.True(status.KeepAwakeReported)
        Assert.False(ExchangeAdminParser.Status("{}").KeepAwakeReported)
        Assert.False(status.KeepAwake)
        Assert.Equal(7, status.LogRetentionDays)
    End Sub

    <Fact>
    Public Sub Explicit_off_is_sent_and_missing_host_settings_are_not_in_other_requests()
        Dim request As New ExchangeAdminRequest("host.settings") With {.KeepAwake = False, .LogRetentionDays = 7}
        Using doc As JsonDocument = JsonDocument.Parse(Encoding.UTF8.GetString(request.ToJsonBytes()))
            Assert.False(doc.RootElement.GetProperty("keepAwake").GetBoolean())
            Assert.Equal(7, doc.RootElement.GetProperty("logRetentionDays").GetInt32())
        End Using
        Assert.DoesNotContain("keepAwake", Encoding.UTF8.GetString(New ExchangeAdminRequest("status").ToJsonBytes()))
    End Sub

    <Fact>
    Public Sub Retention_statement_and_host_controls_are_translated_in_all_languages()
        Dim previous As String = Localization.CurrentCode
        Dim keys As String() = {"Не давать ПК уснуть, пока сервер работает", "Хранить журналы, дней", "Применить настройки сервера",
            "Продлить сертификат с тем же ключом", "Запрос удержания от сна действует", "Запрос удержания от сна не действует"}
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                Assert.Contains("7", String.Join(" ", ExchangeStatement.Paragraphs(7)))
                Assert.DoesNotContain("31", String.Join(" ", ExchangeStatement.Paragraphs(7)))
                For Each key As String In keys
                    Dim text As String = Localization.T(key)
                    Assert.NotEmpty(text)
                    If code <> "ru" Then Assert.NotEqual(key, text)
                Next
            Next
        Finally
            Localization.CurrentCode = previous
        End Try
    End Sub
End Class
