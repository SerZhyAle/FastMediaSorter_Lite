Option Strict On

' <summary>
' The per-shared-folder options dialog (.fmscfg schema v2 fields) and the
' connection-statistics window.
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddParamsStrings()

        ' --- profile + media vocabulary (shared with the wizard grid) --------------
        ' GIF, PDF, EPUB and Office are the same token in every language and stay
        ' untranslated literals at the call site - there is nothing to translate.

        Add("Обычная папка",
            "Regular folder", "Звичайна папка", "Normaler Ordner", "Cartella normale",
            "Carpeta normal", "Dossier ordinaire", "Pasta normal",
            "مجلد عادي", "सामान्य फ़ोल्डर", "সাধারণ ফোল্ডার", "عام فولڈر", "普通文件夹")

        Add("Обычная папка (по умолчанию)",
            "Regular folder (default)", "Звичайна папка (за замовчуванням)",
            "Normaler Ordner (Standard)", "Cartella normale (predefinita)",
            "Carpeta normal (predeterminada)", "Dossier ordinaire (par défaut)",
            "Pasta normal (predefinição)", "مجلد عادي (افتراضي)",
            "सामान्य फ़ोल्डर (डिफ़ॉल्ट)", "সাধারণ ফোল্ডার (ডিফল্ট)",
            "عام فولڈر (ڈیفالٹ)", "普通文件夹（默认）")

        Add("Изображения",
            "Images", "Зображення", "Bilder", "Immagini", "Imágenes", "Images", "Imagens",
            "الصور", "छवियाँ", "ছবি", "تصاویر", "图片")

        Add("Видео",
            "Video", "Відео", "Video", "Video", "Vídeo", "Vidéo", "Vídeo",
            "فيديو", "वीडियो", "ভিডিও", "ویڈیو", "视频")

        Add("Аудио",
            "Audio", "Аудіо", "Audio", "Audio", "Audio", "Audio", "Áudio",
            "صوت", "ऑडियो", "অডিও", "آڈیو", "音频")

        Add("Текст",
            "Text", "Текст", "Text", "Testo", "Texto", "Texte", "Texto",
            "نص", "पाठ", "টেক্সট", "متن", "文本")

        ' --- per-folder options ----------------------------------------------------

        Add("Параметры ресурса - {0}",
            "Resource options - {0}", "Параметри ресурсу - {0}", "Ressourcenoptionen - {0}",
            "Opzioni della risorsa - {0}", "Opciones del recurso: {0}",
            "Options de la ressource - {0}", "Opções do recurso - {0}",
            "خيارات المورد - {0}", "संसाधन विकल्प - {0}", "রিসোর্স বিকল্প - {0}",
            "ریسورس اختیارات - {0}", "资源选项 - {0}")

        Add("Название на телефоне:",
            "Name on the phone:", "Назва на телефоні:", "Name am Telefon:", "Nome sul telefono:",
            "Nombre en el teléfono:", "Nom sur le téléphone :", "Nome no telemóvel:",
            "الاسم على الهاتف:", "फ़ोन पर नाम:", "ফোনে নাম:", "فون پر نام:", "手机上的名称：")

        Add("Как ресурс называется в приложении. Пусто = имя папки.",
            "The resource name in the app. Empty = the folder name.",
            "Як ресурс називається в застосунку. Порожньо = ім'я папки.",
            "Wie die Ressource in der App heißt. Leer = der Ordnername.",
            "Come si chiama la risorsa nell'app. Vuoto = il nome della cartella.",
            "Cómo se llama el recurso en la aplicación. Vacío = el nombre de la carpeta.",
            "Le nom de la ressource dans l'application. Vide = le nom du dossier.",
            "Como o recurso se chama na aplicação. Vazio = o nome da pasta.",
            "اسم المورد داخل التطبيق. فارغ = اسم المجلد.",
            "ऐप में संसाधन का नाम। खाली = फ़ोल्डर का नाम।",
            "অ্যাপে রিসোর্সের নাম। ফাঁকা = ফোল্ডারের নাম।",
            "ایپ میں ریسورس کا نام۔ خالی = فولڈر کا نام۔",
            "资源在应用中的名称。留空 = 文件夹名称。")

        Add("Тип ресурса:",
            "Resource type:", "Тип ресурсу:", "Ressourcentyp:", "Tipo di risorsa:",
            "Tipo de recurso:", "Type de ressource :", "Tipo de recurso:",
            "نوع المورد:", "संसाधन प्रकार:", "রিসোর্সের ধরন:", "ریسورس کی قسم:", "资源类型：")

        Add("Как приложение покажет папку и какие файлы возьмёт (напр. «Видеотека» - только видео).",
            "How the app shows the folder and which files it takes (e.g. Video library - videos only).",
            "Як застосунок покаже папку і які файли візьме (напр. «Відеотека» - лише відео).",
            "Wie die App den Ordner anzeigt und welche Dateien sie nimmt (z. B. «Videothek» - nur Videos).",
            "Come l'app mostra la cartella e quali file prende (es. «Videoteca» - solo video).",
            "Cómo muestra la aplicación la carpeta y qué archivos toma (p. ej. «Videoteca»: solo vídeos).",
            "Comment l'application affiche le dossier et quels fichiers elle prend (p. ex. « Vidéothèque » - vidéos uniquement).",
            "Como a aplicação mostra a pasta e que ficheiros aceita (p. ex. «Videoteca» - apenas vídeos).",
            "كيف يعرض التطبيق المجلد وأي الملفات يأخذ (مثلاً «مكتبة فيديو» - الفيديو فقط).",
            "ऐप फ़ोल्डर को कैसे दिखाएगा और कौन-सी फ़ाइलें लेगा (जैसे «वीडियो लाइब्रेरी» - केवल वीडियो)।",
            "অ্যাপ ফোল্ডারটি কীভাবে দেখাবে ও কোন ফাইল নেবে (যেমন «ভিডিও লাইব্রেরি» - শুধু ভিডিও)।",
            "ایپ فولڈر کو کیسے دکھائے گی اور کون سی فائلیں لے گی (مثلاً «ویڈیو لائبریری» - صرف ویڈیو)۔",
            "应用如何显示该文件夹以及接受哪些文件（例如「视频库」- 仅视频）。")

        Add("Точный набор типов:",
            "Exact media set:", "Точний набір типів:", "Genaue Medienauswahl:",
            "Insieme esatto di tipi:", "Conjunto exacto de tipos:", "Ensemble exact de types :",
            "Conjunto exato de tipos:", "مجموعة الأنواع بدقة:", "सटीक प्रकार-समूह:",
            "সঠিক ধরন-সেট:", "درست اقسام کا سیٹ:", "确切类型集合：")

        Add("Необязательно. Пусто = решает тип.",
            "Optional. Empty = the type decides.", "Необов'язково. Порожньо = вирішує тип.",
            "Optional. Leer = der Typ entscheidet.", "Facoltativo. Vuoto = decide il tipo.",
            "Opcional. Vacío = decide el tipo.", "Facultatif. Vide = le type décide.",
            "Opcional. Vazio = decide o tipo.", "اختياري. فارغ = النوع هو الذي يقرر.",
            "वैकल्पिक। खाली = प्रकार तय करता है।", "ঐচ্ছিক। ফাঁকা = ধরনই ঠিক করে।",
            "اختیاری۔ خالی = قسم فیصلہ کرتی ہے۔", "可选。留空 = 由类型决定。")

        Add("Сканировать подпапки",
            "Scan subfolders", "Сканувати підпапки", "Unterordner scannen",
            "Scansiona le sottocartelle", "Escanear subcarpetas", "Analyser les sous-dossiers",
            "Analisar subpastas", "فحص المجلدات الفرعية", "उप-फ़ोल्डर स्कैन करें",
            "সাবফোল্ডার স্ক্যান করুন", "ذیلی فولڈرز اسکین کریں", "扫描子文件夹")

        Add("Показывать подпапки как элементы",
            "Show subfolders as items", "Показувати підпапки як елементи",
            "Unterordner als Elemente anzeigen", "Mostra le sottocartelle come elementi",
            "Mostrar subcarpetas como elementos", "Afficher les sous-dossiers comme éléments",
            "Mostrar subpastas como itens", "عرض المجلدات الفرعية كعناصر",
            "उप-फ़ोल्डर आइटम के रूप में दिखाएँ", "সাবফোল্ডার আইটেম হিসেবে দেখান",
            "ذیلی فولڈرز کو آئٹمز کے طور پر دکھائیں", "将子文件夹显示为条目")

        Add("Показывать скрытые файлы",
            "Show hidden files", "Показувати приховані файли", "Versteckte Dateien anzeigen",
            "Mostra i file nascosti", "Mostrar archivos ocultos", "Afficher les fichiers masqués",
            "Mostrar ficheiros ocultos", "عرض الملفات المخفية", "छिपी फ़ाइलें दिखाएँ",
            "লুকানো ফাইল দেখান", "پوشیدہ فائلیں دکھائیں", "显示隐藏文件")

        Add("Все файлы (не только медиа)",
            "All files (not only media)", "Усі файли (не лише медіа)",
            "Alle Dateien (nicht nur Medien)", "Tutti i file (non solo multimediali)",
            "Todos los archivos (no solo medios)", "Tous les fichiers (pas seulement les médias)",
            "Todos os ficheiros (não apenas multimédia)", "كل الملفات (وليس الوسائط فقط)",
            "सभी फ़ाइलें (केवल मीडिया नहीं)", "সব ফাইল (শুধু মিডিয়া নয়)",
            "تمام فائلیں (صرف میڈیا نہیں)", "所有文件（不限于媒体）")

        Add("Условия сканирования:",
            "Scan conditions:", "Умови сканування:", "Scan-Bedingungen:", "Condizioni di scansione:",
            "Condiciones de escaneo:", "Conditions d'analyse :", "Condições de análise:",
            "شروط الفحص:", "स्कैन की शर्तें:", "স্ক্যানের শর্ত:", "اسکین کی شرائط:", "扫描条件：")

        Add("Доступ:",
            "Access:", "Доступ:", "Zugriff:", "Accesso:", "Acceso:", "Accès :", "Acesso:",
            "الوصول:", "पहुँच:", "অ্যাক্সেস:", "رسائی:", "访问权限：")

        Add("По умолчанию телефон может добавлять, переименовывать и удалять файлы в папке.",
            "By default the phone can add, rename and delete files in the folder.",
            "За замовчуванням телефон може додавати, перейменовувати й видаляти файли в папці.",
            "Standardmäßig kann das Telefon Dateien im Ordner hinzufügen, umbenennen und löschen.",
            "Per impostazione predefinita il telefono può aggiungere, rinominare ed eliminare file nella cartella.",
            "De forma predeterminada, el teléfono puede añadir, renombrar y eliminar archivos en la carpeta.",
            "Par défaut, le téléphone peut ajouter, renommer et supprimer des fichiers dans le dossier.",
            "Por predefinição, o telemóvel pode adicionar, mudar o nome e eliminar ficheiros na pasta.",
            "افتراضيًا يمكن للهاتف إضافة الملفات وإعادة تسميتها وحذفها داخل المجلد.",
            "डिफ़ॉल्ट रूप से फ़ोन फ़ोल्डर में फ़ाइलें जोड़, नाम बदल और हटा सकता है।",
            "ডিফল্টভাবে ফোন ফোল্ডারে ফাইল যোগ, নাম পরিবর্তন ও মুছতে পারে।",
            "بطور ڈیفالٹ فون فولڈر میں فائلیں شامل، نام تبدیل اور حذف کر سکتا ہے۔",
            "默认情况下，手机可以在该文件夹中添加、重命名和删除文件。")

        Add("Недоступно для записи на уровне сервера - сервер запрещает изменения",
            "Not writable at the server level - the server blocks changes",
            "Недоступно для запису на рівні сервера - сервер забороняє зміни",
            "Serverseitig schreibgeschützt - der Server blockiert Änderungen",
            "Non scrivibile a livello di server: il server blocca le modifiche",
            "No escribible a nivel de servidor: el servidor bloquea los cambios",
            "Non accessible en écriture au niveau du serveur - le serveur bloque les modifications",
            "Não gravável ao nível do servidor - o servidor bloqueia alterações",
            "غير قابل للكتابة على مستوى الخادم - الخادم يمنع التعديلات",
            "सर्वर स्तर पर लिखने योग्य नहीं - सर्वर बदलाव रोकता है",
            "সার্ভার স্তরে লেখা যায় না - সার্ভার পরিবর্তন আটকায়",
            "سرور کی سطح پر قابلِ تحریر نہیں - سرور تبدیلیاں روکتا ہے",
            "服务器级别不可写 - 服务器会阻止修改")

        Add("Настоящий запрет: сервер физически не даёт телефону менять файлы.",
            "A real lock: the server physically prevents changes.",
            "Справжня заборона: сервер фізично не дає телефону змінювати файли.",
            "Eine echte Sperre: Der Server verhindert Änderungen physisch.",
            "Un blocco reale: il server impedisce fisicamente le modifiche.",
            "Un bloqueo real: el servidor impide físicamente los cambios.",
            "Un vrai verrou : le serveur empêche physiquement les modifications.",
            "Um bloqueio real: o servidor impede fisicamente as alterações.",
            "قفل حقيقي: الخادم يمنع التعديلات فعليًا.",
            "वास्तविक लॉक: सर्वर भौतिक रूप से बदलाव रोकता है।",
            "প্রকৃত লক: সার্ভার বাস্তবিকভাবেই পরিবর্তন আটকায়।",
            "حقیقی تالا: سرور عملی طور پر تبدیلیاں روکتا ہے۔",
            "真正的锁定：服务器会从物理上阻止修改。")

        Add("Публиковать как «только чтение» - подсказка приложению (сервер не блокирует)",
            "Publish as read-only - a hint to the app (the server does not block)",
            "Публікувати як «лише читання» - підказка застосунку (сервер не блокує)",
            "Als schreibgeschützt veröffentlichen - ein Hinweis an die App (der Server blockiert nicht)",
            "Pubblica come «sola lettura»: un suggerimento per l'app (il server non blocca)",
            "Publicar como «solo lectura»: una indicación para la aplicación (el servidor no bloquea)",
            "Publier en « lecture seule » - une indication pour l'application (le serveur ne bloque pas)",
            "Publicar como «só leitura» - uma sugestão para a aplicação (o servidor não bloqueia)",
            "النشر كـ «للقراءة فقط» - مجرد تلميح للتطبيق (الخادم لا يمنع)",
            "«केवल पढ़ने योग्य» के रूप में प्रकाशित करें - ऐप के लिए संकेत (सर्वर नहीं रोकता)",
            "«শুধু পঠনযোগ্য» হিসেবে প্রকাশ করুন - অ্যাপের জন্য ইঙ্গিত (সার্ভার আটকায় না)",
            "«صرف پڑھنے کے لیے» شائع کریں - ایپ کے لیے اشارہ (سرور نہیں روکتا)",
            "以「只读」方式发布 - 仅是给应用的提示（服务器不会阻止）")

        Add("Папка-получатель - в неё можно копировать и переносить с телефона",
            "Destination folder - the phone can copy and move files into it",
            "Папка-приймач - до неї можна копіювати й переносити з телефона",
            "Zielordner - das Telefon kann Dateien hierher kopieren und verschieben",
            "Cartella di destinazione: il telefono può copiarci e spostarci dentro i file",
            "Carpeta de destino: el teléfono puede copiar y mover archivos a ella",
            "Dossier de destination - le téléphone peut y copier et déplacer des fichiers",
            "Pasta de destino - o telemóvel pode copiar e mover ficheiros para ela",
            "مجلد الوجهة - يمكن للهاتف نسخ الملفات ونقلها إليه",
            "गंतव्य फ़ोल्डर - फ़ोन इसमें फ़ाइलें कॉपी और मूव कर सकता है",
            "গন্তব্য ফোল্ডার - ফোন এতে ফাইল কপি ও সরাতে পারে",
            "منزل فولڈر - فون اس میں فائلیں کاپی اور منتقل کر سکتا ہے",
            "接收文件夹 - 手机可向其复制和移动文件")

        Add("Папка станет доступна на запись; ресурс попадёт в список получателей. Цвет метки выберет приложение.",
            "The folder becomes writable; the resource joins the destinations list. The app picks the chip colour.",
            "Папка стане доступною на запис; ресурс потрапить до списку приймачів. Колір мітки вибере застосунок.",
            "Der Ordner wird beschreibbar; die Ressource landet in der Zielliste. Die Farbe der Markierung wählt die App.",
            "La cartella diventa scrivibile; la risorsa entra nell'elenco delle destinazioni. Il colore dell'etichetta lo sceglie l'app.",
            "La carpeta pasa a ser escribible; el recurso se añade a la lista de destinos. La aplicación elige el color de la etiqueta.",
            "Le dossier devient accessible en écriture ; la ressource rejoint la liste des destinations. L'application choisit la couleur de l'étiquette.",
            "A pasta passa a ser gravável; o recurso entra na lista de destinos. A aplicação escolhe a cor da etiqueta.",
            "يصبح المجلد قابلاً للكتابة، وينضم المورد إلى قائمة الوجهات. ويختار التطبيق لون الوسم.",
            "फ़ोल्डर लिखने योग्य बन जाता है; संसाधन गंतव्य सूची में जुड़ जाता है। लेबल का रंग ऐप चुनता है।",
            "ফোল্ডারটি লেখার যোগ্য হয়; রিসোর্সটি গন্তব্য তালিকায় যুক্ত হয়। লেবেলের রং অ্যাপ বেছে নেয়।",
            "فولڈر قابلِ تحریر ہو جاتا ہے؛ ریسورس منزل کی فہرست میں شامل ہو جاتا ہے۔ لیبل کا رنگ ایپ منتخب کرتی ہے۔",
            "该文件夹将变为可写；该资源会进入接收目标列表。标签颜色由应用决定。")

        Add("Комментарий:",
            "Comment:", "Коментар:", "Kommentar:", "Commento:", "Comentario:", "Commentaire :",
            "Comentário:", "تعليق:", "टिप्पणी:", "মন্তব্য:", "تبصرہ:", "备注：")

        Add("если задан - приложение попросит его при открытии",
            "if set - the app asks for it on open", "якщо задано - застосунок попросить його при відкритті",
            "wenn gesetzt - die App fragt beim Öffnen danach",
            "se impostato, l'app lo chiede all'apertura", "si se define, la aplicación lo pedirá al abrir",
            "s'il est défini - l'application le demande à l'ouverture",
            "se definido - a aplicação pede-o ao abrir", "إذا حُدِّد، يطلبه التطبيق عند الفتح",
            "यदि सेट हो - ऐप खोलते समय इसे माँगेगा", "সেট করা থাকলে - খোলার সময় অ্যাপ এটি চাইবে",
            "اگر مقرر ہو - ایپ کھولتے وقت اسے مانگے گی", "如果设置 - 应用会在打开时询问")

        Add("PIN для ресурса:",
            "Resource PIN:", "PIN для ресурсу:", "PIN für die Ressource:", "PIN della risorsa:",
            "PIN del recurso:", "Code PIN de la ressource :", "PIN do recurso:",
            "رمز PIN للمورد:", "संसाधन का PIN:", "রিসোর্সের PIN:", "ریسورس کا PIN:", "资源 PIN：")

        Add("как часто листать фото",
            "how often to advance photos", "як часто гортати фото",
            "wie oft die Fotos weiterblättern", "ogni quanto avanzare le foto",
            "cada cuánto pasar las fotos", "à quelle fréquence faire défiler les photos",
            "com que frequência avançar as fotos", "كم مرة يتم تبديل الصور",
            "फ़ोटो कितनी बार आगे बढ़ें", "কত ঘন ঘন ছবি বদলাবে",
            "تصاویر کتنی جلدی بدلیں", "照片切换的频率")

        Add("Слайд-шоу, секунд:",
            "Slideshow, seconds:", "Слайд-шоу, секунд:", "Diaschau, Sekunden:",
            "Slideshow, secondi:", "Pase de diapositivas, segundos:", "Diaporama, secondes :",
            "Apresentação, segundos:", "عرض الشرائح، بالثواني:", "स्लाइडशो, सेकंड:",
            "স্লাইডশো, সেকেন্ড:", "سلائیڈ شو، سیکنڈ:", "幻灯片，秒：")

        ' --- the connection-statistics window --------------------------------------

        Add("Текущее состояние раздачи",
            "Sharing status", "Поточний стан роздачі", "Freigabestatus", "Stato della condivisione",
            "Estado del uso compartido", "État du partage", "Estado da partilha",
            "حالة المشاركة", "साझाकरण की स्थिति", "শেয়ারিং স্ট্যাটাস", "شیئرنگ کی حالت", "共享状态")

        Add("Раздача:",
            "Sharing:", "Роздача:", "Freigabe:", "Condivisione:", "Uso compartido:", "Partage :",
            "Partilha:", "المشاركة:", "साझाकरण:", "শেয়ারিং:", "شیئرنگ:", "共享：")

        Add("Используется с:",
            "In use since:", "Використовується з:", "In Nutzung seit:", "In uso da:",
            "En uso desde:", "En service depuis :", "Em uso desde:",
            "قيد الاستخدام منذ:", "उपयोग में तब से:", "ব্যবহারে আছে:", "زیرِ استعمال از:", "使用起始时间：")

        Add("Счётчики хранятся только на этом ПК и никуда не отправляются.",
            "The counters are kept on this PC only and are never sent anywhere.",
            "Лічильники зберігаються лише на цьому ПК і нікуди не надсилаються.",
            "Die Zähler bleiben nur auf diesem PC und werden nirgendwohin gesendet.",
            "I contatori restano solo su questo PC e non vengono inviati da nessuna parte.",
            "Los contadores se guardan solo en este PC y nunca se envían a ninguna parte.",
            "Les compteurs restent uniquement sur ce PC et ne sont envoyés nulle part.",
            "Os contadores ficam apenas neste PC e nunca são enviados para lado nenhum.",
            "تُحفظ العدادات على هذا الحاسوب فقط ولا تُرسل إلى أي جهة.",
            "काउंटर केवल इसी पीसी पर रहते हैं और कहीं नहीं भेजे जाते।",
            "কাউন্টারগুলি কেবল এই পিসিতেই থাকে, কোথাও পাঠানো হয় না।",
            "کاؤنٹرز صرف اسی پی سی پر رہتے ہیں اور کہیں نہیں بھیجے جاتے۔",
            "计数仅保存在这台电脑上，绝不会发送到任何地方。")

        Add("Сбросить счётчики",
            "Reset counters", "Скинути лічильники", "Zähler zurücksetzen", "Azzera i contatori",
            "Restablecer contadores", "Réinitialiser les compteurs", "Repor os contadores",
            "إعادة تعيين العدادات", "काउंटर रीसेट करें", "কাউন্টার রিসেট করুন",
            "کاؤنٹرز ری سیٹ کریں", "重置计数")

        Add("нет связи с сервером",
            "worker unavailable", "немає зв'язку з сервером", "Dienst nicht erreichbar",
            "processo non disponibile", "proceso no disponible", "processus indisponible",
            "processo indisponível", "الخدمة غير متاحة", "सेवा उपलब्ध नहीं",
            "সার্ভিস অনুপলব্ধ", "سروس دستیاب نہیں", "无法连接到服务进程")

        Add("выключена",
            "off", "вимкнено", "aus", "disattivata", "desactivado", "désactivé", "desativada",
            "متوقفة", "बंद", "বন্ধ", "بند", "已关闭")

        Add("нет данных",
            "no data", "немає даних", "keine Daten", "nessun dato", "sin datos",
            "aucune donnée", "sem dados", "لا توجد بيانات", "कोई डेटा नहीं",
            "কোনো তথ্য নেই", "کوئی ڈیٹا نہیں", "无数据")

        Add("работает, порт {0}",
            "on, port {0}", "працює, порт {0}", "aktiv, Port {0}", "attiva, porta {0}",
            "activo, puerto {0}", "actif, port {0}", "ativa, porta {0}",
            "مفعّلة، المنفذ {0}", "चालू, पोर्ट {0}", "চালু, পোর্ট {0}",
            "آن، پورٹ {0}", "已开启，端口 {0}")

    End Sub

End Class
