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

        Add("Служба работает от имени LOCAL SERVICE. Каждой раздаваемой папке нужно право на чтение для этой учётной записи - установщик выдаёт его при добавлении папки. Сетевые пути вида \\сервер\папка так работать не будут.",
            "The service runs as LOCAL SERVICE. Every served folder needs read access for that account - the installer grants it when the folder is added. UNC paths such as \\server\share will not work this way.",
            "Служба працює від імені LOCAL SERVICE. Кожній роздаваній папці потрібне право на читання для цього облікового запису - установник видає його під час додавання папки. Мережеві шляхи виду \\сервер\папка так працювати не будуть.",
            "Der Dienst läuft als LOCAL SERVICE. Jeder freigegebene Ordner braucht Leserechte für dieses Konto - der Installer vergibt sie beim Hinzufügen des Ordners. UNC-Pfade wie \\Server\Freigabe funktionieren so nicht.",
            "Il servizio viene eseguito come LOCAL SERVICE. Ogni cartella condivisa richiede l'accesso in lettura per quell'account: l'installer lo concede quando la cartella viene aggiunta. I percorsi UNC come \\server\condivisione non funzionano in questo modo.",
            "El servicio se ejecuta como LOCAL SERVICE. Cada carpeta compartida necesita acceso de lectura para esa cuenta: el instalador lo concede al añadir la carpeta. Las rutas UNC como \\servidor\recurso no funcionan así.",
            "Le service s'exécute en tant que LOCAL SERVICE. Chaque dossier partagé a besoin d'un accès en lecture pour ce compte : l'installeur l'accorde lors de l'ajout du dossier. Les chemins UNC comme \\serveur\partage ne fonctionnent pas ainsi.",
            "O serviço é executado como LOCAL SERVICE. Cada pasta partilhada precisa de acesso de leitura para essa conta - o instalador concede-o ao adicionar a pasta. Caminhos UNC como \\servidor\partilha não funcionam desta forma.",
            "تعمل الخدمة باسم LOCAL SERVICE. يحتاج كل مجلد مُشارَك إلى صلاحية قراءة لهذا الحساب - يمنحها المثبّت عند إضافة المجلد. مسارات UNC مثل \\server\share لن تعمل بهذه الطريقة.",
            "सेवा LOCAL SERVICE के रूप में चलती है। साझा की गई हर फ़ोल्डर को उस खाते के लिए पढ़ने की अनुमति चाहिए - फ़ोल्डर जोड़ते समय इंस्टॉलर वह दे देता है। \\server\share जैसे UNC पथ इस तरह काम नहीं करेंगे।",
            "সার্ভিসটি LOCAL SERVICE হিসেবে চলে। শেয়ার করা প্রতিটি ফোল্ডারের জন্য ওই অ্যাকাউন্টের পড়ার অনুমতি দরকার - ফোল্ডার যোগ করার সময় ইনস্টলার তা দেয়। \\server\share ধরনের UNC পাথ এভাবে কাজ করবে না।",
            "سروس LOCAL SERVICE کے طور پر چلتی ہے۔ ہر شیئر کیے گئے فولڈر کو اس اکاؤنٹ کے لیے پڑھنے کی اجازت درکار ہے - فولڈر شامل کرتے وقت انسٹالر یہ دیتا ہے۔ \\server\share جیسے UNC راستے اس طرح کام نہیں کریں گے۔",
            "该服务以 LOCAL SERVICE 身份运行。每个共享文件夹都需要授予该账户读取权限 - 添加文件夹时由安装程序授予。\\server\share 这类 UNC 路径无法以此方式工作。")

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
