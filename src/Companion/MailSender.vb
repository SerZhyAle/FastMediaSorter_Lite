Option Strict On

Imports System.IO
Imports System.Runtime.InteropServices

''' <summary>
''' Opens a new email in the user's default mail client with a file attached,
''' via Simple MAPI (MAPISendMail with MAPI_DIALOG). A plain "mailto:" URI cannot
''' carry an attachment (clients ignore the non-standard attachment parameter),
''' so MAPI is the working path. Falls back gracefully: returns False when no MAPI
''' client is configured, so the caller can hint the user.
''' </summary>
Public Module MailSender

    Private Const MAPI_LOGON_UI As Integer = &H1
    Private Const MAPI_DIALOG As Integer = &H8
    Private Const SUCCESS_SUCCESS As Integer = 0
    Private Const MAPI_E_USER_ABORT As Integer = 1

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Ansi)>
    Private Structure MapiFileDesc
        Public reserved As Integer
        Public flags As Integer
        Public position As Integer
        <MarshalAs(UnmanagedType.LPStr)> Public path As String
        <MarshalAs(UnmanagedType.LPStr)> Public fileName As String
        Public fileType As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Ansi)>
    Private Structure MapiMessage
        Public reserved As Integer
        <MarshalAs(UnmanagedType.LPStr)> Public subject As String
        <MarshalAs(UnmanagedType.LPStr)> Public noteText As String
        <MarshalAs(UnmanagedType.LPStr)> Public messageType As String
        <MarshalAs(UnmanagedType.LPStr)> Public dateReceived As String
        <MarshalAs(UnmanagedType.LPStr)> Public conversationID As String
        Public flags As Integer
        Public originator As IntPtr
        Public recipCount As Integer
        Public recips As IntPtr
        Public fileCount As Integer
        Public files As IntPtr
    End Structure

    <DllImport("MAPI32.DLL", CharSet:=CharSet.Ansi)>
    Private Function MAPISendMail(session As IntPtr, uiParam As IntPtr, ByRef message As MapiMessage, flags As Integer, reserved As Integer) As Integer
    End Function

    ''' <summary>Composes a new mail with <paramref name="attachmentPath"/> attached,
    ''' opening the default mail client's editor. Must run on the UI (STA) thread.
    ''' Returns True on success or user-cancel; False when no MAPI client / failure.</summary>
    Public Function SendFile(attachmentPath As String, subject As String, body As String) As Boolean
        If String.IsNullOrEmpty(attachmentPath) OrElse Not File.Exists(attachmentPath) Then Return False

        Dim fileDesc As New MapiFileDesc With {
            .position = -1,
            .path = attachmentPath,
            .fileName = Path.GetFileName(attachmentPath)
        }

        Dim fileMem As IntPtr = Marshal.AllocHGlobal(Marshal.SizeOf(GetType(MapiFileDesc)))
        Try
            Marshal.StructureToPtr(fileDesc, fileMem, False)

            Dim msg As New MapiMessage With {
                .subject = subject,
                .noteText = body,
                .fileCount = 1,
                .files = fileMem
            }

            Dim rc As Integer = MAPISendMail(IntPtr.Zero, IntPtr.Zero, msg, MAPI_LOGON_UI Or MAPI_DIALOG, 0)
            Return rc = SUCCESS_SUCCESS OrElse rc = MAPI_E_USER_ABORT
        Catch
            Return False
        Finally
            Try
                Marshal.DestroyStructure(fileMem, GetType(MapiFileDesc))
            Catch
            End Try
            Marshal.FreeHGlobal(fileMem)
        End Try
    End Function

End Module
