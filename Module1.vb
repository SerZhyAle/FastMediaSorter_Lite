Imports System.Runtime.InteropServices

Module Module1
    Public moveOnKey(10) As String
    Public lngRus As Boolean
    Public copyMode As Boolean
    Public secName As String = "FastMediaSorter"
    Public appName As String = "SZA"
    Public noBackgroundTasksMode As Boolean = False

    ' --- WinAPI Declarations ---
    <DllImport("user32.dll")>
    Public Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Public Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Public Function GetForegroundWindow() As IntPtr
    End Function

    Public Const SW_SHOWNOACTIVATE As Integer = 4
End Module