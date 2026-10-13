#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports Xunit

''' <summary>A fact that runs only against a live server: skipped, loudly and by name, whenever
''' <c>tests\Integration\SftpConsumerLive.ps1</c> has not built the server for it. A skip is the honest
''' answer - a green tick for a test that never touched a socket would be exactly the unverified pass
''' the verdict contract forbids.</summary>
Public NotInheritable Class LiveFactAttribute
    Inherits FactAttribute

    Public Sub New()
        If String.IsNullOrEmpty(Environment.GetEnvironmentVariable(LiveServerFixture.ExeVariable)) Then
            Skip = "needs a live server: run tests\Integration\SftpConsumerLive.ps1"
        End If
    End Sub
End Class

''' <summary>
''' The server the live cases talk to: the worker repository's own <c>devserve</c> - the real SFTP stack
''' (<c>internal/sftpserver</c>) serving one folder read-only - started on a throw-away data directory, so
''' neither the owner's host key nor their credential, shares or listen port are ever read or written.
''' It prints the same <c>.fmscfg</c> the Share Manager exports, which is used here as a real producer
''' fixture: the address, the user, the password and the host-key fingerprint all come from that document.
''' Restarting it keeps the data directory and the port, which is exactly what a server coming back after
''' a dropped link looks like (the port and the host key are guaranteed stable).
''' </summary>
Public NotInheritable Class LiveServerFixture
    Implements IDisposable

    Friend Const ExeVariable As String = "FMS_SFTP_LIVE_DEVSERVE"
    ''' <summary>The one root devserve serves; read-only by construction.</summary>
    Friend Const RootName As String = "Share"

    Private ReadOnly work As String
    Private ReadOnly exe As String
    Private process As Process
    Friend ReadOnly Property Port As Integer
    Friend ReadOnly Property Plan As FmsConfigPlan
    Friend ReadOnly Property Document As String

    Public Sub New()
        exe = Environment.GetEnvironmentVariable(ExeVariable)
        If String.IsNullOrEmpty(exe) Then Return
        work = Path.Combine(Path.GetTempPath(), "fms-sftp-live-" & Guid.NewGuid().ToString("N"))
        Dim share As String = Path.Combine(work, "share")
        Directory.CreateDirectory(Path.Combine(share, "sub"))
        Directory.CreateDirectory(Path.Combine(work, "data"))
        Directory.CreateDirectory(Path.Combine(work, "protected"))
        File.WriteAllText(Path.Combine(work, "protected", "secret.txt"), "protected fixture bytes")
        File.WriteAllBytes(Path.Combine(share, "a.jpg"), New Byte() {&HFF, &HD8, &HFF, &HE0, 1, 2, 3, 4, 5, 6, 7, 8})
        File.WriteAllBytes(Path.Combine(share, "b.png"), New Byte() {&H89, &H50, &H4E, &H47, 9, 8, 7, 6})
        File.WriteAllText(Path.Combine(share, "only.txt"), "read only share content")
        File.WriteAllText(Path.Combine(share, "sub", "c.txt"), "nested file")
        File.WriteAllBytes(Path.Combine(share, "big.bin"), New Byte(3 * 1024 * 1024 - 1) {})
        Port = FreePort()
        Document = StartProcess()
        Dim read As FmsConfigReadResult = FmsConfigReader.ReadText(Document)
        If Not read.Succeeded Then Throw New InvalidOperationException("devserve's own document does not read: " & read.Failure.ToString())
        Plan = read.Plan
    End Sub

    Private Shared Function FreePort() As Integer
        Dim listener As New System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)
        listener.Start()
        Try
            Return CType(listener.LocalEndpoint, System.Net.IPEndPoint).Port
        Finally
            listener.Stop()
        End Try
    End Function

    ''' <summary>Starts (or restarts) the server; returns the document it prints. Same data directory, same
    ''' port: the host key and the credential come back unchanged.</summary>
    Private Function StartProcess() As String
        Dim info As New ProcessStartInfo(exe) With {
            .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True}
        info.ArgumentList.Add(Path.Combine(work, "share"))
        info.ArgumentList.Add(Port.ToString())
        info.Environment("FMS_DEVSERVE_RESOURCE_PIN") = "216-original-pin"
        info.Environment("FMS_COMPANION_DATA_DIR") = Path.Combine(work, "data")
        process = Process.Start(info)
        ' Drain stderr so a full pipe never stalls the server.
        AddHandler process.ErrorDataReceived, Sub(s As Object, e As DataReceivedEventArgs)
                                              End Sub
        process.BeginErrorReadLine()
        Dim line As Task(Of String) = process.StandardOutput.ReadLineAsync()
        If Not line.Wait(TimeSpan.FromSeconds(20)) OrElse String.IsNullOrWhiteSpace(line.Result) Then
            Stop_()
            Throw New InvalidOperationException("devserve printed no document within 20 s")
        End If
        Return line.Result
    End Function

    Private Sub Stop_()
        Try
            If process IsNot Nothing AndAlso Not process.HasExited Then
                process.Kill(entireProcessTree:=True)
                process.WaitForExit(5000)
            End If
        Catch
        End Try
    End Sub

    ''' <summary>The cable is pulled: the server goes away, the data directory stays.</summary>
    Friend Sub Drop()
        Stop_()
    End Sub

    ''' <summary>The server returns on the same port with the same identity.</summary>
    Friend Sub Restore()
        StartProcess()
    End Sub

    Friend ReadOnly Property Host As String
        Get
            Return "127.0.0.1"
        End Get
    End Property

    Friend ReadOnly Property User As String
        Get
            Return Plan.UserName
        End Get
    End Property

    Friend ReadOnly Property Password As String
        Get
            Return Plan.Password
        End Get
    End Property

    Friend ReadOnly Property Fingerprint As String
        Get
            Return Plan.PinnedFingerprint
        End Get
    End Property

    Public Sub Dispose() Implements IDisposable.Dispose
        Stop_()
        Try
            If work IsNot Nothing AndAlso Directory.Exists(work) Then Directory.Delete(work, recursive:=True)
        Catch
        End Try
    End Sub
End Class

''' <summary>
''' 007 §2.2 / §8 Ф1: the SFTP consumer against a REAL SSH server. What the in-memory fixture cannot prove is
''' proved here: that what SSH.NET really throws lands in the failure classes of the spec, that the host key
''' is judged before authentication, that a document the Share Manager's own code produced imports and
''' opens, and that the listing and download do what the file list and the transfer layer assume.
'''
''' The server is read-only (devserve), so the write VERBS are covered by their refusal; their success path
''' stays with the in-memory fixture until a writable throw-away server exists.
'''
''' Run through the script, which builds devserve; without it every case is skipped by name.
''' </summary>
Public Class SftpLiveTests
    Implements IClassFixture(Of LiveServerFixture)
    Implements IDisposable

    Private ReadOnly server As LiveServerFixture
    Private ReadOnly scratch As String
    Private Shared ReadOnly Wrong_Pin As String = "SHA256:" & New String("A"c, 43)
    Private Const Root As String = "/" & LiveServerFixture.RootName

    Public Sub New(fixture As LiveServerFixture)
        server = fixture
        scratch = Path.Combine(Path.GetTempPath(), "fms-sftp-live-test-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(scratch)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(scratch) Then Directory.Delete(scratch, recursive:=True)
        Catch
        End Try
    End Sub

    ' ------------------------------------------------------------------------- helpers ----

    Private Function Resource(Optional pin As String = Nothing, Optional folder As String = Root) As ResourceDescriptor
        Return New ResourceDescriptor With {
            .Id = "live", .DisplayName = "Live", .Kind = ResourceKind.Sftp, .Host = server.Host, .Port = server.Port,
            .UserName = server.User, .RootPath = folder, .PinnedFingerprint = If(pin, server.Fingerprint)}
    End Function

    Private Function Connect(descriptor As ResourceDescriptor, Optional password As String = Nothing,
                             Optional consent As Func(Of String, CancellationToken, Boolean) = Nothing,
                             Optional persist As Func(Of String, Boolean) = Nothing,
                             Optional subfolders As Boolean = False) As ConnectOutcome
        Return ResourceConnect.ConnectAsync(descriptor, If(password, server.Password), consent, persist,
                                            Function() subfolders, CancellationToken.None).GetAwaiter().GetResult()
    End Function

    Private Function Opened(descriptor As ResourceDescriptor, Optional subfolders As Boolean = False) As SftpProvider
        Dim outcome As ConnectOutcome = Connect(descriptor, subfolders:=subfolders)
        Assert.True(outcome.Succeeded, "must connect: " & If(outcome.Failure Is Nothing, "", outcome.Failure.ToString()))
        Return DirectCast(outcome.Provider, SftpProvider)
    End Function

    Private Shared Function Names(entries As IReadOnlyList(Of ResourceEntry)) As String()
        Return entries.Select(Function(e) e.Name).OrderBy(Function(n) n, StringComparer.Ordinal).ToArray()
    End Function

    Private Shared Function Failure(work As Action) As ResourceFailure
        Dim thrown As ResourceFailureException = Assert.Throws(Of ResourceFailureException)(work)
        Return thrown.Failure
    End Function

    <LiveFact>
    Public Async Function Live_OriginalResourcePinIsRequiredAndProofCannotOpenAnotherRoot() As Task
        Dim lockedResource As ResourceDescriptor = Resource(folder:="/Protected")
        lockedResource.PinRequired = True
        ResourcePinMemory.Forget(lockedResource)
        Dim missing As ConnectOutcome = Await ResourceConnect.ConnectAsync(lockedResource, server.Password, Nothing, Nothing, Function() False, CancellationToken.None)
        Assert.False(missing.Succeeded)
        Assert.Equal(ResourceFailureKind.Auth, missing.Failure.Kind)
        Dim wrong As ConnectOutcome = Await ResourceConnect.ConnectAsync(lockedResource, server.Password, Nothing, Nothing, Function() False, CancellationToken.None, resourcePin:="wrong")
        Assert.False(wrong.Succeeded)
        Assert.Equal(ResourceFailureKind.Auth, wrong.Failure.Kind)
        Assert.Null(ResourcePinMemory.GetPin(lockedResource))
        Dim correct As ConnectOutcome = Await ResourceConnect.ConnectAsync(lockedResource, server.Password, Nothing, Nothing, Function() False, CancellationToken.None, resourcePin:="216-original-pin")
        Assert.True(correct.Succeeded, If(correct.Failure?.ToString(), ""))
        Using provider As SftpProvider = DirectCast(correct.Provider, SftpProvider)
            Assert.Equal({"secret.txt"}, Names(provider.ListEntries("/Protected", CancellationToken.None)))
            Dim target As String = Path.Combine(scratch, "secret.txt")
            provider.Download("/Protected/secret.txt", target, Nothing, CancellationToken.None)
            Assert.Equal("protected fixture bytes", File.ReadAllText(target))
            Assert.NotNull(Failure(Sub() provider.ListEntries("/Share", CancellationToken.None)))
            server.Drop()
            Try
                Dim unavailable As ResourceFailure = Failure(Sub() provider.EnsureResourceAuthorization(CancellationToken.None))
                Assert.True(unavailable.Kind = ResourceFailureKind.Transport OrElse unavailable.Kind = ResourceFailureKind.Timeout)
                Assert.Equal("216-original-pin", ResourcePinMemory.GetPin(lockedResource))
            Finally
                server.Restore()
            End Try
            provider.EnsureResourceAuthorization(CancellationToken.None)
            Assert.Equal({"secret.txt"}, Names(provider.ListEntries("/Protected", CancellationToken.None)))
        End Using
        Dim reopened As ConnectOutcome = Await ResourceConnect.ConnectAsync(lockedResource, server.Password, Nothing, Nothing, Function() False, CancellationToken.None)
        Assert.True(reopened.Succeeded, If(reopened.Failure?.ToString(), ""))
        DirectCast(reopened.Provider, IDisposable).Dispose()
        ResourcePinMemory.Forget(lockedResource)
        Using legacy As SftpProvider = Opened(Resource())
            Assert.NotNull(Failure(Sub() legacy.ListEntries("/Protected", CancellationToken.None)))
        End Using
    End Function

    ' --------------------------------------------------------------------------- listing ----

    <LiveFact>
    Public Sub Live_TheServersRoot_IsTheSharedFolder()
        Using provider As SftpProvider = Opened(Resource(folder:="/"))
            Dim top As IReadOnlyList(Of ResourceEntry) = provider.ListEntries("/", CancellationToken.None)
            Assert.Equal({LiveServerFixture.RootName}, Names(top))
            Assert.True(top(0).IsDirectory)
        End Using
    End Sub

    <LiveFact>
    Public Sub Live_AFolderListsItsFilesWithSizes_WithoutASecondRoundTrip()
        Using provider As SftpProvider = Opened(Resource())
            Dim listed As IReadOnlyList(Of ResourceEntry) = provider.ListEntries(Root, CancellationToken.None)
            Assert.Equal({"a.jpg", "b.png", "big.bin", "only.txt", "sub"}, Names(listed))
            Assert.Equal(12L, listed.Single(Function(e) e.Name = "a.jpg").Length)
            Assert.Equal(8L, listed.Single(Function(e) e.Name = "b.png").Length)
            Assert.Equal(3L * 1024 * 1024, listed.Single(Function(e) e.Name = "big.bin").Length)
            Assert.True(listed.Single(Function(e) e.Name = "sub").IsDirectory)
            Assert.All(listed.Where(Function(e) Not e.IsDirectory), Sub(e) Assert.True(e.LastWriteUtc > New DateTime(2000, 1, 1)))
        End Using
    End Sub

    <LiveFact>
    Public Sub Live_IncludeSubfolders_ReachesTheNestedFile()
        Using provider As SftpProvider = Opened(Resource(), subfolders:=True)
            Dim listed As IReadOnlyList(Of ResourceEntry) = provider.ListEntries(Root, CancellationToken.None)
            Assert.Contains(Root & "/sub/c.txt", listed.Select(Function(e) e.FullPath))
            Assert.False(provider.WasTruncated)
        End Using
    End Sub

    <LiveFact>
    Public Sub Live_Listing_entry_limit_is_reported_and_resets_after_a_complete_listing()
        Dim descriptor As ResourceDescriptor = Resource()
        Dim pin As New HostKeyGate(descriptor.PinnedFingerprint, Nothing, Nothing, Function() Environment.TickCount64)
        Using provider As New SftpProvider(descriptor, server.Password, pin, maxListingEntries:=2)
            Assert.Equal(2, provider.ListEntries(Root, CancellationToken.None).Count)
            Assert.True(provider.WasTruncated)
            Assert.Single(provider.ListEntries(Root & "/sub", CancellationToken.None))
            Assert.False(provider.WasTruncated)
        End Using
    End Sub

    <LiveFact>
    Public Sub Live_Listing_depth_limit_is_reported_without_entering_the_child()
        Dim descriptor As ResourceDescriptor = Resource()
        Dim pin As New HostKeyGate(descriptor.PinnedFingerprint, Nothing, Nothing, Function() Environment.TickCount64)
        Using provider As New SftpProvider(descriptor, server.Password, pin, includeSubfolders:=Function() True, maxListingDepth:=0)
            Dim listed = provider.ListEntries(Root, CancellationToken.None)
            Assert.True(provider.WasTruncated)
            Assert.DoesNotContain(Root & "/sub/c.txt", listed.Select(Function(e) e.FullPath))
            Assert.Contains(Root & "/sub", listed.Select(Function(e) e.FullPath))
        End Using
    End Sub

    <LiveFact>
    Public Sub Live_AMissingFolder_IsNoSuchFile_NotATransportError()
        Using provider As SftpProvider = Opened(Resource())
            Dim f As ResourceFailure = Failure(Sub() provider.ListEntries(Root & "/does-not-exist", CancellationToken.None))
            Assert.Equal(ResourceFailureKind.SftpStatus, f.Kind)
            Assert.Equal(2, f.StatusCode.Value)
            Assert.False(f.IsRetryable)
        End Using
    End Sub

    <LiveFact>
    Public Sub Live_Stat_TellsMissingFromFound()
        Using provider As SftpProvider = Opened(Resource())
            Assert.Equal(StatOutcome.Found, provider.Stat(Root & "/a.jpg").Outcome)
            Assert.Equal(StatOutcome.Missing, provider.Stat(Root & "/nope.jpg").Outcome)
        End Using
    End Sub

    ' -------------------------------------------------------------------------- download ----

    <LiveFact>
    Public Sub Live_ADownload_IsByteIdentical_AndLeavesNoPartFile()
        Using provider As SftpProvider = Opened(Resource())
            Dim target As String = Path.Combine(scratch, "a.jpg")
            provider.Download(Root & "/a.jpg", target, Nothing, CancellationToken.None)

            Assert.Equal(New Byte() {&HFF, &HD8, &HFF, &HE0, 1, 2, 3, 4, 5, 6, 7, 8}, File.ReadAllBytes(target))
            Assert.Empty(Directory.GetFiles(scratch, "*.fms-part"))
        End Using
    End Sub

    <LiveFact>
    Public Sub Live_ACancelledDownload_LeavesNeitherAPartFileNorTheFinalName()
        Using provider As SftpProvider = Opened(Resource())
            Dim target As String = Path.Combine(scratch, "big.bin")
            Using cancel As New CancellationTokenSource()
                Dim f As ResourceFailure = Failure(Sub() provider.Download(Root & "/big.bin", target, New CancelOnFirstReport(cancel), cancel.Token))
                Assert.Equal(ResourceFailureKind.Cancelled, f.Kind)
            End Using
            Assert.False(File.Exists(target))
            Assert.Empty(Directory.GetFiles(scratch))
        End Using
    End Sub

    Private NotInheritable Class CancelOnFirstReport
        Implements IProgress(Of Long)
        Private ReadOnly source As CancellationTokenSource
        Friend Sub New(source As CancellationTokenSource)
            Me.source = source
        End Sub
        Public Sub Report(value As Long) Implements IProgress(Of Long).Report
            source.Cancel()
        End Sub
    End Class

    <LiveFact>
    Public Sub Live_ASession_MaterialisesAFileThroughTheCacheAtItsListedSize()
        Dim descriptor As ResourceDescriptor = Resource()
        Using provider As SftpProvider = Opened(descriptor)
            Dim cache As String = Path.Combine(scratch, "session")
            Using session As New ResourceSession(descriptor, provider, cache, 64L * 1024L * 1024L)
                session.LoadListing(Root, CancellationToken.None)
                Dim first As ResourceEntry = session.Entries.Single(Function(e) e.Name = "a.jpg")
                Assert.Equal(EnsureLocalOutcome.Ready, session.TryEnsureLocal(first.LocalPath))
                Assert.Equal(12L, New FileInfo(first.LocalPath).Length)
            End Using
        End Using
    End Sub

    ' ------------------------------------------------------------------------ write verbs ----

    <LiveFact>
    Public Sub Live_AReadOnlyShare_RefusesEveryWrite_WithAServerStatus()
        Using provider As SftpProvider = Opened(Resource())
            Dim source As String = Path.Combine(scratch, "x.bin")
            File.WriteAllBytes(source, New Byte() {1})

            Assert.Equal(ResourceFailureKind.SftpStatus, Failure(Sub() provider.Upload(source, Root & "/x.bin", Nothing, CancellationToken.None)).Kind)
            Assert.Equal(ResourceFailureKind.SftpStatus, Failure(Sub() provider.DeleteFile(Root & "/only.txt")).Kind)
            Assert.Equal(ResourceFailureKind.SftpStatus, Failure(Sub() provider.Rename(Root & "/only.txt", Root & "/renamed.txt")).Kind)
            Assert.Equal(ResourceFailureKind.SftpStatus, Failure(Sub() provider.CreateDirectory(Root & "/newdir")).Kind)
            ' Nothing changed on the server.
            Assert.Equal(StatOutcome.Found, provider.Stat(Root & "/only.txt").Outcome)
            Assert.Equal(StatOutcome.Missing, provider.Stat(Root & "/x.bin").Outcome)
        End Using
    End Sub

    ' ------------------------------------------------------------- credential and host key ----

    <LiveFact>
    Public Sub Live_AWrongPassword_IsAuth_AndOpensNothing()
        Dim outcome As ConnectOutcome = Connect(Resource(), password:="definitely-not-the-password")

        Assert.False(outcome.Succeeded)
        Assert.Equal(ResourceFailureKind.Auth, outcome.Failure.Kind)
        Assert.False(outcome.Failure.IsRetryable)
    End Sub

    <LiveFact>
    Public Sub Live_APinThatIsNotTheServersKey_IsKeyMismatch_WithBothFingerprintsToShow()
        Dim outcome As ConnectOutcome = Connect(Resource(pin:=Wrong_Pin))

        Assert.False(outcome.Succeeded)
        Assert.Equal(ResourceFailureKind.KeyMismatch, outcome.Failure.Kind)
        Assert.NotNull(outcome.HostKeyRefusal)
        Assert.Equal(Wrong_Pin, outcome.HostKeyRefusal.Expected)
        Assert.Equal(server.Fingerprint, outcome.HostKeyRefusal.Received)
    End Sub

    <LiveFact>
    Public Sub Live_AnUnpinnedServer_DeclinedConsent_AsksOnce_AndStops()
        Dim asked As Integer
        Dim outcome As ConnectOutcome = Connect(Resource(pin:=""),
            consent:=Function(fingerprint As String, ct As CancellationToken)
                         Interlocked.Increment(asked)
                         Return False
                     End Function)

        Assert.False(outcome.Succeeded)
        Assert.Equal(ResourceFailureKind.Cancelled, outcome.Failure.Kind)
        ' Declining used to be retried by the provider's reconnect loop - four prompts for one No.
        Assert.Equal(1, asked)
    End Sub

    <LiveFact>
    Public Sub Live_AnUnpinnedServer_AcceptedConsent_ShowsTheRealFingerprint_AndPinsItBeforeLoggingIn()
        Dim shown As String = Nothing
        Dim pinned As String = Nothing
        Dim outcome As ConnectOutcome = Connect(Resource(pin:=""),
            consent:=Function(fingerprint As String, ct As CancellationToken)
                         shown = fingerprint
                         Return True
                     End Function,
            persist:=Function(fingerprint As String)
                         pinned = fingerprint
                         Return True
                     End Function)

        Assert.True(outcome.Succeeded)
        Assert.Equal(server.Fingerprint, shown)
        Assert.Equal(server.Fingerprint, pinned)
        DirectCast(outcome.Provider, IDisposable).Dispose()
    End Sub

    <LiveFact>
    Public Sub Live_AnAcceptedKeyThatCannotBeStored_StopsBeforeAuthentication()
        Dim outcome As ConnectOutcome = Connect(Resource(pin:=""),
            consent:=Function(fingerprint As String, ct As CancellationToken) True,
            persist:=Function(fingerprint As String) False)

        Assert.False(outcome.Succeeded)
        Assert.Equal(ResourceFailureKind.LocalIO, outcome.Failure.Kind)
    End Sub

    ' ---------------------------------------------------------- a real producer's document ----

    <LiveFact>
    Public Sub Live_TheShareManagersOwnDocument_ImportsAndOpensWithItsKeyAlreadyVerified()
        Dim plan As FmsConfigPlan = FmsConfigReader.ReadText(server.Document).Plan
        Dim preview As ImportPreview = ResourceImport.Preview(plan, New List(Of ResourceDescriptor)(), AddressOf ResourceImport.NewResourceId)
        Assert.True(preview.CanApply)

        ' The document's own address is the machine's LAN address; the race tries it exactly as written, and
        ' the fingerprint in the document is the pin - so this very first connection is already verified and
        ' no trust prompt may appear.
        Dim imported As ResourceDescriptor = preview.Items.Single().Descriptor
        Assert.Equal(server.Fingerprint, imported.PinnedFingerprint)
        Assert.True(imported.ReadOnlyHint, "the producer exported the root read-only")
        Dim prompted As Integer
        Dim outcome As ConnectOutcome = ResourceConnect.ConnectAsync(imported, plan.Password,
            Function(fingerprint As String, ct As CancellationToken)
                Interlocked.Increment(prompted)
                Return False
            End Function, Nothing, Nothing, CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(outcome.Succeeded, "the imported resource must open: " & If(outcome.Failure Is Nothing, "", outcome.Failure.ToString()))
        Assert.Equal(0, prompted)
        Using provider As SftpProvider = DirectCast(outcome.Provider, SftpProvider)
            Assert.False(provider.Capabilities.CanWrite, "a root exported read-only is read-only here")
            Assert.Contains("a.jpg", Names(provider.ListEntries(imported.RootPath, CancellationToken.None)))
        End Using
    End Sub

    ' ------------------------------------------------------------------------- the link ----

    <LiveFact>
    Public Sub Live_ADroppedServer_IsATransportFailure_NotLocalIO_AndTheNextCallReconnects()
        Using provider As SftpProvider = Opened(Resource())
            Assert.NotEmpty(provider.ListEntries(Root, CancellationToken.None))
            server.Drop()
            Try
                Dim f As ResourceFailure = Failure(Sub() provider.ListEntries(Root, CancellationToken.None))
                ' The cable is out: a link failure the user can wait out - never "the disk is bad", and
                ' never something that would remove files from the list (007 §2.2).
                Assert.True(f.IsRetryable, "expected Transport or Timeout, got " & f.ToString())
            Finally
                server.Restore()
            End Try
            ' The same pinned key, no prompt, the reconnect ladder (1 s, 4 s, 10 s) and the list is back.
            Assert.NotEmpty(provider.ListEntries(Root, CancellationToken.None))
        End Using
    End Sub

End Class
#End If
