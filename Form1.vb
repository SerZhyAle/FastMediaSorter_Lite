'sza130806lite
'sza240823 eto pizdec
'sza250411 random, filters, etc
'sza250502 refactor
'sza250506 grok
'FastMediaSorter

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
Public Class Form1
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
    Private isWebBrowserLoaded As Boolean = False
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
    Private WithEvents webView2Control As WebView2 = Nothing
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

    Public Sub New()
        ResizeDebounceTimer.Interval = 100
        ResizeDebounceTimer.Enabled = False

        Dim createdNew As Boolean
        mutex = New Mutex(True, AppMutexName, createdNew)

        InitializeComponent()
        BgWorker.WorkerReportsProgress = True
        BgWorker.WorkerSupportsCancellation = True
        WebBrowser1.ObjectForScripting = Me
        InitializeExtensionLists()
        SetWebBrowserCompatibilityMode()
        AddHandler WebBrowser1.DocumentCompleted, AddressOf WebBrowser1_DocumentCompleted
        AddHandler PictureBox1.LoadCompleted, AddressOf PictureBox1_LoadCompleted
        AddHandler PictureBox2.LoadCompleted, AddressOf PictureBox2_LoadCompleted

        CheckWebView2Availability()
        InitializeFileOperationWorker()
    End Sub

    Private Function LoadImage(filePath As String) As Image
        Dim nextImage As Image
        Using stream As New FileStream(nextAfterCurrentFileName, FileMode.Open, FileAccess.Read, FileShare.Read)
            nextImage = Image.FromStream(stream)
        End Using

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0040: LoadImage: " & nextAfterCurrentFileName)
        Return nextImage
    End Function

    Private Sub BgWorker_DoWork(sender As Object, e As DoWorkEventArgs) Handles BgWorker.DoWork
        Dim worker As BackgroundWorker = DirectCast(sender, BackgroundWorker)

        If worker.CancellationPending Then
            e.Cancel = True
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0050: BgWorker got cancellation")
        End If

        If currentFileName = "" OrElse Not My.Computer.FileSystem.FileExists(currentFileName) Then
            Label3.Text = ""
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

    Private Sub PictureBox1_LoadCompleted(sender As Object, e As System.ComponentModel.AsyncCompletedEventArgs)
        If e.Error Is Nothing Then
            If Not isSecondaryPictureBoxActive Then
                isPictureBox1Visible = True
                UpdateControlVisibility()
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0130: P1 loaded and visible")
            End If
        Else
            If Not isSecondaryPictureBoxActive Then
                isPictureBox1Visible = True
                UpdateControlVisibility()
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0140: P1 not loaded and invisible")
            End If
        End If
    End Sub

    Private Sub PictureBox2_LoadCompleted(sender As Object, e As System.ComponentModel.AsyncCompletedEventArgs)
        If e.Error Is Nothing Then
            If isSecondaryPictureBoxActive Then
                isPictureBox2Visible = True
                UpdateControlVisibility()
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0150: P2 loaded and visible")
            End If
        Else
            If isSecondaryPictureBoxActive Then
                isPictureBox2Visible = True
                UpdateControlVisibility()
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0160: P2 not loaded and invisible")
            End If
        End If
    End Sub

    Private Sub BgWorker_ProgressChanged(sender As Object, e As ProgressChangedEventArgs) Handles BgWorker.ProgressChanged
        Dim userState As Dictionary(Of String, String) = DirectCast(e.UserState, Dictionary(Of String, String))

        If Not userState.Count = 0 Then

            If userState.Keys(0) = "fileSizeText" Then

                Dim fileSizeText As String = userState("fileSizeText")

                If Not fileSizeText = Nothing Then
                    Label3.Text = If(lngRus, "Текущий: ", "Current: ") & currentFileName & " " & fileSizeText
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0170: BgWorker size calculated: " & fileSizeText)
                End If

            ElseIf userState.Keys(0) = "totalFilesCountText" Then

                Dim totalFilesCountText As String = userState("totalFilesCountText")

                If Not totalFilesCountText = Nothing Then
                    Label2.Text = If(lngRus, "Файл: 1 из " & totalFilesCountText, "File: 1 from " & totalFilesCountText)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0180: BgWorker files count calculated: " & totalFilesCountText)
                End If
            Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0190: BgWorker reported wrong progress!")
            End If
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0200: BgWorker reported empty progress!")
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
                PictureBox1.Image?.Dispose()
                PictureBox1.Image = nextImage

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0210: bgWorker: P1 is loaded")
            Else
                PictureBox2.Image?.Dispose()
                PictureBox2.Image = nextImage

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
                TextBox1.Text = currentFolderPath
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
                            currentFileIndex = 1

                            ReadShowMediaFile("ReadFolderAndFile")
                        End If
                    Else
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0265: folder is set from arg, but file wasnt in savings")
                        currentFileIndex = 1

                        ReadShowMediaFile("ReadFolderAndFile")
                    End If
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0270: folder is set from arg")
                    currentFileIndex = 1

                    ReadShowMediaFile("ReadFolderAndFile")
                End If
            Else
                If Not File.Exists(argument) Then
                    'MsgBox(If(lngRus, "Ошибка: файл не существует: " + argument, "Error: File does not exist: " + argument))
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0280: File from arg is not exist: " & argument)
                    Return
                End If

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0290: File is set from arg")
                currentFolderPath = Path.GetDirectoryName(argument)
                targetImagePath = argument
                isTextBoxEdition = True
                TextBox1.Text = currentFolderPath
                isTextBoxEdition = False

                ReadShowMediaFile("ReadFolderAndKnownFile")
            End If

        Catch ex As Exception
            'MsgBox(If(lngRus, "Ошибка при обработке аргумента: " + ex.Message, "Error processing argument: " + ex.Message))
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0300: Error processing argument: " & ex.Message)
            currentFolderPath = ""
            isTextBoxEdition = True
            TextBox1.Text = ""
            isTextBoxEdition = False
        End Try
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        Dim folderBrowse As New FolderBrowserDialog()
        folderBrowse.SelectedPath = currentFolderPath

        folderBrowse.Description = If(lngRus, "Выберите папку с медиафайлами..", "Set folder of media files..")

        If folderBrowse.ShowDialog() = Windows.Forms.DialogResult.OK Then
            currentFolderPath = folderBrowse.SelectedPath
            ReadShowMediaFile("ReadFolderAndFile")
            StatusL.Text = If(lngRus, "выбрана папка", "folder selected")
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
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0320: ReadShowMediaFile = " & readMode.ToString)

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

            Label_SLide.Text = If(SlideShowTimer.Enabled, (SlideShowTimer.Interval / 1000).ToString() & "s", "")

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
            needsUpdate = Not TextBox1.Text = currentFolderPath

            If needsUpdate Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0370: folder combo list is updated")
                recentFolders.Remove(currentFolderPath)
                recentFolders.Add(currentFolderPath)
                If recentFolders.Count > MaxRecentFolders Then
                    recentFolders.RemoveAt(0)
                End If

                If TextBox1.InvokeRequired Then
                    TextBox1.Invoke(Sub()
                                        TextBox1.Items.Clear()
                                        For Each folder In recentFolders
                                            TextBox1.Items.Add(folder)
                                        Next
                                        TextBox1.SelectedIndex = recentFolders.Count - 1
                                    End Sub)
                Else
                    TextBox1.Items.Clear()
                    For Each folder In recentFolders
                        TextBox1.Items.Add(folder)
                    Next
                    TextBox1.SelectedIndex = recentFolders.Count - 1
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

                StatusL.Text = ""

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0420: case ReadNextFile ReadNextFile")

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
                StatusL.Text = If(lngRus, "чтение каталога.. ждите!", "reading files.. wait!")

                If Not LoadFiles() Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0450: case ReadFolderAndFile is failed")
                    Return False
                End If
                StatusL.Text = ""
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
                StatusL.Text = ""

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
                StatusL.Text = ""

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0530: case ReadPrevFile")

            Case "DeleteFile" '3
                If String.IsNullOrEmpty(currentFileName) Then
                    StatusL.Text = If(lngRus, "! Нет файла для удаления", "! No file for deleting")
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0540: case DeleteFile failed")
                    Return False
                End If

                Try
                    If isWebBrowser1Visible Then
                        WebBrowser1.DocumentText = ""
                    Else
                        If isPictureBox1Visible Then
                            PictureBox1.Image = Nothing
                        Else
                            PictureBox2.Image = Nothing
                        End If
                    End If

                    currentLoadedFileName = ""

                    If My.Computer.FileSystem.FileExists(currentFileName) Then
                        If Form2.UseIndependentThreadForOperationsWithFiles.Checked Then
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
                            StatusL.Text = If(lngRus, "удален: ", "file deleted: ") & currentFileName
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
                            StatusL.Text = If(lngRus, "удален: ", "file deleted: ") & currentFileName
                        End If
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0570: case DeleteFile")
                    Else
                        StatusL.Text = If(lngRus, "! Файл не найден", "! File not found")
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
                StatusL.Text = If(lngRus, "чтение каталога.. ждите!", "reading files.. wait!")
                Refresh()
                Dim files As Object = GetFiles()
                If files Is Nothing Then
                    StatusL.Text = If(lngRus, "! Ошибка чтения файлов", "! Error reading files")
                    currentFolderPath = ""
                    TextBox1.Text = ""
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

                StatusL.Text = ""
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
                StatusL.Text = ""
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
            TextBox1.Text = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0710: E002 " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LoadFilesForExternalInput(ByRef isFileFound As Boolean) As Boolean
        Try
            If currentFileIndex = 0 Then
                wasExternalInputLast = False
                StatusL.Text = If(lngRus, "чтение каталога.. ждите!", "reading files.. wait!")
                ' Refresh()
                Dim files As Object = GetFiles()
                If files Is Nothing Then
                    StatusL.Text = If(lngRus, "! Ошибка чтения файлов", "! Error reading files")
                    currentFolderPath = ""
                    TextBox1.Text = ""
                    totalFilesCount = 0
                    currentFileIndex = 0

                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0720: files arnt set")
                    Return False
                End If

                If useArray Then
                    filesArray = DirectCast(files, String())
                Else
                    filesList = DirectCast(files, List(Of String))
                End If

                StatusL.Text = ""
                totalFilesCount = If(useArray, filesArray.Length, filesList.Count)
                currentFileIndex = 0
                If totalFilesCount = 0 Then
                    StatusL.Text = If(lngRus, "! Нет файлов в папке", "! No files in folder")

                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0730: file count is 0")
                    Return False
                Else
                    currentFileIndex = If(useArray, Array.IndexOf(filesArray, targetImagePath), filesList.IndexOf(targetImagePath))
                    isFileFound = currentFileIndex >= 0
                End If

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0740: new folder is read")
            Else
                currentFileIndex += 1
                isFileFound = True

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0750: next one is choosen")
            End If
            Return True

        Catch ex As Exception
            MsgBox("E003 " & ex.Message)
            currentFolderPath = ""
            TextBox1.Text = ""

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0760: E003 " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LoadFiles() As Boolean
        Try
            filesList?.Clear()
            filesArray = Nothing
            Dim files As Object = GetFiles()
            If files Is Nothing Then
                StatusL.Text = If(lngRus, "! Ошибка чтения файлов", "! Error reading files")
                currentFolderPath = ""
                TextBox1.Text = ""
                totalFilesCount = 0
                currentFileIndex = 0
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0770: files arnt set")
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
            MsgBox("E004 " & ex.Message)
            currentFolderPath = ""
            TextBox1.Text = ""
            totalFilesCount = 0
            currentFileIndex = 0
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0800: E004 " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub SetVolume(volume As Double)
        videoVolume = Math.Max(0.0, Math.Min(1.0, volume))
    End Sub

    Private Sub LoadWebImageInWebView2(fileUri As String)
        If Not isWebView2Available Then
            StatusL.Text = If(lngRus, "Формат не поддерживается: WebView2 отсутствует", "Format not supported: WebView2 is missing")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0810: webView is missing")
            Return
        End If

        Try
            isWebView2Loaded = False
            isWebView2Visible = False
            isWebBrowser1Visible = False
            isPictureBox1Visible = False
            isPictureBox2Visible = False
            UpdateControlVisibility()

            webView2Control.Source = New Uri(fileUri)
            currentLoadedFileName = currentFileName
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0820: webView2 is loaded")

        Catch ex As Exception
            StatusL.Text = If(lngRus, "Ошибка загрузки в WebView2: " & ex.Message, "Error loading in WebView2: " & ex.Message)
            If webView2Control IsNot Nothing Then
                webView2Control.Source = New Uri("about:blank")
                webView2Control.Visible = False
            End If
            isWebView2Visible = False
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0830: Error loading in WebView2: " & ex.Message)
            UpdateControlVisibility()
        End Try

        If Not WebBrowser1.DocumentText = "" Then
            WebBrowser1.DocumentText = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0840: WebBrowser is vanished")
        End If
    End Sub

    Private Sub LoadVideoInWebBrowser(fileUri As String)
        Try
            isWebBrowserLoaded = False
            isWebBrowser1Visible = False
            isWebView2Visible = False
            isPictureBox1Visible = False
            isPictureBox2Visible = False
            UpdateControlVisibility()

            Dim escapedUri As String = Uri.EscapeUriString(fileUri)
            Dim contentHtml As String = "<video id='videoPlayer' controls autoplay style='width:100%;height:100%;object-fit:fill;'>" &
                            "<source src='" & escapedUri & "'>" &
                            If(lngRus, "Ваш браузер не поддерживает видео.", "Your browser does not support video.") &
                            "</video>"
            Dim html As String = "<html><head><meta http-equiv='X-UA-Compatible' content='IE=edge'>" &
                         "<style>" &
                         "body { margin: 0; overflow: hidden; background: black; }" &
                         "video { width: 100%; height: 100%; object-fit: fill; position: absolute; top: 0; left: 0; }" &
                         "</style></head>" &
                         "<body oncontextmenu='return false;'>" & contentHtml &
                         "<script>" &
                         "var player = document.getElementById('videoPlayer');" &
                         "player.volume = " & videoVolume.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) & ";" &
                         "player.oncontextmenu = function(e) { e.preventDefault(); if (this.paused) this.play(); else this.pause(); return false; };" &
                         "player.onvolumechange = function() { try { window.external.SetVolume(this.volume); } catch(e) { } };" &
                         "</script></body></html>"

            lastLoadedUri = ""
            WebBrowser1.DocumentText = html
            currentLoadedFileName = currentFileName

            isWebBrowser1Visible = True
            UpdateControlVisibility()
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0850: WebBrowser is loaded")
        Catch ex As Exception
            WebBrowser1.DocumentText = ""
            isWebBrowser1Visible = False
            UpdateControlVisibility()
            StatusL.Text = If(lngRus, "Ошибка загрузки видео: " & ex.Message, "Error loading video: " & ex.Message)
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
                If isSecondaryPictureBoxActive Then
                    isPictureBox2Visible = True
                    isPictureBox1Visible = False
                    bgWorkerResult = "USED P2"
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0870: P2 is found already loaded")
                Else
                    isPictureBox2Visible = False
                    isPictureBox1Visible = True
                    bgWorkerResult = "USED P1"
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0880: P1 is found already loaded")
                End If
            Else
                Dim newImage As Image
                Try
                    Using stream As New FileStream(currentFileName, FileMode.Open, FileAccess.Read, FileShare.Read)
                        newImage = Image.FromStream(stream)
                    End Using

                    If isSecondaryPictureBoxActive Then
                        PictureBox2.Image?.Dispose()
                        PictureBox2.Image = newImage
                        isPictureBox2Visible = True
                        isPictureBox1Visible = False

                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0890: P2 set (not found loaded)")
                    Else
                        PictureBox1.Image?.Dispose()
                        PictureBox1.Image = newImage
                        isPictureBox1Visible = True
                        isPictureBox2Visible = False

                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0900: P1 set (not found loaded)")
                    End If
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0905: Error loading image: " & ex.Message)
                    Return
                End Try
            End If
            currentLoadedFileName = currentFileName
            isFirstPictureBoxNeedToBeCached = isSecondaryPictureBoxActive
            isSecondaryPictureBoxActive = Not isSecondaryPictureBoxActive

            UpdateControlVisibility()
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0910: P1.Visible: " & PictureBox1.Visible.ToString & ", P2.Visible: " & PictureBox2.Visible.ToString)
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0920: file is a same, pic set is skipped")
        End If

        If webView2Control IsNot Nothing AndAlso isWebView2Visible Then
            webView2Control.Source = New Uri("about:blank")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0930: WV2 blank")
        End If

        If Not WebBrowser1.DocumentText = "" Then
            WebBrowser1.DocumentText = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0940: WB blank")
        End If

    End Sub

    Private Function GetOppositeColor(backgroundColor As System.Drawing.Color) As System.Drawing.Color
        If backgroundColor.R = 128 AndAlso backgroundColor.G = 128 AndAlso backgroundColor.B = 128 Then
            Return System.Drawing.Color.Black
        End If
        Return System.Drawing.Color.FromArgb(255 - backgroundColor.R, 255 - backgroundColor.G, 255 - backgroundColor.B)
    End Function

    Private Sub UpdateControlVisibility()

        PictureBox1.Visible = isPictureBox1Visible
        PictureBox2.Visible = isPictureBox2Visible
        WebBrowser1.Visible = isWebBrowser1Visible
        If webView2Control IsNot Nothing Then
            webView2Control.Visible = isWebView2Visible
        End If

        If isPictureBox1Visible OrElse isPictureBox2Visible Then
            WebBrowser1.Visible = False
            If webView2Control IsNot Nothing Then
                webView2Control.Visible = False
            End If

            Dim bmp As Bitmap = Nothing

            If isPictureBox1Visible OrElse isPictureBox2Visible Then
                If isPictureBox1Visible AndAlso PictureBox1.Image IsNot Nothing AndAlso TypeOf PictureBox1.Image Is Bitmap Then
                    bmp = CType(PictureBox1.Image, Bitmap)
                Else
                    If isPictureBox2Visible AndAlso PictureBox2.Image IsNot Nothing AndAlso TypeOf PictureBox2.Image Is Bitmap Then
                        bmp = CType(PictureBox2.Image, Bitmap)
                    End If
                End If

                If bmp IsNot Nothing Then
                    If 1 < bmp.Width AndAlso 1 < bmp.Height Then
                        Dim pixelColor As System.Drawing.Color = bmp.GetPixel(1, 1)
                        Me.BackColor = pixelColor

                        Dim OppositeColor = GetOppositeColor(Me.BackColor)
                        For Each ctrl As Control In Me.Controls
                            If TypeOf ctrl Is Label Then
                                Dim lbl As Label = CType(ctrl, Label)
                                lbl.ForeColor = OppositeColor
                            ElseIf TypeOf ctrl Is Button Then
                                Dim btn As Button = CType(ctrl, Button)
                                btn.ForeColor = OppositeColor
                            End If
                        Next
                    End If
                End If
            End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0945: picture box sizes: " & If(isPictureBox1Visible, "P1: ", "P2: ") & If(isPictureBox1Visible, PictureBox1.Width.ToString, PictureBox2.Width.ToString) & "x" & If(isPictureBox1Visible, PictureBox1.Height.ToString, PictureBox2.Height.ToString))
                End If

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0950: Visibility set: " & If(isPictureBox1Visible, "P1-YES ", "P1-NO ") & If(isPictureBox2Visible, "P2-YES ", "P2-NO ") & If(isWebBrowser1Visible, "WB-YES ", "WB-NO ") & If(isWebView2Visible, "WV2-YES", "WV2-NO "))
    End Sub

    Private Sub UpdateCurrentFileAndDisplay(isFileFound As Boolean, isAfterUndo As Boolean)
        'Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w233: totalFilesCount = " & totalFilesCount.ToString)

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
            Label2.Text = If(lngRus, "Файл: " & fileIndexDisplay.ToString() & " из " & totalFilesCount.ToString(), "File: " & fileIndexDisplay.ToString() & " from " & totalFilesCount.ToString())

            Try
                Dim fileExtension As String = Path.GetExtension(currentFileName).ToLower()
                Dim fileUri As String = New Uri(currentFileName).ToString()

                If isWebView2Available AndAlso webView2Control IsNot Nothing AndAlso webImageFileExtensions.Contains(fileExtension) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0990: WV2 to load")
                    LoadWebImageInWebView2(fileUri)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1000: WV2 is set")
                ElseIf videoFileExtensions.Contains(fileExtension) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1010: WB to load")
                    LoadVideoInWebBrowser(fileUri)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1020: WB is set")
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1030: P to load")
                    LoadStandardImageInPictureBox()
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1040: Picture box is set")
                End If

                If isSlideShowRandom OrElse isFileReseivedFromOutside Then
                    nextAfterCurrentFileName = ""
                    isFileReseivedFromOutside = False
                Else
                    nextAfterCurrentFileName = If(totalFilesCount > 0, If(totalFilesCount = currentFileIndex + 1, If(useArray, filesArray(0), filesList(0)), If(useArray, filesArray(currentFileIndex + 1), filesList(currentFileIndex + 1))), "")
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
                    StatusL.Text = If(lngRus, "Файл " & currentFileName & " перемещается назад операционной системой.", "File " & currentFileName & " moving back by OS.")
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1080: UNdo E005 " & ex.Message)
                End If
            End Try
        Else
            PictureBox1.Image = Nothing
            PictureBox2.Image = Nothing
            currentLoadedFileName = ""
            WebBrowser1.DocumentText = ""
            If webView2Control IsNot Nothing Then
                webView2Control.Source = New Uri("about:blank")
            End If
            Label2.Text = ""
            StatusL.Text = If(lngRus, "! Нет файлов в папке", "! No files in folder")
            isPictureBox1Visible = False
            isPictureBox2Visible = False
            isWebBrowser1Visible = False
            isWebView2Visible = False
            UpdateControlVisibility()

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1090: No files in folder, all wiped")
        End If
    End Sub

    Private Function GetFiles() As Object
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1095: GetFiles..")

        Dim fileCountEstimate As Integer
        Try
            fileCountEstimate = Directory.GetFiles(currentFolderPath, "*.*", IO.SearchOption.TopDirectoryOnly).Length
        Catch ex As Exception
            fileCountEstimate = 0
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1100: Files ARNT got: " & ex.Message)
            Return Nothing
        End Try

        useArray = fileCountEstimate > MaxFiles
        Dim filteredFiles As New List(Of String)(Math.Min(fileCountEstimate, If(useArray, 1000000, MaxFiles)))

        Try
            Dim fileCount As Integer = 0
            For Each file In Directory.EnumerateFiles(currentFolderPath, "*.*", IO.SearchOption.TopDirectoryOnly)
                fileCount += 1
                If useArray OrElse fileCount <= MaxFiles Then
                    Dim extension As String = Path.GetExtension(file).ToLower()
                    If allSupportedExtensions.Contains(extension) Then
                        filteredFiles.Add(file)
                    End If
                End If
                If Not useArray AndAlso fileCount > MaxFiles Then
                    Exit For
                End If
            Next

            Select Case SortComboBox.SelectedIndex
                Case 0 'abc
                    filteredFiles.Sort()
                Case 1 'xyz
                    filteredFiles.Sort()
                    filteredFiles.Reverse()
                Case 2 'rnd
                    Dim rnd As New Random()
                    filteredFiles = filteredFiles.OrderBy(Function(x) rnd.Next()).ToList()
                Case 3 '>size
                    filteredFiles.Sort(Function(x, y) My.Computer.FileSystem.GetFileInfo(y).Length.CompareTo(My.Computer.FileSystem.GetFileInfo(x).Length))
                Case 4 '<size
                    filteredFiles.Sort(Function(x, y) My.Computer.FileSystem.GetFileInfo(x).Length.CompareTo(My.Computer.FileSystem.GetFileInfo(y).Length))
            End Select

            If useArray Then
                Return filteredFiles.ToArray()
            Else
                Return filteredFiles
            End If

        Catch ex As Exception
            StatusL.Text = If(lngRus, "! Ошибка чтения файлов", "! Error reading files")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1110: Error reading files: " & ex.Message)
            Return Nothing
        End Try

    End Function

    Private Sub ISizeChanged()
        If isFullScreen <> lastFullScreenState Then
            If isFullScreen Then
                FormBorderStyle = FormBorderStyle.None
                WindowState = FormWindowState.Maximized
                Buttons_to_fullscreen()
            Else
                FormBorderStyle = FormBorderStyle.Sizable
                WindowState = FormWindowState.Normal
                Buttons_to_normal()
            End If
            lastFullScreenState = isFullScreen
        End If

        If Not isFullScreen Then
            Buttons_to_normal()
        End If

        WebBrowser1.Size = PictureBox1.Size
        WebBrowser1.Location = PictureBox1.Location

        If webView2Control IsNot Nothing Then
            webView2Control.Size = PictureBox1.Size
            webView2Control.Location = PictureBox1.Location
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1935: ISizeChanged")
    End Sub
    Private Sub SetViewSizes()
        ISizeChanged()
    End Sub

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        Debug.WriteLine(" - - - ")
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1115: Form started")

        isTextBoxEdition = True
        applicationRunsCount = 0
        Integer.TryParse(GetSetting(appName, secName, "RunsCount", "0"), applicationRunsCount)
        lngRus = GetSetting(appName, secName, "LngRus", "1") = "1"

        SortComboBox.SelectedIndex = 0
        ButtonLNG.Text = If(lngRus, "EN", "RU")
        LngCh()

        Dim isFirstRunStriing As String = GetSetting(appName, secName, "FirstRun", "1")
        FirstRun.Visible = isFirstRunStriing = "1"

        copyMode = GetSetting(appName, secName, "CopyMode", "0") = "1"
        chkTopMost.Checked = GetSetting(appName, secName, "chkTopMost", "0") = "1"
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
                If TextBox1.Items.Count < MaxRecentFolders Then
                    TextBox1.Items.Add(folder)
                End If
            Next
        End If

        If Form2.UseIndependentThreadForOperationsWithFiles IsNot Nothing Then
            Form2.UseIndependentThreadForOperationsWithFiles.Checked = GetSetting(appName, secName, "UseIndependentThreadForOperationsWithFiles", "0") = "1"
        End If

        If My.Application.CommandLineArgs.Count > 0 Then
            ProcessArgument(My.Application.CommandLineArgs(0))
        Else
            currentFolderPath = GetSetting(appName, secName, "ImageFolder", "")
            If Not currentFolderPath = "" Then
                totalFilesCount = FileSystem.GetDirectoryInfo(currentFolderPath).EnumerateFiles.Count

                Integer.TryParse(GetSetting(appName, secName, "LastCounter"), currentFileIndex)
                If currentFileIndex > 0 AndAlso currentFileIndex < totalFilesCount Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1156: folder and file found in savings")

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
            Form2.Show()
        End If
        isTextBoxEdition = False

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1120: Form Loaded")
    End Sub

    Private Sub Form1_Resize(sender As Object, e As EventArgs) Handles Me.Resize
        ResizeDebounceTimer.Stop()
        ResizeDebounceTimer.Start()
    End Sub

    Private Sub ResizeDebounceTimer_Tick(sender As Object, e As EventArgs) Handles ResizeDebounceTimer.Tick
        ResizeDebounceTimer.Stop()
        ISizeChanged()
    End Sub

    Private Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
        SlideShowTimer.Enabled = False
        ReadShowMediaFile("ReadPrevFile")
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1130: Button2_Click ReadPrevFile")
    End Sub

    Private Sub Button3_Click(sender As Object, e As EventArgs) Handles Button3.Click
        SlideShowTimer.Enabled = False
        ReadShowMediaFile("ReadNextFile")
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1140: Button3_Click ReadNextFile")
    End Sub

    Private Sub PictureBox1_MouseDown(sender As Object, e As MouseEventArgs) Handles PictureBox1.MouseDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1150: PictureBox1_MouseDown")
        MouseUse(e)
    End Sub

    Private Sub PictureBox2_MouseDown(sender As Object, e As MouseEventArgs) Handles PictureBox2.MouseDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1160: PictureBox2_MouseDown")
        MouseUse(e)
    End Sub


    Private Sub MouseUse(ByVal e As MouseEventArgs)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1170: MouseUse, Delta: " & e.Delta.ToString)

        SlideShowTimer.Enabled = False

        If (Control.ModifierKeys And Keys.Alt) = Keys.Alt Then

            Dim isTop = StatusL.Top + StatusL.Height - 4

            PictureBox1.Top = isTop
            PictureBox1.Left = 2
            PictureBox1.Width = Me.Width - 4
            PictureBox1.Height = Me.Height - isTop - 4

            PictureBox2.Size = PictureBox1.Size
            PictureBox2.Location = PictureBox1.Location

        ElseIf e.Delta <> 0 AndAlso (isPictureBox1Visible OrElse isPictureBox2Visible) AndAlso (Control.ModifierKeys And Keys.Control) = Keys.Control Then
            Dim ratio As Integer = CInt(PictureBox1.Size.Width * 100 / Me.Width)
            Dim oldWidth As Integer = PictureBox1.Width
            Dim oldHeight As Integer = PictureBox1.Height
            Dim zoomFactor As Double = If(e.Delta > 0, If(ratio <= 1000, 1.1, 1), If(ratio >= 100, 0.9, 1))
            Dim newWidth As Integer = CInt(oldWidth * zoomFactor)
            Dim newHeight As Integer = CInt(oldHeight * zoomFactor)

            Dim clickX As Integer = e.X
            Dim clickY As Integer = e.Y

            PictureBox1.Size = New Size(CInt(PictureBox1.Width * 0.9), CInt(PictureBox1.Height * 0.9))

            Dim newLeft As Integer = PictureBox1.Left - CInt((clickX * zoomFactor) - clickX)
            Dim newTop As Integer = PictureBox1.Top - CInt((clickY * zoomFactor) - clickY)

            PictureBox1.Size = New Size(newWidth, newHeight)
            PictureBox1.Location = New Point(newLeft, newTop)

            PictureBox2.Size = PictureBox1.Size
            PictureBox2.Location = PictureBox1.Location

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
            SaveSetting(appName, secName, "chkTopMost", If(chkTopMost.Checked, "1", "0"))
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
            SaveSetting(appName, secName, "TableOpened", If(Form2.Visible, "1", "0"))
            SaveSetting(appName, secName, "RunsCount", (applicationRunsCount + 1).ToString)

            If Me.Top >= 0 Then SaveSetting(appName, secName, "AppTop", Me.Top.ToString)
            If Me.Left >= 0 Then SaveSetting(appName, secName, "AppLeft", Me.Left.ToString)
            If Me.Height >= 200 Then SaveSetting(appName, secName, "AppHeight", Me.Height.ToString)
            If Me.Width >= 320 Then SaveSetting(appName, secName, "AppWidth", Me.Width.ToString)

            SaveSetting(appName, secName, "VideoVolume", videoVolume.ToString("F2"))

            Dim recentFoldersString As String = String.Join("|", recentFolders)
            SaveSetting(appName, secName, "RecentFolders", recentFoldersString)

            SaveSetting(appName, secName, "UseIndependentThreadForOperationsWithFiles", If(Form2.UseIndependentThreadForOperationsWithFiles.Checked, "1", "0"))

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

        If webView2Control IsNot Nothing Then
            RemoveHandler webView2Control.NavigationCompleted, AddressOf WebView2_NavigationCompleted
            webView2Control.Dispose()
            webView2Control = Nothing
        End If
        If WebBrowser1 IsNot Nothing Then
            RemoveHandler WebBrowser1.DocumentCompleted, AddressOf WebBrowser1_DocumentCompleted
            WebBrowser1.DocumentText = ""
            WebBrowser1.Dispose()
        End If

        PictureBox1.Image = Nothing
        PictureBox2.Image = Nothing

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1230: form is closed")
    End Sub

    Private Sub Form1_KeyDown(sender As Object, e As KeyEventArgs) Handles MyBase.KeyDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1240: keyb: " & e.KeyCode.ToString)
        KeybUse(e)
    End Sub

    Private Sub RenameCurrentFile()
        Try
            If Not My.Computer.FileSystem.FileExists(currentFileName) Then
                StatusL.Text = If(lngRus, "! Файл не найден", "! File not found")
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
                StatusL.Text = If(lngRus, "! Имя не изменено", "! Name not changed")
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
            StatusL.Text = If(lngRus, "Файл переименован: " & newFileName & fileExtension, "File renamed: " & newFileName & fileExtension)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1280: file is renamed")
            ReadShowMediaFile("SetFile")
        Catch ex As Exception
            MsgBox("E011 " & ex.Message)
            StatusL.Text = If(lngRus, "! Ошибка переименования", "! Rename error")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1290: ERR: " & ex.Message)
        End Try
    End Sub

    Public Sub KeybUse(e As KeyEventArgs)
        SlideShowTimer.Enabled = False
        If Me.TextBox1.Focused Then
            If e.KeyCode = Keys.Enter AndAlso TextBox1.Text <> "" Then
                currentFolderPath = TextBox1.Text
                ReadShowMediaFile("ReadFolderAndFile")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1300: Enter pressed")
            End If
            Exit Sub
        End If

        Select Case e.KeyCode
            Case Keys.N, Keys.Next, Keys.PageDown, Keys.Space, Keys.Right, Keys.BrowserForward
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1310: to next file")
                ReadShowMediaFile("ReadNextFile")
            Case Keys.P, Keys.PageUp, Keys.Left, Keys.B, Keys.BrowserBack
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1320: to prev file")
                ReadShowMediaFile("ReadPrevFile")
            Case Keys.Y
                ReadShowMediaFile("ReadForRandom")
            Case Keys.U
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
                    StatusL.Text = If(lngRus, "! Нет файла для переименования", "! No file to rename")
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1350: No file to rename")
                End If
            Case Keys.I
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
                ReadShowMediaFile("ReadForDlideShow")
            Case Keys.Home, Keys.H, Keys.BrowserHome
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1370: to first file")
                currentFileIndex = 0
                ReadShowMediaFile("SetFile")
                StatusL.Text = If(lngRus, "первый файл", "first file")
            Case Keys.End, Keys.E, Keys.L, Keys.BrowserStop
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1380: to last file")
                currentFileIndex = totalFilesCount
                ReadShowMediaFile("SetFile")
                StatusL.Text = If(lngRus, "последний файл", "last file")
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
                        PictureBox2.Image.RotateFlip(RotateFlipType.Rotate90FlipX)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1510: P2 Rotated")
                    Else
                        PictureBox1.Image.RotateFlip(RotateFlipType.Rotate90FlipX)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1520: P1 Rotated")
                    End If
                Catch ex As Exception
                    MsgBox("E012 " & ex.Message)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1530: ERR: " & ex.Message)
                End Try
            Case Keys.T
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1540: Rev Rotate")
                Try
                    If isSecondaryPictureBoxActive Then
                        PictureBox2.Image.RotateFlip(RotateFlipType.Rotate270FlipX)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1550: P2 Rev Rotated")
                    Else
                        PictureBox1.Image.RotateFlip(RotateFlipType.Rotate270FlipX)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1560: P1 Rev Rotated")
                    End If
                Catch ex As Exception
                    MsgBox("E013 " & ex.Message)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1570: ERR: " & ex.Message)
                End Try
            Case Keys.Up
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1580: -10")
                currentFileIndex = currentFileIndex - 10
                ReadShowMediaFile("SetFile")
                StatusL.Text = If(lngRus, "-10 файлов", "-10 files")
            Case Keys.Down
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1590: +10")
                currentFileIndex = currentFileIndex + 10
                ReadShowMediaFile("SetFile")
                StatusL.Text = If(lngRus, "+10 файлов", "+10 files")
            Case Keys.Enter
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1600: +100")
                currentFileIndex = currentFileIndex + 100
                ReadShowMediaFile("SetFile")
                StatusL.Text = If(lngRus, "+100 файлов", "+100 files")
            Case Keys.Back
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1610: -100")
                currentFileIndex = currentFileIndex - 100
                ReadShowMediaFile("SetFile")
                StatusL.Text = If(lngRus, "-100 файлов", "-100 files")
            Case Keys.F1
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1620: F1 help")
                FirstRun.Visible = True
                FirstRun.Show()
            Case Keys.F2
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1630: F2")
                Form2.Show()
            Case Keys.U
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1640: UnDo")
                Undo()
        End Select
    End Sub

    Private Sub Button4_Click(sender As Object, e As EventArgs) Handles Button4.Click
        SlideShowTimer.Enabled = False
        ReadShowMediaFile("DeleteFile")
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1650: Button4_Click delete")
    End Sub

    Private Sub Form1_MouseDown(sender As Object, e As MouseEventArgs) Handles MyBase.MouseDown
        MouseUse(e)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1660: Form1_MouseDown")
    End Sub

    Private Sub Form1_MouseWheel(sender As Object, e As MouseEventArgs) Handles Me.MouseWheel
        MouseUse(e)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1670: Form1_MouseWheel")
    End Sub

    Private Sub PoMove(ByVal moveSec As Integer)
        Dim moveTo As String = moveOnKey(moveSec)
        Dim textKey As String = moveSec.ToString
        If textKey = "10" Then textKey = "0"
        If moveTo = "" Then
            StatusL.Text = If(lngRus, "! Нет каталога-получателя для клавиши " & textKey, "! No dest folder set with key " & textKey)
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

                    If Form2.UseIndependentThreadForOperationsWithFiles.Checked Then
                        If copyMode Then
                            currentFileOperation = "Copy"
                            currentFileOperationArgs = New String() {currentFileName, newDest, textKey}
                            StatusL.Text = If(lngRus, "!Ждите.. Файл копируется (" & textKey & ") в каталог " & newDest, "!Wait.. File copying (" & textKey & ") to " & newDest)
                            FileOperationWorker.RunWorkerAsync()

                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1690: copy async run")

                            ReadShowMediaFile("ReadNextFile")
                        Else
                            currentFileOperation = "Move"
                            currentFileOperationArgs = New String() {currentFileName, newDest, textKey}
                            PictureBox1.Image = Nothing
                            PictureBox2.Image = Nothing
                            WebBrowser1.DocumentText = ""
                            If webView2Control IsNot Nothing Then
                                webView2Control.Source = New Uri("about:blank")
                            End If
                            StatusL.Text = If(lngRus, "!Ждите.. Файл переносится (" & textKey & ") в каталог " & newDest, "!Wait.. File moving (" & textKey & ") to " & newDest)

                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1700: move async run")

                            FileOperationWorker.RunWorkerAsync()
                            If useArray Then
                                filesArray = RemoveAt(filesArray, currentFileIndex)
                            Else
                                filesList.RemoveAt(currentFileIndex)
                            End If
                            totalFilesCount -= 1
                            If currentFileIndex > (totalFilesCount - 1) Then currentFileIndex = totalFilesCount - 1

                            ReadShowMediaFile("SetFile")
                        End If
                    Else
                        If copyMode Then
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1710: copy run")

                            StatusL.Text = If(lngRus, "!Ждите.. Файл копируется (" & textKey & ") в каталог " & newDest, "!Wait.. File copying (" & textKey & ") to " & newDest)
                            Me.Refresh()
                            My.Computer.FileSystem.CopyFile(currentFileName, newDest)
                            StatusL.Text = If(lngRus, "файл скопирован (" & textKey & ") в каталог " & newDest, "file copied (" & textKey & ") to " & newDest)
                            ReadShowMediaFile("ReadNextFile")
                        Else
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1720: move run")

                            PictureBox1.Image = Nothing
                            PictureBox2.Image = Nothing
                            WebBrowser1.DocumentText = ""
                            If webView2Control IsNot Nothing Then
                                webView2Control.Source = New Uri("about:blank")
                            End If
                            StatusL.Text = If(lngRus, "!Ждите.. Файл переносится (" & textKey & ") в каталог " & newDest, "!Wait.. File moving (" & textKey & ") to " & newDest)
                            Me.Refresh()
                            My.Computer.FileSystem.MoveFile(currentFileName, newDest)
                            If useArray Then
                                filesArray = RemoveAt(filesArray, currentFileIndex)
                            Else
                                filesList.RemoveAt(currentFileIndex)
                            End If
                            totalFilesCount -= 1
                            If currentFileIndex > (totalFilesCount - 1) Then currentFileIndex = totalFilesCount - 1
                            StatusL.Text = If(lngRus, "файл перенесен (" & textKey & ") в каталог " & newDest, "file moved (" & textKey & ") to " & newDest)
                            ReadShowMediaFile("SetFile")
                        End If
                    End If
                Catch ex As Exception
                    MsgBox("E014 " & ex.Message)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1730: E014 " & ex.Message)
                End Try
            Else
                StatusL.Text = If(lngRus, "! Нет файла ", "! No file")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1740: No file")
            End If
        End If
    End Sub

    Private Sub Undo()
        If historyDestinationFileName <> "" Then
            If copyMode Then
                If Form2.UseIndependentThreadForOperationsWithFiles.Checked Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1750: undo copied async deletion")

                    currentFileOperation = "DeleteUndo"
                    currentFileOperationArgs = historyDestinationFileName
                    StatusL.Text = If(lngRus, "!Ждите. Файл удаляется в каталоге " & historyDestinationFileName, "!Wait. File deleting in " & historyDestinationFileName)
                    FileOperationWorker.RunWorkerAsync()
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1760: undo copied deletion")
                    Try
                        My.Computer.FileSystem.DeleteFile(historyDestinationFileName)
                        StatusL.Text = If(lngRus, "файл удален в каталоге " & historyDestinationFileName, "file deleted in " & historyDestinationFileName)
                        historyDestinationFileName = ""
                        historySourceFileName = ""
                    Catch ex As Exception
                        MsgBox("E015 " & ex.Message)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1770: undo E015 " & ex.Message)
                    End Try
                End If
            Else
                If Form2.UseIndependentThreadForOperationsWithFiles.Checked Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1780: undo move async deletion")

                    currentFileOperation = "MoveUndo"
                    currentFileOperationArgs = New String() {historyDestinationFileName, historySourceFileName}
                    StatusL.Text = If(lngRus, "!Ждите. Возвращается в каталог " & historySourceFileName, "!Wait. File back to " & historySourceFileName)
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
                        StatusL.Text = If(lngRus, "!Ждите. Возвращается в каталог " & historySourceFileName, "!Wait. File back to " & historySourceFileName)
                        Me.Refresh()
                        My.Computer.FileSystem.MoveFile(historyDestinationFileName, historySourceFileName)
                        If useArray Then
                            filesArray = AddAt(filesArray, historySourceFileName, currentFileIndex)
                        Else
                            filesList.Insert(currentFileIndex, historySourceFileName)
                        End If
                        totalFilesCount += 1
                        StatusL.Text = If(lngRus, "файл возвращен в каталог " & historySourceFileName, "file back to " & historySourceFileName)
                        ReadShowMediaFile("AfterUndo")
                        historyDestinationFileName = ""
                        historySourceFileName = ""
                    Catch ex As Exception
                        MsgBox("E016 " & ex.Message)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1800: undo E016 " & ex.Message)
                    End Try
                End If
            End If
            WebBrowser1.DocumentText = ""
            If webView2Control IsNot Nothing Then
                webView2Control.Source = New Uri("about:blank")
            End If
        Else
            StatusL.Text = If(lngRus, "! Нет истории о переносе", "! No history about moved files")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1810: No history about moved files")
        End If
    End Sub

    Private Sub FirstRun_Click(sender As Object, e As EventArgs) Handles FirstRun.Click
        FirstRun.Visible = False
        FirstRun.Hide()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1820: FirstRun clicked and hidden")
    End Sub

    Private Sub LngCh()
        StatusL.Text = ""
        If lngRus Then
            Label1.Text = "Каталог:"
            Button2.Text = "<< пред(PgUp)"
            Button3.Text = "след(PgDn) >>"
            Button4.Text = "удалить (del)"
            Button5.Text = "таблица получателей"
            FirstRun.Text = " Програма для быстрого переноса/копирования изображений по папкам." & Chr(10) & Chr(10) &
                "Сначала заполните таблицу каталогов-получателей по клавишам 1,2,3.. - 0. " & Chr(10) &
                "Затем укажите каталог-источник для сортировки. " & Chr(10) &
                "Продвигайтесь по файлам с помощью P/N, PgDn/PgUp или скролла мыши. " & Chr(10) &
                "Y - случайно, U - случ слайдшоу, I - слайдшоу. " & Chr(10) &
                "R/T для поворота картинки. " & Chr(10) &
                "F6 для переименования файла. " & Chr(10) &
                "Или за счет переноса/копирования по папкам клавишами (1,2,3.. - 0). " & Chr(10) &
                "Или за счет удаления текущего файла (del). " & Chr(10) &
                "Окно таблицы можно закрепить и щелкать мышью по колонке с цифрой. " & Chr(10) &
                "(U) -вернуть последный перенесенный файл (удалить скопированный). " & Chr(10) & Chr(10) &
                " Щелкните на этот текст (F1) для того, чтобы он исчез."
            ButtonLNG.Text = "EN"

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1830: Russian is set")
        Else
            ButtonLNG.Text = "RU"
            lngRus = False
            Label1.Text = "Folder:"
            Button2.Text = "<< (P)rev"
            Button3.Text = "(N)ext >>"
            Button4.Text = "(D)elete"
            Button5.Text = "dest folders table"
            FirstRun.Text = " Program for fast image sorting." & Chr(10) & Chr(10) &
                "First fill dest folders table for keys: 1,2.. - 0. " & Chr(10) &
                "After set folder with you unsorted files. " & Chr(10) &
                "Go with files by P/N, PgDn/PgUp keys or mouse scroll. " & Chr(10) &
                "Y - random, U - random slide, I - slide. " & Chr(10) &
                "Or move/copy files into dest folders by keys (1,2.. - 0). " & Chr(10) &
                "Or by deleting files (del key). " & Chr(10) &
                "R/T to rotate the image. " & Chr(10) &
                "F6 to rename the file. " & Chr(10) &
                "You can lock Window with folders table and click on key numbers. " & Chr(10) &
                "(U)ndo last moved action (delete copying file). " & Chr(10) & Chr(10) &
                " Click on this text (F1) for hide it."

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1840: English is set")
        End If
    End Sub

    Private Sub butI_Click(sender As Object, e As EventArgs) Handles butI.Click
        FolderSelected()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1850: butI clicked")
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
        If keyIndex = 0 Then
            ReadShowMediaFile("DeleteFile")
        Else
            PoMove(keyIndex + 1)
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1860: DoKey")
    End Sub

    Private Sub TextBox1_KeyPress(sender As Object, e As KeyPressEventArgs)
        If e.KeyChar = Convert.ToChar(Keys.Enter) Then
            currentFolderPath = Me.TextBox1.Text
            FolderSelected()

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1870: Enter pressed in TextBox1")
        End If
    End Sub

    Private Sub Button6_Click(sender As Object, e As EventArgs) Handles Button6.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1880: Button6_Click")

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
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1890: SlideShow_Elapsed")
        ReadShowMediaFile("ReadForSlideShow")
    End Sub

    Private Sub Button7_Click(sender As Object, e As EventArgs) Handles Button7.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1900: Button7_Click (to fullscreen)")

        isFullScreen = True
        Buttons_to_fullscreen()
        SetViewSizes()

    End Sub

    Private Sub Buttons_to_fullscreen()

        PictureBox1.Top = 0
        PictureBox1.Left = 0
        PictureBox1.Width = Me.Width
        PictureBox1.Height = Me.Height

        PictureBox2.Size = PictureBox1.Size
        PictureBox2.Location = PictureBox1.Location

        With Button1
            .Top = 2
            .Left = 2
            .Width = 30
            .Height = 20
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With SortComboBox
            .Visible = False
        End With
        With butI
            .Top = 2
            .Left = Button1.Left + Button1.Width + 10
            .Width = 30
            .Height = 20
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With Button2
            .Top = 2
            .Left = butI.Left + butI.Width + 20
            .Width = 40
            .Height = 20
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With Button3
            .Top = 2
            .Left = Button2.Left + Button2.Width + 2
            .Width = 40
            .Height = 20
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With Button8
            .Top = 2
            .Left = Button3.Left + Button3.Width + 2
            .Width = 40
            .Height = 20
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With Button9
            .Top = 2
            .Left = Button8.Left + Button8.Width + 20
            .Width = 40
            .Height = 20
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With Button6
            .Top = 2
            .Left = Button9.Left + Button9.Width + 2
            .Width = 40
            .Height = 20
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With Button5
            .Top = Button1.Top + Button6.Height + 30
            .Left = 2
            .Width = 40
            .Height = 20
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With ButtonRename
            .Top = Button5.Top + Button5.Height + 30
            .Left = 2
            .Width = 30
            .Height = 20
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With
        With Button4
            .Top = ButtonRename.Top + ButtonRename.Height + 30
            .Left = 2
            .Width = 30
            .Height = 20
            .Font = New Font("Arial", 6, FontStyle.Regular)
            .Visible = True
        End With

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1910: buttons resaized to full screeen")
    End Sub

    Private Sub Buttons_to_normal()

        Dim isTop = StatusL.Top + StatusL.Height - 4

        If Not PictureBox1.Top = isTop OrElse Not PictureBox1.Width = Me.Width - 4 OrElse Not PictureBox1.Height = Me.Height - isTop - 4 Then

            PictureBox1.Top = isTop
            PictureBox1.Left = 2
            PictureBox1.Width = Me.Width - 4
            PictureBox1.Height = Me.Height - isTop - 4

            PictureBox2.Size = PictureBox1.Size
            PictureBox2.Location = PictureBox1.Location

            With Button1
                .Top = 2
                .Left = TextBox1.Width + TextBox1.Left + 2
                .Width = 30
                .Height = Label2.Top - 4
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With SortComboBox
                .Top = 2
                .Left = 2
                .Width = 40
                .Height = TextBox1.Height
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With butI
                .Top = 2
                .Left = Button1.Left + Button1.Width + 10
                .Width = Button1.Width
                .Height = Button1.Height
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With Button2
                .Top = Label2.Top
                .Left = Label2.Left + Label2.Width + 20
                .Width = 100
                .Height = Label3.Top - Label2.Top - 4
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With Button3
                .Top = Button2.Top
                .Left = Button2.Left + Button2.Width + 2
                .Width = 100
                .Height = Button2.Height
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With Button8
                .Top = Button3.Top
                .Left = Button3.Left + Button3.Width + 2
                .Width = 50
                .Height = Button3.Height
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With Button9
                .Top = Button8.Top
                .Left = Button8.Left + Button8.Width + 20
                .Width = 50
                .Height = Button8.Height
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With Button6
                .Top = Button9.Top
                .Left = Button9.Left + Button9.Width + 2
                .Width = 50
                .Height = Button9.Height
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With Button5
                .Top = Button6.Top
                .Left = Button6.Left + Button6.Width + 20
                .Width = 100
                .Height = Button6.Height
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With ButtonRename
                .Top = Button5.Top
                .Left = Button5.Left + Button5.Width + 20
                .Width = 50
                .Height = Button5.Height
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With
            With Button4
                .Top = ButtonRename.Top
                .Left = ButtonRename.Left + ButtonRename.Width + 20
                .Width = 50
                .Height = ButtonRename.Height
                .Font = New Font("Arial", 6, FontStyle.Regular)
                .Visible = True
            End With

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1920: buttons resized to normal screen")
        End If
    End Sub

    Private Sub Form1_ResizeEnd(sender As Object, e As EventArgs) Handles Me.ResizeEnd
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1930: Form1_ResizeEnd")
        ResizeDebounceTimer.Stop()
        ISizeChanged()
    End Sub

    Private Sub Form1_Shown(sender As Object, e As EventArgs) Handles MyBase.Shown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1940: Form1_Shown")

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
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1950: Form1_Sizes: " & appLeftInt.ToString & " - " & appTopInt.ToString & " " & appWidthInt.ToString & " - " & appHeightInt.ToString)

        ResizeDebounceTimer.Stop()

        Buttons_to_normal()
        ISizeChanged()
    End Sub

    Private Sub Button5_Click(sender As Object, e As EventArgs) Handles Button5.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1960: Button5_Click")

        SlideShowTimer.Enabled = False
        Form2.Show()
    End Sub

    Private Sub Label1_MouseClick(sender As Object, e As MouseEventArgs) Handles Label1.MouseClick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1970: Label1_MouseClick")
        CopyFilePathToClipboard()
    End Sub

    Private Sub StatusL_MouseClick(sender As Object, e As MouseEventArgs) Handles StatusL.MouseClick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1980: StatusL_MouseClick")
        CopyFilePathToClipboard()
    End Sub

    Private Sub Button8_Click(sender As Object, e As EventArgs) Handles Button8.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1990: Button8_Click")
        SlideShowTimer.Enabled = False
        ReadShowMediaFile("ReadForRandom")
    End Sub

    Private Sub Button9_Click(sender As Object, e As EventArgs) Handles Button9.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2000: Button9_Click")
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

    Private Sub chkTopMost_CheckedChanged(sender As Object, e As EventArgs) Handles chkTopMost.CheckedChanged
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2010: chkTopMost_CheckedChanged")
        Me.TopMost = chkTopMost.Checked
    End Sub

    Private Sub ButtonLNG_Click(sender As Object, e As EventArgs) Handles ButtonLNG.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2020: ButtonLNG_Click")

        lngRus = Not lngRus
        ButtonLNG.Text = If(lngRus, "EN", "RU")
        LngCh()
        ReadShowMediaFile("SetFile")
    End Sub

    Private Sub ButtonRename_Click(sender As Object, e As EventArgs) Handles ButtonRename.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2030: ButtonRename_Click")

        If Not String.IsNullOrEmpty(currentFileName) Then
            RenameCurrentFile()
        Else
            StatusL.Text = If(lngRus, "! Нет файла для переименования", "! No file to rename")
        End If
    End Sub

    Private Sub CheckWebView2Availability()
        If isWebView2Available Then Return
        Try
            Dim version As String = CoreWebView2Environment.GetAvailableBrowserVersionString()
            If Not String.IsNullOrEmpty(version) Then
                isWebView2Available = True
            Else
                isWebView2Available = False
                allSupportedExtensions.ExceptWith(webImageFileExtensions)
                Dim message As String = If(lngRus, "WebView2 Runtime не установлен. Форматы .webp, .heic, .avif, .svg не поддерживаются. Установите WebView2 для полной функциональности.",
                                      "WebView2 Runtime is not installed. Formats .webp, .heic, .avif, .svg are not supported. Install WebView2 for full functionality.")
                StatusL.Text = message

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2040: isWebView2Available is not found")
            End If
        Catch ex As Exception
            isWebView2Available = False
            allSupportedExtensions.ExceptWith(webImageFileExtensions)
            Dim message As String = If(lngRus, "Ошибка проверки WebView2: " & ex.Message & vbCrLf & "Форматы .webp, .heic, .avif, .svg не поддерживаются.",
                                  "Error checking WebView2: " & ex.Message & vbCrLf & "Formats .webp, .heic, .avif, .svg are not supported.")
            MessageBox.Show(message, If(lngRus, "Ограниченный функционал", "Limited Functionality"), MessageBoxButtons.OK, MessageBoxIcon.Warning)

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2050: isWebView2Available ERR " & ex.Message)
        End Try
    End Sub

    Private Sub WebView2_NavigationCompleted(sender As Object, e As CoreWebView2NavigationCompletedEventArgs)
        If e.IsSuccess Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2060: WebView2_NavigationCompleted IsSuccess")
            isWebView2Loaded = True
            isWebView2Visible = True
            UpdateControlVisibility()
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2070: WebView2_NavigationCompleted Error: " & e.WebErrorStatus.ToString())
            isWebView2Visible = False
            UpdateControlVisibility()
        End If
    End Sub

    Private Async Function InitializeWebView2Async() As Task
        If isWebView2Available AndAlso webView2Control Is Nothing Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2080: InitializeWebView2Async")
            Try
                webView2Control = New WebView2()
                webView2Control.Visible = False
                webView2Control.Location = PictureBox1.Location
                webView2Control.Size = PictureBox1.Size
                Me.Controls.Add(webView2Control)
                Await webView2Control.EnsureCoreWebView2Async(Nothing)
                AddHandler webView2Control.NavigationCompleted, AddressOf WebView2_NavigationCompleted
                webView2Control.Source = New Uri("about:blank")

            Catch ex As Exception
                If webView2Control IsNot Nothing Then
                    webView2Control.Dispose()
                    webView2Control = Nothing
                End If
                isWebView2Available = False
                allSupportedExtensions.ExceptWith(webImageFileExtensions)

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2090: InitializeWebView2Async ERR " & ex.Message)
            End Try
        End If
    End Function

    Private Sub WebBrowser1_DocumentCompleted(sender As Object, e As WebBrowserDocumentCompletedEventArgs)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2100: WebBrowser1_DocumentCompleted")

        isWebBrowserLoaded = True
        lastLoadedUri = e.Url.ToString()

        If lastLoadedUri <> "about:blank" AndAlso WebBrowser1.DocumentText <> "" Then
            isWebBrowser1Visible = True
            UpdateControlVisibility()
        End If
    End Sub

    Private Sub Label3_MouseClick(sender As Object, e As MouseEventArgs) Handles Label3.MouseClick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2110: Label3_MouseClick")
        CopyFilePathToClipboard()
    End Sub

    Private Sub CopyFilePathToClipboard()
        If Not String.IsNullOrEmpty(currentFileName) Then
            Clipboard.SetText(currentFileName)
            StatusL.Text = If(lngRus, "Имя файла скопировано в буфер", "Filename sent to clipboard")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2120: Filename sent to clipboard")
        End If
    End Sub

    Private Sub TextBox1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles TextBox1.SelectedIndexChanged
        If Not isTextBoxEdition Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2130: TextBox1_SelectedIndexChanged")

            If TextBox1.SelectedIndex >= 0 Then
                currentFolderPath = TextBox1.SelectedItem.ToString()

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2140: currentFolderPath = " & currentFolderPath)
                ReadShowMediaFile("ReadFolderAndFile")
            End If
        End If
    End Sub

    Private Sub SortComboBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles SortComboBox.SelectedIndexChanged
        If Not isTextBoxEdition Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2150: SortComboBox_SelectedIndexChanged")

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

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2210: file moved afetr undo")
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
                    StatusL.Text = If(lngRus, "файл скопирован (" & textKey & ") в каталог " & destFile, "file copied (" & textKey & ") to " & destFile)

                Case "Move"
                    Dim args As String() = DirectCast(currentFileOperationArgs, String())
                    Dim textKey As String = args(2)
                    Dim destFile As String = args(1)
                    StatusL.Text = If(lngRus, "файл перенесен (" & textKey & ") в каталог " & destFile, "file moved (" & textKey & ") to " & destFile)

                Case "Delete"
            ' Статус уже установлен в UpdateFileIndexAndList
            ' Можно обновить UI, если нужно

                Case "DeleteUndo"
                    StatusL.Text = If(lngRus, "файл удален в каталоге " & historyDestinationFileName, "file deleted in " & historyDestinationFileName)
                    historyDestinationFileName = ""
                    historySourceFileName = ""

                Case "MoveUndo"
                    StatusL.Text = If(lngRus, "файл возвращен в каталог " & historySourceFileName, "file back to " & historySourceFileName)
                    historyDestinationFileName = ""
                    historySourceFileName = ""
            End Select
        Else
            StatusL.Text = If(lngRus, "Ошибка операции: " & e.Error.Message, "Operation error: " & e.Error.Message)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2230: FileOperationWorker_RunWorkerCompleted ERR " & e.Error.Message)
        End If
    End Sub

    Private Sub Label1_Click(sender As Object, e As EventArgs) Handles Label1.Click
        If Not String.IsNullOrEmpty(TextBox1.Text) Then
            Clipboard.SetText(TextBox1.Text)
            StatusL.Text = If(lngRus, "Имя папки скопировано в буфер", "Folder sent to clipboard")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2240: Folder sent to clipboard")
        End If
    End Sub

    Private Sub StatusL_Click(sender As Object, e As EventArgs) Handles StatusL.Click

    End Sub
End Class