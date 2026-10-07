#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

' The details list of the F3 panel (005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md PG-5):
' the other renderer of the same SelectionModel. A triage pass by DATE or SIZE is a list job,
' and it is the cheap way to see what a folder actually holds once documents are listed.
'
' It is VIRTUAL (the rows are asked for as they scroll into view - five thousand files cost
' nothing to open) and OWNER-DRAWN (the native selection is never consulted: what is
' highlighted is what the model says, which is what lets the selection survive a switch to
' the grid and back). It is its own file for the reason the specification gives - a virtual
' ListView is not a mode flag inside the grid.

Partial Public Class Image_Panel_Form

    Friend NotInheritable Class DetailsList
        Inherits ListView

        Private model As New SelectionModel()
        Private rows As IReadOnlyList(Of PanelFileRow) = New List(Of PanelFileRow)()
        Private sort_Column As PanelSortColumn = PanelSortColumn.None
        Private sort_Descending As Boolean

        ''' <summary>A header was clicked: the panel reorders ITS OWN display order.</summary>
        Friend Event SortRequested As EventHandler(Of PanelSortColumn)
        Friend Event SelectionGesture As EventHandler
        Friend Event ItemActivated As EventHandler(Of Integer)

        Friend Sub New()
            View = View.Details
            VirtualMode = True
            OwnerDraw = True
            FullRowSelect = True
            MultiSelect = True
            HideSelection = False
            HeaderStyle = ColumnHeaderStyle.Clickable
            BorderStyle = BorderStyle.None
            DoubleBuffered = True
            TabStop = True

            Columns.Add(New ColumnHeader With {.Width = 320})
            Columns.Add(New ColumnHeader With {.Width = 110})
            Columns.Add(New ColumnHeader With {.Width = 90, .TextAlign = HorizontalAlignment.Right})
            Columns.Add(New ColumnHeader With {.Width = 150})
            ApplyHeaders()
        End Sub

        ''' <summary>The DPI the column widths are in now. They are authored above in 96-dpi pixels, and a
        ''' ListView never scales its columns (neither at the first layout nor on a DPI change), so this does.</summary>
        Private columns_Dpi As Integer = 96

        Protected Overrides Sub OnHandleCreated(e As EventArgs)
            MyBase.OnHandleCreated(e)
            ScaleColumnsTo(DeviceDpi)
        End Sub

        Protected Overrides Sub OnDpiChangedAfterParent(e As EventArgs)
            MyBase.OnDpiChangedAfterParent(e)
            ScaleColumnsTo(DeviceDpi)
        End Sub

        ''' <summary>Rescales every column by the ratio of the new DPI to the one they are in - proportionally, so a
        ''' column the user dragged keeps its relation to the others.</summary>
        Private Sub ScaleColumnsTo(dpi As Integer)
            If dpi <= 0 OrElse dpi = columns_Dpi Then Return
            Dim ratio As Double = dpi / CDbl(columns_Dpi)
            For Each column As ColumnHeader In Columns
                column.Width = Math.Max(1, CInt(Math.Round(column.Width * ratio)))
            Next
            columns_Dpi = dpi
        End Sub

        Friend Sub Bind(selection As SelectionModel, displayRows As IReadOnlyList(Of PanelFileRow))
            model = selection
            rows = displayRows
            VirtualListSize = rows.Count
            Invalidate()
        End Sub

        Friend Sub SetSort(column As PanelSortColumn, descending As Boolean)
            sort_Column = column
            sort_Descending = descending
            ApplyHeaders()
        End Sub

        Private Sub ApplyHeaders()
            Columns(0).Text = Header(Localization.T("Имя"), PanelSortColumn.Name)
            Columns(1).Text = Header(Localization.T("Тип"), PanelSortColumn.Kind)
            Columns(2).Text = Header(Localization.T("Размер"), PanelSortColumn.Size)
            Columns(3).Text = Header(Localization.T("Изменён"), PanelSortColumn.Modified)
        End Sub

        Private Function Header(caption As String, column As PanelSortColumn) As String
            If sort_Column <> column Then Return caption
            Return caption & If(sort_Descending, "  ▼", "  ▲")
        End Function

        ''' <summary>Scrolls just enough to show a row.</summary>
        Friend Sub ShowRow(index As Integer)
            If index >= 0 AndAlso index < rows.Count Then
                Try
                    EnsureVisible(index)
                Catch
                    ' A list that is not yet laid out: the next paint shows the top, which is
                    ' wrong for one frame and harmless.
                End Try
            End If
        End Sub

        ''' <summary>Whole rows that fit - for Page Up/Down.</summary>
        Friend Function PageRows() As Integer
            Dim row_Height As Integer = Math.Max(1, Font.Height + 6)
            Return Math.Max(1, ClientSize.Height \ row_Height - 1)
        End Function

        ' ------------------------------------------------------------------- data ----

        Protected Overrides Sub OnRetrieveVirtualItem(e As RetrieveVirtualItemEventArgs)
            MyBase.OnRetrieveVirtualItem(e)
            If e.ItemIndex < 0 OrElse e.ItemIndex >= rows.Count Then
                e.Item = New ListViewItem("")
                Return
            End If
            Dim row As PanelFileRow = rows(e.ItemIndex)
            Dim item As New ListViewItem(row.Name)
            item.SubItems.Add(PanelText.KindName(row.Kind))
            item.SubItems.Add(PanelText.FormatSize(row.SizeBytes))
            item.SubItems.Add(PanelText.FormatDate(row.Modified, row.HasMetadata))
            e.Item = item
        End Sub

        Protected Overrides Sub OnColumnClick(e As ColumnClickEventArgs)
            MyBase.OnColumnClick(e)
            Dim clicked As PanelSortColumn
            Select Case e.Column
                Case 0 : clicked = PanelSortColumn.Name
                Case 1 : clicked = PanelSortColumn.Kind
                Case 2 : clicked = PanelSortColumn.Size
                Case Else : clicked = PanelSortColumn.Modified
            End Select
            RaiseEvent SortRequested(Me, clicked)
        End Sub

        ' ------------------------------------------------------------------ paint ----

        Protected Overrides Sub OnDrawColumnHeader(e As DrawListViewColumnHeaderEventArgs)
            e.DrawDefault = True
        End Sub

        Protected Overrides Sub OnDrawItem(e As DrawListViewItemEventArgs)
            ' Drawn cell by cell in OnDrawSubItem.
            e.DrawDefault = False
        End Sub

        Protected Overrides Sub OnDrawSubItem(e As DrawListViewSubItemEventArgs)
            Dim selected As Boolean = model.IsSelected(e.ItemIndex)
            Dim back As Color = If(selected, SystemColors.Highlight, BackColor)
            Dim ink As Color = If(selected, SystemColors.HighlightText, ForeColor)

            Using fill As New SolidBrush(back)
                e.Graphics.FillRectangle(fill, e.Bounds)
            End Using

            Dim flags As TextFormatFlags = TextFormatFlags.VerticalCenter Or
                                           TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix
            If e.ColumnIndex = 2 Then flags = flags Or TextFormatFlags.Right
            Dim bounds As Rectangle = Rectangle.Inflate(e.Bounds, -4, 0)
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, bounds, ink, flags)

            If e.ColumnIndex = 0 AndAlso Focused AndAlso model.FocusIndex = e.ItemIndex Then
                Dim row_Bounds As New Rectangle(0, e.Bounds.Y, ClientSize.Width, e.Bounds.Height)
                ControlPaint.DrawFocusRectangle(e.Graphics, row_Bounds, ink, back)
            End If
        End Sub

        ' ------------------------------------------------------------------ input ----

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Select Case keyData And Keys.KeyCode
                Case Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Space
                    Return True
            End Select
            Return MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            Focus()
            If e.Button <> MouseButtons.Left Then Return

            Dim hit As ListViewHitTestInfo = HitTest(e.Location)
            Dim index As Integer = If(hit.Item Is Nothing, -1, hit.Item.Index)
            Dim ctrl As Boolean = (ModifierKeys And Keys.Control) = Keys.Control
            Dim shift As Boolean = (ModifierKeys And Keys.Shift) = Keys.Shift

            If index >= 0 Then
                If ctrl AndAlso shift Then
                    model.CtrlShiftClick(index)
                ElseIf shift Then
                    model.ShiftClick(index)
                ElseIf ctrl Then
                    model.CtrlClick(index)
                Else
                    model.Click(index)
                End If
            ElseIf Not (ctrl OrElse shift) Then
                model.SelectNone()
            End If
            Invalidate()
            RaiseEvent SelectionGesture(Me, EventArgs.Empty)
        End Sub

        Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
            MyBase.OnMouseDoubleClick(e)
            If e.Button <> MouseButtons.Left Then Return
            Dim hit As ListViewHitTestInfo = HitTest(e.Location)
            If hit.Item IsNot Nothing Then RaiseEvent ItemActivated(Me, hit.Item.Index)
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            Invalidate()
        End Sub

    End Class

End Class

''' <summary>The words and numbers of the details list. Pure, so a size is spelled the same on
''' every machine and a kind has one name in one place.</summary>
Friend Module PanelText

    Friend Function KindName(kind As MediaKind) As String
        Select Case kind
            Case MediaKind.Image : Return Localization.T("Изображение")
            Case MediaKind.Video : Return Localization.T("Видео")
            Case MediaKind.Audio : Return Localization.T("Аудио")
            Case MediaKind.Document : Return Localization.T("Документ")
            Case MediaKind.Container : Return Localization.T("Контейнер")
            Case MediaKind.Book : Return Localization.T("Книга")
            Case MediaKind.Stream : Return Localization.T("Поток")
            Case Else : Return Localization.T("Файл")
        End Select
    End Function

    ''' <summary>"4.2 MB". Units are the ones the audio plate and Explorer's own details view use;
    ''' empty for a row with no metadata yet (-1) rather than a "0" that would be a lie.</summary>
    Friend Function FormatSize(bytes As Long) As String
        If bytes < 0 Then Return ""
        Dim units() As String = {"B", "KB", "MB", "GB", "TB"}
        Dim size As Double = bytes
        Dim unit As Integer = 0
        While size >= 1024 AndAlso unit < units.Length - 1
            size /= 1024
            unit += 1
        End While
        Return If(unit = 0, CInt(size).ToString(CultureInfo.InvariantCulture), size.ToString("0.0", CultureInfo.InvariantCulture)) & " " & units(unit)
    End Function

    Friend Function FormatDate(modified As DateTime, hasMetadata As Boolean) As String
        If Not hasMetadata OrElse modified = DateTime.MinValue Then Return ""
        Return modified.ToString("g", CultureInfo.CurrentCulture)
    End Function

End Module
#End If
