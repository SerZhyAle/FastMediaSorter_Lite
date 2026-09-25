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
    Public Function RefusalReason(languageWasAssumed As Boolean,
                                  kept As List(Of OcrBlock),
                                  dropped As List(Of OcrDroppedLine)) As String
        If Not languageWasAssumed Then Return ""
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

    ''' <summary>The decision as a yes/no. A thin reading of
    ''' <see cref="RefusalReason"/>, never a second copy of the rule.</summary>
    Public Function ShouldRefuse(languageWasAssumed As Boolean,
                                 kept As List(Of OcrBlock),
                                 dropped As List(Of OcrDroppedLine)) As Boolean
        Return RefusalReason(languageWasAssumed, kept, dropped).Length > 0
    End Function

End Module
