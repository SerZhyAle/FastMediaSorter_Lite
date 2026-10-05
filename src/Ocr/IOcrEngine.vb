Option Strict On

Imports System.Drawing
Imports System.Threading

Public Enum OcrStatus
    Ok
    NoText
    RuntimeMissing
    Failed
End Enum

''' <summary>Outcome of one OCR pass. Lines are in original-image pixels.</summary>
Public Class OcrResult
    Public Property Status As OcrStatus = OcrStatus.Failed
    Public Property Lines As New List(Of OcrLine)
    Public Property Message As String = ""

    ''' <summary>Lines this pass recognized and then refused, with the rule that refused each.
    ''' Carried per attempt rather than merged across all of them: the engine returns ONE
    ''' attempt's result, so the record travels with the pass that won and describes the
    ''' decisions actually taken on the text the reader is looking at.</summary>
    Public Property Dropped As New List(Of OcrDroppedLine)

    ''' <summary>
    ''' True when <see cref="OcrLineSplitter"/> cut a stitched line on this pass and therefore
    ''' put <see cref="Lines"/> into ITS column order rather than the engine's. The clustering
    ''' must then keep that order instead of re-sorting top-then-left, which is exactly the
    ''' sort that interleaves two columns (see OcrBlockBuilder.BuildBlocks, preserveOrder).
    '''
    ''' False on a page with no stitch, and then the list is the engine's own, untouched.
    ''' </summary>
    Public Property LinesRegrouped As Boolean

    ''' <summary>
    ''' Mean line confidence (0..1) of the pass that produced <see cref="Lines"/>, in the
    ''' engine's own words. Nothing when no pass measured it - an empty or failed result - which
    ''' is NOT the same as 0: "not measured" must never read as "read badly" (and the other way
    ''' round). It is what tells a wrong-language reading from a right one, because the score
    ''' the engine ranks passes by cannot (see <see cref="OcrLanguageGuard"/>).
    ''' </summary>
    Public Property MeanConfidence As Double? = Nothing

    Public Shared Function Runtime(message As String) As OcrResult
        Return New OcrResult With {.Status = OcrStatus.RuntimeMissing, .Message = message}
    End Function

    Public Shared Function FromError(message As String) As OcrResult
        Return New OcrResult With {.Status = OcrStatus.Failed, .Message = message}
    End Function
End Class

''' <summary>
''' Pluggable OCR backend. v1 ships <see cref="TesseractOcrEngine"/>; the
''' interface keeps Windows.Media.Ocr / PaddleOCR as drop-in future options.
''' Implementations must be safe to call from a background thread (the pipeline
''' serializes calls), must never throw for missing runtime/data, and must map
''' all coordinates back to original-image pixels.
''' </summary>
Public Interface IOcrEngine
    ReadOnly Property Name As String

    ''' <param name="source">A private bitmap snapshot the engine may read freely.</param>
    ''' <param name="languages">Normalized source hint such as "auto", "rus" or "eng".</param>
    ''' <param name="ct">
    ''' Checked between attempts and before any language-data download. Passing the token to
    ''' Task.Run only ever stopped a job that had not STARTED; once inside, a run does up to
    ''' ~18 Tesseract passes (seconds each) plus a possible 60 s download per language, and a
    ''' "cancelled" job used to burn all of it - while holding the engine lock that the newest
    ''' job was queued on. During auto-OCR over a slideshow that stacked up threads until the
    ''' whole app went sluggish.
    ''' </param>
    Function Recognize(source As Bitmap, languages As String, ct As CancellationToken) As OcrResult
End Interface
