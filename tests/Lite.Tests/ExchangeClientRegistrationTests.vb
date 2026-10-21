#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Reflection
Imports System.Text.Json
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

<Collection("AppFileLogger")>
Public Class ExchangeClientRegistrationTests
    <Theory>
    <InlineData("Abcdefgh1", -1)>
    <InlineData("Abcdefg1", 0)>
    Public Sub An_Astral_Character_Counts_As_One_Password_Character(prefix As String, expected As Integer)
        Assert.Equal(expected, ExchangePasswordVerdict.Of(prefix & Char.ConvertFromUtf32(&H1F600)))
    End Sub

    <Fact>
    Public Sub Viewer_Password_Precheck_Agrees_With_The_Server_Vectors()
        Dim root As New DirectoryInfo(AppContext.BaseDirectory)
        While root IsNot Nothing AndAlso Not File.Exists(Path.Combine(root.FullName, "FastMediaSorter.sln"))
            root = root.Parent
        End While
        Assert.NotNull(root)
        Using doc As JsonDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "exchange-server", "internal", "password", "testdata", "password-policy.json")))
            For Each row As JsonElement In doc.RootElement.EnumerateArray()
                Assert.Equal(row.GetProperty("accept").GetBoolean(), ExchangePasswordVerdict.Of(row.GetProperty("password").GetString()) = -1)
            Next
        End Using
    End Sub

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.5F)>
    <InlineData(1.75F)>
    Public Sub Switching_Into_Registration_Preserves_The_Current_Scale(factor As Single)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         Using form As New Exchange_Connect_Form("exchange.example:443", Nothing, New ExchangeConsumerPorts())
                                             form.AutoScaleMode = AutoScaleMode.None
                                             form.Scale(New SizeF(factor, factor))
                                             form.ShowInTaskbar = False
                                             form.Show()
                                             Dim input As TextBox = DirectCast(form.Controls("xc_Input"), TextBox)
                                             Assert.NotNull(input)
                                             Dim inputWidth As Integer = input.Width
                                             Dim built As ExchangeConnectionStringResult = ExchangeConnectionString.Build("exchange.example:443", "", "", "", "abcdefghijklmnopqrstuA")
                                             Dim refused As ExchangeConnectionStringResult = Nothing
                                             Dim invitation As String = ExchangeConnectionString.Format(built.Value, refused)
                                             Dim registrationTop As Integer = -1
                                             For iteration As Integer = 0 To 2
                                                 input.Text = invitation
                                                 Assert.Equal(inputWidth, input.Width)
                                                 ' The answers ride the shared bottom row since ticket 243 T-8b: the button is a child of the
                                                 ' row, found through the tree and measured in the window's own coordinates.
                                                 Dim connect As Control = form.Controls.Find("xc_Connect", True).Single()
                                                 Dim connectTop As Integer = form.RectangleToClient(connect.RectangleToScreen(connect.ClientRectangle)).Top
                                                 Assert.InRange(connectTop, CInt(320 * factor), CInt(470 * factor))
                                                 If registrationTop >= 0 Then Assert.Equal(registrationTop, connectTop)
                                                 registrationTop = connectTop
                                                 Assert.True(form.Controls("xc_Password2").Bottom <= connectTop)
                                                 input.Text = "exchange.example:443"
                                                 Assert.Equal(inputWidth, input.Width)
                                             Next
                                         End Using
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)))
        If failure IsNot Nothing Then Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub
End Class
#End If
