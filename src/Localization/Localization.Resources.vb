Option Strict On

' <summary>
' Strings of remote resources (007: a saved remote place - an SFTP server, or a network
' share that needs a login - opened like a folder): the resource manager window, the edit
' form, the trust and password prompts, the file and clipboard import, and the honest
' failure sentences.
'
' Three of these carry more weight than the rest and must not be softened in any language:
'
'   - the host-key prompts are a security question. "Trust this server" and "the key has
'     changed" must never read as routine; the changed-key text names both fingerprints
'     and says plainly that it may be an impersonation.
'   - a failure sentence says what actually failed (the password, the folder, the port,
'     the network) and never folds two causes into one friendly line.
'   - the PIN refusal says the folder was NOT imported and why, rather than importing it
'     half-way.
'
' Compiled by both builds for the same reason Localization.Secure.vb is: the x86 exe shares
' the source tree even though it never reaches a remote resource.
'
' See Localization.vb for the key convention.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddResourceStrings()
        Add("Будет передано {0} через медленный или тарифицируемый ресурс. Между разными серверами файл сначала скачивается, затем загружается; проверка также расходует трафик. Продолжить?",
            "{0} will be transferred over a slow or metered resource. Between different servers, files are downloaded then uploaded; verification also uses data. Continue?",
            "Буде передано {0} через повільний або тарифікований ресурс. Між різними серверами файл спочатку завантажується на ПК, потім на сервер; перевірка також витрачає трафік. Продовжити?",
            "{0} werden über eine langsame oder kostenpflichtige Verbindung übertragen. Zwischen verschiedenen Servern werden Dateien herunter- und wieder hochgeladen; auch die Prüfung verbraucht Daten. Fortfahren?",
            "Verranno trasferiti {0} tramite una risorsa lenta o a consumo. Tra server diversi i file vengono scaricati e poi caricati; anche la verifica consuma dati. Continuare?",
            "Se transferirán {0} mediante un recurso lento o de uso medido. Entre servidores distintos, los archivos se descargan y luego se suben; la verificación también consume datos. ¿Continuar?",
            "{0} seront transférés via une ressource lente ou facturée à l’usage. Entre serveurs différents, les fichiers sont téléchargés puis envoyés ; la vérification consomme aussi des données. Continuer ?",
            "Serão transferidos {0} por um recurso lento ou com cobrança por uso. Entre servidores diferentes, os arquivos são baixados e depois enviados; a verificação também consome dados. Continuar?",
            "سيتم نقل {0} عبر مورد بطيء أو محدود الاستهلاك. بين خوادم مختلفة، تُنزّل الملفات ثم تُرفع؛ ويستهلك التحقق بيانات أيضًا. هل تريد المتابعة؟",
            "धीमे या सीमित डेटा वाले संसाधन पर {0} स्थानांतरित होगा। अलग सर्वरों के बीच फ़ाइल पहले डाउनलोड, फिर अपलोड होती है; जाँच भी डेटा इस्तेमाल करती है। जारी रखें?",
            "ধীর বা মিটারযুক্ত রিসোর্সে {0} স্থানান্তর হবে। ভিন্ন সার্ভারের মধ্যে ফাইল আগে ডাউনলোড, পরে আপলোড হয়; যাচাইও ডেটা ব্যবহার করে। চালিয়ে যাবেন?",
            "سست یا محدود ڈیٹا والے وسیلے پر {0} منتقل ہوگا۔ مختلف سروروں کے درمیان فائل پہلے ڈاؤن لوڈ، پھر اپ لوڈ ہوتی ہے؛ تصدیق بھی ڈیٹا استعمال کرتی ہے۔ جاری رکھیں؟",
            "将通过慢速或按流量计费的资源传输 {0}。在不同服务器之间，文件先下载再上传；验证也会消耗流量。继续？")

        ' --- the manager window ---------------------------------------------------

        Add("Ресурсы",
            "Resources", "Ресурси", "Ressourcen", "Risorse",
            "Recursos", "Ressources", "Recursos", "الموارد",
            "संसाधन", "রিসোর্স", "وسائل", "资源")

        Add("Управление ресурсами..",
            "Manage resources..", "Керування ресурсами..", "Ressourcen verwalten..", "Gestisci risorse..",
            "Administrar recursos..", "Gérer les ressources..", "Gerenciar recursos..", "إدارة الموارد..",
            "संसाधन प्रबंधित करें..", "রিসোর্স পরিচালনা..", "وسائل کا انتظام..", "管理资源..")

        Add("Здесь пока нет ресурсов. Добавьте сервер вручную или импортируйте файл настроек из Share Manager (.fmscfg).",
            "There are no resources here yet. Add a server manually or import a settings file from Share Manager (.fmscfg).",
            "Тут поки немає ресурсів. Додайте сервер вручну або імпортуйте файл налаштувань із Share Manager (.fmscfg).",
            "Hier gibt es noch keine Ressourcen. Fügen Sie einen Server manuell hinzu oder importieren Sie eine Einstellungsdatei aus Share Manager (.fmscfg).",
            "Qui non ci sono ancora risorse. Aggiungere un server manualmente o importare un file di impostazioni da Share Manager (.fmscfg).",
            "Aún no hay recursos. Añada un servidor manualmente o importe un archivo de configuración de Share Manager (.fmscfg).",
            "Il n'y a pas encore de ressources ici. Ajoutez un serveur manuellement ou importez un fichier de paramètres depuis Share Manager (.fmscfg).",
            "Ainda não há recursos aqui. Adicione um servidor manualmente ou importe um arquivo de configurações do Share Manager (.fmscfg).",
            "لا توجد موارد هنا بعد. أضف خادمًا يدويًا أو استورد ملف إعدادات من Share Manager (.fmscfg).",
            "यहाँ अभी कोई संसाधन नहीं है। सर्वर हाथ से जोड़ें या Share Manager से सेटिंग फ़ाइल (.fmscfg) आयात करें।",
            "এখানে এখনও কোনো রিসোর্স নেই। হাতে সার্ভার যোগ করুন অথবা Share Manager থেকে সেটিংস ফাইল (.fmscfg) আমদানি করুন।",
            "یہاں ابھی کوئی وسیلہ نہیں ہے۔ سرور ہاتھ سے شامل کریں یا Share Manager سے ترتیبات کی فائل (.fmscfg) درآمد کریں۔",
            "这里还没有资源。请手动添加服务器，或从 Share Manager 导入设置文件 (.fmscfg)。")

        Add("Название",
            "Name", "Назва", "Name", "Nome",
            "Nombre", "Nom", "Nome", "الاسم",
            "नाम", "নাম", "نام", "名称")

        Add("Тип:",
            "Type:", "Тип:", "Typ:", "Tipo:",
            "Tipo:", "Type :", "Tipo:", "النوع:",
            "प्रकार:", "ধরন:", "قسم:", "类型:")

        Add("Адрес",
            "Address", "Адреса", "Adresse", "Indirizzo",
            "Dirección", "Adresse", "Endereço", "العنوان",
            "पता", "ঠিকানা", "پتا", "地址")

        Add("Папка",
            "Folder", "Папка", "Ordner", "Cartella",
            "Carpeta", "Dossier", "Pasta", "المجلد",
            "फ़ोल्डर", "ফোল্ডার", "فولڈر", "文件夹")

        Add("Доступ",
            "Access", "Доступ", "Zugriff", "Accesso",
            "Acceso", "Accès", "Acesso", "الوصول",
            "पहुँच", "অ্যাক্সেস", "رسائی", "访问")

        Add("Открывался",
            "Last opened", "Відкривався", "Zuletzt geöffnet", "Ultima apertura",
            "Última apertura", "Dernière ouverture", "Última abertura", "آخر فتح",
            "आख़िरी बार खोला", "সর্বশেষ খোলা", "آخری بار کھولا", "上次打开")

        Add("только чтение",
            "read-only", "лише читання", "nur lesen", "sola lettura",
            "solo lectura", "lecture seule", "somente leitura", "للقراءة فقط",
            "केवल पढ़ने के लिए", "শুধু পড়া", "صرف پڑھنے کے لیے", "只读")

        Add("чтение и запись",
            "read and write", "читання й запис", "lesen und schreiben", "lettura e scrittura",
            "lectura y escritura", "lecture et écriture", "leitura e gravação", "للقراءة والكتابة",
            "पढ़ना और लिखना", "পড়া ও লেখা", "پڑھنا اور لکھنا", "读写")

        Add("Добавить..",
            "Add..", "Додати..", "Hinzufügen..", "Aggiungi..",
            "Añadir..", "Ajouter..", "Adicionar..", "إضافة..",
            "जोड़ें..", "যোগ করুন..", "شامل کریں..", "添加..")

        Add("Изменить..",
            "Edit..", "Змінити..", "Bearbeiten..", "Modifica..",
            "Editar..", "Modifier..", "Editar..", "تعديل..",
            "बदलें..", "সম্পাদনা..", "ترمیم..", "编辑..")

        Add("Подключиться",
            "Connect", "Підключитися", "Verbinden", "Connetti",
            "Conectar", "Se connecter", "Conectar", "اتصال",
            "कनेक्ट करें", "সংযুক্ত করুন", "کنیکٹ کریں", "连接")

        Add("Импорт из файла..",
            "Import from file..", "Імпорт із файлу..", "Aus Datei importieren..", "Importa da file..",
            "Importar de archivo..", "Importer depuis un fichier..", "Importar de arquivo..", "استيراد من ملف..",
            "फ़ाइल से आयात..", "ফাইল থেকে আমদানি..", "فائل سے درآمد..", "从文件导入..")

        Add("Импорт из буфера",
            "Import from clipboard", "Імпорт із буфера", "Aus Zwischenablage importieren", "Importa dagli appunti",
            "Importar del portapapeles", "Importer du presse-papiers", "Importar da área de transferência", "استيراد من الحافظة",
            "क्लिपबोर्ड से आयात", "ক্লিপবোর্ড থেকে আমদানি", "کلپ بورڈ سے درآمد", "从剪贴板导入")

        Add("Ресурс удалён",
            "Resource deleted", "Ресурс видалено", "Ressource gelöscht", "Risorsa eliminata",
            "Recurso eliminado", "Ressource supprimée", "Recurso excluído", "تم حذف المورد",
            "संसाधन हटा दिया गया", "রিসোর্স মুছে ফেলা হয়েছে", "وسیلہ حذف کر دیا گیا", "资源已删除")

        Add("Удалить ресурс «{0}»? Сохранённый для него пароль тоже будет удалён. Файлы на сервере не затрагиваются.",
            "Delete the resource «{0}»? The password saved for it will be deleted too. Files on the server are not affected.",
            "Видалити ресурс «{0}»? Збережений для нього пароль також буде видалено. Файли на сервері не зачіпаються.",
            "Ressource «{0}» löschen? Das dafür gespeicherte Kennwort wird ebenfalls gelöscht. Dateien auf dem Server bleiben unberührt.",
            "Eliminare la risorsa «{0}»? Verrà eliminata anche la password salvata per essa. I file sul server non vengono toccati.",
            "¿Eliminar el recurso «{0}»? También se eliminará la contraseña guardada para él. Los archivos del servidor no se tocan.",
            "Supprimer la ressource «{0}» ? Le mot de passe enregistré pour elle sera également supprimé. Les fichiers sur le serveur ne sont pas touchés.",
            "Excluir o recurso «{0}»? A senha salva para ele também será excluída. Os arquivos no servidor não são afetados.",
            "هل تريد حذف المورد «{0}»؟ ستُحذف أيضًا كلمة المرور المحفوظة له. لا تتأثر الملفات الموجودة على الخادم.",
            "क्या संसाधन «{0}» हटाएँ? उसके लिए सहेजा गया पासवर्ड भी हटा दिया जाएगा। सर्वर की फ़ाइलों पर कोई असर नहीं पड़ेगा।",
            "রিসোর্স «{0}» মুছবেন? এর জন্য সংরক্ষিত পাসওয়ার্ডও মুছে যাবে। সার্ভারের ফাইলে কোনো প্রভাব পড়বে না।",
            "وسیلہ «{0}» حذف کریں؟ اس کے لیے محفوظ پاس ورڈ بھی حذف ہو جائے گا۔ سرور کی فائلوں پر کوئی اثر نہیں پڑے گا۔",
            "要删除资源「{0}」吗？为它保存的密码也会一并删除。服务器上的文件不受影响。")

        Add("Не удалось удалить ресурс",
            "Could not delete the resource", "Не вдалося видалити ресурс", "Die Ressource konnte nicht gelöscht werden", "Impossibile eliminare la risorsa",
            "No se pudo eliminar el recurso", "Impossible de supprimer la ressource", "Não foi possível excluir o recurso", "تعذّر حذف المورد",
            "संसाधन हटाया नहीं जा सका", "রিসোর্স মুছে ফেলা যায়নি", "وسیلہ حذف نہیں ہو سکا", "无法删除资源")

        Add("Ресурс «{0}», пользователь {1}",
            "Resource «{0}», user {1}", "Ресурс «{0}», користувач {1}", "Ressource «{0}», Benutzer {1}", "Risorsa «{0}», utente {1}",
            "Recurso «{0}», usuario {1}", "Ressource «{0}», utilisateur {1}", "Recurso «{0}», usuário {1}", "المورد «{0}»، المستخدم {1}",
            "संसाधन «{0}», उपयोगकर्ता {1}", "রিসোর্স «{0}», ব্যবহারকারী {1}", "وسیلہ «{0}»، صارف {1}", "资源「{0}」，用户 {1}")

        ' --- the edit form --------------------------------------------------------

        Add("Новый ресурс",
            "New resource", "Новий ресурс", "Neue Ressource", "Nuova risorsa",
            "Nuevo recurso", "Nouvelle ressource", "Novo recurso", "مورد جديد",
            "नया संसाधन", "নতুন রিসোর্স", "نیا وسیلہ", "新建资源")

        Add("Изменить ресурс",
            "Edit resource", "Змінити ресурс", "Ressource bearbeiten", "Modifica risorsa",
            "Editar recurso", "Modifier la ressource", "Editar recurso", "تعديل المورد",
            "संसाधन बदलें", "রিসোর্স সম্পাদনা", "وسیلہ میں ترمیم", "编辑资源")

        Add("Новый сервер",
            "New server", "Новий сервер", "Neuer Server", "Nuovo server",
            "Nuevo servidor", "Nouveau serveur", "Novo servidor", "خادم جديد",
            "नया सर्वर", "নতুন সার্ভার", "نیا سرور", "新建服务器")

        Add("Название:",
            "Name:", "Назва:", "Name:", "Nome:",
            "Nombre:", "Nom :", "Nome:", "الاسم:",
            "नाम:", "নাম:", "نام:", "名称:")

        Add("SFTP-сервер",
            "SFTP server", "SFTP-сервер", "SFTP-Server", "Server SFTP",
            "Servidor SFTP", "Serveur SFTP", "Servidor SFTP", "خادم SFTP",
            "SFTP सर्वर", "SFTP সার্ভার", "SFTP سرور", "SFTP 服务器")

        Add("Сетевая папка (SMB)",
            "Network folder (SMB)", "Мережева папка (SMB)", "Netzwerkordner (SMB)", "Cartella di rete (SMB)",
            "Carpeta de red (SMB)", "Dossier réseau (SMB)", "Pasta de rede (SMB)", "مجلد شبكة (SMB)",
            "नेटवर्क फ़ोल्डर (SMB)", "নেটওয়ার্ক ফোল্ডার (SMB)", "نیٹ ورک فولڈر (SMB)", "网络文件夹 (SMB)")

        Add("Папка на SFTP-сервере или сетевая папка с паролем",
            "A folder on an SFTP server, or a network folder that needs a password",
            "Папка на SFTP-сервері або мережева папка з паролем",
            "Ordner auf einem SFTP-Server oder Netzwerkordner mit Kennwort",
            "Cartella su un server SFTP o cartella di rete con password",
            "Carpeta en un servidor SFTP o carpeta de red con contraseña",
            "Dossier sur un serveur SFTP ou dossier réseau avec mot de passe",
            "Pasta em um servidor SFTP ou pasta de rede com senha",
            "مجلد على خادم SFTP أو مجلد شبكة بكلمة مرور",
            "SFTP सर्वर पर फ़ोल्डर या पासवर्ड वाला नेटवर्क फ़ोल्डर",
            "SFTP সার্ভারের ফোল্ডার বা পাসওয়ার্ডসহ নেটওয়ার্ক ফোল্ডার",
            "SFTP سرور پر فولڈر یا پاس ورڈ والا نیٹ ورک فولڈر",
            "SFTP 服务器上的文件夹，或需要密码的网络文件夹")

        Add("Сервер:",
            "Server:", "Сервер:", "Server:", "Server:",
            "Servidor:", "Serveur :", "Servidor:", "الخادم:",
            "सर्वर:", "সার্ভার:", "سرور:", "服务器:")

        Add("Порт:",
            "Port:", "Порт:", "Port:", "Porta:",
            "Puerto:", "Port :", "Porta:", "المنفذ:",
            "पोर्ट:", "পোর্ট:", "پورٹ:", "端口:")

        Add("Пользователь:",
            "User:", "Користувач:", "Benutzer:", "Utente:",
            "Usuario:", "Utilisateur :", "Usuário:", "المستخدم:",
            "उपयोगकर्ता:", "ব্যবহারকারী:", "صارف:", "用户:")

        Add("Пароль:",
            "Password:", "Пароль:", "Kennwort:", "Password:",
            "Contraseña:", "Mot de passe :", "Senha:", "كلمة المرور:",
            "पासवर्ड:", "পাসওয়ার্ড:", "پاس ورڈ:", "密码:")

        Add("Папка на сервере:",
            "Folder on the server:", "Папка на сервері:", "Ordner auf dem Server:", "Cartella sul server:",
            "Carpeta en el servidor:", "Dossier sur le serveur :", "Pasta no servidor:", "المجلد على الخادم:",
            "सर्वर पर फ़ोल्डर:", "সার্ভারে ফোল্ডার:", "سرور پر فولڈر:", "服务器上的文件夹:")

        Add("Сетевая папка:",
            "Network folder:", "Мережева папка:", "Netzwerkordner:", "Cartella di rete:",
            "Carpeta de red:", "Dossier réseau :", "Pasta de rede:", "مجلد الشبكة:",
            "नेटवर्क फ़ोल्डर:", "নেটওয়ার্ক ফোল্ডার:", "نیٹ ورک فولڈر:", "网络文件夹:")

        Add("Ключ сервера:",
            "Server key:", "Ключ сервера:", "Serverschlüssel:", "Chiave del server:",
            "Clave del servidor:", "Clé du serveur :", "Chave do servidor:", "مفتاح الخادم:",
            "सर्वर की कुंजी:", "সার্ভারের কী:", "سرور کی کلید:", "服务器密钥:")

        Add("ещё не запомнен: появится при первом подключении",
            "not remembered yet: it will appear on the first connection",
            "ще не запам'ятований: з'явиться під час першого підключення",
            "noch nicht gemerkt: erscheint bei der ersten Verbindung",
            "non ancora memorizzata: comparirà alla prima connessione",
            "aún no recordada: aparecerá en la primera conexión",
            "pas encore mémorisée : elle apparaîtra à la première connexion",
            "ainda não lembrada: aparecerá na primeira conexão",
            "لم يُحفظ بعد: سيظهر عند أول اتصال",
            "अभी याद नहीं रखी गई: पहले कनेक्शन पर दिखाई देगी",
            "এখনও মনে রাখা হয়নি: প্রথম সংযোগে দেখা যাবে",
            "ابھی محفوظ نہیں: پہلے کنکشن پر ظاہر ہو گی",
            "尚未记住：首次连接时会显示")

        Add("Забыть ключ сервера..",
            "Forget the server key..", "Забути ключ сервера..", "Serverschlüssel vergessen..", "Dimentica la chiave del server..",
            "Olvidar la clave del servidor..", "Oublier la clé du serveur..", "Esquecer a chave do servidor..", "نسيان مفتاح الخادم..",
            "सर्वर की कुंजी भूल जाएँ..", "সার্ভারের কী ভুলে যান..", "سرور کی کلید بھول جائیں..", "忘记服务器密钥..")

        Add("Забыть ключ сервера",
            "Forget the server key", "Забути ключ сервера", "Serverschlüssel vergessen", "Dimentica la chiave del server",
            "Olvidar la clave del servidor", "Oublier la clé du serveur", "Esquecer a chave do servidor", "نسيان مفتاح الخادم",
            "सर्वर की कुंजी भूल जाएँ", "সার্ভারের কী ভুলে যান", "سرور کی کلید بھول جائیں", "忘记服务器密钥")

        Add("Запомнить пароль (защищён средствами Windows)",
            "Remember the password (protected by Windows)", "Запам'ятати пароль (захищено засобами Windows)",
            "Kennwort merken (durch Windows geschützt)", "Ricorda la password (protetta da Windows)",
            "Recordar la contraseña (protegida por Windows)", "Mémoriser le mot de passe (protégé par Windows)",
            "Lembrar a senha (protegida pelo Windows)", "تذكّر كلمة المرور (محمية بواسطة Windows)",
            "पासवर्ड याद रखें (Windows द्वारा सुरक्षित)", "পাসওয়ার্ড মনে রাখুন (Windows দ্বারা সুরক্ষিত)",
            "پاس ورڈ یاد رکھیں (Windows سے محفوظ)", "记住密码 (由 Windows 保护)")

        Add("Пусто - оставить сохранённый пароль",
            "Empty - keep the saved password", "Порожньо - залишити збережений пароль",
            "Leer - gespeichertes Kennwort behalten", "Vuoto - mantieni la password salvata",
            "Vacío - conservar la contraseña guardada", "Vide - conserver le mot de passe enregistré",
            "Vazio - manter a senha salva", "فارغ - الإبقاء على كلمة المرور المحفوظة",
            "खाली - सहेजा हुआ पासवर्ड रखें", "খালি - সংরক্ষিত পাসওয়ার্ড রাখুন",
            "خالی - محفوظ پاس ورڈ برقرار رکھیں", "留空 - 保留已保存的密码")

        Add("Только чтение (ничего не менять на сервере)",
            "Read-only (change nothing on the server)", "Лише читання (нічого не змінювати на сервері)",
            "Nur lesen (auf dem Server nichts ändern)", "Sola lettura (non modificare nulla sul server)",
            "Solo lectura (no cambiar nada en el servidor)", "Lecture seule (ne rien modifier sur le serveur)",
            "Somente leitura (não alterar nada no servidor)", "للقراءة فقط (دون تغيير أي شيء على الخادم)",
            "केवल पढ़ने के लिए (सर्वर पर कुछ न बदलें)", "শুধু পড়া (সার্ভারে কিছু পরিবর্তন করবেন না)",
            "صرف پڑھنے کے لیے (سرور پر کچھ نہ بدلیں)", "只读 (不更改服务器上的任何内容)")

        ' --- checking the server (the probe verdicts) -----------------------------

        Add("Проверить",
            "Check", "Перевірити", "Prüfen", "Verifica",
            "Comprobar", "Vérifier", "Verificar", "فحص",
            "जाँचें", "যাচাই করুন", "جانچیں", "检查")

        Add("Проверка..",
            "Checking..", "Перевірка..", "Prüfung..", "Verifica..",
            "Comprobando..", "Vérification..", "Verificando..", "جارٍ الفحص..",
            "जाँच हो रही है..", "যাচাই হচ্ছে..", "جانچ جاری ہے..", "检查中..")

        Add("Проверять нечего: не указан адрес сервера",
            "Nothing to check: no server address given", "Перевіряти нічого: не вказано адресу сервера",
            "Nichts zu prüfen: keine Serveradresse angegeben", "Niente da verificare: l'indirizzo del server non è indicato",
            "Nada que comprobar: no se ha indicado la dirección del servidor", "Rien à vérifier : aucune adresse de serveur indiquée",
            "Nada a verificar: o endereço do servidor não foi informado", "لا شيء للفحص: لم يُحدَّد عنوان الخادم",
            "जाँचने के लिए कुछ नहीं है: सर्वर का पता नहीं दिया गया", "যাচাই করার কিছু নেই: সার্ভারের ঠিকানা দেওয়া হয়নি",
            "جانچنے کو کچھ نہیں: سرور کا پتا نہیں دیا گیا", "没有可检查的内容：未填写服务器地址")

        Add("Сервер отвечает",
            "The server responds", "Сервер відповідає", "Der Server antwortet", "Il server risponde",
            "El servidor responde", "Le serveur répond", "O servidor responde", "الخادم يستجيب",
            "सर्वर जवाब दे रहा है", "সার্ভার সাড়া দিচ্ছে", "سرور جواب دے رہا ہے", "服务器有响应")

        Add("Папка открывается",
            "The folder opens", "Папка відкривається", "Der Ordner lässt sich öffnen", "La cartella si apre",
            "La carpeta se abre", "Le dossier s'ouvre", "A pasta abre", "يمكن فتح المجلد",
            "फ़ोल्डर खुल जाता है", "ফোল্ডারটি খোলা যায়", "فولڈر کھل جاتا ہے", "文件夹可以打开")

        Add("Порт открыт, но это не SSH-сервер",
            "The port is open, but it is not an SSH server", "Порт відкрито, але це не SSH-сервер",
            "Der Port ist offen, aber es ist kein SSH-Server", "La porta è aperta, ma non è un server SSH",
            "El puerto está abierto, pero no es un servidor SSH", "Le port est ouvert, mais ce n'est pas un serveur SSH",
            "A porta está aberta, mas não é um servidor SSH", "المنفذ مفتوح، لكنه ليس خادم SSH",
            "पोर्ट खुला है, लेकिन यह SSH सर्वर नहीं है", "পোর্ট খোলা, কিন্তু এটি SSH সার্ভার নয়",
            "پورٹ کھلا ہے، لیکن یہ SSH سرور نہیں ہے", "端口已开放，但这不是 SSH 服务器")

        ' --- what the form asks for ----------------------------------------------

        Add("Укажите адрес сервера (имя или IP)",
            "Enter the server address (name or IP)", "Вкажіть адресу сервера (ім'я або IP)",
            "Geben Sie die Serveradresse an (Name oder IP)", "Indicare l'indirizzo del server (nome o IP)",
            "Indique la dirección del servidor (nombre o IP)", "Indiquez l'adresse du serveur (nom ou IP)",
            "Informe o endereço do servidor (nome ou IP)", "أدخل عنوان الخادم (الاسم أو IP)",
            "सर्वर का पता बताएँ (नाम या IP)", "সার্ভারের ঠিকানা দিন (নাম বা IP)",
            "سرور کا پتا بتائیں (نام یا IP)", "请填写服务器地址 (名称或 IP)")

        Add("Укажите имя пользователя",
            "Enter the user name", "Вкажіть ім'я користувача", "Geben Sie den Benutzernamen an", "Indicare il nome utente",
            "Indique el nombre de usuario", "Indiquez le nom d'utilisateur", "Informe o nome de usuário", "أدخل اسم المستخدم",
            "उपयोगकर्ता नाम बताएँ", "ব্যবহারকারীর নাম দিন", "صارف کا نام بتائیں", "请填写用户名")

        Add("Укажите путь вида \\сервер\папка",
            "Enter a path like \\server\folder", "Вкажіть шлях виду \\сервер\папка",
            "Geben Sie einen Pfad wie \\Server\Ordner an", "Indicare un percorso del tipo \\server\cartella",
            "Indique una ruta del tipo \\servidor\carpeta", "Indiquez un chemin du type \\serveur\dossier",
            "Informe um caminho como \\servidor\pasta", "أدخل مسارًا بالشكل \\server\folder",
            "\\server\folder जैसा पथ बताएँ", "\\server\folder ধরনের পথ দিন",
            "\\server\folder جیسا راستہ بتائیں", "请填写形如 \\server\folder 的路径")

        Add("Порт - число от 1 до 65535",
            "Port is a number from 1 to 65535", "Порт - число від 1 до 65535",
            "Der Port ist eine Zahl von 1 bis 65535", "La porta è un numero da 1 a 65535",
            "El puerto es un número de 1 a 65535", "Le port est un nombre de 1 à 65535",
            "A porta é um número de 1 a 65535", "المنفذ رقم من 1 إلى 65535",
            "पोर्ट 1 से 65535 के बीच की संख्या है", "পোর্ট হলো 1 থেকে 65535 পর্যন্ত একটি সংখ্যা",
            "پورٹ 1 سے 65535 تک کا عدد ہے", "端口是 1 到 65535 之间的数字")

        Add("Папка на сервере указана неверно",
            "The folder on the server is specified incorrectly", "Папку на сервері вказано неправильно",
            "Der Ordner auf dem Server ist falsch angegeben", "La cartella sul server non è indicata correttamente",
            "La carpeta del servidor está mal indicada", "Le dossier sur le serveur est mal indiqué",
            "A pasta no servidor está indicada incorretamente", "المجلد على الخادم محدد بشكل غير صحيح",
            "सर्वर पर फ़ोल्डर ग़लत बताया गया है", "সার্ভারের ফোল্ডারটি ভুলভাবে দেওয়া হয়েছে",
            "سرور پر فولڈر غلط بتایا گیا ہے", "服务器上的文件夹填写有误")

        Add("Адрес ресурса не распознан",
            "The resource address was not recognized", "Адресу ресурсу не розпізнано",
            "Die Adresse der Ressource wurde nicht erkannt", "Indirizzo della risorsa non riconosciuto",
            "No se reconoce la dirección del recurso", "L'adresse de la ressource n'est pas reconnue",
            "O endereço do recurso não foi reconhecido", "لم يتم التعرف على عنوان المورد",
            "संसाधन का पता पहचाना नहीं गया", "রিসোর্সের ঠিকানা চেনা যায়নি",
            "وسیلے کا پتا پہچانا نہیں گیا", "无法识别资源地址")

        Add("Этот вид ресурса здесь не поддерживается",
            "This kind of resource is not supported here", "Цей вид ресурсу тут не підтримується",
            "Diese Art von Ressource wird hier nicht unterstützt", "Questo tipo di risorsa non è supportato qui",
            "Este tipo de recurso no se admite aquí", "Ce type de ressource n'est pas pris en charge ici",
            "Este tipo de recurso não é compatível aqui", "هذا النوع من الموارد غير مدعوم هنا",
            "इस प्रकार का संसाधन यहाँ समर्थित नहीं है", "এই ধরনের রিসোর্স এখানে সমর্থিত নয়",
            "اس قسم کا وسیلہ یہاں معاون نہیں ہے", "此处不支持这种资源")

        Add("Не удалось сохранить ресурс. Ничего не потеряно: то, что было сохранено раньше, осталось как было",
            "Could not save the resource. Nothing is lost: what was saved before is unchanged",
            "Не вдалося зберегти ресурс. Нічого не втрачено: те, що було збережено раніше, залишилося як було",
            "Die Ressource konnte nicht gespeichert werden. Nichts ist verloren: Was zuvor gespeichert war, ist unverändert geblieben",
            "Impossibile salvare la risorsa. Non si è perso nulla: ciò che era stato salvato prima è rimasto invariato",
            "No se pudo guardar el recurso. No se ha perdido nada: lo guardado antes sigue como estaba",
            "Impossible d'enregistrer la ressource. Rien n'est perdu : ce qui était enregistré auparavant est resté tel quel",
            "Não foi possível salvar o recurso. Nada foi perdido: o que estava salvo antes continua como estava",
            "تعذّر حفظ المورد. لم يضِع شيء: ما حُفظ سابقًا بقي كما كان",
            "संसाधन सहेजा नहीं जा सका। कुछ भी खोया नहीं है: पहले जो सहेजा गया था वह जैसा था वैसा ही है",
            "রিসোর্স সংরক্ষণ করা যায়নি। কিছুই হারায়নি: আগে যা সংরক্ষিত ছিল তা আগের মতোই আছে",
            "وسیلہ محفوظ نہیں ہو سکا۔ کچھ ضائع نہیں ہوا: جو پہلے محفوظ تھا وہ جوں کا توں ہے",
            "无法保存资源。没有任何损失：之前保存的内容保持原样")

        ' --- the password prompt --------------------------------------------------

        Add("Пароль ресурса",
            "Resource password", "Пароль ресурсу", "Ressourcenkennwort", "Password della risorsa",
            "Contraseña del recurso", "Mot de passe de la ressource", "Senha do recurso", "كلمة مرور المورد",
            "संसाधन का पासवर्ड", "রিসোর্সের পাসওয়ার্ড", "وسیلے کا پاس ورڈ", "资源密码")

        Add("Введите пароль",
            "Enter the password", "Введіть пароль", "Geben Sie das Kennwort ein", "Inserire la password",
            "Introduzca la contraseña", "Saisissez le mot de passe", "Digite a senha", "أدخل كلمة المرور",
            "पासवर्ड दर्ज करें", "পাসওয়ার্ড লিখুন", "پاس ورڈ درج کریں", "请输入密码")

        Add("Введите пароль для «{0}» (пользователь {1})",
            "Enter the password for «{0}» (user {1})", "Введіть пароль для «{0}» (користувач {1})",
            "Geben Sie das Kennwort für «{0}» ein (Benutzer {1})", "Inserire la password per «{0}» (utente {1})",
            "Introduzca la contraseña de «{0}» (usuario {1})", "Saisissez le mot de passe de «{0}» (utilisateur {1})",
            "Digite a senha de «{0}» (usuário {1})", "أدخل كلمة المرور لـ «{0}» (المستخدم {1})",
            "«{0}» के लिए पासवर्ड दर्ज करें (उपयोगकर्ता {1})", "«{0}»-এর পাসওয়ার্ড লিখুন (ব্যবহারকারী {1})",
            "«{0}» کا پاس ورڈ درج کریں (صارف {1})", "请输入「{0}」的密码 (用户 {1})")

        Add("Пароль не подошёл. Введите его ещё раз.",
            "The password was not accepted. Enter it again.", "Пароль не підійшов. Введіть його ще раз.",
            "Das Kennwort wurde nicht akzeptiert. Geben Sie es erneut ein.", "La password non è stata accettata. Inserirla di nuovo.",
            "La contraseña no es válida. Introdúzcala de nuevo.", "Le mot de passe n'a pas été accepté. Saisissez-le à nouveau.",
            "A senha não foi aceita. Digite-a novamente.", "لم تُقبل كلمة المرور. أدخلها مرة أخرى.",
            "पासवर्ड स्वीकार नहीं हुआ। उसे फिर से दर्ज करें।", "পাসওয়ার্ড গ্রহণ করা হয়নি। আবার লিখুন।",
            "پاس ورڈ قبول نہیں ہوا۔ اسے دوبارہ درج کریں۔", "密码不正确。请重新输入。")

        Add("Пароль не сохранён: введите его через «Изменить..»",
            "The password is not saved: enter it via «Edit..»", "Пароль не збережено: введіть його через «Змінити..»",
            "Kennwort nicht gespeichert: Geben Sie es über «Bearbeiten..» ein", "Password non salvata: inseriscila tramite «Modifica..»",
            "La contraseña no está guardada: introdúzcala mediante «Editar..»", "Mot de passe non enregistré : saisissez-le via «Modifier..»",
            "Senha não salva: digite-a em «Editar..»", "كلمة المرور غير محفوظة: أدخلها عبر «تعديل..»",
            "पासवर्ड सहेजा नहीं गया: उसे «बदलें..» से दर्ज करें", "পাসওয়ার্ড সংরক্ষিত নেই: «সম্পাদনা..» দিয়ে লিখুন",
            "پاس ورڈ محفوظ نہیں ہے: اسے «ترمیم..» کے ذریعے درج کریں", "密码未保存：请通过「编辑..」输入")

        ' --- the host key: the trust prompts and what the form says about it -----

        Add("Этот сервер программе ещё незнаком: {0}{1}Отпечаток его ключа:{1}{2}{1}{1}Сверьте его с отпечатком, который вам дал владелец сервера (в Share Manager он показан рядом с адресом). Доверять этому серверу и запомнить его ключ?",
            "This server is not yet known to the program: {0}{1}Its key fingerprint:{1}{2}{1}{1}Compare it with the fingerprint the server owner gave you (Share Manager shows it next to the address). Trust this server and remember its key?",
            "Цей сервер програмі ще не знайомий: {0}{1}Відбиток його ключа:{1}{2}{1}{1}Звірте його з відбитком, який вам дав власник сервера (у Share Manager він показаний поруч з адресою). Довіряти цьому серверу і запам'ятати його ключ?",
            "Dieser Server ist dem Programm noch unbekannt: {0}{1}Fingerabdruck seines Schlüssels:{1}{2}{1}{1}Vergleichen Sie ihn mit dem Fingerabdruck, den Ihnen der Serverbesitzer gegeben hat (Share Manager zeigt ihn neben der Adresse an). Diesem Server vertrauen und seinen Schlüssel merken?",
            "Questo server non è ancora noto al programma: {0}{1}Impronta della sua chiave:{1}{2}{1}{1}Confrontarla con l'impronta fornita dal proprietario del server (in Share Manager è mostrata accanto all'indirizzo). Considerare attendibile questo server e memorizzarne la chiave?",
            "Este servidor aún es desconocido para el programa: {0}{1}Huella de su clave:{1}{2}{1}{1}Compárela con la huella que le dio el propietario del servidor (Share Manager la muestra junto a la dirección). ¿Confiar en este servidor y recordar su clave?",
            "Ce serveur est encore inconnu du programme : {0}{1}Empreinte de sa clé :{1}{2}{1}{1}Comparez-la avec l'empreinte que vous a donnée le propriétaire du serveur (Share Manager l'affiche à côté de l'adresse). Faire confiance à ce serveur et mémoriser sa clé ?",
            "Este servidor ainda é desconhecido para o programa: {0}{1}Impressão digital da chave dele:{1}{2}{1}{1}Compare-a com a impressão digital que o proprietário do servidor lhe deu (o Share Manager a mostra ao lado do endereço). Confiar neste servidor e lembrar a chave dele?",
            "هذا الخادم غير معروف للبرنامج بعد: {0}{1}بصمة مفتاحه:{1}{2}{1}{1}قارنها بالبصمة التي أعطاك إياها مالك الخادم (يعرضها Share Manager بجوار العنوان). هل تثق بهذا الخادم وتحفظ مفتاحه؟",
            "यह सर्वर प्रोग्राम के लिए अभी अनजान है: {0}{1}उसकी कुंजी का फ़िंगरप्रिंट:{1}{2}{1}{1}इसे उस फ़िंगरप्रिंट से मिलाएँ जो सर्वर के मालिक ने आपको दिया है (Share Manager में वह पते के पास दिखता है)। क्या इस सर्वर पर भरोसा करें और उसकी कुंजी याद रखें?",
            "এই সার্ভার প্রোগ্রামের কাছে এখনও অপরিচিত: {0}{1}এর কী-এর ফিঙ্গারপ্রিন্ট:{1}{2}{1}{1}সার্ভারের মালিক আপনাকে যে ফিঙ্গারপ্রিন্ট দিয়েছেন তার সঙ্গে এটি মিলিয়ে নিন (Share Manager-এ এটি ঠিকানার পাশে দেখানো হয়)। এই সার্ভারকে বিশ্বাস করবেন এবং এর কী মনে রাখবেন?",
            "یہ سرور پروگرام کے لیے ابھی اجنبی ہے: {0}{1}اس کی کلید کا فنگر پرنٹ:{1}{2}{1}{1}اسے اس فنگر پرنٹ سے ملائیں جو سرور کے مالک نے آپ کو دیا ہے (Share Manager میں یہ پتے کے ساتھ دکھایا جاتا ہے)۔ کیا اس سرور پر بھروسا کریں اور اس کی کلید یاد رکھیں؟",
            "程序还不认识这台服务器：{0}{1}其密钥指纹：{1}{2}{1}{1}请将它与服务器所有者提供的指纹进行比对 (Share Manager 会在地址旁显示它)。要信任这台服务器并记住其密钥吗？")

        Add("Ключ сервера «{0}» не совпал с запомненным.{1}Запомнен: {2}{1}Предложен: {3}{1}{1}Это может быть переустановка сервера - а может быть подмена. Подключение отменено. Если вы уверены в сервере, откройте «Изменить..» в списке ресурсов и забудьте его ключ.",
            "The server key for «{0}» does not match the remembered one.{1}Remembered: {2}{1}Offered: {3}{1}{1}This may be a server reinstall - or it may be impersonation. The connection was cancelled. If you are sure about the server, open «Edit..» in the resource list and forget its key.",
            "Ключ сервера «{0}» не збігся із запам'ятованим.{1}Запам'ятований: {2}{1}Запропонований: {3}{1}{1}Це може бути перевстановлення сервера - а може бути підміна. Підключення скасовано. Якщо ви впевнені в сервері, відкрийте «Змінити..» у списку ресурсів і забудьте його ключ.",
            "Der Serverschlüssel von «{0}» stimmt nicht mit dem gemerkten überein.{1}Gemerkt: {2}{1}Angeboten: {3}{1}{1}Das kann eine Neuinstallation des Servers sein - oder eine Fälschung. Die Verbindung wurde abgebrochen. Wenn Sie dem Server vertrauen, öffnen Sie in der Ressourcenliste «Bearbeiten..» und vergessen Sie seinen Schlüssel.",
            "La chiave del server «{0}» non corrisponde a quella memorizzata.{1}Memorizzata: {2}{1}Proposta: {3}{1}{1}Potrebbe trattarsi di una reinstallazione del server - oppure di una falsificazione. La connessione è stata annullata. Se il server è attendibile, aprire «Modifica..» nell'elenco delle risorse e dimenticarne la chiave.",
            "La clave del servidor «{0}» no coincide con la recordada.{1}Recordada: {2}{1}Ofrecida: {3}{1}{1}Puede ser una reinstalación del servidor - o una suplantación. Se canceló la conexión. Si está seguro del servidor, abra «Editar..» en la lista de recursos y olvide su clave.",
            "La clé du serveur «{0}» ne correspond pas à celle mémorisée.{1}Mémorisée : {2}{1}Proposée : {3}{1}{1}Il peut s'agir d'une réinstallation du serveur - ou d'une usurpation. La connexion a été annulée. Si vous êtes sûr du serveur, ouvrez «Modifier..» dans la liste des ressources et oubliez sa clé.",
            "A chave do servidor «{0}» não corresponde à lembrada.{1}Lembrada: {2}{1}Oferecida: {3}{1}{1}Pode ser uma reinstalação do servidor - ou uma falsificação. A conexão foi cancelada. Se você tem certeza do servidor, abra «Editar..» na lista de recursos e esqueça a chave dele.",
            "مفتاح الخادم «{0}» لا يطابق المحفوظ.{1}المحفوظ: {2}{1}المعروض: {3}{1}{1}قد تكون هذه إعادة تثبيت للخادم - وقد تكون انتحالًا. تم إلغاء الاتصال. إذا كنت واثقًا من الخادم، فافتح «تعديل..» في قائمة الموارد وانسَ مفتاحه.",
            "«{0}» की सर्वर कुंजी याद रखी गई कुंजी से मेल नहीं खाती।{1}याद रखी गई: {2}{1}प्रस्तुत की गई: {3}{1}{1}यह सर्वर का पुनः इंस्टॉल हो सकता है - या कोई सर्वर का रूप धरकर आया हो सकता है। कनेक्शन रद्द किया गया। यदि आपको सर्वर पर भरोसा है, तो संसाधनों की सूची में «बदलें..» खोलें और उसकी कुंजी भुला दें।",
            "«{0}»-এর সার্ভার কী মনে রাখা কী-এর সঙ্গে মেলেনি।{1}মনে রাখা: {2}{1}প্রদর্শিত: {3}{1}{1}এটি সার্ভার পুনরায় ইনস্টল করার ফল হতে পারে - আবার ছদ্মবেশও হতে পারে। সংযোগ বাতিল করা হয়েছে। সার্ভারের ব্যাপারে নিশ্চিত হলে রিসোর্স তালিকায় «সম্পাদনা..» খুলে তার কী ভুলে যান।",
            "«{0}» کی سرور کلید محفوظ کردہ کلید سے مطابقت نہیں رکھتی۔{1}محفوظ کردہ: {2}{1}پیش کردہ: {3}{1}{1}یہ سرور کی دوبارہ تنصیب ہو سکتی ہے - یا کسی کا بہروپ بھی ہو سکتا ہے۔ کنکشن منسوخ کر دیا گیا۔ اگر آپ کو سرور پر یقین ہے تو وسائل کی فہرست میں «ترمیم..» کھولیں اور اس کی کلید بھلا دیں۔",
            "「{0}」的服务器密钥与已记住的不一致。{1}已记住：{2}{1}对方提供：{3}{1}{1}这可能是服务器重装，也可能是冒充。连接已取消。如果您确认该服务器可信，请在资源列表中打开「编辑..」并忘记它的密钥。")

        Add("Ключ сервера изменился",
            "Server key changed", "Ключ сервера змінився", "Serverschlüssel hat sich geändert", "La chiave del server è cambiata",
            "La clave del servidor ha cambiado", "La clé du serveur a changé", "A chave do servidor mudou", "تغيّر مفتاح الخادم",
            "सर्वर की कुंजी बदल गई", "সার্ভারের কী বদলে গেছে", "سرور کی کلید بدل گئی", "服务器密钥已更改")

        Add("Ключ сервера не совпал с запомненным - подключение отменено",
            "The server key does not match the remembered one - connection cancelled",
            "Ключ сервера не збігся із запам'ятованим - підключення скасовано",
            "Der Serverschlüssel stimmt nicht mit dem gemerkten überein - Verbindung abgebrochen",
            "La chiave del server non corrisponde a quella memorizzata - connessione annullata",
            "La clave del servidor no coincide con la recordada - conexión cancelada",
            "La clé du serveur ne correspond pas à celle mémorisée - connexion annulée",
            "A chave do servidor não corresponde à lembrada - conexão cancelada",
            "مفتاح الخادم لا يطابق المحفوظ - تم إلغاء الاتصال",
            "सर्वर की कुंजी याद रखी गई कुंजी से मेल नहीं खाती - कनेक्शन रद्द किया गया",
            "সার্ভারের কী মনে রাখা কী-এর সঙ্গে মেলেনি - সংযোগ বাতিল করা হয়েছে",
            "سرور کی کلید محفوظ کردہ کلید سے مطابقت نہیں رکھتی - کنکشن منسوخ کر دیا گیا",
            "服务器密钥与已记住的不一致 - 连接已取消")

        Add("Сейчас запомнен ключ:{0}{1}{0}{0}Если сервер переустановили, это нормально. Если нет - кто-то может выдавать себя за него. Забыть ключ и доверять новому при следующем подключении?",
            "The remembered key is currently:{0}{1}{0}{0}If the server was reinstalled, this is normal. If not, someone may be impersonating it. Forget the key and trust the new one on the next connection?",
            "Зараз запам'ятовано ключ:{0}{1}{0}{0}Якщо сервер перевстановили, це нормально. Якщо ні - хтось може видавати себе за нього. Забути ключ і довіряти новому під час наступного підключення?",
            "Aktuell gemerkter Schlüssel:{0}{1}{0}{0}Wenn der Server neu installiert wurde, ist das normal. Wenn nicht, gibt sich möglicherweise jemand als dieser Server aus. Schlüssel vergessen und beim nächsten Verbinden dem neuen vertrauen?",
            "Al momento è memorizzata la chiave:{0}{1}{0}{0}Se il server è stato reinstallato, è normale. In caso contrario, qualcuno potrebbe spacciarsi per esso. Dimenticare la chiave e considerare attendibile quella nuova alla prossima connessione?",
            "La clave recordada ahora es:{0}{1}{0}{0}Si el servidor se reinstaló, es normal. Si no, alguien podría estar suplantándolo. ¿Olvidar la clave y confiar en la nueva en la próxima conexión?",
            "La clé actuellement mémorisée est :{0}{1}{0}{0}Si le serveur a été réinstallé, c'est normal. Sinon, quelqu'un peut se faire passer pour lui. Oublier la clé et faire confiance à la nouvelle à la prochaine connexion ?",
            "A chave lembrada no momento é:{0}{1}{0}{0}Se o servidor foi reinstalado, isso é normal. Se não, alguém pode estar se passando por ele. Esquecer a chave e confiar na nova na próxima conexão?",
            "المفتاح المحفوظ حاليًا:{0}{1}{0}{0}إذا أُعيد تثبيت الخادم فهذا طبيعي. وإن لم يكن كذلك فقد ينتحل أحدٌ صفته. هل تريد نسيان المفتاح والوثوق بالجديد عند الاتصال التالي؟",
            "अभी याद रखी गई कुंजी:{0}{1}{0}{0}यदि सर्वर दोबारा इंस्टॉल किया गया है, तो यह सामान्य है। यदि नहीं, तो कोई उसका रूप धर सकता है। क्या कुंजी भुलाकर अगले कनेक्शन पर नई कुंजी पर भरोसा करें?",
            "এখন মনে রাখা কী:{0}{1}{0}{0}সার্ভার পুনরায় ইনস্টল করা হলে এটি স্বাভাবিক। না হলে কেউ সার্ভারের ছদ্মবেশ নিতে পারে। কী ভুলে গিয়ে পরের সংযোগে নতুনটিকে বিশ্বাস করবেন?",
            "اس وقت محفوظ کلید یہ ہے:{0}{1}{0}{0}اگر سرور دوبارہ نصب کیا گیا ہے تو یہ معمول کی بات ہے۔ اگر نہیں، تو کوئی اس کا بہروپ بھر سکتا ہے۔ کیا کلید بھلا دیں اور اگلے کنکشن پر نئی کلید پر بھروسا کریں؟",
            "当前已记住的密钥：{0}{1}{0}{0}如果服务器重装过，这是正常的；否则可能有人在冒充它。要忘记该密钥，并在下次连接时信任新的密钥吗？")

        ' --- import (file and clipboard) -------------------------------------------

        Add("Импорт ресурса",
            "Import resource", "Імпорт ресурсу", "Ressource importieren", "Importa risorsa",
            "Importar recurso", "Importer une ressource", "Importar recurso", "استيراد مورد",
            "संसाधन आयात", "রিসোর্স আমদানি", "وسیلہ درآمد کریں", "导入资源")

        Add("Файл настроек ресурса (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|Все файлы (*.*)|*.*",
            "Resource settings file (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|All files (*.*)|*.*",
            "Файл налаштувань ресурсу (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|Усі файли (*.*)|*.*",
            "Ressourcen-Einstellungsdatei (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|Alle Dateien (*.*)|*.*",
            "File di impostazioni della risorsa (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|Tutti i file (*.*)|*.*",
            "Archivo de configuración del recurso (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|Todos los archivos (*.*)|*.*",
            "Fichier de paramètres de ressource (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|Tous les fichiers (*.*)|*.*",
            "Arquivo de configurações do recurso (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|Todos os arquivos (*.*)|*.*",
            "ملف إعدادات المورد (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|كل الملفات (*.*)|*.*",
            "संसाधन की सेटिंग फ़ाइल (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|सभी फ़ाइलें (*.*)|*.*",
            "রিসোর্সের সেটিংস ফাইল (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|সব ফাইল (*.*)|*.*",
            "وسیلے کی ترتیبات کی فائل (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|تمام فائلیں (*.*)|*.*",
            "资源设置文件 (*.fmscfg;*.json;*.txt)|*.fmscfg;*.json;*.txt|所有文件 (*.*)|*.*")

        Add("В буфере обмена нет настроек ресурса",
            "The clipboard has no resource settings", "У буфері обміну немає налаштувань ресурсу",
            "Die Zwischenablage enthält keine Ressourceneinstellungen", "Negli appunti non ci sono impostazioni di risorsa",
            "El portapapeles no contiene la configuración de un recurso", "Le presse-papiers ne contient aucun paramètre de ressource",
            "A área de transferência não contém configurações de recurso", "لا توجد إعدادات مورد في الحافظة",
            "क्लिपबोर्ड में किसी संसाधन की सेटिंग नहीं है", "ক্লিপবোর্ডে কোনো রিসোর্সের সেটিংস নেই",
            "کلپ بورڈ میں کسی وسیلے کی ترتیبات نہیں ہیں", "剪贴板中没有资源设置")

        Add("Файл пуст",
            "The file is empty", "Файл порожній", "Die Datei ist leer", "Il file è vuoto",
            "El archivo está vacío", "Le fichier est vide", "O arquivo está vazio", "الملف فارغ",
            "फ़ाइल खाली है", "ফাইলটি খালি", "فائل خالی ہے", "文件为空")

        Add("Файл слишком велик для файла настроек ресурса",
            "The file is too large to be a resource settings file", "Файл завеликий для файлу налаштувань ресурсу",
            "Die Datei ist zu groß für eine Ressourceneinstellungsdatei", "Il file è troppo grande per essere un file di impostazioni di risorsa",
            "El archivo es demasiado grande para ser un archivo de configuración de recurso", "Le fichier est trop volumineux pour un fichier de paramètres de ressource",
            "O arquivo é grande demais para um arquivo de configurações de recurso", "الملف أكبر من أن يكون ملف إعدادات مورد",
            "फ़ाइल संसाधन की सेटिंग फ़ाइल होने के लिए बहुत बड़ी है", "ফাইলটি রিসোর্সের সেটিংস ফাইলের জন্য খুব বড়",
            "فائل وسیلے کی ترتیبات کی فائل ہونے کے لیے بہت بڑی ہے", "文件过大，不可能是资源设置文件")

        Add("Файл настроек повреждён или сделан не в Share Manager",
            "The settings file is damaged or was not made by Share Manager", "Файл налаштувань пошкоджено або створено не в Share Manager",
            "Die Einstellungsdatei ist beschädigt oder stammt nicht aus Share Manager", "Il file di impostazioni è danneggiato o non è stato creato da Share Manager",
            "El archivo de configuración está dañado o no se creó con Share Manager", "Le fichier de paramètres est endommagé ou n'a pas été créé par Share Manager",
            "O arquivo de configurações está danificado ou não foi criado pelo Share Manager", "ملف الإعدادات تالف أو لم يُنشأ بواسطة Share Manager",
            "सेटिंग फ़ाइल क्षतिग्रस्त है या Share Manager से नहीं बनी है", "সেটিংস ফাইলটি ক্ষতিগ্রস্ত অথবা Share Manager দিয়ে তৈরি নয়",
            "ترتیبات کی فائل خراب ہے یا Share Manager سے نہیں بنائی گئی", "设置文件已损坏，或不是由 Share Manager 创建的")

        Add("Файл создан более новой версией программы. Обновите Fast Media Sorter",
            "The file was created by a newer version of the program. Update Fast Media Sorter",
            "Файл створено новішою версією програми. Оновіть Fast Media Sorter",
            "Die Datei wurde mit einer neueren Version des Programms erstellt. Aktualisieren Sie Fast Media Sorter",
            "Il file è stato creato da una versione più recente del programma. Aggiornare Fast Media Sorter",
            "El archivo fue creado por una versión más reciente del programa. Actualice Fast Media Sorter",
            "Le fichier a été créé par une version plus récente du programme. Mettez à jour Fast Media Sorter",
            "O arquivo foi criado por uma versão mais recente do programa. Atualize o Fast Media Sorter",
            "أُنشئ الملف بإصدار أحدث من البرنامج. حدّث Fast Media Sorter",
            "फ़ाइल प्रोग्राम के नए संस्करण से बनी है। Fast Media Sorter को अपडेट करें",
            "ফাইলটি প্রোগ্রামের নতুন সংস্করণে তৈরি। Fast Media Sorter আপডেট করুন",
            "فائل پروگرام کے نئے ورژن سے بنائی گئی ہے۔ Fast Media Sorter کو اپ ڈیٹ کریں",
            "文件由更新版本的程序创建。请更新 Fast Media Sorter")

        Add("В файле не SFTP-ресурс, его нельзя открыть",
            "The file does not describe an SFTP resource, so it cannot be opened", "У файлі не SFTP-ресурс, його не можна відкрити",
            "Die Datei beschreibt keine SFTP-Ressource und kann nicht geöffnet werden", "Il file non descrive una risorsa SFTP, quindi non può essere aperta",
            "El archivo no describe un recurso SFTP, por lo que no se puede abrir", "Le fichier ne décrit pas une ressource SFTP, il ne peut donc pas être ouvert",
            "O arquivo não descreve um recurso SFTP, então não pode ser aberto", "الملف لا يصف مورد SFTP، لذا لا يمكن فتحه",
            "फ़ाइल में SFTP संसाधन नहीं है, इसलिए उसे खोला नहीं जा सकता", "ফাইলটিতে SFTP রিসোর্স নেই, তাই এটি খোলা যাবে না",
            "فائل میں SFTP وسیلہ نہیں ہے، اس لیے اسے کھولا نہیں جا سکتا", "文件中不是 SFTP 资源，无法打开")

        Add("В файле нет ни одной папки для импорта",
            "The file contains no folders to import", "У файлі немає жодної папки для імпорту",
            "Die Datei enthält keinen Ordner zum Importieren", "Il file non contiene nessuna cartella da importare",
            "El archivo no contiene ninguna carpeta para importar", "Le fichier ne contient aucun dossier à importer",
            "O arquivo não contém nenhuma pasta para importar", "لا يحتوي الملف على أي مجلد للاستيراد",
            "फ़ाइल में आयात करने के लिए कोई फ़ोल्डर नहीं है", "ফাইলে আমদানি করার মতো কোনো ফোল্ডার নেই",
            "فائل میں درآمد کرنے کے لیے کوئی فولڈر نہیں ہے", "文件中没有可导入的文件夹")

        Add("В файле нет подходящего адреса сервера",
            "The file has no usable server address", "У файлі немає придатної адреси сервера",
            "Die Datei enthält keine verwendbare Serveradresse", "Il file non contiene un indirizzo del server utilizzabile",
            "El archivo no contiene una dirección de servidor utilizable", "Le fichier ne contient aucune adresse de serveur utilisable",
            "O arquivo não contém um endereço de servidor utilizável", "لا يحتوي الملف على عنوان خادم صالح",
            "फ़ाइल में सर्वर का कोई उपयुक्त पता नहीं है", "ফাইলে সার্ভারের কোনো উপযুক্ত ঠিকানা নেই",
            "فائل میں سرور کا کوئی موزوں پتا نہیں ہے", "文件中没有可用的服务器地址")

        Add("Файл не импортирован: папка «{0}» защищена PIN-кодом, а Fast Media Sorter пока не умеет открывать такие ресурсы.",
            "The file was not imported: the folder «{0}» is protected by a PIN, and Fast Media Sorter cannot open such resources yet.",
            "Файл не імпортовано: папку «{0}» захищено PIN-кодом, а Fast Media Sorter поки що не вміє відкривати такі ресурси.",
            "Die Datei wurde nicht importiert: Der Ordner «{0}» ist durch eine PIN geschützt, und Fast Media Sorter kann solche Ressourcen noch nicht öffnen.",
            "Il file non è stato importato: la cartella «{0}» è protetta da un PIN e Fast Media Sorter non sa ancora aprire queste risorse.",
            "El archivo no se importó: la carpeta «{0}» está protegida con un PIN y Fast Media Sorter aún no sabe abrir esos recursos.",
            "Le fichier n'a pas été importé : le dossier «{0}» est protégé par un PIN, et Fast Media Sorter ne sait pas encore ouvrir de telles ressources.",
            "O arquivo não foi importado: a pasta «{0}» está protegida por um PIN e o Fast Media Sorter ainda não sabe abrir esses recursos.",
            "لم يُستورد الملف: المجلد «{0}» محمي برمز PIN، ولا يستطيع Fast Media Sorter فتح مثل هذه الموارد حتى الآن.",
            "फ़ाइल आयात नहीं हुई: फ़ोल्डर «{0}» PIN से सुरक्षित है, और Fast Media Sorter अभी ऐसे संसाधन खोलना नहीं जानता।",
            "ফাইল আমদানি হয়নি: «{0}» ফোল্ডারটি PIN দিয়ে সুরক্ষিত, আর Fast Media Sorter এখনও এমন রিসোর্স খুলতে পারে না।",
            "فائل درآمد نہیں ہوئی: فولڈر «{0}» PIN سے محفوظ ہے، اور Fast Media Sorter ابھی ایسے وسائل کھولنا نہیں جانتا۔",
            "文件未导入：文件夹「{0}」受 PIN 保护，而 Fast Media Sorter 目前还无法打开此类资源。")

        Add("Ключ сервера в файле отличается от запомненного.{0}Запомнен: {1}{0}В файле: {2}{0}{0}Заменить его? Если вы не ждали этого, ответьте «Нет».",
            "The server key in the file differs from the remembered one.{0}Remembered: {1}{0}In the file: {2}{0}{0}Replace it? If you were not expecting this, answer «No».",
            "Ключ сервера у файлі відрізняється від запам'ятованого.{0}Запам'ятований: {1}{0}У файлі: {2}{0}{0}Замінити його? Якщо ви цього не очікували, відповідайте «Ні».",
            "Der Serverschlüssel in der Datei weicht vom gemerkten ab.{0}Gemerkt: {1}{0}In der Datei: {2}{0}{0}Ersetzen? Wenn Sie das nicht erwartet haben, antworten Sie mit «Nein».",
            "La chiave del server nel file è diversa da quella memorizzata.{0}Memorizzata: {1}{0}Nel file: {2}{0}{0}Sostituirla? Se non era prevedibile, rispondere «No».",
            "La clave del servidor del archivo es distinta de la recordada.{0}Recordada: {1}{0}En el archivo: {2}{0}{0}¿Reemplazarla? Si no lo esperaba, responda «No».",
            "La clé du serveur dans le fichier diffère de celle mémorisée.{0}Mémorisée : {1}{0}Dans le fichier : {2}{0}{0}La remplacer ? Si vous ne vous y attendiez pas, répondez «Non».",
            "A chave do servidor no arquivo é diferente da lembrada.{0}Lembrada: {1}{0}No arquivo: {2}{0}{0}Substituí-la? Se você não esperava isso, responda «Não».",
            "مفتاح الخادم في الملف يختلف عن المحفوظ.{0}المحفوظ: {1}{0}في الملف: {2}{0}{0}هل تريد استبداله؟ إذا لم تكن تتوقع ذلك، فأجب بـ «لا».",
            "फ़ाइल में सर्वर की कुंजी याद रखी गई कुंजी से अलग है।{0}याद रखी गई: {1}{0}फ़ाइल में: {2}{0}{0}क्या उसे बदलें? यदि आपको इसकी अपेक्षा नहीं थी, तो «नहीं» चुनें।",
            "ফাইলের সার্ভার কী মনে রাখা কী থেকে আলাদা।{0}মনে রাখা: {1}{0}ফাইলে: {2}{0}{0}এটি কি বদলাবেন? আপনি এমনটা আশা না করলে «না» বলুন।",
            "فائل میں سرور کی کلید محفوظ کردہ کلید سے مختلف ہے۔{0}محفوظ کردہ: {1}{0}فائل میں: {2}{0}{0}کیا اسے بدل دیں؟ اگر آپ کو اس کی توقع نہیں تھی تو «نہیں» کہیں۔",
            "文件中的服务器密钥与已记住的不同。{0}已记住：{1}{0}文件中：{2}{0}{0}要替换吗？如果这不在您的预期之内，请选择「否」。")

        Add("Ключ сервера есть в файле: он будет проверен уже при первом подключении.",
            "The file contains the server key: it will be checked on the very first connection.",
            "Ключ сервера є у файлі: його буде перевірено вже під час першого підключення.",
            "Der Serverschlüssel steht in der Datei: Er wird schon bei der ersten Verbindung geprüft.",
            "La chiave del server è nel file: verrà verificata già alla prima connessione.",
            "El archivo contiene la clave del servidor: se comprobará ya en la primera conexión.",
            "Le fichier contient la clé du serveur : elle sera vérifiée dès la première connexion.",
            "O arquivo contém a chave do servidor: ela será verificada já na primeira conexão.",
            "مفتاح الخادم موجود في الملف: سيتم التحقق منه منذ أول اتصال.",
            "फ़ाइल में सर्वर की कुंजी है: उसकी जाँच पहले कनेक्शन पर ही हो जाएगी।",
            "ফাইলে সার্ভারের কী আছে: প্রথম সংযোগেই সেটি যাচাই করা হবে।",
            "فائل میں سرور کی کلید موجود ہے: پہلے ہی کنکشن پر اس کی جانچ ہو جائے گی۔",
            "文件中包含服务器密钥：首次连接时就会对其进行校验。")

        Add("Ключа сервера в файле нет: при первом подключении вы увидите его отпечаток и решите, доверять ли ему.",
            "The file has no server key: on the first connection you will see its fingerprint and decide whether to trust it.",
            "Ключа сервера у файлі немає: під час першого підключення ви побачите його відбиток і вирішите, чи довіряти йому.",
            "Die Datei enthält keinen Serverschlüssel: Bei der ersten Verbindung sehen Sie seinen Fingerabdruck und entscheiden, ob Sie ihm vertrauen.",
            "Il file non contiene la chiave del server: alla prima connessione verrà mostrata la sua impronta e potrà decidere se considerarla attendibile.",
            "El archivo no contiene la clave del servidor: en la primera conexión verá su huella y decidirá si confiar en ella.",
            "Le fichier ne contient pas la clé du serveur : à la première connexion, vous verrez son empreinte et déciderez si vous lui faites confiance.",
            "O arquivo não contém a chave do servidor: na primeira conexão você verá a impressão digital dela e decidirá se confia nela.",
            "لا يحتوي الملف على مفتاح الخادم: عند أول اتصال سترى بصمته وتقرر ما إذا كنت تثق به.",
            "फ़ाइल में सर्वर की कुंजी नहीं है: पहले कनेक्शन पर आपको उसका फ़िंगरप्रिंट दिखेगा और आप तय करेंगे कि उस पर भरोसा करना है या नहीं।",
            "ফাইলে সার্ভারের কী নেই: প্রথম সংযোগে আপনি তার ফিঙ্গারপ্রিন্ট দেখবেন এবং সেটিকে বিশ্বাস করবেন কি না তা ঠিক করবেন।",
            "فائل میں سرور کی کلید نہیں ہے: پہلے کنکشن پر آپ کو اس کا فنگر پرنٹ نظر آئے گا اور آپ طے کریں گے کہ اس پر بھروسا کرنا ہے یا نہیں۔",
            "文件中没有服务器密钥：首次连接时您会看到它的指纹，并决定是否信任它。")

        Add("Добавлено или обновлено ресурсов: {0}",
            "Resources added or updated: {0}", "Додано або оновлено ресурсів: {0}",
            "Hinzugefügte oder aktualisierte Ressourcen: {0}", "Risorse aggiunte o aggiornate: {0}",
            "Recursos añadidos o actualizados: {0}", "Ressources ajoutées ou mises à jour : {0}",
            "Recursos adicionados ou atualizados: {0}", "الموارد المضافة أو المحدَّثة: {0}",
            "जोड़े या अपडेट किए गए संसाधन: {0}", "যোগ করা বা আপডেট করা রিসোর্স: {0}",
            "شامل یا اپ ڈیٹ کیے گئے وسائل: {0}", "已添加或更新的资源数：{0}")

        Add("(новый)",
            "(new)", "(новий)", "(neu)", "(nuovo)",
            "(nuevo)", "(nouveau)", "(novo)", "(جديد)",
            "(नया)", "(নতুন)", "(نیا)", "(新增)")

        Add("(обновить)",
            "(update)", "(оновити)", "(aktualisieren)", "(aggiorna)",
            "(actualizar)", "(mettre à jour)", "(atualizar)", "(تحديث)",
            "(अपडेट)", "(আপডেট)", "(اپ ڈیٹ)", "(更新)")

        Add("Не удалось сохранить файл на этом компьютере",
            "Could not save the file on this computer", "Не вдалося зберегти файл на цьому комп'ютері",
            "Die Datei konnte auf diesem Computer nicht gespeichert werden", "Impossibile salvare il file su questo computer",
            "No se pudo guardar el archivo en este equipo", "Impossible d'enregistrer le fichier sur cet ordinateur",
            "Não foi possível salvar o arquivo neste computador", "تعذّر حفظ الملف على هذا الكمبيوتر",
            "इस कंप्यूटर पर फ़ाइल सहेजी नहीं जा सकी", "এই কম্পিউটারে ফাইলটি সংরক্ষণ করা যায়নি",
            "اس کمپیوٹر پر فائل محفوظ نہیں ہو سکی", "无法在这台计算机上保存文件")

        ' --- connecting: progress and honest failures -----------------------------

        Add("Подключение к «{0}»..",
            "Connecting to «{0}»..", "Підключення до «{0}»..", "Verbindung mit «{0}»..", "Connessione a «{0}»..",
            "Conectando con «{0}»..", "Connexion à «{0}»..", "Conectando a «{0}»..", "جارٍ الاتصال بـ «{0}»..",
            "«{0}» से कनेक्ट हो रहा है..", "«{0}»-এর সঙ্গে সংযোগ হচ্ছে..", "«{0}» سے کنیکٹ ہو رہا ہے..", "正在连接「{0}」..")

        Add("Подключение отменено",
            "Connection cancelled", "Підключення скасовано", "Verbindung abgebrochen", "Connessione annullata",
            "Conexión cancelada", "Connexion annulée", "Conexão cancelada", "تم إلغاء الاتصال",
            "कनेक्शन रद्द किया गया", "সংযোগ বাতিল করা হয়েছে", "کنکشن منسوخ کر دیا گیا", "连接已取消")

        Add("«{0}»: {1}",
            "«{0}»: {1}", "«{0}»: {1}", "«{0}»: {1}", "«{0}»: {1}",
            "«{0}»: {1}", "«{0}» : {1}", "«{0}»: {1}", "«{0}»: {1}",
            "«{0}»: {1}", "«{0}»: {1}", "«{0}»: {1}", "「{0}」: {1}")

        Add("Не удалось подключиться к ресурсу",
            "Could not connect to the resource", "Не вдалося підключитися до ресурсу", "Verbindung zur Ressource fehlgeschlagen", "Impossibile connettersi alla risorsa",
            "No se pudo conectar con el recurso", "Impossible de se connecter à la ressource", "Não foi possível conectar ao recurso", "تعذّر الاتصال بالمورد",
            "संसाधन से कनेक्ट नहीं हो सका", "রিসোর্সের সঙ্গে সংযোগ করা যায়নি", "وسیلے سے رابطہ نہیں ہو سکا", "无法连接到资源")

        Add("Сервер не ответил вовремя",
            "The server did not respond in time", "Сервер не відповів вчасно", "Der Server hat nicht rechtzeitig geantwortet", "Il server non ha risposto in tempo",
            "El servidor no respondió a tiempo", "Le serveur n'a pas répondu à temps", "O servidor não respondeu a tempo", "لم يستجب الخادم في الوقت المناسب",
            "सर्वर ने समय पर जवाब नहीं दिया", "সার্ভার সময়মতো সাড়া দেয়নি", "سرور نے وقت پر جواب نہیں دیا", "服务器未及时响应")

        Add("Сервер не отвечает или связь оборвалась",
            "The server is not responding or the connection was lost", "Сервер не відповідає або зв'язок обірвався",
            "Der Server antwortet nicht oder die Verbindung wurde unterbrochen", "Il server non risponde oppure la connessione si è interrotta",
            "El servidor no responde o se interrumpió la conexión", "Le serveur ne répond pas ou la connexion a été interrompue",
            "O servidor não responde ou a conexão foi interrompida", "الخادم لا يستجيب أو انقطع الاتصال",
            "सर्वर जवाब नहीं दे रहा या कनेक्शन टूट गया", "সার্ভার সাড়া দিচ্ছে না বা সংযোগ বিচ্ছিন্ন হয়ে গেছে",
            "سرور جواب نہیں دے رہا یا کنکشن ٹوٹ گیا", "服务器没有响应，或连接已中断")

        Add("Сервер недоступен: подключение отклонено",
            "Server unavailable: connection refused", "Сервер недоступний: підключення відхилено",
            "Server nicht erreichbar: Verbindung abgelehnt", "Server non raggiungibile: connessione rifiutata",
            "Servidor no disponible: conexión rechazada", "Serveur inaccessible : connexion refusée",
            "Servidor indisponível: conexão recusada", "الخادم غير متاح: تم رفض الاتصال",
            "सर्वर उपलब्ध नहीं: कनेक्शन अस्वीकार किया गया", "সার্ভার পাওয়া যাচ্ছে না: সংযোগ প্রত্যাখ্যাত হয়েছে",
            "سرور دستیاب نہیں: کنکشن مسترد کر دیا گیا", "服务器不可用：连接被拒绝")

        Add("Сервер не принял логин или пароль",
            "The server rejected the login or password", "Сервер не прийняв логін або пароль",
            "Der Server hat den Benutzernamen oder das Kennwort abgelehnt", "Il server ha rifiutato il nome utente o la password",
            "El servidor rechazó el usuario o la contraseña", "Le serveur a refusé l'identifiant ou le mot de passe",
            "O servidor rejeitou o usuário ou a senha", "رفض الخادم اسم المستخدم أو كلمة المرور",
            "सर्वर ने उपयोगकर्ता नाम या पासवर्ड स्वीकार नहीं किया", "সার্ভার ব্যবহারকারীর নাম বা পাসওয়ার্ড গ্রহণ করেনি",
            "سرور نے صارف نام یا پاس ورڈ قبول نہیں کیا", "服务器拒绝了用户名或密码")

        Add("Сервер не разрешает доступ к этой папке",
            "The server does not allow access to this folder", "Сервер не дозволяє доступ до цієї папки",
            "Der Server erlaubt keinen Zugriff auf diesen Ordner", "Il server non consente l'accesso a questa cartella",
            "El servidor no permite el acceso a esta carpeta", "Le serveur n'autorise pas l'accès à ce dossier",
            "O servidor não permite o acesso a esta pasta", "لا يسمح الخادم بالوصول إلى هذا المجلد",
            "सर्वर इस फ़ोल्डर तक पहुँच की अनुमति नहीं देता", "সার্ভার এই ফোল্ডারে অ্যাক্সেসের অনুমতি দেয় না",
            "سرور اس فولڈر تک رسائی کی اجازت نہیں دیتا", "服务器不允许访问此文件夹")

        Add("Сервер отклонил запрос",
            "The server rejected the request", "Сервер відхилив запит", "Der Server hat die Anfrage abgelehnt", "Il server ha rifiutato la richiesta",
            "El servidor rechazó la solicitud", "Le serveur a rejeté la requête", "O servidor rejeitou a solicitação", "رفض الخادم الطلب",
            "सर्वर ने अनुरोध अस्वीकार कर दिया", "সার্ভার অনুরোধ প্রত্যাখ্যান করেছে", "سرور نے درخواست مسترد کر دی", "服务器拒绝了请求")

        Add("На сервере нет такой папки",
            "There is no such folder on the server", "На сервері немає такої папки", "Auf dem Server gibt es diesen Ordner nicht", "Sul server non esiste questa cartella",
            "No existe esa carpeta en el servidor", "Ce dossier n'existe pas sur le serveur", "Não existe essa pasta no servidor", "لا يوجد مثل هذا المجلد على الخادم",
            "सर्वर पर ऐसा कोई फ़ोल्डर नहीं है", "সার্ভারে এমন কোনো ফোল্ডার নেই", "سرور پر ایسا کوئی فولڈر نہیں ہے", "服务器上没有这个文件夹")

        Add("Windows уже держит подключение к этому серверу под другим именем. Отключите его (net use) и повторите",
            "Windows already holds a connection to this server under a different user name. Disconnect it (net use) and try again",
            "Windows уже тримає підключення до цього сервера під іншим іменем. Від'єднайте його (net use) і повторіть",
            "Windows hat bereits eine Verbindung zu diesem Server unter einem anderen Benutzernamen. Trennen Sie sie (net use) und versuchen Sie es erneut",
            "Windows mantiene già una connessione a questo server con un altro nome utente. Disconnetterla (net use) e riprovare",
            "Windows ya mantiene una conexión con este servidor con otro nombre de usuario. Desconéctela (net use) y vuelva a intentarlo",
            "Windows maintient déjà une connexion à ce serveur sous un autre nom d'utilisateur. Déconnectez-la (net use) puis réessayez",
            "O Windows já mantém uma conexão com este servidor com outro nome de usuário. Desconecte-a (net use) e tente novamente",
            "يحتفظ Windows بالفعل باتصال بهذا الخادم باسم مستخدم آخر. افصل الاتصال (net use) ثم أعد المحاولة",
            "Windows पहले से इस सर्वर से किसी दूसरे उपयोगकर्ता नाम से जुड़ा हुआ है। उस कनेक्शन को हटाएँ (net use) और फिर से प्रयास करें",
            "Windows ইতিমধ্যে অন্য ব্যবহারকারীর নামে এই সার্ভারের সঙ্গে একটি সংযোগ ধরে রেখেছে। সেটি বিচ্ছিন্ন করুন (net use) এবং আবার চেষ্টা করুন",
            "Windows پہلے ہی اس سرور سے کسی دوسرے صارف نام کے تحت جڑا ہوا ہے۔ اس کنکشن کو منقطع کریں (net use) اور دوبارہ کوشش کریں",
            "Windows 已用另一个用户名连接到此服务器。请断开该连接 (net use) 后重试")

        ' --- refreshing an open resource ------------------------------------------

        Add("Обновление списка «{0}»..",
            "Refreshing the list of «{0}»..", "Оновлення списку «{0}»..", "Liste von «{0}» wird aktualisiert..", "Aggiornamento dell'elenco di «{0}»..",
            "Actualizando la lista de «{0}»..", "Actualisation de la liste de «{0}»..", "Atualizando a lista de «{0}»..", "جارٍ تحديث قائمة «{0}»..",
            "«{0}» की सूची ताज़ा की जा रही है..", "«{0}»-এর তালিকা হালনাগাদ হচ্ছে..", "«{0}» کی فہرست تازہ کی جا رہی ہے..", "正在刷新「{0}」的列表..")
        ' --- a server as a destination (not yet) ----------------------------------

        ' --- a recipient slot that names a folder of a saved resource (007 §4.2) --------

        Add("! Каталог {0}: этого сервера нет в списке ресурсов - добавьте его через «Ресурсы»",
            "! Folder {0}: this server is not in the resource list - add it under «Resources»",
            "! Каталог {0}: цього сервера немає в списку ресурсів - додайте його через «Ресурси»",
            "! Ordner {0}: Dieser Server ist nicht in der Ressourcenliste - fügen Sie ihn unter «Ressourcen» hinzu",
            "! Cartella {0}: questo server non è nell'elenco delle risorse - aggiungilo da «Risorse»",
            "! Carpeta {0}: este servidor no está en la lista de recursos - añádalo en «Recursos»",
            "! Dossier {0} : ce serveur n'est pas dans la liste des ressources - ajoutez-le dans «Ressources»",
            "! Pasta {0}: este servidor não está na lista de recursos - adicione-o em «Recursos»",
            "! المجلد {0}: هذا الخادم غير موجود في قائمة الموارد - أضفه من «الموارد»",
            "! फ़ोल्डर {0}: यह सर्वर संसाधन सूची में नहीं है - इसे «संसाधन» में जोड़ें",
            "! ফোল্ডার {0}: এই সার্ভারটি রিসোর্সের তালিকায় নেই - «রিসোর্স»-এ এটি যোগ করুন",
            "! فولڈر {0}: یہ سرور وسائل کی فہرست میں نہیں ہے - اسے «وسائل» میں شامل کریں",
            "! 文件夹 {0}: 此服务器不在资源列表中 - 请在「资源」中添加")

        Add("! Каталог {0}: ключ сервера «{1}» ещё не проверен - откройте этот ресурс один раз",
            "! Folder {0}: the key of server «{1}» has not been verified yet - open this resource once",
            "! Каталог {0}: ключ сервера «{1}» ще не перевірено - відкрийте цей ресурс один раз",
            "! Ordner {0}: Der Schlüssel des Servers «{1}» wurde noch nicht geprüft - öffnen Sie diese Ressource einmal",
            "! Cartella {0}: la chiave del server «{1}» non è ancora stata verificata - apri questa risorsa una volta",
            "! Carpeta {0}: la clave del servidor «{1}» aún no se ha verificado - abra este recurso una vez",
            "! Dossier {0} : la clé du serveur «{1}» n'a pas encore été vérifiée - ouvrez cette ressource une fois",
            "! Pasta {0}: a chave do servidor «{1}» ainda não foi verificada - abra este recurso uma vez",
            "! المجلد {0}: لم يتم التحقق بعد من مفتاح الخادم «{1}» - افتح هذا المورد مرة واحدة",
            "! फ़ोल्डर {0}: सर्वर «{1}» की कुंजी अभी जाँची नहीं गई है - यह संसाधन एक बार खोलें",
            "! ফোল্ডার {0}: সার্ভার «{1}»-এর কী এখনও যাচাই করা হয়নি - এই রিসোর্সটি একবার খুলুন",
            "! فولڈر {0}: سرور «{1}» کی کلید ابھی تصدیق نہیں ہوئی - یہ وسیلہ ایک بار کھولیں",
            "! 文件夹 {0}: 服务器「{1}」的密钥尚未验证 - 请先打开此资源一次")

        Add("! Каталог {0}: ресурс «{1}» только для чтения",
            "! Folder {0}: resource «{1}» is read-only",
            "! Каталог {0}: ресурс «{1}» лише для читання",
            "! Ordner {0}: Die Ressource «{1}» ist schreibgeschützt",
            "! Cartella {0}: la risorsa «{1}» è di sola lettura",
            "! Carpeta {0}: el recurso «{1}» es de solo lectura",
            "! Dossier {0} : la ressource «{1}» est en lecture seule",
            "! Pasta {0}: o recurso «{1}» é somente leitura",
            "! المجلد {0}: المورد «{1}» للقراءة فقط",
            "! फ़ोल्डर {0}: संसाधन «{1}» केवल पढ़ने के लिए है",
            "! ফোল্ডার {0}: রিসোর্স «{1}» শুধু পড়ার জন্য",
            "! فولڈر {0}: وسیلہ «{1}» صرف پڑھنے کے لیے ہے",
            "! 文件夹 {0}: 资源「{1}」为只读")

        Add("! Сервер не ответил, свободно ли имя файла, - действие отменено",
            "! The server did not say whether the file name is free - the action was cancelled",
            "! Сервер не відповів, чи вільне ім'я файлу, - дію скасовано",
            "! Der Server hat nicht gemeldet, ob der Dateiname frei ist - die Aktion wurde abgebrochen",
            "! Il server non ha risposto se il nome del file è libero - azione annullata",
            "! El servidor no indicó si el nombre de archivo está libre - la acción se canceló",
            "! Le serveur n'a pas indiqué si le nom de fichier est libre - action annulée",
            "! O servidor não informou se o nome do arquivo está livre - a ação foi cancelada",
            "! لم يُجب الخادم عمّا إذا كان اسم الملف متاحًا - أُلغي الإجراء",
            "! सर्वर ने नहीं बताया कि फ़ाइल का नाम खाली है या नहीं - कार्रवाई रद्द कर दी गई",
            "! ফাইলের নামটি খালি কি না সার্ভার জানায়নি - কাজটি বাতিল করা হয়েছে",
            "! سرور نے نہیں بتایا کہ فائل کا نام خالی ہے یا نہیں - عمل منسوخ کر دیا گیا",
            "! 服务器未答复该文件名是否可用 - 操作已取消")

        Add("Папка на этом компьютере..",
            "A folder on this PC..", "Папка на цьому комп'ютері..", "Ein Ordner auf diesem PC..", "Una cartella su questo PC..",
            "Una carpeta de este equipo..", "Un dossier sur ce PC..", "Uma pasta neste computador..", "مجلد على هذا الكمبيوتر..",
            "इस पीसी पर एक फ़ोल्डर..", "এই পিসির একটি ফোল্ডার..", "اس پی سی پر ایک فولڈر..", "本机上的文件夹..")

        Add("Ресурс: {0}",
            "Resource: {0}", "Ресурс: {0}", "Ressource: {0}", "Risorsa: {0}",
            "Recurso: {0}", "Ressource : {0}", "Recurso: {0}", "المورد: {0}",
            "संसाधन: {0}", "রিসোর্স: {0}", "وسیلہ: {0}", "资源: {0}")
    End Sub

End Class
