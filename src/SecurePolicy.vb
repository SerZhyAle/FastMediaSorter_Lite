#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Net

''' <summary>
''' How one attempt to show a secure container ended, as the viewer needs to know it
''' (audit 052 G-07, G-09). Finer than <see cref="FdSecOutcome"/> on purpose: the outcome
''' class is what the user is TOLD, and it never changes here; this is what the viewer
''' DOES next - and "a chunk failed after the credential proved right" is told as the
''' CredentialOrTamper class while asking for another password would be pointless.
''' </summary>
Public Enum SecureAttempt
    ''' <summary>Nothing was tried: no credential was held.</summary>
    NotTried
    ''' <summary>The content is on screen.</summary>
    Shown
    ''' <summary>Key slot 0 or the metadata did not authenticate - the credential may be
    ''' the reason, so asking for another one is worth it.</summary>
    CredentialOrTamper
    ''' <summary>A payload chunk did not authenticate. Said as the CredentialOrTamper
    ''' class, but the credential already proved itself on the head.</summary>
    ContentAltered
    Damaged
    Unsupported
    ''' <summary>Opened, and holds something the viewer does not show (a sealed PDF).</summary>
    NotMedia
    ''' <summary>An I/O failure - not one of the three classes.</summary>
    ReadFailure
    ''' <summary>Opened, but the decoder or the player refused the content.</summary>
    DisplayFailure
End Enum

''' <summary>
''' The pure decisions of the viewer's secure-container side (026, audit 052), kept out of
''' Main_Form so a test can hold them. Nothing here touches the disk, a setting or a form.
''' </summary>
Friend Module SecurePolicy

    ''' <summary>The attempt an exception of the format module stands for, when it was
    ''' raised by the OPEN (head and metadata).</summary>
    Friend Function AttemptFromOpen(outcome As FdSecOutcome) As SecureAttempt
        Select Case outcome
            Case FdSecOutcome.CredentialOrTamper : Return SecureAttempt.CredentialOrTamper
            Case FdSecOutcome.Damaged : Return SecureAttempt.Damaged
            Case FdSecOutcome.Unsupported : Return SecureAttempt.Unsupported
            Case Else : Return SecureAttempt.Shown
        End Select
    End Function

    ''' <summary>The attempt an exception stands for when it was raised while READING THE
    ''' CONTENT - after the head authenticated, so a tag failure there cannot be the
    ''' credential (FDSEC-FORMAT §12: "chunk tag failure on a structurally intact file").</summary>
    Friend Function AttemptFromContent(outcome As FdSecOutcome) As SecureAttempt
        If outcome = FdSecOutcome.CredentialOrTamper Then Return SecureAttempt.ContentAltered
        Return AttemptFromOpen(outcome)
    End Function

    ''' <summary>
    ''' G-07: may the viewer ask for a password for this container? Only when the file
    ''' passed the length screen - a file that could not have used a password is never
    ''' asked for one (FDSEC-BEHAVIOUR §8.1) - and only when nothing was tried yet or the
    ''' last try ended in the one class a different credential can change. A damaged,
    ''' unsupported, unreadable or non-media container, or one whose credential already
    ''' worked, is never answered with a password dialog.
    ''' </summary>
    Friend Function ShouldAskForPassword(screenPassed As Boolean, attempt As SecureAttempt) As Boolean
        If Not screenPassed Then Return False
        Return attempt = SecureAttempt.NotTried OrElse attempt = SecureAttempt.CredentialOrTamper
    End Function

    ''' <summary>
    ''' G-10: the name a FAILURE surface may show for a container. The sealed true name only
    ''' while the user has <c>SecureShowTrueName</c> on, otherwise the container's own name,
    ''' which is on disk for anyone to read anyway.
    ''' </summary>
    Friend Function ShownName(trueName As String, containerPath As String, showTrueName As Boolean) As String
        If showTrueName AndAlso Not String.IsNullOrEmpty(trueName) Then Return trueName
        Return If(Path.GetFileName(If(containerPath, "")), "")
    End Function

    ''' <summary>
    ''' G-13: does this translator endpoint stay on this machine? An empty endpoint is the
    ''' translator's own default, which is local. Anything that cannot be parsed is
    ''' treated as REMOTE - the safe answer when the question is "may a secret leave".
    ''' </summary>
    Friend Function IsLoopbackEndpoint(endpoint As String) As Boolean
        Dim raw As String = If(endpoint, "").Trim()
        If raw.Length = 0 Then Return True
        If Not raw.Contains("://") Then raw = "http://" & raw

        Dim parsed As Uri = Nothing
        If Not Uri.TryCreate(raw, UriKind.Absolute, parsed) Then Return False
        Dim host As String = parsed.Host.Trim("["c, "]"c)
        If String.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) Then Return True

        Dim address As IPAddress = Nothing
        If IPAddress.TryParse(host, address) Then Return IPAddress.IsLoopback(address)
        Return False
    End Function

    ''' <summary>The host a confirmation names, or the raw text when it will not parse.</summary>
    Friend Function EndpointHost(endpoint As String) As String
        Dim raw As String = If(endpoint, "").Trim()
        Dim probe As String = If(raw.Contains("://"), raw, "http://" & raw)
        Dim parsed As Uri = Nothing
        If Uri.TryCreate(probe, UriKind.Absolute, parsed) AndAlso parsed.Host.Length > 0 Then Return parsed.Host
        Return raw
    End Function

End Module
#End If
