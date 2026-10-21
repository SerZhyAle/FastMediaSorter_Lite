#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Every call of a <c>tools\Stage-*.ps1</c> payload script from a packager passes only parameters the script declares
''' (specification 251 T-2). PowerShell refuses an unknown named parameter at run time and nowhere earlier, so a stale
''' <c>-Server</c> survived in <c>Build-OfflineRelease.ps1</c> - the free pre-flight of every release - until a full
''' local run stopped on it, while <c>release.yml</c> had the correct call. Read from the files that ship; net10 only
''' (repository files, the runtime changes nothing).
''' </summary>
Public Class PackagerStageCallTests

    ' What a script gets for free from [CmdletBinding()] / [Parameter()]; accepted without a declaration.
    Private Shared ReadOnly CommonParameters As String() = {
        "Verbose", "Debug", "ErrorAction", "WarningAction", "InformationAction", "ErrorVariable", "WarningVariable",
        "InformationVariable", "OutVariable", "OutBuffer", "PipelineVariable", "WhatIf", "Confirm"}

    <Fact>
    Public Sub EveryStagePayloadCallPassesOnlyParametersTheScriptDeclares()
        Dim callers As New List(Of String) From {"build.ps1", Path.Combine(".github", "workflows", "release.yml")}
        callers.AddRange(Directory.GetFiles(Path.Combine(RepoRoot(), "tools"), "Build-*.ps1").Select(Function(f) Path.Combine("tools", Path.GetFileName(f))))

        Dim declared As New Dictionary(Of String, HashSet(Of String))(StringComparer.OrdinalIgnoreCase)
        For Each script As String In Directory.GetFiles(Path.Combine(RepoRoot(), "tools"), "Stage-*.ps1")
            declared(Path.GetFileName(script)) = DeclaredParameters(File.ReadAllText(script))
        Next
        Assert.True(declared.Count >= 3, "expected the Stage-*.ps1 scripts under tools")

        Dim checkedCalls As Integer = 0
        Dim problems As New List(Of String)
        For Each caller As String In callers
            For Each stageCall As StageCall In StageCalls(File.ReadAllText(Path.Combine(RepoRoot(), caller)))
                checkedCalls += 1
                Dim known As HashSet(Of String) = Nothing
                If Not declared.TryGetValue(stageCall.Script, known) Then
                    problems.Add($"{caller}: calls {stageCall.Script}, which is not under tools")
                    Continue For
                End If
                For Each name As String In stageCall.Names
                    If Not known.Contains(name) AndAlso Not CommonParameters.Contains(name, StringComparer.OrdinalIgnoreCase) Then
                        problems.Add($"{caller}: {stageCall.Script} is called with -{name}, which it does not declare (declares: {String.Join(", ", known.OrderBy(Function(n) n))})")
                    End If
                Next
            Next
        Next
        ' A reader that finds nothing would pass forever: the packagers make well over ten such calls.
        Assert.True(checkedCalls >= 10, $"only {checkedCalls} Stage-*.ps1 calls were recognised - the reader is broken")
        Assert.True(problems.Count = 0, String.Join(Environment.NewLine, problems))
    End Sub

    <Fact>
    Public Sub TheReaderSeesAnUnknownParameter()
        ' The reader itself: a call with a name the script lacks must come out as that name.
        Dim ps As String = "& (Join-Path $PSScriptRoot ""Stage-ServerViewerPayload.ps1"") -UserStage $a -Destination $b -Server -Require" & vbLf &
                           "# & (Join-Path $PSScriptRoot ""Stage-ServerViewerPayload.ps1"") -Commented" & vbLf &
                           "& (Join-Path $PSScriptRoot ""Stage-FileDoPayload.ps1"") `" & vbLf &
                           "    -Destination $stageDir" & vbLf
        Dim found = StageCalls(ps).ToList()
        Assert.Equal(2, found.Count)
        Assert.Equal({"UserStage", "Destination", "Server", "Require"}, found(0).Names)
        Assert.Equal({"Destination"}, found(1).Names)
        Dim declared As HashSet(Of String) = DeclaredParameters("[CmdletBinding()]" & vbLf & "param(" & vbLf &
            "    [Parameter(Mandatory)][string]$UserStage," & vbLf & "    [Parameter(Mandatory = $true)][string]$Destination," & vbLf &
            "    [switch]$Require" & vbLf & ")" & vbLf)
        Assert.Equal({"Destination", "Require", "UserStage"}, declared.OrderBy(Function(n) n).ToArray())
    End Sub

    ' --- reading PowerShell ------------------------------------------------------------------

    Private Structure StageCall
        Public Script As String
        Public Names As String()
    End Structure

    ''' <summary>The named parameters of the first <c>param(..)</c> block: every <c>]$Name</c> inside it.</summary>
    Private Shared Function DeclaredParameters(text As String) As HashSet(Of String)
        Dim result As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim start As Match = Regex.Match(text, "(?im)^\s*param\s*\(")
        If Not start.Success Then Return result
        Dim depth As Integer = 0
        Dim i As Integer = start.Index + start.Length - 1
        Dim blockStart As Integer = i
        While i < text.Length
            If text(i) = "("c Then depth += 1
            If text(i) = ")"c Then
                depth -= 1
                If depth = 0 Then Exit While
            End If
            i += 1
        End While
        For Each m As Match In Regex.Matches(text.Substring(blockStart, i - blockStart), "\]\s*\$(\w+)")
            result.Add(m.Groups(1).Value)
        Next
        Return result
    End Function

    ''' <summary>
    ''' The invocations of a <c>Stage-*.ps1</c> script in a packager: lines that CALL it (<c>&amp;</c>, <c>.\tools\</c>,
    ''' <c>pwsh</c>), with backtick continuations joined, comments left out, and the <c>-Name</c> tokens that follow it.
    ''' </summary>
    Private Shared Iterator Function StageCalls(text As String) As IEnumerable(Of StageCall)
        Dim joined As String = Regex.Replace(text.Replace(vbCr, ""), "`\n[ \t]*", " ")
        For Each line As String In joined.Split(vbLf(0))
            Dim trimmed As String = line.TrimStart()
            If trimmed.StartsWith("#", StringComparison.Ordinal) Then Continue For
            If Not Regex.IsMatch(trimmed, "^(?:&\s|\.\\tools\\|\./tools/|pwsh\b|powershell\b)") Then Continue For
            Dim m As Match = Regex.Match(line, "(Stage-\w+\.ps1)")
            If Not m.Success Then Continue For
            Dim names As New List(Of String)
            For Each n As Match In Regex.Matches(line.Substring(m.Index + m.Length), "(?<=\s)-([A-Za-z]\w*)")
                names.Add(n.Groups(1).Value)
            Next
            Yield New StageCall With {.Script = m.Groups(1).Value, .Names = names.ToArray()}
        Next
    End Function

    Private Shared Function RepoRoot() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

End Class
#End If
