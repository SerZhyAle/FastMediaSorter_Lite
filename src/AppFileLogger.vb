Option Strict On

Imports System.Diagnostics
Imports System.IO
Imports System.Reflection
Imports System.Runtime.InteropServices
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

    Public ReadOnly Property IsReady As Boolean
        Get
            Return isInitialized
        End Get
    End Property

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

                ' Append-only, not FileMode.Append: Append seeks to the end ONCE, at open,
                ' and each handle then writes at its own offset - a second process (the
                ' forwarding launch, a second window) overwrote this one's lines and this
                ' one overwrote its (053 F-11). An append-only handle always writes at EOF.
                logWriter = New StreamWriter(OpenAppendOnly(logPathValue))
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
            '
            ' Move current.log aside FIRST and only then replace previous.log. The other
            ' order deleted previous.log and then failed the move whenever another
            ' instance held current.log - destroying a generation without rotating
            ' (053 F-11). A locked file now leaves both untouched and keeps growing until
            ' a start where it is free.
            Dim staging As String = Path.Combine(folder, "previous.log.rotating")
            If File.Exists(staging) Then File.Delete(staging)
            Try
                File.Move(log_Path, staging)
            Catch
                Return False
            End Try
            If File.Exists(rolled) Then File.Delete(rolled)
            File.Move(staging, rolled)
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Const FILE_APPEND_DATA As UInteger = &H4UI
    Private Const SYNCHRONIZE As UInteger = &H100000UI
    Private Const FILE_SHARE_READ As UInteger = &H1UI
    Private Const FILE_SHARE_WRITE As UInteger = &H2UI
    Private Const OPEN_ALWAYS As UInteger = 4UI
    Private Const FILE_ATTRIBUTE_NORMAL As UInteger = &H80UI

    <DllImport("kernel32.dll", EntryPoint:="CreateFileW", SetLastError:=True,
                                               CharSet:=CharSet.Unicode)>
    Private Function CreateFileW(fileName As String, desiredAccess As UInteger, shareMode As UInteger,
                                 securityAttributes As IntPtr, creationDisposition As UInteger,
                                 flagsAndAttributes As UInteger, templateFile As IntPtr) As Microsoft.Win32.SafeHandles.SafeFileHandle
    End Function

    ''' <summary>
    ''' Opens <paramref name="log_Path"/> for appending only - FILE_APPEND_DATA without
    ''' FILE_WRITE_DATA - which Windows documents as "write operations will not overwrite
    ''' existing data": every write lands at the end of the file as it is at that moment,
    ''' whoever else is appending. Shared for reading and writing, as before, so a second
    ''' process and the send-logs packer can still open it.
    ''' </summary>
    Friend Function OpenAppendOnly(log_Path As String) As Stream
        Dim handle As Microsoft.Win32.SafeHandles.SafeFileHandle =
            CreateFileW(log_Path, FILE_APPEND_DATA Or SYNCHRONIZE, FILE_SHARE_READ Or FILE_SHARE_WRITE,
                        IntPtr.Zero, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero)
        If handle.IsInvalid Then
            Dim errorCode As Integer = Marshal.GetLastWin32Error()
            handle.Dispose()
            Throw New IOException("Cannot open the log for appending: " & log_Path, New System.ComponentModel.Win32Exception(errorCode))
        End If
        Return New AppendOnlyFileStream(handle)
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

    ''' <summary>
    ''' Set by the main window: says on screen that something failed. Attaching ANY handler
    ''' to Application.ThreadException stops WinForms from showing its own dialog and keeps
    ''' the loop running, so an exception from a handler - a half-done move, a half-built
    ''' list - used to leave the app in whatever state it reached with nothing visible
    ''' (053 F-15). The log alone is not enough; the user has to know to look.
    ''' </summary>
    Friend UiFaultReporter As Action(Of Exception)

    Private Sub OnThreadException(sender As Object, e As Threading.ThreadExceptionEventArgs)
        LogException("UI thread exception", e.Exception)
        Dim reporter As Action(Of Exception) = UiFaultReporter
        If reporter Is Nothing Then Return
        Try
            reporter(e.Exception)
        Catch ex As Exception
            ' The reporter failing must not become a second fault on the same thread.
            LogException("UI fault reporter", ex)
        End Try
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

''' <summary>
''' A write-only stream over an append-only handle, calling WriteFile itself. Not a
''' FileStream around the handle: FileStream tracks its own position - net48 checks it
''' against the OS pointer before a write and throws once another process has appended,
''' and .NET writes at the tracked offset - and both assumptions are wrong for a file that
''' several processes append to. WriteFile on this handle always writes at EOF.
''' </summary>
Friend NotInheritable Class AppendOnlyFileStream
    Inherits Stream

    Private ReadOnly handle As Microsoft.Win32.SafeHandles.SafeFileHandle

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WriteFile(file As Microsoft.Win32.SafeHandles.SafeFileHandle, buffer As Byte(),
                                      bytesToWrite As Integer, ByRef bytesWritten As Integer,
                                      overlapped As IntPtr) As Boolean
    End Function

    Friend Sub New(handle As Microsoft.Win32.SafeHandles.SafeFileHandle)
        Me.handle = handle
    End Sub

    Public Overrides ReadOnly Property CanRead As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overrides ReadOnly Property CanSeek As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overrides ReadOnly Property CanWrite As Boolean
        Get
            Return Not handle.IsClosed
        End Get
    End Property

    Public Overrides ReadOnly Property Length As Long
        Get
            Throw New NotSupportedException()
        End Get
    End Property

    Public Overrides Property Position As Long
        Get
            Throw New NotSupportedException()
        End Get
        Set(value As Long)
            Throw New NotSupportedException()
        End Set
    End Property

    Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
        If buffer Is Nothing Then Throw New ArgumentNullException(NameOf(buffer))
        If count <= 0 Then Return
        Dim chunk As Byte() = buffer
        If offset <> 0 OrElse count <> buffer.Length Then
            chunk = New Byte(count - 1) {}
            Array.Copy(buffer, offset, chunk, 0, count)
        End If
        Dim written As Integer = 0
        If Not WriteFile(handle, chunk, count, written, IntPtr.Zero) Then
            Throw New IOException("Writing the log failed.",
                                  New System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()))
        End If
    End Sub

    Public Overrides Sub Flush()
        ' Nothing is buffered here: every Write is one WriteFile.
    End Sub

    Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
        Throw New NotSupportedException()
    End Function

    Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
        Throw New NotSupportedException()
    End Function

    Public Overrides Sub SetLength(value As Long)
        Throw New NotSupportedException()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then handle.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class
