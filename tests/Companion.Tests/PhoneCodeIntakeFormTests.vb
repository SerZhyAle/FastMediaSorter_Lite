Option Strict On

Imports System.Reflection
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Spec 227 rule 1 and the secret-holder rule: the intake door's code field is a secret holder, so what is
''' typed or pasted into it must be masked for real. <c>UseSystemPasswordChar</c> is silently ignored by a
''' multiline TextBox (the field showed the code in clear while its property said True), so the field is
''' asked what the window itself holds as its password character, not what its property says.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class PhoneCodeIntakeFormTests

    Private Const EM_GETPASSWORDCHAR As Integer = &HD2

    Private Declare Unicode Function SendMessage Lib "user32.dll" Alias "SendMessageW" (
        hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr

    <Fact>
    Public Sub The_code_field_really_masks_what_is_typed_into_it()
        ExchangeTestSupport.OnUi(
            Sub()
                Using form As New Phone_Code_Intake_Form()
                    Dim field As FieldInfo = GetType(Phone_Code_Intake_Form).GetField("_codeBox", BindingFlags.Instance Or BindingFlags.NonPublic)
                    Dim box As TextBox = DirectCast(field.GetValue(form), TextBox)

                    Assert.False(box.Multiline, "a multiline field ignores the password character and shows the code in clear")
                    Assert.True(box.UseSystemPasswordChar)

                    box.Text = "FMSSFTP2:h=192.168.7.43&p=2022&u=phone&pw=secret"
                    Dim masked As IntPtr = SendMessage(box.Handle, EM_GETPASSWORDCHAR, IntPtr.Zero, IntPtr.Zero)
                    Assert.NotEqual(IntPtr.Zero, masked)
                End Using
            End Sub)
    End Sub

End Class
