Option Strict On

Imports System.Collections.Generic
Imports System.Text.RegularExpressions

''' <summary>
''' Script and reading-order rules for the Arabic-script and Indic columns of the string
''' tables (ticket L0290), and the pt-BR marker list of the pt column (ticket L0291) - pure
''' functions over the cells of one row, so Lite.Tests and
''' Companion.Tests (linked, not copied) hold their own tables to the same rules.
'''
''' What a rule can see is limited on purpose. A cell in the wrong script is certain. A cell
''' stored in "visual" order (typed for a left-to-right paragraph, so it shows reversed in a
''' window with RightToLeft = Yes) leaves a few tell-tale shapes: a sentence mark that leads
''' the line, a bracket pair that does not close, a file-dialog pattern that moved. A cell that
''' is merely worded badly stays with the native-speaker review.
''' </summary>
Friend NotInheritable Class LocaleScriptRules

    Private Sub New()
    End Sub

    ''' <summary>The language of each translation slot of a table row (the Russian key is not a slot).</summary>
    Friend Shared ReadOnly SlotCodes As String() = {
        "en", "uk", "de", "it", "es", "fr", "pt", "ar", "hi", "bn", "ur", "zh"}

    ' A sentence mark that opens a line and sits directly against a letter: in a right-to-left
    ' paragraph that mark belongs at the END of the sentence, so the line was stored reversed.
    Private Shared ReadOnly LeadingExtension As New Regex("^\.[A-Za-z]")
    Private Shared ReadOnly LeadingMark As New Regex("^(?<mark>\.\.|\.|:|!|\?)\p{L}")

    Private Shared Function IsDevanagari(c As Integer, includeDanda As Boolean) As Boolean
        If c < &H900 OrElse c > &H97F Then Return False
        ' The danda (U+0964, U+0965) is the full stop of both Hindi and Bengali.
        If (c = &H964 OrElse c = &H965) AndAlso Not includeDanda Then Return False
        Return True
    End Function

    Private Shared Function IsBengali(c As Integer) As Boolean
        Return c >= &H980 AndAlso c <= &H9FF
    End Function

    Private Shared Function IsArabicScript(c As Integer) As Boolean
        Return (c >= &H600 AndAlso c <= &H6FF) OrElse (c >= &H750 AndAlso c <= &H77F) OrElse
               (c >= &HFB50 AndAlso c <= &HFDFF) OrElse (c >= &HFE70 AndAlso c <= &HFEFF)
    End Function

    ''' <summary>
    ''' The first letter of a script the language does not use, or Nothing: Devanagari or
    ''' Arabic-script (the Urdu full stop included) in bn, Bengali or Arabic-script in hi,
    ''' Devanagari (the danda included) or Bengali in ar and ur.
    ''' </summary>
    Friend Shared Function WrongScript(code As String, text As String) As String
        If String.IsNullOrEmpty(text) Then Return Nothing

        For i = 0 To text.Length - 1
            Dim c = Convert.ToInt32(text(i))
            Dim script As String = Nothing

            Select Case code
                Case "bn"
                    If IsDevanagari(c, False) Then
                        script = "Devanagari"
                    ElseIf IsArabicScript(c) Then
                        script = "Arabic-script"
                    End If
                Case "hi"
                    If IsBengali(c) Then
                        script = "Bengali"
                    ElseIf IsArabicScript(c) Then
                        script = "Arabic-script"
                    End If
                Case "ar", "ur"
                    If IsDevanagari(c, True) Then
                        script = "Devanagari"
                    ElseIf IsBengali(c) Then
                        script = "Bengali"
                    End If
            End Select

            If script IsNot Nothing Then
                Return code & " cell has a " & script & " character U+" & c.ToString("X4") & " at " & i
            End If
        Next

        Return Nothing
    End Function

    ''' <summary>
    ''' A line of an ar/ur cell that opens wrongly for a right-to-left paragraph: with a sentence
    ''' mark glued to a letter while the English source has no such line (a sentence stored in
    ''' visual order), or with a file-extension token (".fdd") and no left-to-right mark before the
    ''' dot - the bidi algorithm gives that dot the paragraph's direction and it lands after the
    ''' token ("fdd.").
    ''' </summary>
    Friend Shared Function BadLineStart(text As String, source As String) As String
        If String.IsNullOrEmpty(text) Then Return Nothing

        For Each part In text.Split(ChrW(10))
            Dim line = part.TrimEnd(ChrW(13))
            If LeadingExtension.IsMatch(line) Then
                Return "line opens with a file-extension token and no left-to-right mark"
            End If
            Dim m = LeadingMark.Match(line)
            If m.Success AndAlso Not AnyLineStartsWith(source, m.Groups("mark").Value) Then
                Return "line opens with '" & m.Groups("mark").Value & "' against a letter (visual order)"
            End If
        Next

        Return Nothing
    End Function

    Private Shared Function AnyLineStartsWith(text As String, prefix As String) As Boolean
        If String.IsNullOrEmpty(text) Then Return False
        For Each part In text.Split(ChrW(10))
            If part.TrimEnd(ChrW(13)).StartsWith(prefix, StringComparison.Ordinal) Then Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' A cell whose brackets do not close (or close before they open) while the English source
    ''' is balanced - what a reversed line looks like once its parentheses are not mirrored.
    ''' </summary>
    Friend Shared Function UnbalancedBrackets(text As String, source As String) As String
        If String.IsNullOrEmpty(text) OrElse String.IsNullOrEmpty(source) Then Return Nothing
        If IsBalanced(source) AndAlso Not IsBalanced(text) Then Return "brackets do not close"
        Return Nothing
    End Function

    Private Shared Function IsBalanced(s As String) As Boolean
        Dim round = 0
        Dim square = 0

        For Each ch In s
            Select Case ch
                Case "("c
                    round += 1
                Case ")"c
                    round -= 1
                    If round < 0 Then Return False
                Case "["c
                    square += 1
                Case "]"c
                    square -= 1
                    If square < 0 Then Return False
            End Select
        Next

        Return round = 0 AndAlso square = 0
    End Function

    ''' <summary>
    ''' A file-dialog filter (<c>description|pattern|description|pattern</c>) keeps the English
    ''' pattern segments byte for byte: they are not text, the dialog matches files with them.
    ''' Returns Nothing for a source that is not such a filter.
    ''' </summary>
    Friend Shared Function FilterPatternsDiffer(text As String, source As String) As String
        If text Is Nothing OrElse source Is Nothing Then Return Nothing

        Dim src = source.Split("|"c)
        If src.Length < 2 OrElse src.Length Mod 2 <> 0 Then Return Nothing
        For i = 1 To src.Length - 1 Step 2
            If src(i).IndexOf("*"c) < 0 OrElse src(i).IndexOf(" "c) >= 0 Then Return Nothing
        Next

        Dim tr = text.Split("|"c)
        If tr.Length <> src.Length Then
            Return "filter has " & tr.Length & " segments instead of " & src.Length
        End If
        For i = 1 To src.Length - 1 Step 2
            If Not String.Equals(tr(i), src(i), StringComparison.Ordinal) Then
                Return "filter pattern '" & tr(i) & "' instead of '" & src(i) & "'"
            End If
        Next

        Return Nothing
    End Function

    ' Words and constructions only European Portuguese uses in UI text (ticket L0291: the pt
    ' column is pt-BR, the Store locale pt-br). Only markers with no Brazilian reading are listed:
    ' "transferir", "guardar", "ligação" or "aplicação" are Brazilian too in some sense, so a cell
    ' that uses them in the European sense stays with the native-speaker review.
    Private Shared ReadOnly EuropeanPortuguese As New Regex(
        "\b(ficheiros?|palavras?-passe|utilizador(es)?|telemóve(l|is)|ecrãs?|regist(o|os|ar|ado|ada|ados|adas)|" &
        "contacto(s)?|contactar|secç(ão|ões)|receç(ão|ões)|deteç(ão|ões)|detet(ar|ado|ada|ados|adas)|facto|carácter|" &
        "aceder|premir|hiperligaç(ão|ões)|arranque|grelhas?|diapositivos?|ambiente de trabalho|" &
        "(iniciar|terminar) (a )?sessão|tem a certeza)\b|" &
        "\b(está|estão|estou|estava|estavam|esteja|estar|continua|continuar)\s+a\s+[a-zà-ú]+(ar|er|ir)\b",
        RegexOptions.IgnoreCase)

    ''' <summary>The first European Portuguese marker of a pt cell, or Nothing.</summary>
    Friend Shared Function EuropeanPortugueseWord(text As String) As String
        If String.IsNullOrEmpty(text) Then Return Nothing
        Dim m = EuropeanPortuguese.Match(text)
        If Not m.Success Then Return Nothing
        Return "pt-PT '" & m.Value & "' in a pt-BR column"
    End Function

    ''' <summary>
    ''' Every rule over every translation of every row (key, 12 translations en..zh).
    ''' <paramref name="inspected"/> counts the translations looked at, so a walk over an empty
    ''' table cannot pass as quietly as a clean one.
    ''' </summary>
    Friend Shared Function Audit(entries As IEnumerable(Of KeyValuePair(Of String, String())),
                                 ByRef inspected As Integer) As List(Of String)
        Dim findings As New List(Of String)()
        inspected = 0

        For Each entry In entries
            Dim values = entry.Value
            If values Is Nothing OrElse values.Length <> SlotCodes.Length Then Continue For

            Dim key = entry.Key
            Dim sep = key.IndexOf(ChrW(1))
            If sep >= 0 Then key = key.Substring(sep + 1)
            Dim english = values(0)

            For slot = 0 To SlotCodes.Length - 1
                Dim code = SlotCodes(slot)
                Dim value = values(slot)
                If String.IsNullOrEmpty(value) Then Continue For
                inspected += 1

                Dim problems As New List(Of String)()
                problems.Add(WrongScript(code, value))
                If code = "ar" OrElse code = "ur" Then
                    problems.Add(BadLineStart(value, english))
                    problems.Add(UnbalancedBrackets(value, english))
                End If
                If code = "pt" Then problems.Add(EuropeanPortugueseWord(value))
                If slot > 0 Then problems.Add(FilterPatternsDiffer(value, english))

                For Each problem In problems
                    If problem IsNot Nothing Then
                        findings.Add(code & "  <-  " & Shorten(key) & "  :  " & problem)
                    End If
                Next
            Next
        Next

        Return findings
    End Function

    Private Shared Function Shorten(s As String) As String
        Dim one = s.Replace(vbCr, " ").Replace(vbLf, " ")
        Return If(one.Length <= 60, one, one.Substring(0, 60) & "..")
    End Function

End Class
