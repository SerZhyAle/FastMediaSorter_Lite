#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>What a read of the clipboard for the paste of a code or a QR image found.</summary>
Friend Enum ClipboardPasteOutcome
    Busy = 0
    Empty = 1
    Image = 2
    ImageTooLarge = 3
    Text = 4
    TextTooLarge = 5
    NoAnswer = 6
    UnderWay = 7
    Unreadable = 8
End Enum

''' <summary>The outcome, with the image as a BMP file or the text. The text holds a secret and is never logged
''' (spec 227 rule 6, CLIPBOARD-GUARD item R).</summary>
Friend NotInheritable Class ClipboardPasteResult
    Public ReadOnly Property Outcome As ClipboardPasteOutcome
    Public ReadOnly Property Bmp As Byte()
    Public ReadOnly Property Text As String

    Friend Sub New(outcome As ClipboardPasteOutcome, Optional bmp As Byte() = Nothing, Optional text As String = "")
        Me.Outcome = outcome
        Me.Bmp = bmp
        Me.Text = If(text, "")
    End Sub

    Public Overrides Function ToString() As String
        Return "ClipboardPasteResult{" & Outcome.ToString() & "}"
    End Function
End Class

''' <summary>
''' The one read of the clipboard behind the paste of a code or a QR image (CLIPBOARD-GUARD items B, C and N, rule 3). The calls are
''' the raw Win32 ones of the contract, on a worker of their own: the open keeps its pinned schedule, a size is compared with its
''' cap through GlobalSize before one byte is copied, and the clipboard is closed again before anything decodes. The image still
''' comes before the text: the order follows the answer to the read-order proposal of CLIPBOARD-GUARD, not this reader. CF_DIB is
''' the only image format, because Windows renders it from what a screenshot places.
''' </summary>
Friend NotInheritable Class ClipboardPaste

    Private Sub New()
    End Sub

    Friend Const OpenAttempts As Integer = 12
    Friend Const OpenPauseMs As Integer = 15

    ''' <summary>The contract's ceiling of a CF_DIB. It lies above 16 MP at 32 bits per pixel, so nothing the QR search would take is
    ''' refused here first.</summary>
    Friend Const MaxImageBytes As Long = 64L * 1024L * 1024L

    ''' <summary>A code or a connection string is a few KB at most: a QR code itself holds under 3 KB.</summary>
    Friend Const MaxTextBytes As Long = 1024L * 1024L

    ''' <summary>What a size reader answers for a format the clipboard does not hold.</summary>
    Friend Const NoData As Long = -1L

    Friend Const FormatDib As UInteger = 8UI
    Friend Const FormatUnicodeText As UInteger = 13UI

    Private Const BmpFileHeaderSize As Integer = 14

    ''' <summary>Item N: how long a window waits for a program that renders its format on demand and does not answer.</summary>
    Friend Shared ReadOnly ReadBound As TimeSpan = TimeSpan.FromSeconds(5)

    Private Shared _reading As Integer

    ''' <summary>One read at a time, an abandoned one included: Windows ends it about 30 s after the owner stopped answering.</summary>
    Friend Shared ReadOnly Property ReadInProgress As Boolean
        Get
            Return Volatile.Read(_reading) <> 0
        End Get
    End Property

    ''' <summary>Item N: the read off the caller's thread, so no window waits on another program. It answers inside the bound.</summary>
    Friend Shared Function ReadBoundedAsync() As Task(Of ClipboardPasteResult)
        Return RunBoundedAsync(AddressOf Read, ReadBound)
    End Function

    ''' <summary>Runs <paramref name="work"/> on a background thread of its own, not the pool: a few reads held by an owner that does
    ''' not answer must not be able to starve it. A read past the bound is abandoned and says so. No exception text leaves here.</summary>
    Friend Shared Async Function RunBoundedAsync(work As Func(Of ClipboardPasteResult), bound As TimeSpan) As Task(Of ClipboardPasteResult)
        If Interlocked.CompareExchange(_reading, 1, 0) <> 0 Then Return New ClipboardPasteResult(ClipboardPasteOutcome.UnderWay)
        Dim done As New TaskCompletionSource(Of ClipboardPasteResult)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim worker As New Thread(
            Sub()
                Dim result As ClipboardPasteResult
                Try
                    result = work()
                Catch ex As Exception
                    result = New ClipboardPasteResult(ClipboardPasteOutcome.Unreadable)
                End Try
                Interlocked.Exchange(_reading, 0)
                done.TrySetResult(result)
            End Sub) With {.IsBackground = True, .Name = "FMS clipboard paste"}
        worker.Start()
        Dim first As Task = Await Task.WhenAny(done.Task, Task.Delay(bound)).ConfigureAwait(False)
        If first Is done.Task Then Return Await done.Task.ConfigureAwait(False)
        Return New ClipboardPasteResult(ClipboardPasteOutcome.NoAnswer)
    End Function

    Friend Shared Function Read() As ClipboardPasteResult
        If Not OpenWithRetry(Function() OpenClipboard(IntPtr.Zero), Sub(ms) Thread.Sleep(ms)) Then
            Return New ClipboardPasteResult(ClipboardPasteOutcome.Busy)
        End If
        Try
            Return ReadOpened(AddressOf SizeOfFormat, AddressOf CopyFormat)
        Finally
            CloseClipboard()
        End Try
    End Function

    ''' <summary>Item B: the attempts and the pause are the contract's, and the pause never follows the last attempt.</summary>
    Friend Shared Function OpenWithRetry(tryOpen As Func(Of Boolean), pause As Action(Of Integer)) As Boolean
        For attempt As Integer = 1 To OpenAttempts
            If tryOpen() Then Return True
            If attempt < OpenAttempts Then pause(OpenPauseMs)
        Next
        Return False
    End Function

    ''' <summary>The read of an open clipboard. Item C: <paramref name="copy"/> runs only for a size at or under its cap.</summary>
    Friend Shared Function ReadOpened(sizeOf As Func(Of UInteger, Long), copy As Func(Of UInteger, Integer, Byte())) As ClipboardPasteResult
        Dim imageSize As Long = sizeOf(FormatDib)
        If imageSize > MaxImageBytes Then Return New ClipboardPasteResult(ClipboardPasteOutcome.ImageTooLarge)
        If imageSize > 0 Then
            Dim bmp As Byte() = BmpOfDib(copy(FormatDib, CInt(imageSize)))
            If bmp IsNot Nothing Then Return New ClipboardPasteResult(ClipboardPasteOutcome.Image, bmp:=bmp)
        End If
        Dim textSize As Long = sizeOf(FormatUnicodeText)
        If textSize > MaxTextBytes Then Return New ClipboardPasteResult(ClipboardPasteOutcome.TextTooLarge)
        If textSize > 0 Then
            Dim text As String = TextOf(copy(FormatUnicodeText, CInt(textSize)))
            If text.Length > 0 Then Return New ClipboardPasteResult(ClipboardPasteOutcome.Text, text:=text)
        End If
        Return New ClipboardPasteResult(ClipboardPasteOutcome.Empty)
    End Function

    ''' <summary>Rule 3: the bound is the size GlobalSize gave; the text ends at the first NUL inside it. An odd last byte is no character.</summary>
    Friend Shared Function TextOf(bytes As Byte()) As String
        If bytes Is Nothing Then Return ""
        Dim chars As Integer = bytes.Length \ 2
        For i As Integer = 0 To chars - 1
            If bytes(2 * i) = 0 AndAlso bytes(2 * i + 1) = 0 Then
                chars = i
                Exit For
            End If
        Next
        Return Encoding.Unicode.GetString(bytes, 0, chars * 2)
    End Function

    ''' <summary>A packed DIB (what CF_DIB carries) as a BMP file, or Nothing when its header does not fit its bytes. The header comes
    ''' from another program, so the offset is computed in Long and compared with the real length.</summary>
    Friend Shared Function BmpOfDib(dib As Byte()) As Byte()
        If dib Is Nothing OrElse dib.Length < 40 Then Return Nothing
        Dim headerSize As Integer = BitConverter.ToInt32(dib, 0)
        If headerSize < 40 OrElse headerSize > dib.Length Then Return Nothing
        Dim bitCount As Integer = BitConverter.ToInt16(dib, 14)
        Dim compression As Integer = BitConverter.ToInt32(dib, 16)
        Dim colors As Long = BitConverter.ToInt32(dib, 32)
        If colors < 0L Then Return Nothing
        If colors = 0L AndAlso bitCount > 0 AndAlso bitCount <= 8 Then colors = 1L << bitCount
        ' BI_BITFIELDS (3) and BI_ALPHABITFIELDS (6) with a plain 40-byte header carry the masks right after it
        Dim masks As Long = 0L
        If headerSize = 40 AndAlso compression = 3 Then masks = 12L
        If headerSize = 40 AndAlso compression = 6 Then masks = 16L
        Dim pixelsAt As Long = headerSize + masks + colors * 4L
        If pixelsAt > dib.Length Then Return Nothing
        If compression = 0 OrElse compression = 3 OrElse compression = 6 Then
            ' uncompressed rows have a known length, so a canvas its bytes cannot fill never reaches the decoder
            Dim width As Long = BitConverter.ToInt32(dib, 4)
            Dim height As Long = Math.Abs(CLng(BitConverter.ToInt32(dib, 8)))
            If width <= 0L OrElse height = 0L OrElse bitCount <= 0 Then Return Nothing
            Dim stride As Long = ((width * bitCount + 31L) \ 32L) * 4L
            If stride > dib.Length OrElse pixelsAt + stride * height > dib.Length Then Return Nothing
        End If

        Dim bmp(BmpFileHeaderSize + dib.Length - 1) As Byte
        bmp(0) = CByte(AscW("B"c))
        bmp(1) = CByte(AscW("M"c))
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2)
        BitConverter.GetBytes(CInt(BmpFileHeaderSize + pixelsAt)).CopyTo(bmp, 10)
        Buffer.BlockCopy(dib, 0, bmp, BmpFileHeaderSize, dib.Length)
        Return bmp
    End Function

    Private Shared Function SizeOfFormat(format As UInteger) As Long
        If Not IsClipboardFormatAvailable(format) Then Return NoData
        Dim handle As IntPtr = GetClipboardData(format)
        If handle = IntPtr.Zero Then Return NoData
        Return CLng(GlobalSize(handle).ToUInt64())
    End Function

    Private Shared Function CopyFormat(format As UInteger, size As Integer) As Byte()
        Dim handle As IntPtr = GetClipboardData(format)
        If handle = IntPtr.Zero Then Return Nothing
        Dim source As IntPtr = GlobalLock(handle)
        If source = IntPtr.Zero Then Return Nothing
        Try
            Dim bytes(size - 1) As Byte
            Marshal.Copy(source, bytes, 0, size)
            Return bytes
        Finally
            GlobalUnlock(handle)
        End Try
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function OpenClipboard(hWndNewOwner As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function CloseClipboard() As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsClipboardFormatAvailable(format As UInteger) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetClipboardData(uFormat As UInteger) As IntPtr
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

End Class
#End If
