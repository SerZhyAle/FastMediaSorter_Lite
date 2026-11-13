#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' L0281 T-5 (owner answer Q281-1: the window commands are movable). The strip of the window-commands panel with its new
''' handle slot is proven on a real <see cref="ChromeSurface"/> (the physical order, the room, the footprint); the wiring in the
''' viewer - the handle's events, the place keys, the band, the protected surfaces, the zone, the minimum tracking size - is
''' proven the way <c>ChromeReturnPanelsTests</c> and <c>RecipientsOverlaySeamTests</c> prove theirs: by reading the sources.
''' </summary>
Public Class ChromeWindowCommandsMovableTests

    ' ----------------------------------------------------------------------------------- helpers ----

    Private Shared Function Source(name As String) As String
        Dim root As New DirectoryInfo(AppContext.BaseDirectory)
        While root IsNot Nothing AndAlso Not File.Exists(Path.Combine(root.FullName, "src", "Main_Form.vb"))
            root = root.Parent
        End While
        Assert.NotNull(root)
        Dim fullPath As String = Path.Combine(root.FullName, "src", name)
        Assert.True(File.Exists(fullPath), "missing source: " & name)
        ' One line-ending convention for every assertion, whatever the file on disk uses.
        Return File.ReadAllText(fullPath).Replace(vbCrLf, vbLf).Replace(vbLf, vbCrLf)
    End Function

    ''' <summary>The body of the first member whose declaration contains <paramref name="declared"/>.</summary>
    Private Shared Function MemberBody(text As String, declared As String) As String
        Dim start As Integer = text.IndexOf(declared, StringComparison.Ordinal)
        Assert.True(start >= 0, "declaration not found: " & declared)
        Dim endSub As Integer = text.IndexOf(vbCrLf & "    End Sub", start, StringComparison.Ordinal)
        Dim endFunction As Integer = text.IndexOf(vbCrLf & "    End Function", start, StringComparison.Ordinal)
        Dim [end] As Integer = If(endSub >= 0 AndAlso (endFunction < 0 OrElse endSub < endFunction), endSub, endFunction)
        Assert.True([end] > start, "member end not found: " & declared)
        Return text.Substring(start, [end] - start)
    End Function

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

    ''' <summary>A surface configured the way <c>BuildWindowControlsPanel</c> configures the panel's: pin first, the extra
    ''' horizontal padding, the grip and the three commands in their one physical order, and (T-5) the move handle.</summary>
    Private Shared Function ControlsSurface(width As Integer, ByRef pin As ChromeItem, ByRef handle As ChromeItem,
                                            ByRef items As List(Of ChromeItem)) As ChromeSurface
        Dim surface As New ChromeSurface()
        Dim font As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel)
        surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, False, font, font)
        surface.Size = New Size(width, 44)
        pin = New ChromeItem("chrome_pin_chrome_WindowControls", ChromeItemKind.Toggle) With {.IsPin = True}
        handle = ChromeSurface.CreatePanelHandleItem("chrome_WindowControls")
        items = New List(Of ChromeItem) From {
            New ChromeItem("win_Grip", ChromeItemKind.Text) With {.IsMoveGrip = True},
            New ChromeItem("win_Minimize", ChromeItemKind.Button) With {.CustomGlyph = ChromeCustomGlyph.WindowMinimize},
            New ChromeItem("win_Maximize", ChromeItemKind.Button) With {.CustomGlyph = ChromeCustomGlyph.WindowMaximize},
            New ChromeItem("win_Close", ChromeItemKind.Button) With {.CustomGlyph = ChromeCustomGlyph.WindowClose}}
        surface.PinAtStart = True
        surface.ExtraPadding = ChromeMetrics.WindowControlsExtraPadding
        surface.PinItem = pin
        surface.PanelHandleItem = handle
        surface.SetItems(items)
        Return surface
    End Function

    ' --------------------------------------------------------------------------- the real strip ----

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "ChromeSurface")>
    Public Sub The_strip_reads_pin_handle_grip_then_the_three_commands_in_their_one_physical_order()
        OnSta(Sub()
                  Dim pin As ChromeItem = Nothing
                  Dim handle As ChromeItem = Nothing
                  Dim items As List(Of ChromeItem) = Nothing
                  Using surface As ChromeSurface = ControlsSurface(UiScale.Px(ChromeMetrics.WindowControlsLogicalWidth(), 1.0F), pin, handle, items)
                      Dim grip As ChromeItem = items(0)
                      Dim minimize As ChromeItem = items(1)
                      Dim maximize As ChromeItem = items(2)
                      Dim close As ChromeItem = items(3)
                      Assert.Empty(surface.OverflowItems)
                      Assert.True(pin.Bounds.Right <= handle.Bounds.Left, "the handle follows the pin")
                      Assert.True(handle.Bounds.Right <= grip.Bounds.Left, "the handle stands clear of the drag grip")
                      Assert.True(grip.Bounds.Right <= minimize.Bounds.Left, "the grip, then Minimize")
                      Assert.True(minimize.Bounds.Right <= maximize.Bounds.Left, "Minimize, then Maximize / Restore")
                      Assert.True(maximize.Bounds.Right <= close.Bounds.Left, "Maximize / Restore, then Close: Close rightmost")
                      ' The handle is not among the commands: it never sits between two of them, and the grip keeps the window drag.
                      Assert.True(handle.Bounds.Right <= grip.Bounds.Left AndAlso handle.IsPanelHandle)
                      Assert.True(grip.IsMoveGrip AndAlso Not grip.IsPanelHandle)
                      Assert.Equal(UiScale.Px(ChromeMetrics.MinTarget, 1.0F), handle.Bounds.Width)
                  End Using
              End Sub)
    End Sub

    <Fact, Trait("Kind", "UiForm"), Trait("Form", "ChromeSurface")>
    Public Sub The_strip_at_the_floor_width_is_exactly_the_window_footprint_with_the_handle_slot()
        OnSta(Sub()
                  Dim pin As ChromeItem = Nothing
                  Dim handle As ChromeItem = Nothing
                  Dim items As List(Of ChromeItem) = Nothing
                  Dim floor As Integer = UiScale.Px(ChromeMetrics.WindowControlsLogicalWidth(), 1.0F)
                  Using surface As ChromeSurface = ControlsSurface(floor, pin, handle, items)
                      ' The panel's own preferred width IS the footprint the window's minimum tracking size is built from,
                      ' handle slot included: the window can never be asked to hold less than the panel shows.
                      Assert.Equal(floor, surface.PreferredPanelSize().Width)
                      Assert.Equal(floor, ChromeMetrics.WindowControlsLogicalWidth())
                      ' Every one of the four slots is whole at the floor: nothing overflows, Close ends at the padding.
                      Assert.Empty(surface.OverflowItems)
                      For Each item As ChromeItem In items
                          Assert.False(item.Bounds.IsEmpty, item.Id)
                          Assert.True(item.Bounds.Left >= 0 AndAlso item.Bounds.Right <= surface.ClientSize.Width, item.Id)
                      Next
                  End Using
              End Sub)
    End Sub

    ' ------------------------------------------------------------------------------ the viewer ----

    <Fact>
    Public Sub The_panel_gets_the_shared_handle_wiring_and_keeps_its_grip_and_its_physical_order()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Dim build As String = MemberBody(chrome, "Private Sub BuildWindowControlsPanel()")
        ' The handle is wired by the same helper the rows use, after the pin and before the items are set.
        Dim pin As Integer = build.IndexOf("panel.Host.Surface.PinItem = panel.Pin", StringComparison.Ordinal)
        Dim wire As Integer = build.IndexOf("AddChromePanelHandle(panel)", StringComparison.Ordinal)
        Dim setItems As Integer = build.IndexOf("panel.Host.Surface.SetItems(items)", StringComparison.Ordinal)
        Assert.True(pin >= 0 AndAlso wire > pin AndAlso setItems > wire, "pin, then the handle, then the items")
        Assert.Contains("panel.Host.Surface.PinAtStart = True", build)
        ' The empty grip still drags the WINDOW and double-click still toggles maximize; the handle's events never do.
        Assert.Contains("AddHandler panel.Host.Surface.MoveGripPress, Sub(sender, args) ChromeBeginWindowDrag()", build)
        Assert.Contains("AddHandler panel.Host.Surface.MoveGripDoubleClick, Sub(sender, args) ChromeToggleMaximized()", build)
        Assert.DoesNotContain("PanelHandle", build.Replace("AddChromePanelHandle(panel)", ""))
        ' The commands' list is the grip and the three commands in one physical order; the handle is not an item of it.
        Dim grip As Integer = build.IndexOf("""win_Grip""", StringComparison.Ordinal)
        Dim minimize As Integer = build.IndexOf("""win_Minimize""", StringComparison.Ordinal)
        Dim maximize As Integer = build.IndexOf("""win_Maximize""", StringComparison.Ordinal)
        Dim closeItem As Integer = build.IndexOf("""win_Close""", StringComparison.Ordinal)
        Assert.True(grip >= 0 AndAlso grip < minimize AndAlso minimize < maximize AndAlso maximize < closeItem)
        ' The helper is the rows' wiring, once: the handle item and all six events, the click opening the handle's menu.
        Dim helper As String = MemberBody(chrome, "Private Sub AddChromePanelHandle(panel As ChromePanel)")
        Assert.Contains("ChromeSurface.CreatePanelHandleItem(panel.Name)", helper)
        For Each ev As String In {"PanelHandlePress", "PanelHandleDrag", "PanelHandleRelease", "PanelHandleKeyMove", "PanelHandleMoveModeChanged", "PanelHandleClick"}
            Assert.Contains("AddHandler panel.Host.Surface." & ev & ",", helper)
        Next
        Assert.Contains("ChromeMoveClick(panel)", helper)
        Dim row As String = MemberBody(chrome, "Private Sub BuildChromeRow(name As String, controls As IEnumerable(Of Control))")
        Assert.Contains("AddChromePanelHandle(panel)", row)
        Assert.DoesNotContain("PanelHandlePress", row)
    End Sub

    <Fact>
    Public Sub The_window_commands_are_movable_and_their_place_is_the_window_controls_key()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Assert.Contains("p.Name = Window_Controls_Panel_Name", MemberBody(chrome, "Private Shared Function ChromePanelMovable(p As ChromePanel) As Boolean"))
        Assert.Contains("Case Window_Controls_Panel_Name : Return modern_Preferences.ChromeWindowControlsPlace",
                        MemberBody(chrome, "Private Function ChromePanelPlaceText(p As ChromePanel) As String"))
        Assert.Contains("Case Window_Controls_Panel_Name : modern_Preferences.ChromeWindowControlsPlace = text",
                        MemberBody(chrome, "Private Sub SetChromePanelPlaceText(p As ChromePanel, text As String)"))
    End Sub

    <Fact>
    Public Sub Anchored_the_panel_is_the_physical_corner_unclamped_and_free_it_is_fitted()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Dim anchored As String = MemberBody(chrome, "Private Function ChromeAnchoredRect(p As ChromePanel) As Rectangle")
        Dim corner As Integer = anchored.IndexOf("If p.Name = Window_Controls_Panel_Name Then", StringComparison.Ordinal)
        Dim physical As Integer = anchored.IndexOf("left = media.Right - inset - size.Width", corner, StringComparison.Ordinal)
        Dim leaves As Integer = anchored.IndexOf("Return New Rectangle(left, top, size.Width, size.Height)", physical, StringComparison.Ordinal)
        Dim rows As Integer = anchored.IndexOf("If p.Name = ""chrome_Row1"" OrElse p.Name = ""chrome_Row2""", StringComparison.Ordinal)
        Assert.True(corner >= 0 AndAlso physical > corner AndAlso leaves > physical AndAlso rows > leaves,
                    "the corner branch returns before the rows' band is asked")
        ' The free branch goes through the fit, like the rows and the strip; the reading direction does not move this panel.
        Dim rect As String = MemberBody(chrome, "Private Function ChromePanelRect(p As ChromePanel) As Rectangle")
        Assert.Contains("ChromeFreeRect(p, place, p.Host.Surface.ScaleFactor)", rect)
        Assert.Contains("Return ChromeAnchoredRect(p)", rect)
        Dim start As String = MemberBody(chrome, "Private Shared Function ChromeStartIsRight(p As ChromePanel) As Boolean")
        Assert.Contains("p.Name <> Window_Controls_Panel_Name", start)
        For Each declared As String In {
            "Private Function ChromeFreeRect(p As ChromePanel, place As PanelPlace, factor As Single) As Rectangle",
            "Private Sub ChromeMoveDrag(p As ChromePanel, e As PanelHandleEventArgs)",
            "Private Sub ChromeMoveKey(p As ChromePanel, e As PanelHandleEventArgs)",
            "Private Sub ChromeStoreDrop(p As ChromePanel, start As Rectangle, dropped As Rectangle)"}
            Dim body As String = MemberBody(chrome, declared)
            Assert.Contains("ChromeStartIsRight(p)", body)
            Assert.DoesNotContain("Localization.IsRightToLeft()", body)
        Next
    End Sub

    <Fact>
    Public Sub An_anchored_top_row_asks_the_band_about_the_panels_actual_rectangle()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Dim anchored As String = MemberBody(chrome, "Private Function ChromeAnchoredRect(p As ChromePanel) As Rectangle")
        Dim band As Integer = anchored.IndexOf("ChromeLayout.PlaceRowBesideCornerPanel(", StringComparison.Ordinal)
        Assert.True(band >= 0)
        Dim before As String = anchored.Substring(0, band)
        ' The rectangle is where the panel stands now (the corner or the user's place), stretched to the band's far edge.
        Assert.Contains("ChromeControlsBandRect(controls)", before)
        Assert.Contains("ChromeLayout.BandObstacle(", before)
        Assert.DoesNotContain("controlsSize", before)
        Assert.DoesNotContain("controls.Host.Surface.PreferredPanelSize()", before)
        ' A free row never asks (section 3.8): the band is asked inside the anchored branch only.
        Dim rect As String = MemberBody(chrome, "Private Function ChromePanelRect(p As ChromePanel) As Rectangle")
        Assert.DoesNotContain("PlaceRowBesideCornerPanel", rect)
        ' Mid-drag the rows keep the band they had at the press; otherwise it is the panel's effective rectangle.
        Dim bandRect As String = MemberBody(chrome, "Private Function ChromeControlsBandRect(controls As ChromePanel) As Rectangle")
        Assert.Contains("If ChromeMoving(controls) Then Return chrome_Move_Start", bandRect)
        Assert.Contains("Return ChromePanelRect(controls)", bandRect)
        ' Row 2 stands under row 1 where row 1 REALLY stands (it may have wrapped below the window commands).
        Assert.Contains("top = ChromeAnchoredRect(chrome_Panels(0)).Bottom + UiScale.Px(ChromeMetrics.Gap, factor)", anchored)
    End Sub

    <Fact>
    Public Sub The_window_commands_stay_protected_and_are_not_judged_against_themselves()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Dim guarded As String = MemberBody(chrome, "Private Function ChromeProtectedRects(Optional moved As ChromePanel = Nothing")
        Assert.Contains("controls IsNot Nothing AndAlso controls IsNot moved", guarded)
        Assert.Contains("guarded.Add(ChromePanelRect(controls))", guarded)
        ' The transport bar is still protected while it is shown - it is what a dropped window-commands panel is judged against.
        Assert.Contains("guarded.Add(video_Chrome_Host.Bounds)", guarded)
        Assert.Contains("ChromeProtectedRects(p)", MemberBody(chrome, "Private Sub ChromeStoreDrop(p As ChromePanel, start As Rectangle, dropped As Rectangle)"))
        ' Raised above every other chrome window, and raised again after any movable panel was.
        Dim raise As String = MemberBody(chrome, "Private Sub ChromeRaiseProtectedSurfaces()")
        Assert.Contains("ChromeRaiseWindow(controls.Host)", raise)
        Assert.Contains("ChromeRaiseProtectedSurfaces()", MemberBody(chrome, "Private Sub ChromeRaiseMovedPanel(p As ChromePanel)"))
    End Sub

    <Fact>
    Public Sub The_window_commands_keep_their_own_reveal_zone_free_or_in_the_corner()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Dim zone As String = MemberBody(chrome, "Private Function ChromeHotZone(p As ChromePanel) As Rectangle")
        ' The generic free-panel zone is for the others; the window commands inflate their own rectangle inside the media.
        Assert.Contains("If p.Name <> Window_Controls_Panel_Name AndAlso ChromePanelIsFree(p) Then", zone)
        Dim own As Integer = zone.IndexOf("Case Window_Controls_Panel_Name", StringComparison.Ordinal)
        Assert.True(own >= 0)
        Assert.Contains("Rectangle.Inflate(r, zoneHeight, zoneHeight)", zone.Substring(own))
        Assert.Contains("zone.Intersect(media)", zone.Substring(own))
    End Sub

    <Fact>
    Public Sub The_minimum_tracking_size_is_still_built_from_the_panels_footprint()
        Dim border As String = Source("Main_Form.WindowBorder.vb")
        Dim minMax As String = MemberBody(border, "Private Sub ChromeWindowMinMax(ByRef m As Message)")
        Assert.Contains("ChromeMetrics.WindowControlsLogicalWidth() + 2 * ChromeMetrics.ScreenMargin", minMax)
        Assert.Contains("ChromeMetrics.WindowControlsLogicalHeight() + 2 * ChromeMetrics.ScreenMargin", minMax)
        ' The footprint names the handle's slot (two MinTarget + Gap slots: the pin's and the handle's).
        Dim metrics As String = Source("Chrome\ChromeMetrics.vb")
        Assert.Contains("(MinTarget + Gap) + (MinTarget + Gap) + 4 * button + 3 * Gap", metrics)
    End Sub

End Class
#End If
