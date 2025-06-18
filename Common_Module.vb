Imports System.Runtime.InteropServices

Module Common_Module
    Public hardkeys_to_move_mediafile(10) As String
    Public is_Russian_Language As Boolean
    Public is_Copying_not_Moving As Boolean
    Public second_App_Name As String = "FastMediaSorter"
    Public app_name As String = "SZA"
    Public is_No_Background_Tasks As Boolean = False
    Public is_Pespective As Boolean = True
    Public form_Color_Scheme As Integer = 0 ' 0 - dynamic, 1 - black, 2 - white, 3 - most

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