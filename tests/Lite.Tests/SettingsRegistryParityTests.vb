#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports Microsoft.VisualBasic
Imports Xunit

''' <summary>
''' 103: the registry loader and the JSON import must normalize the same stored value to
''' the same number or choice. The pure suites judge Normalize/FromJson against literals;
''' this one calls the real ModernViewerPreferences.Load() with a probe value in the live
''' HKCU profile and compares it with FromJson of the same value - so Load and import
''' cannot drift, not even in a property the pure tables forgot.
'''
''' House rules (ResumeLastPlaybackTests): ONE key is written at a time, its previous
''' value is put back in Finally, and Save() is never called - so nothing else of the
''' profile is touched. A missing ConfirmDelete key makes Load() perform its one-time
''' legacy migration write; that is the shipped behaviour of any start of the app and is
''' left alone here.
''' </summary>
Public Class SettingsRegistryParityTests

    <Fact>
    Public Sub Registry_load_and_import_agree_for_every_plain_clamp_property()
        For Each spec As ImportedSettingsLimitsTests.NumericSpec In ImportedSettingsLimitsTests.NumericSpecs()
            Dim prop As Reflection.PropertyInfo = GetType(ModernViewerPreferences).GetProperty(spec.Name)
            Dim previous As String = Interaction.GetSetting(App_name, Second_App_Name, spec.Name, "")
            Try
                For Each raw As Integer In {Integer.MinValue, spec.Min - 1, 0, spec.Min, spec.Max, spec.Max + 1, Integer.MaxValue}
                    Interaction.SaveSetting(App_name, Second_App_Name, spec.Name, raw.ToString(CultureInfo.InvariantCulture))
                    Dim loaded As ModernViewerPreferences = ModernViewerPreferences.Load()
                    Dim expected As Integer = Math.Max(spec.Min, Math.Min(spec.Max, raw))
                    Assert.Equal(expected, CInt(prop.GetValue(loaded)))
                    Dim imported As ModernViewerPreferences = ModernViewerPreferences.FromJson(ImportedSettingsLimitsTests.JsonWith(spec.Name, raw))
                    Assert.Equal(CInt(prop.GetValue(loaded)), CInt(prop.GetValue(imported)))
                Next
            Finally
                RestoreKey(spec.Name, previous)
            End Try
        Next
    End Sub

    <Fact>
    Public Sub Registry_load_and_import_agree_for_the_two_bespoke_scale_properties()
        ' InterfaceScalePercent: 0 stays 0 ("follow the system scale"), below 90 moves up
        ' to 90, above 150 comes down to 150.
        CheckSpecial("InterfaceScalePercent",
                     New Integer() {Integer.MinValue, -1, 0, 1, 89, 90, 120, 150, 151, Integer.MaxValue},
                     AddressOf InterfaceScaleExpected)
        CheckSpecial("InterfaceScaleRelativePercent",
                     New Integer() {Integer.MinValue, -1, 0, 1, 89, 90, 120, 150, 151, Integer.MaxValue},
                     AddressOf InterfaceScaleExpected)
        ' PanelThumbnailSize: 0 stays 0 ("follow the legacy card size"), anything else
        ' lands on the nearest of the four sizes the panel's layout was built for.
        CheckSpecial("PanelThumbnailSize",
                     New Integer() {Integer.MinValue, -1, 0, 1, 95, 96, 150, 220, 257, Integer.MaxValue},
                     AddressOf PanelThumbnailExpected)
    End Sub

    <Fact>
    Public Sub Registry_load_and_import_agree_for_every_choice_property()
        For Each spec As ImportedSettingsLimitsTests.ChoiceSpec In ImportedSettingsLimitsTests.ChoiceSpecs()
            Dim prop As Reflection.PropertyInfo = GetType(ModernViewerPreferences).GetProperty(spec.Name)
            Dim previous As String = Interaction.GetSetting(App_name, Second_App_Name, spec.Name, "")
            Try
                Interaction.SaveSetting(App_name, Second_App_Name, spec.Name, "bogus")
                Dim bogus As ModernViewerPreferences = ModernViewerPreferences.Load()
                Assert.Equal(spec.DefaultValue, CStr(prop.GetValue(bogus)))
                Dim bogusImported As ModernViewerPreferences = ModernViewerPreferences.FromJson(ImportedSettingsLimitsTests.JsonWithString(spec.Name, "bogus"))
                Assert.Equal(CStr(prop.GetValue(bogus)), CStr(prop.GetValue(bogusImported)))

                For Each token As String In spec.Allowed
                    Interaction.SaveSetting(App_name, Second_App_Name, spec.Name, token.ToUpperInvariant()) ' matching ignores case
                    Dim loaded As ModernViewerPreferences = ModernViewerPreferences.Load()
                    Assert.Equal(token, CStr(prop.GetValue(loaded)))
                    Dim imported As ModernViewerPreferences = ModernViewerPreferences.FromJson(ImportedSettingsLimitsTests.JsonWithString(spec.Name, token.ToUpperInvariant()))
                    Assert.Equal(CStr(prop.GetValue(loaded)), CStr(prop.GetValue(imported)))
                Next

                ' A blank value falls back to the default - except ConfirmDelete, whose
                ' absent/blank state migrates once from the legacy checkbox (017 §3.7);
                ' both migration outcomes are accepted here, the fold itself is covered
                ' by the pure tests.
                Interaction.SaveSetting(App_name, Second_App_Name, spec.Name, "")
                Dim blank As ModernViewerPreferences = ModernViewerPreferences.Load()
                If spec.Name = "ConfirmDelete" Then
                    Assert.True(blank.ConfirmDelete = "always" OrElse blank.ConfirmDelete = "permanentOnly")
                Else
                    Assert.Equal(spec.DefaultValue, CStr(prop.GetValue(blank)))
                End If
            Finally
                RestoreKey(spec.Name, previous)
            End Try
        Next
    End Sub

    Private Shared Function InterfaceScaleExpected(raw As Integer) As Integer
        If raw <= 0 Then Return 0
        If raw < 90 Then Return 90
        Return Math.Min(150, raw)
    End Function

    Private Shared Function PanelThumbnailExpected(raw As Integer) As Integer
        If raw <= 0 Then Return 0
        Dim best As Integer = 96
        For Each tier As Integer In New Integer() {96, 128, 192, 256}
            If Math.Abs(tier - raw) < Math.Abs(best - raw) Then best = tier
        Next
        Return best
    End Function

    Private Shared Sub CheckSpecial(name As String, probes As Integer(), expected As Func(Of Integer, Integer))
        Dim prop As Reflection.PropertyInfo = GetType(ModernViewerPreferences).GetProperty(name)
        Dim previous As String = Interaction.GetSetting(App_name, Second_App_Name, name, "")
        Try
            For Each raw As Integer In probes
                Interaction.SaveSetting(App_name, Second_App_Name, name, raw.ToString(CultureInfo.InvariantCulture))
                Dim loaded As ModernViewerPreferences = ModernViewerPreferences.Load()
                Assert.Equal(expected(raw), CInt(prop.GetValue(loaded)))
                Dim imported As ModernViewerPreferences = ModernViewerPreferences.FromJson(ImportedSettingsLimitsTests.JsonWith(name, raw))
                Assert.Equal(CInt(prop.GetValue(loaded)), CInt(prop.GetValue(imported)))
            Next
        Finally
            RestoreKey(name, previous)
        End Try
    End Sub

    Private Shared Sub RestoreKey(key As String, previous As String)
        If previous = "" Then
            Try
                Interaction.DeleteSetting(App_name, Second_App_Name, key)
            Catch
            End Try
        Else
            Interaction.SaveSetting(App_name, Second_App_Name, key, previous)
        End If
    End Sub

End Class
#End If
