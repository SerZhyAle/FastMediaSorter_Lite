#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 204 section 2, row "Exports", and section 3 item 2 (the golden comparison moved here from 148): with no
''' exchange account and no Google sign-in the <c>.fmscfg</c> and the QR of a reference share are IDENTICAL to what the
''' build before the exchange program produced.
'''
''' The two files per export live in <c>tests\Lite.Tests\TestData\dual-mode</c> (linked into this project, beside the
''' viewer capture, under one PROVENANCE.txt whose hashes are checked FIRST). They are the output of the REAL
''' <see cref="ShareConfigBuilder"/> called with <c>exchange:=Nothing</c>, a fixed stamp, the language pinned to English
''' and every per-root parameter passed explicitly (the fallback reads the user's registry, which would make the
''' expected bytes a fact of one machine) - the inputs follow <c>ShareConfigCanonicalVectorTests</c>.
'''
''' Strictness: the <c>.fmscfg</c> bytes are compared byte for byte. The QR PNG file is committed and hashed, but it is
''' compared as the digest of its DECODED pixels, because the deflate stream inside a PNG may change with a runtime
''' servicing update while the picture does not - and the picture is what a phone scans.
'''
''' The fixtures are FROZEN "today" files (Q204-2): regenerated only on purpose, with the generator at the bottom,
''' and the regeneration shows in review as a changed file and a changed hash. Nothing in them is real: documentation
''' addresses (RFC 5737 / RFC 3849), a throw-away password and PIN, and a made-up host key fingerprint.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class DualModeShareBaselineTests

    Private Const FullName As String = "reference-share-full"
    Private Const LanName As String = "reference-share-lan"

    Private Const LanHost As String = "192.0.2.10"
    Private Const ForwardHost As String = "198.51.100.7"
    Private Const Ipv6Host As String = "2001:db8::10"

    Private Shared ReadOnly TopLevelKeys As String() = {
        "schemaVersion", "resourceName", "protocol", "accessPaths", "username", "password",
        "hostKeyFingerprintSha256", "roots", "createdAt", "accessNote"}

    ''' <summary>A word of the exchange: none may reach an export of a machine with no account.</summary>
    Private Shared ReadOnly ExchangeWords As New Regex("exchange|relay|tunnel|fmsx|deviceToken|pairing",
                                                       RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)

    Private Shared Function ReferenceStatus(roots As List(Of ShareFolder)) As WorkerStatus
        Return New WorkerStatus With {
            .Running = True,
            .ListenPort = 22022,
            .Username = "fms",
            .Password = "reference-password-not-a-secret",
            .Fingerprint = "SHA256:cmVmZXJlbmNlLWZpbmdlcnByaW50LWRvY3VtZW50YXRpb24",
            .Roots = roots,
            .Reachability = New WorkerReachability With {
                .LanAddress = LanHost,
                .ExternalHost = ForwardHost,
                .ExternalPort = 22022,
                .IsCgnat = False,
                .Ipv6Address = Ipv6Host
            }
        }
    End Function

    Private Const PhotosPath As String = "C:\fmscfg-reference\Photos"
    Private Const MusicPath As String = "C:\fmscfg-reference\Music"
    Private Const InboxPath As String = "C:\fmscfg-reference\Inbox"

    ''' <summary>The real builder, the exchange argument explicitly absent. Language is restored afterwards.</summary>
    Private Shared Function BuildReference(includeExternal As Boolean) As ShareConfigResult
        Dim roots As New List(Of ShareFolder) From {
            New ShareFolder With {.name = "Photos", .hostPath = PhotosPath, .readOnly = True},
            New ShareFolder With {.name = "Music", .hostPath = MusicPath, .readOnly = False},
            New ShareFolder With {.name = "Inbox", .hostPath = InboxPath, .readOnly = False}
        }
        Dim params As New Dictionary(Of String, ShareRootParams) From {
            {PhotosPath, New ShareRootParams()},
            {MusicPath, New ShareRootParams With {
                .Label = "Reference Music",
                .Profile = "audio_library",
                .MediaTypes = New List(Of String) From {"audio"},
                .Comment = "Reference share",
                .AccessPin = "1234",
                .SlideshowInterval = 15
            }},
            {InboxPath, New ShareRootParams With {
                .Label = "Drop Box",
                .IsDestination = True,
                .HasDestinationColor = True,
                .DestinationColorArgb = -14575885,
                .Comment = "Move target"
            }}
        }
        Dim saved As String = Localization.CurrentCode
        Localization.CurrentCode = "en"
        Try
            Dim res As ShareConfigResult = ShareConfigBuilder.Build(
                ReferenceStatus(roots),
                includeExternal:=includeExternal,
                includePassword:=True,
                exportOverrides:=Nothing,
                perRootOverrides:=Nothing,
                perRootParams:=params,
                stamp:=New ShareConfigStamp With {
                    .ResourceName = "Reference PC",
                    .CreatedAtUtc = New Date(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc)
                },
                exchange:=Nothing)
            Assert.NotNull(res)
            Assert.NotNull(res.QrPng)
            Assert.False(res.QrOverflow)
            Return res
        Finally
            Localization.CurrentCode = saved
        End Try
    End Function

    ''' <summary>SHA-256 over the decoded picture: width, height, then the pixels as 32-bit ARGB rows. Independent of the
    ''' PNG's palette, filter and deflate choices.</summary>
    Friend Shared Function PixelDigest(png As Byte()) As String
        Using ms As New MemoryStream(png)
            Using decoded As New Bitmap(ms)
                Using argb As Bitmap = decoded.Clone(New Rectangle(0, 0, decoded.Width, decoded.Height), PixelFormat.Format32bppArgb)
                    Dim data As BitmapData = argb.LockBits(New Rectangle(0, 0, argb.Width, argb.Height),
                                                           ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
                    Try
                        Dim rowBytes As Integer = argb.Width * 4
                        Dim all As Byte() = New Byte(8 + rowBytes * argb.Height - 1) {}
                        BitConverter.GetBytes(argb.Width).CopyTo(all, 0)
                        BitConverter.GetBytes(argb.Height).CopyTo(all, 4)
                        For y As Integer = 0 To argb.Height - 1
                            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), all, 8 + y * rowBytes, rowBytes)
                        Next
                        Return DualModeFixtures.Sha256Hex(all)
                    Finally
                        argb.UnlockBits(data)
                    End Try
                End Using
            End Using
        End Using
    End Function

    Private Shared Function Include_(name As String) As Boolean
        Return String.Equals(name, FullName, StringComparison.Ordinal)
    End Function

    ' --- the assertions ----------------------------------------------------------------------------

    <Fact>
    Public Sub The_committed_provenance_matches_every_fixture()
        DualModeFixtures.VerifyProvenance()
    End Sub

    <Theory>
    <InlineData(FullName)>
    <InlineData(LanName)>
    Public Sub The_export_bytes_equal_the_committed_baseline(name As String)
        DualModeFixtures.VerifyProvenance()
        Dim fresh As ShareConfigResult = BuildReference(Include_(name))
        Dim committed As Byte() = File.ReadAllBytes(DualModeFixtures.FixturePath(name & ".fmscfg"))
        Assert.True(committed.SequenceEqual(Encoding.UTF8.GetBytes(fresh.ConfigJson)),
                    "the .fmscfg of the reference share is no longer byte-identical to the committed baseline (" & name &
                    "): a change of this export is a contract change, regenerated on purpose with DualModeBaselineGenerator.")
        Assert.False(fresh.ConfigJson.IndexOf(ChrW(&HFEFF)) >= 0, "a byte-order mark reached the payload")
    End Sub

    <Theory>
    <InlineData(FullName)>
    <InlineData(LanName)>
    Public Sub The_qr_decodes_to_the_same_pixels_as_the_committed_picture(name As String)
        DualModeFixtures.VerifyProvenance()
        Dim fresh As ShareConfigResult = BuildReference(Include_(name))
        Dim committed As Byte() = File.ReadAllBytes(DualModeFixtures.FixturePath(name & "-qr.png"))
        Assert.True(committed.Length > 8 AndAlso committed(0) = &H89 AndAlso committed(1) = &H50 AndAlso committed(2) = &H4E AndAlso committed(3) = &H47,
                    "the committed QR is not a PNG")
        Assert.Equal(PixelDigest(committed), PixelDigest(fresh.QrPng))
    End Sub

    <Fact>
    Public Sub The_export_is_deterministic_for_fixed_input()
        Dim a As ShareConfigResult = BuildReference(True)
        Dim b As ShareConfigResult = BuildReference(True)
        Assert.Equal(a.ConfigJson, b.ConfigJson)
        Assert.Equal(PixelDigest(a.QrPng), PixelDigest(b.QrPng))
    End Sub

    <Fact>
    Public Sub The_export_carries_the_three_known_paths_and_nothing_of_the_exchange()
        DualModeFixtures.VerifyProvenance()
        Dim full As ShareConfigResult = BuildReference(True)
        Dim lan As ShareConfigResult = BuildReference(False)
        Assert.False(full.HasRelay)
        Assert.False(lan.HasRelay)

        Assert.Equal(New String() {"lan", "ipv6", "portforward"}, PathKinds(full.ConfigJson))
        Assert.Equal(New String() {"lan"}, PathKinds(lan.ConfigJson))

        For Each name As String In New String() {FullName, LanName}
            Dim text As String = File.ReadAllText(DualModeFixtures.FixturePath(name & ".fmscfg"), New UTF8Encoding(False))
            Assert.False(ExchangeWords.IsMatch(text), "an exchange word is in the baseline export " & name & ": " & ExchangeWords.Match(text).Value)
            Using doc As JsonDocument = JsonDocument.Parse(text)
                ' A client that knows only schema 2 and the fixed field set keeps working (ANYWHERE-ACCESS 8 rule 6).
                Assert.Equal(2, doc.RootElement.GetProperty("schemaVersion").GetInt32())
                For Each prop As JsonProperty In doc.RootElement.EnumerateObject()
                    Assert.Contains(prop.Name, TopLevelKeys)
                Next
                For Each p As JsonElement In doc.RootElement.GetProperty("accessPaths").EnumerateArray()
                    Assert.Equal(New String() {"kind", "host", "port"}, p.EnumerateObject().Select(Function(x) x.Name).ToArray())
                Next
            End Using
        Next
    End Sub

    <Fact>
    Public Sub The_fixtures_carry_documentation_addresses_only()
        DualModeFixtures.VerifyProvenance()
        For Each name As String In New String() {FullName, LanName}
            Dim text As String = File.ReadAllText(DualModeFixtures.FixturePath(name & ".fmscfg"), New UTF8Encoding(False))
            For Each m As Match In Regex.Matches(text, "\b\d{1,3}(\.\d{1,3}){3}\b")
                Assert.True(m.Value.StartsWith("192.0.2.", StringComparison.Ordinal) OrElse
                            m.Value.StartsWith("198.51.100.", StringComparison.Ordinal) OrElse
                            m.Value.StartsWith("203.0.113.", StringComparison.Ordinal),
                            "a fixture holds an address outside the documentation ranges: " & m.Value)
            Next
            Using doc As JsonDocument = JsonDocument.Parse(text)
                For Each p As JsonElement In doc.RootElement.GetProperty("accessPaths").EnumerateArray()
                    Dim host As String = p.GetProperty("host").GetString()
                    If host.Contains(":"c) Then Assert.StartsWith("2001:db8:", host, StringComparison.OrdinalIgnoreCase)
                Next
            End Using
        Next
    End Sub

    Private Shared Function PathKinds(json As String) As String()
        Using doc As JsonDocument = JsonDocument.Parse(json)
            Return doc.RootElement.GetProperty("accessPaths").EnumerateArray().
                Select(Function(p) p.GetProperty("kind").GetString()).ToArray()
        End Using
    End Function

    ' --- the generator (hand-run) -------------------------------------------------------------------

    ''' <summary>Writes the four reference-share files into the folder named by FMS_WRITE_DUAL_MODE_BASELINE (the
    ''' TestData\dual-mode folder), then run <c>tests\Integration\ExchangeBaseline.ps1 -Mode Provenance</c>. A
    ''' default run skips it.</summary>
    <GeneratorFact>
    Public Sub DualModeBaselineGenerator()
        Dim dir As String = Environment.GetEnvironmentVariable(DualModeFixtures.GeneratorVariable)
        Assert.False(String.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir)
        For Each name As String In New String() {FullName, LanName}
            Dim res As ShareConfigResult = BuildReference(Include_(name))
            File.WriteAllBytes(Path.Combine(dir, name & ".fmscfg"), Encoding.UTF8.GetBytes(res.ConfigJson))
            File.WriteAllBytes(Path.Combine(dir, name & "-qr.png"), res.QrPng)
        Next
    End Sub

End Class
#End If
