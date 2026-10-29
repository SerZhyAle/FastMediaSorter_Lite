#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Linq

' Identity is deliberately the existing path + write time + length tuple.
' Provenance distinguishes leased archive/remote inputs from an ordinary file.
Public NotInheritable Class DocumentIdentity
    Public Property Path As String = ""
    Public Property LastWriteUtcTicks As Long
    Public Property Length As Long
    Public Property Provenance As String = ""

    Public Function Matches(other As DocumentIdentity) As Boolean
        Return other IsNot Nothing AndAlso Length = other.Length AndAlso LastWriteUtcTicks = other.LastWriteUtcTicks AndAlso
            String.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase) AndAlso
            String.Equals(Provenance, other.Provenance, StringComparison.Ordinal)
    End Function
End Class

Public NotInheritable Class DocumentReadingPosition
    Public Property Format As String = "pdf"
    Public Property PageNumber As Integer = 1
    Public Property SpineKey As String = ""
    Public Property Anchor As String = ""
    Public Property TextOffset As Integer
    Public Property Viewport As Double
    Public Property HorizontalViewport As Double
    Public Property ZoomMode As String = "fitPage"
    Public Property ZoomPercent As Integer = 100
    Public Property HasViewport As Boolean

    Public Sub Normalize()
        If Format <> "epub" Then Format = "pdf"
        PageNumber = Math.Max(1, PageNumber)
        TextOffset = Math.Max(0, TextOffset)
        Viewport = If(Double.IsNaN(Viewport) OrElse Double.IsInfinity(Viewport), 0, Math.Max(0, Math.Min(1, Viewport)))
        HorizontalViewport = If(Double.IsNaN(HorizontalViewport) OrElse Double.IsInfinity(HorizontalViewport), 0, Math.Max(0, Math.Min(1, HorizontalViewport)))
        ZoomPercent = Math.Max(25, Math.Min(800, ZoomPercent))
        If Not {"fitPage", "fitWidth", "actual", "manual"}.Contains(ZoomMode) Then ZoomMode = "fitPage"
        SpineKey = If(SpineKey, "")
        Anchor = If(Anchor, "")
        If SpineKey.Length > 2048 Then SpineKey = SpineKey.Substring(0, 2048)
        If Anchor.Length > 2048 Then Anchor = Anchor.Substring(0, 2048)
    End Sub
End Class

Public NotInheritable Class DocumentBookmark
    Public Property Id As String = Guid.NewGuid().ToString("N")
    Public Property Identity As DocumentIdentity
    Public Property Position As DocumentReadingPosition
    Public Property Label As String = ""
End Class

Public Module DocumentReadingState
    Public Const HistoryLimit As Integer = 500
    Public Const BookmarkLimit As Integer = 2000

    Public Function NormalizeBookmarks(items As IEnumerable(Of DocumentBookmark)) As List(Of DocumentBookmark)
        If items Is Nothing Then Return New List(Of DocumentBookmark)()
        Dim result As New List(Of DocumentBookmark)()
        Dim ids As New HashSet(Of String)(StringComparer.Ordinal)
        For Each item In items.Reverse().Take(BookmarkLimit * 2)
            If item Is Nothing OrElse item.Identity Is Nothing OrElse item.Position Is Nothing OrElse
                String.IsNullOrWhiteSpace(item.Identity.Path) OrElse item.Identity.Length < 0 OrElse
                String.IsNullOrEmpty(item.Id) OrElse Not ids.Add(item.Id) Then Continue For
            item.Position.Normalize()
            item.Label = If(item.Label, "")
            If item.Label.Length > 256 Then item.Label = item.Label.Substring(0, 256)
            result.Add(item)
            If result.Count = BookmarkLimit Then Exit For
        Next
        result.Reverse()
        Return result
    End Function

    ' Literal search avoids regex denial of service. Callers own cancellation between units.
    Public Function FindMatches(text As String, query As String, caseSensitive As Boolean,
                                Optional limit As Integer = 500) As List(Of Integer)
        Dim result As New List(Of Integer)()
        If String.IsNullOrEmpty(text) OrElse String.IsNullOrEmpty(query) Then Return result
        Dim comparison = If(caseSensitive, StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase)
        Dim offset As Integer
        While result.Count < Math.Max(0, Math.Min(5000, limit)) AndAlso offset <= text.Length - query.Length
            Dim found = text.IndexOf(query, offset, comparison)
            If found < 0 Then Exit While
            result.Add(found)
            offset = found + query.Length
        End While
        Return result
    End Function

    Public Function SpreadPages(page As Integer, total As Integer, firstAlone As Boolean, rightToLeft As Boolean) As Integer()
        page = Math.Max(1, Math.Min(page, Math.Max(1, total)))
        If firstAlone AndAlso page = 1 Then Return {1}
        Dim first = page - ((page - If(firstAlone, 2, 1)) Mod 2)
        If first >= total Then Return {first}
        Return If(rightToLeft, New Integer() {first + 1, first}, New Integer() {first, first + 1})
    End Function
End Module
#End If
