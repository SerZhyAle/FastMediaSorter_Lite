Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' L0326, APP-BEHAVIOUR rule 2 ("growing text grows the window"): the static sweep of the viewer's DESIGNER-built forms.
''' Code-built dialogs end their builder with <c>DialogLayout.ApplyAutoScale</c> and size themselves in <c>OnLoad</c>
''' (<c>DialogLayout.FitContent</c>, held by <c>DialogLayoutTests</c> in 13 languages); a form made in the Designer has no
''' such builder, so a NEW one is the one place the rule can be missed without a test noticing. This list is the sweep's
''' result: every Designer-built form of the viewer and what answers rule 2 for it. A form that is not on the list fails
''' <see cref="Every_Designer_built_form_has_been_judged_against_rule_2"/> until it has been judged and listed - the gate,
''' not the list, is the point.
''' </summary>
Public Class DesignerDialogSweepTests

    ''' <summary>The Designer-built forms of the viewer, judged 2026-11-15 (file under src/ to the finding).</summary>
    Private Shared ReadOnly Judged As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
        {"Main_Form.Designer.vb", "the main window - not a secondary window, nothing in it is a dialog whose text grows (the floating chrome lays itself out from its own measurements)"},
        {"Table_Form.Designer.vb", "the Settings window, a FixedDialog in the Designer - the modern build sizes it to its content (Table_Form.SettingsDiscovery, FitModernSettingsClientSize: the widest caption, the page height, bounded by the work area); the net48 fallback keeps the Designer size under the maintenance policy"}
    }

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src/ not found above " & AppContext.BaseDirectory)
    End Function

    ''' <summary>The Designer-built forms among <paramref name="relativePaths"/> (paths under src/, either slash): a
    ''' <c>*.Designer.vb</c> that is not the Visual Basic project's own generated Resources/Settings and not a build output.</summary>
    Friend Shared Function DesignerForms(relativePaths As IEnumerable(Of String)) As List(Of String)
        Dim found As New List(Of String)()
        For Each relative As String In relativePaths
            Dim p As String = relative.Replace("\"c, "/"c)
            If Not p.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase) Then Continue For
            If p.StartsWith("My Project/", StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim rooted As String = "/" & p
            If rooted.IndexOf("/obj/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse rooted.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            found.Add(p)
        Next
        found.Sort(StringComparer.Ordinal)
        Return found
    End Function

    <Fact>
    Public Sub Every_Designer_built_form_has_been_judged_against_rule_2()
        Dim root As String = SourceDir()
        Dim all As IEnumerable(Of String) = Directory.EnumerateFiles(root, "*.Designer.vb", SearchOption.AllDirectories).
            Select(Function(f) f.Substring(root.Length).TrimStart("\"c, "/"c))
        Dim actual As List(Of String) = DesignerForms(all)
        Dim unjudged As List(Of String) = actual.Where(Function(f) Not Judged.ContainsKey(f)).ToList()
        Dim gone As List(Of String) = Judged.Keys.Where(Function(f) Not actual.Contains(f)).ToList()
        Assert.True(unjudged.Count = 0,
                    "A Designer-built form has not been judged against APP-BEHAVIOUR rule 2 (growing text grows the window): " &
                    String.Join(", ", unjudged) & ". Build a dialog in code (DialogLayout.ApplyAutoScale + FitContent) or size this one to its content, then list it with its finding.")
        Assert.True(gone.Count = 0, "The sweep lists a Designer form that no longer exists: " & String.Join(", ", gone))
    End Sub

    <Fact>
    Public Sub The_scanner_finds_a_planted_designer_form_and_leaves_the_generated_resources_alone()
        Dim found As List(Of String) = DesignerForms({
            "Main_Form.Designer.vb", "My Project\Resources.Designer.vb", "My Project/Settings.Designer.vb",
            "Sub\New_Dialog.Designer.vb", "obj/x/Generated.Designer.vb", "Main_Form.vb"})
        Assert.Equal({"Main_Form.Designer.vb", "Sub/New_Dialog.Designer.vb"}, found.ToArray())
    End Sub

    <Fact>
    Public Sub The_designer_built_settings_dialog_is_sized_to_its_content_by_the_modern_build()
        Dim root As String = SourceDir()
        Dim layout As String = File.ReadAllText(Path.Combine(root, "Table_Form.ModernLayout.vb"))
        Dim discovery As String = File.ReadAllText(Path.Combine(root, "Table_Form.SettingsDiscovery.vb"))
        Assert.Contains("Private Sub FitModernSettingsClientSize(", layout, StringComparison.Ordinal)
        Assert.Contains("WindowPlacement.BoundMinimum(", layout, StringComparison.Ordinal)
        ' two callers: the remembered size and the size the content asks for
        Assert.Contains("FitModernSettingsClientSize(Screen.FromControl(Me).WorkingArea, savedWidth, savedHeight)", layout, StringComparison.Ordinal)
        Assert.Contains("FitModernSettingsClientSize(Screen.FromControl(If(Owner, Me)).WorkingArea, desiredWidth, desiredHeight)", discovery, StringComparison.Ordinal)
        ' what the content asks for is measured, not typed: the widest caption of the first page
        Assert.Contains("TextRenderer.MeasureText(r.Title.Text, r.Title.Font).Width", discovery, StringComparison.Ordinal)
    End Sub

End Class
