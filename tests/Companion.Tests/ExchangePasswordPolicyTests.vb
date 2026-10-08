Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The VB pre-check of the exchange server's password floor against the SHARED vectors file the Go suite
''' also runs (ticket 142 T-2): <c>exchange-server/internal/password/testdata/password-policy.json</c>. The
''' binary owns the policy; this copy only gives immediate feedback, and these tests are what stop the two
''' from drifting.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangePasswordPolicyTests

    Private Shared Function VectorsPath() As String
        Return Path.Combine(ExchangeTestSupport.RepoRoot(), "exchange-server", "internal", "password", "testdata", "password-policy.json")
    End Function

    <Fact>
    Public Sub Every_row_of_the_shared_vectors_file_gets_the_same_verdict_as_the_Go_rule()
        Dim path As String = VectorsPath()
        Assert.True(File.Exists(path), "The shared vectors file is missing: " & path)
        Using doc As JsonDocument = JsonDocument.Parse(File.ReadAllText(path))
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind)
            Dim rows As Integer = 0
            Dim disagreements As New List(Of String)()
            For Each row As JsonElement In doc.RootElement.EnumerateArray()
                rows += 1
                Dim password As String = row.GetProperty("password").GetString()
                Dim accept As Boolean = row.GetProperty("accept").GetBoolean()
                If ExchangePasswordPolicy.IsAccepted(password) <> accept Then
                    disagreements.Add("'" & password & "' (" & row.GetProperty("why").GetString() & ") - the file says accept=" & accept.ToString())
                End If
            Next
            Assert.True(rows >= 10, "Only " & rows & " vectors - the file did not load as expected.")
            Assert.True(disagreements.Count = 0, "The VB pre-check and the Go rule disagree:" & vbLf & String.Join(vbLf, disagreements))
        End Using
    End Sub

    <Fact>
    Public Sub The_three_named_examples_hold()
        Assert.False(ExchangePasswordPolicy.IsAccepted("Abcdefgh1"))      ' nine characters
        Assert.False(ExchangePasswordPolicy.IsAccepted("abcdefghij1"))    ' no upper-case Latin letter
        Assert.True(ExchangePasswordPolicy.IsAccepted("Abcdefghi1"))      ' exactly ten, all three classes
    End Sub

    <Fact>
    Public Sub The_verdict_names_the_first_failing_rule_in_the_Go_order()
        Assert.Equal(ExchangePasswordPolicy.Verdict.TooShort, ExchangePasswordPolicy.Check("Abcdefgh1"))
        Assert.Equal(ExchangePasswordPolicy.Verdict.TooShort, ExchangePasswordPolicy.Check(""))
        Assert.Equal(ExchangePasswordPolicy.Verdict.NoUpper, ExchangePasswordPolicy.Check("abcdefghij1"))
        Assert.Equal(ExchangePasswordPolicy.Verdict.NoLower, ExchangePasswordPolicy.Check("ABCDEFGHIJ1"))
        Assert.Equal(ExchangePasswordPolicy.Verdict.NoDigit, ExchangePasswordPolicy.Check("Abcdefghij"))
        Assert.Equal(ExchangePasswordPolicy.Verdict.Accepted, ExchangePasswordPolicy.Check("Passw0rdTest"))
    End Sub

    ''' <summary>Counted in code points like Go's utf8.RuneCountInString: ten astral characters are ten, and
    ''' a Cyrillic letter is no Latin lower-case letter.</summary>
    Private Shared ReadOnly AstralPair As String = Char.ConvertFromUtf32(&H1F600)

    <Fact>
    Public Sub Characters_are_counted_as_code_points_and_the_classes_are_ASCII_only()
        ' Nine ASCII characters plus one astral character (two UTF-16 units): ten code points, so the length
        ' rule passes; the three classes are all there.
        Assert.True(ExchangePasswordPolicy.IsAccepted("Abcdefgh1" & AstralPair))
        ' Eight Latin characters and one astral pair: nine code points although eleven UTF-16 units.
        Assert.Equal(ExchangePasswordPolicy.Verdict.TooShort, ExchangePasswordPolicy.Check("Abcdefg1" & AstralPair))
        Assert.False(ExchangePasswordPolicy.IsAccepted("Абвгдежзий1A"))
    End Sub

    <Fact>
    Public Sub Every_refusal_has_its_own_sentence_in_every_language_and_acceptance_has_none()
        Dim saved As String = Localization.CurrentCode
        Try
            Dim verdicts As ExchangePasswordPolicy.Verdict() = {ExchangePasswordPolicy.Verdict.TooShort, ExchangePasswordPolicy.Verdict.NoUpper,
                                                                ExchangePasswordPolicy.Verdict.NoLower, ExchangePasswordPolicy.Verdict.NoDigit}
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                Dim texts As List(Of String) = verdicts.Select(Function(v) ExchangeText.PolicyReason(v)).ToList()
                Assert.All(texts, Sub(t) Assert.False(String.IsNullOrWhiteSpace(t)))
                Assert.Equal(texts.Count, texts.Distinct().Count())
                Assert.Equal("", ExchangeText.PolicyReason(ExchangePasswordPolicy.Verdict.Accepted))
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ' --- the generator (ticket 168: a password made for an account, shown once) ------------------------------

    <Fact>
    Public Sub A_generated_password_always_meets_the_floor_has_the_fixed_length_and_avoids_look_alikes()
        For i As Integer = 1 To 500
            Dim p As String = ExchangePasswordPolicy.Generate()
            Assert.True(ExchangePasswordPolicy.IsAccepted(p), p.Length.ToString())
            Assert.Equal(ExchangePasswordPolicy.GeneratedLength, p.Length)
            Assert.True(p.IndexOfAny("Il1O0".ToCharArray()) < 0, "a look-alike character in a password that is read out or retyped")
            Assert.True(p.All(Function(c) (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "0"c AndAlso c <= "9"c)))
        Next
    End Sub

    <Fact>
    Public Sub Generated_passwords_differ_and_do_not_keep_their_guaranteed_characters_in_front()
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        Dim startsWithUpper As Integer = 0
        For i As Integer = 1 To 300
            Dim p As String = ExchangePasswordPolicy.Generate()
            seen.Add(p)
            If Char.IsUpper(p(0)) Then startsWithUpper += 1
        Next
        Assert.Equal(300, seen.Count)
        ' the three guaranteed characters are shuffled in: the first character is an upper-case letter well short of every time
        Assert.InRange(startsWithUpper, 40, 240)
    End Sub

    <Fact>
    Public Sub The_generator_uses_the_operating_system_random_source_and_no_clock_or_seed()
        Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Core\ExchangePasswordPolicy.vb"))
        Assert.Contains("RandomNumberGenerator.GetInt32", code)
        Assert.DoesNotContain("New Random", code)
        Assert.DoesNotContain("Rnd(", code)
        Assert.DoesNotContain("DateTime", code)
    End Sub

End Class
