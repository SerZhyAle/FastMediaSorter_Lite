#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Threading
Imports Xunit

Public NotInheritable Class SmbLiveFactAttribute
    Inherits FactAttribute

    Public Sub New()
        If String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FMS_SMB_LIVE_SHARE")) Then
            Skip = "needs an explicitly supplied share: SftpConsumerLive.ps1 -SmbShare <UNC>"
        End If
    End Sub
End Class

''' <summary>Read-only acceptance against an owner-supplied share with the current Windows
''' credentials. No file on the share is changed, no credential is stored, and no global
''' connection is force-cancelled. Two-user/password-prompt acceptance remains separate.</summary>
Public Class SmbLiveTests
    <SmbLiveFact>
    Public Sub Live_Smb_existing_Windows_connection_survives_resource_leave()
        Dim root As String = SmbPathUtil.ExtractUncRoot(Environment.GetEnvironmentVariable("FMS_SMB_LIVE_SHARE"))
        Dim existingUser As String = ""
        Dim before As Integer = NativeWNet.Instance.GetConnectedUser(root, existingUser)
        Assert.True(before = 0 OrElse before = SmbConnectionManager.ERROR_NOT_CONNECTED)
        Dim fixtureCreated As Boolean
        If before <> 0 Then
            Dim resource As New NETRESOURCE With {.dwType = SmbConnectionManager.RESOURCETYPE_DISK, .lpRemoteName = root}
            Assert.Equal(0, NativeWNet.Instance.WNetAddConnection2Impl(resource, Nothing, Nothing, SmbConnectionManager.CONNECT_TEMPORARY))
            fixtureCreated = True
        End If
        Try
            Assert.Equal(0, NativeWNet.Instance.GetConnectedUser(root, existingUser))
            Dim manager As New SmbConnectionManager()
            Dim result = manager.AcquireLease(root, "", Nothing)
            Assert.True(result.Succeeded)
            Using lease As SmbConnectionLease = result.Value
                Assert.False(lease.IsAppCreated)
            End Using
            Dim remainingUser As String = ""
            Assert.Equal(0, NativeWNet.Instance.GetConnectedUser(root, remainingUser))
            Assert.Equal(existingUser, remainingUser)
            Assert.Equal(0, manager.ActiveCount)
        Finally
            If fixtureCreated Then Assert.Equal(0, NativeWNet.Instance.WNetCancelConnection2Impl(root, 0, False))
        End Try
    End Sub

    <SmbLiveFact>
    Public Sub Live_Smb_current_account_lists_downloads_and_releases_its_lease()
        Dim share As String = Environment.GetEnvironmentVariable("FMS_SMB_LIVE_SHARE")
        Dim root As String = SmbPathUtil.ExtractUncRoot(share)
        Assert.False(String.IsNullOrEmpty(root))
        Dim existingUser As String = ""
        Dim before As Integer = NativeWNet.Instance.GetConnectedUser(root, existingUser)
        Assert.True(before = 0 OrElse before = SmbConnectionManager.ERROR_NOT_CONNECTED, "connection state must be inspectable")
        Dim manager As New SmbConnectionManager()
        Dim lease = manager.AcquireLease(share, "", Nothing)
        Assert.True(lease.Succeeded, If(lease.Failure Is Nothing, "", lease.Failure.ToString()))
        Dim owned As Boolean = lease.Value.IsAppCreated
        Assert.Equal(before <> 0, owned)
        Dim scratch As String = Path.Combine(Path.GetTempPath(), "fms-smb-live-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(scratch)
        Try
            Using provider As New SmbProvider(New ResourceDescriptor With {.Kind = ResourceKind.Smb, .RootPath = share, .ReadOnlyHint = True}, lease.Value)
                Dim listing = provider.ListEntries(share, CancellationToken.None)
                Assert.NotEmpty(listing)
                Assert.False(provider.Capabilities.CanWrite)
                Dim sample = listing.FirstOrDefault(Function(e) Not e.IsDirectory AndAlso e.Length > 0 AndAlso e.Length <= 65536)
                Assert.NotNull(sample)
                Assert.Equal(StatOutcome.Found, provider.Stat(sample.FullPath).Outcome)
                Dim copy As String = Path.Combine(scratch, "download.bin")
                provider.Download(sample.FullPath, copy, Nothing, CancellationToken.None)
                Assert.Equal(File.ReadAllBytes(sample.FullPath), File.ReadAllBytes(copy))
            End Using
            Assert.True(lease.Value.IsDisposed)
            Assert.Equal(0, manager.ActiveCount)
            Dim afterUser As String = ""
            Dim after As Integer = NativeWNet.Instance.GetConnectedUser(root, afterUser)
            If owned Then
                Assert.Equal(SmbConnectionManager.ERROR_NOT_CONNECTED, after)
            Else
                Assert.Equal(0, after)
                Assert.Equal(existingUser, afterUser)
            End If
        Finally
            lease.Value.Dispose()
            Directory.Delete(scratch, recursive:=True)
        End Try
    End Sub
End Class
#End If
