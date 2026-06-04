Option Strict On

Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Microsoft.Win32
Imports System.Diagnostics

Partial Public Class Main_Form

    Private Sub StartGifLoopPlayback(image As Image)
        StopGifLoopPlayback()

        If image Is Nothing Then Return

        Try
            If Not image.RawFormat.Equals(System.Drawing.Imaging.ImageFormat.Gif) Then Return

            Dim frameDimension As New System.Drawing.Imaging.FrameDimension(image.FrameDimensionsList(0))
            Dim frameCount As Integer = image.GetFrameCount(frameDimension)
            If frameCount <= 1 Then Return

            Dim durationMs As Integer = 0
            Try
                Dim item As System.Drawing.Imaging.PropertyItem = image.GetPropertyItem(&H5100)
                If item IsNot Nothing AndAlso item.Value IsNot Nothing AndAlso item.Len >= frameCount * 4 Then
                    For i As Integer = 0 To frameCount - 1
                        Dim delay As Integer = BitConverter.ToInt32(item.Value, i * 4)
                        If delay <= 0 Then delay = 10
                        durationMs += delay * 10
                    Next
                End If
            Catch
                durationMs = 0
            End Try

            If durationMs <= 0 Then durationMs = 1000

            gif_Restart_Image_Ref = image
            gif_Total_Duration_Ms = durationMs
            gif_Restart_Timer.Interval = Math.Max(100, gif_Total_Duration_Ms)
            gif_Restart_Timer.Start()
        Catch
            StopGifLoopPlayback()
        End Try
    End Sub

    Private Sub StopGifLoopPlayback()
        gif_Restart_Timer.Stop()
        gif_Total_Duration_Ms = 0
        gif_Restart_Image_Ref = Nothing
    End Sub

    Private Sub Gif_Restart_Timer_Tick(sender As Object, e As EventArgs) Handles gif_Restart_Timer.Tick
        If gif_Restart_Image_Ref Is Nothing Then
            StopGifLoopPlayback()
            Return
        End If

        Try
            Dim frameDimension As New System.Drawing.Imaging.FrameDimension(gif_Restart_Image_Ref.FrameDimensionsList(0))
            gif_Restart_Image_Ref.SelectActiveFrame(frameDimension, 0)

            If is_PictureBox1_Visible AndAlso Object.ReferenceEquals(Picture_Box_1.Image, gif_Restart_Image_Ref) Then
                Picture_Box_1.Invalidate()
            ElseIf is_PictureBox2_Visible AndAlso Object.ReferenceEquals(Picture_Box_2.Image, gif_Restart_Image_Ref) Then
                Picture_Box_2.Invalidate()
            Else
                StopGifLoopPlayback()
            End If
        Catch
            StopGifLoopPlayback()
        End Try
    End Sub

End Class
