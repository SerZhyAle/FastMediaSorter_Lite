Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Minimal file logger for Companion - writes companion.log next to the exe, or
''' falls back to %LOCALAPPDATA%\FastMediaSorterCompanion when the install dir is
''' read-only (e.g. under MSIX). Mirrors the viewer's AppFileLogger contract that the
''' ported Share code already calls (currently ShareConfigBuilder diagnostics).
''' Never throws - a logging failure must never break a share operation.
'''
''' Since ticket 028 Ф-3 (APP-BEHAVIOUR rule 6) this is where a caught exception goes:
''' before it, seven catch sites wrote only to Debug.WriteLine, which a Release build
''' drops, so the log the user can send held nothing about the failure they saw. It is
''' capped the way the viewer's log is, so a chatty failure cannot grow it for ever.
''' </summary>
Public Module AppFileLogger

    Private ReadOnly Gate As New Object()
    Private _path As String
    Private _resolved As Boolean
    Private _sessionStarted As Boolean
    Private _sessionHeader As String

    Public Sub Initialize()
        Try
            SyncLock Gate
                If _sessionStarted Then Return
                Dim p = LogPath()
                If p Is Nothing Then Return
                Dim warnings = LogSessionDiagnostics.ReadPreviousWarnings(p)
                RotateIfOversized(p)
                Using process = Diagnostics.Process.GetCurrentProcess()
                    Dim header = String.Join(Environment.NewLine, warnings)
                    If header <> "" Then header &= Environment.NewLine
                    _sessionHeader = "Session started: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff", Globalization.CultureInfo.InvariantCulture) & "; PID=" & process.Id.ToString(Globalization.CultureInfo.InvariantCulture) & Environment.NewLine
                    header &= _sessionHeader
                    File.AppendAllText(p, header)
                End Using
                _sessionStarted = True
                AddHandler AppDomain.CurrentDomain.ProcessExit, AddressOf OnProcessExit
            End SyncLock
        Catch
        End Try
    End Sub

    Private Sub OnProcessExit(sender As Object, e As EventArgs)
        Using process = Diagnostics.Process.GetCurrentProcess()
            WriteLine("Session finished: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff", Globalization.CultureInfo.InvariantCulture) & "; PID=" & process.Id.ToString(Globalization.CultureInfo.InvariantCulture))
        End Using
    End Sub

    ''' <summary>Past this size the log is rotated to companion.log.old on the next write.</summary>
    Friend Const Max_Log_Bytes As Long = 2L * 1024L * 1024L

    ''' <summary>Where companion.log lives, or Nothing when no location is writable.</summary>
    Public ReadOnly Property LogFilePath As String
        Get
            Return LogPath()
        End Get
    End Property

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
            Initialize()
            Dim p As String = LogPath()
            If p Is Nothing Then Return
            SyncLock Gate
                If RotateIfOversized(p) AndAlso _sessionStarted Then File.AppendAllText(p, _sessionHeader)
                File.AppendAllText(p, message & Environment.NewLine)
            End SyncLock
        Catch
        End Try
    End Sub

    ''' <summary>A caught exception, whole, with a time stamp and what was being done.</summary>
    Public Sub LogException(context As String, ex As Exception)
        If ex Is Nothing Then
            WriteLine(Stamp() & " " & context)
            Return
        End If
        WriteLine(Stamp() & " " & context & ": " & ex.ToString())
    End Sub

    Private Function Stamp() As String
        Return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Globalization.CultureInfo.InvariantCulture)
    End Function

    ''' <summary>Keeps one previous generation; True when a rotation happened.</summary>
    Friend Function RotateIfOversized(logPath As String) As Boolean
        Try
            Dim info As New FileInfo(logPath)
            If Not info.Exists OrElse info.Length <= Max_Log_Bytes Then Return False
            Dim older As String = logPath & ".old"
            If File.Exists(older) Then File.Delete(older)
            File.Move(logPath, older)
            Return True
        Catch
            Return False
        End Try
    End Function

End Module
