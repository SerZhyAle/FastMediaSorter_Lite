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

    ''' <summary>Spec 248 V1, V5 and V7: the "My devices" heading and the diagnosis heading wear the named mark, and an
    ''' account header of the panel draws its 16 px mark at the reading-start edge (left, or right in the right-to-left
    ''' languages) and nothing at the opposite edge: the picture is never mirrored.</summary>
    <Fact>
    Public Sub Viewer_panel_heading_diagnosis_heading_and_account_header_carry_the_mark()
        Dim failure As Exception = Nothing
        Dim thread As New Threading.Thread(
            Sub()
                Dim previousProbe As Func(Of Boolean) = ExchangeFeatures.ComponentProbeOverride
                ExchangeFeatures.ComponentProbeOverride = Function() True
                Try
                    Dim store As New ExchangeEntryStore(New ExchangeStoresTests.MemorySettings(),
                        Path.Combine(Path.GetTempPath(), "fms-mark-panel-" & Guid.NewGuid().ToString("N")))
                    Dim entry As New ExchangeEntry(ExchangeEntryStore.NewEntryId(), "127.0.0.1:22022", "alice", "",
                        ExchangeCertificateKind.CaSigned, New String("D"c, 21) & "A", "Test viewer", "Test server", False)
                    Assert.True(store.Upsert(entry))
                    Using hub As New ExchangeEntryHub(New ExchangeConsumerPorts With {.Entries = store})
                        Using panel As New Exchange_Devices_Panel(hub, store, Function() New Collections.Generic.List(Of ResourceDescriptor)(),
                                                                 Function(r) False, Sub(r) Return)
                            Dim title As Control = panel.Controls.Find("res_DevicesTitle", True).Single()
                            Assert.Single(title.Controls.Find("exchangeMark", False))
                            Assert.Equal(ExchangeMark.AccessibleName(), title.Controls.Find("exchangeMark", False)(0).AccessibleName)
                            panel.EntryAdded(entry)
                            Dim flags As Reflection.BindingFlags = Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic
                            GetType(Exchange_Devices_Panel).GetMethod("Render", flags).Invoke(panel, Nothing)
                            Dim list As ListView = DirectCast(GetType(Exchange_Devices_Panel).GetField("_list", flags).GetValue(panel), ListView)
                            Assert.True(list.Items.Count > 0, "The account header row must exist")
                            For Each rtl In {False, True}
                                panel.RightToLeft = If(rtl, RightToLeft.Yes, RightToLeft.No)
                                AssertHeaderMark(panel, list, list.Items(0), rtl)
                            Next
                        End Using
                    End Using
                    Using diagnosis As New Connection_Diagnosis_Dialog(Nothing)
                        Dim marks As Control() = diagnosis.Controls.Find("exchangeMark", True)
                        Assert.NotEmpty(marks)
                        Assert.All(marks, Sub(mark) Assert.Equal(ExchangeMark.AccessibleName(), mark.AccessibleName))
                    End Using
                Catch ex As Exception
                    failure = ex
                Finally
                    ExchangeFeatures.ComponentProbeOverride = previousProbe
                End Try
            End Sub)
        thread.SetApartmentState(Threading.ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(20000), "The panel must open and close")
        If failure IsNot Nothing Then Throw failure
    End Sub

    ''' <summary>Spec 248 S1, S2 and S5: the server program's icon comes from the vendored catalog ICO through the version
    ''' resource at build time (not patched in afterwards), staging copies the built pair untouched, and the collector shortcut
    ''' of the Server installer takes its icon from the installed server program.</summary>
    <Fact>
    Public Sub Server_program_icon_is_built_in_and_the_server_shortcut_carries_it()
        Dim root As String = Nothing
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing AndAlso root Is Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then root = dir.FullName
            dir = dir.Parent
        End While
        Assert.NotNull(root)

        Dim ico As String = Path.Combine(root, "assets", "exchange-mark", "ico", "exchange-server.ico")
        Dim header As Byte() = File.ReadAllBytes(ico).Take(4).ToArray()
        Assert.Equal(New Byte() {0, 0, 1, 0}, header)

        Dim versionInfo As String = File.ReadAllText(Path.Combine(root, "exchange-server", "versioninfo.json"))
        Assert.Contains("""IconPath"": ""../assets/exchange-mark/ico/exchange-server.ico""", versionInfo, StringComparison.Ordinal)

        Dim build As String = File.ReadAllText(Path.Combine(root, "exchange-server", "build.ps1"))
        ' goversioninfo resolves a relative IconPath against the folder it runs in (a temp folder), so the build passes an absolute path
        ' once, in the one argument list that both the online and the offline-proxy attempts use.
        Assert.Contains("'-icon', (Join-Path (Split-Path $ModuleDir -Parent) 'assets/exchange-mark/ico/exchange-server.ico')", build, StringComparison.Ordinal)
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(build, "'-icon'").Count)

        Dim staging As String = File.ReadAllText(Path.Combine(root, "tools", "Stage-ExchangePayload.ps1"))
        Assert.Contains("Copy-Item -LiteralPath $exe, $sha -Destination $dest -Force", staging, StringComparison.Ordinal)

        Dim serverSetup As String = File.ReadAllText(Path.Combine(root, "publishing", "installer", "FastMediaSorterServer.iss"))
        Dim collector As String = serverSetup.Split({vbLf}, StringSplitOptions.None).Single(Function(l) l.Contains("{cm:CollectStateIcon}", StringComparison.Ordinal) AndAlso l.StartsWith("Name:", StringComparison.Ordinal))
        Assert.Contains("IconFilename: ""{app}\exchange\fms-exchange-server.exe""", collector, StringComparison.Ordinal)
    End Sub

    ''' <summary>Draws the header row the way the list does and counts the coloured pixels in the mark's own box at each edge.
    ''' The box is clear of the row text (the text starts 24 px after the mark's left edge, or ends 24 px before it).</summary>
    Private Shared Sub AssertHeaderMark(panel As Exchange_Devices_Panel, list As ListView, item As ListViewItem, rtl As Boolean)
        Const width As Integer = 320
        Const height As Integer = 28
        Using canvas As New Bitmap(width, height)
            Using g As Graphics = Graphics.FromImage(canvas)
                Dim args As New DrawListViewItemEventArgs(g, item, New Rectangle(0, 0, width, height), 0, ListViewItemStates.Default)
                GetType(Exchange_Devices_Panel).GetMethod("OnDrawItem", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic).
                    Invoke(panel, New Object() {list, args})
            End Using
            Dim leftBox As Integer = ColouredPixels(canvas, 6, 6 + 16)
            Dim rightBox As Integer = ColouredPixels(canvas, width - 6 - 16, width - 6)
            If rtl Then
                Assert.True(rightBox >= 8, "The mark must stand at the right edge in a right-to-left language")
                Assert.Equal(0, leftBox)
            Else
                Assert.True(leftBox >= 8, "The mark must stand at the left edge in a left-to-right language")
                Assert.Equal(0, rightBox)
            End If
        End Using
    End Sub

    Private Shared Function ColouredPixels(canvas As Bitmap, fromX As Integer, toX As Integer) As Integer
        Dim count As Integer = 0
        For x As Integer = fromX To toX - 1
            For y As Integer = 0 To canvas.Height - 1
                Dim p As Color = canvas.GetPixel(x, y)
                If Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) > 48 Then count += 1
            Next
        Next
        Return count
    End Function

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
