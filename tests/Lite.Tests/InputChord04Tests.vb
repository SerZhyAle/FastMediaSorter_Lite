#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Windows.Forms
Imports Xunit

Public Class InputChord04Tests
    <Theory>
    <InlineData("Num +", "NumpadAdd")>
    <InlineData("Num 5", "Numpad5")>
    <InlineData("OemOpenBrackets", "BracketLeft")>
    <InlineData("OemCloseBrackets", "BracketRight")>
    <InlineData("OemPeriod", "Period")>
    <InlineData("Oemcomma", "Comma")>
    <InlineData("Oemplus", "Equal")>
    <InlineData("OemMinus", "Minus")>
    <InlineData("OemPipe", "Backslash")>
    <InlineData("MediaNextTrack", "MediaTrackNext")>
    <InlineData("MediaPreviousTrack", "MediaTrackPrevious")>
    <InlineData("VolumeUp", "AudioVolumeUp")>
    <InlineData("VolumeDown", "AudioVolumeDown")>
    <InlineData("VolumeMute", "AudioVolumeMute")>
    Public Sub Legacy_tokens_keep_their_physical_key_and_save_in_the_current_vocabulary(legacy As String, canonical As String)
        Assert.Equal(canonical, CustomHotkeys.Canonical(legacy))
        Assert.Equal(CustomHotkeys.Parse(legacy), CustomHotkeys.Parse(canonical))
        Assert.NotEqual(Keys.None, CustomHotkeys.Parse(canonical))
        Dim loaded = CustomHotkeys.Load("{""undo"":""Ctrl+" & legacy & """}")
        Dim saved = CustomHotkeys.Save(loaded)
        Assert.Equal(CustomHotkeys.Parse("Ctrl+" & legacy), CustomHotkeys.Parse(CustomHotkeys.Load(saved)("undo")))
        Assert.Contains(canonical, saved)
    End Sub

    ' VIEWER-KEYBOARD as INPUT-CHORD/PARITY 0.4 incorporates it (specification 252 T-3, the defaults half):
    ' F is the family's fullscreen key (F7 stays as the inherited alias), F4 alone is the picker, and G
    ' opens the thumbnail panel beside the viewer's F3.
    <Fact>
    Public Sub The_Family_Defaults_Fullscreen_F_Picker_F4_Panel_G_Are_The_Factory_Keys()
        Dim fullscreen = CustomHotkeys.FactoryCombos(CustomHotkeys.Find("fullScreen"))
        Assert.Contains("F", fullscreen)
        Assert.Contains("F7", fullscreen)

        Dim picker = CustomHotkeys.FactoryCombos(CustomHotkeys.Find("chooseFile"))
        Assert.Contains("F4", picker)
        Assert.DoesNotContain("F", picker)

        Dim panel = CustomHotkeys.FactoryCombos(CustomHotkeys.Find("imagePanel"))
        Assert.Contains("G", panel)
        Assert.Contains("F3", panel)
    End Sub
End Class
#End If
