Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' Holds the viewer's string table to the script and reading-order rules of
''' <see cref="LocaleScriptRules"/> (ticket L0290): a Bengali row with a Devanagari letter, a
''' Hindi row with a Bengali one, an Urdu row ending in a danda, an Arabic or Urdu sentence stored
''' in visual order (it comes out reversed in a window with RightToLeft = Yes), a file-dialog filter
''' whose patterns moved.
'''
''' Runs on BOTH legs (net48 + net10): the tables are shared, only Codes/Names are cut by the
''' #If NETFRAMEWORK seam. The Share Manager's tables have the same facts in Companion.Tests.
''' </summary>
<Collection("LocalizationState")>
Public Class LocalizationScriptTests

    <Fact>
    Public Sub Every_translation_keeps_to_the_script_and_reading_order_of_its_language()
        Dim inspected As Integer
        Dim findings = LocaleScriptRules.Audit(Localization.All, inspected)

        Assert.True(inspected > 15000, "Only " & inspected & " translations were inspected - the tables did not all load.")
        Assert.True(findings.Count = 0,
                    "Script or reading-order faults (" & findings.Count & "):" & vbLf & String.Join(vbLf, findings.Take(40)))
    End Sub

    ''' <summary>
    ''' A rule that finds nothing in a clean table proves nothing until it is seen finding the
    ''' fault it was written for - each defect of the ticket, written as code points.
    ''' </summary>
    <Fact>
    Public Sub The_rules_catch_the_faults_they_were_written_for()
        Dim bengaliWithDevanagariRa = ChrW(&H9AA) & ChrW(&H9CD) & ChrW(&H930) & ChrW(&H9A4)
        Dim bengaliProper = ChrW(&H9AA) & ChrW(&H9CD) & ChrW(&H9B0) & ChrW(&H9A4)
        Assert.NotNull(LocaleScriptRules.WrongScript("bn", bengaliWithDevanagariRa))
        Assert.Null(LocaleScriptRules.WrongScript("bn", bengaliProper))

        Dim hindiWithBengaliRa = ChrW(&H92A) & ChrW(&H94D) & ChrW(&H9B0) & ChrW(&H924)
        Assert.NotNull(LocaleScriptRules.WrongScript("hi", hindiWithBengaliRa))

        ' The danda is the full stop of Bengali and Hindi, never of Urdu; the Urdu full stop is never Bengali's.
        Dim danda = ChrW(&H964).ToString()
        Dim urduStop = ChrW(&H6D4).ToString()
        Assert.Null(LocaleScriptRules.WrongScript("bn", bengaliProper & danda))
        Assert.Null(LocaleScriptRules.WrongScript("hi", danda))
        Assert.NotNull(LocaleScriptRules.WrongScript("ur", ChrW(&H6A9) & danda))
        Assert.NotNull(LocaleScriptRules.WrongScript("bn", bengaliProper & urduStop))

        ' A sentence stored in visual order: the mark leads, the Latin name sits where it was typed.
        Dim arabicWord = ChrW(&H628) & ChrW(&H62B)
        Assert.NotNull(LocaleScriptRules.BadLineStart("." & arabicWord, "Broadcast."))
        Assert.NotNull(LocaleScriptRules.BadLineStart(":" & arabicWord, "Address:"))
        Assert.NotNull(LocaleScriptRules.BadLineStart("!" & arabicWord & vbCrLf & vbCrLf & arabicWord, "Done!"))
        ' A file-extension token opening a line needs a left-to-right mark before its dot.
        Assert.NotNull(LocaleScriptRules.BadLineStart(".fdd " & arabicWord, ".fdd disks.."))
        Assert.Null(LocaleScriptRules.BadLineStart(ChrW(&H200E) & ".fdd " & arabicWord, ".fdd disks.."))
        Assert.Null(LocaleScriptRules.BadLineStart(arabicWord & ".", "Broadcast."))
        ' A leading mark the English source has too is a prefix on purpose ("..and 3 more").
        Assert.Null(LocaleScriptRules.BadLineStart(".." & arabicWord, "..and 3 more"))

        Assert.NotNull(LocaleScriptRules.UnbalancedBrackets("(" & arabicWord & " (" & arabicWord, "Gateway (port {0})"))
        Assert.Null(LocaleScriptRules.UnbalancedBrackets(arabicWord & " (" & arabicWord & ")", "Gateway (port {0})"))

        Dim english = "Media files|*.mp4;*.mkv|All files (*.*)|*.*"
        Assert.Null(LocaleScriptRules.FilterPatternsDiffer(arabicWord & "|*.mp4;*.mkv|" & arabicWord & " (*.*)|*.*", english))
        Assert.NotNull(LocaleScriptRules.FilterPatternsDiffer("*.*|(*.*) " & arabicWord & "|*.mp4;*.mkv|" & arabicWord, english))
        Assert.Null(LocaleScriptRules.FilterPatternsDiffer(arabicWord, "Plain text, no filter"))

        ' The pt column is pt-BR (ticket L0291): a European word or construction is a finding.
        Assert.NotNull(LocaleScriptRules.EuropeanPortugueseWord("Não foi possível abrir o ficheiro"))
        Assert.NotNull(LocaleScriptRules.EuropeanPortugueseWord("Introduza a palavra-passe"))
        Assert.NotNull(LocaleScriptRules.EuropeanPortugueseWord("A ligação está a carregar.."))
        Assert.NotNull(LocaleScriptRules.EuropeanPortugueseWord("Utilizador: {0}"))
        Assert.Null(LocaleScriptRules.EuropeanPortugueseWord("Não foi possível abrir o arquivo"))
        Assert.Null(LocaleScriptRules.EuropeanPortugueseWord("Digite a senha do usuário"))
        Assert.Null(LocaleScriptRules.EuropeanPortugueseWord("Carregando registros.."))
    End Sub

    ''' <summary>
    ''' A path, a file name or an address placed in an Arabic or Urdu line (ticket L0292). The table rows are in
    ''' logical order; the fault was the value: its neutral edge takes the paragraph direction, so "\\server\share"
    ''' read "server\share\\". The value is embedded left to right (LRE..PDF, measured on screen: U+200E fixes only
    ''' the edges, the isolates U+2066..U+2069 draw as boxes in TextRenderer) and only in a right-to-left language.
    ''' </summary>
    <Fact>
    Public Sub A_left_to_right_value_is_embedded_only_in_a_right_to_left_language()
        Dim value = "\\server\share\photos"
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = "en"
            Assert.Equal(value, Localization.LtrValue(value))
            Assert.Null(Localization.LtrValue(Nothing))
            Assert.Equal("", Localization.LtrValue(""))
#If Not NETFRAMEWORK Then
            For Each code As String In New String() {"ar", "ur"}
                Localization.CurrentCode = code
                Assert.True(Localization.IsRightToLeft(), code & " is written right to left")
                Dim wrapped As String = Localization.LtrValue(value)
                Assert.Equal(ChrW(&H202A) & value & ChrW(&H202C), wrapped)
                Assert.Equal(value, Localization.PlainValue(wrapped))
                Assert.Equal("", Localization.LtrValue(""))
            Next
#End If
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ''' <summary>
    ''' The fix sits where label and value meet, not in the tables: the three places the ticket names read their
    ''' value through <c>Localization.LtrValue</c>, and the details window copies text without the marks.
    ''' </summary>
    <Fact>
    Public Sub The_places_that_compose_a_label_with_a_path_embed_the_value()
        Dim details As String = SourceText("File_Details_Form.vb")
        Assert.Contains("Localization.LtrValue(row.Value)", details)
        Assert.Contains("Clipboard.SetText(Localization.PlainValue(box.Text))", details)
        Assert.Contains("Localization.LtrValue(ResourceLocator.Build(resource, resource.RootPath))", SourceText("Resources\ResourceMessages.vb"))
        Dim folders As String = SourceText("FastMediaSorterCompanion\Forms\MainWindow.Folders.vb")
        Assert.Contains("it.SubItems.Add(Localization.LtrValue(path))", folders)
        Assert.Contains("it.SubItems.Add(Localization.LtrValue(host))", folders)
    End Sub

    Private Shared Function SourceText(relative As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", relative)
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src\" & relative & " not found above " & AppContext.BaseDirectory)
    End Function

End Class
