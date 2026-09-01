Option Strict On

' <summary>
' The Share Manager main window: header, server grid, options, usage counters,
' the shared-folders list and every status line it shows.
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddManagerStrings()

        ' --- header ---------------------------------------------------------------

        Add("Откройте папки этого ПК на телефоне - по Wi-Fi или через интернет.",
            "Open this PC's folders on your phone - over Wi-Fi or the internet.",
            "Відкрийте папки цього ПК на телефоні - по Wi-Fi або через інтернет.",
            "Öffnen Sie die Ordner dieses PCs auf Ihrem Telefon - über WLAN oder das Internet.",
            "Apri le cartelle di questo PC sul telefono, via Wi-Fi o internet.",
            "Abre las carpetas de este PC en tu teléfono, por Wi-Fi o por internet.",
            "Ouvrez les dossiers de ce PC sur votre téléphone - en Wi-Fi ou via internet.",
            "Abra as pastas deste PC no telemóvel - por Wi-Fi ou pela internet.",
            "افتح مجلدات هذا الحاسوب على هاتفك - عبر Wi-Fi أو الإنترنت.",
            "इस पीसी के फ़ोल्डर अपने फ़ोन पर खोलें - Wi-Fi या इंटरनेट के ज़रिए।",
            "এই পিসির ফোল্ডারগুলি আপনার ফোনে খুলুন - Wi-Fi বা ইন্টারনেটের মাধ্যমে।",
            "اس پی سی کے فولڈرز اپنے فون پر کھولیں - Wi-Fi یا انٹرنیٹ کے ذریعے۔",
            "在手机上打开这台电脑的文件夹 - 通过 Wi-Fi 或互联网。")

        Add("Поделиться",
            "Share", "Поділитися", "Teilen", "Condividi", "Compartir", "Partager", "Partilhar",
            "مشاركة", "साझा करें", "শেয়ার করুন", "شیئر کریں", "共享")

        Add("FastMediaSorter для Android",
            "FastMediaSorter for Android", "FastMediaSorter для Android",
            "FastMediaSorter für Android", "FastMediaSorter per Android",
            "FastMediaSorter para Android", "FastMediaSorter pour Android",
            "FastMediaSorter para Android", "FastMediaSorter لنظام Android",
            "Android के लिए FastMediaSorter", "Android-এর জন্য FastMediaSorter",
            "Android کے لیے FastMediaSorter", "FastMediaSorter Android 版")

        Add("Как публиковать папки (сайт)",
            "How to publish folders (website)", "Як публікувати папки (сайт)",
            "Ordner freigeben - Anleitung (Website)", "Come pubblicare le cartelle (sito)",
            "Cómo publicar carpetas (sitio web)", "Comment publier des dossiers (site web)",
            "Como publicar pastas (site)", "كيفية نشر المجلدات (الموقع)",
            "फ़ोल्डर कैसे प्रकाशित करें (वेबसाइट)", "কীভাবে ফোল্ডার প্রকাশ করবেন (ওয়েবসাইট)",
            "فولڈرز کیسے شائع کریں (ویب سائٹ)", "如何发布文件夹（网站）")

        Add("Инструкция для моей модели роутера",
            "Guide for my router model", "Інструкція для моєї моделі роутера",
            "Anleitung für mein Routermodell", "Guida per il mio modello di router",
            "Guía para mi modelo de router", "Guide pour mon modèle de routeur",
            "Guia para o meu modelo de router", "دليل لطراز الموجّه لديّ",
            "मेरे राउटर मॉडल के लिए मार्गदर्शिका", "আমার রাউটার মডেলের নির্দেশিকা",
            "میرے روٹر ماڈل کے لیے رہنما", "适用于我的路由器型号的指南")

        ' --- the server grid ------------------------------------------------------

        Add("Доступ с Android",
            "Android access", "Доступ з Android", "Android-Zugriff", "Accesso da Android",
            "Acceso desde Android", "Accès depuis Android", "Acesso a partir do Android",
            "الوصول من Android", "Android से पहुँच", "Android থেকে অ্যাক্সেস",
            "Android سے رسائی", "Android 访问")

        Add("Через интернет:",
            "Via internet:", "Через інтернет:", "Über das Internet:", "Via internet:",
            "Por internet:", "Via internet :", "Pela internet:",
            "عبر الإنترنت:", "इंटरनेट के ज़रिए:", "ইন্টারনেটের মাধ্যমে:", "انٹرنیٹ کے ذریعے:", "通过互联网：")

        Add("Дома (Wi-Fi):",
            "Home (Wi-Fi):", "Удома (Wi-Fi):", "Zu Hause (WLAN):", "A casa (Wi-Fi):",
            "En casa (Wi-Fi):", "À la maison (Wi-Fi) :", "Em casa (Wi-Fi):",
            "في المنزل (Wi-Fi):", "घर पर (Wi-Fi):", "বাড়িতে (Wi-Fi):", "گھر پر (Wi-Fi):", "在家（Wi-Fi）：")

        Add("IPv6:",
            "IPv6:", "IPv6:", "IPv6:", "IPv6:", "IPv6:", "IPv6 :", "IPv6:",
            "IPv6:", "IPv6:", "IPv6:", "IPv6:", "IPv6：")

        Add("Ключ узла:",
            "Host key:", "Ключ вузла:", "Hostschlüssel:", "Chiave host:", "Clave del host:",
            "Clé de l'hôte :", "Chave do anfitrião:", "مفتاح المضيف:", "होस्ट कुंजी:",
            "হোস্ট কী:", "ہوسٹ کی:", "主机密钥：")

        Add("Логин:",
            "Login:", "Логін:", "Benutzername:", "Utente:", "Usuario:", "Identifiant :",
            "Utilizador:", "اسم المستخدم:", "लॉगिन:", "লগইন:", "لاگ ان:", "用户名：")

        Add("Пароль:",
            "Password:", "Пароль:", "Kennwort:", "Password:", "Contraseña:", "Mot de passe :",
            "Palavra-passe:", "كلمة المرور:", "पासवर्ड:", "পাসওয়ার্ড:", "پاس ورڈ:", "密码：")

        Add("Проверить доступ из интернета",
            "Test internet access", "Перевірити доступ з інтернету", "Internetzugriff testen",
            "Verifica l'accesso da internet", "Comprobar el acceso desde internet",
            "Tester l'accès depuis internet", "Testar o acesso pela internet",
            "اختبار الوصول من الإنترنت", "इंटरनेट पहुँच जाँचें", "ইন্টারনেট অ্যাক্সেস পরীক্ষা করুন",
            "انٹرنیٹ رسائی جانچیں", "测试互联网访问")

        Add("Как настроить доступ через интернет",
            "How to set up internet access", "Як налаштувати доступ через інтернет",
            "Internetzugriff einrichten", "Come configurare l'accesso da internet",
            "Cómo configurar el acceso desde internet", "Comment configurer l'accès internet",
            "Como configurar o acesso pela internet", "كيفية إعداد الوصول من الإنترنت",
            "इंटरनेट पहुँच कैसे सेट करें", "কীভাবে ইন্টারনেট অ্যাক্সেস সেট করবেন",
            "انٹرنیٹ رسائی کیسے ترتیب دیں", "如何设置互联网访问")

        ' --- controls -------------------------------------------------------------

        Add("Начать раздачу",
            "Start sharing", "Почати роздачу", "Freigabe starten", "Avvia la condivisione",
            "Iniciar el uso compartido", "Démarrer le partage", "Iniciar a partilha",
            "بدء المشاركة", "साझाकरण शुरू करें", "শেয়ারিং শুরু করুন", "شیئرنگ شروع کریں", "开始共享")

        Add("Остановить раздачу",
            "Stop sharing", "Зупинити роздачу", "Freigabe stoppen", "Ferma la condivisione",
            "Detener el uso compartido", "Arrêter le partage", "Parar a partilha",
            "إيقاف المشاركة", "साझाकरण रोकें", "শেয়ারিং বন্ধ করুন", "شیئرنگ روکیں", "停止共享")

        Add("Запускать при входе в Windows",
            "Start at Windows logon", "Запускати при вході в Windows",
            "Beim Windows-Anmelden starten", "Avvia all'accesso a Windows",
            "Iniciar al iniciar sesión en Windows", "Démarrer à l'ouverture de session Windows",
            "Iniciar ao iniciar sessão no Windows", "التشغيل عند تسجيل الدخول إلى Windows",
            "Windows लॉगऑन पर शुरू करें", "Windows লগঅনে চালু করুন",
            "Windows لاگ اِن پر شروع کریں", "Windows 登录时启动")

        Add("Открывать окно менеджера при запуске",
            "Open the manager window at startup", "Відкривати вікно менеджера під час запуску",
            "Managerfenster beim Start öffnen", "Apri la finestra del gestore all'avvio",
            "Abrir la ventana del gestor al iniciar", "Ouvrir la fenêtre du gestionnaire au démarrage",
            "Abrir a janela do gestor no arranque", "فتح نافذة المدير عند بدء التشغيل",
            "स्टार्टअप पर प्रबंधक विंडो खोलें", "চালুর সময় ম্যানেজার উইন্ডো খুলুন",
            "اسٹارٹ اپ پر مینیجر ونڈو کھولیں", "启动时打开管理器窗口")

        Add("Без галочки любой запуск программы оставляет только значок рядом с часами - окно открывается двойным щелчком по нему. С галочкой окно открывается сразу.",
            "With this off, any start of the program leaves only the tray icon - double-click it to open the window. With it on, the window opens right away.",
            "Без галочки будь-який запуск програми залишає лише значок біля годинника - вікно відкривається подвійним клацанням по ньому. З галочкою вікно відкривається одразу.",
            "Ohne Häkchen hinterlässt jeder Programmstart nur das Symbol im Infobereich - ein Doppelklick öffnet das Fenster. Mit Häkchen öffnet sich das Fenster sofort.",
            "Se disattivato, ogni avvio del programma lascia solo l'icona nell'area di notifica: doppio clic per aprire la finestra. Se attivo, la finestra si apre subito.",
            "Si está desactivado, cualquier arranque del programa deja solo el icono de la bandeja: haz doble clic para abrir la ventana. Si está activado, la ventana se abre de inmediato.",
            "Sans cette case, tout démarrage du programme ne laisse que l'icône dans la zone de notification - double-cliquez pour ouvrir la fenêtre. Avec la case, la fenêtre s'ouvre aussitôt.",
            "Sem esta opção, qualquer arranque do programa deixa apenas o ícone na área de notificação - faça duplo clique para abrir a janela. Com ela, a janela abre logo.",
            "بدون هذا الخيار، يترك كل تشغيل للبرنامج أيقونة شريط المهام فقط - انقر عليها نقرًا مزدوجًا لفتح النافذة. ومع تفعيله تُفتح النافذة فورًا.",
            "यह बंद होने पर कार्यक्रम का कोई भी आरंभ केवल ट्रे आइकन छोड़ता है - विंडो खोलने के लिए उस पर डबल-क्लिक करें। चालू होने पर विंडो तुरंत खुल जाती है।",
            "এটি বন্ধ থাকলে প্রোগ্রামের যেকোনো সূচনা কেবল ট্রে আইকন রেখে যায় - উইন্ডো খুলতে সেটিতে ডাবল-ক্লিক করুন। চালু থাকলে উইন্ডো সঙ্গে সঙ্গে খোলে।",
            "یہ بند ہو تو پروگرام کا ہر آغاز صرف ٹرے آئیکن چھوڑتا ہے - ونڈو کھولنے کے لیے اس پر ڈبل کلک کریں۔ آن ہونے پر ونڈو فوراً کھل جاتی ہے۔",
            "关闭时，程序的任何启动都只会留下托盘图标 - 双击即可打开窗口。开启后窗口会立即打开。")

        Add("Макс. одновременных подключений:",
            "Max simultaneous connections:", "Макс. одночасних з'єднань:",
            "Max. gleichzeitige Verbindungen:", "Connessioni simultanee max:",
            "Máx. de conexiones simultáneas:", "Connexions simultanées max :",
            "Máx. de ligações simultâneas:", "الحد الأقصى للاتصالات المتزامنة:",
            "अधिकतम एक साथ कनेक्शन:", "সর্বাধিক একযোগে সংযোগ:",
            "زیادہ سے زیادہ بیک وقت کنکشنز:", "最大并发连接数：")

        Add("Сколько устройств могут быть подключены одновременно. По умолчанию 10; можно от 1 до 99999. Значение меньше 2 может кратко отклонять переподключение телефона.",
            "How many devices may be connected at once. Default 10; anything from 1 to 99999. Below 2 can briefly refuse a phone's reconnect.",
            "Скільки пристроїв можуть бути підключені одночасно. За замовчуванням 10; можна від 1 до 99999. Значення менше 2 може ненадовго відхиляти перепідключення телефона.",
            "Wie viele Geräte gleichzeitig verbunden sein dürfen. Standard 10; erlaubt sind 1 bis 99999. Unter 2 kann ein erneutes Verbinden des Telefons kurz abgewiesen werden.",
            "Quanti dispositivi possono essere connessi contemporaneamente. Predefinito 10; da 1 a 99999. Sotto 2 la riconnessione del telefono può essere brevemente rifiutata.",
            "Cuántos dispositivos pueden estar conectados a la vez. Por defecto 10; de 1 a 99999. Por debajo de 2 puede rechazar brevemente la reconexión del teléfono.",
            "Combien d'appareils peuvent être connectés en même temps. Par défaut 10 ; de 1 à 99999. En dessous de 2, la reconnexion du téléphone peut être brièvement refusée.",
            "Quantos dispositivos podem estar ligados ao mesmo tempo. Predefinição 10; de 1 a 99999. Abaixo de 2 a reconexão do telemóvel pode ser recusada por breves instantes.",
            "عدد الأجهزة التي يمكن أن تتصل في الوقت نفسه. القيمة الافتراضية 10؛ ويسمح بالقيم من 1 إلى 99999. وأقل من 2 قد يرفض إعادة اتصال الهاتف لفترة قصيرة.",
            "एक साथ कितने उपकरण जुड़े रह सकते हैं। डिफ़ॉल्ट 10; 1 से 99999 तक। 2 से कम होने पर फ़ोन का पुनः कनेक्शन थोड़ी देर के लिए अस्वीकृत हो सकता है।",
            "একসাথে কতগুলি ডিভাইস সংযুক্ত থাকতে পারে। ডিফল্ট ১০; ১ থেকে ৯৯৯৯৯ পর্যন্ত। ২-এর কম হলে ফোনের পুনঃসংযোগ কিছুক্ষণের জন্য প্রত্যাখ্যাত হতে পারে।",
            "بیک وقت کتنے آلات جڑے رہ سکتے ہیں۔ پہلے سے طے شدہ 10؛ 1 سے 99999 تک۔ 2 سے کم پر فون کا دوبارہ کنکشن مختصر وقت کے لیے مسترد ہو سکتا ہے۔",
            "可同时连接的设备数量。默认 10；可设 1 至 99999。小于 2 时，手机重新连接可能被短暂拒绝。")

        Add("Порт:",
            "Port:", "Порт:", "Port:", "Porta:", "Puerto:", "Port :", "Porta:",
            "المنفذ:", "पोर्ट:", "পোর্ট:", "پورٹ:", "端口：")

        Add("Подобрать свободный",
            "Find a free one", "Підібрати вільний", "Freien suchen", "Trova una libera",
            "Buscar uno libre", "En trouver un libre", "Encontrar uma livre",
            "اختيار منفذ متاح", "कोई खाली चुनें", "একটি খালি খুঁজুন",
            "کوئی خالی تلاش کریں", "查找空闲端口")

        ' --- usage counters -------------------------------------------------------

        Add("Статистика раздачи",
            "Usage", "Статистика роздачі", "Nutzung", "Utilizzo", "Uso", "Utilisation",
            "Utilização", "الاستخدام", "उपयोग", "ব্যবহার", "استعمال", "使用统计")

        Add("Последнее подключение:",
            "Last connection:", "Останнє підключення:", "Letzte Verbindung:", "Ultima connessione:",
            "Última conexión:", "Dernière connexion :", "Última ligação:",
            "آخر اتصال:", "अंतिम कनेक्शन:", "সর্বশেষ সংযোগ:", "آخری کنکشن:", "最近连接：")

        Add("Подключений:",
            "Connections:", "З'єднань:", "Verbindungen:", "Connessioni:", "Conexiones:",
            "Connexions :", "Ligações:", "الاتصالات:", "कनेक्शन:", "সংযোগ:", "کنکشنز:", "连接数：")

        Add("Файлов отдано:",
            "Files served:", "Файлів віддано:", "Ausgelieferte Dateien:", "File serviti:",
            "Archivos servidos:", "Fichiers servis :", "Ficheiros servidos:",
            "الملفات المُقدَّمة:", "भेजी गई फ़ाइलें:", "প্রদত্ত ফাইল:", "بھیجی گئی فائلیں:", "已提供文件数：")

        Add("Считается каждый сеанс связи. Один телефон может подключаться несколько раз (проверка доступа, просмотр файла, переподключение).",
            "Counts each connection session. One phone can connect several times (reachability check, opening a file, reconnects).",
            "Рахується кожен сеанс зв'язку. Один телефон може підключатися кілька разів (перевірка доступу, перегляд файлу, перепідключення).",
            "Gezählt wird jede Verbindungssitzung. Ein Telefon kann sich mehrfach verbinden (Erreichbarkeitsprüfung, Datei öffnen, erneutes Verbinden).",
            "Viene contata ogni sessione di connessione. Un solo telefono può connettersi più volte (verifica di raggiungibilità, apertura di un file, riconnessioni).",
            "Se cuenta cada sesión de conexión. Un mismo teléfono puede conectarse varias veces (comprobación de acceso, abrir un archivo, reconexiones).",
            "Chaque session de connexion est comptée. Un même téléphone peut se connecter plusieurs fois (test d'accessibilité, ouverture d'un fichier, reconnexions).",
            "Conta-se cada sessão de ligação. Um mesmo telemóvel pode ligar-se várias vezes (verificação de acesso, abertura de um ficheiro, reconexões).",
            "تُحتسب كل جلسة اتصال. وقد يتصل هاتف واحد عدة مرات (فحص إمكانية الوصول، فتح ملف، إعادة الاتصال).",
            "हर कनेक्शन सत्र गिना जाता है। एक ही फ़ोन कई बार जुड़ सकता है (पहुँच जाँच, फ़ाइल खोलना, पुनः कनेक्शन)।",
            "প্রতিটি সংযোগ সেশন গণনা করা হয়। একই ফোন একাধিকবার সংযুক্ত হতে পারে (অ্যাক্সেস যাচাই, ফাইল খোলা, পুনঃসংযোগ)।",
            "ہر کنکشن سیشن شمار ہوتا ہے۔ ایک ہی فون کئی بار جڑ سکتا ہے (رسائی جانچ، فائل کھولنا، دوبارہ کنکشن)۔",
            "统计每一次连接会话。同一部手机可能连接多次（可达性检测、打开文件、重新连接）。")

        Add("ещё не было",
            "never", "ще не було", "noch nie", "mai", "nunca", "jamais", "nunca",
            "لم يحدث بعد", "अभी तक नहीं", "এখনও হয়নি", "ابھی تک نہیں", "尚未发生")

        Add("всего {0} (с запуска {1})",
            "{0} total ({1} since start)", "усього {0} (з запуску {1})",
            "insgesamt {0} ({1} seit dem Start)", "{0} in totale ({1} dall'avvio)",
            "{0} en total ({1} desde el inicio)", "{0} au total ({1} depuis le démarrage)",
            "{0} no total ({1} desde o início)", "الإجمالي {0} ({1} منذ البدء)",
            "कुल {0} (शुरू से {1})", "মোট {0} (শুরু থেকে {1})",
            "کل {0} (آغاز سے {1})", "共 {0}（启动以来 {1}）")

        ' --- shared folders -------------------------------------------------------

        Add("Общие папки",
            "Shared folders", "Спільні папки", "Freigegebene Ordner", "Cartelle condivise",
            "Carpetas compartidas", "Dossiers partagés", "Pastas partilhadas",
            "المجلدات المشتركة", "साझा फ़ोल्डर", "শেয়ার করা ফোল্ডার", "مشترکہ فولڈرز", "共享文件夹")

        Add("Добавить папку..",
            "Add folder..", "Додати папку..", "Ordner hinzufügen..", "Aggiungi cartella..",
            "Añadir carpeta..", "Ajouter un dossier..", "Adicionar pasta..",
            "إضافة مجلد..", "फ़ोल्डर जोड़ें..", "ফোল্ডার যোগ করুন..", "فولڈر شامل کریں..", "添加文件夹..")

        Add("+ Текущая",
            "+ Current", "+ Поточна", "+ Aktueller", "+ Corrente", "+ Actual", "+ Actuel",
            "+ Atual", "+ الحالي", "+ वर्तमान", "+ বর্তমান", "+ موجودہ", "+ 当前")

        Add("Убрать",
            "Remove", "Прибрати", "Entfernen", "Rimuovi", "Quitar", "Retirer", "Remover",
            "إزالة", "हटाएँ", "সরান", "ہٹائیں", "移除")

        Add("Редактировать",
            "Edit", "Редагувати", "Bearbeiten", "Modifica", "Editar", "Modifier",
            "Editar", "تعديل", "संपादित करें", "সম্পাদনা", "ترمیم", "编辑")

        Add("Название",
            "Name", "Назва", "Name", "Nome", "Nombre", "Nom", "Nome",
            "الاسم", "नाम", "নাম", "نام", "名称")

        Add("Тип",
            "Type", "Тип", "Typ", "Tipo", "Tipo", "Type", "Tipo",
            "النوع", "प्रकार", "ধরন", "قسم", "类型")

        Add("Папка",
            "Folder", "Папка", "Ordner", "Cartella", "Carpeta", "Dossier", "Pasta",
            "المجلد", "फ़ोल्डर", "ফোল্ডার", "فولڈر", "文件夹")

        Add("Выберите папку, которую хотите открыть на телефоне",
            "Choose the folder to open on the phone", "Виберіть папку, яку хочете відкрити на телефоні",
            "Wählen Sie den Ordner, der auf dem Telefon geöffnet werden soll",
            "Scegli la cartella da aprire sul telefono", "Elige la carpeta que quieres abrir en el teléfono",
            "Choisissez le dossier à ouvrir sur le téléphone", "Escolha a pasta a abrir no telemóvel",
            "اختر المجلد الذي تريد فتحه على الهاتف", "वह फ़ोल्डर चुनें जिसे फ़ोन पर खोलना है",
            "ফোনে যে ফোল্ডারটি খুলতে চান তা বেছে নিন", "وہ فولڈر منتخب کریں جسے فون پر کھولنا ہے",
            "选择要在手机上打开的文件夹")

        Add("Эта папка уже в списке.",
            "That folder is already in the list.", "Ця папка вже в списку.",
            "Dieser Ordner steht bereits in der Liste.", "Quella cartella è già nell'elenco.",
            "Esa carpeta ya está en la lista.", "Ce dossier est déjà dans la liste.",
            "Essa pasta já está na lista.", "هذا المجلد موجود بالفعل في القائمة.",
            "यह फ़ोल्डर पहले से सूची में है।", "এই ফোল্ডারটি ইতিমধ্যেই তালিকায় আছে।",
            "یہ فولڈر پہلے ہی فہرست میں ہے۔", "该文件夹已在列表中。")

        ' --- server-features gate -------------------------------------------------

        Add("Функции сервера выключены",
            "Server features are off", "Функції сервера вимкнено", "Serverfunktionen sind aus",
            "Le funzioni server sono disattivate", "Las funciones de servidor están desactivadas",
            "Les fonctions serveur sont désactivées", "As funções de servidor estão desativadas",
            "وظائف الخادم متوقفة", "सर्वर सुविधाएँ बंद हैं", "সার্ভার বৈশিষ্ট্য বন্ধ",
            "سرور خصوصیات بند ہیں", "服务器功能已关闭")

        Add("Общий доступ к папкам поднимает локальный SFTP-сервер и требует одного исключения в брандмауэре Windows (один раз, с правами администратора). Пока это не включено, программа ничего не раздаёт.",
            "Folder sharing runs a local SFTP server and needs one Windows Firewall exception (once, as administrator). Until enabled, nothing is shared.",
            "Спільний доступ до папок піднімає локальний SFTP-сервер і потребує одного винятку в брандмауері Windows (один раз, з правами адміністратора). Поки це не ввімкнено, програма нічого не роздає.",
            "Die Ordnerfreigabe startet einen lokalen SFTP-Server und benötigt eine Windows-Firewall-Ausnahme (einmalig, als Administrator). Bis zur Aktivierung wird nichts freigegeben.",
            "La condivisione delle cartelle avvia un server SFTP locale e richiede un'eccezione nel Windows Firewall (una volta, come amministratore). Finché non è attiva, non viene condiviso nulla.",
            "El uso compartido de carpetas levanta un servidor SFTP local y necesita una excepción en el Firewall de Windows (una vez, como administrador). Hasta activarlo, no se comparte nada.",
            "Le partage de dossiers lance un serveur SFTP local et nécessite une exception dans le pare-feu Windows (une seule fois, en tant qu'administrateur). Tant qu'il n'est pas activé, rien n'est partagé.",
            "A partilha de pastas arranca um servidor SFTP local e precisa de uma exceção na Firewall do Windows (uma vez, como administrador). Até ser ativada, nada é partilhado.",
            "تشغّل مشاركة المجلدات خادم SFTP محليًا وتحتاج إلى استثناء واحد في جدار حماية Windows (مرة واحدة، بصلاحيات المسؤول). وإلى أن تُفعَّل، لا تتم مشاركة أي شيء.",
            "फ़ोल्डर साझाकरण एक स्थानीय SFTP सर्वर चलाता है और Windows फ़ायरवॉल में एक अपवाद माँगता है (एक बार, व्यवस्थापक के रूप में)। सक्षम होने तक कुछ भी साझा नहीं होता।",
            "ফোল্ডার শেয়ারিং একটি স্থানীয় SFTP সার্ভার চালায় এবং Windows ফায়ারওয়ালে একটি ব্যতিক্রম প্রয়োজন (একবার, প্রশাসক হিসেবে)। চালু না হওয়া পর্যন্ত কিছুই শেয়ার হয় না।",
            "فولڈر شیئرنگ ایک مقامی SFTP سرور چلاتی ہے اور Windows فائر وال میں ایک استثنا مانگتی ہے (ایک بار، ایڈمنسٹریٹر کے طور پر)۔ فعال ہونے تک کچھ بھی شیئر نہیں ہوتا۔",
            "文件夹共享会运行一个本地 SFTP 服务器，并需要一条 Windows 防火墙例外（仅一次，需管理员权限）。未启用前不会共享任何内容。")

        Add("Включить функции сервера..",
            "Enable server features..", "Увімкнути функції сервера..", "Serverfunktionen aktivieren..",
            "Attiva le funzioni server..", "Activar las funciones de servidor..",
            "Activer les fonctions serveur..", "Ativar as funções de servidor..",
            "تفعيل وظائف الخادم..", "सर्वर सुविधाएँ सक्षम करें..", "সার্ভার বৈশিষ্ট্য চালু করুন..",
            "سرور خصوصیات فعال کریں..", "启用服务器功能..")

        ' --- status lines ---------------------------------------------------------

        Add("Копировать в буфер",
            "Copy to clipboard", "Копіювати в буфер", "In die Zwischenablage kopieren",
            "Copia negli appunti", "Copiar al portapapeles", "Copier dans le presse-papiers",
            "Copiar para a área de transferência", "نسخ إلى الحافظة",
            "क्लिपबोर्ड पर कॉपी करें", "ক্লিপবোর্ডে কপি করুন", "کلپ بورڈ پر کاپی کریں", "复制到剪贴板")

        Add("Скопировано в буфер.",
            "Copied to clipboard.", "Скопійовано в буфер.", "In die Zwischenablage kopiert.",
            "Copiato negli appunti.", "Copiado al portapapeles.", "Copié dans le presse-papiers.",
            "Copiado para a área de transferência.", "تم النسخ إلى الحافظة.",
            "क्लिपबोर्ड पर कॉपी हो गया।", "ক্লিপবোর্ডে কপি হয়েছে।", "کلپ بورڈ پر کاپی ہو گیا۔", "已复制到剪贴板。")

        Add("определяется..",
            "detecting..", "визначається..", "wird ermittelt..", "rilevamento..",
            "detectando..", "détection..", "a detetar..",
            "جارٍ التحديد..", "पहचाना जा रहा है..", "শনাক্ত করা হচ্ছে..", "معلوم کیا جا رہا ہے..", "检测中..")

        Add("за CGNAT (недоступно)",
            "behind CGNAT (unreachable)", "за CGNAT (недоступно)", "hinter CGNAT (nicht erreichbar)",
            "dietro CGNAT (non raggiungibile)", "detrás de CGNAT (inaccesible)",
            "derrière un CGNAT (injoignable)", "atrás de CGNAT (inalcançável)",
            "خلف CGNAT (غير متاح)", "CGNAT के पीछे (पहुँच नहीं)", "CGNAT-এর পিছনে (পৌঁছানো যায় না)",
            "CGNAT کے پیچھے (ناقابلِ رسائی)", "位于 CGNAT 之后（不可达）")

        Add("адрес неизвестен",
            "address unknown", "адреса невідома", "Adresse unbekannt", "indirizzo sconosciuto",
            "dirección desconocida", "adresse inconnue", "endereço desconhecido",
            "العنوان غير معروف", "पता अज्ञात", "ঠিকানা অজানা", "پتہ نامعلوم", "地址未知")

        Add("Компонент общего доступа не найден - переустановите приложение.",
            "The sharing component is missing - reinstall the app.",
            "Компонент спільного доступу не знайдено - перевстановіть застосунок.",
            "Die Freigabekomponente fehlt - installieren Sie die Anwendung neu.",
            "Il componente di condivisione manca: reinstalla l'applicazione.",
            "Falta el componente de uso compartido: reinstala la aplicación.",
            "Le composant de partage est absent - réinstallez l'application.",
            "Falta o componente de partilha - reinstale a aplicação.",
            "مكوّن المشاركة مفقود - أعد تثبيت التطبيق.",
            "साझाकरण घटक अनुपस्थित है - एप्लिकेशन दोबारा स्थापित करें।",
            "শেয়ারিং উপাদানটি অনুপস্থিত - অ্যাপ্লিকেশনটি পুনরায় ইনস্টল করুন।",
            "شیئرنگ جزو غائب ہے - ایپلیکیشن دوبارہ انسٹال کریں۔",
            "缺少共享组件 - 请重新安装应用。")

        Add("Запуск компаньона..",
            "Starting companion..", "Запуск компаньйона..", "Companion wird gestartet..",
            "Avvio del componente companion..", "Iniciando el componente complementario..",
            "Démarrage du composant compagnon..", "A iniciar o componente companion..",
            "جارٍ تشغيل المكوّن المرافق..", "साथी घटक शुरू हो रहा है..",
            "সহযোগী উপাদান চালু হচ্ছে..", "معاون جزو شروع ہو رہا ہے..", "正在启动配套组件..")

        Add("Не удалось связаться с компаньоном.",
            "Could not reach the companion worker.", "Не вдалося зв'язатися з компаньйоном.",
            "Der Companion-Dienst war nicht erreichbar.", "Impossibile contattare il processo companion.",
            "No se pudo contactar con el proceso complementario.",
            "Impossible de joindre le processus compagnon.", "Não foi possível contactar o processo companion.",
            "تعذّر الوصول إلى العملية المرافقة.", "साथी प्रक्रिया से संपर्क नहीं हो सका।",
            "সহযোগী প্রক্রিয়ার সাথে যোগাযোগ করা যায়নি।", "معاون عمل سے رابطہ نہ ہو سکا۔",
            "无法与配套进程通信。")

        Add("Автозапуском управляет Windows (пакет из Store).",
            "Autostart is managed by Windows (Store package).",
            "Автозапуском керує Windows (пакет зі Store).",
            "Der Autostart wird von Windows verwaltet (Store-Paket).",
            "L'avvio automatico è gestito da Windows (pacchetto dello Store).",
            "El inicio automático lo gestiona Windows (paquete de la Store).",
            "Le démarrage automatique est géré par Windows (paquet du Store).",
            "O arranque automático é gerido pelo Windows (pacote da Store).",
            "يدير Windows التشغيل التلقائي (حزمة من المتجر).",
            "स्वतः-प्रारंभ Windows द्वारा नियंत्रित है (Store पैकेज)।",
            "স্বয়ংক্রিয় সূচনা Windows দ্বারা নিয়ন্ত্রিত (Store প্যাকেজ)।",
            "خودکار آغاز Windows کے زیرِ انتظام ہے (Store پیکیج)۔",
            "自动启动由 Windows 管理（应用商店包）。")

        Add("Роутер: не определён",
            "Router: unknown", "Роутер: не визначено", "Router: unbekannt", "Router: sconosciuto",
            "Router: desconocido", "Routeur : inconnu", "Router: desconhecido",
            "الموجّه: غير معروف", "राउटर: अज्ञात", "রাউটার: অজানা", "روٹر: نامعلوم", "路由器：未知")

        Add("Не удалось определить адрес роутера.",
            "Could not determine the router address.", "Не вдалося визначити адресу роутера.",
            "Die Router-Adresse konnte nicht ermittelt werden.",
            "Impossibile determinare l'indirizzo del router.",
            "No se pudo determinar la dirección del router.",
            "Impossible de déterminer l'adresse du routeur.",
            "Não foi possível determinar o endereço do router.",
            "تعذّر تحديد عنوان الموجّه.", "राउटर का पता निर्धारित नहीं हो सका।",
            "রাউটারের ঠিকানা নির্ধারণ করা যায়নি।", "روٹر کا پتہ معلوم نہ ہو سکا۔", "无法确定路由器地址。")

        Add("Модель не определена - открыт общий поиск.",
            "Model unknown - opened a general search.", "Модель не визначено - відкрито загальний пошук.",
            "Modell unbekannt - eine allgemeine Suche wurde geöffnet.",
            "Modello sconosciuto: aperta una ricerca generica.",
            "Modelo desconocido: se abrió una búsqueda general.",
            "Modèle inconnu - une recherche générale a été ouverte.",
            "Modelo desconhecido - foi aberta uma pesquisa geral.",
            "الطراز غير معروف - تم فتح بحث عام.", "मॉडल अज्ञात - सामान्य खोज खोली गई।",
            "মডেল অজানা - সাধারণ অনুসন্ধান খোলা হয়েছে।", "ماڈل نامعلوم - عام تلاش کھولی گئی۔",
            "型号未知 - 已打开通用搜索。")

        Add("Адрес из интернета ещё не определён.",
            "No internet address yet.", "Адресу з інтернету ще не визначено.",
            "Noch keine Internetadresse.", "Nessun indirizzo internet ancora.",
            "Aún no hay dirección de internet.", "Pas encore d'adresse internet.",
            "Ainda sem endereço de internet.", "لا يوجد عنوان إنترنت بعد.",
            "अभी कोई इंटरनेट पता नहीं।", "এখনও কোনো ইন্টারনেট ঠিকানা নেই।",
            "ابھی کوئی انٹرنیٹ پتہ نہیں۔", "尚无互联网地址。")

        Add("Сервер ответил по внешнему адресу {0} - но проверка не покидала вашу сеть.",
            "The server answered on the external address {0} - but the check never left your network.",
            "Сервер відповів на зовнішній адресі {0} - але перевірка не покидала вашу мережу.",
            "Der Server hat unter der externen Adresse {0} geantwortet - die Prüfung hat Ihr Netzwerk aber nie verlassen.",
            "Il server ha risposto all'indirizzo esterno {0}, ma la verifica non ha lasciato la tua rete.",
            "El servidor respondió en la dirección externa {0}, pero la comprobación no salió de tu red.",
            "Le serveur a répondu à l'adresse externe {0} - mais la vérification n'a jamais quitté votre réseau.",
            "O servidor respondeu no endereço externo {0} - mas a verificação nunca saiu da sua rede.",
            "استجاب الخادم على العنوان الخارجي {0} - لكن الفحص لم يغادر شبكتك.",
            "सर्वर ने बाहरी पते {0} पर उत्तर दिया - पर यह जाँच आपके नेटवर्क से बाहर गई ही नहीं।",
            "সার্ভার বাহ্যিক ঠিকানা {0}-এ সাড়া দিয়েছে - তবে পরীক্ষাটি আপনার নেটওয়ার্কের বাইরে যায়নি।",
            "سرور نے بیرونی پتے {0} پر جواب دیا - مگر یہ جانچ آپ کے نیٹ ورک سے باہر گئی ہی نہیں۔",
            "服务器在外部地址 {0} 上作出了响应 - 但这次检查从未离开您的网络。")

        Add("✗ С этого ПК не отвечает. Роутер может не пускать на свой адрес изнутри - проверьте с телефона по мобильной сети.",
            "✗ No answer from this PC. Your router may block its own address from inside - test from the phone on mobile data.",
            "✗ З цього ПК не відповідає. Роутер може не пускати на свою адресу зсередини - перевірте з телефона через мобільну мережу.",
            "✗ Von diesem PC keine Antwort. Ihr Router lässt seine eigene Adresse von innen möglicherweise nicht zu - testen Sie es vom Telefon über Mobilfunk.",
            "✗ Nessuna risposta da questo PC. Il router potrebbe bloccare il proprio indirizzo dall'interno: prova dal telefono in rete mobile.",
            "✗ Sin respuesta desde este PC. El router puede bloquear su propia dirección desde dentro: prueba desde el teléfono con datos móviles.",
            "✗ Aucune réponse depuis ce PC. Votre routeur peut bloquer sa propre adresse depuis l'intérieur - testez depuis le téléphone en données mobiles.",
            "✗ Sem resposta a partir deste PC. O router pode bloquear o próprio endereço a partir de dentro - teste com o telemóvel em dados móveis.",
            "✗ لا استجابة من هذا الحاسوب. قد يمنع الموجّه عنوانه الخاص من الداخل - اختبر من الهاتف عبر بيانات الجوال.",
            "✗ इस पीसी से कोई उत्तर नहीं। राउटर भीतर से अपने ही पते को रोक सकता है - मोबाइल डेटा पर फ़ोन से जाँचें।",
            "✗ এই পিসি থেকে কোনো সাড়া নেই। রাউটার ভেতর থেকে নিজের ঠিকানা আটকে দিতে পারে - মোবাইল ডেটায় ফোন থেকে পরীক্ষা করুন।",
            "✗ اس پی سی سے کوئی جواب نہیں۔ روٹر اندر سے اپنے ہی پتے کو روک سکتا ہے - موبائل ڈیٹا پر فون سے جانچیں۔",
            "✗ 本机无响应。路由器可能拒绝从内部访问其地址 - 请用手机通过移动数据测试。")

        ' --- internet-access test dialog (Share_Access_Test_Form) -----------------
        ' The verdict lines above are the dialog's headline; these carry the title, the
        ' address under test and the reasoning that never fit into a status line.

        Add("Проверка доступа из интернета",
            "Internet access test", "Перевірка доступу з інтернету", "Test des Internetzugriffs",
            "Verifica dell'accesso da internet", "Comprobación del acceso desde internet",
            "Test de l'accès depuis internet", "Teste do acesso pela internet",
            "اختبار الوصول من الإنترنت", "इंटरनेट पहुँच की जाँच", "ইন্টারনেট অ্যাক্সেস পরীক্ষা",
            "انٹرنیٹ رسائی کی جانچ", "互联网访问测试")

        Add("Проверяемый адрес: {0}",
            "Address tested: {0}", "Адреса, що перевіряється: {0}", "Geprüfte Adresse: {0}",
            "Indirizzo verificato: {0}", "Dirección comprobada: {0}", "Adresse testée : {0}",
            "Endereço testado: {0}", "العنوان المُختبَر: {0}", "जाँचा गया पता: {0}",
            "পরীক্ষিত ঠিকানা: {0}", "جانچا گیا پتہ: {0}", "测试的地址：{0}")

        Add("Запрос ушёл с этого ПК на внешний адрес роутера и вернулся внутрь - так проверяется правило проброса, но не то, пускает ли провайдер входящие подключения снаружи. Окончательно подтвердит только телефон: выключите на нём Wi-Fi, оставьте мобильный интернет и откройте QR-код.",
            "The request left this PC for the router's external address and came straight back inside - that tests the forwarding rule, not whether your provider lets inbound connections in from outside. Only the phone can settle it: turn its Wi-Fi off, leave mobile data on and open the QR code.",
            "Запит пішов з цього ПК на зовнішню адресу роутера і повернувся всередину - так перевіряється правило пробросу, але не те, чи пускає провайдер вхідні підключення ззовні. Остаточно підтвердить лише телефон: вимкніть на ньому Wi-Fi, залиште мобільний інтернет і відкрийте QR-код.",
            "Die Anfrage ging von diesem PC an die externe Adresse des Routers und kam direkt wieder herein - das prüft die Weiterleitungsregel, nicht ob Ihr Anbieter eingehende Verbindungen von außen zulässt. Endgültig klärt das nur das Telefon: WLAN dort ausschalten, mobile Daten anlassen und den QR-Code öffnen.",
            "La richiesta è partita da questo PC verso l'indirizzo esterno del router ed è rientrata subito: così si verifica la regola di inoltro, non se il provider accetta connessioni in ingresso dall'esterno. Solo il telefono può dirlo con certezza: spegni il Wi-Fi, lascia i dati mobili e apri il codice QR.",
            "La petición salió de este PC hacia la dirección externa del router y volvió adentro: eso comprueba la regla de reenvío, no si tu proveedor permite conexiones entrantes desde fuera. Solo el teléfono lo confirma: apaga su Wi-Fi, deja los datos móviles y abre el código QR.",
            "La requête est partie de ce PC vers l'adresse externe du routeur et est revenue à l'intérieur - cela teste la règle de redirection, pas le fait que votre fournisseur laisse entrer les connexions depuis l'extérieur. Seul le téléphone peut trancher : coupez son Wi-Fi, laissez les données mobiles et ouvrez le code QR.",
            "O pedido saiu deste PC para o endereço externo do router e voltou logo para dentro - isso testa a regra de encaminhamento, não se o seu operador deixa entrar ligações vindas de fora. Só o telemóvel confirma: desligue o Wi-Fi, deixe os dados móveis e abra o código QR.",
            "خرج الطلب من هذا الحاسوب إلى العنوان الخارجي للموجّه وعاد إلى الداخل مباشرة - وهذا يختبر قاعدة التوجيه، لا ما إذا كان المزوّد يسمح بالاتصالات الواردة من الخارج. الهاتف وحده يحسم الأمر: أوقف Wi-Fi عليه، وأبقِ بيانات الجوال، وافتح رمز QR.",
            "अनुरोध इस पीसी से राउटर के बाहरी पते पर गया और सीधे भीतर लौट आया - इससे फ़ॉरवर्डिंग नियम की जाँच होती है, यह नहीं कि आपका प्रदाता बाहर से आने वाले कनेक्शन भीतर आने देता है या नहीं। निर्णय केवल फ़ोन कर सकता है: उसका Wi-Fi बंद करें, मोबाइल डेटा चालू रखें और QR कोड खोलें।",
            "অনুরোধটি এই পিসি থেকে রাউটারের বাহ্যিক ঠিকানায় গিয়ে সরাসরি ভেতরে ফিরে এসেছে - এতে ফরওয়ার্ডিং নিয়ম যাচাই হয়, আপনার প্রোভাইডার বাইরে থেকে আসা সংযোগ ঢুকতে দেয় কি না তা নয়। কেবল ফোনই এটি নিশ্চিত করতে পারে: তার Wi-Fi বন্ধ করুন, মোবাইল ডেটা চালু রাখুন এবং QR কোড খুলুন।",
            "درخواست اس پی سی سے روٹر کے بیرونی پتے پر گئی اور سیدھی اندر واپس آ گئی - اس سے فارورڈنگ اصول جانچا جاتا ہے، یہ نہیں کہ آپ کا فراہم کنندہ باہر سے آنے والے کنکشنز کو اندر آنے دیتا ہے یا نہیں۔ فیصلہ صرف فون کر سکتا ہے: اس کا Wi-Fi بند کریں، موبائل ڈیٹا چالو رکھیں اور QR کوڈ کھولیں۔",
            "请求从这台电脑发往路由器的外部地址后又直接绕回内部 - 这检验的是转发规则，而不是运营商是否允许来自外部的入站连接。只有手机能给出结论：关掉它的 Wi-Fi，保留移动数据，然后打开二维码。")

        Add("Порт снаружи открыт, но ответила не наша программа. Обычно так бывает, когда правило проброса на роутере ведёт на другое устройство или этот порт занят другой службой. Проверьте правило: внешний порт должен вести на этот ПК и на порт раздачи.",
            "The port is open from outside, but the reply did not come from our program. Usually the forwarding rule on the router points at another device, or another service holds that port. Check the rule: the external port must lead to this PC and to the share port.",
            "Порт ззовні відкритий, але відповіла не наша програма. Зазвичай так буває, коли правило пробросу на роутері веде на інший пристрій або цей порт зайнятий іншою службою. Перевірте правило: зовнішній порт має вести на цей ПК і на порт роздачі.",
            "Der Port ist von außen offen, aber die Antwort kam nicht von unserem Programm. Meist zeigt die Weiterleitungsregel im Router auf ein anderes Gerät, oder ein anderer Dienst belegt diesen Port. Prüfen Sie die Regel: Der externe Port muss auf diesen PC und auf den Freigabeport führen.",
            "La porta è aperta dall'esterno, ma non ha risposto il nostro programma. Di solito la regola di inoltro sul router punta a un altro dispositivo, oppure un altro servizio occupa quella porta. Controlla la regola: la porta esterna deve portare a questo PC e alla porta della condivisione.",
            "El puerto está abierto desde fuera, pero quien respondió no es nuestro programa. Normalmente la regla de redirección del router apunta a otro dispositivo, o ese puerto lo ocupa otro servicio. Revisa la regla: el puerto externo debe llevar a este PC y al puerto del uso compartido.",
            "Le port est ouvert depuis l'extérieur, mais ce n'est pas notre programme qui a répondu. En général, la règle de redirection du routeur pointe vers un autre appareil, ou un autre service occupe ce port. Vérifiez la règle : le port externe doit mener à ce PC et au port du partage.",
            "A porta está aberta do exterior, mas quem respondeu não foi o nosso programa. Normalmente a regra de encaminhamento no router aponta para outro dispositivo, ou outro serviço ocupa essa porta. Verifique a regra: a porta externa deve levar a este PC e à porta da partilha.",
            "المنفذ مفتوح من الخارج، لكن الذي استجاب ليس برنامجنا. غالبًا ما تشير قاعدة إعادة التوجيه في الموجّه إلى جهاز آخر، أو تشغل خدمة أخرى هذا المنفذ. تحقّق من القاعدة: يجب أن يقود المنفذ الخارجي إلى هذا الحاسوب وإلى منفذ المشاركة.",
            "पोर्ट बाहर से खुला है, पर उत्तर हमारे कार्यक्रम ने नहीं दिया। आमतौर पर राउटर का फ़ॉरवर्डिंग नियम किसी दूसरे उपकरण की ओर जाता है, या उस पोर्ट पर कोई दूसरी सेवा चल रही है। नियम जाँचें: बाहरी पोर्ट इसी पीसी और साझाकरण के पोर्ट तक जाना चाहिए।",
            "পোর্টটি বাইরে থেকে খোলা, কিন্তু সাড়া দিয়েছে আমাদের প্রোগ্রাম নয়। সাধারণত রাউটারের ফরওয়ার্ডিং নিয়ম অন্য ডিভাইসের দিকে যায়, অথবা ওই পোর্টে অন্য কোনো পরিষেবা চলছে। নিয়মটি যাচাই করুন: বাহ্যিক পোর্টটি এই পিসি ও শেয়ারিং পোর্টে যাওয়া উচিত।",
            "پورٹ باہر سے کھلا ہے، مگر جواب ہمارے پروگرام نے نہیں دیا۔ عام طور پر روٹر کا فارورڈنگ اصول کسی دوسرے آلے کی طرف جاتا ہے، یا اس پورٹ پر کوئی اور سروس چل رہی ہے۔ اصول جانچیں: بیرونی پورٹ اسی پی سی اور شیئرنگ پورٹ تک جانا چاہیے۔",
            "端口从外部是开放的，但应答的并不是本程序。通常是路由器的转发规则指向了其他设备，或该端口被别的服务占用。请检查规则：外部端口应指向本机以及共享所用的端口。")

        Add("Проверка идёт с этого же ПК, поэтому она не окончательная: многие роутеры не пускают запрос на свой внешний адрес изнутри домашней сети. Надёжный способ - открыть QR-код на телефоне, выключив на нём Wi-Fi и оставив мобильный интернет. Если и так подключиться не удаётся, настройте проброс порта - кнопка «Как настроить доступ через интернет».",
            "The test runs from this same PC, so it is not conclusive: many routers refuse a request to their own external address from inside the home network. The reliable way is to open the QR code on the phone with its Wi-Fi off and mobile data on. If it still cannot connect, set up port forwarding - the 'How to set up internet access' button.",
            "Перевірка йде з цього ж ПК, тому вона не остаточна: багато роутерів не пускають запит на свою зовнішню адресу зсередини домашньої мережі. Надійний спосіб - відкрити QR-код на телефоні, вимкнувши на ньому Wi-Fi і залишивши мобільний інтернет. Якщо й так підключитися не вдається, налаштуйте проброс порту - кнопка «Як налаштувати доступ через інтернет».",
            "Der Test läuft von diesem PC aus und ist deshalb nicht endgültig: Viele Router lassen eine Anfrage an ihre eigene externe Adresse aus dem Heimnetz heraus nicht zu. Zuverlässig ist es, den QR-Code auf dem Telefon zu öffnen - dort WLAN aus, mobile Daten an. Klappt es auch dann nicht, richten Sie die Portweiterleitung ein - Schaltfläche «Internetzugriff einrichten».",
            "La verifica parte da questo stesso PC, quindi non è definitiva: molti router rifiutano dall'interno della rete di casa una richiesta al proprio indirizzo esterno. Il modo affidabile è aprire il codice QR sul telefono con il Wi-Fi spento e la rete mobile attiva. Se anche così non si collega, configura l'inoltro della porta - pulsante «Come configurare l'accesso da internet».",
            "La comprobación se hace desde este mismo PC, por lo que no es concluyente: muchos routers rechazan desde la red doméstica una petición a su propia dirección externa. Lo fiable es abrir el código QR en el teléfono con el Wi-Fi apagado y los datos móviles activos. Si aun así no conecta, configura la redirección de puerto - botón «Cómo configurar el acceso desde internet».",
            "Le test part de ce même PC, il n'est donc pas concluant : beaucoup de routeurs refusent, depuis le réseau domestique, une requête vers leur propre adresse externe. La méthode fiable est d'ouvrir le code QR sur le téléphone, Wi-Fi coupé et données mobiles activées. Si la connexion échoue encore, configurez la redirection de port - bouton « Comment configurer l'accès internet ».",
            "O teste parte deste mesmo PC, por isso não é conclusivo: muitos routers recusam, a partir da rede doméstica, um pedido ao seu próprio endereço externo. O modo fiável é abrir o código QR no telemóvel com o Wi-Fi desligado e os dados móveis ligados. Se mesmo assim não ligar, configure o encaminhamento de porta - botão «Como configurar o acesso pela internet».",
            "يجري الاختبار من هذا الحاسوب نفسه، لذا فهو غير حاسم: كثير من الموجّهات ترفض من داخل الشبكة المنزلية أي طلب إلى عنوانها الخارجي. الطريقة الموثوقة هي فتح رمز QR على الهاتف مع إيقاف Wi-Fi وتشغيل بيانات الجوال. وإذا لم يتصل حتى حينها، فاضبط إعادة توجيه المنفذ - زر «كيفية إعداد الوصول من الإنترنت».",
            "जाँच इसी पीसी से चलती है, इसलिए यह अंतिम नहीं है: कई राउटर घरेलू नेटवर्क के भीतर से अपने ही बाहरी पते पर आया अनुरोध नहीं मानते। भरोसेमंद तरीका यह है कि फ़ोन पर Wi-Fi बंद करके और मोबाइल डेटा चालू रखकर QR कोड खोलें। तब भी न जुड़े, तो पोर्ट फ़ॉरवर्डिंग सेट करें - बटन «इंटरनेट पहुँच कैसे सेट करें»।",
            "পরীক্ষাটি এই পিসি থেকেই চলে, তাই এটি চূড়ান্ত নয়: অনেক রাউটার বাড়ির নেটওয়ার্কের ভেতর থেকে নিজের বাহ্যিক ঠিকানায় আসা অনুরোধ গ্রহণ করে না। নির্ভরযোগ্য উপায় হলো ফোনের Wi-Fi বন্ধ রেখে ও মোবাইল ডেটা চালু রেখে QR কোডটি খোলা। তাতেও সংযোগ না হলে পোর্ট ফরওয়ার্ডিং সেট করুন - «কীভাবে ইন্টারনেট অ্যাক্সেস সেট করবেন» বোতাম।",
            "جانچ اسی پی سی سے چلتی ہے، اس لیے یہ حتمی نہیں: بہت سے روٹر گھریلو نیٹ ورک کے اندر سے اپنے ہی بیرونی پتے پر آنے والی درخواست قبول نہیں کرتے۔ قابلِ اعتماد طریقہ یہ ہے کہ فون پر Wi-Fi بند اور موبائل ڈیٹا آن کر کے QR کوڈ کھولیں۔ اگر پھر بھی رابطہ نہ ہو تو پورٹ فارورڈنگ ترتیب دیں - «انٹرنیٹ رسائی کیسے ترتیب دیں» بٹن۔",
            "该测试从本机发起，因此结论并不确定：许多路由器不允许家庭网络内部访问其自身的外部地址。可靠的做法是在手机上关闭 Wi-Fi、开启移动数据后再打开二维码。若仍无法连接，请设置端口转发 - 「如何设置互联网访问」按钮。")

        ' --- share lifecycle ------------------------------------------------------

        Add("Обновляю список папок..",
            "Updating the folder list..", "Оновлюю список папок..", "Ordnerliste wird aktualisiert..",
            "Aggiornamento dell'elenco cartelle..", "Actualizando la lista de carpetas..",
            "Mise à jour de la liste des dossiers..", "A atualizar a lista de pastas..",
            "جارٍ تحديث قائمة المجلدات..", "फ़ोल्डर सूची अपडेट हो रही है..",
            "ফোল্ডার তালিকা হালনাগাদ হচ্ছে..", "فولڈر فہرست اپ ڈیٹ ہو رہی ہے..", "正在更新文件夹列表..")

        Add("Минутку..",
            "One moment..", "Хвилинку..", "Einen Moment..", "Un attimo..", "Un momento..",
            "Un instant..", "Um momento..", "لحظة من فضلك..", "एक क्षण..",
            "এক মুহূর্ত..", "ایک لمحہ..", "请稍候..")

        Add("Останавливаю раздачу..",
            "Stopping sharing..", "Зупиняю роздачу..", "Freigabe wird gestoppt..",
            "Arresto della condivisione..", "Deteniendo el uso compartido..",
            "Arrêt du partage..", "A parar a partilha..",
            "جارٍ إيقاف المشاركة..", "साझाकरण रोका जा रहा है..",
            "শেয়ারিং বন্ধ করা হচ্ছে..", "شیئرنگ روکی جا رہی ہے..", "正在停止共享..")

        Add("Раздача остановлена.",
            "Sharing stopped.", "Роздачу зупинено.", "Freigabe gestoppt.", "Condivisione arrestata.",
            "Uso compartido detenido.", "Partage arrêté.", "Partilha parada.",
            "تم إيقاف المشاركة.", "साझाकरण रुक गया।", "শেয়ারিং বন্ধ হয়েছে।", "شیئرنگ رک گئی۔", "共享已停止。")

        Add("Сначала добавьте папку и отметьте её галочкой.",
            "Add a folder and tick it first.", "Спершу додайте папку й позначте її галочкою.",
            "Fügen Sie zuerst einen Ordner hinzu und setzen Sie das Häkchen.",
            "Aggiungi prima una cartella e spuntala.", "Primero añade una carpeta y márcala.",
            "Ajoutez d'abord un dossier et cochez-le.", "Adicione primeiro uma pasta e assinale-a.",
            "أضف مجلدًا أولاً وحدّده بعلامة.", "पहले एक फ़ोल्डर जोड़ें और उस पर निशान लगाएँ।",
            "প্রথমে একটি ফোল্ডার যোগ করে তাতে টিক দিন।", "پہلے ایک فولڈر شامل کریں اور اس پر نشان لگائیں۔",
            "请先添加文件夹并勾选它。")

        Add("Включаю раздачу..",
            "Starting sharing..", "Вмикаю роздачу..", "Freigabe wird gestartet..",
            "Avvio della condivisione..", "Iniciando el uso compartido..",
            "Démarrage du partage..", "A iniciar a partilha..",
            "جارٍ بدء المشاركة..", "साझाकरण शुरू हो रहा है..",
            "শেয়ারিং শুরু হচ্ছে..", "شیئرنگ شروع ہو رہی ہے..", "正在开始共享..")

        Add("Раздача запущена.",
            "Sharing started.", "Роздачу запущено.", "Freigabe gestartet.", "Condivisione avviata.",
            "Uso compartido iniciado.", "Partage démarré.", "Partilha iniciada.",
            "بدأت المشاركة.", "साझाकरण शुरू हो गया।", "শেয়ারিং শুরু হয়েছে।", "شیئرنگ شروع ہو گئی۔", "共享已开始。")

        Add("Запущено, адрес не подтверждён - проверьте брандмауэр/сеть.",
            "Started, address unconfirmed - check firewall/network.",
            "Запущено, адресу не підтверджено - перевірте брандмауер/мережу.",
            "Gestartet, Adresse unbestätigt - prüfen Sie Firewall/Netzwerk.",
            "Avviata, indirizzo non confermato: controlla firewall/rete.",
            "Iniciado, dirección sin confirmar: revisa el cortafuegos o la red.",
            "Démarré, adresse non confirmée - vérifiez le pare-feu/le réseau.",
            "Iniciada, endereço por confirmar - verifique a firewall/rede.",
            "بدأت، والعنوان غير مؤكَّد - تحقّق من جدار الحماية/الشبكة.",
            "शुरू हुआ, पता अपुष्ट - फ़ायरवॉल/नेटवर्क जाँचें।",
            "শুরু হয়েছে, ঠিকানা অনিশ্চিত - ফায়ারওয়াল/নেটওয়ার্ক যাচাই করুন।",
            "شروع ہو گئی، پتہ غیر مصدقہ - فائر وال/نیٹ ورک جانچیں۔",
            "已启动，地址未确认 - 请检查防火墙/网络。")

        Add("Раздача с этого ПК работает",
            "Sharing from this PC is on", "Роздача з цього ПК працює",
            "Die Freigabe von diesem PC läuft", "La condivisione da questo PC è attiva",
            "El uso compartido desde este PC está activo", "Le partage depuis ce PC est actif",
            "A partilha a partir deste PC está ativa", "المشاركة من هذا الحاسوب مفعّلة",
            "इस पीसी से साझाकरण चालू है", "এই পিসি থেকে শেয়ারিং চালু আছে",
            "اس پی سی سے شیئرنگ آن ہے", "本机共享已开启")

        Add("Раздача выключена",
            "Sharing is off", "Роздачу вимкнено", "Die Freigabe ist aus",
            "La condivisione è disattivata", "El uso compartido está desactivado",
            "Le partage est désactivé", "A partilha está desativada",
            "المشاركة متوقفة", "साझाकरण बंद है", "শেয়ারিং বন্ধ", "شیئرنگ بند ہے", "共享已关闭")

        Add("Сначала запустите сервер.",
            "Start the server first.", "Спершу запустіть сервер.", "Starten Sie zuerst den Server.",
            "Avvia prima il server.", "Inicia primero el servidor.", "Démarrez d'abord le serveur.",
            "Inicie primeiro o servidor.", "شغّل الخادم أولاً.", "पहले सर्वर शुरू करें।",
            "প্রথমে সার্ভার চালু করুন।", "پہلے سرور شروع کریں۔", "请先启动服务器。")

        Add("Fast Media Sorter не найден рядом.",
            "Fast Media Sorter not found alongside.", "Fast Media Sorter не знайдено поряд.",
            "Fast Media Sorter wurde nicht daneben gefunden.", "Fast Media Sorter non è stato trovato accanto.",
            "No se encontró Fast Media Sorter al lado.", "Fast Media Sorter est introuvable à côté.",
            "O Fast Media Sorter não foi encontrado ao lado.", "لم يُعثر على Fast Media Sorter بجواره.",
            "Fast Media Sorter पास नहीं मिला।", "পাশে Fast Media Sorter পাওয়া যায়নি।",
            "Fast Media Sorter ساتھ نہیں ملا۔", "未在旁边找到 Fast Media Sorter。")

        ' --- resource types (also used by the wizard and the per-folder dialog) ----

        Add("Аудиотека",
            "Audio library", "Аудіотека", "Audiothek", "Libreria audio", "Biblioteca de audio",
            "Bibliothèque audio", "Biblioteca de áudio", "مكتبة صوتية", "ऑडियो लाइब्रेरी",
            "অডিও লাইব্রেরি", "آڈیو لائبریری", "音频库")

        Add("Видеотека",
            "Video library", "Відеотека", "Videothek", "Videoteca", "Videoteca",
            "Vidéothèque", "Videoteca", "مكتبة فيديو", "वीडियो लाइब्रेरी",
            "ভিডিও লাইব্রেরি", "ویڈیو لائبریری", "视频库")

        Add("Фотохранилище",
            "Photo storage", "Фотосховище", "Fotospeicher", "Archivio foto", "Almacén de fotos",
            "Stockage photo", "Armazenamento de fotos", "مخزن الصور", "फ़ोटो संग्रह",
            "ফটো সংগ্রহ", "تصاویر کا ذخیرہ", "照片存储")

        Add("Документы",
            "Documents", "Документи", "Dokumente", "Documenti", "Documentos", "Documents",
            "Documentos", "المستندات", "दस्तावेज़", "নথি", "دستاویزات", "文档")

        Add("Все файлы",
            "All files", "Усі файли", "Alle Dateien", "Tutti i file", "Todos los archivos",
            "Tous les fichiers", "Todos os ficheiros", "كل الملفات", "सभी फ़ाइलें",
            "সব ফাইল", "تمام فائلیں", "所有文件")

        ' --- the compact window: section headers, header strip, settings ----------
        ' SPECIFICATION_SHARE_MANAGER_COMPACT_WINDOW.md §5. The three section titles are
        ' short ON PURPOSE - they are measured at the minimum window width in all thirteen
        ' languages (§7.8) and, unlike the summaries beside them, must never ellipsize.

        Add("Доступ с телефона",
            "Phone access", "Доступ з телефона", "Zugriff vom Telefon", "Accesso dal telefono",
            "Acceso desde el teléfono", "Accès depuis le téléphone", "Acesso pelo telemóvel",
            "الوصول من الهاتف", "फ़ोन से पहुँच", "ফোন থেকে অ্যাক্সেস", "فون سے رسائی", "手机访问")

        Add("Доступ из интернета",
            "Internet access", "Доступ з інтернету", "Internetzugriff", "Accesso da internet",
            "Acceso desde internet", "Accès depuis internet", "Acesso pela internet",
            "الوصول عبر الإنترنت", "इंटरनेट से पहुँच", "ইন্টারনেট থেকে অ্যাক্সেস",
            "انٹرنیٹ سے رسائی", "互联网访问")

        Add("Статистика",
            "Statistics", "Статистика", "Statistik", "Statistiche", "Estadísticas",
            "Statistiques", "Estatísticas", "الإحصائيات", "आँकड़े", "পরিসংখ্যান",
            "اعداد و شمار", "统计")

        Add("Раздача работает - {0}, порт {1}",
            "Sharing is on - {0}, port {1}", "Роздача працює - {0}, порт {1}",
            "Freigabe aktiv - {0}, Port {1}", "Condivisione attiva - {0}, porta {1}",
            "Compartición activa - {0}, puerto {1}", "Partage actif - {0}, port {1}",
            "Partilha ativa - {0}, porta {1}", "المشاركة تعمل - {0}، المنفذ {1}",
            "साझाकरण चालू है - {0}, पोर्ट {1}", "শেয়ারিং চালু - {0}, পোর্ট {1}",
            "شیئرنگ چالو ہے - {0}، پورٹ {1}", "共享已开启 - {0}，端口 {1}")

        ' Three plural forms because the Russian source needs three; a language whose rule
        ' is simpler maps two of them onto the same wording, which costs nothing.
        Add("{0} папка",
            "{0} folder", "{0} папка", "{0} Ordner", "{0} cartella", "{0} carpeta",
            "{0} dossier", "{0} pasta", "{0} مجلد", "{0} फ़ोल्डर", "{0} ফোল্ডার",
            "{0} فولڈر", "{0} 个文件夹")

        Add("{0} папки",
            "{0} folders", "{0} папки", "{0} Ordner", "{0} cartelle", "{0} carpetas",
            "{0} dossiers", "{0} pastas", "{0} مجلدات", "{0} फ़ोल्डर", "{0} ফোল্ডার",
            "{0} فولڈرز", "{0} 个文件夹")

        Add("{0} папок",
            "{0} folders", "{0} папок", "{0} Ordner", "{0} cartelle", "{0} carpetas",
            "{0} dossiers", "{0} pastas", "{0} مجلدات", "{0} फ़ोल्डर", "{0} ফোল্ডার",
            "{0} فولڈرز", "{0} 个文件夹")

        Add("{0} подключений, {1} файлов",
            "{0} connections, {1} files", "{0} підключень, {1} файлів",
            "{0} Verbindungen, {1} Dateien", "{0} connessioni, {1} file",
            "{0} conexiones, {1} archivos", "{0} connexions, {1} fichiers",
            "{0} ligações, {1} ficheiros", "{0} اتصالات، {1} ملفات",
            "{0} कनेक्शन, {1} फ़ाइलें", "{0} সংযোগ, {1} ফাইল",
            "{0} کنکشنز، {1} فائلیں", "{0} 次连接，{1} 个文件")

        Add("Подробнее..",
            "Details..", "Докладніше..", "Details..", "Dettagli..", "Detalles..",
            "Détails..", "Detalhes..", "التفاصيل..", "विवरण..", "বিস্তারিত..",
            "تفصیلات..", "详情..")

        Add("Справка ▾",
            "Help ▾", "Довідка ▾", "Hilfe ▾", "Guida ▾", "Ayuda ▾", "Aide ▾", "Ajuda ▾",
            "مساعدة ▾", "सहायता ▾", "সহায়তা ▾", "مدد ▾", "帮助 ▾")

        Add("Показать пароль",
            "Show password", "Показати пароль", "Passwort anzeigen", "Mostra la password",
            "Mostrar la contraseña", "Afficher le mot de passe", "Mostrar a palavra-passe",
            "إظهار كلمة المرور", "पासवर्ड दिखाएँ", "পাসওয়ার্ড দেখান",
            "پاس ورڈ دکھائیں", "显示密码")

        Add("Скрыть пароль",
            "Hide password", "Сховати пароль", "Passwort verbergen", "Nascondi la password",
            "Ocultar la contraseña", "Masquer le mot de passe", "Ocultar a palavra-passe",
            "إخفاء كلمة المرور", "पासवर्ड छिपाएँ", "পাসওয়ার্ড লুকান",
            "پاس ورڈ چھپائیں", "隐藏密码")

        Add("Настройки",
            "Settings", "Налаштування", "Einstellungen", "Impostazioni", "Configuración",
            "Paramètres", "Definições", "الإعدادات", "सेटिंग्स", "সেটিংস", "ترتیبات", "设置")

        Add("Настройки менеджера..",
            "Manager settings..", "Налаштування менеджера..", "Manager-Einstellungen..",
            "Impostazioni del gestore..", "Ajustes del gestor..",
            "Paramètres du gestionnaire..", "Definições do gestor..", "إعدادات المدير..",
            "मैनेजर सेटिंग्स..", "ম্যানেজার সেটিংস..", "مینیجر کی ترتیبات..", "管理器设置..")

        Add("Настройки менеджера",
            "Manager settings", "Налаштування менеджера", "Manager-Einstellungen",
            "Impostazioni del gestore", "Ajustes del gestor", "Paramètres du gestionnaire",
            "Definições do gestor", "إعدادات المدير", "मैनेजर सेटिंग्स",
            "ম্যানেজার সেটিংস", "مینیجر کی ترتیبات", "管理器设置")

        Add("Запуск",
            "Startup", "Запуск", "Start", "Avvio", "Inicio", "Démarrage", "Arranque",
            "بدء التشغيل", "स्टार्टअप", "স্টার্টআপ", "اسٹارٹ اپ", "启动")

        Add("Сеть",
            "Network", "Мережа", "Netzwerk", "Rete", "Red", "Réseau", "Rede",
            "الشبكة", "नेटवर्क", "নেটওয়ার্ক", "نیٹ ورک", "网络")

        Add("Хостинг",
            "Hosting", "Хостинг", "Hosting", "Hosting", "Alojamiento", "Hébergement",
            "Alojamento", "الاستضافة", "होस्टिंग", "হোস্টিং", "ہوسٹنگ", "托管")

        ' The settings groups' folded summaries - the answer without unfolding anything.
        Add("Автозапуск включён",
            "Autostart on", "Автозапуск увімкнено", "Autostart ein", "Avvio automatico attivo",
            "Inicio automático activado", "Démarrage automatique activé", "Arranque automático ativado",
            "التشغيل التلقائي مفعّل", "ऑटोस्टार्ट चालू", "অটোস্টার্ট চালু",
            "آٹو اسٹارٹ آن", "自动启动：开")

        Add("Автозапуск выключен",
            "Autostart off", "Автозапуск вимкнено", "Autostart aus", "Avvio automatico disattivo",
            "Inicio automático desactivado", "Démarrage automatique désactivé", "Arranque automático desativado",
            "التشغيل التلقائي معطّل", "ऑटोस्टार्ट बंद", "অটোস্টার্ট বন্ধ",
            "آٹو اسٹارٹ آف", "自动启动：关")

        Add("до {0} подключений, порт {1}",
            "up to {0} connections, port {1}", "до {0} підключень, порт {1}",
            "bis zu {0} Verbindungen, Port {1}", "fino a {0} connessioni, porta {1}",
            "hasta {0} conexiones, puerto {1}", "jusqu'à {0} connexions, port {1}",
            "até {0} ligações, porta {1}", "حتى {0} اتصالات، المنفذ {1}",
            "अधिकतम {0} कनेक्शन, पोर्ट {1}", "সর্বোচ্চ {0} সংযোগ, পোর্ট {1}",
            "زیادہ سے زیادہ {0} کنکشنز، پورٹ {1}", "最多 {0} 个连接，端口 {1}")

        Add("до {0} подключений",
            "up to {0} connections", "до {0} підключень", "bis zu {0} Verbindungen",
            "fino a {0} connessioni", "hasta {0} conexiones", "jusqu'à {0} connexions",
            "até {0} ligações", "حتى {0} اتصالات", "अधिकतम {0} कनेक्शन",
            "সর্বোচ্চ {0} সংযোগ", "زیادہ سے زیادہ {0} کنکشنز", "最多 {0} 个连接")

    End Sub

End Class
