Option Strict On

Imports Xunit

''' <summary>
''' The pure rules of ticket 047's file operations, on both shipped runtimes.
''' C-18: a rename composed "folder + input + extension" with Path.Combine, whose rooted
''' second argument wins - "D:\other\x" moved the file to another drive.
''' C-13: a completion, rollback or undo edited whatever list was on screen, not the one
''' the operation came from.
''' </summary>
Public Class FileOpRulesTests

    <Theory>
    <InlineData("holiday")>
    <InlineData("IMG_0001 (2)")>
    <InlineData("фото из отпуска")>
    <InlineData("a.b.c")>
    <InlineData(" leading space")>
    Public Sub A_plain_name_is_accepted(name As String)
        Assert.True(FileOpRules.IsValidNewFileName(name))
    End Sub

    <Theory>
    <InlineData("..\x")>
    <InlineData("sub\x")>
    <InlineData("sub/x")>
    <InlineData("D:\other\x")>
    <InlineData("C:x")>
    <InlineData("\\server\share\x")>
    <InlineData("a*b")>
    <InlineData("a?b")>
    <InlineData("a<b")>
    <InlineData("a>b")>
    <InlineData("a|b")>
    <InlineData("a""b")>
    <InlineData(".")>
    <InlineData("..")>
    <InlineData("")>
    <InlineData("   ")>
    Public Sub A_name_that_is_a_path_or_carries_a_forbidden_character_is_refused(name As String)
        Assert.False(FileOpRules.IsValidNewFileName(name))
    End Sub

    <Fact>
    Public Sub Nothing_is_refused()
        Assert.False(FileOpRules.IsValidNewFileName(Nothing))
    End Sub

    <Fact>
    Public Sub A_control_character_is_refused()
        Assert.False(FileOpRules.IsValidNewFileName("a" & ChrW(1) & "b"))
    End Sub

    ' ------------------------------------------------------------ the list stamp ----

    <Fact>
    Public Sub The_same_list_matches()
        Assert.True(FileOpRules.ListStampMatches(7, 7))
    End Sub

    ''' <summary>The folder was changed, or the same folder rescanned: another list, whose
    ''' positions mean something else.</summary>
    <Fact>
    Public Sub A_replaced_list_does_not_match()
        Assert.False(FileOpRules.ListStampMatches(7, 8))
    End Sub

    ''' <summary>Generation 0 is "no list yet" - an operation from before any list, or a
    ''' screen with none, never matches, even itself.</summary>
    <Fact>
    Public Sub No_list_matches_nothing()
        Assert.False(FileOpRules.ListStampMatches(0, 0))
        Assert.False(FileOpRules.ListStampMatches(0, 3))
        Assert.False(FileOpRules.ListStampMatches(3, 0))
    End Sub

End Class
