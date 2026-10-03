#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Drawing
Imports System.Windows.Forms
Imports Xunit

Public Class ChromeCaptionFitTests
    <Fact>
    Public Sub Captions_fit_measured_slots_in_all_thirteen_script_fonts_at_four_monitor_scales()
        ' Read the localization table without mutating the process-wide language.
        Dim russian As String() = {"Имя", "Дата", "Размер", "Удалить", "Язык интерфейса", "Выбрать каталог..", "Настройки"}
        Assert.Equal(13, Localization.Codes.Length)
        For index As Integer = 0 To Localization.Codes.Length - 1
            Dim code As String = Localization.Codes(index)
            Dim family As String = If(code = "hi" OrElse code = "bn", "Nirmala UI", If(code = "zh", "Microsoft YaHei UI", "Segoe UI"))
            For Each factor As Single In New Single() {1, 1.25F, 1.5F, 1.75F}
                Using font As New Font(family, UiScale.Px(ChromeMetrics.TextSize, factor), GraphicsUnit.Pixel),
                      symbols As New Font("Segoe UI Symbol", UiScale.Px(ChromeMetrics.SymbolSize, factor), GraphicsUnit.Pixel),
                      surface As New ChromeSurface()
                    surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), factor, code = "ar" OrElse code = "ur", font, symbols)
                    Dim items As New List(Of ChromeItem)()
                    For Each key In russian
                        Dim caption As String = key
                        Dim values As String() = Localization.All.Where(Function(pair) pair.Key = key).Select(Function(pair) pair.Value).FirstOrDefault()
                        If values IsNot Nothing AndAlso index > 0 Then caption = If(values(index - 1).Length > 0, values(index - 1), values(0))
                        items.Add(New ChromeItem(key, ChromeItemKind.Text) With {.Caption = caption})
                    Next
                    surface.SetItems(items)
                    surface.Size = surface.PreferredPanelSize()
                    For Each item In items
                        Dim measured As Size = TextRenderer.MeasureText(item.Caption, font, New Size(Integer.MaxValue, Integer.MaxValue),
                            TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix)
                        Assert.True(item.Bounds.Width >= measured.Width, code & " caption clipped: " & item.Caption)
                        Assert.True(item.Bounds.Height >= measured.Height, code & " script clipped: " & item.Caption)
                    Next
                    Assert.Empty(surface.OverflowItems)
                End Using
            Next
        Next
    End Sub
End Class
#End If
