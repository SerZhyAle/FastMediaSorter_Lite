Option Strict On

' <summary>
' Strings of media loading, the loading indicator, URL opening, the Share
' launcher and the OCR / translation surfaces. See Localization.vb for the
' key convention and Localization.Main.vb for the argument order.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddMediaStrings()

        ' --- Main_Form.MediaLoading.vb -----------------------------------------
        Add("! Нет читаемых файлов в папке",
            "! No readable files in this folder", "! Немає придатних для читання файлів у теці",
            "! Keine lesbaren Dateien in diesem Ordner", "! Nessun file leggibile in questa cartella",
            "! No hay archivos legibles en esta carpeta", "! Aucun fichier lisible dans ce dossier",
            "! Nenhum arquivo legível nesta pasta", "! لا توجد ملفات قابلة للقراءة في هذا المجلد",
            "! इस फ़ोल्डर में कोई पढ़ने योग्य फ़ाइल नहीं", "! এই ফোল্ডারে পড়ার মতো ফাইল নেই",
            "! اس فولڈر میں کوئی قابلِ مطالعہ فائل نہیں", "! 此文件夹中没有可读文件")
        Add("!Ждите.. предыдущая операция ещё выполняется",
            "!Wait.. previous operation still running", "!Зачекайте.. попередня операція ще виконується",
            "!Warten.. der vorherige Vorgang läuft noch", "!Attendi.. l'operazione precedente è ancora in corso",
            "!Espere.. la operación anterior sigue en curso", "!Patientez.. l'opération précédente est en cours",
            "!Aguarde.. a operação anterior ainda está em execução",
            "!انتظر.. العملية السابقة ما زالت جارية", "!प्रतीक्षा करें.. पिछली प्रक्रिया अभी चल रही है",
            "!অপেক্ষা করুন.. আগের কাজ এখনও চলছে", "!انتظار کریں.. پچھلا عمل ابھی جاری ہے",
            "!请稍候.. 上一个操作仍在进行")
        Add("чтение каталога.. ждите!",
            "reading files.. wait!", "читання каталогу.. зачекайте!", "Dateien werden gelesen.. bitte warten!",
            "lettura dei file.. attendi!", "leyendo archivos.. ¡espere!", "lecture des fichiers.. patientez !",
            "lendo arquivos.. aguarde!", "جارٍ قراءة الملفات.. انتظر!", "फ़ाइलें पढ़ी जा रही हैं.. प्रतीक्षा करें!",
            "ফাইল পড়া হচ্ছে.. অপেক্ষা করুন!", "فائلیں پڑھی جا رہی ہیں.. انتظار کریں!", "正在读取文件.. 请稍候！")
        Add("! Нет файла для удаления",
            "! No file for deleting", "! Немає файлу для видалення", "! Keine Datei zum Löschen",
            "! Nessun file da eliminare", "! No hay archivo para eliminar", "! Aucun fichier à supprimer",
            "! Nenhum arquivo para excluir", "! لا يوجد ملف للحذف", "! हटाने के लिए कोई फ़ाइल नहीं",
            "! মোছার মতো কোনো ফাইল নেই", "! حذف کرنے کے لیے کوئی فائل نہیں", "! 没有可删除的文件")
        Add("Подтверждение удаления",
            "Deletion Confirmation", "Підтвердження видалення", "Löschbestätigung", "Conferma eliminazione",
            "Confirmación de eliminación", "Confirmation de suppression", "Confirmação de exclusão",
            "تأكيد الحذف", "हटाने की पुष्टि", "মোছার নিশ্চিতকরণ", "حذف کی تصدیق", "删除确认")
        Add("удалён: ", "file deleted: ", "видалено: ", "gelöscht: ", "eliminato: ", "eliminado: ",
            "supprimé : ", "excluído: ", "تم الحذف: ", "हटाया गया: ", "মোছা হয়েছে: ", "حذف ہوا: ", "已删除：")
        Add("! Файл не найден", "! File not found", "! Файл не знайдено", "! Datei nicht gefunden",
            "! File non trovato", "! Archivo no encontrado", "! Fichier introuvable",
            "! Arquivo não encontrado", "! لم يتم العثور على الملف", "! फ़ाइल नहीं मिली",
            "! ফাইল পাওয়া যায়নি", "! فائل نہیں ملی", "! 未找到文件")
        Add("! Ошибка чтения файлов", "! Error reading files", "! Помилка читання файлів",
            "! Fehler beim Lesen der Dateien", "! Errore nella lettura dei file",
            "! Error al leer los archivos", "! Erreur de lecture des fichiers",
            "! Erro ao ler os arquivos", "! خطأ في قراءة الملفات", "! फ़ाइलें पढ़ने में त्रुटि",
            "! ফাইল পড়তে ত্রুটি", "! فائلیں پڑھنے میں خرابی", "! 读取文件出错")
        Add("! Нет списка файлов", "! No file list available", "! Немає списку файлів",
            "! Keine Dateiliste verfügbar", "! Nessun elenco di file disponibile",
            "! No hay lista de archivos", "! Aucune liste de fichiers", "! Nenhuma lista de arquivos",
            "! لا توجد قائمة ملفات", "! फ़ाइल सूची उपलब्ध नहीं", "! ফাইলের তালিকা নেই",
            "! فائلوں کی فہرست دستیاب نہیں", "! 没有文件列表")
        Add("Ошибка получения имени файла",
            "Error getting file name", "Помилка отримання імені файлу", "Fehler beim Ermitteln des Dateinamens",
            "Errore nel recupero del nome del file", "Error al obtener el nombre del archivo",
            "Erreur lors de la récupération du nom du fichier", "Erro ao obter o nome do arquivo",
            "خطأ في الحصول على اسم الملف", "फ़ाइल का नाम प्राप्त करने में त्रुटि",
            "ফাইলের নাম পেতে ত্রুটি", "فائل کا نام حاصل کرنے میں خرابی", "获取文件名出错")
        Add("Файл не найден, переход к следующему",
            "File not found, moving to next", "Файл не знайдено, перехід до наступного",
            "Datei nicht gefunden, weiter zur nächsten", "File non trovato, passo al successivo",
            "Archivo no encontrado, pasando al siguiente", "Fichier introuvable, passage au suivant",
            "Arquivo não encontrado, indo para o próximo", "الملف غير موجود، الانتقال إلى التالي",
            "फ़ाइल नहीं मिली, अगली पर जा रहे हैं", "ফাইল পাওয়া যায়নি, পরেরটিতে যাচ্ছি",
            "فائل نہیں ملی، اگلی پر جا رہے ہیں", "未找到文件，跳到下一个")
        Add(" из ", " from ", " з ", " von ", " di ", " de ", " sur ", " de ", " من ", " / ", " / ", " از ", " / ")
        Add("Текущий: ", "Current: ", "Поточний: ", "Aktuell: ", "Corrente: ", "Actual: ",
            "Actuel : ", "Atual: ", "الحالي: ", "मौजूदा: ", "বর্তমান: ", "موجودہ: ", "当前：")
        Add("! Нет файлов в папке", "! No files in folder", "! Немає файлів у теці",
            "! Keine Dateien im Ordner", "! Nessun file nella cartella", "! No hay archivos en la carpeta",
            "! Aucun fichier dans le dossier", "! Nenhum arquivo na pasta", "! لا توجد ملفات في المجلد",
            "! फ़ोल्डर में कोई फ़ाइल नहीं", "! ফোল্ডারে কোনো ফাইল নেই", "! فولڈر میں کوئی فائل نہیں",
            "! 文件夹中没有文件")

        ' --- Main_Form.LoadingIndicator.vb --------------------------------------
        Add("Загрузка: ", "Loading: ", "Завантаження: ", "Wird geladen: ", "Caricamento: ",
            "Cargando: ", "Chargement : ", "Carregando: ", "جارٍ التحميل: ", "लोड हो रहा है: ",
            "লোড হচ্ছে: ", "لوڈ ہو رہا ہے: ", "正在加载：")
        Add("Загрузка.. ", "Loading.. ", "Завантаження.. ", "Wird geladen.. ", "Caricamento.. ",
            "Cargando.. ", "Chargement.. ", "Carregando.. ", "جارٍ التحميل.. ", "लोड हो रहा है.. ",
            "লোড হচ্ছে.. ", "لوڈ ہو رہا ہے.. ", "正在加载.. ")
        Add(" с", " s", " с", " s", " s", " s", " s", " s", " ث", " से", " সে", " س", " 秒")
        Add(" МиБ", " MiB", " МіБ", " MiB", " MiB", " MiB", " Mio", " MiB", " ميب", " MiB", " MiB", " MiB", " MiB")
        Add(" КиБ", " KiB", " КіБ", " KiB", " KiB", " KiB", " Kio", " KiB", " كيب", " KiB", " KiB", " KiB", " KiB")
        Add(" Б", " B", " Б", " B", " B", " B", " o", " B", " بايت", " B", " B", " B", " B")

        ' --- Main_Form.OpenUrl.vb ------------------------------------------------
        Add("Открыть URL..", "Open URL..", "Відкрити URL..", "URL öffnen..", "Apri URL..",
            "Abrir URL..", "Ouvrir une URL..", "Abrir URL..", "فتح رابط..", "URL खोलें..",
            "URL খুলুন..", "URL کھولیں..", "打开网址..")
        Add("Выбрать файл..  (правый клик - открыть URL)",
            "Choose file..  (right-click to open a URL)", "Вибрати файл..  (правий клік - відкрити URL)",
            "Datei wählen..  (Rechtsklick öffnet eine URL)", "Scegli file..  (tasto destro per aprire un URL)",
            "Elegir archivo..  (clic derecho para abrir una URL)",
            "Choisir un fichier..  (clic droit pour ouvrir une URL)",
            "Escolher arquivo..  (clique com o botão direito para abrir uma URL)",
            "اختيار ملف..  (الزر الأيمن لفتح رابط)", "फ़ाइल चुनें..  (URL खोलने के लिए दायाँ क्लिक)",
            "ফাইল নির্বাচন করুন..  (URL খুলতে ডান ক্লিক)", "فائل منتخب کریں..  (URL کھولنے کے لیے دایاں کلک)",
            "选择文件..（右键打开网址）")
        Add("Открыть URL", "Open URL", "Відкрити URL", "URL öffnen", "Apri URL", "Abrir URL",
            "Ouvrir une URL", "Abrir URL", "فتح رابط", "URL खोलें", "URL খুলুন", "URL کھولیں", "打开网址")
        Add("Это обычный путь, а не URL. Откройте его кнопкой ""Выбрать файл..""."   ,
            "That is an ordinary path, not a URL. Open it with ""Choose file..""."   ,
            "Це звичайний шлях, а не URL. Відкрийте його кнопкою ""Вибрати файл..""."   ,
            "Das ist ein normaler Pfad, keine URL. Öffnen Sie ihn mit ""Datei wählen..""."   ,
            "Questo è un percorso normale, non un URL. Aprilo con ""Scegli file..""."   ,
            "Es una ruta normal, no una URL. Ábrala con ""Elegir archivo..""."   ,
            "Ceci est un chemin ordinaire, pas une URL. Ouvrez-le avec ""Choisir un fichier..""."   ,
            "Isso é um caminho comum, não uma URL. Abra com ""Escolher arquivo..""."   ,
            "هذا مسار عادي وليس رابطًا. افتحه عبر ""اختيار ملف..""."   ,
            "यह सामान्य पथ है, URL नहीं। इसे ""फ़ाइल चुनें.."" से खोलें।",
            "এটি সাধারণ পাথ, URL নয়। ""ফাইল নির্বাচন করুন.."" দিয়ে খুলুন।",
            "یہ عام راستہ ہے، URL نہیں۔ اسے ""فائل منتخب کریں.."" سے کھولیں۔",
            "这是普通路径，不是网址。请用""选择文件..""打开。")
        Add("По URL можно открыть только видео. Картинку скачайте и откройте файлом.",
            "Only video can be opened by URL. Download a picture and open it as a file.",
            "За URL можна відкрити лише відео. Зображення завантажте й відкрийте файлом.",
            "Per URL lässt sich nur Video öffnen. Ein Bild bitte herunterladen und als Datei öffnen.",
            "Da URL si possono aprire solo video. Scarica l'immagine e aprila come file.",
            "Por URL solo se puede abrir vídeo. Descargue la imagen y ábrala como archivo.",
            "Seule la vidéo peut être ouverte par URL. Téléchargez l'image et ouvrez-la en tant que fichier.",
            "Só vídeo pode ser aberto por URL. Baixe a imagem e abra como arquivo.",
            "يمكن فتح الفيديو فقط عبر الرابط. نزّل الصورة وافتحها كملف.",
            "URL से केवल वीडियो खुल सकता है। चित्र डाउनलोड कर फ़ाइल की तरह खोलें।",
            "URL দিয়ে কেবল ভিডিও খোলা যায়। ছবি ডাউনলোড করে ফাইল হিসেবে খুলুন।",
            "URL سے صرف ویڈیو کھل سکتی ہے۔ تصویر ڈاؤن لوڈ کر کے فائل کے طور پر کھولیں۔",
            "网址只能打开视频。图片请下载后作为文件打开。")
        Add("Подключение..", "Connecting..", "Підключення..", "Verbinden..", "Connessione..",
            "Conectando..", "Connexion..", "Conectando..", "جارٍ الاتصال..", "कनेक्ट हो रहा है..",
            "সংযোগ করা হচ্ছে..", "منسلک ہو رہا ہے..", "正在连接..")

        ' --- Main_Form.ShareLauncher.vb ------------------------------------------
        ' Share is an optional installer component, so "not found" is not automatically a
        ' broken install - it is usually a compact / unattended one. The message offers the
        ' fix (run the full setup over this version) instead of "please reinstall".
        Add("Компонент общего доступа не установлен (Fast Media Sorter: Share Manager)." & vbCrLf & vbCrLf &
            "Он входит в полный установщик: запустите его поверх текущей версии - настройки сохранятся." & vbCrLf & vbCrLf &
            "Открыть страницу загрузки?",
            "The folder-sharing component is not installed (Fast Media Sorter: Share Manager)." & vbCrLf & vbCrLf &
            "It comes with the full installer: run it over your current version - your settings are kept." & vbCrLf & vbCrLf &
            "Open the download page?",
            "Компонент спільного доступу не встановлено (Fast Media Sorter: Share Manager)." & vbCrLf & vbCrLf &
            "Він входить до повного установника: запустіть його поверх поточної версії - налаштування збережуться." & vbCrLf & vbCrLf &
            "Відкрити сторінку завантаження?",
            "Die Komponente für die Ordnerfreigabe ist nicht installiert (Fast Media Sorter: Share Manager)." & vbCrLf & vbCrLf &
            "Sie gehört zum vollständigen Installationsprogramm: Führen Sie es über die aktuelle Version aus - Ihre Einstellungen bleiben erhalten." & vbCrLf & vbCrLf &
            "Download-Seite öffnen?",
            "Il componente di condivisione cartelle non è installato (Fast Media Sorter: Share Manager)." & vbCrLf & vbCrLf &
            "Fa parte del programma di installazione completo: eseguilo sulla versione attuale - le impostazioni vengono mantenute." & vbCrLf & vbCrLf &
            "Aprire la pagina di download?",
            "El componente de uso compartido de carpetas no está instalado (Fast Media Sorter: Share Manager)." & vbCrLf & vbCrLf &
            "Viene con el instalador completo: ejecútelo sobre la versión actual - se conservan sus ajustes." & vbCrLf & vbCrLf &
            "¿Abrir la página de descarga?",
            "Le composant de partage de dossiers n'est pas installé (Fast Media Sorter: Share Manager)." & vbCrLf & vbCrLf &
            "Il fait partie du programme d'installation complet : lancez-le par-dessus la version actuelle - vos réglages sont conservés." & vbCrLf & vbCrLf &
            "Ouvrir la page de téléchargement ?",
            "O componente de compartilhamento de pastas não está instalado (Fast Media Sorter: Share Manager)." & vbCrLf & vbCrLf &
            "Ele vem com o instalador completo: execute-o sobre a versão atual - suas configurações são mantidas." & vbCrLf & vbCrLf &
            "Abrir a página de download?",
            "مكوّن مشاركة المجلدات غير مثبَّت (Fast Media Sorter: Share Manager)." & vbCrLf & vbCrLf &
            "وهو جزء من المثبِّت الكامل: شغّله فوق النسخة الحالية - وستبقى إعداداتك كما هي." & vbCrLf & vbCrLf &
            "هل تفتح صفحة التنزيل؟",
            "फ़ोल्डर साझाकरण घटक इंस्टॉल नहीं है (Fast Media Sorter: Share Manager)।" & vbCrLf & vbCrLf &
            "यह पूर्ण इंस्टॉलर के साथ आता है: इसे मौजूदा संस्करण के ऊपर चलाएँ - आपकी सेटिंग्स बनी रहेंगी।" & vbCrLf & vbCrLf &
            "डाउनलोड पेज खोलें?",
            "ফোল্ডার শেয়ারিং উপাদান ইনস্টল করা নেই (Fast Media Sorter: Share Manager)।" & vbCrLf & vbCrLf &
            "এটি সম্পূর্ণ ইনস্টলারের সঙ্গে আসে: বর্তমান সংস্করণের উপরে চালান - আপনার সেটিংস থাকবে।" & vbCrLf & vbCrLf &
            "ডাউনলোড পেজ খুলবেন?",
            "فولڈر شیئرنگ کا جزو انسٹال نہیں ہے (Fast Media Sorter: Share Manager)۔" & vbCrLf & vbCrLf &
            "یہ مکمل انسٹالر کے ساتھ آتا ہے: اسے موجودہ ورژن کے اوپر چلائیں - آپ کی ترتیبات محفوظ رہیں گی۔" & vbCrLf & vbCrLf &
            "ڈاؤن لوڈ صفحہ کھولیں؟",
            "文件夹共享组件未安装（Fast Media Sorter: Share Manager）。" & vbCrLf & vbCrLf &
            "它包含在完整安装程序中：在当前版本上直接运行即可 - 设置会保留。" & vbCrLf & vbCrLf &
            "打开下载页面？")
        Add("Общий доступ", "Folder sharing", "Спільний доступ", "Ordnerfreigabe", "Condivisione cartelle",
            "Uso compartido de carpetas", "Partage de dossiers", "Compartilhamento de pastas",
            "مشاركة المجلدات", "फ़ोल्डर साझाकरण", "ফোল্ডার শেয়ারিং", "فولڈر شیئرنگ", "文件夹共享")

        ' --- Main_Form.BrowserTranslate.vb ---------------------------------------
        Add("В браузере", "Browser", "У браузері", "Im Browser", "Nel browser", "En el navegador",
            "Dans le navigateur", "No navegador", "في المتصفح", "ब्राउज़र में", "ব্রাউজারে",
            "براؤزر میں", "在浏览器中")
        Add("Открыть картинку в браузере с бесплатным переводом Google (doc-html-translate). ЛКМ - открыть. Shift - заново распознать.",
            "Open the image in the browser with free Google translation (doc-html-translate). Click to open. Shift = re-OCR.",
            "Відкрити зображення в браузері з безкоштовним перекладом Google (doc-html-translate). ЛКМ - відкрити. Shift - розпізнати заново.",
            "Bild im Browser mit kostenloser Google-Übersetzung öffnen (doc-html-translate). Klick zum Öffnen. Shift = erneut OCR.",
            "Apri l'immagine nel browser con la traduzione gratuita di Google (doc-html-translate). Clic per aprire. Shift = ripeti OCR.",
            "Abrir la imagen en el navegador con la traducción gratuita de Google (doc-html-translate). Clic para abrir. Mayús = repetir OCR.",
            "Ouvrir l'image dans le navigateur avec la traduction Google gratuite (doc-html-translate). Clic pour ouvrir. Maj = refaire l'OCR.",
            "Abrir a imagem no navegador com a tradução gratuita do Google (doc-html-translate). Clique para abrir. Shift = refazer OCR.",
            "افتح الصورة في المتصفح مع ترجمة Google المجانية (doc-html-translate). انقر للفتح. Shift = إعادة التعرف.",
            "छवि को ब्राउज़र में मुफ़्त Google अनुवाद के साथ खोलें (doc-html-translate)। खोलने के लिए क्लिक। Shift = फिर से OCR।",
            "ছবিটি ব্রাউজারে বিনামূল্যে Google অনুবাদসহ খুলুন (doc-html-translate)। খুলতে ক্লিক। Shift = আবার OCR।",
            "تصویر کو براؤزر میں مفت Google ترجمے کے ساتھ کھولیں (doc-html-translate)۔ کھولنے کے لیے کلک۔ Shift = دوبارہ OCR۔",
            "在浏览器中打开图片并使用免费的 Google 翻译（doc-html-translate）。点击打开，Shift 重新 OCR。")
        Add("Открываю в браузере (doc-html-translate)..",
            "Opening in the browser (doc-html-translate)..", "Відкриваю в браузері (doc-html-translate)..",
            "Wird im Browser geöffnet (doc-html-translate)..", "Apertura nel browser (doc-html-translate)..",
            "Abriendo en el navegador (doc-html-translate)..", "Ouverture dans le navigateur (doc-html-translate)..",
            "Abrindo no navegador (doc-html-translate)..", "جارٍ الفتح في المتصفح (doc-html-translate)..",
            "ब्राउज़र में खोला जा रहा है (doc-html-translate)..", "ব্রাউজারে খোলা হচ্ছে (doc-html-translate)..",
            "براؤزر میں کھولا جا رہا ہے (doc-html-translate)..", "正在浏览器中打开（doc-html-translate）..")
        Add("Не удалось запустить doc-html-translate",
            "Could not launch doc-html-translate", "Не вдалося запустити doc-html-translate",
            "doc-html-translate konnte nicht gestartet werden", "Impossibile avviare doc-html-translate",
            "No se pudo iniciar doc-html-translate", "Impossible de lancer doc-html-translate",
            "Não foi possível iniciar o doc-html-translate", "تعذّر تشغيل doc-html-translate",
            "doc-html-translate शुरू नहीं हो सका", "doc-html-translate চালু করা যায়নি",
            "doc-html-translate چل نہ سکا", "无法启动 doc-html-translate")
        Add("Настройки OCR..", "OCR settings..", "Налаштування OCR..", "OCR-Einstellungen..",
            "Impostazioni OCR..", "Configuración de OCR..", "Paramètres OCR..", "Configurações de OCR..",
            "إعدادات OCR..", "OCR सेटिंग्स..", "OCR সেটিংস..", "OCR ترتیبات..", "OCR 设置..")
        Add("Перевод в браузере - установить doc-html-translate..",
            "Translate in browser - install doc-html-translate..",
            "Переклад у браузері - встановити doc-html-translate..",
            "Im Browser übersetzen - doc-html-translate installieren..",
            "Traduci nel browser - installa doc-html-translate..",
            "Traducir en el navegador: instalar doc-html-translate..",
            "Traduire dans le navigateur - installer doc-html-translate..",
            "Traduzir no navegador - instalar doc-html-translate..",
            "الترجمة في المتصفح - ثبّت doc-html-translate..",
            "ब्राउज़र में अनुवाद - doc-html-translate इंस्टॉल करें..",
            "ব্রাউজারে অনুবাদ - doc-html-translate ইনস্টল করুন..",
            "براؤزر میں ترجمہ - doc-html-translate انسٹال کریں..",
            "在浏览器中翻译 - 安装 doc-html-translate..")
        Add("Перевод в браузере", "Translate in browser", "Переклад у браузері", "Im Browser übersetzen",
            "Traduci nel browser", "Traducir en el navegador", "Traduire dans le navigateur",
            "Traduzir no navegador", "الترجمة في المتصفح", "ब्राउज़र में अनुवाद", "ব্রাউজারে অনুবাদ",
            "براؤزر میں ترجمہ", "在浏览器中翻译")
        Add("Устанавливаю doc-html-translate.. после установки откройте меню снова",
            "Installing doc-html-translate.. reopen the menu once it finishes",
            "Встановлюю doc-html-translate.. після встановлення відкрийте меню знову",
            "doc-html-translate wird installiert.. öffnen Sie das Menü danach erneut",
            "Installazione di doc-html-translate.. riapri il menu al termine",
            "Instalando doc-html-translate.. vuelva a abrir el menú al terminar",
            "Installation de doc-html-translate.. rouvrez le menu une fois terminé",
            "Instalando doc-html-translate.. reabra o menu quando terminar",
            "جارٍ تثبيت doc-html-translate.. أعد فتح القائمة بعد الانتهاء",
            "doc-html-translate इंस्टॉल हो रहा है.. पूरा होने पर मेन्यू फिर खोलें",
            "doc-html-translate ইনস্টল হচ্ছে.. শেষ হলে মেনু আবার খুলুন",
            "doc-html-translate انسٹال ہو رہا ہے.. مکمل ہونے پر مینو دوبارہ کھولیں",
            "正在安装 doc-html-translate.. 完成后请重新打开菜单")

        ' --- Main_Form.OcrTranslate.vb --------------------------------------------
        Add("OCR + перевод текущего изображения (T) - притворимся, что понимаем эту мангу. ПКМ - настройки. Shift+T - авто-режим.",
            "OCR + translate the current image (T) - let's pretend we understand this manga. Right-click for settings. Shift+T = auto mode.",
            "OCR + переклад поточного зображення (T) - вдамо, що розуміємо цю мангу. ПКМ - налаштування. Shift+T - авторежим.",
            "OCR + Übersetzung des aktuellen Bildes (T) - tun wir so, als verstünden wir diesen Manga. Rechtsklick für Einstellungen. Shift+T = Automatik.",
            "OCR + traduzione dell'immagine corrente (T) - fingiamo di capire questo manga. Tasto destro per le impostazioni. Shift+T = modalità automatica.",
            "OCR + traducción de la imagen actual (T): finjamos que entendemos este manga. Clic derecho para la configuración. Mayús+T = modo automático.",
            "OCR + traduction de l'image courante (T) - faisons semblant de comprendre ce manga. Clic droit pour les paramètres. Maj+T = mode auto.",
            "OCR + tradução da imagem atual (T) - vamos fingir que entendemos este mangá. Botão direito para as configurações. Shift+T = modo automático.",
            "OCR + ترجمة الصورة الحالية (T) - لنتظاهر بأننا نفهم هذه المانغا. الزر الأيمن للإعدادات. Shift+T = الوضع التلقائي.",
            "मौजूदा छवि का OCR + अनुवाद (T) - मान लेते हैं कि हम यह मंगा समझते हैं। सेटिंग्स के लिए दायाँ क्लिक। Shift+T = स्वतः मोड।",
            "বর্তমান ছবির OCR + অনুবাদ (T) - ধরে নিই আমরা এই মাঙ্গা বুঝি। সেটিংসের জন্য ডান ক্লিক। Shift+T = স্বয়ংক্রিয় মোড।",
            "موجودہ تصویر کا OCR + ترجمہ (T) - فرض کریں ہم یہ مانگا سمجھتے ہیں۔ ترتیبات کے لیے دایاں کلک۔ Shift+T = خودکار موڈ۔",
            "对当前图片进行 OCR + 翻译 (T) - 假装我们看得懂这部漫画。右键打开设置，Shift+T 为自动模式。")
        Add("Это не изображение", "Not an image", "Це не зображення", "Kein Bild", "Non è un'immagine",
            "No es una imagen", "Ce n'est pas une image", "Não é uma imagem", "ليست صورة",
            "यह छवि नहीं है", "এটি ছবি নয়", "یہ تصویر نہیں ہے", "这不是图片")
        Add("Распознавание..", "OCR..", "Розпізнавання..", "Texterkennung..", "Riconoscimento..",
            "Reconociendo..", "Reconnaissance..", "Reconhecendo..", "جارٍ التعرف..", "पहचान हो रही है..",
            "শনাক্ত করা হচ্ছে..", "شناخت جاری ہے..", "正在识别..")
        Add("Из кэша", "Result loaded from cache", "З кешу", "Aus dem Cache geladen",
            "Risultato caricato dalla cache", "Resultado cargado de la caché",
            "Résultat chargé depuis le cache", "Resultado carregado do cache", "محمّل من الذاكرة المؤقتة",
            "कैश से लोड किया गया", "ক্যাশ থেকে লোড হয়েছে", "کیش سے لوڈ ہوا", "已从缓存加载")
        Add("OCR-движок недоступен", "OCR runtime missing", "OCR-рушій недоступний",
            "OCR-Laufzeit fehlt", "Motore OCR non disponibile", "Falta el motor de OCR",
            "Moteur OCR indisponible", "Mecanismo de OCR ausente", "محرك OCR غير متوفر",
            "OCR इंजन उपलब्ध नहीं", "OCR ইঞ্জিন নেই", "OCR انجن دستیاب نہیں", "OCR 引擎不可用")
        Add("Ошибка OCR", "OCR error", "Помилка OCR", "OCR-Fehler", "Errore OCR", "Error de OCR",
            "Erreur OCR", "Erro de OCR", "خطأ OCR", "OCR त्रुटि", "OCR ত্রুটি", "OCR خرابی", "OCR 出错")
        Add("Текст не найден", "No text found", "Текст не знайдено", "Kein Text gefunden",
            "Nessun testo trovato", "No se encontró texto", "Aucun texte trouvé",
            "Nenhum texto encontrado", "لم يُعثر على نص", "कोई पाठ नहीं मिला", "কোনো লেখা পাওয়া যায়নি",
            "کوئی متن نہیں ملا", "未找到文本")
        Add("Перевод..", "Translating..", "Переклад..", "Wird übersetzt..", "Traduzione..",
            "Traduciendo..", "Traduction..", "Traduzindo..", "جارٍ الترجمة..", "अनुवाद हो रहा है..",
            "অনুবাদ হচ্ছে..", "ترجمہ ہو رہا ہے..", "正在翻译..")
        Add("Переводчик недоступен", "Translator unavailable", "Перекладач недоступний",
            "Übersetzer nicht verfügbar", "Traduttore non disponibile", "Traductor no disponible",
            "Traducteur indisponible", "Tradutor indisponível", "المترجم غير متاح",
            "अनुवादक उपलब्ध नहीं", "অনুবাদক অনুপলব্ধ", "مترجم دستیاب نہیں", "翻译器不可用")
        Add("Перевод", "Translate", "Переклад", "Übersetzen", "Traduci", "Traducir", "Traduire",
            "Traduzir", "ترجمة", "अनुवाद", "অনুবাদ", "ترجمہ", "翻译")
    End Sub

End Class
