#If Not NETFRAMEWORK Then
Option Strict On

Imports Microsoft.VisualBasic
Imports Xunit

''' <summary>
''' The stored half of specification 242 (T-5): the "Animated graphics through video" option
''' is ON until it is switched off, the remembered "No" to the FFmpeg offer (rule 22) is OFF
''' until it is given, both survive a profile round trip and an exported settings file, and
''' both are classified for the profile reset and the send-logs allow-list on purpose.
'''
''' Modern-only, like the option itself - ModernViewerPreferences.vb is whole-file
''' "#If Not NETFRAMEWORK", so this class compiles to nothing on the net48 leg.
'''
''' ModernViewerPreferences.Save() is deliberately NOT called (see ResumeLastPlaybackTests):
''' it writes the whole block into the live HKCU hive. The stored direction is proven by
''' writing the single profile value by hand and putting the previous one back.
''' </summary>
Public Class AnimatedViaVideoSettingTests

    Private Const EnabledKey As String = "AnimatedViaVideo"
    Private Const AskedKey As String = "AnimatedViaVideoFfmpegAsked"

    <Fact>
    Public Sub The_option_is_on_and_the_remembered_no_is_off_by_default()
        Dim defaults As New ModernViewerPreferences()
        Assert.True(defaults.AnimatedViaVideo)
        Assert.False(defaults.AnimatedViaVideoFfmpegAsked)
    End Sub

    ''' <summary>Absent key, then "0", then "1" for the option; the previous value is put back
    ''' whatever the assertions do. An absent key must read as the DEFAULT (on).</summary>
    <Fact>
    Public Sub The_profile_value_decides_the_option()
        Dim previous As String = Interaction.GetSetting(App_name, Second_App_Name, EnabledKey, "")
        Try
            DeleteKey(EnabledKey)
            Assert.True(ModernViewerPreferences.Load().AnimatedViaVideo)

            Interaction.SaveSetting(App_name, Second_App_Name, EnabledKey, "0")
            Assert.False(ModernViewerPreferences.Load().AnimatedViaVideo)

            Interaction.SaveSetting(App_name, Second_App_Name, EnabledKey, "1")
            Assert.True(ModernViewerPreferences.Load().AnimatedViaVideo)
        Finally
            Restore(EnabledKey, previous)
        End Try
    End Sub

    <Fact>
    Public Sub The_profile_value_decides_the_remembered_no()
        Dim previous As String = Interaction.GetSetting(App_name, Second_App_Name, AskedKey, "")
        Try
            DeleteKey(AskedKey)
            Assert.False(ModernViewerPreferences.Load().AnimatedViaVideoFfmpegAsked)

            Interaction.SaveSetting(App_name, Second_App_Name, AskedKey, "1")
            Assert.True(ModernViewerPreferences.Load().AnimatedViaVideoFfmpegAsked)

            Interaction.SaveSetting(App_name, Second_App_Name, AskedKey, "0")
            Assert.False(ModernViewerPreferences.Load().AnimatedViaVideoFfmpegAsked)
        Finally
            Restore(AskedKey, previous)
        End Try
    End Sub

    ''' <summary>Both are settings, so both are exported; an exported OFF survives the round
    ''' trip although it is the non-default state of a default-on option.</summary>
    <Fact>
    Public Sub Export_and_import_carry_both_values()
        Dim off As New ModernViewerPreferences() With {.AnimatedViaVideo = False, .AnimatedViaVideoFfmpegAsked = True}
        Dim back As ModernViewerPreferences = ModernViewerPreferences.FromJson(off.ExportJson())
        Assert.False(back.AnimatedViaVideo)
        Assert.True(back.AnimatedViaVideoFfmpegAsked)

        Dim defaults As New ModernViewerPreferences() With {.AnimatedViaVideo = True, .AnimatedViaVideoFfmpegAsked = False}
        Dim backOn As ModernViewerPreferences = ModernViewerPreferences.FromJson(defaults.ExportJson())
        Assert.True(backOn.AnimatedViaVideo)
        Assert.False(backOn.AnimatedViaVideoFfmpegAsked)
    End Sub

    ''' <summary>The value is configuration the viewer alone owns (reset); the remembered No is a
    ''' consent record (kept) - a profile reset must not ask the FFmpeg question again.</summary>
    <Fact>
    Public Sub The_profile_reset_resets_the_option_and_keeps_the_consent()
        Assert.Equal("Reset", ProfileResetScope.Classify(EnabledKey))
        Assert.Equal("Kept", ProfileResetScope.Classify(AskedKey))
    End Sub

    ''' <summary>Both names are on the send-logs allow-list on purpose: two booleans, no path, no secret.</summary>
    <Fact>
    Public Sub Both_names_are_on_the_send_logs_allow_list()
        Assert.Contains(EnabledKey, LogPackage.SafeSettingNames)
        Assert.Contains(AskedKey, LogPackage.SafeSettingNames)
    End Sub

    Private Shared Sub DeleteKey(name As String)
        Try
            Interaction.DeleteSetting(App_name, Second_App_Name, name)
        Catch ex As ArgumentException
            ' Already absent.
        End Try
    End Sub

    Private Shared Sub Restore(name As String, previous As String)
        If previous = "" Then
            DeleteKey(name)
        Else
            Interaction.SaveSetting(App_name, Second_App_Name, name, previous)
        End If
    End Sub

End Class
#End If
