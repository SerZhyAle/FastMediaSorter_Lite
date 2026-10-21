#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms
#If COMPANION_TESTS Then
Imports FastMediaSorterCompanion
#End If
Imports Xunit

Public Class ExchangeMarkTests
    Private Shared Function OpenAsset(name As String) As Stream
#If COMPANION_TESTS Then
        Return CompanionAssets.OpenBundledAsset(name)
#Else
        Return RuntimeBootstrap.OpenBundledAsset(name)
#End If
    End Function

    <Theory>
    <InlineData(16, 16)>
    <InlineData(20, 20)>
    <InlineData(21, 24)>
    <InlineData(24, 24)>
    <InlineData(28, 28)>
    <InlineData(30, 30)>
    <InlineData(32, 32)>
    <InlineData(33, 36)>
    <InlineData(36, 36)>
    <InlineData(40, 40)>
    <InlineData(42, 42)>
    <InlineData(48, 48)>
    <InlineData(56, 56)>
    <InlineData(64, 64)>
    Public Sub Dpi_slots_use_bundled_artwork_without_upscaling(size As Integer, rasterSize As Integer)
        Dim requested As String = ""
        Using picture = ExchangeMark.Render(size, SystemColors.WindowText,
            Function(name)
                requested = name
                Return OpenAsset(name)
            End Function)
            Assert.Equal(New Size(size, size), picture.Size)
            If Not SystemInformation.HighContrast Then
                Assert.Equal("exchange-mark/png/mark-" & rasterSize.ToString(Globalization.CultureInfo.InvariantCulture) & ".png", requested)
                Dim colours As New Collections.Generic.HashSet(Of Integer)()
                For x = 0 To size - 1
                    For y = 0 To size - 1
                        Dim pixel = picture.GetPixel(x, y)
                        If pixel.A = 255 Then colours.Add(pixel.ToArgb())
                    Next
                Next
                Assert.True(colours.Count >= 5, "The five coloured rays must survive rendering")
            End If
        End Using
    End Sub

    <Fact>
    Public Sub Pending_catalog_controls_render_the_contract_glyph_and_keep_their_proposal()
        For Each name In {"res_DevicesAdd", "managerSettingsNav_exchange"}
            Dim meaning = IconMap.GetMeaning(name)
            Assert.Equal("", meaning.GlyphId)
            Assert.Equal(IconMap.ExchangeMarkProposal, meaning.Proposal)
            Using picture = GlyphRenderer.Render(meaning.RenderGlyphId, 32, Color.White, AddressOf OpenAsset)
                Assert.Equal(New Size(32, 32), picture.Size)
                Assert.True(Enumerable.Range(0, 32).Any(Function(x) Enumerable.Range(0, 32).Any(Function(y) picture.GetPixel(x, y).A > 128)))
            End Using
        Next
    End Sub

    <Fact>
    Public Sub Image_only_mark_uses_the_existing_localized_CW_term()
        Assert.Equal(Localization.T("Сервер обмена"), ExchangeMark.AccessibleName())
        Assert.True(Localization.All.Any(Function(row) row.Key = "Сервер обмена" AndAlso row.Value.Length = 12))
    End Sub

    <Fact>
    Public Sub Native_account_list_can_create_and_recreate_its_handle_after_the_temporary_bitmap_is_disposed()
        Dim failure As Exception = Nothing
        Dim thread As New Threading.Thread(
            Sub()
                Try
                    Using list As New ListView With {.View = View.Details, .Size = New Size(300, 100)}
                        list.Columns.Add("Account", 280)
                        list.Items.Add("alice")
                        ExchangeMark.ApplyAccounts(list, AddressOf OpenAsset)
                        Dim handle As IntPtr = list.Handle
                        Assert.NotEqual(IntPtr.Zero, handle)
                        list.ForeColor = Color.Blue
                        Using snapshot As New Bitmap(300, 100)
                            list.DrawToBitmap(snapshot, New Rectangle(0, 0, 300, 100))
                        End Using
                        Assert.Equal("exchange", list.Items(0).ImageKey)
                    End Using
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        thread.SetApartmentState(Threading.ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(10000), "The native list handle must not recurse or hang")
        If failure IsNot Nothing Then Throw failure
    End Sub

#If Not COMPANION_TESTS Then
    ''' <summary>Spec 248 V3 and V4: the two fixed-coordinate viewer dialogs name the mark by the CW term, keep it in
    ''' the reading-start padding column of their heading and leave every authored row below the heading.</summary>
    <Fact>
    Public Sub Viewer_account_dialogs_carry_the_named_mark_clear_of_their_rows()
        Dim failure As Exception = Nothing
        Dim thread As New Threading.Thread(
            Sub()
                Try
                    Using connect As New Exchange_Connect_Form("exchange.example:443", Nothing, New ExchangeConsumerPorts())
                        connect.ShowInTaskbar = False
                        connect.Show()
                        AssertMarkedHeading(connect.Controls("xc_Intro"), connect.Controls("xc_InputLabel"))
                    End Using
                    Using change As New Exchange_Password_Change_Form("user@exchange.example")
                        change.ShowInTaskbar = False
                        change.Show()
                        AssertMarkedHeading(change.Controls("xpc_Heading"), change.Controls("xpc_Old"))
                        ' The answers ride the shared bottom row since ticket 243 T-8b: the button is a child of the row, so it is found
                        ' by name through the tree and measured in the window's own coordinates.
                        Dim ok As Control = change.Controls.Find("xpc_Ok", True).Single()
                        Assert.True(change.RectangleToClient(ok.RectangleToScreen(ok.ClientRectangle)).Bottom <= change.ClientSize.Height, "The buttons must stay inside the grown window")
                    End Using
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        thread.SetApartmentState(Threading.ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(15000), "The dialogs must open and close")
        If failure IsNot Nothing Then Throw failure
    End Sub

    Private Shared Sub AssertMarkedHeading(heading As Control, nextRow As Control)
        Assert.NotNull(heading)
        Assert.NotNull(nextRow)
        Dim marks As Control() = heading.Controls.Find("exchangeMark", False)
        Assert.Single(marks)
        Dim mark As Control = marks(0)
        Assert.Equal(ExchangeMark.AccessibleName(), mark.AccessibleName)
        Assert.Equal(AccessibleRole.Graphic, mark.AccessibleRole)
        Assert.True(mark.Right <= heading.Padding.Left, "The mark lives in the reading-start column, not over the text")
        Assert.True(heading.Height >= mark.Height, "The heading is at least as tall as its mark")
        Assert.True(nextRow.Top >= heading.Bottom, "The heading must not overlap the first row")
    End Sub
#End If
End Class
#End If
