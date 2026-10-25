Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Threading.Tasks

''' <summary>
''' The operator's rules of ticket 168, all pure (no window, no process, no pipe), so the tests drive each one:
''' the login an account may have, the public address clients are told, the third-party port range, what the
''' accounts and devices lists show in a state, and the six points of the statement of what the operator can see.
'''
''' The rules here mirror the Go module's (<c>accounts.NormalizeLogin</c>, <c>config.CheckPublicEndpoint</c>,
''' <c>config.ParsePortRange</c>) for IMMEDIATE feedback beside a field only; the binary still has the last word and
''' a value it refuses is reported, never written.
''' </summary>
Friend NotInheritable Class ExchangeLoginRules
    Private Sub New()
    End Sub

    Friend Const MinLength As Integer = 3
    Friend Const MaxLength As Integer = 64

    Friend Enum Verdict
        Accepted
        TooShort
        TooLong
        BadCharacter
    End Enum

    ''' <summary>A login is 3-64 characters of a-z, 0-9, dot, underscore and hyphen, folded to lower case on every
    ''' entry (162 rule 1).</summary>
    Friend Shared Function Normalize(text As String) As String
        Return If(text, "").Trim().ToLowerInvariant()
    End Function

    Friend Shared Function Check(text As String) As Verdict
        Dim s As String = Normalize(text)
        If s.Length < MinLength Then Return Verdict.TooShort
        If s.Length > MaxLength Then Return Verdict.TooLong
        For Each c As Char In s
            Dim ok As Boolean = (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "0"c AndAlso c <= "9"c) OrElse c = "."c OrElse c = "_"c OrElse c = "-"c
            If Not ok Then Return Verdict.BadCharacter
        Next
        Return Verdict.Accepted
    End Function

    ''' <summary>A first guess for the wizard's login: the Windows user name, made to fit the rule; "" when nothing
    ''' of it survives.</summary>
    Friend Shared Function SuggestFrom(userName As String) As String
        Dim sb As New Text.StringBuilder()
        For Each c As Char In Normalize(userName)
            If (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "0"c AndAlso c <= "9"c) OrElse c = "."c OrElse c = "_"c OrElse c = "-"c Then sb.Append(c)
        Next
        Dim s As String = sb.ToString()
        If s.Length > MaxLength Then s = s.Substring(0, MaxLength)
        Return If(s.Length >= MinLength, s, "")
    End Function
End Class

Friend NotInheritable Class ExchangeOperatorEndpoint
    Private Sub New()
    End Sub

    Friend Enum Verdict
        ''' <summary>A usable host:port.</summary>
        Accepted
        ''' <summary>Empty: allowed - the server then hides the relay and tunnel endpoints.</summary>
        Empty
        NotHostPort
        BadPort
        NoHost
        ''' <summary>Loopback, localhost or a wildcard: no other device could reach it (168 section 3.3).</summary>
        Loopback
    End Enum

    Friend Shared Function Check(text As String) As Verdict
        Dim s As String = If(text, "").Trim()
        If s.Length = 0 Then Return Verdict.Empty
        Dim colon As Integer = s.LastIndexOf(":"c)
        If colon <= 0 Then Return Verdict.NotHostPort
        Dim host As String = s.Substring(0, colon)
        Dim portText As String = s.Substring(colon + 1)
        ' a bare IPv6 address has colons of its own and must be bracketed (net.SplitHostPort does the same)
        If host.Contains(":") AndAlso Not (host.StartsWith("[", StringComparison.Ordinal) AndAlso host.EndsWith("]", StringComparison.Ordinal)) Then Return Verdict.NotHostPort
        Dim port As Integer
        If Not Integer.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, port) OrElse port < 1 OrElse port > 65535 Then Return Verdict.BadPort
        Dim bare As String = host.Trim("["c, "]"c)
        If bare.Length = 0 OrElse bare.IndexOfAny({" "c, ControlChars.Tab, "/"c, "\"c, "@"c}) >= 0 Then Return Verdict.NoHost
        If String.Equals(bare, "localhost", StringComparison.OrdinalIgnoreCase) OrElse bare.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) Then Return Verdict.Loopback
        Dim ip As IPAddress = Nothing
        If IPAddress.TryParse(bare, ip) AndAlso (IPAddress.IsLoopback(ip) OrElse ip.Equals(IPAddress.Any) OrElse ip.Equals(IPAddress.IPv6Any)) Then Return Verdict.Loopback
        Return Verdict.Accepted
    End Function

    ''' <summary>The suggestion a link offers beside an empty field, from facts the Share Manager already has and NO
    ''' external lookup (142 Q142-3): the worker's known public host, else this PC's LAN address; "" when it has
    ''' neither. A suggestion is only ever offered, never written.</summary>
    Friend Shared Function Suggest(knownPublicHost As String, lanAddress As String, port As Integer) As String
        Dim p As Integer = If(port >= 1 AndAlso port <= 65535, port, ExchangeSettings.DefaultPort)
        For Each candidate As String In {knownPublicHost, lanAddress}
            Dim host As String = If(candidate, "").Trim()
            If host.Length = 0 Then Continue For
            Dim shown As String = If(host.Contains(":") AndAlso Not host.StartsWith("[", StringComparison.Ordinal), "[" & host & "]", host)
            Dim text As String = shown & ":" & p.ToString(CultureInfo.InvariantCulture)
            If Check(text) = Verdict.Accepted Then Return text
        Next
        Return ""
    End Function
End Class

Friend NotInheritable Class ExchangePortRangeRules
    Private Sub New()
    End Sub

    Friend Enum Verdict
        Accepted
        ''' <summary>Low is above high.</summary>
        Order
        ''' <summary>Outside the allowed ports.</summary>
        Bounds
        ''' <summary>The range contains the rendezvous listen port: a third-party tunnel could never be granted it.</summary>
        ContainsListenPort
    End Enum

    Friend Shared Function Check(low As Integer, high As Integer, listenPort As Integer) As Verdict
        If low > high Then Return Verdict.Order
        If low < ExchangeSettings.MinPort OrElse high > ExchangeSettings.MaxPort Then Return Verdict.Bounds
        If listenPort >= low AndAlso listenPort <= high Then Return Verdict.ContainsListenPort
        Return Verdict.Accepted
    End Function

    ''' <summary>"lo-hi", the form the binary takes.</summary>
    Friend Shared Function Format(low As Integer, high As Integer) As String
        Return low.ToString(CultureInfo.InvariantCulture) & "-" & high.ToString(CultureInfo.InvariantCulture)
    End Function

    ''' <summary>Reads "lo-hi" back; False for anything else.</summary>
    Friend Shared Function TryParse(text As String, ByRef low As Integer, ByRef high As Integer) As Boolean
        low = 0
        high = 0
        Dim parts As String() = If(text, "").Split("-"c)
        If parts.Length <> 2 Then Return False
        Return Integer.TryParse(parts(0).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, low) AndAlso
               Integer.TryParse(parts(1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, high)
    End Function

    ''' <summary>How many ports the range holds (the numbers a person can forward on a router).</summary>
    Friend Shared Function Count(low As Integer, high As Integer) As Integer
        Return If(high >= low, high - low + 1, 0)
    End Function
End Class

''' <summary>What one refresh of the accounts and devices read from the server (or its stores).</summary>
Friend NotInheritable Class ExchangeAccountsView
    ''' <summary>The admin channel or the offline path answered.</summary>
    Public Property Reached As Boolean
    Public Property Route As ExchangeAdminRoute = ExchangeAdminRoute.NotReached
    Public Property Accounts As IReadOnlyList(Of ExchangeAdminAccount) = Array.Empty(Of ExchangeAdminAccount)()
    ''' <summary>The devices of the account the list was read for ("" when none was asked).</summary>
    Public Property DevicesOf As String = ""
    Public Property KeepAwakeActive As Boolean
    Public Property KeepAwakeKnown As Boolean
    Public Property Devices As IReadOnlyList(Of ExchangeAdminDevice) = Array.Empty(Of ExchangeAdminDevice)()
    Public Property Invitations As IReadOnlyList(Of ExchangeAdminInvitation) = Array.Empty(Of ExchangeAdminInvitation)()
    Public Property SharedInvitation As ExchangeAdminSharedInvitation
    Public Property Broadcasts As IReadOnlyList(Of ExchangeAdminBroadcast) = Array.Empty(Of ExchangeAdminBroadcast)()
    Public Property Resources As IReadOnlyList(Of ExchangeAdminResource) = Array.Empty(Of ExchangeAdminResource)()
    Public Property ClipboardSlot As ExchangeAdminClipboardInfo
    Public Property Audit As ExchangeAdminAuditView

    ''' <summary>Reads the accounts and, for <paramref name="devicesOf"/>, its devices, broadcasts, resources, and clipboard. Blocks nothing: awaited.</summary>
    Friend Shared Async Function ReadAsync(devicesOf As String, ct As Threading.CancellationToken) As Task(Of ExchangeAccountsView)
        Dim view As New ExchangeAccountsView()
        Dim accounts As ExchangeAdminReply = Await ExchangeAdmin.ListAccountsAsync(ct).ConfigureAwait(False)
        view.Route = accounts.Route
        If Not accounts.Ok Then Return view
        view.Reached = True
        view.Accounts = ExchangeAdminParser.Accounts(accounts.DataJson)
        Dim status As ExchangeAdminReply = Await ExchangeAdmin.SendAsync(New ExchangeAdminRequest(ExchangeAdminRequest.OpStatus), ct).ConfigureAwait(False)
        Dim host As ExchangeAdminStatus = ExchangeAdminParser.Status(status.DataJson)
        view.KeepAwakeKnown = status.Ok AndAlso status.Route = ExchangeAdminRoute.Pipe AndAlso host.KeepAwakeReported
        view.KeepAwakeActive = status.Ok AndAlso host.KeepAwakeActive
        Dim wanted As String = ExchangeLoginRules.Normalize(devicesOf)
        If wanted.Length > 0 AndAlso view.Accounts.Any(Function(a) String.Equals(a.Login, wanted, StringComparison.Ordinal)) Then
            Dim devices As ExchangeAdminReply = Await ExchangeAdmin.ListDevicesAsync(wanted, ct).ConfigureAwait(False)
            If devices.Ok Then
                view.DevicesOf = wanted
                view.Devices = ExchangeAdminParser.Devices(devices.DataJson)
            End If
            Dim broReply As ExchangeAdminReply = Await ExchangeAdmin.ListBroadcastsAsync(wanted, ct).ConfigureAwait(False)
            If broReply.Ok Then
                view.Broadcasts = ExchangeAdminParser.Broadcasts(broReply.DataJson)
            End If
            Dim resReply As ExchangeAdminReply = Await ExchangeAdmin.ListResourcesAsync(wanted, ct).ConfigureAwait(False)
            If resReply.Ok Then
                view.Resources = ExchangeAdminParser.Resources(resReply.DataJson)
            End If
            Dim clipReply As ExchangeAdminReply = Await ExchangeAdmin.GetClipboardInfoAsync(wanted, ct).ConfigureAwait(False)
            If clipReply.Ok Then
                view.ClipboardSlot = ExchangeAdminParser.Clipboard(clipReply.DataJson)
            End If
        End If
        Dim invReply As ExchangeAdminReply = Await ExchangeAdmin.ListInvitationsAsync(ct).ConfigureAwait(False)
        If invReply.Ok Then
            view.Invitations = ExchangeAdminParser.Invitations(invReply.DataJson)
        End If
        Dim sharedReply As ExchangeAdminReply = Await ExchangeAdmin.GetSharedInvitationStatusAsync(ct).ConfigureAwait(False)
        If sharedReply.Ok Then
            view.SharedInvitation = ExchangeAdminParser.SharedInvitation(sharedReply.DataJson)
        End If
        Dim auditReply As ExchangeAdminReply = Await ExchangeAdmin.ListAdminAuditAsync(ct).ConfigureAwait(False)
        If auditReply.Ok Then
            view.Audit = ExchangeAdminParser.Audit(auditReply.DataJson)
        End If
        Return view
    End Function
End Class

''' <summary>
''' What the "Accounts" part of the group draws in a state (ticket 168 section 3.1-3.2, ticket 246): which rows exist. Pure.
''' Buttons are shown or hidden by state, never disabled - the Hosting rule.
''' </summary>
Friend NotInheritable Class ExchangeAccountsModel
    Public Property Reached As Boolean
    Public Property AccountCount As Integer
    Public Property DeviceTotal As Integer
    ''' <summary>The server is not reached at all: nothing below can be listed.</summary>
    Public Property ShowNotReached As Boolean
    ''' <summary>No account exists yet: the hint that the server will not start without one.</summary>
    Public Property ShowFirstAccountHint As Boolean
    ''' <summary>The lists were read from the stores because no server runs: a change applies when it starts.</summary>
    Public Property ShowOfflineNote As Boolean
    Public Property ShowAccountsList As Boolean
    Public Property ShowAdd As Boolean
    Public Property ShowReset As Boolean
    Public Property ShowLimits As Boolean
    Public Property ShowRemove As Boolean
    Public Property ShowDevices As Boolean
    Public Property ShowRevoke As Boolean
    Public Property ShowInvitations As Boolean
    Public Property ShowWithdrawInvitation As Boolean
    Public Property ShowResources As Boolean
    Public Property ShowUnpublishResource As Boolean
    Public Property ShowBroadcasts As Boolean
    Public Property ShowStopBroadcast As Boolean
    Public Property ShowClipboard As Boolean
    Public Property ShowAuditLog As Boolean
    Public Property ExternalAdminActive As Boolean
    Public Property ExternalAdminBadgeText As String = ""

    Friend Shared Function Build(view As ExchangeAccountsView, selectedLogin As String, selectedDeviceId As String, Optional selectedInvHash As String = "", Optional selectedResId As String = "", Optional selectedBroId As String = "") As ExchangeAccountsModel
        Dim m As New ExchangeAccountsModel()
        If view Is Nothing OrElse Not view.Reached Then
            m.ShowNotReached = view IsNot Nothing
            Return m
        End If
        m.Reached = True
        m.AccountCount = view.Accounts.Count
        m.DeviceTotal = view.Accounts.Sum(Function(a) a.Devices)
        m.ShowFirstAccountHint = view.Accounts.Count = 0
        m.ShowOfflineNote = view.Route = ExchangeAdminRoute.Offline
        m.ShowAccountsList = view.Accounts.Count > 0
        m.ShowAdd = True
        Dim login As String = ExchangeLoginRules.Normalize(selectedLogin)
        Dim selected As Boolean = login.Length > 0 AndAlso view.Accounts.Any(Function(a) String.Equals(a.Login, login, StringComparison.Ordinal))
        m.ShowReset = selected
        m.ShowLimits = selected
        m.ShowRemove = selected
        m.ShowDevices = selected AndAlso String.Equals(view.DevicesOf, login, StringComparison.Ordinal) AndAlso view.Devices.Count > 0
        m.ShowRevoke = m.ShowDevices AndAlso Not String.IsNullOrEmpty(selectedDeviceId) AndAlso
                       view.Devices.Any(Function(d) String.Equals(d.DeviceId, selectedDeviceId, StringComparison.Ordinal))
        m.ShowInvitations = True
        Dim invHash As String = If(selectedInvHash, "")
        m.ShowWithdrawInvitation = invHash.Length > 0 AndAlso view.Invitations.Any(Function(i) String.Equals(i.CodeHash, invHash, StringComparison.Ordinal) AndAlso Not i.Used)
        m.ShowResources = selected
        m.ShowUnpublishResource = selected AndAlso Not String.IsNullOrEmpty(selectedResId) AndAlso view.Resources.Any(Function(r) String.Equals(r.ResourceId, selectedResId, StringComparison.Ordinal))
        m.ShowBroadcasts = selected
        m.ShowStopBroadcast = selected AndAlso Not String.IsNullOrEmpty(selectedBroId) AndAlso view.Broadcasts.Any(Function(b) String.Equals(b.BroadcastId, selectedBroId, StringComparison.Ordinal))
        m.ShowClipboard = selected
        m.ShowAuditLog = True
        If view.Audit IsNot Nothing Then
            Dim extSess = view.Audit.ActiveSessions.FirstOrDefault(Function(s) s.ClientProduct <> "local")
            If extSess IsNot Nothing Then
                m.ExternalAdminActive = True
                m.ExternalAdminBadgeText = Localization.TF("Внешнее администрирование: {0} ({1})", If(String.IsNullOrEmpty(extSess.ClientProduct), "remote", extSess.ClientProduct), extSess.RemoteAddr)
            End If
        End If
        Return m
    End Function
End Class

''' <summary>
''' The statement of what the operator can see (DEVICE-EXCHANGE 7.9, ticket 167 rule 8, shown by 168 section 3.6): seven
''' points, each a fact of its own. The ids are the Go module's (<c>internal/statement</c>) and never change meaning;
''' a point is added, never reworded in place. The text is the Russian source of the 13-language layer, one key per
''' point, so a language never shows a different set of limits.
''' </summary>
Friend NotInheritable Class ExchangeStatement
    Private Sub New()
    End Sub

    Friend Shared ReadOnly Ids As String() = {"relay-visible", "tunnel-broadcast-plain", "access-visible", "sftp-opaque", "multi-user-limit", "flat-trust", "clipboard-visible"}

    Friend Shared Function Text(id As String) As String
        Select Case id
            Case "relay-visible"
                Return Localization.T("Трансляция, идущая через сервер обмена, видна оператору сервера: соединение заканчивается на сервере, и он обрабатывает трансляцию в открытом виде. На диск она не пишется и в памяти держится не дольше пары секунд.")
            Case "tunnel-broadcast-plain"
                Return Localization.T("Прямая трансляция через туннель - это обычный RTSP или HTTP внутри соединения с этим сервером, поэтому оператор видит её байты, пока они проходят.")
            Case "access-visible"
                Return Localization.T("Данные доступа к общей папке (адрес, имя пользователя и пароль) хранятся в памяти, пока устройство, которое её раздаёт, в сети, и оператор может их прочитать. На диск они не пишутся и в журнал не попадают.")
            Case "sftp-opaque"
                Return Localization.T("Общая папка, к которой подключаются через туннель, остаётся защищённой собственным SSH-соединением: сервер переносит байты, которые не может прочитать.")
            Case "multi-user-limit"
                Return Localization.T("На сервере, где живут чужие учётные записи, это известное ограничение данной версии: оператор видит то, что описано выше. Сквозное шифрование данных доступа, при котором их не прочитает даже оператор, - работа впереди.")
            Case "flat-trust"
                Return Localization.T("Каждое устройство учётной записи полностью доверяет каждому другому устройству этой записи: любое устройство может удалить другое, запросить коды сопряжения и прочитать данные доступа всех общих папок записи. Удалить себя может любое устройство.")
            Case "clipboard-visible"
                Return Localization.T("Последнее значение, отправленное в буфер этого сервера, - текст, картинка или один файл в пределах размера, установленного для аккаунта, - лежит в памяти этого сервера и нигде больше: на диск не пишется, в журнал не попадает и теряется при перезапуске сервера. Пока оно там, оператор может прочитать его в открытом виде. У аккаунта одно значение - последнее, пришедшее целиком.")
            Case Else
                Return ""
        End Select
    End Function

    ''' <summary>The whole statement as paragraphs, in the order it is shown.</summary>
    Friend Shared Function Paragraphs(Optional retentionDays As Integer = 31, Optional sealed As Boolean = True) As List(Of String)
        Dim points As List(Of String) = If(sealed, New List(Of String) From {Localization.T("Сервер обмена соединяет ваши устройства, поэтому знает имена вашего аккаунта, устройств, папок и трансляций, видит, какие устройства в сети, и сколько и когда передаётся. Открыть то, что идёт через него, он не может: папки и их файлы, трансляции и буфер шифруются на вашем устройстве и открываются только на других ваших устройствах или у тех, кому вы дали доступ. Чтобы заглянуть внутрь, тому, кто управляет сервером, пришлось бы добавить в ваш аккаунт своё устройство, и оно появилось бы в списке ваших устройств.")}, Ids.Select(Function(id) Text(id)).ToList())
        points.Add(Localization.TF("Журналы хранятся {0} дней. Файлы создаются по дням UTC и очищаются ежедневно.", retentionDays))
        Return points
    End Function
End Class
