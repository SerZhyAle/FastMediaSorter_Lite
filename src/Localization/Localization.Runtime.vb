Option Strict On

' <summary>
' Strings that carry a runtime value ({0}, {1}) and the multi-line message boxes.
'
' These go through Localization.TF, never through concatenation: word order differs
' per language, and in Arabic and Urdu bidi reorders a concatenation outright - which
' is how the file counter used to read "from 5 1" instead of "1 from 5".
'
' Multi-line keys are registered with the same vbCrLf/vbLf the call site builds, so
' the key matches character for character.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddRuntimeStrings()

        ' --- file counter --------------------------------------------------------
        Add("{0} из {1}", "{0} from {1}", "{0} з {1}", "{0} von {1}", "{0} di {1}", "{0} de {1}",
            "{0} sur {1}", "{0} de {1}", "{0} من {1}", "{0} / {1}", "{0} / {1}", "{0} از {1}", "{0} / {1}")

        ' --- file operations -----------------------------------------------------
        Add("! Нет каталога-получателя для клавиши {0}", "! No dest folder set with key {0}",
            "! Немає теки-отримувача для клавіші {0}", "! Kein Zielordner für Taste {0}",
            "! Nessuna cartella di destinazione per il tasto {0}", "! No hay carpeta de destino para la tecla {0}",
            "! Aucun dossier de destination pour la touche {0}", "! Nenhuma pasta de destino para a tecla {0}",
            "! لا يوجد مجلد وجهة للمفتاح {0}", "! कुंजी {0} के लिए कोई गंतव्य फ़ोल्डर नहीं",
            "! {0} কী-এর জন্য কোনো গন্তব্য ফোল্ডার নেই", "! کلید {0} کے لیے کوئی منزل فولڈر نہیں", "! 按键 {0} 没有设置目标文件夹")
        Add("Файл переименован: {0}{1}", "File renamed: {0}{1}", "Файл перейменовано: {0}{1}",
            "Datei umbenannt: {0}{1}", "File rinominato: {0}{1}", "Archivo renombrado: {0}{1}",
            "Fichier renommé : {0}{1}", "Arquivo renomeado: {0}{1}", "تمت إعادة تسمية الملف: {0}{1}",
            "फ़ाइल का नाम बदला: {0}{1}", "ফাইলের নাম বদলেছে: {0}{1}", "فائل کا نام بدل گیا: {0}{1}", "文件已重命名：{0}{1}")
        Add(" (имя занято, сохранён как {0})", " (name taken, saved as {0})", " (ім'я зайняте, збережено як {0})",
            " (Name vergeben, gespeichert als {0})", " (nome occupato, salvato come {0})",
            " (nombre ocupado, guardado como {0})", " (nom pris, enregistré sous {0})",
            " (nome ocupado, salvo como {0})", " (الاسم مستخدم، حُفظ باسم {0})",
            " (नाम पहले से है, {0} के रूप में सहेजा गया)", " (নাম দখলে, {0} নামে সংরক্ষিত)",
            " (نام پہلے سے موجود، {0} کے طور پر محفوظ)", "（名称已占用，另存为 {0}）")
        Add("!Ждите.. Файл копируется ({0}) в каталог {1}", "!Wait.. File copying ({0}) to {1}",
            "!Зачекайте.. Файл копіюється ({0}) до теки {1}", "!Warten.. Datei wird kopiert ({0}) nach {1}",
            "!Attendi.. copia del file ({0}) in {1}", "!Espere.. copiando el archivo ({0}) a {1}",
            "!Patientez.. copie du fichier ({0}) vers {1}", "!Aguarde.. copiando o arquivo ({0}) para {1}",
            "!انتظر.. جارٍ نسخ الملف ({0}) إلى {1}", "!प्रतीक्षा करें.. फ़ाइल ({0}) {1} में कॉपी हो रही है",
            "!অপেক্ষা করুন.. ফাইল ({0}) {1}-এ কপি হচ্ছে", "!انتظار کریں.. فائل ({0}) {1} میں نقل ہو رہی ہے",
            "!请稍候.. 正在把文件 ({0}) 复制到 {1}")
        Add("файл скопирован ({0}) в каталог {1}", "file copied ({0}) to {1}",
            "файл скопійовано ({0}) до теки {1}", "Datei kopiert ({0}) nach {1}",
            "file copiato ({0}) in {1}", "archivo copiado ({0}) a {1}",
            "fichier copié ({0}) vers {1}", "arquivo copiado ({0}) para {1}",
            "تم نسخ الملف ({0}) إلى {1}", "फ़ाइल ({0}) {1} में कॉपी हुई",
            "ফাইল ({0}) {1}-এ কপি হয়েছে", "فائل ({0}) {1} میں نقل ہوئی", "文件 ({0}) 已复制到 {1}")
        Add("!Ждите.. Файл переносится ({0}) в каталог {1}", "!Wait.. File moving ({0}) to {1}",
            "!Зачекайте.. Файл переміщується ({0}) до теки {1}", "!Warten.. Datei wird verschoben ({0}) nach {1}",
            "!Attendi.. spostamento del file ({0}) in {1}", "!Espere.. moviendo el archivo ({0}) a {1}",
            "!Patientez.. déplacement du fichier ({0}) vers {1}", "!Aguarde.. movendo o arquivo ({0}) para {1}",
            "!انتظر.. جارٍ نقل الملف ({0}) إلى {1}", "!प्रतीक्षा करें.. फ़ाइल ({0}) {1} में जा रही है",
            "!অপেক্ষা করুন.. ফাইল ({0}) {1}-এ যাচ্ছে", "!انتظار کریں.. فائل ({0}) {1} میں جا رہی ہے",
            "!请稍候.. 正在把文件 ({0}) 移动到 {1}")
        Add("файл перенесён ({0}) в каталог {1}", "file moved ({0}) to {1}",
            "файл переміщено ({0}) до теки {1}", "Datei verschoben ({0}) nach {1}",
            "file spostato ({0}) in {1}", "archivo movido ({0}) a {1}",
            "fichier déplacé ({0}) vers {1}", "arquivo movido ({0}) para {1}",
            "تم نقل الملف ({0}) إلى {1}", "फ़ाइल ({0}) {1} में चली गई",
            "ফাইল ({0}) {1}-এ সরানো হয়েছে", "فائل ({0}) {1} میں منتقل ہوئی", "文件 ({0}) 已移动到 {1}")
        Add("!Ждите. Файл удаляется в каталоге {0}", "!Wait. File deleting in {0}",
            "!Зачекайте. Файл видаляється в теці {0}", "!Warten. Datei wird gelöscht in {0}",
            "!Attendi. Eliminazione del file in {0}", "!Espere. Eliminando el archivo en {0}",
            "!Patientez. Suppression du fichier dans {0}", "!Aguarde. Excluindo o arquivo em {0}",
            "!انتظر. جارٍ حذف الملف في {0}", "!प्रतीक्षा करें। {0} में फ़ाइल हट रही है",
            "!অপেক্ষা করুন। {0}-এ ফাইল মোছা হচ্ছে", "!انتظار کریں۔ {0} میں فائل حذف ہو رہی ہے",
            "!请稍候。正在 {0} 中删除文件")
        Add("файл удалён в каталоге {0}", "file deleted in {0}", "файл видалено в теці {0}",
            "Datei gelöscht in {0}", "file eliminato in {0}", "archivo eliminado en {0}",
            "fichier supprimé dans {0}", "arquivo excluído em {0}", "تم حذف الملف في {0}",
            "{0} में फ़ाइल हटा दी गई", "{0}-এ ফাইল মোছা হয়েছে", "{0} میں فائل حذف ہو گئی", "已在 {0} 中删除文件")
        Add("!Ждите. Возвращается в каталог {0}", "!Wait. File back to {0}",
            "!Зачекайте. Повертається до теки {0}", "!Warten. Datei geht zurück nach {0}",
            "!Attendi. Il file torna in {0}", "!Espere. El archivo vuelve a {0}",
            "!Patientez. Le fichier revient vers {0}", "!Aguarde. O arquivo volta para {0}",
            "!انتظر. يعود الملف إلى {0}", "!प्रतीक्षा करें। फ़ाइल {0} में लौट रही है",
            "!অপেক্ষা করুন। ফাইল {0}-এ ফিরছে", "!انتظار کریں۔ فائل {0} میں واپس جا رہی ہے", "!请稍候。文件正在退回 {0}")
        Add("файл возвращён в каталог {0}", "file back to {0}", "файл повернуто до теки {0}",
            "Datei zurück nach {0}", "file tornato in {0}", "archivo devuelto a {0}",
            "fichier revenu vers {0}", "arquivo de volta em {0}", "عاد الملف إلى {0}",
            "फ़ाइल {0} में लौट आई", "ফাইল {0}-এ ফিরেছে", "فائل {0} میں واپس آ گئی", "文件已退回 {0}")
        Add("Ошибка операции: {0}", "Operation error: {0}", "Помилка операції: {0}",
            "Fehler beim Vorgang: {0}", "Errore nell'operazione: {0}", "Error en la operación: {0}",
            "Erreur d'opération : {0}", "Erro na operação: {0}", "خطأ في العملية: {0}",
            "कार्रवाई में त्रुटि: {0}", "কাজে ত্রুটি: {0}", "عمل میں خرابی: {0}", "操作出错：{0}")

        ' --- file / media loading ------------------------------------------------
        Add("Файл не найден: {0}", "File not found: {0}", "Файл не знайдено: {0}",
            "Datei nicht gefunden: {0}", "File non trovato: {0}", "Archivo no encontrado: {0}",
            "Fichier introuvable : {0}", "Arquivo não encontrado: {0}", "لم يُعثر على الملف: {0}",
            "फ़ाइल नहीं मिली: {0}", "ফাইল পাওয়া যায়নি: {0}", "فائل نہیں ملی: {0}", "未找到文件：{0}")
        Add("Файл пуст: {0}", "File is empty: {0}", "Файл порожній: {0}", "Datei ist leer: {0}",
            "Il file è vuoto: {0}", "El archivo está vacío: {0}", "Le fichier est vide : {0}",
            "O arquivo está vazio: {0}", "الملف فارغ: {0}", "फ़ाइल खाली है: {0}",
            "ফাইল খালি: {0}", "فائل خالی ہے: {0}", "文件为空：{0}")
        Add("Не удалось загрузить: {0}", "Failed to load: {0}", "Не вдалося завантажити: {0}",
            "Laden fehlgeschlagen: {0}", "Caricamento non riuscito: {0}", "No se pudo cargar: {0}",
            "Échec du chargement : {0}", "Falha ao carregar: {0}", "تعذّر التحميل: {0}",
            "लोड नहीं हो सका: {0}", "লোড করা যায়নি: {0}", "لوڈ نہ ہو سکا: {0}", "加载失败：{0}")
        Add("Недопустимый файл изображения: {0}", "Invalid image file: {0}", "Недопустимий файл зображення: {0}",
            "Ungültige Bilddatei: {0}", "File immagine non valido: {0}", "Archivo de imagen no válido: {0}",
            "Fichier image invalide : {0}", "Arquivo de imagem inválido: {0}", "ملف صورة غير صالح: {0}",
            "अमान्य छवि फ़ाइल: {0}", "অবৈধ ছবির ফাইল: {0}", "غلط تصویری فائل: {0}", "无效的图片文件：{0}")
        Add("Недостаточно памяти для загрузки: {0}", "Out of memory loading: {0}",
            "Недостатньо пам'яті для завантаження: {0}", "Zu wenig Speicher zum Laden: {0}",
            "Memoria insufficiente per caricare: {0}", "Memoria insuficiente para cargar: {0}",
            "Mémoire insuffisante pour charger : {0}", "Memória insuficiente para carregar: {0}",
            "الذاكرة غير كافية للتحميل: {0}", "लोड करने के लिए स्मृति कम है: {0}",
            "লোড করার মতো মেমরি নেই: {0}", "لوڈ کرنے کے لیے میموری کم ہے: {0}", "内存不足，无法加载：{0}")
        Add("Ошибка загрузки: {0}", "Loading error: {0}", "Помилка завантаження: {0}",
            "Ladefehler: {0}", "Errore di caricamento: {0}", "Error de carga: {0}",
            "Erreur de chargement : {0}", "Erro de carregamento: {0}", "خطأ في التحميل: {0}",
            "लोडिंग त्रुटि: {0}", "লোডিং ত্রুটি: {0}", "لوڈنگ خرابی: {0}", "加载出错：{0}")
        Add("Формат не поддерживается: {0}", "Unsupported format: {0}", "Формат не підтримується: {0}",
            "Format nicht unterstützt: {0}", "Formato non supportato: {0}", "Formato no compatible: {0}",
            "Format non pris en charge : {0}", "Formato não suportado: {0}", "الصيغة غير مدعومة: {0}",
            "यह प्रारूप समर्थित नहीं: {0}", "এই ফরম্যাট সমর্থিত নয়: {0}", "یہ فارمیٹ معاون نہیں: {0}", "不支持的格式：{0}")
        Add("Файл {0} перемещается назад операционной системой.", "File {0} moving back by OS.",
            "Файл {0} переміщується назад операційною системою.",
            "Datei {0} wird vom Betriebssystem zurückverschoben.",
            "Il file {0} viene riportato indietro dal sistema operativo.",
            "El sistema operativo está devolviendo el archivo {0}.",
            "Le système d'exploitation ramène le fichier {0}.",
            "O sistema operacional está devolvendo o arquivo {0}.",
            "نظام التشغيل يعيد الملف {0}.", "ऑपरेटिंग सिस्टम फ़ाइल {0} को वापस ले जा रहा है।",
            "অপারেটিং সিস্টেম ফাইল {0} ফিরিয়ে নিচ্ছে।", "آپریٹنگ سسٹم فائل {0} واپس لے جا رہا ہے۔",
            "操作系统正在把文件 {0} 移回。")
        Add("Ошибка файла, переход к следующему: {0}", "File error, moving to next: {0}",
            "Помилка файлу, перехід до наступного: {0}", "Dateifehler, weiter zur nächsten: {0}",
            "Errore del file, passo al successivo: {0}", "Error de archivo, pasando al siguiente: {0}",
            "Erreur de fichier, passage au suivant : {0}", "Erro no arquivo, indo para o próximo: {0}",
            "خطأ في الملف، الانتقال إلى التالي: {0}", "फ़ाइल त्रुटि, अगली पर जा रहे हैं: {0}",
            "ফাইলে ত্রুটি, পরেরটিতে যাচ্ছি: {0}", "فائل میں خرابی، اگلی پر جا رہے ہیں: {0}", "文件出错，跳到下一个：{0}")
        Add("Файл занят, ждём разблокировки (качается?): {0}",
            "File locked, waiting to open (downloading?): {0}",
            "Файл зайнятий, чекаємо розблокування (качається?): {0}",
            "Datei gesperrt, warte auf Freigabe (lädt sie gerade?): {0}",
            "File bloccato, attendo lo sblocco (in download?): {0}",
            "Archivo bloqueado, esperando a que se libere (¿se está descargando?): {0}",
            "Fichier verrouillé, en attente de libération (téléchargement ?) : {0}",
            "Arquivo bloqueado, aguardando liberar (está baixando?): {0}",
            "الملف مقفل، بانتظار تحريره (هل يجري تنزيله؟): {0}",
            "फ़ाइल लॉक है, खुलने की प्रतीक्षा (डाउनलोड हो रही है?): {0}",
            "ফাইল লক, খোলার অপেক্ষা (ডাউনলোড হচ্ছে?): {0}",
            "فائل مقفل ہے، کھلنے کا انتظار (ڈاؤن لوڈ ہو رہی ہے؟): {0}",
            "文件被占用，等待解锁（在下载吗？）：{0}")
        Add("Сетевой ресурс недоступен, повторите: {0}", "Share unreachable, retry: {0}",
            "Мережевий ресурс недоступний, повторіть: {0}", "Freigabe nicht erreichbar, erneut versuchen: {0}",
            "Condivisione irraggiungibile, riprova: {0}", "Recurso de red inaccesible, reintente: {0}",
            "Partage inaccessible, réessayez : {0}", "Compartilhamento inacessível, tente de novo: {0}",
            "المشاركة غير متاحة، أعد المحاولة: {0}", "नेटवर्क साझा उपलब्ध नहीं, फिर कोशिश करें: {0}",
            "নেটওয়ার্ক শেয়ার অনুপলব্ধ, আবার চেষ্টা করুন: {0}",
            "نیٹ ورک شیئر دستیاب نہیں، دوبارہ کوشش کریں: {0}", "网络共享不可达，请重试：{0}")
        Add("Проверяю доступность: {0}", "Checking availability: {0}", "Перевіряю доступність: {0}",
            "Verfügbarkeit wird geprüft: {0}", "Verifica della disponibilità: {0}",
            "Comprobando disponibilidad: {0}", "Vérification de la disponibilité : {0}",
            "Verificando disponibilidade: {0}", "جارٍ التحقق من التوفر: {0}",
            "उपलब्धता जाँची जा रही है: {0}", "উপলব্ধতা যাচাই হচ্ছে: {0}",
            "دستیابی جانچی جا رہی ہے: {0}", "正在检查可用性：{0}")
        Add("Не удалось открыть: {0}", "Could not open: {0}", "Не вдалося відкрити: {0}",
            "Konnte nicht geöffnet werden: {0}", "Impossibile aprire: {0}", "No se pudo abrir: {0}",
            "Impossible d'ouvrir : {0}", "Não foi possível abrir: {0}", "تعذّر الفتح: {0}",
            "खोला नहीं जा सका: {0}", "খোলা যায়নি: {0}", "کھولا نہ جا سکا: {0}", "无法打开：{0}")

        ' --- video / zoom / translate ---------------------------------------------
        Add("Видео открыто во внешнем плеере: {0}", "Video opened in external player: {0}",
            "Відео відкрито у зовнішньому плеєрі: {0}", "Video im externen Player geöffnet: {0}",
            "Video aperto nel lettore esterno: {0}", "Vídeo abierto en el reproductor externo: {0}",
            "Vidéo ouverte dans le lecteur externe : {0}", "Vídeo aberto no player externo: {0}",
            "تم فتح الفيديو في مشغّل خارجي: {0}", "वीडियो बाहरी प्लेयर में खुला: {0}",
            "ভিডিও বাইরের প্লেয়ারে খোলা হয়েছে: {0}", "ویڈیو بیرونی پلیئر میں کھلا: {0}", "视频已在外部播放器中打开：{0}")
        Add("Ошибка запуска внешнего плеера: {0}", "Error launching external player: {0}",
            "Помилка запуску зовнішнього плеєра: {0}", "Fehler beim Starten des externen Players: {0}",
            "Errore nell'avvio del lettore esterno: {0}", "Error al iniciar el reproductor externo: {0}",
            "Erreur au lancement du lecteur externe : {0}", "Erro ao iniciar o player externo: {0}",
            "خطأ في تشغيل المشغّل الخارجي: {0}", "बाहरी प्लेयर शुरू करने में त्रुटि: {0}",
            "বাইরের প্লেয়ার চালুতে ত্রুটি: {0}", "بیرونی پلیئر چلانے میں خرابی: {0}", "启动外部播放器出错：{0}")
        Add("Видео воспроизводится через VLC: {0}", "Playing via VLC: {0}", "Відео відтворюється через VLC: {0}",
            "Wiedergabe über VLC: {0}", "Riproduzione tramite VLC: {0}", "Reproduciendo con VLC: {0}",
            "Lecture via VLC : {0}", "Reproduzindo via VLC: {0}", "التشغيل عبر VLC: {0}",
            "VLC से चल रहा है: {0}", "VLC দিয়ে চলছে: {0}", "VLC سے چل رہا ہے: {0}", "正在通过 VLC 播放：{0}")
        Add("Ошибка загрузки видео: {0}", "Error loading video: {0}", "Помилка завантаження відео: {0}",
            "Fehler beim Laden des Videos: {0}", "Errore nel caricamento del video: {0}",
            "Error al cargar el vídeo: {0}", "Erreur de chargement de la vidéo : {0}",
            "Erro ao carregar o vídeo: {0}", "خطأ في تحميل الفيديو: {0}", "वीडियो लोड करने में त्रुटि: {0}",
            "ভিডিও লোডে ত্রুটি: {0}", "ویڈیو لوڈ کرنے میں خرابی: {0}", "加载视频出错：{0}")
        Add("Вписать {0} %", "Fit {0} %", "Вписати {0} %", "Einpassen {0} %", "Adatta {0} %",
            "Ajustar {0} %", "Ajuster {0} %", "Ajustar {0} %", "ملاءمة {0} %", "फ़िट {0} %",
            "ফিট {0} %", "فٹ {0} %", "适应 {0} %")
        Add("Не удалось открыть перевод (код {0})", "Could not open the translation (code {0})",
            "Не вдалося відкрити переклад (код {0})", "Übersetzung konnte nicht geöffnet werden (Code {0})",
            "Impossibile aprire la traduzione (codice {0})", "No se pudo abrir la traducción (código {0})",
            "Impossible d'ouvrir la traduction (code {0})", "Não foi possível abrir a tradução (código {0})",
            "تعذّر فتح الترجمة (الرمز {0})", "अनुवाद नहीं खुल सका (कोड {0})",
            "অনুবাদ খোলা যায়নি (কোড {0})", "ترجمہ نہ کھل سکا (کوڈ {0})", "无法打开翻译（代码 {0}）")

        ' --- multi-line messages ---------------------------------------------------
        Add("Зарегистрировано: {0}" & vbCrLf & "Ошибок: {1} ({2})",
            "Registered: {0}" & vbCrLf & "Errors: {1} ({2})",
            "Зареєстровано: {0}" & vbCrLf & "Помилок: {1} ({2})",
            "Registriert: {0}" & vbCrLf & "Fehler: {1} ({2})",
            "Registrati: {0}" & vbCrLf & "Errori: {1} ({2})",
            "Registrados: {0}" & vbCrLf & "Errores: {1} ({2})",
            "Enregistrés : {0}" & vbCrLf & "Erreurs : {1} ({2})",
            "Registrados: {0}" & vbCrLf & "Erros: {1} ({2})",
            "المسجّلة: {0}" & vbCrLf & "الأخطاء: {1} ({2})",
            "पंजीकृत: {0}" & vbCrLf & "त्रुटियाँ: {1} ({2})",
            "নিবন্ধিত: {0}" & vbCrLf & "ত্রুটি: {1} ({2})",
            "رجسٹرڈ: {0}" & vbCrLf & "خرابیاں: {1} ({2})",
            "已注册：{0}" & vbCrLf & "错误：{1}（{2}）")
        Add("Не удалось запустить Fast Media Sorter: Share Manager." & vbCrLf & "{0}",
            "Could not start Fast Media Sorter: Share Manager." & vbCrLf & "{0}",
            "Не вдалося запустити Fast Media Sorter: Share Manager." & vbCrLf & "{0}",
            "Fast Media Sorter: Share Manager konnte nicht gestartet werden." & vbCrLf & "{0}",
            "Impossibile avviare Fast Media Sorter: Share Manager." & vbCrLf & "{0}",
            "No se pudo iniciar Fast Media Sorter: Share Manager." & vbCrLf & "{0}",
            "Impossible de démarrer Fast Media Sorter : Share Manager." & vbCrLf & "{0}",
            "Não foi possível iniciar o Fast Media Sorter: Share Manager." & vbCrLf & "{0}",
            "تعذّر تشغيل Fast Media Sorter: Share Manager." & vbCrLf & "{0}",
            "Fast Media Sorter: Share Manager शुरू नहीं हो सका।" & vbCrLf & "{0}",
            "Fast Media Sorter: Share Manager চালু করা যায়নি।" & vbCrLf & "{0}",
            "Fast Media Sorter: Share Manager شروع نہ ہو سکا۔" & vbCrLf & "{0}",
            "无法启动 Fast Media Sorter: Share Manager。" & vbCrLf & "{0}")
        Add("Не понимаю эту схему адреса. Поддерживаются: " & vbLf & "{0}",
            "That address scheme is not supported. These are: " & vbLf & "{0}",
            "Не розумію цю схему адреси. Підтримуються: " & vbLf & "{0}",
            "Dieses Adressschema wird nicht unterstützt. Unterstützt werden: " & vbLf & "{0}",
            "Questo schema di indirizzo non è supportato. Sono supportati: " & vbLf & "{0}",
            "Ese esquema de dirección no es compatible. Se admiten: " & vbLf & "{0}",
            "Ce schéma d'adresse n'est pas pris en charge. Sont pris en charge : " & vbLf & "{0}",
            "Esse esquema de endereço não é suportado. São suportados: " & vbLf & "{0}",
            "مخطط العنوان هذا غير مدعوم. المدعوم: " & vbLf & "{0}",
            "यह पता-योजना समर्थित नहीं है। समर्थित हैं: " & vbLf & "{0}",
            "এই ঠিকানা-স্কিম সমর্থিত নয়। সমর্থিত: " & vbLf & "{0}",
            "یہ ایڈریس اسکیم معاون نہیں۔ معاون ہیں: " & vbLf & "{0}",
            "不支持这种地址格式。支持的有：" & vbLf & "{0}")
        Add("Двойной клик по номеру клавиши - выполнить действие." & vbCrLf & "Двойной клик по пути к папке - сменить её (одинарный клик она вежливо проигнорирует).",
            "Double-click a key number to run the action." & vbCrLf & "Double-click a folder path to change it (single clicks are politely ignored).",
            "Подвійний клік по номеру клавіші - виконати дію." & vbCrLf & "Подвійний клік по шляху до теки - змінити її (одинарний клік вона ввічливо проігнорує).",
            "Doppelklick auf eine Tastennummer führt die Aktion aus." & vbCrLf & "Doppelklick auf einen Ordnerpfad ändert ihn (Einfachklicks werden höflich ignoriert).",
            "Doppio clic su un numero di tasto per eseguire l'azione." & vbCrLf & "Doppio clic su un percorso per cambiarlo (i clic singoli vengono garbatamente ignorati).",
            "Doble clic en un número de tecla para ejecutar la acción." & vbCrLf & "Doble clic en una ruta para cambiarla (los clics simples se ignoran cortésmente).",
            "Double-cliquez sur un numéro de touche pour exécuter l'action." & vbCrLf & "Double-cliquez sur un chemin pour le changer (les simples clics sont poliment ignorés).",
            "Clique duas vezes num número de tecla para executar a ação." & vbCrLf & "Clique duas vezes num caminho para trocá-lo (cliques simples são educadamente ignorados).",
            "انقر نقرًا مزدوجًا على رقم المفتاح لتنفيذ الإجراء." & vbCrLf & "وانقر نقرًا مزدوجًا على مسار المجلد لتغييره (النقرة المفردة تُتجاهل بلطف).",
            "क्रिया चलाने के लिए कुंजी संख्या पर डबल-क्लिक करें।" & vbCrLf & "फ़ोल्डर पथ बदलने के लिए उस पर डबल-क्लिक करें (एकल क्लिक शालीनता से अनदेखा किया जाता है)।",
            "কাজ চালাতে কী নম্বরে ডাবল-ক্লিক করুন।" & vbCrLf & "ফোল্ডার পাথ বদলাতে তাতে ডাবল-ক্লিক করুন (একক ক্লিক ভদ্রভাবে উপেক্ষা করা হয়)।",
            "عمل چلانے کے لیے کلید نمبر پر ڈبل کلک کریں۔" & vbCrLf & "فولڈر کا راستہ بدلنے کے لیے اس پر ڈبل کلک کریں (واحد کلک شائستگی سے نظرانداز ہوتا ہے)۔",
            "双击键号执行操作。" & vbCrLf & "双击文件夹路径可更换它（单击会被礼貌地忽略）。")
        Add("ЛКМ: Выбрать изображение" & vbCrLf & "Ctrl+ЛКМ: Добавить/убрать из выделения" & vbCrLf & "Двойной клик: Открыть изображение в главном окне" & vbCrLf & "Del: Удалить выделенные файлы (без лишних церемоний)" & vbCrLf & "Цифры (0-9): Переместить/копировать выделенные файлы" & vbCrLf & "Esc: Закрыть эту панель и сделать вид, что её не было",
            "Left click: select an image" & vbCrLf & "Ctrl+click: add to / remove from the selection" & vbCrLf & "Double click: open the image in the main window" & vbCrLf & "Del: delete the selected files (no ceremony)" & vbCrLf & "Digits (0-9): move/copy the selected files" & vbCrLf & "Esc: close this panel and pretend it was never here",
            "ЛКМ: вибрати зображення" & vbCrLf & "Ctrl+клік: додати/прибрати з виділення" & vbCrLf & "Подвійний клік: відкрити зображення в головному вікні" & vbCrLf & "Del: видалити виділені файли (без зайвих церемоній)" & vbCrLf & "Цифри (0-9): перемістити/копіювати виділені файли" & vbCrLf & "Esc: закрити цю панель і вдати, що її не було",
            "Linksklick: Bild auswählen" & vbCrLf & "Strg+Klick: zur Auswahl hinzufügen/entfernen" & vbCrLf & "Doppelklick: Bild im Hauptfenster öffnen" & vbCrLf & "Entf: ausgewählte Dateien löschen (ohne Umschweife)" & vbCrLf & "Ziffern (0-9): ausgewählte Dateien verschieben/kopieren" & vbCrLf & "Esc: dieses Panel schließen und so tun, als wäre es nie da gewesen",
            "Clic sinistro: seleziona un'immagine" & vbCrLf & "Ctrl+clic: aggiungi/togli dalla selezione" & vbCrLf & "Doppio clic: apri l'immagine nella finestra principale" & vbCrLf & "Canc: elimina i file selezionati (senza cerimonie)" & vbCrLf & "Cifre (0-9): sposta/copia i file selezionati" & vbCrLf & "Esc: chiudi questo pannello e fai finta di niente",
            "Clic izquierdo: seleccionar una imagen" & vbCrLf & "Ctrl+clic: añadir o quitar de la selección" & vbCrLf & "Doble clic: abrir la imagen en la ventana principal" & vbCrLf & "Supr: eliminar los archivos seleccionados (sin ceremonias)" & vbCrLf & "Dígitos (0-9): mover o copiar los archivos seleccionados" & vbCrLf & "Esc: cerrar este panel y fingir que nunca estuvo",
            "Clic gauche : sélectionner une image" & vbCrLf & "Ctrl+clic : ajouter/retirer de la sélection" & vbCrLf & "Double clic : ouvrir l'image dans la fenêtre principale" & vbCrLf & "Suppr : supprimer les fichiers sélectionnés (sans cérémonie)" & vbCrLf & "Chiffres (0-9) : déplacer/copier les fichiers sélectionnés" & vbCrLf & "Échap : fermer ce panneau et faire comme s'il n'avait jamais existé",
            "Clique esquerdo: selecionar uma imagem" & vbCrLf & "Ctrl+clique: adicionar/remover da seleção" & vbCrLf & "Clique duplo: abrir a imagem na janela principal" & vbCrLf & "Del: excluir os arquivos selecionados (sem cerimônia)" & vbCrLf & "Dígitos (0-9): mover/copiar os arquivos selecionados" & vbCrLf & "Esc: fechar este painel e fingir que ele nunca esteve aqui",
            "النقر الأيسر: اختيار صورة" & vbCrLf & "Ctrl+نقر: إضافة/إزالة من التحديد" & vbCrLf & "نقر مزدوج: فتح الصورة في النافذة الرئيسية" & vbCrLf & "Del: حذف الملفات المحددة (بلا مقدمات)" & vbCrLf & "الأرقام (0-9): نقل/نسخ الملفات المحددة" & vbCrLf & "Esc: إغلاق هذه اللوحة والتظاهر بأنها لم تكن",
            "बायाँ क्लिक: छवि चुनें" & vbCrLf & "Ctrl+क्लिक: चयन में जोड़ें/हटाएँ" & vbCrLf & "डबल क्लिक: छवि मुख्य विंडो में खोलें" & vbCrLf & "Del: चयनित फ़ाइलें हटाएँ (बिना औपचारिकता)" & vbCrLf & "अंक (0-9): चयनित फ़ाइलें ले जाएँ/कॉपी करें" & vbCrLf & "Esc: यह पैनल बंद करें और भूल जाएँ कि यह था",
            "বাঁ ক্লিক: ছবি নির্বাচন" & vbCrLf & "Ctrl+ক্লিক: নির্বাচনে যোগ/বাদ" & vbCrLf & "ডাবল ক্লিক: মূল উইন্ডোতে ছবি খুলুন" & vbCrLf & "Del: নির্বাচিত ফাইল মুছুন (কোনো আনুষ্ঠানিকতা ছাড়াই)" & vbCrLf & "সংখ্যা (0-9): নির্বাচিত ফাইল সরান/কপি করুন" & vbCrLf & "Esc: এই প্যানেল বন্ধ করুন, যেন ছিলই না",
            "بایاں کلک: تصویر منتخب کریں" & vbCrLf & "Ctrl+کلک: انتخاب میں شامل/خارج کریں" & vbCrLf & "ڈبل کلک: تصویر مرکزی ونڈو میں کھولیں" & vbCrLf & "Del: منتخب فائلیں حذف کریں (بغیر تکلف)" & vbCrLf & "ہندسے (0-9): منتخب فائلیں منتقل/نقل کریں" & vbCrLf & "Esc: یہ پینل بند کریں اور بھول جائیں کہ تھا",
            "左键：选择图片" & vbCrLf & "Ctrl+点击：加入/移出选区" & vbCrLf & "双击：在主窗口打开图片" & vbCrLf & "Del：删除选中的文件（干脆利落）" & vbCrLf & "数字 (0-9)：移动/复制选中的文件" & vbCrLf & "Esc：关掉这个面板，就当它没来过")
        Add("Установить бесплатный перевод изображения в браузере (doc-html-translate)?" & vbCrLf & "" & vbCrLf & "Да - установить через winget." & vbCrLf & "Нет - открыть страницу в магазине приложений.",
            "Install free in-browser image translation (doc-html-translate)?" & vbCrLf & "" & vbCrLf & "Yes - install it with winget." & vbCrLf & "No - open its page in the app store.",
            "Встановити безкоштовний переклад зображення в браузері (doc-html-translate)?" & vbCrLf & "" & vbCrLf & "Так - встановити через winget." & vbCrLf & "Ні - відкрити сторінку в магазині застосунків.",
            "Kostenlose Bildübersetzung im Browser installieren (doc-html-translate)?" & vbCrLf & "" & vbCrLf & "Ja - mit winget installieren." & vbCrLf & "Nein - die Seite im App-Store öffnen.",
            "Installare la traduzione gratuita delle immagini nel browser (doc-html-translate)?" & vbCrLf & "" & vbCrLf & "Sì - installa con winget." & vbCrLf & "No - apri la pagina nello store.",
            "¿Instalar la traducción gratuita de imágenes en el navegador (doc-html-translate)?" & vbCrLf & "" & vbCrLf & "Sí: instalar con winget." & vbCrLf & "No: abrir su página en la tienda de aplicaciones.",
            "Installer la traduction d'images gratuite dans le navigateur (doc-html-translate) ?" & vbCrLf & "" & vbCrLf & "Oui - l'installer avec winget." & vbCrLf & "Non - ouvrir sa page dans la boutique d'applications.",
            "Instalar a tradução gratuita de imagens no navegador (doc-html-translate)?" & vbCrLf & "" & vbCrLf & "Sim - instalar com o winget." & vbCrLf & "Não - abrir a página na loja de aplicativos.",
            "هل تريد تثبيت ترجمة الصور المجانية في المتصفح (doc-html-translate)؟" & vbCrLf & "" & vbCrLf & "نعم - التثبيت عبر winget." & vbCrLf & "لا - فتح صفحته في متجر التطبيقات.",
            "ब्राउज़र में मुफ़्त छवि अनुवाद (doc-html-translate) इंस्टॉल करें?" & vbCrLf & "" & vbCrLf & "हाँ - winget से इंस्टॉल करें।" & vbCrLf & "नहीं - ऐप स्टोर में इसका पृष्ठ खोलें।",
            "ব্রাউজারে বিনামূল্যে ছবি অনুবাদ (doc-html-translate) ইনস্টল করবেন?" & vbCrLf & "" & vbCrLf & "হ্যাঁ - winget দিয়ে ইনস্টল করুন।" & vbCrLf & "না - অ্যাপ স্টোরে এর পাতা খুলুন।",
            "براؤزر میں مفت تصویری ترجمہ (doc-html-translate) انسٹال کریں؟" & vbCrLf & "" & vbCrLf & "ہاں - winget سے انسٹال کریں۔" & vbCrLf & "نہیں - ایپ اسٹور میں اس کا صفحہ کھولیں۔",
            "是否安装免费的浏览器图片翻译（doc-html-translate）？" & vbCrLf & "" & vbCrLf & "是 - 用 winget 安装。" & vbCrLf & "否 - 在应用商店打开它的页面。")
        Add("Адрес видеопотока или файла на сервере:" & vbLf & "" & vbLf & "smb://сервер/шара/фильм.mkv" & vbLf & "http://сервер/видео.mp4" & vbLf & "sftp://пользователь@сервер/путь/фильм.mkv" & vbLf & "" & vbLf & "Обычные сетевые папки (\\сервер\шара\..) открываются кнопкой ""Выбрать файл.."" как есть.",
            "Address of a video stream or a file on a server:" & vbLf & "" & vbLf & "smb://server/share/movie.mkv" & vbLf & "http://server/video.mp4" & vbLf & "sftp://user@server/path/movie.mkv" & vbLf & "" & vbLf & "Ordinary network folders (\\server\share\..) open with the ""Choose file.."" button as they are.",
            "Адреса відеопотоку або файлу на сервері:" & vbLf & "" & vbLf & "smb://сервер/шара/фільм.mkv" & vbLf & "http://сервер/відео.mp4" & vbLf & "sftp://користувач@сервер/шлях/фільм.mkv" & vbLf & "" & vbLf & "Звичайні мережеві теки (\\сервер\шара\..) відкриваються кнопкою ""Вибрати файл.."" як є.",
            "Adresse eines Videostreams oder einer Datei auf einem Server:" & vbLf & "" & vbLf & "smb://server/freigabe/film.mkv" & vbLf & "http://server/video.mp4" & vbLf & "sftp://benutzer@server/pfad/film.mkv" & vbLf & "" & vbLf & "Normale Netzwerkordner (\\server\freigabe\..) öffnen Sie wie gewohnt mit ""Datei wählen.."".",
            "Indirizzo di uno stream video o di un file su un server:" & vbLf & "" & vbLf & "smb://server/condivisione/film.mkv" & vbLf & "http://server/video.mp4" & vbLf & "sftp://utente@server/percorso/film.mkv" & vbLf & "" & vbLf & "Le cartelle di rete normali (\\server\condivisione\..) si aprono con il pulsante ""Scegli file.."" così come sono.",
            "Dirección de un flujo de vídeo o de un archivo en un servidor:" & vbLf & "" & vbLf & "smb://servidor/recurso/pelicula.mkv" & vbLf & "http://servidor/video.mp4" & vbLf & "sftp://usuario@servidor/ruta/pelicula.mkv" & vbLf & "" & vbLf & "Las carpetas de red normales (\\servidor\recurso\..) se abren con el botón ""Elegir archivo.."" tal cual.",
            "Adresse d'un flux vidéo ou d'un fichier sur un serveur :" & vbLf & "" & vbLf & "smb://serveur/partage/film.mkv" & vbLf & "http://serveur/video.mp4" & vbLf & "sftp://utilisateur@serveur/chemin/film.mkv" & vbLf & "" & vbLf & "Les dossiers réseau ordinaires (\\serveur\partage\..) s'ouvrent tels quels avec le bouton ""Choisir un fichier.."".",
            "Endereço de um fluxo de vídeo ou de um arquivo num servidor:" & vbLf & "" & vbLf & "smb://servidor/compartilhamento/filme.mkv" & vbLf & "http://servidor/video.mp4" & vbLf & "sftp://usuario@servidor/caminho/filme.mkv" & vbLf & "" & vbLf & "Pastas de rede comuns (\\servidor\compartilhamento\..) abrem com o botão ""Escolher arquivo.."" do jeito que estão.",
            "عنوان بث فيديو أو ملف على خادم:" & vbLf & "" & vbLf & "smb://server/share/movie.mkv" & vbLf & "http://server/video.mp4" & vbLf & "sftp://user@server/path/movie.mkv" & vbLf & "" & vbLf & "مجلدات الشبكة العادية (\\server\share\..) تُفتح بزر ""اختيار ملف.."" كما هي.",
            "सर्वर पर वीडियो स्ट्रीम या फ़ाइल का पता:" & vbLf & "" & vbLf & "smb://server/share/movie.mkv" & vbLf & "http://server/video.mp4" & vbLf & "sftp://user@server/path/movie.mkv" & vbLf & "" & vbLf & "साधारण नेटवर्क फ़ोल्डर (\\server\share\..) ""फ़ाइल चुनें.."" बटन से जस के तस खुलते हैं।",
            "সার্ভারে ভিডিও স্ট্রিম বা ফাইলের ঠিকানা:" & vbLf & "" & vbLf & "smb://server/share/movie.mkv" & vbLf & "http://server/video.mp4" & vbLf & "sftp://user@server/path/movie.mkv" & vbLf & "" & vbLf & "সাধারণ নেটওয়ার্ক ফোল্ডার (\\server\share\..) ""ফাইল নির্বাচন করুন.."" বোতাম দিয়েই খোলে।",
            "سرور پر ویڈیو اسٹریم یا فائل کا پتہ:" & vbLf & "" & vbLf & "smb://server/share/movie.mkv" & vbLf & "http://server/video.mp4" & vbLf & "sftp://user@server/path/movie.mkv" & vbLf & "" & vbLf & "عام نیٹ ورک فولڈرز (\\server\share\..) ""فائل منتخب کریں.."" بٹن سے جوں کے توں کھلتے ہیں۔",
            "服务器上视频流或文件的地址：" & vbLf & "" & vbLf & "smb://server/share/movie.mkv" & vbLf & "http://server/video.mp4" & vbLf & "sftp://user@server/path/movie.mkv" & vbLf & "" & vbLf & "普通的网络文件夹（\\server\share\..）用""选择文件..""按钮照常打开。")
    End Sub

End Class
