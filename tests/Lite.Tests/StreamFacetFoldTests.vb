#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports fmsl.Streams
Imports Xunit

Namespace Lite.Tests

    ''' <summary>
    ''' STREAM-BANK rule 14 (item O) and item P: the closed <c>language</c> and <c>country</c> value sets of the stream
    ''' catalogue, the catalog's golden vectors replayed against this producer's fold, the generated table source held to
    ''' the vendored files it came from, and the reader and producer held to both (ticket L0329).
    ''' The vendored files, their digests and the open question whether they are the published artifact are in
    ''' TestData\stream-facets\PROVENANCE.txt.
    ''' </summary>
    Public Class StreamFacetFoldTests
        Implements IDisposable

        Private Shared ReadOnly FacetDirectory As String =
            Path.Combine(AppContext.BaseDirectory, "TestData", "stream-facets")

        Private Shared ReadOnly Utf8NoBom As New UTF8Encoding(False)

        Private Shared ReadOnly VendoredFiles As String() = {
            "facet-language-vocabulary.tsv",
            "facet-language-aliases.tsv",
            "facet-country-assigned.tsv",
            "facet-country-aliases.tsv",
            "facet-country-names.tsv",
            "facet-golden.tsv"
        }

        Private ReadOnly testDir As String

        Public Sub New()
            testDir = Path.Combine(Path.GetTempPath(), "fms_test_facets_" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(testDir)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                If Directory.Exists(testDir) Then Directory.Delete(testDir, True)
            Catch
            End Try
        End Sub

        ' ------------------------------------------------------------------------------ provenance --

        <Fact>
        Public Sub Vendored_files_match_their_recorded_digest()
            Dim provenance As String = File.ReadAllText(Path.Combine(FacetDirectory, "PROVENANCE.txt"))
            For Each fileName As String In VendoredFiles
                Dim actual As String
                Using sha As SHA256 = SHA256.Create()
                    actual = String.Concat(sha.ComputeHash(File.ReadAllBytes(Path.Combine(FacetDirectory, fileName))).Select(Function(b) b.ToString("x2")))
                End Using
                Assert.True(provenance.Contains("  " & actual & "  " & fileName), fileName & " does not match the digest in PROVENANCE.txt")
            Next
        End Sub

        <Fact>
        Public Sub The_generated_tables_equal_the_vendored_files_row_for_row()
            Assert.Equal(SourceRows("facet-language-vocabulary.tsv", 4, "name"), StreamFacetTables.LanguageVocabulary)
            Assert.Equal(SourceRows("facet-language-aliases.tsv", 2, "alias"), StreamFacetTables.LanguageAliases)
            Assert.Equal(SourceRows("facet-country-assigned.tsv", 1, "code"), StreamFacetTables.CountryAssigned)
            Assert.Equal(SourceRows("facet-country-aliases.tsv", 2, "alias"), StreamFacetTables.CountryAliases)
            Assert.Equal(SourceRows("facet-country-names.tsv", 2, "name"), StreamFacetTables.CountryNames)
        End Sub

        ' ------------------------------------------------------------------------------ the vectors --

        ''' <summary>Item P.2: a producer proves rule 14 by replaying the catalog's vectors, every line of them.</summary>
        <Fact>
        Public Sub The_golden_vectors_replay_exactly()
            Dim failures As New List(Of String)()
            Dim languageVectors As Integer = 0
            Dim countryVectors As Integer = 0

            For Each line As String In File.ReadAllLines(Path.Combine(FacetDirectory, "facet-golden.tsv"), Utf8NoBom)
                If line.Length = 0 OrElse line.StartsWith("#", StringComparison.Ordinal) Then Continue For

                Dim f As String() = line.Split(ControlChars.Tab)
                If f.Length <> 3 Then
                    failures.Add("malformed vector line: " & line)
                    Continue For
                End If

                Dim actual As String
                Select Case f(0)
                    Case "language"
                        actual = StreamFacetFold.FoldLanguage(f(1))
                        languageVectors += 1
                    Case "country"
                        actual = StreamFacetFold.FoldCountry(f(1))
                        countryVectors += 1
                    Case Else
                        failures.Add("unknown vector kind: " & f(0))
                        Continue For
                End Select

                If Not String.Equals(actual, f(2), StringComparison.Ordinal) Then
                    failures.Add(f(0) & " [" & f(1) & "] expected [" & f(2) & "] got [" & actual & "]")
                End If
            Next

            Assert.True(languageVectors >= 90, "the replay saw only " & languageVectors & " language vectors")
            Assert.True(countryVectors >= 18, "the replay saw only " & countryVectors & " country vectors")
            Assert.True(failures.Count = 0, String.Join(Environment.NewLine, failures))
        End Sub

        ''' <summary>A fold's own output is already in the closed set, so folding it again changes nothing.</summary>
        <Fact>
        Public Sub Folded_values_are_closed_set_members_and_stable()
            For Each line As String In File.ReadAllLines(Path.Combine(FacetDirectory, "facet-golden.tsv"), Utf8NoBom)
                If line.Length = 0 OrElse line.StartsWith("#", StringComparison.Ordinal) Then Continue For
                Dim f As String() = line.Split(ControlChars.Tab)

                If f(0) = "language" Then
                    Dim folded As String = StreamFacetFold.FoldLanguage(f(1))
                    If folded.Length > 0 Then
                        Dim entries As String() = folded.Split(","c)
                        For Each entry As String In entries
                            Assert.True(StreamFacetFold.IsLanguageName(entry), "[" & f(1) & "] gave [" & entry & "], not a vocabulary name")
                        Next
                        Assert.Equal(entries.Length, entries.Distinct().Count())
                    End If
                    Assert.Equal(folded, StreamFacetFold.FoldLanguage(folded))
                Else
                    Dim folded As String = StreamFacetFold.FoldCountry(f(1))
                    If folded.Length > 0 Then Assert.True(StreamFacetFold.IsAssignedCountry(folded), "[" & f(1) & "] gave [" & folded & "], not assigned")
                    Assert.Equal(folded, StreamFacetFold.FoldCountry(folded))
                End If
            Next
        End Sub

        ' ------------------------------------------------------------------------------ the tables --

        ''' <summary>The canonical names are what the fold returns and the validator compares, so they are their own fold.</summary>
        <Fact>
        Public Sub Every_vocabulary_name_is_its_own_fold_and_the_fold_is_stable()
            For Each row As String In StreamFacetTables.LanguageVocabulary
                Dim name As String = row.Split("|"c)(0)
                Assert.Equal(name, StreamFacetFold.FoldKey(name))
            Next
            For Each table As String() In New String()() {
                StreamFacetTables.LanguageAliases, StreamFacetTables.CountryAliases, StreamFacetTables.CountryNames}
                For Each row As String In table
                    Dim once As String = StreamFacetFold.FoldKey(row.Split("|"c)(0))
                    Assert.Equal(once, StreamFacetFold.FoldKey(once))
                Next
            Next
        End Sub

        ''' <summary>Two rows whose keys are one key after the fold must say the same thing, or "first row wins" would be a hidden choice.</summary>
        <Fact>
        Public Sub No_two_keys_of_one_table_fold_together_with_different_targets()
            Dim clashes As New List(Of String)()
            For Each table As String() In New String()() {
                StreamFacetTables.LanguageAliases, StreamFacetTables.CountryAliases, StreamFacetTables.CountryNames}
                Dim targetOf As New Dictionary(Of String, String)(StringComparer.Ordinal)
                For Each row As String In table
                    Dim f As String() = row.Split("|"c)
                    Dim key As String = StreamFacetFold.FoldKey(f(0))
                    Dim seen As String = Nothing
                    If targetOf.TryGetValue(key, seen) Then
                        If seen <> f(1) Then clashes.Add("[" & f(0) & "] -> " & f(1) & " but another spelling of it -> " & seen)
                    Else
                        targetOf.Add(key, f(1))
                    End If
                Next
            Next
            Assert.True(clashes.Count = 0, String.Join(Environment.NewLine, clashes))
        End Sub

        <Fact>
        Public Sub Every_language_alias_resolves_to_its_target_when_it_stands_alone()
            Dim misses As New List(Of String)()
            For Each row As String In StreamFacetTables.LanguageAliases
                Dim f As String() = row.Split("|"c)
                Dim actual As String = StreamFacetFold.FoldLanguage(f(0))
                If actual <> f(1) Then misses.Add("alias [" & f(0) & "] gave [" & actual & "], not [" & f(1) & "]")
            Next
            Assert.True(misses.Count = 0, String.Join(Environment.NewLine, misses))
        End Sub

        <Fact>
        Public Sub Every_country_alias_and_every_unshadowed_name_resolves_to_its_code_or_to_blank()
            Dim assigned As New HashSet(Of String)(StreamFacetTables.CountryAssigned, StringComparer.Ordinal)
            Dim aliasKeys As New HashSet(Of String)(StringComparer.Ordinal)
            Dim misses As New List(Of String)()

            For Each row As String In StreamFacetTables.CountryAliases
                Dim f As String() = row.Split("|"c)
                aliasKeys.Add(StreamFacetFold.FoldKey(f(0)))
                Dim expected As String = If(assigned.Contains(f(1)), f(1), "")
                If StreamFacetFold.FoldCountry(f(0)) <> expected Then misses.Add("alias [" & f(0) & "] gave [" & StreamFacetFold.FoldCountry(f(0)) & "], not [" & expected & "]")
            Next
            For Each row As String In StreamFacetTables.CountryNames
                Dim f As String() = row.Split("|"c)
                If aliasKeys.Contains(StreamFacetFold.FoldKey(f(0))) Then Continue For
                Dim expected As String = If(assigned.Contains(f(1)), f(1), "")
                If StreamFacetFold.FoldCountry(f(0)) <> expected Then misses.Add("name [" & f(0) & "] gave [" & StreamFacetFold.FoldCountry(f(0)) & "], not [" & expected & "]")
            Next
            Assert.True(misses.Count = 0, String.Join(Environment.NewLine, misses))
        End Sub

        <Theory>
        <InlineData("대한민국", "KR")>
        Public Sub A_name_typed_composed_or_decomposed_is_one_key(raw As String, expected As String)
            Assert.Equal(expected, StreamFacetFold.FoldCountry(raw))
            Assert.Equal(expected, StreamFacetFold.FoldCountry(raw.Normalize(System.Text.NormalizationForm.FormD)))
        End Sub

        <Fact>
        Public Sub Alias_targets_are_vocabulary_names_and_no_alias_shadows_a_name()
            Dim names As New HashSet(Of String)(StreamFacetTables.LanguageVocabulary.Select(Function(r) r.Split("|"c)(0)), StringComparer.Ordinal)
            For Each row As String In StreamFacetTables.LanguageAliases
                Dim f As String() = row.Split("|"c)
                Assert.False(names.Contains(f(0)), "alias [" & f(0) & "] is itself a vocabulary name")
                For Each target As String In f(1).Split(","c)
                    Assert.True(names.Contains(target), "alias [" & f(0) & "] points at [" & target & "], not a vocabulary name")
                Next
            Next
        End Sub

        <Fact>
        Public Sub No_language_code_belongs_to_two_languages()
            Dim owner As New Dictionary(Of String, String)(StringComparer.Ordinal)
            For Each row As String In StreamFacetTables.LanguageVocabulary
                Dim f As String() = row.Split("|"c)
                Dim codes As New List(Of String)()
                codes.Add(f(1))
                codes.AddRange(f(2).Split(","c))
                codes.Add(f(3))
                For Each code As String In codes
                    If code.Length = 0 Then Continue For
                    Dim existing As String = Nothing
                    If owner.TryGetValue(code, existing) AndAlso existing <> f(0) Then
                        Assert.True(False, "code [" & code & "] belongs to [" & existing & "] and [" & f(0) & "]")
                    End If
                    owner(code) = f(0)
                Next
            Next
        End Sub

        <Fact>
        Public Sub Country_alias_targets_are_assigned_and_name_targets_are_assigned_or_not_a_country()
            Dim assigned As New HashSet(Of String)(StreamFacetTables.CountryAssigned, StringComparer.Ordinal)
            Assert.Equal(249, assigned.Count)

            For Each row As String In StreamFacetTables.CountryAliases
                Dim f As String() = row.Split("|"c)
                Assert.True(assigned.Contains(f(1)), "alias [" & f(0) & "] points at [" & f(1) & "], which is not assigned")
            Next

            ' The platform's region tables also name continents and regions (029, 419, 001) and Kosovo (XK):
            ' none of them is an assigned alpha-2 code, so the fold must make them blank.
            For Each row As String In StreamFacetTables.CountryNames
                Dim f As String() = row.Split("|"c)
                If Not assigned.Contains(f(1)) Then Assert.Equal("", StreamFacetFold.FoldCountry(f(0)))
            Next
        End Sub

        ' ------------------------------------------------------------------------------ the fold --

        <Theory>
        <InlineData("")>
        <InlineData("   ")>
        <InlineData(" , ; / | ")>
        Public Sub A_blank_cell_stays_blank(raw As String)
            Assert.Equal("", StreamFacetFold.FoldLanguage(raw))
            Assert.Equal("", StreamFacetFold.FoldCountry(raw))
        End Sub

        <Fact>
        Public Sub An_absent_value_stays_blank()
            Assert.Equal("", StreamFacetFold.FoldLanguage(Nothing))
            Assert.Equal("", StreamFacetFold.FoldCountry(Nothing))
        End Sub

        <Theory>
        <InlineData("US;CA")>
        <InlineData("US, CA")>
        <InlineData("US CA")>
        <InlineData("USA/Canada")>
        <InlineData("001")>
        <InlineData("Europe")>
        <InlineData("Kosovo")>
        Public Sub A_country_cell_that_is_not_one_assigned_code_is_blank(raw As String)
            Assert.Equal("", StreamFacetFold.FoldCountry(raw))
        End Sub

        <Theory>
        <InlineData("de", "DE")>
        <InlineData("Germany", "DE")>
        <InlineData("  germany ", "DE")>
        <InlineData("UNITED KINGDOM", "GB")>
        <InlineData("Côte d’Ivoire", "CI")>
        <InlineData("Österreich", "AT")>
        <InlineData("Россия", "RU")>
        Public Sub A_country_name_folds_case_and_diacritics_to_its_code(raw As String, expected As String)
            Assert.Equal(expected, StreamFacetFold.FoldCountry(raw))
        End Sub

        <Fact>
        Public Sub A_value_a_lone_surrogate_breaks_is_folded_as_it_stands_not_thrown_at_the_build()
            Assert.Equal("german", StreamFacetFold.FoldLanguage(ChrW(&HD800) & ", german"))
            Assert.Equal("", StreamFacetFold.FoldCountry(ChrW(&HD800) & "e"))
        End Sub

        <Fact>
        Public Sub The_validator_names_what_is_wrong_with_a_language_cell()
            Assert.Equal("", StreamCatalogValidator.DescribeLanguageCellProblem("english,german"))
            Assert.Contains("empty", StreamCatalogValidator.DescribeLanguageCellProblem("english,"))
            Assert.Contains("repeats", StreamCatalogValidator.DescribeLanguageCellProblem("english,german,english"))
            Assert.Contains("klingon", StreamCatalogValidator.DescribeLanguageCellProblem("klingon"))
            Assert.Contains("no language", StreamCatalogValidator.DescribeLanguageCellProblem("en"))
        End Sub

        ' ------------------------------------------------------------------------------ reader and producer --

        <Theory>
        <InlineData("tvg-language=""English, Gernan"" tvg-country=""UK""", "english,german", "GB")>
        <InlineData("tvg-language=""Russian"" tvg-country=""The Russian Federation""", "russian", "RU")>
        <InlineData("language=""rus; eng"" country=""ua""", "russian,english", "UA")>
        <InlineData("tvg-language=""mul"" tvg-country=""XX""", "english", "")>
        <InlineData("tvg-language=""Klingon"" tvg-country=""Atlantis""", "english", "")>
        <InlineData("tvg-language=""  "" tvg-country=""  """, "", "")>
        <InlineData("tvg-id=""x""", "", "")>
        Public Sub The_reader_folds_both_facet_attributes_to_the_closed_sets(attributes As String, expectedLanguage As String, expectedCountry As String)
            Dim row = ReadOneRow(attributes)
            Assert.Equal(expectedLanguage, row.Language)
            Assert.Equal(expectedCountry, row.Country)
        End Sub

        <Fact>
        Public Sub A_built_catalog_carries_the_folded_cells_and_passes_the_validator()
            Dim playlist As String = Path.Combine(testDir, "facets.m3u")
            File.WriteAllText(playlist,
                "#EXTM3U" & vbLf &
                "#EXTINF:-1 tvg-language=""English, Gernan"" tvg-country=""UK"",One" & vbLf &
                "https://one.example.com/live" & vbLf &
                "#EXTINF:-1 tvg-language=""sámi swedish"" tvg-country=""XX"",Two" & vbLf &
                "https://two.example.com/live" & vbLf, Encoding.UTF8)

            Dim zipPath As String = Path.Combine(testDir, "facets.zip")
            Dim built = StreamCatalogProducer.Build(playlist, zipPath)
            Assert.True(built.Success)

            Dim v11 = StreamCatalogValidator.ValidateArchive(zipPath).Verdicts.Find(Function(v) v.Id = "V11")
            Assert.NotNull(v11)
            Assert.True(v11.Passed, v11.Detail)

            Dim languages As New List(Of String)()
            Dim countries As New List(Of String)()
            Using zip As New ZipArchive(File.OpenRead(zipPath), ZipArchiveMode.Read)
                Using ms As New MemoryStream()
                    Using s = zip.GetEntry(StreamCatalogSchema.CatalogCsvEntryName).Open()
                        s.CopyTo(ms)
                    End Using
                    ms.Position = 0
                    Dim hasBom As Boolean = False
                    Dim parsed = StreamCsvWriter.ParseRfc4180(ms, hasBom)
                    For i As Integer = 1 To parsed.Count - 1
                        languages.Add(parsed(i)(10))
                        countries.Add(parsed(i)(11))
                    Next
                End Using
            End Using

            Assert.Equal(New String() {"english,german", "northern sami,swedish"}, languages.OrderBy(Function(x) x, StringComparer.Ordinal).ToArray())
            Assert.Equal(New String() {"", "GB"}, countries.OrderBy(Function(x) x, StringComparer.Ordinal).ToArray())
        End Sub

        ' ------------------------------------------------------------------------------ helpers --

        Private Function ReadOneRow(attributes As String) As StreamCatalogRow
            Dim path As String = System.IO.Path.Combine(testDir, "one.m3u")
            File.WriteAllText(path,
                "#EXTM3U" & vbLf &
                "#EXTINF:-1 " & attributes & ",Station" & vbLf &
                "https://station.example.com/live" & vbLf, Encoding.UTF8)
            Return Assert.Single(M3uPlaylistReader.ReadPath(path).Rows)
        End Function

        ''' <summary>The rows <c>tools/Export-StreamFacetTables.ps1</c> makes of a vendored file: no comment, no header, padded
        ''' to the column count, fields joined by a bar.</summary>
        Private Shared Function SourceRows(fileName As String, columns As Integer, header As String) As String()
            Dim rows As New List(Of String)()
            Dim sawHeader As Boolean = False

            For Each line As String In File.ReadAllLines(Path.Combine(FacetDirectory, fileName), Utf8NoBom)
                If line.Length = 0 OrElse line.StartsWith("#", StringComparison.Ordinal) Then Continue For

                Dim fields As String() = line.Split(ControlChars.Tab)
                If Not sawHeader Then
                    Assert.Equal(header, fields(0))
                    sawHeader = True
                    Continue For
                End If

                Assert.True(fields.Length <= columns, fileName & ": more than " & columns & " columns in " & line)
                Dim padded(columns - 1) As String
                For i As Integer = 0 To columns - 1
                    padded(i) = If(i < fields.Length, fields(i), "")
                Next
                rows.Add(String.Join("|", padded))
            Next

            Assert.True(sawHeader, fileName & ": no header row")
            Return rows.ToArray()
        End Function

    End Class

End Namespace
#End If
