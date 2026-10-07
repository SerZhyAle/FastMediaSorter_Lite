#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' The archive behind Settings -> About -> "send the logs to the author"
''' (SPECIFICATION_SEND_LOGS_TO_AUTHOR.md).
'''
''' Two things here are worth pinning down, and both are the reasons the feature could
''' silently be useless: the log is held OPEN FOR WRITING by AppFileLogger while it is
''' being packed, and a log that has grown for months must not produce a mail nobody
''' can send.
''' </summary>
Public Class LogPackageTests

    <Fact>
    Public Sub A_log_that_is_open_for_writing_still_lands_in_the_archive()
        Dim logPath As String = Path.Combine(Path.GetTempPath(), "fms-test-open-" & Guid.NewGuid().ToString("N") & ".log")
        Dim zipPath As String = logPath & ".zip"

        Try
            File.WriteAllText(logPath, "first line" & vbLf & "second line" & vbLf)

            ' Exactly how AppFileLogger holds it: appending, sharing read+write. The
            ' convenience API (CreateEntryFromFile) opens with FileShare.Read and would
            ' throw IOException right here.
            Using writer As New FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                Using zip As New FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None)
                    Using archive As New ZipArchive(zip, ZipArchiveMode.Create)
                        Dim outcome As LogPackage.LogEntryOutcome = LogPackage.CopyLogEntry(archive, "current.log", logPath)
                        Assert.Equal(LogPackage.LogSourceState.Included, outcome.State)
                        Assert.NotNull(outcome.Line)
                        Assert.Contains("current.log", outcome.Line)
                    End Using
                End Using
            End Using

            Assert.Equal("first line" & vbLf & "second line" & vbLf, ReadEntry(zipPath, "current.log"))
        Finally
            Delete(logPath)
            Delete(zipPath)
        End Try
    End Sub

    <Fact>
    Public Sub A_long_log_contributes_its_tail_with_a_truncation_marker()
        Dim logPath As String = Path.Combine(Path.GetTempPath(), "fms-test-long-" & Guid.NewGuid().ToString("N") & ".log")
        Dim zipPath As String = logPath & ".zip"

        Try
            Using writer As New StreamWriter(logPath, False, New UTF8Encoding(False))
                ' Comfortably over the 2 MB per-entry cap, with identifiable lines.
                For i As Integer = 0 To 60000
                    writer.WriteLine("line " & i.ToString() & " " & New String("x"c, 50))
                Next
            End Using

            Using zip As New FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None)
                Using archive As New ZipArchive(zip, ZipArchiveMode.Create)
                    LogPackage.CopyLogEntry(archive, "current.log", logPath)
                End Using
            End Using

            Dim packed As String = ReadEntry(zipPath, "current.log")
            Dim lines As String() = packed.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)

            Assert.StartsWith("[.. truncated:", lines(0))
            ' The tail seek lands mid-record; the partial line is dropped, so the first
            ' real line is a whole one.
            Assert.StartsWith("line ", lines(1))
            Assert.EndsWith("xxxxx", lines(1))
            ' The last line of the source survived - it is the tail we kept, not the head.
            Assert.StartsWith("line 60000 ", lines(lines.Length - 1))
            Assert.True(packed.Length < 2.2 * 1024 * 1024, "packed entry should be about the 2 MB cap, was " & packed.Length.ToString())
        Finally
            Delete(logPath)
            Delete(zipPath)
        End Try
    End Sub

    ''' <summary>The report dumps the registry profile, and exactly one value in there is
    ''' a secret. It never leaves the machine - not even in its DPAPI-encrypted form.</summary>
    <Theory>
    <InlineData("TranslateApiKey")>
    <InlineData("translateapikey")>
    <InlineData("SomePassword")>
    <InlineData("Share_Token")>
    Public Sub Secret_settings_are_recognised(name As String)
        Assert.True(LogPackage.IsSecretSetting(name))
    End Sub

    <Theory>
    <InlineData("MoveOn0")>
    <InlineData("OcrEnabled")>
    <InlineData("UiLanguage")>
    Public Sub Ordinary_settings_are_not_treated_as_secret(name As String)
        Assert.False(LogPackage.IsSecretSetting(name))
    End Sub

    <Fact>
    Public Sub Build_produces_a_readable_archive_with_a_report()
        Dim result As LogPackage.LogPackageResult = LogPackage.Build()

        Try
            Assert.False(String.IsNullOrEmpty(result.Path), "Build() returned no path: " & If(result.ErrorText, ""))
            Assert.True(File.Exists(result.Path))
            Assert.Contains("report.txt", result.Entries)

            Dim report As String = ReadEntry(result.Path, "report.txt")
            Assert.Contains("[application]", report)
            Assert.Contains("[system]", report)
            Assert.Contains("[settings]", report)
            ' Whatever is in this machine's profile, the key itself is never written out.
            Assert.DoesNotContain("TranslateApiKey=" & vbTab, report)
            If report.Contains("TranslateApiKey") Then Assert.Contains("TranslateApiKey=<hidden>", report)
        Finally
            If Not String.IsNullOrEmpty(result.Path) Then Delete(result.Path)
        End Try
    End Sub

    ' ---- spec 039 phase 1: nothing secret or personal leaves in the bundle ----

    Private Const SeededPin As String = "739184"

    Private Shared Function SeededSettings() As List(Of KeyValuePair(Of String, Object))
        Dim profile As String = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        Return New List(Of KeyValuePair(Of String, Object)) From {
            New KeyValuePair(Of String, Object)("Share_RootParams",
                "{""D:\\Photos\\Private"":{""label"":""Family"",""accessPin"":""" & SeededPin & """}}"),
            New KeyValuePair(Of String, Object)("RecentFolders", profile & "\Pictures|D:\Holiday|E:\Work"),
            New KeyValuePair(Of String, Object)("RecentMediaFiles", ""),
            New KeyValuePair(Of String, Object)("LastPlayedFile", profile & "\Videos\clip.mp4"),
            New KeyValuePair(Of String, Object)("ImageFolder", profile & "\Pictures"),
            New KeyValuePair(Of String, Object)("MoveOn3", "D:\Sorted\Cats"),
            New KeyValuePair(Of String, Object)("MoveOn4", ""),
            New KeyValuePair(Of String, Object)("TranslateEndpoint", "http://user:pass@example.org"),
            New KeyValuePair(Of String, Object)("TranslateApiKey", "encrypted-blob"),
            New KeyValuePair(Of String, Object)("UiLanguage", "de"),
            New KeyValuePair(Of String, Object)("VideoVolume", 42)
        }
    End Function

    <Fact>
    Public Sub A_share_pin_never_reaches_any_entry_of_the_archive()
        Dim result As LogPackage.LogPackageResult = LogPackage.Build(AddressOf SeededSettings)

        Try
            Assert.False(String.IsNullOrEmpty(result.Path), "Build() returned no path: " & If(result.ErrorText, ""))
            Using archive As ZipArchive = ZipFile.OpenRead(result.Path)
                For Each entry As ZipArchiveEntry In archive.Entries
                    Using reader As New StreamReader(entry.Open())
                        Dim text As String = reader.ReadToEnd()
                        Assert.DoesNotContain(SeededPin, text)
                        Assert.DoesNotContain("Share_RootParams", text)
                    End Using
                Next
            End Using
        Finally
            If Not String.IsNullOrEmpty(result.Path) Then Delete(result.Path)
        End Try
    End Sub

    ''' <summary>007 acceptance 13: a saved remote resource - its address, its user name, the handle of its
    ''' stored credential and the fingerprint it is pinned to - is withheld like every other value that is
    ''' not on the allow-list. The password itself is never in the settings at all (DPAPI blob under a
    ''' handle), so what is asserted here is that nothing AROUND it leaves the PC either.</summary>
    <Fact>
    Public Sub A_saved_resource_never_reaches_any_entry_of_the_archive()
        Dim secretHost As String = "nas-private.example.lan"
        Dim secretUser As String = "alice-the-owner"
        Dim secretHandle As String = "cred:0123456789abcdef0123456789abcdef"
        Dim resources As String = "[{""Id"":""res:1"",""DisplayName"":""Family NAS"",""Kind"":""Sftp"",""Host"":""" & secretHost & """,""Port"":2022," &
                                  """UserName"":""" & secretUser & """,""RootPath"":""/Photos"",""SecretHandle"":""" & secretHandle & """," &
                                  """PinnedFingerprint"":""SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk""}]"
        Dim seeded As Func(Of List(Of KeyValuePair(Of String, Object))) =
            Function()
                Dim list As List(Of KeyValuePair(Of String, Object)) = SeededSettings()
                list.Add(New KeyValuePair(Of String, Object)("Resources", resources))
                list.Add(New KeyValuePair(Of String, Object)("Resources.bak", resources))
                Return list
            End Function
        Dim result As LogPackage.LogPackageResult = LogPackage.Build(seeded)

        Try
            Assert.False(String.IsNullOrEmpty(result.Path), "Build() returned no path: " & If(result.ErrorText, ""))
            Using archive As ZipArchive = ZipFile.OpenRead(result.Path)
                For Each entry As ZipArchiveEntry In archive.Entries
                    Using reader As New StreamReader(entry.Open())
                        Dim text As String = reader.ReadToEnd()
                        Assert.DoesNotContain(secretHost, text)
                        Assert.DoesNotContain(secretUser, text)
                        Assert.DoesNotContain(secretHandle, text)
                        Assert.DoesNotContain("SHA256:8f6TQ", text)
                        Assert.DoesNotContain("Family NAS", text)
                    End Using
                Next
            End Using
        Finally
            If Not String.IsNullOrEmpty(result.Path) Then Delete(result.Path)
        End Try
    End Sub
    <Fact>
    Public Sub Only_allow_listed_values_are_written_and_paths_become_counts()
        Dim lines As List(Of String) = LogPackage.DescribeSettings(SeededSettings())
        Dim text As String = String.Join(vbLf, lines)

        Assert.Contains("UiLanguage=de", lines)
        Assert.Contains("VideoVolume=42", lines)
        Assert.Contains("TranslateApiKey=<hidden>", lines)
        Assert.Contains("RecentFolders=<3 entries, not sent>", lines)
        Assert.Contains("RecentMediaFiles=<0 entries, not sent>", lines)
        Assert.Contains("MoveOn3=<set, path not sent>", lines)
        Assert.Contains("MoveOn4=<empty>", lines)
        ' LastPlayedFile, ImageFolder and TranslateEndpoint are not on the list.
        Assert.Contains("(3 other values not sent)", lines)

        Assert.DoesNotContain("Holiday", text)
        Assert.DoesNotContain("Cats", text)
        Assert.DoesNotContain("clip.mp4", text)
        Assert.DoesNotContain("user:pass", text)
        Assert.DoesNotContain("encrypted-blob", text)
    End Sub

    <Fact>
    Public Sub An_empty_profile_says_so()
        Assert.Equal({"no saved profile"}, LogPackage.DescribeSettings(New List(Of KeyValuePair(Of String, Object))()).ToArray())
    End Sub

    <Theory>
    <InlineData("Running exe: C:\Users\Alice\AppData\Local\app.exe", "Running exe: <USER>\AppData\Local\app.exe")>
    <InlineData("opened c:\users\alice\Pictures\a.jpg", "opened <USER>\Pictures\a.jpg")>
    <InlineData("uri C:/Users/Alice/x.png twice C:\Users\Alice", "uri <USER>/x.png twice <USER>")>
    <InlineData("D:\Photos\a.jpg", "D:\Photos\a.jpg")>
    Public Sub The_user_profile_prefix_is_replaced(input As String, expected As String)
        Assert.Equal(expected, LogPackage.RedactUserProfile(input, "C:\Users\Alice\"))
    End Sub

    <Fact>
    Public Sub A_copied_log_carries_no_user_profile_path()
        Dim profile As String = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        Dim logPath As String = Path.Combine(Path.GetTempPath(), "fms-test-profile-" & Guid.NewGuid().ToString("N") & ".log")
        Dim zipPath As String = logPath & ".zip"

        Try
            File.WriteAllText(logPath, "opened " & profile & "\Pictures\a.jpg" & vbLf)
            Using zip As New FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None)
                Using archive As New ZipArchive(zip, ZipArchiveMode.Create)
                    LogPackage.CopyLogEntry(archive, "current.log", logPath)
                End Using
            End Using

            Assert.Equal("opened <USER>\Pictures\a.jpg" & vbLf, ReadEntry(zipPath, "current.log"))
        Finally
            Delete(logPath)
            Delete(zipPath)
        End Try
    End Sub

    <Fact>
    Public Sub Concurrent_builds_with_fixed_time_produce_distinct_packages_and_cancelling_one_leaves_other()
        Dim testDir As String = Path.Combine(Path.GetTempPath(), "fms-test-pkg-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(testDir)
        Dim fixedTime As New DateTime(2026, 10, 1, 14, 0, 0)

        Try
            Dim task1 As Task(Of LogPackage.LogPackageResult) = Task.Run(
                Function() LogPackage.Build(AddressOf SeededSettings, fixedTime, testDir, Nothing))
            Dim task2 As Task(Of LogPackage.LogPackageResult) = Task.Run(
                Function() LogPackage.Build(AddressOf SeededSettings, fixedTime, testDir, Nothing))

            Task.WaitAll(task1, task2)

            Dim res1 As LogPackage.LogPackageResult = task1.Result
            Dim res2 As LogPackage.LogPackageResult = task2.Result

            Assert.False(String.IsNullOrEmpty(res1.Path), "First build should succeed")
            Assert.False(String.IsNullOrEmpty(res2.Path), "Second build should succeed")
            Assert.NotEqual(res1.Path, res2.Path)
            Assert.True(File.Exists(res1.Path))
            Assert.True(File.Exists(res2.Path))

            ' Verify both are valid ZIP archives
            Assert.Contains("[application]", ReadEntry(res1.Path, "report.txt"))
            Assert.Contains("[application]", ReadEntry(res2.Path, "report.txt"))

            ' Simulate cancelling request 1: delete only its owned package
            File.Delete(res1.Path)
            Assert.False(File.Exists(res1.Path))
            Assert.True(File.Exists(res2.Path))
            Assert.Contains("[application]", ReadEntry(res2.Path, "report.txt"))
        Finally
            Try
                If Directory.Exists(testDir) Then Directory.Delete(testDir, True)
            Catch
            End Try
        End Try
    End Sub

    <Fact>
    Public Sub Preseeded_collision_sentinel_survives_intact()
        Dim testDir As String = Path.Combine(Path.GetTempPath(), "fms-test-sentinel-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(testDir)
        Dim fixedTime As New DateTime(2026, 10, 1, 14, 0, 0)
        Dim sentinelPath As String = Path.Combine(testDir, "fms-logs-sentinel-collision.zip")
        Dim sentinelBytes As Byte() = Encoding.UTF8.GetBytes("PREEXISTING_COLLISION_SENTINEL_BYTES_MUST_SURVIVE")
        File.WriteAllBytes(sentinelPath, sentinelBytes)

        Try
            Dim result As LogPackage.LogPackageResult = LogPackage.Build(AddressOf SeededSettings, fixedTime, testDir, Nothing)

            Assert.False(String.IsNullOrEmpty(result.Path))
            Assert.True(File.Exists(result.Path))
            Assert.NotEqual(sentinelPath, result.Path)

            Assert.True(File.Exists(sentinelPath))
            Assert.Equal(sentinelBytes, File.ReadAllBytes(sentinelPath))
        Finally
            Try
                If Directory.Exists(testDir) Then Directory.Delete(testDir, True)
            Catch
            End Try
        End Try
    End Sub

    <Fact>
    Public Sub Failure_after_reservation_deletes_only_owned_partial_package()
        Dim testDir As String = Path.Combine(Path.GetTempPath(), "fms-test-fail-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(testDir)
        Dim fixedTime As New DateTime(2026, 10, 1, 14, 0, 0)
        Dim sentinelPath As String = Path.Combine(testDir, "fms-logs-sentinel.zip")
        Dim sentinelBytes As Byte() = Encoding.UTF8.GetBytes("SENTINEL_PRESERVED")
        File.WriteAllBytes(sentinelPath, sentinelBytes)

        Try
            Dim result As LogPackage.LogPackageResult = LogPackage.Build(
                AddressOf SeededSettings,
                fixedTime,
                testDir,
                Function(src)
                    Throw New InvalidOperationException("Injected report generation failure")
                End Function)

            Assert.Equal("", result.Path)
            Assert.NotNull(result.Failure)
            Assert.IsType(Of InvalidOperationException)(result.Failure)

            ' Only the sentinel should remain in testDir - no partial or orphan ZIP
            Dim remainingZipFiles As String() = Directory.GetFiles(testDir, "fms-logs-*.zip")
            Assert.Single(remainingZipFiles)
            Assert.Equal(sentinelPath, remainingZipFiles(0))
            Assert.Equal(sentinelBytes, File.ReadAllBytes(sentinelPath))
        Finally
            Try
                If Directory.Exists(testDir) Then Directory.Delete(testDir, True)
            Catch
            End Try
        End Try
    End Sub

    <Fact>
    Public Sub Sweep_deletes_only_stale_fms_logs_packages()
        Dim testDir As String = Path.Combine(Path.GetTempPath(), "fms-test-sweep-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(testDir)
        Dim now As New DateTime(2026, 10, 1, 14, 0, 0)

        Dim stalePkg As String = Path.Combine(testDir, "fms-logs-stale.zip")
        Dim freshPkg As String = Path.Combine(testDir, "fms-logs-fresh.zip")
        Dim otherOldZip As String = Path.Combine(testDir, "other-stale.zip")

        File.WriteAllText(stalePkg, "stale")
        File.SetLastWriteTime(stalePkg, now.AddDays(-8))

        File.WriteAllText(freshPkg, "fresh")
        File.SetLastWriteTime(freshPkg, now.AddDays(-2))

        File.WriteAllText(otherOldZip, "other")
        File.SetLastWriteTime(otherOldZip, now.AddDays(-10))

        Try
            LogPackage.SweepOldPackages(testDir, now)

            Assert.False(File.Exists(stalePkg), "Stale fms package should be deleted")
            Assert.True(File.Exists(freshPkg), "Fresh fms package should survive")
            Assert.True(File.Exists(otherOldZip), "Non-fms zip should survive")
        Finally
            Try
                If Directory.Exists(testDir) Then Directory.Delete(testDir, True)
            Catch
            End Try
        End Try
    End Sub

    ' ---- ticket 141 T-8: the exchange server in the package ----

    <Fact>
    Public Sub The_report_lists_the_exchange_server_component_beside_the_share_worker()
        Dim names As List(Of String) = LogPackage.ComponentPaths().Select(Function(p) p.Key).ToList()
        Assert.Contains("share worker", names)
        Assert.Contains("exchange server", names)
        Dim exchange As KeyValuePair(Of String, String) = LogPackage.ComponentPaths().Single(Function(p) p.Key = "exchange server")
        Assert.EndsWith("exchange" & Path.DirectorySeparatorChar & "fms-exchange-server.exe", exchange.Value)
        Assert.StartsWith(AppContext.BaseDirectory, exchange.Value)
    End Sub

    ' The exchange server's logs live in <data dir>\logs\ since ticket 182 L-1 - the one subdirectory the
    ' enrolled user can read. Where each is looked for, the names it gets in the archive and what happens
    ' when it cannot be read are asserted by LogPackageSourcesTests (compiled into both test projects).

    <Fact>
    Public Sub The_real_roots_of_this_process_name_the_current_machine_and_the_override_variable()
        Dim saved As String = Environment.GetEnvironmentVariable("FMS_EXCHANGE_DATA_DIR")
        Try
            Environment.SetEnvironmentVariable("FMS_EXCHANGE_DATA_DIR", "D:\scratch\xd")
            Dim roots As LogPackage.LogSourceRoots = LogPackage.CurrentLogRoots()
            Assert.Equal("D:\scratch\xd", roots.ExchangeDataDirOverride)
            Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), roots.CommonAppData)
            Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), roots.LocalAppData)
            Assert.Equal(AppContext.BaseDirectory, roots.ExeDir)
            Assert.False(String.IsNullOrEmpty(roots.TempDir))
        Finally
            Environment.SetEnvironmentVariable("FMS_EXCHANGE_DATA_DIR", saved)
        End Try
    End Sub

    ''' <summary>The allow-list never carries a value that could be a password or a hash, however the
    ''' name is spelled - a future "Exchange_PasswordHint" must be refused at review, not by luck.</summary>
    <Fact>
    Public Sub No_allow_listed_setting_name_can_carry_a_password_or_a_hash()
        For Each name As String In LogPackage.SafeSettingNames
            Assert.False(name.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0, "Password in the name: " & name)
            Assert.False(name.IndexOf("Hash", StringComparison.OrdinalIgnoreCase) >= 0, "Hash in the name: " & name)
            Assert.False(name.IndexOf("Fingerprint", StringComparison.OrdinalIgnoreCase) >= 0, "Fingerprint in the name: " & name)
        Next
        ' And the exchange settings that ARE on the list are exactly the three that cannot hold a secret.
        Dim exchange As List(Of String) = LogPackage.SafeSettingNames.Where(Function(n) n.StartsWith("Exchange_", StringComparison.OrdinalIgnoreCase)).
            OrderBy(Function(n) n, StringComparer.Ordinal).ToList()
        Assert.Equal({"Exchange_EverStarted", "Exchange_KeepRunningOnExit", "Exchange_ListenPort"}, exchange.ToArray())
    End Sub

    <Fact>
    Public Sub An_exchange_setting_that_is_not_on_the_list_is_counted_not_sent()
        Dim values As New List(Of KeyValuePair(Of String, Object)) From {
            New KeyValuePair(Of String, Object)("Exchange_KeepRunningOnExit", "1"),
            New KeyValuePair(Of String, Object)("Exchange_ListenPort", "22022"),
            New KeyValuePair(Of String, Object)("Exchange_PasswordHint", "my cat"),
            New KeyValuePair(Of String, Object)("Exchange_Fingerprint", "AB:CD")
        }
        Dim lines As List(Of String) = LogPackage.DescribeSettings(values)
        Assert.Contains("Exchange_KeepRunningOnExit=1", lines)
        Assert.Contains("Exchange_ListenPort=22022", lines)
        ' A name with Password in it is shown as <hidden>; anything else unlisted is only counted.
        Assert.Contains("Exchange_PasswordHint=<hidden>", lines)
        Assert.DoesNotContain("my cat", String.Join(vbLf, lines))
        Assert.DoesNotContain("AB:CD", String.Join(vbLf, lines))
        Assert.Contains("(1 other values not sent)", lines)
    End Sub

    Private Shared Function ReadEntry(zipPath As String, entryName As String) As String
        Using archive As ZipArchive = ZipFile.OpenRead(zipPath)
            Dim entry As ZipArchiveEntry = archive.GetEntry(entryName)
            Assert.NotNull(entry)
            Using reader As New StreamReader(entry.Open())
                Return reader.ReadToEnd()
            End Using
        End Using
    End Function

    Private Shared Sub Delete(path As String)
        Try
            If File.Exists(path) Then File.Delete(path)
        Catch
        End Try
    End Sub

End Class

''' <summary>
''' The viewer's side of ticket 182 L-3 and L-6: the consent-list line of a log that cannot be included and the
''' no-mail-client message, in every language the mainline ships; and the order of the manual route in the
''' viewer's own flow (the mailto only after the handler check, the message carrying the full path).
''' </summary>
<Collection("LocalizationState")>
Public Class LogPackageStringsTests

    <Fact>
    Public Sub An_unreadable_log_line_names_the_entry_and_a_reason_in_every_language()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                For Each failure As LogPackage.LogReadFailure In {LogPackage.LogReadFailure.AccessDenied, LogPackage.LogReadFailure.InUse, LogPackage.LogReadFailure.Other}
                    Dim line As String = LogPackage.UnreadableLine("exchange/exchange.log", failure)
                    Assert.Contains("exchange/exchange.log", line)
                    Assert.DoesNotContain("{0}", line)
                    Assert.DoesNotContain("{1}", line)
                    If code <> "ru" AndAlso code <> "uk" Then Assert.DoesNotContain(LogPackage.ReasonKey(failure), line)
                Next
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub The_no_mail_client_message_carries_the_full_path_in_every_language()
        Dim saved As String = Localization.CurrentCode
        Dim zipPath As String = "C:\Users\someone\AppData\Local\Temp\fms-logs-26.10.7.1200-20261007-120000-abcd1234.zip"
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                Dim text As String = Localization.TF("Почтовая программа не найдена. Архив скопирован в буфер обмена и показан в папке. Полный путь к архиву: {0}", zipPath)
                Assert.Contains(zipPath, text)
                Assert.DoesNotContain("{0}", text)
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub The_viewers_manual_route_checks_for_a_handler_before_it_starts_a_mailto_and_prints_the_path()
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing AndAlso Not File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln"))
            dir = dir.Parent
        End While
        Assert.NotNull(dir)
        Dim src As String = File.ReadAllText(Path.Combine(dir.FullName, "src", "Table_Form.SendLogs.vb"))
        Dim decision As Integer = src.IndexOf("MailSender.ManualRouteFor(MailSender.HasMailHandler())", StringComparison.Ordinal)
        Dim mailto As Integer = src.IndexOf("""mailto:""", decision, StringComparison.Ordinal)
        Dim clipboard As Integer = src.IndexOf("Clipboard.SetFileDropList", StringComparison.Ordinal)
        Dim reveal As Integer = src.IndexOf("RevealPackage(zipPath)", clipboard, StringComparison.Ordinal)
        Assert.True(clipboard > 0 AndAlso reveal > clipboard, "the clipboard drop and the folder reveal stay")
        Assert.True(decision > reveal AndAlso decision < mailto, "the handler check must sit between the reveal and the blind mailto")
        Assert.Contains("Полный путь к архиву: {0}", src.Substring(decision, mailto - decision))
        Assert.Contains("zipPath)", src.Substring(decision, mailto - decision))
    End Sub

End Class
#End If
