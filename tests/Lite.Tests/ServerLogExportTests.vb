#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports Xunit
#If COMPANION_TESTS Then
Imports FastMediaSorterCompanion
#End If

''' <summary>Specification 272: seeded local stores, safe provenance, complete outcomes and finalized archives.</summary>
Public Class ServerLogExportTests
    Private NotInheritable Class Machine
        Implements IDisposable
        Friend ReadOnly Root As String = Path.Combine(Path.GetTempPath(), "fms-export-" & Guid.NewGuid().ToString("N"))
        Friend ReadOnly Roots As LogPackage.LogSourceRoots
        Friend Sub New()
            Directory.CreateDirectory(Root)
            Roots = New LogPackage.LogSourceRoots With {.ExeDir = Path.Combine(Root, "app"),
                .CommonAppData = Path.Combine(Root, "machine"), .LocalAppData = Path.Combine(Root, "user"),
                .TempDir = Path.Combine(Root, "temp"), .SystemTempDir = Path.Combine(Root, "system-temp"),
                .ExchangeDataDirOverride = Path.Combine(Root, "override"), .WorkerDataDirOverride = Path.Combine(Root, "worker-override")}
        End Sub
        Friend Function Put(relative As String, Optional content As String = "diagnostic line") As String
            Dim target = Path.Combine(Root, relative.Replace("/"c, Path.DirectorySeparatorChar))
            Directory.CreateDirectory(Path.GetDirectoryName(target))
            File.WriteAllText(target, content, New UTF8Encoding(False))
            Return target
        End Function
        Friend Function Build(Optional opener As Func(Of String, Stream) = Nothing,
                              Optional enumerate As Func(Of String, IEnumerable(Of String)) = Nothing,
                              Optional rootsOverride As LogPackage.LogSourceRoots? = Nothing,
                              Optional report As Func(Of Func(Of List(Of KeyValuePair(Of String, Object))), String) = Nothing) As LogPackage.LogPackageResult
            Return LogPackage.Build(Function() New List(Of KeyValuePair(Of String, Object))(),
                New DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc), Path.Combine(Root, "output"), report,
                If(rootsOverride, Roots), opener, HostingProbes.Absent(), allServerLogs:=True, enumerateSources:=enumerate)
        End Function
        Public Sub Dispose() Implements IDisposable.Dispose
            Directory.Delete(Root, True)
        End Sub
    End Class

    Private Shared Function TextOf(zip As ZipArchive, name As String) As String
        Using reader As New StreamReader(zip.GetEntry(name).Open(), New UTF8Encoding(False, True))
            Return reader.ReadToEnd()
        End Using
    End Function

    <Fact>
    Public Sub Every_retained_day_rotation_and_store_has_distinct_safe_provenance()
        Using machine As New Machine()
            For Each day As String In {"2026-10-24", "2026-10-25"}
                For Each store In {"machine/FastMediaSorterExchange", "user/FastMediaSorterExchange", "override"}
                    machine.Put(store & "/logs/exchange-" & day & ".log", store & " day")
                    machine.Put(store & "/logs/exchange-" & day & ".log.1", store & " rotation")
                Next
            Next
            machine.Put("machine/FastMediaSorterExchange/logs/exchange.log.old")
            machine.Put("machine/FastMediaSorterExchange/logs/service.log.2")
            machine.Put("worker-override/security.log.1")
            machine.Put("machine/FastMediaSorterCompanion/service.log")
            machine.Put("user/FastMediaSorterCompanion/service.log")
            machine.Put("temp/install-exchange-service.log")
            machine.Put("system-temp/install-share-service.log.1")
            machine.Put("temp/fms-setup-20261026-100000.log")
            machine.Put("app/current.log")
            machine.Put("user/FastMediaSorterCompanion/companion.log")
            Dim result = machine.Build()
            Assert.False(String.IsNullOrEmpty(result.Path), result.ErrorText)
            Assert.False(result.IsPartial)
            Assert.True(result.ServerSourcesFound)
            Assert.Equal(22, result.Sources.Count)
            Using zip = ZipFile.OpenRead(result.Path)
                Assert.Equal(24, zip.Entries.Count)
                Assert.Contains("machine/FastMediaSorterExchange day", TextOf(zip, "exchange/machine/exchange-2026-10-24.log"))
                Assert.Contains("user/FastMediaSorterExchange day", TextOf(zip, "exchange/user/exchange-2026-10-24.log"))
                Assert.Contains("override day", TextOf(zip, "exchange/override/exchange-2026-10-24.log"))
                Assert.DoesNotContain(machine.Root, TextOf(zip, "report.txt"))
                Assert.StartsWith("schema_version=1", TextOf(zip, "environment.txt"))
                Assert.Contains("generated_utc=2026-10-26T10:00:00Z", TextOf(zip, "environment.txt"))
                For Each source In result.Sources
                    Assert.NotNull(zip.GetEntry(source.EntryName))
                    Assert.Contains(source.EntryName, TextOf(zip, "report.txt"))
                    Assert.Contains(result.Entries, Function(line) line.Contains(source.EntryName))
                Next
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Previous_logs_are_discovered_without_the_current_log_and_canonical_aliases_are_deduplicated()
        Using machine As New Machine()
            machine.Put("app/previous.log")
            machine.Put("user/FastMediaSorterCompanion/previous.log")
            machine.Put("machine/FastMediaSorterExchange/logs/exchange-2020-01-02.log.1")
            Dim roots = machine.Roots
            roots.ExchangeDataDirOverride = Path.Combine(roots.CommonAppData, "FastMediaSorterExchange", "..", "FastMediaSorterExchange")
            Dim result = machine.Build(rootsOverride:=roots)
            Assert.Equal(3, result.Sources.Count)
            Assert.Equal(3, result.Sources.Select(Function(source) source.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).Count())
            Assert.Contains(result.Sources, Function(source) source.EntryName = "application/installed/previous.log")
            Assert.Contains(result.Sources, Function(source) source.EntryName = "application/manager-user/previous.log")
        End Using
    End Sub

    <Fact>
    Public Sub Only_owned_log_patterns_are_eligible_and_user_names_never_become_entry_names()
        Using machine As New Machine()
            For Each name In {"exchange.json", "accounts.db", "key.pem", "notes.txt", "alice.log", "exchange-invalid.log", "exchange-2026-02-31.log", "exchange-2026-10-24.log.secret"}
                machine.Put("override/logs/" & name, "NEVER_EXPORT")
            Next
            machine.Put("temp/Setup Log 2026-10-26.txt", "UNRELATED_INSTALLER")
            machine.Put("override/logs/exchange-2026-10-24.log", "password=NEEDLE_SECRET")
            Dim result = machine.Build()
            Assert.Single(result.Sources)
            Using zip = ZipFile.OpenRead(result.Path)
                For Each entry In zip.Entries
                    Dim text = TextOf(zip, entry.FullName)
                    Assert.DoesNotContain("NEVER_EXPORT", text)
                    Assert.DoesNotContain("UNRELATED_INSTALLER", text)
                    Assert.DoesNotContain("NEEDLE_SECRET", text)
                    Assert.DoesNotContain(machine.Root, entry.FullName)
                Next
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Enumeration_denial_unreadable_and_disappeared_sources_have_matching_report_and_review_outcomes()
        Using machine As New Machine()
            Dim denied = machine.Put("override/logs/service.log")
            Dim gone = machine.Put("override/logs/exchange.log.1")
            machine.Put("user/FastMediaSorterExchange/logs/exchange.log", "historical")
            Dim deniedDirectory = Path.Combine(machine.Roots.CommonAppData, "FastMediaSorterExchange")
            Directory.CreateDirectory(deniedDirectory)
            Dim result = machine.Build(
                opener:=Function(file)
                            If file = denied Then Throw New UnauthorizedAccessException("password=NEEDLE_FAILURE " & machine.Root)
                            If file = gone Then Throw New FileNotFoundException("NEEDLE_MISSING")
                            Return New FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                        End Function,
                enumerate:=Function(folder)
                               If folder = deniedDirectory Then Throw New UnauthorizedAccessException("NEEDLE_DIRECTORY")
                               Return Directory.EnumerateFiles(folder)
                           End Function)
            Assert.True(result.IsPartial)
            Assert.Contains(result.Sources, Function(source) source.Failure = LogPackage.LogReadFailure.Disappeared)
            Assert.Contains(result.Sources, Function(source) source.EntryName = "exchange/machine/root/directory" AndAlso source.Failure = LogPackage.LogReadFailure.AccessDenied)
            Using zip = ZipFile.OpenRead(result.Path)
                Dim report = TextOf(zip, "report.txt")
                Assert.Contains("partial=True", report)
                Assert.Contains("reason=Disappeared", report)
                Assert.DoesNotContain("NEEDLE_", report)
                Assert.DoesNotContain(machine.Root, report)
                For Each source In result.Sources
                    Assert.Contains(source.EntryName, report)
                    Assert.Contains(result.Entries, Function(line) line.Contains(source.EntryName))
                Next
                Assert.Contains("historical", TextOf(zip, "exchange/user/exchange.log"))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Oversized_sources_are_omitted_with_the_exact_marker_and_make_the_review_partial()
        Using machine As New Machine()
            Dim source = machine.Put("override/logs/service.log")
            Using stream = New FileStream(source, FileMode.Open, FileAccess.Write)
                stream.SetLength(LogPackage.MaxSourceBytes + 1)
            End Using
            Dim result = machine.Build()
            Assert.True(result.IsPartial)
            Assert.True(result.Sources.Single().Omitted)
            Using zip = ZipFile.OpenRead(result.Path)
                Assert.Equal("[Diag] LOG OMITTED | reason=oversized_untrusted | source_bytes=67108865" & vbLf, TextOf(zip, "exchange/override/service.log"))
                Assert.Contains("reason=oversized_untrusted", TextOf(zip, "report.txt"))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub The_final_zip_is_not_exposed_before_finalization_and_failure_leaves_no_partial_archive()
        Using machine As New Machine()
            machine.Put("app/current.log")
            Dim result = machine.Build(report:=Function(settings)
                                                   Assert.Empty(Directory.GetFiles(Path.Combine(machine.Root, "output"), "*.zip"))
                                                   Assert.Single(Directory.GetFiles(Path.Combine(machine.Root, "output"), "*.partial"))
                                                   Throw New IOException("simulated output failure")
                                               End Function)
            Assert.Equal("", result.Path)
            Assert.NotNull(result.Failure)
            Assert.Empty(Directory.GetFiles(Path.Combine(machine.Root, "output")))
            Assert.True(File.Exists(Path.Combine(machine.Root, "app", "current.log")))
        End Using
    End Sub

    <Fact>
    Public Sub Active_archives_survive_cleanup_and_become_eligible_after_the_lease_ends()
        Using machine As New Machine()
            Dim source = machine.Put("fms-logs-test-20200101-000000.zip")
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddDays(-10))
            Using lease = LogPackage.ProtectPackage(source)
                LogPackage.SweepOldPackages(machine.Root, DateTime.Now)
                Assert.True(File.Exists(source))
            End Using
            LogPackage.SweepOldPackages(machine.Root, DateTime.Now)
            Assert.False(File.Exists(source))
        End Using
    End Sub

    <Fact>
    Public Sub Service_config_directory_is_resolved_without_running_the_executable()
        Assert.Equal("C:\server state", ServerLogSources.ServiceDataDirectory("""C:\missing\server.exe"" --config=""C:\server state\exchange.json"" --service"))
        Assert.Equal("C:\data", ServerLogSources.ServiceDataDirectory("server.exe --config C:\data\exchange.json"))
        Assert.Equal("", ServerLogSources.ServiceDataDirectory("server.exe --service"))
        Assert.Equal("C:\worker state", ServerLogSources.WorkerServiceDataDirectory("""C:\missing\worker.exe"" --service --datadir ""C:\worker state"""))
        Assert.Equal("", ServerLogSources.WorkerServiceDataDirectory("worker.exe --service"))
    End Sub

    <Fact>
    Public Sub A_many_day_archive_above_the_mail_limit_keeps_all_days()
        Using machine As New Machine()
            Dim random As New Random(272)
            Dim bytes(1024 * 1024 - 1) As Byte
            random.NextBytes(bytes)
            Dim encoded = Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks)
            For index As Integer = 1 To 22
                machine.Put("override/logs/exchange-2026-10-" & index.ToString("00", Globalization.CultureInfo.InvariantCulture) & ".log", encoded)
            Next
            Dim result = machine.Build()
            Assert.False(String.IsNullOrEmpty(result.Path), result.ErrorText)
            Assert.True(result.OverSizeLimit, result.SizeBytes.ToString())
            Assert.False(result.IsPartial)
            Assert.Equal(22, result.Sources.Count)
            Using zip = ZipFile.OpenRead(result.Path)
                Assert.Equal(24, zip.Entries.Count)
                Assert.All(result.Sources, Sub(source) Assert.NotNull(zip.GetEntry(source.EntryName)))
            End Using
        End Using
    End Sub

    Private NotInheritable Class LiveSource
        Inherits FileStream
        Private ReadOnly mutate As Action
        Friend BytesServed As Long
        Friend Sub New(file As String, onRead As Action)
            MyBase.New(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
            mutate = onRead
        End Sub
        Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
            mutate()
            Dim got = MyBase.Read(buffer, offset, count)
            BytesServed += got
            Return got
        End Function
    End Class

    <Fact>
    Public Sub A_live_writer_can_grow_and_rotate_while_the_original_snapshot_stays_bounded()
        Using machine As New Machine()
            Dim original = String.Concat(Enumerable.Repeat("snapshot line" & vbLf, 30000))
            Dim file = machine.Put("override/logs/exchange.log", original)
            Dim originalLength = New FileInfo(file).Length
            Dim live As LiveSource = Nothing
            Using writer As New FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite Or FileShare.Delete)
                Dim rotated As Boolean
                Dim result = machine.Build(opener:=Function(source)
                                                       live = New LiveSource(source,
                                                           Sub()
                                                               Dim extra = Encoding.UTF8.GetBytes("late write" & vbLf)
                                                               writer.Write(extra, 0, extra.Length)
                                                               writer.Flush()
                                                               If Not rotated Then
                                                                   System.IO.File.Move(file, file & ".1")
                                                                   System.IO.File.WriteAllText(file, "new current")
                                                                   rotated = True
                                                               End If
                                                           End Sub)
                                                       Return live
                                                   End Function)
                Assert.False(String.IsNullOrEmpty(result.Path), result.ErrorText)
                Assert.True(rotated)
                Assert.Equal(originalLength, live.BytesServed)
                Assert.True(writer.CanWrite)
                Using zip = ZipFile.OpenRead(result.Path)
                    Assert.Equal(original, TextOf(zip, "exchange/override/exchange.log"))
                    Assert.Contains("bounded per file", TextOf(zip, "report.txt"))
                End Using
                Assert.Equal("new current", System.IO.File.ReadAllText(file))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Export_redacts_structured_values_PEM_blocks_and_metadata_by_content()
        Using machine As New Machine()
            machine.Put("override/logs/service.log", String.Join(vbLf,
                "config={""innocent"":{""pin"":""NEEDLE_PIN""}}",
                "authorization: Bearer NEEDLE_AUTH", "serverAddress=NEEDLE_SERVER", "oldFingerprint=NEEDLE_OLD", "newFingerprint=NEEDLE_NEW",
                "https://NEEDLE_USER:NEEDLE_PASS@example.net", "-----BEGIN PRIVATE KEY-----", "NEEDLE_BODY", "-----END PRIVATE KEY-----"))
            Dim result = machine.Build(report:=Function(settings) "token=NEEDLE_REPORT" & vbLf & "serverAddress=NEEDLE_REPORT_SERVER")
            Using zip = ZipFile.OpenRead(result.Path)
                For Each entry In zip.Entries
                    Assert.DoesNotContain("NEEDLE_", TextOf(zip, entry.FullName))
                Next
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Network_overrides_are_reported_without_accessing_a_remote_directory()
        Using machine As New Machine()
            Dim roots = machine.Roots
            roots.ExchangeDataDirOverride = "\\SHOULD_NOT_CONTACT\private-store"
            Dim result = machine.Build(rootsOverride:=roots, enumerate:=Function(folder)
                                                                            Assert.False(folder.StartsWith("\\", StringComparison.Ordinal))
                                                                            Return Directory.EnumerateFiles(folder)
                                                                        End Function)
            Assert.True(result.IsPartial)
            Assert.Equal(2, result.Sources.Where(Function(source) source.Failure = LogPackage.LogReadFailure.NonLocal).Count())
            Assert.False(result.ServerSourcesFound)
            Using zip = ZipFile.OpenRead(result.Path)
                Assert.DoesNotContain("SHOULD_NOT_CONTACT", TextOf(zip, "report.txt"))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_source_that_shrinks_during_read_is_explicitly_partial()
        Using machine As New Machine()
            Dim file = machine.Put("override/logs/service.log", New String("x"c, 200000))
            Dim changed As Boolean
            Dim result = machine.Build(opener:=Function(source) New LiveSource(source,
                Sub()
                    If changed Then Return
                    Using writer As New FileStream(file, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite Or FileShare.Delete)
                        writer.Write(Encoding.UTF8.GetBytes("shortened" & vbLf))
                    End Using
                    changed = True
                End Sub))
            Assert.True(result.IsPartial)
            Assert.True(result.Sources.Single().IncompleteSnapshot)
            Using zip = ZipFile.OpenRead(result.Path)
                Assert.Contains("reason=source_shortened", TextOf(zip, "report.txt"))
                Assert.Equal("shortened" & vbLf, TextOf(zip, "exchange/override/service.log"))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Directory_junctions_are_accounted_for_without_exporting_their_targets()
        Using machine As New Machine()
            machine.Put("outside-logs/service.log", "OUTSIDE_SUPPORTED_ROOT")
            Directory.CreateDirectory(machine.Roots.ExchangeDataDirOverride)
            Dim junction = Path.Combine(machine.Roots.ExchangeDataDirOverride, "logs")
            Dim target = Path.Combine(machine.Root, "outside-logs")
            Using command = Process.Start(New ProcessStartInfo("cmd.exe", "/d /c mklink /J """ & junction & """ """ & target & """") With {
                .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True})
                Assert.True(command.WaitForExit(5000))
                Assert.Equal(0, command.ExitCode)
            End Using
            Try
                Dim result = machine.Build()
                Assert.True(result.IsPartial)
                Assert.Contains(result.Sources, Function(source) source.Failure = LogPackage.LogReadFailure.ReparsePoint)
                Using zip = ZipFile.OpenRead(result.Path)
                    Assert.Null(zip.GetEntry("exchange/override/service.log"))
                    Assert.DoesNotContain("OUTSIDE_SUPPORTED_ROOT", TextOf(zip, "report.txt"))
                End Using
            Finally
                Directory.Delete(junction, False)
            End Try
        End Using
    End Sub

    Private Shared Iterator Function InterruptedEnumeration(file As String) As IEnumerable(Of String)
        Yield file
        Throw New IOException("NEEDLE_ENUMERATION_FAILURE")
    End Function

    <Fact>
    Public Sub Files_discovered_before_a_directory_enumeration_failure_keep_their_own_outcomes()
        Using machine As New Machine()
            Dim file = machine.Put("override/logs/service.log")
            Dim result = machine.Build(enumerate:=Function(folder)
                                                     If folder = Path.GetDirectoryName(file) Then Return InterruptedEnumeration(file)
                                                     Return Directory.EnumerateFiles(folder)
                                                 End Function)
            Assert.True(result.IsPartial)
            Assert.Equal(2, result.Sources.Count)
            Assert.Contains(result.Sources, Function(source) source.EntryName = "exchange/override/service.log" AndAlso source.State = LogPackage.LogSourceState.Included)
            Assert.Contains(result.Sources, Function(source) source.Failure = LogPackage.LogReadFailure.EnumerationFailed)
            Using zip = ZipFile.OpenRead(result.Path)
                Assert.DoesNotContain("NEEDLE_ENUMERATION_FAILURE", TextOf(zip, "report.txt"))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Effective_service_and_environment_stores_do_not_conceal_historical_Windows_stores()
        Using machine As New Machine()
            machine.Put("service/logs/service.log", "registered service")
            machine.Put("environment/FastMediaSorterExchange/logs/service.log", "environment machine")
            machine.Put("machine/FastMediaSorterExchange/logs/service.log", "known machine")
            machine.Put("user/FastMediaSorterExchange/logs/service.log", "known user")
            machine.Put("worker-service/security.log", "registered worker")
            Dim roots = machine.Roots
            roots.ExchangeServiceDataDir = Path.Combine(machine.Root, "service")
            roots.ServerCommonAppData = Path.Combine(machine.Root, "environment")
            roots.WorkerServiceDataDir = Path.Combine(machine.Root, "worker-service")
            Dim result = machine.Build(rootsOverride:=roots)
            Assert.False(result.IsPartial)
            Assert.Equal(5, result.Sources.Count)
            Using zip = ZipFile.OpenRead(result.Path)
                Assert.Contains("registered service", TextOf(zip, "exchange/service/service.log"))
                Assert.Contains("environment machine", TextOf(zip, "exchange/environment-machine/service.log"))
                Assert.Contains("known machine", TextOf(zip, "exchange/machine/service.log"))
                Assert.Contains("known user", TextOf(zip, "exchange/user/service.log"))
                Assert.Contains("registered worker", TextOf(zip, "share/service/security.log"))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Server_report_settings_never_disclose_unknown_setting_names_or_their_values()
        Dim settings As New List(Of KeyValuePair(Of String, Object)) From {
            New KeyValuePair(Of String, Object)("NEEDLE_ACCOUNT_Password", "NEEDLE_PASSWORD"),
            New KeyValuePair(Of String, Object)("Resources", "NEEDLE_RESOURCE"),
            New KeyValuePair(Of String, Object)("Share_RootParams", "NEEDLE_PIN"),
            New KeyValuePair(Of String, Object)("Share_ListenPort", 22022)}
        Dim report = LogPackage.BuildReport(Function() settings, hostingBlock:="not collected", safeMetadata:=True)
        Assert.DoesNotContain("NEEDLE_", report)
        Assert.Contains("Share_ListenPort=22022", report)
        Assert.DoesNotContain("Resources=", report)
    End Sub
End Class
#End If
