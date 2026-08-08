Option Strict On

Imports System.Diagnostics
Imports System.IO
Imports System.Reflection
Imports System.Threading.Tasks
Imports System.Windows.Forms

Friend Module AppFileLogger

    ''' <summary>
    ''' Size at which current.log is rolled over to previous.log on the next start.
    ''' The file is opened Append and nothing else in the app ever shrinks it, so without
    ''' this it grew for the life of the installation - a daily user sorting thousands of
    ''' files adds megabytes a day. Two files, capped: enough to investigate the last
    ''' incident, bounded on disk. (LogPackage caps only the COPY it puts in the
    ''' send-to-author ZIP, never the file itself.)
    ''' </summary>
    Friend Const Max_Log_Bytes As Long = 8L * 1024L * 1024L

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

                RotateIfOversized(logPathValue)

                Dim fileStream As New FileStream(logPathValue, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                logWriter = New StreamWriter(fileStream)
                logWriter.AutoFlush = True
                logListener = New TextWriterTraceListener(logWriter)
#If NETFRAMEWORK Then
                Debug.Listeners.Add(logListener)
#Else
                ' .NET has no Debug.Listeners; Debug.WriteLine routes through the
                ' shared Trace listener collection instead.
                Trace.Listeners.Add(logListener)
#End If
                Trace.AutoFlush = True

                AddHandler Application.ThreadException, AddressOf OnThreadException
                AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException
                AddHandler TaskScheduler.UnobservedTaskException, AddressOf OnUnobservedTaskException
                AddHandler AppDomain.CurrentDomain.ProcessExit, AddressOf OnProcessExit

                isInitialized = True

                WriteLine("============================================================")
                WriteLine("Session started: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff") &
                          "; PID=" & Process.GetCurrentProcess().Id.ToString() &
                          "; role=" & MultiWindowPolicy.InstanceRoleName())
                WriteLine("Log file: " & logPathValue)
                ' Process.MainModule, not Assembly.Location: the latter is an empty
                ' string in a single-file publish (.NET 10 build).
                WriteLine("Executable: " & Process.GetCurrentProcess().MainModule.FileName)
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

    ''' <summary>
    ''' Rolls <paramref name="log_Path"/> over to "previous.log" beside it when it has grown
    ''' past <see cref="Max_Log_Bytes"/>, so the next Append starts from empty. Returns True
    ''' when it rotated.
    '''
    ''' Runs once per start, before the file is opened - the cheapest possible place, and the
    ''' only one where no writer holds the handle. Never throws: a log that cannot be rotated
    ''' must not stop the app from starting (an unwritable folder falls back to
    ''' %LOCALAPPDATA% in ResolveLogPath, and a locked file simply keeps growing until a
    ''' start where it is free).
    ''' </summary>
    Friend Function RotateIfOversized(log_Path As String) As Boolean
        Try
            If String.IsNullOrEmpty(log_Path) Then Return False

            Dim current As New FileInfo(log_Path)
            If Not current.Exists OrElse current.Length <= Max_Log_Bytes Then Return False

            Dim folder As String = Path.GetDirectoryName(log_Path)
            If String.IsNullOrEmpty(folder) Then Return False
            Dim rolled As String = Path.Combine(folder, "previous.log")

            ' One generation back, not a numbered series: the previous session is what an
            ' incident report needs, and a series is another thing that grows.
            If File.Exists(rolled) Then File.Delete(rolled)
            File.Move(log_Path, rolled)
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Function ResolveLogPath() As String
        ' AppContext.BaseDirectory = exe directory on net48 AND in a .NET 10
        ' single-file publish (where Assembly.Location is empty).
        Dim exeDir As String = AppContext.BaseDirectory

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
#If NETFRAMEWORK Then
                If logListener IsNot Nothing Then Debug.Listeners.Remove(logListener)
#Else
                If logListener IsNot Nothing Then Trace.Listeners.Remove(logListener)
#End If
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
