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
    End Sub

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
            RunHotkeyAction(actionId, wasSlideShow)
            Return True
        End If

        Return CustomHotkeys.IsRetiredDefault(custom_Hotkeys, combo)
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
            Case "deletePermanent"
                pending_Delete_Permanent = True
                ReadShowMediaFile(Mode_Delete)
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
            Case "zoomIn" : ZoomStepAt(True, False, CursorAnchorOnPanel())
            Case "zoomOut" : ZoomStepAt(False, False, CursorAnchorOnPanel())
            Case "zoomFit" : ZoomToFit()
            Case "zoomActual" : ZoomToActualSize(CursorAnchorOnPanel())
        End Select
    End Sub

End Class
#End If
