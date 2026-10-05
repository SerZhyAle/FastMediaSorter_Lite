Option Strict On

''' <summary>
''' `OCR-OVERLAY` rule 10, "correct plates, or none": when the recognition language is wrong,
''' unavailable or merely ASSUMED, the product draws no overlay and says why, naming what would
''' fix it. Transliterated debris over artwork that was readable before is output strictly worse
''' than the input, and it is the failure a default language setting produces silently.
'''
''' What this module decides, and why it can decide it without the engine
''' ---------------------------------------------------------------------
''' With an explicit source language the user made the choice and this module stands aside.
''' With `auto` the language is assumed - a fixed ladder (`OcrTranslateSettings.OcrAttemptCodes`)
''' scored against itself - and a page in a script none of its members can read still comes back
''' full of text, because Tesseract answers in the alphabet it was asked about whatever the page
''' really says. So the failure is NOT an empty result. It is a result that the translatability
''' filter then refuses almost all of.
'''
''' That makes the discard record of rule 12 the instrument, which is exactly what the contract's
''' section 6 says it is for: "everything else is measured with it". The decision here is a
''' reading of that record and of nothing else - no second engine pass, no second download, and
''' no copy of the filter's conditions (the numbers it compares were produced by the filter's own
''' predicate, through `OcrBlockBuilder`).
'''
''' Why not Tesseract's `osd.traineddata` - the decision, so it is not re-opened
''' ---------------------------------------------------------------------------
''' OSD is the usual instrument and `Tesseract.Page.DetectBestOrientationAndScript` does exist in
''' the wrapper this product uses, so the option was real. It is NOT taken, for three reasons in
''' descending weight:
'''   1. OSD needs the legacy engine model. This product builds its engine with
'''      `EngineMode.LstmOnly`; `PageSegMode.OsdOnly` under that mode fails outright. Adopting OSD
'''      therefore means changing the recognition engine mode or standing up a second engine -
'''      a change to the recognition path itself, which is a larger step than the refusal this
'''      ticket asks for and would have to be measured against the current results first.
'''   2. It answers a slightly different question. OSD reports the script of the DOMINANT text;
'''      what rule 10 is about is whether the ladder could read the page at all, which the record
'''      above reports directly.
'''   3. It costs a further download (`osd.traineddata`, comparable in size to a language pack) on
'''      a path that already makes the user wait for one.
''' If the refusal below ever proves too blunt on real material, OSD is the named next step and
''' the engine-mode question is the first thing it has to answer.
'''
''' Constant status (`OCR-OVERLAY` rule 13)
''' | Constant | Status | Source |
''' | --- | --- | --- |
''' | <see cref="MinRefusedChars"/> | chosen here, NOT derived | a floor that keeps a nearly-blank picture from being called a language failure. No corpus behind it. Ticket 033 owns deriving it (it replaced the closed ticket 025). |
''' | <see cref="MinRefusedFraction"/> | chosen here, NOT derived | the share of what was read that has to have been refused before the assumption is called wrong. Set high on purpose - see the bias note below. Ticket 033. |
''' | <see cref="MinAssumedConfidence"/> | policy | 0.64: the geometric middle of the two measurements it sits between - the highest mean line confidence a WRONG language gave (0.561, Cyrillic model on an English page; 0.517 the other way round) and the lowest a legitimate reading gave (0.723, a mixed English/Russian UI screenshot). Six wrong-language readings and four legitimate pages is thin; it is chosen, not derived, and a measurement over more pages replaces it. |
''' | <see cref="ClearlyReadConfidence"/> | policy | 0.80: a reading this sure ends the language search at once, so a page one language reads well costs one pass. Between the best wrong-language reading (0.561) and the weakest clean one (0.883, a real 4384 px page); a mixed page at 0.72-0.78 deliberately stays below it and is compared against the other languages. Chosen, not derived. |
'''
''' Second evidence route: how sure the engine was (2026-10-04)
''' ------------------------------------------------------------
''' The discard record above is blind to one case, and it is the commonest one: an English page
''' read by the Russian model comes back as 20 "natural" lines of letters, none of which the
''' translatability filter refuses, so nothing is in the record and the debris is drawn - and then
''' handed to the translator, which invents a story around it. Found on a real page: the Cyrillic
''' model gave 0.51-0.56 mean line confidence, the English model 0.88 on the same pixels, and the
''' engine's own score ranked the Cyrillic reading higher because the score counts lines and
''' letters and weighs confidence at about 40 points in thousands.
'''
''' So the language comparison moved onto confidence (TesseractOcrEngine, assumed language: every
''' language reads the page once on the same profile, the surest wins), and the same number is the
''' second thing this module reads: when the language was assumed and even the best reading was
''' not confident, nothing is drawn. Same stance as above - an explicit language is never touched.
'''
''' The bias is deliberate and stated rather than discovered: refusing wrongly costs the user an
''' overlay they could have had, and a picture that is exactly as readable as it was before;
''' proceeding wrongly costs them the picture, covered in nonsense. The thresholds are set so the
''' first mistake is the likely one.
''' </summary>
Public Module OcrLanguageGuard

    ''' <summary>Fewest refused letters and digits before the run is even considered a language
    ''' failure. CHOSEN HERE, not derived - see the constant status table.</summary>
    Public Const MinRefusedChars As Integer = 40

    ''' <summary>Share of everything the engine read that must have been refused. CHOSEN HERE,
    ''' not derived.</summary>
    Public Const MinRefusedFraction As Double = 0.75

    ''' <summary>Mean line confidence below which an ASSUMED language's best reading is not
    ''' trusted and nothing is drawn. Policy, see the constant status table.</summary>
    Public Const MinAssumedConfidence As Double = 0.64

    ''' <summary>Mean line confidence at which one language's reading of the page is accepted
    ''' without asking the other languages. Policy, see the constant status table.</summary>
    Public Const ClearlyReadConfidence As Double = 0.8

    ''' <summary>
    ''' Blank when the run may be shown, otherwise the name of the rule that refused it - the
    ''' same shape <see cref="OcrTextFilter.RejectionReason"/> uses, so a refusal can be recorded
    ''' by the call that decides it rather than by a second copy of the condition.
    ''' </summary>
    ''' <param name="languageWasAssumed">The source language was `auto`. With an explicit choice
    ''' this module always returns blank: the user named the language, and second-guessing a
    ''' stated choice is a different rule from refusing an assumed one.</param>
    ''' <param name="kept">Blocks that survived the translatability filter - what would be
    ''' painted.</param>
    ''' <param name="dropped">What the thresholds refused on this run (rule 12's record). Nothing
    ''' or empty means nothing was refused, which is never a language failure.</param>
    ''' <param name="meanConfidence">The winning pass's mean line confidence, or Nothing when it
    ''' was not measured. Nothing never refuses: a result nobody measured is not a result somebody
    ''' read badly.</param>
    Public Function RefusalReason(languageWasAssumed As Boolean,
                                  kept As List(Of OcrBlock),
                                  dropped As List(Of OcrDroppedLine),
                                  Optional meanConfidence As Double? = Nothing) As String
        If Not languageWasAssumed Then Return ""
        If meanConfidence.HasValue AndAlso meanConfidence.Value < MinAssumedConfidence AndAlso
           kept IsNot Nothing AndAlso kept.Count > 0 Then
            Return "assumed-language-low-confidence"
        End If
        If dropped Is Nothing OrElse dropped.Count = 0 Then Return ""

        Dim refused As Integer = 0
        For Each d As OcrDroppedLine In dropped
            If d Is Nothing Then Continue For
            refused += OcrTextFilter.CountLettersAndDigits(d.Text)
        Next
        If refused < MinRefusedChars Then Return ""

        Dim survived As Integer = 0
        If kept IsNot Nothing Then
            For Each b As OcrBlock In kept
                If b Is Nothing Then Continue For
                survived += OcrTextFilter.CountLettersAndDigits(b.SourceText)
            Next
        End If

        Dim total As Integer = refused + survived
        If total <= 0 Then Return ""
        If refused / CDbl(total) < MinRefusedFraction Then Return ""

        Return "assumed-language-unreadable"
    End Function

    ''' <summary>Which of several languages' readings of the SAME page the engine was surest of:
    ''' the index of the highest mean line confidence, the earlier one on a tie (the ladder's own
    ''' order), -1 for none. Confidence and not the engine's score, because the score counts
    ''' lines and letters and a wrong-language reading has as many of both as a right one.</summary>
    Public Function IndexOfSurest(confidences As IList(Of Double)) As Integer
        Dim top As Integer = -1
        If confidences Is Nothing Then Return top
        For i As Integer = 0 To confidences.Count - 1
            If top < 0 OrElse confidences(i) > confidences(top) Then top = i
        Next
        Return top
    End Function

    ''' <summary>The decision as a yes/no. A thin reading of
    ''' <see cref="RefusalReason"/>, never a second copy of the rule.</summary>
    Public Function ShouldRefuse(languageWasAssumed As Boolean,
                                 kept As List(Of OcrBlock),
                                 dropped As List(Of OcrDroppedLine),
                                 Optional meanConfidence As Double? = Nothing) As Boolean
        Return RefusalReason(languageWasAssumed, kept, dropped, meanConfidence).Length > 0
    End Function

End Module
