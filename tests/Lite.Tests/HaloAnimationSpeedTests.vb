Option Strict On

Imports Xunit

''' <summary>
''' The stored halo growth speed (SPECIFICATION_PERSPECTIVE_HALO_ANIMATION_SPEED.md §4).
'''
''' The one thing worth pinning down here is that NOTHING throws and nothing lands on a
''' surprise speed: the setting is read straight from the registry, where a value can be
''' absent (every profile that predates the feature), empty, or whatever an earlier crash
''' or a hand edit left behind. All three have to mean "medium" - which is also what hands
''' existing users the new, faster default without a migration step.
'''
''' Assertions go through the string round-trip rather than the enum: Common_Module is a
''' Friend module, so naming HaloAnimationSpeed in a public test signature would not
''' compile (BC30909). Normalize(x) is exactly what the load/save pair does anyway.
''' </summary>
Public Class HaloAnimationSpeedTests

    Private Shared Function Normalize(stored As String) As String
        Return HaloSpeedToSetting(HaloSpeedFromSetting(stored))
    End Function

    <Theory>
    <InlineData("slow")>
    <InlineData("medium")>
    <InlineData("fast")>
    Public Sub The_three_documented_values_round_trip(stored As String)
        Assert.Equal(stored, Normalize(stored))
    End Sub

    ''' <summary>Case and stray whitespace are a registry fact, not a different setting.</summary>
    <Theory>
    <InlineData("Slow")>
    <InlineData("  SLOW ")>
    Public Sub Case_and_padding_do_not_change_the_meaning(stored As String)
        Assert.Equal("slow", Normalize(stored))
    End Sub

    <Theory>
    <InlineData(Nothing)>
    <InlineData("")>
    <InlineData("   ")>
    <InlineData("350")>
    <InlineData("mediuim")>
    Public Sub Absent_empty_or_corrupt_reads_as_medium(stored As String)
        Assert.Equal("medium", Normalize(stored))
    End Sub

    ''' <summary>The three speeds really are three distinct settings - a mapping that
    ''' collapsed two of them would still pass the round-trip above.</summary>
    <Fact>
    Public Sub The_speeds_are_distinct()
        Assert.NotEqual(HaloSpeedFromSetting("slow"), HaloSpeedFromSetting("medium"))
        Assert.NotEqual(HaloSpeedFromSetting("medium"), HaloSpeedFromSetting("fast"))
        Assert.NotEqual(HaloSpeedFromSetting("slow"), HaloSpeedFromSetting("fast"))
    End Sub

End Class
