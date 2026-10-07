Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
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

    ''' <summary>True while the process-wide handlers (UI/unhandled/unobserved/exit) are subscribed -
    ''' exactly once, whatever happened to earlier initialization attempts.</summary>
    Friend ReadOnly Property HandlersAttached As Boolean
        Get
            Return eventsAttached
        End Get
    End Property

    Public ReadOnly Property LogFilePath As String
        Get
            Return logPathValue
        End Get
    End Property

    ''' <summary>Resets initialization state for testing (057.S13-12).</summary>
    Friend Sub ResetForTesting()
        SyncLock syncRoot
            If eventsAttached Then
                Try : RemoveHandler Application.ThreadException, AddressOf OnThreadException : Catch : End Try
                Try : RemoveHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException : Catch : End Try
                Try : RemoveHandler TaskScheduler.UnobservedTaskException, AddressOf OnUnobservedTaskException : Catch : End Try
                Try : RemoveHandler AppDomain.CurrentDomain.ProcessExit, AddressOf OnProcessExit : Catch : End Try
                eventsAttached = False
            End If
            If logListener IsNot Nothing Then
                Try
#If NETFRAMEWORK Then
                    Debug.Listeners.Remove(logListener)
#Else
                    Trace.Listeners.Remove(logListener)
#End If
                Catch
                End Try
                Try : logListener.Dispose() : Catch : End Try
                logListener = Nothing
            End If
            If logWriter IsNot Nothing Then
                Try : logWriter.Dispose() : Catch : End Try
                logWriter = Nothing
            End If
            isInitialized = False
            logPathValue = ""
        End SyncLock
    End Sub

    Private eventsAttached As Boolean = False
    Friend InitializeFaultInjector As Action(Of Integer) = Nothing

    ''' <summary>Tests only: the folder the log lives in, instead of next to the exe.</summary>
    Friend LogDirectoryOverride As String = Nothing

    Public Sub Initialize()
        SyncLock syncRoot
            If isInitialized Then Return

            Dim acquiredStream As Stream = Nothing
            Dim acquiredWriter As StreamWriter = Nothing
            Dim acquiredListener As TextWriterTraceListener = Nothing
            Dim listenerAdded As Boolean = False
            Dim handlersAdded As Integer = 0

            Try
                logPathValue = ResolveLogPath()
                Dim logDir As String = Path.GetDirectoryName(logPathValue)
                If Not String.IsNullOrEmpty(logDir) Then Directory.CreateDirectory(logDir)

                If InitializeFaultInjector IsNot Nothing Then InitializeFaultInjector(1)

                Dim previousWarnings = LogSessionDiagnostics.ReadPreviousWarnings(logPathValue)
                RotateIfOversized(logPathValue)

                If InitializeFaultInjector IsNot Nothing Then InitializeFaultInjector(2)

                ' Append-only, not FileMode.Append: Append seeks to the end ONCE, at open,
                ' and each handle then writes at its own offset - a second process (the
                ' forwarding launch, a second window) overwrote this one's lines and this
                ' one overwrote its (053 F-11). An append-only handle always writes at EOF.
                acquiredStream = OpenAppendOnly(logPathValue)
                acquiredWriter = New StreamWriter(acquiredStream)
                acquiredWriter.AutoFlush = True
                acquiredListener = New TextWriterTraceListener(acquiredWriter)

                If InitializeFaultInjector IsNot Nothing Then InitializeFaultInjector(3)

#If NETFRAMEWORK Then
                Debug.Listeners.Add(acquiredListener)
#Else
                ' .NET has no Debug.Listeners; Debug.WriteLine routes through the
                ' shared Trace listener collection instead.
                Trace.Listeners.Add(acquiredListener)
#End If
                listenerAdded = True
                Trace.AutoFlush = True

                If InitializeFaultInjector IsNot Nothing Then InitializeFaultInjector(4)

                If Not eventsAttached Then
                    AddHandler Application.ThreadException, AddressOf OnThreadException
                    handlersAdded = 1
                    If InitializeFaultInjector IsNot Nothing Then InitializeFaultInjector(7)
                    AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException
                    handlersAdded = 2
                    If InitializeFaultInjector IsNot Nothing Then InitializeFaultInjector(8)
                    AddHandler TaskScheduler.UnobservedTaskException, AddressOf OnUnobservedTaskException
                    handlersAdded = 3
                    If InitializeFaultInjector IsNot Nothing Then InitializeFaultInjector(9)
                    AddHandler AppDomain.CurrentDomain.ProcessExit, AddressOf OnProcessExit
                    handlersAdded = 4
                    If InitializeFaultInjector IsNot Nothing Then InitializeFaultInjector(10)
                    eventsAttached = True
                End If

                If InitializeFaultInjector IsNot Nothing Then InitializeFaultInjector(5)

                ' The session header is the last thing that can fail; everything above is already
                ' acquired, so a failure here has to give all of it back.
                If InitializeFaultInjector IsNot Nothing Then InitializeFaultInjector(6)

                For Each warning In previousWarnings
                    acquiredWriter.WriteLine(warning)
                Next
                acquiredWriter.WriteLine("============================================================")
                Using currentProcess As Process = Process.GetCurrentProcess()
                    acquiredWriter.WriteLine("Session started: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff", Globalization.CultureInfo.InvariantCulture) &
                                             "; PID=" & currentProcess.Id.ToString() &
                                             "; role=" & MultiWindowPolicy.InstanceRoleName())
                    acquiredWriter.WriteLine("Log file: " & logPathValue)
                    ' MainModule works for a single-file publish, where Assembly.Location is empty.
                    acquiredWriter.WriteLine("Executable: " & currentProcess.MainModule.FileName)
                End Using
                acquiredWriter.WriteLine("Version: " & Assembly.GetExecutingAssembly().GetName().Version.ToString())
                acquiredWriter.WriteLine("OS: " & Environment.OSVersion.ToString())
                ' Ticket 182 L-8: the runtime too, so a log says which of the two exes wrote it
                ' without anyone reading the file name (".NET 10.x" for the mainline, ".NET Framework 4.8.x").
                Try
#If NETFRAMEWORK Then
                    acquiredWriter.WriteLine("Runtime: .NET Framework " & Environment.Version.ToString())
#Else
                    acquiredWriter.WriteLine("Runtime: " & RuntimeInformation.FrameworkDescription)
#End If
                Catch
                End Try
                acquiredWriter.WriteLine("64-bit OS: " & Environment.Is64BitOperatingSystem.ToString() & "; 64-bit process: " & Environment.Is64BitProcess.ToString())
                acquiredWriter.WriteLine("Command line: " & Environment.CommandLine)
                acquiredWriter.Flush()

                logWriter = acquiredWriter
                logListener = acquiredListener
                isInitialized = True
            Catch ex As Exception
                ' Transactional rollback of acquired resources (057.S13-12).
                If handlersAdded > 0 Then
                    If handlersAdded >= 4 Then
                        Try : RemoveHandler AppDomain.CurrentDomain.ProcessExit, AddressOf OnProcessExit : Catch : End Try
                    End If
                    If handlersAdded >= 3 Then
                        Try : RemoveHandler TaskScheduler.UnobservedTaskException, AddressOf OnUnobservedTaskException : Catch : End Try
                    End If
                    If handlersAdded >= 2 Then
                        Try : RemoveHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException : Catch : End Try
                    End If
                    Try : RemoveHandler Application.ThreadException, AddressOf OnThreadException : Catch : End Try
                    eventsAttached = False
                End If
                If listenerAdded AndAlso acquiredListener IsNot Nothing Then
                    Try
#If NETFRAMEWORK Then
                        Debug.Listeners.Remove(acquiredListener)
#Else
                        Trace.Listeners.Remove(acquiredListener)
#End If
                    Catch
                    End Try
                End If
                If acquiredListener IsNot Nothing Then
                    Try : acquiredListener.Dispose() : Catch : End Try
                End If
                If acquiredWriter IsNot Nothing Then
                    Try : acquiredWriter.Dispose() : Catch : End Try
                ElseIf acquiredStream IsNot Nothing Then
                    Try : acquiredStream.Dispose() : Catch : End Try
                End If
                logWriter = Nothing
                logListener = Nothing
                isInitialized = False
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

    Public Sub LogMessage(message As String)
        WriteLine(message)
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
    ''' when the current log was rolled away.
    '''
    ''' Runs once per start, before the file is opened - the cheapest possible place, and the
    ''' only one where no writer holds the handle. Never throws: a log that cannot be rotated
    ''' must not stop the app from starting.
    '''
    ''' Ownership (057.S13-10): the current log is MOVED to a staging name no other process can
    ''' also be using ("previous.log.rotating.&lt;guid&gt;"), and only then published over
    ''' previous.log. A failure at any later step RETAINS the staging file: nothing is deleted
    ''' that this call did not create, previous.log is replaced atomically (never deleted first),
    ''' and the next start completes the stranded rotation instead of discarding it.
    ''' </summary>
    Friend Function RotateIfOversized(log_Path As String, Optional ops As ILogFileOps = Nothing) As Boolean
        Dim files As ILogFileOps = If(ops, RealLogFileOps.Instance)
        Try
            If String.IsNullOrEmpty(log_Path) Then Return False
            Dim folder As String = Path.GetDirectoryName(log_Path)
            If String.IsNullOrEmpty(folder) Then Return False
            Dim rolled As String = Path.Combine(folder, "previous.log")

            ' A rotation an earlier start could not finish is finished first. Nothing is overwritten
            ' or removed to do it: with previous.log in place the stranded data simply stays.
            CompleteStrandedRotations(folder, rolled, files)

            If Not files.Exists(log_Path) OrElse files.Length(log_Path) <= Max_Log_Bytes Then Return False

            Dim staging As String = Path.Combine(folder, Staging_Prefix & Guid.NewGuid().ToString("N"))
            Try
                files.Move(log_Path, staging)
            Catch
                Return False
            End Try

            Try
                Publish(staging, rolled, files)
                Return True
            Catch publishEx As Exception
                ' previous.log could not be replaced (locked, say). Put the data back as the
                ' current log when that name is still free - Move never overwrites, so a log
                ' another process has started meanwhile is untouched - and otherwise leave the
                ' staging file where it is for the next start to finish.
                Try
                    If Not files.Exists(log_Path) Then files.Move(staging, log_Path)
                Catch
                End Try
                Return False
            End Try
        Catch
            Return False
        End Try
    End Function

    Friend Const Staging_Prefix As String = "previous.log.rotating."

    Private Sub Publish(staging As String, rolled As String, files As ILogFileOps)
        If Not files.Exists(staging) Then
            Throw New FileNotFoundException("Staging file not found", staging)
        End If
        If Not files.Exists(rolled) Then
            Try
                files.Move(staging, rolled)
                Return
            Catch ex As IOException
                If Not files.Exists(rolled) Then Throw
            End Try
        End If
        Dim backup As String = rolled & ".replaced." & Guid.NewGuid().ToString("N")
        files.Replace(staging, rolled, backup)
        ' The replaced generation was ours to discard; failing to drop it only leaves it behind.
        Try : files.Delete(backup) : Catch : End Try
    End Sub

    Private Sub CompleteStrandedRotations(folder As String, rolled As String, files As ILogFileOps)
        For Each stranded As String In files.StagingFiles(folder, Staging_Prefix)
            Try
                If Not files.Exists(rolled) Then files.Move(stranded, rolled)
            Catch
                ' Retained: the next start tries again.
            End Try
        Next
    End Sub
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
        Dim exeDir As String = If(LogDirectoryOverride, AppContext.BaseDirectory)

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
                    Using currentProcess = Process.GetCurrentProcess()
                        logWriter.WriteLine("Session finished: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff", Globalization.CultureInfo.InvariantCulture) & "; PID=" & currentProcess.Id.ToString(Globalization.CultureInfo.InvariantCulture))
                    End Using
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
        If offset < 0 Then Throw New ArgumentOutOfRangeException(NameOf(offset))
        If count < 0 Then Throw New ArgumentOutOfRangeException(NameOf(count))
        If offset > buffer.Length OrElse count > buffer.Length - offset Then Throw New ArgumentException("The buffer range is invalid.")
        If handle.IsClosed Then Throw New ObjectDisposedException(NameOf(AppendOnlyFileStream))
        If count = 0 Then Return
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
        If written <> count Then Throw New IOException("The log write was incomplete.")
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

''' <summary>The file operations the log rotation performs - injectable so each failure of the
''' rotation can be produced on demand (two rotators, a locked previous.log, a vanished staging).</summary>
Friend Interface ILogFileOps
    Function Exists(path As String) As Boolean
    Function Length(path As String) As Long
    Sub Move(source As String, destination As String)
    Sub Replace(source As String, destination As String, backup As String)
    Sub Delete(path As String)
    Function StagingFiles(folder As String, prefix As String) As IEnumerable(Of String)
End Interface

Friend NotInheritable Class RealLogFileOps
    Implements ILogFileOps

    Friend Shared ReadOnly Instance As New RealLogFileOps()

    Public Function Exists(path As String) As Boolean Implements ILogFileOps.Exists
        Return File.Exists(path)
    End Function

    Public Function Length(path As String) As Long Implements ILogFileOps.Length
        Return New FileInfo(path).Length
    End Function

    Public Sub Move(source As String, destination As String) Implements ILogFileOps.Move
        File.Move(source, destination)
    End Sub

    Public Sub Replace(source As String, destination As String, backup As String) Implements ILogFileOps.Replace
        File.Replace(source, destination, backup)
    End Sub

    Public Sub Delete(path As String) Implements ILogFileOps.Delete
        File.Delete(path)
    End Sub

    Public Function StagingFiles(folder As String, prefix As String) As IEnumerable(Of String) Implements ILogFileOps.StagingFiles
        Return Directory.GetFiles(folder, prefix & "*")
    End Function
End Class
