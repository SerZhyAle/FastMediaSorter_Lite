#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Collections.Generic

Partial Public Class Main_Form

    Private shuffle_Cycle As New Queue(Of Integer)()
    Private shuffle_Cycle_Count As Integer

    Private Function RecentFilesLimit() As Integer
        Return If(modern_Preferences Is Nothing, 50, modern_Preferences.RecentFilesLimit)
    End Function

    Friend Function GetModernPreferences() As ModernViewerPreferences
        If modern_Preferences Is Nothing Then modern_Preferences = ModernViewerPreferences.Load()
        Return modern_Preferences
    End Function

    Friend Sub ApplyModernPreferencesFromSettings()
        If modern_Preferences Is Nothing Then Return
        modern_Preferences.Normalize()
        InitializeExtensionLists()
        folder_List_Loaded_For = String.Empty
        ResetShuffleCycle()
        ApplyRecipientsOverlay()
        If video_Controls IsNot Nothing Then video_Controls_Hide_Timer.Interval = VideoControlsHideDelayMilliseconds()
    End Sub

    Private Function RecentFoldersLimit() As Integer
        Return If(modern_Preferences Is Nothing, 100, modern_Preferences.RecentFoldersLimit)
    End Function

    Private Function StartupOpenMode() As String
        Return If(modern_Preferences Is Nothing, "lastFolder", modern_Preferences.StartupOpenMode)
    End Function

    ''' <summary>Restricts the already-supported extension set, never adds an
    ''' unknown extension merely because it appears in a hand-edited profile.</summary>
    Private Sub ApplyConfiguredExtensionFilter()
        If modern_Preferences Is Nothing OrElse String.IsNullOrWhiteSpace(modern_Preferences.IncludedExtensions) Then Return

        Dim requested As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each part As String In modern_Preferences.IncludedExtensions.Split(";"c)
            Dim extension As String = part.Trim()
            If extension.Length = 0 Then Continue For
            If Not extension.StartsWith(".", StringComparison.Ordinal) Then extension = "." & extension
            requested.Add(extension.ToLowerInvariant())
        Next
        all_Supported_Extensions.IntersectWith(requested)
    End Sub

    Private Function GetConfiguredSearchOption() As SearchOption
        Return If(modern_Preferences IsNot Nothing AndAlso modern_Preferences.IncludeSubfolders,
                  SearchOption.AllDirectories,
                  SearchOption.TopDirectoryOnly)
    End Function

    Private Function StopSlideShowForManualNavigation() As Boolean
        Return modern_Preferences Is Nothing OrElse modern_Preferences.StopSlideshowOnManualNavigation
    End Function

    Private Function RecipientsOverlayWidth() As Integer
        Return If(modern_Preferences Is Nothing, 280, modern_Preferences.RecipientsOverlayWidth)
    End Function

    Private Function RecipientsOverlayFontSize() As Single
        Return CSng(If(modern_Preferences Is Nothing, 11, modern_Preferences.RecipientsOverlayFontSize))
    End Function

    Private Function RecipientsOverlayAlpha() As Integer
        Dim opacity As Integer = If(modern_Preferences Is Nothing, 88, modern_Preferences.RecipientsOverlayOpacity)
        Return CInt(Math.Round(opacity * 255.0R / 100.0R))
    End Function

    Private Function RecipientsOverlayVisibleRows() As Integer
        Return If(modern_Preferences Is Nothing, 10, modern_Preferences.RecipientsOverlayVisibleRows)
    End Function

    Private Function RecipientsOverlayPosition() As String
        Return If(modern_Preferences Is Nothing, "topLeft", modern_Preferences.RecipientsOverlayPosition)
    End Function

    Private Function VideoControlsHideDelayMilliseconds() As Integer
        Return If(modern_Preferences Is Nothing, 3000, modern_Preferences.VideoControlsHideDelaySec * 1000)
    End Function

    Private Function KeepVideoControlsVisibleWhilePaused() As Boolean
        Return modern_Preferences Is Nothing OrElse modern_Preferences.ShowVideoControlsWhenPaused
    End Function

    Private Function VideoClickMovesToNextFile() As Boolean
        Return modern_Preferences IsNot Nothing AndAlso modern_Preferences.VideoSingleClickAction = "nextFile"
    End Function

    Private Function VideoShouldAutoplay() As Boolean
        Return modern_Preferences Is Nothing OrElse modern_Preferences.VideoAutoplay
    End Function

    Private Function VideoEndAction() As String
        Return If(modern_Preferences Is Nothing, "stay", modern_Preferences.VideoEndAction)
    End Function

    Private Sub ResetShuffleCycle()
        shuffle_Cycle.Clear()
        shuffle_Cycle_Count = 0
    End Sub

    Private Function NextShuffleCycleIndex(total As Integer, current As Integer) As Integer
        If total <= 1 Then Return 0
        If shuffle_Cycle_Count <> total OrElse shuffle_Cycle.Count = 0 Then
            Dim values As New List(Of Integer)()
            For i As Integer = 0 To total - 1
                If i <> current Then values.Add(i)
            Next
            For i As Integer = values.Count - 1 To 1 Step -1
                Dim j As Integer = slideshow_Rng.Next(i + 1)
                Dim swap As Integer = values(i)
                values(i) = values(j)
                values(j) = swap
            Next
            If values.Count = 0 Then values.Add(0)
            shuffle_Cycle = New Queue(Of Integer)(values)
            shuffle_Cycle_Count = total
        End If
        Return shuffle_Cycle.Dequeue()
    End Function
End Class
#End If

#If NETFRAMEWORK Then
Partial Public Class Main_Form
    ' Keep the shared loading code buildable for the frozen x86 target.  These
    ' values preserve its historical behaviour; the settings themselves are
    ' deliberately a .NET 10 feature.
    Private Function RecentFilesLimit() As Integer
        Return 50
    End Function

    Private Function RecentFoldersLimit() As Integer
        Return 100
    End Function

    Private Function StartupOpenMode() As String
        Return "lastFolder"
    End Function

    Private Function GetConfiguredSearchOption() As IO.SearchOption
        Return IO.SearchOption.TopDirectoryOnly
    End Function

    Private Function RecipientsOverlayWidth() As Integer
        Return 280
    End Function

    Private Function RecipientsOverlayFontSize() As Single
        Return 11.0F
    End Function

    Private Function RecipientsOverlayAlpha() As Integer
        Return 224
    End Function

    Private Function RecipientsOverlayVisibleRows() As Integer
        Return 10
    End Function

    Private Function RecipientsOverlayPosition() As String
        Return "topLeft"
    End Function

    Private Function VideoControlsHideDelayMilliseconds() As Integer
        Return 2500
    End Function

    Private Function KeepVideoControlsVisibleWhilePaused() As Boolean
        Return True
    End Function

    Private Function VideoClickMovesToNextFile() As Boolean
        Return False
    End Function

    Private Function VideoShouldAutoplay() As Boolean
        Return True
    End Function

    Private Function VideoEndAction() As String
        Return "stay"
    End Function
End Class
#End If
