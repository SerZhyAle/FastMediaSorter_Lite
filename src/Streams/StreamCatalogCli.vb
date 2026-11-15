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
        ''' <summary>Exit codes of the table of APP-COMMANDS rule 11 (the usage code was 2 before ticket L0317).</summary>
        Friend Const ExitPass As Integer = 0
        Friend Const ExitFailure As Integer = 1
        Friend Const ExitUsage As Integer = 10
        Friend Const ExitInputMissing As Integer = 11

        ''' <summary>
        ''' Runs the stream catalog CLI command of the shipped switch spellings and returns the process exit code
        ''' (0 = pass, 1 = defect/refusal, 10 = usage error, 11 = input not found).
        ''' </summary>
        Public Function Run(args As String()) As Integer
            Try
                AttachConsole(ATTACH_PARENT_PROCESS)
            Catch
            End Try

            If args Is Nothing OrElse args.Length = 0 Then
                PrintUsage()
                Return ExitUsage
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
                    Return ExitPass
                Else
                    If String.IsNullOrEmpty(inputPath) AndAlso Not arg.StartsWith("-"c) AndAlso Not arg.StartsWith("/"c) Then
                        inputPath = arg
                    End If
                    i += 1
                End If
            End While

            If String.IsNullOrEmpty(mode) Then
                PrintUsage()
                Return ExitUsage
            End If

            If mode = "BUILD" Then
                Return RunBuild(inputPath, outputPath, forceChurn)
            ElseIf mode = "CHECK" Then
                Return RunCheck(inputPath)
            Else
                PrintUsage()
                Return ExitUsage
            End If
        End Function

        Private Function RunBuild(inputPath As String, outputPath As String, forceChurn As Boolean) As Integer
            If String.IsNullOrWhiteSpace(inputPath) Then
                Console.Error.WriteLine("Error: Missing input playlist file or folder for --build-stream-catalog")
                PrintUsage()
                Return ExitUsage
            End If
            ' The shipped spellings always replaced an existing archive (under the churn guard).
            Return BuildCore(inputPath, outputPath, forceChurn, True).ExitCode
        End Function

        Private Function RunCheck(archivePath As String) As Integer
            If String.IsNullOrWhiteSpace(archivePath) Then
                Console.Error.WriteLine("Error: Missing archive path for --check-stream-catalog")
                PrintUsage()
                Return ExitUsage
            End If
            Return CheckCore(archivePath).ExitCode
        End Function

        ''' <summary>
        ''' The one build both entry points share: the shipped switches (replaceExisting True) and the verb
        ''' <c>stream-catalog build</c> (replaceExisting is its <c>--overwrite</c>, rule 14). Prints the report as the
        ''' switches always did. The caller has already checked that an input was named.
        ''' </summary>
        Friend Function BuildCore(inputPath As String, outputPath As String, forceChurn As Boolean, replaceExisting As Boolean) As StreamCatalogRunResult
            Dim result As New StreamCatalogRunResult()
            If Not File.Exists(inputPath) AndAlso Not Directory.Exists(inputPath) Then
                Console.Error.WriteLine("Error: Input path does not exist: " & inputPath)
                result.ExitCode = ExitInputMissing
                result.Detail = inputPath
                Return result
            End If

            If String.IsNullOrWhiteSpace(outputPath) Then
                If Directory.Exists(inputPath) Then
                    outputPath = Path.Combine(inputPath, StreamCatalogSchema.ArchiveFileName)
                Else
                    Dim dir = Path.GetDirectoryName(Path.GetFullPath(inputPath))
                    outputPath = Path.Combine(dir, StreamCatalogSchema.ArchiveFileName)
                End If
            End If
            result.OutputPath = outputPath

            If Not replaceExisting AndAlso File.Exists(outputPath) Then
                Console.Error.WriteLine("Error: The archive exists and --overwrite was not given: " & outputPath)
                result.ExitCode = ExitFailure
                result.Detail = "exists"
                Return result
            End If

            Console.WriteLine(ProductNames.Compact & ": Building stream catalog...")
            Console.WriteLine("  Input:   " & inputPath)
            Console.WriteLine("  Archive: " & outputPath)

            Dim buildResult = StreamCatalogProducer.Build(inputPath, outputPath, forceChurn)

            Console.WriteLine()
            If buildResult.Report IsNot Nothing Then
                Console.WriteLine(buildResult.Report.FormatReportText())
                result.Rows = buildResult.Report.ShippedRowCount
            End If

            result.Passed = buildResult.Success
            If buildResult.Success Then
                Console.WriteLine("Catalog build SUCCESS: " & outputPath)
                result.ExitCode = ExitPass
            Else
                Console.Error.WriteLine("Catalog build FAILED: " & buildResult.ErrorDescription)
                result.ExitCode = ExitFailure
                result.Detail = buildResult.ErrorDescription
            End If
            Return result
        End Function

        ''' <summary>The one check both entry points share (the switches and <c>stream-catalog check</c>).</summary>
        Friend Function CheckCore(archivePath As String) As StreamCatalogRunResult
            Dim result As New StreamCatalogRunResult()
            If Not File.Exists(archivePath) Then
                Console.Error.WriteLine("Error: Archive file not found: " & archivePath)
                result.ExitCode = ExitInputMissing
                result.Detail = archivePath
                Return result
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

            result.OutputPath = archivePath
            result.Rows = validation.RowCount
            result.Passed = validation.Passed
            result.FailedChecks = validation.FailedVerdicts.Count
            If validation.Passed Then
                Console.WriteLine("Validation SUCCESS: All checks passed for " & archivePath)
                result.ExitCode = ExitPass
            Else
                Console.Error.WriteLine("Validation FAILED: " & validation.FailedVerdicts.Count & " check(s) failed.")
                result.ExitCode = ExitFailure
            End If
            Return result
        End Function

        Private Sub PrintUsage()
            Console.WriteLine(ProductNames.Compact & " - Stream Catalog Producer & Validator (" & StreamCatalogSchema.ContractTag & ")")
            Console.WriteLine()
            Console.WriteLine("Usage:")
            Console.WriteLine("  FastMediaSorter_LITE.exe --build-stream-catalog <playlist.m3u|folder> [-o <stream-catalog.zip>] [--force]")
            Console.WriteLine("  FastMediaSorter_LITE.exe --check-stream-catalog <stream-catalog.zip>")
            Console.WriteLine("  FastMediaSorter_LITE.exe stream-catalog build <playlist.m3u|folder> [--out <zip>] [--overwrite] [--force]")
            Console.WriteLine("  FastMediaSorter_LITE.exe stream-catalog check <stream-catalog.zip>")
            Console.WriteLine()
            Console.WriteLine("Options:")
            Console.WriteLine("  --build-stream-catalog <path>   Build a conforming stream-catalog.zip from M3U playlist(s)")
            Console.WriteLine("  --check-stream-catalog <zip>    Run V1..V12 validation rules on an existing catalog archive")
            Console.WriteLine("  -o, --output <path>             Specify destination ZIP path (default: stream-catalog.zip)")
            Console.WriteLine("  --force                         Bypass the churn guard threshold when replacing existing archives")
            Console.WriteLine("  -h, --help                      Show this help message")
            Console.WriteLine()
            Console.WriteLine("Exit codes: 0 = PASS, 1 = Verification/Build Failure, 10 = Usage/Argument Error, 11 = Input Not Found")
        End Sub
#End If

    End Module

#If Not NETFRAMEWORK Then
    ''' <summary>What one catalogue build or check came to - the shipped switches use only the exit code; the verbs of the
    ''' shared command profile (ticket L0317) also return the numbers.</summary>
    Friend NotInheritable Class StreamCatalogRunResult
        Friend Property ExitCode As Integer
        Friend Property Rows As Integer
        Friend Property Passed As Boolean
        Friend Property FailedChecks As Integer
        Friend Property OutputPath As String = String.Empty
        ''' <summary>The cause when it is not a pass: an English text from the producer, the missing path, or "exists".</summary>
        Friend Property Detail As String = String.Empty
    End Class
#End If

End Namespace
