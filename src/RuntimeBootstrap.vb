Option Strict On

Imports System.IO
Imports System.Reflection

''' <summary>
''' Keeps the single-file build lightweight: only managed helper assemblies and
''' small UI assets are embedded. Heavy native runtimes (OCR/VLC) are handled
''' separately on first use by <see cref="OptionalRuntimeManager"/>.
''' </summary>
Friend NotInheritable Class RuntimeBootstrap

    Private Const ResourcePrefix As String = "FmsPayload"
    Private Const ManagedPrefix As String = ResourcePrefix & "managed/"

    Private Shared ReadOnly sync As New Object()
    Private Shared ReadOnly managedCache As New Dictionary(Of String, Assembly)(StringComparer.OrdinalIgnoreCase)

    Private Shared isInitialized As Boolean = False

    Public Shared Sub Initialize()
        SyncLock sync
            If isInitialized Then Return
            isInitialized = True
#If NETFRAMEWORK Then
            ' net48 single-exe only: ILMerge leaves a few managed deps embedded as
            ' FmsPayloadmanaged/*.dll resources. The .NET 10 build ships/publishes
            ' its managed dependencies natively, so no resolver is needed there
            ' (OpenBundledAsset below still serves the shared UI assets).
            AddHandler AppDomain.CurrentDomain.AssemblyResolve, AddressOf ResolveManagedAssembly
#End If
        End SyncLock
    End Sub

    Public Shared Function OpenBundledAsset(relativePath As String) As Stream
        If String.IsNullOrWhiteSpace(relativePath) Then Return Nothing

        Dim normalized As String = relativePath.Replace("\"c, "/"c).TrimStart("/"c)
        Dim resourceName As String = ResourcePrefix & normalized
        Dim asm As Assembly = Assembly.GetExecutingAssembly()
        Dim stream As Stream = asm.GetManifestResourceStream(resourceName)
        If stream Is Nothing Then Return Nothing

        Dim copy As New MemoryStream()
        stream.CopyTo(copy)
        stream.Dispose()
        copy.Position = 0
        Return copy
    End Function

    Private Shared Function ResolveManagedAssembly(sender As Object, args As ResolveEventArgs) As Assembly
        Dim requestedName As String
        Try
            requestedName = New AssemblyName(args.Name).Name
        Catch
            Return Nothing
        End Try

        If String.IsNullOrWhiteSpace(requestedName) Then Return Nothing

        SyncLock sync
            If managedCache.ContainsKey(requestedName) Then
                Return managedCache(requestedName)
            End If

            For Each loaded As Assembly In AppDomain.CurrentDomain.GetAssemblies()
                Try
                    If String.Equals(loaded.GetName().Name, requestedName, StringComparison.OrdinalIgnoreCase) Then
                        managedCache(requestedName) = loaded
                        Return loaded
                    End If
                Catch
                End Try
            Next

            Dim resourceName As String = ManagedPrefix & requestedName & ".dll"
            Dim asm As Assembly = Assembly.GetExecutingAssembly()

            Using stream As Stream = asm.GetManifestResourceStream(resourceName)
                If stream Is Nothing Then Return Nothing

                Dim bytes(CInt(stream.Length) - 1) As Byte
                Dim read As Integer = stream.Read(bytes, 0, bytes.Length)
                If read <> bytes.Length Then Return Nothing

                Dim loadedAssembly As Assembly = Assembly.Load(bytes)
                managedCache(requestedName) = loadedAssembly
                Return loadedAssembly
            End Using
        End SyncLock
    End Function

End Class
