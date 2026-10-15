#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' The reading of a source file that finds a translated fragment glued to a value (ticket 124 UX-217):
''' <c>Localization.T("Модель установлена: ") &amp; modelName</c>, <c>size &amp; " · " &amp; T("изменено")</c>.
'''
''' WHY IT IS A DEFECT. A translated fragment that must be completed by a value decides, in the source, where the value
''' goes and which way the words read around it - for all thirteen languages at once. Word order differs (a
''' number before its noun, a verb at the end) and direction differs (an Arabic or Urdu sentence around a Latin file
''' name reorders itself under the bidi algorithm), so the sentence has to be ONE registered key with the value in a
''' placeholder - <c>TF("Модель установлена: {0}", modelName)</c> - which is what a translator can actually translate.
'''
''' WHAT IT FLAGS. A chain joined with the concatenation operator, between newline constants, in which a T / TC call
''' sits beside a value: anything that is not a string literal, another translated call, a newline or a character
''' constant. A <c>TF</c> call is never a finding. What is NOT a finding, on purpose:
'''   - paragraphs: <c>message &amp; vbCrLf &amp; vbCrLf &amp; T("..?")</c> - the chain is split at the newline constants;
'''   - a translated complete sentence appended after a value (<c>status &amp; " " &amp; T("... not opened.")</c>) - two
'''     sentences side by side, not a frame around a value;
'''   - an accumulation (<c>text &amp;= " " &amp; T("..")</c>): the left side of <c>&amp;=</c> is not an operand;
'''   - code that only the x86 fallback compiles (<c>#If NETFRAMEWORK</c>): frozen by the maintenance policy, and not a
'''     defect there anyway - the build ships two languages.
'''
''' It is a reading, not a parser: it is exact on the shapes the tree uses and held by the fixtures below in both
''' directions (what it must flag and what it must not), so a change to it is a change to a test first.
''' </summary>
Public Class TranslatedValueConcatenationTests

    ' =====================================================================================================
    ' the scan
    ' =====================================================================================================

    Private Enum OperandKind
        Empty
        Translated
        Formatted
        Literal
        Newline
        Character
        Value
    End Enum

    Private Shared ReadOnly TranslatedCall As New Regex("(?<![A-Za-z0-9_])(?:Localization\.)?TC?\(", RegexOptions.Compiled)
    Private Shared ReadOnly TranslatedStart As New Regex("^(?:Localization\.)?TC?\(", RegexOptions.Compiled)
    Private Shared ReadOnly FormattedStart As New Regex("^(?:Localization\.)?TF\(", RegexOptions.Compiled)
    Private Shared ReadOnly NewlineStart As New Regex("^(?:vbCrLf|vbLf|vbCr|vbNewLine|Environment\.NewLine)\b", RegexOptions.Compiled)
    Private Shared ReadOnly CharacterStart As New Regex("^(?:vbTab\b|Chr\(|ChrW\()", RegexOptions.Compiled)
    Private Shared ReadOnly LastLiteral As New Regex("""((?:[^""]|"""")*)""\s*\)\s*$", RegexOptions.Compiled)

    ''' <summary>The findings of one file: "name:line: statement".</summary>
    Friend Shared Function ScanLines(fileName As String, lines As IList(Of String)) As List(Of String)
        Dim found As New List(Of String)()
        Dim regions As New Stack(Of String)()      ' "modern" | "legacy" | "both"
        Dim buffer As New StringBuilder()
        Dim startLine As Integer = 0

        For index As Integer = 0 To lines.Count - 1
            Dim trimmed As String = lines(index).Trim()
            If trimmed.StartsWith("#If", StringComparison.Ordinal) Then
                If Regex.IsMatch(trimmed, "^#If\s+Not\s+NETFRAMEWORK\b") Then
                    regions.Push("modern")
                ElseIf Regex.IsMatch(trimmed, "^#If\s+NETFRAMEWORK\b") Then
                    regions.Push("legacy")
                Else
                    regions.Push("both")
                End If
                Continue For
            End If
            If trimmed.StartsWith("#Else", StringComparison.Ordinal) Then
                If regions.Count > 0 Then
                    Dim top As String = regions.Pop()
                    regions.Push(If(top = "modern", "legacy", If(top = "legacy", "modern", "both")))
                End If
                Continue For
            End If
            If trimmed.StartsWith("#End If", StringComparison.Ordinal) Then
                If regions.Count > 0 Then regions.Pop()
                Continue For
            End If
            If regions.Contains("legacy") Then
                buffer.Clear()
                Continue For
            End If

            Dim code As String = StripComment(lines(index)).TrimEnd()
            If buffer.Length = 0 Then startLine = index + 1
            buffer.Append(" "c).Append(code.Trim())
            If code.EndsWith("&", StringComparison.Ordinal) OrElse code.EndsWith(" _", StringComparison.Ordinal) OrElse
               code.EndsWith(",", StringComparison.Ordinal) OrElse code.EndsWith("(", StringComparison.Ordinal) Then Continue For

            Dim statement As String = buffer.ToString().Trim()
            buffer.Clear()
            If Not TranslatedCall.IsMatch(statement) Then Continue For
            If StatementHasFinding(statement) Then
                found.Add(fileName & ":" & startLine.ToString() & ": " & If(statement.Length > 140, statement.Substring(0, 140), statement))
            End If
        Next
        Return found
    End Function

    Private Shared Function StatementHasFinding(statement As String) As Boolean
        If ChainHasFinding(statement) Then Return True
        For Each group As String In InnerGroups(statement)
            If ChainHasFinding(group) Then Return True
        Next
        Return False
    End Function

    ''' <summary>One expression: arguments split at top-level commas, each argument's chain split at top-level
    ''' concatenations, the chain split again at newline constants.</summary>
    Private Shared Function ChainHasFinding(expression As String) As Boolean
        For Each argument As String In SplitTop(expression, ","c)
            Dim operands As List(Of String) = SplitTop(argument, "&"c)
            If operands.Count < 2 Then Continue For

            Dim kinds As New List(Of OperandKind)()
            Dim texts As New List(Of String)()
            For position As Integer = 0 To operands.Count - 1
                Dim text As String = If(position = 0, CleanFirstOperand(operands(position)), operands(position))
                kinds.Add(Classify(text))
                texts.Add(text)
            Next

            Dim segmentKinds As New List(Of OperandKind)()
            Dim segmentTexts As New List(Of String)()
            For position As Integer = 0 To kinds.Count - 1
                If kinds(position) = OperandKind.Newline Then
                    If SegmentHasFrameAndValue(segmentKinds, segmentTexts) Then Return True
                    segmentKinds.Clear()
                    segmentTexts.Clear()
                Else
                    segmentKinds.Add(kinds(position))
                    segmentTexts.Add(texts(position))
                End If
            Next
            If SegmentHasFrameAndValue(segmentKinds, segmentTexts) Then Return True
        Next
        Return False
    End Function

    Private Shared Function SegmentHasFrameAndValue(kinds As List(Of OperandKind), texts As List(Of String)) As Boolean
        Dim firstValue As Integer = -1
        Dim hasFrame As Boolean = False
        For position As Integer = 0 To kinds.Count - 1
            If kinds(position) = OperandKind.Value AndAlso firstValue < 0 Then firstValue = position
            If kinds(position) = OperandKind.Translated Then
                ' A complete sentence that comes AFTER a value is a second sentence, not a frame around the value.
                Dim appendedSentence As Boolean = firstValue >= 0 AndAlso IsCompleteSentence(texts(position))
                If Not appendedSentence Then hasFrame = True
            End If
        Next
        Return hasFrame AndAlso kinds.Contains(OperandKind.Value)
    End Function

    ''' <summary>The key ends the way a sentence ends - a full stop, a question or exclamation mark - and not with the
    ''' house ellipsis "..", which marks a label or a command in progress.</summary>
    Private Shared Function IsCompleteSentence(translatedCall As String) As Boolean
        Dim match As Match = LastLiteral.Match(translatedCall.Trim())
        If Not match.Success Then Return False
        Dim key As String = match.Groups(1).Value
        If key.EndsWith("..", StringComparison.Ordinal) Then Return False
        Return key.EndsWith(".", StringComparison.Ordinal) OrElse key.EndsWith("!", StringComparison.Ordinal) OrElse key.EndsWith("?", StringComparison.Ordinal)
    End Function

    Private Shared Function Classify(operand As String) As OperandKind
        Dim text As String = StripParentheses(operand)
        If text.Length = 0 Then Return OperandKind.Empty
        If FormattedStart.IsMatch(text) Then Return OperandKind.Formatted
        If TranslatedStart.IsMatch(text) Then Return OperandKind.Translated
        If text.StartsWith("""", StringComparison.Ordinal) Then Return OperandKind.Literal
        If NewlineStart.IsMatch(text) Then Return OperandKind.Newline
        If CharacterStart.IsMatch(text) Then Return OperandKind.Character
        If text.StartsWith("If(", StringComparison.Ordinal) AndAlso text.EndsWith(")", StringComparison.Ordinal) Then
            ' A conditional whose branches are all words is words: the value is what it tests, not what it returns.
            Dim arguments As List(Of String) = SplitTop(text.Substring(3, text.Length - 4), ","c)
            If arguments.Count >= 2 AndAlso Not arguments.Skip(1).Any(Function(branch) Classify(branch) = OperandKind.Value) Then
                Return OperandKind.Literal
            End If
        End If
        Return OperandKind.Value
    End Function

    ''' <summary>The first operand of a chain carries what precedes it in the statement: the target of an assignment,
    ''' a Return, an If..Then, a property initialiser. None of that is part of the sentence.</summary>
    Private Shared Function CleanFirstOperand(operand As String) As String
        Dim text As String = operand.Trim()
        For pass As Integer = 0 To 5
            Dim before As String = text
            text = Regex.Replace(text, "^(?:.*?\s)?&=\s*", "")
            text = Regex.Replace(text, "^Return\s+", "")
            text = Regex.Replace(text, "^(?:Sub|Function)\s*\([^)]*\)\s*", "")
            text = Regex.Replace(text, "^Case\b[^:]*:\s*Return\s+", "")
            text = Regex.Replace(text, "^Dim\s+\w+(?:\s+As\s+[\w\.\(\)]+)?\s*=\s*", "")
            text = Regex.Replace(text, "^[\w\.\(\)]+\s*=\s*(?!=)", "")
            text = Regex.Replace(text, "^.*?\{\s*\.\w+\s*=\s*", "")
            text = Regex.Replace(text, "^\.\w+\s*=\s*", "")
            text = Regex.Replace(text, "^(?:If\b.*?\bThen|Else)\s+", "")
            text = text.Trim()
            If String.Equals(text, before, StringComparison.Ordinal) Then Exit For
        Next
        Return text
    End Function

    Private Shared Function StripParentheses(operand As String) As String
        Dim text As String = operand.Trim()
        While text.Length >= 2 AndAlso text(0) = "("c AndAlso text(text.Length - 1) = ")"c AndAlso OuterPairMatches(text)
            text = text.Substring(1, text.Length - 2).Trim()
        End While
        Return text
    End Function

    Private Shared Function OuterPairMatches(text As String) As Boolean
        Dim depth As Integer = 0
        Dim inString As Boolean = False
        For index As Integer = 0 To text.Length - 1
            Dim c As Char = text(index)
            If c = """"c Then
                inString = Not inString
            ElseIf Not inString Then
                If c = "("c Then
                    depth += 1
                ElseIf c = ")"c Then
                    depth -= 1
                    If depth = 0 AndAlso index < text.Length - 1 Then Return False
                End If
            End If
        Next
        Return True
    End Function

    ''' <summary>Splits at a separator that is outside string literals and outside any bracket pair. "&amp;=" is
    ''' not a concatenation operand boundary.</summary>
    Private Shared Function SplitTop(text As String, separator As Char) As List(Of String)
        Dim parts As New List(Of String)()
        Dim depth As Integer = 0
        Dim inString As Boolean = False
        Dim start As Integer = 0
        Dim index As Integer = 0
        While index < text.Length
            Dim c As Char = text(index)
            If c = """"c Then
                If inString AndAlso index + 1 < text.Length AndAlso text(index + 1) = """"c Then
                    index += 2
                    Continue While
                End If
                inString = Not inString
            ElseIf Not inString Then
                If c = "("c OrElse c = "{"c Then
                    depth += 1
                ElseIf c = ")"c OrElse c = "}"c Then
                    depth -= 1
                ElseIf c = separator AndAlso depth = 0 Then
                    If separator = "&"c AndAlso index + 1 < text.Length AndAlso text(index + 1) = "="c Then
                        index += 2
                        Continue While
                    End If
                    parts.Add(text.Substring(start, index - start).Trim())
                    start = index + 1
                End If
            End If
            index += 1
        End While
        parts.Add(text.Substring(start).Trim())
        Return parts
    End Function

    ''' <summary>The text inside every bracket pair of a statement (a call's arguments, an object initialiser), so a
    ''' chain nested in a call is read as a chain.</summary>
    Private Shared Function InnerGroups(statement As String) As List(Of String)
        Dim groups As New List(Of String)()
        Dim opens As New Stack(Of Integer)()
        Dim inString As Boolean = False
        Dim index As Integer = 0
        While index < statement.Length
            Dim c As Char = statement(index)
            If c = """"c Then
                If inString AndAlso index + 1 < statement.Length AndAlso statement(index + 1) = """"c Then
                    index += 2
                    Continue While
                End If
                inString = Not inString
            ElseIf Not inString Then
                If c = "("c OrElse c = "{"c Then
                    opens.Push(index)
                ElseIf (c = ")"c OrElse c = "}"c) AndAlso opens.Count > 0 Then
                    Dim open As Integer = opens.Pop()
                    groups.Add(statement.Substring(open + 1, index - open - 1))
                End If
            End If
            index += 1
        End While
        Return groups
    End Function

    Private Shared Function StripComment(line As String) As String
        Dim inString As Boolean = False
        For index As Integer = 0 To line.Length - 1
            Dim c As Char = line(index)
            If c = """"c Then
                inString = Not inString
            ElseIf c = "'"c AndAlso Not inString Then
                Return line.Substring(0, index)
            End If
        Next
        Return line
    End Function

    ' =====================================================================================================
    ' the reading itself: what it must flag, and what it must not
    ' =====================================================================================================

    Private Shared Function Findings(ParamArray code As String()) As List(Of String)
        Return ScanLines("fixture.vb", code)
    End Function

    <Theory>
    <InlineData("lblOcrStatus.Text = Localization.T(""Модель установлена: "") & modelName")>
    <InlineData("Dim text As String = size & "" · "" & Localization.T(""изменено"")")>
    <InlineData("Return Localization.T(""Версия"") & "": "" & Application.ProductVersion")>
    <InlineData("section.AddBodyRow(New Label With {.Text = Localization.T(""Версия"") & "": "" & version, .AutoSize = True})")>
    <InlineData("AppendLine(Localization.T(row.LabelKey) & "": "" & row.Value)")>
    <InlineData("x.AccessibleName = If(badge.Length = 0, """", Localization.T(""Повтор A-B"") & "": "" & badge)")>
    <InlineData("lbl.Text = Localization.T(""Проверка.."") & "" "" & done.ToString() & ""/"" & total.ToString()")>
    <InlineData("dialog.FileName = stem & Localization.T("" (ред.)"") & extension")>
    <InlineData("Sub(s) lbl.Text = (Localization.T(""Скачивание: "")) & s")>
    <InlineData("Dim a As String = Localization.T(""Строка"") & value & vbCrLf & Localization.T(""Другая"")")>
    Public Sub A_translated_fragment_glued_to_a_value_is_found(code As String)
        Assert.Single(Findings(code))
    End Sub

    <Theory>
    <InlineData("lbl.Text = Localization.TF(""Модель установлена: {0}"", modelName)")>
    <InlineData("Return Localization.T(""Первая"") & "" "" & Localization.T(""Вторая"")")>
    <InlineData("Return Localization.T(""Файлы PEM"") & ""|*.pem;*.crt|"" & Localization.T(""Все файлы"") & ""|*.*""")>
    <InlineData("text &= "" "" & Localization.T(""(основной)"")")>
    <InlineData("text &= vbCrLf & Localization.T(""Это последняя отмеченная папка."")")>
    <InlineData("Dim q As String = HostingText.RemoveNote() & vbCrLf & vbCrLf & Localization.T(""Удалить роль сервера?"")")>
    <InlineData("Return Localization.T(""опубликована"") & If(row.PinProtected, "" · "" & Localization.T(""защищено PIN-кодом""), """")")>
    <InlineData("Dim label As String = Localization.T(""Название"")")>
    <InlineData("Dim n As Integer = a & b")>
    <InlineData("x = Localization.T(""Экспорт.."")")>
    Public Sub A_join_of_words_or_sentences_is_not_a_finding(code As String)
        Assert.Empty(Findings(code))
    End Sub

    <Fact>
    Public Sub A_complete_sentence_appended_after_a_value_is_not_a_finding()
        Assert.Empty(Findings("lbl_Status.Text = (lbl_Status.Text & "" "" & Localization.T(""Во внешнем плеере не открыто."")).Trim()"))
        Assert.Empty(Findings("Dim note As String = reason & "" "" & Localization.T(""Повторите попытку."")"))
    End Sub

    ''' <summary>The house ellipsis ".." marks a label or a command in progress, not the end of a sentence.</summary>
    <Fact>
    Public Sub A_label_ending_in_the_house_ellipsis_is_not_a_sentence()
        Assert.Single(Findings("lbl_Status.Text = (lbl_Status.Text & "" "" & Localization.T(""Во внешнем плеере не открыто.."")).Trim()"))
    End Sub

    <Fact>
    Public Sub A_sentence_before_a_value_is_still_a_frame()
        ' The sentence comes first and the value completes it: the order is the translator's to choose.
        Assert.Single(Findings("Dim s As String = Localization.T(""Файл не найден."") & "" "" & path"))
    End Sub

    <Fact>
    Public Sub A_statement_across_physical_lines_is_one_statement()
        Dim found As List(Of String) = Findings(
            "lbl.Text = Localization.T(""Версия"") & "": "" &",
            "    Application.ProductVersion")
        Assert.Single(found)
        Assert.StartsWith("fixture.vb:1:", found(0))
    End Sub

    <Fact>
    Public Sub A_comment_is_not_code()
        Assert.Empty(Findings("' Localization.T(""Версия"") & version   (the old way)"))
        Assert.Empty(Findings("x = 1 ' & Localization.T(""Версия"") & version"))
    End Sub

    <Fact>
    Public Sub Code_the_x86_fallback_alone_compiles_is_left_to_the_maintenance_policy()
        Assert.Empty(Findings(
            "#If NETFRAMEWORK Then",
            "lbl.Text = Localization.T(""Скачивание: "") & name",
            "#End If"))
        Assert.Single(Findings(
            "#If NETFRAMEWORK Then",
            "lbl.Text = Localization.T(""Скачивание: "") & name",
            "#Else",
            "lbl.Text = Localization.T(""Скачивание: "") & name",
            "#End If"))
        Assert.Single(Findings(
            "#If Not NETFRAMEWORK Then",
            "lbl.Text = Localization.T(""Скачивание: "") & name",
            "#End If"))
    End Sub

    ' =====================================================================================================
    ' the gate
    ' =====================================================================================================

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src/ not found above " & AppContext.BaseDirectory)
    End Function

    ''' <summary>The gate itself: no registered fragment is glued to a value anywhere in the viewer or the Share Manager
    ''' (the localization tables are string literals only and are not read - they hold no calls).</summary>
    <Fact>
    Public Sub No_translated_fragment_is_glued_to_a_value_in_either_program()
        Dim found As New List(Of String)()
        Dim root As String = SourceDir()
        For Each path As String In Directory.GetFiles(root, "*.vb", SearchOption.AllDirectories)
            If path.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) >= 0 OrElse path.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            Dim name As String = System.IO.Path.GetFileName(path)
            If name.StartsWith("Localization.", StringComparison.Ordinal) Then Continue For
            found.AddRange(ScanLines(path.Substring(root.Length).TrimStart("\"c), File.ReadAllLines(path)))
        Next
        Assert.True(found.Count = 0,
                    "A translated fragment is glued to a value - register ONE sentence with the value in a placeholder " &
                    "(Localization.TF(""..: {0}"", value)): " & Environment.NewLine & String.Join(Environment.NewLine, found))
    End Sub

End Class
#End If
