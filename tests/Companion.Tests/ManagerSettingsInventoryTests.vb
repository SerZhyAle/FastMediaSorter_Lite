Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' APP-SETTINGS rule 11 for the Share Manager (ticket L0333): the Manager keeps ONE list of its settings - page, group,
''' caption, editor and default - and that list is generated from the windows themselves, never typed. The two settings
''' windows (Manager and the local exchange server) are built the way the program builds them, on an STA thread, over a
''' scratch settings hive with the exchange component and the SFTP worker faked, and the rows the settings search offers
''' (<c>Share_Settings_Form.SettingsSearchEntries</c>) are read in display order. The default of a row is the value its
''' editor holds when the window opens on that clean hive. The text is compared with
''' <c>docs/guides/SHARE_MANAGER_SETTINGS_INVENTORY.md</c>: a setting added, renamed, moved to another page or given
''' another default without regenerating the document fails here.
''' Regenerate with <c>tools/Export-ManagerSettingsInventory.ps1</c> (it sets <see cref="WriteVariable"/> and runs this fact);
''' <c>-Check</c> runs the comparison alone. The fact is tagged <c>Kind=UiForm</c>, <c>Form=Share_Settings_Form</c>: it runs
''' with <c>tools/Test-UiForms.ps1 -Forms Share_Settings_Form</c> whenever that form changes, which is the only time a row
''' can change, and never in the release run (owner decision 2026-11-13).
''' The window is only constructed, never shown: no desktop is needed and nothing is painted.
''' </summary>
<Collection(ScheduledCollection.Name), Trait("Kind", "UiForm"), Trait("Form", "Share_Settings_Form")>
Public Class ManagerSettingsInventoryTests

    ''' <summary>Set to "1" by the generator script: the fact writes the document instead of comparing with it.</summary>
    Friend Const WriteVariable As String = "FMS_SETTINGS_INVENTORY_WRITE"

    Private Const DocumentRelative As String = "docs/guides/SHARE_MANAGER_SETTINGS_INVENTORY.md"
    Private Const NotGeneratedMarker As String = "<!-- inventory:not-generated -->"

    Private Const Flags As BindingFlags = BindingFlags.Instance Or BindingFlags.Public Or BindingFlags.NonPublic

    ''' <summary>The settings windows of the shipped Share Manager, in the order the document lists them. The remote
    ''' administration window is a second, shut gate (ExchangeRemoteAdminFeatures.Offered) and is not a settings window of
    ''' the shipped program.</summary>
    Private Shared ReadOnly Destinations As SettingsDestination() = {SettingsDestination.Manager, SettingsDestination.ExchangeServer}

    ''' <summary>Editors whose value is read from the machine, not from the settings hive: their default is the state of the
    ''' computer the window opens on, so the document names the source instead of a value that would differ per machine.</summary>
    Private Shared ReadOnly MachineStateEditors As String() = {"chkAutostart"}

    <Fact>
    Public Sub Inventory_document_matches_the_built_windows()
        Dim text As String = Nothing
        OnSta(Sub() text = BuildDocument())
        Assert.False(String.IsNullOrEmpty(text), "the inventory came out empty")

        Dim target As String = Path.Combine(ExchangeTestSupport.RepoRoot(), DocumentRelative.Replace("/"c, Path.DirectorySeparatorChar))
        If Environment.GetEnvironmentVariable(WriteVariable) = "1" Then
            File.WriteAllText(target, ToCrLf(text), New UTF8Encoding(False))
            Return
        End If

        Assert.True(File.Exists(target), DocumentRelative & " is missing: run tools/Export-ManagerSettingsInventory.ps1")
        Dim committed As String = File.ReadAllText(target, Encoding.UTF8)
        Assert.False(committed.Contains(NotGeneratedMarker),
                     DocumentRelative & " has not been generated yet: run tools/Export-ManagerSettingsInventory.ps1 once and keep the result")
        Dim expected As String = ToLf(text)
        Dim actual As String = ToLf(committed)
        Assert.True(String.Equals(expected, actual, StringComparison.Ordinal),
                    DocumentRelative & " differs from the built windows (" & FirstDifference(expected, actual) &
                    "): regenerate it with tools/Export-ManagerSettingsInventory.ps1")
    End Sub

    ' --- harness ---------------------------------------------------------------------------------------

    Private Shared Sub OnSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(280000), "the inventory build did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    ''' <summary>Builds every destination in English over a scratch hive, the exchange component and the worker faked (nothing
    ''' is started, nothing is asked of a live service), and puts every seam back afterwards.</summary>
    Private Shared Function BuildDocument() As String
        Dim saved As String = Localization.CurrentCode
        ExchangeTestSupport.Reset()
        Dim component As String = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        Dim worker As String = ExchangeTestSupport.MakeFakeWorker()
        Try
            Localization.CurrentCode = "en"
            ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
            ExchangeProcess.ExeDirectoryOverride = component
            ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
            ' A server running on this computer must not answer the exchange window's administration pipe.
            ExchangeAdmin.PipeNameOverride = "fms-inventory-no-server-" & Guid.NewGuid().ToString("N")
            Using hive As New ScratchHive()
                Dim blocks As New List(Of String)()
                For Each destination As SettingsDestination In Destinations
                    blocks.Add(RenderDestination(destination))
                Next
                Return Compose(blocks)
            End Using
        Finally
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(component)
            ExchangeTestSupport.DeleteQuietly(worker)
            Localization.CurrentCode = saved
        End Try
    End Function

    ''' <summary>The table of one window: one line per row the settings search offers, in display order.</summary>
    Private Shared Function RenderDestination(destination As SettingsDestination) As String
        Dim method As MethodInfo = GetType(Share_Settings_Form).GetMethod("SettingsSearchEntries", Flags)
        Assert.True(method IsNot Nothing, "Share_Settings_Form.SettingsSearchEntries is gone: the inventory reads the rows the settings search reads")
        Dim lines As New List(Of String)()
        Dim seen As New HashSet(Of Control)()
        Using form As New Share_Settings_Form(destination:=destination)
            Dim entries As System.Collections.IEnumerable = DirectCast(method.Invoke(form, Nothing), System.Collections.IEnumerable)
            For Each entry As Object In entries
                Dim editor As Control = DirectCast(Field(entry, "Editor"), Control)
                ' A setting row whose editor is a check box is met twice by the search walk: once as the row, once as the box.
                If editor Is Nothing OrElse Not seen.Add(editor) Then Continue For
                Dim group As Share_Settings_Form.SettingsNavGroup = DirectCast(Field(entry, "Group"), Share_Settings_Form.SettingsNavGroup)
                Dim title As String = CStr(Field(entry, "Title"))
                lines.Add("| " & Cell(CStr(Field(entry, "PageTitle"))) & " | " & Cell(If(group Is Nothing, "", group.Caption)) &
                          " | " & Cell(If(String.IsNullOrWhiteSpace(title), "(no caption)", title)) &
                          " | " & EditorKind(editor) & " | " & Cell(DefaultOf(editor)) & " |")
            Next
        End Using
        Return String.Join(vbLf, lines)
    End Function

    Private Shared Function Field(entry As Object, name As String) As Object
        Dim info As FieldInfo = entry.GetType().GetField(name, Flags)
        Assert.True(info IsNot Nothing, "the settings search entry has no field " & name)
        Return info.GetValue(entry)
    End Function

    Private Shared Function Compose(blocks As IList(Of String)) As String
        Dim builder As New StringBuilder()
        builder.Append("# Share Manager settings inventory").Append(vbLf).Append(vbLf)
        builder.Append("This document is generated from the built settings windows by `tools/Export-ManagerSettingsInventory.ps1`; ").
            Append("do not edit it by hand. It holds no profile value.").Append(vbLf).Append(vbLf)
        builder.Append("Every row is a setting the Share Manager settings search finds: its page, its group in the left menu, ").
            Append("its caption as written in the English interface, the kind of editor, and the value the editor holds when ").
            Append("the window opens on a clean profile. Commands show `none (command)`, a field the program fills in from the ").
            Append("computer shows `machine state`, and a secret field shows `masked`. A change to a page, a group, a caption ").
            Append("or a default of any setting moves a row here, and the check that compares this file with the windows ").
            Append("fails until it is regenerated. The window layout is described in ").
            Append("[Sharing settings windows](SHARING_SETTINGS_WINDOWS.md).").Append(vbLf).Append(vbLf)
        Dim titles As String() = {"Manager settings window", "Local exchange server window"}
        For index As Integer = 0 To blocks.Count - 1
            builder.Append("## ").Append(titles(index)).Append(vbLf).Append(vbLf)
            builder.Append("| Page | Group | Caption | Editor | Default |").Append(vbLf)
            builder.Append("| --- | --- | --- | --- | --- |").Append(vbLf)
            builder.Append(blocks(index)).Append(vbLf).Append(vbLf)
        Next
        Return builder.ToString().TrimEnd() & vbLf
    End Function

    ' --- one row ----------------------------------------------------------------------------------------

    Private Shared Function EditorKind(editor As Control) As String
        If TypeOf editor Is CheckBox Then Return "checkbox"
        If TypeOf editor Is RadioButton Then Return "radio"
        If TypeOf editor Is ComboBox Then Return "list"
        If TypeOf editor Is NumericUpDown Then Return "number"
        If TypeOf editor Is TextBox Then Return "text"
        If TypeOf editor Is Button Then Return "command"
        If TypeOf editor Is LinkLabel Then Return "link"
        If TypeOf editor Is Label Then Return "information"
        If TypeOf editor Is FlowLayoutPanel Then Return "commands"
        Return editor.GetType().Name
    End Function

    ''' <summary>The value the editor holds on a clean profile. A secret is never read into the document.</summary>
    Private Shared Function DefaultOf(editor As Control) As String
        If MachineStateEditors.Contains(editor.Name) Then Return "machine state"
        Dim check As CheckBox = TryCast(editor, CheckBox)
        If check IsNot Nothing Then Return If(check.Checked, "On", "Off")
        Dim radio As RadioButton = TryCast(editor, RadioButton)
        If radio IsNot Nothing Then Return If(radio.Checked, "selected", "not selected")
        Dim number As NumericUpDown = TryCast(editor, NumericUpDown)
        If number IsNot Nothing Then Return number.Value.ToString("0.##", CultureInfo.InvariantCulture)
        Dim box As TextBox = TryCast(editor, TextBox)
        If box IsNot Nothing Then
            If box.UseSystemPasswordChar OrElse box.PasswordChar <> ChrW(0) Then Return "masked"
            Return If(box.Text.Length = 0, "empty", Stable(box.Text))
        End If
        Dim list As ComboBox = TryCast(editor, ComboBox)
        If list IsNot Nothing Then
            If list.SelectedIndex >= 0 Then Return list.GetItemText(list.SelectedItem)
            Return If(list.Text.Length = 0, "none selected", Stable(list.Text))
        End If
        If TypeOf editor Is Button OrElse TypeOf editor Is LinkLabel OrElse TypeOf editor Is FlowLayoutPanel Then Return "none (command)"
        If TypeOf editor Is Label Then Return "none (information)"
        Return "not a stored value"
    End Function

    ''' <summary>The computer, the account and the profile folder are not part of a default.</summary>
    Private Shared Function Stable(text As String) As String
        Dim result As String = text
        Dim replacements As New List(Of KeyValuePair(Of String, String)) From {
            New KeyValuePair(Of String, String)(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<user profile>"),
            New KeyValuePair(Of String, String)(Environment.MachineName, "<computer name>"),
            New KeyValuePair(Of String, String)(Environment.UserName, "<user name>")}
        For Each pair As KeyValuePair(Of String, String) In replacements
            If pair.Key.Length > 2 Then result = result.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase)
        Next
        Return result
    End Function

    ''' <summary>A table cell in the document's house style: one line, the typographic dash, ellipsis and quote characters
    ''' written as plain ones, the pipe escaped.</summary>
    Private Shared Function Cell(text As String) As String
        Dim result As String = If(text, "")
        result = result.Replace(ChrW(&H2026), "..").Replace("...", "..")
        result = result.Replace(ChrW(&H2013), "-").Replace(ChrW(&H2014), "-")
        result = result.Replace(ChrW(&H2018), "'").Replace(ChrW(&H2019), "'")
        result = result.Replace(ChrW(&H201C), """").Replace(ChrW(&H201D), """")
        result = Regex.Replace(result, "\s+", " ").Trim()
        Return result.Replace("|", "\|")
    End Function

    ' --- comparison -------------------------------------------------------------------------------------

    Private Shared Function ToLf(text As String) As String
        Return text.Replace(vbCrLf, vbLf)
    End Function

    Private Shared Function ToCrLf(text As String) As String
        Return ToLf(text).Replace(vbLf, vbCrLf)
    End Function

    ''' <summary>The first line that differs, as "line N: built [..] / committed [..]".</summary>
    Private Shared Function FirstDifference(expected As String, actual As String) As String
        Dim a As String() = expected.Split(ControlChars.Lf)
        Dim b As String() = actual.Split(ControlChars.Lf)
        For index As Integer = 0 To Math.Max(a.Length, b.Length) - 1
            Dim left As String = If(index < a.Length, a(index), "(end)")
            Dim right As String = If(index < b.Length, b(index), "(end)")
            If Not String.Equals(left, right, StringComparison.Ordinal) Then
                Return "line " & (index + 1).ToString(CultureInfo.InvariantCulture) & ": built [" & left & "] / committed [" & right & "]"
            End If
        Next
        Return "no line differs"
    End Function

End Class
