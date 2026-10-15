Option Strict On

Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading.Tasks

' The premise experiment of DEVICE-EXCHANGE 8.2 (specification 203 step 1; done\202 sections 3-5, steps T-2/T-3).
'
' The Drive channel is cross-product only if the appDataFolder space is scoped per Google Cloud PROJECT and not
' per OAuth CLIENT - a premise to prove: this probe, run with the owner's Desktop-app client of the family
' project, must list and read a file the ANDROID client of the same project wrote. PASS unblocks the rest of
' specification 203; FAIL closes it "by the premise" with no broader scope ever asked for (done\202 section 4).
'
' What it does, in order:
'   1. reads FMS_DRIVE_CLIENT_ID and FMS_DRIVE_CLIENT_SECRET from this shell's environment (never from a file,
'      printed nowhere - only their SHA-256, for the ticket's record);
'   2. opens the system browser at Google's consent screen - the authorization URL carries exactly
'      https://www.googleapis.com/auth/drive.appdata and nothing else (the scope guard of 203 rule 1) - and
'      takes the redirect on a random-port loopback of 127.0.0.1 with PKCE (S256) and a state check;
'   3. exchanges the code at Google's token endpoint (tokens live in memory for this run and are stored and
'      printed nowhere);
'   4. lists spaces=appDataFolder, paged, with names, sizes and times - never contents - and prints Drive's
'      own Date header beside them (contract item C judges expiry on Drive's clock);
'   5. with --write: uploads fmsx-probe-<random>.json, reads it back and verifies the SHA-256; the record stays
'      in the folder until the end of the run - the reverse direction the owner looks for from the phone, at
'      the pause before the exit - and is deleted last, however the run ended;
'   6. asks which of the listed files the ANDROID client wrote (or takes it from --read / --answer), reads it
'      and prints its SHA-256 and length;
'   7. prints one verdict line and exits: PASS 0, FAIL 1, COULD NOT VERIFY 2.
'
' Run by hand only (tools\check-placement.jsonl, class hand-run):
'   dotnet run --project tests/Integration/DriveAppDataProbe/DriveAppDataProbe.vbproj [--read <name>] [--write]
'              [--answer y|n|c]

Friend Module Program

    Private Const ProbeName As String = "DriveAppDataProbe"
    Private Const ClientIdVariable As String = "FMS_DRIVE_CLIENT_ID"
    Private Const ClientSecretVariable As String = "FMS_DRIVE_CLIENT_SECRET"
    Private Const ConsentWaitSeconds As Integer = 300
    Private Const DriveFilesBase As String = "https://www.googleapis.com/drive/v3/files"
    Private Const DriveUploadBase As String = "https://www.googleapis.com/upload/drive/v3/files"
    Private Const ExpectedScope As String = "https://www.googleapis.com/auth/drive.appdata"

    Private Enum ProbeVerdict
        Pass
        Fail
        CouldNotVerify
    End Enum

    Private Enum ReadOutcome
        Ok
        Refused
        Unreachable
    End Enum

    Private Class DriveFile
        Public Id As String = ""
        Public Name As String = ""
        Public MimeType As String = ""
        Public SizeText As String = "?"
        Public Created As String = ""
        Public Modified As String = ""
        Public AppProperties As String = ""
    End Class

    Private ReadOnly Http As New HttpClient With {.Timeout = TimeSpan.FromSeconds(60)}

    Function Main(args As String()) As Integer
        Dim readName As String = ""
        Dim doWrite As Boolean = False
        Dim answer As String = ""
        Dim i As Integer = 0
        While i < args.Length
            Select Case args(i)
                Case "--read"
                    If i + 1 >= args.Length Then Return Usage("the --read switch needs a file name")
                    i += 1
                    readName = args(i)
                Case "--write"
                    doWrite = True
                Case "--answer"
                    If i + 1 >= args.Length Then Return Usage("the --answer switch needs y, n or c")
                    i += 1
                    answer = args(i).Trim().ToLowerInvariant()
                    If answer <> "y" AndAlso answer <> "n" AndAlso answer <> "c" Then Return Usage("--answer understands y, n or c")
                Case "--help", "-h", "/?"
                    PrintUsage()
                    Return 0
                Case Else
                    Return Usage("unknown argument: " & args(i))
            End Select
            i += 1
        End While

        Dim result = Run(readName, doWrite, answer)
        Return Verdict(result.Key, result.Value)
    End Function

    Private Function Run(readName As String, doWrite As Boolean, answer As String) As KeyValuePair(Of ProbeVerdict, String)
        ' The --write record is the run's own artifact (done\202 section 3 step 4: deleted at the end of the run):
        ' it stays until the owner has had the chance to look for it from the phone, and goes here last.
        Dim probeFileId As String = ""
        Dim probeFileName As String = ""
        Try
            Dim result As KeyValuePair(Of ProbeVerdict, String) = RunCore(readName, doWrite, answer, probeFileId, probeFileName)
            If probeFileId.Length > 0 Then WaitForPhone(probeFileName)
            Return result
        Finally
            DeleteProbeFile(probeFileId, probeFileName)
        End Try
    End Function

    Private Function RunCore(readName As String, doWrite As Boolean, answer As String, ByRef probeFileId As String, ByRef probeFileName As String) As KeyValuePair(Of ProbeVerdict, String)

        ' The probe's own scope guard: the constant the whole channel shares is the one rule 203.1 names, or the
        ' run is a defect before anything is sent.
        If Not String.Equals(ExchangeDriveAuth.Scope, ExpectedScope, StringComparison.Ordinal) Then
            Return KeyValuePair.Create(ProbeVerdict.Fail, "the scope constant is not the one constant of 203 rule 1 - refusing to run")
        End If

        Dim clientId As String = Environment.GetEnvironmentVariable(ClientIdVariable)
        Dim clientSecret As String = Environment.GetEnvironmentVariable(ClientSecretVariable)
        If String.IsNullOrWhiteSpace(clientId) OrElse String.IsNullOrWhiteSpace(clientSecret) Then
            Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify,
                "set " & ClientIdVariable & " and " & ClientSecretVariable & " in this shell's environment (done\202 section 2; the probe stores and prints neither)")
        End If

        Console.WriteLine(ProbeName & ": client id sha256 " & Sha256Hex(Encoding.UTF8.GetBytes(clientId)) & " (the id itself is printed nowhere)")

        ' ---- the consent loop: a random-port loopback listener, the system browser, PKCE S256, a state check
        Dim listener As HttpListener = Nothing
        Dim redirectUri As String = ""
        If Not TryStartLoopback(listener, redirectUri) Then
            Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "no free loopback port for the redirect listener")
        End If

        Dim codeVerifier As String = ExchangeDriveAuth.NewCodeVerifier()
        Dim state As String = ExchangeDriveAuth.NewState()
        Dim authorizationUrl As String = ExchangeDriveAuth.BuildAuthorizationUrl(clientId, redirectUri, ExchangeDriveAuth.CodeChallenge(codeVerifier), state)

        ' The URL about to leave this process is re-read against the constant here too (203 rule 1, twice).
        Dim scopeProblem As String = ExchangeDriveAuth.ScopeViolation(authorizationUrl)
        If scopeProblem.Length > 0 Then
            listener.Close()
            Return KeyValuePair.Create(ProbeVerdict.Fail, "the authorization URL fails the scope rule of 203: " & scopeProblem)
        End If

        Console.WriteLine(ProbeName & ": opening the system browser for the consent screen (scope " & ExchangeDriveAuth.Scope & ", only that scope)..")
        Try
            Process.Start(New ProcessStartInfo(authorizationUrl) With {.UseShellExecute = True})
        Catch ex As Exception
            listener.Close()
            Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "the system browser did not open (" & ex.Message & ")")
        End Try

        Dim authorizationCode As String = ""
        Dim consentError As String = ""
        Dim stateMatches As Boolean = False
        ' One deadline for the whole consent. A request that carries none of state, code or error (a favicon
        ' fetch, a scanner) is not the redirect: it is answered 404 and the wait goes on.
        Dim received As HttpListenerContext = Nothing
        Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(ConsentWaitSeconds)
        Do
            Dim remaining As TimeSpan = deadline - DateTime.UtcNow
            If remaining <= TimeSpan.Zero Then Exit Do
            Dim contextWait As Task(Of HttpListenerContext) = listener.GetContextAsync()
            If Not contextWait.Wait(remaining) Then Exit Do
            Dim candidate As HttpListenerContext = contextWait.Result
            Dim candidateQuery = candidate.Request.QueryString
            If candidateQuery("state") IsNot Nothing OrElse candidateQuery("code") IsNot Nothing OrElse candidateQuery("error") IsNot Nothing Then
                received = candidate
                Exit Do
            End If
            candidate.Response.StatusCode = 404
            candidate.Response.Close()
        Loop
        If received Is Nothing Then
            listener.Close()
            Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "no redirect arrived within " & ConsentWaitSeconds.ToString(Globalization.CultureInfo.InvariantCulture) & " s")
        End If

        AnswerBrowser(received)
        Dim query = received.Request.QueryString
        consentError = If(query("error"), "")
        stateMatches = String.Equals(query("state"), state, StringComparison.Ordinal)
        authorizationCode = If(query("code"), "")
        listener.Close()

        If consentError.Length > 0 Then
            Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "consent did not complete: " & consentError)
        End If
        If Not stateMatches Then
            ' A state that does not match says nothing about the premise of 8.2: the run could not be verified.
            Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "the redirect's state does not match what was sent - something else answered on the port; run the probe again")
        End If
        If authorizationCode.Length = 0 Then
            Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "the redirect carried no authorization code")
        End If

        ' ---- the code goes straight back to Google for the tokens of this one run
        Dim responseBody As String = ""
        Try
            Using request As New HttpRequestMessage(HttpMethod.Post, ExchangeDriveAuth.TokenEndpoint)
                request.Content = New StringContent(ExchangeDriveAuth.TokenRequestBody(authorizationCode, clientId, clientSecret, redirectUri, codeVerifier), Encoding.UTF8, "application/x-www-form-urlencoded")
                Using response As HttpResponseMessage = Http.SendAsync(request).GetAwaiter().GetResult()
                    responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    If Not response.IsSuccessStatusCode Then
                        Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "the token exchange was refused (HTTP " & CInt(response.StatusCode).ToString(Globalization.CultureInfo.InvariantCulture) & "); check the client id and secret this shell carries")
                    End If
                End Using
            End Using
        Catch ex As Exception
            Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "the token endpoint was not reached (" & ex.Message & ")")
        End Try
        Dim accessToken As String = JsonString(responseBody, "access_token")
        If String.IsNullOrEmpty(accessToken) Then
            Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "the token response carried no access_token")
        End If
        Http.DefaultRequestHeaders.Authorization = New AuthenticationHeaderValue("Bearer", accessToken)

        ' ---- the listing of the appDataFolder space
        Dim files As New List(Of DriveFile)
        Dim driveDateHeader As String = ""
        Dim listProblem As String = ""
        If Not TryList(files, driveDateHeader, listProblem) Then
            Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, listProblem)
        End If

        Console.WriteLine(ProbeName & ": appDataFolder listing - " & files.Count.ToString(Globalization.CultureInfo.InvariantCulture) & " file(s); Drive's own Date header: " & If(driveDateHeader, "(absent)"))
        For number = 1 To files.Count
            Dim f = files(number - 1)
            Dim line As String = "  " & number.ToString(Globalization.CultureInfo.InvariantCulture) & ". " & f.Name & "  " & f.MimeType & "  " & f.SizeText & " bytes  modified " & f.Modified
            If f.Created <> f.Modified Then line &= "  created " & f.Created
            If f.AppProperties.Length > 0 Then line &= "  appProperties " & f.AppProperties
            Console.WriteLine(line)
        Next

        ' ---- --write: our own record, read back with its hash, kept until the end of the run (Run deletes it)
        If doWrite Then
            Dim writeOutcome As ProbeVerdict = ProbeVerdict.Pass
            Dim writeProblem As String = ""
            If Not WriteRoundTrip(writeOutcome, writeProblem, probeFileId, probeFileName) Then
                Return KeyValuePair.Create(writeOutcome, writeProblem)
            End If
        End If

        ' ---- the cross-product question: which of these did the ANDROID client write?
        Dim picked As DriveFile = Nothing

        If readName.Length > 0 Then
            picked = files.FirstOrDefault(Function(f) String.Equals(f.Name, readName, StringComparison.Ordinal))
            If picked Is Nothing Then
                Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "the --read file """ & readName & """ is not in the listing")
            End If
        ElseIf answer = "y" Then
            picked = files.FirstOrDefault(Function(f) f.Name.StartsWith("fmsx-", StringComparison.Ordinal) AndAlso Not f.Name.StartsWith("fmsx-probe-", StringComparison.Ordinal))
            If picked Is Nothing Then
                Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "--answer y but the listing holds no fmsx- file; pass --read <name> to name the phone's file")
            End If
        ElseIf answer = "n" Then
            Return KeyValuePair.Create(ProbeVerdict.Fail, "the owner finds no file the Android client wrote in the appDataFolder of the shared account - the premise of 8.2 fails for this client pair")
        ElseIf answer = "c" Then
            Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "the owner could not tell whether the listing holds a file the Android client wrote")
        Else
            Console.WriteLine()
            Console.WriteLine("Which of the files above did the ANDROID client write to this folder?")
            Console.WriteLine("  a number - read that file and print its SHA-256 and length (the premise proven for the read)")
            Console.WriteLine("  n - none of them is such a file (the premise fails)")
            Console.WriteLine("  c - cannot tell (the phone may have written nothing to this account yet)")
            Console.Write("your answer: ")
            Dim typed As String = Console.ReadLine()
            If typed Is Nothing Then typed = "c"
            Dim choice As String = typed.Trim().ToLowerInvariant()
            If choice = "n" Then
                Return KeyValuePair.Create(ProbeVerdict.Fail, "the owner finds no file the Android client wrote in the appDataFolder of the shared account - the premise of 8.2 fails for this client pair")
            End If
            If choice = "c" Then
                Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "the owner could not tell whether the listing holds a file the Android client wrote")
            End If
            Dim number As Integer = 0
            If Not Integer.TryParse(choice, number) OrElse number < 1 OrElse number > files.Count Then
                Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, "the answer was not one of the choices (a number, n or c)")
            End If
            picked = files(number - 1)
        End If

        ' ---- the read: its SHA-256 and length, never its contents
        Dim outcome As ReadOutcome = ReadOutcome.Unreachable
        Dim readProblem As String = ""
        Dim hashLine As String = ""
        outcome = ReadAndHash(picked, hashLine, readProblem)
        If hashLine.Length > 0 Then Console.WriteLine(ProbeName & ": " & hashLine)
        Select Case outcome
            Case ReadOutcome.Ok
                Return KeyValuePair.Create(ProbeVerdict.Pass, "signed in, listed " & files.Count.ToString(Globalization.CultureInfo.InvariantCulture) & " file(s) and read """ & picked.Name & """ - the owner names it as the Android client's; the premise of 8.2 is proven for this client pair")
            Case ReadOutcome.Refused
                Return KeyValuePair.Create(ProbeVerdict.Fail, readProblem)
            Case Else
                Return KeyValuePair.Create(ProbeVerdict.CouldNotVerify, readProblem)
        End Select
    End Function

    ' ---------------------------------------------------------------- the loopback listener

    Private Function TryStartLoopback(ByRef listener As HttpListener, ByRef redirectUri As String) As Boolean
        Dim portPicker As New Random()
        For attempt = 1 To 20
            Dim port As Integer = portPicker.Next(49152, 65536)
            Dim candidate As New HttpListener()
            candidate.Prefixes.Add(ExchangeDriveAuth.NewRedirectUri(port))
            Try
                candidate.Start()
                listener = candidate
                redirectUri = ExchangeDriveAuth.NewRedirectUri(port)
                Return True
            Catch ex As HttpListenerException
                candidate.Close()
            End Try
        Next
        Return False
    End Function

    Private Sub AnswerBrowser(context As HttpListenerContext)
        Dim page As Byte() = Encoding.UTF8.GetBytes("<html><body><p>Sign-in complete. You can close this window and read the probe's console.</p></body></html>")
        context.Response.ContentType = "text/html; charset=utf-8"
        context.Response.ContentLength64 = page.Length
        context.Response.OutputStream.Write(page, 0, page.Length)
        context.Response.OutputStream.Close()
    End Sub

    ' ---------------------------------------------------------------- the Drive calls

    Private Function TryList(files As List(Of DriveFile), ByRef driveDateHeader As String, ByRef problem As String) As Boolean
        Dim pageToken As String = ""
        Try
            Do
                Dim url As String = DriveFilesBase & "?spaces=appDataFolder&pageSize=200&fields=" &
                    Uri.EscapeDataString("nextPageToken,files(id,name,mimeType,size,createdTime,modifiedTime,appProperties)")
                If pageToken.Length > 0 Then url &= "&pageToken=" & Uri.EscapeDataString(pageToken)
                Using response As HttpResponseMessage = Http.GetAsync(url).GetAwaiter().GetResult()
                    Dim json As String = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    If Not response.IsSuccessStatusCode Then
                        problem = "files.list was refused (HTTP " & CInt(response.StatusCode).ToString(Globalization.CultureInfo.InvariantCulture) & ")"
                        Return False
                    End If
                    If response.Headers.Date.HasValue Then driveDateHeader = response.Headers.Date.Value.ToString("R")
                    pageToken = ""
                    Using document As JsonDocument = JsonDocument.Parse(json)
                        Dim root As JsonElement = document.RootElement
                        Dim token As JsonElement = Nothing
                        If root.TryGetProperty("nextPageToken", token) Then pageToken = If(token.GetString(), "")
                        Dim array As JsonElement = Nothing
                        If root.TryGetProperty("files", array) AndAlso array.ValueKind = JsonValueKind.Array Then
                            For Each item As JsonElement In array.EnumerateArray()
                                Dim file As New DriveFile()
                                file.Id = JsonProperty(item, "id")
                                file.Name = JsonProperty(item, "name")
                                file.MimeType = JsonProperty(item, "mimeType")
                                file.SizeText = JsonProperty(item, "size")
                                If file.SizeText.Length = 0 Then file.SizeText = "?"
                                file.Created = JsonProperty(item, "createdTime")
                                file.Modified = JsonProperty(item, "modifiedTime")
                                Dim properties As JsonElement = Nothing
                                If item.TryGetProperty("appProperties", properties) AndAlso properties.ValueKind = JsonValueKind.Object AndAlso properties.EnumerateObject().Any() Then
                                    file.AppProperties = properties.GetRawText()
                                End If
                                files.Add(file)
                            Next
                        End If
                    End Using
                End Using
            Loop While pageToken.Length > 0
            Return True
        Catch ex As Exception
            problem = "the listing was not reached (" & ex.Message & ")"
            Return False
        End Try
    End Function

    ''' <summary>Reads one file with alt=media, hashing the stream - the contents are never held or printed.</summary>
    Private Function ReadAndHash(file As DriveFile, ByRef hashLine As String, ByRef problem As String) As ReadOutcome
        Dim url As String = DriveFilesBase & "/" & Uri.EscapeDataString(file.Id) & "?alt=media"
        Try
            Using response As HttpResponseMessage = Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult()
                If Not response.IsSuccessStatusCode Then
                    Dim refusedBody As String = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    problem = "the read of """ & file.Name & """ was refused (HTTP " & CInt(response.StatusCode).ToString(Globalization.CultureInfo.InvariantCulture) & ")"
                    ' A refusal that says nothing about the premise (credential, rate limit, server) is not a FAIL.
                    Return If(IsNotAboutThePremise(CInt(response.StatusCode), refusedBody), ReadOutcome.Unreachable, ReadOutcome.Refused)
                End If
                Dim hash As String = ""
                Dim total As Long = 0
                Using stream As Stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                    HashStream(stream, hash, total)
                End Using
                hashLine = "read " & file.Name & " - " & total.ToString(Globalization.CultureInfo.InvariantCulture) & " bytes, sha256 " & hash & " (the contents are printed nowhere)"
                Return ReadOutcome.Ok
            End Using
        Catch ex As Exception
            problem = "the read of """ & file.Name & """ was not reached (" & ex.Message & ")"
            Return ReadOutcome.Unreachable
        End Try
    End Function

    ''' <summary>Uploads fmsx-probe-&lt;random&gt;.json (multipart), reads it back and verifies the SHA-256 - the
    ''' reverse direction the owner can look for from the phone. On success the record is NOT deleted: its id and
    ''' name come back in <paramref name="keptId"/> / <paramref name="keptName"/> and <see cref="Run"/> deletes it at
    ''' the end of the run. Returns False (the record already deleted) with <paramref name="verdict"/> saying
    ''' whether that is a defect (a hash mismatch) or the drive or network saying no.</summary>
    Private Function WriteRoundTrip(ByRef verdict As ProbeVerdict, ByRef problem As String, ByRef keptId As String, ByRef keptName As String) As Boolean
        Dim keep As Boolean = False
        Dim nonce As String = Sha256Hex(Guid.NewGuid().ToByteArray()).Substring(0, 12)
        Dim name As String = "fmsx-probe-" & nonce & ".json"
        Dim body As String = "{""probe"":""FMS_W DriveAppDataProbe (specification 203)"",""writtenAtUtc"":""" & DateTime.UtcNow.ToString("o", Globalization.CultureInfo.InvariantCulture) & """,""nonce"":""" & nonce & """}"
        Dim localHash As String = Sha256Hex(Encoding.UTF8.GetBytes(body))
        Dim uploadedId As String = ""

        Try
            Dim metadata As String = "{""name"":""" & name & """,""parents"":[""appDataFolder""]}"
            Using form As New MultipartContent("related", "fmsprobe" & nonce)
                Dim metadataPart As New StringContent(metadata, Encoding.UTF8, "application/json")
                Dim mediaPart As New StringContent(body, Encoding.UTF8, "application/json")
                form.Add(metadataPart)
                form.Add(mediaPart)
                Using response As HttpResponseMessage = Http.PostAsync(DriveUploadBase & "?uploadType=multipart&fields=" & Uri.EscapeDataString("id"), form).GetAwaiter().GetResult()
                    Dim json As String = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    If Not response.IsSuccessStatusCode Then
                        problem = "Drive refused the probe write (HTTP " & CInt(response.StatusCode).ToString(Globalization.CultureInfo.InvariantCulture) & ")"
                        verdict = ProbeVerdict.CouldNotVerify
                        Return False
                    End If
                    uploadedId = JsonString(json, "id")
                End Using
            End Using

            If uploadedId.Length = 0 Then
                problem = "the upload response named no file id"
                verdict = ProbeVerdict.Fail
                Return False
            End If

            Dim backHash As String = ""
            Dim backSize As Long = 0
            Using response As HttpResponseMessage = Http.GetAsync(DriveFilesBase & "/" & Uri.EscapeDataString(uploadedId) & "?alt=media", HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult()
                If Not response.IsSuccessStatusCode Then
                    Dim refusedBody As String = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    problem = "the read-back of " & name & " was refused (HTTP " & CInt(response.StatusCode).ToString(Globalization.CultureInfo.InvariantCulture) & ")"
                    verdict = If(IsNotAboutThePremise(CInt(response.StatusCode), refusedBody), ProbeVerdict.CouldNotVerify, ProbeVerdict.Fail)
                    Return False
                End If
                Using stream As Stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                    Dim hash As String = ""
                    HashStream(stream, hash, backSize)
                    backHash = hash
                End Using
            End Using

            If Not String.Equals(backHash, localHash, StringComparison.Ordinal) Then
                problem = "the read-back of " & name & " does not match what was written (sha256 " & backHash & ", expected " & localHash & ")"
                verdict = ProbeVerdict.Fail
                Return False
            End If
            Console.WriteLine(ProbeName & ": wrote " & name & " (" & backSize.ToString(Globalization.CultureInfo.InvariantCulture) & " bytes), read it back, sha256 matches - it stays in the folder until the end of the run, look for it from the phone")
            keptId = uploadedId
            keptName = name
            keep = True
            Return True
        Catch ex As Exception
            problem = "the probe write was not reached (" & ex.Message & ")"
            verdict = ProbeVerdict.CouldNotVerify
            Return False
        Finally
            ' A round trip that did not complete leaves nothing behind; a complete one is deleted by Run.
            If uploadedId.Length > 0 AndAlso Not keep Then DeleteProbeFile(uploadedId, name)
        End Try
    End Function

    ''' <summary>Deletes the probe's own record - one permanent delete, appDataFolder has no trash (contract item
    ''' CR). A no-op when no record was written; a refusal is a printed warning, never a verdict.</summary>
    Private Sub DeleteProbeFile(fileId As String, fileName As String)
        If String.IsNullOrEmpty(fileId) Then Return
        Try
            Using response As HttpResponseMessage = Http.DeleteAsync(DriveFilesBase & "/" & Uri.EscapeDataString(fileId)).GetAwaiter().GetResult()
                If Not response.IsSuccessStatusCode Then
                    Console.WriteLine(ProbeName & ": WARNING - deleting " & fileName & " was refused (HTTP " & CInt(response.StatusCode).ToString(Globalization.CultureInfo.InvariantCulture) & "); delete it by hand from the phone or another Drive client")
                Else
                    Console.WriteLine(ProbeName & ": deleted " & fileName)
                End If
            End Using
        Catch ex As Exception
            Console.WriteLine(ProbeName & ": WARNING - deleting " & fileName & " was not reached (" & ex.Message & "); delete it by hand from the phone or another Drive client")
        End Try
    End Sub

    ''' <summary>The pause that makes the reverse direction observable: the record is still in the folder, the owner
    ''' looks for it from the phone, Enter deletes it and ends the run. A run with no console to answer on (input
    ''' redirected) deletes at once.</summary>
    Private Sub WaitForPhone(fileName As String)
        If Console.IsInputRedirected Then Return
        Console.WriteLine(ProbeName & ": " & fileName & " is still in the folder - look for it from the phone now (the reverse direction), then press Enter here to delete it and finish")
        Console.ReadLine()
    End Sub

    ''' <summary>True when a refused Drive call says nothing about the premise of 8.2: a missing or expired
    ''' credential (401), a timeout (408), a rate limit (429, or a 403 with a rate-limit reason) or a server error
    ''' (5xx) - the back-off cases of contract item CQ. Such a refusal is COULD NOT VERIFY, never a FAIL: a FAIL
    ''' closes specification 203 "by the premise" (done\202 section 4).</summary>
    Private Function IsNotAboutThePremise(statusCode As Integer, body As String) As Boolean
        If statusCode = 401 OrElse statusCode = 408 OrElse statusCode = 429 OrElse statusCode >= 500 Then Return True
        If statusCode <> 403 OrElse String.IsNullOrEmpty(body) Then Return False
        For Each reason As String In {"rateLimitExceeded", "userRateLimitExceeded", "dailyLimitExceeded", "sharingRateLimitExceeded", "quotaExceeded", "backendError"}
            If body.IndexOf(reason, StringComparison.Ordinal) >= 0 Then Return True
        Next
        Return False
    End Function

    ' ---------------------------------------------------------------- small helpers

    Private Sub HashStream(stream As Stream, ByRef hashHex As String, ByRef totalBytes As Long)
        Using sha As SHA256 = SHA256.Create()
            Dim buffer(65535) As Byte
            Dim read As Integer = 0
            totalBytes = 0
            Do
                read = stream.Read(buffer, 0, buffer.Length)
                If read <= 0 Then Exit Do
                sha.TransformBlock(buffer, 0, read, Nothing, 0)
                totalBytes += read
            Loop
            sha.TransformFinalBlock(buffer, 0, 0)
            hashHex = ShaHex(sha.Hash)
        End Using
    End Sub

    Private Function Sha256Hex(bytes As Byte()) As String
        Using sha As SHA256 = SHA256.Create()
            Return ShaHex(sha.ComputeHash(bytes))
        End Using
    End Function

    Private Function ShaHex(hash As Byte()) As String
        Dim text As New StringBuilder(hash.Length * 2)
        For Each b As Byte In hash
            text.Append(b.ToString("x2", Globalization.CultureInfo.InvariantCulture))
        Next
        Return text.ToString()
    End Function

    Private Function JsonString(json As String, name As String) As String
        Try
            Using document As JsonDocument = JsonDocument.Parse(json)
                Return JsonProperty(document.RootElement, name)
            End Using
        Catch ex As JsonException
            Return ""
        End Try
    End Function

    Private Function JsonProperty(element As JsonElement, name As String) As String
        Dim value As JsonElement = Nothing
        If element.ValueKind = JsonValueKind.Object AndAlso element.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.String Then
            Return value.GetString()
        End If
        Return ""
    End Function

    Private Function Verdict(v As ProbeVerdict, reason As String) As Integer
        Select Case v
            Case ProbeVerdict.Pass
                Console.WriteLine(ProbeName & ": PASS (" & reason & ")")
                Return 0
            Case ProbeVerdict.Fail
                Console.WriteLine(ProbeName & ": FAIL (" & reason & ")")
                Return 1
            Case Else
                Console.WriteLine(ProbeName & ": COULD NOT VERIFY (" & reason & ")")
                Return 2
        End Select
    End Function

    Private Function Usage(problem As String) As Integer
        Console.WriteLine(ProbeName & ": " & problem)
        PrintUsage()
        Return 2
    End Function

    Private Sub PrintUsage()
        Console.WriteLine("usage: dotnet run --project tests/Integration/DriveAppDataProbe/DriveAppDataProbe.vbproj [options]")
        Console.WriteLine("  --read <name>   read this file from the listing and print its SHA-256 and length")
        Console.WriteLine("  --write         upload fmsx-probe-<random>.json, read it back (sha256), keep it for the phone to see until you press Enter, then delete it")
        Console.WriteLine("  --answer y|n|c  pre-answer the cross-product question (y reads the first fmsx- file)")
        Console.WriteLine("environment: " & ClientIdVariable & ", " & ClientSecretVariable & " - the Desktop OAuth client of the family Google Cloud project")
        Console.WriteLine("verdicts: PASS 0 (a phone-written file listed and read), FAIL 1 (the premise failed), COULD NOT VERIFY 2 (no client, no consent, no network)")
    End Sub

End Module
