Option Strict On

Imports System.ComponentModel
Imports System.Diagnostics

''' <summary>
''' The one door the exchange server's elevated helpers go through (tickets 143, 144): a short-lived
''' PowerShell run of a bundled helper script with a visible UAC prompt (<c>runas</c>), exactly the shape
''' of <see cref="ServerFeatures"/> and <see cref="ServiceControl"/>. The Share Manager itself stays
''' non-elevated; nothing here is reachable from a phone request or an IPC command.
'''
''' The launch is a seam (<see cref="Launcher"/>) so the tests prove the arguments and the exit-code
''' mapping without ever raising a UAC prompt or touching the machine.
''' </summary>
Friend Module ExchangeElevation

    ''' <summary>Win32 ERROR_CANCELLED: the person said No to the UAC prompt.</summary>
    Friend Const ErrorCancelled As Integer = 1223

    ''' <summary>The result of one elevated run, kept apart from "it failed".</summary>
    Friend Enum ElevatedOutcome
        ''' <summary>The helper ran and exited 0 - the only success.</summary>
        Succeeded
        ''' <summary>The UAC prompt was declined: nothing happened.</summary>
        Declined
        ''' <summary>The helper ran and reported a non-zero code, or it could not be launched.</summary>
        Failed
        ''' <summary>The helper script is not on disk - the component is a bare copy.</summary>
        Unavailable
    End Enum

    ''' <summary>The launch seam: (file, arguments) to exit code. Throws Win32Exception 1223 on a declined
    ''' prompt, like Process.Start with Verb = runas does. Only tests replace it.</summary>
    Friend Launcher As Func(Of String, String, Integer) = AddressOf LaunchElevated

    Private Function LaunchElevated(file As String, arguments As String) As Integer
        Dim psi As New ProcessStartInfo(file, arguments) With {
            .UseShellExecute = True,        ' required for Verb = runas
            .Verb = "runas",                ' the single UAC prompt
            .WindowStyle = ProcessWindowStyle.Hidden}
        Using p As Process = Process.Start(psi)
            If p Is Nothing Then Return -1
            p.WaitForExit()
            Return p.ExitCode
        End Using
    End Function

    ''' <summary>The argument string of "powershell -File script ...": the script and every value quoted.
    ''' A value that carries a quote, a control character or a pipe-style separator is refused by
    ''' <see cref="IsSafeValue"/> before it gets here.</summary>
    Friend Function BuildScriptArguments(scriptPath As String, ParamArray pairs As String()) As String
        Dim sb As New System.Text.StringBuilder("-NoProfile -ExecutionPolicy Bypass -File """ & scriptPath & """")
        Dim i As Integer = 0
        While i < pairs.Length
            Dim name As String = pairs(i)
            sb.Append(" ").Append(name)
            If i + 1 < pairs.Length AndAlso Not pairs(i + 1).StartsWith("-", StringComparison.Ordinal) Then
                sb.Append(" """).Append(pairs(i + 1)).Append(""""c)
                i += 2
            Else
                i += 1
            End If
        End While
        Return sb.ToString()
    End Function

    ''' <summary>A path or an identifier is safe to put between quotes on the helper's command line: no quote
    ''' (it would end the argument), no control character, and not empty.</summary>
    Friend Function IsSafeValue(value As String) As Boolean
        If String.IsNullOrWhiteSpace(value) Then Return False
        For Each c As Char In value
            If c = """"c OrElse Char.IsControl(c) Then Return False
        Next
        Return True
    End Function

    ''' <summary>Runs the helper script elevated and maps the result. <paramref name="what"/> is only the
    ''' companion.log label of the action.</summary>
    Friend Function RunHelper(what As String, scriptPath As String, ByRef exitCode As Integer, ParamArray pairs As String()) As ElevatedOutcome
        exitCode = 0
        If String.IsNullOrEmpty(scriptPath) OrElse Not IO.File.Exists(scriptPath) Then Return ElevatedOutcome.Unavailable
        If Not IsSafeValue(scriptPath) Then Return ElevatedOutcome.Failed
        For i As Integer = 0 To pairs.Length - 1
            If Not pairs(i).StartsWith("-", StringComparison.Ordinal) AndAlso Not IsSafeValue(pairs(i)) Then Return ElevatedOutcome.Failed
        Next
        Try
            exitCode = Launcher("powershell.exe", BuildScriptArguments(scriptPath, pairs))
        Catch ex As Win32Exception
            If ex.NativeErrorCode = ErrorCancelled Then Return ElevatedOutcome.Declined
            AppFileLogger.LogException(what, ex)
            Return ElevatedOutcome.Failed
        Catch ex As Exception
            AppFileLogger.LogException(what, ex)
            Return ElevatedOutcome.Failed
        End Try
        If exitCode <> 0 Then
            AppFileLogger.WriteLine(what & " - helper exit code " & exitCode.ToString(Globalization.CultureInfo.InvariantCulture))
            Return ElevatedOutcome.Failed
        End If
        Return ElevatedOutcome.Succeeded
    End Function

End Module
