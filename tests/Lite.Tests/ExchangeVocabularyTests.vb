#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Text.RegularExpressions
#If COMPANION_TESTS Then
Imports FastMediaSorterCompanion
#End If
Imports Xunit

''' <summary>Specification 233: both consumers read the same DEVICE-EXCHANGE dictionary fixture.</summary>
Public Class ExchangeVocabularyTests
    Private Shared Function Root() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "tools", "product-vocabulary.json")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("Repository sources are required for the surface checks")
    End Function

    <Fact>
    Public Sub Every_visible_dictionary_term_matches_English_Russian_and_Ukrainian()
        Using doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "tests", "Lite.Tests", "TestData", "exchange-terms", "dictionary.json")))
            Assert.Contains("DEVICE-EXCHANGE 0.27", doc.RootElement.GetProperty("contract").GetString())
            Dim count As Integer = 0
            For Each term In doc.RootElement.GetProperty("terms").EnumerateArray()
                If term.GetProperty("term").GetString() = "relay" Then
                    Assert.True(term.GetProperty("internalOnly").GetBoolean())
                    Continue For
                End If
                Dim key = term.GetProperty("key").GetString()
                Dim values = Localization.All.Single(Function(kv) kv.Key = key).Value
                Dim expectedEn = term.GetProperty("en").GetString()
                Dim expectedRu = term.GetProperty("ru").GetString()
                Dim expectedUk = term.GetProperty("uk").GetString()
                If term.GetProperty("term").GetString() = "Drive channel" Then
                    expectedEn = "via Google Drive"
                    expectedRu = "через Google Drive"
                    expectedUk = "через Google Drive"
                    Dim shortValues = Localization.All.Single(Function(kv) kv.Key = "через GD").Value
                    Assert.Equal("via GD", shortValues(0))
                    Assert.Equal("через GD", shortValues(1))
                End If
                Assert.Equal(expectedRu.ToLowerInvariant(), key.ToLowerInvariant())
                Assert.Equal(expectedEn.ToLowerInvariant(), values(0).ToLowerInvariant())
                Assert.Equal(expectedUk.ToLowerInvariant(), values(1).ToLowerInvariant())
                Assert.Equal(12, values.Length)
                Assert.All(values, Sub(value) Assert.False(String.IsNullOrWhiteSpace(value)))
                count += 1
            Next
            Assert.Equal(18, count)
        End Using
    End Sub

    <Fact>
    Public Sub Forbidden_forms_fail_the_gate_patterns_and_network_words_remain_allowed()
        Using doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "tools", "product-vocabulary.json")))
            For Each rule In doc.RootElement.EnumerateArray()
                Dim rx As New Regex(rule.GetProperty("pattern").GetString())
                For Each example In rule.GetProperty("forbidden").EnumerateArray()
                    Assert.Matches(rx, example.GetString())
                Next
                For Each example In rule.GetProperty("allowed").EnumerateArray()
                    Assert.DoesNotMatch(rx, example.GetString())
                Next
                For Each entry In Localization.All
                    Assert.DoesNotMatch(rx, entry.Key)
                    Assert.DoesNotMatch(rx, entry.Value(0))
                    Assert.DoesNotMatch(rx, entry.Value(1))
                Next
            Next
        End Using
    End Sub

    <Fact>
    Public Sub Store_descriptions_omit_the_platform_suffix_and_keep_the_reserved_title()
        Dim csv = File.ReadAllText(Path.Combine(Root(), "publishing", "store", "listingData.csv"))
        Assert.Matches("(?m)^Title,\d+,Text,,FastMediaSorter LITE,", csv)
        Assert.DoesNotMatch("Fast Media Sorter &\s+Sharing for\s+Windows", csv)
        Dim manifest = File.ReadAllText(Path.Combine(Root(), "publishing", "msix", "AppxManifest.xml"))
        Assert.Contains("<DisplayName>FastMediaSorter LITE</DisplayName>", manifest)
        Assert.Contains("DisplayName=""FastMediaSorter LITE""", manifest)
    End Sub
End Class
#End If
