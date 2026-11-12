Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' A <see cref="FactAttribute"/> for a test that needs the REAL <c>fms-exchange-server.exe</c> (ticket L0278 T-9, specification 7.5).
''' The older binary tests end in <c>If Not _hasBinary Then Return</c>, which xUnit 2.9.2 reports as a PASS; here the constructor sets
''' <see cref="FactAttribute.Skip"/> when no binary is found, so a run without one reports <c>skipped=N</c> and cannot be read as proof.
'''
''' Where the binary is looked for, in this order: the file named by <c>FMS_EXCHANGE_TEST_EXE</c> (the variable the neighbouring
''' binary tests honour, for a fresh <c>go build</c> into a scratch folder), <c>bin\Release\exchange\fms-exchange-server.exe</c> (the
''' staged distribution shape, the path the specification names) and <c>exchange-server\bin\fms-exchange-server.exe</c> (the output of
''' <c>exchange-server\build.ps1</c>).
'''
''' <c>FMS_REQUIRE_BINARY=1</c> (the release preflight's switch, the pattern of <c>FMS263_REQUIRE_BINARY</c>) turns a missing binary
''' from a skip into a FAILURE: the attribute then sets no <c>Skip</c>, the test runs, and its own first assertion
''' (<see cref="RemoteAdminBinaryIntegrationTests"/>) fails with the reason. A run that was asked to prove something must not end green
''' by not running.
'''
''' The decision is split into small pure functions (<see cref="Candidates"/>, <see cref="LocateIn"/>, <see cref="SkipReason"/>) so a
''' fact can call them over a path that does not exist and see the skip reason without renaming any real file.
''' </summary>
<AttributeUsage(AttributeTargets.Method, AllowMultiple:=False)>
Public NotInheritable Class RealBinaryFactAttribute
    Inherits FactAttribute

    ''' <summary>The release preflight's switch: <c>1</c> makes a missing binary a failure.</summary>
    Friend Const RequireVariable As String = "FMS_REQUIRE_BINARY"
    ''' <summary>An explicit exe, as the neighbouring binary tests take it.</summary>
    Friend Const OverrideVariable As String = "FMS_EXCHANGE_TEST_EXE"
    ''' <summary>The skip reason a run reports when there is no binary and nothing requires one.</summary>
    Friend Const NotBuilt As String = "the server binary is not built"

    Public Sub New()
        MyBase.New()
        Dim reason As String = SkipReason(Locate(), Environment.GetEnvironmentVariable(RequireVariable))
        If reason IsNot Nothing Then Skip = reason
    End Sub

    ''' <summary>The first existing candidate of this machine, or Nothing.</summary>
    Friend Shared Function Locate() As String
        Dim root As String = Nothing
        Try
            root = ExchangeTestSupport.RepoRoot()
        Catch ex As InvalidOperationException
            ' no repository above the test binary (a copied output): only the explicit variable can name a binary then
        End Try
        Return LocateIn(Candidates(root, Environment.GetEnvironmentVariable(OverrideVariable)))
    End Function

    ''' <summary>The paths to try, in order. <paramref name="repoRoot"/> may be Nothing (then only the explicit path is listed).</summary>
    Friend Shared Function Candidates(repoRoot As String, explicitPath As String) As List(Of String)
        Dim list As New List(Of String)()
        If Not String.IsNullOrEmpty(explicitPath) Then list.Add(explicitPath)
        If Not String.IsNullOrEmpty(repoRoot) Then
            list.Add(Path.Combine(repoRoot, "bin", "Release", "exchange", ExchangeProcess.ExeName))
            list.Add(Path.Combine(repoRoot, "exchange-server", "bin", ExchangeProcess.ExeName))
        End If
        Return list
    End Function

    ''' <summary>The first of <paramref name="paths"/> that is an existing file, or Nothing.</summary>
    Friend Shared Function LocateIn(paths As IEnumerable(Of String)) As String
        For Each p As String In paths
            If Not String.IsNullOrEmpty(p) AndAlso File.Exists(p) Then Return p
        Next
        Return Nothing
    End Function

    ''' <summary>The skip reason for a located binary (or Nothing for none) and the value of <c>FMS_REQUIRE_BINARY</c>: Nothing means
    ''' "run the test" (a binary is there, or one is required and its absence must fail in the test body).</summary>
    Friend Shared Function SkipReason(binary As String, requireFlag As String) As String
        If Not String.IsNullOrEmpty(binary) Then Return Nothing
        If String.Equals(requireFlag, "1", StringComparison.Ordinal) Then Return Nothing
        Return NotBuilt
    End Function

End Class

''' <summary>
''' A <see cref="FactAttribute"/> for the opt-in test against a real, owner-run server (specification 7.7): skipped unless both
''' <c>FMS_REMOTE_ADMIN_HOST</c> and <c>FMS_REMOTE_ADMIN_PASSWORD</c> are set. The skip text names neither variable's value and the
''' test never prints, logs or asserts on either (the host, the password and any connection string never enter a tracked file, a log, a
''' test name or a report).
''' </summary>
<AttributeUsage(AttributeTargets.Method, AllowMultiple:=False)>
Public NotInheritable Class RemoteAdminLiveHostFactAttribute
    Inherits FactAttribute

    Friend Const HostVariable As String = "FMS_REMOTE_ADMIN_HOST"
    Friend Const PasswordVariable As String = "FMS_REMOTE_ADMIN_PASSWORD"
    Friend Const NotConfigured As String = "no live test host is configured for this run"

    Public Sub New()
        MyBase.New()
        Dim reason As String = SkipReason(Environment.GetEnvironmentVariable(HostVariable), Environment.GetEnvironmentVariable(PasswordVariable))
        If reason IsNot Nothing Then Skip = reason
    End Sub

    ''' <summary>Nothing when both values are present (the test runs), otherwise the skip text.</summary>
    Friend Shared Function SkipReason(host As String, password As String) As String
        If String.IsNullOrWhiteSpace(host) OrElse String.IsNullOrEmpty(password) Then Return NotConfigured
        Return Nothing
    End Function

End Class
