'sza130806lite
'sza240823 eto pizdec
'sza250411 random, filters, etc
'sza250502 refactor
'sza250506 grok
'FastMediaSorter
'sza250520 

'W275

Option Strict On

Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Media
Imports Microsoft.VisualBasic.FileIO
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.WinForms
Imports Microsoft.Win32

<ComVisible(True)>
Public Class the_Main_Form
    Private Shared mutex As Mutex
    Private Const AppMutexName As String = "FastMediaSorterSingleInstanceMutex"
    Private Const theJPGFileExtension As String = ".jpg"
    Private Const theJPGFileTypeDescription As String = "JPEG Image"
    Private applicationRunsCount As Integer
    Private isComboSetAuto As Boolean = False

    Private isFileReseivedFromOutside As Boolean = False
    Private firstScrollEvent As Boolean = False
    Private currentFolderPath As String
    Private isSecondaryPictureBoxActive As Boolean = False
    Private thisIsFirstPictureFileWeShow As Boolean = True
    Private isFirstPictureBoxNeedToBeCached As Boolean = False
    Private targetImagePath As String
    Private fileList As ReadOnlyCollection(Of String)
    Private currentFileIndex As Integer
    Private currentSecondLoadedFileName As String
    Private totalFilesCount As Integer
    Private currentFileName As String
    Private nextAfterCurrentFileName As String
    Private currentLoadedFileName As String
    Private imageFileExtensions As String() = {".jpg", ".gif", ".jpeg", ".png", ".bmp", ".tiff", ".ico", ".wmf", ".emf", ".exif"}
    Private videoFileExtensions As New HashSet(Of String) From {".webm", ".ogg", ".3g2", ".mkv", ".3gp", ".mp4", ".m4v", ".m4a", ".mov", ".mp3", ".avi", ".wmv", ".asf", ".mpg", ".mpeg", ".flv", ".wav", ".wma"}
    Private webImageFileExtensions As New HashSet(Of String) From {".webp", ".heic", ".avif", ".svg"}
    Private historyFileName As String
    Private loadedImageScale As String = ""
    Private lastBackColor As System.Drawing.Color

    Private historyOperatedTargetPath As String
    Private isImageMode As Boolean = True

    Private filesList As List(Of String) = Nothing
    Private filesArray As String() = Nothing
    Private useArray As Boolean = False
    Private Const MaxFiles As Integer = 22222

    Private isTableFormOpen As Boolean
    Private lastActionTime As DateTime
    Private isFullScreen As Boolean
    Private isExternalInputReceived As Boolean = False
    Private wasExternalInputLast As Boolean
    Private WithEvents SlideShowTimer As New System.Windows.Forms.Timer()
    Private isSlideShowRandom As Boolean
    Private isWebBrowser1Visible As Boolean
    Private isPictureBox1Visible As Boolean
    Private isPictureBox2Visible As Boolean
    Private isWebView2Loaded As Boolean = False
    '    Private isWebBrowserLoaded As Boolean = False
    Private lastLoadedUri As String = ""
    Private isFolderReadRequired As Boolean = False

    Private videoVolume As Double = 1
    Private isTextBoxEdition As Boolean = False

    Dim historySourceFileName As String = ""
    Dim historyDestinationFileName As String = ""
    Private WithEvents BgWorker As New BackgroundWorker()
    Private bgWorkerOnline As Boolean
    Private bgWorkerResult As String = "EMPTY"

    Private Const WmCopyData As Integer = &H4A

    Private allSupportedExtensions As New HashSet(Of String)()
    Private recentFolders As New List(Of String)
    Private Const MaxRecentFolders As Integer = 100

    Private isWebView2Available As Boolean = False
    Private WithEvents Web_View2 As WebView2 = Nothing
    Private isWebView2Visible As Boolean = False

    Private WithEvents FileOperationWorker As New BackgroundWorker
    Private currentFileOperation As String
    Private currentFileOperationArgs As Object

    Private WithEvents ResizeDebounceTimer As New System.Windows.Forms.Timer()
    Private lastFullScreenState As Boolean = False

    Private Sub InitializeFileOperationWorker()
        FileOperationWorker.WorkerSupportsCancellation = True
    End Sub

    Private Sub InitializeExtensionLists()
        allSupportedExtensions.UnionWith(imageFileExtensions)
        allSupportedExtensions.UnionWith(videoFileExtensions)
        allSupportedExtensions.UnionWith(webImageFileExtensions)
    End Sub

    Private Const WM_COPYDATA As Integer = &H4A

    <StructLayout(LayoutKind.Sequential)>
    Public Structure COPYDATASTRUCT
        Public dwData As IntPtr
        Public cbData As Integer
        Public lpData As IntPtr
    End Structure

    ' Declare the necessary Windows API functions
    <DllImport("user32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, ByRef lParam As COPYDATASTRUCT) As Integer
    End Function

    Protected Overrides Sub WndProc(ByRef m As Message)
        If m.Msg = WM_COPYDATA Then
            Try
                Dim cds As COPYDATASTRUCT = CType(Marshal.PtrToStructure(m.LParam, GetType(COPYDATASTRUCT)), COPYDATASTRUCT)
                Dim receivedData As String = Marshal.PtrToStringAnsi(cds.lpData, cds.cbData)
                If String.IsNullOrEmpty(receivedData) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0000: Error processing WM_COPYDATA - received data is null or empty")
                    Return
                End If

                Dim argument As String = receivedData.TrimEnd(Chr(0)).Trim()
                argument = Regex.Replace(argument, "(?<!^)(\\\\)+", "\")

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0010: received from new instance: " & argument)

                isExternalInputReceived = True
                isFileReseivedFromOutside = True
                ProcessArgument(argument)
            Catch ex As Exception
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0020: Error processing WM_COPYDATA - " & ex.Message)
            End Try
        End If

        MyBase.WndProc(m)
    End Sub

    Private Sub SetWebBrowserCompatibilityMode()
        Try
            Using key = Registry.CurrentUser.OpenSubKey("Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION", True)
                If key Is Nothing Then
                    Using newKey = Registry.CurrentUser.CreateSubKey("Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION")
                        newKey.SetValue(Process.GetCurrentProcess().ProcessName & ".exe", 11001, RegistryValueKind.DWord)
                    End Using
                Else
                    key.SetValue(Process.GetCurrentProcess().ProcessName & ".exe", 11001, RegistryValueKind.DWord)
                End If
            End Using
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0030: Error to set WebBrowser mode")
        End Try
    End Sub

    Public Sub InitNew()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0001: init new")
        ResizeDebounceTimer.Interval = 100
        ResizeDebounceTimer.Enabled = False

        Dim createdNew As Boolean
        mutex = New Mutex(True, AppMutexName, createdNew)

        isTextBoxEdition = True
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0002: Initialize")
        cmbox_Sort.Items.Clear()
        cmbox_Sort.Items.AddRange(New String() {"abc", "xyz", "rnd", ">size", "<size", ">time", "<time"})
        cmbox_Sort.SelectedIndex = 0
        isTextBoxEdition = False

        BgWorker.WorkerReportsProgress = True
        BgWorker.WorkerSupportsCancellation = True
        Web_Browser.ObjectForScripting = Me

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0003: InitializeExtensionLists")
        InitializeExtensionLists()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0004: SetWebBrowserCompatibilityMode")
        SetWebBrowserCompatibilityMode()
        '        AddHandler Web_Browser.DocumentCompleted, AddressOf WebBrowser1_DocumentCompleted
        '        AddHandler Picture_Box_1.LoadCompleted, AddressOf PictureBox1_LoadCompleted
        '        AddHandler Picture_Box_2.LoadCompleted, AddressOf PictureBox2_LoadCompleted

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0005: CheckWebView2Availability")
        CheckWebView2Availability()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0007: InitializeWebView2")
        InitializeWebView2()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0009: InitializeFileOperationWorker")
        InitializeFileOperationWorker()
    End Sub

    Private Function LoadImage(filePath As String) As Image
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0035: LoadImage begin " & filePath)
        Try
            Dim nextImage As Image
            Using stream As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)
                nextImage = Image.FromStream(stream)
            End Using

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0040: LoadImage end ")
            Return nextImage
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0041: ERR loading image " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub BgWorker_DoWork(sender As Object, e As DoWorkEventArgs) Handles BgWorker.DoWork
        Dim worker As BackgroundWorker = DirectCast(sender, BackgroundWorker)

        If worker.CancellationPending Then
            e.Cancel = True
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0050: BgWorker got cancellation")
        End If

        If currentFileName = "" OrElse Not My.Computer.FileSystem.FileExists(currentFileName) Then
            lbl_Current_File.Text = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0060: File is lost for BgWorker size calculation")
        Else
            Dim fileInfo = My.Computer.FileSystem.GetFileInfo(currentFileName)
            Dim fileSize = fileInfo.Length
            Dim fileSizeText As String

            If fileSize = 0 Then
                fileSizeText = "0 bytes!"
            ElseIf fileSize / 1000 > 1000 Then
                fileSizeText = (fileSize / 1000000).ToString("F1") + "MB"
            Else
                fileSizeText = (fileSize / 1000).ToString("F1") + "KB"
            End If

            Dim userState As New Dictionary(Of String, String)
            userState("fileSizeText") = fileSizeText
            DirectCast(sender, BackgroundWorker).ReportProgress(0, userState)

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0070: BgWorker reported size calculation")
        End If

        If wasExternalInputLast Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0080: folder files going be counted on background..")
            totalFilesCount = FileSystem.GetDirectoryInfo(currentFolderPath).EnumerateFiles.Count

            Dim userState As New Dictionary(Of String, String)
            userState("totalFilesCountText") = totalFilesCount.ToString
            DirectCast(sender, BackgroundWorker).ReportProgress(0, userState)

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0090: folder files: " & totalFilesCount)
        End If

        If Not isSlideShowRandom AndAlso Not nextAfterCurrentFileName = "" AndAlso Not nextAfterCurrentFileName = currentFileName Then
            Dim SecondFileExtension = Path.GetExtension(nextAfterCurrentFileName).ToLower

            If imageFileExtensions.Contains(SecondFileExtension) Then

                '                Dim nextImage As Image = LoadScaledImage(nextAfterCurrentFileName, PictureBox1.Width, PictureBox1.Height)
                Dim nextImage As Image = LoadImage(nextAfterCurrentFileName)
                currentSecondLoadedFileName = nextAfterCurrentFileName

                e.Result = New Tuple(Of Image, Boolean)(nextImage, isFirstPictureBoxNeedToBeCached)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0100: BgWorker loaded image into memory: " & nextAfterCurrentFileName.ToString)

            Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0110: Next file is not image, backload is cancelled")
                e.Cancel = True
            End If
        Else

            currentSecondLoadedFileName = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0120: No needs for the Next file, backload is cancelled")
            e.Cancel = True
        End If
    End Sub

    Private Sub BgWorker_ProgressChanged(sender As Object, e As ProgressChangedEventArgs) Handles BgWorker.ProgressChanged
        Dim userState As Dictionary(Of String, String) = DirectCast(e.UserState, Dictionary(Of String, String))

        If userState.ContainsKey("fileSizeText") Then
            Dim fileSizeText As String = userState("fileSizeText")

            If Not fileSizeText = Nothing Then
                lbl_Current_File.Text = If(lngRus, "Текущий: ", "Current: ") & currentFileName & " " & fileSizeText
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0170: BgWorker size calculated: " & fileSizeText)
            End If

        ElseIf userState.ContainsKey("totalFilesCountText") Then
            Dim totalFilesCountText As String = userState("totalFilesCountText")

            If Not totalFilesCountText = Nothing Then
                lbl_File_Number.Text = If(lngRus, "Файл: 1 из " & totalFilesCountText, "File: 1 from " & totalFilesCountText)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0175: BgWorker files count calculated: " & totalFilesCountText)
            Else
                lbl_File_Number.Text = If(lngRus, "Файл: 0 ", "File: 0 ")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0180: BgWorker files count calculated: " & totalFilesCountText)
            End If
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0190: BgWorker reported wrong progress!")
        End If

    End Sub

    Private Sub BgWorker_RunWorkerCompleted(sender As Object, e As RunWorkerCompletedEventArgs) Handles BgWorker.RunWorkerCompleted
        bgWorkerOnline = False

        If e.Cancelled Then
            bgWorkerResult = "CANCELLED"
        ElseIf e.Error IsNot Nothing Then
            bgWorkerResult = "ERR: " & e.Error.Message
        ElseIf currentSecondLoadedFileName = "" Then
            bgWorkerResult = "SKIPED"
        Else
            Dim result As Tuple(Of Image, Boolean) = DirectCast(e.Result, Tuple(Of Image, Boolean))
            Dim nextImage As Image = result.Item1
            Dim isFirstPictureBox As Boolean = result.Item2

            If isFirstPictureBox Then
                If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image?.Dispose()
                Picture_Box_1.Image = nextImage

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0210: bgWorker: P1 is loaded")
            Else
                If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image?.Dispose()
                Picture_Box_2.Image = nextImage

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0220: bgWorker: P2 is loaded")
            End If

            bgWorkerResult = "LOADED"
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0230: bgWorkerResult: " & bgWorkerResult)
    End Sub

    Public Sub ProcessArgument(argument As String)
        argument = argument.Trim()

        Try
            If String.IsNullOrEmpty(argument) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0240: arg is EMPTY")
                Return
            End If

            Dim isDirectory As Boolean = Directory.Exists(argument)
            If isDirectory Then
                currentFolderPath = argument
                isTextBoxEdition = True
                cmbox_Media_Folder.Text = currentFolderPath
                isTextBoxEdition = False

                Dim folderPathSaved = GetSetting(appName, secName, "ImageFolder", "")
                If folderPathSaved = currentFolderPath Then
                    Integer.TryParse(GetSetting(appName, secName, "LastCounter"), currentFileIndex)
                    If currentFileIndex > 0 Then
                        totalFilesCount = FileSystem.GetDirectoryInfo(currentFolderPath).EnumerateFiles.Count
                        If currentFileIndex < totalFilesCount Then
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0250: folder is set from arg, file found in savings")
                            ReadShowMediaFile("ReadFiles")
                        Else
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0260: folder is set from arg, but file is not found in savings")
                            currentFileIndex = 0
                            ReadShowMediaFile("ReadFolderAndFile")
                        End If
                    Else
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0265: folder is set from arg, but file wasn't in savings")
                        currentFileIndex = 0
                        ReadShowMediaFile("ReadFolderAndFile")
                    End If
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0270: folder is set from arg")
                    currentFileIndex = 0
                    ReadShowMediaFile("ReadFolderAndFile")
                End If
            Else
                If Not File.Exists(argument) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0280: File from arg does not exist: " & argument)
                    Return
                End If

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0290: File is set from arg: " & argument)
                currentFolderPath = Path.GetDirectoryName(argument)
                targetImagePath = argument
                currentFileName = argument
                isTextBoxEdition = True
                cmbox_Media_Folder.Text = currentFolderPath
                isTextBoxEdition = False
                isExternalInputReceived = True
                wasExternalInputLast = True
                currentFileIndex = 0
                totalFilesCount = 1

                ReadShowMediaFile("ReadFolderAndKnownFile")
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0300: Error processing argument: " & ex.Message)
            currentFolderPath = ""
            isTextBoxEdition = True
            cmbox_Media_Folder.Text = ""
            isTextBoxEdition = False
        End Try
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles btn_Select_Folder.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0305: btn_Select_Folder")
        Dim folderBrowse As New FolderBrowserDialog()
        folderBrowse.SelectedPath = currentFolderPath

        folderBrowse.Description = If(lngRus, "Выберите папку с медиафайлами..", "Set folder of media files..")

        If folderBrowse.ShowDialog() = Windows.Forms.DialogResult.OK Then
            currentFolderPath = folderBrowse.SelectedPath
            ReadShowMediaFile("ReadFolderAndFile")
            lbl_Status.Text = If(lngRus, "выбрана папка", "folder selected")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0310: Folder read")
        End If
    End Sub

    Private Function AddAt(Of T)(ByVal arr As T(), ByVal newOne As T, ByVal index As Integer) As T()
        Dim uBound = arr.GetUpperBound(0)
        Dim lBound = arr.GetLowerBound(0)
        Dim outArr(uBound + 1) As T
        Array.Copy(arr, lBound, outArr, lBound, index)
        outArr(index) = newOne
        Array.Copy(arr, index, outArr, index + 1, uBound - index + 1)
        Return outArr
    End Function

    Private Function RemoveAt(Of T)(ByVal arr As T(), ByVal index As Integer) As T()
        Dim uBound = arr.GetUpperBound(0)
        Dim lBound = arr.GetLowerBound(0)
        If uBound < lBound Then Return New T() {}
        Dim outArr(uBound - 1) As T
        Array.Copy(arr, lBound, outArr, lBound, index)
        Array.Copy(arr, index + 1, outArr, index, uBound - index)
        Return outArr
    End Function

    Private Sub ReadShowMediaFile(ByVal readMode As String)

        If Not isFolderReadRequired Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0050: ReadShowMediaFile = " & readMode.ToString)

            Dim currentTime As DateTime = DateTime.Now
            If lastActionTime.AddSeconds(0.1) > currentTime Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0330: Try to read the new file less than 0.1s - cancelled")
                Exit Sub
            End If
            lastActionTime = currentTime

            If FileOperationWorker.IsBusy Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0340: Read file skiped while FileOperationWorker")
                Exit Sub
            End If

            lbl_Slideshow_Time.Text = If(SlideShowTimer.Enabled, (SlideShowTimer.Interval / 1000).ToString() & "s", "")

            Dim isAfterUndo As Boolean = (readMode = "ReadAfterUndo")
            Dim isFileFound As Boolean = True
            If Not UpdateFileIndexAndList(readMode, isFileFound) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0350: Mastering the file is failed")
                Return
            End If

            If String.IsNullOrEmpty(currentFolderPath) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0360: currentFolderPath is lost")
                Return
            End If

            isTextBoxEdition = True
            Dim needsUpdate As Boolean = True
            needsUpdate = Not cmbox_Media_Folder.Text = currentFolderPath

            If needsUpdate Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0370: folder combo list is updated")
                If recentFolders.LastOrDefault() <> currentFolderPath Then
                    recentFolders.Remove(currentFolderPath)
                    recentFolders.Add(currentFolderPath)
                    If recentFolders.Count > MaxRecentFolders Then
                        recentFolders.RemoveAt(0)
                    End If
                End If

                If cmbox_Media_Folder.InvokeRequired Then
                    cmbox_Media_Folder.Invoke(Sub()
                                                  cmbox_Media_Folder.Items.Clear()
                                                  For Each folder In recentFolders
                                                      cmbox_Media_Folder.Items.Add(folder)
                                                  Next
                                                  cmbox_Media_Folder.SelectedIndex = recentFolders.Count - 1
                                              End Sub)
                Else
                    cmbox_Media_Folder.Items.Clear()
                    For Each folder In recentFolders
                        cmbox_Media_Folder.Items.Add(folder)
                    Next
                    cmbox_Media_Folder.SelectedIndex = recentFolders.Count - 1
                End If
            End If
            isTextBoxEdition = False

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0380: UpdateCurrentFileAndDisplay")
            UpdateCurrentFileAndDisplay(isFileFound, isAfterUndo)
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0390: folder read is skiped")
        End If
    End Sub

    Private Function UpdateFileIndexAndList(readMode As String, ByRef isFileFound As Boolean) As Boolean
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0400: UpdateFileIndexAndList = " & readMode.ToString)

        Select Case readMode
            Case "ReadNextFile" ' 1
                If wasExternalInputLast Then
                    If Not LoadFilesForExternalInput(isFileFound) Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0410: case ReadNextFile is failed")
                        Return False
                    End If
                End If
                currentFileIndex += 1
                If currentFileIndex > totalFilesCount - 1 Then currentFileIndex = 0

                lbl_Status.Text = ""

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0420: case ReadNextFile")

            Case "ReadFiles" '80
                If Not LoadFiles() Then Return False
                If currentFileIndex < 0 Then currentFileIndex = 0
                If currentFileIndex > totalFilesCount - 1 Then currentFileIndex = totalFilesCount - 1
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0430: case ReadFiles")

            Case "SetFile" '99
                If currentFileIndex < 0 Then currentFileIndex = 0
                If currentFileIndex > totalFilesCount - 1 Then currentFileIndex = totalFilesCount - 1
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0440: case SetFile")

            Case "ReadFolderAndFile" '0
                lbl_Status.Text = If(lngRus, "чтение каталога.. ждите!", "reading files.. wait!")

                If Not LoadFiles() Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0450: case ReadFolderAndFile is failed")
                    Return False
                End If
                lbl_Status.Text = ""
                currentFileIndex = 0

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0460: case ReadFolderAndFile")

            Case "ReadFolderAndKnownFile" '91
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0470: isExternalInputReceived = " & isExternalInputReceived)
                isFileFound = False

                If isExternalInputReceived Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0480: GetDirectoryInfo = " & currentFolderPath)

                    currentFileIndex = 0
                    isExternalInputReceived = False
                    wasExternalInputLast = True
                Else
                    wasExternalInputLast = False
                    If Not LoadFilesForExternalInput(isFileFound) Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0490: case ReadFolderAndKnownFile is failed")
                        Return False
                    End If
                    If currentFileIndex < 0 OrElse Not isFileFound Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0500: targetImagePath not found in file list")
                        currentFileIndex = 0
                        isFileFound = True
                    End If
                End If
                lbl_Status.Text = ""

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0510: case ReadFolderAndKnownFile")

            Case "ReadPrevFile" '2
                If wasExternalInputLast Then
                    If Not LoadFilesForExternalInput(isFileFound) Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0520: case ReadPrevFile is failed")
                        Return False
                    End If
                End If
                currentFileIndex -= 1
                If currentFileIndex < 0 Then currentFileIndex = totalFilesCount - 1
                lbl_Status.Text = ""

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0530: case ReadPrevFile")

            Case "DeleteFile" '3
                If String.IsNullOrEmpty(currentFileName) Then
                    lbl_Status.Text = If(lngRus, "! Нет файла для удаления", "! No file for deleting")
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0540: case DeleteFile failed")
                    Return False
                End If

                Try
                    If isWebBrowser1Visible Then
                        Web_Browser.DocumentText = ""
                    Else
                        If isPictureBox1Visible Then
                            If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image?.Dispose()
                        Else
                            If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image?.Dispose()
                        End If
                    End If

                    currentLoadedFileName = ""

                    If My.Computer.FileSystem.FileExists(currentFileName) Then
                        If the_Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked Then
                            currentFileOperation = "Delete"
                            currentFileOperationArgs = currentFileName
                            FileOperationWorker.RunWorkerAsync()
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0550: file in task to be deleted: " & currentFileName)
                            If useArray Then
                                filesArray = RemoveAt(filesArray, currentFileIndex)
                            Else
                                filesList.RemoveAt(currentFileIndex)
                            End If
                            totalFilesCount -= 1
                            If currentFileIndex > totalFilesCount - 1 Then currentFileIndex = totalFilesCount - 1
                            lbl_Status.Text = If(lngRus, "удален: ", "file deleted: ") & currentFileName
                        Else
                            My.Computer.FileSystem.DeleteFile(currentFileName)
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0560: file deleted: " & currentFileName)
                            If useArray Then
                                filesArray = RemoveAt(filesArray, currentFileIndex)
                            Else
                                filesList.RemoveAt(currentFileIndex)
                            End If
                            totalFilesCount -= 1
                            If currentFileIndex > totalFilesCount - 1 Then currentFileIndex = totalFilesCount - 1
                            lbl_Status.Text = If(lngRus, "удален: ", "file deleted: ") & currentFileName
                        End If
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0570: case DeleteFile")
                    Else
                        lbl_Status.Text = If(lngRus, "! Файл не найден", "! File not found")
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0580: case DeleteFile failed: not found")
                    End If
                Catch ex As Exception
                    MsgBox("E001 " & ex.Message)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0590: ERR: " & ex.Message)
                End Try

            Case "ReadForRandom" '4
                If Not LoadFilesForRandomOrSlideshow(isFileFound, True) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0600: case ReadForRandomOrSlideshow failed")
                    Return False
                End If
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0610: case ReadForRandomOrSlideshow")

            Case "ReadForSlideShow" '5
                If Not LoadFilesForRandomOrSlideshow(isFileFound, isSlideShowRandom) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0620: case ReadForSlideShow failed")
                    Return False
                End If
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0630: case ReadForSlideShow")

            Case "AfterUndo" '98
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0640: case AfterUndo")
        End Select

        Return True
    End Function

    Private Function LoadFilesForRandomOrSlideshow(ByRef isFileFound As Boolean, isRandom As Boolean) As Boolean
        Try
            If currentFileIndex = 0 Then
                wasExternalInputLast = False
                lbl_Status.Text = If(lngRus, "чтение каталога.. ждите!", "reading files.. wait!")
                '         Refresh()
                Dim files As Object = GetFiles()
                If files Is Nothing Then
                    lbl_Status.Text = If(lngRus, "! Ошибка чтения файлов", "! Error reading files")
                    currentFolderPath = ""
                    cmbox_Media_Folder.Text = ""
                    totalFilesCount = 0
                    currentFileIndex = 0
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0650: Error loading slideshow")
                    Return False
                End If

                If useArray Then
                    filesArray = DirectCast(files, String())
                Else
                    filesList = DirectCast(files, List(Of String))
                End If

                lbl_Status.Text = ""
                totalFilesCount = If(useArray, filesArray.Length, filesList.Count)
                currentFileIndex = 0
                If totalFilesCount <> 0 Then
                    If isRandom Then
                        Dim random As New Random
                        currentFileIndex = random.Next(0, totalFilesCount)
                        isFileFound = True
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0660: New random file set")
                    Else
                        currentFileIndex = If(useArray, Array.IndexOf(filesArray, targetImagePath), filesList.IndexOf(targetImagePath))
                        isFileFound = currentFileIndex >= 0
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0670: Next slideshow file set")
                    End If
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0680: No files for slides")
                End If
            Else
                lbl_Status.Text = ""
                If isRandom Then
                    Dim random As New Random
                    currentFileIndex = random.Next(0, totalFilesCount)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0690: random file set")
                Else
                    currentFileIndex += 1
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0700: slide file set")
                End If
            End If
            Return True
        Catch ex As Exception
            MsgBox("E002 " & ex.Message)
            currentFolderPath = ""
            cmbox_Media_Folder.Text = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0710: E002 " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LoadFilesForExternalInput(ByRef isFileFound As Boolean) As Boolean
        Try
            If wasExternalInputLast Then
                wasExternalInputLast = False
                lbl_Status.Text = If(lngRus, "чтение каталога.. ждите!", "reading files.. wait!")

                Dim files As Object = GetFiles()
                If files Is Nothing Then
                    lbl_Status.Text = If(lngRus, "! Ошибка чтения файлов", "! Error reading files")
                    currentFolderPath = ""
                    cmbox_Media_Folder.Text = ""
                    totalFilesCount = 0
                    currentFileIndex = 0
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0720: files aren't set")
                    Return False
                End If

                If useArray Then
                    filesArray = DirectCast(files, String())
                Else
                    filesList = DirectCast(files, List(Of String))
                End If

                lbl_Status.Text = ""
                totalFilesCount = If(useArray, filesArray.Length, filesList.Count)
                currentFileIndex = If(useArray, Array.IndexOf(filesArray, targetImagePath), filesList.IndexOf(targetImagePath))
                isFileFound = currentFileIndex >= 0

                If Not isFileFound Then
                    If useArray Then
                        filesArray = AddAt(filesArray, targetImagePath, 0)
                    Else
                        filesList.Insert(0, targetImagePath)
                    End If
                    totalFilesCount += 1
                    currentFileIndex = 0
                    isFileFound = True
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0745: targetImagePath added to file list")
                End If

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0740: new folder is read")
                Return True
            Else
                currentFileIndex += 1
                isFileFound = True
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0750: next one is chosen")
                Return True
            End If
        Catch ex As Exception
            MsgBox("E003 " & ex.Message)
            currentFolderPath = ""
            cmbox_Media_Folder.Text = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0760: E003 " & ex.Message)
            Return False
        End Try
    End Function


    Private Function LoadFiles() As Boolean
        Try
            Dim files As Object = GetFiles()
            If files Is Nothing Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0770: files arnt set")
                lbl_Status.Text = If(lngRus, "! Ошибка чтения файлов", "! Error reading files")
                currentFolderPath = ""
                cmbox_Media_Folder.Text = ""
                totalFilesCount = 0
                currentFileIndex = 0

                Return False
            End If

            If useArray Then
                filesArray = DirectCast(files, String())
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0780: folder files ARRAY is counted: " & filesArray.Length.ToString)
            Else
                filesList = DirectCast(files, List(Of String))
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0790: folder files LIST is counted: " & filesList.Count.ToString)
            End If
            totalFilesCount = If(useArray, filesArray.Length, filesList.Count)

            Return True
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0800: E004 " & ex.Message)
            lbl_Status.Text = If(lngRus, "! Ошибка чтения файлов", "! Error reading files")
            MsgBox("E004 " & ex.Message)
            currentFolderPath = ""
            cmbox_Media_Folder.Text = ""
            totalFilesCount = 0
            currentFileIndex = 0

            Return False
        End Try
    End Function

    Public Sub SetVolume(volume As Double)
        videoVolume = Math.Max(0.0, Math.Min(1.0, volume))
    End Sub

    Private Sub LoadWebImageInWebView2(fileUri As String)
        If Not isWebView2Available OrElse Web_View2 Is Nothing Then
            lbl_Status.Text = If(lngRus, "Формат .webp не поддерживается: WebView2 недоступен или не инициализирован", "Format .webp not supported: WebView2 is unavailable or not initialized")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0810: WebView2 is missing or not initialized")
            Return
        End If

        Try
            isWebView2Loaded = False
            isWebView2Visible = False
            isWebBrowser1Visible = False
            isPictureBox1Visible = False
            isPictureBox2Visible = False

            UpdateControlVisibility()

            Web_View2.Source = New Uri(fileUri)
            currentLoadedFileName = currentFileName
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0820: WebView2 is loaded")
        Catch ex As Exception
            lbl_Status.Text = If(lngRus, "Ошибка загрузки в WebView2: " & ex.Message, "Error loading in WebView2: " & ex.Message)
            If Web_View2 IsNot Nothing Then
                Web_View2.Source = New Uri("about:blank")
                Web_View2.Visible = False
            End If
            isWebView2Visible = False
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0830: Error loading in WebView2: " & ex.Message)

            UpdateControlVisibility()
        End Try

        If Not Web_Browser.DocumentText = "" Then
            Web_Browser.DocumentText = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0840: WebBrowser is vanished")
        End If
    End Sub


    Private Sub LoadVideoInWebBrowser(fileUri As String)
        Try
            isWebBrowser1Visible = False
            isWebView2Visible = False
            isPictureBox1Visible = False
            isPictureBox2Visible = False

            Dim escapedUri As String = Uri.EscapeUriString(fileUri)
            Dim contentHtml As String = "<video id='videoPlayer' controls autoplay style='width:100%;calc(100% - 35px);object-fit:fill;'>" &
                            "<source src='" & escapedUri & "'>" &
                            If(lngRus, "Ваш браузер не поддерживает видео.", "Your browser does not support video.") &
                            "</video>"
            Dim html As String = "<html><head><meta http-equiv='X-UA-Compatible' content='IE=edge'>" &
                         "<style>" &
                         "body { margin: 0; overflow: hidden; background: black; }" &
                         "video { width: 100%; height: calc(100% - 35px); object-fit: fill; position: absolute; top: 0; left: 0; }" &
                         "</style></head>" &
                         "<body oncontextmenu='return false;'>" & contentHtml &
                         "<script>" &
                         "var player = document.getElementById('videoPlayer');" &
                         "player.volume = " & videoVolume.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) & ";" &
                         "player.oncontextmenu = function(e) { e.preventDefault(); if (this.paused) this.play(); else this.pause(); return false; };" &
                         "player.onvolumechange = function() { try { window.external.SetVolume(this.volume); } catch(e) { } };" &
                         "</script></body></html>"

            lastLoadedUri = ""
            Web_Browser.DocumentText = html
            currentLoadedFileName = currentFileName

            isWebBrowser1Visible = True
            isPictureBox1Visible = False
            isPictureBox2Visible = False

            UpdateControlVisibility()

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0850: WebBrowser is loaded")
        Catch ex As Exception
            Web_Browser.DocumentText = ""
            isWebBrowser1Visible = False

            UpdateControlVisibility()
            lbl_Status.Text = If(lngRus, "Ошибка загрузки видео: " & ex.Message, "Error loading video: " & ex.Message)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0860: Error loading video: " & ex.Message)
        End Try
    End Sub

    Private Sub LoadStandardImageInPictureBox()
        isPictureBox1Visible = False
        isPictureBox2Visible = False
        isWebBrowser1Visible = False
        isWebView2Visible = False

        If currentLoadedFileName <> currentFileName Then

            If bgWorkerResult = "LOADED" AndAlso currentSecondLoadedFileName = currentFileName Then
                If Not isSecondaryPictureBoxActive Then
                    isPictureBox2Visible = True
                    isPictureBox1Visible = False
                    '                    Picture_Box_2.Refresh()

                    bgWorkerResult = "USED P2"
                    isSecondaryPictureBoxActive = True
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0870: P2 is found already loaded isSecondaryPictureBoxActive=true")
                Else
                    isPictureBox2Visible = False
                    isPictureBox1Visible = True
                    '                    Picture_Box_1.Refresh()

                    bgWorkerResult = "USED P1"
                    isSecondaryPictureBoxActive = False
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0880: P1 is found already loaded isSecondaryPictureBoxActive =false")
                End If
            Else
                Dim newImage As Image
                Try
                    Using stream As New FileStream(currentFileName, FileMode.Open, FileAccess.Read, FileShare.Read)
                        newImage = Image.FromStream(stream)
                    End Using

                    If Not thisIsFirstPictureFileWeShow AndAlso isSecondaryPictureBoxActive Then
                        If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image?.Dispose()
                        Picture_Box_2.Image = newImage
                        '  Picture_Box_2.Refresh()

                        isPictureBox2Visible = True
                        isPictureBox1Visible = False
                        isSecondaryPictureBoxActive = True

                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0890: P2 set (not found loaded) isSecondaryPictureBoxActive=true")
                    Else
                        If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image?.Dispose()
                        Picture_Box_1.Image = newImage
                        ' Picture_Box_1.Refresh()

                        isPictureBox1Visible = True
                        isPictureBox2Visible = False
                        isSecondaryPictureBoxActive = False
                        thisIsFirstPictureFileWeShow = False

                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0900: P1 set (not found loaded) isSecondaryPictureBoxActive=false")
                    End If
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0905: Error loading image: " & ex.Message)
                    Return
                End Try
            End If
            currentLoadedFileName = currentFileName

            UpdateControlVisibility()
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0920: file is a same, pic set is skipped")
        End If

        If Web_View2 IsNot Nothing AndAlso isWebView2Visible Then
            Web_View2.Source = New Uri("about:blank")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0930: WV2 blank")
        End If

        If Not Web_Browser.DocumentText = "" Then
            Web_Browser.DocumentText = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0940: WB blank")
        End If

    End Sub

    Private Function GetOppositeColor(backgroundColor As System.Drawing.Color) As System.Drawing.Color
        Dim sumColor = (Int(backgroundColor.R) + Int(backgroundColor.G) + Int(backgroundColor.B))

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0942: colorsum " & sumColor.ToString)

        If sumColor < 200 Then
            Return System.Drawing.Color.White
        Else
            Return System.Drawing.Color.Black
        End If
    End Function

    Private Sub UpdateControlVisibility()

        Picture_Box_1.Visible = isPictureBox1Visible
        Picture_Box_2.Visible = isPictureBox2Visible
        Web_Browser.Visible = isWebBrowser1Visible
        If isWebView2Available AndAlso Web_View2 IsNot Nothing Then
            Web_View2.Visible = isWebView2Visible
        End If

        If isPictureBox1Visible OrElse isPictureBox2Visible Then
            Web_Browser.Visible = False
            If Web_View2 IsNot Nothing AndAlso Web_View2.Visible Then
                isWebView2Visible = False
                Web_View2.Visible = isWebView2Visible
            End If

            Dim bmp As Bitmap = Nothing

            If isPictureBox1Visible AndAlso Picture_Box_1.Image IsNot Nothing AndAlso TypeOf Picture_Box_1.Image Is Bitmap Then
                bmp = CType(Picture_Box_1.Image, Bitmap)
                '    Picture_Box_1.Refresh()
                Picture_Box_1.BringToFront()
            ElseIf isPictureBox2Visible AndAlso Picture_Box_2.Image IsNot Nothing AndAlso TypeOf Picture_Box_2.Image Is Bitmap Then
                bmp = CType(Picture_Box_2.Image, Bitmap)
                '       Picture_Box_2.Refresh()
                Picture_Box_2.BringToFront()
                End If

                If bmp IsNot Nothing Then
                    If 1 < bmp.Width AndAlso 1 < bmp.Height Then
                        Dim pixelColor As System.Drawing.Color = System.Drawing.Color.Black

                        If bmp.Width > 1 AndAlso bmp.Height > 1 Then
                            pixelColor = bmp.GetPixel(1, 1)
                        End If

                        If pixelColor <> lastBackColor Then
                            lastBackColor = pixelColor

                            Me.BackColor = pixelColor

                            Dim OppositeColor = GetOppositeColor(pixelColor)
                            For Each ctrl As Control In Me.Controls
                                If TypeOf ctrl Is Label Then
                                    Dim lbl As Label = CType(ctrl, Label)
                                    lbl.ForeColor = OppositeColor
                                    lbl.BackColor = System.Drawing.Color.Transparent
                                ElseIf TypeOf ctrl Is Button Then
                                    Dim btn As Button = CType(ctrl, Button)
                                    btn.ForeColor = OppositeColor
                                ElseIf TypeOf ctrl Is ComboBox Then
                                    Dim cmb As ComboBox = CType(ctrl, ComboBox)
                                    cmb.BackColor = pixelColor
                                    cmb.ForeColor = OppositeColor
                                ElseIf TypeOf ctrl Is CheckBox Then
                                    Dim chb As CheckBox = CType(ctrl, CheckBox)
                                    chb.BackColor = pixelColor
                                    chb.ForeColor = OppositeColor
                                End If
                            Next
                        End If
                    End If
                End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0945: picture box sizes: " & If(isPictureBox1Visible, "P1: ", "P2: ") & If(isPictureBox1Visible, Picture_Box_1.Width.ToString, Picture_Box_2.Width.ToString) & "x" & If(isPictureBox1Visible, Picture_Box_1.Height.ToString, Picture_Box_2.Height.ToString))
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0950: Visibility set: " & If(isPictureBox1Visible, "P1-YES ", "P1-NO ") & If(isPictureBox2Visible, "P2-YES ", "P2-NO ") & If(isWebBrowser1Visible, "WB-YES ", "WB-NO ") & If(isWebView2Visible, "WV2-YES", "WV2-NO "))
    End Sub

    Private Sub UpdateCurrentFileAndDisplay(isFileFound As Boolean, isAfterUndo As Boolean)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0381: UpdateCurrentFileAndDisplay, currentFileName: " & currentFileName)
        currentFileName = ""

        If totalFilesCount <> 0 Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0960: isFileFound = " & isFileFound.ToString)
            If isFileFound Then
                currentFileName = If(useArray, filesArray(currentFileIndex), filesList(currentFileIndex))
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0970: currentFileIndex = " & currentFileIndex.ToString)
            Else
                currentFileName = targetImagePath
            End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0980: currentFileName = " & currentFileName)

            Dim fileIndexDisplay As Integer = currentFileIndex + 1
            lbl_File_Number.Text = If(lngRus, "Файл: " & fileIndexDisplay.ToString() & " из " & totalFilesCount.ToString(), "File: " & fileIndexDisplay.ToString() & " from " & totalFilesCount.ToString())

            Try
                Dim fileExtension As String = Path.GetExtension(currentFileName).ToLower()
                Dim fileUri As String = New Uri(currentFileName).ToString()

                If imageFileExtensions.Contains(fileExtension) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1030: P to load")
                    LoadStandardImageInPictureBox()
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1040: Picture box is set")
                ElseIf videoFileExtensions.Contains(fileExtension) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1010: WB to load")
                    LoadVideoInWebBrowser(fileUri)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1020: WB is set")
                ElseIf isWebView2Available AndAlso webImageFileExtensions.Contains(fileExtension) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0990: WV2 to load")
                    LoadWebImageInWebView2(fileUri)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1000: WV2 is set")
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1045: No selected control to show!?")
                End If

                isFirstPictureBoxNeedToBeCached = isSecondaryPictureBoxActive

                If isSlideShowRandom OrElse isFileReseivedFromOutside Then
                    nextAfterCurrentFileName = ""
                    isFileReseivedFromOutside = False
                ElseIf Not wasExternalInputLast AndAlso Not (fileList Is Nothing And filesArray Is Nothing) Then
                    nextAfterCurrentFileName = If(totalFilesCount > 0, If(totalFilesCount = currentFileIndex + 1, If(useArray, filesArray(0), filesList(0)), If(useArray, filesArray(currentFileIndex + 1), filesList(currentFileIndex + 1))), "")
                Else
                    nextAfterCurrentFileName = ""
                End If

                If bgWorkerOnline OrElse BgWorker.IsBusy Then
                    BgWorker.CancelAsync()
                    bgWorkerOnline = False
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1050: BgWorker set off line")
                End If

                If Not bgWorkerOnline AndAlso Not BgWorker.IsBusy Then
                    bgWorkerOnline = True
                    BgWorker.RunWorkerAsync()
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1060: BgWorker is run")
                End If

            Catch ex As Exception
                If Not isAfterUndo Then
                    MsgBox("E005 " & ex.Message)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1070: E005 " & ex.Message)
                Else
                    lbl_Status.Text = If(lngRus, "Файл " & currentFileName & " перемещается назад операционной системой.", "File " & currentFileName & " moving back by OS.")
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1080: UNdo E005 " & ex.Message)
                End If
            End Try
        Else
            If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image?.Dispose()
            If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image?.Dispose()
            currentLoadedFileName = ""
            Web_Browser.DocumentText = ""
            If Web_View2 IsNot Nothing Then
                Web_View2.Source = New Uri("about:blank")
            End If
            lbl_File_Number.Text = ""
            lbl_Status.Text = If(lngRus, "! Нет файлов в папке", "! No files in folder")
            isPictureBox1Visible = False
            isPictureBox2Visible = False
            isWebBrowser1Visible = False
            isWebView2Visible = False

            UpdateControlVisibility()

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1090: No files in folder, all wiped")
        End If
    End Sub

    Private Structure FileEntry
        Public Property FilePath As String
        Public Property FileSize As Long
        Public Property FileName As String
        Public Property FileDate As Date
    End Structure

    Private Function GetFiles() As Object
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1095: GetFiles..")

        Try
            Dim dirInfo As DirectoryInfo = FileSystem.GetDirectoryInfo(currentFolderPath)
            Dim fileEntries As List(Of FileEntry) = dirInfo.EnumerateFiles() _
            .Where(Function(f) allSupportedExtensions.Contains(f.Extension.ToLower())) _
            .Select(Function(f) New FileEntry With {
                .FilePath = f.FullName,
                .FileSize = f.Length,
                .FileName = f.Name,
                .FileDate = f.LastWriteTime
            }).ToList()

            ' Ограничиваем количество файлов
            If fileEntries.Count > MaxFiles Then
                fileEntries = fileEntries.Take(MaxFiles).ToList()
            End If

            If fileEntries.Count = 0 Then
                Return Nothing
            End If

            ' Шаг 2: Сортировка в памяти
            Dim orderedEntries As IEnumerable(Of FileEntry)
            Select Case cmbox_Sort.SelectedItem?.ToString()
                Case "abc"
                    orderedEntries = fileEntries.OrderBy(Function(f) f.FileName)
                Case "xyz"
                    orderedEntries = fileEntries.OrderByDescending(Function(f) f.FileName)
                Case "rnd"
                    orderedEntries = fileEntries.OrderBy(Function(f) Guid.NewGuid())
                Case ">size"
                    orderedEntries = fileEntries.OrderByDescending(Function(f) f.FileSize)
                Case "<size"
                    orderedEntries = fileEntries.OrderBy(Function(f) f.FileSize)
                Case ">time"
                    orderedEntries = fileEntries.OrderByDescending(Function(f) f.FileDate)
                Case "<time"
                    orderedEntries = fileEntries.OrderBy(Function(f) f.FileDate)
                Case Else
                    orderedEntries = fileEntries.OrderBy(Function(f) f.FilePath)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1109:  sort is lost?!")
            End Select

            Dim filePaths As List(Of String) = orderedEntries.Select(Function(f) f.FilePath).ToList()

            If useArray Then
                Return filePaths.ToArray()
            Else
                Return filePaths
            End If
        Catch ex As Exception
            lbl_Status.Text = If(lngRus, "! Ошибка чтения файлов", "! Error reading files")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1110: Error reading files: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub ISizeChanged()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1935: ISizeChanged")

        If isFullScreen <> lastFullScreenState Then
            If isFullScreen Then
                FormBorderStyle = FormBorderStyle.None
                WindowState = FormWindowState.Maximized
            Else
                FormBorderStyle = FormBorderStyle.Sizable
                WindowState = FormWindowState.Normal
            End If
            lastFullScreenState = isFullScreen
        End If

        If isFullScreen Then
            Buttons_to_fullscreen()
        Else
            Buttons_to_normal()
        End If

        Web_Browser.Size = Picture_Box_1.Size
        Web_Browser.Location = Picture_Box_1.Location

        If Web_View2 IsNot Nothing Then
            Web_View2.Size = Picture_Box_1.Size
            Web_View2.Location = Picture_Box_1.Location
        End If

    End Sub
    Private Sub SetViewSizes()
        ISizeChanged()
    End Sub

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        Debug.WriteLine(" - - - ")
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0000: Form started")
        InitNew()

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0010: init finished")

        isTextBoxEdition = True
        applicationRunsCount = 0
        Integer.TryParse(GetSetting(appName, secName, "RunsCount", "0"), applicationRunsCount)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0020: Apps RUN: " & applicationRunsCount.ToString)

        lngRus = GetSetting(appName, secName, "LngRus", "1") = "1"

        cmbox_Sort.SelectedIndex = 0
        btn_Language.Text = If(lngRus, "EN", "RU")
        LngCh()

        Dim isFirstRunStriing As String = GetSetting(appName, secName, "FirstRun", "1")
        lbl_Help_Info.Visible = isFirstRunStriing = "1"

        copyMode = GetSetting(appName, secName, "CopyMode", "0") = "1"
        chkbox_Top_Most.Checked = GetSetting(appName, secName, "chkTopMost", "0") = "1"
        isTableFormOpen = GetSetting(appName, secName, "TableOpened", "0") = "1"

        Dim videoVolumeStr = GetSetting(appName, secName, "VideoVolume", "1.0")
        Double.TryParse(videoVolumeStr, videoVolume)

        For z = 0 To 9
            moveOnKey(z) = GetSetting(appName, secName, "MoveOn" & z.ToString, "")
        Next
        moveOnKey(10) = GetSetting(appName, secName, "MoveOn0", "")

        Dim recentFoldersString As String = GetSetting(appName, secName, "RecentFolders", "")
        If Not String.IsNullOrEmpty(recentFoldersString) Then
            recentFolders = recentFoldersString.Split("|"c).ToList()
            recentFolders.RemoveAll(Function(x) String.IsNullOrEmpty(x))

            For Each folder In recentFolders
                If cmbox_Media_Folder.Items.Count < MaxRecentFolders Then
                    cmbox_Media_Folder.Items.Add(folder)
                End If
            Next
        End If

        If the_Table_Form.chkbox_Independent_Thread_For_File_Operation IsNot Nothing Then
            the_Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked = GetSetting(appName, secName, "UseIndependentThreadForOperationsWithFiles", "0") = "1"
        End If

        If My.Application.CommandLineArgs.Count > 0 Then
            ProcessArgument(My.Application.CommandLineArgs(0))
        Else
            currentFolderPath = GetSetting(appName, secName, "ImageFolder", "")
            If Not currentFolderPath = "" Then
                totalFilesCount = FileSystem.GetDirectoryInfo(currentFolderPath).EnumerateFiles.Count

                Integer.TryParse(GetSetting(appName, secName, "LastCounter"), currentFileIndex)
                If currentFileIndex > 0 AndAlso currentFileIndex < totalFilesCount Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0040: folder and file found in savings")

                    ReadShowMediaFile("ReadFiles")
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1157: folder set from savings, but saved file is not found")
                    currentFileIndex = 1

                    ReadShowMediaFile("ReadFolderAndFile")
                End If
            Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1158: no folder saved")
            End If
        End If

        If isTableFormOpen Then
            the_Table_Form.Show()
        End If
        isTextBoxEdition = False

        Dim appTopInt As Integer = 100
        Dim appLeftInt As Integer = 100
        Dim appWidthInt As Integer = 640
        Dim appHeightInt As Integer = 480

        Integer.TryParse(GetSetting(appName, secName, "AppTop"), appTopInt)
        Integer.TryParse(GetSetting(appName, secName, "AppLeft"), appLeftInt)
        Integer.TryParse(GetSetting(appName, secName, "AppWidth"), appWidthInt)
        Integer.TryParse(GetSetting(appName, secName, "AppHeight"), appHeightInt)

        appTopInt = If(appTopInt < 0 OrElse appTopInt > 1920, 100, appTopInt)
        appLeftInt = If(appLeftInt < 0 OrElse appLeftInt > 720, 100, appLeftInt)
        appWidthInt = If(appWidthInt < 640 OrElse appWidthInt > 1920, 640, appWidthInt)
        appHeightInt = If(appHeightInt < 480 OrElse appHeightInt > 1024, 480, appHeightInt)

        Me.SetBounds(appLeftInt, appTopInt, appWidthInt, appHeightInt)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1950: Form_Sizes: " & appLeftInt.ToString & " - " & appTopInt.ToString & " " & appWidthInt.ToString & " - " & appHeightInt.ToString)

        ResizeDebounceTimer.Stop()

        ISizeChanged()

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1120: Form Loaded")
    End Sub

    Private Sub Form1_Resize(sender As Object, e As EventArgs) Handles Me.Resize
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1126: Form Resize")
        ResizeDebounceTimer.Stop()
        ResizeDebounceTimer.Start()
    End Sub

    Private Sub ResizeDebounceTimer_Tick(sender As Object, e As EventArgs) Handles ResizeDebounceTimer.Tick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1128: ResizeDebounceTimer_Tick")
        ResizeDebounceTimer.Stop()
        ISizeChanged()
    End Sub

    Private Sub Button2_Click(sender As Object, e As EventArgs) Handles btn_Prev_File.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1130: btn_Prev_File")
        SlideShowTimer.Enabled = False
        ReadShowMediaFile("ReadPrevFile")
    End Sub

    Private Sub Button3_Click(sender As Object, e As EventArgs) Handles btn_Next_File.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1140: btn_Next_File")
        SlideShowTimer.Enabled = False
        ReadShowMediaFile("ReadNextFile")
    End Sub

    Private Sub PictureBox1_MouseDown(sender As Object, e As MouseEventArgs) Handles Picture_Box_1.MouseDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1150: PictureBox1_MouseDown")
        MouseUse(e)
    End Sub

    Private Sub PictureBox2_MouseDown(sender As Object, e As MouseEventArgs) Handles Picture_Box_2.MouseDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1160: PictureBox2_MouseDown")
        MouseUse(e)
    End Sub


    Private Sub MouseUse(ByVal e As MouseEventArgs)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1170: MouseUse Delta: " & e.Delta.ToString)

        SlideShowTimer.Enabled = False

        If (Control.ModifierKeys And Keys.Alt) = Keys.Alt Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1171: zoom reset")

            Dim isTop = lbl_Status.Top + lbl_Status.Height - 4

            Picture_Box_1.Top = isTop
            Picture_Box_1.Left = 2
            Picture_Box_1.Width = Me.Width - 4
            Picture_Box_1.Height = Me.Height - isTop - 4

            Picture_Box_2.Size = Picture_Box_1.Size
            Picture_Box_2.Location = Picture_Box_1.Location

        ElseIf e.Delta <> 0 AndAlso (isPictureBox1Visible OrElse isPictureBox2Visible) AndAlso (Control.ModifierKeys And Keys.Control) = Keys.Control Then
            Dim zoomFactor As Single = If(e.Delta > 0, 1.1F, 0.9F)

            Dim oldWidth As Integer = Picture_Box_1.Width
            Dim oldHeight As Integer = Picture_Box_1.Height
            Dim oldLeft As Integer = Picture_Box_1.Left
            Dim oldTop As Integer = Picture_Box_1.Top

            Dim newWidth As Integer = CInt(oldWidth * zoomFactor)
            Dim newHeight As Integer = CInt(oldHeight * zoomFactor)

            Dim mouseX As Integer = e.X - oldLeft
            Dim mouseY As Integer = e.Y - oldTop

            Dim relativeX As Single = CSng(mouseX / oldWidth)
            Dim relativeY As Single = CSng(mouseY / oldHeight)

            Dim newLeft As Integer = CInt(oldLeft - (newWidth - oldWidth) * relativeX)
            Dim newTop As Integer = CInt(oldTop - (newHeight - oldHeight) * relativeY)

            Picture_Box_1.Width = newWidth
            Picture_Box_1.Height = newHeight
            Picture_Box_1.Left = newLeft
            Picture_Box_1.Top = newTop

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1172: new size: " & newWidth.ToString & "-" & newHeight.ToString & " " & newLeft.ToString & "-" & newTop.ToString)

            Picture_Box_2.Size = Picture_Box_1.Size
            Picture_Box_2.Location = Picture_Box_1.Location
        Else
            Select Case e.Delta
                Case Is < 0 ' Прокрутка вниз
                    ReadShowMediaFile("ReadNextFile") ' Следующий файл
                Case Is > 0 ' Прокрутка вверх
                    ReadShowMediaFile("ReadPrevFile") ' Предыдущий файл
                Case 0 ' Клик
                    Select Case e.Button
                        Case MouseButtons.Left
                            ReadShowMediaFile("ReadNextFile") ' Следующий файл
                        Case MouseButtons.Right
                            If Not isWebBrowser1Visible Then
                                ReadShowMediaFile("ReadPrevFile")
                            End If
                        Case Windows.Forms.MouseButtons.Middle
                            RenameCurrentFile()
                        Case Windows.Forms.MouseButtons.XButton1
                            ReadShowMediaFile("ReadNextFile")
                        Case Windows.Forms.MouseButtons.XButton2
                            ReadShowMediaFile("ReadPrevFile")
                    End Select
            End Select
        End If

    End Sub

    Private Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        Try
            SaveSetting(appName, secName, "ImageFolder", currentFolderPath)
            SaveSetting(appName, secName, "LastCounter", currentFileIndex.ToString)
            SaveSetting(appName, secName, "chkTopMost", If(chkbox_Top_Most.Checked, "1", "0"))
            For z = 0 To 9
                Try
                    SaveSetting(appName, secName, "MoveOn" & z.ToString, moveOnKey(z).ToString)
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1180: ERR: " & ex.Message)
                Finally
                    moveOnKey(z) = Nothing
                End Try
            Next
            SaveSetting(appName, secName, "LngRus", If(lngRus, "1", "0"))
            SaveSetting(appName, secName, "FirstRun", "0")
            SaveSetting(appName, secName, "CopyMode", If(copyMode, "1", "0"))
            SaveSetting(appName, secName, "TableOpened", If(the_Table_Form.Visible, "1", "0"))
            SaveSetting(appName, secName, "RunsCount", (applicationRunsCount + 1).ToString)

            If Me.Top >= 0 Then SaveSetting(appName, secName, "AppTop", Me.Top.ToString)
            If Me.Left >= 0 Then SaveSetting(appName, secName, "AppLeft", Me.Left.ToString)
            If Me.Height >= 200 Then SaveSetting(appName, secName, "AppHeight", Me.Height.ToString)
            If Me.Width >= 320 Then SaveSetting(appName, secName, "AppWidth", Me.Width.ToString)

            SaveSetting(appName, secName, "VideoVolume", videoVolume.ToString("F2"))

            Dim recentFoldersString As String = String.Join("|", recentFolders)
            SaveSetting(appName, secName, "RecentFolders", recentFoldersString)

            SaveSetting(appName, secName, "UseIndependentThreadForOperationsWithFiles", If(the_Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked, "1", "0"))

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1190: settings are saved")
        Catch ex As Exception
            MsgBox("E010 " & ex.Message)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1200: ERR: " & ex.Message)
        End Try

        If BgWorker.IsBusy Then
            BgWorker.CancelAsync()
            Dim timeout As Integer = 1000
            Dim startTime As DateTime = DateTime.Now
            While BgWorker.IsBusy AndAlso (DateTime.Now - startTime).TotalMilliseconds < timeout
                Thread.Sleep(10)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1210: BgWorker try to CancelAsync")
            End While
        End If

        If FileOperationWorker.IsBusy Then
            FileOperationWorker.CancelAsync()
            Dim timeout As Integer = 5000 ' Увеличим таймаут для операций с файлами
            Dim startTime As DateTime = DateTime.Now
            While FileOperationWorker.IsBusy AndAlso (DateTime.Now - startTime).TotalMilliseconds < timeout
                Thread.Sleep(10)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1220: FileOperationWorker try to CancelAsync")
            End While
        End If

        SlideShowTimer.Enabled = False
        SlideShowTimer.Dispose()

        If Web_View2 IsNot Nothing Then
            RemoveHandler Web_View2.NavigationCompleted, AddressOf WebView2_NavigationCompleted
            Web_View2.Dispose()
            Web_View2 = Nothing
        End If
        If Web_Browser IsNot Nothing Then
            '       RemoveHandler Web_Browser.DocumentCompleted, AddressOf WebBrowser1_DocumentCompleted
            Web_Browser.DocumentText = ""
            Web_Browser.Dispose()
        End If

        If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image?.Dispose()
        If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image?.Dispose()

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1230: form is closed")
    End Sub

    Private Sub Form1_KeyDown(sender As Object, e As KeyEventArgs) Handles MyBase.KeyDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1240: keyb: " & e.KeyCode.ToString)
        KeybUse(e)
    End Sub

    Private Sub RenameCurrentFile()
        Try
            If Not My.Computer.FileSystem.FileExists(currentFileName) Then
                lbl_Status.Text = If(lngRus, "! Файл не найден", "! File not found")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1250: file not found")
                Return
            End If
            Dim fileNameWithoutExtension As String = Path.GetFileNameWithoutExtension(currentFileName)
            Dim fileExtension As String = Path.GetExtension(currentFileName)
            Dim newFileName As String = InputBox(If(lngRus, "Введите новое имя файла:", "Enter new file name:"),
                                            If(lngRus, "Переименование файла", "Rename File"),
                                            fileNameWithoutExtension)
            If String.IsNullOrEmpty(newFileName) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1260: empty new file name - no rename")
                Return
            End If
            Dim directory As String = Path.GetDirectoryName(currentFileName)
            Dim newFullPath As String = Path.Combine(directory, newFileName & fileExtension)
            If newFullPath = currentFileName Then
                lbl_Status.Text = If(lngRus, "! Имя не изменено", "! Name not changed")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1270: file is not new")
                Return
            End If
            My.Computer.FileSystem.RenameFile(currentFileName, newFileName & fileExtension)
            If useArray Then
                filesArray(currentFileIndex) = newFullPath
            Else
                filesList(currentFileIndex) = newFullPath
            End If
            currentFileName = newFullPath
            lbl_Status.Text = If(lngRus, "Файл переименован: " & newFileName & fileExtension, "File renamed: " & newFileName & fileExtension)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1280: file is renamed")

            ReadShowMediaFile("SetFile")
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1290: ERR: " & ex.Message)
            MsgBox("E011 " & ex.Message)
            lbl_Status.Text = If(lngRus, "! Ошибка переименования", "! Rename error")
        End Try
    End Sub

    Public Sub KeybUse(e As KeyEventArgs)
        SlideShowTimer.Enabled = False
        If Me.cmbox_Media_Folder.Focused Then
            If e.KeyCode = Keys.Enter AndAlso cmbox_Media_Folder.Text <> "" Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1300: Enter pressed")
                currentFolderPath = cmbox_Media_Folder.Text
                ReadShowMediaFile("ReadFolderAndFile")
            End If
            Exit Sub
        End If

        If e.Shift Then
            Select Case e.KeyCode
                Case Keys.PageDown
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1600: +100")
                    currentFileIndex = currentFileIndex + 100
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(lngRus, "+100 файлов", "+100 files")
                Case Keys.PageUp
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1610: -100")
                    currentFileIndex = currentFileIndex - 100
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(lngRus, "-100 файлов", "-100 files")
            End Select
        Else
            Select Case e.KeyCode
                Case Keys.N, Keys.Space, Keys.Right, Keys.BrowserForward, Keys.Next, Keys.PageDown
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1310: to next file")
                    ReadShowMediaFile("ReadNextFile")
                Case Keys.P, Keys.Left, Keys.B, Keys.BrowserBack, Keys.Back, Keys.PageUp
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1320: to prev file")
                    ReadShowMediaFile("ReadPrevFile")
                Case Keys.Y
                    ReadShowMediaFile("ReadForRandom")
                Case Keys.S
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1330: to slide show")
                    isSlideShowRandom = True
                    If SlideShowTimer.Enabled Then
                        SlideShowTimer.Interval = CInt(SlideShowTimer.Interval / 2)
                        If SlideShowTimer.Interval < 50 Then
                            SlideShowTimer.Interval = 50
                        End If
                    Else
                        SlideShowTimer.Interval = 10000
                        SlideShowTimer.Enabled = True
                    End If
                    ReadShowMediaFile("ReadForSlideShow")
                Case Keys.F6
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1340: to rename")
                    If Not String.IsNullOrEmpty(currentFileName) Then
                        RenameCurrentFile()
                    Else
                        lbl_Status.Text = If(lngRus, "! Нет файла для переименования", "! No file to rename")
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1350: No file to rename")
                    End If
                Case Keys.I, Keys.F5
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1360: to random show")
                    isSlideShowRandom = False
                    If SlideShowTimer.Enabled Then
                        SlideShowTimer.Interval = CInt(SlideShowTimer.Interval / 2)
                        If SlideShowTimer.Interval < 50 Then
                            SlideShowTimer.Interval = 50
                        End If
                    Else
                        SlideShowTimer.Interval = 10000
                        SlideShowTimer.Enabled = True
                    End If
                    ReadShowMediaFile("ReadForSlideShow")
                Case Keys.Home, Keys.H, Keys.BrowserHome
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1370: to first file")
                    currentFileIndex = 0
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(lngRus, "первый файл", "first file")
                Case Keys.End, Keys.E, Keys.L, Keys.BrowserStop
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1380: to last file")
                    currentFileIndex = totalFilesCount
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(lngRus, "последний файл", "last file")
                Case Keys.D, Keys.Delete
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1390: to delete")
                    ReadShowMediaFile("DeleteFile")
                Case Keys.D1, Keys.NumPad1
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1400: 01")
                    PoMove(1)
                Case Keys.D2, Keys.NumPad2
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1410: 02")
                    PoMove(2)
                Case Keys.D3, Keys.NumPad3
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1420: 03")
                    PoMove(3)
                Case Keys.D4, Keys.NumPad4
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1430: 04")
                    PoMove(4)
                Case Keys.D5, Keys.NumPad5
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1440: 05")
                    PoMove(5)
                Case Keys.D6, Keys.NumPad6
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1450: 06")
                    PoMove(6)
                Case Keys.D7, Keys.NumPad7
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1460: 07")
                    PoMove(7)
                Case Keys.D8, Keys.NumPad8
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1470: 08")
                    PoMove(8)
                Case Keys.D9, Keys.NumPad9
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1480: 09")
                    PoMove(9)
                Case Keys.D0, Keys.NumPad0
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1490: 0")
                    PoMove(10)
                Case Keys.R
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1500: Rotate")
                    Try
                        If isSecondaryPictureBoxActive Then
                            If isPictureBox2Visible AndAlso Picture_Box_2.Image IsNot Nothing Then
                                Picture_Box_2.Image.RotateFlip(RotateFlipType.Rotate90FlipNone)
                                '   Picture_Box_2.Refresh()
                                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1510: P2 Rotated")
                            End If
                        Else
                            If isPictureBox1Visible AndAlso Picture_Box_1.Image IsNot Nothing Then
                                Picture_Box_1.Image.RotateFlip(RotateFlipType.Rotate90FlipNone)
                                '    Picture_Box_1.Refresh()
                                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1520: P1 Rotated")
                            End If
                        End If
                    Catch ex As Exception
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1530: ERR: " & ex.Message)
                        MsgBox("E012 " & ex.Message)
                    End Try
                Case Keys.T
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1540: Rev Rotate")
                    Try
                        If isSecondaryPictureBoxActive Then
                            If isPictureBox2Visible AndAlso Picture_Box_2.Image IsNot Nothing Then
                                Picture_Box_2.Image.RotateFlip(RotateFlipType.Rotate270FlipNone)
                                '     Picture_Box_2.Refresh()
                                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1530: P2 Rotated")
                            End If
                        Else
                            If isPictureBox1Visible AndAlso Picture_Box_1.Image IsNot Nothing Then
                                Picture_Box_1.Image.RotateFlip(RotateFlipType.Rotate270FlipNone)
                                '       Picture_Box_1.Refresh()
                                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1540: P1 Rotated")
                            End If
                        End If
                    Catch ex As Exception
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1570: ERR: " & ex.Message)
                        MsgBox("E013 " & ex.Message)
                    End Try
                Case Keys.Up
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1580: -10")
                    currentFileIndex = currentFileIndex - 10
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(lngRus, "-10 файлов", "-10 files")
                Case Keys.Down
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1590: +10")
                    currentFileIndex = currentFileIndex + 10
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(lngRus, "+10 файлов", "+10 files")
                Case Keys.F1
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1620: F1 help")
                    lbl_Help_Info.Visible = True
                    lbl_Help_Info.Show()
                Case Keys.F2
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1630: F2")
                    the_Table_Form.Show()
                Case Keys.U
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1640: UnDo")
                    Undo()
                Case Keys.Escape, Keys.X, Keys.Q
                    If isFullScreen Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1641: ESC to normal")
                        isFullScreen = False
                        SetViewSizes()
                    Else
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1642: ESC to close")
                        Me.Close()
                    End If
            End Select
        End If
    End Sub

    Private Sub Button4_Click(sender As Object, e As EventArgs) Handles bt_Delete.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1650: bt_Delete")
        SlideShowTimer.Enabled = False
        ReadShowMediaFile("DeleteFile")
    End Sub

    Private Sub Form1_MouseDown(sender As Object, e As MouseEventArgs) Handles MyBase.MouseDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1660: Form MouseDown")
        MouseUse(e)
    End Sub

    Private Sub Form1_MouseWheel(sender As Object, e As MouseEventArgs) Handles Me.MouseWheel
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1670: Form MouseWheel")
        MouseUse(e)
    End Sub

    Private Sub PoMove(ByVal moveSec As Integer)
        Dim moveTo As String = moveOnKey(moveSec)
        Dim textKey As String = moveSec.ToString
        If textKey = "10" Then textKey = "0"
        If moveTo = "" Then
            lbl_Status.Text = If(lngRus, "! Нет каталога-получателя для клавиши " & textKey, "! No dest folder set with key " & textKey)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1680: No dest folder set with key " & textKey)
        Else
            If currentFileName <> "" Then
                Try
                    Dim newDest As String = moveTo
                    Dim testFile As System.IO.FileInfo
                    testFile = My.Computer.FileSystem.GetFileInfo(currentFileName)
                    newDest = newDest & "\" & testFile.Name
                    historySourceFileName = currentFileName
                    historyDestinationFileName = newDest

                    If the_Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked Then
                        If copyMode Then
                            currentFileOperation = "Copy"
                            currentFileOperationArgs = New String() {currentFileName, newDest, textKey}
                            lbl_Status.Text = If(lngRus, "!Ждите.. Файл копируется (" & textKey & ") в каталог " & newDest, "!Wait.. File copying (" & textKey & ") to " & newDest)
                            FileOperationWorker.RunWorkerAsync()

                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1690: file is copied ASYNC to " & newDest)

                            ReadShowMediaFile("ReadNextFile")
                        Else
                            currentFileOperation = "Move"
                            currentFileOperationArgs = New String() {currentFileName, newDest, textKey}

                            If isSecondaryPictureBoxActive Then
                                If isPictureBox2Visible AndAlso Picture_Box_2.Image IsNot Nothing Then
                                    Picture_Box_2.Image?.Dispose()
                                    Picture_Box_2.Image = Nothing
                                End If
                            Else
                                If isPictureBox1Visible AndAlso Picture_Box_1.Image IsNot Nothing Then
                                    Picture_Box_1.Image?.Dispose()
                                    Picture_Box_1.Image = Nothing
                                End If
                            End If

                            Web_Browser.DocumentText = ""
                            If Web_View2 IsNot Nothing Then
                                Web_View2.Source = New Uri("about:blank")
                            End If
                            lbl_Status.Text = If(lngRus, "!Ждите.. Файл переносится (" & textKey & ") в каталог " & newDest, "!Wait.. File moving (" & textKey & ") to " & newDest)

                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1700: move async run")

                            FileOperationWorker.RunWorkerAsync()
                            If useArray Then
                                filesArray = RemoveAt(filesArray, currentFileIndex)
                            Else
                                filesList.RemoveAt(currentFileIndex)
                            End If
                            totalFilesCount -= 1
                            If currentFileIndex > (totalFilesCount - 1) Then currentFileIndex = totalFilesCount - 1

                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1705: file is moved ASYNC to " & newDest)

                            ReadShowMediaFile("SetFile")
                        End If
                    Else
                        If copyMode Then
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1710: copy run")

                            lbl_Status.Text = If(lngRus, "!Ждите.. Файл копируется (" & textKey & ") в каталог " & newDest, "!Wait.. File copying (" & textKey & ") to " & newDest)
                            '     Me.Refresh()
                            My.Computer.FileSystem.CopyFile(currentFileName, newDest)
                            lbl_Status.Text = If(lngRus, "файл скопирован (" & textKey & ") в каталог " & newDest, "file copied (" & textKey & ") to " & newDest)

                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1715: file is copied to " & newDest)

                            ReadShowMediaFile("ReadNextFile")
                        Else
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1720: move run")

                            If isSecondaryPictureBoxActive Then
                                If isPictureBox1Visible AndAlso Picture_Box_2.Image IsNot Nothing Then
                                    Picture_Box_2.Image?.Dispose()
                                    Picture_Box_2.Image = Nothing
                                End If
                            Else
                                If isPictureBox1Visible AndAlso Picture_Box_1.Image IsNot Nothing Then
                                    Picture_Box_1.Image?.Dispose()
                                    Picture_Box_1.Image = Nothing
                                End If
                            End If

                            Web_Browser.DocumentText = ""
                            If Web_View2 IsNot Nothing Then
                                    Web_View2.Source = New Uri("about:blank")
                                End If
                                lbl_Status.Text = If(lngRus, "!Ждите.. Файл переносится (" & textKey & ") в каталог " & newDest, "!Wait.. File moving (" & textKey & ") to " & newDest)
                            '      Me.Refresh()
                            My.Computer.FileSystem.MoveFile(currentFileName, newDest)
                                If useArray Then
                                    filesArray = RemoveAt(filesArray, currentFileIndex)
                                Else
                                    filesList.RemoveAt(currentFileIndex)
                                End If
                                totalFilesCount -= 1
                                If currentFileIndex > (totalFilesCount - 1) Then currentFileIndex = totalFilesCount - 1
                                lbl_Status.Text = If(lngRus, "файл перенесен (" & textKey & ") в каталог " & newDest, "file moved (" & textKey & ") to " & newDest)

                                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1729: file is moved to " & newDest)

                                ReadShowMediaFile("SetFile")
                            End If
                        End If
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1730: E014 " & ex.Message)
                    MsgBox("E014 " & ex.Message)
                End Try
            Else
                lbl_Status.Text = If(lngRus, "! Нет файла ", "! No file")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1740: No file")
            End If
        End If
    End Sub

    Private Sub Undo()
        If historyDestinationFileName <> "" Then
            If copyMode Then
                If the_Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1750: undo copied async deletion")

                    currentFileOperation = "DeleteUndo"
                    currentFileOperationArgs = historyDestinationFileName
                    lbl_Status.Text = If(lngRus, "!Ждите. Файл удаляется в каталоге " & historyDestinationFileName, "!Wait. File deleting in " & historyDestinationFileName)
                    FileOperationWorker.RunWorkerAsync()
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1760: undo copied deletion")
                    Try
                        My.Computer.FileSystem.DeleteFile(historyDestinationFileName)
                        lbl_Status.Text = If(lngRus, "файл удален в каталоге " & historyDestinationFileName, "file deleted in " & historyDestinationFileName)
                        historyDestinationFileName = ""
                        historySourceFileName = ""
                    Catch ex As Exception
                        MsgBox("E015 " & ex.Message)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1770: undo E015 " & ex.Message)
                    End Try
                End If
            Else
                If the_Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1780: undo move async deletion")

                    currentFileOperation = "MoveUndo"
                    currentFileOperationArgs = New String() {historyDestinationFileName, historySourceFileName}
                    lbl_Status.Text = If(lngRus, "!Ждите. Возвращается в каталог " & historySourceFileName, "!Wait. File back to " & historySourceFileName)
                    FileOperationWorker.RunWorkerAsync()
                    If useArray Then
                        filesArray = AddAt(filesArray, historySourceFileName, currentFileIndex)
                    Else
                        filesList.Insert(currentFileIndex, historySourceFileName)
                    End If
                    totalFilesCount += 1
                    ReadShowMediaFile("AfterUndo")
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1790: undo move deletion")
                    Try
                        lbl_Status.Text = If(lngRus, "!Ждите. Возвращается в каталог " & historySourceFileName, "!Wait. File back to " & historySourceFileName)
                        '            Me.Refresh()
                        My.Computer.FileSystem.MoveFile(historyDestinationFileName, historySourceFileName)
                        If useArray Then
                            filesArray = AddAt(filesArray, historySourceFileName, currentFileIndex)
                        Else
                            filesList.Insert(currentFileIndex, historySourceFileName)
                        End If
                        totalFilesCount += 1
                        lbl_Status.Text = If(lngRus, "файл возвращен в каталог " & historySourceFileName, "file back to " & historySourceFileName)
                        ReadShowMediaFile("AfterUndo")
                        historyDestinationFileName = ""
                        historySourceFileName = ""
                    Catch ex As Exception
                        MsgBox("E016 " & ex.Message)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1800: undo E016 " & ex.Message)
                    End Try
                End If
            End If
            Web_Browser.DocumentText = ""
            If Web_View2 IsNot Nothing Then
                Web_View2.Source = New Uri("about:blank")
            End If
        Else
            lbl_Status.Text = If(lngRus, "! Нет истории о переносе", "! No history about moved files")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1810: No history about moved files")
        End If
    End Sub

    Private Sub FirstRun_Click(sender As Object, e As EventArgs) Handles lbl_Help_Info.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1820: lbl_Help_Info clicked and hidden")
        lbl_Help_Info.Visible = False
        lbl_Help_Info.Hide()
    End Sub

    Private Sub LngCh()
        lbl_Status.Text = ""
        If lngRus Then
            lbl_Folder.Text = "Каталог:"
            btn_Prev_File.Text = "<< пред(PgUp)"
            btn_Next_File.Text = "след(PgDn) >>"
            bt_Delete.Text = "удалить (del)"
            btn_Move_Table.Text = "таблица получателей"
            lbl_Help_Info.Text = " Програма для быстрого переноса/копирования изображений по папкам." & Chr(10) & Chr(10) &
                "Сначала заполните таблицу каталогов-получателей по клавишам 1,2,3.. - 0. " & Chr(10) &
                "Затем укажите каталог-источник для сортировки. " & Chr(10) &
                "Продвигайтесь по файлам с помощью стрелок, P/N (PgDn/PgUp) или кликов/скролла мыши. " & Chr(10) &
                "Стрелки вверх-вниз: +10-10 и Shift+ PgDn/PgUp: + 100/ - 100 файлов" & Chr(10) &
                "Y- случайно, S- случайное слайдшоу, I- слайдшоу. " & Chr(10) &
                "R/T для поворота картинки. " & Chr(10) &
                "F6 для переименования файла. " & Chr(10) &
                "Или за счет переноса/копирования по папкам клавишами (1,2,3.. - 0). " & Chr(10) &
                "Или за счет удаления текущего файла (del). " & Chr(10) &
                "Окно таблицы можно закрепить и щелкать мышью по колонке с цифрой. " & Chr(10) &
                "(U) -вернуть последный перенесенный файл (удалить скопированный). " & Chr(10) & Chr(10) &
                " Щелкните на этот текст (F1) для того, чтобы он исчез."
            btn_Language.Text = "EN"

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0030: Russian is set")
        Else
            btn_Language.Text = "RU"
            lngRus = False
            lbl_Folder.Text = "Folder:"
            btn_Prev_File.Text = "<< (P)rev"
            btn_Next_File.Text = "(N)ext >>"
            bt_Delete.Text = "(D)elete"
            btn_Move_Table.Text = "dest folders table"
            lbl_Help_Info.Text = " Program for fast image sorting." & Chr(10) & Chr(10) &
                "First fill dest folders table for keys: 1,2.. - 0. " & Chr(10) &
                "After set folder with you unsorted files. " & Chr(10) &
                "Go with files by P/N (PgDn/PgUp) keys or mouse clicks/scroll. " & Chr(10) &
                "Up/Down- +10-10 and Shift+ PgDn/PgUp- + 100/ - 100 files" & Chr(10) &
                "Y- random, S- random slide, I- slide. " & Chr(10) &
                "Or move/copy files into dest folders by keys (1,2.. - 0). " & Chr(10) &
                "Or by deleting files (del key). " & Chr(10) &
                "R/T to rotate the image. " & Chr(10) &
                "F6 to rename the file. " & Chr(10) &
                "You can lock Window with folders table and click on key numbers. " & Chr(10) &
                "(U)ndo last moved action (delete copying file). " & Chr(10) & Chr(10) &
                " Click on this text (F1) for hide it."

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0040: English is set")
        End If
    End Sub

    Private Sub butI_Click(sender As Object, e As EventArgs) Handles btn_Review.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1850: btn_Review clicked")
        FolderSelected()
    End Sub

    Private Sub FolderSelected()
        If currentFolderPath <> "" Then
            ReadShowMediaFile("ReadFolderAndFile")
        Else
            If lngRus Then
                MsgBox("Укажите каталог с медиа файлами..")
            Else
                MsgBox("Select folder with media files..")
            End If
        End If
    End Sub

    Public Sub DoKey(ByVal keyIndex As Integer)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1860: DoKey")

        If keyIndex = 0 Then
            ReadShowMediaFile("DeleteFile")
        Else
            PoMove(keyIndex + 1)
        End If
    End Sub

    Private Sub TextBox1_KeyPress(sender As Object, e As KeyPressEventArgs)
        If e.KeyChar = Convert.ToChar(Keys.Enter) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1870: Enter pressed in folder box")

            currentFolderPath = Me.cmbox_Media_Folder.Text
            FolderSelected()
        End If
    End Sub

    Private Sub Button6_Click(sender As Object, e As EventArgs) Handles btn_Slideshow.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1880: btn_Slideshow")

        isSlideShowRandom = False
        If SlideShowTimer.Enabled Then
            SlideShowTimer.Interval = CInt(SlideShowTimer.Interval / 2)
            If SlideShowTimer.Interval < 50 Then
                SlideShowTimer.Interval = 50
            End If
        Else
            SlideShowTimer.Interval = 10000
            SlideShowTimer.Enabled = True
        End If
        ReadShowMediaFile("ReadForSlideShow")
    End Sub

    Private Sub SlideShow_Elapsed() Handles SlideShowTimer.Tick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1890: SlideShowTimer")
        ReadShowMediaFile("ReadForSlideShow")
    End Sub

    Private Sub Button7_Click(sender As Object, e As EventArgs) Handles btn_Full_Screen.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1900: btn_Full_Screen (fullscreen)")
        isFullScreen = Not isFullScreen
        SetViewSizes()
    End Sub

    Private Sub Buttons_to_fullscreen()
        Dim the_Height_For_buttons = 20
        Dim the_Width_For_buttons = 15

        Picture_Box_1.Top = 0
        Picture_Box_1.Left = 0
        Picture_Box_1.Width = Me.Width
        Picture_Box_1.Height = Me.Height

        Picture_Box_2.Size = Picture_Box_1.Size
        Picture_Box_2.Location = Picture_Box_1.Location

        If isWebView2Available Then
            Web_View2.Size = Picture_Box_1.Size
            Web_View2.Location = Picture_Box_1.Location
        End If

        Web_Browser.Size = Picture_Box_1.Size
        Web_Browser.Location = Picture_Box_1.Location

        lbl_Folder.Visible = False
        btn_Move_Table.Visible = False

        cmbox_Sort.Visible = False

        With btn_Select_Folder
            .Top = 2
            .Left = 2
            .Width = the_Width_For_buttons * 2
            .Height = the_Height_For_buttons
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With btn_Review
            .Top = 2
            .Left = btn_Select_Folder.Left + btn_Select_Folder.Width + 10
            .Width = the_Width_For_buttons * 2
            .Height = the_Height_For_buttons
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With btn_Prev_File
            .Top = 2
            .Left = btn_Review.Left + btn_Review.Width + 20
            .Width = the_Width_For_buttons * 2
            .Height = the_Height_For_buttons
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With btn_Next_File
            .Top = 2
            .Left = btn_Prev_File.Left + btn_Prev_File.Width + 2
            .Width = the_Width_For_buttons * 3
            .Height = the_Height_For_buttons
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With btn_Next_Random
            .Top = 2
            .Left = btn_Next_File.Left + btn_Next_File.Width + 2
            .Width = the_Width_For_buttons * 2
            .Height = the_Height_For_buttons
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With btn_Random_Slideshow
            .Top = 2
            .Left = btn_Next_Random.Left + btn_Next_Random.Width + 20
            .Width = the_Width_For_buttons * 2
            .Height = the_Height_For_buttons
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With btn_Slideshow
            .Top = 2
            .Left = btn_Random_Slideshow.Left + btn_Random_Slideshow.Width + 2
            .Width = the_Width_For_buttons * 2
            .Height = the_Height_For_buttons
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With btn_Rename
            .Top = lbl_Status.Top + lbl_Status.Height + 32
            .Left = 2
            .Width = the_Width_For_buttons * 2
            .Height = the_Height_For_buttons
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With bt_Delete
            .Top = btn_Rename.Top + btn_Rename.Height + 30
            .Left = 2
            .Width = the_Width_For_buttons * 2
            .Height = the_Height_For_buttons
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1910: buttons resaized to full screeen")
    End Sub

    Private Sub Buttons_to_normal()

        Dim isTop = lbl_Status.Top + lbl_Status.Height - 4

        lbl_Folder.Visible = True
        btn_Move_Table.Visible = True

        If Not Picture_Box_1.Top = isTop OrElse Not Picture_Box_1.Width = Me.Width - 4 OrElse Not Picture_Box_1.Height = Me.Height - isTop - 4 Then

            Dim the_Height_For_buttons = 20
            Dim the_Width_For_buttons = 15

            Picture_Box_1.Top = isTop
            Picture_Box_1.Left = 2
            Picture_Box_1.Width = Me.Width - 4
            Picture_Box_1.Height = Me.Height - isTop - 4

            Picture_Box_2.Size = Picture_Box_1.Size
            Picture_Box_2.Location = Picture_Box_1.Location

            With cmbox_Sort
                .Top = 2
                .Left = 2
                .Width = the_Width_For_buttons * 3
                .Height = cmbox_Media_Folder.Height
                .Font = New Font("Arial", 7, FontStyle.Regular)
                .Visible = True
            End With
            With lbl_Folder
                .Top = 2
                .Left = cmbox_Sort.Left + cmbox_Sort.Width + 2
                .Height = cmbox_Media_Folder.Height
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With btn_Select_Folder
                .Top = 2
                .Left = cmbox_Media_Folder.Width + cmbox_Media_Folder.Left + 2
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With btn_Review
                .Top = 2
                .Left = btn_Select_Folder.Left + btn_Select_Folder.Width + 10
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With btn_Full_Screen
                .Top = 2
                .Left = btn_Review.Left + btn_Review.Width + 10
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With lbl_Slideshow_Time
                .Top = 2
                .Left = btn_Full_Screen.Left + btn_Full_Screen.Width + 10
                .Width = the_Width_For_buttons * 3
                .Height = btn_Select_Folder.Height
                .Visible = True
            End With
            With chkbox_Top_Most
                .Top = 2
                .Left = lbl_Slideshow_Time.Left + lbl_Slideshow_Time.Width + 40
                .Width = the_Width_For_buttons * 2
                .Height = btn_Select_Folder.Height
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With btn_Language
                .Top = 2
                .Left = chkbox_Top_Most.Left + chkbox_Top_Most.Width + 10
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            'second line
            With lbl_File_Number
                .Top = cmbox_Sort.Top + cmbox_Sort.Height + 4
                .Left = 2
                .Visible = True
            End With
            With btn_Prev_File
                .Top = lbl_File_Number.Top
                .Left = cmbox_Media_Folder.Left + 50
                .Width = the_Width_For_buttons * 7
                .Height = the_Height_For_buttons
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With btn_Next_File
                .Top = btn_Prev_File.Top
                .Left = btn_Prev_File.Left + btn_Prev_File.Width + 2
                .Width = the_Width_For_buttons * 7
                .Height = the_Height_For_buttons
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With btn_Next_Random
                .Top = btn_Next_File.Top
                .Left = btn_Next_File.Left + btn_Next_File.Width + 2
                .Width = the_Width_For_buttons * 3
                .Height = the_Height_For_buttons
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With btn_Random_Slideshow
                .Top = btn_Next_Random.Top
                .Left = btn_Next_Random.Left + btn_Next_Random.Width + 20
                .Width = the_Width_For_buttons * 3
                .Height = the_Height_For_buttons
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With btn_Slideshow
                .Top = btn_Random_Slideshow.Top
                .Left = btn_Random_Slideshow.Left + btn_Random_Slideshow.Width + 2
                .Width = the_Width_For_buttons * 3
                .Height = the_Height_For_buttons
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With btn_Move_Table
                .Top = btn_Slideshow.Top
                .Left = btn_Slideshow.Left + btn_Slideshow.Width + 20
                .Width = the_Width_For_buttons * 7
                .Height = the_Height_For_buttons
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With btn_Rename
                .Top = btn_Move_Table.Top
                .Left = btn_Move_Table.Left + btn_Move_Table.Width + 20
                .Width = the_Width_For_buttons * 3
                .Height = the_Height_For_buttons
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With bt_Delete
                .Top = btn_Rename.Top
                .Left = btn_Rename.Left + btn_Rename.Width + 20
                .Width = the_Width_For_buttons * 3
                .Height = the_Height_For_buttons
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1920: buttons resized to normal screen")
        End If

        If isWebView2Available Then
            Web_View2.Size = Picture_Box_1.Size
            Web_View2.Location = Picture_Box_1.Location
        End If

        Web_Browser.Size = Picture_Box_1.Size
        Web_Browser.Location = Picture_Box_1.Location
    End Sub

    Private Sub Form1_ResizeEnd(sender As Object, e As EventArgs) Handles Me.ResizeEnd
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1930: Form_ResizeEnd")
        ResizeDebounceTimer.Stop()
        ISizeChanged()
    End Sub

    Private Sub Button5_Click(sender As Object, e As EventArgs) Handles btn_Move_Table.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1960: btn_MoveTable")

        SlideShowTimer.Enabled = False
        the_Table_Form.Show()
    End Sub

    Private Sub Label1_MouseClick(sender As Object, e As MouseEventArgs) Handles lbl_Folder.MouseClick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1970: lbl_Folder MouseClick")
        CopyFilePathToClipboard()
    End Sub

    Private Sub StatusL_MouseClick(sender As Object, e As MouseEventArgs) Handles lbl_Status.MouseClick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1980: lbl_Status MouseClick")
        CopyFilePathToClipboard()
    End Sub

    Private Sub Button8_Click(sender As Object, e As EventArgs) Handles btn_Next_Random.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1990: btn_Next_Random")
        SlideShowTimer.Enabled = False
        ReadShowMediaFile("ReadForRandom")
    End Sub

    Private Sub Button9_Click(sender As Object, e As EventArgs) Handles btn_Random_Slideshow.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2000: btn_Random_Slideshow")
        isSlideShowRandom = True
        If SlideShowTimer.Enabled Then
            SlideShowTimer.Interval = CInt(SlideShowTimer.Interval / 2)
            If SlideShowTimer.Interval < 50 Then
                SlideShowTimer.Interval = 50
            End If
        Else
            SlideShowTimer.Interval = 10000
            SlideShowTimer.Enabled = True
        End If
        ReadShowMediaFile("ReadForSlideShow")
    End Sub

    Private Sub chkTopMost_CheckedChanged(sender As Object, e As EventArgs) Handles chkbox_Top_Most.CheckedChanged
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2010: chkTopMost_CheckedChanged")
        Me.TopMost = chkbox_Top_Most.Checked
    End Sub

    Private Sub ButtonLNG_Click(sender As Object, e As EventArgs) Handles btn_Language.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2020: btn_Language")

        lngRus = Not lngRus
        btn_Language.Text = If(lngRus, "EN", "RU")
        LngCh()
        'ReadShowMediaFile("SetFile")
    End Sub

    Private Sub ButtonRename_Click(sender As Object, e As EventArgs) Handles btn_Rename.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2030: btn_Rename")

        If Not String.IsNullOrEmpty(currentFileName) Then
            RenameCurrentFile()
        Else
            lbl_Status.Text = If(lngRus, "! Нет файла для переименования", "! No file to rename")
        End If
    End Sub

    Private Sub CheckWebView2Availability()
        If isWebView2Available Then Return

        Try
            Dim version As String = CoreWebView2Environment.GetAvailableBrowserVersionString()
            If Not String.IsNullOrEmpty(version) Then
                isWebView2Available = True
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0006: WebView2 Runtime found, version: " & version)
            Else
                isWebView2Available = False
                allSupportedExtensions.ExceptWith(webImageFileExtensions)
                Dim message As String = If(lngRus, "WebView2 Runtime не установлен. Форматы .webp, .heic, .avif, .svg не поддерживаются. Установите WebView2 для полной функциональности.",
                                  "WebView2 Runtime is not installed. Formats .webp, .heic, .avif, .svg are not supported. Install WebView2 for full functionality.")
                lbl_Status.Text = message
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2040: WebView2 Runtime is not found")
            End If
        Catch ex As Exception
            isWebView2Available = False
            allSupportedExtensions.ExceptWith(webImageFileExtensions)
            Dim message As String = If(lngRus, "Ошибка проверки WebView2: " & ex.Message & vbCrLf & "Форматы .webp, .heic, .avif, .svg не поддерживаются.",
                              "Error checking WebView2: " & ex.Message & vbCrLf & "Formats .webp, .heic, .avif, .svg are not supported.")
            MessageBox.Show(message, If(lngRus, "Ограниченный функционал", "Limited Functionality"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2050: WebView2 check failed with error: " & ex.Message)
        End Try
    End Sub

    Private Sub WebView2_NavigationCompleted(sender As Object, e As CoreWebView2NavigationCompletedEventArgs)
        If e.IsSuccess Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2060: WebView2_NavigationCompleted IsSuccess")
            isWebView2Loaded = True
            isWebView2Visible = True

            '    UpdateControlVisibility()
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2070: WebView2_NavigationCompleted Error: " & e.WebErrorStatus.ToString())
            isWebView2Visible = False
         '   UpdateControlVisibility()
        End If
    End Sub

    Private Sub InitializeWebView2()
        If isWebView2Available AndAlso Web_View2 Is Nothing Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0008: InitializeWebView2Async")
            Try
                Web_View2 = New WebView2()
                Web_View2.Visible = False
                Web_View2.Location = Picture_Box_1.Location
                Web_View2.Size = Picture_Box_1.Size
                Me.Controls.Add(Web_View2)
                Web_View2.EnsureCoreWebView2Async(Nothing)
                AddHandler Web_View2.NavigationCompleted, AddressOf WebView2_NavigationCompleted
                Web_View2.Source = New Uri("about:blank")

            Catch ex As Exception
                If Web_View2 IsNot Nothing Then
                    Web_View2.Dispose()
                    Web_View2 = Nothing
                End If
                isWebView2Available = False
                allSupportedExtensions.ExceptWith(webImageFileExtensions)

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2090: InitializeWebView2Async ERR " & ex.Message)
            End Try
        End If
    End Sub

    'Private Sub WebBrowser1_DocumentCompleted(sender As Object, e As WebBrowserDocumentCompletedEventArgs)
    '    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2100: Web Browser_DocumentCompleted")

    '    isWebBrowserLoaded = True
    '    'lastLoadedUri = e.Url.ToString()

    '    'If lastLoadedUri <> "about:blank" AndAlso Web_Browser.DocumentText <> "" Then
    '    '    isWebBrowser1Visible = True
    '    '    UpdateControlVisibility()
    '    'End If
    'End Sub

    Private Sub Label3_MouseClick(sender As Object, e As MouseEventArgs) Handles lbl_Current_File.MouseClick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2110: lbl_Current_File.MouseClick")
        CopyFilePathToClipboard()
    End Sub

    Private Sub CopyFilePathToClipboard()
        If Not String.IsNullOrEmpty(currentFileName) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2120: Filename sent to clipboard")
            Clipboard.SetText(currentFileName)
            lbl_Status.Text = If(lngRus, "Имя файла скопировано в буфер", "Filename sent to clipboard")
        End If
    End Sub

    Private Sub TextBox1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbox_Media_Folder.SelectedIndexChanged
        If Not isTextBoxEdition Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2130: cmbox_MediaFolder SelectedIndexChanged")

            If cmbox_Media_Folder.SelectedIndex >= 0 Then
                currentFolderPath = cmbox_Media_Folder.SelectedItem.ToString()

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2140: currentFolderPath = " & currentFolderPath)
                ReadShowMediaFile("ReadFolderAndFile")
            End If
        End If
    End Sub

    Private Sub SortComboBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbox_Sort.SelectedIndexChanged
        If Not isTextBoxEdition Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2150: cmbox_Sort SelectedIndexChanged")

            If Not String.IsNullOrEmpty(currentFolderPath) Then
                ReadShowMediaFile("ReadFolderAndFile")
            End If
        End If
    End Sub

    Private Sub FileOperationWorker_DoWork(sender As Object, e As DoWorkEventArgs) Handles FileOperationWorker.DoWork
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2160: FileOperationWorker_DoWork")

        Select Case currentFileOperation
            Case "Copy"
                Dim args As String() = DirectCast(currentFileOperationArgs, String())
                Dim sourceFile As String = args(0)
                Dim destFile As String = args(1)
                My.Computer.FileSystem.CopyFile(sourceFile, destFile)

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2170: file copied")

            Case "Move"
                Dim args As String() = DirectCast(currentFileOperationArgs, String())
                Dim sourceFile As String = args(0)
                Dim destFile As String = args(1)
                My.Computer.FileSystem.MoveFile(sourceFile, destFile)

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2180: file moved")

            Case "Delete"
                Dim filePath As String = DirectCast(currentFileOperationArgs, String)
                My.Computer.FileSystem.DeleteFile(filePath)
                historyDestinationFileName = ""
                historySourceFileName = ""

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2190: file deleted")

            Case "DeleteUndo"
                ' #todo: check undo from garbage bin
                Dim filePath As String = DirectCast(currentFileOperationArgs, String)
                My.Computer.FileSystem.DeleteFile(filePath)
                historyDestinationFileName = ""
                historySourceFileName = ""
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2200: file deleted in undo")

            Case "MoveUndo"
                Dim args As String() = DirectCast(currentFileOperationArgs, String())
                Dim sourceFile As String = args(0)
                Dim destFile As String = args(1)
                My.Computer.FileSystem.MoveFile(sourceFile, destFile)
                historyDestinationFileName = ""

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2210: file moved after undo")
            Case Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2215: !! FileOperationWorker_DoWork for nothing ")
        End Select
    End Sub

    Private Sub FileOperationWorker_RunWorkerCompleted(sender As Object, e As RunWorkerCompletedEventArgs) Handles FileOperationWorker.RunWorkerCompleted
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2220: FileOperationWorker_RunWorkerCompleted")

        If e.Error Is Nothing Then
            Select Case currentFileOperation
                Case "Copy"
                    Dim args As String() = DirectCast(currentFileOperationArgs, String())
                    Dim textKey As String = args(2)
                    Dim destFile As String = args(1)
                    lbl_Status.Text = If(lngRus, "файл скопирован (" & textKey & ") в каталог " & destFile, "file copied (" & textKey & ") to " & destFile)

                Case "Move"
                    Dim args As String() = DirectCast(currentFileOperationArgs, String())
                    Dim textKey As String = args(2)
                    Dim destFile As String = args(1)
                    lbl_Status.Text = If(lngRus, "файл перенесен (" & textKey & ") в каталог " & destFile, "file moved (" & textKey & ") to " & destFile)

                Case "Delete"

                Case "DeleteUndo"
                    lbl_Status.Text = If(lngRus, "файл удален в каталоге " & historyDestinationFileName, "file deleted in " & historyDestinationFileName)
                    historyDestinationFileName = ""
                    historySourceFileName = ""

                Case "MoveUndo"
                    lbl_Status.Text = If(lngRus, "файл возвращен в каталог " & historySourceFileName, "file back to " & historySourceFileName)
                    historyDestinationFileName = ""
                    historySourceFileName = ""
            End Select
        Else
            lbl_Status.Text = If(lngRus, "Ошибка операции: " & e.Error.Message, "Operation error: " & e.Error.Message)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2230: FileOperationWorker_RunWorkerCompleted ERR " & e.Error.Message)
        End If
    End Sub

    Private Sub Label1_Click(sender As Object, e As EventArgs) Handles lbl_Folder.Click
        If Not String.IsNullOrEmpty(cmbox_Media_Folder.Text) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2240: Folder sent to clipboard")
            Clipboard.SetText(cmbox_Media_Folder.Text)
            lbl_Status.Text = If(lngRus, "Имя папки скопировано в буфер", "Folder sent to clipboard")
        End If
    End Sub

    Private Sub StatusL_Click(sender As Object, e As EventArgs) Handles lbl_Status.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2300: Visibility set: " & If(isPictureBox1Visible, "P1-YES ", "P1-NO ") & If(isPictureBox2Visible, "P2-YES ", "P2-NO ") & If(isWebBrowser1Visible, "WB-YES ", "WB-NO ") & If(isWebView2Visible, "WV2-YES", "WV2-NO "))
    End Sub
End Class