#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports Xunit

''' <summary>
''' Specification 169 §8: Tests for ProfileResetRunner using InMemoryProfileStore.
''' </summary>
Public Class ProfileResetRunnerTests

    Private Function CreateSampleStore() As InMemoryProfileStore
        Dim initial As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"chkTopMost", "1"},
            {"MoveOn1", "C:\Photos"},
            {"OcrEnabled", "1"},
            {"NameCollisionPolicy", "skip"},
            {"AppWidth", "1024"},
            {"UiLanguage", "ru"},
            {"TranslateApiKey", "secret-key-xyz"},
            {"RecentFolders", "C:\OldPhotos"},
            {"Resources", "server1"},
            {"Share_ListenPort", "8080"},
            {"Share_CustomKey", "custom-value"}
        }
        Return New InMemoryProfileStore(initial)
    End Function

    Private Shared Sub NoopLog(msg As String)
    End Sub

    Private Shared Sub NoopLogEx(msg As String, ex As Exception)
    End Sub

    Private Shared Function ThrowingBackupWriter(values As IReadOnlyDictionary(Of String, String)) As String
        Throw New IOException("Disk is full")
    End Function

    Private Function DummyBackupWriter(savedBackups As List(Of IReadOnlyDictionary(Of String, String))) As Func(Of IReadOnlyDictionary(Of String, String), String)
        Return Function(values)
                   savedBackups.Add(New Dictionary(Of String, String)(values, StringComparer.OrdinalIgnoreCase))
                   Return "C:\backups\profile-before-reset-20261008-120000-dummy.reg"
               End Function
    End Function

    <Fact>
    Public Sub After_ApplyPending_exactly_owned_names_are_gone_and_others_are_identical()
        Dim store = CreateSampleStore()
        Dim nowUtc = New DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc)
        store.WriteMarker("1|" & nowUtc.ToString("o", CultureInfo.InvariantCulture))

        Dim backups As New List(Of IReadOnlyDictionary(Of String, String))()
        Dim outcome = ProfileResetRunner.ApplyPending(
            store,
            DummyBackupWriter(backups),
            Function() nowUtc,
            AddressOf NoopLog,
            AddressOf NoopLogEx)

        Assert.Equal(ProfileResetStatus.Applied, outcome.Status)
        Assert.False(String.IsNullOrEmpty(outcome.BackupPath))
        Assert.Single(backups)
        Assert.True(backups(0).ContainsKey("chkTopMost"))
        Assert.True(backups(0).ContainsKey("MoveOn1"))
        Assert.True(backups(0).ContainsKey("OcrEnabled"))
        Assert.True(backups(0).ContainsKey("NameCollisionPolicy"))
        Assert.True(backups(0).ContainsKey("AppWidth"))
        Assert.False(backups(0).ContainsKey("UiLanguage"))
        Assert.False(backups(0).ContainsKey("TranslateApiKey"))
        Assert.False(backups(0).ContainsKey("Share_ListenPort"))

        Dim snap = store.Snapshot()
        ' Owned names are gone
        Assert.False(snap.ContainsKey("chkTopMost"))
        Assert.False(snap.ContainsKey("MoveOn1"))
        Assert.False(snap.ContainsKey("OcrEnabled"))
        Assert.False(snap.ContainsKey("NameCollisionPolicy"))
        Assert.False(snap.ContainsKey("AppWidth"))

        ' Kept and foreign are byte-identical
        Assert.Equal("ru", snap("UiLanguage"))
        Assert.Equal("secret-key-xyz", snap("TranslateApiKey"))
        Assert.Equal("C:\OldPhotos", snap("RecentFolders"))
        Assert.Equal("server1", snap("Resources"))
        Assert.Equal("8080", snap("Share_ListenPort"))
        Assert.Equal("custom-value", snap("Share_CustomKey"))

        ' Marker is removed
        Assert.Empty(store.ReadMarker())

        ' A second run is a no-op
        Dim secondOutcome = ProfileResetRunner.ApplyPending(
            store,
            DummyBackupWriter(backups),
            Function() nowUtc,
            AddressOf NoopLog,
            AddressOf NoopLogEx)
        Assert.Equal(ProfileResetStatus.None, secondOutcome.Status)
    End Sub

    <Fact>
    Public Sub No_marker_is_a_noop()
        Dim store = CreateSampleStore()
        Dim backups As New List(Of IReadOnlyDictionary(Of String, String))()
        Dim outcome = ProfileResetRunner.ApplyPending(
            store,
            DummyBackupWriter(backups),
            Function() DateTime.UtcNow,
            AddressOf NoopLog,
            AddressOf NoopLogEx)

        Assert.Equal(ProfileResetStatus.None, outcome.Status)
        Assert.Empty(backups)
        Assert.True(store.ContainsKey("chkTopMost"))
    End Sub

    <Fact>
    Public Sub Stale_marker_is_removed_and_reported_expired()
        Dim store = CreateSampleStore()
        Dim nowUtc = New DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc)
        Dim staleTime = nowUtc.AddDays(-8)
        store.WriteMarker("1|" & staleTime.ToString("o", CultureInfo.InvariantCulture))

        Dim backups As New List(Of IReadOnlyDictionary(Of String, String))()
        Dim outcome = ProfileResetRunner.ApplyPending(
            store,
            DummyBackupWriter(backups),
            Function() nowUtc,
            AddressOf NoopLog,
            AddressOf NoopLogEx)

        Assert.Equal(ProfileResetStatus.Expired, outcome.Status)
        Assert.Empty(backups)
        Assert.Empty(store.ReadMarker())
        Assert.True(store.ContainsKey("chkTopMost"))
    End Sub

    <Fact>
    Public Sub Garbage_marker_is_removed_and_changes_nothing()
        Dim store = CreateSampleStore()
        store.WriteMarker("garbage|not-valid")

        Dim backups As New List(Of IReadOnlyDictionary(Of String, String))()
        Dim outcome = ProfileResetRunner.ApplyPending(
            store,
            DummyBackupWriter(backups),
            Function() DateTime.UtcNow,
            AddressOf NoopLog,
            AddressOf NoopLogEx)

        Assert.Equal(ProfileResetStatus.None, outcome.Status)
        Assert.Empty(backups)
        Assert.Empty(store.ReadMarker())
        Assert.True(store.ContainsKey("chkTopMost"))
    End Sub

    <Fact>
    Public Sub Backup_that_throws_deletes_nothing_and_reports_skipped()
        Dim store = CreateSampleStore()
        store.WriteMarker("1|" & DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))

        Dim outcome = ProfileResetRunner.ApplyPending(
            store,
            AddressOf ThrowingBackupWriter,
            Function() DateTime.UtcNow,
            AddressOf NoopLog,
            AddressOf NoopLogEx)

        Assert.Equal(ProfileResetStatus.Skipped, outcome.Status)
        Assert.False(String.IsNullOrEmpty(outcome.FailureCause))
        Assert.Empty(store.ReadMarker())
        ' Nothing was deleted
        Assert.True(store.ContainsKey("chkTopMost"))
        Assert.True(store.ContainsKey("MoveOn1"))
    End Sub

    <Fact>
    Public Sub Delete_that_throws_for_two_names_reports_partial_and_still_deletes_the_rest()
        Dim store = CreateSampleStore()
        store.SetThrowOnDelete("chkTopMost", "MoveOn1")
        store.WriteMarker("1|" & DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))

        Dim backups As New List(Of IReadOnlyDictionary(Of String, String))()
        Dim outcome = ProfileResetRunner.ApplyPending(
            store,
            DummyBackupWriter(backups),
            Function() DateTime.UtcNow,
            AddressOf NoopLog,
            AddressOf NoopLogEx)

        Assert.Equal(ProfileResetStatus.PartialSuccess, outcome.Status)
        Assert.Equal(2, outcome.FailedCount)
        Assert.Equal(ProfileResetScope.ResetNames.Count, outcome.TotalCount)
        Assert.False(String.IsNullOrEmpty(outcome.BackupPath))

        ' Failed names remain
        Assert.True(store.ContainsKey("chkTopMost"))
        Assert.True(store.ContainsKey("MoveOn1"))
        ' Other reset names were deleted
        Assert.False(store.ContainsKey("OcrEnabled"))
        Assert.False(store.ContainsKey("NameCollisionPolicy"))
        Assert.False(store.ContainsKey("AppWidth"))

        ' Kept values remain
        Assert.True(store.ContainsKey("UiLanguage"))
        Assert.True(store.ContainsKey("TranslateApiKey"))

        ' Marker is gone
        Assert.Empty(store.ReadMarker())
    End Sub

    <Fact>
    Public Sub HasAnyResetSettings_reports_true_only_when_reset_scope_keys_exist()
        Dim store As New InMemoryProfileStore()
        Assert.False(ProfileResetRunner.HasAnyResetSettings(store))

        store.SetValue("UiLanguage", "en")
        store.SetValue("Share_ListenPort", "8080")
        Assert.False(ProfileResetRunner.HasAnyResetSettings(store))

        store.SetValue("chkTopMost", "0")
        Assert.True(ProfileResetRunner.HasAnyResetSettings(store))
    End Sub

End Class
#End If
