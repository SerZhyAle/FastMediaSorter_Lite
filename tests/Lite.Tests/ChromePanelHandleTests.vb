#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>L0281 T-3a: the move handle of a movable floating panel on the chrome surface - its slot in the row, its
''' place beside the pin, the keyboard move mode and the pointer gesture. The surface half only: what a panel's owner does
''' with the events (moving the host, the placement preferences) is T-3b.</summary>
Public Class ChromePanelHandleTests
    Private Shared ReadOnly GlyphDir As String = Path.Combine(AppContext.BaseDirectory, "TestData", "glyphs")

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

    Private Shared Function NewSurface(width As Integer, rightToLeft As Boolean) As ChromeSurface
        Dim surface As New ChromeSurface()
        Dim font As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel)
        surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, rightToLeft, font, font)
        surface.Size = New Size(width, 44)
        Return surface
    End Function

    Private Shared Function RowItems(count As Integer) As List(Of ChromeItem)
        Dim list As New List(Of ChromeItem)()
        For index As Integer = 0 To count - 1
            list.Add(New ChromeItem("button" & index.ToString(), ChromeItemKind.Button))
        Next
        Return list
    End Function

    Private Shared Function NewPin() As ChromeItem
        Return New ChromeItem("chrome_pin_row", ChromeItemKind.Toggle) With {.IsPin = True}
    End Function

    Private Shared Function SlotWidth() As Integer
        Return UiScale.Px(ChromeMetrics.MinTarget, 1.0F) + UiScale.Px(ChromeMetrics.Gap, 1.0F)
    End Function

    Private Shared Sub Send(surface As ChromeSurface, methodName As String, at As Point)
        Dim args As New MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0)
        surface.GetType().GetMethod(methodName, Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic).Invoke(surface, {args})
    End Sub

    ' --- the slot in the row ------------------------------------------------------------------------------

    <Fact>
    Public Sub A_handle_widens_the_panel_and_the_minimum_row_by_exactly_one_slot()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewSurface(800, False)
                      surface.PinItem = NewPin()
                      surface.SetItems(RowItems(4))
                      Dim plainWidth As Integer = surface.PreferredPanelSize().Width
                      Dim plainMinimum As Integer = surface.MinimumRowWidth()
                      surface.PanelHandleItem = ChromeSurface.CreatePanelHandleItem("row1")
                      surface.LayoutItems()
                      Assert.Equal(SlotWidth(), surface.PreferredPanelSize().Width - plainWidth)
                      Assert.Equal(SlotWidth(), surface.MinimumRowWidth() - plainMinimum)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub The_handle_is_never_taken_by_overflow_and_overlaps_nothing()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewSurface(140, False)
                      Dim pin As ChromeItem = NewPin()
                      Dim handle As ChromeItem = ChromeSurface.CreatePanelHandleItem("row1")
                      surface.PinItem = pin
                      surface.PanelHandleItem = handle
                      surface.SetItems(RowItems(6))
                      ' Squeezed: buttons collapse into "more"; the handle and the pin keep their slots.
                      Assert.NotEmpty(surface.OverflowItems)
                      Assert.DoesNotContain(handle, surface.OverflowItems)
                      Assert.Contains(handle, surface.Items)
                      Assert.False(handle.Bounds.IsEmpty)
                      Assert.Equal(UiScale.Px(ChromeMetrics.MinTarget, 1.0F), handle.Bounds.Width)
                      Assert.True(handle.Bounds.Left >= 0 AndAlso handle.Bounds.Right <= surface.ClientSize.Width)
                      For Each item In surface.Items.Where(Function(candidate) candidate IsNot handle AndAlso Not candidate.Bounds.IsEmpty)
                          Assert.False(item.Bounds.IntersectsWith(handle.Bounds), item.Id & " overlaps the handle")
                      Next
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub The_handle_stands_between_the_pin_and_the_row_in_every_direction()
        OnSta(Sub()
                  For Each rightToLeft As Boolean In {False, True}
                      For Each pinAtStart As Boolean In {False, True}
                          Using surface As ChromeSurface = NewSurface(800, rightToLeft)
                              Dim pin As ChromeItem = NewPin()
                              Dim handle As ChromeItem = ChromeSurface.CreatePanelHandleItem("row1")
                              Dim items As List(Of ChromeItem) = RowItems(3)
                              surface.PinAtStart = pinAtStart
                              surface.PinItem = pin
                              surface.PanelHandleItem = handle
                              surface.SetItems(items)
                              Dim label As String = "rtl=" & rightToLeft.ToString() & " pinAtStart=" & pinAtStart.ToString()
                              Assert.False(handle.Bounds.IntersectsWith(pin.Bounds), label)
                              Assert.Equal(pin.Bounds.Top, handle.Bounds.Top)
                              Assert.Equal(pin.Bounds.Height, handle.Bounds.Height)
                              If pinAtStart OrElse rightToLeft Then
                                  ' The pin leads; the handle follows it; the row starts after both.
                                  Assert.True(pin.Bounds.Right <= handle.Bounds.Left, label)
                                  Assert.True(items.All(Function(item) item.Bounds.Left >= handle.Bounds.Right), label)
                              Else
                                  ' The pin ends the row; the handle stands just before it.
                                  Assert.True(handle.Bounds.Right <= pin.Bounds.Left, label)
                                  Assert.True(items.All(Function(item) item.Bounds.Right <= handle.Bounds.Left), label)
                              End If
                          End Using
                      Next
                  Next
              End Sub)
    End Sub

    <Fact>
    Public Sub Without_a_pin_the_handle_takes_the_pins_corner()
        OnSta(Sub()
                  For Each rightToLeft As Boolean In {False, True}
                      Dim pinCorner As Rectangle = Rectangle.Empty
                      Using surface As ChromeSurface = NewSurface(800, rightToLeft)
                          Dim pin As ChromeItem = NewPin()
                          surface.PinItem = pin
                          surface.SetItems(RowItems(3))
                          pinCorner = pin.Bounds
                      End Using
                      Using surface As ChromeSurface = NewSurface(800, rightToLeft)
                          Dim handle As ChromeItem = ChromeSurface.CreatePanelHandleItem("row1")
                          surface.PanelHandleItem = handle
                          surface.SetItems(RowItems(3))
                          Assert.Equal(pinCorner, handle.Bounds)
                      End Using
                  Next
              End Sub)
    End Sub

    <Fact>
    Public Sub A_surface_without_a_handle_lays_out_exactly_as_it_did_before()
        OnSta(Sub()
                  Using fresh As ChromeSurface = NewSurface(300, False)
                      Using used As ChromeSurface = NewSurface(300, False)
                          For Each surface As ChromeSurface In {fresh, used}
                              surface.PinItem = NewPin()
                              surface.SetItems(RowItems(6))
                          Next
                          ' A handle comes and goes on one of the two; the other never had one.
                          used.PanelHandleItem = ChromeSurface.CreatePanelHandleItem("row1")
                          used.LayoutItems()
                          used.PanelHandleItem = Nothing
                          used.LayoutItems()
                          Assert.Equal(fresh.PreferredPanelSize(), used.PreferredPanelSize())
                          Assert.Equal(fresh.MinimumRowWidth(), used.MinimumRowWidth())
                          Assert.Equal(fresh.Items.Select(Function(item) item.Id).ToArray(), used.Items.Select(Function(item) item.Id).ToArray())
                          Assert.Equal(fresh.Items.Select(Function(item) item.Bounds).ToArray(), used.Items.Select(Function(item) item.Bounds).ToArray())
                          Assert.Equal(fresh.OverflowItems.Select(Function(item) item.Id).ToArray(), used.OverflowItems.Select(Function(item) item.Id).ToArray())
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_menu_surface_ignores_a_handle()
        OnSta(Sub()
                  Using plain As ChromeSurface = NewSurface(200, False)
                      Using withHandle As ChromeSurface = NewSurface(200, False)
                          plain.VerticalLayout = True
                          withHandle.VerticalLayout = True
                          Dim handle As ChromeItem = ChromeSurface.CreatePanelHandleItem("menu")
                          withHandle.PanelHandleItem = handle
                          plain.SetItems(RowItems(3))
                          withHandle.SetItems(RowItems(3))
                          Assert.Equal(plain.PreferredPanelSize(), withHandle.PreferredPanelSize())
                          Assert.Equal(0, withHandle.MinimumRowWidth())
                          Assert.True(handle.Bounds.IsEmpty)
                          Assert.DoesNotContain(handle, withHandle.Items)
                      End Using
                  End Using
              End Sub)
    End Sub

    ' --- the ready item and its glyph ---------------------------------------------------------------------

    <Fact>
    Public Sub The_ready_handle_item_carries_the_catalog_glyph_the_flag_and_the_same_words_twice()
        Dim handle As ChromeItem = ChromeSurface.CreatePanelHandleItem("row1")
        Assert.Equal("chrome_handle_row1", handle.Id)
        Assert.Equal("action.reorder", handle.GlyphId)
        Assert.True(handle.IsPanelHandle)
        Assert.False(handle.IsPin)
        Assert.Null(handle.Action)
        Assert.NotEmpty(handle.AccessibleName)
        Assert.Equal(handle.AccessibleName, handle.Tooltip)
        Assert.Equal(Localization.T("Переместить панель"), handle.AccessibleName)
    End Sub

    <Fact>
    Public Sub The_handle_glyph_is_vendored_and_draws_two_bars()
        Dim glyphFile As String = Path.Combine(GlyphDir, "action.reorder.svg")
        Assert.True(File.Exists(glyphFile), "action.reorder.svg is not vendored")
        Using bitmap As Bitmap = GlyphRenderer.RenderSvg(File.ReadAllText(glyphFile), 24, Color.Black)
            Assert.True(bitmap.GetPixel(12, 10).A > 128, "the upper bar is not painted")
            Assert.True(bitmap.GetPixel(12, 14).A > 128, "the lower bar is not painted")
            Assert.True(bitmap.GetPixel(12, 12).A < 64, "the gap between the bars is painted")
            Assert.True(bitmap.GetPixel(12, 3).A < 64, "the glyph reaches the top edge")
        End Using
    End Sub

    ' --- pure rules ---------------------------------------------------------------------------------------

    <Fact>
    Public Sub A_press_becomes_a_drag_when_it_leaves_the_rectangle_the_system_centres_on_the_press()
        Dim pressed As New Point(100, 100)
        Dim dragSize As New Size(4, 4)
        Assert.False(ChromeSurface.ExceedsDragSize(pressed, pressed, dragSize))
        Assert.False(ChromeSurface.ExceedsDragSize(pressed, New Point(102, 100), dragSize))
        Assert.False(ChromeSurface.ExceedsDragSize(pressed, New Point(98, 102), dragSize))
        Assert.True(ChromeSurface.ExceedsDragSize(pressed, New Point(103, 100), dragSize))
        Assert.True(ChromeSurface.ExceedsDragSize(pressed, New Point(100, 97), dragSize))
        ' The two sides are separate: a wide rectangle does not widen the vertical travel.
        Assert.False(ChromeSurface.ExceedsDragSize(pressed, New Point(104, 100), New Size(10, 4)))
        Assert.True(ChromeSurface.ExceedsDragSize(pressed, New Point(106, 100), New Size(10, 4)))
        Assert.True(ChromeSurface.ExceedsDragSize(pressed, New Point(100, 103), New Size(10, 4)))
    End Sub

    <Fact>
    Public Sub Arrow_keys_become_physical_steps_and_shift_makes_five()
        Dim stepsX As Integer
        Dim stepsY As Integer
        Assert.True(ChromeSurface.PanelKeySteps(Keys.Left, False, stepsX, stepsY))
        Assert.Equal(-1, stepsX)
        Assert.Equal(0, stepsY)
        Assert.True(ChromeSurface.PanelKeySteps(Keys.Right, False, stepsX, stepsY))
        Assert.Equal(1, stepsX)
        Assert.Equal(0, stepsY)
        Assert.True(ChromeSurface.PanelKeySteps(Keys.Up, False, stepsX, stepsY))
        Assert.Equal(0, stepsX)
        Assert.Equal(-1, stepsY)
        Assert.True(ChromeSurface.PanelKeySteps(Keys.Down, False, stepsX, stepsY))
        Assert.Equal(0, stepsX)
        Assert.Equal(1, stepsY)
        Assert.True(ChromeSurface.PanelKeySteps(Keys.Left, True, stepsX, stepsY))
        Assert.Equal(-ChromeSurface.PanelMoveBigSteps, stepsX)
        Assert.Equal(5, ChromeSurface.PanelMoveBigSteps)
        Assert.True(ChromeSurface.PanelKeySteps(Keys.Down, True, stepsX, stepsY))
        Assert.Equal(ChromeSurface.PanelMoveBigSteps, stepsY)
        stepsX = 7
        stepsY = 7
        Assert.False(ChromeSurface.PanelKeySteps(Keys.Enter, False, stepsX, stepsY))
        Assert.Equal(0, stepsX)
        Assert.Equal(0, stepsY)
        Assert.False(ChromeSurface.PanelKeySteps(Keys.A, True, stepsX, stepsY))
    End Sub

    ' --- the keyboard move mode ---------------------------------------------------------------------------

    <Fact>
    Public Sub The_f10_ring_reaches_the_handle_between_the_row_and_the_pin()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewSurface(800, False)
                      Dim pin As ChromeItem = NewPin()
                      Dim handle As ChromeItem = ChromeSurface.CreatePanelHandleItem("row1")
                      surface.PinItem = pin
                      surface.PanelHandleItem = handle
                      surface.SetItems(RowItems(3))
                      Dim reached As New List(Of String)()
                      Assert.True(surface.EnterKeyboardMode())
                      reached.Add(surface.FocusedRow.Id)
                      While surface.MoveFocus(True)
                          reached.Add(surface.FocusedRow.Id)
                      End While
                      Assert.Equal(New String() {"button0", "button1", "button2", handle.Id, pin.Id}, reached.ToArray())
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Enter_or_space_on_the_focused_handle_enters_the_move_mode_and_the_arrows_move()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewSurface(800, False)
                      Dim handle As ChromeItem = ChromeSurface.CreatePanelHandleItem("row1")
                      surface.PinItem = NewPin()
                      surface.PanelHandleItem = handle
                      surface.SetItems(RowItems(3))
                      Dim moves As New List(Of String)()
                      Dim modeChanges As Integer = 0
                      AddHandler surface.PanelHandleKeyMove, Sub(sender, e) moves.Add(e.StepsX.ToString() & "," & e.StepsY.ToString())
                      AddHandler surface.PanelHandleMoveModeChanged, Sub(sender, e) modeChanges += 1

                      ' No ring yet, then the ring on another item: the handle owns none of these keys.
                      Assert.False(surface.HandlePanelMoveKey(Keys.Enter))
                      Assert.True(surface.EnterKeyboardMode())
                      Assert.False(surface.HandlePanelMoveKey(Keys.Enter))
                      Assert.False(surface.IsPanelMoveMode)

                      surface.FocusItem(handle)
                      Assert.False(surface.HandlePanelMoveKey(Keys.Control Or Keys.Enter))
                      Assert.False(surface.HandlePanelMoveKey(Keys.Shift Or Keys.Enter))
                      Assert.False(surface.HandlePanelMoveKey(Keys.Right))
                      Assert.False(surface.IsPanelMoveMode)
                      Assert.Empty(moves)

                      Assert.True(surface.HandlePanelMoveKey(Keys.Enter))
                      Assert.True(surface.IsPanelMoveMode)
                      Assert.True(handle.IsPressed)
                      Assert.Equal(1, modeChanges)

                      Assert.True(surface.HandlePanelMoveKey(Keys.Right))
                      Assert.True(surface.HandlePanelMoveKey(Keys.Shift Or Keys.Down))
                      Assert.True(surface.HandlePanelMoveKey(Keys.Shift Or Keys.Left))
                      Assert.True(surface.HandlePanelMoveKey(Keys.Up))
                      Assert.Equal(New String() {"1,0", "0,5", "-5,0", "0,-1"}, moves.ToArray())
                      ' A chord and an unrelated key are not the handle's, and the mode stays.
                      Assert.False(surface.HandlePanelMoveKey(Keys.Control Or Keys.Left))
                      Assert.False(surface.HandlePanelMoveKey(Keys.A))
                      Assert.Equal(4, moves.Count)
                      Assert.True(surface.IsPanelMoveMode)

                      Assert.True(surface.HandlePanelMoveKey(Keys.Escape))
                      Assert.False(surface.IsPanelMoveMode)
                      Assert.False(handle.IsPressed)
                      Assert.Equal(2, modeChanges)

                      Assert.True(surface.HandlePanelMoveKey(Keys.Space))
                      Assert.True(surface.IsPanelMoveMode)
                      Assert.True(surface.HandlePanelMoveKey(Keys.Enter))
                      Assert.False(surface.IsPanelMoveMode)
                      Assert.Equal(4, modeChanges)
                      Assert.Equal(4, moves.Count)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub The_move_mode_ends_when_the_ring_leaves_the_handle_or_the_panel()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewSurface(800, False)
                      Dim handle As ChromeItem = ChromeSurface.CreatePanelHandleItem("row1")
                      surface.PinItem = NewPin()
                      surface.PanelHandleItem = handle
                      surface.SetItems(RowItems(3))
                      surface.EnterKeyboardMode()
                      ' The ring steps off the handle.
                      surface.FocusItem(handle)
                      Assert.True(surface.HandlePanelMoveKey(Keys.Enter))
                      Assert.True(surface.MoveFocus(False))
                      Assert.False(surface.IsPanelMoveMode)
                      Assert.False(handle.IsPressed)
                      ' The ring leaves the panel.
                      surface.FocusItem(handle)
                      Assert.True(surface.HandlePanelMoveKey(Keys.Enter))
                      surface.LeaveKeyboardMode()
                      Assert.False(surface.IsPanelMoveMode)
                      Assert.False(surface.HandlePanelMoveKey(Keys.Right))
                      ' The items are replaced under it.
                      surface.EnterKeyboardMode()
                      surface.FocusItem(handle)
                      Assert.True(surface.HandlePanelMoveKey(Keys.Enter))
                      surface.SetItems(RowItems(2))
                      Assert.False(surface.IsPanelMoveMode)
                      ' The ring's own activation (the accessible default action, Enter on a ring that is not routed) toggles it.
                      surface.EnterKeyboardMode()
                      surface.FocusItem(handle)
                      Assert.True(surface.ActivateFocused())
                      Assert.True(surface.IsPanelMoveMode)
                      Assert.True(surface.ActivateFocused())
                      Assert.False(surface.IsPanelMoveMode)
                  End Using
              End Sub)
    End Sub

    ' --- the pointer gesture ------------------------------------------------------------------------------

    Private Shared Function GestureRig(surface As ChromeSurface, log As List(Of String), pointer As Func(Of Point)) As ChromeItem
        Dim handle As ChromeItem = ChromeSurface.CreatePanelHandleItem("row1")
        surface.PinItem = NewPin()
        surface.PanelHandleItem = handle
        surface.SetItems(RowItems(3))
        surface.SetAnimationEnabled(False)
        surface.PointerScreenPosition = pointer
        AddHandler surface.PanelHandlePress, Sub(sender, e) log.Add("press")
        AddHandler surface.PanelHandleDrag, Sub(sender, e) log.Add("drag")
        AddHandler surface.PanelHandleRelease, Sub(sender, e) log.Add(If(e.Canceled, "release-canceled", "release"))
        AddHandler surface.PanelHandleClick, Sub(sender, e) log.Add("click")
        AddHandler surface.MoveGripPress, Sub(sender, e) log.Add("grip")
        AddHandler surface.MoveGripDoubleClick, Sub(sender, e) log.Add("grip-double")
        Return handle
    End Function

    <Fact>
    Public Sub Press_and_travel_raise_press_then_drag_then_release_and_never_the_move_grip()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewSurface(800, False)
                      Dim log As New List(Of String)()
                      Dim pointer As New Point(500, 300)
                      Dim handle As ChromeItem = GestureRig(surface, log, Function() pointer)
                      Dim centre As New Point(handle.Bounds.Left + handle.Bounds.Width \ 2, handle.Bounds.Top + handle.Bounds.Height \ 2)

                      Send(surface, "OnMouseDown", centre)
                      Assert.Equal(New String() {"press"}, log.ToArray())
                      Assert.True(surface.Capture, "the surface must hold the mouse for the gesture")
                      Assert.True(handle.IsPressed)

                      ' Not yet a drag: the pointer has not moved.
                      Send(surface, "OnMouseMove", centre)
                      Assert.Equal(New String() {"press"}, log.ToArray())

                      pointer = New Point(540, 300)
                      Send(surface, "OnMouseMove", centre)
                      pointer = New Point(560, 310)
                      Send(surface, "OnMouseMove", centre)
                      Send(surface, "OnMouseUp", centre)
                      Assert.Equal(New String() {"press", "drag", "drag", "release"}, log.ToArray())
                      Assert.False(handle.IsPressed)
                      Assert.False(surface.Capture)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_press_that_never_travels_is_a_click()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewSurface(800, False)
                      Dim log As New List(Of String)()
                      Dim pointer As New Point(500, 300)
                      Dim handle As ChromeItem = GestureRig(surface, log, Function() pointer)
                      Dim centre As New Point(handle.Bounds.Left + handle.Bounds.Width \ 2, handle.Bounds.Top + handle.Bounds.Height \ 2)
                      Send(surface, "OnMouseDown", centre)
                      Send(surface, "OnMouseUp", centre)
                      Assert.Equal(New String() {"press", "click"}, log.ToArray())
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub The_press_event_carries_the_screen_point_and_the_drag_event_follows_the_pointer()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewSurface(800, False)
                      Dim pointer As New Point(500, 300)
                      Dim handle As ChromeItem = GestureRig(surface, New List(Of String)(), Function() pointer)
                      Dim pressAt As Point = Point.Empty
                      Dim dragAt As Point = Point.Empty
                      AddHandler surface.PanelHandlePress, Sub(sender, e) pressAt = e.ScreenPoint
                      AddHandler surface.PanelHandleDrag, Sub(sender, e) dragAt = e.ScreenPoint
                      Dim centre As New Point(handle.Bounds.Left + handle.Bounds.Width \ 2, handle.Bounds.Top + handle.Bounds.Height \ 2)
                      Send(surface, "OnMouseDown", centre)
                      pointer = New Point(620, 345)
                      Send(surface, "OnMouseMove", centre)
                      Send(surface, "OnMouseUp", centre)
                      Assert.Equal(New Point(500, 300), pressAt)
                      Assert.Equal(New Point(620, 345), dragAt)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_capture_lost_in_the_middle_of_a_drag_ends_it_as_a_canceled_release()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewSurface(800, False)
                      Dim log As New List(Of String)()
                      Dim pointer As New Point(500, 300)
                      Dim handle As ChromeItem = GestureRig(surface, log, Function() pointer)
                      Dim centre As New Point(handle.Bounds.Left + handle.Bounds.Width \ 2, handle.Bounds.Top + handle.Bounds.Height \ 2)
                      Send(surface, "OnMouseDown", centre)
                      pointer = New Point(560, 300)
                      Send(surface, "OnMouseMove", centre)
                      surface.Capture = False
                      Assert.Equal(New String() {"press", "drag", "release-canceled"}, log.ToArray())
                      Assert.False(handle.IsPressed)
                      ' The button coming up afterwards finds no gesture: nothing more is raised.
                      Send(surface, "OnMouseUp", centre)
                      Assert.Equal(3, log.Count)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_disabled_handle_starts_no_gesture()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewSurface(800, False)
                      Dim log As New List(Of String)()
                      Dim pointer As New Point(500, 300)
                      Dim handle As ChromeItem = GestureRig(surface, log, Function() pointer)
                      handle.Enabled = False
                      Dim centre As New Point(handle.Bounds.Left + handle.Bounds.Width \ 2, handle.Bounds.Top + handle.Bounds.Height \ 2)
                      Send(surface, "OnMouseDown", centre)
                      Send(surface, "OnMouseUp", centre)
                      Assert.Empty(log)
                  End Using
              End Sub)
    End Sub
End Class
#End If
