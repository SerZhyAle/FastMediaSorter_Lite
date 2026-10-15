#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization

''' <summary>
''' Spec 232 / DEVICE-EXCHANGE 0.27 section 25 items CY, CZ, DD, DR:
''' Localization table for all 15 check steps and all message rows in 13 languages.
''' Languages: en, ru, uk (canonical from contract) plus de, es, fr, it, ja, ko, pl, pt, tr, zh.
''' </summary>
Public Module ConnectionDiagnosisLocalization

    Private ReadOnly SupportedLocales As String() = {
        "en", "ru", "uk", "de", "es", "fr", "it", "ja", "ko", "pl", "pt", "tr", "zh"
    }

    Private ReadOnly StepTexts As New Dictionary(Of String, Dictionary(Of ConnectionDiagnosisStep, String))(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly MessageTexts As New Dictionary(Of String, Dictionary(Of String, String))(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly UiTexts As New Dictionary(Of String, Dictionary(Of String, String))(StringComparer.OrdinalIgnoreCase)

    Sub New()
        InitializeEnglish()
        InitializeRussian()
        InitializeUkrainian()
        InitializeGerman()
        InitializeSpanish()
        InitializeFrench()
        InitializeItalian()
        InitializeJapanese()
        InitializeKorean()
        InitializePolish()
        InitializePortuguese()
        InitializeTurkish()
        InitializeChinese()
    End Sub

    Public Function CurrentLocale() As String
        Dim name As String = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant()
        If StepTexts.ContainsKey(name) Then Return name
        ' check for ua -> uk
        If name = "ua" AndAlso StepTexts.ContainsKey("uk") Then Return "uk"
        Return "en"
    End Function

    Public Function GetStepText(stepVal As ConnectionDiagnosisStep, Optional locale As String = "") As String
        Dim loc As String = NormalizeLocale(locale)
        Dim dict As Dictionary(Of ConnectionDiagnosisStep, String) = Nothing
        If StepTexts.TryGetValue(loc, dict) AndAlso dict.ContainsKey(stepVal) Then
            Return dict(stepVal)
        End If
        If StepTexts.TryGetValue("en", dict) AndAlso dict.ContainsKey(stepVal) Then
            Return dict(stepVal)
        End If
        Return String.Empty
    End Function

    Public Function GetMessageText(code As String, Optional locale As String = "") As String
        If String.IsNullOrWhiteSpace(code) Then Return String.Empty
        Dim loc As String = NormalizeLocale(locale)
        Dim key As String = code.Trim().ToLowerInvariant()
        Dim dict As Dictionary(Of String, String) = Nothing
        If MessageTexts.TryGetValue(loc, dict) AndAlso dict.ContainsKey(key) Then
            Return dict(key)
        End If
        If MessageTexts.TryGetValue("en", dict) AndAlso dict.ContainsKey(key) Then
            Return dict(key)
        End If
        Return code
    End Function

    Public Function GetUiText(uiKey As String, Optional locale As String = "") As String
        If String.IsNullOrWhiteSpace(uiKey) Then Return String.Empty
        Dim loc As String = NormalizeLocale(locale)
        Dim key As String = uiKey.Trim().ToLowerInvariant()
        Dim dict As Dictionary(Of String, String) = Nothing
        If UiTexts.TryGetValue(loc, dict) AndAlso dict.ContainsKey(key) Then
            Return dict(key)
        End If
        If UiTexts.TryGetValue("en", dict) AndAlso dict.ContainsKey(key) Then
            Return dict(key)
        End If
        Return uiKey
    End Function

    Private Function NormalizeLocale(locale As String) As String
        If String.IsNullOrWhiteSpace(locale) Then Return CurrentLocale()
        Dim clean As String = locale.Trim().ToLowerInvariant()
        If clean = "ua" Then clean = "uk"
        If StepTexts.ContainsKey(clean) Then Return clean
        Return "en"
    End Function

    ' --- 1. ENGLISH (Canon item CY & CZ) ---
    Private Sub InitializeEnglish()
        Dim steps As New Dictionary(Of ConnectionDiagnosisStep, String) From {
            {ConnectionDiagnosisStep.PcAsleep, "Switch on and wake the device that runs the server or the sharing service, and keep it from going to sleep."},
            {ConnectionDiagnosisStep.AddressLan, "Check the address, and make sure both devices are on the same Wi-Fi or home network."},
            {ConnectionDiagnosisStep.GuestNetwork, "Join the main Wi-Fi network, not the router's guest network: in a guest network devices often cannot see each other."},
            {ConnectionDiagnosisStep.Vpn, "Turn the VPN off on this device for a moment and try again."},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, "On the Windows computer, set this network to private: Windows keeps other devices out of a public network."},
            {ConnectionDiagnosisStep.IpFamily, "Try another network, for example mobile data: the server may be reachable only by a kind of address (IPv4 or IPv6) that this network does not give you."},
            {ConnectionDiagnosisStep.ServerInternet, "From another network, for example mobile data, check that the server answers from the internet."},
            {ConnectionDiagnosisStep.PortForward, "In the router's settings, forward the server's port to the computer that runs it."},
            {ConnectionDiagnosisStep.DeviceClock, "Set the right date and time on this device, or turn on automatic time, and try again."},
            {ConnectionDiagnosisStep.Certificate, "If you did not expect this, do not go on: ask whoever runs the server. If the server was reinstalled, confirm its new fingerprint."},
            {ConnectionDiagnosisStep.Credentials, "Check the login and type the password again. If you used a one-time code, ask for a new one."},
            {ConnectionDiagnosisStep.SignInRate, "Wait a few seconds and try again: the server takes about one attempt per second."},
            {ConnectionDiagnosisStep.Invitation, "Check the invitation or ask for a new one: each works once, and this one may be wrong, used or withdrawn."},
            {ConnectionDiagnosisStep.DeviceRemoved, "Sign in to the account again to add this device back."},
            {ConnectionDiagnosisStep.Operator, "Ask whoever runs the server: this can be fixed only on the server's side."}
        }
        StepTexts("en") = steps

        Dim msgs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"bad-credentials", "The login or the password is wrong."},
            {"bad-credentials-code", "The login or the one-time code is wrong."},
            {"device-revoked", "This device was removed from the account."},
            {"signed-out", "The password of this account was changed, so this device was signed out."},
            {"version-unsupported", "This program and the server speak different versions of the exchange."},
            {"tls-required", "The server accepts this only over a protected connection, and this one is not protected."},
            {"id-collision", "The server already knows another device under this device's number. Remove this server from the list and add it again."},
            {"capacity", "The server cannot take this: it has reached a limit of its own, or the item is too big to be listed."},
            {"port-unavailable", "The server has no free port to open for this folder."},
            {"unavailable", "The device or the server is not available right now."},
            {"rate-limited", "Too many attempts in a row."},
            {"protection-unsupported", "This folder is locked with a PIN, and this program cannot ask for it. Open it in a program that can."},
            {"invitation-invalid", "This invitation does not work."},
            {"login-taken", "This login is already taken. Choose another one."},
            {"password-policy", "The password is too simple. Use at least 10 characters, with capital and small Latin letters and digits."},
            {"limit-reached", "A limit set on the server has been reached."},
            {"limit-reached-clipboard", "This server does not take a clipboard of this size, or has the clipboard turned off. Send something smaller, or ask whoever runs the server."},
            {"not-permitted", "A guest account cannot do this. If you need it, ask for an invitation of the kind ""own device or family""."},
            {"declined", "The other device did not accept the cast."},
            {"timeout", "The other device did not answer in time."},
            {"unsupported", "The other device cannot play this kind of broadcast."},
            {"unreachable-server", "Cannot reach the server."},
            {"unreachable-device", "Cannot reach the device."},
            {"unanswered", "The server did not answer."},
            {"tls-failed", "A protected connection could not be set up."},
            {"certificate-changed", "This server does not look like the one you connected to before."},
            {"empty-credential", "Enter the login and the password first."},
            {"server-offline", "The server is offline. What you added stays in the list."},
            {"resource-locked", "This folder is locked with a PIN. Enter the PIN to open it."},
            {"clipboard-empty", "There is nothing in the clipboard on the server."},
            {"share-paused", "Android paused sharing on this phone. It will continue when the app is opened on the phone."},
            {"e2e-missing", "This device cannot receive protected content yet, so nothing is sent to it through the server."},
            {"key-changed", "The protection key of this device has changed, so nothing is sent to it. Remove the device from the account and add it again."},
            {"server-no-e2e", "This server cannot carry protected content, so folders, broadcasts and the clipboard do not go through it."},
            {"sealed-failed", "This could not be opened on this device. Try again from the other device."},
            {"refused", "The server refused this."}
        }
        MessageTexts("en") = msgs

        Dim ui As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"door_button", "Why doesn't it connect?.."},
            {"dialog_title", "Connection Diagnosis"},
            {"what_happened", "What happened:"},
            {"what_to_try", "What to try:"},
            {"btn_next_step", "Next step"},
            {"btn_details", "Details"},
            {"btn_copy", "Copy"},
            {"btn_close", "Close"},
            {"btn_open_settings", "Open Windows Settings"},
            {"check_visibility", "Is my server visible from outside"},
            {"checking", "Checking..."},
            {"reachable", "Reachable from the internet."},
            {"not_reachable", "Not reachable from the internet."},
            {"could_not_check", "Could not check reachability right now."},
            {"likely_causes", "Likely causes: router port forward is missing, carrier NAT (CGNAT), or firewall."},
            {"codes_copied", "Diagnosis codes copied to clipboard."}
        }
        UiTexts("en") = ui
    End Sub

    ' --- 2. RUSSIAN (Canon item CY & CZ) ---
    Private Sub InitializeRussian()
        Dim steps As New Dictionary(Of ConnectionDiagnosisStep, String) From {
            {ConnectionDiagnosisStep.PcAsleep, "Включите и разбудите устройство, на котором работает сервер или сервис шаринга, и не давайте ему засыпать."},
            {ConnectionDiagnosisStep.AddressLan, "Проверьте адрес и убедитесь, что оба устройства в одной сети Wi-Fi или в одной домашней сети."},
            {ConnectionDiagnosisStep.GuestNetwork, "Подключитесь к основной сети Wi-Fi, а не к гостевой сети роутера: в гостевой сети устройства часто не видят друг друга."},
            {ConnectionDiagnosisStep.Vpn, "Ненадолго выключите VPN на этом устройстве и попробуйте ещё раз."},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, "На компьютере с Windows переключите эту сеть на «Частная»: из общедоступной сети Windows не пускает другие устройства."},
            {ConnectionDiagnosisStep.IpFamily, "Попробуйте другую сеть, например мобильный интернет: возможно, сервер доступен только по такому виду адреса (IPv4 или IPv6), которого эта сеть вам не даёт."},
            {ConnectionDiagnosisStep.ServerInternet, "Из другой сети, например через мобильный интернет, проверьте, отвечает ли сервер из интернета."},
            {ConnectionDiagnosisStep.PortForward, "В настройках роутера пробросьте порт сервера на компьютер, на котором он работает."},
            {ConnectionDiagnosisStep.DeviceClock, "Установите на этом устройстве правильные дату и время или включите автоматическую настройку времени и попробуйте ещё раз."},
            {ConnectionDiagnosisStep.Certificate, "Если вы этого не ждали, не продолжайте: спросите того, кто управляет сервером. Если сервер переустановили, подтвердите его новый отпечаток."},
            {ConnectionDiagnosisStep.Credentials, "Проверьте логин и введите пароль ещё раз. Если вы вводили одноразовый код, попросите новый."},
            {ConnectionDiagnosisStep.SignInRate, "Подождите несколько секунд и попробуйте снова: сервер принимает не больше одной попытки в секунду."},
            {ConnectionDiagnosisStep.Invitation, "Проверьте приглашение или попросите новое: каждое работает один раз, а это может быть неверным, уже использованным или отозванным."},
            {ConnectionDiagnosisStep.DeviceRemoved, "Войдите в аккаунт заново, чтобы снова добавить это устройство."},
            {ConnectionDiagnosisStep.Operator, "Обратитесь к тому, кто управляет сервером: исправить это можно только на стороне сервера."}
        }
        StepTexts("ru") = steps

        Dim msgs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"bad-credentials", "Логин или пароль неверны."},
            {"bad-credentials-code", "Логин или одноразовый код неверны."},
            {"device-revoked", "Это устройство удалено из аккаунта."},
            {"signed-out", "Пароль этого аккаунта изменили, поэтому это устройство вышло из аккаунта."},
            {"version-unsupported", "Эта программа и сервер работают по разным версиям обмена."},
            {"tls-required", "Сервер принимает это только по защищённому соединению, а это соединение не защищено."},
            {"id-collision", "Сервер уже знает другое устройство под номером этого. Удалите этот сервер из списка и добавьте его снова."},
            {"capacity", "Сервер не может это принять: он достиг своего предела или объект слишком большой, чтобы показать его в списке."},
            {"port-unavailable", "У сервера нет свободного порта для этой папки."},
            {"unavailable", "Сейчас устройство или сервер недоступны."},
            {"rate-limited", "Слишком много попыток подряд."},
            {"protection-unsupported", "Эта папка закрыта PIN-кодом, и эта программа не умеет его запрашивать. Откройте её в программе, которая умеет."},
            {"invitation-invalid", "Это приглашение не подходит."},
            {"login-taken", "Этот логин уже занят. Выберите другой."},
            {"password-policy", "Пароль слишком простой. Нужно не меньше 10 знаков: заглавные и строчные латинские буквы и цифры."},
            {"limit-reached", "На сервере достигнут установленный предел."},
            {"limit-reached-clipboard", "Этот сервер не принимает буфер такого размера или отключил буфер. Отправьте что-нибудь поменьше или обратитесь к тому, кто управляет сервером."},
            {"not-permitted", "Гостевой аккаунт не может это делать. Если это нужно, попросите приглашение «своё устройство или семья»."},
            {"declined", "Другое устройство не приняло cast."},
            {"timeout", "Другое устройство не ответило вовремя."},
            {"unsupported", "Другое устройство не умеет воспроизводить такую трансляцию."},
            {"unreachable-server", "Не удаётся связаться с сервером."},
            {"unreachable-device", "Не удаётся связаться с устройством."},
            {"unanswered", "Сервер не ответил."},
            {"tls-failed", "Не удалось установить защищённое соединение."},
            {"certificate-changed", "Этот сервер не похож на тот, к которому вы подключались раньше."},
            {"empty-credential", "Сначала введите логин и пароль."},
            {"server-offline", "Сервер не в сети. Всё, что вы добавили, остаётся в списке."},
            {"resource-locked", "Папка закрыта PIN-кодом. Введите PIN-код, чтобы открыть её."},
            {"clipboard-empty", "Буфер на сервере пуст."},
            {"share-paused", "Android приостановил раздачу на этом телефоне. Она продолжится, когда на телефоне откроют приложение."},
            {"e2e-missing", "Это устройство пока не умеет принимать защищённое содержимое, поэтому через сервер ему ничего не отправляется."},
            {"key-changed", "Ключ защиты этого устройства изменился, поэтому ему ничего не отправляется. Удалите устройство из аккаунта и добавьте его снова."},
            {"server-no-e2e", "Этот сервер не умеет передавать защищённое содержимое, поэтому папки, трансляции и буфер через него не идут."},
            {"sealed-failed", "Это не удалось открыть на этом устройстве. Повторите с другого устройства."},
            {"refused", "Сервер отказал в этом."}
        }
        MessageTexts("ru") = msgs

        Dim ui As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"door_button", "Почему не подключается?.."},
            {"dialog_title", "Диагностика подключения"},
            {"what_happened", "Что произошло:"},
            {"what_to_try", "Что попробовать:"},
            {"btn_next_step", "Следующий шаг"},
            {"btn_details", "Подробности"},
            {"btn_copy", "Копировать"},
            {"btn_close", "Закрыть"},
            {"btn_open_settings", "Открыть настройки Windows"},
            {"check_visibility", "Виден ли мой сервер снаружи"},
            {"checking", "Проверка..."},
            {"reachable", "Сервер доступен из интернета."},
            {"not_reachable", "Сервер недоступен из интернета."},
            {"could_not_check", "Не удалось проверить доступность сейчас."},
            {"likely_causes", "Вероятные причины: не проброшен порт на роутере, провайдерский NAT (CGNAT) или брандмауэр."},
            {"codes_copied", "Коды диагностики скопированы в буфер обмена."}
        }
        UiTexts("ru") = ui
    End Sub

    ' --- 3. UKRAINIAN (Canon item CY & CZ) ---
    Private Sub InitializeUkrainian()
        Dim steps As New Dictionary(Of ConnectionDiagnosisStep, String) From {
            {ConnectionDiagnosisStep.PcAsleep, "Увімкніть і розбудіть пристрій, на якому працює сервер або сервіс шарингу, і не давайте йому засинати."},
            {ConnectionDiagnosisStep.AddressLan, "Перевірте адресу й переконайтеся, що обидва пристрої в одній мережі Wi-Fi або в одній домашній мережі."},
            {ConnectionDiagnosisStep.GuestNetwork, "Підключіться до основної мережі Wi-Fi, а не до гостьової мережі роутера: у гостьовій мережі пристрої часто не бачать одне одного."},
            {ConnectionDiagnosisStep.Vpn, "На хвилину вимкніть VPN на цьому пристрої й спробуйте ще раз."},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, "На комп'ютері з Windows перемкніть цю мережу на «Приватна»: до загальнодоступної мережі Windows не пускає інші пристрої."},
            {ConnectionDiagnosisStep.IpFamily, "Спробуйте іншу мережу, наприклад мобільний інтернет: можливо, сервер доступний лише за таким видом адреси (IPv4 або IPv6), якого ця мережа вам не дає."},
            {ConnectionDiagnosisStep.ServerInternet, "З іншої мережі, наприклад через мобільний інтернет, перевірте, чи відповідає сервер з інтернету."},
            {ConnectionDiagnosisStep.PortForward, "У налаштуваннях роутера пробросіть порт сервера на комп'ютер, на якому він працює."},
            {ConnectionDiagnosisStep.DeviceClock, "Встановіть на цьому пристрої правильні дату й час або ввімкніть автоматичне налаштування часу й спробуйте ще раз."},
            {ConnectionDiagnosisStep.Certificate, "Якщо ви цього не чекали, не продовжуйте: запитайте того, хто керує сервером. Якщо сервер перевстановили, підтвердьте його новий відбиток."},
            {ConnectionDiagnosisStep.Credentials, "Перевірте логін і введіть пароль ще раз. Якщо ви вводили одноразовий код, попросіть новий."},
            {ConnectionDiagnosisStep.SignInRate, "Зачекайте кілька секунд і спробуйте знову: сервер приймає не більше однієї спроби на секунду."},
            {ConnectionDiagnosisStep.Invitation, "Перевірте запрошення або попросіть нове: кожне працює один раз, а це може бути неправильним, уже використаним або відкликаним."},
            {ConnectionDiagnosisStep.DeviceRemoved, "Увійдіть в акаунт знову, щоб додати цей пристрій назад."},
            {ConnectionDiagnosisStep.Operator, "Зверніться до того, хто керує сервером: виправити це можна лише на боці сервера."}
        }
        StepTexts("uk") = steps

        Dim msgs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"bad-credentials", "Логін або пароль неправильні."},
            {"bad-credentials-code", "Логін або одноразовий код неправильні."},
            {"device-revoked", "Цей пристрій видалено з акаунта."},
            {"signed-out", "Пароль цього акаунта змінили, тому цей пристрій вийшов з акаунта."},
            {"version-unsupported", "Ця програма і сервер працюють за різними версіями обміну."},
            {"tls-required", "Сервер приймає це лише через захищене з'єднання, а це з'єднання не захищене."},
            {"id-collision", "Сервер уже знає інший пристрій під таким самим номером. Видаліть цей сервер зі списку й додайте його знову."},
            {"capacity", "Сервер не може це прийняти: він досяг свого ліміту або об'єкт завеликий, щоб показати його в списку."},
            {"port-unavailable", "У сервера немає вільного порту для цієї папки."},
            {"unavailable", "Зараз пристрій або сервер недоступні."},
            {"rate-limited", "Забагато спроб поспіль."},
            {"protection-unsupported", "Ця папка закрита PIN-кодом, і ця програма не вміє його запитувати. Відкрийте її в програмі, яка вміє."},
            {"invitation-invalid", "Це запрошення не підходить."},
            {"login-taken", "Цей логін уже зайнятий. Виберіть інший."},
            {"password-policy", "Пароль надто простий. Потрібно щонайменше 10 символів: великі й малі латинські літери та цифри."},
            {"limit-reached", "На сервері досягнуто встановленого ліміту."},
            {"limit-reached-clipboard", "Цей сервер не приймає буфер такого розміру або вимкнув буфер. Надішліть щось менше або зверніться до того, хто керує сервером."},
            {"not-permitted", "Гостьовий акаунт не може цього робити. Якщо це потрібно, попросіть запрошення «свій пристрій або сім'я»."},
            {"declined", "Інший пристрій не прийняв cast."},
            {"timeout", "Інший пристрій не відповів вчасно."},
            {"unsupported", "Інший пристрій не вміє відтворювати таку трансляцію."},
            {"unreachable-server", "Не вдається зв'язатися із сервером."},
            {"unreachable-device", "Не вдається зв'язатися з пристроєм."},
            {"unanswered", "Сервер не відповів."},
            {"tls-failed", "Не вдалося встановити захищене з'єднання."},
            {"certificate-changed", "Цей сервер не схожий на той, до якого ви підключалися раніше."},
            {"empty-credential", "Спочатку введіть логін і пароль."},
            {"server-offline", "Сервер не в мережі. Усе, що ви додали, залишається в списку."},
            {"resource-locked", "Папка закрита PIN-кодом. Введіть PIN-код, щоб відкрити її."},
            {"clipboard-empty", "Буфер на сервері порожній."},
            {"share-paused", "Android призупинив роздачу на цьому телефоні. Вона продовжиться, коли на телефоні відкриють застосунок."},
            {"e2e-missing", "Цей пристрій поки не вміє приймати захищений вміст, тому через сервер йому нічого не надсилається."},
            {"key-changed", "Ключ захисту цього пристрою змінився, тому йому нічого не надсилається. Видаліть пристрій з акаунта й додайте його знову."},
            {"server-no-e2e", "Цей сервер не вміє передавати захищений вміст, тому папки, трансляції й буфер через нього не йдуть."},
            {"sealed-failed", "Це не вдалося відкрити на цьому пристрої. Повторіть з іншого пристрою."},
            {"refused", "Сервер відмовив у цьому."}
        }
        MessageTexts("uk") = msgs

        Dim ui As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"door_button", "Чому не підключається?.."},
            {"dialog_title", "Діагностика підключення"},
            {"what_happened", "Що сталося:"},
            {"what_to_try", "Що спробувати:"},
            {"btn_next_step", "Наступний крок"},
            {"btn_details", "Подробиці"},
            {"btn_copy", "Копіювати"},
            {"btn_close", "Закрити"},
            {"btn_open_settings", "Відкрити налаштування Windows"},
            {"check_visibility", "Чи видно мій сервер ззовні"},
            {"checking", "Перевірка..."},
            {"reachable", "Сервер доступний з інтернету."},
            {"not_reachable", "Сервер недоступний з інтернету."},
            {"could_not_check", "Не вдалося перевірити доступність зараз."},
            {"likely_causes", "Ймовірні причини: не прокинуто порт на роутері, провайдерський NAT (CGNAT) або брандмауер."},
            {"codes_copied", "Коди діагностики скопійовано в буфер обміну."}
        }
        UiTexts("uk") = ui
    End Sub

    ' Helper to populate translated locales with localized step and message dictionaries
    Private Sub AddLocale(loc As String, steps As Dictionary(Of ConnectionDiagnosisStep, String), msgs As Dictionary(Of String, String), ui As Dictionary(Of String, String))
        StepTexts(loc) = steps
        MessageTexts(loc) = msgs
        UiTexts(loc) = ui
    End Sub

    ' --- 4. GERMAN ---
    Private Sub InitializeGerman()
        Dim steps As New Dictionary(Of ConnectionDiagnosisStep, String) From {
            {ConnectionDiagnosisStep.PcAsleep, "Schalten Sie das Gerät ein und wecken Sie es auf, auf dem der Server läuft."},
            {ConnectionDiagnosisStep.AddressLan, "Prüfen Sie die Adresse und stellen Sie sicher, dass beide Geräte im selben WLAN oder Heimnetzwerk sind."},
            {ConnectionDiagnosisStep.GuestNetwork, "Verbinden Sie sich mit dem Haupt-WLAN, nicht mit dem Gastnetzwerk des Routers."},
            {ConnectionDiagnosisStep.Vpn, "Deaktivieren Sie das VPN auf diesem Gerät kurz und versuchen Sie es erneut."},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, "Stellen Sie dieses Netzwerk auf dem Windows-Computer auf privat um."},
            {ConnectionDiagnosisStep.IpFamily, "Versuchen Sie ein anderes Netzwerk (IPv4- oder IPv6-Unterstützung)."},
            {ConnectionDiagnosisStep.ServerInternet, "Prüfen Sie aus einem anderen Netzwerk, ob der Server aus dem Internet erreichbar ist."},
            {ConnectionDiagnosisStep.PortForward, "Leiten Sie im Router den Port des Servers an diesen Computer weiter."},
            {ConnectionDiagnosisStep.DeviceClock, "Stellen Sie Datum und Uhrzeit auf diesem Gerät richtig ein."},
            {ConnectionDiagnosisStep.Certificate, "Bestätigen Sie den neuen Fingerabdruck des Servers oder fragen Sie den Administrator."},
            {ConnectionDiagnosisStep.Credentials, "Prüfen Sie Login und Passwort und versuchen Sie es erneut."},
            {ConnectionDiagnosisStep.SignInRate, "Warten Sie einige Sekunden: Der Server begrenzt die Anmeldeversuche."},
            {ConnectionDiagnosisStep.Invitation, "Prüfen Sie die Einladung oder fordern Sie eine neue an."},
            {ConnectionDiagnosisStep.DeviceRemoved, "Melden Sie sich erneut beim Konto an, um dieses Gerät hinzuzufügen."},
            {ConnectionDiagnosisStep.Operator, "Wenden Sie sich an den Server-Administrator."}
        }
        Dim msgs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"bad-credentials", "Der Benutzername oder das Passwort ist falsch."},
            {"bad-credentials-code", "Der Benutzername oder der Einmalcode ist falsch."},
            {"device-revoked", "Dieses Gerät wurde aus dem Konto entfernt."},
            {"signed-out", "Das Passwort wurde geändert, dieses Gerät wurde abgemeldet."},
            {"version-unsupported", "Programm und Server verwenden unterschiedliche Austauschversionen."},
            {"tls-required", "Der Server akzeptiert dies nur über eine geschützte Verbindung."},
            {"id-collision", "Der Server kennt bereits ein anderes Gerät mit dieser Nummer."},
            {"capacity", "Der Server hat sein Limit erreicht."},
            {"port-unavailable", "Der Server hat keinen freien Port für diesen Ordner."},
            {"unavailable", "Das Gerät oder der Server ist derzeit nicht verfügbar."},
            {"rate-limited", "Zu viele Versuche hintereinander."},
            {"protection-unsupported", "Dieser Ordner ist mit einer PIN geschützt."},
            {"invitation-invalid", "Diese Einladung ist ungültig."},
            {"login-taken", "Dieser Benutzername ist bereits vergeben."},
            {"password-policy", "Das Passwort ist zu einfach."},
            {"limit-reached", "Ein Limit auf dem Server wurde erreicht."},
            {"limit-reached-clipboard", "Dieser Server nimmt keine Zwischenablage dieser Größe an."},
            {"not-permitted", "Ein Gastkonto kann dies nicht tun."},
            {"declined", "Das andere Gerät hat den Cast abgelehnt."},
            {"timeout", "Das andere Gerät hat nicht rechtzeitig geantwortet."},
            {"unsupported", "Das andere Gerät kann diese Übertragung nicht abspielen."},
            {"unreachable-server", "Der Server kann nicht erreicht werden."},
            {"unreachable-device", "Das Gerät kann nicht erreicht werden."},
            {"unanswered", "Der Server hat nicht geantwortet."},
            {"tls-failed", "Eine geschützte Verbindung konnte nicht aufgebaut werden."},
            {"certificate-changed", "Dieser Server entspricht nicht dem zuvor verbundenen."},
            {"empty-credential", "Geben Sie zuerst Benutzername und Passwort ein."},
            {"server-offline", "Der Server ist offline."},
            {"resource-locked", "Dieser Ordner ist mit einer PIN gesperrt."},
            {"clipboard-empty", "Die Zwischenablage auf dem Server ist leer."},
            {"share-paused", "Android hat die Freigabe auf diesem Telefon pausiert."},
            {"e2e-missing", "Dieses Gerät kann noch keine geschützten Inhalte empfangen."},
            {"key-changed", "Der Sicherheitsschlüssel dieses Geräts hat sich geändert."},
            {"server-no-e2e", "Dieser Server unterstützt keine geschützten Inhalte."},
            {"sealed-failed", "Konnte auf diesem Gerät nicht geöffnet werden."},
            {"refused", "Der Server hat dies abgelehnt."}
        }
        Dim ui As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"door_button", "Warum verbindet es nicht?.."},
            {"dialog_title", "Verbindungsdiagnose"},
            {"what_happened", "Was passiert ist:"},
            {"what_to_try", "Was Sie versuchen können:"},
            {"btn_next_step", "Nächster Schritt"},
            {"btn_details", "Details"},
            {"btn_copy", "Kopieren"},
            {"btn_close", "Schließen"},
            {"btn_open_settings", "Windows-Einstellungen öffnen"},
            {"check_visibility", "Ist mein Server von außen sichtbar"},
            {"checking", "Prüfung..."},
            {"reachable", "Aus dem Internet erreichbar."},
            {"not_reachable", "Aus dem Internet nicht erreichbar."},
            {"could_not_check", "Erreichbarkeit konnte nicht geprüft werden."},
            {"likely_causes", "Mögliche Ursachen: Portweiterleitung fehlt, Carrier-NAT (CGNAT) oder Firewall."},
            {"codes_copied", "Diagnosecodes in die Zwischenablage kopiert."}
        }
        AddLocale("de", steps, msgs, ui)
    End Sub

    ' --- 5. SPANISH ---
    Private Sub InitializeSpanish()
        Dim steps As New Dictionary(Of ConnectionDiagnosisStep, String) From {
            {ConnectionDiagnosisStep.PcAsleep, "Encienda y active el dispositivo que ejecuta el servidor para evitar que se suspenda."},
            {ConnectionDiagnosisStep.AddressLan, "Verifique la dirección y asegúrese de que ambos dispositivos estén en la misma red Wi-Fi o local."},
            {ConnectionDiagnosisStep.GuestNetwork, "Conéctese a la red Wi-Fi principal, no a la red de invitados del router."},
            {ConnectionDiagnosisStep.Vpn, "Desactive la VPN en este dispositivo momentáneamente y vuelva a intentarlo."},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, "En la computadora con Windows, configure esta red como privada."},
            {ConnectionDiagnosisStep.IpFamily, "Pruebe con otra red (soporte para IPv4 o IPv6)."},
            {ConnectionDiagnosisStep.ServerInternet, "Desde otra red, compruebe si el servidor responde desde Internet."},
            {ConnectionDiagnosisStep.PortForward, "En la configuración del router, redirija el puerto del servidor a esta computadora."},
            {ConnectionDiagnosisStep.DeviceClock, "Configure la fecha y hora correctas en este dispositivo."},
            {ConnectionDiagnosisStep.Certificate, "Confirme la nueva huella digital del servidor o consulte al administrador."},
            {ConnectionDiagnosisStep.Credentials, "Compruebe el usuario y escriba la contraseña nuevamente."},
            {ConnectionDiagnosisStep.SignInRate, "Espere unos segundos e intente de nuevo: el servidor limita los intentos."},
            {ConnectionDiagnosisStep.Invitation, "Verifique la invitación o solicite una nueva."},
            {ConnectionDiagnosisStep.DeviceRemoved, "Inicie sesión nuevamente para volver a agregar este dispositivo."},
            {ConnectionDiagnosisStep.Operator, "Consulte con el administrador del servidor."}
        }
        Dim msgs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"bad-credentials", "El usuario o la contraseña son incorrectos."},
            {"bad-credentials-code", "El usuario o el código de un solo uso son incorrectos."},
            {"device-revoked", "Este dispositivo fue eliminado de la cuenta."},
            {"signed-out", "Se cambió la contraseña de la cuenta y este dispositivo se desconectó."},
            {"version-unsupported", "El programa y el servidor usan versiones de intercambio distintas."},
            {"tls-required", "El servidor solo acepta esto mediante una conexión protegida."},
            {"id-collision", "El servidor ya reconoce otro dispositivo con este identificador."},
            {"capacity", "El servidor ha alcanzado su límite de capacidad."},
            {"port-unavailable", "El servidor no tiene puertos libres para esta carpeta."},
            {"unavailable", "El dispositivo o el servidor no están disponibles ahora."},
            {"rate-limited", "Demasiados intentos seguidos."},
            {"protection-unsupported", "Esta carpeta está bloqueada con un PIN."},
            {"invitation-invalid", "Esta invitación no es válida."},
            {"login-taken", "Este nombre de usuario ya está ocupado."},
            {"password-policy", "La contraseña es demasiado sencilla."},
            {"limit-reached", "Se ha alcanzado un límite en el servidor."},
            {"limit-reached-clipboard", "El servidor no admite un portapapeles de este tamaño."},
            {"not-permitted", "Una cuenta de invitado no puede hacer esto."},
            {"declined", "El otro dispositivo no aceptó la transmisión."},
            {"timeout", "El otro dispositivo no respondió a tiempo."},
            {"unsupported", "El otro dispositivo no admite este tipo de transmisión."},
            {"unreachable-server", "No se puede establecer conexión con el servidor."},
            {"unreachable-device", "No se puede establecer conexión con el dispositivo."},
            {"unanswered", "El servidor no respondió."},
            {"tls-failed", "No se pudo establecer una conexión protegida."},
            {"certificate-changed", "Este servidor no coincide con el conectado anteriormente."},
            {"empty-credential", "Introduzca primero el usuario y la contraseña."},
            {"server-offline", "El servidor está desconectado."},
            {"resource-locked", "Esta carpeta está bloqueada con PIN."},
            {"clipboard-empty", "El portapapeles en el servidor está vacío."},
            {"share-paused", "Android pausó el uso compartido en este teléfono."},
            {"e2e-missing", "Este dispositivo aún no puede recibir contenido protegido."},
            {"key-changed", "La clave de protección de este dispositivo ha cambiado."},
            {"server-no-e2e", "Este servidor no puede transmitir contenido protegido."},
            {"sealed-failed", "No se pudo abrir en este dispositivo."},
            {"refused", "El servidor rechazó esta solicitud."}
        }
        Dim ui As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"door_button", "¿Por qué no conecta?.."},
            {"dialog_title", "Diagnóstico de conexión"},
            {"what_happened", "Qué ocurrió:"},
            {"what_to_try", "Qué intentar:"},
            {"btn_next_step", "Siguiente paso"},
            {"btn_details", "Detalles"},
            {"btn_copy", "Copiar"},
            {"btn_close", "Cerrar"},
            {"btn_open_settings", "Abrir configuración de Windows"},
            {"check_visibility", "¿Es visible mi servidor desde el exterior?"},
            {"checking", "Comprobando..."},
            {"reachable", "Accesible desde Internet."},
            {"not_reachable", "No accesible desde Internet."},
            {"could_not_check", "No se pudo comprobar la visibilidad en este momento."},
            {"likely_causes", "Causas probables: falta redirección de puertos, CGNAT del operador o firewall."},
            {"codes_copied", "Códigos de diagnóstico copiados al portapapeles."}
        }
        AddLocale("es", steps, msgs, ui)
    End Sub

    ' --- 6. FRENCH ---
    Private Sub InitializeFrench()
        Dim steps As New Dictionary(Of ConnectionDiagnosisStep, String) From {
            {ConnectionDiagnosisStep.PcAsleep, "Allumez et réveillez l'appareil exécutant le serveur pour éviter sa mise en veille."},
            {ConnectionDiagnosisStep.AddressLan, "Vérifiez l'adresse et assurez-vous que les deux appareils sont sur le même réseau Wi-Fi."},
            {ConnectionDiagnosisStep.GuestNetwork, "Rejoignez le réseau Wi-Fi principal, pas le réseau invité du routeur."},
            {ConnectionDiagnosisStep.Vpn, "Désactivez temporairement le VPN sur cet appareil et réessayez."},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, "Sur l'ordinateur Windows, configurez ce réseau comme privé."},
            {ConnectionDiagnosisStep.IpFamily, "Essayez un autre réseau (disponibilité IPv4 ou IPv6)."},
            {ConnectionDiagnosisStep.ServerInternet, "Depuis un autre réseau, vérifiez si le serveur répond depuis Internet."},
            {ConnectionDiagnosisStep.PortForward, "Dans les réglages du routeur, redirigez le port du serveur vers cet ordinateur."},
            {ConnectionDiagnosisStep.DeviceClock, "Réglez la date et l'heure correctes sur cet appareil."},
            {ConnectionDiagnosisStep.Certificate, "Confirmez la nouvelle empreinte du serveur ou contactez l'administrateur."},
            {ConnectionDiagnosisStep.Credentials, "Vérifiez l'identifiant et retapez le mot de passe."},
            {ConnectionDiagnosisStep.SignInRate, "Attendez quelques secondes : le serveur limite la cadence de connexion."},
            {ConnectionDiagnosisStep.Invitation, "Vérifiez l'invitation ou demandez-en une nouvelle."},
            {ConnectionDiagnosisStep.DeviceRemoved, "Reconnectez-vous au compte pour réajouter cet appareil."},
            {ConnectionDiagnosisStep.Operator, "Contactez l'administrateur du serveur."}
        }
        Dim msgs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"bad-credentials", "L'identifiant ou le mot de passe est incorrect."},
            {"bad-credentials-code", "L'identifiant ou le code à usage unique est incorrect."},
            {"device-revoked", "Cet appareil a été retiré du compte."},
            {"signed-out", "Le mot de passe a été modifié, cet appareil a été déconnecté."},
            {"version-unsupported", "L'application et le serveur utilisent des versions d'échange différentes."},
            {"tls-required", "Le serveur n'accepte cela que sur une connexion sécurisée."},
            {"id-collision", "Le serveur connaît déjà un autre appareil avec ce numéro."},
            {"capacity", "Le serveur a atteint sa limite de capacité."},
            {"port-unavailable", "Le serveur n'a aucun port libre pour ce dossier."},
            {"unavailable", "L'appareil ou le serveur n'est pas disponible pour le moment."},
            {"rate-limited", "Trop de tentatives consécutives."},
            {"protection-unsupported", "Ce dossier est verrouillé par un code PIN."},
            {"invitation-invalid", "Cette invitation n'est pas valide."},
            {"login-taken", "Cet identifiant est déjà utilisé."},
            {"password-policy", "Le mot de passe est trop simple."},
            {"limit-reached", "Une limite sur le serveur a été atteinte."},
            {"limit-reached-clipboard", "Ce serveur n'accepte pas un presse-papiers de cette taille."},
            {"not-permitted", "Un compte invité ne peut pas effectuer cette action."},
            {"declined", "L'autre appareil a refusé la diffusion."},
            {"timeout", "L'autre appareil n'a pas répondu à temps."},
            {"unsupported", "L'autre appareil ne prend pas en charge cette diffusion."},
            {"unreachable-server", "Impossible de joindre le serveur."},
            {"unreachable-device", "Impossible de joindre l'appareil."},
            {"unanswered", "Le serveur n'a pas répondu."},
            {"tls-failed", "Impossible d'établir une connexion sécurisée."},
            {"certificate-changed", "Ce serveur ne correspond pas à celui précédemment connecté."},
            {"empty-credential", "Saisissez d'abord l'identifiant et le mot de passe."},
            {"server-offline", "Le serveur est hors ligne."},
            {"resource-locked", "Ce dossier est verrouillé par un code PIN."},
            {"clipboard-empty", "Le presse-papiers sur le serveur est vide."},
            {"share-paused", "Android a mis en pause le partage sur ce téléphone."},
            {"e2e-missing", "Cet appareil ne peut pas encore recevoir de contenu protégé."},
            {"key-changed", "La clé de protection de cet appareil a changé."},
            {"server-no-e2e", "Ce serveur ne prend pas en charge le contenu protégé."},
            {"sealed-failed", "Impossible d'ouvrir sur cet appareil."},
            {"refused", "Le serveur a refusé cette requête."}
        }
        Dim ui As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"door_button", "Pourquoi la connexion échoue-t-elle ?.."},
            {"dialog_title", "Diagnostic de connexion"},
            {"what_happened", "Que s'est-il passé :"},
            {"what_to_try", "Que tenter :"},
            {"btn_next_step", "Étape suivante"},
            {"btn_details", "Détails"},
            {"btn_copy", "Copier"},
            {"btn_close", "Fermer"},
            {"btn_open_settings", "Ouvrir les paramètres Windows"},
            {"check_visibility", "Mon serveur est-il visible depuis l'extérieur ?"},
            {"checking", "Vérification..."},
            {"reachable", "Accessible depuis Internet."},
            {"not_reachable", "Non accessible depuis Internet."},
            {"could_not_check", "Impossible de vérifier la visibilité actuellement."},
            {"likely_causes", "Causes probables : redirection de port manquante, CGNAT ou pare-feu."},
            {"codes_copied", "Codes de diagnostic copiés dans le presse-papiers."}
        }
        AddLocale("fr", steps, msgs, ui)
    End Sub

    ' --- 7. ITALIAN ---
    Private Sub InitializeItalian()
        Dim steps As New Dictionary(Of ConnectionDiagnosisStep, String) From {
            {ConnectionDiagnosisStep.PcAsleep, "Accendi e risveglia il dispositivo con il server ed evita che entri in sospensione."},
            {ConnectionDiagnosisStep.AddressLan, "Controlla l'indirizzo e assicurati che entrambi i dispositivi siano sulla stessa rete Wi-Fi."},
            {ConnectionDiagnosisStep.GuestNetwork, "Collegati alla rete Wi-Fi principale, non alla rete ospiti del router."},
            {ConnectionDiagnosisStep.Vpn, "Disattiva temporaneamente la VPN su questo dispositivo e riprova."},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, "Sul computer Windows imposta questa rete come privata."},
            {ConnectionDiagnosisStep.IpFamily, "Prova un'altra rete (disponibilità IPv4 o IPv6)."},
            {ConnectionDiagnosisStep.ServerInternet, "Da un'altra rete verifica se il server risponde da Internet."},
            {ConnectionDiagnosisStep.PortForward, "Nelle impostazioni del router inoltra la porta del server a questo computer."},
            {ConnectionDiagnosisStep.DeviceClock, "Imposta data e ora corrette su questo dispositivo."},
            {ConnectionDiagnosisStep.Certificate, "Conferma la nuova impronta del server o chiedi all'amministratore."},
            {ConnectionDiagnosisStep.Credentials, "Verifica nome utente e password e riprova."},
            {ConnectionDiagnosisStep.SignInRate, "Attendi qualche secondo: il server limita i tentativi di accesso."},
            {ConnectionDiagnosisStep.Invitation, "Verifica l'invito o richiedine uno nuovo."},
            {ConnectionDiagnosisStep.DeviceRemoved, "Accedi di nuovo all'account per aggiungere questo dispositivo."},
            {ConnectionDiagnosisStep.Operator, "Rivolgiti all'amministratore del server."}
        }
        Dim msgs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"bad-credentials", "Nome utente o password non corretti."},
            {"bad-credentials-code", "Nome utente o codice monouso non corretti."},
            {"device-revoked", "Questo dispositivo è stato rimosso dall'account."},
            {"signed-out", "La password è stata modificata, il dispositivo è stato disconnesso."},
            {"version-unsupported", "Applicazione e server usano versioni di scambio diverse."},
            {"tls-required", "Il server accetta solo connessioni protette."},
            {"id-collision", "Il server riconosce già un altro dispositivo con questo identificativo."},
            {"capacity", "Il server ha raggiunto il limite di capacità."},
            {"port-unavailable", "Il server non ha porte libere per questa cartella."},
            {"unavailable", "Dispositivo o server al momento non disponibili."},
            {"rate-limited", "Troppi tentativi consecutivi."},
            {"protection-unsupported", "Questa cartella è protetta da un PIN."},
            {"invitation-invalid", "Questo invito non è valido."},
            {"login-taken", "Nome utente già occupato."},
            {"password-policy", "La password è troppo semplice."},
            {"limit-reached", "È stato raggiunto un limite sul server."},
            {"limit-reached-clipboard", "Il server non supporta appunti di queste dimensioni."},
            {"not-permitted", "Un account ospite non può eseguire questa operazione."},
            {"declined", "L'altro dispositivo ha rifiutato la trasmissione."},
            {"timeout", "L'altro dispositivo non ha risposto in tempo."},
            {"unsupported", "L'altro dispositivo non supporta questo tipo di trasmissione."},
            {"unreachable-server", "Impossibile raggiungere il server."},
            {"unreachable-device", "Impossibile raggiungere il dispositivo."},
            {"unanswered", "Il server non ha risposto."},
            {"tls-failed", "Impossibile stabilire una connessione protetta."},
            {"certificate-changed", "Questo server non corrisponde a quello a cui ti eri connesso prima."},
            {"empty-credential", "Inserisci prima nome utente e password."},
            {"server-offline", "Il server è offline."},
            {"resource-locked", "Questa cartella è bloccata con PIN."},
            {"clipboard-empty", "Gli appunti sul server sono vuoti."},
            {"share-paused", "Android ha sospeso la condivisione su questo telefono."},
            {"e2e-missing", "Questo dispositivo non può ancora ricevere contenuti protetti."},
            {"key-changed", "La chiave di protezione di questo dispositivo è cambiata."},
            {"server-no-e2e", "Questo server non supporta contenuti protetti."},
            {"sealed-failed", "Impossibile aprire su questo dispositivo."},
            {"refused", "Il server ha rifiutato la richiesta."}
        }
        Dim ui As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"door_button", "Perché non si connette?.."},
            {"dialog_title", "Diagnosi di connessione"},
            {"what_happened", "Cosa è successo:"},
            {"what_to_try", "Cosa provare:"},
            {"btn_next_step", "Passaggio successivo"},
            {"btn_details", "Dettagli"},
            {"btn_copy", "Copia"},
            {"btn_close", "Chiudi"},
            {"btn_open_settings", "Apri impostazioni di Windows"},
            {"check_visibility", "Il mio server è visibile dall'esterno?"},
            {"checking", "Controllo in corso..."},
            {"reachable", "Raggiungibile da Internet."},
            {"not_reachable", "Non raggiungibile da Internet."},
            {"could_not_check", "Impossibile verificare la visibilità ora."},
            {"likely_causes", "Possibili cause: inoltro porte mancante, CGNAT o firewall."},
            {"codes_copied", "Codici di diagnosi copiati negli appunti."}
        }
        AddLocale("it", steps, msgs, ui)
    End Sub

    ' --- 8. JAPANESE ---
    Private Sub InitializeJapanese()
        Dim steps As New Dictionary(Of ConnectionDiagnosisStep, String) From {
            {ConnectionDiagnosisStep.PcAsleep, "サーバーが稼働している機器の電源を入れ、スリープしないように設定してください。"},
            {ConnectionDiagnosisStep.AddressLan, "アドレスを確認し、両方の機器が同一のWi-Fiまたはホームネットワークにあることを確認してください。"},
            {ConnectionDiagnosisStep.GuestNetwork, "ルーターのゲストネットワークではなく、メインのWi-Fiネットワークに接続してください。"},
            {ConnectionDiagnosisStep.Vpn, "この機器のVPNを一時的にオフにして再試行してください。"},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, "Windows PCでこのネットワークをプライベートネットワークに設定してください。"},
            {ConnectionDiagnosisStep.IpFamily, "別のネットワーク（IPv4またはIPv6の利用可否）をお試しください。"},
            {ConnectionDiagnosisStep.ServerInternet, "モバイルデータなどの別ネットワークから、サーバーが応答するか確認してください。"},
            {ConnectionDiagnosisStep.PortForward, "ルーターの設定でサーバーのポートをこのPCに転送してください。"},
            {ConnectionDiagnosisStep.DeviceClock, "この機器の日時を正しく設定するか、自動設定を有効にしてください。"},
            {ConnectionDiagnosisStep.Certificate, "新しい証明書の指紋を確認するか、サーバー管理者に問い合わせてください。"},
            {ConnectionDiagnosisStep.Credentials, "ログイン名とパスワードを確認して再入力してください。"},
            {ConnectionDiagnosisStep.SignInRate, "数秒待ってから再試行してください（アクセス頻度制限）。"},
            {ConnectionDiagnosisStep.Invitation, "招待を確認するか、新しい招待を要求してください。"},
            {ConnectionDiagnosisStep.DeviceRemoved, "アカウントに再度サインインして、この機器を再登録してください。"},
            {ConnectionDiagnosisStep.Operator, "サーバー管理者に問い合わせてください。"}
        }
        Dim msgs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"bad-credentials", "ログイン名またはパスワードが正しくありません。"},
            {"bad-credentials-code", "ログイン名またはワンタイムコードが正しくありません。"},
            {"device-revoked", "この機器はアカウントから削除されました。"},
            {"signed-out", "パスワードが変更されたため、サインアウトされました。"},
            {"version-unsupported", "アプリとサーバーで交換バージョンの不一致があります。"},
            {"tls-required", "サーバーは保護された接続のみを受け入れます。"},
            {"id-collision", "サーバー上に同じ番号の別機器がすでに存在します。"},
            {"capacity", "サーバーの制限に達しました。"},
            {"port-unavailable", "このフォルダーに使用できる空きポートがありません。"},
            {"unavailable", "機器またはサーバーが現在利用できません。"},
            {"rate-limited", "短時間の試行回数が多すぎます。"},
            {"protection-unsupported", "このフォルダーはPINでロックされています。"},
            {"invitation-invalid", "この招待は無効です。"},
            {"login-taken", "このログイン名は既に使用されています。"},
            {"password-policy", "パスワードが簡単すぎます。"},
            {"limit-reached", "サーバーの設定上限に達しました。"},
            {"limit-reached-clipboard", "このサーバーはこのサイズのクリップボードを受け入れません。"},
            {"not-permitted", "ゲストアカウントではこの操作を実行できません。"},
            {"declined", "相手の機器がキャストを拒否しました。"},
            {"timeout", "相手の機器から時間内に応答がありませんでした。"},
            {"unsupported", "相手の機器はこの配信の再生に対応していません。"},
            {"unreachable-server", "サーバーに接続できません。"},
            {"unreachable-device", "機器に接続できません。"},
            {"unanswered", "サーバーから応答がありませんでした。"},
            {"tls-failed", "保護された接続を確立できませんでした。"},
            {"certificate-changed", "このサーバーは以前接続したものと一致しません。"},
            {"empty-credential", "まずログイン名とパスワードを入力してください。"},
            {"server-offline", "サーバーはオフラインです。"},
            {"resource-locked", "フォルダーはPINでロックされています。"},
            {"clipboard-empty", "サーバー上のクリップボードは空です。"},
            {"share-paused", "Androidが共有を一時停止しました。"},
            {"e2e-missing", "この機器は保護されたコンテンツを受信できません。"},
            {"key-changed", "この機器の保護キーが変更されました。"},
            {"server-no-e2e", "このサーバーは保護されたコンテンツを送信できません。"},
            {"sealed-failed", "この機器では開けませんでした。"},
            {"refused", "サーバーが接続を拒否しました。"}
        }
        Dim ui As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"door_button", "接続できない原因を調べる.."},
            {"dialog_title", "接続診断"},
            {"what_happened", "発生した現象:"},
            {"what_to_try", "対処方法:"},
            {"btn_next_step", "次のステップ"},
            {"btn_details", "詳細"},
            {"btn_copy", "コピー"},
            {"btn_close", "閉じる"},
            {"btn_open_settings", "Windowsの設定を開く"},
            {"check_visibility", "外部からサーバーが見えるか確認"},
            {"checking", "確認中..."},
            {"reachable", "インターネットから接続可能です。"},
            {"not_reachable", "インターネットから接続できません。"},
            {"could_not_check", "現在外部からの接続確認ができません。"},
            {"likely_causes", "考えられる原因: ルーターのポート開放未設定、CGNAT、ファイアウォール。"},
            {"codes_copied", "診断コードをクリップボードにコピーしました。"}
        }
        AddLocale("ja", steps, msgs, ui)
    End Sub

    ' --- 9. KOREAN ---
    Private Sub InitializeKorean()
        Dim steps As New Dictionary(Of ConnectionDiagnosisStep, String) From {
            {ConnectionDiagnosisStep.PcAsleep, "서버가 실행 중인 기기를 켜고 절전 모드로 들어가지 않도록 설정하세요."},
            {ConnectionDiagnosisStep.AddressLan, "주소를 확인하고 두 기기가 동일한 Wi-Fi 또는 홈 네트워크에 있는지 확인하세요."},
            {ConnectionDiagnosisStep.GuestNetwork, "공유기의 게스트 네트워크 대신 기본 Wi-Fi 네트워크에 연결하세요."},
            {ConnectionDiagnosisStep.Vpn, "이 기기의 VPN을 잠시 끄고 다시 시도하세요."},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, "Windows 컴퓨터에서 이 네트워크를 개인 네트워크로 설정하세요."},
            {ConnectionDiagnosisStep.IpFamily, "다른 네트워크(IPv4 또는 IPv6 지원 여부)를 이용해 보세요."},
            {ConnectionDiagnosisStep.ServerInternet, "모바일 데이터 등 다른 네트워크에서 서버가 인터넷으로 응답하는지 확인하세요."},
            {ConnectionDiagnosisStep.PortForward, "공유기 설정에서 서버 포트를 이 컴퓨터로 포트포워딩하세요."},
            {ConnectionDiagnosisStep.DeviceClock, "이 기기의 날짜와 시간을 올바르게 설정하거나 자동 시간 설정을 켜세요."},
            {ConnectionDiagnosisStep.Certificate, "새 서버 인증서 지문을 확인하거나 서버 관리자에게 문의하세요."},
            {ConnectionDiagnosisStep.Credentials, "아이디와 비밀번호를 확인하고 다시 입력하세요."},
            {ConnectionDiagnosisStep.SignInRate, "몇 초 기다린 후 다시 시도하세요(시도 횟수 제한)."},
            {ConnectionDiagnosisStep.Invitation, "초대를 확인하거나 새 초대를 요청하세요."},
            {ConnectionDiagnosisStep.DeviceRemoved, "계정에 다시 로그인하여 이 기기를 다시 추가하세요."},
            {ConnectionDiagnosisStep.Operator, "서버 관리자에게 문의하세요."}
        }
        Dim msgs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"bad-credentials", "아이디 또는 비밀번호가 잘못되었습니다."},
            {"bad-credentials-code", "아이디 또는 일회용 코드가 잘못되었습니다."},
            {"device-revoked", "이 기기는 계정에서 삭제되었습니다."},
            {"signed-out", "비밀번호가 변경되어 로그아웃되었습니다."},
            {"version-unsupported", "프로그램과 서버의 교환 프로토콜 버전이 다릅니다."},
            {"tls-required", "서버는 보안 연결만 허용합니다."},
            {"id-collision", "서버에 이미 동일한 번호의 기기가 등록되어 있습니다."},
            {"capacity", "서버 용량 한도에 도달했습니다."},
            {"port-unavailable", "이 폴더에 사용할 수 있는 포트가 서버에 없습니다."},
            {"unavailable", "기기 또는 서버를 현재 사용할 수 없습니다."},
            {"rate-limited", "시도 횟수가 너무 많습니다."},
            {"protection-unsupported", "이 폴더는 PIN으로 잠겨 있습니다."},
            {"invitation-invalid", "이 초대는 유효하지 않습니다."},
            {"login-taken", "이미 사용 중인 아이디입니다."},
            {"password-policy", "비밀번호가 너무 단순합니다."},
            {"limit-reached", "서버 제한 한도에 도달했습니다."},
            {"limit-reached-clipboard", "이 서버는 이 크기의 클립보드를 허용하지 않습니다."},
            {"not-permitted", "게스트 계정에서는 이 작업을 수행할 수 없습니다."},
            {"declined", "상대 기기가 전송(cast)을 수락하지 않았습니다."},
            {"timeout", "상대 기기가 제시간에 응답하지 않았습니다."},
            {"unsupported", "상대 기기에서 이 방송 재생을 지원하지 않습니다."},
            {"unreachable-server", "서버에 연결할 수 없습니다."},
            {"unreachable-device", "기기에 연결할 수 없습니다."},
            {"unanswered", "서버가 응답하지 않았습니다."},
            {"tls-failed", "보안 연결을 설정할 수 없습니다."},
            {"certificate-changed", "이전에 연결했던 서버와 일치하지 않습니다."},
            {"empty-credential", "먼저 아이디와 비밀번호를 입력하세요."},
            {"server-offline", "서버가 오프라인 상태입니다."},
            {"resource-locked", "폴더가 PIN으로 잠겨 있습니다."},
            {"clipboard-empty", "서버 클립보드가 비어 있습니다."},
            {"share-paused", "Android에서 이 휴대폰의 공유를 일시 중지했습니다."},
            {"e2e-missing", "이 기기는 아직 보호된 콘텐츠를 수신할 수 없습니다."},
            {"key-changed", "이 기기의 보안 키가 변경되었습니다."},
            {"server-no-e2e", "이 서버는 보호된 콘텐츠를 전달할 수 없습니다."},
            {"sealed-failed", "이 기기에서 열 수 없습니다."},
            {"refused", "서버가 요청을 거부했습니다."}
        }
        Dim ui As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"door_button", "연결되지 않는 이유 확인.."},
            {"dialog_title", "연결 진단"},
            {"what_happened", "발생한 문제:"},
            {"what_to_try", "권장 해결 방법:"},
            {"btn_next_step", "다음 단계"},
            {"btn_details", "자세히"},
            {"btn_copy", "복사"},
            {"btn_close", "닫기"},
            {"btn_open_settings", "Windows 설정 열기"},
            {"check_visibility", "내 서버 외부 연결 가능 여부 확인"},
            {"checking", "확인 중..."},
            {"reachable", "인터넷에서 접근 가능합니다."},
            {"not_reachable", "인터넷에서 접근할 수 없습니다."},
            {"could_not_check", "현재 외부 접근성을 확인할 수 없습니다."},
            {"likely_causes", "예상 원인: 공유기 포트포워딩 누락, 통신사 CGNAT 또는 방화벽."},
            {"codes_copied", "진단 코드가 클립보드에 복사되었습니다."}
        }
        AddLocale("ko", steps, msgs, ui)
    End Sub

    ' --- 10. POLISH ---
    Private Sub InitializePolish()
        Dim steps As New Dictionary(Of ConnectionDiagnosisStep, String) From {
            {ConnectionDiagnosisStep.PcAsleep, "Włącz i wybudź urządzenie z serwerem, aby nie przechodziło w stan uśpienia."},
            {ConnectionDiagnosisStep.AddressLan, "Sprawdź adres i upewnij się, że oba urządzenia są w tej samej sieci Wi-Fi."},
            {ConnectionDiagnosisStep.GuestNetwork, "Połącz się z główną siecią Wi-Fi, a nie z siecią gościnną routera."},
            {ConnectionDiagnosisStep.Vpn, "Wyłącz na chwilę VPN na tym urządzeniu i spróbuj ponownie."},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, "Na komputerze z systemem Windows zmień typ sieci na prywatną."},
            {ConnectionDiagnosisStep.IpFamily, "Wypróbuj inną sieć (obsługa adresów IPv4 lub IPv6)."},
            {ConnectionDiagnosisStep.ServerInternet, "Z innej sieci sprawdź, czy serwer odpowiada z Internetu."},
            {ConnectionDiagnosisStep.PortForward, "W ustawieniach routera przekieruj port serwera na ten komputer."},
            {ConnectionDiagnosisStep.DeviceClock, "Ustaw prawidłową datę i godzinę na tym urządzeniu."},
            {ConnectionDiagnosisStep.Certificate, "Potwierdź nowy odcisk palca serwera lub skontaktuj się z administratorem."},
            {ConnectionDiagnosisStep.Credentials, "Sprawdź login i hasło, a następnie spróbuj ponownie."},
            {ConnectionDiagnosisStep.SignInRate, "Odczekaj kilka sekund i spróbuj ponownie (limit zapytań)."},
            {ConnectionDiagnosisStep.Invitation, "Sprawdź zaproszenie lub poproś o nowe."},
            {ConnectionDiagnosisStep.DeviceRemoved, "Zaloguj się ponownie do konta, aby dodać to urządzenie."},
            {ConnectionDiagnosisStep.Operator, "Skontaktuj się z administratorem serwera."}
        }
        Dim msgs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"bad-credentials", "Nieprawidłowy login lub hasło."},
            {"bad-credentials-code", "Nieprawidłowy login lub kod jednorazowy."},
            {"device-revoked", "To urządzenie zostało usunięte z konta."},
            {"signed-out", "Hasło zostało zmienione, urządzenie zostało wylogowane."},
            {"version-unsupported", "Program i serwer używają różnych wersji wymiany."},
            {"tls-required", "Serwer wymaga połączenia szyfrowanego."},
            {"id-collision", "Serwer rozpoznaje już inne urządzenie o tym numerze."},
            {"capacity", "Osiągnięto limit pojemności serwera."},
            {"port-unavailable", "Serwer nie ma wolnego portu dla tego folderu."},
            {"unavailable", "Urządzenie lub serwer jest obecnie niedostępny."},
            {"rate-limited", "Zbyt wiele prób z rzędu."},
            {"protection-unsupported", "Ten folder jest zablokowany kodem PIN."},
            {"invitation-invalid", "To zaproszenie jest nieprawidłowe."},
            {"login-taken", "Ten login jest już zajęty."},
            {"password-policy", "Hasło jest zbyt proste."},
            {"limit-reached", "Osiągnięto limit na serwerze."},
            {"limit-reached-clipboard", "Serwer nie akceptuje schowka o tym rozmiarze."},
            {"not-permitted", "Konto gościa nie ma uprawnień do tej operacji."},
            {"declined", "Drugie urządzenie odrzuciło transmisję."},
            {"timeout", "Drugie urządzenie nie odpowiedziało na czas."},
            {"unsupported", "Drugie urządzenie nie obsługuje tego typu transmisji."},
            {"unreachable-server", "Nie można nawiązać połączenia z serwerem."},
            {"unreachable-device", "Nie można nawiązać połączenia z urządzeniem."},
            {"unanswered", "Serwer nie odpowiedział."},
            {"tls-failed", "Nie udało się nawiązać bezpiecznego połączenia."},
            {"certificate-changed", "Ten serwer różni się od połączonego wcześniej."},
            {"empty-credential", "Najpierw wprowadź login i hasło."},
            {"server-offline", "Serwer jest offline."},
            {"resource-locked", "Folder jest zablokowany kodem PIN."},
            {"clipboard-empty", "Schowek na serwerze jest pusty."},
            {"share-paused", "System Android wstrzymał udostępnianie na tym telefonie."},
            {"e2e-missing", "To urządzenie nie obsługuje jeszcze chronionej zawartości."},
            {"key-changed", "Klucz zabezpieczeń tego urządzenia uległ zmianie."},
            {"server-no-e2e", "Ten serwer nie obsługuje chronionej zawartości."},
            {"sealed-failed", "Nie udało się otworzyć na tym urządzeniu."},
            {"refused", "Serwer odrzucił żądanie."}
        }
        Dim ui As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"door_button", "Dlaczego się nie łączy?.."},
            {"dialog_title", "Diagnostyka połączenia"},
            {"what_happened", "Co się stało:"},
            {"what_to_try", "Co zrobić:"},
            {"btn_next_step", "Następny krok"},
            {"btn_details", "Szczegóły"},
            {"btn_copy", "Kopiuj"},
            {"btn_close", "Zamknij"},
            {"btn_open_settings", "Otwórz ustawienia systemu Windows"},
            {"check_visibility", "Czy mój serwer jest widoczny z zewnątrz"},
            {"checking", "Sprawdzanie..."},
            {"reachable", "Dostępny z Internetu."},
            {"not_reachable", "Niedostępny z Internetu."},
            {"could_not_check", "Nie można teraz sprawdzić dostępności."},
            {"likely_causes", "Prawdopodobne przyczyny: brak przekierowania portów, CGNAT lub zapora sieciowa."},
            {"codes_copied", "Kody diagnostyczne skopiowano do schowka."}
        }
        AddLocale("pl", steps, msgs, ui)
    End Sub

    ' --- 11. PORTUGUESE ---
    Private Sub InitializePortuguese()
        Dim steps As New Dictionary(Of ConnectionDiagnosisStep, String) From {
            {ConnectionDiagnosisStep.PcAsleep, "Ligue e ative o dispositivo que executa o servidor para evitar que ele suspenda."},
            {ConnectionDiagnosisStep.AddressLan, "Verifique o endereço e certifique-se de que ambos os dispositivos estejam na mesma rede Wi-Fi."},
            {ConnectionDiagnosisStep.GuestNetwork, "Conecte-se à rede Wi-Fi principal, não à rede de convidados do roteador."},
            {ConnectionDiagnosisStep.Vpn, "Desative a VPN neste dispositivo por um momento e tente novamente."},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, "No computador Windows, altere esta rede para privada."},
            {ConnectionDiagnosisStep.IpFamily, "Tente outra rede (suporte a endereços IPv4 ou IPv6)."},
            {ConnectionDiagnosisStep.ServerInternet, "A partir de outra rede, verifique se o servidor responde pela Internet."},
            {ConnectionDiagnosisStep.PortForward, "Nas configurações do roteador, encaminhe a porta do servidor para este computador."},
            {ConnectionDiagnosisStep.DeviceClock, "Defina a data e a hora corretas neste dispositivo."},
            {ConnectionDiagnosisStep.Certificate, "Confirme a nova impressão digital do servidor ou consulte o administrador."},
            {ConnectionDiagnosisStep.Credentials, "Verifique o usuário e digite a senha novamente."},
            {ConnectionDiagnosisStep.SignInRate, "Aguarde alguns segundos e tente novamente (limite de tentativas)."},
            {ConnectionDiagnosisStep.Invitation, "Verifique o convite ou solicite um novo."},
            {ConnectionDiagnosisStep.DeviceRemoved, "Entre na conta novamente para adicionar este dispositivo de volta."},
            {ConnectionDiagnosisStep.Operator, "Consulte o administrador do servidor."}
        }
        Dim msgs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"bad-credentials", "Usuário ou senha incorretos."},
            {"bad-credentials-code", "Usuário ou código de uso único incorretos."},
            {"device-revoked", "Este dispositivo foi removido da conta."},
            {"signed-out", "A senha foi alterada e este dispositivo foi desconectado."},
            {"version-unsupported", "O aplicativo e o servidor usam versões de troca diferentes."},
            {"tls-required", "O servidor aceita apenas conexões protegidas."},
            {"id-collision", "O servidor já conhece outro dispositivo com esse identificador."},
            {"capacity", "O servidor atingiu o limite de capacidade."},
            {"port-unavailable", "O servidor não tem portas livres para esta pasta."},
            {"unavailable", "Dispositivo ou servidor indisponível no momento."},
            {"rate-limited", "Muitas tentativas consecutivas."},
            {"protection-unsupported", "Esta pasta está protegida por PIN."},
            {"invitation-invalid", "Este convite é inválido."},
            {"login-taken", "Este nome de usuário já está em uso."},
            {"password-policy", "A senha é muito simples."},
            {"limit-reached", "Um limite no servidor foi atingido."},
            {"limit-reached-clipboard", "O servidor não aceita uma área de transferência deste tamanho."},
            {"not-permitted", "Uma conta de convidado não pode fazer isso."},
            {"declined", "O outro dispositivo recusou a transmissão."},
            {"timeout", "O outro dispositivo não respondeu a tempo."},
            {"unsupported", "O outro dispositivo não suporta esta transmissão."},
            {"unreachable-server", "Não foi possível conectar ao servidor."},
            {"unreachable-device", "Não foi possível conectar ao dispositivo."},
            {"unanswered", "O servidor não respondeu."},
            {"tls-failed", "Não foi possível estabelecer uma conexão protegida."},
            {"certificate-changed", "Este servidor não coincide com o conectado anteriormente."},
            {"empty-credential", "Digite primeiro o usuário e a senha."},
            {"server-offline", "O servidor está offline."},
            {"resource-locked", "A pasta está bloqueada por PIN."},
            {"clipboard-empty", "A área de transferência no servidor está vazia."},
            {"share-paused", "O Android pausou o compartilhamento neste telefone."},
            {"e2e-missing", "Este dispositivo ainda não pode receber conteúdo protegido."},
            {"key-changed", "A chave de segurança deste dispositivo mudou."},
            {"server-no-e2e", "Este servidor não suporta conteúdo protegido."},
            {"sealed-failed", "Não foi possível abrir neste dispositivo."},
            {"refused", "O servidor recusou a solicitação."}
        }
        Dim ui As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"door_button", "Por que não conecta?.."},
            {"dialog_title", "Diagnóstico de conexão"},
            {"what_happened", "O que aconteceu:"},
            {"what_to_try", "O que tentar:"},
            {"btn_next_step", "Próximo passo"},
            {"btn_details", "Detalhes"},
            {"btn_copy", "Copiar"},
            {"btn_close", "Fechar"},
            {"btn_open_settings", "Abrir configurações do Windows"},
            {"check_visibility", "Meu servidor é visível externamente?"},
            {"checking", "Verificando..."},
            {"reachable", "Acessível pela Internet."},
            {"not_reachable", "Inacessível pela Internet."},
            {"could_not_check", "Não foi possível verificar a visibilidade agora."},
            {"likely_causes", "Causas prováveis: falta de encaminhamento de portas, CGNAT ou firewall."},
            {"codes_copied", "Códigos de diagnóstico copiados para a área de transferência."}
        }
        AddLocale("pt", steps, msgs, ui)
    End Sub

    ' --- 12. TURKISH ---
    Private Sub InitializeTurkish()
        Dim steps As New Dictionary(Of ConnectionDiagnosisStep, String) From {
            {ConnectionDiagnosisStep.PcAsleep, "Sunucuyu çalıştıran cihazı açın ve uyku moduna geçmesini engelleyin."},
            {ConnectionDiagnosisStep.AddressLan, "Adresi kontrol edin ve her iki cihazın da aynı Wi-Fi ağında olduğundan emin olun."},
            {ConnectionDiagnosisStep.GuestNetwork, "Yönlendiricinin misafir ağı yerine ana Wi-Fi ağına bağlanın."},
            {ConnectionDiagnosisStep.Vpn, "Bu cihazdaki VPN'i geçici olarak kapatıp tekrar deneyin."},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, "Windows bilgisayarda bu ağı özel ağ olarak ayarlayın."},
            {ConnectionDiagnosisStep.IpFamily, "Farklı bir ağ deneyin (IPv4 veya IPv6 kullanılabilirliği)."},
            {ConnectionDiagnosisStep.ServerInternet, "Farklı bir ağdan sunucunun İnternet üzerinden yanıt verip vermediğini kontrol edin."},
            {ConnectionDiagnosisStep.PortForward, "Yönlendirici ayarlarında sunucu bağlantı noktasını bu bilgisayara yönlendirin."},
            {ConnectionDiagnosisStep.DeviceClock, "Bu cihazda tarih ve saati doğru ayarlayın."},
            {ConnectionDiagnosisStep.Certificate, "Sunucunun yeni parmak izini onaylayın veya yöneticiye danışın."},
            {ConnectionDiagnosisStep.Credentials, "Kullanıcı adını ve parolayı kontrol edip tekrar girin."},
            {ConnectionDiagnosisStep.SignInRate, "Birkaç saniye bekleyip tekrar deneyin (istek sıklığı sınırı)."},
            {ConnectionDiagnosisStep.Invitation, "Daveti kontrol edin veya yeni bir davet isteyin."},
            {ConnectionDiagnosisStep.DeviceRemoved, "Bu cihazı tekrar eklemek için hesaba yeniden giriş yapın."},
            {ConnectionDiagnosisStep.Operator, "Sunucu yöneticisine başvurun."}
        }
        Dim msgs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"bad-credentials", "Kullanıcı adı veya parola hatalı."},
            {"bad-credentials-code", "Kullanıcı adı veya tek kullanımlık kod hatalı."},
            {"device-revoked", "Bu cihaz hesaptan kaldırıldı."},
            {"signed-out", "Parola değiştirildi, bu cihazın oturumu kapatıldı."},
            {"version-unsupported", "Uygulama ve sunucu farklı değişim sürümleri kullanıyor."},
            {"tls-required", "Sunucu bunu yalnızca korumalı bağlantı üzerinden kabul eder."},
            {"id-collision", "Sunucu bu numaraya sahip başka bir cihazı zaten tanıyor."},
            {"capacity", "Sunucu kapasite sınırına ulaştı."},
            {"port-unavailable", "Sunucuda bu klasör için boş bağlantı noktası yok."},
            {"unavailable", "Cihaz veya sunucu şu anda kullanılamıyor."},
            {"rate-limited", "Üst üste çok fazla deneme yapıldı."},
            {"protection-unsupported", "Bu klasör PIN ile kilitlenmiştir."},
            {"invitation-invalid", "Bu davet geçerli değil."},
            {"login-taken", "Bu kullanıcı adı zaten alınmış."},
            {"password-policy", "Parola çok basit."},
            {"limit-reached", "Sunucuda belirlenen sınıra ulaşıldı."},
            {"limit-reached-clipboard", "Sunucu bu boyutta bir panoyu kabul etmiyor."},
            {"not-permitted", "Misafir hesabı bu işlemi yapamaz."},
            {"declined", "Diğer cihaz yayını kabul etmedi."},
            {"timeout", "Diğer cihaz zamanında yanıt vermedi."},
            {"unsupported", "Diğer cihaz bu yayın türünü oynatamıyor."},
            {"unreachable-server", "Sunucuya ulaşılamıyor."},
            {"unreachable-device", "Cihaza ulaşılamıyor."},
            {"unanswered", "Sunucu yanıt vermedi."},
            {"tls-failed", "Korumalı bağlantı kurulamadı."},
            {"certificate-changed", "Bu sunucu daha önce bağlandığınız sunucuyla eşleşmiyor."},
            {"empty-credential", "Önce kullanıcı adını ve parolayı girin."},
            {"server-offline", "Sunucu çevrimdışı."},
            {"resource-locked", "Klasör PIN ile kilitlenmiştir."},
            {"clipboard-empty", "Sunucudaki pano boş."},
            {"share-paused", "Android bu telefonda paylaşımı duraklattı."},
            {"e2e-missing", "Bu cihaz henüz korumalı içerik alamıyor."},
            {"key-changed", "Bu cihazın koruma anahtarı değişti."},
            {"server-no-e2e", "Bu sunucu korumalı içerik aktaramıyor."},
            {"sealed-failed", "Bu cihazda açılamadı."},
            {"refused", "Sunucu isteği reddetti."}
        }
        Dim ui As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"door_button", "Neden bağlanmıyor?.."},
            {"dialog_title", "Bağlantı Tanısı"},
            {"what_happened", "Ne oldu:"},
            {"what_to_try", "Ne denenmeli:"},
            {"btn_next_step", "Sonraki adım"},
            {"btn_details", "Ayrıntılar"},
            {"btn_copy", "Kopyala"},
            {"btn_close", "Kapat"},
            {"btn_open_settings", "Windows Ayarlarını Aç"},
            {"check_visibility", "Sunucum dışarıdan görünüyor mu?"},
            {"checking", "Kontrol ediliyor..."},
            {"reachable", "İnternet üzerinden erişilebilir."},
            {"not_reachable", "İnternet üzerinden erişilemiyor."},
            {"could_not_check", "Erişilebilirlik şu anda kontrol edilemedi."},
            {"likely_causes", "Olası nedenler: bağlantı noktası yönlendirme eksik, CGNAT veya güvenlik duvarı."},
            {"codes_copied", "Tanı kodları panoya kopyalandı."}
        }
        AddLocale("tr", steps, msgs, ui)
    End Sub

    ' --- 13. CHINESE ---
    Private Sub InitializeChinese()
        Dim steps As New Dictionary(Of ConnectionDiagnosisStep, String) From {
            {ConnectionDiagnosisStep.PcAsleep, "打开并唤醒运行服务器的设备，并防止其进入睡眠状态。"},
            {ConnectionDiagnosisStep.AddressLan, "检查地址，并确保两台设备在同一个 Wi-Fi 或家庭网络中。"},
            {ConnectionDiagnosisStep.GuestNetwork, "连接到主 Wi-Fi 网络，而非路由器的访客网络。"},
            {ConnectionDiagnosisStep.Vpn, "在此设备上暂时关闭 VPN，然后重试。"},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, "在 Windows 电脑上将此网络设置为专用网络。"},
            {ConnectionDiagnosisStep.IpFamily, "尝试其他网络（IPv4 或 IPv6 可用性）。"},
            {ConnectionDiagnosisStep.ServerInternet, "从其他网络（如移动数据）检查服务器是否能从互联网响应。"},
            {ConnectionDiagnosisStep.PortForward, "在路由器设置中将服务器端口转发到此电脑。"},
            {ConnectionDiagnosisStep.DeviceClock, "在此设备上设置正确的日期和时间。"},
            {ConnectionDiagnosisStep.Certificate, "确认新的服务器指纹或咨询服务器管理员。"},
            {ConnectionDiagnosisStep.Credentials, "检查登录名并重新输入密码。"},
            {ConnectionDiagnosisStep.SignInRate, "等待几秒钟后重试（请求频率限制）。"},
            {ConnectionDiagnosisStep.Invitation, "检查邀请或请求新邀请。"},
            {ConnectionDiagnosisStep.DeviceRemoved, "重新登录账户以将此设备添加回来。"},
            {ConnectionDiagnosisStep.Operator, "请联系服务器管理员。"}
        }
        Dim msgs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"bad-credentials", "登录名或密码错误。"},
            {"bad-credentials-code", "登录名或一次性代码错误。"},
            {"device-revoked", "此设备已从账户中移除。"},
            {"signed-out", "密码已更改，此设备已退出登录。"},
            {"version-unsupported", "此程序与服务器使用的交换版本不同。"},
            {"tls-required", "服务器仅通过受保护的连接接受此操作。"},
            {"id-collision", "服务器已知存在相同编号的另一台设备。"},
            {"capacity", "服务器已达到容量限制。"},
            {"port-unavailable", "服务器没有可用于此文件夹的空闲端口。"},
            {"unavailable", "设备或服务器目前不可用。"},
            {"rate-limited", "连续尝试次数过多。"},
            {"protection-unsupported", "此文件夹已通过 PIN 锁定。"},
            {"invitation-invalid", "此邀请无效。"},
            {"login-taken", "此登录名已被使用。"},
            {"password-policy", "密码过于简单。"},
            {"limit-reached", "已达到服务器上的限制。"},
            {"limit-reached-clipboard", "此服务器不接受此大小的剪贴板。"},
            {"not-permitted", "访客账户无法执行此操作。"},
            {"declined", "另一台设备拒绝了投屏。"},
            {"timeout", "另一台设备未及时响应。"},
            {"unsupported", "另一台设备不支持播放此类型的广播。"},
            {"unreachable-server", "无法连接到服务器。"},
            {"unreachable-device", "无法连接到设备。"},
            {"unanswered", "服务器未响应。"},
            {"tls-failed", "无法建立受保护的连接。"},
            {"certificate-changed", "此服务器与之前连接的服务器不一致。"},
            {"empty-credential", "请先输入登录名和密码。"},
            {"server-offline", "服务器处于离线状态。"},
            {"resource-locked", "此文件夹已锁定。"},
            {"clipboard-empty", "服务器上的剪贴板为空。"},
            {"share-paused", "Android 已在此手机上暂停共享。"},
            {"e2e-missing", "此设备尚无法接收受保护内容。"},
            {"key-changed", "此设备的保护密钥已更改。"},
            {"server-no-e2e", "此服务器无法传输受保护内容。"},
            {"sealed-failed", "无法在此设备上打开。"},
            {"refused", "服务器拒绝了此请求。"}
        }
        Dim ui As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"door_button", "为什么连不上？.."},
            {"dialog_title", "连接诊断"},
            {"what_happened", "发生了什么："},
            {"what_to_try", "建议尝试："},
            {"btn_next_step", "下一步"},
            {"btn_details", "详细信息"},
            {"btn_copy", "复制"},
            {"btn_close", "关闭"},
            {"btn_open_settings", "打开 Windows 设置"},
            {"check_visibility", "我的服务器是否可从外部访问"},
            {"checking", "正在检查..."},
            {"reachable", "可以从互联网访问。"},
            {"not_reachable", "无法从互联网访问。"},
            {"could_not_check", "当前无法检查可访问性。"},
            {"likely_causes", "可能的原因：缺少端口转发、运营商 NAT (CGNAT) 或防火墙。"},
            {"codes_copied", "诊断代码已复制到剪贴板。"}
        }
        AddLocale("zh", steps, msgs, ui)
    End Sub

End Module
#End If
