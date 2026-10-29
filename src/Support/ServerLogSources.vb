#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Globalization
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.RegularExpressions

''' <summary>Specification 272: local owned logs only. No process, IPC, remote access or configuration-file reads.</summary>
Friend Module ServerLogSources
    Private ReadOnly Rotation As String = "(?:\.[0-9]{1,6}|\.old)?"
    Private ReadOnly ExchangeName As New Regex("^(?:exchange(?:-[0-9]{4}-[0-9]{2}-[0-9]{2})?\.log|service\.log|install-exchange-service\.log|enable-exchange-server\.log)" & Rotation & "$", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
    Private ReadOnly ShareName As New Regex("^(?:service|security|worker|fms-share-worker|install-share-service|enable-share-server)\.log" & Rotation & "$", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
    Private ReadOnly HelperName As New Regex("^(?:(?:install-(?:share|exchange)-service|enable-(?:share|exchange)-server)\.log" & Rotation & "|fms-setup(?:-[0-9]{8}(?:-[0-9]{6})?)?\.log" & Rotation & ")$", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
    Private ReadOnly AppName As New Regex("^(?:current|companion|previous|companion\.previous)\.log" & Rotation & "$", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)

    ''' <summary>Windows service --config is the server's first directory-resolution rule. Do not launch inspect.</summary>
    Friend Function ServiceDataDirectory(image As String) As String
        Dim config As String = CommandArgument(image, "--config")
        If config.Length = 0 Then Return ""
        Return Path.GetDirectoryName(Path.GetFullPath(config))
    End Function

    Friend Function WorkerServiceDataDirectory(image As String) As String
        Dim folder As String = CommandArgument(image, "--datadir")
        Return If(folder.Length = 0, "", Path.GetFullPath(folder))
    End Function

    Private Function CommandArgument(image As String, flag As String) As String
        If String.IsNullOrEmpty(image) Then Return ""
        Dim match As Match = Regex.Match(image, "(?:^|\s)" & Regex.Escape(flag) & "(?:=|\s+)(?:""(?<path>[^""]+)""|(?<path>\S+))", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
        Return If(match.Success, Environment.ExpandEnvironmentVariables(match.Groups("path").Value), "")
    End Function

    Friend Function Discover(roots As LogPackage.LogSourceRoots,
                             Optional enumerate As Func(Of String, IEnumerable(Of String)) = Nothing) As List(Of LogPackage.LogSourceSpec)
        Dim result As New List(Of LogPackage.LogSourceSpec)()
        Dim seenFiles As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim seenDirectories As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim list As Func(Of String, IEnumerable(Of String)) = enumerate
        If list Is Nothing Then list = Function(folder) Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)

        If roots.RegistrationFailure Then
            result.Add(New LogPackage.LogSourceSpec With {.EntryName = "exchange/service-registration", .DiscoveryFailure = LogPackage.LogReadFailure.AccessDenied, .IsServer = True})
        End If
        If roots.WorkerRegistrationFailure Then
            result.Add(New LogPackage.LogSourceSpec With {.EntryName = "share/service-registration", .DiscoveryFailure = LogPackage.LogReadFailure.AccessDenied, .IsServer = True})
        End If
        For Each store In New KeyValuePair(Of String, String)() {
            New KeyValuePair(Of String, String)("service", roots.ExchangeServiceDataDir),
            New KeyValuePair(Of String, String)("override", roots.ExchangeDataDirOverride),
            New KeyValuePair(Of String, String)("environment-machine", Combine(roots.ServerCommonAppData, "FastMediaSorterExchange")),
            New KeyValuePair(Of String, String)("environment-user", Combine(roots.ServerLocalAppData, "FastMediaSorterExchange")),
            New KeyValuePair(Of String, String)("environment-config", If(String.IsNullOrEmpty(roots.ServerLocalAppData), Combine(roots.ServerUserConfig, "FastMediaSorterExchange"), "")),
            New KeyValuePair(Of String, String)("machine", Combine(roots.CommonAppData, "FastMediaSorterExchange")),
            New KeyValuePair(Of String, String)("user", Combine(roots.LocalAppData, "FastMediaSorterExchange")),
            New KeyValuePair(Of String, String)("user-config", If(String.IsNullOrEmpty(roots.LocalAppData), Combine(roots.RoamingAppData, "FastMediaSorterExchange"), ""))}
            Collect(store.Value, "exchange/" & store.Key & "/root", ExchangeName, True, list, result, seenDirectories, seenFiles)
            Collect(Combine(store.Value, "logs"), "exchange/" & store.Key, ExchangeName, True, list, result, seenDirectories, seenFiles)
        Next
        For Each store In New KeyValuePair(Of String, String)() {
            New KeyValuePair(Of String, String)("service", roots.WorkerServiceDataDir),
            New KeyValuePair(Of String, String)("override", roots.WorkerDataDirOverride),
            New KeyValuePair(Of String, String)("environment-machine", Combine(roots.ServerCommonAppData, "FastMediaSorterCompanion")),
            New KeyValuePair(Of String, String)("environment-user", Combine(roots.ServerLocalAppData, "FastMediaSorterCompanion")),
            New KeyValuePair(Of String, String)("machine", Combine(roots.CommonAppData, "FastMediaSorterCompanion")),
            New KeyValuePair(Of String, String)("user", Combine(roots.LocalAppData, "FastMediaSorterCompanion"))}
            Collect(store.Value, "share/" & store.Key, ShareName, True, list, result, seenDirectories, seenFiles)
            Collect(Combine(store.Value, "logs"), "share/" & store.Key & "/logs", ShareName, True, list, result, seenDirectories, seenFiles)
        Next
        ' Helper fallbacks are not arbitrary Setup Log *.txt files belonging to other installers.
        Collect(roots.TempDir, "setup/user-temp", HelperName, True, list, result, seenDirectories, seenFiles)
        Collect(roots.SystemTempDir, "setup/system-temp", HelperName, True, list, result, seenDirectories, seenFiles)

        Collect(roots.ExeDir, "application/installed", AppName, False, list, result, seenDirectories, seenFiles)
        Collect(Combine(roots.LocalAppData, App_name, Second_App_Name, "logs"), "application/viewer-user", AppName, False, list, result, seenDirectories, seenFiles)
        ' This directory also holds worker state: enumerate again with the application allow-list.
        Collect(Combine(roots.LocalAppData, "FastMediaSorterCompanion"), "application/manager-user", AppName, False, list, result, seenDirectories, seenFiles)
        If Not String.IsNullOrWhiteSpace(roots.OwnLogPath) Then
            Collect(Path.GetDirectoryName(roots.OwnLogPath), "application/current-store", AppName, False, list, result, seenDirectories, seenFiles)
        End If
        Return result.OrderBy(Function(source) source.EntryName, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Private Sub Collect(folder As String, label As String, allowed As Regex, server As Boolean,
                        enumerate As Func(Of String, IEnumerable(Of String)), result As List(Of LogPackage.LogSourceSpec),
                        seenDirectories As HashSet(Of String), seenFiles As HashSet(Of String))
        If String.IsNullOrWhiteSpace(folder) Then Return
        Dim directoryObserved As Boolean
        Try
            Dim canonical As String = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))
            If canonical.StartsWith("\\", StringComparison.Ordinal) OrElse New DriveInfo(Path.GetPathRoot(canonical)).DriveType = DriveType.Network Then
                result.Add(Failed(label & "/directory", LogPackage.LogReadFailure.NonLocal, server))
                Return
            End If
            ' The allow-list is part of directory identity: one store can hold both application and worker logs.
            If Not seenDirectories.Add(canonical & "|" & allowed.ToString()) Then Return
            If HasReparsePoint(canonical) Then
                result.Add(Failed(label & "/directory", LogPackage.LogReadFailure.ReparsePoint, server))
                Return
            End If
            directoryObserved = True
            ' Enumerate before opening any source. Missing optional directories are normal; access failures are not.
            For Each file As String In enumerate(canonical)
                Dim name As String = Path.GetFileName(file)
                If Not allowed.IsMatch(name) Then Continue For
                If name.StartsWith("exchange-", StringComparison.OrdinalIgnoreCase) Then
                    Dim day As DateTime
                    If Not DateTime.TryParseExact(name.Substring(9, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, day) Then Continue For
                End If
                Dim full As String = Path.GetFullPath(file)
                If Not String.Equals(Path.GetDirectoryName(full), canonical, StringComparison.OrdinalIgnoreCase) Then Continue For
                If Not seenFiles.Add(full) Then Continue For
                Dim spec As New LogPackage.LogSourceSpec With {.EntryName = label & "/" & name.ToLowerInvariant(),
                    .Candidates = {full}, .Discovered = True, .IsServer = server}
                Try
                    If HasReparsePoint(full) Then spec.DiscoveryFailure = LogPackage.LogReadFailure.ReparsePoint
                Catch ex As Exception
                    spec.DiscoveryFailure = If(LogPackage.IsMissingFailure(ex), LogPackage.LogReadFailure.Disappeared, LogPackage.ClassifyReadFailure(ex))
                End Try
                result.Add(spec)
            Next
        Catch ex As Exception
            If LogPackage.IsMissingFailure(ex) AndAlso Not directoryObserved Then Return
            Dim reason As LogPackage.LogReadFailure = If(LogPackage.IsMissingFailure(ex), LogPackage.LogReadFailure.Disappeared, LogPackage.ClassifyReadFailure(ex))
            If reason = LogPackage.LogReadFailure.Other Then reason = LogPackage.LogReadFailure.EnumerationFailed
            result.Add(Failed(label & "/directory", reason, server))
        End Try
    End Sub

    Private Function Failed(label As String, reason As LogPackage.LogReadFailure, server As Boolean) As LogPackage.LogSourceSpec
        Return New LogPackage.LogSourceSpec With {.EntryName = label, .DiscoveryFailure = reason, .IsServer = server}
    End Function

    ''' <summary>Reject the file AND every ancestor junction. Never traverse a link outside a supported root.</summary>
    Friend Function HasReparsePoint(sourcePath As String) As Boolean
        Dim current As String = Path.GetFullPath(sourcePath)
        While Not String.IsNullOrEmpty(current)
            If (File.GetAttributes(current) And FileAttributes.ReparsePoint) <> 0 Then Return True
            current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current))
        End While
        Return False
    End Function

    Private Function Combine(root As String, ParamArray parts As String()) As String
        If String.IsNullOrWhiteSpace(root) Then Return ""
        Return Path.Combine(New String() {root}.Concat(parts).ToArray())
    End Function
End Module
#End If
