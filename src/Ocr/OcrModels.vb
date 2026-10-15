Option Strict On

Imports System.Drawing

' Data model for the OCR + Translation overlay feature.
'
' IMPORTANT: every Rectangle stored here is expressed in ORIGINAL image pixels
' (the pixel grid of the file on disk, before any zoom/pan/fit scaling). The
' overlay renderer maps these to picture-box client coordinates at paint time
' via GetZoomedImageRectangle(...) against the current PictureBox.ClientSize.

''' <summary>
''' Version of everything that runs BETWEEN the raw engine output and the cached
''' document: line clustering, the translatability filter, the sampled plate
''' colours. It rides in the cache key (see Main_Form.OcrTranslate.RunOcrPipeline),
''' because the key otherwise describes only the file, the engine and the
''' languages - so after a clustering change the disk would keep handing back
''' documents built by the old code and the change would look like it never
''' applied.
'''
''' Bump it in EVERY change that alters the CONTENT of an OcrOverlayDocument. A
''' change that only paints the same document differently (the overlay fit ladder)
''' must NOT bump it - the old cache stays valid there. Stale files simply stop
''' being found and are reclaimed by the normal disk-budget trim.
'''
''' One constant for BOTH builds, deliberately not split by "#If": the x86 viewer
''' and the mainline share one cache directory, so a document written by either
''' has to land under the same key. What the x86 leg cannot compute (plate colours)
''' is stored as 0 = "not computed" and filled in on the spot when the mainline
''' reads it.
''' </summary>
Public Module OcrPipeline
    ''' 2 - S1: line clustering by median pitch, type size and the dissolve rule.
    ''' 3 - S3: the translatability filter (vowels, addresses, word-likeness, a CJK branch).
    ''' 4 - S4: plate background/ink sampled from the image.
    ''' 5 - S7: the stitched-line split and the column regrouping in front of the clustering.
    '''     It changes which lines exist and in which order they arrive, so it changes the
    '''     blocks - the one thing this constant is for.
    ''' 6 - ticket 025 section 4: the overlay type size is the median of WORD heights, not of
    '''     line-box heights (`OCR-OVERLAY` rule 5). It looks like a rendering change and is
    '''     not: the same median is what OcrPlateColors.Compute is handed, so the SAMPLED
    '''     plate background and ink - which ARE cached content - move with it.
    ''' 7 - audit 051 (E-02, E-03, E-05, E-10), one bump for four changes to content: only a
    '''     complete translation is cached now; a refusal is judged by shape, so correct
    '''     translations containing "пожалуйста" or "sorry" stop being replaced by the source;
    '''     a tall strip is no longer shrunk by its long edge; and the curly quotes, the
    '''     ellipsis and the dashes count as punctuation in attempt scoring. It also retires
    '''     every document a failed translation had already written to disk as a success.
    '''     (The E-01 key change - the displayed grid - changes no content and needs none.)
    ''' 8, 9 - ticket 093 (audit 057.S12-1/-2/-3/-5/-15): a correct Hindi, Bengali, Chinese or
    '''     Japanese translation is no longer rejected as noisy or truncated, a Japanese echo for
    '''     a Chinese target is no longer accepted, and the attempt scorer stops calling every
    '''     Indic line suspicious - so both the kept translation and the winning OCR attempt change.
    '''     Two builds of this ticket wrote documents (one as 8, one as 9); 9 retires both.
    ''' 10 - 2026-10-04: with the language on `auto` the engine now compares the languages by mean
    '''     line confidence instead of taking the first "strong" reading, so an English page is no
    '''     longer read by the Russian model into Cyrillic debris. The recognized text of an
    '''     existing document changes, and a cached wrong-language document must not be served again.
    ''' 11 - 2026-10-13, ticket 183, `OCR-PIPELINE` 1.7 A and 1.8 A/B, one bump for two changes to
    '''     content: the line split also cuts before a word taller than 1.6 median word heights (a
    '''     balloon outline read as a token), so which lines exist and what a plate holds change; and
    '''     a line's and a plate's text carries no space where words meet on Han, kana or CJK
    '''     punctuation (Hangul only under a gap bound), so every CJK plate's source text - which is
    '''     what the translator is sent and what is cached - changes. Nothing that is DECIDED reads
    '''     the joined text (<see cref="OcrLine.SpacedText"/>); only what is drawn and translated.
    Public Const OcrPipelineVersion As Integer = 11
End Module

Public Class OcrWord
    Public Property Text As String = ""
    Public Property Box As Rectangle
    Public Property Confidence As Single
End Class

Public Class OcrLine
    ''' <summary>The line's own words, in the engine's order, with their own boxes and
    ''' confidences. Real since S7 (section 18) - it used to hold ONE stub word equal to the
    ''' whole line, which is why the type-size rule of 16.2/16.3 could not be written.</summary>
    Public Property Words As New List(Of OcrWord)
    Public Property Text As String = ""
    Public Property Box As Rectangle

    ''' <summary>
    ''' Engine confidence for the line, 0..1.
    '''
    ''' Carried on the line rather than derived from <see cref="Words"/> on purpose. The
    ''' attempt scorer averages a confidence to pick the LANGUAGE, and while Words held one
    ''' stub per line that average was the mean of line confidences; going to real words would
    ''' have silently changed it to a mean over words - a different number, and therefore a
    ''' different language on some images - as a side effect of a geometry change. Keeping the
    ''' value the scorer reads here makes that change provably nil (section 18.4).
    '''
    ''' For a run produced by <see cref="OcrLineSplitter"/> it is the mean confidence of that
    ''' run's OWN words, never the stitched line's.
    ''' </summary>
    Public Property Confidence As Single

    ''' <summary>
    ''' The line as the recognizer's own words with ONE SPACE between every two - the reading every
    ''' threshold of this pipeline was measured on. <see cref="Text"/> is for people and for the
    ''' translator, and since `OCR-PIPELINE` amendment 1.8 it carries no space where words meet on
    ''' CJK text (<see cref="OcrTextJoin"/>); a decision that read that text would move with a
    ''' cosmetic change - a joined Japanese line is one unbroken letter run where the spaced one was
    ''' many short ones, which is exactly how the reference's rescue admission changed its verdicts
    ''' before it learned to read the words (1.8 F). So the line filter and the attempt scorer read
    ''' this instead, and the join alters what is DRAWN and TRANSLATED and nothing that is DECIDED.
    ''' A line with no words has nothing to rebuild from and is its own text.
    ''' </summary>
    Public ReadOnly Property SpacedText As String
        Get
            If Words Is Nothing OrElse Words.Count = 0 Then Return Text
            Return String.Join(" ", Words.Select(Function(w) If(w Is Nothing, "", w.Text))).Trim()
        End Get
    End Property
End Class

''' <summary>
''' One thing a threshold refused, and which threshold refused it
''' (SPECIFICATION_OCR_OVERLAY_ACCURACY.md, section 16.1).
'''
''' The question the acceptance run could not answer was never "are plates found" - it was
''' "what did the filter throw away that the previous code kept". Comparing two builds answers
''' that once and goes stale the same day, so the threshold records its own refusals instead,
''' through the SAME predicate that applies them: two copies of one condition would pass any
''' review of the constants and drift apart the first time one of them changed, after which the
''' record would describe a decision that is no longer being taken.
'''
''' It is inert on purpose. Nothing here is rendered, and the attempt score is still computed
''' from the lines that were KEPT, so recording a refusal cannot change which pass the reader
''' ends up seeing. It rides with the winning attempt rather than being merged across all of
''' them - a union describes no single decision.
''' </summary>
Public Class OcrDroppedLine
    ''' <summary>Text as the recognizer read it, after cleanup - the thing that was lost.</summary>
    Public Property Text As String = ""

    ''' <summary>Where it sat, in original image pixels. Zero-sized for a block-level refusal
    ''' whose box was never assembled.</summary>
    Public Property Box As Rectangle

    ''' <summary>Engine confidence, 0..1, or a negative value when the refusal happened after
    ''' the engine (the block filter has no confidence of its own).</summary>
    Public Property Confidence As Single = -1.0F

    ''' <summary>Name of the rule that refused it - "short-low-confidence", "vowel",
    ''' "address", .. - so a scene can be read without re-deriving which threshold bit.</summary>
    Public Property Rule As String = ""
End Class

Public Class OcrBlock
    Public Property Lines As New List(Of OcrLine)
    Public Property SourceText As String = ""
    Public Property TranslatedText As String = ""
    Public Property Box As Rectangle

    ' --- render state ---------------------------------------------------------
    ' Filled by the overlay painter on every frame and deliberately NOT part of the
    ' cached document: it describes how this block was last drawn, not what was
    ' recognized. The diagnostics dump reads it so that turning diagnostics on
    ' reports the values the renderer actually used instead of recomputing its own.

    ''' <summary>Height of the plate after the growth rung of the fit ladder, back in
    ''' original image pixels. Equals <c>Box.Height</c> when it did not have to grow.</summary>
    Public Property RenderPlateHeight As Integer

    ''' <summary>Font size the ladder settled on, in display pixels.</summary>
    Public Property RenderFontPx As Single

    ''' <summary>The translation still did not fit after shrinking to the floor AND growing
    ''' the plate as far as its budget allowed, so the plate was RELEASED: it grew past its
    ''' budget, bounded only by the bottom of the displayed image, and may overlap the plate
    ''' below it. Rare by design - a tiny source box with another block right below it - and
    ''' recorded so the frequency can be measured rather than guessed at.
    '''
    ''' It replaced a field named <c>Truncated</c>, which recorded the same condition when the
    ''' answer to it was an ellipsis. `OCR-OVERLAY` rule 9 permits no ellipsis, so nothing is
    ''' cut any more and the old name could only ever have been False.</summary>
    Public Property Released As Boolean

    ' --- sampled colours ------------------------------------------------------
    ' ARGB, and 0 means "not computed" - NOT black. Only the mainline samples them; the
    ' x86 fallback leaves them at 0 and its plates keep the old constant near-white.
    '
    ' Declared UNCONDITIONALLY in both builds even though only one fills them, and that is
    ' load-bearing rather than tidy: the two exes share one disk cache directory and one
    ' cache key, so a document written by either has to deserialize in the other. Splitting
    ' the fields by "#If" would have one exe writing, under a key the other also uses, JSON
    ' whose shape that one does not know - leaving correctness to how forgiving each leg's
    ' serializer happens to be (JavaScriptSerializer on net48, System.Text.Json on modern).

    ''' <summary>Sampled plate background. 0 = not computed.</summary>
    Public Property PlateBackgroundArgb As Integer

    ''' <summary>Sampled text colour. 0 = not computed.</summary>
    Public Property PlateInkArgb As Integer
End Class

''' <summary>
''' One cached OCR + translation result for a single image file. Coordinates are
''' in original image pixels; <see cref="ImageSize"/> records the pixel grid they
''' were computed against so the renderer can rescale to any display size.
''' </summary>
Public Class OcrOverlayDocument
    Public Property FilePath As String = ""
    Public Property FileWriteTicks As Long
    Public Property ImageSize As Size
    Public Property SourceLanguage As String = ""
    Public Property TargetLanguage As String = ""
    Public Property Engine As String = ""
    Public Property Translator As String = ""
    Public Property Blocks As New List(Of OcrBlock)

    ''' <summary>
    ''' What the thresholds refused on the run that produced this document, or Nothing when
    ''' nobody measured it. The two are DIFFERENT values and must not collapse into one: an
    ''' empty list means "measured, nothing was dropped", Nothing means "this document came
    ''' back from the cache, so the refusals of the run that built it are not here". Reporting
    ''' the second as the first is the same flattering-zero mistake the acceptance harness made
    ''' with hidden ink (section 16.5).
    '''
    ''' Deliberately NOT part of the cache DTO (<see cref="OcrCacheDoc"/>): it describes a run,
    ''' not the image, and a document read from disk honestly has none.
    ''' </summary>
    Public Property Dropped As List(Of OcrDroppedLine) = Nothing

    ''' <summary>
    ''' True when this document is shown but was NOT cached, because its translation is
    ''' incomplete - the translator was unreachable, timed out, refused the language or
    ''' handed some segments back untranslated. T then runs the pipeline again instead of
    ''' toggling the plates (audit 051 E-02). Not part of the cache DTO for the same reason
    ''' as <see cref="Dropped"/>: only a complete document is ever written.
    ''' </summary>
    Public Property IsPartial As Boolean = False
End Class
