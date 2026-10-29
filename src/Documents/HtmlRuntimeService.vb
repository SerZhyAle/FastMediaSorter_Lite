#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Win32

' Local documents and online pages never share a user-data folder: SmartScreen and storage
' settings are UDF-wide (section 9.3).
Friend Enum HtmlRuntimeProfile
    Local
    Online
End Enum

Friend NotInheritable Class HtmlRuntimeAvailability
    Public Property Status As HtmlOutcome
    Public Property RuntimeVersion As String = ""
End Class

' Specification 275 sections 9 and 12: lazy loader/runtime detection, owned UDFs, environment
' creation and typed failures. Nothing here runs unless the HTML opt-in asked for it.
Friend NotInheritable Class HtmlRuntimeService
    ' First stable Runtime exposing the Find API this host requires (section 9.1); a baseline for
    ' refusing an outdated install, never a recommendation to install an old version.
    Friend Const MinimumRuntimeMajor As Integer = 139
    Friend Const SdkVersion As String = "1.0.4314.35"
    Private Const MaxSweptDirectories As Integer = 16
    Private Const PfSse3InstructionsAvailable As Integer = 13
    Private Const ImageFileMachineAmd64 As UShort = &H8664US

    Private Shared ReadOnly loaderLock As New Object()
    Private Shared loaderOutcome As HtmlOutcome = HtmlOutcome.Disabled
    Private Shared loaderPrepared As Boolean
    Private Shared sweepTask As Task
    ' UI-thread state: environments are created, counted and retired on the UI STA only.
    Private Shared ReadOnly environments As New Dictionary(Of HtmlRuntimeProfile, Task(Of CoreWebView2Environment))()
    Private Shared ReadOnly environmentFolders As New Dictionary(Of HtmlRuntimeProfile, String)()
    Private Shared ReadOnly liveControllers As New Dictionary(Of HtmlRuntimeProfile, Integer)()

    Private Sub New()
    End Sub

    <DllImport("kernel32.dll")>
    Private Shared Function IsProcessorFeaturePresent(feature As Integer) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    Friend Shared ReadOnly Property LoaderDirectory As String
        Get
            Return Path.Combine(AppContext.BaseDirectory, "webview2", "win-x64")
        End Get
    End Property

    Friend Shared ReadOnly Property DataRoot As String
        Get
            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SZA", "FastMediaSorter", "documents", "webview")
        End Get
    End Property

    ' Section 9.1: an explicit client/server/edition decision, not a broad build >= 14393 check.
    Friend Shared Function PlatformOutcome() As HtmlOutcome
        If Not Environment.Is64BitProcess OrElse RuntimeInformation.ProcessArchitecture <> Architecture.X64 Then Return HtmlOutcome.UnsupportedPlatform
        If Not IsProcessorFeaturePresent(PfSse3InstructionsAvailable) Then Return HtmlOutcome.UnsupportedPlatform
        Dim build = Environment.OSVersion.Version.Build
        Dim edition = "", installation = ""
        Try
            Using key = Registry.LocalMachine.OpenSubKey("SOFTWARE\Microsoft\Windows NT\CurrentVersion")
                edition = Convert.ToString(key?.GetValue("EditionID"))
                installation = Convert.ToString(key?.GetValue("InstallationType"))
            End Using
        Catch ex As Exception When TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Security.SecurityException OrElse TypeOf ex Is IOException
        End Try
        Return PlatformOutcomeFor(Environment.OSVersion.Version.Major, build, edition, installation)
    End Function

    Friend Shared Function PlatformOutcomeFor(major As Integer, build As Integer, edition As String, installation As String) As HtmlOutcome
        If major < 10 Then Return HtmlOutcome.UnsupportedPlatform
        Dim kind = If(installation, "")
        ' Server Core has no interactive desktop for a document surface.
        If kind.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 Then Return HtmlOutcome.UnsupportedPlatform
        If kind.StartsWith("Server", StringComparison.OrdinalIgnoreCase) Then Return If(build >= 14393, HtmlOutcome.Ok, HtmlOutcome.UnsupportedPlatform)
        If build >= 16299 Then Return HtmlOutcome.Ok
        ' Windows 10 Enterprise 2016 LTSB is the listed 1607 exception; other 1607/1703 clients are not.
        If build = 14393 AndAlso If(edition, "").EndsWith("EnterpriseS", StringComparison.OrdinalIgnoreCase) Then Return HtmlOutcome.Ok
        If build = 14393 AndAlso If(edition, "").EndsWith("EnterpriseSN", StringComparison.OrdinalIgnoreCase) Then Return HtmlOutcome.Ok
        Return HtmlOutcome.UnsupportedPlatform
    End Function

    ' Sets the absolute loader folder once per process, before any environment static call.
    Friend Shared Function PrepareLoader() As HtmlOutcome
        SyncLock loaderLock
            If loaderPrepared Then Return loaderOutcome
            loaderPrepared = True
            loaderOutcome = PlatformOutcome()
            If loaderOutcome <> HtmlOutcome.Ok Then Return loaderOutcome
            loaderOutcome = LoaderFileOutcome(Path.Combine(LoaderDirectory, "WebView2Loader.dll"))
            If loaderOutcome <> HtmlOutcome.Ok Then Return loaderOutcome
            Try
                CoreWebView2Environment.SetLoaderDllFolderPath(LoaderDirectory)
            Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is COMException OrElse TypeOf ex Is ArgumentException
                loaderOutcome = HtmlOutcome.LoaderMissing
            End Try
            Return loaderOutcome
        End SyncLock
    End Function

    ' A missing or non-x64 loader is a repair failure; there is no PATH or root fallback.
    Friend Shared Function LoaderFileOutcome(file As String) As HtmlOutcome
        Try
            If Not IO.File.Exists(file) Then Return HtmlOutcome.LoaderMissing
            Using stream As New FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)
                Using reader As New BinaryReader(stream)
                    If stream.Length < 64 OrElse reader.ReadUInt16() <> &H5A4DUS Then Return HtmlOutcome.LoaderWrongArchitecture
                    stream.Position = &H3C
                    Dim header = reader.ReadInt32()
                    If header <= 0 OrElse header > stream.Length - 6 Then Return HtmlOutcome.LoaderWrongArchitecture
                    stream.Position = header
                    If reader.ReadUInt32() <> &H4550UI Then Return HtmlOutcome.LoaderWrongArchitecture
                    Return If(reader.ReadUInt16() = ImageFileMachineAmd64, HtmlOutcome.Ok, HtmlOutcome.LoaderWrongArchitecture)
                End Using
            End Using
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return HtmlOutcome.LoaderMissing
        End Try
    End Function

    ' Detects the installed Evergreen Runtime only; never downloads or installs one.
    Friend Shared Function Detect() As HtmlRuntimeAvailability
        Dim result As New HtmlRuntimeAvailability With {.Status = PrepareLoader()}
        If result.Status <> HtmlOutcome.Ok Then Return result
        Try
            result.RuntimeVersion = If(CoreWebView2Environment.GetAvailableBrowserVersionString(Nothing), "")
            result.Status = If(RuntimeMajor(result.RuntimeVersion) >= MinimumRuntimeMajor, HtmlOutcome.Ok, HtmlOutcome.RequiredApiUnavailable)
            If result.RuntimeVersion.Length = 0 Then result.Status = HtmlOutcome.RuntimeMissing
        Catch ex As Exception
            result.Status = OutcomeOf(ex)
        End Try
        Return result
    End Function

    Friend Shared Function RuntimeMajor(version As String) As Integer
        Dim text = If(version, "")
        Dim cut = text.IndexOf("."c)
        Dim major As Integer
        Return If(Integer.TryParse(If(cut > 0, text.Substring(0, cut), text), Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, major), major, 0)
    End Function

    Friend Shared Function OutcomeOf(ex As Exception) As HtmlOutcome
        If TypeOf ex Is WebView2RuntimeNotFoundException Then Return HtmlOutcome.RuntimeMissing
        If TypeOf ex Is DllNotFoundException OrElse TypeOf ex Is EntryPointNotFoundException Then Return HtmlOutcome.LoaderMissing
        If TypeOf ex Is BadImageFormatException Then Return HtmlOutcome.LoaderWrongArchitecture
        If TypeOf ex Is OperationCanceledException Then Return HtmlOutcome.Cancelled
        If TypeOf ex Is TimeoutException Then Return HtmlOutcome.TimedOut
        If TypeOf ex Is InvalidCastException OrElse TypeOf ex Is NotImplementedException OrElse TypeOf ex Is MissingMethodException Then Return HtmlOutcome.RequiredApiUnavailable
        Return HtmlOutcome.RuntimeBlocked
    End Function

    ' One environment per profile, shared by the controllers of that profile while any is alive.
    ' Call on the UI STA; the caller must pair a successful acquire with ReleaseController.
    Friend Shared Async Function AcquireEnvironmentAsync(profile As HtmlRuntimeProfile, token As CancellationToken) As Task(Of CoreWebView2Environment)
        If PrepareLoader() <> HtmlOutcome.Ok Then Throw New InvalidOperationException("HTML runtime loader unavailable.")
        BeginLaunchSweep()
        Await sweepTask
        Dim pending As Task(Of CoreWebView2Environment) = Nothing
        If Not environments.TryGetValue(profile, pending) OrElse pending.IsFaulted OrElse pending.IsCanceled Then
            Dim folder = Path.Combine(DataRoot, ProfileFolder(profile), Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(folder)
            Dim options As New CoreWebView2EnvironmentOptions With {
                .AllowSingleSignOnUsingOSPrimaryAccount = False, .AreBrowserExtensionsEnabled = False,
                .IsCustomCrashReportingEnabled = True, .ExclusiveUserDataFolderAccess = True}
            pending = CoreWebView2Environment.CreateAsync(Nothing, folder, options)
            environments(profile) = pending
            environmentFolders(profile) = folder
        End If
        liveControllers(profile) = LiveCount(profile) + 1
        Try
            Dim environment = Await pending.WaitAsync(token)
            RemoveHandler environment.BrowserProcessExited, AddressOf OnBrowserProcessExited
            AddHandler environment.BrowserProcessExited, AddressOf OnBrowserProcessExited
            Return environment
        Catch
            ReleaseController(profile)
            Throw
        End Try
    End Function

    Friend Shared Sub ReleaseController(profile As HtmlRuntimeProfile)
        liveControllers(profile) = Math.Max(0, LiveCount(profile) - 1)
    End Sub

    Private Shared Function LiveCount(profile As HtmlRuntimeProfile) As Integer
        Dim count As Integer
        Return If(liveControllers.TryGetValue(profile, count), count, 0)
    End Function

    Private Shared Function ProfileFolder(profile As HtmlRuntimeProfile) As String
        Return If(profile = HtmlRuntimeProfile.Online, "online", "local")
    End Function

    ' Raised on the UI thread once every browser process of an environment has exited. With no
    ' controller left the environment retires and its owned folder is deleted; a crash with a
    ' live controller keeps it (the next controller starts a new browser process).
    Private Shared Sub OnBrowserProcessExited(sender As Object, e As CoreWebView2BrowserProcessExitedEventArgs)
        For Each profile In New List(Of HtmlRuntimeProfile)(environments.Keys)
            Dim pending = environments(profile)
            If Not pending.IsCompletedSuccessfully OrElse Not ReferenceEquals(pending.Result, sender) Then Continue For
            If LiveCount(profile) > 0 Then Return
            Dim folder = environmentFolders(profile)
            environments.Remove(profile)
            environmentFolders.Remove(profile)
            DeleteOwnedFolder(folder)
            Return
        Next
    End Sub

    Friend Shared ReadOnly Property HasLiveEnvironment As Boolean
        Get
            Return environments.Count > 0
        End Get
    End Property

    ' Section 12: the process exits before BrowserProcessExited when the window closes, so a run's
    ' folders are left for the next launch - swept once per launch whether or not the viewer is
    ' still switched on. Off the UI thread; the first environment and Clear runtime data wait for
    ' it, so no environment folder exists while it runs. Needs no loader and starts no browser.
    Friend Shared Sub BeginLaunchSweep()
        If sweepTask Is Nothing Then sweepTask = Task.Run(AddressOf SweepStaleFolders)
    End Sub

    ' Previous runs' folders: their owner is gone when the folder can be deleted at all
    ' (ExclusiveUserDataFolderAccess keeps a live one locked). Bounded per sweep.
    Private Shared Sub SweepStaleFolders()
        Dim budget = MaxSweptDirectories
        Dim legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SZA", "FastMediaSorter", "document-host")
        For Each parent In {Path.Combine(DataRoot, "local"), Path.Combine(DataRoot, "online"), legacy}
            For Each folder In OwnedChildren(parent)
                If budget <= 0 Then Return
                budget -= 1
                DeleteOwnedFolder(folder)
            Next
        Next
    End Sub

    Private Shared Function OwnedChildren(parent As String) As List(Of String)
        Dim result As New List(Of String)()
        Try
            If Not Directory.Exists(parent) OrElse (IO.File.GetAttributes(parent) And FileAttributes.ReparsePoint) <> 0 Then Return result
            For Each child In Directory.GetDirectories(parent)
                Dim id As Guid
                If Guid.TryParseExact(Path.GetFileName(child), "N", id) AndAlso Not environmentFolders.ContainsValue(child) Then result.Add(child)
            Next
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
        Return result
    End Function

    ' Deletes only a GUID-named directory directly below one of the owned parents, never a
    ' reparse point and never an active environment's folder.
    Private Shared Function DeleteOwnedFolder(folder As String) As Boolean
        Try
            Dim target = Path.GetFullPath(folder)
            Dim parent = Path.GetDirectoryName(target)
            Dim legacy = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SZA", "FastMediaSorter", "document-host"))
            Dim allowed = String.Equals(parent, Path.GetFullPath(Path.Combine(DataRoot, "local")), StringComparison.OrdinalIgnoreCase) OrElse
                String.Equals(parent, Path.GetFullPath(Path.Combine(DataRoot, "online")), StringComparison.OrdinalIgnoreCase) OrElse
                String.Equals(parent, legacy, StringComparison.OrdinalIgnoreCase)
            Dim id As Guid
            If Not allowed OrElse Not Guid.TryParseExact(Path.GetFileName(target), "N", id) Then Return False
            If environmentFolders.ContainsValue(folder) OrElse environmentFolders.ContainsValue(target) Then Return False
            If Not Directory.Exists(target) Then Return True
            If (IO.File.GetAttributes(target) And FileAttributes.ReparsePoint) <> 0 Then Return False
            Directory.Delete(target, recursive:=True)
            Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        End Try
    End Function

    ' Settings "Runtime status" row: detection only on this explicit request, no install/download.
    Friend Shared Function DescribeAvailability() As String
        Dim availability = Detect()
        If availability.Status = HtmlOutcome.Ok Then Return Localization.TF("Компонент Microsoft Edge WebView2 установлен (версия {0}). Просмотр HTML-страниц доступен.", availability.RuntimeVersion)
        If availability.Status = HtmlOutcome.RequiredApiUnavailable AndAlso availability.RuntimeVersion.Length > 0 Then
            Return Localization.TF("Установленный компонент Microsoft Edge WebView2 устарел (версия {0}). Обновите его через Центр обновления Windows или с сайта Microsoft.", availability.RuntimeVersion)
        End If
        Return HtmlViewerText.Outcome(availability.Status)
    End Function

    ' Settings "Clear runtime data": retires owned environments, then clears owned UDFs only.
    ' An active environment is refused, never deleted under a running browser process.
    Friend Shared Function ClearOwnedData() As Boolean
        sweepTask?.Wait()
        Dim complete = Not HasLiveEnvironment
        For Each parent In {Path.Combine(DataRoot, "local"), Path.Combine(DataRoot, "online")}
            For Each folder In OwnedChildren(parent)
                If Not DeleteOwnedFolder(folder) Then complete = False
            Next
        Next
        Return complete
    End Function
End Class
#End If
