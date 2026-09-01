#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO

''' <summary>
''' The decode cache on disk
''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §5).
'''
''' Three properties are load-bearing and each is here rather than at the call site:
'''
'''  * A read NEVER trusts the file. Any exception, a zero-length entry, or bytes GDI+
'''    later refuses is a MISS - the entry is deleted and the caller decodes as it always
'''    did (invariant 1: a cache miss must be indistinguishable from today's behaviour, and
'''    a cache can never show someone else's picture).
'''  * A write is atomic and cross-process safe - temp file beside the destination, then
'''    Replace/Move onto the final name. Two viewer windows share this directory, exactly
'''    as they share the OCR cache this is copied from.
'''  * The budget is enforced at the point the directory GROWS. That is the whole lesson of
'''    LONG_RUN_STABILITY stage D: a limit that is read, displayed and enforced by nobody
'''    is not a limit.
'''
''' A store failure is silent by design. A full disk must slow the application down, not
''' interrupt somebody's viewing with a dialog about a cache.
'''
''' The DIRECTORY is a parameter of every operation, and <see cref="CacheDir"/> is the one
''' place that knows where the real one is. That is not ceremony: it is what lets the
''' round trip, the eviction order and the "a damaged entry is a miss" rule be proven
''' against a temporary directory instead of against the user's own cache.
''' </summary>
Friend Module DecodeCacheStore

    ''' <summary>
    ''' The budget in megabytes, mirrored here from ModernViewerPreferences.DecodeCacheMaxMb.
    '''
    ''' A copy rather than a read, because the decode runs on a pool thread (the loading
    ''' indicator and the prefetch worker both call into here) and the preferences object
    ''' belongs to the form. An Integer field is written and read atomically, so the worst
    ''' a race can do is use the previous budget for one file.
    ''' </summary>
    Private budget_Mb As Integer = 512

    Friend Property BudgetMb As Integer
        Get
            Return budget_Mb
        End Get
        Set(value As Integer)
            budget_Mb = value
        End Set
    End Property

    ''' <summary>The same %LOCALAPPDATA% root the archive cache and the OCR cache use -
    ''' which is what keeps it writable inside the MSIX container.</summary>
    Friend Function CacheDir() As String
        Return Path.Combine(AppPaths.LocalAppDataRoot(), "decode-cache")
    End Function

    ''' <summary>
    ''' The payload stored for <paramref name="key"/>, or Nothing.
    '''
    ''' Both kinds are probed because the kind lives in the file name and a key alone does
    ''' not say which one was produced - two File.Exists calls against a local directory,
    ''' against a decode measured in seconds.
    ''' </summary>
    Friend Function TryLoad(cacheDir As String, key As String) As DecodedPayload
        If String.IsNullOrEmpty(cacheDir) OrElse String.IsNullOrEmpty(key) Then Return Nothing

        For Each kind As DecodedPayloadKind In New DecodedPayloadKind() {DecodedPayloadKind.Gif, DecodedPayloadKind.Png}
            Dim storedFile As String = EntryPath(cacheDir, key, kind)
            Try
                If Not File.Exists(storedFile) Then Continue For
                Dim bytes As Byte() = File.ReadAllBytes(storedFile)
                If bytes Is Nothing OrElse bytes.Length = 0 Then
                    ' Truncated by a power cut, or a write that never finished.
                    DeleteQuietly(storedFile)
                    Continue For
                End If
                Return New DecodedPayload With {
                    .Bytes = bytes,
                    .Kind = kind,
                    .IsAnimation = (kind = DecodedPayloadKind.Gif)
                }
            Catch ex As Exception
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " decode-cache: load failed: " & ex.Message)
            End Try
        Next

        Return Nothing
    End Function

    ''' <summary>Drops the entry a caller found undecodable, so the next open rebuilds it
    ''' instead of failing the same way for ever.</summary>
    Friend Sub Invalidate(cacheDir As String, key As String, kind As DecodedPayloadKind)
        If String.IsNullOrEmpty(cacheDir) OrElse String.IsNullOrEmpty(key) Then Return
        DeleteQuietly(EntryPath(cacheDir, key, kind))
    End Sub

    ''' <summary>Writes the payload and enforces the budget. Silent on failure - see the
    ''' class comment.</summary>
    Friend Sub TryStore(cacheDir As String, key As String, payload As DecodedPayload, budgetMb As Integer)
        If String.IsNullOrEmpty(cacheDir) OrElse String.IsNullOrEmpty(key) Then Return
        If Not DecodeCachePolicy.ShouldStore(payload, budgetMb) Then Return

        Try
            Directory.CreateDirectory(cacheDir)

            Dim target As String = EntryPath(cacheDir, key, payload.Kind)
            Dim temporary As String = Path.Combine(cacheDir, Path.GetRandomFileName() & ".tmp")
            Try
                File.WriteAllBytes(temporary, payload.Bytes)
                Swap(temporary, target)
            Finally
                Try
                    If File.Exists(temporary) Then File.Delete(temporary)
                Catch
                End Try
            End Try

            ' Right where the directory grew, oldest last-write first.
            DiskCacheTrim.TrimToBudget(cacheDir, budgetMb, DecodeCacheKey.File_Pattern)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " decode-cache: store failed: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Everything the cache holds, in bytes - what the settings row displays.
    ''' Never throws: a directory that cannot be measured is a disk problem, not a reason
    ''' to fail opening the settings window.</summary>
    Friend Function BytesOnDisk(cacheDir As String) As Long
        Dim total As Long = 0
        Try
            If String.IsNullOrEmpty(cacheDir) OrElse Not Directory.Exists(cacheDir) Then Return 0
            For Each entry As String In Directory.GetFiles(cacheDir, DecodeCacheKey.File_Pattern)
                Try
                    total += New FileInfo(entry).Length
                Catch
                End Try
            Next
        Catch
        End Try
        Return total
    End Function

    ''' <summary>Wipes the cache. Returns how many entries went; an entry another window is
    ''' reading right now simply stays until the next sweep.</summary>
    Friend Function Clear(cacheDir As String) As Integer
        Dim removed As Integer = 0
        Try
            If String.IsNullOrEmpty(cacheDir) OrElse Not Directory.Exists(cacheDir) Then Return 0
            For Each entry As String In Directory.GetFiles(cacheDir, DecodeCacheKey.File_Pattern)
                If DeleteQuietly(entry) Then removed += 1
            Next
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " decode-cache: clear failed: " & ex.Message)
        End Try
        Return removed
    End Function

    Friend Function EntryPath(cacheDir As String, key As String, kind As DecodedPayloadKind) As String
        Return Path.Combine(cacheDir, DecodeCacheKey.FileNameFor(key, kind))
    End Function

    ''' <summary>
    ''' File.Replace keeps the destination's identity and is the right call locally, but it
    ''' is not implemented on every network file system - and %LOCALAPPDATA% can be
    ''' redirected onto one. The fallback loses atomicity, so it is second, never first.
    ''' </summary>
    Private Sub Swap(temporary As String, target As String)
        If Not File.Exists(target) Then
            File.Move(temporary, target)
            Return
        End If

        Try
            File.Replace(temporary, target, Nothing, ignoreMetadataErrors:=True)
        Catch ex As PlatformNotSupportedException
            ReplaceByMove(temporary, target)
        Catch ex As IOException
            ReplaceByMove(temporary, target)
        End Try
    End Sub

    Private Sub ReplaceByMove(temporary As String, target As String)
        File.Delete(target)
        File.Move(temporary, target)
    End Sub

    Private Function DeleteQuietly(storedFile As String) As Boolean
        Try
            If Not File.Exists(storedFile) Then Return False
            File.Delete(storedFile)
            Return True
        Catch
            ' Being read by another window right now - the next trim will get it.
            Return False
        End Try
    End Function

End Module
#End If
