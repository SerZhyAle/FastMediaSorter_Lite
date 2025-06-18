Imports System.Runtime.InteropServices

Module Common_Module
    Public Hardkeys_to_move_mediafile(10) As String
    Public Is_Russian_Language As Boolean
    Public Is_Copying_not_Moving As Boolean
    Public Second_App_Name As String = "FastMediaSorter"
    Public App_name As String = "SZA"
    Public Is_No_Background_Tasks As Boolean = False
    Public Is_Pespective As Boolean = True
    Public Form_Color_Scheme As Integer = 0 ' 0 - dynamic, 1 - black, 2 - white, 3 - most
    Public Choosen_Picture_From_Panel As String = ""
    Public Current_File_Name As String
    Public Current_Image_Path As String

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