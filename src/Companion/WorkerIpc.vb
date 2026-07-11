Option Strict On

Imports System.IO
Imports System.IO.Pipes
Imports System.Text
Imports System.Web.Script.Serialization

''' <summary>
''' Named-pipe JSON control client for the bundled Android-share sidecar
''' (companion\fms-share-worker.exe). One request -> one response per
''' connection; the worker closes the pipe after replying. Transport only -
''' the worker owns the SFTP server, keys, mDNS, port mapping and QR/config
''' rendering. Protocol: SPECIFICATION_ANDROID_FOLDER_SHARE.md Appendix A (v1).
''' </summary>
Public NotInheritable Class WorkerIpc

    ''' <summary>Pipe name (server side is \\.\pipe\fms-companion).</summary>
    Public Const PipeName As String = "fms-companion"

    ''' <summary>IPC schema version this client speaks. Mismatch = "update the app".</summary>
    Public Const SchemaVersion As Integer = 1

    Private Sub New()
    End Sub

    ''' <summary>
    ''' Sends one request and returns the worker's response. Throws on transport
    ''' failure (no worker listening / connect timeout / broken pipe); callers
    ''' that want a soft failure should wrap in Try or use WorkerProcess helpers.
    ''' </summary>
    Public Shared Function Send(request As WorkerRequest, Optional connectTimeoutMs As Integer = 5000) As WorkerResponse
        If request Is Nothing Then Throw New ArgumentNullException(NameOf(request))
        request.schemaVersion = SchemaVersion

        Using pipe As New NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None)
            pipe.Connect(connectTimeoutMs)

            Dim ser As New JavaScriptSerializer()
            Dim payload As Byte() = Encoding.UTF8.GetBytes(ser.Serialize(request))
            pipe.Write(payload, 0, payload.Length)
            pipe.Flush()

            ' No length prefix - read until the worker closes its end.
            Dim respText As String
            Using ms As New MemoryStream()
                Dim buffer(8191) As Byte
                Do
                    Dim n As Integer = pipe.Read(buffer, 0, buffer.Length)
                    If n <= 0 Then Exit Do
                    ms.Write(buffer, 0, n)
                Loop
                respText = Encoding.UTF8.GetString(ms.ToArray())
            End Using

            If String.IsNullOrEmpty(respText) Then
                Throw New IOException("Companion worker closed the pipe without a response.")
            End If

            ' Deserialize is case-insensitive (verified), so PascalCase DTO names
            ' bind to the worker's camelCase JSON fields.
            Return New JavaScriptSerializer().Deserialize(Of WorkerResponse)(respText)
        End Using
    End Function

End Class

' --- request DTOs (property names are the on-the-wire JSON field names) --------

''' <summary>Request envelope. type is one of the Appendix A message names.</summary>
Public Class WorkerRequest
    Public Property schemaVersion As Integer = WorkerIpc.SchemaVersion
    ' "type" and "readOnly" collide with VB keywords; bracket-escape keeps the
    ' emitted JSON field name (the worker matches it case-insensitively anyway).
    Public Property [type] As String = ""
    ''' <summary>Only sent for SetSharedFolders; replaces the whole list.</summary>
    Public Property folders As List(Of ShareFolder) = Nothing
End Class

''' <summary>A shared root. Used both in requests (folders) and status (roots).</summary>
Public Class ShareFolder
    Public Property name As String = ""
    Public Property hostPath As String = ""
    Public Property [readOnly] As Boolean = True
End Class

' --- response DTOs (Appendix A.2) ---------------------------------------------

Public Class WorkerResponse
    Public Property SchemaVersion As Integer
    Public Property Ok As Boolean
    ''' <summary>Present only on failure (omitempty on the wire).</summary>
    Public Property [Error] As String
    Public Property Status As WorkerStatus
    Public Property Export As WorkerExport
End Class

Public Class WorkerStatus
    Public Property Running As Boolean
    Public Property ListenPort As Integer
    Public Property Fingerprint As String
    Public Property Username As String
    Public Property Password As String
    Public Property Roots As List(Of ShareFolder)
    ''' <summary>Always present (may be empty). Last server-side error text.</summary>
    Public Property LastError As String
    Public Property Reachability As WorkerReachability
End Class

Public Class WorkerReachability
    Public Property LanAddress As String
    Public Property LanMdnsActive As Boolean
    Public Property PortMapMethod As String
    Public Property ExternalHost As String
    Public Property ExternalPort As Integer
    Public Property IsCgnat As Boolean
    Public Property ManualForwardHint As String
End Class

Public Class WorkerExport
    ''' <summary>The exact .fmscfg JSON (opaque to LITE).</summary>
    Public Property ConfigJson As String
    ''' <summary>Rendered QR as base64 PNG - decode straight into a PictureBox.</summary>
    Public Property QrPngBase64 As String
    Public Property HasInternetPath As Boolean
    Public Property ManualForwardHint As String
    Public Property FileExtension As String
End Class
