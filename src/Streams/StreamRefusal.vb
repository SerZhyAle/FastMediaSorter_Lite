#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System

Namespace Streams

    ''' <summary>
    ''' Represents an entry that was refused from inclusion in the stream catalog,
    ''' or a duplicate entry folded into a canonical row.
    ''' </summary>
    Friend Class StreamRefusal

        Public Property SourceFile As String = String.Empty
        Public Property SourceLine As Integer = 0
        Public Property RawName As String = String.Empty
        Public Property RawUrl As String = String.Empty
        Public Property Reason As String = String.Empty
        Public Property IsRefusal As Boolean = True
        Public Property CanonicalUrl As String = String.Empty

        Public Sub New()
        End Sub

        Public Sub New(sourceFile As String, sourceLine As Integer, rawName As String, rawUrl As String, reason As String, Optional isRefusal As Boolean = True, Optional canonicalUrl As String = "")
            Me.SourceFile = sourceFile
            Me.SourceLine = sourceLine
            Me.RawName = rawName
            Me.RawUrl = rawUrl
            Me.Reason = reason
            Me.IsRefusal = isRefusal
            Me.CanonicalUrl = canonicalUrl
        End Sub

    End Class

End Namespace
#End If
