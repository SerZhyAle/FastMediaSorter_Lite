Option Strict On

Imports System.IO
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The autostart takeover rule (ticket 054 H-16). The one-shot migration used to rewrite
''' the HKCU Run value on ANY difference from its own command, so a portable ZIP copy, a dev
''' build or the other edition's Share Manager that merely RAN once re-pointed logon
''' autostart at itself - and deleting that copy left a dead entry behind a checkbox that
''' still read "on". The predicate here is the whole decision, kept pure: what may be
''' rewritten, and what must be left alone.
''' </summary>
Public Class AutostartRewriteTests

    Private Shared ReadOnly CompanionExe As String =
        Path.Combine(Path.GetTempPath(), "fms-autostart-" & Guid.NewGuid().ToString("N"), "Companion.exe")

    Private Shared Function Target(path As String) As String
        Return """" & path & """ " & Program.TrayFlag
    End Function

    <Fact>
    Public Sub OwnExactCommand_IsLeftAlone()
        Assert.False(AutostartManager.ShouldRewriteRunTarget(Target(CompanionExe), CompanionExe))
    End Sub

    <Fact>
    Public Sub OwnExeWithAWrongFlag_IsRewrittenToFixIt()
        ' Quoted us, but without --tray: the window would pop at every logon.
        Assert.True(AutostartManager.ShouldRewriteRunTarget("""" & CompanionExe & """", CompanionExe))
    End Sub

    <Fact>
    Public Sub OldBareWorkerTarget_IsMigrated()
        Dim worker As String = Path.Combine(Path.GetTempPath(), "fms-autostart-" & Guid.NewGuid().ToString("N"),
                                            WorkerProcess.ExeName)
        Assert.True(AutostartManager.ShouldRewriteRunTarget(Target(worker), CompanionExe))
    End Sub

    <Fact>
    Public Sub OldViewerTarget_IsMigrated()
        For Each name As String In {"FastMediaSorter_LITE.exe", "FastMediaSorter_x86.exe"}
            Dim viewer As String = Path.Combine(Path.GetTempPath(), "fms-autostart-" & Guid.NewGuid().ToString("N"), name)
            Assert.True(AutostartManager.ShouldRewriteRunTarget(Target(viewer), CompanionExe), name)
        Next
    End Sub

    <Fact>
    Public Sub DeadEntry_IsRepaired()
        ' The file it names is gone: pointing the entry at the running exe is a repair,
        ' not a takeover - nothing else can ever start from it.
        Dim gone As String = Path.Combine(Path.GetTempPath(), "fms-autostart-" & Guid.NewGuid().ToString("N"),
                                          "fms-share-worker.exe")
        Assert.True(AutostartManager.ShouldRewriteRunTarget(Target(gone), CompanionExe))
    End Sub

    <Fact>
    Public Sub AnotherLiveCompanionCopy_OwnsTheSlot()
        ' A different installation's Companion that EXISTS is not ours to displace -
        ' merely running is not consent to take the logon entry over.
        Dim other As String = Path.Combine(Path.GetTempPath(), "fms-autostart-" & Guid.NewGuid().ToString("N"),
                                           "Companion.exe")
        Directory.CreateDirectory(Path.GetDirectoryName(other))
        File.WriteAllText(other, "x")
        Try
            Assert.False(AutostartManager.ShouldRewriteRunTarget(Target(other), CompanionExe))
        Finally
            Try : File.Delete(other) : Catch : End Try
            Try : Directory.Delete(Path.GetDirectoryName(other)) : Catch : End Try
        End Try
    End Sub

    <Fact>
    Public Sub EmptyValueOrCommand_IsLeftAlone()
        Assert.False(AutostartManager.ShouldRewriteRunTarget(Nothing, CompanionExe))
        Assert.False(AutostartManager.ShouldRewriteRunTarget("", CompanionExe))
        Assert.False(AutostartManager.ShouldRewriteRunTarget(Target(CompanionExe), ""))
    End Sub

End Class
