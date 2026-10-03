#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Runtime.InteropServices

''' <summary>
''' What the Windows host can say about a backdrop that animates for the length of a track
''' (WAVE-PARTICLES rules 10 and 11, ticket 029 F-5 and F-6): is the window somewhere nobody can see,
''' and is the machine asking for less. Both are plain reads with no WinRT projection, so the viewer
''' stays on <c>net10.0-windows</c>.
''' </summary>
Friend Module BackdropHostSignals

    <StructLayout(LayoutKind.Sequential)>
    Private Structure SystemPowerStatus
        Public AcLineStatus As Byte
        Public BatteryFlag As Byte
        Public BatteryLifePercent As Byte
        Public SystemStatusFlag As Byte
        Public BatteryLifeTime As UInteger
        Public BatteryFullLifeTime As UInteger
    End Structure

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function GetSystemPowerStatus(ByRef status As SystemPowerStatus) As Boolean
    End Function

    <DllImport("dwmapi.dll")>
    Private Function DwmGetWindowAttribute(hwnd As IntPtr, attribute As Integer, ByRef value As Integer, size As Integer) As Integer
    End Function

    Private Const Dwma_Cloaked As Integer = 14
    Private Const Ac_Line_Offline As Byte = 0
    Private Const Power_Saver_On As Byte = 1
    Private Const Power_Poll_Ms As Long = 1000

    Private power_Read_At As Long = Long.MinValue
    Private power_Saving As Boolean

    ''' <summary>
    ''' Energy saver switched on AND the machine running on battery - the level at which an
    ''' <c>ambient</c> backdrop freezes (rule 11; a <c>decorative</c> one would already freeze at energy
    ''' saver alone). Read from the system at most once a second; any failure answers "no", because a
    ''' query that did not work is not a request for stillness.
    ''' </summary>
    Friend Function IsEnergySaverOnBattery() As Boolean
        Dim now As Long = Environment.TickCount64
        If power_Read_At <> Long.MinValue AndAlso now - power_Read_At < Power_Poll_Ms Then Return power_Saving
        power_Read_At = now
        Dim saving As Boolean = False
        Try
            Dim status As SystemPowerStatus
            If GetSystemPowerStatus(status) Then
                saving = status.SystemStatusFlag = Power_Saver_On AndAlso status.AcLineStatus = Ac_Line_Offline
            End If
        Catch
            saving = False
        End Try
        power_Saving = saving
        Return saving
    End Function

    ''' <summary>The window is cloaked - on another virtual desktop, say - so it is neither minimised nor
    ''' hidden yet nobody can see it (rule 10). A window merely covered by another cannot be told apart
    ''' cheaply from WinForms and is not detected.</summary>
    Friend Function IsWindowCloaked(handle As IntPtr) As Boolean
        If handle = IntPtr.Zero Then Return False
        Try
            Dim cloaked As Integer = 0
            Return DwmGetWindowAttribute(handle, Dwma_Cloaked, cloaked, 4) = 0 AndAlso cloaked <> 0
        Catch
            Return False
        End Try
    End Function
End Module
#End If
