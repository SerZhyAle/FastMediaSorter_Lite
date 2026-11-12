#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' CLIPBOARD-GUARD items B, C and N and rule 3 on the read behind the paste of a code or a QR image (L0252, exception CR-19): the pinned
''' open schedule, a read on a worker under its bound, a cap compared with the size before a byte is copied, the text bound by that
''' size, a header from another program that does not fit its bytes. Only the holder of the last fact touches the real clipboard, and
''' it writes nothing to it.
''' </summary>
Public Class ClipboardPasteTests

    ' ---------------------------------------------------------------- the open ---

    <Fact>
    Public Sub A_held_clipboard_is_tried_twelve_times_fifteen_milliseconds_apart()
        Dim attempts As Integer = 0
        Dim pauses As New List(Of Integer)()
        Dim opened As Boolean = ClipboardPaste.OpenWithRetry(
            Function()
                attempts += 1
                Return False
            End Function,
            Sub(ms) pauses.Add(ms))
        Assert.False(opened)
        Assert.Equal(12, attempts)
        Assert.Equal(11, pauses.Count)
        Assert.True(pauses.All(Function(ms) ms = 15))
    End Sub

    <Fact>
    Public Sub The_attempts_end_on_the_first_that_opens()
        Dim attempts As Integer = 0
        Dim pauses As Integer = 0
        Dim opened As Boolean = ClipboardPaste.OpenWithRetry(
            Function()
                attempts += 1
                Return attempts = 3
            End Function,
            Sub(ms) pauses += 1)
        Assert.True(opened)
        Assert.Equal(3, attempts)
        Assert.Equal(2, pauses)
    End Sub

    ' ---------------------------------------------------------------- no hangs ---

    Private Shared Sub AssertTheReaderEnds()
        Assert.True(SpinWait.SpinUntil(Function() Not ClipboardPaste.ReadInProgress, TimeSpan.FromSeconds(5)), "the reader did not end")
    End Sub

    <Fact>
    Public Sub The_bound_of_an_owner_that_does_not_answer_is_the_contracts()
        Assert.Equal(TimeSpan.FromSeconds(5), ClipboardPaste.ReadBound)
    End Sub

    <Fact>
    Public Async Function The_read_runs_on_a_background_thread_of_its_own() As Task
        Dim caller As Integer = Environment.CurrentManagedThreadId
        Dim reader As Integer = caller
        Dim background As Boolean = False
        Dim pooled As Boolean = True
        Dim result As ClipboardPasteResult = Await ClipboardPaste.RunBoundedAsync(
            Function()
                reader = Environment.CurrentManagedThreadId
                background = Thread.CurrentThread.IsBackground
                pooled = Thread.CurrentThread.IsThreadPoolThread
                Return New ClipboardPasteResult(ClipboardPasteOutcome.Empty)
            End Function, TimeSpan.FromSeconds(5))
        Assert.Equal(ClipboardPasteOutcome.Empty, result.Outcome)
        Assert.NotEqual(caller, reader)
        Assert.True(background)
        Assert.False(pooled)
        Assert.False(ClipboardPaste.ReadInProgress)
    End Function

    <Fact>
    Public Async Function A_read_past_the_bound_is_abandoned_and_a_second_press_hears_it_is_under_way() As Task
        Dim release As New ManualResetEventSlim(False)
        Dim late As ClipboardPasteResult = Await ClipboardPaste.RunBoundedAsync(
            Function()
                release.Wait(TimeSpan.FromSeconds(3))
                Return New ClipboardPasteResult(ClipboardPasteOutcome.Empty)
            End Function, TimeSpan.FromMilliseconds(50))
        Assert.Equal(ClipboardPasteOutcome.NoAnswer, late.Outcome)

        Dim ran As Boolean = False
        Dim second As ClipboardPasteResult = Await ClipboardPaste.RunBoundedAsync(
            Function()
                ran = True
                Return New ClipboardPasteResult(ClipboardPasteOutcome.Empty)
            End Function, TimeSpan.FromSeconds(1))
        Assert.Equal(ClipboardPasteOutcome.UnderWay, second.Outcome)
        Assert.False(ran)

        release.Set()
        AssertTheReaderEnds()
        Dim third As ClipboardPasteResult = Await ClipboardPaste.RunBoundedAsync(
            Function() New ClipboardPasteResult(ClipboardPasteOutcome.Empty), TimeSpan.FromSeconds(1))
        Assert.Equal(ClipboardPasteOutcome.Empty, third.Outcome)
    End Function

    <Fact>
    Public Async Function A_read_that_throws_says_unreadable_and_frees_the_next_one() As Task
        Dim result As ClipboardPasteResult = Await ClipboardPaste.RunBoundedAsync(
            Function() As ClipboardPasteResult
                Throw New InvalidOperationException("secret-code")
            End Function, TimeSpan.FromSeconds(5))
        Assert.Equal(ClipboardPasteOutcome.Unreadable, result.Outcome)
        Assert.Equal("", result.Text)
        Assert.False(ClipboardPaste.ReadInProgress)
    End Function

    ' ---------------------------------------------------------------- the caps and the order ---

    Private Shared Function Sizes(image As Long, text As Long) As Func(Of UInteger, Long)
        Return Function(format) If(format = ClipboardPaste.FormatDib, image, text)
    End Function

    <Fact>
    Public Sub An_image_over_its_cap_is_refused_before_a_byte_is_copied()
        Dim copies As Integer = 0
        Dim result As ClipboardPasteResult = ClipboardPaste.ReadOpened(
            Sizes(ClipboardPaste.MaxImageBytes + 1L, 100L),
            Function(format, size)
                copies += 1
                Return Nothing
            End Function)
        Assert.Equal(ClipboardPasteOutcome.ImageTooLarge, result.Outcome)
        Assert.Equal(0, copies)
    End Sub

    <Fact>
    Public Sub A_text_over_its_cap_is_refused_before_a_byte_is_copied()
        Dim copies As Integer = 0
        Dim result As ClipboardPasteResult = ClipboardPaste.ReadOpened(
            Sizes(ClipboardPaste.NoData, ClipboardPaste.MaxTextBytes + 1L),
            Function(format, size)
                copies += 1
                Return Nothing
            End Function)
        Assert.Equal(ClipboardPasteOutcome.TextTooLarge, result.Outcome)
        Assert.Equal(0, copies)
    End Sub

    <Fact>
    Public Sub A_size_exactly_at_its_cap_is_read_with_that_size()
        Dim askedImage As Integer = 0
        Dim image As ClipboardPasteResult = ClipboardPaste.ReadOpened(
            Sizes(ClipboardPaste.MaxImageBytes, ClipboardPaste.NoData),
            Function(format, size)
                askedImage = size
                Return Pixel24()
            End Function)
        Assert.Equal(ClipboardPasteOutcome.Image, image.Outcome)
        Assert.Equal(CInt(ClipboardPaste.MaxImageBytes), askedImage)

        Dim askedText As Integer = 0
        Dim text As ClipboardPasteResult = ClipboardPaste.ReadOpened(
            Sizes(ClipboardPaste.NoData, ClipboardPaste.MaxTextBytes),
            Function(format, size)
                askedText = size
                Return Encoding.Unicode.GetBytes("code")
            End Function)
        Assert.Equal(ClipboardPasteOutcome.Text, text.Outcome)
        Assert.Equal("code", text.Text)
        Assert.Equal(CInt(ClipboardPaste.MaxTextBytes), askedText)
    End Sub

    <Fact>
    Public Sub The_image_comes_before_the_text()
        Dim asked As New List(Of UInteger)()
        Dim dib As Byte() = Pixel24()
        Dim result As ClipboardPasteResult = ClipboardPaste.ReadOpened(
            Sizes(dib.Length, 100L),
            Function(format, size)
                asked.Add(format)
                Return dib
            End Function)
        Assert.Equal(ClipboardPasteOutcome.Image, result.Outcome)
        Assert.NotNull(result.Bmp)
        Assert.Equal(New UInteger() {ClipboardPaste.FormatDib}, asked.ToArray())
    End Sub

    <Fact>
    Public Sub An_image_that_does_not_fit_its_bytes_leaves_the_text_to_be_read()
        Dim result As ClipboardPasteResult = ClipboardPaste.ReadOpened(
            Sizes(4L, 8L),
            Function(format, size)
                If format = ClipboardPaste.FormatDib Then Return New Byte(3) {}
                Return Encoding.Unicode.GetBytes("code")
            End Function)
        Assert.Equal(ClipboardPasteOutcome.Text, result.Outcome)
        Assert.Equal("code", result.Text)
    End Sub

    <Fact>
    Public Sub Nothing_on_the_clipboard_copies_nothing()
        Dim copies As Integer = 0
        Dim result As ClipboardPasteResult = ClipboardPaste.ReadOpened(
            Sizes(ClipboardPaste.NoData, ClipboardPaste.NoData),
            Function(format, size)
                copies += 1
                Return Nothing
            End Function)
        Assert.Equal(ClipboardPasteOutcome.Empty, result.Outcome)
        Assert.Equal(0, copies)
    End Sub

    <Fact>
    Public Sub A_result_prints_its_outcome_and_never_its_text()
        Dim result As New ClipboardPasteResult(ClipboardPasteOutcome.Text, text:="secret-code")
        Assert.DoesNotContain("secret-code", result.ToString())
        Assert.Contains("Text", result.ToString())
    End Sub

    ' ---------------------------------------------------------------- the text ---

    <Fact>
    Public Sub The_text_ends_at_the_first_nul_inside_the_size()
        Assert.Equal("abc", ClipboardPaste.TextOf(Encoding.Unicode.GetBytes("abc" & ChrW(0) & "not this")))
    End Sub

    <Fact>
    Public Sub A_text_with_no_nul_ends_at_the_size()
        Assert.Equal("abc", ClipboardPaste.TextOf(Encoding.Unicode.GetBytes("abc")))
    End Sub

    <Fact>
    Public Sub An_odd_last_byte_is_no_character()
        Dim bytes As Byte() = Encoding.Unicode.GetBytes("ab").Concat(New Byte() {65}).ToArray()
        Assert.Equal("ab", ClipboardPaste.TextOf(bytes))
    End Sub

    <Fact>
    Public Sub No_bytes_are_no_text()
        Assert.Equal("", ClipboardPaste.TextOf(Nothing))
        Assert.Equal("", ClipboardPaste.TextOf(Array.Empty(Of Byte)()))
    End Sub

    ' ---------------------------------------------------------------- the image ---

    Private Shared Sub WriteHeader(dib As Byte(), width As Integer, height As Integer, bitCount As Short, compression As Integer, colorsUsed As Integer)
        BitConverter.GetBytes(40).CopyTo(dib, 0)
        BitConverter.GetBytes(width).CopyTo(dib, 4)
        BitConverter.GetBytes(height).CopyTo(dib, 8)
        BitConverter.GetBytes(CShort(1)).CopyTo(dib, 12)
        BitConverter.GetBytes(bitCount).CopyTo(dib, 14)
        BitConverter.GetBytes(compression).CopyTo(dib, 16)
        BitConverter.GetBytes(colorsUsed).CopyTo(dib, 32)
    End Sub

    ''' <summary>One blue-green-red pixel: 40 header bytes and one row of four.</summary>
    Private Shared Function Pixel24() As Byte()
        Dim dib(43) As Byte
        WriteHeader(dib, 1, 1, 24, 0, 0)
        dib(40) = 30
        dib(41) = 20
        dib(42) = 10
        Return dib
    End Function

    ''' <summary>The picture as a packed 24-bit DIB, bottom-up as Windows places one.</summary>
    Private Shared Function Dib24Of(picture As Bitmap) As Byte()
        Dim stride As Integer = ((picture.Width * 24 + 31) \ 32) * 4
        Dim dib(40 + stride * picture.Height - 1) As Byte
        WriteHeader(dib, picture.Width, picture.Height, 24, 0, 0)
        For y As Integer = 0 To picture.Height - 1
            Dim row As Integer = 40 + stride * (picture.Height - 1 - y)
            For x As Integer = 0 To picture.Width - 1
                Dim ink As Color = picture.GetPixel(x, y)
                dib(row + 3 * x) = ink.B
                dib(row + 3 * x + 1) = ink.G
                dib(row + 3 * x + 2) = ink.R
            Next
        Next
        Return dib
    End Function

    Private Shared Function PixelOf(bmp As Byte(), x As Integer, y As Integer) As Color
        Using ms As New MemoryStream(bmp, writable:=False)
            Using picture As New Bitmap(ms)
                Return picture.GetPixel(x, y)
            End Using
        End Using
    End Function

    Private Shared Sub AssertRgb(red As Integer, green As Integer, blue As Integer, actual As Color)
        Assert.Equal(red, CInt(actual.R))
        Assert.Equal(green, CInt(actual.G))
        Assert.Equal(blue, CInt(actual.B))
    End Sub

    <Fact>
    Public Sub A_plain_dib_becomes_a_file_the_decoder_reads()
        Dim bmp As Byte() = ClipboardPaste.BmpOfDib(Pixel24())
        Assert.NotNull(bmp)
        Assert.Equal(bmp.Length, BitConverter.ToInt32(bmp, 2))
        Assert.Equal(14 + 40, BitConverter.ToInt32(bmp, 10))
        AssertRgb(10, 20, 30, PixelOf(bmp, 0, 0))
    End Sub

    <Fact>
    Public Sub A_palette_named_by_its_count_comes_before_the_pixels()
        Dim dib(40 + 8 + 4 - 1) As Byte
        WriteHeader(dib, 2, 1, 8, 0, 2)
        dib(40 + 2) = 255  ' entry 0 red, in blue-green-red order
        dib(40 + 4) = 255  ' entry 1 blue
        dib(48) = 0
        dib(49) = 1
        Dim bmp As Byte() = ClipboardPaste.BmpOfDib(dib)
        Assert.NotNull(bmp)
        Assert.Equal(14 + 40 + 8, BitConverter.ToInt32(bmp, 10))
        AssertRgb(255, 0, 0, PixelOf(bmp, 0, 0))
        AssertRgb(0, 0, 255, PixelOf(bmp, 1, 0))
    End Sub

    <Fact>
    Public Sub A_zero_colour_count_means_the_whole_palette_of_the_bit_depth()
        Dim dib(40 + 1024 + 4 - 1) As Byte
        WriteHeader(dib, 1, 1, 8, 0, 0)
        dib(40 + 1) = 200  ' entry 0 green
        Dim bmp As Byte() = ClipboardPaste.BmpOfDib(dib)
        Assert.NotNull(bmp)
        Assert.Equal(14 + 40 + 1024, BitConverter.ToInt32(bmp, 10))
        AssertRgb(0, 200, 0, PixelOf(bmp, 0, 0))
    End Sub

    <Fact>
    Public Sub Bit_field_masks_after_a_plain_header_come_before_the_pixels()
        Dim dib(40 + 12 + 4 - 1) As Byte
        WriteHeader(dib, 1, 1, 32, 3, 0)
        BitConverter.GetBytes(&HFF0000).CopyTo(dib, 40)
        BitConverter.GetBytes(&HFF00).CopyTo(dib, 44)
        BitConverter.GetBytes(&HFF).CopyTo(dib, 48)
        dib(52) = 30
        dib(53) = 20
        dib(54) = 10
        Dim bmp As Byte() = ClipboardPaste.BmpOfDib(dib)
        Assert.NotNull(bmp)
        Assert.Equal(14 + 40 + 12, BitConverter.ToInt32(bmp, 10))
        AssertRgb(10, 20, 30, PixelOf(bmp, 0, 0))
    End Sub

    <Fact>
    Public Sub A_top_down_dib_keeps_its_rows()
        Dim dib As Byte() = Pixel24()
        BitConverter.GetBytes(-1).CopyTo(dib, 8)
        Assert.NotNull(ClipboardPaste.BmpOfDib(dib))
    End Sub

    <Theory>
    <InlineData(39, 40, 1, 1, 24, 0)>     ' shorter than any header
    <InlineData(44, 12, 1, 1, 24, 0)>     ' a header size below the plain one
    <InlineData(44, 45, 1, 1, 24, 0)>     ' a header size past the bytes
    <InlineData(44, 40, 1, 1, 24, Integer.MaxValue)>  ' a colour count whose palette would overflow an Int32 offset
    <InlineData(44, 40, 1, 1, 24, -1)>    ' a negative colour count
    <InlineData(44, 40, 2, 2, 24, 0)>     ' more rows than bytes
    <InlineData(44, 40, 50000, 50000, 24, 0)>  ' a canvas far past the bytes
    <InlineData(44, 40, Integer.MaxValue, Integer.MaxValue, 32, 0)>  ' a canvas whose size would overflow a Long
    <InlineData(44, 40, -1, 1, 24, 0)>    ' a negative width
    <InlineData(44, 40, 1, 0, 24, 0)>     ' no rows
    <InlineData(44, 40, 1, 1, 0, 0)>      ' no bits per pixel
    Public Sub A_header_that_does_not_fit_its_bytes_is_refused(length As Integer, headerSize As Integer, width As Integer, height As Integer, bitCount As Integer, colorsUsed As Integer)
        Dim dib(length - 1) As Byte
        If length >= 40 Then
            WriteHeader(dib, width, height, CShort(bitCount), 0, colorsUsed)
            BitConverter.GetBytes(headerSize).CopyTo(dib, 0)
        End If
        Assert.Null(ClipboardPaste.BmpOfDib(dib))
    End Sub

    <Fact>
    Public Sub A_palette_past_the_bytes_is_refused()
        Dim dib(40 + 10 - 1) As Byte
        WriteHeader(dib, 1, 1, 8, 0, 0)
        Assert.Null(ClipboardPaste.BmpOfDib(dib))
        ' A compressed picture has no row length to compare, so the palette is all that holds it
        WriteHeader(dib, 1, 1, 8, 1, 0)
        Assert.Null(ClipboardPaste.BmpOfDib(dib))
    End Sub

    ' ---------------------------------------------------------------- what the windows are told ---

    Private Shared Sub AssertRefused(expected As QrDecodeOutcome, read As ClipboardPasteResult)
        Dim decoded As QrDecodeResult = QrCodeDecoder.DecodePaste(read)
        Assert.Equal(expected, decoded.Outcome)
        Assert.False(decoded.Succeeded)
        Assert.NotEqual("", decoded.FailureMessage)
    End Sub

    <Fact>
    Public Sub Every_refusal_of_the_read_reaches_the_window_as_a_sentence()
        AssertRefused(QrDecodeOutcome.Failed, New ClipboardPasteResult(ClipboardPasteOutcome.Busy))
        AssertRefused(QrDecodeOutcome.Failed, New ClipboardPasteResult(ClipboardPasteOutcome.NoAnswer))
        AssertRefused(QrDecodeOutcome.Failed, New ClipboardPasteResult(ClipboardPasteOutcome.UnderWay))
        AssertRefused(QrDecodeOutcome.Failed, New ClipboardPasteResult(ClipboardPasteOutcome.Unreadable))
        AssertRefused(QrDecodeOutcome.TooLarge, New ClipboardPasteResult(ClipboardPasteOutcome.ImageTooLarge))
        AssertRefused(QrDecodeOutcome.TooLarge, New ClipboardPasteResult(ClipboardPasteOutcome.TextTooLarge))
        AssertRefused(QrDecodeOutcome.NoQr, New ClipboardPasteResult(ClipboardPasteOutcome.Empty))
    End Sub

    <Fact>
    Public Sub A_text_is_handed_over_trimmed_and_blank_text_is_none()
        Dim decoded As QrDecodeResult = QrCodeDecoder.DecodePaste(New ClipboardPasteResult(ClipboardPasteOutcome.Text, text:="  code" & vbCrLf))
        Assert.True(decoded.Succeeded)
        Assert.Equal("code", decoded.Text)
        AssertRefused(QrDecodeOutcome.NoQr, New ClipboardPasteResult(ClipboardPasteOutcome.Text, text:=" " & vbCrLf))
    End Sub

    <Fact>
    Public Sub A_screenshot_with_a_code_is_decoded_through_the_dib()
        Const Payload As String = "FMSEXCH1-clipboard-paste"
        Dim bmp As Byte()
        Using picture As Bitmap = QrCodeRenderer.RenderBitmap(Payload)
            bmp = ClipboardPaste.BmpOfDib(Dib24Of(picture))
        End Using
        Dim decoded As QrDecodeResult = QrCodeDecoder.DecodePaste(New ClipboardPasteResult(ClipboardPasteOutcome.Image, bmp:=bmp))
        Assert.True(decoded.Succeeded)
        Assert.Equal(Payload, decoded.Text)
    End Sub

    <Fact>
    Public Sub A_picture_with_no_code_says_so_and_an_unreadable_one_fails()
        AssertRefused(QrDecodeOutcome.NoQr, New ClipboardPasteResult(ClipboardPasteOutcome.Image, bmp:=ClipboardPaste.BmpOfDib(Pixel24())))
        AssertRefused(QrDecodeOutcome.Failed, New ClipboardPasteResult(ClipboardPasteOutcome.Image, bmp:=New Byte(19) {}))
    End Sub

    ' ---------------------------------------------------------------- the real clipboard ---

    <Fact>
    Public Sub A_clipboard_another_window_holds_reads_as_busy_inside_the_bound()
        Dim holding As New ManualResetEventSlim(False)
        Dim release As New ManualResetEventSlim(False)
        Dim heldOk As Boolean = False
        Dim holder As New Thread(
            Sub()
                ' A real window owns the open: two OpenClipboard(NULL) calls from two threads of one process both succeed and prove nothing
                Dim window As IntPtr = NativeClipboard.CreateWindowExW(0, "STATIC", "fms-test-holder", 0, 0, 0, 0, 0,
                                                                       New IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero)
                For attempt As Integer = 1 To 100
                    If window <> IntPtr.Zero AndAlso NativeClipboard.OpenClipboard(window) Then
                        heldOk = True
                        Exit For
                    End If
                    Thread.Sleep(20)
                Next
                holding.Set()
                If heldOk Then
                    release.Wait(TimeSpan.FromSeconds(30))
                    NativeClipboard.CloseClipboard()
                End If
                If window <> IntPtr.Zero Then NativeClipboard.DestroyWindow(window)
            End Sub) With {.IsBackground = True, .Name = "test clipboard holder"}
        holder.Start()
        Try
            holding.Wait(TimeSpan.FromSeconds(10))
            ' A locked desktop refuses the open with ERROR_ACCESS_DENIED: the bound cannot be proved in such a session
            If Not heldOk Then Return

            Dim clock As Stopwatch = Stopwatch.StartNew()
            Dim result As ClipboardPasteResult = ClipboardPaste.Read()
            clock.Stop()
            Assert.Equal(ClipboardPasteOutcome.Busy, result.Outcome)
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), "the read took " & clock.ElapsedMilliseconds & " ms")
        Finally
            release.Set()
            holder.Join(TimeSpan.FromSeconds(10))
        End Try
    End Sub

    Private NotInheritable Class NativeClipboard
        <DllImport("user32.dll", SetLastError:=True)>
        Friend Shared Function OpenClipboard(hWndNewOwner As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Friend Shared Function CloseClipboard() As <MarshalAs(UnmanagedType.Bool)> Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
        Friend Shared Function CreateWindowExW(exStyle As Integer, className As String, windowName As String, style As Integer,
                                               x As Integer, y As Integer, width As Integer, height As Integer,
                                               parent As IntPtr, menu As IntPtr, instance As IntPtr, param As IntPtr) As IntPtr
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Friend Shared Function DestroyWindow(hWnd As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
        End Function
    End Class

End Class
#End If
