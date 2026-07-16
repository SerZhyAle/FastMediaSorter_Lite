Option Strict On

Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
#If NETFRAMEWORK Then
Imports System.Web.Script.Serialization
#End If

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

#If NETFRAMEWORK Then
    Private Function NewSerializer() As JavaScriptSerializer
        Dim s As New JavaScriptSerializer()
        s.MaxJsonLength = Integer.MaxValue
        Return s
    End Function
#Else
    ' Case-insensitive binding mirrors JavaScriptSerializer, so OCR disk-cache
    ' files written by the net48 build load unchanged after an upgrade. The relaxed
    ' encoder keeps non-ASCII text raw like JavaScriptSerializer did - the batch
    ' JSON is embedded into the LLM prompt, and \u-escaped Cyrillic would degrade
    ' translation quality (bodies go to localhost over HTTP; no HTML context).
    Private ReadOnly stj_Options As New System.Text.Json.JsonSerializerOptions With {
        .PropertyNameCaseInsensitive = True,
        .Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    }
#End If

    ''' <summary>
    ''' Portable JSON facade (SPECIFICATION_DOTNET10_MODERN_BUILD §3 seams):
    ''' net48 keeps JavaScriptSerializer, the .NET 10 build uses System.Text.Json
    ''' shaped to the SAME untyped graph - objects become
    ''' Dictionary(Of String, Object), arrays become Object(), numbers become
    ''' Integer/Long/Double - so every consumer keeps its TryCast logic.
    ''' </summary>
    Public Function JsonSerialize(value As Object) As String
#If NETFRAMEWORK Then
        Return NewSerializer().Serialize(value)
#Else
        Return System.Text.Json.JsonSerializer.Serialize(value, stj_Options)
#End If
    End Function

    Public Function JsonDeserialize(Of T)(json As String) As T
#If NETFRAMEWORK Then
        Return NewSerializer().Deserialize(Of T)(json)
#Else
        Return System.Text.Json.JsonSerializer.Deserialize(Of T)(json, stj_Options)
#End If
    End Function

    Public Function JsonDeserializeObject(json As String) As Object
#If NETFRAMEWORK Then
        Return NewSerializer().DeserializeObject(json)
#Else
        ' JavaScriptSerializer returns Nothing for empty input (callers rely on it
        ' to degrade gracefully, e.g. an empty 200 body from Ollama); malformed
        ' JSON throws in both builds and is caught by the same call-site handlers.
        If String.IsNullOrWhiteSpace(json) Then Return Nothing
        Using doc As System.Text.Json.JsonDocument = System.Text.Json.JsonDocument.Parse(json)
            Return JsonElementToClr(doc.RootElement)
        End Using
#End If
    End Function

#If Not NETFRAMEWORK Then
    Private Function JsonElementToClr(element As System.Text.Json.JsonElement) As Object
        Select Case element.ValueKind
            Case System.Text.Json.JsonValueKind.Object
                Dim map As New Dictionary(Of String, Object)()
                For Each prop As System.Text.Json.JsonProperty In element.EnumerateObject()
                    map(prop.Name) = JsonElementToClr(prop.Value)
                Next
                Return map
            Case System.Text.Json.JsonValueKind.Array
                Dim items As New List(Of Object)()
                For Each child As System.Text.Json.JsonElement In element.EnumerateArray()
                    items.Add(JsonElementToClr(child))
                Next
                Return items.ToArray()
            Case System.Text.Json.JsonValueKind.String
                Return element.GetString()
            Case System.Text.Json.JsonValueKind.Number
                Dim asLong As Long
                If element.TryGetInt64(asLong) Then
                    If asLong >= Integer.MinValue AndAlso asLong <= Integer.MaxValue Then Return CInt(asLong)
                    Return asLong
                End If
                Return element.GetDouble()
            Case System.Text.Json.JsonValueKind.True
                Return True
            Case System.Text.Json.JsonValueKind.False
                Return False
            Case Else
                Return Nothing
        End Select
    End Function
#End If

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

    Private Const SystemPrompt As String =
        "You are a precise translation engine. Output text ONLY in the requested target language, " &
        "using that language's native script. Never switch to another language and never use Chinese " &
        "characters unless Chinese is explicitly the requested target."

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
            ' Repair echoed, wrong-script, refusal or noisy segments individually.
            For i As Integer = 0 To texts.Count - 1
                Dim reason As String = GetBadTranslationReason(texts(i), batched(i), targetLang)
                If reason.Length > 0 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") &
                                    " ollama: retry segment " & i.ToString() &
                                    " because " & reason &
                                    " src=" & CountUsefulCharacters(texts(i)).ToString() &
                                    " dst=" & CountUsefulCharacters(batched(i)).ToString())
                    batched(i) = Await TranslateOneAsync(texts(i), langName, targetLang, ct).ConfigureAwait(False)
                End If
            Next
            Return batched
        End If

        ' 2) Fall back to per-segment translation.
        Dim outList As New List(Of String)(texts.Count)
        For i As Integer = 0 To texts.Count - 1
            ct.ThrowIfCancellationRequested()
            outList.Add(Await TranslateOneAsync(texts(i), langName, targetLang, ct).ConfigureAwait(False))
        Next
        Return outList
    End Function

    Private Async Function TryTranslateBatchAsync(texts As List(Of String), langName As String, ct As CancellationToken) As Task(Of List(Of String))
        Try
            Dim inputJson As String = TranslateHttp.JsonSerialize(texts)
            Dim prompt As String =
                "Translate each FULL input segment into " & langName & "." & vbLf &
                "Translate the complete content faithfully. Do not summarize, shorten, or omit any sentence, clause, or detail." & vbLf &
                "Preserve sentence boundaries and paragraph structure when possible." & vbLf &
                "Write every translation ENTIRELY in " & langName & " using its native alphabet. " &
                "Do NOT use Chinese characters or any language other than " & langName & "." & vbLf &
                "Keep it natural and concise; no notes, no quotation marks." & vbLf &
                "Return ONLY a JSON object of the form {""translations"":[...]} containing exactly " &
                texts.Count.ToString() & " strings, in the same order as the input." & vbLf &
                "Input segments: " & inputJson

            Dim response As String = Await GenerateAsync(prompt, True, ct).ConfigureAwait(False)
            If String.IsNullOrWhiteSpace(response) Then Return Nothing

            Dim root As Dictionary(Of String, Object) = TryCast(TranslateHttp.JsonDeserializeObject(response), Dictionary(Of String, Object))
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

    Private Async Function TranslateOneAsync(text As String, langName As String, targetLang As String, ct As CancellationToken) As Task(Of String)
        If String.IsNullOrWhiteSpace(text) Then Return text

        Dim prompt As String =
            "Translate the FULL following text into " & langName & "." & vbLf &
            "Translate every sentence and detail faithfully; do not summarize, shorten, or omit anything." & vbLf &
            "Write the translation ENTIRELY in " & langName & " using its native alphabet; do NOT use " &
            "Chinese characters or any other language. Output only the translation, no notes or quotes." & vbLf &
            "Text: " & text

        Dim first As String = Await GenerateAsync(prompt, False, ct).ConfigureAwait(False)
        first = If(first, "").Trim()

        Dim firstReason As String = GetBadTranslationReason(text, first, targetLang)
        Dim firstBad As Boolean = first.Length = 0 OrElse firstReason.Length > 0
        If firstBad Then
            Dim retryPrompt As String =
                "Translate this ENTIRE text into " & langName & " ONLY. Translate every sentence and detail; do not summarize, shorten, or omit anything. " &
                "Preserve the full meaning. The output must be written entirely in " &
                langName & "'s native alphabet. Absolutely no Chinese characters and no other language. " &
                "Produce a genuine translation, not a copy. Output only the translation." & vbLf &
                "Text: " & text
            Dim second As String = Await GenerateAsync(retryPrompt, False, ct).ConfigureAwait(False)
            second = If(second, "").Trim()
            Dim secondReason As String = GetBadTranslationReason(text, second, targetLang)
            If second.Length > 0 AndAlso secondReason.Length = 0 Then Return second
            ' Prefer a usable first result over a bad retry.
            If first.Length > 0 AndAlso firstReason.Length = 0 Then Return first
            If second.Length > 0 AndAlso secondReason.Length = 0 Then Return second
            If firstReason.Length > 0 Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") &
                                " ollama: rejected first translation because " & firstReason &
                                " src=" & CountUsefulCharacters(text).ToString() &
                                " dst=" & CountUsefulCharacters(first).ToString())
            End If
            If secondReason.Length > 0 Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") &
                                " ollama: rejected retry translation because " & secondReason &
                                " src=" & CountUsefulCharacters(text).ToString() &
                                " dst=" & CountUsefulCharacters(second).ToString())
            End If
            ' Everything drifted to the wrong script -> show the source, not gibberish.
            Return text
        End If

        Return first
    End Function

    ''' <summary>POST /api/generate (stream=false); returns the "response" field.</summary>
    Private Async Function GenerateAsync(prompt As String, asJson As Boolean, ct As CancellationToken) As Task(Of String)
        Dim body As New Dictionary(Of String, Object) From {
            {"model", model},
            {"prompt", prompt},
            {"system", SystemPrompt},
            {"stream", False},
            {"keep_alive", "5m"},
            {"options", New Dictionary(Of String, Object) From {{"temperature", 0}}}
        }
        If asJson Then body("format") = "json"

        Dim payload As String = TranslateHttp.JsonSerialize(body)
        Using content As New StringContent(payload, Encoding.UTF8, "application/json")
            Dim resp As HttpResponseMessage = Await TranslateHttp.Client.PostAsync(baseUrl & "/api/generate", content, ct).ConfigureAwait(False)
            If Not resp.IsSuccessStatusCode Then Return ""
            Dim json As String = Await resp.Content.ReadAsStringAsync().ConfigureAwait(False)
            Dim root As Dictionary(Of String, Object) = TryCast(TranslateHttp.JsonDeserializeObject(json), Dictionary(Of String, Object))
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
            Using linked As CancellationTokenSource = LinkedTimeout(ct, 4000)
                Dim resp As HttpResponseMessage = Await TranslateHttp.Client.GetAsync(baseUrl & "/api/tags", linked.Token).ConfigureAwait(False)
                If Not resp.IsSuccessStatusCode Then Return names
                Dim json As String = Await resp.Content.ReadAsStringAsync().ConfigureAwait(False)
                Dim root As Dictionary(Of String, Object) = TryCast(TranslateHttp.JsonDeserializeObject(json), Dictionary(Of String, Object))
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
        Dim body As String = TranslateHttp.JsonSerialize(New Dictionary(Of String, Object) From {
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
                                        Dim obj As Dictionary(Of String, Object) = TryCast(TranslateHttp.JsonDeserializeObject(line), Dictionary(Of String, Object))
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

    Private Shared Function IsBadTranslation(source As String, candidate As String, targetLang As String) As Boolean
        Return GetBadTranslationReason(source, candidate, targetLang).Length > 0
    End Function

    Private Shared Function GetBadTranslationReason(source As String, candidate As String, targetLang As String) As String
        If String.IsNullOrWhiteSpace(candidate) Then Return "empty"
        If LooksLikeEcho(source, candidate) Then Return "echo"
        If LooksWrongScript(candidate, targetLang) Then Return "wrong-script"
        If LooksLikeRefusal(candidate) Then Return "refusal"
        If LooksNoisy(candidate) Then Return "noisy"
        If LooksTruncated(source, candidate, targetLang) Then Return "truncated"
        Return ""
    End Function

    Private Shared Function LooksWrongScript(text As String, targetLang As String) As Boolean
        If String.IsNullOrWhiteSpace(text) Then Return False
        Dim expected As TextScriptFamily = ExpectedScriptForLanguage(targetLang)
        If expected = TextScriptFamily.Unknown Then Return False

        Dim dominant As TextScriptFamily = TextScriptFamily.Unknown
        Dim share As Double = 0.0
        Dim letters As Integer = 0
        Dim mixedLetters As Integer = 0
        If Not TryGetDominantScript(text, dominant, share, letters, mixedLetters) Then Return False

        If expected = TextScriptFamily.Cjk Then
            Return dominant <> TextScriptFamily.Cjk AndAlso letters >= 2 AndAlso share >= 0.6
        End If

        If dominant = TextScriptFamily.Cjk AndAlso letters >= 2 AndAlso share >= 0.15 Then Return True
        Return letters >= 4 AndAlso dominant <> TextScriptFamily.Unknown AndAlso dominant <> expected AndAlso share >= 0.6
    End Function

    Private Shared Function LooksLikeEcho(source As String, candidate As String) As Boolean
        If String.IsNullOrWhiteSpace(candidate) Then Return False
        Dim a As String = source.Trim()
        Dim b As String = candidate.Trim()
        If a.Length < 3 Then Return False
        Return String.Equals(a, b, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function LooksLikeRefusal(text As String) As Boolean
        Dim s As String = If(text, "").Trim().ToLowerInvariant()
        If s.Length = 0 Then Return False

        Dim phrases As String() = {
            "i cannot", "i can't", "sorry", "unable to", "cannot fulfill", "can't fulfill",
            "please make sure", "unrecognized symbols", "use the correct alphabet",
            "translation request", "output only", "json object",
            "не могу", "не могу выполнить", "не распознаю", "пожалуйста", "алфавит", "язык для перевода"
        }

        For Each phrase As String In phrases
            If s.IndexOf(phrase, StringComparison.Ordinal) >= 0 Then Return True
        Next

        Return False
    End Function

    Private Shared Function LooksNoisy(text As String) As Boolean
        Dim symbolNoise As Double = SymbolRatio(text)
        If text.Length >= 10 AndAlso symbolNoise >= 0.22 Then Return True

        Dim digits As Integer = 0
        Dim nonWhitespace As Integer = 0
        For Each ch As Char In text
            If Char.IsWhiteSpace(ch) Then Continue For
            nonWhitespace += 1
            If Char.IsDigit(ch) Then digits += 1
        Next
        If nonWhitespace >= 10 AndAlso digits / CDbl(nonWhitespace) >= 0.35 Then Return True

        Dim dominant As TextScriptFamily = TextScriptFamily.Unknown
        Dim share As Double = 0.0
        Dim letters As Integer = 0
        Dim mixedLetters As Integer = 0
        If TryGetDominantScript(text, dominant, share, letters, mixedLetters) AndAlso
           letters >= 8 AndAlso share < 0.55 Then
            Return True
        End If

        Return False
    End Function

    Private Shared Function LooksTruncated(source As String, candidate As String, targetLang As String) As Boolean
        Dim sourceUseful As Integer = CountUsefulCharacters(source)
        If sourceUseful < 45 Then Return False

        Dim candidateUseful As Integer = CountUsefulCharacters(candidate)
        Dim minRatio As Double = If(ExpectedScriptForLanguage(targetLang) = TextScriptFamily.Cjk, 0.22, 0.42)

        If sourceUseful >= 120 AndAlso candidateUseful < CInt(Math.Floor(sourceUseful * 0.35)) Then
            Return True
        End If

        Dim sourceSentences As Integer = CountSentenceMarkers(source)
        Dim candidateSentences As Integer = CountSentenceMarkers(candidate)

        If sourceSentences >= 2 AndAlso
           candidateSentences + 1 < sourceSentences AndAlso
           candidateUseful < CInt(Math.Floor(sourceUseful * 0.7)) Then
            Return True
        End If

        If (sourceSentences >= 2 OrElse sourceUseful >= 90) AndAlso
           candidateUseful < Math.Max(18, CInt(Math.Floor(sourceUseful * minRatio))) Then
            Return True
        End If

        Return False
    End Function

    Private Shared Function CountUsefulCharacters(text As String) As Integer
        Dim count As Integer = 0
        For Each ch As Char In If(text, "")
            If Char.IsLetterOrDigit(ch) Then count += 1
        Next
        Return count
    End Function

    Private Shared Function CountSentenceMarkers(text As String) As Integer
        Dim count As Integer = 0
        For Each ch As Char In If(text, "")
            If ch = "."c OrElse ch = "!"c OrElse ch = "?"c Then count += 1
        Next
        Return count
    End Function

    Private Shared Function LinkedTimeout(ct As CancellationToken, ms As Integer) As CancellationTokenSource
        Dim cts As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
        cts.CancelAfter(ms)
        Return cts
    End Function

End Class
