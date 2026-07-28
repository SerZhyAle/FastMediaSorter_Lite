Option Strict On

' <summary>
' Strings of the OCR / translation tab: Ollama install and start, model pulling,
' language-pack download. See Localization.vb for the key convention.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddOcrStrings()

        Add("Ollama не установлен - нажмите «Установить Ollama».",
            "Ollama not installed - press Install Ollama.", "Ollama не встановлено - натисніть «Встановити Ollama».",
            "Ollama ist nicht installiert - klicken Sie auf ""Ollama installieren"".",
            "Ollama non è installato - premi Installa Ollama.",
            "Ollama no está instalado: pulse Instalar Ollama.",
            "Ollama n'est pas installé - cliquez sur Installer Ollama.",
            "Ollama não está instalado - clique em Instalar Ollama.",
            "‏Ollama غير مثبت - اضغط ""تثبيت Ollama"".", "Ollama इंस्टॉल नहीं है - ""Ollama इंस्टॉल करें"" दबाएँ।",
            "Ollama ইনস্টল করা নেই - ""Ollama ইনস্টল করুন"" চাপুন।",
            "‏Ollama انسٹال نہیں ہے - ""Ollama انسٹال کریں"" دبائیں۔", "未安装 Ollama - 请点击""安装 Ollama""。")
        Add("Ollama не запущен - нажмите «Запустить Ollama».",
            "Ollama not running - press Start Ollama.", "Ollama не запущено - натисніть «Запустити Ollama».",
            "Ollama läuft nicht - klicken Sie auf ""Ollama starten"".",
            "Ollama non è in esecuzione - premi Avvia Ollama.",
            "Ollama no se está ejecutando: pulse Iniciar Ollama.",
            "Ollama n'est pas lancé - cliquez sur Démarrer Ollama.",
            "Ollama não está em execução - clique em Iniciar Ollama.",
            "‏Ollama لا يعمل - اضغط ""تشغيل Ollama"".", "Ollama चल नहीं रहा - ""Ollama शुरू करें"" दबाएँ।",
            "Ollama চলছে না - ""Ollama চালু করুন"" চাপুন।",
            "‏Ollama نہیں چل رہا - ""Ollama شروع کریں"" دبائیں۔", "Ollama 未运行 - 请点击""启动 Ollama""。")
        Add("Ollama уже установлен. Нажмите «Запустить Ollama».",
            "Ollama is already installed. Press Start Ollama.",
            "Ollama вже встановлено. Натисніть «Запустити Ollama».",
            "Ollama ist bereits installiert. Klicken Sie auf ""Ollama starten"".",
            "Ollama è già installato. Premi Avvia Ollama.",
            "Ollama ya está instalado. Pulse Iniciar Ollama.",
            "Ollama est déjà installé. Cliquez sur Démarrer Ollama.",
            "Ollama já está instalado. Clique em Iniciar Ollama.",
            "‏Ollama مثبت بالفعل. اضغط ""تشغيل Ollama"".", "Ollama पहले से इंस्टॉल है। ""Ollama शुरू करें"" दबाएँ।",
            "Ollama আগে থেকেই ইনস্টল করা। ""Ollama চালু করুন"" চাপুন।",
            "‏Ollama پہلے سے انسٹال ہے۔ ""Ollama شروع کریں"" دبائیں۔", "Ollama 已安装。请点击""启动 Ollama""。")
        Add("Скачать и установить Ollama? Это несколько сотен МБ, загрузка может занять время.",
            "Download and install Ollama? This is several hundred MB and may take a while.",
            "Завантажити й встановити Ollama? Це кілька сотень МБ, завантаження може зайняти час.",
            "Ollama herunterladen und installieren? Das sind mehrere hundert MB und kann dauern.",
            "Scaricare e installare Ollama? Sono diverse centinaia di MB e può richiedere tempo.",
            "¿Descargar e instalar Ollama? Son varios cientos de MB y puede tardar.",
            "Télécharger et installer Ollama ? Plusieurs centaines de Mo, cela peut prendre du temps.",
            "Baixar e instalar o Ollama? São várias centenas de MB e pode demorar.",
            "هل تريد تنزيل Ollama وتثبيته؟ الحجم عدة مئات من الميغابايت وقد يستغرق وقتًا.",
            "Ollama डाउनलोड कर इंस्टॉल करें? यह कई सौ MB है और समय ले सकता है।",
            "Ollama ডাউনলোড করে ইনস্টল করবেন? এটি কয়েকশো MB, সময় লাগতে পারে।",
            "‏Ollama ڈاؤن لوڈ کر کے انسٹال کریں؟ یہ کئی سو MB ہے اور وقت لے سکتا ہے۔",
            "下载并安装 Ollama？体积为数百 MB，可能需要一些时间。")
        Add("Скачивание Ollama..", "Downloading Ollama..", "Завантаження Ollama..",
            "Ollama wird heruntergeladen..", "Download di Ollama..", "Descargando Ollama..",
            "Téléchargement d'Ollama..", "Baixando o Ollama..", "جارٍ تنزيل Ollama..",
            "Ollama डाउनलोड हो रहा है..", "Ollama ডাউনলোড হচ্ছে..", "Ollama ڈاؤن لوڈ ہو رہا ہے..",
            "正在下载 Ollama..")
        Add("Скачивание Ollama: ", "Downloading Ollama: ", "Завантаження Ollama: ",
            "Ollama wird heruntergeladen: ", "Download di Ollama: ", "Descargando Ollama: ",
            "Téléchargement d'Ollama : ", "Baixando o Ollama: ", "جارٍ تنزيل Ollama: ",
            "Ollama डाउनलोड हो रहा है: ", "Ollama ডাউনলোড হচ্ছে: ", "Ollama ڈاؤن لوڈ ہو رہا ہے: ",
            "正在下载 Ollama：")
        Add("Запуск установщика Ollama..", "Launching Ollama installer..", "Запуск інсталятора Ollama..",
            "Ollama-Installationsprogramm wird gestartet..", "Avvio dell'installer di Ollama..",
            "Iniciando el instalador de Ollama..", "Lancement de l'installateur Ollama..",
            "Iniciando o instalador do Ollama..", "تشغيل مثبّت Ollama..",
            "Ollama इंस्टॉलर शुरू हो रहा है..", "Ollama ইনস্টলার চালু হচ্ছে..",
            "Ollama انسٹالر شروع ہو رہا ہے..", "正在启动 Ollama 安装程序..")
        Add("Не удалось скачать. Открываю сайт Ollama..",
            "Download failed. Opening Ollama website..", "Не вдалося завантажити. Відкриваю сайт Ollama..",
            "Download fehlgeschlagen. Die Ollama-Website wird geöffnet..",
            "Download non riuscito. Apertura del sito di Ollama..",
            "Error en la descarga. Abriendo el sitio de Ollama..",
            "Échec du téléchargement. Ouverture du site Ollama..",
            "Falha no download. Abrindo o site do Ollama..", "فشل التنزيل. جارٍ فتح موقع Ollama..",
            "डाउनलोड विफल। Ollama की वेबसाइट खोली जा रही है..",
            "ডাউনলোড ব্যর্থ। Ollama-এর সাইট খোলা হচ্ছে..",
            "ڈاؤن لوڈ ناکام۔ Ollama کی ویب سائٹ کھولی جا رہی ہے..", "下载失败。正在打开 Ollama 网站..")
        Add("Запуск Ollama..", "Starting Ollama..", "Запуск Ollama..", "Ollama wird gestartet..",
            "Avvio di Ollama..", "Iniciando Ollama..", "Démarrage d'Ollama..", "Iniciando o Ollama..",
            "جارٍ تشغيل Ollama..", "Ollama शुरू हो रहा है..", "Ollama চালু হচ্ছে..",
            "Ollama شروع ہو رہا ہے..", "正在启动 Ollama..")
        Add("Ollama запущен.", "Ollama is running.", "Ollama запущено.", "Ollama läuft.",
            "Ollama è in esecuzione.", "Ollama está en ejecución.", "Ollama est en cours d'exécution.",
            "O Ollama está em execução.", "‏Ollama يعمل.", "Ollama चल रहा है।", "Ollama চলছে।",
            "‏Ollama چل رہا ہے۔", "Ollama 正在运行。")
        Add("Не удалось запустить Ollama.", "Could not start Ollama.", "Не вдалося запустити Ollama.",
            "Ollama konnte nicht gestartet werden.", "Impossibile avviare Ollama.",
            "No se pudo iniciar Ollama.", "Impossible de démarrer Ollama.",
            "Não foi possível iniciar o Ollama.", "تعذّر تشغيل Ollama.", "Ollama शुरू नहीं हो सका।",
            "Ollama চালু করা যায়নি।", "Ollama شروع نہ ہو سکا۔", "无法启动 Ollama。")
        Add("Укажите имя модели (например, llama3.2)",
            "Enter a model name (e.g. llama3.2)", "Вкажіть ім'я моделі (наприклад, llama3.2)",
            "Modellnamen eingeben (z. B. llama3.2)", "Inserisci il nome del modello (es. llama3.2)",
            "Introduzca el nombre del modelo (p. ej. llama3.2)",
            "Saisissez un nom de modèle (par ex. llama3.2)", "Digite o nome do modelo (ex.: llama3.2)",
            "أدخل اسم النموذج (مثل llama3.2)", "मॉडल का नाम दर्ज करें (जैसे llama3.2)",
            "মডেলের নাম লিখুন (যেমন llama3.2)", "ماڈل کا نام درج کریں (مثلاً llama3.2)",
            "输入模型名称（例如 llama3.2）")
        AddC("ollama", "Загрузка: ", "Pulling: ", "Завантаження: ", "Wird geholt: ", "Download: ",
            "Descargando: ", "Récupération : ", "Baixando: ", "جارٍ الجلب: ", "खींचा जा रहा है: ",
            "টানা হচ্ছে: ", "کھینچا جا رہا ہے: ", "正在拉取：")
        Add("Модель установлена: ", "Model installed: ", "Модель встановлено: ", "Modell installiert: ",
            "Modello installato: ", "Modelo instalado: ", "Modèle installé : ", "Modelo instalado: ",
            "تم تثبيت النموذج: ", "मॉडल इंस्टॉल हुआ: ", "মডেল ইনস্টল হয়েছে: ", "ماڈل انسٹال ہوا: ",
            "模型已安装：")
        Add("Не удалось загрузить модель (Ollama запущен?)",
            "Pull failed (is Ollama running?)", "Не вдалося завантажити модель (Ollama запущено?)",
            "Abruf fehlgeschlagen (läuft Ollama?)", "Download non riuscito (Ollama è in esecuzione?)",
            "Fallo al descargar (¿Ollama está en ejecución?)",
            "Échec de la récupération (Ollama est-il lancé ?)", "Falha ao baixar (o Ollama está em execução?)",
            "فشل الجلب (هل Ollama يعمل؟)", "खींचना विफल (क्या Ollama चल रहा है?)",
            "টানা ব্যর্থ (Ollama কি চলছে?)", "کھینچنا ناکام (کیا Ollama چل رہا ہے؟)",
            "拉取失败（Ollama 在运行吗？）")
        Add("OCR-движок не установлен.", "OCR runtime is not installed.", "OCR-рушій не встановлено.",
            "Die OCR-Laufzeit ist nicht installiert.", "Il motore OCR non è installato.",
            "El motor de OCR no está instalado.", "Le moteur OCR n'est pas installé.",
            "O mecanismo de OCR não está instalado.", "محرك OCR غير مثبت.",
            "OCR इंजन इंस्टॉल नहीं है।", "OCR ইঞ্জিন ইনস্টল করা নেই।", "OCR انجن انسٹال نہیں ہے۔",
            "未安装 OCR 引擎。")
        Add("Скачивание языкового пакета: ", "Downloading language pack: ", "Завантаження мовного пакета: ",
            "Sprachpaket wird heruntergeladen: ", "Download del pacchetto lingua: ",
            "Descargando el paquete de idioma: ", "Téléchargement du pack de langue : ",
            "Baixando o pacote de idioma: ", "جارٍ تنزيل حزمة اللغة: ", "भाषा पैक डाउनलोड हो रहा है: ",
            "ভাষা প্যাক ডাউনলোড হচ্ছে: ", "زبان پیک ڈاؤن لوڈ ہو رہا ہے: ", "正在下载语言包：")
        Add("Готово: пакет ", "Ready: pack ", "Готово: пакет ", "Fertig: Paket ", "Pronto: pacchetto ",
            "Listo: paquete ", "Prêt : pack ", "Pronto: pacote ", "جاهز: الحزمة ", "तैयार: पैक ",
            "প্রস্তুত: প্যাক ", "تیار: پیک ", "完成：语言包 ")
        Add("Не удалось скачать (нет сети?)", "Download failed (no network?)",
            "Не вдалося завантажити (немає мережі?)", "Download fehlgeschlagen (kein Netzwerk?)",
            "Download non riuscito (rete assente?)", "Error en la descarga (¿sin red?)",
            "Échec du téléchargement (pas de réseau ?)", "Falha no download (sem rede?)",
            "فشل التنزيل (لا يوجد اتصال؟)", "डाउनलोड विफल (नेटवर्क नहीं?)",
            "ডাউনলোড ব্যর্থ (নেটওয়ার্ক নেই?)", "ڈاؤن لوڈ ناکام (نیٹ ورک نہیں؟)", "下载失败（没有网络？）")
        Add("Управление SFTP-сервером для андроид-клиента..",
            "Manage the SFTP server for the Android client..",
            "Керування SFTP-сервером для андроїд-клієнта..",
            "SFTP-Server für den Android-Client verwalten..",
            "Gestisci il server SFTP per il client Android..",
            "Gestionar el servidor SFTP para el cliente Android..",
            "Gérer le serveur SFTP pour le client Android..",
            "Gerenciar o servidor SFTP para o cliente Android..",
            "إدارة خادم SFTP لعميل أندرويد..", "Android क्लाइंट के लिए SFTP सर्वर प्रबंधित करें..",
            "Android ক্লায়েন্টের জন্য SFTP সার্ভার পরিচালনা করুন..",
            "‏Android کلائنٹ کے لیے SFTP سرور کا انتظام کریں..", "管理面向 Android 客户端的 SFTP 服务器..")
    End Sub

End Class
