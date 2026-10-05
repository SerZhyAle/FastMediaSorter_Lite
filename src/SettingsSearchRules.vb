#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq

''' <summary>Searches display metadata only. The caller never supplies editable/user values.</summary>
Friend NotInheritable Class SettingsSearchRules
    Friend Shared Function IsExtensionList(value As String) As Boolean
        If String.IsNullOrWhiteSpace(value) Then Return True
        Return value.Split(New Char() {";"c, " "c, ChrW(9), ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries).
            All(Function(token) System.Text.RegularExpressions.Regex.IsMatch(token, "^\.[A-Za-z0-9][A-Za-z0-9_-]*$"))
    End Function
    Private Sub New()
    End Sub

    Friend Shared Function Matches(query As String, caption As String, hint As String, options As IEnumerable(Of String), culture As CultureInfo) As Boolean
        Dim terms = query.Trim().Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
        Dim text = caption & " " & hint & " " & String.Join(" ", options)
        Return terms.All(Function(term) culture.CompareInfo.IndexOf(text, term, CompareOptions.IgnoreCase) >= 0)
    End Function

    Friend Shared Function IsServiceEndpoint(value As String) As Boolean
        If value.Trim().Length = 0 Then Return True
        Dim address As Uri = Nothing
        Return Uri.TryCreate(value.Trim(), UriKind.Absolute, address) AndAlso
            (address.Scheme = Uri.UriSchemeHttp OrElse address.Scheme = Uri.UriSchemeHttps) AndAlso address.Host.Length > 0
    End Function
End Class
#End If
