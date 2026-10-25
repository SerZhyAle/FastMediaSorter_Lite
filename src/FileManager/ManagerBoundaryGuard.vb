#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO

''' <summary>
''' Specification 262: Boundary refusal rules for test fixtures and operation boundaries.
''' Ensures that mutations and test fixtures stay strictly inside an explicitly specified,
''' expendable directory beneath a validated TestRoot, refusing drive roots, system folders,
''' user profiles, and media library folders.
''' </summary>
Public NotInheritable Class ManagerBoundaryGuard

    Private Sub New()
    End Sub

    Public Shared Function CheckSafeExpendableRoot(candidatePath As String, ByRef refusalReason As String) As Boolean
        refusalReason = String.Empty
        If String.IsNullOrWhiteSpace(candidatePath) Then
            refusalReason = "Path is empty or whitespace"
            Return False
        End If

        Dim fullPath As String
        Try
            fullPath = IO.Path.GetFullPath(candidatePath).TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar)
        Catch
            refusalReason = "Invalid path syntax"
            Return False
        End Try

        Dim root As String = IO.Path.GetPathRoot(fullPath)
        If String.IsNullOrEmpty(root) OrElse String.Equals(fullPath, root.TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) Then
            refusalReason = "Refused root drive directory: " & fullPath
            Return False
        End If

        Dim userProfile As String = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        If Not String.IsNullOrEmpty(userProfile) Then
            userProfile = IO.Path.GetFullPath(userProfile).TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar)
            If String.Equals(fullPath, userProfile, StringComparison.OrdinalIgnoreCase) Then
                refusalReason = "Refused user profile directory: " & fullPath
                Return False
            End If
        End If

        Dim windowsDir As String = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        If Not String.IsNullOrEmpty(windowsDir) Then
            windowsDir = IO.Path.GetFullPath(windowsDir).TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar)
            If fullPath.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase) Then
                refusalReason = "Refused Windows system directory: " & fullPath
                Return False
            End If
        End If

        Dim programFiles As String = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        If Not String.IsNullOrEmpty(programFiles) Then
            programFiles = IO.Path.GetFullPath(programFiles).TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar)
            If fullPath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) Then
                refusalReason = "Refused Program Files directory: " & fullPath
                Return False
            End If
        End If

        Dim programFilesX86 As String = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        If Not String.IsNullOrEmpty(programFilesX86) Then
            programFilesX86 = IO.Path.GetFullPath(programFilesX86).TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar)
            If fullPath.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase) Then
                refusalReason = "Refused Program Files (x86) directory: " & fullPath
                Return False
            End If
        End If

        Dim specialFolders As Environment.SpecialFolder() = {
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.CommonApplicationData
        }

        For Each folder In specialFolders
            Dim target As String = Environment.GetFolderPath(folder)
            If Not String.IsNullOrEmpty(target) Then
                target = IO.Path.GetFullPath(target).TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar)
                If String.Equals(fullPath, target, StringComparison.OrdinalIgnoreCase) Then
                    refusalReason = "Refused special system or media directory: " & fullPath
                    Return False
                End If
            End If
        Next

        Return True
    End Function

End Class
#End If
