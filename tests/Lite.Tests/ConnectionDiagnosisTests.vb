#If Not NETFRAMEWORK Then
Option Strict On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Spec 232 and ticket 239 CR-9 / DEVICE-EXCHANGE 1.1 (items CW, CX, CY, CZ, DD, DR):
''' Tests for connection diagnosis engine, probes, 13-language parity,
''' first-failure stop, redacted support bundle strings, and outside visibility.
''' </summary>
Public Class ConnectionDiagnosisTests

    Private Shared ReadOnly Locales13 As String() = {
        "en", "ru", "uk", "de", "es", "fr", "it", "ja", "ko", "pl", "pt", "tr", "zh"
    }

    <Fact>
    Public Sub All15StepsHaveNonEmptyTextsInAll13Languages()
        Dim allSteps As ConnectionDiagnosisStep() = CType([Enum].GetValues(GetType(ConnectionDiagnosisStep)), ConnectionDiagnosisStep())
        Assert.Equal(15, allSteps.Length)

        For Each stepVal In allSteps
            Assert.False(String.IsNullOrWhiteSpace(stepVal.IdOf()))
            For Each localeCode In Locales13
                Dim text As String = ConnectionDiagnosisLocalization.GetStepText(stepVal, localeCode)
                Assert.False(String.IsNullOrWhiteSpace(text), $"Step {stepVal.IdOf()} missing text for locale {localeCode}")
            Next
        Next
    End Sub

    <Fact>
    Public Sub CanonicalStepTextsMatchContractTableCY()
        ' Verbatim checks for EN, RU, UK from DEVICE-EXCHANGE 1.1 item CY
        Dim pcAsleepEn = ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.PcAsleep, "en")
        Assert.Equal("Switch on and wake the device that runs the server or the sharing service, and keep it from going to sleep.", pcAsleepEn)

        Dim pcAsleepRu = ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.PcAsleep, "ru")
        Assert.Equal("Включите и разбудите устройство, на котором работает сервер или сервис шаринга, и не давайте ему засыпать.", pcAsleepRu)

        Dim pcAsleepUk = ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.PcAsleep, "uk")
        Assert.Equal("Увімкніть і розбудіть пристрій, на якому працює сервер або сервіс шарингу, і не давайте йому засинати.", pcAsleepUk)

        Dim pubNetEn = ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.WindowsPublicNetwork, "en")
        Assert.Equal("On the Windows computer, set this network to private: Windows keeps other devices out of a public network.", pubNetEn)

        Dim pubNetRu = ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.WindowsPublicNetwork, "ru")
        Assert.Equal("На компьютере с Windows переключите эту сеть на «Частная»: из общедоступной сети Windows не пускает другие устройства.", pubNetRu)

        Dim pubNetUk = ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.WindowsPublicNetwork, "uk")
        Assert.Equal("На комп'ютері з Windows перемкніть цю мережу на «Приватна»: до загальнодоступної мережі Windows не пускає інші пристрої.", pubNetUk)
    End Sub

    <Fact>
    Public Sub CanonicalMessageTextsMatchContractTableCZ()
        ' Verbatim checks for EN, RU, UK from DEVICE-EXCHANGE 1.1 item CZ
        Dim badCredsEn = ConnectionDiagnosisLocalization.GetMessageText("bad-credentials", "en")
        Assert.Equal("The login or the password is wrong.", badCredsEn)

        Dim badCredsRu = ConnectionDiagnosisLocalization.GetMessageText("bad-credentials", "ru")
        Assert.Equal("Логин или пароль неверны.", badCredsRu)

        Dim badCredsUk = ConnectionDiagnosisLocalization.GetMessageText("bad-credentials", "uk")
        Assert.Equal("Логін або пароль неправильні.", badCredsUk)

        Dim unreachEn = ConnectionDiagnosisLocalization.GetMessageText("unreachable-server", "en")
        Assert.Equal("Cannot reach the server.", unreachEn)

        Dim unreachRu = ConnectionDiagnosisLocalization.GetMessageText("unreachable-server", "ru")
        Assert.Equal("Не удаётся связаться с сервером.", unreachRu)

        Dim unreachUk = ConnectionDiagnosisLocalization.GetMessageText("unreachable-server", "uk")
        Assert.Equal("Не вдається зв'язатися із сервером.", unreachUk)
    End Sub

    ' --- ticket 239 CR-9: every row of items CZ, DD and DR and every step of item CY, verbatim, with its kind and step ---

    ''' <summary>Code, kind, step id ("-" = none), English, Russian, Ukrainian of each row of the message tables of DEVICE-EXCHANGE
    ''' items CZ, DD and DR, plus the canonical local row <c>format-unsupported</c> of the amendment of 2026-10-17 (DEVICE-EXCHANGE 1.3; LIVE-BROADCAST 1.1
    ''' names it). Typed from the catalog, not read from it at run time, so a changed word is a failing test.</summary>
    Private Shared ReadOnly MessageRows As String()() = {
        New String() {"bad-credentials", "wire", "credentials", "The login or the password is wrong.", "Логин или пароль неверны.", "Логін або пароль неправильні."},
        New String() {"bad-credentials-code", "wire", "credentials", "The login or the one-time code is wrong.", "Логин или одноразовый код неверны.", "Логін або одноразовий код неправильні."},
        New String() {"device-revoked", "wire", "device-removed", "This device was removed from the account.", "Это устройство удалено из аккаунта.", "Цей пристрій видалено з акаунта."},
        New String() {"signed-out", "wire", "credentials", "The password of this account was changed, so this device was signed out.", "Пароль этого аккаунта изменили, поэтому это устройство вышло из аккаунта.", "Пароль цього акаунта змінили, тому цей пристрій вийшов з акаунта."},
        New String() {"version-unsupported", "wire", "operator", "This program and the server speak different versions of the exchange.", "Эта программа и сервер работают по разным версиям обмена.", "Ця програма і сервер працюють за різними версіями обміну."},
        New String() {"tls-required", "wire", "operator", "The server accepts this only over a protected connection, and this one is not protected.", "Сервер принимает это только по защищённому соединению, а это соединение не защищено.", "Сервер приймає це лише через захищене з'єднання, а це з'єднання не захищене."},
        New String() {"id-collision", "wire", "-", "The server already knows another device under this device's number. Remove this server from the list and add it again.", "Сервер уже знает другое устройство под номером этого. Удалите этот сервер из списка и добавьте его снова.", "Сервер уже знає інший пристрій під таким самим номером. Видаліть цей сервер зі списку й додайте його знову."},
        New String() {"capacity", "wire", "operator", "The server cannot take this: it has reached a limit of its own, or the item is too big to be listed.", "Сервер не может это принять: он достиг своего предела или объект слишком большой, чтобы показать его в списке.", "Сервер не може це прийняти: він досяг свого ліміту або об'єкт завеликий, щоб показати його в списку."},
        New String() {"port-unavailable", "wire", "operator", "The server has no free port to open for this folder.", "У сервера нет свободного порта для этой папки.", "У сервера немає вільного порту для цієї папки."},
        New String() {"unavailable", "wire", "pc-asleep", "The device or the server is not available right now.", "Сейчас устройство или сервер недоступны.", "Зараз пристрій або сервер недоступні."},
        New String() {"rate-limited", "wire", "sign-in-rate", "Too many attempts in a row.", "Слишком много попыток подряд.", "Забагато спроб поспіль."},
        New String() {"protection-unsupported", "wire", "-", "This folder is locked with a PIN, and this program cannot ask for it. Open it in a program that can.", "Эта папка закрыта PIN-кодом, и эта программа не умеет его запрашивать. Откройте её в программе, которая умеет.", "Ця папка закрита PIN-кодом, і ця програма не вміє його запитувати. Відкрийте її в програмі, яка вміє."},
        New String() {"invitation-invalid", "wire", "invitation", "This invitation does not work.", "Это приглашение не подходит.", "Це запрошення не підходить."},
        New String() {"login-taken", "wire", "-", "This login is already taken. Choose another one.", "Этот логин уже занят. Выберите другой.", "Цей логін уже зайнятий. Виберіть інший."},
        New String() {"password-policy", "wire", "-", "The password is too simple. Use at least 10 characters, with capital and small Latin letters and digits.", "Пароль слишком простой. Нужно не меньше 10 знаков: заглавные и строчные латинские буквы и цифры.", "Пароль надто простий. Потрібно щонайменше 10 символів: великі й малі латинські літери та цифри."},
        New String() {"limit-reached", "wire", "operator", "A limit set on the server has been reached.", "На сервере достигнут установленный предел.", "На сервері досягнуто встановленого ліміту."},
        New String() {"limit-reached-clipboard", "wire", "-", "This server does not take a clipboard of this size, or has the clipboard turned off. Send something smaller, or ask whoever runs the server.", "Этот сервер не принимает буфер такого размера или отключил буфер. Отправьте что-нибудь поменьше или обратитесь к тому, кто управляет сервером.", "Цей сервер не приймає буфер такого розміру або вимкнув буфер. Надішліть щось менше або зверніться до того, хто керує сервером."},
        New String() {"not-permitted", "wire", "-", "A guest account cannot do this. If you need it, ask for an invitation of the kind ""own device or family"".", "Гостевой аккаунт не может это делать. Если это нужно, попросите приглашение «своё устройство или семья».", "Гостьовий акаунт не може цього робити. Якщо це потрібно, попросіть запрошення «свій пристрій або сім'я»."},
        New String() {"declined", "cast", "-", "The other device did not accept the cast.", "Другое устройство не приняло cast.", "Інший пристрій не прийняв cast."},
        New String() {"timeout", "cast", "-", "The other device did not answer in time.", "Другое устройство не ответило вовремя.", "Інший пристрій не відповів вчасно."},
        New String() {"unsupported", "cast", "-", "The other device cannot play this kind of broadcast.", "Другое устройство не умеет воспроизводить такую трансляцию.", "Інший пристрій не вміє відтворювати таку трансляцію."},
        New String() {"unreachable-server", "local", "pc-asleep", "Cannot reach the server.", "Не удаётся связаться с сервером.", "Не вдається зв'язатися із сервером."},
        New String() {"unreachable-device", "local", "pc-asleep", "Cannot reach the device.", "Не удаётся связаться с устройством.", "Не вдається зв'язатися з пристроєм."},
        New String() {"unanswered", "local", "operator", "The server did not answer.", "Сервер не ответил.", "Сервер не відповів."},
        New String() {"tls-failed", "local", "device-clock", "A protected connection could not be set up.", "Не удалось установить защищённое соединение.", "Не вдалося встановити захищене з'єднання."},
        New String() {"certificate-changed", "local", "certificate", "This server does not look like the one you connected to before.", "Этот сервер не похож на тот, к которому вы подключались раньше.", "Цей сервер не схожий на той, до якого ви підключалися раніше."},
        New String() {"empty-credential", "local", "-", "Enter the login and the password first.", "Сначала введите логин и пароль.", "Спочатку введіть логін і пароль."},
        New String() {"server-offline", "state", "pc-asleep", "The server is offline. What you added stays in the list.", "Сервер не в сети. Всё, что вы добавили, остаётся в списке.", "Сервер не в мережі. Усе, що ви додали, залишається в списку."},
        New String() {"resource-locked", "state", "-", "This folder is locked with a PIN. Enter the PIN to open it.", "Папка закрыта PIN-кодом. Введите PIN-код, чтобы открыть её.", "Папка закрита PIN-кодом. Введіть PIN-код, щоб відкрити її."},
        New String() {"clipboard-empty", "result", "-", "There is nothing in the clipboard on the server.", "Буфер на сервере пуст.", "Буфер на сервері порожній."},
        New String() {"share-paused", "state", "-", "Android paused sharing on this phone. It will continue when the app is opened on the phone.", "Android приостановил раздачу на этом телефоне. Она продолжится, когда на телефоне откроют приложение.", "Android призупинив роздачу на цьому телефоні. Вона продовжиться, коли на телефоні відкриють застосунок."},
        New String() {"e2e-missing", "state", "-", "This device cannot receive protected content yet, so nothing is sent to it through the server.", "Это устройство пока не умеет принимать защищённое содержимое, поэтому через сервер ему ничего не отправляется.", "Цей пристрій поки не вміє приймати захищений вміст, тому через сервер йому нічого не надсилається."},
        New String() {"key-changed", "state", "-", "The protection key of this device has changed, so nothing is sent to it. Remove the device from the account and add it again.", "Ключ защиты этого устройства изменился, поэтому ему ничего не отправляется. Удалите устройство из аккаунта и добавьте его снова.", "Ключ захисту цього пристрою змінився, тому йому нічого не надсилається. Видаліть пристрій з акаунта й додайте його знову."},
        New String() {"server-no-e2e", "state", "operator", "This server cannot carry protected content, so folders, broadcasts and the clipboard do not go through it.", "Этот сервер не умеет передавать защищённое содержимое, поэтому папки, трансляции и буфер через него не идут.", "Цей сервер не вміє передавати захищений вміст, тому папки, трансляції й буфер через нього не йдуть."},
        New String() {"sealed-failed", "local", "-", "This could not be opened on this device. Try again from the other device.", "Это не удалось открыть на этом устройстве. Повторите с другого устройства.", "Це не вдалося відкрити на цьому пристрої. Повторіть з іншого пристрою."},
        New String() {"format-unsupported", "local", "-", "This format is newer than this program supports. Nothing was imported. Use a compatible program.", "Этот формат новее, чем поддерживает программа. Ничего не импортировано. Используйте совместимую программу.", "Цей формат новіший, ніж підтримує програма. Нічого не імпортовано. Скористайтеся сумісною програмою."},
        New String() {"refused", "wire", "operator", "The server refused this.", "Сервер отказал в этом.", "Сервер відмовив у цьому."}}

    <Fact>
    Public Sub EveryRowOfTheMessageTablesIsPinnedVerbatimWithItsKindAndStep()
        Assert.Equal(37, MessageRows.Length)
        Dim seen As New HashSet(Of String)()
        For Each row As String() In MessageRows
            Dim code As String = row(0)
            Assert.True(seen.Add(code), "twice in the pinned table: " & code)
            Dim message As ConnectionDiagnosisMessage = ConnectionDiagnosisMessages.OfCode(code)
            Assert.Equal(code, message.Code)
            Assert.Equal(row(1), message.Kind.ToString().ToLowerInvariant())
            Assert.Equal(row(2), If(message.StepValue.HasValue, message.StepValue.Value.IdOf(), "-"))
            Assert.Equal(row(3), ConnectionDiagnosisLocalization.GetMessageText(code, "en"))
            Assert.Equal(row(4), ConnectionDiagnosisLocalization.GetMessageText(code, "ru"))
            Assert.Equal(row(5), ConnectionDiagnosisLocalization.GetMessageText(code, "uk"))
        Next
    End Sub

    ''' <summary>Step id, applies, English, Russian, Ukrainian of the fifteen steps of item CY, in the order of the ladder.</summary>
    Private Shared ReadOnly StepRows As String()() = {
        New String() {"pc-asleep", "any", "Switch on and wake the device that runs the server or the sharing service, and keep it from going to sleep.", "Включите и разбудите устройство, на котором работает сервер или сервис шаринга, и не давайте ему засыпать.", "Увімкніть і розбудіть пристрій, на якому працює сервер або сервіс шарингу, і не давайте йому засинати."},
        New String() {"address-lan", "network", "Check the address, and make sure both devices are on the same Wi-Fi or home network.", "Проверьте адрес и убедитесь, что оба устройства в одной сети Wi-Fi или в одной домашней сети.", "Перевірте адресу й переконайтеся, що обидва пристрої в одній мережі Wi-Fi або в одній домашній мережі."},
        New String() {"guest-network", "network", "Join the main Wi-Fi network, not the router's guest network: in a guest network devices often cannot see each other.", "Подключитесь к основной сети Wi-Fi, а не к гостевой сети роутера: в гостевой сети устройства часто не видят друг друга.", "Підключіться до основної мережі Wi-Fi, а не до гостьової мережі роутера: у гостьовій мережі пристрої часто не бачать одне одного."},
        New String() {"vpn", "any", "Turn the VPN off on this device for a moment and try again.", "Ненадолго выключите VPN на этом устройстве и попробуйте ещё раз.", "На хвилину вимкніть VPN на цьому пристрої й спробуйте ще раз."},
        New String() {"windows-public-network", "network", "On the Windows computer, set this network to private: Windows keeps other devices out of a public network.", "На компьютере с Windows переключите эту сеть на «Частная»: из общедоступной сети Windows не пускает другие устройства.", "На комп'ютері з Windows перемкніть цю мережу на «Приватна»: до загальнодоступної мережі Windows не пускає інші пристрої."},
        New String() {"ip-family", "any", "Try another network, for example mobile data: the server may be reachable only by a kind of address (IPv4 or IPv6) that this network does not give you.", "Попробуйте другую сеть, например мобильный интернет: возможно, сервер доступен только по такому виду адреса (IPv4 или IPv6), которого эта сеть вам не даёт.", "Спробуйте іншу мережу, наприклад мобільний інтернет: можливо, сервер доступний лише за таким видом адреси (IPv4 або IPv6), якого ця мережа вам не дає."},
        New String() {"server-internet", "internet", "From another network, for example mobile data, check that the server answers from the internet.", "Из другой сети, например через мобильный интернет, проверьте, отвечает ли сервер из интернета.", "З іншої мережі, наприклад через мобільний інтернет, перевірте, чи відповідає сервер з інтернету."},
        New String() {"port-forward", "internet", "In the router's settings, forward the server's port to the computer that runs it.", "В настройках роутера пробросьте порт сервера на компьютер, на котором он работает.", "У налаштуваннях роутера пробросіть порт сервера на комп'ютер, на якому він працює."},
        New String() {"device-clock", "any", "Set the right date and time on this device, or turn on automatic time, and try again.", "Установите на этом устройстве правильные дату и время или включите автоматическую настройку времени и попробуйте ещё раз.", "Встановіть на цьому пристрої правильні дату й час або ввімкніть автоматичне налаштування часу й спробуйте ще раз."},
        New String() {"certificate", "any", "If you did not expect this, do not go on: ask whoever runs the server. If the server was reinstalled, confirm its new fingerprint.", "Если вы этого не ждали, не продолжайте: спросите того, кто управляет сервером. Если сервер переустановили, подтвердите его новый отпечаток.", "Якщо ви цього не чекали, не продовжуйте: запитайте того, хто керує сервером. Якщо сервер перевстановили, підтвердьте його новий відбиток."},
        New String() {"credentials", "any", "Check the login and type the password again. If you used a one-time code, ask for a new one.", "Проверьте логин и введите пароль ещё раз. Если вы вводили одноразовый код, попросите новый.", "Перевірте логін і введіть пароль ще раз. Якщо ви вводили одноразовий код, попросіть новий."},
        New String() {"sign-in-rate", "any", "Wait a few seconds and try again: the server takes about one attempt per second.", "Подождите несколько секунд и попробуйте снова: сервер принимает не больше одной попытки в секунду.", "Зачекайте кілька секунд і спробуйте знову: сервер приймає не більше однієї спроби на секунду."},
        New String() {"invitation", "any", "Check the invitation or ask for a new one: each works once, and this one may be wrong, used or withdrawn.", "Проверьте приглашение или попросите новое: каждое работает один раз, а это может быть неверным, уже использованным или отозванным.", "Перевірте запрошення або попросіть нове: кожне працює один раз, а це може бути неправильним, уже використаним або відкликаним."},
        New String() {"device-removed", "any", "Sign in to the account again to add this device back.", "Войдите в аккаунт заново, чтобы снова добавить это устройство.", "Увійдіть в акаунт знову, щоб додати цей пристрій назад."},
        New String() {"operator", "any", "Ask whoever runs the server: this can be fixed only on the server's side.", "Обратитесь к тому, кто управляет сервером: исправить это можно только на стороне сервера.", "Зверніться до того, хто керує сервером: виправити це можна лише на боці сервера."}}

    <Fact>
    Public Sub EveryStepOfTheLadderIsPinnedVerbatimInItsOrderWithWhereItApplies()
        Dim allSteps As ConnectionDiagnosisStep() = CType([Enum].GetValues(GetType(ConnectionDiagnosisStep)), ConnectionDiagnosisStep())
        Assert.Equal(15, StepRows.Length)
        Assert.Equal(StepRows.Length, allSteps.Length)
        For i As Integer = 0 To StepRows.Length - 1
            Dim row As String() = StepRows(i)
            Dim stepValue As ConnectionDiagnosisStep = allSteps.OrderBy(Function(s) CInt(s)).ElementAt(i)
            Assert.Equal(row(0), stepValue.IdOf())
            Assert.Equal(row(1), stepValue.AppliesOf().ToString().ToLowerInvariant())
            Assert.Equal(row(2), ConnectionDiagnosisLocalization.GetStepText(stepValue, "en"))
            Assert.Equal(row(3), ConnectionDiagnosisLocalization.GetStepText(stepValue, "ru"))
            Assert.Equal(row(4), ConnectionDiagnosisLocalization.GetStepText(stepValue, "uk"))
        Next
    End Sub

    <Fact>
    Public Sub MessageCodesResolveToNonEmptyTextsInAll13Languages()
        Dim codes As String() = {
            "bad-credentials", "bad-credentials-code", "device-revoked", "signed-out", "version-unsupported",
            "tls-required", "id-collision", "capacity", "port-unavailable", "unavailable", "rate-limited",
            "protection-unsupported", "invitation-invalid", "login-taken", "password-policy", "limit-reached",
            "limit-reached-clipboard", "not-permitted", "declined", "timeout", "unsupported",
            "unreachable-server", "unreachable-device", "unanswered", "tls-failed", "certificate-changed",
            "empty-credential", "server-offline", "resource-locked", "clipboard-empty",
            "share-paused", "e2e-missing", "key-changed", "server-no-e2e", "sealed-failed", "format-unsupported", "refused"
        }

        For Each code In codes
            Dim resolvedMsg = ConnectionDiagnosisMessages.OfCode(code)
            Assert.NotNull(resolvedMsg)
            Assert.Equal(code, resolvedMsg.Code)

            For Each localeCode In Locales13
                Dim text As String = ConnectionDiagnosisLocalization.GetMessageText(code, localeCode)
                Assert.False(String.IsNullOrWhiteSpace(text), $"Message {code} missing text for locale {localeCode}")
            Next
        Next
    End Sub

    ''' <summary>DEVICE-EXCHANGE 1.3 amendment of 2026-10-17 (named by LIVE-BROADCAST 1.1): the one `format-unsupported` sentence, in the
    ''' language the program shows. English, Russian and Ukrainian are the catalog's words (pinned above); every other table language and
    ''' the four interface languages the table does not carry (ar, hi, bn, ur) have a sentence of their own, none a copy of the English one,
    ''' and none asks for an update.</summary>
    <Fact>
    Public Sub FormatUnsupportedReadsInEveryInterfaceLanguageAndTheTableLanguagesWithoutAnUpdateDemand()
        Dim english As String = ConnectionDiagnosisLocalization.GetMessageText("format-unsupported", "en")
        Assert.Equal("This format is newer than this program supports. Nothing was imported. Use a compatible program.", english)
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                Dim text As String = ConnectionDiagnosisLocalization.FormatUnsupportedText()
                Assert.Equal(ConnectionDiagnosisLocalization.GetMessageText("format-unsupported", code), text)
                Assert.False(String.IsNullOrWhiteSpace(text), "format-unsupported missing for interface language " & code)
                Assert.NotEqual("format-unsupported", text)
                If code <> "en" Then Assert.NotEqual(english, text)
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
        For Each code As String In Locales13
            Dim text As String = ConnectionDiagnosisLocalization.GetMessageText("format-unsupported", code)
            If code <> "en" Then Assert.NotEqual(english, text)
            ' three sentences, no placeholder, no ellipsis, no smart double quote (house text style)
            Assert.DoesNotContain("{0}", text)
            Assert.DoesNotContain(ChrW(&H201C).ToString(), text)
            Assert.DoesNotContain(ChrW(&H201D).ToString(), text)
            Assert.DoesNotContain("...", text)
        Next
        ' the wire reason `version-unsupported` keeps its own row and asks for no update either
        Assert.NotEqual(english, ConnectionDiagnosisLocalization.GetMessageText("version-unsupported", "en"))
        Assert.Equal(ConnectionDiagnosisKind.Local, ConnectionDiagnosisMessages.FormatUnsupported.Kind)
        Assert.False(ConnectionDiagnosisMessages.FormatUnsupported.StepValue.HasValue)
        Assert.Same(ConnectionDiagnosisMessages.FormatUnsupported, ConnectionDiagnosisMessages.OfCode("format-unsupported"))
        ' a language the table does not carry still reads every OTHER row as English (the step and ui tables fall back as before)
        Assert.Equal(ConnectionDiagnosisLocalization.GetMessageText("bad-credentials", "en"), ConnectionDiagnosisLocalization.GetMessageText("bad-credentials", "ar"))
        Assert.Equal(ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.PcAsleep, "en"), ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.PcAsleep, "hi"))
    End Sub

    <Fact>
    Public Sub WindowsSettingsUriMappedCorrectly()
        Assert.Equal("ms-settings:network-status", ConnectionDiagnosisStep.WindowsPublicNetwork.WindowsSettingsUriOf())
        Assert.Equal("ms-settings:dateandtime", ConnectionDiagnosisStep.DeviceClock.WindowsSettingsUriOf())
        Assert.Null(ConnectionDiagnosisStep.PcAsleep.WindowsSettingsUriOf())
        Assert.Null(ConnectionDiagnosisStep.Operator.WindowsSettingsUriOf())
    End Sub

    <Fact>
    Public Sub RouteClassificationCategorizesRfc1918AndLanAsNetwork()
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("192.168.1.5"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("10.0.0.1"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("172.16.0.10"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("myhost.local"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("phone.lan"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("home.home"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("::1"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("fe80::1"))

        Assert.Equal(ConnectionDiagnosisApplies.Internet, ConnectionDiagnosisEngine.RouteOf("8.8.8.8"))
        Assert.Equal(ConnectionDiagnosisApplies.Internet, ConnectionDiagnosisEngine.RouteOf("example.com"))
        Assert.Equal(ConnectionDiagnosisApplies.Internet, ConnectionDiagnosisEngine.RouteOf("share.mydomain.org"))
    End Sub

    Private Class MockProbeProvider
        Implements IConnectionDiagnosisProbeProvider

        Public ReadOnly Outcomes As New Dictionary(Of ConnectionDiagnosisStep, ProbeOutcome)
        Public ReadOnly ExecutedSteps As New List(Of ConnectionDiagnosisStep)

        Public Sub New(configuredOutcomes As Dictionary(Of ConnectionDiagnosisStep, ProbeOutcome))
            If configuredOutcomes IsNot Nothing Then
                For Each kvp In configuredOutcomes
                    Outcomes(kvp.Key) = kvp.Value
                Next
            End If
        End Sub

        Public Function ProbeAsync(stepVal As ConnectionDiagnosisStep, context As ConnectionDiagnosisContext, ct As CancellationToken) As Task(Of ProbeOutcome) Implements IConnectionDiagnosisProbeProvider.ProbeAsync
            ExecutedSteps.Add(stepVal)
            Dim outcome As ProbeOutcome = ProbeOutcome.Passed
            If Outcomes.TryGetValue(stepVal, outcome) Then
                Return Task.FromResult(outcome)
            End If
            Return Task.FromResult(ProbeOutcome.Passed)
        End Function
    End Class

    <Fact>
    Public Async Function LadderStopsAtFirstFailedStep() As Task
        Dim mockOutcomes As New Dictionary(Of ConnectionDiagnosisStep, ProbeOutcome) From {
            {ConnectionDiagnosisStep.PcAsleep, ProbeOutcome.Passed},
            {ConnectionDiagnosisStep.AddressLan, ProbeOutcome.Passed},
            {ConnectionDiagnosisStep.GuestNetwork, ProbeOutcome.Passed},
            {ConnectionDiagnosisStep.Vpn, ProbeOutcome.Failed},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, ProbeOutcome.Passed}
        }
        Dim mockProbes As New MockProbeProvider(mockOutcomes)
        Dim engine As New ConnectionDiagnosisEngine(mockProbes)

        Dim ctx As New ConnectionDiagnosisContext("192.168.1.50", 22, ConnectionDiagnosisTarget.Server, ConnectionDiagnosisMessages.UnreachableServer)
        Dim result As ConnectionDiagnosisResult = Await engine.DiagnoseAsync(ctx, CancellationToken.None)

        Assert.Equal(ConnectionDiagnosisStep.Vpn, result.FirstFailedStep)
        Assert.Equal(4, mockProbes.ExecutedSteps.Count)
        Assert.Contains(ConnectionDiagnosisStep.Vpn, mockProbes.ExecutedSteps)
        Assert.DoesNotContain(ConnectionDiagnosisStep.WindowsPublicNetwork, mockProbes.ExecutedSteps)
    End Function

    <Fact>
    Public Async Function LadderContinuesWhenProbeReturnsCouldNotCheck() As Task
        Dim mockOutcomes As New Dictionary(Of ConnectionDiagnosisStep, ProbeOutcome) From {
            {ConnectionDiagnosisStep.PcAsleep, ProbeOutcome.CouldNotCheck},
            {ConnectionDiagnosisStep.AddressLan, ProbeOutcome.Passed},
            {ConnectionDiagnosisStep.GuestNetwork, ProbeOutcome.CouldNotCheck},
            {ConnectionDiagnosisStep.Vpn, ProbeOutcome.Passed},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, ProbeOutcome.Failed}
        }
        Dim mockProbes As New MockProbeProvider(mockOutcomes)
        Dim engine As New ConnectionDiagnosisEngine(mockProbes)

        Dim ctx As New ConnectionDiagnosisContext("192.168.1.50", 22, ConnectionDiagnosisTarget.Server, ConnectionDiagnosisMessages.UnreachableServer)
        Dim result As ConnectionDiagnosisResult = Await engine.DiagnoseAsync(ctx, CancellationToken.None)

        Assert.Equal(ConnectionDiagnosisStep.WindowsPublicNetwork, result.FirstFailedStep)
        Assert.Equal(5, mockProbes.ExecutedSteps.Count)
        Assert.Equal(ProbeOutcome.CouldNotCheck, result.StepOutcomes(ConnectionDiagnosisStep.PcAsleep))
        Assert.Equal(ProbeOutcome.CouldNotCheck, result.StepOutcomes(ConnectionDiagnosisStep.GuestNetwork))
        Assert.Equal(ProbeOutcome.Failed, result.StepOutcomes(ConnectionDiagnosisStep.WindowsPublicNetwork))
    End Function

    <Fact>
    Public Async Function RedactedDiagnosticStringContainsNoSensitiveData() As Task
        Dim mockOutcomes As New Dictionary(Of ConnectionDiagnosisStep, ProbeOutcome) From {
            {ConnectionDiagnosisStep.PcAsleep, ProbeOutcome.Passed},
            {ConnectionDiagnosisStep.AddressLan, ProbeOutcome.Failed}
        }
        Dim mockProbes As New MockProbeProvider(mockOutcomes)
        Dim engine As New ConnectionDiagnosisEngine(mockProbes)

        Dim secretHost As String = "192.168.1.188"
        Dim ctx As New ConnectionDiagnosisContext(secretHost, 8080, ConnectionDiagnosisTarget.Server, ConnectionDiagnosisMessages.UnreachableServer)
        Dim result As ConnectionDiagnosisResult = Await engine.DiagnoseAsync(ctx, CancellationToken.None)

        Dim diagnosticText As String = result.ToRedactedDiagnosticString()

        Assert.DoesNotContain(secretHost, diagnosticText)
        Assert.DoesNotContain("8080", diagnosticText)
        Assert.Contains("walk=pc-asleep:passed,address-lan:failed", diagnosticText)
        Assert.Contains("fail=address-lan", diagnosticText)
        Assert.Contains("msg=unreachable-server", diagnosticText)
        Assert.Contains("route=network", diagnosticText)

        Dim report As String = LogPackage.BuildReport(Function() New List(Of KeyValuePair(Of String, Object))(), Path.GetTempPath(), Array.Empty(Of String)(), "none")
        Assert.Contains("[connection diagnosis]", report)
        Assert.Contains("walk=pc-asleep:passed,address-lan:failed", report)
        Assert.DoesNotContain(secretHost, report)
    End Function

    Private Class MockOutsideProbe
        Implements IOutsideVisibilityProbe

        Public CallCount As Integer = 0
        Public DesiredVerdict As OutsideVisibilityVerdict

        Public Sub New(verdict As OutsideVisibilityVerdict)
            DesiredVerdict = verdict
        End Sub

        Public Function CheckVisibilityAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of OutsideVisibilityVerdict) Implements IOutsideVisibilityProbe.CheckVisibilityAsync
            CallCount += 1
            Return Task.FromResult(DesiredVerdict)
        End Function
    End Class

    <Fact>
    Public Async Function OutsideVisibilityCheckerCallsServiceOnlyOnRequest() As Task
        Dim probeReachable As New MockOutsideProbe(OutsideVisibilityVerdict.Reachable)
        Dim checker As New OutsideVisibilityChecker(probeReachable)

        Assert.Equal(0, probeReachable.CallCount)

        Dim res = Await checker.CheckAsync("203.0.113.10", 443, CancellationToken.None)
        Assert.Equal(1, probeReachable.CallCount)
        Assert.Equal(OutsideVisibilityVerdict.Reachable, res.Verdict)

        Dim probeNotReachable As New MockOutsideProbe(OutsideVisibilityVerdict.NotReachable)
        Dim checkerNotReachable As New OutsideVisibilityChecker(probeNotReachable)
        Dim res2 = Await checkerNotReachable.CheckAsync("example.com:8443", CancellationToken.None)
        Assert.Equal(1, probeNotReachable.CallCount)
        Assert.Equal(OutsideVisibilityVerdict.NotReachable, res2.Verdict)

        ' Invalid inputs should return CouldNotCheck without calling probe
        Dim probeNeverCalled As New MockOutsideProbe(OutsideVisibilityVerdict.Reachable)
        Dim checkerInvalid As New OutsideVisibilityChecker(probeNeverCalled)
        Dim resInvalid = Await checkerInvalid.CheckAsync("", 0, CancellationToken.None)
        Assert.Equal(0, probeNeverCalled.CallCount)
        Assert.Equal(OutsideVisibilityVerdict.CouldNotCheck, resInvalid.Verdict)
    End Function

End Class
#End If
