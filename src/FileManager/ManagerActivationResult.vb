#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

''' <summary>
''' Typed selection result from the File Manager to the Viewer (Specification 258).
''' Modern build only.
''' </summary>
Friend NotInheritable Class ManagerActivationResult

    Friend Sub New(location As ManagerLocation, entry As ManagerEntry)
        Me.Location = location
        Me.Entry = entry
    End Sub

    Friend ReadOnly Property Location As ManagerLocation
    Friend ReadOnly Property Entry As ManagerEntry

    Friend ReadOnly Property IsRemote As Boolean
        Get
            Return Location IsNot Nothing AndAlso Location.Place IsNot Nothing AndAlso Location.Place.NeedsConnection
        End Get
    End Property

    ''' <summary>The absolute path if local, else empty.</summary>
    Friend ReadOnly Property LocalPath As String
        Get
            If Location Is Nothing OrElse Location.Place Is Nothing OrElse Location.Place.NeedsConnection OrElse Entry Is Nothing Then Return ""
            Return ManagerPathRules.Combine(Location.Place.Kind, Location.DirectoryPath, Entry.Name)
        End Get
    End Property

    ''' <summary>The full path in the provider's spelling.</summary>
    Friend ReadOnly Property FullPath As String
        Get
            If Location Is Nothing OrElse Location.Place Is Nothing OrElse Entry Is Nothing Then Return ""
            Return ManagerPathRules.Combine(Location.Place.Kind, Location.DirectoryPath, Entry.Name)
        End Get
    End Property

End Class
#End If
