Option Strict On

' <summary>
' Strings of SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md - both halves,
' because they arrived in one specification and share one settings page: the decode
' cache's two settings rows, and everything "Replace with video" can have to say.
'
' Modern-only surfaces, but the table is shared - a string table costs the x86 build
' nothing, and splitting it per build is how two tables drift apart (the same reasoning
' Localization.Editor.vb states).
'
' Two placeholder notes, both load-bearing:
'   * the confirmation names BOTH file names through {0}/{1} rather than concatenating,
'     because "will be converted into" sits between them in some languages and around
'     them in others;
'   * the FFmpeg prompt carries its download size as {0} rather than a literal number, so
'     the sentence cannot go stale when the pinned build changes size - the constant in
'     FfmpegRuntime is the single place that number lives.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddVideoStrings()

        Add("Обложки, управление и поведение аудиодорожек.", "Cover art, controls and audio-track behaviour.", "Обкладинки, керування та поведінка аудіодоріжок.", "Cover, Steuerung und Verhalten von Audiotiteln.", "Copertine, controlli e comportamento delle tracce audio.", "Carátulas, controles y comportamiento de pistas de audio.", "Pochettes, commandes et comportement des pistes audio.", "Capas, controles e comportamento de faixas de áudio.", "الغلاف وعناصر التحكم وسلوك المسارات الصوتية.", "कवर, नियंत्रण और ऑडियो ट्रैक का व्यवहार।", "কভার, নিয়ন্ত্রণ এবং অডিও ট্র্যাকের আচরণ।", "کور، کنٹرول اور آڈیو ٹریک کا رویہ۔", "封面、控件和音频曲目行为。")

        Add("Таймер сна остановил воспроизведение", "Sleep timer stopped playback", "Таймер сну зупинив відтворення", "Sleep-Timer hat die Wiedergabe beendet", "Il timer di spegnimento ha fermato la riproduzione", "El temporizador de apagado detuvo la reproducción", "La minuterie de sommeil a arrêté la lecture", "O temporizador de sono parou a reprodução", "أوقف مؤقت النوم التشغيل", "स्लीप टाइमर ने प्लेबैक रोक दिया", "স্লিপ টাইমার প্লেব্যাক বন্ধ করেছে", "سلیپ ٹائمر نے پلے بیک روک دیا", "睡眠定时器已停止播放")
        Add("Остановиться", "Stop", "Зупинитися", "Anhalten", "Fermare", "Detener", "Arrêter", "Parar", "إيقاف", "रोकें", "থামুন", "رکیں", "停止")
        Add("Учитывать видео при пролистывании файлов", "Include videos when browsing files", "Ураховувати відео під час перегляду файлів", "Videos beim Durchsuchen von Dateien einbeziehen", "Includi i video durante la navigazione dei file", "Incluir vídeos al explorar archivos", "Inclure les vidéos lors de la navigation dans les fichiers", "Incluir vídeos ao navegar pelos ficheiros", "تضمين الفيديو عند استعراض الملفات", "फ़ाइलों को ब्राउज़ करते समय वीडियो शामिल करें", "ফাইল ব্রাউজ করার সময় ভিডিও অন্তর্ভুক্ত করুন", "فائلوں کو براؤز کرتے وقت ویڈیو شامل کریں", "浏览文件时包含视频")
        Add("Учитывать аудио при пролистывании файлов", "Include audio when browsing files", "Ураховувати аудіо під час перегляду файлів", "Audio beim Durchsuchen von Dateien einbeziehen", "Includi l'audio durante la navigazione dei file", "Incluir audio al explorar archivos", "Inclure l'audio lors de la navigation dans les fichiers", "Incluir áudio ao navegar pelos ficheiros", "تضمين الصوت عند استعراض الملفات", "फ़ाइलों को ब्राउज़ करते समय ऑडियो शामिल करें", "ফাইল ব্রাউজ করার সময় অডিও অন্তর্ভুক্ত করুন", "فائلوں کو براؤز کرتے وقت آڈیو شامل کریں", "浏览文件时包含音频")
        Add("Выключенные типы не показываются в текущей папке и не входят в счётчик.", "Disabled types are hidden from the current folder and its count.", "Вимкнені типи не показуються в поточній папці й не входять до лічильника.", "Deaktivierte Typen werden im aktuellen Ordner und dessen Zähler nicht angezeigt.", "I tipi disattivati non sono mostrati nella cartella corrente né nel conteggio.", "Los tipos desactivados no aparecen en la carpeta actual ni en el contador.", "Les types désactivés ne sont pas affichés dans le dossier actuel ni dans le compteur.", "Os tipos desativados não aparecem na pasta atual nem no contador.", "لا تظهر الأنواع المعطلة في المجلد الحالي ولا تدخل في العداد.", "बंद किए गए प्रकार वर्तमान फ़ोल्डर और गिनती में नहीं दिखते।", "নিষ্ক্রিয় ধরনগুলি বর্তমান ফোল্ডার বা গণনায় দেখানো হয় না।", "غیر فعال اقسام موجودہ فولڈر یا شمار میں نہیں دکھائی جاتیں۔", "已禁用的类型不会显示在当前文件夹或计数中。")
        Add("Поведение аудио", "Audio behaviour", "Поведінка аудіо", "Audioverhalten", "Comportamento audio", "Comportamiento de audio", "Comportement audio", "Comportamento de áudio", "سلوك الصوت", "ऑडियो व्यवहार", "অডিও আচরণ", "آڈیو رویہ", "音频行为")
        Add("После окончания аудио", "After audio ends", "Після завершення аудіо", "Nach Audioende", "Dopo la fine dell'audio", "Después de terminar el audio", "Après la fin de l'audio", "Depois do fim do áudio", "بعد انتهاء الصوت", "ऑडियो खत्म होने पर", "অডিও শেষ হলে", "آڈیو ختم ہونے کے بعد", "音频结束后")
        Add("Всегда показывать панель аудио", "Always show audio controls", "Завжди показувати панель аудіо", "Audiosteuerung immer anzeigen", "Mostra sempre i controlli audio", "Mostrar siempre los controles de audio", "Toujours afficher les commandes audio", "Mostrar sempre os controles de áudio", "إظهار عناصر تحكم الصوت دائماً", "हमेशा ऑडियो नियंत्रण दिखाएं", "সবসময় অডিও নিয়ন্ত্রণ দেখান", "ہمیشہ آڈیو کنٹرول دکھائیں", "始终显示音频控件")
        Add("Визуализатор без обложки", "Visualiser without cover art", "Візуалізатор без обкладинки", "Visualisierung ohne Cover", "Visualizzatore senza copertina", "Visualizador sin carátula", "Visualiseur sans pochette", "Visualizador sem capa", "مرئي بلا غلاف", "कवर के बिना विज़ुअलाइज़र", "কভার ছাড়া ভিজ্যুয়ালাইজার", "کور کے بغیر ویژولائزر", "无封面时显示可视化")
        Add("Показывает фирменные волны и частицы, если в аудио нет встроенной обложки.", "Shows branded waves and particles when the audio file has no embedded cover art.", "Показує фірмові хвилі й частинки, якщо в аудіо немає вбудованої обкладинки.", "Zeigt Markenwellen und Partikel ohne eingebettetes Cover.", "Mostra onde e particelle del marchio senza copertina incorporata.", "Muestra ondas y partículas de marca sin carátula incrustada.", "Affiche les vagues et particules de la marque sans pochette intégrée.", "Mostra ondas e partículas da marca sem capa incorporada.", "يعرض موجات وجسيمات العلامة عند عدم وجود غلاف مضمن.", "जब एम्बेड किया कवर न हो तो ब्रांडेड तरंगें और कण दिखाता है।", "এমবেড করা কভার না থাকলে ব্র্যান্ডেড তরঙ্গ ও কণা দেখায়।", "جب ایمبیڈڈ کور نہ ہو تو برانڈڈ لہریں اور ذرات دکھاتا ہے۔", "没有内嵌封面时显示品牌波浪和粒子。")
        Add("Таймер сна, мин", "Sleep timer, min", "Таймер сну, хв", "Sleep-Timer, Min.", "Timer di spegnimento, min", "Temporizador de apagado, min", "Minuterie de sommeil, min", "Temporizador de sono, min", "مؤقت النوم، دقيقة", "स्लीप टाइमर, मिनट", "স্লিপ টাইমার, মিনিট", "سلیپ ٹائمر، منٹ", "睡眠定时器，分钟")
        Add("Что делать, когда аудиодорожка достигла конца.", "What to do when an audio track reaches its end.", "Що робити, коли аудіодоріжка дійшла кінця.", "Was beim Ende eines Audiotitels geschehen soll.", "Cosa fare quando una traccia audio finisce.", "Qué hacer cuando termina una pista de audio.", "Que faire lorsqu'une piste audio arrive à sa fin.", "O que fazer quando uma faixa de áudio termina.", "ما يجب فعله عند انتهاء المسار الصوتي.", "ऑडियो ट्रैक समाप्त होने पर क्या करना है।", "অডিও ট্র্যাক শেষ হলে কী করতে হবে।", "آڈیو ٹریک ختم ہونے پر کیا کرنا ہے۔", "音频曲目结束时要做什么。")
        Add("Оставляет транспортную панель видимой во время аудио.", "Keeps the transport bar visible during audio.", "Не ховає панель керування під час аудіо.", "Lässt die Steuerleiste bei Audio sichtbar.", "Mantiene visibile la barra di controllo durante l'audio.", "Mantiene visible la barra de controles durante el audio.", "Garde la barre de contrôle visible pendant l'audio.", "Mantém a barra de controles visível durante o áudio.", "يبقي شريط التحكم ظاهراً أثناء الصوت.", "ऑडियो के दौरान नियंत्रण पट्टी दिखाता है।", "অডিও চলাকালে কন্ট্রোল বার দেখায়।", "آڈیو کے دوران کنٹرول بار دکھاتا ہے۔", "音频期间保持控制栏可见。")
        Add("Показывает визуализатор VLC, если у аудио нет обложки.", "Shows VLC's visualiser when audio has no cover art.", "Показує візуалізатор VLC, якщо в аудіо немає обкладинки.", "Zeigt den VLC-Visualisierer ohne Cover.", "Mostra il visualizzatore VLC senza copertina.", "Muestra el visualizador de VLC sin carátula.", "Affiche le visualiseur VLC sans pochette.", "Mostra o visualizador VLC sem capa.", "يعرض مرئي VLC عند عدم وجود غلاف.", "कवर न होने पर VLC विज़ुअलाइज़र दिखाता है।", "কভার না থাকলে VLC ভিজ্যুয়ালাইজার দেখায়।", "کور نہ ہونے پر VLC ویژولائزر دکھاتا ہے۔", "没有封面时显示 VLC 可视化。")
        Add("Через сколько минут остановить воспроизведение. 0 отключает таймер.", "How many minutes before stopping playback. 0 turns the timer off.", "Через скільки хвилин зупинити відтворення. 0 вимикає таймер.", "Nach wie vielen Minuten die Wiedergabe endet. 0 schaltet den Timer aus.", "Dopo quanti minuti fermare la riproduzione. 0 disattiva il timer.", "Cuántos minutos antes de detener la reproducción. 0 desactiva el temporizador.", "Après combien de minutes arrêter la lecture. 0 désactive la minuterie.", "Após quantos minutos parar a reprodução. 0 desliga o temporizador.", "عدد الدقائق قبل إيقاف التشغيل. 0 يعطّل المؤقت.", "प्लेबैक रोकने से पहले मिनट। 0 टाइमर बंद करता है।", "প্লেব্যাক বন্ধ করার আগে মিনিট। 0 টাইমার বন্ধ করে।", "پلے بیک روکنے سے پہلے منٹ۔ 0 ٹائمر بند کرتا ہے۔", "停止播放前的分钟数。0 表示关闭定时器。")

        ' --- the decode cache: two settings rows and one confirmation (§6.2) ----------
        Add("Кэш декодирования, МБ",
            "Decode cache, MB", "Кеш декодування, МБ", "Dekodier-Cache, MB",
            "Cache di decodifica, MB", "Caché de decodificación, MB", "Cache de décodage, Mo",
            "Cache de decodificação, MB", "ذاكرة فك الترميز، ميغابايت", "डिकोड कैश, MB",
            "ডিকোড ক্যাশ, MB", "ڈی کوڈ کیش، MB", "解码缓存，MB")
        Add("Ускоряет повторное открытие анимаций и медленных форматов. 0 отключает кэш.",
            "Speeds up reopening animations and slow formats. 0 turns the cache off.",
            "Пришвидшує повторне відкриття анімацій і повільних форматів. 0 вимикає кеш.",
            "Beschleunigt das erneute Öffnen von Animationen und langsamen Formaten. 0 schaltet den Cache aus.",
            "Velocizza la riapertura di animazioni e formati lenti. 0 disattiva la cache.",
            "Acelera la reapertura de animaciones y formatos lentos. 0 desactiva la caché.",
            "Accélère la réouverture des animations et des formats lents. 0 désactive le cache.",
            "Acelera a reabertura de animações e formatos lentos. 0 desativa o cache.",
            "يسرّع إعادة فتح الصور المتحركة والصيغ البطيئة. القيمة 0 تعطّل الذاكرة.",
            "एनिमेशन और धीमे प्रारूपों को दोबारा खोलना तेज़ करता है। 0 कैश बंद कर देता है।",
            "অ্যানিমেশন ও ধীর ফরম্যাট আবার খোলা দ্রুত করে। 0 দিলে ক্যাশ বন্ধ।",
            "اینیمیشن اور سست فارمیٹ دوبارہ کھولنا تیز کرتا ہے۔ 0 کیش بند کر دیتا ہے۔",
            "加快再次打开动画和慢速格式的速度。0 表示关闭缓存。")
        Add("Текущий размер сохранённых результатов декодирования.",
            "How much decoded data is currently kept.",
            "Поточний розмір збережених результатів декодування.",
            "Aktuelle Größe der gespeicherten Dekodier-Ergebnisse.",
            "Dimensione attuale dei risultati di decodifica salvati.",
            "Tamaño actual de los resultados de decodificación guardados.",
            "Taille actuelle des résultats de décodage enregistrés.",
            "Tamanho atual dos resultados de decodificação salvos.",
            "الحجم الحالي لنتائج فك الترميز المحفوظة.",
            "सहेजे गए डिकोड परिणामों का वर्तमान आकार।",
            "সংরক্ষিত ডিকোড ফলাফলের বর্তমান আকার।",
            "محفوظ شدہ ڈی کوڈ نتائج کا موجودہ حجم۔",
            "已保存解码结果的当前大小。")
        Add("Удалить сохранённые результаты декодирования? Сами изображения не изменятся.",
            "Delete the saved decode results? The images themselves are not changed.",
            "Видалити збережені результати декодування? Самі зображення не зміняться.",
            "Die gespeicherten Dekodier-Ergebnisse löschen? Die Bilder selbst bleiben unverändert.",
            "Eliminare i risultati di decodifica salvati? Le immagini non vengono modificate.",
            "¿Eliminar los resultados de decodificación guardados? Las imágenes no se modifican.",
            "Supprimer les résultats de décodage enregistrés ? Les images elles-mêmes ne changent pas.",
            "Excluir os resultados de decodificação salvos? As imagens em si não mudam.",
            "هل تريد حذف نتائج فك الترميز المحفوظة؟ لن تتغيّر الصور نفسها.",
            "सहेजे गए डिकोड परिणाम हटाएँ? छवियाँ स्वयं नहीं बदलेंगी।",
            "সংরক্ষিত ডিকোড ফলাফল মুছবেন? ছবিগুলো নিজে বদলাবে না।",
            "محفوظ شدہ ڈی کوڈ نتائج حذف کریں؟ تصاویر خود تبدیل نہیں ہوں گی۔",
            "删除已保存的解码结果？图片本身不会改变。")

        ' --- entry point: toolbar button + picture menu (§11) -------------------------
        Add("Заменить видео",
            "Replace with video", "Замінити відео", "Durch Video ersetzen",
            "Sostituisci con un video", "Reemplazar por vídeo", "Remplacer par une vidéo",
            "Substituir por vídeo", "استبدال بفيديو", "वीडियो से बदलें",
            "ভিডিও দিয়ে বদলান", "ویڈیو سے بدلیں", "替换为视频")
        Add("Заменить видео..",
            "Replace with video..", "Замінити відео..", "Durch Video ersetzen..",
            "Sostituisci con un video..", "Reemplazar por vídeo..", "Remplacer par une vidéo..",
            "Substituir por vídeo..", "استبدال بفيديو..", "वीडियो से बदलें..",
            "ভিডিও দিয়ে বদলান..", "ویڈیو سے بدلیں..", "替换为视频..")
        Add("Преобразовать анимацию в видео и удалить оригинал",
            "Convert the animation to a video and delete the original",
            "Перетворити анімацію на відео та видалити оригінал",
            "Die Animation in ein Video umwandeln und das Original löschen",
            "Converti l'animazione in un video ed elimina l'originale",
            "Convertir la animación en un vídeo y borrar el original",
            "Convertir l'animation en vidéo et supprimer l'original",
            "Converter a animação em vídeo e excluir o original",
            "تحويل الصورة المتحركة إلى فيديو وحذف الملف الأصلي",
            "एनिमेशन को वीडियो में बदलें और मूल फ़ाइल हटाएँ",
            "অ্যানিমেশনটি ভিডিওতে রূপান্তর করে মূল ফাইল মুছে ফেলুন",
            "اینیمیشن کو ویڈیو میں بدلیں اور اصل فائل حذف کریں",
            "将动画转换为视频并删除原文件")

        ' --- the confirmation (§10.1) -------------------------------------------------
        Add("Преобразование в видео",
            "Convert to video", "Перетворення на відео", "In Video umwandeln",
            "Conversione in video", "Convertir a vídeo", "Conversion en vidéo",
            "Conversão em vídeo", "التحويل إلى فيديو", "वीडियो में बदलना",
            "ভিডিওতে রূপান্তর", "ویڈیو میں تبدیلی", "转换为视频")
        Add("{0} будет преобразован в {1}. Оригинал будет удалён безвозвратно, минуя Корзину.",
            "{0} will be converted into {1}. The original is deleted permanently, bypassing the Recycle Bin.",
            "{0} буде перетворено на {1}. Оригінал буде видалено безповоротно, повз Кошик.",
            "{0} wird in {1} umgewandelt. Das Original wird endgültig gelöscht, am Papierkorb vorbei.",
            "{0} verrà convertito in {1}. L'originale viene eliminato definitivamente, senza passare dal Cestino.",
            "{0} se convertirá en {1}. El original se elimina de forma permanente, sin pasar por la Papelera.",
            "{0} sera converti en {1}. L'original est supprimé définitivement, sans passer par la Corbeille.",
            "{0} será convertido em {1}. O original é excluído permanentemente, sem passar pela Lixeira.",
            "سيتم تحويل {0} إلى {1}. سيُحذف الملف الأصلي نهائيًا دون المرور بسلة المحذوفات.",
            "{0} को {1} में बदला जाएगा। मूल फ़ाइल रीसायकल बिन में गए बिना स्थायी रूप से हट जाएगी।",
            "{0} কে {1} এ রূপান্তর করা হবে। মূল ফাইলটি রিসাইকল বিনে না গিয়ে স্থায়ীভাবে মুছে যাবে।",
            "{0} کو {1} میں بدلا جائے گا۔ اصل فائل ری سائیکل بن میں گئے بغیر مستقل طور پر حذف ہو جائے گی۔",
            "{0} 将被转换为 {1}。原文件将被永久删除，不经过回收站。")
        Add("Прозрачность будет залита чёрным.",
            "Transparency will be filled with black.", "Прозорість буде залито чорним.",
            "Transparenz wird mit Schwarz gefüllt.", "La trasparenza verrà riempita di nero.",
            "La transparencia se rellenará de negro.", "La transparence sera remplie de noir.",
            "A transparência será preenchida com preto.", "ستُملأ الشفافية باللون الأسود.",
            "पारदर्शिता को काले रंग से भर दिया जाएगा।", "স্বচ্ছতা কালো রঙে ভরাট করা হবে।",
            "شفافیت کو سیاہ رنگ سے بھر دیا جائے گا۔", "透明区域将被填充为黑色。")
        Add("Больше не спрашивать",
            "Do not ask again", "Більше не запитувати", "Nicht mehr fragen",
            "Non chiedere più", "No volver a preguntar", "Ne plus demander",
            "Não perguntar novamente", "عدم السؤال مرة أخرى", "फिर से न पूछें",
            "আর জিজ্ঞাসা করবেন না", "دوبارہ نہ پوچھیں", "不再询问")
        Add("Преобразовать",
            "Convert", "Перетворити", "Umwandeln", "Converti", "Convertir", "Convertir",
            "Converter", "تحويل", "बदलें", "রূপান্তর", "تبدیل کریں", "转换")

        ' --- the FFmpeg download prompt (§8.3) ----------------------------------------
        Add("Для создания видео нужен FFmpeg (около {0} МБ). Он будет загружен с сайта проекта и сохранён в папке программы. FFmpeg - свободная программа под лицензией GPL. Загрузить сейчас?",
            "Creating a video needs FFmpeg (about {0} MB). It will be downloaded from the project's own site and kept in the application's folder. FFmpeg is free software under the GPL. Download it now?",
            "Для створення відео потрібен FFmpeg (близько {0} МБ). Його буде завантажено із сайту проєкту та збережено в теці програми. FFmpeg - вільна програма під ліцензією GPL. Завантажити зараз?",
            "Zum Erstellen eines Videos wird FFmpeg benötigt (etwa {0} MB). Es wird von der Projektseite geladen und im Programmordner abgelegt. FFmpeg ist freie Software unter der GPL. Jetzt herunterladen?",
            "Per creare un video serve FFmpeg (circa {0} MB). Verrà scaricato dal sito del progetto e salvato nella cartella del programma. FFmpeg è software libero con licenza GPL. Scaricarlo ora?",
            "Para crear un vídeo hace falta FFmpeg (unos {0} MB). Se descargará del sitio del proyecto y se guardará en la carpeta del programa. FFmpeg es software libre con licencia GPL. ¿Descargarlo ahora?",
            "La création d'une vidéo nécessite FFmpeg (environ {0} Mo). Il sera téléchargé depuis le site du projet et conservé dans le dossier du programme. FFmpeg est un logiciel libre sous licence GPL. Le télécharger maintenant ?",
            "Para criar um vídeo é preciso o FFmpeg (cerca de {0} MB). Ele será baixado do site do projeto e guardado na pasta do programa. O FFmpeg é software livre sob a licença GPL. Baixar agora?",
            "يحتاج إنشاء الفيديو إلى FFmpeg (نحو {0} ميغابايت). سيُنزَّل من موقع المشروع ويُحفظ في مجلد البرنامج. FFmpeg برنامج حر برخصة GPL. هل تريد تنزيله الآن؟",
            "वीडियो बनाने के लिए FFmpeg चाहिए (लगभग {0} MB)। इसे परियोजना की साइट से डाउनलोड करके प्रोग्राम के फ़ोल्डर में रखा जाएगा। FFmpeg GPL लाइसेंस वाला मुक्त सॉफ़्टवेयर है। अभी डाउनलोड करें?",
            "ভিডিও তৈরি করতে FFmpeg দরকার (প্রায় {0} MB)। এটি প্রকল্পের সাইট থেকে নামিয়ে প্রোগ্রামের ফোল্ডারে রাখা হবে। FFmpeg হলো GPL লাইসেন্সের মুক্ত সফটওয়্যার। এখনই নামাবেন?",
            "ویڈیو بنانے کے لیے FFmpeg درکار ہے (تقریباً {0} MB)۔ اسے منصوبے کی سائٹ سے ڈاؤن لوڈ کر کے پروگرام کے فولڈر میں رکھا جائے گا۔ FFmpeg GPL لائسنس والا آزاد سافٹ ویئر ہے۔ ابھی ڈاؤن لوڈ کریں؟",
            "创建视频需要 FFmpeg（约 {0} MB）。它将从项目网站下载并保存在程序文件夹中。FFmpeg 是 GPL 许可下的自由软件。现在下载吗？")

        ' --- while it runs, and what it reports afterwards (§9.4, §10) ----------------
        Add("Создаю видео..",
            "Creating the video..", "Створюю відео..", "Video wird erstellt..",
            "Creazione del video..", "Creando el vídeo..", "Création de la vidéo..",
            "Criando o vídeo..", "جارٍ إنشاء الفيديو..", "वीडियो बन रहा है..",
            "ভিডিও তৈরি হচ্ছে..", "ویڈیو بن رہی ہے..", "正在创建视频..")
        Add("Создаю видео.. {0} %",
            "Creating the video.. {0} %", "Створюю відео.. {0} %", "Video wird erstellt.. {0} %",
            "Creazione del video.. {0} %", "Creando el vídeo.. {0} %", "Création de la vidéo.. {0} %",
            "Criando o vídeo.. {0} %", "جارٍ إنشاء الفيديو.. {0} %", "वीडियो बन रहा है.. {0} %",
            "ভিডিও তৈরি হচ্ছে.. {0} %", "ویڈیو بن رہی ہے.. {0} %", "正在创建视频.. {0} %")
        Add("Готово: {0}",
            "Done: {0}", "Готово: {0}", "Fertig: {0}", "Fatto: {0}", "Listo: {0}",
            "Terminé : {0}", "Pronto: {0}", "تم: {0}", "पूर्ण: {0}", "সম্পন্ন: {0}",
            "مکمل: {0}", "完成：{0}")
        Add("Не удалось создать видео: {0}",
            "The video could not be created: {0}", "Не вдалося створити відео: {0}",
            "Das Video konnte nicht erstellt werden: {0}", "Impossibile creare il video: {0}",
            "No se pudo crear el vídeo: {0}", "Impossible de créer la vidéo : {0}",
            "Não foi possível criar o vídeo: {0}", "تعذّر إنشاء الفيديو: {0}",
            "वीडियो नहीं बनाया जा सका: {0}", "ভিডিও তৈরি করা যায়নি: {0}",
            "ویڈیو نہیں بنائی جا سکی: {0}", "无法创建视频：{0}")
        Add("Видео создано, но не удалось удалить оригинал: {0}",
            "The video was created, but the original could not be deleted: {0}",
            "Відео створено, але не вдалося видалити оригінал: {0}",
            "Das Video wurde erstellt, das Original ließ sich aber nicht löschen: {0}",
            "Il video è stato creato, ma l'originale non è stato eliminato: {0}",
            "El vídeo se creó, pero no se pudo borrar el original: {0}",
            "La vidéo a été créée, mais l'original n'a pas pu être supprimé : {0}",
            "O vídeo foi criado, mas não foi possível excluir o original: {0}",
            "أُنشئ الفيديو، لكن تعذّر حذف الملف الأصلي: {0}",
            "वीडियो बन गया, लेकिन मूल फ़ाइल नहीं हटाई जा सकी: {0}",
            "ভিডিও তৈরি হয়েছে, কিন্তু মূল ফাইল মোছা যায়নি: {0}",
            "ویڈیو بن گئی، لیکن اصل فائل حذف نہیں ہو سکی: {0}",
            "视频已创建，但无法删除原文件：{0}")
        Add("Преобразование отменено",
            "The conversion was cancelled", "Перетворення скасовано",
            "Die Umwandlung wurde abgebrochen", "Conversione annullata",
            "Conversión cancelada", "Conversion annulée", "Conversão cancelada",
            "أُلغي التحويل", "रूपांतरण रद्द किया गया", "রূপান্তর বাতিল করা হয়েছে",
            "تبدیلی منسوخ کر دی گئی", "转换已取消")

        ' --- the suppressible-confirmation setting (§10.1) ----------------------------
        Add("Спрашивать перед заменой видео",
            "Ask before replacing with video", "Запитувати перед заміною відео",
            "Vor dem Ersetzen durch ein Video fragen", "Chiedi prima di sostituire con un video",
            "Preguntar antes de reemplazar por vídeo", "Demander avant le remplacement par une vidéo",
            "Perguntar antes de substituir por vídeo", "السؤال قبل الاستبدال بفيديو",
            "वीडियो से बदलने से पहले पूछें", "ভিডিও দিয়ে বদলানোর আগে জিজ্ঞাসা করুন",
            "ویڈیو سے بدلنے سے پہلے پوچھیں", "替换为视频前询问")
        Add("Оригинал удаляется безвозвратно, поэтому по умолчанию программа спрашивает.",
            "The original is deleted permanently, so the application asks first by default.",
            "Оригінал видаляється безповоротно, тому типово програма запитує.",
            "Das Original wird endgültig gelöscht, deshalb fragt das Programm standardmäßig nach.",
            "L'originale viene eliminato definitivamente, perciò per impostazione predefinita il programma chiede conferma.",
            "El original se borra de forma permanente, por eso el programa pregunta de manera predeterminada.",
            "L'original est supprimé définitivement, c'est pourquoi le programme demande confirmation par défaut.",
            "O original é excluído permanentemente, por isso o programa pergunta por padrão.",
            "يُحذف الملف الأصلي نهائيًا، لذلك يسأل البرنامج افتراضيًا.",
            "मूल फ़ाइल स्थायी रूप से हटती है, इसलिए प्रोग्राम डिफ़ॉल्ट रूप से पूछता है।",
            "মূল ফাইল স্থায়ীভাবে মুছে যায়, তাই প্রোগ্রাম ডিফল্টভাবে জিজ্ঞাসা করে।",
            "اصل فائل مستقل طور پر حذف ہوتی ہے، اس لیے پروگرام بطور طے شدہ پوچھتا ہے۔",
            "原文件会被永久删除，因此程序默认会先询问。")

    End Sub

End Class
