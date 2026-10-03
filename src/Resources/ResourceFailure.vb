#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Net.Sockets
Imports System.Threading

''' <summary>Why a remote or local resource call did not succeed - the classes a caller
''' must be able to tell apart (100 §15.4 item 3): a retry policy, a refusal and a prompt
''' each hang off a different one.</summary>
Friend Enum ResourceFailureKind
    None = 0
    ''' <summary>The link failed or timed out; a later attempt may work.</summary>
    Transport = 1
    ''' <summary>The server refused the credential. Never retried with the same one.</summary>
    Auth = 2
    ''' <summary>The host key is not the pinned one. The whole attempt stops.</summary>
    KeyMismatch = 3
    ''' <summary>The server answered with a status (permission denied, no such file ..).
    ''' <see cref="ResourceFailure.StatusCode"/> carries it.</summary>
    SftpStatus = 4
    Cancelled = 5
    ''' <summary>The provider does not do this (a verb its capabilities lack).</summary>
    Unsupported = 6
    ''' <summary>The cache/disk side on this machine failed.</summary>
    LocalIO = 7
    Timeout = 8
    ''' <summary>Windows error 1219: multiple connections by same user with different credentials.
    ''' Typed refusal without force-disconnecting existing external mappings.</summary>
    CredentialConflict = 9
End Enum

''' <summary>
''' An immutable failure that carries no secret and no raw payload: only a class, an
''' optional protocol status code and a short detail the caller may log. Retryability is a
''' property of the class, decided once here (100 §15.3: auth/key/status failures stop
''' immediately; transport and timeout are the ones a transparent reconnect may repeat).
''' </summary>
Friend NotInheritable Class ResourceFailure

    Friend Sub New(kind As ResourceFailureKind, Optional statusCode As Integer? = Nothing, Optional detail As String = "")
        Me.Kind = kind
        Me.StatusCode = statusCode
        Me.Detail = If(detail, "")
    End Sub

    Friend ReadOnly Property Kind As ResourceFailureKind
    Friend ReadOnly Property StatusCode As Integer?
    Friend ReadOnly Property Detail As String

    ''' <summary>True only where waiting and trying again can change the outcome.</summary>
    Friend ReadOnly Property IsRetryable As Boolean
        Get
            Return Kind = ResourceFailureKind.Transport OrElse Kind = ResourceFailureKind.Timeout
        End Get
    End Property

    ''' <summary>A failure that must end the whole attempt, not just one candidate.</summary>
    Friend ReadOnly Property IsFatalForAttempt As Boolean
        Get
            Return Kind = ResourceFailureKind.KeyMismatch OrElse Kind = ResourceFailureKind.Auth OrElse
                   Kind = ResourceFailureKind.Cancelled OrElse Kind = ResourceFailureKind.CredentialConflict
        End Get
    End Property

    ''' <summary>Class and code, never the detail's source: safe for any log line.</summary>
    Public Overrides Function ToString() As String
        Return Kind.ToString() & If(StatusCode.HasValue, " (" & StatusCode.Value.ToString(Globalization.CultureInfo.InvariantCulture) & ")", "")
    End Function

    ''' <summary>
    ''' Classifies what a provider threw. A <see cref="ResourceFailureException"/> keeps its own
    ''' class; the framework's exceptions map by type. Anything unknown is LocalIO rather than
    ''' Transport, so an unrecognised bug is never silently retried as a network blip.
    ''' </summary>
    Friend Shared Function FromException(ex As Exception) As ResourceFailure
        If ex Is Nothing Then Return New ResourceFailure(ResourceFailureKind.None)
        Dim typed As ResourceFailureException = TryCast(ex, ResourceFailureException)
        If typed IsNot Nothing Then Return typed.Failure
        Dim aggregate As AggregateException = TryCast(ex, AggregateException)
        If aggregate IsNot Nothing AndAlso aggregate.InnerExceptions.Count = 1 Then Return FromException(aggregate.InnerExceptions(0))
        If TypeOf ex Is OperationCanceledException Then Return New ResourceFailure(ResourceFailureKind.Cancelled)
        If TypeOf ex Is TimeoutException Then Return New ResourceFailure(ResourceFailureKind.Timeout)
        If TypeOf ex Is SocketException Then Return New ResourceFailure(ResourceFailureKind.Transport, DirectCast(ex, SocketException).ErrorCode)
        If TypeOf ex Is System.ComponentModel.Win32Exception Then
            Dim win32Ex = DirectCast(ex, System.ComponentModel.Win32Exception)
            If win32Ex.NativeErrorCode = 1219 Then
                Return New ResourceFailure(ResourceFailureKind.CredentialConflict, 1219, "credential conflict")
            ElseIf win32Ex.NativeErrorCode = 5 OrElse win32Ex.NativeErrorCode = 1326 OrElse win32Ex.NativeErrorCode = 86 Then
                Return New ResourceFailure(ResourceFailureKind.Auth, win32Ex.NativeErrorCode, "auth failure")
            ElseIf win32Ex.NativeErrorCode = 53 OrElse win32Ex.NativeErrorCode = 67 OrElse win32Ex.NativeErrorCode = 1203 OrElse win32Ex.NativeErrorCode = 1204 Then
                Return New ResourceFailure(ResourceFailureKind.Transport, win32Ex.NativeErrorCode, "transport failure")
            End If
        End If
        If TypeOf ex Is UnauthorizedAccessException Then Return New ResourceFailure(ResourceFailureKind.LocalIO, Nothing, "access denied")
        If TypeOf ex Is NotSupportedException OrElse TypeOf ex Is NotImplementedException Then Return New ResourceFailure(ResourceFailureKind.Unsupported)
        If TypeOf ex Is IOException Then Return New ResourceFailure(ResourceFailureKind.LocalIO, Nothing, ex.GetType().Name)
        Return New ResourceFailure(ResourceFailureKind.LocalIO, Nothing, ex.GetType().Name)
    End Function

End Class

''' <summary>The exception form of a <see cref="ResourceFailure"/>, for the provider verbs whose
''' signature is a plain Sub. The message is the class and code only.</summary>
Friend NotInheritable Class ResourceFailureException
    Inherits Exception

    Friend Sub New(failure As ResourceFailure)
        MyBase.New(If(failure Is Nothing, "ResourceFailure", failure.ToString()))
        Me.Failure = If(failure, New ResourceFailure(ResourceFailureKind.None))
    End Sub

    Friend ReadOnly Property Failure As ResourceFailure
End Class

''' <summary>A success value or a failure - never an empty success standing in for an error.</summary>
Friend NotInheritable Class ResourceResult(Of T)

    Private Sub New(value As T, failure As ResourceFailure)
        Me.Value = value
        Me.Failure = failure
    End Sub

    Friend ReadOnly Property Value As T
    Friend ReadOnly Property Failure As ResourceFailure
    Friend ReadOnly Property Succeeded As Boolean
        Get
            Return Failure Is Nothing
        End Get
    End Property

    Friend Shared Function Ok(value As T) As ResourceResult(Of T)
        Return New ResourceResult(Of T)(value, Nothing)
    End Function

    Friend Shared Function Fail(failure As ResourceFailure) As ResourceResult(Of T)
        If failure Is Nothing Then Throw New ArgumentNullException(NameOf(failure))
        Return New ResourceResult(Of T)(Nothing, failure)
    End Function
End Class

''' <summary>What asking "does this path exist" came to. Missing is a fact about the server;
''' Denied and Failed are NOT - treating them as missing would let an overwrite or a
''' "file is gone" verdict rest on a permission error or a dropped link.</summary>
Friend Enum StatOutcome
    Found = 0
    Missing = 1
    ''' <summary>The server or the OS refused the question (permission).</summary>
    Denied = 2
    ''' <summary>Transport, timeout, cancellation or anything else: no verdict.</summary>
    Failed = 3
End Enum

Friend NotInheritable Class StatResult

    Private Sub New(outcome As StatOutcome, entry As ResourceEntry, failure As ResourceFailure)
        Me.Outcome = outcome
        Me.Entry = entry
        Me.Failure = failure
    End Sub

    Friend ReadOnly Property Outcome As StatOutcome
    Friend ReadOnly Property Entry As ResourceEntry
    Friend ReadOnly Property Failure As ResourceFailure

    Friend ReadOnly Property Exists As Boolean
        Get
            Return Outcome = StatOutcome.Found
        End Get
    End Property

    ''' <summary>True only when the answer is a definite "not there". The one value an
    ''' overwrite decision may treat as "free".</summary>
    Friend ReadOnly Property IsDefinitelyMissing As Boolean
        Get
            Return Outcome = StatOutcome.Missing
        End Get
    End Property

    Friend Shared Function Found(entry As ResourceEntry) As StatResult
        Return New StatResult(StatOutcome.Found, entry, Nothing)
    End Function

    Friend Shared Function Missing() As StatResult
        Return New StatResult(StatOutcome.Missing, Nothing, Nothing)
    End Function

    Friend Shared Function Denied(Optional failure As ResourceFailure = Nothing) As StatResult
        Return New StatResult(StatOutcome.Denied, Nothing, If(failure, New ResourceFailure(ResourceFailureKind.SftpStatus, 3)))
    End Function

    Friend Shared Function Failed(failure As ResourceFailure) As StatResult
        Return New StatResult(StatOutcome.Failed, Nothing, If(failure, New ResourceFailure(ResourceFailureKind.Transport)))
    End Function
End Class

''' <summary>A provider that can tell Missing from Denied from a dead link implements this;
''' the legacy bool <see cref="IFileSystemProvider.TryStat"/> stays for callers that do not
''' care. Optional by design, so a fake provider in a test needs nothing new.</summary>
Friend Interface ITypedStatProvider
    Function Stat(path As String) As StatResult
End Interface

''' <summary>The adapters: one place where a legacy provider is read as a typed one.</summary>
Friend Module ResourceProviderCalls

    ''' <summary>Typed stat. A provider without <see cref="ITypedStatProvider"/> falls back to
    ''' the bool call: False becomes Missing (the only thing that call ever meant), and an
    ''' exception becomes a Failed result instead of escaping into a caller's "not there" branch.</summary>
    Friend Function Stat(provider As IFileSystemProvider, path As String) As StatResult
        If provider Is Nothing Then Throw New ArgumentNullException(NameOf(provider))
        Try
            Dim typed As ITypedStatProvider = TryCast(provider, ITypedStatProvider)
            If typed IsNot Nothing Then Return typed.Stat(path)
            Dim entry As ResourceEntry = Nothing
            Return If(provider.TryStat(path, entry), StatResult.Found(entry), StatResult.Missing())
        Catch ex As Exception
            Return StatResult.Failed(ResourceFailure.FromException(ex))
        End Try
    End Function

    ''' <summary>A listing that cannot be read is a failure with a class, never an empty
    ''' success: the caller keeps the prior listing and says why.</summary>
    Friend Function ListChecked(provider As IFileSystemProvider, dirPath As String, ct As CancellationToken) As ResourceResult(Of IReadOnlyList(Of ResourceEntry))
        If provider Is Nothing Then Throw New ArgumentNullException(NameOf(provider))
        Try
            Dim listed As IReadOnlyList(Of ResourceEntry) = provider.ListEntries(dirPath, ct)
            If listed Is Nothing Then Return ResourceResult(Of IReadOnlyList(Of ResourceEntry)).Fail(New ResourceFailure(ResourceFailureKind.Transport, Nothing, "no listing"))
            Return ResourceResult(Of IReadOnlyList(Of ResourceEntry)).Ok(listed)
        Catch ex As Exception
            Return ResourceResult(Of IReadOnlyList(Of ResourceEntry)).Fail(ResourceFailure.FromException(ex))
        End Try
    End Function

End Module
#End If
