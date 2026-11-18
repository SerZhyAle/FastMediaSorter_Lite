Option Strict On
Option Explicit On

Imports System.Globalization

' What the verb "open" does with its one target (L0317): which of the viewer's existing entry paths the target
' belongs to, and the text the one ProcessArgument entry has always taken. Pure - no file system, no window -
' so the spellings are held by Lite.Tests without a form.
Namespace AppCommands

    ''' <summary>How an <c>open</c> target is told apart before anything touches the disk.</summary>
    Friend Enum OpenTargetClass
        ''' <summary>A file, folder or archive path: probed off the UI thread, answered when the window shows it.</summary>
        Path = 0
        ''' <summary>A resource locator or a link (<c>sftp://</c>, <c>gd://</c>, <c>smb://</c>, <c>http(s)://</c>): handed to the
        ''' connect flow, which may ask the person something, so the answer is "handed over".</summary>
        Address = 1
        ''' <summary>A document the viewer reads into a confirmation dialog (<c>.fmscfg</c>, <c>.fmsexch</c>, <c>.fmsbcast</c>).</summary>
        Document = 2
        ''' <summary>An internal <c>::name::</c> token of the window-message transport: never a target of a command, because it
        ''' would reach the phone-code door without the gate of that verb.</summary>
        Internal = 3
        ''' <summary>Nothing to open.</summary>
        Empty = 4
    End Enum

    Friend Module OpenTargets

        ''' <summary>The words of the <c>kind</c> the verb returns.</summary>
        Friend Const KindImage As String = "image"
        Friend Const KindVideo As String = "video"
        Friend Const KindAudio As String = "audio"
        Friend Const KindDocument As String = "document"
        Friend Const KindArchive As String = "archive"
        Friend Const KindFolder As String = "folder"
        Friend Const KindResource As String = "resource"
        Friend Const KindOther As String = "other"

        ''' <summary>The shipped switch the bare launch understands, in the spelling the entry composes (it reads it in any letter case).</summary>
        Friend Const LegacyNoBackSwitch As String = "-NoBack"

        Friend Function Classify(target As String) As OpenTargetClass
            If String.IsNullOrWhiteSpace(target) Then Return OpenTargetClass.Empty
            Dim text As String = target.Trim()
            If text.StartsWith("::", StringComparison.Ordinal) Then Return OpenTargetClass.Internal
            If HasScheme(text) Then Return OpenTargetClass.Address
            Dim extension As String = ExtensionOf(text)
            If extension = ".fmscfg" OrElse extension = ".fmsexch" OrElse extension = ".fmsbcast" Then Return OpenTargetClass.Document
            Return OpenTargetClass.Path
        End Function

        ''' <summary>A URI scheme (<c>letters[letters digits + - .]*://</c>). A drive letter is never one: a drive is one letter followed by a colon, not "://".</summary>
        Friend Function HasScheme(text As String) As Boolean
            If String.IsNullOrEmpty(text) Then Return False
            Dim sep As Integer = text.IndexOf("://", StringComparison.Ordinal)
            If sep < 2 Then Return False
            For i As Integer = 0 To sep - 1
                Dim c As Char = text(i)
                Dim letter As Boolean = (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "A"c AndAlso c <= "Z"c)
                If i = 0 AndAlso Not letter Then Return False
                If Not (letter OrElse (c >= "0"c AndAlso c <= "9"c) OrElse c = "+"c OrElse c = "-"c OrElse c = "."c) Then Return False
            Next
            Return True
        End Function

        ''' <summary>
        ''' The full path a target names for a caller whose working folder is <paramref name="workingDir"/> (a relative path is
        ''' relative to the caller, not to the running viewer). An address is returned as it is. Nothing is read from the disk.
        ''' Nothing when the text is not a path at all.
        ''' </summary>
        Friend Function ResolveAgainst(workingDir As String, target As String) As String
            Dim text As String = If(target, String.Empty).Trim()
            If text.Length = 0 Then Return Nothing
            If HasScheme(text) Then Return text
            Try
                If String.IsNullOrEmpty(workingDir) Then Return System.IO.Path.GetFullPath(text)
                Return System.IO.Path.GetFullPath(System.IO.Path.Combine(workingDir, text))
            Catch ex As ArgumentException
                Return Nothing
            Catch ex As NotSupportedException
                Return Nothing
            Catch ex As System.IO.PathTooLongException
                Return Nothing
            End Try
        End Function

        ''' <summary>The text the viewer's single entry takes for this target: the path, and the shipped switch after it when
        ''' <c>--no-back</c> was given - exactly what a bare launch of the same spelling put into that entry.</summary>
        Friend Function ToLegacyArgument(target As String, noBack As Boolean) As String
            Dim text As String = If(target, String.Empty).Trim()
            If Not noBack Then Return text
            Return text & " " & LegacyNoBackSwitch
        End Function

        ''' <summary>The target and the switch a bare launch carries, whichever side the switch stood on. The same reading the
        ''' viewer's entry gives its argument; kept here so the spellings listed as aliases of the verb have one test.</summary>
        Friend Function SplitLegacyArgument(argument As String, ByRef noBack As Boolean) As String
            noBack = False
            Dim text As String = If(argument, String.Empty).Trim()
            If String.Equals(text, LegacyNoBackSwitch, StringComparison.OrdinalIgnoreCase) Then
                noBack = True
                Return String.Empty
            End If
            If text.StartsWith(LegacyNoBackSwitch & " ", StringComparison.OrdinalIgnoreCase) Then
                noBack = True
                text = text.Substring(LegacyNoBackSwitch.Length + 1).Trim()
            End If
            If text.EndsWith(" " & LegacyNoBackSwitch, StringComparison.OrdinalIgnoreCase) Then
                noBack = True
                text = text.Substring(0, text.Length - LegacyNoBackSwitch.Length - 1).Trim()
            End If
            Return text
        End Function

        Private Function ExtensionOf(text As String) As String
            Dim slash As Integer = text.LastIndexOfAny(New Char() {"/"c, "\"c})
            Dim dot As Integer = text.LastIndexOf("."c)
            If dot <= slash Then Return String.Empty
            Return text.Substring(dot).TrimEnd(" "c).ToLower(CultureInfo.InvariantCulture)
        End Function

    End Module

End Namespace
