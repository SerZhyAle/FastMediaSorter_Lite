#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Windows.Forms

' Custom keyboard shortcuts in the viewer - §3.5 of SPECIFICATION_SETTINGS_EXPANSION.
' The model, the parsing and the conflict rules live in CustomHotkeys.vb; what is here is
' the two lines of glue KeybUse needs and the one Select Case that turns an action id
' back into the call the historical branch would have made.
'
' Deliberately NOT a rewrite of KeybUse. The map holds overrides only, so a profile that
' has never been to the shortcuts dialog produces an empty dictionary, TryHandleCustomHotkey
' returns False on the first line, and every key reaches exactly the branch it always did.
Partial Public Class Main_Form

    ''' <summary>Overrides only, reloaded whenever the dialog writes the profile.</summary>
    Private custom_Hotkeys As Dictionary(Of String, String)

    Friend Sub ReloadCustomHotkeys()
        custom_Hotkeys = CustomHotkeys.Load(If(modern_Preferences Is Nothing, "{}", modern_Preferences.CustomHotkeysJson))
        ' The recipients overlay writes each slot's key on its row: a rebinding must reach it now, not at the next
        ' rebuild (125 UX-334). Nothing exists to redraw while the form is still loading.
        If recipients_Overlay IsNot Nothing AndAlso IsHandleCreated Then ApplyRecipientsOverlay()
    End Sub

    ''' <summary>The key a recipient slot answers to RIGHT NOW, for the settings grid's first column (125 UX-334):
    ''' the live map, not the factory digit - a slot rebound to Q is labelled Q, one with its key taken away "-".</summary>
    Friend Function SlotKeyLabel(slot As Integer) As String
        Dim combo As String = ChromeMenuRules.HintFor(custom_Hotkeys, CustomHotkeys.RecipientActionId(slot), CustomHotkeys.SlotDigit(slot))
        Return If(combo.Length = 0, "-", combo)
    End Function

    ''' <summary>The same for the delete row ("DEL" while the factory key stands).</summary>
    Friend Function DeleteKeyLabel() As String
        Dim combo As String = ChromeMenuRules.HintFor(custom_Hotkeys, "deleteFile", "DEL")
        If combo.Length = 0 Then Return "-"
        Return If(String.Equals(combo, "Delete", StringComparison.Ordinal), "DEL", combo)
    End Function

    ''' <summary>
    ''' Returns True when this key press belongs to the custom map and must not travel on
    ''' to the historical dispatch - either because a moved action answered it, or because
    ''' it is the abandoned home of one and answering it would undo the move.
    ''' </summary>
    Private Function TryHandleCustomHotkey(e As KeyEventArgs, wasSlideShow As Boolean) As Boolean
        If custom_Hotkeys Is Nothing OrElse custom_Hotkeys.Count = 0 Then Return False

        Dim combo As String = CustomHotkeys.Format(e.KeyData)
        If combo.Length = 0 Then Return False

        Dim actionId As String = CustomHotkeys.OwnerOfOverride(custom_Hotkeys, combo)
        If actionId.Length > 0 Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1650: custom hotkey " & combo & " -> " & actionId)
            ' KeybUse asks the custom map before it stops the slideshow (so a digit bound to
            ' an action works whatever its slot holds - audit 047, C-19), so the stop is
            ' done here - except for a slot with no folder, which is not an action at all.
            ' A playback action is not navigation either (119 §5.0.2): the speed / seek /
            ' A-B keys work INSIDE the clip a slideshow is waiting on, so they must not
            ' stop it - the actions that mean "I am inspecting this clip" stop it
            ' themselves (frame step, arming A-B).
            Dim slot As Integer = CustomHotkeys.RecipientSlotOf(actionId)
            If slot = 0 Then slot = CustomHotkeys.CopySlotOf(actionId)
            If Not CustomHotkeys.IsPlaybackAction(actionId) AndAlso
               (slot = 0 OrElse IsRecipientSlotConfigured(slot)) Then
                SlideShowStop()
                is_Slide_Show_Random_Mode = False
            End If
            ' The one action id both dispatch paths answer through - a rebound key and
            ' its factory key cannot drift (the invariant of the Select Case below).
            If CustomHotkeys.IsPlaybackAction(actionId) Then
                If PlaybackActionIgnoresRepeat(actionId) AndAlso IsAutoRepeatOf(e.KeyCode) Then Return True
                If Not PlaybackActionApplicable(actionId) Then Return CustomHotkeys.IsVideoBasicsAction(actionId)
                RunPlaybackAction(actionId)
                Return True
            End If
            RunHotkeyAction(actionId, wasSlideShow)
            Return True
        End If

        Return CustomHotkeys.IsRetiredDefault(custom_Hotkeys, combo)
    End Function

    ''' <summary>
    ''' What Enter has always done, as one action (UX-112 of 123): a document the viewer only shows as a
    ''' placeholder, a text file or a PDF opens in its own program; a locked container asks for its password.
    ''' Returns False when neither is on screen - the factory key then lets the press go on, as it always
    ''' did, and a key the user moved the action to simply does nothing.
    ''' </summary>
    Private Function RunOpenContentAction() As Boolean
        If IsContentPlaceholderShown() OrElse IsTextDocumentShown() OrElse IsPdfDocumentShown() Then
            OpenContentInDefaultProgram()
            Return True
        End If
        If IsSecurePlaceholderLocked() Then
            SecureUnlockCurrent()
            Return True
        End If
        Return False
    End Function

    ''' <summary>The key the open action answers to right now, for the hints that name it (placeholder, text
    ''' footer): empty when the user took the shortcut away, in which case a hint must not name a key.</summary>
    Friend Function OpenContentKeyLabel() As String
        Return ChromeMenuRules.HintFor(custom_Hotkeys, "openContent", "Enter")
    End Function

    ''' <summary>Every branch here calls the SAME method the built-in key calls - the
    ''' multi-line ones through the helpers extracted in Main_Form.KeyboardInput.vb, so a
    ''' rebound shortcut and its factory key can never drift apart.</summary>
    Private Sub RunHotkeyAction(actionId As String, wasSlideShow As Boolean)
        Dim slot As Integer = CustomHotkeys.RecipientSlotOf(actionId)
        If slot > 0 Then
            If IsRecipientSlotConfigured(slot) Then PoMove(slot)
            Return
        End If
        ' Shift + digit, rebindable (UX-112): the same implementation the Shift+digit key calls.
        Dim copySlot As Integer = CustomHotkeys.CopySlotOf(actionId)
        If copySlot > 0 Then
            If IsRecipientSlotConfigured(copySlot) Then ExecuteRecipientAction(copySlot, RecipientActionKind.Copy)
            Return
        End If

        Select Case actionId
            Case "nextFile" : ReadShowMediaFile(Mode_Next)
            Case "prevFile" : ReadShowMediaFile(Mode_Prev)
            Case "randomFile" : ReadShowMediaFile(Mode_ForRandom)
            Case "slideshow" : SetSlideShow(wasSlideShow)
            Case "randomSlideshow" : SetRandomSlideShow(wasSlideShow)
            Case "firstFile" : JumpTo(0, "первый файл")
            Case "lastFile" : JumpTo(total_File_Count - 1, "последний файл")
            Case "back10" : JumpBy(-10, "-10 файлов")
            Case "forward10" : JumpBy(10, "+10 файлов")
            Case "back100" : JumpBy(-100, "-100 файлов")
            Case "forward100" : JumpBy(100, "+100 файлов")
            Case "chooseFile" : Choose_file()
            Case "jumpToNumber" : Jump_To_file_Number()
            Case "renameFile" : RenameCurrentFileFromKeyboard()
            Case "deleteFile" : ReadShowMediaFile(Mode_Delete)
            Case "deletePermanent" : DeleteCurrentFilePermanently()
            Case "undo" : Undo()
            Case "rotateCw" : RotateActiveImage(True)
            Case "rotateCcw" : RotateActiveImage(False)
            Case "ocrTranslate" : RunOcrHotkeyOrRotate()
            Case "ocrAuto"
                If ocr_Settings IsNot Nothing Then ToggleOcrAutoMode()
            Case "fullScreen" : ToggleFullScreenMode()
            Case "settings" : ShowSettingsWindow()
            Case "imagePanel" : ShowImagePanelForm()
            Case "help" : ShowFirstRunHelp()
                ' The same implementation the NumPad / main-row zoom keys end in (UX-113 of 123): a text
                ' document zooms its text, a picture its pixels.
            Case "zoomIn", "zoomOut", "zoomFit", "zoomActual" : RunZoomAction(actionId)
            Case "openContent" : RunOpenContentAction()
                ' Same calls the A / V keys make (TryHandleTrackKey): nothing happens, and nothing is
                ' said, when the file offers no choice.
            Case "mediaMenu" : ShowMediaMenuFromKeyboard()
            Case "audioTrack" : CycleTrack(audio:=True)
            Case "subtitleTrack" : CycleTrack(audio:=False)
                ' The playback extras (119 §5.0.2) and the video basics (123 Ф-3): an owned override
                ' always consumes the key, and when the action is not applicable RunPlaybackAction
                ' only says so in the status line and changes no state.
            Case "speedSlower", "speedFaster", "speedReset",
                 "seekBackShort", "seekFwdShort", "seekBackMedium", "seekFwdMedium", "seekBackLong", "seekFwdLong",
                 "frameStep", "abLoop",
                 "playPause", "mute", "volumeUp", "volumeDown"
                RunPlaybackAction(actionId)
        End Select
    End Sub

End Class
#End If
