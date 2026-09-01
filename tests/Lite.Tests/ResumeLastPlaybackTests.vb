#If Not NETFRAMEWORK Then
Option Strict On

Imports Microsoft.VisualBasic
Imports Xunit

''' <summary>
''' The stored half of SPECIFICATION_RESUME_LAST_PLAYBACK_DOTNET10 (§8.1): the option is
''' off until it is asked for, it survives a profile round trip, and it travels in an
''' exported settings file.
'''
''' Modern-only, like the option itself - ModernViewerPreferences.vb is whole-file
''' "#If Not NETFRAMEWORK", so this class compiles to nothing on the net48 leg, which is
''' the honest shape: §2.2 says the x86 fallback never gets the setting.
'''
''' WHAT IS DELIBERATELY NOT TESTED: ModernViewerPreferences.Save(). It writes the WHOLE
''' preference block into the live HKCU hive the shipped viewer reads, so a test object at
''' its defaults would flatten the settings of whoever runs `dotnet test` on their own
''' machine. The two directions of the key are proven instead by writing the single profile
''' value by hand (restored afterwards) and by the JSON round trip - both halves of "the
''' value is stored and read back", neither of which can damage a real profile.
''' </summary>
Public Class ResumeLastPlaybackTests

    Private Const Key As String = "ResumeLastPlayback"

    <Fact>
    Public Sub Off_by_default()
        Assert.False(New ModernViewerPreferences().ResumeLastPlayback)
    End Sub

    ''' <summary>Absent key, then "1", then "0" - the three states a profile can be in.
    ''' The previous value is put back whatever the assertions do.
    '''
    ''' Load() also migrates ConfirmDelete once when that key is missing; that write is the
    ''' shipped behaviour of any start of the app and is left alone here.</summary>
    <Fact>
    Public Sub The_profile_value_decides()
        Dim previous As String = Interaction.GetSetting(App_name, Second_App_Name, Key, "")
        Try
            DeleteKey()
            Assert.False(ModernViewerPreferences.Load().ResumeLastPlayback)

            Interaction.SaveSetting(App_name, Second_App_Name, Key, "1")
            Assert.True(ModernViewerPreferences.Load().ResumeLastPlayback)

            Interaction.SaveSetting(App_name, Second_App_Name, Key, "0")
            Assert.False(ModernViewerPreferences.Load().ResumeLastPlayback)
        Finally
            If previous = "" Then
                DeleteKey()
            Else
                Interaction.SaveSetting(App_name, Second_App_Name, Key, previous)
            End If
        End Try
    End Sub

    ''' <summary>It is a setting, so it is exported - unlike the remembered path itself,
    ''' which is personal data and lives outside this class entirely (§4).</summary>
    <Fact>
    Public Sub Export_and_import_carry_the_flag()
        Dim source As New ModernViewerPreferences() With {.ResumeLastPlayback = True}
        Assert.True(ModernViewerPreferences.FromJson(source.ExportJson()).ResumeLastPlayback)

        Dim off As New ModernViewerPreferences() With {.ResumeLastPlayback = False}
        Assert.False(ModernViewerPreferences.FromJson(off.ExportJson()).ResumeLastPlayback)
    End Sub

    <Fact>
    Public Sub Navigation_kind_defaults_and_export_are_stable()
        Dim defaults As New ModernViewerPreferences()
        Assert.True(defaults.IncludeVideoInNavigation)
        Assert.True(defaults.IncludeAudioInNavigation)
        Assert.False(defaults.IncludeDocumentInNavigation)

        Dim source As New ModernViewerPreferences() With {
            .IncludeVideoInNavigation = False,
            .IncludeAudioInNavigation = False,
            .IncludeDocumentInNavigation = True
        }
        Dim restored As ModernViewerPreferences = ModernViewerPreferences.FromJson(source.ExportJson())
        Assert.False(restored.IncludeVideoInNavigation)
        Assert.False(restored.IncludeAudioInNavigation)
        Assert.True(restored.IncludeDocumentInNavigation)
    End Sub

    Private Shared Sub DeleteKey()
        Try
            Interaction.DeleteSetting(App_name, Second_App_Name, Key)
        Catch
            ' Nothing to delete - the state this call was asking for.
        End Try
    End Sub

End Class
#End If
