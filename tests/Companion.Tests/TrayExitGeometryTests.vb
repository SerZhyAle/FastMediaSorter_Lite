Option Strict On

Imports System.IO
Imports Xunit

''' <summary>
''' The Share Manager writes its window geometry in FormClosing. Exit from the tray used
''' to dispose the window without closing it, so that handler never ran and the next
''' start forgot where the window was (spec 028 phase 1, APP-BEHAVIOUR rule 10). A source
''' read - the project builds no form.
''' </summary>
Public Class TrayExitGeometryTests

    <Fact>
    Public Sub Tray_exit_closes_the_window_before_leaving_the_message_loop()
        Dim body As String = Between(TraySource(), "Private Sub ExitApplication()", "End Sub")
        Dim closeAt As Integer = body.IndexOf("_mainWindow.Close()", StringComparison.Ordinal)
        Dim exitAt As Integer = body.IndexOf("ExitThread()", StringComparison.Ordinal)
        Assert.True(closeAt >= 0, "ExitApplication must close the main window")
        Assert.True(exitAt > closeAt, "the window must be closed BEFORE ExitThread")
    End Sub

    <Fact>
    Public Sub Dispose_never_disposes_the_window_without_closing_it_first()
        Dim body As String = Between(TraySource(), "Protected Overrides Sub Dispose(", "End Sub")
        Dim closeAt As Integer = body.IndexOf("_mainWindow.Close()", StringComparison.Ordinal)
        Dim disposeAt As Integer = body.IndexOf("_mainWindow.Dispose()", StringComparison.Ordinal)
        Assert.True(disposeAt >= 0)
        Assert.True(closeAt >= 0 AndAlso closeAt < disposeAt, "Close() must precede Dispose()")
    End Sub

    Private Shared Function Between(source As String, startToken As String, endToken As String) As String
        Dim start As Integer = source.IndexOf(startToken, StringComparison.Ordinal)
        Assert.True(start >= 0, startToken & " not found")
        Dim finish As Integer = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal)
        Assert.True(finish > start)
        Return source.Substring(start, finish - start)
    End Function

    Private Shared Function TraySource() As String
        Dim dir As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "FastMediaSorterCompanion", "TrayContext.vb")
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src\FastMediaSorterCompanion\TrayContext.vb not found above the test binary")
    End Function

End Class
