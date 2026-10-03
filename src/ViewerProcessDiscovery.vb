Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Diagnostics

''' <summary>What is needed of a running process - and nothing that outlives it.</summary>
Friend Structure ViewerProcessTarget
    Public Pid As Integer
    Public MainWindowHandle As IntPtr
    Public TopLevelHandles As List(Of IntPtr)
End Structure

''' <summary>A process the discovery owns until it has been snapshotted. Every member may throw
''' because the process can exit at any instant (057.S13-7).</summary>
Friend Interface IViewerProcess
    Inherits IDisposable
    ReadOnly Property Id As Integer
    ReadOnly Property MainWindowHandle As IntPtr
End Interface

Friend NotInheritable Class RealViewerProcess
    Implements IViewerProcess

    Private ReadOnly inner As Process

    Friend Sub New(inner As Process)
        Me.inner = inner
    End Sub

    Public ReadOnly Property Id As Integer Implements IViewerProcess.Id
        Get
            If inner Is Nothing Then Throw New InvalidOperationException("No inner process.")
            Return inner.Id
        End Get
    End Property

    Public ReadOnly Property MainWindowHandle As IntPtr Implements IViewerProcess.MainWindowHandle
        Get
            If inner Is Nothing Then Throw New InvalidOperationException("No inner process.")
            Return inner.MainWindowHandle
        End Get
    End Property

    Public Sub Dispose() Implements IDisposable.Dispose
        If inner IsNot Nothing Then
            Try : inner.Dispose() : Catch : End Try
        End If
    End Sub
End Class

''' <summary>
''' Finds the other viewer processes a launch can forward to. The WHOLE collection of process
''' objects returned by the enumerator is owned here and disposed on every path - the excluded
''' current process, a candidate that vanished mid-lookup and the ones a failure never reached.
''' A process that exits while it is being looked at costs only that candidate; it never aborts
''' the launch, which would then not open its file at all.
''' </summary>
Friend Module ViewerProcessDiscovery

    Friend Function Discover(names As IEnumerable(Of String), currentId As Integer,
                             enumerate As Func(Of String, IList(Of IViewerProcess)),
                             topLevelWindows As Func(Of Integer, List(Of IntPtr))) As List(Of ViewerProcessTarget)
        Dim targets As New List(Of ViewerProcessTarget)()
        If names Is Nothing OrElse enumerate Is Nothing Then Return targets
        If topLevelWindows Is Nothing Then topLevelWindows = Function(p) New List(Of IntPtr)()

        For Each viewerName As String In names
            If String.IsNullOrEmpty(viewerName) Then Continue For
            Dim processes As IList(Of IViewerProcess) = Nothing
            Try
                processes = enumerate(viewerName)
                If processes Is Nothing Then Continue For
                For Each proc As IViewerProcess In processes
                    If proc Is Nothing Then Continue For
                    Try
                        Dim pid As Integer = proc.Id
                        If pid = currentId Then Continue For
                        Dim mainHandle As IntPtr = IntPtr.Zero
                        Try
                            mainHandle = proc.MainWindowHandle
                        Catch
                            ' Gone, or no window yet: the top-level scan below is the fallback.
                        End Try
                        Dim topLevel As List(Of IntPtr) = New List(Of IntPtr)()
                        Try
                            topLevel = topLevelWindows(pid)
                        Catch
                        End Try
                        targets.Add(New ViewerProcessTarget With {
                            .Pid = pid,
                            .MainWindowHandle = mainHandle,
                            .TopLevelHandles = If(topLevel, New List(Of IntPtr)())})
                    Catch
                        ' This candidate exited under us (Id or the scan threw): skip only it.
                    End Try
                Next
            Catch
                ' The enumeration itself failed for this name; the other name is still tried.
            Finally
                If processes IsNot Nothing Then
                    For Each proc As IViewerProcess In processes
                        If proc IsNot Nothing Then
                            Try : proc.Dispose() : Catch : End Try
                        End If
                    Next
                End If
            End Try
        Next
        Return targets
    End Function

End Module
