#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading

' How the file manager mode of the F3 panel gets a provider for a place (257, slice N3). The manager connects FOR ITSELF through the
' same ResourceConnect the viewer and the slots use, and owns what it opened: it never reuses the viewer's open session and never
' attaches its own to it. Nothing here may ask the user anything - it runs on a worker - so the trust prompt always says no, the pin
' store never stores, and a password that is not already known is a typed "sign in first" status, not a dialog. Modern build only.

Friend Module ManagerProviderOpener

    ''' <param name="resources">The saved resources, read on every open so a removed one is noticed.</param>
    ''' <param name="credentialOf">The stored or this-run password of a resource, or Nothing when there is none the manager may use.</param>
    Friend Function Create(resources As Func(Of IEnumerable(Of ResourceDescriptor)),
                           credentialOf As Func(Of ResourceDescriptor, String)) As Func(Of ManagerPlace, CancellationToken, ManagerOpenResult)
        If resources Is Nothing Then Throw New ArgumentNullException(NameOf(resources))
        If credentialOf Is Nothing Then Throw New ArgumentNullException(NameOf(credentialOf))
        Return Function(place As ManagerPlace, ct As CancellationToken) Open(place, ct, resources, credentialOf)
    End Function

    Private Function Open(place As ManagerPlace, ct As CancellationToken,
                          resources As Func(Of IEnumerable(Of ResourceDescriptor)),
                          credentialOf As Func(Of ResourceDescriptor, String)) As ManagerOpenResult
        ct.ThrowIfCancellationRequested()
        If place Is Nothing Then Return ManagerOpenResult.Fail(ManagerStatusKind.Unavailable)

        ' A disk (or a plain UNC path Windows already reaches): nothing to dial, nothing to close.
        If Not place.NeedsConnection Then
            Return ManagerOpenResult.Ok(New LocalProvider(Function() False))
        End If

        Dim descriptor As ResourceDescriptor = Nothing
        Try
            descriptor = resources().FirstOrDefault(Function(r) String.Equals(r.Id, place.ResourceId, StringComparison.OrdinalIgnoreCase))
        Catch ex As Exception
            Return ManagerOpenResult.Fail(ManagerStatusKind.Unavailable)
        End Try
        If descriptor Is Nothing Then Return ManagerOpenResult.Fail(ManagerStatusKind.Unavailable)
        If ManagerPlace.ForResource(descriptor).ExchangeIdentity <> place.ExchangeIdentity Then Return ManagerOpenResult.Fail(ManagerStatusKind.Unavailable)

        ' Follow only this entry. A removed entry never falls back to the Share Manager's account.
        If ResourceOrigin.IsExchange(descriptor) AndAlso Not String.IsNullOrEmpty(descriptor.ExchangeEntryId) Then
            Dim sync As ExchangeSync = ExchangeFollowSync.For(descriptor)
            If sync IsNot Nothing Then
                Dim followed As ExchangeFollowOutcome = sync.FollowOneAsync(descriptor, descriptor.ExchangeDeviceName, ct).GetAwaiter().GetResult()
                descriptor = followed.Descriptor
                If ManagerPlace.ForResource(descriptor).ExchangeIdentity <> place.ExchangeIdentity Then Return ManagerOpenResult.Fail(ManagerStatusKind.Unavailable)
            End If
        End If

        Dim password As String = credentialOf(descriptor)
        If Not String.IsNullOrEmpty(descriptor.SecretHandle) Then
            Dim stored As CredentialReadResult = ResourceCommit.ReadCredential(descriptor, New RegistryResourceSecretStore())
            If stored.Outcome = CredentialReadOutcome.Ok Then password = stored.Secret
        End If
        If password Is Nothing AndAlso Not String.IsNullOrEmpty(descriptor.SecretHandle) Then
            Return ManagerOpenResult.Fail(ManagerStatusKind.SignInNeeded)
        End If

        Dim outcome As ConnectOutcome
        Try
            outcome = ResourceConnect.ConnectAsync(descriptor, If(password, ""),
                                                   Function(fingerprint As String, c As CancellationToken) False,
                                                   Function(fingerprint As String) False,
                                                   Function() False, ct, tunnelSource:=ExchangeTunnelSources.For(descriptor,
                                                       New RegistryResourceSecretStore(), Function(fp, token) False, Function(fp) False)).GetAwaiter().GetResult()
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            Return ManagerOpenResult.Fail(ManagerStatusKind.Disconnected, ResourceFailure.FromException(ex))
        End Try

        If outcome.Provider Is Nothing Then Return ManagerOpenResult.Fail(StatusOf(outcome), outcome.Failure)

        Dim directory As IManagerDirectoryProvider = TryCast(outcome.Provider, IManagerDirectoryProvider)
        If directory Is Nothing Then
            Dim orphan As IDisposable = TryCast(outcome.Provider, IDisposable)
            orphan?.Dispose()
            Return ManagerOpenResult.Fail(ManagerStatusKind.Unavailable)
        End If
        Return NewOpened(directory, outcome.Provider, descriptor)
    End Function

    Private Function NewOpened(directory As IManagerDirectoryProvider, provider As IFileSystemProvider, descriptor As ResourceDescriptor) As ManagerOpenResult
        Dim result As ManagerOpenResult = ManagerOpenResult.Ok(directory, TryCast(provider, IDisposable))
        result.ConnectedDescriptor = descriptor
        Return result
    End Function

    ''' <summary>A refused connection as the user is told it: an unknown key, a refused credential and a dead link are different sentences.</summary>
    Private Function StatusOf(outcome As ConnectOutcome) As ManagerStatusKind
        If outcome.HostKeyRefusal IsNot Nothing Then Return ManagerStatusKind.HostKeyUnknown
        Dim failure As ResourceFailure = outcome.Failure
        If failure Is Nothing Then Return ManagerStatusKind.Disconnected
        Select Case failure.Kind
            Case ResourceFailureKind.Cancelled
                Return ManagerStatusKind.Cancelled
            Case ResourceFailureKind.Auth, ResourceFailureKind.CredentialConflict
                Return ManagerStatusKind.Denied
            Case ResourceFailureKind.KeyMismatch
                Return ManagerStatusKind.HostKeyUnknown
            Case ResourceFailureKind.Unsupported
                Return ManagerStatusKind.Unavailable
            Case Else
                Return ManagerStatusKind.Disconnected
        End Select
    End Function
End Module
#End If
