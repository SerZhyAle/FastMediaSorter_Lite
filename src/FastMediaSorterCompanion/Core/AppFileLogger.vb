Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Minimal file logger for Companion - writes companion.log next to the exe, or
''' falls back to %LOCALAPPDATA%\FastMediaSorterCompanion when the install dir is
''' read-only (e.g. under MSIX). Mirrors the LITE AppFileLogger contract that the
''' ported Share code already calls (currently ShareConfigBuilder diagnostics).
''' Never throws - a logging failure must never break a share operation.
''' </summary>
Public Module AppFileLogger

    Private ReadOnly Gate As New Object()
    Private _path As String
    Private _resolved As Boolean

    Private Function LogPath() As String
        If _resolved Then Return _path
        _resolved = True
        Try
            Dim dir As String = Path.GetDirectoryName(Application.ExecutablePath)
            Dim p As String = Path.Combine(dir, "companion.log")
            File.AppendAllText(p, String.Empty) ' probe writability
            _path = p
            Return _path
        Catch
        End Try
        Try
            Dim dir As String = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FastMediaSorterCompanion")
            Directory.CreateDirectory(dir)
            _path = Path.Combine(dir, "companion.log")
        Catch
            _path = Nothing
        End Try
        Return _path
    End Function

    Public Sub WriteLine(message As String)
        Try
            Dim p As String = LogPath()
            If p Is Nothing Then Return
            SyncLock Gate
                File.AppendAllText(p, message & Environment.NewLine)
            End SyncLock
        Catch
        End Try
    End Sub

End Module
