Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The accounts and devices of the "Media Exchange Server" group (ticket 168 sections 3.1-3.2) driven headlessly with a stand-in binary: the
''' lists fill from the admin channel's offline path, an account selected shows its devices, a device selected offers Revoke, and a refresh -
''' the poll, an action's refresh - never loses either selection. Two defects the first hand run of this window would have met are held here:
''' rebuilding a ListView's items from inside its own selection event (a NullReferenceException in ListViewItem.Selected) and a read whose
''' continuation resumes off the window's thread.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeAdminGroupUiTests
    Implements IDisposable

    Private ReadOnly _dir As String

    Public Sub New()
        ExchangeTestSupport.Reset()
        _dir = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        ExchangeProcess.ExeDirectoryOverride = _dir
        ExchangeProcess.RunOverride = AddressOf StandIn
        ' no server runs: the pipe does not exist and every call goes through the binary's offline verbs
        ExchangeAdmin.PipeNameOverride = "fms-exchange-admin-test-none-" & Guid.NewGuid().ToString("N").Substring(0, 12)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        ExchangeTestSupport.Reset()
        ExchangeTestSupport.DeleteQuietly(_dir)
    End Sub

    Private Shared Function StandIn(args As String(), stdin As String) As ExchangeProcess.RunResult
        Dim r As New ExchangeProcess.RunResult With {.Started = True}
        Select Case args(0)
            Case "inspect"
                r.StdOut = "{""dataDir"":""C:\\fms-test-no-such-dir"",""configPresent"":true,""accounts"":2,""devices"":3,""certPresent"":true,""fingerprint"":""SHA256:abc"",""listen"":"":22022"",""portRange"":""40000-40100"",""publicEndpoint"":""""}"
            Case "account"
                r.StdOut = "[{""login"":""alice"",""createdAt"":""2026-10-07T10:00:00Z"",""devices"":2},{""login"":""bob"",""createdAt"":""2026-10-07T11:30:00Z"",""devices"":0}]"
            Case "device"
                r.StdOut = If(args.Contains("alice"),
                    "[{""deviceId"":""AAAAAAAAAAAAAAAAAAAAAA"",""deviceName"":""OFFICE-PC"",""product"":""fms-windows"",""enrolledAt"":""e"",""lastSeen"":""2026-10-07T12:00:00Z"",""online"":true}," &
                    "{""deviceId"":""BBBBBBBBBBBBBBBBBBBBBB"",""deviceName"":""Phone"",""product"":""fms-android"",""enrolledAt"":""e"",""lastSeen"":"""",""online"":false}]", "[]")
        End Select
        Return r
    End Function

    Private Shared Function Field(Of T)(obj As Object, name As String) As T
        Return DirectCast(obj.GetType().GetField(name, BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(obj), T)
    End Function

    Private Shared Sub Pump(until As Func(Of Boolean), what As String)
        Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(15)
        While DateTime.UtcNow < deadline AndAlso Not until()
            Application.DoEvents()
            Thread.Sleep(15)
        End While
        Assert.True(until(), "timed out waiting for " & what)
    End Sub

    Private Shared Sub Refresh(form As Share_Settings_Form)
        Dim t As Threading.Tasks.Task = DirectCast(GetType(Share_Settings_Form).GetMethod("RefreshAccountsAsync", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, Nothing), Threading.Tasks.Task)
        Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(15)
        While Not t.IsCompleted AndAlso DateTime.UtcNow < deadline
            Application.DoEvents()
            Thread.Sleep(15)
        End While
        Assert.True(t.IsCompleted, "the refresh did not end")
    End Sub

    <Fact>
    Public Sub An_account_selected_shows_its_devices_a_device_selected_offers_revoke_and_a_refresh_keeps_both_selections()
        ExchangeTestSupport.OnUi(
            Sub()
                Using form As New Share_Settings_Form()
                    form.StartPosition = FormStartPosition.Manual
                    form.Location = New Point(-6000, -6000)
                    form.ShowInTaskbar = False
                    form.Show()
                    GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, New Object() {"exchange"})
                    Dim accounts As ListView = Field(Of ListView)(form, "_lvXAccounts")
                    Dim devices As ListView = Field(Of ListView)(form, "_lvXDevices")
                    Dim reset As Button = Field(Of Button)(form, "_btnXResetPw")
                    Dim remove As Button = Field(Of Button)(form, "_btnXRemoveAccount")
                    Dim revoke As Button = Field(Of Button)(form, "_btnXRevoke")
                    Dim add As Button = Field(Of Button)(form, "_btnXAddAccount")

                    Pump(Function() accounts.Items.Count = 2, "the accounts list")
                    Assert.Equal({"alice", "bob"}, accounts.Items.Cast(Of ListViewItem)().Select(Function(i) i.Text).ToArray())
                    Assert.True(add.Visible)
                    Assert.False(reset.Visible OrElse remove.Visible, "nothing is selected: nothing to reset or remove")
                    Assert.False(devices.Visible)
                    Assert.True(Field(Of Label)(form, "_lblXAccOffline").Visible, "no server runs: the note says a change applies when it starts")

                    ' selecting an account (programmatically here, by a click in life) must not throw and shows its devices
                    accounts.Handle.ToString()
                    accounts.Items(0).Selected = True
                    Pump(Function() devices.Items.Count = 2, "the devices of alice")
                    Assert.True(devices.Visible)
                    Assert.True(reset.Visible AndAlso remove.Visible)
                    Assert.Contains("alice", Field(Of Label)(form, "_lblXDevTitle").Text)
                    Assert.False(revoke.Visible, "no device is selected")
                    Assert.Equal("OFFICE-PC", devices.Items(0).Text)
                    Assert.Equal("Phone", devices.Items(1).Text)

                    devices.Handle.ToString()
                    devices.Items(1).Selected = True
                    Application.DoEvents()
                    Assert.True(revoke.Visible)

                    ' a refresh (the poll, or an action's) changes no row: both selections stay
                    Refresh(form)
                    Refresh(form)
                    Assert.Equal(1, accounts.SelectedItems.Count)
                    Assert.Equal("alice", accounts.SelectedItems(0).Text)
                    Assert.Equal(1, devices.SelectedItems.Count)
                    Assert.Equal("Phone", devices.SelectedItems(0).Text)
                    Assert.True(revoke.Visible)

                    ' another account: its devices replace them (bob has none, so the devices block goes), the revoke button with them
                    accounts.Items(1).Selected = True
                    Pump(Function() Not devices.Visible, "the devices of bob to be hidden")
                    Assert.False(revoke.Visible)
                    Assert.True(reset.Visible AndAlso remove.Visible)
                    form.Hide()
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub The_wizard_button_is_not_drawn_when_accounts_exist_and_the_group_shows_the_totals()
        ExchangeTestSupport.OnUi(
            Sub()
                Using form As New Share_Settings_Form()
                    form.StartPosition = FormStartPosition.Manual
                    form.Location = New Point(-6000, -6000)
                    form.ShowInTaskbar = False
                    form.Show()
                    Dim accounts As ListView = Field(Of ListView)(form, "_lvXAccounts")
                    GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, New Object() {"exchange"})
                    Pump(Function() accounts.Items.Count = 2, "the accounts list")
                    Assert.False(Field(Of FlowLayoutPanel)(form, "_rowXWizard").Visible, "accounts exist: the wizard is for a PC where nothing exists yet")
                    Assert.Equal(ExchangeAdminText.Totals(2, 2), Field(Of Label)(form, "_lblXAccTotals").Text)
                    Dim pages = Field(Of Dictionary(Of String, Panel))(form, "_pages")
                    Assert.True(pages("exchange").DisplayRectangle.Height > pages("exchange").ClientSize.Height)
                    Dim capture = Environment.GetEnvironmentVariable("FMS_SETTINGS_CAPTURE")
                    If Not String.IsNullOrEmpty(capture) Then
                        IO.Directory.CreateDirectory(capture)
                        Using bitmap As New Bitmap(form.Width, form.Height)
                            form.DrawToBitmap(bitmap, New Rectangle(Point.Empty, bitmap.Size))
                            bitmap.Save(IO.Path.Combine(capture, "exchange.png"))
                        End Using
                    End If
                    form.Hide()
                End Using
            End Sub)
    End Sub

    ''' <summary>A new row or a gone row rebuilds the list and puts the selection back; the person's login stays selected when a different one
    ''' is added.</summary>
    <Fact>
    Public Sub A_changed_set_of_accounts_rebuilds_the_list_and_keeps_the_selected_login()
        ExchangeTestSupport.OnUi(
            Sub()
                Using form As New Share_Settings_Form()
                    form.StartPosition = FormStartPosition.Manual
                    form.Location = New Point(-6000, -6000)
                    form.ShowInTaskbar = False
                    form.Show()
                    Dim accounts As ListView = Field(Of ListView)(form, "_lvXAccounts")
                    GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, New Object() {"exchange"})
                    Pump(Function() accounts.Items.Count = 2, "the accounts list")
                    accounts.Handle.ToString()
                    accounts.Items(1).Selected = True
                    Application.DoEvents()
                    ' a third account appears on the server
                    ExchangeProcess.RunOverride = Function(args, stdin)
                                                      Dim r As ExchangeProcess.RunResult = StandIn(args, stdin)
                                                      If args(0) = "account" Then r.StdOut = "[{""login"":""alice"",""createdAt"":"""",""devices"":2},{""login"":""bob"",""createdAt"":"""",""devices"":0},{""login"":""carol"",""createdAt"":"""",""devices"":0}]"
                                                      Return r
                                                  End Function
                    Refresh(form)
                    Pump(Function() accounts.Items.Count = 3, "the third account")
                    Assert.Equal(1, accounts.SelectedItems.Count)
                    Assert.Equal("bob", accounts.SelectedItems(0).Text)
                    form.Hide()
                End Using
            End Sub)
    End Sub

End Class
