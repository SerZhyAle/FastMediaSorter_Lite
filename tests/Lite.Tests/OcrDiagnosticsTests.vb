#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports Xunit

' Ticket 033 Ф-8: the two OCR-OVERLAY rules the diagnostics dump is the evidence for.
'   rule 14 - "not measured" and "nothing" are different values: a document whose refusals
'             nobody measured says null, one that measured and dropped nothing says [].
'   rule 17 - diagnostics change nothing: building the line reads the document and writes
'             nothing back, so a run with the dump on paints what a run with it off paints.
' Modern-only, like OcrDiagnostics itself (ticket 033 Ф-5 records why the x86 exe has none).
Public Class OcrDiagnosticsTests

    Private Shared Function Doc(dropped As List(Of OcrDroppedLine)) As OcrOverlayDocument
        Dim block As New OcrBlock With {
            .SourceText = "Hello",
            .TranslatedText = "Привет",
            .Box = New Rectangle(10, 20, 200, 40),
            .RenderFontPx = 18.5F,
            .RenderPlateHeight = 44,
            .Released = True,
            .PlateBackgroundArgb = Color.FromArgb(255, 240, 230, 200).ToArgb(),
            .PlateInkArgb = Color.Black.ToArgb()
        }
        block.Lines.Add(New OcrLine With {.Text = "Hello", .Box = New Rectangle(10, 20, 200, 40), .Confidence = 0.9F})
        Return New OcrOverlayDocument With {
            .FilePath = "C:\pics\page.png",
            .ImageSize = New Size(800, 600),
            .Blocks = New List(Of OcrBlock) From {block},
            .Dropped = dropped
        }
    End Function

    ' --- rule 14 ---------------------------------------------------------------

    <Fact>
    Public Sub NotMeasured_IsNull()
        Assert.Contains("""dropped"":null", OcrDiagnostics.BuildDumpLine(Doc(Nothing)))
    End Sub

    <Fact>
    Public Sub MeasuredAndNothingDropped_IsAnEmptyArray()
        Assert.Contains("""dropped"":[]", OcrDiagnostics.BuildDumpLine(Doc(New List(Of OcrDroppedLine)())))
    End Sub

    <Fact>
    Public Sub ADroppedLine_IsReportedWithItsRule()
        Dim dropped As New List(Of OcrDroppedLine) From {
            New OcrDroppedLine With {.Text = "~~", .Box = New Rectangle(1, 2, 3, 4), .Confidence = -1.0F, .Rule = "no-vowel"}}
        Dim line As String = OcrDiagnostics.BuildDumpLine(Doc(dropped))
        Assert.Contains("""rule"":""no-vowel""", line)
        Assert.Contains("""conf"":null", line)
    End Sub

    <Fact>
    Public Sub TheReleasedFlag_IsReportedUnderItsCurrentName()
        ' The scene harness reads "released" (033 Ф-2); "truncated" was retired with the ellipsis.
        Dim line As String = OcrDiagnostics.BuildDumpLine(Doc(Nothing))
        Assert.Contains("""released"":true", line)
        Assert.DoesNotContain("""truncated""", line)
    End Sub

    ' --- rule 17 ---------------------------------------------------------------

    <Fact>
    Public Sub BuildingTheLine_ChangesNothingTheRendererReads()
        Dim d As OcrOverlayDocument = Doc(New List(Of OcrDroppedLine)())
        Dim b As OcrBlock = d.Blocks(0)
        Dim before As String = Snapshot(d)

        Dim first As String = OcrDiagnostics.BuildDumpLine(d)
        Dim second As String = OcrDiagnostics.BuildDumpLine(d)

        Assert.Equal(before, Snapshot(d))
        Assert.Equal(first, second)
        Assert.Equal(18.5F, b.RenderFontPx)
        Assert.Equal(44, b.RenderPlateHeight)
    End Sub

    ''' <summary>Every field of the document the renderer or the cache reads, as one string.</summary>
    Private Shared Function Snapshot(d As OcrOverlayDocument) As String
        Dim parts As New List(Of String) From {d.FilePath, d.ImageSize.ToString(), d.Blocks.Count.ToString(),
                                                If(d.Dropped Is Nothing, "null", d.Dropped.Count.ToString()), d.IsPartial.ToString()}
        For Each b As OcrBlock In d.Blocks
            parts.Add(String.Join("|", b.SourceText, b.TranslatedText, b.Box.ToString(), b.RenderFontPx.ToString(),
                                  b.RenderPlateHeight.ToString(), b.Released.ToString(), b.PlateBackgroundArgb.ToString(),
                                  b.PlateInkArgb.ToString(), b.Lines.Count.ToString()))
        Next
        Return String.Join(";", parts)
    End Function

End Class
#End If
