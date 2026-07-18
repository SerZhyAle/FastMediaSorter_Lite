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

    Public Async Function TranslateAsync(texts As List(Of String), sourceLang As String, targetLang As String, ct As CancellationToken) As Task(Of List(Of String)) Implements ITranslator.TranslateAsync
        If texts.Count = 0 Then Return New List(Of String)()
        Try
            Dim body As New Dictionary(Of String, Object) From {
                {"q", texts.ToArray()},
                {"source", NormalizeLang(sourceLang)},
                {"target", NormalizeLang(targetLang)},
                {"format", "text"}
            }
            If apiKey.Length > 0 Then body("api_key") = apiKey

            Dim payload As String = TranslateHttp.JsonSerialize(body)
            Using content As New StringContent(payload, Encoding.UTF8, "application/json")
                Dim resp As HttpResponseMessage = Await TranslateHttp.Client.PostAsync(baseUrl & "/translate", content, ct).ConfigureAwait(False)
                If Not resp.IsSuccessStatusCode Then Return New List(Of String)(texts)
                Dim json As String = Await resp.Content.ReadAsStringAsync().ConfigureAwait(False)
                Return ParseTranslated(json, texts)
            End Using
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " libretranslate: error: " & ex.Message)
            Return New List(Of String)(texts)
        End Try
    End Function

    Private Shared Function ParseTranslated(json As String, fallback As List(Of String)) As List(Of String)
        Dim root As Dictionary(Of String, Object) = TryCast(TranslateHttp.JsonDeserializeObject(json), Dictionary(Of String, Object))
        If root Is Nothing Then Return New List(Of String)(fallback)

        Dim tObj As Object = Nothing
        If Not root.TryGetValue("translatedText", tObj) Then Return New List(Of String)(fallback)

        Dim arr As Object() = TryCast(tObj, Object())
        If arr IsNot Nothing Then
            Dim outList As New List(Of String)(arr.Length)
            For Each item As Object In arr
                outList.Add(TranslateHttp.JsonItemToString(item))
            Next
            Return outList
        End If

        ' Single-string response form.
        Return New List(Of String) From {TranslateHttp.JsonItemToString(tObj)}
    End Function

    Private Shared Function NormalizeLang(code As String) As String
        Dim c As String = If(code, "").Trim().ToLowerInvariant()
        Select Case c
            Case "", "auto" : Return "auto"
            Case "eng" : Return "en"
            Case "rus" : Return "ru"
            Case "ukr" : Return "uk"
            Case "deu", "ger" : Return "de"
            Case "fra", "fre" : Return "fr"
            Case "spa" : Return "es"
            Case "ita" : Return "it"
            Case "jpn" : Return "ja"
            Case "kor" : Return "ko"
            Case "chi_sim", "chi-sim" : Return "zh"
            Case Else : Return c
        End Select
    End Function

End Class
