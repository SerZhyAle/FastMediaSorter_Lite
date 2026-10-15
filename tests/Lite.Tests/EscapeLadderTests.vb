#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' The Escape ladder (UX-101 of ticket 123): one press undoes exactly one layer, top first, and the
''' window closes only when no layer is left. The pure function is walked over every combination of
''' the seven layers, and a source read pins where <c>KeybUse</c> answers Esc.
''' </summary>
Public Class EscapeLadderTests

    ''' <summary>The documented order, top first - the table the whole feature is.</summary>
    Private Shared ReadOnly Order As EscapeLadder.Rung() = {
        EscapeLadder.Rung.ClosePopup, EscapeLadder.Rung.CloseHelpCard, EscapeLadder.Rung.LeaveKeyboardMode,
        EscapeLadder.Rung.HideOcrOverlay, EscapeLadder.Rung.ZoomToFit, EscapeLadder.Rung.StopSlideshow,
        EscapeLadder.Rung.LeaveFullScreen}

    Private Shared Function Build(mask As Integer) As EscapeLadder.State
        Dim state As New EscapeLadder.State
        state.PopupOpen = (mask And 1) <> 0
        state.HelpCardShown = (mask And 2) <> 0
        state.KeyboardModeOn = (mask And 4) <> 0
        state.OcrOverlayShown = (mask And 8) <> 0
        state.ZoomedOrPanned = (mask And 16) <> 0
        state.SlideshowRunning = (mask And 32) <> 0
        state.FullScreen = (mask And 64) <> 0
        Return state
    End Function

    <Fact>
    Public Sub Nothing_on_top_means_the_window_closes()
        Assert.Equal(EscapeLadder.Rung.Close, EscapeLadder.Next(New EscapeLadder.State))
    End Sub

    <Fact>
    Public Sub Every_combination_undoes_exactly_the_topmost_layer()
        For mask As Integer = 0 To 127
            Dim expected As EscapeLadder.Rung = EscapeLadder.Rung.Close
            For layer As Integer = 0 To 6
                If (mask And (1 << layer)) <> 0 Then
                    expected = Order(layer)
                    Exit For
                End If
            Next
            Assert.Equal(expected, EscapeLadder.Next(Build(mask)))
        Next
    End Sub

    <Fact>
    Public Sub Close_is_never_answered_while_any_layer_is_up()
        For mask As Integer = 1 To 127
            Assert.NotEqual(EscapeLadder.Rung.Close, EscapeLadder.Next(Build(mask)))
        Next
    End Sub

    <Fact>
    Public Sub Peeling_a_full_stack_takes_one_press_per_layer_in_the_documented_order()
        ' Popup, F1 card, F10 mode, translation, zoom, slideshow and full screen all at once.
        Dim mask As Integer = 127
        Dim seen As New List(Of EscapeLadder.Rung)()
        While True
            Dim rung As EscapeLadder.Rung = EscapeLadder.Next(Build(mask))
            If rung = EscapeLadder.Rung.Close Then Exit While
            seen.Add(rung)
            ' The press removes exactly that layer.
            mask = mask And Not (1 << Array.IndexOf(Order, rung))
        End While
        Assert.Equal(Order.ToList(), seen)
    End Sub

    <Fact>
    Public Sub A_slideshow_in_full_screen_takes_two_presses_and_the_window_stays()
        ' Acceptance 3 of 123: Esc stops the slideshow and the window stays; the next Esc leaves full screen.
        Dim state As New EscapeLadder.State With {.SlideshowRunning = True, .FullScreen = True}
        Assert.Equal(EscapeLadder.Rung.StopSlideshow, EscapeLadder.Next(state))
        state.SlideshowRunning = False
        Assert.Equal(EscapeLadder.Rung.LeaveFullScreen, EscapeLadder.Next(state))
        state.FullScreen = False
        Assert.Equal(EscapeLadder.Rung.Close, EscapeLadder.Next(state))
    End Sub

    ''' <summary>Acceptance 2 of 123: translation hides, then zoom returns to Fit, then (windowed) the window closes.</summary>
    <Fact>
    Public Sub Zoom_with_a_translation_shown_peels_in_two_presses_then_closes()
        Dim state As New EscapeLadder.State With {.OcrOverlayShown = True, .ZoomedOrPanned = True}
        Assert.Equal(EscapeLadder.Rung.HideOcrOverlay, EscapeLadder.Next(state))
        state.OcrOverlayShown = False
        Assert.Equal(EscapeLadder.Rung.ZoomToFit, EscapeLadder.Next(state))
        state.ZoomedOrPanned = False
        Assert.Equal(EscapeLadder.Rung.Close, EscapeLadder.Next(state))
    End Sub

    ''' <summary>124 UX-216: a text selection is the lightest layer there is - Esc lets go of it first, under the popup,
    ''' the F1 card and the F10 mode, and above everything that costs the user a view.</summary>
    <Fact>
    Public Sub Esc_lets_go_of_a_text_selection_before_zoom_slideshow_and_full_screen()
        Dim state As New EscapeLadder.State With {.TextSelectionShown = True, .ZoomedOrPanned = True, .SlideshowRunning = True, .FullScreen = True}
        Assert.Equal(EscapeLadder.Rung.ClearTextSelection, EscapeLadder.Next(state))
        state.TextSelectionShown = False
        Assert.Equal(EscapeLadder.Rung.ZoomToFit, EscapeLadder.Next(state))

        Assert.Equal(EscapeLadder.Rung.CloseHelpCard, EscapeLadder.Next(New EscapeLadder.State With {.TextSelectionShown = True, .HelpCardShown = True}))
        Assert.Equal(EscapeLadder.Rung.LeaveKeyboardMode, EscapeLadder.Next(New EscapeLadder.State With {.TextSelectionShown = True, .KeyboardModeOn = True}))
        Assert.Equal(EscapeLadder.Rung.ClearTextSelection, EscapeLadder.Next(New EscapeLadder.State With {.TextSelectionShown = True, .OcrOverlayShown = True}))
        Assert.Equal(EscapeLadder.Rung.Close, EscapeLadder.Next(New EscapeLadder.State With {.TextSelectionShown = False}))
    End Sub

    ''' <summary>The selection rung is answered by the same Esc step that answers every other (one layer per press).</summary>
    <Fact>
    Public Sub The_selection_rung_is_acted_on_and_the_state_reads_the_selection()
        Dim source As String = KeyboardSource()
        Assert.Contains("state.TextSelectionShown = HasTextSelection()", source)
        Assert.Contains("Case EscapeLadder.Rung.ClearTextSelection", source)
        Assert.Contains("ClearTextSelection()", source)
    End Sub

    ' --- where KeybUse answers it -------------------------------------------------------

    ''' <summary>124 UX-211: while a connection is being dialled, Esc cancels it - after a popup, before the card and the rest.</summary>
    <Fact>
    Public Sub Esc_cancels_a_connection_that_is_being_dialled()
        Dim state As New EscapeLadder.State With {.Connecting = True, .ZoomedOrPanned = True, .FullScreen = True, .SlideshowRunning = True}
        Assert.Equal(EscapeLadder.Rung.CancelConnect, EscapeLadder.Next(state))
        state.PopupOpen = True
        Assert.Equal(EscapeLadder.Rung.ClosePopup, EscapeLadder.Next(state))
        state.PopupOpen = False
        state.HelpCardShown = True
        Assert.Equal(EscapeLadder.Rung.CancelConnect, EscapeLadder.Next(state))
        state.Connecting = False
        Assert.Equal(EscapeLadder.Rung.CloseHelpCard, EscapeLadder.Next(state))
    End Sub

    ''' <summary>The Esc gate must come BEFORE the SlideShowStop that every other key reaches: that call used
    ''' to run for Esc too, so one press stopped the slideshow and closed the window.</summary>
    <Fact>
    Public Sub KeybUse_answers_Escape_before_it_stops_the_slideshow()
        Dim source As String = KeyboardSource()
        Dim start As Integer = source.IndexOf("Public Sub KeybUse", StringComparison.Ordinal)
        Assert.True(start >= 0)
        Dim body As String = source.Substring(start)
        Dim escapeAt As Integer = body.IndexOf("If e.KeyData = Keys.Escape Then", StringComparison.Ordinal)
        Dim stopAt As Integer = body.IndexOf(vbLf & "        SlideShowStop()", StringComparison.Ordinal)
        Dim slotAt As Integer = body.IndexOf("Not IsRecipientSlotConfigured(recipient_Slot) Then Return", StringComparison.Ordinal)
        Assert.True(escapeAt > 0, "the Esc gate is missing")
        Assert.True(stopAt > escapeAt, "the Esc gate must precede the unconditional SlideShowStop")
        Assert.True(escapeAt > slotAt, "the Esc gate belongs after the archive/slot gates, with the other keys")
        Assert.Contains("RunEscapeStep()", body.Substring(escapeAt, 300))
    End Sub

    Private Shared Function KeyboardSource() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src", "Main_Form.KeyboardInput.vb")
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src/Main_Form.KeyboardInput.vb not found above " & AppContext.BaseDirectory)
    End Function

End Class
#End If
