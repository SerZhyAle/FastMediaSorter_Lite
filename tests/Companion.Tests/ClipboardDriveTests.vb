Option Strict On

Imports System.IO
Imports System.Net.Http
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 269 P4 - the two clipboard actions through the person's own Google Drive (DEVICE-EXCHANGE item CP, spec 230 rule 6), on the
''' in-memory Drive and through the real orchestrator: Drive only and beside a server, the newest by each store's own time with the
''' server first on equal times, the refusals of item CS before a byte is uploaded, one line per failure that never hides a server's
''' value, nothing on the disk for a text or an image, and a log with neither the value, nor a name, nor a device.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ClipboardDriveTests
    Implements IDisposable

    Private Const IdA As String = "AAAAAAAAAAAAAAAAAAAAAA"
    Private Const IdB As String = "BBBBBBBBBBBBBBBBBBBBBA"

    Private ReadOnly _drive As New FakeDriveHandler()
    Private ReadOnly _http As HttpClient

    Public Sub New()
        _http = New HttpClient(_drive, False)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _http.Dispose()
    End Sub

    Private Function PathOf(id As String, device As String) As ClipboardDrivePath
        Dim client As New ExchangeDriveClient(_http, New FakeDriveTokens(), Function(span As TimeSpan, token As CancellationToken) Task.CompletedTask, Function() 0.5)
        Return New ClipboardDrivePath(New ExchangeDriveQueue(client, id, device, New ExchangeDriveMemoryHandledStore()), client)
    End Function

    Private Shared Function TextSnapshot(text As String) As ClipboardSnapshot
        Dim wire As Byte() = ClipboardText.ToWireBytes(text)
        Return New ClipboardSnapshot(ClipboardReadKind.Text, wire, "", wire.LongLength)
    End Function

    ''' <summary>Sends one value from a device of its own, so a test starts from an account that holds it.</summary>
    Private Async Function SendFromLaptopAsync(snapshot As ClipboardSnapshot) As Task
        Using sender As New ClipboardTestRig()
            sender.Drive = PathOf(IdA, "Laptop")
            sender.Port.Snapshot = snapshot
            Dim sent As ClipboardRunResult = Await sender.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())
            Assert.Equal(ClipboardLineCode.SentViaDrive, Assert.Single(sent.Lines).Code)
            Assert.True(sent.Succeeded)
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------ Drive only

    <Fact>
    Public Async Function Drive_only_carries_a_text_between_two_devices_and_leaves_no_file_and_keeps_the_value() As Task
        Await SendFromLaptopAsync(TextSnapshot("a value" & vbCrLf & "of two lines"))
        Using receiver As New ClipboardTestRig()
            receiver.Drive = PathOf(IdB, "Tablet")

            Dim result As ClipboardRunResult = Await receiver.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())

            Assert.True(result.Succeeded)
            Dim line As ClipboardLine = Assert.Single(result.Lines)
            Assert.Equal(ClipboardLineCode.ReceivedValueViaDrive, line.Code)
            Assert.Equal("Laptop", line.Device)
            Assert.Equal(ClipboardText.ToWireBytes("a value" & vbLf & "of two lines"), receiver.Port.PlacedText)
            Assert.Empty(Directory.GetFiles(receiver.Folder))
            ' Item CP: a receiver never deletes the value it took, and a second device still finds it.
            Assert.Equal(1, _drive.CountNamed(ExchangeDrivePackets.ClipboardTextFileName))
        End Using
        Using second As New ClipboardTestRig()
            second.Drive = PathOf(IdB, "Tablet")
            Assert.True((Await second.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())).Succeeded)
        End Using
    End Function

    <Fact>
    Public Async Function Drive_only_carries_an_image_and_leaves_no_file() As Task
        Dim png As Byte() = ClipboardTestKit.MakePng()
        Await SendFromLaptopAsync(New ClipboardSnapshot(ClipboardReadKind.Image, png, "", png.LongLength))
        Using receiver As New ClipboardTestRig()
            receiver.Drive = PathOf(IdB, "Tablet")

            Dim result As ClipboardRunResult = Await receiver.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())

            Assert.Equal(ClipboardLineCode.ReceivedValueViaDrive, Assert.Single(result.Lines).Code)
            Assert.Equal(png, receiver.Port.PlacedImage)
            Assert.Null(receiver.Port.PlacedText)
            Assert.Empty(Directory.GetFiles(receiver.Folder))
        End Using
    End Function

    <Fact>
    Public Async Function Drive_only_carries_a_file_to_the_received_folder_under_a_free_name_with_the_mark_and_opens_nothing() As Task
        Dim content As Byte() = ClipboardTestKit.Pattern(4096)
        Using sender As New ClipboardTestRig()
            Dim source As String = Path.Combine(sender.Source, "report.pdf")
            File.WriteAllBytes(source, content)
            Await SendFromLaptopAsync(New ClipboardSnapshot(ClipboardReadKind.OneFile, Nothing, source, content.LongLength))
        End Using
        Using receiver As New ClipboardTestRig()
            receiver.Drive = PathOf(IdB, "Tablet")
            Dim taken As String = Path.Combine(receiver.Folder, "report.pdf")
            File.WriteAllText(taken, "a file of the person's")

            Dim result As ClipboardRunResult = Await receiver.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())

            Assert.True(result.Succeeded)
            Dim line As ClipboardLine = Assert.Single(result.Lines)
            Assert.Equal(ClipboardLineCode.ReceivedFileViaDrive, line.Code)
            Assert.Equal("Laptop", line.Device)
            Assert.Equal(receiver.Folder, line.Detail)
            Assert.Equal("a file of the person's", File.ReadAllText(taken))
            Dim placed As String = receiver.Port.PlacedFile
            Assert.NotEqual(taken, placed)
            Assert.Equal(receiver.Folder, Path.GetDirectoryName(placed))
            Assert.Equal(content, File.ReadAllBytes(placed))
            Assert.Contains("ZoneId=3", File.ReadAllText(placed & ":Zone.Identifier"))
        End Using
    End Function

    <Fact>
    Public Async Function A_text_above_the_memory_bound_is_saved_as_a_file_and_is_not_refused() As Task
        Dim wire(ExchangeDriveQueue.MaxClipboardTextBytes) As Byte
        Array.Fill(wire, CByte(AscW("x"c)))
        Await SendFromLaptopAsync(New ClipboardSnapshot(ClipboardReadKind.Text, wire, "", wire.LongLength))
        Using receiver As New ClipboardTestRig()
            receiver.Drive = PathOf(IdB, "Tablet")

            Dim result As ClipboardRunResult = Await receiver.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())

            Assert.Equal(ClipboardLineCode.ReceivedFileViaDrive, Assert.Single(result.Lines).Code)
            Assert.Null(receiver.Port.PlacedText)
            Assert.Equal(wire.LongLength, New FileInfo(receiver.Port.PlacedFile).Length)
        End Using
    End Function

    <Fact>
    Public Async Function An_account_with_no_value_says_so_in_drive_words() As Task
        Using rig As New ClipboardTestRig()
            rig.Drive = PathOf(IdB, "Tablet")
            Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
            Assert.False(result.Succeeded)
            Assert.Equal(ClipboardLineCode.EmptyViaDrive, Assert.Single(result.Lines).Code)
            Assert.Equal(0, rig.Port.Places)
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------ beside a server

    <Fact>
    Public Async Function A_device_with_a_server_and_drive_sends_to_both_with_one_line_each() As Task
        Using server As New ClipboardTestServer()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "One"))
                rig.Drive = PathOf(IdA, "Laptop")
                rig.Port.Snapshot = TextSnapshot("to both")

                Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())

                Assert.True(result.Succeeded)
                Assert.Equal({ClipboardLineCode.Sent, ClipboardLineCode.SentViaDrive}, result.Lines.Select(Function(l) l.Code).ToArray())
                Assert.Equal(1, server.Puts)
                Assert.Equal(1, _drive.CountNamed(ExchangeDrivePackets.ClipboardTextFileName))
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_refused_server_does_not_stop_drive_and_a_refused_drive_does_not_undo_the_server() As Task
        Using server As New ClipboardTestServer()
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "One"))
                rig.Drive = New ClipboardDrivePath(Nothing, Nothing)
                rig.Port.Snapshot = TextSnapshot("to the server only")

                Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())

                Assert.True(result.Succeeded)
                Assert.Equal({ClipboardLineCode.Sent, ClipboardLineCode.DriveSignedOut}, result.Lines.Select(Function(l) l.Code).ToArray())
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Get_takes_drive_when_it_is_later_than_the_server() As Task
        _drive.ServerNow = New DateTimeOffset(2026, 10, 23, 12, 0, 1, TimeSpan.Zero)
        Await SendFromLaptopAsync(TextSnapshot("from drive"))
        Using server As New ClipboardTestServer()
            server.Stored = ClipboardText.ToWireBytes("from the server")
            server.StoredAt = "2026-10-23T12:00:00Z"
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "One"))
                rig.Drive = PathOf(IdB, "Tablet")

                Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())

                Assert.True(result.Succeeded)
                Assert.Equal(ClipboardLineCode.ReceivedValueViaDrive, result.Lines(0).Code)
                Assert.Equal(ClipboardText.ToWireBytes("from drive"), rig.Port.PlacedText)
                Assert.Equal(0, server.Gets)
            End Using
        End Using
    End Function

    <Theory>
    <InlineData("2026-10-23T12:00:00Z")>
    <InlineData("2026-10-23T12:00:01Z")>
    Public Async Function Get_takes_the_server_when_it_is_later_or_the_times_are_equal(storedAt As String) As Task
        _drive.ServerNow = New DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero)
        Await SendFromLaptopAsync(TextSnapshot("from drive"))
        Using server As New ClipboardTestServer()
            server.Stored = ClipboardText.ToWireBytes("from the server")
            server.StoredAt = storedAt
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "One"))
                rig.Drive = PathOf(IdB, "Tablet")

                Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())

                Assert.True(result.Succeeded)
                Assert.Equal(ClipboardLineCode.ReceivedValue, result.Lines(0).Code)
                Assert.Equal(ClipboardText.ToWireBytes("from the server"), rig.Port.PlacedText)
                Assert.Equal(1, server.Gets)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_drive_that_cannot_be_asked_is_one_line_and_never_hides_the_value_of_a_server() As Task
        _drive.FailWhen(Function(r) True, 100000, 503)
        Using server As New ClipboardTestServer()
            server.Stored = ClipboardText.ToWireBytes("from the server")
            Using rig As New ClipboardTestRig()
                rig.Targets.Add(ClipboardTestKit.TargetOf(server, "One"))
                rig.Drive = PathOf(IdB, "Tablet")

                Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())

                Assert.True(result.Succeeded)
                Assert.Equal({ClipboardLineCode.ReceivedValue, ClipboardLineCode.DriveUnavailable}, result.Lines.Select(Function(l) l.Code).ToArray())
                Assert.Equal(ClipboardText.ToWireBytes("from the server"), rig.Port.PlacedText)
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------ refusals and failures

    <Fact>
    Public Async Function A_value_that_does_not_fit_is_refused_with_the_free_space_before_a_byte_is_uploaded() As Task
        _drive.QuotaLimit = 200L * 1024 * 1024
        _drive.QuotaUsage = 190L * 1024 * 1024
        Using rig As New ClipboardTestRig()
            rig.Drive = PathOf(IdA, "Laptop")
            Dim wire(9 * 1024 * 1024 - 1) As Byte
            rig.Port.Snapshot = New ClipboardSnapshot(ClipboardReadKind.Text, wire, "", wire.LongLength)

            Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())

            Assert.False(result.Succeeded)
            Dim line As ClipboardLine = Assert.Single(result.Lines)
            Assert.Equal(ClipboardLineCode.DriveFull, line.Code)
            ' 1% of the limit stays in reserve (item CS).
            Assert.Equal("8", line.Detail)
            Assert.Equal(0, _drive.CountNamed(ExchangeDrivePackets.ClipboardTextFileName))
            Assert.Contains("8", ClipboardMessages.TextOf(line))
        End Using
    End Function

    <Fact>
    Public Async Function A_value_over_the_largest_packet_is_refused_before_the_clipboard_value_is_touched_again() As Task
        Using rig As New ClipboardTestRig()
            rig.Drive = PathOf(IdA, "Laptop")
            rig.Port.Snapshot = New ClipboardSnapshot(ClipboardReadKind.OneFile, Nothing, Path.Combine(rig.Source, "no-such-file.bin"), ExchangeDrivePackets.DefaultPacketBytes + 1)

            Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())

            Assert.False(result.Succeeded)
            Dim line As ClipboardLine = Assert.Single(result.Lines)
            Assert.Equal(ClipboardLineCode.OverDriveLimit, line.Code)
            Assert.Equal("5", line.Detail)
            Assert.Equal(0, _drive.RequestCount)
        End Using
    End Function

    <Fact>
    Public Async Function A_sign_in_that_cannot_be_used_says_so_on_send_and_on_get_and_places_nothing() As Task
        Using rig As New ClipboardTestRig()
            rig.Drive = New ClipboardDrivePath(Nothing, Nothing)
            rig.Port.Snapshot = TextSnapshot("value")

            Dim sent As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())
            Assert.False(sent.Succeeded)
            Assert.Equal(ClipboardLineCode.DriveSignedOut, Assert.Single(sent.Lines).Code)

            Dim got As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
            Assert.False(got.Succeeded)
            Assert.Equal(ClipboardLineCode.DriveSignedOut, Assert.Single(got.Lines).Code)
            Assert.Equal(0, rig.Port.Places)
        End Using
    End Function

    <Fact>
    Public Async Function The_daily_limit_is_its_own_sentence_and_is_not_retried() As Task
        _drive.FailWhen(Function(r) r.Method = "POST" AndAlso r.Path.StartsWith("/upload", StringComparison.Ordinal), 100000, 403, "dailyLimitExceeded")
        Using rig As New ClipboardTestRig()
            rig.Drive = PathOf(IdA, "Laptop")
            rig.Port.Snapshot = TextSnapshot("value")

            Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())

            Assert.False(result.Succeeded)
            Assert.Equal(ClipboardLineCode.DriveDailyLimit, Assert.Single(result.Lines).Code)
        End Using
    End Function

    <Fact>
    Public Async Function A_value_replaced_under_the_download_asks_for_one_more_press() As Task
        Await SendFromLaptopAsync(TextSnapshot("value"))
        Dim payload As String = _drive.Find(ExchangeDrivePackets.ClipboardTextFileName).Id
        _drive.FailWhen(Function(r) r.Method = "GET" AndAlso r.Path.EndsWith(payload, StringComparison.Ordinal), 100000, 404)
        Using rig As New ClipboardTestRig()
            rig.Drive = PathOf(IdB, "Tablet")
            Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
            Assert.False(result.Succeeded)
            Assert.Equal(ClipboardLineCode.DriveValueReplaced, Assert.Single(result.Lines).Code)
            Assert.Equal(0, rig.Port.Places)
        End Using
    End Function

    <Fact>
    Public Async Function A_text_that_fails_its_hash_is_not_placed() As Task
        Await SendFromLaptopAsync(TextSnapshot("value"))
        _drive.Find(ExchangeDrivePackets.ClipboardTextFileName).Content = ClipboardText.ToWireBytes("VALUE")
        Using rig As New ClipboardTestRig()
            rig.Drive = PathOf(IdB, "Tablet")
            Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
            Assert.False(result.Succeeded)
            Assert.Equal(ClipboardLineCode.Incomplete, Assert.Single(result.Lines).Code)
            Assert.Equal(0, rig.Port.Places)
        End Using
    End Function

    <Fact>
    Public Async Function A_file_that_fails_its_hash_leaves_nothing_in_the_folder() As Task
        Dim content As Byte() = ClipboardTestKit.Pattern(1024)
        Using sender As New ClipboardTestRig()
            Dim source As String = Path.Combine(sender.Source, "data.bin")
            File.WriteAllBytes(source, content)
            Await SendFromLaptopAsync(New ClipboardSnapshot(ClipboardReadKind.OneFile, Nothing, source, content.LongLength))
        End Using
        _drive.Find("data.bin").Content = ClipboardTestKit.Pattern(1023).Concat(New Byte() {0}).ToArray()
        Using rig As New ClipboardTestRig()
            rig.Drive = PathOf(IdB, "Tablet")
            Dim result As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
            Assert.False(result.Succeeded)
            Assert.Equal(ClipboardLineCode.Incomplete, Assert.Single(result.Lines).Code)
            Assert.Equal(0, rig.Port.Places)
            Assert.Empty(Directory.GetFiles(rig.Folder))
        End Using
    End Function

    <Fact>
    Public Async Function A_press_cancelled_before_drive_sends_nothing_through_it() As Task
        Using cancelled As New CancellationTokenSource()
            cancelled.Cancel()
            Using rig As New ClipboardTestRig()
                rig.Drive = PathOf(IdA, "Laptop")
                rig.Port.Snapshot = TextSnapshot("value")
                Dim result As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, cancelled.Token)
                Assert.False(result.Succeeded)
                Assert.Equal(ClipboardLineCode.Cancelled, result.Lines.Last().Code)
                Assert.Equal(0, _drive.CountNamed(ExchangeDrivePackets.ClipboardTextFileName))
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------ rule 10, rule 9, the words

    <Fact>
    Public Async Function The_log_holds_neither_the_value_nor_a_file_name_nor_a_path_nor_a_device() As Task
        Const SecretText As String = "TOP-SECRET-VALUE-4411"
        Const SecretFile As String = "TOP-SECRET-NAME-5522.txt"
        Const SecretDevice As String = "TOP-SECRET-DEVICE-6633"
        Using rig As New ClipboardTestRig()
            rig.Drive = PathOf(IdA, SecretDevice)
            rig.Port.Snapshot = TextSnapshot(SecretText)
            Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())
            rig.Drive = PathOf(IdB, "Tablet")
            Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())

            Dim source As String = Path.Combine(rig.Source, SecretFile)
            File.WriteAllText(source, SecretText)
            rig.Drive = PathOf(IdA, SecretDevice)
            rig.Port.Snapshot = New ClipboardSnapshot(ClipboardReadKind.OneFile, Nothing, source, New FileInfo(source).Length)
            Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())
            rig.Drive = PathOf(IdB, "Tablet")
            Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())

            Dim logged As String = String.Join(vbLf, rig.Logged())
            Assert.Contains("via=drive", logged)
            For Each secret As String In {SecretText, SecretFile, SecretDevice, rig.Folder, rig.Source}
                Assert.DoesNotContain(secret, logged, StringComparison.OrdinalIgnoreCase)
            Next
        End Using
    End Function

    <Fact>
    Public Sub With_no_google_sign_in_the_path_is_absent()
        Dim before As Func(Of Boolean) = DriveChannelHosts.TokenPresent
        Try
            DriveChannelHosts.TokenPresent = Function() False
            Assert.False(ClipboardDrivePath.IsOffered())
            Assert.Null(ClipboardDrivePath.ForThisProcess())
        Finally
            DriveChannelHosts.TokenPresent = before
        End Try
    End Sub

    <Fact>
    Public Async Function With_neither_a_server_nor_a_sign_in_nothing_reads_the_clipboard() As Task
        Using rig As New ClipboardTestRig()
            rig.Port.Snapshot = TextSnapshot("value")
            Dim sent As ClipboardRunResult = Await rig.Exchange().SendAsync(Nothing, ClipboardTestKit.Token())
            Dim got As ClipboardRunResult = Await rig.Exchange().GetAsync(Nothing, ClipboardTestKit.Token())
            Assert.Equal(ClipboardLineCode.NoServer, Assert.Single(sent.Lines).Code)
            Assert.Equal(ClipboardLineCode.NoServer, Assert.Single(got.Lines).Code)
            Assert.Equal(0, rig.Port.Reads)
            Assert.Empty(rig.Logged())
        End Using
    End Function

    <Fact>
    Public Async Function The_size_query_adds_one_row_for_google_drive() As Task
        Await SendFromLaptopAsync(TextSnapshot("value"))
        Using rig As New ClipboardTestRig()
            rig.Drive = PathOf(IdB, "Tablet")
            Dim row As ClipboardSlotStatus = Assert.Single(Await rig.Exchange().QueryClipboardSizeAsync(ClipboardTestKit.Token()))
            Assert.Equal(ClipboardDrivePath.SlotLabel, row.Label)
            Assert.Contains("Google Drive", row.DisplayText())
            Assert.NotNull(row.Drive.Value)
        End Using
        Using rig As New ClipboardTestRig()
            rig.Drive = New ClipboardDrivePath(Nothing, Nothing)
            Dim row As ClipboardSlotStatus = Assert.Single(Await rig.Exchange().QueryClipboardSizeAsync(ClipboardTestKit.Token()))
            Assert.Contains("Google Drive", row.DisplayText())
            Assert.Equal(ExchangeDriveFailure.SignedOut, row.Drive.Failure)
        End Using
    End Function

    <Fact>
    Public Sub The_words_of_the_actions_follow_what_the_person_has()
        For Each isSend As Boolean In {True, False}
            Dim serverOnly As String = ClipboardMessages.ActionText(isSend, True, False)
            Dim driveOnly As String = ClipboardMessages.ActionText(isSend, False, True)
            Dim both As String = ClipboardMessages.ActionText(isSend, True, True)
            Assert.DoesNotContain("Google Drive", serverOnly)
            Assert.Contains("Google Drive", driveOnly)
            Assert.Contains("Google Drive", both)
            Assert.Equal(3, {serverOnly, driveOnly, both}.Distinct().Count())
            ' A leftover shortcut of a device that lost both still reads as the server's action.
            Assert.Equal(serverOnly, ClipboardMessages.ActionText(isSend, False, False))
        Next
        Assert.DoesNotContain("Google Drive", ClipboardMessages.BlockTitle(True, False))
        Assert.Contains("Google Drive", ClipboardMessages.BlockTitle(False, True))
        Assert.Contains("Google Drive", ClipboardMessages.BlockTitle(True, True))
    End Sub

    <Fact>
    Public Sub Every_drive_line_has_a_sentence_that_names_google_drive()
        For Each code As ClipboardLineCode In {ClipboardLineCode.SentViaDrive, ClipboardLineCode.EmptyViaDrive, ClipboardLineCode.DriveFull, ClipboardLineCode.DriveSignedOut,
                                               ClipboardLineCode.DriveOffline, ClipboardLineCode.DriveDailyLimit, ClipboardLineCode.DriveValueReplaced, ClipboardLineCode.DriveUnavailable,
                                               ClipboardLineCode.OverDriveLimit, ClipboardLineCode.DriveBadName, ClipboardLineCode.ReceivedValueViaDrive, ClipboardLineCode.ReceivedFileViaDrive}
            Assert.Contains("Google Drive", ClipboardMessages.TextOf(New ClipboardLine(code, "", "", "Laptop", "5")))
        Next
    End Sub

End Class
