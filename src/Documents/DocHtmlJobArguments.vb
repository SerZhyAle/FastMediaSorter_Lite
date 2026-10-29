#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic

''' <summary>Specification 276 section 7.2: the document command line of doc-html-translate, built from the
''' producer's documented flags only. The image route keeps its own builder (<c>DocHtmlTranslate.BuildArguments</c>,
''' OCR-INVOCATION 1.1) and is not touched. The list is handed to <c>ProcessStartInfo.ArgumentList</c>, so no value
''' is quoted by hand.</summary>
Public NotInheritable Class DocHtmlJobArguments
    ''' <summary>Convert or recognize, and let the browser translate the page (the default flow, section 4).</summary>
    Public Const ModeBrowser As String = "browser"
    ''' <summary>Convert or recognize only: <c>-notranslate</c>.</summary>
    Public Const ModeConvert As String = "convert"
    ''' <summary>The producer translates with its configured local Ollama service: <c>-ollama</c>.</summary>
    Public Const ModeLocal As String = "local"

    Private Sub New()
    End Sub

    Public Shared Function IsKnownMode(mode As String) As Boolean
        Return String.Equals(mode, ModeBrowser, StringComparison.Ordinal) OrElse
               String.Equals(mode, ModeConvert, StringComparison.Ordinal) OrElse
               String.Equals(mode, ModeLocal, StringComparison.Ordinal)
    End Function

    ''' <summary>The arguments for one job, or Nothing when the request cannot be expressed safely: an unknown
    ''' mode, or an input or output root that is not an absolute local or UNC path. <c>-noopen</c> is never sent
    ''' here - until the producer profile is accepted the converter opens its own output (section 1.1, Q276-3).
    ''' The input goes last, so it can never be read as a flag.</summary>
    Public Shared Function Build(inputPath As String, outputRoot As String, mode As String, force As Boolean) As IReadOnlyList(Of String)
        If Not IsKnownMode(mode) Then Return Nothing
        If Not IsAbsolutePath(inputPath) OrElse Not IsAbsolutePath(outputRoot) Then Return Nothing
        Dim args As New List(Of String) From {"-folder", outputRoot}
        If String.Equals(mode, ModeConvert, StringComparison.Ordinal) Then
            args.Add("-notranslate")
        ElseIf String.Equals(mode, ModeLocal, StringComparison.Ordinal) Then
            args.Add("-ollama")
        End If
        If force Then args.Add("-force")
        args.Add(inputPath)
        Return args
    End Function

    ''' <summary>A drive-rooted (<c>C:\..</c>) or UNC (<c>\\server\share\..</c>) path with no control character.
    ''' A relative path would resolve against the producer's working directory, and such a path can never start
    ''' with <c>-</c>.</summary>
    Public Shared Function IsAbsolutePath(path As String) As Boolean
        If String.IsNullOrWhiteSpace(path) OrElse path.Length < 3 OrElse path.Length > 32767 Then Return False
        For Each ch As Char In path
            If Char.IsControl(ch) Then Return False
        Next
        Dim driveRooted As Boolean = Char.IsLetter(path(0)) AndAlso path(1) = ":"c AndAlso (path(2) = "\"c OrElse path(2) = "/"c)
        Dim unc As Boolean = path.StartsWith("\\", StringComparison.Ordinal) AndAlso path.Length > 2 AndAlso path(2) <> "\"c AndAlso path(2) <> "?"c AndAlso path(2) <> "."c
        Return driveRooted OrElse unc
    End Function
End Class
#End If
