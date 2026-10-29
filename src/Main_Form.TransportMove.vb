#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

' The video transport bar is movable (L0281 T-5b, the owner's answer to Q281-2): a handle of its own at the end of the row,
' the same glyph and the same words as the other panels' handles, a stored place (ChromeTransportPlace) and the same fit,
' snap and ways back. The bar is a ChromeHost holding ordinary controls, not a ChromeSurface, so the gesture is this file's
' (the surface's own gesture code cannot serve it) - but every decision is the shared pure model: the rectangle arithmetic
' is TransportRules.FreeBarBand / FreeBarRect / DragFreeBar / NudgeFreeBar / DropFreeBar over FloatingPanelPlacement.
'
' Vertical movement only: the bar keeps its anchored width and horizontal position. That is a property of the geometry (the
' fit runs in a strip exactly as wide as the bar), not of a check. The bar stays a protected surface for the other panels
' (ChromeProtectedRects) and is judged against the window commands when it is the one being dropped. Its hot zone (any
' pointer movement over the video) and its hide policy are its own and unchanged; only a held handle keeps it up, as a
' held slider does.
Partial Public Class Main_Form

    ''' <summary>The bar's move handle (L0281 T-5b): the last stop of the bar's ring, after "more".</summary>
    Private WithEvents btn_Video_Move As NonFocusButton

    ''' <summary>The pointer is down on the handle (it may still become a click).</summary>
    Private transport_Move_Pressed As Boolean
    ''' <summary>The press travelled past the system drag size: it is a drag now, and its release is not a click.</summary>
    Private transport_Move_Dragged As Boolean
    ''' <summary>The bar's rectangle when the gesture began - where a refused or canceled move returns to.</summary>
    Private transport_Move_Start As Rectangle
    ''' <summary>The pointer's screen position at the press.</summary>
    Private transport_Move_Press As Point
    ''' <summary>Where the host stands right now during a move (already fitted); empty while nothing is moving.</summary>
    Private transport_Move_Live As Rectangle
    ''' <summary>The move is the keyboard's (the handle's move mode in the ring), not the pointer's.</summary>
    Private transport_Move_Keyboard As Boolean
    ''' <summary>The release of a drag also raises the button's Click (in either order with MouseUp); that click is not a menu.</summary>
    Private transport_Move_SuppressClick As Boolean

    ' --- the handle ---------------------------------------------------------------------------------

    ''' <summary>Builds the handle at the end of the bar (called by the bar's builder, before the bar's mouse-move hook is wired).</summary>
    Private Sub BuildTransportMoveHandle(barHeight As Integer)
        btn_Video_Move = NewBarButton("btn_Video_Move", UiScale.Px(ChromeMetrics.MinTarget, ChromeScale()), barHeight)
        btn_Video_Move.Cursor = Cursors.SizeNS
        video_Controls.Controls.Add(btn_Video_Move)
        SetVideoGlyph(btn_Video_Move, ChromeSurface.PanelHandleGlyphId)
    End Sub

    ''' <summary>The handle's words follow the language (the bar's other names are re-read in the same pass).</summary>
    Private Sub LocalizeTransportMoveHandle()
        If btn_Video_Move Is Nothing Then Return
        Dim words As String = Localization.T("Переместить панель")
        btn_Video_Move.AccessibleName = words
        If toolTip IsNot Nothing Then toolTip.SetToolTip(btn_Video_Move, words)
    End Sub

    ''' <summary>The handle is drawn in its pressed state while the keyboard move mode is on (the mode's visible cue, as on the
    ''' panels' handles).</summary>
    Private Sub SyncTransportMoveLook()
        If btn_Video_Move Is Nothing Then Return
        btn_Video_Move.BackColor = If(transport_Move_Keyboard, chrome_Theme.ItemPressed, chrome_Theme.PanelFill)
    End Sub

    ' --- the place and the rectangles ------------------------------------------------------------------

    ''' <summary>The stored place of the bar as text; empty = anchored.</summary>
    Private Function TransportPlaceText() As String
        If modern_Preferences Is Nothing Then Return ""
        Return modern_Preferences.ChromeTransportPlace
    End Function

    ''' <summary>True and the place when the bar is free; a missing, unreadable or future value is anchored.</summary>
    Private Function TransportFree(ByRef place As PanelPlace) As Boolean
        place = New PanelPlace(PanelCorner.TopLeft, 0, 0)
        Return FloatingPanelPlacement.TryDecode(TransportPlaceText(), place)
    End Function

    Private Function TransportPlaceIsFree() As Boolean
        Dim place As PanelPlace
        Return TransportFree(place)
    End Function

    ''' <summary>A handle gesture has moved the host: its live rectangle is the placement until the gesture ends.</summary>
    Private Function TransportMoving() As Boolean
        Return Not transport_Move_Live.IsEmpty
    End Function

    ''' <summary>Where the bar stands by the default rules, in screen coordinates: the bottom of the media panel, a margin up,
    ''' nearly its full width and centred - today's placement, unchanged. Also what a drop is measured against for the snap.</summary>
    Private Function TransportAnchoredRect() As Rectangle
        Dim margin As Integer = UiScale.Px(ChromeMetrics.TransportMargin, ChromeScale())
        Dim w As Integer = panel_Media.ClientSize.Width - margin * 2
        If w < UiScale.Px(ChromeMetrics.TransportMinimumWidth, ChromeScale()) Then w = Math.Max(0, panel_Media.ClientSize.Width)
        Dim origin As Point = panel_Media.PointToScreen(New Point((panel_Media.ClientSize.Width - w) \ 2,
                                                                  panel_Media.ClientSize.Height - video_Controls.Height - margin))
        Return New Rectangle(origin, New Size(w, video_Controls.Height))
    End Function

    ''' <summary>The bar's effective rectangle: the live one mid-gesture, the stored place resolved in the allowed area as it is
    ''' now and fitted (section 3.4: computed afresh, never written back by a resize), or the anchored one.</summary>
    Private Function TransportPlacedRect(anchored As Rectangle) As Rectangle
        If TransportMoving() Then Return transport_Move_Live
        Dim place As PanelPlace
        If TransportFree(place) Then
            Dim factor As Single = ChromeScale()
            Return TransportRules.FreeBarRect(place, anchored, ChromeAllowedArea(factor), factor)
        End If
        Return anchored
    End Function

    ''' <summary>The strip every move of the bar is fitted in: as wide as the bar, as tall as the allowed area.</summary>
    Private Function TransportMoveBand() As Rectangle
        Return TransportRules.FreeBarBand(TransportAnchoredRect(), ChromeAllowedArea(ChromeScale()))
    End Function

    ' --- the pointer gesture ---------------------------------------------------------------------------

    ''' <summary>A held handle is the bar's to keep up: the countdown to hiding it restarts.</summary>
    Private Sub TransportHoldBar()
        If video_Controls Is Nothing OrElse Not video_Controls.Visible Then Return
        video_Controls_Hide_Timer.Stop()
        video_Controls_Hide_Timer.Start()
    End Sub

    ''' <summary>A press on the handle: the viewer takes the keyboard (the bar is never activated), the press and the bar's
    ''' rectangle are remembered. Nothing moves until the pointer passes the drag size - a press that does not travel is a
    ''' click (the handle's menu).</summary>
    Private Sub Btn_Video_Move_MouseDown(sender As Object, e As MouseEventArgs) Handles btn_Video_Move.MouseDown
        If e.Button <> MouseButtons.Left OrElse video_Chrome_Host Is Nothing Then Return
        ChromeTakeKeyboardForViewer()
        transport_Move_Pressed = True
        transport_Move_Dragged = False
        transport_Move_SuppressClick = False
        transport_Move_Keyboard = False
        transport_Move_Live = Rectangle.Empty
        transport_Move_Press = Cursor.Position
        transport_Move_Start = video_Chrome_Host.Bounds
        TransportHoldBar()
    End Sub

    ''' <summary>The pointer moved with the handle held: the bar is the start rectangle offset by the pointer's VERTICAL travel,
    ''' passed through the fit BEFORE the host's bounds are set. The pointer is read from the screen, not from the event: the
    ''' control moves under it.</summary>
    Private Sub Btn_Video_Move_MouseMove(sender As Object, e As MouseEventArgs) Handles btn_Video_Move.MouseMove
        If Not transport_Move_Pressed OrElse video_Chrome_Host Is Nothing Then Return
        Dim here As Point = Cursor.Position
        If Not transport_Move_Dragged Then
            If Not ChromeSurface.ExceedsDragSize(transport_Move_Press, here, SystemInformation.DragSize) Then Return
            transport_Move_Dragged = True
            transport_Move_Live = transport_Move_Start
            ChromeRaiseProtectedSurfaces()
        End If
        TransportSetMoveLive(TransportRules.DragFreeBar(transport_Move_Start, here.Y - transport_Move_Press.Y, TransportMoveBand()))
    End Sub

    Private Sub TransportSetMoveLive(r As Rectangle)
        transport_Move_Live = r
        If video_Chrome_Host.Bounds <> r Then video_Chrome_Host.Bounds = r
        TransportHoldBar()
    End Sub

    ''' <summary>The button went up: after a drag the move ends (decided exactly as a drop); a press that never travelled is left
    ''' to the click.</summary>
    Private Sub Btn_Video_Move_MouseUp(sender As Object, e As MouseEventArgs) Handles btn_Video_Move.MouseUp
        If e.Button <> MouseButtons.Left OrElse Not transport_Move_Pressed Then Return
        transport_Move_Pressed = False
        If Not transport_Move_Dragged Then Return
        ' The release of a drag raises the button's Click as well, before or after this event: that click is not the menu.
        transport_Move_SuppressClick = True
        If IsHandleCreated Then BeginInvoke(New Action(Sub() transport_Move_SuppressClick = False))
        TransportFinishMove(True)
    End Sub

    ''' <summary>The capture went elsewhere while the handle was held (a dialog, Alt+Tab, the host hidden): a drag in progress
    ''' is canceled - the bar returns to where it began; a press that never became a drag just ends.</summary>
    Private Sub Btn_Video_Move_MouseCaptureChanged(sender As Object, e As EventArgs) Handles btn_Video_Move.MouseCaptureChanged
        If Not transport_Move_Pressed OrElse btn_Video_Move.Capture Then Return
        transport_Move_Pressed = False
        If transport_Move_Dragged Then TransportFinishMove(False)
    End Sub

    ''' <summary>A press on the handle that did not travel, or its accessible default action: the handle's menu opens under it.</summary>
    Private Sub Btn_Video_Move_Click(sender As Object, e As EventArgs) Handles btn_Video_Move.Click
        If transport_Move_SuppressClick OrElse transport_Move_Dragged Then Return
        TransportHoldBar()
        TransportShowHandleMenu()
    End Sub

    ' --- ending a move ---------------------------------------------------------------------------------

    ''' <summary>Ends a move: the gesture's state is cleared first (the placement is the ordinary one again), a committed move is
    ''' decided and stored once, and every panel is placed afresh - the bar included, which returns to its start when nothing
    ''' was stored, and the anchored status strip, which follows the bar's state (section 3.8).</summary>
    Private Sub TransportFinishMove(commit As Boolean)
        Dim start As Rectangle = transport_Move_Start
        Dim dropped As Rectangle = transport_Move_Live
        transport_Move_Pressed = False
        transport_Move_Dragged = False
        transport_Move_Live = Rectangle.Empty
        transport_Move_Keyboard = False
        SyncTransportMoveLook()
        Try
            If commit AndAlso Not dropped.IsEmpty Then TransportStoreDrop(start, dropped)
        Catch ex As Exception
            AppFileLogger.LogException("Chrome: finishing a transport bar move", ex)
        End Try
        PlaceChromePanels()
        ChromeRaiseProtectedSurfaces()
        TransportHoldBar()
    End Sub

    ''' <summary>The drop decision of section 3.5 for the bar: nothing to write, the place cleared, or a new place stored (one
    ''' write per drop). The bar is judged against the window commands only - it is the one being dropped.</summary>
    Private Sub TransportStoreDrop(start As Rectangle, dropped As Rectangle)
        Dim factor As Single = ChromeScale()
        Dim allowed As Rectangle = ChromeAllowedArea(factor)
        Dim anchored As Rectangle = TransportAnchoredRect()
        Dim result As Rectangle
        Dim stored As String
        Dim snapped As Boolean
        Select Case TransportRules.DropFreeBar(start, dropped, anchored, allowed, ChromeProtectedRects(excludeTransport:=True),
                                               UiScale.Px(ChromeMetrics.AnchorSnap, factor), result)
            Case PanelDropOutcome.Anchor
                stored = ""
                snapped = True
            Case PanelDropOutcome.Placed
                stored = FloatingPanelPlacement.Encode(FloatingPanelPlacement.FromRectangle(result, TransportRules.FreeBarBand(anchored, allowed), factor))
            Case Else
                Return
        End Select
        ' A drop near the anchor is the third way back (section 3.7): it answers like the other two, moved or already there.
        If stored = TransportPlaceText() Then
            If snapped Then ChromeSayPlaces(False)
            Return
        End If
        TransportWritePlace(stored)
        If snapped Then ChromeSayPlaces(True)
    End Sub

    ''' <summary>Stores the bar's place the way a pin toggle stores its switch: save now, and on a failed write the settings flush retries.</summary>
    Private Sub TransportWritePlace(text As String)
        If modern_Preferences Is Nothing Then Return
        modern_Preferences.ChromeTransportPlace = text
        ChromeSavePlaces()
    End Sub

    ''' <summary>Ends a handle gesture without storing anything (a command that returns the panels ends the move in progress).</summary>
    Private Sub TransportAbandonMove()
        If btn_Video_Move IsNot Nothing AndAlso transport_Move_Pressed Then btn_Video_Move.Capture = False
        transport_Move_Pressed = False
        transport_Move_Dragged = False
        transport_Move_Live = Rectangle.Empty
        transport_Move_Keyboard = False
        SyncTransportMoveLook()
    End Sub

    ' --- the keyboard route (section 3.6) -----------------------------------------------------------------

    ''' <summary>The handle's keys in the F10 ring, for the key dispatch to call BEFORE the ring steps: with the handle as the
    ''' ring's stop, Enter or Space starts the keyboard move, Up and Down move the bar by one minimum target (Shift: five), Left
    ''' and Right are the move's too (the bar has no horizontal room), and Enter, Space or Escape end it. True = the key was the
    ''' handle's and goes no further. Every other key, a Ctrl or Alt chord and any key on another stop answer False.</summary>
    Private Function TransportMoveKey(keyData As Keys) As Boolean
        If btn_Video_Move Is Nothing OrElse Not Object.ReferenceEquals(video_Ring_Control, btn_Video_Move) Then Return False
        Dim code As Keys = keyData And Keys.KeyCode
        Dim modifiers As Keys = keyData And Keys.Modifiers
        If (modifiers And (Keys.Control Or Keys.Alt)) <> Keys.None Then Return False
        If Not transport_Move_Keyboard Then
            If modifiers = Keys.None AndAlso (code = Keys.Enter OrElse code = Keys.Space) Then
                TransportBeginKeyboardMove()
                Return True
            End If
            Return False
        End If
        Dim stepsX As Integer
        Dim stepsY As Integer
        If ChromeSurface.PanelKeySteps(code, (modifiers And Keys.Shift) = Keys.Shift, stepsX, stepsY) Then
            If stepsY <> 0 Then
                TransportSetMoveLive(TransportRules.NudgeFreeBar(transport_Move_Live, stepsY, UiScale.Px(ChromeMetrics.MinTarget, ChromeScale()), TransportMoveBand()))
            End If
            Return True
        End If
        If code = Keys.Enter OrElse code = Keys.Space OrElse code = Keys.Escape Then
            TransportFinishMove(True)
            Return True
        End If
        Return False
    End Function

    Private Sub TransportBeginKeyboardMove()
        If video_Chrome_Host Is Nothing Then Return
        transport_Move_Keyboard = True
        transport_Move_Pressed = False
        transport_Move_Dragged = False
        transport_Move_Start = video_Chrome_Host.Bounds
        transport_Move_Live = transport_Move_Start
        SyncTransportMoveLook()
        TransportHoldBar()
        ChromeRaiseProtectedSurfaces()
    End Sub

    ''' <summary>The ring moved off the handle or left the bar while the keyboard move was on: it ends as an Enter would end it.</summary>
    Private Sub TransportEndKeyboardMove()
        If transport_Move_Keyboard Then TransportFinishMove(True)
    End Sub

    ' --- the ways back (section 3.7) --------------------------------------------------------------------

    ''' <summary>The handle's menu: this panel back to its place (shown but unavailable while the bar is anchored, like any row
    ''' that cannot act now) and all panels back - the same two rows as every other panel's handle menu. It opens under the
    ''' handle, or above it when the bar stands low, through the chrome's own popup: the bar is never activated and Escape
    ''' closes the menu before it reaches the viewer.</summary>
    Private Sub TransportShowHandleMenu()
        If btn_Video_Move Is Nothing Then Return
        Dim entries As New List(Of ChromeMenuItem) From {
            New ChromeMenuItem("chrome_places_one") With {
                .Caption = Localization.T("Вернуть панель на место"), .Enabled = TransportPlaceIsFree(),
                .Action = Sub() TransportResetPlace()},
            New ChromeMenuItem("chrome_places_all") With {
                .Caption = Localization.T("Вернуть все панели на места"),
                .Action = Sub() ChromeResetPlaces(Nothing)}}
        Dim location As Point = btn_Video_Move.PointToScreen(New Point(0, btn_Video_Move.Height))
        Dim anchorTop As Integer = btn_Video_Move.PointToScreen(Point.Empty).Y
        ChromeMenu.ShowPopup(Me, location, entries, chrome_Theme, ChromeScale(), Localization.IsRightToLeft(), chrome_Text_Font, chrome_Symbol_Font,
                             focusFirstRow:=chrome_Keyboard_Mode, anchorTop:=anchorTop)
    End Sub

    ''' <summary>Returns the bar alone to its anchored place. True when a place was cleared; the Notify line answers either way.</summary>
    Private Function TransportResetPlace() As Boolean
        TransportAbandonMove()
        Dim changed As Boolean = TransportPlaceText().Length > 0
        If changed Then TransportWritePlace("")
        PlaceChromePanels()
        ChromeSayPlaces(changed)
        Return changed
    End Function

End Class
#End If
