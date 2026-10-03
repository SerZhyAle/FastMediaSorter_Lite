#If Not NETFRAMEWORK Then
Option Strict On

Imports Xunit

''' <summary>
''' 108 Q108-1 and 105 S13-8/S13-9: which version pairs may carry a folder, and how the
''' existing-instance hint is read. The version-pair matrix of the viewer -> Share Manager wake:
'''
'''   new viewer -> new Share Manager   : folder sent (it asks the user before sharing)
'''   new viewer -> old Share Manager   : wake only + the user is told to add the folder
'''   new viewer -> unreadable version  : wake only + advice (unknown is not proof)
'''   old viewer -> new Share Manager   : untagged payload; a folder is staged for consent, never
'''                                       shared (ActivationDeliveryTests, Companion.Tests)
'''   old viewer -> old Share Manager   : unchanged legacy behaviour (nothing here can alter it)
''' </summary>
Public Class ShareActivationPolicyTests

    <Fact>
    Public Sub A_folder_travels_only_to_a_share_manager_at_or_above_the_consent_build()
        Dim minimum As Version = ShareActivationPolicy.MinConsentVersion
        Assert.Equal(ActivationPlan.SendFolder, ShareActivationPolicy.PlanFor("D:\Photos", minimum))
        Assert.Equal(ActivationPlan.SendFolder, ShareActivationPolicy.PlanFor("D:\Photos", New Version(26, 10, 2, 1530)))
        Assert.Equal(ActivationPlan.SendFolder, ShareActivationPolicy.PlanFor("D:\Photos", New Version(27, 1, 1, 0)))
    End Sub

    <Fact>
    Public Sub An_older_or_unreadable_share_manager_gets_a_wake_and_the_user_gets_advice()
        Assert.Equal(ActivationPlan.WakeAndAdvise, ShareActivationPolicy.PlanFor("D:\Photos", New Version(26, 9, 20, 1206)))
        Assert.Equal(ActivationPlan.WakeAndAdvise, ShareActivationPolicy.PlanFor("D:\Photos", New Version(26, 10, 1, 2359)))
        Assert.Equal(ActivationPlan.WakeAndAdvise, ShareActivationPolicy.PlanFor("D:\Photos", Nothing))
    End Sub

    <Fact>
    Public Sub No_folder_means_a_plain_wake_whatever_the_version()
        Assert.Equal(ActivationPlan.WakeOnly, ShareActivationPolicy.PlanFor("", New Version(1, 0, 0, 0)))
        Assert.Equal(ActivationPlan.WakeOnly, ShareActivationPolicy.PlanFor(Nothing, Nothing))
    End Sub

    <Fact>
    Public Sub The_file_version_is_read_as_four_parts_and_an_unversioned_file_is_unknown()
        Assert.Equal(New Version(26, 10, 2, 108), ShareActivationPolicy.VersionOf(26, 10, 2, 108))
        Assert.Null(ShareActivationPolicy.VersionOf(0, 0, 0, 0))
    End Sub

    <Fact>
    Public Sub An_opened_mutex_means_yes_access_denied_means_yes_and_other_failures_mean_unknown()
        Assert.True(ShareActivationPolicy.ExistingInstanceHint(True, Nothing).Value)
        Assert.False(ShareActivationPolicy.ExistingInstanceHint(False, Nothing).Value)
        ' An elevated Share Manager: its DACL excludes this token. That is an instance, not an absence.
        Assert.True(ShareActivationPolicy.ExistingInstanceHint(False, New UnauthorizedAccessException()).Value)
        Assert.Null(ShareActivationPolicy.ExistingInstanceHint(False, New InvalidOperationException("odd")))
    End Sub

End Class
#End If
