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

        Dim repeatAt As Integer = body.IndexOf("KeyRouting.IsDestructiveKey(e.KeyCode) AndAlso IsAutoRepeatOf(e.KeyCode)", StringComparison.Ordinal)
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
