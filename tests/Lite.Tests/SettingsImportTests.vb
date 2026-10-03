#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' 103 and 105 L10-L12 (057.S15-1, S14-1, S14-2, S14-3): the import previews the policy that will
''' really apply and takes an app-private, exclusively created, read-back-verified backup BEFORE
''' touching anything; settings rows never lose uncommitted typing and a measurement can only land
''' if it is the newest; the association caption and action share one chooser.
''' </summary>
Public Class SettingsImportTests

    Private Shared Function TempDir() As String
        Dim p As String = Path.Combine(Path.GetTempPath(), "fms-backup-" & Guid.NewGuid().ToString("N"))
        Return p
    End Function

    ' ----------------------------------------------------------------- backup store ----

    <Fact>
    Public Sub A_protection_failure_refuses_the_backup_before_opening_a_file_or_mutating_preferences()
        Dim current As ModernViewerPreferences = Profile()
        Dim before As String = current.ExportJson(True)
        Dim opened As Boolean = False
        Assert.Throws(Of UnauthorizedAccessException)(
            Sub() SettingsImportBackupStore.Create(current, TempDir(),
                Function(path)
                    opened = True
                    Return New MemoryStream()
                End Function, protectDirectory:=Sub(path) Throw New UnauthorizedAccessException("private ACL unavailable")))
        Assert.False(opened)
        Assert.Equal(before, current.ExportJson(True))
    End Sub

    <Fact>
    Public Sub A_fixed_name_collision_is_retried_without_overwriting_the_existing_snapshot()
        Dim dir As String = TempDir()
        Try
            Dim original As String = SettingsImportBackupStore.Create(Profile(), dir, nextName:=Function() "fixed.json")
            Dim before As String = File.ReadAllText(original)
            Assert.Throws(Of IOException)(Sub() SettingsImportBackupStore.Create(New ModernViewerPreferences(), dir, nextName:=Function() "fixed.json"))
            Assert.Equal(before, File.ReadAllText(original))
            Assert.Single(Directory.GetFiles(dir))
        Finally
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        End Try
    End Sub

    <Fact>
    Public Sub A_failed_backup_does_not_normalize_or_mutate_the_live_profile()
        Dim dir As String = TempDir()
        Dim current As ModernViewerPreferences = Profile()
        current.ArchiveCacheMaxMb = -1
        Try
            Assert.Throws(Of IOException)(Sub() SettingsImportBackupStore.Create(current, dir,
                Function(path) New FailingStream(path)))
            Assert.Equal(-1, current.ArchiveCacheMaxMb)
            Assert.Empty(Directory.GetFiles(dir))
        Finally
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        End Try
    End Sub

    <Fact>
    Public Sub Backup_storage_and_files_exclude_other_accounts()
        Dim dir As String = TempDir()
        Try
            Dim backup As String = SettingsImportBackupStore.Create(Profile(), dir)
            Using identity As Security.Principal.WindowsIdentity = Security.Principal.WindowsIdentity.GetCurrent()
                Dim security As Security.AccessControl.FileSecurity = New FileInfo(backup).GetAccessControl()
                Assert.True(security.AreAccessRulesProtected)
                For Each rule As Security.AccessControl.FileSystemAccessRule In security.GetAccessRules(True, True, GetType(Security.Principal.SecurityIdentifier))
                    Assert.Equal(identity.User, rule.IdentityReference)
                    Assert.False(rule.IsInherited)
                Next
            End Using
        Finally
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        End Try
    End Sub

    Private Shared Function Profile() As ModernViewerPreferences
        Dim p As New ModernViewerPreferences() With {.ArchiveCacheMaxMb = 2048, .RecentFilesLimit = 7, .ConfirmDelete = "never"}
        p.StoreFolderZoom("D:\Private\Photos", 1.75R)
        p.VideoPositionHistory = "[{""Path"":""D:\\Private\\clip.mp4"",""LastWriteUtcTicks"":1,""Length"":2,""Position"":0.4}]"
        Return p
    End Function

    <Fact>
    Public Sub The_backup_holds_the_CURRENT_profile_including_personal_history_and_reloads_equal()
        Dim dir As String = TempDir()
        Try
            Dim current As ModernViewerPreferences = Profile()
            Dim path As String = SettingsImportBackupStore.Create(current, dir)

            Assert.True(File.Exists(path))
            Assert.StartsWith(dir, path)
            Dim restored As ModernViewerPreferences = SettingsImportBackupStore.Restore(path)
            Assert.Equal(current.ExportJson(True), restored.ExportJson(True))
            Assert.Contains("Private", File.ReadAllText(path)) ' the personal rollback state is in it
            Assert.Equal("never", restored.ConfirmDelete)       ' an explicit owner choice survives the round trip
        Finally
            Try : Directory.Delete(dir, True) : Catch : End Try
        End Try
    End Sub

    <Fact>
    Public Sub An_import_from_a_read_only_folder_still_gets_its_backup_in_the_private_folder()
        Dim source As String = TempDir()
        Dim backups As String = TempDir()
        Try
            Directory.CreateDirectory(source)
            Dim file As String = Path.Combine(source, "in.fms-settings.json")
            IO.File.WriteAllText(file, Profile().ExportJson())
            IO.File.SetAttributes(file, FileAttributes.ReadOnly) ' as a locked share or a stick would be
            Dim backupPath As String = SettingsImportBackupStore.Create(Profile(), backups)
            Assert.StartsWith(backups, backupPath)
            Assert.DoesNotContain(source, backupPath)
            IO.File.SetAttributes(file, FileAttributes.Normal)
        Finally
            Try : Directory.Delete(source, True) : Catch : End Try
            Try : Directory.Delete(backups, True) : Catch : End Try
        End Try
    End Sub

    <Fact>
    Public Sub An_existing_backup_is_never_overwritten_and_names_are_retried_at_most_three_times()
        Dim dir As String = TempDir()
        Try
            Dim first As String = SettingsImportBackupStore.Create(Profile(), dir)
            Dim before As String = File.ReadAllText(first)

            Dim attempts As New List(Of String)()
            Dim ex As IOException = Assert.Throws(Of IOException)(
                Sub()
                    SettingsImportBackupStore.Create(Profile(), dir,
                        Function(candidate As String) As Stream
                            attempts.Add(candidate)
                            File.WriteAllText(candidate, "someone else") ' the name is taken by another writer
                            Throw New IOException("exists")
                        End Function)
                End Sub)
            Assert.Equal(SettingsImportBackupStore.MaxNameAttempts, attempts.Count)
            Assert.Equal(before, File.ReadAllText(first))
            ' What other writers created is not ours to delete.
            Assert.All(attempts, Sub(a) Assert.Equal("someone else", File.ReadAllText(a)))
            GC.KeepAlive(ex)
        Finally
            Try : Directory.Delete(dir, True) : Catch : End Try
        End Try
    End Sub

    <Fact>
    Public Sub An_unwritable_destination_throws_so_the_import_does_not_happen_and_nothing_is_left()
        Dim dir As String = TempDir()
        Dim blocker As String = dir & "-file"
        Try
            File.WriteAllText(blocker, "x") ' the "directory" is a file: creation must fail, never fall back to TEMP
            Assert.ThrowsAny(Of Exception)(Sub() SettingsImportBackupStore.Create(Profile(), blocker))
            Assert.False(Directory.Exists(blocker))
        Finally
            Try : File.Delete(blocker) : Catch : End Try
        End Try
    End Sub

    <Fact>
    Public Sub A_backup_that_does_not_read_back_identical_is_deleted_and_the_import_is_refused()
        Dim dir As String = TempDir()
        Try
            Assert.Throws(Of IOException)(
                Sub()
                    SettingsImportBackupStore.Create(Profile(), dir, Nothing,
                        Function(path As String) As String
                            Return Profile().ExportJson(True).Replace("2048", "999")
                        End Function)
                End Sub)
            Assert.Empty(Directory.GetFiles(dir)) ' only the file this call created, and it is gone
        Finally
            Try : Directory.Delete(dir, True) : Catch : End Try
        End Try
    End Sub

    <Fact>
    Public Sub A_write_failure_deletes_only_the_partial_file_of_this_call()
        Dim dir As String = TempDir()
        Try
            Dim seed As String = SettingsImportBackupStore.Create(Profile(), dir)
            File.Delete(seed)
            Dim bystander As String = Path.Combine(dir, "settings-backup-older.fms-settings.json")
            File.WriteAllText(bystander, "older backup")
            Assert.Throws(Of IOException)(
                Sub()
                    SettingsImportBackupStore.Create(Profile(), dir,
                        Function(path As String) As Stream
                            Return New FailingStream(path)
                        End Function)
                End Sub)
            Assert.Equal({bystander}, Directory.GetFiles(dir))
            Assert.Equal("older backup", File.ReadAllText(bystander))
        Finally
            Try : Directory.Delete(dir, True) : Catch : End Try
        End Try
    End Sub

    ''' <summary>Creates its file (as the exclusive open does) and then fails every write.</summary>
    Private NotInheritable Class FailingStream
        Inherits FileStream

        Friend Sub New(path As String)
            MyBase.New(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
        End Sub

        Public Overrides Sub Write(buffer() As Byte, offset As Integer, count As Integer)
            Throw New IOException("disk full")
        End Sub
    End Class

    ' ---------------------------------------------------------------------- the plan ----

    <Fact>
    Public Sub The_preview_is_computed_from_the_normalized_document_not_the_raw_input()
        Dim current As New ModernViewerPreferences()
        Dim imported As ModernViewerPreferences = ModernViewerPreferences.FromJson(
            "{""SchemaVersion"":1,""Preferences"":{""ArchiveMaxEntries"":2147483647,""SecureRememberMinutes"":-9}}")
        Dim plan As SettingsImportPlan.Plan = SettingsImportPlan.Build(current, imported)
        Assert.Contains(plan.ReplaceChanges, Function(c) c.StartsWith("ArchiveMaxEntries:") AndAlso c.EndsWith("-> " & ModernViewerPreferences.ArchiveMaxEntriesMax.ToString(CultureInfo.InvariantCulture)))
        Assert.DoesNotContain(plan.ReplaceChanges, Function(c) c.Contains("2147483647"))
    End Sub

    <Fact>
    Public Sub Merge_applies_only_values_that_differ_from_the_shipped_defaults()
        Dim current As New ModernViewerPreferences() With {.RecentFilesLimit = 33, .NameCollisionPolicy = "replace"}
        ' The document says "ask" - which IS the shipped default - and a non-default limit.
        Dim imported As New ModernViewerPreferences() With {.NameCollisionPolicy = "ask", .RecentFoldersLimit = 11}
        Dim plan As SettingsImportPlan.Plan = SettingsImportPlan.Build(current, imported)

        Assert.Equal("replace", plan.Merged.NameCollisionPolicy) ' an imported default cannot replace a current non-default
        Assert.Equal(11, plan.Merged.RecentFoldersLimit)
        Assert.Equal(33, plan.Merged.RecentFilesLimit)
        ' ...while Replace would have taken the document's "ask".
        Assert.Contains(plan.ReplaceChanges, Function(c) c.StartsWith("NameCollisionPolicy:"))
        Assert.DoesNotContain(plan.MergeChanges, Function(c) c.StartsWith("NameCollisionPolicy:"))
    End Sub

    <Fact>
    Public Sub The_preview_names_the_policy_that_will_apply_replace_and_merge_can_differ_in_size()
        Dim current As New ModernViewerPreferences() With {.RecentFilesLimit = 33}
        Dim imported As New ModernViewerPreferences() With {.RecentFoldersLimit = 11}
        Dim plan As SettingsImportPlan.Plan = SettingsImportPlan.Build(current, imported)
        Assert.Equal(2, plan.ReplaceChanges.Count) ' Replace also resets RecentFilesLimit to the document's default
        Assert.Equal(1, plan.MergeChanges.Count)
    End Sub

    <Fact>
    Public Sub Building_a_plan_never_mutates_the_live_model()
        Dim current As New ModernViewerPreferences() With {.RecentFilesLimit = 33}
        Dim snapshot As String = current.ExportJson(True)
        SettingsImportPlan.Build(current, New ModernViewerPreferences() With {.RecentFilesLimit = 5})
        Assert.Equal(snapshot, current.ExportJson(True))
        Assert.Equal(33, current.RecentFilesLimit)
    End Sub

    <Fact>
    Public Sub CopyFrom_keeps_the_destination_object_identity()
        Dim live As New ModernViewerPreferences()
        Dim same As ModernViewerPreferences = live
        live.CopyFrom(New ModernViewerPreferences() With {.RecentFilesLimit = 5})
        Assert.Same(same, live)
        Assert.Equal(5, live.RecentFilesLimit)
    End Sub

    ' ------------------------------------------------------- 103: the wider inventory ----

    <Theory>
    <InlineData("{")>
    <InlineData("")>
    <InlineData("null")>
    <InlineData("{""SchemaVersion"":2,""Preferences"":{}}")>
    <InlineData("{""SchemaVersion"":1}")>
    <InlineData("{""SchemaVersion"":1,""Preferences"":{""ArchiveCacheMaxMb"":""lots""}}")>
    <InlineData("{""SchemaVersion"":1,""Preferences"":{""ArchiveCacheMaxMb"":99999999999}}")>
    <InlineData("{""SchemaVersion"":1,""Preferences"":{""ArchiveCacheMaxMb"":1.5}}")>
    <InlineData("{""SchemaVersion"":1,""Preferences"":{""ArchiveCacheMaxMb"":true}}")>
    <InlineData("{""SchemaVersion"":1,""Preferences"":{""ArchiveCacheMaxMb"":null}}")>
    <InlineData("{""SchemaVersion"":1,""Preferences"":{""VideoAutoplay"":""yes""}}")>
    <InlineData("{""SchemaVersion"":1,""Preferences"":{""ConfirmDelete"":5}}")>
    Public Sub Malformed_wrong_typed_or_overflowing_documents_fail_before_any_mutation(json As String)
        Dim live As New ModernViewerPreferences() With {.ArchiveCacheMaxMb = 777}
        Assert.ThrowsAny(Of Exception)(Sub() ModernViewerPreferences.FromJson(json))
        Assert.Equal(777, live.ArchiveCacheMaxMb) ' a failed parse never produced a zero that could be applied
    End Sub

    <Fact>
    Public Sub A_missing_field_keeps_its_constructor_default_it_does_not_become_zero()
        Dim p As ModernViewerPreferences = ModernViewerPreferences.FromJson("{""SchemaVersion"":1,""Preferences"":{""RecentFilesLimit"":9}}")
        Assert.Equal(9, p.RecentFilesLimit)
        Assert.Equal(New ModernViewerPreferences().ArchiveCacheMaxMb, p.ArchiveCacheMaxMb)
        Assert.Equal(New ModernViewerPreferences().SecureRememberMinutes, p.SecureRememberMinutes)
    End Sub

    <Fact>
    Public Sub Every_writable_integer_property_stays_inside_a_declared_range_for_the_extremes()
        ' Not Clamp-against-the-same-constants: the oracle is "the extreme does not survive, and the
        ' result is stable", which holds for any policy a new property is given.
        For Each prop As Reflection.PropertyInfo In ModernViewerPreferences.WritableProperties().Where(Function(x) x.PropertyType = GetType(Integer))
            For Each extreme As Integer In New Integer() {Integer.MinValue, Integer.MaxValue}
                Dim p As New ModernViewerPreferences()
                prop.SetValue(p, extreme)
                p.Normalize()
                Dim once As Integer = CInt(prop.GetValue(p))
                p.Normalize()
                Assert.True(once <> extreme, prop.Name & " keeps " & extreme.ToString(CultureInfo.InvariantCulture))
                Assert.Equal(once, CInt(prop.GetValue(p)))
            Next
        Next
    End Sub

    ' -------------------------------------------------------------- 105 L10-L11 ----

    <Fact>
    Public Sub Only_the_newest_measurement_may_update_its_label()
        Dim gate As New RefreshGate()
        Dim older As Integer = gate.Begin()
        Dim newer As Integer = gate.Begin()
        Assert.False(gate.IsLatest(older)) ' finished late: its result must not overwrite the newer one
        Assert.True(gate.IsLatest(newer))
    End Sub

    <Fact>
    Public Sub Overlapping_measurements_finishing_in_either_order_leave_the_newest_value()
        Dim gate As New RefreshGate()
        Dim label As String = ""
        Dim first As Integer = gate.Begin()
        Dim second As Integer = gate.Begin()
        ' second finishes first, then the stale first one arrives.
        If gate.IsLatest(second) Then label = "second"
        If gate.IsLatest(first) Then label = "first"
        Assert.Equal("second", label)
    End Sub

    <Theory>
    <InlineData(False, False, True)>
    <InlineData(False, True, False)>
    <InlineData(True, False, True)>
    <InlineData(True, True, False)>
    Public Sub A_focused_field_with_uncommitted_text_is_not_overwritten_by_a_refresh(focused As Boolean, dirty As Boolean, reload As Boolean)
        Assert.Equal(reload, SettingsRowRefresh.ShouldReload(focused, dirty))
    End Sub

    <Fact>
    Public Sub A_language_switch_cannot_change_the_selected_choice_because_the_token_is_not_the_caption()
        Dim tokens As String() = {"ask", "skip", "rename", "replace"}
        Assert.Equal(2, SettingsRowRefresh.SelectedToken(tokens, "rename"))
        Assert.Equal(0, SettingsRowRefresh.SelectedToken(tokens, "vanished")) ' a retired token falls back to the first
        Assert.Equal(0, SettingsRowRefresh.SelectedToken(tokens, Nothing))
    End Sub

    <Fact>
    Public Sub The_association_caption_and_action_come_from_one_chooser()
        Assert.Equal(AssociationMode.OpenWindowsSettings, AssociationUx.ModeFor(True))
        Assert.Equal(AssociationMode.RegisterForCurrentUser, AssociationUx.ModeFor(False))
        Assert.NotEqual(AssociationUx.Caption(AssociationMode.OpenWindowsSettings), AssociationUx.Caption(AssociationMode.RegisterForCurrentUser))
    End Sub

    <Fact>
    Public Sub Both_captions_exist_in_every_shipped_language()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                For Each mode As AssociationMode In {AssociationMode.OpenWindowsSettings, AssociationMode.RegisterForCurrentUser}
                    Assert.False(String.IsNullOrWhiteSpace(AssociationUx.Caption(mode)), code & "/" & mode.ToString())
                Next
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub Cancelled_import_leaves_live_preferences_unmodified()
        Dim current As ModernViewerPreferences = Profile()
        Dim originalJson As String = current.ExportJson(True)

        Dim importedJson As String = "{""SchemaVersion"":1,""Preferences"":{""RecentFilesLimit"":100,""ArchiveCacheMaxMb"":8192}}"
        Dim imported As ModernViewerPreferences = ModernViewerPreferences.FromJson(importedJson)
        Dim plan As SettingsImportPlan.Plan = SettingsImportPlan.Build(current, imported)

        Assert.NotEmpty(plan.ReplaceChanges)
        ' User chooses Cancel (DialogResult.Cancel):
        ' No backup is made, no CopyFrom / Replace is performed.
        Assert.Equal(originalJson, current.ExportJson(True))
        Assert.Equal(2048, current.ArchiveCacheMaxMb)
        Assert.Equal(7, current.RecentFilesLimit)
        Assert.Equal("never", current.ConfirmDelete)
    End Sub

    <Fact>
    Public Sub Failed_backup_creation_aborts_import_without_mutating_live_preferences()
        Dim current As ModernViewerPreferences = Profile()
        Dim originalJson As String = current.ExportJson(True)

        Dim importedJson As String = "{""SchemaVersion"":1,""Preferences"":{""RecentFilesLimit"":99}}"
        Dim imported As ModernViewerPreferences = ModernViewerPreferences.FromJson(importedJson)
        Dim plan As SettingsImportPlan.Plan = SettingsImportPlan.Build(current, imported)

        Dim invalidDir As String = Path.Combine(Path.GetTempPath(), "nonexistent-" & Guid.NewGuid().ToString("N"), "nested")
        ' Backup creation fails because directory is blocked / unwritable:
        Dim backupFailed As Boolean = False
        Try
            SettingsImportBackupStore.Create(current, invalidDir,
                Function(candidate As String) As Stream
                    Throw New IOException("Disk write failure")
                End Function)
        Catch ex As Exception
            backupFailed = True
        End Try

        Assert.True(backupFailed)
        ' On backup failure, import must abort immediately and not mutate live preferences
        Assert.Equal(originalJson, current.ExportJson(True))
        Assert.Equal(7, current.RecentFilesLimit)
    End Sub

    <Fact>
    Public Sub Failed_or_corrupt_json_import_aborts_without_mutating_live_preferences()
        Dim current As ModernViewerPreferences = Profile()
        Dim originalJson As String = current.ExportJson(True)

        Dim malformedInputs As String() = {
            "{invalid json",
            "{""SchemaVersion"":1,""Preferences"":{""ArchiveCacheMaxMb"":""not-a-number""}}",
            "{""SchemaVersion"":999,""Preferences"":{}}"
        }

        For Each badJson As String In malformedInputs
            Dim parseFailed As Boolean = False
            Try
                Dim imported As ModernViewerPreferences = ModernViewerPreferences.FromJson(badJson)
                current.CopyFrom(imported)
            Catch ex As Exception
                parseFailed = True
            End Try

            Assert.True(parseFailed)
            Assert.Equal(originalJson, current.ExportJson(True))
        Next
    End Sub

    <Fact>
    Public Sub Specification_103_limits_integrate_into_import_and_apply_without_mutating_on_cancel()
        Dim current As ModernViewerPreferences = Profile()
        Dim originalJson As String = current.ExportJson(True)

        ' Imported file has out-of-bounds limits according to specification 103:
        Dim importedJson As String = "{""SchemaVersion"":1,""Preferences"":{" &
            """ArchiveCacheMaxMb"":999999," &
            """ArchiveMaxEntryMb"":-5," &
            """ArchiveMaxEntries"":0," &
            """SecureRememberMinutes"":-100," &
            """ConfirmDelete"":""invalidChoice""" &
        "}}"

        Dim imported As ModernViewerPreferences = ModernViewerPreferences.FromJson(importedJson)
        Dim plan As SettingsImportPlan.Plan = SettingsImportPlan.Build(current, imported)

        ' Verify plan was computed with normalized values (Spec 103)
        Assert.Equal(ModernViewerPreferences.ArchiveCacheMaxMbMax, imported.ArchiveCacheMaxMb)
        Assert.Equal(ModernViewerPreferences.ArchiveMaxEntryMbMin, imported.ArchiveMaxEntryMb)
        Assert.Equal(ModernViewerPreferences.ArchiveMaxEntriesMin, imported.ArchiveMaxEntries)
        Assert.Equal(0, imported.SecureRememberMinutes)
        Assert.Equal("always", imported.ConfirmDelete)

        ' Simulate Cancel: live preferences remain intact
        Assert.Equal(originalJson, current.ExportJson(True))

        ' Simulate Replace: live preferences safely receive normalized values
        Dim liveClone As ModernViewerPreferences = SettingsImportPlan.Clone(current)
        liveClone.CopyFrom(imported)
        liveClone.Normalize()

        Assert.Equal(ModernViewerPreferences.ArchiveCacheMaxMbMax, liveClone.ArchiveCacheMaxMb)
        Assert.Equal(ModernViewerPreferences.ArchiveMaxEntryMbMin, liveClone.ArchiveMaxEntryMb)
        Assert.Equal(ModernViewerPreferences.ArchiveMaxEntriesMin, liveClone.ArchiveMaxEntries)
        Assert.Equal(0, liveClone.SecureRememberMinutes)
        Assert.Equal("always", liveClone.ConfirmDelete)
    End Sub

    <Fact>
    Public Sub Merge_takes_the_normalized_limit_and_preserves_never_and_the_session_zero()
        ' The document carries a limit that must not survive raw, the owner's explicit
        ' "never", and an explicit zero (session lifetime). All three differ from the
        ' shipped defaults, so Merge takes them - but the limit only in its normalized form.
        Dim imported As ModernViewerPreferences = ModernViewerPreferences.FromJson(
            "{""SchemaVersion"":1,""Preferences"":{""ArchiveMaxEntries"":2147483647,""ConfirmDelete"":""never"",""SecureRememberMinutes"":0,""RecentFilesLimit"":9}}")
        Dim current As New ModernViewerPreferences()
        Dim plan As SettingsImportPlan.Plan = SettingsImportPlan.Build(current, imported)

        Assert.Equal(ModernViewerPreferences.ArchiveMaxEntriesMax, plan.Merged.ArchiveMaxEntries)
        Assert.Equal("never", plan.Merged.ConfirmDelete)
        Assert.Equal(0, plan.Merged.SecureRememberMinutes)
        Assert.Equal(9, plan.Merged.RecentFilesLimit)
        ' The preview of the merge names exactly these four properties.
        Assert.Equal(4, plan.MergeChanges.Count)
    End Sub

End Class
#End If
