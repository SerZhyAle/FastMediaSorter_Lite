#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Runtime.InteropServices

''' <summary>
''' Probe for camera presence (spec 227 rule 2, section 5).
''' In the viewer, the "Scan with the camera.." item is absent when no camera is present.
''' </summary>
Friend NotInheritable Class CameraPresence

    Private Sub New()
    End Sub

    ''' <summary>Test override hook for deterministic testing of camera presence.</summary>
    Friend Shared Property OverrideForTesting As Boolean? = Nothing

    Private Shared ReadOnly ClsidSystemDeviceEnum As New Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86")
    Private Shared ReadOnly ClsidVideoInputDeviceCategory As New Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86")

    <ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface ICreateDevEnum
        <PreserveSig>
        Function CreateClassEnumerator(<[In], MarshalAs(UnmanagedType.LPStruct)> ByRef pType As Guid,
                                      <Out> ByRef ppEnumMoniker As Object,
                                      <[In]> dwFlags As Integer) As Integer
    End Interface

    ''' <summary>
    ''' Returns True if at least one video capture camera is connected to this PC.
    ''' </summary>
    Friend Shared Function IsCameraPresent() As Boolean
        If OverrideForTesting.HasValue Then Return OverrideForTesting.Value

        Try
            Dim devEnumType As Type = Type.GetTypeFromCLSID(ClsidSystemDeviceEnum)
            If devEnumType Is Nothing Then Return False
            Dim devEnum As Object = Activator.CreateInstance(devEnumType)
            Dim creator As ICreateDevEnum = TryCast(devEnum, ICreateDevEnum)
            If creator Is Nothing Then Return False

            Dim enumMoniker As Object = Nothing
            Dim hr As Integer = creator.CreateClassEnumerator(ClsidVideoInputDeviceCategory, enumMoniker, 0)
            ' S_OK (0) with a non-null enumerator means at least one device was found in the category.
            ' S_FALSE (1) means category is empty.
            Return hr = 0 AndAlso enumMoniker IsNot Nothing
        Catch
            Return False
        End Try
    End Function

End Class
#End If
