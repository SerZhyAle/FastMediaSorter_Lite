Option Strict On

Imports System.Collections
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports Xunit

''' <summary>
''' OCR-PIPELINE amendments 1.7 A (the line split cuts before a word taller than the type-size
''' ratio) and 1.8 A/B (CJK words and lines join without a space), ticket 183: the reference
''' implementation's own shared cases - the ones its Go and JavaScript editions both replay - run
''' through this product's pure functions.
'''
''' The files are vendored under TestData\ocr-pipeline-vectors beside a PROVENANCE.txt whose
''' digests are checked first, the .fd-sec way. They are NOT the catalog's: the catalog ships no
''' vector for either amendment and names these two files as the pins, so what they prove is
''' agreement with the reference on its own material - the real word boxes of two touching balloons
''' on a public-service Superman page, and the Pepper&amp;Carrot lettering the join was measured on.
'''
''' Runs on BOTH legs: the split, the join and the block builder are shared code that both shipped
''' exes compile.
''' </summary>
Public Class OcrPipelineVectorTests

    Private Const StitchFile As String = "ocr_balloon_stitch_cases.json"
    Private Const JoinFile As String = "ocr_cjk_join_cases.json"

    Private Shared ReadOnly VectorDirectory As String =
        Path.Combine(AppContext.BaseDirectory, "TestData", "ocr-pipeline-vectors")

    ' ------------------------------------------------------------------------- reading the JSON --

    Private Shared Function Load(fileName As String) As Dictionary(Of String, Object)
        ' The viewer's own portable JSON facade: JavaScriptSerializer on net48, System.Text.Json on
        ' net10, both shaped to Dictionary(Of String, Object) / Object() / numbers.
        Dim node As Object = TranslateHttp.JsonDeserializeObject(File.ReadAllText(Path.Combine(VectorDirectory, fileName)))
        Dim map As Dictionary(Of String, Object) = TryCast(node, Dictionary(Of String, Object))
        Assert.NotNull(map)
        Return map
    End Function

    Private Shared Function Items(node As Object) As List(Of Object)
        Assert.False(TypeOf node Is String)
        Dim sequence As IEnumerable = TryCast(node, IEnumerable)
        Assert.NotNull(sequence)
        Return sequence.Cast(Of Object)().ToList()
    End Function

    Private Shared Function Obj(node As Object) As Dictionary(Of String, Object)
        Dim map As Dictionary(Of String, Object) = TryCast(node, Dictionary(Of String, Object))
        Assert.NotNull(map)
        Return map
    End Function

    Private Shared Function Str(node As Object) As String
        Return Convert.ToString(node, CultureInfo.InvariantCulture)
    End Function

    Private Shared Function IntOf(node As Object) As Integer
        Return Convert.ToInt32(node, CultureInfo.InvariantCulture)
    End Function

    ' ------------------------------------------------------------------------------ provenance --

    <Theory>
    <InlineData(StitchFile)>
    <InlineData(JoinFile)>
    Public Sub Vendored_cases_match_their_recorded_digest(fileName As String)
        Dim provenance As String = File.ReadAllText(Path.Combine(VectorDirectory, "PROVENANCE.txt"))
        Dim actual As String
        Using sha As SHA256 = SHA256.Create()
            actual = String.Concat(sha.ComputeHash(File.ReadAllBytes(Path.Combine(VectorDirectory, fileName))).Select(Function(b) b.ToString("x2")))
        End Using

        Assert.Contains("  " & actual & "  " & fileName, provenance)
    End Sub

    ' ------------------------------------------------------------ 1.7 A: the tall-word cut --

    Public Shared Function StitchCases() As IEnumerable(Of Object())
        Return Items(Load(StitchFile)("cases")).
            Select(Function(c, i) New Object() {i, Str(Obj(c)("name"))}).ToList()
    End Function

    ''' <summary>
    ''' The three cases of the producer's split fixture, replayed through <c>SplitLine</c>: the word
    ''' boxes are the engine's own for two balloons whose touching outlines were read as tokens
    ''' ("J at 2.3x, a lone "A" at 2.1x the line's word height), and a real single-size line is
    ''' the negative. Expected is the TEXT of each run - which also holds the join, since the run's
    ''' text is rebuilt from its own words.
    ''' </summary>
    <Theory, MemberData(NameOf(StitchCases))>
    Public Sub Stitch_case_is_cut_where_the_reference_cuts(index As Integer, name As String)
        Dim spec As Dictionary(Of String, Object) = Obj(Items(Load(StitchFile)("cases"))(index))
        Assert.Equal(name, Str(spec("name")))

        Dim words As New List(Of OcrWord)
        For Each w As Object In Items(spec("words"))
            Dim word As Dictionary(Of String, Object) = Obj(w)
            Dim box As Dictionary(Of String, Object) = Obj(word("bbox"))
            words.Add(New OcrWord With {
                .Text = Str(word("text")),
                .Box = Rectangle.FromLTRB(IntOf(box("x0")), IntOf(box("y0")), IntOf(box("x1")), IntOf(box("y1"))),
                .Confidence = CSng(Convert.ToDouble(word("conf"), CultureInfo.InvariantCulture) / 100.0)
            })
        Next

        Dim line As New OcrLine With {
            .Words = words,
            .Text = String.Join(" ", words.Select(Function(w) w.Text)),
            .Box = words.Select(Function(w) w.Box).Aggregate(Function(a, b) Rectangle.Union(a, b)),
            .Confidence = CSng(words.Average(Function(w) w.Confidence))
        }

        Dim want As List(Of String) = Items(spec("want")).Select(Function(t) Str(t)).ToList()
        Dim runs As List(Of OcrLine) = OcrLineSplitter.SplitLine(line)

        Assert.Equal(want, runs.Select(Function(r) r.Text).ToList())
        ' A line that is not cut comes back BY IDENTITY - the negative case is exactly that.
        If want.Count = 1 Then Assert.Same(line, runs(0))
    End Sub

    ' -------------------------------------------------------------- 1.8 A: words in a line --

    Public Shared Function WordCases() As IEnumerable(Of Object())
        Return Items(Load(JoinFile)("words")).
            Select(Function(c, i) New Object() {i, Str(Obj(c)("about"))}).ToList()
    End Function

    ''' <summary>The producer's ten word-join cases: Japanese cut to single characters, a fullwidth
    ''' comma that joins and an ASCII "!" that keeps its space, Hangul under and at the 0.33 bound,
    ''' hanja beside Hangul, a word with no height, the prolonged sound mark, Latin and digits
    ''' beside CJK, and a Latin line untouched however tight its gaps.</summary>
    <Theory, MemberData(NameOf(WordCases))>
    Public Sub Word_join_case_is_joined_where_the_reference_joins(index As Integer, about As String)
        Dim spec As Dictionary(Of String, Object) = Obj(Items(Load(JoinFile)("words"))(index))
        Assert.Equal(about, Str(spec("about")))

        ' A word is [text, x0, y0, x1, y1] in recognizer pixels.
        Dim words As New List(Of OcrWord)
        For Each w As Object In Items(spec("words"))
            Dim f As List(Of Object) = Items(w)
            Assert.Equal(5, f.Count)
            words.Add(New OcrWord With {
                .Text = Str(f(0)),
                .Box = Rectangle.FromLTRB(IntOf(f(1)), IntOf(f(2)), IntOf(f(3)), IntOf(f(4)))
            })
        Next

        Assert.Equal(Str(spec("want")), OcrTextJoin.JoinLineWords(words))
    End Sub

    ' -------------------------------------------------------------- 1.8 B: lines in a plate --

    Public Shared Function LineCases() As IEnumerable(Of Object())
        Return Items(Load(JoinFile)("lines")).
            Select(Function(c, i) New Object() {i, Str(Obj(c)("about"))}).ToList()
    End Function

    ''' <summary>The producer's four plate cases: a Japanese balloon that wraps with no space, a
    ''' Chinese line ending on a fullwidth comma, a Korean line break that keeps its space, and a
    ''' Latin line meeting a CJK one.</summary>
    <Theory, MemberData(NameOf(LineCases))>
    Public Sub Plate_join_case_is_joined_where_the_reference_joins(index As Integer, about As String)
        Dim spec As Dictionary(Of String, Object) = Obj(Items(Load(JoinFile)("lines"))(index))
        Assert.Equal(about, Str(spec("about")))

        Dim lines As List(Of String) = Items(spec("lines")).Select(Function(t) Str(t)).ToList()
        Assert.Equal(Str(spec("want")), OcrTextJoin.JoinPlateLines(lines))
    End Sub

    ''' <summary>The fixture is the rule's pin only while it is whole: the counts are the ones
    ''' the PROVENANCE names, so a case dropped from the file cannot go unnoticed even with its
    ''' digest re-taken.</summary>
    <Fact>
    Public Sub The_fixtures_carry_the_cases_the_provenance_names()
        Assert.Equal(3, Items(Load(StitchFile)("cases")).Count)
        Assert.Equal(10, Items(Load(JoinFile)("words")).Count)
        Assert.Equal(4, Items(Load(JoinFile)("lines")).Count)
    End Sub

End Class
