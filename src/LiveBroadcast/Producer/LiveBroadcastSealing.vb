#If Not NETFRAMEWORK Then
Option Strict On

' Spec 234 T-3 (DEVICE-EXCHANGE section 27 items DK and DL): the one question the producer asks the exchange agent before it
' lists a RELAY or TUNNEL endpoint. The controller owns no key and no transport; the host wires this to the agent, and a test
' wires a fake. A controller with NO sealing port keeps the descriptor it always built (no `seal` member) and nothing here runs.

''' <summary>What the producer needs from the sealed layer of the exchange agent.</summary>
Friend Interface ILiveBroadcastSealing

    ''' <summary>True when content may go through the exchange server now: connected, the server lists `sealed`, this device has a key.
    ''' False means NO RELAY and NO TUNNEL endpoint (state `server-no-e2e`, or not connected) - there is no clear fallback.</summary>
    ReadOnly Property CanSealThroughServer As Boolean

    ''' <summary>A new group key for one outward link of the broadcast, as the base64url text the outward descriptor carries in
    ''' `seal.key`; "" when no key could be made. The agent keeps the key for the key ring of the upload. A secret: never logged.</summary>
    Function NewLinkKey(broadcastId As String) As String

    ''' <summary>The `SHA256:` pin of the inner TLS leaf of this broadcast's TUNNEL (item DM, ticket 234 E1b), as the outward descriptor
    ''' carries it in `seal.pin`; "" when no tunnel identity could be made (the TUNNEL endpoint is then not listed at all). The agent
    ''' makes ONE identity per broadcast, keeps its private key in memory only, and uses it to answer every tunnel connection; asking
    ''' again for the same broadcast returns the same pin. The pin names no secret, but it is handed only through the outward descriptor
    ''' and the sealed box, never logged.</summary>
    Function NewTunnelPin(broadcastId As String) As String

End Interface
#End If
