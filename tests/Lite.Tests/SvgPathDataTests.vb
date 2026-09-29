#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing.Drawing2D
Imports Xunit

''' <summary>
''' The path reader behind the catalog glyphs (ICON-RENDER). Net10 only, like the reader:
''' the x86 fallback does not render catalog glyphs. Audit 057.S03: two inputs the vendored
''' set does not carry today but a re-import from the catalog could - minified arc flags,
''' and a number after a closepath, which used to spin the reader forever.
''' </summary>
Public Class SvgPathDataTests

    <Fact>
    Public Sub Compact_arc_flags_read_like_separated_ones()
        ' "011" is large-arc 0, sweep 1 and the x coordinate 1 (SVG 1.1 section 8.3).
        Using separated As GraphicsPath = SvgPathData.Parse("M2 12 a1 1 0 0 1 1 1"),
              compact As GraphicsPath = SvgPathData.Parse("M2 12 a1 1 0 011 1")
            Assert.Equal(separated.PointCount, compact.PointCount)
            Assert.Equal(separated.PathPoints, compact.PathPoints)
            Assert.Equal(separated.PathTypes, compact.PathTypes)
        End Using
    End Sub

    <Fact>
    Public Sub Separated_arc_flags_are_unchanged()
        Using path As GraphicsPath = SvgPathData.Parse("M4 12 A8 8 0 1 0 20 12")
            Assert.True(path.PointCount > 2)
            Dim last = path.PathPoints(path.PointCount - 1)
            Assert.Equal(20.0F, last.X, 3)
            Assert.Equal(12.0F, last.Y, 3)
        End Using
    End Sub

    <Fact>
    Public Sub A_number_after_a_closepath_is_refused_instead_of_looping()
        Assert.Throws(Of FormatException)(Sub() SvgPathData.Parse("M0 0 L1 1 Z 5 5").Dispose())
    End Sub

    <Fact>
    Public Sub An_empty_path_is_an_empty_figure()
        Using path As GraphicsPath = SvgPathData.Parse("")
            Assert.Equal(0, path.PointCount)
        End Using
    End Sub

End Class
#End If
