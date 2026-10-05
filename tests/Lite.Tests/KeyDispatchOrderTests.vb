Option Strict On

Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' The ORDER of the gates at the top of Main_Form.KeybUse (audit 047). A source read, like
''' SettingsKeyForwardingTests - neither test project builds a form - because each defect
''' was a gate in the wrong place, not a wrong gate:
'''   C-20 - the auto-repeat check comes first, so a held digit or DEL acts once;
'''   C-19 - the custom map is asked BEFORE the unconfigured-slot return, or "5" bound to
'''          an action did nothing while slot 5 had no folder;
'''   C-10 - Ctrl/Alt are turned away BEFORE the historical map, which never looked at
'''          them (Alt+F4 opened the file chooser, Ctrl+1 moved a file).
''' </summary>
Public Class KeyDispatchOrderTests

    <Fact>
    Public Sub The_gates_run_in_the_order_the_defects_need()
        Dim body As String = MethodBody(KeyboardSource(), "Public Sub KeybUse")

        Dim repeatAt As Integer = body.IndexOf("If destructive_Press AndAlso IsAutoRepeatOf(e.KeyCode)", StringComparison.Ordinal)
        Dim customAt As Integer = body.IndexOf("TryHandleCustomHotkey(", StringComparison.Ordinal)
        Dim modifierAt As Integer = body.IndexOf("If Not KeyRouting.MainMapAccepts(e.KeyData) Then Exit Sub", StringComparison.Ordinal)
        Dim slotAt As Integer = body.IndexOf("Not IsRecipientSlotConfigured(recipient_Slot) Then Return", StringComparison.Ordinal)
        Dim mapAt As Integer = body.IndexOf("If e.Shift Then", StringComparison.Ordinal)

        Assert.True(repeatAt >= 0, "the auto-repeat gate is missing")
        Assert.True(customAt > repeatAt, "the custom map must come after the auto-repeat gate")
        Assert.True(modifierAt > customAt, "Ctrl/Alt must be turned away after the custom map (which may bind them)")
        Assert.True(slotAt > modifierAt, "the unconfigured-slot return must come after the custom map and the modifier gate")
        Assert.True(mapAt > slotAt, "the historical map must come last")
    End Sub

    ''' <summary>C-15: Shift+DEL goes through the helper that clears the permanent flag in a
    ''' Finally - never by setting the field and hoping the delete branch reads it.</summary>
    <Fact>
    Public Sub The_permanent_delete_flag_is_never_left_standing()
        Dim source As String = KeyboardSource()
        Assert.Contains("DeleteCurrentFilePermanently()", source)
        Assert.DoesNotContain("pending_Delete_Permanent = True", StripComments(source))
    End Sub

    ''' <summary>119 §5.0.2: the playback extras are asked between the custom map and the
    ''' Ctrl/Alt gate, because three of the seek chords ARE Ctrl/Alt chords the historical
    ''' map rejects. Too early would let them steal a rebound action; too late and
    ''' MainMapAccepts throws the chord away before anyone can use it.</summary>
    <Fact>
    Public Sub The_playback_dispatch_sits_between_the_custom_map_and_the_modifier_gate()
        Dim body As String = MethodBody(KeyboardSource(), "Public Sub KeybUse")

        Dim customAt As Integer = body.IndexOf("TryHandleCustomHotkey(", StringComparison.Ordinal)
        Dim playbackAt As Integer = body.IndexOf("TryHandlePlaybackKey(", StringComparison.Ordinal)
        Dim modifierAt As Integer = body.IndexOf("If Not KeyRouting.MainMapAccepts(e.KeyData) Then Exit Sub", StringComparison.Ordinal)

        Assert.True(playbackAt > customAt, "the playback dispatch must come after the custom map (a moved action is already handled there)")
        Assert.True(modifierAt > playbackAt, "the playback dispatch must come before the Ctrl/Alt gate (three seek chords are Ctrl/Alt chords)")
    End Sub

    ''' <summary>119 §5.0.2: a playback override must not stop the slideshow - speed, seek
    ''' and the A-B marks work INSIDE the clip a slideshow is waiting on. Only the
    ''' actions that mean "I am inspecting this clip" stop it, and they do it themselves.</summary>
    <Fact>
    Public Sub A_playback_override_does_not_stop_the_slideshow()
        Dim source As String = StripComments(CustomHotkeysSource())
        Dim at As Integer = source.IndexOf("If Not CustomHotkeys.IsPlaybackAction(actionId) AndAlso", StringComparison.Ordinal)
        Assert.True(at >= 0, "the SlideShowStop carve-out for playback actions is missing")
        Dim stopAt As Integer = source.IndexOf("SlideShowStop()", at, StringComparison.Ordinal)
        Dim endifAt As Integer = source.IndexOf("End If", at, StringComparison.Ordinal)
        Assert.True(stopAt >= 0 AndAlso endifAt > stopAt, "SlideShowStop must sit inside the carve-out")
    End Sub

    Private Shared Function CustomHotkeysSource() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src", "Main_Form.CustomHotkeys.vb")
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src/Main_Form.CustomHotkeys.vb not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function MethodBody(source As String, signature As String) As String
        Dim start As Integer = source.IndexOf(signature, StringComparison.Ordinal)
        Assert.True(start >= 0, signature & " not found")
        Dim finish As Integer = source.IndexOf("End Sub", start, StringComparison.Ordinal)
        Assert.True(finish > start)
        Return StripComments(source.Substring(start, finish - start))
    End Function

    Private Shared Function StripComments(text As String) As String
        Dim lines = text.Split({vbCrLf, vbLf}, StringSplitOptions.None).
            Where(Function(l) Not l.TrimStart().StartsWith("'", StringComparison.Ordinal))
        Return String.Join(vbLf, lines)
    End Function

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
