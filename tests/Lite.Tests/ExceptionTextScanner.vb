Option Strict On

Imports System.Collections.Generic
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>
''' The rule-6 gate's reader (APP-BEHAVIOUR rule 6, 028_SPECIFICATION_CONTRACT_SYNC_DESKTOP_APP_UX.md
''' §5 item 3): finds every use of an exception's Message outside a logging statement.
''' The owner's gate of the same name is the model - it fails on any use, not only on a
''' message box, because the text travels: a Message assigned to a variable in one place
''' is shown by a status line in another. Shared by Lite.Tests and Companion.Tests (linked).
'''
''' Comments and string literals are blanked first, so a sentence that merely mentions a
''' message is not a hit. A statement may span lines (VB continues after "&", ",", "(" and
''' friends), so a hit counts as logged when the logging call starts on one of the lines
''' the statement continues from.
''' </summary>
Friend NotInheritable Class ExceptionTextScanner

    Private Sub New()
    End Sub

    Friend NotInheritable Class Hit
        Public Property Line As Integer
        Public Property Text As String
    End Class

    ''' <summary>X.Message, X.InnerException.Message, e.Error.Message - but not MessageBox.</summary>
    Private Shared ReadOnly MessageUse As New Regex("\b[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*\.Message\b(?!\s*Box)")

    Private Shared ReadOnly LoggingCall As New Regex(
        "\b(?:Debug\.(?:WriteLine|Print|Write)|Trace\.\w+|AppFileLogger\.\w+|Console\.Write\w*)\s*\(")

    ''' <summary>A line ending with one of these is continued on the next one.</summary>
    Private Shared ReadOnly ContinuesOnNextLine As New Regex("(?:[&,(+={]|\b_|\bOrElse|\bAndAlso|\bAnd|\bOr)\s*$")

    ''' <summary>Every Message use in <paramref name="source"/> that is not inside a log call.</summary>
    Friend Shared Function Scan(source As String) As List(Of Hit)
        Dim hits As New List(Of Hit)()
        Dim raw As String() = source.Replace(vbCrLf, vbLf).Split(ChrW(10))
        Dim lines(raw.Length - 1) As String
        For i = 0 To raw.Length - 1
            lines(i) = BlankCommentsAndStrings(raw(i))
        Next

        For i = 0 To lines.Length - 1
            If Not MessageUse.IsMatch(lines(i)) Then Continue For
            If InLoggingStatement(lines, i) Then Continue For
            hits.Add(New Hit With {.Line = i + 1, .Text = raw(i).Trim()})
        Next
        Return hits
    End Function

    Private Shared Function InLoggingStatement(lines As String(), index As Integer) As Boolean
        If LoggingCall.IsMatch(lines(index)) Then Return True
        Dim k = index - 1
        Dim hops = 0
        While k >= 0 AndAlso hops < 8 AndAlso ContinuesOnNextLine.IsMatch(lines(k))
            If LoggingCall.IsMatch(lines(k)) Then Return True
            k -= 1
            hops += 1
        End While
        Return False
    End Function

    ''' <summary>The line with comment text removed and string contents blanked, quotes kept.</summary>
    Friend Shared Function BlankCommentsAndStrings(line As String) As String
        Dim sb As New StringBuilder(line.Length)
        Dim inString = False
        Dim i = 0
        While i < line.Length
            Dim c = line(i)
            If inString Then
                If c = """"c Then
                    If i + 1 < line.Length AndAlso line(i + 1) = """"c Then
                        sb.Append("  ")
                        i += 2
                        Continue While
                    End If
                    inString = False
                    sb.Append(c)
                Else
                    sb.Append(" "c)
                End If
            ElseIf c = """"c Then
                inString = True
                sb.Append(c)
            ElseIf c = "'"c Then
                Exit While
            Else
                sb.Append(c)
            End If
            i += 1
        End While
        Return sb.ToString()
    End Function

End Class
