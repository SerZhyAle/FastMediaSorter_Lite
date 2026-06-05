Option Strict On

Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Web.Script.Serialization

''' <summary>Shared HttpClient for the translation backends (one socket pool).</summary>
Friend Module TranslateHttp
    Private ReadOnly _client As New Lazy(Of HttpClient)(
        Function()
            Dim c As New HttpClient()
            ' LLMs can be slow; navigation cancellation is what actually aborts a
            ' stale request, so the hard timeout is just a backstop.
            c.Timeout = TimeSpan.FromSeconds(120)
            Return c
        End Function)

    Public ReadOnly Property Client As HttpClient
        Get
            Return _client.Value
        End Get
    End Property

    Public Function NewSerializer() As JavaScriptSerializer
        Dim s As New JavaScriptSerializer()
        s.MaxJsonLength = Integer.MaxValue
        Return s
    End Function

    ''' <summary>
    ''' Coerces one element of a translated-text array to a string. Backends are asked
    ''' for plain strings but sometimes wrap each as an object (e.g. {"translation":"..."},
    ''' {"text":"..."}). Convert.ToString on such a Dictionary yields the type name
    ''' "System.Collections.Generic.Dictionary`2[...]", which then surfaced as the overlay
    ''' text -- so dig the real string out of the common shapes instead.
    ''' </summary>
    Public Function JsonItemToString(item As Object) As String
        If item Is Nothing Then Return ""
        If TypeOf item Is String Then Return DirectCast(item, String)

        Dim dict As Dictionary(Of String, Object) = TryCast(item, Dictionary(Of String, Object))
        If dict IsNot Nothing Then
            For Each preferredKey As String In New String() {"translation", "translatedText", "translated", "text", "value", "output", "result"}
                Dim v As Object = Nothing
                If dict.TryGetValue(preferredKey, v) AndAlso TypeOf v Is String Then Return DirectCast(v, String)
            Next
            ' Fallback: first string value in the object (covers {"0":"..."} etc.).
            For Each kv As KeyValuePair(Of String, Object) In dict
                If TypeOf kv.Value Is String Then Return DirectCast(kv.Value, String)
            Next
            Return ""
        End If

        Dim arr As Object() = TryCast(item, Object())
        If arr IsNot Nothing Then Return If(arr.Length > 0, JsonItemToString(arr(0)), "")

        Return Convert.ToString(item)
    End Function
End Module

''' <summary>
''' Default translator: a local Ollama server (http://localhost:11434).
''' Probes availability, auto-selects an installed model when none is configured,
''' batches all blocks into one request, and retries once per segment when the
''' model echoes the source instead of translating.
''' </summary>
Public Class OllamaTranslator
    Implements ITranslator

    Private ReadOnly baseUrl As String
    Private model As String

    Public Sub New(endpoint As String, model As String)
        Me.baseUrl = NormalizeBase(endpoint)
        Me.model = If(model, "").Trim()
    End Sub

    Public ReadOnly Property Name As String Implements ITranslator.Name
        Get
            Return "ollama"
        End Get
    End Property

    Private Shared Function NormalizeBase(endpoint As String) As String
        Dim e As String = If(endpoint, "").Trim()
        If e.Length = 0 Then e = "http://localhost:11434"
        If e.EndsWith("/", StringComparison.Ordinal) Then e = e.Substring(0, e.Length - 1)
        ' Allow the user to enter either the host or the /api root.
        If e.EndsWith("/api", StringComparison.OrdinalIgnoreCase) Then e = e.Substring(0, e.Length - 4)
        Return e
    End Function

    Public Async Function ProbeAsync(ct As CancellationToken) As Task(Of Boolean) Implements ITranslator.ProbeAsync
        Try
            Using linked As CancellationTokenSource = LinkedTimeout(ct, 2500)
                Dim resp As HttpResponseMessage = Await TranslateHttp.Client.GetAsync(baseUrl & "/api/tags", linked.Token).ConfigureAwait(False)
                Return resp.IsSuccessStatusCode
            End Using
        Catch
            Return False
        End Try
    End Function

    Public Async Function TranslateAsync(texts As List(Of String), sourceLang As String, targetLang As String, ct As CancellationToken) As Task(Of List(Of String)) Implements ITranslator.TranslateAsync
        Dim results As New List(Of String)(texts)

        If texts.Count = 0 Then Return results

        If Not Await EnsureModelAsync(ct).ConfigureAwait(False) Then
            Return New List(Of String)(texts) ' no model -> echo source
        End If

        Dim langName As String = TranslateLang.DisplayName(targetLang)

        ' 1) Try a single batched request.
        Dim batched As List(Of String) = Await TryTranslateBatchAsync(texts, langName, ct).ConfigureAwait(False)
        If batched IsNot Nothing AndAlso batched.Count = texts.Count Then
            ' Repair any echoed segments individually.
            For i As Integer = 0 To texts.Count - 1
                If LooksLikeEcho(texts(i), batched(i)) Then
                    batched(i) = Await TranslateOneAsync(texts(i), langName, ct).ConfigureAwait(False)
                End If
            Next
            Return batched
        End If

        ' 2) Fall back to per-segment translation.
        Dim outList As New List(Of String)(texts.Count)
        For i As Integer = 0 To texts.Count - 1
            ct.ThrowIfCancellationRequested()
            outList.Add(Await TranslateOneAsync(texts(i), langName, ct).ConfigureAwait(False))
        Next
        Return outList
    End Function

    Private Async Function TryTranslateBatchAsync(texts As List(Of String), langName As String, ct As CancellationToken) As Task(Of List(Of String))
        Try
            Dim serializer As JavaScriptSerializer = TranslateHttp.NewSerializer()
            Dim inputJson As String = serializer.Serialize(texts)
            Dim prompt As String =
                "You are a professional translator. Translate each input segment into " & langName & "." & vbLf &
                "Keep it natural and concise. Do not add notes, explanations or quotation marks." & vbLf &
                "Return ONLY a JSON object of the form {""translations"":[...]} containing exactly " &
                texts.Count.ToString() & " strings, in the same order as the input." & vbLf &
                "Input segments: " & inputJson

            Dim response As String = Await GenerateAsync(prompt, True, ct).ConfigureAwait(False)
            If String.IsNullOrWhiteSpace(response) Then Return Nothing

            Dim root As Dictionary(Of String, Object) = TryCast(serializer.DeserializeObject(response), Dictionary(Of String, Object))
            If root Is Nothing Then Return Nothing

            Dim tObj As Object = Nothing
            If Not root.TryGetValue("translations", tObj) Then Return Nothing
            Dim arr As Object() = TryCast(tObj, Object())
            If arr Is Nothing Then Return Nothing

            Dim outList As New List(Of String)(arr.Length)
            For Each item As Object In arr
                outList.Add(TranslateHttp.JsonItemToString(item).Trim())
            Next
            Return outList
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ollama: batch failed: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Async Function TranslateOneAsync(text As String, langName As String, ct As CancellationToken) As Task(Of String)
        If String.IsNullOrWhiteSpace(text) Then Return text

        Dim prompt As String =
            "Translate the following text into " & langName & ". Output only the translation, with no notes or quotes." & vbLf &
            "Text: " & text

        Dim first As String = Await GenerateAsync(prompt, False, ct).ConfigureAwait(False)
        first = If(first, "").Trim()

        If first.Length = 0 OrElse LooksLikeEcho(text, first) Then
            Dim retryPrompt As String =
                "Translate this text into " & langName & ". It is currently in another language; produce a genuine " &
                langName & " translation, not a copy. Output only the translation." & vbLf &
                "Text: " & text
            Dim second As String = Await GenerateAsync(retryPrompt, False, ct).ConfigureAwait(False)
            second = If(second, "").Trim()
            If second.Length > 0 Then Return second
        End If

        Return If(first.Length > 0, first, text)
    End Function

    ''' <summary>POST /api/generate (stream=false); returns the "response" field.</summary>
    Private Async Function GenerateAsync(prompt As String, asJson As Boolean, ct As CancellationToken) As Task(Of String)
        Dim serializer As JavaScriptSerializer = TranslateHttp.NewSerializer()
        Dim body As New Dictionary(Of String, Object) From {
            {"model", model},
            {"prompt", prompt},
            {"stream", False},
            {"keep_alive", "5m"},
            {"options", New Dictionary(Of String, Object) From {{"temperature", 0}}}
        }
        If asJson Then body("format") = "json"

        Dim payload As String = serializer.Serialize(body)
        Using content As New StringContent(payload, Encoding.UTF8, "application/json")
            Dim resp As HttpResponseMessage = Await TranslateHttp.Client.PostAsync(baseUrl & "/api/generate", content, ct).ConfigureAwait(False)
            If Not resp.IsSuccessStatusCode Then Return ""
            Dim json As String = Await resp.Content.ReadAsStringAsync().ConfigureAwait(False)
            Dim root As Dictionary(Of String, Object) = TryCast(serializer.DeserializeObject(json), Dictionary(Of String, Object))
            If root Is Nothing Then Return ""
            Dim r As Object = Nothing
            root.TryGetValue("response", r)
            Return Convert.ToString(r)
        End Using
    End Function

    Private Async Function EnsureModelAsync(ct As CancellationToken) As Task(Of Boolean)
        Dim installed As List(Of String) = Await ListModelsAsync(ct).ConfigureAwait(False)

        ' Can't enumerate (server down/empty): optimistically keep an explicit model.
        If installed.Count = 0 Then Return model.Length > 0

        ' Use the configured model if it's actually installed.
        If model.Length > 0 AndAlso installed.Any(Function(n) ModelMatches(n, model)) Then Return True

        ' Configured model missing (or none set) -> fall back to an installed one.
        model = PickPreferredModel(installed)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ollama: selected model " & model)
        Return True
    End Function

    ''' <summary>True if an installed model name matches the wanted name/tag.</summary>
    Private Shared Function ModelMatches(installedName As String, wanted As String) As Boolean
        If String.Equals(installedName, wanted, StringComparison.OrdinalIgnoreCase) Then Return True
        ' "qwen2.5" should match installed "qwen2.5:latest" / "qwen2.5:3b".
        If Not wanted.Contains(":") AndAlso installedName.StartsWith(wanted & ":", StringComparison.OrdinalIgnoreCase) Then Return True
        ' "qwen2.5:latest" should match installed "qwen2.5".
        If wanted.EndsWith(":latest", StringComparison.OrdinalIgnoreCase) AndAlso
           String.Equals(installedName, wanted.Substring(0, wanted.Length - ":latest".Length), StringComparison.OrdinalIgnoreCase) Then Return True
        Return False
    End Function

    ''' <summary>Names of the models currently installed in the Ollama server.</summary>
    Public Async Function ListModelsAsync(ct As CancellationToken) As Task(Of List(Of String))
        Dim names As New List(Of String)
        Try
            Dim serializer As JavaScriptSerializer = TranslateHttp.NewSerializer()
            Using linked As CancellationTokenSource = LinkedTimeout(ct, 4000)
                Dim resp As HttpResponseMessage = Await TranslateHttp.Client.GetAsync(baseUrl & "/api/tags", linked.Token).ConfigureAwait(False)
                If Not resp.IsSuccessStatusCode Then Return names
                Dim json As String = Await resp.Content.ReadAsStringAsync().ConfigureAwait(False)
                Dim root As Dictionary(Of String, Object) = TryCast(serializer.DeserializeObject(json), Dictionary(Of String, Object))
                If root Is Nothing Then Return names
                Dim modelsObj As Object = Nothing
                root.TryGetValue("models", modelsObj)
                Dim arr As Object() = TryCast(modelsObj, Object())
                If arr Is Nothing Then Return names
                For Each m As Object In arr
                    Dim md As Dictionary(Of String, Object) = TryCast(m, Dictionary(Of String, Object))
                    If md IsNot Nothing Then
                        Dim nm As Object = Nothing
                        md.TryGetValue("name", nm)
                        Dim s As String = Convert.ToString(nm)
                        If Not String.IsNullOrWhiteSpace(s) Then names.Add(s)
                    End If
                Next
            End Using
        Catch
        End Try
        Return names
    End Function

    ''' <summary>
    ''' Pulls (installs) a model into Ollama via POST /api/pull, streaming
    ''' progress lines to <paramref name="progress"/>. Uses a dedicated client
    ''' with no timeout (pulls can take minutes); cancellation honours the token.
    ''' </summary>
    Public Async Function PullModelAsync(modelName As String, progress As IProgress(Of String), ct As CancellationToken) As Task(Of Boolean)
        If String.IsNullOrWhiteSpace(modelName) Then Return False
        Dim serializer As JavaScriptSerializer = TranslateHttp.NewSerializer()
        Dim body As String = serializer.Serialize(New Dictionary(Of String, Object) From {
            {"name", modelName.Trim()}, {"stream", True}})

        Try
            Using client As New HttpClient()
                client.Timeout = Timeout.InfiniteTimeSpan
                Using content As New StringContent(body, Encoding.UTF8, "application/json")
                    Using req As New HttpRequestMessage(HttpMethod.Post, baseUrl & "/api/pull") With {.Content = content}
                        Using resp As HttpResponseMessage = Await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(False)
                            If Not resp.IsSuccessStatusCode Then Return False
                            Using stream As Stream = Await resp.Content.ReadAsStreamAsync().ConfigureAwait(False)
                                Using reader As New StreamReader(stream)
                                    Dim success As Boolean = False
                                    While Not reader.EndOfStream
                                        ct.ThrowIfCancellationRequested()
                                        Dim line As String = Await reader.ReadLineAsync().ConfigureAwait(False)
                                        If String.IsNullOrWhiteSpace(line) Then Continue While
                                        Dim obj As Dictionary(Of String, Object) = TryCast(serializer.DeserializeObject(line), Dictionary(Of String, Object))
                                        If obj Is Nothing Then Continue While

                                        Dim st As Object = Nothing
                                        obj.TryGetValue("status", st)
                                        Dim statusText As String = Convert.ToString(st)

                                        Dim completedO As Object = Nothing, totalO As Object = Nothing
                                        obj.TryGetValue("completed", completedO)
                                        obj.TryGetValue("total", totalO)
                                        Dim pct As String = ""
                                        If completedO IsNot Nothing AndAlso totalO IsNot Nothing Then
                                            Dim t As Double = Convert.ToDouble(totalO)
                                            If t > 0 Then pct = " " & CInt(Convert.ToDouble(completedO) / t * 100).ToString() & "%"
                                        End If

                                        Dim errObj As Object = Nothing
                                        If obj.TryGetValue("error", errObj) Then
                                            If progress IsNot Nothing Then progress.Report("error: " & Convert.ToString(errObj))
                                            Return False
                                        End If

                                        If progress IsNot Nothing AndAlso Not String.IsNullOrEmpty(statusText) Then progress.Report(statusText & pct)
                                        If String.Equals(statusText, "success", StringComparison.OrdinalIgnoreCase) Then success = True
                                    End While
                                    Return success
                                End Using
                            End Using
                        End Using
                    End Using
                End Using
            End Using
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ollama: pull failed: " & ex.Message)
            If progress IsNot Nothing Then progress.Report("error: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function PickPreferredModel(names As List(Of String)) As String
        ' Prefer models that tend to translate well; otherwise take the first.
        Dim preferred As String() = {"aya", "qwen", "gemma", "mistral", "llama", "phi"}
        For Each p As String In preferred
            For Each n As String In names
                If n.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0 Then Return n
            Next
        Next
        Return names(0)
    End Function

    Private Shared Function LooksLikeEcho(source As String, candidate As String) As Boolean
        If String.IsNullOrWhiteSpace(candidate) Then Return False
        Dim a As String = source.Trim()
        Dim b As String = candidate.Trim()
        If a.Length < 3 Then Return False
        Return String.Equals(a, b, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function LinkedTimeout(ct As CancellationToken, ms As Integer) As CancellationTokenSource
        Dim cts As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
        cts.CancelAfter(ms)
        Return cts
    End Function

End Class
