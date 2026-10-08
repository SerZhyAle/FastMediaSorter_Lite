Option Strict On

' <summary>
' Strings of the server welcome (spec 185): the window title, the step frame, the three named parts of every step
' (what it is, why it exists, how it will work) and the inputs of block A (the share server) and block B (the
' Media Exchange Server, shown only when its gate is open).
'
' The product NAME is never in this table: the sentence that mentions the exchange group takes a {0} placeholder
' filled from ProductNames.ExchangeServer, so it stays Latin in all 13 languages. The program is called "the program"
' ("программа") here, not by its name. House style: ".." for an ellipsis, a plain hyphen, ё in Russian.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' Никогда не ставьте «умные» кавычки в литерал - VB считает U+201C/U+201D разделителями строки;
' используются «…» и 「…」.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddServerWelcomeStrings()

        ' --- the frame -------------------------------------------------------------------------------
        Add("Первый запуск сервера",
            "First run of the server", "Перший запуск сервера", "Erster Start des Servers",
            "Primo avvio del server", "Primer inicio del servidor", "Premier démarrage du serveur",
            "Primeiro arranque do servidor", "أول تشغيل للخادم", "सर्वर का पहला चलन",
            "সার্ভারের প্রথম চালু", "سرور کا پہلا اجرا", "服务器首次运行")
        Add("Шаг {0} из {1}",
            "Step {0} of {1}", "Крок {0} з {1}", "Schritt {0} von {1}",
            "Passo {0} di {1}", "Paso {0} de {1}", "Étape {0} sur {1}",
            "Passo {0} de {1}", "الخطوة {0} من {1}", "चरण {0} / {1}",
            "ধাপ {0} / {1}", "مرحلہ {0} از {1}", "第 {0} 步，共 {1} 步")
        Add("Позже",
            "Later", "Пізніше", "Später",
            "Più tardi", "Más tarde", "Plus tard",
            "Mais tarde", "لاحقاً", "बाद में",
            "পরে", "بعد میں", "稍后")
        Add("Назад",
            "Back", "Назад", "Zurück",
            "Indietro", "Atrás", "Précédent",
            "Voltar", "رجوع", "पीछे",
            "পিছনে", "پیچھے", "返回")
        Add("Далее",
            "Next", "Далі", "Weiter",
            "Avanti", "Siguiente", "Suivant",
            "Avançar", "التالي", "आगे",
            "পরবর্তী", "آگے", "下一步")
        Add("Готово",
            "Finish", "Готово", "Fertig",
            "Fine", "Finalizar", "Terminer",
            "Concluir", "إنهاء", "समाप्त",
            "শেষ", "ختم", "完成")

        ' --- the three named parts of every step ------------------------------------------------------
        Add("Что это",
            "What it is", "Що це", "Was es ist",
            "Cos'è", "Qué es", "Ce que c'est",
            "O que é", "ما هو", "यह क्या है",
            "এটি কী", "یہ کیا ہے", "这是什么")
        Add("Зачем",
            "Why it exists", "Навіщо", "Wozu",
            "Perché", "Para qué", "Pourquoi",
            "Para que serve", "لماذا", "यह क्यों है",
            "কেন", "کیوں", "为什么")
        Add("Как это будет работать",
            "How it will work", "Як це працюватиме", "So wird es funktionieren",
            "Come funzionerà", "Cómo funcionará", "Comment cela fonctionnera",
            "Como vai funcionar", "كيف سيعمل", "यह कैसे काम करेगा",
            "এটি কীভাবে কাজ করবে", "یہ کیسے کام کرے گا", "它将如何工作")

        ' --- block A: the folder share server ----------------------------------------------------------
        Add("Что станет этим компьютером",
            "What this computer becomes", "Чим стане цей комп'ютер", "Was dieser Computer wird",
            "Cosa diventa questo computer", "Qué será este equipo", "Ce que deviendra cet ordinateur",
            "O que este computador passa a ser", "ما الذي سيصبح عليه هذا الكمبيوتر", "यह कंप्यूटर क्या बनेगा",
            "এই কম্পিউটার কী হবে", "یہ کمپیوٹر کیا بنے گا", "这台电脑将成为什么")
        Add("SFTP-сервер для выбранных папок: телефон с приложением или любой SFTP-клиент откроет их по сети.",
            "SFTP server for the folders you pick: a phone with the app, or any SFTP client, opens them over the network.", "SFTP-сервер для обраних папок: телефон із застосунком або будь-який SFTP-клієнт відкриє їх мережею.", "SFTP-Server für die gewählten Ordner: ein Telefon mit der App oder ein beliebiger SFTP-Client öffnet sie über das Netzwerk.",
            "Server SFTP per le cartelle scelte: un telefono con l'app o un qualsiasi client SFTP le apre in rete.", "Servidor SFTP para las carpetas que elija: un teléfono con la aplicación o cualquier cliente SFTP las abre por la red.", "Serveur SFTP pour les dossiers que vous choisissez : un téléphone avec l'application, ou tout client SFTP, les ouvre sur le réseau.",
            "Servidor SFTP para as pastas que escolher: um telemóvel com a aplicação, ou qualquer cliente SFTP, abre-as pela rede.", "خادم SFTP للمجلدات التي تختارها: هاتف بالتطبيق أو أي عميل SFTP يفتحها عبر الشبكة.", "आपके चुने फ़ोल्डरों के लिए SFTP सर्वर: ऐप वाला फ़ोन या कोई भी SFTP क्लाइंट इन्हें नेटवर्क पर खोलता है।",
            "আপনার বেছে নেওয়া ফোল্ডারের জন্য SFTP সার্ভার: অ্যাপসহ একটি ফোন বা যেকোনো SFTP ক্লায়েন্ট নেটওয়ার্কের মাধ্যমে এগুলো খোলে।", "آپ کے منتخب فولڈرز کے لیے SFTP سرور: ایپ والا فون یا کوئی بھی SFTP کلائنٹ انہیں نیٹ ورک پر کھولتا ہے۔", "为您选择的文件夹提供 SFTP 服务器：带应用的手机或任何 SFTP 客户端都可通过网络打开它们。")
        Add("Телефон не видит папки на компьютере, пока сервер не включён. Включённый сервер ничего не открывает, пока вы не выбрали папки.",
            "A phone cannot see the folders on this computer until the server is on. A running server opens nothing until you pick the folders.", "Телефон не бачить папок на комп'ютері, доки сервер не увімкнено. Увімкнений сервер нічого не відкриває, доки ви не обрали папки.", "Ein Telefon sieht die Ordner auf diesem Computer erst, wenn der Server eingeschaltet ist. Ein laufender Server gibt nichts frei, bevor Sie Ordner gewählt haben.",
            "Un telefono non vede le cartelle di questo computer finché il server non è acceso. Un server acceso non apre nulla finché non scegli le cartelle.", "Un teléfono no ve las carpetas de este equipo hasta que el servidor está encendido. Un servidor encendido no abre nada hasta que elija las carpetas.", "Un téléphone ne voit pas les dossiers de cet ordinateur tant que le serveur n'est pas activé. Un serveur activé n'ouvre rien tant que vous n'avez pas choisi de dossiers.",
            "Um telemóvel não vê as pastas deste computador enquanto o servidor não estiver ligado. Um servidor ligado não abre nada até escolher as pastas.", "لا يرى الهاتف المجلدات على هذا الكمبيوتر إلا بعد تشغيل الخادم. والخادم المشغل لا يفتح شيئاً قبل أن تختار المجلدات.", "सर्वर चालू होने तक फ़ोन इस कंप्यूटर के फ़ोल्डर नहीं देख सकता। चालू सर्वर तब तक कुछ नहीं खोलता जब तक आप फ़ोल्डर न चुनें।",
            "সার্ভার চালু না হওয়া পর্যন্ত ফোন এই কম্পিউটারের ফোল্ডার দেখতে পায় না। আপনি ফোল্ডার না বাছা পর্যন্ত চালু সার্ভার কিছু খোলে না।", "سرور آن ہونے تک فون اس کمپیوٹر کے فولڈر نہیں دیکھ سکتا۔ چلتا ہوا سرور اس وقت تک کچھ نہیں کھولتا جب تک آپ فولڈر نہ چنیں۔", "在服务器开启之前，手机看不到这台电脑上的文件夹。开启的服务器在您选择文件夹之前不会打开任何内容。")
        Add("Сервер выключен до этого шага. Кнопка «Включить» запросит права администратора один раз и добавит одно правило брандмауэра Windows для этой программы. Телефоны подключаются сканированием QR-кода, который показывает окно программы.",
            "The server is off until this step. The Enable button asks for administrator rights once and adds one Windows Firewall rule for this program. Phones connect by scanning the QR code that the program window shows.", "До цього кроку сервер вимкнено. Кнопка «Увімкнути» один раз запитає права адміністратора і додасть одне правило брандмауера Windows для цієї програми. Телефони підключаються скануванням QR-коду, який показує вікно програми.", "Bis zu diesem Schritt ist der Server aus. Die Schaltfläche »Einschalten« fragt einmal nach Administratorrechten und legt eine Windows-Firewallregel für dieses Programm an. Telefone verbinden sich, indem sie den QR-Code scannen, den das Programmfenster zeigt.",
            "Fino a questo passaggio il server è spento. Il pulsante «Attiva» chiede i privilegi di amministratore una sola volta e aggiunge una regola del Firewall di Windows per questo programma. I telefoni si collegano scansionando il codice QR mostrato dalla finestra del programma.", "El servidor está apagado hasta este paso. El botón «Activar» pide derechos de administrador una vez y añade una regla del Firewall de Windows para este programa. Los teléfonos se conectan escaneando el código QR que muestra la ventana del programa.", "Le serveur est désactivé jusqu'à cette étape. Le bouton « Activer » demande les droits d'administrateur une seule fois et ajoute une règle du Pare-feu Windows pour ce programme. Les téléphones se connectent en scannant le code QR affiché par la fenêtre du programme.",
            "O servidor está desligado até este passo. O botão «Ativar» pede direitos de administrador uma vez e acrescenta uma regra da Firewall do Windows para este programa. Os telemóveis ligam-se lendo o código QR que a janela do programa mostra.", "الخادم متوقف حتى هذه الخطوة. يطلب زر «تشغيل» صلاحيات المسؤول مرة واحدة ويضيف قاعدة واحدة في جدار حماية Windows لهذا البرنامج. تتصل الهواتف بمسح رمز QR الذي تعرضه نافذة البرنامج.", "इस चरण तक सर्वर बंद है। «चालू करें» बटन एक बार व्यवस्थापक अधिकार माँगता है और इस प्रोग्राम के लिए Windows फ़ायरवॉल का एक नियम जोड़ता है। फ़ोन प्रोग्राम विंडो में दिखे QR कोड को स्कैन करके जुड़ते हैं।",
            "এই ধাপ পর্যন্ত সার্ভার বন্ধ থাকে। «চালু করুন» বোতাম একবার প্রশাসক অধিকার চায় এবং এই প্রোগ্রামের জন্য একটি Windows Firewall নিয়ম যোগ করে। প্রোগ্রাম উইন্ডোতে দেখানো QR কোড স্ক্যান করে ফোন সংযুক্ত হয়।", "اس مرحلے تک سرور بند ہے۔ «آن کریں» کا بٹن ایک بار ایڈمنسٹریٹر کی اجازت مانگتا ہے اور اس پروگرام کے لیے Windows فائر وال کا ایک قاعدہ شامل کرتا ہے۔ فون پروگرام ونڈو میں دکھایا گیا QR کوڈ اسکین کر کے جڑتے ہیں۔", "在此步骤之前服务器处于关闭状态。「启用」按钮只会请求一次管理员权限，并为本程序添加一条 Windows 防火墙规则。手机通过扫描程序窗口显示的二维码进行连接。")
        Add("Включить сервер",
            "Turn the server on", "Увімкнути сервер", "Server einschalten",
            "Attiva il server", "Activar el servidor", "Activer le serveur",
            "Ativar o servidor", "تشغيل الخادم", "सर्वर चालू करें",
            "সার্ভার চালু করুন", "سرور آن کریں", "启用服务器")
        Add("Этот шаг можно пропустить: сервер останется выключенным.",
            "You can skip this step: the server stays off.", "Цей крок можна пропустити: сервер залишиться вимкненим.", "Dieser Schritt kann übersprungen werden: Der Server bleibt aus.",
            "Puoi saltare questo passaggio: il server resta spento.", "Puede omitir este paso: el servidor seguirá apagado.", "Vous pouvez passer cette étape : le serveur reste désactivé.",
            "Pode saltar este passo: o servidor continua desligado.", "يمكنك تخطي هذه الخطوة: يبقى الخادم متوقفاً.", "आप यह चरण छोड़ सकते हैं: सर्वर बंद रहेगा।",
            "আপনি এই ধাপ এড়িয়ে যেতে পারেন: সার্ভার বন্ধ থাকবে।", "یہ مرحلہ چھوڑا جا سکتا ہے: سرور بند رہے گا۔", "可以跳过此步骤：服务器将保持关闭。")
        Add("Сервер включён.",
            "Server is on.", "Сервер увімкнено.", "Der Server ist eingeschaltet.",
            "Il server è acceso.", "El servidor está encendido.", "Le serveur est activé.",
            "O servidor está ligado.", "الخادم قيد التشغيل.", "सर्वर चालू है।",
            "সার্ভার চালু আছে।", "سرور آن ہے۔", "服务器已开启。")
        Add("Сервер пока выключен. Это не ошибка: включить его можно позже в настройках.",
            "The server is still off. This is not an error: you can turn it on later in the settings.", "Сервер поки вимкнено. Це не помилка: увімкнути його можна пізніше в налаштуваннях.", "Der Server ist noch aus. Das ist kein Fehler: Sie können ihn später in den Einstellungen einschalten.",
            "Il server è ancora spento. Non è un errore: puoi attivarlo più tardi nelle impostazioni.", "El servidor sigue apagado. No es un error: puede encenderlo más tarde en los ajustes.", "Le serveur est toujours désactivé. Ce n'est pas une erreur : vous pourrez l'activer plus tard dans les réglages.",
            "O servidor continua desligado. Não é um erro: pode ligá-lo mais tarde nas definições.", "الخادم ما زال متوقفاً. هذا ليس خطأ: يمكنك تشغيله لاحقاً من الإعدادات.", "सर्वर अभी बंद है। यह कोई त्रुटि नहीं है: आप इसे बाद में सेटिंग्स में चालू कर सकते हैं।",
            "সার্ভার এখনও বন্ধ। এটি কোনো ত্রুটি নয়: আপনি পরে সেটিংসে চালু করতে পারেন।", "سرور ابھی بند ہے۔ یہ کوئی خرابی نہیں: آپ اسے بعد میں سیٹنگز میں آن کر سکتے ہیں۔", "服务器仍处于关闭状态。这不是错误：您可以稍后在设置中开启。")
        Add("Сервер выключен.",
            "Server is off.", "Сервер вимкнено.", "Der Server ist aus.",
            "Il server è spento.", "El servidor está apagado.", "Le serveur est désactivé.",
            "O servidor está desligado.", "الخادم متوقف.", "सर्वर बंद है।",
            "সার্ভার বন্ধ আছে।", "سرور بند ہے۔", "服务器已关闭。")
        Add("Порт",
            "Port", "Порт", "Port",
            "Porta", "Puerto", "Port",
            "Porta", "المنفذ", "पोर्ट",
            "পোর্ট", "پورٹ", "端口")
        Add("Выбирает система",
            "Chosen by the system", "Обирає система", "Vom System gewählt",
            "Scelta dal sistema", "Elige el sistema", "Choisi par le système",
            "Escolhida pelo sistema", "يختاره النظام", "सिस्टम चुनता है",
            "সিস্টেম বেছে নেয়", "سسٹم منتخب کرے", "由系统选择")
        Add("Фиксированный порт:",
            "Fixed port:", "Фіксований порт:", "Fester Port:",
            "Porta fissa:", "Puerto fijo:", "Port fixe :",
            "Porta fixa:", "منفذ ثابت:", "स्थिर पोर्ट:",
            "স্থির পোর্ট:", "مقررہ پورٹ:", "固定端口：")
        Add("Номер порта, на котором сервер принимает подключения.",
            "The port the server accepts connections on.", "Номер порту, на якому сервер приймає підключення.", "Die Portnummer, auf der der Server Verbindungen annimmt.",
            "Il numero di porta su cui il server accetta le connessioni.", "El número de puerto en el que el servidor acepta conexiones.", "Le numéro de port sur lequel le serveur accepte les connexions.",
            "O número da porta em que o servidor aceita ligações.", "رقم المنفذ الذي يقبل عليه الخادم الاتصالات.", "वह पोर्ट नंबर जिस पर सर्वर कनेक्शन स्वीकार करता है।",
            "যে পোর্টে সার্ভার সংযোগ গ্রহণ করে তার নম্বর।", "وہ پورٹ نمبر جس پر سرور کنکشن قبول کرتا ہے۔", "服务器接受连接的端口号。")
        Add("Номер записан в каждый QR-код и файл .fmscfg. Если его поменять, уже выданные коды перестанут работать.",
            "The number is written into every QR code and .fmscfg file. Changing it stops the codes already handed out from working.", "Номер записано в кожен QR-код і файл .fmscfg. Якщо його змінити, вже видані коди перестануть працювати.", "Die Nummer steht in jedem QR-Code und jeder .fmscfg-Datei. Wenn Sie sie ändern, funktionieren bereits ausgegebene Codes nicht mehr.",
            "Il numero è scritto in ogni codice QR e file .fmscfg. Cambiarlo fa smettere di funzionare i codici già distribuiti.", "El número está escrito en cada código QR y archivo .fmscfg. Si lo cambia, dejarán de funcionar los códigos ya entregados.", "Le numéro est inscrit dans chaque code QR et fichier .fmscfg. Le changer rend inopérants les codes déjà remis.",
            "O número está escrito em cada código QR e ficheiro .fmscfg. Se o alterar, os códigos já entregues deixam de funcionar.", "الرقم مكتوب في كل رمز QR وملف .fmscfg. تغييره يوقف عمل الرموز التي تم توزيعها.", "नंबर हर QR कोड और .fmscfg फ़ाइल में लिखा है। इसे बदलने से पहले दिए गए कोड काम करना बंद कर देंगे।",
            "নম্বরটি প্রতিটি QR কোড ও .fmscfg ফাইলে লেখা থাকে। এটি বদলালে আগে দেওয়া কোডগুলো আর কাজ করবে না।", "یہ نمبر ہر QR کوڈ اور .fmscfg فائل میں لکھا ہوتا ہے۔ اسے بدلنے سے پہلے جاری کیے گئے کوڈ کام کرنا بند کر دیں گے۔", "这个数字写入每个二维码和 .fmscfg 文件。更改后，已发出的二维码将无法继续使用。")
        Add("«Выбирает система» - порт назначается один раз и больше не меняется. «Фиксированный» - вы задаёте номер сами, например для проброса порта на роутере. По умолчанию предлагается {0}.",
            "«Chosen by the system» - the port is assigned once and never changes. «Fixed» - you enter the number yourself, for example to forward a port on the router. The default suggested is {0}.", "«Обирає система» - порт призначається один раз і більше не змінюється. «Фіксований» - номер ви вводите самі, наприклад для прокидання порту на роутері. Типово пропонується {0}.", "»Vom System gewählt« - der Port wird einmal vergeben und ändert sich nie. »Fest« - Sie geben die Nummer selbst ein, etwa für eine Portweiterleitung am Router. Vorgeschlagen wird {0}.",
            "«Scelta dal sistema» - la porta viene assegnata una volta e non cambia mai. «Fissa» - inserisci tu il numero, ad esempio per inoltrare una porta sul router. Il valore suggerito è {0}.", "«Elige el sistema» - el puerto se asigna una vez y nunca cambia. «Fijo» - usted introduce el número, por ejemplo para reenviar un puerto en el router. Se sugiere {0}.", "« Choisi par le système » : le port est attribué une fois et ne change jamais. « Fixe » : vous saisissez le numéro vous-même, par exemple pour rediriger un port sur le routeur. Par défaut, {0} est proposé.",
            "«Escolhida pelo sistema» - a porta é atribuída uma vez e nunca muda. «Fixa» - introduz o número, por exemplo para reencaminhar uma porta no router. Sugere-se {0}.", "«يختاره النظام» - يُعيَّن المنفذ مرة واحدة ولا يتغير أبداً. «ثابت» - تُدخل الرقم بنفسك، مثلاً لتوجيه منفذ على الموجّه. المقترح الافتراضي هو {0}.", "«सिस्टम चुनता है» - पोर्ट एक बार तय होता है और कभी नहीं बदलता। «स्थिर» - संख्या आप खुद लिखते हैं, जैसे राउटर पर पोर्ट फ़ॉरवर्ड करने के लिए। सुझाया गया डिफ़ॉल्ट {0} है।",
            "«সিস্টেম বেছে নেয়» - পোর্ট একবার ঠিক হয় এবং কখনো বদলায় না। «স্থির» - আপনি নিজে নম্বর লেখেন, যেমন রাউটারে পোর্ট ফরওয়ার্ড করতে। প্রস্তাবিত ডিফল্ট {0}।", "«سسٹم منتخب کرے» - پورٹ ایک بار طے ہوتا ہے اور کبھی نہیں بدلتا۔ «مقررہ» - نمبر آپ خود لکھتے ہیں، جیسے راؤٹر پر پورٹ فارورڈ کرنے کے لیے۔ تجویز کردہ ڈیفالٹ {0} ہے۔", "«由系统选择» - 端口只分配一次，之后不再更改。«固定» - 由您自己输入号码，例如用于在路由器上转发端口。建议的默认值为 {0}。")
        Add("Правило брандмауэра относится к программе, а не к порту, поэтому смена порта его не требует.",
            "The firewall rule applies to the program, not to a port, so changing the port does not need it.", "Правило брандмауера стосується програми, а не порту, тож зміна порту його не потребує.", "Die Firewallregel gilt für das Programm, nicht für einen Port; deshalb ist bei einer Portänderung keine neue Regel nötig.",
            "La regola del firewall riguarda il programma, non una porta, quindi cambiare porta non la richiede.", "La regla del firewall se aplica al programa, no a un puerto, así que cambiar el puerto no la requiere.", "La règle du pare-feu concerne le programme, et non un port : changer de port ne la rend pas nécessaire.",
            "A regra da firewall aplica-se ao programa, não a uma porta, por isso mudar a porta não a exige.", "قاعدة جدار الحماية تخص البرنامج لا المنفذ، لذلك لا يتطلب تغيير المنفذ أي تغيير فيها.", "फ़ायरवॉल नियम प्रोग्राम के लिए है, किसी पोर्ट के लिए नहीं; इसलिए पोर्ट बदलने पर इसकी ज़रूरत नहीं।",
            "ফায়ারওয়াল নিয়মটি প্রোগ্রামের জন্য, কোনো পোর্টের জন্য নয়; তাই পোর্ট বদলালে এটি লাগে না।", "فائر وال کا قاعدہ پروگرام کے لیے ہے، پورٹ کے لیے نہیں، اس لیے پورٹ بدلنے سے اس کی ضرورت نہیں۔", "防火墙规则针对的是程序而非端口，因此更改端口不需要重新设置。")
        Add("Здесь вы сделали то, что нужно для первого подключения. Всё остальное живёт в настройках программы: порт, папки, подключённые устройства.",
            "You have done what the first connection needs. Everything else lives in the program's settings: port, folders, connected devices.", "Ви зробили те, що потрібно для першого підключення. Решта живе в налаштуваннях програми: порт, папки, підключені пристрої.", "Sie haben getan, was für die erste Verbindung nötig ist. Alles Weitere steht in den Einstellungen des Programms: Port, Ordner, verbundene Geräte.",
            "Hai fatto ciò che serve per il primo collegamento. Tutto il resto è nelle impostazioni del programma: porta, cartelle, dispositivi collegati.", "Ha hecho lo necesario para la primera conexión. Lo demás está en los ajustes del programa: puerto, carpetas, dispositivos conectados.", "Vous avez fait ce qu'il fallait pour la première connexion. Le reste se trouve dans les réglages du programme : port, dossiers, appareils connectés.",
            "Fez o necessário para a primeira ligação. O resto está nas definições do programa: porta, pastas, dispositivos ligados.", "لقد أنجزت ما يلزم للاتصال الأول. كل ما عدا ذلك موجود في إعدادات البرنامج: المنفذ والمجلدات والأجهزة المتصلة.", "पहले कनेक्शन के लिए ज़रूरी काम आप कर चुके हैं। बाकी सब प्रोग्राम की सेटिंग्स में है: पोर्ट, फ़ोल्डर, जुड़े उपकरण।",
            "প্রথম সংযোগের জন্য যা দরকার তা আপনি করেছেন। বাকি সবকিছু প্রোগ্রামের সেটিংসে আছে: পোর্ট, ফোল্ডার, সংযুক্ত ডিভাইস।", "پہلے کنکشن کے لیے جو ضروری تھا وہ آپ کر چکے ہیں۔ باقی سب پروگرام کی سیٹنگز میں ہے: پورٹ، فولڈرز، جڑے ہوئے آلات۔", "首次连接所需的步骤已完成。其余内容都在程序的设置中：端口、文件夹、已连接的设备。")
        Add("Выключить сервер, сменить порт или открыть папки можно в любой момент - в группах настроек.",
            "You can turn the server off, change the port or open folders at any time, in the settings groups.", "Вимкнути сервер, змінити порт або відкрити папки можна будь-коли - у групах налаштувань.", "Sie können den Server jederzeit ausschalten, den Port ändern oder Ordner freigeben - in den Einstellungsgruppen.",
            "Puoi spegnere il server, cambiare la porta o aprire cartelle in qualsiasi momento, nei gruppi delle impostazioni.", "Puede apagar el servidor, cambiar el puerto o abrir carpetas en cualquier momento, en los grupos de ajustes.", "Vous pouvez à tout moment désactiver le serveur, changer le port ou ouvrir des dossiers, dans les groupes de réglages.",
            "Pode desligar o servidor, mudar a porta ou abrir pastas a qualquer momento, nos grupos de definições.", "يمكنك في أي وقت إيقاف الخادم أو تغيير المنفذ أو فتح المجلدات، من مجموعات الإعدادات.", "आप सर्वर बंद कर सकते हैं, पोर्ट बदल सकते हैं या फ़ोल्डर खोल सकते हैं - किसी भी समय, सेटिंग्स के समूहों में।",
            "আপনি যেকোনো সময় সার্ভার বন্ধ করতে, পোর্ট বদলাতে বা ফোল্ডার খুলতে পারেন - সেটিংসের গ্রুপে।", "آپ کسی بھی وقت سرور بند کر سکتے ہیں، پورٹ بدل سکتے ہیں یا فولڈرز کھول سکتے ہیں - سیٹنگز کے گروپس میں۔", "您可以随时关闭服务器、更改端口或开放文件夹，均在设置分组中进行。")
        Add("Для подключения телефона дальше: отсканируйте QR-код в окне программы, затем опубликуйте первую папку.",
            "To connect a phone next: scan the QR code in the program window, then publish the first folder.", "Щоб підключити телефон далі: відскануйте QR-код у вікні програми, потім опублікуйте першу папку.", "Um ein Telefon zu verbinden: Scannen Sie als Nächstes den QR-Code im Programmfenster und geben Sie dann den ersten Ordner frei.",
            "Per collegare un telefono: inquadra il codice QR nella finestra del programma, poi pubblica la prima cartella.", "Para conectar un teléfono: escanee el código QR de la ventana del programa y después publique la primera carpeta.", "Pour connecter un téléphone : scannez le code QR dans la fenêtre du programme, puis publiez le premier dossier.",
            "Para ligar um telemóvel: leia o código QR na janela do programa e depois publique a primeira pasta.", "لربط الهاتف: امسح رمز QR في نافذة البرنامج، ثم انشر أول مجلد.", "फ़ोन जोड़ने के लिए: प्रोग्राम विंडो में QR कोड स्कैन करें, फिर पहला फ़ोल्डर साझा करें।",
            "ফোন সংযুক্ত করতে: প্রোগ্রাম উইন্ডোর QR কোড স্ক্যান করুন, তারপর প্রথম ফোল্ডার প্রকাশ করুন।", "فون جوڑنے کے لیے: پروگرام ونڈو میں QR کوڈ اسکین کریں، پھر پہلا فولڈر شائع کریں۔", "要连接手机：扫描程序窗口中的二维码，然后发布第一个文件夹。")

        ' --- block B: the Media Exchange Server (the gate decides whether it is shown) ---------------
        Add("Учётные записи, устройства и опубликованные ресурсы: вход по логину и паролю или по коду сопряжения.",
            "Accounts, devices and published resources: sign-in with a login and password or with a pairing code.", "Облікові записи, пристрої та опубліковані ресурси: вхід за логіном і паролем або за кодом сполучення.", "Konten, Geräte und veröffentlichte Ressourcen: Anmeldung mit Benutzername und Passwort oder mit einem Kopplungscode.",
            "Account, dispositivi e risorse pubblicate: accesso con login e password oppure con un codice di abbinamento.", "Cuentas, dispositivos y recursos publicados: inicio de sesión con usuario y contraseña o con un código de emparejamento.", "Comptes, appareils et ressources publiées : connexion par identifiant et mot de passe, ou par code d'appairage.",
            "Contas, dispositivos e recursos publicados: início de sessão com login e palavra-passe ou com um código de emparelhamento.", "الحسابات والأجهزة والموارد المنشورة: تسجيل الدخول بالاسم وكلمة المرور أو برمز الإقران.", "खाते, उपकरण और प्रकाशित संसाधन: लॉगिन और पासवर्ड से या पेयरिंग कोड से साइन-इन।",
            "অ্যাকাউন্ট, ডিভাইস ও প্রকাশিত সংস্থান: লগইন ও পাসওয়ার্ড বা পেয়ারিং কোড দিয়ে সাইন-ইন।", "اکاؤنٹس، آلات اور شائع شدہ وسائل: لاگ اِن اور پاس ورڈ یا پیئرنگ کوڈ کے ذریعے سائن اِن۔", "账户、设备和已发布的资源：使用登录名和密码或配对码登录。")
        Add("Устройство доверяет серверу только после того, как проверит отпечаток его сертификата при первом подключении.",
            "A device trusts the server only after it checks the server's certificate fingerprint on the first connection.", "Пристрій довіряє серверу лише після того, як перевірить відбиток його сертифіката під час першого підключення.", "Ein Gerät vertraut dem Server erst, nachdem es beim ersten Verbinden den Fingerabdruck des Zertifikats geprüft hat.",
            "Un dispositivo si fida del server solo dopo aver verificato l'impronta del certificato al primo collegamento.", "Un dispositivo solo confía en el servidor después de comprobar la huella del certificado en la primera conexión.", "Un appareil ne fait confiance au serveur qu'après avoir vérifié l'empreinte du certificat lors de la première connexion.",
            "Um dispositivo só confia no servidor depois de verificar a impressão digital do certificado na primeira ligação.", "لا يثق الجهاز بالخادم إلا بعد أن يتحقق من بصمة شهادته عند أول اتصال.", "कोई उपकरण सर्वर पर तभी भरोसा करता है जब पहले कनेक्शन पर वह सर्वर के सर्टिफ़िकेट की फ़िंगरप्रिंट जाँच लेता है।",
            "কোনো ডিভাইস প্রথম সংযোগে সার্ভারের সার্টিফিকেটের ফিঙ্গারপ্রিন্ট যাচাই করার পরেই সার্ভারকে বিশ্বাস করে।", "کوئی آلہ سرور پر اس وقت تک بھروسہ نہیں کرتا جب تک پہلے کنکشن پر وہ سرور کے سرٹیفکیٹ کا فنگر پرنٹ جانچ نہ لے۔", "设备只有在首次连接时核对了服务器证书指纹之后，才会信任该服务器。")
        Add("Ниже - заявление оператора в полном виде. Его нельзя пропустить: оно описывает, кто и что видит на сервере.",
            "Below is the operator's statement in full. It cannot be skipped: it describes who sees what on the server.", "Нижче - заява оператора повністю. Її не можна пропустити: вона описує, хто і що бачить на сервері.", "Unten steht die Erklärung des Betreibers vollständig. Sie kann nicht übersprungen werden: Sie beschreibt, wer auf dem Server was sieht.",
            "Di seguito la dichiarazione dell'operatore per intero. Non si può saltare: descrive chi vede cosa sul server.", "A continuación, la declaración del operador completa. No se puede omitir: describe quién ve qué en el servidor.", "Ci-dessous, la déclaration complète de l'opérateur. Elle ne peut pas être ignorée : elle décrit qui voit quoi sur le serveur.",
            "Em baixo, a declaração completa do operador. Não pode ser saltada: descreve quem vê o quê no servidor.", "فيما يلي بيان المشغّل كاملاً. لا يمكن تخطّيه: فهو يصف من يرى ماذا على الخادم.", "नीचे ऑपरेटर का पूरा वक्तव्य है। इसे छोड़ा नहीं जा सकता: यह बताता है कि सर्वर पर कौन क्या देखता है।",
            "নীচে অপারেটরের বিবৃতি পুরোটা আছে। এটি এড়ানো যায় না: এতে বলা আছে সার্ভারে কে কী দেখে।", "نیچے آپریٹر کا مکمل بیان ہے۔ اسے چھوڑا نہیں جا سکتا: اس میں بتایا گیا ہے کہ سرور پر کون کیا دیکھتا ہے۔", "以下是完整的运营方声明。此项不可跳过：它说明服务器上谁能看到什么。")
        Add("Заявление оператора",
            "Operator statement", "Заява оператора", "Erklärung des Betreibers",
            "Dichiarazione dell'operatore", "Declaración del operador", "Déclaration de l'opérateur",
            "Declaração do operador", "بيان المشغّل", "ऑपरेटर का वक्तव्य",
            "অপারেটরের বিবৃতি", "آپریٹر کا بیان", "运营方声明")
        Add("Учётная запись, с которой войдёт первое устройство.",
            "The account the first device signs in with.", "Обліковий запис, з яким увійде перший пристрій.", "Das Konto, mit dem sich das erste Gerät anmeldet.",
            "L'account con cui accederà il primo dispositivo.", "La cuenta con la que iniciará sesión el primer dispositivo.", "Le compte avec lequel le premier appareil se connectera.",
            "A conta com que o primeiro dispositivo vai iniciar sessão.", "الحساب الذي سيسجّل به الجهاز الأول الدخول.", "वह खाता जिससे पहला उपकरण साइन-इन करेगा।",
            "যে অ্যাকাউন্ট দিয়ে প্রথম ডিভাইস সাইন-ইন করবে।", "وہ اکاؤنٹ جس سے پہلا آلہ سائن اِن کرے گا۔", "第一台设备登录所用的账户。")
        Add("Без неё сервер ничего не принимает, а пароль показывается один раз.",
            "Without it the server accepts nothing, and the password is shown only once.", "Без нього сервер нічого не приймає, а пароль показується один раз.", "Ohne Konto nimmt der Server nichts an, und das Passwort wird nur einmal angezeigt.",
            "Senza di essa il server non accetta nulla, e la password viene mostrata una sola volta.", "Sin ella el servidor no acepta nada, y la contraseña se muestra una sola vez.", "Sans lui, le serveur n'accepte rien, et le mot de passe n'est affiché qu'une seule fois.",
            "Sem ela o servidor não aceita nada, e a palavra-passe é mostrada apenas uma vez.", "بدونه لا يقبل الخادم شيئاً، وتُعرض كلمة المرور مرة واحدة فقط.", "इसके बिना सर्वर कुछ स्वीकार नहीं करता, और पासवर्ड केवल एक बार दिखाया जाता है।",
            "এটি ছাড়া সার্ভার কিছুই গ্রহণ করে না, আর পাসওয়ার্ড শুধু একবার দেখানো হয়।", "اس کے بغیر سرور کچھ قبول نہیں کرتا، اور پاس ورڈ صرف ایک بار دکھایا جاتا ہے۔", "没有它，服务器不接受任何连接，而且密码只显示一次。")
        Add("Откроется мастер: он создаст учётную запись, запустит сервер, покажет отпечаток сертификата и, если нужно, подключит агент этого компьютера. Пароль показывается один раз; «Скопировать» кладёт его в буфер обмена, который очищается сам.",
            "The wizard opens: it creates the account, starts the server, shows the certificate fingerprint and, if you want, connects this computer's agent. The password is shown once; Copy puts it on the clipboard, which clears itself.", "Відкриється майстер: він створить обліковий запис, запустить сервер, покаже відбиток сертифіката і, за потреби, підключить агент цього комп'ютера. Пароль показується один раз; «Копіювати» кладе його в буфер обміну, який очищується сам.", "Der Assistent öffnet sich: Er legt das Konto an, startet den Server, zeigt den Zertifikat-Fingerabdruck und verbindet bei Bedarf den Agenten dieses Computers. Das Passwort wird einmal angezeigt; »Kopieren« legt es in die Zwischenablage, die sich selbst leert.",
            "Si apre la procedura guidata: crea l'account, avvia il server, mostra l'impronta del certificato e, se vuoi, collega l'agente di questo computer. La password viene mostrata una volta; «Copia» la mette negli appunti, che si svuotano da soli.", "Se abre el asistente: crea la cuenta, inicia el servidor, muestra la huella del certificado y, si lo desea, conecta el agente de este equipo. La contraseña se muestra una vez; «Copiar» la pone en el portapapeles, que se borra solo.", "L'assistant s'ouvre : il crée le compte, démarre le serveur, affiche l'empreinte du certificat et, si vous le souhaitez, connecte l'agent de cet ordinateur. Le mot de passe est affiché une seule fois ; « Copier » le place dans le presse-papiers, qui se vide automatiquement.",
            "Abre-se o assistente: cria a conta, inicia o servidor, mostra a impressão digital do certificado e, se quiser, liga o agente deste computador. A palavra-passe é mostrada uma vez; «Copiar» põe-na na área de transferência, que se limpa sozinha.", "سيُفتح المعالج: ينشئ الحساب، ويشغّل الخادم، ويعرض بصمة الشهادة، وإن أردت يربط وكيل هذا الكمبيوتر. تُعرض كلمة المرور مرة واحدة؛ يضعها «نسخ» في الحافظة التي تُفرَّغ تلقائياً.", "विज़ार्ड खुलेगा: यह खाता बनाएगा, सर्वर शुरू करेगा, सर्टिफ़िकेट की फ़िंगरप्रिंट दिखाएगा और चाहें तो इस कंप्यूटर का एजेंट जोड़ेगा। पासवर्ड एक बार दिखेगा; «कॉपी» उसे क्लिपबोर्ड में रखता है, जो अपने-आप साफ़ हो जाता है।",
            "উইজার্ড খুলবে: এটি অ্যাকাউন্ট তৈরি করবে, সার্ভার চালু করবে, সার্টিফিকেটের ফিঙ্গারপ্রিন্ট দেখাবে এবং চাইলে এই কম্পিউটারের এজেন্ট যুক্ত করবে। পাসওয়ার্ড একবার দেখানো হবে; «কপি» তা ক্লিপবোর্ডে রাখে, যা নিজে থেকে সাফ হয়।", "وزرڈ کھلے گا: یہ اکاؤنٹ بنائے گا، سرور شروع کرے گا، سرٹیفکیٹ کا فنگر پرنٹ دکھائے گا اور اگر چاہیں تو اس کمپیوٹر کا ایجنٹ جوڑے گا۔ پاس ورڈ ایک بار دکھایا جائے گا؛ «کاپی» اسے کلپ بورڈ میں رکھتا ہے جو خود صاف ہو جاتا ہے۔", "将打开向导：它会创建账户、启动服务器、显示证书指纹，并在您需要时连接本电脑的代理。密码只显示一次；「复制」会把它放入剪贴板，剪贴板会自动清空。")
        Add("Открыть мастер",
            "Open the wizard", "Відкрити майстер", "Assistent öffnen",
            "Apri la procedura guidata", "Abrir el asistente", "Ouvrir l'assistant",
            "Abrir o assistente", "فتح المعالج", "विज़ार्ड खोलें",
            "উইজার্ড খুলুন", "وزرڈ کھولیں", "打开向导")
        Add("Адрес для этого компьютера и для других устройств, логин и отпечаток сертификата записаны в группе «{0}».",
            "The address for this computer and for other devices, the login and the certificate fingerprint are recorded in the group «{0}».", "Адреса для цього комп'ютера та інших пристроїв, логін і відбиток сертифіката записані в групі «{0}».", "Adresse für diesen Computer und für andere Geräte, Anmeldename und Zertifikat-Fingerabdruck stehen in der Gruppe »{0}«.",
            "Indirizzo per questo computer e per gli altri dispositivi, login e impronta del certificato sono registrati nel gruppo «{0}».", "La dirección para este equipo y para otros dispositivos, el usuario y la huella del certificado figuran en el grupo «{0}».", "L'adresse de cet ordinateur et celle des autres appareils, l'identifiant et l'empreinte du certificat figurent dans le groupe «{0}».",
            "O endereço deste computador e dos outros dispositivos, o login e a impressão digital do certificado estão registados no grupo «{0}».", "العنوان لهذا الكمبيوتر وللأجهزة الأخرى، واسم الدخول وبصمة الشهادة، مسجّلة في المجموعة «{0}».", "इस कंप्यूटर और दूसरे उपकरणों का पता, लॉगिन और सर्टिफ़िकेट की फ़िंगरप्रिंट «{0}» समूह में दर्ज हैं।",
            "এই কম্পিউটার ও অন্য ডিভাইসের ঠিকানা, লগইন এবং সার্টিফিকেটের ফিঙ্গারপ্রিন্ট «{0}» গ্রুপে লেখা আছে।", "اس کمپیوٹر اور دوسرے آلات کا پتہ، لاگ اِن اور سرٹیفکیٹ کا فنگر پرنٹ «{0}» گروپ میں درج ہے۔", "本机及其他设备的地址、登录名和证书指纹记录在「{0}」分组中。")
        Add("Пароль больше нигде не показывается. Устройство подключается, пройдя проверку отпечатка при первом подключении.",
            "The password is not shown anywhere else. A device connects after passing the fingerprint check on its first connection.", "Пароль більше ніде не показується. Пристрій підключається, пройшовши перевірку відбитка під час першого підключення.", "Das Passwort wird nirgends mehr angezeigt. Ein Gerät verbindet sich, nachdem es beim ersten Verbinden die Fingerabdruckprüfung bestanden hat.",
            "La password non viene più mostrata da nessuna parte. Un dispositivo si collega dopo aver superato il controllo dell'impronta al primo collegamento.", "La contraseña ya no se muestra en ningún sitio. Un dispositivo se conecta tras superar la comprobación de la huella en su primera conexión.", "Le mot de passe n'est plus affiché nulle part. Un appareil se connecte après la vérification de l'empreinte lors de sa première connexion.",
            "A palavra-passe já não é mostrada em lado nenhum. Um dispositivo liga-se depois de passar a verificação da impressão digital na primeira ligação.", "لا تُعرض كلمة المرور في أي مكان آخر. يتصل الجهاز بعد اجتياز التحقق من البصمة عند أول اتصال.", "पासवर्ड अब कहीं और नहीं दिखाया जाता। पहले कनेक्शन पर फ़िंगरप्रिंट जाँच पास करने के बाद ही उपकरण जुड़ता है।",
            "পাসওয়ার্ড আর অন্য কোথাও দেখানো হয় না। প্রথম সংযোগে ফিঙ্গারপ্রিন্ট যাচাই পার হলে ডিভাইস সংযুক্ত হয়।", "پاس ورڈ اب کہیں اور نہیں دکھایا جاتا۔ پہلے کنکشن پر فنگر پرنٹ کی جانچ پاس کرنے کے بعد آلہ جڑتا ہے۔", "密码不会再在其他地方显示。设备在首次连接时通过指纹核对后才会连接。")
        Add("Для Android-приложения следующий шаг - QR-код в окне программы. Остальные параметры - в группах настроек.",
            "For the Android app, the next step is the QR code in the program window. Other settings are in the settings groups.", "Для Android-застосунку наступний крок - QR-код у вікні програми. Решта параметрів - у групах налаштувань.", "Für die Android-App ist der nächste Schritt der QR-Code im Programmfenster. Die übrigen Einstellungen stehen in den Einstellungsgruppen.",
            "Per l'app Android, il passo successivo è il codice QR nella finestra del programma. Gli altri parametri sono nei gruppi delle impostazioni.", "Para la aplicación de Android, el siguiente paso es el código QR de la ventana del programa. Los demás ajustes están en los grupos de ajustes.", "Pour l'application Android, l'étape suivante est le code QR dans la fenêtre du programme. Les autres réglages se trouvent dans les groupes de réglages.",
            "Para a aplicação Android, o passo seguinte é o código QR na janela do programa. As outras definições estão nos grupos de definições.", "بالنسبة لتطبيق Android، الخطوة التالية هي رمز QR في نافذة البرنامج. بقية الإعدادات في مجموعات الإعدادات.", "Android ऐप के लिए अगला चरण प्रोग्राम विंडो में QR कोड है। बाकी सेटिंग्स सेटिंग्स के समूहों में हैं।",
            "Android অ্যাপের জন্য পরের ধাপ প্রোগ্রাম উইন্ডোর QR কোড। বাকি সেটিংস সেটিংসের গ্রুপে আছে।", "Android ایپ کے لیے اگلا مرحلہ پروگرام ونڈو میں QR کوڈ ہے۔ باقی سیٹنگز سیٹنگز کے گروپس میں ہیں۔", "对于 Android 应用，下一步是程序窗口中的二维码。其余设置位于设置分组中。")

        ' --- the step titles and the closing words ---------------------------------------------------------
        Add("Порт",
            "Port", "Порт", "Port",
            "Porta", "Puerto", "Port",
            "Porta", "المنفذ", "पोर्ट",
            "পোর্ট", "پورٹ", "端口")
        Add("Сервер настроен",
            "Server is set up", "Сервер налаштовано", "Server eingerichtet",
            "Server configurato", "Servidor configurado", "Serveur configuré",
            "Servidor configurado", "تم إعداد الخادم", "सर्वर सेट हो गया",
            "সার্ভার সেট আপ হয়েছে", "سرور سیٹ اپ ہو گیا", "服务器已配置")
        Add("Первая учётная запись",
            "First account", "Перший обліковий запис", "Erstes Konto",
            "Primo account", "Primera cuenta", "Premier compte",
            "Primeira conta", "الحساب الأول", "पहला खाता",
            "প্রথম অ্যাকাউন্ট", "پہلا اکاؤنٹ", "第一个账户")
        Add("Сервер готов",
            "Server is ready", "Сервер готовий", "Server bereit",
            "Server pronto", "Servidor listo", "Serveur prêt",
            "Servidor pronto", "الخادم جاهز", "सर्वर तैयार है",
            "সার্ভার প্রস্তুত", "سرور تیار ہے", "服务器已就绪")
        Add("Учётная запись создана.",
            "Account created.", "Обліковий запис створено.", "Konto angelegt.",
            "Account creato.", "Cuenta creada.", "Compte créé.",
            "Conta criada.", "تم إنشاء الحساب.", "खाता बन गया।",
            "অ্যাকাউন্ট তৈরি হয়েছে।", "اکاؤنٹ بن گیا۔", "账户已创建。")
        Add("Учётная запись ещё не создана. Этот шаг обязательный: откройте мастер снова.",
            "The account is not created yet. This step is required: open the wizard again.", "Обліковий запис ще не створено. Цей крок обов'язковий: відкрийте майстер ще раз.", "Das Konto ist noch nicht angelegt. Dieser Schritt ist Pflicht: Öffnen Sie den Assistenten erneut.",
            "L'account non è ancora creato. Questo passaggio è obbligatorio: apri di nuovo la procedura guidata.", "La cuenta todavía no está creada. Este paso es obligatorio: vuelva a abrir el asistente.", "Le compte n'est pas encore créé. Cette étape est obligatoire : rouvrez l'assistant.",
            "A conta ainda não foi criada. Este passo é obrigatório: abra o assistente novamente.", "لم يُنشأ الحساب بعد. هذه الخطوة إلزامية: أعد فتح المعالج.", "खाता अभी नहीं बना है। यह चरण अनिवार्य है: विज़ार्ड फिर से खोलें।",
            "অ্যাকাউন্ট এখনও তৈরি হয়নি। এই ধাপ আবশ্যক: উইজার্ড আবার খুলুন।", "اکاؤنٹ ابھی نہیں بنا۔ یہ مرحلہ لازمی ہے: وزرڈ دوبارہ کھولیں۔", "账户尚未创建。此步骤为必需：请重新打开向导。")
    End Sub

End Class
