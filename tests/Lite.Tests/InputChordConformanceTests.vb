#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text.Json
Imports Xunit

Namespace Lite.Tests

    ''' <summary>
    ''' `INPUT-CHORD` 0.3 rules 1-3 as the viewer's shortcut model meets them: the catalog's token vectors
    ''' (vendored, digest asserted first) run through <c>CustomHotkeys.Canonical</c>, the one reader and writer of
    ''' a stored chord.
    '''
    ''' The product does NOT yet conform on every keyboard row, and the honest form of that is a table, not a
    ''' skipped test: each row the model answers differently is recorded below with what it answers TODAY and
    ''' why. A row that conforms must keep conforming; a recorded row must keep answering exactly what is
    ''' recorded - so the day the canonical form is brought to rule 1 (modifier order Ctrl, Shift, Alt, Win, the
    ''' aliases, the tolerated empty token) the table fails and has to shrink in the same edit, and the registry
    ''' exception shrinks with it. That change touches every stored profile (a stored "Ctrl+Alt+Shift+N" would be
    ''' re-written), so it is a specification of its own and nothing here changes what the model answers.
    '''
    ''' The 18 mouse rows bind nothing in this product (an in-window table: no mouse chords, no global hook;
    ''' docs/contracts/INPUT-CONTROLS.md), so only the keyboard rows are run.
    '''
    ''' Modern-only, exactly like <c>CustomHotkeys.vb</c>.
    ''' </summary>
    Public Class InputChordConformanceTests

        Private Shared ReadOnly VectorDirectory As String =
            Path.Combine(AppContext.BaseDirectory, "TestData", "input-chord-vectors")

        Private Const VectorFile As String = "chord-tokens.jsonl"

        Private NotInheritable Class VectorRow
            Public Kind As String = ""
            Public Input As String = ""
            ''' <summary>The canonical spelling; "" when the row is inert (the vector's `inert: true`).</summary>
            Public Expected As String = ""
            Public Inert As Boolean
        End Class

        ''' <summary>What the model answers today for a keyboard row that does not conform, and why.</summary>
        Private NotInheritable Class Deviation
            Public ReadOnly Input As String
            Public ReadOnly Actual As String
            Public ReadOnly Reason As String

            Public Sub New(input As String, actual As String, reason As String)
                Me.Input = input
                Me.Actual = actual
                Me.Reason = reason
            End Sub
        End Class

        Private Const ReasonOrder As String = "rule 1: the model writes Ctrl, Alt, Shift - the contract says Ctrl, Shift, Alt, Win"
        Private Const ReasonWin As String = "rules 1-2: the Win modifier and its aliases (Meta, Super) are not modelled"
        Private Const ReasonAlias As String = "rule 2: the alias is not read"
        Private Const ReasonEmpty As String = "rule 2: an empty token (a doubled or trailing +) is not tolerated"
        Private Const ReasonRefusal As String = "rule 6 runs at parse time here: the combination is refused, not spelled"
        Private Const ReasonTrigger As String = "rule 1: the trigger set is wider than the canonical one (NumPad5 is written Num 5)"

        ''' <summary>
        ''' Recorded 2026-10-13 against catalog 0.3. Every other keyboard row is an exact match. The expected
        ''' spelling is the vector's, so a reader sees each gap without opening the vector file.
        ''' </summary>
        Private Shared ReadOnly RecordedDeviations As Deviation() = {
            New Deviation("Win+Space", "", ReasonWin),
            New Deviation("meta+space", "", ReasonWin),
            New Deviation("Super+Space", "", ReasonWin),
            New Deviation("Win+Alt+Shift+Ctrl+Backspace", "", ReasonWin),
            New Deviation("Alt+F4", "", ReasonRefusal),
            New Deviation("Ctrl+Escape", "", ReasonRefusal),
            New Deviation("Ctrl+Alt+Return", "", ReasonAlias),
            New Deviation("Ctrl+Del", "", ReasonAlias),
            New Deviation("Ctrl+Ins", "", ReasonAlias),
            New Deviation("Ctrl+Shift+PgUp", "", ReasonAlias),
            New Deviation("Ctrl+Shift+PgDn", "", ReasonAlias),
            New Deviation("Ctrl+Shift+PrtSc", "", ReasonAlias),
            New Deviation("Ctrl+Shift+PrtScn", "", ReasonAlias),
            New Deviation("Ctrl+Shift+Alt+N", "Ctrl+Alt+Shift+N", ReasonOrder),
            New Deviation("Ctrl++F12", "", ReasonEmpty),
            New Deviation("Ctrl+Shift+F12+", "", ReasonEmpty),
            New Deviation("Ctrl+Shift+NumPad5", "Ctrl+Shift+Num 5", ReasonTrigger)
        }

        ' ------------------------------------------------------------- loading ----

        Private Shared Function VectorPath(name As String) As String
            Return Path.Combine(VectorDirectory, name)
        End Function

        Private Shared Function ReadRows() As List(Of VectorRow)
            Dim rows As New List(Of VectorRow)()
            For Each line As String In File.ReadAllLines(VectorPath(VectorFile))
                If line.Trim().Length = 0 Then Continue For
                Using doc As JsonDocument = JsonDocument.Parse(line)
                    Dim root As JsonElement = doc.RootElement
                    Dim row As New VectorRow()
                    row.Kind = root.GetProperty("kind").GetString()
                    row.Input = root.GetProperty("in").GetString()
                    Dim canonical As JsonElement
                    If root.TryGetProperty("canonical", canonical) Then row.Expected = canonical.GetString()
                    Dim inert As JsonElement
                    If root.TryGetProperty("inert", inert) Then row.Inert = inert.GetBoolean()
                    rows.Add(row)
                End Using
            Next
            Return rows
        End Function

        Private Shared Function KeyboardRows() As List(Of VectorRow)
            Return ReadRows().Where(Function(r) r.Kind = "keyboard").ToList()
        End Function

        Private Shared Function FindDeviation(input As String) As Deviation
            Return RecordedDeviations.FirstOrDefault(Function(d) String.Equals(d.Input, input, StringComparison.Ordinal))
        End Function

        ' ---------------------------------------------------------- provenance ----

        ''' <summary>The vendored file still is what PROVENANCE.txt says: a changed vector would quietly redefine "conformant".</summary>
        <Fact>
        Public Sub The_vendored_vector_file_matches_its_recorded_digest()
            Dim provenance As String = File.ReadAllText(VectorPath("PROVENANCE.txt"))
            Dim actual As String = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(VectorPath(VectorFile)))).ToLowerInvariant()
            Assert.Contains("  " & actual & "  " & VectorFile, provenance)
        End Sub

        <Fact>
        Public Sub The_vector_file_has_the_rows_the_record_says()
            Dim rows As List(Of VectorRow) = ReadRows()
            Assert.Equal(52, rows.Count)
            Assert.Equal(34, rows.Where(Function(r) r.Kind = "keyboard").Count())
            Assert.Equal(18, rows.Where(Function(r) r.Kind = "mouse").Count())
        End Sub

        ' ---------------------------------------------------------- conformance ----

        ''' <summary>
        ''' Every keyboard row either answers the vector's canonical spelling (or nothing, for an inert row), or
        ''' is a recorded deviation that still answers exactly what is recorded.
        ''' </summary>
        <Fact>
        Public Sub Every_keyboard_row_conforms_or_is_a_recorded_deviation()
            Dim failures As New List(Of String)()
            For Each row As VectorRow In KeyboardRows()
                Dim actual As String = CustomHotkeys.Canonical(row.Input)
                Dim expected As String = If(row.Inert, "", row.Expected)
                Dim recorded As Deviation = FindDeviation(row.Input)

                If recorded Is Nothing Then
                    If Not String.Equals(actual, expected, StringComparison.Ordinal) Then
                        failures.Add("'" & row.Input & "' answers '" & actual & "', the vector says '" & expected & "' and no deviation is recorded")
                    End If
                Else
                    If String.Equals(recorded.Actual, expected, StringComparison.Ordinal) Then
                        failures.Add("'" & row.Input & "' is recorded as a deviation but its recorded answer equals the vector's: the table is wrong")
                    ElseIf Not String.Equals(actual, recorded.Actual, StringComparison.Ordinal) Then
                        failures.Add("'" & row.Input & "' answers '" & actual & "', the table records '" & recorded.Actual &
                                     "' (vector '" & expected & "'): the model moved - update the table and the registry exception together")
                    End If
                End If
            Next
            Assert.True(failures.Count = 0, String.Join(Environment.NewLine, failures))
        End Sub

        ''' <summary>The table names no row that is not in the vector file (a typo would hide a real deviation).</summary>
        <Fact>
        Public Sub Every_recorded_deviation_names_a_keyboard_row_of_the_vectors()
            Dim inputs As HashSet(Of String) = New HashSet(Of String)(KeyboardRows().Select(Function(r) r.Input), StringComparer.Ordinal)
            Dim unknown As List(Of String) = RecordedDeviations.Where(Function(d) Not inputs.Contains(d.Input)).Select(Function(d) d.Input).ToList()
            Assert.True(unknown.Count = 0, "Recorded deviations with no keyboard row: " & String.Join(", ", unknown))
            Assert.Equal(RecordedDeviations.Length, RecordedDeviations.Select(Function(d) d.Input).Distinct(StringComparer.Ordinal).Count())
        End Sub

        ''' <summary>
        ''' The summary the registry row quotes: how many of the 34 keyboard rows conform. Changing this number
        ''' is changing the product's conformance claim, so it is asserted by name.
        ''' </summary>
        <Fact>
        Public Sub Seventeen_of_the_thirty_four_keyboard_rows_conform()
            Dim rows As List(Of VectorRow) = KeyboardRows()
            Dim conforming As Integer = rows.Where(Function(r) FindDeviation(r.Input) Is Nothing).Count()
            Assert.Equal(34, rows.Count)
            Assert.Equal(17, RecordedDeviations.Length)
            Assert.Equal(17, conforming)
        End Sub

        ''' <summary>
        ''' What is held by the rows that conform, spelled out so a reader does not have to read the vector file:
        ''' case, spacing, the long modifier name, a repeated modifier and every inert shape are rule-true.
        ''' </summary>
        <Theory>
        <InlineData("ctrl+shift+f12", "Ctrl+Shift+F12")>
        <InlineData("Shift+Ctrl+F12", "Ctrl+Shift+F12")>
        <InlineData("control + 9", "Ctrl+9")>
        <InlineData("Ctrl+Ctrl+A", "Ctrl+A")>
        <InlineData("F24", "F24")>
        Public Sub The_held_rows_answer_the_canonical_spelling(written As String, expected As String)
            Assert.Equal(expected, CustomHotkeys.Canonical(written))
        End Sub

        <Theory>
        <InlineData("")>
        <InlineData("   ")>
        <InlineData("Ctrl+Shift")>
        <InlineData("Ctrl+Shift+Hyper+F12")>
        <InlineData("Ctrl+A+B")>
        <InlineData("Ctrl+F25")>
        <InlineData("Ctrl+F0")>
        <InlineData("Ctrl+Shift+0x41")>
        <InlineData("Ctrl+RightClick")>
        Public Sub An_inert_row_never_becomes_another_chord(written As String)
            Assert.Equal(String.Empty, CustomHotkeys.Canonical(written))
        End Sub

    End Class

End Namespace
#End If
