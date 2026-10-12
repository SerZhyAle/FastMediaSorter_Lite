#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq

''' <summary>Where the DPAPI blobs of resource credentials live, by handle. The registry JSON
''' holds the handle only (<see cref="ResourceDescriptor.SecretHandle"/>), never the blob, so a
''' resource list can be exported, logged or backed up without carrying a password.</summary>
Friend Interface IResourceSecretStore
    Sub PutBlob(handle As String, protectedBlob As String)
    Function GetBlob(handle As String) As String
    Sub DeleteBlob(handle As String)
    Function AllHandles() As IReadOnlyList(Of String)
End Interface

''' <summary>The registry-backed store: one value per handle in its own section.</summary>
Friend NotInheritable Class RegistryResourceSecretStore
    Implements IResourceSecretStore

    Private Const Section As String = "ResourceSecrets"

    Public Sub PutBlob(handle As String, protectedBlob As String) Implements IResourceSecretStore.PutBlob
        Microsoft.VisualBasic.Interaction.SaveSetting(App_name, Section, handle, protectedBlob)
        ' SaveSetting does not report failure; reading it back is the only proof the stage worked.
        If Not String.Equals(GetBlob(handle), protectedBlob, StringComparison.Ordinal) Then
            Throw New IO.IOException("The credential could not be stored.")
        End If
    End Sub

    Public Function GetBlob(handle As String) As String Implements IResourceSecretStore.GetBlob
        Return Microsoft.VisualBasic.Interaction.GetSetting(App_name, Section, handle, "")
    End Function

    Public Sub DeleteBlob(handle As String) Implements IResourceSecretStore.DeleteBlob
        Try
            Microsoft.VisualBasic.Interaction.DeleteSetting(App_name, Section, handle)
        Catch ex As ArgumentException
            ' Already gone: deleting twice is the same as deleting once.
        End Try
    End Sub

    Public Function AllHandles() As IReadOnlyList(Of String) Implements IResourceSecretStore.AllHandles
        Dim all As String(,) = Microsoft.VisualBasic.Interaction.GetAllSettings(App_name, Section)
        Dim result As New List(Of String)()
        If all IsNot Nothing Then
            For i As Integer = 0 To all.GetLength(0) - 1
                result.Add(all(i, 0))
            Next
        End If
        Return result
    End Function
End Class

Friend Enum ResourceCommitOutcome
    Committed = 0
    ''' <summary>An SFTP resource needs a credential and none was given (empty is not "no password").</summary>
    SecretRequired = 1
    ''' <summary>DPAPI could not protect the secret; nothing was written anywhere.</summary>
    ProtectionFailed = 2
    ''' <summary>The credential could not be stored; the registry was not touched.</summary>
    StageFailed = 3
    ''' <summary>The registry could not be saved; the staged credential was removed again and
    ''' the previous registry value is intact.</summary>
    RegistryFailed = 4
    NotFound = 5
End Enum

Friend NotInheritable Class ResourceCommitResult
    Friend Sub New(outcome As ResourceCommitOutcome, Optional handle As String = "")
        Me.Outcome = outcome
        Me.SecretHandle = If(handle, "")
    End Sub
    Friend ReadOnly Property Outcome As ResourceCommitOutcome
    Friend ReadOnly Property SecretHandle As String
    Friend ReadOnly Property Succeeded As Boolean
        Get
            Return Outcome = ResourceCommitOutcome.Committed
        End Get
    End Property
End Class

''' <summary>What reading a stored credential back came to.</summary>
Friend Enum CredentialReadOutcome
    Ok = 0
    ''' <summary>No handle, or no blob under it: the user is asked for the password again.</summary>
    Missing = 1
    ''' <summary>The blob did not decrypt (another profile, a damaged value): ask again, and the
    ''' old blob is left where it is until a new one has been committed.</summary>
    NeedsReentry = 2
End Enum

Friend NotInheritable Class CredentialReadResult
    Friend Sub New(outcome As CredentialReadOutcome, Optional secret As String = "")
        Me.Outcome = outcome
        Me.Secret = If(secret, "")
    End Sub
    Friend ReadOnly Property Outcome As CredentialReadOutcome
    Friend ReadOnly Property Secret As String
    Public Overrides Function ToString() As String
        Return Outcome.ToString()
    End Function
End Class

''' <summary>
''' The persistence order for adding, changing and removing a resource (100 §15.4 item 5):
''' <b>stage the protected secret under a NEW handle, then commit the registry reference
''' last</b>. A crash or failure before the commit can leave an orphaned secret - harmless,
''' and removable by <see cref="SweepOrphans"/> - but never a registry record pointing at a
''' missing or undecryptable credential. The superseded secret is deleted only after the
''' registry holds the new reference, and only when no other root of the same server shares it.
''' </summary>
Friend Module ResourceCommit

    ''' <summary>Adds or replaces <paramref name="descriptor"/> (matched by Id).</summary>
    ''' <param name="plainSecret">Nothing keeps the existing credential unchanged; a non-empty
    ''' string replaces it; the empty string is refused for SFTP.</param>
    Friend Function Commit(descriptor As ResourceDescriptor, plainSecret As String,
                           readValue As Func(Of String, String), writeValue As Action(Of String, String),
                           secrets As IResourceSecretStore,
                           Optional protect As Func(Of String, SecretProtectionResult) = Nothing) As ResourceCommitResult
        If descriptor Is Nothing OrElse Not ResourceRegistry.IsStorable(descriptor) Then
            Throw New ArgumentException("The descriptor is not storable.", NameOf(descriptor))
        End If
        Dim protector As Func(Of String, SecretProtectionResult) = If(protect, Function(s As String) DpapiSecretChecks.ProtectChecked(s))

        Dim current As List(Of ResourceDescriptor) = ResourceRegistry.Parse(readValue("Resources"))
        Dim previous As ResourceDescriptor = current.FirstOrDefault(Function(r) String.Equals(r.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase))
        Dim oldHandle As String = If(previous IsNot Nothing, previous.SecretHandle, If(descriptor.SecretHandle, ""))

        Dim newHandle As String = ""
        If plainSecret IsNot Nothing Then
            If plainSecret.Length = 0 Then Return New ResourceCommitResult(ResourceCommitOutcome.SecretRequired)
            Dim protectedSecret As SecretProtectionResult = protector(plainSecret)
            If Not protectedSecret.Succeeded Then Return New ResourceCommitResult(ResourceCommitOutcome.ProtectionFailed)
            newHandle = "cred:" & Guid.NewGuid().ToString("N")
            Try
                secrets.PutBlob(newHandle, protectedSecret.Value)
            Catch ex As Exception
                SafeDelete(secrets, newHandle)
                Return New ResourceCommitResult(ResourceCommitOutcome.StageFailed)
            End Try
        ElseIf descriptor.Kind = ResourceKind.Sftp AndAlso String.IsNullOrEmpty(oldHandle) Then
            Return New ResourceCommitResult(ResourceCommitOutcome.SecretRequired)
        End If

        Dim effectiveHandle As String = If(newHandle.Length > 0, newHandle, oldHandle)
        descriptor.SecretHandle = effectiveHandle
        Dim replacement As New List(Of ResourceDescriptor)(current.Where(Function(r) Not String.Equals(r.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase)))
        If previous IsNot Nothing Then
            ' Same slot in the list: a replaced resource does not jump to the end.
            Dim index As Integer = current.IndexOf(previous)
            replacement.Insert(Math.Min(index, replacement.Count), descriptor)
        Else
            replacement.Add(descriptor)
        End If

        Try
            ResourceRegistry.Save(replacement, readValue, writeValue)
        Catch ex As Exception
            ' Only a secret THIS call created is cleaned up; the previous one is still referenced.
            If newHandle.Length > 0 Then SafeDelete(secrets, newHandle)
            descriptor.SecretHandle = oldHandle
            Return New ResourceCommitResult(ResourceCommitOutcome.RegistryFailed)
        End Try

        If newHandle.Length > 0 AndAlso oldHandle.Length > 0 AndAlso Not IsReferenced(replacement, oldHandle) Then
            SafeDelete(secrets, oldHandle)
        End If
        Return New ResourceCommitResult(ResourceCommitOutcome.Committed, effectiveHandle)
    End Function

    ''' <summary>Removes a resource: the registry first, the credential afterwards and only if
    ''' no other root still points at it.</summary>
    Friend Function Remove(id As String, readValue As Func(Of String, String), writeValue As Action(Of String, String),
                           secrets As IResourceSecretStore) As ResourceCommitResult
        Dim current As List(Of ResourceDescriptor) = ResourceRegistry.Parse(readValue("Resources"))
        Dim target As ResourceDescriptor = current.FirstOrDefault(Function(r) String.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))
        If target Is Nothing Then Return New ResourceCommitResult(ResourceCommitOutcome.NotFound)
        ResourcePinMemory.Forget(target)
        Dim remaining As List(Of ResourceDescriptor) = current.Where(Function(r) Not ReferenceEquals(r, target)).ToList()
        Try
            ResourceRegistry.Save(remaining, readValue, writeValue)
        Catch ex As Exception
            Return New ResourceCommitResult(ResourceCommitOutcome.RegistryFailed)
        End Try
        If Not String.IsNullOrEmpty(target.SecretHandle) AndAlso Not IsReferenced(remaining, target.SecretHandle) Then
            SafeDelete(secrets, target.SecretHandle)
        End If
        ' a link's share id (ticket 171) is a second secret of the same resource and goes with it
        If Not String.IsNullOrEmpty(target.TunnelSecretHandle) AndAlso Not IsReferenced(remaining, target.TunnelSecretHandle) Then
            SafeDelete(secrets, target.TunnelSecretHandle)
        End If
        Return New ResourceCommitResult(ResourceCommitOutcome.Committed)
    End Function

    ''' <summary>Reads a resource's credential. A missing or undecryptable one is reported, never
    ''' replaced with "" and never deleted: the old blob is the only recovery copy there is.</summary>
    Friend Function ReadCredential(descriptor As ResourceDescriptor, secrets As IResourceSecretStore,
                                   Optional unprotect As Func(Of String, SecretProtectionResult) = Nothing) As CredentialReadResult
        If descriptor Is Nothing OrElse String.IsNullOrEmpty(descriptor.SecretHandle) Then Return New CredentialReadResult(CredentialReadOutcome.Missing)
        Dim blob As String = secrets.GetBlob(descriptor.SecretHandle)
        If String.IsNullOrEmpty(blob) Then Return New CredentialReadResult(CredentialReadOutcome.Missing)
        Dim opener As Func(Of String, SecretProtectionResult) = If(unprotect, Function(s As String) DpapiSecretChecks.UnprotectChecked(s))
        Dim opened As SecretProtectionResult = opener(blob)
        Return If(opened.Succeeded, New CredentialReadResult(CredentialReadOutcome.Ok, opened.Value),
                  New CredentialReadResult(CredentialReadOutcome.NeedsReentry))
    End Function

    ''' <summary>Deletes secrets no registry record references. Run explicitly, never from a
    ''' failure path: an orphan is harmless, a deleted live credential is not.</summary>
    Friend Function SweepOrphans(readValue As Func(Of String, String), secrets As IResourceSecretStore) As Integer
        Dim live As List(Of ResourceDescriptor) = ResourceRegistry.Parse(readValue("Resources"))
        Dim removed As Integer
        For Each handle As String In secrets.AllHandles().ToList()
            If Not IsReferenced(live, handle) Then
                SafeDelete(secrets, handle)
                removed += 1
            End If
        Next
        Return removed
    End Function

    Private Function IsReferenced(resources As IEnumerable(Of ResourceDescriptor), handle As String) As Boolean
        Return resources.Any(Function(r) String.Equals(r.SecretHandle, handle, StringComparison.Ordinal) OrElse
                                         String.Equals(r.TunnelSecretHandle, handle, StringComparison.Ordinal))
    End Function

    Private Sub SafeDelete(secrets As IResourceSecretStore, handle As String)
        Try
            secrets.DeleteBlob(handle)
        Catch ex As Exception
            AppFileLogger.WriteLine("Resource: a credential could not be removed - " & ex.GetType().Name)
        End Try
    End Sub

End Module
#End If
