#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' The document panel on a real <see cref="ChromeSurface"/> (specification 280 section 7): the items of each reader
''' laid out at several widths. The pure rules (which items, in which order, which are the core) are held by
''' <c>DocumentPanelRulesTests</c>; this class holds what the SURFACE does with them - the page or chapter box and the
''' page find never go into the "more" menu, the items that collapse are the last ones in reading order and only those,
''' a narrower row never shows more than a wider one, and a conditional item that is away keeps its place as air.
''' The items are built the way the glue builds them (kind, core, conditional); the words are the localization keys,
''' because only their widths matter here.
''' </summary>
Public Class DocumentPanelSurfaceTests

    Private Shared ReadOnly AllKinds As DocumentReaderKind() = {
        DocumentReaderKind.Pdf, DocumentReaderKind.Epub, DocumentReaderKind.Html, DocumentReaderKind.Text}

    ''' <summary>The widths tried, in logical px: far below the core, the narrow window, a laptop, a wide screen.</summary>
    Private Shared ReadOnly Widths As Integer() = {60, 120, 200, 300, 420, 560, 720, 960, 1400, 4000}

    ' Shared by every surface of the class: a surface keeps the fonts it was configured with, so they are never disposed under it.
    Private Shared ReadOnly TextFont As New Font("Segoe UI", UiScale.Px(ChromeMetrics.TextSize, 1.0F), GraphicsUnit.Pixel)
    Private Shared ReadOnly SymbolFont As New Font("Segoe UI Symbol", UiScale.Px(ChromeMetrics.SymbolSize, 1.0F), GraphicsUnit.Pixel)

    Private Shared Function ItemsOf(kind As DocumentReaderKind, Optional awayVisible As Boolean = False) As List(Of ChromeItem)
        Dim items As New List(Of ChromeItem)()
        For Each id As String In DocumentPanelRules.ItemIds(kind)
            Dim item As ChromeItem
            Select Case DocumentPanelRules.KindOf(id)
                Case DocumentItemKind.Toggle
                    item = New ChromeItem(id, ChromeItemKind.Toggle) With {.GlyphId = "action.more"}
                Case DocumentItemKind.MenuButton
                    item = New ChromeItem(id, ChromeItemKind.MenuButton) With {.GlyphId = "action.more"}
                Case DocumentItemKind.Position
                    Dim reserved As String = DocumentPanelRules.PositionReadoutReserved(9999, DocumentPanelRules.PositionReadoutFormat)
                    item = New ChromeItem(id, ChromeItemKind.Text) With {.Caption = reserved, .ReservedText = reserved}
                Case DocumentItemKind.FindBox
                    item = New ChromeItem(id, ChromeItemKind.Text) With {.Caption = DocumentPanelRules.CaptionKey(id), .ReservedText = DocumentPanelRules.CaptionKey(id)}
                Case DocumentItemKind.Readout
                    item = New ChromeItem(id, ChromeItemKind.Text) With {.Caption = "999 / 999", .ReservedText = "999 / 999"}
                Case Else
                    item = New ChromeItem(id, ChromeItemKind.Button) With {.GlyphId = "action.more"}
            End Select
            item.NeverOverflows = DocumentPanelRules.IsPinnedCore(kind, id)
            If DocumentPanelRules.IsConditional(kind, id) AndAlso Not awayVisible Then
                item.Visible = False
                item.KeepsSlotWhenHidden = True
            End If
            items.Add(item)
        Next
        Return items
    End Function

    Private Shared Sub OnSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(60000), "Document panel surface check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    ''' <summary>A surface carrying <paramref name="items"/>, <paramref name="width"/> logical px wide, laid out.</summary>
    Private Shared Function LaidOut(items As List(Of ChromeItem), width As Integer, rightToLeft As Boolean) As ChromeSurface
        Dim surface As New ChromeSurface()
        surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, rightToLeft, TextFont, SymbolFont)
        surface.OverflowId = "btn_Overflow_3"
        surface.PinItem = New ChromeItem("chrome_pin_chrome_Document", ChromeItemKind.Toggle) With {.IsPin = True}
        surface.SetItems(items)
        surface.Size = New Size(width, surface.PreferredPanelSize().Height)
        surface.LayoutItems()
        Return surface
    End Function

    Private Shared Function OverflowIds(surface As ChromeSurface) As List(Of String)
        Return surface.OverflowItems.Select(Function(item) item.Id).ToList()
    End Function

    <Fact>
    Public Sub The_page_box_chapter_box_and_page_find_never_go_into_the_menu_at_any_width()
        OnSta(Sub()
                  Dim failures As New List(Of String)()
                  For Each kind As DocumentReaderKind In AllKinds
                      Dim core As List(Of String) = DocumentPanelRules.ItemIds(kind).Where(Function(id) DocumentPanelRules.IsPinnedCore(kind, id)).ToList()
                      For Each width As Integer In Widths
                          Using surface As ChromeSurface = LaidOut(ItemsOf(kind), width, False)
                              Dim collapsed As List(Of String) = OverflowIds(surface)
                              For Each id As String In core
                                  If collapsed.Contains(id) Then failures.Add(kind.ToString() & "@" & width.ToString() & ":" & id)
                              Next
                          End Using
                      Next
                  Next
                  Assert.True(failures.Count = 0, "a pinned core item went into the menu: " & String.Join(", ", failures))
              End Sub)
    End Sub

    <Fact>
    Public Sub Only_the_last_items_in_reading_order_collapse_and_never_fewer_as_the_row_narrows()
        OnSta(Sub()
                  Dim failures As New List(Of String)()
                  For Each kind As DocumentReaderKind In AllKinds
                      Dim order As List(Of String) = DocumentPanelRules.ItemIds(kind).ToList()
                      Dim previousCount As Integer = Integer.MaxValue
                      ' Widest first: every step narrower may only add to the menu.
                      For Each width As Integer In Widths.Reverse()
                          Using surface As ChromeSurface = LaidOut(ItemsOf(kind), width, False)
                              Dim collapsed As List(Of String) = OverflowIds(surface)
                              Dim label As String = kind.ToString() & "@" & width.ToString()
                              ' The menu lists them in reading order.
                              Dim positions As List(Of Integer) = collapsed.Select(Function(id) order.IndexOf(id)).ToList()
                              If positions.Any(Function(index) index < 0) OrElse Not positions.SequenceEqual(positions.OrderBy(Function(index) index)) Then
                                  failures.Add(label & " menu order " & String.Join("/", collapsed))
                              End If
                              ' Every visible, non-core item that comes after a collapsed one has collapsed too.
                              If collapsed.Count > 0 Then
                                  Dim first As Integer = positions.Min()
                                  For index As Integer = first To order.Count - 1
                                      Dim id As String = order(index)
                                      If DocumentPanelRules.IsPinnedCore(kind, id) OrElse DocumentPanelRules.IsConditional(kind, id) Then Continue For
                                      If Not collapsed.Contains(id) Then failures.Add(label & " " & id & " stayed after " & order(first) & " collapsed")
                                  Next
                              End If
                              If previousCount <> Integer.MaxValue AndAlso collapsed.Count < previousCount Then
                                  failures.Add(label & " has " & collapsed.Count.ToString() & " in the menu, a wider row had " & previousCount.ToString())
                              End If
                              previousCount = collapsed.Count
                          End Using
                      Next
                  Next
                  Assert.True(failures.Count = 0, String.Join("; ", failures))
              End Sub)
    End Sub

    <Fact>
    Public Sub At_the_natural_width_nothing_collapses_and_every_visible_item_is_placed()
        OnSta(Sub()
                  For Each kind As DocumentReaderKind In AllKinds
                      Dim probe As ChromeSurface = LaidOut(ItemsOf(kind), 4000, False)
                      Dim natural As Integer = probe.PreferredPanelSize().Width
                      probe.Dispose()
                      Using surface As ChromeSurface = LaidOut(ItemsOf(kind), natural + 8, False)
                          Assert.True(surface.OverflowItems.Count = 0, kind.ToString() & " collapsed " & String.Join("/", OverflowIds(surface)) & " at its natural width " & natural.ToString())
                          For Each item As ChromeItem In surface.Items
                              If item.IsPin OrElse item.Id = surface.OverflowId Then Continue For
                              If item.Visible Then
                                  Assert.False(item.Bounds.IsEmpty, kind.ToString() & ": " & item.Id & " has no place at its natural width")
                              Else
                                  Assert.True(item.Bounds.IsEmpty, kind.ToString() & ": the away item " & item.Id & " is painted")
                              End If
                          Next
                      End Using
                  Next
              End Sub)
    End Sub

    <Fact>
    Public Sub A_conditional_html_item_that_is_away_keeps_its_place_so_the_next_one_does_not_move()
        OnSta(Sub()
                  Dim kind As DocumentReaderKind = DocumentReaderKind.Html
                  Dim wide As Integer = 4000
                  Using away As ChromeSurface = LaidOut(ItemsOf(kind, awayVisible:=False), wide, False),
                        shown As ChromeSurface = LaidOut(ItemsOf(kind, awayVisible:=True), wide, False)
                      Dim conditional As List(Of String) = DocumentPanelRules.ItemIds(kind).Where(Function(id) DocumentPanelRules.IsConditional(kind, id)).ToList()
                      Assert.NotEmpty(conditional)
                      For Each id As String In conditional
                          Dim awayItem As ChromeItem = away.Items.First(Function(item) item.Id = id)
                          Assert.True(awayItem.Bounds.IsEmpty, id & " is painted while it is away")
                          Assert.DoesNotContain(id, OverflowIds(away))
                      Next
                      ' An item after the last conditional one stands in the same place whether the conditional one is there or not.
                      Dim after As String = DocumentPanelRules.IdReturnToFiles
                      Dim movedBy As Integer = away.Items.First(Function(item) item.Id = after).Bounds.X - shown.Items.First(Function(item) item.Id = after).Bounds.X
                      Assert.True(movedBy = 0, after & " moved by " & movedBy.ToString() & " px when a conditional item appeared")
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_right_to_left_row_collapses_the_same_items_as_a_left_to_right_row()
        OnSta(Sub()
                  Dim differences As New List(Of String)()
                  For Each kind As DocumentReaderKind In AllKinds
                      For Each width As Integer In Widths
                          Using left As ChromeSurface = LaidOut(ItemsOf(kind), width, False),
                                right As ChromeSurface = LaidOut(ItemsOf(kind), width, True)
                              Dim a As String = String.Join("/", OverflowIds(left))
                              Dim b As String = String.Join("/", OverflowIds(right))
                              If a <> b Then differences.Add(kind.ToString() & "@" & width.ToString() & ": " & a & " vs " & b)
                          End Using
                      Next
                  Next
                  Assert.True(differences.Count = 0, "reading order is the contract, not left to right: " & String.Join("; ", differences))
              End Sub)
    End Sub

    ' --- the glue: source scans (the WinForms half cannot be built here) ---------------------------------------------

    Private Shared Function Source(name As String) As String
        Dim root As New DirectoryInfo(AppContext.BaseDirectory)
        While root IsNot Nothing AndAlso Not File.Exists(Path.Combine(root.FullName, "src", "Main_Form.vb"))
            root = root.Parent
        End While
        Assert.NotNull(root)
        Dim fullPath As String = Path.Combine(root.FullName, "src", name)
        Assert.True(File.Exists(fullPath), "missing source: " & name)
        Return File.ReadAllText(fullPath).Replace(vbCrLf, vbLf).Replace(vbLf, vbCrLf)
    End Function

    <Fact>
    Public Sub The_panel_has_its_own_overflow_id_and_its_own_explicit_pin_case()
        Dim panel As String = Source("Main_Form.DocumentPanel.vb")
        Assert.Contains("OverflowId = ""btn_Overflow_3""", panel)
        Assert.Contains("Private Const Document_Panel_Name As String = ""chrome_Document""", panel)
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Assert.Contains("modern_Preferences.ChromeDocumentPinned", chrome)
        Dim autoHide As String = Source(Path.Combine("Chrome", "ChromeAutoHide.vb"))
        Assert.Contains("chrome_Document", autoHide)
    End Sub

    <Fact>
    Public Sub The_panel_is_in_the_chrome_list_only_while_a_document_is_on_screen()
        Dim panel As String = Source("Main_Form.DocumentPanel.vb")
        Assert.Contains("chrome_Panels.Insert(DocumentPanelInsertIndex(), panel)", panel)
        Assert.Contains("chrome_Panels.Remove(panel)", panel)
        ' The teardown reaches the window even when it is not in the list.
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Assert.Contains("TearDownDocumentPanel()", chrome)
    End Sub

    <Fact>
    Public Sub Both_new_source_files_are_in_the_net48_project_list()
        Dim root As New DirectoryInfo(AppContext.BaseDirectory)
        While root IsNot Nothing AndAlso Not File.Exists(Path.Combine(root.FullName, "src", "FastMediaSorter.vbproj"))
            root = root.Parent
        End While
        Assert.NotNull(root)
        Dim project As String = File.ReadAllText(Path.Combine(root.FullName, "src", "FastMediaSorter.vbproj"))
        Assert.Contains("<Compile Include=""Chrome\ChromeEditBox.vb"" />", project)
        Assert.Contains("<Compile Include=""Main_Form.DocumentPanel.vb"">", project)
    End Sub
End Class
#End If
