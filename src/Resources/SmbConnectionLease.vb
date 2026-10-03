#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading

''' <summary>
''' Helpers for parsing and normalizing UNC roots and SMB connection keys (100 §15.6 item 6).
''' Pure.
''' </summary>
Friend Module SmbPathUtil

    ''' <summary>
    ''' Extracts the canonical "\\server\share" root from any UNC path or smb:// URI.
    ''' Returns "" if the path is not a valid UNC/SMB root.
    ''' </summary>
    Friend Function ExtractUncRoot(path As String) As String
        If String.IsNullOrWhiteSpace(path) Then Return ""
        Dim trimmed As String = path.Trim()

        ' Handle smb:// URI prefix
        If trimmed.StartsWith("smb://", StringComparison.OrdinalIgnoreCase) Then
            trimmed = trimmed.Substring(6).Replace("/"c, "\"c)
            If Not trimmed.StartsWith("\\", StringComparison.Ordinal) Then
                trimmed = "\\" & trimmed.TrimStart("\"c)
            End If
        End If

        ' Normalize forward slashes
        Dim normalized As String = trimmed.Replace("/"c, "\"c)
        If Not normalized.StartsWith("\\", StringComparison.Ordinal) Then Return ""

        Dim withoutPrefix As String = normalized.Substring(2).TrimStart("\"c)
        Dim parts As String() = withoutPrefix.Split(New Char() {"\"c}, StringSplitOptions.RemoveEmptyEntries)
        If parts.Length < 2 Then Return ""

        Dim server As String = parts(0).Trim()
        Dim share As String = parts(1).Trim()
        If String.IsNullOrEmpty(server) OrElse String.IsNullOrEmpty(share) Then Return ""

        Return "\\" & server.ToLowerInvariant() & "\" & share.ToLowerInvariant()
    End Function

    ''' <summary>Normalizes username for connection key matching.</summary>
    Friend Function NormalizeUserName(userName As String) As String
        Return If(userName, "").Trim()
    End Function

    ''' <summary>Builds a unique normalized connection key: "\\server\share|username".</summary>
    Friend Function BuildConnectionKey(uncRoot As String, userName As String) As String
        Dim canonicalRoot As String = ExtractUncRoot(uncRoot)
        Dim canonicalUser As String = NormalizeUserName(userName).ToLowerInvariant()
        Return canonicalRoot & "|" & canonicalUser
    End Function

End Module

''' <summary>Win32 NETRESOURCE structure for WNetAddConnection2.</summary>
<StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Auto)>
Friend Structure NETRESOURCE
    Public dwScope As Integer
    Public dwType As Integer
    Public dwDisplayType As Integer
    Public dwUsage As Integer
    Public lpLocalName As String
    Public lpRemoteName As String
    Public lpComment As String
    Public lpProvider As String
End Structure

''' <summary>Abstraction over Win32 mpr.dll WNet calls for testability.</summary>
Friend Interface IWNetNative
    Function GetConnectedUser(uncRoot As String, ByRef userName As String) As Integer
    Function WNetAddConnection2(ByRef netResource As NETRESOURCE, password As String, username As String, flags As Integer) As Integer
    Function WNetCancelConnection2(name As String, flags As Integer, force As Boolean) As Integer
End Interface

''' <summary>Production P/Invoke wrapper for mpr.dll.</summary>
Friend NotInheritable Class NativeWNet
    Implements IWNetNative

    Friend Shared ReadOnly Instance As New NativeWNet()

    <DllImport("mpr.dll", CharSet:=CharSet.Unicode, EntryPoint:="WNetGetUserW")>
    Private Shared Function WNetGetUser(name As String, userName As StringBuilder, ByRef length As Integer) As Integer
    End Function

    Public Function GetConnectedUser(uncRoot As String, ByRef userName As String) As Integer Implements IWNetNative.GetConnectedUser
        userName = ""
        Dim length As Integer = 256
        Dim buffer As New StringBuilder(length)
        Dim rc As Integer = WNetGetUser(uncRoot, buffer, length)
        If rc = 234 AndAlso length > 0 AndAlso length <= 32768 Then
            buffer = New StringBuilder(length)
            rc = WNetGetUser(uncRoot, buffer, length)
        End If
        If rc = 0 Then userName = buffer.ToString()
        Return rc
    End Function

    <DllImport("mpr.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function WNetAddConnection2(ByRef lpNetResource As NETRESOURCE, lpPassword As String, lpUserName As String, dwFlags As Integer) As Integer
    End Function

    <DllImport("mpr.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function WNetCancelConnection2(lpName As String, dwFlags As Integer, bForce As Boolean) As Integer
    End Function

    Public Function WNetAddConnection2Impl(ByRef netResource As NETRESOURCE, password As String, username As String, flags As Integer) As Integer Implements IWNetNative.WNetAddConnection2
        Return WNetAddConnection2(netResource, password, username, flags)
    End Function

    Public Function WNetCancelConnection2Impl(name As String, flags As Integer, force As Boolean) As Integer Implements IWNetNative.WNetCancelConnection2
        Return WNetCancelConnection2(name, flags, force)
    End Function
End Class

''' <summary>
''' Represents an app-owned connection lease on an SMB network share (100 §15.6 item 6).
''' Holds an active lease count. Releasing/disposing the lease decrements the count
''' and disconnects app-created idle connections without affecting external mappings.
''' </summary>
Friend NotInheritable Class SmbConnectionLease
    Implements IDisposable

    Private ReadOnly manager As SmbConnectionManager
    Private ReadOnly key_Value As String
    Private ReadOnly unc_Root As String
    Private ReadOnly user_Name As String
    Private ReadOnly is_AppCreated As Boolean
    Private disposed_State As Integer

    Friend Sub New(manager As SmbConnectionManager, key As String, uncRoot As String, userName As String, isAppCreated As Boolean)
        Me.manager = manager
        Me.key_Value = key
        Me.unc_Root = uncRoot
        Me.user_Name = userName
        Me.is_AppCreated = isAppCreated
    End Sub

    Friend ReadOnly Property Key As String
        Get
            Return key_Value
        End Get
    End Property

    Friend ReadOnly Property UncRoot As String
        Get
            Return unc_Root
        End Get
    End Property

    Friend ReadOnly Property UserName As String
        Get
            Return user_Name
        End Get
    End Property

    ''' <summary>True if this connection was established by our app via WNetAddConnection2;
    ''' False if it was already existing / external.</summary>
    Friend ReadOnly Property IsAppCreated As Boolean
        Get
            Return is_AppCreated
        End Get
    End Property

    Friend ReadOnly Property IsDisposed As Boolean
        Get
            Return Volatile.Read(disposed_State) <> 0
        End Get
    End Property

    Public Sub Dispose() Implements IDisposable.Dispose
        If Interlocked.Exchange(disposed_State, 1) = 0 Then
            manager.ReleaseLease(Me)
        End If
    End Sub
End Class

Friend NotInheritable Class SmbLeaseEntry
    Friend ReadOnly Key As String
    Friend ReadOnly UncRoot As String
    Friend ReadOnly UserName As String
    Friend ReadOnly IsAppCreated As Boolean
    Friend LeaseCount As Integer
    Friend LastAcquiredUtc As DateTime

    Friend Sub New(key As String, uncRoot As String, userName As String, isAppCreated As Boolean, initialLeaseCount As Integer)
        Me.Key = key
        Me.UncRoot = uncRoot
        Me.UserName = userName
        Me.IsAppCreated = isAppCreated
        Me.LeaseCount = initialLeaseCount
        Me.LastAcquiredUtc = DateTime.UtcNow
    End Sub
End Class

''' <summary>
''' Manages app-owned credentialed SMB connection leases (100 §15.6 item 6).
''' Thread-safe. Tracks lease reference counts, establishes temporary process connections
''' via WNetAddConnection2 without persistent drive mappings, handles Windows 1219 credential
''' conflicts as typed refusals without forcing disconnects, and disconnects only app-created
''' idle connections on lease release.
''' </summary>
Friend NotInheritable Class SmbConnectionManager

    Public Const RESOURCETYPE_DISK As Integer = &H1
    Public Const CONNECT_TEMPORARY As Integer = &H4

    Public Const NO_ERROR As Integer = 0
    Public Const ERROR_NOT_CONNECTED As Integer = 2250
    Public Const ERROR_ACCESS_DENIED As Integer = 5
    Public Const ERROR_BAD_NETPATH As Integer = 53
    Public Const ERROR_BAD_NET_NAME As Integer = 67
    Public Const ERROR_ALREADY_ASSIGNED As Integer = 85
    Public Const ERROR_INVALID_PASSWORD As Integer = 86
    Public Const ERROR_BAD_PROVIDER As Integer = 1204
    Public Const ERROR_NO_NET_OR_BAD_PATH As Integer = 1203
    Public Const ERROR_ALREADY_EXISTS As Integer = 1218
    Public Const ERROR_SESSION_CREDENTIAL_CONFLICT As Integer = 1219
    Public Const ERROR_NETWORK_UNREACHABLE As Integer = 1231
    Public Const ERROR_LOGON_FAILURE As Integer = 1326

    Private Shared ReadOnly shared_Instance As New SmbConnectionManager()

    Friend Shared ReadOnly Property SharedInstance As SmbConnectionManager
        Get
            Return shared_Instance
        End Get
    End Property

    Private ReadOnly activeLeases As New Dictionary(Of String, SmbLeaseEntry)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly gate As New Object()
    Private ReadOnly wnetApi As IWNetNative

    Friend Sub New(Optional wnet As IWNetNative = Nothing)
        Me.wnetApi = If(wnet, NativeWNet.Instance)
    End Sub

    ''' <summary>
    ''' Acquires a connection lease on the normalized UNC root for the specified user and password.
    ''' Reuses an existing connection lease if one is already active for this key.
    ''' </summary>
    Friend Function AcquireLease(uncPath As String, userName As String, password As String) As ResourceResult(Of SmbConnectionLease)
        Dim uncRoot As String = SmbPathUtil.ExtractUncRoot(uncPath)
        If String.IsNullOrEmpty(uncRoot) Then
            Return ResourceResult(Of SmbConnectionLease).Fail(New ResourceFailure(ResourceFailureKind.Unsupported, Nothing, "Invalid UNC root: " & uncPath))
        End If

        Dim normalizedUser As String = SmbPathUtil.NormalizeUserName(userName)
        Dim key As String = SmbPathUtil.BuildConnectionKey(uncRoot, normalizedUser)

        SyncLock gate
            Dim entry As SmbLeaseEntry = Nothing
            If activeLeases.TryGetValue(key, entry) Then
                entry.LeaseCount += 1
                entry.LastAcquiredUtc = DateTime.UtcNow
                Return ResourceResult(Of SmbConnectionLease).Ok(New SmbConnectionLease(Me, key, uncRoot, normalizedUser, entry.IsAppCreated))
            End If

            ' WNetAddConnection2 may return success when Windows already has this connection.
            ' Inspect it first: success alone never establishes ownership of an external mapping.
            Dim connectedUser As String = ""
            Dim inspect As Integer = wnetApi.GetConnectedUser(uncRoot, connectedUser)
            If inspect <> NO_ERROR AndAlso inspect <> ERROR_NOT_CONNECTED Then
                Return ResourceResult(Of SmbConnectionLease).Fail(New ResourceFailure(ResourceFailureKind.LocalIO, inspect, "SMB connection ownership could not be inspected."))
            End If
            Dim existed As Boolean = inspect = NO_ERROR

            ' Attempt WNetAddConnection2
            Dim nr As New NETRESOURCE With {
                .dwType = RESOURCETYPE_DISK,
                .lpRemoteName = uncRoot,
                .lpLocalName = Nothing
            }

            Dim rc As Integer = wnetApi.WNetAddConnection2(nr, password, If(String.IsNullOrEmpty(normalizedUser), Nothing, normalizedUser), CONNECT_TEMPORARY)

            Select Case rc
                Case NO_ERROR
                    Dim appCreated As Boolean = Not existed
                    For Each active As SmbLeaseEntry In activeLeases.Values
                        If String.Equals(active.UncRoot, uncRoot, StringComparison.OrdinalIgnoreCase) AndAlso active.IsAppCreated Then appCreated = True
                    Next
                    entry = New SmbLeaseEntry(key, uncRoot, normalizedUser, isAppCreated:=appCreated, initialLeaseCount:=1)
                    activeLeases(key) = entry
                    Return ResourceResult(Of SmbConnectionLease).Ok(New SmbConnectionLease(Me, key, uncRoot, normalizedUser, isAppCreated:=appCreated))

                Case ERROR_ALREADY_ASSIGNED, ERROR_ALREADY_EXISTS
                    ' Existing OS mapping / connection exists externally
                    entry = New SmbLeaseEntry(key, uncRoot, normalizedUser, isAppCreated:=False, initialLeaseCount:=1)
                    activeLeases(key) = entry
                    Return ResourceResult(Of SmbConnectionLease).Ok(New SmbConnectionLease(Me, key, uncRoot, normalizedUser, isAppCreated:=False))

                Case ERROR_SESSION_CREDENTIAL_CONFLICT
                    ' Win32 1219: Multiple connections by same user with different credentials not allowed.
                    ' Typed refusal without force-disconnecting external mappings (100 §15.6 item 6).
                    Return ResourceResult(Of SmbConnectionLease).Fail(
                        New ResourceFailure(ResourceFailureKind.CredentialConflict, ERROR_SESSION_CREDENTIAL_CONFLICT,
                                           "Windows credential conflict (1219): an existing connection to this server exists under different credentials. Disconnect external mappings or use the existing user session."))

                Case ERROR_ACCESS_DENIED, ERROR_LOGON_FAILURE, ERROR_INVALID_PASSWORD
                    Return ResourceResult(Of SmbConnectionLease).Fail(
                        New ResourceFailure(ResourceFailureKind.Auth, rc, "Logon failure or access denied."))

                Case ERROR_BAD_NETPATH, ERROR_BAD_NET_NAME, ERROR_NO_NET_OR_BAD_PATH, ERROR_BAD_PROVIDER, ERROR_NETWORK_UNREACHABLE
                    Return ResourceResult(Of SmbConnectionLease).Fail(
                        New ResourceFailure(ResourceFailureKind.Transport, rc, "Network path or server not reachable."))

                Case Else
                    Return ResourceResult(Of SmbConnectionLease).Fail(
                        New ResourceFailure(ResourceFailureKind.LocalIO, rc, "WNet error " & rc.ToString()))
            End Select
        End SyncLock
    End Function

    ''' <summary>
    ''' Releases a connection lease. If the lease count reaches 0, removes the lease and
    ''' disconnects with WNetCancelConnection2 only if the connection was created by our app.
    ''' </summary>
    Friend Sub ReleaseLease(lease As SmbConnectionLease)
        If lease Is Nothing Then Return
        SyncLock gate
            Dim entry As SmbLeaseEntry = Nothing
            If activeLeases.TryGetValue(lease.Key, entry) Then
                entry.LeaseCount -= 1
                If entry.LeaseCount <= 0 Then
                    activeLeases.Remove(lease.Key)
                    If entry.IsAppCreated Then
                        ' Windows may accept another spelling of the same account. Such a lease
                        ' has a different key but holds the same connection; wait for its last user.
                        For Each active As SmbLeaseEntry In activeLeases.Values
                            If String.Equals(active.UncRoot, entry.UncRoot, StringComparison.OrdinalIgnoreCase) Then Return
                        Next
                        Try
                            wnetApi.WNetCancelConnection2(entry.UncRoot, 0, False)
                        Catch
                            ' Disconnect best effort
                        End Try
                    End If
                End If
            End If
        End SyncLock
    End Sub

    ''' <summary>Number of unique active connection keys tracked by this manager.</summary>
    Friend ReadOnly Property ActiveCount As Integer
        Get
            SyncLock gate
                Return activeLeases.Count
            End SyncLock
        End Get
    End Property

    ''' <summary>Returns active lease count for a specific UNC root and user (0 if none).</summary>
    Friend Function GetActiveLeaseCount(uncRoot As String, userName As String) As Integer
        Dim key As String = SmbPathUtil.BuildConnectionKey(uncRoot, userName)
        SyncLock gate
            Dim entry As SmbLeaseEntry = Nothing
            If activeLeases.TryGetValue(key, entry) Then
                Return entry.LeaseCount
            End If
            Return 0
        End SyncLock
    End Function

    ''' <summary>Returns whether an active lease was created by the app (Nothing if no active lease).</summary>
    Friend Function IsAppCreated(uncRoot As String, userName As String) As Boolean?
        Dim key As String = SmbPathUtil.BuildConnectionKey(uncRoot, userName)
        SyncLock gate
            Dim entry As SmbLeaseEntry = Nothing
            If activeLeases.TryGetValue(key, entry) Then
                Return entry.IsAppCreated
            End If
            Return Nothing
        End SyncLock
    End Function

End Class
#End If
