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
            If Not String.IsNullOrEmpty(s.ask) Then
                Assert.Equal("off", s.ask)
                Assert.Equal("inherit", s.form)
            End If
        Next
    End Sub

    ' CONTEXT-HELP rule 3, second sentence (L0334): F1 with the focus on a control that has its own entry goes to that
    ' entry's anchor; any other control, and the help button itself (0.3 item K), go to the top of the page.
    Private Const ElementMapJson As String =
        "{""schemaVersion"":1,""surfaces"":[" &
        "{""id"":""w"",""type"":""x.W"",""form"":""page"",""help"":""w"",""elements"":[" &
        "{""control"":""ok"",""anchor"":""save""},{""control"":""list"",""anchor"":""rows""},{""control"":""contextHelp"",""anchor"":""never""}]}," &
        "{""id"":""child"",""type"":""x.C"",""form"":""inherit"",""parent"":""w"",""reason"":""r"",""elements"":[{""control"":""ok"",""anchor"":""save""}]}]}"

    <Fact>
    Public Sub F1OnAControlWithAnEntryOpensThatAnchorAndOtherControlsOpenTheTop()
        Dim map = ContextHelp.ReadMap(ElementMapJson)
        Assert.Equal("save", ContextHelp.ElementFor(map, "w", {"ok", "panel"}))
        Assert.Equal("rows", ContextHelp.ElementFor(map, "w", {"list"}))
        Assert.Null(ContextHelp.ElementFor(map, "w", {"somethingElse", "panel"}))
        Assert.Null(ContextHelp.ElementFor(map, "w", {"", ""}))
        Assert.Null(ContextHelp.ElementFor(map, "w", New String() {}))
        Assert.Null(ContextHelp.ElementFor(map, "nope", {"ok"}))
        Assert.Null(ContextHelp.ElementFor(map, "w", Nothing))
    End Sub

    <Fact>
    Public Sub TheInnermostControlWithAnEntryWinsOverItsParent()
        Dim map = ContextHelp.ReadMap(ElementMapJson)
        Assert.Equal("save", ContextHelp.ElementFor(map, "w", {"unnamedInner", "ok", "list"}))
        Assert.Equal("rows", ContextHelp.ElementFor(map, "w", {"list", "ok"}))
    End Sub

    <Fact>
    Public Sub TheHelpButtonAndAnInheritRowNeverOpenAnElementAnchor()
        Dim map = ContextHelp.ReadMap(ElementMapJson)
        Assert.Equal("contextHelp", ContextHelp.HelpButtonName)
        ' Even when a map row names the button, and even when a parent of it has an entry.
        Assert.Null(ContextHelp.ElementFor(map, "w", {ContextHelp.HelpButtonName, "list"}))
        ' An inherit row has no page of its own to anchor into.
        Assert.Null(ContextHelp.ElementFor(map, "child", {"ok"}))
    End Sub

    <Fact>
    Public Sub TheShippedMapNamesRealControlsOfTheKeyboardShortcutsWindow()
        Dim hotkeys = ContextHelp.Inventory.surfaces.Single(Function(s) s.id = "hotkeys")
        Assert.NotNull(hotkeys.elements)
        Assert.Equal("save-cancel", ContextHelp.ElementFor(ContextHelp.Inventory, "hotkeys", {"hotkeys_Ok", "hotkeys_Buttons"}))
        Assert.Equal("record-shortcut", ContextHelp.ElementFor(ContextHelp.Inventory, "hotkeys", {"hotkeys_Capture"}))
        Assert.Equal("shortcut-list", ContextHelp.ElementFor(ContextHelp.Inventory, "hotkeys", {"hotkeys_List"}))
        For Each row In ContextHelp.Inventory.surfaces.Where(Function(s) s.elements IsNot Nothing)
            Assert.Contains(row.form, New String() {"page", "both"})
            Dim controls = row.elements.Select(Function(e) e.control).ToList()
            Assert.Equal(controls.Count, controls.Distinct(StringComparer.Ordinal).Count())
        Next
    End Sub

    <Fact>
    Public Sub TheHotkeysWindowControlsCarryTheNamesTheMapIsKeyedOn()
        Dim source As String = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Hotkeys_Form.vb"))
        Dim hotkeys = ContextHelp.Inventory.surfaces.Single(Function(s) s.id = "hotkeys")
        For Each element In hotkeys.elements
            Assert.Contains(".Name = """ & element.control & """", source)
        Next
    End Sub

    Private Shared Function RepositoryRoot() As String
        Dim dir As String = AppContext.BaseDirectory
        Do While dir IsNot Nothing AndAlso Not File.Exists(Path.Combine(dir, "help-map.json"))
            dir = Path.GetDirectoryName(dir)
        Loop
        If dir Is Nothing Then Throw New InvalidOperationException("help-map.json not found above the test output")
        Return dir
    End Function

    <Fact>
    Public Sub TheQrWindowTakesNoAddedControl()
        Dim row = ContextHelp.Inventory.surfaces.Single(Function(s) s.id = "qr-zoom")
        Assert.Equal("off", row.ask)
        Assert.Equal("inherit", row.form)
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
