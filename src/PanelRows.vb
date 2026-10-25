#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq

' The rows of the F3 panel and the order the details list may show them in
' (005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md PG-2, PG-5).
'
' The panel does not build its own file list any more: it asks the main window for the one it
' already has (Main_Form.SnapshotFileList), so the order, the filter and the metadata are the
' main window's by construction. What lives here is only the pure part - the row, and the sort
' the details list applies to ITS OWN display order without ever touching the main window's.
' Modern build only.

''' <summary>One file of the panel. Size and date come from the metadata the main window's
''' scan already carried; a row for a file that arrived later (an undo put it back) has none
''' until somebody asks the file system, once.</summary>
Friend NotInheritable Class PanelFileRow
    ''' <summary>The file on disk (triage), or in file manager mode the local or materialised path of an entry,
    ''' "" for a remote entry that has none. It is where the bytes are, NOT what the row is: see <see cref="Identity"/>.</summary>
    Public Property Path As String = ""
    Public Property Kind As MediaKind = MediaKind.Other
    Public Property SizeBytes As Long = -1
    Public Property Modified As DateTime = DateTime.MinValue

    ' --- file manager mode (257). Every member below keeps the default a triage row has, so the panel's own
    ' rows and everything that reads them behave as they always did. ---

    ''' <summary>The opaque identity of a file manager entry; "" for a triage row, whose identity is its path.</summary>
    Public Property EntryId As String = ""
    ''' <summary>The name to show when it is not the last segment of <see cref="Path"/> (a remote entry has no Windows path).</summary>
    Public Property DisplayName As String = ""
    Public Property IsDirectory As Boolean
    Public Property IsLink As Boolean
    Public Property IsHidden As Boolean
    Public Property IsSystem As Boolean
    ''' <summary>False for a row the thumbnail loader must never be handed: a folder, a non-media file, an entry with no disk path.</summary>
    Public Property AllowThumbnail As Boolean = True

    ''' <summary>What selection, the thumbnail memory and the renderers key this row by. A triage row's path (compared
    ''' case-insensitively, as ever); a manager entry's opaque id (compared exactly, so two remote names that differ only
    ''' by case stay two rows).</summary>
    Public ReadOnly Property Identity As String
        Get
            Return If(EntryId.Length > 0, EntryId, Path)
        End Get
    End Property

    Public ReadOnly Property HasMetadata As Boolean
        Get
            Return SizeBytes >= 0 OrElse (IsDirectory AndAlso Modified <> DateTime.MinValue)
        End Get
    End Property

    Public ReadOnly Property Name As String
        Get
            If DisplayName.Length > 0 Then Return DisplayName
            Try
                Return System.IO.Path.GetFileName(Path)
            Catch
                Return Path
            End Try
        End Get
    End Property
End Class

''' <summary>What the main window hands the panel: its list, as it is right now.</summary>
Friend NotInheritable Class PanelListSnapshot
    Public ReadOnly Property Rows As New List(Of PanelFileRow)()
    ''' <summary>The file on screen in the main window - the card the panel opens on.</summary>
    Public Property CurrentPath As String = ""
    ''' <summary>The main window's file-list generation (FileOpRules.ListStampMatches).</summary>
    Public Property Generation As Integer
    Public Property Folder As String = ""
End Class

Friend Enum PanelSortColumn
    ''' <summary>The main window's order, untouched.</summary>
    None
    Name
    Kind
    Size
    Modified
End Enum

Friend Module PanelSort

    ''' <summary>
    ''' The rows in the order a column asks for. <see cref="PanelSortColumn.None"/> returns
    ''' the input order. The sort is STABLE, so two files with the same size keep the main
    ''' window's relative order, and it is a pure function of the rows: sorting here never
    ''' reaches the main window's list (acceptance 6).
    ''' </summary>
    Friend Function Order(rows As IEnumerable(Of PanelFileRow), column As PanelSortColumn,
                          descending As Boolean) As List(Of PanelFileRow)
        Dim source As List(Of PanelFileRow) = If(rows Is Nothing, New List(Of PanelFileRow)(), rows.ToList())

        ' File manager mode (257): folders come before files in either direction, and a folder has no size to sort by,
        ' so its Size column falls back to its name. A triage list has no folder and takes the single-group path below.
        If source.Any(Function(r) r.IsDirectory) Then
            Dim folders As List(Of PanelFileRow) = source.Where(Function(r) r.IsDirectory).ToList()
            Dim files As List(Of PanelFileRow) = source.Where(Function(r) Not r.IsDirectory).ToList()
            Dim folder_Column As PanelSortColumn = If(column = PanelSortColumn.Size, PanelSortColumn.Name, column)
            Dim result As List(Of PanelFileRow) = OrderGroup(folders, folder_Column, descending)
            result.AddRange(OrderGroup(files, column, descending))
            Return result
        End If
        Return OrderGroup(source, column, descending)
    End Function

    Private Function OrderGroup(source As List(Of PanelFileRow), column As PanelSortColumn,
                                descending As Boolean) As List(Of PanelFileRow)
        If column = PanelSortColumn.None Then Return source

        Dim ordered As IOrderedEnumerable(Of PanelFileRow)
        Select Case column
            Case PanelSortColumn.Name
                ordered = If(descending,
                             source.OrderByDescending(Function(r) r.Name, NaturalNameComparer.Instance),
                             source.OrderBy(Function(r) r.Name, NaturalNameComparer.Instance))
            Case PanelSortColumn.Kind
                ' By kind, then by name inside a kind - "all the videos together" is the use.
                ordered = If(descending,
                             source.OrderByDescending(Function(r) CInt(r.Kind)).ThenBy(Function(r) r.Name, NaturalNameComparer.Instance),
                             source.OrderBy(Function(r) CInt(r.Kind)).ThenBy(Function(r) r.Name, NaturalNameComparer.Instance))
            Case PanelSortColumn.Size
                ordered = If(descending,
                             source.OrderByDescending(Function(r) r.SizeBytes),
                             source.OrderBy(Function(r) r.SizeBytes))
            Case Else
                ordered = If(descending,
                             source.OrderByDescending(Function(r) r.Modified),
                             source.OrderBy(Function(r) r.Modified))
        End Select
        Return ordered.ToList()
    End Function

    ''' <summary>The next state of a header click: ascending, then descending, then back to
    ''' the main window's own order.</summary>
    Friend Sub NextClick(clicked As PanelSortColumn, ByRef column As PanelSortColumn, ByRef descending As Boolean)
        If clicked = PanelSortColumn.None Then
            column = PanelSortColumn.None
            descending = False
        ElseIf column <> clicked Then
            column = clicked
            descending = False
        ElseIf Not descending Then
            descending = True
        Else
            column = PanelSortColumn.None
            descending = False
        End If
    End Sub

End Module

''' <summary>"IMG_2" before "IMG_10" - digit runs compare as numbers, everything else
''' case-insensitively. Pure, so a sort is the same on every machine (the shell's own natural
''' compare depends on the OS locale).</summary>
Friend NotInheritable Class NaturalNameComparer
    Implements IComparer(Of String)

    Friend Shared ReadOnly Instance As New NaturalNameComparer()

    Public Function Compare(x As String, y As String) As Integer Implements IComparer(Of String).Compare
        If x Is Nothing Then Return If(y Is Nothing, 0, -1)
        If y Is Nothing Then Return 1

        Dim i As Integer = 0
        Dim j As Integer = 0
        While i < x.Length AndAlso j < y.Length
            If Char.IsDigit(x(i)) AndAlso Char.IsDigit(y(j)) Then
                Dim si As Integer = i
                Dim sj As Integer = j
                While i < x.Length AndAlso Char.IsDigit(x(i)) : i += 1 : End While
                While j < y.Length AndAlso Char.IsDigit(y(j)) : j += 1 : End While
                Dim a As String = x.Substring(si, i - si).TrimStart("0"c)
                Dim b As String = y.Substring(sj, j - sj).TrimStart("0"c)
                If a.Length <> b.Length Then Return a.Length.CompareTo(b.Length)
                Dim c As Integer = String.CompareOrdinal(a, b)
                If c <> 0 Then Return c
            Else
                Dim cx As Char = Char.ToUpperInvariant(x(i))
                Dim cy As Char = Char.ToUpperInvariant(y(j))
                If cx <> cy Then Return cx.CompareTo(cy)
                i += 1
                j += 1
            End If
        End While
        Return (x.Length - i).CompareTo(y.Length - j)
    End Function

End Class
#End If
