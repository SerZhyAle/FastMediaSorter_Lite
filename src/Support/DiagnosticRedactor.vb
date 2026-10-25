#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Text.Json

''' <summary>DIAGNOSTIC-REPORT sections 7, 8 C and 10. One instance per ordered log stream;
''' export starts at byte zero so a PEM body cannot become a raw tail after compaction.</summary>
Public NotInheritable Class DiagnosticRedactor
    Private inPrivateKey As Boolean
    Private Const Marker As String = "[REDACTED]"
    Private Const SecretNames As String = "(?:[a-z0-9_-]{0,80}(?:password|token|secret|accesspin|pairingcode|fingerprint|salt|login|serveraddress)|pass|pin|auth|api[_-]?key|authorization|x-amz-signature|x-amz-credential|wmsauthsign|hdnts|hdnea|policy|key-pair-id|user|username|exchangeserver|oldpin|newpin)"
    Private Shared ReadOnly KeyBegin As Regex = Pattern("-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----")
    Private Shared ReadOnly KeyEnd As Regex = Pattern("-----END [A-Z0-9 ]*PRIVATE KEY-----")
    Private Shared ReadOnly UserInfo As Regex = Pattern("(?<scheme>\b[a-z][a-z0-9+.-]*://)[^\s<>""']*@")
    Private Shared ReadOnly AccountPath As Regex = Pattern("(?<kind>/(?:live|movie|series)/)[^/\s]+/[^/\s]+/")
    Private Shared ReadOnly QuotedValue As Regex = Pattern("(?<prefix>[""']?" & SecretNames & "[""']?\s*[:=]\s*)(?<quote>[""'])(?:\\.|(?!\k<quote>).)*\k<quote>")
    Private Shared ReadOnly BareValue As Regex = Pattern("(?<prefix>(?<![a-z0-9_-])[""']?" & SecretNames & "[""']?\s*[:=]\s*)(?![""'\[{])[^\s&;,}\]""']+")
    Private Shared ReadOnly StructuredSecret As Regex = Pattern("[""']?" & SecretNames & "[""']?\s*[:=]\s*[\[{]")
    Private Shared ReadOnly AuthHeader As Regex = Pattern("(?<prefix>\b(?:authorization|proxy-authorization)\s*:\s*).*")
    Private Shared ReadOnly Profile As Regex = Pattern("[a-z]:[\\/]Users[\\/][^\\/\s""<>]+|/data/user/\d+/[^/\s""<>]+")
    Private Shared ReadOnly StructuredAssignment As Regex = Pattern("^(?<prefix>[a-z][a-z0-9_.-]*=)(?<value>.*)$")
    Private Shared ReadOnly SecretKey As Regex = Pattern("^(?:" & SecretNames & ")$")

    Private Shared Function Pattern(expression As String) As Regex
        Return New Regex(expression, RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))
    End Function

    Public Function RedactLine(line As String) As String
        If line Is Nothing Then Return ""
        Try
            Dim hasKeyMarker = line.IndexOf("PRIVATE KEY", StringComparison.OrdinalIgnoreCase) >= 0
            Dim begins = hasKeyMarker AndAlso KeyBegin.IsMatch(line)
            Dim ends = hasKeyMarker AndAlso KeyEnd.IsMatch(line)
            If inPrivateKey OrElse begins Then
                Dim wasInside = inPrivateKey
                inPrivateKey = Not ends
                Return If(wasInside, "", Marker)
            End If
            Dim clean = line
            Dim objectAt = clean.IndexOf("{"c)
            If objectAt >= 0 AndAlso Not clean.Contains("="c) Then
                Try
                    Using document = JsonDocument.Parse(clean.Substring(objectAt))
                        If ContainsPrivateField(document.RootElement) Then Return Marker
                    End Using
                Catch ex As JsonException
                    Return Marker
                End Try
            End If
            Dim assignment = StructuredAssignment.Match(clean)
            If assignment.Success Then
                Dim value = assignment.Groups("value").Value.Trim()
                If value.StartsWith("{"c) OrElse value.StartsWith("["c) Then
                    ' Parse property names, including escaped names, before deciding whether to export a structured value.
                    Try
                        Using document = JsonDocument.Parse(value)
                            If ContainsPrivateField(document.RootElement) Then Return assignment.Groups("prefix").Value & Marker
                        End Using
                    Catch ex As JsonException
                        Return assignment.Groups("prefix").Value & Marker
                    End Try
                ElseIf value.IndexOf("Host=", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso value.Contains(";"c) Then
                    Return assignment.Groups("prefix").Value & Marker
                End If
            End If
            If clean.Contains("://") Then clean = UserInfo.Replace(clean, "${scheme}" & Marker & "@")
            If clean.Contains("/") Then clean = AccountPath.Replace(clean, "${kind}" & Marker & "/" & Marker & "/")
            If clean.Contains("="c) OrElse clean.Contains(":"c) Then
                If StructuredSecret.IsMatch(clean) Then Return Marker
                clean = AuthHeader.Replace(clean, "${prefix}" & Marker)
                clean = QuotedValue.Replace(clean, "${prefix}${quote}" & Marker & "${quote}")
                clean = BareValue.Replace(clean, "${prefix}" & Marker)
            End If
            If clean.IndexOf("Users", StringComparison.OrdinalIgnoreCase) >= 0 OrElse clean.Contains("/data/user/") Then clean = Profile.Replace(clean, "<USER>")
            Return clean
        Catch ex As RegexMatchTimeoutException
            Return "[Diag] PATH REDACTION TIMEOUT | dropped_line_bytes=" & Encoding.UTF8.GetByteCount(line).ToString(Globalization.CultureInfo.InvariantCulture)
        End Try
    End Function

    Private Shared Function ContainsPrivateField(value As JsonElement) As Boolean
        Select Case value.ValueKind
            Case JsonValueKind.Object
                For Each member In value.EnumerateObject()
                    If SecretKey.IsMatch(member.Name) OrElse ContainsPrivateField(member.Value) Then Return True
                Next
            Case JsonValueKind.Array
                For Each member In value.EnumerateArray()
                    If ContainsPrivateField(member) Then Return True
                Next
        End Select
        Return False
    End Function

    Public Function Redact(text As String) As String
        If String.IsNullOrEmpty(text) Then Return If(text, "")
        Dim result As New StringBuilder()
        Using reader As New StringReader(text)
            Dim line = reader.ReadLine()
            While line IsNot Nothing
                If result.Length > 0 Then result.Append(vbLf)
                result.Append(RedactLine(line))
                line = reader.ReadLine()
            End While
        End Using
        Return result.ToString()
    End Function

    Public Shared Function Clean(text As String) As String
        Return New DiagnosticRedactor().Redact(text)
    End Function
End Class

''' <summary>Trace.Write fragments are buffered until a complete line exists; flushing cannot expose a partial credential.</summary>
Friend NotInheritable Class DiagnosticTextWriter
    Inherits TextWriter
    Private ReadOnly inner As TextWriter
    Private ReadOnly redactor As DiagnosticRedactor
    Private ReadOnly gate As Object
    Private ReadOnly pending As New StringBuilder()

    Friend Sub New(inner As TextWriter, redactor As DiagnosticRedactor, gate As Object)
        Me.inner = inner
        Me.redactor = redactor
        Me.gate = gate
    End Sub

    Public Overrides ReadOnly Property Encoding As Encoding
        Get
            Return inner.Encoding
        End Get
    End Property

    Public Overrides Sub Write(value As String)
        If value Is Nothing Then Return
        SyncLock gate
            For Each c In value
                If c = ChrW(10) Then
                    Dim clean = redactor.RedactLine(pending.ToString().TrimEnd(ChrW(13)))
                    inner.WriteLine(If(dropping, "[Diag] LOG OMITTED | reason=oversized_untrusted | source_bytes=" & droppedBytes.ToString(Globalization.CultureInfo.InvariantCulture), clean))
                    pending.Clear()
                    dropping = False
                    droppedBytes = 0
                Else
                    pending.Append(c)
                    droppedBytes += Encoding.UTF8.GetByteCount(c.ToString())
                    If pending.Length >= 65536 Then
                        ' Preserve PEM state, including a marker split between fragments, without retaining an unbounded line.
                        redactor.RedactLine(pending.ToString())
                        Dim boundary = pending.ToString(pending.Length - 80, 80)
                        pending.Clear()
                        pending.Append(boundary)
                        dropping = True
                    End If
                End If
            Next
        End SyncLock
    End Sub

    Private dropping As Boolean
    Private droppedBytes As Long

    Public Overrides Sub Write(value As Char)
        Write(value.ToString())
    End Sub

    Public Overrides Sub WriteLine()
        Write(vbLf)
    End Sub

    Public Overrides Sub WriteLine(value As String)
        Write(If(value, "") & vbLf)
    End Sub

    Public Overrides Sub Flush()
        SyncLock gate
            inner.Flush()
        End SyncLock
    End Sub
End Class
#End If
