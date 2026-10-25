#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Runtime.InteropServices

''' <summary>
''' MEDIA-VIEWER 1.0 rule 5 - "keep the screen awake while possible and active". One
''' process-wide display keep-awake held by the running slideshow: the ES_CONTINUOUS
''' request belongs to the calling thread and dies with it, so even a hard exit cannot
''' leak it past the process. SlideShowStart and SlideShowStop are the only callers.
''' </summary>
Public NotInheritable Class DisplayKeepAwake

    Private Const ES_CONTINUOUS As Integer = &H80000000
    Private Const ES_DISPLAY_REQUIRED As Integer = &H2

    Private Shared active As Boolean

    Private Sub New()
    End Sub

    ''' <summary>True while the slideshow holds the screen awake.</summary>
    Public Shared ReadOnly Property IsActive As Boolean
        Get
            Return active
        End Get
    End Property

    ''' <summary>Requests or releases the display keep-awake. Idempotent: repeating the
    ''' current state re-issues the same flags and changes nothing.</summary>
    Public Shared Sub SetActive(value As Boolean)
        If active = value Then Return
        NativeSetThreadExecutionState(ES_CONTINUOUS Or If(value, ES_DISPLAY_REQUIRED, 0))
        active = value
    End Sub

    ''' <summary>Releases the keep-awake whatever the state - the belt-and-braces call for
    ''' a shutdown path that may bypass SlideShowStop.</summary>
    Public Shared Sub Release()
        SetActive(False)
    End Sub

    <DllImport("kernel32", EntryPoint:="SetThreadExecutionState", SetLastError:=True)>
    Private Shared Function NativeSetThreadExecutionState(flags As Integer) As Integer
    End Function

End Class
#End If
