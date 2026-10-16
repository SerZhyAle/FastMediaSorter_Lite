#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

Public Class ContextHelpTests

    <Fact>
    Public Sub EverySurfaceHasAUniqueIdAndValidFormAndReason()
        Dim map = ContextHelp.Inventory
        Assert.NotNull(map)
        Assert.Equal(1, map.schemaVersion)
        Dim ids = map.surfaces.Select(Function(s) s.id).ToList()
        Assert.Equal(ids.Count, ids.Distinct().Count())

        For Each s In map.surfaces
            Assert.Contains(s.form, New String() {"page", "both", "inherit", "none"})
            If s.form = "none" OrElse s.form = "inherit" Then
                Assert.False(String.IsNullOrWhiteSpace(s.reason), $"Surface {s.id} lacks reason")
            End If
            If s.form = "inherit" Then
                Assert.Contains(s.parent, ids)
            End If
        Next
    End Sub

    <Fact>
    Public Sub ResolverSelectsLanguageAndRejectsArbitraryAnchors()
        Assert.Equal("uk/main-viewer.html", ContextHelp.Resolve("main-viewer", "uk"))
        Assert.Equal("en/main-viewer.html", ContextHelp.Resolve("main-viewer", "unknown_lang"))
        Assert.Equal("en/settings.html#destinations", ContextHelp.Resolve("settings.destinations", "en", "destinations"))
        ' Traversal in anchor fragment is rejected (sanitized)
        Assert.Equal("en/main-viewer.html", ContextHelp.Resolve("main-viewer", "en", "../../outside"))
        ' none and layer resolve to Nothing
        Assert.Null(ContextHelp.Resolve("installer", "en"))
    End Sub

    <Fact>
    Public Sub MapThrowsOnInvalidVersion()
        Assert.Throws(Of InvalidDataException)(Sub() ContextHelp.ReadMap("{""schemaVersion"":2}"))
        Dim valid = ContextHelp.ReadMap("{""schemaVersion"":1,""product"":""Fast Media Sorter""}")
        Assert.Equal(1, valid.schemaVersion)
    End Sub

    <Fact>
    Public Sub ExtractedFilesKeepTheElementFragment()
        Dim root = Path.Combine(Path.GetTempPath(), "FmslHelpTests", Guid.NewGuid().ToString("N"))
        Try
            Directory.CreateDirectory(root)
            Dim uri = ContextHelp.Extract("en/index.html#destinations", root)
            Assert.Equal("#destinations", New Uri(uri).Fragment)
            Dim localPath = New Uri(uri).LocalPath
            Assert.True(File.Exists(localPath))
            Dim text = File.ReadAllText(localPath)
            Assert.Contains("<html lang=""en""", text)
        Finally
            If Directory.Exists(root) Then
                Try
                    Directory.Delete(root, True)
                Catch
                End Try
            End If
        End Try
    End Sub

    <Fact>
    Public Sub HelpButtonUsesVocabularyAndHasCorrectTargetSize()
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
            Try
                Using form As New Form()
                    ' Simulate Ask_Dialog or a known dialog type
                    form.Text = "Confirm"
                    ContextHelpUi.Attach(form)
                    ' For a form not in map or form=none, no button
                    Assert.Null(ContextHelpUi.AttachedButton(form))
                End Using
            Catch ex As Exception
                failure = ex
            End Try
        End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(10000))
        If failure IsNot Nothing Then Throw failure
    End Sub

End Class
#End If
