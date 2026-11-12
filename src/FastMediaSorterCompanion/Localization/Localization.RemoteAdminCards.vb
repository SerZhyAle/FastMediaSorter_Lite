Option Strict On

' <summary>
' Strings of the six cards of the remote administration window (ticket L0278 T-6, specification 5.6.2): the two rail captions the
' local tables do not have, the status rows, the limit labels, the column heads, the button captions and the one-line states. A key the
' local exchange tables already carry (Состояние, Учётные записи, Устройства, Приглашения, Обновить, Тип, Логин, Создано, Лимиты..,
' Создать.., Изменить.., Сменить пароль.., Удалить учётную запись.., Опубликованные ресурсы, Ресурс, Да, Нет) is reused, never added again.
' The product NAME is never in this table. House style: ".." for an ellipsis, a plain hyphen, ё in Russian.
'
' Every translation here is model-produced and needs a native review before the gate opens (specification 5.12). The pt column is
' Brazilian.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' Никогда не ставьте «умные» кавычки в литерал - VB считает U+201C/U+201D разделителями
' строки; используются «…» и 「…」.
' </summary>
Partial Public NotInheritable Class Localization
    Private Shared Sub AddRemoteAdminCardStrings()
        ' --- the rail captions and the grey caption of a card that lost its session ----------------------------------
        Add("Общее приглашение", "Shared invitation", "Спільне запрошення", "Gemeinsame Einladung", "Invito condiviso", "Invitación compartida", "Invitation partagée", "Convite compartilhado", "دعوة مشتركة", "साझा आमंत्रण", "শেয়ার করা আমন্ত্রণ", "مشترکہ دعوت", "共享邀请")
        Add("Ограничения и политика", "Limits and policy", "Обмеження та політика", "Limits und Richtlinie", "Limiti e criteri", "Límites y política", "Limites et politique", "Limites e política", "الحدود والسياسة", "सीमाएँ और नीति", "সীমা ও নীতি", "حدود اور پالیسی", "限制与策略")
        Add("Последнее известное состояние", "Last known state", "Останній відомий стан", "Zuletzt bekannter Zustand", "Ultimo stato noto", "Último estado conocido", "Dernier état connu", "Último estado conhecido", "آخر حالة معروفة", "अंतिम ज्ञात स्थिति", "সর্বশেষ জানা অবস্থা", "آخری معلوم حالت", "最后已知状态")

        ' --- status ------------------------------------------------------------------------------------------------
        Add("Из них в сети", "Of them online", "З них у мережі", "Davon online", "Di cui online", "De ellos en línea", "Dont en ligne", "Destes, online", "منها المتصلة", "इनमें से ऑनलाइन", "এর মধ্যে অনলাইনে", "ان میں سے آن لائن", "其中在线")
        Add("Трансляции", "Broadcasts", "Трансляції", "Übertragungen", "Trasmissioni", "Transmisiones", "Diffusions", "Transmissões", "البثوث", "प्रसारण", "সম্প্রচার", "نشریات", "广播")
        Add("Буферы обмена", "Clipboards", "Буфери обміну", "Zwischenablagen", "Appunti", "Portapapeles", "Presse-papiers", "Áreas de transferência", "الحافظات", "क्लिपबोर्ड", "ক্লিপবোর্ড", "کلپ بورڈز", "剪贴板")
        Add("Защищённое хранилище", "Sealed storage", "Захищене сховище", "Geschützter Speicher", "Archivio protetto", "Almacenamiento protegido", "Stockage protégé", "Armazenamento protegido", "التخزين المحمي", "सुरक्षित भंडारण", "সুরক্ষিত সংরক্ষণ", "محفوظ اسٹوریج", "受保护的存储")
        Add("{0} из {1}", "{0} of {1}", "{0} з {1}", "{0} von {1}", "{0} su {1}", "{0} de {1}", "{0} sur {1}", "{0} de {1}", "{0} من {1}", "{1} में से {0}", "{1}-এর মধ্যে {0}", "{1} میں سے {0}", "{0}/{1}")

        ' --- the five limits (labels, not sentences) ---------------------------------------------------------------
        Add("Устройств на учётную запись", "Devices per account", "Пристроїв на обліковий запис", "Geräte pro Konto", "Dispositivi per account", "Dispositivos por cuenta", "Appareils par compte", "Dispositivos por conta", "الأجهزة لكل حساب", "प्रति खाता डिवाइस", "প্রতি অ্যাকাউন্টে ডিভাইস", "فی اکاؤنٹ آلات", "每个账户的设备数")
        Add("Ресурсов на учётную запись", "Resources per account", "Ресурсів на обліковий запис", "Ressourcen pro Konto", "Risorse per account", "Recursos por cuenta", "Ressources par compte", "Recursos por conta", "الموارد لكل حساب", "प्रति खाता संसाधन", "প্রতি অ্যাকাউন্টে রিসোর্স", "فی اکاؤنٹ وسائل", "每个账户的资源数")
        Add("Трансляций на учётную запись", "Broadcasts per account", "Трансляцій на обліковий запис", "Übertragungen pro Konto", "Trasmissioni per account", "Transmisiones por cuenta", "Diffusions par compte", "Transmissões por conta", "البثوث لكل حساب", "प्रति खाता प्रसारण", "প্রতি অ্যাকাউন্টে সম্প্রচার", "فی اکاؤنٹ نشریات", "每个账户的广播数")
        Add("Трансляций на устройство", "Broadcasts per device", "Трансляцій на пристрій", "Übertragungen pro Gerät", "Trasmissioni per dispositivo", "Transmisiones por dispositivo", "Diffusions par appareil", "Transmissões por dispositivo", "البثوث لكل جهاز", "प्रति डिवाइस प्रसारण", "প্রতি ডিভাইসে সম্প্রচার", "فی آلہ نشریات", "每台设备的广播数")
        Add("Размер буфера обмена, байт", "Clipboard size, bytes", "Розмір буфера обміну, байт", "Größe der Zwischenablage, Byte", "Dimensione degli appunti, byte", "Tamaño del portapapeles, bytes", "Taille du presse-papiers, octets", "Tamanho da área de transferência, bytes", "حجم الحافظة، بالبايت", "क्लिपबोर्ड का आकार, बाइट", "ক্লিপবোর্ডের আকার, বাইট", "کلپ بورڈ کا سائز، بائٹس", "剪贴板大小（字节）")
        Add("Для новых учётных записей: {0}", "For new accounts: {0}", "Для нових облікових записів: {0}", "Für neue Konten: {0}", "Per i nuovi account: {0}", "Para cuentas nuevas: {0}", "Pour les nouveaux comptes : {0}", "Para novas contas: {0}", "للحسابات الجديدة: {0}", "नए खातों के लिए: {0}", "নতুন অ্যাকাউন্টের জন্য: {0}", "نئے اکاؤنٹس کے لیے: {0}", "新账户：{0}")

        ' --- accounts ----------------------------------------------------------------------------------------------
        Add("Свои лимиты", "Own limits", "Власні обмеження", "Eigene Limits", "Limiti propri", "Límites propios", "Limites propres", "Limites próprios", "حدود خاصة", "अपनी सीमाएँ", "নিজস্ব সীমা", "اپنی حدود", "自有限制")
        Add("Участник", "Member", "Учасник", "Mitglied", "Membro", "Miembro", "Membre", "Membro", "عضو", "सदस्य", "সদস্য", "رکن", "成员")
        Add("Гость", "Guest", "Гість", "Gast", "Ospite", "Invitado", "Invité", "Convidado", "ضيف", "अतिथि", "অতিথি", "مہمان", "访客")
        Add("Сменить тип..", "Change type..", "Змінити тип..", "Typ ändern..", "Cambia tipo..", "Cambiar tipo..", "Changer le type..", "Alterar tipo..", "تغيير النوع..", "प्रकार बदलें..", "ধরন পরিবর্তন করুন..", "قسم تبدیل کریں..", "更改类型..")
        Add("Доступы выбранной учётной записи", "Grants of the selected account", "Доступи вибраного облікового запису", "Freigaben des gewählten Kontos", "Accessi dell'account selezionato", "Accesos de la cuenta seleccionada", "Accès du compte sélectionné", "Acessos da conta selecionada", "صلاحيات الحساب المحدد", "चयनित खाते की पहुँचें", "নির্বাচিত অ্যাকাউন্টের অ্যাক্সেস", "منتخب اکاؤنٹ کی رسائیاں", "所选账户的授权")
        Add("Объект доступа", "Granted item", "Об'єкт доступу", "Zugriffsziel", "Oggetto dell'accesso", "Objeto del acceso", "Objet de l'accès", "Objeto do acesso", "عنصر الوصول", "पहुँच की वस्तु", "অ্যাক্সেসের বস্তু", "رسائی کا آئٹم", "授权对象")
        Add("Выдал", "Granted by", "Надав", "Vergeben von", "Concesso da", "Concedido por", "Accordé par", "Concedido por", "مُنح بواسطة", "किसने दिया", "কে দিয়েছে", "کس نے دیا", "授权者")
        Add("Трансляция", "Broadcast", "Трансляція", "Übertragung", "Trasmissione", "Transmisión", "Diffusion", "Transmissão", "بث", "प्रसारण", "সম্প্রচার", "نشریہ", "广播")
        Add("Выберите учётную запись, чтобы увидеть её доступы.", "Select an account to see its grants.", "Виберіть обліковий запис, щоб побачити його доступи.", "Wählen Sie ein Konto, um seine Freigaben zu sehen.", "Seleziona un account per vederne gli accessi.", "Selecciona una cuenta para ver sus accesos.", "Sélectionnez un compte pour voir ses accès.", "Selecione uma conta para ver seus acessos.", "اختر حسابًا لعرض صلاحياته.", "किसी खाते की पहुँचें देखने के लिए उसे चुनें।", "অ্যাক্সেস দেখতে একটি অ্যাকাউন্ট নির্বাচন করুন।", "رسائیاں دیکھنے کے لیے اکاؤنٹ منتخب کریں۔", "选择账户以查看其授权。")

        ' --- devices -----------------------------------------------------------------------------------------------
        Add("Учётная запись:", "Account:", "Обліковий запис:", "Konto:", "Account:", "Cuenta:", "Compte :", "Conta:", "الحساب:", "खाता:", "অ্যাকাউন্ট:", "اکاؤنٹ:", "账户：")
        Add("Платформа", "Platform", "Платформа", "Plattform", "Piattaforma", "Plataforma", "Plateforme", "Plataforma", "النظام الأساسي", "प्लेटफ़ॉर्म", "প্ল্যাটফর্ম", "پلیٹ فارم", "平台")
        Add("Последняя активность", "Last seen", "Остання активність", "Zuletzt aktiv", "Ultima attività", "Última actividad", "Dernière activité", "Última atividade", "آخر نشاط", "अंतिम गतिविधि", "সর্বশেষ কার্যকলাপ", "آخری سرگرمی", "最后活动")
        Add("В сети", "Online", "У мережі", "Online", "Online", "En línea", "En ligne", "Online", "متصل", "ऑनलाइन", "অনলাইন", "آن لائن", "在线")
        Add("Не в сети", "Offline", "Не в мережі", "Offline", "Offline", "Sin conexión", "Hors ligne", "Offline", "غير متصل", "ऑफ़लाइन", "অফলাইন", "آف لائن", "离线")
        Add("Вход сброшен", "Signed out", "Вхід скинуто", "Abgemeldet", "Disconnesso", "Sesión cerrada", "Déconnecté", "Desconectado", "تم تسجيل الخروج", "साइन आउट", "সাইন আউট", "سائن آؤٹ", "已登出")
        Add("Отозвать устройство..", "Revoke device..", "Відкликати пристрій..", "Gerät widerrufen..", "Revoca dispositivo..", "Revocar dispositivo..", "Révoquer l'appareil..", "Revogar dispositivo..", "إلغاء الجهاز..", "डिवाइस रद्द करें..", "ডিভাইস প্রত্যাহার করুন..", "آلہ منسوخ کریں..", "撤销设备..")
        Add("Код сопряжения..", "Pairing code..", "Код сполучення..", "Kopplungscode..", "Codice di abbinamento..", "Código de vinculación..", "Code d'appairage..", "Código de pareamento..", "رمز الاقتران..", "पेयरिंग कोड..", "পেয়ারিং কোড..", "جوڑنے کا کوڈ..", "配对码..")
        Add("Выберите учётную запись, чтобы увидеть её устройства.", "Select an account to see its devices.", "Виберіть обліковий запис, щоб побачити його пристрої.", "Wählen Sie ein Konto, um seine Geräte zu sehen.", "Seleziona un account per vederne i dispositivi.", "Selecciona una cuenta para ver sus dispositivos.", "Sélectionnez un compte pour voir ses appareils.", "Selecione uma conta para ver seus dispositivos.", "اختر حسابًا لعرض أجهزته.", "किसी खाते के डिवाइस देखने के लिए उसे चुनें।", "ডিভাইস দেখতে একটি অ্যাকাউন্ট নির্বাচন করুন।", "آلات دیکھنے کے لیے اکاؤنٹ منتخب کریں۔", "选择账户以查看其设备。")

        ' --- invitations and the shared invitation -------------------------------------------------------------------
        Add("Для логина", "For login", "Для логіна", "Für Login", "Per il login", "Para el usuario", "Pour l'identifiant", "Para o login", "لاسم الدخول", "लॉगिन के लिए", "লগইনের জন্য", "لاگ اِن کے لیے", "适用登录名")
        Add("Ожидает", "Waiting", "Очікує", "Wartet", "In attesa", "En espera", "En attente", "Aguardando", "في الانتظار", "प्रतीक्षा में", "অপেক্ষারত", "منتظر", "等待中")
        Add("Отозвать приглашение..", "Withdraw invitation..", "Відкликати запрошення..", "Einladung zurückziehen..", "Ritira invito..", "Retirar invitación..", "Retirer l'invitation..", "Retirar convite..", "سحب الدعوة..", "आमंत्रण वापस लें..", "আমন্ত্রণ প্রত্যাহার করুন..", "دعوت واپس لیں..", "撤回邀请..")
        Add("Общее приглашение включено", "The shared invitation is on", "Спільне запрошення увімкнено", "Die gemeinsame Einladung ist eingeschaltet", "L'invito condiviso è attivo", "La invitación compartida está activada", "L'invitation partagée est activée", "O convite compartilhado está ativado", "الدعوة المشتركة قيد التشغيل", "साझा आमंत्रण चालू है", "শেয়ার করা আমন্ত্রণ চালু আছে", "مشترکہ دعوت آن ہے", "共享邀请已开启")
        Add("Общее приглашение включено, тип: {0}", "The shared invitation is on, kind: {0}", "Спільне запрошення увімкнено, тип: {0}", "Die gemeinsame Einladung ist eingeschaltet, Typ: {0}", "L'invito condiviso è attivo, tipo: {0}", "La invitación compartida está activada, tipo: {0}", "L'invitation partagée est activée, type : {0}", "O convite compartilhado está ativado, tipo: {0}", "الدعوة المشتركة قيد التشغيل، النوع: {0}", "साझा आमंत्रण चालू है, प्रकार: {0}", "শেয়ার করা আমন্ত্রণ চালু আছে, ধরন: {0}", "مشترکہ دعوت آن ہے، قسم: {0}", "共享邀请已开启，类型：{0}")
        Add("Общее приглашение выключено", "The shared invitation is off", "Спільне запрошення вимкнено", "Die gemeinsame Einladung ist ausgeschaltet", "L'invito condiviso è disattivato", "La invitación compartida está desactivada", "L'invitation partagée est désactivée", "O convite compartilhado está desativado", "الدعوة المشتركة متوقفة", "साझा आमंत्रण बंद है", "শেয়ার করা আমন্ত্রণ বন্ধ আছে", "مشترکہ دعوت بند ہے", "共享邀请已关闭")
        Add("Задать пароль..", "Set password..", "Задати пароль..", "Passwort festlegen..", "Imposta password..", "Establecer contraseña..", "Définir le mot de passe..", "Definir senha..", "تعيين كلمة المرور..", "पासवर्ड सेट करें..", "পাসওয়ার্ড সেট করুন..", "پاس ورڈ سیٹ کریں..", "设置密码..")
        Add("Выключить", "Turn off", "Вимкнути", "Ausschalten", "Disattiva", "Desactivar", "Désactiver", "Desativar", "إيقاف", "बंद करें", "বন্ধ করুন", "بند کریں", "关闭")

        ' --- limits and policy -----------------------------------------------------------------------------------------
        Add("Предел учётных записей", "Account cap", "Ліміт облікових записів", "Kontolimit", "Limite di account", "Límite de cuentas", "Limite de comptes", "Limite de contas", "الحد الأقصى للحسابات", "खातों की अधिकतम संख्या", "অ্যাকাউন্টের সর্বোচ্চ সংখ্যা", "اکاؤنٹس کی حد", "账户上限")
        Add("Учётных записей на сервере", "Accounts on the server", "Облікових записів на сервері", "Konten auf dem Server", "Account sul server", "Cuentas en el servidor", "Comptes sur le serveur", "Contas no servidor", "الحسابات على الخادم", "सर्वर पर खाते", "সার্ভারে অ্যাকাউন্ট", "سرور پر اکاؤنٹس", "服务器上的账户")
        Add("Изменение действует до перезапуска сервера.", "The change holds until the server restarts.", "Зміна діє до перезапуску сервера.", "Die Änderung gilt, bis der Server neu startet.", "La modifica vale fino al riavvio del server.", "El cambio se mantiene hasta que el servidor se reinicie.", "La modification est conservée jusqu'au redémarrage du serveur.", "A alteração vale até o servidor reiniciar.", "يبقى التغيير ساريًا حتى إعادة تشغيل الخادم.", "यह बदलाव सर्वर के पुनः चालू होने तक रहेगा।", "পরিবর্তনটি সার্ভার পুনরায় চালু হওয়া পর্যন্ত বহাল থাকবে।", "یہ تبدیلی سرور کے دوبارہ شروع ہونے تک برقرار رہے گی۔", "此更改在服务器重启前有效。")
    End Sub
End Class
