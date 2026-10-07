Option Strict On

Partial Public NotInheritable Class Localization

    ''' <summary>
    ''' Strings for Live Broadcast consumption (023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md).
    ''' </summary>
    Private Shared Sub AddLiveBroadcastStrings()

        Add("Открыть live-трансляцию..",
            "Open live broadcast..", "Відкрити live-трансляцію..",
            "Live-Übertragung öffnen..", "Apri trasmissione live..",
            "Abrir transmisión en vivo..", "Ouvrir la diffusion en direct..",
            "Abrir transmissão ao vivo..", "..فتح البث المباشر",
            "लाइव प्रसारण खोलें..", "লাইভ সম্প্রচার খুলুন..",
            "..لائیو نشریات کھولیں", "打开实时直播..")

        Add("Открыть live-трансляцию",
            "Open Live Broadcast", "Відкрити live-трансляцію",
            "Live-Übertragung öffnen", "Apri trasmissione live",
            "Abrir transmisión en vivo", "Ouvrir la diffusion en direct",
            "Abrir transmissão ao vivo", "فتح البث المباشر",
            "लाइव प्रसारण खोलें", "লাইভ সম্প্রচার খুলুন",
            "لائیو نشریات کھولیں", "打开实时直播")

        Add("Вставьте код FMSBCAST1:, ссылку или URL потока:",
            "Paste FMSBCAST1: code, link, or stream URL:", "Вставте код FMSBCAST1:, посилання або URL потоку:",
            "FMSBCAST1:-Code, Link oder Stream-URL einfügen:", "Incolla il codice FMSBCAST1:, il link o l'URL dello streaming:",
            "Pegue el código FMSBCAST1:, enlace o URL de transmisión:", "Collez le code FMSBCAST1:, le lien ou l'URL du flux :",
            "Cole o código FMSBCAST1:, link ou URL do stream:", ":أو رابط أو عنوان بث FMSBCAST1: الصق رمز",
            "FMSBCAST1: कोड, लिंक या स्ट्रीम URL चिपकाएँ:", "FMSBCAST1: কোড, লিঙ্ক বা স্ট্রিম URL পেস্ট করুন:",
            ":کوڈ، لنک یا اسٹریم یو آر ایل چسپاں کریں FMSBCAST1:", "粘贴 FMSBCAST1: 代码、链接或流 URL：")

        Add("Файл .fmsbcast..",
            ".fmsbcast file..", "Файл .fmsbcast..",
            ".fmsbcast-Datei..", "File .fmsbcast..",
            "Archivo .fmsbcast..", "Fichier .fmsbcast..",
            "Arquivo .fmsbcast..", "..fmsbcast. ملف",
            ".fmsbcast फ़ाइल..", ".fmsbcast ফাইল..",
            "..fmsbcast. فائل", ".fmsbcast 文件..")

        Add("Это live-трансляция",
            "This is a live broadcast", "Це live-трансляція",
            "Dies ist eine Live-Übertragung", "Questa è una trasmissione live",
            "Esta es una transmisión en vivo", "Il s'agit d'une diffusion en direct",
            "Esta é uma transmissão ao vivo", "هذا بث مباشر",
            "यह एक लाइव प्रसारण है", "এটি একটি লাইভ সম্প্রচার",
            "یہ ایک لائیو نشریات ہے", "这是实时直播")

        Add("ПК и вещающее устройство должны находиться в одной локальной сети (Wi-Fi / Ethernet).",
            "The PC and broadcasting device must be on the same local network (Wi-Fi / Ethernet).",
            "ПК та пристрій мовлення повинні перебувати в одній локальній мережі (Wi-Fi / Ethernet).",
            "PC und Sendegerät müssen sich im selben lokalen Netzwerk befinden (WLAN / Ethernet).",
            "Il PC e il dispositivo di trasmissione devono trovarsi nella stessa rete locale (Wi-Fi / Ethernet).",
            "La PC y el dispositivo emisor deben estar en la misma red local (Wi-Fi / Ethernet).",
            "Le PC et l'appareil émetteur doivent être sur le même réseau local (Wi-Fi / Ethernet).",
            "O PC e o dispositivo transmissor devem estar na mesma rede local (Wi-Fi / Ethernet).",
            ".(Wi-Fi / Ethernet) يجب أن يكون الكمبيوتر وجهاز البث في نفس الشبكة المحلية",
            "पीसी और प्रसारण उपकरण एक ही स्थानीय नेटवर्क (Wi-Fi / Ethernet) पर होने चाहिए।",
            "পিসি এবং সম্প্রচারকারী ডিভাইস একই স্থানীয় নেটওয়ার্কে (Wi-Fi / Ethernet) থাকতে হবে।",
            "پی سی اور براڈکاسٹنگ ڈیوائس ایک ہی لوکل نیٹ ورک (وائی فائی / ایتھرنیٹ) پر ہونی چاہئیں۔",
            "电脑与广播设备必须处于同一个局域网内 (Wi-Fi / 以太网)。")

        Add("Недавние трансляции:",
            "Recent broadcasts:", "Нещодавні трансляції:",
            "Kürzliche Übertragungen:", "Trasmissioni recenti:",
            "Transmisiones recientes:", "Diffusions récentes :",
            "Transmissões recentes:", ":عمليات البث الأخيرة",
            "हाल के प्रसारण:", "সাম্প্রতিক সম্প্রচার:",
            ":حالیہ نشریات", "最近的直播：")

        Add("Устройство / Название",
            "Device / Title", "Пристрій / Назва",
            "Gerät / Titel", "Dispositivo / Titolo",
            "Dispositivo / Título", "Appareil / Titre",
            "Dispositivo / Título", "الجهاز / العنوان",
            "उपकरण / शीर्षक", "ডিভাইস / শিরোনাম",
            "ڈیوائس / عنوان", "设备 / 标题")


        Add("Файлы трансляций (*.fmsbcast)|*.fmsbcast|Все файлы (*.*)|*.*",
            "Broadcast Files (*.fmsbcast)|*.fmsbcast|All Files (*.*)|*.*",
            "Файли трансляцій (*.fmsbcast)|*.fmsbcast|Усі файли (*.*)|*.*",
            "Übertragungsdateien (*.fmsbcast)|*.fmsbcast|Alle Dateien (*.*)|*.*",
            "File di trasmissione (*.fmsbcast)|*.fmsbcast|Tutti i file (*.*)|*.*",
            "Archivos de transmisión (*.fmsbcast)|*.fmsbcast|Todos los archivos (*.*)|*.*",
            "Fichiers de diffusion (*.fmsbcast)|*.fmsbcast|Tous les fichiers (*.*)|*.*",
            "Arquivos de transmissão (*.fmsbcast)|*.fmsbcast|Todos os arquivos (*.*)|*.*",
            "ملفات البث (*.fmsbcast)|*.fmsbcast|جميع الملفات (*.*)|*.*",
            "प्रसारण फ़ाइलें (*.fmsbcast)|*.fmsbcast|सभी फ़ाइलें (*.*)|*.*",
            "সম্প্রচার ফাইল (*.fmsbcast)|*.fmsbcast|সমস্ত ফাইল (*.*)|*.*",
            "نشریاتی فائلیں (*.fmsbcast)|*.fmsbcast|تمام فائلیں (*.*)|*.*",
            "直播文件 (*.fmsbcast)|*.fmsbcast|所有文件 (*.*)|*.*")

        Add("Выбрать файл .fmsbcast",
            "Select .fmsbcast File", "Вибрати файл .fmsbcast",
            ".fmsbcast-Datei auswählen", "Seleziona file .fmsbcast",
            "Seleccionar archivo .fmsbcast", "Sélectionner le fichier .fmsbcast",
            "Selecionar arquivo .fmsbcast", "fmsbcast. تحديد ملف",
            ".fmsbcast फ़ाइल चुनें", ".fmsbcast ফাইল নির্বাচন করুন",
            "fmsbcast. فائل منتخب کریں", "选择 .fmsbcast 文件")

        Add("Введите адрес или код трансляции",
            "Enter broadcast address or code", "Введіть адресу або код трансляції",
            "Geben Sie die Übertragungsadresse oder den Code ein", "Inserisci l'indirizzo o il codice della trasmissione",
            "Introduzca la dirección o código de transmisión", "Saisissez l'adresse ou le code de diffusion",
            "Digite o endereço ou código da transmissão", "أدخل عنوان البث أو الرمز",
            "प्रसारण का पता या कोड दर्ज करें", "সম্প্রচারের ঠিকানা বা কোড লিখুন",
            "نشریات کا پتہ یا کوڈ درج کریں", "请输入直播地址或代码")

        Add("Требуется обновить приложение для этой трансляции",
            "An app update is required for this broadcast", "Потрібно оновити застосунок для цієї трансляції",
            "Für diese Übertragung ist ein App-Update erforderlich", "È necessario aggiornare l'app per questa trasmissione",
            "Se requiere actualizar la aplicación para esta transmisión", "Une mise à jour de l'application est requise pour cette diffusion",
            "É necessária uma atualização do aplicativo para esta transmissão", "يلزم تحديث التطبيق لهذا البث",
            "इस प्रसारण के लिए ऐप अपडेट आवश्यक है", "এই সম্প্রচারের জন্য অ্যাপ আপডেট প্রয়োজন",
            "اس نشریات کے لیے ایپ اپ ڈیٹ درکار ہے", "此直播需要更新应用程序")

        Add("Этот режим пока не поддерживается",
            "This mode is not yet supported", "Цей режим поки що не підтримується",
            "Dieser Modus wird noch nicht unterstützt", "Questa modalità non è ancora supportata",
            "Este modo aún no es compatible", "Ce mode n'est pas encore pris en charge",
            "Este modo ainda não é suportado", "هذا الوضع غير مدعوم حاليًا",
            "यह मोड अभी समर्थित नहीं है", "এই মোডটি এখনও সমর্থিত নয়",
            "یہ موڈ ابھی تعاون یافتہ نہیں ہے", "此模式暂不支持")

        Add("Устройство передало локальный адрес (loopback), недоступный с ПК",
            "The device sent a local loopback address unreachable from the PC",
            "Пристрій передав локальну адресу (loopback), недоступну з ПК",
            "Das Gerät hat eine lokale Loopback-Adresse gesendet, die vom PC aus nicht erreichbar ist",
            "Il dispositivo ha inviato un indirizzo di loopback locale non raggiungibile dal PC",
            "El dispositivo envió una dirección local de bucle invertido (loopback) inaccesible desde la PC",
            "L'appareil a envoyé une adresse de bouclage locale (loopback) inaccessible depuis le PC",
            "O dispositivo enviou um endereço de loopback local inacessível a partir do PC",
            "أرسل الجهاز عنوان استرجاع محليًا لا يمكن الوصول إليه من الكمبيوتر",
            "उपकरण ने एक स्थानीय लूपबैक पता भेजा जो पीसी से पहुंच योग्य नहीं है",
            "ডিভাইসটি একটি স্থানীয় লুপব্যাক ঠিকানা পাঠিয়েছে যা পিসি থেকে অপ্রাপ্য",
            "ڈیوائس نے ایک مقامی لوپ بیک پتہ بھیجا جو پی سی سے ناقابل رسائی ہے",
            "设备发送了从电脑无法访问的本地回环地址 (loopback)")

        Add("Превышен максимальный размер дескриптора (64 КБ)",
            "Maximum descriptor size exceeded (64 KiB)", "Перевищено максимальний розмір дескриптора (64 КБ)",
            "Maximale Deskriptorgröße überschritten (64 KiB)", "Dimensione massima del descrittore superata (64 KiB)",
            "Tamaño máximo de descriptor superado (64 KiB)", "Taille maximale du descripteur dépassée (64 Kio)",
            "Tamanho máximo do descritor excedido (64 KiB)", "تجاوز الحد الأقصى لحجم الواصف (64 كيلوبايت)",
            "अधिकतम डिस्क्रिप्टर आकार पार हो गया (64 KiB)", "সর্বোচ্চ বর্ণনাকারীর আকার অতিক্রম করেছে (64 KiB)",
            "زیادہ سے زیادہ ڈسکرپٹر سائز سے تجاوز کر گیا (64 KiB)", "超出描述符最大大小 (64 KiB)")

        Add("Неверный формат трансляции или повреждённые данные",
            "Invalid broadcast format or corrupted data", "Невірний формат трансляції або пошкоджені дані",
            "Ungültiges Übertragungsformat oder beschädigte Daten", "Formato di trasmissione non valido o dati danneggiati",
            "Formato de transmisión no válido o datos dañados", "Format de diffusion non valide ou données corrompues",
            "Formato de transmissão inválido ou dados corrompidos", "تنسيق بث غير صالح أو بيانات تالفة",
            "अमान्य प्रसारण प्रारूप या दूषित डेटा", "অবৈধ সম্প্রচার বিন্যাস বা ক্ষতিগ্রস্ত ডেটা",
            "غلط نشریاتی فارمیٹ یا خراب ڈیٹا", "无效的直播格式或损坏的数据")

        Add("Не удалось распознать трансляцию",
            "Could not recognize broadcast", "Не вдалося розпізнати трансляцію",
            "Übertragung konnte nicht erkannt werden", "Impossibile riconoscere la trasmissione",
            "No se pudo reconocer la transmisión", "Impossible de reconnaître la diffusion",
            "Não foi possível reconhecer a transmissão", "تعذّر التعرف على البث",
            "प्रसारण की पहचान नहीं हो सकी", "সম্প্রচার সনাক্ত করা যায়নি",
            "نشریات کی شناخت نہیں ہو سکی", "无法识别直播")

        Add("На часах уже максимальное число слушателей",
            "The watch already has the maximum number of listeners",
            "На годиннику вже максимальна кількість слухачів",
            "Die Uhr hat bereits die maximale Anzahl an Hörern",
            "L'orologio ha già raggiunto il numero massimo di ascoltatori",
            "El reloj ya tiene el número máximo de oyentes",
            "La montre a déjà atteint le nombre maximal d'auditeurs",
            "O relógio já atingiu o número máximo de ouvintes",
            "وصلت الساعة بالفعل إلى الحد الأقصى لعدد المستمعين",
            "घड़ी में श्रोताओं की अधिकतम संख्या पहले ही पूरी हो चुकी है",
            "ঘড়িতে ইতিমধ্যে সর্বাধিক সংখ্যক শ্রোতা রয়েছে",
            "گھڑی میں پہلے ہی زیادہ سے زیادہ سننے والوں کی تعداد پوری ہو چکی ہے",
            "手表上的听众人数已达上限")

        Add("Устройство отказало в доступе",
            "The device denied access", "Пристрій відмовив у доступі",
            "Das Gerät hat den Zugriff verweigert", "Il dispositivo ha negato l'accesso",
            "El dispositivo denegó el acceso", "L'appareil a refusé l'accès",
            "O dispositivo negou o acesso", "رفض الجهاز الوصول",
            "उपकरण ने पहुँच अस्वीकार कर दी", "ডিভাইসটি অ্যাক্সেস অস্বীকার করেছে",
            "ڈیوائس نے رسائی سے انکار کر دیا", "设备拒绝了访问")

        Add("Трансляция больше недоступна",
            "The broadcast is no longer available", "Трансляція більше недоступна",
            "Die Übertragung ist nicht mehr verfügbar", "La trasmissione non è più disponibile",
            "La transmisión ya no está disponible", "La diffusion n'est plus disponible",
            "A transmissão não está mais disponível", "لم يعد البث متاحًا",
            "प्रसारण अब उपलब्ध नहीं है", "সম্প্রচারটি আর উপলব্ধ নেই",
            "نشریات اب دستیاب نہیں ہے", "直播已不再可用")

        Add("Устройство вернуло не аудиопоток",
            "The device returned a non-audio stream", "Пристрій повернув не аудіопотік",
            "Das Gerät hat keinen Audiostream zurückgegeben", "Il dispositivo ha restituito un flusso non audio",
            "El dispositivo devolvió una secuencia no de audio", "L'appareil a renvoyé un flux non audio",
            "O dispositivo retornou um fluxo que não é de áudio", "أعاد الجهاز تدفقًا ليس صوتيًا",
            "उपकरण ने गैर-ऑडियो स्ट्रीम वापस की", "ডিভাইসটি একটি অ-অডিও স্ট্রিম ফেরত দিয়েছে",
            "ڈیوائس نے غیر آڈیو اسٹریم واپس کی", "设备返回的不是音频流")

        Add("Трансляция завершена или устройство недоступно",
            "Broadcast ended or device unreachable", "Трансляцію завершено або пристрій недоступний",
            "Übertragung beendet oder Gerät nicht erreichbar", "Trasmissione terminata o dispositivo non raggiungibile",
            "Transmisión finalizada o dispositivo inaccesible", "Diffusion terminée ou appareil inaccessible",
            "Transmissão encerrada ou dispositivo inacessível", "انتهى البث أو تعذّر الوصول إلى الجهاز",
            "प्रसारण समाप्त हो गया या उपकरण अनुपलब्ध है", "সম্প্রচার সমাপ্ত হয়েছে বা ডিভাইস অপ্রাপ্য",
            "نشریات ختم ہو گئی یا ڈیوائس دستیاب نہیں ہے", "直播已结束或设备不可达")

        Add("Не удалось подключиться к трансляции",
            "Could not connect to broadcast", "Не вдалося підключитися до трансляції",
            "Verbindung zur Übertragung fehlgeschlagen", "Impossibile connettersi alla trasmissione",
            "No se pudo conectar a la transmisión", "Impossible de se connecter à la diffusion",
            "Não foi possível conectar à transmissão", "تعذّر الاتصال بالبث",
            "प्रसारण से कनेक्ट नहीं हो सका", "সম্প্রচারের সাথে সংযোগ করা যায়নি",
            "نشریات سے رابطہ قائم نہیں ہو سکا", "无法连接到直播")

        Add("Трансляция завершена",
            "Broadcast ended", "Трансляцію завершено",
            "Übertragung beendet", "Trasmissione terminata",
            "Transmisión finalizada", "Diffusion terminée",
            "Transmissão encerrada", "انتهى البث",
            "प्रसारण समाप्त", "সম্প্রচার সমাপ্ত",
            "نشریات ختم ہو گئی", "直播已结束")

        Add("LIVE - {0}",
            "LIVE - {0}", "LIVE - {0}",
            "LIVE - {0}", "LIVE - {0}",
            "EN VIVO - {0}", "EN DIRECT - {0}",
            "AO VIVO - {0}", "{0} - بث مباشر",
            "लाइव - {0}", "লাইভ - {0}",
            "{0} - لائیو", "直播 - {0}")

        Add("Переподключение {0}/5..",
            "Reconnecting {0}/5..", "Перепідключення {0}/5..",
            "Wiederverbindung {0}/5..", "Riconnessione {0}/5..",
            "Reconectando {0}/5..", "Reconnexion {0}/5..",
            "Reconectando {0}/5..", "..إعادة الاتصال {0}/5",
            "पुनः कनेक्ट हो रहा है {0}/5..", "পুনরায় সংযোগ করা হচ্ছে {0}/5..",
            "..دوبارہ منسلک ہو رہا ہے {0}/5", "正在重新连接 {0}/5..")

        Add("Остановить",
            "Stop", "Зупинити",
            "Stoppen", "Interrompi",
            "Detener", "Arrêter",
            "Parar", "إيقاف",
            "रोकें", "থামান",
            "روکیں", "停止")

        Add("Подключиться снова",
            "Reconnect", "Підключитися знову",
            "Erneut verbinden", "Riconnetti",
            "Reconectar", "Se reconnecter",
            "Reconectar", "إعادة الاتصال",
            "फिर से कनेक्ट करें", "আবার সংযোগ করুন",
            "دوبارہ منسلک کریں", "重新连接")

        Add("Адреса в списке - последние известные: устройство могло перестать вещать или сменить порт.",
            "Addresses in the list are the last known ones: the device may have stopped broadcasting or changed its port.",
            "Адреси в списку - останні відомі: пристрій міг припинити мовлення або змінити порт.",
            "Die Adressen in der Liste sind die zuletzt bekannten: Das Gerät hat möglicherweise aufgehört zu senden oder den Port gewechselt.",
            "Gli indirizzi nell'elenco sono gli ultimi noti: il dispositivo potrebbe aver smesso di trasmettere o aver cambiato porta.",
            "Las direcciones de la lista son las últimas conocidas: el dispositivo puede haber dejado de emitir o haber cambiado de puerto.",
            "Les adresses de la liste sont les dernières connues : l'appareil a pu cesser d'émettre ou changer de port.",
            "Os endereços da lista são os últimos conhecidos: o dispositivo pode ter parado de transmitir ou mudado de porta.",
            ".العناوين في القائمة هي آخر عناوين معروفة: ربما توقف الجهاز عن البث أو غيّر المنفذ",
            "सूची के पते अंतिम ज्ञात पते हैं: उपकरण ने प्रसारण बंद कर दिया हो या पोर्ट बदल लिया हो सकता है।",
            "তালিকার ঠিকানাগুলি সর্বশেষ জানা ঠিকানা: ডিভাইসটি সম্প্রচার বন্ধ করে থাকতে পারে বা পোর্ট বদলাতে পারে।",
            ".فہرست میں موجود پتے آخری معلوم پتے ہیں: ہو سکتا ہے ڈیوائس نے نشریات بند کر دی ہوں یا پورٹ بدل لیا ہو",
            "列表中的地址是最后已知的地址：设备可能已停止广播或更换了端口。")

        Add("Трансляция остановлена",
            "Broadcast stopped", "Трансляцію зупинено",
            "Übertragung gestoppt", "Trasmissione interrotta",
            "Transmisión detenida", "Diffusion arrêtée",
            "Transmissão parada", "توقف البث",
            "प्रसारण रोक दिया गया", "সম্প্রচার থামানো হয়েছে",
            "نشریات روک دی گئی", "直播已停止")

        ' --- Settings: the explicit, reversible .fmsbcast association (never done by the installer) ---
        Add("Файлы .fmsbcast (live-трансляции)",
            ".fmsbcast files (live broadcasts)", "Файли .fmsbcast (live-трансляції)",
            ".fmsbcast-Dateien (Live-Übertragungen)", "File .fmsbcast (trasmissioni live)",
            "Archivos .fmsbcast (transmisiones en vivo)", "Fichiers .fmsbcast (diffusions en direct)",
            "Arquivos .fmsbcast (transmissões ao vivo)", "ملفات ‎.fmsbcast (بث مباشر)",
            ".fmsbcast फ़ाइलें (लाइव प्रसारण)", ".fmsbcast ফাইল (লাইভ সম্প্রচার)",
            ".fmsbcast فائلیں (لائیو نشریات)", ".fmsbcast 文件（实时直播）")

        Add("Файлы .fmsbcast присылают телефон и часы, когда вещают звук. Если у них ещё нет программы по умолчанию, их будет открывать эта; чужую запись она не заменяет, а только появляется в меню «Открыть с помощью».",
            "Phones and watches send .fmsbcast files when they broadcast sound. If these files have no default program yet, this one will open them; it never replaces someone else's entry and then only appears in the Open with menu.",
            "Файли .fmsbcast надсилають телефон і годинник, коли транслюють звук. Якщо в них ще немає програми за замовчуванням, їх відкриватиме ця; чужий запис вона не замінює, а лише з'являється в меню «Відкрити за допомогою».",
            "Smartphones und Uhren senden .fmsbcast-Dateien, wenn sie Ton übertragen. Hat diese Dateien noch kein Standardprogramm, öffnet dieses Programm sie; einen fremden Eintrag ersetzt es nie und erscheint dann nur im Menü «Öffnen mit».",
            "Telefoni e smartwatch inviano file .fmsbcast quando trasmettono audio. Se questi file non hanno ancora un programma predefinito, li aprirà questo; non sostituisce mai la voce di un altro programma e compare solo nel menu «Apri con».",
            "Los teléfonos y relojes envían archivos .fmsbcast cuando emiten sonido. Si estos archivos aún no tienen un programa predeterminado, los abrirá este; nunca reemplaza la entrada de otro programa y solo aparece en el menú «Abrir con».",
            "Les téléphones et les montres envoient des fichiers .fmsbcast lorsqu'ils diffusent du son. Si ces fichiers n'ont pas encore de programme par défaut, celui-ci les ouvrira ; il ne remplace jamais l'entrée d'un autre programme et apparaît alors seulement dans le menu «Ouvrir avec».",
            "Telefones e relógios enviam arquivos .fmsbcast quando transmitem som. Se esses arquivos ainda não têm um programa padrão, este os abrirá; ele nunca substitui a entrada de outro programa e, nesse caso, só aparece no menu «Abrir com».",
            ".ترسل الهواتف والساعات ملفات ‎.fmsbcast عندما تبث الصوت. إذا لم يكن لهذه الملفات برنامج افتراضي بعد فسيفتحها هذا البرنامج، ولا يستبدل أبدًا إدخال برنامج آخر بل يظهر فقط في قائمة «فتح باستخدام»",
            ".fmsbcast फ़ाइलें फ़ोन और घड़ियाँ तब भेजते हैं जब वे ध्वनि प्रसारित करते हैं। यदि इन फ़ाइलों का अभी कोई डिफ़ॉल्ट प्रोग्राम नहीं है, तो इन्हें यह प्रोग्राम खोलेगा; यह किसी और की प्रविष्टि को कभी नहीं बदलता, केवल «इसके साथ खोलें» मेनू में दिखाई देता है।",
            "ফোন ও ঘড়ি শব্দ সম্প্রচার করার সময় .fmsbcast ফাইল পাঠায়। এই ফাইলগুলির এখনও কোনো ডিফল্ট প্রোগ্রাম না থাকলে এই প্রোগ্রামটি সেগুলি খুলবে; এটি অন্য কারও এন্ট্রি কখনও প্রতিস্থাপন করে না, শুধু «এর সাহায্যে খুলুন» মেনুতে দেখা যায়।",
            ".فون اور گھڑیاں آواز نشر کرتے وقت ‎.fmsbcast فائلیں بھیجتی ہیں۔ اگر ان فائلوں کا ابھی کوئی ڈیفالٹ پروگرام نہیں ہے تو انہیں یہ پروگرام کھولے گا؛ یہ کسی اور کی اندراج کو کبھی تبدیل نہیں کرتا بلکہ صرف «اس کے ساتھ کھولیں» مینو میں ظاہر ہوتا ہے",
            ".fmsbcast 文件由手机和手表在广播声音时发送。如果这些文件还没有默认程序，将由本程序打开；它绝不会替换其他程序的设置，只会出现在「打开方式」菜单中。")

        Add("Устройство ответило: HTTP {0}",
            "The device answered: HTTP {0}", "Пристрій відповів: HTTP {0}",
            "Das Gerät antwortete: HTTP {0}", "Il dispositivo ha risposto: HTTP {0}",
            "El dispositivo respondió: HTTP {0}", "L'appareil a répondu : HTTP {0}",
            "O dispositivo respondeu: HTTP {0}", "رد الجهاز: HTTP {0}",
            "उपकरण ने उत्तर दिया: HTTP {0}", "ডিভাইস উত্তর দিয়েছে: HTTP {0}",
            "ڈیوائس نے جواب دیا: HTTP {0}", "设备返回：HTTP {0}")

    End Sub

End Class
