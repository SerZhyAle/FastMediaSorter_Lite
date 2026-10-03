#If Not NETFRAMEWORK Then
Option Strict On

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Threading
Imports Xunit

''' <summary>
''' 005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md PG-3 and §7: the key is stable and
''' invalidates on exactly what changes a picture, the store round-trips, a damaged entry is a
''' miss, and the budget is a ceiling. Everything runs against a temporary directory - never
''' the user's own cache.
''' </summary>
Public Class ThumbnailCacheKeyTests
    Implements IDisposable

    Private ReadOnly dir As String

    Public Sub New()
        dir = Path.Combine(Path.GetTempPath(), "fms-thumb-cache-tests", Guid.NewGuid().ToString("N"))
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        Catch
        End Try
    End Sub

    Private Shared Function Swatch(w As Integer, h As Integer, c As Color) As Bitmap
        Dim b As New Bitmap(w, h, PixelFormat.Format32bppPArgb)
        Using g As Graphics = Graphics.FromImage(b)
            g.Clear(c)
        End Using
        Return b
    End Function

    ' ------------------------------------------------------------------- the key ----

    <Fact>
    Public Sub The_key_is_stable()
        Dim a As String = ThumbnailCacheKey.Build("D:\in\a.jpg", 639000000000000000L, 1234L, 128)
        Dim b As String = ThumbnailCacheKey.Build("D:\in\a.jpg", 639000000000000000L, 1234L, 128)
        Assert.Equal(a, b)
        Assert.Equal(ThumbnailCacheKey.FileNameFor(a), ThumbnailCacheKey.FileNameFor(b))
    End Sub

    ''' <summary>The only way this cache shows a wrong picture is a key that fails to change
    ''' when the answer does - so every input is moved in turn.</summary>
    <Fact>
    Public Sub Each_input_that_changes_the_picture_changes_the_key()
        Dim baseKey As String = ThumbnailCacheKey.Build("D:\in\a.jpg", 100L, 5000L, 128)
        Dim names As New HashSet(Of String) From {
            ThumbnailCacheKey.FileNameFor(baseKey),
            ThumbnailCacheKey.FileNameFor(ThumbnailCacheKey.Build("D:\in\b.jpg", 100L, 5000L, 128)),   ' another file
            ThumbnailCacheKey.FileNameFor(ThumbnailCacheKey.Build("D:\in\a.jpg", 101L, 5000L, 128)),   ' edited in place
            ThumbnailCacheKey.FileNameFor(ThumbnailCacheKey.Build("D:\in\a.jpg", 100L, 5001L, 128)),   ' same time, other size
            ThumbnailCacheKey.FileNameFor(ThumbnailCacheKey.Build("D:\in\a.jpg", 100L, 5000L, 192))    ' another card size
        }
        Assert.Equal(5, names.Count)
    End Sub

    <Fact>
    Public Sub The_name_is_a_legal_fixed_length_file_name_whatever_the_picture_is_called()
        Dim ugly As String = ThumbnailCacheKey.FileNameFor(ThumbnailCacheKey.Build("\\server\sh are\a:b*c?.jpg" & ChrW(&H202E), 1L, 2L, 96))
        Assert.EndsWith(ThumbnailCacheKey.File_Extension, ugly)
        Assert.Equal(40 + ThumbnailCacheKey.File_Extension.Length, ugly.Length)
        Assert.True(ugly.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
        Assert.Equal(ThumbnailCacheKey.FileNameFor(Nothing), ThumbnailCacheKey.FileNameFor(""))
    End Sub

    ' ------------------------------------------------------------------ the store ----

    <Fact>
    Public Sub A_thumbnail_round_trips_with_its_pixels_and_size()
        Dim store As New ThumbnailCacheStore(dir, 64)
        Using source As Bitmap = Swatch(40, 25, Color.FromArgb(255, 200, 30, 60))
            store.Put("k1", source)
        End Using

        Using hit As Bitmap = store.TryGet("k1")
            Assert.NotNull(hit)
            Assert.Equal(40, hit.Width)
            Assert.Equal(25, hit.Height)
            Dim px As Color = hit.GetPixel(20, 12)
            Assert.Equal(200, px.R)
            Assert.Equal(30, px.G)
            Assert.Equal(60, px.B)
        End Using
    End Sub

    <Fact>
    Public Sub Transparency_survives()
        Dim store As New ThumbnailCacheStore(dir, 64)
        Using source As New Bitmap(8, 8, PixelFormat.Format32bppPArgb)
            ' fully transparent except one opaque pixel
            source.SetPixel(2, 2, Color.FromArgb(255, 10, 20, 30))
            store.Put("k", source)
        End Using
        Using hit As Bitmap = store.TryGet("k")
            Assert.Equal(0, hit.GetPixel(0, 0).A)
            Assert.Equal(255, hit.GetPixel(2, 2).A)
        End Using
    End Sub

    <Fact>
    Public Sub A_miss_is_Nothing_and_nothing_is_created_by_asking()
        Dim store As New ThumbnailCacheStore(dir, 64)
        Assert.Null(store.TryGet("never stored"))
        Assert.False(Directory.Exists(dir))
    End Sub

    ''' <summary>A read never trusts the file (invariant 1 of the decode cache, kept): garbage is a
    ''' miss AND is removed, so the next look draws it again instead of failing the same way.</summary>
    <Theory>
    <InlineData(0)>
    <InlineData(7)>
    Public Sub A_damaged_entry_is_a_miss_and_is_deleted(length As Integer)
        Dim store As New ThumbnailCacheStore(dir, 64)
        Directory.CreateDirectory(dir)
        Dim entry As String = Path.Combine(dir, ThumbnailCacheKey.FileNameFor("bad"))
        File.WriteAllBytes(entry, New Byte(length - 1) {})

        Assert.Null(store.TryGet("bad"))
        Assert.False(File.Exists(entry))
    End Sub

    <Fact>
    Public Sub A_budget_of_zero_switches_the_cache_off_both_ways()
        Dim store As New ThumbnailCacheStore(dir, 0)
        Assert.False(store.IsEnabled)
        Using source As Bitmap = Swatch(8, 8, Color.Red)
            store.Put("k", source)
        End Using
        Assert.False(Directory.Exists(dir))
        Assert.Null(store.TryGet("k"))
    End Sub

    <Fact>
    Public Sub A_write_leaves_no_temporary_file_behind()
        Dim store As New ThumbnailCacheStore(dir, 64)
        Using source As Bitmap = Swatch(8, 8, Color.Red)
            store.Put("k", source)
        End Using
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"))
        Assert.Single(Directory.GetFiles(dir, ThumbnailCacheKey.File_Pattern))
    End Sub

    ''' <summary>The budget is a ceiling: when the directory is over it, the OLDEST entries go.</summary>
    <Fact>
    Public Sub Trimming_removes_the_oldest_entries_first()
        Directory.CreateDirectory(dir)
        ' Four 400 KB entries against a 1 MB budget; the two oldest must be the ones to go.
        Dim names As New List(Of String)()
        For i As Integer = 0 To 3
            Dim name As String = Path.Combine(dir, ThumbnailCacheKey.FileNameFor("entry" & i.ToString()))
            File.WriteAllBytes(name, New Byte(400 * 1024 - 1) {})
            File.SetLastWriteTimeUtc(name, DateTime.UtcNow.AddHours(-(10 - i)))
            names.Add(name)
        Next

        Dim store As New ThumbnailCacheStore(dir, 1)
        store.TrimToBudget()

        Assert.False(File.Exists(names(0)))
        Assert.False(File.Exists(names(1)))
        Assert.True(File.Exists(names(3)))
        Assert.True(store.BytesOnDisk() <= 1024L * 1024L)
    End Sub

    <Fact>
    Public Sub Clear_removes_every_entry()
        Dim store As New ThumbnailCacheStore(dir, 64)
        For i As Integer = 0 To 2
            Using source As Bitmap = Swatch(8, 8, Color.Red)
                store.Put("k" & i.ToString(), source)
            End Using
        Next
        Assert.True(store.BytesOnDisk() > 0)
        store.Clear()
        Assert.Equal(0L, store.BytesOnDisk())
    End Sub

    ' ------------------------------------------------------------------ the shell ----

    <Fact>
    Public Sub The_shell_gives_Nothing_not_an_exception_for_a_file_that_is_not_there()
        Dim result As Bitmap = Nothing
        Dim crashed As Exception = Nothing
        Dim worker As New Thread(Sub()
                                     Try
                                         result = ShellThumbnails.TryGet(Path.Combine(dir, "no-such-file.jpg"), 128, cachedOnly:=False)
                                     Catch ex As Exception
                                         crashed = ex
                                     End Try
                                 End Sub)
        worker.SetApartmentState(ApartmentState.STA)
        worker.Start()
        worker.Join()
        Assert.Null(crashed)
        Assert.Null(result)
    End Sub

    ''' <summary>
    ''' If this machine's shell DOES make a thumbnail of a plain PNG, it comes out upright and
    ''' within the size asked for. The shell is not under our control (a headless build agent
    ''' may have no thumbnail host at all), so an empty answer is not a failure - only a wrong
    ''' one is. This is the check that GetDIBits with a negative height earns its keep on.
    ''' </summary>
    <Fact>
    Public Sub A_shell_thumbnail_if_there_is_one_is_upright_and_fits()
        Directory.CreateDirectory(dir)
        Dim file As String = Path.Combine(dir, "two-bands.png")
        Using b As New Bitmap(200, 100, PixelFormat.Format32bppArgb)
            Using g As Graphics = Graphics.FromImage(b)
                g.Clear(Color.Blue)
                Using top As New SolidBrush(Color.Red)
                    g.FillRectangle(top, 0, 0, 200, 50)
                End Using
            End Using
            b.Save(file, ImageFormat.Png)
        End Using

        Dim result As Bitmap = Nothing
        Dim worker As New Thread(Sub() result = ShellThumbnails.TryGet(file, 128, cachedOnly:=False))
        worker.SetApartmentState(ApartmentState.STA)
        worker.Start()
        worker.Join()

        If result Is Nothing Then Return   ' no thumbnail host here: nothing to assert about
        Using result
            Assert.True(Math.Max(result.Width, result.Height) <= 128 + 1)
            Dim above As Color = result.GetPixel(result.Width \ 2, result.Height \ 4)
            Dim below As Color = result.GetPixel(result.Width \ 2, result.Height * 3 \ 4)
            ' Upright: the red band is the TOP half.
            Assert.True(above.R > above.B, "the top half should be red - the thumbnail is upside down")
            Assert.True(below.B > below.R, "the bottom half should be blue")
        End Using
    End Sub

    <Fact>
    Public Sub FitInside_keeps_the_aspect_and_the_longer_side()
        Using source As Bitmap = Swatch(400, 100, Color.Green)
            Using fitted As Bitmap = ShellThumbnails.FitInside(source, 128)
                Assert.Equal(128, fitted.Width)
                Assert.Equal(32, fitted.Height)
            End Using
        End Using
        Using tall As Bitmap = Swatch(50, 200, Color.Green)
            Using fitted As Bitmap = ShellThumbnails.FitInside(tall, 100)
                Assert.Equal(25, fitted.Width)
                Assert.Equal(100, fitted.Height)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Rendering_a_missing_file_is_Nothing()
        Dim service As New ThumbnailService(New ThumbnailCacheStore(dir, 64))
        Assert.Null(service.Render(Path.Combine(dir, "nope.jpg"), 128, MediaKind.Image))
    End Sub

End Class
#End If
