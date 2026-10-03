#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Threading
Imports Xunit

''' <summary>A fact that needs the writable throw-away server: skipped, by name, unless
''' <c>tests\Integration\SftpConsumerLive.ps1</c> has built it.</summary>
Public NotInheritable Class WritableLiveFactAttribute
    Inherits FactAttribute

    Public Sub New()
        If String.IsNullOrEmpty(Environment.GetEnvironmentVariable(WritableServerFixture.ExeVariable)) Then
            Skip = "needs the writable live server: run tests\Integration\SftpConsumerLive.ps1"
        End If
    End Sub
End Class

''' <summary>
''' <c>tests\Integration\rwsftp</c>: a real SSH server with a real SFTP subsystem (pkg/sftp, the library the
''' worker is built on) that keeps every file in memory and listens on 127.0.0.1 only. It exists because the
''' worker's devserve is read-only by construction, and a remote recipient slot is all about writing. Its
''' host key lives in this fixture's temporary directory, so a restart comes back with the same identity -
''' and an empty file system, which is what a server that lost its state looks like.
''' </summary>
Public NotInheritable Class WritableServerFixture
    Implements IDisposable

    Friend Const ExeVariable As String = "FMS_SFTP_LIVE_RW"

    Private ReadOnly work As String
    Private ReadOnly exe As String
    Private process As Process
    Friend ReadOnly Property Port As Integer
    Friend ReadOnly Property User As String = "slot"
    Friend ReadOnly Property Password As String = "rw-" & Guid.NewGuid().ToString("N")
    Friend ReadOnly Property Fingerprint As String

    Public Sub New()
        exe = Environment.GetEnvironmentVariable(ExeVariable)
        If String.IsNullOrEmpty(exe) Then Return
        work = Path.Combine(Path.GetTempPath(), "fms-rw-live-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(work)
        Dim listener As New System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)
        listener.Start()
        Port = CType(listener.LocalEndpoint, System.Net.IPEndPoint).Port
        listener.Stop()
        Fingerprint = StartProcess()
    End Sub

    Private Function StartProcess() As String
        Dim info As New ProcessStartInfo(exe) With {
            .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True}
        info.ArgumentList.Add(Port.ToString())
        info.ArgumentList.Add(Path.Combine(work, "host_key.pem"))
        info.ArgumentList.Add(Password)
        process = Process.Start(info)
        AddHandler process.ErrorDataReceived, Sub(s As Object, e As DataReceivedEventArgs)
                                              End Sub
        process.BeginErrorReadLine()
        Dim line As Task(Of String) = process.StandardOutput.ReadLineAsync()
        If Not line.Wait(TimeSpan.FromSeconds(20)) OrElse String.IsNullOrWhiteSpace(line.Result) Then
            Stop_()
            Throw New InvalidOperationException("rwsftp printed nothing within 20 s")
        End If
        Using doc As JsonDocument = JsonDocument.Parse(line.Result)
            Return doc.RootElement.GetProperty("fingerprint").GetString()
        End Using
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

    Friend Sub Drop()
        Stop_()
    End Sub

    Friend Sub Restore()
        StartProcess()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Stop_()
        Try
            If work IsNot Nothing AndAlso Directory.Exists(work) Then Directory.Delete(work, recursive:=True)
        Catch
        End Try
    End Sub
End Class

''' <summary>
''' 007 §4.2 / §8 Ф3 against a REAL SSH server: a recipient slot that names a server folder. The slot
''' connections dial exactly as the viewer does (the resource's own connect, a trust prompt that always
''' says no, a pin store that never stores), and FileManager's verbs go through the routing - so what is
''' proved here is the viewer's path minus the window: the probe and its verdicts, the folder created
''' under a live parent, a move that lands byte-identical and takes the source only afterwards, its undo,
''' a copy that refuses to overwrite, a refused password, a key that is not the pin, and a dropped link.
'''
''' In the "ResourceRouting" collection: the routing reads one process-wide set of slot connections.
''' </summary>
<Collection("ResourceRouting")>
Public Class RemoteSlotLiveTests
    Implements IClassFixture(Of WritableServerFixture)
    Implements IDisposable

    Private ReadOnly server As WritableServerFixture
    Private ReadOnly scratch As String

    Public Sub New(fixture As WritableServerFixture)
        server = fixture
        scratch = Path.Combine(Path.GetTempPath(), "fms-rw-live-test-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(scratch)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        RemoteSlotConnections.Current?.Dispose()
        RemoteSlotConnections.Current = Nothing
        Try
            If Directory.Exists(scratch) Then Directory.Delete(scratch, recursive:=True)
        Catch
        End Try
    End Sub

    Private Function Descriptor(Optional pin As String = Nothing) As ResourceDescriptor
        Return New ResourceDescriptor With {
            .Id = "rw-live", .DisplayName = "RW live", .Kind = ResourceKind.Sftp, .Host = "127.0.0.1", .Port = server.Port,
            .UserName = server.User, .RootPath = "/MOV", .PinnedFingerprint = If(pin, server.Fingerprint)}
    End Function

    ''' <summary>The viewer's own dial (Main_Form.DialSlotResource): never asks, never pins.</summary>
    Private Shared Function Dial(descriptor As ResourceDescriptor, password As String, ct As CancellationToken) As ConnectOutcome
        Return ResourceConnect.ConnectAsync(descriptor, password,
                                            Function(fingerprint As String, c As CancellationToken) False,
                                            Function(fingerprint As String) False,
                                            Function() False, ct).GetAwaiter().GetResult()
    End Function

    Private Function Slots(Optional password As String = Nothing, Optional pin As String = Nothing) As RemoteSlotConnections
        Dim d As ResourceDescriptor = Descriptor(pin)
        Dim pw As String = If(password, server.Password)
        Dim created As New RemoteSlotConnections(Function() New List(Of ResourceDescriptor) From {d},
                                                 Function(x As ResourceDescriptor) pw,
                                                 AddressOf Dial)
        RemoteSlotConnections.Current = created
        Return created
    End Function

    Private Function SlotFolder(name As String) As String
        Return "sftp://" & server.User & "@127.0.0.1:" & server.Port.ToString() & "/MOV/" & name
    End Function

    Private Function LocalFile(name As String, size As Integer) As String
        Dim p As String = Path.Combine(scratch, name)
        Dim bytes(size - 1) As Byte
        RandomNumberGenerator.Fill(bytes)
        File.WriteAllBytes(p, bytes)
        Return p
    End Function

    Private Function ServerBytes(fileLocator As String) As Byte()
        Dim target As TransferEndpoint = RemoteSlotConnections.Current.Endpoint(fileLocator)
        Dim copy As String = Path.Combine(scratch, "back-" & Guid.NewGuid().ToString("N"))
        target.Provider.Download(target.Path, copy, Nothing, CancellationToken.None)
        Return File.ReadAllBytes(copy)
    End Function

    <WritableLiveFact>
    Public Sub Live_AMissingSlotFolderUnderALiveParent_IsCreatedOnce_ThenReady()
        Dim connections As RemoteSlotConnections = Slots()
        Dim folder As String = SlotFolder("new-" & Guid.NewGuid().ToString("N").Substring(0, 8))

        Assert.Equal(SlotState.WillBeCreated, connections.Probe(folder, allowCreate:=True).State)
        Assert.Equal(SlotState.Missing, connections.Probe(folder, allowCreate:=False).State)
        Assert.True(connections.EnsureFolder(folder))
        Assert.False(connections.EnsureFolder(folder))
        Assert.Equal(SlotState.Ready, connections.Probe(folder, allowCreate:=False).State)
    End Sub

    <WritableLiveFact>
    Public Sub Live_Writable_resource_renames_and_permanently_deletes_the_server_file()
        Dim outcome As ConnectOutcome = Dial(Descriptor(), server.Password, CancellationToken.None)
        Assert.True(outcome.Succeeded)
        Using provider As SftpProvider = DirectCast(outcome.Provider, SftpProvider)
            Dim folder As String = "/MOV/verbs-" & Guid.NewGuid().ToString("N")
            provider.CreateDirectory(folder)
            Dim source As String = LocalFile("rename.jpg", 2049)
            provider.Upload(source, folder & "/old.jpg", Nothing, CancellationToken.None)
            provider.Rename(folder & "/old.jpg", folder & "/new.jpg")
            Assert.Equal(StatOutcome.Missing, provider.Stat(folder & "/old.jpg").Outcome)
            Assert.Equal(StatOutcome.Found, provider.Stat(folder & "/new.jpg").Outcome)
            Dim downloaded As String = Path.Combine(scratch, "renamed.jpg")
            provider.Download(folder & "/new.jpg", downloaded, Nothing, CancellationToken.None)
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(downloaded))
            Assert.False(provider.Capabilities.HasRecycleBin)
            provider.DeleteFile(folder & "/new.jpg")
            Assert.Equal(StatOutcome.Missing, provider.Stat(folder & "/new.jpg").Outcome)
            Assert.Empty(provider.ListEntries(folder, CancellationToken.None))
        End Using
    End Sub

    <WritableLiveFact>
    Public Sub Live_AMoveIntoTheSlot_LandsByteIdentical_TakesTheSourceAfterwards_AndUndoBringsItBack()
        Dim connections As RemoteSlotConnections = Slots()
        Dim folder As String = SlotFolder("move-" & Guid.NewGuid().ToString("N").Substring(0, 8))
        connections.EnsureFolder(folder)
        Dim source As String = LocalFile("IMG 0001 фото.jpg", 300 * 1024 + 17)
        Dim original As Byte() = File.ReadAllBytes(source)
        Dim destination As String = SlotPaths.Combine(folder, Path.GetFileName(source))

        Dim moved As TransferOutcome = Nothing
        FileManager.MoveFileTracked(source, destination, False, moved)

        Assert.Equal(TransferResult.Completed, moved.Result)
        Assert.False(File.Exists(source))
        Assert.Equal(original, ServerBytes(destination))

        ' U brings the file back - and, out of a server that cannot condition a delete, keeps the
        ' server's copy and says so (100 §15.6.3: a move out of arbitrary SFTP is a copy). Both
        ' copies are whole; nothing is lost either way.
        Dim undone As TransferOutcome = TransferUndo.Undo(TransferUndo.Record(moved), ResourceRouting.TransferScratchDir)
        Assert.Equal(TransferResult.CopyOnly, undone.Result)
        Assert.Equal(original, File.ReadAllBytes(source))
        Assert.Equal(original, ServerBytes(destination))
    End Sub

    <WritableLiveFact>
    Public Sub Live_ACopyKeepsTheSource_RefusesToOverwrite_AndAFreeNameIsFound()
        Dim connections As RemoteSlotConnections = Slots()
        Dim folder As String = SlotFolder("copy-" & Guid.NewGuid().ToString("N").Substring(0, 8))
        connections.EnsureFolder(folder)
        Dim source As String = LocalFile("a.jpg", 4096)
        Dim destination As String = SlotPaths.Combine(folder, "a.jpg")

        FileManager.CopyFile(source, destination)
        Assert.True(File.Exists(source))
        Assert.True(connections.FileExists(destination))
        Assert.Equal(File.ReadAllBytes(source), ServerBytes(destination))

        Dim other As String = LocalFile("other.jpg", 10)
        Assert.ThrowsAny(Of Exception)(Sub() FileManager.CopyFile(other, destination))
        Assert.Equal(File.ReadAllBytes(source), ServerBytes(destination))

        Dim free As String = BatchPlan.FreeName(destination, Function(p As String) connections.FileExists(p), Nothing)
        Assert.Equal(SlotPaths.Combine(folder, "a (2).jpg"), free)
        FileManager.CopyFile(other, free)
        Assert.Equal(10, ServerBytes(free).Length)

        ' Deleting through the routing (an undo of a copy with its record) removes exactly that file.
        FileManager.DeleteFile(free)
        Assert.False(connections.FileExists(free))
        Assert.True(connections.FileExists(destination))
    End Sub

    <WritableLiveFact>
    Public Sub Live_AWrongPassword_IsDenied_AndIsNotTriedAgain()
        Dim connections As RemoteSlotConnections = Slots(password:="not-the-password")
        Assert.Equal(SlotState.Denied, connections.Probe(SlotFolder(""), allowCreate:=True).State)
        Assert.True(connections.WasRefused("rw-live"))
        Dim d As ResourceDescriptor = Nothing
        Dim remote As String = ""
        Assert.Equal(RemoteSlotProblem.NoCredential, connections.Locate(SlotFolder(""), d, remote))

        connections.RememberCredential("rw-live", server.Password)
        Assert.Equal(SlotState.Ready, connections.Probe("sftp://" & server.User & "@127.0.0.1:" & server.Port.ToString() & "/MOV", allowCreate:=True).State)
    End Sub

    <WritableLiveFact>
    Public Sub Live_AKeyThatIsNotThePin_IsDenied_AndNothingIsWritten()
        Dim connections As RemoteSlotConnections = Slots(pin:="SHA256:" & New String("A"c, 43))
        Dim verdict As SlotVerdict = connections.Probe(SlotFolder(""), allowCreate:=True)
        Assert.Equal(SlotState.Denied, verdict.State)
        Assert.Contains("KeyMismatch", verdict.Detail)
        Dim source As String = LocalFile("b.jpg", 100)
        Assert.ThrowsAny(Of Exception)(Sub() FileManager.CopyFile(source, SlotPaths.Combine(SlotFolder(""), "b.jpg")))
        Assert.True(File.Exists(source))
    End Sub

    <WritableLiveFact>
    Public Sub Live_ADroppedServer_IsUnreachable_AndAnsweringAgainAfterItReturns()
        Dim connections As RemoteSlotConnections = Slots()
        Dim root As String = "sftp://" & server.User & "@127.0.0.1:" & server.Port.ToString() & "/MOV"
        Assert.Equal(SlotState.Ready, connections.Probe(root, allowCreate:=False).State)

        server.Drop()
        Try
            Dim watch As Stopwatch = Stopwatch.StartNew()
            Assert.Equal(SlotState.Unreachable, connections.Probe(root, allowCreate:=False).State)
            ' Bounded: the probe's own wait in the viewer is RemoteSlotPolicy.ProbeTimeoutMs, and this
            ' answer must be honest well within the reconnect ladder's give-up.
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), watch.Elapsed.ToString())
        Finally
            server.Restore()
        End Try
        Assert.Equal(SlotState.Ready, connections.Probe(root, allowCreate:=False).State)
    End Sub

End Class
#End If
