#If Not NETFRAMEWORK Then
Option Strict On

Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

''' <summary>
''' 005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md PG-5, acceptance 6: sorting in the
''' details list is a pure function of the panel's own rows and never reaches the main
''' window's order.
''' </summary>
Public Class PanelRowsTests

    Private Shared Function Row(name As String, kind As MediaKind, size As Long, day As Integer) As PanelFileRow
        Return New PanelFileRow With {.Path = "D:\in\" & name, .Kind = kind, .SizeBytes = size,
                                      .Modified = New DateTime(2026, 9, day)}
    End Function

    Private Shared Function Sample() As List(Of PanelFileRow)
        Return New List(Of PanelFileRow) From {
            Row("IMG_10.jpg", MediaKind.Image, 300, 3),
            Row("clip.mp4", MediaKind.Video, 9000, 1),
            Row("IMG_2.jpg", MediaKind.Image, 100, 2),
            Row("song.mp3", MediaKind.Audio, 300, 5)}
    End Function

    Private Shared Function Names(rows As IEnumerable(Of PanelFileRow)) As String()
        Return rows.Select(Function(r) r.Name).ToArray()
    End Function

    <Fact>
    Public Sub None_keeps_the_main_windows_order()
        Dim rows As List(Of PanelFileRow) = Sample()
        Dim ordered As List(Of PanelFileRow) = PanelSort.Order(rows, PanelSortColumn.None, descending:=True)
        Assert.Equal(Names(rows), Names(ordered))
        Assert.NotSame(rows, ordered)                                    ' a copy: the caller's list is never reordered
    End Sub

    <Fact>
    Public Sub Name_sorts_naturally_so_IMG_2_comes_before_IMG_10()
        Dim ordered As List(Of PanelFileRow) = PanelSort.Order(Sample(), PanelSortColumn.Name, False)
        Assert.Equal(New String() {"clip.mp4", "IMG_2.jpg", "IMG_10.jpg", "song.mp3"}, Names(ordered))
        Assert.Equal(New String() {"song.mp3", "IMG_10.jpg", "IMG_2.jpg", "clip.mp4"}, Names(PanelSort.Order(Sample(), PanelSortColumn.Name, True)))
    End Sub

    ''' <summary>Equal sizes keep the main window's relative order - the sort is stable.</summary>
    <Fact>
    Public Sub Size_is_stable_for_equal_sizes()
        Dim ordered As List(Of PanelFileRow) = PanelSort.Order(Sample(), PanelSortColumn.Size, False)
        Assert.Equal(New String() {"IMG_2.jpg", "IMG_10.jpg", "song.mp3", "clip.mp4"}, Names(ordered))
        Dim biggest As List(Of PanelFileRow) = PanelSort.Order(Sample(), PanelSortColumn.Size, True)
        Assert.Equal("clip.mp4", biggest(0).Name)
        Assert.Equal(New String() {"IMG_10.jpg", "song.mp3"}, Names(biggest.Skip(1).Take(2)))
    End Sub

    <Fact>
    Public Sub Modified_sorts_by_date()
        Assert.Equal(New String() {"clip.mp4", "IMG_2.jpg", "IMG_10.jpg", "song.mp3"},
                     Names(PanelSort.Order(Sample(), PanelSortColumn.Modified, False)))
    End Sub

    <Fact>
    Public Sub Kind_groups_then_sorts_by_name()
        Dim ordered As List(Of PanelFileRow) = PanelSort.Order(Sample(), PanelSortColumn.Kind, False)
        Assert.Equal(New String() {"IMG_2.jpg", "IMG_10.jpg", "clip.mp4", "song.mp3"}, Names(ordered))
    End Sub

    <Fact>
    Public Sub A_header_click_cycles_ascending_descending_and_back_to_the_main_order()
        Dim column As PanelSortColumn = PanelSortColumn.None
        Dim descending As Boolean = False

        PanelSort.NextClick(PanelSortColumn.Size, column, descending)
        Assert.Equal(PanelSortColumn.Size, column)
        Assert.False(descending)

        PanelSort.NextClick(PanelSortColumn.Size, column, descending)
        Assert.True(descending)

        PanelSort.NextClick(PanelSortColumn.Size, column, descending)
        Assert.Equal(PanelSortColumn.None, column)
        Assert.False(descending)

        PanelSort.NextClick(PanelSortColumn.Size, column, descending)
        PanelSort.NextClick(PanelSortColumn.Name, column, descending)     ' another column starts ascending
        Assert.Equal(PanelSortColumn.Name, column)
        Assert.False(descending)
    End Sub

    <Fact>
    Public Sub A_row_without_metadata_says_so_and_sorts_first_by_size()
        Dim fresh As New PanelFileRow With {.Path = "D:\in\back.jpg"}
        Assert.False(fresh.HasMetadata)
        Dim rows As List(Of PanelFileRow) = Sample()
        rows.Add(fresh)
        Assert.Equal("back.jpg", PanelSort.Order(rows, PanelSortColumn.Size, False)(0).Name)
    End Sub

    <Fact>
    Public Sub The_natural_comparer_handles_leading_zeros_case_and_nothing()
        Dim c As NaturalNameComparer = NaturalNameComparer.Instance
        Assert.True(c.Compare("a002", "a10") < 0)
        Assert.Equal(0, c.Compare("ABC", "abc"))
        Assert.True(c.Compare("a", "ab") < 0)
        Assert.True(c.Compare(Nothing, "a") < 0)
        Assert.Equal(0, c.Compare(Nothing, Nothing))
    End Sub

End Class
#End If
