Option Strict On

Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Secondary translator: an external or self-hosted LibreTranslate HTTP API.
''' Per the spec the server itself is NOT bundled (AGPL); this is API-only.
''' Contract: POST /translate with q, source, target, format=text.
''' </summary>
Public Class LibreTranslateTranslator
    Implements ITranslator

    Private ReadOnly baseUrl As String
    Private ReadOnly apiKey As String

    Public Sub New(endpoint As String, apiKey As String)
        Me.baseUrl = NormalizeBase(endpoint)
        Me.apiKey = If(apiKey, "")
    End Sub

    Public ReadOnly Property Name As String Implements ITranslator.Name
        Get
            Return "libretranslate"
        End Get
    End Property

    Private Shared Function NormalizeBase(endpoint As String) As String
        Dim e As String = If(endpoint, "").Trim()
        If e.EndsWith("/", StringComparison.Ordinal) Then e = e.Substring(0, e.Length - 1)
        If e.EndsWith("/translate", StringComparison.OrdinalIgnoreCase) Then e = e.Substring(0, e.Length - "/translate".Length)
        Return e
    End Function

    Public Async Function ProbeAsync(ct As CancellationToken) As Task(Of Boolean) Implements ITranslator.ProbeAsync
        If baseUrl.Length = 0 Then Return False
        Try
            Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                linked.CancelAfter(3000)
                Dim resp As HttpResponseMessage = Await TranslateHttp.Client.GetAsync(baseUrl & "/languages", linked.Token).ConfigureAwait(False)
                Return resp.IsSuccessStatusCode
            End Using
        Catch
            Return False
        End Try
    End Function

    Public Async Function TranslateAsync(texts As List(Of String), sourceLang As String, targetLang As String, ct As CancellationToken) As Task(Of TranslationBatch) Implements ITranslator.TranslateAsync
        If texts.Count = 0 Then Return New TranslationBatch()

        ' A target the server has no model for is refused before the request, by name,
        ' instead of an HTTP 400 that used to come back as "the source text, translated"
        ' and be cached as such (audit 051 E-13). An unsupported SOURCE only loses the hint.
        Dim target As String = TranslateLang.LibreTranslateCode(targetLang)
        If target.Length = 0 Then Return TranslationBatch.AllFailed(texts, TranslationFailure.UnsupportedLanguage)
        Dim source As String = TranslateLang.LibreTranslateCode(sourceLang)
        If source.Length = 0 Then source = "auto"

        Try
            Dim body As New Dictionary(Of String, Object) From {
                {"q", texts.ToArray()},
                {"source", source},
                {"target", target},
                {"format", "text"}
            }
            If apiKey.Length > 0 Then body("api_key") = apiKey

            Dim payload As String = TranslateHttp.JsonSerialize(body)
            Using content As New StringContent(payload, Encoding.UTF8, "application/json")
                Dim resp As HttpResponseMessage = Await TranslateHttp.Client.PostAsync(baseUrl & "/translate", content, ct).ConfigureAwait(False)
                If Not resp.IsSuccessStatusCode Then Return TranslationBatch.AllFailed(texts, TranslationFailure.Other)
                Dim json As String = Await resp.Content.ReadAsStringAsync().ConfigureAwait(False)
                Return ParseTranslated(json, texts)
            End Using
        Catch ex As OperationCanceledException When ct.IsCancellationRequested
            Throw
        Catch ex As OperationCanceledException
            ' The HttpClient timeout, not navigation (audit 051 E-04).
            Return TranslationBatch.AllFailed(texts, TranslationFailure.Timeout)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " libretranslate: error: " & ex.Message)
            Return TranslationBatch.AllFailed(texts, TranslationFailure.Other)
        End Try
    End Function

    Private Shared Function ParseTranslated(json As String, sources As List(Of String)) As TranslationBatch
        Dim root As Dictionary(Of String, Object) = TryCast(TranslateHttp.JsonDeserializeObject(json), Dictionary(Of String, Object))
        If root Is Nothing Then Return TranslationBatch.AllFailed(sources, TranslationFailure.Other)

        Dim tObj As Object = Nothing
        If Not root.TryGetValue("translatedText", tObj) Then Return TranslationBatch.AllFailed(sources, TranslationFailure.Other)

        Dim answers As New List(Of String)
        Dim arr As Object() = TryCast(tObj, Object())
        If arr IsNot Nothing Then
            For Each item As Object In arr
                answers.Add(TranslateHttp.JsonItemToString(item))
            Next
        Else
            ' Single-string response form.
            answers.Add(TranslateHttp.JsonItemToString(tObj))
        End If

        ' One entry per source, whatever the server sent: a missing or blank answer is that
        ' segment's failure, not a shorter list for the pipeline to guess about.
        Dim result As New TranslationBatch With {.ModelUsed = "libretranslate"}
        For i As Integer = 0 To sources.Count - 1
            Dim answer As String = If(i < answers.Count, answers(i), "")
            If String.IsNullOrWhiteSpace(answer) Then
                result.Add(sources(i), False)
            Else
                result.Add(answer, True)
            End If
        Next
        Return result
    End Function

End Class
