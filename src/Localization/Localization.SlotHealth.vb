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
        ' --- the same two sentences for a CLICK on the recipients overlay (125 UX-332) ---
        ' A click is not a key press: it says "click again" to someone who never pressed a key.
        Add("! Каталог {0}: нет связи с папкой. Нажмите ещё раз, чтобы повторить проверку.",
            "! Folder {0}: no connection. Click again to check once more.",
            "! Каталог {0}: немає зв'язку з текою. Натисніть ще раз, щоб перевірити знову.",
            "! Ordner {0}: keine Verbindung. Erneut klicken, um noch einmal zu prüfen.",
            "! Cartella {0}: nessuna connessione. Fai di nuovo clic per riprovare.",
            "! Carpeta {0}: sin conexión. Haga clic otra vez para volver a comprobar.",
            "! Dossier {0} : pas de connexion. Cliquez de nouveau pour revérifier.",
            "! Pasta {0}: sem conexão. Clique novamente para verificar de novo.",
            "! المجلد {0}: لا يوجد اتصال. انقر مرة أخرى لإعادة الفحص.",
            "! फ़ोल्डर {0}: संपर्क नहीं। दोबारा जाँचने के लिए फिर क्लिक करें।",
            "! ফোল্ডার {0}: সংযোগ নেই। আবার পরীক্ষা করতে পুনরায় ক্লিক করুন।",
            "! فولڈر {0}: رابطہ نہیں۔ دوبارہ جانچنے کے لیے پھر کلک کریں۔",
            "! 文件夹 {0}：无法连接。再次点击可重新检查。")
        Add("каталог {0} доступен - нажмите ещё раз",
            "folder {0} is available - click again",
            "каталог {0} доступний - натисніть ще раз",
            "Ordner {0} ist verfügbar - erneut klicken",
            "la cartella {0} è disponibile - fai di nuovo clic",
            "la carpeta {0} está disponible: haga clic otra vez",
            "le dossier {0} est disponible - cliquez de nouveau",
            "a pasta {0} está disponível - clique novamente",
            "المجلد {0} متاح - انقر مرة أخرى",
            "फ़ोल्डर {0} उपलब्ध है - फिर क्लिक करें",
            "ফোল্ডার {0} উপলব্ধ - আবার ক্লিক করুন",
            "فولڈر {0} دستیاب ہے - پھر کلک کریں",
            "文件夹 {0} 可用 - 请再点击一次")

        ' --- the REAL reason on the status line (owner request 2026-10-17) ---------------
        ' Chosen from the error CODE the system returned (SlotCauses), never from its text; {1} is the
        ' computer, share, drive or server name taken from the destination.
        Add("! Каталог {0}: компьютер «{1}» не найден в сети - он выключен, не в этой сети или имя указано неверно.",
            "! Folder {0}: computer «{1}» was not found on the network - it is off, on another network or the name is wrong.",
            "! Каталог {0}: комп'ютер «{1}» не знайдено в мережі - він вимкнений, не в цій мережі або назву вказано невірно.",
            "! Ordner {0}: Computer «{1}» wurde im Netzwerk nicht gefunden - er ist aus, in einem anderen Netzwerk oder der Name ist falsch.",
            "! Cartella {0}: il computer «{1}» non è stato trovato in rete - è spento, in un'altra rete o il nome non è corretto.",
            "! Carpeta {0}: no se encuentra el equipo «{1}» en la red: está apagado, en otra red o el nombre es incorrecto.",
            "! Dossier {0} : l'ordinateur «{1}» est introuvable sur le réseau - il est éteint, sur un autre réseau ou son nom est incorrect.",
            "! Pasta {0}: o computador «{1}» não foi encontrado na rede - está desligado, em outra rede ou o nome está incorreto.",
            "! المجلد {0}: لم يتم العثور على الكمبيوتر «{1}» في الشبكة - فهو مطفأ أو في شبكة أخرى أو الاسم غير صحيح.",
            "! फ़ोल्डर {0}: कंप्यूटर «{1}» नेटवर्क पर नहीं मिला - वह बंद है, दूसरे नेटवर्क पर है या नाम गलत है।",
            "! ফোল্ডার {0}: কম্পিউটার «{1}» নেটওয়ার্কে পাওয়া যায়নি - সেটি বন্ধ, অন্য নেটওয়ার্কে আছে বা নামটি ভুল।",
            "! فولڈر {0}: کمپیوٹر «{1}» نیٹ ورک پر نہیں ملا - وہ بند ہے، کسی اور نیٹ ورک پر ہے یا نام غلط ہے۔",
            "! 文件夹 {0}：在网络中找不到计算机「{1}」- 它已关机、不在此网络中，或名称有误。")

        Add("! Каталог {0}: общая папка «{1}» не найдена - проверьте имя папки на компьютере.",
            "! Folder {0}: shared folder «{1}» was not found - check the share name on the computer.",
            "! Каталог {0}: спільну теку «{1}» не знайдено - перевірте назву теки на комп'ютері.",
            "! Ordner {0}: Freigabe «{1}» wurde nicht gefunden - Freigabenamen auf dem Computer prüfen.",
            "! Cartella {0}: la cartella condivisa «{1}» non è stata trovata - controlla il nome della condivisione sul computer.",
            "! Carpeta {0}: no se encuentra la carpeta compartida «{1}»: compruebe el nombre del recurso en el equipo.",
            "! Dossier {0} : le dossier partagé «{1}» est introuvable - vérifiez le nom du partage sur l'ordinateur.",
            "! Pasta {0}: a pasta compartilhada «{1}» não foi encontrada - verifique o nome da pasta no computador.",
            "! المجلد {0}: لم يتم العثور على المجلد المشترك «{1}» - تحقق من اسم المجلد على الكمبيوتر.",
            "! फ़ोल्डर {0}: साझा फ़ोल्डर «{1}» नहीं मिला - कंप्यूटर पर फ़ोल्डर का नाम जाँचें।",
            "! ফোল্ডার {0}: শেয়ার করা ফোল্ডার «{1}» পাওয়া যায়নি - কম্পিউটারে ফোল্ডারের নামটি পরীক্ষা করুন।",
            "! فولڈر {0}: مشترکہ فولڈر «{1}» نہیں ملا - کمپیوٹر پر فولڈر کا نام جانچیں۔",
            "! 文件夹 {0}：找不到共享文件夹「{1}」- 请检查该计算机上的共享名称。")

        Add("! Каталог {0}: сеть до «{1}» недоступна - проверьте подключение к сети.",
            "! Folder {0}: the network to «{1}» is not reachable - check the network connection.",
            "! Каталог {0}: мережа до «{1}» недоступна - перевірте підключення до мережі.",
            "! Ordner {0}: Das Netzwerk zu «{1}» ist nicht erreichbar - Netzwerkverbindung prüfen.",
            "! Cartella {0}: la rete verso «{1}» non è raggiungibile - controlla la connessione di rete.",
            "! Carpeta {0}: la red hacia «{1}» no está disponible: compruebe la conexión de red.",
            "! Dossier {0} : le réseau vers «{1}» est inaccessible - vérifiez la connexion réseau.",
            "! Pasta {0}: a rede até «{1}» está inacessível - verifique a conexão de rede.",
            "! المجلد {0}: الشبكة إلى «{1}» غير متاحة - تحقق من اتصال الشبكة.",
            "! फ़ोल्डर {0}: «{1}» तक नेटवर्क उपलब्ध नहीं है - नेटवर्क कनेक्शन जाँचें।",
            "! ফোল্ডার {0}: «{1}» পর্যন্ত নেটওয়ার্ক পাওয়া যাচ্ছে না - নেটওয়ার্ক সংযোগ পরীক্ষা করুন।",
            "! فولڈر {0}: «{1}» تک نیٹ ورک دستیاب نہیں - نیٹ ورک کنکشن جانچیں۔",
            "! 文件夹 {0}：无法通过网络访问「{1}」- 请检查网络连接。")

        Add("! Каталог {0}: связь с «{1}» оборвалась.",
            "! Folder {0}: the connection to «{1}» was lost.",
            "! Каталог {0}: зв'язок із «{1}» обірвався.",
            "! Ordner {0}: Die Verbindung zu «{1}» wurde unterbrochen.",
            "! Cartella {0}: la connessione con «{1}» si è interrotta.",
            "! Carpeta {0}: se perdió la conexión con «{1}».",
            "! Dossier {0} : la connexion avec «{1}» a été interrompue.",
            "! Pasta {0}: a conexão com «{1}» foi perdida.",
            "! المجلد {0}: انقطع الاتصال بـ «{1}».",
            "! फ़ोल्डर {0}: «{1}» से कनेक्शन टूट गया।",
            "! ফোল্ডার {0}: «{1}»-এর সঙ্গে সংযোগ বিচ্ছিন্ন হয়েছে।",
            "! فولڈر {0}: «{1}» سے رابطہ منقطع ہو گیا۔",
            "! 文件夹 {0}：与「{1}」的连接已断开。")

        Add("! Каталог {0}: устройство «{1}» не готово - диск, карта или сетевой диск не подключены.",
            "! Folder {0}: device «{1}» is not ready - the drive, card or network drive is not connected.",
            "! Каталог {0}: пристрій «{1}» не готовий - диск, картка або мережевий диск не підключені.",
            "! Ordner {0}: Das Gerät «{1}» ist nicht bereit - Laufwerk, Karte oder Netzlaufwerk ist nicht verbunden.",
            "! Cartella {0}: il dispositivo «{1}» non è pronto - unità, scheda o unità di rete non collegata.",
            "! Carpeta {0}: el dispositivo «{1}» no está listo: la unidad, la tarjeta o la unidad de red no está conectada.",
            "! Dossier {0} : le périphérique «{1}» n'est pas prêt - le disque, la carte ou le lecteur réseau n'est pas connecté.",
            "! Pasta {0}: o dispositivo «{1}» não está pronto - o disco, o cartão ou a unidade de rede não está conectado.",
            "! المجلد {0}: الجهاز «{1}» غير جاهز - القرص أو البطاقة أو محرك الشبكة غير متصل.",
            "! फ़ोल्डर {0}: डिवाइस «{1}» तैयार नहीं है - डिस्क, कार्ड या नेटवर्क ड्राइव जुड़ा नहीं है।",
            "! ফোল্ডার {0}: ডিভাইস «{1}» প্রস্তুত নয় - ডিস্ক, কার্ড বা নেটওয়ার্ক ড্রাইভ সংযুক্ত নেই।",
            "! فولڈر {0}: ڈیوائس «{1}» تیار نہیں - ڈسک، کارڈ یا نیٹ ورک ڈرائیو منسلک نہیں۔",
            "! 文件夹 {0}：设备「{1}」未就绪 - 磁盘、存储卡或网络驱动器未连接。")

        Add("! Каталог {0}: «{1}» не ответил за {2} с.",
            "! Folder {0}: «{1}» did not answer within {2} s.",
            "! Каталог {0}: «{1}» не відповів за {2} с.",
            "! Ordner {0}: «{1}» hat nicht innerhalb von {2} s geantwortet.",
            "! Cartella {0}: «{1}» non ha risposto entro {2} s.",
            "! Carpeta {0}: «{1}» no respondió en {2} s.",
            "! Dossier {0} : «{1}» n'a pas répondu en {2} s.",
            "! Pasta {0}: «{1}» não respondeu em {2} s.",
            "! المجلد {0}: لم يستجب «{1}» خلال {2} ث.",
            "! फ़ोल्डर {0}: «{1}» ने {2} सेकंड में उत्तर नहीं दिया।",
            "! ফোল্ডার {0}: «{1}» {2} সেকেন্ডের মধ্যে সাড়া দেয়নি।",
            "! فولڈر {0}: «{1}» نے {2} سیکنڈ میں جواب نہیں دیا۔",
            "! 文件夹 {0}：「{1}」在 {2} 秒内没有响应。")

        ' A finished sentence from the resource layer ("the server did not accept the login"), wrapped.
        Add("! Каталог {0}: {1}.",
            "! Folder {0}: {1}.", "! Каталог {0}: {1}.", "! Ordner {0}: {1}.",
            "! Cartella {0}: {1}.", "! Carpeta {0}: {1}.", "! Dossier {0} : {1}.",
            "! Pasta {0}: {1}.", "! المجلد {0}: {1}.", "! फ़ोल्डर {0}: {1}।",
            "! ফোল্ডার {0}: {1}।", "! فولڈر {0}: {1}۔", "! 文件夹 {0}：{1}。")

        ' The retry hint that follows a cause sentence, by key press and by click (125 UX-332).
        Add("Нажмите клавишу ещё раз, чтобы повторить проверку.",
            "Press the key again to check once more.",
            "Натисніть клавішу ще раз, щоб перевірити знову.",
            "Taste erneut drücken, um noch einmal zu prüfen.",
            "Premi di nuovo il tasto per riprovare.",
            "Pulse la tecla otra vez para volver a comprobar.",
            "Appuyez de nouveau sur la touche pour revérifier.",
            "Pressione a tecla novamente para verificar de novo.",
            "اضغط المفتاح مرة أخرى لإعادة الفحص.",
            "दोबारा जाँचने के लिए कुंजी फिर दबाएँ।",
            "আবার পরীক্ষা করতে কী-টি পুনরায় চাপুন।",
            "دوبارہ جانچنے کے لیے کلید پھر دبائیں۔",
            "再次按该键可重新检查。")
        Add("Нажмите ещё раз, чтобы повторить проверку.",
            "Click again to check once more.",
            "Натисніть ще раз, щоб перевірити знову.",
            "Erneut klicken, um noch einmal zu prüfen.",
            "Fai di nuovo clic per riprovare.",
            "Haga clic otra vez para volver a comprobar.",
            "Cliquez de nouveau pour revérifier.",
            "Clique novamente para verificar de novo.",
            "انقر مرة أخرى لإعادة الفحص.",
            "दोबारा जाँचने के लिए फिर क्लिक करें।",
            "আবার পরীক্ষা করতে পুনরায় ক্লিক করুন।",
            "دوبارہ جانچنے کے لیے پھر کلک کریں۔",
            "再次点击可重新检查。")

    End Sub

End Class
