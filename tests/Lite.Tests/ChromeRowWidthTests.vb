#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' How wide the second toolbar row is in each of the 13 interface languages (125 UX-313d). The old caption
''' check measured seven short words; the row itself - the counter, the three buttons that carry words (previous,
''' next, delete), the translate pair, the tracks word and the glyph-only buttons between them - was never
''' measured, so it could grow a long caption in one language and the "more" menu would take the end of the row
''' on a laptop screen without a test saying so.
'''
''' The inventory below mirrors <c>BuildChromePanels</c> / <c>SyncChromeItem</c> in Main_Form.Chrome.vb for the
''' items that carry a measurable width: which carry words, which are a glyph and which reserve a text. A new
''' button on the row is a new line here, and the budget is the thing that says whether it still fits.
''' </summary>
Public Class ChromeRowWidthTests

    ''' <summary>The widest the row may be, in logical px (the row's natural width: items, gaps, the corner pin
    ''' and the panel padding), with the largest set of buttons a single file ever shows at once. 1366 px is the
    ''' narrowest screen the viewer is normally run on; at 125 % that is 1093 logical px, and the row must sit in
    ''' it whole, so that the "more" menu is for a narrow window and not for a laptop. Measured on 2026-10-13 at
    ''' scale 1: 906 (hi) ... 1052 (fr), 1043 for ru, 929 for en - about one more glyph button of room left.</summary>
    Private Const RowBudgetLogical As Integer = 1093

    Private Shared Function FamilyFor(code As String) As String
        If code = "hi" OrElse code = "bn" Then Return "Nirmala UI"
        If code = "zh" Then Return "Microsoft YaHei UI"
        Return "Segoe UI"
    End Function

    ''' <summary>The words of one key in language number <paramref name="index"/> (0 = the Russian source).</summary>
    Private Shared Function Word(key As String, index As Integer) As String
        If index = 0 Then Return key
        Dim values As String() = Localization.All.Where(Function(pair) pair.Key = key).Select(Function(pair) pair.Value).FirstOrDefault()
        If values Is Nothing Then Return key
        Return If(values(index - 1).Length > 0, values(index - 1), values(0))
    End Function

    ''' <summary>The second row as one file shows it, the most it ever shows: a still picture with the OCR in
    ''' automatic mode (the translate button's longest caption), the browser companion, the editor, and a zoom
    ''' value. Tracks and "to video" belong to other kinds of file and are not on a still picture - they are
    ''' the places a row keeps empty and give up first (125 UX-313b), so they are not part of this width.</summary>
    Private Shared Function Row2(index As Integer) As List(Of ChromeItem)
        Dim counter As String = String.Format(Word("{0} из {1}", index), 9999, 9999)
        Dim translate As String = Word("Перевод", index) & " " & Word("(авто)", index)
        Return New List(Of ChromeItem) From {
            New ChromeItem("btn_RecentFiles", ChromeItemKind.MenuButton) With {.GlyphId = "source.recent"},
            New ChromeItem("lbl_File_Number", ChromeItemKind.Text) With {.Caption = counter, .ReservedText = counter},
            New ChromeItem("btn_Prev_File", ChromeItemKind.Button) With {.GlyphId = "media.previous", .ShowCaptionWithGlyph = True, .Caption = Word("Предыдущий файл", index)},
            New ChromeItem("btn_Next_File", ChromeItemKind.Button) With {.GlyphId = "media.next", .ShowCaptionWithGlyph = True, .Caption = Word("Следующий файл", index)},
            New ChromeItem("btn_Next_Random", ChromeItemKind.Button) With {.GlyphId = "media.random"},
            New ChromeItem("btn_Random_Slideshow", ChromeItemKind.Button) With {.Caption = "⤮▶", .CaptionIsSymbol = True},
            New ChromeItem("btn_Slideshow", ChromeItemKind.Toggle) With {.GlyphId = "media.slideshow"},
            New ChromeItem("btn_Rename", ChromeItemKind.Button) With {.GlyphId = "action.rename"},
            New ChromeItem("bt_Delete", ChromeItemKind.Button) With {.GlyphId = "action.delete", .ShowCaptionWithGlyph = True, .Caption = Word("Удалить", index)},
            New ChromeItem("lbl_Zoom", ChromeItemKind.Text) With {.Caption = "1600 %", .ReservedText = "1600 %"},
            New ChromeItem("btn_Translate", ChromeItemKind.Button) With {.Caption = translate},
            New ChromeItem("btn_TranslateBrowser", ChromeItemKind.Button) With {.Caption = Word("В браузере", index)},
            New ChromeItem("btn_Edit", ChromeItemKind.Button) With {.GlyphId = "action.edit"},
            New ChromeItem("btn_Image_Menu", ChromeItemKind.Button) With {.GlyphId = "action.more"}}
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
        Assert.True(thread.Join(60000), "Row width check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    <Fact>
    Public Sub The_second_row_fits_a_1366_pixel_laptop_at_125_percent_in_all_thirteen_languages()
        Assert.Equal(13, Localization.Codes.Length)
        OnSta(Sub()
                  Dim over As New List(Of String)()
                  Dim widest As Integer = 0
                  For index As Integer = 0 To Localization.Codes.Length - 1
                      Dim code As String = Localization.Codes(index)
                      Using font As New Font(FamilyFor(code), UiScale.Px(ChromeMetrics.TextSize, 1.0F), GraphicsUnit.Pixel),
                            symbols As New Font("Segoe UI Symbol", UiScale.Px(ChromeMetrics.SymbolSize, 1.0F), GraphicsUnit.Pixel),
                            surface As New ChromeSurface()
                          surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, code = "ar" OrElse code = "ur", font, symbols)
                          Dim pin As New ChromeItem("chrome_pin_chrome_Row2", ChromeItemKind.Toggle) With {.IsPin = True}
                          surface.PinItem = pin
                          surface.SetItems(Row2(index))
                          Dim natural As Integer = surface.PreferredPanelSize().Width
                          If natural > widest Then widest = natural
                          If natural > RowBudgetLogical Then over.Add(code & "=" & natural.ToString())
                      End Using
                  Next
                  Assert.True(over.Count = 0, "row 2 is wider than " & RowBudgetLogical.ToString() & " logical px in: " & String.Join(", ", over) &
                              " (widest of all: " & widest.ToString() & ")")
              End Sub)
    End Sub

    <Fact>
    Public Sub At_the_narrowest_screen_only_air_and_never_a_button_is_given_up_first()
        ' The row of a still picture with the kept places of the other kinds (tracks, to video, zoom) in it: at the
        ' budget, nothing may collapse into the menu, whatever the language - the places go first (125 UX-313b).
        Assert.Equal(13, Localization.Codes.Length)
        OnSta(Sub()
                  Dim collapsed As New List(Of String)()
                  For index As Integer = 0 To Localization.Codes.Length - 1
                      Dim code As String = Localization.Codes(index)
                      Using font As New Font(FamilyFor(code), UiScale.Px(ChromeMetrics.TextSize, 1.0F), GraphicsUnit.Pixel),
                            symbols As New Font("Segoe UI Symbol", UiScale.Px(ChromeMetrics.SymbolSize, 1.0F), GraphicsUnit.Pixel),
                            surface As New ChromeSurface()
                          surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, code = "ar" OrElse code = "ur", font, symbols)
                          surface.PinItem = New ChromeItem("chrome_pin_chrome_Row2", ChromeItemKind.Toggle) With {.IsPin = True}
                          Dim items As List(Of ChromeItem) = Row2(index)
                          ' The two buttons of the other kinds, away but kept, between the editor and the menu.
                          Dim at As Integer = items.FindIndex(Function(item) item.Id = "btn_Image_Menu")
                          items.Insert(at, New ChromeItem("btn_Tracks", ChromeItemKind.Button) With {
                              .GlyphId = "media.audio-track", .ShowCaptionWithGlyph = True, .Caption = Word("Дорожки", index), .Visible = False, .KeepsSlotWhenHidden = True})
                          items.Insert(at + 1, New ChromeItem("btn_ToVideo", ChromeItemKind.Button) With {
                              .GlyphId = "action.convert-to-video", .Visible = False, .KeepsSlotWhenHidden = True})
                          surface.SetItems(items)
                          surface.Size = New Size(RowBudgetLogical, surface.PreferredPanelSize().Height)
                          surface.LayoutItems()
                          If surface.OverflowItems.Count > 0 Then collapsed.Add(code & ":" & String.Join("/", surface.OverflowItems.Select(Function(item) item.Id)))
                      End Using
                  Next
                  Assert.True(collapsed.Count = 0, "a button went into the menu at " & RowBudgetLogical.ToString() & " px: " & String.Join(", ", collapsed))
              End Sub)
    End Sub
End Class
#End If
