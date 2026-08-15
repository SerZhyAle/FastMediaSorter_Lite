Option Strict On

Imports System.IO

''' <summary>
''' The one place that knows where this application keeps state on disk.
'''
''' It existed inside <c>OcrPaths</c> first, because OCR was the first feature that needed
''' a writable directory - but the location is not an OCR fact: the archive cache
''' (SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md §4.1) writes under the same root, and a
''' second copy of the path would be a second answer to "where does this app live", one of
''' which would eventually be wrong. OcrPaths now defers here.
'''
''' Compiled into BOTH builds: the two exes share one hive and one state folder, so they
''' must agree on this even though the archive feature itself is modern-only.
''' </summary>
Friend Module AppPaths

    ''' <summary>
    ''' %LOCALAPPDATA%\SZA\FastMediaSorter - writable without elevation, and the same
    ''' container the rest of the app's state lives in (which is what keeps it working
    ''' unchanged inside an MSIX package, where the install directory is read-only).
    ''' Not created here: each caller creates the subdirectory it actually needs.
    ''' </summary>
    Friend Function LocalAppDataRoot() As String
        Return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            App_name, Second_App_Name)
    End Function

End Module
