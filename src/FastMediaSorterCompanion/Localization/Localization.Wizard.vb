Option Strict On

' <summary>
' The one-shot "share this, right now" package wizard and the QR zoom window.
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddWizardStrings()

        Add("Поделиться - код доступа",
            "Share - access code", "Поділитися - код доступу", "Teilen - Zugangscode",
            "Condividi - codice di accesso", "Compartir: código de acceso",
            "Partager - code d'accès", "Partilhar - código de acesso",
            "مشاركة - رمز الوصول", "साझा करें - एक्सेस कोड", "শেয়ার করুন - অ্যাক্সেস কোড",
            "شیئر کریں - رسائی کوڈ", "共享 - 访问码")

        Add("Папки и параметры этого кода доступа",
            "Folders and settings for this access code", "Папки та параметри цього коду доступу",
            "Ordner und Einstellungen für diesen Zugangscode",
            "Cartelle e impostazioni di questo codice di accesso",
            "Carpetas y ajustes de este código de acceso",
            "Dossiers et réglages de ce code d'accès", "Pastas e definições deste código de acesso",
            "المجلدات وإعدادات رمز الوصول هذا", "इस एक्सेस कोड के फ़ोल्डर और सेटिंग्स",
            "এই অ্যাক্সেস কোডের ফোল্ডার ও সেটিং", "اس رسائی کوڈ کے فولڈرز اور ترتیبات",
            "此访问码的文件夹与设置")

        Add("Нажмите на любую ячейку, чтобы изменить значение. Значения взяты из настроек папки; правки - только для этого кода.",
            "Click any cell to change a value. Values come from each folder's settings; edits apply only to this code.",
            "Натисніть на будь-яку клітинку, щоб змінити значення. Значення взято з налаштувань папки; правки - лише для цього коду.",
            "Klicken Sie auf eine Zelle, um den Wert zu ändern. Die Werte stammen aus den Ordnereinstellungen; Änderungen gelten nur für diesen Code.",
            "Fai clic su una cella per cambiarne il valore. I valori provengono dalle impostazioni della cartella; le modifiche valgono solo per questo codice.",
            "Haz clic en cualquier celda para cambiar su valor. Los valores provienen de los ajustes de cada carpeta; los cambios solo afectan a este código.",
            "Cliquez sur une cellule pour en modifier la valeur. Les valeurs viennent des réglages du dossier ; les modifications ne concernent que ce code.",
            "Clique numa célula para alterar o valor. Os valores vêm das definições de cada pasta; as alterações aplicam-se apenas a este código.",
            "انقر على أي خلية لتغيير قيمتها. القيم مأخوذة من إعدادات المجلد؛ والتعديلات تخص هذا الرمز فقط.",
            "किसी भी सेल पर क्लिक करके मान बदलें। मान फ़ोल्डर की सेटिंग्स से आते हैं; बदलाव केवल इसी कोड पर लागू होते हैं।",
            "মান পরিবর্তন করতে যেকোনো ঘরে ক্লিক করুন। মানগুলি ফোল্ডারের সেটিং থেকে আসে; পরিবর্তন শুধু এই কোডের জন্য প্রযোজ্য।",
            "کوئی بھی خانہ کلک کر کے قدر بدلیں۔ اقدار فولڈر کی ترتیبات سے آتی ہیں؛ تبدیلیاں صرف اسی کوڈ پر لاگو ہوتی ہیں۔",
            "点击任意单元格即可修改数值。数值来自各文件夹的设置；修改仅对此访问码生效。")

        Add("Общие настройки передачи:",
            "Common transfer settings:", "Загальні налаштування передачі:",
            "Allgemeine Übertragungseinstellungen:", "Impostazioni di trasferimento comuni:",
            "Ajustes comunes de transferencia:", "Réglages de transfert communs :",
            "Definições comuns de transferência:", "إعدادات النقل المشتركة:",
            "सामान्य स्थानांतरण सेटिंग्स:", "সাধারণ স্থানান্তর সেটিং:",
            "عمومی منتقلی ترتیبات:", "通用传输设置：")

        Add("Только локальная сеть (без адреса из интернета)",
            "LAN only (no internet address)", "Лише локальна мережа (без адреси з інтернету)",
            "Nur lokales Netzwerk (ohne Internetadresse)", "Solo rete locale (senza indirizzo internet)",
            "Solo red local (sin dirección de internet)", "Réseau local uniquement (sans adresse internet)",
            "Apenas rede local (sem endereço de internet)", "الشبكة المحلية فقط (بدون عنوان إنترنت)",
            "केवल स्थानीय नेटवर्क (इंटरनेट पता नहीं)", "শুধু স্থানীয় নেটওয়ার্ক (ইন্টারনেট ঠিকানা ছাড়া)",
            "صرف مقامی نیٹ ورک (انٹرنیٹ پتے کے بغیر)", "仅局域网（不含互联网地址）")

        Add("Пароль не попадёт в файл/QR - телефон запросит его при импорте; передайте пароль отдельно.",
            "The password stays out of the file/QR - the phone asks for it at import; pass it separately.",
            "Пароль не потрапить у файл/QR - телефон запитає його під час імпорту; передайте пароль окремо.",
            "Das Kennwort kommt nicht in Datei/QR - das Telefon fragt beim Import danach; geben Sie es separat weiter.",
            "La password resta fuori dal file/QR: il telefono la chiede all'importazione; comunicala separatamente.",
            "La contraseña no se incluye en el archivo/QR: el teléfono la pedirá al importar; compártela por separado.",
            "Le mot de passe reste hors du fichier/QR - le téléphone le demande à l'import ; transmettez-le séparément.",
            "A palavra-passe fica fora do ficheiro/QR - o telemóvel pede-a na importação; transmita-a em separado.",
            "لن تُدرج كلمة المرور في الملف/رمز QR - سيطلبها الهاتف عند الاستيراد؛ أرسلها بشكل منفصل.",
            "पासवर्ड फ़ाइल/QR में नहीं जाएगा - आयात के समय फ़ोन उसे माँगेगा; पासवर्ड अलग से भेजें।",
            "পাসওয়ার্ড ফাইল/QR-এ যাবে না - আমদানির সময় ফোন সেটি চাইবে; পাসওয়ার্ডটি আলাদাভাবে পাঠান।",
            "پاس ورڈ فائل/QR میں شامل نہیں ہوگا - درآمد کے وقت فون اسے مانگے گا؛ پاس ورڈ الگ سے بھیجیں۔",
            "密码不会写入文件/二维码 - 手机在导入时会询问；请另行传达密码。")

        Add("Показать QR-код",
            "Show QR code", "Показати QR-код", "QR-Code anzeigen", "Mostra il codice QR",
            "Mostrar el código QR", "Afficher le code QR", "Mostrar o código QR",
            "عرض رمز QR", "QR कोड दिखाएँ", "QR কোড দেখান", "QR کوڈ دکھائیں", "显示二维码")

        Add("Скопировать логин/пароль",
            "Copy login/password", "Скопіювати логін/пароль", "Benutzername/Kennwort kopieren",
            "Copia utente/password", "Copiar usuario/contraseña", "Copier identifiant/mot de passe",
            "Copiar utilizador/palavra-passe", "نسخ اسم المستخدم/كلمة المرور",
            "लॉगिन/पासवर्ड कॉपी करें", "লগইন/পাসওয়ার্ড কপি করুন",
            "لاگ ان/پاس ورڈ کاپی کریں", "复制用户名/密码")

        Add("Сохранить файл .fmscfg..",
            "Save .fmscfg file..", "Зберегти файл .fmscfg..", ".fmscfg-Datei speichern..",
            "Salva il file .fmscfg..", "Guardar el archivo .fmscfg..", "Enregistrer le fichier .fmscfg..",
            "Guardar o ficheiro .fmscfg..", "حفظ ملف ‎.fmscfg‎..", ".fmscfg फ़ाइल सहेजें..",
            ".fmscfg ফাইল সংরক্ষণ করুন..", "‎.fmscfg‎ فائل محفوظ کریں..", "保存 .fmscfg 文件..")

        Add("Отправить по почте..",
            "Send by email..", "Надіслати поштою..", "Per E-Mail senden..", "Invia per email..",
            "Enviar por correo..", "Envoyer par e-mail..", "Enviar por email..",
            "إرسال بالبريد الإلكتروني..", "ईमेल से भेजें..", "ইমেলে পাঠান..",
            "ای میل سے بھیجیں..", "通过邮件发送..")

        ' --- the per-folder grid columns ------------------------------------------

        Add("Вкл",
            "On", "Увімк", "Ein", "On", "Sí", "Actif", "Lig",
            "مفعّل", "चालू", "চালু", "آن", "开")

        Add("Имя на телефоне",
            "Name on phone", "Ім'я на телефоні", "Name am Telefon", "Nome sul telefono",
            "Nombre en el teléfono", "Nom sur le téléphone", "Nome no telemóvel",
            "الاسم على الهاتف", "फ़ोन पर नाम", "ফোনে নাম", "فون پر نام", "手机上的名称")

        Add("Типы медиа",
            "Media types", "Типи медіа", "Medientypen", "Tipi di media", "Tipos de medios",
            "Types de médias", "Tipos de multimédia", "أنواع الوسائط", "मीडिया प्रकार",
            "মিডিয়ার ধরন", "میڈیا اقسام", "媒体类型")

        Add("Скан подпапок",
            "Scan subfolders", "Скан підпапок", "Unterordner scannen", "Scansiona sottocartelle",
            "Escanear subcarpetas", "Analyser les sous-dossiers", "Analisar subpastas",
            "فحص المجلدات الفرعية", "उप-फ़ोल्डर स्कैन", "সাবফোল্ডার স্ক্যান",
            "ذیلی فولڈرز اسکین", "扫描子文件夹")

        Add("Подпапки как элементы",
            "Subfolders as items", "Підпапки як елементи", "Unterordner als Elemente",
            "Sottocartelle come elementi", "Subcarpetas como elementos",
            "Sous-dossiers comme éléments", "Subpastas como itens",
            "المجلدات الفرعية كعناصر", "उप-फ़ोल्डर आइटम के रूप में", "সাবফোল্ডার আইটেম হিসেবে",
            "ذیلی فولڈرز بطور آئٹمز", "子文件夹作为条目")

        Add("Скрытые",
            "Hidden", "Приховані", "Versteckte", "Nascosti", "Ocultos", "Masqués", "Ocultos",
            "المخفية", "छिपे हुए", "লুকানো", "پوشیدہ", "隐藏")

        Add("Только чтение",
            "Read-only", "Лише читання", "Schreibgeschützt", "Sola lettura", "Solo lectura",
            "Lecture seule", "Só leitura", "للقراءة فقط", "केवल पढ़ने योग्य",
            "শুধু পঠনযোগ্য", "صرف پڑھنے کے لیے", "只读")

        Add("RO-подсказка",
            "RO hint", "RO-підказка", "RO-Hinweis", "Suggerimento RO", "Indicación RO",
            "Indication RO", "Sugestão RO", "تلميح للقراءة فقط", "RO संकेत",
            "RO ইঙ্গিত", "RO اشارہ", "只读提示")

        Add("Приёмник",
            "Destination", "Приймач", "Ziel", "Destinazione", "Destino", "Destination",
            "Destino", "الوجهة", "गंतव्य", "গন্তব্য", "منزل", "接收目标")

        Add("Комментарий",
            "Comment", "Коментар", "Kommentar", "Commento", "Comentario", "Commentaire",
            "Comentário", "تعليق", "टिप्पणी", "মন্তব্য", "تبصرہ", "备注")

        Add("Слайд-шоу, сек",
            "Slideshow, sec", "Слайд-шоу, сек", "Diaschau, Sek.", "Slideshow, sec",
            "Pase de diapositivas, s", "Diaporama, s", "Apresentação, s",
            "عرض الشرائح، ثانية", "स्लाइडशो, सेकंड", "স্লাইডশো, সেকেন্ড", "سلائیڈ شو، سیکنڈ", "幻灯片，秒")

        Add("Сервер запрещает изменения (настоящий запрет).",
            "The server blocks changes (a real lock).", "Сервер забороняє зміни (справжня заборона).",
            "Der Server blockiert Änderungen (eine echte Sperre).",
            "Il server blocca le modifiche (un blocco reale).",
            "El servidor bloquea los cambios (un bloqueo real).",
            "Le serveur bloque les modifications (un vrai verrou).",
            "O servidor bloqueia alterações (um bloqueio real).",
            "يمنع الخادم التعديلات (قفل حقيقي).", "सर्वर बदलाव रोकता है (वास्तविक लॉक)।",
            "সার্ভার পরিবর্তন আটকায় (প্রকৃত লক)।", "سرور تبدیلیاں روکتا ہے (حقیقی تالا)۔",
            "服务器会阻止修改（真正的锁定）。")

        Add("Приложение спрячет кнопки изменения, но сервер запись не запрещает.",
            "The app hides edit buttons, but the server does not block writes.",
            "Застосунок сховає кнопки змінення, але сервер запис не забороняє.",
            "Die App blendet die Bearbeitungsschaltflächen aus, der Server verhindert Schreibzugriffe aber nicht.",
            "L'app nasconde i pulsanti di modifica, ma il server non blocca la scrittura.",
            "La aplicación oculta los botones de edición, pero el servidor no bloquea la escritura.",
            "L'application masque les boutons d'édition, mais le serveur n'empêche pas l'écriture.",
            "A aplicação esconde os botões de edição, mas o servidor não bloqueia a escrita.",
            "يخفي التطبيق أزرار التعديل، لكن الخادم لا يمنع الكتابة.",
            "ऐप संपादन बटन छिपा देता है, पर सर्वर लिखने से नहीं रोकता।",
            "অ্যাপ সম্পাদনার বোতাম লুকায়, কিন্তু সার্ভার লেখা আটকায় না।",
            "ایپ ترمیم کے بٹن چھپا دیتی ہے، مگر سرور لکھنے سے نہیں روکتا۔",
            "应用会隐藏编辑按钮，但服务器不会阻止写入。")

        Add("Папка-получатель: в неё можно копировать/переносить с телефона (делает папку доступной на запись).",
            "Destination folder: the phone can copy/move into it (makes the folder writable).",
            "Папка-приймач: до неї можна копіювати/переносити з телефона (робить папку доступною на запис).",
            "Zielordner: Das Telefon kann hierher kopieren/verschieben (macht den Ordner beschreibbar).",
            "Cartella di destinazione: il telefono può copiarci/spostarci dentro (rende la cartella scrivibile).",
            "Carpeta de destino: el teléfono puede copiar o mover archivos a ella (la hace escribible).",
            "Dossier de destination : le téléphone peut y copier/déplacer des fichiers (rend le dossier accessible en écriture).",
            "Pasta de destino: o telemóvel pode copiar/mover para ela (torna a pasta gravável).",
            "مجلد الوجهة: يمكن للهاتف النسخ/النقل إليه (يجعل المجلد قابلاً للكتابة).",
            "गंतव्य फ़ोल्डर: फ़ोन इसमें कॉपी/मूव कर सकता है (फ़ोल्डर लिखने योग्य बन जाता है)।",
            "গন্তব্য ফোল্ডার: ফোন এতে কপি/সরাতে পারে (ফোল্ডারটি লেখার যোগ্য হয়)।",
            "منزل فولڈر: فون اس میں کاپی/منتقل کر سکتا ہے (فولڈر قابلِ تحریر ہو جاتا ہے)۔",
            "接收文件夹：手机可向其复制/移动文件（该文件夹将变为可写）。")

        Add("Нажмите, чтобы выбрать точный набор типов. Пусто = решает тип.",
            "Click to pick the exact media set. Empty = the type decides.",
            "Натисніть, щоб вибрати точний набір типів. Порожньо = вирішує тип.",
            "Klicken Sie, um die genaue Medienauswahl zu treffen. Leer = der Typ entscheidet.",
            "Fai clic per scegliere l'insieme esatto di tipi. Vuoto = decide il tipo.",
            "Haz clic para elegir el conjunto exacto de tipos. Vacío = decide el tipo.",
            "Cliquez pour choisir l'ensemble exact de types. Vide = le type décide.",
            "Clique para escolher o conjunto exato de tipos. Vazio = decide o tipo.",
            "انقر لاختيار مجموعة الأنواع بدقة. فارغ = النوع هو الذي يقرر.",
            "सटीक प्रकार-समूह चुनने के लिए क्लिक करें। खाली = प्रकार तय करता है।",
            "সঠিক ধরন-সেট বেছে নিতে ক্লিক করুন। ফাঁকা = ধরনই ঠিক করে।",
            "درست اقسام کا سیٹ منتخب کرنے کے لیے کلک کریں۔ خالی = قسم فیصلہ کرتی ہے۔",
            "点击以选择确切的类型集合。留空 = 由类型决定。")

        Add("Получение состояния..",
            "Fetching state..", "Отримання стану..", "Status wird abgerufen..",
            "Recupero dello stato..", "Obteniendo el estado..", "Récupération de l'état..",
            "A obter o estado..", "جارٍ جلب الحالة..", "स्थिति प्राप्त की जा रही है..",
            "স্ট্যাটাস আনা হচ্ছে..", "حالت حاصل کی جا رہی ہے..", "正在获取状态..")

        Add("Сервер не запущен.",
            "The server is not running.", "Сервер не запущено.", "Der Server läuft nicht.",
            "Il server non è in esecuzione.", "El servidor no está en marcha.",
            "Le serveur n'est pas démarré.", "O servidor não está em execução.",
            "الخادم غير قيد التشغيل.", "सर्वर चालू नहीं है।", "সার্ভার চলছে না।",
            "سرور نہیں چل رہا۔", "服务器未运行。")

        Add("по типу",
            "by type", "за типом", "nach Typ", "per tipo", "por tipo", "selon le type",
            "por tipo", "حسب النوع", "प्रकार के अनुसार", "ধরন অনুযায়ী", "قسم کے مطابق", "按类型")

        Add("Отметьте хотя бы одну папку.",
            "Check at least one folder.", "Позначте хоча б одну папку.",
            "Wählen Sie mindestens einen Ordner aus.", "Seleziona almeno una cartella.",
            "Marca al menos una carpeta.", "Cochez au moins un dossier.",
            "Assinale pelo menos uma pasta.", "حدّد مجلدًا واحدًا على الأقل.",
            "कम से कम एक फ़ोल्डर चुनें।", "অন্তত একটি ফোল্ডার নির্বাচন করুন।",
            "کم از کم ایک فولڈر منتخب کریں۔", "请至少勾选一个文件夹。")

        Add("Нет доступного адреса для раздачи.",
            "No usable address to share.", "Немає доступної адреси для роздачі.",
            "Keine nutzbare Adresse für die Freigabe.", "Nessun indirizzo utilizzabile per la condivisione.",
            "No hay una dirección utilizable para compartir.", "Aucune adresse utilisable pour le partage.",
            "Nenhum endereço utilizável para partilhar.", "لا يوجد عنوان صالح للمشاركة.",
            "साझा करने के लिए कोई उपयोगी पता नहीं।", "শেয়ার করার মতো কোনো ব্যবহারযোগ্য ঠিকানা নেই।",
            "شیئر کرنے کے لیے کوئی قابلِ استعمال پتہ نہیں۔", "没有可用于共享的地址。")

        Add("Код слишком большой для QR - сохраните файл .fmscfg и передайте его.",
            "Too large for a QR - save the .fmscfg file and share that instead.",
            "Код завеликий для QR - збережіть файл .fmscfg і передайте його.",
            "Zu groß für einen QR-Code - speichern Sie die .fmscfg-Datei und geben Sie diese weiter.",
            "Troppo grande per un QR: salva il file .fmscfg e condividi quello.",
            "Demasiado grande para un QR: guarda el archivo .fmscfg y comparte ese.",
            "Trop volumineux pour un QR - enregistrez le fichier .fmscfg et transmettez-le.",
            "Demasiado grande para um QR - guarde o ficheiro .fmscfg e partilhe-o.",
            "أكبر من أن يتسع في رمز QR - احفظ ملف ‎.fmscfg‎ وشاركه بدلاً من ذلك.",
            "QR के लिए बहुत बड़ा - .fmscfg फ़ाइल सहेजकर उसे साझा करें।",
            "QR-এর জন্য অনেক বড় - .fmscfg ফাইল সংরক্ষণ করে সেটি শেয়ার করুন।",
            "QR کے لیے بہت بڑا - ‎.fmscfg‎ فائل محفوظ کر کے اسے شیئر کریں۔",
            "内容过大，二维码放不下 - 请保存 .fmscfg 文件并分享该文件。")

        Add("Логин и пароль скопированы.",
            "Login and password copied.", "Логін і пароль скопійовано.",
            "Benutzername und Kennwort kopiert.", "Utente e password copiati.",
            "Usuario y contraseña copiados.", "Identifiant et mot de passe copiés.",
            "Utilizador e palavra-passe copiados.", "تم نسخ اسم المستخدم وكلمة المرور.",
            "लॉगिन और पासवर्ड कॉपी हो गए।", "লগইন ও পাসওয়ার্ড কপি হয়েছে।",
            "لاگ ان اور پاس ورڈ کاپی ہو گئے۔", "用户名和密码已复制。")

        Add("Файл сохранён.",
            "File saved.", "Файл збережено.", "Datei gespeichert.", "File salvato.",
            "Archivo guardado.", "Fichier enregistré.", "Ficheiro guardado.",
            "تم حفظ الملف.", "फ़ाइल सहेजी गई।", "ফাইল সংরক্ষিত হয়েছে।", "فائل محفوظ ہو گئی۔", "文件已保存。")

        Add("Не удалось сохранить файл.",
            "Could not save the file.", "Не вдалося зберегти файл.",
            "Die Datei konnte nicht gespeichert werden.", "Impossibile salvare il file.",
            "No se pudo guardar el archivo.", "Impossible d'enregistrer le fichier.",
            "Não foi possível guardar o ficheiro.", "تعذّر حفظ الملف.",
            "फ़ाइल सहेजी नहीं जा सकी।", "ফাইল সংরক্ষণ করা যায়নি।", "فائل محفوظ نہ ہو سکی۔", "无法保存文件。")

        Add("Доступ к папкам Fast Media Sorter",
            "Fast Media Sorter folder access", "Доступ до папок Fast Media Sorter",
            "Fast Media Sorter - Ordnerzugriff", "Accesso alle cartelle di Fast Media Sorter",
            "Acceso a carpetas de Fast Media Sorter", "Accès aux dossiers Fast Media Sorter",
            "Acesso às pastas do Fast Media Sorter", "الوصول إلى مجلدات Fast Media Sorter",
            "Fast Media Sorter फ़ोल्डर पहुँच", "Fast Media Sorter ফোল্ডার অ্যাক্সেস",
            "Fast Media Sorter فولڈر رسائی", "Fast Media Sorter 文件夹访问")

        Add("Импортируйте вложенный файл .fmscfg в приложении FastMediaSorter на Android.",
            "Import the attached .fmscfg file in the FastMediaSorter Android app.",
            "Імпортуйте вкладений файл .fmscfg у застосунку FastMediaSorter на Android.",
            "Importieren Sie die angehängte .fmscfg-Datei in der FastMediaSorter-App für Android.",
            "Importa il file .fmscfg allegato nell'app FastMediaSorter per Android.",
            "Importa el archivo .fmscfg adjunto en la aplicación FastMediaSorter para Android.",
            "Importez le fichier .fmscfg joint dans l'application FastMediaSorter pour Android.",
            "Importe o ficheiro .fmscfg anexado na aplicação FastMediaSorter para Android.",
            "استورد ملف ‎.fmscfg‎ المرفق في تطبيق FastMediaSorter على Android.",
            "संलग्न .fmscfg फ़ाइल को Android के FastMediaSorter ऐप में आयात करें।",
            "সংযুক্ত .fmscfg ফাইলটি Android-এর FastMediaSorter অ্যাপে আমদানি করুন।",
            "منسلک ‎.fmscfg‎ فائل کو Android کی FastMediaSorter ایپ میں درآمد کریں۔",
            "请在 Android 版 FastMediaSorter 应用中导入附带的 .fmscfg 文件。")

        Add("Не удалось открыть почтовый клиент.",
            "Could not open the mail client.", "Не вдалося відкрити поштовий клієнт.",
            "Der E-Mail-Client konnte nicht geöffnet werden.", "Impossibile aprire il client di posta.",
            "No se pudo abrir el cliente de correo.", "Impossible d'ouvrir le client de messagerie.",
            "Não foi possível abrir o cliente de email.", "تعذّر فتح برنامج البريد.",
            "मेल क्लाइंट नहीं खुल सका।", "মেইল ক্লায়েন্ট খোলা যায়নি।",
            "میل کلائنٹ نہ کھل سکا۔", "无法打开邮件客户端。")

        Add("Не удалось отправить письмо.",
            "Could not send the email.", "Не вдалося надіслати листа.",
            "Die E-Mail konnte nicht gesendet werden.", "Impossibile inviare l'email.",
            "No se pudo enviar el correo.", "Impossible d'envoyer l'e-mail.",
            "Não foi possível enviar o email.", "تعذّر إرسال البريد الإلكتروني.",
            "ईमेल नहीं भेजा जा सका।", "ইমেল পাঠানো যায়নি।", "ای میل نہ بھیجی جا سکی۔", "无法发送邮件。")

        ' The QR zoom window title carries its whole contract - it is the only chrome that
        ' window has (SPECIFICATION_QR_SAVE_AND_COPY.md §3).
        Add("QR-код - клик увеличивает, сохраняет и копирует; Esc закрывает",
            "QR code - click to enlarge, save and copy; Esc closes",
            "QR-код - клік збільшує, зберігає й копіює; Esc закриває",
            "QR-Code - Klick vergrößert, speichert und kopiert; Esc schließt",
            "Codice QR - il clic ingrandisce, salva e copia; Esc chiude",
            "Código QR: el clic amplía, guarda y copia; Esc cierra",
            "Code QR - le clic agrandit, enregistre et copie ; Échap ferme",
            "Código QR - o clique amplia, guarda e copia; Esc fecha",
            "رمز QR - النقر يكبّر ويحفظ وينسخ؛ Esc يغلق",
            "QR कोड - क्लिक बड़ा करता है, सहेजता और कॉपी करता है; बंद करने के लिए Esc",
            "QR কোড - ক্লিক বড় করে, সংরক্ষণ ও কপি করে; বন্ধ করতে Esc",
            "QR کوڈ - کلک بڑا کرتا، محفوظ اور کاپی کرتا ہے؛ بند کرنے کے لیے Esc",
            "二维码 - 点击可放大、保存并复制；Esc 关闭")

        Add("Скопировано в буфер обмена",
            "Copied to the clipboard", "Скопійовано в буфер обміну",
            "In die Zwischenablage kopiert", "Copiato negli appunti", "Copiado al portapapeles",
            "Copié dans le presse-papiers", "Copiado para a área de transferência",
            "تم النسخ إلى الحافظة", "क्लिपबोर्ड पर कॉपी किया गया", "ক্লিপবোর্ডে কপি করা হয়েছে",
            "کلپ بورڈ پر کاپی ہو گیا", "已复制到剪贴板")

        Add("Не удалось скопировать в буфер обмена",
            "Could not copy to the clipboard", "Не вдалося скопіювати в буфер обміну",
            "Kopieren in die Zwischenablage fehlgeschlagen", "Impossibile copiare negli appunti",
            "No se pudo copiar al portapapeles", "Impossible de copier dans le presse-papiers",
            "Não foi possível copiar para a área de transferência", "تعذّر النسخ إلى الحافظة",
            "क्लिपबोर्ड पर कॉपी नहीं किया जा सका", "ক্লিপবোর্ডে কপি করা যায়নি",
            "کلپ بورڈ پر کاپی نہ ہو سکا", "无法复制到剪贴板")

        Add("Не удалось сохранить изображение",
            "Could not save the image", "Не вдалося зберегти зображення",
            "Das Bild konnte nicht gespeichert werden", "Impossibile salvare l'immagine",
            "No se pudo guardar la imagen", "Impossible d'enregistrer l'image",
            "Não foi possível guardar a imagem", "تعذّر حفظ الصورة",
            "छवि सहेजी नहीं जा सकी", "ছবিটি সংরক্ষণ করা যায়নি",
            "تصویر محفوظ نہ ہو سکی", "无法保存图片")

        Add("Изображение содержит доступ к вашим папкам - не публикуйте его.",
            "The image contains access to your folders - do not publish it.",
            "Зображення містить доступ до ваших папок - не публікуйте його.",
            "Das Bild enthält den Zugang zu Ihren Ordnern - veröffentlichen Sie es nicht.",
            "L'immagine contiene l'accesso alle tue cartelle: non pubblicarla.",
            "La imagen contiene el acceso a tus carpetas: no la publiques.",
            "L'image contient l'accès à vos dossiers - ne la publiez pas.",
            "A imagem contém o acesso às suas pastas - não a publique.",
            "تحتوي الصورة على صلاحية الوصول إلى مجلداتك - لا تنشرها.",
            "छवि में आपके फ़ोल्डरों तक पहुँच है - इसे प्रकाशित न करें।",
            "ছবিটিতে আপনার ফোল্ডারের অ্যাক্সেস রয়েছে - এটি প্রকাশ করবেন না।",
            "تصویر میں آپ کے فولڈرز تک رسائی موجود ہے - اسے شائع نہ کریں۔",
            "该图片包含访问你文件夹的凭据 - 请勿公开。")

        ' --- strings carrying a runtime value --------------------------------------

        Add("Пароль (передайте отдельно): {0}",
            "Password (pass separately): {0}", "Пароль (передайте окремо): {0}",
            "Kennwort (separat weitergeben): {0}", "Password (comunicala a parte): {0}",
            "Contraseña (compártela aparte): {0}", "Mot de passe (à transmettre séparément) : {0}",
            "Palavra-passe (transmita à parte): {0}", "كلمة المرور (أرسلها بشكل منفصل): {0}",
            "पासवर्ड (अलग से भेजें): {0}", "পাসওয়ার্ড (আলাদাভাবে পাঠান): {0}",
            "پاس ورڈ (الگ سے بھیجیں): {0}", "密码（请另行传达）：{0}")

        Add("Логин: {0}",
            "Login: {0}", "Логін: {0}", "Benutzername: {0}", "Utente: {0}", "Usuario: {0}",
            "Identifiant : {0}", "Utilizador: {0}", "اسم المستخدم: {0}", "लॉगिन: {0}",
            "লগইন: {0}", "لاگ ان: {0}", "用户名：{0}")

        Add("Пароль: {0}",
            "Password: {0}", "Пароль: {0}", "Kennwort: {0}", "Password: {0}", "Contraseña: {0}",
            "Mot de passe : {0}", "Palavra-passe: {0}", "كلمة المرور: {0}", "पासवर्ड: {0}",
            "পাসওয়ার্ড: {0}", "پاس ورڈ: {0}", "密码：{0}")

        Add("Сохранено и скопировано: {0}",
            "Saved and copied: {0}", "Збережено й скопійовано: {0}", "Gespeichert und kopiert: {0}",
            "Salvato e copiato: {0}", "Guardado y copiado: {0}", "Enregistré et copié : {0}",
            "Guardado e copiado: {0}", "تم الحفظ والنسخ: {0}", "सहेजा और कॉपी किया गया: {0}",
            "সংরক্ষণ ও কপি করা হয়েছে: {0}", "محفوظ اور کاپی ہو گیا: {0}", "已保存并复制：{0}")

        Add("Сохранено: {0}",
            "Saved: {0}", "Збережено: {0}", "Gespeichert: {0}", "Salvato: {0}", "Guardado: {0}",
            "Enregistré : {0}", "Guardado: {0}", "تم الحفظ: {0}", "सहेजा गया: {0}",
            "সংরক্ষণ করা হয়েছে: {0}", "محفوظ ہو گیا: {0}", "已保存：{0}")

        Add("Папка «Изображения» недоступна - сохранено в {0}",
            "The Pictures folder is not available - saved to {0}",
            "Папка «Зображення» недоступна - збережено в {0}",
            "Der Ordner «Bilder» ist nicht verfügbar - gespeichert in {0}",
            "La cartella «Immagini» non è disponibile - salvato in {0}",
            "La carpeta «Imágenes» no está disponible: guardado en {0}",
            "Le dossier « Images » n'est pas disponible - enregistré dans {0}",
            "A pasta «Imagens» não está disponível - guardado em {0}",
            "مجلد الصور غير متاح - تم الحفظ في {0}",
            "«चित्र» फ़ोल्डर उपलब्ध नहीं है - {0} में सहेजा गया",
            "«ছবি» ফোল্ডার উপলব্ধ নয় - {0}-এ সংরক্ষণ করা হয়েছে",
            "«تصاویر» فولڈر دستیاب نہیں - {0} میں محفوظ کیا گیا",
            "「图片」文件夹不可用 - 已保存到 {0}")

    End Sub

End Class
