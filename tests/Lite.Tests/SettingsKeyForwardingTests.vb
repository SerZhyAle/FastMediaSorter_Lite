Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' The settings window forwards keys to the viewer so photos can be flipped while it
''' stays open. Before spec 028 phase 1 it forwarded EVERYTHING it did not recognise, so
''' Escape on a focused checkbox closed the whole application and D/Delete deleted the
''' file on screen. A source read (neither test project builds a form): the forwarding
''' is an allow-list, and nothing destructive is on it.
''' </summary>
Public Class SettingsKeyForwardingTests

    <Fact>
    Public Sub The_forwarding_allow_list_holds_no_destructive_key()
        Dim body As String = MethodBody(TableFormSource(), "Function IsForwardedToViewer")

        For Each forbidden As String In {"Keys.Escape", "Keys.Delete", "Keys.D,", "Keys.D)", "Keys.X", "Keys.Q",
                                         "Keys.D0", "Keys.D1", "Keys.NumPad", "Keys.Shift", "Keys.Control"}
            Assert.DoesNotContain(forbidden, body)
        Next
        Assert.Contains("Keys.Left", body)
        Assert.Contains("Keys.Right", body)
    End Sub

    <Fact>
    Public Sub The_key_handler_closes_on_Escape_and_forwards_only_through_the_allow_list()
        Dim body As String = MethodBody(TableFormSource(), "Sub Form2_KeyDown")

        Dim escapeAt As Integer = body.IndexOf("Keys.Escape", StringComparison.Ordinal)
        Dim closeAt As Integer = body.IndexOf("Me.Close()", StringComparison.Ordinal)
        Dim gateAt As Integer = body.IndexOf("If Not IsForwardedToViewer(e.KeyData) Then Return", StringComparison.Ordinal)
        Dim forwardAt As Integer = body.IndexOf("Main_Form.KeybUse", StringComparison.Ordinal)

        Assert.True(escapeAt >= 0 AndAlso closeAt > escapeAt, "Escape must close the settings window itself")
        Assert.True(gateAt >= 0, "the forward must be gated by IsForwardedToViewer")
        Assert.True(forwardAt > gateAt, "KeybUse must come after the allow-list gate")
        Assert.Equal(1, Regex.Matches(body, "KeybUse").Count)
    End Sub

    Private Shared Function MethodBody(source As String, signature As String) As String
        Dim start As Integer = source.IndexOf(signature, StringComparison.Ordinal)
        Assert.True(start >= 0, signature & " not found in Table_Form.vb")
        Dim endToken As String = If(signature.StartsWith("Function", StringComparison.Ordinal), "End Function", "End Sub")
        Dim finish As Integer = source.IndexOf(endToken, start, StringComparison.Ordinal)
        Assert.True(finish > start)
        ' Comments mention the keys on purpose; only the code counts.
        Dim lines = source.Substring(start, finish - start).Split({vbCrLf, vbLf}, StringSplitOptions.None).
            Where(Function(l) Not l.TrimStart().StartsWith("'", StringComparison.Ordinal))
        Return String.Join(vbLf, lines)
    End Function

    Private Shared Function TableFormSource() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src", "Table_Form.vb")
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src/Table_Form.vb not found above " & AppContext.BaseDirectory)
    End Function

End Class
