Option Strict On
Friend Module ResourceAddText
    Friend Function Title(kind As ResourceAddStep) As String
        Select Case kind
            Case ResourceAddStep.Introduction : Return Localization.T("Что такое ресурс")
            Case ResourceAddStep.Pick : Return Localization.T("Выбор папки")
            Case ResourceAddStep.Questions : Return Localization.T("Параметры ресурса")
            Case Else : Return Localization.T("Ресурс и дальнейшие действия")
        End Select
    End Function
    Friend Function Explanation(kind As ResourceAddStep) As String()
        Select Case kind
            Case ResourceAddStep.Introduction
                Return {Localization.T("Ресурс - папка или открытый виртуальный диск FileDO, доступный просмотрщику через этот компьютер."),
                    Localization.T("Вы выбираете, что увидит телефон: имя, файлы и разрешённые действия."),
                    Localization.T("Файлы остаются на компьютере. Телефон просматривает и копирует их по соединению; галочка в списке папок означает раздачу."),
                    Localization.T("Позже: строка папки - Параметры ресурса; QR и .fmscfg - окно доступа с телефона.")}
            Case ResourceAddStep.Pick
                Return {Localization.T("Выберите папку, которую хотите добавить в список раздачи."),
                    Localization.T("Телефон увидит только выбранные ресурсы, а не все папки компьютера."),
                    Localization.T("Кнопка откроет обычный выбор папки. Повторная папка не добавляется. Диски FileDO выбирайте уже открытыми; мастер не создаёт и не монтирует их."),
                    Localization.T("Позже: список папок - Добавить, Изменить или Удалить.")}
            Case ResourceAddStep.Questions
                Return {Localization.T("Имя, набор файлов, глубина просмотра и доступ этой папки."),
                    Localization.T("Только чтение запрещает изменения; папка-получатель разрешает копирование и перенос в неё."),
                    Localization.T("Выберите профиль и типы файлов, затем подпапки и доступ. Дополнительные параметры содержат комментарий, PIN и интервал слайд-шоу. Цвет получателя выбирает телефон."),
                    Localization.T("Позже: Параметры ресурса у строки папки. Настройки для отдельного получателя - мастер пакета .fmscfg.")}
            Case Else
                Return {Localization.T("Проверьте выбор и нажмите Готово, чтобы добавить ресурс."),
                    Localization.T("До подтверждения можно вернуться назад или отменить без изменений."),
                    Localization.T("Готово сохранит параметры и добавит отмеченный ресурс. До этого отмена ничего не меняет."),
                    Localization.T("Позже: параметры - у строки папки; раздача - Старт/Стоп; подключение - QR-код или пакет .fmscfg.")}
        End Select
    End Function
    Friend Function Finish(result As ResourceAddResult) As String
        Dim delivery As String
        Select Case result.Delivery
            Case ResourceAddDelivery.Disabled
                delivery = Localization.T("Пока сервер не включён, телефон не увидит ресурс. Включить его можно в настройках хостинга.")
            Case ResourceAddDelivery.NotServing
                delivery = Localization.T("Ресурс добавлен, но его доступность не подтверждена. Проверьте раздачу и права доступа в настройках.")
            Case Else
                delivery = Localization.T("Ресурс добавлен и раздаётся. На телефоне обновите список или QR-конфигурацию; получение ресурса телефоном здесь не подтверждается.")
        End Select
        Return String.Join(Environment.NewLine & Environment.NewLine, {delivery, result.Publication})
    End Function
End Module
