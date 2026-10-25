#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq

''' <summary>Final, post-collision bytes and peak scratch for sequential verified transfers.</summary>
Friend NotInheritable Class ManagerTransferPreflight
    Friend ReadOnly Property KnownBytes As Long
    Friend ReadOnly Property HasUnknownBytes As Boolean
    Friend ReadOnly Property ScratchBytes As Long
    Friend ReadOnly Property HasTransfers As Boolean
    Friend ReadOnly Property NeedsWarning As Boolean
    Friend ReadOnly Property RelaysThroughPc As Boolean
    Friend Const ScratchOverheadBytes As Long = 1024L * 1024L

    Friend Sub New(operations As IEnumerable(Of BatchOp), source As ProviderCapabilities, destination As ProviderCapabilities,
                   sameResource As Boolean)
        Dim largest As Long
        Try
            For Each op In operations.Where(Function(o) o.Kind = FileOpKind.Copy OrElse o.Kind = FileOpKind.Move)
                HasTransfers = True
                If op.SizeBytes < 0 Then
                    HasUnknownBytes = True
                Else
                    KnownBytes += op.SizeBytes
                    largest = Math.Max(largest, op.SizeBytes)
                End If
            Next
            If HasTransfers Then ScratchBytes = largest * 2L + ScratchOverheadBytes
        Catch ex As OverflowException
            Throw ManagerCommandPlanner.Failure("", ManagerFailure.ScratchSpace)
        End Try
        Dim costly As Boolean = IsCostly(source) OrElse IsCostly(destination)
        NeedsWarning = HasTransfers AndAlso (RemoteBatchPolicy.NeedsWarning(KnownBytes, source, destination) OrElse (HasUnknownBytes AndAlso costly))
        RelaysThroughPc = HasTransfers AndAlso Not sameResource AndAlso source IsNot Nothing AndAlso destination IsNot Nothing AndAlso
            source.CostHint <> ProviderCostHint.Local AndAlso destination.CostHint <> ProviderCostHint.Local
    End Sub

    Private Shared Function IsCostly(caps As ProviderCapabilities) As Boolean
        Return caps IsNot Nothing AndAlso (caps.IsMetered OrElse caps.CostHint = ProviderCostHint.Slow)
    End Function

    Friend Sub CheckScratch(scratch As String, Optional available As Func(Of String, Long) = Nothing)
        If Not HasTransfers Then Return
        Dim free As Long = If(available Is Nothing, New DriveInfo(Path.GetPathRoot(Path.GetFullPath(scratch))).AvailableFreeSpace, available(scratch))
        If free < ScratchBytes Then Throw ManagerCommandPlanner.Failure(scratch, ManagerFailure.ScratchSpace)
    End Sub

    Friend Function BytesText() As String
        Return If(HasUnknownBytes, Localization.T("Объём передачи неизвестен"), FileDetails.FormatSize(KnownBytes))
    End Function

    Friend Shared Function RelayText() As String
        Return Localization.T("Этот ПК передаёт данные между ресурсами: скачивание, загрузка и проверка.")
    End Function
End Class
#End If
