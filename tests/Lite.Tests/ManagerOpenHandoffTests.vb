#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO
Imports System.Threading
Imports Xunit

Public Class ManagerOpenHandoffTests
    Implements IDisposable

    Private ReadOnly tempDir As String

    Public Sub New()
        tempDir = Path.Combine(Path.GetTempPath(), "fms_open_handoff_test_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        ManagerShellOpen.LauncherSpy = Nothing
        Try
            If Directory.Exists(tempDir) Then
                Directory.Delete(tempDir, recursive:=True)
            End If
        Catch
        End Try
    End Sub

    <Fact>
    Public Sub ManagerActivationResult_Local_ExposesPropertiesCorrectly()
        Dim place As ManagerPlace = ManagerPlace.ForDrive("C:\", "C:")
        Dim loc As New ManagerLocation(place, "C:\Photos")
        Dim entry As New ManagerEntry(ManagerEntry.IdOf(place, "C:\Photos\test.jpg"), "test.jpg", "C:\Photos\test.jpg",
                                      False, False, False, False, 1024L, DateTime.UtcNow, "C:\Photos\test.jpg")
        Dim result As New ManagerActivationResult(loc, entry)

        Assert.False(result.IsRemote)
        Assert.Equal("C:\Photos\test.jpg", result.LocalPath)
        Assert.Equal("C:\Photos\test.jpg", result.FullPath)
        Assert.Same(loc, result.Location)
        Assert.Same(entry, result.Entry)
    End Sub

    <Fact>
    Public Sub ManagerActivationResult_Remote_ExposesPropertiesCorrectly()
        Dim desc As New ResourceDescriptor With {
            .Id = "srv1",
            .Kind = ResourceKind.Sftp,
            .DisplayName = "My Server",
            .RootPath = "/var/media"
        }
        Dim place As ManagerPlace = ManagerPlace.ForResource(desc)
        Dim loc As New ManagerLocation(place, "/var/media/sub")
        Dim entry As New ManagerEntry(ManagerEntry.IdOf(place, "/var/media/sub/img.png"), "img.png", "/var/media/sub/img.png",
                                      False, False, False, False, 2048L, DateTime.UtcNow, "")
        Dim result As New ManagerActivationResult(loc, entry)

        Assert.True(result.IsRemote)
        Assert.Equal("", result.LocalPath)
        Assert.Equal("/var/media/sub/img.png", result.FullPath)
        Assert.Same(loc, result.Location)
        Assert.Same(entry, result.Entry)
    End Sub

    <Fact>
    Public Sub ManagerShellOpen_LauncherSpy_InterceptsAllowedFile()
        Dim testFile As String = Path.Combine(tempDir, "document.txt")
        File.WriteAllText(testFile, "Hello world")

        Dim launchedPath As String = Nothing
        Dim launchedPicker As Boolean = False
        Dim callCount As Integer = 0

        ManagerShellOpen.LauncherSpy = Function(path As String, usePicker As Boolean, ownerHwnd As IntPtr) As Boolean
                                           launchedPath = path
                                           launchedPicker = usePicker
                                           callCount += 1
                                           Return True
                                       End Function

        Dim ok As Boolean = ManagerShellOpen.OpenFile(IntPtr.Zero, testFile, usePicker:=False)
        Assert.True(ok)
        Assert.Equal(1, callCount)
        Assert.Equal(testFile, launchedPath)
        Assert.False(launchedPicker)

        ok = ManagerShellOpen.OpenFile(IntPtr.Zero, testFile, usePicker:=True)
        Assert.True(ok)
        Assert.Equal(2, callCount)
        Assert.Equal(testFile, launchedPath)
        Assert.True(launchedPicker)
    End Sub

    <Fact>
    Public Sub ManagerShellOpen_LauncherSpy_BlocksDangerousFiles()
        Dim exeFile As String = Path.Combine(tempDir, "dangerous.exe")
        File.WriteAllText(exeFile, "fake exe")

        Dim batFile As String = Path.Combine(tempDir, "dangerous.bat")
        File.WriteAllText(batFile, "fake bat")

        Dim ps1File As String = Path.Combine(tempDir, "dangerous.ps1")
        File.WriteAllText(ps1File, "fake script")

        Dim callCount As Integer = 0
        ManagerShellOpen.LauncherSpy = Function(path As String, usePicker As Boolean, ownerHwnd As IntPtr) As Boolean
                                           callCount += 1
                                           Return True
                                       End Function

        Assert.False(ManagerShellOpen.OpenFile(IntPtr.Zero, exeFile, usePicker:=False))
        Assert.False(ManagerShellOpen.OpenFile(IntPtr.Zero, batFile, usePicker:=False))
        Assert.False(ManagerShellOpen.OpenFile(IntPtr.Zero, ps1File, usePicker:=False))
        Assert.Equal(0, callCount)
    End Sub

    <Fact>
    Public Sub ManagerShellOpen_OpenFile_FailsForNonexistentFile()
        Dim callCount As Integer = 0
        ManagerShellOpen.LauncherSpy = Function(path As String, usePicker As Boolean, ownerHwnd As IntPtr) As Boolean
                                           callCount += 1
                                           Return True
                                       End Function

        Dim missingPath As String = Path.Combine(tempDir, "nonexistent.txt")
        Dim ok As Boolean = ManagerShellOpen.OpenFile(IntPtr.Zero, missingPath, usePicker:=False)
        Assert.False(ok)
        Assert.Equal(0, callCount)
    End Sub

    <Fact>
    Public Sub ManagerExternalCache_BudgetChecks()
        Assert.True(ManagerExternalCache.IsWithinBudget(1024L, 1))
        Assert.False(ManagerExternalCache.IsWithinBudget(2L * 1024L * 1024L, 1))
        Assert.True(ManagerExternalCache.IsWithinBudget(500L * 1024L * 1024L, 0))
    End Sub

    <Fact>
    Public Sub ManagerExternalCache_PrepareLeasePath_ReturnsSubfolderAndPreservesLeaf()
        Dim leasePath As String = ManagerExternalCache.PrepareLeasePath("res123", "/docs/reports", "annual.pdf")
        Assert.EndsWith("annual.pdf", leasePath, StringComparison.OrdinalIgnoreCase)
        Dim parentDir As String = Path.GetDirectoryName(leasePath)
        Assert.True(Directory.Exists(parentDir))
        Assert.StartsWith(ManagerExternalCache.GetCacheRoot(), parentDir, StringComparison.OrdinalIgnoreCase)
    End Sub

    <Fact>
    Public Sub ManagerExternalCache_RetainLease_And_PruneUnleased()
        Dim pathA As String = ManagerExternalCache.PrepareLeasePath("resA", "/folder", "leased.pdf")
        Dim pathB As String = ManagerExternalCache.PrepareLeasePath("resB", "/folder", "unleased.pdf")

        File.WriteAllBytes(pathA, New Byte(1024 * 50) {})
        File.WriteAllBytes(pathB, New Byte(1024 * 50) {})

        ManagerExternalCache.RetainLease(pathA)

        ' Prune with budget 0MB: unleased files must be purged, leased files kept
        ManagerExternalCache.PruneUnleased(0)

        Assert.True(File.Exists(pathA))
        Assert.False(File.Exists(pathB))

        Try
            File.Delete(pathA)
        Catch
        End Try
    End Sub

    <Fact>
    Public Sub ManagerExternalCache_RemoteNoticeText_IsNonEmpty()
        Dim text As String = ManagerExternalCache.RemoteNoticeText()
        Assert.False(String.IsNullOrWhiteSpace(text))
    End Sub

    <Fact>
    Public Sub ManagerShellOpen_RevealInExplorer_NonexistentPath_DoesNotThrow()
        Dim nonexistent As String = Path.Combine(tempDir, "does_not_exist.txt")
        ManagerShellOpen.RevealInExplorer(nonexistent)
    End Sub

End Class
#End If
