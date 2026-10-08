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

        ''' <summary>
        ''' `USER-PLAYLIST` section 8 item 1: a reader bounds its input. 32 MiB (33 554 432 bytes) for a
        ''' local file; a larger one is refused whole and nothing is imported.
        ''' </summary>
        Friend Const MaxPlaylistBytes As Long = 33554432L

        ''' <summary>Leads the reason of a refusal of a whole file that is an HLS media manifest (`USER-PLAYLIST` rule 3).</summary>
        Friend Const HlsManifestStatus As String = "HlsManifest"

        ''' <summary>Leads the reason of a refusal of a whole file over <see cref="MaxPlaylistBytes"/> (`USER-PLAYLIST` section 8 item 1).</summary>
        Friend Const TooLargeStatus As String = "TooLarge"

        Private Const ExtinfPrefix As String = "#EXTINF:"
        Private Const HlsTagPrefix As String = "#EXT-X-"

        Private ReadOnly SlotPrefixRegex As New Regex("^\s*-\s*\d+\s+[A-Za-z0-9]+\s*-\s*", RegexOptions.Compiled)
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
            Dim lines As List(Of String) = Nothing
            Try
                lines = ReadBoundedLines(filePath)
            Catch ex As Exception
                result.Refusals.Add(New StreamRefusal(fileName, 0, "", filePath, "Failed to read file: " & FailureText.Describe(ex), True))
                Return
            End Try

            If lines Is Nothing Then
                result.Refusals.Add(New StreamRefusal(fileName, 0, "", filePath,
                    TooLargeStatus & ": the file is larger than " & MaxPlaylistBytes.ToString(Globalization.CultureInfo.InvariantCulture) &
                    " bytes (32 MiB); nothing was imported", True))
                Return
            End If

            ' `USER-PLAYLIST` rule 3 with section 9: a file that has a line BEGINNING `#EXT-X-` is an HLS media
            ' manifest and is refused whole, 0 entries. The text inside a title, an attribute or a URL does not
            ' count - a reader matches at the start of a line, never anywhere in it.
            Dim manifestLine As Integer = FindHlsManifestTagLine(lines)
            If manifestLine > 0 Then
                result.Refusals.Add(New StreamRefusal(fileName, manifestLine, "", "",
                    HlsManifestStatus & ": line " & manifestLine.ToString(Globalization.CultureInfo.InvariantCulture) &
                    " begins with " & HlsTagPrefix & ", so this is an HLS media manifest and not a playlist; 0 entries were imported", True))
                Return
            End If

            Dim currentExtinf As String = Nothing
            Dim currentExtinfLine As Integer = 0

            For lineIndex = 0 To lines.Count - 1
                Dim lineNumber As Integer = lineIndex + 1
                Dim rawLine As String = lines(lineIndex).Trim()

                If String.IsNullOrEmpty(rawLine) Then
                    Continue For
                End If

                If rawLine.StartsWith(ExtinfPrefix, StringComparison.OrdinalIgnoreCase) Then
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

        ''' <summary>
        ''' Reads the file as UTF-8 lines, holding no more than the length it was found to have in memory.
        ''' Returns Nothing - without reading a byte - when that length is over <see cref="MaxPlaylistBytes"/>.
        ''' The buffer is sized from the length that was checked, so a file that grows during the read cannot
        ''' push the read past the ceiling.
        ''' </summary>
        Private Function ReadBoundedLines(filePath As String) As List(Of String)
            Using stream As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                Dim length As Long = stream.Length
                If length > MaxPlaylistBytes Then Return Nothing

                Dim buffer(CInt(length) - 1) As Byte
                Dim filled As Integer = 0
                While filled < buffer.Length
                    Dim n As Integer = stream.Read(buffer, filled, buffer.Length - filled)
                    If n <= 0 Then Exit While
                    filled += n
                End While

                Dim lines As New List(Of String)()
                Using reader As New StreamReader(New MemoryStream(buffer, 0, filled), Encoding.UTF8, True)
                    Dim line As String = reader.ReadLine()
                    While line IsNot Nothing
                        lines.Add(line)
                        line = reader.ReadLine()
                    End While
                End Using
                Return lines
            End Using
        End Function

        ''' <summary>The 1-based number of the first line that begins with the HLS tag prefix, or 0 when none does.</summary>
        Friend Function FindHlsManifestTagLine(lines As IReadOnlyList(Of String)) As Integer
            For i As Integer = 0 To lines.Count - 1
                If lines(i).TrimStart().StartsWith(HlsTagPrefix, StringComparison.Ordinal) Then Return i + 1
            Next
            Return 0
        End Function

        ''' <summary>
        ''' Splits an `#EXTINF` record body (the text after `#EXTINF:`) at the first comma that is not inside
        ''' double quotes (`USER-PLAYLIST` section 8 item 2): `-1 group-title="Rock, Pop",Name` splits before
        ''' `Name`. Returns -1 when no such comma exists, which means the record carries no title.
        ''' </summary>
        Friend Function IndexOfTitleComma(body As String) As Integer
            Dim inQuotes As Boolean = False
            For i As Integer = 0 To body.Length - 1
                Dim c As Char = body(i)
                If c = """"c Then
                    inQuotes = Not inQuotes
                ElseIf c = ","c AndAlso Not inQuotes Then
                    Return i
                End If
            Next
            Return -1
        End Function

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
                ' The title starts after the first comma outside double quotes; what precedes it is the
                ' duration and the attributes. No such comma: no title, and the host-name fallback applies.
                Dim body As String = extinfLine.Substring(ExtinfPrefix.Length)
                Dim titleComma As Integer = IndexOfTitleComma(body)
                Dim attrPart As String = If(titleComma >= 0, body.Substring(0, titleComma), body)
                If titleComma >= 0 Then rawName = body.Substring(titleComma + 1).Trim()

                For Each am As Match In AttributeRegex.Matches(attrPart)
                    Dim key = am.Groups(1).Value.Trim()
                    Dim val = am.Groups(2).Value.Trim()
                    attributes(key) = val
                Next
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
                    Dim words As List(Of String) = LetterWords(lowerType)
                    If lowerType = "audio" OrElse words.Exists(AddressOf IsRadioWord) Then
                        declaredKind = StreamCatalogSchema.MediaKindAudio
                        Return StreamCatalogSchema.MediaKindAudio
                    ElseIf lowerType = "video" OrElse words.Exists(AddressOf IsTvWord) Then
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

        ''' <summary>The maximal runs of letters in <paramref name="text"/> (digits and punctuation separate words).</summary>
        Private Function LetterWords(text As String) As List(Of String)
            Dim words As New List(Of String)()
            Dim start As Integer = -1
            For i As Integer = 0 To text.Length
                Dim isLetter As Boolean = i < text.Length AndAlso Char.IsLetter(text(i))
                If isLetter Then
                    If start < 0 Then start = i
                ElseIf start >= 0 Then
                    words.Add(text.Substring(start, i - start))
                    start = -1
                End If
            Next
            Return words
        End Function

        ' A group title is read by WORDS, not by substring: "Latvia" contains "tv" and is not television. A word
        ' ending in "tv" (IPTV, HDTV, WebTV) or "radio" (Webradio) still counts, because those are how the
        ' groups of real playlists are written; "Radiohead" is a name and stays undeclared.
        Private Function IsTvWord(word As String) As Boolean
            Return word = "tv" OrElse word = "tvs" OrElse (word.Length > 2 AndAlso word.EndsWith("tv", StringComparison.Ordinal))
        End Function

        Private Function IsRadioWord(word As String) As Boolean
            Return word = "radio" OrElse word = "radios" OrElse (word.Length > 5 AndAlso word.EndsWith("radio", StringComparison.Ordinal))
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
