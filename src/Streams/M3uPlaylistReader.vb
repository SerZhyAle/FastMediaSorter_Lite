#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Text.RegularExpressions

Namespace Streams

    ''' <summary>
    ''' Result of reading and parsing one or more M3U/M3U8 playlist files.
    ''' </summary>
    Friend Class PlaylistParseResult
        Public Property Rows As New List(Of StreamCatalogRow)()
        Public Property Refusals As New List(Of StreamRefusal)()
        Public Property Folds As New List(Of StreamRefusal)()
        Public Property TotalEntriesFound As Integer = 0
    End Class

    ''' <summary>
    ''' Robust M3U and M3U8 playlist reader conforming to STREAM-BANK 2.1 producer rules (spec 022).
    ''' </summary>
    Friend Module M3uPlaylistReader

        Private ReadOnly SlotPrefixRegex As New Regex("^\s*-\s*\d+\s+[A-Za-z0-9]+\s*-\s*", RegexOptions.Compiled)
        Private ReadOnly ExtinfRegex As New Regex("^#EXTINF:\s*(-?\d*(?:\.\d+)?)\s*(.*?),(.*)$", RegexOptions.Compiled Or RegexOptions.Singleline)
        Private ReadOnly AttributeRegex As New Regex("([a-zA-Z0-9_-]+)\s*=\s*""([^""]*)""", RegexOptions.Compiled)

        ''' <summary>
        ''' Reads a single playlist file or all playlist files in a folder recursively.
        ''' </summary>
        Public Function ReadPath(inputPath As String) As PlaylistParseResult
            Dim result As New PlaylistParseResult()
            If String.IsNullOrWhiteSpace(inputPath) Then Return result

            If File.Exists(inputPath) Then
                ReadSingleFile(inputPath, result)
            ElseIf Directory.Exists(inputPath) Then
                Dim files = Directory.GetFiles(inputPath, "*.*", SearchOption.AllDirectories)
                Array.Sort(files, StringComparer.OrdinalIgnoreCase)
                For Each f In files
                    Dim ext = Path.GetExtension(f).ToLowerInvariant()
                    If ext = ".m3u" OrElse ext = ".m3u8" Then
                        ReadSingleFile(f, result)
                    End If
                Next
            End If

            Return result
        End Function

        ''' <summary>
        ''' Reads and parses a single playlist file into the cumulative result.
        ''' </summary>
        Public Sub ReadSingleFile(filePath As String, result As PlaylistParseResult)
            If Not File.Exists(filePath) Then Return

            Dim fileName As String = Path.GetFileName(filePath)
            Dim lines As String() = Nothing
            Try
                lines = File.ReadAllLines(filePath, Encoding.UTF8)
            Catch ex As Exception
                result.Refusals.Add(New StreamRefusal(fileName, 0, "", filePath, "Failed to read file: " & FailureText.Describe(ex), True))
                Return
            End Try

            Dim currentExtinf As String = Nothing
            Dim currentExtinfLine As Integer = 0

            For lineIndex = 0 To lines.Length - 1
                Dim lineNumber As Integer = lineIndex + 1
                Dim rawLine As String = lines(lineIndex).Trim()

                If String.IsNullOrEmpty(rawLine) Then
                    Continue For
                End If

                If rawLine.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase) Then
                    currentExtinf = rawLine
                    currentExtinfLine = lineNumber
                    Continue For
                End If

                If rawLine.StartsWith("#"c) Then
                    ' Ignore other directives/comments (e.g. #EXTM3U, #EXTVLCOPT, etc.)
                    Continue For
                End If

                ' We found a stream address line
                result.TotalEntriesFound += 1
                Dim entryLineNumber = If(currentExtinfLine > 0, currentExtinfLine, lineNumber)
                ParseAndAddEntry(fileName, entryLineNumber, currentExtinf, rawLine, result)

                currentExtinf = Nothing
                currentExtinfLine = 0
            Next
        End Sub

        Private Sub ParseAndAddEntry(fileName As String, lineNumber As Integer, extinfLine As String, rawUrl As String, result As PlaylistParseResult)
            ' 1. Check URL presence
            If String.IsNullOrWhiteSpace(rawUrl) Then
                result.Refusals.Add(New StreamRefusal(fileName, lineNumber, "", "", "Missing stream URL", True))
                Return
            End If

            Dim cleanedUrl As String = rawUrl.Trim()

            ' 2. Parse URL and check scheme and authority
            Dim uriObj As Uri = Nothing
            If Not Uri.TryCreate(cleanedUrl, UriKind.Absolute, uriObj) Then
                result.Refusals.Add(New StreamRefusal(fileName, lineNumber, "", cleanedUrl, "Invalid absolute URL format: " & cleanedUrl, True))
                Return
            End If

            Dim scheme As String = uriObj.Scheme.ToLowerInvariant()

            ' Check launchable scheme (http, https, rtsp)
            If Not StreamCatalogSchema.LaunchableSchemes.Contains(scheme) Then
                result.Refusals.Add(New StreamRefusal(fileName, lineNumber, "", cleanedUrl,
                    "Non-launchable scheme '" & uriObj.Scheme & "' (allowed: http, https, rtsp)", True))
                Return
            End If

            ' Check credentials in URL
            If Not String.IsNullOrEmpty(uriObj.UserInfo) Then
                result.Refusals.Add(New StreamRefusal(fileName, lineNumber, "", cleanedUrl, "URL contains credentials (user:pass@)", True))
                Return
            End If

            ' Check broadcast token pattern (/b/<token>)
            Dim urlPath As String = uriObj.AbsolutePath
            If IsBroadcastUrl(uriObj) Then
                result.Refusals.Add(New StreamRefusal(fileName, lineNumber, "", cleanedUrl, "URL contains private broadcast token (/b/...)", True))
                Return
            End If

            ' Check nested playlist reference (.m3u, .pls)
            Dim urlExtension As String = Path.GetExtension(urlPath).ToLowerInvariant()
            If StreamCatalogSchema.RefusedPlaylistExtensions.Contains(urlExtension) Then
                result.Refusals.Add(New StreamRefusal(fileName, lineNumber, "", cleanedUrl, "Nested playlist reference is not admitted into catalog (" & urlExtension & ")", True))
                Return
            End If

            ' 3. Parse EXTINF attributes and display name
            Dim rawName As String = String.Empty
            Dim attributes As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            If Not String.IsNullOrEmpty(extinfLine) Then
                Dim m = ExtinfRegex.Match(extinfLine)
                If m.Success Then
                    Dim attrPart = m.Groups(2).Value
                    rawName = m.Groups(3).Value.Trim()

                    Dim attrMatches = AttributeRegex.Matches(attrPart)
                    For Each am As Match In attrMatches
                        Dim key = am.Groups(1).Value.Trim()
                        Dim val = am.Groups(2).Value.Trim()
                        attributes(key) = val
                    Next
                Else
                    Dim commaIndex = extinfLine.IndexOf(","c)
                    If commaIndex >= 0 AndAlso commaIndex < extinfLine.Length - 1 Then
                        rawName = extinfLine.Substring(commaIndex + 1).Trim()
                    End If
                End If
            End If

            ' 4. Name processing & Five Guarantees
            ' Guarantee 4: U+FFFD stops the row and is reported
            If rawName.IndexOf(Convert.ToChar(&HFFFD)) >= 0 Then
                result.Refusals.Add(New StreamRefusal(fileName, lineNumber, rawName, cleanedUrl, "Name contains replacement character (U+FFFD)", True))
                Return
            End If

            ' Repair name: decode HTML entities (double-encoded shape supported)
            Dim decodedName As String = WebUtility.HtmlDecode(WebUtility.HtmlDecode(rawName))

            ' Strip serialized encoder-slot prefix (- 1 A - )
            decodedName = SlotPrefixRegex.Replace(decodedName, "").Trim()

            ' Check if name carries no information or is an encoder default
            If IsEmptyOrEncoderDefaultName(decodedName) Then
                decodedName = BuildFallbackNameFromHost(uriObj)
            End If

            ' Guarantee 5: Clean string
            decodedName = NormalizeWhitespace(decodedName)

            If String.IsNullOrWhiteSpace(decodedName) Then
                decodedName = BuildFallbackNameFromHost(uriObj)
            End If

            If String.IsNullOrWhiteSpace(decodedName) Then
                result.Refusals.Add(New StreamRefusal(fileName, lineNumber, rawName, cleanedUrl, "No usable name could be derived", True))
                Return
            End If

            ' 5. Media Kind Classification
            Dim declaredKind As String = Nothing
            Dim unrecognizedDeclaredKind As String = Nothing
            Dim finalMediaKind As String = ClassifyMediaKind(uriObj, attributes, declaredKind, unrecognizedDeclaredKind)

            ' 6. Facets
            Dim countryFacet As String = ExtractCountryFacet(attributes)
            Dim languageFacet As String = ExtractLanguageFacet(attributes)

            ' 7. Build StreamCatalogRow
            Dim row As New StreamCatalogRow() With {
                .Name = decodedName,
                .Url = StreamIdentity.CanonicalizeUrl(cleanedUrl),
                .MediaKind = finalMediaKind,
                .Https = If(scheme = "https", "true", String.Empty),
                .Country = countryFacet,
                .Language = languageFacet,
                .SourceFile = fileName,
                .SourceLine = lineNumber,
                .NormalizedIdentity = StreamIdentity.GetIdentityKey(cleanedUrl),
                .DeclaredKind = If(declaredKind, String.Empty),
                .UnrecognizedDeclaredKind = If(unrecognizedDeclaredKind, String.Empty)
            }

            result.Rows.Add(row)
        End Sub

        Private Function IsBroadcastUrl(uri As Uri) As Boolean
            Dim path = uri.AbsolutePath
            If String.IsNullOrEmpty(path) Then Return False
            If path.StartsWith("/b/", StringComparison.OrdinalIgnoreCase) Then
                Return True
            End If
            Return False
        End Function

        Private Function IsEmptyOrEncoderDefaultName(name As String) As Boolean
            If String.IsNullOrWhiteSpace(name) Then Return True

            Dim trimmed = name.Trim()
            If String.Equals(trimmed, "(null)", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(trimmed, "(blank)", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(trimmed, "blank", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(trimmed, "(none)", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase) Then
                Return True
            End If

            ' Has no letter and no digit
            Dim hasLetterOrDigit As Boolean = False
            For Each c In trimmed
                If Char.IsLetterOrDigit(c) Then
                    hasLetterOrDigit = True
                    Exit For
                End If
            Next
            If Not hasLetterOrDigit Then Return True

            ' Known encoder defaults
            Dim lower = trimmed.ToLowerInvariant()
            Select Case lower
                Case "encoder", "stream", "live", "default", "untitled", "no name", "noname",
                     "channel", "audio", "video", "radio", "test", "broadcast"
                    Return True
            End Select

            Return False
        End Function

        Private Function BuildFallbackNameFromHost(uri As Uri) As String
            If uri Is Nothing Then Return "Stream"
            Dim host = uri.Host
            Dim port = uri.Port
            Dim isDefaultPort = (uri.Scheme = "http" AndAlso port = 80) OrElse
                                (uri.Scheme = "https" AndAlso port = 443) OrElse
                                (uri.Scheme = "rtsp" AndAlso port = 554) OrElse
                                (port <= 0)

            If Not isDefaultPort Then
                Return host & ":" & port.ToString()
            Else
                Return host
            End If
        End Function

        Private Function NormalizeWhitespace(text As String) As String
            If String.IsNullOrEmpty(text) Then Return String.Empty
            Dim parts = text.Split(New Char() {" "c, ControlChars.Tab, ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
            Return String.Join(" ", parts).Trim()
        End Function

        Private Function ClassifyMediaKind(uri As Uri, attributes As Dictionary(Of String, String), ByRef declaredKind As String, ByRef unrecognizedDeclaredKind As String) As String
            declaredKind = Nothing
            unrecognizedDeclaredKind = Nothing

            ' Check declared attributes
            Dim typeAttr As String = Nothing
            If attributes.TryGetValue("type", typeAttr) OrElse
               attributes.TryGetValue("tvg-type", typeAttr) OrElse
               attributes.TryGetValue("group-title", typeAttr) Then
                If Not String.IsNullOrWhiteSpace(typeAttr) Then
                    Dim lowerType = typeAttr.Trim().ToLowerInvariant()
                    If lowerType = "audio" OrElse lowerType = "radio" OrElse lowerType.Contains("radio") Then
                        declaredKind = StreamCatalogSchema.MediaKindAudio
                        Return StreamCatalogSchema.MediaKindAudio
                    ElseIf lowerType = "video" OrElse lowerType = "tv" OrElse lowerType.Contains("tv") Then
                        declaredKind = StreamCatalogSchema.MediaKindVideo
                        Return StreamCatalogSchema.MediaKindVideo
                    ElseIf lowerType = "rtsp" Then
                        declaredKind = StreamCatalogSchema.MediaKindRtsp
                        Return StreamCatalogSchema.MediaKindRtsp
                    Else
                        unrecognizedDeclaredKind = typeAttr
                    End If
                End If
            End If

            Dim radioAttr As String = Nothing
            If attributes.TryGetValue("radio", radioAttr) AndAlso Not String.IsNullOrWhiteSpace(radioAttr) Then
                If radioAttr.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) Then
                    declaredKind = StreamCatalogSchema.MediaKindAudio
                    Return StreamCatalogSchema.MediaKindAudio
                End If
            End If

            ' URL classifier fallback (STREAM-BANK 2.1 / 03 §4)
            If uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) Then
                Return StreamCatalogSchema.MediaKindRtsp
            End If

            Dim path = uri.AbsolutePath
            Dim ext = System.IO.Path.GetExtension(path).ToLowerInvariant()
            If StreamCatalogSchema.VideoExtensions.Contains(ext) Then
                Return StreamCatalogSchema.MediaKindVideo
            End If

            Return StreamCatalogSchema.MediaKindAudio
        End Function

        Private Function ExtractCountryFacet(attributes As Dictionary(Of String, String)) As String
            Dim countryVal As String = Nothing
            If attributes.TryGetValue("tvg-country", countryVal) OrElse
               attributes.TryGetValue("country", countryVal) Then
                If Not String.IsNullOrWhiteSpace(countryVal) Then
                    Dim trimmed = countryVal.Trim()
                    If trimmed.Length = 2 AndAlso Char.IsLetter(trimmed(0)) AndAlso Char.IsLetter(trimmed(1)) Then
                        Return trimmed.ToUpperInvariant()
                    End If
                End If
            End If
            Return String.Empty
        End Function

        Private Function ExtractLanguageFacet(attributes As Dictionary(Of String, String)) As String
            Dim langVal As String = Nothing
            If attributes.TryGetValue("tvg-language", langVal) OrElse
               attributes.TryGetValue("language", langVal) Then
                If Not String.IsNullOrWhiteSpace(langVal) Then
                    Dim trimmed = langVal.Trim().ToLowerInvariant()
                    Return MapLanguageToEnglishName(trimmed)
                End If
            End If
            Return String.Empty
        End Function

        Private Function MapLanguageToEnglishName(codeOrName As String) As String
            Select Case codeOrName
                Case "ru", "rus", "russian" : Return "russian"
                Case "en", "eng", "english" : Return "english"
                Case "uk", "ukr", "ukrainian" : Return "ukrainian"
                Case "de", "ger", "deu", "german" : Return "german"
                Case "fr", "fra", "fre", "french" : Return "french"
                Case "es", "spa", "spanish" : Return "spanish"
                Case "it", "ita", "italian" : Return "italian"
                Case "pt", "por", "portuguese" : Return "portuguese"
                Case "ar", "ara", "arabic" : Return "arabic"
                Case "hi", "hin", "hindi" : Return "hindi"
                Case "bn", "ben", "bengali" : Return "bengali"
                Case "ur", "urd", "urdu" : Return "urdu"
                Case "zh", "zho", "chi", "chinese" : Return "chinese"
                Case "ja", "jpn", "japanese" : Return "japanese"
                Case "ko", "kor", "korean" : Return "korean"
                Case "pl", "pol", "polish" : Return "polish"
                Case "nl", "nld", "dut", "dutch" : Return "dutch"
                Case "tr", "tur", "turkish" : Return "turkish"
                Case Else
                    ' If it's already a single English word of letters, allow lowercase
                    If codeOrName.Length >= 2 AndAlso codeOrName.Length <= 20 Then
                        Dim allLetters = True
                        For Each c In codeOrName
                            If Not Char.IsLetter(c) Then
                                allLetters = False
                                Exit For
                            End If
                        Next
                        If allLetters Then Return codeOrName
                    End If
                    Return String.Empty
            End Select
        End Function

    End Module

End Namespace
#End If
