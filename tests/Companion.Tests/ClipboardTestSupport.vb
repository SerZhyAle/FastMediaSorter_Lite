Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

' Spec 230 section 4: what the clipboard tests share - a clipboard that is a fake (so no test touches the person's real one), a
' scripted exchange server that keeps one value the way a real one does, and a rig that wires both into the orchestrator.
' Nothing here leaves the machine: the server is the kit's TLS stub on 127.0.0.1.

''' <summary>The clipboard as the exchange sees it, with nothing behind it: a read answers a scripted snapshot, a place is recorded.</summary>
Friend NotInheritable Class FakeClipboardPort
    Implements IClipboardPort

    Public Snapshot As ClipboardSnapshot = ClipboardSnapshot.OfKind(ClipboardReadKind.Empty)
    Public PlaceResult As ClipboardPlaceOutcome = ClipboardPlaceOutcome.Placed
    ''' <summary>When set, a read waits for it (or for the token) before it answers: an operation that is under way.</summary>
    Public ReadGate As TaskCompletionSource(Of Boolean)
    ''' <summary>When set, a read throws it: a defect below the exchange.</summary>
    Public ThrowOnRead As Exception
    Public PlacedText As Byte()
    Public PlacedImage As Byte()
    Public PlacedFile As String
    Private _reads As Integer
    Private _places As Integer

    Public ReadOnly Property Reads As Integer
        Get
            Return Volatile.Read(_reads)
        End Get
    End Property

    Public ReadOnly Property Places As Integer
        Get
            Return Volatile.Read(_places)
        End Get
    End Property

    Public Async Function ReadAsync(ct As CancellationToken) As Task(Of ClipboardSnapshot) Implements IClipboardPort.ReadAsync
        Interlocked.Increment(_reads)
        If ThrowOnRead IsNot Nothing Then Throw ThrowOnRead
        Dim gate As TaskCompletionSource(Of Boolean) = ReadGate
        If gate IsNot Nothing Then
            Using registration As CancellationTokenRegistration = ct.Register(Sub() gate.TrySetCanceled(ct))
                Await gate.Task.ConfigureAwait(False)
            End Using
        End If
        Return Snapshot
    End Function

    Public Function PlaceTextAsync(utf8 As Byte(), ct As CancellationToken) As Task(Of ClipboardPlaceOutcome) Implements IClipboardPort.PlaceTextAsync
        Interlocked.Increment(_places)
        PlacedText = utf8
        Return Task.FromResult(PlaceResult)
    End Function

    Public Function PlaceImageAsync(png As Byte(), ct As CancellationToken) As Task(Of ClipboardPlaceOutcome) Implements IClipboardPort.PlaceImageAsync
        Interlocked.Increment(_places)
        PlacedImage = png
        Return Task.FromResult(PlaceResult)
    End Function

    Public Function PlaceFileAsync(path As String, ct As CancellationToken) As Task(Of ClipboardPlaceOutcome) Implements IClipboardPort.PlaceFileAsync
        Interlocked.Increment(_places)
        PlacedFile = path
        Return Task.FromResult(PlaceResult)
    End Function

End Class

''' <summary>The orchestrator with its edges replaced: a fake clipboard, the servers a test added, a folder of its own and a
''' log that is a list.</summary>
Friend NotInheritable Class ClipboardTestRig
    Implements IDisposable

    Public ReadOnly Port As New FakeClipboardPort()
    Public ReadOnly Targets As New List(Of ClipboardTarget)()
    ''' <summary>The folder a received file goes into.</summary>
    Public ReadOnly Folder As String
    ''' <summary>A folder for the files a test copies "from the clipboard".</summary>
    Public ReadOnly Source As String
    Private ReadOnly _log As New List(Of String)()
    ''' <summary>The device of an entry whose server does not answer (nothing to resolve a directory from): a key and an empty directory.</summary>
    Private ReadOnly _unreachable As New SealedTestDevice(ExchangeWireTestKit.IdOf("D"c))

    Public Sub New()
        Dim root As String = Path.Combine(Path.GetTempPath(), "fms-clip-test-" & Guid.NewGuid().ToString("N"))
        Folder = Path.Combine(root, "received")
        Source = Path.Combine(root, "source")
        Directory.CreateDirectory(Folder)
        Directory.CreateDirectory(Source)
    End Sub

    Public Function Logged() As String()
        SyncLock _log
            Return _log.ToArray()
        End SyncLock
    End Function

    Public Function Exchange() As ClipboardExchange
        Dim ports As New ClipboardExchangePorts With {
            .Targets = Function() New List(Of ClipboardTarget)(Targets),
            .Port = Port,
            .ProductVersion = "26.10.16.1",
            .ReceivedFolder = Function() Folder,
            .Log = Sub(line)
                       SyncLock _log
                           _log.Add(line)
                       End SyncLock
                   End Sub,
            .Idle = TimeSpan.FromSeconds(10),
            .StateIdle = TimeSpan.FromSeconds(10),
            .Sealing = Function(target As ClipboardTarget, ct As CancellationToken) Task.FromResult(ClipboardTestKit.SealingFor(target, _unreachable))}
        Return New ClipboardExchange(ports)
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _unreachable.Dispose()
        Try
            Directory.Delete(Path.GetDirectoryName(Folder), True)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            ' a test folder that cannot be removed is left to the temp cleaner
        End Try
    End Sub

End Class

Friend Module ClipboardTestKit

    Friend Function Token(Optional seconds As Integer = 90) As CancellationToken
        Return New CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token
    End Function

    Friend Function Sha(bytes As Byte()) As String
        Return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
    End Function

    ''' <summary>One server of the person's, addressed the way an account entry is.</summary>
    Friend Function TargetOf(server As ClipboardTestServer, label As String) As ClipboardTarget
        Return New ClipboardTarget(label, "key|" & label, New ExchangeServerAddress("127.0.0.1", server.Port), server.Fingerprint,
                                   New ExchangeDeviceCredentials(ExchangeWireTestKit.IdOf("D"c), ExchangeWireTestKit.TokenOf("T"c)),
                                   ExchangeTlsTransport.Instance)
    End Function

    ''' <summary>What the sealed clipboard needs for one target: the id, the key and the directory of the entry as the test server
    ''' models them. An address no test server listens on gets the device of the rig and an empty directory that has heard nothing, as
    ''' the real provider returns for a server that never answered: the exchange then reports the real reason.</summary>
    Friend Function SealingFor(target As ClipboardTarget, unreachable As SealedTestDevice) As ExchangeClipboardSealing
        Dim server As ClipboardTestServer = ClipboardTestServer.ByPort(target.Address.Port)
        If server Is Nothing Then
            Dim silent As New SealedTestDirectory(unreachable) With {.ServerHeard = False}
            Return New ExchangeClipboardSealing(unreachable.Id, unreachable.Key, silent)
        End If
        Return New ExchangeClipboardSealing(server.Own.Id, If(server.Directory.OwnKeyMissing, Nothing, server.Own.Key), server.Directory)
    End Function

    ''' <summary>A server address nobody listens on: the port is taken and given back.</summary>
    Friend Function ClosedPort() As Integer
        Dim listener As New TcpListener(IPAddress.Loopback, 0)
        listener.Start()
        Dim port As Integer = DirectCast(listener.LocalEndpoint, IPEndPoint).Port
        listener.Stop()
        Return port
    End Function

    Friend Async Function ReadExactAsync(stream As Stream, count As Long, ct As CancellationToken) As Task(Of Byte())
        Dim result(CInt(count) - 1) As Byte
        Dim got As Integer = 0
        While got < result.Length
            Dim n As Integer = Await stream.ReadAsync(result, got, result.Length - got, ct)
            If n = 0 Then Exit While
            got += n
        End While
        If got < result.Length Then ReDim Preserve result(got - 1)
        Return result
    End Function

    ''' <summary>Reads `count` bytes and returns how many came and their SHA-256, keeping none of them.</summary>
    Friend Async Function HashExactAsync(stream As Stream, count As Long, ct As CancellationToken) As Task(Of Tuple(Of Long, String))
        Dim buffer(65535) As Byte
        Dim got As Long = 0
        Using h As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            While got < count
                Dim n As Integer = Await stream.ReadAsync(buffer, 0, CInt(Math.Min(count - got, CLng(buffer.Length))), ct)
                If n = 0 Then Exit While
                h.AppendData(buffer, 0, n)
                got += n
            End While
            Return Tuple.Create(got, Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant())
        End Using
    End Function

    ''' <summary>Waits (without sleeping in a loop on the test's thread pool slot for long) until a condition holds.</summary>
    Friend Async Function WaitUntilAsync(condition As Func(Of Boolean), Optional seconds As Integer = 20) As Task(Of Boolean)
        Dim until As DateTime = DateTime.UtcNow.AddSeconds(seconds)
        While DateTime.UtcNow < until
            If condition.Invoke() Then Return True
            Await Task.Delay(10)
        End While
        Return condition.Invoke()
    End Function

    ''' <summary>Deterministic bytes: the same length always gives the same content.</summary>
    Friend Function Pattern(length As Integer) As Byte()
        Dim b(length - 1) As Byte
        For i As Integer = 0 To length - 1
            b(i) = CByte((i * 31 + 7) Mod 251)
        Next
        Return b
    End Function

    ''' <summary>A small real PNG of a few distinct opaque colours.</summary>
    Friend Function MakePng(Optional width As Integer = 8, Optional height As Integer = 6) As Byte()
        Using bitmap As New System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb)
            For y As Integer = 0 To height - 1
                For x As Integer = 0 To width - 1
                    bitmap.SetPixel(x, y, System.Drawing.Color.FromArgb(255, (x * 37) Mod 256, (y * 53) Mod 256, ((x + y) * 17) Mod 256))
                Next
            Next
            Using ms As New MemoryStream()
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png)
                Return ms.ToArray()
            End Using
        End Using
    End Function

End Module
