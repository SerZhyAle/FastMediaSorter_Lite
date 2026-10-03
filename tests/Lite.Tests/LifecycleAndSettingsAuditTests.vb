Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Audit 053 (lifecycle, single instance, settings persistence and logging). One test
''' per finding that has a pure core; the rest are manual checks listed in the ticket's
''' implementation log.
''' </summary>
Public Class LifecycleAndSettingsAuditTests

    ' ---------------------------------------------------------------- F-01 ----
    ' A cancelled shutdown used to leave the slot array cleared in a running window, and
    ' the next flush wrote ten empty MoveOn values. PersistSettings no longer clears it,
    ' and a shutdown only persists in FormClosing.

    <Fact>
    Public Sub F01_saving_the_settings_never_clears_the_destination_slots()
        Dim lifecycle As String = ReadSource("Main_Form.Lifecycle.vb")
        Assert.DoesNotMatch(New Regex("Hardkeys_to_move_mediafile\(\w+\)\s*=\s*Nothing"), lifecycle)
        Assert.Contains("Private Function PersistSettings() As Boolean", lifecycle)
        Assert.DoesNotMatch(New Regex("PersistSettings\(\s*final_Save"), lifecycle)
    End Sub

    <Fact>
    Public Sub F01_a_shutdown_only_persists_in_FormClosing_and_tears_down_in_FormClosed()
        Dim lifecycle As String = ReadSource("Main_Form.Lifecycle.vb")
        Dim closing As String = MethodBody(lifecycle, "Form1_FormClosing")
        Assert.Matches(New Regex("If e\.CloseReason = CloseReason\.WindowsShutDown Then\s+(?:PersistSettings|SettingsFlush\.SaveNow)\(\)\s+Return"), closing)
        Assert.DoesNotContain("Dispose()", closing)

        Dim closed As String = MethodBody(lifecycle, "Form1_FormClosed")
        Assert.Contains("SlideShowTimer.Dispose()", closed)
        Assert.Contains("libVlc.Dispose()", closed)
    End Sub

    ' ---------------------------------------------------------------- F-06 ----

    <Fact>
    Public Sub F06_only_a_tagged_WM_COPYDATA_counts_as_ours()
        Assert.True(SingleInstanceProtocol.IsViewerCopyData(New IntPtr(SingleInstanceProtocol.Tag)))
        Assert.False(SingleInstanceProtocol.IsViewerCopyData(IntPtr.Zero))
        Assert.False(SingleInstanceProtocol.IsViewerCopyData(New IntPtr(1)))
    End Sub

    <Fact>
    Public Sub F06_a_helper_window_answering_zero_is_not_a_delivery()
        Assert.True(SingleInstanceProtocol.IsDelivered(True, New IntPtr(SingleInstanceProtocol.Handled_Result)))
        ' The GDI+ and broadcast windows answer WM_COPYDATA with 0 at once.
        Assert.False(SingleInstanceProtocol.IsDelivered(True, IntPtr.Zero))
        ' A timed-out or failed send is never a delivery, whatever the result holds.
        Assert.False(SingleInstanceProtocol.IsDelivered(False, New IntPtr(SingleInstanceProtocol.Handled_Result)))
    End Sub

    ' ---------------------------------------------------------------- F-07 ----

    <Fact>
    Public Sub F07_the_message_filter_is_relaxed_only_when_elevated()
        Assert.False(SingleInstanceProtocol.ShouldAdmitLowerIntegrityMessages(False))
        Assert.True(SingleInstanceProtocol.ShouldAdmitLowerIntegrityMessages(True))
    End Sub

    <Fact>
    Public Sub F07_no_process_wide_message_filter_is_left_in_the_viewer()
        Dim dragDrop As String = ReadSource("Main_Form.DragDrop.vb")
        Assert.DoesNotMatch(New Regex("\bChangeWindowMessageFilter\s*\("), dragDrop)
    End Sub

    ' ---------------------------------------------------------------- F-04 ----

    <Fact>
    Public Sub F04_history_shows_both_lists_newest_first()
        ' Files are stored newest LAST, folders newest FIRST.
        Dim files As New List(Of String) From {"old.jpg", "mid.jpg", "new.jpg"}
        Dim folders As New List(Of String) From {"C:\new", "C:\mid", "C:\old"}
        Assert.Equal(New String() {"new.jpg", "mid.jpg", "old.jpg"}, Utils.RecentNewestFirst(files, storedNewestFirst:=False).ToArray())
        Assert.Equal(New String() {"C:\new", "C:\mid", "C:\old"}, Utils.RecentNewestFirst(folders, storedNewestFirst:=True).ToArray())
        ' A copy, never the stored list itself.
        Assert.Equal("old.jpg", files(0))
    End Sub

    ' ---------------------------------------------------------------- F-12 ----

    <Fact>
    Public Sub F12_a_limit_of_zero_keeps_nothing()
        Dim folders As New List(Of String) From {"C:\a", "C:\b"}
        Utils.ApplyRecentLimit(folders, 0, newestFirst:=True)
        Assert.Empty(folders)
    End Sub

    <Fact>
    Public Sub F12_a_lowered_limit_trims_the_oldest_at_once()
        Dim folders As New List(Of String) From {"C:\1", "C:\2", "C:\3", "C:\4", "C:\5"}
        Utils.ApplyRecentLimit(folders, 2, newestFirst:=True)
        Assert.Equal(New String() {"C:\1", "C:\2"}, folders.ToArray())

        Dim files As New List(Of String) From {"1", "2", "3", "4", "5"}
        Utils.ApplyRecentLimit(files, 2, newestFirst:=False)
        Assert.Equal(New String() {"4", "5"}, files.ToArray())
    End Sub

    <Fact>
    Public Sub F12_a_list_within_the_limit_is_left_alone()
        Dim folders As New List(Of String) From {"C:\1", "C:\2"}
        Utils.ApplyRecentLimit(folders, 100, newestFirst:=True)
        Assert.Equal(2, folders.Count)
    End Sub

    ' ---------------------------------------------------------------- F-13 ----

    <DllImport("shell32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function CommandLineToArgvW(commandLine As String, ByRef argumentCount As Integer) As IntPtr
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function LocalFree(memory As IntPtr) As IntPtr
    End Function

    ''' <summary>What a receiving process would read back, by the rules Windows applies.</summary>
    Private Shared Function ParseBack(quoted As String) As String()
        Dim count As Integer
        Dim argv As IntPtr = CommandLineToArgvW("app.exe " & quoted, count)
        Assert.NotEqual(IntPtr.Zero, argv)
        Try
            Dim result As New List(Of String)()
            For i = 1 To count - 1
                result.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)))
            Next
            Return result.ToArray()
        Finally
            LocalFree(argv)
        End Try
    End Function

    <Theory>
    <InlineData("E:\", """E:\\""")>
    <InlineData("\\srv\share\", """\\srv\share\\""")>
    <InlineData("C:\a b\", """C:\a b\\""")>
    <InlineData("C:\a b", """C:\a b""")>
    Public Sub F13_a_path_is_quoted_so_the_receiver_reads_it_back_exactly(path As String, expected As String)
        Dim quoted As String = Utils.QuoteArgument(path)
        Assert.Equal(expected, quoted)
        Assert.Equal(New String() {path}, ParseBack(quoted))
    End Sub

    <Fact>
    Public Sub F13_embedded_quotes_and_backslashes_survive()
        For Each value As String In {"a""b", "a\""b", "a\\""b\", "", "plain"}
            Assert.Equal(New String() {value}, ParseBack(Utils.QuoteArgument(value)))
        Next
    End Sub

    ' ---------------------------------------------------------------- F-14 ----

    <Fact>
    Public Sub F14_the_W_MAPI_structures_marshal_their_strings_as_UTF16()
        Const hindiPath As String = "C:\Users\अनुज\AppData\Local\Temp\fms-logs.zip"
        Dim desc As New MailSender.MapiFileDescW With {.position = -1, .path = hindiPath, .fileName = "fms-logs.zip"}
        ' Same shape as MapiFileDescA: three ULONGs, two string pointers, one pointer.
        Assert.Equal(3 * 4 + (If(IntPtr.Size = 8, 4, 0)) + 3 * IntPtr.Size, Marshal.SizeOf(GetType(MailSender.MapiFileDescW)))

        Dim memory As IntPtr = Marshal.AllocHGlobal(Marshal.SizeOf(GetType(MailSender.MapiFileDescW)))
        Try
            Marshal.StructureToPtr(desc, memory, False)
            Dim pathOffset As Integer = Marshal.OffsetOf(GetType(MailSender.MapiFileDescW), "path").ToInt32()
            Dim pathPointer As IntPtr = Marshal.ReadIntPtr(memory, pathOffset)
            Assert.Equal(hindiPath, Marshal.PtrToStringUni(pathPointer))
            Marshal.DestroyStructure(memory, GetType(MailSender.MapiFileDescW))
        Finally
            Marshal.FreeHGlobal(memory)
        End Try

        Dim message As New MailSender.MapiMessageW With {.subject = "লগ", .noteText = "कोई पाठ"}
        Dim messageMemory As IntPtr = Marshal.AllocHGlobal(Marshal.SizeOf(GetType(MailSender.MapiMessageW)))
        Try
            Marshal.StructureToPtr(message, messageMemory, False)
            Dim subjectOffset As Integer = Marshal.OffsetOf(GetType(MailSender.MapiMessageW), "subject").ToInt32()
            Assert.Equal("লগ", Marshal.PtrToStringUni(Marshal.ReadIntPtr(messageMemory, subjectOffset)))
            Dim bodyOffset As Integer = Marshal.OffsetOf(GetType(MailSender.MapiMessageW), "noteText").ToInt32()
            Assert.Equal("कोई पाठ", Marshal.PtrToStringUni(Marshal.ReadIntPtr(messageMemory, bodyOffset)))
            Marshal.DestroyStructure(messageMemory, GetType(MailSender.MapiMessageW))
        Finally
            Marshal.FreeHGlobal(messageMemory)
        End Try
    End Sub

    <Fact>
    Public Sub F14_a_user_abort_is_a_cancel_not_a_send()
        Assert.Equal(MailSender.MailSendOutcome.Sent, MailSender.OutcomeOf(0))
        Assert.Equal(MailSender.MailSendOutcome.Cancelled, MailSender.OutcomeOf(1))
        Assert.Equal(MailSender.MailSendOutcome.Failed, MailSender.OutcomeOf(2))
    End Sub

    ' ---------------------------------------------------------------- F-15 ----
    ' Release strips Debug.WriteLine, so a catch block that reports only through it
    ' leaves nothing in the log the user sends.

    <Theory>
    <InlineData("Main_Form.Lifecycle.vb")>
    <InlineData("Application_Events.vb")>
    Public Sub F15_catch_blocks_do_not_report_only_through_Debug(fileName As String)
        Dim lines As String() = ReadSource(fileName).Split({vbLf}, StringSplitOptions.None)
        Dim offenders As New List(Of String)()
        For i = 0 To lines.Length - 1
            If Not Regex.IsMatch(lines(i), "^\s*Catch\b") Then Continue For
            Dim body As New System.Text.StringBuilder()
            Dim j As Integer = i + 1
            While j < lines.Length AndAlso Not Regex.IsMatch(lines(j), "^\s*(End Try|Catch\b|Finally\b)")
                body.AppendLine(lines(j))
                j += 1
            End While
            Dim text As String = body.ToString()
            If text.Contains("Debug.WriteLine") AndAlso Not text.Contains("AppFileLogger") Then
                offenders.Add(fileName & ":" & (i + 1).ToString())
            End If
        Next
        Assert.True(offenders.Count = 0, "Catch blocks logging only through Debug.WriteLine: " & String.Join(", ", offenders))
    End Sub

    ' ------------------------------------------------------------ F-16/F-17 ----

    <Theory>
    <InlineData("0", 0)>
    <InlineData("5", 5)>
    <InlineData("6", 1)>
    <InlineData("-1", 1)>
    <InlineData("abc", 1)>
    <InlineData("", 1)>
    Public Sub F16_an_out_of_range_colour_scheme_falls_back_to_the_default(stored As String, expected As Integer)
        Assert.Equal(expected, Utils.ColorSchemeFromSetting(stored))
    End Sub

    <Fact>
    Public Sub F17_a_corrupt_number_is_the_default_not_zero()
        Assert.Equal(10, Utils.ParseIntSetting("abc", 10, 1, 120))
        Assert.Equal(15, Utils.ParseIntSetting("", 15, 0, 480))
        Assert.Equal(80, Utils.ParseIntSetting("8o", 80))
        ' A real number still wins, clamped.
        Assert.Equal(1, Utils.ParseIntSetting("0", 10, 1, 120))
        Assert.Equal(120, Utils.ParseIntSetting("500", 10, 1, 120))
        Assert.Equal(42, Utils.ParseIntSetting("42", 10, 1, 120))
    End Sub

    ' ---------------------------------------------------------------- F-19 ----

    <Fact>
    Public Sub F19_no_localization_key_is_registered_twice()
        ' Touching the class runs RegisterStrings.
        Localization.T("Настройки")
        Assert.True(Localization.DuplicateKeys.Count = 0,
                    "Registered more than once: " & String.Join(", ", Localization.DuplicateKeys))
    End Sub

    ' ---------------------------------------------------------------- F-11 ----

    <Fact>
    Public Sub F11_a_locked_log_is_not_rotated_and_previous_log_survives()
        Using dir As New TempDir()
            Dim logPath As String = Path.Combine(dir.Directory_Path, "current.log")
            Dim rolled As String = Path.Combine(dir.Directory_Path, "previous.log")
            File.WriteAllText(rolled, "the generation before")
            Using fs As New FileStream(logPath, FileMode.Create, FileAccess.Write)
                fs.SetLength(AppFileLogger.Max_Log_Bytes + 1)
            End Using

            ' Another instance holding current.log, as AppFileLogger does (no FileShare.Delete).
            Using holder As New FileStream(logPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)
                Assert.False(AppFileLogger.RotateIfOversized(logPath))
            End Using

            Assert.True(File.Exists(logPath))
            Assert.Equal("the generation before", File.ReadAllText(rolled))
        End Using
    End Sub

    <Fact>
    Public Sub F11_an_unlocked_oversized_log_still_rotates_over_previous_log()
        Using dir As New TempDir()
            Dim logPath As String = Path.Combine(dir.Directory_Path, "current.log")
            Dim rolled As String = Path.Combine(dir.Directory_Path, "previous.log")
            File.WriteAllText(rolled, "old")
            Using fs As New FileStream(logPath, FileMode.Create, FileAccess.Write)
                fs.SetLength(AppFileLogger.Max_Log_Bytes + 1)
            End Using

            Assert.True(AppFileLogger.RotateIfOversized(logPath))
            Assert.False(File.Exists(logPath))
            Assert.True(New FileInfo(rolled).Length > AppFileLogger.Max_Log_Bytes)
            Assert.False(File.Exists(Path.Combine(dir.Directory_Path, "previous.log.rotating")))
        End Using
    End Sub

    <Fact>
    Public Sub F11_two_appenders_on_one_log_keep_every_line()
        Using dir As New TempDir()
            Dim logPath As String = Path.Combine(dir.Directory_Path, "current.log")
            Using first As New StreamWriter(AppFileLogger.OpenAppendOnly(logPath)) With {.AutoFlush = True}
                Using second As New StreamWriter(AppFileLogger.OpenAppendOnly(logPath)) With {.AutoFlush = True}
                    first.WriteLine("first process, banner")
                    second.WriteLine("second process, banner")
                    first.WriteLine("first process, next line")
                    second.WriteLine("second process, next line")
                End Using
            End Using

            Dim lines As String() = File.ReadAllLines(logPath)
            Assert.Equal(New String() {"first process, banner", "second process, banner",
                                       "first process, next line", "second process, next line"}, lines)
        End Using
    End Sub

#If Not NETFRAMEWORK Then
    ' ---------------------------------------------------------------- F-02 ----

    <Fact>
    Public Sub F02_CopyFrom_keeps_the_instance_and_takes_every_stored_value()
        Dim live As New ModernViewerPreferences()
        Dim imported As New ModernViewerPreferences() With {
            .ReduceMotion = Not live.ReduceMotion,
            .RecentFoldersLimit = 7,
            .SlideshowRandomOrder = "random",
            .AllowNewWindows = Not live.AllowNewWindows
        }
        Dim sameObject As ModernViewerPreferences = live

        live.CopyFrom(imported)

        Assert.Same(sameObject, live)
        For Each prop As Reflection.PropertyInfo In ModernViewerPreferences.WritableProperties()
            Assert.Equal(prop.GetValue(imported), prop.GetValue(live))
        Next
    End Sub

    <Fact>
    Public Sub F02_an_import_never_swaps_the_preferences_object()
        Dim source As String = ReadSource("Main_Form.ModernPreferences.vb")
        Dim replace As String = MethodBody(source, "ReplaceModernPreferences")
        Assert.DoesNotMatch(New Regex("modern_Preferences\s*=\s*imported"), replace)
        Assert.Contains("CopyFrom(imported)", replace)
        ' F-18: the one key Save() re-reads is written explicitly.
        Assert.Contains("PersistAllowNewWindows()", replace)
    End Sub
#End If

    ' ---------------------------------------------------------------- helpers ----

    Private Shared Function ReadSource(fileName As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", fileName)
            If File.Exists(candidate) Then Return File.ReadAllText(candidate).Replace(vbCr, "")
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src/" & fileName & " not found above " & AppContext.BaseDirectory)
    End Function

    ''' <summary>The text from a Sub/Function declaration to its End.</summary>
    Private Shared Function MethodBody(source As String, name As String) As String
        Dim m As Match = Regex.Match(source, "(Sub|Function)\s+" & Regex.Escape(name) & "\b[\s\S]*?\n\s*End (Sub|Function)")
        Assert.True(m.Success, name & " not found")
        Return m.Value
    End Function

    Private NotInheritable Class TempDir
        Implements IDisposable

        Public ReadOnly Property Directory_Path As String

        Public Sub New()
            _Directory_Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fms-053-" & Guid.NewGuid().ToString("N"))
            System.IO.Directory.CreateDirectory(_Directory_Path)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                System.IO.Directory.Delete(_Directory_Path, True)
            Catch
            End Try
        End Sub
    End Class

End Class
