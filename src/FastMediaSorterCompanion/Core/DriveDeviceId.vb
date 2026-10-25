Option Strict On

Imports System.IO
Imports System.Text

''' <summary>
''' The device id of this PC in the Google Drive channel (specification 203 section 5.9, owner decision T-UI-1).
'''
''' A PC that only signs in to Drive has no remote-server account and therefore no device id, yet every record and packet
''' of the channel is named by one. Rules held here:
'''   * One device even when both channels exist: when the PC already has the device id of its remote-server account
'''     (<see cref="ExchangeAccountStore.DeviceIdName"/>), that id is the Drive id too.
'''   * Otherwise a random id of the same shape (<see cref="ExchangeIds.NewId"/>) is created once, on the first sign-in, and
'''     kept in <c>drive-device-id.txt</c> beside <c>drive-token.dat</c> - the same per-user folder, written the same way (a
'''     temporary file in that folder, then a move). The id is not a secret, so DPAPI is not used.
'''   * The file always follows the id in use, so it stays stable after the account is left.
'''   * Asking is free: <see cref="Peek"/> reads and creates nothing; only <see cref="Resolve"/> writes, and only the page's
'''     "Sign in" command and a start with a stored sign-in reach it.
''' </summary>
Friend NotInheritable Class DriveDeviceId

    Public Const FileName As String = "drive-device-id.txt"

    Private ReadOnly _directory As String
    Private ReadOnly _serverId As Func(Of String)
    Private ReadOnly _newId As Func(Of String)

    ''' <param name="directory">The folder of the Drive token file.</param>
    ''' <param name="serverId">The id of the remote-server account, or "" (a pure read). Nothing: the registry value.</param>
    ''' <param name="newId">A fresh id. Nothing: <see cref="ExchangeIds.NewId"/>.</param>
    Public Sub New(directory As String, Optional serverId As Func(Of String) = Nothing, Optional newId As Func(Of String) = Nothing)
        If String.IsNullOrWhiteSpace(directory) Then Throw New ArgumentException("directory", NameOf(directory))
        _directory = directory
        _serverId = If(serverId, Function() ReadServerDeviceId())
        _newId = If(newId, Function() ExchangeIds.NewId())
    End Sub

    ''' <summary>The per-user folder of the Drive token (nothing is created by asking for it).</summary>
    Public Shared Function CreateDefault() As DriveDeviceId
        Return New DriveDeviceId(ExchangeCredentialStore.DefaultDirectory())
    End Function

    Public ReadOnly Property FilePath As String
        Get
            Return Path.Combine(_directory, FileName)
        End Get
    End Property

    ''' <summary>The stored id, or "" when there is none (or the file is not an id). A pure read.</summary>
    Public Function Peek() As String
        Try
            If Not File.Exists(FilePath) Then Return ""
            Dim text As String = File.ReadAllText(FilePath, Encoding.ASCII).Trim()
            Return If(ExchangeIds.IsValidId(text), text, "")
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return ""
        End Try
    End Function

    ''' <summary>The id this PC uses in the channel: the remote-server account's id when there is one, else the stored one, else
    ''' a new one that is stored at once. "" only when a new id could not be stored - an id that would change at the next
    ''' start is worse than none.</summary>
    Public Function Resolve() As String
        Dim stored As String = Peek()
        Dim server As String = ReadServer()
        If server.Length > 0 Then
            If Not String.Equals(stored, server, StringComparison.Ordinal) Then Write(server)
            Return server
        End If
        If stored.Length > 0 Then Return stored
        Dim fresh As String = _newId()
        If Not ExchangeIds.IsValidId(fresh) Then Return ""
        Return If(Write(fresh), fresh, "")
    End Function

    Private Function ReadServer() As String
        Dim id As String = ""
        Try
            id = If(_serverId(), "")
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is System.Security.SecurityException
            id = ""
        End Try
        Return If(ExchangeIds.IsValidId(id), id, "")
    End Function

    Private Shared Function ReadServerDeviceId() As String
        Return New ExchangeRegistrySettings().GetValue(ExchangeAccountStore.DeviceIdName)
    End Function

    ''' <summary>A temporary file in the same folder, then a move over the target: never a half-written id.</summary>
    Private Function Write(id As String) As Boolean
        Try
            Directory.CreateDirectory(_directory)
            Dim temp As String = Path.Combine(_directory, FileName & "." & Guid.NewGuid().ToString("N") & ".tmp")
            Try
                File.WriteAllText(temp, id, Encoding.ASCII)
                File.Move(temp, FilePath, True)
            Finally
                Try
                    If File.Exists(temp) Then File.Delete(temp)
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                End Try
            End Try
            Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        End Try
    End Function

End Class
