#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' Which decodes are worth a file on disk
''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §6.1).
'''
''' Pure, and therefore testable without a disk - which is the point: every rule here is a
''' trade of megabytes for milliseconds, and the failure mode of getting one wrong is
''' silent (a cache that fills up with entries nobody was ever going to wait for).
''' </summary>
Friend Module DecodeCachePolicy

    ''' <summary>
    ''' How slow a STILL image has to have been before its payload is kept.
    '''
    ''' Chosen against the loading badge's own threshold (loading_Badge_Delay_Ms = 250 ms):
    ''' anything the user was actually told about is comfortably above this, and a fast
    ''' WEBP that decodes in 30 ms never consumes an entry. Animations do not consult it -
    ''' they are the case that costs seconds.
    ''' </summary>
    Friend Const Decode_Cache_Min_Ms As Long = 400

    ''' <summary>Hard ceiling on one entry, whatever the budget is.</summary>
    Friend Const Absolute_Max_Entry_Bytes As Long = 64L * 1024L * 1024L

    ''' <summary>Zero, or less, means the user switched the cache off - nothing is read and
    ''' nothing is written. The same reading OcrDiskCacheMaxMb already documents.</summary>
    Friend Function IsEnabled(budgetMb As Integer) As Boolean
        Return budgetMb > 0
    End Function

    ''' <summary>
    ''' min(64 MB, a quarter of the budget). One 300-megabyte animation must not evict the
    ''' whole cache in order to store itself.
    ''' </summary>
    Friend Function MaxEntryBytes(budgetMb As Integer) As Long
        If Not IsEnabled(budgetMb) Then Return 0
        Dim quarterOfBudget As Long = (CLng(budgetMb) * 1024L * 1024L) \ 4L
        Return Math.Min(Absolute_Max_Entry_Bytes, quarterOfBudget)
    End Function

    ''' <summary>
    ''' An animation is always worth keeping; a still only once it has cost real time.
    ''' Nothing at all is kept for a failed decode - a Nothing payload is not an answer
    ''' worth remembering, and the file may still be arriving.
    ''' </summary>
    Friend Function ShouldStore(payload As DecodedPayload, budgetMb As Integer) As Boolean
        If Not IsEnabled(budgetMb) Then Return False
        If payload Is Nothing OrElse payload.Bytes Is Nothing OrElse payload.Bytes.Length = 0 Then Return False
        If payload.Bytes.LongLength > MaxEntryBytes(budgetMb) Then Return False
        If payload.IsAnimation Then Return True
        Return payload.DecodeMs >= Decode_Cache_Min_Ms
    End Function

End Module
#End If
