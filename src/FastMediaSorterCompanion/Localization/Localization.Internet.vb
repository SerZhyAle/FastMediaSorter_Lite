Option Strict On

' <summary>
' The "Internet access" window: external-address detection, the reachability probe
' and the router port-forward helpers.
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddInternetStrings()

        Add("Инструкция по пробросу порта",
            "Port-forward guide", "Інструкція з пробросу порту", "Anleitung zur Portweiterleitung",
            "Guida all'inoltro delle porte", "Guía de redirección de puertos",
            "Guide de redirection de port", "Guia de encaminhamento de portas",
            "دليل إعادة توجيه المنافذ", "पोर्ट फ़ॉरवर्डिंग मार्गदर्शिका", "পোর্ট ফরওয়ার্ডিং নির্দেশিকা",
            "پورٹ فارورڈنگ رہنما", "端口转发指南")

        Add("Тест",
            "Test", "Тест", "Test", "Prova", "Prueba", "Test", "Teste",
            "اختبار", "जाँच", "পরীক্ষা", "ٹیسٹ", "测试")

        Add("Обновить",
            "Refresh", "Оновити", "Aktualisieren", "Aggiorna", "Actualizar", "Actualiser",
            "Atualizar", "تحديث", "ताज़ा करें", "রিফ্রেশ", "تازہ کریں", "刷新")

        Add("Проверить внешний адрес с этого ПК (не окончательно - роутер может не пускать на свой адрес изнутри).",
            "Probe the external address from this PC (inconclusive - a router may refuse its own address from inside).",
            "Перевірити зовнішню адресу з цього ПК (не остаточно - роутер може не пускати на свою адресу зсередини).",
            "Die externe Adresse von diesem PC aus prüfen (nicht endgültig - ein Router lässt seine eigene Adresse von innen oft nicht zu).",
            "Verifica l'indirizzo esterno da questo PC (non conclusivo: un router può rifiutare il proprio indirizzo dall'interno).",
            "Comprobar la dirección externa desde este PC (no concluyente: un router puede rechazar su propia dirección desde dentro).",
            "Tester l'adresse externe depuis ce PC (non concluant - un routeur peut refuser sa propre adresse depuis l'intérieur).",
            "Testar o endereço externo a partir deste PC (não conclusivo - um router pode recusar o próprio endereço a partir de dentro).",
            "اختبر العنوان الخارجي من هذا الحاسوب (غير حاسم - قد يرفض الموجّه عنوانه الخاص من الداخل).",
            "इस पीसी से बाहरी पते की जाँच करें (निर्णायक नहीं - राउटर भीतर से अपने ही पते को अस्वीकार कर सकता है)।",
            "এই পিসি থেকে বাহ্যিক ঠিকানা পরীক্ষা করুন (চূড়ান্ত নয় - রাউটার ভেতর থেকে নিজের ঠিকানা প্রত্যাখ্যান করতে পারে)।",
            "اس پی سی سے بیرونی پتے کی جانچ کریں (حتمی نہیں - روٹر اندر سے اپنے ہی پتے کو مسترد کر سکتا ہے)۔",
            "从这台电脑测试外部地址（结果不确定 - 路由器可能拒绝从内部访问自身地址）。")

        Add("Открыть подробную инструкцию (HTML, с вашими значениями и моделью роутера)..",
            "Open the detailed guide (HTML, prefilled with your values + router model)..",
            "Відкрити детальну інструкцію (HTML, з вашими значеннями та моделлю роутера)..",
            "Ausführliche Anleitung öffnen (HTML, mit Ihren Werten und Ihrem Routermodell)..",
            "Apri la guida dettagliata (HTML, precompilata con i tuoi valori e il modello di router)..",
            "Abrir la guía detallada (HTML, con tus valores y el modelo de router)..",
            "Ouvrir le guide détaillé (HTML, prérempli avec vos valeurs et votre modèle de routeur)..",
            "Abrir o guia detalhado (HTML, preenchido com os seus valores e o modelo do router)..",
            "افتح الدليل المفصّل (HTML، معبّأ بقيمك وطراز الموجّه)..",
            "विस्तृत मार्गदर्शिका खोलें (HTML, आपके मानों और राउटर मॉडल के साथ)..",
            "বিস্তারিত নির্দেশিকা খুলুন (HTML, আপনার মান ও রাউটার মডেল সহ)..",
            "تفصیلی رہنما کھولیں (HTML، آپ کی اقدار اور روٹر ماڈل کے ساتھ)..",
            "打开详细指南（HTML，已填入您的数值和路由器型号）..")

        Add("Закрыть",
            "Close", "Закрити", "Schließen", "Chiudi", "Cerrar", "Fermer", "Fechar",
            "إغلاق", "बंद करें", "বন্ধ করুন", "بند کریں", "关闭")

        Add("Обновление..",
            "Refreshing..", "Оновлення..", "Wird aktualisiert..", "Aggiornamento..",
            "Actualizando..", "Actualisation..", "A atualizar..",
            "جارٍ التحديث..", "ताज़ा किया जा रहा है..", "রিফ্রেশ হচ্ছে..", "تازہ کیا جا رہا ہے..", "正在刷新..")

        Add("Запустите общий доступ, чтобы настроить интернет.",
            "Start sharing to set up internet access.",
            "Запустіть спільний доступ, щоб налаштувати інтернет.",
            "Starten Sie die Freigabe, um den Internetzugriff einzurichten.",
            "Avvia la condivisione per configurare l'accesso da internet.",
            "Inicia el uso compartido para configurar el acceso desde internet.",
            "Démarrez le partage pour configurer l'accès internet.",
            "Inicie a partilha para configurar o acesso pela internet.",
            "ابدأ المشاركة لإعداد الوصول من الإنترنت.",
            "इंटरनेट पहुँच सेट करने के लिए साझाकरण शुरू करें।",
            "ইন্টারনেট অ্যাক্সেস সেট করতে শেয়ারিং শুরু করুন।",
            "انٹرنیٹ رسائی ترتیب دینے کے لیے شیئرنگ شروع کریں۔",
            "请先开始共享，以便设置互联网访问。")

        Add("Определяем внешний адрес..",
            "Detecting the external address..", "Визначаємо зовнішню адресу..",
            "Externe Adresse wird ermittelt..", "Rilevamento dell'indirizzo esterno..",
            "Detectando la dirección externa..", "Détection de l'adresse externe..",
            "A detetar o endereço externo..", "جارٍ تحديد العنوان الخارجي..",
            "बाहरी पता पहचाना जा रहा है..", "বাহ্যিক ঠিকানা শনাক্ত করা হচ্ছে..",
            "بیرونی پتہ معلوم کیا جا رہا ہے..", "正在检测外部地址..")

        Add("адрес роутера",
            "the router address", "адреса роутера", "die Router-Adresse", "l'indirizzo del router",
            "la dirección del router", "l'adresse du routeur", "o endereço do router",
            "عنوان الموجّه", "राउटर का पता", "রাউটারের ঠিকানা", "روٹر کا پتہ", "路由器地址")

        Add("IP этого ПК",
            "this PC's IP", "IP цього ПК", "die IP dieses PCs", "l'IP di questo PC",
            "la IP de este PC", "l'IP de ce PC", "o IP deste PC",
            "عنوان IP لهذا الحاسوب", "इस पीसी का IP", "এই পিসির IP", "اس پی سی کا IP", "本机 IP")

        Add("За CGNAT - извне недоступно.",
            "Behind CGNAT - not reachable from outside.", "За CGNAT - ззовні недоступно.",
            "Hinter CGNAT - von außen nicht erreichbar.", "Dietro CGNAT: non raggiungibile dall'esterno.",
            "Detrás de CGNAT: no accesible desde fuera.", "Derrière un CGNAT - injoignable depuis l'extérieur.",
            "Atrás de CGNAT - não alcançável do exterior.", "خلف CGNAT - غير قابل للوصول من الخارج.",
            "CGNAT के पीछे - बाहर से पहुँच नहीं।", "CGNAT-এর পিছনে - বাইরে থেকে পৌঁছানো যায় না।",
            "CGNAT کے پیچھے - باہر سے قابلِ رسائی نہیں۔", "位于 CGNAT 之后 - 外网无法访问。")

        Add("Порт открыт автоматически (UPnP) - настраивать роутер не нужно. Адрес уже в QR-коде и файле .fmscfg. Учтите: это не подтверждает работу извне - точный тест только с телефона по мобильной сети. При долгой работе общего доступа проверка может устареть; если телефон не подключается, отключите и снова включите общий доступ.",
            "The port was opened automatically (UPnP) - no router setup needed. The address is already in the QR code and .fmscfg file. Note: this does not confirm it actually works from outside - the definitive test is from the phone on mobile data. Long-running sessions can go stale; if the phone can't connect, turn sharing off and back on.",
            "Порт відкрито автоматично (UPnP) - налаштовувати роутер не потрібно. Адреса вже в QR-коді й файлі .fmscfg. Врахуйте: це не підтверджує роботу ззовні - точний тест лише з телефона через мобільну мережу. При тривалій роботі спільного доступу перевірка може застаріти; якщо телефон не підключається, вимкніть і знову ввімкніть спільний доступ.",
            "Der Port wurde automatisch geöffnet (UPnP) - keine Routereinrichtung nötig. Die Adresse steht bereits im QR-Code und in der .fmscfg-Datei. Beachten Sie: das bestätigt nicht, dass es von außen funktioniert - der eindeutige Test ist das Telefon über Mobilfunk. Bei langer Laufzeit kann die Prüfung veralten; verbindet sich das Telefon nicht, schalten Sie die Freigabe aus und wieder ein.",
            "La porta è stata aperta automaticamente (UPnP): non serve configurare il router. L'indirizzo è già nel codice QR e nel file .fmscfg. Nota: questo non conferma il funzionamento dall'esterno - la prova definitiva è dal telefono in rete mobile. Con sessioni lunghe il controllo può scadere; se il telefono non si connette, disattiva e riattiva la condivisione.",
            "El puerto se abrió automáticamente (UPnP): no hace falta configurar el router. La dirección ya está en el código QR y en el archivo .fmscfg. Ten en cuenta que esto no confirma que funcione desde fuera: la prueba definitiva es desde el teléfono con datos móviles. En sesiones largas la comprobación puede quedar obsoleta; si el teléfono no conecta, desactiva y vuelve a activar el uso compartido.",
            "Le port a été ouvert automatiquement (UPnP) - aucune configuration du routeur nécessaire. L'adresse est déjà dans le code QR et le fichier .fmscfg. À noter : cela ne confirme pas le fonctionnement depuis l'extérieur - le test décisif se fait depuis le téléphone en données mobiles. Sur une session longue, la vérification peut devenir obsolète ; si le téléphone ne se connecte pas, désactivez puis réactivez le partage.",
            "A porta foi aberta automaticamente (UPnP) - não é preciso configurar o router. O endereço já está no código QR e no ficheiro .fmscfg. Note: isto não confirma que funciona do exterior - o teste definitivo é a partir do telemóvel com dados móveis. Em sessões longas a verificação pode ficar desatualizada; se o telemóvel não ligar, desative e reative a partilha.",
            "تم فتح المنفذ تلقائيًا (UPnP) - لا حاجة لإعداد الموجّه. العنوان موجود بالفعل في رمز QR وملف ‎.fmscfg‎. لاحظ أن ذلك لا يؤكد عمله من الخارج - الاختبار الحاسم يكون من الهاتف عبر بيانات الجوال. في الجلسات الطويلة قد يصبح الفحص قديمًا؛ فإذا لم يتصل الهاتف، أوقف المشاركة ثم شغّلها من جديد.",
            "पोर्ट स्वतः खुल गया (UPnP) - राउटर सेट करने की ज़रूरत नहीं। पता पहले से QR कोड और .fmscfg फ़ाइल में है। ध्यान दें: यह बाहर से काम करने की पुष्टि नहीं करता - निर्णायक जाँच मोबाइल डेटा पर फ़ोन से ही होती है। लंबे सत्र में जाँच पुरानी पड़ सकती है; फ़ोन न जुड़े तो साझाकरण बंद करके फिर चालू करें।",
            "পোর্টটি স্বয়ংক্রিয়ভাবে খোলা হয়েছে (UPnP) - রাউটার সেট করার দরকার নেই। ঠিকানাটি ইতিমধ্যেই QR কোড ও .fmscfg ফাইলে আছে। মনে রাখুন: এটি বাইরে থেকে কাজ করার নিশ্চয়তা দেয় না - চূড়ান্ত পরীক্ষা মোবাইল ডেটায় ফোন থেকেই। দীর্ঘ সেশনে যাচাই পুরনো হয়ে যেতে পারে; ফোন সংযুক্ত না হলে শেয়ারিং বন্ধ করে আবার চালু করুন।",
            "پورٹ خودکار طور پر کھل گیا (UPnP) - روٹر ترتیب دینے کی ضرورت نہیں۔ پتہ پہلے ہی QR کوڈ اور ‎.fmscfg‎ فائل میں ہے۔ نوٹ کریں: یہ باہر سے کام کرنے کی تصدیق نہیں کرتا - حتمی جانچ موبائل ڈیٹا پر فون سے ہی ہوتی ہے۔ طویل سیشن میں جانچ پرانی ہو سکتی ہے؛ فون نہ جڑے تو شیئرنگ بند کر کے دوبارہ شروع کریں۔",
            "端口已自动打开（UPnP）- 无需设置路由器。地址已包含在二维码和 .fmscfg 文件中。请注意：这并不能确认外网可用 - 确定性的测试是用手机通过移动数据。长时间运行后检测结果可能过期；若手机连不上，请关闭再重新开启共享。")

        Add("Внешний адрес неизвестен - узнайте в роутере.",
            "External address unknown - check the router.", "Зовнішня адреса невідома - дізнайтеся в роутері.",
            "Externe Adresse unbekannt - im Router nachsehen.", "Indirizzo esterno sconosciuto: controlla il router.",
            "Dirección externa desconocida: consúltala en el router.", "Adresse externe inconnue - vérifiez sur le routeur.",
            "Endereço externo desconhecido - consulte o router.", "العنوان الخارجي غير معروف - راجع الموجّه.",
            "बाहरी पता अज्ञात - राउटर में देखें।", "বাহ্যিক ঠিকানা অজানা - রাউটারে দেখুন।",
            "بیرونی پتہ نامعلوم - روٹر میں دیکھیں۔", "外部地址未知 - 请在路由器中查看。")

        Add("Адрес ещё не определён.",
            "No address yet.", "Адресу ще не визначено.", "Noch keine Adresse.",
            "Nessun indirizzo ancora.", "Aún no hay dirección.", "Pas encore d'adresse.",
            "Ainda sem endereço.", "لا يوجد عنوان بعد.", "अभी कोई पता नहीं।",
            "এখনও কোনো ঠিকানা নেই।", "ابھی کوئی پتہ نہیں۔", "尚无地址。")

        Add("Не удалось выполнить проверку.",
            "Could not run the test.", "Не вдалося виконати перевірку.",
            "Der Test konnte nicht ausgeführt werden.", "Impossibile eseguire la prova.",
            "No se pudo realizar la prueba.", "Impossible d'exécuter le test.",
            "Não foi possível executar o teste.", "تعذّر تنفيذ الاختبار.",
            "जाँच नहीं चलाई जा सकी।", "পরীক্ষা চালানো যায়নি।", "جانچ نہ چلائی جا سکی۔", "无法执行测试。")

        Add("Адрес некорректен.",
            "Invalid address.", "Адреса некоректна.", "Ungültige Adresse.", "Indirizzo non valido.",
            "Dirección no válida.", "Adresse invalide.", "Endereço inválido.",
            "عنوان غير صالح.", "पता अमान्य है।", "ঠিকানা অবৈধ।", "پتہ غلط ہے۔", "地址无效。")

        Add("Определяем роутер..",
            "Detecting router..", "Визначаємо роутер..", "Router wird erkannt..",
            "Rilevamento del router..", "Detectando el router..", "Détection du routeur..",
            "A detetar o router..", "جارٍ التعرّف على الموجّه..", "राउटर पहचाना जा रहा है..",
            "রাউটার শনাক্ত করা হচ্ছে..", "روٹر پہچانا جا رہا ہے..", "正在检测路由器..")

        Add("Не удалось открыть инструкцию.",
            "Could not open the guide.", "Не вдалося відкрити інструкцію.",
            "Die Anleitung konnte nicht geöffnet werden.", "Impossibile aprire la guida.",
            "No se pudo abrir la guía.", "Impossible d'ouvrir le guide.",
            "Não foi possível abrir o guia.", "تعذّر فتح الدليل.",
            "मार्गदर्शिका नहीं खुल सकी।", "নির্দেশিকা খোলা যায়নি।", "رہنما نہ کھل سکا۔", "无法打开指南。")

        ' --- strings carrying a runtime value --------------------------------------

        Add("Доступно из интернета: {0}",
            "Reachable from internet: {0}", "Доступно з інтернету: {0}", "Aus dem Internet erreichbar: {0}",
            "Raggiungibile da internet: {0}", "Accesible desde internet: {0}",
            "Joignable depuis internet : {0}", "Alcançável pela internet: {0}",
            "يمكن الوصول إليه من الإنترنت: {0}", "इंटरनेट से उपलब्ध: {0}", "ইন্টারনেট থেকে পৌঁছানো যায়: {0}",
            "انٹرنیٹ سے قابلِ رسائی: {0}", "可从互联网访问：{0}")

        Add("Внешний адрес: {0} (нужен проброс порта)",
            "External address: {0} (needs port forwarding)", "Зовнішня адреса: {0} (потрібен проброс порту)",
            "Externe Adresse: {0} (Portweiterleitung nötig)", "Indirizzo esterno: {0} (serve l'inoltro della porta)",
            "Dirección externa: {0} (requiere redirección de puerto)",
            "Adresse externe : {0} (redirection de port nécessaire)",
            "Endereço externo: {0} (precisa de encaminhamento de porta)",
            "العنوان الخارجي: {0} (يحتاج إعادة توجيه المنفذ)",
            "बाहरी पता: {0} (पोर्ट फ़ॉरवर्डिंग चाहिए)", "বাহ্যিক ঠিকানা: {0} (পোর্ট ফরওয়ার্ডিং দরকার)",
            "بیرونی پتہ: {0} (پورٹ فارورڈنگ درکار)", "外部地址：{0}（需要端口转发）")

        Add("Проверка {0} ..",
            "Testing {0} ..", "Перевірка {0} ..", "Test {0} ..", "Prova di {0} ..",
            "Probando {0} ..", "Test de {0} ..", "A testar {0} ..",
            "اختبار {0} ..", "जाँच {0} ..", "পরীক্ষা {0} ..", "جانچ {0} ..", "正在测试 {0} ..")

        Add("✓ SFTP-сервер доступен: {0}",
            "✓ SFTP server reachable: {0}", "✓ SFTP-сервер доступний: {0}",
            "✓ SFTP-Server erreichbar: {0}", "✓ Server SFTP raggiungibile: {0}",
            "✓ Servidor SFTP accesible: {0}", "✓ Serveur SFTP joignable : {0}",
            "✓ Servidor SFTP alcançável: {0}", "✓ خادم SFTP متاح: {0}",
            "✓ SFTP सर्वर उपलब्ध: {0}", "✓ SFTP সার্ভার উপলব্ধ: {0}",
            "✓ SFTP سرور قابلِ رسائی: {0}", "✓ SFTP 服务器可访问：{0}")

        Add("Порт открыт, но SFTP не ответил: {0}",
            "Port open, but no SFTP reply: {0}", "Порт відкрито, але SFTP не відповів: {0}",
            "Port offen, aber keine SFTP-Antwort: {0}", "Porta aperta, ma nessuna risposta SFTP: {0}",
            "Puerto abierto, pero sin respuesta SFTP: {0}", "Port ouvert, mais aucune réponse SFTP : {0}",
            "Porta aberta, mas sem resposta SFTP: {0}", "المنفذ مفتوح لكن لا استجابة من SFTP: {0}",
            "पोर्ट खुला है, पर SFTP ने उत्तर नहीं दिया: {0}", "পোর্ট খোলা, কিন্তু SFTP সাড়া দেয়নি: {0}",
            "پورٹ کھلا ہے مگر SFTP نے جواب نہیں دیا: {0}", "端口已开放，但 SFTP 无响应：{0}")

        Add("✗ С этого ПК не отвечает ({0}). Роутер может не пускать на свой внешний адрес изнутри - проверьте с телефона по мобильной сети.",
            "✗ No answer from this PC ({0}). Your router may block its own address from inside - test from the phone on mobile data.",
            "✗ З цього ПК не відповідає ({0}). Роутер може не пускати на свою зовнішню адресу зсередини - перевірте з телефона через мобільну мережу.",
            "✗ Von diesem PC keine Antwort ({0}). Ihr Router lässt seine eigene Adresse von innen möglicherweise nicht zu - testen Sie es vom Telefon über Mobilfunk.",
            "✗ Nessuna risposta da questo PC ({0}). Il router potrebbe bloccare il proprio indirizzo dall'interno: prova dal telefono in rete mobile.",
            "✗ Sin respuesta desde este PC ({0}). El router puede bloquear su propia dirección desde dentro: prueba desde el teléfono con datos móviles.",
            "✗ Aucune réponse depuis ce PC ({0}). Votre routeur peut bloquer sa propre adresse depuis l'intérieur - testez depuis le téléphone en données mobiles.",
            "✗ Sem resposta a partir deste PC ({0}). O router pode bloquear o próprio endereço a partir de dentro - teste com o telemóvel em dados móveis.",
            "✗ لا استجابة من هذا الحاسوب ({0}). قد يمنع الموجّه عنوانه الخاص من الداخل - اختبر من الهاتف عبر بيانات الجوال.",
            "✗ इस पीसी से कोई उत्तर नहीं ({0})। राउटर भीतर से अपने ही पते को रोक सकता है - मोबाइल डेटा पर फ़ोन से जाँचें।",
            "✗ এই পিসি থেকে কোনো সাড়া নেই ({0})। রাউটার ভেতর থেকে নিজের ঠিকানা আটকে দিতে পারে - মোবাইল ডেটায় ফোন থেকে পরীক্ষা করুন।",
            "✗ اس پی سی سے کوئی جواب نہیں ({0})۔ روٹر اندر سے اپنے ہی پتے کو روک سکتا ہے - موبائل ڈیٹا پر فون سے جانچیں۔",
            "✗ 本机无响应（{0}）。路由器可能拒绝从内部访问其外部地址 - 请用手机通过移动数据测试。")

        Add("Роутер: {0}",
            "Router: {0}", "Роутер: {0}", "Router: {0}", "Router: {0}", "Router: {0}",
            "Routeur : {0}", "Router: {0}", "الموجّه: {0}", "राउटर: {0}", "রাউটার: {0}",
            "روٹر: {0}", "路由器：{0}")

    End Sub

End Class
