#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Security.Cryptography
Imports System.Text.Json
Imports Xunit

Namespace Lite.Tests
    ' INPUT-CHORD 0.3 rules 1-3. Mouse chords and global hooks are outside this viewer.
    Public Class InputChordConformanceTests
        Private Shared ReadOnly VectorDirectory As String = Path.Combine(AppContext.BaseDirectory, "TestData", "input-chord-vectors")
        Private Const VectorFile As String = "chord-tokens.jsonl"

        <Fact>
        Public Sub The_vendored_vector_file_matches_its_recorded_digest()
            Dim provenance As String = File.ReadAllText(Path.Combine(VectorDirectory, "PROVENANCE.txt"))
            Dim actual As String = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(VectorDirectory, VectorFile)))).ToLowerInvariant()
            Assert.Contains("  " & actual & "  " & VectorFile, provenance)
        End Sub

        <Fact>
        Public Sub All_thirty_four_keyboard_rows_conform()
            Dim keyboardRows As Integer
            Dim mouseRows As Integer
            For Each line In File.ReadAllLines(Path.Combine(VectorDirectory, VectorFile))
                If String.IsNullOrWhiteSpace(line) Then Continue For
                Using doc = JsonDocument.Parse(line)
                    Dim row = doc.RootElement
                    If row.GetProperty("kind").GetString() = "mouse" Then
                        mouseRows += 1
                        Continue For
                    End If
                    keyboardRows += 1
                    Dim written As String = row.GetProperty("in").GetString()
                    Dim inert As JsonElement
                    Dim expected As String = If(row.TryGetProperty("inert", inert) AndAlso inert.GetBoolean(), String.Empty, row.GetProperty("canonical").GetString())
                    Assert.True(String.Equals(expected, CustomHotkeys.Canonical(written), StringComparison.Ordinal),
                                "Chord '" & written & "' must normalize to '" & expected & "'.")
                End Using
            Next
            Assert.Equal(34, keyboardRows)
            Assert.Equal(18, mouseRows)
        End Sub
    End Class
End Namespace
#End If