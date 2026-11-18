#If Not NETFRAMEWORK Then
Option Strict On
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text.Json
Imports Xunit

Public Class FdSecNameCorpusTests
    Private Shared ReadOnly CorpusDirectory As String = Path.Combine(AppContext.BaseDirectory, "TestData", "fdsec-vectors", "names")

    <Fact>
    Public Sub The_catalog_suite_one_names_replay_with_the_reference_credentials_and_bytes()
        Dim manifest = File.ReadAllLines(Path.Combine(CorpusDirectory, "PROVENANCE.txt"))
        Using document = JsonDocument.Parse(File.ReadAllText(Path.Combine(CorpusDirectory, "vectors.json")))
            Dim count = 0
            For Each row In document.RootElement.GetProperty("cases").EnumerateArray()
                If row.GetProperty("suite").GetInt32() <> 1 Then Continue For
                Dim filename = row.GetProperty("file").GetString()
                Dim bytes = File.ReadAllBytes(Path.Combine(CorpusDirectory, filename))
                Dim digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                Assert.Contains("sha256 " & filename & " " & digest, manifest)
                Dim credential = row.GetProperty("credential").GetString()
                If row.GetProperty("expect").GetString() = "opens" Then
                    Using source As New MemoryStream(bytes), container = FdSecContainer.Open(source, credential)
                        Assert.Equal(row.GetProperty("sealed_name").GetString(), container.Metadata.Name)
                        Using content = container.OpenContentStream(), recovered As New MemoryStream()
                            content.CopyTo(recovered)
                            Assert.Equal(Convert.FromHexString(row.GetProperty("content").GetString()), recovered.ToArray())
                        End Using
                    End Using
                Else
                    Using source As New MemoryStream(bytes)
                        Dim failure = Assert.Throws(Of FdSecException)(Sub()
                                                                          Using container = FdSecContainer.Open(source, credential)
                                                                          End Using
                                                                      End Sub)
                        Assert.Equal(FdSecOutcome.Damaged, failure.Outcome)
                    End Using
                End If
                count += 1
            Next
            ' 34 since FDSEC-FORMAT 1.6 section 23: the 32 boundary names plus s1-valid-nfc and s1-refused-not-nfc.
            Assert.Equal(34, count)
        End Using
    End Sub

    <Fact>
    Public Sub A_sealed_name_that_is_not_in_NFC_is_damaged_and_its_NFC_twin_opens()
        ' FDSEC-FORMAT 1.6 section 23: the same letter as U+0065 U+0301 is damaged, as U+00E9 it is valid.
        Dim manifest = File.ReadAllLines(Path.Combine(CorpusDirectory, "PROVENANCE.txt"))
        Using document = JsonDocument.Parse(File.ReadAllText(Path.Combine(CorpusDirectory, "vectors.json")))
            Dim rows = document.RootElement.GetProperty("cases").EnumerateArray().
                Where(Function(r) r.GetProperty("suite").GetInt32() = 1 AndAlso r.GetProperty("id").GetString().EndsWith("nfc", StringComparison.Ordinal)).ToList()
            Assert.Equal(2, rows.Count)
            For Each row In rows
                Dim filename = row.GetProperty("file").GetString()
                Dim bytes = File.ReadAllBytes(Path.Combine(CorpusDirectory, filename))
                Assert.Contains("sha256 " & filename & " " & Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), manifest)
                Dim credential = row.GetProperty("credential").GetString()
                Using source As New MemoryStream(bytes)
                    If row.GetProperty("expect").GetString() = "opens" Then
                        Using container = FdSecContainer.Open(source, credential)
                            Assert.Equal(row.GetProperty("sealed_name").GetString(), container.Metadata.Name)
                            Assert.True(container.Metadata.Name.IsNormalized(System.Text.NormalizationForm.FormC))
                        End Using
                    Else
                        Dim failure = Assert.Throws(Of FdSecException)(Sub()
                                                                          Using container = FdSecContainer.Open(source, credential)
                                                                          End Using
                                                                      End Sub)
                        Assert.Equal(FdSecOutcome.Damaged, failure.Outcome)
                    End If
                End Using
            Next
        End Using
    End Sub
End Class
#End If
