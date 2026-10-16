#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' APP-SETTINGS rules 5 and 7 and ICON-EXTERNAL rule 6 for the viewer's settings window (source reads, like
''' SettingsLayoutTests - no form is built): the language selector is on the FIRST page, lists every language by
''' its endonym and never by a flag, and every drop-down list is drawn at one scaled item height.
''' </summary>
Public Class LanguageSelectorContractTests

    Private Shared Function Source(fileName As String) As String
        Return LocalizationCoverageTests.BlankComments(File.ReadAllText(Path.Combine(SourceDir(), fileName)))
    End Function

    ''' <summary>The text with every x86-only block (#If NETFRAMEWORK .. #Else/#End If) removed.</summary>
    Private Shared Function WithoutNet48Blocks(text As String) As String
        Return Regex.Replace(text, "#If NETFRAMEWORK Then.*?(?:#Else|#End If)", "", RegexOptions.Singleline)
    End Function

    <Fact>
    Public Sub The_language_selector_is_the_first_row_of_the_first_page()
        Dim layout As String = Source("Table_Form.ModernLayout.vb")
        Dim data As Match = Regex.Match(layout, "Sub BuildModernDataLayout\b.*?End Sub", RegexOptions.Singleline)
        Assert.True(data.Success)
        Dim groups As String = Source("Table_Form.SettingsGroups.vb")
        Assert.Contains("Tab_Control.TabPages.Insert(0, modernGeneralPage)", layout)
        Assert.Contains("AddUiLanguageRow(destinations)", data.Value)
        Assert.Matches("AddSettingsGroup\(""general"", ""basic"", .*?""interface_language"", ""ui_theme""\)", groups)
        Assert.Contains("Dim body = settingsGroups(ownerId).Body", groups)
        Assert.Contains("If row.Host.Parent IsNot body Then body.Controls.Add(row.Host)", groups)
        Assert.Contains("RegisterSettingsRow(""interface_language""", data.Value)
        Assert.DoesNotContain("""interface_language"", btn_Language", data.Value)
    End Sub

    <Fact>
    Public Sub No_modern_language_list_shows_a_flag()
        For Each fileName As String In {"Main_Form.Chrome.vb", "Table_Form.ExpandedSettings.vb", "Table_Form.ModernLayout.vb",
                                        "Main_Form.Localization.vb", "Table_Form.vb", "Table_Form.Ocr.vb"}
            Dim text As String = WithoutNet48Blocks(Source(fileName))
            Assert.False(text.Contains("FlagImages"), fileName & " draws a flag on the net10 build (ICON-EXTERNAL rule 6: a language is its endonym).")
        Next
    End Sub

    <Fact>
    Public Sub The_selector_offers_every_shipped_language_by_its_endonym_and_applies_it_at_once()
        Dim text As String = Source("Table_Form.ExpandedSettings.vb")
        ' Up to the row's own AddSettingRow call: the handlers inside are lambdas with their own End Sub.
        Dim row As Match = Regex.Match(text, "Sub AddUiLanguageRow\b.*?""interface_language""", RegexOptions.Singleline)
        Assert.True(row.Success)
        Assert.Contains("Localization.Names(index)", row.Value)
        Assert.Contains("Localization.Codes(index)", row.Value)
        Assert.Contains("Main_Form.SetUiLanguage(", row.Value)
        Assert.Contains("expandedRefreshActions.Add(refresh)", row.Value)
        Assert.Contains("AccessibleName", row.Value)
    End Sub

    <Fact>
    Public Sub Every_drop_down_list_is_drawn_at_a_scaled_item_height_in_a_pixel_font()
        Dim text As String = Source("Table_Form.ModernLayout.vb")
        Assert.Contains("SettingsPixelFont(""Segoe UI"", 12)", text)
        ' The row height may be a literal or a named Const (it is the Const now): resolve
        ' either, so the contract survives the rename without going blind to the value.
        Dim height = Regex.Match(text, "combo\.ItemHeight\s*=\s*Math\.Max\(Sp\((\w+)\)")
        Assert.True(height.Success)
        Dim token As String = height.Groups(1).Value
        Dim logical As Integer
        If Not Integer.TryParse(token, Globalization.CultureInfo.InvariantCulture, logical) Then
            Dim declared = Regex.Match(text, "Const\s+" & Regex.Escape(token) & "\s+As\s+Integer\s*=\s*(\d+)")
            Assert.True(declared.Success, token & " is neither a number nor a Const in this file.")
            logical = Integer.Parse(declared.Groups(1).Value, Globalization.CultureInfo.InvariantCulture)
        End If
        ' The row itself is 38 logical px; the closed field adds the control's own 6 px
        ' frame, which keeps the field the user actually points at at the 44 the old
        ' literal pinned - so 38 is the floor the row must not go under.
        Assert.True(logical >= 38)
        Assert.Contains("DrawMode.OwnerDrawFixed", text)
    End Sub

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("Could not locate the src\ directory from the test binary.")
    End Function

End Class
#End If
