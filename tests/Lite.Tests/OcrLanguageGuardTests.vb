Option Strict On

Imports System.Drawing
Imports Xunit

' OCR-OVERLAY rule 10, "correct plates, or none" (ticket 025 section 5). Both legs: drawing
' transliterated debris over artwork that was readable before is a DEFECT, not a cosmetic
' difference, so the refusal ships in both viewers.
'
' The thing under test is a reading of the discard record rule 12 already obliges the pipeline
' to write - which is why it can be proven here, with no engine, no image and no language pack.
Public Class OcrLanguageGuardTests

    Private Shared Function Block(text As String) As OcrBlock
        Return New OcrBlock With {.SourceText = text, .Box = New Rectangle(10, 10, 200, 40)}
    End Function

    Private Shared Function Refused(text As String, rule As String) As OcrDroppedLine
        Return New OcrDroppedLine With {.Text = text, .Rule = rule, .Confidence = -1.0F}
    End Function

    ''' <summary>Debris of a given length, in letters - what a ladder that cannot read the page
    ''' hands back and the filter then refuses.</summary>
    Private Shared Function Debris(letters As Integer) As OcrDroppedLine
        Return Refused(New String("щ"c, letters), "no-vowel")
    End Function

    ' --- the refusal ----------------------------------------------------------

    <Fact>
    Public Sub AssumedLanguage_WithAlmostEverythingRefused_IsRefused()
        ' The scene the rule exists for: `auto` ran its fixed ladder over a page in a script
        ' none of its members can read, Tesseract answered in the alphabet it was asked about,
        ' and the translatability filter threw nearly all of it away. Reported as "no text
        ' found" this hides the one fact that would let the user fix it.
        Dim kept As New List(Of OcrBlock) From {Block("word")}
        Dim dropped As New List(Of OcrDroppedLine) From {Debris(120)}

        Assert.True(OcrLanguageGuard.ShouldRefuse(languageWasAssumed:=True, kept:=kept, dropped:=dropped))
        Assert.Equal("assumed-language-unreadable",
                     OcrLanguageGuard.RefusalReason(True, kept, dropped))
    End Sub

    <Fact>
    Public Sub AssumedLanguage_WithEverythingRefusedAndNothingKept_IsRefused()
        ' The same failure at its extreme - the page produced no plates at all. Without this
        ' the run falls through to "no text found", which is a DIFFERENT outcome and the one
        ' rule 16 says must stay distinguishable.
        Dim dropped As New List(Of OcrDroppedLine) From {Debris(60), Debris(60)}

        Assert.True(OcrLanguageGuard.ShouldRefuse(True, New List(Of OcrBlock)(), dropped))
    End Sub

    ' --- and everything that must NOT be refused ------------------------------

    <Fact>
    Public Sub ExplicitLanguage_IsNeverRefused()
        ' The user named the language. Second-guessing a stated choice is a different rule from
        ' refusing an assumed one, and this module only has evidence for the second.
        Dim dropped As New List(Of OcrDroppedLine) From {Debris(500)}

        Assert.False(OcrLanguageGuard.ShouldRefuse(languageWasAssumed:=False,
                                                   kept:=New List(Of OcrBlock)(), dropped:=dropped))
    End Sub

    <Fact>
    Public Sub NothingRefused_IsNeverALanguageFailure()
        Dim kept As New List(Of OcrBlock) From {Block("a perfectly ordinary sentence")}

        Assert.False(OcrLanguageGuard.ShouldRefuse(True, kept, New List(Of OcrDroppedLine)()))
        Assert.False(OcrLanguageGuard.ShouldRefuse(True, kept, Nothing))
    End Sub

    <Fact>
    Public Sub ASmallAmountOfNoise_IsNotALanguageFailure()
        ' A nearly blank picture with one speck read as two letters refuses 100 % of what it
        ' read. The floor is what stops that being called a wrong language.
        Dim dropped As New List(Of OcrDroppedLine) From {Refused("щщ", "too-few-letters")}

        Assert.False(OcrLanguageGuard.ShouldRefuse(True, New List(Of OcrBlock)(), dropped))
    End Sub

    <Fact>
    Public Sub APageThatMostlyREAD_IsNotRefused_EvenWithALotOfNoise()
        ' The bias, stated as a test: a page where the ladder clearly worked keeps its overlay
        ' however much texture noise came along beside it. Refusing wrongly costs the user an
        ' overlay; proceeding wrongly costs them the picture - so the thresholds lean this way
        ' and the test holds them there.
        Dim kept As New List(Of OcrBlock) From {
            Block("a long and perfectly readable paragraph of ordinary prose that the engine got right"),
            Block("and a second one just like it, which is what a working page looks like")
        }
        Dim dropped As New List(Of OcrDroppedLine) From {Debris(60)}

        Assert.False(OcrLanguageGuard.ShouldRefuse(True, kept, dropped))
    End Sub

    <Fact>
    Public Sub TheEvidenceIsCountedInLettersAndDigits_NotInLines()
        ' One refused paragraph and one refused stray word are not the same evidence, so the
        ' count is over characters. Both cases below invert what a line count would have said.
        Dim kept As New List(Of OcrBlock) From {Block("short")}

        ' THREE refused lines against one kept block - 75 % by line count - and no refusal,
        ' because 15 refused letters is nothing.
        Dim manyTinyLines As New List(Of OcrDroppedLine) From {Debris(5), Debris(5), Debris(5)}
        Assert.False(OcrLanguageGuard.ShouldRefuse(True, kept, manyTinyLines))

        ' ONE refused line against one kept block - 50 % by line count - and a refusal, because
        ' that line is the whole page.
        Dim oneHugeLine As New List(Of OcrDroppedLine) From {Debris(200)}
        Assert.True(OcrLanguageGuard.ShouldRefuse(True, kept, oneHugeLine))
    End Sub

    <Fact>
    Public Sub TheReasonAndTheBooleanCannotDisagree()
        ' ShouldRefuse is a thin reading of RefusalReason, never a second copy of the rule - the
        ' same discipline OcrTextFilter.ShouldTranslate keeps, and for the same reason: the
        ' caller that acts and the record that explains have to be looking at one condition.
        Dim cases As New List(Of OcrDroppedLine()) From {
            New OcrDroppedLine() {},
            New OcrDroppedLine() {Debris(10)},
            New OcrDroppedLine() {Debris(120)}
        }
        For Each c As OcrDroppedLine() In cases
            For Each assumed As Boolean In New Boolean() {True, False}
                Dim dropped As New List(Of OcrDroppedLine)(c)
                Dim kept As New List(Of OcrBlock) From {Block("word")}
                Assert.Equal(OcrLanguageGuard.RefusalReason(assumed, kept, dropped).Length > 0,
                             OcrLanguageGuard.ShouldRefuse(assumed, kept, dropped))
            Next
        Next
    End Sub

    ' --- the second evidence route: how sure the engine was -------------------
    ' Measured on a real 4384 px English page, 2026-10-04: the Cyrillic model read it into
    ' "КМОСК,КМОСК!" at mean line confidence 0.49-0.56, the English model read it correctly at
    ' 0.883, and the discard record held NOTHING - every debris line was "natural" and passed the
    ' translatability filter.

    <Fact>
    Public Sub AssumedLanguage_ReadWithoutConfidence_IsRefused_EvenWhenNothingWasDropped()
        Dim kept As New List(Of OcrBlock) From {Block("Уез, ме! хй1се Сго#{ тя$е4")}

        Assert.True(OcrLanguageGuard.ShouldRefuse(True, kept, New List(Of OcrDroppedLine)(), 0.52))
        Assert.Equal("assumed-language-low-confidence",
                     OcrLanguageGuard.RefusalReason(True, kept, New List(Of OcrDroppedLine)(), 0.52))
    End Sub

    <Fact>
    Public Sub AssumedLanguage_ReadConfidently_IsNotRefused()
        Dim kept As New List(Of OcrBlock) From {Block("Look your father and I have apologised")}

        Assert.False(OcrLanguageGuard.ShouldRefuse(True, kept, New List(Of OcrDroppedLine)(), 0.883))
        ' The weakest legitimate reading measured (a mixed English/Russian UI screenshot) stays.
        Assert.False(OcrLanguageGuard.ShouldRefuse(True, kept, New List(Of OcrDroppedLine)(), 0.723))
    End Sub

    <Fact>
    Public Sub TheConfidenceFloor_SitsBetweenTheTwoMeasurements()
        ' Rule 13: a constant is set BETWEEN the measurements it was derived from, and the test
        ' holds it there, so moving it past either one is a visible decision.
        Assert.True(OcrLanguageGuard.MinAssumedConfidence > 0.561, "the best WRONG-language reading measured")
        Assert.True(OcrLanguageGuard.MinAssumedConfidence < 0.723, "the weakest legitimate reading measured")
        Assert.True(OcrLanguageGuard.ClearlyReadConfidence > 0.723, "a mixed page must still compare languages")
        Assert.True(OcrLanguageGuard.ClearlyReadConfidence < 0.883, "a clean real page must stop the search")
        Assert.True(OcrLanguageGuard.MinAssumedConfidence < OcrLanguageGuard.ClearlyReadConfidence)
    End Sub

    <Fact>
    Public Sub ExplicitLanguage_IsNeverRefusedForLowConfidence()
        Dim kept As New List(Of OcrBlock) From {Block("blurry but chosen by the user")}

        Assert.False(OcrLanguageGuard.ShouldRefuse(False, kept, New List(Of OcrDroppedLine)(), 0.1))
    End Sub

    <Fact>
    Public Sub ConfidenceNotMeasured_IsNeverRefused()
        ' Nothing is not 0: a result nobody measured has not been read badly.
        Dim kept As New List(Of OcrBlock) From {Block("a perfectly ordinary sentence")}

        Assert.False(OcrLanguageGuard.ShouldRefuse(True, kept, New List(Of OcrDroppedLine)(), Nothing))
    End Sub

    <Fact>
    Public Sub NothingKept_IsNotALowConfidenceRefusal()
        ' An empty page is "no text found", a different outcome (rule 16) - not a language failure.
        Assert.False(OcrLanguageGuard.ShouldRefuse(True, New List(Of OcrBlock)(), New List(Of OcrDroppedLine)(), 0.0))
    End Sub

    <Fact>
    Public Sub IndexOfSurest_PicksTheHighestConfidence_EarlierOnATie()
        ' The three readings of the real page, in ladder order: rus, ukr, eng.
        Assert.Equal(2, OcrLanguageGuard.IndexOfSurest(New List(Of Double) From {0.489, 0.552, 0.883}))
        ' The Russian page: the first language is the right one and stays.
        Assert.Equal(0, OcrLanguageGuard.IndexOfSurest(New List(Of Double) From {0.958, 0.517, 0.517}))
        ' A tie keeps the ladder's order.
        Assert.Equal(0, OcrLanguageGuard.IndexOfSurest(New List(Of Double) From {0.7, 0.7}))
        Assert.Equal(-1, OcrLanguageGuard.IndexOfSurest(New List(Of Double)()))
        Assert.Equal(-1, OcrLanguageGuard.IndexOfSurest(Nothing))
    End Sub

    <Fact>
    Public Sub DegenerateInput_DoesNotThrow()
        Assert.False(OcrLanguageGuard.ShouldRefuse(True, Nothing, Nothing))
        Dim withNulls As New List(Of OcrDroppedLine) From {Nothing, Debris(120)}
        Assert.True(OcrLanguageGuard.ShouldRefuse(True, Nothing, withNulls))
    End Sub

End Class
