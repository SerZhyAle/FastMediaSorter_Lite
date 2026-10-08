#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports Microsoft.VisualBasic

''' <summary>
''' Store abstraction over the viewer's registry section.
''' Enables unit tests for profile reset without touching the user's actual hive.
''' </summary>
Public Interface IProfileStore
    ''' <summary>Returns a snapshot of all key-value pairs currently stored in the section.</summary>
    Function Snapshot() As IReadOnlyDictionary(Of String, String)

    ''' <summary>Deletes the named value. A missing value is treated as success.</summary>
    Sub Delete(name As String)

    ''' <summary>Reads the transient reset marker, or empty string if absent.</summary>
    Function ReadMarker() As String

    ''' <summary>Writes the transient reset marker.</summary>
    Sub WriteMarker(value As String)

    ''' <summary>Clears the transient reset marker.</summary>
    Sub ClearMarker()
End Interface

''' <summary>
''' Production store backed by Microsoft.VisualBasic Interaction GetSetting / SaveSetting / DeleteSetting.
''' </summary>
Public NotInheritable Class VbSettingsProfileStore
    Implements IProfileStore

    Public Const MarkerKey As String = "ProfileResetRequested"

    Private ReadOnly _appName As String
    Private ReadOnly _section As String

    Public Sub New(Optional appName As String = Nothing, Optional section As String = Nothing)
        _appName = If(appName, Common_Module.App_name)
        _section = If(section, Common_Module.Second_App_Name)
    End Sub

    Public Function Snapshot() As IReadOnlyDictionary(Of String, String) Implements IProfileStore.Snapshot
        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Try
            Dim raw As String(,) = Interaction.GetAllSettings(_appName, _section)
            If raw IsNot Nothing Then
                Dim count As Integer = raw.GetLength(0)
                For i As Integer = 0 To count - 1
                    Dim key As String = raw(i, 0)
                    Dim value As String = raw(i, 1)
                    If Not String.IsNullOrEmpty(key) Then
                        result(key) = If(value, String.Empty)
                    End If
                Next
            End If
        Catch
        End Try
        Return result
    End Function

    Public Sub Delete(name As String) Implements IProfileStore.Delete
        If String.IsNullOrEmpty(name) Then Return
        Try
            Interaction.DeleteSetting(_appName, _section, name)
        Catch ex As ArgumentException
            ' DeleteSetting throws ArgumentException when the key does not exist.
            ' The adapter treats that as done.
        End Try
    End Sub

    Public Function ReadMarker() As String Implements IProfileStore.ReadMarker
        Return Interaction.GetSetting(_appName, _section, MarkerKey, String.Empty)
    End Function

    Public Sub WriteMarker(value As String) Implements IProfileStore.WriteMarker
        Interaction.SaveSetting(_appName, _section, MarkerKey, If(value, String.Empty))
    End Sub

    Public Sub ClearMarker() Implements IProfileStore.ClearMarker
        Delete(MarkerKey)
    End Sub

End Class

''' <summary>
''' In-memory store for unit testing.
''' </summary>
Public NotInheritable Class InMemoryProfileStore
    Implements IProfileStore

    Private ReadOnly _values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _throwOnDelete As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    Public Sub New()
    End Sub

    Public Sub New(initialValues As IDictionary(Of String, String))
        If initialValues IsNot Nothing Then
            For Each kvp In initialValues
                _values(kvp.Key) = kvp.Value
            Next
        End If
    End Sub

    ''' <summary>Configures the store to throw on Delete for specified key names.</summary>
    Public Sub SetThrowOnDelete(ParamArray names As String())
        If names IsNot Nothing Then
            For Each name In names
                _throwOnDelete.Add(name)
            Next
        End If
    End Sub

    Public Function Snapshot() As IReadOnlyDictionary(Of String, String) Implements IProfileStore.Snapshot
        Return New Dictionary(Of String, String)(_values, StringComparer.OrdinalIgnoreCase)
    End Function

    Public Sub Delete(name As String) Implements IProfileStore.Delete
        If String.IsNullOrEmpty(name) Then Return
        If _throwOnDelete.Contains(name) Then
            Throw New InvalidOperationException("Simulated registry delete failure for " & name)
        End If
        _values.Remove(name)
    End Sub

    Public Function ReadMarker() As String Implements IProfileStore.ReadMarker
        Dim val As String = Nothing
        If _values.TryGetValue(VbSettingsProfileStore.MarkerKey, val) Then
            Return val
        End If
        Return String.Empty
    End Function

    Public Sub WriteMarker(value As String) Implements IProfileStore.WriteMarker
        _values(VbSettingsProfileStore.MarkerKey) = If(value, String.Empty)
    End Sub

    Public Sub ClearMarker() Implements IProfileStore.ClearMarker
        _values.Remove(VbSettingsProfileStore.MarkerKey)
    End Sub

    Public Sub SetValue(key As String, value As String)
        _values(key) = If(value, String.Empty)
    End Sub

    Public Function TryGetValue(key As String, ByRef value As String) As Boolean
        Return _values.TryGetValue(key, value)
    End Function

    Public Function ContainsKey(key As String) As Boolean
        Return _values.ContainsKey(key)
    End Function

End Class
#End If
