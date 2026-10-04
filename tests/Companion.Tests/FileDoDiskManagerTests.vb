Option Strict On

Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>Where FileDO's Disk Manager is looked for, and how a refusal "already mounted by FileDO"
''' is put in plain words. No registry, no process, no file system: both are injected or pure.</summary>
<Collection(UiLanguageCollection.Name)>
Public Class FileDoDiskManagerTests

    Private Shared Function Exists(ParamArray present As String()) As Func(Of String, Boolean)
        Return Function(path) present.Contains(path, StringComparer.OrdinalIgnoreCase)
    End Function

    <Fact>
    Public Sub The_bundle_beside_the_program_is_tried_first()
        Dim found As String = FileDoDiskManager.Locate("C:\App\filedo", {"C:\FileDO"},
            Exists("C:\App\filedo\filedo_win.exe", "C:\FileDO\filedo_win.exe"))
        Assert.Equal("C:\App\filedo\filedo_win.exe", found)
    End Sub

    <Fact>
    Public Sub A_bundle_with_only_filedo_exe_falls_through_to_the_registered_installation()
        Dim found As String = FileDoDiskManager.Locate("C:\App\filedo", {"C:\Missing", "C:\FileDO"},
            Exists("C:\App\filedo\filedo.exe", "C:\FileDO\filedo_win.exe"))
        Assert.Equal("C:\FileDO\filedo_win.exe", found)
    End Sub

    <Fact>
    Public Sub Nothing_found_answers_empty_and_never_guesses_a_path()
        Assert.Equal("", FileDoDiskManager.Locate("C:\App\filedo", Array.Empty(Of String)(), Exists()))
        Assert.Equal("", FileDoDiskManager.Locate("", Nothing, Exists()))
    End Sub

    <Theory>
    <InlineData("FileDO", "C:\Program Files\FileDO\", "", "C:\Program Files\FileDO")>
    <InlineData("FileDO 26.9", "", """C:\Tools\FileDO\filedo_win.exe"",0", "C:\Tools\FileDO")>
    <InlineData("FileDO", "", "C:\Tools\FileDO\filedo_win.exe,1", "C:\Tools\FileDO")>
    <InlineData("FileDO", "", "C:\Tools\FileDO\filedo_win.exe", "C:\Tools\FileDO")>
    <InlineData("FileDO", "", "", "")>
    <InlineData("Fileable", "C:\Other", "", "")>
    <InlineData("Some FileDO plug-in", "C:\Other", "", "")>
    <InlineData("", "C:\Other", "", "")>
    Public Sub An_uninstall_entry_is_taken_only_when_it_is_FileDOs(name As String, location As String, icon As String, expected As String)
        Assert.Equal(expected, FileDoDiskManager.InstallDirFromEntry(name, location, icon))
    End Sub

    <Theory>
    <InlineData("C:\Program Files\FileDO\", "C:\Program Files\FileDO")>
    <InlineData("""C:\Tools\FileDO""", "C:\Tools\FileDO")>
    <InlineData("  ", "")>
    <InlineData(Nothing, "")>
    Public Sub FileDOs_own_InstallPath_value_names_the_folder(value As String, expected As String)
        ' The Uninstall entries of a real FileDO 26.10 carry no InstallLocation and a DisplayIcon inside
        ' C:\ProgramData\Package Cache, so only HKLM\SOFTWARE\FileDO\InstallPath finds the Disk Manager.
        Assert.Equal(expected, FileDoDiskManager.InstallDirFromInstallPath(value))
    End Sub

    <Fact>
    Public Sub Only_the_folder_comes_from_the_registry_the_file_name_is_ours()
        Dim found As String = FileDoDiskManager.Locate("", {"C:\Tools\FileDO"}, Function(p) True)
        Assert.Equal("C:\Tools\FileDO\" & FileDoDiskManager.ManagerExe, found)
        Assert.Equal("--disks", FileDoDiskManager.DisksArgument)
    End Sub

    <Fact>
    Public Sub A_disk_mounted_by_FileDO_is_said_in_plain_words_with_its_drive()
        Const reason As String = "already mounted by FileDO at D:; unmount it before sharing"
        Assert.True(DiskSharing.IsMountedByFileDo(reason))
        Dim text As String = DiskSharing.PlainReason(reason)
        Assert.Contains("D:", text)
        Assert.DoesNotContain("unmount it before sharing", text)
        Assert.DoesNotContain(";", text)
    End Sub

    <Fact>
    Public Sub A_mounted_refusal_without_a_drive_still_gets_the_plain_sentence()
        Dim text As String = DiskSharing.PlainReason("disk is already mounted by FileDO; unmount it before sharing")
        Assert.DoesNotContain("mounted by FileDO", text)
        Assert.DoesNotContain("{0}", text)
    End Sub

    <Fact>
    Public Sub Every_other_reason_is_left_exactly_as_the_worker_worded_it()
        Const reason As String = "bundled FileDO lacks disk-sharing metadata; update FMS for Windows"
        Assert.False(DiskSharing.IsMountedByFileDo(reason))
        Assert.Equal(reason, DiskSharing.PlainReason(reason))
        Assert.Equal("", DiskSharing.PlainReason(Nothing))
        Assert.False(DiskSharing.IsMountedByFileDo(Nothing))
    End Sub

    <Fact>
    Public Sub The_unavailable_note_and_an_operation_failure_both_use_the_plain_words()
        Dim status As New WorkerStatus With {.DiskSharingError = "already mounted by FileDO at E:; unmount it before sharing"}
        Assert.Contains("E:", DiskSharing.UnavailableText(status))
        Assert.DoesNotContain("unmount it before sharing", DiskSharing.UnavailableText(status))
        Dim failure As String = DiskSharing.FailureText(New DiskOperationRefusedException("already mounted by FileDO at E:; unmount it before sharing"))
        Assert.Contains("E:", failure)
        Assert.DoesNotContain("unmount it before sharing", failure)
    End Sub
End Class
