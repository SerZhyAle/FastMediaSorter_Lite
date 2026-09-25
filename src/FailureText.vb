Option Strict On

Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Reflection
Imports System.Security

''' <summary>
''' What the user reads when something they asked for failed - APP-BEHAVIOUR rule 6
''' (028_SPECIFICATION_CONTRACT_SYNC_DESKTOP_APP_UX.md Ф-3). The exception itself goes to
''' the log; the user gets a named cause and what to do next.
'''
''' The cause is read from the exception's TYPE, never from its text: the text is in
''' whatever language the OS speaks, differs per Windows build, and is exactly the "an
''' error occurred" sentence the rule exists to replace. The causes are the ones a user
''' acts on differently - a network to check, a disk or share to wake, space to free, a
''' file that moved, a lock or a permission, a damaged file - plus the honest unknown,
''' which points at the log the user can send.
'''
''' This is the ONE place an exception's Message may reach the screen, and only on the x86
''' fallback: the maintenance policy freezes that build, so it keeps showing the raw text
''' it always showed, under a dated exception in the contract registry. A gate
''' (DesktopUxConformanceTests) fails on a Message used anywhere else outside a log call.
''' </summary>
Public Module FailureText

    Public Enum FailureCause
        ''' <summary>A server or the internet did not answer.</summary>
        Network
        ''' <summary>A disk or a network folder stopped answering mid-operation.</summary>
        Storage
        ''' <summary>The disk is full.</summary>
        DiskFull
        ''' <summary>The file or folder is not there.</summary>
        Missing
        ''' <summary>A lock or a permission said no.</summary>
        Denied
        ''' <summary>The bytes arrived and are not what they should be.</summary>
        DamagedData
        ''' <summary>Nobody has classified this one; the log has the details.</summary>
        Unknown
    End Enum

    Private Const HResultDiskFull As Integer = &H80070070
    Private Const HResultHandleDiskFull As Integer = &H80070027
    Private Const HResultSharingViolation As Integer = &H80070020
    Private Const HResultLockViolation As Integer = &H80070021

    ''' <summary>The cause, from the exception's type (and, for IOException, its HResult).</summary>
    Public Function CauseOf(ex As Exception) As FailureCause
        Dim real As Exception = Unwrap(ex)
        If real Is Nothing Then Return FailureCause.Unknown

        If TypeOf real Is WebException OrElse
           TypeOf real Is SocketException OrElse
           TypeOf real Is TimeoutException OrElse
           IsTypeNamed(real, "System.Net.Http.HttpRequestException") Then Return FailureCause.Network
        ' HttpClient reports its own timeout as a cancellation wrapping a TimeoutException.
        If TypeOf real Is OperationCanceledException AndAlso
           (TypeOf real.InnerException Is TimeoutException OrElse TypeOf real.InnerException Is SocketException) Then
            Return FailureCause.Network
        End If

        If TypeOf real Is FileNotFoundException OrElse
           TypeOf real Is DirectoryNotFoundException OrElse
           TypeOf real Is DriveNotFoundException Then Return FailureCause.Missing

        If TypeOf real Is UnauthorizedAccessException OrElse
           TypeOf real Is SecurityException Then Return FailureCause.Denied

        If TypeOf real Is InvalidDataException OrElse
           TypeOf real Is EndOfStreamException OrElse
           TypeOf real Is FormatException OrElse
           TypeOf real Is OutOfMemoryException OrElse
           IsTypeNamed(real, "System.Text.Json.JsonException") Then Return FailureCause.DamagedData

        If TypeOf real Is IOException Then
            Select Case real.HResult
                Case HResultDiskFull, HResultHandleDiskFull
                    Return FailureCause.DiskFull
                Case HResultSharingViolation, HResultLockViolation
                    Return FailureCause.Denied
            End Select
            Return FailureCause.Storage
        End If

        Return FailureCause.Unknown
    End Function

    ''' <summary>
    ''' The sentence to show for a failure: its cause and what to do. On the x86 fallback,
    ''' the exception's own text, exactly as that build always showed it.
    ''' </summary>
    Public Function Describe(ex As Exception) As String
#If NETFRAMEWORK Then
        Return If(ex Is Nothing, "", ex.Message)
#Else
        Return Sentence(CauseOf(ex))
#End If
    End Function

    ''' <summary>The localized cause-and-action sentence.</summary>
    Public Function Sentence(cause As FailureCause) As String
        Select Case cause
            Case FailureCause.Network
                Return Localization.T("Нет связи с сервером. Проверьте подключение к интернету и повторите попытку.")
            Case FailureCause.Storage
                Return Localization.T("Диск или сетевая папка не отвечает. Проверьте подключение и повторите попытку.")
            Case FailureCause.DiskFull
                Return Localization.T("На диске не хватает места. Освободите место и повторите попытку.")
            Case FailureCause.Missing
                Return Localization.T("Файл или папка не найдены - возможно, их переместили или удалили.")
            Case FailureCause.Denied
                Return Localization.T("Нет доступа: файл занят другой программой или не хватает прав. Закройте её или проверьте права и повторите попытку.")
            Case FailureCause.DamagedData
                Return Localization.T("Данные повреждены или в неподдерживаемом формате.")
            Case Else
                Return Localization.T("Непредвиденная ошибка. Подробности записаны в журнал - его можно отправить автору: Настройки - О программе.")
        End Select
    End Function

    Private Function IsTypeNamed(ex As Exception, fullName As String) As Boolean
        Dim t As Type = ex.GetType()
        While t IsNot Nothing
            If String.Equals(t.FullName, fullName, StringComparison.Ordinal) Then Return True
            t = t.BaseType
        End While
        Return False
    End Function

    ''' <summary>The real failure under the async and reflection wrappers - bounded, as in
    ''' PathFailure, so a corrupt chain cannot spin the caller.</summary>
    Private Function Unwrap(ex As Exception) As Exception
        Dim current As Exception = ex
        For hop As Integer = 0 To 7
            If current Is Nothing Then Exit For
            Dim aggregate As AggregateException = TryCast(current, AggregateException)
            If aggregate IsNot Nothing AndAlso aggregate.InnerExceptions.Count > 0 Then
                current = aggregate.InnerExceptions(0)
                Continue For
            End If
            If TypeOf current Is TargetInvocationException AndAlso current.InnerException IsNot Nothing Then
                current = current.InnerException
                Continue For
            End If
            ' A WebException or HttpRequestException carrying a socket error is still the
            ' network; anything else wrapping an inner exception is classified as itself.
            Exit For
        Next
        Return current
    End Function

End Module
