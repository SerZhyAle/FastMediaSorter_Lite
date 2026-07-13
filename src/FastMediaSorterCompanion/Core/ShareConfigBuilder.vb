Option Strict On

Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Text.Encodings.Web
Imports System.Text.Json

''' <summary>
''' Builds the .fmscfg resource config and its QR code entirely on the LITE side,
''' straight from the worker's live Status. The worker's own ExportConfig omits
''' the internet access path unless it auto-mapped the port via UPnP, so a manual
''' router forward never reaches the phone. Building it here lets the "open
''' internet access" toggle advertise the external path (LAN + port-forward) in a
''' single scannable QR / one .fmscfg, so sharing works by one scan either way.
'''
''' The .fmscfg schema (frozen contract - see the companion CONFIG_FORMAT.md and
''' the Android-side COMPANION_EXPORT_SPEC, S1002) and the QR payload rule (plain
''' JSON when small, else "FMSCFG1:" + base64(gzip)) are reproduced faithfully so
''' the shipped Android importer accepts it.
'''
''' Schema v2 (resource params): each root can carry the target resource's
''' configuration (profile, media types, scan conditions, destination flag,
''' comment, PIN, slideshow interval) from ShareRootParamsStore. Fields are
''' emitted ONLY when they differ from the Android import defaults (keeps the QR
''' payload small), and schemaVersion is adaptive: 2 only when at least one v2
''' field is present, otherwise 1 - so an unconfigured share stays importable by
''' Android apps that predate v2. JSON is assembled by hand (ordered, omitting
''' defaults); JavaScriptSerializer cannot omit properties.
''' </summary>
Public NotInheritable Class ShareConfigResult
    Public Property ConfigJson As String = ""
    Public Property QrPng As Byte()
    Public Property HasExternal As Boolean
    ''' <summary>The exported config carries an IPv6 access path - a direct,
    ''' NAT/CGNAT-bypassing route the phone can dial from other networks.</summary>
    Public Property HasIpv6 As Boolean
    Public Property LanDisplay As String = ""   ' "host:port" for copy/display
    ''' <summary>Localized short reachability line, also embedded in the .fmscfg as
    ''' the optional "accessNote" (Android S1014 shows it on connection failure)
    ''' and surfaced in the Share tab hint. "" when there is nothing to say.</summary>
    Public Property AccessNote As String = ""
    ''' <summary>The payload does not fit a QR code (contract §7) - the UI must
    ''' tell the user to share the .fmscfg file instead. Never silently truncate.</summary>
    Public Property QrOverflow As Boolean
End Class

Public Module ShareConfigBuilder

    Private Const QrPrefix As String = "FMSCFG1:"
    Private Const QrComfortLimit As Integer = 900   ' bytes; denser QR scans poorly
    ''' <summary>Hard QR capacity: version 40, ECC M, byte mode = 2331 bytes. A
    ''' payload above this cannot be encoded at all - fall back to the file.</summary>
    Private Const QrHardLimit As Integer = 2331

    ' String-escaping options for the hand-built JSON. UnsafeRelaxedJsonEscaping
    ' keeps non-ASCII as raw UTF-8 (no \uXXXX bloat) - smaller QR payloads for
    ' Cyrillic names and byte-for-byte closer to the worker's own Go json output,
    ' while staying valid JSON the shipped Android importer already accepts.
    Private ReadOnly JsonStringOpts As New JsonSerializerOptions With {.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping}

    ''' <summary>
    ''' Builds the config + QR from a running worker status. Always advertises the
    ''' LAN path; adds the internet (port-forward) path when <paramref name="includeExternal"/>
    ''' is on and reachability found a usable non-CGNAT external host. Returns
    ''' Nothing when there is nothing to serve (not running / no address / no roots).
    ''' <paramref name="includePassword"/> off = the §6 "exclude password" safeguard:
    ''' the password key stays present with an empty value (spec §2 - "passwordless
    ''' share, recipient types it at import") and the sender must pass the real
    ''' password out-of-band.
    ''' </summary>
    Public Function Build(status As WorkerStatus, includeExternal As Boolean,
                          Optional includePassword As Boolean = True) As ShareConfigResult
        If status Is Nothing OrElse Not status.Running Then Return Nothing
        Dim reach As WorkerReachability = status.Reachability
        Dim port As Integer = status.ListenPort
        If port <= 0 Then Return Nothing

        Dim lanFromReach As String = If(reach IsNot Nothing, If(reach.LanAddress, ""), "")
        Dim lanFallback As String = NetworkInfo.LocalIPv4()
        Dim lan As String = If(lanFromReach.Length > 0, lanFromReach, lanFallback)
        If lan.Length = 0 Then
            ' Diagnostic for the 2026-07 "no lan accessPath in the real export"
            ' field failure - both LAN sources (worker reachability AND this
            ' process's own NetworkInfo.LocalIPv4() scan) came back empty at the
            ' same moment; log so a repeat is diagnosable instead of guessed at.
            Try
                AppFileLogger.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") &
                    " ShareConfigBuilder.Build: no LAN address available (reach Is Nothing=" &
                    (reach Is Nothing).ToString() & "; reach.LanAddress and NetworkInfo.LocalIPv4() both empty)")
            Catch
            End Try
        End If

        Dim paths As New StringBuilder()
        If lan.Length > 0 Then AppendAccessPath(paths, "lan", lan, port)

        ' Order LAN -> IPv6 -> port-forward (S1006). IPv6 is a direct, globally-
        ' routable address on the same listen port that bypasses NAT/CGNAT, so it
        ' is included even behind CGNAT (the escape hatch) - but only in the
        ' internet-enabled export, since it is externally routable (the LAN-only
        ' privacy toggle must not embed any address reachable from outside).
        Dim hasIpv6 As Boolean = False
        If includeExternal AndAlso reach IsNot Nothing AndAlso Not String.IsNullOrEmpty(reach.Ipv6Address) Then
            AppendAccessPath(paths, "ipv6", reach.Ipv6Address, port)
            hasIpv6 = True
        End If

        Dim hasExt As Boolean = False
        If includeExternal AndAlso reach IsNot Nothing AndAlso Not reach.IsCgnat AndAlso Not String.IsNullOrEmpty(reach.ExternalHost) Then
            Dim extPort As Integer = If(reach.ExternalPort > 0, reach.ExternalPort, port)
            AppendAccessPath(paths, "portforward", reach.ExternalHost, extPort)
            hasExt = True
        End If
        If paths.Length = 0 Then Return Nothing

        Dim anyV2 As Boolean = False
        Dim roots As New StringBuilder()
        If status.Roots IsNot Nothing Then
            For Each r As ShareFolder In status.Roots
                Dim nm As String = If(r.name, "")
                If nm.Length = 0 Then Continue For
                If roots.Length > 0 Then roots.Append(","c)
                roots.Append(RootJson(nm, ShareRootParamsStore.GetFor(r.hostPath), anyV2))
            Next
        End If

        ' The short, localized reachability line the phone shows on connection
        ' failure (Android S1014) and the Share tab surfaces. Built from the same
        ' reach facts; emitted as an optional top-level field (old parsers ignore
        ' it, so it never bumps schemaVersion).
        Dim note As String = ShareText.AccessNote(Is_Russian_Language, reach, port, includeExternal)

        ' Field order mirrors the companion CONFIG_FORMAT.md / Android fixture;
        ' accessNote is the optional trailing field.
        Dim sb As New StringBuilder()
        sb.Append("{""schemaVersion"":").Append(If(anyV2, "2", "1"))
        sb.Append(",""resourceName"":").Append(J("FastMediaSorter Companion on " & SafeMachineName()))
        sb.Append(",""protocol"":""sftp""")
        sb.Append(",""accessPaths"":[").Append(paths).Append("]"c)
        sb.Append(",""username"":").Append(J(If(status.Username, "")))
        sb.Append(",""password"":").Append(J(If(includePassword, If(status.Password, ""), "")))
        sb.Append(",""hostKeyFingerprintSha256"":").Append(J(If(status.Fingerprint, "")))
        sb.Append(",""roots"":[").Append(roots).Append("]"c)
        sb.Append(",""createdAt"":").Append(J(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)))
        If note.Length > 0 Then sb.Append(",""accessNote"":").Append(J(note))
        sb.Append("}"c)
        Dim json As String = sb.ToString()

        If lan.Length = 0 Then
            ' Same diagnostic as above, one level deeper: the actual accessPaths
            ' shipped when the lan entry is missing. Password redacted - this is
            ' a log file, not a secrets store.
            Try
                AppFileLogger.WriteLine("ShareConfigBuilder.Build: exported JSON (password redacted): " & RedactPassword(json))
            Catch
            End Try
        End If

        Dim result As New ShareConfigResult With {
            .ConfigJson = json,
            .HasExternal = hasExt,
            .HasIpv6 = hasIpv6,
            .AccessNote = note,
            .LanDisplay = If(lan.Length > 0, lan & ":" & port.ToString(), "")
        }

        Dim payload As String = QrPayload(json)
        If Encoding.UTF8.GetByteCount(payload) > QrHardLimit Then
            result.QrOverflow = True   ' §7: fall back to the file, tell the user
        Else
            Try
                result.QrPng = RenderQr(payload)
            Catch
                result.QrPng = Nothing
                result.QrOverflow = True
            End Try
        End If
        Return result
    End Function

    ''' <summary>One roots[] entry. v1 fields (virtualPath, label) always present;
    ''' every v2 field only when it differs from the Android import default (§3).
    ''' Sets <paramref name="anyV2"/> when at least one v2 field was emitted.</summary>
    Private Function RootJson(name As String, p As ShareRootParams, ByRef anyV2 As Boolean) As String
        Dim label As String = name
        If p IsNot Nothing AndAlso p.Label IsNot Nothing AndAlso p.Label.Trim().Length > 0 Then label = p.Label.Trim()

        Dim sb As New StringBuilder()
        sb.Append("{""virtualPath"":").Append(J("/" & name))
        sb.Append(",""label"":").Append(J(label))

        ' readOnly: what the phone is TOLD. It is the ADVERTISED read-only
        ' (ShareRootParams.AdvertisedReadOnly) = the server-enforced restriction
        ' (Not IsWritable, hard RO) OR the soft client-hint (SoftReadOnly). This is
        ' always >= the SFTP server's real restriction (never advertises writable when
        ' the server would deny it - the "Move deletes original -> rm denied" bug), but
        ' MAY be stricter (soft RO: server writable, phone shown read-only). A
        ' destination is inherently writable and (unless soft RO) exports readOnly:false.
        Dim advertisedRo As Boolean = p IsNot Nothing AndAlso p.AdvertisedReadOnly()
        sb.Append(",""readOnly"":").Append(If(advertisedRo, "true", "false"))

        If p IsNot Nothing Then
            Dim v2 As New StringBuilder()
            If Not String.IsNullOrEmpty(p.Profile) AndAlso p.Profile <> "none" Then
                v2.Append(",""profile"":").Append(J(p.Profile))
            End If
            If p.MediaTypes IsNot Nothing AndAlso p.MediaTypes.Count > 0 Then
                v2.Append(",""mediaTypes"":[")
                For i As Integer = 0 To p.MediaTypes.Count - 1
                    If i > 0 Then v2.Append(","c)
                    v2.Append(J(p.MediaTypes(i)))
                Next
                v2.Append("]"c)
            End If
            If Not p.ScanSubdirectories Then v2.Append(",""scanSubdirectories"":false")
            If p.ShowSubfoldersAsItems Then v2.Append(",""showSubfoldersAsItems"":true")
            If p.ShowHiddenFiles Then v2.Append(",""showHiddenFiles"":true")
            If p.AllFiles Then v2.Append(",""allFiles"":true")
            If p.IsDestination Then
                v2.Append(",""isDestination"":true")
                If p.HasDestinationColor Then
                    v2.Append(",""destinationColor"":").Append(p.DestinationColorArgb.ToString(CultureInfo.InvariantCulture))
                End If
            End If
            If p.Comment IsNot Nothing AndAlso p.Comment.Trim().Length > 0 Then
                v2.Append(",""comment"":").Append(J(p.Comment.Trim()))
            End If
            If Not String.IsNullOrEmpty(p.AccessPin) Then
                v2.Append(",""accessPin"":").Append(J(p.AccessPin))
            End If
            If p.SlideshowInterval > 0 AndAlso p.SlideshowInterval <> 10 Then
                v2.Append(",""slideshowInterval"":").Append(p.SlideshowInterval.ToString(CultureInfo.InvariantCulture))
            End If
            If v2.Length > 0 Then
                anyV2 = True
                sb.Append(v2)
            End If
        End If

        sb.Append("}"c)
        Return sb.ToString()
    End Function

    Private Sub AppendAccessPath(sb As StringBuilder, kind As String, host As String, port As Integer)
        If sb.Length > 0 Then sb.Append(","c)
        sb.Append("{""kind"":").Append(J(kind)).
           Append(",""host"":").Append(J(host)).
           Append(",""port"":").Append(port.ToString(CultureInfo.InvariantCulture)).
           Append("}"c)
    End Sub

    ''' <summary>JSON string literal (quoted + escaped).</summary>
    Private Function J(s As String) As String
        Return JsonSerializer.Serialize(If(s, ""), JsonStringOpts)
    End Function

    ''' <summary>Blanks out the "password" field's value for diagnostic logging -
    ''' the export JSON carries a live SFTP credential by design (contract §6),
    ''' which must never land in a plaintext log file.</summary>
    Private Function RedactPassword(json As String) As String
        Const marker As String = """password"":"""
        Dim idx As Integer = json.IndexOf(marker, StringComparison.Ordinal)
        If idx < 0 Then Return json
        Dim valueStart As Integer = idx + marker.Length
        Dim valueEnd As Integer = json.IndexOf(""""c, valueStart)
        If valueEnd < 0 Then Return json
        Return json.Substring(0, valueStart) & "***" & json.Substring(valueEnd)
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
