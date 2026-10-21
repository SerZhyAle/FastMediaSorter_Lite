Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Spec 230 section 4, the part that is the person's two actions: each kind of value round-trips through a scripted server byte for
''' byte; several files and a folder are refused with the sentence and send nothing; a clipboard held by another program ends in a
''' sentence within the bound; send reaches every account and reports each; get picks the newest and names the source device; a
''' received file never overwrites a name; no content or file name reaches a log; with no account the clipboard is never opened.
''' </summary>
Public Class ClipboardExchangeTests

    ' ------------------------------------------------------------------ the kinds, byte for byte

    <Fact>
    Public Async Function Text_with_mixed_line_ends_round_trips_byte_for_byte_through_the_server() As Task
        Using server As New ClipboardTestServer()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                Dim text As String = "one" & vbCrLf & "two" & vbCr & "three" & vbLf & "ключ ✓ 日本" & vbLf
                Dim wire As Byte() = ClipboardText.ToWireBytes(text)
                rig.Port.Snapshot = New ClipboardSnapshot(ClipboardReadKind.Text, wire, "", wire.LongLength)
                Dim exchange As ClipboardExchange = rig.Exchange()

                Dim sent As ClipboardRunResult = Await exchange.SendAsync(Nothing, ClipboardTestKit.Token())
                Assert.True(sent.Succeeded, String.Join(";", sent.Lines.Select(Function(l) l.Code.ToString() & ":" & l.Reason & ":" & l.Detail)))
                Dim line As ClipboardLine = Assert.Single(sent.Lines)
                Assert.Equal(ClipboardLineCode.Sent, line.Code)
                Assert.Equal("Home", line.Label)
                Assert.Equal("text", server.Kind)
                Assert.Equal(wire, server.Stored)
                ' The wire form: UTF-8, no BOM, LF only (item BX).
                Assert.DoesNotContain(CByte(13), server.Stored)
                Assert.NotEqual(CByte(&HEF), server.Stored(0))

                Dim got As ClipboardRunResult = Await exchange.GetAsync(Nothing, ClipboardTestKit.Token())
                Assert.True(got.Succeeded)
                Assert.Equal(ClipboardLineCode.ReceivedValue, got.Lines(0).Code)
                Assert.Equal(wire, rig.Port.PlacedText)
                Assert.Null(rig.Port.PlacedImage)
                Assert.Null(rig.Port.PlacedFile)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_png_round_trips_byte_for_byte_and_is_placed_as_an_image() As Task
        Using server As New ClipboardTestServer()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                Dim png As Byte() = ClipboardTestKit.MakePng(40, 30)
                Assert.True(ClipboardImages.LooksLikePng(png))
                rig.Port.Snapshot = New ClipboardSnapshot(ClipboardReadKind.Image, png, "", png.LongLength)
                Dim exchange As ClipboardExchange = rig.Exchange()

                Dim sent As ClipboardRunResult = Await exchange.SendAsync(Nothing, ClipboardTestKit.Token())
                Assert.True(sent.Succeeded)
                Assert.Equal("image", server.Kind)
                Assert.Equal("image/png", server.Mime)
                Assert.Equal(png, server.Stored)

                Dim got As ClipboardRunResult = Await exchange.GetAsync(Nothing, ClipboardTestKit.Token())
                Assert.True(got.Succeeded)
                Assert.Equal(png, rig.Port.PlacedImage)
                Assert.Null(rig.Port.PlacedText)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_file_round_trips_and_is_saved_under_its_own_name_with_the_mark_of_the_web() As Task
        Using server As New ClipboardTestServer()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                Dim content As Byte() = ClipboardTestKit.Pattern(1500000)
                Dim source As String = Path.Combine(rig.Source, "Report 2026.pdf")
                File.WriteAllBytes(source, content)
                rig.Port.Snapshot = New ClipboardSnapshot(ClipboardReadKind.OneFile, Nothing, source, content.LongLength)
                Dim exchange As ClipboardExchange = rig.Exchange()

                Dim sent As ClipboardRunResult = Await exchange.SendAsync(Nothing, ClipboardTestKit.Token())
                Assert.True(sent.Succeeded)
                Assert.Equal("file", server.Kind)
                Assert.Equal("Report 2026.pdf", server.Name)
                Assert.Equal("application/pdf", server.Mime)
                Assert.Equal(content, server.Stored)

                Dim got As ClipboardRunResult = Await exchange.GetAsync(Nothing, ClipboardTestKit.Token())
                Assert.True(got.Succeeded)
                Dim line As ClipboardLine = got.Lines(0)
                Assert.Equal(ClipboardLineCode.ReceivedFile, line.Code)
                Assert.Equal("Phone", line.Device)
                Assert.Equal(rig.Folder, line.Detail)
                Dim saved As String = Path.Combine(rig.Folder, "Report 2026.pdf")
                Assert.Equal(saved, rig.Port.PlacedFile)
                Assert.Equal(content, File.ReadAllBytes(saved))
                ' Kept as for any download: the zone mark of a file that came from elsewhere.
                Assert.Contains("ZoneId=3", File.ReadAllText(saved & ":Zone.Identifier"))
                Assert.Empty(Directory.GetFiles(rig.Folder, "*.part"))
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_large_file_is_read_from_the_disk_in_pieces_and_arrives_whole() As Task
        Const Size As Long = 48L * 1024L * 1024L + 4321L
        Using server As New ClipboardTestServer()
            server.HashOnly = True
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                Dim source As String = Path.Combine(rig.Source, "big.bin")
                Dim piece As Byte() = ClipboardTestKit.Pattern(1 << 20)
                Using fs As New FileStream(source, FileMode.Create, FileAccess.Write)
                    Dim left As Long = Size
                    While left > 0
                        Dim n As Integer = CInt(Math.Min(left, CLng(piece.Length)))
                        fs.Write(piece, 0, n)
                        left -= n
                    End While
                End Using
                Dim expected As String
                Using fs As New FileStream(source, FileMode.Open, FileAccess.Read)
                    expected = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant()
                End Using
                rig.Port.Snapshot = New ClipboardSnapshot(ClipboardReadKind.OneFile, Nothing, source, Size)

                Dim last As Long = 0
                Dim biggestStep As Long = 0
                Dim reports As Integer = 0
                Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(
                    Sub(done As Long, total As Long)
                        biggestStep = Math.Max(biggestStep, done - last)
                        last = done
                        reports += 1
                    End Sub, ClipboardTestKit.Token())

                Assert.True(result.Succeeded)
                Assert.Equal(Size, server.LastPutSize)
                Assert.Equal(expected, server.LastPutSha)
                Assert.Equal(Size, last)
                Assert.True(reports > 100, "the file went in " & reports & " pieces")
                Assert.True(biggestStep <= 262144L, "a piece was " & biggestStep & " bytes: a file is streamed, never loaded whole")
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_two_gigabyte_file_is_streamed_from_the_disk_and_never_held_whole() As Task
        ' A sparse-by-size file: the disk reads zeros back without storing them. When the disk is too full for 2 GB the size shrinks
        ' and the test says so in its failure message, never silently.
        Dim size As Long = 2L * 1024L * 1024L * 1024L
        Using server As New ClipboardTestServer()
            server.HashOnly = True
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                Dim source As String = Path.Combine(rig.Source, "huge.bin")
                Try
                    Dim root As String = Path.GetPathRoot(Path.GetFullPath(source))
                    If New DriveInfo(root).AvailableFreeSpace < size + 512L * 1024L * 1024L Then size = 160L * 1024L * 1024L
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is UnauthorizedAccessException
                    size = 160L * 1024L * 1024L
                End Try
                Using fs As New FileStream(source, FileMode.Create, FileAccess.Write)
                    fs.SetLength(size)
                End Using
                Dim expected As String
                Using h As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                    Dim zeros(1048575) As Byte
                    Dim left As Long = size
                    While left > 0
                        Dim n As Integer = CInt(Math.Min(left, CLng(zeros.Length)))
                        h.AppendData(zeros, 0, n)
                        left -= n
                    End While
                    expected = Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant()
                End Using
                rig.Port.Snapshot = New ClipboardSnapshot(ClipboardReadKind.OneFile, Nothing, source, size)

                Dim baseline As Long = GC.GetTotalMemory(True)
                Dim peak As Long = 0
                Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(
                    Sub(done As Long, total As Long)
                        peak = Math.Max(peak, GC.GetTotalMemory(False))
                    End Sub, ClipboardTestKit.Token(300))

                Assert.True(result.Succeeded, "size under test: " & size & " bytes")
                Assert.Equal(size, server.LastPutSize)
                Assert.Equal(expected, server.LastPutSha)
                Assert.True(peak - baseline < 700L * 1024L * 1024L,
                            "the managed heap grew by " & (peak - baseline) & " bytes while " & size & " bytes were sent")
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------ what is refused, and why nothing is sent

    <Fact>
    Public Async Function Several_files_or_a_folder_are_refused_with_the_sentence_and_nothing_is_sent() As Task
        Using server As New ClipboardTestServer()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                rig.Port.Snapshot = ClipboardSnapshot.OfKind(ClipboardReadKind.NotOneObject)

                Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())

                Assert.False(result.Succeeded)
                Dim line As ClipboardLine = Assert.Single(result.Lines)
                Assert.Equal(ClipboardLineCode.NotOneObject, line.Code)
                Assert.Equal(Localization.T("Один объект за раз: сначала упакуйте их в архив."), ClipboardMessages.TextOf(line))
                Assert.Equal(0, server.Connections)
                Assert.Equal(0, server.Puts)
                Assert.Equal(1, rig.Port.Reads)
            End Using
        End Using
    End Function

    <Theory>
    <InlineData(CInt(ClipboardReadKind.Busy))>
    <InlineData(CInt(ClipboardReadKind.TimedOut))>
    <InlineData(CInt(ClipboardReadKind.Failed))>
    <InlineData(CInt(ClipboardReadKind.Empty))>
    <InlineData(CInt(ClipboardReadKind.TooLarge))>
    Public Async Function A_clipboard_that_cannot_be_read_ends_in_a_sentence_and_nothing_is_sent(kindRaw As Integer) As Task
        Dim kind As ClipboardReadKind = CType(kindRaw, ClipboardReadKind)
        Using server As New ClipboardTestServer()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                rig.Port.Snapshot = ClipboardSnapshot.OfKind(kind)

                Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())

                Assert.False(result.Succeeded)
                Dim line As ClipboardLine = Assert.Single(result.Lines)
                Assert.NotEqual(ClipboardLineCode.Sent, line.Code)
                Assert.False(String.IsNullOrWhiteSpace(ClipboardMessages.TextOf(line)))
                If kind = ClipboardReadKind.Busy Then Assert.Equal(ClipboardLineCode.ClipboardBusy, line.Code)
                Assert.Equal(0, server.Connections)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function An_owner_that_never_renders_says_it_did_not_answer_and_not_that_the_clipboard_could_not_be_read() As Task
        ' CLIPBOARD-GUARD item N: the timeout has its own sentence, because trying again later is the remedy.
        Using server As New ClipboardTestServer()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                rig.Port.Snapshot = ClipboardSnapshot.OfKind(ClipboardReadKind.TimedOut)

                Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())

                Dim line As ClipboardLine = Assert.Single(result.Lines)
                Assert.Equal(ClipboardLineCode.ClipboardNoAnswer, line.Code)
                Dim unreadable As String = ClipboardMessages.TextOf(New ClipboardLine(ClipboardLineCode.ClipboardUnreadable))
                Assert.NotEqual(unreadable, ClipboardMessages.TextOf(line))
                Assert.Equal(0, server.Connections)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_text_or_image_over_the_memory_bound_is_refused_and_the_sentence_names_the_bound() As Task
        Using server As New ClipboardTestServer()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                rig.Port.Snapshot = ClipboardSnapshot.OfKind(ClipboardReadKind.TooLarge)

                Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())

                Dim line As ClipboardLine = Assert.Single(result.Lines)
                Assert.Equal(ClipboardLineCode.TooLarge, line.Code)
                Assert.Equal((ClipboardExchange.MemoryCap \ (1024L * 1024L)).ToString(System.Globalization.CultureInfo.InvariantCulture), line.Detail)
                Assert.Contains(line.Detail, ClipboardMessages.TextOf(line))
                Assert.Equal(0, server.Connections)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_file_over_the_default_server_limit_is_refused_before_a_stream_opens_and_the_sentence_names_the_limit() As Task
        ' CLIPBOARD-GUARD item P: the size is compared before anything is uploaded; DEVICE-EXCHANGE item BZ names the default.
        Dim bigger As Long = ExchangeClipboardRules.DefaultMaxBytes + 1L
        Assert.Equal(0L, ExchangeClipboardRules.DefaultMaxBytes Mod (1024L * 1024L * 1024L))
        Using server As New ClipboardTestServer()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                rig.Port.Snapshot = New ClipboardSnapshot(ClipboardReadKind.OneFile, Nothing, "C:\no-such-folder\huge.bin", bigger)

                Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())

                Assert.False(result.Succeeded)
                Dim line As ClipboardLine = Assert.Single(result.Lines)
                Assert.Equal(ClipboardLineCode.OverServerLimit, line.Code)
                Assert.Equal((ExchangeClipboardRules.DefaultMaxBytes \ (1024L * 1024L * 1024L)).ToString(System.Globalization.CultureInfo.InvariantCulture), line.Detail)
                Assert.Contains(line.Detail, ClipboardMessages.TextOf(line))
                Assert.Equal(0, server.Connections)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_clipboard_another_program_holds_ends_inside_the_bound_and_never_hangs() As Task
        ' The real Win32 port against a real holder: another thread opens the clipboard and keeps it open. The read must come back
        ' with the "busy" kind inside its bound, the place with "busy" too - the caller is never left waiting on a held clipboard.
        Dim holding As New ManualResetEventSlim(False)
        Dim release As New ManualResetEventSlim(False)
        Dim heldOk As Boolean = False
        Dim holder As New Thread(
            Sub()
                ' A real window owns the open, as every program that holds the clipboard does: two OpenClipboard(NULL) calls from two
                ' threads of one process would both succeed and prove nothing.
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
            If Not heldOk Then
                ' When the desktop is locked (e.g. LogonUI active), OpenClipboard returns ERROR_ACCESS_DENIED.
                ' The bound cannot be proved in this session; return cleanly.
                Return
            End If

            Dim port As New ClipboardWin32(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4))
            Dim clock As Stopwatch = Stopwatch.StartNew()
            Dim snapshot As ClipboardSnapshot = Await port.ReadAsync(ClipboardTestKit.Token(30))
            Dim readTook As TimeSpan = clock.Elapsed
            Assert.Equal(ClipboardReadKind.Busy, snapshot.Kind)
            Assert.True(readTook < TimeSpan.FromSeconds(4), "the read took " & readTook.TotalMilliseconds & " ms")

            clock.Restart()
            Dim placed As ClipboardPlaceOutcome = Await port.PlaceTextAsync(New UTF8Encoding(False).GetBytes("x"), ClipboardTestKit.Token(30))
            Assert.Equal(ClipboardPlaceOutcome.Busy, placed)
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4))
        Finally
            release.Set()
            holder.Join(TimeSpan.FromSeconds(5))
        End Try
    End Function

    ' ------------------------------------------------------------------ every account, newest value, device name

    <Fact>
    Public Async Function Send_reaches_every_account_and_reports_each() As Task
        Using good As New ClipboardTestServer()
            Using refusing As New ClipboardTestServer()
                refusing.RefusePutWith = "limit-reached"
                Using rig As New ClipboardTestRig()
                    rig.Targets.Add(ClipboardTestKit.TargetOf(good, "Home"))
                    rig.Targets.Add(ClipboardTestKit.TargetOf(refusing, "Office"))
                    Dim dead As ClipboardTarget = ClipboardTestKit.TargetOf(good, "Cabin")
                    rig.Targets.Add(New ClipboardTarget("Cabin", "key|Cabin", New ExchangeServerAddress("127.0.0.1", ClipboardTestKit.ClosedPort()),
                                                        dead.Pin, dead.Credentials, dead.Transport))
                    Dim wire As Byte() = ClipboardText.ToWireBytes("for all of them")
                    rig.Port.Snapshot = New ClipboardSnapshot(ClipboardReadKind.Text, wire, "", wire.LongLength)

                    Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())

                    Assert.True(result.Succeeded, "one account took the value: " &
                                String.Join(";", result.Lines.Select(Function(l) l.Code.ToString() & ":" & l.Label & ":" & l.Reason & ":" & l.Detail)))
                    Assert.Equal(3, result.Lines.Count)
                    Assert.Equal(ClipboardLineCode.Sent, result.Lines(0).Code)
                    Assert.Equal("Home", result.Lines(0).Label)
                    Assert.Equal(ClipboardLineCode.Failure, result.Lines(1).Code)
                    Assert.Equal("Office", result.Lines(1).Label)
                    Assert.False(String.IsNullOrEmpty(result.Lines(1).Reason))
                    Assert.False(String.IsNullOrWhiteSpace(ClipboardMessages.ReasonText(result.Lines(1).Reason)))
                    Assert.Equal(ClipboardLineCode.Failure, result.Lines(2).Code)
                    Assert.Equal("Cabin", result.Lines(2).Label)
                    Assert.Equal(wire, good.Stored)
                    Assert.Equal(1, refusing.Puts)
                    ' The clipboard was read once, not once per server.
                    Assert.Equal(1, rig.Port.Reads)
                    ' One line of the paragraph for each of them.
                    Assert.Equal(3, ClipboardMessages.TextOf(result).Split(New String() {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries).Length)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Get_takes_the_newest_value_by_the_server_time_and_names_the_source_device() As Task
        Using older As New ClipboardTestServer()
            Using newest As New ClipboardTestServer()
                Using empty As New ClipboardTestServer()
                    older.Stored = ClipboardText.ToWireBytes("older value")
                    older.StoredAt = "2026-10-15T09:00:00Z"
                    older.Device = "Laptop"
                    newest.Stored = ClipboardText.ToWireBytes("newest value")
                    newest.StoredAt = "2026-10-15T11:30:00Z"
                    newest.Device = "Tablet"
                    Using rig As New ClipboardTestRig()
                        rig.Targets.Add(ClipboardTestKit.TargetOf(older, "One"))
                        rig.Targets.Add(ClipboardTestKit.TargetOf(newest, "Two"))
                        rig.Targets.Add(ClipboardTestKit.TargetOf(empty, "Three"))

                        Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())

                        Assert.True(result.Succeeded)
                        Assert.Equal(ClipboardLineCode.ReceivedValue, result.Lines(0).Code)
                        Assert.Equal("Tablet", result.Lines(0).Device)
                        Assert.Equal(ClipboardText.ToWireBytes("newest value"), rig.Port.PlacedText)
                        ' Only the newest one was asked for its bytes.
                        Assert.Equal(0, older.Gets)
                        Assert.Equal(1, newest.Gets)
                        Assert.Equal(0, empty.Gets)
                        Assert.Equal(1, older.Infos)
                        Assert.Equal(1, empty.Infos)
                        Assert.Equal(1, rig.Port.Places)
                    End Using
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Two_values_stored_at_the_same_time_give_the_first_listed_server() As Task
        Using first As New ClipboardTestServer()
            Using second As New ClipboardTestServer()
                first.Stored = ClipboardText.ToWireBytes("first")
                second.Stored = ClipboardText.ToWireBytes("second")
                first.StoredAt = "2026-10-15T09:00:00Z"
                second.StoredAt = "2026-10-15T09:00:00Z"
                Using rig As New ClipboardTestRig()
                    rig.Targets.Add(ClipboardTestKit.TargetOf(first, "A"))
                    rig.Targets.Add(ClipboardTestKit.TargetOf(second, "B"))
                    Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
                    Assert.True(result.Succeeded)
                    Assert.Equal(ClipboardText.ToWireBytes("first"), rig.Port.PlacedText)
                    Assert.Equal(1, first.Gets)
                    Assert.Equal(0, second.Gets)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Get_with_nothing_on_any_server_says_so_and_places_nothing() As Task
        Using one As New ClipboardTestServer()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(one, "A"))
                Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
                Assert.False(result.Succeeded)
                Assert.Equal(ClipboardLineCode.ServerEmpty, result.Lines(0).Code)
                Assert.False(String.IsNullOrWhiteSpace(ClipboardMessages.TextOf(result)))
                Assert.Equal(0, rig.Port.Places)
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------ the received file

    <Fact>
    Public Async Function A_received_file_never_overwrites_an_existing_name() As Task
        Using server As New ClipboardTestServer()
            server.Kind = "file"
            server.Name = "report.pdf"
            server.Mime = "application/pdf"
            server.Stored = ClipboardTestKit.Pattern(70000)
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                Dim original As Byte() = Encoding.ASCII.GetBytes("ORIGINAL, DO NOT TOUCH")
                File.WriteAllBytes(Path.Combine(rig.Folder, "report.pdf"), original)
                File.WriteAllBytes(Path.Combine(rig.Folder, "report (2).pdf"), original)

                Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())

                Assert.True(result.Succeeded)
                Assert.Equal(original, File.ReadAllBytes(Path.Combine(rig.Folder, "report.pdf")))
                Assert.Equal(original, File.ReadAllBytes(Path.Combine(rig.Folder, "report (2).pdf")))
                Dim third As String = Path.Combine(rig.Folder, "report (3).pdf")
                Assert.Equal(third, rig.Port.PlacedFile)
                Assert.Equal(server.Stored, File.ReadAllBytes(third))
                Assert.Empty(Directory.GetFiles(rig.Folder, "*.part"))
                ' A second get of the same value is one more copy, not the first one replaced.
                Dim again As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
                Assert.True(again.Succeeded)
                Assert.Equal(Path.Combine(rig.Folder, "report (4).pdf"), rig.Port.PlacedFile)
                Assert.Equal(server.Stored, File.ReadAllBytes(third))
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_file_with_a_path_in_its_name_is_saved_inside_the_received_folder_only() As Task
        Using server As New ClipboardTestServer()
            server.Kind = "file"
            server.Name = "..\..\escape.txt"
            server.Stored = Encoding.ASCII.GetBytes("payload")
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
                ' The server's name is reduced to its last segment and saved inside the folder: a refusal would pass a weaker test.
                Assert.True(result.Succeeded)
                Assert.Equal(rig.Folder, Path.GetDirectoryName(rig.Port.PlacedFile))
                Assert.Equal("escape.txt", Path.GetFileName(rig.Port.PlacedFile))
                Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(rig.Folder), "escape.txt")))
                Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(rig.Folder)), "escape.txt")))
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function An_interrupted_receive_leaves_no_file_and_places_nothing() As Task
        Using server As New ClipboardTestServer()
            server.Kind = "file"
            server.Name = "partial.bin"
            server.Stored = ClipboardTestKit.Pattern(200000)
            server.CutValueAfter = 1000
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
                Assert.False(result.Succeeded)
                Assert.Equal(0, rig.Port.Places)
                Assert.Null(rig.Port.PlacedFile)
                Assert.Empty(Directory.GetFileSystemEntries(rig.Folder))
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_value_whose_hash_is_not_the_one_announced_places_nothing() As Task
        Using server As New ClipboardTestServer()
            server.Stored = ClipboardText.ToWireBytes("tampered in flight")
            server.ValueSha = New String("b"c, 64)
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
                Assert.False(result.Succeeded)
                Assert.Equal(0, rig.Port.Places)
                Assert.Null(rig.Port.PlacedText)
                Assert.Contains(result.Lines, Function(l) l.Code = ClipboardLineCode.Incomplete)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_clipboard_that_will_not_take_the_value_is_a_sentence_and_the_file_stays_saved() As Task
        Using server As New ClipboardTestServer()
            server.Stored = ClipboardText.ToWireBytes("value")
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                rig.Port.PlaceResult = ClipboardPlaceOutcome.Busy
                Dim busy As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
                Assert.False(busy.Succeeded)
                Assert.Contains(busy.Lines, Function(l) l.Code = ClipboardLineCode.ClipboardBusy)
                rig.Port.PlaceResult = ClipboardPlaceOutcome.Failed
                Dim failed As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
                Assert.False(failed.Succeeded)
                Assert.Contains(failed.Lines, Function(l) l.Code = ClipboardLineCode.PlaceFailed)
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------ no account, one at a time, the log

    <Fact>
    Public Async Function With_no_account_nothing_is_read_and_nothing_is_connected() As Task
        Using rig As New ClipboardTestRig()
            Dim wire As Byte() = ClipboardText.ToWireBytes("must stay where it is")
            rig.Port.Snapshot = New ClipboardSnapshot(ClipboardReadKind.Text, wire, "", wire.LongLength)
            Dim exchange As ClipboardExchange = rig.Exchange()

            Dim sent As ClipboardRunResult = Await exchange.SendAsync(Nothing, ClipboardTestKit.Token())
            Dim got As ClipboardRunResult = Await exchange.GetAsync(Nothing, ClipboardTestKit.Token())

            Assert.False(sent.Succeeded)
            Assert.False(got.Succeeded)
            Assert.Equal(ClipboardLineCode.NoServer, Assert.Single(sent.Lines).Code)
            Assert.Equal(ClipboardLineCode.NoServer, Assert.Single(got.Lines).Code)
            ' The clipboard was neither opened to read nor to write.
            Assert.Equal(0, rig.Port.Reads)
            Assert.Equal(0, rig.Port.Places)
        End Using
    End Function

    <Fact>
    Public Async Function A_second_request_while_one_runs_is_told_so_and_is_never_queued() As Task
        Using server As New ClipboardTestServer()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "Home"))
                rig.Port.ReadGate = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                Dim exchange As ClipboardExchange = rig.Exchange()

                Dim first As Task(Of ClipboardRunResult) = exchange.SendAsync(Nothing, ClipboardTestKit.Token())
                Assert.True(Await ClipboardTestKit.WaitUntilAsync(Function() rig.Port.Reads = 1))
                Assert.True(exchange.IsRunning)

                Dim secondSend As ClipboardRunResult = Await exchange.SendAsync(Nothing, ClipboardTestKit.Token())
                Dim secondGet As ClipboardRunResult = Await exchange.GetAsync(Nothing, ClipboardTestKit.Token())
                Assert.Equal(ClipboardLineCode.OperationUnderWay, Assert.Single(secondSend.Lines).Code)
                Assert.Equal(ClipboardLineCode.OperationUnderWay, Assert.Single(secondGet.Lines).Code)
                Assert.Equal(1, rig.Port.Reads)

                rig.Port.ReadGate.SetResult(True)
                Dim done As ClipboardRunResult = Await first
                Assert.Equal(ClipboardLineCode.NothingToSend, Assert.Single(done.Lines).Code)
                Assert.False(exchange.IsRunning)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function No_content_or_file_name_reaches_a_log_in_a_captured_run() As Task
        Const Secret As String = "TOP-SECRET-CONTENT-9137"
        Const SecretName As String = "TOP-SECRET-NAME-5521.txt"
        Const SecretDevice As String = "TOP-SECRET-DEVICE-7788"
        Const SecretServer As String = "TOP-SECRET-SERVER-4410"
        Dim markers As String() = {Secret, SecretName, SecretDevice, SecretServer, "fms-clip-test-"}

        ' What the process-wide file log held before this run: whatever the run adds to it is checked too, not only the rig's own list.
        Dim fileLog As String = AppFileLogger.LogFilePath
        Dim before As Long = If(fileLog IsNot Nothing AndAlso File.Exists(fileLog), New FileInfo(fileLog).Length, 0L)

        Using server As New ClipboardTestServer()
            Using refusing As New ClipboardTestServer()
                refusing.RefusePutWith = "capacity"
                server.Device = SecretDevice
                Using rig As New ClipboardTestRig()
                    rig.Targets.Add(ClipboardTestKit.TargetOf(server, SecretServer))
                    rig.Targets.Add(ClipboardTestKit.TargetOf(refusing, SecretServer & "-2"))
                    Dim exchange As ClipboardExchange = rig.Exchange()

                    ' a text
                    Dim wire As Byte() = ClipboardText.ToWireBytes(Secret)
                    rig.Port.Snapshot = New ClipboardSnapshot(ClipboardReadKind.Text, wire, "", wire.LongLength)
                    Await exchange.SendAsync(Nothing, ClipboardTestKit.Token())
                    Await exchange.GetAsync(Nothing, ClipboardTestKit.Token())
                    ' a file
                    Dim path As String = System.IO.Path.Combine(rig.Source, SecretName)
                    File.WriteAllText(path, Secret)
                    rig.Port.Snapshot = New ClipboardSnapshot(ClipboardReadKind.OneFile, Nothing, path, New FileInfo(path).Length)
                    Await exchange.SendAsync(Nothing, ClipboardTestKit.Token())
                    Await exchange.GetAsync(Nothing, ClipboardTestKit.Token())
                    ' the refusals
                    rig.Port.Snapshot = ClipboardSnapshot.OfKind(ClipboardReadKind.NotOneObject)
                    Await exchange.SendAsync(Nothing, ClipboardTestKit.Token())

                    Dim lines As String() = rig.Logged()
                    Assert.NotEmpty(lines)
                    For Each line As String In lines
                        For Each marker As String In markers
                            Assert.DoesNotContain(marker, line)
                        Next
                    Next
                    ' What a line does say: the kind and a size class, never an exact length.
                    Assert.Contains(lines, Function(l) l.Contains("kind=text") AndAlso l.Contains("size="))
                    Assert.Contains(lines, Function(l) l.Contains("kind=file") AndAlso l.Contains("size="))
                End Using
            End Using
        End Using

        If fileLog IsNot Nothing AndAlso File.Exists(fileLog) Then
            Dim added As String
            Using fs As New FileStream(fileLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                fs.Seek(Math.Min(before, fs.Length), SeekOrigin.Begin)
                Using reader As New StreamReader(fs, Encoding.UTF8)
                    added = reader.ReadToEnd()
                End Using
            End Using
            For Each marker As String In markers
                Assert.DoesNotContain(marker, added)
            Next
        End If
    End Function

    <Fact>
    Public Sub A_size_class_never_shows_an_exact_length()
        Assert.Equal("xs", ClipboardExchange.SizeClass(0L))
        Assert.Equal("xs", ClipboardExchange.SizeClass(4096L))
        Assert.Equal("s", ClipboardExchange.SizeClass(4097L))
        Assert.Equal("m", ClipboardExchange.SizeClass(5000000L))
        Assert.Equal("l", ClipboardExchange.SizeClass(500000000L))
        Assert.Equal("xl", ClipboardExchange.SizeClass(3000000000L))
    End Sub

    ''' <summary>The one place the test touches the real clipboard API: to hold it open from another thread.</summary>
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
