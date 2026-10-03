Option Strict On

Imports System.Collections.Generic
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

' Audit 057.S10-1: OllamaTranslator error handling and ITranslator contract.
' A non-cancellation error (e.g. server crash or aborted connection mid-run)
' must not escape TranslateAsync - it must log and return TranslationBatch.AllFailed(Other).
Public Class OllamaTranslatorTests
    Implements IDisposable

    Private listener As HttpListener
    Private port As Integer
    Private serverRunning As Boolean
    Private serverLoop As Task

    Private Function StartMockServer(handler As Action(Of HttpListenerContext)) As String
        Dim sock As New TcpListener(IPAddress.Loopback, 0)
        Try
            sock.Start()
            port = CType(sock.LocalEndpoint, IPEndPoint).Port
        Finally
            sock.Stop()
        End Try

        listener = New HttpListener()
        listener.Prefixes.Add("http://127.0.0.1:" & port.ToString() & "/")
        listener.Start()
        serverRunning = True

        serverLoop = Task.Run(
            Sub()
                While serverRunning AndAlso listener IsNot Nothing AndAlso listener.IsListening
                    Try
                        Dim ctx As HttpListenerContext = listener.GetContext()
                        Task.Run(Sub()
                                     Try
                                         handler(ctx)
                                     Catch
                                         Try : ctx.Response.Abort() : Catch : End Try
                                     End Try
                                 End Sub)
                    Catch
                    End Try
                End While
            End Sub)

        Return "http://127.0.0.1:" & port.ToString()
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        serverRunning = False
        If listener IsNot Nothing Then
            Try
                listener.Stop()
                listener.Close()
            Catch
            End Try
            listener = Nothing
        End If
    End Sub

    <Fact>
    Public Async Function TranslateAsync_WhenGenerateAborts_ReturnsAllFailedOtherWithoutThrowing() As Task
        Dim endpoint As String = StartMockServer(
            Sub(ctx As HttpListenerContext)
                Dim path As String = ctx.Request.Url.AbsolutePath
                If path = "/api/tags" Then
                    Dim json As String = "{""models"":[{""name"":""qwen2.5:3b""}]}"
                    Dim bytes As Byte() = Encoding.UTF8.GetBytes(json)
                    ctx.Response.StatusCode = 200
                    ctx.Response.ContentType = "application/json"
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length)
                    ctx.Response.Close()
                ElseIf path = "/api/generate" Then
                    ctx.Response.Abort()
                Else
                    ctx.Response.StatusCode = 404
                    ctx.Response.Close()
                End If
            End Sub)

        Dim translator As New OllamaTranslator(endpoint, "qwen2.5:3b")
        Dim sources As New List(Of String) From {"Sample source text"}

        Dim batch As TranslationBatch = Await translator.TranslateAsync(sources, "auto", "ru", CancellationToken.None)

        Assert.NotNull(batch)
        Assert.Equal(TranslationFailure.Other, batch.Failure)
        Assert.Equal(1, batch.FailedCount)
        Assert.Single(batch.Texts)
        Assert.Equal("Sample source text", batch.Texts(0))
        Assert.False(batch.Translated(0))
    End Function

    <Fact>
    Public Async Function TranslateAsync_WhenTagsFails_ReturnsAllFailedOtherWithoutThrowing() As Task
        Dim endpoint As String = StartMockServer(
            Sub(ctx As HttpListenerContext)
                ctx.Response.StatusCode = 500
                ctx.Response.Close()
            End Sub)

        Dim translator As New OllamaTranslator(endpoint, "")
        Dim sources As New List(Of String) From {"Hello world"}

        Dim batch As TranslationBatch = Await translator.TranslateAsync(sources, "auto", "ru", CancellationToken.None)

        Assert.NotNull(batch)
        Assert.Equal(TranslationFailure.Other, batch.Failure)
        Assert.Equal(1, batch.FailedCount)
        Assert.False(batch.Translated(0))
    End Function

    <Fact>
    Public Async Function TranslateAsync_WhenGenerateSucceeds_ReturnsTranslatedBatch() As Task
        Dim endpoint As String = StartMockServer(
            Sub(ctx As HttpListenerContext)
                Dim path As String = ctx.Request.Url.AbsolutePath
                If path = "/api/tags" Then
                    Dim json As String = "{""models"":[{""name"":""qwen2.5:3b""}]}"
                    Dim bytes As Byte() = Encoding.UTF8.GetBytes(json)
                    ctx.Response.StatusCode = 200
                    ctx.Response.ContentType = "application/json"
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length)
                    ctx.Response.Close()
                ElseIf path = "/api/generate" Then
                    Dim json As String = "{""response"":""{\""translations\"":[\""Привет мир\""]}""}"
                    Dim bytes As Byte() = Encoding.UTF8.GetBytes(json)
                    ctx.Response.StatusCode = 200
                    ctx.Response.ContentType = "application/json"
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length)
                    ctx.Response.Close()
                Else
                    ctx.Response.StatusCode = 404
                    ctx.Response.Close()
                End If
            End Sub)

        Dim translator As New OllamaTranslator(endpoint, "qwen2.5:3b")
        Dim sources As New List(Of String) From {"Hello world"}

        Dim batch As TranslationBatch = Await translator.TranslateAsync(sources, "auto", "ru", CancellationToken.None)

        Assert.NotNull(batch)
        Assert.Equal(TranslationFailure.None, batch.Failure)
        Assert.Equal(0, batch.FailedCount)
        Assert.Single(batch.Texts)
        Assert.Equal("Привет мир", batch.Texts(0))
        Assert.True(batch.Translated(0))
    End Function

End Class
