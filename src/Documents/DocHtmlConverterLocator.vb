#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Diagnostics
Imports System.IO
Imports System.Text

Public Enum DocHtmlConverterState
    ''' <summary>No converter was found, or the chosen file does not exist.</summary>
    Missing
    ''' <summary>A file is there, but it did not start or did not answer the version query in time.</summary>
    NotRunnable
    Ready
End Enum

''' <summary>What a readiness check found. <see cref="VersionText"/> is the producer's own first line, shown as text
''' only: its format is not a contract yet (specification 276 section 2.4).</summary>
Public NotInheritable Class DocHtmlConverterProbe
    Public Property State As DocHtmlConverterState
    Public Property ExePath As String = ""
    Public Property VersionText As String = ""
    ''' <summary>True when the path came from the user's explicit choice rather than discovery.</summary>
    Public Property FromExplicitChoice As Boolean
End Class

''' <summary>Specification 276 section 7.1: discovery for the document command. It reads the frozen image-route
''' discovery (<c>DocHtmlTranslate.FindExe</c>) without changing it, adds the explicit location the user can choose,
''' and probes <c>-version</c>. Call <see cref="Probe"/> off the UI thread.</summary>
Public NotInheritable Class DocHtmlConverterLocator
    Public Const VersionTimeoutMs As Integer = 5000
    Public Const VersionTextMaxLength As Integer = 120
    Private Const OutputReadLimit As Integer = 4096

    Private Sub New()
    End Sub

    ''' <summary>An explicit location is accepted only as an absolute path whose file name is the converter's
    ''' command-line executable: the GUI or any other program is never launched with job arguments.</summary>
    Public Shared Function IsAcceptableExplicitPath(path As String) As Boolean
        If Not DocHtmlJobArguments.IsAbsolutePath(path) Then Return False
        Dim name As String
        Try
            name = IO.Path.GetFileName(path)
        Catch ex As ArgumentException
            Return False
        End Try
        Return String.Equals(name, DocHtmlTranslate.ExeFileName, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>The first non-empty line of the version output, control characters dropped and length bounded.</summary>
    Public Shared Function VersionLine(output As String) As String
        If String.IsNullOrEmpty(output) Then Return ""
        For Each raw As String In output.Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
            Dim clean As New StringBuilder()
            For Each ch As Char In raw
                If Not Char.IsControl(ch) Then clean.Append(ch)
            Next
            Dim line As String = clean.ToString().Trim()
            If line.Length = 0 Then Continue For
            If line.Length > VersionTextMaxLength Then line = line.Substring(0, VersionTextMaxLength)
            Return line
        Next
        Return ""
    End Function

    ''' <summary>Finds the converter (the explicit location when one is set, otherwise discovery) and asks it for its
    ''' version. A set but unusable explicit location is reported as such; discovery does not silently replace it.</summary>
    Public Shared Function Probe(explicitLocation As String) As DocHtmlConverterProbe
        Dim result As New DocHtmlConverterProbe()
        Dim chosen As String = If(explicitLocation, "").Trim()
        If chosen.Length > 0 Then
            result.FromExplicitChoice = True
            result.ExePath = chosen
            If Not IsAcceptableExplicitPath(chosen) OrElse Not File.Exists(chosen) Then
                result.State = DocHtmlConverterState.Missing
                Return result
            End If
        Else
            result.ExePath = DocHtmlTranslate.FindExe()
            If result.ExePath.Length = 0 Then
                result.State = DocHtmlConverterState.Missing
                Return result
            End If
        End If
        Dim version As String = ""
        result.State = If(TryReadVersion(result.ExePath, version), DocHtmlConverterState.Ready, DocHtmlConverterState.NotRunnable)
        result.VersionText = version
        Return result
    End Function

    ''' <summary>Runs <c>-version</c> as a standalone query (never part of a job), reads a bounded amount of its
    ''' output and waits a bounded time. Only the process started here is ever stopped.</summary>
    Private Shared Function TryReadVersion(exe As String, ByRef version As String) As Boolean
        version = ""
        Dim psi As New ProcessStartInfo(exe) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .WorkingDirectory = Path.GetTempPath()}
        psi.ArgumentList.Add("-version")
        Try
            Using proc As Process = Process.Start(psi)
                If proc Is Nothing Then Return False
                proc.StandardError.ReadToEndAsync()
                Dim buffer(OutputReadLimit - 1) As Char
                Dim reading = proc.StandardOutput.ReadBlockAsync(buffer, 0, buffer.Length)
                If Not proc.WaitForExit(VersionTimeoutMs) Then
                    Try
                        proc.Kill(entireProcessTree:=True)
                    Catch ex As InvalidOperationException
                    Catch ex As ComponentModel.Win32Exception
                    End Try
                    Return False
                End If
                Dim count As Integer = If(reading.Wait(1000), reading.Result, 0)
                version = VersionLine(New String(buffer, 0, count))
                Return proc.ExitCode = 0 AndAlso version.Length > 0
            End Using
        Catch ex As ComponentModel.Win32Exception
            AppFileLogger.LogException("doc-html-translate version query", ex)
            Return False
        Catch ex As InvalidOperationException
            AppFileLogger.LogException("doc-html-translate version query", ex)
            Return False
        Catch ex As IOException
            AppFileLogger.LogException("doc-html-translate version query", ex)
            Return False
        End Try
    End Function
End Class
#End If
