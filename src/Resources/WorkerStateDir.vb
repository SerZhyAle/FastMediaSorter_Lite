#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO

''' <summary>
''' The ONE place a host decides which persisted-state directory the share worker uses
''' (WORKER-LAUNCH 1.0 section 1: "hosts must use the same declared resolution and pass the chosen
''' path explicitly when starting a worker"). Linked by the viewer and by the Share Manager, so the
''' state-store line of the Hosting page, the viewer's read of the worker's shares file and the
''' worker's own spawn arguments cannot name three different folders.
'''
''' The order is the worker's (<c>sftpserver.DataDir()</c> in its <c>hostkey.go</c>), and so is the
''' meaning of "usable":
'''   1. The explicit override, <c>FMS_COMPANION_DATA_DIR</c> - any non-empty value, as the worker reads it.
'''   2. The machine directory <c>%ProgramData%\FastMediaSorterCompanion</c> when it is USABLE: it exists
'''      as a directory and this process may open it for listing. The worker's <c>dirUsable</c> is
'''      exactly that - a <c>Stat</c> that says "directory" and an <c>Open</c> that succeeds. It is not a
'''      write probe and it creates nothing. A machine directory this account cannot read is NOT adopted:
'''      the worker then falls back to the per-user store, so a non-enrolled account keeps working, and a
'''      host that kept asking "does it exist?" would report (and read) a store the worker never uses.
'''   3. The per-user directory <c>%LOCALAPPDATA%\FastMediaSorterCompanion</c>, whether or not it exists yet
'''      (the worker creates it on its first host key).
''' </summary>
Friend Module WorkerStateDir

    ''' <summary>The worker's environment override of the state directory (its <c>DataDirOverrideEnv</c>).</summary>
    Friend Const OverrideVariable As String = "FMS_COMPANION_DATA_DIR"

    ''' <summary>The folder name under ProgramData / LocalAppData (the worker's <c>appDataDirName</c>).</summary>
    Friend Const FolderName As String = "FastMediaSorterCompanion"

    ''' <summary>The Server edition's machine-owned state directory; "" when the system folder cannot be named.</summary>
    Friend Function MachineDir() As String
        Try
            Dim base As String = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            If String.IsNullOrEmpty(base) Then Return ""
            Return Path.Combine(base, FolderName)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>The per-user state directory (the User edition's, and the worker's default); "" when it cannot be named.</summary>
    Friend Function UserDir() As String
        Try
            Dim base As String = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            If String.IsNullOrEmpty(base) Then Return ""
            Return Path.Combine(base, FolderName)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>The explicit override as the worker reads it (non-empty, not trimmed); "" when there is none.</summary>
    Friend Function ExplicitOverride() As String
        Try
            Return If(Environment.GetEnvironmentVariable(OverrideVariable), "")
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' The worker's <c>dirUsable</c>: <paramref name="dir"/> exists as a directory and can be opened for
    ''' listing by this process. Opening a directory for reading needs the list right, which is what
    ''' enumerating its first entry asks for; nothing is created, written or left open.
    ''' </summary>
    Friend Function IsUsable(dir As String) As Boolean
        If String.IsNullOrEmpty(dir) Then Return False
        Try
            If Not Directory.Exists(dir) Then Return False
            Using entries As IEnumerator(Of String) = Directory.EnumerateFileSystemEntries(dir).GetEnumerator()
                entries.MoveNext()
            End Using
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>The order of the resolution as a pure function of its inputs (what the tests drive).</summary>
    Friend Function Choose(explicitDir As String, machineDir As String, userDir As String,
                           usable As Func(Of String, Boolean)) As String
        If Not String.IsNullOrEmpty(explicitDir) Then Return explicitDir
        If Not String.IsNullOrEmpty(machineDir) AndAlso usable(machineDir) Then Return machineDir
        Return If(userDir, "")
    End Function

    ''' <summary>
    ''' The state directory this PC's worker uses right now, by the order above. "" only when neither
    ''' the override nor any system folder can be named - a caller then passes no path at all and
    ''' leaves the worker to resolve for itself.
    ''' </summary>
    Friend Function Resolve() As String
        Return Choose(ExplicitOverride(), MachineDir(), UserDir(), AddressOf IsUsable)
    End Function

End Module
#End If
