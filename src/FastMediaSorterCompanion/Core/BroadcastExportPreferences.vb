Option Explicit On
Option Strict On
Option Compare Binary
Option Infer On

''' <summary>Host-only export consent. The worker owns tokens; this stores only a record id and a choice.</summary>
Public Module BroadcastExportPreferences
    Public Function IncludeExternal(id As String) As Boolean
        If String.IsNullOrWhiteSpace(id) Then Return False
        Return GetSetting(App_name, Second_App_Name, "Broadcast_External_" & id, "0") = "1"
    End Function

    Public Sub Remember(id As String, includeExternal As Boolean)
        If String.IsNullOrWhiteSpace(id) Then Throw New ArgumentException("A broadcast id is required.", NameOf(id))
        SaveSetting(App_name, Second_App_Name, "Broadcast_External_" & id, If(includeExternal, "1", "0"))
    End Sub
End Module
