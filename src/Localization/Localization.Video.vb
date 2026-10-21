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
        Add("Сколько места занимают сохранённые результаты декодирования. Очистка не меняет сами изображения - они декодируются заново.",
            "How much room the stored decode results take. Clearing changes nothing in the images themselves - they are decoded again.",
            "Скільки місця займають збережені результати декодування. Очищення не змінює самі зображення - вони декодуються знову.",
            "Wie viel Raum die gespeicherten Dekodierergebnisse belegen. Leeren ändert nichts an den Bildern selbst - sie werden neu dekodiert.",
            "Quanto spazio occupano i risultati di decodifica salvati. Svuotare non cambia le immagini stesse - vengono decodificate di nuovo.",
            "Cuánto espacio ocupan los resultados de decodificado guardados. Vaciar no cambia las imágenes mismas - se vuelven a decodificar.",
            "Place occupée par les résultats de décodage enregistrés. Vider ne change rien aux images elles-mêmes - elles sont décodées à nouveau.",
            "Quanto espaço ocupam os resultados de decodificação salvos. Limpar não muda as próprias imagens - elas são decodificadas de novo.",
            "كم يشغل من المكان نتائج فك الترميز المحفوظة. المسح لا يغيّر الصور نفسها - تُفك من جديد.",
            "सहेजे गए डिकोड परिणाम कितनी जगह लेते हैं। साफ़ करने से चित्रों में कोई बदलाव नहीं होता - वे फिर से डिकोड होते हैं।",
            "সংরক্ষিত ডিকোড ফলাফল কত জায়গা নেয়। পরিষ্কার করলে ছবিগুলোর কিছুই বদলায় না - সেগুলো আবার ডিকোড হয়।",
            "محفوظ ڈی کوڈ نتائج کتنی جگہ لیتے ہیں۔ صاف کرنے سے تصاویر خود نہیں بدلتیں - وہ دوبارہ ڈی کوڈ ہوتی ہیں۔",
            "保存的解码结果占用多少空间。清空不会更改图像本身 - 它们会重新解码。")
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

        ' --- the playback extras of ticket 119 ----------------------------------------
        ' Status lines, the transport speed button and the A-B machine's answers.
        Add("Скорость: {0}",
            "Speed: {0}", "Швидкість: {0}", "Geschwindigkeit: {0}",
            "Velocità: {0}", "Velocidad: {0}", "Vitesse : {0}",
            "Velocidade: {0}", "السرعة: {0}", "गति: {0}",
            "গতি: {0}", "رفتار: {0}", "速度：{0}")
        Add("Скорость воспроизведения",
            "Playback speed", "Швидкість відтворення", "Wiedergabegeschwindigkeit",
            "Velocità di riproduzione", "Velocidad de reproducción", "Vitesse de lecture",
            "Velocidade de reprodução", "سرعة التشغيل", "प्लेबैक गति",
            "প্লেব্যাক গতি", "پلے بیک رفتار", "播放速度")
        Add("Скорость недоступна для этого файла",
            "This file does not support speed changes", "Швидкість недоступна для цього файлу", "Für diese Datei ist keine Geschwindigkeitsänderung möglich",
            "Velocità non disponibile per questo file", "La velocidad no está disponible para este archivo", "Vitesse non disponible pour ce fichier",
            "Velocidade indisponível para este ficheiro", "تغيير السرعة غير مدعوم لهذا الملف", "इस फ़ाइल के लिए गति उपलब्ध नहीं है",
            "এই ফাইলের জন্য গতি পরিবর্তন সম্ভব নয়", "اس فائل کے لیے رفتار دستیاب نہیں", "此文件不支持调整速度")
        Add("Обычная",
            "Normal", "Звичайна", "Normal",
            "Normale", "Normal", "Normale",
            "Normal", "عادي", "सामान्य",
            "স্বাভাবিক", "معمول", "正常")
        Add("Перемотка: {0} с",
            "Seek: {0} s", "Перемотка: {0} с", "Sprung: {0} s",
            "Salto: {0} s", "Salto: {0} s", "Saut : {0} s",
            "Salto: {0} s", "قفزة: {0} ث", "सीक: {0} से",
            "সিক: {0} সে", "سیک: {0} سی", "跳转：{0} 秒")
        Add("Нет воспроизводимого видео",
            "No video is playing", "Немає відео, що відтворюється", "Es wird kein Video abgespielt",
            "Nessun video in riproduzione", "No hay vídeo en reproducción", "Aucune vidéo en lecture",
            "Nenhum vídeo em reprodução", "لا يوجد فيديو قيد التشغيل", "कोई वीडियो नहीं चल रहा है",
            "কোনো ভিডিও চলছে না", "کوئی ویڈیو نہیں چل رہی", "没有正在播放的视频")
        Add("Метка A: {0}",
            "Mark A: {0}", "Мітка A: {0}", "Marke A: {0}",
            "Punto A: {0}", "Marca A: {0}", "Marque A : {0}",
            "Marca A: {0}", "العلامة A: {0}", "मार्क A: {0}",
            "মার্ক A: {0}", "مارک A: {0}", "标记 A：{0}")
        Add("Повтор A-B: {0} - {1}",
            "A-B repeat: {0} - {1}", "Повтор A-B: {0} - {1}", "A-B-Wiederholung: {0} - {1}",
            "Ripetizione A-B: {0} - {1}", "Repetición A-B: {0} - {1}", "Répétition A-B : {0} - {1}",
            "Repetição A-B: {0} - {1}", "تكرار A-B: {0} - {1}", "A-B रिपीट: {0} - {1}",
            "A-B রিপিট: {0} - {1}", "A-B ریپیٹ: {0} - {1}", "A-B 重复：{0} - {1}")
        Add("Повтор A-B выключен",
            "A-B repeat cleared", "Повтор A-B вимкнено", "A-B-Wiederholung ausgeschaltet",
            "Ripetizione A-B disattivata", "Repetición A-B desactivada", "Répétition A-B désactivée",
            "Repetição A-B desligada", "تم إيقاف تكرار A-B", "A-B रिपीट बंद",
            "A-B রিপিট বন্ধ", "A-B ریپیٹ بند", "A-B 重复已关闭")
        Add("Метка B слишком близко к A (нужна минимум 1 с)",
            "Mark B is too close to A (1 s minimum)", "Мітка B надто близько до A (потрібна мінімум 1 с)", "Marke B ist zu nahe an A (mindestens 1 s)",
            "Il punto B è troppo vicino ad A (minimo 1 s)", "La marca B está demasiado cerca de A (mínimo 1 s)", "La marque B est trop proche de A (1 s minimum)",
            "A marca B está demasiado perto de A (mínimo 1 s)", "العلامة B قريبة جدًا من A (يلزم ثانية واحدة على الأقل)", "मार्क B, A के बहुत करीब है (न्यूनतम 1 से)",
            "মার্ক B, A-এর খুব কাছে (কমপক্ষে 1 সেকেন্ড দরকার)", "مارک B، A کے بہت قریب ہے (کم از کم 1 سیکنڈ درکار)", "标记 B 距 A 过近（至少需要 1 秒）")
        Add("Метка B слишком близко к концу",
            "Mark B is too close to the end", "Мітка B надто близько до кінця", "Marke B ist zu nah am Ende",
            "Il punto B è troppo vicino alla fine", "La marca B está demasiado cerca del final", "La marque B est trop proche de la fin",
            "A marca B está demasiado perto do fim", "العلامة B قريبة جدًا من النهاية", "मार्क B अंत से बहुत करीब है",
            "মার্ক B শেষের খুব কাছে", "مارک B اختتام کے بہت قریب ہے", "标记 B 距结尾过近")
        Add("Повтор A-B требует файл с известной длиной",
            "A-B repeat needs a file with a known length", "Повтор A-B потребує файл з відомою тривалістю", "A-B-Wiederholung benötigt eine Datei mit bekannter Länge",
            "La ripetizione A-B richiede un file di durata nota", "La repetición A-B necesita un archivo de duración conocida", "La répétition A-B exige un fichier de durée connue",
            "A repetição A-B precisa de um ficheiro com duração conhecida", "يتطلب تكرار A-B ملفًا بمدة معروفة", "A-B रिपीट को ज्ञात अवधि वाली फ़ाइल चाहिए",
            "A-B রিপিটের জন্য পরিচিত দৈর্ঘ্যের ফাইল দরকার", "A-B ریپیٹ کو معلوم دورانیے کی فائل درکار ہے", "A-B 重复需要时长已知的文件")
        Add("На паузе. Следующий кадр - ещё раз эту клавишу",
            "Paused. Press again for the next frame", "На паузі. Наступний кадр - ще раз цю клавішу", "Angehalten. Für das nächste Bild dieselbe Taste erneut drücken",
            "In pausa. Premere di nuovo per il fotogramma successivo", "En pausa. Pulse de nuevo para el fotograma siguiente", "En pause. Appuyez à nouveau pour l'image suivante",
            "Em pausa. Prima outra vez para o fotograma seguinte", "متوقف مؤقتًا. اضغط المفتاح نفسه مرة أخرى للإطار التالي", "रुका हुआ। अगले फ़्रेम के लिए फिर से यही कुंजी दबाएँ",
            "বিরত। পরের ফ্রেমের জন্য আবার একই কী চাপুন", "روکا ہوا۔ اگلے فریم کے لیے دوبارہ یہی کلید دبائیں", "已暂停。再按一次该键查看下一帧")
        Add("Следующий кадр",
            "Next frame", "Наступний кадр", "Nächstes Bild",
            "Fotogramma successivo", "Fotograma siguiente", "Image suivante",
            "Fotograma seguinte", "الإطار التالي", "अगला फ़्रेम",
            "পরের ফ্রেম", "اگلا فریم", "下一帧")
        Add("Следующий кадр (назад на кадр VLC не умеет)",
            "Next frame (VLC cannot step back)", "Наступний кадр (назад на кадр VLC не вміє)", "Nächstes Bild (zurück kann VLC nicht)",
            "Fotogramma successivo (VLC non può tornare indietro)", "Fotograma siguiente (VLC no sabe retroceder)", "Image suivante (VLC ne sait pas revenir en arrière)",
            "Fotograma seguinte (o VLC não consegue recuar)", "الإطار التالي (لا يمكن لـ VLC التراجع إطارًا)", "अगला फ़्रेम (VLC पीछे नहीं जा सकता)",
            "পরের ফ্রেম (VLC পিছনে যেতে পারে না)", "اگلا فریم (VLC پیچھے نہیں جا سکتا)", "下一帧（VLC 无法后退一帧）")
        Add("Повтор A-B: отметить A",
            "A-B repeat: mark A", "Повтор A-B: позначити A", "A-B-Wiederholung: A markieren",
            "Ripetizione A-B: segna A", "Repetición A-B: marcar A", "Répétition A-B : marquer A",
            "Repetição A-B: marcar A", "تكرار A-B: تحديد A", "A-B रिपीट: A चिह्नित करें",
            "A-B রিপিট: A চিহ্নিত করুন", "A-B ریپیٹ: A نشان زد کریں", "A-B 重复：标记 A")
        Add("Повтор A-B: отметить B",
            "A-B repeat: mark B", "Повтор A-B: позначити B", "A-B-Wiederholung: B markieren",
            "Ripetizione A-B: segna B", "Repetición A-B: marcar B", "Répétition A-B : marquer B",
            "Repetição A-B: marcar B", "تكرار A-B: تحديد B", "A-B रिपीट: B चिह्नित करें",
            "A-B রিপিট: B চিহ্নিত করুন", "A-B ریپیٹ: B نشان زد کریں", "A-B 重复：标记 B")
        Add("Повтор A-B: сбросить",
            "A-B repeat: clear", "Повтор A-B: скинути", "A-B-Wiederholung: zurücksetzen",
            "Ripetizione A-B: azzera", "Repetición A-B: borrar", "Répétition A-B : effacer",
            "Repetição A-B: limpar", "تكرار A-B: مسح", "A-B रिपीट: साफ़ करें",
            "A-B রিপিট: মুছুন", "A-B ریپیٹ: صاف کریں", "A-B 重复：清除")

        ' The hotkey catalogue captions of the eleven playback actions (119 §5.0.2) -
        ' the shortcuts dialog shows them through Localization.T(action.Caption).
        Add("Медленнее",
            "Slower", "Повільніше", "Langsamer",
            "Più lento", "Más lento", "Plus lent",
            "Mais lento", "أبطأ", "धीमा",
            "ধীর", "سست", "减速")
        Add("Быстрее",
            "Faster", "Швидше", "Schneller",
            "Più veloce", "Más rápido", "Plus rapide",
            "Mais rápido", "أسرع", "तेज़",
            "দ্রুত", "تیز", "加速")
        Add("Нормальная скорость",
            "Normal speed", "Звичайна швидкість", "Normale Geschwindigkeit",
            "Velocità normale", "Velocidad normal", "Vitesse normale",
            "Velocidade normal", "السرعة العادية", "सामान्य गति",
            "স্বাভাবিক গতি", "معمول رفتار", "正常速度")
        Add("Назад на короткий шаг",
            "Back one short step", "Назад на короткий крок", "Kurzer Rücksprung",
            "Indietro di un passo breve", "Atrás un paso corto", "Recul d'un pas court",
            "Recuar um passo curto", "خطوة قصيرة إلى الخلف", "एक छोटा कदम पीछे",
            "একটি ছোট ধাপ পিছনে", "ایک مختصر قدم پیچھے", "后退一小步")
        Add("Вперёд на короткий шаг",
            "Forward one short step", "Вперед на короткий крок", "Kurzer Vorsprung",
            "Avanti di un passo breve", "Adelante un paso corto", "Avance d'un pas court",
            "Avançar um passo curto", "خطوة قصيرة إلى الأمام", "एक छोटा कदम आगे",
            "একটি ছোট ধাপ সামনে", "ایک مختصر قدم آگے", "前进一小步")
        Add("Назад на средний шаг",
            "Back one medium step", "Назад на середній крок", "Mittlerer Rücksprung",
            "Indietro di un passo medio", "Atrás un paso medio", "Recul d'un pas moyen",
            "Recuar um passo médio", "خطوة متوسطة إلى الخلف", "एक मध्यम कदम पीछे",
            "একটি মাঝারি ধাপ পিছনে", "درمیانہ قدم پیچھے", "后退中步")
        Add("Вперёд на средний шаг",
            "Forward one medium step", "Вперед на середній крок", "Mittlerer Vorsprung",
            "Avanti di un passo medio", "Adelante un paso medio", "Avance d'un pas moyen",
            "Avançar um passo médio", "خطوة متوسطة إلى الأمام", "एक मध्यम कदम आगे",
            "একটি মাঝারি ধাপ সামনে", "درمیانہ قدم آگے", "前进中步")
        Add("Назад на длинный шаг",
            "Back one long step", "Назад на довгий крок", "Langer Rücksprung",
            "Indietro di un passo lungo", "Atrás un paso largo", "Recul d'un pas long",
            "Recuar um passo longo", "خطوة طويلة إلى الخلف", "एक लंबा कदम पीछे",
            "একটি দীর্ঘ ধাপ পিছনে", "طویل قدم پیچھے", "后退一大步")
        Add("Вперёд на длинный шаг",
            "Forward one long step", "Вперед на довгий крок", "Langer Vorsprung",
            "Avanti di un passo lungo", "Adelante un paso largo", "Avance d'un pas long",
            "Avançar um passo longo", "خطوة طويلة إلى الأمام", "एक लंबा कदम आगे",
            "একটি দীর্ঘ ধাপ সামনে", "طویل قدم آگے", "前进一大步")
        Add("Повтор A-B",
            "A-B repeat", "Повтор A-B", "A-B-Wiederholung",
            "Ripetizione A-B", "Repetición A-B", "Répétition A-B",
            "Repetição A-B", "تكرار A-B", "A-B रिपीट",
            "A-B রিপিট", "A-B ریپیٹ", "A-B 重复")

        ' --- ticket 123 Ф-3: the video basics and the rebindable track / copy keys ----------
        ' Catalogue captions ("Пауза / продолжить" and "Звук вкл/выкл" already exist in the
        ' tables of the click-on-video choices and the transport bar) ...
        Add("Громче",
            "Volume up", "Гучніше", "Lauter",
            "Alza il volume", "Subir volumen", "Augmenter le volume",
            "Aumentar o volume", "رفع الصوت", "आवाज़ बढ़ाएँ",
            "আওয়াজ বাড়ান", "آواز بڑھائیں", "调高音量")
        Add("Тише",
            "Volume down", "Тихіше", "Leiser",
            "Abbassa il volume", "Bajar volumen", "Baisser le volume",
            "Diminuir o volume", "خفض الصوت", "आवाज़ घटाएँ",
            "আওয়াজ কমান", "آواز کم کریں", "调低音量")
        Add("Следующая звуковая дорожка",
            "Next audio track", "Наступна звукова доріжка", "Nächste Tonspur",
            "Traccia audio successiva", "Pista de audio siguiente", "Piste audio suivante",
            "Faixa de áudio seguinte", "المسار الصوتي التالي", "अगला ऑडियो ट्रैक",
            "পরবর্তী অডিও ট্র্যাক", "اگلا آڈیو ٹریک", "下一音轨")
        Add("Следующие субтитры",
            "Next subtitles", "Наступні субтитри", "Nächste Untertitel",
            "Sottotitoli successivi", "Subtítulos siguientes", "Sous-titres suivants",
            "Legendas seguintes", "الترجمة التالية", "अगले उपशीर्षक",
            "পরবর্তী সাবটাইটেল", "اگلے سب ٹائٹل", "下一字幕")
        Add("Копировать в папку {0}",
            "Copy to folder {0}", "Копіювати до теки {0}", "In Ordner {0} kopieren",
            "Copia nella cartella {0}", "Copiar a la carpeta {0}", "Copier vers le dossier {0}",
            "Copiar para a pasta {0}", "النسخ إلى المجلد {0}", "फ़ोल्डर {0} में कॉपी करें",
            "ফোল্ডার {0}-এ কপি করুন", "فولڈر {0} میں کاپی کریں", "复制到文件夹 {0}")
        Add("Меню файла - ещё одна клавиша (Menu и Shift+F10 работают всегда)",
            "File menu - an extra key (Menu and Shift+F10 always work)", "Меню файлу - ще одна клавіша (Menu і Shift+F10 працюють завжди)", "Dateimenü - zusätzliche Taste (Menütaste und Shift+F10 funktionieren immer)",
            "Menu del file - un tasto aggiuntivo (Menu e Shift+F10 funzionano sempre)", "Menú del archivo - una tecla adicional (Menú y Shift+F10 funcionan siempre)", "Menu du fichier - une touche supplémentaire (Menu et Shift+F10 fonctionnent toujours)",
            "Menu do ficheiro - uma tecla adicional (Menu e Shift+F10 funcionam sempre)", "قائمة الملف - مفتاح إضافي (مفتاح القائمة وShift+F10 يعملان دائمًا)", "फ़ाइल मेनू - एक अतिरिक्त कुंजी (Menu और Shift+F10 हमेशा काम करते हैं)",
            "ফাইল মেনু - একটি অতিরিক্ত কী (Menu এবং Shift+F10 সবসময় কাজ করে)", "فائل مینو - ایک اضافی کلید (Menu اور Shift+F10 ہمیشہ کام کرتے ہیں)", "文件菜单 - 额外的按键（Menu 键和 Shift+F10 始终有效）")
        Add("Режим клавиатуры панелей: стрелки и Tab - выбор, Enter - нажать, Esc - выйти",
            "Panel keyboard mode: arrows and Tab choose, Enter presses, Esc leaves", "Режим клавіатури панелей: стрілки та Tab - вибір, Enter - натиснути, Esc - вийти", "Tastaturmodus der Leisten: Pfeile und Tab wählen, Enter drückt, Esc beendet",
            "Modalità tastiera dei pannelli: frecce e Tab scelgono, Invio preme, Esc esce", "Modo de teclado de los paneles: flechas y Tab eligen, Intro pulsa, Esc sale", "Mode clavier des panneaux : flèches et Tab choisissent, Entrée active, Échap quitte",
            "Modo de teclado dos painéis: setas e Tab escolhem, Enter prime, Esc sai", "وضع لوحة المفاتيح للّوحات: الأسهم وTab للاختيار وEnter للضغط وEsc للخروج", "पैनल कीबोर्ड मोड: तीर और Tab से चुनें, Enter से दबाएँ, Esc से बाहर निकलें",
            "প্যানেল কীবোর্ড মোড: তীর ও Tab দিয়ে বাছুন, Enter চাপুন, Esc দিয়ে বেরোন", "پینل کی بورڈ موڈ: تیر اور Tab سے منتخب کریں، Enter دبائیں، Esc سے باہر نکلیں", "面板键盘模式：方向键和 Tab 选择，Enter 按下，Esc 退出")
        ' ... and the status lines the mute and volume keys answer with.
        Add("Звук выключен",
            "Sound off", "Звук вимкнено", "Ton aus",
            "Audio disattivato", "Sonido desactivado", "Son coupé",
            "Som desativado", "الصوت مكتوم", "ध्वनि बंद",
            "শব্দ বন্ধ", "آواز بند", "已静音")
        Add("Звук включён",
            "Sound on", "Звук увімкнено", "Ton ein",
            "Audio attivato", "Sonido activado", "Son rétabli",
            "Som ativado", "تم تشغيل الصوت", "ध्वनि चालू",
            "শব্দ চালু", "آواز آن", "已取消静音")
        Add("Громкость: {0} %",
            "Volume: {0} %", "Гучність: {0} %", "Lautstärke: {0} %",
            "Volume: {0} %", "Volumen: {0} %", "Volume : {0} %",
            "Volume: {0} %", "مستوى الصوت: {0} %", "वॉल्यूम: {0} %",
            "ভলিউম: {0} %", "والیوم: {0} %", "音量：{0} %")

        ' --- ticket 121 section 7: the transport bar's "more" button, the clock, opening and buffering ----------
        Add("Ещё",
            "More", "Ще", "Mehr",
            "Altro", "Más", "Plus",
            "Mais", "المزيد", "और",
            "আরও", "مزید", "更多")
        Add("Скорость, дорожки, повтор и другие операции с файлом",
            "Speed, tracks, repeat and other operations on the file", "Швидкість, доріжки, повтор та інші операції з файлом", "Geschwindigkeit, Spuren, Wiederholung und weitere Aktionen für die Datei",
            "Velocità, tracce, ripetizione e altre operazioni sul file", "Velocidad, pistas, repetición y otras operaciones con el archivo", "Vitesse, pistes, répétition et autres opérations sur le fichier",
            "Velocidade, faixas, repetição e outras operações com o ficheiro", "السرعة والمسارات والتكرار وعمليات أخرى على الملف", "गति, ट्रैक, दोहराव और फ़ाइल पर अन्य कार्य",
            "গতি, ট্র্যাক, পুনরাবৃত্তি এবং ফাইলের অন্যান্য কাজ", "رفتار، ٹریک، تکرار اور فائل پر دیگر کام", "速度、音轨、重复及对文件的其他操作")
        Add("Нажмите, чтобы показать оставшееся время",
            "Click to show the remaining time", "Натисніть, щоб показати час, що залишився", "Klicken, um die Restzeit anzuzeigen",
            "Fai clic per mostrare il tempo rimanente", "Haga clic para mostrar el tiempo restante", "Cliquez pour afficher le temps restant",
            "Clique para mostrar o tempo restante", "انقر لإظهار الوقت المتبقي", "शेष समय दिखाने के लिए क्लिक करें",
            "অবশিষ্ট সময় দেখাতে ক্লিক করুন", "باقی وقت دکھانے کے لیے کلک کریں", "点击显示剩余时间")
        Add("Время",
            "Time", "Час", "Zeit",
            "Tempo", "Tiempo", "Durée",
            "Tempo", "الوقت", "समय",
            "সময়", "وقت", "时间")
        Add("Открываю: {0}..",
            "Opening: {0}..", "Відкриваю: {0}..", "Öffne: {0}..",
            "Apertura: {0}..", "Abriendo: {0}..", "Ouverture : {0}..",
            "A abrir: {0}..", "جارٍ فتح: {0}..", "खोल रहे हैं: {0}..",
            "খোলা হচ্ছে: {0}..", "کھول رہے ہیں: {0}..", "正在打开：{0}..")
        Add("Буферизация: {0} %",
            "Buffering: {0} %", "Буферизація: {0} %", "Pufferung: {0} %",
            "Buffering: {0} %", "Almacenando en búfer: {0} %", "Mise en mémoire tampon : {0} %",
            "A armazenar em buffer: {0} %", "جارٍ التخزين المؤقت: {0} %", "बफ़र हो रहा है: {0} %",
            "বাফার হচ্ছে: {0} %", "بفرنگ: {0} %", "正在缓冲：{0} %")
        Add("Воспроизводится: {0}",
            "Playing: {0}", "Відтворюється: {0}", "Wiedergabe: {0}",
            "In riproduzione: {0}", "Reproduciendo: {0}", "Lecture : {0}",
            "A reproduzir: {0}", "قيد التشغيل: {0}", "चल रहा है: {0}",
            "চলছে: {0}", "چل رہا ہے: {0}", "正在播放：{0}")
        Add("Готово, на паузе: {0}",
            "Ready, paused: {0}", "Готово, на паузі: {0}", "Bereit, pausiert: {0}",
            "Pronto, in pausa: {0}", "Listo, en pausa: {0}", "Prêt, en pause : {0}",
            "Pronto, em pausa: {0}", "جاهز، متوقف مؤقتاً: {0}", "तैयार, रुका हुआ: {0}",
            "প্রস্তুত, বিরতিতে: {0}", "تیار، رکا ہوا: {0}", "已就绪，已暂停：{0}")

        ' --- spec 242: animated graphics through a temporary video, the Settings rows (rules 2, 30) ----
        ' The two FFmpeg state texts start with a Latin word, so the Arabic and Urdu ones carry a leading
        ' right-to-left mark, as the Ollama status texts in Localization.Ocr.vb do.
        Add("Анимированная графика через видео",
            "Animated graphics through video", "Анімована графіка через відео", "Animierte Grafik als Video",
            "Grafica animata tramite video", "Gráficos animados mediante vídeo", "Graphiques animés via la vidéo",
            "Gráficos animados via vídeo", "الرسوم المتحركة عبر الفيديو", "एनिमेटेड ग्राफ़िक्स वीडियो के रूप में",
            "অ্যানিমেটেড গ্রাফিক্স ভিডিওর মাধ্যমে", "اینیمیٹڈ گرافکس بذریعہ ویڈیو", "通过视频播放动画图形")
        Add("Длинная анимированная картинка проигрывается как видео - без масштаба, панорамы, OCR и редактора; короткие анимации показываются как раньше.",
            "A long animated picture plays as a video - without zoom, pan, OCR and the editor; short animations are shown as before.",
            "Довга анімована картинка відтворюється як відео - без масштабу, панорами, OCR і редактора; короткі анімації показуються як раніше.",
            "Ein langes animiertes Bild wird als Video abgespielt - ohne Zoom, Verschieben, OCR und Editor; kurze Animationen werden wie bisher gezeigt.",
            "Una lunga immagine animata viene riprodotta come video - senza zoom, spostamento, OCR ed editor; le animazioni brevi restano come prima.",
            "Una imagen animada larga se reproduce como vídeo - sin zoom, desplazamiento, OCR ni editor; las animaciones cortas se muestran como antes.",
            "Une longue image animée est lue comme une vidéo - sans zoom, déplacement, OCR ni éditeur ; les animations courtes restent affichées comme avant.",
            "Uma imagem animada longa é reproduzida como vídeo - sem zoom, deslocamento, OCR nem editor; as animações curtas são exibidas como antes.",
            "تُشغَّل الصورة المتحركة الطويلة كفيديو - دون تكبير أو تحريك أو OCR أو محرر؛ وتُعرض الصور المتحركة القصيرة كما كانت.",
            "लंबी एनिमेटेड तस्वीर वीडियो की तरह चलती है - ज़ूम, खिसकाने, OCR और संपादक के बिना; छोटे एनिमेशन पहले की तरह दिखते हैं।",
            "দীর্ঘ অ্যানিমেটেড ছবি ভিডিও হিসেবে চলে - জুম, সরানো, OCR ও সম্পাদক ছাড়া; ছোট অ্যানিমেশন আগের মতোই দেখানো হয়।",
            "لمبی اینیمیٹڈ تصویر ویڈیو کی طرح چلتی ہے - زوم، کھسکانے، OCR اور ایڈیٹر کے بغیر؛ چھوٹی اینیمیشنز پہلے کی طرح دکھائی جاتی ہیں۔",
            "较长的动画图片会作为视频播放，不支持缩放、平移、OCR 和编辑器；较短的动画仍按原样显示。")
        Add("FFmpeg: установлен",
            "FFmpeg: installed", "FFmpeg: встановлено", "FFmpeg: installiert",
            "FFmpeg: installato", "FFmpeg: instalado", "FFmpeg : installé",
            "FFmpeg: instalado", "‏FFmpeg: مثبت", "FFmpeg: इंस्टॉल है",
            "FFmpeg: ইনস্টল করা আছে", "‏FFmpeg: انسٹال ہے", "FFmpeg：已安装")
        Add("FFmpeg: не установлен",
            "FFmpeg: not installed", "FFmpeg: не встановлено", "FFmpeg: nicht installiert",
            "FFmpeg: non installato", "FFmpeg: no instalado", "FFmpeg : non installé",
            "FFmpeg: não instalado", "‏FFmpeg: غير مثبت", "FFmpeg: इंस्टॉल नहीं है",
            "FFmpeg: ইনস্টল করা নেই", "‏FFmpeg: انسٹال نہیں ہے", "FFmpeg：未安装")
        Add("Загрузить..",
            "Download..", "Завантажити..", "Herunterladen..",
            "Scarica..", "Descargar..", "Télécharger..",
            "Baixar..", "تنزيل..", "डाउनलोड करें..",
            "ডাউনলোড করুন..", "ڈاؤن لوڈ کریں..", "下载..")
        Add("Видео готовится..",
            "Preparing video..", "Відео готується..", "Video wird vorbereitet..",
            "Preparazione del video..", "Preparando el vídeo..", "Préparation de la vidéo..",
            "A preparar o vídeo..", "جارٍ إعداد الفيديو..", "वीडियो तैयार हो रहा है..",
            "ভিডিও প্রস্তুত হচ্ছে..", "ویڈیو تیار ہو رہی ہے..", "正在准备视频..")
        Add("Видео готовится.. {0} %",
            "Preparing video.. {0} %", "Відео готується.. {0} %", "Video wird vorbereitet.. {0} %",
            "Preparazione del video.. {0} %", "Preparando el vídeo.. {0} %", "Préparation de la vidéo.. {0} %",
            "A preparar o vídeo.. {0} %", "جارٍ إعداد الفيديو.. {0} %", "वीडियो तैयार हो रहा है.. {0} %",
            "ভিডিও প্রস্তুত হচ্ছে.. {0} %", "ویڈیو تیار ہو رہی ہے.. {0} %", "正在准备视频.. {0} %")
        Add("Анимация показана без видео: {0}",
            "Animation shown without video: {0}", "Анімацію показано без відео: {0}", "Animation ohne Video angezeigt: {0}",
            "Animazione mostrata senza video: {0}", "Animación mostrada sin vídeo: {0}", "Animation affichée sans vidéo : {0}",
            "Animação exibida sem vídeo: {0}", "عُرضت الرسوم المتحركة بدون فيديو: {0}", "एनिमेशन बिना वीडियो के दिखाया गया: {0}",
            "ভিডিও ছাড়া অ্যানিমেশন দেখানো হয়েছে: {0}", "اینیمیشن ویڈیو کے بغیر دکھائی گئی: {0}", "动画已在没有视频的情况下显示：{0}")

    End Sub

End Class
