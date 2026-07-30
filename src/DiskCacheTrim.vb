Option Strict On

Imports System.Collections.Generic
Imports System.IO

''' <summary>
''' Keeps an on-disk cache directory under a size budget by deleting its oldest entries.
'''
''' Written for two caches that had no eviction at all and grew for the life of the
''' installation (SPECIFICATION_LONG_RUN_STABILITY.md, stage D):
'''  - the OCR result cache, one JSON per recognized image. Its key folds in the file's
'''    write time, the engine, the model and the languages, so re-recognizing an edited
'''    image - or changing any setting - ORPHANS the previous entry rather than replacing
'''    it. A user reading a few hundred pages a day added hundreds of files a day, and the
'''    only deletion path in the code was a full wipe triggered by opening the OCR settings
'''    tab. Worse, the limit the settings window offers (OcrDiskCacheMaxMb, default 250 MB)
'''    was read, written to the registry, shown to the user - and enforced by nobody.
'''  - the browser-translate cache, one FOLDER per image handed to the external tool, each
'''    holding an HTML page plus its assets.
'''
''' Deliberately not an LRU: last-write time is what the file system already records, and
''' these entries are written once and read as long as the source image is unchanged. Age
''' is the honest proxy, and it costs no bookkeeping of our own.
'''
''' Never throws. A cache that cannot be trimmed is a disk-space problem, not a reason to
''' fail whatever the caller was actually doing.
''' </summary>
Friend Module DiskCacheTrim

    ''' <summary>
    ''' Deletes the oldest entries of <paramref name="cache_Dir"/> until the total is under
    ''' <paramref name="max_Mb"/>. Returns how many entries were removed.
    ''' </summary>
    ''' <param name="entries_Are_Folders">False = each cache entry is a file matching
    ''' <paramref name="file_Pattern"/>; True = each entry is an immediate subdirectory and
    ''' its whole tree counts towards the total.</param>
    ''' <param name="max_Mb">Budget in megabytes. Zero or less means "no limit" and the call
    ''' does nothing - that is the reading the settings UI documents.</param>
    Friend Function TrimToBudget(cache_Dir As String,
                                 max_Mb As Integer,
                                 Optional file_Pattern As String = "*.json",
                                 Optional entries_Are_Folders As Boolean = False) As Integer
        Try
            If max_Mb <= 0 Then Return 0
            If String.IsNullOrEmpty(cache_Dir) OrElse Not Directory.Exists(cache_Dir) Then Return 0

            Dim budget As Long = CLng(max_Mb) * 1024L * 1024L
            Dim entries As List(Of CacheEntry) = Measure(cache_Dir, file_Pattern, entries_Are_Folders)

            Dim total As Long = 0
            For Each e As CacheEntry In entries
                total += e.Bytes
            Next
            If total <= budget Then Return 0

            ' Oldest first - those are the ones whose source image is least likely to still
            ' be the one on screen.
            entries.Sort(Function(a, b) a.LastWrite.CompareTo(b.LastWrite))

            Dim removed As Integer = 0
            For Each e As CacheEntry In entries
                If total <= budget Then Exit For
                If Delete(e, entries_Are_Folders) Then
                    total -= e.Bytes
                    removed += 1
                End If
            Next
            Return removed
        Catch
            Return 0
        End Try
    End Function

    Private Class CacheEntry
        Public Property Path As String = ""
        Public Property Bytes As Long
        Public Property LastWrite As DateTime
    End Class

    Private Function Measure(cache_Dir As String, file_Pattern As String, entries_Are_Folders As Boolean) As List(Of CacheEntry)
        Dim result As New List(Of CacheEntry)()

        If entries_Are_Folders Then
            For Each dir As String In Directory.GetDirectories(cache_Dir)
                Try
                    Dim info As New DirectoryInfo(dir)
                    Dim bytes As Long = 0
                    Dim newest As DateTime = info.LastWriteTimeUtc
                    For Each f As FileInfo In info.GetFiles("*", SearchOption.AllDirectories)
                        bytes += f.Length
                        If f.LastWriteTimeUtc > newest Then newest = f.LastWriteTimeUtc
                    Next
                    result.Add(New CacheEntry With {.Path = dir, .Bytes = bytes, .LastWrite = newest})
                Catch
                    ' An entry we cannot even measure is one we will not delete either.
                End Try
            Next
            Return result
        End If

        For Each f As String In Directory.GetFiles(cache_Dir, file_Pattern)
            Try
                Dim info As New FileInfo(f)
                result.Add(New CacheEntry With {.Path = f, .Bytes = info.Length, .LastWrite = info.LastWriteTimeUtc})
            Catch
            End Try
        Next
        Return result
    End Function

    Private Function Delete(entry As CacheEntry, entries_Are_Folders As Boolean) As Boolean
        Try
            If entries_Are_Folders Then
                Directory.Delete(entry.Path, True)
            Else
                File.Delete(entry.Path)
            End If
            Return True
        Catch
            ' In use by something else right now - skip it, the next trim will get it.
            Return False
        End Try
    End Function

End Module
