#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' The pure half of the chrome's auto-hide cycle (117). The tick in <c>Main_Form.Chrome.vb</c>
''' feeds it the clock and the pointer state; everything that decides whether a surface is up -
''' the leave-to-hide countdown, the fade arithmetic and the pin algebra shared by the panels
''' and the recipients overlay - lives here, with no window and no timer, so tests can run the
''' cycle at any timestamp. Whole-file "#If Not NETFRAMEWORK", like the rest of the chrome.
''' </summary>
Friend NotInheritable Class ChromeAutoHide

    Private Sub New()
    End Sub

    ''' <summary>One surface's hide/reveal decision for a single tick. The countdown to hide starts
    ''' at the moment the pointer leaves (the caller refreshes <paramref name="lastActivityMs"/> for
    ''' as long as the surface is held up, so on the leave tick the countdown is zero) and runs for
    ''' <see cref="ChromeMetrics.PanelHideDelayMilliseconds"/>; re-entry, a hold or a reveal from the
    ''' hot zone cancels it. The parameter order follows the tick's own precedence:
    ''' over / pinned / hot zone win outright, deactivation drops an unpinned surface at once, and a
    ''' hold (open popup, path editing, keyboard mode) keeps it up only while the window is in use.
    ''' Returns the new wanted; <paramref name="lastActivityMs"/> is refreshed in place.</summary>
    Public Shared Function NextWanted(nowMs As Long,
                                      wanted As Boolean,
                                      ByRef lastActivityMs As Long,
                                      pinned As Boolean,
                                      over As Boolean,
                                      hold As Boolean,
                                      hotZoneReveal As Boolean,
                                      windowActive As Boolean) As Boolean
        If over OrElse pinned OrElse hotZoneReveal Then
            lastActivityMs = nowMs
            Return True
        End If
        ' Unpinned and the window not in use: let it go, whatever held it up a moment ago.
        If Not windowActive Then Return False
        If hold Then
            lastActivityMs = nowMs
            Return True
        End If
        If wanted AndAlso (nowMs - lastActivityMs) >= ChromeMetrics.PanelHideDelayMilliseconds Then Return False
        Return wanted
    End Function

    ''' <summary>The popup menu's dismissal rule for one poll of the foreground window (see
    ''' <c>ChromeMenu.PreFilterMessage</c>). The menu opens from no-activate panels, so the viewer
    ''' may be a background window for the menu's whole life: what closes it is the foreground
    ''' MOVING to a window that is neither the one it opened over nor one of the viewer's owned
    ''' forms - not the static fact that the viewer holds no focus.</summary>
    ''' <param name="ownedHandles">The viewer's own handle followed by its owned forms' handles.</param>
    Public Shared Function ShouldPopupCloseOnActivationChange(openForeground As IntPtr,
                                                              currentForeground As IntPtr,
                                                              ownedHandles As IEnumerable(Of IntPtr)) As Boolean
        If currentForeground = openForeground Then Return False
        If ownedHandles IsNot Nothing Then
            For Each handle As IntPtr In ownedHandles
                If handle <> IntPtr.Zero AndAlso handle = currentForeground Then Return False
            Next
        End If
        Return True
    End Function

    ''' <summary>A window with nothing to show holds its panels up (125 UX-313m): on the first run, or after the
    ''' last file of a folder has gone, there is no picture for a panel to cover and the window's only commands
    ''' (choose a file, choose a folder) are on them - a bar that waits for the pointer to find a 48 px band at
    ''' the top is a window with no way in. It is a HOLD, not a pin: it keeps the panels up while the window is
    ''' in use, the pin buttons keep telling the stored preference, and the ordinary countdown takes over from the
    ''' moment media appears. <paramref name="emptySinceMs"/> is the caller's own field (-1 = media is on screen):
    ''' a window is "empty" only after it has been so for <see cref="ChromeMetrics.EmptyWindowHoldDelayMilliseconds"/>,
    ''' so the gap while one file hands over to a file of another kind never raises the panels.</summary>
    Public Shared Function HoldsForEmptyWindow(mediaOnScreen As Boolean, nowMs As Long, ByRef emptySinceMs As Long) As Boolean
        If mediaOnScreen Then
            emptySinceMs = -1L
            Return False
        End If
        If emptySinceMs < 0L Then emptySinceMs = nowMs
        Return nowMs - emptySinceMs >= ChromeMetrics.EmptyWindowHoldDelayMilliseconds
    End Function

    ''' <summary>Where the reveal zone of the TOP rows ends: the bottom of the lowest of them plus the zone
    ''' margin, for every top row alike. Each row used to measure from its own bottom, so row 2 (which sits
    ''' below row 1) hung alone in the band between the two zones (125 UX-308).</summary>
    Public Shared Function TopHotZoneBottom(panelBottoms As IEnumerable(Of Integer), zoneMargin As Integer) As Integer
        Dim lowest As Integer = Integer.MinValue
        If panelBottoms IsNot Nothing Then
            For Each bottom As Integer In panelBottoms
                If bottom > lowest Then lowest = bottom
            Next
        End If
        If lowest = Integer.MinValue Then lowest = 0
        Return lowest + zoneMargin
    End Function

    ''' <summary>Whether a mouse press belongs to the popup that it closes (UX-103 of 123): a press outside
    ''' a visible popup dismisses it and goes no further, so a click-away never flips a file or pauses a
    ''' video. A press with no popup open, or one inside the popup, is none of this rule's business.</summary>
    Public Shared Function ShouldConsumePressThatClosesPopup(popupWasVisible As Boolean, pressIsOutside As Boolean) As Boolean
        Return popupWasVisible AndAlso pressIsOutside
    End Function

    ''' <summary>One fade step toward the target. Without animation (reduce motion, high contrast)
    ''' the alpha jumps. <paramref name="fadeElapsedMs"/> is the time since the previous tick, and
    ''' the caller passes 0 on the tick a transition starts, because the idle poll that noticed the
    ''' change is not part of the fade.</summary>
    Public Shared Function NextAlpha(target As Double,
                                     alpha As Double,
                                     fadeAllowed As Boolean,
                                     fadeElapsedMs As Integer) As Double
        If Not fadeAllowed Then Return target
        If alpha < target Then Return Math.Min(target, alpha + fadeElapsedMs / CDbl(ChromeMetrics.FadeMilliseconds))
        If alpha > target Then Return Math.Max(target, alpha - fadeElapsedMs / CDbl(ChromeMetrics.FadeMilliseconds))
        Return alpha
    End Function

    ''' <summary>The six persisted pin switches, as one value - the algebra below and the tests
    ''' both read and write it as a whole.</summary>
    Public Structure ChromePinState
        Public GlobalPinned As Boolean
        Public Row1Pinned As Boolean
        Public Row2Pinned As Boolean
        Public StatusPinned As Boolean
        ''' <summary>The recipients overlay's own pin (2026-10-04 revision of 117 Ф-2): it is a
        ''' fourth floating surface with a corner pin of its own, not a rider on the panels'.</summary>
        Public OverlayPinned As Boolean
        ''' <summary>The window-controls panel's own pin (188 §4): a fifth floating surface,
        ''' mapped explicitly below - the unknown-name fallback must never reach it.</summary>
        Public WindowControlsPinned As Boolean
    End Structure

    ''' <summary>A panel's own pin - the corner button's switch - by the panel's name. The
    ''' recipients overlay answers by its window name, exactly like a panel.</summary>
    Public Shared Function OwnPin(state As ChromePinState, panelName As String) As Boolean
        Select Case panelName
            Case "chrome_Row1" : Return state.Row1Pinned
            Case "chrome_Row2" : Return state.Row2Pinned
            Case "recipients_Overlay" : Return state.OverlayPinned
            Case "chrome_WindowControls" : Return state.WindowControlsPinned
            Case Else : Return state.StatusPinned
        End Select
    End Function

    Private Shared Sub SetOwnPin(ByRef state As ChromePinState, panelName As String, value As Boolean)
        Select Case panelName
            Case "chrome_Row1" : state.Row1Pinned = value
            Case "chrome_Row2" : state.Row2Pinned = value
            Case "recipients_Overlay" : state.OverlayPinned = value
            Case "chrome_WindowControls" : state.WindowControlsPinned = value
            Case Else : state.StatusPinned = value
        End Select
    End Sub

    ''' <summary>Whether a panel stays up: the global switch, or its own pin.</summary>
    Public Shared Function PanelPinned(state As ChromePinState, panelName As String) As Boolean
        Return state.GlobalPinned OrElse OwnPin(state, panelName)
    End Function

    ''' <summary>A pin button's click (117, the <c>ChromeTogglePin</c> rule). While the global
    ''' switch is on, unpinning one surface converts the switch into the individual pins of all the
    ''' others minus this one - otherwise the click would do nothing visible. Pinning an unpinned
    ''' surface sets its own pin. The window-controls panel joins the split like a panel
    ''' (188 §4): unpinning another surface leaves its effective pinned state alone.</summary>
    Public Shared Function TogglePin(state As ChromePinState, panelName As String) As ChromePinState
        Dim result As ChromePinState = state
        If PanelPinned(state, panelName) Then
            If state.GlobalPinned Then
                result.GlobalPinned = False
                result.Row1Pinned = True
                result.Row2Pinned = True
                result.StatusPinned = True
                result.WindowControlsPinned = True
            End If
            SetOwnPin(result, panelName, False)
        Else
            SetOwnPin(result, panelName, True)
        End If
        Return result
    End Function

End Class
#End If
