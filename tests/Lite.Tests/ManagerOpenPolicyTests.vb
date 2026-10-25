#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports Xunit

Public Class ManagerOpenPolicyTests

    Private Function MakeEntry(name As String, Optional isDir As Boolean = False, Optional isLink As Boolean = False,
                               Optional diskPath As String = "C:\test") As ManagerEntry
        Dim place As ManagerPlace = ManagerPlace.ForDrive("C:\", "C:")
        Return New ManagerEntry(ManagerEntry.IdOf(place, "C:\" & name), name, "C:\" & name,
                                isDir, isLink, False, False, If(isDir, -1L, 100L),
                                DateTime.UtcNow, If(String.IsNullOrEmpty(diskPath), "", diskPath & "\" & name))
    End Function

    <Fact>
    Public Sub Directory_EvaluatesToNavigate()
        Dim folder = MakeEntry("SubDir", isDir:=True)
        Dim decision = ManagerOpenPolicy.Evaluate(folder, "default")
        Assert.Equal(ManagerOpenKind.Navigate, decision.Kind)
    End Sub

    <Fact>
    Public Sub Symlink_EvaluatesToRefused()
        Dim link = MakeEntry("FolderLink", isDir:=True, isLink:=True)
        Dim decision = ManagerOpenPolicy.Evaluate(link, "default")
        Assert.Equal(ManagerOpenKind.Refused, decision.Kind)
    End Sub

    <Theory>
    <InlineData("photo.jpg")>
    <InlineData("video.mp4")>
    <InlineData("audio.flac")>
    <InlineData("playlist.m3u8")>
    <InlineData("archive.zip")>
    <InlineData("secure.fd-sec")>
    Public Sub SupportedMedia_EvaluatesToViewerHandoff(name As String)
        Dim entry = MakeEntry(name)
        Dim decision = ManagerOpenPolicy.Evaluate(entry, "default")
        Assert.Equal(ManagerOpenKind.ViewerHandoff, decision.Kind)
    End Sub

    <Theory>
    <InlineData("document.pdf")>
    <InlineData("book.epub")>
    <InlineData("sheet.xlsx")>
    <InlineData("archive.tar")>
    <InlineData("unknown.xyz")>
    Public Sub NonMediaPermittedFiles_EvaluateToExternalOpen(name As String)
        Dim entry = MakeEntry(name)
        Dim defaultDecision = ManagerOpenPolicy.Evaluate(entry, "default")
        Assert.Equal(ManagerOpenKind.ExternalOpen, defaultDecision.Kind)
        Assert.False(defaultDecision.UsePicker)

        Dim pickerDecision = ManagerOpenPolicy.Evaluate(entry, "openWith")
        Assert.Equal(ManagerOpenKind.ExternalOpen, pickerDecision.Kind)
        Assert.True(pickerDecision.UsePicker)
    End Sub

    <Theory>
    <InlineData(".exe")>
    <InlineData(".com")>
    <InlineData(".scr")>
    <InlineData(".pif")>
    <InlineData(".bat")>
    <InlineData(".cmd")>
    <InlineData(".ps1")>
    <InlineData(".psm1")>
    <InlineData(".psd1")>
    <InlineData(".vbs")>
    <InlineData(".vbe")>
    <InlineData(".js")>
    <InlineData(".jse")>
    <InlineData(".wsf")>
    <InlineData(".wsh")>
    <InlineData(".hta")>
    <InlineData(".msi")>
    <InlineData(".msp")>
    <InlineData(".mst")>
    <InlineData(".cpl")>
    <InlineData(".reg")>
    <InlineData(".lnk")>
    <InlineData(".url")>
    <InlineData(".appref-ms")>
    Public Sub MandatoryDenySet_IsBlocked(extension As String)
        Dim entry = MakeEntry("payload" & extension)
        Dim decision = ManagerOpenPolicy.Evaluate(entry, "default")
        Assert.Equal(ManagerOpenKind.Blocked, decision.Kind)
        Assert.Equal(extension.ToLowerInvariant(), decision.BlockedExtension)
    End Sub

    <Theory>
    <InlineData(".py")>
    <InlineData(".pyw")>
    <InlineData(".rb")>
    <InlineData(".pl")>
    <InlineData(".sh")>
    <InlineData(".bash")>
    <InlineData(".sct")>
    <InlineData(".scf")>
    <InlineData(".inf")>
    <InlineData(".ins")>
    <InlineData(".isp")>
    <InlineData(".jar")>
    <InlineData(".application")>
    <InlineData(".appx")>
    <InlineData(".appxbundle")>
    <InlineData(".msix")>
    <InlineData(".msixbundle")>
    Public Sub ScriptDenySet_IsBlocked(extension As String)
        Dim entry = MakeEntry("script" & extension)
        Dim decision = ManagerOpenPolicy.Evaluate(entry, "default")
        Assert.Equal(ManagerOpenKind.Blocked, decision.Kind)
        Assert.Equal(extension.ToLowerInvariant(), decision.BlockedExtension)
    End Sub

    <Theory>
    <InlineData("file.exe.", ".exe")>
    <InlineData("file.exe..", ".exe")>
    <InlineData("file.bat   ", ".bat")>
    <InlineData("file.ps1. . ", ".ps1")>
    <InlineData("PAYLOAD.CMD", ".cmd")>
    Public Sub Normalization_CatchesTrailingDotsAndSpaces(name As String, expectedExt As String)
        Dim ext = ManagerOpenPolicy.NormalizeExtensionForGuard(name)
        Assert.Equal(expectedExt, ext)
        Dim entry = MakeEntry(name)
        Dim decision = ManagerOpenPolicy.Evaluate(entry, "default")
        Assert.Equal(ManagerOpenKind.Blocked, decision.Kind)
    End Sub

    <Fact>
    Public Sub AssocIsDangerous_BlocksDangerousType()
        Dim entry = MakeEntry("document.custom")
        Dim dangerousCheck As Func(Of String, Boolean) = Function(ext) ext = ".custom"

        Dim decision = ManagerOpenPolicy.Evaluate(entry, "default", dangerousCheck)
        Assert.Equal(ManagerOpenKind.Blocked, decision.Kind)
        Assert.Equal(".custom", decision.BlockedExtension)
    End Sub

    <Fact>
    Public Sub AssocIsDangerous_QueryFailure_RefusesOpening()
        Dim entry = MakeEntry("document.pdf")
        Dim failingCheck As Func(Of String, Boolean) = Function(ext)
                                                           Throw New InvalidOperationException("API failure")
                                                       End Function

        Dim decision = ManagerOpenPolicy.Evaluate(entry, "default", failingCheck)
        Assert.Equal(ManagerOpenKind.Refused, decision.Kind)
        Assert.Equal(Localization.T("Не удалось проверить безопасность типа файла"), decision.Reason)
    End Sub

    <Fact>
    Public Sub RecheckMaterialized_BlocksForbiddenExtension()
        Assert.False(ManagerOpenPolicy.RecheckMaterialized("C:\cache\malware.exe"))
        Assert.False(ManagerOpenPolicy.RecheckMaterialized("C:\cache\script.bat. . "))
        Assert.True(ManagerOpenPolicy.RecheckMaterialized("C:\cache\report.docx"))
    End Sub

End Class
#End If
