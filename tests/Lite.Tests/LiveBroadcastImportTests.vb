#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports Xunit

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md sections 3, 4 and the contract-sync work items 3-5: what the
' importer accepts and - as important - what it refuses, and by which name. The fixtures are built here from the
' contract's examples (LIVE-BROADCAST section 3.1); nothing is a stored blob that could go stale.

Public Class LiveBroadcastImportTests

    Private Const PhoneJson As String = "{""schemaVersion"":1,""url"":""http://192.168.1.50:8768/live-audio.aac"",""mode"":""AUDIO_ONLY"",""title"":""Pixel"",""sourceId"":""phone-1""}"

    Private Shared Function GzipOf(text As String) As Byte()
        Dim json As Byte() = Encoding.UTF8.GetBytes(text)
        Using output As New MemoryStream()
            Using gz As New GZipStream(output, CompressionLevel.Optimal, True)
                gz.Write(json, 0, json.Length)
            End Using
            Return output.ToArray()
        End Using
    End Function

    Private Shared Function Barcode(compressed As Byte()) As String
        Return "FMSBCAST1:" & Convert.ToBase64String(compressed)
    End Function

    Private Shared Function Concat(ParamArray parts As Byte()()) As Byte()
        Return parts.SelectMany(Function(p) p).ToArray()
    End Function

#Region "One gzip member, nothing after it (section 3.3)"

    <Fact>
    Public Sub One_Gzip_Member_Is_Accepted()
        Assert.Equal(LiveReadStatus.Ok, LiveBroadcastReader.ReadText(Barcode(GzipOf(PhoneJson))).Status)
    End Sub

    <Fact>
    Public Sub Bytes_After_The_Member_Make_It_Invalid()
        Dim member As Byte() = GzipOf(PhoneJson)
        Dim withGarbage As Byte() = Concat(member, New Byte() {1, 2, 3})
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastReader.ReadText(Barcode(withGarbage)).Status)
    End Sub

    <Fact>
    Public Sub A_Second_Member_Makes_It_Invalid_Even_When_It_Is_A_Valid_Gzip()
        Dim first As Byte() = GzipOf(PhoneJson)
        Dim second As Byte() = GzipOf(PhoneJson)
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastReader.ReadText(Barcode(Concat(first, second))).Status)
    End Sub

    <Fact>
    Public Sub A_Damaged_Trailer_Makes_It_Invalid()
        Dim member As Byte() = GzipOf(PhoneJson)
        member(member.Length - 8) = CByte(member(member.Length - 8) Xor &HFF)       ' the CRC-32
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastReader.ReadText(Barcode(member)).Status)

        Dim wrongSize As Byte() = GzipOf(PhoneJson)
        wrongSize(wrongSize.Length - 4) = CByte(wrongSize(wrongSize.Length - 4) Xor &H1)  ' ISIZE
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastReader.ReadText(Barcode(wrongSize)).Status)
    End Sub

    <Fact>
    Public Sub A_Member_Cut_Short_Is_Invalid()
        Dim member As Byte() = GzipOf(PhoneJson)
        Dim cut As Byte() = member.Take(member.Length - 5).ToArray()
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastReader.ReadText(Barcode(cut)).Status)
    End Sub

    <Fact>
    Public Sub A_Header_With_A_File_Name_Is_Walked_Correctly()
        ' RFC 1952: flags FNAME (8) and FCOMMENT (16) put zero-terminated strings between the header and the body.
        Dim json As Byte() = Encoding.UTF8.GetBytes(PhoneJson)
        Dim body As Byte()
        Using output As New MemoryStream()
            Using deflate As New DeflateStream(output, CompressionLevel.Optimal, True)
                deflate.Write(json, 0, json.Length)
            End Using
            body = output.ToArray()
        End Using

        Dim crc As UInteger = &HFFFFFFFFUI
        For Each b As Byte In json
            crc = crc Xor b
            For k As Integer = 0 To 7
                crc = If((crc And 1UI) <> 0UI, &HEDB88320UI Xor (crc >> 1), crc >> 1)
            Next
        Next
        crc = crc Xor &HFFFFFFFFUI

        Dim header As Byte() = Concat(New Byte() {&H1F, &H8B, 8, 8 Or 16, 0, 0, 0, 0, 0, 255},
                                      Encoding.ASCII.GetBytes("broadcast.json"), New Byte() {0},
                                      Encoding.ASCII.GetBytes("a comment"), New Byte() {0})
        Dim trailer As Byte() = Concat(BitConverter.GetBytes(crc), BitConverter.GetBytes(CUInt(json.Length)))

        Assert.Equal(LiveReadStatus.Ok, LiveBroadcastReader.ReadText(Barcode(Concat(header, body, trailer))).Status)
    End Sub

    <Fact>
    Public Sub Something_That_Is_Not_Gzip_At_All_Is_Invalid_Not_Too_Large()
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastReader.ReadText("FMSBCAST1:" & Convert.ToBase64String(Encoding.UTF8.GetBytes(PhoneJson))).Status)
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastReader.ReadText("FMSBCAST1:").Status)
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastReader.ReadText("FMSBCAST1:not base64 at all!").Status)
    End Sub

    <Fact>
    Public Sub Compressed_Input_Of_65537_Decompressed_Bytes_Is_Too_Large_Before_Any_Json_Parse()
        ' Not even JSON: if the size were checked after parsing this would be InvalidDocument.
        Dim bomb As Byte() = GzipOf(New String("A"c, 65537))
        Assert.Equal(LiveReadStatus.TooLarge, LiveBroadcastReader.ReadText(Barcode(bomb)).Status)
        ' And exactly at the limit it is merely not a document.
        Dim atLimit As Byte() = GzipOf(New String("A"c, 65536))
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastReader.ReadText(Barcode(atLimit)).Status)
    End Sub

#End Region

#Region "The mode is judged first, the address after (section 3.2)"

    <Fact>
    Public Sub An_Unsupported_Mode_Wins_Over_A_Playable_Endpoint_And_Over_A_Loopback_Address()
        Dim withEndpoint As String = "{""schemaVersion"":1,""url"":""rtsp://192.168.1.60:8554/"",""mode"":""HOLOGRAM"",""endpoints"":[" &
            "{""url"":""http://192.168.1.60:8000/live.aac"",""transport"":""HTTP"",""mode"":""AUDIO_ONLY""}]}"
        Assert.Equal(LiveReadStatus.UnsupportedMode, LiveBroadcastReader.ReadText(withEndpoint).Status)

        Dim loopbackVideo As String = "{""schemaVersion"":1,""url"":""rtsp://127.0.0.1:8554/"",""mode"":""HOLOGRAM""}"
        Assert.Equal(LiveReadStatus.UnsupportedMode, LiveBroadcastReader.ReadText(loopbackVideo).Status)
    End Sub

    <Fact>
    Public Sub An_Unknown_Mode_Is_Unsupported_And_Never_Guessed_From_The_Address()
        Dim json As String = "{""schemaVersion"":1,""url"":""http://192.168.1.60:8768/live-audio.aac"",""mode"":""HOLOGRAM""}"
        Assert.Equal(LiveReadStatus.UnsupportedMode, LiveBroadcastReader.ReadText(json).Status)
    End Sub

    <Fact>
    Public Sub A_Loopback_Endpoint_Is_Skipped_And_The_Top_Level_Address_Still_Plays()
        Dim json As String = "{""schemaVersion"":1,""url"":""http://192.168.1.60:8768/live-audio.aac"",""mode"":""AUDIO_ONLY"",""endpoints"":[" &
            "{""url"":""http://127.0.0.1:8768/live-audio.aac"",""transport"":""HTTP"",""mode"":""AUDIO_ONLY""}]}"
        Dim result As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Dim chosen As LiveBroadcastEndpoint = LiveEndpointSelector.SelectEndpoint(result.Descriptor)
        Assert.Equal("http://192.168.1.60:8768/live-audio.aac", chosen.Url)
    End Sub

    <Theory>
    <InlineData("http://127.0.0.1:8768/live-audio.aac")>
    <InlineData("http://127.9.9.9/live-audio.aac")>
    <InlineData("http://localhost:8768/live-audio.aac")>
    <InlineData("http://LOCALHOST:8768/live-audio.aac")>
    <InlineData("http://[::1]:8768/live-audio.aac")>
    <InlineData("http://0.0.0.0:8768/live-audio.aac")>
    <InlineData("http://[::]:8768/live-audio.aac")>
    Public Sub A_Loopback_Or_Unspecified_Host_Is_Refused_By_Name_In_Every_Hand_Off_Form(url As String)
        Dim json As String = "{""schemaVersion"":1,""url"":""" & url & """,""mode"":""AUDIO_ONLY""}"

        ' The same refusal through the four forms: text, barcode, link and file - one parser behind all of them.
        Assert.Equal(LiveReadStatus.LoopbackHost, LiveBroadcastReader.ReadText(json).Status)
        Assert.Equal(LiveReadStatus.LoopbackHost, LiveBroadcastReader.ReadText(Barcode(GzipOf(json))).Status)
        Assert.Equal(LiveReadStatus.LoopbackHost, LiveBroadcastLinkReader.ReadLink("fmsbcast://import?payload=" & Uri.EscapeDataString(Barcode(GzipOf(json)))).Status)
        Assert.Equal(LiveReadStatus.LoopbackHost, LiveBroadcastLinkReader.ReadLink("intent://import?payload=" & Uri.EscapeDataString(Barcode(GzipOf(json))) & "#Intent;scheme=fmsbcast;package=x;end").Status)

        Dim file As String = Path.Combine(Path.GetTempPath(), "fms-live-" & Guid.NewGuid().ToString("N") & ".fmsbcast")
        Try
            IO.File.WriteAllText(file, json)
            Assert.Equal(LiveReadStatus.LoopbackHost, LiveBroadcastReader.ReadFile(file).Status)
            Assert.Equal(LiveReadStatus.LoopbackHost, LiveBroadcastIngress.TryRead(file).Status)
        Finally
            If IO.File.Exists(file) Then IO.File.Delete(file)
        End Try
    End Sub

    <Fact>
    Public Sub An_Address_With_No_Host_Is_A_Damaged_Document_Not_A_Loopback()
        Dim json As String = "{""schemaVersion"":1,""url"":""file:///C:/music/live.aac"",""mode"":""AUDIO_ONLY""}"
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastReader.ReadText(json).Status)
    End Sub

    <Fact>
    Public Sub An_Audio_Descriptor_With_No_Address_We_Can_Play_Is_Unsupported_And_Nothing_Is_Written()
        ' AUDIO_ONLY, but the only address is RTSP: a correct descriptor this build cannot play (section 3.2), so it
        ' is refused as such - not "Ok" and then a failure at play time, after it was remembered.
        Dim rtspOnly As String = "{""schemaVersion"":1,""url"":""rtsp://192.168.1.60:8554/"",""mode"":""AUDIO_ONLY""}"
        Assert.Equal(LiveReadStatus.UnsupportedMode, LiveBroadcastReader.ReadText(rtspOnly).Status)

        ' A playable HTTP endpoint beside an RTSP top level is enough.
        Dim mixed As String = "{""schemaVersion"":1,""url"":""rtsp://192.168.1.60:8554/"",""mode"":""AUDIO_ONLY"",""endpoints"":[" &
            "{""url"":""http://192.168.1.60:8768/live-audio.aac"",""transport"":""HTTP"",""mode"":""AUDIO_ONLY""}]}"
        Assert.Equal(LiveReadStatus.Ok, LiveBroadcastReader.ReadText(mixed).Status)
    End Sub

#End Region

#Region "Links (section 4.3)"

    Private Shared Function Intent(payload As String, Optional extras As String = "scheme=fmsbcast;package=com.sza.fastmediasorter;end") As String
        Return "intent://import?payload=" & payload & "#Intent;" & extras
    End Function

    <Fact>
    Public Sub A_Percent_Encoded_Payload_Is_Decoded_Exactly_Once()
        Dim code As String = Barcode(GzipOf(PhoneJson))
        Dim once As String = Uri.EscapeDataString(code)
        Assert.Equal(LiveReadStatus.Ok, LiveBroadcastLinkReader.ReadLink(Intent(once)).Status)

        ' Encoded twice means one decode leaves percent signs behind: not a payload.
        Dim twice As String = Uri.EscapeDataString(once)
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastLinkReader.ReadLink(Intent(twice)).Status)
    End Sub

    <Theory>
    <InlineData("FMSBCAST1%3A%ZZ")>
    <InlineData("FMSBCAST1%3A%4")>
    <InlineData("FMSBCAST1%3Aabc%")>
    <InlineData("%G1")>
    Public Sub Malformed_Percent_Encoding_Is_Refused(payload As String)
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastLinkReader.ReadLink(Intent(payload)).Status)
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastLinkReader.ReadLink("fmsbcast://import?payload=" & payload).Status)
    End Sub

    <Fact>
    Public Sub A_Missing_Or_Empty_Payload_Is_Refused_And_Not_Mistaken_For_Something_Else()
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastLinkReader.ReadLink("fmsbcast://import").Status)
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastLinkReader.ReadLink("fmsbcast://import?other=1").Status)
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastLinkReader.ReadLink("fmsbcast://import?payload=").Status)
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastLinkReader.ReadLink("intent://import#Intent;scheme=fmsbcast;end").Status)
    End Sub

    <Fact>
    Public Sub A_Payload_That_Is_Not_An_Fmsbcast1_Code_Is_Refused()
        Dim json As String = Uri.EscapeDataString(PhoneJson)
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastLinkReader.ReadLink(Intent(json)).Status)
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastLinkReader.ReadLink("fmsbcast://import?payload=" & json).Status)
        Assert.Equal(LiveReadStatus.InvalidDocument, LiveBroadcastLinkReader.ReadLink(Intent(Uri.EscapeDataString("http://192.168.1.5/live-audio"))).Status)
    End Sub

    <Fact>
    Public Sub The_Intent_Parameters_After_The_Uri_Are_Never_Read_Or_Followed()
        Dim code As String = Uri.EscapeDataString(Barcode(GzipOf(PhoneJson)))
        ' A browser fallback page, the package hint and a hostile "payload" hiding in an extra: none of them counts.
        Dim extras As String = "scheme=fmsbcast;package=com.sza.fastmediasorter;" &
                               "S.browser_fallback_url=https%3A%2F%2Fexample.com%2Fbroadcast-import.html%3Fpayload%3Dx;S.payload=evil;end"
        Dim result As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink(Intent(code, extras))
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Assert.Equal("http://192.168.1.50:8768/live-audio.aac", result.Descriptor.Url)
    End Sub

    <Fact>
    Public Sub An_Intent_For_Another_Scheme_Is_Not_Ours()
        Assert.Equal(LiveReadStatus.NotBroadcast, LiveBroadcastLinkReader.ReadLink("intent://example.com/page#Intent;scheme=https;package=com.android.chrome;end").Status)
    End Sub

    <Fact>
    Public Sub A_Plus_In_The_Payload_Stays_A_Plus()
        ' Base64 contains '+'; this is not form encoding, so it must never become a space.
        Assert.Equal("a+b/c=", LiveBroadcastLinkReader.StrictPercentDecode("a+b/c="))
        Assert.Equal("a+b", LiveBroadcastLinkReader.StrictPercentDecode("a%2Bb"))
        Assert.Null(LiveBroadcastLinkReader.StrictPercentDecode("%"))
        Assert.Null(LiveBroadcastLinkReader.StrictPercentDecode("100%zz"))
    End Sub

#End Region

#Region "LAN IPv4 is any non-loopback, non-unspecified IPv4 literal (section 4.3, work item 5)"

    <Theory>
    <InlineData("192.168.1.50")>       ' home Wi-Fi
    <InlineData("10.0.0.7")>           ' private 10/8
    <InlineData("172.16.4.9")>         ' private 172.16/12
    <InlineData("172.31.255.1")>
    <InlineData("100.64.0.5")>         ' carrier-grade NAT / Tailscale-style overlay 100.64/10
    <InlineData("100.127.255.254")>
    <InlineData("169.254.12.34")>      ' link-local (a phone hotspot with no DHCP)
    <InlineData("203.0.113.9")>        ' a public literal: the user typed an address, it is not guessed at
    Public Sub Every_Non_Loopback_Ipv4_Literal_Is_One_Of_Ours_On_The_Three_Paths(host As String)
        For Each path As String In New String() {"/live-audio", "/live-audio.aac", "/listen"}
            Dim result As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink("http://" & host & ":8768" & path)
            Assert.Equal(LiveReadStatus.Ok, result.Status)
            Assert.True(result.Descriptor.IsLive.GetValueOrDefault())
            Assert.Equal(LiveBroadcastMode.AudioOnly, result.Descriptor.ModeValue)
        Next
    End Sub

    <Theory>
    <InlineData("http://127.0.0.1:8768/live-audio")>
    <InlineData("http://127.5.5.5:8768/listen")>
    <InlineData("http://0.0.0.0:8768/live-audio")>
    <InlineData("http://[::1]:8768/live-audio")>
    <InlineData("http://[fe80::1]:8768/live-audio")>
    <InlineData("http://phone.local:8768/live-audio")>
    <InlineData("http://example.com/live-audio")>
    <InlineData("http://192.168.1.50:8768/live-audio/extra")>
    <InlineData("http://192.168.1.50:8768/live-audio.aac.mp3")>
    <InlineData("http://192.168.1.50:8768/LIVE-AUDIO")>
    <InlineData("http://192.168.1.50:8768/")>
    Public Sub Everything_Else_Needs_The_User_To_Say_It_Is_Live(url As String)
        ' "The exact path" (section 4.3): near misses stay plain addresses, played as ordinary media unless marked live.
        Assert.Equal(LiveReadStatus.NotBroadcast, LiveBroadcastLinkReader.ReadLink(url).Status)
    End Sub

    <Fact>
    Public Sub A_Query_Or_A_Fragment_Is_Not_Part_Of_The_Path()
        Assert.Equal(LiveReadStatus.Ok, LiveBroadcastLinkReader.ReadLink("http://192.168.1.50:8768/listen?x=1#frag").Status)
    End Sub

#End Region

#Region "One door for every channel (section 4.2)"

    <Fact>
    Public Sub Ingress_Routes_By_The_Shape_Of_The_Text()
        Dim code As String = Barcode(GzipOf(PhoneJson))
        Assert.Equal(LiveReadStatus.Ok, LiveBroadcastIngress.TryRead(code).Status)
        Assert.Equal(LiveReadStatus.Ok, LiveBroadcastIngress.TryRead("  """ & code & """  ").Status)      ' quotes and spaces of a command line
        Assert.Equal(LiveReadStatus.Ok, LiveBroadcastIngress.TryRead("fmsbcast://import?payload=" & Uri.EscapeDataString(code)).Status)
        Assert.Equal(LiveReadStatus.Ok, LiveBroadcastIngress.TryRead(Intent(Uri.EscapeDataString(code))).Status)

        ' Not hand-offs at all: the caller carries on with a path or an address.
        Assert.Null(LiveBroadcastIngress.TryRead("C:\Pictures\cat.jpg"))
        Assert.Null(LiveBroadcastIngress.TryRead("\\server\share\clip.mp4"))
        Assert.Null(LiveBroadcastIngress.TryRead("http://192.168.1.50:8768/live-audio"))
        Assert.Null(LiveBroadcastIngress.TryRead("sftp://user@host/path"))
        Assert.Null(LiveBroadcastIngress.TryRead(""))
        Assert.Null(LiveBroadcastIngress.TryRead("   "))
    End Sub

    <Fact>
    Public Sub A_File_Is_Routed_by_Extension_But_The_Extension_Is_Not_Trusted()
        Dim good As String = Path.Combine(Path.GetTempPath(), "fms-live-" & Guid.NewGuid().ToString("N") & ".fmsbcast")
        Dim junk As String = Path.Combine(Path.GetTempPath(), "fms-live-" & Guid.NewGuid().ToString("N") & ".FMSBCAST")
        Dim missing As String = Path.Combine(Path.GetTempPath(), "fms-live-" & Guid.NewGuid().ToString("N") & ".fmsbcast")
        Try
            IO.File.WriteAllText(good, PhoneJson)
            IO.File.WriteAllText(junk, "this is a holiday photo, honest")

            Assert.Equal(LiveReadStatus.Ok, LiveBroadcastIngress.TryRead(good).Status)
            Dim refused As LiveBroadcastReadResult = LiveBroadcastIngress.TryRead(junk)
            Assert.NotNull(refused)
            Assert.NotEqual(LiveReadStatus.Ok, refused.Status)
            ' A file that is not there is still a refusal with a name, never an exception.
            Assert.NotEqual(LiveReadStatus.Ok, LiveBroadcastIngress.TryRead(missing).Status)
        Finally
            If IO.File.Exists(good) Then IO.File.Delete(good)
            If IO.File.Exists(junk) Then IO.File.Delete(junk)
        End Try
    End Sub

    <Fact>
    Public Sub A_File_Over_64_KiB_Is_Too_Large_Without_Being_Read()
        Dim big As String = Path.Combine(Path.GetTempPath(), "fms-live-" & Guid.NewGuid().ToString("N") & ".fmsbcast")
        Try
            IO.File.WriteAllBytes(big, New Byte(65536) {})
            Assert.Equal(LiveReadStatus.TooLarge, LiveBroadcastIngress.TryRead(big).Status)
        Finally
            If IO.File.Exists(big) Then IO.File.Delete(big)
        End Try
    End Sub

    <Fact>
    Public Sub File_Command_Line_And_Drop_Share_One_Parser_And_One_Entry_Point()
        ' A drop, a forwarded launch and the command line all end in ProcessArgument (see Main_Form.DragDrop / the
        ' WM_COPYDATA receiver); it asks TryHandleLiveBroadcastArgument first, and that asks the one ingress.
        Dim src As String = FindSourceDirectory()
        Dim lifecycle As String = IO.File.ReadAllText(Path.Combine(src, "Main_Form.Lifecycle.vb"))
        Assert.Contains("TryHandleLiveBroadcastArgument(argument_For_Path)", lifecycle, StringComparison.Ordinal)

        Dim callers As List(Of String) = Directory.GetFiles(src, "*.vb", SearchOption.AllDirectories).
            Where(Function(f) IO.File.ReadAllText(f).Contains("LiveBroadcastIngress.TryRead(")).
            Select(Function(f) Path.GetFileName(f)).OrderBy(Function(n) n).ToList()
        Assert.Equal(New String() {"LiveBroadcast_Form.vb", "Main_Form.LiveBroadcast.vb"}, callers.ToArray())
    End Sub

    Private Shared Function FindSourceDirectory() As String
        Dim dir As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If File.Exists(Path.Combine(candidate, "Main_Form.Lifecycle.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src was not found above the test output")
    End Function

#End Region

#Region "Live playback has no resume position (sections 8, 10.2)"

    <Theory>
    <InlineData("http://192.168.1.50:8768/live-audio.aac")>
    <InlineData("http://192.168.1.50:8768/live-audio")>
    <InlineData("http://192.168.1.55:9090/listen")>
    <InlineData("https://relay.example/v2/b/abc/stream")>
    Public Sub A_Live_Address_Is_A_Remote_Address_So_The_Resume_Store_Never_Keys_On_It(url As String)
        ' CanRememberPositionFor refuses anything MediaUrl calls remote ("a stream has no file to key a position by");
        ' every live address is one, so nothing is ever read from or written to the resume store for a broadcast.
        Assert.True(MediaUrl.IsRemote(url))
    End Sub

    <Fact>
    Public Sub The_Live_Window_Code_Never_Touches_The_Resume_Store()
        Dim src As String = FindSourceDirectory()
        Dim live As String = IO.File.ReadAllText(Path.Combine(src, "Main_Form.LiveBroadcast.vb"))
        For Each forbidden As String In New String() {"QueueRememberedVideoPosition", "StoreVideoPosition", "RememberedVideoPosition(", "RememberVideoFinished", "pending_Video_Seek ="}
            Assert.DoesNotContain(forbidden, live, StringComparison.Ordinal)
        Next
        ' The one RememberCurrentVideoPosition call is for the OUTGOING media, before the broadcast takes the screen.
        Dim calls As Integer = live.Split(New String() {"RememberCurrentVideoPosition()"}, StringSplitOptions.None).Length - 1
        Assert.Equal(1, calls)
        Assert.Contains("If is_Vlc_Playing Then RememberCurrentVideoPosition()", live, StringComparison.Ordinal)
    End Sub

#End Region

#Region "Outcomes are never merged (section 13 item 8)"

    <Fact>
    Public Sub Every_Read_Status_Has_Its_Own_Sentence_And_Only_A_Damaged_Document_Is_Called_Damaged()
        Dim sentences As New Dictionary(Of LiveReadStatus, String)()
        For Each status As LiveReadStatus In [Enum].GetValues(GetType(LiveReadStatus))
            If status = LiveReadStatus.Ok Then Continue For
            sentences(status) = LiveBroadcastMessages.ForReadStatus(status)
        Next

        ' Two different outcomes never share a sentence...
        Dim duplicates As List(Of String) = sentences.Values.GroupBy(Function(s) s).Where(Function(g) g.Count() > 1).Select(Function(g) g.Key).ToList()
        ' ...except "not a broadcast" and nothing else: it is the catch-all and has its own text too.
        Assert.Empty(duplicates)

        ' A newer app's descriptor asks for an update; it is not "damaged".
        Assert.Equal(Localization.T("Требуется обновить приложение для этой трансляции"), sentences(LiveReadStatus.UnsupportedVersion))
        Assert.NotEqual(sentences(LiveReadStatus.InvalidDocument), sentences(LiveReadStatus.UnsupportedVersion))
        Assert.NotEqual(sentences(LiveReadStatus.InvalidDocument), sentences(LiveReadStatus.UnsupportedMode))
    End Sub

#End Region

End Class
#End If
