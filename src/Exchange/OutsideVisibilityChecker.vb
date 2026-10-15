#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Net.Http
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Verdict of the external connect-back reachability check (Spec 232 rule 5).
''' </summary>
Public Enum OutsideVisibilityVerdict
    Reachable = 1
    NotReachable = 2
    CouldNotCheck = 3
End Enum

''' <summary>
''' Result details of checking outside visibility.
''' </summary>
Public Class OutsideVisibilityResult
    Public Property Verdict As OutsideVisibilityVerdict
    Public Property Host As String = String.Empty
    Public Property Port As Integer = 0
    Public Property Message As String = String.Empty

    Public Sub New(verdict As OutsideVisibilityVerdict, host As String, port As Integer, Optional message As String = "")
        Me.Verdict = verdict
        Me.Host = host
        Me.Port = port
        Me.Message = message
    End Sub
End Class

''' <summary>
''' Test seam for external visibility check.
''' </summary>
Public Interface IOutsideVisibilityProbe
    Function CheckVisibilityAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of OutsideVisibilityVerdict)
End Interface

''' <summary>
''' Spec 232 rule 5 / T-3: "Is my server visible from outside".
''' Contacts check-host.net only on explicit user request to verify whether server's public endpoint can be reached.
''' </summary>
Public Class OutsideVisibilityChecker

    Private ReadOnly _probe As IOutsideVisibilityProbe

    Public Sub New(Optional probe As IOutsideVisibilityProbe = Nothing)
        _probe = If(probe, New DefaultOutsideVisibilityProbe())
    End Sub

    Public Async Function CheckAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of OutsideVisibilityResult)
        If String.IsNullOrWhiteSpace(host) OrElse port <= 0 OrElse port > 65535 Then
            Return New OutsideVisibilityResult(OutsideVisibilityVerdict.CouldNotCheck, host, port, ConnectionDiagnosisLocalization.GetUiText("could_not_check"))
        End If

        Dim v As OutsideVisibilityVerdict = Await _probe.CheckVisibilityAsync(host.Trim(), port, ct).ConfigureAwait(False)
        Dim msgKey As String = "could_not_check"
        Select Case v
            Case OutsideVisibilityVerdict.Reachable
                msgKey = "reachable"
            Case OutsideVisibilityVerdict.NotReachable
                msgKey = "not_reachable"
            Case OutsideVisibilityVerdict.CouldNotCheck
                msgKey = "could_not_check"
        End Select

        Return New OutsideVisibilityResult(v, host, port, ConnectionDiagnosisLocalization.GetUiText(msgKey))
    End Function

End Class

''' <summary>
''' Production probe using check-host.net via HTTP (same service used by share worker).
''' </summary>
Public Class DefaultOutsideVisibilityProbe
    Implements IOutsideVisibilityProbe

    Private Shared ReadOnly HttpClient As New HttpClient() With {
        .Timeout = TimeSpan.FromSeconds(10)
    }

    Private Const CheckHostBase As String = "https://check-host.net"

    Public Async Function CheckVisibilityAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of OutsideVisibilityVerdict) Implements IOutsideVisibilityProbe.CheckVisibilityAsync
        Try
            ' 1. Submit TCP check
            Dim submitUrl As String = $"{CheckHostBase}/check-tcp?host={Uri.EscapeDataString(host)}:{port}&max_nodes=3"
            Using request As New HttpRequestMessage(HttpMethod.Get, submitUrl)
                request.Headers.Add("Accept", "application/json")
                Using response As HttpResponseMessage = Await HttpClient.SendAsync(request, ct).ConfigureAwait(False)
                    If Not response.IsSuccessStatusCode Then Return OutsideVisibilityVerdict.CouldNotCheck
                    Dim body As String = Await response.Content.ReadAsStringAsync(ct).ConfigureAwait(False)
                    Dim json As JsonNode = JsonNode.Parse(body)
                    If json Is Nothing Then Return OutsideVisibilityVerdict.CouldNotCheck
                    Dim reqId As String = json("request_id")?.GetValue(Of String)()
                    If String.IsNullOrWhiteSpace(reqId) Then Return OutsideVisibilityVerdict.CouldNotCheck

                    ' 2. Poll result with deadline
                    Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(12)
                    While DateTime.UtcNow < deadline
                        ct.ThrowIfCancellationRequested()
                        Await Task.Delay(1500, ct).ConfigureAwait(False)

                        Dim resultUrl As String = $"{CheckHostBase}/check-result/{Uri.EscapeDataString(reqId)}"
                        Using resReq As New HttpRequestMessage(HttpMethod.Get, resultUrl)
                            resReq.Headers.Add("Accept", "application/json")
                            Using resResponse As HttpResponseMessage = Await HttpClient.SendAsync(resReq, ct).ConfigureAwait(False)
                                If resResponse.IsSuccessStatusCode Then
                                    Dim resBody As String = Await resResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(False)
                                    Dim pollResult As Nullable(Of Boolean) = EvaluateCheckHostResult(resBody)
                                    If pollResult.HasValue Then
                                        Return If(pollResult.Value, OutsideVisibilityVerdict.Reachable, OutsideVisibilityVerdict.NotReachable)
                                    End If
                                End If
                            End Using
                        End Using
                    End While
                End Using
            End Using
            Return OutsideVisibilityVerdict.CouldNotCheck
        Catch ex As OperationCanceledException
            Return OutsideVisibilityVerdict.CouldNotCheck
        Catch ex As Exception
            Return OutsideVisibilityVerdict.CouldNotCheck
        End Try
    End Function

    Friend Shared Function EvaluateCheckHostResult(jsonString As String) As Nullable(Of Boolean)
        Try
            Dim node As JsonNode = JsonNode.Parse(jsonString)
            If node Is Nothing OrElse TypeOf node IsNot JsonObject Then Return Nothing
            Dim obj As JsonObject = node.AsObject()
            If obj.Count = 0 Then Return Nothing

            Dim allDone As Boolean = True
            Dim anySuccess As Boolean = False

            For Each kvp As KeyValuePair(Of String, JsonNode) In obj
                Dim val As JsonNode = kvp.Value
                If val Is Nothing Then
                    allDone = False
                    Continue For
                End If
                If TypeOf val Is JsonArray Then
                    Dim arr As JsonArray = val.AsArray()
                    For Each el As JsonNode In arr
                        If el Is Nothing Then Continue For
                        If el("time") IsNot Nothing Then
                            anySuccess = True
                        End If
                    Next
                Else
                    allDone = False
                End If
            Next

            If anySuccess Then Return True
            If allDone Then Return False
            Return Nothing
        Catch
            Return Nothing
        End Try
    End Function

End Class
#End If
