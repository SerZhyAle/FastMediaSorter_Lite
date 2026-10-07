#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports System.Text
Imports Xunit
#If COMPANION_TESTS Then
Imports FastMediaSorterCompanion
#End If

''' <summary>
''' Ticket 182 L-2, L-3 and L-7 for the archive "Send the log to the author" builds: WHICH logs go in, under
''' which folder-qualified names, from which store first; that a log which EXISTS but cannot be read is
''' named (and a missing one is not); and that no needle planted in any log, and nothing of the forbidden set,
''' ever reaches report.txt.
'''
''' This file is compiled TWICE, into Lite.Tests (the viewer's call) and into Companion.Tests (the Share
''' Manager's call, <c>COMPANION_TESTS</c>), because both programs link the same <c>LogPackage</c> and must
''' come out with the same sources - the assertions here are the proof, not a comparison of two runs.
'''
''' Nothing here touches the real machine's logs: every layout is seeded into a throw-away folder and passed
''' in as <see cref="LogPackage.LogSourceRoots"/>, and the hosting block is answered by stubs.
''' </summary>
Public Class LogPackageSourcesTests

    ''' <summary>The archive entries of the two products, in the documented order after the program's own log.</summary>
    Private Shared ReadOnly Documented As String() = {
        "current.log", "companion.log",
        "exchange/exchange.log", "exchange/exchange.log.1", "exchange/service.log",
        "exchange/install-exchange-service.log", "exchange/enable-exchange-server.log",
        "share/service.log", "share/install-share-service.log"}

    ' ------------------------------------------------------------------------------ layout ----

    ''' <summary>A whole seeded machine: an exe folder, a per-user store, a ProgramData stand-in and a %TEMP% stand-in.</summary>
    Private NotInheritable Class Layout
        Implements IDisposable

        Public ReadOnly Root As String
        Public ReadOnly Exe As String
        Public ReadOnly Local As String
        Public ReadOnly Common As String
        Public ReadOnly Temp As String
        Public ReadOnly Dest As String

        Public Sub New()
            Root = Path.Combine(Path.GetTempPath(), "fms-logsrc-" & Guid.NewGuid().ToString("N"))
            Exe = Path.Combine(Root, "exe")
            Local = Path.Combine(Root, "local")
            Common = Path.Combine(Root, "common")
            Temp = Path.Combine(Root, "temp")
            Dest = Path.Combine(Root, "dest")
            For Each dir As String In {Exe, Local, Common, Temp, Dest}
                Directory.CreateDirectory(dir)
            Next
        End Sub

        Public Function Roots(Optional ownLog As String = Nothing, Optional exchangeOverride As String = "") As LogPackage.LogSourceRoots
            Return New LogPackage.LogSourceRoots With {
                .ExeDir = Exe, .LocalAppData = Local, .CommonAppData = Common, .TempDir = Temp,
                .ExchangeDataDirOverride = exchangeOverride,
                .OwnLogPath = If(ownLog, Path.Combine(Exe, "current.log"))}
        End Function

        ''' <summary>Writes <paramref name="text"/> to <paramref name="relative"/> under the root and returns its full path.</summary>
        Public Function Put(relative As String, text As String) As String
            Dim full As String = Path.Combine(Root, relative.Replace("/"c, Path.DirectorySeparatorChar))
            Directory.CreateDirectory(Path.GetDirectoryName(full))
            File.WriteAllText(full, text, New UTF8Encoding(False))
            Return full
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                Directory.Delete(Root, True)
            Catch
            End Try
        End Sub
    End Class

    Private Shared Function NoSettings() As List(Of KeyValuePair(Of String, Object))
        Return New List(Of KeyValuePair(Of String, Object))()
    End Function

    Private Shared Function Build(l As Layout, Optional roots As LogPackage.LogSourceRoots? = Nothing,
                                  Optional opener As Func(Of String, Stream) = Nothing,
                                  Optional settings As Func(Of List(Of KeyValuePair(Of String, Object))) = Nothing,
                                  Optional probes As HostingProbes = Nothing) As LogPackage.LogPackageResult
        Return LogPackage.Build(If(settings, AddressOf NoSettings), New DateTime(2026, 10, 7, 12, 0, 0), l.Dest, Nothing,
                                If(roots, l.Roots()), opener, If(probes, HostingProbes.Absent()))
    End Function

    Private Shared Function ReadEntry(zipPath As String, entryName As String) As String
        Using archive As ZipArchive = ZipFile.OpenRead(zipPath)
            Dim entry As ZipArchiveEntry = archive.GetEntry(entryName)
            Assert.True(entry IsNot Nothing, "the archive has no entry " & entryName)
            Using reader As New StreamReader(entry.Open())
                Return reader.ReadToEnd()
            End Using
        End Using
    End Function

    Private Shared Function EntryNames(zipPath As String) As List(Of String)
        Using archive As ZipArchive = ZipFile.OpenRead(zipPath)
            Return archive.Entries.Select(Function(e) e.FullName).ToList()
        End Using
    End Function

    ' --------------------------------------------------------------------------- the names -----

    <Fact>
    Public Sub Both_programs_plan_the_same_documented_sources_with_folder_qualified_names()
        Using l As New Layout()
            Dim viewer As List(Of String) = LogPackage.PlanLogSources(l.Roots(Path.Combine(l.Exe, "current.log"))).Select(Function(s) s.EntryName).ToList()
            Dim manager As List(Of String) = LogPackage.PlanLogSources(l.Roots(Path.Combine(l.Exe, "companion.log"))).Select(Function(s) s.EntryName).ToList()
            ' The running program's own log leads, under its own file name...
            Assert.Equal("current.log", viewer(0))
            Assert.Equal("companion.log", manager(0))
            ' ...and everything else is the same set, whichever program asks.
            Assert.Equal(Documented.OrderBy(Function(n) n, StringComparer.Ordinal).ToArray(), viewer.OrderBy(Function(n) n, StringComparer.Ordinal).ToArray())
            Assert.Equal(viewer.OrderBy(Function(n) n, StringComparer.Ordinal).ToArray(), manager.OrderBy(Function(n) n, StringComparer.Ordinal).ToArray())
            ' Same-named files of two products can never collide in one archive.
            Assert.Equal(viewer.Count, viewer.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            Assert.Contains("exchange/service.log", viewer)
            Assert.Contains("share/service.log", viewer)
        End Using
    End Sub

    <Fact>
    Public Sub The_machine_store_is_looked_at_before_the_per_user_store_and_the_temp_fallback()
        Using l As New Layout()
            Dim specs As Dictionary(Of String, String()) = LogPackage.PlanLogSources(l.Roots()).ToDictionary(Function(s) s.EntryName, Function(s) s.Candidates)

            Dim exchangeLogs As String() = specs("exchange/exchange.log")
            Assert.Equal({Path.Combine(l.Common, "FastMediaSorterExchange", "logs", "exchange.log"),
                          Path.Combine(l.Local, "FastMediaSorterExchange", "logs", "exchange.log")}, exchangeLogs)

            ' A helper that could not trust the data directory leaves its log in the administrator's %TEMP%.
            Assert.Equal({Path.Combine(l.Common, "FastMediaSorterExchange", "logs", "install-exchange-service.log"),
                          Path.Combine(l.Temp, "install-exchange-service.log")}, specs("exchange/install-exchange-service.log"))
            Assert.Equal({Path.Combine(l.Common, "FastMediaSorterExchange", "logs", "enable-exchange-server.log"),
                          Path.Combine(l.Temp, "enable-exchange-server.log")}, specs("exchange/enable-exchange-server.log"))
            Assert.Equal({Path.Combine(l.Common, "FastMediaSorterCompanion", "service.log"),
                          Path.Combine(l.Local, "FastMediaSorterCompanion", "service.log")}, specs("share/service.log"))
            Assert.Equal({Path.Combine(l.Common, "FastMediaSorterCompanion", "install-share-service.log"),
                          Path.Combine(l.Temp, "install-share-service.log")}, specs("share/install-share-service.log"))
        End Using
    End Sub

    <Fact>
    Public Sub An_exchange_data_directory_override_is_tried_before_the_two_stores()
        Using l As New Layout()
            Dim overrideDir As String = Path.Combine(l.Root, "xd")
            Dim specs As Dictionary(Of String, String()) =
                LogPackage.PlanLogSources(l.Roots(exchangeOverride:=overrideDir)).ToDictionary(Function(s) s.EntryName, Function(s) s.Candidates)
            Assert.Equal(Path.Combine(overrideDir, "logs", "exchange.log"), specs("exchange/exchange.log")(0))
            Assert.Equal(3, specs("exchange/exchange.log").Length)
        End Using
    End Sub

    ' --------------------------------------------------------------------------- the content -----

    <Fact>
    Public Sub A_seeded_machine_produces_every_documented_entry_under_its_qualified_name()
        Using l As New Layout()
            l.Put("exe/current.log", "viewer line" & vbLf)
            l.Put("local/FastMediaSorterCompanion/companion.log", "manager line" & vbLf)
            l.Put("common/FastMediaSorterExchange/logs/exchange.log", "exchange main" & vbLf)
            l.Put("common/FastMediaSorterExchange/logs/exchange.log.1", "exchange rotated" & vbLf)
            l.Put("common/FastMediaSorterExchange/logs/service.log", "exchange service" & vbLf)
            l.Put("common/FastMediaSorterExchange/logs/install-exchange-service.log", "exchange install" & vbLf)
            l.Put("common/FastMediaSorterExchange/logs/enable-exchange-server.log", "exchange firewall" & vbLf)
            l.Put("common/FastMediaSorterCompanion/service.log", "share service" & vbLf)
            l.Put("common/FastMediaSorterCompanion/install-share-service.log", "share install" & vbLf)

            Dim result As LogPackage.LogPackageResult = Build(l)
            Try
                Assert.False(String.IsNullOrEmpty(result.Path), "Build returned no archive: " & If(result.ErrorText, ""))
                Dim names As List(Of String) = EntryNames(result.Path)
                Assert.Equal("report.txt", names(0))
                For Each expected As String In Documented
                    Assert.Contains(expected, names)
                Next
                Assert.Equal("exchange main" & vbLf, ReadEntry(result.Path, "exchange/exchange.log"))
                Assert.Equal("exchange rotated" & vbLf, ReadEntry(result.Path, "exchange/exchange.log.1"))
                Assert.Equal("exchange service" & vbLf, ReadEntry(result.Path, "exchange/service.log"))
                Assert.Equal("exchange install" & vbLf, ReadEntry(result.Path, "exchange/install-exchange-service.log"))
                Assert.Equal("exchange firewall" & vbLf, ReadEntry(result.Path, "exchange/enable-exchange-server.log"))
                Assert.Equal("share service" & vbLf, ReadEntry(result.Path, "share/service.log"))
                Assert.Equal("share install" & vbLf, ReadEntry(result.Path, "share/install-share-service.log"))
                Assert.Equal("viewer line" & vbLf, ReadEntry(result.Path, "current.log"))
                Assert.Equal("manager line" & vbLf, ReadEntry(result.Path, "companion.log"))
                ' Every included source has a line in the consent list, and every line names its entry.
                For Each source As LogPackage.LogEntryOutcome In result.Sources
                    Assert.Equal(LogPackage.LogSourceState.Included, source.State)
                    Assert.Contains(result.Entries, Function(line) line.StartsWith(source.EntryName, StringComparison.Ordinal))
                Next
            Finally
                If Not String.IsNullOrEmpty(result.Path) Then File.Delete(result.Path)
            End Try
        End Using
    End Sub

    <Fact>
    Public Sub The_machine_store_wins_over_the_per_user_store_and_the_per_user_store_over_nothing()
        Using l As New Layout()
            l.Put("common/FastMediaSorterExchange/logs/exchange.log", "MACHINE")
            l.Put("local/FastMediaSorterExchange/logs/exchange.log", "USER")
            l.Put("local/FastMediaSorterExchange/logs/service.log", "ONLY-USER")
            Dim result As LogPackage.LogPackageResult = Build(l)
            Try
                Assert.Equal("MACHINE" & vbLf, ReadEntry(result.Path, "exchange/exchange.log"))
                Assert.Equal("ONLY-USER" & vbLf, ReadEntry(result.Path, "exchange/service.log"))
            Finally
                If Not String.IsNullOrEmpty(result.Path) Then File.Delete(result.Path)
            End Try
        End Using
    End Sub

    <Fact>
    Public Sub A_helper_log_comes_from_the_temp_fallback_only_when_the_machine_store_has_none()
        Using l As New Layout()
            l.Put("temp/install-exchange-service.log", "FROM-TEMP")
            l.Put("temp/enable-exchange-server.log", "TEMP-LOSES")
            l.Put("common/FastMediaSorterExchange/logs/enable-exchange-server.log", "MACHINE-WINS")
            Dim result As LogPackage.LogPackageResult = Build(l)
            Try
                Assert.Equal("FROM-TEMP" & vbLf, ReadEntry(result.Path, "exchange/install-exchange-service.log"))
                Assert.Equal("MACHINE-WINS" & vbLf, ReadEntry(result.Path, "exchange/enable-exchange-server.log"))
            Finally
                If Not String.IsNullOrEmpty(result.Path) Then File.Delete(result.Path)
            End Try
        End Using
    End Sub

    <Fact>
    Public Sub A_log_over_the_cap_contributes_its_tail_and_every_new_source_has_the_user_profile_replaced()
        Using l As New Layout()
            Dim profile As String = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            Dim sb As New StringBuilder()
            For i As Integer = 0 To 60000
                sb.Append("line ").Append(i).Append(" ").Append(New String("x"c, 50)).Append(vbLf)
            Next
            l.Put("common/FastMediaSorterExchange/logs/exchange.log", sb.ToString())
            l.Put("common/FastMediaSorterCompanion/service.log", "opened " & profile & "\Pictures\a.jpg" & vbLf)
            Dim result As LogPackage.LogPackageResult = Build(l)
            Try
                Dim big As String = ReadEntry(result.Path, "exchange/exchange.log")
                Assert.StartsWith("[.. truncated:", big)
                Assert.Contains("line 60000 ", big)
                Assert.True(big.Length < 2.2 * 1024 * 1024, "the entry should stay about the 2 MB cap")
                Assert.Equal("opened <USER>\Pictures\a.jpg" & vbLf, ReadEntry(result.Path, "share/service.log"))
            Finally
                If Not String.IsNullOrEmpty(result.Path) Then File.Delete(result.Path)
            End Try
        End Using
    End Sub

    <Fact>
    Public Sub With_no_logs_at_all_the_archive_holds_the_report_and_says_so_without_naming_any_source()
        Using l As New Layout()
            Dim result As LogPackage.LogPackageResult = Build(l)
            Try
                Assert.Equal({"report.txt"}, EntryNames(result.Path).ToArray())
                Assert.Empty(result.Sources)
                Dim report As String = ReadEntry(result.Path, "report.txt")
                Assert.Contains("[log sources]", report)
                Dim start As Integer = report.IndexOf("[log sources]", StringComparison.Ordinal)
                Dim section As String = report.Substring(start, report.IndexOf("[settings]", start, StringComparison.Ordinal) - start)
                Assert.Contains("no log file found", section)
                ' A source that is not there is not named - not as missing, not as anything.
                For Each name As String In Documented
                    Assert.DoesNotContain(name & ":", section)
                Next
            Finally
                If Not String.IsNullOrEmpty(result.Path) Then File.Delete(result.Path)
            End Try
        End Using
    End Sub

    ' ---------------------------------------------------------------- exists but unreadable -----

    <Fact>
    Public Sub A_log_that_exists_but_cannot_be_read_is_named_with_its_reason_and_a_missing_one_is_silent()
        Using l As New Layout()
            Dim denied As String = l.Put("common/FastMediaSorterExchange/logs/exchange.log", "SECRET-ENOUGH")
            l.Put("common/FastMediaSorterExchange/logs/service.log", "fine" & vbLf)
            l.Put("common/FastMediaSorterCompanion/service.log", "busy")
            Dim busy As String = Path.Combine(l.Common, "FastMediaSorterCompanion", "service.log")
            Dim opener As Func(Of String, Stream) =
                Function(path As String) As Stream
                    If String.Equals(path, denied, StringComparison.OrdinalIgnoreCase) Then Throw New UnauthorizedAccessException("access to the path is denied")
                    If String.Equals(path, busy, StringComparison.OrdinalIgnoreCase) Then Throw New IOException("in use", -2147024864)
                    Return New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                End Function

            Dim result As LogPackage.LogPackageResult = Build(l, opener:=opener)
            Try
                Dim names As List(Of String) = EntryNames(result.Path)
                ' The unreadable ones are NOT in the archive, the readable one is.
                Assert.DoesNotContain("exchange/exchange.log", names)
                Assert.DoesNotContain("share/service.log", names)
                Assert.Contains("exchange/service.log", names)

                Dim byName As Dictionary(Of String, LogPackage.LogEntryOutcome) = result.Sources.ToDictionary(Function(s) s.EntryName)
                Assert.Equal(LogPackage.LogSourceState.Unreadable, byName("exchange/exchange.log").State)
                Assert.Equal(LogPackage.LogReadFailure.AccessDenied, byName("exchange/exchange.log").Failure)
                Assert.Equal(LogPackage.LogSourceState.Unreadable, byName("share/service.log").State)
                Assert.Equal(LogPackage.LogReadFailure.InUse, byName("share/service.log").Failure)
                ' A source with no file at all is not in the list - not as missing, not as anything.
                Assert.False(byName.ContainsKey("exchange/exchange.log.1"))
                Assert.False(byName.ContainsKey("share/install-share-service.log"))

                ' The consent list names the unreadable sources (whatever language the reason is in)...
                Assert.Contains(result.Entries, Function(line) line.Contains("exchange/exchange.log") AndAlso Not line.StartsWith("exchange/exchange.log  (", StringComparison.Ordinal))
                Assert.Contains(result.Entries, Function(line) line.Contains("share/service.log"))
                ' ...and nothing else that does not exist.
                Assert.DoesNotContain(result.Entries, Function(line) line.Contains("exchange.log.1"))

                ' report.txt says the same, in English, with the reason.
                Dim report As String = ReadEntry(result.Path, "report.txt")
                Assert.Contains("exchange/exchange.log: NOT included - access denied", report)
                Assert.Contains("share/service.log: NOT included - in use by another process", report)
                Assert.DoesNotContain("exchange/exchange.log.1", report)
                Assert.DoesNotContain("SECRET-ENOUGH", report)
            Finally
                If Not String.IsNullOrEmpty(result.Path) Then File.Delete(result.Path)
            End Try
        End Using
    End Sub

    ''' <summary>The same through the real opener and a real ACL: a file this account was DENIED reading - the
    ''' G-1 shape of the exchange state directory - is named, never silently absent.</summary>
    <Fact>
    Public Sub A_file_the_account_is_denied_is_reported_as_unreadable_through_the_real_opener()
        Using l As New Layout()
            Dim path As String = l.Put("common/FastMediaSorterExchange/logs/exchange.log", "locked away")
            Dim me_ As SecurityIdentifier = WindowsIdentity.GetCurrent().User
            Dim info As New FileInfo(path)
            Dim security As FileSecurity = info.GetAccessControl()
            security.AddAccessRule(New FileSystemAccessRule(me_, FileSystemRights.ReadData Or FileSystemRights.ReadAttributes, AccessControlType.Deny))
            info.SetAccessControl(security)
            Dim result As LogPackage.LogPackageResult = Nothing
            Try
                result = Build(l)
                Dim outcome As LogPackage.LogEntryOutcome = result.Sources.Single(Function(s) s.EntryName = "exchange/exchange.log")
                Assert.Equal(LogPackage.LogSourceState.Unreadable, outcome.State)
                Assert.Equal(LogPackage.LogReadFailure.AccessDenied, outcome.Failure)
                Assert.DoesNotContain("exchange/exchange.log", EntryNames(result.Path))
                Assert.Contains("exchange/exchange.log: NOT included - access denied", ReadEntry(result.Path, "report.txt"))
            Finally
                If result.Path IsNot Nothing AndAlso result.Path.Length > 0 Then File.Delete(result.Path)
                Try
                    Dim restore As FileSecurity = info.GetAccessControl()
                    restore.RemoveAccessRule(New FileSystemAccessRule(me_, FileSystemRights.ReadData Or FileSystemRights.ReadAttributes, AccessControlType.Deny))
                    info.SetAccessControl(restore)
                Catch
                End Try
            End Try
        End Using
    End Sub

    <Fact>
    Public Sub Reading_failures_are_classified_and_a_missing_file_is_not_one()
        Assert.True(LogPackage.IsMissingFailure(New FileNotFoundException()))
        Assert.True(LogPackage.IsMissingFailure(New DirectoryNotFoundException()))
        Assert.False(LogPackage.IsMissingFailure(New UnauthorizedAccessException()))
        Assert.False(LogPackage.IsMissingFailure(New IOException()))
        Assert.Equal(LogPackage.LogReadFailure.AccessDenied, LogPackage.ClassifyReadFailure(New UnauthorizedAccessException()))
        Assert.Equal(LogPackage.LogReadFailure.AccessDenied, LogPackage.ClassifyReadFailure(New IOException("denied", 5)))
        Assert.Equal(LogPackage.LogReadFailure.InUse, LogPackage.ClassifyReadFailure(New IOException("busy", -2147024864)))   ' 0x80070020
        Assert.Equal(LogPackage.LogReadFailure.InUse, LogPackage.ClassifyReadFailure(New IOException("lock", 33)))
        Assert.Equal(LogPackage.LogReadFailure.Other, LogPackage.ClassifyReadFailure(New IOException("other")))
        Assert.Equal(LogPackage.LogReadFailure.Other, LogPackage.ClassifyReadFailure(New InvalidOperationException()))
        ' Each failure has an English sentence for the author and a Russian key for the user.
        For Each failure As LogPackage.LogReadFailure In {LogPackage.LogReadFailure.AccessDenied, LogPackage.LogReadFailure.InUse, LogPackage.LogReadFailure.Other}
            Assert.False(String.IsNullOrEmpty(LogPackage.ReasonText(failure)))
            Assert.False(String.IsNullOrEmpty(LogPackage.ReasonKey(failure)))
        Next
        Assert.Equal(3, {LogPackage.LogReadFailure.AccessDenied, LogPackage.LogReadFailure.InUse, LogPackage.LogReadFailure.Other}.Select(Function(f) LogPackage.ReasonKey(f)).Distinct().Count())
    End Sub

    ' ------------------------------------------------------------------------ the needles -----

    ''' <summary>A needle in every log (and in the settings, and in a seeded exchange config) never appears in
    ''' report.txt - the report describes, it does not quote - while each log still carries its own needle.</summary>
    <Fact>
    Public Sub A_needle_planted_in_each_log_never_reaches_the_report_and_the_forbidden_set_is_absent()
        Using l As New Layout()
            Dim needles As New Dictionary(Of String, String)()
            Dim locations As String() = {
                "exe/current.log", "local/FastMediaSorterCompanion/companion.log",
                "common/FastMediaSorterExchange/logs/exchange.log", "common/FastMediaSorterExchange/logs/exchange.log.1",
                "common/FastMediaSorterExchange/logs/service.log", "common/FastMediaSorterExchange/logs/install-exchange-service.log",
                "common/FastMediaSorterExchange/logs/enable-exchange-server.log", "common/FastMediaSorterCompanion/service.log",
                "common/FastMediaSorterCompanion/install-share-service.log"}
            For i As Integer = 0 To locations.Length - 1
                Dim needle As String = "NEEDLE-" & i.ToString() & "-" & Guid.NewGuid().ToString("N").Substring(0, 12)
                needles(locations(i)) = needle
                l.Put(locations(i), "ordinary line" & vbLf & "carrying " & needle & vbLf)
            Next
            ' The forbidden set, seeded where a careless report could pick it up: settings and the exchange config.
            Dim secrets As String() = {"alice-the-admin-login", "needle-password-hash-$argon2id", "SHA256:FINGERPRINT-NEEDLE-0123456789",
                                       "198.51.100.77-public-endpoint", "token-needle-9f8e7d6c"}
            l.Put("common/FastMediaSorterExchange/exchange.json",
                  "{""listen"":"":22022"",""portRange"":""40000-40100"",""passwordHash"":""" & secrets(1) & """,""fingerprint"":""" & secrets(2) & """,""endpoint"":""" & secrets(3) & """,""token"":""" & secrets(4) & """}")
            Dim settings As Func(Of List(Of KeyValuePair(Of String, Object))) =
                Function()
                    Return New List(Of KeyValuePair(Of String, Object)) From {
                        New KeyValuePair(Of String, Object)("Exchange_PasswordHint", secrets(1)),
                        New KeyValuePair(Of String, Object)("Exchange_Endpoint", secrets(3)),
                        New KeyValuePair(Of String, Object)("Share_Token", secrets(4)),
                        New KeyValuePair(Of String, Object)("Exchange_ListenPort", "22022")}
                End Function
            ' A hosting stub that would leak if the builder printed what it is given.
            Dim probes As HostingProbes = HostingProbes.Absent()
            probes.ServiceInfo = Function(name) New HostingReport.ServiceFacts With {
                .Name = name, .Registered = True, .State = "Running", .StartType = "Automatic",
                .Account = "CORP\" & secrets(0), .Win32ExitCode = 0, .ServiceSpecificExitCode = 0}

            Dim result As LogPackage.LogPackageResult = Build(l, settings:=settings, probes:=probes)
            Try
                Dim report As String = ReadEntry(result.Path, "report.txt")
                For Each pair As KeyValuePair(Of String, String) In needles
                    Assert.DoesNotContain(pair.Value, report)
                Next
                For Each secret As String In secrets
                    Assert.DoesNotContain(secret, report)
                Next
                ' A user account is never named; the three Windows service accounts would be.
                Assert.Contains("a user account (name withheld)", report)
                ' Each log carries its own needle (the archive is where a log is meant to be quoted).
                Assert.Contains(needles("common/FastMediaSorterExchange/logs/exchange.log"), ReadEntry(result.Path, "exchange/exchange.log"))
                Assert.Contains(needles("common/FastMediaSorterExchange/logs/exchange.log.1"), ReadEntry(result.Path, "exchange/exchange.log.1"))
                Assert.Contains(needles("common/FastMediaSorterExchange/logs/service.log"), ReadEntry(result.Path, "exchange/service.log"))
                Assert.Contains(needles("common/FastMediaSorterCompanion/service.log"), ReadEntry(result.Path, "share/service.log"))
                ' And nothing of the seeded config or settings is in ANY entry of the archive.
                Using archive As ZipArchive = ZipFile.OpenRead(result.Path)
                    For Each entry As ZipArchiveEntry In archive.Entries
                        Using reader As New StreamReader(entry.Open())
                            Dim text As String = reader.ReadToEnd()
                            For Each secret As String In secrets
                                Assert.False(text.Contains(secret), "'" & secret & "' reached the archive entry " & entry.FullName)
                            Next
                        End Using
                    Next
                End Using
            Finally
                If Not String.IsNullOrEmpty(result.Path) Then File.Delete(result.Path)
            End Try
        End Using
    End Sub

    <Fact>
    Public Sub The_exchange_config_is_read_for_two_numbers_and_nothing_else()
        Using l As New Layout()
            Dim config As String = l.Put("cfg/exchange.json",
                "{""_comment"":""x"",""listen"":""0.0.0.0:23023"",""portRange"":""41000-41050"",""passwordHash"":""needle"",""tls"":{""certFile"":""C:\\secret\\c.pem""}}")
            Dim listen As Integer, low As Integer, high As Integer
            Assert.True(HostingProbesLive.TryReadExchangeNumbers(config, listen, low, high))
            Assert.Equal(23023, listen)
            Assert.Equal(41000, low)
            Assert.Equal(41050, high)

            ' A config that names neither means "the defaults apply"; a file that is not JSON is not read at all.
            Dim bare As String = l.Put("cfg/bare.json", "{}")
            Assert.True(HostingProbesLive.TryReadExchangeNumbers(bare, listen, low, high))
            Assert.Equal(22022, listen)
            Assert.Equal(0, low)
            Dim broken As String = l.Put("cfg/broken.json", "not json {")
            Assert.False(HostingProbesLive.TryReadExchangeNumbers(broken, listen, low, high))
            Assert.False(HostingProbesLive.TryReadExchangeNumbers(Path.Combine(l.Root, "cfg", "absent.json"), listen, low, high))
        End Using
    End Sub

    <Theory>
    <InlineData(":22022", 22022)>
    <InlineData("0.0.0.0:23023", 23023)>
    <InlineData("[::]:24024", 24024)>
    <InlineData("22022", 0)>
    <InlineData(":0", 0)>
    <InlineData(":70000", 0)>
    <InlineData("", 0)>
    Public Sub A_listen_address_gives_its_port_or_zero(text As String, expected As Integer)
        Assert.Equal(expected, HostingProbesLive.PortOfListenAddress(text))
    End Sub

End Class
#End If
