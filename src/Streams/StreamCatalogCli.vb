Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Runtime.InteropServices

Namespace Streams

    ''' <summary>
    ''' Command-line interface for the stream catalog producer and validator (spec 022 §7).
    ''' Intercepted above the single-instance mutex in Application_Events.vb.
    ''' </summary>
    Friend Module StreamCatalogCli

        Private Const ATTACH_PARENT_PROCESS As Integer = -1

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Function AttachConsole(dwProcessId As Integer) As Boolean
        End Function

        ''' <summary>
        ''' Checks if the given command line contains any stream catalog switches.
        ''' </summary>
        Public Function IsCatalogCommandLine(args As String()) As Boolean
            If args Is Nothing OrElse args.Length = 0 Then Return False

            For Each arg In args
                If String.IsNullOrWhiteSpace(arg) Then Continue For
                Dim normalized = arg.Trim().ToLowerInvariant()
                If normalized = "--build-stream-catalog" OrElse normalized = "-build-stream-catalog" OrElse normalized = "/build-stream-catalog" OrElse
                   normalized = "--build-catalog" OrElse normalized = "-build-catalog" OrElse normalized = "/build-catalog" OrElse
                   normalized = "--check-stream-catalog" OrElse normalized = "-check-stream-catalog" OrElse normalized = "/check-stream-catalog" OrElse
                   normalized = "--check-catalog" OrElse normalized = "-check-catalog" OrElse normalized = "/check-catalog" Then
                    Return True
                End If
            Next
            Return False
        End Function

#If NETFRAMEWORK Then
        ''' <summary>
        ''' On the x86 / .NET Framework fallback, refuses the switch with a clear reason (criterion 21).
        ''' </summary>
        Public Sub RefuseNetFramework()
            Try
                AttachConsole(ATTACH_PARENT_PROCESS)
            Catch
            End Try

            Console.Error.WriteLine(ProductNames.Compact & ": Stream catalog producer/validator is only supported on the 64-bit modern build (.NET 10).")
            Environment.ExitCode = 1
        End Sub
#Else
        ''' <summary>
        ''' Runs the stream catalog CLI command and returns the process exit code (0 = pass, 1 = defect/refusal, 2 = usage error).
        ''' </summary>
        Public Function Run(args As String()) As Integer
            Try
                AttachConsole(ATTACH_PARENT_PROCESS)
            Catch
            End Try

            If args Is Nothing OrElse args.Length = 0 Then
                PrintUsage()
                Return 2
            End If

            Dim mode As String = Nothing
            Dim inputPath As String = Nothing
            Dim outputPath As String = Nothing
            Dim forceChurn As Boolean = False

            Dim i As Integer = 0
            While i < args.Length
                Dim arg = args(i).Trim()
                Dim lowerArg = arg.ToLowerInvariant()

                If lowerArg = "--build-stream-catalog" OrElse lowerArg = "-build-stream-catalog" OrElse lowerArg = "/build-stream-catalog" OrElse
                   lowerArg = "--build-catalog" OrElse lowerArg = "-build-catalog" OrElse lowerArg = "/build-catalog" Then
                    mode = "BUILD"
                    i += 1
                    If i < args.Length AndAlso Not args(i).StartsWith("-"c) AndAlso Not args(i).StartsWith("/"c) Then
                        inputPath = args(i)
                        i += 1
                    End If
                ElseIf lowerArg = "--check-stream-catalog" OrElse lowerArg = "-check-stream-catalog" OrElse lowerArg = "/check-stream-catalog" OrElse
                       lowerArg = "--check-catalog" OrElse lowerArg = "-check-catalog" OrElse lowerArg = "/check-catalog" Then
                    mode = "CHECK"
                    i += 1
                    If i < args.Length AndAlso Not args(i).StartsWith("-"c) AndAlso Not args(i).StartsWith("/"c) Then
                        inputPath = args(i)
                        i += 1
                    End If
                ElseIf lowerArg = "-o" OrElse lowerArg = "--output" OrElse lowerArg = "/o" OrElse lowerArg = "/output" Then
                    i += 1
                    If i < args.Length Then
                        outputPath = args(i)
                        i += 1
                    End If
                ElseIf lowerArg = "--force" OrElse lowerArg = "-f" OrElse lowerArg = "/force" OrElse lowerArg = "/f" Then
                    forceChurn = True
                    i += 1
                ElseIf lowerArg = "--help" OrElse lowerArg = "-h" OrElse lowerArg = "/help" OrElse lowerArg = "/?" Then
                    PrintUsage()
                    Return 0
                Else
                    If String.IsNullOrEmpty(inputPath) AndAlso Not arg.StartsWith("-"c) AndAlso Not arg.StartsWith("/"c) Then
                        inputPath = arg
                    End If
                    i += 1
                End If
            End While

            If String.IsNullOrEmpty(mode) Then
                PrintUsage()
                Return 2
            End If

            If mode = "BUILD" Then
                Return RunBuild(inputPath, outputPath, forceChurn)
            ElseIf mode = "CHECK" Then
                Return RunCheck(inputPath)
            Else
                PrintUsage()
                Return 2
            End If
        End Function

        Private Function RunBuild(inputPath As String, outputPath As String, forceChurn As Boolean) As Integer
            If String.IsNullOrWhiteSpace(inputPath) Then
                Console.Error.WriteLine("Error: Missing input playlist file or folder for --build-stream-catalog")
                PrintUsage()
                Return 2
            End If

            If Not File.Exists(inputPath) AndAlso Not Directory.Exists(inputPath) Then
                Console.Error.WriteLine("Error: Input path does not exist: " & inputPath)
                Return 2
            End If

            If String.IsNullOrWhiteSpace(outputPath) Then
                If Directory.Exists(inputPath) Then
                    outputPath = Path.Combine(inputPath, StreamCatalogSchema.ArchiveFileName)
                Else
                    Dim dir = Path.GetDirectoryName(Path.GetFullPath(inputPath))
                    outputPath = Path.Combine(dir, StreamCatalogSchema.ArchiveFileName)
                End If
            End If

            Console.WriteLine(ProductNames.Compact & ": Building stream catalog...")
            Console.WriteLine("  Input:   " & inputPath)
            Console.WriteLine("  Archive: " & outputPath)

            Dim buildResult = StreamCatalogProducer.Build(inputPath, outputPath, forceChurn)

            Console.WriteLine()
            If buildResult.Report IsNot Nothing Then
                Console.WriteLine(buildResult.Report.FormatReportText())
            End If

            If buildResult.Success Then
                Console.WriteLine("Catalog build SUCCESS: " & outputPath)
                Return 0
            Else
                Console.Error.WriteLine("Catalog build FAILED: " & buildResult.ErrorDescription)
                Return 1
            End If
        End Function

        Private Function RunCheck(archivePath As String) As Integer
            If String.IsNullOrWhiteSpace(archivePath) Then
                Console.Error.WriteLine("Error: Missing archive path for --check-stream-catalog")
                PrintUsage()
                Return 2
            End If

            If Not File.Exists(archivePath) Then
                Console.Error.WriteLine("Error: Archive file not found: " & archivePath)
                Return 2
            End If

            Console.WriteLine(ProductNames.Compact & ": Validating stream catalog archive...")
            Console.WriteLine("  Archive: " & archivePath)

            Dim validation = StreamCatalogValidator.ValidateArchive(archivePath)
            Dim reportDir = Path.GetDirectoryName(Path.GetFullPath(archivePath))
            Dim reportBase = Path.GetFileNameWithoutExtension(archivePath)
            Dim reportPath = Path.Combine(reportDir, reportBase & "-report.txt")

            Dim report As New StreamCatalogReport() With {
                .Mode = "CHECK",
                .InputPath = archivePath,
                .ArchivePath = archivePath,
                .ValidationResult = validation,
                .ShippedRowCount = validation.RowCount
            }
            report.WriteToFile(reportPath)

            Console.WriteLine()
            Console.WriteLine(report.FormatReportText())

            If validation.Passed Then
                Console.WriteLine("Validation SUCCESS: All checks passed for " & archivePath)
                Return 0
            Else
                Console.Error.WriteLine("Validation FAILED: " & validation.FailedVerdicts.Count & " check(s) failed.")
                Return 1
            End If
        End Function

        Private Sub PrintUsage()
            Console.WriteLine(ProductNames.Compact & " - Stream Catalog Producer & Validator (" & StreamCatalogSchema.ContractTag & ")")
            Console.WriteLine()
            Console.WriteLine("Usage:")
            Console.WriteLine("  FastMediaSorter_LITE.exe --build-stream-catalog <playlist.m3u|folder> [-o <stream-catalog.zip>] [--force]")
            Console.WriteLine("  FastMediaSorter_LITE.exe --check-stream-catalog <stream-catalog.zip>")
            Console.WriteLine()
            Console.WriteLine("Options:")
            Console.WriteLine("  --build-stream-catalog <path>   Build a conforming stream-catalog.zip from M3U playlist(s)")
            Console.WriteLine("  --check-stream-catalog <zip>    Run V1..V12 validation rules on an existing catalog archive")
            Console.WriteLine("  -o, --output <path>             Specify destination ZIP path (default: stream-catalog.zip)")
            Console.WriteLine("  --force                         Bypass the churn guard threshold when replacing existing archives")
            Console.WriteLine("  -h, --help                      Show this help message")
            Console.WriteLine()
            Console.WriteLine("Exit codes: 0 = PASS, 1 = Verification/Build Failure, 2 = Usage/Argument Error")
        End Sub
#End If

    End Module

End Namespace
