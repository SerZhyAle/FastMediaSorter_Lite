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
            "Inicie sessão no Google Drive para ligar este PC aos seus dispositivos.",
            "سجّل الدخول إلى Google Drive لربط هذا الكمبيوتر بأجهزتك.",
            "इस पीसी को अपने डिवाइसों से जोड़ने के लिए Google Drive में साइन इन करें।",
            "এই পিসিকে আপনার ডিভাইসগুলোর সঙ্গে যুক্ত করতে Google Drive-এ সাইন ইন করুন।",
            "اس پی سی کو اپنے آلات سے جوڑنے کے لیے Google Drive میں سائن اِن کریں۔",
            "登录 Google Drive，将这台电脑与您的设备连接起来。")
        Add("Вход", "Sign-in", "Вхід", "Anmeldung", "Accesso", "Inicio de sesión", "Connexion", "Início de sessão", "تسجيل الدخول", "साइन इन", "সাইন ইন", "سائن اِن", "登录")
        ' The rail entry's name for assistive technology (IconMap "managerSettingsNav_drive"): the product name, never translated.
        Add("Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive", "Google Drive")
        Add("Этот ПК и данные", "This PC and its data", "Цей ПК і дані", "Dieser PC und seine Daten", "Questo PC e i suoi dati", "Este PC y sus datos", "Ce PC et ses données", "Este PC e os seus dados", "هذا الكمبيوتر وبياناته", "यह पीसी और इसका डेटा", "এই পিসি ও এর ডেটা", "یہ پی سی اور اس کا ڈیٹا", "此电脑及其数据")
        Add("Вход в Google Drive не настроен в этой сборке", "Google Drive sign-in is not set up in this build", "Вхід у Google Drive не налаштовано в цій збірці", "Die Anmeldung bei Google Drive ist in diesem Build nicht eingerichtet", "L'accesso a Google Drive non è configurato in questa versione", "El inicio de sesión en Google Drive no está configurado en esta versión", "La connexion à Google Drive n'est pas configurée dans cette version", "O início de sessão no Google Drive não está configurado nesta versão", "تسجيل الدخول إلى Google Drive غير مُعدّ في هذا الإصدار", "इस संस्करण में Google Drive साइन-इन सेट नहीं है", "এই সংস্করণে Google Drive সাইন-ইন সেট করা নেই", "اس ورژن میں Google Drive سائن اِن ترتیب نہیں دیا گیا", "此版本未配置 Google Drive 登录")
        Add("Ожидание входа в браузере..", "Waiting for sign-in in the browser..", "Очікування входу в браузері..", "Warte auf die Anmeldung im Browser..", "In attesa dell'accesso nel browser..", "Esperando el inicio de sesión en el navegador..", "En attente de la connexion dans le navigateur..", "A aguardar o início de sessão no navegador..", "في انتظار تسجيل الدخول في المتصفح..", "ब्राउज़र में साइन-इन की प्रतीक्षा है..", "ব্রাউজারে সাইন-ইনের জন্য অপেক্ষা চলছে..", "براؤزر میں سائن اِن کا انتظار ہے..", "正在等待在浏览器中登录..")
        Add("Вход в Google Drive выполнен", "Signed in to Google Drive", "Вхід у Google Drive виконано", "Bei Google Drive angemeldet", "Accesso a Google Drive effettuato", "Sesión iniciada en Google Drive", "Connecté à Google Drive", "Sessão iniciada no Google Drive", "تم تسجيل الدخول إلى Google Drive", "Google Drive में साइन इन हो गया है", "Google Drive-এ সাইন ইন করা হয়েছে", "Google Drive میں سائن اِن ہو چکا ہے", "已登录 Google Drive")
        Add("Google Drive сейчас недоступен", "Google Drive is unavailable right now", "Google Drive зараз недоступний", "Google Drive ist derzeit nicht verfügbar", "Google Drive al momento non è disponibile", "Google Drive no está disponible en este momento", "Google Drive est indisponible pour le moment", "O Google Drive está indisponível neste momento", "Google Drive غير متاح حاليًا", "Google Drive अभी उपलब्ध नहीं है", "Google Drive এখন অনুপলব্ধ", "Google Drive اس وقت دستیاب نہیں ہے", "Google Drive 当前不可用")
        Add("Выход из Google Drive..", "Signing out of Google Drive..", "Вихід із Google Drive..", "Abmeldung von Google Drive..", "Uscita da Google Drive..", "Cerrando sesión en Google Drive..", "Déconnexion de Google Drive..", "A terminar a sessão no Google Drive..", "جارٍ تسجيل الخروج من Google Drive..", "Google Drive से साइन-आउट हो रहा है..", "Google Drive থেকে সাইন-আউট হচ্ছে..", "Google Drive سے سائن اوٹ ہو رہا ہے..", "正在退出 Google Drive..")
        Add("Вход в Google Drive не выполнен", "Not signed in to Google Drive", "Вхід у Google Drive не виконано", "Nicht bei Google Drive angemeldet", "Accesso a Google Drive non effettuato", "No se ha iniciado sesión en Google Drive", "Non connecté à Google Drive", "Sessão não iniciada no Google Drive", "لم يتم تسجيل الدخول إلى Google Drive", "Google Drive में साइन इन नहीं है", "Google Drive-এ সাইন ইন করা নেই", "Google Drive میں سائن اِن نہیں ہے", "未登录 Google Drive")
        Add("Эта сборка не содержит ключа доступа к Google Drive, поэтому вход недоступен. Остальные функции менеджера работают как обычно.",
            "This build does not include an access key for Google Drive, so sign-in is unavailable. The rest of the Manager works as usual.",
            "Ця збірка не містить ключа доступу до Google Drive, тому вхід недоступний. Решта функцій менеджера працює як зазвичай.",
            "Dieser Build enthält keinen Zugriffsschlüssel für Google Drive, daher ist die Anmeldung nicht verfügbar. Alle anderen Funktionen des Managers arbeiten wie gewohnt.",
            "Questa versione non include una chiave di accesso a Google Drive, quindi l'accesso non è disponibile. Le altre funzioni del Manager funzionano come al solito.",
            "Esta versión no incluye una clave de acceso a Google Drive, por lo que el inicio de sesión no está disponible. El resto de las funciones del administrador funciona como de costumbre.",
            "Cette version ne contient pas de clé d'accès à Google Drive ; la connexion est donc indisponible. Les autres fonctions du gestionnaire fonctionnent comme d'habitude.",
            "Esta versão não inclui uma chave de acesso ao Google Drive, por isso o início de sessão não está disponível. As restantes funções do gestor funcionam como habitualmente.",
            "لا يتضمن هذا الإصدار مفتاح وصول إلى Google Drive، لذا فإن تسجيل الدخول غير متاح. تعمل باقي وظائف المدير كالمعتاد.",
            "इस संस्करण में Google Drive की एक्सेस कुंजी शामिल नहीं है, इसलिए साइन-इन उपलब्ध नहीं है। मैनेजर के बाकी फ़ंक्शन सामान्य रूप से काम करते हैं।",
            "এই সংস্করণে Google Drive-এর অ্যাক্সেস কী অন্তর্ভুক্ত নেই, তাই সাইন-ইন অনুপলব্ধ। ম্যানেজারের বাকি ফিচারগুলো যথারীতি কাজ করে।",
            "اس ورژن میں Google Drive کی رسائی کی کلید شامل نہیں ہے، اس لیے سائن اِن دستیاب نہیں ہے۔ مینیجر کی باقی خصوصیات معمول کے مطابق کام کرتی ہیں۔",
            "此版本不包含 Google Drive 的访问密钥，因此无法登录。管理器的其他功能照常工作。")
        Add("Вход больше не действует: доступ отозван или истёк. Войдите снова.",
            "The sign-in is no longer valid: access was revoked or has expired. Sign in again.",
            "Вхід більше не дійсний: доступ відкликано або він закінчився. Увійдіть знову.",
            "Die Anmeldung ist nicht mehr gültig: Der Zugriff wurde widerrufen oder ist abgelaufen. Melden Sie sich erneut an.",
            "L'accesso non è più valido: l'autorizzazione è stata revocata o è scaduta. Accedi di nuovo.",
            "El inicio de sesión ya no es válido: el acceso se revocó o caducó. Inicia sesión de nuevo.",
            "La connexion n'est plus valide : l'accès a été révoqué ou a expiré. Connectez-vous à nouveau.",
            "O início de sessão deixou de ser válido: o acesso foi revogado ou expirou. Inicie sessão novamente.",
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
            "Inicie sessão no seu Google Drive para ligar este PC aos seus dispositivos.",
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
            "Conclua o início de sessão na janela do navegador que foi aberta. Para desistir, clique em «Cancelar».",
            "أكمل تسجيل الدخول في نافذة المتصفح التي فُتحت. للتراجع، اضغط «إلغاء».",
            "खुली हुई ब्राउज़र विंडो में साइन-इन पूरा करें। रुकने के लिए «रद्द करें» दबाएँ।",
            "খোলা ব্রাউজার উইন্ডোতে সাইন-ইন সম্পূর্ণ করুন। বাদ দিতে «বাতিল» চাপুন।",
            "کھلی ہوئی براؤزر ونڈو میں سائن اِن مکمل کریں۔ دستبردار ہونے کے لیے «منسوخ» دبائیں۔",
            "请在已打开的浏览器窗口中完成登录。如要放弃，请点击「取消」。")
        Add("Этот ПК связан с вашим Google Drive.", "This PC is connected to your Google Drive.", "Цей ПК пов'язано з вашим Google Drive.", "Dieser PC ist mit Ihrem Google Drive verbunden.", "Questo PC è collegato al tuo Google Drive.", "Este PC está conectado con tu Google Drive.", "Ce PC est relié à votre Google Drive.", "Este PC está ligado ao seu Google Drive.", "هذا الكمبيوتر مرتبط بحساب Google Drive الخاص بك.", "यह पीसी आपके Google Drive से जुड़ा है।", "এই পিসি আপনার Google Drive-এর সঙ্গে যুক্ত।", "یہ پی سی آپ کے Google Drive سے جڑا ہوا ہے۔", "这台电脑已与您的 Google Drive 关联。")
        Add("Нет связи с Google Drive. Повторная попытка выполняется автоматически.", "No connection to Google Drive. Retrying automatically.", "Немає зв'язку з Google Drive. Повторна спроба виконується автоматично.", "Keine Verbindung zu Google Drive. Der nächste Versuch erfolgt automatisch.", "Nessuna connessione a Google Drive. Il nuovo tentativo avviene automaticamente.", "Sin conexión con Google Drive. El reintento se realiza automáticamente.", "Pas de connexion à Google Drive. Une nouvelle tentative est faite automatiquement.", "Sem ligação ao Google Drive. A nova tentativa é feita automaticamente.", "لا يوجد اتصال بـ Google Drive. تُجرى إعادة المحاولة تلقائيًا.", "Google Drive से कोई कनेक्शन नहीं है। दोबारा प्रयास अपने आप किया जाता है।", "Google Drive-এর সঙ্গে সংযোগ নেই। পুনরায় চেষ্টা স্বয়ংক্রিয়ভাবে করা হয়।", "Google Drive سے رابطہ نہیں ہے۔ دوبارہ کوشش خودکار طور پر کی جاتی ہے۔", "无法连接到 Google Drive。系统会自动重试。")
        Add("Достигнут суточный лимит Google Drive на загрузку. Повторите завтра.", "The daily Google Drive upload limit has been reached. Try again tomorrow.", "Досягнуто добового ліміту Google Drive на завантаження. Повторіть завтра.", "Das tägliche Upload-Limit von Google Drive ist erreicht. Versuchen Sie es morgen erneut.", "È stato raggiunto il limite giornaliero di caricamento di Google Drive. Riprova domani.", "Se alcanzó el límite diario de subida de Google Drive. Inténtalo de nuevo mañana.", "La limite quotidienne d'envoi de Google Drive est atteinte. Réessayez demain.", "Foi atingido o limite diário de carregamento do Google Drive. Tente novamente amanhã.", "تم بلوغ حد الرفع اليومي في Google Drive. حاول مرة أخرى غدًا.", "Google Drive की दैनिक अपलोड सीमा पूरी हो गई है। कल फिर प्रयास करें।", "Google Drive-এর দৈনিক আপলোড সীমা পূর্ণ হয়েছে। আগামীকাল আবার চেষ্টা করুন।", "Google Drive کی یومیہ اپ لوڈ کی حد پوری ہو گئی ہے۔ کل دوبارہ کوشش کریں۔", "已达到 Google Drive 的每日上传上限。请明天再试。")
        Add("На вашем Google Drive нет свободного места.", "There is no free space on your Google Drive.", "На вашому Google Drive немає вільного місця.", "Auf Ihrem Google Drive ist kein freier Speicherplatz mehr vorhanden.", "Sul tuo Google Drive non c'è più spazio libero.", "No queda espacio libre en tu Google Drive.", "Il n'y a plus d'espace libre sur votre Google Drive.", "Não há espaço livre no seu Google Drive.", "لا توجد مساحة خالية في Google Drive الخاص بك.", "आपके Google Drive में खाली जगह नहीं है।", "আপনার Google Drive-এ ফাঁকা জায়গা নেই।", "آپ کے Google Drive میں خالی جگہ نہیں ہے۔", "您的 Google Drive 没有可用空间了。")
        Add("Google Drive отвечает ошибкой или ограничивает запросы. Повторная попытка выполняется автоматически.", "Google Drive is returning an error or limiting requests. Retrying automatically.", "Google Drive відповідає помилкою або обмежує запити. Повторна спроба виконується автоматично.", "Google Drive meldet einen Fehler oder begrenzt die Anfragen. Der nächste Versuch erfolgt automatisch.", "Google Drive risponde con un errore o limita le richieste. Il nuovo tentativo avviene automaticamente.", "Google Drive responde con un error o limita las solicitudes. El reintento se realiza automáticamente.", "Google Drive répond par une erreur ou limite les requêtes. Une nouvelle tentative est faite automatiquement.", "O Google Drive responde com um erro ou limita os pedidos. A nova tentativa é feita automaticamente.", "يُرجع Google Drive خطأً أو يحدّ من الطلبات. تُجرى إعادة المحاولة تلقائيًا.", "Google Drive त्रुटि लौटा रहा है या अनुरोधों को सीमित कर रहा है। दोबारा प्रयास अपने आप किया जाता है।", "Google Drive ত্রুটি দিচ্ছে বা অনুরোধ সীমিত করছে। পুনরায় চেষ্টা স্বয়ংক্রিয়ভাবে করা হয়।", "Google Drive خرابی دے رہا ہے یا درخواستوں کو محدود کر رہا ہے۔ دوبارہ کوشش خودکار طور پر کی جاتی ہے۔", "Google Drive 返回错误或正在限制请求。系统会自动重试。")
        Add("Доступ не предоставлен. Вход отменён.", "Access was not granted. Sign-in cancelled.", "Доступ не надано. Вхід скасовано.", "Der Zugriff wurde nicht erteilt. Anmeldung abgebrochen.", "Autorizzazione non concessa. Accesso annullato.", "No se concedió el acceso. Inicio de sesión cancelado.", "L'accès n'a pas été accordé. Connexion annulée.", "O acesso não foi concedido. Início de sessão cancelado.", "لم يتم منح الوصول. أُلغي تسجيل الدخول.", "एक्सेस नहीं दिया गया। साइन-इन रद्द किया गया।", "অ্যাক্সেস দেওয়া হয়নি। সাইন-ইন বাতিল করা হয়েছে।", "رسائی نہیں دی گئی۔ سائن اِن منسوخ کر دیا گیا۔", "未授予访问权限。登录已取消。")
        Add("Вход отменён.", "Sign-in cancelled.", "Вхід скасовано.", "Anmeldung abgebrochen.", "Accesso annullato.", "Inicio de sesión cancelado.", "Connexion annulée.", "Início de sessão cancelado.", "أُلغي تسجيل الدخول.", "साइन-इन रद्द किया गया।", "সাইন-ইন বাতিল করা হয়েছে।", "سائن اِن منسوخ کر دیا گیا۔", "登录已取消。")
        Add("Вход не завершён вовремя. Попробуйте ещё раз.", "Sign-in was not completed in time. Try again.", "Вхід не завершено вчасно. Спробуйте ще раз.", "Die Anmeldung wurde nicht rechtzeitig abgeschlossen. Versuchen Sie es erneut.", "L'accesso non è stato completato in tempo. Riprova.", "El inicio de sesión no se completó a tiempo. Inténtalo de nuevo.", "La connexion n'a pas été terminée à temps. Réessayez.", "O início de sessão não foi concluído a tempo. Tente novamente.", "لم يكتمل تسجيل الدخول في الوقت المحدد. حاول مرة أخرى.", "साइन-इन समय पर पूरा नहीं हुआ। फिर से प्रयास करें।", "সাইন-ইন সময়মতো সম্পূর্ণ হয়নি। আবার চেষ্টা করুন।", "سائن اِن بروقت مکمل نہیں ہوا۔ دوبارہ کوشش کریں۔", "登录未在规定时间内完成。请重试。")
        Add("Не удалось открыть браузер. Проверьте браузер по умолчанию и повторите вход.", "The browser could not be opened. Check your default browser and sign in again.", "Не вдалося відкрити браузер. Перевірте браузер за замовчуванням і повторіть вхід.", "Der Browser konnte nicht geöffnet werden. Prüfen Sie den Standardbrowser und melden Sie sich erneut an.", "Impossibile aprire il browser. Controlla il browser predefinito e ripeti l'accesso.", "No se pudo abrir el navegador. Revisa el navegador predeterminado y vuelve a iniciar sesión.", "Impossible d'ouvrir le navigateur. Vérifiez le navigateur par défaut et reconnectez-vous.", "Não foi possível abrir o navegador. Verifique o navegador predefinido e inicie sessão novamente.", "تعذّر فتح المتصفح. تحقّق من المتصفح الافتراضي وكرّر تسجيل الدخول.", "ब्राउज़र नहीं खोला जा सका। डिफ़ॉल्ट ब्राउज़र जाँचें और फिर से साइन इन करें।", "ব্রাউজার খোলা যায়নি। ডিফল্ট ব্রাউজার পরীক্ষা করুন এবং আবার সাইন ইন করুন।", "براؤزر نہیں کھولا جا سکا۔ ڈیفالٹ براؤزر چیک کریں اور دوبارہ سائن اِن کریں۔", "无法打开浏览器。请检查默认浏览器后重新登录。")
        Add("Google предоставил не тот доступ, который запрошен, поэтому вход отклонён. Разрешите только доступ к данным самого приложения.",
            "Google granted different access than was requested, so the sign-in was rejected. Allow only access to the application's own data.",
            "Google надав не той доступ, що запитувався, тому вхід відхилено. Дозвольте лише доступ до даних самого застосунку.",
            "Google hat nicht den angeforderten Zugriff erteilt, deshalb wurde die Anmeldung abgelehnt. Erlauben Sie nur den Zugriff auf die eigenen Daten der Anwendung.",
            "Google ha concesso un accesso diverso da quello richiesto, quindi l'accesso è stato rifiutato. Consenti solo l'accesso ai dati dell'applicazione stessa.",
            "Google concedió un acceso distinto del solicitado, por lo que se rechazó el inicio de sesión. Permite solo el acceso a los datos propios de la aplicación.",
            "Google a accordé un accès différent de celui demandé ; la connexion a donc été refusée. N'autorisez que l'accès aux données propres à l'application.",
            "A Google concedeu um acesso diferente do pedido, por isso o início de sessão foi recusado. Permita apenas o acesso aos dados da própria aplicação.",
            "منحت Google وصولًا مختلفًا عن المطلوب، لذا رُفض تسجيل الدخول. اسمح فقط بالوصول إلى بيانات التطبيق نفسه.",
            "Google ने माँगे गए एक्सेस से अलग एक्सेस दिया, इसलिए साइन-इन अस्वीकार कर दिया गया। केवल ऐप के अपने डेटा तक पहुँच की अनुमति दें।",
            "Google অনুরোধ করা অ্যাক্সেসের চেয়ে ভিন্ন অ্যাক্সেস দিয়েছে, তাই সাইন-ইন প্রত্যাখ্যান করা হয়েছে। শুধু অ্যাপের নিজের ডেটায় অ্যাক্সেসের অনুমতি দিন।",
            "Google نے مطلوبہ رسائی سے مختلف رسائی دی، اس لیے سائن اِن مسترد کر دیا گیا۔ صرف ایپ کے اپنے ڈیٹا تک رسائی کی اجازت دیں۔",
            "Google 授予的权限与所请求的不同，因此登录被拒绝。请只允许访问应用自己的数据。")
        Add("Не удалось сохранить вход на этом ПК.", "The sign-in could not be saved on this PC.", "Не вдалося зберегти вхід на цьому ПК.", "Die Anmeldung konnte auf diesem PC nicht gespeichert werden.", "Impossibile salvare l'accesso su questo PC.", "No se pudo guardar el inicio de sesión en este PC.", "Impossible d'enregistrer la connexion sur ce PC.", "Não foi possível guardar o início de sessão neste PC.", "تعذّر حفظ تسجيل الدخول على هذا الكمبيوتر.", "इस पीसी पर साइन-इन सहेजा नहीं जा सका।", "এই পিসিতে সাইন-ইন সংরক্ষণ করা যায়নি।", "اس پی سی پر سائن اِن محفوظ نہیں کیا جا سکا۔", "无法在这台电脑上保存登录信息。")
        Add("Нет связи с Google. Проверьте подключение и повторите вход.", "No connection to Google. Check your connection and sign in again.", "Немає зв'язку з Google. Перевірте підключення й повторіть вхід.", "Keine Verbindung zu Google. Prüfen Sie die Verbindung und melden Sie sich erneut an.", "Nessuna connessione a Google. Controlla la connessione e ripeti l'accesso.", "Sin conexión con Google. Comprueba la conexión y vuelve a iniciar sesión.", "Pas de connexion à Google. Vérifiez la connexion et reconnectez-vous.", "Sem ligação à Google. Verifique a ligação e inicie sessão novamente.", "لا يوجد اتصال بـ Google. تحقّق من الاتصال وكرّر تسجيل الدخول.", "Google से कोई कनेक्शन नहीं है। कनेक्शन जाँचें और फिर से साइन इन करें।", "Google-এর সঙ্গে সংযোগ নেই। সংযোগ পরীক্ষা করুন এবং আবার সাইন ইন করুন।", "Google سے رابطہ نہیں ہے۔ کنکشن چیک کریں اور دوبارہ سائن اِن کریں۔", "无法连接到 Google。请检查网络连接后重新登录。")
        Add("Не удалось завершить вход в Google. Повторите попытку.", "Sign-in to Google could not be completed. Try again.", "Не вдалося завершити вхід у Google. Повторіть спробу.", "Die Anmeldung bei Google konnte nicht abgeschlossen werden. Versuchen Sie es erneut.", "Impossibile completare l'accesso a Google. Riprova.", "No se pudo completar el inicio de sesión en Google. Inténtalo de nuevo.", "Impossible de terminer la connexion à Google. Réessayez.", "Não foi possível concluir o início de sessão na Google. Tente novamente.", "تعذّر إكمال تسجيل الدخول إلى Google. حاول مرة أخرى.", "Google में साइन-इन पूरा नहीं हो सका। फिर से प्रयास करें।", "Google-এ সাইন-ইন সম্পূর্ণ করা যায়নি। আবার চেষ্টা করুন।", "Google میں سائن اِن مکمل نہیں ہو سکا۔ دوبارہ کوشش کریں۔", "无法完成 Google 登录。请重试。")
        Add("Вы вышли из Google Drive на этом ПК, но подтвердить отзыв доступа у Google не удалось. Отзовите его вручную в настройках аккаунта Google.",
            "You are signed out of Google Drive on this PC, but Google did not confirm that access was revoked. Revoke it manually in your Google Account settings.",
            "Ви вийшли з Google Drive на цьому ПК, але підтвердити відкликання доступу в Google не вдалося. Відкличте його вручну в налаштуваннях облікового запису Google.",
            "Sie sind auf diesem PC von Google Drive abgemeldet, aber Google hat den Widerruf des Zugriffs nicht bestätigt. Widerrufen Sie ihn manuell in den Einstellungen Ihres Google-Kontos.",
            "Sei uscito da Google Drive su questo PC, ma Google non ha confermato la revoca dell'accesso. Revocalo manualmente nelle impostazioni dell'account Google.",
            "Has cerrado sesión en Google Drive en este PC, pero Google no confirmó la revocación del acceso. Revócalo manualmente en la configuración de tu cuenta de Google.",
            "Vous êtes déconnecté de Google Drive sur ce PC, mais Google n'a pas confirmé la révocation de l'accès. Révoquez-le manuellement dans les paramètres de votre compte Google.",
            "Terminou a sessão no Google Drive neste PC, mas a Google não confirmou a revogação do acesso. Revogue-o manualmente nas definições da sua conta Google.",
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
            "Terminou a sessão no Google Drive neste PC. O acesso foi revogado, mas não foi possível eliminar do Google Drive os registos deste PC.",
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
            "Terminou a sessão no Google Drive. O acesso foi revogado; registos deste PC eliminados: {0}.",
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
            "Terminar a sessão no Google Drive neste PC? O acesso será revogado na Google e removido deste PC, e os registos deste PC serão eliminados do seu Google Drive.",
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
            "A aplicação só pode aceder à sua própria pasta oculta no seu Google Drive, e não aos seus ficheiros. O início de sessão é guardado neste PC sob a proteção do Windows.",
            "يمكن للتطبيق الوصول فقط إلى مجلده المخفي الخاص على Google Drive الخاص بك، وليس إلى ملفاتك. يُحفظ تسجيل الدخول على هذا الكمبيوتر بحماية Windows.",
            "ऐप केवल आपके Google Drive पर अपने ही छिपे हुए फ़ोल्डर तक पहुँच सकता है, आपकी फ़ाइलों तक नहीं। साइन-इन इस पीसी पर Windows की सुरक्षा के तहत सहेजा जाता है।",
            "অ্যাপটি আপনার Google Drive-এ শুধু নিজের লুকানো ফোল্ডারে অ্যাক্সেস করতে পারে, আপনার ফাইলে নয়। সাইন-ইন এই পিসিতে Windows-এর সুরক্ষায় সংরক্ষিত থাকে।",
            "ایپ آپ کے Google Drive پر صرف اپنے چھپے ہوئے فولڈر تک رسائی حاصل کر سکتی ہے، آپ کی فائلوں تک نہیں۔ سائن اِن اس پی سی پر Windows کے تحفظ کے تحت محفوظ رہتا ہے۔",
            "应用只能访问您 Google Drive 上属于它自己的隐藏文件夹，而不能访问您的文件。登录信息在这台电脑上受 Windows 保护存储。")
        Add("Имя этого ПК в Google Drive: «{0}». Идентификатор: {1}.", "This PC's name in Google Drive: «{0}». Identifier: {1}.", "Ім'я цього ПК у Google Drive: «{0}». Ідентифікатор: {1}.", "Name dieses PCs in Google Drive: «{0}». Kennung: {1}.", "Nome di questo PC in Google Drive: «{0}». Identificatore: {1}.", "Nombre de este PC en Google Drive: «{0}». Identificador: {1}.", "Nom de ce PC dans Google Drive : « {0} ». Identifiant : {1}.", "Nome deste PC no Google Drive: «{0}». Identificador: {1}.", "اسم هذا الكمبيوتر في Google Drive: «{0}». المعرّف: {1}.", "Google Drive में इस पीसी का नाम: «{0}»। पहचानकर्ता: {1}।", "Google Drive-এ এই পিসির নাম: «{0}»। শনাক্তকারী: {1}।", "Google Drive میں اس پی سی کا نام: «{0}»۔ شناخت کنندہ: {1}۔", "此电脑在 Google Drive 中的名称：「{0}」。标识符：{1}。")
        Add("Ответ получен. Эту вкладку можно закрыть и вернуться в программу.", "Response received. You can close this tab and return to the program.", "Відповідь отримано. Цю вкладку можна закрити й повернутися до програми.", "Antwort empfangen. Sie können diesen Tab schließen und zum Programm zurückkehren.", "Risposta ricevuta. Puoi chiudere questa scheda e tornare al programma.", "Respuesta recibida. Puedes cerrar esta pestaña y volver al programa.", "Réponse reçue. Vous pouvez fermer cet onglet et revenir au programme.", "Resposta recebida. Pode fechar este separador e voltar ao programa.", "تم استلام الرد. يمكنك إغلاق هذه العلامة والعودة إلى البرنامج.", "उत्तर प्राप्त हो गया। आप इस टैब को बंद करके प्रोग्राम पर लौट सकते हैं।", "উত্তর পাওয়া গেছে। আপনি এই ট্যাবটি বন্ধ করে প্রোগ্রামে ফিরে যেতে পারেন।", "جواب موصول ہو گیا۔ آپ اس ٹیب کو بند کر کے پروگرام پر واپس جا سکتے ہیں۔", "已收到响应。您可以关闭此标签页并返回程序。")
    End Sub
End Class
