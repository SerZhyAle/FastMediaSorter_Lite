#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports Xunit

' Specification 276 Step 5.1: the editor's text (ticket 277) as an immutable UTF-8 snapshot in a viewer-owned folder.
Public Class DocHtmlSnapshotTests
    Implements IDisposable

    Private ReadOnly root As String = Path.Combine(Path.GetTempPath(), "fms-snapshot-tests-" & Guid.NewGuid().ToString("N"))

    Public Sub Dispose() Implements IDisposable.Dispose
        If Directory.Exists(root) Then Directory.Delete(root, recursive:=True)
    End Sub

    <Theory>
    <InlineData("C:\Notes\plan.md", 2, "plan.selection.txt")>
    <InlineData("C:\Notes\plan.md", 1, "plan.unsaved.txt")>
    <InlineData("", 1, "text.unsaved.txt")>
    <InlineData("C:\Notes\-version.txt", 2, "text.selection.txt")>
    <InlineData("C:\Notes\Пушкин «Сказки».txt", 1, "Пушкин «Сказки».unsaved.txt")>
    Public Sub FileName_NamesTheDocumentAndTheScope(origin As String, scope As Integer, expected As String)
        Assert.Equal(expected, DocHtmlSnapshot.FileNameFor(origin, CType(scope, DocHtmlSnapshotScope)))
    End Sub

    <Fact>
    Public Sub Write_StoresUtf8WithABomAndItsHash()
        Dim text = "Привет, мир" & vbCrLf & "second line"
        Dim folder = Path.Combine(root, "j1")
        Dim file = DocHtmlSnapshot.Write(folder, "C:\Notes\plan.txt", DocHtmlSnapshotScope.Selection, text)
        Assert.NotNull(file)
        Assert.Equal(Path.Combine(folder, "plan.selection.txt"), file.Path)
        Dim bytes = IO.File.ReadAllBytes(file.Path)
        Assert.Equal(New Byte() {&HEF, &HBB, &HBF}, bytes.Take(3).ToArray())
        Assert.Equal(text, New UTF8Encoding(False).GetString(bytes, 3, bytes.Length - 3))
        Assert.Equal(bytes.LongLength, file.Length)
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), file.Sha256)
    End Sub

    <Fact>
    Public Sub Write_NeverOverwritesAnExistingFile()
        Dim folder = Path.Combine(root, "j2")
        Assert.NotNull(DocHtmlSnapshot.Write(folder, "C:\a.txt", DocHtmlSnapshotScope.Buffer, "one"))
        Assert.Throws(Of IOException)(Function() DocHtmlSnapshot.Write(folder, "C:\a.txt", DocHtmlSnapshotScope.Buffer, "two"))
        Assert.Equal("one", IO.File.ReadAllText(Path.Combine(folder, "a.unsaved.txt")))
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("   " & vbCrLf)>
    Public Sub Write_RefusesEmptyText(text As String)
        Assert.Null(DocHtmlSnapshot.Write(Path.Combine(root, "j3"), "C:\a.txt", DocHtmlSnapshotScope.Buffer, text))
        Assert.False(Directory.Exists(Path.Combine(root, "j3")))
    End Sub

    <Fact>
    Public Sub Write_RefusesARelativeFolderAndAnUnknownScope()
        Assert.Null(DocHtmlSnapshot.Write("snap\j4", "C:\a.txt", DocHtmlSnapshotScope.Buffer, "x"))
        Assert.Null(DocHtmlSnapshot.Write(Path.Combine(root, "j4"), "C:\a.txt", CType(0, DocHtmlSnapshotScope), "x"))
    End Sub

    <Theory>
    <InlineData("C:\Vault\notes.fd-sec", True)>
    <InlineData("C:\Vault\NOTES.FD-SEC", True)>
    <InlineData("C:\Notes\notes.txt", False)>
    <InlineData("", False)>
    Public Sub SecureOrigin_IsRecognised(origin As String, expected As Boolean)
        Assert.Equal(expected, DocHtmlSnapshot.IsSecureOrigin(origin))
    End Sub

    <Fact>
    Public Sub Sweep_RemovesOnlyOldFoldersAndKeepsTheNamedOne()
        Dim oldOne = Directory.CreateDirectory(Path.Combine(root, "old")).FullName
        Dim kept = Directory.CreateDirectory(Path.Combine(root, "kept")).FullName
        Dim fresh = Directory.CreateDirectory(Path.Combine(root, "fresh")).FullName
        Dim now = DateTime.UtcNow
        Directory.SetLastWriteTimeUtc(oldOne, now.AddHours(-30))
        Directory.SetLastWriteTimeUtc(kept, now.AddHours(-30))
        Assert.Equal(1, DocHtmlSnapshot.SweepStale(root, now, kept))
        Assert.False(Directory.Exists(oldOne))
        Assert.True(Directory.Exists(kept))
        Assert.True(Directory.Exists(fresh))
    End Sub

    <Fact>
    Public Sub TryDelete_RemovesTheJobFolder()
        Dim folder = Path.Combine(root, "j5")
        DocHtmlSnapshot.Write(folder, "C:\a.txt", DocHtmlSnapshotScope.Buffer, "x")
        Assert.True(DocHtmlSnapshot.TryDelete(folder))
        Assert.False(Directory.Exists(folder))
        Assert.True(DocHtmlSnapshot.TryDelete(folder))
    End Sub
End Class
#End If
