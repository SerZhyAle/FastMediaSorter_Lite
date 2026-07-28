Option Strict On

' <summary>
' The "OCR и перевод" tab of the Settings window - captions and the hint under each
' control. Built in code (Table_Form.Ocr.vb), and missed by the first migration pass:
' one "Dim rus As Boolean" at the top of LocalizeOcrTab covered all forty-odd strings.
'
' The tab heading "Перевод" is NOT here: it collides with the toolbar button of the same
' Russian word and lives in Localization.SettingsHints.vb under the "settings" context.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddOcrTabStrings()

        ' --- captions ---------------------------------------------------------------

        Add("Распознавание (OCR)",
            "Recognition (OCR)", "Розпізнавання (OCR)", "Erkennung (OCR)", "Riconoscimento (OCR)",
            "Reconocimiento (OCR)", "Reconnaissance (OCR)", "Reconhecimento (OCR)",
            "التعرّف الضوئي (OCR)", "पहचान (OCR)", "শনাক্তকরণ (OCR)", "شناخت (OCR)", "识别 (OCR)")

        Add("Включить OCR и перевод",
            "Enable OCR & translation", "Увімкнути OCR і переклад", "OCR und Übersetzung aktivieren",
            "Attiva OCR e traduzione", "Activar OCR y traducción", "Activer l'OCR et la traduction",
            "Ativar OCR e tradução", "تفعيل التعرّف الضوئي والترجمة",
            "OCR और अनुवाद सक्षम करें", "OCR ও অনুবাদ চালু করুন",
            "OCR اور ترجمہ فعال کریں", "启用 OCR 与翻译")

        Add("Авто-режим (после показа изображения)",
            "Auto mode (after each image settles)", "Авто-режим (після показу зображення)",
            "Automatikmodus (nach jedem Bild)", "Modalità automatica (dopo ogni immagine)",
            "Modo automático (tras mostrar cada imagen)", "Mode automatique (après chaque image)",
            "Modo automático (depois de cada imagem)", "الوضع التلقائي (بعد عرض كل صورة)",
            "स्वतः मोड (प्रत्येक छवि दिखने के बाद)", "স্বয়ংক্রিয় মোড (প্রতিটি ছবির পরে)",
            "خودکار موڈ (ہر تصویر کے بعد)", "自动模式（每张图片显示后）")

        Add("Переводчик:",
            "Translator:", "Перекладач:", "Übersetzer:", "Traduttore:", "Traductor:",
            "Traducteur :", "Tradutor:", "المترجم:", "अनुवादक:", "অনুবাদক:", "مترجم:", "翻译服务：")

        Add("Адрес (endpoint):",
            "Endpoint URL:", "Адреса (endpoint):", "Endpunkt-URL:", "URL endpoint:",
            "URL del endpoint:", "URL du point de terminaison :", "URL do endpoint:",
            "عنوان الخدمة (endpoint):", "एंडपॉइंट URL:", "এন্ডপয়েন্ট URL:",
            "اینڈ پوائنٹ URL:", "接口地址（endpoint）：")

        Add("Сервер Ollama:",
            "Ollama server:", "Сервер Ollama:", "Ollama-Server:", "Server Ollama:",
            "Servidor Ollama:", "Serveur Ollama :", "Servidor Ollama:",
            "خادم Ollama:", "Ollama सर्वर:", "Ollama সার্ভার:", "Ollama سرور:", "Ollama 服务器：")

        Add("Установить Ollama",
            "Install Ollama", "Встановити Ollama", "Ollama installieren", "Installa Ollama",
            "Instalar Ollama", "Installer Ollama", "Instalar o Ollama",
            "تثبيت Ollama", "Ollama स्थापित करें", "Ollama ইনস্টল করুন",
            "Ollama انسٹال کریں", "安装 Ollama")

        Add("Запустить Ollama",
            "Start Ollama", "Запустити Ollama", "Ollama starten", "Avvia Ollama",
            "Iniciar Ollama", "Démarrer Ollama", "Iniciar o Ollama",
            "تشغيل Ollama", "Ollama शुरू करें", "Ollama চালু করুন",
            "Ollama شروع کریں", "启动 Ollama")

        Add("Модель Ollama:",
            "Ollama model:", "Модель Ollama:", "Ollama-Modell:", "Modello Ollama:",
            "Modelo de Ollama:", "Modèle Ollama :", "Modelo do Ollama:",
            "نموذج Ollama:", "Ollama मॉडल:", "Ollama মডেল:", "Ollama ماڈل:", "Ollama 模型：")

        Add("Загрузить",
            "Pull", "Завантажити", "Herunterladen", "Scarica", "Descargar", "Télécharger",
            "Transferir", "تنزيل", "डाउनलोड करें", "ডাউনলোড করুন", "ڈاؤن لوڈ کریں", "拉取")

        Add("API-ключ:",
            "API key:", "API-ключ:", "API-Schlüssel:", "Chiave API:", "Clave de API:",
            "Clé API :", "Chave de API:", "مفتاح API:", "API कुंजी:", "API কী:",
            "API کلید:", "API 密钥：")

        Add("Язык перевода:",
            "Translate to (target):", "Мова перекладу:", "Zielsprache:", "Lingua di destinazione:",
            "Idioma de destino:", "Langue cible :", "Idioma de destino:",
            "لغة الترجمة:", "अनुवाद की भाषा:", "অনুবাদের ভাষা:", "ترجمے کی زبان:", "翻译目标语言：")

        Add("Язык распознавания:",
            "Recognition (source):", "Мова розпізнавання:", "Erkennungssprache:",
            "Lingua di riconoscimento:", "Idioma de reconocimiento:", "Langue de reconnaissance :",
            "Idioma de reconhecimento:", "لغة التعرّف:", "पहचान की भाषा:",
            "শনাক্তকরণের ভাষা:", "شناخت کی زبان:", "识别源语言：")

        Add("Модель OCR:",
            "OCR model:", "Модель OCR:", "OCR-Modell:", "Modello OCR:", "Modelo de OCR:",
            "Modèle OCR :", "Modelo de OCR:", "نموذج OCR:", "OCR मॉडल:", "OCR মডেল:",
            "OCR ماڈل:", "OCR 模型：")

        Add("Режим OCR:",
            "OCR mode:", "Режим OCR:", "OCR-Modus:", "Modalità OCR:", "Modo de OCR:",
            "Mode OCR :", "Modo de OCR:", "وضع OCR:", "OCR मोड:", "OCR মোড:",
            "OCR موڈ:", "OCR 模式：")

        Add("Скачать пакет распознавания",
            "Download recognition language pack", "Завантажити пакет розпізнавання",
            "Erkennungssprachpaket herunterladen", "Scarica il pacchetto di riconoscimento",
            "Descargar el paquete de reconocimiento", "Télécharger le pack de reconnaissance",
            "Transferir o pacote de reconhecimento", "تنزيل حزمة لغة التعرّف",
            "पहचान भाषा पैक डाउनलोड करें", "শনাক্তকরণ ভাষা প্যাক ডাউনলোড করুন",
            "شناخت کا زبان پیک ڈاؤن لوڈ کریں", "下载识别语言包")

        Add("Непрозрачность:",
            "Opacity:", "Непрозорість:", "Deckkraft:", "Opacità:", "Opacidad:", "Opacité :",
            "Opacidade:", "العتامة:", "अपारदर्शिता:", "অস্বচ্ছতা:", "دھندلاپن:", "不透明度：")

        Add("Показывать панель перевода",
            "Show translation overlay", "Показувати панель перекладу",
            "Übersetzungs-Overlay anzeigen", "Mostra la sovrapposizione della traduzione",
            "Mostrar la superposición de traducción", "Afficher la superposition de traduction",
            "Mostrar a sobreposição de tradução", "إظهار تراكب الترجمة",
            "अनुवाद ओवरले दिखाएँ", "অনুবাদ ওভারলে দেখান", "ترجمے کا اوورلے دکھائیں", "显示翻译叠加层")

        Add("Дисковый кэш результатов",
            "Cache results on disk", "Дисковий кеш результатів", "Ergebnisse auf der Festplatte zwischenspeichern",
            "Memorizza i risultati su disco", "Guardar los resultados en caché en disco",
            "Mettre les résultats en cache sur le disque", "Colocar os resultados em cache no disco",
            "تخزين النتائج مؤقتًا على القرص", "परिणाम डिस्क पर कैश करें",
            "ফলাফল ডিস্কে ক্যাশ করুন", "نتائج ڈسک پر کیش کریں", "将结果缓存到磁盘")

        ' The one entry of the language pickers that is NOT a language: every other row
        ' shows its own endonym and needs no translation (§2.8).
        Add("Автоопределение",
            "Auto-detect", "Автовизначення", "Automatisch erkennen", "Rilevamento automatico",
            "Detección automática", "Détection automatique", "Deteção automática",
            "كشف تلقائي", "स्वतः पहचान", "স্বয়ংক্রিয় শনাক্তকরণ", "خودکار شناخت", "自动检测")

        ' --- combo items --------------------------------------------------------------

        Add("Быстрая (fast)",
            "Fast", "Швидка (fast)", "Schnell (fast)", "Veloce (fast)", "Rápida (fast)",
            "Rapide (fast)", "Rápida (fast)", "سريع (fast)", "तेज़ (fast)",
            "দ্রুত (fast)", "تیز (fast)", "快速 (fast)")

        Add("Лучшая (best, медленнее)",
            "Best (more accurate, slower)", "Найкраща (best, повільніше)",
            "Beste (genauer, langsamer)", "Migliore (più precisa, più lenta)",
            "Mejor (más precisa, más lenta)", "Meilleure (plus précise, plus lente)",
            "Melhor (mais precisa, mais lenta)", "الأفضل (أدق وأبطأ)",
            "सर्वोत्तम (अधिक सटीक, धीमी)", "সেরা (আরও নির্ভুল, ধীর)",
            "بہترین (زیادہ درست، سست)", "最佳（更准确，更慢）")

        Add("Авто (рекомендуется)",
            "Auto (recommended)", "Авто (рекомендовано)", "Automatisch (empfohlen)",
            "Automatico (consigliato)", "Automático (recomendado)", "Auto (recommandé)",
            "Automático (recomendado)", "تلقائي (موصى به)", "स्वतः (अनुशंसित)",
            "স্বয়ংক্রিয় (প্রস্তাবিত)", "خودکار (تجویز کردہ)", "自动（推荐）")

        Add("Один блок",
            "Single block", "Один блок", "Ein Block", "Blocco singolo", "Bloque único",
            "Bloc unique", "Bloco único", "كتلة واحدة", "एकल ब्लॉक", "একক ব্লক",
            "ایک بلاک", "单一区块")

        Add("Разреженный текст",
            "Sparse text", "Розріджений текст", "Verstreuter Text", "Testo sparso",
            "Texto disperso", "Texte épars", "Texto disperso", "نص متفرّق",
            "विरल पाठ", "বিক্ষিপ্ত টেক্সট", "بکھرا ہوا متن", "稀疏文本")

        Add("Одна строка",
            "Single line", "Один рядок", "Eine Zeile", "Riga singola", "Línea única",
            "Ligne unique", "Linha única", "سطر واحد", "एकल पंक्ति", "একক লাইন",
            "ایک سطر", "单行")

        Add("Вертикальный текст",
            "Vertical text", "Вертикальний текст", "Vertikaler Text", "Testo verticale",
            "Texto vertical", "Texte vertical", "Texto vertical", "نص عمودي",
            "ऊर्ध्वाधर पाठ", "উল্লম্ব টেক্সট", "عمودی متن", "竖排文本")

        ' --- hints ---------------------------------------------------------------------

        Add("Включить распознавание текста и перевод на изображениях - для картинок, что упорно говорят на чужом языке.",
            "Enable on-image text recognition and translation - for pictures that stubbornly speak another language.",
            "Увімкнути розпізнавання тексту й переклад на зображеннях - для картинок, що вперто говорять чужою мовою.",
            "Texterkennung und Übersetzung im Bild aktivieren - für Bilder, die hartnäckig eine andere Sprache sprechen.",
            "Attiva il riconoscimento del testo e la traduzione sulle immagini - per le figure che si ostinano a parlare un'altra lingua.",
            "Activar el reconocimiento de texto y la traducción en las imágenes: para las que se empeñan en hablar otro idioma.",
            "Activer la reconnaissance de texte et la traduction sur les images - pour celles qui s'obstinent à parler une autre langue.",
            "Ativar o reconhecimento de texto e a tradução nas imagens - para as que teimam em falar outra língua.",
            "فعّل التعرّف على النص وترجمته داخل الصور - للصور التي تصرّ على التحدث بلغة أخرى.",
            "छवियों पर पाठ पहचान और अनुवाद सक्षम करें - उन चित्रों के लिए जो हठपूर्वक दूसरी भाषा बोलते हैं।",
            "ছবির উপর টেক্সট শনাক্তকরণ ও অনুবাদ চালু করুন - যেসব ছবি একগুঁয়েভাবে অন্য ভাষায় কথা বলে তাদের জন্য।",
            "تصاویر پر متن کی شناخت اور ترجمہ فعال کریں - ان تصویروں کے لیے جو ضد کر کے کسی اور زبان میں بولتی ہیں۔",
            "启用图片内文字识别与翻译 - 为那些偏偏说着另一种语言的图片准备。")

        Add("Распознавать и переводить автоматически после каждого изображения - вы только смотрите, программа отдувается.",
            "Recognize and translate automatically after each image - you just look, the app does the heavy lifting.",
            "Розпізнавати й перекладати автоматично після кожного зображення - ви лише дивитеся, програма відпрацьовує.",
            "Nach jedem Bild automatisch erkennen und übersetzen - Sie schauen nur, das Programm macht die Arbeit.",
            "Riconosci e traduci automaticamente dopo ogni immagine: tu guardi, il programma fatica.",
            "Reconocer y traducir automáticamente tras cada imagen: tú solo miras, el programa se encarga.",
            "Reconnaître et traduire automatiquement après chaque image - vous regardez, le programme travaille.",
            "Reconhecer e traduzir automaticamente após cada imagem - você só olha, o programa faz o trabalho.",
            "التعرّف والترجمة تلقائيًا بعد كل صورة - أنت تنظر فقط، والبرنامج يتولّى العمل.",
            "प्रत्येक छवि के बाद स्वतः पहचानें और अनुवाद करें - आप बस देखिए, काम कार्यक्रम करेगा।",
            "প্রতিটি ছবির পরে স্বয়ংক্রিয়ভাবে শনাক্ত ও অনুবাদ করুন - আপনি শুধু দেখুন, কাজটা প্রোগ্রাম করবে।",
            "ہر تصویر کے بعد خودکار شناخت اور ترجمہ - آپ صرف دیکھیں، کام پروگرام کرے گا۔",
            "每张图片显示后自动识别并翻译 - 您只管看，程序来干活。")

        Add("Сервис перевода: локальный Ollama или LibreTranslate.",
            "Translation service: local Ollama or LibreTranslate.",
            "Сервіс перекладу: локальний Ollama або LibreTranslate.",
            "Übersetzungsdienst: lokales Ollama oder LibreTranslate.",
            "Servizio di traduzione: Ollama locale o LibreTranslate.",
            "Servicio de traducción: Ollama local o LibreTranslate.",
            "Service de traduction : Ollama local ou LibreTranslate.",
            "Serviço de tradução: Ollama local ou LibreTranslate.",
            "خدمة الترجمة: Ollama محلي أو LibreTranslate.",
            "अनुवाद सेवा: स्थानीय Ollama या LibreTranslate।",
            "অনুবাদ পরিষেবা: স্থানীয় Ollama বা LibreTranslate।",
            "ترجمہ سروس: مقامی Ollama یا LibreTranslate۔",
            "翻译服务：本地 Ollama 或 LibreTranslate。")

        Add("Адрес сервера перевода (оставьте пустым для значения по умолчанию).",
            "Translation server URL (leave empty for the default).",
            "Адреса сервера перекладу (залиште порожнім для значення за замовчуванням).",
            "URL des Übersetzungsservers (leer lassen für den Standardwert).",
            "URL del server di traduzione (lascia vuoto per il valore predefinito).",
            "URL del servidor de traducción (déjalo vacío para el valor predeterminado).",
            "URL du serveur de traduction (laissez vide pour la valeur par défaut).",
            "URL do servidor de tradução (deixe vazio para o valor predefinido).",
            "عنوان خادم الترجمة (اتركه فارغًا للقيمة الافتراضية).",
            "अनुवाद सर्वर का URL (डिफ़ॉल्ट के लिए खाली छोड़ें)।",
            "অনুবাদ সার্ভারের URL (ডিফল্টের জন্য ফাঁকা রাখুন)।",
            "ترجمہ سرور کا URL (ڈیفالٹ کے لیے خالی چھوڑیں)۔",
            "翻译服务器地址（留空则使用默认值）。")

        Add("Скачать и установить Ollama - местного полиглота, который поселится на вашем компьютере.",
            "Download and install Ollama - a local polyglot that moves into your machine.",
            "Завантажити й встановити Ollama - місцевого поліглота, який оселиться на вашому комп'ютері.",
            "Ollama herunterladen und installieren - ein lokaler Polyglott, der bei Ihnen einzieht.",
            "Scarica e installa Ollama: un poliglotta locale che va a vivere sul tuo computer.",
            "Descargar e instalar Ollama: un políglota local que se muda a tu ordenador.",
            "Télécharger et installer Ollama - un polyglotte local qui emménage sur votre machine.",
            "Transferir e instalar o Ollama - um poliglota local que se muda para o seu computador.",
            "نزّل وثبّت Ollama - مترجمًا محليًا متعدد اللغات يسكن حاسوبك.",
            "Ollama डाउनलोड और स्थापित करें - एक स्थानीय बहुभाषी जो आपके कंप्यूटर में बस जाएगा।",
            "Ollama ডাউনলোড ও ইনস্টল করুন - একজন স্থানীয় বহুভাষী যে আপনার কম্পিউটারে থাকবে।",
            "Ollama ڈاؤن لوڈ اور انسٹال کریں - ایک مقامی کثیراللسانی جو آپ کے کمپیوٹر میں بس جائے گا۔",
            "下载并安装 Ollama - 一位住进您电脑的本地多语通。")

        Add("Запустить локальный сервер Ollama.",
            "Start the local Ollama server.", "Запустити локальний сервер Ollama.",
            "Den lokalen Ollama-Server starten.", "Avvia il server Ollama locale.",
            "Iniciar el servidor local de Ollama.", "Démarrer le serveur Ollama local.",
            "Iniciar o servidor Ollama local.", "شغّل خادم Ollama المحلي.",
            "स्थानीय Ollama सर्वर शुरू करें।", "স্থানীয় Ollama সার্ভার চালু করুন।",
            "مقامی Ollama سرور شروع کریں۔", "启动本地 Ollama 服务器。")

        Add("Имя модели Ollama для перевода.",
            "Ollama model name used for translation.", "Ім'я моделі Ollama для перекладу.",
            "Name des Ollama-Modells für die Übersetzung.", "Nome del modello Ollama usato per la traduzione.",
            "Nombre del modelo de Ollama usado para traducir.", "Nom du modèle Ollama utilisé pour la traduction.",
            "Nome do modelo Ollama usado na tradução.", "اسم نموذج Ollama المستخدم في الترجمة.",
            "अनुवाद के लिए उपयोग होने वाले Ollama मॉडल का नाम।",
            "অনুবাদে ব্যবহৃত Ollama মডেলের নাম।", "ترجمے کے لیے استعمال ہونے والے Ollama ماڈل کا نام۔",
            "用于翻译的 Ollama 模型名称。")

        Add("Загрузить выбранную модель в Ollama.",
            "Pull the selected model into Ollama.", "Завантажити вибрану модель в Ollama.",
            "Das gewählte Modell in Ollama laden.", "Scarica il modello selezionato in Ollama.",
            "Descargar el modelo elegido en Ollama.", "Télécharger le modèle choisi dans Ollama.",
            "Transferir o modelo escolhido para o Ollama.", "نزّل النموذج المحدد إلى Ollama.",
            "चयनित मॉडल को Ollama में डाउनलोड करें।", "নির্বাচিত মডেলটি Ollama-তে ডাউনলোড করুন।",
            "منتخب ماڈل کو Ollama میں ڈاؤن لوڈ کریں۔", "将所选模型拉取到 Ollama。")

        Add("Ключ API (для облачных переводчиков). Храним зашифрованным, честное слово.",
            "API key (for cloud translators). We keep it encrypted, scout's honor.",
            "Ключ API (для хмарних перекладачів). Зберігаємо зашифрованим, чесне слово.",
            "API-Schlüssel (für Cloud-Übersetzer). Wir speichern ihn verschlüsselt, Ehrenwort.",
            "Chiave API (per i traduttori cloud). La conserviamo cifrata, parola d'onore.",
            "Clave de API (para traductores en la nube). La guardamos cifrada, palabra.",
            "Clé API (pour les traducteurs cloud). Nous la stockons chiffrée, parole d'honneur.",
            "Chave de API (para tradutores na nuvem). Guardamo-la cifrada, palavra de honra.",
            "مفتاح API (للمترجمات السحابية). نحفظه مشفّرًا، وعد.",
            "API कुंजी (क्लाउड अनुवादकों के लिए)। हम इसे एन्क्रिप्टेड रखते हैं, वचन।",
            "API কী (ক্লাউড অনুবাদকের জন্য)। আমরা এটি এনক্রিপ্ট করে রাখি, কথা দিলাম।",
            "API کلید (کلاؤڈ مترجمین کے لیے)۔ ہم اسے خفیہ کر کے رکھتے ہیں، وعدہ۔",
            "API 密钥（用于云端翻译）。我们加密保存，说话算数。")

        Add("Язык, на который переводить текст.",
            "Language to translate the text into.", "Мова, якою перекладати текст.",
            "Sprache, in die der Text übersetzt wird.", "Lingua in cui tradurre il testo.",
            "Idioma al que traducir el texto.", "Langue vers laquelle traduire le texte.",
            "Idioma para o qual traduzir o texto.", "اللغة التي يُترجم إليها النص.",
            "पाठ का अनुवाद जिस भाषा में करना है।", "যে ভাষায় টেক্সট অনুবাদ করতে হবে।",
            "وہ زبان جس میں متن کا ترجمہ کرنا ہے۔", "要将文字翻译成的语言。")

        Add("Язык исходного текста на изображении (или автоопределение).",
            "Language of the source text on the image (or auto-detect).",
            "Мова вихідного тексту на зображенні (або автовизначення).",
            "Sprache des Ausgangstexts im Bild (oder automatisch erkennen).",
            "Lingua del testo originale nell'immagine (o rilevamento automatico).",
            "Idioma del texto original de la imagen (o detección automática).",
            "Langue du texte source sur l'image (ou détection automatique).",
            "Idioma do texto original na imagem (ou deteção automática).",
            "لغة النص الأصلي في الصورة (أو الكشف التلقائي).",
            "छवि पर मूल पाठ की भाषा (या स्वतः पहचान)।",
            "ছবিতে থাকা মূল লেখার ভাষা (বা স্বয়ংক্রিয় শনাক্তকরণ)।",
            "تصویر پر اصل متن کی زبان (یا خودکار شناخت)۔",
            "图片上原文的语言（或自动检测）。")

        Add("Качество распознавания: быстрое или лучшее (то есть медленнее). Вечный выбор между скоростью и совестью.",
            "Recognition quality: fast or best (read: slower). The eternal trade-off between speed and conscience.",
            "Якість розпізнавання: швидке чи найкраще (тобто повільніше). Вічний вибір між швидкістю й совістю.",
            "Erkennungsqualität: schnell oder am besten (sprich: langsamer). Die ewige Wahl zwischen Tempo und Gewissen.",
            "Qualità del riconoscimento: veloce o migliore (cioè più lenta). L'eterno compromesso tra velocità e coscienza.",
            "Calidad del reconocimiento: rápida o la mejor (léase: más lenta). La eterna elección entre velocidad y conciencia.",
            "Qualité de reconnaissance : rapide ou meilleure (comprendre : plus lente). L'éternel choix entre vitesse et conscience.",
            "Qualidade do reconhecimento: rápida ou melhor (leia-se: mais lenta). A eterna escolha entre velocidade e consciência.",
            "جودة التعرّف: سريعة أم الأفضل (أي أبطأ). المفاضلة الأزلية بين السرعة والضمير.",
            "पहचान की गुणवत्ता: तेज़ या सर्वोत्तम (यानी धीमी)। गति और अंतरात्मा के बीच शाश्वत चुनाव।",
            "শনাক্তকরণের মান: দ্রুত নাকি সেরা (অর্থাৎ ধীর)। গতি ও বিবেকের মধ্যে চিরন্তন দ্বন্দ্ব।",
            "شناخت کا معیار: تیز یا بہترین (یعنی سست)۔ رفتار اور ضمیر کے درمیان ازلی انتخاب۔",
            "识别质量：快速还是最佳（也就是更慢）。速度与良心之间的永恒取舍。")

        Add("Режим разбора страницы Tesseract.",
            "Tesseract page segmentation mode.", "Режим розбору сторінки Tesseract.",
            "Tesseract-Seitensegmentierungsmodus.", "Modalità di segmentazione della pagina di Tesseract.",
            "Modo de segmentación de página de Tesseract.", "Mode de segmentation de page de Tesseract.",
            "Modo de segmentação de página do Tesseract.", "وضع تقسيم الصفحة في Tesseract.",
            "Tesseract का पेज सेगमेंटेशन मोड।", "Tesseract-এর পেজ সেগমেন্টেশন মোড।",
            "Tesseract کا صفحہ سیگمنٹیشن موڈ۔", "Tesseract 的页面分割模式。")

        Add("Скачать языковой пакет распознавания для выбранного языка.",
            "Download the recognition language pack for the chosen language.",
            "Завантажити мовний пакет розпізнавання для вибраної мови.",
            "Das Erkennungssprachpaket für die gewählte Sprache herunterladen.",
            "Scarica il pacchetto linguistico di riconoscimento per la lingua scelta.",
            "Descargar el paquete de reconocimiento del idioma elegido.",
            "Télécharger le pack de reconnaissance pour la langue choisie.",
            "Transferir o pacote de reconhecimento para o idioma escolhido.",
            "نزّل حزمة لغة التعرّف للّغة المختارة.",
            "चुनी गई भाषा के लिए पहचान भाषा पैक डाउनलोड करें।",
            "নির্বাচিত ভাষার জন্য শনাক্তকরণ ভাষা প্যাক ডাউনলোড করুন।",
            "منتخب زبان کے لیے شناخت کا زبان پیک ڈاؤن لوڈ کریں۔",
            "为所选语言下载识别语言包。")

        Add("Непрозрачность наложения перевода - от еле заметного до полностью закрывающего оригинал.",
            "Opacity of the translation overlay - from barely there to fully hiding the original.",
            "Непрозорість накладення перекладу - від ледь помітного до цілком закриваючого оригінал.",
            "Deckkraft der Übersetzungsüberlagerung - von kaum sichtbar bis das Original vollständig verdeckend.",
            "Opacità della sovrapposizione della traduzione: da appena visibile a completamente coprente.",
            "Opacidad de la superposición de traducción: desde apenas visible hasta tapar del todo el original.",
            "Opacité de la superposition de traduction - de à peine visible à masquant totalement l'original.",
            "Opacidade da sobreposição de tradução - de quase invisível a tapar totalmente o original.",
            "عتامة تراكب الترجمة - من الكاد يُرى إلى إخفاء الأصل تمامًا.",
            "अनुवाद ओवरले की अपारदर्शिता - बमुश्किल दिखने से लेकर मूल को पूरी तरह ढकने तक।",
            "অনুবাদ ওভারলের অস্বচ্ছতা - প্রায় অদৃশ্য থেকে মূলটি পুরোপুরি ঢেকে দেওয়া পর্যন্ত।",
            "ترجمے کے اوورلے کا دھندلاپن - بمشکل نظر آنے سے لے کر اصل کو مکمل چھپانے تک۔",
            "翻译叠加层的不透明度 - 从几乎看不见到完全遮住原文。")

        Add("Показывать или скрывать уже распознанный перевод поверх изображения.",
            "Show or hide the already recognized translation over the image.",
            "Показувати або приховувати вже розпізнаний переклад поверх зображення.",
            "Die bereits erkannte Übersetzung über dem Bild ein- oder ausblenden.",
            "Mostra o nascondi la traduzione già riconosciuta sopra l'immagine.",
            "Mostrar u ocultar la traducción ya reconocida sobre la imagen.",
            "Afficher ou masquer la traduction déjà reconnue au-dessus de l'image.",
            "Mostrar ou ocultar a tradução já reconhecida sobre a imagem.",
            "إظهار أو إخفاء الترجمة المتعرَّف عليها فوق الصورة.",
            "छवि के ऊपर पहले से पहचाने गए अनुवाद को दिखाएँ या छिपाएँ।",
            "ছবির উপরে ইতিমধ্যে শনাক্ত হওয়া অনুবাদ দেখান বা লুকান।",
            "تصویر کے اوپر پہلے سے شناخت شدہ ترجمہ دکھائیں یا چھپائیں۔",
            "在图片上显示或隐藏已识别的译文。")

        Add("Кэшировать результаты на диск, чтобы не распознавать одно и то же дважды.",
            "Cache results on disk, so the same image isn't recognized twice for nothing.",
            "Кешувати результати на диск, щоб не розпізнавати те саме двічі.",
            "Ergebnisse auf der Festplatte zwischenspeichern, damit dasselbe Bild nicht zweimal erkannt wird.",
            "Memorizza i risultati su disco, per non riconoscere due volte la stessa immagine.",
            "Guardar los resultados en disco para no reconocer dos veces la misma imagen.",
            "Mettre les résultats en cache sur le disque, pour ne pas reconnaître deux fois la même image.",
            "Guardar os resultados em cache no disco, para não reconhecer a mesma imagem duas vezes.",
            "خزّن النتائج على القرص حتى لا يُعاد التعرّف على الصورة نفسها مرتين.",
            "परिणाम डिस्क पर कैश करें, ताकि एक ही छवि दो बार न पहचानी जाए।",
            "ফলাফল ডিস্কে ক্যাশ করুন, যাতে একই ছবি দু'বার শনাক্ত করতে না হয়।",
            "نتائج ڈسک پر کیش کریں تاکہ ایک ہی تصویر دو بار شناخت نہ کرنی پڑے۔",
            "将结果缓存到磁盘，避免同一张图片被识别两次。")

    End Sub

End Class
