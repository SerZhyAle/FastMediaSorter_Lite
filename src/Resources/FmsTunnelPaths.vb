#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text.Json

' Ticket 171 rule 7 (phase C4): the outsider path. A person who gave a link gives it as an `FMSSFTP2` text (its `x` and `sid`) or as a `.fmscfg`
' whose access paths end with a `rendezvousTunnel` path (FMSCFG 2.4): the same resource, NO ACCOUNT, the tunnel being `connect {shareId}` on a
' TLS stream to the exchange server, without `hello`.
'
' The one reader of FMSCFG documents (FmsConfigReader) keeps its rule for every kind it does not know - skip it, never dial it, never refuse the
' file for it - and hands THIS kind to this module. Here it is read only where the exchange client features are offered
' (<see cref="ExchangeFeatures"/>); everywhere else it stays an unknown kind and the document reads exactly as it did before. A tunnel path is
' kept in the plan beside the endpoints and never among them: the exchange server is not an SSH address of the share, and a reader that dialled
' it as one would hand the share's password to the exchange.
'
' A tunnel path that cannot be used (no valid host and port, a share id that is not the id form) is skipped, not refused: the direct paths of
' the same file stay importable (the duty of FMSCFG rule 7 is the producer's).

''' <summary>The exchange server an outsider link tunnels through, and the capability that opens the share. The share id is a secret.</summary>
Friend NotInheritable Class FmsTunnelPath

    Friend ReadOnly Property Host As String
    Friend ReadOnly Property Port As Integer
    ''' <summary>22 base64url characters, the id form. A bearer capability: kept only as a DPAPI blob, never logged.</summary>
    Friend ReadOnly Property ShareId As String

    Friend Sub New(host As String, port As Integer, shareId As String)
        Me.Host = host
        Me.Port = port
        Me.ShareId = shareId
    End Sub

    Public Overrides Function ToString() As String
        Return "FmsTunnelPath"
    End Function

End Class

Friend NotInheritable Class FmsTunnelPaths
    Private Sub New()
    End Sub

    Friend Const TunnelKind As String = "rendezvousTunnel"

    ''' <summary>Whether the reader takes the kind. The real gate; a test replaces it to hold the reader both ways without an environment.</summary>
    Friend Shared ReadsTunnelKind As Func(Of Boolean) = Function() ExchangeFeatures.IsEnabled()

    ''' <summary>True when <paramref name="kind"/> is the tunnel kind (the entry is consumed: taken into the plan, or skipped - either way the
    ''' caller must not treat it as an address). False for every other kind, which the reader handles as before.</summary>
    Friend Shared Function TryTake(entry As JsonElement, kind As String, plan As FmsConfigPlan) As Boolean
        If Not String.Equals(kind, TunnelKind, StringComparison.Ordinal) Then Return False
        If plan.Tunnel IsNot Nothing OrElse Not ReadsTunnelKind.Invoke() Then Return True
        Dim host As String = Nothing
        Dim port As Integer
        Dim sid As String = Nothing
        If Not TryReadMembers(entry, host, port, sid) Then Return True
        plan.Tunnel = New FmsTunnelPath(host, port, sid)
        Return True
    End Function

    Private Shared Function TryReadMembers(entry As JsonElement, ByRef host As String, ByRef port As Integer, ByRef sid As String) As Boolean
        host = ""
        sid = ""
        Dim el As JsonElement = Nothing
        If Not entry.TryGetProperty("host", el) OrElse el.ValueKind <> JsonValueKind.String Then Return False
        host = el.GetString()
        If String.IsNullOrWhiteSpace(host) OrElse host.Trim().Length <> host.Length OrElse host.Length > 253 Then Return False
        For Each ch As Char In host
            If AscW(ch) < &H21 OrElse AscW(ch) = &H7F OrElse ch = "/"c OrElse ch = "\"c OrElse ch = "@"c Then Return False
        Next
        If Not entry.TryGetProperty("port", el) OrElse el.ValueKind <> JsonValueKind.Number OrElse Not el.TryGetInt32(port) OrElse port < 1 OrElse port > 65535 Then Return False
        If Not entry.TryGetProperty("shareId", el) OrElse el.ValueKind <> JsonValueKind.String Then Return False
        sid = el.GetString()
        Return ExchangeIds.IsValidId(sid)
    End Function

End Class

''' <summary>An `FMSSFTP1` / `FMSSFTP2` text as the plan of an import (ticket 171 C1: "attach by hand from a pasted code"), so the code goes through the
''' same preview, confirmation and commit as a file does. A code carries a host list, a port, a user, a password and a fingerprint - and no root:
''' the resource is the server's root, which lists the shares by name.</summary>
Friend NotInheritable Class FmsSftpPlans
    Private Sub New()
    End Sub

    ''' <summary>True when <paramref name="text"/> is a pairing code (whatever it holds): the reader of FMSCFG is then not asked.</summary>
    Friend Shared Function LooksLikeCode(text As String) As Boolean
        Dim t As String = If(text, "").TrimStart()
        Return t.StartsWith(FmsSftpCode.PrefixV1, StringComparison.Ordinal) OrElse t.StartsWith(FmsSftpCode.PrefixV2, StringComparison.Ordinal) OrElse
               FmsSftpCode.IsNewerPrefix(t)
    End Function

    ''' <summary>The code as an import. Never throws; a refused code is a result with a failure, never a half-read plan.</summary>
    Friend Shared Function Read(text As String) As FmsConfigReadResult
        Dim code As FmsSftpCode = Nothing
        ' ANYWHERE-ACCESS 4.3: a prefix above FMSSFTP2 gets the one message that asks the person to update
        If FmsSftpCode.IsNewerPrefix(text) Then
            Return New FmsConfigReadResult With {.Failure = FmsConfigFailure.UpdateRequired,
                                                 .Detail = Localization.T("Код подключения создан более новой версией программы. Обновите Fast Media Sorter")}
        End If
        If Not FmsSftpCode.TryParse(text, code) Then Return New FmsConfigReadResult With {.Failure = FmsConfigFailure.InvalidJson}

        Dim plan As New FmsConfigPlan With {.SchemaVersion = 2, .UserName = code.Username, .Password = If(code.Password, ""), .PinnedFingerprint = code.Fingerprint}
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        For Each host As String In code.Hosts
            Dim endpoint As New FmsEndpoint With {.Kind = ExchangeAttach.KindOfHost(host), .Host = host, .Port = code.Port}
            If seen.Add(endpoint.Key()) Then plan.Endpoints.Add(endpoint)
        Next
        If code.Exchange.Length > 0 AndAlso ExchangeFeatures.IsEnabled() Then
            Dim address As ExchangeServerAddress = Nothing
            If ExchangeServerAddress.TryParse(code.Exchange, address) Then plan.Tunnel = New FmsTunnelPath(address.Host, address.Port, code.ShareId)
        End If
        plan.Roots.Add(New FmsRootPlan With {.VirtualPath = "/", .Label = "", .ReadOnlyRaw = False})
        Return New FmsConfigReadResult With {.Plan = plan}
    End Function

End Class
#End If
