Option Strict On
Option Explicit On

' docs/CLI.md holds one section per program that answers APP-COMMANDS: the viewer's document first,
' unchanged, then the Share Manager's as a section. This is the one place the whole file is composed,
' for the generator (tools/Build-CliDocs.ps1) and for AppCommandsTests. Neither program uses it: the viewer's project
' excludes it (and the Share Manager's table) from its glob, and the Share Manager links only the table.
Namespace AppCommands

    Friend Module CliDocument

        Friend Function Markdown() As String
            Return ViewerCommandTable.ToMarkdown() & ControlChars.Lf & ShareManagerCommandTable.ToMarkdownSection()
        End Function

    End Module

End Namespace
