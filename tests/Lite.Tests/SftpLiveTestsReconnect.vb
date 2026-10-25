#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary
Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

<Collection("ResourceRouting")>
Public Class SftpLiveTestsReconnect
    <WritableLiveFact>
    Public Sub A_dropped_winner_reraces_saved_paths_and_the_same_provider_downloads_again()
        Using server As New WritableServerFixture(), proxy As New LoopbackForwarder(server.Port)
            Dim descriptor As New ResourceDescriptor With {
                .Id = "reconnect-live", .Kind = ResourceKind.Sftp, .Host = "127.0.0.1", .Port = server.Port,
                .RootPath = "/MOV", .UserName = server.User,
                .Endpoints = New List(Of ResourceEndpoint) From {
                    New ResourceEndpoint With {.Kind = "lan", .Host = "127.0.0.1", .Port = proxy.Port}}}
            Dim prompts = 0
            Dim outcome = ResourceConnect.ConnectAsync(descriptor, server.Password,
                Function(pin, ct)
                    Interlocked.Increment(prompts)
                    Assert.Equal(server.Fingerprint, pin)
                    Return True
                End Function, Function(pin) True, Function() False, CancellationToken.None).GetAwaiter().GetResult()
            Assert.True(outcome.Succeeded)
            Using provider = DirectCast(outcome.Provider, SftpProvider)
                Assert.Equal(proxy.Port, provider.Descriptor.Port)
                Dim scratch = Path.Combine(Path.GetTempPath(), "fms-reconnect-" & Guid.NewGuid().ToString("N"))
                Directory.CreateDirectory(scratch)
                Try
                    Dim source = Path.Combine(scratch, "source.txt")
                    Dim target = Path.Combine(scratch, "target.txt")
                    File.WriteAllText(source, "survives endpoint loss")
                    provider.Upload(source, "/MOV/reconnect.txt", Nothing, CancellationToken.None)
                    proxy.Dispose()
                    Using deadline As New CancellationTokenSource(TimeSpan.FromSeconds(20))
                        provider.Download("/MOV/reconnect.txt", target, Nothing, deadline.Token)
                    End Using
                    Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target))
                    Assert.Equal(1, prompts)
                    Assert.True(provider.Stat("/MOV/reconnect.txt").Outcome = StatOutcome.Found)
                Finally
                    Directory.Delete(scratch, True)
                End Try
            End Using
        End Using
    End Sub

    Private NotInheritable Class LoopbackForwarder
        Implements IDisposable
        Private ReadOnly listener As New TcpListener(IPAddress.Loopback, 0)
        Private ReadOnly stopSource As New CancellationTokenSource()
        Private ReadOnly clients As New List(Of TcpClient)()
        Private ReadOnly acceptTask As Task
        Private ReadOnly serverPort As Integer
        Friend ReadOnly Property Port As Integer

        Friend Sub New(serverPort As Integer)
            Me.serverPort = serverPort
            listener.Start()
            Port = DirectCast(listener.LocalEndpoint, IPEndPoint).Port
            acceptTask = RunAsync()
        End Sub

        Private Async Function RunAsync() As Task
            Try
                While Not stopSource.IsCancellationRequested
                    Dim incoming = Await listener.AcceptTcpClientAsync(stopSource.Token)
                    Dim outgoing As New TcpClient()
                    SyncLock clients
                        clients.Add(incoming)
                        clients.Add(outgoing)
                    End SyncLock
                    Await outgoing.ConnectAsync(IPAddress.Loopback, serverPort, stopSource.Token)
                    ' One accepted SSH connection is enough for the initial winner; closing the proxy closes its socket.
                    Await Task.WhenAll(incoming.GetStream().CopyToAsync(outgoing.GetStream(), stopSource.Token),
                                       outgoing.GetStream().CopyToAsync(incoming.GetStream(), stopSource.Token))
                End While
            Catch ex As OperationCanceledException
            Catch ex As IOException When stopSource.IsCancellationRequested
            Catch ex As SocketException When stopSource.IsCancellationRequested
            Catch ex As ObjectDisposedException When stopSource.IsCancellationRequested
            End Try
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            If stopSource.IsCancellationRequested Then Return
            stopSource.Cancel()
            listener.Stop()
            SyncLock clients
                For Each socket As TcpClient In clients
                    socket.Dispose()
                Next
            End SyncLock
            Assert.True(acceptTask.Wait(TimeSpan.FromSeconds(5)), "loopback forwarder did not stop")
            stopSource.Dispose()
        End Sub
    End Class
End Class
#End If
