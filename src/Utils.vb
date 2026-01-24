Imports System.Drawing
Imports System.Windows.Forms

Module Utils

    ''' <summary>
    ''' Adds an element to an array at the specified index.
    ''' </summary>
    Public Function AddAt(Of T)(ByVal arr As T(), ByVal newOne As T, ByVal index As Integer) As T()
        Dim uBound = arr.GetUpperBound(0)
        Dim lBound = arr.GetLowerBound(0)
        Dim outArr(uBound + 1) As T
        Array.Copy(arr, lBound, outArr, lBound, index)
        outArr(index) = newOne
        Array.Copy(arr, index, outArr, index + 1, uBound - index + 1)
        Return outArr
    End Function

    ''' <summary>
    ''' Removes an element from an array at the specified index.
    ''' </summary>
    Public Function RemoveAt(Of T)(ByVal arr As T(), ByVal index As Integer) As T()
        Dim uBound = arr.GetUpperBound(0)
        Dim lBound = arr.GetLowerBound(0)
        If uBound < lBound Then Return New T() {}
        Dim outArr(uBound - 1) As T
        Array.Copy(arr, lBound, outArr, lBound, index)
        Array.Copy(arr, index + 1, outArr, index, uBound - index)
        Return outArr
    End Function

    ''' <summary>
    ''' Returns a color (black or white) that contrasts with the given background color.
    ''' </summary>
    Public Function GetOppositeColor(backgroundColor As Color) As Color
        Dim sumColor = CInt(backgroundColor.R) + CInt(backgroundColor.G) + CInt(backgroundColor.B)
        If sumColor < 333 Then
            Return Color.White
        Else
            Return Color.Black
        End If
    End Function

    ''' <summary>
    ''' Copies the given text to the clipboard and optionally updates a status label.
    ''' </summary>
    Public Sub CopyTextToClipboard(text As String, Optional statusLabel As Label = Nothing, Optional statusMessage As String = "")
        If Not String.IsNullOrEmpty(text) Then
            Clipboard.SetText(text)
            If statusLabel IsNot Nothing AndAlso Not String.IsNullOrEmpty(statusMessage) Then
                statusLabel.Text = statusMessage
            End If
        End If
    End Sub

End Module