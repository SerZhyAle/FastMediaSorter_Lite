Option Strict On

' <summary>
' Strings of 011_SPECIFICATION_SLOT_HEALTH_AND_HONEST_FAILURES_DOTNET10.md Ф2..Ф4 - the
' refusal that names a slot and a reason, the short note the recipients overlay and the
' settings grid show, the auto-created destination folder, and the operation failures that
' now speak in categories instead of in exception text (§3.7).
'
' Ф1's two read-failure sentences are NOT here - they went into Localization.Media.vb with
' the rest of the media-loading path, where they belong.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' Никогда не ставьте «умные» кавычки в литерал - VB считает U+201C/U+201D разделителями
' строки (see the localization rules in CLAUDE.md); используются «…» и 「…」.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddSlotHealthStrings()

        ' --- the refusal, naming the slot key and the reason (§3.4) -----------------
        ' The retry hint rides on the unreachable case ONLY: it is the one state a woken
        ' NAS changes, and the one where pressing the key again is the right answer.
        Add("! Каталог {0}: нет связи с папкой. Нажмите клавишу ещё раз, чтобы повторить проверку.",
            "! Folder {0}: no connection. Press the key again to check once more.",
            "! Каталог {0}: немає зв'язку з текою. Натисніть клавішу ще раз, щоб перевірити знову.",
            "! Ordner {0}: keine Verbindung. Taste erneut drücken, um noch einmal zu prüfen.",
            "! Cartella {0}: nessuna connessione. Premi di nuovo il tasto per riprovare.",
            "! Carpeta {0}: sin conexión. Pulse la tecla otra vez para volver a comprobar.",
            "! Dossier {0} : pas de connexion. Appuyez de nouveau sur la touche pour revérifier.",
            "! Pasta {0}: sem conexão. Pressione a tecla novamente para verificar de novo.",
            "! المجلد {0}: لا يوجد اتصال. اضغط المفتاح مرة أخرى لإعادة الفحص.",
            "! फ़ोल्डर {0}: संपर्क नहीं। दोबारा जाँचने के लिए कुंजी फिर दबाएँ।",
            "! ফোল্ডার {0}: সংযোগ নেই। আবার পরীক্ষা করতে কী-টি পুনরায় চাপুন।",
            "! فولڈر {0}: رابطہ نہیں۔ دوبارہ جانچنے کے لیے کلید پھر دبائیں۔",
            "! 文件夹 {0}：无法连接。再次按该键可重新检查。")

        Add("! Каталог {0} не найден",
            "! Folder {0} not found", "! Каталог {0} не знайдено", "! Ordner {0} nicht gefunden",
            "! Cartella {0} non trovata", "! Carpeta {0} no encontrada", "! Dossier {0} introuvable",
            "! Pasta {0} não encontrada", "! المجلد {0} غير موجود", "! फ़ोल्डर {0} नहीं मिला",
            "! ফোল্ডার {0} পাওয়া যায়নি", "! فولڈر {0} نہیں ملا", "! 未找到文件夹 {0}")

        Add("! Каталог {0}: нет доступа",
            "! Folder {0}: access denied", "! Каталог {0}: немає доступу", "! Ordner {0}: kein Zugriff",
            "! Cartella {0}: accesso negato", "! Carpeta {0}: acceso denegado", "! Dossier {0} : accès refusé",
            "! Pasta {0}: acesso negado", "! المجلد {0}: تم رفض الوصول", "! फ़ोल्डर {0}: पहुँच नहीं",
            "! ফোল্ডার {0}: প্রবেশাধিকার নেই", "! فولڈر {0}: رسائی نہیں", "! 文件夹 {0}：没有访问权限")

        Add("! Каталог {0}: недопустимый путь",
            "! Folder {0}: invalid path", "! Каталог {0}: неприпустимий шлях", "! Ordner {0}: ungültiger Pfad",
            "! Cartella {0}: percorso non valido", "! Carpeta {0}: ruta no válida", "! Dossier {0} : chemin non valide",
            "! Pasta {0}: caminho inválido", "! المجلد {0}: مسار غير صالح", "! फ़ोल्डर {0}: अमान्य पथ",
            "! ফোল্ডার {0}: অবৈধ পথ", "! فولڈر {0}: ناقابلِ قبول راستہ", "! 文件夹 {0}：路径无效")

        Add("! Каталог {0} не задан",
            "! Folder {0} is not set", "! Каталог {0} не задано", "! Ordner {0} ist nicht festgelegt",
            "! Cartella {0} non impostata", "! Carpeta {0} no está definida", "! Dossier {0} non défini",
            "! Pasta {0} não definida", "! لم يُحدَّد المجلد {0}", "! फ़ोल्डर {0} निर्धारित नहीं",
            "! ফোল্ডার {0} নির্ধারিত নয়", "! فولڈر {0} مقرر نہیں", "! 未设置文件夹 {0}")

        Add("проверяю каталог {0}..",
            "checking folder {0}..", "перевіряю каталог {0}..", "prüfe Ordner {0}..",
            "controllo la cartella {0}..", "comprobando la carpeta {0}..", "vérification du dossier {0}..",
            "verificando a pasta {0}..", "جارٍ فحص المجلد {0}..", "फ़ोल्डर {0} की जाँच..",
            "ফোল্ডার {0} পরীক্ষা করা হচ্ছে..", "فولڈر {0} کی جانچ..", "正在检查文件夹 {0}..")

        ' The probe answered after the user had already moved on. The action is deliberately
        ' NOT repeated - it would land on a file they never chose - so the answer is reported
        ' and the next press does the move, instantly, off the warm cache.
        Add("каталог {0} доступен - нажмите клавишу ещё раз",
            "folder {0} is available - press the key again",
            "каталог {0} доступний - натисніть клавішу ще раз",
            "Ordner {0} ist verfügbar - Taste erneut drücken",
            "la cartella {0} è disponibile - premi di nuovo il tasto",
            "la carpeta {0} está disponible: pulse la tecla otra vez",
            "le dossier {0} est disponible - appuyez de nouveau sur la touche",
            "a pasta {0} está disponível - pressione a tecla novamente",
            "المجلد {0} متاح - اضغط المفتاح مرة أخرى",
            "फ़ोल्डर {0} उपलब्ध है - कुंजी फिर दबाएँ",
            "ফোল্ডার {0} উপলব্ধ - কী-টি আবার চাপুন",
            "فولڈر {0} دستیاب ہے - کلید پھر دبائیں",
            "文件夹 {0} 可用 - 请再按一次该键")

        ' --- the same reason in three words, for the overlay and the grid (§3.5) ----
        Add("нет связи",
            "no connection", "немає зв'язку", "keine Verbindung", "nessuna connessione",
            "sin conexión", "pas de connexion", "sem conexão", "لا يوجد اتصال",
            "संपर्क नहीं", "সংযোগ নেই", "رابطہ نہیں", "无法连接")

        Add("не найден",
            "not found", "не знайдено", "nicht gefunden", "non trovata", "no encontrada",
            "introuvable", "não encontrada", "غير موجود", "नहीं मिला", "পাওয়া যায়নি",
            "نہیں ملا", "未找到")

        Add("нет доступа",
            "access denied", "немає доступу", "kein Zugriff", "accesso negato", "acceso denegado",
            "accès refusé", "acesso negado", "تم رفض الوصول", "पहुँच नहीं", "প্রবেশাধিকার নেই",
            "رسائی نہیں", "没有访问权限")

        Add("недопустимый путь",
            "invalid path", "неприпустимий шлях", "ungültiger Pfad", "percorso non valido",
            "ruta no válida", "chemin non valide", "caminho inválido", "مسار غير صالح",
            "अमान्य पथ", "অবৈধ পথ", "ناقابلِ قبول راستہ", "路径无效")

        Add("будет создан",
            "will be created", "буде створено", "wird erstellt", "verrà creata", "se creará",
            "sera créé", "será criada", "سيتم إنشاؤه", "बनाया जाएगा", "তৈরি করা হবে",
            "بنایا جائے گا", "将会创建")

        ' --- auto-created destination (§3.6) ---------------------------------------
        Add("; каталог создан",
            "; the folder was created", "; каталог створено", "; der Ordner wurde erstellt",
            "; la cartella è stata creata", "; la carpeta se ha creado", "; le dossier a été créé",
            "; a pasta foi criada", "؛ تم إنشاء المجلد", "; फ़ोल्डर बना दिया गया",
            "; ফোল্ডারটি তৈরি হয়েছে", "؛ فولڈر بنا دیا گیا", "；文件夹已创建")

        ' --- operation failures speak in categories, not in exception text (§3.7) ---
        Add("! Каталог не найден: {0}",
            "! Folder not found: {0}", "! Каталог не знайдено: {0}", "! Ordner nicht gefunden: {0}",
            "! Cartella non trovata: {0}", "! Carpeta no encontrada: {0}", "! Dossier introuvable : {0}",
            "! Pasta não encontrada: {0}", "! المجلد غير موجود: {0}", "! फ़ोल्डर नहीं मिला: {0}",
            "! ফোল্ডার পাওয়া যায়নি: {0}", "! فولڈر نہیں ملا: {0}", "! 未找到文件夹：{0}")

        Add("! Нет доступа к каталогу: {0}",
            "! No access to the folder: {0}", "! Немає доступу до каталогу: {0}",
            "! Kein Zugriff auf den Ordner: {0}", "! Nessun accesso alla cartella: {0}",
            "! Sin acceso a la carpeta: {0}", "! Pas d'accès au dossier : {0}",
            "! Sem acesso à pasta: {0}", "! لا يوجد وصول إلى المجلد: {0}",
            "! फ़ोल्डर तक पहुँच नहीं: {0}", "! ফোল্ডারে প্রবেশাধিকার নেই: {0}",
            "! فولڈر تک رسائی نہیں: {0}", "! 无法访问文件夹：{0}")

        Add("! Нет связи с каталогом: {0}",
            "! No connection to the folder: {0}", "! Немає зв'язку з каталогом: {0}",
            "! Keine Verbindung zum Ordner: {0}", "! Nessuna connessione alla cartella: {0}",
            "! Sin conexión con la carpeta: {0}", "! Pas de connexion au dossier : {0}",
            "! Sem conexão com a pasta: {0}", "! لا يوجد اتصال بالمجلد: {0}",
            "! फ़ोल्डर से संपर्क नहीं: {0}", "! ফোল্ডারের সঙ্গে সংযোগ নেই: {0}",
            "! فولڈر سے رابطہ نہیں: {0}", "! 无法连接到文件夹：{0}")

        Add("! Недопустимый путь: {0}",
            "! Invalid path: {0}", "! Неприпустимий шлях: {0}", "! Ungültiger Pfad: {0}",
            "! Percorso non valido: {0}", "! Ruta no válida: {0}", "! Chemin non valide : {0}",
            "! Caminho inválido: {0}", "! مسار غير صالح: {0}", "! अमान्य पथ: {0}",
            "! অবৈধ পথ: {0}", "! ناقابلِ قبول راستہ: {0}", "! 路径无效：{0}")

        ' --- the preference (§3.9) --------------------------------------------------
        Add("Создавать отсутствующий каталог получателя",
            "Create a missing destination folder", "Створювати відсутній каталог-отримувач",
            "Fehlenden Zielordner anlegen", "Crea la cartella di destinazione mancante",
            "Crear la carpeta de destino que falte", "Créer le dossier de destination manquant",
            "Criar a pasta de destino ausente", "إنشاء مجلد الوجهة المفقود",
            "अनुपस्थित गंतव्य फ़ोल्डर बनाएँ", "অনুপস্থিত গন্তব্য ফোল্ডার তৈরি করুন",
            "غائب منزل فولڈر بنائیں", "创建缺失的目标文件夹")

        Add("Если у каталога-получателя нет только последней папки, а та, что над ней, доступна - папка создаётся при первом переносе. Выключено - такой каталог считается ненайденным.",
            "When only the destination's last folder is missing and the one above it answers, that folder is created on the first transfer. Off: such a destination counts as not found.",
            "Якщо в каталозі-отримувачі бракує лише останньої теки, а та, що над нею, доступна - тека створюється під час першого перенесення. Вимкнено - такий каталог вважається не знайденим.",
            "Fehlt am Ziel nur der letzte Ordner und antwortet der darüber, wird dieser Ordner beim ersten Verschieben angelegt. Aus: Ein solches Ziel gilt als nicht gefunden.",
            "Se alla destinazione manca solo l'ultima cartella e quella superiore risponde, viene creata al primo trasferimento. Disattivato: la destinazione risulta non trovata.",
            "Si al destino solo le falta la última carpeta y la superior responde, esa carpeta se crea en la primera transferencia. Desactivado: el destino se considera no encontrado.",
            "Si seul le dernier dossier de la destination manque et que celui au-dessus répond, il est créé lors du premier transfert. Désactivé : cette destination est considérée introuvable.",
            "Se ao destino falta apenas a última pasta e a de cima responde, ela é criada na primeira transferência. Desligado: esse destino conta como não encontrado.",
            "إذا كان المجلد الأخير فقط مفقودًا في الوجهة وكان المجلد الأعلى يستجيب، يُنشأ عند أول نقل. عند الإيقاف تُعدّ الوجهة غير موجودة.",
            "यदि गंतव्य में केवल अंतिम फ़ोल्डर नहीं है और उसके ऊपर वाला उपलब्ध है, तो वह पहले स्थानांतरण पर बन जाता है। बंद होने पर ऐसा गंतव्य «नहीं मिला» माना जाता है।",
            "গন্তব্যে যদি কেবল শেষ ফোল্ডারটি না থাকে এবং তার উপরেরটি সাড়া দেয়, প্রথম স্থানান্তরেই সেটি তৈরি হয়। বন্ধ থাকলে এমন গন্তব্য «পাওয়া যায়নি» ধরা হয়।",
            "اگر منزل میں صرف آخری فولڈر موجود نہ ہو اور اس کے اوپر والا جواب دے تو وہ پہلی منتقلی پر بن جاتا ہے۔ بند ہونے پر ایسی منزل «نہیں ملی» شمار ہوتی ہے۔",
            "如果目标只缺最后一级文件夹，而其上一级可访问，则首次转移时创建该文件夹。关闭时，此类目标视为未找到。")

    End Sub

End Class
