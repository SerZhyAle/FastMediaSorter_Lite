Option Strict On

Imports System.Drawing

' Data model for the OCR + Translation overlay feature.
'
' IMPORTANT: every Rectangle stored here is expressed in ORIGINAL image pixels
' (the pixel grid of the file on disk, before any zoom/pan/fit scaling). The
' overlay renderer maps these to picture-box client coordinates at paint time
' via GetZoomedImageRectangle(...) against the current PictureBox.ClientSize.

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

Public Class OcrBlock
    Public Property Lines As New List(Of OcrLine)
    Public Property SourceText As String = ""
    Public Property TranslatedText As String = ""
    Public Property Box As Rectangle
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
End Class
