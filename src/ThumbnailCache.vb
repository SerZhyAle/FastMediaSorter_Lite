#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading

' The F3 panel's own on-disk thumbnail cache
' (005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md PG-3).
'
' It sits behind the shell's thumbnail cache, not instead of it: the shell answers for the
' formats Windows has a handler for, and this answers for what the shell cannot do (a HEIC
' without the paid codec, an animated WEBP, a file materialised from an archive) and for
' whatever the shell's own cache has evicted. Same discipline as the decode cache it is
' modelled on: a hit is never trusted blindly, a write is atomic, the budget is enforced
' where the directory grows, and a failure is silent - a full disk must slow a panel down,
' not interrupt somebody's triage with a dialog about a cache.

''' <summary>
''' What identifies one cached thumbnail. Pure on purpose - no disk, no clock - because the
''' only way this cache can show a WRONG picture is a key that fails to change when the file
''' does, and that is a question for a test.
''' </summary>
Friend Module ThumbnailCacheKey

    Friend Const File_Extension As String = ".fmsthumb"

    Friend Const File_Pattern As String = "*" & File_Extension

    ''' <summary>Bump when the way a thumbnail is produced changes what it looks like (the
    ''' fit rule, the padding, the colour handling): every older entry is then a miss.</summary>
    Friend Const Format_Version As Integer = 1

    ''' <summary>
    ''' path + write time + size + requested pixels. Write time AND size rather than a content
    ''' hash - hashing a 200 MB video to decide whether to skip drawing one frame of it is the
    ''' cost this cache exists to remove. A file edited in place keeps its path and gets a new
    ''' pair, so the old entry is orphaned (and trimmed), and a file the editor just wrote
    ''' needs no explicit invalidation: its key has already changed.
    ''' </summary>
    Friend Function Build(filePath As String, lastWriteUtcTicks As Long, lengthBytes As Long,
                          requestedPx As Integer) As String
        Return String.Join("|",
                           If(filePath, String.Empty),
                           lastWriteUtcTicks.ToString(Globalization.CultureInfo.InvariantCulture),
                           lengthBytes.ToString(Globalization.CultureInfo.InvariantCulture),
                           requestedPx.ToString(Globalization.CultureInfo.InvariantCulture),
                           Format_Version.ToString(Globalization.CultureInfo.InvariantCulture))
    End Function

    ''' <summary>
    ''' <c>3f0a..d1.fmsthumb</c>: SHA-1 of the key as lower-case hex - a fixed-length name that
    ''' is legal on every file system whatever the picture was called. A collision would serve
    ''' a wrong thumbnail, not crash; over path + ticks + size + pixels it is not in this
    ''' universe.
    ''' </summary>
    Friend Function FileNameFor(key As String) As String
        Using sha As SHA1 = SHA1.Create()
            Dim bytes As Byte() = sha.ComputeHash(Encoding.UTF8.GetBytes(If(key, String.Empty)))
            Dim builder As New StringBuilder(bytes.Length * 2 + File_Extension.Length)
            For Each b As Byte In bytes
                builder.Append(b.ToString("x2", Globalization.CultureInfo.InvariantCulture))
            Next
            builder.Append(File_Extension)
            Return builder.ToString()
        End Using
    End Function

End Module

''' <summary>
''' The cache directory. One instance per panel session; the DIRECTORY is a constructor
''' argument, which is what lets the round trip, the eviction and "a damaged entry is a miss"
''' be proven against a temporary directory instead of against the user's own cache.
''' </summary>
Friend NotInheritable Class ThumbnailCacheStore

    ''' <summary>A trim scans the directory; doing it after every one of five hundred writes
    ''' would turn filling the cache into a quadratic walk. Every Nth write is enough - the
    ''' budget is a ceiling over a long run, not a per-write promise.</summary>
    Friend Const Trim_Every_Writes As Integer = 40

    ''' <summary>A hit older than this is touched, so the oldest-first trim behaves as LRU
    ''' (what is looked at stays) without a write on every single read.</summary>
    Private Shared ReadOnly Touch_After As TimeSpan = TimeSpan.FromHours(12)

    Private ReadOnly directory_Path As String
    Private ReadOnly budget_Mb As Integer
    Private writes_Since_Trim As Integer

    ''' <param name="budgetMb">Zero or less: the cache is OFF - nothing read, nothing written.</param>
    Friend Sub New(directoryPath As String, budgetMb As Integer)
        directory_Path = directoryPath
        Me.budget_Mb = budgetMb
    End Sub

    ''' <summary>The same %LOCALAPPDATA% root every other cache uses, which is what keeps it
    ''' writable inside the MSIX container.</summary>
    Friend Shared Function DefaultDirectory() As String
        Return Path.Combine(AppPaths.LocalAppDataRoot(), "thumb-cache")
    End Function

    Friend ReadOnly Property IsEnabled As Boolean
        Get
            Return budget_Mb > 0 AndAlso Not String.IsNullOrEmpty(directory_Path)
        End Get
    End Property

    Friend ReadOnly Property DirectoryPath As String
        Get
            Return directory_Path
        End Get
    End Property

    ''' <summary>
    ''' The stored thumbnail, or Nothing. NEVER trusts the file: any exception, an empty entry
    ''' or bytes GDI+ refuses is a MISS and the entry is deleted, so the caller draws it again
    ''' as it always did (a cache miss must be indistinguishable from today's behaviour, and a
    ''' cache can never show someone else's picture). The bitmap is a private copy - the
    ''' caller owns it and the file is not held open.
    ''' </summary>
    Friend Function TryGet(key As String) As Bitmap
        If Not IsEnabled OrElse String.IsNullOrEmpty(key) Then Return Nothing

        Dim entry As String = Path.Combine(directory_Path, ThumbnailCacheKey.FileNameFor(key))
        Try
            If Not File.Exists(entry) Then Return Nothing
            Dim bytes As Byte() = File.ReadAllBytes(entry)
            If bytes Is Nothing OrElse bytes.Length = 0 Then
                DeleteQuietly(entry)
                Return Nothing
            End If

            Dim copy As Bitmap
            Using stream As New MemoryStream(bytes)
                Using decoded As Image = Image.FromStream(stream)
                    copy = New Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppPArgb)
                    Using g As Graphics = Graphics.FromImage(copy)
                        g.DrawImage(decoded, 0, 0, decoded.Width, decoded.Height)
                    End Using
                End Using
            End Using

            Try
                If DateTime.UtcNow - File.GetLastWriteTimeUtc(entry) > Touch_After Then
                    File.SetLastWriteTimeUtc(entry, DateTime.UtcNow)
                End If
            Catch
                ' Read-only cache directory: the entry still served, it just ages.
            End Try
            Return copy
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " thumb-cache: read failed: " & ex.Message)
            DeleteQuietly(entry)
            Return Nothing
        End Try
    End Function

    ''' <summary>Writes the thumbnail as a PNG (so transparency survives) atomically, and
    ''' enforces the budget every <see cref="Trim_Every_Writes"/> writes. Silent on failure.</summary>
    Friend Sub Put(key As String, thumbnail As Bitmap)
        If Not IsEnabled OrElse String.IsNullOrEmpty(key) OrElse thumbnail Is Nothing Then Return

        Try
            Directory.CreateDirectory(directory_Path)
            Dim target As String = Path.Combine(directory_Path, ThumbnailCacheKey.FileNameFor(key))
            Dim temporary As String = Path.Combine(directory_Path, Path.GetRandomFileName() & ".tmp")
            Try
                Using stream As New FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                    thumbnail.Save(stream, ImageFormat.Png)
                End Using
                ' Two panels (two windows) share this directory: the last writer wins, and
                ' both wrote the same picture for the same key.
                File.Move(temporary, target, overwrite:=True)
            Finally
                DeleteQuietly(temporary)
            End Try

            If Interlocked.Increment(writes_Since_Trim) >= Trim_Every_Writes Then
                Interlocked.Exchange(writes_Since_Trim, 0)
                TrimToBudget()
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " thumb-cache: store failed: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Deletes the oldest entries until the directory is under the budget.</summary>
    Friend Sub TrimToBudget()
        If Not IsEnabled Then Return
        DiskCacheTrim.TrimToBudget(directory_Path, budget_Mb, ThumbnailCacheKey.File_Pattern)
    End Sub

    ''' <summary>Everything the cache holds, in bytes. Never throws.</summary>
    Friend Function BytesOnDisk() As Long
        Dim total As Long = 0
        Try
            If String.IsNullOrEmpty(directory_Path) OrElse Not Directory.Exists(directory_Path) Then Return 0
            For Each entry As String In Directory.GetFiles(directory_Path, ThumbnailCacheKey.File_Pattern)
                Try
                    total += New FileInfo(entry).Length
                Catch
                End Try
            Next
        Catch
        End Try
        Return total
    End Function

    ''' <summary>Removes every entry (and any leftover temporary file).</summary>
    Friend Sub Clear()
        Try
            If String.IsNullOrEmpty(directory_Path) OrElse Not Directory.Exists(directory_Path) Then Return
            For Each entry As String In Directory.GetFiles(directory_Path, ThumbnailCacheKey.File_Pattern)
                DeleteQuietly(entry)
            Next
            For Each leftover As String In Directory.GetFiles(directory_Path, "*.tmp")
                DeleteQuietly(leftover)
            Next
        Catch
        End Try
    End Sub

    Private Shared Sub DeleteQuietly(path As String)
        Try
            If File.Exists(path) Then File.Delete(path)
        Catch
        End Try
    End Sub

End Class
#End If
