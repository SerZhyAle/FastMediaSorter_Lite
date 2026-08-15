Option Strict On

' <summary>
' Strings of file operations, folder scanning, file associations and the image
' panel window. See Localization.vb for the key convention.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddFileStrings()

        ' --- Main_Form.FileOperations.vb ---------------------------------------
        Add("в очереди: ", "in the queue: ", "у черзі: ", "in der Warteschlange: ", "in coda: ",
            "en la cola: ", "dans la file : ", "na fila: ", "في قائمة الانتظار: ", "कतार में: ",
            "সারিতে: ", "قطار میں: ", "队列中：")
        Add("Введите новое имя файла:", "Enter new file name:", "Введіть нове ім'я файлу:",
            "Neuen Dateinamen eingeben:", "Inserisci il nuovo nome del file:",
            "Introduzca el nuevo nombre del archivo:", "Saisissez le nouveau nom du fichier :",
            "Digite o novo nome do arquivo:", "أدخل اسم الملف الجديد:", "नया फ़ाइल नाम दर्ज करें:",
            "নতুন ফাইলের নাম লিখুন:", "نئی فائل کا نام درج کریں:", "输入新的文件名：")
        Add("Переименование файла", "Rename File", "Перейменування файлу", "Datei umbenennen",
            "Rinomina file", "Cambiar nombre de archivo", "Renommer le fichier", "Renomear arquivo",
            "إعادة تسمية الملف", "फ़ाइल का नाम बदलें", "ফাইলের নাম পরিবর্তন", "فائل کا نام تبدیل کریں",
            "重命名文件")
        Add("! Имя не изменено", "! Name not changed", "! Ім'я не змінено", "! Name nicht geändert",
            "! Nome non modificato", "! El nombre no ha cambiado", "! Nom inchangé",
            "! Nome não alterado", "! لم يتغير الاسم", "! नाम नहीं बदला", "! নাম বদলায়নি",
            "! نام تبدیل نہیں ہوا", "! 名称未更改")
        Add("! Файл с таким именем уже есть", "! A file with that name already exists",
            "! Файл із таким ім'ям уже є", "! Eine Datei dieses Namens existiert bereits",
            "! Esiste già un file con questo nome", "! Ya existe un archivo con ese nombre",
            "! Un fichier portant ce nom existe déjà", "! Já existe um arquivo com esse nome",
            "! يوجد ملف بهذا الاسم بالفعل", "! इस नाम की फ़ाइल पहले से मौजूद है",
            "! এই নামে একটি ফাইল আগে থেকেই আছে", "! اس نام کی فائل پہلے سے موجود ہے", "! 已存在同名文件")
        Add("! Ошибка переименования", "! Rename error", "! Помилка перейменування",
            "! Fehler beim Umbenennen", "! Errore di rinomina", "! Error al cambiar el nombre",
            "! Erreur de renommage", "! Erro ao renomear", "! خطأ في إعادة التسمية",
            "! नाम बदलने में त्रुटि", "! নাম পরিবর্তনে ত্রুটি", "! نام بدلنے میں خرابی", "! 重命名出错")
        Add("! Нет файла ", "! No file", "! Немає файлу", "! Keine Datei", "! Nessun file",
            "! No hay archivo", "! Aucun fichier", "! Nenhum arquivo", "! لا يوجد ملف",
            "! कोई फ़ाइल नहीं", "! কোনো ফাইল নেই", "! کوئی فائل نہیں", "! 没有文件")
        Add("! Файл уже в этой папке", "! File is already in that folder", "! Файл уже в цій теці",
            "! Die Datei liegt bereits in diesem Ordner", "! Il file è già in quella cartella",
            "! El archivo ya está en esa carpeta", "! Le fichier est déjà dans ce dossier",
            "! O arquivo já está nessa pasta", "! الملف موجود بالفعل في ذلك المجلد",
            "! फ़ाइल पहले से उस फ़ोल्डर में है", "! ফাইলটি ইতিমধ্যেই সেই ফোল্ডারে আছে",
            "! فائل پہلے ہی اس فولڈر میں ہے", "! 文件已在该文件夹中")
        Add("Файл с таким именем уже есть: операция пропущена", "A file with this name already exists: operation skipped",
            "Файл із таким ім'ям уже є: операцію пропущено", "Eine Datei dieses Namens existiert bereits: Vorgang übersprungen",
            "Esiste già un file con questo nome: operazione ignorata", "Ya existe un archivo con este nombre: operación omitida",
            "Un fichier de ce nom existe déjà : opération ignorée", "Já existe um arquivo com esse nome: operação ignorada",
            "يوجد ملف بهذا الاسم بالفعل: تم تخطي العملية", "इस नाम की फ़ाइल पहले से है: कार्रवाई छोड़ी गई",
            "এই নামে একটি ফাইল আগে থেকেই আছে: কাজটি এড়ানো হয়েছে", "اس نام کی فائل پہلے سے موجود ہے: کارروائی چھوڑ دی گئی",
            "已存在同名文件：操作已跳过")
        Add(" (существующий файл будет заменён)", " (the existing file will be replaced)",
            " (наявний файл буде замінено)", " (die vorhandene Datei wird ersetzt)",
            " (il file esistente verrà sostituito)", " (se reemplazará el archivo existente)",
            " (le fichier existant sera remplacé)", " (o arquivo existente será substituído)",
            " (سيتم استبدال الملف الموجود)", " (मौजूदा फ़ाइल बदली जाएगी)",
            " (বিদ্যমান ফাইলটি প্রতিস্থাপিত হবে)", " (موجودہ فائل بدل دی جائے گی)", "（将替换现有文件）")
        Add("В папке назначения уже есть файл {0}.\r\nДа — заменить; Нет — сохранить оба; Отмена — пропустить.",
            "The destination already contains {0}.\r\nYes — replace; No — keep both; Cancel — skip.",
            "У папці призначення вже є файл {0}.\r\nТак — замінити; Ні — зберегти обидва; Скасувати — пропустити.",
            "Im Zielordner gibt es bereits {0}.\r\nJa — ersetzen; Nein — beide behalten; Abbrechen — überspringen.",
            "La cartella di destinazione contiene già {0}.\r\nSì — sostituisci; No — conserva entrambi; Annulla — salta.",
            "La carpeta de destino ya contiene {0}.\r\nSí — reemplazar; No — conservar ambos; Cancelar — omitir.",
            "Le dossier de destination contient déjà {0}.\r\nOui — remplacer ; Non — conserver les deux ; Annuler — ignorer.",
            "A pasta de destino já contém {0}.\r\nSim — substituir; Não — manter ambos; Cancelar — ignorar.",
            "تحتوي وجهة النقل على {0}.\r\nنعم للاستبدال؛ لا للاحتفاظ بكليهما؛ إلغاء للتخطي.",
            "गंतव्य में {0} पहले से है।\r\nहाँ — बदलें; नहीं — दोनों रखें; रद्द — छोड़ें।",
            "গন্তব্যে {0} ইতিমধ্যে আছে।\r\nহ্যাঁ — প্রতিস্থাপন; না — দুটোই রাখুন; বাতিল — এড়িয়ে যান।",
            "منزل میں {0} پہلے سے موجود ہے۔\r\nہاں — بدلیں؛ نہیں — دونوں رکھیں؛ منسوخ — چھوڑیں۔",
            "目标位置已包含 {0}。\r\n是—替换；否—保留两者；取消—跳过。")
        Add("; файлов больше нет", "; no files remain", "; файлів більше немає", "; keine Dateien mehr",
            "; non rimangono file", "; no quedan archivos", "; il ne reste aucun fichier", "; não há mais arquivos",
            "; لا توجد ملفات متبقية", "; कोई फ़ाइल शेष नहीं", "; আর কোনো ফাইল নেই", "; مزید فائلیں باقی نہیں", "；没有更多文件")
        Add("Экспортировать настройки", "Export settings", "Експортувати налаштування", "Einstellungen exportieren",
            "Esporta impostazioni", "Exportar configuración", "Exporter les paramètres", "Exportar configurações",
            "تصدير الإعدادات", "सेटिंग्स निर्यात करें", "সেটিংস রপ্তানি করুন", "ترتیبات برآمد کریں", "导出设置")
        Add("Настройки экспортированы.", "Settings exported.", "Налаштування експортовано.", "Einstellungen exportiert.",
            "Impostazioni esportate.", "Configuración exportada.", "Paramètres exportés.", "Configurações exportadas.",
            "تم تصدير الإعدادات.", "सेटिंग्स निर्यात की गईं।", "সেটিংস রপ্তানি করা হয়েছে।", "ترتیبات برآمد ہو گئیں۔", "设置已导出。")
        Add("Не удалось экспортировать настройки: {0}", "Could not export settings: {0}", "Не вдалося експортувати налаштування: {0}", "Einstellungen konnten nicht exportiert werden: {0}",
            "Impossibile esportare le impostazioni: {0}", "No se pudo exportar la configuración: {0}", "Impossible d'exporter les paramètres : {0}", "Não foi possível exportar as configurações: {0}",
            "تعذر تصدير الإعدادات: {0}", "सेटिंग्स निर्यात नहीं की जा सकीं: {0}", "সেটিংস রপ্তানি করা যায়নি: {0}", "ترتیبات برآمد نہیں ہو سکیں: {0}", "无法导出设置：{0}")
        Add("Импортировать настройки", "Import settings", "Імпортувати налаштування", "Einstellungen importieren",
            "Importa impostazioni", "Importar configuración", "Importer les paramètres", "Importar configurações",
            "استيراد الإعدادات", "सेटिंग्स आयात करें", "সেটিংস আমদানি করুন", "ترتیبات درآمد کریں", "导入设置")
        Add("Настройки импортированы. Резервная копия: {0}", "Settings imported. Backup: {0}", "Налаштування імпортовано. Резервна копія: {0}", "Einstellungen importiert. Sicherung: {0}",
            "Impostazioni importate. Backup: {0}", "Configuración importada. Copia de seguridad: {0}", "Paramètres importés. Sauvegarde : {0}", "Configurações importadas. Backup: {0}",
            "تم استيراد الإعدادات. النسخة الاحتياطية: {0}", "सेटिंग्स आयात की गईं। बैकअप: {0}", "সেটিংস আমদানি করা হয়েছে। ব্যাকআপ: {0}", "ترتیبات درآمد ہو گئیں۔ بیک اپ: {0}", "设置已导入。备份：{0}")
        Add("Не удалось импортировать настройки: {0}", "Could not import settings: {0}", "Не вдалося імпортувати налаштування: {0}", "Einstellungen konnten nicht importiert werden: {0}",
            "Impossibile importare le impostazioni: {0}", "No se pudo importar la configuración: {0}", "Impossible d'importer les paramètres : {0}", "Não foi possível importar as configurações: {0}",
            "تعذر استيراد الإعدادات: {0}", "सेटिंग्स आयात नहीं की जा सकीं: {0}", "সেটিংস আমদানি করা যায়নি: {0}", "ترتیبات درآمد نہیں ہو سکیں: {0}", "无法导入设置：{0}")
        Add("! Нет истории о переносе", "! No history about moved files", "! Немає історії перенесень",
            "! Kein Verlauf verschobener Dateien", "! Nessuna cronologia dei file spostati",
            "! No hay historial de archivos movidos", "! Aucun historique de fichiers déplacés",
            "! Sem histórico de arquivos movidos", "! لا يوجد سجل للملفات المنقولة",
            "! स्थानांतरित फ़ाइलों का कोई इतिहास नहीं", "! সরানো ফাইলের কোনো ইতিহাস নেই",
            "! منتقل شدہ فائلوں کی کوئی تاریخ نہیں", "! 没有移动记录")
        ' What U says when the history HAS an entry and still cannot put the file back. The
        ' distinction from "no history" above is the whole point of recording a permanent
        ' deletion at all (R-1 §3.5, invariant 7).
        Add("! Файл был удалён безвозвратно по вашему выбору - возвращать нечего",
            "! The file was deleted permanently at your request - there is nothing to bring back",
            "! Файл було видалено безповоротно на ваш вибір - повертати нічого",
            "! Die Datei wurde auf Ihren Wunsch endgültig gelöscht - es gibt nichts zurückzuholen",
            "! Il file è stato eliminato definitivamente su tua richiesta - non c'è nulla da recuperare",
            "! El archivo se borró definitivamente a petición tuya - no hay nada que recuperar",
            "! Le fichier a été supprimé définitivement à votre demande - il n'y a rien à récupérer",
            "! O ficheiro foi eliminado definitivamente a seu pedido - não há nada para recuperar",
            "! حُذف الملف نهائيًا بناءً على طلبك - لا شيء لاستعادته",
            "! फ़ाइल आपके कहने पर स्थायी रूप से हटाई गई थी - लौटाने को कुछ नहीं है",
            "! ফাইলটি আপনার অনুরোধে স্থায়ীভাবে মোছা হয়েছিল - ফেরানোর কিছু নেই",
            "! فائل آپ کی درخواست پر مستقل طور پر حذف ہوئی تھی - واپس لانے کو کچھ نہیں",
            "! 该文件已按你的要求被永久删除，没有可恢复的内容")
        Add("! Файл был удалён безвозвратно: Корзины в том расположении нет - возвращать нечего",
            "! The file was deleted permanently: there is no Recycle Bin in that location - there is nothing to bring back",
            "! Файл було видалено безповоротно: Кошика в тому розташуванні немає - повертати нічого",
            "! Die Datei wurde endgültig gelöscht: an diesem Ort gibt es keinen Papierkorb - es gibt nichts zurückzuholen",
            "! Il file è stato eliminato definitivamente: in quella posizione non c'è il Cestino - non c'è nulla da recuperare",
            "! El archivo se borró definitivamente: en esa ubicación no hay Papelera - no hay nada que recuperar",
            "! Le fichier a été supprimé définitivement : il n'y a pas de Corbeille à cet emplacement - il n'y a rien à récupérer",
            "! O ficheiro foi eliminado definitivamente: nessa localização não há Reciclagem - não há nada para recuperar",
            "! حُذف الملف نهائيًا: لا توجد سلة محذوفات في ذلك الموقع - لا شيء لاستعادته",
            "! फ़ाइल स्थायी रूप से हटाई गई थी: उस स्थान पर रीसायकल बिन नहीं है - लौटाने को कुछ नहीं है",
            "! ফাইলটি স্থায়ীভাবে মোছা হয়েছিল: সেই অবস্থানে রিসাইকেল বিন নেই - ফেরানোর কিছু নেই",
            "! فائل مستقل طور پر حذف ہوئی تھی: اُس مقام پر ری سائیکل بن نہیں ہے - واپس لانے کو کچھ نہیں",
            "! 该文件已被永久删除：那个位置没有回收站，没有可恢复的内容")
        ' Undoing a recycled delete (R-1 Ф3). The refusals are named rather than lumped into
        ' one "could not restore": "the bin was emptied" is something the user can check, and
        ' "the folder is gone" is something only they can decide what to do about - we do not
        ' recreate a tree they deleted.
        Add("!Ждите. Файл возвращается из Корзины..",
            "!Wait. Restoring the file from the Recycle Bin..",
            "!Зачекайте. Файл повертається з Кошика..",
            "!Bitte warten. Die Datei wird aus dem Papierkorb wiederhergestellt..",
            "!Attendi. Ripristino del file dal Cestino..",
            "!Espere. Restaurando el archivo desde la Papelera..",
            "!Patientez. Restauration du fichier depuis la Corbeille..",
            "!Aguarde. A restaurar o ficheiro da Reciclagem..",
            "!انتظر. جارٍ استعادة الملف من سلة المحذوفات..",
            "!प्रतीक्षा करें. फ़ाइल रीसायकल बिन से लौटाई जा रही है..",
            "!অপেক্ষা করুন. ফাইলটি রিসাইকেল বিন থেকে ফেরানো হচ্ছে..",
            "!انتظار کریں. فائل ری سائیکل بن سے بحال ہو رہی ہے..",
            "!请稍候。正在从回收站还原文件..")
        Add("файл восстановлен из Корзины: {0}",
            "file restored from the Recycle Bin: {0}",
            "файл відновлено з Кошика: {0}",
            "Datei aus dem Papierkorb wiederhergestellt: {0}",
            "file ripristinato dal Cestino: {0}",
            "archivo restaurado desde la Papelera: {0}",
            "fichier restauré depuis la Corbeille : {0}",
            "ficheiro restaurado da Reciclagem: {0}",
            "استُعيد الملف من سلة المحذوفات: {0}",
            "फ़ाइल रीसायकल बिन से लौटाई गई: {0}",
            "ফাইল রিসাইকেল বিন থেকে ফেরানো হয়েছে: {0}",
            "فائل ری سائیکل بن سے بحال ہو گئی: {0}",
            "已从回收站还原文件：{0}")
        Add("файл восстановлен из Корзины под именем {0}",
            "file restored from the Recycle Bin as {0}",
            "файл відновлено з Кошика під іменем {0}",
            "Datei aus dem Papierkorb wiederhergestellt als {0}",
            "file ripristinato dal Cestino come {0}",
            "archivo restaurado desde la Papelera como {0}",
            "fichier restauré depuis la Corbeille sous le nom {0}",
            "ficheiro restaurado da Reciclagem como {0}",
            "استُعيد الملف من سلة المحذوفات باسم {0}",
            "फ़ाइल रीसायकल बिन से {0} नाम से लौटाई गई",
            "ফাইল রিসাইকেল বিন থেকে {0} নামে ফেরানো হয়েছে",
            "فائل ری سائیکل بن سے {0} نام سے بحال ہوئی",
            "已从回收站还原文件，名称为 {0}")
        Add("! Файла больше нет в Корзине - возможно, она очищена",
            "! The file is no longer in the Recycle Bin - it may have been emptied",
            "! Файла більше немає в Кошику - можливо, його очищено",
            "! Die Datei ist nicht mehr im Papierkorb - er wurde möglicherweise geleert",
            "! Il file non è più nel Cestino - potrebbe essere stato svuotato",
            "! El archivo ya no está en la Papelera - puede que se haya vaciado",
            "! Le fichier n'est plus dans la Corbeille - elle a peut-être été vidée",
            "! O ficheiro já não está na Reciclagem - pode ter sido esvaziada",
            "! لم يعد الملف في سلة المحذوفات - ربما أُفرغت",
            "! फ़ाइल अब रीसायकल बिन में नहीं है - शायद उसे खाली कर दिया गया",
            "! ফাইলটি আর রিসাইকেল বিনে নেই - সম্ভবত সেটি খালি করা হয়েছে",
            "! فائل اب ری سائیکل بن میں نہیں ہے - شاید اسے خالی کر دیا گیا",
            "! 文件已不在回收站中，回收站可能已被清空")
        Add("! Папка, из которой файл был удалён, больше не существует",
            "! The folder the file came from no longer exists",
            "! Папки, з якої файл було видалено, більше не існує",
            "! Der Ordner, aus dem die Datei stammt, existiert nicht mehr",
            "! La cartella da cui proviene il file non esiste più",
            "! La carpeta de la que procede el archivo ya no existe",
            "! Le dossier dont provient le fichier n'existe plus",
            "! A pasta de onde o ficheiro veio já não existe",
            "! لم يعد المجلد الذي حُذف منه الملف موجودًا",
            "! जिस फ़ोल्डर से फ़ाइल हटाई गई थी, वह अब मौजूद नहीं है",
            "! যে ফোল্ডার থেকে ফাইলটি মোছা হয়েছিল, সেটি আর নেই",
            "! جس فولڈر سے فائل حذف ہوئی تھی، وہ اب موجود نہیں",
            "! 该文件所在的文件夹已不存在")
        Add("!Ждите. Возвращается прежнее имя..",
            "!Wait. Restoring the previous name..",
            "!Зачекайте. Повертається попереднє ім'я..",
            "!Bitte warten. Der vorherige Name wird wiederhergestellt..",
            "!Attendi. Ripristino del nome precedente..",
            "!Espere. Restaurando el nombre anterior..",
            "!Patientez. Restauration du nom précédent..",
            "!Aguarde. A restaurar o nome anterior..",
            "!انتظر. جارٍ استعادة الاسم السابق..",
            "!प्रतीक्षा करें. पिछला नाम लौटाया जा रहा है..",
            "!অপেক্ষা করুন. আগের নাম ফেরানো হচ্ছে..",
            "!انتظار کریں. پچھلا نام بحال ہو رہا ہے..",
            "!请稍候。正在恢复原来的名称..")
        Add("имя возвращено: {0}",
            "name restored: {0}", "ім'я повернуто: {0}", "Name wiederhergestellt: {0}",
            "nome ripristinato: {0}", "nombre restaurado: {0}", "nom restauré : {0}",
            "nome restaurado: {0}", "استُعيد الاسم: {0}", "नाम वापस किया गया: {0}",
            "নাম ফেরানো হয়েছে: {0}", "نام بحال ہو گیا: {0}", "名称已恢复：{0}")
        Add("! Нет файла для переименования", "! No file to rename", "! Немає файлу для перейменування",
            "! Keine Datei zum Umbenennen", "! Nessun file da rinominare",
            "! No hay archivo para cambiar de nombre", "! Aucun fichier à renommer",
            "! Nenhum arquivo para renomear", "! لا يوجد ملف لإعادة تسميته",
            "! नाम बदलने के लिए कोई फ़ाइल नहीं", "! নাম বদলানোর মতো ফাইল নেই",
            "! نام بدلنے کے لیے کوئی فائل نہیں", "! 没有可重命名的文件")
        Add("Имя файла скопировано в буфер", "Filename sent to clipboard", "Шлях до файлу скопійовано",
            "Dateipfad in die Zwischenablage kopiert", "Percorso del file copiato negli appunti",
            "Ruta del archivo copiada al portapapeles", "Chemin du fichier copié dans le presse-papiers",
            "Caminho do arquivo copiado", "تم نسخ مسار الملف", "फ़ाइल पथ क्लिपबोर्ड पर कॉपी हुआ",
            "ফাইল পাথ ক্লিপবোর্ডে কপি হয়েছে", "فائل کا راستہ کلپ بورڈ پر نقل ہوا", "文件路径已复制到剪贴板")

        ' --- Main_Form.FileScanning.vb -------------------------------------------
        Add("Папка пустая", "Folder is empty", "Тека порожня", "Der Ordner ist leer",
            "La cartella è vuota", "La carpeta está vacía", "Le dossier est vide",
            "A pasta está vazia", "المجلد فارغ", "फ़ोल्डर खाली है", "ফোল্ডার খালি",
            "فولڈر خالی ہے", "文件夹为空")

        ' --- Main_Form.FileAssociation.vb ----------------------------------------
        Add("Ошибка ассоциации: ", "Failed to set association: ", "Помилка асоціації: ",
            "Verknüpfung fehlgeschlagen: ", "Impossibile impostare l'associazione: ",
            "Error al establecer la asociación: ", "Échec de l'association : ",
            "Falha ao definir a associação: ", "تعذّر ضبط الاقتران: ", "संबद्धता सेट करने में विफल: ",
            "সংযুক্তি সেট করা যায়নি: ", "ایسوسی ایشن سیٹ کرنے میں ناکامی: ", "关联设置失败：")
        Add("Ассоциировать .JPG файлы с этой программой?",
            "Associate .JPG files with this application?", "Асоціювати файли .JPG із цією програмою?",
            "JPG-Dateien mit dieser Anwendung verknüpfen?",
            "Associare i file .JPG a questa applicazione?", "¿Asociar los archivos .JPG con esta aplicación?",
            "Associer les fichiers .JPG à cette application ?", "Associar arquivos .JPG a este aplicativo?",
            "هل تريد اقتران ملفات ‎.JPG بهذا التطبيق؟", "क्या .JPG फ़ाइलें इस ऐप से संबद्ध करें?",
            "কি .JPG ফাইল এই অ্যাপের সাথে যুক্ত করবেন?", "کیا ‎.JPG فائلیں اس ایپ سے منسلک کریں؟",
            "将 .JPG 文件关联到本程序？")
        Add("Ассоциации установлены. Возможно потребуется перезапустить Проводник или Windows.",
            "Associations set. You may need to restart Explorer or Windows for changes to take effect.",
            "Асоціації встановлено. Можливо, знадобиться перезапустити Провідник або Windows.",
            "Verknüpfungen gesetzt. Möglicherweise müssen Sie Explorer oder Windows neu starten.",
            "Associazioni impostate. Potrebbe essere necessario riavviare Esplora risorse o Windows.",
            "Asociaciones establecidas. Puede que deba reiniciar el Explorador o Windows.",
            "Associations définies. Il peut être nécessaire de redémarrer l'Explorateur ou Windows.",
            "Associações definidas. Pode ser necessário reiniciar o Explorer ou o Windows.",
            "تم ضبط الاقترانات. قد تحتاج إلى إعادة تشغيل مستكشف الملفات أو Windows.",
            "संबद्धताएँ सेट हो गईं। बदलाव के लिए Explorer या Windows पुनः आरंभ करना पड़ सकता है।",
            "সংযুক্তি সেট হয়েছে। পরিবর্তন কার্যকর করতে Explorer বা Windows পুনরায় চালু করতে হতে পারে।",
            "ایسوسی ایشنز سیٹ ہو گئیں۔ تبدیلی کے لیے Explorer یا Windows دوبارہ شروع کرنا پڑ سکتا ہے۔",
            "关联已设置。可能需要重启资源管理器或 Windows 才能生效。")
        Add("Регистрация завершена", "Registration complete", "Реєстрацію завершено",
            "Registrierung abgeschlossen", "Registrazione completata", "Registro completado",
            "Enregistrement terminé", "Registro concluído", "اكتمل التسجيل", "पंजीकरण पूरा हुआ",
            "নিবন্ধন সম্পন্ন", "رجسٹریشن مکمل", "注册完成")
        Add("Регистрация", "Registration", "Реєстрація", "Registrierung", "Registrazione",
            "Registro", "Enregistrement", "Registro", "التسجيل", "पंजीकरण", "নিবন্ধন", "رجسٹریشن", "注册")

        ' --- Image_Panel_Form.vb ---------------------------------------------------
        Add("Панель изображений", "Image Panel", "Панель зображень", "Bildleiste", "Pannello immagini",
            "Panel de imágenes", "Panneau d'images", "Painel de imagens", "لوحة الصور",
            "छवि पैनल", "ছবির প্যানেল", "تصویری پینل", "图片面板")
        AddC("panel", "Подтверждение удаления",
            "Deletion..", "Видалення..", "Löschen..", "Eliminazione..", "Eliminación..",
            "Suppression..", "Exclusão..", "الحذف..", "हटाना..", "মোছা..", "حذف..", "删除..")
        Add("Не удалось удалить файл: ", "Fail to delete file:", "Не вдалося видалити файл: ",
            "Datei konnte nicht gelöscht werden: ", "Impossibile eliminare il file: ",
            "No se pudo eliminar el archivo: ", "Impossible de supprimer le fichier : ",
            "Falha ao excluir o arquivo: ", "تعذّر حذف الملف: ", "फ़ाइल हटाई नहीं जा सकी: ",
            "ফাইল মোছা যায়নি: ", "فائل حذف نہ ہو سکی: ", "无法删除文件：")
        Add("Ошибка", "Error", "Помилка", "Fehler", "Errore", "Error", "Erreur", "Erro",
            "خطأ", "त्रुटि", "ত্রুটি", "خرابی", "错误")
        Add("Внимание", "Warning", "Увага", "Achtung", "Attenzione", "Aviso", "Attention",
            "Atenção", "تنبيه", "चेतावनी", "সতর্কতা", "انتباہ", "警告")
        Add("копировать", "copy", "копіювати", "kopieren", "copiare", "copiar", "copier",
            "copiar", "نسخ", "कॉपी", "কপি", "نقل", "复制")
        Add("переместить", "move", "перемістити", "verschieben", "spostare", "mover", "déplacer",
            "mover", "نقل", "स्थानांतरित", "সরান", "منتقل", "移动")
        Add("Подтверждение", "Confirm", "Підтвердження", "Bestätigen", "Conferma", "Confirmar",
            "Confirmer", "Confirmar", "تأكيد", "पुष्टि", "নিশ্চিত করুন", "تصدیق", "确认")
        Add("Ошибки:", "Errors:", "Помилки:", "Fehler:", "Errori:", "Errores:", "Erreurs :",
            "Erros:", "الأخطاء:", "त्रुटियाँ:", "ত্রুটি:", "خرابیاں:", "错误：")
        Add("Операция завершена", "Operation Complete", "Операцію завершено", "Vorgang abgeschlossen",
            "Operazione completata", "Operación completada", "Opération terminée",
            "Operação concluída", "اكتملت العملية", "कार्रवाई पूरी हुई", "কাজ সম্পন্ন",
            "عمل مکمل", "操作完成")
    End Sub

End Class
