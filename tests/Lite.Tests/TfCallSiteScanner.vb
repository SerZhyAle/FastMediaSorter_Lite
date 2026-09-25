Option Strict On

Imports System.Collections.Generic
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>
''' Finds every <c>Localization.TF(key, args..)</c> call in a VB source text and reports
''' how many arguments follow the key - the rung-3 call-site gate of APP-BEHAVIOUR
''' section 13 (rule 7). Shared by Lite.Tests and Companion.Tests (linked, not copied).
'''
''' A regex cannot count top-level commas across nested calls, strings and line breaks,
''' so this walks the text: strings (with the doubled-quote escape), parentheses and
''' braces are tracked, and a comment outside a string runs to the end of its line.
''' </summary>
Friend NotInheritable Class TfCallSiteScanner

    Private Sub New()
    End Sub

    Friend NotInheritable Class TfCall
        Public Property Key As String
        Public Property ArgumentCount As Integer
        Public Property Line As Integer
    End Class

    Private Shared ReadOnly KeyExpression As New Regex(
        "^\s*(?:""(?:[^""]|"""")*""|vbCrLf|vbLf|vbCr|Chr\(1[03]\))" &
        "(?:\s*&\s*(?:""(?:[^""]|"""")*""|vbCrLf|vbLf|vbCr|Chr\(1[03]\)))*\s*$",
        RegexOptions.Singleline)

    Private Shared ReadOnly KeyToken As New Regex(
        """(?<lit>(?:[^""]|"""")*)""|(?<nl>vbCrLf|vbLf|vbCr|Chr\(10\)|Chr\(13\))")

    ''' <summary>
    ''' Every TF call whose key is a literal chain. A call whose key is a variable cannot
    ''' be checked from source and is left out; the count of those is returned separately.
    ''' </summary>
    Friend Shared Function Scan(text As String, ByRef unkeyed As Integer) As List(Of TfCall)
        Const marker As String = "Localization.TF("
        Dim result As New List(Of TfCall)()
        Dim start = text.IndexOf(marker, StringComparison.Ordinal)
        While start >= 0
            Dim args = SplitArguments(text, start + marker.Length)
            If args IsNot Nothing AndAlso args.Count > 0 Then
                If KeyExpression.IsMatch(args(0)) Then
                    result.Add(New TfCall With {
                        .Key = RebuildKey(args(0)),
                        .ArgumentCount = args.Count - 1,
                        .Line = LineOf(text, start)})
                Else
                    unkeyed += 1
                End If
            End If
            start = text.IndexOf(marker, start + marker.Length, StringComparison.Ordinal)
        End While
        Return result
    End Function

    ''' <summary>The top-level arguments of a call whose "(" ends just before <paramref name="pos"/>.</summary>
    Private Shared Function SplitArguments(text As String, pos As Integer) As List(Of String)
        Dim args As New List(Of String)()
        Dim current As New StringBuilder()
        Dim depth = 0
        Dim i = pos
        While i < text.Length
            Dim c = text(i)
            If c = """"c Then
                ' A string literal, "" inside it being one quote.
                Dim j = i + 1
                While j < text.Length
                    If text(j) = """"c Then
                        If j + 1 < text.Length AndAlso text(j + 1) = """"c Then
                            j += 2
                            Continue While
                        End If
                        Exit While
                    End If
                    j += 1
                End While
                current.Append(text, i, Math.Min(j + 1, text.Length) - i)
                i = j + 1
                Continue While
            End If
            If c = "'"c Then
                ' A comment: skip to the end of the line.
                While i < text.Length AndAlso text(i) <> vbLf(0)
                    i += 1
                End While
                Continue While
            End If
            Select Case c
                Case "("c, "{"c
                    depth += 1
                Case ")"c, "}"c
                    If depth = 0 Then
                        args.Add(current.ToString())
                        Return args
                    End If
                    depth -= 1
                Case ","c
                    If depth = 0 Then
                        args.Add(current.ToString())
                        current.Clear()
                        i += 1
                        Continue While
                    End If
            End Select
            current.Append(c)
            i += 1
        End While
        Return Nothing
    End Function

    Private Shared Function RebuildKey(expr As String) As String
        Dim sb As New StringBuilder()
        For Each t As Match In KeyToken.Matches(expr)
            If t.Groups("lit").Success Then
                sb.Append(t.Groups("lit").Value.Replace("""""", """"))
            Else
                Select Case t.Groups("nl").Value
                    Case "vbCrLf" : sb.Append(vbCrLf)
                    Case "vbLf", "Chr(10)" : sb.Append(vbLf)
                    Case "vbCr", "Chr(13)" : sb.Append(vbCr)
                End Select
            End If
        Next
        Return sb.ToString()
    End Function

    Private Shared Function LineOf(text As String, index As Integer) As Integer
        Dim line = 1
        For k = 0 To index - 1
            If text(k) = vbLf(0) Then line += 1
        Next
        Return line
    End Function

    ''' <summary>How many arguments a key needs: its highest {n} plus one.</summary>
    Friend Shared Function ExpectedArguments(key As String) As Integer
        Dim highest = -1
        For Each m As Match In Regex.Matches(key.Replace("{{", "").Replace("}}", ""), "\{(\d+)[^}]*\}")
            highest = Math.Max(highest, Integer.Parse(m.Groups(1).Value, Globalization.CultureInfo.InvariantCulture))
        Next
        Return highest + 1
    End Function

End Class
