Option Strict On

Imports System.Globalization

''' <summary>
''' The two decisions around the OCR result cache, kept pure so a test can hold them:
''' what a result is filed under, and whether it may be filed at all.
''' </summary>
Friend Module OcrResultPolicy

    ''' <summary>
    ''' The cache key. It names everything that changes the ANSWER - the file, the engine and
    ''' translator identities, the languages - and, since audit 051 E-01, the pixel grid the
    ''' boxes are measured in: the displayed size, the quarter turns applied on screen since
    ''' the picture was shown, and whether EXIF orientation was applied at load. Without the
    ''' grid a page OCR'd sideways and then turned with R was served the sideways document
    ''' (and the other way round), because the file, and so the key, had not changed.
    '''
    ''' Two orientations with the same width and height (a 180-degree turn) differ only in
    ''' <paramref name="quarterTurns"/>, which is why the turn count is in the key and not
    ''' just the size. The format changed with the grid, so every older entry is simply never
    ''' found again and the disk-budget trim reclaims it; no document content changed, so no
    ''' OcrPipelineVersion bump for this part.
    ''' </summary>
    Friend Function BuildCacheKey(filePath As String, fileWriteTicks As Long,
                                  gridWidth As Integer, gridHeight As Integer,
                                  quarterTurns As Integer, exifAutoRotate As Boolean,
                                  ocrEngine As String, translator As String,
                                  srcLang As String, dstLang As String) As String
        Dim turns As Integer = ((quarterTurns Mod 4) + 4) Mod 4
        Dim grid As String = gridWidth.ToString(CultureInfo.InvariantCulture) & "x" &
                             gridHeight.ToString(CultureInfo.InvariantCulture) &
                             "@r" & turns.ToString(CultureInfo.InvariantCulture) &
                             If(exifAutoRotate, "e", "n")
        Return String.Join("|", filePath, fileWriteTicks.ToString(CultureInfo.InvariantCulture), grid,
                           ocrEngine, translator, srcLang, dstLang)
    End Function

    ''' <summary>
    ''' True when a cached document can stand in for a fresh run on this picture: its boxes
    ''' are in the grid on screen. The key already says so for every document written from
    ''' now on; this is the second check for one that reaches the key some other way.
    ''' </summary>
    Friend Function DocumentFitsGrid(documentWidth As Integer, documentHeight As Integer,
                                     gridWidth As Integer, gridHeight As Integer) As Boolean
        Return documentWidth = gridWidth AndAlso documentHeight = gridHeight AndAlso gridWidth > 0 AndAlso gridHeight > 0
    End Function

    ''' <summary>How far a translation got.</summary>
    Friend Enum TranslationOutcome
        ''' <summary>Every segment is a real translation.</summary>
        Complete
        ''' <summary>Some segments are the source text handed back.</summary>
        [Partial]
        ''' <summary>No segment is a translation.</summary>
        Failed
    End Enum

    Friend Function Classify(batch As TranslationBatch) As TranslationOutcome
        If batch Is Nothing OrElse batch.Translated.Count = 0 Then Return TranslationOutcome.Failed
        Dim failed As Integer = batch.FailedCount
        If failed = 0 AndAlso batch.Failure = TranslationFailure.None Then Return TranslationOutcome.Complete
        If failed >= batch.Translated.Count Then Return TranslationOutcome.Failed
        Return TranslationOutcome.Partial
    End Function

    ''' <summary>
    ''' Only a complete translation is cached (audit 051 E-02). A partial or failed one is
    ''' shown - the recognized text is still worth seeing - but kept out of both tiers, so the
    ''' next T runs it again instead of serving "the source, translated" from the cache for
    ''' ever. The owner's call recorded in the ticket: all segments, not "at least one".
    ''' </summary>
    Friend Function IsCacheable(outcome As TranslationOutcome) As Boolean
        Return outcome = TranslationOutcome.Complete
    End Function

End Module
