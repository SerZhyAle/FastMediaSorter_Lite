Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Threading
Imports System.Threading.Tasks

''' <summary>Metadata authorized by the enrolled device, never an operator token or downloaded content.</summary>
Friend NotInheritable Class ClipboardSlotStatus
    Public ReadOnly Label As String
    Public ReadOnly Result As ExchangeClipboardResult
    ''' <summary>The Google Drive row (ticket 269 P4); Nothing on a server's.</summary>
    Public ReadOnly Drive As ClipboardDriveAnswer

    Public Sub New(label As String, result As ExchangeClipboardResult)
        Me.Label = label
        Me.Result = result
    End Sub

    Public Sub New(label As String, drive As ClipboardDriveAnswer)
        Me.Label = label
        Me.Drive = drive
    End Sub

    Public Function DisplayText() As String
        If Drive IsNot Nothing Then
            If Drive.Failure <> ExchangeDriveFailure.None Then Return Localization.TF("{0}: размер буфера недоступен.", Label)
            If Drive.Value Is Nothing Then Return ClipboardMessages.TextOf(New ClipboardLine(ClipboardLineCode.EmptyViaDrive))
            Return Localization.TF("{0}: размер текущего буфера: {1:N0} байт.", Label, Drive.Value.Manifest.TotalSizeBytes)
        End If
        If Result.Outcome = ExchangeClipboardOutcome.Empty Then
            Return ClipboardMessages.TextOf(New ClipboardLine(ClipboardLineCode.ServerEmpty, label:=Label))
        End If
        If Not Result.IsDone OrElse Result.Value Is Nothing Then
            Return Localization.TF("{0}: размер буфера недоступен.", Label)
        End If
        ' For sealed slots this is the transfer size, never a claim about the clear payload size.
        If Result.Value.IsSealedKind Then
            Return Localization.TF("{0}: размер зашифрованного буфера: {1:N0} байт.", Label, Result.Value.Size)
        End If
        Return Localization.TF("{0}: размер текущего буфера: {1:N0} байт.", Label, Result.Value.Size)
    End Function
End Class
