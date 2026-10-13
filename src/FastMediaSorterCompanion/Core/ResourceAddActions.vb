Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary
Imports System.Threading.Tasks
Imports System.Windows.Forms

Friend NotInheritable Class ResourceAddResult
    Public Added As Boolean
    Public Delivery As ResourceAddDelivery
    Public Publication As String = ""
End Class
Friend Interface IResourceAddActions
    ReadOnly Property CanPickDisks As Boolean
    Function Pick(owner As Form, disk As Boolean) As String
    Function Duplicate(path As String) As Boolean
    Sub WarnNetwork(owner As Form, path As String)
    Function Parameters(path As String) As ShareRootParams
    Function FolderName(path As String) As String
    Function CommitAsync(owner As Form, path As String, parameters As ShareRootParams, exchange As Boolean, publish As Boolean) As Task(Of ResourceAddResult)
    Sub Pair(owner As Form)
    Sub Settings(owner As Form)
End Interface
