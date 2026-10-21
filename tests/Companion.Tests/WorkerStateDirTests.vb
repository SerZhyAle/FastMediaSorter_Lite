Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' WORKER-LAUNCH 1.0 section 1: the host resolves the worker's state directory by the worker's own order
''' (explicit override, then a machine directory this account can READ, then the per-user one) in ONE place,
''' and passes the chosen path as <c>--datadir</c> when it starts a worker. "Usable" is the worker's
''' <c>dirUsable</c> (a directory that can be opened for listing), not "exists": a machine directory this
''' account may not read is not adopted.
'''
''' No worker is started here - the control pipe is a machine-wide singleton. The spawn command line is
''' tested as data (<see cref="WorkerProcess.SpawnArguments"/>).
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class WorkerStateDirTests

    Private Shared Function MakeTempDir(Optional create As Boolean = True) As String
        Dim dir As String = Path.Combine(Path.GetTempPath(), "fms-244-" & Guid.NewGuid().ToString("N"))
        If create Then Directory.CreateDirectory(dir)
        Return dir
    End Function

    Private Shared Sub DeleteQuietly(dir As String)
        Try : If Directory.Exists(dir) Then Directory.Delete(dir, recursive:=True)
        Catch : End Try
    End Sub

    Private Shared Function ThroughRealProbe() As Func(Of String, Boolean)
        Return AddressOf WorkerStateDir.IsUsable
    End Function

    ' --- the order, as a pure function -------------------------------------------------------

    <Fact>
    Public Sub TheExplicitOverride_Wins_AndTheMachineDirIsNotEvenProbed()
        Dim probed As Boolean = False
        Dim chosen As String = WorkerStateDir.Choose("X:\explicit", "X:\machine", "X:\user",
                                                    Function(d)
                                                        probed = True
                                                        Return True
                                                    End Function)
        Assert.Equal("X:\explicit", chosen)
        Assert.False(probed)
    End Sub

    <Fact>
    Public Sub AUsableMachineDir_IsChosen_OverTheUserDir()
        Assert.Equal("X:\machine", WorkerStateDir.Choose("", "X:\machine", "X:\user", Function(d) d = "X:\machine"))
        Assert.Equal("X:\machine", WorkerStateDir.Choose(Nothing, "X:\machine", "X:\user", Function(d) True))
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("X:\machine")>
    Public Sub AMachineDirThatIsAbsentOrUnusable_FallsToTheUserDir(machine As String)
        Assert.Equal("X:\user", WorkerStateDir.Choose("", machine, "X:\user", Function(d) False))
    End Sub

    <Fact>
    Public Sub TheUserDir_IsChosenWhetherOrNotItExistsYet()
        ' The worker creates it on its first host key; the host names it all the same.
        Assert.Equal("X:\never-created", WorkerStateDir.Choose("", "X:\machine", "X:\never-created", Function(d) False))
        Assert.Equal("", WorkerStateDir.Choose("", "", "", Function(d) False))
    End Sub

    ' --- "usable" is the worker's dirUsable --------------------------------------------------

    <Fact>
    Public Sub UsableMeansADirectoryThatCanBeOpened_AnEmptyOneIncluded()
        Dim dir As String = MakeTempDir()
        Try
            Assert.True(WorkerStateDir.IsUsable(dir))
            File.WriteAllText(Path.Combine(dir, "hostkey"), "x")
            Assert.True(WorkerStateDir.IsUsable(dir))
        Finally
            DeleteQuietly(dir)
        End Try
    End Sub

    <Fact>
    Public Sub AnAbsentPath_AnEmptyPath_AndAFile_AreNotUsable()
        Dim dir As String = MakeTempDir()
        Try
            Assert.False(WorkerStateDir.IsUsable(MakeTempDir(create:=False)))
            Assert.False(WorkerStateDir.IsUsable(""))
            Assert.False(WorkerStateDir.IsUsable(Nothing))
            Dim file As String = Path.Combine(dir, "a-file")
            IO.File.WriteAllText(file, "x")
            Assert.False(WorkerStateDir.IsUsable(file))   ' the worker's Stat says "not a directory"
        Finally
            DeleteQuietly(dir)
        End Try
    End Sub

    ' --- the four cases of the brief, through the real probe ---------------------------------

    <Fact>
    Public Sub ProgramDataUsable_TheMachineDirIsChosen()
        Dim machine As String = MakeTempDir()
        Dim user As String = MakeTempDir()
        Try
            Assert.Equal(machine, WorkerStateDir.Choose("", machine, user, ThroughRealProbe()))
        Finally
            DeleteQuietly(machine) : DeleteQuietly(user)
        End Try
    End Sub

    <Fact>
    Public Sub ProgramDataAbsent_TheUserDirIsChosen()
        Dim machine As String = MakeTempDir(create:=False)
        Dim user As String = MakeTempDir()
        Try
            Assert.Equal(user, WorkerStateDir.Choose("", machine, user, ThroughRealProbe()))
        Finally
            DeleteQuietly(user)
        End Try
    End Sub

    ''' <summary>The case the old "does it exist" test got wrong: the machine directory is there but this
    ''' account may not list it (not enrolled by the Server installer). The worker falls back to the
    ''' per-user store; so does the host now, and it also states why the old rule disagreed - the
    ''' directory exists.</summary>
    <Fact>
    Public Sub ProgramDataPresentButNotReadable_FallsToTheUserDir()
        Dim machine As String = MakeTempDir()
        Dim user As String = MakeTempDir()
        Dim me_ As SecurityIdentifier = WindowsIdentity.GetCurrent().User
        Dim deny As New FileSystemAccessRule(me_, FileSystemRights.ListDirectory Or FileSystemRights.ReadData,
                                             InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny)
        Dim info As New DirectoryInfo(machine)
        Dim locked As Boolean = False
        Try
            Dim security As DirectorySecurity = FileSystemAclExtensions.GetAccessControl(info)
            security.AddAccessRule(deny)
            FileSystemAclExtensions.SetAccessControl(info, security)
            locked = True

            Assert.True(Directory.Exists(machine), "the directory is there - an existence test would adopt it")
            Assert.False(WorkerStateDir.IsUsable(machine), "this account cannot list it - the worker's dirUsable says no")
            Assert.Equal(user, WorkerStateDir.Choose("", machine, user, ThroughRealProbe()))
        Finally
            If locked Then
                Try
                    Dim security As DirectorySecurity = FileSystemAclExtensions.GetAccessControl(info)
                    security.RemoveAccessRule(deny)
                    FileSystemAclExtensions.SetAccessControl(info, security)
                Catch
                End Try
            End If
            DeleteQuietly(machine) : DeleteQuietly(user)
        End Try
    End Sub

    <Fact>
    Public Sub TheExplicitOverrideWins_EvenOverAUsableMachineDir_AndEvenWhenItDoesNotExist()
        Dim machine As String = MakeTempDir()
        Dim user As String = MakeTempDir()
        Try
            Dim named As String = MakeTempDir(create:=False)
            Assert.Equal(named, WorkerStateDir.Choose(named, machine, user, ThroughRealProbe()))
        Finally
            DeleteQuietly(machine) : DeleteQuietly(user)
        End Try
    End Sub

    ' --- the live resolution -----------------------------------------------------------------

    <Fact>
    Public Sub Resolve_ReadsTheWorkersOverrideVariable()
        Dim saved As String = Environment.GetEnvironmentVariable(WorkerStateDir.OverrideVariable)
        Dim named As String = MakeTempDir(create:=False)
        Try
            Assert.Equal("FMS_COMPANION_DATA_DIR", WorkerStateDir.OverrideVariable)
            Environment.SetEnvironmentVariable(WorkerStateDir.OverrideVariable, named)
            Assert.Equal(named, WorkerStateDir.ExplicitOverride())
            Assert.Equal(named, WorkerStateDir.Resolve())
            Assert.Equal(named, ServiceControl.ActiveDataDir())
        Finally
            Environment.SetEnvironmentVariable(WorkerStateDir.OverrideVariable, saved)
        End Try
    End Sub

    <Fact>
    Public Sub WithoutAnOverride_TheHostsAgree_OnTheOneDirectory()
        Dim saved As String = Environment.GetEnvironmentVariable(WorkerStateDir.OverrideVariable)
        Try
            Environment.SetEnvironmentVariable(WorkerStateDir.OverrideVariable, Nothing)
            Dim resolved As String = WorkerStateDir.Resolve()
            Assert.NotEqual("", resolved)
            Assert.Equal(resolved, ServiceControl.ActiveDataDir())
            ' Always one of the two named stores, never a third.
            Assert.Contains(resolved, New String() {ServiceControl.MachineDataDir(), ServiceControl.UserDataDir()})
            Assert.EndsWith("FastMediaSorterCompanion", resolved)
            ' The machine one only when this account can really use it.
            If resolved = ServiceControl.MachineDataDir() Then Assert.True(WorkerStateDir.IsUsable(resolved))
        Finally
            Environment.SetEnvironmentVariable(WorkerStateDir.OverrideVariable, saved)
        End Try
    End Sub

    ' --- --datadir on the spawn --------------------------------------------------------------

    <Fact>
    Public Sub TheSpawnPassesTheChosenDirectory_AsOneValue()
        Dim dir As String = "C:\Users\Some One\AppData\Local\FastMediaSorterCompanion"
        Assert.Equal(New List(Of String) From {"--datadir", dir}, WorkerProcess.SpawnArguments(dir))

        Dim psi As Diagnostics.ProcessStartInfo = WorkerProcess.SpawnStartInfo("C:\app\companion\fms-share-worker.exe", dir)
        Assert.Equal(New String() {"--datadir", dir}, psi.ArgumentList.ToArray())   ' a path with spaces is ONE argument
        Assert.False(psi.UseShellExecute)
        Assert.True(psi.CreateNoWindow)
        Assert.Equal(Diagnostics.ProcessWindowStyle.Hidden, psi.WindowStyle)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData(Nothing)>
    Public Sub WithNoResolvableDirectory_NoArgumentIsPassed_AndTheWorkerResolvesForItself(dir As String)
        Assert.Empty(WorkerProcess.SpawnArguments(dir))
        Assert.Empty(WorkerProcess.SpawnStartInfo("C:\app\companion\fms-share-worker.exe", dir).ArgumentList)
    End Sub

    ''' <summary>What the spawn passes is what the Hosting page reports: one resolution, not two that
    ''' happen to agree today.</summary>
    <Fact>
    Public Sub TheSpawnedDirectory_IsTheOneTheHostingPageReports()
        Dim args As List(Of String) = WorkerProcess.SpawnArguments(WorkerStateDir.Resolve())
        Assert.Equal(2, args.Count)
        Assert.Equal("--datadir", args(0))
        Assert.Equal(ServiceControl.ActiveDataDir(), args(1))
    End Sub

    ' --- one resolution, held at the source --------------------------------------------------

    ''' <summary>The three sites the brief names reach the directory only through
    ''' <c>WorkerStateDir</c>; none of them rebuilds a ProgramData / LocalAppData path of its own.</summary>
    <Fact>
    Public Sub NoSiteResolvesTheStateDirectoryOnItsOwn()
        Dim root As String = ExchangeTestSupport.RepoRoot()
        Dim control As String = File.ReadAllText(Path.Combine(root, "src", "FastMediaSorterCompanion", "Core", "ServiceControl.vb"))
        Dim spawn As String = File.ReadAllText(Path.Combine(root, "src", "FastMediaSorterCompanion", "Core", "WorkerProcess.vb"))
        Dim shares As String = File.ReadAllText(Path.Combine(root, "src", "Resources", "WorkerShares.vb"))

        Assert.Contains("WorkerStateDir.Resolve()", control)
        Assert.Contains("WorkerStateDir.Resolve()", spawn)
        Assert.Contains("WorkerStateDir.Resolve()", shares)
        For Each text As String In {control, shares}
            Assert.DoesNotContain("CommonApplicationData", text)
            Assert.DoesNotContain("LocalApplicationData", text)
            Assert.DoesNotContain("""FastMediaSorterCompanion""", text)
            Assert.DoesNotContain("FMS_COMPANION_DATA_DIR", text)   ' the variable's name lives in WorkerStateDir only
        Next
        Assert.DoesNotContain("CommonApplicationData", spawn)
        Assert.DoesNotContain("LocalApplicationData", spawn)
    End Sub

    ''' <summary>The compile lists: the Share Manager links the file, the net48 list names it (whole-file
    ''' <c>#If Not NETFRAMEWORK</c>, like WorkerShares), and the viewer tests link it.</summary>
    <Fact>
    Public Sub TheSharedFile_IsInEveryProjectThatCompilesAConsumer()
        Dim root As String = ExchangeTestSupport.RepoRoot()
        Dim source As String = File.ReadAllText(Path.Combine(root, "src", "Resources", "WorkerStateDir.vb"))
        Assert.StartsWith("#If Not NETFRAMEWORK Then", source)
        Assert.EndsWith("#End If", source.TrimEnd())

        Assert.Contains("..\Resources\WorkerStateDir.vb", File.ReadAllText(Path.Combine(root, "src", "FastMediaSorterCompanion", "FastMediaSorterCompanion.vbproj")))
        Assert.Contains("<Compile Include=""Resources\WorkerStateDir.vb"" />", File.ReadAllText(Path.Combine(root, "src", "FastMediaSorter.vbproj")))
        Assert.Contains("..\..\src\Resources\WorkerStateDir.vb", File.ReadAllText(Path.Combine(root, "tests", "Lite.Tests", "Lite.Tests.vbproj")))
    End Sub

End Class
