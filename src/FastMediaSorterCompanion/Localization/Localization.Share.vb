Option Strict On

' <summary>
' Tray menu, the shared ShareText prose and the server-features opt-in dialog.
'
' Columns after the Russian key are, in order:
'   en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh
' Russian, English and Ukrainian are proofread; the other ten are machine
' translations, and the app says so (spec §10.1).
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddShareStrings()

        ' --- tray icon ------------------------------------------------------------

        Add("Менеджер работает в трее. Двойной щелчок по значку открывает окно.",
            "The manager is running in the tray. Double-click the icon to open its window.",
            "Менеджер працює в треї. Подвійний клік по значку відкриває вікно.",
            "Der Manager läuft im Infobereich. Ein Doppelklick auf das Symbol öffnet das Fenster.",
            "Il gestore è in esecuzione nell'area di notifica. Fai doppio clic sull'icona per aprire la finestra.",
            "El gestor se ejecuta en la bandeja. Haz doble clic en el icono para abrir la ventana.",
            "Le gestionnaire tourne dans la zone de notification. Double-cliquez sur l'icône pour ouvrir la fenêtre.",
            "O gestor está a correr na área de notificação. Faça duplo clique no ícone para abrir a janela.",
            "يعمل المدير في شريط المهام. انقر نقرًا مزدوجًا على الأيقونة لفتح النافذة.",
            "प्रबंधक ट्रे में चल रहा है। विंडो खोलने के लिए आइकन पर डबल-क्लिक करें।",
            "ম্যানেজার ট্রেতে চলছে। উইন্ডো খুলতে আইকনে ডাবল-ক্লিক করুন।",
            "مینیجر ٹرے میں چل رہا ہے۔ ونڈو کھولنے کے لیے آئیکن پر ڈبل کلک کریں۔",
            "管理器正在托盘中运行。双击图标可打开窗口。")

        Add("Открыть менеджер общего доступа",
            "Open Share Manager", "Відкрити менеджер спільного доступу", "Freigabe-Manager öffnen",
            "Apri Gestione condivisione", "Abrir el gestor de uso compartido",
            "Ouvrir le gestionnaire de partage", "Abrir o gestor de partilha",
            "فتح مدير المشاركة", "शेयर प्रबंधक खोलें", "শেয়ার ম্যানেজার খুলুন",
            "شیئر مینیجر کھولیں", "打开共享管理器")

        Add("Поделиться..",
            "Share..", "Поділитися..", "Teilen..", "Condividi..", "Compartir..", "Partager..",
            "Partilhar..", "مشاركة..", "साझा करें..", "শেয়ার করুন..", "شیئر کریں..", "共享..")

        Add("Текущее состояние..",
            "Status..", "Поточний стан..", "Status..", "Stato..", "Estado..", "État..",
            "Estado..", "الحالة..", "स्थिति..", "স্ট্যাটাস..", "حالت..", "状态..")

        Add("Описание..",
            "Description..", "Опис..", "Beschreibung..", "Descrizione..", "Descripción..",
            "Description..", "Descrição..", "الوصف..", "विवरण..", "বিবরণ..", "تفصیل..", "说明..")

        Add("Выключить общий доступ",
            "Turn off sharing", "Вимкнути спільний доступ", "Freigabe ausschalten",
            "Disattiva la condivisione", "Desactivar el uso compartido", "Désactiver le partage",
            "Desativar a partilha", "إيقاف المشاركة", "साझाकरण बंद करें", "শেয়ারিং বন্ধ করুন",
            "شیئرنگ بند کریں", "关闭共享")

        Add("Открыть Fast Media Sorter",
            "Open Fast Media Sorter", "Відкрити Fast Media Sorter", "Fast Media Sorter öffnen",
            "Apri Fast Media Sorter", "Abrir Fast Media Sorter", "Ouvrir Fast Media Sorter",
            "Abrir o Fast Media Sorter", "فتح Fast Media Sorter", "Fast Media Sorter खोलें",
            "Fast Media Sorter খুলুন", "Fast Media Sorter کھولیں", "打开 Fast Media Sorter")

        Add("Язык интерфейса",
            "Interface language", "Мова інтерфейсу", "Sprache der Oberfläche",
            "Lingua dell'interfaccia", "Idioma de la interfaz", "Langue de l'interface",
            "Idioma da interface", "لغة الواجهة", "इंटरफ़ेस की भाषा",
            "ইন্টারফেসের ভাষা", "انٹرفیس کی زبان", "界面语言")

        Add("Выход",
            "Exit", "Вихід", "Beenden", "Esci", "Salir", "Quitter", "Sair",
            "خروج", "बाहर निकलें", "প্রস্থান", "خروج", "退出")

        Add("Общий доступ включён",
            "Sharing on", "Спільний доступ увімкнено", "Freigabe aktiv", "Condivisione attiva",
            "Uso compartido activo", "Partage actif", "Partilha ativa",
            "المشاركة مفعّلة", "साझाकरण चालू", "শেয়ারিং চালু", "شیئرنگ آن ہے", "共享已开启")

        ' Tray-balloon fragments, appended after the "Sharing on" line. Leading space
        ' and trailing separator are part of the key - they are what joins the pieces.
        Add(" · подключений: {0}",
            " · conns: {0}", " · з'єднань: {0}", " · Verb.: {0}", " · conn.: {0}", " · con.: {0}",
            " · conn. : {0}", " · lig.: {0}", " · اتصالات: {0}", " · कनेक्शन: {0}", " · সংযোগ: {0}",
            " · کنکشنز: {0}", " · 连接数: {0}")

        Add(" · посл.: {0}",
            " · last: {0}", " · ост.: {0}", " · zuletzt: {0}", " · ultimo: {0}", " · último: {0}",
            " · dernier : {0}", " · último: {0}", " · الأخير: {0}", " · अंतिम: {0}", " · সর্বশেষ: {0}",
            " · آخری: {0}", " · 最近: {0}")

        ' --- ShareText: internet-access prose -------------------------------------

        Add("Внимание: вы открываете SFTP-сервер в интернет. Доступ ко всем общим папкам получит любой, кто узнает адрес, логин и пароль. Не публикуйте QR-код и файл .fmscfg. Выключайте доступ, когда закончили.",
            "Warning: you are exposing an SFTP server to the internet. Anyone who learns the address, username and password can reach every shared folder. Do not publish the QR code or the .fmscfg file. Switch it off when you are done.",
            "Увага: ви відкриваєте SFTP-сервер в інтернет. Доступ до всіх спільних папок отримає будь-хто, хто дізнається адресу, логін і пароль. Не публікуйте QR-код і файл .fmscfg. Вимикайте доступ, коли завершите.",
            "Achtung: Sie stellen einen SFTP-Server ins Internet. Wer Adresse, Benutzername und Kennwort kennt, erreicht jeden freigegebenen Ordner. Veröffentlichen Sie den QR-Code und die .fmscfg-Datei nicht. Schalten Sie die Freigabe aus, wenn Sie fertig sind.",
            "Attenzione: stai esponendo un server SFTP a internet. Chiunque conosca indirizzo, nome utente e password può raggiungere ogni cartella condivisa. Non pubblicare il codice QR né il file .fmscfg. Disattiva la condivisione quando hai finito.",
            "Atención: estás exponiendo un servidor SFTP a internet. Cualquiera que conozca la dirección, el usuario y la contraseña podrá acceder a todas las carpetas compartidas. No publiques el código QR ni el archivo .fmscfg. Desactiva el acceso cuando termines.",
            "Attention : vous exposez un serveur SFTP à internet. Quiconque connaît l'adresse, l'identifiant et le mot de passe atteint chaque dossier partagé. Ne publiez pas le code QR ni le fichier .fmscfg. Désactivez l'accès quand vous avez terminé.",
            "Atenção: está a expor um servidor SFTP à internet. Qualquer pessoa que saiba o endereço, o utilizador e a palavra-passe alcança todas as pastas partilhadas. Não publique o código QR nem o ficheiro .fmscfg. Desative o acesso quando terminar.",
            "تحذير: أنت تعرّض خادم SFTP للإنترنت. أي شخص يعرف العنوان واسم المستخدم وكلمة المرور يمكنه الوصول إلى كل مجلد مشترك. لا تنشر رمز QR ولا ملف ‎.fmscfg‎. أوقف الوصول عند الانتهاء.",
            "चेतावनी: आप एक SFTP सर्वर को इंटरनेट पर खोल रहे हैं। पता, उपयोगकर्ता नाम और पासवर्ड जानने वाला कोई भी हर साझा फ़ोल्डर तक पहुँच सकता है। QR कोड और .fmscfg फ़ाइल प्रकाशित न करें। काम पूरा होने पर पहुँच बंद कर दें।",
            "সতর্কতা: আপনি একটি SFTP সার্ভার ইন্টারনেটে উন্মুক্ত করছেন। ঠিকানা, ব্যবহারকারীর নাম ও পাসওয়ার্ড জানলে যে কেউ প্রতিটি শেয়ার করা ফোল্ডারে পৌঁছাতে পারবে। QR কোড ও .fmscfg ফাইল প্রকাশ করবেন না। কাজ শেষ হলে অ্যাক্সেস বন্ধ করুন।",
            "انتباہ: آپ ایک SFTP سرور کو انٹرنیٹ پر کھول رہے ہیں۔ جو بھی پتہ، صارف نام اور پاس ورڈ جان لے وہ ہر مشترکہ فولڈر تک پہنچ سکتا ہے۔ QR کوڈ اور ‎.fmscfg‎ فائل شائع نہ کریں۔ کام مکمل ہونے پر رسائی بند کر دیں۔",
            "警告：您正在将 SFTP 服务器暴露到互联网。任何知道地址、用户名和密码的人都能访问所有共享文件夹。请勿公开二维码或 .fmscfg 文件。用完后请关闭访问。")

        Add("Работает на телефоне в той же сети Wi-Fi. Ничего настраивать не нужно. Любой, у кого есть этот код или файл, получит доступ к папкам - не показывайте его посторонним.",
            "Works on a phone on the same Wi-Fi. Nothing to configure. Anyone who has this code or file can open the folders - do not show it to others.",
            "Працює на телефоні в тій самій мережі Wi-Fi. Нічого налаштовувати не потрібно. Будь-хто, у кого є цей код або файл, отримає доступ до папок - не показуйте його стороннім.",
            "Funktioniert auf einem Telefon im selben WLAN. Nichts einzurichten. Wer diesen Code oder diese Datei hat, kann die Ordner öffnen - zeigen Sie sie niemandem sonst.",
            "Funziona su un telefono nella stessa rete Wi-Fi. Niente da configurare. Chiunque abbia questo codice o file può aprire le cartelle - non mostrarlo ad altri.",
            "Funciona en un teléfono en la misma red Wi-Fi. No hay nada que configurar. Cualquiera que tenga este código o archivo podrá abrir las carpetas: no se lo muestres a otros.",
            "Fonctionne sur un téléphone sur le même Wi-Fi. Rien à configurer. Quiconque possède ce code ou ce fichier peut ouvrir les dossiers - ne le montrez pas à d'autres.",
            "Funciona num telemóvel na mesma rede Wi-Fi. Nada para configurar. Quem tiver este código ou ficheiro consegue abrir as pastas - não o mostre a outras pessoas.",
            "يعمل على هاتف متصل بنفس شبكة Wi-Fi. لا حاجة لأي إعداد. أي شخص يملك هذا الرمز أو الملف يمكنه فتح المجلدات - لا تُظهره للآخرين.",
            "उसी Wi-Fi से जुड़े फ़ोन पर काम करता है। कुछ भी सेट करने की ज़रूरत नहीं। जिसके पास यह कोड या फ़ाइल है वह फ़ोल्डर खोल सकता है - इसे दूसरों को न दिखाएँ।",
            "একই Wi-Fi-তে থাকা ফোনে কাজ করে। কিছু কনফিগার করার দরকার নেই। এই কোড বা ফাইল যার কাছে আছে সে ফোল্ডারগুলি খুলতে পারবে - অন্যদের দেখাবেন না।",
            "اسی Wi-Fi پر موجود فون پر کام کرتا ہے۔ کچھ سیٹ کرنے کی ضرورت نہیں۔ جس کے پاس یہ کوڈ یا فائل ہو وہ فولڈرز کھول سکتا ہے - اسے دوسروں کو نہ دکھائیں۔",
            "在连接同一 Wi-Fi 的手机上可用，无需任何设置。拥有此二维码或文件的人都能打开这些文件夹 - 请勿出示给他人。")

        Add("Пока раздача включена, устройства в вашей текущей сети (в том числе в публичном Wi-Fi) могут подключиться к этому ПК по паролю. Выключайте раздачу, когда она не нужна.",
            "While sharing is on, devices on your current network (including public Wi-Fi) can reach this PC using the password. Switch sharing off when you don't need it.",
            "Поки роздачу ввімкнено, пристрої у вашій поточній мережі (зокрема в публічному Wi-Fi) можуть підключитися до цього ПК за паролем. Вимикайте роздачу, коли вона не потрібна.",
            "Solange die Freigabe läuft, können Geräte in Ihrem aktuellen Netzwerk (auch in einem öffentlichen WLAN) diesen PC mit dem Kennwort erreichen. Schalten Sie die Freigabe aus, wenn Sie sie nicht brauchen.",
            "Mentre la condivisione è attiva, i dispositivi della rete attuale (anche un Wi-Fi pubblico) possono raggiungere questo PC con la password. Disattiva la condivisione quando non serve.",
            "Mientras el uso compartido está activo, los dispositivos de tu red actual (incluida una Wi-Fi pública) pueden acceder a este PC con la contraseña. Desactívalo cuando no lo necesites.",
            "Tant que le partage est actif, les appareils de votre réseau actuel (y compris un Wi-Fi public) peuvent atteindre ce PC avec le mot de passe. Désactivez le partage quand il est inutile.",
            "Enquanto a partilha estiver ativa, os dispositivos da sua rede atual (incluindo um Wi-Fi público) conseguem alcançar este PC com a palavra-passe. Desative a partilha quando não precisar dela.",
            "أثناء تشغيل المشاركة، يمكن للأجهزة الموجودة على شبكتك الحالية (بما فيها شبكة Wi-Fi عامة) الوصول إلى هذا الحاسوب باستخدام كلمة المرور. أوقف المشاركة عند عدم الحاجة إليها.",
            "जब तक साझाकरण चालू है, आपके मौजूदा नेटवर्क (सार्वजनिक Wi-Fi सहित) के उपकरण पासवर्ड से इस पीसी तक पहुँच सकते हैं। ज़रूरत न होने पर साझाकरण बंद कर दें।",
            "শেয়ারিং চালু থাকা অবস্থায় আপনার বর্তমান নেটওয়ার্কের (পাবলিক Wi-Fi সহ) ডিভাইসগুলি পাসওয়ার্ড দিয়ে এই পিসিতে পৌঁছাতে পারে। প্রয়োজন না হলে শেয়ারিং বন্ধ করুন।",
            "جب تک شیئرنگ آن ہے، آپ کے موجودہ نیٹ ورک (بشمول عوامی Wi-Fi) کے آلات پاس ورڈ کے ذریعے اس پی سی تک پہنچ سکتے ہیں۔ ضرورت نہ ہو تو شیئرنگ بند کر دیں۔",
            "共享开启期间，当前网络（包括公共 Wi-Fi）中的设备可凭密码访问这台电脑。不需要时请关闭共享。")

        Add("Один код на дом и на улицу - телефон сам выберет доступный адрес. В коде есть и интернет-адрес: не публикуйте его, выключайте доступ, когда закончили. Настройка и проверка - на вкладке «Доступ из интернета».",
            "One code for home and away - the phone picks whichever address is reachable. It also carries your internet address: do not publish it, and switch sharing off when done. Setup and testing are on the 'Internet access' tab.",
            "Один код для дому й для вулиці - телефон сам вибере доступну адресу. У коді є й інтернет-адреса: не публікуйте його, вимикайте доступ, коли завершите. Налаштування й перевірка - на вкладці «Доступ з інтернету».",
            "Ein Code für zu Hause und unterwegs - das Telefon wählt die erreichbare Adresse. Er enthält auch Ihre Internetadresse: nicht veröffentlichen und die Freigabe danach ausschalten. Einrichtung und Test finden Sie im Reiter «Internetzugriff».",
            "Un solo codice per casa e fuori casa: il telefono sceglie l'indirizzo raggiungibile. Contiene anche il tuo indirizzo internet: non pubblicarlo e disattiva la condivisione al termine. Configurazione e test nella scheda «Accesso da internet».",
            "Un único código para casa y fuera: el teléfono elige la dirección accesible. También lleva tu dirección de internet: no la publiques y desactiva el acceso al terminar. La configuración y la prueba están en la pestaña «Acceso desde internet».",
            "Un seul code pour la maison et l'extérieur : le téléphone choisit l'adresse joignable. Il contient aussi votre adresse internet : ne la publiez pas et désactivez le partage une fois terminé. Configuration et test dans l'onglet « Accès depuis internet ».",
            "Um único código para casa e fora: o telemóvel escolhe o endereço alcançável. Também leva o seu endereço de internet: não o publique e desative a partilha quando terminar. Configuração e teste no separador «Acesso pela internet».",
            "رمز واحد للمنزل وللخارج - يختار الهاتف العنوان المتاح. يحتوي أيضًا على عنوانك على الإنترنت: لا تنشره، وأوقف المشاركة عند الانتهاء. الإعداد والاختبار في تبويب «الوصول من الإنترنت».",
            "घर और बाहर दोनों के लिए एक ही कोड - फ़ोन खुद उपलब्ध पता चुन लेता है। इसमें आपका इंटरनेट पता भी है: इसे प्रकाशित न करें और काम पूरा होने पर साझाकरण बंद कर दें। सेटअप और जाँच «इंटरनेट पहुँच» टैब में हैं।",
            "ঘরে ও বাইরে - একটাই কোড, ফোন নিজেই উপলব্ধ ঠিকানা বেছে নেয়। এতে আপনার ইন্টারনেট ঠিকানাও আছে: এটি প্রকাশ করবেন না, কাজ শেষে শেয়ারিং বন্ধ করুন। সেটআপ ও পরীক্ষা «ইন্টারনেট অ্যাক্সেস» ট্যাবে।",
            "گھر اور باہر دونوں کے لیے ایک ہی کوڈ - فون خود قابلِ رسائی پتہ منتخب کر لیتا ہے۔ اس میں آپ کا انٹرنیٹ پتہ بھی ہے: اسے شائع نہ کریں اور کام مکمل ہونے پر شیئرنگ بند کر دیں۔ سیٹ اپ اور جانچ «انٹرنیٹ رسائی» ٹیب میں ہیں۔",
            "一个二维码，居家和外出通用 - 手机会自动选择可达的地址。其中也包含您的互联网地址：请勿公开，用完后请关闭共享。设置和测试在「互联网访问」选项卡中。")

        Add("Не включать пароль в файл/QR",
            "Keep the password out of the file/QR", "Не включати пароль до файлу/QR",
            "Kennwort nicht in Datei/QR aufnehmen", "Non includere la password nel file/QR",
            "No incluir la contraseña en el archivo/QR", "Ne pas inclure le mot de passe dans le fichier/QR",
            "Não incluir a palavra-passe no ficheiro/QR", "عدم تضمين كلمة المرور في الملف/رمز QR",
            "पासवर्ड को फ़ाइल/QR में शामिल न करें", "ফাইল/QR-এ পাসওয়ার্ড অন্তর্ভুক্ত করবেন না",
            "پاس ورڈ کو فائل/QR میں شامل نہ کریں", "不要将密码写入文件/二维码")

        Add("Слишком много настроек для QR-кода - сохраните файл .fmscfg и отправьте его на телефон.",
            "Too many settings for a QR code - save the .fmscfg file and send it to the phone instead.",
            "Забагато налаштувань для QR-коду - збережіть файл .fmscfg і надішліть його на телефон.",
            "Zu viele Einstellungen für einen QR-Code - speichern Sie die .fmscfg-Datei und senden Sie sie ans Telefon.",
            "Troppe impostazioni per un codice QR: salva il file .fmscfg e invialo al telefono.",
            "Demasiados ajustes para un código QR: guarda el archivo .fmscfg y envíalo al teléfono.",
            "Trop de réglages pour un code QR - enregistrez le fichier .fmscfg et envoyez-le au téléphone.",
            "Demasiadas definições para um código QR - guarde o ficheiro .fmscfg e envie-o para o telemóvel.",
            "الإعدادات كثيرة على رمز QR - احفظ ملف ‎.fmscfg‎ وأرسله إلى الهاتف بدلاً من ذلك.",
            "QR कोड के लिए बहुत अधिक सेटिंग्स - .fmscfg फ़ाइल सहेजें और उसे फ़ोन पर भेजें।",
            "QR কোডের জন্য অনেক বেশি সেটিং - .fmscfg ফাইল সংরক্ষণ করে ফোনে পাঠান।",
            "QR کوڈ کے لیے بہت زیادہ ترتیبات - ‎.fmscfg‎ فائل محفوظ کر کے فون پر بھیجیں۔",
            "设置项过多，二维码放不下 - 请保存 .fmscfg 文件并发送到手机。")

        Add("Не удалось определить адрес этого ПК в локальной сети (проверьте, не включён ли VPN). Если телефон рядом, в той же сети, остановите и снова начните общий доступ, чтобы попробовать ещё раз - сейчас в QR-коде есть только адрес из интернета.",
            "Could not determine this PC's local-network address (check whether a VPN is active). If the phone is nearby on the same network, stop and start sharing again to retry - for now, only the internet address is in the QR code.",
            "Не вдалося визначити адресу цього ПК у локальній мережі (перевірте, чи не ввімкнено VPN). Якщо телефон поруч, у тій самій мережі, зупиніть і знову увімкніть спільний доступ, щоб спробувати ще раз - зараз у QR-коді є лише адреса з інтернету.",
            "Die Adresse dieses PCs im lokalen Netzwerk konnte nicht ermittelt werden (prüfen Sie, ob ein VPN aktiv ist). Ist das Telefon im selben Netzwerk in der Nähe, beenden und starten Sie die Freigabe erneut - derzeit enthält der QR-Code nur die Internetadresse.",
            "Impossibile determinare l'indirizzo di questo PC nella rete locale (verifica se è attiva una VPN). Se il telefono è vicino, sulla stessa rete, ferma e riavvia la condivisione per riprovare: per ora il codice QR contiene solo l'indirizzo internet.",
            "No se pudo determinar la dirección de este PC en la red local (comprueba si hay una VPN activa). Si el teléfono está cerca, en la misma red, detén y vuelve a iniciar el uso compartido para reintentarlo: por ahora el código QR solo lleva la dirección de internet.",
            "Impossible de déterminer l'adresse de ce PC sur le réseau local (vérifiez si un VPN est actif). Si le téléphone est à proximité sur le même réseau, arrêtez puis relancez le partage pour réessayer - pour l'instant, le code QR ne contient que l'adresse internet.",
            "Não foi possível determinar o endereço deste PC na rede local (verifique se há uma VPN ativa). Se o telemóvel estiver perto, na mesma rede, pare e volte a iniciar a partilha para tentar de novo - por agora o código QR só tem o endereço de internet.",
            "تعذّر تحديد عنوان هذا الحاسوب في الشبكة المحلية (تحقّق مما إذا كانت VPN مفعّلة). إذا كان الهاتف قريبًا على الشبكة نفسها، أوقف المشاركة ثم شغّلها من جديد للمحاولة مرة أخرى - حاليًا يحتوي رمز QR على عنوان الإنترنت فقط.",
            "इस पीसी का स्थानीय नेटवर्क पता निर्धारित नहीं हो सका (देखें कि कोई VPN सक्रिय तो नहीं)। यदि फ़ोन उसी नेटवर्क पर पास है, तो साझाकरण बंद करके फिर से चालू करें - अभी QR कोड में केवल इंटरनेट पता है।",
            "এই পিসির স্থানীয় নেটওয়ার্ক ঠিকানা নির্ধারণ করা যায়নি (VPN চালু আছে কি না দেখুন)। ফোনটি একই নেটওয়ার্কে কাছাকাছি থাকলে শেয়ারিং বন্ধ করে আবার চালু করুন - এখন QR কোডে শুধু ইন্টারনেট ঠিকানা আছে।",
            "اس پی سی کا مقامی نیٹ ورک پتہ معلوم نہ ہو سکا (دیکھیں کہ VPN آن تو نہیں)۔ اگر فون اسی نیٹ ورک پر قریب ہے تو شیئرنگ بند کر کے دوبارہ شروع کریں - فی الحال QR کوڈ میں صرف انٹرنیٹ پتہ ہے۔",
            "无法确定这台电脑在本地网络中的地址（请检查是否启用了 VPN）。如果手机就在同一网络附近，请停止再重新开启共享以重试 - 目前二维码中只有互联网地址。")

        Add("Порт открыт автоматически (UPnP), но проверить, что провайдер не использует скрытый NAT, не удалось (не ответил внешний сервис проверки IP). Адрес может не работать извне - обязательно проверьте с телефона по мобильной сети.",
            "The port was opened automatically (UPnP), but checking whether your provider uses a hidden NAT failed (the external IP-check service did not respond). This address may not work from outside - be sure to test it from the phone on mobile data.",
            "Порт відкрито автоматично (UPnP), але перевірити, чи не використовує провайдер прихований NAT, не вдалося (не відповів зовнішній сервіс перевірки IP). Адреса може не працювати ззовні - обов'язково перевірте з телефона через мобільну мережу.",
            "Der Port wurde automatisch geöffnet (UPnP), aber die Prüfung auf ein verstecktes NAT des Anbieters schlug fehl (der externe IP-Prüfdienst antwortete nicht). Die Adresse funktioniert von außen möglicherweise nicht - testen Sie sie unbedingt vom Telefon über Mobilfunk.",
            "La porta è stata aperta automaticamente (UPnP), ma non è stato possibile verificare se il provider usa un NAT nascosto (il servizio esterno di controllo IP non ha risposto). L'indirizzo potrebbe non funzionare dall'esterno: provalo dal telefono in rete mobile.",
            "El puerto se abrió automáticamente (UPnP), pero no se pudo comprobar si tu proveedor usa un NAT oculto (el servicio externo de comprobación de IP no respondió). Puede que la dirección no funcione desde fuera: pruébala desde el teléfono con datos móviles.",
            "Le port a été ouvert automatiquement (UPnP), mais la vérification d'un NAT caché chez votre fournisseur a échoué (le service externe de contrôle d'IP n'a pas répondu). L'adresse peut ne pas fonctionner depuis l'extérieur - testez-la depuis le téléphone en données mobiles.",
            "A porta foi aberta automaticamente (UPnP), mas não foi possível verificar se o operador usa um NAT oculto (o serviço externo de verificação de IP não respondeu). O endereço pode não funcionar do exterior - teste-o a partir do telemóvel com dados móveis.",
            "تم فتح المنفذ تلقائيًا (UPnP)، لكن تعذّر التحقق مما إذا كان مزوّد الخدمة يستخدم NAT مخفيًا (لم تستجب خدمة فحص IP الخارجية). قد لا يعمل هذا العنوان من الخارج - تأكد من اختباره من الهاتف عبر بيانات الجوال.",
            "पोर्ट स्वतः खुल गया (UPnP), लेकिन यह जाँचा नहीं जा सका कि प्रदाता छिपे NAT का उपयोग करता है या नहीं (बाहरी IP-जाँच सेवा ने उत्तर नहीं दिया)। यह पता बाहर से काम नहीं भी कर सकता - इसे मोबाइल डेटा पर फ़ोन से अवश्य जाँचें।",
            "পোর্টটি স্বয়ংক্রিয়ভাবে খোলা হয়েছে (UPnP), তবে প্রদানকারী গোপন NAT ব্যবহার করে কি না তা যাচাই করা যায়নি (বাহ্যিক IP-যাচাই পরিষেবা সাড়া দেয়নি)। ঠিকানাটি বাইরে থেকে কাজ নাও করতে পারে - মোবাইল ডেটায় ফোন থেকে অবশ্যই পরীক্ষা করুন।",
            "پورٹ خودکار طور پر کھل گیا (UPnP)، مگر یہ جانچا نہ جا سکا کہ فراہم کنندہ پوشیدہ NAT استعمال کرتا ہے یا نہیں (بیرونی IP جانچ سروس نے جواب نہیں دیا)۔ یہ پتہ باہر سے کام نہ بھی کرے - موبائل ڈیٹا پر فون سے ضرور آزمائیں۔",
            "端口已自动打开（UPnP），但无法确认运营商是否使用了隐藏 NAT（外部 IP 检测服务未响应）。该地址在外网可能无法使用 - 请务必用手机通过移动数据测试。")

        Add("Ваш провайдер использует CGNAT (общий внешний адрес). Проброс портов не поможет - извне к этому ПК подключиться нельзя. По локальной сети всё работает как обычно.",
            "Your ISP uses CGNAT (a shared public address). Port forwarding will not help - this PC cannot be reached from outside. Local-network sharing works as usual.",
            "Ваш провайдер використовує CGNAT (спільну зовнішню адресу). Проброс портів не допоможе - ззовні до цього ПК підключитися не можна. У локальній мережі все працює як завжди.",
            "Ihr Anbieter nutzt CGNAT (eine gemeinsame öffentliche Adresse). Portweiterleitung hilft nicht - dieser PC ist von außen nicht erreichbar. Im lokalen Netzwerk funktioniert alles wie gewohnt.",
            "Il tuo provider usa CGNAT (un indirizzo pubblico condiviso). L'inoltro delle porte non serve: questo PC non è raggiungibile dall'esterno. Sulla rete locale funziona tutto come sempre.",
            "Tu proveedor usa CGNAT (una dirección pública compartida). Abrir puertos no ayudará: no se puede llegar a este PC desde fuera. En la red local todo funciona igual.",
            "Votre fournisseur utilise le CGNAT (adresse publique partagée). La redirection de port n'y changera rien - ce PC est injoignable depuis l'extérieur. Sur le réseau local, tout fonctionne normalement.",
            "O seu operador usa CGNAT (endereço público partilhado). O encaminhamento de portas não ajuda - este PC não é alcançável do exterior. Na rede local tudo funciona como habitualmente.",
            "يستخدم مزوّد خدمتك تقنية CGNAT (عنوان عام مشترك). لن يفيد إعادة توجيه المنافذ - لا يمكن الوصول إلى هذا الحاسوب من الخارج. أما على الشبكة المحلية فكل شيء يعمل كالمعتاد.",
            "आपका प्रदाता CGNAT (साझा सार्वजनिक पता) उपयोग करता है। पोर्ट फ़ॉरवर्डिंग से मदद नहीं मिलेगी - इस पीसी तक बाहर से नहीं पहुँचा जा सकता। स्थानीय नेटवर्क पर सब सामान्य रूप से काम करता है।",
            "আপনার প্রদানকারী CGNAT (শেয়ার করা পাবলিক ঠিকানা) ব্যবহার করে। পোর্ট ফরওয়ার্ডিং সাহায্য করবে না - বাইরে থেকে এই পিসিতে পৌঁছানো যাবে না। স্থানীয় নেটওয়ার্কে সবকিছু আগের মতোই কাজ করে।",
            "آپ کا فراہم کنندہ CGNAT (مشترکہ عوامی پتہ) استعمال کرتا ہے۔ پورٹ فارورڈنگ سے فائدہ نہیں ہوگا - باہر سے اس پی سی تک نہیں پہنچا جا سکتا۔ مقامی نیٹ ورک پر سب معمول کے مطابق کام کرتا ہے۔",
            "您的运营商使用 CGNAT（共享公网地址）。端口转发无济于事 - 外网无法访问这台电脑。局域网内一切照常。")

        ' --- ShareText: accessNote (embedded in the .fmscfg, shown on the phone) ----

        Add("Работает в той же сети Wi-Fi (интернет-адрес намеренно не включён).",
            "Reachable on the same Wi-Fi as this PC (internet address intentionally left out).",
            "Працює в тій самій мережі Wi-Fi (інтернет-адресу навмисно не включено).",
            "Im selben WLAN erreichbar (die Internetadresse wurde bewusst weggelassen).",
            "Raggiungibile sulla stessa rete Wi-Fi (indirizzo internet volutamente escluso).",
            "Accesible en la misma red Wi-Fi (la dirección de internet se omitió a propósito).",
            "Joignable sur le même Wi-Fi (l'adresse internet a été volontairement omise).",
            "Alcançável na mesma rede Wi-Fi (endereço de internet deixado de fora de propósito).",
            "يمكن الوصول إليه على شبكة Wi-Fi نفسها (تم استبعاد عنوان الإنترنت عمدًا).",
            "उसी Wi-Fi पर उपलब्ध (इंटरनेट पता जानबूझकर शामिल नहीं किया गया)।",
            "একই Wi-Fi-তে পৌঁছানো যায় (ইন্টারনেট ঠিকানা ইচ্ছাকৃতভাবে বাদ দেওয়া হয়েছে)।",
            "اسی Wi-Fi پر قابلِ رسائی (انٹرنیٹ پتہ جان بوجھ کر شامل نہیں کیا گیا)۔",
            "可在同一 Wi-Fi 下访问（已刻意不包含互联网地址）。")

        Add("Работает по Wi-Fi и из интернета - проброшенный порт ответил на внешнюю проверку.",
            "Reachable on your Wi-Fi and over the internet - the forwarded port passed an external test.",
            "Працює по Wi-Fi і з інтернету - проброшений порт відповів на зовнішню перевірку.",
            "Über WLAN und Internet erreichbar - der weitergeleitete Port hat einen externen Test bestanden.",
            "Raggiungibile via Wi-Fi e da internet: la porta inoltrata ha superato un test esterno.",
            "Accesible por Wi-Fi y desde internet: el puerto redirigido superó una prueba externa.",
            "Joignable en Wi-Fi et depuis internet - le port redirigé a passé un test externe.",
            "Alcançável por Wi-Fi e pela internet - a porta encaminhada passou num teste externo.",
            "يمكن الوصول إليه عبر Wi-Fi ومن الإنترنت - نجح المنفذ المُعاد توجيهه في اختبار خارجي.",
            "Wi-Fi और इंटरनेट दोनों से उपलब्ध - फ़ॉरवर्ड किया गया पोर्ट बाहरी जाँच में सफल रहा।",
            "Wi-Fi ও ইন্টারনেট উভয় থেকেই পৌঁছানো যায় - ফরওয়ার্ড করা পোর্ট বাহ্যিক পরীক্ষায় উত্তীর্ণ।",
            "Wi-Fi اور انٹرنیٹ دونوں سے قابلِ رسائی - فارورڈ کیا گیا پورٹ بیرونی جانچ میں کامیاب رہا۔",
            "可通过 Wi-Fi 和互联网访问 - 转发的端口已通过外部测试。")

        Add("Работает только в той же сети Wi-Fi. Провайдер использует CGNAT - проброс порта не сработает; используйте адрес IPv6 (если показан) или VPN.",
            "Reachable only on the same Wi-Fi. Your ISP uses CGNAT, so a forwarded port cannot work - use the IPv6 address if shown, or a VPN/relay.",
            "Працює лише в тій самій мережі Wi-Fi. Провайдер використовує CGNAT - проброс порту не спрацює; використовуйте адресу IPv6 (якщо показана) або VPN.",
            "Nur im selben WLAN erreichbar. Ihr Anbieter nutzt CGNAT, ein weitergeleiteter Port kann nicht funktionieren - nutzen Sie die IPv6-Adresse, falls angezeigt, oder ein VPN.",
            "Raggiungibile solo sulla stessa rete Wi-Fi. Il provider usa CGNAT, quindi una porta inoltrata non può funzionare: usa l'indirizzo IPv6 se mostrato, oppure una VPN.",
            "Solo accesible en la misma red Wi-Fi. Tu proveedor usa CGNAT, así que redirigir un puerto no funciona: usa la dirección IPv6 si aparece, o una VPN.",
            "Joignable uniquement sur le même Wi-Fi. Votre fournisseur utilise le CGNAT : un port redirigé ne peut pas fonctionner - utilisez l'adresse IPv6 si elle est affichée, ou un VPN.",
            "Alcançável apenas na mesma rede Wi-Fi. O operador usa CGNAT, por isso uma porta encaminhada não funciona - use o endereço IPv6 se aparecer, ou uma VPN.",
            "يمكن الوصول إليه على شبكة Wi-Fi نفسها فقط. يستخدم مزوّدك تقنية CGNAT، لذا لن يعمل المنفذ المُعاد توجيهه - استخدم عنوان IPv6 إن ظهر، أو شبكة VPN.",
            "केवल उसी Wi-Fi पर उपलब्ध। आपका प्रदाता CGNAT उपयोग करता है, इसलिए फ़ॉरवर्ड किया गया पोर्ट काम नहीं करेगा - दिखे तो IPv6 पता, अन्यथा VPN का उपयोग करें।",
            "শুধু একই Wi-Fi-তে পৌঁছানো যায়। আপনার প্রদানকারী CGNAT ব্যবহার করে, তাই ফরওয়ার্ড করা পোর্ট কাজ করবে না - দেখানো হলে IPv6 ঠিকানা, নয়তো VPN ব্যবহার করুন।",
            "صرف اسی Wi-Fi پر قابلِ رسائی۔ آپ کا فراہم کنندہ CGNAT استعمال کرتا ہے، اس لیے فارورڈ کیا گیا پورٹ کام نہیں کرے گا - دکھایا جائے تو IPv6 پتہ، ورنہ VPN استعمال کریں۔",
            "仅可在同一 Wi-Fi 下访问。您的运营商使用 CGNAT，端口转发无法生效 - 请使用显示的 IPv6 地址，或使用 VPN。")

        Add("Работает по Wi-Fi и по IPv6 из сетей, где он поддерживается. Обычный проброс порта на этом подключении недоступен.",
            "Reachable on your Wi-Fi, and over IPv6 from networks that support it. A plain port-forward is not available on this connection.",
            "Працює по Wi-Fi і по IPv6 з мереж, де він підтримується. Звичайний проброс порту на цьому з'єднанні недоступний.",
            "Über WLAN erreichbar und per IPv6 aus Netzwerken, die es unterstützen. Eine einfache Portweiterleitung ist bei dieser Verbindung nicht verfügbar.",
            "Raggiungibile via Wi-Fi e via IPv6 dalle reti che lo supportano. Su questa connessione non è disponibile un semplice inoltro di porta.",
            "Accesible por Wi-Fi y por IPv6 desde redes que lo admitan. En esta conexión no hay redirección de puertos sencilla.",
            "Joignable en Wi-Fi et en IPv6 depuis les réseaux qui le prennent en charge. Une simple redirection de port n'est pas disponible sur cette connexion.",
            "Alcançável por Wi-Fi e por IPv6 a partir de redes que o suportem. Nesta ligação não há encaminhamento de portas simples.",
            "يمكن الوصول إليه عبر Wi-Fi وعبر IPv6 من الشبكات التي تدعمه. إعادة توجيه المنافذ العادية غير متاحة على هذا الاتصال.",
            "Wi-Fi से और IPv6 का समर्थन करने वाले नेटवर्क से उपलब्ध। इस कनेक्शन पर सामान्य पोर्ट फ़ॉरवर्डिंग उपलब्ध नहीं है।",
            "Wi-Fi থেকে এবং IPv6 সমর্থনকারী নেটওয়ার্ক থেকে পৌঁছানো যায়। এই সংযোগে সাধারণ পোর্ট ফরওয়ার্ডিং নেই।",
            "Wi-Fi سے اور ان نیٹ ورکس سے IPv6 کے ذریعے قابلِ رسائی جو اسے سپورٹ کرتے ہیں۔ اس کنکشن پر عام پورٹ فارورڈنگ دستیاب نہیں۔",
            "可通过 Wi-Fi 访问，也可从支持 IPv6 的网络经由 IPv6 访问。此连接不支持普通端口转发。")

        Add("Работает по Wi-Fi и, если проброс/UPnP держится, из интернета - проверьте с телефона по мобильной сети.",
            "Reachable on your Wi-Fi and, if the router forward/UPnP holds, over the internet - confirm from the phone on mobile data.",
            "Працює по Wi-Fi і, якщо проброс/UPnP тримається, з інтернету - перевірте з телефона через мобільну мережу.",
            "Über WLAN erreichbar und, wenn die Weiterleitung/UPnP hält, auch über das Internet - prüfen Sie es vom Telefon über Mobilfunk.",
            "Raggiungibile via Wi-Fi e, se l'inoltro/UPnP regge, da internet: verifica dal telefono in rete mobile.",
            "Accesible por Wi-Fi y, si la redirección/UPnP se mantiene, desde internet: compruébalo desde el teléfono con datos móviles.",
            "Joignable en Wi-Fi et, si la redirection/UPnP tient, depuis internet - vérifiez-le depuis le téléphone en données mobiles.",
            "Alcançável por Wi-Fi e, se o encaminhamento/UPnP se mantiver, pela internet - confirme a partir do telemóvel com dados móveis.",
            "يمكن الوصول إليه عبر Wi-Fi، ومن الإنترنت إذا ظلّت إعادة التوجيه/UPnP فعّالة - تحقّق من ذلك من الهاتف عبر بيانات الجوال.",
            "Wi-Fi से उपलब्ध और, यदि फ़ॉरवर्ड/UPnP बना रहे, तो इंटरनेट से भी - मोबाइल डेटा पर फ़ोन से पुष्टि करें।",
            "Wi-Fi থেকে পৌঁছানো যায় এবং ফরওয়ার্ড/UPnP টিকে থাকলে ইন্টারনেট থেকেও - মোবাইল ডেটায় ফোন থেকে নিশ্চিত করুন।",
            "Wi-Fi سے قابلِ رسائی اور اگر فارورڈ/UPnP برقرار رہے تو انٹرنیٹ سے بھی - موبائل ڈیٹا پر فون سے تصدیق کریں۔",
            "可通过 Wi-Fi 访问；若路由器转发/UPnP 有效，也可从互联网访问 - 请用手机通过移动数据确认。")

        ' --- ShareText: "what works now / what to do next" under the address grid ---

        Add("Проверяем, что доступно с телефона..",
            "Checking what the phone can reach..", "Перевіряємо, що доступно з телефона..",
            "Es wird geprüft, was das Telefon erreichen kann..",
            "Verifica di ciò che il telefono può raggiungere..",
            "Comprobando a qué puede llegar el teléfono..",
            "Vérification de ce que le téléphone peut joindre..",
            "A verificar o que o telemóvel consegue alcançar..",
            "جارٍ التحقق مما يمكن للهاتف الوصول إليه..",
            "जाँच रहे हैं कि फ़ोन कहाँ तक पहुँच सकता है..",
            "ফোন কোথায় পৌঁছাতে পারে তা যাচাই করা হচ্ছে..",
            "جانچا جا رہا ہے کہ فون کہاں تک پہنچ سکتا ہے..",
            "正在检查手机可以访问哪些地址..")

        Add("Сейчас работает только в вашей сети Wi-Fi. Провайдер использует CGNAT - доступ через интернет невозможен.",
            "Right now this works only on your own Wi-Fi. Your ISP uses CGNAT - access over the internet is not possible.",
            "Зараз працює лише у вашій мережі Wi-Fi. Провайдер використовує CGNAT - доступ через інтернет неможливий.",
            "Derzeit funktioniert das nur in Ihrem eigenen WLAN. Ihr Anbieter nutzt CGNAT - ein Zugriff über das Internet ist nicht möglich.",
            "Al momento funziona solo sulla tua rete Wi-Fi. Il provider usa CGNAT: l'accesso da internet non è possibile.",
            "Ahora mismo solo funciona en tu propia red Wi-Fi. Tu proveedor usa CGNAT: el acceso desde internet no es posible.",
            "Pour l'instant, cela ne fonctionne que sur votre propre Wi-Fi. Votre fournisseur utilise le CGNAT - l'accès depuis internet est impossible.",
            "Neste momento só funciona na sua própria rede Wi-Fi. O operador usa CGNAT - o acesso pela internet não é possível.",
            "يعمل هذا حاليًا على شبكة Wi-Fi الخاصة بك فقط. يستخدم مزوّدك تقنية CGNAT - الوصول عبر الإنترنت غير ممكن.",
            "अभी यह केवल आपके अपने Wi-Fi पर काम करता है। आपका प्रदाता CGNAT उपयोग करता है - इंटरनेट से पहुँच संभव नहीं है।",
            "এখন এটি কেবল আপনার নিজের Wi-Fi-তে কাজ করে। আপনার প্রদানকারী CGNAT ব্যবহার করে - ইন্টারনেট থেকে প্রবেশ সম্ভব নয়।",
            "ابھی یہ صرف آپ کے اپنے Wi-Fi پر کام کرتا ہے۔ آپ کا فراہم کنندہ CGNAT استعمال کرتا ہے - انٹرنیٹ سے رسائی ممکن نہیں۔",
            "目前只能在您自己的 Wi-Fi 网络中访问。您的运营商使用 CGNAT - 无法通过互联网访问。")

        Add("Работает и в вашей сети Wi-Fi, и через интернет - внешний порт ответил на проверку снаружи.",
            "Works both on your Wi-Fi and over the internet - the external port answered a check from outside.",
            "Працює і у вашій мережі Wi-Fi, і через інтернет - зовнішній порт відповів на перевірку ззовні.",
            "Funktioniert sowohl in Ihrem WLAN als auch über das Internet - der externe Port hat auf eine Prüfung von außen geantwortet.",
            "Funziona sia sulla tua rete Wi-Fi sia da internet: la porta esterna ha risposto a un controllo dall'esterno.",
            "Funciona tanto en tu Wi-Fi como desde internet: el puerto externo respondió a una comprobación desde fuera.",
            "Fonctionne à la fois sur votre Wi-Fi et depuis internet - le port externe a répondu à un test depuis l'extérieur.",
            "Funciona tanto na sua rede Wi-Fi como pela internet - a porta externa respondeu a uma verificação a partir de fora.",
            "يعمل على شبكة Wi-Fi لديك ومن الإنترنت معًا - استجاب المنفذ الخارجي لفحص من الخارج.",
            "आपके Wi-Fi पर और इंटरनेट से, दोनों तरह काम करता है - बाहरी पोर्ट ने बाहर से की गई जाँच का उत्तर दिया।",
            "আপনার Wi-Fi-তে এবং ইন্টারনেট থেকে - দুইভাবেই কাজ করে; বাইরের পোর্ট বাইরে থেকে করা পরীক্ষায় সাড়া দিয়েছে।",
            "آپ کے Wi-Fi پر اور انٹرنیٹ سے، دونوں طرح کام کرتا ہے - بیرونی پورٹ نے باہر سے کی گئی جانچ کا جواب دیا۔",
            "在您的 Wi-Fi 和互联网上均可访问 - 外部端口已响应来自外网的检测。")

        ' {0} = the port the outside world would knock on (mapped external port, else listen port).
        Add("Сейчас работает только в вашей сети Wi-Fi. Порт {0} открыт на роутере автоматически (UPnP), но снаружи не отвечает - похоже, входящие подключения закрывает провайдер.",
            "For now this works only on your Wi-Fi. Port {0} was opened on the router automatically (UPnP), but it does not answer from outside - your provider is probably blocking inbound connections.",
            "Зараз працює лише у вашій мережі Wi-Fi. Порт {0} відкрито на роутері автоматично (UPnP), але ззовні він не відповідає - схоже, вхідні підключення закриває провайдер.",
            "Derzeit funktioniert das nur in Ihrem WLAN. Port {0} wurde am Router automatisch geöffnet (UPnP), antwortet von außen aber nicht - vermutlich blockiert Ihr Anbieter eingehende Verbindungen.",
            "Per ora funziona solo nella tua rete Wi-Fi. La porta {0} è stata aperta sul router automaticamente (UPnP), ma dall'esterno non risponde: probabilmente il provider blocca le connessioni in entrata.",
            "Por ahora solo funciona en tu red Wi-Fi. El puerto {0} se abrió en el router automáticamente (UPnP), pero no responde desde fuera: seguramente tu proveedor bloquea las conexiones entrantes.",
            "Pour l'instant, cela ne fonctionne que sur votre Wi-Fi. Le port {0} a été ouvert automatiquement sur le routeur (UPnP), mais il ne répond pas de l'extérieur - votre fournisseur bloque probablement les connexions entrantes.",
            "Por agora funciona apenas na sua rede Wi-Fi. A porta {0} foi aberta no router automaticamente (UPnP), mas não responde do exterior - o seu operador deve estar a bloquear ligações de entrada.",
            "يعمل حاليًا داخل شبكة Wi-Fi لديك فقط. فُتح المنفذ {0} على الموجّه تلقائيًا (UPnP)، لكنه لا يستجيب من الخارج - على الأرجح يحجب المزوّد الاتصالات الواردة.",
            "अभी यह केवल आपके Wi-Fi नेटवर्क में काम करता है। पोर्ट {0} राउटर पर स्वतः खुल गया (UPnP), पर बाहर से उत्तर नहीं देता - संभवतः आपका प्रदाता आने वाले कनेक्शन रोक रहा है।",
            "এখন এটি কেবল আপনার Wi-Fi নেটওয়ার্কে কাজ করে। পোর্ট {0} রাউটারে স্বয়ংক্রিয়ভাবে খোলা হয়েছে (UPnP), কিন্তু বাইরে থেকে সাড়া দেয় না - সম্ভবত আপনার প্রোভাইডার ইনকামিং সংযোগ আটকে রাখছে।",
            "ابھی یہ صرف آپ کے Wi-Fi نیٹ ورک میں کام کرتا ہے۔ پورٹ {0} روٹر پر خودکار طور پر کھل گیا (UPnP)، مگر باہر سے جواب نہیں دیتا - غالباً آپ کا فراہم کنندہ آنے والے کنکشنز روک رہا ہے۔",
            "目前只能在您的 Wi-Fi 网络中使用。端口 {0} 已由路由器自动打开（UPnP），但从外部没有响应 - 可能是运营商屏蔽了入站连接。")

        Add("Сейчас работает только в вашей сети Wi-Fi. Порт {0} снаружи не отвечает - похоже, на роутере нет проброса.",
            "Right now this works only on your own Wi-Fi. Port {0} does not answer from outside - the router most likely has no forward for it.",
            "Зараз працює лише у вашій мережі Wi-Fi. Порт {0} ззовні не відповідає - схоже, на роутері немає проброса.",
            "Derzeit funktioniert das nur in Ihrem eigenen WLAN. Port {0} antwortet von außen nicht - im Router fehlt vermutlich die Weiterleitung.",
            "Al momento funziona solo sulla tua rete Wi-Fi. La porta {0} non risponde dall'esterno: probabilmente sul router manca l'inoltro.",
            "Ahora mismo solo funciona en tu propia red Wi-Fi. El puerto {0} no responde desde fuera: es probable que falte la redirección en el router.",
            "Pour l'instant, cela ne fonctionne que sur votre propre Wi-Fi. Le port {0} ne répond pas depuis l'extérieur - la redirection manque sans doute sur le routeur.",
            "Neste momento só funciona na sua própria rede Wi-Fi. A porta {0} não responde a partir de fora - provavelmente falta o encaminhamento no router.",
            "يعمل هذا حاليًا على شبكة Wi-Fi الخاصة بك فقط. المنفذ {0} لا يستجيب من الخارج - على الأرجح لا توجد إعادة توجيه له في الموجّه.",
            "अभी यह केवल आपके अपने Wi-Fi पर काम करता है। पोर्ट {0} बाहर से उत्तर नहीं देता - संभवतः राउटर में उसका फ़ॉरवर्ड नहीं है।",
            "এখন এটি কেবল আপনার নিজের Wi-Fi-তে কাজ করে। পোর্ট {0} বাইরে থেকে সাড়া দেয় না - সম্ভবত রাউটারে এর ফরওয়ার্ড নেই।",
            "ابھی یہ صرف آپ کے اپنے Wi-Fi پر کام کرتا ہے۔ پورٹ {0} باہر سے جواب نہیں دیتا - غالباً روٹر میں اس کا فارورڈ نہیں ہے۔",
            "目前只能在您自己的 Wi-Fi 网络中访问。端口 {0} 在外网没有响应 - 路由器很可能未配置转发。")

        Add("Сейчас надёжно работает только в вашей сети Wi-Fi. Порт открыт автоматически (UPnP), но снаружи это ещё не проверено.",
            "Right now only your own Wi-Fi is certain. The port was opened automatically (UPnP), but that has not been confirmed from outside yet.",
            "Зараз надійно працює лише у вашій мережі Wi-Fi. Порт відкрито автоматично (UPnP), але ззовні це ще не перевірено.",
            "Sicher ist derzeit nur Ihr eigenes WLAN. Der Port wurde automatisch geöffnet (UPnP), von außen ist das aber noch nicht bestätigt.",
            "Al momento è sicuro solo sulla tua rete Wi-Fi. La porta è stata aperta automaticamente (UPnP), ma dall'esterno non è ancora confermato.",
            "Ahora mismo solo es seguro en tu propia red Wi-Fi. El puerto se abrió automáticamente (UPnP), pero aún no se ha confirmado desde fuera.",
            "Pour l'instant, seul votre propre Wi-Fi est certain. Le port a été ouvert automatiquement (UPnP), mais cela n'a pas encore été confirmé depuis l'extérieur.",
            "Neste momento só a sua própria rede Wi-Fi é certa. A porta foi aberta automaticamente (UPnP), mas isso ainda não foi confirmado a partir de fora.",
            "المؤكَّد حاليًا هو شبكة Wi-Fi الخاصة بك فقط. فُتح المنفذ تلقائيًا (UPnP)، لكن لم يتم التأكد من ذلك من الخارج بعد.",
            "अभी निश्चित रूप से केवल आपका अपना Wi-Fi काम करता है। पोर्ट स्वतः खुला (UPnP), पर बाहर से इसकी पुष्टि अभी नहीं हुई है।",
            "এখন নিশ্চিতভাবে কেবল আপনার নিজের Wi-Fi কাজ করে। পোর্টটি স্বয়ংক্রিয়ভাবে খোলা হয়েছে (UPnP), তবে বাইরে থেকে তা এখনও নিশ্চিত করা হয়নি।",
            "ابھی یقینی طور پر صرف آپ کا اپنا Wi-Fi کام کرتا ہے۔ پورٹ خودکار طور پر کھلا (UPnP)، مگر باہر سے اس کی تصدیق ابھی نہیں ہوئی۔",
            "目前可以确定的只有您自己的 Wi-Fi。端口已自动打开（UPnP），但尚未从外网确认。")

        Add("Сейчас надёжно работает только в вашей сети Wi-Fi. Адрес из интернета известен, но снаружи ещё не проверен - обычно для него нужен проброс порта на роутере.",
            "Right now only your own Wi-Fi is certain. The internet address is known but has not been checked from outside - it usually needs a port forward on the router first.",
            "Зараз надійно працює лише у вашій мережі Wi-Fi. Адреса з інтернету відома, але ззовні ще не перевірена - зазвичай для неї потрібен проброс порту на роутері.",
            "Sicher ist derzeit nur Ihr eigenes WLAN. Die Internetadresse ist bekannt, wurde aber von außen noch nicht geprüft - dafür ist meist erst eine Portweiterleitung im Router nötig.",
            "Al momento è sicuro solo sulla tua rete Wi-Fi. L'indirizzo internet è noto ma non è stato verificato dall'esterno: di solito serve prima un inoltro di porta sul router.",
            "Ahora mismo solo es seguro en tu propia red Wi-Fi. La dirección de internet se conoce, pero no se ha comprobado desde fuera: normalmente hace falta antes una redirección de puerto en el router.",
            "Pour l'instant, seul votre propre Wi-Fi est certain. L'adresse internet est connue mais n'a pas été testée depuis l'extérieur - il faut généralement d'abord une redirection de port sur le routeur.",
            "Neste momento só a sua própria rede Wi-Fi é certa. O endereço de internet é conhecido mas não foi verificado a partir de fora - normalmente é preciso primeiro um encaminhamento de porta no router.",
            "المؤكَّد حاليًا هو شبكة Wi-Fi الخاصة بك فقط. عنوان الإنترنت معروف لكنه لم يُفحص من الخارج بعد - وعادةً يحتاج أولًا إلى إعادة توجيه منفذ في الموجّه.",
            "अभी निश्चित रूप से केवल आपका अपना Wi-Fi काम करता है। इंटरनेट पता ज्ञात है, पर बाहर से जाँचा नहीं गया - आमतौर पर इसके लिए पहले राउटर में पोर्ट फ़ॉरवर्ड चाहिए।",
            "এখন নিশ্চিতভাবে কেবল আপনার নিজের Wi-Fi কাজ করে। ইন্টারনেট ঠিকানা জানা আছে, কিন্তু বাইরে থেকে যাচাই করা হয়নি - সাধারণত এর জন্য আগে রাউটারে পোর্ট ফরওয়ার্ড দরকার।",
            "ابھی یقینی طور پر صرف آپ کا اپنا Wi-Fi کام کرتا ہے۔ انٹرنیٹ پتہ معلوم ہے مگر باہر سے جانچا نہیں گیا - عام طور پر اس کے لیے پہلے روٹر میں پورٹ فارورڈ درکار ہوتا ہے۔",
            "目前可以确定的只有您自己的 Wi-Fi。互联网地址已知，但尚未从外网验证 - 通常需要先在路由器上做端口转发。")

        Add("Работает в вашей сети Wi-Fi и по IPv6 - там, где провайдер телефона его поддерживает.",
            "Works on your Wi-Fi and over IPv6 - wherever the phone's carrier supports it.",
            "Працює у вашій мережі Wi-Fi і по IPv6 - там, де провайдер телефона його підтримує.",
            "Funktioniert in Ihrem WLAN und über IPv6 - überall dort, wo der Mobilfunkanbieter des Telefons es unterstützt.",
            "Funziona sulla tua rete Wi-Fi e via IPv6, dove l'operatore del telefono lo supporta.",
            "Funciona en tu red Wi-Fi y por IPv6, allí donde el operador del teléfono lo admita.",
            "Fonctionne sur votre Wi-Fi et en IPv6 - partout où l'opérateur du téléphone le prend en charge.",
            "Funciona na sua rede Wi-Fi e por IPv6 - onde a operadora do telemóvel o suportar.",
            "يعمل على شبكة Wi-Fi لديك وعبر IPv6 - حيثما دعمه مشغّل الهاتف.",
            "आपके Wi-Fi पर और IPv6 से काम करता है - जहाँ फ़ोन का ऑपरेटर उसे समर्थन देता है।",
            "আপনার Wi-Fi-তে এবং IPv6-এ কাজ করে - যেখানে ফোনের অপারেটর তা সমর্থন করে।",
            "آپ کے Wi-Fi پر اور IPv6 کے ذریعے کام کرتا ہے - جہاں فون کا آپریٹر اسے سپورٹ کرتا ہو۔",
            "可在您的 Wi-Fi 上以及通过 IPv6 访问 - 只要手机运营商支持 IPv6。")

        Add("Работает только в вашей сети Wi-Fi. Адрес из интернета не определён.",
            "Works only on your own Wi-Fi. No internet address was determined.",
            "Працює лише у вашій мережі Wi-Fi. Адресу з інтернету не визначено.",
            "Funktioniert nur in Ihrem eigenen WLAN. Es wurde keine Internetadresse ermittelt.",
            "Funziona solo sulla tua rete Wi-Fi. Nessun indirizzo internet rilevato.",
            "Solo funciona en tu propia red Wi-Fi. No se ha determinado ninguna dirección de internet.",
            "Fonctionne uniquement sur votre propre Wi-Fi. Aucune adresse internet n'a été déterminée.",
            "Só funciona na sua própria rede Wi-Fi. Não foi determinado nenhum endereço de internet.",
            "يعمل على شبكة Wi-Fi الخاصة بك فقط. لم يتم تحديد أي عنوان إنترنت.",
            "केवल आपके अपने Wi-Fi पर काम करता है। कोई इंटरनेट पता निर्धारित नहीं हुआ।",
            "কেবল আপনার নিজের Wi-Fi-তে কাজ করে। কোনো ইন্টারনেট ঠিকানা নির্ধারণ করা যায়নি।",
            "صرف آپ کے اپنے Wi-Fi پر کام کرتا ہے۔ کوئی انٹرنیٹ پتہ متعین نہیں ہوا۔",
            "仅可在您自己的 Wi-Fi 网络中访问。未能确定互联网地址。")

        ' The next-step lines name the buttons literally, so each language must use the same
        ' wording the button itself carries ("Поделиться", "Проверить доступ из интернета").
        Add("Дальше: нажмите «Поделиться» вверху - получите QR-код, который телефон прочитает и дома, и в дороге.",
            "Next: press 'Share' at the top - you get a QR code the phone can use at home and away.",
            "Далі: натисніть «Поділитися» вгорі - отримаєте QR-код, який телефон прочитає і вдома, і в дорозі.",
            "Weiter: oben auf «Teilen» klicken - Sie erhalten einen QR-Code, den das Telefon zu Hause und unterwegs nutzen kann.",
            "Poi: premi «Condividi» in alto - ottieni un codice QR che il telefono usa a casa e fuori casa.",
            "Siguiente: pulsa «Compartir» arriba y obtendrás un código QR que el teléfono puede usar en casa y fuera.",
            "Ensuite : cliquez sur « Partager » en haut - vous obtenez un code QR utilisable par le téléphone à la maison comme à l'extérieur.",
            "A seguir: carregue em «Partilhar» no topo - obtém um código QR que o telemóvel usa em casa e fora.",
            "التالي: اضغط «مشاركة» في الأعلى - ستحصل على رمز QR يستخدمه الهاتف في المنزل وخارجه.",
            "आगे: ऊपर «साझा करें» दबाएँ - आपको एक QR कोड मिलेगा, जिसे फ़ोन घर पर और बाहर दोनों जगह उपयोग कर सकता है।",
            "পরবর্তী: উপরে «শেয়ার করুন» চাপুন - একটি QR কোড পাবেন, যা ফোন বাড়িতে ও বাইরে দুই জায়গাতেই ব্যবহার করতে পারবে।",
            "اگلا: اوپر «شیئر کریں» دبائیں - آپ کو ایک QR کوڈ ملے گا جسے فون گھر پر اور باہر دونوں جگہ استعمال کر سکتا ہے۔",
            "下一步：点击顶部的「共享」 - 会生成一个二维码，手机在家中和外出时都能使用。")

        Add("Дальше: нажмите «Поделиться» вверху - получите QR-код для домашней сети. Настройка роутера здесь не поможет.",
            "Next: press 'Share' at the top - you get a QR code for your home network. Configuring the router will not help here.",
            "Далі: натисніть «Поділитися» вгорі - отримаєте QR-код для домашньої мережі. Налаштування роутера тут не допоможе.",
            "Weiter: oben auf «Teilen» klicken - Sie erhalten einen QR-Code für Ihr Heimnetz. Eine Router-Einrichtung hilft hier nicht.",
            "Poi: premi «Condividi» in alto - ottieni un codice QR per la rete di casa. Configurare il router qui non serve.",
            "Siguiente: pulsa «Compartir» arriba y obtendrás un código QR para tu red doméstica. Configurar el router no ayudará aquí.",
            "Ensuite : cliquez sur « Partager » en haut - vous obtenez un code QR pour votre réseau domestique. Configurer le routeur n'y changera rien.",
            "A seguir: carregue em «Partilhar» no topo - obtém um código QR para a sua rede doméstica. Configurar o router não ajuda aqui.",
            "التالي: اضغط «مشاركة» في الأعلى - ستحصل على رمز QR لشبكتك المنزلية. لن يفيد ضبط الموجّه هنا.",
            "आगे: ऊपर «साझा करें» दबाएँ - आपको घर के नेटवर्क के लिए QR कोड मिलेगा। राउटर सेट करने से यहाँ मदद नहीं मिलेगी।",
            "পরবর্তী: উপরে «শেয়ার করুন» চাপুন - বাড়ির নেটওয়ার্কের জন্য একটি QR কোড পাবেন। এখানে রাউটার কনফিগার করে লাভ হবে না।",
            "اگلا: اوپر «شیئر کریں» دبائیں - آپ کو گھریلو نیٹ ورک کے لیے QR کوڈ ملے گا۔ یہاں روٹر سیٹ کرنے سے فائدہ نہیں ہوگا۔",
            "下一步：点击顶部的「共享」 - 会生成一个用于家庭网络的二维码。此处配置路由器没有作用。")

        Add("Дальше - на выбор. Только дома: нажмите «Поделиться» вверху и покажите QR-код телефону. Из любой сети: нажмите «Проверить доступ из интернета» или проверьте с телефона по мобильной сети.",
            "Next, your choice. Home only: press 'Share' at the top and show the QR code to the phone. From any network: press 'Test internet access', or check from the phone on mobile data.",
            "Далі - на вибір. Лише вдома: натисніть «Поділитися» вгорі й покажіть QR-код телефону. З будь-якої мережі: натисніть «Перевірити доступ з інтернету» або перевірте з телефона через мобільну мережу.",
            "Weiter - Ihre Wahl. Nur zu Hause: oben auf «Teilen» klicken und den QR-Code dem Telefon zeigen. Aus jedem Netz: auf «Internetzugriff testen» klicken oder vom Telefon über Mobilfunk prüfen.",
            "Poi, a scelta. Solo a casa: premi «Condividi» in alto e mostra il codice QR al telefono. Da qualsiasi rete: premi «Verifica l'accesso da internet», oppure verifica dal telefono in rete mobile.",
            "Ahora, a tu elección. Solo en casa: pulsa «Compartir» arriba y muestra el código QR al teléfono. Desde cualquier red: pulsa «Comprobar el acceso desde internet» o compruébalo desde el teléfono con datos móviles.",
            "Ensuite, au choix. À la maison seulement : cliquez sur « Partager » en haut et montrez le code QR au téléphone. Depuis n'importe quel réseau : cliquez sur « Tester l'accès depuis internet », ou vérifiez depuis le téléphone en données mobiles.",
            "A seguir, à sua escolha. Só em casa: carregue em «Partilhar» no topo e mostre o código QR ao telemóvel. A partir de qualquer rede: carregue em «Testar o acesso pela internet» ou confirme no telemóvel com dados móveis.",
            "التالي، والخيار لك. في المنزل فقط: اضغط «مشاركة» في الأعلى وأظهر رمز QR للهاتف. من أي شبكة: اضغط «اختبار الوصول من الإنترنت» أو تحقّق من الهاتف عبر بيانات الجوال.",
            "आगे, आपकी पसंद। केवल घर पर: ऊपर «साझा करें» दबाएँ और QR कोड फ़ोन को दिखाएँ। किसी भी नेटवर्क से: «इंटरनेट पहुँच जाँचें» दबाएँ, या मोबाइल डेटा पर फ़ोन से जाँचें।",
            "পরবর্তী, আপনার পছন্দ। শুধু বাড়িতে: উপরে «শেয়ার করুন» চাপুন এবং QR কোডটি ফোনকে দেখান। যেকোনো নেটওয়ার্ক থেকে: «ইন্টারনেট অ্যাক্সেস পরীক্ষা করুন» চাপুন, বা মোবাইল ডেটায় ফোন থেকে যাচাই করুন।",
            "اگلا، آپ کی مرضی۔ صرف گھر پر: اوپر «شیئر کریں» دبائیں اور QR کوڈ فون کو دکھائیں۔ کسی بھی نیٹ ورک سے: «انٹرنیٹ رسائی جانچیں» دبائیں، یا موبائل ڈیٹا پر فون سے جانچیں۔",
            "下一步，任您选择。仅在家中使用：点击顶部的「共享」，把二维码给手机扫描。从任意网络使用：点击「测试互联网访问」，或用手机通过移动数据验证。")

        Add("Дальше - на выбор. Только дома: нажмите «Поделиться» вверху и покажите QR-код телефону, больше ничего не нужно. Из любой сети: следующий шаг - настроить роутер (кнопки ниже).",
            "Next, your choice. Home only: press 'Share' at the top and show the QR code to the phone - nothing else is needed. From any network: the next step is to set up the router (buttons below).",
            "Далі - на вибір. Лише вдома: натисніть «Поділитися» вгорі й покажіть QR-код телефону, більше нічого не потрібно. З будь-якої мережі: наступний крок - налаштувати роутер (кнопки нижче).",
            "Weiter - Ihre Wahl. Nur zu Hause: oben auf «Teilen» klicken und den QR-Code dem Telefon zeigen, mehr ist nicht nötig. Aus jedem Netz: der nächste Schritt ist die Router-Einrichtung (Schaltflächen unten).",
            "Poi, a scelta. Solo a casa: premi «Condividi» in alto e mostra il codice QR al telefono, non serve altro. Da qualsiasi rete: il passo successivo è configurare il router (pulsanti qui sotto).",
            "Ahora, a tu elección. Solo en casa: pulsa «Compartir» arriba y muestra el código QR al teléfono; no hace falta nada más. Desde cualquier red: el siguiente paso es configurar el router (botones de abajo).",
            "Ensuite, au choix. À la maison seulement : cliquez sur « Partager » en haut et montrez le code QR au téléphone, rien d'autre n'est nécessaire. Depuis n'importe quel réseau : l'étape suivante est la configuration du routeur (boutons ci-dessous).",
            "A seguir, à sua escolha. Só em casa: carregue em «Partilhar» no topo e mostre o código QR ao telemóvel - nada mais é preciso. A partir de qualquer rede: o passo seguinte é configurar o router (botões abaixo).",
            "التالي، والخيار لك. في المنزل فقط: اضغط «مشاركة» في الأعلى وأظهر رمز QR للهاتف، ولا يلزم شيء آخر. من أي شبكة: الخطوة التالية هي ضبط الموجّه (الأزرار بالأسفل).",
            "आगे, आपकी पसंद। केवल घर पर: ऊपर «साझा करें» दबाएँ और QR कोड फ़ोन को दिखाएँ - और कुछ नहीं चाहिए। किसी भी नेटवर्क से: अगला कदम है राउटर सेट करना (नीचे के बटन)।",
            "পরবর্তী, আপনার পছন্দ। শুধু বাড়িতে: উপরে «শেয়ার করুন» চাপুন এবং QR কোডটি ফোনকে দেখান - আর কিছুই লাগবে না। যেকোনো নেটওয়ার্ক থেকে: পরের ধাপ হলো রাউটার সেট করা (নিচের বোতামগুলি)।",
            "اگلا، آپ کی مرضی۔ صرف گھر پر: اوپر «شیئر کریں» دبائیں اور QR کوڈ فون کو دکھائیں - اور کچھ درکار نہیں۔ کسی بھی نیٹ ورک سے: اگلا قدم روٹر سیٹ کرنا ہے (نیچے کے بٹن)۔",
            "下一步，任您选择。仅在家中使用：点击顶部的「共享」，把二维码给手机扫描，无需其他设置。从任意网络使用：下一步是配置路由器（下方按钮）。")

        ' --- ShareText: the opt-in server-features dialog --------------------------

        Add("Включить общий доступ к папкам",
            "Enable folder sharing", "Увімкнути спільний доступ до папок", "Ordnerfreigabe aktivieren",
            "Attiva la condivisione delle cartelle", "Activar el uso compartido de carpetas",
            "Activer le partage de dossiers", "Ativar a partilha de pastas",
            "تفعيل مشاركة المجلدات", "फ़ोल्डर साझाकरण सक्षम करें", "ফোল্ডার শেয়ারিং চালু করুন",
            "فولڈر شیئرنگ فعال کریں", "启用文件夹共享")

        Add("Чтобы делиться папками с телефоном Android, нужен небольшой фоновый SFTP-сервер и разрешение в брандмауэре Windows. Это один раз потребует прав администратора. После включения выбранные вами папки станут доступны для чтения по сети. Продолжить?",
            "To share folders with an Android phone, a small background SFTP server and a Windows Firewall exception are needed. This asks for administrator rights once. After it is on, the folders you pick become readable over the network. Continue?",
            "Щоб ділитися папками з телефоном Android, потрібен невеликий фоновий SFTP-сервер і дозвіл у брандмауері Windows. Це один раз вимагатиме прав адміністратора. Після ввімкнення вибрані вами папки стануть доступними для читання по мережі. Продовжити?",
            "Um Ordner mit einem Android-Telefon zu teilen, werden ein kleiner SFTP-Server im Hintergrund und eine Ausnahme in der Windows-Firewall benötigt. Dafür sind einmalig Administratorrechte nötig. Danach sind die gewählten Ordner im Netzwerk lesbar. Fortfahren?",
            "Per condividere cartelle con un telefono Android servono un piccolo server SFTP in background e un'eccezione nel Windows Firewall. Richiede una volta i diritti di amministratore. Dopo l'attivazione le cartelle scelte diventano leggibili in rete. Continuare?",
            "Para compartir carpetas con un teléfono Android hacen falta un pequeño servidor SFTP en segundo plano y una excepción en el Firewall de Windows. Esto pide permisos de administrador una vez. Después, las carpetas elegidas serán legibles por la red. ¿Continuar?",
            "Pour partager des dossiers avec un téléphone Android, un petit serveur SFTP en arrière-plan et une exception dans le pare-feu Windows sont nécessaires. Cela demande une fois les droits administrateur. Ensuite, les dossiers choisis seront lisibles sur le réseau. Continuer ?",
            "Para partilhar pastas com um telemóvel Android são necessários um pequeno servidor SFTP em segundo plano e uma exceção na Firewall do Windows. Isto pede direitos de administrador uma vez. Depois, as pastas escolhidas ficam legíveis na rede. Continuar?",
            "لمشاركة المجلدات مع هاتف Android يلزم خادم SFTP صغير يعمل في الخلفية واستثناء في جدار حماية Windows. يتطلب ذلك صلاحيات المسؤول مرة واحدة. بعد التفعيل تصبح المجلدات التي تختارها قابلة للقراءة عبر الشبكة. هل تريد المتابعة؟",
            "Android फ़ोन के साथ फ़ोल्डर साझा करने के लिए एक छोटा पृष्ठभूमि SFTP सर्वर और Windows फ़ायरवॉल में एक अपवाद चाहिए। इसके लिए एक बार व्यवस्थापक अधिकार माँगे जाएँगे। चालू होने के बाद आपके चुने फ़ोल्डर नेटवर्क पर पढ़े जा सकेंगे। जारी रखें?",
            "Android ফোনের সাথে ফোল্ডার শেয়ার করতে একটি ছোট ব্যাকগ্রাউন্ড SFTP সার্ভার এবং Windows ফায়ারওয়ালে একটি ব্যতিক্রম প্রয়োজন। এতে একবার প্রশাসক অধিকার চাওয়া হবে। চালু হলে আপনার নির্বাচিত ফোল্ডারগুলি নেটওয়ার্কে পড়া যাবে। চালিয়ে যাবেন?",
            "Android فون کے ساتھ فولڈرز شیئر کرنے کے لیے ایک چھوٹا پس منظر SFTP سرور اور Windows فائر وال میں ایک استثنا درکار ہے۔ اس کے لیے ایک بار ایڈمنسٹریٹر حقوق مانگے جائیں گے۔ فعال ہونے کے بعد آپ کے منتخب فولڈرز نیٹ ورک پر پڑھے جا سکیں گے۔ جاری رکھیں؟",
            "要与 Android 手机共享文件夹，需要一个小型后台 SFTP 服务器和一条 Windows 防火墙例外规则。这需要一次管理员权限。启用后，您选择的文件夹将可通过网络读取。是否继续？")

        Add("Общий доступ к папкам для телефона Android пока не включён. Он добавляет небольшой фоновый SFTP-сервер и одно разрешение в брандмауэре Windows (нужны права администратора один раз). Пока он выключен, программа работает как просмотрщик и сортировщик медиафайлов.",
            "Folder sharing for an Android phone is not enabled yet. It adds a small background SFTP server and one Windows Firewall exception (administrator rights are needed once). While it is off, the app works as a plain image/video viewer and sorter.",
            "Спільний доступ до папок для телефона Android поки не ввімкнено. Він додає невеликий фоновий SFTP-сервер і один дозвіл у брандмауері Windows (права адміністратора потрібні один раз). Поки він вимкнений, програма працює як переглядач і сортувальник медіафайлів.",
            "Die Ordnerfreigabe für ein Android-Telefon ist noch nicht aktiviert. Sie ergänzt einen kleinen SFTP-Server im Hintergrund und eine Windows-Firewall-Ausnahme (einmalig Administratorrechte). Solange sie aus ist, arbeitet das Programm als reiner Bild- und Videobetrachter mit Sortierung.",
            "La condivisione delle cartelle con un telefono Android non è ancora attiva. Aggiunge un piccolo server SFTP in background e un'eccezione nel Windows Firewall (i diritti di amministratore servono una volta). Finché è disattivata, il programma funziona come visualizzatore e ordinatore di file multimediali.",
            "El uso compartido de carpetas para un teléfono Android aún no está activado. Añade un pequeño servidor SFTP en segundo plano y una excepción en el Firewall de Windows (los permisos de administrador se piden una vez). Mientras esté desactivado, el programa funciona como visor y clasificador de medios.",
            "Le partage de dossiers pour un téléphone Android n'est pas encore activé. Il ajoute un petit serveur SFTP en arrière-plan et une exception dans le pare-feu Windows (droits administrateur une seule fois). Tant qu'il est désactivé, le programme reste une visionneuse et un trieur de médias.",
            "A partilha de pastas para um telemóvel Android ainda não está ativada. Acrescenta um pequeno servidor SFTP em segundo plano e uma exceção na Firewall do Windows (direitos de administrador uma vez). Enquanto estiver desativada, o programa funciona como visualizador e organizador de multimédia.",
            "لم يتم بعد تفعيل مشاركة المجلدات مع هاتف Android. تضيف خادم SFTP صغيرًا في الخلفية واستثناءً واحدًا في جدار حماية Windows (تلزم صلاحيات المسؤول مرة واحدة). وطالما ظلت متوقفة، يعمل البرنامج كعارض ومنظّم للوسائط فقط.",
            "Android फ़ोन के लिए फ़ोल्डर साझाकरण अभी सक्षम नहीं है। यह एक छोटा पृष्ठभूमि SFTP सर्वर और Windows फ़ायरवॉल में एक अपवाद जोड़ता है (व्यवस्थापक अधिकार एक बार चाहिए)। बंद रहने तक कार्यक्रम केवल मीडिया दर्शक और छँटाई उपकरण के रूप में काम करता है।",
            "Android ফোনের জন্য ফোল্ডার শেয়ারিং এখনও চালু নয়। এটি একটি ছোট ব্যাকগ্রাউন্ড SFTP সার্ভার ও Windows ফায়ারওয়ালে একটি ব্যতিক্রম যোগ করে (একবার প্রশাসক অধিকার লাগে)। বন্ধ থাকা অবস্থায় প্রোগ্রামটি কেবল মিডিয়া দর্শক ও সাজানোর সরঞ্জাম হিসেবে চলে।",
            "Android فون کے لیے فولڈر شیئرنگ ابھی فعال نہیں۔ یہ ایک چھوٹا پس منظر SFTP سرور اور Windows فائر وال میں ایک استثنا شامل کرتی ہے (ایڈمنسٹریٹر حقوق ایک بار درکار)۔ بند رہنے تک پروگرام صرف میڈیا ویوَر اور ترتیب دینے والے کے طور پر کام کرتا ہے۔",
            "尚未启用面向 Android 手机的文件夹共享。它会添加一个小型后台 SFTP 服务器和一条 Windows 防火墙例外（仅需一次管理员权限）。未启用时，本程序仅作为图片/视频查看与整理工具运行。")

        Add("Установить функции сервера..",
            "Install server features..", "Встановити функції сервера..", "Serverfunktionen installieren..",
            "Installa le funzioni server..", "Instalar las funciones de servidor..",
            "Installer les fonctions serveur..", "Instalar as funções de servidor..",
            "تثبيت وظائف الخادم..", "सर्वर सुविधाएँ स्थापित करें..", "সার্ভার বৈশিষ্ট্য ইনস্টল করুন..",
            "سرور خصوصیات انسٹال کریں..", "安装服务器功能..")

        Add("Отмена",
            "Cancel", "Скасувати", "Abbrechen", "Annulla", "Cancelar", "Annuler", "Cancelar",
            "إلغاء", "रद्द करें", "বাতিল", "منسوخ", "取消")

        Add("Настройка.. подтвердите запрос прав администратора.",
            "Setting up.. confirm the administrator prompt.",
            "Налаштування.. підтвердьте запит прав адміністратора.",
            "Einrichtung läuft.. bestätigen Sie die Administratorabfrage.",
            "Configurazione in corso.. conferma la richiesta di amministratore.",
            "Configurando.. confirma la solicitud de administrador.",
            "Configuration.. confirmez la demande d'administrateur.",
            "A configurar.. confirme o pedido de administrador.",
            "جارٍ الإعداد.. أكّد طلب صلاحيات المسؤول.",
            "सेटअप हो रहा है.. व्यवस्थापक अनुरोध की पुष्टि करें।",
            "সেটআপ চলছে.. প্রশাসক অনুরোধটি নিশ্চিত করুন।",
            "سیٹ اپ جاری ہے.. ایڈمنسٹریٹر درخواست کی تصدیق کریں۔",
            "正在设置..请确认管理员提示。")

        Add("Готово. Общий доступ включён.",
            "Done. Folder sharing is enabled.", "Готово. Спільний доступ увімкнено.",
            "Fertig. Die Ordnerfreigabe ist aktiviert.", "Fatto. La condivisione delle cartelle è attiva.",
            "Listo. El uso compartido de carpetas está activado.", "Terminé. Le partage de dossiers est activé.",
            "Concluído. A partilha de pastas está ativada.", "تم. تم تفعيل مشاركة المجلدات.",
            "हो गया। फ़ोल्डर साझाकरण सक्षम है।", "সম্পন্ন। ফোল্ডার শেয়ারিং চালু হয়েছে।",
            "مکمل۔ فولڈر شیئرنگ فعال ہے۔", "完成。文件夹共享已启用。")

        Add("Общий доступ не включён - не получены права администратора.",
            "Sharing not enabled - administrator rights were not granted.",
            "Спільний доступ не ввімкнено - не отримано прав адміністратора.",
            "Freigabe nicht aktiviert - Administratorrechte wurden nicht erteilt.",
            "Condivisione non attivata: i diritti di amministratore non sono stati concessi.",
            "Uso compartido no activado: no se concedieron permisos de administrador.",
            "Partage non activé - les droits administrateur n'ont pas été accordés.",
            "Partilha não ativada - os direitos de administrador não foram concedidos.",
            "لم يتم تفعيل المشاركة - لم تُمنح صلاحيات المسؤول.",
            "साझाकरण सक्षम नहीं - व्यवस्थापक अधिकार नहीं मिले।",
            "শেয়ারিং চালু হয়নি - প্রশাসক অধিকার দেওয়া হয়নি।",
            "شیئرنگ فعال نہیں ہوئی - ایڈمنسٹریٹر حقوق نہیں ملے۔",
            "共享未启用 - 未授予管理员权限。")

        Add("Не удалось настроить брандмауэр. Попробуйте ещё раз.",
            "Could not configure the firewall. Please try again.",
            "Не вдалося налаштувати брандмауер. Спробуйте ще раз.",
            "Die Firewall konnte nicht konfiguriert werden. Bitte erneut versuchen.",
            "Impossibile configurare il firewall. Riprova.",
            "No se pudo configurar el cortafuegos. Inténtalo de nuevo.",
            "Impossible de configurer le pare-feu. Veuillez réessayer.",
            "Não foi possível configurar a firewall. Tente novamente.",
            "تعذّر ضبط جدار الحماية. حاول مرة أخرى.",
            "फ़ायरवॉल कॉन्फ़िगर नहीं हो सका। कृपया फिर से प्रयास करें।",
            "ফায়ারওয়াল কনফিগার করা যায়নি। আবার চেষ্টা করুন।",
            "فائر وال ترتیب نہ دی جا سکی۔ دوبارہ کوشش کریں۔",
            "无法配置防火墙。请重试。")

        Add("Компонент сервера не найден рядом с программой - переустановите приложение, чтобы включить общий доступ.",
            "The server component was not found next to the app - reinstall the application to enable sharing.",
            "Компонент сервера не знайдено поряд із програмою - перевстановіть застосунок, щоб увімкнути спільний доступ.",
            "Die Serverkomponente wurde neben dem Programm nicht gefunden - installieren Sie die Anwendung neu, um die Freigabe zu aktivieren.",
            "Il componente server non è stato trovato accanto al programma: reinstalla l'applicazione per attivare la condivisione.",
            "No se encontró el componente del servidor junto a la aplicación: reinstálala para activar el uso compartido.",
            "Le composant serveur est introuvable à côté du programme - réinstallez l'application pour activer le partage.",
            "O componente do servidor não foi encontrado junto à aplicação - reinstale-a para ativar a partilha.",
            "لم يُعثر على مكوّن الخادم بجوار البرنامج - أعد تثبيت التطبيق لتفعيل المشاركة.",
            "सर्वर घटक कार्यक्रम के पास नहीं मिला - साझाकरण सक्षम करने के लिए एप्लिकेशन दोबारा स्थापित करें।",
            "প্রোগ্রামের পাশে সার্ভার উপাদানটি পাওয়া যায়নি - শেয়ারিং চালু করতে অ্যাপ্লিকেশনটি পুনরায় ইনস্টল করুন।",
            "پروگرام کے ساتھ سرور جزو نہیں ملا - شیئرنگ فعال کرنے کے لیے ایپلیکیشن دوبارہ انسٹال کریں۔",
            "未在程序旁找到服务器组件 - 请重新安装应用以启用共享。")

        Add("Общий доступ к папкам для телефона Android. Нажмите, чтобы включить (нужны права администратора один раз).",
            "Folder sharing for an Android phone. Click to enable it (administrator rights are needed once).",
            "Спільний доступ до папок для телефона Android. Натисніть, щоб увімкнути (права адміністратора потрібні один раз).",
            "Ordnerfreigabe für ein Android-Telefon. Klicken Sie zum Aktivieren (einmalig Administratorrechte).",
            "Condivisione delle cartelle con un telefono Android. Fai clic per attivarla (i diritti di amministratore servono una volta).",
            "Uso compartido de carpetas para un teléfono Android. Haz clic para activarlo (los permisos de administrador se piden una vez).",
            "Partage de dossiers pour un téléphone Android. Cliquez pour l'activer (droits administrateur une seule fois).",
            "Partilha de pastas para um telemóvel Android. Clique para ativar (direitos de administrador uma vez).",
            "مشاركة المجلدات مع هاتف Android. انقر للتفعيل (تلزم صلاحيات المسؤول مرة واحدة).",
            "Android फ़ोन के लिए फ़ोल्डर साझाकरण। सक्षम करने के लिए क्लिक करें (व्यवस्थापक अधिकार एक बार चाहिए)।",
            "Android ফোনের জন্য ফোল্ডার শেয়ারিং। চালু করতে ক্লিক করুন (একবার প্রশাসক অধিকার লাগে)।",
            "Android فون کے لیے فولڈر شیئرنگ۔ فعال کرنے کے لیے کلک کریں (ایڈمنسٹریٹر حقوق ایک بار درکار)۔",
            "面向 Android 手机的文件夹共享。点击启用（仅需一次管理员权限）。")

        ' --- ShareText: strings carrying a runtime value ---------------------------
        ' Placeholders, never concatenation: word order and direction differ per
        ' language, so "port " & n & " did not answer" cannot be translated correctly.

        Add("Пароль не попадёт в файл/QR. Сообщите его получателю отдельно: {0}",
            "The password stays out of the file/QR. Pass it on separately: {0}",
            "Пароль не потрапить у файл/QR. Повідомте його одержувачу окремо: {0}",
            "Das Kennwort kommt nicht in Datei/QR. Geben Sie es separat weiter: {0}",
            "La password resta fuori dal file/QR. Comunicala separatamente: {0}",
            "La contraseña no se incluye en el archivo/QR. Compártela por separado: {0}",
            "Le mot de passe reste hors du fichier/QR. Transmettez-le séparément : {0}",
            "A palavra-passe fica fora do ficheiro/QR. Transmita-a em separado: {0}",
            "لن تُدرج كلمة المرور في الملف/رمز QR. أرسلها بشكل منفصل: {0}",
            "पासवर्ड फ़ाइल/QR में नहीं जाएगा। इसे अलग से बताएँ: {0}",
            "পাসওয়ার্ড ফাইল/QR-এ যাবে না। এটি আলাদাভাবে জানান: {0}",
            "پاس ورڈ فائل/QR میں شامل نہیں ہوگا۔ اسے الگ سے بتائیں: {0}",
            "密码不会写入文件/二维码。请另行告知：{0}")

        Add("Работает только в той же сети Wi-Fi. Интернет-порт {0} не ответил на внешнюю проверку - проверьте проброс порта на роутере или включите UPnP.",
            "Reachable only on the same Wi-Fi. Internet port {0} did not answer an external test - re-check the router forward or enable UPnP.",
            "Працює лише в тій самій мережі Wi-Fi. Інтернет-порт {0} не відповів на зовнішню перевірку - перевірте проброс порту на роутері або ввімкніть UPnP.",
            "Nur im selben WLAN erreichbar. Der Internet-Port {0} hat einen externen Test nicht beantwortet - prüfen Sie die Weiterleitung im Router oder aktivieren Sie UPnP.",
            "Raggiungibile solo sulla stessa rete Wi-Fi. La porta internet {0} non ha risposto a un test esterno: ricontrolla l'inoltro sul router o attiva UPnP.",
            "Solo accesible en la misma red Wi-Fi. El puerto de internet {0} no respondió a una prueba externa: revisa la redirección en el router o activa UPnP.",
            "Joignable uniquement sur le même Wi-Fi. Le port internet {0} n'a pas répondu à un test externe - vérifiez la redirection sur le routeur ou activez l'UPnP.",
            "Alcançável apenas na mesma rede Wi-Fi. A porta de internet {0} não respondeu a um teste externo - verifique o encaminhamento no router ou ative o UPnP.",
            "يمكن الوصول إليه على شبكة Wi-Fi نفسها فقط. لم يستجب منفذ الإنترنت {0} لاختبار خارجي - راجع إعادة التوجيه في الموجّه أو فعّل UPnP.",
            "केवल उसी Wi-Fi पर उपलब्ध। इंटरनेट पोर्ट {0} ने बाहरी जाँच का उत्तर नहीं दिया - राउटर पर फ़ॉरवर्डिंग जाँचें या UPnP चालू करें।",
            "শুধু একই Wi-Fi-তে পৌঁছানো যায়। ইন্টারনেট পোর্ট {0} বাহ্যিক পরীক্ষায় সাড়া দেয়নি - রাউটারে ফরওয়ার্ডিং যাচাই করুন বা UPnP চালু করুন।",
            "صرف اسی Wi-Fi پر قابلِ رسائی۔ انٹرنیٹ پورٹ {0} نے بیرونی جانچ کا جواب نہیں دیا - روٹر پر فارورڈنگ دیکھیں یا UPnP فعال کریں۔",
            "仅可在同一 Wi-Fi 下访问。互联网端口 {0} 未响应外部测试 - 请检查路由器转发或启用 UPnP。")

        Add("Работает в той же сети Wi-Fi. Для доступа из других сетей пробросьте TCP-порт {0} на {1} в роутере или включите UPnP.",
            "Reachable on the same Wi-Fi as this PC. For other networks, forward TCP port {0} to {1} on your router, or enable UPnP.",
            "Працює в тій самій мережі Wi-Fi. Для доступу з інших мереж пробросьте TCP-порт {0} на {1} у роутері або ввімкніть UPnP.",
            "Im selben WLAN erreichbar. Für andere Netzwerke leiten Sie den TCP-Port {0} im Router auf {1} weiter oder aktivieren Sie UPnP.",
            "Raggiungibile sulla stessa rete Wi-Fi. Per altre reti inoltra la porta TCP {0} a {1} sul router, oppure attiva UPnP.",
            "Accesible en la misma red Wi-Fi. Para otras redes, redirige el puerto TCP {0} a {1} en el router, o activa UPnP.",
            "Joignable sur le même Wi-Fi. Pour d'autres réseaux, redirigez le port TCP {0} vers {1} sur votre routeur, ou activez l'UPnP.",
            "Alcançável na mesma rede Wi-Fi. Para outras redes, encaminhe a porta TCP {0} para {1} no router, ou ative o UPnP.",
            "يمكن الوصول إليه على شبكة Wi-Fi نفسها. للشبكات الأخرى، أعد توجيه منفذ TCP رقم {0} إلى {1} في الموجّه، أو فعّل UPnP.",
            "उसी Wi-Fi पर उपलब्ध। अन्य नेटवर्क के लिए राउटर में TCP पोर्ट {0} को {1} पर फ़ॉरवर्ड करें, या UPnP चालू करें।",
            "একই Wi-Fi-তে পৌঁছানো যায়। অন্য নেটওয়ার্কের জন্য রাউটারে TCP পোর্ট {0} কে {1}-এ ফরওয়ার্ড করুন, বা UPnP চালু করুন।",
            "اسی Wi-Fi پر قابلِ رسائی۔ دیگر نیٹ ورکس کے لیے روٹر میں TCP پورٹ {0} کو {1} پر فارورڈ کریں، یا UPnP فعال کریں۔",
            "可在同一 Wi-Fi 下访问。若要从其他网络访问，请在路由器上将 TCP 端口 {0} 转发到 {1}，或启用 UPnP。")

        ' Router port-forward walkthrough. {0} router address, {1} external port,
        ' {2} this PC's LAN IP, {3} the share port. One key, five lines: splitting it
        ' per line would let a translator lose the numbering.
        Add("Внешний адрес уже добавлен в QR-код и файл .fmscfg. Чтобы он заработал, пробросьте порт на роутере:" & vbCrLf &
            "1. Откройте роутер: {0} (кнопка «Открыть роутер»)." & vbCrLf &
            "2. Войдите (логин и пароль обычно на наклейке снизу роутера)." & vbCrLf &
            "3. Найдите раздел «Проброс портов» (Port Forwarding / Virtual Server)." & vbCrLf &
            "4. Добавьте правило: внешний порт {1} -> {2}:{3}, протокол TCP." & vbCrLf &
            "5. Сохраните правило - и заново отсканируйте QR-код (или сохраните .fmscfg) на телефоне.",
            "The external address is already in the QR code and .fmscfg file. To make it work, forward the port on your router:" & vbCrLf &
            "1. Open the router: {0} (the ""Open router"" button)." & vbCrLf &
            "2. Sign in (login and password are usually on a sticker under the router)." & vbCrLf &
            "3. Find the ""Port Forwarding"" section (Virtual Server / NAT)." & vbCrLf &
            "4. Add a rule: external port {1} -> {2}:{3}, protocol TCP." & vbCrLf &
            "5. Save the rule - then rescan the QR code (or save the .fmscfg) on the phone.",
            "Зовнішню адресу вже додано до QR-коду й файлу .fmscfg. Щоб вона запрацювала, пробросьте порт на роутері:" & vbCrLf &
            "1. Відкрийте роутер: {0} (кнопка «Відкрити роутер»)." & vbCrLf &
            "2. Увійдіть (логін і пароль зазвичай на наклейці знизу роутера)." & vbCrLf &
            "3. Знайдіть розділ «Проброс портів» (Port Forwarding / Virtual Server)." & vbCrLf &
            "4. Додайте правило: зовнішній порт {1} -> {2}:{3}, протокол TCP." & vbCrLf &
            "5. Збережіть правило - і знову відскануйте QR-код (або збережіть .fmscfg) на телефоні.",
            "Die externe Adresse steht bereits im QR-Code und in der .fmscfg-Datei. Damit sie funktioniert, leiten Sie den Port im Router weiter:" & vbCrLf &
            "1. Router öffnen: {0} (Schaltfläche «Router öffnen»)." & vbCrLf &
            "2. Anmelden (Benutzername und Kennwort stehen meist auf einem Aufkleber an der Router-Unterseite)." & vbCrLf &
            "3. Den Bereich «Portweiterleitung» (Port Forwarding / Virtual Server) suchen." & vbCrLf &
            "4. Regel hinzufügen: externer Port {1} -> {2}:{3}, Protokoll TCP." & vbCrLf &
            "5. Regel speichern - und den QR-Code am Telefon erneut scannen (oder .fmscfg neu speichern).",
            "L'indirizzo esterno è già nel codice QR e nel file .fmscfg. Perché funzioni, inoltra la porta sul router:" & vbCrLf &
            "1. Apri il router: {0} (pulsante «Apri router»)." & vbCrLf &
            "2. Accedi (nome utente e password di solito sono sull'etichetta sotto il router)." & vbCrLf &
            "3. Cerca la sezione «Port Forwarding» (Virtual Server / NAT)." & vbCrLf &
            "4. Aggiungi una regola: porta esterna {1} -> {2}:{3}, protocollo TCP." & vbCrLf &
            "5. Salva la regola, poi riscansiona il codice QR (o risalva il .fmscfg) sul telefono.",
            "La dirección externa ya está en el código QR y en el archivo .fmscfg. Para que funcione, redirige el puerto en el router:" & vbCrLf &
            "1. Abre el router: {0} (botón «Abrir router»)." & vbCrLf &
            "2. Inicia sesión (el usuario y la contraseña suelen estar en una pegatina bajo el router)." & vbCrLf &
            "3. Busca la sección «Redirección de puertos» (Port Forwarding / Virtual Server)." & vbCrLf &
            "4. Añade una regla: puerto externo {1} -> {2}:{3}, protocolo TCP." & vbCrLf &
            "5. Guarda la regla y vuelve a escanear el código QR (o guarda el .fmscfg) en el teléfono.",
            "L'adresse externe est déjà dans le code QR et le fichier .fmscfg. Pour qu'elle fonctionne, redirigez le port sur votre routeur :" & vbCrLf &
            "1. Ouvrez le routeur : {0} (bouton « Ouvrir le routeur »)." & vbCrLf &
            "2. Connectez-vous (identifiant et mot de passe sont souvent sur une étiquette sous le routeur)." & vbCrLf &
            "3. Trouvez la section « Redirection de ports » (Port Forwarding / Virtual Server)." & vbCrLf &
            "4. Ajoutez une règle : port externe {1} -> {2}:{3}, protocole TCP." & vbCrLf &
            "5. Enregistrez la règle, puis rescannez le code QR (ou réenregistrez le .fmscfg) sur le téléphone.",
            "O endereço externo já está no código QR e no ficheiro .fmscfg. Para funcionar, encaminhe a porta no router:" & vbCrLf &
            "1. Abra o router: {0} (botão «Abrir router»)." & vbCrLf &
            "2. Inicie sessão (utilizador e palavra-passe estão normalmente num autocolante sob o router)." & vbCrLf &
            "3. Procure a secção «Encaminhamento de portas» (Port Forwarding / Virtual Server)." & vbCrLf &
            "4. Adicione uma regra: porta externa {1} -> {2}:{3}, protocolo TCP." & vbCrLf &
            "5. Guarde a regra e volte a ler o código QR (ou guarde o .fmscfg) no telemóvel.",
            "العنوان الخارجي موجود بالفعل في رمز QR وملف ‎.fmscfg‎. ولكي يعمل، أعد توجيه المنفذ في الموجّه:" & vbCrLf &
            "1. افتح الموجّه: {0} (زر «فتح الموجّه»)." & vbCrLf &
            "2. سجّل الدخول (اسم المستخدم وكلمة المرور عادةً على ملصق أسفل الموجّه)." & vbCrLf &
            "3. ابحث عن قسم «إعادة توجيه المنافذ» (Port Forwarding / Virtual Server)." & vbCrLf &
            "4. أضف قاعدة: المنفذ الخارجي {1} -> {2}:{3}، البروتوكول TCP." & vbCrLf &
            "5. احفظ القاعدة، ثم أعد مسح رمز QR (أو احفظ ‎.fmscfg‎) على الهاتف.",
            "बाहरी पता पहले से ही QR कोड और .fmscfg फ़ाइल में है। इसे काम करने के लिए राउटर पर पोर्ट फ़ॉरवर्ड करें:" & vbCrLf &
            "1. राउटर खोलें: {0} («राउटर खोलें» बटन)।" & vbCrLf &
            "2. साइन इन करें (उपयोगकर्ता नाम और पासवर्ड आमतौर पर राउटर के नीचे स्टिकर पर होते हैं)।" & vbCrLf &
            "3. «पोर्ट फ़ॉरवर्डिंग» अनुभाग खोजें (Port Forwarding / Virtual Server)।" & vbCrLf &
            "4. नियम जोड़ें: बाहरी पोर्ट {1} -> {2}:{3}, प्रोटोकॉल TCP।" & vbCrLf &
            "5. नियम सहेजें - फिर फ़ोन पर QR कोड दोबारा स्कैन करें (या .fmscfg फिर से सहेजें)।",
            "বাহ্যিক ঠিকানাটি ইতিমধ্যেই QR কোড ও .fmscfg ফাইলে আছে। এটি কাজ করাতে রাউটারে পোর্ট ফরওয়ার্ড করুন:" & vbCrLf &
            "1. রাউটার খুলুন: {0} («রাউটার খুলুন» বোতাম)।" & vbCrLf &
            "2. সাইন ইন করুন (ব্যবহারকারীর নাম ও পাসওয়ার্ড সাধারণত রাউটারের নিচের স্টিকারে থাকে)।" & vbCrLf &
            "3. «পোর্ট ফরওয়ার্ডিং» বিভাগ খুঁজুন (Port Forwarding / Virtual Server)।" & vbCrLf &
            "4. একটি নিয়ম যোগ করুন: বাহ্যিক পোর্ট {1} -> {2}:{3}, প্রোটোকল TCP।" & vbCrLf &
            "5. নিয়মটি সংরক্ষণ করুন - তারপর ফোনে QR কোড আবার স্ক্যান করুন (বা .fmscfg আবার সংরক্ষণ করুন)।",
            "بیرونی پتہ پہلے ہی QR کوڈ اور ‎.fmscfg‎ فائل میں موجود ہے۔ اسے کام کرنے کے لیے روٹر پر پورٹ فارورڈ کریں:" & vbCrLf &
            "1. روٹر کھولیں: {0} («روٹر کھولیں» بٹن)۔" & vbCrLf &
            "2. سائن ان کریں (صارف نام اور پاس ورڈ عام طور پر روٹر کے نیچے اسٹیکر پر ہوتے ہیں)۔" & vbCrLf &
            "3. «پورٹ فارورڈنگ» سیکشن تلاش کریں (Port Forwarding / Virtual Server)۔" & vbCrLf &
            "4. ایک قاعدہ شامل کریں: بیرونی پورٹ {1} -> {2}:{3}، پروٹوکول TCP۔" & vbCrLf &
            "5. قاعدہ محفوظ کریں - پھر فون پر QR کوڈ دوبارہ اسکین کریں (یا ‎.fmscfg‎ دوبارہ محفوظ کریں)۔",
            "外部地址已包含在二维码和 .fmscfg 文件中。要使其生效，请在路由器上转发端口：" & vbCrLf &
            "1. 打开路由器：{0}（「打开路由器」按钮）。" & vbCrLf &
            "2. 登录（用户名和密码通常在路由器底部的标签上）。" & vbCrLf &
            "3. 找到「端口转发」板块（Port Forwarding / Virtual Server）。" & vbCrLf &
            "4. 添加规则：外部端口 {1} -> {2}:{3}，协议 TCP。" & vbCrLf &
            "5. 保存规则 - 然后在手机上重新扫描二维码（或重新保存 .fmscfg）。")

        ' --- the pinned listen port -----------------------------------------------

        Add("Этот же номер вы прописываете в правиле проброса на роутере, и он записан в выданных QR-кодах. Программа его не меняет - поменяете вы, поменяйте и в роутере. Лучше держать число меньше 49152: выше начинается диапазон, из которого Windows раздаёт порты исходящим соединениям, и его может занять любая программа.",
            "This is the same number you put in the router's forwarding rule, and it is written into the QR codes you handed out. The program never changes it - if you change it, change it on the router too. Better keep it below 49152: above that is the range Windows hands out to outgoing connections, and any program can take it.",
            "Цей самий номер ви прописуєте в правилі пробросу на роутері, і він записаний у виданих QR-кодах. Програма його не змінює - зміните ви, змініть і в роутері. Краще тримати число менше 49152: вище починається діапазон, з якого Windows роздає порти вихідним з'єднанням, і його може зайняти будь-яка програма.",
            "Dieselbe Zahl tragen Sie in die Weiterleitungsregel des Routers ein, und sie steht in den bereits verteilten QR-Codes. Das Programm ändert sie nie - ändern Sie sie, ändern Sie sie auch im Router. Halten Sie sie möglichst unter 49152: darüber beginnt der Bereich, aus dem Windows Ports für ausgehende Verbindungen vergibt, und den kann jedes Programm belegen.",
            "È lo stesso numero che scrivi nella regola di inoltro del router ed è scritto nei codici QR che hai distribuito. Il programma non lo cambia mai: se lo cambi tu, cambialo anche sul router. Meglio tenerlo sotto 49152: sopra inizia l'intervallo che Windows assegna alle connessioni in uscita, e qualsiasi programma può occuparlo.",
            "Es el mismo número que pones en la regla de reenvío del router y que está escrito en los códigos QR que repartiste. El programa nunca lo cambia: si lo cambias tú, cámbialo también en el router. Mejor mantenerlo por debajo de 49152: por encima empieza el rango que Windows reparte a las conexiones salientes, y cualquier programa puede ocuparlo.",
            "C'est le même numéro que vous indiquez dans la règle de redirection du routeur, et il est inscrit dans les codes QR déjà distribués. Le programme ne le change jamais : si vous le changez, changez-le aussi sur le routeur. Mieux vaut rester sous 49152 : au-dessus commence la plage que Windows attribue aux connexions sortantes, et n'importe quel programme peut la prendre.",
            "É o mesmo número que indica na regra de encaminhamento do router e que está escrito nos códigos QR distribuídos. O programa nunca o muda - se o mudar, mude-o também no router. É melhor mantê-lo abaixo de 49152: acima disso começa o intervalo que o Windows atribui às ligações de saída, e qualquer programa o pode ocupar.",
            "هو نفسه الرقم الذي تكتبه في قاعدة التوجيه على الموجّه، وهو مكتوب في رموز QR التي وزّعتها. البرنامج لا يغيّره أبدًا - وإذا غيّرته أنت فغيّره في الموجّه أيضًا. يُفضَّل إبقاؤه أقل من 49152: فوق ذلك يبدأ النطاق الذي يوزّعه Windows على الاتصالات الصادرة، ويمكن لأي برنامج أن يشغله.",
            "यही संख्या आप राउटर के फ़ॉरवर्डिंग नियम में लिखते हैं, और यही बाँटे गए QR कोड में दर्ज है। कार्यक्रम इसे कभी नहीं बदलता - आप बदलें तो राउटर में भी बदलें। इसे 49152 से नीचे रखना बेहतर है: उससे ऊपर वह श्रेणी शुरू होती है जिसे Windows बाहर जाने वाले कनेक्शनों को देता है, और उसे कोई भी प्रोग्राम ले सकता है।",
            "এই একই সংখ্যা আপনি রাউটারের ফরওয়ার্ডিং নিয়মে লেখেন, আর সেটিই দেওয়া QR কোডে লেখা আছে। প্রোগ্রাম এটি কখনও বদলায় না - আপনি বদলালে রাউটারেও বদলান। এটি 49152-এর নিচে রাখা ভালো: তার উপরে সেই পরিসর শুরু হয় যা Windows বাইরে যাওয়া সংযোগগুলিকে দেয়, আর যেকোনো প্রোগ্রাম সেটি দখল করতে পারে।",
            "یہی نمبر آپ روٹر کے فارورڈنگ اصول میں لکھتے ہیں، اور یہی دیے گئے QR کوڈز میں درج ہے۔ پروگرام اسے کبھی نہیں بدلتا - آپ بدلیں تو روٹر میں بھی بدلیں۔ اسے 49152 سے نیچے رکھنا بہتر ہے: اس سے اوپر وہ حد شروع ہوتی ہے جو Windows باہر جانے والے کنکشنز کو دیتا ہے، اور اسے کوئی بھی پروگرام لے سکتا ہے۔",
            "这就是您填进路由器转发规则里的号码，也写在已经发出的二维码中。程序从不改动它 - 您改了它，也请在路由器上一并改。最好保持小于 49152：再往上是 Windows 分配给对外连接的范围，任何程序都可能占用。")

        Add("Порт {0} занят другой программой - общий доступ не запущен. Освободите порт или выберите другой номер (кнопка «Подобрать свободный» рядом с полем) - и поменяйте его в правиле на роутере.",
            "Port {0} is taken by another program - sharing did not start. Free that port, or choose another number (the «Find a free one» button next to the field) - and change it in the router rule too.",
            "Порт {0} зайнятий іншою програмою - спільний доступ не запущено. Звільніть порт або виберіть інший номер (кнопка «Підібрати вільний» поряд з полем) - і змініть його в правилі на роутері.",
            "Port {0} ist von einem anderen Programm belegt - die Freigabe wurde nicht gestartet. Geben Sie den Port frei oder wählen Sie eine andere Zahl (Schaltfläche «Freien suchen» neben dem Feld) - und ändern Sie sie auch in der Router-Regel.",
            "La porta {0} è occupata da un altro programma: la condivisione non è partita. Libera quella porta oppure scegli un altro numero (il pulsante «Trova una libera» accanto al campo) e cambialo anche nella regola del router.",
            "El puerto {0} está ocupado por otro programa: no se inició el acceso compartido. Libera ese puerto o elige otro número (el botón «Buscar uno libre» junto al campo) y cámbialo también en la regla del router.",
            "Le port {0} est occupé par un autre programme - le partage n'a pas démarré. Libérez ce port ou choisissez un autre numéro (le bouton «En trouver un libre» à côté du champ) - et changez-le aussi dans la règle du routeur.",
            "A porta {0} está ocupada por outro programa - a partilha não arrancou. Liberte essa porta ou escolha outro número (o botão «Encontrar uma livre» ao lado do campo) - e mude-o também na regra do router.",
            "المنفذ {0} مشغول ببرنامج آخر - لم تبدأ المشاركة. حرّر المنفذ أو اختر رقمًا آخر (زر «اختيار منفذ متاح» بجانب الحقل) - وغيّره أيضًا في قاعدة الموجّه.",
            "पोर्ट {0} किसी दूसरे प्रोग्राम के पास है - साझा पहुँच शुरू नहीं हुई। वह पोर्ट खाली करें या दूसरी संख्या चुनें (फ़ील्ड के पास «कोई खाली चुनें» बटन) - और राउटर के नियम में भी बदल दें।",
            "পোর্ট {0} অন্য একটি প্রোগ্রামের দখলে - শেয়ারিং চালু হয়নি। পোর্টটি খালি করুন বা অন্য একটি সংখ্যা বেছে নিন (ফিল্ডের পাশে «একটি খালি খুঁজুন» বোতাম) - আর রাউটারের নিয়মেও সেটি বদলে দিন।",
            "پورٹ {0} کسی دوسرے پروگرام کے پاس ہے - اشتراک شروع نہیں ہوا۔ وہ پورٹ خالی کریں یا دوسرا نمبر منتخب کریں (فیلڈ کے پاس «کوئی خالی تلاش کریں» بٹن) - اور روٹر کے اصول میں بھی بدل دیں۔",
            "端口 {0} 已被其他程序占用 - 共享未启动。请释放该端口，或选择另一个号码（字段旁的「查找空闲端口」按钮）- 并在路由器规则中一并修改。")

        Add("Порт не удаётся занять, хотя его никто не слушает. Возможно, он попал в диапазон, зарезервированный Hyper-V, WSL или Docker. Проверьте командой: netsh int ipv4 show excludedportrange tcp",
            "The port cannot be bound even though nothing is listening on it. It may fall inside a range reserved by Hyper-V, WSL or Docker. Check with: netsh int ipv4 show excludedportrange tcp",
            "Порт не вдається зайняти, хоча його ніхто не слухає. Можливо, він потрапив у діапазон, зарезервований Hyper-V, WSL або Docker. Перевірте командою: netsh int ipv4 show excludedportrange tcp",
            "Der Port lässt sich nicht belegen, obwohl niemand darauf lauscht. Möglicherweise liegt er in einem von Hyper-V, WSL oder Docker reservierten Bereich. Prüfen Sie mit: netsh int ipv4 show excludedportrange tcp",
            "La porta non può essere occupata anche se nessuno è in ascolto. Potrebbe rientrare in un intervallo riservato da Hyper-V, WSL o Docker. Verifica con: netsh int ipv4 show excludedportrange tcp",
            "El puerto no se puede ocupar aunque nadie escucha en él. Puede estar dentro de un rango reservado por Hyper-V, WSL o Docker. Compruébalo con: netsh int ipv4 show excludedportrange tcp",
            "Le port ne peut pas être pris alors que personne ne l'écoute. Il est peut-être dans une plage réservée par Hyper-V, WSL ou Docker. Vérifiez avec : netsh int ipv4 show excludedportrange tcp",
            "Não é possível ocupar a porta embora ninguém esteja à escuta. Pode estar num intervalo reservado pelo Hyper-V, WSL ou Docker. Verifique com: netsh int ipv4 show excludedportrange tcp",
            "لا يمكن حجز المنفذ رغم أنّ لا أحد يستمع عليه. ربما يقع ضمن نطاق محجوز لـ Hyper-V أو WSL أو Docker. تحقّق بالأمر: netsh int ipv4 show excludedportrange tcp",
            "पोर्ट पर कोई सुन नहीं रहा, फिर भी उसे लिया नहीं जा सकता। संभव है वह Hyper-V, WSL या Docker द्वारा सुरक्षित रखी गई श्रेणी में आता हो। इस आदेश से जाँचें: netsh int ipv4 show excludedportrange tcp",
            "কেউ শুনছে না, তবুও পোর্টটি দখল করা যাচ্ছে না। সম্ভবত এটি Hyper-V, WSL বা Docker-এর সংরক্ষিত পরিসরে পড়েছে। এই কমান্ড দিয়ে দেখুন: netsh int ipv4 show excludedportrange tcp",
            "پورٹ پر کوئی سن نہیں رہا، پھر بھی اسے حاصل نہیں کیا جا سکتا۔ ممکن ہے یہ Hyper-V، WSL یا Docker کی محفوظ کردہ حد میں آتا ہو۔ اس کمانڈ سے جانچیں: netsh int ipv4 show excludedportrange tcp",
            "虽然没有程序在监听，该端口仍无法占用。它可能落在 Hyper-V、WSL 或 Docker 预留的范围内。请用命令检查：netsh int ipv4 show excludedportrange tcp")

        Add("Сервер работает на порту {0}, а не на выбранном {1}. Обновите приложение - установленный рабочий модуль ещё не умеет выбирать порт.",
            "The server is running on port {0}, not the chosen {1}. Update the app - the installed worker cannot choose a port yet.",
            "Сервер працює на порту {0}, а не на вибраному {1}. Оновіть застосунок - встановлений робочий модуль ще не вміє вибирати порт.",
            "Der Server läuft auf Port {0}, nicht auf dem gewählten {1}. Aktualisieren Sie die App - das installierte Arbeitsmodul kann den Port noch nicht wählen.",
            "Il server è in esecuzione sulla porta {0}, non su quella scelta {1}. Aggiorna l'app: il modulo installato non sa ancora scegliere la porta.",
            "El servidor funciona en el puerto {0}, no en el elegido {1}. Actualiza la aplicación: el módulo instalado todavía no sabe elegir el puerto.",
            "Le serveur tourne sur le port {0}, pas sur celui choisi {1}. Mettez l'application à jour - le module installé ne sait pas encore choisir le port.",
            "O servidor está a funcionar na porta {0} e não na escolhida {1}. Atualize a aplicação - o módulo instalado ainda não sabe escolher a porta.",
            "الخادم يعمل على المنفذ {0} وليس على المنفذ المختار {1}. حدّث التطبيق - وحدة العمل المثبَّتة لا تستطيع بعد اختيار المنفذ.",
            "सर्वर पोर्ट {0} पर चल रहा है, चुने गए {1} पर नहीं। ऐप अपडेट करें - स्थापित वर्कर मॉड्यूल अभी पोर्ट चुनना नहीं जानता।",
            "সার্ভার চলছে পোর্ট {0}-এ, নির্বাচিত {1}-এ নয়। অ্যাপটি আপডেট করুন - ইনস্টল করা ওয়ার্কার মডিউল এখনও পোর্ট বেছে নিতে পারে না।",
            "سرور پورٹ {0} پر چل رہا ہے، منتخب کردہ {1} پر نہیں۔ ایپ اپ ڈیٹ کریں - نصب شدہ ورکر ماڈیول ابھی پورٹ منتخب کرنا نہیں جانتا۔",
            "服务器运行在端口 {0}，而不是所选的 {1}。请更新应用 - 已安装的工作模块尚不支持选择端口。")

        Add("Порт изменился - выданные раньше QR-коды и файлы настроек больше не подходят. Создайте их заново.",
            "The port changed - QR codes and config files you handed out earlier no longer match. Export them again.",
            "Порт змінився - видані раніше QR-коди та файли налаштувань більше не підходять. Створіть їх заново.",
            "Der Port hat sich geändert - bereits verteilte QR-Codes und Konfigurationsdateien passen nicht mehr. Erstellen Sie sie neu.",
            "La porta è cambiata: i codici QR e i file di configurazione già distribuiti non sono più validi. Creali di nuovo.",
            "El puerto ha cambiado: los códigos QR y los archivos de configuración ya repartidos dejan de servir. Vuelve a crearlos.",
            "Le port a changé - les codes QR et fichiers de configuration déjà distribués ne conviennent plus. Recréez-les.",
            "A porta mudou - os códigos QR e ficheiros de configuração já distribuídos deixaram de servir. Crie-os de novo.",
            "تغيّر المنفذ - رموز QR وملفات الإعداد التي وزّعتها من قبل لم تعد صالحة. أنشئها من جديد.",
            "पोर्ट बदल गया है - पहले बाँटे गए QR कोड और सेटिंग फ़ाइलें अब मेल नहीं खातीं। उन्हें दोबारा बनाएँ।",
            "পোর্ট বদলে গেছে - আগে দেওয়া QR কোড ও কনফিগ ফাইল আর মেলে না। সেগুলি আবার তৈরি করুন।",
            "پورٹ بدل گیا ہے - پہلے دیے گئے QR کوڈ اور سیٹنگ فائلیں اب مطابقت نہیں رکھتیں۔ انہیں دوبارہ بنائیں۔",
            "端口已更改 - 之前发出的二维码和配置文件不再匹配。请重新生成。")

    End Sub

End Class
