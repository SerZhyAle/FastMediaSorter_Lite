#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices

' Thumbnails from the shell, with a decode of our own when the shell has none
' (005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md PG-3).
'
' Windows already holds a thumbnail for most files in a folder, and has handlers for
' formats this application would otherwise have to decode itself (a PDF's first page, a
' video's frame, HEIC where the user installed the codec). Asking the shell first is what
' makes the second F3 on a 500-file folder cost almost nothing, and it is the only way a
' video or a document gets a picture at all - this application never decodes those for the
' grid.
'
' Two passes, as the specification says: IShellItemImageFactory with INCACHEONLY first
' (instant, no work done), then a real extraction. Both are THUMBNAILONLY: without it the
' shell answers a file it has no preview for with its generic ICON, and a grid of identical
' document icons looks like a thumbnail and means nothing.
'
' Every call in here must run on an STA thread (the loader owns two): thumbnail
' handlers are COM objects and some of them refuse any other apartment.

Friend Module ShellThumbnails

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativeSize
        Public cx As Integer
        Public cy As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure BitmapInfoHeader
        Public biSize As Integer
        Public biWidth As Integer
        Public biHeight As Integer
        Public biPlanes As Short
        Public biBitCount As Short
        Public biCompression As Integer
        Public biSizeImage As Integer
        Public biXPelsPerMeter As Integer
        Public biYPelsPerMeter As Integer
        Public biClrUsed As Integer
        Public biClrImportant As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativeBitmap
        Public bmType As Integer
        Public bmWidth As Integer
        Public bmHeight As Integer
        Public bmWidthBytes As Integer
        Public bmPlanes As Short
        Public bmBitsPixel As Short
        Public bmBits As IntPtr
    End Structure

    <ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IShellItemImageFactory
        <PreserveSig>
        Function GetImage(<[In]> size As NativeSize, <[In]> flags As Integer, ByRef phbm As IntPtr) As Integer
    End Interface

    ' SIIGBF_*
    Private Const SIIGBF_BIGGERSIZEOK As Integer = &H1
    Private Const SIIGBF_THUMBNAILONLY As Integer = &H8
    Private Const SIIGBF_INCACHEONLY As Integer = &H10

    Private Const BI_RGB As Integer = 0
    Private Const DIB_RGB_COLORS As Integer = 0

    <DllImport("shell32.dll", CharSet:=CharSet.Unicode, PreserveSig:=False)>
    Private Sub SHCreateItemFromParsingName(<MarshalAs(UnmanagedType.LPWStr)> pszPath As String,
                                            pbc As IntPtr,
                                            ByRef riid As Guid,
                                            <MarshalAs(UnmanagedType.Interface)> ByRef ppv As IShellItemImageFactory)
    End Sub

    <DllImport("gdi32.dll")>
    Private Function DeleteObject(hObject As IntPtr) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Function GetObject(hObject As IntPtr, nCount As Integer, ByRef lpObject As NativeBitmap) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Function GetDIBits(hdc As IntPtr, hbmp As IntPtr, uStartScan As Integer, cScanLines As Integer,
                               lpvBits As Byte(), ByRef lpbi As BitmapInfoHeader, uUsage As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Function GetDC(hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Function ReleaseDC(hWnd As IntPtr, hdc As IntPtr) As Integer
    End Function

    Private ReadOnly IID_IShellItemImageFactory As New Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")

    ''' <summary>
    ''' The shell's thumbnail of <paramref name="path"/>, at most <paramref name="px"/> on its
    ''' longer side, or Nothing. <paramref name="cachedOnly"/> asks for what the system already
    ''' has and does no work. The caller owns the bitmap. Never throws: "the shell has no
    ''' thumbnail" and "the shell failed" are the same answer to a grid, which falls back.
    ''' </summary>
    Friend Function TryGet(path As String, px As Integer, cachedOnly As Boolean) As Bitmap
        If String.IsNullOrEmpty(path) OrElse px < 16 Then Return Nothing

        Dim factory As IShellItemImageFactory = Nothing
        Dim handle As IntPtr = IntPtr.Zero
        Try
            Dim iid As Guid = IID_IShellItemImageFactory
            SHCreateItemFromParsingName(path, IntPtr.Zero, iid, factory)
            If factory Is Nothing Then Return Nothing

            Dim flags As Integer = SIIGBF_THUMBNAILONLY Or SIIGBF_BIGGERSIZEOK
            If cachedOnly Then flags = flags Or SIIGBF_INCACHEONLY

            Dim hr As Integer = factory.GetImage(New NativeSize With {.cx = px, .cy = px}, flags, handle)
            If hr <> 0 OrElse handle = IntPtr.Zero Then Return Nothing
            Return FromHandle(handle, px)
        Catch ex As Exception
            ' E_FAIL, "no thumbnail", a handler that crashed, a path with characters the
            ' shell refuses: all the same to the caller.
            Return Nothing
        Finally
            If handle <> IntPtr.Zero Then DeleteObject(handle)
            If factory IsNot Nothing Then
                Try
                    Marshal.ReleaseComObject(factory)
                Catch
                End Try
            End If
        End Try
    End Function

    ''' <summary>
    ''' The HBITMAP as a managed bitmap, top-down whatever the source's orientation: GetDIBits
    ''' with a NEGATIVE height always delivers rows top to bottom, which is the one way not to
    ''' have to know whether this particular DIB section was made bottom-up. Alpha is kept
    ''' (premultiplied, which is what the shell hands out); a bitmap that is bigger than asked
    ''' (BIGGERSIZEOK) is shrunk to fit.
    ''' </summary>
    Private Function FromHandle(handle As IntPtr, px As Integer) As Bitmap
        Dim info As NativeBitmap
        If GetObject(handle, Marshal.SizeOf(GetType(NativeBitmap)), info) = 0 Then Return Nothing
        Dim w As Integer = info.bmWidth
        Dim h As Integer = Math.Abs(info.bmHeight)
        If w <= 0 OrElse h <= 0 OrElse w > 8192 OrElse h > 8192 Then Return Nothing

        Dim header As New BitmapInfoHeader With {
            .biSize = Marshal.SizeOf(GetType(BitmapInfoHeader)),
            .biWidth = w,
            .biHeight = -h,
            .biPlanes = 1,
            .biBitCount = 32,
            .biCompression = BI_RGB}
        Dim pixels(w * h * 4 - 1) As Byte

        Dim screen As IntPtr = GetDC(IntPtr.Zero)
        Dim copied As Integer
        Try
            copied = GetDIBits(screen, handle, 0, h, pixels, header, DIB_RGB_COLORS)
        Finally
            If screen <> IntPtr.Zero Then ReleaseDC(IntPtr.Zero, screen)
        End Try
        If copied = 0 Then Return Nothing

        Dim full As New Bitmap(w, h, PixelFormat.Format32bppPArgb)
        Dim data As BitmapData = full.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb)
        Try
            ' Both are w*4 bytes per row, but the bitmap's stride is padded to its own rule.
            For row As Integer = 0 To h - 1
                Marshal.Copy(pixels, row * w * 4, IntPtr.Add(data.Scan0, row * data.Stride), w * 4)
            Next
        Finally
            full.UnlockBits(data)
        End Try

        If w <= px AndAlso h <= px Then Return full
        Try
            Return FitInside(full, px)
        Finally
            full.Dispose()
        End Try
    End Function

    ''' <summary>The bitmap shrunk, aspect kept, so its longer side is <paramref name="px"/>.</summary>
    Friend Function FitInside(source As Image, px As Integer) As Bitmap
        Dim scale As Double = Math.Min(px / CDbl(source.Width), px / CDbl(source.Height))
        Dim w As Integer = Math.Max(1, CInt(Math.Round(source.Width * scale)))
        Dim h As Integer = Math.Max(1, CInt(Math.Round(source.Height * scale)))
        Dim result As New Bitmap(w, h, PixelFormat.Format32bppPArgb)
        Try
            Using g As Graphics = Graphics.FromImage(result)
                g.InterpolationMode = InterpolationMode.HighQualityBilinear
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.DrawImage(source, New Rectangle(0, 0, w, h), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel)
            End Using
            Return result
        Catch
            result.Dispose()
            Throw
        End Try
    End Function

End Module

''' <summary>
''' The whole recipe for one thumbnail: our disk cache, then the shell's cache, then a real
''' shell extraction, then - for pictures - our own decode. Whatever cost work is written back
''' to our cache; what the shell already had is not copied (it keeps it better than we would).
''' Everything here is blocking and belongs on a worker thread; the panel's loader owns that.
''' </summary>
Friend NotInheritable Class ThumbnailService

    Private ReadOnly store As ThumbnailCacheStore
    Private ReadOnly materialize As Func(Of String, Boolean)

    ''' <param name="materialize">For a file that has no place on disk until somebody asks for it
    ''' (an entry of an open archive): makes it exist, on THIS worker thread, and says whether it
    ''' now does. Nothing for ordinary folders.</param>
    Friend Sub New(store As ThumbnailCacheStore, Optional materialize As Func(Of String, Boolean) = Nothing)
        Me.store = store
        Me.materialize = materialize
    End Sub

    ''' <summary>The thumbnail, or Nothing when there is no way to make one (a document with
    ''' no shell handler, a video the shell cannot frame, a damaged picture). The caller draws
    ''' the kind's glyph then. The bitmap is the caller's to dispose.</summary>
    Friend Function Render(path As String, px As Integer, kind As MediaKind) As Bitmap
        Dim info As FileInfo
        Try
            info = New FileInfo(path)
            If Not info.Exists Then
                If materialize Is Nothing OrElse Not materialize(path) Then Return Nothing
                info.Refresh()
                If Not info.Exists Then Return Nothing
            End If
        Catch
            Return Nothing
        End Try

        Dim key As String = ThumbnailCacheKey.Build(path, info.LastWriteTimeUtc.Ticks, info.Length, px)

        Dim found As Bitmap = store.TryGet(key)
        If found IsNot Nothing Then Return found

        found = ShellThumbnails.TryGet(path, px, cachedOnly:=True)
        If found IsNot Nothing Then Return found

        found = ShellThumbnails.TryGet(path, px, cachedOnly:=False)
        If found Is Nothing AndAlso kind = MediaKind.Image Then found = DecodeFit(path, px)
        If found IsNot Nothing Then store.Put(key, found)
        Return found
    End Function

    ''' <summary>The decoder seam's own picture, shrunk to fit - for what Windows has no
    ''' codec for (AVIF/HEIC without the Store extension, animated WEBP). The same
    ''' LoadImageWithStream every other path uses, so a format works in the grid exactly when
    ''' it works on the main surface.</summary>
    Private Shared Function DecodeFit(path As String, px As Integer) As Bitmap
        Try
            ' An animation is drawn by its first frame in a grid cell: the whole transcode of a long
            ' one is seconds of work nobody sees (spec 241 rule 14).
            Dim failure As PathFailureKind = PathFailureKind.Content
            Dim loaded = LoadImageStaged(path, failure, ImageLoadStage.FirstFrame)
            If loaded Is Nothing Then Return Nothing
            Using stream As MemoryStream = loaded.Item2
                Using original As Image = loaded.Item1
                    If original.Width <= 0 OrElse original.Height <= 0 Then Return Nothing
                    Return ShellThumbnails.FitInside(original, px)
                End Using
            End Using
        Catch
            Return Nothing
        End Try
    End Function

End Class
#End If
