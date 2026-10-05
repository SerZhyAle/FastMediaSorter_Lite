Option Strict On

Imports System.Drawing
Imports System.Media
Imports System.Windows.Forms

''' <summary>
''' What the viewer tells the user, and how loudly (ticket 124 section 3.3, UX-206/UX-207/UX-209).
'''
''' One entry point, <c>Notify</c>, with a <c>NotifyLevel</c> instead of the leading "!" that used to
''' be the only carrier of severity. The status line is still a label (a hidden carrier of the chrome's status
''' item), so every one of the legacy writes - <c>lbl_Status.Text = "! .."</c>, a few hundred of them - is caught
''' by its TextChanged and classified (<c>NotifyRules.Classify</c>: a leading "!" is a warning); a caller
''' that knows better says so with Notify. Either way ONE place decides what happens next:
'''
''' - the chrome's status item is written in the level's colour (SyncChromeItem reads <c>StatusMessage</c>),
''' - a warning or an error raises a toast over the media when the status line cannot be seen (full screen without
'''   panels, a slideshow that hides it) - and an error also plays the system sound there,
''' - a modal box is never the answer to news.
'''
''' The empty state also lives here: when the last file has gone the surface itself says so and names the key that
''' goes on, instead of a black rectangle and a status line that is not visible in full screen.
''' </summary>
Partial Public Class Main_Form

    ''' <summary>A status that says "working on it" - copying, moving, restoring. The tree wrote these with the same
    ''' leading "!" as a refusal, so "!Ждите.. файл копируется" and "! Нет файла" read as one severity; a progress
    ''' line is information. The legacy marker in the string is dropped on the mainline; net48 keeps the text as it
    ''' was (the maintenance policy of CLAUDE.md).</summary>
    Private Sub SayProgress(text As String)
#If Not NETFRAMEWORK Then
        Notify(NotifyRules.Classify(text).Text, NotifyLevel.Info)
#Else
        lbl_Status.Text = text
#End If
    End Sub

#If Not NETFRAMEWORK Then

    ''' <summary>The level a caller asked for, held for the one TextChanged that its own write raises.</summary>
    Private status_Pending_Level As NotifyLevel
    Private status_Has_Pending As Boolean

    ''' <summary>The words and the level the status line shows now, and the raw text they were derived from.</summary>
    Private status_Message As New NotifyMessage(String.Empty, NotifyLevel.Info)
    Private status_Message_Source As String = String.Empty

    Private notify_Toast As NotifyToast

    ''' <summary>The status line, as words (no "!") and a level - what the chrome paints and a screen reader reads.</summary>
    Friend ReadOnly Property StatusMessage As NotifyMessage
        Get
            If String.Equals(status_Message_Source, lbl_Status.Text, StringComparison.Ordinal) Then Return status_Message
            Return NotifyRules.Classify(lbl_Status.Text)
        End Get
    End Property

    ''' <summary>Says something to the user: the status line, and - when that cannot be seen - a toast.</summary>
    Friend Sub Notify(text As String, level As NotifyLevel)
        Dim words As String = If(text, String.Empty)
        status_Pending_Level = level
        status_Has_Pending = True
        Try
            If String.Equals(lbl_Status.Text, words, StringComparison.Ordinal) Then
                ' The same sentence said again (a second refusal): the label raises no change, so present it here.
                PresentStatus(New NotifyMessage(words, level))
            Else
                lbl_Status.Text = words
            End If
        Finally
            status_Has_Pending = False
        End Try
    End Sub

    ''' <summary>Wired to the status label's TextChanged in Form_Load: classifies every write, legacy or not.</summary>
    Private Sub StatusLabelTextChanged(sender As Object, e As EventArgs)
        Dim message As NotifyMessage = If(status_Has_Pending, New NotifyMessage(lbl_Status.Text, status_Pending_Level),
                                          NotifyRules.Classify(lbl_Status.Text))
        status_Has_Pending = False
        PresentStatus(message)
    End Sub

    Private Sub PresentStatus(message As NotifyMessage)
        status_Message = message
        status_Message_Source = lbl_Status.Text
        If message.Text.Length = 0 Then
            DismissNotifyToast()
            Return
        End If
        Dim toast As Boolean = NotifyRules.ShouldToast(message.Level, StatusLineReachable()) AndAlso CanShowToast()
        If toast Then ShowToast(message)
        If NotifyRules.ShouldPlaySound(message.Level, toast) Then
            Try
                SystemSounds.Hand.Play()
            Catch
            End Try
        End If
    End Sub

    ''' <summary>False in the modes where the status line is not on screen at all: full screen without panels, and
    ''' a slideshow whose setting hides it. (Elsewhere a changed status text reveals its panel - 125 UX-309.)</summary>
    Private Function StatusLineReachable() As Boolean
        If Not ChromeUsable() Then Return False
        If SlideshowHidesStatus() Then Return False
        Return True
    End Function

    ''' <summary>A toast needs a window to speak over: visible, not minimized, with a media area.</summary>
    Private Function CanShowToast() As Boolean
        If IsDisposed OrElse Not Visible OrElse WindowState = FormWindowState.Minimized Then Return False
        Return panel_Media IsNot Nothing AndAlso panel_Media.IsHandleCreated AndAlso panel_Media.ClientSize.Width > 0
    End Function

    Private Sub ShowToast(message As NotifyMessage)
        Try
            If notify_Toast Is Nothing OrElse notify_Toast.IsDisposed Then notify_Toast = New NotifyToast()
            Dim media As Rectangle = panel_Media.RectangleToScreen(panel_Media.ClientRectangle)
            notify_Toast.Present(Me, media, message, NotifyRules.ToastMilliseconds(message.Level))
        Catch ex As Exception
            ' A toast is a courtesy over the status line: failing to raise it must never fail the action.
            AppFileLogger.LogException("Notify toast", ex)
        End Try
    End Sub

    ''' <summary>Takes the toast down. Any key does it (ProcessCmdKey) and so does the next status text.</summary>
    Friend Sub DismissNotifyToast()
        If notify_Toast IsNot Nothing AndAlso Not notify_Toast.IsDisposed Then notify_Toast.DismissNow()
    End Sub

    ''' <summary>Paints the status panel NOW, for the few places that are about to block the UI thread (the container
    ''' password check): the chrome follows its carrier on a timer tick, and a tick cannot run during the block.</summary>
    Private Sub ChromeRepaintStatusNow()
        Try
            For Each panel As ChromePanel In chrome_Panels
                For Each pair In panel.Legacy
                    If pair.Value Is lbl_Status AndAlso SyncChromeItem(pair.Key, pair.Value) Then
                        panel.Host.Surface.LayoutItems()
                        panel.Host.Surface.Refresh()
                    End If
                Next
            Next
        Catch ex As Exception
            AppFileLogger.LogException("Notify: repainting the status panel", ex)
        End Try
    End Sub

    ' --- the empty state (UX-209) ---------------------------------------------------------------

    ''' <summary>The two lines drawn on the surface while no file is shown; empty when the surface is not empty.</summary>
    Private empty_State_Lines As String() = Array.Empty(Of String)()

    ''' <summary>Draws the empty state over the (cleared) media surface: what happened, and the key that goes on.</summary>
    Private Sub ShowEmptyState()
        empty_State_Lines = EmptyStateRules.Lines()
        If panel_Media IsNot Nothing Then panel_Media.Invalidate()
    End Sub

    Private Sub HideEmptyState()
        If empty_State_Lines.Length = 0 Then Return
        empty_State_Lines = Array.Empty(Of String)()
        If panel_Media IsNot Nothing Then panel_Media.Invalidate()
    End Sub

    ''' <summary>Paints the empty state on panel_Media - only while nothing is shown on it: a visible child (a
    ''' picture, the video) covers the panel anyway, and a stale flag must never write over a loading picture.</summary>
    Private Sub PaintEmptyState(sender As Object, e As PaintEventArgs)
        If empty_State_Lines.Length = 0 OrElse panel_Media Is Nothing Then Return
        For Each child As Control In panel_Media.Controls
            If child.Visible Then Return
        Next
        Dim area As Rectangle = Rectangle.Inflate(panel_Media.ClientRectangle, -panel_Media.LogicalToDeviceUnits(24), -panel_Media.LogicalToDeviceUnits(24))
        If area.Width <= 0 OrElse area.Height <= 0 Then Return
        Dim ink As Color = ChromeTheme.ContrastingText(panel_Media.BackColor)
        Dim family As String = If(Localization.FontFamily(), Font.FontFamily.Name)
        Dim flags As TextFormatFlags = TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter
        If Localization.IsRightToLeft() Then flags = flags Or TextFormatFlags.RightToLeft
        Using titleFont As New Font(family, 16.0F, FontStyle.Regular, GraphicsUnit.Point),
              hintFont As New Font(family, 11.0F, FontStyle.Regular, GraphicsUnit.Point)
            Dim titleSize As Size = TextRenderer.MeasureText(e.Graphics, empty_State_Lines(0), titleFont, New Size(area.Width, Integer.MaxValue), flags)
            Dim hint As String = If(empty_State_Lines.Length > 1, empty_State_Lines(1), String.Empty)
            Dim hintSize As Size = If(hint.Length = 0, Size.Empty, TextRenderer.MeasureText(e.Graphics, hint, hintFont, New Size(area.Width, Integer.MaxValue), flags))
            Dim gap As Integer = panel_Media.LogicalToDeviceUnits(10)
            Dim total As Integer = titleSize.Height + If(hint.Length = 0, 0, gap + hintSize.Height)
            Dim top As Integer = area.Top + Math.Max(0, (area.Height - total) \ 2)
            TextRenderer.DrawText(e.Graphics, empty_State_Lines(0), titleFont, New Rectangle(area.Left, top, area.Width, titleSize.Height), ink, flags)
            If hint.Length > 0 Then
                Dim dim_Ink As Color = ChromeTheme.Blend(panel_Media.BackColor, ink, 0.7F)
                TextRenderer.DrawText(e.Graphics, hint, hintFont, New Rectangle(area.Left, top + titleSize.Height + gap, area.Width, hintSize.Height), dim_Ink, flags)
            End If
        End Using
    End Sub

#End If

End Class
