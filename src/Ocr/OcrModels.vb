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
    Public Const OcrPipelineVersion As Integer = 4
End Module

Public Class OcrWord
    Public Property Text As String = ""
    Public Property Box As Rectangle
    Public Property Confidence As Single
End Class

Public Class OcrLine
    Public Property Words As New List(Of OcrWord)
    Public Property Text As String = ""
    Public Property Box As Rectangle
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
    ''' the plate as far as its budget allowed, so its tail was trimmed. Rare by design -
    ''' a tiny source box with another block right below it - but no longer silent.</summary>
    Public Property Truncated As Boolean

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
End Class
