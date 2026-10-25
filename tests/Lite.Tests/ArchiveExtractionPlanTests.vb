#If Not NETFRAMEWORK Then
Option Strict On
Imports System.IO
Imports System.Linq
Imports Xunit

Public Class ArchiveExtractionPlanTests
    Private Shared Function Header(name As String, Optional directory As Boolean = False, Optional size As Long = 0) As ArchiveExtractionHeader
        Return New ArchiveExtractionHeader With {.Name = name, .IsDirectory = directory, .Size = size, .CompressedSize = size}
    End Function
    Private Shared Function Plan(ParamArray headers As ArchiveExtractionHeader()) As ArchiveExtractionPlan
        Return ArchiveExtractionPlan.Create(headers, Path.Combine(Path.GetTempPath(), "extract"), 1000000)
    End Function

    <Theory>
    <InlineData("../escape")> <InlineData("a/../../escape")> <InlineData("/rooted")>
    <InlineData("\\server\share")> <InlineData("C:\drive")> <InlineData("a:stream")>
    <InlineData("a//b")> <InlineData("a/./b")> <InlineData("a\\b")>
    <InlineData("a/CON.txt")> <InlineData("a/aux .bin")> <InlineData("a/LPT².bin")>
    <InlineData("a/trim.")> <InlineData("a/trim ")> <InlineData("a/")>
    Public Sub Unsafe_hierarchy_is_refused(name As String)
        Assert.Throws(Of ArchiveSafetyException)(Function() Plan(Header(name)))
    End Sub

    <Fact>
    Public Sub Normalization_aliases_and_file_parent_conflicts_are_refused()
        For Each names In {New String() {"a/b", "A\B"}, New String() {"a", "a/b"}, New String() {"a/b", "a"}}
            Assert.Equal(ArchiveFailure.DuplicateName, Assert.Throws(Of ArchiveSafetyException)(Function() Plan(names.Select(Function(n) Header(n)).ToArray())).Reason)
        Next
        Assert.Throws(Of ArchiveSafetyException)(Function() Plan(Header("a/", True), Header("A")))
    End Sub

    <Fact>
    Public Sub Mixed_ordinary_content_and_empty_directories_keep_the_real_hierarchy()
        Dim result As ArchiveExtractionPlan = Plan(Header("folder/", True), Header("folder/readme.txt", size:=9), Header("folder/empty/", True), Header("folder/zero.exe"))
        Assert.Equal(4, result.Items.Count)
        Assert.Equal(9L, result.TotalBytes)
        Assert.EndsWith(Path.Combine("folder", "readme.txt"), result.Items(1).TargetPath, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub Links_encryption_split_volumes_and_all_limits_have_named_refusals()
        Dim link As ArchiveExtractionHeader = Header("link") : link.IsLink = True
        Assert.Equal(ArchiveFailure.Link, Assert.Throws(Of ArchiveSafetyException)(Function() Plan(link)).Reason)
        link.IsLink = False : link.IsEncrypted = True
        Assert.Equal(ArchiveFailure.Encrypted, Assert.Throws(Of ArchiveSafetyException)(Function() Plan(link)).Reason)
        link.IsEncrypted = False : link.IsSplit = True
        Assert.Equal(ArchiveFailure.MultiVolume, Assert.Throws(Of ArchiveSafetyException)(Function() Plan(link)).Reason)
        Assert.Equal(ArchiveFailure.EntrySizeLimit, Assert.Throws(Of ArchiveSafetyException)(Function() Plan(Header("big", size:=ArchiveSession.Default_Max_Entry_Bytes + 1))).Reason)
        Dim many As IEnumerable(Of ArchiveExtractionHeader) = Enumerable.Range(0, ArchiveExtractionPlan.MaxEntries + 1).Select(Function(i) Header(i.ToString()))
        Assert.Equal(ArchiveFailure.EntryLimit, Assert.Throws(Of ArchiveSafetyException)(Function() ArchiveExtractionPlan.Create(many, Path.GetTempPath(), 1)).Reason)
        Dim total As ArchiveExtractionHeader() = Enumerable.Range(0, 5).Select(Function(i) Header(i.ToString(), size:=ArchiveSession.Default_Max_Entry_Bytes)).ToArray()
        Assert.Equal(ArchiveFailure.TotalSizeLimit, Assert.Throws(Of ArchiveSafetyException)(Function() ArchiveExtractionPlan.Create(total, Path.GetTempPath(), Long.MaxValue)).Reason)
    End Sub

    <Fact>
    Public Sub Zero_compressed_claims_bombs_and_checked_sum_overflow_cannot_bypass_limits()
        Dim entry As ArchiveExtractionHeader = Header("a", size:=1)
        entry.CompressedSize = 0
        Assert.Equal(ArchiveFailure.ExpansionLimit, Assert.Throws(Of ArchiveSafetyException)(Function() Plan(entry)).Reason)
        entry.Size = 201 : entry.CompressedSize = 1
        Assert.Equal(ArchiveFailure.ExpansionLimit, Assert.Throws(Of ArchiveSafetyException)(Function() Plan(entry)).Reason)
        Dim huge As ArchiveExtractionHeader() = {Header("a", size:=Long.MaxValue), Header("b", size:=1)}
        Assert.Equal(ArchiveFailure.TotalSizeLimit, Assert.Throws(Of ArchiveSafetyException)(Function() ArchiveExtractionPlan.Create(huge, Path.GetTempPath(), Long.MaxValue, False, Long.MaxValue, Long.MaxValue)).Reason)
        Dim solid As ArchiveExtractionHeader = Header("solid", size:=201)
        solid.CompressedSize = 0
        Assert.Throws(Of ArchiveSafetyException)(Function() ArchiveExtractionPlan.Create({solid}, Path.GetTempPath(), 1, True))
    End Sub
End Class
#End If
