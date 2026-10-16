Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading

' Spec 230 T-1: the Windows clipboard behind a seam, for the "send / get the clipboard" functions of the Share Manager.
'
' What this file does and does not do (CLIPBOARD-GUARD 0.11 section 8, items L-R; DEVICE-EXCHANGE section 22 items BU, CC):
'   - the clipboard is read and written by raw Win32 calls on a DEDICATED background thread, never on the UI thread, and the
'     caller waits for it under a bound: a program that holds the clipboard, or a delay-rendered owner that never answers,
'     ends in a sentence and an abandoned thread, never in a frozen window (item N);
'   - ONE open per operation, with a bounded retry (12 x 15 ms); the kind, the size and the content (or the path) are read inside
'     that one open and the clipboard is closed BEFORE a byte goes to the network or to the disk (items B, N, O);
'   - the order of the kinds is the contract's: ONE regular file in a file drop, else an image (the registered PNG format, else
'     CF_DIBV5 or CF_DIB converted to PNG), else Unicode text; several files or a folder are refused (item BU);
'   - a received value is placed in one open-empty-set-close pass, with NO privacy marker: the person asked for it, and the Windows
'     history may keep it (item Q). The privacy-flag writer of SecureClipboard is for secrets and is not used here;
'   - nothing here logs a value, a name or a path - the caller logs the kind and a size class only (item R).
'
' The Share Manager's own rule "never write the clipboard outside the helper" (SecureClipboardTests) is about the managed
' clipboard class; this file talks to Win32 and is the one place that does so for a person's own value.

''' <summary>What one read of the clipboard found.</summary>
Friend Enum ClipboardReadKind
    ''' <summary>Nothing the exchange can carry (no text, no image, no file).</summary>
    Empty = 0
    Text = 1
    Image = 2
    OneFile = 3
    ''' <summary>A file drop of several files or of a folder: refused with one sentence, nothing is sent.</summary>
    NotOneObject = 4
    ''' <summary>Another program held the clipboard for the whole bounded retry.</summary>
    Busy = 5
    ''' <summary>The read did not return within its bound (a delay-rendered owner that never answered).</summary>
    TimedOut = 6
    ''' <summary>The clipboard could not be read, or a file drop names a file that is not there.</summary>
    Failed = 7
    ''' <summary>A text or an image bigger than this program keeps in memory.</summary>
    TooLarge = 8
End Enum

''' <summary>What was read, without its words in any text a log could print.</summary>
Friend NotInheritable Class ClipboardSnapshot

    Public ReadOnly Kind As ClipboardReadKind
    ''' <summary>Text: UTF-8 without a BOM, line ends LF. Image: PNG bytes. Else Nothing.</summary>
    Public ReadOnly Content As Byte()
    ''' <summary>The file of <see cref="ClipboardReadKind.OneFile"/> (fully qualified). Else "".</summary>
    Public ReadOnly Path As String
    ''' <summary>The bytes that will be sent: the content's length, or the file's length.</summary>
    Public ReadOnly Size As Long

    Public Sub New(kind As ClipboardReadKind, Optional content As Byte() = Nothing, Optional path As String = "", Optional size As Long = 0)
        Me.Kind = kind
        Me.Content = content
        Me.Path = If(path, "")
        Me.Size = size
    End Sub

    Friend Shared Function OfKind(kind As ClipboardReadKind) As ClipboardSnapshot
        Return New ClipboardSnapshot(kind)
    End Function

    ''' <summary>The kind only - never the content and never the path.</summary>
    Public Overrides Function ToString() As String
        Return "ClipboardSnapshot " & Kind.ToString()
    End Function

End Class

Friend Enum ClipboardPlaceOutcome
    Placed = 0
    ''' <summary>Another program held the clipboard for the whole bounded retry.</summary>
    Busy = 1
    TimedOut = 2
    Failed = 3
End Enum

''' <summary>The clipboard as the exchange uses it. A test hands in a fake; production is <see cref="ClipboardWin32"/>.</summary>
Friend Interface IClipboardPort
    ''' <summary>Reads once. Never blocks the caller beyond the port's own bound.</summary>
    Function ReadAsync(ct As CancellationToken) As Task(Of ClipboardSnapshot)
    ''' <summary>Places text (UTF-8, line ends as given): one open-empty-set-close pass, CF_UNICODETEXT.</summary>
    Function PlaceTextAsync(utf8 As Byte(), ct As CancellationToken) As Task(Of ClipboardPlaceOutcome)
    ''' <summary>Places a PNG: the registered PNG format and CF_DIB, in one pass.</summary>
    Function PlaceImageAsync(png As Byte(), ct As CancellationToken) As Task(Of ClipboardPlaceOutcome)
    ''' <summary>Places one file as a file drop with the copy drop effect, in one pass.</summary>
    Function PlaceFileAsync(path As String, ct As CancellationToken) As Task(Of ClipboardPlaceOutcome)
End Interface

''' <summary>The line-end rules of item BX, in one place and free of Win32.</summary>
Friend NotInheritable Class ClipboardText

    Private Sub New()
    End Sub

    ''' <summary>CRLF and a bare CR become LF (the wire form).</summary>
    Public Shared Function ToLf(text As String) As String
        If String.IsNullOrEmpty(text) Then Return ""
        Return text.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
    End Function

    ''' <summary>LF becomes CRLF (the Windows form); a CRLF already there is not doubled.</summary>
    Public Shared Function ToCrLf(text As String) As String
        If String.IsNullOrEmpty(text) Then Return ""
        Return ToLf(text).Replace(vbLf, vbCrLf)
    End Function

    ''' <summary>The wire bytes of a text: LF line ends, UTF-8, no BOM.</summary>
    Public Shared Function ToWireBytes(text As String) As Byte()
        Return New UTF8Encoding(False).GetBytes(ToLf(text))
    End Function

End Class

''' <summary>DIB &lt;-&gt; PNG, free of the clipboard so a test can feed it bytes.</summary>
Friend NotInheritable Class ClipboardImages

    Private Sub New()
    End Sub

    Private Const BmpFileHeaderSize As Integer = 14

    ''' <summary>The PNG of a packed DIB (a CF_DIB or CF_DIBV5 payload), or Nothing when the bytes are not a bitmap this program can
    ''' decode.</summary>
    Public Shared Function DibToPng(dib As Byte()) As Byte()
        Try
            If dib Is Nothing OrElse dib.Length < 40 Then Return Nothing
            Dim headerSize As Integer = BitConverter.ToInt32(dib, 0)
            If headerSize < 40 OrElse headerSize > dib.Length Then Return Nothing
            Dim bitCount As Integer = BitConverter.ToInt16(dib, 14)
            Dim compression As Integer = BitConverter.ToInt32(dib, 16)
            Dim colorsUsed As Integer = BitConverter.ToInt32(dib, 32)
            Dim colors As Integer = colorsUsed
            If colors = 0 AndAlso bitCount > 0 AndAlso bitCount <= 8 Then colors = 1 << bitCount
            ' BI_BITFIELDS (3) and BI_ALPHABITFIELDS (6) with a plain 40-byte header carry the masks right after it.
            Dim masks As Integer = 0
            If headerSize = 40 AndAlso compression = 3 Then masks = 12
            If headerSize = 40 AndAlso compression = 6 Then masks = 16
            Dim offBits As Integer = BmpFileHeaderSize + headerSize + masks + colors * 4

            Using bmp As New MemoryStream(BmpFileHeaderSize + dib.Length)
                bmp.WriteByte(CByte(AscW("B"c)))
                bmp.WriteByte(CByte(AscW("M"c)))
                bmp.Write(BitConverter.GetBytes(BmpFileHeaderSize + dib.Length), 0, 4)
                bmp.Write(New Byte(3) {}, 0, 4)
                bmp.Write(BitConverter.GetBytes(offBits), 0, 4)
                bmp.Write(dib, 0, dib.Length)
                bmp.Position = 0
                Using decoded As Image = Image.FromStream(bmp, False, False)
                    Using png As New MemoryStream()
                        decoded.Save(png, ImageFormat.Png)
                        Return png.ToArray()
                    End Using
                End Using
            End Using
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is ExternalException OrElse TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is IOException
            Return Nothing
        End Try
    End Function

    ''' <summary>The packed DIB (what CF_DIB carries, no file header) of a PNG, or Nothing when the bytes are not an image.</summary>
    Public Shared Function PngToDib(png As Byte()) As Byte()
        Try
            If png Is Nothing OrElse png.Length = 0 Then Return Nothing
            Using source As New MemoryStream(png, False)
                Using decoded As Image = Image.FromStream(source, False, False)
                    Using canvas As New Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppArgb)
                        Using g As Graphics = Graphics.FromImage(canvas)
                            g.DrawImage(decoded, 0, 0, decoded.Width, decoded.Height)
                        End Using
                        Using bmp As New MemoryStream()
                            canvas.Save(bmp, ImageFormat.Bmp)
                            Dim all As Byte() = bmp.ToArray()
                            If all.Length <= BmpFileHeaderSize Then Return Nothing
                            Dim dib(all.Length - BmpFileHeaderSize - 1) As Byte
                            Buffer.BlockCopy(all, BmpFileHeaderSize, dib, 0, dib.Length)
                            Return dib
                        End Using
                    End Using
                End Using
            End Using
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is ExternalException OrElse TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is IOException
            Return Nothing
        End Try
    End Function

    ''' <summary>True when the bytes start with the PNG signature.</summary>
    Public Shared Function LooksLikePng(bytes As Byte()) As Boolean
        If bytes Is Nothing OrElse bytes.Length < 8 Then Return False
        Return bytes(0) = &H89 AndAlso bytes(1) = &H50 AndAlso bytes(2) = &H4E AndAlso bytes(3) = &H47 AndAlso
               bytes(4) = &HD AndAlso bytes(5) = &HA AndAlso bytes(6) = &H1A AndAlso bytes(7) = &HA
    End Function

End Class

''' <summary>The production port: raw Win32 on a dedicated thread, under a bound.</summary>
Friend NotInheritable Class ClipboardWin32
    Implements IClipboardPort

    ''' <summary>The bounded retry of CLIPBOARD-GUARD item B: 12 attempts, 15 ms apart.</summary>
    Friend Const OpenAttempts As Integer = 12
    Friend Const OpenPauseMs As Integer = 15

    ''' <summary>The most a text or an image is kept in memory (a file is streamed from the disk and has no such bound).</summary>
    Friend Const MaxInMemoryBytes As Long = 256L * 1024L * 1024L

    Private ReadOnly _readBound As TimeSpan
    Private ReadOnly _placeBound As TimeSpan

    Public Sub New(Optional readBound As TimeSpan = Nothing, Optional placeBound As TimeSpan = Nothing)
        _readBound = If(readBound > TimeSpan.Zero, readBound, TimeSpan.FromSeconds(8))
        _placeBound = If(placeBound > TimeSpan.Zero, placeBound, TimeSpan.FromSeconds(8))
    End Sub

    ' --- the port -------------------------------------------------------------------------------------------

    Public Async Function ReadAsync(ct As CancellationToken) As Task(Of ClipboardSnapshot) Implements IClipboardPort.ReadAsync
        Return Await RunBoundedAsync(Function() ReadNow(), ClipboardSnapshot.OfKind(ClipboardReadKind.TimedOut), _readBound, ct).ConfigureAwait(False)
    End Function

    Public Async Function PlaceTextAsync(utf8 As Byte(), ct As CancellationToken) As Task(Of ClipboardPlaceOutcome) Implements IClipboardPort.PlaceTextAsync
        Dim text As String = New UTF8Encoding(False).GetString(If(utf8, Array.Empty(Of Byte)()))
        Dim bytes As Byte() = Encoding.Unicode.GetBytes(ClipboardText.ToCrLf(text) & vbNullChar)
        Return Await RunBoundedAsync(Function() PlaceNow(New List(Of KeyValuePair(Of UInteger, Byte()))() From {
                                         New KeyValuePair(Of UInteger, Byte())(CF_UNICODETEXT, bytes)}),
                                     ClipboardPlaceOutcome.TimedOut, _placeBound, ct).ConfigureAwait(False)
    End Function

    Public Async Function PlaceImageAsync(png As Byte(), ct As CancellationToken) As Task(Of ClipboardPlaceOutcome) Implements IClipboardPort.PlaceImageAsync
        Return Await RunBoundedAsync(
            Function()
                Dim dib As Byte() = ClipboardImages.PngToDib(png)
                If dib Is Nothing Then Return ClipboardPlaceOutcome.Failed
                Dim formats As New List(Of KeyValuePair(Of UInteger, Byte()))()
                Dim pngFormat As UInteger = RegisterClipboardFormatW("PNG")
                If pngFormat <> 0UI Then formats.Add(New KeyValuePair(Of UInteger, Byte())(pngFormat, png))
                formats.Add(New KeyValuePair(Of UInteger, Byte())(CF_DIB, dib))
                Return PlaceNow(formats)
            End Function, ClipboardPlaceOutcome.TimedOut, _placeBound, ct).ConfigureAwait(False)
    End Function

    Public Async Function PlaceFileAsync(path As String, ct As CancellationToken) As Task(Of ClipboardPlaceOutcome) Implements IClipboardPort.PlaceFileAsync
        Return Await RunBoundedAsync(
            Function()
                Dim full As String
                Try
                    full = System.IO.Path.GetFullPath(path)
                Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException OrElse TypeOf ex Is PathTooLongException
                    Return ClipboardPlaceOutcome.Failed
                End Try
                Dim drop As Byte() = BuildDropFiles(full)
                Dim formats As New List(Of KeyValuePair(Of UInteger, Byte()))()
                formats.Add(New KeyValuePair(Of UInteger, Byte())(CF_HDROP, drop))
                Dim effect As UInteger = RegisterClipboardFormatW("Preferred DropEffect")
                If effect <> 0UI Then formats.Add(New KeyValuePair(Of UInteger, Byte())(effect, BitConverter.GetBytes(DROPEFFECT_COPY)))
                Return PlaceNow(formats)
            End Function, ClipboardPlaceOutcome.TimedOut, _placeBound, ct).ConfigureAwait(False)
    End Function

    ''' <summary>
    ''' Runs <paramref name="work"/> on a thread of its own and waits for it under <paramref name="bound"/>. A thread that
    ''' does not return (a clipboard owner that never renders) is abandoned - it is a background thread, so it ends the
    ''' process nowhere - and <paramref name="whenLate"/> is the answer. The pool is not used: a few such threads must not be
    ''' able to starve it.
    ''' </summary>
    Private Shared Async Function RunBoundedAsync(Of T)(work As Func(Of T), whenLate As T, bound As TimeSpan, ct As CancellationToken) As Task(Of T)
        Dim done As New TaskCompletionSource(Of T)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim worker As New Thread(
            Sub()
                Try
                    done.TrySetResult(work())
                Catch ex As Exception
                    ' Never an exception text into a log from here; the outcome says "late/failed" through the default.
                    done.TrySetResult(whenLate)
                End Try
            End Sub) With {.IsBackground = True, .Name = "FMS clipboard"}
        worker.Start()
        Using cancelTimer As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
            Dim timer As Task = Task.Delay(bound, cancelTimer.Token)
            Dim first As Task = Await Task.WhenAny(done.Task, timer).ConfigureAwait(False)
            If first Is done.Task Then
                cancelTimer.Cancel()
                Return Await done.Task.ConfigureAwait(False)
            End If
            ' Cancelled by the person, not timed out: the coordinator turns this into the Cancelled sentence.
            ct.ThrowIfCancellationRequested()
        End Using
        Return whenLate
    End Function

    ' --- reading (runs on the worker thread) ----------------------------------------------------------------

    Private Shared Function ReadNow() As ClipboardSnapshot
        If Not OpenWithRetry(IntPtr.Zero) Then Return ClipboardSnapshot.OfKind(ClipboardReadKind.Busy)
        Dim filePath As String = Nothing
        Dim pngBytes As Byte() = Nothing
        Dim dibBytes As Byte() = Nothing
        Dim textBytes As Byte() = Nothing
        Dim early As ClipboardSnapshot = Nothing
        Try
            early = ReadFormats(filePath, pngBytes, dibBytes, textBytes)
        Finally
            ' The clipboard is closed exactly once, before the file system, a conversion or the network is touched (item N).
            CloseClipboard()
        End Try
        If early IsNot Nothing Then Return early

        If filePath IsNot Nothing Then
            ' A file drop: the regular-file test and the size are the file system's, after the close.
            Try
                If Directory.Exists(filePath) Then Return ClipboardSnapshot.OfKind(ClipboardReadKind.NotOneObject)
                Dim info As New FileInfo(filePath)
                If Not info.Exists Then Return ClipboardSnapshot.OfKind(ClipboardReadKind.Failed)
                Return New ClipboardSnapshot(ClipboardReadKind.OneFile, Nothing, info.FullName, info.Length)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException
                Return ClipboardSnapshot.OfKind(ClipboardReadKind.Failed)
            End Try
        End If
        Return Build(pngBytes, dibBytes, textBytes)
    End Function

    ''' <summary>Everything one value needs, copied while the clipboard is open: the kind is decided here (a file drop, else an
    ''' image, else text). Returns a snapshot only when the read already has its answer (several files, too large).</summary>
    Private Shared Function ReadFormats(ByRef filePath As String, ByRef pngBytes As Byte(), ByRef dibBytes As Byte(), ByRef textBytes As Byte()) As ClipboardSnapshot
        ' 1. A file drop: one entry is the file kind, several are refused (item BU).
        If IsClipboardFormatAvailable(CF_HDROP) Then
            Dim hDrop As IntPtr = GetClipboardData(CF_HDROP)
            If hDrop <> IntPtr.Zero Then
                Dim count As UInteger = DragQueryFileW(hDrop, &HFFFFFFFFUI, Nothing, 0UI)
                If count > 1UI Then Return ClipboardSnapshot.OfKind(ClipboardReadKind.NotOneObject)
                If count = 1UI Then
                    Dim length As UInteger = DragQueryFileW(hDrop, 0UI, Nothing, 0UI)
                    If length = 0UI OrElse length > 32767UI Then Return ClipboardSnapshot.OfKind(ClipboardReadKind.Failed)
                    Dim name As New StringBuilder(CInt(length) + 1)
                    DragQueryFileW(hDrop, 0UI, name, length + 1UI)
                    filePath = name.ToString()
                    Return Nothing
                End If
            End If
        End If

        ' 2. An image: the registered PNG, else CF_DIBV5, else CF_DIB.
        Dim tooLarge As Boolean = False
        Dim pngFormat As UInteger = RegisterClipboardFormatW("PNG")
        If pngFormat <> 0UI AndAlso IsClipboardFormatAvailable(pngFormat) Then
            pngBytes = CopyGlobal(GetClipboardData(pngFormat), tooLarge)
            If pngBytes IsNot Nothing AndAlso Not ClipboardImages.LooksLikePng(pngBytes) Then pngBytes = Nothing
        End If
        If pngBytes Is Nothing AndAlso Not tooLarge Then
            If IsClipboardFormatAvailable(CF_DIBV5) Then dibBytes = CopyGlobal(GetClipboardData(CF_DIBV5), tooLarge)
            If dibBytes Is Nothing AndAlso Not tooLarge AndAlso IsClipboardFormatAvailable(CF_DIB) Then
                dibBytes = CopyGlobal(GetClipboardData(CF_DIB), tooLarge)
            End If
        End If
        If tooLarge Then Return ClipboardSnapshot.OfKind(ClipboardReadKind.TooLarge)

        ' 3. Text, in the same open: one value is one moment of the clipboard.
        If pngBytes Is Nothing AndAlso dibBytes Is Nothing AndAlso IsClipboardFormatAvailable(CF_UNICODETEXT) Then
            textBytes = CopyGlobal(GetClipboardData(CF_UNICODETEXT), tooLarge)
            If tooLarge Then Return ClipboardSnapshot.OfKind(ClipboardReadKind.TooLarge)
        End If
        Return Nothing
    End Function

    Private Shared Function Build(png As Byte(), dib As Byte(), textBytes As Byte()) As ClipboardSnapshot
        If png IsNot Nothing Then
            Return New ClipboardSnapshot(ClipboardReadKind.Image, png, "", png.LongLength)
        End If
        If dib IsNot Nothing Then
            Dim converted As Byte() = ClipboardImages.DibToPng(dib)
            If converted Is Nothing Then Return ClipboardSnapshot.OfKind(ClipboardReadKind.Failed)
            Return New ClipboardSnapshot(ClipboardReadKind.Image, converted, "", converted.LongLength)
        End If
        If textBytes IsNot Nothing Then
            ' UTF-16 up to the first NUL.
            Dim units As Integer = 0
            While units + 1 < textBytes.Length AndAlso (textBytes(units) <> 0 OrElse textBytes(units + 1) <> 0)
                units += 2
            End While
            Dim text As String = Encoding.Unicode.GetString(textBytes, 0, units)
            If text.Length = 0 Then Return ClipboardSnapshot.OfKind(ClipboardReadKind.Empty)
            Dim wire As Byte() = ClipboardText.ToWireBytes(text)
            Return New ClipboardSnapshot(ClipboardReadKind.Text, wire, "", wire.LongLength)
        End If
        Return ClipboardSnapshot.OfKind(ClipboardReadKind.Empty)
    End Function

    ''' <summary>A copy of a global block, or Nothing; <paramref name="tooLarge"/> when it is past the memory bound.</summary>
    Private Shared Function CopyGlobal(handle As IntPtr, ByRef tooLarge As Boolean) As Byte()
        If handle = IntPtr.Zero Then Return Nothing
        Dim size As Long = CLng(GlobalSize(handle).ToUInt64())
        If size <= 0L Then Return Nothing
        If size > MaxInMemoryBytes Then
            tooLarge = True
            Return Nothing
        End If
        Dim locked As IntPtr = GlobalLock(handle)
        If locked = IntPtr.Zero Then Return Nothing
        Try
            Dim bytes(CInt(size) - 1) As Byte
            Marshal.Copy(locked, bytes, 0, CInt(size))
            Return bytes
        Finally
            GlobalUnlock(handle)
        End Try
    End Function

    ' --- placing (runs on the worker thread) ----------------------------------------------------------------

    ''' <summary>One open-empty-set-close pass over every format. The owner is a message-only window of this thread: a NULL owner
    ''' makes SetClipboardData fail, and a window that outlives the pass would have to answer the clipboard-destroyed message
    ''' from a thread that does not pump.</summary>
    Private Shared Function PlaceNow(formats As List(Of KeyValuePair(Of UInteger, Byte()))) As ClipboardPlaceOutcome
        Dim owner As IntPtr = CreateWindowExW(0UI, "STATIC", "", 0UI, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero)
        If owner = IntPtr.Zero Then Return ClipboardPlaceOutcome.Failed
        Try
            If Not OpenWithRetry(owner) Then Return ClipboardPlaceOutcome.Busy
            Try
                If Not EmptyClipboard() Then Return ClipboardPlaceOutcome.Failed
                For Each item As KeyValuePair(Of UInteger, Byte()) In formats
                    Dim handle As IntPtr = ToGlobal(item.Value)
                    If handle = IntPtr.Zero Then Return ClipboardPlaceOutcome.Failed
                    If SetClipboardData(item.Key, handle) = IntPtr.Zero Then
                        GlobalFree(handle)
                        Return ClipboardPlaceOutcome.Failed
                    End If
                    ' Owned by the system from here on.
                Next
                Return ClipboardPlaceOutcome.Placed
            Finally
                CloseClipboard()
            End Try
        Finally
            DestroyWindow(owner)
        End Try
    End Function

    Private Shared Function ToGlobal(bytes As Byte()) As IntPtr
        If bytes Is Nothing Then Return IntPtr.Zero
        Dim handle As IntPtr = GlobalAlloc(GMEM_MOVEABLE, New UIntPtr(CULng(Math.Max(bytes.LongLength, 1L))))
        If handle = IntPtr.Zero Then Return IntPtr.Zero
        Dim locked As IntPtr = GlobalLock(handle)
        If locked = IntPtr.Zero Then
            GlobalFree(handle)
            Return IntPtr.Zero
        End If
        Try
            If bytes.Length > 0 Then Marshal.Copy(bytes, 0, locked, bytes.Length)
        Finally
            GlobalUnlock(handle)
        End Try
        Return handle
    End Function

    ''' <summary>DROPFILES (20 bytes: offset, point, non-client flag, wide flag) and one double-NUL-terminated wide path.</summary>
    Friend Shared Function BuildDropFiles(fullPath As String) As Byte()
        Dim path As Byte() = Encoding.Unicode.GetBytes(fullPath & vbNullChar & vbNullChar)
        Dim bytes(19 + path.Length) As Byte
        BitConverter.GetBytes(20UI).CopyTo(bytes, 0)
        ' pt.x, pt.y, fNC stay 0
        BitConverter.GetBytes(1).CopyTo(bytes, 16)
        Buffer.BlockCopy(path, 0, bytes, 20, path.Length)
        Return bytes
    End Function

    Private Shared Function OpenWithRetry(owner As IntPtr) As Boolean
        For attempt As Integer = 1 To OpenAttempts
            If OpenClipboard(owner) Then Return True
            If attempt < OpenAttempts Then Thread.Sleep(OpenPauseMs)
        Next
        Return False
    End Function

    ' --- Win32 ----------------------------------------------------------------------------------------------

    Private Const CF_DIB As UInteger = 8UI
    Private Const CF_UNICODETEXT As UInteger = 13UI
    Private Const CF_HDROP As UInteger = 15UI
    Private Const CF_DIBV5 As UInteger = 17UI
    Private Const GMEM_MOVEABLE As UInteger = 2UI
    Private Const DROPEFFECT_COPY As Integer = 1
    Private Shared ReadOnly HWND_MESSAGE As New IntPtr(-3)

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function OpenClipboard(hWndNewOwner As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function CloseClipboard() As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function EmptyClipboard() As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsClipboardFormatAvailable(format As UInteger) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetClipboardData(uFormat As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetClipboardData(uFormat As UInteger, hMem As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function RegisterClipboardFormatW(lpszFormat As String) As UInteger
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function CreateWindowExW(dwExStyle As UInteger, lpClassName As String, lpWindowName As String, dwStyle As UInteger,
                                            x As Integer, y As Integer, nWidth As Integer, nHeight As Integer,
                                            hWndParent As IntPtr, hMenu As IntPtr, hInstance As IntPtr, lpParam As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function DestroyWindow(hWnd As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function GlobalAlloc(uFlags As UInteger, dwBytes As UIntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function GlobalFree(hMem As IntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function GlobalLock(hMem As IntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function GlobalUnlock(hMem As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function GlobalSize(hMem As IntPtr) As UIntPtr
    End Function

    <DllImport("shell32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function DragQueryFileW(hDrop As IntPtr, iFile As UInteger, lpszFile As StringBuilder, cch As UInteger) As UInteger
    End Function

End Class
