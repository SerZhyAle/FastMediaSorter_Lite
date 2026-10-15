#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' The Escape ladder of the modern viewer (UX-101 of ticket 123). Esc used to mean "close the
''' window" everywhere except full screen, so a user who pressed it to back out of a zoom, an OCR
''' overlay, the F1 card or a slideshow lost the program instead. Now one press undoes exactly the
''' ONE layer that is on top, and only when no layer is left does the legacy behaviour stand
''' (the window closes - Q123-1).
'''
''' Pure on purpose: <see cref="[Next]"/> reads a snapshot of "what is on top" and names the single
''' rung to undo; <c>Main_Form.KeyboardInput.vb</c> only builds the snapshot and acts on the answer,
''' so the order is a table that a test can walk. Whole-file "#If Not NETFRAMEWORK": the net48
''' fallback keeps its historical Esc (maintenance policy).
''' </summary>
Friend NotInheritable Class EscapeLadder

    Private Sub New()
    End Sub

    ''' <summary>What one Esc press undoes, top layer first. <see cref="Rung.Close"/> is the bottom of
    ''' the ladder: nothing is left to undo, so the window closes.</summary>
    Public Enum Rung
        ''' <summary>A chrome popup menu is open.</summary>
        ClosePopup = 0
        ''' <summary>The F1 help card covers the picture.</summary>
        CloseHelpCard = 1
        ''' <summary>The F10 panel keyboard mode is on.</summary>
        LeaveKeyboardMode = 2
        ''' <summary>A translation overlay is painted over the picture.</summary>
        HideOcrOverlay = 3
        ''' <summary>Zoomed in or panned: back to Fit.</summary>
        ZoomToFit = 4
        ''' <summary>A slideshow (sequential or random) is running.</summary>
        StopSlideshow = 5
        ''' <summary>Full screen or the panel-less full screen.</summary>
        LeaveFullScreen = 6
        ''' <summary>Nothing left to undo.</summary>
        Close = 7
        ''' <summary>A connection to a remote resource is being dialled (124 UX-211): Esc cancels the attempt. Numbered
        ''' last so the older values stay what they were; its place in the order is <see cref="[Next]"/>'s.</summary>
        CancelConnect = 8
        ''' <summary>Text is selected in a text document (124 UX-216): Esc lets go of the selection and leaves
        ''' the file. Numbered after the older values like <see cref="CancelConnect"/>.</summary>
        ClearTextSelection = 9
    End Enum

    ''' <summary>A snapshot of the layers that can be on top, read by the caller at the moment of the press.</summary>
    Public Structure State
        Public PopupOpen As Boolean
        Public HelpCardShown As Boolean
        Public KeyboardModeOn As Boolean
        Public OcrOverlayShown As Boolean
        Public ZoomedOrPanned As Boolean
        Public SlideshowRunning As Boolean
        Public FullScreen As Boolean
        ''' <summary>A connection to a resource is being dialled and can be cancelled.</summary>
        Public Connecting As Boolean
        ''' <summary>Text is selected in the text document on screen.</summary>
        Public TextSelectionShown As Boolean
    End Structure

    ''' <summary>The single layer this Esc undoes: popup, F1 card, F10 mode, OCR overlay, zoom, slideshow,
    ''' full screen, and finally <see cref="Rung.Close"/>. Never two layers for one press.</summary>
    Public Shared Function [Next](current As State) As Rung
        If current.PopupOpen Then Return Rung.ClosePopup
        ' An attempt in flight is what Esc means while it runs: nothing else under it matters until it ends (124 UX-211).
        If current.Connecting Then Return Rung.CancelConnect
        If current.HelpCardShown Then Return Rung.CloseHelpCard
        If current.KeyboardModeOn Then Return Rung.LeaveKeyboardMode
        ' A selection is the lightest layer there is - one press and it is gone, before anything is undone that
        ' costs the user a view (124 UX-216).
        If current.TextSelectionShown Then Return Rung.ClearTextSelection
        If current.OcrOverlayShown Then Return Rung.HideOcrOverlay
        If current.ZoomedOrPanned Then Return Rung.ZoomToFit
        If current.SlideshowRunning Then Return Rung.StopSlideshow
        If current.FullScreen Then Return Rung.LeaveFullScreen
        Return Rung.Close
    End Function

End Class

''' <summary>
''' The full-screen flags after F11 (UX-106 of 123). F11 used to invert BOTH flags, so from the plain full
''' screen (F7) it produced "not full, super" and the layout then cleared the super flag: F7 then F11 left
''' full screen instead of entering the panel-less one. Now F11 from the plain full screen keeps full screen
''' and adds the panel-less mode, and the next F11 returns to where it came from. Pure, so the table is a test.
''' </summary>
Friend NotInheritable Class FullScreenRules

    Private Sub New()
    End Sub

    ''' <summary>The three pieces of state F11 reads and writes.</summary>
    Public Structure Modes
        Public IsFull As Boolean
        Public IsSuper As Boolean
        ''' <summary>The panel-less mode was entered from the plain full screen, so F11 again goes back to it
        ''' rather than all the way to the window.</summary>
        Public SuperEnteredFromFull As Boolean
    End Structure

    ''' <summary>The state after one F11 press.</summary>
    Public Shared Function AfterSuperToggle(current As Modes) As Modes
        Dim result As Modes
        If current.IsSuper Then
            ' Leaving the panel-less screen: back to the plain full screen it was entered from, else out.
            result.IsFull = current.SuperEnteredFromFull
            result.IsSuper = False
            result.SuperEnteredFromFull = False
        Else
            ' Entering it, from the window or from the plain full screen: the screen is full either way.
            result.IsFull = True
            result.IsSuper = True
            result.SuperEnteredFromFull = current.IsFull
        End If
        Return result
    End Function

End Class
#End If
