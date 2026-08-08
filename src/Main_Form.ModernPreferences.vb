#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Collections.Generic
Imports System.Text.Json

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

    Friend Sub ReplaceModernPreferences(imported As ModernViewerPreferences)
        If imported Is Nothing Then Throw New ArgumentNullException(NameOf(imported))
        imported.Normalize()
        modern_Preferences = imported
        ApplyModernPreferencesFromSettings()
    End Sub

    Friend Sub ApplyModernPreferencesFromSettings()
        If modern_Preferences Is Nothing Then Return
        modern_Preferences.Normalize()
        InitializeExtensionLists()
        folder_List_Loaded_For = String.Empty
        ResetShuffleCycle()
        ApplyRecipientsOverlay()
        If video_Controls IsNot Nothing Then video_Controls_Hide_Timer.Interval = VideoControlsHideDelayMilliseconds()
        ' Expanded settings are live settings. Persist after normalisation so a change
        ' made in the settings window survives a crash and is visible to the next
        ' process without waiting for the main form to close.
        modern_Preferences.Save()
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
        Try
            Using doc As JsonDocument = JsonDocument.Parse(modern_Preferences.IncludedExtensions)
                If doc.RootElement.ValueKind <> JsonValueKind.Array Then Return
                For Each item As JsonElement In doc.RootElement.EnumerateArray()
                    If item.ValueKind = JsonValueKind.String Then requested.Add(item.GetString().ToLowerInvariant())
                Next
            End Using
        Catch
            Return
        End Try
        If requested.Count = 0 Then Return
        all_Supported_Extensions.IntersectWith(requested)
    End Sub

    Private Function GetConfiguredSearchOption() As SearchOption
        Return If(modern_Preferences IsNot Nothing AndAlso modern_Preferences.IncludeSubfolders,
                  SearchOption.AllDirectories,
                  SearchOption.TopDirectoryOnly)
    End Function

    ''' <summary>Whether a copy moves the view on to the next file. Default (and the value
    ''' when preferences are not loaded yet) is True: the historical behaviour, and the one
    ''' the sorting run needs.</summary>
    Private Function AdvanceAfterCopy() As Boolean
        Return modern_Preferences Is Nothing OrElse modern_Preferences.AdvanceAfterCopy
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

    Private Sub QueueRememberedVideoPosition(filePath As String)
        If modern_Preferences Is Nothing OrElse Not modern_Preferences.RememberVideoPosition Then Return
        Try
            Dim info As New FileInfo(filePath)
            pending_Video_Seek = modern_Preferences.RememberedVideoPosition(info.FullName, info.LastWriteTimeUtc.Ticks, info.Length)
        Catch
            ClearPendingVideoPosition()
        End Try
    End Sub

    Private Sub RememberCurrentVideoPosition()
        If modern_Preferences Is Nothing OrElse Not modern_Preferences.RememberVideoPosition OrElse
           vlc_Media_Player Is Nothing OrElse String.IsNullOrEmpty(current_Loaded_File_Name) Then Return
        Try
            If Not vlc_Media_Player.IsSeekable Then Return
            Dim info As New FileInfo(current_Loaded_File_Name)
            modern_Preferences.StoreVideoPosition(info.FullName, info.LastWriteTimeUtc.Ticks, info.Length, vlc_Media_Player.Position)
            modern_Preferences.Save()
        Catch
        End Try
    End Sub

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
