Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Globalization

''' <summary>
''' Pure calculation and formatting rules for the loading badge (percentage, remaining time estimate).
''' Recalculated once per second to prevent flickering during network jitter.
''' </summary>
Friend Module LoadingIndicatorRules

    ''' <summary>Computes the percentage (0 to 100) from loaded and total bytes.</summary>
    Friend Function ComputePercent(loadedBytes As Long, totalBytes As Long) As Integer
        If totalBytes <= 0 Then Return 0
        If loadedBytes <= 0 Then Return 0
        If loadedBytes >= totalBytes Then Return 100
        Dim pct As Integer = CInt(Math.Floor((Math.Min(loadedBytes, totalBytes) * 100.0) / totalBytes))
        Return Math.Max(0, Math.Min(100, pct))
    End Function

    ''' <summary>Estimates the remaining seconds, or returns -1 if unknown / cannot be estimated.</summary>
    Friend Function EstimateRemainingSeconds(loadedBytes As Long, totalBytes As Long, elapsedSeconds As Double) As Integer
        If totalBytes <= 0 OrElse loadedBytes <= 0 Then Return -1
        If loadedBytes >= totalBytes Then Return 0
        If elapsedSeconds < 1.0 Then Return -1
        Dim speed As Double = loadedBytes / elapsedSeconds
        If speed <= 0 Then Return -1
        Dim remainingBytes As Long = Math.Max(0L, totalBytes - loadedBytes)
        Return CInt(Math.Round(remainingBytes / speed))
    End Function

    ''' <summary>Formats the remaining time string, e.g. "~12 с" or "~1:15".</summary>
    Friend Function FormatRemainingTime(remainingSeconds As Integer) As String
        If remainingSeconds < 0 Then Return ""
        If remainingSeconds < 60 Then
            Return "~" & Localization.TF("{0} с", remainingSeconds.ToString(CultureInfo.InvariantCulture))
        ElseIf remainingSeconds < 3600 Then
            Dim m As Integer = remainingSeconds \ 60
            Dim s As Integer = remainingSeconds Mod 60
            Return String.Format(CultureInfo.InvariantCulture, "~{0}:{1:D2}", m, s)
        Else
            Dim h As Integer = remainingSeconds \ 3600
            Dim m As Integer = (remainingSeconds Mod 3600) \ 60
            Dim s As Integer = remainingSeconds Mod 60
            Return String.Format(CultureInfo.InvariantCulture, "~{0}:{1:D2}:{2:D2}", h, m, s)
        End If
    End Function

    ''' <summary>Binary units, like the file-size column in the status bar: KiB/MiB are
    ''' powers of 1024 by definition, so the number and the unit come from the same
    ''' system.</summary>
    Friend Function FormatLoadingSize(sizeBytes As Long) As String
        If sizeBytes >= 1024L * 1024L Then
            Return Localization.TF("{0} МиБ", (sizeBytes / (1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture))
        ElseIf sizeBytes >= 1024L Then
            Return Localization.TF("{0} КиБ", (sizeBytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture))
        End If
        Return Localization.TF("{0} Б", sizeBytes.ToString(CultureInfo.InvariantCulture))
    End Function

    ''' <summary>
    ''' Updates cached percent and remaining seconds once per second.
    ''' </summary>
    Friend Sub UpdateMetricsOncePerSecond(loadedBytes As Long,
                                          totalBytes As Long,
                                          elapsed As TimeSpan,
                                          ByRef lastSec As Integer,
                                          ByRef cachedPercent As Integer,
                                          ByRef cachedRemainingSec As Integer)
        Dim currentSec As Integer = CInt(Math.Floor(elapsed.TotalSeconds))
        If currentSec <> lastSec Then
            lastSec = currentSec
            cachedPercent = ComputePercent(loadedBytes, totalBytes)
            cachedRemainingSec = EstimateRemainingSeconds(loadedBytes, totalBytes, elapsed.TotalSeconds)
        End If
    End Sub

End Module

''' <summary>
''' Lightweight progress reporter that invokes a callback immediately on the reporting thread,
''' avoiding synchronization context delays.
''' </summary>
Friend NotInheritable Class ActionProgress(Of T)
    Implements IProgress(Of T)

    Private ReadOnly action_Value As Action(Of T)

    Friend Sub New(action As Action(Of T))
        action_Value = action
    End Sub

    Public Sub Report(value As T) Implements IProgress(Of T).Report
        action_Value?.Invoke(value)
    End Sub
End Class
