Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports System.Text
Imports System.Text.Encodings.Web
Imports System.Text.Json
Imports System.Text.RegularExpressions

''' <summary>
''' Builds the .fmscfg resource config and its QR code entirely on the viewer side,
''' straight from the worker's live Status. The worker's own ExportConfig omits
''' the internet access path unless it auto-mapped the port via UPnP, so a manual
''' router forward never reaches the phone. Building it here lets the "open
''' internet access" toggle advertise the external path (LAN + port-forward) in a
''' single scannable QR / one .fmscfg, so sharing works by one scan either way.
'''
''' The .fmscfg schema is the FMSCFG contract - CONFIG_FORMAT.md "Schema (version 1)"
''' and "Schema (version 2 - per-root resource params)" - and the QR payload rule
''' (plain JSON when small, else "FMSCFG1:" + base64(gzip)) is FMSCFG rule 4. Both are
''' reproduced faithfully so the shipped Android importer accepts what we write.
''' See docs/contracts/FMSCFG.md.
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
    ''' <summary>The exported config carries the exchange server's relay paths (ticket 148). Always False
    ''' while the export is dormant.</summary>
    Public Property HasRelay As Boolean
    Public Property LanDisplay As String = ""   ' "host:port" for copy/display
    ''' <summary>Localized short reachability line, also embedded in the .fmscfg as
    ''' the optional "accessNote" (Android S1014 shows it on connection failure)
    ''' and surfaced in the Share tab hint. "" when there is nothing to say.</summary>
    Public Property AccessNote As String = ""
    ''' <summary>The payload does not fit a QR code (contract §7) - the UI must
    ''' tell the user to share the .fmscfg file instead. Never silently truncate.</summary>
    Public Property QrOverflow As Boolean
End Class

''' <summary>Per-package (per-recipient) overrides the "Поделиться" wizard applies to
''' THIS access code only - PIN, slideshow interval and a soft read-only hint (§4.5.3).
''' Left off = each share's own defaults are used.</summary>
Public NotInheritable Class ShareExportOverrides
    Public Property HasPin As Boolean
    Public Property Pin As String = ""
    Public Property HasSlideshow As Boolean
    Public Property SlideshowInterval As Integer = 10
    Public Property ForceSoftReadOnly As Boolean
End Class

''' <summary>
''' The two values in the payload that are NOT a function of the input: the exporting PC's
''' name and the moment of export. Supplied only by the byte-stable conformance test
''' (FMSCFG rule 5), which cannot assert bytes against a value that changes with the machine
''' and the clock.
'''
''' Nothing on every product path, and the product path is still the one that decides what
''' those two values normally are - so a test cannot quietly pin a different timestamp format
''' or a different resource-name shape than the one that actually ships.
''' </summary>
Public NotInheritable Class ShareConfigStamp
    ''' <summary>Replaces "Fast Media Sorter &amp; Sharing on &lt;machine&gt;" (ProductNames.Compact). Empty = the real one.</summary>
    Public Property ResourceName As String = ""
    ''' <summary>Replaces DateTime.UtcNow. Date.MinValue = the real clock.</summary>
    Public Property CreatedAtUtc As Date = Date.MinValue
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
    ''' <paramref name="includePassword"/> off = the "exclude password" safeguard: the
    ''' password key stays present with an empty value and the sender must pass the real
    ''' password out-of-band. FMSCFG does not describe an empty password, and whether the
    ''' shipped importer accepts one is an open question to the Android side - a dated
    ''' registry exception until it is answered (ticket 034 phase 3).
    ''' </summary>
    ''' <param name="exportOverrides">Shared overrides applied to EVERY root (the legacy
    ''' uniform path - kept for callers/tests that set one PIN/RO/slideshow for the whole code).</param>
    ''' <param name="perRootOverrides">Per-root 3-field overrides keyed by <c>ShareFolder.hostPath</c>.
    ''' When a root has an entry it wins over <paramref name="exportOverrides"/>; no entry falls back to
    ''' the shared object, and with neither it uses the share's own stored defaults.</param>
    ''' <param name="perRootParams">FULL per-root params keyed by <c>ShareFolder.hostPath</c> (the package
    ''' wizard's editable grid - §4.5.3). When a root has an entry, that complete ShareRootParams is used
    ''' AS-IS for this export (highest precedence), so a recipient can get any per-folder configuration.
    ''' The frozen .fmscfg wire shape is unchanged - RootJson emits the same roots[] fields.</param>
    ''' <param name="stamp">Test-only injection of the two values that are not a function of the
    ''' input - see <see cref="ShareConfigStamp"/>. Nothing on every product path.</param>
    Public Function Build(status As WorkerStatus, includeExternal As Boolean,
                          Optional includePassword As Boolean = True,
                          Optional exportOverrides As ShareExportOverrides = Nothing,
                          Optional perRootOverrides As Dictionary(Of String, ShareExportOverrides) = Nothing,
                          Optional perRootParams As Dictionary(Of String, ShareRootParams) = Nothing,
                          Optional stamp As ShareConfigStamp = Nothing,
                          Optional exchange As ExchangeExport = Nothing) As ShareConfigResult
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

        ' Emitted even when the worker's connect-back ran and found the port closed
        ' (ExternalPortChecked and not ExternalPortOpen): the code keeps working once the
        ' user fixes the forward the accessNote points at. FMSCFG says "a dead entry is a
        ' bug" - a dated registry exception until the owner rules (ticket 034 phase 4).
        Dim hasExt As Boolean = False
        If includeExternal AndAlso reach IsNot Nothing AndAlso Not reach.IsCgnat AndAlso Not String.IsNullOrEmpty(reach.ExternalHost) Then
            Dim extPort As Integer = If(reach.ExternalPort > 0, reach.ExternalPort, port)
            AppendAccessPath(paths, "portforward", reach.ExternalHost, extPort)
            hasExt = True
        End If

        ' Ticket 148 T-5, DORMANT: nothing on a product path passes `exchange` before contract rung 2, so
        ' with Nothing this block does not run and the bytes are exactly what they were (ANYWHERE-ACCESS 8.5).
        Dim hasRelay As Boolean = AppendExchangePaths(paths, exchange, includeExternal)
        If paths.Length = 0 Then Return Nothing

        Dim anyV2 As Boolean = False
        Dim roots As New StringBuilder()
        If status.Roots IsNot Nothing Then
            For Each r As ShareFolder In status.Roots
                Dim nm As String = If(r.name, "")
                If nm.Length = 0 Then Continue For
                If roots.Length > 0 Then roots.Append(","c)
                ' Precedence: a FULL per-root params entry (the wizard's editable grid) is used
                ' as-is; else the per-root/shared 3-field overrides applied on the stored defaults.
                Dim p As ShareRootParams = Nothing
                If perRootParams IsNot Nothing Then perRootParams.TryGetValue(If(r.hostPath, ""), p)
                If p Is Nothing Then
                    Dim ov As ShareExportOverrides = exportOverrides
                    If perRootOverrides IsNot Nothing Then
                        Dim hit As ShareExportOverrides = Nothing
                        If perRootOverrides.TryGetValue(If(r.hostPath, ""), hit) Then ov = hit
                    End If
                    p = ApplyOverrides(ShareRootParamsStore.GetFor(r.hostPath), ov)
                End If
                ' The SFTP server's per-root read-only comes from the folder list pushed to
                ' it, never from a code - so what a code advertises may be stricter than the
                ' server, never looser (ticket 054 H-07, the FMSCFG producer rule the stored
                ' params already follow through AdvertisedReadOnly).
                p = ClampToServer(p, r.readOnly)
                roots.Append(RootJson(nm, p, anyV2))
            Next
        End If

        ' The short, localized reachability line the phone shows on connection
        ' failure (Android S1014) and the Share tab surfaces. Built from the same
        ' reach facts; emitted as an optional top-level field (old parsers ignore
        ' it, so it never bumps schemaVersion).
        Dim note As String = ShareText.AccessNote(reach, port, includeExternal)

        ' Field order mirrors CONFIG_FORMAT.md "Canonical test vector" and the Android
        ' fixture built from the same bytes (FMSCFG rule 5); accessNote is the optional
        ' trailing field.
        Dim resourceName As String = ProductNames.Compact & " on " & SafeMachineName()
        Dim createdAt As Date = DateTime.UtcNow
        If stamp IsNot Nothing Then
            If Not String.IsNullOrEmpty(stamp.ResourceName) Then resourceName = stamp.ResourceName
            If stamp.CreatedAtUtc <> Date.MinValue Then createdAt = stamp.CreatedAtUtc
        End If

        Dim sb As New StringBuilder()
        sb.Append("{""schemaVersion"":").Append(If(anyV2, "2", "1"))
        sb.Append(",""resourceName"":").Append(J(resourceName))
        sb.Append(",""protocol"":""sftp""")
        sb.Append(",""accessPaths"":[").Append(paths).Append("]"c)
        sb.Append(",""username"":").Append(J(If(status.Username, "")))
        sb.Append(",""password"":").Append(J(If(includePassword, If(status.Password, ""), "")))
        sb.Append(",""hostKeyFingerprintSha256"":").Append(J(If(status.Fingerprint, "")))
        sb.Append(",""roots"":[").Append(roots).Append("]"c)
        sb.Append(",""createdAt"":").Append(J(createdAt.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)))
        If note.Length > 0 Then sb.Append(",""accessNote"":").Append(J(note))
        sb.Append("}"c)
        Dim json As String = sb.ToString()

        If lan.Length = 0 Then
            ' Same diagnostic as above, one level deeper: the actual accessPaths
            ' shipped when the lan entry is missing. Every secret redacted - this is
            ' a log file, not a secrets store, and "send logs to the author" packs it.
            Try
                AppFileLogger.WriteLine("ShareConfigBuilder.Build: exported JSON (secrets redacted): " & RedactSecrets(json))
            Catch
            End Try
        End If

        Dim result As New ShareConfigResult With {
            .ConfigJson = json,
            .HasExternal = hasExt,
            .HasIpv6 = hasIpv6,
            .HasRelay = hasRelay,
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

    ''' <summary>Applies the per-package (per-recipient) overrides on top of the share's
    ''' own params, on a COPY so the persisted share defaults are never touched. Only PIN,
    ''' slideshow interval and the soft read-only hint are overridable at package level
    ''' (§4.5.3); name/type/hard-RO stay the share's.</summary>
    Private Function ApplyOverrides(p As ShareRootParams, o As ShareExportOverrides) As ShareRootParams
        If o Is Nothing OrElse p Is Nothing Then Return p
        Dim c As ShareRootParams = p.Clone()
        If o.HasPin Then c.AccessPin = If(o.Pin, "")
        If o.HasSlideshow Then c.SlideshowInterval = o.SlideshowInterval
        If o.ForceSoftReadOnly Then c.SoftReadOnly = True
        Return c
    End Function

    ''' <summary>
    ''' The params a code may advertise for a root the server serves read-only when
    ''' <paramref name="serverReadOnly"/> is set: read-only, and never a destination - a
    ''' writable promise there is exactly the "Move deletes the original, then rm is denied"
    ''' bug. On a writable root nothing changes: a stricter code (soft or claimed hard
    ''' read-only) is allowed, and says readOnly:true either way. A copy; the caller's params
    ''' are untouched.
    ''' </summary>
    Friend Function ClampToServer(p As ShareRootParams, serverReadOnly As Boolean) As ShareRootParams
        If p Is Nothing OrElse Not serverReadOnly Then Return p
        If Not p.IsWritable() Then Return p
        Dim c As ShareRootParams = p.Clone()
        c.IsReadOnly = True
        c.IsDestination = False
        Return c
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
        ' destination is inherently writable and always exports readOnly:false - the
        ' soft hint does not apply to it (FMSCFG 2.1 amendment A, ticket 034 phase 2).
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

    ''' <summary>
    ''' Appends the relay's two paths AFTER every direct one - the client races them all and the relay is the
    ''' fallback, never the first path (ANYWHERE-ACCESS section 1): the third-party relay port (one more plain
    ''' TCP SSH endpoint, same fingerprint) and then the tunnel (endpoint + share id). Both are externally
    ''' reachable addresses, so the "LAN only" export never carries them, exactly like the IPv6 and
    ''' port-forward paths above. A loopback or unparsable client host, or a malformed share id, appends
    ''' nothing: a phone cannot use them and a bad id must not be advertised. Returns True when anything was added.
    ''' </summary>
    Private Function AppendExchangePaths(paths As StringBuilder, exchange As ExchangeExport, includeExternal As Boolean) As Boolean
        If exchange Is Nothing OrElse Not includeExternal Then Return False
        If exchange.ClientPort < 1 OrElse exchange.ClientPort > 65535 Then Return False
        Dim ep As ExchangeEndpoint = Nothing
        ' ToString brackets an IPv6 literal, so the round trip through TryParse is the one syntax check.
        If Not ExchangeEndpointRules.TryParse(New ExchangeEndpoint(If(exchange.ClientHost, ""), exchange.ClientPort).ToString(), ep) Then Return False
        If ExchangeEndpointRules.IsLoopbackOrUnspecified(ep.Host) Then Return False

        Dim added As Boolean = False
        If exchange.RelayPort >= 1 AndAlso exchange.RelayPort <= 65535 Then
            AppendAccessPath(paths, ExchangeWireNames.RelayPortKind, ep.Host, exchange.RelayPort)
            added = True
        End If
        If ExchangeShareIds.IsWellFormed(exchange.ShareId) Then
            If paths.Length > 0 Then paths.Append(","c)
            paths.Append("{""kind"":").Append(J(ExchangeWireNames.TunnelKind)).
                  Append(",""host"":").Append(J(ep.Host)).
                  Append(",""port"":").Append(ep.Port.ToString(CultureInfo.InvariantCulture)).
                  Append(",").Append(J(ExchangeWireNames.TunnelShareIdMember)).Append(":").Append(J(exchange.ShareId)).
                  Append("}"c)
            added = True
        End If
        Return added
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

    ''' <summary>The payload fields FMSCFG treats as secrets: the SFTP password and every
    ''' root's accessPin (the v2 table gives the PIN the password's sensitivity), and - since
    ''' ticket 148 - a tunnel path's share id, which is a bearer secret link (133 R-6).</summary>
    Private ReadOnly SecretField As New Regex("(""(?:password|accessPin|sid)"":"")(?:[^""\\]|\\.)*("")", RegexOptions.CultureInvariant)

    ''' <summary>Blanks out the value of every secret field for diagnostic logging - the
    ''' export JSON carries a live SFTP credential and per-root PINs by design (FMSCFG rule 6),
    ''' which must never land in a plaintext log file: that log is what "send logs to the
    ''' author" packs (ticket 034 phase 1). An escaped quote inside a value does not end it.</summary>
    Friend Function RedactSecrets(json As String) As String
        If String.IsNullOrEmpty(json) Then Return If(json, "")
        Return SecretField.Replace(json, "$1***$2")
    End Function

    ''' <summary>
    ''' Writes an exported .fmscfg - a KEY (FMSCFG rule 6) - through ProtectedConfigWriter: the file is
    ''' created owner-only before any secret byte exists, published by rename, and verified. Throws
    ''' <see cref="ConfigWriteRefusedException"/> when it was not written (an unsuitable destination,
    ''' a failed verification): nothing may say "saved" after that. 108 H03.
    ''' </summary>
    Public Sub WriteConfigFile(path As String, json As String)
        Dim outcome As ConfigWriteOutcome = ProtectedConfigWriter.Publish(path, json)
        If outcome <> ConfigWriteOutcome.Saved Then Throw New ConfigWriteRefusedException(outcome)
    End Sub
    ''' <summary>Owner-only ACL on <paramref name="path"/>. False when the file system has no
    ''' ACLs or the change was refused - never an exception.</summary>
    Friend Function RestrictToCurrentUser(path As String) As Boolean
        Try
            Dim me_ As SecurityIdentifier = WindowsIdentity.GetCurrent().User
            If me_ Is Nothing Then Return False
            Dim fi As New FileInfo(path)
            Dim acl As New FileSecurity()
            acl.SetAccessRuleProtection(True, False)   ' protected; inherited rules NOT copied
            acl.AddAccessRule(New FileSystemAccessRule(me_, FileSystemRights.FullControl, AccessControlType.Allow))
            fi.SetAccessControl(acl)
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Deletes plaintext .fmscfg copies directly under <paramref name="dir"/> and in
    ''' its "mail-*" folders - the email hand-off of builds before ticket 034 left one there
    ''' for good. Only .fmscfg files; anything else in the folder is not ours to touch.</summary>
    Public Sub DeleteStaleConfigCopies(dir As String)
        Try
            If Not Directory.Exists(dir) Then Return
            For Each f As String In Directory.GetFiles(dir, "*.fmscfg")
                Try : File.Delete(f) : Catch : End Try
            Next
            For Each d As String In Directory.GetDirectories(dir, "mail-*")
                Try : Directory.Delete(d, recursive:=True) : Catch : End Try
            Next
        Catch
        End Try
    End Sub

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
