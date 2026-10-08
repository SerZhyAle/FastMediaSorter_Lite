#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' APP-SETTINGS 0.3 section 5 rung 3 as section 9 item 5 words it: the static sweep looks for an IRREVERSIBLE HANDLER
''' ATTACHED TO A VALUE CONTROL - not for the dedicated, confirmed button of an operation. The settings window offers
''' no Commit, so a checkbox, a number, a drop-down or a text box takes effect when it is touched; nothing that
''' cannot be changed back may hang from one.
'''
''' What the sweep reads (source only, no form is built): every handler attached to a value event in the settings
''' window's sources - `AddHandler x.CheckedChanged, ..` with a lambda or an `AddressOf`, and a `Handles
''' x.CheckedChanged` method - and what it can reach: its own body and the bodies of the methods it calls by name, two
''' calls deep. Lambdas inside a called method are not followed (they are handed to something else - a Click handler, a
''' refresh list - and the called method does not run them), and neither is an `AddressOf` that is only a reference.
'''
''' "Irreversible" is a list a scan can hold, and it is deliberately short: a call that deletes a file or a folder,
''' deletes a registry key or value, or empties a cache directory on disk (<see cref="SettingsHandlerSweep.IrreversibleCalls"/>).
''' An in-memory cache that is rebuilt on demand is derived data and is not on it. A scan that cannot be sure says so
''' instead of passing: a handler it cannot find and a lambda whose end it cannot place are failures of the sweep.
''' </summary>
Public Class SettingsIrreversibleHandlerTests

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src/ not found above " & AppContext.BaseDirectory)
    End Function

    ''' <summary>The settings window's own sources: its partials (not the generated Designer file) and the panel it embeds.</summary>
    Private Shared Function SettingsSources() As Dictionary(Of String, String)
        Dim files As New Dictionary(Of String, String)()
        For Each path As String In Directory.GetFiles(SourceDir(), "Table_Form*.vb")
            Dim name As String = System.IO.Path.GetFileName(path)
            If name.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase) Then Continue For
            files(name) = File.ReadAllText(path)
        Next
        files("Resource_List_Panel.vb") = File.ReadAllText(System.IO.Path.Combine(SourceDir(), "Resource_List_Panel.vb"))
        Return files
    End Function

    ' --------------------------------------------------------------- the real window ----

    <Fact>
    Public Sub No_value_control_of_the_settings_window_runs_an_irreversible_operation()
        Dim result As SettingsHandlerSweep.SweepResult = SettingsHandlerSweep.Sweep(SettingsSources())

        Assert.True(result.Unresolved.Count = 0, "the sweep could not place: " & vbLf & String.Join(vbLf, result.Unresolved))
        ' The sweep must have SEEN the handlers: a scan that finds nothing passes by being blind. 64 value handlers
        ' on 2026-10-07; a floor well under that, so a removed row does not trip it and a broken scan does.
        Assert.True(result.AttachPoints >= 50, "the sweep found only " & result.AttachPoints & " value handlers")
        Assert.True(result.Violations.Count = 0, "an irreversible operation hangs from a value control: " & vbLf & String.Join(vbLf, result.Violations))
    End Sub

    ' ----------------------------------------------------------- the sweep itself ----

    Private Shared Function Sweep(ParamArray lines As String()) As SettingsHandlerSweep.SweepResult
        Return SettingsHandlerSweep.Sweep(New Dictionary(Of String, String) From {{"Fixture.vb", String.Join(vbLf, lines)}})
    End Function

    <Fact>
    Public Sub A_multi_line_lambda_on_a_value_event_that_deletes_a_file_is_found()
        Dim r = Sweep(
            "Class F",
            "    Sub Build()",
            "        AddHandler check.CheckedChanged,",
            "            Sub()",
            "                If isUpdating Then Return",
            "                File.Delete(path)",
            "            End Sub",
            "    End Sub",
            "End Class")
        Assert.Empty(r.Unresolved)
        Assert.Equal(1, r.AttachPoints)
        Assert.Contains("File.Delete", Assert.Single(r.Violations))
    End Sub

    <Fact>
    Public Sub A_lambda_that_starts_on_the_attach_line_is_read_to_its_own_end()
        ' Visual Studio aligns the End Sub with the Sub keyword, not with the AddHandler.
        Dim first As String = "        AddHandler box.TextChanged, "
        Dim second As String = "        AddHandler other.ValueChanged, "
        Dim r = Sweep(
            "Class F",
            "    Sub Build()",
            first & "Sub()",
            New String(" "c, first.Length + 4) & "If Not loading Then pending = True",
            New String(" "c, first.Length) & "End Sub",
            second & "Sub()",
            New String(" "c, second.Length + 4) & "CacheMaintenance.ClearFiles(dir, ""*.bin"")",
            New String(" "c, second.Length) & "End Sub",
            "    End Sub",
            "End Class")
        Assert.Empty(r.Unresolved)
        Assert.Equal(2, r.AttachPoints)
        Assert.Contains("CacheMaintenance.ClearFiles", Assert.Single(r.Violations))   ' only the second one
    End Sub
    <Fact>
    Public Sub A_single_line_lambda_and_an_AddressOf_handler_are_both_read()
        Dim r = Sweep(
            "Class F",
            "    Sub Build()",
            "        AddHandler a.CheckedChanged, Sub() Registry.CurrentUser.DeleteSubKeyTree(""x"")",
            "        AddHandler b.SelectedIndexChanged, AddressOf OnPick",
            "        AddHandler c.ValueChanged, AddressOf OnQuiet",
            "    End Sub",
            "    Private Sub OnPick(s As Object, e As EventArgs)",
            "        Directory.Delete(folder, True)",
            "    End Sub",
            "    Private Sub OnQuiet(s As Object, e As EventArgs)",
            "        count = 1",
            "    End Sub",
            "End Class")
        Assert.Empty(r.Unresolved)
        Assert.Equal(3, r.AttachPoints)
        Assert.Equal(2, r.Violations.Count)
        Assert.Contains(r.Violations, Function(v) v.Contains("DeleteSubKeyTree"))
        Assert.Contains(r.Violations, Function(v) v.Contains("OnPick") AndAlso v.Contains("Directory.Delete"))
    End Sub

    <Fact>
    Public Sub A_Handles_method_is_a_value_handler_too()
        Dim r = Sweep(
            "Class F",
            "    Private Sub Chk_Reset_CheckedChanged(sender As Object, e As EventArgs) Handles chk_Reset.CheckedChanged",
            "        ResetEverything()",
            "    End Sub",
            "    Private Sub ResetEverything()",
            "        Registry.CurrentUser.DeleteValue(""Setting"")",
            "    End Sub",
            "    Private Sub Btn_Reset_Click(sender As Object, e As EventArgs) Handles btn_Reset.Click",
            "        ResetEverything()",
            "    End Sub",
            "End Class")
        Assert.Empty(r.Unresolved)
        Assert.Equal(1, r.AttachPoints)                 ' the Click one is the dedicated button, not a value control
        Assert.Contains("ResetEverything -> ", Assert.Single(r.Violations))
    End Sub

    <Fact>
    Public Sub A_handler_held_in_a_local_variable_is_followed_to_its_lambda()
        Dim r = Sweep(
            "Class F",
            "    Sub Hook()",
            "        Dim wipe As EventHandler = Sub(sender As Object, e As EventArgs) Directory.Delete(folder, True)",
            "        Dim quiet As EventHandler = Sub(sender As Object, e As EventArgs) combo.Invalidate()",
            "        AddHandler a.ValueChanged, wipe",
            "        AddHandler b.TextChanged, quiet",
            "        AddHandler c.TextChanged, stranger",
            "    End Sub",
            "End Class")
        Assert.Equal(3, r.AttachPoints)
        Assert.Contains("Directory.Delete", Assert.Single(r.Violations))
        Assert.Contains("stranger", Assert.Single(r.Unresolved))                 ' declared nowhere in the method: reported, not assumed safe
    End Sub

    <Fact>
    Public Sub The_dedicated_button_of_an_operation_is_not_a_violation_and_is_not_followed_through_a_builder()
        Dim r = Sweep(
            "Class F",
            "    Sub AddCacheRow()",
            "        AddHandler clearButton.Click,",
            "            Sub()",
            "                CacheMaintenance.ClearFiles(dir, ""*.bin"")",
            "            End Sub",
            "    End Sub",
            "    Sub Rebuild()",
            "        AddCacheRow()",
            "        AddRow(""Clear"", Sub() CacheMaintenance.ClearSessions(root))",
            "        AddRow(""Wipe"",",
            "               Sub()",
            "                   File.Delete(path)",
            "               End Sub)",
            "    End Sub",
            "    Sub Build()",
            "        AddHandler language.SelectedIndexChanged,",
            "            Sub()",
            "                Rebuild()",
            "            End Sub",
            "    End Sub",
            "End Class")
        Assert.Empty(r.Unresolved)
        Assert.Equal(1, r.AttachPoints)
        Assert.Empty(r.Violations)                      ' Rebuild only WIRES the buttons; it does not press them
    End Sub

    <Fact>
    Public Sub A_comment_and_an_AddressOf_reference_alone_reach_nothing()
        Dim r = Sweep(
            "Class F",
            "    Sub Build()",
            "        AddHandler a.CheckedChanged, Sub() count += 1 ' File.Delete(path) would be wrong here",
            "        AddHandler b.CheckedChanged,",
            "            Sub()",
            "                ' CacheMaintenance.ClearFiles(dir, ""*"")",
            "                Wire(AddressOf Wipe)",
            "            End Sub",
            "    End Sub",
            "    Sub Wire(handler As Action)",
            "    End Sub",
            "    Sub Wipe()",
            "        File.Delete(path)",
            "    End Sub",
            "End Class")
        Assert.Empty(r.Unresolved)
        Assert.Equal(2, r.AttachPoints)
        Assert.Empty(r.Violations)
    End Sub

    <Fact>
    Public Sub What_the_sweep_cannot_place_it_reports_instead_of_passing()
        ' A handler method that is in none of the scanned files, and a lambda that is never closed: both are failures of the sweep, never a clean result.
        Dim missing = Sweep(
            "Class F",
            "    Sub Build()",
            "        AddHandler a.CheckedChanged, AddressOf Defined.Elsewhere",
            "    End Sub",
            "End Class")
        Assert.Single(missing.Unresolved)

        Dim unclosed = Sweep(
            "Class F",
            "    Sub Build()",
            "        AddHandler a.CheckedChanged,",
            "            Sub()",
            "                File.Delete(path)",
            "End Class")
        Assert.NotEmpty(unclosed.Unresolved)
    End Sub

End Class

''' <summary>The pure half of <see cref="SettingsIrreversibleHandlerTests"/>: text in, findings out.</summary>
Friend NotInheritable Class SettingsHandlerSweep

    Friend NotInheritable Class SweepResult
        Public ReadOnly Violations As New List(Of String)()
        Public ReadOnly Unresolved As New List(Of String)()
        Public AttachPoints As Integer
    End Class

    Private Const ValueEvents As String =
        "CheckedChanged|CheckStateChanged|ValueChanged|SelectedIndexChanged|SelectedValueChanged|SelectedItemChanged|SelectionChangeCommitted|TextChanged|Scroll|Leave|Validated|CellValueChanged|CellEndEdit"

    ''' <summary>
    ''' The calls the sweep calls irreversible: a file or folder deleted, a registry key or value deleted, a cache
    ''' directory emptied on disk. Deleting to the Recycle Bin is reversible and is not here; an in-memory cache is
    ''' derived data and is not here either.
    ''' </summary>
    Friend Shared ReadOnly IrreversibleCalls As New Regex(
        "\bFile\.Delete\b|\bDirectory\.Delete\b|\bFileSystem\.Delete(?:File|Directory)\b|\.DeleteSubKey(?:Tree)?\b|\.DeleteValue\b|\bCacheMaintenance\.Clear\w*|\bClearOcrResultCacheOnDisk\b",
        RegexOptions.Compiled)

    Private Const CallDepth As Integer = 2

    Private Shared ReadOnly MethodStart As New Regex(
        "^(?<ind>[ \t]*)(?:(?:Private|Public|Friend|Protected|Shared|Overrides|Overloads|Overridable|Async|Iterator)\s+)*(?:Sub|Function)\s+(?<name>\w+)",
        RegexOptions.Compiled)
    Private Shared ReadOnly AddValueHandler As New Regex(
        "^\s*AddHandler\s+[\w\.\(\)]+?\.(?:" & ValueEvents & ")\s*,\s*(?<rest>.*)$", RegexOptions.Compiled)
    Private Shared ReadOnly HandlesValue As New Regex(
        "^\s*(?:(?:Private|Public|Friend|Protected)\s+)*Sub\s+(?<name>\w+)\s*\(.*\)\s*Handles\s+.*\.(?:" & ValueEvents & ")\b", RegexOptions.Compiled)
    Private Shared ReadOnly LambdaStart As New Regex("\b(?:Sub|Function)\s*\(", RegexOptions.Compiled)
    Private Shared ReadOnly EndOfBlock As New Regex("^\s*End (?:Sub|Function)\b", RegexOptions.Compiled)
    Private Shared ReadOnly CallSite As New Regex("\b(?<name>[A-Za-z_]\w*)\s*\(", RegexOptions.Compiled)
    Private Shared ReadOnly AddressOfTarget As New Regex("\bAddressOf\s+(?<name>[\w\.]+)", RegexOptions.Compiled)

    ' ----------------------------------------------------------------- reading ----

    Friend Shared Function StripComment(line As String) As String
        Dim inString As Boolean = False
        For i As Integer = 0 To line.Length - 1
            Dim c As Char = line(i)
            If c = """"c Then
                inString = Not inString
            ElseIf c = "'"c AndAlso Not inString Then
                Return line.Substring(0, i)
            End If
        Next
        Return line
    End Function

    Private Shared Function LinesOf(text As String) As List(Of String)
        Return text.Replace(vbCr, "").Split(ControlChars.Lf).Select(AddressOf StripComment).ToList()
    End Function

    ''' <summary>One lambda: where it starts, what its body says, and the last row it covers.</summary>
    Private Structure LambdaRead
        Public Resolved As Boolean
        Public MultiLine As Boolean
        Public Body As String
        Public EndRow As Integer
    End Structure

    ''' <summary>The index of the `)` that closes the parameter list whose `(` is the last character of <paramref name="keyword"/>, or -1.</summary>
    Private Shared Function ParameterListEnd(text As String, keyword As Match) As Integer
        Dim depth As Integer = 0
        For i As Integer = keyword.Index + keyword.Length - 1 To text.Length - 1
            If text(i) = "("c Then
                depth += 1
            ElseIf text(i) = ")"c Then
                depth -= 1
                If depth = 0 Then Return i
            End If
        Next
        Return -1
    End Function

    ''' <summary>How many lambdas on <paramref name="line"/> open a block - a `Sub(..)` or `Function(..)` with nothing after its parameter list.</summary>
    Private Shared Function LambdaOpens(line As String) As Integer
        Dim opens As Integer = 0
        For Each keyword As Match In LambdaStart.Matches(line)
            Dim close As Integer = ParameterListEnd(line, keyword)
            If close < 0 Then Continue For
            Dim tail As String = line.Substring(close + 1).Trim()
            If tail.Length = 0 OrElse tail = "_" Then opens += 1
        Next
        Return opens
    End Function

    ''' <summary>
    ''' The row of the `End Sub` / `End Function` that closes the block opened on <paramref name="openRow"/>, found by
    ''' counting: every nested multi-line lambda opens one more and every `End Sub` / `End Function` closes one. The
    ''' count, not the column, because hand edits leave an `End Sub` a column off its keyword (it happens in these
    ''' sources). -1 when the block never closes.
    ''' </summary>
    Private Shared Function BlockEnd(lines As List(Of String), openRow As Integer, opensOnOpenRow As Integer) As Integer
        Dim depth As Integer = opensOnOpenRow
        For r As Integer = openRow + 1 To lines.Count - 1
            If EndOfBlock.IsMatch(lines(r)) Then
                depth -= 1
                If depth = 0 Then Return r
            Else
                depth += LambdaOpens(lines(r))
            End If
        Next
        Return -1
    End Function

    ''' <summary>
    ''' Reads the lambda whose `Sub`/`Function` keyword (or its `Async` prefix) starts at <paramref name="col"/> of
    ''' <paramref name="row"/>. A single-line lambda is the text after its parameter list; a multi-line one runs to the
    ''' `End Sub` / `End Function` that closes it (<see cref="BlockEnd"/>) - and when none does, it is unresolved.
    ''' </summary>
    Private Shared Function ReadLambda(lines As List(Of String), row As Integer, col As Integer) As LambdaRead
        Dim text As String = lines(row)
        Dim keyword As Match = LambdaStart.Match(text, col)
        Dim result As New LambdaRead With {.Body = String.Empty, .EndRow = row}
        If Not keyword.Success Then Return result

        Dim close As Integer = ParameterListEnd(text, keyword)
        If close < 0 Then Return result                                   ' a parameter list that wraps: not placed

        Dim tail As String = text.Substring(close + 1).Trim()
        If tail.Length > 0 AndAlso tail <> "_" Then
            result.Resolved = True
            result.Body = tail
            Return result
        End If

        result.MultiLine = True
        Dim endRow As Integer = BlockEnd(lines, row, 1)
        If endRow < 0 Then Return result
        result.Resolved = True
        result.EndRow = endRow
        result.Body = String.Join(vbLf, lines.GetRange(row + 1, endRow - row - 1))
        Return result
    End Function
    ''' <summary>The column a lambda starts at in <paramref name="text"/> - at its `Async` prefix when it has one.</summary>
    Private Shared Function LambdaColumn(text As String, keywordIndex As Integer) As Integer
        If keywordIndex >= 6 AndAlso String.CompareOrdinal(text, keywordIndex - 6, "Async ", 0, 6) = 0 Then Return keywordIndex - 6
        Return keywordIndex
    End Function

    ''' <summary>A method's text without the lambdas inside it: what the method does when it is CALLED, as opposed to what it hands out.</summary>
    Private Shared Function WithoutLambdas(lines As List(Of String), first As Integer, last As Integer, owner As String, result As SweepResult) As String
        Dim text As New StringBuilder()
        Dim row As Integer = first
        While row <= last
            Dim line As String = lines(row)
            Dim keyword As Match = LambdaStart.Match(line)
            If Not keyword.Success Then
                text.AppendLine(line)
                row += 1
                Continue While
            End If
            Dim col As Integer = LambdaColumn(line, keyword.Index)
            Dim lambda As LambdaRead = ReadLambda(lines, row, col)
            If Not lambda.Resolved Then
                result.Unresolved.Add(owner & ": a lambda at row " & (row + 1) & " has no end at its column")
                text.AppendLine(line)
                row += 1
                Continue While
            End If
            text.AppendLine(line.Substring(0, col))
            row = lambda.EndRow + 1
        End While
        Return text.ToString()
    End Function

    ' -------------------------------------------------------------------- the sweep ----

    Friend Shared Function Sweep(files As IDictionary(Of String, String)) As SweepResult
        Dim result As New SweepResult()
        Dim parsed As New Dictionary(Of String, List(Of String))()
        For Each pair In files
            parsed(pair.Key) = LinesOf(pair.Value)
        Next

        ' Every method of the scanned files, by name (overloads and same-named methods are read as one: the sweep
        ' would rather follow a call too far than miss one).
        Dim methods As New Dictionary(Of String, String)(StringComparer.Ordinal)
        For Each pair In parsed
            Dim lines As List(Of String) = pair.Value
            For row As Integer = 0 To lines.Count - 1
                Dim m As Match = MethodStart.Match(lines(row))
                If Not m.Success Then Continue For
                Dim endRow As Integer = BlockEnd(lines, row, 1)
                If endRow < 0 Then Continue For                            ' a declaration with no body
                Dim name As String = m.Groups("name").Value
                Dim body As String = WithoutLambdas(lines, row + 1, endRow - 1, pair.Key & ":" & name, result)
                methods(name) = If(methods.ContainsKey(name), methods(name) & vbLf & body, body)
            Next
        Next

        For Each pair In parsed
            Dim lines As List(Of String) = pair.Value
            For row As Integer = 0 To lines.Count - 1
                Dim origin As String = pair.Key & ":" & (row + 1)
                Dim attach As Match = AddValueHandler.Match(lines(row))
                Dim handlesClause As Match = HandlesValue.Match(lines(row))
                Dim body As String = Nothing

                If attach.Success Then
                    result.AttachPoints += 1
                    body = HandlerBody(lines, row, attach.Groups("rest").Value, methods, origin, result)
                ElseIf handlesClause.Success Then
                    result.AttachPoints += 1
                    body = ReferencedBody(handlesClause.Groups("name").Value, methods, origin, result)
                Else
                    Continue For
                End If
                If body Is Nothing Then Continue For

                Dim path As String = Reaches(body, methods, New HashSet(Of String)(), CallDepth)
                If path IsNot Nothing Then result.Violations.Add(origin & "  " & lines(row).Trim() & "  =>  " & path)
            Next
        Next
        Return result
    End Function

    Private Shared Function ReferencedBody(name As String, methods As Dictionary(Of String, String), origin As String, result As SweepResult) As String
        Dim last As String = name.Substring(name.LastIndexOf("."c) + 1)
        Dim body As String = Nothing
        If methods.TryGetValue(last, body) Then Return body
        result.Unresolved.Add(origin & ": the handler " & name & " is in none of the scanned sources")
        Return Nothing
    End Function

    ''' <summary>The text a value handler runs: the method an AddressOf names, or the lambda attached on this row.</summary>
    Private Shared Function HandlerBody(lines As List(Of String), row As Integer, rest As String,
                                        methods As Dictionary(Of String, String), origin As String, result As SweepResult) As String
        Dim reference As Match = AddressOfTarget.Match(rest)
        If reference.Success AndAlso rest.TrimStart().StartsWith("AddressOf", StringComparison.Ordinal) Then
            Return ReferencedBody(reference.Groups("name").Value, methods, origin, result)
        End If

        ' A handler that is a plain name is a local variable: `Dim repaint As EventHandler = Sub(..) ..` above it.
        If Regex.IsMatch(rest.Trim(), "^\w+$") Then
            Dim declared As Regex = New Regex("\b(?:Dim\s+" & Regex.Escape(rest.Trim()) & "\b[^=]*=|" & Regex.Escape(rest.Trim()) & "\s*=)\s*(?<value>.*)$")
            For r As Integer = row - 1 To 0 Step -1
                If MethodStart.IsMatch(lines(r)) Then Exit For
                Dim d As Match = declared.Match(lines(r))
                If Not d.Success Then Continue For
                Dim value As String = d.Groups("value").Value
                If value.TrimStart().StartsWith("AddressOf", StringComparison.Ordinal) Then
                    Return ReferencedBody(AddressOfTarget.Match(value).Groups("name").Value, methods, origin, result)
                End If
                Dim lambdaAt As Match = LambdaStart.Match(lines(r))
                If Not lambdaAt.Success Then Exit For
                Dim declaredLambda As LambdaRead = ReadLambda(lines, r, LambdaColumn(lines(r), lambdaAt.Index))
                If declaredLambda.Resolved Then Return declaredLambda.Body
                Exit For
            Next
            result.Unresolved.Add(origin & ": the handler " & rest.Trim() & " is not a lambda or AddressOf declared in the same method")
            Return Nothing
        End If

        ' The lambda starts on this row, or - when the row ends with the comma or a line continuation - on the next.
        Dim lambdaRow As Integer = row
        Dim text As String = lines(row)
        If rest.Trim().Length = 0 OrElse rest.Trim() = "_" Then
            lambdaRow = row + 1
            While lambdaRow < lines.Count AndAlso lines(lambdaRow).Trim().Length = 0
                lambdaRow += 1
            End While
            If lambdaRow >= lines.Count Then
                result.Unresolved.Add(origin & ": the handler has nothing after the comma")
                Return Nothing
            End If
            text = lines(lambdaRow)
        End If

        Dim keyword As Match = LambdaStart.Match(text)
        If Not keyword.Success Then
            result.Unresolved.Add(origin & ": the handler is neither an AddressOf nor a lambda")
            Return Nothing
        End If
        Dim lambda As LambdaRead = ReadLambda(lines, lambdaRow, LambdaColumn(text, keyword.Index))
        If Not lambda.Resolved Then
            result.Unresolved.Add(origin & ": the lambda has no End Sub at its own column")
            Return Nothing
        End If
        Return lambda.Body
    End Function

    ''' <summary>The first irreversible call <paramref name="text"/> reaches, as a "Method -> Method -> call" path, or Nothing.</summary>
    Private Shared Function Reaches(text As String, methods As Dictionary(Of String, String), seen As HashSet(Of String), depth As Integer) As String
        Dim direct As Match = IrreversibleCalls.Match(text)
        If direct.Success Then Return direct.Value
        If depth <= 0 Then Return Nothing
        For Each site As Match In CallSite.Matches(text)
            Dim name As String = site.Groups("name").Value
            Dim body As String = Nothing
            If methods.TryGetValue(name, body) AndAlso seen.Add(name) Then
                Dim inner As String = Reaches(body, methods, seen, depth - 1)
                If inner IsNot Nothing Then Return name & " -> " & inner
            End If
        Next
        Return Nothing
    End Function

End Class
#End If
