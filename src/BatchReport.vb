#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Text

' What a batch did, and the one sentence that says so
' (004_SPECIFICATION_BATCH_OPERATIONS_ENGINE_DOTNET10.md PB-4).
'
' The record is plain data and the formatter is a pure function of it: the panel's progress
' strip, the main window's status line and the details window all render THIS, so none of
' them can word the same outcome differently. Modern build only.

''' <summary>What happened to one file of the run.</summary>
Friend Enum BatchItemState
    Done
    Failed
    ''' <summary>Decided before the run (BatchPlan.Skipped).</summary>
    Skipped
    ''' <summary>The run was cancelled, or aborted because the destination stopped answering,
    ''' before this file's turn.</summary>
    NotStarted
End Enum

''' <summary>What kind of run this was - the verb of the summary line.</summary>
Friend Enum BatchRunKind
    Move
    Copy
    Delete
    Undo
End Enum

Friend NotInheritable Class BatchItemResult
    Public Property Path As String = ""
    Public Property State As BatchItemState
    ''' <summary>For Failed: the category sentence the user reads (never the exception text).</summary>
    Public Property Detail As String = ""
    ''' <summary>For Failed: what kind of failure, so the runner can recognise a dead destination.</summary>
    Public Property FailureKind As PathFailureKind
    ''' <summary>For Skipped: why.</summary>
    Public Property SkipReason As SkipReason
    ''' <summary>The operation that carried it out; Nothing for a skipped file.</summary>
    Public Property Op As BatchOp

    Public ReadOnly Property Name As String
        Get
            Try
                Return System.IO.Path.GetFileName(Path)
            Catch
                Return Path
            End Try
        End Get
    End Property
End Class

Friend NotInheritable Class BatchReport

    Public Property Kind As BatchRunKind
    ''' <summary>One entry per file the run was given, in selection order.</summary>
    Public ReadOnly Property Results As New List(Of BatchItemResult)()
    ''' <summary>The user pressed Cancel.</summary>
    Public Property Cancelled As Boolean
    ''' <summary>Three transport failures in a row: the destination is gone, and the rest
    ''' would have been that many timeouts (PB-3).</summary>
    Public Property AbortedForTransport As Boolean
    ''' <summary>Undo only: deleted for good, nothing to bring back.</summary>
    Public Property NotReturnableCount As Integer
    Public Property BytesDone As Long

    Public ReadOnly Property DoneCount As Integer
        Get
            Return CountOf(BatchItemState.Done)
        End Get
    End Property

    Public ReadOnly Property FailedCount As Integer
        Get
            Return CountOf(BatchItemState.Failed)
        End Get
    End Property

    Public ReadOnly Property SkippedCount As Integer
        Get
            Return CountOf(BatchItemState.Skipped)
        End Get
    End Property

    Public ReadOnly Property NotStartedCount As Integer
        Get
            Return CountOf(BatchItemState.NotStarted)
        End Get
    End Property

    Private Function CountOf(state As BatchItemState) As Integer
        Dim n As Integer = 0
        For Each r As BatchItemResult In Results
            If r.State = state Then n += 1
        Next
        Return n
    End Function

    ''' <summary>True when everything asked for happened.</summary>
    Public ReadOnly Property IsClean As Boolean
        Get
            Return FailedCount = 0 AndAlso SkippedCount = 0 AndAlso NotStartedCount = 0 AndAlso NotReturnableCount = 0
        End Get
    End Property

    ' ------------------------------------------------------------------ text ----

    ''' <summary>
    ''' "Перенесено файлов: 29; пропущено: 1; ошибок: 1". The lead is the verb; every tail
    ''' appears only when it is not zero, so a clean run is one short clause. Words, not
    ''' bare numbers - a status line that reads "29 1 1" is the thing this replaces.
    ''' </summary>
    Public Function SummaryLine() As String
        Dim lead As String
        Select Case Kind
            Case BatchRunKind.Copy : lead = Localization.TF("Скопировано файлов: {0}", DoneCount)
            Case BatchRunKind.Delete : lead = Localization.TF("Удалено файлов: {0}", DoneCount)
            Case BatchRunKind.Undo : lead = Localization.TF("Возвращено файлов: {0}", DoneCount)
            Case Else : lead = Localization.TF("Перенесено файлов: {0}", DoneCount)
        End Select

        Dim text As New StringBuilder(lead)
        If SkippedCount > 0 Then text.Append("; ").Append(Localization.TF("пропущено: {0}", SkippedCount))
        If FailedCount > 0 Then text.Append("; ").Append(Localization.TF("ошибок: {0}", FailedCount))
        If NotReturnableCount > 0 Then text.Append("; ").Append(Localization.TF("удалено безвозвратно, вернуть нельзя: {0}", NotReturnableCount))
        If NotStartedCount > 0 Then
            If AbortedForTransport Then
                text.Append("; ").Append(Localization.TF("прервано, нет связи с папкой - не начато: {0}", NotStartedCount))
            Else
                text.Append("; ").Append(Localization.TF("отменено - не начато: {0}", NotStartedCount))
            End If
        End If
        Return text.ToString()
    End Function

    ''' <summary>Why a file was left out, in a short clause that follows its name.</summary>
    Public Shared Function SkipText(reason As SkipReason) As String
        Select Case reason
            Case SkipReason.AlreadyInDestination : Return Localization.T("уже в этой папке")
            Case SkipReason.VanishedBeforeStart : Return Localization.T("файла уже нет на диске")
            Case SkipReason.InsideArchive : Return Localization.T("внутри архива файлы не изменяются")
            Case SkipReason.DuplicateName : Return Localization.T("то же имя уже есть среди выбранных файлов")
            Case SkipReason.NotWritableResource : Return Localization.T("ресурс доступен только для чтения")
            Case SkipReason.SameFile : Return Localization.T("это тот же самый файл")
            Case Else : Return Localization.T("имя уже занято в папке назначения")
        End Select
    End Function

    ''' <summary>The words for one row of the details window.</summary>
    Public Shared Function StateText(state As BatchItemState) As String
        Select Case state
            Case BatchItemState.Done : Return Localization.T("готово")
            Case BatchItemState.Failed : Return Localization.T("ошибка")
            Case BatchItemState.Skipped : Return Localization.T("пропущен")
            Case Else : Return Localization.T("не начат")
        End Select
    End Function

    ''' <summary>The detail column of one row: the failure category, the skip reason, or
    ''' nothing for a file that simply went through.</summary>
    Public Shared Function DetailText(result As BatchItemResult) As String
        If result Is Nothing Then Return ""
        Select Case result.State
            Case BatchItemState.Failed : Return result.Detail
            Case BatchItemState.Skipped : Return SkipText(result.SkipReason)
            Case BatchItemState.NotStarted : Return Localization.T("запуск не состоялся: операция прервана")
            Case Else
                ' StatusNote holds the new name when the old one was taken.
                If result.Op IsNot Nothing AndAlso Not String.IsNullOrEmpty(result.Op.StatusNote) Then
                    Return Localization.TF("сохранён как {0}", result.Op.StatusNote)
                End If
                Return ""
        End Select
    End Function

End Class
#End If
