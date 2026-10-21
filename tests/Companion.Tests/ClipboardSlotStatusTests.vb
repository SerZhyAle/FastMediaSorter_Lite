Option Strict On

Imports System.Linq
Imports System.Drawing
Imports System.IO
Imports System.Reflection
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

<Collection(UiLanguageCollection.Name)>
Public Class ClipboardSlotStatusTests
    <Fact>
    Public Async Function Refresh_uses_device_metadata_without_downloading_or_touching_the_local_clipboard() As Task
        Using populated As New ClipboardTestServer(), empty As New ClipboardTestServer(), rig As New ClipboardTestRig()
            populated.Stored = Encoding.UTF8.GetBytes("private value")
            rig.Targets.Add(ClipboardTestKit.TargetOf(populated, "Home"))
            rig.Targets.Add(ClipboardTestKit.TargetOf(empty, "Other"))
            Dim slots As IReadOnlyList(Of ClipboardSlotStatus) = Await rig.Exchange().QueryClipboardSizeAsync(ClipboardTestKit.Token())
            Assert.Equal(2, slots.Count)
            Assert.Equal("Home", slots(0).Label)
            Assert.True(slots(0).Result.IsDone)
            Assert.True(slots(0).Result.Value.Size >= populated.Stored.LongLength)
            Assert.Equal(ExchangeClipboardOutcome.Empty, slots(1).Result.Outcome)
            Assert.Equal(1, populated.Infos)
            Assert.Equal(1, empty.Infos)
            Assert.Equal(0, populated.Gets)
            Assert.Equal(0, empty.Gets)
            Assert.Equal(0, populated.Puts)
            Assert.Equal(0, rig.Port.Reads)
            Assert.Equal(0, rig.Port.Places)
            Assert.DoesNotContain("private value", String.Join(" ", slots.Select(Function(s) s.DisplayText())))
        End Using
    End Function

    <Fact>
    Public Async Function Refresh_with_no_accounts_opens_no_clipboard() As Task
        Using rig As New ClipboardTestRig()
            Assert.Empty(Await rig.Exchange().QueryClipboardSizeAsync(ClipboardTestKit.Token()))
            Assert.Equal(0, rig.Port.Reads)
            Assert.Equal(0, rig.Port.Places)
        End Using
    End Function

    <Fact>
    Public Sub Settings_refresh_button_updates_the_visible_size_and_preserves_the_clipboard()
        ExchangeTestSupport.OnUi(
            Sub()
                Using server As New ClipboardTestServer(), rig As New ClipboardTestRig(), form As New Share_Settings_Form()
                    server.Stored = Encoding.UTF8.GetBytes("size probe")
                    rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Test account"))
                    Dim flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic
                    GetType(Share_Settings_Form).GetField("_clipMetadata", flags).SetValue(form, rig.Exchange())
                    form.StartPosition = FormStartPosition.Manual
                    form.Location = New Point(-6000, -6000)
                    form.ShowInTaskbar = False
                    form.Show()
                    form.SelectSettingsPage("exchange-account")
                    Dim pageGroups = DirectCast(GetType(Share_Settings_Form).GetField("_pageGroups", flags).GetValue(form),
                        Dictionary(Of String, List(Of Share_Settings_Form.SettingsNavGroup)))
                    If pageGroups.ContainsKey("exchange-account") Then
                        For Each grp In pageGroups("exchange-account")
                            grp.Expanded = True
                        Next
                    End If
                    Dim rows = DirectCast(GetType(Share_Settings_Form).GetField("_clipRows", flags).GetValue(form), List(Of Control))
                    For Each row As Control In rows
                        row.Visible = True
                    Next
                    Dim refresh = DirectCast(GetType(Share_Settings_Form).GetField("_clipRefresh", flags).GetValue(form), Button)
                    Dim size = DirectCast(GetType(Share_Settings_Form).GetField("_clipSize", flags).GetValue(form), Label)
                    refresh.PerformClick()
                    Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(15)
                    While DateTime.UtcNow < deadline AndAlso Not size.Text.Contains("Test account", StringComparison.Ordinal)
                        Application.DoEvents()
                        Thread.Sleep(10)
                    End While
                    Assert.Contains("Test account", size.Text)
                    Assert.Equal(1, server.Infos)
                    Assert.Equal(0, server.Gets)
                    Assert.Equal(0, rig.Port.Reads)
                    Assert.Equal(0, rig.Port.Places)
                    Dim screenshot As String = Path.Combine(FindRoot(), "temp", "247", "clipboard-settings.png")
                    Directory.CreateDirectory(Path.GetDirectoryName(screenshot))
                    Using bitmap As New Bitmap(form.Width, form.Height)
                        form.DrawToBitmap(bitmap, form.ClientRectangle)
                        bitmap.Save(screenshot)
                    End Using
                    form.Close()
                End Using
            End Sub)
    End Sub

    Private Shared Function FindRoot() As String
        Dim current As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)
        While current IsNot Nothing
            If File.Exists(Path.Combine(current.FullName, "CLAUDE.md")) Then Return current.FullName
            current = current.Parent
        End While
        Throw New IOException("Repository root not found.")
    End Function
End Class
