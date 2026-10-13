Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Friend Enum ResourceAddEntry
    AddButton
    EmptyList
    Welcome
    Exchange
    Forwarded
End Enum
Friend Enum ResourceAddStep
    Introduction
    Pick
    Questions
    Finish
End Enum
Friend Enum ResourceAddDelivery
    Disabled
    NotServing
    NoPhone
    Connected
End Enum
Friend Module ResourceAddModel
    Friend Function Steps(entry As ResourceAddEntry, gate As Boolean) As ResourceAddStep()
        If entry = ResourceAddEntry.Forwarded OrElse (entry = ResourceAddEntry.Exchange AndAlso Not gate) Then Return {}
        Return {ResourceAddStep.Introduction, ResourceAddStep.Pick, ResourceAddStep.Questions, ResourceAddStep.Finish}
    End Function
    Friend Function Start(entry As ResourceAddEntry) As ResourceAddStep
        Return If(entry = ResourceAddEntry.EmptyList OrElse entry = ResourceAddEntry.Welcome, ResourceAddStep.Pick, ResourceAddStep.Introduction)
    End Function
    Friend Function CanAdvance(stepKind As ResourceAddStep, picked As Boolean, busy As Boolean, committed As Boolean) As Boolean
        If busy OrElse committed Then Return False
        Return stepKind = ResourceAddStep.Introduction OrElse picked
    End Function
    Friend Function Delivery(enabled As Boolean, running As Boolean, activeConnections As Integer, hasAccess As Boolean) As ResourceAddDelivery
        If Not enabled Then Return ResourceAddDelivery.Disabled
        If Not running OrElse Not hasAccess Then Return ResourceAddDelivery.NotServing
        Return If(activeConnections > 0, ResourceAddDelivery.Connected, ResourceAddDelivery.NoPhone)
    End Function
    Friend Function EmptyHint(count As Integer) As Boolean
        Return count = 0
    End Function
End Module
