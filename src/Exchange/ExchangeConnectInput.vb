#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 187 T-4. What a person gave the "Add from an exchange server.." dialog, classified once, purely: a connection string
' (`FMSEXCH1:..`, pasted or read from a .fmsexch file), or an address typed by hand (`host:port`), or neither. The dialog never decides this
' itself, so a pasted share code is never mistaken for an address and a typed address is never treated as a string.
'
' Rule 13 of the grammar proposal is kept by the CALLER, and the data here makes it possible: the destination (host and port) is always
' available to be shown BEFORE the password field takes focus, and the display name `n` is only a label (never the identity, never the title).

Friend Enum ExchangeConnectInputKind
    ''' <summary>Nothing typed yet.</summary>
    Empty = 0
    ''' <summary>A valid connection string: <see cref="ExchangeConnectInput.Value"/> holds it.</summary>
    ConnectionString = 1
    ''' <summary>A host:port typed by hand: <see cref="ExchangeConnectInput.Value"/> holds the endpoint and no pin.</summary>
    Address = 2
    ''' <summary>Neither: <see cref="ExchangeConnectInput.Reason"/> and <see cref="ExchangeConnectInput.Looks"/> say what it is not.</summary>
    Refused = 3
End Enum

Friend NotInheritable Class ExchangeConnectInput

    Public ReadOnly Kind As ExchangeConnectInputKind
    Public ReadOnly Value As ExchangeConnectionStringValue
    ''' <summary>One of <see cref="ExchangeConnectionStringReason"/> for a refused input.</summary>
    Public ReadOnly Reason As String
    Public ReadOnly Looks As String

    Private Sub New(kind As ExchangeConnectInputKind, value As ExchangeConnectionStringValue, reason As String, looks As String)
        Me.Kind = kind
        Me.Value = value
        Me.Reason = If(reason, "")
        Me.Looks = If(looks, "")
    End Sub

    Public ReadOnly Property IsUsable As Boolean
        Get
            Return Kind = ExchangeConnectInputKind.ConnectionString OrElse Kind = ExchangeConnectInputKind.Address
        End Get
    End Property

    ''' <summary>True when the string carries the pin of the leaf: the trust anchor, no prompt (rule 3).</summary>
    Public ReadOnly Property HasPin As Boolean
        Get
            Return Value IsNot Nothing AndAlso Value.Fingerprint.Length > 0
        End Get
    End Property

    ''' <summary>True when the string is an INVITATION (ticket 224 rule 1): it carries `i`, so the dialog offers
    ''' registration instead of sign-in (226 rule 1). The code itself is a secret holder.</summary>
    Public ReadOnly Property HasInvitation As Boolean
        Get
            Return Kind = ExchangeConnectInputKind.ConnectionString AndAlso Value IsNot Nothing AndAlso Value.Invitation.Length > 0
        End Get
    End Property

    ''' <summary>Classifies text from a clipboard, a field or a file's content.</summary>
    Public Shared Function Classify(text As String) As ExchangeConnectInput
        Dim t As String = If(text, "").Trim()
        If t.Length = 0 Then Return New ExchangeConnectInput(ExchangeConnectInputKind.Empty, Nothing, "", "")
        Dim parsed As ExchangeConnectionStringResult = ExchangeConnectionString.Parse(t)
        If parsed.Succeeded Then Return New ExchangeConnectInput(ExchangeConnectInputKind.ConnectionString, parsed.Value, "", "")
        ' not a string: a host:port typed by hand is fine, unless the text looks like another family's (a share code, a URL)
        If parsed.Reason = ExchangeConnectionStringReason.NotAString AndAlso parsed.Looks.Length = 0 Then
            Dim address As ExchangeConnectionStringResult = ExchangeConnectionString.Build(t, "", "", "")
            If address.Succeeded Then Return New ExchangeConnectInput(ExchangeConnectInputKind.Address, address.Value, "", "")
        End If
        Return New ExchangeConnectInput(ExchangeConnectInputKind.Refused, Nothing, parsed.Reason, parsed.Looks)
    End Function

    ''' <summary>The content of a dropped or opened .fmsexch file.</summary>
    Public Shared Function ClassifyFile(bytes As Byte()) As ExchangeConnectInput
        Dim parsed As ExchangeConnectionStringResult = ExchangeConnectionString.ParseFile(bytes)
        If parsed.Succeeded Then Return New ExchangeConnectInput(ExchangeConnectInputKind.ConnectionString, parsed.Value, "", "")
        Return New ExchangeConnectInput(ExchangeConnectInputKind.Refused, Nothing, parsed.Reason, parsed.Looks)
    End Function

    ''' <summary>True for text a paste or a dropped argument may be: it begins like a connection string (the prefix of any major).
    ''' Used to decide whether an argument or a pasted line opens the dialog at all.</summary>
    Public Shared Function LooksLikeConnectionString(text As String) As Boolean
        Dim t As String = If(text, "").Trim()
        If t.Length > 0 AndAlso t(0) = ChrW(65279) Then t = t.Substring(1).TrimStart()
        Return t.StartsWith("FMSEXCH", StringComparison.Ordinal)
    End Function

End Class
#End If
