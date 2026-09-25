Option Strict On

Imports System.IO
Imports System.Runtime.InteropServices

''' <summary>
''' Opens a new email in the user's default mail client with a file attached,
''' via Simple MAPI (MAPISendMail with MAPI_DIALOG). A plain "mailto:" URI cannot
''' carry an attachment (clients ignore the non-standard attachment parameter),
''' so MAPI is the working path. Falls back gracefully: returns False when no MAPI
''' client is configured, so the caller can hint the user.
'''
''' Unicode first (MAPISendMailW, Windows 8+), ANSI only when that export is missing or
''' the client refuses it. The ANSI call alone marshalled the subject, the localized body
''' and the attachment path to the system code page: Hindi and Bengali, which have no
''' code page, always arrived as "????", every other script was mangled on a machine with
''' another system locale, and a profile path outside the code page made MAPI report the
''' attachment as not found (053 F-14).
''' </summary>
Public Module MailSender

    ''' <summary>The author's address - the one literal both programs write to. The viewer
    ''' reads it as Common_Module.Author_Email.</summary>
    Public Const AuthorAddress As String = "sza@ukr.net"

    Private Const MAPI_LOGON_UI As Integer = &H1
    Private Const MAPI_DIALOG As Integer = &H8
    Private Const SUCCESS_SUCCESS As Integer = 0
    Private Const MAPI_E_USER_ABORT As Integer = 1
    Private Const MAPI_E_NOT_SUPPORTED As Integer = 26

    ''' <summary>What became of a send.</summary>
    Public Enum MailSendOutcome
        ''' <summary>The mail client took the message (or its editor was closed after
        ''' sending - Simple MAPI does not tell the two apart).</summary>
        Sent
        ''' <summary>The user closed the client's editor without sending.</summary>
        Cancelled
        ''' <summary>No MAPI client, or it failed: the caller offers the manual route.</summary>
        Failed
    End Enum

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

    ''' <summary>MapiFileDescW - the same layout with UTF-16 strings. Friend for the
    ''' marshalling test.</summary>
    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Friend Structure MapiFileDescW
        Public reserved As Integer
        Public flags As Integer
        Public position As Integer
        <MarshalAs(UnmanagedType.LPWStr)> Public path As String
        <MarshalAs(UnmanagedType.LPWStr)> Public fileName As String
        Public fileType As IntPtr
    End Structure

    ''' <summary>MapiMessageW. Friend for the marshalling test.</summary>
    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Friend Structure MapiMessageW
        Public reserved As Integer
        <MarshalAs(UnmanagedType.LPWStr)> Public subject As String
        <MarshalAs(UnmanagedType.LPWStr)> Public noteText As String
        <MarshalAs(UnmanagedType.LPWStr)> Public messageType As String
        <MarshalAs(UnmanagedType.LPWStr)> Public dateReceived As String
        <MarshalAs(UnmanagedType.LPWStr)> Public conversationID As String
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

    <DllImport("MAPI32.DLL", EntryPoint:="MAPISendMailW", CharSet:=CharSet.Unicode, ExactSpelling:=True)>
    Private Function MAPISendMailW(session As IntPtr, uiParam As IntPtr, ByRef message As MapiMessageW, flags As Integer, reserved As Integer) As Integer
    End Function

    ''' <summary>Composes a new mail with <paramref name="attachmentPath"/> attached,
    ''' opening the default mail client's editor. Must run on the UI (STA) thread.
    ''' Returns True on success or user-cancel; False when no MAPI client / failure.</summary>
    Public Function SendFile(attachmentPath As String, subject As String, body As String,
                             Optional owner As IntPtr = Nothing) As Boolean
        Return Send(attachmentPath, subject, body, owner) <> MailSendOutcome.Failed
    End Function

    ''' <summary>
    ''' The same, telling a cancel apart from a send. <paramref name="owner"/> is the window
    ''' the client's editor belongs to: with it the editor is modal to that window, instead
    ''' of a free-floating dialog over a window that has stopped painting.
    ''' </summary>
    Public Function Send(attachmentPath As String, subject As String, body As String,
                         Optional owner As IntPtr = Nothing) As MailSendOutcome
        If String.IsNullOrEmpty(attachmentPath) OrElse Not File.Exists(attachmentPath) Then Return MailSendOutcome.Failed

        Dim rc As Integer
        Dim unicodeAnswered As Boolean = TrySendUnicode(attachmentPath, subject, body, owner, rc)
        If Not unicodeAnswered Then
            If Not TrySendAnsi(attachmentPath, subject, body, owner, rc) Then Return MailSendOutcome.Failed
        End If
        Return OutcomeOf(rc)
    End Function

    Friend Function OutcomeOf(rc As Integer) As MailSendOutcome
        If rc = SUCCESS_SUCCESS Then Return MailSendOutcome.Sent
        If rc = MAPI_E_USER_ABORT Then Return MailSendOutcome.Cancelled
        Return MailSendOutcome.Failed
    End Function

    ''' <summary>False when the Unicode entry point is missing (Windows 7) or the client
    ''' says it does not support it - the caller then tries ANSI. Any other answer is
    ''' final and lands in <paramref name="rc"/>.</summary>
    Private Function TrySendUnicode(attachmentPath As String, subject As String, body As String,
                                    owner As IntPtr, ByRef rc As Integer) As Boolean
        Dim fileDesc As New MapiFileDescW With {
            .position = -1,
            .path = attachmentPath,
            .fileName = Path.GetFileName(attachmentPath)
        }
        Dim fileMem As IntPtr = Marshal.AllocHGlobal(Marshal.SizeOf(GetType(MapiFileDescW)))
        Try
            Marshal.StructureToPtr(fileDesc, fileMem, False)
            Dim msg As New MapiMessageW With {
                .subject = subject,
                .noteText = body,
                .fileCount = 1,
                .files = fileMem
            }
            rc = MAPISendMailW(IntPtr.Zero, owner, msg, MAPI_LOGON_UI Or MAPI_DIALOG, 0)
            Return rc <> MAPI_E_NOT_SUPPORTED
        Catch ex As EntryPointNotFoundException
            Return False
        Catch ex As DllNotFoundException
            Return False
        Finally
            Try
                Marshal.DestroyStructure(fileMem, GetType(MapiFileDescW))
            Catch
            End Try
            Marshal.FreeHGlobal(fileMem)
        End Try
    End Function

    ''' <summary>The ANSI call, as before. False when it could not be made at all.</summary>
    Private Function TrySendAnsi(attachmentPath As String, subject As String, body As String,
                                 owner As IntPtr, ByRef rc As Integer) As Boolean
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
            rc = MAPISendMail(IntPtr.Zero, owner, msg, MAPI_LOGON_UI Or MAPI_DIALOG, 0)
            Return True
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
