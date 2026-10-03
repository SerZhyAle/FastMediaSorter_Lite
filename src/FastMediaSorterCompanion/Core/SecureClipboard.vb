Option Explicit On
Option Strict On
Option Compare Binary
Option Infer On

Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' The one door a credential takes onto the clipboard (contract <c>CLIPBOARD-GUARD</c> rules 2 and 6,
''' ticket 041). A password copied with a plain <c>Clipboard.SetText</c> is offered to Win+V history and
''' to the cloud clipboard, where it outlives the moment it was needed and reaches other devices; the
''' three privacy formats below tell Windows not to. They are advisory - a clipboard manager may
''' ignore them - which is why the password stays masked on screen as well.
'''
''' Everything in the Share Manager that puts a secret (the SFTP password, login + password, the
''' <c>.fmscfg</c> QR image) goes through here, and a test holds that no other
''' <c>Clipboard.Set*</c> call exists in this project. UI thread only, like every clipboard call.
''' </summary>
Friend NotInheritable Class SecureClipboard

    Private Sub New()
    End Sub

    ''' <summary>Keeps the content out of clipboard-monitoring software that honours the flag.</summary>
    Friend Const ExcludeFromMonitorFormat As String = "ExcludeClipboardContentFromMonitorProcessing"
    ''' <summary>DWORD 0 = do not record in the Win+V history.</summary>
    Friend Const HistoryFormat As String = "CanIncludeInClipboardHistory"
    ''' <summary>DWORD 0 = do not sync to the user's other devices.</summary>
    Friend Const CloudFormat As String = "CanUploadToCloudClipboard"

    ''' <summary>Bounded: a clipboard held by another process is common and short-lived, an unbounded
    ''' wait would freeze the window. Three tries, 120 ms apart, is under half a second.</summary>
    Friend Const MaxAttempts As Integer = 3
    Friend Const RetryDelayMs As Integer = 120

    ''' <summary>The formats every secret carries, in the order they are written.</summary>
    Friend Shared ReadOnly PrivacyFormats As String() = {ExcludeFromMonitorFormat, HistoryFormat, CloudFormat}

    ''' <summary>Adds the three privacy formats to <paramref name="data"/>. Each is a 4-byte zero
    ''' (a little-endian DWORD 0), written as a stream so it reaches the clipboard as raw bytes.</summary>
    Friend Shared Sub ApplyPrivacyFlags(data As DataObject)
        For Each format As String In PrivacyFormats
            data.SetData(format, False, New MemoryStream(New Byte() {0, 0, 0, 0}))
        Next
    End Sub

    ''' <summary>A text DataObject (<c>CF_UNICODETEXT</c>) that carries the privacy flags.</summary>
    Friend Shared Function BuildSecretText(text As String) As DataObject
        Dim data As New DataObject()
        data.SetText(text, TextDataFormat.UnicodeText)
        ApplyPrivacyFlags(data)
        Return data
    End Function

    ''' <summary>Puts <paramref name="text"/> on the clipboard as a secret. False when every attempt
    ''' failed - the caller says so on screen; a failure is never swallowed.</summary>
    Friend Shared Function SetSecret(text As String) As Boolean
        If String.IsNullOrEmpty(text) Then Return False
        Try
            Return TrySetDataObject(BuildSecretText(text))
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Writes an already-built DataObject, flushed to the OLE clipboard so it survives this
    ''' process closing, retrying while another process holds the clipboard. The caller adds the
    ''' privacy flags with <see cref="ApplyPrivacyFlags"/> when the content is a secret.</summary>
    Friend Shared Function TrySetDataObject(data As DataObject) As Boolean
        Try
            ' The framework's own retry, made explicit: the 2-argument overload silently uses
            ' ten tries 100 ms apart, which is a full second of frozen window per copy.
            Clipboard.SetDataObject(data, True, MaxAttempts, RetryDelayMs)
            Return True
        Catch
            Return False
        End Try
    End Function

End Class
