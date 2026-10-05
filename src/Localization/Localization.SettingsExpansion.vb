Option Strict On

' <summary>
' Strings introduced by SPECIFICATION_SETTINGS_EXPANSION - the interface scale (§4.1),
' the file-type and shortcut dialogs (§3.4, §3.5), the history window (§7.2), the OCR
' cache row (§7.3) and the replace-or-merge import (§7.4).
'
' The forty-odd shortcut ACTIONS live here too. Ten of them - the recipient slots - are
' one placeholder string rather than ten entries, which is the same reason
' OcrLanguageCatalog shows endonyms: naming the same thing thirteen times over is work
' that buys nothing.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' Никогда не ставьте «умные» кавычки в литерал - VB считает U+201C/U+201D разделителями
' строки (see the localization rules in CLAUDE.md); используются «…» и 「…」.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddSettingsExpansionStrings()

        ' --- interface scale (§4.1) -----------------------------------------------

        Add("Масштаб интерфейса",
            "Interface scale", "Масштаб інтерфейсу", "Oberflächenskalierung", "Scala dell'interfaccia",
            "Escala de la interfaz", "Échelle de l'interface", "Escala da interface",
            "مقياس الواجهة", "इंटरफ़ेस स्केल", "ইন্টারফেস স্কেল", "انٹرفیس اسکیل", "界面缩放")

        Add("Размер шрифта и элементов окна настроек. Применяется после его перезапуска.",
            "Font and control size of the settings window. Applied after it is reopened.",
            "Розмір шрифту та елементів вікна налаштувань. Застосовується після його перезапуску.",
            "Schrift- und Elementgröße des Einstellungsfensters. Wirksam nach dem Neuöffnen.",
            "Dimensione di testo ed elementi della finestra impostazioni. Attiva dopo la riapertura.",
            "Tamaño de texto y controles de la ventana de ajustes. Se aplica al reabrirla.",
            "Taille du texte et des éléments de la fenêtre des réglages. Appliquée après réouverture.",
            "Tamanho do texto e dos elementos da janela de configurações. Aplicado ao reabri-la.",
            "حجم الخط وعناصر نافذة الإعدادات. يُطبَّق بعد إعادة فتحها.",
            "सेटिंग्स विंडो के फ़ॉन्ट और नियंत्रणों का आकार। दोबारा खोलने पर लागू होता है।",
            "সেটিংস উইন্ডোর ফন্ট ও উপাদানের আকার। পুনরায় খুললে প্রযোজ্য।",
            "ترتیبات ونڈو کے فونٹ اور عناصر کا سائز۔ دوبارہ کھولنے پر لاگو ہوتا ہے۔",
            "设置窗口的字体与控件大小。重新打开后生效。")

        Add("Системный",
            "System", "Системний", "System", "Di sistema", "Del sistema", "Système", "Do sistema",
            "النظام", "सिस्टम", "সিস্টেম", "سسٹم", "系统")

        Add("Новый масштаб применится после перезапуска окна настроек. Перезапустить сейчас?",
            "The new scale applies once the settings window is reopened. Reopen it now?",
            "Новий масштаб застосується після перезапуску вікна налаштувань. Перезапустити зараз?",
            "Die neue Skalierung wirkt nach dem Neuöffnen des Einstellungsfensters. Jetzt neu öffnen?",
            "La nuova scala si applica dopo la riapertura della finestra impostazioni. Riaprirla ora?",
            "La nueva escala se aplica al reabrir la ventana de ajustes. ¿Reabrirla ahora?",
            "La nouvelle échelle s'applique après réouverture de la fenêtre des réglages. La rouvrir maintenant ?",
            "A nova escala é aplicada ao reabrir a janela de configurações. Reabrir agora?",
            "يُطبَّق المقياس الجديد بعد إعادة فتح نافذة الإعدادات. إعادة فتحها الآن؟",
            "नया स्केल सेटिंग्स विंडो दोबारा खोलने पर लागू होगा। अभी दोबारा खोलें?",
            "নতুন স্কেল সেটিংস উইন্ডো পুনরায় খুললে কার্যকর হবে। এখনই খুলবেন?",
            "نیا اسکیل ترتیبات ونڈو دوبارہ کھولنے پر لاگو ہوگا۔ ابھی دوبارہ کھولیں؟",
            "新的缩放会在重新打开设置窗口后生效。现在重新打开吗？")

        ' --- shared small captions --------------------------------------------------

        Add("ОК", "OK", "OK", "OK", "OK", "Aceptar", "OK", "OK",
            "موافق", "ठीक है", "ঠিক আছে", "ٹھیک ہے", "确定")

        Add("Настроить..",
            "Configure..", "Налаштувати..", "Einrichten..", "Configura..", "Configurar..",
            "Configurer..", "Configurar..", "إعداد..", "कॉन्फ़िगर करें..", "কনফিগার করুন..",
            "ترتیب دیں..", "配置..")

        Add("Очистить", "Clear", "Очистити", "Leeren", "Svuota", "Vaciar", "Vider", "Limpar",
            "مسح", "साफ़ करें", "পরিষ্কার করুন", "صاف کریں", "清空")

        Add("Сбросить", "Reset", "Скинути", "Zurücksetzen", "Reimposta", "Restablecer",
            "Réinitialiser", "Redefinir", "إعادة تعيين", "रीसेट करें", "রিসেট করুন",
            "ری سیٹ کریں", "重置")

        Add("Сбросить всё",
            "Reset all", "Скинути все", "Alles zurücksetzen", "Reimposta tutto", "Restablecer todo",
            "Tout réinitialiser", "Redefinir tudo", "إعادة تعيين الكل", "सब कुछ रीसेट करें",
            "সব রিসেট করুন", "سب ری سیٹ کریں", "全部重置")

        ' --- file types (§3.4) ------------------------------------------------------

        Add("Изображения",
            "Images", "Зображення", "Bilder", "Immagini", "Imágenes", "Images", "Imagens",
            "الصور", "छवियाँ", "ছবি", "تصاویر", "图片")

        Add("Аудио", "Audio", "Аудіо", "Audio", "Audio", "Audio", "Audio", "Áudio",
            "الصوت", "ऑडियो", "অডিও", "آڈیو", "音频")

        Add("Другие поддерживаемые",
            "Other supported", "Інші підтримувані", "Weitere unterstützte", "Altri supportati",
            "Otros compatibles", "Autres pris en charge", "Outros suportados",
            "أنواع مدعومة أخرى", "अन्य समर्थित", "অন্যান্য সমর্থিত", "دیگر معاون", "其他受支持格式")

        Add("Выберите группы и отдельные форматы. Если отмечено всё - показываются все поддерживаемые.",
            "Pick groups or single formats. With everything ticked, all supported formats are shown.",
            "Виберіть групи й окремі формати. Якщо позначено все - показуються всі підтримувані.",
            "Wählen Sie Gruppen oder einzelne Formate. Ist alles markiert, werden alle unterstützten gezeigt.",
            "Scegli gruppi o singoli formati. Se è tutto selezionato, vengono mostrati tutti quelli supportati.",
            "Elija grupos o formatos sueltos. Con todo marcado se muestran todos los compatibles.",
            "Choisissez des groupes ou des formats isolés. Tout coché, tous les formats pris en charge sont affichés.",
            "Escolha grupos ou formatos avulsos. Com tudo marcado, todos os suportados são exibidos.",
            "اختر مجموعات أو صيغًا مفردة. عند تحديد الكل تُعرض جميع الصيغ المدعومة.",
            "समूह या अलग-अलग प्रारूप चुनें। सब चुने होने पर सभी समर्थित प्रारूप दिखते हैं।",
            "গ্রুপ বা আলাদা ফরম্যাট বেছে নিন। সব চিহ্নিত থাকলে সব সমর্থিত ফরম্যাট দেখানো হয়।",
            "گروپ یا انفرادی فارمیٹ منتخب کریں۔ سب منتخب ہونے پر تمام معاون فارمیٹ دکھائے جاتے ہیں۔",
            "选择分组或单个格式。全部勾选时显示所有受支持的格式。")

        ' --- shortcuts dialog (§3.5) ------------------------------------------------

        Add("Сочетания клавиш",
            "Keyboard shortcuts", "Сполучення клавіш", "Tastenkombinationen", "Scorciatoie da tastiera",
            "Atajos de teclado", "Raccourcis clavier", "Atalhos de teclado",
            "اختصارات لوحة المفاتيح", "कीबोर्ड शॉर्टकट", "কীবোর্ড শর্টকাট", "کی بورڈ شارٹ کٹس", "键盘快捷键")

        Add("Переназначьте клавиши действий. Системные сочетания, F11 и Esc остаются за программой.",
            "Rebind the action keys. System combinations, F11 and Esc stay with the program.",
            "Перепризначте клавіші дій. Системні сполучення, F11 та Esc лишаються за програмою.",
            "Belegen Sie die Aktionstasten neu. Systemkombinationen, F11 und Esc bleiben beim Programm.",
            "Riassegna i tasti delle azioni. Le combinazioni di sistema, F11 ed Esc restano al programma.",
            "Reasigne las teclas de acción. Las combinaciones del sistema, F11 y Esc siguen siendo del programa.",
            "Réattribuez les touches d'action. Les combinaisons système, F11 et Échap restent au programme.",
            "Reatribua as teclas de ação. As combinações do sistema, F11 e Esc continuam com o programa.",
            "أعد تعيين مفاتيح الإجراءات. تبقى اختصارات النظام وF11 وEsc للبرنامج.",
            "क्रियाओं की कुंजियाँ बदलें। सिस्टम संयोजन, F11 और Esc प्रोग्राम के पास रहते हैं।",
            "ক্রিয়ার কী পুনরায় নির্ধারণ করুন। সিস্টেম কম্বিনেশন, F11 ও Esc প্রোগ্রামের কাছেই থাকে।",
            "ایکشن کیز دوبارہ تفویض کریں۔ سسٹم امتزاج، F11 اور Esc پروگرام کے پاس رہتے ہیں۔",
            "重新指定操作按键。系统组合键、F11 和 Esc 仍归程序所有。")

        Add("Действие", "Action", "Дія", "Aktion", "Azione", "Acción", "Action", "Ação",
            "الإجراء", "क्रिया", "ক্রিয়া", "عمل", "操作")

        Add("Сочетание",
            "Shortcut", "Сполучення", "Kombination", "Combinazione", "Combinación", "Combinaison",
            "Combinação", "الاختصار", "शॉर्टकट", "শর্টকাট", "شارٹ کٹ", "快捷键")

        Add("Не назначено",
            "Not assigned", "Не призначено", "Nicht zugewiesen", "Non assegnata", "Sin asignar",
            "Non attribué", "Não atribuído", "غير معيَّن", "निर्दिष्ट नहीं", "নির্ধারিত নয়",
            "تفویض نہیں", "未指定")

        Add("Снять сочетание",
            "Remove the shortcut", "Зняти сполучення", "Kombination entfernen", "Rimuovi la combinazione",
            "Quitar la combinación", "Supprimer la combinaison", "Remover a combinação",
            "إزالة الاختصار", "शॉर्टकट हटाएँ", "শর্টকাট সরান", "شارٹ کٹ ہٹائیں", "移除快捷键")

        Add("Выберите действие, поставьте курсор в поле и нажмите сочетание. Esc отменяет запись.",
            "Pick an action, put the cursor in the field and press the combination. Esc cancels the capture.",
            "Виберіть дію, поставте курсор у поле й натисніть сполучення. Esc скасовує запис.",
            "Wählen Sie eine Aktion, klicken Sie ins Feld und drücken Sie die Kombination. Esc bricht ab.",
            "Scegli un'azione, porta il cursore nel campo e premi la combinazione. Esc annulla la registrazione.",
            "Elija una acción, ponga el cursor en el campo y pulse la combinación. Esc cancela la captura.",
            "Choisissez une action, placez le curseur dans le champ et appuyez sur la combinaison. Échap annule.",
            "Escolha uma ação, ponha o cursor no campo e pressione a combinação. Esc cancela a captura.",
            "اختر إجراءً، ضع المؤشر في الحقل واضغط الاختصار. يُلغي Esc التسجيل.",
            "कोई क्रिया चुनें, कर्सर फ़ील्ड में रखें और संयोजन दबाएँ। Esc रिकॉर्डिंग रद्द करता है।",
            "একটি ক্রিয়া বাছুন, কার্সার ফিল্ডে রাখুন এবং কম্বিনেশন চাপুন। Esc রেকর্ড বাতিল করে।",
            "کوئی عمل منتخب کریں، کرسر فیلڈ میں رکھیں اور امتزاج دبائیں۔ Esc ریکارڈنگ منسوخ کرتا ہے۔",
            "选择一个操作，把光标放进输入框并按下组合键。Esc 取消录制。")

        Add("Это сочетание зарезервировано системой или программой и не может быть назначено.",
            "This combination is reserved by the system or the program and cannot be assigned.",
            "Це сполучення зарезервоване системою або програмою і не може бути призначене.",
            "Diese Kombination ist vom System oder vom Programm belegt und kann nicht zugewiesen werden.",
            "Questa combinazione è riservata dal sistema o dal programma e non può essere assegnata.",
            "Esta combinación está reservada por el sistema o el programa y no puede asignarse.",
            "Cette combinaison est réservée par le système ou le programme et ne peut pas être attribuée.",
            "Esta combinação é reservada pelo sistema ou pelo programa e não pode ser atribuída.",
            "هذا الاختصار محجوز للنظام أو للبرنامج ولا يمكن تعيينه.",
            "यह संयोजन सिस्टम या प्रोग्राम द्वारा आरक्षित है और निर्दिष्ट नहीं किया जा सकता।",
            "এই কম্বিনেশনটি সিস্টেম বা প্রোগ্রামের জন্য সংরক্ষিত, নির্ধারণ করা যাবে না।",
            "یہ امتزاج سسٹم یا پروگرام کے لیے مخصوص ہے اور تفویض نہیں کیا جا سکتا۔",
            "该组合键由系统或程序保留，无法指定。")

        Add("Сочетание {0} уже назначено действию «{1}».",
            "The combination {0} already belongs to «{1}».",
            "Сполучення {0} вже призначене дії «{1}».",
            "Die Kombination {0} gehört bereits zu «{1}».",
            "La combinazione {0} appartiene già a «{1}».",
            "La combinación {0} ya pertenece a «{1}».",
            "La combinaison {0} appartient déjà à «{1}».",
            "A combinação {0} já pertence a «{1}».",
            "الاختصار {0} معيَّن بالفعل للإجراء «{1}».",
            "संयोजन {0} पहले से «{1}» को दिया गया है।",
            "কম্বিনেশন {0} ইতিমধ্যে «{1}»-এর জন্য নির্ধারিত।",
            "امتزاج {0} پہلے ہی «{1}» کو دیا گیا ہے۔",
            "组合键 {0} 已属于「{1}」。")

        Add("Да - обменять сочетания, Нет - снять сочетание у этого действия.",
            "Yes - swap the combinations, No - leave that action without one.",
            "Так - обміняти сполучення, Ні - зняти сполучення в тієї дії.",
            "Ja - Kombinationen tauschen, Nein - jener Aktion die Kombination nehmen.",
            "Sì - scambia le combinazioni, No - lascia quell'azione senza.",
            "Sí - intercambiar las combinaciones, No - dejar esa acción sin ninguna.",
            "Oui - échanger les combinaisons, Non - laisser cette action sans combinaison.",
            "Sim - trocar as combinações, Não - deixar aquela ação sem nenhuma.",
            "نعم - تبادل الاختصارين، لا - ترك ذلك الإجراء بلا اختصار.",
            "हाँ - संयोजन आपस में बदलें, नहीं - उस क्रिया को बिना संयोजन छोड़ें।",
            "হ্যাঁ - কম্বিনেশন বিনিময় করুন, না - ওই ক্রিয়াকে কম্বিনেশন ছাড়াই রাখুন।",
            "ہاں - امتزاج تبدیل کریں، نہیں - اُس عمل کو بغیر امتزاج چھوڑ دیں۔",
            "是 - 交换组合键，否 - 让该操作没有组合键。")

        Add("Вернуть все сочетания к значениям по умолчанию?",
            "Reset every shortcut to its default?",
            "Повернути всі сполучення до типових значень?",
            "Alle Tastenkombinationen auf die Standardwerte zurücksetzen?",
            "Reimpostare tutte le combinazioni ai valori predefiniti?",
            "¿Restablecer todas las combinaciones a sus valores predeterminados?",
            "Réinitialiser toutes les combinaisons à leurs valeurs par défaut ?",
            "Redefinir todas as combinações para os valores padrão?",
            "إعادة كل الاختصارات إلى قيمها الافتراضية؟",
            "क्या सभी शॉर्टकट डिफ़ॉल्ट पर लौटा दें?",
            "সব শর্টকাট ডিফল্টে ফিরিয়ে আনবেন?",
            "کیا تمام شارٹ کٹس کو ڈیفالٹ پر واپس لائیں؟",
            "将所有快捷键恢复为默认值？")

        Add("Переместить в папку {0}",
            "Move to folder {0}", "Перемістити до теки {0}", "In Ordner {0} verschieben",
            "Sposta nella cartella {0}", "Mover a la carpeta {0}", "Déplacer vers le dossier {0}",
            "Mover para a pasta {0}", "النقل إلى المجلد {0}", "फ़ोल्डर {0} में ले जाएँ",
            "ফোল্ডার {0}-এ সরান", "فولڈر {0} میں منتقل کریں", "移动到文件夹 {0}")

        ' --- shortcut actions (§3.5) ------------------------------------------------

        Add("Первый файл",
            "First file", "Перший файл", "Erste Datei", "Primo file", "Primer archivo",
            "Premier fichier", "Primeiro ficheiro", "الملف الأول", "पहली फ़ाइल", "প্রথম ফাইল",
            "پہلی فائل", "第一个文件")

        Add("Назад на 10 файлов",
            "Back 10 files", "Назад на 10 файлів", "10 Dateien zurück", "Indietro di 10 file",
            "Retroceder 10 archivos", "Reculer de 10 fichiers", "Voltar 10 ficheiros",
            "الرجوع 10 ملفات", "10 फ़ाइल पीछे", "১০ ফাইল পিছনে", "10 فائلیں پیچھے", "后退 10 个文件")

        Add("Вперёд на 10 файлов",
            "Forward 10 files", "Уперед на 10 файлів", "10 Dateien vorwärts", "Avanti di 10 file",
            "Avanzar 10 archivos", "Avancer de 10 fichiers", "Avançar 10 ficheiros",
            "التقدم 10 ملفات", "10 फ़ाइल आगे", "১০ ফাইল সামনে", "10 فائلیں آگے", "前进 10 个文件")

        Add("Назад на 100 файлов",
            "Back 100 files", "Назад на 100 файлів", "100 Dateien zurück", "Indietro di 100 file",
            "Retroceder 100 archivos", "Reculer de 100 fichiers", "Voltar 100 ficheiros",
            "الرجوع 100 ملف", "100 फ़ाइल पीछे", "১০০ ফাইল পিছনে", "100 فائلیں پیچھے", "后退 100 个文件")

        Add("Вперёд на 100 файлов",
            "Forward 100 files", "Уперед на 100 файлів", "100 Dateien vorwärts", "Avanti di 100 file",
            "Avanzar 100 archivos", "Avancer de 100 fichiers", "Avançar 100 ficheiros",
            "التقدم 100 ملف", "100 फ़ाइल आगे", "১০০ ফাইল সামনে", "100 فائلیں آگے", "前进 100 个文件")

        Add("Выбрать файл",
            "Choose a file", "Вибрати файл", "Datei wählen", "Scegli un file", "Elegir archivo",
            "Choisir un fichier", "Escolher ficheiro", "اختيار ملف", "फ़ाइल चुनें", "ফাইল বাছুন",
            "فائل منتخب کریں", "选择文件")

        Add("Перейти к номеру файла",
            "Go to a file number", "Перейти до номера файлу", "Zu einer Dateinummer springen",
            "Vai al numero di file", "Ir al número de archivo", "Aller au numéro de fichier",
            "Ir para o número do ficheiro", "الانتقال إلى رقم ملف", "फ़ाइल संख्या पर जाएँ",
            "ফাইল নম্বরে যান", "فائل نمبر پر جائیں", "跳转到文件编号")

        Add("Переименовать файл",
            "Rename the file", "Перейменувати файл", "Datei umbenennen", "Rinomina il file",
            "Renombrar el archivo", "Renommer le fichier", "Renomear o ficheiro",
            "إعادة تسمية الملف", "फ़ाइल का नाम बदलें", "ফাইলের নাম বদলান", "فائل کا نام بدلیں", "重命名文件")

        Add("Удалить файл",
            "Delete the file", "Видалити файл", "Datei löschen", "Elimina il file",
            "Eliminar el archivo", "Supprimer le fichier", "Excluir o ficheiro",
            "حذف الملف", "फ़ाइल हटाएँ", "ফাইল মুছুন", "فائل حذف کریں", "删除文件")

        Add("Удалить безвозвратно",
            "Delete permanently", "Видалити безповоротно", "Endgültig löschen", "Elimina definitivamente",
            "Eliminar definitivamente", "Supprimer définitivement", "Excluir definitivamente",
            "حذف نهائي", "स्थायी रूप से हटाएँ", "স্থায়ীভাবে মুছুন", "مستقل طور پر حذف کریں", "永久删除")

        Add("Отменить действие",
            "Undo", "Скасувати дію", "Rückgängig", "Annulla", "Deshacer", "Annuler", "Desfazer",
            "تراجع", "पूर्ववत करें", "পূর্বাবস্থায় ফিরুন", "واپس لیں", "撤销")

        Add("Повернуть по часовой стрелке",
            "Rotate clockwise", "Повернути за годинниковою стрілкою", "Im Uhrzeigersinn drehen",
            "Ruota in senso orario", "Girar en sentido horario", "Pivoter dans le sens horaire",
            "Girar no sentido horário", "التدوير باتجاه عقارب الساعة", "दक्षिणावर्त घुमाएँ",
            "ঘড়ির কাঁটার দিকে ঘোরান", "گھڑی کی سمت گھمائیں", "顺时针旋转")

        Add("Повернуть против часовой стрелки",
            "Rotate counter-clockwise", "Повернути проти годинникової стрілки", "Gegen den Uhrzeigersinn drehen",
            "Ruota in senso antiorario", "Girar en sentido antihorario", "Pivoter dans le sens antihoraire",
            "Girar no sentido anti-horário", "التدوير عكس عقارب الساعة", "वामावर्त घुमाएँ",
            "ঘড়ির কাঁটার উল্টো দিকে ঘোরান", "گھڑی کی مخالف سمت گھمائیں", "逆时针旋转")

        Add("Распознать и перевести",
            "Recognize and translate", "Розпізнати й перекласти", "Erkennen und übersetzen",
            "Riconosci e traduci", "Reconocer y traducir", "Reconnaître et traduire",
            "Reconhecer e traduzir", "التعرّف والترجمة", "पहचानें और अनुवाद करें",
            "শনাক্ত করে অনুবাদ করুন", "پہچانیں اور ترجمہ کریں", "识别并翻译")

        Add("Автоматическое распознавание",
            "Automatic recognition", "Автоматичне розпізнавання", "Automatische Erkennung",
            "Riconoscimento automatico", "Reconocimiento automático", "Reconnaissance automatique",
            "Reconhecimento automático", "التعرّف التلقائي", "स्वचालित पहचान",
            "স্বয়ংক্রিয় শনাক্তকরণ", "خودکار شناخت", "自动识别")

        Add("Полноэкранный режим",
            "Full screen", "Повноекранний режим", "Vollbildmodus", "Schermo intero",
            "Pantalla completa", "Plein écran", "Ecrã inteiro", "وضع ملء الشاشة",
            "पूर्ण स्क्रीन", "পূর্ণ পর্দা", "فل اسکرین", "全屏模式")

        Add("Справка", "Help", "Довідка", "Hilfe", "Guida", "Ayuda", "Aide", "Ajuda",
            "المساعدة", "सहायता", "সহায়তা", "مدد", "帮助")

        Add("Панель изображений",
            "Image panel", "Панель зображень", "Bildleiste", "Pannello immagini",
            "Panel de imágenes", "Panneau d'images", "Painel de imagens",
            "لوحة الصور", "छवि पैनल", "ছবি প্যানেল", "تصویری پینل", "图片面板")

        Add("Увеличить масштаб",
            "Zoom in", "Збільшити масштаб", "Vergrößern", "Ingrandisci", "Acercar", "Zoom avant",
            "Ampliar", "تكبير", "ज़ूम इन", "জুম ইন", "زوم اِن", "放大")

        Add("Уменьшить масштаб",
            "Zoom out", "Зменшити масштаб", "Verkleinern", "Riduci", "Alejar", "Zoom arrière",
            "Reduzir", "تصغير", "ज़ूम आउट", "জুম আউট", "زوم آؤٹ", "缩小")

        Add("Вписать в окно",
            "Fit to the window", "Вписати у вікно", "An das Fenster anpassen", "Adatta alla finestra",
            "Ajustar a la ventana", "Ajuster à la fenêtre", "Ajustar à janela",
            "الملاءمة مع النافذة", "विंडो में फ़िट करें", "উইন্ডোতে ফিট করুন",
            "ونڈو میں فٹ کریں", "适应窗口")

        Add("Реальный размер",
            "Actual size", "Реальний розмір", "Originalgröße", "Dimensione reale", "Tamaño real",
            "Taille réelle", "Tamanho real", "الحجم الحقيقي", "वास्तविक आकार", "প্রকৃত আকার",
            "اصل سائز", "实际大小")

        ' --- history (§7.2) ---------------------------------------------------------

        Add("Недавние файлы и папки",
            "Recent files and folders", "Нещодавні файли та теки", "Zuletzt verwendete Dateien und Ordner",
            "File e cartelle recenti", "Archivos y carpetas recientes", "Fichiers et dossiers récents",
            "Ficheiros e pastas recentes", "الملفات والمجلدات الأخيرة",
            "हाल की फ़ाइलें और फ़ोल्डर", "সাম্প্রতিক ফাইল ও ফোল্ডার",
            "حالیہ فائلیں اور فولڈرز", "最近的文件和文件夹")

        Add("Открыть историю",
            "Open the history", "Відкрити історію", "Verlauf öffnen", "Apri la cronologia",
            "Abrir el historial", "Ouvrir l'historique", "Abrir o histórico",
            "فتح السجل", "इतिहास खोलें", "ইতিহাস খুলুন", "تاریخچہ کھولیں", "打开历史记录")

        Add("Открыть запись, удалить одну или очистить историю целиком.",
            "Open an entry, remove one, or clear the whole history.",
            "Відкрити запис, видалити один або очистити історію повністю.",
            "Einen Eintrag öffnen, einen entfernen oder den ganzen Verlauf leeren.",
            "Apri una voce, rimuovine una o svuota tutta la cronologia.",
            "Abrir una entrada, quitar una o vaciar todo el historial.",
            "Ouvrir une entrée, en supprimer une ou vider tout l'historique.",
            "Abrir uma entrada, remover uma ou limpar todo o histórico.",
            "افتح عنصرًا أو احذف واحدًا أو امسح السجل بالكامل.",
            "कोई प्रविष्टि खोलें, एक हटाएँ या पूरा इतिहास साफ़ करें।",
            "একটি এন্ট্রি খুলুন, একটি সরান বা পুরো ইতিহাস মুছুন।",
            "کوئی اندراج کھولیں، ایک ہٹائیں یا پورا تاریخچہ صاف کریں۔",
            "打开一条记录、删除一条，或清空整个历史。")

        Add("Папки", "Folders", "Теки", "Ordner", "Cartelle", "Carpetas", "Dossiers", "Pastas",
            "المجلدات", "फ़ोल्डर", "ফোল্ডার", "فولڈرز", "文件夹")

        Add("Очистить весь список?",
            "Clear the whole list?", "Очистити весь список?", "Die ganze Liste leeren?",
            "Svuotare l'intero elenco?", "¿Vaciar toda la lista?", "Vider toute la liste ?",
            "Limpar a lista inteira?", "مسح القائمة بالكامل؟", "क्या पूरी सूची साफ़ करें?",
            "পুরো তালিকা মুছবেন?", "کیا پوری فہرست صاف کریں؟", "清空整个列表？")

        ' --- OCR cache (§7.3) -------------------------------------------------------

        Add("Занято на диске",
            "Used on disk", "Зайнято на диску", "Belegt auf der Festplatte", "Spazio su disco usato",
            "Ocupado en disco", "Occupé sur le disque", "Ocupado no disco",
            "المستخدَم على القرص", "डिस्क पर उपयोग", "ডিস্কে ব্যবহৃত", "ڈسک پر استعمال", "占用磁盘")

        Add("Сколько места занимают сохранённые результаты распознавания и перевода. Очистка не меняет настройки OCR и не удаляет языковые данные - текст распознаётся заново.",
            "How much room the stored recognition and translation results take. Clearing changes no OCR setting and removes no language data - the text is recognized again.",
            "Скільки місця займають збережені результати розпізнавання й перекладу. Очищення не змінює налаштування OCR і не видаляє мовні дані - текст розпізнається знову.",
            "Wie viel Raum die gespeicherten Erkennungs- und Übersetzungsergebnisse belegen. Leeren ändert keine OCR-Einstellung und löscht keine Sprachdaten - der Text wird neu erkannt.",
            "Quanto spazio occupano i risultati salvati di riconoscimento e traduzione. Svuotare non cambia le impostazioni OCR e non rimuove i dati linguistici - il testo viene riconosciuto di nuovo.",
            "Cuánto espacio ocupan los resultados guardados de reconocimiento y traducción. Vaciar no cambia los ajustes de OCR ni elimina los datos de idioma - el texto se vuelve a reconocer.",
            "Place occupée par les résultats enregistrés de reconnaissance et de traduction. Vider ne modifie aucun réglage OCR et ne supprime pas les données de langue - le texte est reconnu à nouveau.",
            "Quanto espaço ocupam os resultados salvos de reconhecimento e tradução. Limpar não altera as configurações de OCR nem remove os dados de idioma - o texto é reconhecido de novo.",
            "كم يشغل من المكان نتائج التعرّف والترجمة المحفوظة. المسح لا يغيّر إعدادات OCR ولا يحذف البيانات اللغوية - يُتعرّف على النص من جديد.",
            "सहेजे गए पहचान और अनुवाद परिणाम कितनी जगह लेते हैं। साफ़ करने से OCR सेटिंग्स नहीं बदलतीं और भाषा डेटा नहीं हटता - पाठ फिर से पहचाना जाता है।",
            "সংরক্ষিত শনাক্তকরণ ও অনুবাদ ফলাফল কত জায়গা নেয়। পরিষ্কার করলে OCR সেটিংস বদলায় না এবং ভাষার ডেটা মুছে যায় না - লেখা আবার শনাক্ত হয়।",
            "محفوظ شناخت اور ترجمہ نتائج کتنی جگہ لیتے ہیں۔ صاف کرنے سے OCR ترتیبات نہیں بدلتیں اور زبان کا ڈیٹا نہیں مٹتا - متن دوبارہ شناخت ہوتا ہے۔",
            "保存的识别和翻译结果占用多少空间。清空不会更改 OCR 设置，也不会删除语言数据 - 文本会重新识别。")

        Add("Очистить кэш",
            "Clear the cache", "Очистити кеш", "Cache leeren", "Svuota la cache", "Vaciar la caché",
            "Vider le cache", "Limpar a cache", "مسح الذاكرة المؤقتة", "कैश साफ़ करें",
            "ক্যাশ পরিষ্কার করুন", "کیش صاف کریں", "清除缓存")

        Add("Удалить сохранённые результаты распознавания? Настройки OCR не изменятся.",
            "Delete the stored recognition results? The OCR settings stay as they are.",
            "Видалити збережені результати розпізнавання? Налаштування OCR не зміняться.",
            "Die gespeicherten Erkennungsergebnisse löschen? Die OCR-Einstellungen bleiben unverändert.",
            "Eliminare i risultati di riconoscimento salvati? Le impostazioni OCR non cambiano.",
            "¿Eliminar los resultados de reconocimiento guardados? Los ajustes de OCR no cambian.",
            "Supprimer les résultats de reconnaissance enregistrés ? Les réglages OCR restent inchangés.",
            "Excluir os resultados de reconhecimento guardados? As configurações de OCR não mudam.",
            "حذف نتائج التعرّف المحفوظة؟ لن تتغيّر إعدادات OCR.",
            "क्या सहेजे गए पहचान परिणाम हटाएँ? OCR सेटिंग्स नहीं बदलेंगी।",
            "সংরক্ষিত শনাক্তকরণ ফলাফল মুছবেন? OCR সেটিংস বদলাবে না।",
            "کیا محفوظ شناخت نتائج حذف کریں؟ OCR ترتیبات نہیں بدلیں گی۔",
            "删除已保存的识别结果吗？OCR 设置不会改变。")

        Add("{0} МБ", "{0} MB", "{0} МБ", "{0} MB", "{0} MB", "{0} MB", "{0} Mo", "{0} MB",
            "{0} م.ب", "{0} एमबी", "{0} এমবি", "{0} ایم بی", "{0} MB")

        ' --- Storage page cache rows (130 §3.3) -------------------------------------

        Add("Обновить",
            "Refresh", "Оновити", "Aktualisieren", "Aggiorna", "Actualizar",
            "Actualiser", "Atualizar", "تحديث", "रिफ्रेश", "রিফ্রেশ", "ریفریش", "刷新")

        Add("Неизвестно",
            "Unknown", "Невідомо", "Unbekannt", "Sconosciuto", "Desconocido",
            "Inconnu", "Desconhecido", "غير معروف", "अज्ञात", "অজানা", "نامعلوم", "未知")

        Add("Не удалось измерить: {0}. Кнопка «Обновить» повторит попытку.",
            "Could not measure: {0}. The Refresh button will try again.",
            "Не вдалося виміряти: {0}. Кнопка «Оновити» повторить спробу.",
            "Messen nicht möglich: {0}. Die Schaltfläche »Aktualisieren« versucht es erneut.",
            "Impossibile misurare: {0}. Il pulsante Aggiorna riproverà.",
            "No se pudo medir: {0}. El botón Actualizar lo reintentará.",
            "Impossible de mesurer : {0}. Le bouton Actualiser réessaiera.",
            "Não foi possível medir: {0}. O botão Atualizar tentará de novo.",
            "تعذّر القياس: {0}. زر «تحديث» سيحاول مجددًا.",
            "मापन नहीं हो सका: {0}. रिफ्रेश बटन फिर कोशिश करेगा।",
            "মাপা যায়নি: {0}. রিফ্রেশ বোতাম আবার চেষ্টা করবে।",
            "ماپنا ممکن نہیں: {0}. ریفریش بٹن دوبارہ کوشش کرے گا۔",
            "无法测量：{0}。「刷新」按钮会重试。")

        Add("Кэш декодирования очищен.",
            "Decode cache cleared.", "Кеш декодування очищено.", "Dekodierer-Cache geleert.",
            "Cache di decodifica svuotata.", "Caché de decodificado vaciada.", "Cache de décodage vidé.",
            "Cache de decodificação limpo.", "تم مسح ذاكرة فك الترميز.", "डिकोड कैश साफ़ हो गया।",
            "ডিকোড ক্যাশ পরিষ্কার হয়েছে।", "ڈی کوڈ کیش صاف ہو گیا۔", "解码缓存已清空。")

        Add("Кэш миниатюр очищен.",
            "Thumbnail cache cleared.", "Кеш мініатюр очищено.", "Vorschaubild-Cache geleert.",
            "Cache delle miniature svuotata.", "Caché de miniaturas vaciada.", "Cache des miniatures vidé.",
            "Cache de miniaturas limpo.", "تم مسح ذاكرة الصور المصغرة.", "थंबनेल कैश साफ़ हो गया।",
            "থাম্বনেইল ক্যাশ পরিষ্কার হয়েছে।", "تھمب نیل کیش صاف ہو گیا۔", "缩略图缓存已清空。")

        Add("Сохранённые результаты распознавания удалены.",
            "Stored recognition results deleted.", "Збережені результати розпізнавання видалено.",
            "Gespeicherte Erkennungsergebnisse gelöscht.", "Risultati di riconoscimento salvati eliminati.",
            "Resultados de reconocimiento guardados eliminados.", "Résultats de reconnaissance enregistrés supprimés.",
            "Resultados de reconhecimento salvos excluídos.", "تم حذف نتائج التعرّف المحفوظة.",
            "सहेजे गए पहचान परिणाम हटा दिए गए।", "সংরক্ষিত শনাক্তকরণ ফলাফল মুছে ফেলা হয়েছে।",
            "محفوظ شناخت نتائج حذف ہو گئے۔", "已删除保存的识别结果。")

        Add("Сколько места занимают временные копии записей открытых архивов. Очистка убирает только оставшиеся копии - открытый архив продолжит работать.",
            "How much room the temporary copies of opened archive entries take. Clearing removes only the copies left behind - an open archive keeps working.",
            "Скільки місця займають тимчасові копії записів відкритих архівів. Очищення прибирає лише копії, що залишилися - відкритий архів продовжить працювати.",
            "Wie viel Raum die temporären Kopien geöffneter Archiveinträge belegen. Leeren entfernt nur zurückgebliebene Kopien - ein geöffnetes Archiv arbeitet weiter.",
            "Quanto spazio occupano le copie temporanee delle voci degli archivi aperti. Svuotare rimuove solo le copie rimaste - un archivio aperto continua a funzionare.",
            "Cuánto espacio ocupan las copias temporales de las entradas de archivos abiertos. Vaciar elimina solo las copias que quedaron - un archivo abierto sigue funcionando.",
            "Place occupée par les copies temporaires des entrées des archives ouvertes. Vider ne supprime que les copies restées - une archive ouverte continue de fonctionner.",
            "Quanto espaço ocupam as cópias temporárias das entradas de arquivos abertos. Limpar remove apenas as cópias que ficaram - um arquivo aberto continua funcionando.",
            "كم تشغل من المكان النسخ المؤقتة لإدخالات الأرشيف المفتوح. المسح يزيل النسخ المتبقية فقط - الأرشيف المفتوح يواصل العمل.",
            "खुले संग्रह प्रविष्टियों की अस्थायी प्रतियाँ कितनी जगह लेती हैं। साफ़ करने से केवल बची प्रतियाँ हटती हैं - खुला संग्रह काम करता रहता है।",
            "খোলা আর্কাইভ এন্ট্রির অস্থায়ী কপি কত জায়গা নেয়। পরিষ্কার করলে শুধু বাকি থাকা কপিগুলো মুছে যায় - খোলা আর্কাইভ কাজ চালিয়ে যায়।",
            "کھلے آرکائو انٹریوں کی عارضی کاپیاں کتنی جگہ لیتی ہیں۔ صاف کرنے سے صرف بچی کاپیاں ہٹتی ہیں - کھلا آرکائو کام کرتا رہتا ہے۔",
            "打开的归档条目的临时副本占用多少空间。清空只清除遗留的副本 - 打开的归档继续正常工作。")

        Add("Удалить временные копии записей архивов? Открытые архивы продолжат работать.",
            "Delete the temporary copies of archive entries? Open archives keep working.",
            "Видалити тимчасові копії записів архівів? Відкриті архіви продовжать працювати.",
            "Temporäre Kopien von Archiveinträgen löschen? Geöffnete Archive arbeiten weiter.",
            "Eliminare le copie temporanee delle voci degli archivi? Gli archivi aperti continuano a funzionare.",
            "¿Eliminar las copias temporales de las entradas de los archivos? Los archivos abiertos siguen funcionando.",
            "Supprimer les copies temporaires des entrées d'archives ? Les archives ouvertes continuent de fonctionner.",
            "Excluir as cópias temporárias das entradas dos arquivos? Arquivos abertos continuam funcionando.",
            "حذف النسخ المؤقتة لإدخالات الأرشيف؟ الأرشيف المفتوح يواصل العمل.",
            "संग्रह प्रविष्टियों की अस्थायी प्रतियाँ हटाएँ? खुले संग्रह काम करते रहेंगे।",
            "আর্কাইভ এন্ট্রির অস্থায়ী কপি মুছবেন? খোলা আর্কাইভ কাজ চালিয়ে যাবে।",
            "آرکائو انٹریوں کی عارضی کاپیاں حذف کریں؟ کھلے آرکائو کام کرتے رہیں گے۔",
            "删除归档条目的临时副本吗？打开的归档继续正常工作。")

        Add("Временные копии архивов удалены.",
            "Temporary archive copies deleted.", "Тимчасові копії архівів видалено.",
            "Temporäre Archivkopien gelöscht.", "Copie temporanee degli archivi eliminate.",
            "Copias temporales de archivos eliminadas.", "Copies temporaires des archives supprimées.",
            "Cópias temporárias de arquivos excluídas.", "تم حذف النسخ المؤقتة للأرشيف.",
            "संग्रह की अस्थायी प्रतियाँ हटा दी गईं।", "আর্কাইভের অস্থায়ী কপি মুছে ফেলা হয়েছে।",
            "آرکائو کی عارضی کاپیاں حذف ہو گئیں۔", "归档临时副本已删除。")

        Add("Сетевые ресурсы",
            "Network resources", "Мережеві ресурси", "Netzwerkressourcen", "Risorse di rete",
            "Recursos de red", "Ressources réseau", "Recursos de rede", "موارد الشبكة",
            "नेटवर्क संसाधन", "নেটওয়ার্ক রিসোর্স", "نیٹ ورک وسائل", "网络资源")

        Add("Сколько места занимают временные копии файлов сетевых ресурсов. Очистка не прерывает активные подключения - копия загрузится заново при обращении.",
            "How much room the temporary copies of network resource files take. Clearing interrupts no active connection - a copy is fetched again when needed.",
            "Скільки місця займають тимчасові копії файлів мережевих ресурсів. Очищення не перериває активні підключення - копія завантажиться знову при зверненні.",
            "Wie viel Raum die temporären Kopien von Netzwerkressourcen-Dateien belegen. Leeren unterbricht keine aktive Verbindung - eine Kopie wird bei Bedarf neu geladen.",
            "Quanto spazio occupano le copie temporanee dei file delle risorse di rete. Svuotare non interrompe le connessioni attive - la copia viene riscaricata quando serve.",
            "Cuánto espacio ocupan las copias temporales de los archivos de recursos de red. Vaciar no interrumpe las conexiones activas - la copia se descarga de nuevo cuando se necesita.",
            "Place occupée par les copies temporaires des fichiers des ressources réseau. Vider n'interrompt aucune connexion active - la copie est retéléchargée quand nécessaire.",
            "Quanto espaço ocupam as cópias temporárias dos arquivos de recursos de rede. Limpar não interrompe conexões ativas - a cópia é baixada de novo quando necessário.",
            "كم تشغل من المكان النسخ المؤقتة لملفات موارد الشبكة. المسح لا يقطع الاتصالات النشطة - تُحمَّل النسخة من جديد عند الحاجة.",
            "नेटवर्क संसाधन फ़ाइलों की अस्थायी प्रतियाँ कितनी जगह लेती हैं। साफ़ करने से सक्रिय कनेक्शन नहीं टूटते - ज़रूरत पर प्रति फिर से डाउनलोड हो जाती है।",
            "নেটওয়ার্ক রিসোর্স ফাইলের অস্থায়ী কপি কত জায়গা নেয়। পরিষ্কার করলে সক্রিয় সংযোগ ভাঙে না - দরকারে কপি আবার ডাউনলোড হয়।",
            "نیٹ ورک وسائل کی فائلوں کی عارضی کاپیاں کتنی جگہ لیتی ہیں۔ صاف کرنے سے فعال کنکشن منقطع نہیں ہوتے - ضرورت پر کاپی دوبارہ ڈاؤن لوڈ ہو جاتی ہے۔",
            "网络资源文件的临时副本占用多少空间。清空不会中断活动连接 - 需要时会重新下载副本。")

        Add("Удалить временные копии файлов сетевых ресурсов? Активные подключения не пострадают.",
            "Delete the temporary copies of network resource files? Active connections are not affected.",
            "Видалити тимчасові копії файлів мережевих ресурсів? Активні підключення не постраждають.",
            "Temporäre Kopien von Netzwerkressourcen-Dateien löschen? Aktive Verbindungen bleiben unberührt.",
            "Eliminare le copie temporanee dei file delle risorse di rete? Le connessioni attive non ne risentono.",
            "¿Eliminar las copias temporales de los archivos de recursos de red? Las conexiones activas no se ven afectadas.",
            "Supprimer les copies temporaires des fichiers des ressources réseau ? Les connexions actives ne sont pas affectées.",
            "Excluir as cópias temporárias dos arquivos de recursos de rede? Conexões ativas não são afetadas.",
            "حذف النسخ المؤقتة لملفات موارد الشبكة؟ الاتصالات النشطة لن تتأثر.",
            "नेटवर्क संसाधन फ़ाइलों की अस्थायी प्रतियाँ हटाएँ? सक्रिय कनेक्शन प्रभावित नहीं होंगे।",
            "নেটওয়ার্ক রিসোর্স ফাইলের অস্থায়ী কপি মুছবেন? সক্রিয় সংযোগ ক্ষতিগ্রস্ত হবে না।",
            "نیٹ ورک وسائل کی فائلوں کی عارضی کاپیاں حذف کریں؟ فعال کنکشن متاثر نہیں ہوں گے۔",
            "删除网络资源文件的临时副本吗？活动连接不受影响。")

        Add("Временные копии файлов сетевых ресурсов удалены.",
            "Temporary network resource copies deleted.", "Тимчасові копії файлів мережевих ресурсів видалено.",
            "Temporäre Kopien von Netzwerkressourcen gelöscht.", "Copie temporanee delle risorse di rete eliminate.",
            "Copias temporales de recursos de red eliminadas.", "Copies temporaires des ressources réseau supprimées.",
            "Cópias temporárias de recursos de rede excluídas.", "تم حذف النسخ المؤقتة لموارد الشبكة.",
            "नेटवर्क संसाधनों की अस्थायी प्रतियाँ हटा दी गईं।", "নেটওয়ার্ক রিসোর্সের অস্থায়ী কপি মুছে ফেলা হয়েছে।",
            "نیٹ ورک وسائل کی عارضی کاپیاں حذف ہو گئیں۔", "网络资源临时副本已删除。")

        ' --- video track languages (§6.3) -------------------------------------------

        Add("Не выбирать",
            "Do not choose", "Не вибирати", "Nicht wählen", "Non scegliere", "No elegir",
            "Ne pas choisir", "Não escolher", "بدون اختيار", "न चुनें", "নির্বাচন করবেন না",
            "منتخب نہ کریں", "不选择")

        Add("Всегда выключены",
            "Always off", "Завжди вимкнені", "Immer aus", "Sempre disattivati", "Siempre desactivados",
            "Toujours désactivés", "Sempre desligadas", "مُطفأة دائمًا", "हमेशा बंद",
            "সর্বদা বন্ধ", "ہمیشہ بند", "始终关闭")

        Add("{0} - язык системы",
            "{0} - system language", "{0} - мова системи", "{0} - Systemsprache",
            "{0} - lingua di sistema", "{0} - idioma del sistema", "{0} - langue du système",
            "{0} - idioma do sistema", "{0} - لغة النظام", "{0} - सिस्टम भाषा",
            "{0} - সিস্টেম ভাষা", "{0} - سسٹم زبان", "{0} - 系统语言")

        Add("Если такой дорожки нет, выбор остаётся за плеером.",
            "When there is no such track, the player keeps its own choice.",
            "Якщо такої доріжки немає, вибір лишається за програвачем.",
            "Fehlt eine solche Spur, bleibt die Wahl beim Player.",
            "Se la traccia non esiste, la scelta resta al player.",
            "Si no existe esa pista, la elección la mantiene el reproductor.",
            "S'il n'y a pas de telle piste, le lecteur garde son propre choix.",
            "Se essa faixa não existir, a escolha fica com o reprodutor.",
            "إذا لم يوجد مسار كهذا، يبقى الاختيار للمشغّل.",
            "यदि ऐसा ट्रैक न हो, तो चुनाव प्लेयर के पास रहता है।",
            "এমন ট্র্যাক না থাকলে নির্বাচন প্লেয়ারের হাতেই থাকে।",
            "اگر ایسا ٹریک نہ ہو تو انتخاب پلیئر کے پاس رہتا ہے۔",
            "如果没有这样的音轨，则由播放器自行选择。")

        Add("Запоминать позицию просмотра",
            "Remember the playback position", "Запам'ятовувати позицію перегляду",
            "Wiedergabeposition merken", "Ricorda la posizione di riproduzione",
            "Recordar la posición de reproducción", "Mémoriser la position de lecture",
            "Memorizar a posição de reprodução", "تذكّر موضع التشغيل",
            "प्लेबैक स्थिति याद रखें", "প্লেব্যাক অবস্থান মনে রাখুন",
            "پلے بیک مقام یاد رکھیں", "记住播放位置")

        Add("Изменившийся файл всегда начинается с начала.",
            "A file that has changed always starts from the beginning.",
            "Змінений файл завжди починається спочатку.",
            "Eine geänderte Datei beginnt immer von vorn.",
            "Un file che è cambiato riparte sempre dall'inizio.",
            "Un archivo que ha cambiado siempre empieza desde el principio.",
            "Un fichier qui a changé recommence toujours au début.",
            "Um ficheiro que mudou recomeça sempre do início.",
            "الملف الذي تغيّر يبدأ دائمًا من البداية.",
            "बदली हुई फ़ाइल हमेशा शुरुआत से चलती है।",
            "পরিবর্তিত ফাইল সবসময় শুরু থেকে চলে।",
            "تبدیل شدہ فائل ہمیشہ شروع سے چلتی ہے۔",
            "已更改的文件总是从头开始播放。")

        ' --- export and import (§7.4) -----------------------------------------------

        Add("Включить личные данные",
            "Include personal data", "Включити особисті дані", "Persönliche Daten einschließen",
            "Includi i dati personali", "Incluir datos personales", "Inclure les données personnelles",
            "Incluir dados pessoais", "تضمين البيانات الشخصية", "निजी डेटा शामिल करें",
            "ব্যক্তিগত তথ্য অন্তর্ভুক্ত করুন", "ذاتی ڈیٹا شامل کریں", "包含个人数据")

        Add("История папок и позиции просмотра попадут в файл. API-ключи и пароли - никогда.",
            "The folder history and playback positions go into the file. API keys and passwords never do.",
            "Історія тек і позиції перегляду потраплять у файл. API-ключі та паролі - ніколи.",
            "Ordnerverlauf und Wiedergabepositionen kommen in die Datei. API-Schlüssel und Passwörter nie.",
            "La cronologia delle cartelle e le posizioni di riproduzione finiscono nel file. Chiavi API e password mai.",
            "El historial de carpetas y las posiciones de reproducción van al archivo. Las claves API y contraseñas nunca.",
            "L'historique des dossiers et les positions de lecture vont dans le fichier. Jamais les clés d'API ni les mots de passe.",
            "O histórico de pastas e as posições de reprodução vão para o ficheiro. Chaves de API e palavras-passe nunca.",
            "يدخل سجل المجلدات ومواضع التشغيل في الملف. أما مفاتيح API وكلمات المرور فلا أبدًا.",
            "फ़ोल्डर इतिहास और प्लेबैक स्थिति फ़ाइल में जाती हैं। API कुंजियाँ और पासवर्ड कभी नहीं।",
            "ফোল্ডার ইতিহাস ও প্লেব্যাক অবস্থান ফাইলে যায়। API কী ও পাসওয়ার্ড কখনও নয়।",
            "فولڈر تاریخچہ اور پلے بیک مقامات فائل میں جاتے ہیں۔ API کیز اور پاس ورڈ کبھی نہیں۔",
            "文件夹历史和播放位置会写入文件。API 密钥和密码永远不会。")

        Add("Включает в файл историю папок и позиции просмотра видео. API-ключи и пароли не экспортируются никогда.",
            "Adds the folder history and video playback positions to the file. API keys and passwords are never exported.",
            "Додає у файл історію тек і позиції перегляду відео. API-ключі та паролі не експортуються ніколи.",
            "Nimmt Ordnerverlauf und Video-Wiedergabepositionen in die Datei auf. API-Schlüssel und Passwörter werden nie exportiert.",
            "Aggiunge al file la cronologia delle cartelle e le posizioni dei video. Chiavi API e password non vengono mai esportate.",
            "Añade al archivo el historial de carpetas y las posiciones de vídeo. Las claves API y contraseñas nunca se exportan.",
            "Ajoute au fichier l'historique des dossiers et les positions des vidéos. Les clés d'API et les mots de passe ne sont jamais exportés.",
            "Adiciona ao ficheiro o histórico de pastas e as posições dos vídeos. Chaves de API e palavras-passe nunca são exportadas.",
            "يضيف إلى الملف سجل المجلدات ومواضع تشغيل الفيديو. لا تُصدَّر مفاتيح API وكلمات المرور أبدًا.",
            "फ़ाइल में फ़ोल्डर इतिहास और वीडियो प्लेबैक स्थिति जोड़ता है। API कुंजियाँ और पासवर्ड कभी निर्यात नहीं होते।",
            "ফাইলে ফোল্ডার ইতিহাস ও ভিডিও প্লেব্যাক অবস্থান যোগ করে। API কী ও পাসওয়ার্ড কখনও রপ্তানি হয় না।",
            "فائل میں فولڈر تاریخچہ اور ویڈیو پلے بیک مقامات شامل کرتا ہے۔ API کیز اور پاس ورڈ کبھی برآمد نہیں ہوتے۔",
            "把文件夹历史和视频播放位置写入文件。API 密钥和密码从不导出。")

        Add("В файл попадут пути к вашим папкам и позиции просмотра. Продолжить?",
            "Your folder paths and playback positions will go into the file. Continue?",
            "У файл потраплять шляхи до ваших тек і позиції перегляду. Продовжити?",
            "Ihre Ordnerpfade und Wiedergabepositionen kommen in die Datei. Fortfahren?",
            "Nel file finiranno i percorsi delle tue cartelle e le posizioni di riproduzione. Continuare?",
            "En el archivo irán las rutas de sus carpetas y las posiciones de reproducción. ¿Continuar?",
            "Les chemins de vos dossiers et les positions de lecture iront dans le fichier. Continuer ?",
            "Os caminhos das suas pastas e as posições de reprodução irão para o ficheiro. Continuar?",
            "ستدخل مسارات مجلداتك ومواضع التشغيل في الملف. المتابعة؟",
            "आपके फ़ोल्डर पथ और प्लेबैक स्थिति फ़ाइल में जाएँगे। जारी रखें?",
            "আপনার ফোল্ডার পথ ও প্লেব্যাক অবস্থান ফাইলে যাবে। চালিয়ে যাবেন?",
            "آپ کے فولڈر راستے اور پلے بیک مقامات فائل میں جائیں گے۔ جاری رکھیں؟",
            "您的文件夹路径和播放位置将写入文件。继续吗？")

        Add("Файл прочитан. Отличий от текущих настроек нет.",
            "The file was read. Nothing differs from the current settings.",
            "Файл прочитано. Відмінностей від поточних налаштувань немає.",
            "Die Datei wurde gelesen. Es gibt keine Unterschiede zu den aktuellen Einstellungen.",
            "File letto. Non ci sono differenze rispetto alle impostazioni attuali.",
            "Se leyó el archivo. No hay diferencias con los ajustes actuales.",
            "Le fichier a été lu. Aucune différence avec les réglages actuels.",
            "O ficheiro foi lido. Não há diferenças em relação às configurações atuais.",
            "تمت قراءة الملف. لا توجد فروق عن الإعدادات الحالية.",
            "फ़ाइल पढ़ ली गई। मौजूदा सेटिंग्स से कोई अंतर नहीं।",
            "ফাইলটি পড়া হয়েছে। বর্তমান সেটিংসের সাথে কোনো পার্থক্য নেই।",
            "فائل پڑھ لی گئی۔ موجودہ ترتیبات سے کوئی فرق نہیں۔",
            "文件已读取。与当前设置没有差异。")

        Add("Файл прочитан. Изменится параметров: {0}.",
            "The file was read. Settings that will change: {0}.",
            "Файл прочитано. Зміниться параметрів: {0}.",
            "Die Datei wurde gelesen. Zu ändernde Einstellungen: {0}.",
            "File letto. Impostazioni che cambieranno: {0}.",
            "Se leyó el archivo. Ajustes que cambiarán: {0}.",
            "Le fichier a été lu. Réglages qui changeront : {0}.",
            "O ficheiro foi lido. Definições que vão mudar: {0}.",
            "تمت قراءة الملف. عدد الإعدادات التي ستتغيّر: {0}.",
            "फ़ाइल पढ़ ली गई। बदलने वाली सेटिंग्स: {0}.",
            "ফাইলটি পড়া হয়েছে। যেসব সেটিংস বদলাবে: {0}.",
            "فائل پڑھ لی گئی۔ تبدیل ہونے والی ترتیبات: {0}.",
            "文件已读取。将更改的设置项：{0}。")

        Add("Да - заменить настройки, Нет - объединить с текущими.",
            "Yes - replace the settings, No - merge them into the current ones.",
            "Так - замінити налаштування, Ні - об'єднати з поточними.",
            "Ja - Einstellungen ersetzen, Nein - mit den aktuellen zusammenführen.",
            "Sì - sostituisci le impostazioni, No - uniscile a quelle attuali.",
            "Sí - reemplazar los ajustes, No - combinarlos con los actuales.",
            "Oui - remplacer les réglages, Non - les fusionner avec les actuels.",
            "Sim - substituir as configurações, Não - combiná-las com as atuais.",
            "نعم - استبدال الإعدادات، لا - دمجها مع الحالية.",
            "हाँ - सेटिंग्स बदलें, नहीं - मौजूदा के साथ मिलाएँ।",
            "হ্যাঁ - সেটিংস প্রতিস্থাপন করুন, না - বর্তমানের সাথে মেলান।",
            "ہاں - ترتیبات بدلیں، نہیں - موجودہ کے ساتھ ملا دیں۔",
            "是 - 替换设置，否 - 与当前设置合并。")

        Add("Не удалось создать резервную копию: {0}",
            "Could not create the backup: {0}", "Не вдалося створити резервну копію: {0}",
            "Sicherung konnte nicht erstellt werden: {0}", "Impossibile creare il backup: {0}",
            "No se pudo crear la copia de seguridad: {0}", "Impossible de créer la sauvegarde : {0}",
            "Não foi possível criar a cópia de segurança: {0}", "تعذّر إنشاء النسخة الاحتياطية: {0}",
            "बैकअप नहीं बनाया जा सका: {0}", "ব্যাকআপ তৈরি করা যায়নি: {0}",
            "بیک اپ نہیں بنایا جا سکا: {0}", "无法创建备份：{0}")

        Add("Масштаб интерфейса и режим запуска применятся после перезапуска.",
            "The interface scale and the startup mode take effect after a restart.",
            "Масштаб інтерфейсу та режим запуску застосуються після перезапуску.",
            "Oberflächenskalierung und Startmodus wirken nach einem Neustart.",
            "La scala dell'interfaccia e la modalità di avvio si applicano dopo un riavvio.",
            "La escala de la interfaz y el modo de inicio se aplican tras reiniciar.",
            "L'échelle de l'interface et le mode de démarrage s'appliquent après un redémarrage.",
            "A escala da interface e o modo de arranque aplicam-se após reiniciar.",
            "يُطبَّق مقياس الواجهة ووضع البدء بعد إعادة التشغيل.",
            "इंटरफ़ेस स्केल और स्टार्टअप मोड पुनः आरंभ के बाद लागू होते हैं।",
            "ইন্টারফেস স্কেল ও শুরুর মোড পুনরায় চালুর পর কার্যকর হয়।",
            "انٹرفیس اسکیل اور اسٹارٹ اپ موڈ دوبارہ شروع کرنے پر لاگو ہوتے ہیں۔",
            "界面缩放和启动模式将在重启后生效。")

        Add("Показывать панель инструментов всегда",
            "Show toolbar always", "Показувати панель інструментів завжди", "Symbolleiste immer anzeigen", "Mostra sempre la barra degli strumenti",
            "Mostrar siempre la barra de herramientas", "Toujours afficher la barre d'outils", "Sempre mostrar a barra de ferramentas",
            "إظهار شريط الأدوات دائمًا", "हमेशा टूलबार दिखाएं", "সদা টুলবার দেখান", "ہمیشہ ٹولبار دکھائیں", "始终显示工具栏")

        Add("Если отмечено, панель инструментов всегда видна и не скрывается автоматически.",
            "If checked, the toolbar is always visible and never auto-hides.", "Якщо позначено, панель інструментів завжди видима і ніколи не приховується автоматично.", "Wenn aktiviert, ist die Symbolleiste immer sichtbar und wird nie automatisch ausgeblendet.",
            "Se selezionato, la barra degli strumenti è sempre visibile e non si nasconde automaticamente.",
            "Si está marcado, la barra de herramientas siempre está visible y nunca se oculta automáticamente.", "Si coché, la barre d'outils reste toujours visible et ne se masque jamais automatiquement.",
            "Se marcado, a barra de ferramentas sempre está visível e nunca desaparece automaticamente.",
            "عند التحديد، يبقى شريط الأدوات ظاهرًا دائمًا ولا يختفي تلقائيًا.", "चुनने पर टूलबार हमेशा दिखाई देता है और स्वतः छुपता नहीं है.",
            "নির্বাচিত হলে টুলবার সর্বদা দৃশ্যমান থাকে এবং স্বয়ংক্রিয়ভাবে লুকায় না.", "منتخب ہونے پر ٹولبار ہمیشہ نظر آتا ہے اور خود بخود چھپتا نہیں ہے.",
            "勾选后工具栏将始终可见，不会自动隐藏。")
    End Sub

End Class
