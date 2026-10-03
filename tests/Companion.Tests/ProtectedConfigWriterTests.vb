Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports System.Text
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' 108 H03 (057.S22-5): the exported .fmscfg - a key - is created owner-only before any secret byte
''' exists, published by rename (so a permissive existing target cannot hand its descriptor to the new
''' bytes), verified, and never reported as saved otherwise. The real tests run on this machine's NTFS;
''' every failure of the publication is produced by an injected file-operations fake.
''' </summary>
Public Class ProtectedConfigWriterTests

    Private Shared Function NewDir() As String
        Return PrivateTestFolders.Create("fms-cfg-")
    End Function

    Private Shared Sub Remove(dir As String)
        Try
            ' Tests deliberately loosen ACLs; take the folder back before deleting.
            Dim di As New DirectoryInfo(dir)
            Dim security As DirectorySecurity = di.GetAccessControl()
            security.SetAccessRuleProtection(False, False)
            di.SetAccessControl(security)
        Catch
        End Try
        Try : Directory.Delete(dir, True) : Catch : End Try
    End Sub

    Private Shared Function Me_() As SecurityIdentifier
        Return WindowsIdentity.GetCurrent().User
    End Function

    Private Shared Function IsOwnerOnly(path As String) As Boolean
        Return RealConfigFileOps.Instance.IsOwnerOnly(path, Me_())
    End Function

    Private Const Secret As String = "{""password"":""pa$$word"",""roots"":[{""virtualPath"":""/Фото"",""accessPin"":""4711""}]}"

    ' ----------------------------------------------------------------- real filesystem ----

    <Fact>
    Public Sub A_new_file_in_an_ordinary_folder_is_owner_only_with_exactly_the_bytes_given()
        Dim dir As String = NewDir()
        Try
            Dim target As String = Path.Combine(dir, "FastMediaSorter.fmscfg")
            Assert.Equal(ConfigWriteOutcome.Saved, ProtectedConfigWriter.Publish(target, Secret))
            Assert.True(IsOwnerOnly(target))
            Assert.Equal(New UTF8Encoding(False).GetBytes(Secret), File.ReadAllBytes(target)) ' UTF-8, no BOM, no change
            Assert.Equal({target}, Directory.GetFiles(dir)) ' no temp left behind
        Finally
            Remove(dir)
        End Try
    End Sub

    <Fact>
    Public Sub Overwriting_a_permissive_existing_file_leaves_the_target_owner_only()
        Dim dir As String = NewDir()
        Try
            Dim target As String = Path.Combine(dir, "FastMediaSorter.fmscfg")
            File.WriteAllText(target, "old key")
            ' Make the existing target permissive: everyone may read it, and it inherits.
            Dim fi As New FileInfo(target)
            Dim security As FileSecurity = fi.GetAccessControl()
            security.AddAccessRule(New FileSystemAccessRule(New SecurityIdentifier(WellKnownSidType.WorldSid, Nothing), FileSystemRights.Read, AccessControlType.Allow))
            fi.SetAccessControl(security)
            Assert.False(IsOwnerOnly(target))

            Assert.Equal(ConfigWriteOutcome.Saved, ProtectedConfigWriter.Publish(target, Secret))

            ' The descriptor that now guards the new bytes is the temp's, not the old file's.
            Assert.True(IsOwnerOnly(target))
            Assert.Equal(Secret, File.ReadAllText(target))
            Assert.Equal({target}, Directory.GetFiles(dir))
        Finally
            Remove(dir)
        End Try
    End Sub

    <Fact>
    Public Sub A_locked_target_fails_without_touching_it_and_leaves_no_temp()
        Dim dir As String = NewDir()
        Try
            Dim target As String = Path.Combine(dir, "FastMediaSorter.fmscfg")
            File.WriteAllText(target, "old key")
            Using held As New FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None)
                Assert.Equal(ConfigWriteOutcome.Failed, ProtectedConfigWriter.Publish(target, Secret))
            End Using
            Assert.Equal("old key", File.ReadAllText(target))
            Assert.Equal({target}, Directory.GetFiles(dir))
        Finally
            Remove(dir)
        End Try
    End Sub

    <Fact>
    Public Sub A_folder_that_others_can_delete_from_is_refused_and_nothing_is_written()
        Dim dir As String = NewDir()
        Try
            Dim di As New DirectoryInfo(dir)
            Dim security As DirectorySecurity = di.GetAccessControl()
            security.AddAccessRule(New FileSystemAccessRule(New SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, Nothing),
                                                            FileSystemRights.Modify, InheritanceFlags.ContainerInherit Or InheritanceFlags.ObjectInherit,
                                                            PropagationFlags.None, AccessControlType.Allow))
            di.SetAccessControl(security)

            Dim target As String = Path.Combine(dir, "FastMediaSorter.fmscfg")
            Assert.Equal(ConfigWriteOutcome.RefusedUnsafeTarget, ProtectedConfigWriter.Publish(target, Secret))
            Assert.Empty(Directory.GetFiles(dir))
        Finally
            Remove(dir)
        End Try
    End Sub

    <Fact>
    Public Sub A_junction_destination_is_refused()
        Dim dir As String = NewDir()
        Dim real As String = NewDir()
        Try
            Dim link As String = Path.Combine(dir, "link")
            Dim info As New ProcessStartInfo("cmd.exe", "/c mklink /J """ & link & """ """ & real & """") With {.UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True}
            Using p As Process = Process.Start(info)
                p.WaitForExit(10000)
            End Using
            If Not Directory.Exists(link) Then Return ' this account cannot make a junction here; the fake-ops test below covers the branch
            Assert.Equal(ConfigWriteOutcome.RefusedUnsafeTarget, ProtectedConfigWriter.Publish(Path.Combine(link, "x.fmscfg"), Secret))
            Assert.Empty(Directory.GetFiles(real))
        Finally
            Try : Directory.Delete(Path.Combine(dir, "link")) : Catch : End Try
            Remove(dir)
            Remove(real)
        End Try
    End Sub

    ' ------------------------------------------------------------------- injected faults ----

    Private NotInheritable Class FakeOps
        Implements IConfigFileOps

        Friend ReadOnly Files As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
        Friend ReadOnly Calls As New List(Of String)()
        Friend Supports As Boolean = True
        Friend OthersReplace As Boolean
        Friend Redirected As Boolean
        Friend FailCreate As Exception
        Friend FailMove As Boolean
        Friend FailWrite As Boolean
        Friend TempOwnerOnly As Boolean = True
        Friend FinalOwnerOnly As Boolean = True
        Friend CorruptFinal As Boolean

        Public Function CreateProtected(path As String, ownerSid As SecurityIdentifier) As Stream Implements IConfigFileOps.CreateProtected
            Calls.Add("create")
            If FailCreate IsNot Nothing Then Throw FailCreate
            Files(path) = New Byte() {}
            Return New CapturingStream(Me, path, FailWrite)
        End Function

        Public Sub Move(source As String, destination As String, overwrite As Boolean) Implements IConfigFileOps.Move
            Calls.Add("move")
            If FailMove Then Throw New IOException("locked")
            Dim bytes As Byte() = Files(source)
            If CorruptFinal Then bytes = New Byte() {1, 2, 3}
            Files.Remove(source)
            Files(destination) = bytes
        End Sub

        Public Sub Delete(path As String) Implements IConfigFileOps.Delete
            Calls.Add("delete:" & IO.Path.GetFileName(path))
            Files.Remove(path)
        End Sub

        Public Function Exists(path As String) As Boolean Implements IConfigFileOps.Exists
            Return Files.ContainsKey(path)
        End Function

        Public Function IsOwnerOnly(path As String, ownerSid As SecurityIdentifier) As Boolean Implements IConfigFileOps.IsOwnerOnly
            Return If(path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase), TempOwnerOnly, FinalOwnerOnly)
        End Function

        Public Function SupportsOwnerOnly(directory As String) As Boolean Implements IConfigFileOps.SupportsOwnerOnly
            Return Supports
        End Function

        Public Function OthersCanReplaceIn(directory As String, ownerSid As SecurityIdentifier) As Boolean Implements IConfigFileOps.OthersCanReplaceIn
            Return OthersReplace
        End Function

        Public Function IsRedirected(path As String) As Boolean Implements IConfigFileOps.IsRedirected
            Return Redirected
        End Function

        Public Function ReadAllBytes(path As String) As Byte() Implements IConfigFileOps.ReadAllBytes
            Return Files(path)
        End Function
    End Class

    Private NotInheritable Class CapturingStream
        Inherits MemoryStream

        Private ReadOnly owner As FakeOps
        Private ReadOnly path As String
        Private ReadOnly failWrite As Boolean

        Friend Sub New(owner As FakeOps, path As String, failWrite As Boolean)
            Me.owner = owner
            Me.path = path
            Me.failWrite = failWrite
        End Sub

        Public Overrides Sub Write(buffer() As Byte, offset As Integer, count As Integer)
            If failWrite Then Throw New IOException("disk full")
            MyBase.Write(buffer, offset, count)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing AndAlso owner.Files.ContainsKey(path) Then owner.Files(path) = ToArray()
            MyBase.Dispose(disposing)
        End Sub
    End Class

    Private Shared ReadOnly Target As String = "X:\share\FastMediaSorter.fmscfg"

    <Fact>
    Public Sub FAT_or_any_system_without_enforceable_descriptors_is_refused_before_a_byte_is_written()
        Dim ops As New FakeOps() With {.Supports = False}
        Assert.Equal(ConfigWriteOutcome.RefusedNoAcls, ProtectedConfigWriter.Publish(Target, Secret, ops))
        Assert.DoesNotContain("create", ops.Calls)
    End Sub

    <Fact>
    Public Sub Unsafe_folders_and_redirected_paths_are_refused_before_a_byte_is_written()
        Dim shared_ As New FakeOps() With {.OthersReplace = True}
        Assert.Equal(ConfigWriteOutcome.RefusedUnsafeTarget, ProtectedConfigWriter.Publish(Target, Secret, shared_))
        Assert.DoesNotContain("create", shared_.Calls)

        Dim link As New FakeOps() With {.Redirected = True}
        Assert.Equal(ConfigWriteOutcome.RefusedUnsafeTarget, ProtectedConfigWriter.Publish(Target, Secret, link))
        Assert.DoesNotContain("create", link.Calls)
    End Sub

    <Fact>
    Public Sub A_failed_create_changes_nothing_and_deletes_nothing()
        Dim ops As New FakeOps() With {.FailCreate = New UnauthorizedAccessException("denied")}
        ops.Files(Target) = Encoding.UTF8.GetBytes("old")
        Assert.Equal(ConfigWriteOutcome.Failed, ProtectedConfigWriter.Publish(Target, Secret, ops))
        Assert.Equal("old", Encoding.UTF8.GetString(ops.Files(Target)))
        Assert.DoesNotContain(ops.Calls, Function(c) c.StartsWith("delete"))
    End Sub

    <Fact>
    Public Sub A_platform_that_cannot_hold_the_descriptor_at_create_is_a_refusal()
        Dim ops As New FakeOps() With {.FailCreate = New PlatformNotSupportedException("no ACLs")}
        Assert.Equal(ConfigWriteOutcome.RefusedNoAcls, ProtectedConfigWriter.Publish(Target, Secret, ops))
    End Sub

    <Fact>
    Public Sub An_interrupted_write_removes_only_its_own_temp_and_keeps_the_old_target()
        Dim ops As New FakeOps() With {.FailWrite = True}
        ops.Files(Target) = Encoding.UTF8.GetBytes("old")
        Assert.Equal(ConfigWriteOutcome.Failed, ProtectedConfigWriter.Publish(Target, Secret, ops))
        Assert.Equal("old", Encoding.UTF8.GetString(ops.Files(Target)))
        Assert.Single(ops.Files) ' the temp is gone
    End Sub

    <Fact>
    Public Sub A_temp_without_the_owner_only_descriptor_is_never_published()
        Dim ops As New FakeOps() With {.TempOwnerOnly = False}
        ops.Files(Target) = Encoding.UTF8.GetBytes("old")
        Assert.Equal(ConfigWriteOutcome.Failed, ProtectedConfigWriter.Publish(Target, Secret, ops))
        Assert.DoesNotContain("move", ops.Calls)
        Assert.Equal("old", Encoding.UTF8.GetString(ops.Files(Target)))
        Assert.Single(ops.Files)
    End Sub

    <Fact>
    Public Sub A_failed_rename_keeps_the_old_target_and_removes_the_temp()
        Dim ops As New FakeOps() With {.FailMove = True}
        ops.Files(Target) = Encoding.UTF8.GetBytes("old")
        Assert.Equal(ConfigWriteOutcome.Failed, ProtectedConfigWriter.Publish(Target, Secret, ops))
        Assert.Equal("old", Encoding.UTF8.GetString(ops.Files(Target)))
        Assert.Single(ops.Files)
    End Sub

    <Fact>
    Public Sub A_published_file_that_fails_the_final_verification_is_withdrawn_and_not_reported_saved()
        Dim wrongDescriptor As New FakeOps() With {.FinalOwnerOnly = False}
        Assert.Equal(ConfigWriteOutcome.Failed, ProtectedConfigWriter.Publish(Target, Secret, wrongDescriptor))
        Assert.False(wrongDescriptor.Files.ContainsKey(Target)) ' it holds a secret under the wrong guard: gone

        Dim wrongBytes As New FakeOps() With {.CorruptFinal = True}
        Assert.Equal(ConfigWriteOutcome.Failed, ProtectedConfigWriter.Publish(Target, Secret, wrongBytes))
        Assert.False(wrongBytes.Files.ContainsKey(Target))
    End Sub

    <Fact>
    Public Sub The_happy_path_creates_writes_moves_in_that_order_and_leaves_one_file()
        Dim ops As New FakeOps()
        Assert.Equal(ConfigWriteOutcome.Saved, ProtectedConfigWriter.Publish(Target, Secret, ops))
        Assert.Equal({"create", "move"}, ops.Calls.ToArray())
        Assert.Equal(Secret, Encoding.UTF8.GetString(ops.Files(Target)))
        Assert.Single(ops.Files)
    End Sub

    <Fact>
    Public Sub The_exception_form_carries_the_outcome_so_no_caller_can_report_saved()
        Dim missing As String = Path.Combine(Path.GetTempPath(), "fms-no-dir-" & Guid.NewGuid().ToString("N"), "x.fmscfg")
        Dim ex As ConfigWriteRefusedException = Assert.Throws(Of ConfigWriteRefusedException)(Sub() ShareConfigBuilder.WriteConfigFile(missing, Secret))
        Assert.NotEqual(ConfigWriteOutcome.Saved, ex.Outcome)
    End Sub

End Class
