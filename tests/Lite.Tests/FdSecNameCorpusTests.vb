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
            Assert.Equal(32, count)
        End Using
    End Sub
End Class
#End If
