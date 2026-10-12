Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' WINDOWS-UI 0.4 section 11 in the Share Manager: every command button that has a caption carries a glyph before it,
''' and an answer button of a dialog is hued by its polarity. The windows are built the way the program builds them and
''' walked; a button that is on purpose without one (the password eye keeps its own picture) is named here, so a new
''' button without a glyph fails this test instead of shipping bare.
''' With FMS_SHOT_DIR set the same windows are rendered, light and dark, to PNG files there (the acceptance evidence).
''' </summary>
<Collection("AppFileLogger")>
Public Class ButtonGlyphCoverageTests

    ''' <summary>Buttons that keep their own picture or are glyph-only on purpose (a dated exception in the registry).</summary>
    Private Shared ReadOnly OwnPicture As New HashSet(Of String)(StringComparer.Ordinal) From {
        "btnRevealPassword", "btnShowQr", "btnCopySecret"}

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
        Assert.True(thread.Join(120000), "the UI check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    Private Shared Sub Collect(parent As Control, into As List(Of Button))
        For Each c As Control In parent.Controls
            Dim b As Button = TryCast(c, Button)
            If b IsNot Nothing Then into.Add(b)
            Collect(c, into)
        Next
    End Sub

    Private Shared Function Bare(form As Form) As List(Of String)
        Dim all As New List(Of Button)()
        Collect(form, all)
        Dim missing As New List(Of String)()
        For Each b As Button In all
            If String.IsNullOrWhiteSpace(b.Text) Then Continue For
            If OwnPicture.Contains(b.Name) Then Continue For
            ' The settings nav tree's subordinate rows are caption-only by design (spec 210):
            ' the page row carries the icon, the item is its indented text.
            If b.Name.StartsWith("managerSettingsNavItem_", StringComparison.Ordinal) Then Continue For
            ' The navigation rail and the header tools render their own catalog glyph (ThemePalette-aware, 20 px); a
            ' picture is what this walk asks for, whichever mechanism drew it.
            If Not ButtonGlyphs.CarriesGlyph(b) AndAlso b.Image Is Nothing Then missing.Add(form.GetType().Name & "/" & If(b.Name.Length > 0, b.Name, "(unnamed)") & " '" & b.Text & "' [glyph=" & ButtonGlyphs.GlyphOf(b) & ", image=" & (b.Image IsNot Nothing).ToString() & ", enabled=" & b.Enabled.ToString() & "]")
        Next
        Return missing
    End Function

    <Fact>
    Public Sub Every_captioned_command_button_of_the_walked_windows_has_a_glyph()
        OnSta(Sub()
                  ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                  Dim missing As New List(Of String)()
                  Using f As New Share_Enable_Form()
                      missing.AddRange(Bare(f))
                  End Using
                  Using f As New Share_Status_Form()
                      missing.AddRange(Bare(f))
                  End Using
                  Using f As New Share_Settings_Form()
                      missing.AddRange(Bare(f))
                  End Using
                  Assert.True(missing.Count = 0, "command buttons without a glyph: " & String.Join("; ", missing))
              End Sub)
    End Sub

    <Fact>
    Public Sub The_enable_dialog_answers_are_an_amber_install_and_a_red_cancel()
        OnSta(Sub()
                  ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                  Using f As New Share_Enable_Form()
                      Dim all As New List(Of Button)()
                      Collect(f, all)
                      Dim accept As Button = TryCast(f.AcceptButton, Button)
                      Dim cancel As Button = TryCast(f.CancelButton, Button)
                      Assert.NotNull(accept)
                      Assert.NotNull(cancel)
                      Assert.Equal(ButtonGlyphRole.Caution, ButtonGlyphs.RoleOf(accept))
                      Assert.Equal(ButtonGlyphRole.Dismissing, ButtonGlyphs.RoleOf(cancel))
                      Assert.Equal("action.cancel", ButtonGlyphs.GlyphOf(cancel))
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Render_the_windows_light_and_dark_when_a_folder_is_given()
        Dim dir As String = Environment.GetEnvironmentVariable("FMS_SHOT_DIR")
        If String.IsNullOrEmpty(dir) Then Return
        Directory.CreateDirectory(dir)
        OnSta(Sub()
                  ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                  For Each dark As Boolean In New Boolean() {False, True}
                      Dim palette As ThemePalette = ThemePalette.ForDark(dark)
                      Dim tag As String = If(dark, "dark", "light")
                      Dim shots As New List(Of Tuple(Of String, Form))() From {
                          Tuple.Create("share-enable", CType(New Share_Enable_Form(), Form)),
                          Tuple.Create("share-status", CType(New Share_Status_Form(), Form))}
                      For Each shot In shots
                          Dim f As Form = shot.Item2
                          Try
                              f.StartPosition = FormStartPosition.Manual
                              f.Location = New Point(-20000, -20000)
                              f.ShowInTaskbar = False
                              f.Show()
                              ThemeApplier.ApplyTree(f, palette)
                              ButtonGlyphs.UsePalette(palette)
                              Application.DoEvents()
                              Using bmp As New Bitmap(f.Width, f.Height)
                                  f.DrawToBitmap(bmp, New Rectangle(Point.Empty, f.Size))
                                  bmp.Save(Path.Combine(dir, shot.Item1 & "-" & tag & ".png"), System.Drawing.Imaging.ImageFormat.Png)
                              End Using
                          Finally
                              f.Close()
                              f.Dispose()
                          End Try
                      Next
                  Next
                  ButtonGlyphs.UsePalette(ThemePalette.Light)
              End Sub)
    End Sub

End Class
