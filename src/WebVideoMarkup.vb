#If NETFRAMEWORK Then
Option Strict On

Imports System.Text

''' <summary>
''' The pieces of the page the x86 viewer hands the IE WebBrowser for a video that carry a
''' value from outside - the file's own name (audit 046, D-06).
'''
''' The file URI went into a single-quoted attribute as it came, and Uri.AbsoluteUri
''' escapes a space but not an apostrophe, a parenthesis, "=" or ";" - all legal in an
''' NTFS name. "Don't stop.mp4" cut the src short (IE failed and the video fell through to
''' VLC), and a name built for it closed the attribute and added an event handler of its
''' own, in a page whose window.external was the main window. Encoding the value for the
''' attribute is the whole fix: the browser decodes it back to the same URL before it
''' loads anything.
'''
''' Pure, so the encoding is held by a test rather than by reading the concatenation.
''' net48 only - the mainline has no WebBrowser.
''' </summary>
Friend Module WebVideoMarkup

    ''' <summary>A value made safe for a quoted HTML attribute, single or double.</summary>
    Friend Function AttributeValue(value As String) As String
        If String.IsNullOrEmpty(value) Then Return ""
        Dim encoded As New StringBuilder(value.Length + 16)
        For Each c As Char In value
            Select Case c
                Case "&"c : encoded.Append("&amp;")
                Case "'"c : encoded.Append("&#39;")
                Case """"c : encoded.Append("&quot;")
                Case "<"c : encoded.Append("&lt;")
                Case ">"c : encoded.Append("&gt;")
                Case Else : encoded.Append(c)
            End Select
        Next
        Return encoded.ToString()
    End Function

    ''' <summary>The video's &lt;source&gt; element for <paramref name="fileUri"/>.</summary>
    Friend Function SourceTag(fileUri As String) As String
        Return "<source src='" & AttributeValue(fileUri) &
               "' onerror=""fmsReportVideoError('Error: Unsupported video type (source failed to load)');"">"
    End Function

End Module
#End If
