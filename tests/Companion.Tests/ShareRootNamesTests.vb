Option Strict On

Imports System.Collections.Generic
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The share-name builder (ticket 054 H-03). The worker refuses the WHOLE folder set when
''' any name is empty, contains "/" or "\", or repeats another one - and it refuses at
''' start, so the list was persisted fine, failed at every start, and the window blamed the
''' port. Two folders with the same leaf, a whole drive, or a label typed as
''' "Photos/2024" were each enough to take all sharing down.
'''
''' All pure: the volume label is injectable, so nothing touches a real drive.
''' </summary>
Public Class ShareRootNamesTests

    <Fact>
    Public Sub Clean_ReplacesSeparatorsAndDropsBlankOnlyNames()
        Assert.Equal("Photos-2024", ShareRootNames.Clean("Photos/2024"))
        Assert.Equal("Photos-2024", ShareRootNames.Clean("Photos\2024"))
        Assert.Equal("Photos", ShareRootNames.Clean("  Photos  "))
        ' A name made only of separators is no name at all - the folder's own is used.
        Assert.Equal("", ShareRootNames.Clean("/\\ //"))
    End Sub

    <Fact>
    Public Sub ForFolder_DriveRootIsNamedAfterItsVolumeOrLetter()
        Assert.Equal("Backup", ShareRootNames.ForFolder("D:\", Function(root) "Backup"))
        Assert.Equal("D", ShareRootNames.ForFolder("D:\", Function(root) ""))
        ' The probe may throw on a dead drive - the letter is the fallback, not a crash.
        Assert.Equal("E", ShareRootNames.ForFolder("E:\",
                          Function(root)
                              Throw New IO.IOException("gone")
                          End Function))
    End Sub

    <Fact>
    Public Sub ForFolder_OrdinaryFolderIsItsLeaf()
        Assert.Equal("Photos", ShareRootNames.ForFolder("C:\Users\me\Pictures\Photos"))
    End Sub

    <Fact>
    Public Sub MakeUnique_NumbersDuplicatesInListOrder()
        Dim names As List(Of String) = ShareRootNames.MakeUnique(
            {Nothing, Nothing, Nothing},
            {"D:\2024\Photos", "E:\Backup\Photos", "F:\Photos"})
        Assert.Equal({"Photos", "Photos (2)", "Photos (3)"}, names)
    End Sub

    ''' <summary>The worker compares names exactly, but two names a phone shows as the same
    ''' folder differing only in case would be a trap of their own.</summary>
    <Fact>
    Public Sub MakeUnique_TreatsDifferentCaseAsTaken()
        Dim names As List(Of String) = ShareRootNames.MakeUnique(
            {"photos", "Photos"}, {"D:\a", "E:\b"})
        Assert.Equal({"photos", "Photos (2)"}, names)
    End Sub

    <Fact>
    Public Sub MakeUnique_CleansTypedLabelsBeforeComparing()
        ' A label typed as "Photos/2024" would otherwise carry a separator to the worker.
        Dim names As List(Of String) = ShareRootNames.MakeUnique(
            {"Photos/2024", "Photos-2024"}, {"D:\a", "E:\b"})
        Assert.Equal({"Photos-2024", "Photos-2024 (2)"}, names)
    End Sub

    <Fact>
    Public Sub MakeUnique_MixedRootsAndLabels()
        Dim names As List(Of String) = ShareRootNames.MakeUnique(
            {"Мои фото", Nothing},
            {"C:\Users\me\Мои фото", "D:\"},
            Function(root) "Мои фото")
        ' The first keeps the name codes already handed out carry; the drive is named
        ' after its label, and the collision is broken the same way.
        Assert.Equal({"Мои фото", "Мои фото (2)"}, names)
    End Sub

End Class
