#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' The ONE place this program sets ImageMagick's process-wide resource limits (audit 052
''' G-20; audit 048 A-17 extends it with its pixel and frame budget - add to
''' <see cref="EnsureApplied"/>, never set <c>ImageMagick.ResourceLimits</c> anywhere else).
'''
''' <b>Disk = 0.</b> ImageMagick moves its pixel cache into a <c>magick-*</c> file under
''' %TEMP% when an image outgrows the memory limits, and for a picture out of a secure
''' container that file IS the plaintext - on disk, and left there after a crash. With a
''' disk limit of zero such a decode fails instead of spilling, which the display path
''' reports like any other decode failure. The limit is process-wide, so it holds for
''' ordinary files as well; nobody asked for a %TEMP% full of pixel caches either.
'''
''' Idempotent and cheap: callers run it right before any Magick decode that matters, and
''' only the first call sets anything. The Share Manager links this file (its QR intake reads
''' through <see cref="MagickReadGate"/>), so it names nothing of the viewer: the area ceiling
''' is its own constant, held equal to the viewer's decode budget by a test, and a failure is
''' reported through <see cref="Trace"/>, which the viewer's log listens to.
''' </summary>
Friend Module MagickResourceLimits

    ''' <summary>The largest single image ImageMagick keeps in its pixel cache, in pixels - the
    ''' viewer's <c>DecodeBudget.Max_Frame_Pixels</c> (a test holds them equal).</summary>
    Friend Const Max_Area_Pixels As Long = 400L * 1000L * 1000L

    Private applied As Boolean
    Private ReadOnly apply_Gate As New Object()

    Friend Sub EnsureApplied()
        If applied Then Return
        SyncLock apply_Gate
            If applied Then Return
            Try
                ImageMagick.ResourceLimits.Disk = 0UL
                ' Audit 048, A-17: no single image larger than the decode budget stays in the
                ' pixel cache - past Area it would go to disk, and with Disk = 0 above that
                ' request fails instead. The decoder also refuses such a header before it
                ' calls Magick at all (DecodeBudget); this is the second line behind it.
                ImageMagick.ResourceLimits.Area = CULng(Max_Area_Pixels)
            Catch ex As Exception
                ' The native library failed to load: then Magick decodes nothing at all, and
                ' the decode path reports that on its own.
                Trace.WriteLine("Magick resource limits not applied: " & ex.GetType().Name)
            End Try
            applied = True
        End SyncLock
    End Sub

    ''' <summary>What the disk limit is right now - for the test that holds it at zero.</summary>
    Friend Function CurrentDiskLimit() As ULong
        Return ImageMagick.ResourceLimits.Disk
    End Function

End Module
#End If
