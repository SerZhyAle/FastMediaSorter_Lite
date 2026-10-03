Option Strict On

''' <summary>
''' Judges whether a translator's answer for one segment is usable. Pure: it sees only the
''' source text, the candidate and the target language, which is why it lives outside
''' OllamaTranslator - these are the decisions that throw a translation away, and a rule that
''' throws away correct text has to be held by a test, not found on a comic page.
'''
''' Audit 051 E-03. The refusal test used to be a bare substring search for everyday words -
''' "sorry", "i can't", "не могу", "пожалуйста" - so "Please wait!" -> "Пожалуйста,
''' подождите!" was rejected as a refusal, retried twice, and replaced by the English source.
''' A refusal is now judged by SHAPE: the model talks about the request (translation, the
''' text, JSON, its alphabet) in words the source never used. And an echo is accepted when
''' the source is already in the target script or is a single word - a name or a brand -
''' instead of costing two more generations that would echo it again.
''' </summary>
Friend Module TranslationQuality

    ''' <summary>"" when the candidate is usable, otherwise the reason it is not.</summary>
    Friend Function BadTranslationReason(source As String, candidate As String, targetLang As String) As String
        If String.IsNullOrWhiteSpace(candidate) Then Return "empty"
        If LooksLikeEcho(source, candidate) Then
            ' Nothing else is checked for an accepted echo: it is the source, and the source
            ' can be noisy or in the "wrong" script for reasons no retry changes.
            If EchoIsAcceptable(source, targetLang) Then Return ""
            Return "echo"
        End If
        If LooksWrongScript(candidate, targetLang) Then Return "wrong-script"
        If LooksLikeRefusal(source, candidate) Then Return "refusal"
        If LooksNoisy(candidate) Then Return "noisy"
        If LooksTruncated(source, candidate, targetLang) Then Return "truncated"
        Return ""
    End Function

    Friend Function LooksLikeEcho(source As String, candidate As String) As Boolean
        If String.IsNullOrWhiteSpace(candidate) Then Return False
        Dim a As String = If(source, "").Trim()
        Dim b As String = candidate.Trim()
        If a.Length < 3 Then Return False
        Return String.Equals(a, b, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>
    ''' An echo that no retry will improve: the source is already written in the target's
    ''' script (it probably IS in the target language), or it is one Latin word - a name, a
    ''' brand, a model number - that translates to itself. A single word in any other script
    ''' is not excused: a model that hands back "Привет" for English has failed, and marking
    ''' that as translated would cache the failure.
    ''' </summary>
    Friend Function EchoIsAcceptable(source As String, targetLang As String) As Boolean
        Dim s As String = If(source, "").Trim()
        If s.Length = 0 Then Return True

        Dim dominant As TextScriptFamily = TextScriptFamily.Unknown
        Dim share As Double = 0.0
        Dim letters As Integer = 0
        Dim mixed As Integer = 0
        ' No letters at all (numbers, symbols): nothing to translate.
        If Not TryGetDominantScript(s, dominant, share, letters, mixed) Then Return True

        Dim singleWord As Boolean = s.IndexOfAny({" "c, ControlChars.Tab, ControlChars.Lf, ControlChars.Cr}) < 0
        If singleWord AndAlso dominant = TextScriptFamily.Latin Then Return True

        Dim expected As TextScriptFamily = ExpectedScriptForLanguage(targetLang)
        If expected = TextScriptFamily.Unknown Then Return False
        If dominant <> expected OrElse share < 0.6 Then Return False
        ' One script, several languages: a Ukrainian page echoed for a Russian target is
        ' an untranslated page, not a Russian one, and must stay retryable.
        Return Not HasLettersForeignToTarget(s, targetLang)
    End Function

    ''' <summary>
    ''' Letters that the target language does not use but a neighbour sharing its script does.
    ''' Deliberately small: it only has to stop the common confusions (ru/uk/be/bg in Cyrillic,
    ''' English against any accented Latin language), not identify a language.
    ''' </summary>
    Friend Function HasLettersForeignToTarget(text As String, targetLang As String) As Boolean
        Dim t As String = If(text, "").ToLowerInvariant()
        Dim foreign As String
        Select Case If(targetLang, "").Trim().ToLowerInvariant()
            Case "ru", "rus" : foreign = "іїєґў"
            Case "uk", "ukr" : foreign = "ыэъёў"
            Case "be", "bel" : foreign = "щиїєґъ"
            Case "bg", "bul" : foreign = "ыэёіїєґў"
            Case "zh", "zh-cn", "zh-tw", "chi_sim", "chi-sim", "chi_tra", "chi-tra",
                 "ja", "jpn", "ko", "kor"
                Dim target As String = If(targetLang, "").Trim().ToLowerInvariant()
                For Each ch As Char In t
                    If Not Char.IsLetter(ch) Then Continue For
                    Dim code As Integer = Convert.ToInt32(ch)
                    Dim kana As Boolean = (code >= &H3040 AndAlso code <= &H30FF) OrElse
                                          (code >= &H31F0 AndAlso code <= &H31FF) OrElse
                                          (code >= 65382 AndAlso code <= 65437)
                    Dim hangul As Boolean = (code >= &H1100 AndAlso code <= &H11FF) OrElse
                                            (code >= &H3130 AndAlso code <= &H318F) OrElse
                                            (code >= &HA960 AndAlso code <= &HA97F) OrElse
                                            (code >= &HAC00 AndAlso code <= &HD7AF)
                    If kana AndAlso target <> "ja" AndAlso target <> "jpn" Then Return True
                    If hangul AndAlso target <> "ko" AndAlso target <> "kor" Then Return True
                Next
                ' Han characters alone cannot distinguish Chinese from Japanese.
                Return False
            Case "en", "eng"
                ' Any accented Latin letter says the text is not English.
                For Each ch As Char In t
                    If Char.IsLetter(ch) AndAlso AscW(ch) > &H7F AndAlso AscW(ch) <= &H24F Then Return True
                Next
                Return False
            Case Else : Return False
        End Select
        Return t.IndexOfAny(foreign.ToCharArray()) >= 0
    End Function

    Friend Function LooksWrongScript(text As String, targetLang As String) As Boolean
        If String.IsNullOrWhiteSpace(text) Then Return False
        Dim expected As TextScriptFamily = ExpectedScriptForLanguage(targetLang)
        If expected = TextScriptFamily.Unknown Then Return False

        Dim dominant As TextScriptFamily = TextScriptFamily.Unknown
        Dim share As Double = 0.0
        Dim letters As Integer = 0
        Dim mixedLetters As Integer = 0
        If Not TryGetDominantScript(text, dominant, share, letters, mixedLetters) Then Return False

        If expected = TextScriptFamily.Cjk Then
            Return dominant <> TextScriptFamily.Cjk AndAlso letters >= 2 AndAlso share >= 0.6
        End If

        If dominant = TextScriptFamily.Cjk AndAlso letters >= 2 AndAlso share >= 0.15 Then Return True
        Return letters >= 4 AndAlso dominant <> TextScriptFamily.Unknown AndAlso dominant <> expected AndAlso share >= 0.6
    End Function

    ''' <summary>
    ''' Words a model uses when it talks ABOUT the request instead of answering it. Grouped by
    ''' concept, across languages: a group counts only when the SOURCE mentions none of it, so
    ''' a page that itself says "I can't translate this" still translates to "Я не могу это
    ''' перевести".
    ''' </summary>
    Private ReadOnly MetaConceptGroups As String()() = {
        New String() {"translat", "перевод", "перевест", "переклад", "перекласти"},
        New String() {"json", "output only", "input segment", "the input", "the following text", "this text", "the text you"},
        New String() {"alphabet", "алфавит", "unrecognized symbols", "не распознаю", "language model", "as an ai", "языковая модель"},
        New String() {"язык для перевода", "target language", "source language"}
    }

    ''' <summary>Openers of an inability statement. Each alone is ordinary dialogue; they
    ''' matter only next to a meta concept the source lacks.</summary>
    Private ReadOnly InabilityPhrases As String() = {
        "i cannot", "i can't", "i can not", "i'm unable", "i am unable", "unable to", "i'm sorry", "i am sorry",
        "cannot fulfill", "can't fulfill", "please make sure", "please provide",
        "не могу", "невозможно", "не удаётся", "не удается", "к сожалению", "извините, но"
    }

    ' Ticket 093: recognize source dialogue about translation across languages. These
    ' only suppress a meta hit; they do not introduce new candidate refusal triggers.
    Private ReadOnly SourceTranslationWords As String() = {
        "übersetz", "traduc", "tradu", "traduz", "tradott", "tłumacz", "přelož", "překlad",
        "çevir", "vertaal", "översätt", "翻译", "翻譯", "翻訳", "번역", "अनुवाद", "অনুবাদ"
    }

    ''' <summary>
    ''' A refusal or a meta-answer: the candidate names a concept of the REQUEST that the source
    ''' never mentions, and either says it cannot, or is nothing but that talk. "Please wait!"
    ''' -> "Пожалуйста, подождите!" and "Извини, я не могу" -> "Sorry, I can't" are not
    ''' refusals by this rule; "I'm sorry, but I cannot translate this" is.
    ''' </summary>
    Friend Function LooksLikeRefusal(source As String, candidate As String) As Boolean
        Dim c As String = If(candidate, "").Trim().ToLowerInvariant()
        If c.Length = 0 Then Return False
        Dim s As String = If(source, "").ToLowerInvariant()

        Dim metaHits As Integer = 0
        For Each group As String() In MetaConceptGroups
            Dim inCandidate As Boolean = group.Any(Function(w) c.IndexOf(w, StringComparison.Ordinal) >= 0)
            If Not inCandidate Then Continue For
            Dim inSource As Boolean = group.Any(Function(w) s.IndexOf(w, StringComparison.Ordinal) >= 0)
            If group Is MetaConceptGroups(0) Then
                inSource = inSource OrElse SourceTranslationWords.Any(Function(w) s.IndexOf(w, StringComparison.Ordinal) >= 0)
            End If
            If Not inSource Then metaHits += 1
        Next
        If metaHits = 0 Then Return False

        ' Instruction vocabulary from two different concepts is talk about the request on
        ' its own, with or without an apology ("Output only the JSON translation").
        If metaHits >= 2 Then Return True
        Return InabilityPhrases.Any(Function(p) c.IndexOf(p, StringComparison.Ordinal) >= 0)
    End Function

    Friend Function LooksNoisy(text As String) As Boolean
        Dim symbolNoise As Double = SymbolRatio(text)
        If text.Length >= 10 AndAlso symbolNoise >= 0.22 Then Return True

        Dim digits As Integer = 0
        Dim nonWhitespace As Integer = 0
        For Each ch As Char In text
            If Char.IsWhiteSpace(ch) Then Continue For
            nonWhitespace += 1
            If Char.IsDigit(ch) Then digits += 1
        Next
        If nonWhitespace >= 10 AndAlso digits / CDbl(nonWhitespace) >= 0.35 Then Return True

        Dim dominant As TextScriptFamily = TextScriptFamily.Unknown
        Dim share As Double = 0.0
        Dim letters As Integer = 0
        Dim mixedLetters As Integer = 0
        If TryGetDominantScript(text, dominant, share, letters, mixedLetters) AndAlso
           letters >= 8 AndAlso share < 0.55 Then
            Return True
        End If

        Return False
    End Function

    Friend Function LooksTruncated(source As String, candidate As String, targetLang As String) As Boolean
        Dim sourceUseful As Integer = CountUsefulCharacters(source)
        If sourceUseful < 45 Then Return False

        Dim candidateUseful As Integer = CountUsefulCharacters(candidate)
        Dim minRatio As Double = If(ExpectedScriptForLanguage(targetLang) = TextScriptFamily.Cjk, 0.22, 0.42)

        If sourceUseful >= 120 AndAlso candidateUseful < CInt(Math.Floor(sourceUseful * 0.35)) Then
            Return True
        End If

        Dim sourceSentences As Integer = CountSentenceMarkers(source)
        Dim candidateSentences As Integer = CountSentenceMarkers(candidate)

        If sourceSentences >= 2 AndAlso
           candidateSentences + 1 < sourceSentences AndAlso
           candidateUseful < CInt(Math.Floor(sourceUseful * 0.7)) Then
            Return True
        End If

        If (sourceSentences >= 2 OrElse sourceUseful >= 90) AndAlso
           candidateUseful < Math.Max(18, CInt(Math.Floor(sourceUseful * minRatio))) Then
            Return True
        End If

        Return False
    End Function

    Friend Function CountUsefulCharacters(text As String) As Integer
        Dim count As Integer = 0
        For Each ch As Char In If(text, "")
            If Char.IsLetterOrDigit(ch) Then count += 1
        Next
        Return count
    End Function

    Private Function CountSentenceMarkers(text As String) As Integer
        Dim count As Integer = 0
        For Each ch As Char In If(text, "")
            Select Case Convert.ToInt32(ch)
                Case 46, 33, 63, &H3002, 65281, 65311, &H964, &H6D4, &H61F
                    count += 1
            End Select
        Next
        Return count
    End Function

End Module
