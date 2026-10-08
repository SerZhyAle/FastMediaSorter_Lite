Option Strict On

''' <summary>
''' The exchange server's password floor, mirrored for IMMEDIATE feedback only (ticket 142 T-2).
'''
''' The policy is the owner's and lives in exactly one place that decides: the Go binary
''' (<c>internal/password</c>: at least 10 characters with an upper-case Latin letter, a lower-case Latin
''' letter and a digit; ticket 138). This copy never stores or sends anything - it lets the dialog say
''' "too short" beside the field before a round trip, and the binary still has the last word (exit 3).
''' The two cannot drift unnoticed: the tests read the SHARED vectors file the Go suite also reads
''' (<c>exchange-server/internal/password/testdata/password-policy.json</c>) and run every row through
''' this function.
'''
''' Counted the way the Go rule counts: characters are Unicode code points (<c>utf8.RuneCountInString</c>),
''' not UTF-16 units and not grapheme clusters; the three classes are ASCII only, so a Cyrillic letter is
''' no lower-case letter here.
''' </summary>
Friend NotInheritable Class ExchangePasswordPolicy

    Private Sub New()
    End Sub

    ''' <summary>The contract's floor, in characters.</summary>
    Friend Const MinLength As Integer = 10

    ''' <summary>The verdict, in the order the Go rule reports it: length first, then the classes.</summary>
    Friend Enum Verdict
        Accepted
        TooShort
        NoUpper
        NoLower
        NoDigit
    End Enum

    Friend Shared Function Check(password As String) As Verdict
        Dim text As String = If(password, "")
        Dim count As Integer = 0
        Dim up As Boolean = False
        Dim low As Boolean = False
        Dim dig As Boolean = False
        For Each r As Text.Rune In text.EnumerateRunes()
            count += 1
            Dim v As Integer = r.Value
            If v >= AscW("A"c) AndAlso v <= AscW("Z"c) Then
                up = True
            ElseIf v >= AscW("a"c) AndAlso v <= AscW("z"c) Then
                low = True
            ElseIf v >= AscW("0"c) AndAlso v <= AscW("9"c) Then
                dig = True
            End If
        Next
        If count < MinLength Then Return Verdict.TooShort
        If Not up Then Return Verdict.NoUpper
        If Not low Then Return Verdict.NoLower
        If Not dig Then Return Verdict.NoDigit
        Return Verdict.Accepted
    End Function

    Friend Shared Function IsAccepted(password As String) As Boolean
        Return Check(password) = Verdict.Accepted
    End Function

    ''' <summary>The length of a generated password: well above the floor.</summary>
    Friend Const GeneratedLength As Integer = 16

    ' No look-alikes (I l 1 O 0): a generated password is read out or retyped by a person.
    Private Const Upper As String = "ABCDEFGHJKLMNPQRSTUVWXYZ"
    Private Const Lower As String = "abcdefghijkmnopqrstuvwxyz"
    Private Const Digits As String = "23456789"

    ''' <summary>A password that meets the floor, from the operating system's random source: one character of each
    ''' class guaranteed, the rest drawn from all three, then shuffled so the guaranteed ones are not in front.
    ''' It exists in memory only - the caller shows it once and lets go of it (ticket 168 section 3.1).</summary>
    Friend Shared Function Generate() As String
        Dim all As String = Upper & Lower & Digits
        Dim chars(GeneratedLength - 1) As Char
        chars(0) = Upper(Security.Cryptography.RandomNumberGenerator.GetInt32(Upper.Length))
        chars(1) = Lower(Security.Cryptography.RandomNumberGenerator.GetInt32(Lower.Length))
        chars(2) = Digits(Security.Cryptography.RandomNumberGenerator.GetInt32(Digits.Length))
        For i As Integer = 3 To GeneratedLength - 1
            chars(i) = all(Security.Cryptography.RandomNumberGenerator.GetInt32(all.Length))
        Next
        For i As Integer = chars.Length - 1 To 1 Step -1
            Dim j As Integer = Security.Cryptography.RandomNumberGenerator.GetInt32(i + 1)
            Dim tmp As Char = chars(i)
            chars(i) = chars(j)
            chars(j) = tmp
        Next
        Dim result As New String(chars)
        Array.Clear(chars, 0, chars.Length)
        Return result
    End Function

End Class
