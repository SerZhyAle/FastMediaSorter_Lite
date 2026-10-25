#If Not NETFRAMEWORK Then
Option Strict On
Imports System.IO
Imports System.Text
Imports System.Security.Cryptography
Imports Xunit

Public Class DiagnosticRedactorTests
    <Fact>
    Public Sub Catalog_structured_values_match_one_of_the_two_permitted_outputs()
        Dim path = IO.Path.Combine(AppContext.BaseDirectory, "TestData", "diagnostic-vectors", "redaction-structured-value.txt")
        Assert.Equal("1B166E64137C4577EDD7F83CFC499DDB18DA30567723F7D74321038526180F2D", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
        Dim actual As String = Nothing
        Dim allowed As New List(Of String)()
        Dim count = 0
        For Each line In File.ReadAllLines(path).Concat({"INPUT="})
            If line.StartsWith("INPUT=", StringComparison.Ordinal) Then
                If actual IsNot Nothing Then
                    Assert.Contains(actual, allowed)
                    count += 1
                End If
                actual = DiagnosticRedactor.Clean(line.Substring(6))
                allowed.Clear()
            ElseIf line.StartsWith("EXPECTED-FIELD=", StringComparison.Ordinal) OrElse line.StartsWith("EXPECTED-DROP=", StringComparison.Ordinal) Then
                allowed.Add(line.Substring(line.IndexOf("="c) + 1))
            End If
        Next
        Assert.Equal(3, count)
    End Sub
    <Theory>
    <InlineData("https://example.test/p?TOKEN=NEEDLE&x=1")>
    <InlineData("https://example.test/p?x=1&amp;X-Amz-Signature=NEEDLE")>
    <InlineData("https://example.test/live/NEEDLE/secret/17.ts")>
    <InlineData("http://user:NEEDLE/with?hash#inside@example.test/play")>
    <InlineData("Authorization: Bearer NEEDLE secret")>
    <InlineData("deviceToken=NEEDLE pairingCode=NEEDLE certFingerprint=NEEDLE")>
    <InlineData("exchangeLogin=NEEDLE serverAddress=NEEDLE oldPin=NEEDLE newPin=NEEDLE")>
    <InlineData("folderParams={""folders"":[{""accessPin"":""NEEDLE""}]}")>
    <InlineData("source=Host=example.test;User=guest;Password=NEEDLE;Timeout=15")>
    Public Sub Credential_shapes_and_exchange_values_are_redacted(text As String)
        Dim clean = DiagnosticRedactor.Clean(text)
        Assert.DoesNotContain("NEEDLE", clean)
        Assert.Contains("[REDACTED]", clean)
    End Sub

    <Fact>
    Public Sub A_private_key_is_removed_across_separate_sink_calls_and_following_lines_survive()
        Dim redactor As New DiagnosticRedactor()
        Assert.Equal("[REDACTED]", redactor.Redact("prefix -----BEGIN PRIVATE KEY-----"))
        Assert.Equal("", redactor.Redact("NEEDLE"))
        Assert.Equal("", redactor.Redact("-----END PRIVATE KEY-----"))
        Assert.Equal("Exception: ordinary failure", redactor.Redact("Exception: ordinary failure"))
    End Sub

    <Fact>
    Public Sub Redaction_precedes_compaction_even_when_the_raw_tail_would_start_inside_a_key()
        Dim raw = "safe head" & vbLf & "-----BEGIN RSA PRIVATE KEY-----" & vbLf &
            String.Concat(Enumerable.Repeat("NEEDLE key bytes" & vbLf, 180000)) &
            "-----END RSA PRIVATE KEY-----" & vbLf & "Exception: safe after key" & vbLf
        Using source As New MemoryStream(Encoding.UTF8.GetBytes(raw))
            Dim compacted = LogPackage.CompactRedacted(source)
            Dim clean = Encoding.UTF8.GetString(compacted.HeadBytes) & Encoding.UTF8.GetString(compacted.TailBytes)
            Assert.DoesNotContain("NEEDLE", clean)
            Assert.DoesNotContain("PRIVATE KEY", clean)
            Assert.Contains("Exception: safe after key", clean)
            Assert.Equal(0, compacted.DroppedMiddleBytes)
        End Using
    End Sub

    <Fact>
    Public Sub An_unterminated_private_key_never_leaves_its_body_in_the_archive()
        Using source As New MemoryStream(Encoding.UTF8.GetBytes("-----BEGIN OPENSSH PRIVATE KEY-----" & vbLf & "NEEDLE"))
            Dim compacted = LogPackage.CompactRedacted(source)
            Assert.DoesNotContain("NEEDLE", Encoding.UTF8.GetString(compacted.HeadBytes))
        End Using
    End Sub

    <Fact>
    Public Sub Trace_fragments_and_flushes_do_not_bypass_the_sink_redactor()
        Using output As New StringWriter()
            Dim writer As New DiagnosticTextWriter(output, New DiagnosticRedactor(), New Object())
            writer.Write("deviceToken=")
            writer.Flush()
            Assert.Equal("", output.ToString())
            writer.WriteLine("NEEDLE")
            Assert.DoesNotContain("NEEDLE", output.ToString())
            Assert.Contains("[REDACTED]", output.ToString())
        End Using
    End Sub
End Class
#End If
