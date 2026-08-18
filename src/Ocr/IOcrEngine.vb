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
