Option Strict On

' <summary>
' Strings of the image editor (SPECIFICATION_IMAGE_EDITOR_DOTNET10.md): its toolbar
' button, its menu entry, the editor window itself and everything the save path can
' have to say. Modern-only surfaces, but the table is shared - a string table costs
' the x86 build nothing, and splitting it per build is how two tables drift apart.
'
' The refusal reasons are separate keys fed into one framed sentence through {0}
' rather than five long sentences: the frame carries the advice ("Save as.." is
' still available) and the reason is a phrase after a colon, which is a position
' every one of the thirteen languages can take a noun phrase in.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddEditorStrings()

        ' --- entry point: toolbar button + middle-click menu --------------------
        Add("Правка изображения",
            "Edit image", "Редагування зображення", "Bild bearbeiten", "Modifica immagine",
            "Editar imagen", "Modifier l'image", "Editar imagem", "تحرير الصورة",
            "छवि संपादन", "ছবি সম্পাদনা", "تصویر میں ترمیم", "编辑图片")
        Add("Правка",
            "Edit", "Редагувати", "Bearbeiten", "Modifica", "Editar", "Modifier", "Editar",
            "تحرير", "संपादन", "সম্পাদনা", "ترمیم", "编辑")
        Add("Открыть изображение в редакторе",
            "Open the picture in the editor", "Відкрити зображення в редакторі",
            "Bild im Editor öffnen", "Apri l'immagine nell'editor",
            "Abrir la imagen en el editor", "Ouvrir l'image dans l'éditeur",
            "Abrir a imagem no editor", "فتح الصورة في المحرّر",
            "चित्र को संपादक में खोलें", "ছবিটি সম্পাদকে খুলুন",
            "تصویر کو ایڈیٹر میں کھولیں", "在编辑器中打开图片")
        Add("Редактировать..",
            "Edit..", "Редагувати..", "Bearbeiten..", "Modifica..", "Editar..", "Modifier..",
            "Editar..", "تحرير..", "संपादित करें..", "সম্পাদনা করুন..", "ترمیم کریں..", "编辑..")

        ' --- the editor window --------------------------------------------------
        Add("Правка: {0}",
            "Editing: {0}", "Редагування: {0}", "Bearbeiten: {0}", "Modifica: {0}",
            "Editando: {0}", "Modification : {0}", "Editando: {0}", "تحرير: {0}",
            "संपादन: {0}", "সম্পাদনা: {0}", "ترمیم: {0}", "编辑：{0}")
        Add("Сохранить",
            "Save", "Зберегти", "Speichern", "Salva", "Guardar", "Enregistrer", "Salvar",
            "حفظ", "सहेजें", "সংরক্ষণ", "محفوظ کریں", "保存")
        Add("Сохранить как..",
            "Save as..", "Зберегти як..", "Speichern unter..", "Salva con nome..",
            "Guardar como..", "Enregistrer sous..", "Salvar como..", "حفظ باسم..",
            "इस रूप में सहेजें..", "এভাবে সংরক্ষণ করুন..", "بطور محفوظ کریں..", "另存为..")
        Add("Закрыть",
            "Close", "Закрити", "Schließen", "Chiudi", "Cerrar", "Fermer", "Fechar",
            "إغلاق", "बंद करें", "বন্ধ করুন", "بند کریں", "关闭")
        Add("Не удалось открыть изображение для правки",
            "Could not open the image for editing", "Не вдалося відкрити зображення для редагування",
            "Das Bild konnte nicht zum Bearbeiten geöffnet werden",
            "Impossibile aprire l'immagine per la modifica",
            "No se pudo abrir la imagen para editarla",
            "Impossible d'ouvrir l'image pour la modifier",
            "Não foi possível abrir a imagem para edição", "تعذّر فتح الصورة للتحرير",
            "छवि को संपादन के लिए नहीं खोला जा सका", "ছবিটি সম্পাদনার জন্য খোলা যায়নি",
            "تصویر ترمیم کے لیے نہیں کھل سکی", "无法打开该图片进行编辑")

        ' --- the tools (§6, §7) -------------------------------------------------
        '
        ' Tool names are also their AccessibleName: the buttons draw their icon instead of
        ' carrying a caption, so without this a screen reader would announce five
        ' identically nameless buttons.
        Add("Кисть",
            "Brush", "Пензель", "Pinsel", "Pennello", "Pincel", "Pinceau", "Pincel",
            "فرشاة", "ब्रश", "ব্রাশ", "برش", "画笔")
        Add("Прямоугольник",
            "Rectangle", "Прямокутник", "Rechteck", "Rettangolo", "Rectángulo",
            "Rectangle", "Retângulo", "مستطيل", "आयत", "আয়ত", "مستطیل", "矩形")
        Add("Залитый прямоугольник",
            "Filled rectangle", "Залитий прямокутник", "Gefülltes Rechteck",
            "Rettangolo pieno", "Rectángulo relleno", "Rectangle plein",
            "Retângulo preenchido", "مستطيل ممتلئ", "भरा आयत", "ভরাট আয়ত",
            "بھرا مستطیل", "实心矩形")
        Add("Овал",
            "Ellipse", "Овал", "Ellipse", "Ovale", "Óvalo", "Ellipse", "Oval",
            "بيضاوي", "अंडाकार", "উপবৃত্ত", "بیضوی", "椭圆")
        Add("Залитый овал",
            "Filled ellipse", "Залитий овал", "Gefüllte Ellipse", "Ovale pieno",
            "Óvalo relleno", "Ellipse pleine", "Oval preenchido", "بيضاوي ممتلئ",
            "भरा अंडाकार", "ভরাট উপবৃত্ত", "بھرا بیضوی", "实心椭圆")
        ' Holding Shift is the one thing about a shape tool nobody discovers by trying.
        Add("{0} (с Shift - квадрат или круг)",
            "{0} (with Shift - a square or a circle)", "{0} (з Shift - квадрат або коло)",
            "{0} (mit Shift - Quadrat oder Kreis)", "{0} (con Maiusc - quadrato o cerchio)",
            "{0} (con Mayús - cuadrado o círculo)", "{0} (avec Maj - carré ou cercle)",
            "{0} (com Shift - quadrado ou círculo)", "{0} (مع Shift - مربع أو دائرة)",
            "{0} (Shift के साथ - वर्ग या वृत्त)", "{0} (Shift সহ - বর্গ বা বৃত্ত)",
            "{0} (Shift کے ساتھ - مربع یا دائرہ)", "{0}（按住 Shift - 正方形或圆形）")
        ' --- crop (Ф-4, §6.1) ---------------------------------------------------
        '
        ' The frame is live until it is applied, so its two keys have to be said somewhere:
        ' nothing about a rectangle on screen suggests that Enter cuts and Esc dismisses.
        Add("Обрезка",
            "Crop", "Обрізання", "Zuschneiden", "Ritaglio", "Recortar", "Rogner",
            "Recortar", "اقتصاص", "काट-छाँट", "ছাঁটাই", "کٹائی", "裁剪")
        Add("Обрезка (Enter - применить, Esc - снять рамку)",
            "Crop (Enter applies it, Esc drops the frame)",
            "Обрізання (Enter - застосувати, Esc - зняти рамку)",
            "Zuschneiden (Enter wendet an, Esc verwirft den Rahmen)",
            "Ritaglio (Invio applica, Esc rimuove il riquadro)",
            "Recortar (Entrar aplica, Esc quita el marco)",
            "Rogner (Entrée applique, Échap retire le cadre)",
            "Recortar (Enter aplica, Esc remove a moldura)",
            "اقتصاص (Enter للتطبيق، Esc لإزالة الإطار)",
            "काट-छाँट (Enter लागू करता है, Esc फ़्रेम हटाता है)",
            "ছাঁটাই (Enter প্রয়োগ করে, Esc ফ্রেম সরায়)",
            "کٹائی (Enter لاگو کرے، Esc فریم ہٹائے)",
            "裁剪（Enter 应用，Esc 取消选框）")
        Add("Применить обрезку",
            "Apply crop", "Застосувати обрізання", "Zuschnitt anwenden", "Applica ritaglio",
            "Aplicar recorte", "Appliquer le rognage", "Aplicar recorte", "تطبيق الاقتصاص",
            "काट-छाँट लागू करें", "ছাঁটাই প্রয়োগ করুন", "کٹائی لاگو کریں", "应用裁剪")
        Add("обрезка: {0} × {1}",
            "crop: {0} × {1}", "обрізання: {0} × {1}", "Zuschnitt: {0} × {1}",
            "ritaglio: {0} × {1}", "recorte: {0} × {1}", "rognage : {0} × {1}",
            "recorte: {0} × {1}", "الاقتصاص: {0} × {1}", "काट-छाँट: {0} × {1}",
            "ছাঁটাই: {0} × {1}", "کٹائی: {0} × {1}", "裁剪：{0} × {1}")
        Add("Не удалось обрезать: {0}",
            "Could not crop: {0}", "Не вдалося обрізати: {0}",
            "Zuschneiden fehlgeschlagen: {0}", "Impossibile ritagliare: {0}",
            "No se pudo recortar: {0}", "Impossible de rogner : {0}",
            "Não foi possível recortar: {0}", "تعذّر الاقتصاص: {0}",
            "काट-छाँट नहीं हो सकी: {0}", "ছাঁটাই করা যায়নি: {0}",
            "کٹائی نہیں ہو سکی: {0}", "无法裁剪：{0}")

        Add("Выбрать цвет",
            "Choose a colour", "Вибрати колір", "Farbe wählen", "Scegli il colore",
            "Elegir color", "Choisir la couleur", "Escolher a cor", "اختيار اللون",
            "रंग चुनें", "রঙ বেছে নিন", "رنگ منتخب کریں", "选择颜色")
        Add("Быстрый выбор цвета",
            "Quick colour pick", "Швидкий вибір кольору", "Schnelle Farbwahl",
            "Scelta rapida del colore", "Selección rápida de color",
            "Choix rapide de couleur", "Escolha rápida de cor", "اختيار سريع للون",
            "त्वरित रंग चयन", "দ্রুত রঙ নির্বাচন", "رنگ کا فوری انتخاب", "快速选色")
        Add("Толщина:",
            "Thickness:", "Товщина:", "Stärke:", "Spessore:", "Grosor:", "Épaisseur :",
            "Espessura:", "السماكة:", "मोटाई:", "পুরুত্ব:", "موٹائی:", "粗细：")
        ' In IMAGE pixels, not screen ones - the canvas is fitted, so the two differ by a
        ' factor of five on a 24-megapixel photo and the setting would otherwise puzzle.
        Add("Толщина линии в пикселях картинки",
            "Line thickness in image pixels", "Товщина лінії в пікселях зображення",
            "Linienstärke in Bildpixeln", "Spessore della linea in pixel dell'immagine",
            "Grosor de línea en píxeles de la imagen",
            "Épaisseur du trait en pixels de l'image",
            "Espessura da linha em pixels da imagem", "سماكة الخط بوحدات بكسل الصورة",
            "छवि के पिक्सेल में रेखा की मोटाई", "ছবির পিক্সেলে রেখার পুরুত্ব",
            "تصویر کے پکسل میں لکیر کی موٹائی", "线条粗细（以图片像素计）")
        Add("Отменить",
            "Undo", "Скасувати", "Rückgängig", "Annulla", "Deshacer", "Annuler",
            "Desfazer", "تراجع", "पूर्ववत करें", "পূর্বাবস্থা", "واپس", "撤销")
        Add("изменено",
            "modified", "змінено", "geändert", "modificato", "modificado", "modifié",
            "modificado", "مُعدَّل", "बदला गया", "পরিবর্তিত", "تبدیل شدہ", "已修改")
        ' Asked even when "no confirmations" is on (§9.7): that setting is about file
        ' operations, this is about work that exists nowhere but in this window.
        Add("Правки не сохранены. Закрыть?",
            "The edits are not saved. Close?", "Правки не збережено. Закрити?",
            "Die Änderungen sind nicht gespeichert. Schließen?",
            "Le modifiche non sono salvate. Chiudere?",
            "Los cambios no están guardados. ¿Cerrar?",
            "Les modifications ne sont pas enregistrées. Fermer ?",
            "As alterações não foram salvas. Fechar?", "التعديلات غير محفوظة. هل تريد الإغلاق؟",
            "बदलाव सहेजे नहीं गए हैं। बंद करें?", "পরিবর্তনগুলি সংরক্ষিত হয়নি। বন্ধ করবেন?",
            "تبدیلیاں محفوظ نہیں ہوئیں۔ بند کریں؟", "修改尚未保存。是否关闭？")

        ' --- saving -------------------------------------------------------------
        Add("Сохраняю..",
            "Saving..", "Зберігаю..", "Wird gespeichert..", "Salvataggio..", "Guardando..",
            "Enregistrement..", "Salvando..", "جارٍ الحفظ..", "सहेजा जा रहा है..",
            "সংরক্ষণ করা হচ্ছে..", "محفوظ کیا جا رہا ہے..", "正在保存..")
        Add("Сохранено: {0}",
            "Saved: {0}", "Збережено: {0}", "Gespeichert: {0}", "Salvato: {0}",
            "Guardado: {0}", "Enregistré : {0}", "Salvo: {0}", "تم الحفظ: {0}",
            "सहेजा गया: {0}", "সংরক্ষিত: {0}", "محفوظ ہو گیا: {0}", "已保存：{0}")
        Add("Сохранено в: {0}",
            "Saved to: {0}", "Збережено в: {0}", "Gespeichert unter: {0}", "Salvato in: {0}",
            "Guardado en: {0}", "Enregistré dans : {0}", "Salvo em: {0}", "تم الحفظ في: {0}",
            "यहाँ सहेजा गया: {0}", "এখানে সংরক্ষিত: {0}", "یہاں محفوظ ہوا: {0}", "已保存到：{0}")
        Add("Не удалось сохранить: {0}",
            "Could not save: {0}", "Не вдалося зберегти: {0}", "Speichern fehlgeschlagen: {0}",
            "Salvataggio non riuscito: {0}", "No se pudo guardar: {0}",
            "Échec de l'enregistrement : {0}", "Não foi possível salvar: {0}",
            "تعذّر الحفظ: {0}", "सहेजा नहीं जा सका: {0}", "সংরক্ষণ করা যায়নি: {0}",
            "محفوظ نہیں ہو سکا: {0}", "无法保存：{0}")

        ' --- why "Save" is not available (§9.3) ---------------------------------
        Add("Поверх оригинала нельзя: {0}. Доступно «Сохранить как..»",
            "Cannot write over the original: {0}. Save as.. is available.",
            "Поверх оригіналу не можна: {0}. Доступно «Зберегти як..»",
            "Das Original kann nicht überschrieben werden: {0}. Speichern unter.. ist möglich.",
            "Impossibile sovrascrivere l'originale: {0}. È disponibile Salva con nome..",
            "No se puede sobrescribir el original: {0}. Está disponible Guardar como..",
            "Impossible d'écraser l'original : {0}. Enregistrer sous.. reste disponible.",
            "Não é possível sobrescrever o original: {0}. Salvar como.. está disponível.",
            "لا يمكن الكتابة فوق الأصل: {0}. خيار «حفظ باسم..» متاح.",
            "मूल फ़ाइल पर नहीं लिखा जा सकता: {0}. «इस रूप में सहेजें..» उपलब्ध है।",
            "মূল ফাইলের উপরে লেখা যাবে না: {0}. «এভাবে সংরক্ষণ করুন..» উপলব্ধ।",
            "اصل فائل پر نہیں لکھا جا سکتا: {0}۔ «بطور محفوظ کریں..» دستیاب ہے۔",
            "无法覆盖原文件：{0}。可以使用「另存为..」。")
        Add("этот формат не записывается",
            "this format is not written", "цей формат не записується",
            "dieses Format wird nicht geschrieben", "questo formato non viene scritto",
            "este formato no se escribe", "ce format n'est pas écrit",
            "este formato não é gravado", "هذه الصيغة لا تُكتب",
            "यह प्रारूप लिखा नहीं जाता", "এই ফরম্যাট লেখা হয় না",
            "یہ فارمیٹ لکھا نہیں جاتا", "不写入该格式")
        Add("файл только для чтения",
            "the file is read-only", "файл лише для читання", "die Datei ist schreibgeschützt",
            "il file è di sola lettura", "el archivo es de solo lectura",
            "le fichier est en lecture seule", "o arquivo é somente leitura",
            "الملف للقراءة فقط", "फ़ाइल केवल पढ़ने योग्य है", "ফাইলটি কেবল পড়ার জন্য",
            "فائل صرف پڑھنے کے لیے ہے", "该文件为只读")
        Add("нет прав на запись",
            "there is no write permission", "немає прав на запис", "keine Schreibrechte",
            "mancano i permessi di scrittura", "no hay permisos de escritura",
            "les droits d'écriture manquent", "não há permissão de gravação",
            "لا توجد صلاحية للكتابة", "लिखने की अनुमति नहीं है", "লেখার অনুমতি নেই",
            "لکھنے کی اجازت نہیں", "没有写入权限")
        Add("файл занят другой программой",
            "another program is holding the file", "файл зайнятий іншою програмою",
            "eine andere Anwendung hält die Datei", "un altro programma sta usando il file",
            "otro programa está usando el archivo", "un autre programme utilise le fichier",
            "outro programa está usando o arquivo", "برنامج آخر يستخدم الملف",
            "फ़ाइल किसी अन्य प्रोग्राम के पास है", "ফাইলটি অন্য প্রোগ্রাম ব্যবহার করছে",
            "فائل کسی اور پروگرام کے زیرِ استعمال ہے", "文件正被其他程序占用")
        Add("файл не найден",
            "the file is missing", "файл не знайдено", "die Datei fehlt",
            "il file non esiste", "el archivo no existe", "le fichier est introuvable",
            "o arquivo não existe", "الملف غير موجود", "फ़ाइल मौजूद नहीं है",
            "ফাইলটি নেই", "فائل موجود نہیں", "文件不存在")

        ' --- the one confirmation the save path asks (§9.5) ---------------------
        Add("JPEG будет пересжат - это необратимо. Дата съёмки и остальные EXIF-данные сохранятся. Продолжить?",
            "The JPEG will be re-compressed - that cannot be undone. The capture date and the rest of the EXIF data are kept. Continue?",
            "JPEG буде перестиснуто - це незворотно. Дата зйомки та решта EXIF-даних збережуться. Продовжити?",
            "Das JPEG wird neu komprimiert - das lässt sich nicht rückgängig machen. Aufnahmedatum und die übrigen EXIF-Daten bleiben erhalten. Fortfahren?",
            "Il JPEG verrà ricompresso - l'operazione è irreversibile. La data di scatto e gli altri dati EXIF vengono mantenuti. Continuare?",
            "El JPEG se volverá a comprimir - es irreversible. La fecha de captura y el resto de los datos EXIF se conservan. ¿Continuar?",
            "Le JPEG sera recompressé - c'est irréversible. La date de prise de vue et les autres données EXIF sont conservées. Continuer ?",
            "O JPEG será recomprimido - isso é irreversível. A data da foto e os demais dados EXIF são mantidos. Continuar?",
            "ستتم إعادة ضغط ملف JPEG - وهذا إجراء لا رجعة فيه. يتم الاحتفاظ بتاريخ التصوير وبقية بيانات EXIF. هل تريد المتابعة؟",
            "JPEG दोबारा संपीड़ित होगा - यह वापस नहीं किया जा सकता। खींचने की तिथि और बाकी EXIF डेटा बना रहेगा। जारी रखें?",
            "JPEG আবার সংকুচিত হবে - এটি ফেরানো যায় না। ছবি তোলার তারিখ ও বাকি EXIF তথ্য থেকে যাবে। চালিয়ে যাবেন?",
            "JPEG دوبارہ کمپریس ہو گا - یہ واپس نہیں کیا جا سکتا۔ تصویر کی تاریخ اور باقی EXIF ڈیٹا محفوظ رہے گا۔ جاری رکھیں؟",
            "JPEG 将被重新压缩 - 此操作不可撤销。拍摄日期和其余 EXIF 数据会保留。是否继续？")

    End Sub


End Class
