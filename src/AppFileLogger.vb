Option Strict On

Imports System.Diagnostics
Imports System.IO
Imports System.Reflection
Imports System.Threading.Tasks
Imports System.Windows.Forms

Friend Module AppFileLogger

    Private ReadOnly syncRoot As New Object()

    Private logWriter As StreamWriter = Nothing
    Private logListener As TextWriterTraceListener = Nothing
    Private isInitialized As Boolean = False
    Private logPathValue As String = ""

    Public ReadOnly Property LogFilePath As String
        Get
            Return logPathValue
        End Get
    End Property

    Public Sub Initialize()
        SyncLock syncRoot
            If isInitialized Then Return

            Try
                logPathValue = ResolveLogPath()
                Dim logDir As String = Path.GetDirectoryName(logPathValue)
                If Not String.IsNullOrEmpty(logDir) Then Directory.CreateDirectory(logDir)

                Dim fileStream As New FileStream(logPathValue, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                logWriter = New StreamWriter(fileStream)
                logWriter.AutoFlush = True
                logListener = New TextWriterTraceListener(logWriter)
                Debug.Listeners.Add(logListener)
                Trace.AutoFlush = True

                AddHandler Application.ThreadException, AddressOf OnThreadException
                AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException
                AddHandler TaskScheduler.UnobservedTaskException, AddressOf OnUnobservedTaskException
                AddHandler AppDomain.CurrentDomain.ProcessExit, AddressOf OnProcessExit

                isInitialized = True

                WriteLine("============================================================")
                WriteLine("Session started: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"))
                WriteLine("Log file: " & logPathValue)
                WriteLine("Executable: " & Assembly.GetExecutingAssembly().Location)
                WriteLine("Version: " & Assembly.GetExecutingAssembly().GetName().Version.ToString())
                WriteLine("OS: " & Environment.OSVersion.ToString())
                WriteLine("64-bit OS: " & Environment.Is64BitOperatingSystem.ToString() & "; 64-bit process: " & Environment.Is64BitProcess.ToString())
                WriteLine("Command line: " & Environment.CommandLine)
            Catch ex As Exception
                ' Logging must never break app startup.
            End Try
        End SyncLock
    End Sub

    Public Sub WriteLine(message As String)
        SyncLock syncRoot
            If logWriter Is Nothing Then Return

            Try
                logWriter.WriteLine(message)
                logWriter.Flush()
            Catch
            End Try
        End SyncLock
    End Sub

    Public Sub LogException(context As String, ex As Exception)
        If ex Is Nothing Then Return

        Dim details As String =
            context & " [" & ex.GetType().FullName & "] " & ex.Message &
            Environment.NewLine & ex.StackTrace

        If ex.InnerException IsNot Nothing Then
            details &= Environment.NewLine &
                "Inner: [" & ex.InnerException.GetType().FullName & "] " & ex.InnerException.Message &
                Environment.NewLine & ex.InnerException.StackTrace
        End If

        WriteLine(details)
    End Sub

    Private Function ResolveLogPath() As String
        Dim exePath As String = Assembly.GetExecutingAssembly().Location
        Dim exeDir As String = Path.GetDirectoryName(exePath)

        If Not String.IsNullOrEmpty(exeDir) Then
            Try
                Dim candidate As String = Path.Combine(exeDir, "current.log")
                Directory.CreateDirectory(exeDir)
                Using fs As New FileStream(candidate, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                End Using
                Return candidate
            Catch
            End Try
        End If

        Dim fallbackDir As String = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            App_name,
            Second_App_Name,
            "logs")

        Directory.CreateDirectory(fallbackDir)
        Return Path.Combine(fallbackDir, "current.log")
    End Function

    Private Sub OnThreadException(sender As Object, e As Threading.ThreadExceptionEventArgs)
        LogException("UI thread exception", e.Exception)
    End Sub

    Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        Dim ex As Exception = TryCast(e.ExceptionObject, Exception)
        If ex IsNot Nothing Then
            LogException("Unhandled exception; IsTerminating=" & e.IsTerminating.ToString(), ex)
        Else
            WriteLine("Unhandled non-exception object; IsTerminating=" & e.IsTerminating.ToString())
        End If
    End Sub

    Private Sub OnUnobservedTaskException(sender As Object, e As UnobservedTaskExceptionEventArgs)
        LogException("Unobserved task exception", e.Exception)
    End Sub

    Private Sub OnProcessExit(sender As Object, e As EventArgs)
        SyncLock syncRoot
            Try
                If logWriter IsNot Nothing Then
                    logWriter.WriteLine("Session finished: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"))
                    logWriter.Flush()
                End If
            Catch
            End Try

            Try
                If logListener IsNot Nothing Then Debug.Listeners.Remove(logListener)
            Catch
            End Try

            Try
                If logListener IsNot Nothing Then logListener.Close()
            Catch
            End Try

            Try
                If logWriter IsNot Nothing Then logWriter.Close()
            Catch
            End Try
        End SyncLock
    End Sub

End Module
