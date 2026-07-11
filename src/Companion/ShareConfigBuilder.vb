Option Strict On

Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Web.Script.Serialization

''' <summary>
''' Builds the .fmscfg resource config and its QR code entirely on the LITE side,
''' straight from the worker's live Status. The worker's own ExportConfig omits
''' the internet access path unless it auto-mapped the port via UPnP, so a manual
''' router forward never reaches the phone. Building it here lets the "open
''' internet access" toggle advertise the external path (LAN + port-forward) in a
''' single scannable QR / one .fmscfg, so sharing works by one scan either way.
'''
''' The .fmscfg schema (frozen contract, v1 - see the companion CONFIG_FORMAT.md)
''' and the QR payload rule (plain JSON when small, else "FMSCFG1:" + base64(gzip))
''' are reproduced faithfully so the shipped Android importer accepts it.
''' </summary>
Public NotInheritable Class ShareConfigResult
    Public Property ConfigJson As String = ""
    Public Property QrPng As Byte()
    Public Property HasExternal As Boolean
    Public Property LanDisplay As String = ""   ' "host:port" for copy/display
End Class

Public Module ShareConfigBuilder

    Private Const QrPrefix As String = "FMSCFG1:"
    Private Const QrComfortLimit As Integer = 900   ' bytes; denser QR scans poorly

    ' DTOs - property names ARE the on-the-wire JSON field names (schema v1).
    Private Class CfgRoot
        Public Property virtualPath As String
        Public Property label As String
    End Class
    Private Class CfgPath
        Public Property kind As String
        Public Property host As String
        Public Property port As Integer
    End Class
    Private Class Cfg
        Public Property schemaVersion As Integer = 1
        Public Property resourceName As String
        Public Property protocol As String = "sftp"
        Public Property accessPaths As List(Of CfgPath)
        Public Property username As String
        Public Property password As String
        Public Property hostKeyFingerprintSha256 As String
        Public Property roots As List(Of CfgRoot)
        Public Property createdAt As String
    End Class

    ''' <summary>
    ''' Builds the config + QR from a running worker status. Always advertises the
    ''' LAN path; adds the internet (port-forward) path when <paramref name="includeExternal"/>
    ''' is on and reachability found a usable non-CGNAT external host. Returns
    ''' Nothing when there is nothing to serve (not running / no address / no roots).
    ''' </summary>
    Public Function Build(status As WorkerStatus, includeExternal As Boolean) As ShareConfigResult
        If status Is Nothing OrElse Not status.Running Then Return Nothing
        Dim reach As WorkerReachability = status.Reachability
        Dim port As Integer = status.ListenPort
        If port <= 0 Then Return Nothing

        Dim lan As String = If(reach IsNot Nothing, If(reach.LanAddress, ""), "")
        If lan.Length = 0 Then lan = NetworkInfo.LocalIPv4()

        Dim paths As New List(Of CfgPath)
        If lan.Length > 0 Then paths.Add(New CfgPath With {.kind = "lan", .host = lan, .port = port})

        Dim hasExt As Boolean = False
        If includeExternal AndAlso reach IsNot Nothing AndAlso Not reach.IsCgnat AndAlso Not String.IsNullOrEmpty(reach.ExternalHost) Then
            Dim extPort As Integer = If(reach.ExternalPort > 0, reach.ExternalPort, port)
            paths.Add(New CfgPath With {.kind = "portforward", .host = reach.ExternalHost, .port = extPort})
            hasExt = True
        End If
        If paths.Count = 0 Then Return Nothing

        Dim roots As New List(Of CfgRoot)
        If status.Roots IsNot Nothing Then
            For Each r As ShareFolder In status.Roots
                Dim nm As String = If(r.name, "")
                If nm.Length = 0 Then Continue For
                roots.Add(New CfgRoot With {.virtualPath = "/" & nm, .label = nm})
            Next
        End If

        Dim cfg As New Cfg With {
            .resourceName = "FastMediaSorter Companion on " & SafeMachineName(),
            .accessPaths = paths,
            .username = If(status.Username, ""),
            .password = If(status.Password, ""),
            .hostKeyFingerprintSha256 = If(status.Fingerprint, ""),
            .roots = roots,
            .createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)
        }

        Dim json As String = New JavaScriptSerializer().Serialize(cfg)
        Dim result As New ShareConfigResult With {
            .ConfigJson = json,
            .HasExternal = hasExt,
            .LanDisplay = If(lan.Length > 0, lan & ":" & port.ToString(), "")
        }
        Try
            result.QrPng = RenderQr(QrPayload(json))
        Catch
            result.QrPng = Nothing
        End Try
        Return result
    End Function

    ''' <summary>Plain JSON when small enough, else "FMSCFG1:" + base64(gzip(json))
    ''' - matches the worker's QrPayload rule so the Android importer decodes it.</summary>
    Private Function QrPayload(json As String) As String
        Dim raw As Byte() = Encoding.UTF8.GetBytes(json)
        If raw.Length <= QrComfortLimit Then Return json
        Using ms As New MemoryStream()
            Using gz As New GZipStream(ms, CompressionMode.Compress, leaveOpen:=True)
                gz.Write(raw, 0, raw.Length)
            End Using
            Return QrPrefix & Convert.ToBase64String(ms.ToArray())
        End Using
    End Function

    Private Function RenderQr(payload As String) As Byte()
        Using gen As New QRCoder.QRCodeGenerator()
            Using data As QRCoder.QRCodeData = gen.CreateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.M)
                Dim png As New QRCoder.PngByteQRCode(data)
                Return png.GetGraphic(10)
            End Using
        End Using
    End Function

    Private Function SafeMachineName() As String
        Try
            Dim n As String = Environment.MachineName
            If Not String.IsNullOrEmpty(n) Then Return n
        Catch
        End Try
        Return "PC"
    End Function

End Module
