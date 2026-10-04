#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

Public Class ChromeBehaviorTests
    Private Shared Sub OnSta(action As Action)
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
        Assert.True(thread.Join(10000), "Chrome check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    <Fact>
    Public Sub Overflow_preserves_actions_order_and_the_row_automation_name()
        OnSta(Sub()
                  Using surface As New ChromeSurface()
                      surface.Size = New Size(128, 44)
                      surface.OverflowId = "btn_Overflow_2"
                      Dim invoked As Integer = 0
                      Dim items As New List(Of ChromeItem)()
                      For index As Integer = 0 To 4
                          Dim selected As Integer = index
                          items.Add(New ChromeItem("button" & index.ToString(), ChromeItemKind.Button) With {.Action = Sub() invoked = selected})
                      Next
                      surface.SetItems(items)
                      Assert.Equal(New String() {"button2", "button3", "button4"}, surface.OverflowItems.Select(Function(item) item.Id).ToArray())
                      Assert.Equal("btn_Overflow_2", surface.Items.Last().Id)
                      surface.OverflowItems.Last().Action.Invoke()
                      Assert.Equal(4, invoked)
                      surface.Width = 400
                      Assert.Empty(surface.OverflowItems)
                      Assert.Equal(5, surface.Items.Count)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Corner_pin_sits_at_the_end_of_the_row_and_is_never_swallowed_by_overflow()
        OnSta(Sub()
                  Using surface As New ChromeSurface()
                      surface.Size = New Size(140, 44)
                      Dim pin As New ChromeItem("chrome_pin_row", ChromeItemKind.Toggle) With {.IsPin = True}
                      surface.PinItem = pin
                      Dim items As New List(Of ChromeItem)()
                      For index As Integer = 0 To 5
                          items.Add(New ChromeItem("button" & index.ToString(), ChromeItemKind.Button))
                      Next
                      surface.SetItems(items)
                      ' Squeezed: some buttons collapse into "more", the pin does not.
                      Assert.NotEmpty(surface.OverflowItems)
                      Assert.Contains(pin, surface.Items)
                      Assert.False(pin.Bounds.IsEmpty)
                      Assert.True(pin.Bounds.Width >= UiScale.Px(ChromeMetrics.MinTarget, 1.0F))
                      Assert.True(pin.Bounds.Right <= surface.ClientSize.Width)
                      For Each item In surface.Items.Where(Function(candidate) candidate IsNot pin AndAlso Not candidate.Bounds.IsEmpty)
                          Assert.False(item.Bounds.IntersectsWith(pin.Bounds), item.Id & " overlaps the pin")
                      Next
                      ' Room enough: nothing overflows, and the pin is still the last thing in the row.
                      surface.Width = 800
                      Assert.Empty(surface.OverflowItems)
                      Assert.True(items.All(Function(item) item.Bounds.Right <= pin.Bounds.Left))
                      ' The panel asks for room for the pin: the natural size grew by exactly its slot.
                      Dim withPin As Integer = surface.PreferredPanelSize().Width
                      surface.PinItem = Nothing
                      surface.LayoutItems()
                      Assert.Equal(UiScale.Px(ChromeMetrics.MinTarget, 1.0F) + UiScale.Px(ChromeMetrics.Gap, 1.0F), withPin - surface.PreferredPanelSize().Width)
                      Assert.DoesNotContain(pin, surface.Items)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Keyboard_skips_text_and_disabled_items_and_escape_returns_focus_state()
        OnSta(Sub()
                  Using surface As New ChromeSurface With {.Size = New Size(400, 40)}
                      Dim invoked As Integer = 0
                      Dim text As New ChromeItem("count", ChromeItemKind.Text) With {.Caption = "1 / 2"}
                      Dim disabled As New ChromeItem("disabled", ChromeItemKind.Button) With {.Enabled = False}
                      Dim button As New ChromeItem("btn_Next_File", ChromeItemKind.Button) With {.Action = Sub() invoked += 1}
                      surface.SetItems({text, disabled, button})
                      Assert.True(surface.EnterKeyboardMode())
                      Assert.True(surface.IsFocusedItem(button))
                      Assert.True(surface.ActivateFocused())
                      Assert.Equal(1, invoked)
                      Assert.False(surface.MoveFocus(True))
                      surface.LeaveKeyboardMode()
                      Assert.False(surface.KeyboardMode)
                      Assert.False(surface.ActivateFocused())
                      Assert.Equal("btn_Next_File", surface.AccessibleFor(button).Description)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Popup_items_stack_vertically_without_overlap_and_do_not_dispose_borrowed_fonts()
        OnSta(Sub()
                  Using font As New Font("Segoe UI", 12, GraphicsUnit.Pixel), symbols As New Font("Segoe UI Symbol", 14, GraphicsUnit.Pixel)
                      Using menu As New ChromeMenu()
                          menu.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1, False, font, symbols)
                          menu.SetItems({New ChromeMenuItem("first") With {.Caption = "First"}, New ChromeMenuItem("second") With {.Caption = "Second"}})
                          menu.Surface.Size = menu.Surface.PreferredPanelSize()
                          Dim first = menu.Surface.Items(0).Bounds
                          Dim second = menu.Surface.Items(1).Bounds
                          Assert.True(first.Bottom <= second.Top)
                          Assert.Equal(first.Left, second.Left)
                          Assert.Empty(menu.Surface.OverflowItems)
                      End Using
                      Assert.True(font.GetHeight() > 0)
                      Assert.True(symbols.GetHeight() > 0)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Programmatic_path_updates_do_not_start_editing_and_native_clipboard_is_preserved()
        OnSta(Sub()
                  Using path As New ChromePathBox()
                      Dim started As Boolean = False
                      AddHandler path.EditingStarted, Sub() started = True
                      path.Text = "C:\photos"
                      Assert.Equal("C:\photos", path.Editor.Text)
                      Assert.False(started)
                      Assert.True(path.Editor.ShortcutsEnabled)
                      Assert.True(path.Editor.AllowDrop)
                      path.BeginEditing()
                      path.Text = "C:\other"
                      path.EndEditing(True)
                      Assert.True(started)
                      Assert.Equal("C:\photos", path.Text)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Holding_a_secondary_action_invokes_it_once_and_suppresses_the_primary_click()
        OnSta(Sub()
                  Using surface As New ChromeSurface With {.Size = New Size(100, 44)}
                      Dim primary As Integer = 0
                      Dim secondary As Integer = 0
                      Dim item As New ChromeItem("open", ChromeItemKind.Button) With {
                          .Action = Sub() primary += 1, .SecondaryAction = Sub() secondary += 1}
                      surface.SetItems({item})
                      surface.SetAnimationEnabled(False)
                      Dim at As New Point(item.Bounds.Left + 1, item.Bounds.Top + 1)
                      Dim args As New MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0)
                      surface.GetType().GetMethod("OnMouseDown", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic).Invoke(surface, {args})
                      Assert.True(surface.NeedsAnimation)
                      surface.Animate(ChromeMetrics.HoldMilliseconds)
                      surface.Animate(ChromeMetrics.HoldMilliseconds)
                      surface.GetType().GetMethod("OnMouseUp", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic).Invoke(surface, {args})
                      Assert.Equal(0, primary)
                      Assert.Equal(1, secondary)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Completion_matches_case_insensitively_caps_results_and_honors_cancellation()
        Dim root As String = Path.Combine(Path.GetTempPath(), "fms-chrome-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root)
        Try
            For index As Integer = 0 To 204
                Directory.CreateDirectory(Path.Combine(root, "Photos" & index.ToString()))
            Next
            Directory.CreateDirectory(Path.Combine(root, "Other"))
            Dim matches As String() = ChromePathCompletion.Find(Path.Combine(root, "photos"), CancellationToken.None)
            Assert.Equal(ChromeMetrics.CompletionLimit, matches.Length)
            Assert.All(matches, Sub(value) Assert.StartsWith("Photos", Path.GetFileName(value)))
            Using cancellation As New CancellationTokenSource()
                cancellation.Cancel()
                Assert.Throws(Of OperationCanceledException)(Sub() ChromePathCompletion.Find(root, cancellation.Token))
            End Using
            Assert.Empty(ChromePathCompletion.Find(Path.Combine(root, "missing", "child"), CancellationToken.None))
        Finally
            Directory.Delete(root, True)
        End Try
    End Sub
    <Fact>
    Public Sub PathBox_handle_creation_survives_caller_disposing_borrowed_or_previous_fonts()
        OnSta(Sub()
                  Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException)
                  Using surface As New ChromeSurface()
                      Using path As New ChromePathBox()
                          surface.Controls.Add(path)
                          Dim font1 As New Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel)
                          Dim symbols1 As New Font("Segoe UI Symbol", 14, FontStyle.Regular, GraphicsUnit.Pixel)
                          path.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, False, font1, symbols1)
                          font1.Dispose()
                          symbols1.Dispose()

                          ' Reconfiguring with equivalent font instance (e.g. repeated ChromeConfigure)
                          Dim font2 As New Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel)
                          Dim symbols2 As New Font("Segoe UI Symbol", 14, FontStyle.Regular, GraphicsUnit.Pixel)
                          path.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, False, font2, symbols2)
                          font2.Dispose()
                          symbols2.Dispose()

                          Dim h As IntPtr = path.Editor.Handle
                          Assert.NotEqual(IntPtr.Zero, h)
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub AlignLeft_item_and_Delete_button_with_caption_measure_and_layout_properly()
        OnSta(Sub()
                  Using surface As New ChromeSurface()
                      Dim font As New Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel)
                      Dim symbols As New Font("Segoe UI Symbol", 14, FontStyle.Regular, GraphicsUnit.Pixel)
                      surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, False, font, symbols)

                      Dim fileItem As New ChromeItem("lbl_Current_File", ChromeItemKind.Button) With {
                          .Caption = "C:\Photos\sample.jpg",
                          .AlignLeft = True
                      }
                      Assert.True(fileItem.AlignLeft)

                      Dim deleteItem As New ChromeItem("bt_Delete", ChromeItemKind.Button) With {
                          .GlyphId = "action.delete",
                          .Caption = "Delete",
                          .ShowCaptionWithGlyph = True,
                          .Destructive = True
                      }
                      Dim deleteWidth As Integer = surface.MeasureItemWidth(deleteItem)
                      Dim singleWidth As Integer = Math.Max(UiScale.Px(ChromeMetrics.ItemHeight, 1.0F), UiScale.Px(ChromeMetrics.MinTarget, 1.0F))
                      Assert.True(deleteWidth > singleWidth, "Delete button with caption should be wider than a bare glyph icon button")
                      font.Dispose()
                      symbols.Dispose()
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Corner_pin_does_not_paint_accent_fill_when_pinned()
        OnSta(Sub()
                  Using surface As New ChromeSurface()
                      surface.Size = New Size(200, 44)
                      Dim font As New Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel)
                      Dim symbols As New Font("Segoe UI Symbol", 14, FontStyle.Regular, GraphicsUnit.Pixel)
                      Dim theme As ChromeTheme = ChromeTheme.FromScheme(Color.Black, Color.White, False)
                      surface.Configure(theme, 1.0F, False, font, symbols)

                      Dim pin As New ChromeItem("chrome_pin_row", ChromeItemKind.Toggle) With {
                          .IsPin = True,
                          .Checked = True
                      }
                      surface.PinItem = pin
                      surface.LayoutItems()

                      Using bmp As New Bitmap(surface.Width, surface.Height)
                          Using g As Graphics = Graphics.FromImage(bmp)
                              g.Clear(Color.Black)
                              Dim pe As New PaintEventArgs(g, New Rectangle(0, 0, surface.Width, surface.Height))
                              surface.GetType().GetMethod("OnPaint", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic).Invoke(surface, {pe})
                          End Using

                          Dim accent As Color = theme.Accent
                          Dim foundAccent As Boolean = False
                          For x As Integer = pin.Bounds.Left To pin.Bounds.Right - 1
                              For y As Integer = pin.Bounds.Top To pin.Bounds.Bottom - 1
                                  Dim pixel As Color = bmp.GetPixel(x, y)
                                  If pixel.R = accent.R AndAlso pixel.G = accent.G AndAlso pixel.B = accent.B Then
                                      foundAccent = True
                                      Exit For
                                  End If
                              Next
                              If foundAccent Then Exit For
                          Next
                          Assert.False(foundAccent, "Pin button should not have bright accent fill when pinned")
                      End Using
                      font.Dispose()
                      symbols.Dispose()
                  End Using
              End Sub)
    End Sub
End Class
#End If

