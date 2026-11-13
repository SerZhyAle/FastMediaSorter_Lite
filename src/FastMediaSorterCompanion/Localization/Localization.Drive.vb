Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

' Columns: Russian key, en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' Specification 203 section 5.10 (T-UI-1): every sentence of the Google Drive sign-in page of Manager settings and the
' page the browser shows after Google redirects back. "Google Drive" is a product name and stays Latin in every language.
' The words "Войти.." (Sign in..), "Отмена" (Cancel) and "Выйти на этом устройстве" (Sign out on this device) are the
' rows of the exchange account page (Localization.ExchangeDevice.vb / Localization.Share.vb) and are reused, not repeated.
Partial Public NotInheritable Class Localization
    Private Shared Sub AddDriveStrings()
        Add("Вход в Google Drive для связи этого ПК с вашими устройствами.",
            "Sign in to Google Drive to connect this PC with your devices.",
            "Вхід у Google Drive для зв'язку цього ПК з вашими пристроями.",
            "Bei Google Drive anmelden, um diesen PC mit Ihren Geräten zu verbinden.",
            "Accedi a Google Drive per collegare questo PC ai tuoi dispositivi.",
            "Inicia sesión en Google Drive para conectar este PC con tus dispositivos.",
            "Connectez-vous à Google Drive pour relier ce PC à vos appareils.",
            "Entre no Google Drive para conectar este PC aos seus dispositivos.",
            "سجّل الدخول إلى Google Drive لربط هذا الكمبيوتر بأجهزتك.",
            "इस पीसी को अपने डिवाइसों से जोड़ने के लिए Google Drive में साइन इन करें।",
            "এই পিসিকে আপনার ডিভাইসগুলোর সঙ্গে যুক্ত করতে Google Drive-এ সাইন ইন করুন।",
            "اس پی سی کو اپنے آلات سے جوڑنے کے لیے Google Drive میں سائن اِن کریں۔",
            "登录 Google Drive，将这台电脑与您的设备连接起来。")
        Add("Вход", "Sign-in", "Вхід", "Anmeldung", "Accesso", "Inicio de sesión", "Connexion", "Login", "تسجيل الدخول", "साइन इन", "সাইন ইন", "سائن اِن", "登录")
        ' The rail entry's name for assistive technology (IconMap "managerSettingsNav_drive"): the product name, never translated.
        Add("Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive")
        Add("Этот ПК и данные", "This PC and its data", "Цей ПК і дані", "Dieser PC und seine Daten", "Questo PC e i suoi dati", "Este PC y sus datos", "Ce PC et ses données", "Este PC e seus dados", "هذا الكمبيوتر وبياناته", "यह पीसी और इसका डेटा", "এই পিসি ও এর ডেটা", "یہ پی سی اور اس کا ڈیٹا", "此电脑及其数据")
        Add("Вход в Google Drive не настроен в этой сборке", "Google Drive sign-in is not set up in this build", "Вхід у Google Drive не налаштовано в цій збірці", "Die Anmeldung bei Google Drive ist in diesem Build nicht eingerichtet", "L'accesso a Google Drive non è configurato in questa versione", "El inicio de sesión en Google Drive no está configurado en esta versión", "La connexion à Google Drive n'est pas configurée dans cette version", "O login no Google Drive não está configurado nesta versão", "تسجيل الدخول إلى Google Drive غير مُعدّ في هذا الإصدار", "इस संस्करण में Google Drive साइन-इन सेट नहीं है", "এই সংস্করণে Google Drive সাইন-ইন সেট করা নেই", "اس ورژن میں Google Drive سائن اِن ترتیب نہیں دیا گیا", "此版本未配置 Google Drive 登录")
        Add("Ожидание входа в браузере..", "Waiting for sign-in in the browser..", "Очікування входу в браузері..", "Warte auf die Anmeldung im Browser..", "In attesa dell'accesso nel browser..", "Esperando el inicio de sesión en el navegador..", "En attente de la connexion dans le navigateur..", "Aguardando o login no navegador..", "في انتظار تسجيل الدخول في المتصفح..", "ब्राउज़र में साइन-इन की प्रतीक्षा है..", "ব্রাউজারে সাইন-ইনের জন্য অপেক্ষা চলছে..", "براؤزر میں سائن اِن کا انتظار ہے..", "正在等待在浏览器中登录..")
        Add("Вход в Google Drive выполнен", "Signed in to Google Drive", "Вхід у Google Drive виконано", "Bei Google Drive angemeldet", "Accesso a Google Drive effettuato", "Sesión iniciada en Google Drive", "Connecté à Google Drive", "Conectado ao Google Drive", "تم تسجيل الدخول إلى Google Drive", "Google Drive में साइन इन हो गया है", "Google Drive-এ সাইন ইন করা হয়েছে", "Google Drive میں سائن اِن ہو چکا ہے", "已登录 Google Drive")
        Add("Google Drive сейчас недоступен", "Google Drive is unavailable right now", "Google Drive зараз недоступний", "Google Drive ist derzeit nicht verfügbar", "Google Drive al momento non è disponibile", "Google Drive no está disponible en este momento", "Google Drive est indisponible pour le moment", "O Google Drive está indisponível neste momento", "Google Drive غير متاح حاليًا", "Google Drive अभी उपलब्ध नहीं है", "Google Drive এখন অনুপলব্ধ", "Google Drive اس وقت دستیاب نہیں ہے", "Google Drive 当前不可用")
        Add("Выход из Google Drive..", "Signing out of Google Drive..", "Вихід із Google Drive..", "Abmeldung von Google Drive..", "Uscita da Google Drive..", "Cerrando sesión en Google Drive..", "Déconnexion de Google Drive..", "Saindo do Google Drive..", "جارٍ تسجيل الخروج من Google Drive..", "Google Drive से साइन-आउट हो रहा है..", "Google Drive থেকে সাইন-আউট হচ্ছে..", "Google Drive سے سائن اوٹ ہو رہا ہے..", "正在退出 Google Drive..")
        Add("Вход в Google Drive не выполнен", "Not signed in to Google Drive", "Вхід у Google Drive не виконано", "Nicht bei Google Drive angemeldet", "Accesso a Google Drive non effettuato", "No se ha iniciado sesión en Google Drive", "Non connecté à Google Drive", "Não conectado ao Google Drive", "لم يتم تسجيل الدخول إلى Google Drive", "Google Drive में साइन इन नहीं है", "Google Drive-এ সাইন ইন করা নেই", "Google Drive میں سائن اِن نہیں ہے", "未登录 Google Drive")
        ' Specification L0308 section 2.4: the not-configured state names its remedy (a configured installation), never a setup.
        Add("Эта сборка не содержит ключа доступа к Google Drive, поэтому вход недоступен. Чтобы войти, установите текущую версию приложения с официального сайта. Остальные функции менеджера работают как обычно.",
            "This build does not include an access key for Google Drive, so sign-in is unavailable. To sign in, install the current version of the application from its official site. The rest of the Manager works as usual.",
            "Ця збірка не містить ключа доступу до Google Drive, тому вхід недоступний. Щоб увійти, установіть поточну версію застосунку з офіційного сайту. Решта функцій менеджера працює як зазвичай.",
            "Dieser Build enthält keinen Zugriffsschlüssel für Google Drive, daher ist die Anmeldung nicht verfügbar. Um sich anzumelden, installieren Sie die aktuelle Version der Anwendung von der offiziellen Website. Alle anderen Funktionen des Managers arbeiten wie gewohnt.",
            "Questa versione non include una chiave di accesso a Google Drive, quindi l'accesso non è disponibile. Per accedere, installa la versione attuale dell'applicazione dal sito ufficiale. Le altre funzioni del Manager funzionano come al solito.",
            "Esta versión no incluye una clave de acceso a Google Drive, por lo que el inicio de sesión no está disponible. Para iniciar sesión, instala la versión actual de la aplicación desde su sitio oficial. El resto de las funciones del administrador funciona como de costumbre.",
            "Cette version ne contient pas de clé d'accès à Google Drive ; la connexion est donc indisponible. Pour vous connecter, installez la version actuelle de l'application depuis son site officiel. Les autres fonctions du gestionnaire fonctionnent comme d'habitude.",
            "Esta versão não inclui uma chave de acesso ao Google Drive, por isso o login não está disponível. Para entrar, instale a versão atual do aplicativo pelo site oficial. As demais funções do gerenciador funcionam normalmente.",
            "لا يتضمن هذا الإصدار مفتاح وصول إلى Google Drive، لذا فإن تسجيل الدخول غير متاح. لتسجيل الدخول، ثبّت الإصدار الحالي من التطبيق من موقعه الرسمي. تعمل باقي وظائف المدير كالمعتاد.",
            "इस संस्करण में Google Drive की एक्सेस कुंजी शामिल नहीं है, इसलिए साइन-इन उपलब्ध नहीं है। साइन इन करने के लिए आधिकारिक साइट से ऐप्लिकेशन का मौजूदा संस्करण इंस्टॉल करें। मैनेजर के बाकी फ़ंक्शन सामान्य रूप से काम करते हैं।",
            "এই সংস্করণে Google Drive-এর অ্যাক্সেস কী অন্তর্ভুক্ত নেই, তাই সাইন-ইন অনুপলব্ধ। সাইন ইন করতে অফিসিয়াল সাইট থেকে অ্যাপ্লিকেশনের বর্তমান সংস্করণ ইনস্টল করুন। ম্যানেজারের বাকি ফিচারগুলো যথারীতি কাজ করে।",
            "اس ورژن میں Google Drive کی رسائی کی کلید شامل نہیں ہے، اس لیے سائن اِن دستیاب نہیں ہے۔ سائن اِن کرنے کے لیے سرکاری سائٹ سے ایپلیکیشن کا موجودہ ورژن انسٹال کریں۔ مینیجر کی باقی خصوصیات معمول کے مطابق کام کرتی ہیں۔",
            "此版本不包含 Google Drive 的访问密钥，因此无法登录。如需登录，请从官方网站安装应用程序的当前版本。管理器的其他功能照常工作。")
        Add("Вход больше не действует: доступ отозван или истёк. Войдите снова.",
            "The sign-in is no longer valid: access was revoked or has expired. Sign in again.",
            "Вхід більше не дійсний: доступ відкликано або він закінчився. Увійдіть знову.",
            "Die Anmeldung ist nicht mehr gültig: Der Zugriff wurde widerrufen oder ist abgelaufen. Melden Sie sich erneut an.",
            "L'accesso non è più valido: l'autorizzazione è stata revocata o è scaduta. Accedi di nuovo.",
            "El inicio de sesión ya no es válido: el acceso se revocó o caducó. Inicia sesión de nuevo.",
            "La connexion n'est plus valide : l'accès a été révoqué ou a expiré. Connectez-vous à nouveau.",
            "O login não é mais válido: o acesso foi revogado ou expirou. Entre novamente.",
            "لم يعد تسجيل الدخول صالحًا: تم إلغاء الوصول أو انتهت صلاحيته. سجّل الدخول مرة أخرى.",
            "साइन-इन अब मान्य नहीं है: एक्सेस रद्द कर दिया गया या समाप्त हो गया। फिर से साइन इन करें।",
            "সাইন-ইন আর বৈধ নয়: অ্যাক্সেস প্রত্যাহার করা হয়েছে বা মেয়াদ শেষ হয়েছে। আবার সাইন ইন করুন।",
            "سائن اِن اب درست نہیں رہا: رسائی منسوخ کر دی گئی یا ختم ہو گئی۔ دوبارہ سائن اِن کریں۔",
            "登录已失效：访问权限已被撤销或已过期。请重新登录。")
        Add("Войдите в свой Google Drive, чтобы связать этот ПК с вашими устройствами.",
            "Sign in to your Google Drive to connect this PC with your devices.",
            "Увійдіть у свій Google Drive, щоб зв'язати цей ПК з вашими пристроями.",
            "Melden Sie sich bei Ihrem Google Drive an, um diesen PC mit Ihren Geräten zu verbinden.",
            "Accedi al tuo Google Drive per collegare questo PC ai tuoi dispositivi.",
            "Inicia sesión en tu Google Drive para conectar este PC con tus dispositivos.",
            "Connectez-vous à votre Google Drive pour relier ce PC à vos appareils.",
            "Entre no seu Google Drive para conectar este PC aos seus dispositivos.",
            "سجّل الدخول إلى Google Drive الخاص بك لربط هذا الكمبيوتر بأجهزتك.",
            "इस पीसी को अपने डिवाइसों से जोड़ने के लिए अपने Google Drive में साइन इन करें।",
            "এই পিসিকে আপনার ডিভাইসগুলোর সঙ্গে যুক্ত করতে আপনার Google Drive-এ সাইন ইন করুন।",
            "اس پی سی کو اپنے آلات سے جوڑنے کے لیے اپنے Google Drive میں سائن اِن کریں۔",
            "请登录您的 Google Drive，以将这台电脑与您的设备连接起来。")
        Add("Завершите вход в открывшемся окне браузера. Чтобы отказаться, нажмите «Отмена».",
            "Finish signing in in the browser window that opened. To back out, press «Cancel».",
            "Завершіть вхід у вікні браузера, що відкрилося. Щоб відмовитися, натисніть «Скасувати».",
            "Schließen Sie die Anmeldung im geöffneten Browserfenster ab. Zum Abbrechen klicken Sie auf «Abbrechen».",
            "Completa l'accesso nella finestra del browser appena aperta. Per annullare, premi «Annulla».",
            "Completa el inicio de sesión en la ventana del navegador que se ha abierto. Para desistir, pulsa «Cancelar».",
            "Terminez la connexion dans la fenêtre du navigateur qui s'est ouverte. Pour y renoncer, cliquez sur « Annuler ».",
            "Conclua o login na janela do navegador que foi aberta. Para desistir, clique em «Cancelar».",
            "أكمل تسجيل الدخول في نافذة المتصفح التي فُتحت. للتراجع، اضغط «إلغاء».",
            "खुली हुई ब्राउज़र विंडो में साइन-इन पूरा करें। रुकने के लिए «रद्द करें» दबाएँ।",
            "খোলা ব্রাউজার উইন্ডোতে সাইন-ইন সম্পূর্ণ করুন। বাদ দিতে «বাতিল» চাপুন।",
            "کھلی ہوئی براؤزر ونڈو میں سائن اِن مکمل کریں۔ دستبردار ہونے کے لیے «منسوخ» دبائیں۔",
            "请在已打开的浏览器窗口中完成登录。如要放弃，请点击「取消」。")
        Add("Этот ПК связан с вашим Google Drive.", "This PC is connected to your Google Drive.", "Цей ПК пов'язано з вашим Google Drive.", "Dieser PC ist mit Ihrem Google Drive verbunden.", "Questo PC è collegato al tuo Google Drive.", "Este PC está conectado con tu Google Drive.", "Ce PC est relié à votre Google Drive.", "Este PC está conectado ao seu Google Drive.", "هذا الكمبيوتر مرتبط بحساب Google Drive الخاص بك.", "यह पीसी आपके Google Drive से जुड़ा है।", "এই পিসি আপনার Google Drive-এর সঙ্গে যুক্ত।", "یہ پی سی آپ کے Google Drive سے جڑا ہوا ہے۔", "这台电脑已与您的 Google Drive 关联。")
        Add("Нет связи с Google Drive. Повторная попытка выполняется автоматически.", "No connection to Google Drive. Retrying automatically.", "Немає зв'язку з Google Drive. Повторна спроба виконується автоматично.", "Keine Verbindung zu Google Drive. Der nächste Versuch erfolgt automatisch.", "Nessuna connessione a Google Drive. Il nuovo tentativo avviene automaticamente.", "Sin conexión con Google Drive. El reintento se realiza automáticamente.", "Pas de connexion à Google Drive. Une nouvelle tentative est faite automatiquement.", "Sem conexão com o Google Drive. Uma nova tentativa será feita automaticamente.", "لا يوجد اتصال بـ Google Drive. تُجرى إعادة المحاولة تلقائيًا.", "Google Drive से कोई कनेक्शन नहीं है। दोबारा प्रयास अपने आप किया जाता है।", "Google Drive-এর সঙ্গে সংযোগ নেই। পুনরায় চেষ্টা স্বয়ংক্রিয়ভাবে করা হয়।", "Google Drive سے رابطہ نہیں ہے۔ دوبارہ کوشش خودکار طور پر کی جاتی ہے۔", "无法连接到 Google Drive。系统会自动重试。")
        Add("Достигнут суточный лимит Google Drive на загрузку. Повторите завтра.", "The daily Google Drive upload limit has been reached. Try again tomorrow.", "Досягнуто добового ліміту Google Drive на завантаження. Повторіть завтра.", "Das tägliche Upload-Limit von Google Drive ist erreicht. Versuchen Sie es morgen erneut.", "È stato raggiunto il limite giornaliero di caricamento di Google Drive. Riprova domani.", "Se alcanzó el límite diario de subida de Google Drive. Inténtalo de nuevo mañana.", "La limite quotidienne d'envoi de Google Drive est atteinte. Réessayez demain.", "Foi atingido o limite diário de uploads do Google Drive. Tente novamente amanhã.", "تم بلوغ حد الرفع اليومي في Google Drive. حاول مرة أخرى غدًا.", "Google Drive की दैनिक अपलोड सीमा पूरी हो गई है। कल फिर प्रयास करें।", "Google Drive-এর দৈনিক আপলোড সীমা পূর্ণ হয়েছে। আগামীকাল আবার চেষ্টা করুন।", "Google Drive کی یومیہ اپ لوڈ کی حد پوری ہو گئی ہے۔ کل دوبارہ کوشش کریں۔", "已达到 Google Drive 的每日上传上限。请明天再试。")
        Add("На вашем Google Drive нет свободного места.", "There is no free space on your Google Drive.", "На вашому Google Drive немає вільного місця.", "Auf Ihrem Google Drive ist kein freier Speicherplatz mehr vorhanden.", "Sul tuo Google Drive non c'è più spazio libero.", "No queda espacio libre en tu Google Drive.", "Il n'y a plus d'espace libre sur votre Google Drive.", "Não há espaço livre no seu Google Drive.", "لا توجد مساحة خالية في Google Drive الخاص بك.", "आपके Google Drive में खाली जगह नहीं है।", "আপনার Google Drive-এ ফাঁকা জায়গা নেই।", "آپ کے Google Drive میں خالی جگہ نہیں ہے۔", "您的 Google Drive 没有可用空间了。")
        Add("Google Drive отвечает ошибкой или ограничивает запросы. Повторная попытка выполняется автоматически.", "Google Drive is returning an error or limiting requests. Retrying automatically.", "Google Drive відповідає помилкою або обмежує запити. Повторна спроба виконується автоматично.", "Google Drive meldet einen Fehler oder begrenzt die Anfragen. Der nächste Versuch erfolgt automatisch.", "Google Drive risponde con un errore o limita le richieste. Il nuovo tentativo avviene automaticamente.", "Google Drive responde con un error o limita las solicitudes. El reintento se realiza automáticamente.", "Google Drive répond par une erreur ou limite les requêtes. Une nouvelle tentative est faite automatiquement.", "O Google Drive está retornando um erro ou limitando as solicitações. Uma nova tentativa será feita automaticamente.", "يُرجع Google Drive خطأً أو يحدّ من الطلبات. تُجرى إعادة المحاولة تلقائيًا.", "Google Drive त्रुटि लौटा रहा है या अनुरोधों को सीमित कर रहा है। दोबारा प्रयास अपने आप किया जाता है।", "Google Drive ত্রুটি দিচ্ছে বা অনুরোধ সীমিত করছে। পুনরায় চেষ্টা স্বয়ংক্রিয়ভাবে করা হয়।", "Google Drive خرابی دے رہا ہے یا درخواستوں کو محدود کر رہا ہے۔ دوبارہ کوشش خودکار طور پر کی جاتی ہے۔", "Google Drive 返回错误或正在限制请求。系统会自动重试。")
        Add("Доступ не предоставлен. Вход отменён.", "Access was not granted. Sign-in cancelled.", "Доступ не надано. Вхід скасовано.", "Der Zugriff wurde nicht erteilt. Anmeldung abgebrochen.", "Autorizzazione non concessa. Accesso annullato.", "No se concedió el acceso. Inicio de sesión cancelado.", "L'accès n'a pas été accordé. Connexion annulée.", "O acesso não foi concedido. Login cancelado.", "لم يتم منح الوصول. أُلغي تسجيل الدخول.", "एक्सेस नहीं दिया गया। साइन-इन रद्द किया गया।", "অ্যাক্সেস দেওয়া হয়নি। সাইন-ইন বাতিল করা হয়েছে।", "رسائی نہیں دی گئی۔ سائن اِن منسوخ کر دیا گیا۔", "未授予访问权限。登录已取消。")
        Add("Вход отменён.", "Sign-in cancelled.", "Вхід скасовано.", "Anmeldung abgebrochen.", "Accesso annullato.", "Inicio de sesión cancelado.", "Connexion annulée.", "Login cancelado.", "أُلغي تسجيل الدخول.", "साइन-इन रद्द किया गया।", "সাইন-ইন বাতিল করা হয়েছে।", "سائن اِن منسوخ کر دیا گیا۔", "登录已取消。")
        Add("Вход не завершён вовремя. Попробуйте ещё раз.", "Sign-in was not completed in time. Try again.", "Вхід не завершено вчасно. Спробуйте ще раз.", "Die Anmeldung wurde nicht rechtzeitig abgeschlossen. Versuchen Sie es erneut.", "L'accesso non è stato completato in tempo. Riprova.", "El inicio de sesión no se completó a tiempo. Inténtalo de nuevo.", "La connexion n'a pas été terminée à temps. Réessayez.", "O login não foi concluído a tempo. Tente novamente.", "لم يكتمل تسجيل الدخول في الوقت المحدد. حاول مرة أخرى.", "साइन-इन समय पर पूरा नहीं हुआ। फिर से प्रयास करें।", "সাইন-ইন সময়মতো সম্পূর্ণ হয়নি। আবার চেষ্টা করুন।", "سائن اِن بروقت مکمل نہیں ہوا۔ دوبارہ کوشش کریں۔", "登录未在规定时间内完成。请重试。")
        Add("Не удалось открыть браузер. Проверьте браузер по умолчанию и повторите вход.", "The browser could not be opened. Check your default browser and sign in again.", "Не вдалося відкрити браузер. Перевірте браузер за замовчуванням і повторіть вхід.", "Der Browser konnte nicht geöffnet werden. Prüfen Sie den Standardbrowser und melden Sie sich erneut an.", "Impossibile aprire il browser. Controlla il browser predefinito e ripeti l'accesso.", "No se pudo abrir el navegador. Revisa el navegador predeterminado y vuelve a iniciar sesión.", "Impossible d'ouvrir le navigateur. Vérifiez le navigateur par défaut et reconnectez-vous.", "Não foi possível abrir o navegador. Verifique o navegador padrão e entre novamente.", "تعذّر فتح المتصفح. تحقّق من المتصفح الافتراضي وكرّر تسجيل الدخول.", "ब्राउज़र नहीं खोला जा सका। डिफ़ॉल्ट ब्राउज़र जाँचें और फिर से साइन इन करें।", "ব্রাউজার খোলা যায়নি। ডিফল্ট ব্রাউজার পরীক্ষা করুন এবং আবার সাইন ইন করুন।", "براؤزر نہیں کھولا جا سکا۔ ڈیفالٹ براؤزر چیک کریں اور دوبارہ سائن اِن کریں۔", "无法打开浏览器。请检查默认浏览器后重新登录。")
        Add("Google предоставил не тот доступ, который запрошен, поэтому вход отклонён. Разрешите только доступ к данным самого приложения.",
            "Google granted different access than was requested, so the sign-in was rejected. Allow only access to the application's own data.",
            "Google надав не той доступ, що запитувався, тому вхід відхилено. Дозвольте лише доступ до даних самого застосунку.",
            "Google hat nicht den angeforderten Zugriff erteilt, deshalb wurde die Anmeldung abgelehnt. Erlauben Sie nur den Zugriff auf die eigenen Daten der Anwendung.",
            "Google ha concesso un accesso diverso da quello richiesto, quindi l'accesso è stato rifiutato. Consenti solo l'accesso ai dati dell'applicazione stessa.",
            "Google concedió un acceso distinto del solicitado, por lo que se rechazó el inicio de sesión. Permite solo el acceso a los datos propios de la aplicación.",
            "Google a accordé un accès différent de celui demandé ; la connexion a donc été refusée. N'autorisez que l'accès aux données propres à l'application.",
            "O Google concedeu um acesso diferente do solicitado, por isso o login foi recusado. Permita apenas o acesso aos dados do próprio aplicativo.",
            "منحت Google وصولًا مختلفًا عن المطلوب، لذا رُفض تسجيل الدخول. اسمح فقط بالوصول إلى بيانات التطبيق نفسه.",
            "Google ने माँगे गए एक्सेस से अलग एक्सेस दिया, इसलिए साइन-इन अस्वीकार कर दिया गया। केवल ऐप के अपने डेटा तक पहुँच की अनुमति दें।",
            "Google অনুরোধ করা অ্যাক্সেসের চেয়ে ভিন্ন অ্যাক্সেস দিয়েছে, তাই সাইন-ইন প্রত্যাখ্যান করা হয়েছে। শুধু অ্যাপের নিজের ডেটায় অ্যাক্সেসের অনুমতি দিন।",
            "Google نے مطلوبہ رسائی سے مختلف رسائی دی، اس لیے سائن اِن مسترد کر دیا گیا۔ صرف ایپ کے اپنے ڈیٹا تک رسائی کی اجازت دیں۔",
            "Google 授予的权限与所请求的不同，因此登录被拒绝。请只允许访问应用自己的数据。")
        Add("Не удалось сохранить вход на этом ПК.", "The sign-in could not be saved on this PC.", "Не вдалося зберегти вхід на цьому ПК.", "Die Anmeldung konnte auf diesem PC nicht gespeichert werden.", "Impossibile salvare l'accesso su questo PC.", "No se pudo guardar el inicio de sesión en este PC.", "Impossible d'enregistrer la connexion sur ce PC.", "Não foi possível salvar o login neste PC.", "تعذّر حفظ تسجيل الدخول على هذا الكمبيوتر.", "इस पीसी पर साइन-इन सहेजा नहीं जा सका।", "এই পিসিতে সাইন-ইন সংরক্ষণ করা যায়নি।", "اس پی سی پر سائن اِن محفوظ نہیں کیا جا سکا۔", "无法在这台电脑上保存登录信息。")
        Add("Нет связи с Google. Проверьте подключение и повторите вход.", "No connection to Google. Check your connection and sign in again.", "Немає зв'язку з Google. Перевірте підключення й повторіть вхід.", "Keine Verbindung zu Google. Prüfen Sie die Verbindung und melden Sie sich erneut an.", "Nessuna connessione a Google. Controlla la connessione e ripeti l'accesso.", "Sin conexión con Google. Comprueba la conexión y vuelve a iniciar sesión.", "Pas de connexion à Google. Vérifiez la connexion et reconnectez-vous.", "Sem conexão com o Google. Verifique a conexão e entre novamente.", "لا يوجد اتصال بـ Google. تحقّق من الاتصال وكرّر تسجيل الدخول.", "Google से कोई कनेक्शन नहीं है। कनेक्शन जाँचें और फिर से साइन इन करें।", "Google-এর সঙ্গে সংযোগ নেই। সংযোগ পরীক্ষা করুন এবং আবার সাইন ইন করুন।", "Google سے رابطہ نہیں ہے۔ کنکشن چیک کریں اور دوبارہ سائن اِن کریں۔", "无法连接到 Google。请检查网络连接后重新登录。")
        Add("Не удалось завершить вход в Google. Повторите попытку.", "Sign-in to Google could not be completed. Try again.", "Не вдалося завершити вхід у Google. Повторіть спробу.", "Die Anmeldung bei Google konnte nicht abgeschlossen werden. Versuchen Sie es erneut.", "Impossibile completare l'accesso a Google. Riprova.", "No se pudo completar el inicio de sesión en Google. Inténtalo de nuevo.", "Impossible de terminer la connexion à Google. Réessayez.", "Não foi possível concluir o login no Google. Tente novamente.", "تعذّر إكمال تسجيل الدخول إلى Google. حاول مرة أخرى.", "Google में साइन-इन पूरा नहीं हो सका। फिर से प्रयास करें।", "Google-এ সাইন-ইন সম্পূর্ণ করা যায়নি। আবার চেষ্টা করুন।", "Google میں سائن اِن مکمل نہیں ہو سکا۔ دوبارہ کوشش کریں۔", "无法完成 Google 登录。请重试。")
        Add("Вы вышли из Google Drive на этом ПК, но подтвердить отзыв доступа у Google не удалось. Отзовите его вручную в настройках аккаунта Google.",
            "You are signed out of Google Drive on this PC, but Google did not confirm that access was revoked. Revoke it manually in your Google Account settings.",
            "Ви вийшли з Google Drive на цьому ПК, але підтвердити відкликання доступу в Google не вдалося. Відкличте його вручну в налаштуваннях облікового запису Google.",
            "Sie sind auf diesem PC von Google Drive abgemeldet, aber Google hat den Widerruf des Zugriffs nicht bestätigt. Widerrufen Sie ihn manuell in den Einstellungen Ihres Google-Kontos.",
            "Sei uscito da Google Drive su questo PC, ma Google non ha confermato la revoca dell'accesso. Revocalo manualmente nelle impostazioni dell'account Google.",
            "Has cerrado sesión en Google Drive en este PC, pero Google no confirmó la revocación del acceso. Revócalo manualmente en la configuración de tu cuenta de Google.",
            "Vous êtes déconnecté de Google Drive sur ce PC, mais Google n'a pas confirmé la révocation de l'accès. Révoquez-le manuellement dans les paramètres de votre compte Google.",
            "Você saiu do Google Drive neste PC, mas o Google não confirmou a revogação do acesso. Revogue-o manualmente nas configurações da sua conta Google.",
            "لقد سجّلت الخروج من Google Drive على هذا الكمبيوتر، لكن Google لم تؤكد إلغاء الوصول. ألغِه يدويًا من إعدادات حساب Google.",
            "आप इस पीसी पर Google Drive से साइन-आउट हो गए हैं, लेकिन Google ने एक्सेस रद्द होने की पुष्टि नहीं की। इसे Google खाते की सेटिंग में मैन्युअल रूप से रद्द करें।",
            "আপনি এই পিসিতে Google Drive থেকে সাইন-আউট করেছেন, কিন্তু Google অ্যাক্সেস প্রত্যাহারের নিশ্চিতকরণ দেয়নি। Google অ্যাকাউন্টের সেটিংসে এটি নিজে প্রত্যাহার করুন।",
            "آپ اس پی سی پر Google Drive سے سائن اوٹ ہو گئے ہیں، لیکن Google نے رسائی کی منسوخی کی تصدیق نہیں کی۔ اسے Google اکاؤنٹ کی ترتیبات میں خود منسوخ کریں۔",
            "您已在这台电脑上退出 Google Drive，但 Google 未能确认访问权限已被撤销。请在 Google 账号设置中手动撤销。")
        Add("Вы вышли из Google Drive на этом ПК. Доступ отозван, но записи этого ПК на Google Drive удалить не удалось.",
            "You are signed out of Google Drive on this PC. Access was revoked, but this PC's records on Google Drive could not be deleted.",
            "Ви вийшли з Google Drive на цьому ПК. Доступ відкликано, але записи цього ПК на Google Drive видалити не вдалося.",
            "Sie sind auf diesem PC von Google Drive abgemeldet. Der Zugriff wurde widerrufen, aber die Einträge dieses PCs auf Google Drive konnten nicht gelöscht werden.",
            "Sei uscito da Google Drive su questo PC. L'accesso è stato revocato, ma non è stato possibile eliminare da Google Drive i record di questo PC.",
            "Has cerrado sesión en Google Drive en este PC. El acceso se revocó, pero no se pudieron eliminar de Google Drive los registros de este PC.",
            "Vous êtes déconnecté de Google Drive sur ce PC. L'accès a été révoqué, mais les enregistrements de ce PC sur Google Drive n'ont pas pu être supprimés.",
            "Você saiu do Google Drive neste PC. O acesso foi revogado, mas não foi possível excluir do Google Drive os registros deste PC.",
            "لقد سجّلت الخروج من Google Drive على هذا الكمبيوتر. أُلغي الوصول، لكن تعذّر حذف سجلات هذا الكمبيوتر من Google Drive.",
            "आप इस पीसी पर Google Drive से साइन-आउट हो गए हैं। एक्सेस रद्द हो गया, लेकिन इस पीसी की प्रविष्टियाँ Google Drive से हटाई नहीं जा सकीं।",
            "আপনি এই পিসিতে Google Drive থেকে সাইন-আউট করেছেন। অ্যাক্সেস প্রত্যাহার করা হয়েছে, কিন্তু Google Drive থেকে এই পিসির রেকর্ড মোছা যায়নি।",
            "آپ اس پی سی پر Google Drive سے سائن اوٹ ہو گئے ہیں۔ رسائی منسوخ ہو گئی، لیکن Google Drive سے اس پی سی کے ریکارڈ حذف نہیں ہو سکے۔",
            "您已在这台电脑上退出 Google Drive。访问权限已撤销，但无法删除这台电脑在 Google Drive 上的记录。")
        Add("Вы вышли из Google Drive. Доступ отозван, записей этого ПК удалено: {0}.",
            "You are signed out of Google Drive. Access was revoked; records of this PC deleted: {0}.",
            "Ви вийшли з Google Drive. Доступ відкликано, записів цього ПК видалено: {0}.",
            "Sie sind von Google Drive abgemeldet. Der Zugriff wurde widerrufen, gelöschte Einträge dieses PCs: {0}.",
            "Sei uscito da Google Drive. L'accesso è stato revocato, record di questo PC eliminati: {0}.",
            "Has cerrado sesión en Google Drive. El acceso se revocó; registros de este PC eliminados: {0}.",
            "Vous êtes déconnecté de Google Drive. L'accès a été révoqué ; enregistrements de ce PC supprimés : {0}.",
            "Você saiu do Google Drive. O acesso foi revogado; registros deste PC excluídos: {0}.",
            "لقد سجّلت الخروج من Google Drive. أُلغي الوصول، وعدد سجلات هذا الكمبيوتر المحذوفة: {0}.",
            "आप Google Drive से साइन-आउट हो गए हैं। एक्सेस रद्द हो गया, इस पीसी की हटाई गई प्रविष्टियाँ: {0}।",
            "আপনি Google Drive থেকে সাইন-আউট করেছেন। অ্যাক্সেস প্রত্যাহার করা হয়েছে, এই পিসির মুছে ফেলা রেকর্ড: {0}।",
            "آپ Google Drive سے سائن اوٹ ہو گئے ہیں۔ رسائی منسوخ ہو گئی، اس پی سی کے حذف کیے گئے ریکارڈ: {0}۔",
            "您已退出 Google Drive。访问权限已撤销，已删除这台电脑的记录：{0}。")
        Add("Выйти из Google Drive на этом ПК? Доступ будет отозван у Google и удалён с этого ПК, а записи этого ПК будут удалены с вашего Google Drive.",
            "Sign out of Google Drive on this PC? Access will be revoked at Google and removed from this PC, and this PC's records will be deleted from your Google Drive.",
            "Вийти з Google Drive на цьому ПК? Доступ буде відкликано в Google і видалено з цього ПК, а записи цього ПК буде видалено з вашого Google Drive.",
            "Auf diesem PC von Google Drive abmelden? Der Zugriff wird bei Google widerrufen und von diesem PC entfernt, und die Einträge dieses PCs werden aus Ihrem Google Drive gelöscht.",
            "Uscire da Google Drive su questo PC? L'accesso sarà revocato presso Google e rimosso da questo PC, e i record di questo PC saranno eliminati dal tuo Google Drive.",
            "¿Cerrar sesión en Google Drive en este PC? El acceso se revocará en Google y se quitará de este PC, y los registros de este PC se eliminarán de tu Google Drive.",
            "Se déconnecter de Google Drive sur ce PC ? L'accès sera révoqué auprès de Google et supprimé de ce PC, et les enregistrements de ce PC seront supprimés de votre Google Drive.",
            "Sair do Google Drive neste PC? O acesso será revogado no Google e removido deste PC, e os registros deste PC serão excluídos do seu Google Drive.",
            "هل تريد تسجيل الخروج من Google Drive على هذا الكمبيوتر؟ سيُلغى الوصول لدى Google ويُزال من هذا الكمبيوتر، وستُحذف سجلات هذا الكمبيوتر من Google Drive الخاص بك.",
            "इस पीसी पर Google Drive से साइन-आउट करें? Google पर एक्सेस रद्द कर दिया जाएगा और इस पीसी से हटा दिया जाएगा, तथा इस पीसी की प्रविष्टियाँ आपके Google Drive से हटा दी जाएँगी।",
            "এই পিসিতে Google Drive থেকে সাইন-আউট করবেন? Google-এ অ্যাক্সেস প্রত্যাহার করা হবে এবং এই পিসি থেকে সরানো হবে, আর এই পিসির রেকর্ড আপনার Google Drive থেকে মুছে ফেলা হবে।",
            "اس پی سی پر Google Drive سے سائن اوٹ کریں؟ Google پر رسائی منسوخ کر دی جائے گی اور اس پی سی سے ہٹا دی جائے گی، اور اس پی سی کے ریکارڈ آپ کے Google Drive سے حذف کر دیے جائیں گے۔",
            "要在这台电脑上退出 Google Drive 吗？访问权限将在 Google 处被撤销并从这台电脑上移除，这台电脑的记录将从您的 Google Drive 中删除。")
        Add("Приложению доступна только его собственная скрытая папка на вашем Google Drive, а не ваши файлы. Вход хранится на этом ПК под защитой Windows.",
            "The application can access only its own hidden folder on your Google Drive, not your files. The sign-in is stored on this PC under Windows protection.",
            "Застосунку доступна лише його власна прихована папка на вашому Google Drive, а не ваші файли. Вхід зберігається на цьому ПК під захистом Windows.",
            "Die Anwendung hat nur Zugriff auf ihren eigenen versteckten Ordner in Ihrem Google Drive, nicht auf Ihre Dateien. Die Anmeldung wird auf diesem PC durch Windows geschützt gespeichert.",
            "L'applicazione può accedere solo alla propria cartella nascosta sul tuo Google Drive, non ai tuoi file. L'accesso è memorizzato su questo PC sotto la protezione di Windows.",
            "La aplicación solo puede acceder a su propia carpeta oculta en tu Google Drive, no a tus archivos. El inicio de sesión se guarda en este PC con la protección de Windows.",
            "L'application n'a accès qu'à son propre dossier masqué sur votre Google Drive, pas à vos fichiers. La connexion est conservée sur ce PC sous la protection de Windows.",
            "O aplicativo só pode acessar a própria pasta oculta no seu Google Drive, e não os seus arquivos. O login é salvo neste PC sob a proteção do Windows.",
            "يمكن للتطبيق الوصول فقط إلى مجلده المخفي الخاص على Google Drive الخاص بك، وليس إلى ملفاتك. يُحفظ تسجيل الدخول على هذا الكمبيوتر بحماية Windows.",
            "ऐप केवल आपके Google Drive पर अपने ही छिपे हुए फ़ोल्डर तक पहुँच सकता है, आपकी फ़ाइलों तक नहीं। साइन-इन इस पीसी पर Windows की सुरक्षा के तहत सहेजा जाता है।",
            "অ্যাপটি আপনার Google Drive-এ শুধু নিজের লুকানো ফোল্ডারে অ্যাক্সেস করতে পারে, আপনার ফাইলে নয়। সাইন-ইন এই পিসিতে Windows-এর সুরক্ষায় সংরক্ষিত থাকে।",
            "ایپ آپ کے Google Drive پر صرف اپنے چھپے ہوئے فولڈر تک رسائی حاصل کر سکتی ہے، آپ کی فائلوں تک نہیں۔ سائن اِن اس پی سی پر Windows کے تحفظ کے تحت محفوظ رہتا ہے۔",
            "应用只能访问您 Google Drive 上属于它自己的隐藏文件夹，而不能访问您的文件。登录信息在这台电脑上受 Windows 保护存储。")
        Add("Имя этого ПК в Google Drive: «{0}». Идентификатор: {1}.", "This PC's name in Google Drive: «{0}». Identifier: {1}.", "Ім'я цього ПК у Google Drive: «{0}». Ідентифікатор: {1}.", "Name dieses PCs in Google Drive: «{0}». Kennung: {1}.", "Nome di questo PC in Google Drive: «{0}». Identificatore: {1}.", "Nombre de este PC en Google Drive: «{0}». Identificador: {1}.", "Nom de ce PC dans Google Drive : « {0} ». Identifiant : {1}.", "Nome deste PC no Google Drive: «{0}». Identificador: {1}.", "اسم هذا الكمبيوتر في Google Drive: «{0}». المعرّف: {1}.", "Google Drive में इस पीसी का नाम: «{0}»। पहचानकर्ता: {1}।", "Google Drive-এ এই পিসির নাম: «{0}»। শনাক্তকারী: {1}।", "Google Drive میں اس پی سی کا نام: «{0}»۔ شناخت کنندہ: {1}۔", "此电脑在 Google Drive 中的名称：「{0}」。标识符：{1}。")
        Add("Ответ получен. Эту вкладку можно закрыть и вернуться в программу.", "Response received. You can close this tab and return to the program.", "Відповідь отримано. Цю вкладку можна закрити й повернутися до програми.", "Antwort empfangen. Sie können diesen Tab schließen und zum Programm zurückkehren.", "Risposta ricevuta. Puoi chiudere questa scheda e tornare al programma.", "Respuesta recibida. Puedes cerrar esta pestaña y volver al programa.", "Réponse reçue. Vous pouvez fermer cet onglet et revenir au programme.", "Resposta recebida. Você pode fechar esta guia e voltar ao programa.", "تم استلام الرد. يمكنك إغلاق هذه العلامة والعودة إلى البرنامج.", "उत्तर प्राप्त हो गया। आप इस टैब को बंद करके प्रोग्राम पर लौट सकते हैं।", "উত্তর পাওয়া গেছে। আপনি এই ট্যাবটি বন্ধ করে প্রোগ্রামে ফিরে যেতে পারেন।", "جواب موصول ہو گیا۔ آپ اس ٹیب کو بند کر کے پروگرام پر واپس جا سکتے ہیں۔", "已收到响应。您可以关闭此标签页并返回程序。")
        ' Ticket 269 P3.4: the queue screen of the page (DEVICE-EXCHANGE item CS). The column words "Состояние", "Тип", "Устройство"
        ' and "Создано" and the kinds "Настройки" and "Буфер обмена" are rows of the other tables and are reused, not repeated.
        Add("Очередь на Google Drive", "Queue on Google Drive", "Черга на Google Drive", "Warteschlange auf Google Drive", "Coda su Google Drive", "Cola en Google Drive", "File d'attente sur Google Drive", "Fila no Google Drive", "قائمة الانتظار على Google Drive", "Google Drive पर कतार", "Google Drive-এ সারি", "Google Drive پر قطار", "Google Drive 上的队列")
        Add("Здесь видно, что ваши устройства передают друг другу через ваш Google Drive и сколько места это занимает. Любой пакет можно удалить.",
            "What your devices pass to each other through your Google Drive and how much space it takes. You can delete any packet.",
            "Тут видно, що ваші пристрої передають одне одному через ваш Google Drive і скільки місця це займає. Будь-який пакет можна видалити.",
            "Hier sehen Sie, was Ihre Geräte über Ihr Google Drive austauschen und wie viel Speicherplatz das belegt. Jedes Paket lässt sich löschen.",
            "Qui vedi cosa si scambiano i tuoi dispositivi tramite il tuo Google Drive e quanto spazio occupa. Puoi eliminare qualsiasi pacchetto.",
            "Aquí ves lo que tus dispositivos se pasan a través de tu Google Drive y cuánto espacio ocupa. Puedes eliminar cualquier paquete.",
            "Ce que vos appareils s'échangent via votre Google Drive et la place que cela occupe. Vous pouvez supprimer n'importe quel paquet.",
            "Aqui você vê o que seus dispositivos trocam pelo seu Google Drive e quanto espaço isso ocupa. Você pode excluir qualquer pacote.",
            "هنا ترى ما تتبادله أجهزتك عبر Google Drive الخاص بك والمساحة التي يشغلها. يمكنك حذف أي حزمة.",
            "यहाँ दिखता है कि आपके डिवाइस आपके Google Drive के ज़रिए एक-दूसरे को क्या भेज रहे हैं और इसमें कितनी जगह लगती है। आप कोई भी पैकेट हटा सकते हैं।",
            "এখানে দেখা যায় আপনার ডিভাইসগুলো আপনার Google Drive-এর মাধ্যমে একে অপরকে কী পাঠাচ্ছে এবং তাতে কতটা জায়গা লাগছে। আপনি যেকোনো প্যাকেট মুছতে পারেন।",
            "یہاں نظر آتا ہے کہ آپ کے آلات آپ کے Google Drive کے ذریعے ایک دوسرے کو کیا بھیج رہے ہیں اور اس میں کتنی جگہ لگتی ہے۔ آپ کوئی بھی پیکٹ حذف کر سکتے ہیں۔",
            "这里显示您的设备通过您的 Google Drive 相互传递的内容及其占用的空间。您可以删除任何数据包。")
        Add("Очередь видна после входа в Google Drive.", "The queue shows after you sign in to Google Drive.", "Черга видна після входу в Google Drive.", "Die Warteschlange erscheint nach der Anmeldung bei Google Drive.", "La coda è visibile dopo l'accesso a Google Drive.", "La cola se ve después de iniciar sesión en Google Drive.", "La file d'attente s'affiche une fois connecté à Google Drive.", "A fila aparece depois que você entra no Google Drive.", "تظهر قائمة الانتظار بعد تسجيل الدخول إلى Google Drive.", "Google Drive में साइन इन करने के बाद कतार दिखती है।", "Google Drive-এ সাইন ইন করার পরে সারি দেখা যায়।", "Google Drive میں سائن اِن کرنے کے بعد قطار نظر آتی ہے۔", "登录 Google Drive 后即可看到队列。")
        Add("Чтение очереди..", "Reading the queue..", "Читання черги..", "Warteschlange wird gelesen..", "Lettura della coda..", "Leyendo la cola..", "Lecture de la file d'attente..", "Lendo a fila..", "جارٍ قراءة قائمة الانتظار..", "कतार पढ़ी जा रही है..", "সারি পড়া হচ্ছে..", "قطار پڑھی جا رہی ہے..", "正在读取队列..")
        Add("Очередь пуста.", "The queue is empty.", "Черга порожня.", "Die Warteschlange ist leer.", "La coda è vuota.", "La cola está vacía.", "La file d'attente est vide.", "A fila está vazia.", "قائمة الانتظار فارغة.", "कतार खाली है।", "সারি খালি।", "قطار خالی ہے۔", "队列为空。")
        Add("Ещё загружается пакетов: {0}.", "Packets still uploading: {0}.", "Ще завантажується пакетів: {0}.", "Pakete, die noch hochgeladen werden: {0}.", "Pacchetti ancora in caricamento: {0}.", "Paquetes que aún se están subiendo: {0}.", "Paquets encore en cours d'envoi : {0}.", "Pacotes ainda sendo enviados: {0}.", "حزم لا يزال رفعها جارياً: {0}.", "अभी अपलोड हो रहे पैकेट: {0}।", "এখনও আপলোড হচ্ছে এমন প্যাকেট: {0}।", "ابھی اپ لوڈ ہو رہے پیکٹ: {0}۔", "仍在上传的数据包: {0}。")
        Add("Не удалось прочитать пакетов: {0}.", "Packets that could not be read: {0}.", "Не вдалося прочитати пакетів: {0}.", "Pakete, die nicht gelesen werden konnten: {0}.", "Pacchetti non leggibili: {0}.", "Paquetes que no se pudieron leer: {0}.", "Paquets illisibles : {0}.", "Pacotes que não puderam ser lidos: {0}.", "حزم تعذرت قراءتها: {0}.", "जो पैकेट पढ़े नहीं जा सके: {0}।", "যে প্যাকেট পড়া যায়নি: {0}।", "جو پیکٹ پڑھے نہیں جا سکے: {0}۔", "无法读取的数据包: {0}。")
        Add("Размер", "Size", "Розмір", "Größe", "Dimensione", "Tamaño", "Taille", "Tamanho", "الحجم", "आकार", "আকার", "سائز", "大小")
        Add("Отправлен", "Sent", "Надіслано", "Gesendet", "Inviato", "Enviado", "Envoyé", "Enviado", "مُرسَل", "भेजा गया", "পাঠানো হয়েছে", "بھیجا گیا", "已发送")
        Add("Ждёт получения", "Waiting to be received", "Очікує отримання", "Wartet auf Empfang", "In attesa di ricezione", "Pendiente de recibir", "En attente de réception", "Aguardando recebimento", "بانتظار الاستلام", "प्राप्ति की प्रतीक्षा में", "গ্রহণের অপেক্ষায়", "وصولی کا منتظر", "等待接收")
        Add("Получен", "Received", "Отримано", "Empfangen", "Ricevuto", "Recibido", "Reçu", "Recebido", "مُستلَم", "प्राप्त", "পাওয়া গেছে", "موصول", "已接收")
        Add("Между другими устройствами", "Between other devices", "Між іншими пристроями", "Zwischen anderen Geräten", "Tra altri dispositivi", "Entre otros dispositivos", "Entre d'autres appareils", "Entre outros dispositivos", "بين أجهزة أخرى", "अन्य डिवाइसों के बीच", "অন্য ডিভাইসগুলোর মধ্যে", "دوسرے آلات کے درمیان", "其他设备之间")
        Add("Файлы", "Files", "Файли", "Dateien", "File", "Archivos", "Fichiers", "Arquivos", "ملفات", "फ़ाइलें", "ফাইল", "فائلیں", "文件")
        Add("Медиафайлы", "Media files", "Медіафайли", "Mediendateien", "File multimediali", "Archivos multimedia", "Fichiers multimédias", "Arquivos de mídia", "ملفات وسائط", "मीडिया फ़ाइलें", "মিডিয়া ফাইল", "میڈیا فائلیں", "媒体文件")
        Add("Запрос", "Request", "Запит", "Anfrage", "Richiesta", "Solicitud", "Demande", "Solicitação", "طلب", "अनुरोध", "অনুরোধ", "درخواست", "请求")
        Add("Ответ", "Reply", "Відповідь", "Antwort", "Risposta", "Respuesta", "Réponse", "Resposta", "رد", "उत्तर", "উত্তর", "جواب", "回复")
        Add("Другое", "Other", "Інше", "Sonstiges", "Altro", "Otro", "Autre", "Outro", "أخرى", "अन्य", "অন্যান্য", "دیگر", "其他")
        Add("для «{0}»", "for «{0}»", "для «{0}»", "für «{0}»", "per «{0}»", "para «{0}»", "pour «{0}»", "para «{0}»", "إلى «{0}»", "«{0}» के लिए", "«{0}»-এর জন্য", "«{0}» کے لیے", "给「{0}」")
        Add("от «{0}»", "from «{0}»", "від «{0}»", "von «{0}»", "da «{0}»", "de «{0}»", "de «{0}»", "de «{0}»", "من «{0}»", "«{0}» से", "«{0}» থেকে", "«{0}» سے", "来自「{0}」")
        Add("от «{0}» для «{1}»", "from «{0}» for «{1}»", "від «{0}» для «{1}»", "von «{0}» für «{1}»", "da «{0}» per «{1}»", "de «{0}» para «{1}»", "de «{0}» pour «{1}»", "de «{0}» para «{1}»", "من «{0}» إلى «{1}»", "«{0}» से «{1}» के लिए", "«{0}» থেকে «{1}»-এর জন্য", "«{0}» سے «{1}» کے لیے", "从「{0}」到「{1}」")
        Add("для всех ваших устройств", "for all your devices", "для всіх ваших пристроїв", "für alle Ihre Geräte", "per tutti i tuoi dispositivi", "para todos tus dispositivos", "pour tous vos appareils", "para todos os seus dispositivos", "لكل أجهزتك", "आपके सभी डिवाइसों के लिए", "আপনার সব ডিভাইসের জন্য", "آپ کے تمام آلات کے لیے", "给您的所有设备")
        Add("Удалить пакет", "Delete packet", "Видалити пакет", "Paket löschen", "Elimina pacchetto", "Eliminar paquete", "Supprimer le paquet", "Excluir pacote", "حذف الحزمة", "पैकेट हटाएँ", "প্যাকেট মুছুন", "پیکٹ حذف کریں", "删除数据包")
        Add("Удалить этот пакет с вашего Google Drive? Устройства больше не смогут его получить.",
            "Delete this packet from your Google Drive? Your devices will no longer be able to receive it.",
            "Видалити цей пакет з вашого Google Drive? Пристрої більше не зможуть його отримати.",
            "Dieses Paket von Ihrem Google Drive löschen? Ihre Geräte können es dann nicht mehr empfangen.",
            "Eliminare questo pacchetto dal tuo Google Drive? I dispositivi non potranno più riceverlo.",
            "¿Eliminar este paquete de tu Google Drive? Los dispositivos ya no podrán recibirlo.",
            "Supprimer ce paquet de votre Google Drive ? Vos appareils ne pourront plus le recevoir.",
            "Excluir este pacote do seu Google Drive? Os dispositivos não poderão mais recebê-lo.",
            "هل تريد حذف هذه الحزمة من Google Drive الخاص بك؟ لن تتمكن الأجهزة من استلامها بعد ذلك.",
            "इस पैकेट को अपने Google Drive से हटाएँ? इसके बाद डिवाइस इसे प्राप्त नहीं कर पाएँगे।",
            "এই প্যাকেটটি আপনার Google Drive থেকে মুছবেন? এরপর ডিভাইসগুলো এটি আর পাবে না।",
            "یہ پیکٹ اپنے Google Drive سے حذف کریں؟ اس کے بعد آلات اسے وصول نہیں کر سکیں گے۔",
            "从您的 Google Drive 删除此数据包？之后设备将无法再接收它。")
        Add("Нет связи с Google Drive, пакет не удалён. Повторите попытку.", "No connection to Google Drive, the packet was not deleted. Try again.", "Немає зв'язку з Google Drive, пакет не видалено. Спробуйте ще раз.", "Keine Verbindung zu Google Drive, das Paket wurde nicht gelöscht. Versuchen Sie es erneut.", "Nessuna connessione a Google Drive, il pacchetto non è stato eliminato. Riprova.", "Sin conexión con Google Drive, el paquete no se eliminó. Inténtalo de nuevo.", "Pas de connexion à Google Drive, le paquet n'a pas été supprimé. Réessayez.", "Sem conexão com o Google Drive, o pacote não foi excluído. Tente novamente.", "لا يوجد اتصال بـ Google Drive، لم تُحذف الحزمة. أعد المحاولة.", "Google Drive से कनेक्शन नहीं है, पैकेट नहीं हटाया गया। फिर से कोशिश करें।", "Google Drive-এর সঙ্গে সংযোগ নেই, প্যাকেট মোছা হয়নি। আবার চেষ্টা করুন।", "Google Drive سے رابطہ نہیں، پیکٹ حذف نہیں ہوا۔ دوبارہ کوشش کریں۔", "无法连接 Google Drive，数据包未删除。请重试。")
        ' Specification L0308 section 3: the help entry beside "Sign in..", the short note under the buttons, and the outcomes
        ' that name their own next action (an account restriction, a build client Google refuses, the first round not yet answered).
        Add("Как подключить Google Drive..", "How to connect Google Drive..", "Як підключити Google Drive..", "So verbinden Sie Google Drive..",
            "Come collegare Google Drive..", "Cómo conectar Google Drive..", "Comment connecter Google Drive..", "Como conectar o Google Drive..",
            "كيفية ربط Google Drive..", "Google Drive कैसे कनेक्ट करें..", "Google Drive কীভাবে যুক্ত করবেন..", "Google Drive کیسے جوڑیں..", "如何连接 Google Drive..")
        Add("Нужны аккаунт Google и интернет; приложение Google Drive не требуется. Пароль вводится только на странице Google в браузере.",
            "You need a Google account and an internet connection; the Google Drive app is not required. Your password is entered only on Google's page in the browser.",
            "Потрібні обліковий запис Google та інтернет; застосунок Google Drive не потрібен. Пароль вводиться лише на сторінці Google у браузері.",
            "Sie benötigen ein Google-Konto und eine Internetverbindung; die Google Drive-App ist nicht erforderlich. Ihr Passwort geben Sie nur auf der Google-Seite im Browser ein.",
            "Servono un account Google e una connessione a Internet; l'app Google Drive non è necessaria. La password si inserisce solo nella pagina di Google nel browser.",
            "Necesitas una cuenta de Google y conexión a internet; no hace falta la aplicación Google Drive. La contraseña se escribe solo en la página de Google en el navegador.",
            "Il faut un compte Google et une connexion Internet ; l'application Google Drive n'est pas nécessaire. Le mot de passe se saisit uniquement sur la page de Google dans le navigateur.",
            "Você precisa de uma conta Google e de conexão com a internet; o app Google Drive não é necessário. A senha é digitada apenas na página do Google no navegador.",
            "تحتاج إلى حساب Google واتصال بالإنترنت؛ ولا حاجة إلى تطبيق Google Drive. تُدخل كلمة المرور فقط في صفحة Google في المتصفح.",
            "Google खाता और इंटरनेट कनेक्शन चाहिए; Google Drive ऐप ज़रूरी नहीं है। पासवर्ड केवल ब्राउज़र में Google के पेज पर डाला जाता है।",
            "একটি Google অ্যাকাউন্ট ও ইন্টারনেট সংযোগ লাগবে; Google Drive অ্যাপ লাগবে না। পাসওয়ার্ড শুধু ব্রাউজারে Google-এর পেজে দেওয়া হয়।",
            "Google اکاؤنٹ اور انٹرنیٹ کنکشن درکار ہے؛ Google Drive ایپ ضروری نہیں۔ پاس ورڈ صرف براؤزر میں Google کے صفحے پر درج کیا جاتا ہے۔",
            "需要一个 Google 账号和互联网连接；不需要 Google Drive 应用。密码只在浏览器中的 Google 页面上输入。")
        Add("Google больше не принимает ключ входа этой установки. Установите текущую версию приложения с официального сайта; вход на этом ПК сохранён.",
            "Google no longer accepts the sign-in key of this installation. Install the current version of the application from its official site; the sign-in on this PC is kept.",
            "Google більше не приймає ключ входу цієї інсталяції. Установіть поточну версію застосунку з офіційного сайту; вхід на цьому ПК збережено.",
            "Google akzeptiert den Anmeldeschlüssel dieser Installation nicht mehr. Installieren Sie die aktuelle Version der Anwendung von der offiziellen Website; die Anmeldung auf diesem PC bleibt erhalten.",
            "Google non accetta più la chiave di accesso di questa installazione. Installa la versione attuale dell'applicazione dal sito ufficiale; l'accesso su questo PC resta salvato.",
            "Google ya no acepta la clave de inicio de sesión de esta instalación. Instala la versión actual de la aplicación desde su sitio oficial; el inicio de sesión en este PC se conserva.",
            "Google n'accepte plus la clé de connexion de cette installation. Installez la version actuelle de l'application depuis son site officiel ; la connexion sur ce PC est conservée.",
            "O Google não aceita mais a chave de login desta instalação. Instale a versão atual do aplicativo pelo site oficial; o login neste PC é mantido.",
            "لم يعد Google يقبل مفتاح تسجيل الدخول لهذا التثبيت. ثبّت الإصدار الحالي من التطبيق من موقعه الرسمي؛ يبقى تسجيل الدخول على هذا الكمبيوتر محفوظًا.",
            "Google अब इस इंस्टॉलेशन की साइन-इन कुंजी स्वीकार नहीं करता। आधिकारिक साइट से ऐप्लिकेशन का मौजूदा संस्करण इंस्टॉल करें; इस पीसी पर साइन-इन सहेजा रहता है।",
            "Google আর এই ইনস্টলেশনের সাইন-ইন কী গ্রহণ করে না। অফিসিয়াল সাইট থেকে অ্যাপ্লিকেশনের বর্তমান সংস্করণ ইনস্টল করুন; এই পিসিতে সাইন-ইন সংরক্ষিত থাকে।",
            "Google اب اس انسٹالیشن کی سائن اِن کلید قبول نہیں کرتا۔ سرکاری سائٹ سے ایپلیکیشن کا موجودہ ورژن انسٹال کریں؛ اس پی سی پر سائن اِن محفوظ رہتا ہے۔",
            "Google 不再接受此安装的登录密钥。请从官方网站安装应用程序的当前版本；此电脑上的登录信息仍会保留。")
        Add("Google не разрешил вход этим аккаунтом: его ограничивает организация или сам Google. Войдите другим аккаунтом Google или обратитесь к администратору аккаунта.",
            "Google did not allow sign-in with this account: an organization or Google itself restricts it. Sign in with another Google account or contact the account's administrator.",
            "Google не дозволив вхід цим обліковим записом: його обмежує організація або сам Google. Увійдіть іншим обліковим записом Google або зверніться до адміністратора облікового запису.",
            "Google hat die Anmeldung mit diesem Konto nicht erlaubt: Eine Organisation oder Google selbst schränkt es ein. Melden Sie sich mit einem anderen Google-Konto an oder wenden Sie sich an den Administrator des Kontos.",
            "Google non ha consentito l'accesso con questo account: lo limita un'organizzazione o Google stesso. Accedi con un altro account Google o rivolgiti all'amministratore dell'account.",
            "Google no permitió iniciar sesión con esta cuenta: una organización o el propio Google la restringe. Inicia sesión con otra cuenta de Google o contacta con el administrador de la cuenta.",
            "Google n'a pas autorisé la connexion avec ce compte : une organisation ou Google lui-même le restreint. Connectez-vous avec un autre compte Google ou contactez l'administrateur du compte.",
            "O Google não permitiu o login com esta conta: uma organização ou o próprio Google a restringe. Entre com outra conta Google ou fale com o administrador da conta.",
            "لم يسمح Google بتسجيل الدخول بهذا الحساب: تقيّده مؤسسة أو Google نفسه. سجّل الدخول بحساب Google آخر أو تواصل مع مسؤول الحساب.",
            "Google ने इस खाते से साइन-इन की अनुमति नहीं दी: इसे कोई संगठन या स्वयं Google सीमित करता है। किसी दूसरे Google खाते से साइन इन करें या खाते के व्यवस्थापक से संपर्क करें।",
            "Google এই অ্যাকাউন্টে সাইন-ইনের অনুমতি দেয়নি: কোনো প্রতিষ্ঠান বা Google নিজেই এটি সীমিত করে। অন্য একটি Google অ্যাকাউন্টে সাইন ইন করুন অথবা অ্যাকাউন্টের অ্যাডমিনিস্ট্রেটরের সঙ্গে যোগাযোগ করুন।",
            "Google نے اس اکاؤنٹ سے سائن اِن کی اجازت نہیں دی: اسے کوئی ادارہ یا خود Google محدود کرتا ہے۔ کسی دوسرے Google اکاؤنٹ سے سائن اِن کریں یا اکاؤنٹ کے منتظم سے رابطہ کریں۔",
            "Google 不允许使用此账号登录：某个组织或 Google 本身限制了该账号。请使用其他 Google 账号登录，或联系该账号的管理员。")
        Add("Проверка связи с Google Drive..", "Checking the connection to Google Drive..", "Перевірка зв'язку з Google Drive..", "Verbindung zu Google Drive wird geprüft..",
            "Verifica della connessione a Google Drive..", "Comprobando la conexión con Google Drive..", "Vérification de la connexion à Google Drive..", "Verificando a conexão com o Google Drive..",
            "جارٍ التحقق من الاتصال بـ Google Drive..", "Google Drive से कनेक्शन की जाँच हो रही है..", "Google Drive-এর সঙ্গে সংযোগ যাচাই করা হচ্ছে..", "Google Drive سے رابطے کی جانچ ہو رہی ہے..", "正在检查与 Google Drive 的连接..")
        Add("Google Drive не удалил пакет. Повторите попытку.", "Google Drive did not delete the packet. Try again.", "Google Drive не видалив пакет. Спробуйте ще раз.", "Google Drive hat das Paket nicht gelöscht. Versuchen Sie es erneut.", "Google Drive non ha eliminato il pacchetto. Riprova.", "Google Drive no eliminó el paquete. Inténtalo de nuevo.", "Google Drive n'a pas supprimé le paquet. Réessayez.", "O Google Drive não excluiu o pacote. Tente novamente.", "لم يحذف Google Drive الحزمة. أعد المحاولة.", "Google Drive ने पैकेट नहीं हटाया। फिर से कोशिश करें।", "Google Drive প্যাকেট মোছেনি। আবার চেষ্টা করুন।", "Google Drive نے پیکٹ حذف نہیں کیا۔ دوبارہ کوشش کریں۔", "Google Drive 未删除该数据包。请重试。")
    End Sub
End Class
