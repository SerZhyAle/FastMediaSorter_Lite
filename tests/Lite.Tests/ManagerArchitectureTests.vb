#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports Xunit

Public Class ManagerArchitectureTests

    <Fact>
    Public Sub BoundaryGuard_RefusesEmptyAndRoots()
        Dim reason As String = String.Empty
        Assert.False(ManagerBoundaryGuard.CheckSafeExpendableRoot("", reason))
        Assert.Contains("empty", reason, StringComparison.OrdinalIgnoreCase)

        Assert.False(ManagerBoundaryGuard.CheckSafeExpendableRoot("   ", reason))
        Assert.False(ManagerBoundaryGuard.CheckSafeExpendableRoot("C:\", reason))
        Assert.Contains("root", reason, StringComparison.OrdinalIgnoreCase)

        Dim systemDrive As String = IO.Path.GetPathRoot(Environment.SystemDirectory)
        Assert.False(ManagerBoundaryGuard.CheckSafeExpendableRoot(systemDrive, reason))
    End Sub

    <Fact>
    Public Sub BoundaryGuard_RefusesSystemAndUserDirectories()
        Dim reason As String = String.Empty

        Dim userProfile As String = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        If Not String.IsNullOrEmpty(userProfile) Then
            Assert.False(ManagerBoundaryGuard.CheckSafeExpendableRoot(userProfile, reason))
            Assert.Contains("user profile", reason, StringComparison.OrdinalIgnoreCase)
        End If

        Dim windowsDir As String = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        If Not String.IsNullOrEmpty(windowsDir) Then
            Assert.False(ManagerBoundaryGuard.CheckSafeExpendableRoot(windowsDir, reason))
            Assert.Contains("Windows", reason, StringComparison.OrdinalIgnoreCase)
            Assert.False(ManagerBoundaryGuard.CheckSafeExpendableRoot(IO.Path.Combine(windowsDir, "System32"), reason))
        End If

        Dim picturesDir As String = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        If Not String.IsNullOrEmpty(picturesDir) Then
            Assert.False(ManagerBoundaryGuard.CheckSafeExpendableRoot(picturesDir, reason))
            Assert.Contains("Refused", reason, StringComparison.OrdinalIgnoreCase)
        End If

        Dim desktopDir As String = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        If Not String.IsNullOrEmpty(desktopDir) Then
            Assert.False(ManagerBoundaryGuard.CheckSafeExpendableRoot(desktopDir, reason))
        End If
    End Sub

    <Fact>
    Public Sub BoundaryGuard_AcceptsExpendableSubdirectory()
        Dim reason As String = String.Empty
        Dim testPath As String = IO.Path.Combine(IO.Path.GetTempPath(), "fms-test-boundary-check-" & Guid.NewGuid().ToString("N"))
        Assert.True(ManagerBoundaryGuard.CheckSafeExpendableRoot(testPath, reason), "Reason: " & reason)
        Assert.True(String.IsNullOrEmpty(reason))
    End Sub

    <Fact>
    Public Sub Architecture_ManagerForms_DoNotCallDirectFileMutations()
        Dim repoRoot As String = FindRepoRoot()
        Dim managerFormFiles As String() = Directory.GetFiles(IO.Path.Combine(repoRoot, "src"), "Image_Panel_Form.FileManager*.vb", SearchOption.TopDirectoryOnly)
        Assert.NotEmpty(managerFormFiles)

        Dim forbiddenMutations As String() = {
            "System.IO.File.Delete(",
            "File.Delete(",
            "System.IO.File.Move(",
            "File.Move(",
            "System.IO.Directory.Delete(",
            "Directory.Delete("
        }

        For Each file In managerFormFiles
            Dim text As String = System.IO.File.ReadAllText(file)
            For Each forbidden In forbiddenMutations
                Assert.DoesNotContain(forbidden, text)
            Next
        Next
    End Sub

    <Fact>
    Public Sub Architecture_ManagerForms_DoNotDirectlyInstantiateSshNet()
        Dim repoRoot As String = FindRepoRoot()
        Dim managerFiles As String() = Directory.GetFiles(IO.Path.Combine(repoRoot, "src", "FileManager"), "*.vb", SearchOption.AllDirectories)
        Dim managerFormFiles As String() = Directory.GetFiles(IO.Path.Combine(repoRoot, "src"), "Image_Panel_Form.FileManager*.vb", SearchOption.TopDirectoryOnly)
        Dim allManagerFiles = managerFiles.Concat(managerFormFiles).ToArray()

        For Each file In allManagerFiles
            Dim text As String = System.IO.File.ReadAllText(file)
            Assert.DoesNotContain("Renci.SshNet", text)
            Assert.DoesNotContain("New SftpClient", text)
        Next
    End Sub

    <Fact>
    Public Sub Architecture_ZipCreation_UsesVerifiedZipWriter()
        Dim repoRoot As String = FindRepoRoot()
        Dim archiveFile As String = IO.Path.Combine(repoRoot, "src", "FileManager", "ManagerArchiveCommands.vb")
        Assert.True(System.IO.File.Exists(archiveFile))
        Dim text As String = System.IO.File.ReadAllText(archiveFile)

        Assert.Contains("VerifiedZipWriter", text)
        Assert.DoesNotContain("ZipFile.CreateFromDirectory", text)
    End Sub

    <Fact>
    Public Sub Architecture_OpenPolicy_BlocksAllExecutableAndScriptExtensions()
        Dim forbiddenExts As String() = {
            ".exe", ".bat", ".cmd", ".com", ".ps1", ".vbs", ".vbe", ".js", ".jse",
            ".wsf", ".wsh", ".msc", ".msi", ".msp", ".scr", ".hta", ".cpl", ".pif", ".reg"
        }

        For Each ext In forbiddenExts
            Assert.True(ManagerOpenPolicy.IsDenySetExtension(ext), "Extension " & ext & " must be in deny set")
            Dim decision As ManagerOpenDecision = ManagerOpenPolicy.EvaluateFile("unsafe" & ext, hasLocalPath:=True, nonMediaAction:="open")
            Assert.Equal(ManagerOpenKind.Blocked, decision.Kind)
        Next
    End Sub

    <Fact>
    Public Sub Architecture_UnopenedFdSec_NeverMaterializedAsExtractedPlaintext()
        Dim decision As ManagerOpenDecision = ManagerOpenPolicy.EvaluateFile("vault.fd-sec", hasLocalPath:=True, nonMediaAction:="open")
        ' Opening a container hands off to viewer (which prompts for password) rather than shell open or raw extract
        Assert.Equal(ManagerOpenKind.ViewerHandoff, decision.Kind)
    End Sub

    Private Shared Function FindRepoRoot() As String
        Dim dir As String = AppContext.BaseDirectory
        While Not String.IsNullOrEmpty(dir)
            If System.IO.File.Exists(IO.Path.Combine(dir, "FastMediaSorter.sln")) Then
                Return dir
            End If
            dir = IO.Path.GetDirectoryName(dir)
        End While
        Throw New InvalidOperationException("Could not find repository root from " & AppContext.BaseDirectory)
    End Function

End Class
#End If
