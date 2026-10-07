Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Runtime.InteropServices
Imports Microsoft.Win32

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

    Friend Enum UnicodeMapiResult
        Answered
        NotSupported
        ExecutionFailed
    End Enum

    Friend Delegate Function UnicodeMapiSendDelegate(attachmentPath As String, subject As String, body As String,
                                                     owner As IntPtr, ByRef rc As Integer) As UnicodeMapiResult

    Friend Delegate Function AnsiMapiSendDelegate(attachmentPath As String, subject As String, body As String,
                                                  owner As IntPtr, ByRef rc As Integer) As Boolean

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
        Return SendCore(attachmentPath, subject, body, owner, AddressOf TrySendUnicode, AddressOf TrySendAnsi)
    End Function

    Friend Function SendCore(attachmentPath As String, subject As String, body As String,
                             owner As IntPtr,
                             unicodeSender As UnicodeMapiSendDelegate,
                             ansiSender As AnsiMapiSendDelegate) As MailSendOutcome
        If String.IsNullOrEmpty(attachmentPath) OrElse Not File.Exists(attachmentPath) Then Return MailSendOutcome.Failed

        ' Send never throws: whatever goes wrong in the interop - including a failure before the
        ' sender's own Try - is a Failed outcome, because the callers' manual route (clipboard,
        ' the folder, a plain mailto) hangs off that outcome and not off an exception (105 L03).
        Try
            Dim rc As Integer = 0
            Dim unicodeStatus As UnicodeMapiResult = If(unicodeSender IsNot Nothing,
                                                       unicodeSender(attachmentPath, subject, body, owner, rc),
                                                       UnicodeMapiResult.NotSupported)
            Select Case unicodeStatus
                Case UnicodeMapiResult.Answered
                    Return OutcomeOf(rc)
                Case UnicodeMapiResult.ExecutionFailed
                    Return MailSendOutcome.Failed
                Case UnicodeMapiResult.NotSupported
                    Dim ansiAnswered As Boolean = False
                    If ansiSender IsNot Nothing Then
                        ansiAnswered = ansiSender(attachmentPath, subject, body, owner, rc)
                    End If
                    If Not ansiAnswered Then Return MailSendOutcome.Failed
                    Return OutcomeOf(rc)
            End Select
        Catch ex As Exception
            Try
                AppFileLogger.LogException("Composing mail through MAPI", ex)
            Catch
                ' The logger is not the broken part we were asked about; never recurse into it.
            End Try
        End Try

        Return MailSendOutcome.Failed
    End Function

    ''' <summary>What a caller does next. One decision for both programs, so that "Failed"
    ''' always means the manual route and never an error box.</summary>
    Friend Enum MailFollowUp
        ''' <summary>The client took it; nothing more to do.</summary>
        Finished
        ''' <summary>The user cancelled: nothing was sent, so the archive is not left in TEMP.</summary>
        DiscardArchive
        ''' <summary>No usable MAPI client: clipboard, folder, plain mailto, explanation.</summary>
        ManualRoute
    End Enum

    Friend Function FollowUpFor(outcome As MailSendOutcome) As MailFollowUp
        Select Case outcome
            Case MailSendOutcome.Sent
                Return MailFollowUp.Finished
            Case MailSendOutcome.Cancelled
                Return MailFollowUp.DiscardArchive
            Case Else
                Return MailFollowUp.ManualRoute
        End Select
    End Function
    Friend Function OutcomeOf(rc As Integer) As MailSendOutcome
        If rc = SUCCESS_SUCCESS Then Return MailSendOutcome.Sent
        If rc = MAPI_E_USER_ABORT Then Return MailSendOutcome.Cancelled
        Return MailSendOutcome.Failed
    End Function

    ''' <summary>False when the Unicode entry point is missing (Windows 7) or the client
    ''' says it does not support it - the caller then tries ANSI. Any other answer is
    ''' final and lands in <paramref name="rc"/>.</summary>
    Friend Function TrySendUnicode(attachmentPath As String, subject As String, body As String,
                                   owner As IntPtr, ByRef rc As Integer) As UnicodeMapiResult
        Dim fileDesc As New MapiFileDescW With {
            .position = -1,
            .path = attachmentPath,
            .fileName = Path.GetFileName(attachmentPath)
        }
        Dim fileMem As IntPtr = Marshal.AllocHGlobal(Marshal.SizeOf(GetType(MapiFileDescW)))
        Dim fileInitialized As Boolean = False
        Try
            Marshal.StructureToPtr(fileDesc, fileMem, False)
            fileInitialized = True
            Dim msg As New MapiMessageW With {
                .subject = subject,
                .noteText = body,
                .fileCount = 1,
                .files = fileMem
            }
            rc = MAPISendMailW(IntPtr.Zero, owner, msg, MAPI_LOGON_UI Or MAPI_DIALOG, 0)
            If rc = MAPI_E_NOT_SUPPORTED Then
                Return UnicodeMapiResult.NotSupported
            End If
            Return UnicodeMapiResult.Answered
        Catch ex As EntryPointNotFoundException
            Return UnicodeMapiResult.NotSupported
        Catch ex As DllNotFoundException
            Return UnicodeMapiResult.NotSupported
        Catch ex As Exception
            AppFileLogger.LogException("Composing mail through Unicode MAPI", ex)
            Return UnicodeMapiResult.ExecutionFailed
        Finally
            Try
                If fileInitialized Then Marshal.DestroyStructure(fileMem, GetType(MapiFileDescW))
            Catch ex As Exception
                AppFileLogger.LogException("Releasing the Unicode MAPI attachment descriptor", ex)
            Finally
                Marshal.FreeHGlobal(fileMem)
            End Try
        End Try
    End Function

    ' --- is there a mail client at all? (ticket 182 L-6) ---------------------------------------

    ''' <summary>What the manual route (clipboard, the folder, a plain mailto) does next.</summary>
    Friend Enum ManualRoute
        ''' <summary>A mail handler is registered: the mailto is worth starting.</summary>
        TryMailto
        ''' <summary>No handler (a Windows Server never has one): a blind mailto would raise a "choose an
        ''' app" prompt for nothing, so it is skipped and the archive's full path is printed instead.</summary>
        PrintPathOnly
    End Enum

    ''' <summary>The decision of the manual route, from the one fact that matters.</summary>
    Friend Function ManualRouteFor(handlerRegistered As Boolean) As ManualRoute
        Return If(handlerRegistered, ManualRoute.TryMailto, ManualRoute.PrintPathOnly)
    End Function

    ''' <summary>
    ''' Pure: is a mail handler registered, given what the registry says? Three independent signs, any one
    ''' is enough - the user's own choice for mailto (UserChoice ProgId), a mailto open command whose program
    ''' really exists on disk (a registration left behind by a removed Windows Mail points at nothing and
    ''' only raises the "choose an app" prompt), or a MAPI client registered under HKLM\SOFTWARE\Clients\Mail.
    ''' </summary>
    Friend Function IsMailHandlerRegistered(userChoiceProgId As String, openCommand As String,
                                            mapiClientNames As IEnumerable(Of String),
                                            fileExists As Func(Of String, Boolean)) As Boolean
        If Not String.IsNullOrWhiteSpace(userChoiceProgId) Then Return True
        Dim program As String = ProgramOfCommand(openCommand)
        If program.Length > 0 AndAlso fileExists IsNot Nothing AndAlso fileExists(program) Then Return True
        If mapiClientNames IsNot Nothing Then
            For Each name As String In mapiClientNames
                If Not String.IsNullOrWhiteSpace(name) Then Return True
            Next
        End If
        Return False
    End Function

    ''' <summary>The program of a registry open command: the quoted path, or the text up to the first space,
    ''' with environment variables expanded. "" when there is none. Pure.</summary>
    Friend Function ProgramOfCommand(command As String) As String
        If String.IsNullOrWhiteSpace(command) Then Return ""
        Dim text As String = command.Trim()
        Dim program As String
        If text.StartsWith("""", StringComparison.Ordinal) Then
            Dim close As Integer = text.IndexOf(""""c, 1)
            program = If(close > 0, text.Substring(1, close - 1), text.Substring(1))
        Else
            Dim space As Integer = text.IndexOf(" "c)
            program = If(space > 0, text.Substring(0, space), text)
        End If
        Try
            Return Environment.ExpandEnvironmentVariables(program.Trim())
        Catch
            Return program.Trim()
        End Try
    End Function

    ''' <summary>The live registry answer behind <see cref="IsMailHandlerRegistered"/>. Never throws.</summary>
    Friend Function HasMailHandler() As Boolean
        Dim userChoice As String = ""
        Dim openCommand As String = ""
        Dim clients As New List(Of String)()
        Try
            Using key As RegistryKey = Registry.CurrentUser.OpenSubKey("Software\Microsoft\Windows\Shell\Associations\UrlAssociations\mailto\UserChoice", False)
                If key IsNot Nothing Then userChoice = Convert.ToString(key.GetValue("ProgId"))
            End Using
        Catch
        End Try
        Try
            Using key As RegistryKey = Registry.ClassesRoot.OpenSubKey("mailto\shell\open\command", False)
                If key IsNot Nothing Then openCommand = Convert.ToString(key.GetValue(""))
            End Using
        Catch
        End Try
        Try
            Using key As RegistryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\Clients\Mail", False)
                If key IsNot Nothing Then clients.AddRange(key.GetSubKeyNames())
            End Using
        Catch
        End Try
        Try
            Return IsMailHandlerRegistered(userChoice, openCommand, clients, Function(p) File.Exists(p))
        Catch
            Return False
        End Try
    End Function

    ''' <summary>The ANSI call, as before. False when it could not be made at all.</summary>
    Friend Function TrySendAnsi(attachmentPath As String, subject As String, body As String,
                                owner As IntPtr, ByRef rc As Integer) As Boolean
        Dim fileDesc As New MapiFileDesc With {
            .position = -1,
            .path = attachmentPath,
            .fileName = Path.GetFileName(attachmentPath)
        }
        Dim fileMem As IntPtr = Marshal.AllocHGlobal(Marshal.SizeOf(GetType(MapiFileDesc)))
        Dim fileInitialized As Boolean = False
        Try
            Marshal.StructureToPtr(fileDesc, fileMem, False)
            fileInitialized = True
            Dim msg As New MapiMessage With {
                .subject = subject,
                .noteText = body,
                .fileCount = 1,
                .files = fileMem
            }
            rc = MAPISendMail(IntPtr.Zero, owner, msg, MAPI_LOGON_UI Or MAPI_DIALOG, 0)
            Return True
        Catch ex As Exception
            AppFileLogger.LogException("Composing mail through ANSI MAPI", ex)
            Return False
        Finally
            Try
                If fileInitialized Then Marshal.DestroyStructure(fileMem, GetType(MapiFileDesc))
            Catch ex As Exception
                AppFileLogger.LogException("Releasing the ANSI MAPI attachment descriptor", ex)
            Finally
                Marshal.FreeHGlobal(fileMem)
            End Try
        End Try
    End Function

End Module
