Option Strict On

Imports System.Collections.Generic
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Per-root param rules - the single source of truth for what the phone is told
''' and what the SFTP server enforces (the readOnly-contract fix). If these drift,
''' "Move shows on the phone but rm is denied" comes back.
''' </summary>
Public Class ShareRootParamsTests

    <Fact>
    Public Sub Default_IsWritable_True()
        ' Product default: a freshly shared folder is writable (readOnly:false).
        Dim p As New ShareRootParams()
        Assert.True(p.IsWritable())
        Assert.True(p.IsDefault())
    End Sub

    <Theory>
    <InlineData(False, False, True)>  ' normal folder -> writable
    <InlineData(True, False, False)>  ' explicit read-only -> not writable
    <InlineData(True, True, True)>    ' destination overrides read-only -> writable
    <InlineData(False, True, True)>   ' destination -> writable
    Public Sub IsWritable_TruthTable(isReadOnly As Boolean, destination As Boolean, expectedWritable As Boolean)
        Dim p As New ShareRootParams With {.IsReadOnly = isReadOnly, .IsDestination = destination}
        Assert.Equal(expectedWritable, p.IsWritable())
    End Sub

    <Fact>
    Public Sub IsDefault_False_WhenAnyFieldSet()
        Assert.False(New ShareRootParams With {.Label = "Photos"}.IsDefault())
        Assert.False(New ShareRootParams With {.Profile = "video_library"}.IsDefault())
        Assert.False(New ShareRootParams With {.IsReadOnly = True}.IsDefault())
        Assert.False(New ShareRootParams With {.IsDestination = True}.IsDefault())
        Assert.False(New ShareRootParams With {.AccessPin = "1234"}.IsDefault())
        Assert.False(New ShareRootParams With {.SlideshowInterval = 5}.IsDefault())
        Assert.False(New ShareRootParams With {.MediaTypes = New List(Of String) From {"image"}}.IsDefault())
    End Sub

    <Fact>
    Public Sub IsDefault_True_ForAppDefaults()
        ' SlideshowInterval = 10 is the app default and must still count as default.
        Assert.True(New ShareRootParams With {.SlideshowInterval = 10, .Profile = "none"}.IsDefault())
    End Sub

    <Fact>
    Public Sub Clone_IsDeep_AndNormalizesNulls()
        Dim src As New ShareRootParams With {
            .Label = "A", .MediaTypes = New List(Of String) From {"image", "video"},
            .IsDestination = True, .DestinationColorArgb = -14575885
        }
        Dim copy As ShareRootParams = src.Clone()
        copy.MediaTypes.Add("audio")
        Assert.Equal(2, src.MediaTypes.Count)   ' original untouched -> deep copy
        Assert.Equal(3, copy.MediaTypes.Count)
        Assert.Equal(-14575885, copy.DestinationColorArgb)

        ' Nulls left by JSON deserialization must normalize on Clone.
        Dim nulls As New ShareRootParams With {.Label = Nothing, .Profile = Nothing, .MediaTypes = Nothing, .Comment = Nothing, .AccessPin = Nothing}
        Dim fixedUp As ShareRootParams = nulls.Clone()
        Assert.Equal("", fixedUp.Label)
        Assert.Equal("none", fixedUp.Profile)
        Assert.NotNull(fixedUp.MediaTypes)
        Assert.Equal("", fixedUp.Comment)
    End Sub

    <Fact>
    Public Sub Store_GetFor_UnknownPath_ReturnsDefault()
        ' Read-only: an unshared path yields defaults, never Nothing (no registry write).
        Dim p As ShareRootParams = ShareRootParamsStore.GetFor("C:\__fms_test_never_shared__" & Guid.NewGuid().ToString("N"))
        Assert.NotNull(p)
        Assert.True(p.IsDefault())
    End Sub

End Class
