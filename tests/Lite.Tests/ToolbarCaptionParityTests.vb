#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports Xunit

''' <summary>
''' The toolbar captions the x86 viewer shows (audit 046, B-06). The mainline draws its
''' buttons from the icon catalog; the x86 fallback keeps its Unicode captions under a
''' recorded exception (spec 027 §3.3) - and a shared-code edit once replaced the block that
''' set them with a mainline-only call, so the x86 toolbar fell back to the Designer's
''' placeholders ("RE", "(Y)Rnd>", "R>"). Nothing but a source check can see that from the
''' mainline test leg, so this reads the source.
''' </summary>
Public Class ToolbarCaptionParityTests

    Private Shared ReadOnly LegacyCaptions As String() = {
        "btn_Review.Text = ""⟳""", "btn_Panel.Text = ""▦""", "btn_Full_Screen.Text = ""⛶""",
        "btn_Slideshow.Text = ""▶▶""", "btn_Random_Slideshow.Text = ""⤮▶""", "btn_Next_Random.Text = ""⚄""",
        "btn_Rename.Text = ""✎""", "btn_RecentFiles.Text = ""▾"""}

    <Fact>
    Public Sub The_x86_branch_still_sets_every_legacy_caption_and_the_mainline_keeps_the_uncatalogued_one()
        Dim source As String = File.ReadAllText(Path.Combine(FindSourceDir(), "Main_Form.ModernLayout.vb"))

        ' The x86 block: from the "#If NETFRAMEWORK Then" that precedes the captions to its #Else.
        Dim at As Integer = source.IndexOf(LegacyCaptions(0), StringComparison.Ordinal)
        Assert.True(at > 0, "The legacy caption block is gone from Main_Form.ModernLayout.vb.")
        Dim opens As Integer = source.LastIndexOf("#If NETFRAMEWORK Then", at, StringComparison.Ordinal)
        Dim elseAt As Integer = source.IndexOf("#Else", at, StringComparison.Ordinal)
        Dim endsAt As Integer = source.IndexOf("#End If", at, StringComparison.Ordinal)
        Assert.True(opens >= 0 AndAlso elseAt > at AndAlso endsAt > elseAt, "The captions are not behind an #If NETFRAMEWORK / #Else seam.")
        Assert.True(source.IndexOf("#End If", opens, StringComparison.Ordinal) > at,
                    "An #End If closes the NETFRAMEWORK block before the captions.")

        Dim legacy As String = source.Substring(opens, elseAt - opens)
        For Each caption As String In LegacyCaptions
            Assert.Contains(caption, legacy)
        Next
        Assert.Contains("glyph_Font", legacy)

        ' The mainline half: the random slideshow has no catalog glyph yet and keeps its own.
        Dim mainline As String = source.Substring(elseAt, endsAt - elseAt)
        Assert.Contains("btn_Random_Slideshow.Text = ""⤮▶""", mainline)
        Assert.Contains("ApplyCatalogToolbarGlyphs()", mainline)
    End Sub

    Private Shared Function FindSourceDir() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then
                Return candidate
            End If
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src/ not found above " & AppContext.BaseDirectory)
    End Function

End Class
#End If
