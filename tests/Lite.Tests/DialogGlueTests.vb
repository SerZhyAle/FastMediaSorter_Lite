#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' Ticket 124, the parts that join the pure rules to the windows: the toast over the media (UX-207), where the status
''' line's severity is wired (UX-206), the system box in a right-to-left language (UX-205), and the text-style gates of
''' the sweep (UX-217/UX-218). Source reads where a window would be needed to see the wiring, a real window where the
''' behaviour is the window's.
''' </summary>
<Collection("AppFileLogger")>
Public Class DialogGlueTests

    <DllImport("user32.dll")>
    Private Shared Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    Private Const WM_NCHITTEST As Integer = &H84
    Private Const WS_EX_NOACTIVATE As Integer = &H8000000

    Private Shared Sub OnSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(30000), "the UI check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src/ not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function Source(fileName As String) As String
        Return File.ReadAllText(Path.Combine(SourceDir(), fileName))
    End Function

    ' --- the toast -------------------------------------------------------------------------------------

    <Fact>
    Public Sub A_toast_shows_over_the_media_without_taking_the_keyboard_and_lets_clicks_through()
        OnSta(Sub()
                  Using owner As New Form()
                      owner.StartPosition = FormStartPosition.Manual
                      owner.Bounds = New Rectangle(20, 20, 900, 600)
                      owner.Show()
                      Using toast As New NotifyToast()
                          Dim media As Rectangle = owner.RectangleToScreen(owner.ClientRectangle)
                          toast.Present(owner, media, New NotifyMessage("The slot is offline", NotifyLevel.Error), NotifyRules.ToastMilliseconds(NotifyLevel.Error))
                          Assert.True(toast.Visible)
                          Assert.True(media.Contains(toast.Bounds), "the toast stays inside the media area")
                          ' Centred horizontally, near the bottom.
                          Assert.InRange(Math.Abs((toast.Left + toast.Width \ 2) - (media.Left + media.Width \ 2)), 0, 2)
                          Assert.True(toast.Top > media.Top + media.Height \ 2, "the toast sits in the lower half")
                          Dim exStyle As Integer = GetWindowLong(toast.Handle, -20)
                          Assert.True((exStyle And WS_EX_NOACTIVATE) <> 0, "never activated")
                          Dim hit As IntPtr = SendMessage(toast.Handle, WM_NCHITTEST, IntPtr.Zero, IntPtr.Zero)
                          Assert.Equal(-1, hit.ToInt32())
                          toast.DismissNow()
                          Assert.False(toast.Visible)
                      End Using
                  End Using
              End Sub)
    End Sub

    <DllImport("user32.dll", EntryPoint:="GetWindowLongW")>
    Private Shared Function GetWindowLong(hWnd As IntPtr, index As Integer) As Integer
    End Function

    <Fact>
    Public Sub A_toast_is_not_raised_for_information_or_empty_text()
        OnSta(Sub()
                  Using owner As New Form()
                      owner.StartPosition = FormStartPosition.Manual
                      owner.Show()
                      Using toast As New NotifyToast()
                          Dim media As Rectangle = owner.RectangleToScreen(owner.ClientRectangle)
                          toast.Present(owner, media, New NotifyMessage("fine", NotifyLevel.Info), 0)
                          Assert.False(toast.Visible, "information has no toast lifetime")
                          toast.Present(owner, media, New NotifyMessage("", NotifyLevel.Error), 3000)
                          Assert.False(toast.Visible)
                      End Using
                  End Using
              End Sub)
    End Sub

    ''' <summary>A card of a fixed dark neutral: its text and its accent read on it whatever lies under the toast.</summary>
    <Theory>
    <InlineData(1)>
    <InlineData(2)>
    Public Sub The_toasts_text_and_accent_read_on_its_card(levelValue As Integer)
        Dim level As NotifyLevel = CType(levelValue, NotifyLevel)
        Assert.True(ChromeTheme.ContrastRatio(NotifyToast.CardInk, NotifyToast.CardFill) >= 7.0)
        Dim accent As Color = NotifyRules.InkOnSurface(level, NotifyToast.CardFill, NotifyToast.CardInk)
        Assert.True(ChromeTheme.ContrastRatio(accent, NotifyToast.CardFill) >= 4.5)
    End Sub

    ' --- where the severity is wired ----------------------------------------------------------------------

    ''' <summary>Every write to the status label - the few hundred legacy "!" ones included - is classified in one place.</summary>
    <Fact>
    Public Sub Every_status_write_is_classified_by_one_handler()
        Assert.Contains("AddHandler lbl_Status.TextChanged, AddressOf StatusLabelTextChanged", Source("Main_Form.Lifecycle.vb"))
        Dim notify As String = Source("Main_Form.Notify.vb")
        Assert.Contains("NotifyRules.Classify(lbl_Status.Text)", notify)
        Assert.Contains("NotifyRules.ShouldToast(message.Level, StatusLineReachable())", notify)
        Assert.Contains("NotifyRules.ShouldPlaySound(message.Level, toast)", notify)
    End Sub

    <Fact>
    Public Sub The_chrome_status_item_carries_the_level_and_the_painter_uses_it()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Assert.Contains("item.Tone = tone", chrome)
        Assert.Contains("tone = status.Level", chrome)
        Assert.Contains("NotifyRules.InkOnSurface(item.Tone", Source("Chrome\ChromeSurface.vb"))
    End Sub

    <Fact>
    Public Sub Any_key_takes_the_toast_down_but_an_auto_repeat_does_not()
        Dim keys As String = Source("Main_Form.KeyboardInput.vb")
        Dim at As Integer = keys.IndexOf("If Not last_KeyDown_Was_Repeat Then DismissNotifyToast()", StringComparison.Ordinal)
        Assert.True(at > 0, "ProcessCmdKey does not dismiss the toast")
    End Sub

    <Fact>
    Public Sub A_failed_operation_is_an_error_not_a_warning()
        Dim main As String = Source("Main_Form.vb")
        Assert.Contains("NotifyLevel.Error)", main)
        Dim at As Integer = main.IndexOf("Private Sub ReportOperationError", StringComparison.Ordinal)
        Dim body As String = main.Substring(at, main.IndexOf("End Sub", at, StringComparison.Ordinal) - at)
        Assert.Contains("Notify(", body)
        Assert.Contains("NotifyLevel.Error", body)
    End Sub

    ''' <summary>The thumbnail panel reads the level, not a character.</summary>
    Private Shared Function PanelReadsTheLevel() As Boolean
        Dim panel As String = Source("Image_Panel_Form.Modern.vb")
        Return panel.Contains("Main_Form.StatusMessageNow") AndAlso Not panel.Contains("TrimStart(""!""c")
    End Function

    <Fact>
    Public Sub The_thumbnail_panel_reads_the_status_level_instead_of_testing_a_bang()
        Assert.True(PanelReadsTheLevel())
    End Sub

    ''' <summary>"!Ждите.. файл копируется" is progress - information - not a refusal.</summary>
    <Fact>
    Public Sub Progress_statuses_are_information_not_warnings()
        Dim ops As String = Source("Main_Form.FileOperations.vb")
        Assert.DoesNotContain("lbl_Status.Text = Localization.TF(""!Ждите.. Файл копируется", ops)
        Assert.Contains("SayProgress(Localization.TF(""!Ждите.. Файл копируется", ops)
        Assert.Contains("SayProgress(Localization.TF(""!Ждите.. Файл переносится", ops)
    End Sub

    ' --- the system box ----------------------------------------------------------------------------------

    <Fact>
    Public Sub A_system_box_reads_right_to_left_only_in_a_right_to_left_language()
        Assert.Equal(CType(0, MessageBoxOptions), ViewerBox.OptionsFor(False))
        Dim rtl As MessageBoxOptions = ViewerBox.OptionsFor(True)
        Assert.True((rtl And MessageBoxOptions.RtlReading) <> 0)
        Assert.True((rtl And MessageBoxOptions.RightAlign) <> 0)
    End Sub

    ''' <summary>The stock call is not used any more: every box of the tree goes through <c>ViewerBox</c> (RTL, owner).</summary>
    <Fact>
    Public Sub No_source_file_calls_the_stock_message_box_directly()
        Dim direct As New List(Of String)()
        For Each path As String In Directory.GetFiles(SourceDir(), "*.vb", SearchOption.AllDirectories)
            If path.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) >= 0 OrElse path.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If path.IndexOf("\FastMediaSorterCompanion\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If System.IO.Path.GetFileName(path) = "ViewerBox.vb" Then Continue For
            Dim lines As String() = File.ReadAllLines(path)
            For i As Integer = 0 To lines.Length - 1
                If lines(i).TrimStart().StartsWith("'", StringComparison.Ordinal) Then Continue For
                If Regex.IsMatch(lines(i), "(?<![A-Za-z_])MessageBox\.Show\(") Then direct.Add(System.IO.Path.GetFileName(path) & ":" & (i + 1).ToString())
            Next
        Next
        Assert.True(direct.Count = 0, "MessageBox.Show called directly (no right-to-left reading, no owner) - use ViewerBox.Show: " & String.Join(", ", direct))
    End Sub

    ' --- the text-style gates --------------------------------------------------------------------------------

    ''' <summary>UX-218: the long dash is not in a UI string - the owner's house style is a plain hyphen. Comments are
    ''' code, not UI, and are left alone.</summary>
    <Fact>
    Public Sub No_ui_string_carries_a_long_dash()
        Dim found As New List(Of String)()
        For Each path As String In Directory.GetFiles(SourceDir(), "*.vb", SearchOption.AllDirectories)
            If path.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) >= 0 OrElse path.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If path.IndexOf("\FastMediaSorterCompanion\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            Dim lines As String() = File.ReadAllLines(path)
            For i As Integer = 0 To lines.Length - 1
                If lines(i).TrimStart().StartsWith("'", StringComparison.Ordinal) Then Continue For
                If lines(i).Contains(ChrW(&H2014)) Then found.Add(System.IO.Path.GetFileName(path) & ":" & (i + 1).ToString())
            Next
        Next
        Assert.True(found.Count = 0, "A long dash in a source string (use a plain hyphen): " & String.Join(", ", found))
    End Sub

    ''' <summary>The gate can only fail if it sees something: the character the scan looks for is the em dash.</summary>
    <Fact>
    Public Sub The_long_dash_the_gate_looks_for_is_the_em_dash()
        Assert.Equal(&H2014, AscW(ChrW(&H2014)))
        Assert.Equal(System.Globalization.UnicodeCategory.DashPunctuation, System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ChrW(&H2014)))
    End Sub

    ''' <summary>UX-217: the hot feedback paths name their value with a placeholder, not by concatenation.</summary>
    <Theory>
    <InlineData("Main_Form.LoadingIndicator.vb", "Localization.T(""Загрузка: "") &")>
    <InlineData("Main_Form.LoadingIndicator.vb", "Localization.T(""Загрузка.. "") &")>
    <InlineData("Main_Form.MediaLoading.vb", "Localization.T(""удалён: "") &")>
    <InlineData("Main_Form.MediaLoading.vb", "Localization.T(""Текущий: "") &")>
    <InlineData("Main_Form.vb", "Localization.T(""выбрана папка"") &")>
    <InlineData("Main_Form.VideoTracks.vb", "kind & "": "" & name")>
    Public Sub The_named_concatenations_are_gone(fileName As String, fragment As String)
        Assert.DoesNotContain(fragment, Source(fileName))
    End Sub

    ''' <summary>UX-220: the settings file filter is a translatable sentence, not an English literal.</summary>
    <Fact>
    Public Sub The_settings_file_filter_is_localized()
        Dim text As String = Source("Table_Form.ExpandedSettings.vb")
        Assert.DoesNotContain(" settings|*.fms-settings.json""", text)
        Assert.Contains("Localization.TF(""{0} - настройки|*.fms-settings.json"", ProductNames.Compact)", text)
    End Sub

    ''' <summary>UX-220/125 UX-335: the system "Animation effects" answer is not frozen for the process any more.</summary>
    <Fact>
    Public Sub The_system_animations_answer_is_invalidated_when_windows_changes_a_preference()
        Dim prefs As String = Source("Main_Form.ModernPreferences.vb")
        Assert.Contains("OnSystemPreferenceChanged", prefs)
        Assert.Contains("Threading.Volatile.Write(system_Animations_State, -1)", prefs)
        Assert.Contains("SystemEvents.UserPreferenceChanged, AddressOf OnSystemPreferenceChanged", Source("Main_Form.Lifecycle.vb"))
    End Sub

    ''' <summary>UX-219: the legacy language shim is read nowhere in the viewer's UI code.</summary>
    <Fact>
    Public Sub No_ui_code_reads_the_legacy_russian_language_flag()
        Dim readers As New List(Of String)()
        For Each path As String In Directory.GetFiles(SourceDir(), "*.vb", SearchOption.AllDirectories)
            If path.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) >= 0 OrElse path.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If path.IndexOf("\FastMediaSorterCompanion\", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            Dim name As String = System.IO.Path.GetFileName(path)
            If name = "Common_Module.vb" OrElse name.StartsWith("Localization", StringComparison.Ordinal) OrElse name = "LogPackage.vb" Then Continue For
            Dim lines As String() = File.ReadAllLines(path)
            For i As Integer = 0 To lines.Length - 1
                If lines(i).TrimStart().StartsWith("'", StringComparison.Ordinal) Then Continue For
                If lines(i).Contains("Is_Russian_Language") Then readers.Add(name & ":" & (i + 1).ToString())
            Next
        Next
        Assert.True(readers.Count = 0, "Is_Russian_Language is a derived shim: " & String.Join(", ", readers))
    End Sub

End Class
#End If
