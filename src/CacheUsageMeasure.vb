#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO

''' <summary>
''' Measures how much disk a cache directory uses, for the Storage page's usage rows
''' (130_SPECIFICATION_CACHE_MANAGEMENT_ON_STORAGE.md §3.3).
'''
''' Unlike the stores' own <c>BytesOnDisk</c> (DecodeCacheStore, ThumbnailCacheStore),
''' which answer "never throws" because a viewer must not die over a cache, these two let
''' a real failure REACH the caller: a settings row has to tell "0.0 MB" from "I could
''' not look", and shows "unknown" for the second. The one failure that is NOT an error
''' is a directory that does not exist - nothing was ever cached there, which is an
''' honest zero (<see cref="DirectoryNotFoundException"/> is swallowed for exactly that).
''' Files removed during the walk are harmless; permission and I/O failures propagate
''' rather than presenting a partial sum as the cache's measured size.
''' </summary>
Friend Module CacheUsageMeasure

    ''' <summary>Bytes of the files matching <paramref name="filePattern"/> directly inside
    ''' <paramref name="dir"/>. The pattern stays with the cache's own key module (the store
    ''' owns it); this only walks.</summary>
    Friend Function FlatBytes(dir As String, filePattern As String) As Long
        If String.IsNullOrEmpty(dir) Then Return 0
        Dim files As String()
        Try
            files = Directory.GetFiles(dir, filePattern)
        Catch missing As DirectoryNotFoundException
            Return 0
        End Try
        Dim total As Long = 0
        For Each entry As String In files
            Try
                total += New FileInfo(entry).Length
            Catch missing As FileNotFoundException
            Catch missing As DirectoryNotFoundException
            End Try
        Next
        Return total
    End Function

    ''' <summary>Bytes of every file under <paramref name="root"/>, recursively - the
    ''' archive and remote caches keep one session directory per viewer process, so the
    ''' size is the sum of their trees.</summary>
    Friend Function TreeBytes(root As String) As Long
        If String.IsNullOrEmpty(root) Then Return 0
        Return DirectoryBytes(root)
    End Function

    Private Function DirectoryBytes(dir As String) As Long
        Dim total As Long = 0
        Dim files As String()
        Dim children As String()
        Try
            files = Directory.GetFiles(dir)
            children = Directory.GetDirectories(dir)
        Catch missing As DirectoryNotFoundException
            Return 0
        End Try
        For Each entry As String In files
            Try
                total += New FileInfo(entry).Length
            Catch missing As FileNotFoundException
            Catch missing As DirectoryNotFoundException
            End Try
        Next
        For Each child As String In children
            If (File.GetAttributes(child) And FileAttributes.ReparsePoint) <> 0 Then
                Throw New IOException("Cannot measure a cache tree containing a reparse point.")
            End If
            total += DirectoryBytes(child)
        Next
        Return total
    End Function

End Module
#End If
