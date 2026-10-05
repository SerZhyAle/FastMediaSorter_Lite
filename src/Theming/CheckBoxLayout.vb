#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms
Imports CheckBoxState = System.Windows.Forms.VisualStyles.CheckBoxState

''' <summary>Shared checkbox anatomy: a left box, its adjacent caption, and an optional
''' hint aligned below the caption. Does not replace controls or their event handlers.</summary>
Friend NotInheritable Class CheckBoxLayout
    Friend Const CaptionGapLogical As Integer = 3

    Private Shared ReadOnly hints As New ConditionalWeakTable(Of Control, Object)()

    Friend Shared Sub ApplyTree(root As Control)
        If root Is Nothing OrElse root.IsDisposed Then Return
        Dim check As CheckBox = TryCast(root, CheckBox)
        If check IsNot Nothing Then Apply(check)
        For Each child As Control In root.Controls
            ApplyTree(child)
        Next
    End Sub

    Friend Shared Sub Apply(check As CheckBox)
        check.CheckAlign = ContentAlignment.MiddleLeft
        check.TextAlign = ContentAlignment.MiddleLeft
        check.AutoEllipsis = False
    End Sub

    ''' <summary>The native checkbox's actual glyph width and caption gap at this DPI.</summary>
    Friend Shared Function CaptionInset(check As CheckBox) As Integer
        Using graphics As Graphics = check.CreateGraphics()
            Return CheckBoxRenderer.GetGlyphSize(graphics, CheckBoxState.UncheckedNormal).Width +
                check.LogicalToDeviceUnits(CaptionGapLogical) + check.Padding.Left
        End Using
    End Function

    ''' <summary>Bind an existing hint to its checkbox. Flow/table rows use a margin;
    ''' coordinate-based dialogs preserve the hint's right edge. Reapply after DPI/font changes.</summary>
    Friend Shared Sub BindHint(check As CheckBox, hint As Control)
        If hints.TryGetValue(hint, Nothing) Then Return
        hints.Add(hint, New Object())
        Apply(check)
        Dim arranging As Boolean = False
        Dim initialMargin As Padding = hint.Margin
        Dim initialMaximum As Size = hint.MaximumSize
        Dim arrange As Action =
            Sub()
                If arranging OrElse check.IsDisposed OrElse hint.IsDisposed Then Return
                arranging = True
                Try
                    Dim inset As Integer = CaptionInset(check)
                    If TypeOf hint.Parent Is FlowLayoutPanel OrElse TypeOf hint.Parent Is TableLayoutPanel Then
                        Dim margin As Padding = initialMargin
                        If check.RightToLeft = RightToLeft.Yes Then
                            margin.Right = check.Margin.Right + inset
                        Else
                            margin.Left = check.Margin.Left + inset
                        End If
                        If hint.Margin <> margin Then
                            If initialMaximum.Width > 0 Then
                                hint.MaximumSize = New Size(Math.Max(1, initialMaximum.Width - margin.Horizontal + initialMargin.Horizontal),
                                                            initialMaximum.Height)
                            End If
                            hint.Margin = margin
                        End If
                    ElseIf hint.Parent IsNot Nothing Then
                        Dim left As Integer = If(check.RightToLeft = RightToLeft.Yes, check.Left, check.Left + inset)
                        Dim right As Integer = If(check.RightToLeft = RightToLeft.Yes, check.Right - inset, hint.Right)
                        If hint.Left <> left OrElse hint.Width <> right - left Then
                            hint.SetBounds(left, hint.Top, Math.Max(1, right - left), hint.Height)
                        End If
                    End If
                Finally
                    arranging = False
                End Try
            End Sub
        AddHandler check.FontChanged, Sub() arrange()
        AddHandler check.DpiChangedAfterParent, Sub() arrange()
        AddHandler check.RightToLeftChanged, Sub() arrange()
        AddHandler hint.ParentChanged, Sub() arrange()
        AddHandler check.Layout, Sub() arrange()
        arrange()
    End Sub
End Class
#End If
