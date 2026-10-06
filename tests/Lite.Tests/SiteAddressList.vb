#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json

''' <summary>
''' tools/site-addresses.jsonl (ticket 150) read for the tests that compare a program constant with the list.
''' Linked into Companion.Tests, which holds the Share Manager's constants. The gate that resolves the
''' addresses against the page set is tools/Test-SiteAddresses.ps1; this reads the same file from the
''' other side, so a constant edited without its line in the list fails here.
''' </summary>
Friend NotInheritable Class SiteAddressList

    ''' <summary>One (address, holder) record.</summary>
    Friend NotInheritable Class Entry
        Friend Property Address As String
        Friend Property Holder As String
        Friend Property Kind As String
        Friend Property Carried As String
        Friend Property Fragment As String
    End Class

    Private Sub New()
    End Sub

    Friend Shared Function Entries() As IReadOnlyList(Of Entry)
        Dim result As New List(Of Entry)
        For Each line As String In File.ReadAllLines(Path.Combine(RepoRoot(), "tools", "site-addresses.jsonl"))
            If String.IsNullOrWhiteSpace(line) Then Continue For
            Using doc As JsonDocument = JsonDocument.Parse(line)
                Dim e As JsonElement = doc.RootElement
                Dim probe As JsonElement
                If Not e.TryGetProperty("address", probe) Then Continue For
                result.Add(New Entry With {
                    .Address = e.GetProperty("address").GetString(),
                    .Holder = e.GetProperty("holder").GetString(),
                    .Kind = e.GetProperty("kind").GetString(),
                    .Carried = Text(e, "carried", "literal"),
                    .Fragment = Text(e, "fragment", "")})
            End Using
        Next
        Return result
    End Function

    ''' <summary>The addresses the list names for one holder file (repository-relative, forward slashes).</summary>
    Friend Shared Function ListedFor(holder As String) As ISet(Of String)
        Return New HashSet(Of String)(Entries().Where(Function(e) e.Holder = holder).Select(Function(e) e.Address), StringComparer.Ordinal)
    End Function

    ''' <summary>The locale-sibling form of a page group ("lang/&lt;code&gt;/trust.html"), or Nothing when it has none.</summary>
    Friend Shared Function LocaleForm(group As String) As String
        For Each line As String In File.ReadAllLines(Path.Combine(RepoRoot(), "tools", "site-addresses.jsonl"))
            If String.IsNullOrWhiteSpace(line) Then Continue For
            Using doc As JsonDocument = JsonDocument.Parse(line)
                Dim e As JsonElement = doc.RootElement
                Dim name As JsonElement
                If e.TryGetProperty("group", name) AndAlso name.GetString() = group Then
                    Dim locale As JsonElement = e.GetProperty("locale")
                    Return If(locale.ValueKind = JsonValueKind.String, locale.GetString(), Nothing)
                End If
            End Using
        Next
        Throw New InvalidOperationException("tools/site-addresses.jsonl declares no group '" & group & "'")
    End Function

    Private Shared Function Text(e As JsonElement, name As String, fallback As String) As String
        Dim v As JsonElement
        Return If(e.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String, v.GetString(), fallback)
    End Function

    Private Shared Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

End Class
#End If
