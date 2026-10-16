#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports Xunit

''' <summary>
''' Tests for Specification 227 (Phone connection code intake and QR decode).
''' Covers QR generation and decoding roundtrips with QRCoder and ZXing.Net,
''' corner QR detection in screenshot-sized images, multiple QRs, no QR,
''' oversized inputs, channel merging (Spec 227 rule 7), and camera presence testing.
''' </summary>
Public Class PhoneConnectionIntakeTests

    Private Shared ReadOnly VectorDirectory As String = Path.Combine(AppContext.BaseDirectory, "TestData", "anywhere-descriptor-vectors")

    Private Shared Function LoadVectors() As JsonElement
        Dim path_str As String = Path.Combine(VectorDirectory, "descriptor-v2.json")
        If Not File.Exists(path_str) Then Return Nothing
        Using document As JsonDocument = JsonDocument.Parse(File.ReadAllBytes(path_str))
            Return document.RootElement.Clone()
        End Using
    End Function

    <Fact>
    Public Sub Vendored_FMSSFTP_vectors_render_to_QR_and_decode_back()
        Dim vectors As JsonElement = LoadVectors()
        ' the vendored file is part of the repository: a missing one is a failure, not a silent pass (spec 239 CR-17)
        Assert.NotEqual(JsonValueKind.Undefined, vectors.ValueKind)

        For Each v As JsonElement In vectors.GetProperty("valid").EnumerateArray()
            Dim name As String = v.GetProperty("name").GetString()
            Dim text As String = v.GetProperty("code").GetString()

            Using bmp As Bitmap = QrCodeRenderer.RenderBitmap(text, 4)
                Assert.NotNull(bmp)
                Dim decoded As QrDecodeResult = QrCodeDecoder.DecodeBitmap(bmp)
                Assert.True(decoded.Succeeded, "QR decode failed for vector " & name & ": " & decoded.FailureMessage)
                Assert.Equal(text, decoded.Text)

                ' Ensure intake parses the decoded text into a plan
                Dim read As FmsConfigReadResult = PhoneConnectionIntake.ReadText(decoded.Text)
                Assert.True(read.Succeeded, "Intake read failed for vector " & name)
                Assert.NotNull(read.Plan)
            End Using
        Next
    End Sub

    <Fact>
    Public Sub Fmscfg_JSON_renders_to_QR_and_decodes_back()
        Dim json As String = "{""schemaVersion"":1,""resourceName"":""TestShare"",""protocol"":""sftp"",""username"":""alice"",""accessPaths"":[{""kind"":""lan"",""host"":""192.168.1.50"",""port"":2022}],""roots"":[{""virtualPath"":""/Photos"",""label"":""Photos"",""readOnly"":false}]}"

        Using bmp As Bitmap = QrCodeRenderer.RenderBitmap(json, 4)
            Assert.NotNull(bmp)
            Dim decoded As QrDecodeResult = QrCodeDecoder.DecodeBitmap(bmp)
            Assert.True(decoded.Succeeded, "QR decode failed for JSON: " & decoded.FailureMessage)
            Assert.Equal(json, decoded.Text)

            Dim read As FmsConfigReadResult = PhoneConnectionIntake.ReadText(decoded.Text)
            Assert.True(read.Succeeded)
            Assert.Equal("TestShare", read.Plan.ResourceName)
            Assert.Equal("alice", read.Plan.UserName)
        End Using
    End Sub

    <Fact>
    Public Sub Screenshot_sized_image_with_QR_in_corner_decodes_successfully()
        Dim code As String = "FMSSFTP2:h=192.168.1.100&p=2022&u=user&pw=secret123&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"

        Using qrBmp As Bitmap = QrCodeRenderer.RenderBitmap(code, 4)
            ' 1920x1080 canvas representing full HD screenshot
            Using canvas As New Bitmap(1920, 1080)
                Using g As Graphics = Graphics.FromImage(canvas)
                    g.Clear(Color.LightGray)
                    ' Draw QR in bottom-right corner with some padding
                    g.DrawImage(qrBmp, canvas.Width - qrBmp.Width - 50, canvas.Height - qrBmp.Height - 50)
                End Using

                Dim decoded As QrDecodeResult = QrCodeDecoder.DecodeBitmap(canvas)
                Assert.True(decoded.Succeeded, "Failed to decode corner QR: " & decoded.FailureMessage)
                Assert.Equal(code, decoded.Text)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Scaled_and_rotated_QR_decodes_successfully()
        Dim code As String = "FMSSFTP2:h=10.0.0.5&p=2222&u=mobile&pw=pass&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"

        Using original As Bitmap = QrCodeRenderer.RenderBitmap(code, 3)
            ' Test 90 degree rotation (blitted onto 32bpp to avoid GDI+ indexed bitmap rotation failure)
            Using rotated As New Bitmap(original.Width, original.Height, Imaging.PixelFormat.Format32bppRgb)
                Using g As Graphics = Graphics.FromImage(rotated)
                    g.DrawImage(original, 0, 0)
                End Using
                rotated.RotateFlip(RotateFlipType.Rotate90FlipNone)
                Dim decodedRotated As QrDecodeResult = QrCodeDecoder.DecodeBitmap(rotated)
                Assert.True(decodedRotated.Succeeded, "Failed to decode 90-degree rotated QR")
                Assert.Equal(code, decodedRotated.Text)
            End Using

            ' Test scaling up
            Using scaled As New Bitmap(original.Width * 2, original.Height * 2)
                Using g As Graphics = Graphics.FromImage(scaled)
                    g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.NearestNeighbor
                    g.DrawImage(original, 0, 0, scaled.Width, scaled.Height)
                End Using
                Dim decodedScaled As QrDecodeResult = QrCodeDecoder.DecodeBitmap(scaled)
                Assert.True(decodedScaled.Succeeded, "Failed to decode scaled QR")
                Assert.Equal(code, decodedScaled.Text)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Image_with_no_QR_returns_NoQr_outcome_and_correct_message()
        Using blank As New Bitmap(300, 300)
            Using g As Graphics = Graphics.FromImage(blank)
                g.Clear(Color.White)
            End Using

            Dim res As QrDecodeResult = QrCodeDecoder.DecodeBitmap(blank)
            Assert.False(res.Succeeded)
            Assert.Equal(QrDecodeOutcome.NoQr, res.Outcome)
            Assert.Equal(Localization.T("В изображении не найден QR-код"), res.FailureMessage)
        End Using
    End Sub

    <Fact>
    Public Sub Image_with_multiple_QRs_returns_MultipleQrs_outcome_and_correct_message()
        Dim code1 As String = "FMSSFTP2:h=192.168.1.1&p=2022&u=u1&pw=p1&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
        Dim code2 As String = "FMSSFTP2:h=192.168.1.2&p=2022&u=u2&pw=p2&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"

        Using qr1 As Bitmap = QrCodeRenderer.RenderBitmap(code1, 3)
            Using qr2 As Bitmap = QrCodeRenderer.RenderBitmap(code2, 3)
                Using combined As New Bitmap(qr1.Width + qr2.Width + 60, Math.Max(qr1.Height, qr2.Height) + 40)
                    Using g As Graphics = Graphics.FromImage(combined)
                        g.Clear(Color.White)
                        g.DrawImage(qr1, 20, 20)
                        g.DrawImage(qr2, qr1.Width + 40, 20)
                    End Using

                    Dim res As QrDecodeResult = QrCodeDecoder.DecodeBitmap(combined)
                    Assert.False(res.Succeeded)
                    Assert.Equal(QrDecodeOutcome.MultipleQrs, res.Outcome)
                    Assert.Equal(Localization.T("В изображении найдено несколько QR-кодов"), res.FailureMessage)
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Oversized_image_returns_TooLarge_outcome()
        ' Dimensions exceeding MaxPixelCount (16,000,000 pixels: 5000 x 4000 = 20 MP)
        Using huge As New Bitmap(5000, 4000)
            Dim res As QrDecodeResult = QrCodeDecoder.DecodeBitmap(huge)
            Assert.False(res.Succeeded)
            Assert.Equal(QrDecodeOutcome.TooLarge, res.Outcome)
            Assert.Equal(Localization.T("Изображение слишком велико для поиска QR-кода"), res.FailureMessage)
        End Using
    End Sub

    <Fact>
    Public Sub Channel_merging_combines_endpoints_for_same_fingerprint_and_root()
        Dim fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
        Dim existing As New List(Of ResourceDescriptor) From {
            New ResourceDescriptor With {
                .Id = "res-existing-1",
                .DisplayName = "Phone Photos",
                .Kind = ResourceKind.Sftp,
                .Host = "192.168.1.50",
                .Port = 2022,
                .UserName = "phone_user",
                .RootPath = "/",
                .PinnedFingerprint = fp,
                .Endpoints = New List(Of ResourceEndpoint) From {
                    New ResourceEndpoint With {.Kind = "lan", .Host = "192.168.1.50", .Port = 2022}
                }
            }
        }

        ' Incoming plan carries a new Wi-Fi address (192.168.1.80) for the same fingerprint and root
        Dim code2 As String = "FMSSFTP2:h=192.168.1.80&p=2022&u=phone_user&pw=secret&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
        Dim read As FmsConfigReadResult = PhoneConnectionIntake.ReadText(code2)
        Assert.True(read.Succeeded)

        Dim preview As ImportPreview = ResourceImport.Preview(read.Plan, existing, AddressOf ResourceImport.NewResourceId)
        Assert.True(preview.CanApply)
        Assert.Single(preview.Items)

        Dim item As ImportItem = preview.Items(0)
        Assert.True(item.IsUpdate, "Must recognize as an update/merge into existing resource")
        Assert.Equal("res-existing-1", item.Descriptor.Id)

        ' Check that endpoints were merged (contains both .50 and .80)
        Assert.Contains(item.Descriptor.Endpoints, Function(ep) ep.Host = "192.168.1.50" AndAlso ep.Port = 2022)
        Assert.Contains(item.Descriptor.Endpoints, Function(ep) ep.Host = "192.168.1.80" AndAlso ep.Port = 2022)
        Assert.Equal(2, item.Descriptor.Endpoints.Count)
    End Sub

    <Fact>
    Public Sub CameraPresence_OverrideForTesting_controls_presence()
        Try
            CameraPresence.OverrideForTesting = True
            Assert.True(CameraPresence.IsCameraPresent())

            CameraPresence.OverrideForTesting = False
            Assert.False(CameraPresence.IsCameraPresent())
        Finally
            CameraPresence.OverrideForTesting = Nothing
        End Try
    End Sub

    <Fact>
    Public Sub Supported_file_extensions_filter_correctly()
        Assert.True(PhoneConnectionIntake.IsSupportedFile("file.png"))
        Assert.True(PhoneConnectionIntake.IsSupportedFile("test.JPG"))
        Assert.True(PhoneConnectionIntake.IsSupportedFile("photo.webp"))
        Assert.True(PhoneConnectionIntake.IsSupportedFile("share.fmscfg"))
        Assert.True(PhoneConnectionIntake.IsSupportedFile("config.json"))
        Assert.True(PhoneConnectionIntake.IsSupportedFile("code.txt"))

        Assert.False(PhoneConnectionIntake.IsSupportedFile("video.mp4"))
        Assert.False(PhoneConnectionIntake.IsSupportedFile("archive.zip"))
        Assert.False(PhoneConnectionIntake.IsSupportedFile("binary.exe"))
        Assert.False(PhoneConnectionIntake.IsSupportedFile(""))
    End Sub

    <Fact>
    Public Sub Low_contrast_QR_still_decodes()
        Dim code As String = "FMSSFTP2:h=192.168.7.40&p=2022&u=lowc&pw=p&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"

        Using original As Bitmap = QrCodeRenderer.RenderBitmap(code, 4)
            ' The dark modules pulled up to a grey that keeps under a 3:1 step against the white
            ' quiet zone - a photo of a screen at a bad angle, not a clean render.
            Using faded As New Bitmap(original.Width, original.Height)
                For y As Integer = 0 To original.Height - 1
                    For x As Integer = 0 To original.Width - 1
                        Dim pixel As Color = original.GetPixel(x, y)
                        Dim dark As Boolean = pixel.R < 128
                        faded.SetPixel(x, y, If(dark, Color.FromArgb(96, 96, 96), Color.White))
                    Next
                Next
                Dim decoded As QrDecodeResult = QrCodeDecoder.DecodeBitmap(faded)
                Assert.True(decoded.Succeeded, "Failed to decode a low-contrast QR: " & decoded.FailureMessage)
                Assert.Equal(code, decoded.Text)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Malformed_code_arriving_through_a_QR_is_refused_whole()
        ' A QR that scans but whose text breaks the reader's rules: the decode succeeds, the reader
        ' refuses, and nothing half-valid survives (spec 227 rule 5).
        Dim malformed As String = "FMSSFTP2:h=&p=notaport&u=&pw=x&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
        Using bmp As Bitmap = QrCodeRenderer.RenderBitmap(malformed, 4)
            Dim decoded As QrDecodeResult = QrCodeDecoder.DecodeBitmap(bmp)
            Assert.True(decoded.Succeeded)
            Assert.Equal(malformed, decoded.Text)

            Dim read As FmsConfigReadResult = PhoneConnectionIntake.ReadText(decoded.Text)
            Assert.False(read.Succeeded)
            Assert.Null(read.Plan)
        End Using
    End Sub

    <Fact>
    Public Sub Intake_file_reads_carry_the_decoders_own_sentences()
        Dim code As String = "FMSSFTP2:h=192.168.7.41&p=2022&u=sent&pw=p&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
        Dim blank As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fms227-blank-" & Guid.NewGuid().ToString("N") & ".png")
        Dim two As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fms227-two-" & Guid.NewGuid().ToString("N") & ".png")
        Try
            Using blankBmp As New Bitmap(300, 300)
                Using g As Graphics = Graphics.FromImage(blankBmp)
                    g.Clear(Color.White)
                End Using
                blankBmp.Save(blank, Imaging.ImageFormat.Png)
            End Using

            Using qr1 As Bitmap = QrCodeRenderer.RenderBitmap(code, 3)
                Using qr2 As Bitmap = QrCodeRenderer.RenderBitmap("FMSSFTP2:h=192.168.7.42&p=2022&u=sent2&pw=p&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk", 3)
                    Using combined As New Bitmap(qr1.Width + qr2.Width + 60, Math.Max(qr1.Height, qr2.Height) + 40)
                        Using g As Graphics = Graphics.FromImage(combined)
                            g.Clear(Color.White)
                            g.DrawImage(qr1, 20, 20)
                            g.DrawImage(qr2, qr1.Width + 40, 20)
                        End Using
                        combined.Save(two, Imaging.ImageFormat.Png)
                    End Using
                End Using
            End Using

            Dim noQr As FmsConfigReadResult = PhoneConnectionIntake.ReadFile(blank)
            Assert.False(noQr.Succeeded)
            Assert.Equal(Localization.T("В изображении не найден QR-код"), noQr.Detail)

            Dim multi As FmsConfigReadResult = PhoneConnectionIntake.ReadFile(two)
            Assert.False(multi.Succeeded)
            Assert.Equal(Localization.T("В изображении найдено несколько QR-кодов"), multi.Detail)
            Assert.NotEqual(noQr.Detail, multi.Detail)
        Finally
            For Each path As String In {blank, two}
                DeleteQuietly(path)
            Next
        End Try
    End Sub

    <Fact>
    Public Sub Webp_QR_image_decodes_through_the_intake()
        ' GDI+ has no WEBP codec: the phone's screenshot arrives as one, and the intake reads it
        ' through the managed fallback (the viewer's ImageSharp).
        Dim code As String = "FMSSFTP2:h=192.168.7.43&p=2022&u=webp&pw=p&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
        Dim path As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fms227-webp-" & Guid.NewGuid().ToString("N") & ".webp")
        Try
            Using bmp As Bitmap = QrCodeRenderer.RenderBitmap(code, 4)
                Using sharp As SixLabors.ImageSharp.Image = SixLabors.ImageSharp.Image.Load(BitmapToPngBytes(bmp))
                    SixLabors.ImageSharp.ImageExtensions.SaveAsWebP(sharp, path)
                End Using
            End Using

            Assert.True(PhoneConnectionIntake.IsSupportedFile(path))
            Dim read As FmsConfigReadResult = PhoneConnectionIntake.ReadFile(path)
            Assert.True(read.Succeeded, "A WEBP QR image did not decode through the intake")
        Finally
            DeleteQuietly(path)
        End Try
    End Sub

    Private Shared Sub DeleteQuietly(path As String)
        Try
            If File.Exists(path) Then File.Delete(path)
        Catch
        End Try
    End Sub

    Private Shared Function BitmapToPngBytes(source As Bitmap) As Byte()
        Using ms As New MemoryStream()
            source.Save(ms, Imaging.ImageFormat.Png)
            Return ms.ToArray()
        End Using
    End Function

    ''' <summary>The QR side of spec 227 rule 6: nothing in the decode/intake/relay path composes a
    ''' log line out of the code. The relay does log - outcomes only - so its lines are checked too.</summary>
    <Fact>
    Public Sub The_decode_and_relay_path_never_logs_the_code()
        Assert.DoesNotContain("AppFileLogger", File.ReadAllText(LocateSource("QrCodeDecoder.vb")))
        Assert.DoesNotContain("AppFileLogger", File.ReadAllText(LocateSource("PhoneConnectionIntake.vb")))

        Dim relay As String = LocateCompanionSource("PhoneCodeRelay.vb")
        For Each line As String In File.ReadAllLines(relay)
            If Not line.Contains("AppFileLogger") Then Continue For
            For Each carrier As String In {"payload", "bytes", "RelayCode", "RelayDocumentFile"}
                Assert.DoesNotContain(carrier, line)
            Next
        Next
    End Sub

    ''' <summary>Spec 227 rule 2 in the panel's own source: the camera item exists only behind the
    ''' three-part gate, and the reflection crutch the first draft used is gone.</summary>
    <Fact>
    Public Sub The_panels_camera_item_is_gated_and_not_reflective()
        Dim source As String = File.ReadAllText(LocatePanelSource())
        Assert.Contains("If scanCameraAction IsNot Nothing AndAlso ShareComponentInstalled() AndAlso CameraPresence.IsCameraPresent() Then", source)
        Assert.DoesNotContain("GetType().GetMethod(", source)
    End Sub

    Private Shared Function LocateSource(fileName As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Resources", fileName)
            If File.Exists(candidate) Then Return candidate
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("Could not locate src\Resources\" & fileName & " from the test binary.")
    End Function

    Private Shared Function LocateCompanionSource(fileName As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "FastMediaSorterCompanion", "Core", fileName)
            If File.Exists(candidate) Then Return candidate
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("Could not locate src\FastMediaSorterCompanion\Core\" & fileName & " from the test binary.")
    End Function

    Private Shared Function LocatePanelSource() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Resource_List_Panel.vb")
            If File.Exists(candidate) Then Return candidate
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("Could not locate src\Resource_List_Panel.vb from the test binary.")
    End Function

End Class
#End If
