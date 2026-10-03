#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' One scale factor for the whole chrome, and the one function every metric goes through
''' (002 section 4). Pure.
'''
''' The factor comes from the <c>InterfaceScalePercent</c> preference: 0 is "automatic" and means
''' the DPI the PROCESS sees divided by 96, 90..150 is an explicit choice. While the app is
''' DpiUnaware the process sees 96 on every monitor (the compositor stretches the whole window),
''' so automatic is 1.0 there - multiplying again by the monitor's real factor would scale the
''' chrome twice. The caller passes <c>Form.DeviceDpi</c>, so the day PerMonitorV2 is switched
''' on (002 phase 7) automatic starts following the monitor with no change here.
''' </summary>
Friend NotInheritable Class UiScale

    Private Sub New()
    End Sub

    Public Const MinPercent As Integer = 90
    Public Const MaxPercent As Integer = 150
    Public Const BaselineDpi As Integer = 96

    ''' <summary>Bounds for the automatic factor - a stray DPI value must not produce a chrome
    ''' that is invisible or larger than the screen.</summary>
    Private Const MinAutomatic As Single = 1.0F
    Private Const MaxAutomatic As Single = 4.0F

    ''' <summary>The preference as the loader promises it: 0 stays 0 (automatic), anything else
    ''' lies in 90..150.</summary>
    Public Shared Function NormalizePercent(percent As Integer) As Integer
        If percent <= 0 Then Return 0
        If percent < MinPercent Then Return MinPercent
        If percent > MaxPercent Then Return MaxPercent
        Return percent
    End Function

    ''' <summary>The scale factor: 1.0 = the logical size. <paramref name="percentPreference"/> 0
    ''' follows <paramref name="processDpi"/>.</summary>
    Public Shared Function Factor(percentPreference As Integer, processDpi As Integer) As Single
        Dim percent As Integer = NormalizePercent(percentPreference)
        If percent > 0 Then Return percent / 100.0F
        If processDpi <= 0 Then Return MinAutomatic
        Dim automatic As Single = processDpi / CSng(BaselineDpi)
        If automatic < MinAutomatic Then Return MinAutomatic
        If automatic > MaxAutomatic Then Return MaxAutomatic
        Return automatic
    End Function

    ''' <summary>A logical size in device pixels. Monotonic in <paramref name="logical"/>, equal
    ''' inputs give equal outputs, and a positive size is never rounded down to nothing.
    ''' Zero and negative sizes are 0 - they are not sizes.</summary>
    Public Shared Function Px(logical As Integer, factor As Single) As Integer
        If logical <= 0 Then Return 0
        If Single.IsNaN(factor) OrElse factor <= 0.0F Then factor = 1.0F
        Dim scaled As Double = Math.Round(logical * CDbl(factor), MidpointRounding.AwayFromZero)
        If scaled < 1.0R Then Return 1
        If scaled > 100000.0R Then Return 100000
        Return CInt(scaled)
    End Function

End Class
#End If
