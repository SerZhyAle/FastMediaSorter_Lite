#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Threading
Imports Xunit

Public Class SftpProviderTests
    Implements IDisposable

    Private ReadOnly tempDir As String

    Public Sub New()
        tempDir = Path.Combine(Path.GetTempPath(), "fms-sftp-test-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(tempDir) Then Directory.Delete(tempDir, recursive:=True)
        Catch
        End Try
    End Sub

    <Fact>
    Public Sub SftpProvider_Capabilities_ReflectReadOnlyHint()
        Dim readOnlyDesc As New ResourceDescriptor With {
            .Id = "sftp-ro",
            .Kind = ResourceKind.Sftp,
            .Host = "127.0.0.1",
            .Port = 22,
            .UserName = "user",
            .RootPath = "/media",
            .ReadOnlyHint = True
        }
        Dim roProvider As New SftpProvider(readOnlyDesc, "pass", Nothing)
        Assert.Equal(ResourceKind.Sftp, roProvider.Kind)
        Assert.False(roProvider.Capabilities.CanWrite)
        Assert.False(roProvider.Capabilities.CanDelete)
        Assert.False(roProvider.Capabilities.CanRename)
        Assert.False(roProvider.Capabilities.HasRecycleBin)
        Assert.True(roProvider.Capabilities.NeedsMaterialisation)
        Assert.True(roProvider.Capabilities.SupportsServerSideRename)
        Assert.Equal(ProviderCostHint.Slow, roProvider.Capabilities.CostHint)

        Dim writableDesc As New ResourceDescriptor With {
            .Id = "sftp-rw",
            .Kind = ResourceKind.Sftp,
            .Host = "127.0.0.1",
            .Port = 22,
            .UserName = "user",
            .RootPath = "/media",
            .ReadOnlyHint = False
        }
        Dim rwProvider As New SftpProvider(writableDesc, "pass", Nothing)
        Assert.True(rwProvider.Capabilities.CanWrite)
        Assert.True(rwProvider.Capabilities.CanDelete)
        Assert.True(rwProvider.Capabilities.CanRename)
    End Sub

    <Fact>
    Public Sub SftpResourceConnector_FailsOnMissingOrInvalidSecret()
        Dim desc As New ResourceDescriptor With {
            .Id = "sftp-1",
            .Kind = ResourceKind.Sftp,
            .Host = "127.0.0.1",
            .Port = 22,
            .UserName = "user",
            .RootPath = "/media",
            .SecretHandle = "cred:missing"
        }
        Dim mockSecrets As New MockSecretStore()

        ' Decryption failure
        Dim result = SftpResourceConnector.Connect(desc, mockSecrets, Nothing,
            unprotect:=Function(blob) New SecretProtectionResult(SecretProtectionStatus.ProtectionFailed, ""))
        Assert.False(result.Succeeded)
        Assert.Equal(ResourceFailureKind.Auth, result.Failure.Kind)
    End Sub

    <Fact>
    Public Sub SftpProvider_TryStat_ReturnsFalseOnDisposedOrEmpty()
        Dim desc As New ResourceDescriptor With {
            .Id = "sftp-1",
            .Kind = ResourceKind.Sftp,
            .Host = "127.0.0.1",
            .Port = 22,
            .UserName = "user",
            .RootPath = "/media"
        }
        Dim provider As New SftpProvider(desc, "pass", Nothing)
        provider.Dispose()

        Dim entry As ResourceEntry = Nothing
        Assert.False(provider.TryStat("", entry))
        Assert.False(provider.TryStat("/nonexistent", entry))
    End Sub

    Private Class MockSecretStore
        Implements IResourceSecretStore

        Private ReadOnly dict As New Dictionary(Of String, String)()

        Public Sub PutBlob(handle As String, protectedBlob As String) Implements IResourceSecretStore.PutBlob
            dict(handle) = protectedBlob
        End Sub

        Public Function GetBlob(handle As String) As String Implements IResourceSecretStore.GetBlob
            Dim v As String = Nothing
            Return If(dict.TryGetValue(handle, v), v, "blob-data")
        End Function

        Public Sub DeleteBlob(handle As String) Implements IResourceSecretStore.DeleteBlob
            dict.Remove(handle)
        End Sub

        Public Function AllHandles() As IReadOnlyList(Of String) Implements IResourceSecretStore.AllHandles
            Return New List(Of String)(dict.Keys)
        End Function
    End Class

End Class
#End If
