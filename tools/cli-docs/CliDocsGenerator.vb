Option Strict On
Option Explicit On

' Entry point of tools/Build-CliDocs.ps1: renders docs/cli-commands.json, docs/cli-commands.share-manager.json,
' docs/CLI.md and docs/guides/COMMAND_LINE_RECIPES.md from the two command tables in code
' (src/AppCommands/ViewerCommandTable.vb and ShareManagerCommandTable.vb). Compiled with the pure library only.
Module CliDocsGenerator

    Function Main(args As String()) As Integer
        If args.Length < 2 Then
            Console.Error.WriteLine("usage: CliDocsGenerator <docs folder> write|check")
            Return 10
        End If
        Dim docs As String = args(0)
        Dim check As Boolean = String.Equals(args(1), "check", StringComparison.OrdinalIgnoreCase)
        Dim wanted As New Dictionary(Of String, String) From {
            {"cli-commands.json", fmsl.AppCommands.ViewerCommandTable.Build().ToJsonText()},
            {"cli-commands.share-manager.json", fmsl.AppCommands.ShareManagerCommandTable.Build().ToJsonText()},
            {"CLI.md", fmsl.AppCommands.CliDocument.Markdown()},
            {Path.Combine("guides", "COMMAND_LINE_RECIPES.md"), fmsl.AppCommands.CliRecipes.Markdown()}}
        Dim stale As Integer = 0
        For Each pair As KeyValuePair(Of String, String) In wanted
            Dim target As String = Path.Combine(docs, pair.Key)
            Dim current As String = If(File.Exists(target), File.ReadAllText(target).Replace(vbCrLf, vbLf), Nothing)
            If String.Equals(current, pair.Value, StringComparison.Ordinal) Then
                Console.WriteLine("up to date: " & pair.Key)
            ElseIf check Then
                Console.WriteLine("STALE: " & pair.Key)
                stale += 1
            Else
                File.WriteAllText(target, pair.Value, New Text.UTF8Encoding(False))
                Console.WriteLine("written: " & pair.Key)
            End If
        Next
        Return If(stale = 0, 0, 1)
    End Function

End Module
