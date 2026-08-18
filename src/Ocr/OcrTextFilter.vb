Option Strict On

Imports System.Globalization
Imports System.Text

''' <summary>
''' Is this block's text worth sending to a translator?
''' (SPECIFICATION_OCR_OVERLAY_ACCURACY.md, stage S3.)
'''
''' The same noise rejection the block builder always did, only sharper: a run of
''' consonants is OCR grinding on a texture, a bare URL or file path is not language, and
''' a block of mostly non-words is a misread pattern. It is stricter than the count of
''' letters it replaces, and the price of "stricter" is FALSE rejections - a translation
''' that used to appear and now does not. That is the risk this stage carries, and it is
''' checked by its own acceptance item rather than assumed away.
'''
''' Two whole scripts are handled by NOT applying the Latin/Cyrillic rules to them:
'''   - CJK has its own branch. "At least five letters" and "must contain a vowel" are not
'''     lenient or strict for ideographs, they are meaningless; two ideographs are a phrase.
'''   - Abjads and abugidas (Arabic, Hebrew, Devanagari, ..) do not write vowels at all, so
'''     the vowel rule is skipped rather than failed - applying it would reject every line
'''     of Arabic on the page.
'''
''' Both thresholds of the pipeline live here: <see cref="LineRejection"/> is the engine-level
''' one (is this recognized LINE worth keeping), <see cref="RejectionReason"/> the block-level
''' one (is this assembled BLOCK worth translating). Each states its answer as a reason rather
''' than a boolean so the same call can decide and be recorded - see <see cref="OcrDroppedLine"/>.
''' </summary>
Public Module OcrTextFilter

    ''' <summary>Fewest letters in a single-line block.</summary>
    Public Const MinLetters As Integer = 5

    ''' <summary>Fewest letters in a block of two or more lines - the leniency the previous
    ''' filter already granted, kept as it was: several short lines together are a real
    ''' speech balloon far more often than they are noise.</summary>
    Public Const MinLettersMultiLine As Integer = 4

    ''' <summary>Fewest CJK characters. One ideograph on its own is as likely to be a mark on
    ''' the artwork as a word.</summary>
    Public Const MinCjkChars As Integer = 2

    ''' <summary>Fraction of the letter-bearing tokens that must look like words.</summary>
    Public Const MinWordFraction As Double = 0.5

    Private Const LatinVowels As String = "aeiouyAEIOUY"
    Private Const CyrillicVowels As String = "аеёиоуыэюяєіїАЕЁИОУЫЭЮЯЄІЇ"

    ''' <summary>
    ''' The decision, as a yes/no. It is a thin reading of <see cref="RejectionReason"/> and
    ''' never a second copy of the rules: the caller that KEEPS a block and the caller that
    ''' RECORDS why one was dropped (section 16.1) have to be looking at the same condition,
    ''' or the record starts describing a decision nobody takes any more.
    ''' </summary>
    Public Function ShouldTranslate(text As String, lineCount As Integer) As Boolean
        Return RejectionReason(text, lineCount).Length = 0
    End Function

    ''' <summary>
    ''' Empty when the block is worth translating; otherwise the name of the rule that refused
    ''' it. The names are stable and appear in the diagnostics dump, so a scene can be read
    ''' without re-deriving which threshold bit.
    ''' </summary>
    Public Function RejectionReason(text As String, lineCount As Integer) As String
        If String.IsNullOrWhiteSpace(text) Then Return "empty"

        Dim trimmed As String = text.Trim()

        ' CJK first: mixed text (ideographs plus Latin) belongs to this branch, because the
        ' Latin rules below would judge it on the handful of Latin characters it happens to
        ' carry rather than on the sentence.
        Dim cjk As Integer = CountCjk(trimmed)
        If cjk > 0 Then Return If(cjk >= MinCjkChars, "", "cjk-too-short")

        Dim letters As Integer = trimmed.Where(Function(c As Char) Char.IsLetter(c)).Count()
        If letters < If(lineCount >= 2, MinLettersMultiLine, MinLetters) Then Return "too-few-letters"

        ' A whole block that is one address is not language. Recognizing part of one inside a
        ' sentence is fine and stays.
        If IsWholeAddress(trimmed) Then Return "address"

        If RequiresVowels(trimmed) AndAlso Not HasVowel(trimmed) Then Return "no-vowel"

        Dim tokens As List(Of String) = trimmed.
            Split(New Char() {" "c, ControlChars.Tab, ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries).
            Where(Function(t) t.Any(Function(c As Char) Char.IsLetter(c))).ToList()
        If tokens.Count = 0 Then Return "no-word-tokens"

        Dim wordLike As Integer = tokens.Where(Function(t) LooksLikeWord(t)).Count()
        If wordLike < tokens.Count * MinWordFraction Then Return "mostly-non-words"
        Return ""
    End Function

    ''' <summary>
    ''' The engine-level rule, one step earlier: is this recognized LINE worth keeping at all?
    ''' It lives here rather than inside the Tesseract wrapper for the same reason the block
    ''' rule does - one implementation that both keeps and records, and a pure function the
    ''' tests can reach without the native engine.
    '''
    ''' Empty means keep. Note what the rule does NOT do: it never rejects a line for low
    ''' confidence alone once the line carries a real word. The other project measured the cost
    ''' of exactly that leniency and rolled their own version of it back (section 16.1) - it is
    ''' safe on their corpus in the average and fails on one class of input, the page whose
    ''' recognition language was chosen wrongly, where it turns "nothing found" into a
    ''' full-size plate of transliterated nonsense. Ours is deliberately kept as it was, and
    ''' the risk is now written down rather than assumed away.
    ''' </summary>
    ''' <param name="confidence">Engine confidence for the line, 0..1.</param>
    Public Function LineRejection(text As String, confidence As Single) As String
        Dim useful As Integer = CountLettersAndDigits(text)
        If useful = 0 Then Return "no-letters-or-digits"
        ' Mostly punctuation/symbols -> noise.
        If useful / CDbl(Math.Max(1, If(text, "").Length)) < MinUsefulFraction Then Return "mostly-punctuation"
        ' A real text line contains at least one proper word; accept it regardless of
        ' confidence.
        If LongestLetterRun(text) >= MinLetterRun Then Return ""
        ' Otherwise keep only highly-confident short tokens (e.g. "OK", "Да").
        If useful >= 2 AndAlso confidence >= ShortTokenConfidence Then Return ""
        Return "short-low-confidence"
    End Function

    ''' <summary>Fraction of a line's characters that must be letters or digits.</summary>
    Public Const MinUsefulFraction As Double = 0.45

    ''' <summary>Length of the unbroken letter run that makes a line a real word regardless of
    ''' confidence.</summary>
    Public Const MinLetterRun As Integer = 3

    ''' <summary>Confidence a line without such a run has to reach to survive.</summary>
    Public Const ShortTokenConfidence As Single = 0.85F

    ''' <summary>Letters and digits in the text. Public because the attempt scorer measures
    ''' the same thing and a second implementation of it would be free to drift.</summary>
    Public Function CountLettersAndDigits(text As String) As Integer
        Dim count As Integer = 0
        For Each ch As Char In If(text, "")
            If Char.IsLetterOrDigit(ch) Then count += 1
        Next
        Return count
    End Function

    ''' <summary>Longest unbroken run of letters - the measure that tells a real word from
    ''' recognizer noise. Shared with the attempt scorer, same reason.</summary>
    Public Function LongestLetterRun(text As String) As Integer
        Dim best As Integer = 0
        Dim current As Integer = 0
        For Each ch As Char In If(text, "")
            If Char.IsLetter(ch) Then
                current += 1
                If current > best Then best = current
            Else
                current = 0
            End If
        Next
        Return best
    End Function

    ''' <summary>
    ''' Does the vowel rule apply to this text? Only when every letter it carries is Latin or
    ''' Cyrillic. Arabic, Hebrew, Devanagari and the rest do not write vowels, so demanding
    ''' one there rejects perfectly good text - and the rule is meant to catch a run of
    ''' consonants where a Latin or Cyrillic word should have been.
    ''' </summary>
    Private Function RequiresVowels(text As String) As Boolean
        Dim sawLetter As Boolean = False
        For Each ch As Char In text
            If Not Char.IsLetter(ch) Then Continue For
            sawLetter = True
            If Not IsLatinOrCyrillic(ch) Then Return False
        Next
        Return sawLetter
    End Function

    Private Function IsLatinOrCyrillic(ch As Char) As Boolean
        Dim code As Integer = AscW(ch)
        If code <= &H24F Then Return True                          ' Latin + its supplements/extensions
        If code >= &H400 AndAlso code <= &H52F Then Return True     ' Cyrillic + supplement
        Return False
    End Function

    Private Function HasVowel(text As String) As Boolean
        For Each ch As Char In text
            If LatinVowels.IndexOf(ch) >= 0 OrElse CyrillicVowels.IndexOf(ch) >= 0 Then Return True
            ' An accented Latin vowel decomposes to a plain one; treat it as one rather than
            ' listing every diacritic in Europe.
            If AscW(ch) > &H7F AndAlso IsLatinOrCyrillic(ch) Then
                Dim plain As String = ch.ToString().Normalize(NormalizationForm.FormD)
                For Each p As Char In plain
                    If CharUnicodeInfo.GetUnicodeCategory(p) <> UnicodeCategory.NonSpacingMark AndAlso
                       LatinVowels.IndexOf(p) >= 0 Then Return True
                Next
            End If
        Next
        Return False
    End Function

    ''' <summary>
    ''' A token counts as a word when it has at least two letters and, where the vowel rule
    ''' applies at all, one of them is a vowel. Deliberately forgiving about a single misread
    ''' character: the filter is here to drop noise, not to spell-check the recognizer.
    ''' </summary>
    Private Function LooksLikeWord(token As String) As Boolean
        Dim letters As String = New String(token.Where(Function(c As Char) Char.IsLetter(c)).ToArray())
        If letters.Length = 0 Then Return False
        If letters.Length = 1 Then
            ' One-letter words exist ("a", "I", "я", "и") - but only as vowels.
            Return LatinVowels.IndexOf(letters(0)) >= 0 OrElse CyrillicVowels.IndexOf(letters(0)) >= 0
        End If
        If Not RequiresVowels(letters) Then Return True
        Return HasVowel(letters)
    End Function

    Private Function CountCjk(text As String) As Integer
        Dim count As Integer = 0
        For Each ch As Char In text
            Dim code As Integer = AscW(ch)
            If (code >= &H3040 AndAlso code <= &H309F) OrElse     ' Hiragana
               (code >= &H30A0 AndAlso code <= &H30FF) OrElse     ' Katakana
               (code >= &H4E00 AndAlso code <= &H9FFF) OrElse     ' CJK Unified Ideographs
               (code >= &H3400 AndAlso code <= &H4DBF) OrElse     ' CJK Extension A
               (code >= &HAC00 AndAlso code <= &HD7AF) OrElse     ' Hangul Syllables
               (code >= &H1100 AndAlso code <= &H11FF) Then       ' Hangul Jamo
                count += 1
            End If
        Next
        Return count
    End Function

    ''' <summary>Is the whole text one address - a URL, an e-mail, a bare domain or a file
    ''' path? Whole, not "contains": a sentence mentioning a site is still a sentence.</summary>
    Private Function IsWholeAddress(text As String) As Boolean
        If text.Any(Function(c As Char) Char.IsWhiteSpace(c)) Then Return False

        Dim lower As String = text.ToLowerInvariant()

        If lower.StartsWith("http://", StringComparison.Ordinal) OrElse
           lower.StartsWith("https://", StringComparison.Ordinal) OrElse
           lower.StartsWith("ftp://", StringComparison.Ordinal) OrElse
           lower.StartsWith("www.", StringComparison.Ordinal) Then Return True

        ' Windows path (C:\..) or UNC share.
        If lower.StartsWith("\\", StringComparison.Ordinal) Then Return True
        If text.Length >= 3 AndAlso Char.IsLetter(text(0)) AndAlso text(1) = ":"c AndAlso
           (text(2) = "\"c OrElse text(2) = "/"c) Then Return True

        ' E-mail: exactly one @ with something either side, and a dot in the domain.
        Dim at As Integer = lower.IndexOf("@"c)
        If at > 0 AndAlso at < lower.Length - 1 AndAlso lower.IndexOf("@"c, at + 1) < 0 Then
            If lower.IndexOf("."c, at) > at Then Return True
        End If

        ' Bare domain: dotted, no spaces, and the last part is a plausible TLD.
        If lower.IndexOf("."c) > 0 AndAlso Not lower.EndsWith(".", StringComparison.Ordinal) Then
            Dim parts As String() = lower.Split("/"c)(0).Split("."c)
            If parts.Length >= 2 Then
                Dim tld As String = parts(parts.Length - 1)
                If tld.Length >= 2 AndAlso tld.Length <= 6 AndAlso tld.All(Function(c As Char) Char.IsLetter(c)) Then Return True
            End If
        End If

        Return False
    End Function

End Module
