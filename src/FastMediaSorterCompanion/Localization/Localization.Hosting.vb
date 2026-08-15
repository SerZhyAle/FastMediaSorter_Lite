Option Strict On

' <summary>
' The Hosting surface: the User edition (this program hosts the worker) vs the
' Server edition (the Windows SCM does), and the administrative controls that go
' with it. See SPECIFICATION_SHARE_SYSTEM_SERVICE.md.
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddHostingStrings()

        Add("Хостинг: служба Windows",
            "Hosting: Windows service", "Хостинг: служба Windows", "Hosting: Windows-Dienst",
            "Hosting: servizio Windows", "Alojamiento: servicio de Windows",
            "Hébergement : service Windows", "Alojamento: serviço do Windows",
            "الاستضافة: خدمة Windows", "होस्टिंग: विंडोज़ सेवा", "হোস্টিং: উইন্ডোজ সার্ভিস",
            "ہوسٹنگ: ونڈوز سروس", "托管方式：Windows 服务")

        Add("Хостинг: раздаёт эта программа",
            "Hosting: this program", "Хостинг: роздає ця програма", "Hosting: dieses Programm",
            "Hosting: questo programma", "Alojamiento: este programa",
            "Hébergement : ce programme", "Alojamento: este programa",
            "الاستضافة: هذا البرنامج", "होस्टिंग: यही प्रोग्राम", "হোস্টিং: এই প্রোগ্রাম",
            "ہوسٹنگ: یہی پروگرام", "托管方式：本程序")

        Add("Управление хостингом..",
            "Manage hosting..", "Керування хостингом..", "Hosting verwalten..",
            "Gestisci hosting..", "Gestionar alojamiento..", "Gérer l'hébergement..",
            "Gerir alojamento..", "إدارة الاستضافة..", "होस्टिंग प्रबंधित करें..",
            "হোস্টিং পরিচালনা..", "ہوسٹنگ کا انتظام..", "管理托管方式..")

        Add("Хостинг общего доступа",
            "Folder-share hosting", "Хостинг спільного доступу", "Hosting der Ordnerfreigabe",
            "Hosting della condivisione cartelle", "Alojamiento del uso compartido de carpetas",
            "Hébergement du partage de dossiers", "Alojamento da partilha de pastas",
            "استضافة مشاركة المجلدات", "फ़ोल्डर साझाकरण की होस्टिंग", "ফোল্ডার শেয়ারিং হোস্টিং",
            "فولڈر شیئرنگ ہوسٹنگ", "文件夹共享的托管")

        Add("Папки раздаёт служба Windows. Она стартует вместе с системой и работает без входа пользователя. Это окно - только пульт управления: его можно закрыть, раздача продолжится.",
            "A Windows service is serving the folders. It starts with Windows and runs with nobody signed in. This window is only a console - closing it does not stop the sharing.",
            "Папки роздає служба Windows. Вона стартує разом із системою та працює без входу користувача. Це вікно - лише пульт керування: його можна закрити, роздача триватиме.",
            "Ein Windows-Dienst gibt die Ordner frei. Er startet mit Windows und läuft, ohne dass jemand angemeldet ist. Dieses Fenster ist nur eine Konsole - das Schließen beendet die Freigabe nicht.",
            "Un servizio di Windows sta condividendo le cartelle. Si avvia con Windows e funziona senza alcun utente connesso. Questa finestra è solo una console: chiuderla non interrompe la condivisione.",
            "Un servicio de Windows está compartiendo las carpetas. Se inicia con Windows y funciona sin que nadie haya iniciado sesión. Esta ventana es solo una consola: cerrarla no detiene el uso compartido.",
            "Un service Windows partage les dossiers. Il démarre avec Windows et fonctionne sans session ouverte. Cette fenêtre n'est qu'une console : la fermer n'arrête pas le partage.",
            "Um serviço do Windows está a partilhar as pastas. Arranca com o Windows e funciona sem ninguém com sessão iniciada. Esta janela é apenas uma consola: fechá-la não pára a partilha.",
            "تقوم خدمة Windows بمشاركة المجلدات. تبدأ مع Windows وتعمل دون تسجيل دخول أي مستخدم. هذه النافذة مجرد لوحة تحكم: إغلاقها لا يوقف المشاركة.",
            "एक विंडोज़ सेवा फ़ोल्डर साझा कर रही है। यह विंडोज़ के साथ शुरू होती है और बिना किसी के साइन इन किए चलती है। यह विंडो केवल एक कंसोल है - इसे बंद करने से साझाकरण नहीं रुकता।",
            "একটি উইন্ডোজ সার্ভিস ফোল্ডারগুলি শেয়ার করছে। এটি উইন্ডোজের সঙ্গে চালু হয় এবং কেউ সাইন ইন না করলেও চলে। এই উইন্ডোটি কেবল একটি কনসোল - এটি বন্ধ করলে শেয়ারিং থামে না।",
            "ایک ونڈوز سروس فولڈرز شیئر کر رہی ہے۔ یہ ونڈوز کے ساتھ شروع ہوتی ہے اور کسی کے سائن اِن کیے بغیر چلتی ہے۔ یہ ونڈو صرف ایک کنسول ہے - اسے بند کرنے سے شیئرنگ نہیں رکتی۔",
            "由 Windows 服务共享这些文件夹。它随 Windows 启动，无人登录时也照常运行。此窗口只是控制台，关闭它不会停止共享。")

        Add("Папки раздаёт эта программа. Раздача работает, пока вы вошли в систему и менеджер запущен. Серверная редакция ставит службу Windows, которая раздаёт папки с загрузки - даже когда в систему никто не вошёл.",
            "This program is serving the folders. Sharing works while you are signed in and the manager is running. The Server edition installs a Windows service that serves the folders from boot - even with nobody signed in.",
            "Папки роздає ця програма. Роздача працює, доки ви увійшли в систему та менеджер запущено. Серверна редакція встановлює службу Windows, яка роздає папки із завантаження - навіть коли ніхто не увійшов.",
            "Dieses Programm gibt die Ordner frei. Die Freigabe läuft, solange Sie angemeldet sind und der Manager läuft. Die Server-Edition installiert einen Windows-Dienst, der die Ordner ab dem Start freigibt - auch ohne angemeldeten Benutzer.",
            "Questo programma sta condividendo le cartelle. La condivisione funziona finché sei connesso e il manager è in esecuzione. L'edizione Server installa un servizio di Windows che condivide le cartelle fin dall'avvio, anche senza utenti connessi.",
            "Este programa está compartiendo las carpetas. El uso compartido funciona mientras tengas la sesión iniciada y el gestor esté en ejecución. La edición Servidor instala un servicio de Windows que comparte las carpetas desde el arranque, incluso sin nadie conectado.",
            "Ce programme partage les dossiers. Le partage fonctionne tant que vous êtes connecté et que le gestionnaire est lancé. L'édition Serveur installe un service Windows qui partage les dossiers dès le démarrage, même sans session ouverte.",
            "Este programa está a partilhar as pastas. A partilha funciona enquanto tiver sessão iniciada e o gestor estiver a correr. A edição Servidor instala um serviço do Windows que partilha as pastas desde o arranque, mesmo sem ninguém com sessão iniciada.",
            "هذا البرنامج يشارك المجلدات. تعمل المشاركة ما دمت مسجّل الدخول والمدير قيد التشغيل. تُثبّت إصدارة الخادم خدمة Windows تشارك المجلدات منذ الإقلاع، حتى دون تسجيل دخول أحد.",
            "यह प्रोग्राम फ़ोल्डर साझा कर रहा है। जब तक आप साइन इन हैं और मैनेजर चल रहा है, तब तक साझाकरण चलता है। सर्वर संस्करण एक विंडोज़ सेवा स्थापित करता है जो बूट से ही फ़ोल्डर साझा करती है - भले ही कोई साइन इन न हो।",
            "এই প্রোগ্রামটি ফোল্ডারগুলি শেয়ার করছে। আপনি সাইন ইন থাকা ও ম্যানেজার চালু থাকা পর্যন্ত শেয়ারিং চলে। সার্ভার সংস্করণ একটি উইন্ডোজ সার্ভিস ইনস্টল করে যা বুট থেকেই ফোল্ডার শেয়ার করে - কেউ সাইন ইন না করলেও।",
            "یہ پروگرام فولڈرز شیئر کر رہا ہے۔ جب تک آپ سائن اِن ہیں اور مینیجر چل رہا ہے، شیئرنگ کام کرتی ہے۔ سرور ایڈیشن ایک ونڈوز سروس نصب کرتا ہے جو بوٹ سے ہی فولڈرز شیئر کرتی ہے - چاہے کوئی سائن اِن نہ ہو۔",
            "当前由本程序共享文件夹。只有在您已登录且管理器在运行时共享才有效。服务器版会安装一个 Windows 服务，从开机起即可共享文件夹 - 即使无人登录。")

        Add("Служба работает",
            "The service is running", "Служба працює", "Der Dienst läuft",
            "Il servizio è in esecuzione", "El servicio está en ejecución",
            "Le service fonctionne", "O serviço está a correr",
            "الخدمة قيد التشغيل", "सेवा चल रही है", "সার্ভিস চলছে", "سروس چل رہی ہے", "服务正在运行")

        Add("Служба установлена, но остановлена",
            "The service is installed but stopped", "Служба встановлена, але зупинена",
            "Der Dienst ist installiert, aber gestoppt", "Il servizio è installato ma fermo",
            "El servicio está instalado pero detenido", "Le service est installé mais arrêté",
            "O serviço está instalado mas parado", "الخدمة مثبّتة لكنها متوقفة",
            "सेवा स्थापित है पर रुकी हुई है", "সার্ভিস ইনস্টল করা আছে কিন্তু বন্ধ",
            "سروس نصب ہے مگر رکی ہوئی ہے", "服务已安装但已停止")

        Add("Служба запускается..",
            "The service is starting..", "Служба запускається..", "Der Dienst wird gestartet..",
            "Il servizio si sta avviando..", "El servicio se está iniciando..",
            "Le service démarre..", "O serviço está a arrancar..",
            "جارٍ بدء الخدمة..", "सेवा शुरू हो रही है..", "সার্ভিস চালু হচ্ছে..",
            "سروس شروع ہو رہی ہے..", "服务正在启动..")

        Add("Служба останавливается..",
            "The service is stopping..", "Служба зупиняється..", "Der Dienst wird beendet..",
            "Il servizio si sta arrestando..", "El servicio se está deteniendo..",
            "Le service s'arrête..", "O serviço está a parar..",
            "جارٍ إيقاف الخدمة..", "सेवा रुक रही है..", "সার্ভিস বন্ধ হচ্ছে..",
            "سروس رک رہی ہے..", "服务正在停止..")

        Add("Служба не установлена",
            "The service is not installed", "Служба не встановлена", "Der Dienst ist nicht installiert",
            "Il servizio non è installato", "El servicio no está instalado",
            "Le service n'est pas installé", "O serviço não está instalado",
            "الخدمة غير مثبّتة", "सेवा स्थापित नहीं है", "সার্ভিস ইনস্টল করা নেই",
            "سروس نصب نہیں ہے", "服务未安装")

        Add("Состояние службы определить не удалось",
            "The service state could not be determined", "Стан служби визначити не вдалося",
            "Der Dienststatus konnte nicht ermittelt werden", "Impossibile determinare lo stato del servizio",
            "No se pudo determinar el estado del servicio", "Impossible de déterminer l'état du service",
            "Não foi possível determinar o estado do serviço", "تعذّر تحديد حالة الخدمة",
            "सेवा की स्थिति निर्धारित नहीं हो सकी", "সার্ভিসের অবস্থা নির্ধারণ করা যায়নি",
            "سروس کی حالت معلوم نہیں ہو سکی", "无法确定服务状态")

        Add("Раздача SFTP выключена",
            "SFTP sharing is off", "Роздача SFTP вимкнена", "SFTP-Freigabe ist aus",
            "La condivisione SFTP è disattivata", "El uso compartido SFTP está desactivado",
            "Le partage SFTP est désactivé", "A partilha SFTP está desligada",
            "مشاركة SFTP متوقفة", "SFTP साझाकरण बंद है", "SFTP শেয়ারিং বন্ধ",
            "SFTP شیئرنگ بند ہے", "SFTP 共享已关闭")

        Add("Раздача SFTP работает",
            "SFTP sharing is on", "Роздача SFTP працює", "SFTP-Freigabe läuft",
            "La condivisione SFTP è attiva", "El uso compartido SFTP está activo",
            "Le partage SFTP est actif", "A partilha SFTP está ligada",
            "مشاركة SFTP تعمل", "SFTP साझाकरण चालू है", "SFTP শেয়ারিং চালু",
            "SFTP شیئرنگ چالو ہے", "SFTP 共享已开启")

        Add("Служба работает, но ни одна папка не выбрана - раздавать нечего",
            "The service is running, but no folder is selected - there is nothing to serve",
            "Служба працює, але жодну папку не вибрано - роздавати нічого",
            "Der Dienst läuft, aber kein Ordner ist ausgewählt - es gibt nichts freizugeben",
            "Il servizio è in esecuzione, ma nessuna cartella è selezionata: non c'è nulla da condividere",
            "El servicio está en ejecución, pero no hay ninguna carpeta seleccionada: no hay nada que compartir",
            "Le service fonctionne, mais aucun dossier n'est sélectionné : il n'y a rien à partager",
            "O serviço está a correr, mas nenhuma pasta está selecionada: não há nada para partilhar",
            "الخدمة تعمل، لكن لم يُحدَّد أي مجلد - لا يوجد ما يُشارَك",
            "सेवा चल रही है, पर कोई फ़ोल्डर चुना नहीं गया - साझा करने को कुछ नहीं है",
            "সার্ভিস চলছে, কিন্তু কোনো ফোল্ডার নির্বাচন করা হয়নি - শেয়ার করার কিছু নেই",
            "سروس چل رہی ہے، مگر کوئی فولڈر منتخب نہیں - شیئر کرنے کو کچھ نہیں",
            "服务正在运行，但未选择任何文件夹 - 没有可共享的内容")

        Add("Сейчас на канал управления никто не отвечает",
            "Nothing is answering the control channel right now",
            "Зараз на канал керування ніхто не відповідає",
            "Derzeit antwortet niemand auf dem Steuerkanal",
            "Al momento nessuno risponde sul canale di controllo",
            "Ahora mismo nadie responde en el canal de control",
            "Pour l'instant, personne ne répond sur le canal de contrôle",
            "De momento ninguém responde no canal de controlo",
            "لا أحد يستجيب على قناة التحكم الآن",
            "अभी नियंत्रण चैनल पर कोई उत्तर नहीं दे रहा",
            "এই মুহূর্তে কন্ট্রোল চ্যানেলে কেউ সাড়া দিচ্ছে না",
            "اس وقت کنٹرول چینل پر کوئی جواب نہیں دے رہا",
            "目前没有任何进程响应控制通道")

        Add("Сейчас отвечает: служба Windows",
            "Answering now: the Windows service", "Зараз відповідає: служба Windows",
            "Antwortet jetzt: der Windows-Dienst", "Risponde ora: il servizio Windows",
            "Responde ahora: el servicio de Windows", "Répond actuellement : le service Windows",
            "A responder agora: o serviço do Windows", "المستجيب الآن: خدمة Windows",
            "अभी उत्तर दे रही है: विंडोज़ सेवा", "এখন সাড়া দিচ্ছে: উইন্ডোজ সার্ভিস",
            "اب جواب دے رہی ہے: ونڈوز سروس", "当前响应者：Windows 服务")

        Add("Сейчас отвечает: фоновый процесс этой программы",
            "Answering now: this program's background process",
            "Зараз відповідає: фоновий процес цієї програми",
            "Antwortet jetzt: der Hintergrundprozess dieses Programms",
            "Risponde ora: il processo in background di questo programma",
            "Responde ahora: el proceso en segundo plano de este programa",
            "Répond actuellement : le processus d'arrière-plan de ce programme",
            "A responder agora: o processo em segundo plano deste programa",
            "المستجيب الآن: العملية الخلفية لهذا البرنامج",
            "अभी उत्तर दे रही है: इस प्रोग्राम की पृष्ठभूमि प्रक्रिया",
            "এখন সাড়া দিচ্ছে: এই প্রোগ্রামের ব্যাকগ্রাউন্ড প্রসেস",
            "اب جواب دے رہا ہے: اس پروگرام کا پس منظر پروسیس",
            "当前响应者：本程序的后台进程")

        Add("Общее хранилище настроек и ключа: {0}",
            "Shared settings and key store: {0}", "Спільне сховище налаштувань і ключа: {0}",
            "Gemeinsamer Speicher für Einstellungen und Schlüssel: {0}",
            "Archivio condiviso di impostazioni e chiave: {0}",
            "Almacén compartido de ajustes y clave: {0}",
            "Stockage partagé des paramètres et de la clé : {0}",
            "Armazenamento partilhado de definições e chave: {0}",
            "مخزن الإعدادات والمفتاح المشترك: {0}",
            "साझा सेटिंग और कुंजी संग्रह: {0}",
            "শেয়ার্ড সেটিংস ও কী সংরক্ষণ: {0}",
            "مشترکہ ترتیبات اور کلید کا ذخیرہ: {0}",
            "共享的设置与密钥存储位置：{0}")

        Add("Перезапустить службу",
            "Restart the service", "Перезапустити службу", "Dienst neu starten",
            "Riavvia il servizio", "Reiniciar el servicio", "Redémarrer le service",
            "Reiniciar o serviço", "إعادة تشغيل الخدمة", "सेवा पुनः आरंभ करें",
            "সার্ভিস পুনরায় চালু করুন", "سروس دوبارہ شروع کریں", "重启服务")

        Add("Выдать службе доступ к общим папкам",
            "Grant the service access to the shared folders",
            "Надати службі доступ до спільних папок",
            "Dem Dienst Zugriff auf die freigegebenen Ordner geben",
            "Concedi al servizio l'accesso alle cartelle condivise",
            "Conceder al servicio acceso a las carpetas compartidas",
            "Accorder au service l'accès aux dossiers partagés",
            "Conceder ao serviço acesso às pastas partilhadas",
            "منح الخدمة حق الوصول إلى المجلدات المُشارَكة",
            "सेवा को साझा फ़ोल्डरों तक पहुँच दें",
            "সার্ভিসকে শেয়ার করা ফোল্ডারে অ্যাক্সেস দিন",
            "سروس کو شیئر کیے گئے فولڈرز تک رسائی دیں",
            "授予服务访问共享文件夹的权限")

        Add("Перевести раздачу в режим службы Windows..",
            "Switch sharing to a Windows service..", "Перевести роздачу в режим служби Windows..",
            "Freigabe auf einen Windows-Dienst umstellen..", "Passa alla condivisione come servizio di Windows..",
            "Cambiar el uso compartido a un servicio de Windows..", "Basculer le partage vers un service Windows..",
            "Mudar o compartilhamento para um serviço do Windows..", "تحويل المشاركة إلى خدمة Windows..",
            "साझाकरण को Windows सेवा में बदलें..", "শেয়ারিং Windows সার্ভিসে পরিবর্তন করুন..",
            "شیئرنگ کو Windows سروس میں منتقل کریں..", "将共享切换为 Windows 服务..")
        Add("Раздачу папок возьмёт на себя служба Windows: она стартует вместе с системой и работает, даже когда в систему никто не вошёл." & vbCrLf & vbCrLf &
            "Windows запросит подтверждение администратора один раз. Будут: перенесены ваши настройки, папки и ключ узла в общее машинное хранилище, зарегистрирована служба, добавлено правило брандмауэра и выдан доступ к выбранным папкам." & vbCrLf & vbCrLf &
            "Ключ узла сохраняется, поэтому уже подключённые телефоны подключать заново не придётся. Вернуться к обычному режиму можно здесь же, кнопкой «Вернуться к пользовательской редакции..»." & vbCrLf & vbCrLf &
            "Перевести раздачу в режим службы?",
            "A Windows service will take folder sharing over: it starts with Windows and keeps serving even when nobody is signed in." & vbCrLf & vbCrLf &
            "Windows will ask for administrator approval once. This moves your settings, folders and host key into the shared machine store, registers the service, adds a firewall rule and grants it access to the selected folders." & vbCrLf & vbCrLf &
            "The host key is kept, so phones that are already paired do not have to be paired again. You can come back to the ordinary mode from here, with «Return to the User edition..»." & vbCrLf & vbCrLf &
            "Switch sharing to the service?",
            "Роздачу тек візьме на себе служба Windows: вона стартує разом із системою й працює навіть тоді, коли в систему ніхто не ввійшов." & vbCrLf & vbCrLf &
            "Windows один раз запитає підтвердження адміністратора. Буде: перенесено ваші налаштування, теки та ключ вузла до спільного машинного сховища, зареєстровано службу, додано правило брандмауера й видано доступ до вибраних тек." & vbCrLf & vbCrLf &
            "Ключ вузла зберігається, тож уже підключені телефони підключати наново не доведеться. Повернутися до звичайного режиму можна тут само, кнопкою «Вернуться к пользовательской редакции..»." & vbCrLf & vbCrLf &
            "Перевести роздачу в режим служби?",
            "Ein Windows-Dienst übernimmt die Ordnerfreigabe: Er startet mit Windows und liefert weiter, auch wenn niemand angemeldet ist." & vbCrLf & vbCrLf &
            "Windows fragt einmal nach der Bestätigung durch einen Administrator. Dabei werden Ihre Einstellungen, Ordner und der Hostschlüssel in den gemeinsamen Maschinenspeicher verschoben, der Dienst registriert, eine Firewallregel hinzugefügt und ihm Zugriff auf die ausgewählten Ordner gewährt." & vbCrLf & vbCrLf &
            "Der Hostschlüssel bleibt erhalten, bereits gekoppelte Telefone müssen also nicht neu gekoppelt werden. Zum gewöhnlichen Modus kommen Sie hier zurück, über «Zur Benutzer-Edition zurückkehren..»." & vbCrLf & vbCrLf &
            "Freigabe auf den Dienst umstellen?",
            "Un servizio di Windows si occuperà della condivisione delle cartelle: si avvia con Windows e continua a servire anche quando nessuno ha effettuato l'accesso." & vbCrLf & vbCrLf &
            "Windows chiederà una volta l'approvazione dell'amministratore. Verranno spostati impostazioni, cartelle e chiave host nell'archivio condiviso della macchina, registrato il servizio, aggiunta una regola del firewall e concesso l'accesso alle cartelle selezionate." & vbCrLf & vbCrLf &
            "La chiave host viene conservata, quindi i telefoni già associati non dovranno essere associati di nuovo. Puoi tornare alla modalità normale da qui, con «Torna all'edizione Utente..»." & vbCrLf & vbCrLf &
            "Passare alla condivisione tramite servizio?",
            "Un servicio de Windows se encargará del uso compartido de carpetas: se inicia con Windows y sigue sirviendo aunque no haya nadie con la sesión iniciada." & vbCrLf & vbCrLf &
            "Windows pedirá la aprobación del administrador una vez. Se moverán sus ajustes, carpetas y la clave del host al almacén compartido del equipo, se registrará el servicio, se añadirá una regla del firewall y se le concederá acceso a las carpetas seleccionadas." & vbCrLf & vbCrLf &
            "La clave del host se conserva, así que los teléfonos ya emparejados no tendrán que emparejarse de nuevo. Puede volver al modo normal desde aquí, con «Volver a la edición de Usuario..»." & vbCrLf & vbCrLf &
            "¿Cambiar el uso compartido al servicio?",
            "Un service Windows prendra en charge le partage de dossiers : il démarre avec Windows et continue de servir même si personne n'est connecté." & vbCrLf & vbCrLf &
            "Windows demandera une fois l'approbation d'un administrateur. Vos réglages, vos dossiers et la clé d'hôte seront déplacés vers le stockage commun de la machine, le service sera enregistré, une règle de pare-feu ajoutée et l'accès aux dossiers sélectionnés accordé." & vbCrLf & vbCrLf &
            "La clé d'hôte est conservée : les téléphones déjà appairés n'auront pas à l'être de nouveau. Vous pouvez revenir au mode ordinaire ici même, avec «Revenir à l'édition Utilisateur..»." & vbCrLf & vbCrLf &
            "Basculer le partage vers le service ?",
            "Um serviço do Windows assumirá o compartilhamento de pastas: ele inicia com o Windows e continua servindo mesmo sem ninguém conectado." & vbCrLf & vbCrLf &
            "O Windows pedirá a aprovação do administrador uma vez. Suas configurações, pastas e a chave do host serão movidas para o armazenamento compartilhado da máquina, o serviço será registrado, uma regra de firewall será adicionada e o acesso às pastas selecionadas concedido." & vbCrLf & vbCrLf &
            "A chave do host é mantida, então os telefones já pareados não precisarão ser pareados novamente. Você pode voltar ao modo comum aqui mesmo, com «Voltar para a edição de Usuário..»." & vbCrLf & vbCrLf &
            "Mudar o compartilhamento para o serviço?",
            "ستتولى خدمة Windows مشاركة المجلدات: تبدأ مع النظام وتستمر في الخدمة حتى لو لم يسجّل أحد الدخول." & vbCrLf & vbCrLf &
            "سيطلب Windows موافقة المسؤول مرة واحدة. ستُنقل إعداداتك ومجلداتك ومفتاح المضيف إلى المخزن المشترك للجهاز، وتُسجَّل الخدمة، وتُضاف قاعدة جدار حماية، ويُمنح الوصول إلى المجلدات المحددة." & vbCrLf & vbCrLf &
            "يُحتفظ بمفتاح المضيف، لذا لن تحتاج الهواتف المقترنة بالفعل إلى الاقتران من جديد. يمكنك العودة إلى الوضع العادي من هنا عبر «العودة إلى إصدارة المستخدم..»." & vbCrLf & vbCrLf &
            "هل تحوّل المشاركة إلى الخدمة؟",
            "फ़ोल्डर साझाकरण को एक Windows सेवा संभालेगी: यह Windows के साथ शुरू होती है और तब भी सेवा देती रहती है जब कोई साइन इन न हो।" & vbCrLf & vbCrLf &
            "Windows एक बार व्यवस्थापक की स्वीकृति माँगेगा। आपकी सेटिंग्स, फ़ोल्डर और होस्ट कुंजी साझा मशीन संग्रह में ले जाई जाएँगी, सेवा पंजीकृत होगी, फ़ायरवॉल नियम जुड़ेगा और चुने हुए फ़ोल्डरों तक पहुँच दी जाएगी।" & vbCrLf & vbCrLf &
            "होस्ट कुंजी सुरक्षित रहती है, इसलिए पहले से जुड़े फ़ोन दोबारा जोड़ने की ज़रूरत नहीं। सामान्य मोड में यहीं से «उपयोगकर्ता संस्करण पर लौटें..» द्वारा लौटा जा सकता है।" & vbCrLf & vbCrLf &
            "साझाकरण को सेवा में बदलें?",
            "ফোল্ডার শেয়ারিং একটি Windows সার্ভিস চালাবে: এটি Windows-এর সঙ্গে চালু হয় এবং কেউ সাইন ইন না থাকলেও সেবা দিতে থাকে।" & vbCrLf & vbCrLf &
            "Windows একবার প্রশাসকের অনুমোদন চাইবে। আপনার সেটিংস, ফোল্ডার ও হোস্ট কী যৌথ মেশিন স্টোরে সরানো হবে, সার্ভিস নিবন্ধিত হবে, ফায়ারওয়াল নিয়ম যোগ হবে এবং নির্বাচিত ফোল্ডারে অ্যাক্সেস দেওয়া হবে।" & vbCrLf & vbCrLf &
            "হোস্ট কী রক্ষিত থাকে, তাই আগে যুক্ত ফোনগুলো আবার যুক্ত করতে হবে না। সাধারণ মোডে এখান থেকেই «ব্যবহারকারী সংস্করণে ফিরুন..» দিয়ে ফেরা যায়।" & vbCrLf & vbCrLf &
            "শেয়ারিং সার্ভিসে বদলাবেন?",
            "فولڈر شیئرنگ ایک Windows سروس سنبھالے گی: یہ Windows کے ساتھ شروع ہوتی ہے اور کسی کے سائن اِن نہ ہونے پر بھی خدمت جاری رکھتی ہے۔" & vbCrLf & vbCrLf &
            "Windows ایک بار منتظم کی منظوری مانگے گا۔ آپ کی ترتیبات، فولڈر اور ہوسٹ کلید مشترکہ مشین اسٹور میں منتقل ہوں گی، سروس رجسٹر ہوگی، فائر وال قاعدہ شامل ہوگا اور منتخب فولڈروں تک رسائی دی جائے گی۔" & vbCrLf & vbCrLf &
            "ہوسٹ کلید محفوظ رہتی ہے، اس لیے پہلے سے جُڑے فون دوبارہ جوڑنے کی ضرورت نہیں۔ عام موڈ میں یہیں سے «صارف ایڈیشن پر واپس جائیں..» کے ذریعے واپس آ سکتے ہیں۔" & vbCrLf & vbCrLf &
            "شیئرنگ کو سروس میں بدلیں؟",
            "文件夹共享将交给 Windows 服务：它随系统启动，即使无人登录也继续提供服务。" & vbCrLf & vbCrLf &
            "Windows 会请求一次管理员确认。您的设置、文件夹和主机密钥将移入共用的计算机存储，服务会被注册，防火墙规则会被添加，并授予其访问所选文件夹的权限。" & vbCrLf & vbCrLf &
            "主机密钥会保留，因此已配对的手机无需重新配对。可以在这里通过「返回用户版..」回到普通模式。" & vbCrLf & vbCrLf &
            "要把共享切换为服务吗？")
        Add("Версия из Microsoft Store не может устанавливать службы Windows. Круглосуточная раздача есть в версии с сайта - её можно поставить рядом.",
            "The Microsoft Store build cannot install Windows services. Always-on sharing is available in the build from the website, which can be installed alongside it.",
            "Версія з Microsoft Store не може встановлювати служби Windows. Цілодобова роздача є у версії з сайту - її можна поставити поруч.",
            "Die Microsoft-Store-Version kann keine Windows-Dienste installieren. Die Dauerfreigabe gibt es in der Version von der Website, die sich daneben installieren lässt.",
            "La versione del Microsoft Store non può installare servizi di Windows. La condivisione sempre attiva è nella versione dal sito, che può essere installata accanto.",
            "La versión de Microsoft Store no puede instalar servicios de Windows. El uso compartido permanente está en la versión del sitio web, que puede instalarse junto a ella.",
            "La version du Microsoft Store ne peut pas installer de services Windows. Le partage permanent existe dans la version du site, qui peut être installée à côté.",
            "A versão da Microsoft Store não pode instalar serviços do Windows. O compartilhamento permanente está na versão do site, que pode ser instalada ao lado.",
            "لا يمكن لإصدارة Microsoft Store تثبيت خدمات Windows. المشاركة الدائمة متوفرة في الإصدارة الموجودة على الموقع، ويمكن تثبيتها إلى جانبها.",
            "Microsoft Store संस्करण Windows सेवाएँ स्थापित नहीं कर सकता। हरदम चालू साझाकरण वेबसाइट वाले संस्करण में है, जिसे साथ में स्थापित किया जा सकता है।",
            "Microsoft Store সংস্করণ Windows সার্ভিস ইনস্টল করতে পারে না। সার্বক্ষণিক শেয়ারিং ওয়েবসাইটের সংস্করণে আছে, যা পাশাপাশি ইনস্টল করা যায়।",
            "Microsoft Store ورژن Windows سروسز انسٹال نہیں کر سکتا۔ ہمہ وقت شیئرنگ ویب سائٹ والے ورژن میں ہے، جسے ساتھ انسٹال کیا جا سکتا ہے۔",
            "Microsoft Store 版本无法安装 Windows 服务。全天候共享在网站版本中提供，可以并存安装。")
        Add("Установить серверную редакцию..",
            "Install the Server edition..", "Установити серверну редакцію..",
            "Server-Edition installieren..", "Installa l'edizione Server..",
            "Instalar la edición Servidor..", "Installer l'édition Serveur..",
            "Instalar a edição Servidor..", "تثبيت إصدارة الخادم..",
            "सर्वर संस्करण स्थापित करें..", "সার্ভার সংস্করণ ইনস্টল করুন..",
            "سرور ایڈیشن نصب کریں..", "安装服务器版..")

        Add("Вернуться к пользовательской редакции..",
            "Return to the User edition..", "Повернутися до користувацької редакції..",
            "Zurück zur Benutzer-Edition..", "Torna all'edizione Utente..",
            "Volver a la edición de Usuario..", "Revenir à l'édition Utilisateur..",
            "Voltar à edição de Utilizador..", "العودة إلى إصدارة المستخدم..",
            "उपयोक्ता संस्करण पर लौटें..", "ইউজার সংস্করণে ফিরুন..",
            "یوزر ایڈیشن پر واپس جائیں..", "返回用户版..")

        Add("Запустить службу",
            "Start the service", "Запустити службу", "Dienst starten", "Avvia il servizio",
            "Iniciar el servicio", "Démarrer le service", "Iniciar o serviço",
            "بدء الخدمة", "सेवा शुरू करें", "সার্ভিস চালু করুন", "سروس شروع کریں", "启动服务")

        Add("Остановить службу",
            "Stop the service", "Зупинити службу", "Dienst beenden", "Arresta il servizio",
            "Detener el servicio", "Arrêter le service", "Parar o serviço",
            "إيقاف الخدمة", "सेवा रोकें", "সার্ভিস বন্ধ করুন", "سروس روکیں", "停止服务")

        Add("Восстановить регистрацию службы",
            "Repair the service registration", "Відновити реєстрацію служби",
            "Dienstregistrierung reparieren", "Ripara la registrazione del servizio",
            "Reparar el registro del servicio", "Réparer l'enregistrement du service",
            "Reparar o registo do serviço", "إصلاح تسجيل الخدمة",
            "सेवा पंजीकरण की मरम्मत करें", "সার্ভিস নিবন্ধন মেরামত করুন",
            "سروس رجسٹریشن کی مرمت کریں", "修复服务注册")

        Add("Удалить роль сервера",
            "Remove the server role", "Видалити роль сервера", "Serverrolle entfernen",
            "Rimuovi il ruolo server", "Quitar el rol de servidor", "Supprimer le rôle serveur",
            "Remover a função de servidor", "إزالة دور الخادم",
            "सर्वर भूमिका हटाएँ", "সার্ভার ভূমিকা সরান", "سرور رول ہٹائیں", "移除服务器角色")

        Add("Серверная редакция скачивается отдельно - с сайта или через winget. Её установщик перенесёт ключ узла, пароль, список папок и порт, так что привязывать телефоны заново не придётся. Эта программа никогда не скачивает и не запускает установщик сама.",
            "The Server edition is a separate download - from the website or through winget. Its installer migrates the host key, password, folder list and port, so paired phones need no re-pairing. This program never downloads or runs an installer on its own.",
            "Серверна редакція завантажується окремо - із сайту або через winget. Її установник перенесе ключ вузла, пароль, список папок і порт, тож прив'язувати телефони заново не доведеться. Ця програма ніколи не завантажує та не запускає установник сама.",
            "Die Server-Edition ist ein separater Download - von der Website oder über winget. Ihr Installer übernimmt Hostschlüssel, Passwort, Ordnerliste und Port, sodass gekoppelte Telefone nicht neu gekoppelt werden müssen. Dieses Programm lädt oder startet niemals selbst einen Installer.",
            "L'edizione Server è un download separato: dal sito o tramite winget. Il suo installer migra chiave host, password, elenco cartelle e porta, quindi i telefoni già associati non vanno riassociati. Questo programma non scarica né avvia mai un installer da solo.",
            "La edición Servidor es una descarga aparte: desde el sitio web o mediante winget. Su instalador migra la clave de host, la contraseña, la lista de carpetas y el puerto, así que los teléfonos ya emparejados no hay que volver a emparejarlos. Este programa nunca descarga ni ejecuta un instalador por su cuenta.",
            "L'édition Serveur est un téléchargement distinct : depuis le site web ou via winget. Son installeur migre la clé d'hôte, le mot de passe, la liste des dossiers et le port, si bien que les téléphones appairés n'ont pas à l'être de nouveau. Ce programme ne télécharge ni ne lance jamais un installeur de lui-même.",
            "A edição Servidor é uma transferência separada - do site ou através do winget. O seu instalador migra a chave de anfitrião, a palavra-passe, a lista de pastas e a porta, pelo que os telemóveis emparelhados não precisam de novo emparelhamento. Este programa nunca transfere nem executa um instalador por si próprio.",
            "إصدارة الخادم تُنزَّل بشكل منفصل - من الموقع أو عبر winget. يقوم مثبّتها بنقل مفتاح المضيف وكلمة المرور وقائمة المجلدات والمنفذ، فلا حاجة لإعادة إقران الهواتف. لا يقوم هذا البرنامج أبدًا بتنزيل مثبّت أو تشغيله من تلقاء نفسه.",
            "सर्वर संस्करण अलग से डाउनलोड होता है - वेबसाइट से या winget के ज़रिये। उसका इंस्टॉलर होस्ट कुंजी, पासवर्ड, फ़ोल्डर सूची और पोर्ट स्थानांतरित कर देता है, इसलिए जुड़े फ़ोन दोबारा जोड़ने की ज़रूरत नहीं। यह प्रोग्राम कभी स्वयं इंस्टॉलर डाउनलोड या चालू नहीं करता।",
            "সার্ভার সংস্করণ আলাদাভাবে ডাউনলোড হয় - ওয়েবসাইট থেকে বা winget দিয়ে। এর ইনস্টলার হোস্ট কী, পাসওয়ার্ড, ফোল্ডার তালিকা ও পোর্ট স্থানান্তর করে, তাই যুক্ত ফোনগুলি আবার যুক্ত করতে হয় না। এই প্রোগ্রাম কখনও নিজে থেকে ইনস্টলার ডাউনলোড বা চালু করে না।",
            "سرور ایڈیشن الگ سے ڈاؤن لوڈ ہوتا ہے - ویب سائٹ سے یا winget کے ذریعے۔ اس کا انسٹالر ہوسٹ کلید، پاس ورڈ، فولڈر فہرست اور پورٹ منتقل کر دیتا ہے، اس لیے جُڑے ہوئے فون دوبارہ جوڑنے کی ضرورت نہیں۔ یہ پروگرام کبھی خود انسٹالر ڈاؤن لوڈ یا چلاتا نہیں۔",
            "服务器版需单独下载 - 从网站或通过 winget。其安装程序会迁移主机密钥、密码、文件夹列表和端口，因此已配对的手机无需重新配对。本程序绝不会自行下载或运行安装程序。")

        Add("Папки раздаёт служба Windows, и работает она под своей учётной записью - к этим папкам у неё пока нет доступа:" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "Выдать его сейчас? Windows спросит подтверждение.",
            "The folders are served by a Windows service, and it runs under an account of its own - which has no access to these folders yet:" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "Grant it now? Windows will ask for confirmation.",
            "Теки роздає служба Windows, і працює вона під власним обліковим записом - до цих тек у неї поки немає доступу:" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "Видати його зараз? Windows запитає підтвердження.",
            "Die Ordner werden von einem Windows-Dienst bereitgestellt, und der läuft unter einem eigenen Konto - auf diese Ordner hat es noch keinen Zugriff:" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "Jetzt vergeben? Windows fragt nach einer Bestätigung.",
            "Le cartelle sono servite da un servizio di Windows, che gira con un account proprio - e a queste cartelle non ha ancora accesso:" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "Concederlo ora? Windows chiederà conferma.",
            "Las carpetas las sirve un servicio de Windows, que se ejecuta con una cuenta propia y todavía no tiene acceso a estas carpetas:" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "¿Concederlo ahora? Windows pedirá confirmación.",
            "Les dossiers sont servis par un service Windows, qui s'exécute avec son propre compte - lequel n'a pas encore accès à ces dossiers :" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "L'accorder maintenant ? Windows demandera une confirmation.",
            "As pastas são servidas por um serviço do Windows, que corre com uma conta própria - e ainda não tem acesso a estas pastas:" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "Conceder agora? O Windows vai pedir confirmação.",
            "تقدّم المجلدات خدمة Windows تعمل بحساب خاص بها - وهي لا تملك بعد صلاحية الوصول إلى هذه المجلدات:" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "هل تمنحها الآن؟ سيطلب Windows تأكيدًا.",
            "फ़ोल्डर एक Windows सेवा साझा करती है, और वह अपने अलग खाते से चलती है - इन फ़ोल्डरों तक उसकी पहुँच अभी नहीं है:" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "अभी दें? Windows पुष्टि माँगेगा।",
            "ফোল্ডারগুলো একটি Windows সার্ভিস শেয়ার করে, আর সেটি নিজের আলাদা অ্যাকাউন্টে চলে - এই ফোল্ডারগুলোতে তার এখনও অনুমতি নেই:" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "এখনই দেবেন? Windows নিশ্চিত করতে বলবে।",
            "فولڈرز ایک Windows سروس فراہم کرتی ہے، اور وہ اپنے الگ اکاؤنٹ سے چلتی ہے - ان فولڈرز تک اسے ابھی رسائی نہیں ہے:" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "ابھی دیں؟ Windows تصدیق مانگے گا۔",
            "文件夹由一个 Windows 服务提供，它以自己的账户运行 - 目前它还无法访问这些文件夹：" & vbCrLf & vbCrLf & "{0}" & vbCrLf & vbCrLf & "现在授予吗？Windows 会请求确认。")

        Add("Без доступа служба не откроет эти папки на телефоне. Выдать его можно позже - «Управление хостингом..».",
            "Without access the service cannot open these folders on the phone. You can grant it later - «Manage hosting..».",
            "Без доступу служба не відкриє ці теки на телефоні. Видати його можна пізніше - «Керування хостингом..».",
            "Ohne Zugriff kann der Dienst diese Ordner auf dem Telefon nicht öffnen. Sie können ihn später vergeben - «Hosting verwalten..».",
            "Senza accesso il servizio non può aprire queste cartelle sul telefono. Puoi concederlo più tardi - «Gestisci hosting..».",
            "Sin acceso el servicio no puede abrir estas carpetas en el teléfono. Puede concederlo más tarde - «Gestionar el alojamiento..».",
            "Sans accès, le service ne peut pas ouvrir ces dossiers sur le téléphone. Vous pouvez l'accorder plus tard - «Gérer l'hébergement..».",
            "Sem acesso o serviço não consegue abrir estas pastas no telefone. Pode concedê-lo mais tarde - «Gerir o alojamento..».",
            "بدون صلاحية الوصول لن تتمكن الخدمة من فتح هذه المجلدات على الهاتف. يمكنك منحها لاحقًا - «إدارة الاستضافة..».",
            "पहुँच के बिना सेवा इन फ़ोल्डरों को फ़ोन पर नहीं खोल पाएगी। आप इसे बाद में दे सकते हैं - «होस्टिंग प्रबंधन..»।",
            "অনুমতি ছাড়া সার্ভিসটি এই ফোল্ডারগুলো ফোনে খুলতে পারবে না। আপনি পরে দিতে পারেন - «হোস্টিং ব্যবস্থাপনা..»।",
            "رسائی کے بغیر سروس یہ فولڈرز فون پر نہیں کھول سکے گی۔ آپ بعد میں دے سکتے ہیں - «ہوسٹنگ کا انتظام..»۔",
            "没有访问权限，该服务无法在手机上打开这些文件夹。你可以稍后授予 - «管理托管..»。")

        Add("Выдаю службе доступ к папкам..",
            "Granting the service access to the folders..",
            "Видаю службі доступ до тек..",
            "Zugriff für den Dienst wird vergeben..",
            "Concessione dell'accesso al servizio..",
            "Concediendo acceso al servicio..",
            "Attribution de l'accès au service..",
            "A conceder acesso ao serviço..",
            "جارٍ منح الخدمة صلاحية الوصول إلى المجلدات..",
            "सेवा को फ़ोल्डरों तक पहुँच दी जा रही है..",
            "সার্ভিসকে ফোল্ডারে অনুমতি দেওয়া হচ্ছে..",
            "سروس کو فولڈرز تک رسائی دی جا رہی ہے..",
            "正在为该服务授予文件夹访问权限..")

        Add("Раздача работает, но у службы нет доступа к папкам: {0}",
            "Sharing is on, but the service has no access to folders: {0}",
            "Роздача працює, але служба не має доступу до тек: {0}",
            "Die Freigabe läuft, aber der Dienst hat keinen Zugriff auf Ordner: {0}",
            "La condivisione è attiva, ma il servizio non ha accesso alle cartelle: {0}",
            "El uso compartido está activo, pero el servicio no tiene acceso a carpetas: {0}",
            "Le partage est actif, mais le service n'a pas accès aux dossiers : {0}",
            "A partilha está ativa, mas o serviço não tem acesso a pastas: {0}",
            "المشاركة مفعّلة، لكن الخدمة لا تملك صلاحية الوصول إلى مجلدات: {0}",
            "साझाकरण चालू है, पर सेवा को फ़ोल्डरों तक पहुँच नहीं: {0}",
            "শেয়ারিং চালু, কিন্তু সার্ভিসের ফোল্ডারে অনুমতি নেই: {0}",
            "شیئرنگ چالو ہے، مگر سروس کو فولڈرز تک رسائی نہیں: {0}",
            "共享已开启，但该服务无权访问文件夹：{0}")

        Add("У службы нет доступа к этой папке - на телефоне она не откроется.",
            "The service has no access to this folder - it will not open on the phone.",
            "Служба не має доступу до цієї теки - на телефоні вона не відкриється.",
            "Der Dienst hat keinen Zugriff auf diesen Ordner - auf dem Telefon lässt er sich nicht öffnen.",
            "Il servizio non ha accesso a questa cartella - sul telefono non si aprirà.",
            "El servicio no tiene acceso a esta carpeta - en el teléfono no se abrirá.",
            "Le service n'a pas accès à ce dossier - il ne s'ouvrira pas sur le téléphone.",
            "O serviço não tem acesso a esta pasta - no telefone não vai abrir.",
            "الخدمة لا تملك صلاحية الوصول إلى هذا المجلد - لن يُفتح على الهاتف.",
            "सेवा को इस फ़ोल्डर तक पहुँच नहीं है - फ़ोन पर यह नहीं खुलेगा।",
            "সার্ভিসের এই ফোল্ডারে অনুমতি নেই - ফোনে এটি খুলবে না।",
            "سروس کو اس فولڈر تک رسائی نہیں - فون پر یہ نہیں کھلے گا۔",
            "该服务无权访问此文件夹 - 在手机上将无法打开。")

        Add("Служба работает от имени LOCAL SERVICE, а не от вашего. Каждой раздаваемой папке нужен доступ для этой учётной записи: чтение, а если папка раздаётся с записью - то и запись. Кнопка выдаёт их по текущему списку папок. Сетевые пути вида \\сервер\папка так работать не будут.",
            "The service runs as LOCAL SERVICE, not as you. Every served folder needs access for that account: read, plus write when the folder is shared writable. The button grants that from the current folder list. UNC paths such as \\server\share will not work this way.",
            "Служба працює від імені LOCAL SERVICE, а не від вашого. Кожній роздаваній папці потрібен доступ для цього облікового запису: читання, а якщо папка роздається із записом - то й запис. Кнопка видає їх за поточним списком папок. Мережеві шляхи виду \\сервер\папка так працювати не будуть.",
            "Der Dienst läuft als LOCAL SERVICE, nicht unter Ihrem Konto. Jeder freigegebene Ordner braucht Rechte für dieses Konto: Lesen und, wenn der Ordner beschreibbar freigegeben ist, auch Schreiben. Die Schaltfläche vergibt sie anhand der aktuellen Ordnerliste. UNC-Pfade wie \\Server\Freigabe funktionieren so nicht.",
            "Il servizio viene eseguito come LOCAL SERVICE, non come te. Ogni cartella condivisa richiede l'accesso per quell'account: lettura e, se la cartella è condivisa in scrittura, anche scrittura. Il pulsante li concede in base all'elenco attuale delle cartelle. I percorsi UNC come \\server\condivisione non funzionano in questo modo.",
            "El servicio se ejecuta como LOCAL SERVICE, no como usted. Cada carpeta compartida necesita acceso para esa cuenta: lectura y, si la carpeta se comparte con escritura, también escritura. El botón los concede según la lista actual de carpetas. Las rutas UNC como \\servidor\recurso no funcionan así.",
            "Le service s'exécute en tant que LOCAL SERVICE, pas en votre nom. Chaque dossier partagé a besoin d'un accès pour ce compte : lecture, et écriture si le dossier est partagé en écriture. Le bouton les accorde d'après la liste actuelle des dossiers. Les chemins UNC comme \\serveur\partage ne fonctionnent pas ainsi.",
            "O serviço é executado como LOCAL SERVICE, não em seu nome. Cada pasta partilhada precisa de acesso para essa conta: leitura e, se a pasta for partilhada com escrita, também escrita. O botão concede-os a partir da lista atual de pastas. Caminhos UNC como \\servidor\partilha não funcionam desta forma.",
            "تعمل الخدمة باسم LOCAL SERVICE وليس باسمك. يحتاج كل مجلد مُشارَك إلى صلاحيات لهذا الحساب: القراءة، وكذلك الكتابة إذا كان المجلد مُشارَكًا مع إمكانية الكتابة. يمنحها الزر وفق قائمة المجلدات الحالية. مسارات UNC مثل \\server\share لن تعمل بهذه الطريقة.",
            "सेवा आपके नाम से नहीं, LOCAL SERVICE के रूप में चलती है। साझा की गई हर फ़ोल्डर को उस खाते के लिए अनुमति चाहिए: पढ़ना, और यदि फ़ोल्डर लिखने की अनुमति के साथ साझा है तो लिखना भी। बटन उन्हें मौजूदा फ़ोल्डर सूची के अनुसार देता है। \\server\share जैसे UNC पथ इस तरह काम नहीं करेंगे।",
            "সার্ভিসটি আপনার নামে নয়, LOCAL SERVICE হিসেবে চলে। শেয়ার করা প্রতিটি ফোল্ডারের জন্য ওই অ্যাকাউন্টের অনুমতি দরকার: পড়া, আর ফোল্ডারটি লেখার অনুমতিসহ শেয়ার করা থাকলে লেখাও। বোতামটি বর্তমান ফোল্ডার তালিকা অনুযায়ী তা দেয়। \\server\share ধরনের UNC পাথ এভাবে কাজ করবে না।",
            "سروس آپ کے نام سے نہیں بلکہ LOCAL SERVICE کے طور پر چلتی ہے۔ ہر شیئر کیے گئے فولڈر کو اس اکاؤنٹ کے لیے اجازت درکار ہے: پڑھنا، اور اگر فولڈر لکھنے کی اجازت کے ساتھ شیئر ہو تو لکھنا بھی۔ بٹن انہیں موجودہ فولڈر فہرست کے مطابق دیتا ہے۔ \\server\share جیسے UNC راستے اس طرح کام نہیں کریں گے۔",
            "该服务以 LOCAL SERVICE 身份运行，而不是以你的身份。每个共享文件夹都需要授予该账户权限：读取，若该文件夹以可写方式共享，还需写入。按钮会按当前文件夹列表授予。\\server\share 这类 UNC 路径无法以此方式工作。")

        Add("Удаление роли сервера остановит и удалит службу. Ключ узла, пароль и список папок останутся на месте - после возврата в пользовательский режим телефоны подключатся как раньше.",
            "Removing the server role stops and deletes the service. The host key, password and folder list stay where they are - after returning to User mode the phones connect as before.",
            "Видалення ролі сервера зупинить і видалить службу. Ключ вузла, пароль і список папок залишаться на місці - після повернення в користувацький режим телефони підключаться як раніше.",
            "Das Entfernen der Serverrolle stoppt und löscht den Dienst. Hostschlüssel, Passwort und Ordnerliste bleiben erhalten - nach der Rückkehr in den Benutzermodus verbinden sich die Telefone wie zuvor.",
            "La rimozione del ruolo server arresta ed elimina il servizio. Chiave host, password ed elenco cartelle restano al loro posto: tornando alla modalità Utente i telefoni si connettono come prima.",
            "Quitar el rol de servidor detiene y elimina el servicio. La clave de host, la contraseña y la lista de carpetas se conservan: al volver al modo de Usuario los teléfonos se conectan como antes.",
            "Supprimer le rôle serveur arrête et supprime le service. La clé d'hôte, le mot de passe et la liste des dossiers restent en place : après le retour en mode Utilisateur, les téléphones se connectent comme avant.",
            "Remover a função de servidor pára e elimina o serviço. A chave de anfitrião, a palavra-passe e a lista de pastas mantêm-se - ao voltar ao modo de Utilizador os telemóveis ligam-se como antes.",
            "إزالة دور الخادم توقف الخدمة وتحذفها. يبقى مفتاح المضيف وكلمة المرور وقائمة المجلدات في مكانها - وبعد العودة إلى وضع المستخدم تتصل الهواتف كما كانت.",
            "सर्वर भूमिका हटाने से सेवा रुकती है और हट जाती है। होस्ट कुंजी, पासवर्ड और फ़ोल्डर सूची यथावत रहती है - उपयोक्ता मोड में लौटने पर फ़ोन पहले की तरह जुड़ते हैं।",
            "সার্ভার ভূমিকা সরালে সার্ভিসটি বন্ধ হয়ে মুছে যায়। হোস্ট কী, পাসওয়ার্ড ও ফোল্ডার তালিকা যেমন আছে তেমনই থাকে - ইউজার মোডে ফিরলে ফোনগুলি আগের মতোই যুক্ত হয়।",
            "سرور رول ہٹانے سے سروس رک کر حذف ہو جاتی ہے۔ ہوسٹ کلید، پاس ورڈ اور فولڈر فہرست اپنی جگہ رہتی ہے - یوزر موڈ میں واپسی پر فون پہلے کی طرح جُڑتے ہیں۔",
            "移除服务器角色会停止并删除该服务。主机密钥、密码和文件夹列表保持不变 - 返回用户模式后，手机仍可照旧连接。")

        Add("Управление службой недоступно: это установка пользовательской редакции.",
            "Service management is unavailable: this is a User edition install.",
            "Керування службою недоступне: це встановлення користувацької редакції.",
            "Dienstverwaltung nicht verfügbar: Dies ist eine Installation der Benutzer-Edition.",
            "Gestione del servizio non disponibile: questa è un'installazione dell'edizione Utente.",
            "La gestión del servicio no está disponible: esta es una instalación de la edición de Usuario.",
            "Gestion du service indisponible : il s'agit d'une installation de l'édition Utilisateur.",
            "Gestão do serviço indisponível: esta é uma instalação da edição de Utilizador.",
            "إدارة الخدمة غير متاحة: هذا تثبيت لإصدارة المستخدم.",
            "सेवा प्रबंधन उपलब्ध नहीं: यह उपयोक्ता संस्करण की स्थापना है।",
            "সার্ভিস ব্যবস্থাপনা অনুপলব্ধ: এটি ইউজার সংস্করণের ইনস্টল।",
            "سروس مینجمنٹ دستیاب نہیں: یہ یوزر ایڈیشن کی تنصیب ہے۔",
            "无法管理服务：这是用户版的安装。")

        Add("Выполняется.. подтвердите запрос прав администратора.",
            "Working.. approve the administrator prompt.",
            "Виконується.. підтвердьте запит прав адміністратора.",
            "Wird ausgeführt.. bestätigen Sie die Administratorabfrage.",
            "In corso.. approva la richiesta di amministratore.",
            "En curso.. aprueba la solicitud de administrador.",
            "En cours.. approuvez l'invite d'administrateur.",
            "Em curso.. aprove o pedido de administrador.",
            "جارٍ التنفيذ.. وافق على طلب صلاحيات المسؤول.",
            "चल रहा है.. व्यवस्थापक अनुरोध स्वीकार करें।",
            "চলছে.. প্রশাসক অনুরোধ অনুমোদন করুন।",
            "جاری ہے.. ایڈمنسٹریٹر درخواست منظور کریں۔",
            "正在执行.. 请批准管理员提示。")

        Add("Готово.",
            "Done.", "Готово.", "Fertig.", "Fatto.", "Listo.", "Terminé.", "Concluído.",
            "تم.", "पूरा हुआ।", "সম্পন্ন।", "مکمل۔", "完成。")

        Add("Не выполнено: не получены права администратора.",
            "Not done: administrator rights were not granted.",
            "Не виконано: не отримано права адміністратора.",
            "Nicht ausgeführt: Administratorrechte wurden nicht erteilt.",
            "Non eseguito: i diritti di amministratore non sono stati concessi.",
            "No realizado: no se concedieron derechos de administrador.",
            "Non effectué : les droits d'administrateur n'ont pas été accordés.",
            "Não efetuado: não foram concedidos direitos de administrador.",
            "لم يتم التنفيذ: لم تُمنح صلاحيات المسؤول.",
            "नहीं हुआ: व्यवस्थापक अधिकार नहीं मिले।",
            "হয়নি: প্রশাসক অধিকার দেওয়া হয়নি।",
            "نہیں ہوا: ایڈمنسٹریٹر حقوق نہیں ملے۔",
            "未执行：未获得管理员权限。")

        Add("Не удалось выполнить действие. Подробности - в журнале службы.",
            "The action failed. See the service log for details.",
            "Не вдалося виконати дію. Подробиці - у журналі служби.",
            "Die Aktion ist fehlgeschlagen. Details stehen im Dienstprotokoll.",
            "Azione non riuscita. I dettagli sono nel log del servizio.",
            "La acción falló. Los detalles están en el registro del servicio.",
            "L'action a échoué. Les détails figurent dans le journal du service.",
            "A ação falhou. Os detalhes estão no registo do serviço.",
            "فشل تنفيذ الإجراء. التفاصيل في سجل الخدمة.",
            "कार्रवाई विफल रही। विवरण सेवा लॉग में हैं।",
            "কাজটি ব্যর্থ হয়েছে। বিস্তারিত সার্ভিস লগে আছে।",
            "کارروائی ناکام رہی۔ تفصیلات سروس لاگ میں ہیں۔",
            "操作失败。详情见服务日志。")

    End Sub

End Class
