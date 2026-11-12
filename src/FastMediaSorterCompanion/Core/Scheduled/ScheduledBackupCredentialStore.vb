Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text

' The password of a sealed scheduled backup (ticket L0298, Q298-3).
'
' One DPAPI (CurrentUser) blob per operation id, in a subfolder of the operation store, so the password is neither in the
' operation's JSON nor in a settings export, and a profile copied to another account or PC reads it as absent - the
' operation then halts until its password is entered here again. The value is handed to FileDO only through a variable of
' the child's own environment (CLI-EVENT-STREAM 0.17 item H, `pe:<VARIABLE>`), never on a command line, and is never
' logged or journaled: a failure is logged by its exception class alone.
Friend NotInheritable Class ScheduledBackupCredentialStore

    Friend Const FolderName As String = "backup-secrets"
    Private Const Extension As String = ".dpapi"
    ''' <summary>Binds a blob to this use: a DPAPI blob of another feature does not unprotect here.</summary>
    Private Shared ReadOnly Entropy As Byte() = Encoding.UTF8.GetBytes("FastMediaSorter.ScheduledBackup.v1")

    Private ReadOnly folder As String

    ''' <param name="operationStoreFolder">The folder of <see cref="ScheduledStore"/>; the blobs live in its subfolder.</param>
    Friend Sub New(operationStoreFolder As String)
        folder = Path.Combine(operationStoreFolder, FolderName)
    End Sub

    Private Function FileFor(id As String) As String
        ' An id is a GUID this program wrote; anything else could name a path outside the folder.
        Dim safe As String = New String(If(id, "").Where(Function(c) Char.IsLetterOrDigit(c) OrElse c = "-"c).ToArray())
        If safe.Length = 0 Then Return ""
        Return Path.Combine(folder, safe & Extension)
    End Function

    Friend Function Has(id As String) As Boolean
        Return Read(id).Length > 0
    End Function

    ''' <summary>"" when there is none or it cannot be unprotected by this account.</summary>
    Friend Function Read(id As String) As String
        Dim file As String = FileFor(id)
        If file.Length = 0 OrElse Not System.IO.File.Exists(file) Then Return ""
        Try
            Dim plain As Byte() = ProtectedData.Unprotect(System.IO.File.ReadAllBytes(file), Entropy, DataProtectionScope.CurrentUser)
            Return Encoding.UTF8.GetString(plain)
        Catch ex As Exception When TypeOf ex Is CryptographicException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppFileLogger.WriteLine("Scheduled operations: a backup password could not be read (" & ex.GetType().Name & ")")
            Return ""
        End Try
    End Function

    ''' <summary>False when the password could not be protected or written; nothing is then stored in its place.</summary>
    Friend Function Write(id As String, password As String) As Boolean
        Dim file As String = FileFor(id)
        If file.Length = 0 OrElse String.IsNullOrEmpty(password) Then Return False
        Try
            Directory.CreateDirectory(folder)
            Dim blob As Byte() = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser)
            Dim temp As String = file & ".tmp"
            System.IO.File.WriteAllBytes(temp, blob)
            System.IO.File.Move(temp, file, overwrite:=True)
            Return True
        Catch ex As Exception When TypeOf ex Is CryptographicException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppFileLogger.WriteLine("Scheduled operations: a backup password could not be stored (" & ex.GetType().Name & ")")
            Return False
        End Try
    End Function

    Friend Sub Delete(id As String)
        Dim file As String = FileFor(id)
        If file.Length = 0 Then Return
        Try
            If System.IO.File.Exists(file) Then System.IO.File.Delete(file)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppFileLogger.WriteLine("Scheduled operations: a backup password could not be removed (" & ex.GetType().Name & ")")
        End Try
    End Sub

    ''' <summary>The weakest passwords FileDO's own refusal would also reject: shorter than 8 characters, or one character repeated.</summary>
    Friend Shared Function IsTrivial(password As String) As Boolean
        Dim text As String = If(password, "")
        If text.Length < 8 Then Return True
        Return text.Distinct().Count() = 1
    End Function

End Class
