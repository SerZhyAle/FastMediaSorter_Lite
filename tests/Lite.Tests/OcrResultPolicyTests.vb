Option Strict On

Imports Xunit

' Audit 051 E-01 and E-02: what a result is filed under, and whether it may be filed at all.
' A wrong answer here never looks wrong on screen - a stale hit paints perfectly good plates
' in the wrong place, and a cached failure reads "from cache" for ever - so both are held here.
Public Class OcrResultPolicyTests

    Private Shared Function Key(w As Integer, h As Integer, turns As Integer, Optional exif As Boolean = True) As String
        Return OcrResultPolicy.BuildCacheKey("C:\pics\page.png", 638000000000000000L, w, h, turns, exif,
                                             "tesseract|fast|auto|v7", "ollama|qwen2.5:3b", "auto", "ru")
    End Function

    ' --- E-01: the grid is part of the key -------------------------------------

    <Fact>
    Public Sub TwoGrids_GiveTwoKeys()
        ' A page turned a quarter: same file, same write time, boxes in another grid.
        Assert.NotEqual(Key(1200, 1800, 0), Key(1800, 1200, 1))
    End Sub

    <Fact>
    Public Sub AHalfTurn_WithTheSameSize_GivesAnotherKey()
        ' 180 degrees keeps width and height: only the turn count tells the grids apart.
        Assert.NotEqual(Key(1200, 1800, 0), Key(1200, 1800, 2))
    End Sub

    <Fact>
    Public Sub ExifOrientation_IsPartOfTheKey()
        Assert.NotEqual(Key(1200, 1800, 0, exif:=True), Key(1200, 1800, 0, exif:=False))
    End Sub

    <Fact>
    Public Sub FourQuarterTurns_AreNoTurnAtAll()
        Assert.Equal(Key(1200, 1800, 0), Key(1200, 1800, 4))
        Assert.Equal(Key(1200, 1800, 3), Key(1200, 1800, -1))
    End Sub

    <Fact>
    Public Sub TheSameRun_GivesTheSameKey()
        Assert.Equal(Key(1200, 1800, 1), Key(1200, 1800, 1))
    End Sub

    <Fact>
    Public Sub ADocumentFromAnotherGrid_DoesNotFit()
        Assert.True(OcrResultPolicy.DocumentFitsGrid(1200, 1800, 1200, 1800))
        Assert.False(OcrResultPolicy.DocumentFitsGrid(1200, 1800, 1800, 1200))
        Assert.False(OcrResultPolicy.DocumentFitsGrid(0, 0, 0, 0))
    End Sub

    ' --- E-02: only a complete translation is cached -----------------------------

    Private Shared Function Batch(ParamArray ok As Boolean()) As TranslationBatch
        Dim b As New TranslationBatch()
        For i As Integer = 0 To ok.Length - 1
            b.Add("text " & i.ToString(), ok(i))
        Next
        Return b
    End Function

    <Fact>
    Public Sub AllSegmentsTranslated_IsCompleteAndCacheable()
        Dim outcome As OcrResultPolicy.TranslationOutcome = OcrResultPolicy.Classify(Batch(True, True, True))
        Assert.Equal(OcrResultPolicy.TranslationOutcome.Complete, outcome)
        Assert.True(OcrResultPolicy.IsCacheable(outcome))
    End Sub

    <Fact>
    Public Sub AllSegmentsFailed_IsNotCacheable()
        Dim outcome As OcrResultPolicy.TranslationOutcome = OcrResultPolicy.Classify(Batch(False, False))
        Assert.Equal(OcrResultPolicy.TranslationOutcome.Failed, outcome)
        Assert.False(OcrResultPolicy.IsCacheable(outcome))
    End Sub

    <Fact>
    Public Sub OneSegmentFailed_IsPartialAndNotCacheable()
        ' The owner's call: every segment, not "at least one".
        Dim outcome As OcrResultPolicy.TranslationOutcome = OcrResultPolicy.Classify(Batch(True, False, True))
        Assert.Equal(OcrResultPolicy.TranslationOutcome.Partial, outcome)
        Assert.False(OcrResultPolicy.IsCacheable(outcome))
    End Sub

    <Fact>
    Public Sub ATimedOutBatch_IsNotCacheable()
        ' E-04: a timeout reaches the pipeline as a failed batch, never as a silent cancel.
        Dim b As TranslationBatch = TranslationBatch.AllFailed({"a", "b"}, TranslationFailure.Timeout)
        Assert.Equal(TranslationFailure.Timeout, b.Failure)
        Assert.Equal(2, b.FailedCount)
        Assert.Equal({"a", "b"}, b.Texts.ToArray())
        Assert.False(OcrResultPolicy.IsCacheable(OcrResultPolicy.Classify(b)))
    End Sub

    <Fact>
    Public Sub AnEmptyBatch_IsNotCacheable()
        Assert.False(OcrResultPolicy.IsCacheable(OcrResultPolicy.Classify(New TranslationBatch())))
        Assert.False(OcrResultPolicy.IsCacheable(OcrResultPolicy.Classify(Nothing)))
    End Sub

End Class
