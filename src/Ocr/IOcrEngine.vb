Option Strict On

Imports System.Drawing

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
    ''' <param name="languages">Tesseract-style code list, e.g. "eng+rus+ukr".</param>
    Function Recognize(source As Bitmap, languages As String) As OcrResult
End Interface
