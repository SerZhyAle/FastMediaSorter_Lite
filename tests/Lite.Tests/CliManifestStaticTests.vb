Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Xunit
Imports fmsl.AppCommands

''' <summary>
''' Ticket L0320, contract APP-COMMANDS 0.1 rules 2, 18 and 20 and case C13: one static check over the two
''' manifests (the viewer's and the Share Manager's). It holds the fields of rule 18, the closed vocabularies
''' of rules 10 and 13, the bound of rule 8, every shipped spelling of the inventory as an alias of exactly one
''' verb, the generated documents against the tables, and the two-build facts that need no process. Each check
''' that reports a problem list has a negative control: a copy of the table with one fault, and the check must
''' name it.
''' </summary>
Public Class CliManifestStaticTests

    Private Shared Function Pair(spelling As String, verb As String) As KeyValuePair(Of String, String)
        Return New KeyValuePair(Of String, String)(spelling, verb)
    End Function

    ''' <summary>The inventory of the viewer's entry points (research ticket L0314, section 4.1: V1 to V8) as it shipped.</summary>
    Private Shared Function ViewerInventory() As List(Of KeyValuePair(Of String, String))
        Dim list As New List(Of KeyValuePair(Of String, String)) From {
            Pair("", "show"), Pair("::fms-show-window::", "show"),
            Pair("<path>", "open"), Pair("-NoBack <path>", "open"), Pair("<path> -NoBack", "open"),
            Pair("::fms-phone-code::", "phone-code receive"), Pair("::fms-phone-code-file::", "phone-code receive")}
        For Each spelling As String In {"--build-stream-catalog", "-build-stream-catalog", "/build-stream-catalog",
                                        "--build-catalog", "-build-catalog", "/build-catalog"}
            list.Add(Pair(spelling, "stream-catalog build"))
        Next
        For Each spelling As String In {"--check-stream-catalog", "-check-stream-catalog", "/check-stream-catalog",
                                        "--check-catalog", "-check-catalog", "/check-catalog"}
            list.Add(Pair(spelling, "stream-catalog check"))
        Next
        Return list
    End Function

    ''' <summary>The inventory of the Share Manager's entry points (L0314, section 4.2: S1 to S10) as it shipped.</summary>
    Private Shared Function ShareManagerInventory() As List(Of KeyValuePair(Of String, String))
        Return New List(Of KeyValuePair(Of String, String)) From {
            Pair("", "start"), Pair("::fms-plain-start::", "start"),
            Pair("--tray", "start"), Pair("/tray", "start"),
            Pair("--show", "show"), Pair("/show", "show"), Pair("::fms-show-window::", "show"),
            Pair("<folder>", "share add"),
            Pair("--add-resource", "resource add"), Pair("/add-resource", "resource add"), Pair("::fms-add-resource::", "resource add"),
            Pair("--add-resource-camera", "resource add"), Pair("/add-resource-camera", "resource add"), Pair("::fms-add-resource-camera::", "resource add"),
            Pair("::fms-schedule::", "schedule open"), Pair("::fms-schedule::<folder>", "schedule open"),
            Pair("::fms-broadcast::<path>", "broadcast open"),
            Pair("--clipboard-send", "clipboard send"), Pair("/clipboard-send", "clipboard send"), Pair("::fms-clipboard-send::", "clipboard send"),
            Pair("--clipboard-get", "clipboard get"), Pair("/clipboard-get", "clipboard get"), Pair("::fms-clipboard-get::", "clipboard get")}
    End Function

    ' --- C13: every shipped spelling is an alias of exactly one verb -----------------------------------

    ''' <summary>One line per fault: a spelling of the inventory that is an alias of no verb, of two, or of another verb than
    ''' the inventory says.</summary>
    Private Shared Function AliasProblems(m As CommandManifest, inventory As IEnumerable(Of KeyValuePair(Of String, String))) As List(Of String)
        Dim problems As New List(Of String)()
        For Each item As KeyValuePair(Of String, String) In inventory
            Dim owners As New List(Of String)()
            For Each spec As CommandSpec In m.Commands
                If spec.Aliases.Contains(item.Key) Then owners.Add(spec.Name)
            Next
            If owners.Count = 0 Then
                problems.Add("the spelling '" & item.Key & "' is an alias of no verb (the inventory says " & item.Value & ")")
            ElseIf owners.Count > 1 Then
                problems.Add("the spelling '" & item.Key & "' is an alias of " & owners.Count.ToString() & " verbs: " & String.Join(", ", owners.ToArray()))
            ElseIf Not String.Equals(owners(0), item.Value, StringComparison.Ordinal) Then
                problems.Add("the spelling '" & item.Key & "' is an alias of " & owners(0) & " but the inventory says " & item.Value)
            End If
        Next
        Return problems
    End Function

    ''' <summary>An alias the manifest carries and the inventory does not list: a new frozen spelling is added to the inventory
    ''' on purpose, in the same edit.</summary>
    Private Shared Function UnlistedAliases(m As CommandManifest, inventory As IEnumerable(Of KeyValuePair(Of String, String))) As List(Of String)
        Dim known As New HashSet(Of String)(StringComparer.Ordinal)
        For Each item As KeyValuePair(Of String, String) In inventory
            known.Add(item.Key)
        Next
        Dim extra As New List(Of String)()
        For Each spec As CommandSpec In m.Commands
            For Each a As String In spec.Aliases
                If Not known.Contains(a) Then extra.Add(spec.Name & ": '" & a & "'")
            Next
        Next
        Return extra
    End Function

    <Fact>
    Public Sub Every_shipped_spelling_of_the_viewer_is_an_alias_of_exactly_one_verb()
        Dim problems As List(Of String) = AliasProblems(ViewerCommandTable.Build(), ViewerInventory())
        Assert.True(problems.Count = 0, String.Join("; ", problems.ToArray()))
        Assert.Equal(19, ViewerInventory().Count)
    End Sub

    <Fact>
    Public Sub Every_shipped_spelling_of_the_share_manager_is_an_alias_of_exactly_one_verb()
        Dim problems As List(Of String) = AliasProblems(ShareManagerCommandTable.Build(), ShareManagerInventory())
        Assert.True(problems.Count = 0, String.Join("; ", problems.ToArray()))
        Assert.Equal(23, ShareManagerInventory().Count)
    End Sub

    <Fact>
    Public Sub An_alias_the_inventory_does_not_list_is_named()
        Assert.Equal(0, UnlistedAliases(ViewerCommandTable.Build(), ViewerInventory()).Count)
        Assert.Equal(0, UnlistedAliases(ShareManagerCommandTable.Build(), ShareManagerInventory()).Count)
        Dim m As CommandManifest = ViewerCommandTable.Build()
        m.Find("show").Aliases.Add("--new-spelling")
        Assert.Contains("show: '--new-spelling'", UnlistedAliases(m, ViewerInventory()))
    End Sub

    <Fact>
    Public Sub No_spelling_is_an_alias_of_two_verbs_in_one_manifest()
        For Each m As CommandManifest In {ViewerCommandTable.Build(), ShareManagerCommandTable.Build()}
            Dim owner As New Dictionary(Of String, String)(StringComparer.Ordinal)
            For Each spec As CommandSpec In m.Commands
                For Each a As String In spec.Aliases
                    Dim other As String = Nothing
                    Assert.False(owner.TryGetValue(a, other), "'" & a & "' is an alias of both " & other & " and " & spec.Name)
                    owner(a) = spec.Name
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub Removing_a_spelling_from_the_alias_list_is_named_by_the_check()
        ' The negative control of C13: one spelling out of a copy of each table.
        Dim viewer As CommandManifest = ViewerCommandTable.Build()
        Assert.True(viewer.Find("phone-code receive").Aliases.Remove("::fms-phone-code-file::"))
        Dim viewerProblems As List(Of String) = AliasProblems(viewer, ViewerInventory())
        Assert.Single(viewerProblems)
        Assert.Contains("::fms-phone-code-file::", viewerProblems(0))
        Dim manager As CommandManifest = ShareManagerCommandTable.Build()
        Assert.True(manager.Find("clipboard get").Aliases.Remove("/clipboard-get"))
        Dim managerProblems As List(Of String) = AliasProblems(manager, ShareManagerInventory())
        Assert.Single(managerProblems)
        Assert.Contains("/clipboard-get", managerProblems(0))
        ' A spelling moved to another verb is named too.
        Dim moved As CommandManifest = ViewerCommandTable.Build()
        moved.Find("stream-catalog check").Aliases.Add("-build-catalog")
        Assert.Contains("-build-catalog", String.Join("; ", AliasProblems(moved, ViewerInventory()).ToArray()))
    End Sub

    ' --- rules 8, 10, 13, 18: the fields and the closed vocabularies ---------------------------------------

    Private Shared ReadOnly Rule18Fields As String() = {"name", "aliases", "summary", "synopsis", "args", "returns", "risk", "coldStart",
                                                        "wait", "timeoutMs", "interactive", "idempotent", "exitCodes", "examples", "since"}

    Private Shared ReadOnly ColdStartModes As String() = {"start", "oneshot", "refuse"}
    Private Shared ReadOnly RiskClasses As String() = {CommandRisk.Safe, CommandRisk.ReadsUserData, CommandRisk.WritesUserData, CommandRisk.LeavesMachine}
    Private Shared ReadOnly WaitModes As String() = {"done", "accepted"}

    ''' <summary>One line per fault in a manifest document: a missing field of rule 18, an empty summary, synopsis or example list,
    ''' a cold start, risk or wait outside the closed set, a timeout outside 1 .. the bound, a duplicate verb name, a verb newer than
    ''' the manifest.</summary>
    Private Shared Function FieldProblems(jsonText As String) As List(Of String)
        Dim problems As New List(Of String)()
        Dim value As Object = Nothing
        If Not CommandJson.TryParse(jsonText, value) Then
            problems.Add("the manifest is not valid JSON")
            Return problems
        End If
        Dim root As JsonObject = DirectCast(value, JsonObject)
        Dim commandsValue As Object = Nothing
        If Not root.TryGet("commands", commandsValue) Then
            problems.Add("the manifest has no commands member")
            Return problems
        End If
        Dim cliVersion As Version = Nothing
        Version.TryParse(root.GetString("cliVersion"), cliVersion)
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        For Each entry As Object In DirectCast(commandsValue, List(Of Object))
            Dim o As JsonObject = DirectCast(entry, JsonObject)
            Dim name As String = o.GetString("name")
            If Not seen.Add(If(name, String.Empty)) Then problems.Add(name & ": the verb is declared twice")
            For Each field As String In Rule18Fields
                Dim ignored As Object = Nothing
                If Not o.TryGet(field, ignored) Then problems.Add(name & ": lacks " & field)
            Next
            If String.IsNullOrWhiteSpace(name) Then problems.Add("a verb has no name")
            If String.IsNullOrWhiteSpace(o.GetString("summary")) Then problems.Add(name & ": empty summary")
            If String.IsNullOrWhiteSpace(o.GetString("synopsis")) Then problems.Add(name & ": empty synopsis")
            If Not ColdStartModes.Contains(o.GetString("coldStart")) Then problems.Add(name & ": coldStart '" & o.GetString("coldStart") & "' is not one of start, oneshot, refuse")
            If Not RiskClasses.Contains(o.GetString("risk")) Then problems.Add(name & ": risk '" & o.GetString("risk") & "' is not a class of rule 13")
            If Not WaitModes.Contains(o.GetString("wait")) Then problems.Add(name & ": wait '" & o.GetString("wait") & "' is not done or accepted")
            Dim timeout As Integer = o.GetInt("timeoutMs", 0)
            If timeout < 1 OrElse timeout > CommandBounds.MaxCompletionMs Then problems.Add(name & ": timeoutMs " & timeout.ToString() & " is outside 1 .. " & CommandBounds.MaxCompletionMs.ToString())
            Dim examples As Object = Nothing
            If o.TryGet("examples", examples) AndAlso DirectCast(examples, List(Of Object)).Count = 0 Then problems.Add(name & ": no example")
            Dim since As Version = Nothing
            If Not Version.TryParse(o.GetString("since"), since) Then
                problems.Add(name & ": since '" & o.GetString("since") & "' is not a version")
            ElseIf cliVersion IsNot Nothing AndAlso since > cliVersion Then
                problems.Add(name & ": since " & o.GetString("since") & " is newer than the manifest " & root.GetString("cliVersion"))
            End If
        Next
        Return problems
    End Function

    <Fact>
    Public Sub Every_verb_of_both_manifests_has_the_fields_and_the_bounds_of_the_contract()
        Dim viewer As List(Of String) = FieldProblems(ViewerCommandTable.Build().ToJsonText())
        Assert.True(viewer.Count = 0, "viewer: " & String.Join("; ", viewer.ToArray()))
        Dim manager As List(Of String) = FieldProblems(ShareManagerCommandTable.Build().ToJsonText())
        Assert.True(manager.Count = 0, "share manager: " & String.Join("; ", manager.ToArray()))
    End Sub

    <Fact>
    Public Sub The_same_check_holds_the_documents_on_disk()
        Dim viewer As List(Of String) = FieldProblems(TextOf("docs", "cli-commands.json"))
        Assert.True(viewer.Count = 0, "docs/cli-commands.json: " & String.Join("; ", viewer.ToArray()))
        Dim manager As List(Of String) = FieldProblems(TextOf("docs", "cli-commands.share-manager.json"))
        Assert.True(manager.Count = 0, "docs/cli-commands.share-manager.json: " & String.Join("; ", manager.ToArray()))
    End Sub

    <Fact>
    Public Sub A_fault_in_a_table_is_named_by_the_field_check()
        ' The negative controls of rules 8, 10, 13 and 18: one fault per copy, the verb is named.
        Dim risk As CommandManifest = ViewerCommandTable.Build()
        risk.Find("show").Risk = "harmless"
        Assert.Contains("show: risk 'harmless'", String.Join("; ", FieldProblems(risk.ToJsonText()).ToArray()))

        Dim cold As CommandManifest = ViewerCommandTable.Build()
        cold.Find("quit").ColdStart = Nothing
        Assert.Contains("quit: coldStart", String.Join("; ", FieldProblems(cold.ToJsonText()).ToArray()))

        Dim slow As CommandManifest = ShareManagerCommandTable.Build()
        slow.Find("clipboard get").TimeoutMs = CommandBounds.MaxCompletionMs + 1
        Assert.Contains("clipboard get: timeoutMs", String.Join("; ", FieldProblems(slow.ToJsonText()).ToArray()))

        Dim empty As CommandManifest = ViewerCommandTable.Build()
        empty.Find("status").Summary = " "
        Assert.Contains("status: empty summary", String.Join("; ", FieldProblems(empty.ToJsonText()).ToArray()))

        Dim twice As CommandManifest = ViewerCommandTable.Build()
        twice.Commands.Add(twice.Find("show"))
        Assert.Contains("show: the verb is declared twice", String.Join("; ", FieldProblems(twice.ToJsonText()).ToArray()))

        Dim fields As String = ViewerCommandTable.Build().ToJsonText().Replace("""idempotent""", """idempotent_""")
        Assert.Contains("lacks idempotent", String.Join("; ", FieldProblems(fields).ToArray()))
    End Sub

    ' --- rule 20: the generated documents equal the tables ----------------------------------------------

    <Fact>
    Public Sub The_generated_documents_equal_the_tables_in_code()
        Assert.True(String.Equals(ViewerCommandTable.Build().ToJsonText(), TextOf("docs", "cli-commands.json"), StringComparison.Ordinal),
                    "docs/cli-commands.json is stale: run pwsh -NoProfile -File tools/Build-CliDocs.ps1")
        Assert.True(String.Equals(ShareManagerCommandTable.Build().ToJsonText(), TextOf("docs", "cli-commands.share-manager.json"), StringComparison.Ordinal),
                    "docs/cli-commands.share-manager.json is stale: run pwsh -NoProfile -File tools/Build-CliDocs.ps1")
        Assert.True(String.Equals(CliDocument.Markdown(), TextOf("docs", "CLI.md"), StringComparison.Ordinal),
                    "docs/CLI.md is stale: run pwsh -NoProfile -File tools/Build-CliDocs.ps1")
    End Sub

    ''' <summary>Every command line the script recipes page quotes, in the order it quotes them: the lines inside code fences.</summary>
    Private Shared Function FencedLines(page As String) As List(Of String)
        Dim lines As New List(Of String)()
        Dim inside As Boolean = False
        For Each raw As String In page.Split(ControlChars.Lf)
            If raw.StartsWith("```", StringComparison.Ordinal) Then
                inside = Not inside
            ElseIf inside Then
                lines.Add(raw)
            End If
        Next
        Return lines
    End Function

    Private Shared Function ExampleLinesOfBothTables() As HashSet(Of String)
        Dim all As New HashSet(Of String)(StringComparer.Ordinal)
        For Each manifest As CommandManifest In {ViewerCommandTable.Build(), ShareManagerCommandTable.Build()}
            For Each spec As CommandSpec In manifest.Commands
                For Each example As CommandExample In spec.Examples
                    all.Add(example.Line)
                Next
            Next
        Next
        Return all
    End Function

    <Fact>
    Public Sub The_script_recipes_page_equals_its_generator_and_quotes_only_example_lines_of_the_tables()
        Dim page As String = TextOf("docs", "guides", "COMMAND_LINE_RECIPES.md")
        Assert.True(String.Equals(CliRecipes.Markdown(), page, StringComparison.Ordinal),
                    "docs/guides/COMMAND_LINE_RECIPES.md is stale: run pwsh -NoProfile -File tools/Build-CliDocs.ps1")
        Dim quoted As List(Of String) = FencedLines(page)
        Dim known As HashSet(Of String) = ExampleLinesOfBothTables()
        Assert.True(quoted.Count >= 5, "the page quotes fewer command lines than its recipes need")
        For Each line As String In quoted
            Assert.True(known.Contains(line), "a command line on the page is no example line of any table: " & line)
        Next
        ' The ticket's two recipes are on the page, each with the verb it is about.
        Assert.Contains("## Schedule a copy of a folder", page)
        Assert.Contains("schedule open --folder", page)
        Assert.Contains("schedule list --result", page)
        Assert.Contains("## Save a screenshot of the viewer window to a file", page)
        Assert.Contains("screenshot window --out", page)
        ' Negative control: a line that is not an example line is not accepted, so the loop above can fail.
        Assert.False(known.Contains(quoted(0) & " --not-an-option"))
        ' Present tense, no promise marker.
        For Each marker As String In {"will ", "planned", "coming soon", "not yet", "in development"}
            Assert.DoesNotContain(marker, page.ToLowerInvariant())
        Next
    End Sub

    <Fact>
    Public Sub A_one_word_change_in_either_table_is_seen_by_the_comparison()
        Dim viewer As CommandManifest = ViewerCommandTable.Build()
        viewer.Commands(0).Summary &= " changed"
        Assert.False(String.Equals(viewer.ToJsonText(), TextOf("docs", "cli-commands.json"), StringComparison.Ordinal))
        Dim manager As CommandManifest = ShareManagerCommandTable.Build()
        manager.Commands(0).Summary &= " changed"
        Assert.False(String.Equals(manager.ToJsonText(), TextOf("docs", "cli-commands.share-manager.json"), StringComparison.Ordinal))
    End Sub

    ' --- the two builds: what needs no process --------------------------------------------------------------

    <Fact>
    Public Sub The_x86_project_compiles_none_of_the_command_profile()
        Dim x86 As String = TextOf("src", "FastMediaSorter.vbproj")
        Assert.DoesNotContain("AppCommands", x86)
        Assert.DoesNotContain("CliRecipes", x86)
        ' Every file that carries the profile's code on the mainline is whole-file mainline-only, so a project that did list it
        ' would still compile an empty file; the entry and the catalog switches keep their own net48 branches.
        For Each relative As String() In {New String() {"src", "AppCommands", "ViewerCommandClient.vb"},
                                          New String() {"src", "AppCommands", "AppCommandsText.vb"},
                                          New String() {"src", "Main_Form.AppCommands.vb"},
                                          New String() {"src", "Main_Form.AppCommandsDrive.vb"}}
            Assert.StartsWith("#If Not NETFRAMEWORK Then", TextOf(relative).TrimStart(ChrW(&HFEFF)), StringComparison.Ordinal)
        Next
    End Sub

    <Fact>
    Public Sub The_x86_entry_keeps_its_legacy_branch_and_never_reaches_the_profile()
        Dim entry As String = TextOf("src", "Application_Events.vb")
        Dim legacy As String = Slice(entry, "If Streams.StreamCatalogCli.IsCatalogCommandLine(rawCmdArgs) Then", "#Else")
        Assert.Contains("RefuseNetFramework()", legacy)
        Assert.DoesNotContain("AppCommands.", legacy)
        Dim mainline As String = Slice(entry, "#Else", "#End If")
        Assert.Contains("AppCommands.ViewerCommandClient.TryRun(rawCmdArgs, command_Exit)", mainline)
        Dim catalog As String = TextOf("src", "Streams", "StreamCatalogCli.vb")
        Assert.Contains("#If NETFRAMEWORK Then", catalog)
        Assert.Contains("Public Sub RefuseNetFramework()", catalog)
        Assert.Contains("Environment.ExitCode = 1", Slice(catalog, "Public Sub RefuseNetFramework()", "End Sub"))
    End Sub

    <Fact>
    Public Sub An_older_copy_answers_exit_2_with_the_sentence_and_show_and_open_go_out_in_the_old_spelling()
        Dim client As String = TextOf("src", "AppCommands", "ViewerCommandClient.vb")
        ' A copy that answers the tag with 0 is one that does not know it: the client remembers that and returns OlderCopy.
        Dim deliver As String = Slice(client, "Private Function Deliver(", "Private Function SendOnce(")
        Assert.Contains("If answer = 0 Then sawZero = True", deliver)
        Assert.Contains("Return If(sawZero, Delivery.OlderCopy, Delivery.NoAnswer)", deliver)
        Dim older As String = Slice(client, "Case Delivery.OlderCopy", "Case Delivery.NoAnswer")
        Assert.Contains("If parsed.Spec.Name = ""show"" Then ForwardLegacyShow()", older)
        Assert.Contains("If parsed.Spec.Name = ""open"" Then ForwardLegacyOpen(parsed)", older)
        Assert.Contains("CommandExit.NotProven", older)
        Assert.Contains("AppCommandsText.OlderCopy()", older)
        Assert.Contains("Return True", older)
        ' The old spelling of show is the shipped token; the old spelling of open is the text the bare launch always took.
        Assert.Contains("Main_Form.Show_Window_Command", Slice(client, "Private Sub ForwardLegacyShow(", "Private Sub ForwardLegacyOpen("))
        Assert.Contains("OpenTargets.ToLegacyArgument(", Slice(client, "Private Sub ForwardLegacyOpen(", "Private Function WaitForReply("))
        Assert.Equal(ViewerCommandTable.LegacyShowToken, "::fms-show-window::")
        ' The sentence is a translated text of the program (a key of the localization table), not a literal of the client.
        Dim text As String = Slice(TextOf("src", "AppCommands", "AppCommandsText.vb"), "Friend Function OlderCopy()", "End Function")
        Assert.Contains("Localization.T(", text)
    End Sub

    <Fact>
    Public Sub An_older_share_manager_gets_the_same_answer_and_the_old_alias()
        Dim client As String = TextOf("src", "FastMediaSorterCompanion", "ShareManagerCommandClient.vb")
        Assert.Contains("OlderCopy", client)
        Assert.Contains("CommandExit.NotProven", client)
        Assert.Contains("ShareManagerCommandText.OlderCopy()", client)
    End Sub

End Class
