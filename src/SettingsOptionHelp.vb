#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Windows.Forms

''' <summary>
''' The help of the Settings window's boolean options (BOOLEAN-OPTION 2.0 section 4; ticket 243, slice T-11b-help): which
''' options carry a `?` and the ONE authored <see cref="OptionHelpSource"/> of each. The window asks this class for the
''' source of an option by its settings key and hands it to <see cref="OptionHeading"/>; an option that is not listed here
''' gets no button and its row is laid out exactly as before.
'''
''' A `?` is given only where the full text states facts the always-visible explanation of the row does not: both states,
''' what an option depends on, a consequence the user would not guess. It is never given to the confirmation options
''' (<c>no_confirmation</c>, <c>confirm_delete</c>, <c>delete_to_recycle_bin</c>): their visible lines already carry what
''' there is to say, and no option may nag. Every fact below was read from the code that acts on the option; when that code
''' changes, the text changes with it (the key of a source is the option's settings key, so a test names the pair).
'''
''' Modern build only: the net48 fallback keeps its fixed form and gets no help buttons (maintenance policy).
''' </summary>
Friend NotInheritable Class SettingsOptionHelp

    Private Sub New()
    End Sub

    ''' <summary>The settings keys of the options that carry a `?`, in the order of the window.</summary>
    Friend Shared ReadOnly Keys As String() = {"perspective", "dynamic_perspective", "animated_perspective", "reduce_motion", "ocr_auto"}

    ''' <summary>True when the option has a help source.</summary>
    Friend Shared Function HasHelp(key As String) As Boolean
        Return Array.IndexOf(Keys, key) >= 0
    End Function

    ''' <summary>The help of the option in the language the window speaks NOW; Nothing for an option without one. The text is
    ''' resolved on every call, so a window that switches language asks again and hands the result to
    ''' <see cref="OptionHeading.SetHelp"/>.</summary>
    Friend Shared Function SourceFor(key As String) As OptionHelpSource
        Select Case key
            Case "perspective"
                Return New OptionHelpSource("perspective",
                    Localization.T("Выключено: однотонные поля. При увеличении и в быстром слайд-шоу фон не рисуется."),
                    Localization.TF("Включено: если пропорции фотографии не совпадают с окном, свободные полосы по бокам или сверху и снизу продолжают её края. Выключено: свободная область заливается одним цветом, его задаёт параметр «{0}». При увеличении изображения полос нет, а в слайд-шоу с интервалом короче 5 секунд они не рисуются. Параметры «{1}» и «{2}» зависят от параметра «{3}»: пока он выключен, они недоступны, а их значения сохраняются.",
                                    Localization.T("Цвет фона"), Localization.T("Динамический ореол"), Localization.T("Анимация ореола"),
                                    Localization.T("Перспективный фон")))
            Case "dynamic_perspective"
                Return New OptionHelpSource("dynamic_perspective",
                    Localization.T("Это постоянный вид кадра, а не эффект появления. Выключено: полосы остаются плоскими."),
                    Localization.TF("Включено: полосы вокруг фотографии постепенно переходят в цвет фона, получается ореол. Это постоянный вид кадра, а не только момент появления. Выключено: полосы остаются плоским продолжением краёв фотографии. Будет ли ореол вырастать при открытии файла, решает параметр «{0}».",
                                    Localization.T("Анимация ореола")))
            Case "animated_perspective"
                Return New OptionHelpSource("animated_perspective",
                    Localization.T("Определяет только то, как ореол появляется. Готовый кадр в обоих случаях одинаков."),
                    Localization.TF("Включено: при открытии нового файла ореол вырастает от края фотографии; скорость выбирается в следующей строке. Выключено: ореол сразу на месте. Готовый кадр в обоих случаях одинаков. При изменении размера окна и масштаба ореол не анимируется. Анимации нет и при включённом параметре «{0}», и когда в Windows выключены анимационные эффекты: ореол тогда появляется сразу. Параметр доступен, пока включены «{1}» и «{2}»; иначе он недоступен, а его значение сохраняется.",
                                    Localization.T("Уменьшать анимацию"), Localization.T("Перспективный фон"), Localization.T("Динамический ореол")))
            Case "reduce_motion"
                Return New OptionHelpSource("reduce_motion",
                    Localization.T("Выключает декоративное движение. Учитывает и настройку анимации самой Windows."),
                    Localization.T("Включено: плавающие панели появляются и исчезают без плавного перехода, ореол вокруг фотографии возникает сразу, а фон аудио без обложки показывается неподвижным кадром. Выключено: движение остаётся. Если анимационные эффекты выключены в самой Windows, программа не анимирует и при снятой галочке; сам параметр она при этом не меняет."))
            Case "ocr_auto"
                Return New OptionHelpSource("ocr_auto",
                    Localization.T("Текст распознаётся и переводится после открытия каждого изображения. Переводчику уходит текст, не картинка."),
                    Localization.T("Включено: после показа каждого изображения программа сама распознаёт текст и переводит его, как по клавише T. Распознавание идёт на этом компьютере, а переводчику отправляется только распознанный текст, не картинка. Он уходит по адресу переводчика из настроек: если это не этот компьютер, текст покидает его с каждым новым изображением. Уже переведённые изображения берутся из кэша. Режим переключается и клавишей Shift+T."))
            Case Else
                Return Nothing
        End Select
    End Function

    ''' <summary>Every shipped source in the current language, in the order of <see cref="Keys"/> (tests, and the one place
    ''' that can say which options carry a `?`).</summary>
    Friend Shared Function AllSources() As List(Of OptionHelpSource)
        Dim all As New List(Of OptionHelpSource)()
        For Each key As String In Keys
            all.Add(SourceFor(key))
        Next
        Return all
    End Function

    ''' <summary>Gives the option's row its `?` when the option has help (see <see cref="OptionHeading.Attach"/>); Nothing and
    ''' no control for any other option. The caller runs <see cref="SettingsCheckBox.ArrangeRow"/> as for any row.</summary>
    Friend Shared Function Attach(host As Control, check As CheckBox, title As Label, description As Label, scale As Single,
                                  key As String) As OptionHeading
        Dim source As OptionHelpSource = SourceFor(key)
        If source Is Nothing Then Return Nothing
        Return OptionHeading.Attach(host, check, title, description, scale, String.Empty, source)
    End Function

    ''' <summary>A language switch: the option's help is built again in the new language and its button re-reads the caption
    ''' (its accessible name and description), keeping the button, its focus and an open popup. A row without help is left alone.</summary>
    Friend Shared Sub Relocalize(check As CheckBox, key As String)
        Dim heading As OptionHeading = OptionHeading.Find(check)
        If heading Is Nothing OrElse Not heading.HasHelp Then Return
        heading.SetHelp(SourceFor(key))
    End Sub

    ''' <summary>The colour roles the window hands to the help of its rows (surface, text, muted text, border, accent).</summary>
    Friend Shared Function PaletteOf(surface As System.Drawing.Color, ink As System.Drawing.Color, mutedInk As System.Drawing.Color,
                                     border As System.Drawing.Color, accent As System.Drawing.Color) As OptionHelpPalette
        Return New OptionHelpPalette With {.Surface = surface, .Ink = ink, .MutedInk = mutedInk, .Border = border, .Accent = accent}
    End Function
End Class
#End If
