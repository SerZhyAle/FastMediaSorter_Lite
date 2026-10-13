Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq

Friend Enum ManagerWelcomeStep
    Introduction
    Enable
    Folder
    Port
    Basics
    Finish
End Enum

''' <summary>First-folder onboarding rules. The pending marker distinguishes a resumed wizard from an upgrade.</summary>
Friend Module ManagerWelcomeModel
    Friend Const DoneValue As String = "Welcome_ManagerDone"
    Friend Const Pending As String = "pending"
    Friend Const Done As String = "1"

    Friend Function StepsFor() As IReadOnlyList(Of ManagerWelcomeStep)
        Return New List(Of ManagerWelcomeStep) From {ManagerWelcomeStep.Introduction, ManagerWelcomeStep.Enable,
            ManagerWelcomeStep.Folder, ManagerWelcomeStep.Port, ManagerWelcomeStep.Basics, ManagerWelcomeStep.Finish}
    End Function

    Friend Function HasManagerValues(names As IEnumerable(Of String)) As Boolean
        If names Is Nothing Then Return False
        Return names.Any(Function(name) name IsNot Nothing AndAlso
            ((name.StartsWith("Share_", StringComparison.Ordinal) AndAlso name <> ServerFeatures.EnabledRegValue) OrElse
             name = ServerWelcomeModel.DismissShareValue OrElse name = ServerWelcomeModel.DismissExchangeValue))
    End Function

    Friend Function ShouldOpen(state As String, fresh As Boolean, silent As Boolean, folderRequest As Boolean) As Boolean
        Return Not silent AndAlso Not folderRequest AndAlso state <> Done AndAlso (fresh OrElse state = Pending)
    End Function

    Friend Function IsGreen(running As Boolean, folderCount As Integer, rootsWithoutAccess As Integer,
                            Optional warning As String = "") As Boolean
        Return running AndAlso folderCount > 0 AndAlso rootsWithoutAccess = 0 AndAlso String.IsNullOrEmpty(warning)
    End Function

    Friend Function CanAdvance(stepKind As ManagerWelcomeStep, enabled As Boolean, folderCount As Integer) As Boolean
        Select Case stepKind
            Case ManagerWelcomeStep.Enable : Return enabled
            Case ManagerWelcomeStep.Folder : Return folderCount > 0
            Case Else : Return True
        End Select
    End Function

    Friend Function StartStep(enabled As Boolean, green As Boolean) As ManagerWelcomeStep
        If green Then Return ManagerWelcomeStep.Finish
        Return If(enabled, ManagerWelcomeStep.Folder, ManagerWelcomeStep.Introduction)
    End Function

    Friend Function RecordDone(stepKind As ManagerWelcomeStep, explicitDecline As Boolean) As Boolean
        Return stepKind = ManagerWelcomeStep.Finish OrElse (stepKind = ManagerWelcomeStep.Introduction AndAlso explicitDecline)
    End Function
End Module

''' <summary>Targeted marker writes do not seed a fresh profile with the rest of the settings defaults.</summary>
Friend Module ManagerWelcomeStore
    Private capturedFresh As Boolean?

    Friend Function CaptureFreshProfile() As Boolean
        If capturedFresh.HasValue Then Return capturedFresh.Value
        Dim values As String(,) = GetAllSettings(App_name, Second_App_Name)
        Dim names As New List(Of String)()
        If values IsNot Nothing Then
            For row As Integer = values.GetLowerBound(0) To values.GetUpperBound(0)
                names.Add(values(row, 0))
            Next
        End If
        Dim fresh As Boolean = Not ManagerWelcomeModel.HasManagerValues(names)
        If fresh AndAlso State() = "" Then SetState(ManagerWelcomeModel.Pending)
        capturedFresh = fresh
        Return fresh
    End Function

    Friend ReadOnly Property FreshAtLaunch As Boolean
        Get
            Return capturedFresh.GetValueOrDefault()
        End Get
    End Property

    Friend Sub WelcomeWasOpened()
        capturedFresh = False
    End Sub

    Friend Function State() As String
        Return GetSetting(App_name, Second_App_Name, ManagerWelcomeModel.DoneValue, "")
    End Function

    Friend Sub SetState(value As String)
        ShareSettings.WriteManagerWelcomeState(value)
    End Sub
End Module
