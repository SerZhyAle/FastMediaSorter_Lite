#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text

Namespace Streams

    ''' <summary>
    ''' Report generator formatting run results and V1..V12 validation verdicts for stream-catalog.zip.
    ''' </summary>
    Friend Class StreamCatalogReport

        Public Property Mode As String = "BUILD"
        Public Property InputPath As String = String.Empty
        Public Property ArchivePath As String = String.Empty
        Public Property GeneratedAtUtc As DateTime = DateTime.UtcNow
        Public Property TotalEntriesFound As Integer = 0
        Public Property ShippedRowCount As Integer = 0
        Public Property Refusals As New List(Of StreamRefusal)()
        Public Property Folds As New List(Of StreamRefusal)()
        Public Property UnrecognizedKinds As New List(Of StreamCatalogRow)()
        Public Property ValidationResult As StreamValidationResult

        Public ReadOnly Property Success As Boolean
            Get
                If ValidationResult Is Nothing Then Return False
                Return ValidationResult.Passed
            End Get
        End Property

        Public Function FormatReportText() As String
            Dim sb As New StringBuilder()

            sb.AppendLine("================================================================================")
            sb.AppendLine("Fast Media Sorter - Stream Catalog Report")
            sb.AppendLine("Contract: " & StreamCatalogSchema.ContractTag)
            sb.AppendLine("Mode:     " & Mode)
            sb.AppendLine("Date:     " & GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm:ss") & " UTC")
            sb.AppendLine("Input:    " & If(String.IsNullOrEmpty(InputPath), "(none)", InputPath))
            sb.AppendLine("Archive:  " & If(String.IsNullOrEmpty(ArchivePath), "(none)", ArchivePath))
            If ValidationResult IsNot Nothing Then
                sb.AppendLine("Size:     " & ValidationResult.ArchiveSizeBytes & " bytes")
            End If
            sb.AppendLine("================================================================================")
            sb.AppendLine()

            ' Summary counts
            sb.AppendLine("--- SUMMARY ---")
            sb.AppendLine("Total entries found:     " & TotalEntriesFound)
            sb.AppendLine("Folded duplicates:       " & Folds.Count)
            sb.AppendLine("Refused entries:         " & Refusals.Count)
            sb.AppendLine("Shipped catalog rows:    " & ShippedRowCount)
            sb.AppendLine()

            ' Refusals
            If Refusals.Count > 0 Then
                sb.AppendLine("--- REFUSED ENTRIES (" & Refusals.Count & ") ---")
                For Each r In Refusals
                    Dim loc = r.SourceFile & If(r.SourceLine > 0, ":" & r.SourceLine.ToString(), "")
                    sb.AppendLine("  [" & loc & "] " & r.Reason)
                    If Not String.IsNullOrEmpty(r.RawName) Then
                        sb.AppendLine("    Name: " & r.RawName)
                    End If
                    If Not String.IsNullOrEmpty(r.RawUrl) Then
                        sb.AppendLine("    URL:  " & r.RawUrl)
                    End If
                Next
                sb.AppendLine()
            End If

            ' Folds
            If Folds.Count > 0 Then
                sb.AppendLine("--- FOLDED DUPLICATES (" & Folds.Count & ") ---")
                For Each f In Folds
                    Dim loc = f.SourceFile & If(f.SourceLine > 0, ":" & f.SourceLine.ToString(), "")
                    sb.AppendLine("  [" & loc & "] Folded to: " & f.CanonicalUrl)
                    sb.AppendLine("    Raw: " & f.RawUrl)
                Next
                sb.AppendLine()
            End If

            ' Unrecognized declared kinds
            If UnrecognizedKinds.Count > 0 Then
                sb.AppendLine("--- UNRECOGNIZED DECLARED KINDS (" & UnrecognizedKinds.Count & ") ---")
                For Each uk In UnrecognizedKinds
                    Dim loc = uk.SourceFile & If(uk.SourceLine > 0, ":" & uk.SourceLine.ToString(), "")
                    sb.AppendLine("  [" & loc & "] Declared: '" & uk.UnrecognizedDeclaredKind & "' -> degraded to classifier: " & uk.MediaKind)
                Next
                sb.AppendLine()
            End If

            ' Validation verdicts
            If ValidationResult IsNot Nothing Then
                sb.AppendLine("--- VALIDATION VERDICTS (V1..V12) ---")
                Dim passedCount As Integer = 0
                For Each v In ValidationResult.Verdicts
                    Dim statusStr = If(v.Passed, "[PASS]", "[FAIL]")
                    If v.Passed Then passedCount += 1
                    sb.AppendLine(statusStr & " " & v.Id & ": " & v.Description)
                    If Not v.Passed AndAlso Not String.IsNullOrEmpty(v.Detail) Then
                        sb.AppendLine("       Reason: " & v.Detail)
                    End If
                Next
                sb.AppendLine()

                If ValidationResult.Passed Then
                    sb.AppendLine("FINAL VERDICT: PASS (" & passedCount & "/" & ValidationResult.Verdicts.Count & " checks passed)")
                Else
                    Dim failedIds As New List(Of String)()
                    For Each failedVerdict In ValidationResult.FailedVerdicts
                        failedIds.Add(failedVerdict.Id)
                    Next
                    sb.AppendLine("FINAL VERDICT: FAIL (failed checks: " & String.Join(", ", failedIds) & ")")
                End If
            End If

            sb.AppendLine("================================================================================")
            Return sb.ToString()
        End Function

        Public Sub WriteToFile(reportFilePath As String)
            If String.IsNullOrWhiteSpace(reportFilePath) Then Return
            Dim dir = Path.GetDirectoryName(reportFilePath)
            If Not String.IsNullOrEmpty(dir) AndAlso Not Directory.Exists(dir) Then
                Directory.CreateDirectory(dir)
            End If
            File.WriteAllText(reportFilePath, FormatReportText(), Encoding.UTF8)
        End Sub

    End Class

End Namespace
#End If
