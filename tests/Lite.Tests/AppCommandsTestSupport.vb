Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports fmsl.AppCommands

''' <summary>
''' Ticket L0320: what the two conformance classes share - the repository root, a source file read with its
''' line ends normalised, a scratch folder, a request, and the smallest valid argument list of a verb.
''' </summary>
Friend Module ConformanceSupport

    Friend Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

    ''' <summary>A file of the repository as text, UTF-8, line ends as LF.</summary>
    Friend Function TextOf(ParamArray relative As String()) As String
        Dim parts As New List(Of String)({RepoRoot()})
        parts.AddRange(relative)
        Return File.ReadAllText(Path.Combine(parts.ToArray()), Encoding.UTF8).Replace(vbCrLf, vbLf)
    End Function

    Friend Function ScratchFolder() As String
        Dim dir As String = Path.Combine(Path.GetTempPath(), "fms-appcommands-conformance", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        Return dir
    End Function

    Friend Function Req(command As String, Optional origin As String = "cli", Optional callerElevated As Boolean = False,
                        Optional args As IEnumerable(Of String) = Nothing) As CommandRequest
        Dim r As New CommandRequest With {.Command = command, .Origin = origin, .RequestId = Guid.NewGuid().ToString("D"),
                                          .CallerElevated = callerElevated}
        If args IsNot Nothing Then r.Args.AddRange(args)
        Return r
    End Function

    ''' <summary>The shortest argument list the parser accepts for a verb: every required option with a sample value
    ''' of its kind, every required positional value as a plain word.</summary>
    Friend Function ValidArgs(spec As CommandSpec) As List(Of String)
        Dim result As New List(Of String)()
        ' Item R.4: the first member of each oneOf group stands in for the group.
        Dim chosen As New HashSet(Of String)(StringComparer.Ordinal)
        For Each g As String() In spec.OneOf
            chosen.Add(g(0))
        Next
        For Each a As CommandArg In spec.Args
            If Not a.Required AndAlso Not chosen.Contains(a.Name) Then Continue For
            Dim sample As String = If(String.Equals(a.Kind, "int", StringComparison.Ordinal), "1", "x")
            If a.Values IsNot Nothing AndAlso a.Values.Length > 0 Then sample = a.Values(0)
            If a.Positional Then
                result.Add(sample)
            Else
                result.Add("--" & a.Name)
                If Not String.Equals(a.Kind, "bool", StringComparison.Ordinal) Then result.Add(sample)
            End If
        Next
        Return result
    End Function

    ''' <summary>The part of a source text from the first occurrence of <paramref name="startMarker"/> up to the first
    ''' occurrence of <paramref name="endMarker"/> after it (to the end when the end marker is not found); empty when
    ''' the start marker is not there.</summary>
    Friend Function Slice(text As String, startMarker As String, endMarker As String) As String
        Dim at As Integer = text.IndexOf(startMarker, StringComparison.Ordinal)
        If at < 0 Then Return String.Empty
        Dim stop_ As Integer = text.IndexOf(endMarker, at + startMarker.Length, StringComparison.Ordinal)
        If stop_ < 0 Then stop_ = text.Length
        Return text.Substring(at, stop_ - at)
    End Function

    ''' <summary>Verbs a second process answers by itself, before any first instance is asked.</summary>
    Friend ReadOnly AnsweredByTheCaller As String() = {"commands", "version"}

End Module
